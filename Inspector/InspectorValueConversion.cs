using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Automation
{
    internal sealed class InspectorStandardValue
    {
        public InspectorStandardValue(object value, string text)
        {
            Value = value;
            Text = text;
        }

        public object Value { get; }
        public string Text { get; }
        public override string ToString() => Text;
    }

    /// <summary>
    /// 检查器字段写入的统一边界：负责转换、相等判断和反射异常解包。
    /// 控件只决定何时提交以及如何显示错误。
    /// </summary>
    internal static class InspectorFieldValueService
    {
        public static bool TryConvertAndSetScalar(
            InspectorScalarFieldDefinition definition,
            string text,
            out bool changed,
            out string error)
        {
            try
            {
                object value = InspectorValueConversion.FromText(
                    definition.Owner, definition.Property, text);
                return TrySetScalar(definition, value, out changed, out error);
            }
            catch (Exception ex)
            {
                changed = false;
                error = Unwrap(ex).Message;
                return false;
            }
        }

        public static bool TrySetScalar(
            InspectorScalarFieldDefinition definition,
            object value,
            out bool changed,
            out string error)
        {
            try
            {
                changed = !Equals(definition.GetValue(), value);
                if (changed) definition.SetValue(value);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                changed = false;
                error = Unwrap(ex).Message;
                return false;
            }
        }

        public static bool TryConvertAndSetReference(
            InspectorValueReferenceFieldDefinition definition,
            InspectorValueReferenceKind kind,
            string text,
            out bool changed,
            out string error)
        {
            PropertyDescriptor property = definition.GetActiveProperty(kind);
            if (property == null)
            {
                changed = false;
                error = "当前引用方式没有可写字段。";
                return false;
            }
            try
            {
                object value = InspectorValueConversion.FromText(definition.Owner, property, text);
                return TrySetReference(definition, kind, value, out changed, out error);
            }
            catch (Exception ex)
            {
                changed = false;
                error = Unwrap(ex).Message;
                return false;
            }
        }

        public static bool TrySetReference(
            InspectorValueReferenceFieldDefinition definition,
            InspectorValueReferenceKind kind,
            object value,
            out bool changed,
            out string error)
        {
            try
            {
                changed = definition.GetCurrentKind() != kind
                    || !Equals(definition.GetValue(kind), value);
                definition.SetValue(kind, value);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                changed = false;
                error = Unwrap(ex).Message;
                return false;
            }
        }

        private static Exception Unwrap(Exception exception)
        {
            return exception is TargetInvocationException invocation && invocation.InnerException != null
                ? invocation.InnerException
                : exception;
        }
    }

    internal static class InspectorValueConversion
    {
        public static bool HasStandardValues(object owner, PropertyDescriptor property)
        {
            Type targetType = Nullable.GetUnderlyingType(property.PropertyType)
                ?? property.PropertyType;
            if (targetType.IsEnum)
            {
                return true;
            }
            try
            {
                return property.Converter?.GetStandardValuesSupported(
                    new InspectorTypeDescriptorContext(owner, property)) == true;
            }
            catch
            {
                return false;
            }
        }

        public static bool StandardValuesExclusive(object owner, PropertyDescriptor property)
        {
            Type targetType = Nullable.GetUnderlyingType(property.PropertyType)
                ?? property.PropertyType;
            if (targetType.IsEnum)
            {
                return true;
            }
            try
            {
                return property.Converter?.GetStandardValuesExclusive(
                    new InspectorTypeDescriptorContext(owner, property)) == true;
            }
            catch
            {
                return false;
            }
        }

        public static IReadOnlyList<InspectorStandardValue> GetStandardValues(
            object owner,
            PropertyDescriptor property)
        {
            var result = new List<InspectorStandardValue>();
            var context = new InspectorTypeDescriptorContext(owner, property);
            Type targetType = Nullable.GetUnderlyingType(property.PropertyType)
                ?? property.PropertyType;
            IEnumerable values;
            try
            {
                if (property.Converter?.GetStandardValuesSupported(context) == true)
                {
                    values = property.Converter.GetStandardValues(context);
                }
                else if (targetType.IsEnum)
                {
                    values = Enum.GetValues(targetType);
                }
                else
                {
                    values = Array.Empty<object>();
                }
            }
            catch
            {
                values = Array.Empty<object>();
            }
            foreach (object value in values)
            {
                result.Add(new InspectorStandardValue(value, ToDisplayText(owner, property, value)));
            }
            return result;
        }

        public static string ToDisplayText(object owner, PropertyDescriptor property, object value)
        {
            if (value == null)
            {
                return string.Empty;
            }
            var context = new InspectorTypeDescriptorContext(owner, property);
            try
            {
                if (property.Converter?.CanConvertTo(context, typeof(string)) == true)
                {
                    return property.Converter.ConvertToString(context, CultureInfo.CurrentCulture, value);
                }
            }
            catch
            {
            }
            return Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty;
        }

        public static object FromText(object owner, PropertyDescriptor property, string text)
        {
            Type propertyType = property.PropertyType;
            Type targetType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
            if (targetType == typeof(string))
            {
                return text;
            }
            if (string.IsNullOrWhiteSpace(text) && Nullable.GetUnderlyingType(propertyType) != null)
            {
                return null;
            }
            var context = new InspectorTypeDescriptorContext(owner, property);
            object value;
            if (property.Converter?.CanConvertFrom(context, typeof(string)) == true)
            {
                value = property.Converter.ConvertFromString(context, CultureInfo.CurrentCulture, text);
            }
            else if (targetType.IsEnum)
            {
                value = Enum.Parse(targetType, text, true);
            }
            else
            {
                value = Convert.ChangeType(text, targetType, CultureInfo.CurrentCulture);
            }
            NumericRangeAttribute range = property.Attributes[typeof(NumericRangeAttribute)]
                as NumericRangeAttribute;
            if (range != null && !range.Contains(value))
            {
                throw new InvalidOperationException(
                    $"{property.DisplayName}必须为{range.Describe()}的数值。");
            }
            return value;
        }
    }
}
