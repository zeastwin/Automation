using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;

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
            using (MemoryStream memoryStream = new MemoryStream())
            {
                IFormatter formatter = new BinaryFormatter();
                formatter.Serialize(memoryStream, this);
                memoryStream.Seek(0, SeekOrigin.Begin);
                return formatter.Deserialize(memoryStream);
            }
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
            using (MemoryStream memoryStream = new MemoryStream())
            {
                IFormatter formatter = new BinaryFormatter();
                formatter.Serialize(memoryStream, this);
                memoryStream.Seek(0, SeekOrigin.Begin);
                return (DataStructItem)formatter.Deserialize(memoryStream);
            }
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
