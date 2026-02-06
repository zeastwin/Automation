using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Serialization;
using static Automation.FrmPropertyGrid;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using System.Runtime.InteropServices.ComTypes;
using static Automation.OperationTypePartial;

namespace Automation
{
    public partial class FrmPropertyGrid : Form
    {

        public List<object> OperationTypeList = new List<object>();

        public FrmPropertyGrid()
        {
            InitializeComponent();
            propertyGrid1.PropertySort = PropertySort.Categorized;
            propertyGrid1.SelectedObjectsChanged += propertyGrid1_SelectedObjectsChanged;
            InlineListTypeDescriptionProvider.Register();
            
            OperationTypeList.Add(new HomeRun());
            OperationTypeList.Add(new StationRunPos());
            OperationTypeList.Add(new ModifyStationPos());
            OperationTypeList.Add(new GetStationPos());
            OperationTypeList.Add(new CreateTray());
            OperationTypeList.Add(new TrayRunPos());
            OperationTypeList.Add(new StationRunRel());
            OperationTypeList.Add(new SetStationVel());
            OperationTypeList.Add(new StationStop());
            OperationTypeList.Add(new WaitStationStop());
            OperationTypeList.Add(new CallCustomFunc());
            OperationTypeList.Add(new IoOperate());
            OperationTypeList.Add(new IoCheck());
            OperationTypeList.Add(new IoLogicGoto());
            OperationTypeList.Add(new ProcOps());
            OperationTypeList.Add(new WaitProc());
            OperationTypeList.Add(new Goto());
            OperationTypeList.Add(new ParamGoto());
            OperationTypeList.Add(new Delay());
            OperationTypeList.Add(new PopupDialog());
            OperationTypeList.Add(new GetValue());
            OperationTypeList.Add(new ModifyValue());
            OperationTypeList.Add(new StringFormat());
            OperationTypeList.Add(new Split());
            OperationTypeList.Add(new Replace());
            OperationTypeList.Add(new TcpOps());
            OperationTypeList.Add(new WaitTcp());
            OperationTypeList.Add(new SendTcpMsg());
            OperationTypeList.Add(new ReceoveTcpMsg());
            OperationTypeList.Add(new SerialPortOps());
            OperationTypeList.Add(new WaitSerialPort());
            OperationTypeList.Add(new SendSerialPortMsg());
            OperationTypeList.Add(new ReceoveSerialPortMsg());
            OperationTypeList.Add(new SendReceoveCommMsg());
            OperationTypeList.Add(new PlcReadWrite());
            OperationTypeList.Add(new GetDataStructCount());
            OperationTypeList.Add(new SetDataStructItem());
            OperationTypeList.Add(new GetDataStructItem());
            OperationTypeList.Add(new CopyDataStructItem());
            OperationTypeList.Add(new InsertDataStructItem());
            OperationTypeList.Add(new DelDataStructItem());
            OperationTypeList.Add(new FindDataStructItem());
     
         



            OperationType.DataSource = OperationTypeList;
            OperationType.DisplayMember = "OperaType";

            KeyPreview = true;

            Enabled = false;

        }

        private void propertyGrid1_SelectedObjectsChanged(object sender, EventArgs e)
        {
            if (propertyGrid1.SelectedObject is OperationType op)
            {
                SetPropertyAttribute(op, "Num", typeof(BrowsableAttribute), "browsable", false);
                TypeDescriptor.Refresh(op);
                propertyGrid1.Refresh();
            }
        }

        public OperationType temp;
        public static T DeepCopy<T>(T t)
        {
            if (ReferenceEquals(t, null))
            {
                return default;
            }
            using (MemoryStream memoryStream = new MemoryStream())
            {
                IFormatter formatter = new BinaryFormatter();
                formatter.Serialize(memoryStream, t);
                memoryStream.Seek(0, SeekOrigin.Begin);
                return (T)formatter.Deserialize(memoryStream);
            }

        }
        private void OperationType_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (OperationType.SelectedIndex != -1 && SF.isModify == ModifyKind.Operation)
            {
                // temp = DeepCopy((OperationType)OperationType.SelectedItem);
                SF.frmDataGrid.OperationTemp = (OperationType)((OperationType)OperationType.SelectedItem).Clone();
                SF.frmDataGrid.OperationTemp.Num = SF.frmDataGrid.iSelectedRow;
                propertyGrid1.SelectedObject = SF.frmDataGrid.OperationTemp;
                SF.frmDataGrid.OperationTemp.evtRP();
                propertyGrid1.ExpandAllGridItems();
            }
            if (OperationType.SelectedIndex != -1 && SF.isAddOps == true)
            {
                int num = ((OperationType)propertyGrid1.SelectedObject).Num;
                SF.frmDataGrid.OperationTemp = (OperationType)((OperationType)OperationType.SelectedItem).Clone();
                SF.frmDataGrid.OperationTemp.Num = num;
                propertyGrid1.SelectedObject = SF.frmDataGrid.OperationTemp;
                SF.frmDataGrid.OperationTemp.evtRP();
                propertyGrid1.ExpandAllGridItems();
            }
         
        }
        //展开特定的组
        public void ExpandGroup(PropertyGrid propertyGrid, string groupName)
        {
            GridItem root = propertyGrid.SelectedGridItem;
            while (root.Parent != null)
                root = root.Parent;

            if (root != null)
            {
                foreach (GridItem g in root.GridItems)
                {
                    if (g.GridItemType == GridItemType.Category && g.Label == groupName)
                    {
                        g.Expanded = true;
                        break;
                    }
                }
            }
        }
        private void FrmPropertyGrid_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                SF.frmToolBar.btnSave.PerformClick();
            }
        }

        private void propertyGrid1_PropertyValueChanged(object s, PropertyValueChangedEventArgs e)
        {
            object selected = propertyGrid1.SelectedObject;
            if (selected == null)
            {
                return;
            }
            TypeDescriptor.Refresh(selected);
            propertyGrid1.SelectedObject = selected;
            propertyGrid1.Refresh();
        }

        private void Address_Click(object sender, EventArgs e)
        {
            var obj = SF.frmDataGrid.OperationTemp;

            foreach (var propertyInfo in obj.GetType().GetProperties())
            {
                var markedAttribute = propertyInfo.GetCustomAttribute<ItemTypeAttribute>();

                if (markedAttribute != null)
                {
                    string propertyName = propertyInfo.Name;
                    if (PropertyName == propertyName)
                    {

                    }
                }
                else
                {
                    // 获取标记了 ItemTypeAttribute 的属性的名称
                    string propertyName = propertyInfo.Name;

                    // 获取属性的值
                    var propertyValue = propertyInfo.GetValue(obj);
                    // 如果属性的值是 List<T> 类型，进一步迭代获取列表元素的属性信息
                    if (propertyValue is System.Collections.IEnumerable enumerable && !(propertyValue is string))
                    {
                        int Num = 1;
                        foreach (var listItem in enumerable)
                        {
                            foreach (var listItemPropertyInfo in listItem.GetType().GetProperties())
                            {
                                var listItemMarkedAttribute = listItemPropertyInfo.GetCustomAttribute<ItemTypeAttribute>();

                                if (listItemMarkedAttribute != null)
                                {
                                    // 获取列表元素中标记了 ItemTypeAttribute 的属性的名称
                                    string listItemPropertyName = listItemPropertyInfo.Name;
                                    string label = listItemMarkedAttribute.Label;
                                    string[] keyTemp = label.Split('-');
                                    if (Index == Num.ToString())
                                    {
                                        GridItem parentGridItem = propertyGrid1.SelectedGridItem;

                                        parentGridItem = parentGridItem.Parent;
                                        // 获取父级的 PropertyDescriptor
                                        PropertyDescriptor parentPropertyDescriptor = parentGridItem.PropertyDescriptor;

                                        // 获取父级属性的实例
                                        object parentInstance = null;

                                        if (parentPropertyDescriptor != null)
                                        {
                                            parentInstance = parentPropertyDescriptor.GetValue(parentGridItem.Parent);
                                        }

                                        if (Key[0] == keyTemp[0]&& Key[1] == keyTemp[1])
                                        {
                                            SetPropertyAttribute(parentInstance, listItemPropertyName, typeof(BrowsableAttribute), "browsable", true);
                                        }
                                        else if(Key[0] == keyTemp[0] && Key[1] != keyTemp[1])
                                        {
                                            SetPropertyAttribute(parentInstance, listItemPropertyName, typeof(BrowsableAttribute), "browsable", false);
                                        }
                                        
                                        SF.frmPropertyGrid.propertyGrid1.SelectedObject = SF.frmPropertyGrid.propertyGrid1.SelectedObject;
                                    }
                                }
                            }

                            Num++;
                        }
                        
                    }
                }

            }
        }
        public void SetPropertyAttribute(object obj, string propertyName, Type attrType, string attrField, object value)
        {
            if (obj == null || string.IsNullOrEmpty(propertyName) || attrType == null)
            {
                return;
            }
            PropertyDescriptorCollection props = InlineListTypeDescriptionProvider.GetOriginalProperties(obj)
                ?? TypeDescriptor.GetProperties(obj);
            PropertyDescriptor prop = props[propertyName];
            if (prop == null)
            {
                return;
            }
            Attribute attr = prop.Attributes[attrType];
            if (attr == null)
            {
                return;
            }
            FieldInfo field = attrType.GetField(attrField, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                return;
            }
            field.SetValue(attr, value);
        }
        public string PropertyName;
        public string AttributeName;
        public string Index;
        public string[] Key;
        private void contextMenuStrip1_Opening(object sender, CancelEventArgs e)
        {
            propertyGrid1.ContextMenuStrip.Items.Clear();
            AttributeName = "";
            PropertyName = "";
            Index = "";
            Key = null;

            ItemTypeAttribute itemTypeAttribute = propertyGrid1.SelectedGridItem.PropertyDescriptor.Attributes[typeof(ItemTypeAttribute)] as ItemTypeAttribute;

            if (itemTypeAttribute != null)
            {
                AttributeName = itemTypeAttribute.Label;
                PropertyName = propertyGrid1.SelectedGridItem.PropertyDescriptor.Name;

                GridItem parentGridItem = propertyGrid1.SelectedGridItem;

                parentGridItem = parentGridItem.Parent;

                Index = parentGridItem.Label.Substring(parentGridItem.Label.IndexOf("：")+1, parentGridItem.Label.Length - parentGridItem.Label.IndexOf("：")-1);

                Key = AttributeName.Split('-');

            }
            propertyGrid1.ContextMenuStrip.Items.Add("切换地址", null, Address_Click);  
        }
    }
  

    public sealed class InlineListTypeDescriptionProvider : TypeDescriptionProvider
    {
        private static bool registered;
        private static InlineListTypeDescriptionProvider instance;
        private readonly TypeDescriptionProvider baseProvider;

        public InlineListTypeDescriptionProvider()
            : this(TypeDescriptor.GetProvider(typeof(OperationType)))
        {
        }

        private InlineListTypeDescriptionProvider(TypeDescriptionProvider baseProvider)
        {
            this.baseProvider = baseProvider;
        }

        public static void Register()
        {
            if (registered)
            {
                return;
            }
            instance = new InlineListTypeDescriptionProvider();
            TypeDescriptor.AddProvider(instance, typeof(OperationType));
            registered = true;
        }

        public static PropertyDescriptorCollection GetOriginalProperties(object instance)
        {
            if (instance == null || !registered || InlineListTypeDescriptionProvider.instance == null)
            {
                return null;
            }
            return InlineListTypeDescriptionProvider.instance.baseProvider
                .GetTypeDescriptor(instance)
                .GetProperties();
        }

        public override ICustomTypeDescriptor GetTypeDescriptor(Type objectType, object instance)
        {
            ICustomTypeDescriptor descriptor = baseProvider.GetTypeDescriptor(objectType, instance);
            if (instance == null)
            {
                return descriptor;
            }
            return new InlineListCustomTypeDescriptor(descriptor);
        }
    }

    public sealed class InlineListCustomTypeDescriptor : CustomTypeDescriptor
    {
        public InlineListCustomTypeDescriptor(ICustomTypeDescriptor parent)
            : base(parent)
        {
        }

        public override PropertyDescriptorCollection GetProperties()
        {
            return GetProperties(null);
        }

        public override PropertyDescriptorCollection GetProperties(Attribute[] attributes)
        {
            PropertyDescriptorCollection props = base.GetProperties(attributes);
            List<PropertyDescriptor> list = new List<PropertyDescriptor>(props.Count);
            foreach (PropertyDescriptor prop in props)
            {
                InlineGroupAttribute inlineGroup = prop.Attributes[typeof(InlineGroupAttribute)] as InlineGroupAttribute;
                if (inlineGroup != null)
                {
                    object groupValue = prop.GetValue(GetPropertyOwner(prop));
                    if (groupValue != null)
                    {
                        string groupLabel = string.IsNullOrWhiteSpace(inlineGroup.DisplayName) ? prop.DisplayName : inlineGroup.DisplayName;
                        PropertyDescriptorCollection groupProps = TypeDescriptor.GetProperties(groupValue);
                        foreach (PropertyDescriptor child in groupProps)
                        {
                            list.Add(new InlineGroupMemberDescriptor(prop, child, groupLabel));
                        }
                    }
                    continue;
                }

                InlineListAttribute inline = prop.Attributes[typeof(InlineListAttribute)] as InlineListAttribute;
                if (inline == null)
                {
                    list.Add(prop);
                    continue;
                }

                IList items = prop.GetValue(GetPropertyOwner(prop)) as IList;
                if (items == null)
                {
                    continue;
                }

                string displayName = string.IsNullOrWhiteSpace(inline.DisplayName) ? prop.DisplayName : inline.DisplayName;
                for (int i = 0; i < items.Count; i++)
                {
                    object item = items[i];
                    if (item == null)
                    {
                        continue;
                    }
                    string groupLabel = $"{displayName}：{i + 1}";
                    PropertyDescriptorCollection itemProps = TypeDescriptor.GetProperties(item);
                    foreach (PropertyDescriptor child in itemProps)
                    {
                        list.Add(new InlineListItemMemberDescriptor(prop, i, child, groupLabel));
                    }
                }
            }
            list = ValueRefPropertyMerger.Merge(list);
            return new PropertyDescriptorCollection(list.ToArray(), true);
        }
    }

    public sealed class InlineGroupMemberDescriptor : PropertyDescriptor
    {
        private readonly PropertyDescriptor groupProperty;
        private readonly PropertyDescriptor memberProperty;
        private readonly string category;
        private readonly string displayName;

        public InlineGroupMemberDescriptor(PropertyDescriptor groupProperty, PropertyDescriptor memberProperty, string category)
            : base($"{groupProperty.Name}_{memberProperty.Name}", memberProperty.Attributes.Cast<Attribute>().ToArray())
        {
            this.groupProperty = groupProperty;
            this.memberProperty = memberProperty;
            this.category = category;
            displayName = memberProperty.DisplayName;
        }

        public override string DisplayName => displayName;

        public override string Category => category;

        public override Type ComponentType => groupProperty.ComponentType;

        public override bool IsReadOnly => memberProperty.IsReadOnly;

        public override Type PropertyType => memberProperty.PropertyType;

        public override bool CanResetValue(object component)
        {
            return false;
        }

        public override object GetValue(object component)
        {
            object groupValue = groupProperty.GetValue(component);
            if (groupValue == null)
            {
                return null;
            }
            return memberProperty.GetValue(groupValue);
        }

        public override void ResetValue(object component)
        {
        }

        public override void SetValue(object component, object value)
        {
            object groupValue = groupProperty.GetValue(component);
            if (groupValue == null)
            {
                return;
            }
            memberProperty.SetValue(groupValue, value);
        }

        public override bool ShouldSerializeValue(object component)
        {
            return false;
        }

    }

    public sealed class InlineListItemMemberDescriptor : PropertyDescriptor
    {
        private readonly PropertyDescriptor listProperty;
        private readonly int index;
        private readonly PropertyDescriptor memberProperty;
        private readonly string category;
        private readonly string displayName;

        public InlineListItemMemberDescriptor(PropertyDescriptor listProperty, int index, PropertyDescriptor memberProperty, string category)
            : base($"{listProperty.Name}_{index}_{memberProperty.Name}", memberProperty.Attributes.Cast<Attribute>().ToArray())
        {
            this.listProperty = listProperty;
            this.index = index;
            this.memberProperty = memberProperty;
            this.category = category;
            displayName = memberProperty.DisplayName;
        }

        public override string DisplayName => displayName;

        public override string Category => category;

        public override Type ComponentType => listProperty.ComponentType;

        public override bool IsReadOnly => memberProperty.IsReadOnly;

        public override Type PropertyType => memberProperty.PropertyType;

        public override bool CanResetValue(object component)
        {
            return false;
        }

        public override object GetValue(object component)
        {
            IList items = listProperty.GetValue(component) as IList;
            if (items == null || index < 0 || index >= items.Count)
            {
                return null;
            }
            object item = items[index];
            if (item == null)
            {
                return null;
            }
            return memberProperty.GetValue(item);
        }

        public override void ResetValue(object component)
        {
        }

        public override void SetValue(object component, object value)
        {
            IList items = listProperty.GetValue(component) as IList;
            if (items == null || index < 0 || index >= items.Count)
            {
                return;
            }
            object item = items[index];
            if (item == null)
            {
                return;
            }
            memberProperty.SetValue(item, value);
        }

        public override bool ShouldSerializeValue(object component)
        {
            return false;
        }
    }

    public enum ValueRefInputKind
    {
        Name,
        Name2,
        Index,
        Index2,
        Conflict
    }

    public sealed class ValueRefGroup
    {
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, Dictionary<string, ValueRefInputKind>> PreferredKinds
            = new System.Runtime.CompilerServices.ConditionalWeakTable<object, Dictionary<string, ValueRefInputKind>>();

        public ValueRefGroup(string baseLabel, string category)
        {
            BaseLabel = baseLabel;
            Category = category;
        }

        public string BaseLabel { get; }
        public string Category { get; }
        public int FirstIndex { get; set; } = int.MaxValue;
        public PropertyDescriptor Index { get; set; }
        public PropertyDescriptor Index2 { get; set; }
        public PropertyDescriptor Name { get; set; }
        public PropertyDescriptor Name2 { get; set; }

        public Type ComponentType
        {
            get
            {
                if (Index != null) return Index.ComponentType;
                if (Index2 != null) return Index2.ComponentType;
                if (Name != null) return Name.ComponentType;
                if (Name2 != null) return Name2.ComponentType;
                return typeof(object);
            }
        }

        public bool CanMerge
        {
            get
            {
                int count = 0;
                if (Index != null) count++;
                if (Index2 != null) count++;
                if (Name != null) count++;
                if (Name2 != null) count++;
                return count >= 2;
            }
        }

        public ValueRefInputKind GetKind(object component)
        {
            int count = 0;
            ValueRefInputKind kind = GetDefaultKind();
            if (HasValue(Index, component))
            {
                count++;
                kind = ValueRefInputKind.Index;
            }
            if (HasValue(Index2, component))
            {
                count++;
                kind = ValueRefInputKind.Index2;
            }
            if (HasValue(Name, component))
            {
                count++;
                kind = ValueRefInputKind.Name;
            }
            if (HasValue(Name2, component))
            {
                count++;
                kind = ValueRefInputKind.Name2;
            }
            if (count == 0)
            {
                ValueRefInputKind? preferred = GetPreferredKind(component);
                if (preferred.HasValue)
                {
                    return preferred.Value;
                }
                return GetDefaultKind();
            }
            if (count > 1)
            {
                return ValueRefInputKind.Conflict;
            }
            SetPreferredKind(component, kind);
            return kind;
        }

        public string GetValueText(object component, ValueRefInputKind kind)
        {
            switch (kind)
            {
                case ValueRefInputKind.Index:
                    return GetText(Index, component);
                case ValueRefInputKind.Index2:
                    return GetText(Index2, component);
                case ValueRefInputKind.Name2:
                    return GetText(Name2, component);
                case ValueRefInputKind.Name:
                    return GetText(Name, component);
                default:
                    return string.Empty;
            }
        }

        public void SetKind(object component, ValueRefInputKind kind)
        {
            ClearAll(component);
            SetPreferredKind(component, kind);
        }

        public void SetValueFromText(object component, ValueRefInputKind kind, string value)
        {
            ClearAll(component);
            if (string.IsNullOrEmpty(value))
            {
                return;
            }
            switch (kind)
            {
                case ValueRefInputKind.Index:
                    SetValue(Index, component, value);
                    break;
                case ValueRefInputKind.Index2:
                    SetValue(Index2, component, value);
                    break;
                case ValueRefInputKind.Name2:
                    SetValue(Name2, component, value);
                    break;
                case ValueRefInputKind.Name:
                    SetValue(Name, component, value);
                    break;
            }
            SetPreferredKind(component, kind);
        }

        public bool UseNameSelector(object component)
        {
            ValueRefInputKind kind = GetKind(component);
            return kind == ValueRefInputKind.Name || kind == ValueRefInputKind.Name2;
        }

        private ValueRefInputKind GetDefaultKind()
        {
            if (Name != null) return ValueRefInputKind.Name;
            if (Name2 != null) return ValueRefInputKind.Name2;
            if (Index != null) return ValueRefInputKind.Index;
            if (Index2 != null) return ValueRefInputKind.Index2;
            return ValueRefInputKind.Name;
        }

        private static bool HasValue(PropertyDescriptor prop, object component)
        {
            if (prop == null)
            {
                return false;
            }
            object value = prop.GetValue(component);
            if (value == null)
            {
                return false;
            }
            if (value is string text)
            {
                return text.Length > 0;
            }
            if (IsNumericType(prop.PropertyType))
            {
                if (!TryGetNumericValue(value, out long number))
                {
                    return false;
                }
                return number >= 0;
            }
            return true;
        }

        private static string GetText(PropertyDescriptor prop, object component)
        {
            if (prop == null)
            {
                return string.Empty;
            }
            object value = prop.GetValue(component);
            if (value == null)
            {
                return string.Empty;
            }
            if (value is string text)
            {
                return text;
            }
            if (IsNumericType(prop.PropertyType))
            {
                if (!TryGetNumericValue(value, out long number))
                {
                    return string.Empty;
                }
                if (number < 0)
                {
                    return string.Empty;
                }
                return number.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            return value.ToString() ?? string.Empty;
        }

        private static void SetValue(PropertyDescriptor prop, object component, string value)
        {
            if (prop == null)
            {
                return;
            }
            Type propType = prop.PropertyType;
            Type targetType = Nullable.GetUnderlyingType(propType) ?? propType;
            if (targetType == typeof(string))
            {
                prop.SetValue(component, value);
                return;
            }
            if (IsNumericType(targetType))
            {
                if (!TryParseNumeric(value, targetType, out object parsed, out string error))
                {
                    throw new FormatException(error);
                }
                prop.SetValue(component, parsed);
                return;
            }
            prop.SetValue(component, Convert.ChangeType(value, targetType, System.Globalization.CultureInfo.InvariantCulture));
        }

        private void ClearAll(object component)
        {
            ClearValue(Index, component);
            ClearValue(Index2, component);
            ClearValue(Name, component);
            ClearValue(Name2, component);
        }

        private static void ClearValue(PropertyDescriptor prop, object component)
        {
            if (prop == null)
            {
                return;
            }
            Type propType = prop.PropertyType;
            Type targetType = Nullable.GetUnderlyingType(propType) ?? propType;
            if (targetType == typeof(string))
            {
                prop.SetValue(component, null);
                return;
            }
            if (Nullable.GetUnderlyingType(propType) != null)
            {
                prop.SetValue(component, null);
                return;
            }
            if (IsNumericType(targetType))
            {
                object emptyValue = Convert.ChangeType(-1, targetType, System.Globalization.CultureInfo.InvariantCulture);
                prop.SetValue(component, emptyValue);
                return;
            }
            object defaultValue = Activator.CreateInstance(targetType);
            prop.SetValue(component, defaultValue);
        }

        private static bool IsNumericType(Type type)
        {
            Type targetType = Nullable.GetUnderlyingType(type) ?? type;
            return targetType == typeof(int)
                || targetType == typeof(long)
                || targetType == typeof(short)
                || targetType == typeof(sbyte)
                || targetType == typeof(uint)
                || targetType == typeof(ulong)
                || targetType == typeof(ushort)
                || targetType == typeof(byte);
        }

        private static bool TryGetNumericValue(object value, out long number)
        {
            number = 0;
            if (value == null)
            {
                return false;
            }
            switch (value)
            {
                case int intValue:
                    number = intValue;
                    return true;
                case long longValue:
                    number = longValue;
                    return true;
                case short shortValue:
                    number = shortValue;
                    return true;
                case sbyte sbyteValue:
                    number = sbyteValue;
                    return true;
                case uint uintValue:
                    number = uintValue;
                    return true;
                case ulong ulongValue:
                    number = unchecked((long)ulongValue);
                    return true;
                case ushort ushortValue:
                    number = ushortValue;
                    return true;
                case byte byteValue:
                    number = byteValue;
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryParseNumeric(string value, Type targetType, out object result, out string error)
        {
            result = null;
            error = null;
            if (string.IsNullOrWhiteSpace(value))
            {
                error = "索引不能为空";
                return false;
            }
            if (targetType == typeof(int))
            {
                if (!int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int parsed) || parsed < 0)
                {
                    error = "索引必须为非负整数";
                    return false;
                }
                result = parsed;
                return true;
            }
            if (targetType == typeof(long))
            {
                if (!long.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long parsed) || parsed < 0)
                {
                    error = "索引必须为非负整数";
                    return false;
                }
                result = parsed;
                return true;
            }
            if (targetType == typeof(short))
            {
                if (!short.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out short parsed) || parsed < 0)
                {
                    error = "索引必须为非负整数";
                    return false;
                }
                result = parsed;
                return true;
            }
            if (targetType == typeof(sbyte))
            {
                if (!sbyte.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out sbyte parsed) || parsed < 0)
                {
                    error = "索引必须为非负整数";
                    return false;
                }
                result = parsed;
                return true;
            }
            if (targetType == typeof(uint))
            {
                if (!uint.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out uint parsed))
                {
                    error = "索引必须为非负整数";
                    return false;
                }
                result = parsed;
                return true;
            }
            if (targetType == typeof(ulong))
            {
                if (!ulong.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out ulong parsed))
                {
                    error = "索引必须为非负整数";
                    return false;
                }
                result = parsed;
                return true;
            }
            if (targetType == typeof(ushort))
            {
                if (!ushort.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out ushort parsed))
                {
                    error = "索引必须为非负整数";
                    return false;
                }
                result = parsed;
                return true;
            }
            if (targetType == typeof(byte))
            {
                if (!byte.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out byte parsed))
                {
                    error = "索引必须为非负整数";
                    return false;
                }
                result = parsed;
                return true;
            }
            error = "索引格式不支持";
            return false;
        }

        private string GetKey()
        {
            return $"{Category ?? string.Empty}||{BaseLabel ?? string.Empty}";
        }

        private ValueRefInputKind? GetPreferredKind(object component)
        {
            if (component == null)
            {
                return null;
            }
            if (!PreferredKinds.TryGetValue(component, out Dictionary<string, ValueRefInputKind> map))
            {
                return null;
            }
            if (map.TryGetValue(GetKey(), out ValueRefInputKind kind))
            {
                return kind;
            }
            return null;
        }

        private void SetPreferredKind(object component, ValueRefInputKind kind)
        {
            if (component == null)
            {
                return;
            }
            if (kind == ValueRefInputKind.Conflict)
            {
                return;
            }
            Dictionary<string, ValueRefInputKind> map = PreferredKinds.GetOrCreateValue(component);
            map[GetKey()] = kind;
        }
    }

    public static class ValueRefPropertyMerger
    {
        private const string SuffixIndex = "索引";
        private const string SuffixIndex2 = "索引二级";
        private const string SuffixName = "名称";
        private const string SuffixName2 = "名称二级";

        public static List<PropertyDescriptor> Merge(List<PropertyDescriptor> props)
        {
            Dictionary<string, ValueRefGroup> groups = new Dictionary<string, ValueRefGroup>();
            Dictionary<string, (PropertyDescriptor Prop, int Index)> nameCandidates = new Dictionary<string, (PropertyDescriptor, int)>();

            for (int i = 0; i < props.Count; i++)
            {
                PropertyDescriptor prop = props[i];
                if (TryGetValueRefPart(prop, out string baseLabel, out ValueRefInputKind part))
                {
                    string key = GetKey(prop.Category, baseLabel);
                    if (!groups.TryGetValue(key, out ValueRefGroup group))
                    {
                        group = new ValueRefGroup(baseLabel, prop.Category);
                        groups[key] = group;
                    }
                    group.FirstIndex = Math.Min(group.FirstIndex, i);
                    switch (part)
                    {
                        case ValueRefInputKind.Index:
                            group.Index = prop;
                            break;
                        case ValueRefInputKind.Index2:
                            group.Index2 = prop;
                            break;
                        case ValueRefInputKind.Name:
                            group.Name = prop;
                            break;
                        case ValueRefInputKind.Name2:
                            group.Name2 = prop;
                            break;
                    }
                    continue;
                }
                if (IsNameCandidate(prop, out string candidateBase))
                {
                    string key = GetKey(prop.Category, candidateBase);
                    if (!nameCandidates.ContainsKey(key))
                    {
                        nameCandidates[key] = (prop, i);
                    }
                }
            }

            foreach (KeyValuePair<string, (PropertyDescriptor Prop, int Index)> candidate in nameCandidates)
            {
                if (!groups.TryGetValue(candidate.Key, out ValueRefGroup group))
                {
                    continue;
                }
                if (group.Name == null)
                {
                    group.Name = candidate.Value.Prop;
                    group.FirstIndex = Math.Min(group.FirstIndex, candidate.Value.Index);
                }
            }

            List<ValueRefGroup> validGroups = groups.Values.Where(group => group.CanMerge).ToList();
            if (validGroups.Count == 0)
            {
                return props;
            }

            HashSet<PropertyDescriptor> hidden = new HashSet<PropertyDescriptor>();
            Dictionary<int, List<ValueRefGroup>> insertAt = new Dictionary<int, List<ValueRefGroup>>();
            foreach (ValueRefGroup group in validGroups)
            {
                if (group.Index != null) hidden.Add(group.Index);
                if (group.Index2 != null) hidden.Add(group.Index2);
                if (group.Name != null) hidden.Add(group.Name);
                if (group.Name2 != null) hidden.Add(group.Name2);

                if (!insertAt.TryGetValue(group.FirstIndex, out List<ValueRefGroup> groupList))
                {
                    groupList = new List<ValueRefGroup>();
                    insertAt[group.FirstIndex] = groupList;
                }
                groupList.Add(group);
            }

            List<PropertyDescriptor> output = new List<PropertyDescriptor>(props.Count);
            for (int i = 0; i < props.Count; i++)
            {
                if (insertAt.TryGetValue(i, out List<ValueRefGroup> groupList))
                {
                    foreach (ValueRefGroup group in groupList)
                    {
                        output.Add(new ValueRefTypePropertyDescriptor(group));
                        output.Add(new ValueRefValuePropertyDescriptor(group));
                    }
                }
                PropertyDescriptor prop = props[i];
                if (hidden.Contains(prop))
                {
                    continue;
                }
                output.Add(prop);
            }

            return output;
        }

        private static bool TryGetValueRefPart(PropertyDescriptor prop, out string baseLabel, out ValueRefInputKind part)
        {
            baseLabel = null;
            part = ValueRefInputKind.Name;
            string name = prop.DisplayName ?? string.Empty;
            if (name.EndsWith(SuffixIndex2, StringComparison.Ordinal))
            {
                baseLabel = name.Substring(0, name.Length - SuffixIndex2.Length);
                part = ValueRefInputKind.Index2;
                return true;
            }
            if (name.EndsWith(SuffixIndex, StringComparison.Ordinal))
            {
                baseLabel = name.Substring(0, name.Length - SuffixIndex.Length);
                part = ValueRefInputKind.Index;
                return true;
            }
            if (name.EndsWith(SuffixName2, StringComparison.Ordinal))
            {
                baseLabel = name.Substring(0, name.Length - SuffixName2.Length);
                part = ValueRefInputKind.Name2;
                return true;
            }
            if (name.EndsWith(SuffixName, StringComparison.Ordinal))
            {
                baseLabel = name.Substring(0, name.Length - SuffixName.Length);
                part = ValueRefInputKind.Name;
                return true;
            }
            return false;
        }

        private static bool IsNameCandidate(PropertyDescriptor prop, out string baseLabel)
        {
            baseLabel = null;
            string name = prop.DisplayName ?? string.Empty;
            if (name.Length == 0)
            {
                return false;
            }
            if (name.EndsWith(SuffixIndex2, StringComparison.Ordinal)
                || name.EndsWith(SuffixIndex, StringComparison.Ordinal)
                || name.EndsWith(SuffixName2, StringComparison.Ordinal)
                || name.EndsWith(SuffixName, StringComparison.Ordinal))
            {
                return false;
            }
            if (prop.Converter == null || prop.Converter.GetType() != typeof(ValueItem))
            {
                return false;
            }
            baseLabel = name;
            return true;
        }

        private static string GetKey(string category, string baseLabel)
        {
            return $"{category ?? string.Empty}||{baseLabel ?? string.Empty}";
        }
    }

    public sealed class ValueRefTypePropertyDescriptor : PropertyDescriptor
    {
        private readonly ValueRefGroup group;

        public ValueRefTypePropertyDescriptor(ValueRefGroup group)
            : base($"{group.Category}.{group.BaseLabel}.Type", null)
        {
            this.group = group;
        }

        public override string DisplayName
        {
            get
            {
                return "参数类型";
            }
        }

        public override string Category => group.Category;

        public override Type ComponentType => group.ComponentType;

        public override bool IsReadOnly => false;

        public override Type PropertyType => typeof(string);

        public override bool CanResetValue(object component)
        {
            return false;
        }

        public override object GetValue(object component)
        {
            return ToText(group.GetKind(component));
        }

        public override void ResetValue(object component)
        {
        }

        public override void SetValue(object component, object value)
        {
            ValueRefInputKind kind = ParseKind(value as string, group);
            if (kind == ValueRefInputKind.Conflict)
            {
                return;
            }
            group.SetKind(component, kind);
            TypeDescriptor.Refresh(component);
        }

        public override bool ShouldSerializeValue(object component)
        {
            return false;
        }

        public override TypeConverter Converter => new ValueRefTypeConverter(group);

        private static string ToText(ValueRefInputKind kind)
        {
            switch (kind)
            {
                case ValueRefInputKind.Index:
                    return "索引";
                case ValueRefInputKind.Index2:
                    return "索引二级";
                case ValueRefInputKind.Name2:
                    return "名称二级";
                case ValueRefInputKind.Name:
                    return "名称";
                case ValueRefInputKind.Conflict:
                    return "冲突";
                default:
                    return "名称";
            }
        }

        private static ValueRefInputKind ParseKind(string text, ValueRefGroup group)
        {
            if (string.Equals(text, "索引", StringComparison.Ordinal))
            {
                return group.Index != null ? ValueRefInputKind.Index : ValueRefInputKind.Conflict;
            }
            if (string.Equals(text, "索引二级", StringComparison.Ordinal))
            {
                return group.Index2 != null ? ValueRefInputKind.Index2 : ValueRefInputKind.Conflict;
            }
            if (string.Equals(text, "名称二级", StringComparison.Ordinal))
            {
                return group.Name2 != null ? ValueRefInputKind.Name2 : ValueRefInputKind.Conflict;
            }
            if (string.Equals(text, "名称", StringComparison.Ordinal))
            {
                return group.Name != null ? ValueRefInputKind.Name : ValueRefInputKind.Conflict;
            }
            if (string.Equals(text, "冲突", StringComparison.Ordinal))
            {
                return ValueRefInputKind.Conflict;
            }
            return group.Name != null ? ValueRefInputKind.Name : ValueRefInputKind.Conflict;
        }

        private sealed class ValueRefTypeConverter : StringConverter
        {
            private readonly ValueRefGroup group;

            public ValueRefTypeConverter(ValueRefGroup group)
            {
                this.group = group;
            }

            public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
            {
                return true;
            }

            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                List<string> values = new List<string>();
                if (IsConflict(context))
                {
                    values.Add("冲突");
                }
                if (group.Name != null) values.Add("名称");
                if (group.Name2 != null) values.Add("名称二级");
                if (group.Index != null) values.Add("索引");
                if (group.Index2 != null) values.Add("索引二级");
                if (values.Count == 0)
                {
                    values.Add("名称");
                }
                return new StandardValuesCollection(values);
            }

            public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
            {
                return true;
            }

            private bool IsConflict(ITypeDescriptorContext context)
            {
                object instance = GetContextInstance(context);
                if (instance == null)
                {
                    return false;
                }
                return group.GetKind(instance) == ValueRefInputKind.Conflict;
            }

            private static object GetContextInstance(ITypeDescriptorContext context)
            {
                if (context == null)
                {
                    return null;
                }
                if (context.Instance is object[] instances)
                {
                    if (instances.Length > 0)
                    {
                        return instances[0];
                    }
                    return null;
                }
                return context.Instance;
            }
        }
    }

    public sealed class ValueRefValuePropertyDescriptor : PropertyDescriptor
    {
        private readonly ValueRefGroup group;

        public ValueRefValuePropertyDescriptor(ValueRefGroup group)
            : base($"{group.Category}.{group.BaseLabel}.Value", null)
        {
            this.group = group;
        }

        public override string DisplayName => group.BaseLabel;

        public override string Category => group.Category;

        public override Type ComponentType => group.ComponentType;

        public override bool IsReadOnly => false;

        public override Type PropertyType => typeof(string);

        public override bool CanResetValue(object component)
        {
            return false;
        }

        public override object GetValue(object component)
        {
            ValueRefInputKind kind = group.GetKind(component);
            if (kind == ValueRefInputKind.Conflict)
            {
                return string.Empty;
            }
            return group.GetValueText(component, kind);
        }

        public override void ResetValue(object component)
        {
        }

        public override void SetValue(object component, object value)
        {
            string text = value as string;
            ValueRefInputKind kind = group.GetKind(component);
            if (kind == ValueRefInputKind.Conflict)
            {
                kind = ValueRefInputKind.Name;
            }
            group.SetValueFromText(component, kind, text);
            TypeDescriptor.Refresh(component);
        }

        public override bool ShouldSerializeValue(object component)
        {
            return false;
        }

        public override TypeConverter Converter => new ValueRefValueConverter(group);

        private sealed class ValueRefValueConverter : StringConverter
        {
            private readonly ValueRefGroup group;

            public ValueRefValueConverter(ValueRefGroup group)
            {
                this.group = group;
            }

            public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
            {
                object instance = GetContextInstance(context);
                if (instance == null)
                {
                    return false;
                }
                TypeConverter converter = GetActiveConverter(instance);
                if (converter == null)
                {
                    return false;
                }
                return converter.GetStandardValuesSupported(context);
            }

            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                object instance = GetContextInstance(context);
                if (instance == null)
                {
                    return new StandardValuesCollection(new List<string>());
                }
                TypeConverter converter = GetActiveConverter(instance);
                if (converter == null)
                {
                    return new StandardValuesCollection(new List<string>());
                }
                return converter.GetStandardValues(context);
            }

            public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
            {
                object instance = GetContextInstance(context);
                if (instance == null)
                {
                    return false;
                }
                TypeConverter converter = GetActiveConverter(instance);
                if (converter == null)
                {
                    return false;
                }
                return converter.GetStandardValuesExclusive(context);
            }

            private static object GetContextInstance(ITypeDescriptorContext context)
            {
                if (context == null)
                {
                    return null;
                }
                if (context.Instance is object[] instances)
                {
                    if (instances.Length > 0)
                    {
                        return instances[0];
                    }
                    return null;
                }
                return context.Instance;
            }

            private TypeConverter GetActiveConverter(object instance)
            {
                ValueRefInputKind kind = group.GetKind(instance);
                switch (kind)
                {
                    case ValueRefInputKind.Name:
                        return group.Name?.Converter;
                    case ValueRefInputKind.Name2:
                        return group.Name2?.Converter;
                    case ValueRefInputKind.Index:
                        return group.Index?.Converter;
                    case ValueRefInputKind.Index2:
                        return group.Index2?.Converter;
                    default:
                        return null;
                }
            }
        }
    }
}
