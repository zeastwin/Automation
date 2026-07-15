using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace Automation
{
    /// <summary>
    /// 将 native.operation 的递归 JSON 字段严格编译为平台注册的原生指令对象。
    /// 契约和编译共用同一套反射规则，避免 Schema 与实际写入能力分叉。
    /// </summary>
    public static class StructuredOperationCompiler
    {
        private const int MaxDepth = 6;

        private static readonly HashSet<string> OperationManagedProperties = new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(OperationType.Id), nameof(OperationType.AiKey), nameof(OperationType.Num), nameof(OperationType.Name),
            nameof(OperationType.OperaType), "Count", "IOCount", "OutIOCount", "CheckIOCount", "ProcCount"
        };

        public static JObject BuildContract(string operaType)
        {
            OperationType operation = OperationDefinitionRegistry.Create(RequireText(operaType, "operaType"));
            return new JObject
            {
                ["operaType"] = operation.OperaType,
                ["purpose"] = "严格配置一个平台注册的原生指令；fields 只允许本契约列出的精确字段",
                ["behavior"] = OperationBehaviorCatalog.BuildContract(operation),
                ["saveRequired"] = new JArray("operaType", "fields"),
                ["optional"] = new JArray("name"),
                ["fields"] = BuildObjectFields(operation.GetType(), 0),
                ["rules"] = new JArray(
                    "字段名区分大小写，未知字段直接拒绝",
                    "数组数量字段由编译器计算，禁止手工填写 Count/IOCount/ProcCount",
                    "operation.update 可通过 clearFields 显式清空顶层字符串字段；同一字段不得同时出现在 fields",
                    "operation.update 只修改同一原生类型；改变类型使用 operation.replace 原位替换",
                    "现有目标使用 {operationId}；当前步骤内按 key 定位使用 {operationKey}；跨步骤时再附加 stepId 或 stepKey；禁止填写物理索引字符串")
            };
        }

        /// <summary>
        /// 按 native.operation 的真实写入契约导出当前字段。
        /// 受管字段不会混入 fields；嵌套对象保持递归结构；跳转转换为稳定符号目标。
        /// </summary>
        public static JObject BuildWritableFields(
            OperationType operation,
            Func<string, JToken> resolveGotoTarget)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            if (resolveGotoTarget == null) throw new ArgumentNullException(nameof(resolveGotoTarget));
            return BuildWritableObject(operation, operation.GetType(), resolveGotoTarget, 0);
        }

        public static OperationType Compile(string operaType, IDictionary<string, object> fields,
            AiOperationCompileContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            OperationType operation = OperationDefinitionRegistry.Create(RequireText(operaType, "native.operation.operaType"));
            JObject fieldObject = fields == null ? null : JObject.FromObject(fields);
            if (fieldObject == null)
            {
                throw new InvalidOperationException("native.operation.fields 必须是对象。");
            }
            ApplyObject(operation, fieldObject, "native.operation.fields", context, 0);
            NormalizeAndValidateOperation(operation, fieldObject, false);
            return operation;
        }

        public static OperationType CompilePatch(OperationType existing, string operaType,
            IDictionary<string, object> fields, IEnumerable<string> clearFields,
            AiOperationCompileContext context)
        {
            if (existing == null) throw new ArgumentNullException(nameof(existing));
            string exactType = RequireText(operaType, "native.operation.operaType");
            if (!string.Equals(existing.OperaType, exactType, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"operation.update 的 operaType 必须保持为[{existing.OperaType}]；改变指令类型请删除后新增。");
            JObject fieldObject = fields == null ? null : JObject.FromObject(fields);
            if (fieldObject == null)
                throw new InvalidOperationException("native.operation.fields 必须是对象。");
            ApplyClearFields(existing.GetType(), fieldObject, clearFields);
            OperationType operation = ObjectGraphCloner.Clone(existing);
            ApplyObject(operation, fieldObject, "native.operation.fields", context, 0);
            NormalizeAndValidateOperation(operation, fieldObject, true);
            return operation;
        }

        private static void ApplyClearFields(Type operationType, JObject fields,
            IEnumerable<string> clearFields)
        {
            var properties = GetConfigurableProperties(operationType)
                .ToDictionary(property => property.Name, StringComparer.Ordinal);
            foreach (string rawName in clearFields ?? Enumerable.Empty<string>())
            {
                string name = rawName?.Trim();
                if (string.IsNullOrEmpty(name))
                    throw new InvalidOperationException("native.operation.clearFields 不能包含空字段名。");
                if (!properties.TryGetValue(name, out PropertyInfo property))
                    throw new InvalidOperationException($"native.operation.clearFields 包含未知或受管字段：{name}。");
                if (property.PropertyType != typeof(string))
                    throw new InvalidOperationException(
                        $"native.operation.clearFields 目前只允许清空顶层字符串字段：{name}。");
                if (fields.Property(name, StringComparison.Ordinal) != null)
                    throw new InvalidOperationException(
                        $"native.operation 字段 {name} 不能同时出现在 fields 和 clearFields。");
                fields[name] = JValue.CreateNull();
            }
        }

        private static void NormalizeAndValidateOperation(
            OperationType operation, JObject fields, bool preserveUnspecified)
        {
            if (!(operation is Goto jump)) return;

            // Goto 构造函数为 PropertyGrid 预放了一个空分支；结构化输入未提供 Params 时不能把该占位项带入运行时。
            if (!preserveUnspecified
                && fields.Property(nameof(Goto.Params), StringComparison.Ordinal) == null)
            {
                jump.Params = new OperationTypePartial.CustomList<GotoParam>();
                jump.Count = "0";
            }
            if (jump.Params == null || jump.Params.Count == 0)
            {
                return;
            }

            for (int index = 0; index < jump.Params.Count; index++)
            {
                GotoParam item = jump.Params[index];
                bool hasLiteral = !string.IsNullOrWhiteSpace(item?.MatchValue);
                bool hasReference = !string.IsNullOrWhiteSpace(item?.MatchValueIndex)
                    || !string.IsNullOrWhiteSpace(item?.MatchValueV);
                if (hasLiteral && hasReference)
                    throw new InvalidOperationException(
                        $"native.operation.fields.Params[{index}]不能同时提供固定匹配值和匹配值变量。");
            }
        }

        private static JObject BuildObjectFields(Type type, int depth)
        {
            if (depth > MaxDepth) throw new InvalidOperationException($"指令结构嵌套超过 {MaxDepth} 层：{type.Name}");
            var fields = new JObject();
            foreach (PropertyInfo property in GetConfigurableProperties(type))
            {
                JObject schema = BuildFieldSchema(property, depth + 1);
                fields[property.Name] = schema;
            }
            return fields;
        }

        private static JObject BuildWritableObject(
            object source,
            Type type,
            Func<string, JToken> resolveGotoTarget,
            int depth)
        {
            if (depth > MaxDepth) throw new InvalidOperationException($"指令结构嵌套超过 {MaxDepth} 层：{type.Name}");
            var fields = new JObject();
            foreach (PropertyInfo property in GetConfigurableProperties(type))
            {
                object value = property.GetValue(source);
                if (property.GetCustomAttribute<MarkedGotoAttribute>() != null)
                {
                    if (value == null)
                    {
                        fields[property.Name] = JValue.CreateNull();
                    }
                    else
                    {
                        JToken target = resolveGotoTarget(value.ToString());
                        if (target != null) fields[property.Name] = target;
                    }
                    continue;
                }
                if (property.GetCustomAttribute<InlineListAttribute>() != null)
                {
                    var items = new JArray();
                    if (value is IEnumerable values)
                    {
                        Type itemType = GetListItemType(property.PropertyType);
                        foreach (object item in values)
                        {
                            if (item != null)
                            {
                                items.Add(BuildWritableObject(item, itemType, resolveGotoTarget, depth + 1));
                            }
                        }
                    }
                    fields[property.Name] = items;
                    continue;
                }
                if (property.GetCustomAttribute<InlineGroupAttribute>() != null)
                {
                    if (value != null)
                    {
                        fields[property.Name] = BuildWritableObject(
                            value, property.PropertyType, resolveGotoTarget, depth + 1);
                    }
                    continue;
                }
                fields[property.Name] = ConvertWritableScalar(value);
            }
            return fields;
        }

        private static JToken ConvertWritableScalar(object value)
        {
            if (value == null) return JValue.CreateNull();
            if (value is Color color)
            {
                return new JValue($"#{color.R:X2}{color.G:X2}{color.B:X2}");
            }
            Type type = value.GetType();
            if (type.IsEnum || value is char || value is Guid)
            {
                return new JValue(value.ToString());
            }
            return JToken.FromObject(value);
        }

        private static JObject BuildFieldSchema(PropertyInfo property, int depth)
        {
            var schema = new JObject
            {
                ["displayName"] = property.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName ?? property.Name,
                ["description"] = property.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty
            };
            if (property.GetCustomAttribute<MarkedGotoAttribute>() != null)
            {
                schema["description"] = "符号跳转目标；Bridge 在最终结构上解析并重算物理地址。";
                schema["jsonType"] = "object";
                schema["referenceType"] = "proc.goto.symbolic";
                schema["shapes"] = new JArray
                {
                    new JObject { ["operationId"] = "现有目标指令Guid" },
                    new JObject { ["operationKey"] = "当前步骤内目标指令key" },
                    new JObject { ["stepId"] = "现有步骤Guid", ["operationKey"] = "目标指令key" },
                    new JObject { ["stepKey"] = "步骤key", ["operationKey"] = "目标指令key" }
                };
                return schema;
            }
            if (property.GetCustomAttribute<InlineListAttribute>() != null)
            {
                Type itemType = GetListItemType(property.PropertyType);
                schema["jsonType"] = "array";
                schema["items"] = new JObject
                {
                    ["jsonType"] = "object",
                    ["fields"] = BuildObjectFields(itemType, depth)
                };
                return schema;
            }
            if (property.GetCustomAttribute<InlineGroupAttribute>() != null)
            {
                schema["jsonType"] = "object";
                schema["fields"] = BuildObjectFields(property.PropertyType, depth);
                return schema;
            }

            Type valueType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            schema["jsonType"] = GetJsonType(valueType);
            string referenceType = GetReferenceType(GetPropertyDescriptor(property)?.Converter?.GetType().Name);
            if (!string.IsNullOrEmpty(referenceType)) schema["referenceType"] = referenceType;
            if (valueType == typeof(Color)) schema["format"] = "#RRGGBB 或 .NET 已知颜色名";
            if (valueType == typeof(char)) schema["length"] = 1;
            if (valueType.IsEnum) schema["values"] = new JArray(Enum.GetNames(valueType));
            JArray values = string.IsNullOrEmpty(referenceType)
                ? BuildStandardValues(GetPropertyDescriptor(property))
                : new JArray();
            if (values.Count > 0) schema["values"] = values;
            return schema;
        }

        private static void ApplyObject(object target, JObject values, string path,
            AiOperationCompileContext context, int depth)
        {
            if (depth > MaxDepth) throw new InvalidOperationException($"{path} 嵌套超过 {MaxDepth} 层。");
            Dictionary<string, PropertyInfo> properties = GetConfigurableProperties(target.GetType())
                .ToDictionary(property => property.Name, StringComparer.Ordinal);
            foreach (JProperty input in values.Properties())
            {
                if (!properties.TryGetValue(input.Name, out PropertyInfo property))
                {
                    throw new InvalidOperationException($"{path}.{input.Name} 不是允许字段；字段名区分大小写。");
                }
                string fieldPath = path + "." + property.Name;
                if (property.GetCustomAttribute<MarkedGotoAttribute>() != null)
                {
                    property.SetValue(target, ResolveSymbolicTarget(input.Value, fieldPath, context));
                    continue;
                }
                if (property.GetCustomAttribute<InlineListAttribute>() != null)
                {
                    ApplyList(target, property, input.Value, fieldPath, context, depth + 1);
                    continue;
                }
                if (property.GetCustomAttribute<InlineGroupAttribute>() != null)
                {
                    if (!(input.Value is JObject groupValues))
                        throw new InvalidOperationException($"{fieldPath} 必须是 JSON 对象。");
                    object group = property.GetValue(target) ?? Activator.CreateInstance(property.PropertyType);
                    ApplyObject(group, groupValues, fieldPath, context, depth + 1);
                    property.SetValue(target, group);
                    continue;
                }
                object converted = ConvertScalar(input.Value, property.PropertyType, fieldPath);
                PropertyDescriptor descriptor = GetPropertyDescriptor(property);
                context.ValidateReference(GetReferenceType(descriptor?.Converter?.GetType().Name), converted, fieldPath);
                ValidateStandardValue(descriptor, converted, fieldPath);
                property.SetValue(target, converted);
            }
        }

        private static void ApplyList(object target, PropertyInfo property, JToken token, string path,
            AiOperationCompileContext context, int depth)
        {
            if (!(token is JArray array)) throw new InvalidOperationException($"{path} 必须是 JSON 数组。");
            Type itemType = GetListItemType(property.PropertyType);
            IList list = (IList)Activator.CreateInstance(property.PropertyType);
            for (int index = 0; index < array.Count; index++)
            {
                if (!(array[index] is JObject itemValues))
                    throw new InvalidOperationException($"{path}[{index}] 必须是 JSON 对象。");
                object item = Activator.CreateInstance(itemType);
                ApplyObject(item, itemValues, $"{path}[{index}]", context, depth + 1);
                list.Add(item);
            }
            property.SetValue(target, list);
            string countPropertyName = GetCountPropertyName(target.GetType(), property.Name);
            PropertyInfo countProperty = target.GetType().GetProperty(countPropertyName,
                BindingFlags.Instance | BindingFlags.Public);
            countProperty?.SetValue(target, array.Count.ToString(CultureInfo.InvariantCulture));
        }

        private static string ResolveSymbolicTarget(JToken token, string path, AiOperationCompileContext context)
        {
            if (token.Type == JTokenType.Null) return null;
            if (!(token is JObject value))
                throw new InvalidOperationException(
                    $"{path} 必须使用 {{operationId}}、{{operationKey}}、{{stepId,operationKey}} 或 {{stepKey,operationKey}} 符号目标，禁止填写物理索引字符串。");
            JProperty unknown = value.Properties().FirstOrDefault(item =>
                !string.Equals(item.Name, "stepId", StringComparison.Ordinal)
                && !string.Equals(item.Name, "stepKey", StringComparison.Ordinal)
                && !string.Equals(item.Name, "operationId", StringComparison.Ordinal)
                && !string.Equals(item.Name, "operationKey", StringComparison.Ordinal));
            if (unknown != null) throw new InvalidOperationException($"{path}.{unknown.Name} 不是允许字段。");
            if (value["stepId"] != null && value["stepId"].Type != JTokenType.String
                || value["stepKey"] != null && value["stepKey"].Type != JTokenType.String
                || value["operationId"] != null && value["operationId"].Type != JTokenType.String
                || value["operationKey"] != null && value["operationKey"].Type != JTokenType.String)
            {
                throw new InvalidOperationException($"{path} 的符号目标字段类型无效。");
            }
            return context.ResolveTarget(new Automation.Protocol.OperationTarget
            {
                StepId = value["stepId"]?.Value<string>(),
                StepKey = value["stepKey"]?.Value<string>(),
                OperationId = value["operationId"]?.Value<string>(),
                OperationKey = value["operationKey"]?.Value<string>()
            }, path);
        }

        private static object ConvertScalar(JToken token, Type targetType, string path)
        {
            Type actualType = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (token.Type == JTokenType.Null)
            {
                if (!targetType.IsValueType || Nullable.GetUnderlyingType(targetType) != null) return null;
                throw new InvalidOperationException($"{path} 不允许 null。");
            }
            try
            {
                if (actualType == typeof(string))
                {
                    if (token.Type != JTokenType.String) throw new InvalidOperationException($"{path} 必须是 JSON 字符串。");
                    return token.Value<string>();
                }
                if (actualType == typeof(char))
                {
                    if (token.Type != JTokenType.String || token.Value<string>()?.Length != 1)
                        throw new InvalidOperationException($"{path} 必须是长度为1的 JSON 字符串。");
                    return token.Value<string>()[0];
                }
                if (actualType == typeof(bool))
                {
                    if (token.Type != JTokenType.Boolean) throw new InvalidOperationException($"{path} 必须是 JSON 布尔值。");
                    return token.Value<bool>();
                }
                if (actualType == typeof(int) || actualType == typeof(long))
                {
                    if (token.Type != JTokenType.Integer) throw new InvalidOperationException($"{path} 必须是 JSON 整数。");
                    return Convert.ChangeType(((JValue)token).Value, actualType, CultureInfo.InvariantCulture);
                }
                if (actualType == typeof(float) || actualType == typeof(double) || actualType == typeof(decimal))
                {
                    if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)
                        throw new InvalidOperationException($"{path} 必须是 JSON 数值。");
                    return Convert.ChangeType(((JValue)token).Value, actualType, CultureInfo.InvariantCulture);
                }
                if (actualType == typeof(Guid))
                {
                    if (token.Type != JTokenType.String || !Guid.TryParse(token.Value<string>(), out Guid guid))
                        throw new InvalidOperationException($"{path} 必须是有效 GUID 字符串。");
                    return guid;
                }
                if (actualType == typeof(Color)) return ParseColor(token, path);
                if (actualType.IsEnum)
                {
                    if (token.Type != JTokenType.String || !Enum.GetNames(actualType).Contains(token.Value<string>(), StringComparer.Ordinal))
                        throw new InvalidOperationException($"{path} 必须是枚举 {string.Join("/", Enum.GetNames(actualType))} 之一。");
                    return Enum.Parse(actualType, token.Value<string>(), false);
                }
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"{path} 值转换失败：{ex.Message}", ex);
            }
            throw new InvalidOperationException($"{path} 使用了不支持的字段类型：{actualType.Name}");
        }

        private static Color ParseColor(JToken token, string path)
        {
            if (token.Type != JTokenType.String) throw new InvalidOperationException($"{path} 必须是颜色字符串。");
            string value = token.Value<string>();
            if (!string.IsNullOrEmpty(value) && value.Length == 7 && value[0] == '#'
                && int.TryParse(value.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
            {
                return Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
            }
            Color named = Color.FromName(value ?? string.Empty);
            if (named.IsKnownColor) return named;
            throw new InvalidOperationException($"{path} 必须是 #RRGGBB 或 .NET 已知颜色名。");
        }

        private static IEnumerable<PropertyInfo> GetConfigurableProperties(Type type)
        {
            return type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property => property.CanRead && property.CanWrite
                    && property.SetMethod != null && property.SetMethod.IsPublic
                    && property.GetIndexParameters().Length == 0
                    && !(typeof(OperationType).IsAssignableFrom(type)
                        && OperationManagedProperties.Contains(property.Name)))
                .OrderBy(property => property.MetadataToken);
        }

        private static Type GetListItemType(Type listType)
        {
            if (!typeof(IList).IsAssignableFrom(listType) || !listType.IsGenericType)
                throw new InvalidOperationException($"不支持的嵌套列表类型：{listType.Name}");
            return listType.GetGenericArguments()[0];
        }

        private static string GetCountPropertyName(Type ownerType, string listName)
        {
            if (listName == "IoParams") return "IOCount";
            if (listName == "OutIoParams") return "OutIOCount";
            if (listName == "CheckIoParams") return "CheckIOCount";
            if (listName == "procParams" || ownerType == typeof(WaitProc)) return "ProcCount";
            return "Count";
        }

        private static PropertyDescriptor GetPropertyDescriptor(PropertyInfo property)
        {
            return TypeDescriptor.GetProperties(property.DeclaringType)[property.Name];
        }

        private static void ValidateStandardValue(PropertyDescriptor descriptor, object value, string path)
        {
            if (descriptor?.Converter == null || value == null) return;
            try
            {
                if (!descriptor.Converter.GetStandardValuesSupported(null)
                    || !descriptor.Converter.GetStandardValuesExclusive(null)) return;
                TypeConverter.StandardValuesCollection values = descriptor.Converter.GetStandardValues(null);
                if (values == null || values.Count == 0) return;
                foreach (object allowed in values)
                {
                    if (Equals(allowed, value) || string.Equals(allowed?.ToString(), value.ToString(), StringComparison.Ordinal)) return;
                }
                throw new InvalidOperationException($"{path} 取值不在允许列表中。");
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch
            {
                // 某些候选值依赖尚未初始化的运行时资源；引用完整性在变更集总体验证阶段检查。
            }
        }

        private static JArray BuildStandardValues(PropertyDescriptor descriptor)
        {
            var result = new JArray();
            try
            {
                if (descriptor?.Converter == null || !descriptor.Converter.GetStandardValuesSupported(null)) return result;
                TypeConverter.StandardValuesCollection values = descriptor.Converter.GetStandardValues(null);
                if (values == null || values.Count > 30) return result;
                foreach (object value in values) if (value != null) result.Add(value.ToString());
            }
            catch
            {
            }
            return result;
        }

        private static string GetJsonType(Type type)
        {
            if (type == typeof(string) || type == typeof(char) || type == typeof(Guid)
                || type == typeof(Color) || type.IsEnum) return "string";
            if (type == typeof(bool)) return "boolean";
            if (type == typeof(int) || type == typeof(long)) return "integer";
            if (type == typeof(float) || type == typeof(double) || type == typeof(decimal)) return "number";
            return "object";
        }

        private static string GetReferenceType(string converterTypeName)
        {
            switch (converterTypeName)
            {
                case "IoOutItem": return "io.output";
                case "IoInItem": return "io.input";
                case "IoItem": return "io.all";
                case "AlarmInfoItem": return "alarm.infoId";
                case "DataStItem": return "dataStruct";
                case "ProcItem": return "proc";
                case "CommItem": return "comm.all";
                case "ValueItem": return "value";
                case "TcpItem": return "comm.tcp";
                case "SerialPortItem": return "comm.serial";
                case "PlcItem": return "plc.device";
                case "StationtItem":
                case "SetStationVelItem": return "station";
                case "StationPosDic":
                case "StationPosWithSpecial": return "station.position";
                case "StationAixsItem": return "station.axis";
                case "funcNameItem": return "customFunc";
                default: return string.Empty;
            }
        }

        private static string RequireText(string value, string path)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException($"{path} 不能为空。");
            return value.Trim();
        }
    }
}
