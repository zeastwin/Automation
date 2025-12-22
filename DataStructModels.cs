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
            DataStruct copy = new DataStruct
            {
                Name = Name
            };

            if (dataStructItems == null)
            {
                return copy;
            }

            foreach (DataStructItem item in dataStructItems)
            {
                if (item == null)
                {
                    copy.dataStructItems.Add(new DataStructItem());
                }
                else
                {
                    copy.dataStructItems.Add(item.Clone());
                }
            }

            return copy;
        }
    }

    [Serializable]
    public class DataStructItem
    {
        public string Name { get; set; }

        public Dictionary<int, string> str { get; set; } = new Dictionary<int, string>();
        public Dictionary<int, double> num { get; set; } = new Dictionary<int, double>();

        public DataStructItem Clone()
        {
            DataStructItem copy = new DataStructItem
            {
                Name = Name
            };

            if (str != null)
            {
                foreach (KeyValuePair<int, string> kvp in str)
                {
                    copy.str[kvp.Key] = kvp.Value;
                }
            }

            if (num != null)
            {
                foreach (KeyValuePair<int, double> kvp in num)
                {
                    copy.num[kvp.Key] = kvp.Value;
                }
            }

            return copy;
        }

        public int GetMaxIndex()
        {
            int maxIndex = -1;
            if (str != null && str.Count > 0)
            {
                maxIndex = Math.Max(maxIndex, str.Keys.Max());
            }

            if (num != null && num.Count > 0)
            {
                maxIndex = Math.Max(maxIndex, num.Keys.Max());
            }

            return maxIndex;
        }
    }
}
