using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Automation.ToolCli
{
    /// <summary>命令行工具声明：工具名来自本特性，描述来自 DescriptionAttribute。</summary>
    [AttributeUsage(AttributeTargets.Method)]
    internal sealed class AiToolAttribute : Attribute
    {
        public string? Name { get; set; }
    }

    /// <summary>一个 AI 命令行工具：名称、描述、强类型参数 Schema 与实现方法。</summary>
    internal sealed class AiTool
    {
        public required string Name { get; init; }

        public string? Description { get; init; }

        public JsonElement InputSchema { get; set; }

        public required MethodInfo Method { get; init; }
    }

    /// <summary>
    /// 从工具方法签名生成 JSON Schema：属性名 camelCase，描述取自 DescriptionAttribute；
    /// 无默认值的参数进入 required；可空标注与可空值类型的 type 追加 "null"；
    /// 同一文档内重复出现的同一复杂类型（描述相同）以 $ref 指向首次出现位置。
    /// </summary>
    internal static class AiToolSchemaFactory
    {
        public static AiTool Create(MethodInfo method)
        {
            AiToolAttribute attribute = method.GetCustomAttribute<AiToolAttribute>()
                ?? throw new InvalidOperationException($"工具方法缺少 AiTool 声明：{method.Name}");
            string name = string.IsNullOrWhiteSpace(attribute.Name) ? method.Name : attribute.Name!;
            return new AiTool
            {
                Name = name,
                Description = method.GetCustomAttribute<DescriptionAttribute>()?.Description,
                Method = method,
                InputSchema = JsonSerializer.SerializeToElement(BuildMethodSchema(method))
            };
        }

        /// <summary>单份 Schema 文档的生成状态：记录复杂类型首次出现位置供 $ref 复用。</summary>
        private sealed class SchemaBuildContext
        {
            public Dictionary<string, string> FirstPaths { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
        }

        private static JsonObject BuildMethodSchema(MethodInfo method)
        {
            var context = new SchemaBuildContext();
            var root = new JsonObject { ["type"] = "object" };
            var properties = new JsonObject();
            var required = new JsonArray();
            var nullabilityContext = new NullabilityInfoContext();
            foreach (ParameterInfo parameter in method.GetParameters())
            {
                string propertyName = JsonNamingPolicy.CamelCase.ConvertName(parameter.Name!);
                properties[propertyName] = BuildParameterSchema(
                    parameter,
                    nullabilityContext.Create(parameter),
                    "#/properties/" + propertyName,
                    context);
                if (!parameter.HasDefaultValue)
                {
                    required.Add(propertyName);
                }
            }
            root["properties"] = properties;
            if (required.Count > 0)
            {
                root["required"] = required;
            }
            return root;
        }

        private static JsonObject BuildParameterSchema(
            ParameterInfo parameter,
            NullabilityInfo nullability,
            string path,
            SchemaBuildContext context)
        {
            string? description = ReadDescription(parameter.GetCustomAttribute<DescriptionAttribute>());
            bool nullable = IsNullable(parameter.ParameterType, nullability.ReadState);
            JsonObject schema = BuildTypeSchema(
                parameter.ParameterType, new HashSet<Type>(), path, context, description, nullable,
                nullDeep: true);
            if (parameter.HasDefaultValue)
            {
                schema["default"] = JsonSerializer.SerializeToNode(parameter.DefaultValue);
            }
            return WithDescription(schema, description);
        }

        private static JsonObject BuildPropertySchema(
            PropertyInfo property,
            NullabilityInfo nullability,
            string path,
            SchemaBuildContext context)
        {
            string? description = ReadDescription(property.GetCustomAttribute<DescriptionAttribute>());
            bool nullable = IsNullable(property.PropertyType, nullability.ReadState);
            JsonObject schema = BuildTypeSchema(
                property.PropertyType, new HashSet<Type>(), path, context, description, nullable,
                nullDeep: false);
            return WithDescription(schema, description);
        }

        private static string? ReadDescription(DescriptionAttribute? attribute)
        {
            return attribute == null || string.IsNullOrWhiteSpace(attribute.Description)
                ? null
                : attribute.Description;
        }

        private static JsonObject WithDescription(JsonObject schema, string? description)
        {
            if (description == null || schema.ContainsKey("$ref"))
            {
                return schema;
            }
            var result = new JsonObject { ["description"] = description };
            foreach (KeyValuePair<string, JsonNode?> pair in schema)
            {
                result[pair.Key] = pair.Value?.DeepClone();
            }
            return result;
        }

        private static JsonObject BuildTypeSchema(
            Type type,
            HashSet<Type> expansionStack,
            string path,
            SchemaBuildContext context,
            string? description,
            bool nullable,
            bool nullDeep)
        {
            Type? nullableUnderlying = Nullable.GetUnderlyingType(type);
            if (nullableUnderlying != null)
            {
                return BuildTypeSchema(
                    nullableUnderlying, expansionStack, path, context, description, true, nullDeep);
            }
            JsonObject schema;
            if (type == typeof(string))
            {
                schema = new JsonObject { ["type"] = "string" };
            }
            else if (type == typeof(bool))
            {
                schema = new JsonObject { ["type"] = "boolean" };
            }
            else if (type == typeof(double) || type == typeof(float) || type == typeof(decimal))
            {
                schema = new JsonObject { ["type"] = "number" };
            }
            else if (type == typeof(int) || type == typeof(uint) || type == typeof(long)
                || type == typeof(ulong) || type == typeof(short) || type == typeof(ushort)
                || type == typeof(byte) || type == typeof(sbyte))
            {
                schema = new JsonObject { ["type"] = "integer" };
            }
            else if (type.IsEnum)
            {
                schema = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = new JsonArray(
                        Enum.GetNames(type).Select(name => JsonValue.Create(name)).ToArray())
                };
            }
            else if (IsDictionary(type))
            {
                // 字典值为动态 JSON，不约束值结构。
                schema = new JsonObject { ["type"] = "object" };
            }
            else if (GetCollectionElementType(type) is Type elementType)
            {
                schema = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = BuildTypeSchema(
                        elementType, expansionStack, path + "/items", context, null, false, false)
                };
            }
            else
            {
                return BuildObjectSchema(type, expansionStack, path, context, description, nullable);
            }
            if (nullable)
            {
                // 参数级可空整棵子树（含数组元素）都允许 null；属性级可空只在根上允许 null。
                if (nullDeep)
                {
                    ApplyNullDeep(schema);
                }
                else
                {
                    ApplyNullAtRoot(schema);
                }
            }
            return schema;
        }

        private static JsonObject BuildObjectSchema(
            Type type,
            HashSet<Type> expansionStack,
            string path,
            SchemaBuildContext context,
            string? description,
            bool nullable)
        {
            // 同一文档内同一类型且描述相同的复杂节点，后续出现处以 $ref 指向首次位置。
            string cacheKey = type.FullName + "\n" + (description ?? string.Empty) + "\n" + nullable;
            if (context.FirstPaths.TryGetValue(cacheKey, out string? firstPath))
            {
                return new JsonObject { ["$ref"] = firstPath };
            }
            if (!expansionStack.Add(type))
            {
                throw new InvalidOperationException($"参数类型存在循环引用，无法展开 Schema：{type.FullName}");
            }
            var schema = new JsonObject { ["type"] = "object" };
            context.FirstPaths[cacheKey] = path;
            try
            {
                var properties = new JsonObject();
                var nullabilityContext = new NullabilityInfoContext();
                foreach (PropertyInfo property in type.GetProperties(
                    BindingFlags.Public | BindingFlags.Instance))
                {
                    if (property.GetIndexParameters().Length > 0
                        || property.GetCustomAttribute<JsonIgnoreAttribute>() != null)
                    {
                        continue;
                    }
                    string propertyName = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                        ?? JsonNamingPolicy.CamelCase.ConvertName(property.Name);
                    properties[propertyName] = BuildPropertySchema(
                        property,
                        nullabilityContext.Create(property),
                        path + "/properties/" + propertyName,
                        context);
                }
                schema["properties"] = properties;
            }
            finally
            {
                expansionStack.Remove(type);
            }
            if (nullable)
            {
                ApplyNullAtRoot(schema);
            }
            return schema;
        }

        private static Type? GetCollectionElementType(Type type)
        {
            if (type == typeof(string))
            {
                return null;
            }
            if (type.IsArray)
            {
                return type.GetElementType();
            }
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                return type.GetGenericArguments()[0];
            }
            return type.GetInterfaces()
                .Where(item => item.IsGenericType
                    && item.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                .Select(item => (Type?)item.GetGenericArguments()[0])
                .FirstOrDefault();
        }

        private static bool IsDictionary(Type type)
        {
            return typeof(System.Collections.IDictionary).IsAssignableFrom(type)
                || type.GetInterfaces().Any(item => item.IsGenericType
                    && item.GetGenericTypeDefinition() == typeof(IDictionary<,>));
        }

        private static bool IsNullable(Type type, NullabilityState readState)
        {
            if (Nullable.GetUnderlyingType(type) != null)
            {
                return true;
            }
            // 值类型不可空；引用类型只有明确非空（NotNull）才不允许 null，
            // 可空标注（Nullable）与未启用可空上下文的程序集（Unknown）都按可空生成。
            return !type.IsValueType && readState != NullabilityState.NotNull;
        }

        private static void ApplyNullAtRoot(JsonObject schema)
        {
            if (schema["type"] is JsonValue value
                && value.TryGetValue(out string? typeName))
            {
                schema["type"] = new JsonArray(JsonValue.Create(typeName), JsonValue.Create("null"));
            }
        }

        private static void ApplyNullDeep(JsonNode node)
        {
            if (node is JsonObject obj)
            {
                ApplyNullAtRoot(obj);
                foreach (KeyValuePair<string, JsonNode?> pair in obj.ToList())
                {
                    if (pair.Value != null)
                    {
                        ApplyNullDeep(pair.Value);
                    }
                }
            }
            else if (node is JsonArray array)
            {
                foreach (JsonNode? item in array)
                {
                    if (item != null)
                    {
                        ApplyNullDeep(item);
                    }
                }
            }
        }
    }
}
