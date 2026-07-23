// 模块：编辑器 / 流程 / Inspector。
// 职责范围：集中处理少数指令依赖业务配置顺序的展示规则。

using System;
using System.ComponentModel;

namespace Automation
{
    internal static class InspectorOperationPresentationPolicy
    {
        public static int AdjustPriority(
            object instance,
            string rootName,
            int currentPriority)
        {
            if (!(instance is SetDataStructItem))
            {
                return currentPriority;
            }
            switch (rootName)
            {
                case nameof(SetDataStructItem.StructName):
                case nameof(SetDataStructItem.StructIndex):
                    return -95;
                case nameof(SetDataStructItem.ItemName):
                case nameof(SetDataStructItem.ItemIndex):
                    return -94;
                case nameof(SetDataStructItem.Params):
                    return -80;
                default:
                    return currentPriority;
            }
        }

        public static bool IsVisible(
            object instance,
            PropertyDescriptor descriptor,
            bool defaultVisibility)
        {
            if (!defaultVisibility
                || !(instance is SetDataStructItem operation)
                || !string.Equals(
                    descriptor?.Name,
                    nameof(SetDataStructItem.Params),
                    StringComparison.Ordinal))
            {
                return defaultVisibility;
            }

            // 先确定结构体和数据项，再展示字段写入配置。每一级地址可以
            // 使用名称或索引，但必须恰好配置一种方式。
            return HasSingleAddress(operation.StructName, operation.StructIndex)
                && HasSingleAddress(operation.ItemName, operation.ItemIndex);
        }

        private static bool HasSingleAddress(string name, int index)
        {
            return !string.IsNullOrWhiteSpace(name) != (index >= 0);
        }
    }
}
