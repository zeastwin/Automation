using Newtonsoft.Json;
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
            
            OperationTypeList.Add(new HomeRun());
            OperationTypeList.Add(new StationRunPos());
            OperationTypeList.Add(new StationRunRel());
            OperationTypeList.Add(new SetStationVel());
            OperationTypeList.Add(new StationStop());
            OperationTypeList.Add(new WaitStationStop());
            OperationTypeList.Add(new CallCustomFunc());
            OperationTypeList.Add(new IoOperate());
            OperationTypeList.Add(new IoCheck());
            OperationTypeList.Add(new ProcOps());
            OperationTypeList.Add(new WaitProc());
            OperationTypeList.Add(new Goto());
            OperationTypeList.Add(new ParamGoto());
            OperationTypeList.Add(new Delay());
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
        public OperationType temp;
        public static T DeepCopy<T>(T t)
        {
            var settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All
            };
            string output = Newtonsoft.Json.JsonConvert.SerializeObject(t, settings);

            T temp = JsonConvert.DeserializeObject<T>(output, settings);

            return temp;

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
  

}
