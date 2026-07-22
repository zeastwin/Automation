using System;
// 模块：引擎 / 指令定义。
// 职责范围：维护原生指令类型、字段、行为和引用元数据的权威事实。

using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace Automation
{
    public enum VariableReferenceKind
    {
        Name,
        Index
    }

    public sealed class VariableReferenceRecord
    {
        public VariableReferenceKind Kind { get; set; }
        public string Path { get; set; }
        public string Value { get; set; }
        public bool IsIndirect { get; set; }
        internal object Owner { get; set; }
        internal PropertyDescriptor Property { get; set; }

        public bool TrySetValue(string value)
        {
            if (Owner == null || Property == null || Property.IsReadOnly) return false;
            try
            {
                object converted = Property.PropertyType == typeof(string)
                    ? (object)value
                    : TypeDescriptor.GetConverter(Property.PropertyType)
                        .ConvertFromInvariantString(value);
                Property.SetValue(Owner, converted);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// 从原生指令属性元数据投影变量引用，供就绪检查、诊断和复制共用。
    /// </summary>
    public static class VariableReferenceCatalog
    {
        public static IReadOnlyList<VariableReferenceRecord> Enumerate(OperationType operation)
        {
            var result = new List<VariableReferenceRecord>();
            AddReferences(operation, string.Empty, 0, new List<object>(), result);
            return result;
        }

        private static void AddReferences(
            object value,
            string path,
            int depth,
            IList<object> visited,
            ICollection<VariableReferenceRecord> result)
        {
            if (value == null || depth > 6 || visited.Any(item => ReferenceEquals(item, value)))
            {
                return;
            }
            visited.Add(value);
            foreach (PropertyDescriptor property in TypeDescriptor.GetProperties(value).Cast<PropertyDescriptor>())
            {
                bool browsable = property?.IsBrowsable == true;
                if (property != null && value is IPropertyVisibilityProvider visibilityProvider)
                {
                    browsable = visibilityProvider.IsPropertyVisible(property.Name, browsable);
                }
                if (property == null || !browsable)
                {
                    continue;
                }
                object fieldValue;
                try
                {
                    fieldValue = property.GetValue(value);
                }
                catch
                {
                    continue;
                }
                string fieldPath = string.IsNullOrEmpty(path)
                    ? property.Name
                    : path + "." + property.Name;
                if (fieldValue is string text && !string.IsNullOrWhiteSpace(text))
                {
                    if (property.Converter?.GetType() == typeof(OperationTypePartial.ValueItem))
                    {
                        result.Add(new VariableReferenceRecord
                        {
                            Kind = VariableReferenceKind.Name,
                            Path = fieldPath,
                            Value = text.Trim(),
                            IsIndirect = IsIndirectProperty(property),
                            Owner = value,
                            Property = property
                        });
                    }
                    else if (IsVariableIndexProperty(property))
                    {
                        result.Add(new VariableReferenceRecord
                        {
                            Kind = VariableReferenceKind.Index,
                            Path = fieldPath,
                            Value = text.Trim(),
                            IsIndirect = IsIndirectProperty(property),
                            Owner = value,
                            Property = property
                        });
                    }
                    continue;
                }
                if (fieldValue is int numericIndex && IsVariableIndexProperty(property))
                {
                    result.Add(new VariableReferenceRecord
                    {
                        Kind = VariableReferenceKind.Index,
                        Path = fieldPath,
                        Value = numericIndex.ToString(),
                        IsIndirect = IsIndirectProperty(property),
                        Owner = value,
                        Property = property
                    });
                    continue;
                }
                if (fieldValue == null || fieldValue is string || depth >= 6)
                {
                    continue;
                }
                if (fieldValue is IEnumerable items)
                {
                    int index = 0;
                    foreach (object item in items)
                    {
                        if (item != null && !IsSimple(item.GetType()))
                        {
                            AddReferences(item, $"{fieldPath}[{index}]", depth + 1, visited, result);
                        }
                        index++;
                    }
                }
                else if (!IsSimple(fieldValue.GetType()))
                {
                    AddReferences(fieldValue, fieldPath, depth + 1, visited, result);
                }
            }
            visited.Remove(value);
        }

        private static bool IsVariableIndexProperty(PropertyDescriptor property)
        {
            string displayName = property.DisplayName ?? string.Empty;
            string description = property.Description ?? string.Empty;
            return displayName.IndexOf("变量", StringComparison.Ordinal) >= 0
                && displayName.IndexOf("索引", StringComparison.Ordinal) >= 0
                || description.IndexOf("变量索引", StringComparison.Ordinal) >= 0;
        }

        private static bool IsIndirectProperty(PropertyDescriptor property)
        {
            string text = (property.Name ?? string.Empty) + " "
                + (property.DisplayName ?? string.Empty) + " "
                + (property.Description ?? string.Empty);
            return text.IndexOf("二级", StringComparison.Ordinal) >= 0
                || text.IndexOf("2Index", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("FromIndex", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsSimple(Type type)
        {
            Type actual = Nullable.GetUnderlyingType(type) ?? type;
            return actual.IsPrimitive || actual.IsEnum || actual == typeof(string)
                || actual == typeof(decimal) || actual == typeof(DateTime)
                || actual == typeof(Guid);
        }
    }
}
