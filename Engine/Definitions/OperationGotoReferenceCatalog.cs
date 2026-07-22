using System;
// 模块：引擎 / 指令定义。
// 职责范围：维护原生指令类型、字段、行为和引用元数据的权威事实。

using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Automation
{
    public sealed class OperationGotoReference
    {
        public string Path { get; set; }
        public string FieldName { get; set; }
        public string DisplayName { get; set; }
        public string Value { get; set; }
        public object Container { get; set; }
        public bool IsAlarmField { get; set; }
    }

    /// <summary>
    /// 递归投影所有标记为跳转目标的字段，作为流程图和读取接口的公共跳转事实源。
    /// </summary>
    public static class OperationGotoReferenceCatalog
    {
        public static IReadOnlyList<OperationGotoReference> Enumerate(OperationType operation)
        {
            var result = new List<OperationGotoReference>();
            if (operation != null)
            {
                Visit(operation, operation, string.Empty, result, new HashSet<object>(ReferenceComparer.Instance), 0);
            }
            return result;
        }

        public static bool HasBusinessGoto(OperationType operation)
        {
            foreach (OperationGotoReference reference in Enumerate(operation))
            {
                if (!reference.IsAlarmField) return true;
            }
            return false;
        }

        private static void Visit(OperationType root, object current, string path,
            List<OperationGotoReference> result, HashSet<object> visited, int depth)
        {
            if (current == null || depth > 12 || !visited.Add(current)) return;
            foreach (PropertyInfo property in current.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!property.CanRead || property.GetIndexParameters().Length != 0) continue;
                object value;
                try { value = property.GetValue(current); }
                catch { continue; }
                string propertyPath = string.IsNullOrEmpty(path) ? property.Name : path + "." + property.Name;
                if (property.PropertyType == typeof(string)
                    && property.GetCustomAttribute<MarkedGotoAttribute>() != null)
                {
                    result.Add(new OperationGotoReference
                    {
                        Path = propertyPath,
                        FieldName = property.Name,
                        DisplayName = property.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName ?? property.Name,
                        Value = value as string,
                        Container = current,
                        IsAlarmField = ReferenceEquals(current, root)
                            && (property.Name == "Goto1" || property.Name == "Goto2" || property.Name == "Goto3")
                    });
                    continue;
                }
                if (value is IEnumerable enumerable && !(value is string))
                {
                    int index = 0;
                    foreach (object item in enumerable)
                    {
                        Visit(root, item, propertyPath + "[" + index++ + "]", result, visited, depth + 1);
                    }
                }
            }
        }

        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceComparer Instance = new ReferenceComparer();
            public new bool Equals(object x, object y) => ReferenceEquals(x, y);
            public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
        }
    }
}
