using Newtonsoft.Json;
// 模块：Bridge / 服务。
// 职责范围：实现 Named Pipe 请求的路由、投影、诊断、预演和事务提交。

using Newtonsoft.Json.Linq;
using Automation.Protocol;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using static System.ComponentModel.TypeConverter;

namespace Automation.Bridge
{
    internal sealed partial class AutomationBridgeService
    {
        private JObject BuildOperationFields(OperationType op)
        {
            return WithOperationReadContext(op, () =>
            {
                RefreshOperationContext(op);
                JObject fields = new JObject();
                foreach (PropertyDescriptor descriptor in TypeDescriptor.GetProperties(op).Cast<PropertyDescriptor>())
                {
                    if (descriptor == null || !descriptor.IsBrowsable)
                    {
                        continue;
                    }
                    fields[descriptor.Name] = ConvertValueToToken(descriptor.GetValue(op));
                }
                return fields;
            });
        }

        private JObject BuildOperationSchema(OperationType op)
        {
            return WithOperationReadContext(op, () =>
            {
                RefreshOperationContext(op);
                JArray fields = new JArray();
                foreach (PropertyDescriptor descriptor in TypeDescriptor.GetProperties(op).Cast<PropertyDescriptor>())
                {
                    if (descriptor == null)
                    {
                        continue;
                    }

                    JObject behaviorRule = OperationBehaviorCatalog.BuildFieldRule(op, descriptor.Name);
                    if (!descriptor.IsBrowsable && behaviorRule == null)
                    {
                        continue;
                    }

                    JObject field = new JObject
                    {
                        ["key"] = descriptor.Name,
                        ["displayName"] = descriptor.DisplayName,
                        ["category"] = descriptor.Category,
                        ["description"] = descriptor.Description ?? string.Empty,
                        ["dataType"] = GetTypeLabel(descriptor.PropertyType),
                        ["jsonType"] = GetJsonTypeLabel(descriptor.PropertyType),
                        ["valueShape"] = GetFieldValueShape(descriptor),
                        ["visible"] = descriptor.IsBrowsable,
                        ["readOnly"] = descriptor.IsReadOnly,
                        ["referenceType"] = GetReferenceType(descriptor.Converter?.GetType().Name),
                        ["enumValues"] = BuildStandardValues(descriptor),
                        ["currentValue"] = ConvertValueToToken(descriptor.GetValue(op))
                    };

                    if (string.Equals(GetReferenceType(descriptor.Converter?.GetType().Name), "proc.goto", StringComparison.Ordinal))
                    {
                        field["format"] = "procIndex-stepIndex-opIndex";
                        field["example"] = "0-2-3";
                        field["allowDisplayText"] = false;
                        field["writeRule"] = "只能写三段式非负整数地址；下拉框显示的步骤名、指令名或“步骤：完成结束”等文字仅供界面展示，禁止作为字段值写入。";
                        field["required"] = op is ParamGoto
                            && (string.Equals(descriptor.Name, "TrueGoto", StringComparison.Ordinal)
                                || string.Equals(descriptor.Name, "FalseGoto", StringComparison.Ordinal));
                    }

                    if (behaviorRule != null)
                    {
                        foreach (JProperty ruleProperty in behaviorRule.Properties())
                        {
                            field[ruleProperty.Name] = ruleProperty.Value.DeepClone();
                        }
                    }

                    JObject itemSchema = BuildOperationListItemSchema(descriptor);
                    if (itemSchema != null)
                    {
                        field["itemSchema"] = itemSchema;
                    }

                    fields.Add(field);
                }

                return new JObject
                {
                    ["operaType"] = op.OperaType ?? string.Empty,
                    ["name"] = op.Name ?? string.Empty,
                    ["behavior"] = OperationBehaviorCatalog.BuildContract(op),
                    ["fields"] = fields
                };
            });
        }

        private JObject BuildOperationListItemSchema(PropertyDescriptor listDescriptor)
        {
            Type listType = listDescriptor?.PropertyType;
            if (listType == null || !listType.IsGenericType)
            {
                return null;
            }

            Type[] arguments = listType.GetGenericArguments();
            if (arguments.Length != 1 || arguments[0] == typeof(string) || arguments[0].IsPrimitive)
            {
                return null;
            }

            Type itemType = arguments[0];
            object item;
            try
            {
                item = Activator.CreateInstance(itemType);
            }
            catch
            {
                return null;
            }

            JArray itemFields = new JArray();
            foreach (PropertyDescriptor descriptor in TypeDescriptor.GetProperties(item).Cast<PropertyDescriptor>())
            {
                if (descriptor == null || !descriptor.IsBrowsable)
                {
                    continue;
                }

                string referenceType = GetReferenceType(descriptor.Converter?.GetType().Name);
                JObject field = new JObject
                {
                    ["key"] = descriptor.Name,
                    ["displayName"] = descriptor.DisplayName,
                    ["description"] = descriptor.Description ?? string.Empty,
                    ["jsonType"] = GetJsonTypeLabel(descriptor.PropertyType),
                    ["valueShape"] = GetFieldValueShape(descriptor),
                    ["referenceType"] = referenceType,
                    ["enumValues"] = BuildStandardValues(descriptor)
                };
                if (string.Equals(referenceType, "proc.goto", StringComparison.Ordinal))
                {
                    field["requiredWhen"] = itemType == typeof(GotoParam)
                        && string.Equals(descriptor.Name, "Goto", StringComparison.Ordinal)
                        ? (JToken)new JObject
                        {
                            ["anySiblingConfigured"] = new JArray("MatchValue", "MatchValueIndex", "MatchValueV")
                        }
                        : null;
                    field["format"] = "procIndex-stepIndex-opIndex";
                    field["allowDisplayText"] = false;
                    field["writeRule"] = "只能写三段式非负整数地址，禁止写步骤名、指令名或界面显示文字。";
                }
                itemFields.Add(field);
            }

            return new JObject
            {
                ["itemType"] = itemType.Name,
                ["fields"] = itemFields
            };
        }

        private static bool IsJumpOperation(OperationType operation)
        {
            return OperationGotoReferenceCatalog.HasBusinessGoto(operation);
        }

        private static string BuildFlowDescription(OperationType operation, int opIndex, int operationCount)
        {
            JObject controlFlow = OperationBehaviorCatalog.BuildContract(operation)?["controlFlow"] as JObject;
            if (controlFlow?["known"]?.Value<bool?>() == false)
            {
                return "控制流尚无确定契约";
            }
            if (controlFlow?["terminal"]?.Value<bool?>() == true)
            {
                return "执行后结束当前流程";
            }
            bool fallThrough = controlFlow?["fallThrough"]?.Value<bool?>() == true;
            bool hasGoto = IsJumpOperation(operation);
            if (hasGoto && fallThrough)
            {
                return opIndex < operationCount - 1
                    ? $"满足分支时跳转；未跳转时自动流向[{opIndex + 1}]"
                    : "满足分支时跳转；未跳转时步骤完成";
            }
            if (hasGoto)
            {
                return "由配置分支决定后续流向（不自动流向下一条）";
            }
            return opIndex < operationCount - 1 ? $"执行后自动流向[{opIndex + 1}]" : "执行后步骤完成";
        }

        private string BuildOperationSummary(OperationType op)
        {
            return WithOperationReadContext(op, () =>
            {
                RefreshOperationContext(op);
                List<string> parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(op.Name))
                {
                    parts.Add(op.Name);
                }
                if (!string.IsNullOrWhiteSpace(op.OperaType))
                {
                    parts.Add($"[{op.OperaType}]");
                }

                int count = 0;
                foreach (PropertyDescriptor descriptor in TypeDescriptor.GetProperties(op).Cast<PropertyDescriptor>())
                {
                    if (descriptor == null || !descriptor.IsBrowsable)
                    {
                        continue;
                    }
                    if (string.Equals(descriptor.Name, "Name", StringComparison.Ordinal)
                        || string.Equals(descriptor.Name, "OperaType", StringComparison.Ordinal)
                        || string.Equals(descriptor.Name, "Num", StringComparison.Ordinal)
                        || string.Equals(descriptor.Name, "Note", StringComparison.Ordinal)
                        || string.Equals(descriptor.Name, "Disable", StringComparison.Ordinal)
                        || string.Equals(descriptor.Name, "IsBreakpoint", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    object value = descriptor.GetValue(op);
                    if (value == null)
                    {
                        continue;
                    }

                    string text = ConvertFieldValueToText(value);
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        continue;
                    }

                    parts.Add($"{descriptor.DisplayName}={text}");
                    count++;
                    if (count >= 3)
                    {
                        break;
                    }
                }

                return string.Join("，", parts);
            });
        }

        private JArray BuildGotoTargets(int? procIndexFilter)
        {
            JArray targets = new JArray();
            if (runtime.Stores.Processes?.Items == null)
            {
                return targets;
            }

            for (int procIndex = 0; procIndex < runtime.Stores.Processes.Items.Count; procIndex++)
            {
                if (procIndexFilter.HasValue && procIndexFilter.Value != procIndex)
                {
                    continue;
                }

                Proc proc = runtime.Stores.Processes.Items[procIndex];
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
                        targets.Add(new JObject
                        {
                            ["key"] = $"{procIndex}-{stepIndex}-{opIndex}",
                            ["procIndex"] = procIndex,
                            ["stepIndex"] = stepIndex,
                            ["opIndex"] = opIndex,
                            ["procId"] = proc?.head?.Id.ToString("D"),
                            ["stepId"] = step?.Id.ToString("D"),
                            ["opId"] = op?.Id.ToString("D"),
                            ["procName"] = proc?.head?.Name ?? string.Empty,
                            ["stepName"] = step?.Name ?? string.Empty,
                            ["opName"] = op?.Name ?? string.Empty,
                            ["operaType"] = op?.OperaType ?? string.Empty
                        });
                    }
                }
            }

            return targets;
        }

    }
}
