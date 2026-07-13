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
        public const int MaxListItems = 20;
        private const int MaxDepth = 6;

        private static readonly HashSet<string> OperationManagedProperties = new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(OperationType.Id), nameof(OperationType.Num), nameof(OperationType.Name),
            nameof(OperationType.OperaType), "Count", "IOCount", "OutIOCount", "CheckIOCount", "ProcCount"
        };

        public static JObject BuildContract(string operaType)
        {
            OperationType operation = OperationDefinitionRegistry.Create(RequireText(operaType, "operaType"));
            return new JObject
            {
                ["kind"] = "native.operation",
                ["operaType"] = operation.OperaType,
                ["purpose"] = "严格配置一个平台注册的原生指令；fields 只允许本契约列出的精确字段",
                ["behavior"] = OperationBehaviorCatalog.BuildContract(operation),
                ["guideTool"] = "语义或字段联动仍不明确时，按同一 operaType 调用 get_operation_guide",
                ["required"] = new JArray("kind", "operaType", "fields"),
                ["optional"] = new JArray("name"),
                ["fields"] = BuildObjectFields(operation.GetType(), 0),
                ["rules"] = new JArray(
                    "字段名区分大小写，未知字段直接拒绝",
                    "数组数量字段由编译器计算，禁止手工填写 Count/IOCount/ProcCount",
                    "跳转字段使用 {step,operation} 符号目标，禁止填写物理索引字符串",
                    $"单个嵌套数组最多 {MaxListItems} 项")
            };
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
            return operation;
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

        private static JObject BuildFieldSchema(PropertyInfo property, int depth)
        {
            var schema = new JObject
            {
                ["displayName"] = property.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName ?? property.Name,
                ["description"] = property.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty
            };
            if (property.GetCustomAttribute<MarkedGotoAttribute>() != null)
            {
                schema["jsonType"] = "object";
                schema["referenceType"] = "proc.goto.symbolic";
                schema["shape"] = new JObject { ["step"] = "步骤key", ["operation"] = "步骤内从0开始的指令索引" };
                return schema;
            }
            if (property.GetCustomAttribute<InlineListAttribute>() != null)
            {
                Type itemType = GetListItemType(property.PropertyType);
                schema["jsonType"] = "array";
                schema["maxItems"] = MaxListItems;
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
            JArray values = BuildStandardValues(GetPropertyDescriptor(property));
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
                ValidateStandardValue(GetPropertyDescriptor(property), converted, fieldPath);
                property.SetValue(target, converted);
            }
        }

        private static void ApplyList(object target, PropertyInfo property, JToken token, string path,
            AiOperationCompileContext context, int depth)
        {
            if (!(token is JArray array)) throw new InvalidOperationException($"{path} 必须是 JSON 数组。");
            if (array.Count > MaxListItems) throw new InvalidOperationException($"{path} 最多 {MaxListItems} 项。");
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
                throw new InvalidOperationException($"{path} 必须使用 {{step,operation}} 符号目标，禁止填写物理索引字符串。");
            JProperty unknown = value.Properties().FirstOrDefault(item =>
                !string.Equals(item.Name, "step", StringComparison.Ordinal)
                && !string.Equals(item.Name, "operation", StringComparison.Ordinal));
            if (unknown != null) throw new InvalidOperationException($"{path}.{unknown.Name} 不是允许字段。");
            if (value["step"]?.Type != JTokenType.String || value["operation"]?.Type != JTokenType.Integer)
                throw new InvalidOperationException($"{path} 必须提供字符串 step 和非负整数 operation。");
            return context.ResolveTarget(new Automation.Protocol.OperationTarget
            {
                Step = value["step"].Value<string>(),
                Operation = value["operation"].Value<int>()
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
