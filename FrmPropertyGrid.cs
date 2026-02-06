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
            this.propertyGrid1.SelectedObject = this.propertyGrid1.SelectedObject;
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
            PropertyDescriptorCollection props = TypeDescriptor.GetProperties(obj);
            Attribute attr = props[propertyName].Attributes[attrType];
            FieldInfo field = attrType.GetField(attrField, BindingFlags.Instance | BindingFlags.NonPublic);
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
            TypeDescriptor.AddProvider(new InlineListTypeDescriptionProvider(), typeof(OperationType));
            registered = true;
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
}
