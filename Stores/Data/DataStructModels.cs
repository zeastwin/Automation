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

        [NonSerialized]
        private DataStructRuntimeState runtimeState;

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

        [JsonIgnore]
        internal DataStructRuntimeState RuntimeState =>
            Volatile.Read(ref runtimeState);

        internal void PublishRuntimeState(DataStructRuntimeState state)
        {
            Volatile.Write(ref runtimeState, state);
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

    }

    internal sealed class DataStructRuntimeState
    {
        private readonly Dictionary<int, DataStructFieldSlot> fields;

        internal DataStructRuntimeState(
            Dictionary<int, DataStructFieldSlot> fields)
        {
            this.fields = fields
                ?? new Dictionary<int, DataStructFieldSlot>();
            SortedFieldIndexes = this.fields.Keys.OrderBy(index => index).ToArray();
        }

        internal int[] SortedFieldIndexes { get; }

        internal IEnumerable<DataStructFieldSlot> Fields => fields.Values;

        internal bool TryGetField(int index, out DataStructFieldSlot field)
        {
            return fields.TryGetValue(index, out field);
        }
    }

    internal sealed class DataStructFieldSlot
    {
        private long numberBits;
        private string textValue;
        private int hasValue;

        internal DataStructFieldSlot(
            int index,
            string name,
            DataStructValueType valueType,
            bool hasInitialValue,
            double number,
            string text)
        {
            Index = index;
            Name = name;
            ValueType = valueType;
            numberBits = BitConverter.DoubleToInt64Bits(number);
            textValue = text;
            hasValue = hasInitialValue ? 1 : 0;
        }

        internal int Index { get; }

        internal string Name { get; }

        internal DataStructValueType ValueType { get; }

        internal object SyncRoot { get; } = new object();

        internal bool HasValue => Volatile.Read(ref hasValue) == 1;

        internal double ReadNumber()
        {
            return BitConverter.Int64BitsToDouble(Volatile.Read(ref numberBits));
        }

        internal string ReadText()
        {
            return Volatile.Read(ref textValue);
        }

        internal void WriteNumber(double value)
        {
            Volatile.Write(ref numberBits, BitConverter.DoubleToInt64Bits(value));
            Volatile.Write(ref hasValue, 1);
        }

        internal void WriteText(string value)
        {
            Volatile.Write(ref textValue, value);
            Volatile.Write(ref hasValue, 1);
        }
    }
}
