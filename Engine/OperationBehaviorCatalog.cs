using Newtonsoft.Json.Linq;
using System;
using System.Reflection;

namespace Automation
{
    /// <summary>
    /// 指令行为契约目录。Schema、指南和校验应共同使用这里的结构化规则，
    /// 避免把执行语义分别维护在提示词、界面转换器和校验代码中。
    /// </summary>
    public static class OperationBehaviorCatalog
    {
        public const int ContractVersion = 1;

        public static JObject BuildContract(OperationType operation)
        {
            if (operation == null)
            {
                return null;
            }

            JObject contract;
            switch (operation.OperaType)
            {
                case "逻辑判断":
                    contract = CreateContract(
                        "组合多个数值或字符条件，并根据最终结果跳转。",
                        new[] { "按条件列表顺序读取变量并计算", "使用且/或组合结果", "成立时跳转 goto1，不成立时按 failDelay 延时后跳转 goto2" },
                        false);
                    AddRequiredGoto(contract, "goto1", "条件成立时的跳转目标");
                    AddRequiredGoto(contract, "goto2", "条件不成立时的跳转目标；若需继续下一条，也必须明确指向下一条指令");
                    contract["failureModes"] = new JArray("条件变量无效时报警", "failDelay 不是非负整数时报警", "任一跳转目标为空或无效时报警");
                    break;

                case "IO逻辑跳转":
                    contract = CreateContract(
                        "组合多个 IO 状态，并根据逻辑结果跳转。",
                        new[] { "读取 IoParams 中的 IO 状态", "使用且/或组合结果", "为真跳转 TrueGoto，为假按 InvalidDelayMs 延时后跳转 FalseGoto" },
                        false);
                    AddRequiredGoto(contract, "TrueGoto", "IO 逻辑为真时的跳转目标");
                    AddRequiredGoto(contract, "FalseGoto", "IO 逻辑为假时的跳转目标");
                    contract["failureModes"] = new JArray("IO 名称无效时报警", "跳转目标为空或无效时报警");
                    break;

                case "跳转":
                    contract = CreateContract(
                        "读取变量并匹配分支，命中后跳转；全部未命中时使用默认跳转。",
                        new[] { "读取 ValueIndex/ValueName 指定的变量", "依次比较 Params 中的匹配值", "命中时跳转到该项 Goto", "未命中时跳转 DefaultGoto" },
                        false);
                    AddGotoRule(contract, "DefaultGoto", false, "未匹配任何分支时的目标；为空且没有分支命中时运行会报警");
                    contract["constraints"] = new JArray("固定匹配值与匹配值变量互斥", "每个分支的 Goto 必须有效", "DefaultGoto 可以为空，但仅当运行时保证存在匹配分支；否则会报警");
                    contract["failureModes"] = new JArray("待匹配变量为空或不存在时报警", "匹配值配置冲突时报警", "未命中且 DefaultGoto 为空时报警");
                    break;

                case "弹框":
                    contract = CreateContract(
                        "显示交互弹框，根据按钮结果跳转。",
                        new[] { "根据 InfoType 解析提示内容", "按 PopupType 显示一至三个按钮", "所选按钮的 PopupGoto 非空时跳转，为空时顺序执行下一条" },
                        true);
                    contract["controlFlow"]["description"] = "按钮对应的 PopupGoto 非空时显式跳转；为空时顺序执行下一条。位于流程末尾时会自然结束并进入 Stopped。";
                    AddConditionalGoto(contract, "PopupGoto1", "PopupType", new[] { "弹是", "弹是与否", "弹是与否与取消" }, "按钮1可选跳转目标；为空时顺序执行下一条");
                    AddConditionalGoto(contract, "PopupGoto2", "PopupType", new[] { "弹是与否", "弹是与否与取消" }, "按钮2可选跳转目标；为空时顺序执行下一条");
                    AddConditionalGoto(contract, "PopupGoto3", "PopupType", new[] { "弹是与否与取消" }, "按钮3可选跳转目标；为空时顺序执行下一条");
                    AddConditionalField(contract, "PopupMessage", "InfoType", new[] { "自定义提示信息" }, "固定提示文本");
                    AddConditionalField(contract, "PopupMessageValue", "InfoType", new[] { "变量类型" }, "提示内容变量");
                    AddConditionalField(contract, "PopupAlarmInfoID", "InfoType", new[] { "报警信息库" }, "报警信息编号");
                    contract["failureModes"] = new JArray("提示内容或可见按钮文本为空时报警", "提示变量或报警信息不存在时报警", "非空跳转地址无效时报警");
                    break;

                default:
                    contract = CreateContract(
                        "执行该类型指令定义的自动化动作。",
                        new[] { "按字段配置执行指令" },
                        true);
                    contract["coverage"] = "基础契约；尚未迁移该指令的专用执行语义";
                    break;
            }

            AddAlarmPolicy(contract);
            return contract;
        }

        public static JObject BuildFieldRule(OperationType operation, string fieldName)
        {
            JObject contract = BuildContract(operation);
            return contract?["fieldRules"]?[fieldName] as JObject;
        }

        public static bool IsFieldRequired(OperationType operation, string fieldName)
        {
            JObject rule = BuildFieldRule(operation, fieldName);
            if (rule == null)
            {
                return false;
            }
            if (rule["required"]?.Value<bool>() == true)
            {
                return true;
            }

            JObject requiredWhen = rule["requiredWhen"] as JObject;
            if (requiredWhen == null)
            {
                return false;
            }
            foreach (JProperty condition in requiredWhen.Properties())
            {
                PropertyInfo property = operation.GetType().GetProperty(condition.Name);
                string currentValue = property?.GetValue(operation)?.ToString();
                if (!(condition.Value is JArray candidates)
                    || !ContainsString(candidates, currentValue))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool ContainsString(JArray values, string target)
        {
            foreach (JToken value in values)
            {
                if (string.Equals(value?.ToString(), target, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static JObject CreateContract(string purpose, string[] execution, bool fallThrough)
        {
            return new JObject
            {
                ["contractVersion"] = ContractVersion,
                ["purpose"] = purpose,
                ["execution"] = new JArray(execution),
                ["controlFlow"] = new JObject
                {
                    ["fallThrough"] = fallThrough,
                    ["description"] = fallThrough
                        ? "正常完成后自动执行下一条指令"
                        : "该指令显式决定后续流向，不自动执行下一条"
                },
                ["fieldRules"] = new JObject()
            };
        }

        private static void AddRequiredGoto(JObject contract, string fieldName, string description)
        {
            AddGotoRule(contract, fieldName, true, description);
        }

        private static void AddGotoRule(JObject contract, string fieldName, bool required, string description)
        {
            ((JObject)contract["fieldRules"])[fieldName] = new JObject
            {
                ["required"] = required,
                ["referenceType"] = "proc.goto",
                ["format"] = "procIndex-stepIndex-opIndex",
                ["allowDisplayText"] = false,
                ["description"] = description
            };
        }

        private static void AddConditionalGoto(JObject contract, string fieldName, string dependsOn, string[] values, string description)
        {
            AddGotoRule(contract, fieldName, false, description);
            JObject rule = (JObject)contract["fieldRules"][fieldName];
            rule["visibleWhen"] = new JObject { [dependsOn] = new JArray(values) };
        }

        private static void AddConditionalField(JObject contract, string fieldName, string dependsOn, string[] values, string description)
        {
            ((JObject)contract["fieldRules"])[fieldName] = new JObject
            {
                ["visibleWhen"] = new JObject { [dependsOn] = new JArray(values) },
                ["requiredWhen"] = new JObject { [dependsOn] = new JArray(values) },
                ["description"] = description
            };
        }

        private static void AddAlarmPolicy(JObject contract)
        {
            JObject rules = (JObject)contract["fieldRules"];
            AddConditionalRule(rules, "AlarmInfoID", "AlarmType", new[] { "弹框确定", "弹框确定与否", "弹框确定与否与取消" }, "弹框报警使用的报警信息编号");
            AddConditionalGotoRule(rules, "Goto1", "AlarmType", new[] { "自动处理", "弹框确定", "弹框确定与否", "弹框确定与否与取消" }, "报警确认或自动处理分支");
            AddConditionalGotoRule(rules, "Goto2", "AlarmType", new[] { "弹框确定与否", "弹框确定与否与取消" }, "报警否定分支");
            AddConditionalGotoRule(rules, "Goto3", "AlarmType", new[] { "弹框确定与否与取消" }, "报警取消分支");
            contract["alarmPolicy"] = new JObject
            {
                ["description"] = "AlarmType 决定异常后的停止、忽略、自动处理或弹框分支行为",
                ["safeDefault"] = "报警停止"
            };
        }

        private static void AddConditionalRule(JObject rules, string fieldName, string dependsOn, string[] values, string description)
        {
            rules[fieldName] = new JObject
            {
                ["visibleWhen"] = new JObject { [dependsOn] = new JArray(values) },
                ["requiredWhen"] = new JObject { [dependsOn] = new JArray(values) },
                ["description"] = description
            };
        }

        private static void AddConditionalGotoRule(JObject rules, string fieldName, string dependsOn, string[] values, string description)
        {
            AddConditionalRule(rules, fieldName, dependsOn, values, description);
            JObject rule = (JObject)rules[fieldName];
            rule["referenceType"] = "proc.goto";
            rule["format"] = "procIndex-stepIndex-opIndex";
            rule["allowDisplayText"] = false;
        }
    }
}
