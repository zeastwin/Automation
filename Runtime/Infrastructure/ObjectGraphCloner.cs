using Newtonsoft.Json;
// 模块：运行时 / 基础设施。
// 职责范围：提供不承载业务规则的计时与对象图辅助能力。


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
