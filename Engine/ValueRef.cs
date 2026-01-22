using System;
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

        private ValueRef(ValueRefKind kind, string token)
        {
            Kind = kind;
            Token = token;
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

            valueRef = new ValueRef(kind, token);
            return true;
        }

        public bool TryResolveIndex(ValueConfigStore store, string label, out int index, out string error)
        {
            index = -1;
            error = null;
            string labelText = NormalizeLabel(label);

            if (store == null)
            {
                error = $"{labelText}变量库未初始化";
                return false;
            }

            switch (Kind)
            {
                case ValueRefKind.Empty:
                    error = $"{labelText}不能为空";
                    return false;
                case ValueRefKind.Index:
                    if (!TryParseIndex(Token, out index, out string parseError))
                    {
                        error = $"{labelText}索引无效:{parseError}";
                        return false;
                    }
                    if (!store.TryGetValueByIndex(index, out _))
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
                    if (!store.TryGetValueByName(Token, out DicValue nameValue))
                    {
                        error = $"{labelText}变量不存在:{Token}";
                        return false;
                    }
                    index = nameValue.Index;
                    return true;
                case ValueRefKind.IndexFromIndex:
                    if (!TryParseIndex(Token, out int sourceIndex, out string sourceIndexError))
                    {
                        error = $"{labelText}索引二级无效:{sourceIndexError}";
                        return false;
                    }
                    if (!store.TryGetValueByIndex(sourceIndex, out DicValue sourceValue))
                    {
                        error = $"{labelText}索引变量不存在:索引{sourceIndex}";
                        return false;
                    }
                    if (!TryParseIndex(sourceValue.Value, out index, out string targetIndexError))
                    {
                        error = $"{labelText}索引变量值无效:{FormatDisplayName(sourceValue)}->{targetIndexError}";
                        return false;
                    }
                    if (!store.TryGetValueByIndex(index, out _))
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
                    if (!store.TryGetValueByName(Token, out DicValue nameSource))
                    {
                        error = $"{labelText}索引变量不存在:{Token}";
                        return false;
                    }
                    if (!TryParseIndex(nameSource.Value, out index, out string nameTargetError))
                    {
                        error = $"{labelText}索引变量值无效:{FormatDisplayName(nameSource)}->{nameTargetError}";
                        return false;
                    }
                    if (!store.TryGetValueByIndex(index, out _))
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
            value = null;
            error = null;
            if (!TryResolveIndex(store, label, out int index, out error))
            {
                return false;
            }
            if (!store.TryGetValueByIndex(index, out value) || value == null)
            {
                error = $"{NormalizeLabel(label)}变量不存在:索引{index}";
                return false;
            }
            return true;
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
