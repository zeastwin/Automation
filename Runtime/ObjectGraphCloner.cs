using Newtonsoft.Json;

namespace Automation
{
    /// <summary>
    /// 仅用于进程内可信对象的深复制，不接受外部 JSON 输入。
    /// </summary>
    public static class ObjectGraphCloner
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.All,
            ObjectCreationHandling = ObjectCreationHandling.Replace
        };

        public static T Clone<T>(T value)
        {
            if (ReferenceEquals(value, null))
            {
                return default(T);
            }
            string json = JsonConvert.SerializeObject(value, Settings);
            return JsonConvert.DeserializeObject<T>(json, Settings);
        }
    }
}
