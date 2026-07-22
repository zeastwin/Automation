using Automation.Protocol;
using Markdig;
using Microsoft.Web.WebView2.WinForms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Automation
{
    public sealed partial class FrmAiAssistant
    {
        // 聊天卡片统一消费图快照；旧流程读取和提交结果也先转为同一快照，避免重复推断线性结构。
        private void CaptureFlowVisualizationEvent(GooseAcpEvent item)
        {
            JObject update = item?.Raw?["params"]?["update"] as JObject
                ?? item?.Raw?["params"] as JObject;
            string callId = update?["toolCallId"]?.Value<string>();
            if (string.Equals(item?.Kind, "tool_call", StringComparison.Ordinal))
            {
                JObject toolCall = update?["_meta"]?["goose"]?["toolCall"] as JObject;
                string extensionName = toolCall?["extensionName"]?.Value<string>();
                string toolName = toolCall?["toolName"]?.Value<string>();
                int toolPrefixSeparator = toolName?.LastIndexOf("__", StringComparison.Ordinal) ?? -1;
                if (toolPrefixSeparator >= 0)
                {
                    toolName = toolName.Substring(toolPrefixSeparator + 2);
                }
                bool capturesFlow = string.Equals(extensionName, "automation", StringComparison.OrdinalIgnoreCase)
                    && (string.Equals(toolName, "get_proc_overview", StringComparison.Ordinal)
                        || string.Equals(toolName, "get_proc_detail", StringComparison.Ordinal)
                        || string.Equals(toolName, "get_flow_graph", StringComparison.Ordinal)
                        || string.Equals(toolName, "apply_change_set", StringComparison.Ordinal));
                if (string.IsNullOrWhiteSpace(callId) || !capturesFlow)
                {
                    return;
                }
                flowVisualizationCallIds.Add(callId);
                return;
            }
            if (!string.Equals(item?.Kind, "tool_result", StringComparison.Ordinal))
            {
                return;
            }
            if (string.IsNullOrWhiteSpace(callId) || !flowVisualizationCallIds.Remove(callId))
            {
                return;
            }

            try
            {
                string resultText = GooseAcpEventReader.ExtractToolResultText(item.Raw);
                JObject result = string.IsNullOrWhiteSpace(resultText) ? null : JObject.Parse(resultText);
                if (result?["ok"]?.Value<bool>() != true || result["data"] is not JObject data)
                {
                    return;
                }

                string resultType = result["type"]?.Value<string>();
                if (string.Equals(resultType, "proc.flow_graph", StringComparison.Ordinal))
                {
                    flowVisualizationProcesses.RemoveAll();
                    foreach (JObject process in BuildFlowVisualizationFromGraph(data))
                    {
                        UpsertFlowVisualizationProcess(process);
                    }
                    return;
                }
                if (string.Equals(resultType, "proc.overview", StringComparison.Ordinal)
                    || string.Equals(resultType, "proc.detail", StringComparison.Ordinal))
                {
                    int procIndex = data["procIndex"]?.Value<int?>() ?? -1;
                    JObject process = BuildUnifiedFlowVisualization(procIndex);
                    if (process != null) UpsertFlowVisualizationProcess(process);
                    return;
                }

                if (!string.Equals(resultType, "change_set.apply", StringComparison.Ordinal)
                    || data["committed"]?.Value<bool>() != true)
                {
                    return;
                }

                flowVisualizationProcesses.RemoveAll();
                foreach (JObject affected in (data["affectedProcesses"] as JArray ?? new JArray())
                    .OfType<JObject>())
                {
                    int procIndex = affected["procIndex"]?.Value<int?>() ?? -1;
                    JObject process = BuildUnifiedFlowVisualization(procIndex);
                    if (process != null)
                    {
                        UpsertFlowVisualizationProcess(process);
                    }
                }
            }
            catch (Exception ex)
            {
                // 只有 Automation 的流程工具进入这里；其返回形状异常需要记录，但不得中断工具主链路。
                Workspace.Runtime.ProcessEngine?.Logger?.Log($"AI流程可视化解析失败:{ex}", LogLevel.Error);
            }
        }

        private JObject BuildUnifiedFlowVisualization(int procIndex)
        {
            if (Workspace.Proc == null || procIndex < 0 || procIndex >= Workspace.Proc.procsList.Count)
            {
                return null;
            }
            ProcessFlowGraphSnapshot graph = ProcessFlowGraphService.BuildProcess(
                Workspace.Proc.procsList,
                procIndex,
                index => Workspace.Runtime.ProcessEngine?.GetSnapshot(index));
            JObject data = ProcessFlowGraphService.ToJObject(graph);
            JObject process = BuildFlowVisualizationFromGraph(data).FirstOrDefault();
            if (process != null) process["action"] = "committed";
            return process;
        }

        private void UpsertFlowVisualizationProcess(JObject process)
        {
            if (process == null)
            {
                return;
            }
            int? procIndex = process["procIndex"]?.Value<int?>();
            string processName = process["name"]?.Value<string>();
            for (int i = 0; i < flowVisualizationProcesses.Count; i++)
            {
                if (flowVisualizationProcesses[i] is not JObject existing)
                {
                    continue;
                }
                bool sameIndex = procIndex.HasValue && existing["procIndex"]?.Value<int?>() == procIndex;
                bool sameInspectedName = string.Equals(process["action"]?.Value<string>(), "inspect", StringComparison.Ordinal)
                    && string.Equals(existing["action"]?.Value<string>(), "inspect", StringComparison.Ordinal)
                    && string.Equals(existing["name"]?.Value<string>(), processName, StringComparison.Ordinal);
                if (sameIndex || sameInspectedName)
                {
                    flowVisualizationProcesses[i] = process.DeepClone();
                    return;
                }
            }
            flowVisualizationProcesses.Add(process.DeepClone());
        }

        private static IEnumerable<JObject> BuildFlowVisualizationFromGraph(JObject data)
        {
            if (data?["graphAvailable"]?.Value<bool?>() == false)
            {
                yield break;
            }
            string scope = data?["scope"]?.Value<string>();
            JArray nodes = data?["nodes"] as JArray ?? new JArray();
            JArray edges = data?["edges"] as JArray ?? new JArray();
            if (string.Equals(scope, "project", StringComparison.Ordinal))
            {
                var names = nodes.OfType<JObject>().ToDictionary(
                    node => node["id"]?.Value<string>() ?? Guid.NewGuid().ToString("N"),
                    node => node["label"]?.Value<string>() ?? "未知节点",
                    StringComparer.Ordinal);
                var relations = new JArray();
                foreach (JObject edge in edges.OfType<JObject>())
                {
                    string sourceId = edge["sourceId"]?.Value<string>();
                    string targetId = edge["targetId"]?.Value<string>();
                    relations.Add(new JObject
                    {
                        ["kind"] = "flow.relation",
                        ["name"] = edge["label"]?.Value<string>() ?? edge["kind"]?.Value<string>() ?? "关系",
                        ["summary"] = $"{ReadName(names, sourceId)} → {ReadName(names, targetId)}"
                    });
                }
                yield return new JObject
                {
                    ["action"] = "inspect",
                    ["name"] = data["name"]?.Value<string>() ?? "项目流程总览",
                    ["hasLoop"] = edges.OfType<JObject>().Any(edge => edge["loop"]?.Value<bool>() == true),
                    ["steps"] = new JArray(new JObject
                    {
                        ["name"] = "跨流程关系",
                        ["operations"] = relations
                    })
                };
                yield break;
            }

            var process = new JObject
            {
                ["action"] = "inspect",
                ["procIndex"] = data?["procIndex"]?.DeepClone(),
                ["name"] = data?["name"]?.Value<string>() ?? "未命名流程",
                ["hasLoop"] = edges.OfType<JObject>().Any(edge => edge["loop"]?.Value<bool>() == true)
            };
            var visualSteps = new JArray();
            var labels = nodes.OfType<JObject>().ToDictionary(
                node => node["id"]?.Value<string>() ?? Guid.NewGuid().ToString("N"),
                node => node["label"]?.Value<string>() ?? "未知节点",
                StringComparer.Ordinal);
            foreach (JObject group in (data?["groups"] as JArray ?? new JArray()).OfType<JObject>()
                .OrderBy(item => item["stepIndex"]?.Value<int?>() ?? int.MaxValue))
            {
                string groupId = group["id"]?.Value<string>();
                int stepIndex = group["stepIndex"]?.Value<int?>() ?? visualSteps.Count;
                var visualOperations = new JArray();
                foreach (JObject node in nodes.OfType<JObject>()
                    .Where(item => string.Equals(item["groupId"]?.Value<string>(), groupId, StringComparison.Ordinal))
                    .OrderBy(item => item["opIndex"]?.Value<int?>() ?? int.MaxValue))
                {
                    string nodeId = node["id"]?.Value<string>();
                    string branches = string.Join("；", edges.OfType<JObject>()
                        .Where(edge => string.Equals(edge["sourceId"]?.Value<string>(), nodeId, StringComparison.Ordinal)
                            && !string.Equals(edge["kind"]?.Value<string>(), "sequence", StringComparison.Ordinal))
                        .Select(edge => $"{edge["label"]?.Value<string>() ?? edge["kind"]?.Value<string>()}→{ReadName(labels, edge["targetId"]?.Value<string>())}"));
                    string summary = node["summary"]?.Value<string>() ?? node["operaType"]?.Value<string>() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(branches)) summary += "｜" + branches;
                    visualOperations.Add(new JObject
                    {
                        ["kind"] = "platform.operation",
                        ["name"] = node["label"]?.Value<string>() ?? string.Empty,
                        ["operaType"] = node["operaType"]?.Value<string>() ?? string.Empty,
                        ["summary"] = summary
                    });
                }
                visualSteps.Add(new JObject
                {
                    ["key"] = "步骤" + stepIndex,
                    ["name"] = group["label"]?.Value<string>() ?? "未命名步骤",
                    ["operations"] = visualOperations
                });
            }
            process["steps"] = visualSteps;
            yield return process;
        }

        private static string ReadName(IReadOnlyDictionary<string, string> names, string nodeId)
        {
            return !string.IsNullOrWhiteSpace(nodeId) && names.TryGetValue(nodeId, out string name) ? name : "未知节点";
        }


        // 工具调用/结果紧凑单行显示；连续相同项合并计数，避免重复占用纵向空间。
        private void AppendToolEntry(string marker, string text, JObject raw)
        {
            if (webViewConversation == null || webViewConversation.IsDisposed)
            {
                return;
            }
            bool isCall = string.Equals(marker, "call", StringComparison.Ordinal);
            string normalizedText = string.IsNullOrWhiteSpace(text) ? "无摘要" : LocalizeAutomationToolText(text.Trim());
            bool isSystemError = !isCall && (normalizedText.StartsWith("×", StringComparison.Ordinal)
                || string.Equals(
                    raw?["params"]?["update"]?["status"]?.Value<string>(),
                    "failed",
                    StringComparison.OrdinalIgnoreCase));
            bool isBusinessFailure = false;
            string resultLabel = isSystemError ? "异常" : "结果";
            if (!isCall)
            {
                try
                {
                    JObject result = JObject.Parse(
                        GooseAcpEventReader.ExtractToolResultText(raw) ?? string.Empty);
                    bool failed = result["ok"]?.Value<bool?>() == false;
                    if (failed)
                    {
                        string code = result["errorCode"]?.Value<string>() ?? "工具调用失败";
                        string detail = result["recovery"]?["validationError"]?.Value<string>()
                            ?? result["message"]?.Value<string>();
                        normalizedText = string.IsNullOrWhiteSpace(detail)
                            ? code
                            : code + " · " + detail;
                        isSystemError = IsSystemToolFailure(code);
                        isBusinessFailure = !isSystemError;
                        resultLabel = isSystemError ? "异常" : ResolveBusinessFailureLabel(code);
                    }
                    else
                    {
                        // MCP/Bridge 已正常返回结果；不能沿用 ACP 摘要中的“×”误报系统异常。
                        isSystemError = false;
                        isBusinessFailure = false;
                        resultLabel = "结果";
                    }
                }
                catch (JsonReaderException)
                {
                }
            }
            string cls = isCall
                ? "tool-call"
                : isSystemError
                    ? "tool-error"
                    : isBusinessFailure ? "tool-business-failure" : "tool-result";
            string display = isCall ? "调用" : resultLabel;
            string callId = raw?["params"]?["update"]?["toolCallId"]?.Value<string>()
                ?? raw?["params"]?["toolCallId"]?.Value<string>()
                ?? string.Empty;
            string html = "<div class=\"tool-entry " + cls + "\" title=\"" + HtmlEncode(normalizedText) + "\">"
                + "<span class=\"tool-entry-label\">" + display + "</span>"
                + "<span class=\"tool-entry-text\">" + HtmlEncode(normalizedText) + "</span>"
                + "<span class=\"tool-entry-count\" style=\"display:none\"></span></div>";
            string boxId = EnsureThinkingBox();
            string htmlJson = JsonConvert.SerializeObject(html);
            string signatureJson = JsonConvert.SerializeObject(marker + "\n" + normalizedText);
            string callIdJson = JsonConvert.SerializeObject(callId);
            string js = "var box=document.getElementById('" + boxId + "');if(box){var callId=" + callIdJson + ";var sig=" + signatureJson
                + ";var paired=null;if(callId){var entries=box.querySelectorAll('.tool-entry');for(var i=entries.length-1;i>=0;i--){if(entries[i].dataset.callId===callId){paired=entries[i];break;}}}"
                + "if(paired&&" + (isCall ? "false" : "true") + "){paired.classList.remove('tool-call');paired.classList.add('" + cls + "');paired.title=paired.title+' | '+"
                + JsonConvert.SerializeObject(normalizedText) + ";var label=paired.querySelector('.tool-entry-label');if(label){label.textContent='" + (isSystemError || isBusinessFailure ? resultLabel : "完成") + "';}var value=paired.querySelector('.tool-entry-text');if(value){value.textContent=value.textContent+'  →  '+"
                + JsonConvert.SerializeObject(normalizedText) + ";}}else{var entries2=box.querySelectorAll('.tool-entry');var last=entries2.length?entries2[entries2.length-1]:null;if(last&&last.dataset.signature===sig){var count=parseInt(last.dataset.count||'1',10)+1;last.dataset.count=count;var badge=last.querySelector('.tool-entry-count');if(badge){badge.textContent='×'+count;badge.style.display='inline-block';}}else{box.insertAdjacentHTML('beforeend',"
                + htmlJson + ");var added=box.lastElementChild;if(added){added.dataset.signature=sig;added.dataset.count='1';added.dataset.callId=callId;}}}scrollThinkingBoxToBottom('" + boxId + "');}";
            EnqueueScript(js);
        }

        private static bool IsSystemToolFailure(string errorCode)
        {
            if (string.IsNullOrWhiteSpace(errorCode))
            {
                return false;
            }
            return errorCode.StartsWith("MCP_", StringComparison.OrdinalIgnoreCase)
                || errorCode.StartsWith("TRANSPORT_", StringComparison.OrdinalIgnoreCase)
                || errorCode.StartsWith("INTERNAL_", StringComparison.OrdinalIgnoreCase)
                || string.Equals(errorCode, "UNHANDLED_EXCEPTION", StringComparison.OrdinalIgnoreCase)
                || string.Equals(errorCode, "TOOL_INVOCATION_FAILED", StringComparison.OrdinalIgnoreCase)
                || string.Equals(errorCode, "INVALID_RESPONSE", StringComparison.OrdinalIgnoreCase)
                || string.Equals(errorCode, "EMPTY_RESPONSE", StringComparison.OrdinalIgnoreCase)
                || string.Equals(errorCode, "BRIDGE_ERROR", StringComparison.OrdinalIgnoreCase)
                || string.Equals(errorCode, "REQUEST_ERROR", StringComparison.OrdinalIgnoreCase)
                || string.Equals(errorCode, "TIMEOUT", StringComparison.OrdinalIgnoreCase)
                || string.Equals(errorCode, "BRIDGE_NOT_READY", StringComparison.OrdinalIgnoreCase)
                || string.Equals(errorCode, "BRIDGE_STOPPING", StringComparison.OrdinalIgnoreCase)
                || string.Equals(errorCode, "RUNTIME_NOT_READY", StringComparison.OrdinalIgnoreCase)
                || string.Equals(errorCode, "STORE_UNAVAILABLE", StringComparison.OrdinalIgnoreCase)
                || string.Equals(errorCode, "PROCS_UNAVAILABLE", StringComparison.OrdinalIgnoreCase)
                || string.Equals(errorCode, "SAVE_FAILED", StringComparison.OrdinalIgnoreCase)
                || string.Equals(errorCode, "COMMIT_FAILED", StringComparison.OrdinalIgnoreCase)
                || string.Equals(errorCode, "CHANGE_SET_COMMIT_FAILED", StringComparison.OrdinalIgnoreCase)
                || string.Equals(errorCode, "CHANGE_SET_ROLLBACK_FAILED", StringComparison.OrdinalIgnoreCase)
                || string.Equals(errorCode, "VARIABLE_COMMIT_FAILED", StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveBusinessFailureLabel(string errorCode)
        {
            if (!string.IsNullOrWhiteSpace(errorCode)
                && (errorCode.EndsWith("_NOT_FOUND", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(errorCode, "NOT_FOUND", StringComparison.OrdinalIgnoreCase)))
            {
                return "未找到";
            }
            if (!string.IsNullOrWhiteSpace(errorCode)
                && (errorCode.IndexOf("INVALID", StringComparison.OrdinalIgnoreCase) >= 0
                    || errorCode.IndexOf("VALIDATE", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return "未通过";
            }
            return "未完成";
        }

        private static string LocalizeAutomationToolText(string value)
        {
            return (value ?? string.Empty)
                .Replace("get_native_operation_schemas", "获取原生指令结构")
                .Replace("get_semantic_operation_schema", "获取语义指令结构")
                .Replace("preview_change_set", "预演变更阶段")
                .Replace("apply_change_set", "提交配置阶段")
                .Replace("get_flow_graph", "获取流程图")
                .Replace("discard_change_set_preview", "结束变更预演")
                .Replace("get_proc_overview", "获取流程概览")
                .Replace("get_proc_detail", "获取流程详情")
                .Replace("list_procs", "列出流程")
                .Replace("get_variable_by_name", "按名称获取变量")
                .Replace("get_variable_by_index", "按索引获取变量")
                .Replace("set_variable_by_name", "按名称设置变量")
                .Replace("set_variable_by_index", "按索引设置变量")
                .Replace("list_variables", "读取变量表")
                .Replace("add_variable", "新增变量")
                .Replace("update_variable", "修改变量")
                .Replace("delete_variable", "删除变量")
                .Replace("get_variable", "获取变量")
                .Replace("search_variables", "搜索变量")
                .Replace("search_data_struct_items", "搜索数据结构项")
                .Replace("get_data_struct", "获取数据结构")
                .Replace("search_alarms", "搜索报警配置")
                .Replace("get_alarm", "获取报警配置")
                .Replace("get_snapshot", "获取运行快照")
                .Replace("run_proc_test", "限时测试流程")
                .Replace("wait_for_proc_state", "等待流程状态")
                .Replace("start_proc", "启动流程")
                .Replace("stop_proc", "停止流程");
        }

        private static string BuildAutomationFlowCardsHtml(string visualizationJson)
        {
            if (string.IsNullOrWhiteSpace(visualizationJson))
            {
                return string.Empty;
            }
            try
            {
                JArray processes = JArray.Parse(visualizationJson);
                if (processes.Count == 0)
                {
                    return string.Empty;
                }

                var html = new StringBuilder();
                html.Append("<div class=\"automation-flow-visual\"><div class=\"flow-visual-title\"><span>流程结构</span><span class=\"flow-badge\">")
                    .Append(processes.Count).Append(" 个流程</span></div>");
                foreach (JObject process in processes.OfType<JObject>())
                {
                    AppendProcessFlowHtml(html, process);
                }
                html.Append("</div>");
                return html.ToString();
            }
            catch (JsonException)
            {
                return string.Empty;
            }
        }

        private static void AppendProcessFlowHtml(StringBuilder html, JObject process)
        {
            string processName = process["name"]?.Value<string>() ?? "未命名流程";
            string action = process["action"]?.Value<string>();
            JArray steps = process["steps"] as JArray ?? new JArray();
            int operationCount = steps.OfType<JObject>()
                .Sum(step => (step["operations"] as JArray)?.Count ?? 0);
            bool hasBackEdge = process["hasLoop"]?.Value<bool>() == true || HasBackEdge(steps);
            string actionText;
            switch (action)
            {
                case "replace": actionText = "替换"; break;
                case "inspect": actionText = "现有"; break;
                case "committed": actionText = "已提交"; break;
                default: actionText = "新建"; break;
            }

            html.Append("<div class=\"flow-process\"><div class=\"flow-process-head\"><span class=\"flow-process-name\">")
                .Append(HtmlEncode(processName)).Append("</span><span class=\"flow-badge\">")
                .Append(actionText)
                .Append("</span><span class=\"flow-badge\">").Append(steps.Count).Append(" 步骤 · ")
                .Append(operationCount).Append(" 指令</span>");
            if (hasBackEdge)
            {
                html.Append("<span class=\"flow-badge loop\">含回环</span>");
            }
            html.Append("</div>");
            if (steps.Count == 0)
            {
                html.Append("<div class=\"flow-empty\">没有步骤</div></div>");
                return;
            }

            html.Append(steps.Count == 1
                ? "<div class=\"flow-track single-step\">"
                : "<div class=\"flow-track\">");
            int stepIndex = 0;
            foreach (JObject step in steps.OfType<JObject>())
            {
                if (stepIndex > 0)
                {
                    html.Append("<div class=\"flow-arrow\">→</div>");
                }
                AppendStepFlowHtml(html, step, stepIndex++);
            }
            html.Append("</div></div>");
        }

        private static void AppendStepFlowHtml(StringBuilder html, JObject step, int stepIndex)
        {
            string stepName = step["name"]?.Value<string>() ?? "未命名步骤";
            string stepKey = step["key"]?.Value<string>() ?? string.Empty;
            JArray operations = step["operations"] as JArray ?? new JArray();
            html.Append("<div class=\"flow-step\"><div class=\"flow-step-head\"><span class=\"flow-step-index\">")
                .Append(stepIndex + 1).Append("</span><span class=\"flow-step-name\">")
                .Append(HtmlEncode(stepName)).Append("</span>");
            if (!string.IsNullOrWhiteSpace(stepKey))
            {
                html.Append("<span class=\"flow-step-key\">").Append(HtmlEncode(stepKey)).Append("</span>");
            }
            html.Append("</div><div class=\"flow-ops\">");
            if (operations.Count == 0)
            {
                html.Append("<div class=\"flow-empty\">没有指令</div>");
            }
            int operationIndex = 0;
            foreach (JObject operation in operations.OfType<JObject>())
            {
                AppendOperationFlowHtml(html, operation, operationIndex++);
            }
            html.Append("</div></div>");
        }

        private static void AppendOperationFlowHtml(StringBuilder html, JObject operation, int operationIndex)
        {
            string kind = operation["kind"]?.Value<string>()
                ?? (operation["operaType"] == null ? "unknown" : "platform.operation");
            string name = operation["name"]?.Value<string>();
            string summary = BuildOperationSummary(operation, kind);
            html.Append("<div class=\"flow-op\" title=\"").Append(HtmlEncode(kind))
                .Append("\"><div class=\"flow-op-line\"><span class=\"flow-op-index\">")
                .Append(operationIndex + 1).Append("</span><span class=\"flow-op-text\">");
            if (!string.IsNullOrWhiteSpace(name) && !string.Equals(name, summary, StringComparison.Ordinal))
            {
                html.Append(HtmlEncode(name)).Append(" · ");
            }
            html.Append(HtmlEncode(summary)).Append("</span></div>");

            if (operation["whenTrue"] is JObject || operation["whenFalse"] is JObject)
            {
                AppendFlowPath(html, "满足", operation["whenTrue"] as JObject, false);
                AppendFlowPath(html, "不满足", operation["whenFalse"] as JObject, true);
            }
            else if (string.Equals(kind, "flow.goto", StringComparison.Ordinal))
            {
                AppendFlowPath(html, "跳转", operation["target"] as JObject, true);
            }
            html.Append("</div>");
        }

        private static string BuildOperationSummary(JObject operation, string kind)
        {
            string explicitSummary = operation["summary"]?.Value<string>();
            if (!string.IsNullOrWhiteSpace(explicitSummary))
            {
                return explicitSummary;
            }
            switch (kind)
            {
                case "wait":
                    return $"等待 {operation["milliseconds"]?.ToString(Formatting.None) ?? "?"} ms";
                case "variable.add":
                    return $"{operation["variable"]?.Value<string>() ?? "变量"} + {operation["amount"]?.ToString(Formatting.None) ?? "?"}";
                case "variable.set":
                    return $"{operation["variable"]?.Value<string>() ?? "变量"} = {operation["value"]?.Value<string>() ?? ""}";
                case "branch.number_range":
                    string left = operation["includeBounds"]?.Value<bool>() == false ? "(" : "[";
                    string right = operation["includeBounds"]?.Value<bool>() == false ? ")" : "]";
                    return $"判断 {operation["variable"]?.Value<string>() ?? "变量"} ∈ {left}{operation["min"]?.ToString(Formatting.None) ?? "?"}, {operation["max"]?.ToString(Formatting.None) ?? "?"}{right}";
                case "popup.message":
                    return $"显示固定文本“{operation["message"]?.Value<string>() ?? ""}”{BuildAutoCloseSuffix(operation)}";
                case "popup.variable":
                    return $"显示变量“{operation["variable"]?.Value<string>() ?? ""}”当前值{BuildAutoCloseSuffix(operation)}";
                case "flow.goto":
                    return "跳转到指定位置";
                case "flow.end":
                    return "正常结束当前流程";
                case "io.write":
                    JArray outputs = operation["outputs"] as JArray;
                    return outputs == null
                        ? "同步设置IO输出"
                        : $"同步设置 {outputs.Count} 个IO输出：{string.Join("、", outputs.OfType<JObject>().Select(item => item["io"]?.Value<string>()).Where(name => !string.IsNullOrWhiteSpace(name)))}";
                case "io.wait":
                    return $"等待 {(operation["conditions"] as JArray)?.Count ?? 0} 个IO条件成立";
                case "process.control":
                    return $"{operation["action"]?.Value<string>() ?? "控制"}流程 {operation["process"]?.Value<string>() ?? ""}";
                case "process.wait":
                    return $"等待流程 {operation["process"]?.Value<string>() ?? ""} 到达 {operation["expectedState"]?.Value<string>() ?? "目标状态"}";
                default:
                    return operation["operaType"]?.Value<string>()
                        ?? (string.IsNullOrWhiteSpace(operation["name"]?.Value<string>())
                            ? kind : operation["name"]?.Value<string>());
            }
        }

        private static string BuildAutoCloseSuffix(JObject operation)
        {
            int? autoCloseMs = operation["autoCloseMs"]?.Value<int?>();
            return autoCloseMs > 0 ? $" · {autoCloseMs} ms后关闭" : string.Empty;
        }

        private static void AppendFlowPath(StringBuilder html, string label, JObject target, bool alternative)
        {
            if (target == null)
            {
                return;
            }
            string step = target["step"]?.Value<string>() ?? "?";
            int operation = target["operation"]?.Value<int?>() ?? 0;
            html.Append("<div class=\"flow-paths\"><span class=\"flow-path")
                .Append(alternative ? " false" : string.Empty).Append("\">")
                .Append(HtmlEncode(label)).Append(" → ").Append(HtmlEncode(step))
                .Append(" · ").Append(operation + 1).Append("</span></div>");
        }

        private static bool HasBackEdge(JArray steps)
        {
            var indexes = steps.OfType<JObject>()
                .Select((step, index) => new { Key = step["key"]?.Value<string>(), Index = index })
                .Where(item => !string.IsNullOrWhiteSpace(item.Key))
                .ToDictionary(item => item.Key, item => item.Index, StringComparer.Ordinal);
            int currentIndex = 0;
            foreach (JObject step in steps.OfType<JObject>())
            {
                foreach (JObject operation in (step["operations"] as JArray ?? new JArray()).OfType<JObject>())
                {
                    foreach (string property in new[] { "target", "whenTrue", "whenFalse" })
                    {
                        string targetStep = operation[property]?["step"]?.Value<string>();
                        if (!string.IsNullOrWhiteSpace(targetStep)
                            && indexes.TryGetValue(targetStep, out int targetIndex)
                            && targetIndex <= currentIndex)
                        {
                            return true;
                        }
                    }
                }
                currentIndex++;
            }
            return false;
        }


        // 确保思维链窗口存在（首次调用时创建），返回窗口 ID。
        private string EnsureThinkingBox()
        {
            if (currentThinkingBoxId != null)
            {
                return currentThinkingBoxId;
            }
            thinkingBoxIndex++;
            currentThinkingBoxId = "thinking-box-" + thinkingBoxIndex;
            string boxId = currentThinkingBoxId;
            string html = "<div class=\"thinking-box\" id=\"" + boxId + "\">"
                + "<div class=\"toggle-bar\" onclick=\"toggleThinkingBox('" + boxId + "')\">思考过程（推理与工具，点击折叠/展开）</div>"
                + "</div>";
            EnqueueAppendHtml(html);
            return boxId;
        }

        // 将 HTML 追加到当前思维链窗口内部，并自动滚动到底部。
        private void AppendToThinkingBox(string html)
        {
            string boxId = EnsureThinkingBox();
            string htmlJson = JsonConvert.SerializeObject(html);
            string js = "var box=document.getElementById('" + boxId + "');if(box){box.insertAdjacentHTML('beforeend'," + htmlJson + ");scrollThinkingBoxToBottom('" + boxId + "');}";
            EnqueueScript(js);
        }

        // 折叠当前思维链窗口（PromptAsync 结束后调用）。
        private void CollapseThinkingBox()
        {
            if (currentThinkingBoxId == null)
            {
                return;
            }
            TimeSpan elapsed = promptStartedAt == default(DateTime) ? TimeSpan.Zero : DateTime.Now - promptStartedAt;
            string elapsedText = elapsed.TotalMinutes >= 1
                ? $"{(int)elapsed.TotalMinutes}分{elapsed.Seconds}秒"
                : $"{Math.Max(1, (int)Math.Ceiling(elapsed.TotalSeconds))}秒";
            string js = "var box=document.getElementById('" + currentThinkingBoxId + "');if(box){box.style.maxHeight='32px';box.scrollTop=0;box.classList.add('collapsed');var bar=box.querySelector('.toggle-bar');if(bar){bar.textContent='思考完成 · 已处理 "
                + elapsedText + "';}}";
            EnqueueScript(js);
            currentThinkingBoxId = null;
        }

        // ACP 的 agent_message_chunk 未提前区分分析段和最终段，生成期间先放在稳定的思考窗口；
        // 后续紧接工具调用时原地保留，轮次结束时才把最后一段转为最终回答。
        private void AppendStreamingText(string text)
        {
            if (webViewConversation == null || webViewConversation.IsDisposed || string.IsNullOrEmpty(text))
            {
                return;
            }
            if (!streamingAssistant)
            {
                streamingSegmentIndex++;
                streamingDivId = "streaming-" + streamingSegmentIndex;
                streamingMarkdown.Clear();
                streamingMarkdown.Append(text);
                streamingAssistant = true;
                lastStreamRender = DateTime.Now;
                AppendToThinkingBox("<div class=\"analysis-segment streaming-segment\" id=\""
                    + streamingDivId + "\">" + StreamingTextToHtml(streamingMarkdown.ToString()) + "</div>");
            }
            else
            {
                streamingMarkdown.Append(text);
                // 上一帧尚未完成时跳过中间帧，最终帧仍由 FinishStreaming 提交，
                // 避免长回答反复全量渲染 Markdown 造成 WebView2 脚本队列积压。
                if ((DateTime.Now - lastStreamRender).TotalMilliseconds >= 100
                    && pendingScriptTask.IsCompleted)
                {
                    lastStreamRender = DateTime.Now;
                    UpdateStreamingPreview();
                }
            }
        }

        // 结束正式助手流式段：用最终 Markdown 替换对话区中的临时 HTML。
        private void FinishStreaming()
        {
            if (!streamingAssistant)
            {
                return;
            }
            string renderedHtml = MarkdownToHtml(streamingMarkdown.ToString());
            string htmlJson = JsonConvert.SerializeObject(renderedHtml);
            string idJson = JsonConvert.SerializeObject(streamingDivId);
            string js = "var el=document.getElementById(" + idJson + ");if(el){el.innerHTML=" + htmlJson + ";}if(window.scrollMessagesToBottom){scrollMessagesToBottom();}";
            EnqueueScript(js);
            latestAssistantSegmentText = streamingMarkdown.ToString();
            latestAssistantSegmentDivId = streamingDivId;
            streamingAssistant = false;
            streamingDivId = null;
        }

        private void DemoteLatestAssistantSegmentToThinking()
        {
            if (string.IsNullOrWhiteSpace(latestAssistantSegmentText))
            {
                return;
            }
            latestAssistantSegmentText = null;
            latestAssistantSegmentDivId = null;
        }

        private string PromoteLatestAssistantSegment(string fallbackText, string visualizationJson)
        {
            string finalText = string.IsNullOrWhiteSpace(latestAssistantSegmentText)
                ? fallbackText
                : latestAssistantSegmentText;
            if (!string.IsNullOrWhiteSpace(latestAssistantSegmentDivId))
            {
                string time = DateTime.Now.ToString("HH:mm:ss");
                string finalHtml = BuildAutomationFlowCardsHtml(visualizationJson) + MarkdownToHtml(finalText);
                string messageHtml = "<div class=\"msg assistant\"><div class=\"msg-head\">"
                    + "<img class=\"avatar avatar-image\" src=\"" + ChickAvatarDataUri
                    + "\" alt=\"AI\" title=\"EW-AI " + HtmlEncode(time) + "\">"
                    + "<span class=\"msg-time\">" + HtmlEncode(time) + "</span>"
                    + CopyButtonHtml + "</div><div class=\"content\">" + finalHtml + "</div></div>";
                EnqueueScript("var el=document.getElementById("
                    + JsonConvert.SerializeObject(latestAssistantSegmentDivId)
                    + ");var box=el&&el.closest('.thinking-box');if(el){el.remove();}"
                    + "var messages=document.getElementById('messages'),finalMessage=null;if(messages){messages.insertAdjacentHTML('beforeend',"
                    + JsonConvert.SerializeObject(messageHtml) + ");finalMessage=messages.lastElementChild;}"
                    + "if(box&&box.querySelectorAll(':scope > :not(.toggle-bar)').length===0){box.remove();}"
                    + "if(window.revealFinalAnswer){revealFinalAnswer(finalMessage);}"
                    + "if(window.scrollMessagesToBottom){scrollMessagesToBottom();}");
            }
            else if (!string.IsNullOrWhiteSpace(finalText))
            {
                AppendConversation("EW-AI", finalText, UiPalette.SuccessHover, null, visualizationJson);
                EnqueueScript("var messages=document.getElementById('messages');if(window.revealFinalAnswer&&messages){revealFinalAnswer(messages.lastElementChild);}");
            }
            latestAssistantSegmentText = null;
            latestAssistantSegmentDivId = null;
            return finalText;
        }

        // 协议显式标记的推理片段仅显示在思维窗口，不混入最终回答。
        private void AppendThoughtText(string text)
        {
            if (webViewConversation == null || webViewConversation.IsDisposed || string.IsNullOrEmpty(text))
            {
                return;
            }

            if (!streamingThought)
            {
                streamingThoughtSegmentIndex++;
                streamingThoughtDivId = "thinking-streaming-" + streamingThoughtSegmentIndex;
                streamingThoughtMarkdown.Clear();
                streamingThoughtMarkdown.Append(text);
                streamingThought = true;
                lastThoughtRender = DateTime.Now;
                AppendToThinkingBox("<div class=\"streaming-segment\" id=\"" + streamingThoughtDivId + "\">"
                    + StreamingTextToHtml(streamingThoughtMarkdown.ToString()) + "</div>");
                return;
            }

            streamingThoughtMarkdown.Append(text);
            if ((DateTime.Now - lastThoughtRender).TotalMilliseconds >= 100
                && pendingScriptTask.IsCompleted)
            {
                lastThoughtRender = DateTime.Now;
                UpdateThoughtPreview();
            }
        }

        private void FinishThoughtStreaming()
        {
            if (!streamingThought)
            {
                return;
            }

            string htmlJson = JsonConvert.SerializeObject(MarkdownToHtml(streamingThoughtMarkdown.ToString()));
            string idJson = JsonConvert.SerializeObject(streamingThoughtDivId);
            string boxId = currentThinkingBoxId ?? string.Empty;
            EnqueueScript("var el=document.getElementById(" + idJson + ");if(el){el.innerHTML=" + htmlJson
                + ";}scrollThinkingBoxToBottom('" + boxId + "');");
            streamingThought = false;
            streamingThoughtDivId = null;
        }

        private void UpdateThoughtPreview()
        {
            if (string.IsNullOrEmpty(streamingThoughtDivId))
            {
                return;
            }

            string htmlJson = JsonConvert.SerializeObject(StreamingTextToHtml(streamingThoughtMarkdown.ToString()));
            string idJson = JsonConvert.SerializeObject(streamingThoughtDivId);
            string boxId = currentThinkingBoxId ?? string.Empty;
            EnqueueScript("var el=document.getElementById(" + idJson + ");if(el){el.innerHTML=" + htmlJson
                + ";}scrollThinkingBoxToBottom('" + boxId + "');");
        }

        // 追加对话消息：根据 role/color 决定 CSS 类，用户消息纯文本转义，Goose 消息走 Markdown→HTML。
        private void AppendConversation(string role, string text, Color color, DateTime? messageTime = null,
            string visualizationJson = null)
        {
            if (webViewConversation == null || webViewConversation.IsDisposed)
            {
                return;
            }
            string time = (messageTime ?? DateTime.Now).ToString("HH:mm:ss");
            string cls;
            string contentHtml;
            string avatarHtml = string.Empty;
            if (role == "用户")
            {
                cls = "msg user";
                contentHtml = HtmlEncode(text);
            }
            else if (role == "EW-AI")
            {
                cls = "msg assistant";
                contentHtml = BuildAutomationFlowCardsHtml(visualizationJson) + MarkdownToHtml(text);
                avatarHtml = "<img class=\"avatar avatar-image\" src=\"" + ChickAvatarDataUri + "\" alt=\"AI\" title=\"EW-AI " + HtmlEncode(time) + "\">";
            }
            else if (role == "错误" || color.ToArgb() == UiPalette.Danger.ToArgb())
            {
                cls = "msg error";
                contentHtml = HtmlEncode(text);
            }
            else
            {
                cls = "msg assistant";
                contentHtml = HtmlEncode(text);
                avatarHtml = "<span class=\"avatar system-avatar\" title=\"" + HtmlEncode(role) + " " + HtmlEncode(time) + "\">!</span>";
            }
            string roleHtml = string.Empty;
            string html = "<div class=\"" + cls + "\"><div class=\"msg-head\">" + avatarHtml + roleHtml + "<span class=\"msg-time\">" + HtmlEncode(time) + "</span>"
                + CopyButtonHtml + "</div>"
                + "<div class=\"content\">" + contentHtml + "</div></div>";
            EnqueueAppendHtml(html);
        }

        // 更新对话区正式回复的流式 HTML，并滚动到最新结果。
        private void UpdateStreamingPreview()
        {
            if (string.IsNullOrEmpty(streamingDivId))
            {
                return;
            }
            string html = StreamingTextToHtml(streamingMarkdown.ToString());
            string htmlJson = JsonConvert.SerializeObject(html);
            string idJson = JsonConvert.SerializeObject(streamingDivId);
            string js = "var el=document.getElementById(" + idJson + ");if(el){el.innerHTML=" + htmlJson + ";}if(window.scrollMessagesToBottom){scrollMessagesToBottom();}";
            EnqueueScript(js);
        }

        // 向 #messages 容器追加 HTML 片段并滚动到底部。
        private void EnqueueAppendHtml(string html)
        {
            string htmlJson = JsonConvert.SerializeObject(html);
            string js = "var box=document.getElementById('messages');"
                + "if(box){var tpl=document.createElement('template');tpl.innerHTML=" + htmlJson + ";"
                + "var incoming=tpl.content.firstElementChild;var last=box.lastElementChild;"
                + "if(incoming&&last&&incoming.classList.contains('assistant')&&last.classList.contains('assistant')){"
                + "var source=incoming.querySelector('.content');var target=last.querySelector('.content');"
                + "if(source&&target){var part=document.createElement('div');part.className='merged-part';part.innerHTML=source.innerHTML;target.appendChild(part);}"
                + "}else if(incoming){box.appendChild(incoming);}}"
                + "if(window.scrollMessagesToBottom){scrollMessagesToBottom();}";
            EnqueueScript(js);
        }

        // 串行化 ExecuteScriptAsync：通过 ContinueWith 链保证脚本按入队顺序执行（状态修改在调用前同步完成，脚本内 HTML 已捕获）。
        private void EnqueueScript(string js, bool requireMatchingGeneration = true)
        {
            if (webViewClosing || IsDisposed || Disposing)
            {
                return;
            }
            WebView2 localWebView = webViewConversation;
            var localCoreWebView = localWebView?.CoreWebView2;
            if (localWebView == null || localWebView.IsDisposed || localCoreWebView == null)
            {
                return;
            }
            try
            {
                int scriptGeneration = conversationViewGeneration;
                string guardedScript = requireMatchingGeneration
                    ? "if(window.__automationConversationViewGeneration==="
                        + scriptGeneration + "){"
                        + js
                        + "}"
                    : js;
                pendingScriptTask = (pendingScriptTask ?? Task.CompletedTask).ContinueWith(
                    async _ =>
                    {
                        try
                        {
                            if (!webViewClosing && !localWebView.IsDisposed)
                            {
                                await localCoreWebView.ExecuteScriptAsync(guardedScript);
                            }
                        }
                        catch
                        {
                            // WebView 关闭或单条脚本失败时终止本次渲染，不影响窗体退出。
                        }
                    },
                    TaskScheduler.FromCurrentSynchronizationContext()).Unwrap();
            }
            catch (InvalidOperationException)
            {
                // 窗体关闭期间 UI 同步上下文可能已终止，不再接受新脚本。
            }
        }

        // HTML 转义文本（< > & 等）。
        private static string HtmlEncode(string text)
        {
            return WebUtility.HtmlEncode(text ?? string.Empty);
        }

        // 完整消息交给 Markdig。只预处理两类可确定的模型格式错误：粘连标题和缺失表头的表格。
        private static string MarkdownToHtml(string markdown)
        {
            if (string.IsNullOrEmpty(markdown))
            {
                return string.Empty;
            }

            try
            {
                return Markdown.ToHtml(NormalizeMarkdownForRendering(markdown), markdownPipeline);
            }
            catch
            {
                return "<pre>" + HtmlEncode(markdown) + "</pre>";
            }
        }

        private static string NormalizeMarkdownForRendering(string markdown)
        {
            string normalizedMarkdown = (markdown ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n");
            normalizedMarkdown = Regex.Replace(normalizedMarkdown, @"(?m)(?<!^)(```|~~~)", "\n$1");
            normalizedMarkdown = UnwrapBareMarkdownFence(normalizedMarkdown);
            string[] lines = normalizedMarkdown.Split('\n');
            var output = new List<string>();
            bool inCodeFence = false;

            foreach (string line in lines)
            {
                string trimmed = line.TrimStart();
                if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
                {
                    inCodeFence = !inCodeFence;
                    output.Add(line);
                    continue;
                }

                if (inCodeFence)
                {
                    output.Add(line);
                    continue;
                }

                string normalizedLine = NormalizeMarkdownBlockMarkers(line);
                normalizedLine = NormalizeHeadingMarkers(normalizedLine);
                foreach (string logicalLine in normalizedLine.Split('\n'))
                {
                    foreach (string expandedLine in ExpandMalformedHeadingBody(logicalLine))
                    {
                        if (TryGetTableSeparatorColumnCount(expandedLine, out int columnCount)
                            && !LastOutputIsPipeTableRow(output))
                        {
                            if (output.Count > 0 && !string.IsNullOrWhiteSpace(output[output.Count - 1]))
                            {
                                output.Add(string.Empty);
                            }
                            output.Add(BuildEmptyTableHeader(columnCount));
                        }
                        output.Add(expandedLine);
                    }
                }
            }

            return string.Join("\n", output);
        }

        // 只修复能够判定为块标记的粘连：行尾粗体标题、列表标记和水平分隔线。
        // 普通句子中的行内粗体保持原样，代码围栏内容在调用本方法前已经排除。
        private static string NormalizeMarkdownBlockMarkers(string line)
        {
            string normalized = line ?? string.Empty;
            normalized = Regex.Replace(normalized, @"(?m)(?<=\S)---\s*$", "\n\n---");
            normalized = Regex.Replace(normalized, @"(?<=\S)(?=- \*\*)", "\n");
            normalized = Regex.Replace(normalized, @"(?<=\S)(?=\d+[\.、]\s+\S)", "\n");
            normalized = Regex.Replace(
                normalized,
                @"(?<=\S)(?=\*\*[^*\n]{1,80}\*\*(?:\s*$|\s+[—–]\s*))",
                "\n\n");
            normalized = Regex.Replace(normalized, @"(?m)^(\s*\d+[\.、])(?=[^\s\d])", "$1 ");
            normalized = Regex.Replace(normalized, @"(?m)^(\s*)-(?=[\p{L}])", "$1- ");
            return normalized;
        }

        private static string UnwrapBareMarkdownFence(string markdown)
        {
            string[] lines = (markdown ?? string.Empty).Split('\n');
            var output = new List<string>();
            for (int i = 0; i < lines.Length; i++)
            {
                string marker = lines[i].Trim();
                if (marker != "```" && marker != "~~~")
                {
                    output.Add(lines[i]);
                    continue;
                }

                int closingIndex = -1;
                for (int j = i + 1; j < lines.Length; j++)
                {
                    if (string.Equals(lines[j].Trim(), marker, StringComparison.Ordinal))
                    {
                        closingIndex = j;
                        break;
                    }
                }
                if (closingIndex < 0)
                {
                    output.Add(lines[i]);
                    continue;
                }

                bool containsMarkdown = lines
                    .Skip(i + 1)
                    .Take(closingIndex - i - 1)
                    .Any(line => line.TrimStart().StartsWith("#", StringComparison.Ordinal)
                        || line.TrimStart().StartsWith("|", StringComparison.Ordinal)
                        || line.TrimStart().StartsWith("- ", StringComparison.Ordinal)
                        || Regex.IsMatch(line, @"^\s*\d+[\.、]\s+"));
                if (!containsMarkdown)
                {
                    output.Add(lines[i]);
                    continue;
                }

                for (int j = i + 1; j < closingIndex; j++)
                {
                    output.Add(lines[j]);
                }
                i = closingIndex;
            }
            return string.Join("\n", output);
        }

        // 模型偶尔会把“正文###标题”拼在同一行，或省略标题标记后的空格。
        // 仅处理二至六级 # 标记，避免把单个 # 用途的普通文本改写为标题。
        private static string NormalizeHeadingMarkers(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return line;
            }

            var result = new StringBuilder();
            for (int i = 0; i < line.Length;)
            {
                if (line[i] != '#')
                {
                    result.Append(line[i]);
                    i++;
                    continue;
                }

                int count = 0;
                while (i + count < line.Length && line[i + count] == '#')
                {
                    count++;
                }

                int next = i + count;
                bool isHeading = count >= 2 && count <= 6 && next < line.Length && line[next] != '#';
                if (!isHeading)
                {
                    result.Append(line, i, count);
                    i = next;
                    continue;
                }

                if (result.Length > 0 && result[result.Length - 1] != '\n')
                {
                    result.Append('\n');
                }
                result.Append('#', count);
                if (!char.IsWhiteSpace(line[next]))
                {
                    result.Append(' ');
                }
                i = next;
            }
            return result.ToString();
        }

        // 模型可能把标题后的列表项或表头继续写在同一行，拆开后再交给 Markdown 渲染器。
        private static IEnumerable<string> ExpandMalformedHeadingBody(string line)
        {
            string trimmed = (line ?? string.Empty).TrimStart();
            if (!Regex.IsMatch(trimmed, @"^#{2,6}\s"))
            {
                yield return line;
                yield break;
            }

            Match headingMatch = Regex.Match(line ?? string.Empty, @"^\s*#{2,6}\s+");
            int headingEnd = headingMatch.Success ? headingMatch.Length : 0;
            int bodyIndex = line.IndexOf("- **", headingEnd, StringComparison.Ordinal);
            int pipeIndex = line.IndexOf('|');
            int strongIndex = line.IndexOf("**", headingEnd, StringComparison.Ordinal);
            if (strongIndex > headingEnd && char.IsWhiteSpace(line[strongIndex - 1]))
            {
                strongIndex = -1;
            }
            if (pipeIndex >= 0 && line.IndexOf('|', pipeIndex + 1) < 0)
            {
                pipeIndex = -1;
            }
            if (bodyIndex < 0 || (pipeIndex >= 0 && pipeIndex < bodyIndex))
            {
                bodyIndex = pipeIndex;
            }
            if (bodyIndex < 0 || (strongIndex >= 0 && strongIndex < bodyIndex))
            {
                bodyIndex = strongIndex;
            }
            if (bodyIndex <= 0)
            {
                yield return line;
                yield break;
            }

            yield return line.Substring(0, bodyIndex).TrimEnd();
            yield return string.Empty;
            yield return line.Substring(bodyIndex).TrimStart();
        }

        private static bool TryGetTableSeparatorColumnCount(string line, out int columnCount)
        {
            columnCount = 0;
            string trimmed = (line ?? string.Empty).Trim();
            if (!trimmed.StartsWith("|", StringComparison.Ordinal) || !trimmed.EndsWith("|", StringComparison.Ordinal))
            {
                return false;
            }

            string[] columns = trimmed.Substring(1, trimmed.Length - 2).Split('|');
            if (columns.Length < 2)
            {
                return false;
            }

            foreach (string column in columns)
            {
                string value = column.Trim().Trim(':');
                if (value.Length < 3)
                {
                    return false;
                }
                foreach (char valueChar in value)
                {
                    if (valueChar != '-')
                    {
                        return false;
                    }
                }
            }

            columnCount = columns.Length;
            return true;
        }

        private static bool LastOutputIsPipeTableRow(List<string> output)
        {
            if (output.Count == 0)
            {
                return false;
            }

            string previous = output[output.Count - 1].Trim();
            return previous.StartsWith("|", StringComparison.Ordinal) && previous.EndsWith("|", StringComparison.Ordinal);
        }

        private static string BuildEmptyTableHeader(int columnCount)
        {
            var builder = new StringBuilder("|");
            for (int i = 0; i < columnCount; i++)
            {
                builder.Append(" |");
            }
            return builder.ToString();
        }

        // 流式片段可能尚未闭合 Markdown 块，只做安全文本预览，结束后再由 MarkdownToHtml 正式渲染。
        private static string StreamingTextToHtml(string text)
        {
            return HtmlEncode(text).Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "<br>");
        }
    }
}
