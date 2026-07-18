using System;
using static Automation.OperationTypePartial;

namespace Automation
{
    internal static class EditableCollection
    {
        public static CustomList<T> Resize<T>(
            CustomList<T> items,
            int count,
            Func<T> factory)
            where T : class
        {
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count), "配置项数量不能为负数。");
            }
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }
            CustomList<T> result = items ?? new CustomList<T>();
            while (result.Count < count)
            {
                result.Add(factory());
            }
            while (result.Count > count)
            {
                result.RemoveAt(result.Count - 1);
            }
            return result;
        }
    }
}
