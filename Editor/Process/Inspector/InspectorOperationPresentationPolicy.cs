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
            switch (instance)
            {
                case SetDataStructItem _:
                    if (IsAny(rootName,
                        nameof(SetDataStructItem.StructName),
                        nameof(SetDataStructItem.StructIndex))) return -95;
                    if (IsAny(rootName,
                        nameof(SetDataStructItem.ItemName),
                        nameof(SetDataStructItem.ItemIndex))) return -94;
                    if (rootName == nameof(SetDataStructItem.Params)) return -80;
                    break;
                case GetDataStructItem _:
                    if (IsAny(rootName,
                        nameof(GetDataStructItem.StructName),
                        nameof(GetDataStructItem.StructIndex))) return -95;
                    if (IsAny(rootName,
                        nameof(GetDataStructItem.ItemName),
                        nameof(GetDataStructItem.ItemIndex))) return -94;
                    if (rootName == nameof(GetDataStructItem.IsAllItem)) return -90;
                    if (rootName == nameof(GetDataStructItem.FirstResultVariableName)) return -85;
                    if (rootName == nameof(GetDataStructItem.Params)) return -80;
                    break;
                case CopyDataStructItem _:
                    if (IsAny(rootName,
                        nameof(CopyDataStructItem.SourceStructName),
                        nameof(CopyDataStructItem.SourceStructIndex))) return -95;
                    if (IsAny(rootName,
                        nameof(CopyDataStructItem.SourceItemName),
                        nameof(CopyDataStructItem.SourceItemIndex))) return -94;
                    if (IsAny(rootName,
                        nameof(CopyDataStructItem.TargetStructName),
                        nameof(CopyDataStructItem.TargetStructIndex))) return -93;
                    if (IsAny(rootName,
                        nameof(CopyDataStructItem.TargetItemName),
                        nameof(CopyDataStructItem.TargetItemIndex))) return -92;
                    if (rootName == nameof(CopyDataStructItem.IsAllValue)) return -90;
                    if (rootName == nameof(CopyDataStructItem.Params)) return -80;
                    break;
                case InsertDataStructItem _:
                    if (IsAny(rootName,
                        nameof(InsertDataStructItem.TargetStructName),
                        nameof(InsertDataStructItem.TargetStructIndex))) return -95;
                    if (rootName == nameof(InsertDataStructItem.ItemName)) return -94;
                    if (rootName == nameof(InsertDataStructItem.TargetItemIndex)) return -93;
                    if (rootName == nameof(InsertDataStructItem.Params)) return -80;
                    break;
                case DelDataStructItem _:
                    if (IsAny(rootName,
                        nameof(DelDataStructItem.TargetStructName),
                        nameof(DelDataStructItem.TargetStructIndex))) return -95;
                    if (IsAny(rootName,
                        nameof(DelDataStructItem.TargetItemName),
                        nameof(DelDataStructItem.TargetItemIndex))) return -94;
                    break;
                case FindDataStructItem _:
                    if (IsAny(rootName,
                        nameof(FindDataStructItem.TargetStructName),
                        nameof(FindDataStructItem.TargetStructIndex))) return -95;
                    if (rootName == nameof(FindDataStructItem.Type)) return -90;
                    if (rootName == nameof(FindDataStructItem.Key)) return -89;
                    if (rootName == nameof(FindDataStructItem.ResultVariableName)) return -88;
                    break;
                case GetDataStructCount _:
                    if (IsAny(rootName,
                        nameof(GetDataStructCount.TargetStructName),
                        nameof(GetDataStructCount.TargetStructIndex))) return -95;
                    if (rootName == nameof(GetDataStructCount.StructCountVariableName)) return -90;
                    if (rootName == nameof(GetDataStructCount.ItemCountVariableName)) return -89;
                    break;
            }
            return currentPriority;
        }

        public static bool IsVisible(
            object instance,
            PropertyDescriptor descriptor,
            bool defaultVisibility)
        {
            if (!defaultVisibility || descriptor == null)
            {
                return defaultVisibility;
            }

            if (instance is SetDataStructItem set
                && descriptor.Name == nameof(SetDataStructItem.Params))
            {
                return HasSingleAddress(set.StructName, set.StructIndex)
                    && HasSingleAddress(set.ItemName, set.ItemIndex);
            }
            if (instance is GetDataStructItem get
                && descriptor.Name == nameof(GetDataStructItem.Params))
            {
                return !get.IsAllItem
                    && HasSingleAddress(get.StructName, get.StructIndex)
                    && HasSingleAddress(get.ItemName, get.ItemIndex);
            }
            if (instance is CopyDataStructItem copy
                && descriptor.Name == nameof(CopyDataStructItem.Params))
            {
                return !copy.IsAllValue
                    && HasSingleAddress(copy.SourceStructName, copy.SourceStructIndex)
                    && HasSingleAddress(copy.SourceItemName, copy.SourceItemIndex)
                    && HasSingleAddress(copy.TargetStructName, copy.TargetStructIndex)
                    && HasSingleAddress(copy.TargetItemName, copy.TargetItemIndex);
            }
            if (instance is InsertDataStructItem insert
                && descriptor.Name == nameof(InsertDataStructItem.Params))
            {
                return HasSingleAddress(
                        insert.TargetStructName,
                        insert.TargetStructIndex)
                    && !string.IsNullOrWhiteSpace(insert.ItemName)
                    && insert.TargetItemIndex >= 0;
            }
            return defaultVisibility;
        }

        private static bool HasSingleAddress(string name, int index)
        {
            return !string.IsNullOrWhiteSpace(name) != (index >= 0);
        }

        private static bool IsAny(string value, params string[] candidates)
        {
            foreach (string candidate in candidates)
            {
                if (string.Equals(value, candidate, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
