using System;
using System.Collections.Generic;
using System.Linq;
using Automation;

namespace Automation.AIFlow
{
    public static class AiFlowVerifier
    {
        public static List<AiFlowIssue> VerifyCore(AiCoreFlow core)
        {
            var issues = new List<AiFlowIssue>();
            AiFlowCompileResult compile = AiFlowCompiler.CompileCore(core);
            if (!compile.Success)
            {
                issues.AddRange(compile.Issues);
                return issues;
            }
            VerifyProcs(compile.Procs, issues);
            return issues;
        }

        public static List<AiFlowIssue> VerifySpec(AiSpecFlow spec)
        {
            var issues = new List<AiFlowIssue>();
            AiFlowCompileResult compile = AiFlowCompiler.CompileSpec(spec);
            if (!compile.Success)
            {
                issues.AddRange(compile.Issues);
                return issues;
            }
            VerifyProcs(compile.Procs, issues);
            return issues;
        }

        public static List<AiFlowIssue> VerifyProcs(List<Proc> procs)
        {
            var issues = new List<AiFlowIssue>();
            VerifyProcs(procs, issues);
            return issues;
        }

        private static void VerifyProcs(List<Proc> procs, List<AiFlowIssue> issues)
        {
            if (procs == null)
            {
                issues.Add(new AiFlowIssue("VERIFY_PROCS_NULL", "流程为空", "verify"));
                return;
            }
            for (int procIndex = 0; procIndex < procs.Count; procIndex++)
            {
                Proc proc = procs[procIndex];
                if (proc?.steps == null)
                {
                    continue;
                }
                for (int stepIndex = 0; stepIndex < proc.steps.Count; stepIndex++)
                {
                    Step step = proc.steps[stepIndex];
                    if (step?.Ops == null)
                    {
                        continue;
                    }
                    for (int opIndex = 0; opIndex < step.Ops.Count; opIndex++)
                    {
                        OperationType op = step.Ops[opIndex];
                        if (op == null)
                        {
                            continue;
                        }
                        string loc = $"proc:{procIndex}/step:{stepIndex}/op:{opIndex}";
                        VerifyOperation(op, loc, issues);
                    }
                }
            }
        }

        private static void VerifyOperation(OperationType op, string loc, List<AiFlowIssue> issues)
        {
            switch (op)
            {
                case IoOperate ioOperate:
                    VerifyIoOperate(ioOperate, loc, issues);
                    break;
                case IoCheck ioCheck:
                    VerifyIoCheck(ioCheck, loc, issues);
                    break;
                case IoLogicGoto ioLogicGoto:
                    VerifyIoLogicGoto(ioLogicGoto, loc, issues);
                    break;
                case ProcOps procOps:
                    VerifyProcOps(procOps, loc, issues);
                    break;
                case WaitProc waitProc:
                    VerifyWaitProc(waitProc, loc, issues);
                    break;
                case Goto gotoOp:
                    VerifyGoto(gotoOp, loc, issues);
                    break;
                case ParamGoto paramGoto:
                    VerifyParamGoto(paramGoto, loc, issues);
                    break;
                case WaitTcp waitTcp:
                    VerifyWaitTcp(waitTcp, loc, issues);
                    break;
                case WaitSerialPort waitSerialPort:
                    VerifyWaitSerial(waitSerialPort, loc, issues);
                    break;
                case SendTcpMsg sendTcpMsg:
                    VerifyTimeout(sendTcpMsg.TimeOut, "TCP发送超时", loc, issues);
                    VerifyNotEmpty(sendTcpMsg.ID, "TCP对象名称为空", loc, issues);
                    break;
                case ReceoveTcpMsg receoveTcpMsg:
                    VerifyTimeout(receoveTcpMsg.TImeOut, "TCP接收超时", loc, issues);
                    VerifyNotEmpty(receoveTcpMsg.ID, "TCP对象名称为空", loc, issues);
                    break;
                case SendSerialPortMsg sendSerial:
                    VerifyTimeout(sendSerial.TimeOut, "串口发送超时", loc, issues);
                    VerifyNotEmpty(sendSerial.ID, "串口对象名称为空", loc, issues);
                    break;
                case ReceoveSerialPortMsg receoveSerial:
                    VerifyTimeout(receoveSerial.TImeOut, "串口接收超时", loc, issues);
                    VerifyNotEmpty(receoveSerial.ID, "串口对象名称为空", loc, issues);
                    break;
                case SendReceoveCommMsg sendReceoveComm:
                    VerifyTimeout(sendReceoveComm.TimeOut, "通讯超时", loc, issues);
                    VerifyNotEmpty(sendReceoveComm.ID, "通讯对象名称为空", loc, issues);
                    break;
            }
        }

        private static void VerifyIoOperate(IoOperate ioOperate, string loc, List<AiFlowIssue> issues)
        {
            if (ioOperate.IoParams == null || ioOperate.IoParams.Count == 0)
            {
                issues.Add(new AiFlowIssue("VERIFY_IO_PARAM_EMPTY", "IO操作参数为空", loc));
                return;
            }
            for (int i = 0; i < ioOperate.IoParams.Count; i++)
            {
                var param = ioOperate.IoParams[i];
                if (param == null || string.IsNullOrWhiteSpace(param.IOName))
                {
                    issues.Add(new AiFlowIssue("VERIFY_IO_NAME_EMPTY", $"IO操作名称为空:#{i + 1}", loc));
                }
            }
        }

        private static void VerifyIoCheck(IoCheck ioCheck, string loc, List<AiFlowIssue> issues)
        {
            VerifyTimeoutConfig(ioCheck.timeOutC, "IO检测超时", loc, issues);
            if (ioCheck.IoParams == null || ioCheck.IoParams.Count == 0)
            {
                issues.Add(new AiFlowIssue("VERIFY_IOCHECK_PARAM_EMPTY", "IO检测参数为空", loc));
                return;
            }
            for (int i = 0; i < ioCheck.IoParams.Count; i++)
            {
                var param = ioCheck.IoParams[i];
                if (param == null || string.IsNullOrWhiteSpace(param.IOName))
                {
                    issues.Add(new AiFlowIssue("VERIFY_IOCHECK_NAME_EMPTY", $"IO检测名称为空:#{i + 1}", loc));
                }
            }
        }

        private static void VerifyIoLogicGoto(IoLogicGoto ioLogicGoto, string loc, List<AiFlowIssue> issues)
        {
            if (ioLogicGoto.IoParams == null || ioLogicGoto.IoParams.Count == 0)
            {
                issues.Add(new AiFlowIssue("VERIFY_IOLOGIC_PARAM_EMPTY", "IO逻辑跳转参数为空", loc));
            }
            if (string.IsNullOrWhiteSpace(ioLogicGoto.TrueGoto))
            {
                issues.Add(new AiFlowIssue("VERIFY_IOLOGIC_TRUE_EMPTY", "IO逻辑跳转 true 跳转为空", loc));
            }
            if (string.IsNullOrWhiteSpace(ioLogicGoto.FalseGoto))
            {
                issues.Add(new AiFlowIssue("VERIFY_IOLOGIC_FALSE_EMPTY", "IO逻辑跳转 false 跳转为空", loc));
            }
        }

        private static void VerifyProcOps(ProcOps procOps, string loc, List<AiFlowIssue> issues)
        {
            if (procOps.procParams == null || procOps.procParams.Count == 0)
            {
                issues.Add(new AiFlowIssue("VERIFY_PROCOPS_EMPTY", "流程协同参数为空", loc));
                return;
            }
            for (int i = 0; i < procOps.procParams.Count; i++)
            {
                var param = procOps.procParams[i];
                if (param == null)
                {
                    issues.Add(new AiFlowIssue("VERIFY_PROCOPS_PARAM_NULL", $"流程协同参数为空:#{i + 1}", loc));
                    continue;
                }
                if (string.IsNullOrWhiteSpace(param.ProcName) && string.IsNullOrWhiteSpace(param.ProcValue))
                {
                    issues.Add(new AiFlowIssue("VERIFY_PROCOPS_NAME_EMPTY", $"流程协同名称为空:#{i + 1}", loc));
                }
                if (string.IsNullOrWhiteSpace(param.value))
                {
                    issues.Add(new AiFlowIssue("VERIFY_PROCOPS_OP_EMPTY", $"流程协同操作类型为空:#{i + 1}", loc));
                }
            }
        }

        private static void VerifyWaitProc(WaitProc waitProc, string loc, List<AiFlowIssue> issues)
        {
            VerifyTimeoutConfig(waitProc.timeOutC, "等待流程超时", loc, issues);
            if (waitProc.Params == null || waitProc.Params.Count == 0)
            {
                issues.Add(new AiFlowIssue("VERIFY_WAITPROC_EMPTY", "等待流程参数为空", loc));
                return;
            }
            for (int i = 0; i < waitProc.Params.Count; i++)
            {
                var param = waitProc.Params[i];
                if (param == null)
                {
                    issues.Add(new AiFlowIssue("VERIFY_WAITPROC_PARAM_NULL", $"等待流程参数为空:#{i + 1}", loc));
                    continue;
                }
                if (string.IsNullOrWhiteSpace(param.ProcName) && string.IsNullOrWhiteSpace(param.ProcValue))
                {
                    issues.Add(new AiFlowIssue("VERIFY_WAITPROC_NAME_EMPTY", $"等待流程名称为空:#{i + 1}", loc));
                }
                if (string.IsNullOrWhiteSpace(param.value))
                {
                    issues.Add(new AiFlowIssue("VERIFY_WAITPROC_OP_EMPTY", $"等待流程操作类型为空:#{i + 1}", loc));
                }
            }
        }

        private static void VerifyGoto(Goto gotoOp, string loc, List<AiFlowIssue> issues)
        {
            bool hasParams = gotoOp.Params != null && gotoOp.Params.Count > 0;
            bool hasDefault = !string.IsNullOrWhiteSpace(gotoOp.DefaultGoto);
            if (!hasParams && !hasDefault)
            {
                issues.Add(new AiFlowIssue("VERIFY_GOTO_EMPTY", "跳转条件与默认跳转均为空", loc));
            }
            if (hasParams)
            {
                for (int i = 0; i < gotoOp.Params.Count; i++)
                {
                    var param = gotoOp.Params[i];
                    if (param == null)
                    {
                        issues.Add(new AiFlowIssue("VERIFY_GOTO_PARAM_NULL", $"跳转参数为空:#{i + 1}", loc));
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(param.Goto))
                    {
                        issues.Add(new AiFlowIssue("VERIFY_GOTO_TARGET_EMPTY", $"跳转目标为空:#{i + 1}", loc));
                    }
                }
            }
        }

        private static void VerifyParamGoto(ParamGoto paramGoto, string loc, List<AiFlowIssue> issues)
        {
            if (paramGoto.Params == null || paramGoto.Params.Count == 0)
            {
                issues.Add(new AiFlowIssue("VERIFY_PARAMGOTO_EMPTY", "逻辑判断条件为空", loc));
            }
            if (string.IsNullOrWhiteSpace(paramGoto.goto1))
            {
                issues.Add(new AiFlowIssue("VERIFY_PARAMGOTO_GOTO1", "逻辑判断成功跳转为空", loc));
            }
            if (string.IsNullOrWhiteSpace(paramGoto.goto2))
            {
                issues.Add(new AiFlowIssue("VERIFY_PARAMGOTO_GOTO2", "逻辑判断失败跳转为空", loc));
            }
        }

        private static void VerifyWaitTcp(WaitTcp waitTcp, string loc, List<AiFlowIssue> issues)
        {
            if (waitTcp.Params == null || waitTcp.Params.Count == 0)
            {
                issues.Add(new AiFlowIssue("VERIFY_WAITTCP_EMPTY", "等待TCP参数为空", loc));
                return;
            }
            for (int i = 0; i < waitTcp.Params.Count; i++)
            {
                var param = waitTcp.Params[i];
                if (param == null)
                {
                    issues.Add(new AiFlowIssue("VERIFY_WAITTCP_PARAM_NULL", $"等待TCP参数为空:#{i + 1}", loc));
                    continue;
                }
                VerifyNotEmpty(param.Name, $"等待TCP名称为空:#{i + 1}", loc, issues);
                VerifyTimeout(param.TimeOut, $"等待TCP超时配置无效:{param.Name}", loc, issues);
            }
        }

        private static void VerifyWaitSerial(WaitSerialPort waitSerialPort, string loc, List<AiFlowIssue> issues)
        {
            if (waitSerialPort.Params == null || waitSerialPort.Params.Count == 0)
            {
                issues.Add(new AiFlowIssue("VERIFY_WAITSERIAL_EMPTY", "等待串口参数为空", loc));
                return;
            }
            for (int i = 0; i < waitSerialPort.Params.Count; i++)
            {
                var param = waitSerialPort.Params[i];
                if (param == null)
                {
                    issues.Add(new AiFlowIssue("VERIFY_WAITSERIAL_PARAM_NULL", $"等待串口参数为空:#{i + 1}", loc));
                    continue;
                }
                VerifyNotEmpty(param.Name, $"等待串口名称为空:#{i + 1}", loc, issues);
                VerifyTimeout(param.TimeOut, $"等待串口超时配置无效:{param.Name}", loc, issues);
            }
        }

        private static void VerifyTimeoutConfig(TimeOutC timeOutC, string label, string loc, List<AiFlowIssue> issues)
        {
            if (timeOutC == null)
            {
                issues.Add(new AiFlowIssue("VERIFY_TIMEOUT_NULL", $"{label}配置为空", loc));
                return;
            }
            if (timeOutC.TimeOut <= 0 && string.IsNullOrWhiteSpace(timeOutC.TimeOutValue))
            {
                issues.Add(new AiFlowIssue("VERIFY_TIMEOUT_INVALID", $"{label}配置无效", loc));
            }
        }

        private static void VerifyTimeout(int value, string message, string loc, List<AiFlowIssue> issues)
        {
            if (value <= 0)
            {
                issues.Add(new AiFlowIssue("VERIFY_TIMEOUT_INVALID", message, loc));
            }
        }

        private static void VerifyNotEmpty(string value, string message, string loc, List<AiFlowIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                issues.Add(new AiFlowIssue("VERIFY_REF_EMPTY", message, loc));
            }
        }
    }
}
