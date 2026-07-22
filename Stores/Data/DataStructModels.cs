using System;
// 模块：持久化 / 业务数据。
// 职责范围：管理报警与数据结构配置的模型和持久化。

using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;

namespace Automation
{
    [Serializable]
    public class DataStruct : ICloneable
    {
        [Browsable(false)]
        public string Name { get; set; }

        public List<DataStructItem> dataStructItems = new List<DataStructItem>();

        public object Clone()
        {
            return ObjectGraphCloner.Clone(this);
        }
    }

    [Serializable]
    public class DataStructItem
    {
        [NonSerialized]
        private object syncRoot;

        [JsonIgnore]
        internal object SyncRoot
        {
            get
            {
                object current = Volatile.Read(ref syncRoot);
                if (current != null)
                {
                    return current;
                }
                Interlocked.CompareExchange(ref syncRoot, new object(), null);
                return syncRoot;
            }
        }

        public string Name { get; set; }
        public Dictionary<int, string> FieldNames { get; set; } = new Dictionary<int, string>();
        public Dictionary<int, DataStructValueType> FieldTypes { get; set; } = new Dictionary<int, DataStructValueType>();
        public Dictionary<int, string> str { get; set; } = new Dictionary<int, string>();
        public Dictionary<int, double> num { get; set; } = new Dictionary<int, double>();

        public DataStructItem Clone()
        {
            return ObjectGraphCloner.Clone(this);
        }

        public int GetMaxIndex()
        {
            return new IEnumerable<int>[] { FieldNames.Keys, FieldTypes.Keys, str.Keys, num.Keys }
                .SelectMany(keys => keys).DefaultIfEmpty(-1).Max();
        }
    }
}
