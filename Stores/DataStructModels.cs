using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

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
