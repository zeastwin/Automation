using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Automation
{
    internal sealed class InspectorDocument
    {
        public object Instance { get; set; }
        public IReadOnlyList<InspectorSectionDefinition> Sections { get; set; }
        public string Signature { get; set; }
    }

    internal sealed class InspectorSectionDefinition
    {
        public string Key { get; set; }
        public string Title { get; set; }
        public bool ShowHeader { get; set; } = true;
        public int Priority { get; set; }
        public List<InspectorFieldDefinition> Fields { get; } = new List<InspectorFieldDefinition>();
    }

    internal abstract class InspectorFieldDefinition
    {
        public string Key { get; set; }
        public string Label { get; set; }
        public string Description { get; set; }
        public bool IsReadOnly { get; set; }
        public int Priority { get; set; }

    }

    internal sealed class InspectorScalarFieldDefinition : InspectorFieldDefinition
    {
        public object Owner { get; set; }
        public PropertyDescriptor Property { get; set; }

        public object GetValue()
        {
            return Property.GetValue(Owner);
        }

        public void SetValue(object value)
        {
            Property.SetValue(Owner, value);
        }
    }

    internal sealed class InspectorCollectionFieldDefinition : InspectorFieldDefinition
    {
        public object Owner { get; set; }
        public PropertyDescriptor Property { get; set; }
        public IList Items => Property.GetValue(Owner) as IList;

        public Type ItemType
        {
            get
            {
                Type propertyType = Property.PropertyType;
                if (propertyType.IsArray)
                {
                    return propertyType.GetElementType();
                }
                if (propertyType.IsGenericType)
                {
                    return propertyType.GetGenericArguments().FirstOrDefault();
                }
                Type listInterface = propertyType.GetInterfaces()
                    .FirstOrDefault(type => type.IsGenericType
                        && type.GetGenericTypeDefinition() == typeof(IList<>));
                return listInterface?.GetGenericArguments()[0];
            }
        }
    }

    internal sealed class InspectorValueReferenceFieldDefinition : InspectorFieldDefinition
    {
        private readonly Dictionary<InspectorValueReferenceKind, PropertyDescriptor> properties
            = new Dictionary<InspectorValueReferenceKind, PropertyDescriptor>();

        public object Owner { get; set; }

        public IEnumerable<InspectorValueReferenceKind> AvailableKinds => properties.Keys;

        public void Add(InspectorValueReferenceKind kind, PropertyDescriptor property)
        {
            if (property != null)
            {
                properties[kind] = property;
            }
        }

        public bool Contains(PropertyDescriptor property)
        {
            return properties.Values.Contains(property);
        }

        public InspectorValueReferenceKind GetCurrentKind()
        {
            InspectorValueReferenceKind current = GetDefaultKind();
            int populated = 0;
            foreach (KeyValuePair<InspectorValueReferenceKind, PropertyDescriptor> item in properties)
            {
                if (!HasValue(item.Value.GetValue(Owner), item.Value.PropertyType))
                {
                    continue;
                }
                current = item.Key;
                populated++;
            }
            return populated > 1 ? InspectorValueReferenceKind.Conflict : current;
        }

        public InspectorValueReferenceKind GetDefaultKind()
        {
            InspectorValueReferenceKind[] order =
            {
                InspectorValueReferenceKind.Name,
                InspectorValueReferenceKind.Name2,
                InspectorValueReferenceKind.Index,
                InspectorValueReferenceKind.Index2
            };
            return order.FirstOrDefault(kind => properties.ContainsKey(kind));
        }

        public PropertyDescriptor GetActiveProperty(InspectorValueReferenceKind kind)
        {
            properties.TryGetValue(kind, out PropertyDescriptor property);
            return property;
        }

        public object GetValue(InspectorValueReferenceKind kind)
        {
            return GetActiveProperty(kind)?.GetValue(Owner);
        }

        public void SetKind(InspectorValueReferenceKind kind)
        {
            ClearAll();
            if (kind != InspectorValueReferenceKind.Conflict && !properties.ContainsKey(kind))
            {
                throw new InvalidOperationException("引用方式与当前字段不匹配。");
            }
        }

        public void SetValue(InspectorValueReferenceKind kind, object value)
        {
            PropertyDescriptor property = GetActiveProperty(kind);
            if (property == null)
            {
                throw new InvalidOperationException("当前引用方式没有可写字段。");
            }
            foreach (KeyValuePair<InspectorValueReferenceKind, PropertyDescriptor> item in properties)
            {
                if (item.Key != kind)
                {
                    ClearValue(item.Value);
                }
            }
            property.SetValue(Owner, value);
        }

        public string GetKindDisplayName(InspectorValueReferenceKind kind)
        {
            bool variable = properties.Values.Any(property =>
                property.Converter?.GetType() == typeof(OperationTypePartial.ValueItem));
            switch (kind)
            {
                case InspectorValueReferenceKind.Name:
                    return variable ? "变量" : "名称";
                case InspectorValueReferenceKind.Name2:
                    return variable ? "变量二级" : "名称二级";
                case InspectorValueReferenceKind.Index:
                    return "索引";
                case InspectorValueReferenceKind.Index2:
                    return "索引二级";
                case InspectorValueReferenceKind.Conflict:
                    return "存在冲突";
                default:
                    return "引用";
            }
        }

        private void ClearAll()
        {
            foreach (PropertyDescriptor property in properties.Values)
            {
                ClearValue(property);
            }
        }

        private void ClearValue(PropertyDescriptor property)
        {
            Type propertyType = property.PropertyType;
            Type targetType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
            if (targetType == typeof(string) || Nullable.GetUnderlyingType(propertyType) != null)
            {
                property.SetValue(Owner, null);
                return;
            }
            if (IsNumeric(targetType))
            {
                property.SetValue(Owner, Convert.ChangeType(-1, targetType, CultureInfo.InvariantCulture));
                return;
            }
            property.SetValue(Owner, Activator.CreateInstance(targetType));
        }

        private static bool HasValue(object value, Type propertyType)
        {
            if (value == null)
            {
                return false;
            }
            if (value is string text)
            {
                return text.Length > 0;
            }
            Type targetType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
            if (IsNumeric(targetType))
            {
                try
                {
                    return Convert.ToDecimal(value, CultureInfo.InvariantCulture) >= 0;
                }
                catch
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsNumeric(Type type)
        {
            return type == typeof(byte) || type == typeof(sbyte)
                || type == typeof(short) || type == typeof(ushort)
                || type == typeof(int) || type == typeof(uint)
                || type == typeof(long) || type == typeof(ulong)
                || type == typeof(float) || type == typeof(double)
                || type == typeof(decimal);
        }
    }

    internal enum InspectorValueReferenceKind
    {
        Name,
        Name2,
        Index,
        Index2,
        Conflict
    }

    internal sealed class InspectorTypeDescriptorContext : ITypeDescriptorContext
    {
        public InspectorTypeDescriptorContext(object instance, PropertyDescriptor property)
        {
            Instance = instance;
            PropertyDescriptor = property;
        }

        public IContainer Container => null;
        public object Instance { get; }
        public PropertyDescriptor PropertyDescriptor { get; }

        public object GetService(Type serviceType)
        {
            return EditorServiceRegistry.GetService(Instance, serviceType);
        }

        public void OnComponentChanged()
        {
        }

        public bool OnComponentChanging()
        {
            return true;
        }
    }

    internal static class InspectorDefinitionBuilder
    {
        private const int MaxDepth = 8;

        public static InspectorDocument Build(object instance)
        {
            var sections = new List<InspectorSectionDefinition>();
            if (instance != null)
            {
                BuildObject(instance, string.Empty, null, sections, 0);
                OrderForPresentation(instance, sections);
            }
            string signature = string.Join("|", sections.Select(section =>
                "S:" + section.Key + ":" + section.Title + ":" + section.ShowHeader + "["
                + string.Join(",", section.Fields.Select(field =>
                    field.GetType().Name + ":" + field.Key)) + "]"));
            return new InspectorDocument
            {
                Instance = instance,
                Sections = sections,
                Signature = signature
            };
        }

        public static IReadOnlyList<InspectorFieldDefinition> BuildItemFields(
            object item,
            string path,
            int itemIndex = -1)
        {
            var sections = new List<InspectorSectionDefinition>();
            if (item != null)
            {
                BuildObject(item, path, null, sections, 0);
            }
            IEnumerable<InspectorFieldDefinition> fields = sections
                .SelectMany(section => section.Fields);
            if (item is ParamGotoParam)
            {
                fields = fields
                    .Where(field => itemIndex != 0
                        || !(field.Key ?? string.Empty).EndsWith(
                            ".Operator",
                            StringComparison.Ordinal))
                    .OrderBy(field =>
                    {
                        string key = field.Key ?? string.Empty;
                        if (key.Contains(".ValueIndex") || key.Contains(".ValueName")) return 0;
                        if (key.EndsWith(".JudgeMode", StringComparison.Ordinal)) return 10;
                        if (key.EndsWith(".Down", StringComparison.Ordinal)) return 20;
                        if (key.EndsWith(".Up", StringComparison.Ordinal)) return 21;
                        if (key.EndsWith(".ExpectedText", StringComparison.Ordinal)) return 20;
                        if (key.EndsWith(".IncludeBoundary", StringComparison.Ordinal)) return 30;
                        if (key.EndsWith(".Operator", StringComparison.Ordinal)) return 40;
                        return 50;
                    });
            }
            return fields.ToList();
        }

        private static void BuildObject(
            object instance,
            string path,
            string forcedSection,
            List<InspectorSectionDefinition> sections,
            int depth)
        {
            if (instance == null || depth > MaxDepth)
            {
                return;
            }
            PropertyDescriptorCollection descriptors = TypeDescriptor.GetProperties(instance);
            var leaves = new List<Tuple<PropertyDescriptor, string, string>>();

            foreach (PropertyDescriptor descriptor in descriptors.Cast<PropertyDescriptor>())
            {
                if (!IsVisible(instance, descriptor))
                {
                    continue;
                }
                if (depth == 0 && instance is OperationType
                    && (string.Equals(descriptor.Name, nameof(OperationType.Num), StringComparison.Ordinal)
                        || string.Equals(descriptor.Name, nameof(OperationType.OperaType), StringComparison.Ordinal)))
                {
                    // 编号和指令类型已在检查器标题区展示，正文不再重复占用两行只读字段。
                    continue;
                }
                string propertyPath = string.IsNullOrEmpty(path)
                    ? descriptor.Name
                    : path + "." + descriptor.Name;
                InlineListAttribute inlineList = descriptor.Attributes[typeof(InlineListAttribute)] as InlineListAttribute;
                if (inlineList != null || IsCollection(descriptor.PropertyType))
                {
                    IList items = descriptor.GetValue(instance) as IList;
                    if (items == null)
                    {
                        continue;
                    }
                    string sectionTitle = forcedSection ?? GetPresentationCategory(
                        instance,
                        descriptor,
                        inlineList?.Category ?? descriptor.Category);
                    InspectorSectionDefinition section = GetOrAddSection(
                        sections,
                        sectionTitle,
                        sectionTitle);
                    section.Fields.Add(new InspectorCollectionFieldDefinition
                    {
                        Key = propertyPath,
                        Label = string.IsNullOrWhiteSpace(inlineList?.DisplayName)
                            ? descriptor.DisplayName
                            : inlineList.DisplayName,
                        Description = descriptor.Description,
                        IsReadOnly = descriptor.IsReadOnly,
                        Owner = instance,
                        Property = descriptor
                    });
                    continue;
                }

                object value = null;
                try
                {
                    value = descriptor.GetValue(instance);
                }
                catch
                {
                }
                InlineGroupAttribute inlineGroup = descriptor.Attributes[typeof(InlineGroupAttribute)] as InlineGroupAttribute;
                if (value != null && (inlineGroup != null || IsExpandable(descriptor, value)))
                {
                    string groupName = inlineGroup?.DisplayName;
                    if (string.IsNullOrWhiteSpace(groupName))
                    {
                        groupName = descriptor.DisplayName;
                    }
                    BuildObject(value, propertyPath, groupName, sections, depth + 1);
                    continue;
                }

                string category = forcedSection ?? GetPresentationCategory(
                    instance,
                    descriptor,
                    descriptor.Category);
                leaves.Add(Tuple.Create(descriptor, propertyPath, category));
            }

            AddLeafFields(instance, leaves, sections);
        }

        private static void AddLeafFields(
            object owner,
            List<Tuple<PropertyDescriptor, string, string>> leaves,
            List<InspectorSectionDefinition> sections)
        {
            var groups = new Dictionary<string, InspectorValueReferenceFieldDefinition>(StringComparer.Ordinal);
            for (int index = 0; index < leaves.Count; index++)
            {
                PropertyDescriptor property = leaves[index].Item1;
                if (!TryGetReferencePart(property, out string baseLabel, out InspectorValueReferenceKind kind))
                {
                    continue;
                }
                string key = leaves[index].Item3 + "||" + baseLabel;
                if (!groups.TryGetValue(key, out InspectorValueReferenceFieldDefinition field))
                {
                    field = new InspectorValueReferenceFieldDefinition
                    {
                        Key = leaves[index].Item2 + ".reference",
                        Label = baseLabel,
                        Description = property.Description,
                        IsReadOnly = property.IsReadOnly,
                        Owner = owner
                    };
                    groups.Add(key, field);
                }
                field.Add(kind, property);
            }

            foreach (Tuple<PropertyDescriptor, string, string> leaf in leaves)
            {
                PropertyDescriptor property = leaf.Item1;
                if (property.Converter?.GetType() != typeof(OperationTypePartial.ValueItem))
                {
                    continue;
                }
                string key = leaf.Item3 + "||" + property.DisplayName;
                if (groups.TryGetValue(key, out InspectorValueReferenceFieldDefinition field))
                {
                    field.Add(InspectorValueReferenceKind.Name, property);
                }
            }

            var usableGroups = groups.Where(pair => pair.Value.AvailableKinds.Count() >= 2)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            var addedGroups = new HashSet<InspectorValueReferenceFieldDefinition>();
            foreach (Tuple<PropertyDescriptor, string, string> leaf in leaves)
            {
                PropertyDescriptor property = leaf.Item1;
                InspectorValueReferenceFieldDefinition reference = usableGroups.Values
                    .FirstOrDefault(field => field.Contains(property));
                InspectorSectionDefinition section = GetOrAddSection(
                    sections,
                    leaf.Item3,
                    leaf.Item3);
                if (reference != null)
                {
                    if (addedGroups.Add(reference))
                    {
                        section.Fields.Add(reference);
                    }
                    continue;
                }
                section.Fields.Add(new InspectorScalarFieldDefinition
                {
                    Key = leaf.Item2,
                    Label = property.DisplayName,
                    Description = property.Description,
                    IsReadOnly = property.IsReadOnly,
                    Owner = owner,
                    Property = property
                });
            }
        }

        private static bool TryGetReferencePart(
            PropertyDescriptor property,
            out string baseLabel,
            out InspectorValueReferenceKind kind)
        {
            string label = property.DisplayName ?? string.Empty;
            var suffixes = new[]
            {
                Tuple.Create("索引二级", InspectorValueReferenceKind.Index2),
                Tuple.Create("名称二级", InspectorValueReferenceKind.Name2),
                Tuple.Create("索引", InspectorValueReferenceKind.Index),
                Tuple.Create("名称", InspectorValueReferenceKind.Name)
            };
            foreach (Tuple<string, InspectorValueReferenceKind> suffix in suffixes)
            {
                if (!label.EndsWith(suffix.Item1, StringComparison.Ordinal))
                {
                    continue;
                }
                baseLabel = label.Substring(0, label.Length - suffix.Item1.Length);
                kind = suffix.Item2;
                return baseLabel.Length > 0;
            }
            baseLabel = null;
            kind = InspectorValueReferenceKind.Name;
            return false;
        }

        private static InspectorSectionDefinition GetOrAddSection(
            List<InspectorSectionDefinition> sections,
            string key,
            string title)
        {
            key = string.IsNullOrWhiteSpace(key) ? "常规" : key;
            InspectorSectionDefinition section = sections.FirstOrDefault(item =>
                string.Equals(item.Key, key, StringComparison.Ordinal));
            if (section != null)
            {
                return section;
            }
            section = new InspectorSectionDefinition
            {
                Key = key,
                Title = string.IsNullOrWhiteSpace(title) ? "常规" : title
            };
            sections.Add(section);
            return section;
        }

        private static void OrderForPresentation(
            object instance,
            List<InspectorSectionDefinition> sections)
        {
            JObject fieldRules = null;
            if (instance is OperationType operation)
            {
                fieldRules = OperationBehaviorCatalog.BuildContract(operation)?["fieldRules"] as JObject;
            }

            foreach (InspectorSectionDefinition section in sections)
            {
                for (int index = 0; index < section.Fields.Count; index++)
                {
                    InspectorFieldDefinition field = section.Fields[index];
                    string rootName = field.Key?.Split('.')[0] ?? string.Empty;
                    JToken rule = fieldRules?[rootName];
                    int priority;
                    if (rule?["requiredForRun"]?.Value<bool>() == true)
                    {
                        priority = 0;
                    }
                    else if (rule?["requiredWhenForRun"] != null)
                    {
                        priority = 5;
                    }
                    else if (field is InspectorCollectionFieldDefinition)
                    {
                        priority = 10;
                    }
                    else
                    {
                        priority = 20;
                    }
                    if (field is InspectorScalarFieldDefinition scalar
                        && scalar.Property.Attributes[typeof(RefreshPropertiesAttribute)]
                            is RefreshPropertiesAttribute refresh
                        && refresh.RefreshProperties == RefreshProperties.All)
                    {
                        // 会切换后续字段可见性或模式的主控项应先于其依赖配置出现。
                        priority = Math.Min(priority, 2);
                    }

                    if (instance is OperationType)
                    {
                        switch (rootName)
                        {
                            case nameof(OperationType.Name):
                                priority = -100;
                                break;
                            case nameof(OperationType.Disable):
                                priority = 45;
                                break;
                            case nameof(OperationType.IsBreakpoint):
                                priority = 50;
                                break;
                            case nameof(OperationType.Note):
                                priority = 55;
                                break;
                            case nameof(OperationType.AlarmType):
                                priority = -90;
                                break;
                            case nameof(OperationType.AlarmInfoId):
                            case nameof(OperationType.Goto1):
                            case nameof(OperationType.Goto2):
                            case nameof(OperationType.Goto3):
                                priority = -85;
                                break;
                        }
                        if (instance is ParamGoto)
                        {
                            switch (rootName)
                            {
                                case nameof(ParamGoto.Params):
                                    priority = 0;
                                    break;
                                case nameof(ParamGoto.TrueGoto):
                                    priority = 10;
                                    break;
                                case nameof(ParamGoto.FalseGoto):
                                    priority = 11;
                                    break;
                                case nameof(ParamGoto.InvalidDelayMs):
                                    priority = 12;
                                    break;
                            }
                        }
                    }
                    else if (string.Equals(rootName, "Name", StringComparison.Ordinal))
                    {
                        // 名称是对象识别的首要信息，应始终先于集合和其他配置显示。
                        priority = -100;
                    }
                    if (field.IsReadOnly)
                    {
                        priority += 80;
                    }
                    field.Priority = priority;
                }

                List<InspectorFieldDefinition> orderedFields = section.Fields
                    .Select((field, index) => new { field, index })
                    .OrderBy(item => item.field.Priority)
                    .ThenBy(item => item.index)
                    .Select(item => item.field)
                    .ToList();
                section.Fields.Clear();
                section.Fields.AddRange(orderedFields);
                section.Priority = section.Fields.Count == 0
                    ? int.MaxValue
                    : section.Fields.Min(field => field.Priority);
                section.ShowHeader = !(instance is OperationType
                    && string.Equals(section.Key, "指令信息", StringComparison.Ordinal));
            }

            List<InspectorSectionDefinition> orderedSections = sections
                .Select((section, index) => new { section, index })
                .OrderBy(item => item.section.Priority)
                .ThenBy(item => item.index)
                .Select(item => item.section)
                .ToList();
            sections.Clear();
            sections.AddRange(orderedSections);
        }

        private static bool IsVisible(object instance, PropertyDescriptor descriptor)
        {
            bool visible = descriptor.IsBrowsable;
            if (instance is IPropertyVisibilityProvider provider)
            {
                visible = provider.IsPropertyVisible(descriptor.Name, visible);
            }
            return visible;
        }

        private static bool IsExpandable(PropertyDescriptor descriptor, object value)
        {
            if (value == null || value is string || value is IDictionary)
            {
                return false;
            }
            Type type = descriptor.PropertyType;
            if (type.IsPrimitive || type.IsEnum || type == typeof(decimal)
                || type == typeof(DateTime) || type == typeof(Guid))
            {
                return false;
            }
            TypeConverter converter = descriptor.Converter ?? TypeDescriptor.GetConverter(value);
            return converter != null && converter.GetPropertiesSupported(
                new InspectorTypeDescriptorContext(value, descriptor));
        }

        private static bool IsCollection(Type type)
        {
            return type != typeof(string) && typeof(IList).IsAssignableFrom(type);
        }

        private static string GetPresentationCategory(
            object instance,
            PropertyDescriptor descriptor,
            string category)
        {
            if (instance is OperationType)
            {
                switch (descriptor.Name)
                {
                    case nameof(OperationType.AlarmType):
                    case nameof(OperationType.AlarmInfoId):
                    case nameof(OperationType.Goto1):
                    case nameof(OperationType.Goto2):
                    case nameof(OperationType.Goto3):
                        return "报警配置";
                    case nameof(OperationType.Name):
                        return "指令信息";
                    case nameof(OperationType.Disable):
                    case nameof(OperationType.IsBreakpoint):
                    case nameof(OperationType.Note):
                        return "运行与调试";
                }
            }
            return NormalizeCategory(category);
        }

        private static string NormalizeCategory(string category)
        {
            if (string.IsNullOrWhiteSpace(category)
                || string.Equals(category, "Misc", StringComparison.OrdinalIgnoreCase))
            {
                return "常规";
            }
            string value = category.Trim().TrimStart('\uFEFF');
            if (value.Length > 2 && char.IsLetter(value[0]) && value[1] == '.')
            {
                value = value.Substring(2);
            }
            else if (value.Length > 1 && value[0] >= 'A' && value[0] <= 'Z'
                && value[1] >= '\u4e00' && value[1] <= '\u9fff')
            {
                value = value.Substring(1);
            }
            return value.Length == 0 ? "常规" : value;
        }
    }
}
