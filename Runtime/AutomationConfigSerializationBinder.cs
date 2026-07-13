using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Automation
{
    /// <summary>
    /// 配置 JSON 的类型白名单。流程指令需要多态类型信息，但配置文件不得实例化 UI 或运行时对象。
    /// </summary>
    internal sealed class AutomationConfigSerializationBinder : ISerializationBinder
    {
        public static readonly AutomationConfigSerializationBinder Instance = new AutomationConfigSerializationBinder();

        private static readonly Assembly AutomationAssembly = typeof(Proc).Assembly;
        private static readonly HashSet<Type> AllowedGenericDefinitions = new HashSet<Type>
        {
            typeof(List<>), typeof(Dictionary<,>), typeof(HashSet<>),
            typeof(KeyValuePair<,>), typeof(Nullable<>), typeof(BindingList<>)
        };

        public Type BindToType(string assemblyName, string typeName)
        {
            EnsureAssemblyNameAllowed(assemblyName);
            foreach (Match match in Regex.Matches(typeName ?? string.Empty,
                @",\s*([A-Za-z_][A-Za-z0-9_.-]*)\s*(?=[,\]])"))
            {
                EnsureSimpleAssemblyNameAllowed(match.Groups[1].Value);
            }
            Type type = Type.GetType($"{typeName}, {assemblyName}", false);
            if (type == null || !IsAllowed(type))
            {
                throw new JsonSerializationException($"配置包含不允许的类型：{typeName}");
            }
            return type;
        }

        public void BindToName(Type serializedType, out string assemblyName, out string typeName)
        {
            if (serializedType == null || !IsAllowed(serializedType))
            {
                throw new JsonSerializationException($"不允许写入配置类型：{serializedType?.FullName}");
            }
            assemblyName = serializedType.Assembly.FullName;
            typeName = serializedType.FullName;
        }

        private static bool IsAllowed(Type type)
        {
            if (type.IsArray)
            {
                return IsAllowed(type.GetElementType());
            }
            if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal)
                || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan)
                || type == typeof(Guid) || type == typeof(Color))
            {
                return true;
            }
            if (type.IsGenericType)
            {
                Type definition = type.GetGenericTypeDefinition();
                if (definition.Assembly != AutomationAssembly && !AllowedGenericDefinitions.Contains(definition))
                {
                    return false;
                }
                return type.GetGenericArguments().All(IsAllowed);
            }
            if (type.Assembly != AutomationAssembly)
            {
                return false;
            }
            return !typeof(Control).IsAssignableFrom(type)
                && !typeof(Component).IsAssignableFrom(type)
                && !typeof(Delegate).IsAssignableFrom(type)
                && !typeof(MarshalByRefObject).IsAssignableFrom(type);
        }

        private static void EnsureAssemblyNameAllowed(string assemblyName)
        {
            try
            {
                EnsureSimpleAssemblyNameAllowed(new AssemblyName(assemblyName).Name);
            }
            catch (JsonSerializationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new JsonSerializationException($"配置类型程序集名称无效：{assemblyName}", ex);
            }
        }

        private static void EnsureSimpleAssemblyNameAllowed(string simpleName)
        {
            if (!string.Equals(simpleName, AutomationAssembly.GetName().Name, StringComparison.Ordinal)
                && !string.Equals(simpleName, "mscorlib", StringComparison.Ordinal)
                && !string.Equals(simpleName, "System", StringComparison.Ordinal)
                && !string.Equals(simpleName, "System.Core", StringComparison.Ordinal)
                && !string.Equals(simpleName, typeof(Color).Assembly.GetName().Name, StringComparison.Ordinal))
            {
                throw new JsonSerializationException($"配置引用了不允许的程序集：{simpleName}");
            }
        }
    }
}
