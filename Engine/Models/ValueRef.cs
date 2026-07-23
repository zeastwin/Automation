using System;
// 模块：引擎 / 模型。
// 职责范围：定义引擎内部流程结构和值引用模型。

using System.Globalization;

namespace Automation
{
    public enum ValueRefKind
    {
        Empty = 0,
        Index = 1,
        IndexFromIndex = 2,
        Name = 3,
        IndexFromName = 4
    }

    public readonly struct ValueRef
    {
        public ValueRefKind Kind { get; }
        public string Token { get; }
        private int ParsedIndex { get; }
        private Guid BoundVariableId { get; }
        private int BoundIndex { get; }

        private ValueRef(
            ValueRefKind kind,
            string token,
            int parsedIndex = -1,
            Guid boundVariableId = default(Guid),
            int boundIndex = -1)
        {
            Kind = kind;
            Token = token;
            ParsedIndex = parsedIndex;
            BoundVariableId = boundVariableId;
            BoundIndex = boundIndex;
        }

        public bool IsEmpty => Kind == ValueRefKind.Empty;

        public static bool TryCreate(string index, string index2Index, string name, string name2Index, bool allowEmpty, string label, out ValueRef valueRef, out string error)
        {
            valueRef = default;
            error = null;
            string labelText = NormalizeLabel(label);

            bool hasIndex = HasToken(index, out string indexError);
            if (indexError != null)
            {
                error = $"{labelText}索引{indexError}";
                return false;
            }

            bool hasIndex2Index = HasToken(index2Index, out string index2IndexError);
            if (index2IndexError != null)
            {
                error = $"{labelText}索引二级{index2IndexError}";
                return false;
            }

            bool hasName = HasToken(name, out string nameError);
            if (nameError != null)
            {
                error = $"{labelText}名称{nameError}";
                return false;
            }

            bool hasName2Index = HasToken(name2Index, out string name2IndexError);
            if (name2IndexError != null)
            {
                error = $"{labelText}名称二级{name2IndexError}";
                return false;
            }

            int count = 0;
            ValueRefKind kind = ValueRefKind.Empty;
            string token = null;

            if (hasIndex)
            {
                count++;
                kind = ValueRefKind.Index;
                token = index;
            }

            if (hasIndex2Index)
            {
                count++;
                kind = ValueRefKind.IndexFromIndex;
                token = index2Index;
            }

            if (hasName)
            {
                count++;
                kind = ValueRefKind.Name;
                token = name;
            }

            if (hasName2Index)
            {
                count++;
                kind = ValueRefKind.IndexFromName;
                token = name2Index;
            }

            if (count == 0)
            {
                if (allowEmpty)
                {
                    valueRef = new ValueRef(ValueRefKind.Empty, null);
                    return true;
                }
                error = $"{labelText}不能为空";
                return false;
            }

            if (count > 1)
            {
                error = $"{labelText}配置冲突";
                return false;
            }

            int parsedIndex = -1;
            if ((kind == ValueRefKind.Index || kind == ValueRefKind.IndexFromIndex)
                && !TryParseIndex(token, out parsedIndex, out string parseError))
            {
                error = $"{labelText}索引无效:{parseError}";
                return false;
            }
            valueRef = new ValueRef(kind, token, parsedIndex);
            return true;
        }

        public bool TryBindStatic(
            ValueConfigStore store,
            Guid procId,
            string label,
            out ValueRef bound,
            out string error)
        {
            bound = this;
            error = null;
            if (Kind != ValueRefKind.Index && Kind != ValueRefKind.Name)
            {
                return true;
            }
            if (!TryResolveValue(store, label, procId, out DicValue variable, out error))
            {
                return false;
            }
            bound = new ValueRef(Kind, Token, ParsedIndex, variable.Id, variable.Index);
            return true;
        }

        public bool TryResolveIndex(ValueConfigStore store, string label, out int index, out string error)
        {
            return TryResolveIndex(store, label, null, out index, out error);
        }

        public bool TryResolveIndex(
            ValueConfigStore store, string label, Guid procId, out int index, out string error)
        {
            return TryResolveIndex(store, label, (Guid?)procId, out index, out error);
        }

        private bool TryResolveIndex(
            ValueConfigStore store, string label, Guid? procId, out int index, out string error)
        {
            index = -1;
            error = null;
            string labelText = NormalizeLabel(label);

            if (store == null)
            {
                error = $"{labelText}变量库未初始化";
                return false;
            }

            if (BoundVariableId != Guid.Empty
                && (Kind == ValueRefKind.Index || Kind == ValueRefKind.Name))
            {
                if (!TryGetByIndex(store, BoundIndex, procId, out DicValue boundValue)
                    || boundValue.Id != BoundVariableId)
                {
                    error = $"{labelText}预绑定变量已失效:{Token}";
                    return false;
                }
                index = BoundIndex;
                return true;
            }

            switch (Kind)
            {
                case ValueRefKind.Empty:
                    error = $"{labelText}不能为空";
                    return false;
                case ValueRefKind.Index:
                    index = ParsedIndex;
                    if (!TryGetByIndex(store, index, procId, out _))
                    {
                        error = $"{labelText}变量不存在:索引{index}";
                        return false;
                    }
                    return true;
                case ValueRefKind.Name:
                    if (string.IsNullOrEmpty(Token))
                    {
                        error = $"{labelText}名称为空";
                        return false;
                    }
                    if (!TryGetByName(store, Token, procId, out DicValue nameValue))
                    {
                        error = $"{labelText}变量不存在:{Token}";
                        return false;
                    }
                    index = nameValue.Index;
                    return true;
                case ValueRefKind.IndexFromIndex:
                    int sourceIndex = ParsedIndex;
                    if (!TryGetByIndex(store, sourceIndex, procId, out DicValue sourceValue))
                    {
                        error = $"{labelText}索引变量不存在:索引{sourceIndex}";
                        return false;
                    }
                    if (!TryParseIndex(sourceValue.Value, out index, out string targetIndexError))
                    {
                        error = $"{labelText}索引变量值无效:{FormatDisplayName(sourceValue)}->{targetIndexError}";
                        return false;
                    }
                    if (!TryGetByIndex(store, index, procId, out _))
                    {
                        error = $"{labelText}目标变量不存在:索引{index}";
                        return false;
                    }
                    return true;
                case ValueRefKind.IndexFromName:
                    if (string.IsNullOrEmpty(Token))
                    {
                        error = $"{labelText}名称二级为空";
                        return false;
                    }
                    if (!TryGetByName(store, Token, procId, out DicValue nameSource))
                    {
                        error = $"{labelText}索引变量不存在:{Token}";
                        return false;
                    }
                    if (!TryParseIndex(nameSource.Value, out index, out string nameTargetError))
                    {
                        error = $"{labelText}索引变量值无效:{FormatDisplayName(nameSource)}->{nameTargetError}";
                        return false;
                    }
                    if (!TryGetByIndex(store, index, procId, out _))
                    {
                        error = $"{labelText}目标变量不存在:索引{index}";
                        return false;
                    }
                    return true;
                default:
                    error = $"{labelText}解析失败";
                    return false;
            }
        }

        public bool TryResolveValue(ValueConfigStore store, string label, out DicValue value, out string error)
        {
            return TryResolveValue(store, label, null, out value, out error);
        }

        public bool TryResolveValue(
            ValueConfigStore store, string label, Guid procId, out DicValue value, out string error)
        {
            return TryResolveValue(store, label, (Guid?)procId, out value, out error);
        }

        private bool TryResolveValue(
            ValueConfigStore store, string label, Guid? procId, out DicValue value, out string error)
        {
            value = null;
            error = null;
            string labelText = NormalizeLabel(label);
            if (store == null)
            {
                error = $"{labelText}变量库未初始化";
                return false;
            }

            // 直接引用和预绑定引用是指令热路径。一次读取同时完成存在性、作用域和
            // 稳定身份校验，避免先解析索引后再重复访问同一变量槽位。
            if (BoundVariableId != Guid.Empty
                && (Kind == ValueRefKind.Index || Kind == ValueRefKind.Name))
            {
                if (!TryGetByIndex(store, BoundIndex, procId, out value)
                    || value == null
                    || value.Id != BoundVariableId)
                {
                    value = null;
                    error = $"{labelText}预绑定变量已失效:{Token}";
                    return false;
                }
                return true;
            }
            if (Kind == ValueRefKind.Index)
            {
                if (!TryGetByIndex(store, ParsedIndex, procId, out value) || value == null)
                {
                    value = null;
                    error = $"{labelText}变量不存在:索引{ParsedIndex}";
                    return false;
                }
                return true;
            }
            if (Kind == ValueRefKind.Name)
            {
                if (string.IsNullOrEmpty(Token))
                {
                    error = $"{labelText}名称为空";
                    return false;
                }
                if (!TryGetByName(store, Token, procId, out value) || value == null)
                {
                    value = null;
                    error = $"{labelText}变量不存在:{Token}";
                    return false;
                }
                return true;
            }

            if (!TryResolveIndex(store, label, procId, out int index, out error))
            {
                return false;
            }
            if (!TryGetByIndex(store, index, procId, out value) || value == null)
            {
                error = $"{labelText}变量不存在:索引{index}";
                return false;
            }
            return true;
        }

        private static bool TryGetByName(
            ValueConfigStore store, string name, Guid? procId, out DicValue value)
        {
            return procId.HasValue
                ? store.TryGetValueByNameForProcess(name, procId.Value, out value)
                : store.TryGetValueByName(name, out value);
        }

        private static bool TryGetByIndex(
            ValueConfigStore store, int index, Guid? procId, out DicValue value)
        {
            return procId.HasValue
                ? store.TryGetValueByIndexForProcess(index, procId.Value, out value)
                : store.TryGetValueByIndex(index, out value);
        }

        private static bool HasToken(string text, out string error)
        {
            error = null;
            if (text == null)
            {
                return false;
            }
            if (text.Length == 0)
            {
                return false;
            }
            if (string.IsNullOrWhiteSpace(text))
            {
                error = "为空白";
                return false;
            }
            return true;
        }

        private static bool TryParseIndex(string text, out int index, out string error)
        {
            index = -1;
            error = null;
            if (string.IsNullOrEmpty(text))
            {
                error = "索引为空";
                return false;
            }
            if (!IsDigits(text))
            {
                error = $"索引格式非法:{text}";
                return false;
            }
            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out index))
            {
                error = $"索引超出范围:{text}";
                return false;
            }
            if (index < 0)
            {
                error = $"索引不能为负:{text}";
                return false;
            }
            return true;
        }

        private static bool IsDigits(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c < '0' || c > '9')
                {
                    return false;
                }
            }
            return text.Length > 0;
        }

        private static string NormalizeLabel(string label)
        {
            return string.IsNullOrWhiteSpace(label) ? "变量" : label;
        }

        private static string FormatDisplayName(DicValue value)
        {
            if (value == null)
            {
                return "未知变量";
            }
            return string.IsNullOrWhiteSpace(value.Name) ? $"索引{value.Index}" : value.Name;
        }
    }
}
