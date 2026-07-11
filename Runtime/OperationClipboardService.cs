using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Automation
{
    public static class OperationClipboardService
    {
        public const string Format = "Automation.OperationClipboard.v2";

        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.Auto,
            SerializationBinder = new OperationTypeBinder(),
            ObjectCreationHandling = ObjectCreationHandling.Replace
        };

        public static string Serialize(IEnumerable<OperationType> operations)
        {
            return JsonConvert.SerializeObject(operations?.ToList() ?? new List<OperationType>(), Settings);
        }

        public static List<OperationType> Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new JsonSerializationException("指令剪贴板内容为空。");
            }
            return JsonConvert.DeserializeObject<List<OperationType>>(json, Settings)
                ?? new List<OperationType>();
        }

        public static List<OperationType> PrepareForPaste(IEnumerable<OperationType> source, int procIndex)
        {
            List<OperationType> result = ObjectGraphCloner.Clone(source?.ToList());
            if (result == null)
            {
                return new List<OperationType>();
            }
            foreach (OperationType operation in result)
            {
                if (operation != null)
                {
                    operation.Id = Guid.NewGuid();
                }
            }
            ProcessEditingService.AdaptGotoProcIndex(result, procIndex);
            return result;
        }

        private sealed class OperationTypeBinder : ISerializationBinder
        {
            public Type BindToType(string assemblyName, string typeName)
            {
                Type type = typeof(OperationType).Assembly.GetType(typeName, false, false);
                if (type == null || !typeof(OperationType).IsAssignableFrom(type))
                {
                    throw new JsonSerializationException($"剪贴板包含不允许的指令类型：{typeName}");
                }
                return type;
            }

            public void BindToName(Type serializedType, out string assemblyName, out string typeName)
            {
                if (serializedType == null || !typeof(OperationType).IsAssignableFrom(serializedType))
                {
                    throw new JsonSerializationException($"不允许序列化剪贴板类型：{serializedType?.FullName}");
                }
                assemblyName = null;
                typeName = serializedType.FullName;
            }
        }
    }
}
