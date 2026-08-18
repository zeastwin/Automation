# -*- coding: utf-8 -*-
"""
Invoke-PiRpcSessionTest.py — Pi RPC 链路端到端会话测试（无头 harness）

用途：
    复刻 EW-AI 当前 Pi 链路的子进程装配，做一次端到端会话测试：
    1. 以 `pi --mode rpc --no-skills --skill <skill目录>... [--provider X] [--model Y]`
       启动子进程，stdin/stdout 走 JSONL（命令 prompt/abort/new_session/set_model/get_state，
       事件 message_update(text_delta/thinking_delta)、tool_execution_start/end、
       turn_end、compaction_start/end 等）。
    2. 仅对当前子进程注入环境变量：PI_CODING_AGENT_DIR 指向集成上下文目录
       （其中 APPEND_SYSTEM.md 即 Assets/Pi/automation-cli.md 的部署副本），
       AUTOMATION_TOOLCLI_PATH / AUTOMATION_TOOL_PROFILE（可选 AUTOMATION_TOOL_FULL_PERMISSION）。
    3. 模型经 Pi 内置 bash 工具调用 Automation.ToolCli.exe；脚本监听
       tool_execution_end 事件，从 result.content[].text 中解析预演 JSON
       （判定 data.previewId 存在且 confirmed=false），随后经 Bridge 命名管道
       （AutomationBridgePipe，4 字节小端长度前缀 + UTF-8 JSON，
       信封 {"requestId","method","path","bodyJson"}）调用
       POST /bridge/previews/confirm 自动确认预演，替代前台确认窗。
    4. 每轮 agent_settled 后按名称核对目标流程与变量，输出 goal state；
       未达成时发送“继续”，最多 --rounds 轮。

前置条件：
    - pi.exe 已部署（默认 D:/AutomationTools/Pi/pi.exe，可用 --pi-exe 覆盖）。
    - 平台编辑器已启动（Bridge 命名管道 AutomationBridgePipe 在监听）。
    - Automation.ToolCli.exe 已构建（用 --toolcli 指定路径）。
    - Skill 目录已就绪（默认 %APPDATA%/Automation/Pi/skills 下的
      automation-tools-cli 与 automation-process-authoring，可用 --skill 重复指定；
      路径参数一律使用正斜杠）。

用法示例：
    python Scripts/Invoke-PiRpcSessionTest.py --log Logs/pi_session_test.jsonl --rounds 3
"""

import argparse
import json
import os
import queue
import struct
import subprocess
import sys
import threading
import time
import uuid

DEFAULT_PROMPT = (
    "创建一个循环流程“标准测试_1到100相加”：从 1 到 100 累加，"
    "结果写入变量“标准测试_累加结果”。"
)

BRIDGE_PIPE_NAME = r"\\.\pipe\AutomationBridgePipe"
CONFIRM_PATH = "/bridge/previews/confirm"


def log_line(log_file, record):
    record["ts"] = time.strftime("%Y-%m-%dT%H:%M:%S")
    text = json.dumps(record, ensure_ascii=False)
    print(text)
    if log_file:
        log_file.write(text + "\n")
        log_file.flush()


def bridge_call(path, body, timeout=10.0):
    """经 Bridge 命名管道发送一次请求，返回 (status_code, body_json_text)。"""
    request = json.dumps({
        "requestId": uuid.uuid4().hex,
        "method": "POST",
        "path": path,
        "bodyJson": json.dumps(body, ensure_ascii=False),
    }, ensure_ascii=False).encode("utf-8")
    deadline = time.time() + timeout
    last_error = None
    while time.time() < deadline:
        try:
            with open(BRIDGE_PIPE_NAME, "r+b", buffering=0) as pipe:
                pipe.write(struct.pack("<i", len(request)))
                pipe.write(request)
                raw_len = _read_exactly(pipe, 4)
                (resp_len,) = struct.unpack("<i", raw_len)
                response = json.loads(_read_exactly(pipe, resp_len).decode("utf-8"))
                return response.get("statusCode", 0), response.get("bodyJson", "")
        except (OSError, json.JSONDecodeError) as exc:
            last_error = exc
            time.sleep(0.5)
    raise RuntimeError(f"Bridge 管道调用失败：{path}; error={last_error}")


def _read_exactly(pipe, length):
    data = b""
    while len(data) < length:
        chunk = pipe.read(length - len(data))
        if not chunk:
            raise EOFError("Bridge 连接在读取响应时提前关闭。")
        data += chunk
    return data


def extract_preview_ids(text):
    """从工具返回文本中提取需要前台确认的 previewId 列表（data.previewId + confirmed=false）。"""
    found = []
    for candidate in _iter_json_objects(text):
        data = candidate.get("data") if isinstance(candidate, dict) else None
        if not isinstance(data, dict):
            continue
        preview_id = data.get("previewId")
        if preview_id and data.get("confirmed") is False:
            found.append(preview_id)
    return found


def _iter_json_objects(text):
    """先按整体 JSON 解析；失败时从后向前扫描内嵌 JSON 对象。"""
    if not text:
        return
    try:
        yield json.loads(text)
        return
    except (ValueError, TypeError):
        pass
    depth = 0
    end = None
    for i in range(len(text) - 1, -1, -1):
        ch = text[i]
        if ch == "}":
            if depth == 0:
                end = i
            depth += 1
        elif ch == "{":
            depth -= 1
            if depth == 0 and end is not None:
                try:
                    yield json.loads(text[i:end + 1])
                except ValueError:
                    pass
                end = None


def tool_end_texts(event):
    """从 tool_execution_end 事件中取出 result.content[].text 全部文本。"""
    result = event.get("result")
    if not isinstance(result, dict):
        return []
    content = result.get("content")
    texts = []
    if isinstance(content, list):
        for item in content:
            if isinstance(item, dict) and isinstance(item.get("text"), str):
                texts.append(item["text"])
    elif isinstance(result.get("text"), str):
        texts.append(result["text"])
    return texts


def stdout_reader(proc, event_queue):
    """按 \\n 严格切分 JSONL（剥除行尾 \\r），解析失败时按原文记录。"""
    for raw in iter(proc.stdout.readline, b""):
        line = raw.rstrip(b"\n").rstrip(b"\r")
        if not line:
            continue
        try:
            event_queue.put(json.loads(line.decode("utf-8")))
        except (ValueError, UnicodeDecodeError):
            event_queue.put({"type": "raw", "text": line.decode("utf-8", "replace")})
    event_queue.put(None)


def send_command(proc, command):
    proc.stdin.write((json.dumps(command, ensure_ascii=False) + "\n").encode("utf-8"))
    proc.stdin.flush()


def check_goal(toolcli_path, profile, proc_name, variable_name, log_file):
    """经 ToolCli 按名称核对目标流程与变量，输出 goal state。"""
    env = os.environ.copy()
    env["AUTOMATION_TOOL_PROFILE"] = profile
    goal = {"processCreated": False, "variableCreated": False}

    def call(name, args):
        result = subprocess.run(
            [toolcli_path, "cli", "call", name, "--json", json.dumps(args, ensure_ascii=False)],
            capture_output=True, text=True, encoding="utf-8", env=env, timeout=60)
        if result.returncode != 0:
            return None
        try:
            return json.loads(result.stdout)
        except ValueError:
            return None

    procs = call("list_procs", {})
    if procs:
        for item in _walk_strings(procs):
            if proc_name in item:
                goal["processCreated"] = True
                break
    variables = call("list_variables", {"limit": 100})
    if variables:
        for item in _walk_strings(variables):
            if variable_name in item:
                goal["variableCreated"] = True
                break
    log_line(log_file, {"type": "goal_state", **goal})
    return goal


def _walk_strings(node):
    if isinstance(node, str):
        yield node
    elif isinstance(node, dict):
        for value in node.values():
            yield from _walk_strings(value)
    elif isinstance(node, list):
        for value in node:
            yield from _walk_strings(value)


def main():
    parser = argparse.ArgumentParser(description="Pi RPC 链路端到端会话测试（无头 harness）")
    parser.add_argument("--pi-exe", default="D:/AutomationTools/Pi/pi.exe", help="pi.exe 路径")
    parser.add_argument("--toolcli", required=True, help="Automation.ToolCli.exe 路径")
    parser.add_argument("--profile", default="Editor", help="工具 Profile（默认 Editor）")
    parser.add_argument("--full-permission", action="store_true", help="注入完全权限（仅 Editor 生效）")
    parser.add_argument("--provider", default=None, help="LLM provider 名")
    parser.add_argument("--model", default=None, help="模型 pattern 或 ID")
    parser.add_argument("--agent-dir",
                        default=os.path.join(os.environ.get("APPDATA", ""), "Automation/Pi/agent"),
                        help="PI_CODING_AGENT_DIR 指向的集成上下文目录")
    parser.add_argument("--skill", action="append", default=None,
                        help="显式加载的 Skill 目录，可重复；缺省加载 agent-dir 同级 skills 下的两个平台 Skill")
    parser.add_argument("--prompt", default=DEFAULT_PROMPT, help="首轮用户目标")
    parser.add_argument("--proc-name", default="标准测试_1到100相加", help="goal 核对的流程名")
    parser.add_argument("--variable-name", default="标准测试_累加结果", help="goal 核对的变量名")
    parser.add_argument("--rounds", type=int, default=3, help="目标未达成时最多“继续”轮数")
    parser.add_argument("--log", default=None, help="JSONL 事件日志输出路径")
    parser.add_argument("--turn-timeout", type=float, default=900.0, help="单轮最长等待秒数")
    args = parser.parse_args()

    pi_exe = os.path.abspath(args.pi_exe).replace("\\", "/")
    toolcli = os.path.abspath(args.toolcli).replace("\\", "/")
    agent_dir = os.path.abspath(args.agent_dir).replace("\\", "/")
    skill_dirs = args.skill
    if not skill_dirs:
        skills_root = os.path.join(os.path.dirname(agent_dir), "skills")
        skill_dirs = [
            f"{skills_root}/automation-tools-cli",
            f"{skills_root}/automation-process-authoring",
        ]
    skill_dirs = [os.path.abspath(p).replace("\\", "/") for p in skill_dirs]

    log_file = open(args.log, "a", encoding="utf-8") if args.log else None

    command = [pi_exe, "--mode", "rpc", "--no-skills"]
    for skill_dir in skill_dirs:
        command += ["--skill", skill_dir]
    if args.provider:
        command += ["--provider", args.provider]
    if args.model:
        command += ["--model", args.model]

    env = os.environ.copy()
    env["PI_CODING_AGENT_DIR"] = agent_dir
    env["AUTOMATION_TOOLCLI_PATH"] = toolcli
    env["AUTOMATION_TOOL_PROFILE"] = args.profile
    if args.full_permission:
        env["AUTOMATION_TOOL_FULL_PERMISSION"] = "1"

    log_line(log_file, {"type": "harness.start", "command": command, "agentDir": agent_dir})

    proc = subprocess.Popen(
        command,
        stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
        env=env, cwd=os.path.dirname(toolcli) or None)

    event_queue = queue.Queue()
    reader = threading.Thread(target=stdout_reader, args=(proc, event_queue), daemon=True)
    reader.start()

    confirmed_previews = set()
    rounds_left = args.rounds
    prompt_sent = False
    exit_code = 1
    turn_deadline = None

    try:
        while True:
            if not prompt_sent:
                send_command(proc, {"id": "req-prompt-1", "type": "prompt", "message": args.prompt})
                prompt_sent = True
                turn_deadline = time.time() + args.turn_timeout
            try:
                event = event_queue.get(timeout=1.0)
            except queue.Empty:
                if proc.poll() is not None:
                    log_line(log_file, {"type": "harness.process_exit", "code": proc.returncode})
                    break
                if turn_deadline and time.time() > turn_deadline:
                    log_line(log_file, {"type": "harness.turn_timeout"})
                    send_command(proc, {"type": "abort"})
                    turn_deadline = None
                continue
            if event is None:
                log_line(log_file, {"type": "harness.stdout_closed", "code": proc.poll()})
                break

            event_type = event.get("type")
            if event_type == "response":
                log_line(log_file, {"type": "rpc.response", "command": event.get("command"),
                                    "success": event.get("success")})
            elif event_type == "message_update":
                delta = (event.get("assistantMessageEvent") or {})
                if delta.get("type") in ("text_delta", "thinking_delta"):
                    sys.stdout.write(delta.get("delta", ""))
                    sys.stdout.flush()
            elif event_type == "tool_execution_start":
                log_line(log_file, {"type": "tool.started", "tool": event.get("toolName"),
                                    "args": event.get("args")})
            elif event_type == "tool_execution_end":
                texts = tool_end_texts(event)
                log_line(log_file, {"type": "tool.finished", "tool": event.get("toolName"),
                                    "isError": (event.get("result") or {}).get("isError")})
                for text in texts:
                    for preview_id in extract_preview_ids(text):
                        if preview_id in confirmed_previews:
                            continue
                        status, body = bridge_call(CONFIRM_PATH, {"previewId": preview_id})
                        confirmed_previews.add(preview_id)
                        log_line(log_file, {"type": "preview.confirmed", "previewId": preview_id,
                                            "bridgeStatus": status, "bridgeBody": body})
            elif event_type == "turn_end":
                log_line(log_file, {"type": "turn.end"})
            elif event_type in ("compaction_start", "compaction_end"):
                log_line(log_file, {"type": event_type})
            elif event_type == "agent_settled":
                turn_deadline = None
                goal = check_goal(toolcli, args.profile, args.proc_name, args.variable_name, log_file)
                if goal["processCreated"] and goal["variableCreated"]:
                    log_line(log_file, {"type": "harness.goal_reached"})
                    exit_code = 0
                    break
                if rounds_left > 0:
                    rounds_left -= 1
                    log_line(log_file, {"type": "harness.continue", "roundsLeft": rounds_left})
                    send_command(proc, {"type": "prompt", "message": "继续"})
                    turn_deadline = time.time() + args.turn_timeout
                else:
                    log_line(log_file, {"type": "harness.goal_not_reached"})
                    break
    finally:
        try:
            state_id = uuid.uuid4().hex
            send_command(proc, {"id": state_id, "type": "get_state"})
            time.sleep(1.0)
        except (OSError, ValueError):
            pass
        proc.terminate()
        if log_file:
            log_file.close()
    return exit_code


if __name__ == "__main__":
    sys.exit(main())
