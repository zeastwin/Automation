using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Reflection;

namespace Automation
{
    /// <summary>
    /// 指令行为契约目录。Schema、指南和校验应共同使用这里的结构化规则，
    /// 避免把执行语义分别维护在提示词、界面转换器和校验代码中。
    /// </summary>
    public static class OperationBehaviorCatalog
    {
        public const int ContractVersion = 3;

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
                    contract["judgeModes"] = new JObject
                    {
                        ["值在区间左"] = "equal=true: value <= Down；equal=false: value < Down；Up 不参与该模式",
                        ["值在区间右"] = "equal=true: value >= Down；equal=false: value > Down；Up 不参与该模式",
                        ["值在区间内"] = "equal=true: Down <= value <= Up；equal=false: Down < value < Up",
                        ["等于特征字符"] = "变量文本与 keyString 做区分大小写的完全相等比较"
                    };
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

                case "修改变量":
                    contract = CreateContract(
                        "读取源变量，用固定值或另一个变量执行运算，并写入结果变量。",
                        new[] { "读取 ValueSource* 指定的源变量", "从 ChangeValue 固定值或 ChangeValue* 变量中二选一取得操作数", "按 ModifyType 计算", "写入 OutputValue* 指定的结果变量" },
                        true);
                    contract["constraints"] = new JArray(
                        "ValueSourceIndex/ValueSourceName 等源引用必须且只能形成一个有效引用",
                        "ChangeValue 固定值与 ChangeValueIndex/ChangeValueName 等变量引用必须且只能选择一种",
                        "OutputValueIndex/OutputValueName 等结果引用必须且只能形成一个有效引用",
                        "叠加、乘法、除法、求余和绝对值要求源变量与结果变量为 double；非绝对值操作数也必须为 double",
                        "除法和求余的固定操作数不能为0");
                    contract["semanticKinds"] = new JObject
                    {
                        ["fixedSet"] = "variable.set",
                        ["fixedAdd"] = "variable.add",
                        ["variableOrNumericCompute"] = "variable.compute"
                    };
                    contract["failureModes"] = new JArray(
                        "源变量、修改值或结果变量缺失时报警",
                        "固定修改值与修改值变量同时配置时报警",
                        "数值模式的变量类型或固定数值无效时报警");
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

                case "PLC读写":
                    contract = CreateContract(
                        "按强类型Modbus地址执行按项或连续批量读取/写入。",
                        new[] { "按项模式处理多个独立地址", "连续批量模式通过一次请求处理连续地址", "读取写入变量、写入固定值均严格匹配PLC数据类型", "任一项失败时报警且不执行宽松转换" },
                        true);
                    AddRequiredField(contract, "DeviceName", "PLC设备精确名称；可通过 get_plc_device 或 list_plc_devices 获取");
                    AddMultiConditionalField(contract, "ReadItems", new JObject
                    {
                        ["Action"] = new JArray("Read"),
                        ["Mode"] = new JArray("Items")
                    }, "按项读取配置；ReadItemCount 由数组长度自动计算");
                    AddMultiConditionalField(contract, "ReadBatch", new JObject
                    {
                        ["Action"] = new JArray("Read"),
                        ["Mode"] = new JArray("ContinuousBatch")
                    }, "连续批量读取配置；结果从首保存变量开始按变量索引写入");
                    AddMultiConditionalField(contract, "WriteItems", new JObject
                    {
                        ["Action"] = new JArray("Write"),
                        ["Mode"] = new JArray("Items")
                    }, "按项写入配置；WriteItemCount 由数组长度自动计算，每项独立选择变量或固定值");
                    AddMultiConditionalField(contract, "WriteBatch", new JObject
                    {
                        ["Action"] = new JArray("Write"),
                        ["Mode"] = new JArray("ContinuousBatch")
                    }, "连续批量写入配置；使用连续变量或明确重复固定值");
                    contract["modeMatrix"] = new JArray(
                        new JObject
                        {
                            ["when"] = new JObject { ["Action"] = "Read", ["Mode"] = "Items" },
                            ["use"] = new JArray("DeviceName", "ReadItems"),
                            ["reject"] = new JArray("ReadBatch", "WriteItems", "WriteBatch")
                        },
                        new JObject
                        {
                            ["when"] = new JObject { ["Action"] = "Read", ["Mode"] = "ContinuousBatch" },
                            ["use"] = new JArray("DeviceName", "ReadBatch"),
                            ["reject"] = new JArray("ReadItems", "WriteItems", "WriteBatch")
                        },
                        new JObject
                        {
                            ["when"] = new JObject { ["Action"] = "Write", ["Mode"] = "Items" },
                            ["use"] = new JArray("DeviceName", "WriteItems"),
                            ["reject"] = new JArray("ReadItems", "ReadBatch", "WriteBatch")
                        },
                        new JObject
                        {
                            ["when"] = new JObject { ["Action"] = "Write", ["Mode"] = "ContinuousBatch" },
                            ["use"] = new JArray("DeviceName", "WriteBatch"),
                            ["reject"] = new JArray("ReadItems", "ReadBatch", "WriteItems")
                        });
                    contract["dataRules"] = new JObject
                    {
                        ["address"] = "StartAddress 0..65535，且按数据宽度计算后的访问末地址不得超过65535",
                        ["elementCount"] = "连续批量1..1000；String固定为1；重复固定值允许明确写入全部元素",
                        ["stringByteLength"] = "String 为1..2000，其他 DataType 必须为0",
                        ["areaAndType"] = "Coil/DiscreteInput 仅 Boolean；HoldingRegister/InputRegister 不允许 Boolean",
                        ["writeArea"] = "Write 仅允许 Coil 或 HoldingRegister",
                        ["variableType"] = "String 对应平台 string；Boolean及所有数值类型对应平台 double"
                    };
                    contract["constraints"] = new JArray(
                        "Action只能是Read或Write",
                        "Mode只能是Items或ContinuousBatch，且只允许当前Action/Mode分支字段",
                        "ReadItems与WriteItems数量范围1..100，数量字段由编译器计算",
                        "连续批量读取从ReadBatch.FirstVariableName开始写入连续变量",
                        "连续批量写入从WriteBatch.FirstVariableName读取连续变量，或将WriteBatch.ConstantValue明确重复到全部元素",
                        "Boolean只允许Coil或DiscreteInput，其他类型只允许寄存器区",
                        "DiscreteInput和InputRegister禁止Write",
                        "固定值按目标DataType严格解析");
                    contract["failureModes"] = new JArray("设备未初始化时报警", "参数或变量类型不匹配时报警", "通讯失败时设备进入故障状态");
                    break;

                case "PLC映射控制":
                    contract = CreateContract(
                        "按设备重新初始化、启动或停止PLC变量映射。",
                        new[] { "按DeviceName定位设备", "执行Reinitialize、Start或Stop", "失败时报警" },
                        true);
                    AddRequiredField(contract, "DeviceName", "PLC设备精确名称；可通过 get_plc_device 或 list_plc_devices 获取");
                    contract["actionSemantics"] = new JObject
                    {
                        ["Reinitialize"] = "重建该设备连接，成功后进入Ready，不自动启动映射",
                        ["Start"] = "启动该设备已启用的变量映射",
                        ["Stop"] = "停止该设备变量映射；重复停止仍视为成功"
                    };
                    contract["constraints"] = new JArray(
                        "Reinitialize只重建连接，不自动启动映射",
                        "Start要求设备处于Ready或Stopped",
                        "Stop幂等");
                    contract["failureModes"] = new JArray("设备不存在或状态不允许时报警", "重新初始化失败时保持Faulted");
                    break;

                case "流程结束":
                    contract = CreateContract(
                        "执行到当前位置时正常结束当前流程。",
                        new[] { "请求流程以 Completed 原因结束", "不执行当前位置之后的指令" },
                        false);
                    contract["controlFlow"]["terminal"] = true;
                    contract["controlFlow"]["terminationReason"] = "Completed";
                    break;

                default:
                    contract = CreateUnknownContract();
                    break;
            }

            if (contract["coverage"] == null)
            {
                contract["coverage"] = "specialized";
            }
            contract["source"] = nameof(OperationBehaviorCatalog);
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
            if (rule["requiredForRun"]?.Value<bool>() == true)
            {
                return true;
            }

            JObject requiredWhen = rule["requiredWhenForRun"] as JObject;
            if (requiredWhen != null && MatchesAllConditions(operation, requiredWhen))
            {
                return true;
            }
            if (rule["requiredWhenAnyForRun"] is JArray alternatives)
            {
                foreach (JObject alternative in alternatives.OfType<JObject>())
                {
                    if (MatchesAllConditions(operation, alternative)) return true;
                }
            }
            return false;
        }

        private static bool MatchesAllConditions(OperationType operation, JObject conditions)
        {
            foreach (JProperty condition in conditions.Properties())
            {
                PropertyInfo property = operation.GetType().GetProperty(condition.Name);
                string currentValue = property?.GetValue(operation)?.ToString();
                if (!(condition.Value is JArray candidates)
                    || !ContainsString(candidates, currentValue)) return false;
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

        private static JObject CreateUnknownContract()
        {
            return new JObject
            {
                ["contractVersion"] = ContractVersion,
                ["coverage"] = "unknown",
                ["purpose"] = "该原生指令的字段结构可以严格读取和写入，但尚无专用运行行为契约。",
                ["execution"] = new JArray(),
                ["controlFlow"] = new JObject
                {
                    ["known"] = false,
                    ["description"] = "未提供控制流结论；不得由通用默认值推断是否顺序执行。"
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
                ["requiredForRun"] = required,
                ["referenceType"] = "proc.goto",
                ["format"] = "{operationId} | {operationKey} | {stepId,operationKey} | {stepKey,operationKey}",
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
                ["requiredWhenForRun"] = new JObject { [dependsOn] = new JArray(values) },
                ["description"] = description
            };
        }

        private static void AddRequiredField(JObject contract, string fieldName, string description)
        {
            ((JObject)contract["fieldRules"])[fieldName] = new JObject
            {
                ["requiredForRun"] = true,
                ["description"] = description
            };
        }

        private static void AddMultiConditionalField(
            JObject contract, string fieldName, JObject conditions, string description)
        {
            ((JObject)contract["fieldRules"])[fieldName] = new JObject
            {
                ["visibleWhen"] = conditions.DeepClone(),
                ["requiredWhenForRun"] = conditions.DeepClone(),
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
                ["safeDefault"] = "报警停止",
                ["gotoScope"] = "Goto1/Goto2/Goto3 是异常处理分支，与指令自身的业务跳转字段相互独立",
                ["missingResourceBehavior"] = "报警信息编号可以先保存；对应资源未配置时流程状态为 incomplete，启动闸门会拒绝运行"
            };
        }

        private static void AddConditionalRule(JObject rules, string fieldName, string dependsOn, string[] values, string description)
        {
            rules[fieldName] = new JObject
            {
                ["visibleWhen"] = new JObject { [dependsOn] = new JArray(values) },
                ["requiredWhenForRun"] = new JObject { [dependsOn] = new JArray(values) },
                ["description"] = description
            };
        }

        private static void AddConditionalGotoRule(JObject rules, string fieldName, string dependsOn, string[] values, string description)
        {
            AddConditionalRule(rules, fieldName, dependsOn, values, description);
            JObject rule = (JObject)rules[fieldName];
            rule["referenceType"] = "proc.goto";
            rule["format"] = "{operationId} | {operationKey} | {stepId,operationKey} | {stepKey,operationKey}";
            rule["allowDisplayText"] = false;
        }
    }
}
