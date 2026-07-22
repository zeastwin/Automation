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
        private static JToken ConvertValueToToken(object value)
        {
            if (value == null)
            {
                return JValue.CreateNull();
            }

            try
            {
                return JToken.FromObject(value);
            }
            catch
            {
                return value.ToString();
            }
        }

        private static string ConvertFieldValueToText(object value)
        {
            if (value == null)
            {
                return string.Empty;
            }
            if (value is bool boolValue)
            {
                return boolValue ? "true" : "false";
            }
            return value.ToString();
        }

        private static string GetTypeLabel(Type type)
        {
            Type underlying = Nullable.GetUnderlyingType(type) ?? type;
            if (underlying == typeof(string))
            {
                return "string";
            }
            if (underlying == typeof(bool))
            {
                return "bool";
            }
            if (underlying == typeof(int))
            {
                return "int";
            }
            if (underlying == typeof(long))
            {
                return "long";
            }
            if (underlying == typeof(float))
            {
                return "float";
            }
            if (underlying == typeof(double))
            {
                return "double";
            }
            if (underlying == typeof(decimal))
            {
                return "decimal";
            }
            if (underlying == typeof(Guid))
            {
                return "guid";
            }
            if (underlying.IsEnum)
            {
                return "enum";
            }
            return underlying.Name;
        }

        private static string GetJsonTypeLabel(Type type)
        {
            Type underlying = Nullable.GetUnderlyingType(type) ?? type;
            if (underlying == typeof(string) || underlying == typeof(Guid) || underlying.IsEnum)
            {
                return "string";
            }
            if (underlying == typeof(bool))
            {
                return "boolean";
            }
            if (underlying == typeof(int) || underlying == typeof(long))
            {
                return "integer";
            }
            if (underlying == typeof(float) || underlying == typeof(double) || underlying == typeof(decimal))
            {
                return "number";
            }
            return "object";
        }

        private static string GetFieldValueShape(PropertyDescriptor descriptor)
        {
            if (descriptor == null)
            {
                return string.Empty;
            }

            string jsonType = GetJsonTypeLabel(descriptor.PropertyType);
            string referenceType = GetReferenceType(descriptor.Converter?.GetType().Name);
            if (jsonType == "string" && !string.IsNullOrEmpty(referenceType))
            {
                return "必须传 JSON 字符串；即使候选值看起来是数字编号，也要写成带引号的字符串，例如 \"0\"。";
            }
            if (jsonType == "string")
            {
                return "必须传 JSON 字符串。";
            }
            if (jsonType == "boolean")
            {
                return "必须传 JSON 布尔值 true/false。";
            }
            if (jsonType == "integer")
            {
                return "必须传 JSON 整数。";
            }
            if (jsonType == "number")
            {
                return "必须传 JSON 数值。";
            }
            return "必须传与字段类型匹配的 JSON 值。";
        }

        private static string GetReferenceType(string converterTypeName)
        {
            switch (converterTypeName)
            {
                case "IoOutItem":
                    return "io.output";
                case "IoInItem":
                    return "io.input";
                case "AlarmInfoItem":
                    return "alarm.infoId";
                case "DataStItem":
                    return "dataStruct";
                case "ProcItem":
                    return "proc";
                case "CommItem":
                    return "comm.all";
                case "ValueItem":
                    return "value";
                case "TcpItem":
                    return "comm.tcp";
                case "SerialPortItem":
                    return "comm.serial";
                case "PlcItem":
                    return "plc.device";
                case "GotoItem":
                    return "proc.goto";
                case "StationtItem":
                case "SetStationVelItem":
                    return "station";
                case "StationPosDic":
                case "StationPosWithSpecial":
                    return "station.position";
                case "StationAixsItem":
                    return "station.axis";
                case "funcNameItem":
                    return "customFunc";
                default:
                    return string.Empty;
            }
        }

        private static bool IsVariableIndexDescriptor(PropertyDescriptor descriptor)
        {
            if (descriptor == null) return false;
            string displayName = descriptor.DisplayName ?? string.Empty;
            string description = descriptor.Description ?? string.Empty;
            return displayName.IndexOf("变量", StringComparison.Ordinal) >= 0
                && displayName.IndexOf("索引", StringComparison.Ordinal) >= 0
                || description.IndexOf("变量索引", StringComparison.Ordinal) >= 0;
        }

        private static JArray BuildStandardValues(PropertyDescriptor descriptor)
        {
            JArray values = new JArray();
            if (descriptor?.Converter == null)
            {
                return values;
            }

            try
            {
                if (!descriptor.Converter.GetStandardValuesSupported(null))
                {
                    return values;
                }

                StandardValuesCollection collection = descriptor.Converter.GetStandardValues(null);
                if (collection == null)
                {
                    return values;
                }

                foreach (object item in collection)
                {
                    values.Add(ConvertValueToToken(item));
                }
            }
            catch
            {
            }

            return values;
        }

    }
}
