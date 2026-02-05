using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Design;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static Automation.OperationTypePartial;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Button;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using Automation;

namespace Automation
{
    
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class MarkedGotoAttribute : Attribute
    {
        public string Label { get; }

        public MarkedGotoAttribute(string label)
        {
            Label = label;
        }
    }

    //顺序： 1 地址 2 二级地址 3 名称 4 变量
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class ItemTypeAttribute : Attribute
    {
        public string Label { get; }

        public ItemTypeAttribute(string label)
        {
            Label = label;
        }
    }


    [TypeConverter(typeof(SerializableExpandableObjectConverter))]
    [Serializable]
    public class DataStructC
    {
        [DisplayName("数据结构索引"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(MathcCount))]
        public string DataStructIndex { get; set; }

        [DisplayName("数据结构名称"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(DataStItem))]
        public string DataStructName { get; set; }
        public override string ToString()
        {
            return "";
        }
    }
    [TypeConverter(typeof(SerializableExpandableObjectConverter))]
    [Serializable]
    public class TimeOutC
    {
        [DisplayName("超时"), Category("参数"), Description(""), ReadOnly(false)]
        public int TimeOut { get; set; }
        [DisplayName("超时变量"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string TimeOutValue { get; set; }

        public override string ToString()
        {
            return "";
        }
    }

    public delegate void EventRefleshPropertyGrid();
    [Serializable]
    public class OperationType : ICloneable
    {
        public OperationType() 
        {
            evtRP += RefleshPropertyAlarm;
        }
        [Browsable(false)]
        [JsonIgnore]
        public EventRefleshPropertyGrid evtRP;

        [Browsable(false)]
        public Guid Id { get; set; }

        [Browsable(false)]
        [DisplayName("编号"), Category("常规"), Description(""), ReadOnly(true)]
        public int Num { get; set; }
        [Browsable(true)]
        [DisplayName("名称"), Category("常规"), Description(""), ReadOnly(false)]
        public virtual string Name { get; set; }

        [Browsable(true)]
        [DisplayName("操作类型"), Category("常规"), Description(""), ReadOnly(true)]
        public string OperaType { get; set; }

        private string alarmType = "报警停止";
        [DisplayName("报警类型"), Category("常规"), Description(""), ReadOnly(false), TypeConverter(typeof(AlarmItem))]
        [Browsable(true)]
        public string AlarmType
        {
            get => alarmType;
            set
            {
                if (alarmType != value)
                {
                    alarmType = value;
                    if (SF.isModify == ModifyKind.Operation || SF.isAddOps)
                    {
                        RefleshPropertyAlarm();
                    }
                }
            }
        }

        public void RefleshPropertyAlarm()
        {
            if (this.alarmType == "报警停止")
            {
                SetPropertyAttribute(this, "AlarmInfoID", typeof(BrowsableAttribute), "browsable", false);
                SetPropertyAttribute(this, "Goto1", typeof(BrowsableAttribute), "browsable", false);
                SetPropertyAttribute(this, "Goto2", typeof(BrowsableAttribute), "browsable", false);
                SetPropertyAttribute(this, "Goto3", typeof(BrowsableAttribute), "browsable", false);
            }
            else if (this.alarmType == "报警忽略")
            {
                SetPropertyAttribute(this, "AlarmInfoID", typeof(BrowsableAttribute), "browsable", false);
                SetPropertyAttribute(this, "Goto1", typeof(BrowsableAttribute), "browsable", false);
                SetPropertyAttribute(this, "Goto2", typeof(BrowsableAttribute), "browsable", false);
                SetPropertyAttribute(this, "Goto3", typeof(BrowsableAttribute), "browsable", false);
            }
            else if (this.alarmType == "自动处理")
            {
                SetPropertyAttribute(this, "AlarmInfoID", typeof(BrowsableAttribute), "browsable", false);
                SetPropertyAttribute(this, "Goto1", typeof(BrowsableAttribute), "browsable", true);
                SetPropertyAttribute(this, "Goto2", typeof(BrowsableAttribute), "browsable", false);
                SetPropertyAttribute(this, "Goto3", typeof(BrowsableAttribute), "browsable", false);
            }
            else if (this.alarmType == "弹框确定")
            {
                SetPropertyAttribute(this, "AlarmInfoID", typeof(BrowsableAttribute), "browsable", true);
                SetPropertyAttribute(this, "Goto1", typeof(BrowsableAttribute), "browsable", true);
                SetPropertyAttribute(this, "Goto2", typeof(BrowsableAttribute), "browsable", false);
                SetPropertyAttribute(this, "Goto3", typeof(BrowsableAttribute), "browsable", false);
            }
            else if (this.alarmType == "弹框确定与否")
            {
                SetPropertyAttribute(this, "AlarmInfoID", typeof(BrowsableAttribute), "browsable", true);
                SetPropertyAttribute(this, "Goto1", typeof(BrowsableAttribute), "browsable", true);
                SetPropertyAttribute(this, "Goto2", typeof(BrowsableAttribute), "browsable", true);
                SetPropertyAttribute(this, "Goto3", typeof(BrowsableAttribute), "browsable", false);
            }
            else if (this.alarmType == "弹框确定与否与取消")
            {
                SetPropertyAttribute(this, "AlarmInfoID", typeof(BrowsableAttribute), "browsable", true);
                SetPropertyAttribute(this, "Goto1", typeof(BrowsableAttribute), "browsable", true);
                SetPropertyAttribute(this, "Goto2", typeof(BrowsableAttribute), "browsable", true);
                SetPropertyAttribute(this, "Goto3", typeof(BrowsableAttribute), "browsable", true);
            }
        }
        [DisplayName("报警信息ID"), Category("常规"), Description(""), ReadOnly(false), TypeConverter(typeof(AlarmInfoItem))]
        [Browsable(false)]
        public string AlarmInfoID { get; set; }
        [Browsable(false)]
        [DisplayName("确定跳转"), Category("常规"), Description(""), ReadOnly(false), TypeConverter(typeof(GotoItem))]
        [MarkedGoto("标识的跳转属性")]
        public string Goto1 { get; set; }
        [Browsable(false)]
        [DisplayName("否跳转"), Category("常规"), Description(""), ReadOnly(false), TypeConverter(typeof(GotoItem))]
        [MarkedGoto("标识的跳转属性")]
        public string Goto2 { get; set; }
        [Browsable(false)]
        [DisplayName("取消跳转"), Category("常规"), Description(""), ReadOnly(false), TypeConverter(typeof(GotoItem))]
        [MarkedGoto("标识的跳转属性")]
        public string Goto3 { get; set; }
        [Browsable(true)]
        [DisplayName("备注"), Category("常规"), Description(""), ReadOnly(false)]
        public string Note { get; set; }
        [Browsable(true)]
        [DisplayName("断点"), Category("常规"), Description(""), ReadOnly(false)]
        public bool isStopPoint { get; set; }

        [Browsable(true)]
        [DisplayName("禁用"), Category("常规"), Description(""), ReadOnly(false)]
        public bool Disable { get; set; }

        public object Clone()
        {
            using (MemoryStream memoryStream = new MemoryStream())
            {
                IFormatter formatter = new BinaryFormatter();
                formatter.Serialize(memoryStream, this);
                memoryStream.Seek(0, SeekOrigin.Begin);
                return formatter.Deserialize(memoryStream);
            }
        }
        public void SetPropertyAttribute(object obj, string propertyName, Type attrType, string attrField, object value)
        {
            PropertyDescriptorCollection props = TypeDescriptor.GetProperties(obj);
            Attribute attr = props[propertyName].Attributes[attrType];
            FieldInfo field = attrType.GetField(attrField, BindingFlags.Instance | BindingFlags.NonPublic);
            field.SetValue(attr, value);
        }

        //更改某个参数的显示名称
        public void SetDisplayName(Type propertyType, string PropertyName, string disPlayName)
        {
            object obj;
            obj = Convert.ChangeType(SF.frmPropertyGrid.propertyGrid1.SelectedObject, propertyType);
            PropertyDescriptor descriptor = TypeDescriptor.GetProperties(obj)[PropertyName];
            DisplayNameAttribute attribute = descriptor.Attributes[typeof(DisplayNameAttribute)] as DisplayNameAttribute;
            FieldInfo field = attribute.GetType().GetField("_displayName", BindingFlags.NonPublic | BindingFlags.Instance);
            field.SetValue(attribute, disPlayName);
            SF.frmPropertyGrid.propertyGrid1.SelectedObject = SF.frmPropertyGrid.propertyGrid1.SelectedObject;
        }

    }
    [Serializable]
    public class CallCustomFunc : OperationType
    {
        public CallCustomFunc()
        {
            OperaType = "自定义方法";
        }
        [TypeConverter(typeof(funcNameItem))]
        public override string Name { get; set; }

    }
    [Serializable]
    public class IoOperate : OperationType
    {
        public IoOperate()
        {
            OperaType = "IO操作";
            ParamListConverter<IoOutParam>.Name = "IO";
        }
        private string iOCount;
        [DisplayName("数量"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(IOCountItem))]
        public string IOCount
        {
            get { return iOCount; }
            set
            {
                iOCount = value;
                if (SF.isModify == ModifyKind.Operation || SF.isAddOps)
                {
                    int num = int.Parse(IOCount);
                    ((IoOperate)SF.frmDataGrid.OperationTemp).IoParams = new CustomList<IoOutParam>();
                    CustomList<IoOutParam> temp = ((IoOperate)SF.frmDataGrid.OperationTemp).IoParams;
                    temp.Clear();
                    for (int i = 0; i < num; i++)
                    {
                        temp.Add(new IoOutParam() { delayAfter = -1,delayBefore = -1});
                    }     
                    SF.frmPropertyGrid.propertyGrid1.SelectedObject = SF.frmDataGrid.OperationTemp;
                    SF.frmPropertyGrid.propertyGrid1.ExpandAllGridItems();
                }
            }
        }

        [DisplayName("IO设置"), Category("参数"), Description(""), ReadOnly(false)]
        [TypeConverter(typeof(ParamListConverter<IoOutParam>))]

        public CustomList<IoOutParam> IoParams { get; set; }
    }
    [TypeConverter(typeof(SerializableExpandableObjectConverter))]
    [Serializable]
    public class IoOutParam
    {

        [DisplayName("名称"), Category("IO参数"), Description(""), ReadOnly(false), TypeConverter(typeof(IoOutItem))]
        public string IOName { get; set; }
        [DisplayName("写入值"), Category("IO参数"), Description(""), ReadOnly(false)]
        public bool value { get; set; }
        [DisplayName("操作前延时"), Category("IO参数"), Description(""), ReadOnly(false)]
        public int delayBefore { get; set; }
        [DisplayName("操作前延时变量"), Category("IO参数"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        [Browsable(true)]
        public string delayBeforeV { get; set; }
        [Browsable(true)]
        [DisplayName("操作后延时"), Category("IO参数"), Description(""), ReadOnly(false)]
        public int delayAfter { get; set; }

        [DisplayName("操作后延时变量"), Category("IO参数"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        [Browsable(true)]
        public string delayAfterV { get; set; }

        public override string ToString()
        {
            return "";
        }

    }
    [Serializable]
    public class IoCheck : OperationType
    {
        public IoCheck()
        {
            OperaType = "IO检测";
            ParamListConverter<IoCheckParam>.Name = "IO";
        }
        private string iOCount;
        [DisplayName("数量"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(IOCountItem))]
        public string IOCount
        {
            get { return iOCount; }
            set
            {
                iOCount = value;
                if (SF.isModify == ModifyKind.Operation || SF.isAddOps)
                {
                    int num = int.Parse(IOCount);
                    ((IoCheck)SF.frmDataGrid.OperationTemp).IoParams = new CustomList<IoCheckParam>();
                    CustomList<IoCheckParam> temp = ((IoCheck)SF.frmDataGrid.OperationTemp).IoParams;
                    temp.Clear();
                    for (int i = 0; i < num; i++)
                    {
                        temp.Add(new IoCheckParam());
                    }
                    SF.frmPropertyGrid.propertyGrid1.SelectedObject = SF.frmDataGrid.OperationTemp;
                    SF.frmPropertyGrid.propertyGrid1.ExpandAllGridItems();
                }
            }
        }
        [DisplayName("超时设置"), Category("参数"), Description(""), ReadOnly(true)]
        public TimeOutC timeOutC { get; set; } = new TimeOutC();
        [DisplayName("IO设置"), Category("参数"), Description(""), ReadOnly(false)]
        [TypeConverter(typeof(ParamListConverter<IoCheckParam>))]

        public CustomList<IoCheckParam> IoParams { get; set; }

    }

    [Serializable]
    public class IoLogicGoto : OperationType
    {
        public IoLogicGoto()
        {
            OperaType = "IO逻辑跳转";
            ParamListConverter<IoLogicGotoParam>.Name = "IO";
        }

        private string iOCount;
        [DisplayName("IO数量"), Category("B判断参数"), Description(""), ReadOnly(false), TypeConverter(typeof(IOCountItem))]
        public string IOCount
        {
            get { return iOCount; }
            set
            {
                iOCount = value;
                if (SF.isModify == ModifyKind.Operation || SF.isAddOps)
                {
                    int num = int.Parse(IOCount);
                    ((IoLogicGoto)SF.frmDataGrid.OperationTemp).IoParams = new CustomList<IoLogicGotoParam>();
                    CustomList<IoLogicGotoParam> temp = ((IoLogicGoto)SF.frmDataGrid.OperationTemp).IoParams;
                    temp.Clear();
                    for (int i = 0; i < num; i++)
                    {
                        temp.Add(new IoLogicGotoParam());
                    }
                    SF.frmPropertyGrid.propertyGrid1.SelectedObject = SF.frmDataGrid.OperationTemp;
                    SF.frmPropertyGrid.propertyGrid1.ExpandAllGridItems();
                }
            }
        }

        [DisplayName("失败延时(ms)"), Category("B判断参数"), Description(""), ReadOnly(false)]
        public int InvalidDelayMs { get; set; }

        [DisplayName("IO设置"), Category("B判断参数"), Description(""), ReadOnly(false)]
        [TypeConverter(typeof(ParamListConverter<IoLogicGotoParam>))]
        public CustomList<IoLogicGotoParam> IoParams { get; set; }

        [DisplayName("true跳转"), Category("A跳转位置"), Description(""), ReadOnly(false), TypeConverter(typeof(GotoItem))]
        [MarkedGoto("标识的跳转属性")]
        public string TrueGoto { get; set; }

        [DisplayName("false跳转"), Category("A跳转位置"), Description(""), ReadOnly(false), TypeConverter(typeof(GotoItem))]
        [MarkedGoto("标识的跳转属性")]
        public string FalseGoto { get; set; }
    }

    [TypeConverter(typeof(SerializableExpandableObjectConverter))]
    [Serializable]
    public class IoLogicGotoParam
    {
        [DisplayName("名称"), Category("IO参数"), Description(""), ReadOnly(false), TypeConverter(typeof(IoItem))]
        public string IOName { get; set; }

        [DisplayName("目标"), Category("IO参数"), Description(""), ReadOnly(false)]
        public bool Target { get; set; }

        [DisplayName("逻辑"), Category("IO参数"), Description(""), ReadOnly(false), TypeConverter(typeof(LogicOperator))]
        public string Logic { get; set; } = "与";

        public override string ToString()
        {
            return "";
        }
    }
    [TypeConverter(typeof(SerializableExpandableObjectConverter))]
    [Serializable]
    public class IoCheckParam
    {
        [DisplayName("名称"), Category("IO参数"), Description(""), ReadOnly(false), TypeConverter(typeof(IoInItem))]
        public string IOName { get; set; }
        [DisplayName("检测状态"), Category("IO参数"), Description(""), ReadOnly(false)]
        public bool value { get; set; }
        //[DisplayName("检测前延时"), Category("IO参数"), Description(""), ReadOnly(false)]
        //public int delayBefore { get; set; }
        //[DisplayName("操作前延时变量"), Category("IO参数"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        //[Browsable(true)]
        //public string delayBeforeV { get; set; }
        //[DisplayName("检测后延时"), Category("IO参数"), Description(""), ReadOnly(false)]

        //public int delayAfter { get; set; }
        //[DisplayName("操作后延时变量"), Category("IO参数"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        //[Browsable(true)]
        //public string delayAfterV { get; set; }

        public override string ToString()
        {
            return "";
        }

    }
    [Serializable]
    public class ProcOps : OperationType
    {
        public ProcOps()
        {
            OperaType = "流程操作";
            ParamListConverter<procParam>.Name = "流程";
        }
        private string procCount;
        [DisplayName("数量"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(ProcItemCount))]
        public string ProcCount
        {
            get { return procCount; }
            set
            {
                procCount = value;
                if (SF.isModify == ModifyKind.Operation || SF.isAddOps)
                {
                    int num = int.Parse(procCount);
                    ((ProcOps)SF.frmDataGrid.OperationTemp).procParams = new CustomList<procParam>();
                    CustomList<procParam> temp = ((ProcOps)SF.frmDataGrid.OperationTemp).procParams;
                    temp.Clear();
                    for (int i = 0; i < num; i++)
                    {
                        temp.Add(new procParam() { delayAfter = -1});
                    }
                    SF.frmPropertyGrid.propertyGrid1.SelectedObject = SF.frmDataGrid.OperationTemp;
                    SF.frmPropertyGrid.propertyGrid1.ExpandAllGridItems();
                }
            }
        }
        [DisplayName("设置"), Category("参数"), Description(""), ReadOnly(false)]
        [TypeConverter(typeof(ParamListConverter<procParam>))]

        public CustomList<procParam> procParams { get; set; }

    }
    [TypeConverter(typeof(SerializableExpandableObjectConverter))]
    [Serializable]
    public class procParam
    {
        [DisplayName("流程名称"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(ProcItem))]
        public string ProcName { get; set; }
        [DisplayName("流程变量"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string ProcValue { get; set; }

        [DisplayName("操作类型"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(ProcItemOps))]
        public string value { get; set; }

        [DisplayName("操作后延时"), Category("参数"), Description(""), ReadOnly(false)]
        public int delayAfter { get; set; }
        [DisplayName("操作后延时变量"), Category("IO参数"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        [Browsable(true)]
        public string delayAfterV { get; set; }

        public override string ToString()
        {
            return "";
        }
    }
    [Serializable]
    public class WaitProc : OperationType
    {
        public WaitProc()
        {
            OperaType = "等待流程状态";
            ParamListConverter<WaitProcParam>.Name = "流程";
        }
        private string procCount;
        [DisplayName("数量"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(ProcItemCount))]
        public string ProcCount
        {
            get { return procCount; }
            set
            {
                procCount = value;
                if (SF.isModify == ModifyKind.Operation || SF.isAddOps)
                {
                    int num = int.Parse(procCount);
                    ((WaitProc)SF.frmDataGrid.OperationTemp).Params = new CustomList<WaitProcParam>();
                    CustomList<WaitProcParam> temp = ((WaitProc)SF.frmDataGrid.OperationTemp).Params;
                    temp.Clear();
                    for (int i = 0; i < num; i++)
                    {
                        temp.Add(new WaitProcParam());
                    }
                    SF.frmPropertyGrid.propertyGrid1.SelectedObject = SF.frmDataGrid.OperationTemp;
                    SF.frmPropertyGrid.propertyGrid1.ExpandAllGridItems();
                }
            }
        }
        [DisplayName("操作后延时"), Category("参数"), Description(""), ReadOnly(false)]
        public int delayAfter { get; set; }
        [DisplayName("操作后延时变量"), Category("IO参数"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        [Browsable(true)]
        public string delayAfterV { get; set; }

        [DisplayName("超时设置"), Category("参数"), Description(""), ReadOnly(false)]
        public TimeOutC timeOutC { get; set; } = new TimeOutC() { TimeOut = -1 };
        [DisplayName("设置"), Category("参数"), Description(""), ReadOnly(false)]
        [TypeConverter(typeof(ParamListConverter<WaitProcParam>))]

        public CustomList<WaitProcParam> Params { get; set; }

    }
    [TypeConverter(typeof(SerializableExpandableObjectConverter))]
    [Serializable]
    public class WaitProcParam
    {
        [DisplayName("流程名称"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(ProcItem))]
        public string ProcName { get; set; }
        [DisplayName("流程变量"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string ProcValue { get; set; }

        [DisplayName("操作类型"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(ProcItemOps))]
        public string value { get; set; }

        public override string ToString()
        {
            return "";
        }
    }
    [Serializable]
    public class Goto : OperationType
    {
        public Goto()
        {
            OperaType = "跳转";
            ParamListConverter<GotoParam>.Name = "跳转";
        }

        [DisplayName("变量索引"), Category("参数"), Description(""), ReadOnly(false)]
        public string ValueIndex { get; set; }

        [DisplayName("变量索引二级"), Category("参数"), Description(""), ReadOnly(false)]
        public string ValueIndex2Index { get; set; }

        [DisplayName("变量名称"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string ValueName { get; set; }

        [DisplayName("变量名称二级"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string ValueName2Index { get; set; }

        private string count;
        [DisplayName("匹配数量"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(MathcCount))]
        public string Count
        {
            get { return count; }
            set
            {
                count = value;
                if (SF.isModify == ModifyKind.Operation || SF.isAddOps)
                {
                    int num = int.Parse(count);
                    ((Goto)SF.frmDataGrid.OperationTemp).Params = new CustomList<GotoParam>();
                    CustomList<GotoParam> temp = ((Goto)SF.frmDataGrid.OperationTemp).Params;
                    temp.Clear();
                    for (int i = 0; i < num; i++)
                    {
                        temp.Add(new GotoParam());
                    }
                    SF.frmPropertyGrid.propertyGrid1.SelectedObject = SF.frmDataGrid.OperationTemp;
                    SF.frmPropertyGrid.propertyGrid1.ExpandAllGridItems();
                }
            }
        }

        [DisplayName("默认跳转"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(GotoItem))]
        [MarkedGoto("标识的跳转属性")]
        public string DefaultGoto { get; set; }


        [DisplayName("设置"), Category("参数"), Description(""), ReadOnly(false)]
        [TypeConverter(typeof(ParamListConverter<GotoParam>))]

        public CustomList<GotoParam> Params { get; set; }

    }
    [TypeConverter(typeof(SerializableExpandableObjectConverter))]
    [Serializable]
    public class GotoParam
    {
        [DisplayName("匹配值"), Category("参数"), Description(""), ReadOnly(false)]
        public string MatchValue { get; set; }

        [DisplayName("匹配值索引"), Category("参数"), Description(""), ReadOnly(false)]
        public string MatchValueIndex { get; set; }

        [DisplayName("匹配值变量"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string MatchValueV { get; set; }

        [DisplayName("跳转位置"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(GotoItem))]
        [MarkedGoto("标识的跳转属性")]
        public string Goto { get; set; }

        public override string ToString()
        {
            return "";
        }
    }

    [Serializable]
    public class ParamGoto : OperationType
    {
        public ParamGoto()
        {
            OperaType = "逻辑判断";
            ParamListConverter<ParamGotoParam>.Name = "条件";
        }
        [DisplayName("成功跳转"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(GotoItem))]
        public string goto1 { get; set; }

        [DisplayName("失败跳转"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(GotoItem))]
        public string goto2 { get; set; }

        [DisplayName("失败延时(ms)"), Category("参数"), Description(""), ReadOnly(false)]
        public string failDelay { get; set; }

        private string count;
        [DisplayName("匹配数量"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(MathcCount))]
        public string Count
        {
            get { return count; }
            set
            {
                count = value;
                if (SF.isModify == ModifyKind.Operation || SF.isAddOps)
                {
                    int num = int.Parse(count);
                    ((ParamGoto)SF.frmDataGrid.OperationTemp).Params = new CustomList<ParamGotoParam>();
                    CustomList<ParamGotoParam> temp = ((ParamGoto)SF.frmDataGrid.OperationTemp).Params;
                    temp.Clear();
                    for (int i = 0; i < num; i++)
                    {
                        temp.Add(new ParamGotoParam());
                    }
                    SF.frmPropertyGrid.propertyGrid1.SelectedObject = SF.frmDataGrid.OperationTemp;
                    SF.frmPropertyGrid.propertyGrid1.ExpandAllGridItems();
                }
            }
        }

        [DisplayName("设置"), Category("参数"), Description(""), ReadOnly(false)]
        [TypeConverter(typeof(ParamListConverter<ParamGotoParam>))]

        public CustomList<ParamGotoParam> Params { get; set; }

    }
    [TypeConverter(typeof(SerializableExpandableObjectConverter))]
    [Serializable]
    public class ParamGotoParam
    {
        [DisplayName("变量索引"), Category("参数"), Description(""), ReadOnly(false)]
        public string ValueIndex { get; set; }

        [DisplayName("变量索引二级"), Category("参数"), Description(""), ReadOnly(false)]
        public string ValueIndex2Index { get; set; }

        [DisplayName("变量名称"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string ValueName { get; set; }

        [DisplayName("变量名称二级"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string ValueName2Index { get; set; }

        [DisplayName("判断模式"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(JudgeMode))]
        public string JudgeMode { get; set; }

        [DisplayName("上限"), Category("参数"), Description(""), ReadOnly(false)]
        public double Up { get; set; }

        [DisplayName("下限"), Category("参数"), Description(""), ReadOnly(false)]
        public double Down { get; set; }

        [DisplayName("特征字符"), Category("参数"), Description(""), ReadOnly(false)]
        public string keyString { get; set; }

        [DisplayName("带等号"), Category("参数"), Description(""), ReadOnly(false)]
        public bool equal { get; set; }

        [DisplayName("运算符"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(Operator))]
        public string Operator { get; set; } = "且";

        public override string ToString()
        {
            return "";
        }
    }
    [Serializable]
    public class Delay : OperationType
    {
        public Delay()
        {
            OperaType = "延时";
        }
        [DisplayName("延时时间(ms)"), Category("参数"), Description(""), ReadOnly(false)]
        public string timeMiniSecond { get; set; }

        [DisplayName("延时时间变量"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string timeMiniSecondV { get; set; }

    }
    [Serializable]
    public class PopupDialog : OperationType
    {
        public PopupDialog()
        {
            OperaType = "弹框";
            popupType = "弹是";
            infoType = "自定义提示信息";
            alarmLightEnable = "禁用";
            buzzerTimeType = "自定义时间";
            PopupBackColor = Color.White;
            PopupFontColor = Color.Black;
            Btn1Text = "是";
            Btn2Text = "否";
            Btn3Text = "取消";
            evtRP += RefleshPropertyPopup;
            RefleshPropertyPopup();
        }

        private string popupType;
        [DisplayName("弹框类型"), Category("弹框相关设置"), Description(""), ReadOnly(false), TypeConverter(typeof(PopupTypeItem))]
        [Browsable(true)]
        public string PopupType
        {
            get => popupType;
            set
            {
                if (popupType != value)
                {
                    popupType = value;
                    if (SF.isModify == ModifyKind.Operation || SF.isAddOps)
                    {
                        RefleshPropertyPopup();
                    }
                }
            }
        }

        [Browsable(true)]
        [DisplayName("弹框背景颜色"), Category("弹框相关设置"), Description(""), ReadOnly(false)]
        public Color PopupBackColor { get; set; }

        [Browsable(true)]
        [DisplayName("弹框字体颜色"), Category("弹框相关设置"), Description(""), ReadOnly(false)]
        public Color PopupFontColor { get; set; }

        [Browsable(true)]
        [DisplayName("按钮1文本"), Category("弹框相关设置"), Description(""), ReadOnly(false)]
        public string Btn1Text { get; set; }

        [Browsable(false)]
        [DisplayName("按钮2文本"), Category("弹框相关设置"), Description(""), ReadOnly(false)]
        public string Btn2Text { get; set; }

        [Browsable(false)]
        [DisplayName("按钮3文本"), Category("弹框相关设置"), Description(""), ReadOnly(false)]
        public string Btn3Text { get; set; }

        [Browsable(true)]
        [DisplayName("确定跳转"), Category("弹框跳转设置"), Description(""), ReadOnly(false), TypeConverter(typeof(GotoItem))]
        [MarkedGoto("标识的跳转属性")]
        public string PopupGoto1 { get; set; }

        [Browsable(false)]
        [DisplayName("否跳转"), Category("弹框跳转设置"), Description(""), ReadOnly(false), TypeConverter(typeof(GotoItem))]
        [MarkedGoto("标识的跳转属性")]
        public string PopupGoto2 { get; set; }

        [Browsable(false)]
        [DisplayName("取消跳转"), Category("弹框跳转设置"), Description(""), ReadOnly(false), TypeConverter(typeof(GotoItem))]
        [MarkedGoto("标识的跳转属性")]
        public string PopupGoto3 { get; set; }

        private string infoType;
        [DisplayName("提示信息类型"), Category("弹框信息设置"), Description(""), ReadOnly(false), TypeConverter(typeof(PopupInfoTypeItem))]
        [Browsable(true)]
        public string InfoType
        {
            get => infoType;
            set
            {
                if (infoType != value)
                {
                    infoType = value;
                    if (SF.isModify == ModifyKind.Operation || SF.isAddOps)
                    {
                        RefleshPropertyPopup();
                    }
                }
            }
        }

        [Browsable(true)]
        [DisplayName("弹框提示信息"), Category("弹框信息设置"), Description(""), ReadOnly(false)]
        public string PopupMessage { get; set; }

        [Browsable(false)]
        [DisplayName("提示变量"), Category("弹框信息设置"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string PopupMessageValue { get; set; }

        [Browsable(false)]
        [DisplayName("报警信息ID"), Category("弹框信息设置"), Description(""), ReadOnly(false), TypeConverter(typeof(AlarmInfoItem))]
        public string PopupAlarmInfoID { get; set; }

        [Browsable(true)]
        [DisplayName("延时后关闭"), Category("弹框操作"), Description(""), ReadOnly(false)]
        public bool DelayClose
        {
            get => delayClose;
            set
            {
                if (delayClose != value)
                {
                    delayClose = value;
                    if (SF.isModify == ModifyKind.Operation || SF.isAddOps)
                    {
                        RefleshPropertyPopup();
                    }
                }
            }
        }

        [Browsable(false)]
        [DisplayName("延时关闭时间(ms)"), Category("弹框操作"), Description(""), ReadOnly(false)]
        public int DelayCloseTimeMs { get; set; }

        [DisplayName("保存到报警文件"), Category("弹框操作"), Description(""), ReadOnly(false)]
        public bool SaveToAlarmFile { get; set; }

        private string alarmLightEnable;
        [DisplayName("启动报警灯"), Category("启动报警灯"), Description(""), ReadOnly(false), TypeConverter(typeof(EnableItem))]
        [Browsable(true)]
        public string AlarmLightEnable
        {
            get => alarmLightEnable;
            set
            {
                if (alarmLightEnable != value)
                {
                    alarmLightEnable = value;
                    if (SF.isModify == ModifyKind.Operation || SF.isAddOps)
                    {
                        RefleshPropertyPopup();
                    }
                }
            }
        }

        [Browsable(false)]
        [DisplayName("蜂鸣器IO"), Category("启动报警灯"), Description(""), ReadOnly(false), TypeConverter(typeof(IoOutItem))]
        public string BuzzerIo { get; set; }

        [Browsable(false)]
        [DisplayName("红灯IO"), Category("启动报警灯"), Description(""), ReadOnly(false), TypeConverter(typeof(IoOutItem))]
        public string RedLightIo { get; set; }

        [Browsable(false)]
        [DisplayName("黄灯IO"), Category("启动报警灯"), Description(""), ReadOnly(false), TypeConverter(typeof(IoOutItem))]
        public string YellowLightIo { get; set; }

        [Browsable(false)]
        [DisplayName("绿灯IO"), Category("启动报警灯"), Description(""), ReadOnly(false), TypeConverter(typeof(IoOutItem))]
        public string GreenLightIo { get; set; }

        private string buzzerTimeType;
        [DisplayName("蜂鸣时间类型"), Category("蜂鸣时间设置"), Description(""), ReadOnly(false), TypeConverter(typeof(BuzzerTimeTypeItem))]
        [Browsable(true)]
        public string BuzzerTimeType
        {
            get => buzzerTimeType;
            set
            {
                if (buzzerTimeType != value)
                {
                    buzzerTimeType = value;
                    if (SF.isModify == ModifyKind.Operation || SF.isAddOps)
                    {
                        RefleshPropertyPopup();
                    }
                }
            }
        }

        [Browsable(false)]
        [DisplayName("蜂鸣时间(ms)"), Category("蜂鸣时间设置"), Description(""), ReadOnly(false)]
        public int BuzzerTimeMs { get; set; } = 1000;

        private bool delayClose;

        private void RefleshPropertyPopup()
        {
            bool isTwoButton = popupType == "弹是与否" || popupType == "弹是与否与取消";
            bool isThreeButton = popupType == "弹是与否与取消";
            bool useAlarmInfo = infoType == "报警信息库";
            bool useValueInfo = infoType == "变量类型";
            bool useCustomInfo = infoType == "自定义提示信息";

            SetPropertyAttribute(this, "PopupGoto2", typeof(BrowsableAttribute), "browsable", isTwoButton);
            SetPropertyAttribute(this, "PopupGoto3", typeof(BrowsableAttribute), "browsable", isThreeButton);

            SetPropertyAttribute(this, "PopupAlarmInfoID", typeof(BrowsableAttribute), "browsable", useAlarmInfo);
            SetPropertyAttribute(this, "PopupMessage", typeof(BrowsableAttribute), "browsable", useCustomInfo);
            SetPropertyAttribute(this, "PopupMessageValue", typeof(BrowsableAttribute), "browsable", useValueInfo);

            bool showBtnText = !useAlarmInfo;
            SetPropertyAttribute(this, "Btn1Text", typeof(BrowsableAttribute), "browsable", showBtnText);
            SetPropertyAttribute(this, "Btn2Text", typeof(BrowsableAttribute), "browsable", showBtnText && isTwoButton);
            SetPropertyAttribute(this, "Btn3Text", typeof(BrowsableAttribute), "browsable", showBtnText && isThreeButton);

            SetPropertyAttribute(this, "DelayCloseTimeMs", typeof(BrowsableAttribute), "browsable", delayClose);

            bool lightEnabled = alarmLightEnable == "启用";
            SetPropertyAttribute(this, "BuzzerIo", typeof(BrowsableAttribute), "browsable", lightEnabled);
            SetPropertyAttribute(this, "RedLightIo", typeof(BrowsableAttribute), "browsable", lightEnabled);
            SetPropertyAttribute(this, "YellowLightIo", typeof(BrowsableAttribute), "browsable", lightEnabled);
            SetPropertyAttribute(this, "GreenLightIo", typeof(BrowsableAttribute), "browsable", lightEnabled);

            bool showBuzzerTime = lightEnabled && buzzerTimeType == "自定义时间";
            SetPropertyAttribute(this, "BuzzerTimeMs", typeof(BrowsableAttribute), "browsable", showBuzzerTime);

        }
    }
    [Serializable]
    public class GetValue : OperationType
    {
        public GetValue()
        {
            OperaType = "获取变量";
            ParamListConverter<GetValueParam>.Name = "变量";
        }
        private string count;
        [DisplayName("数量"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(Count))]
        public string Count
        {
            get { return count; }
            set
            {
                count = value;
                if (SF.isModify == ModifyKind.Operation || SF.isAddOps)
                {
                    int num = int.Parse(count);
                    ((GetValue)SF.frmDataGrid.OperationTemp).Params = new CustomList<GetValueParam>();
                    CustomList<GetValueParam> temp = ((GetValue)SF.frmDataGrid.OperationTemp).Params;
                    temp.Clear();
                    for (int i = 0; i < num; i++)
                    {
                        temp.Add(new GetValueParam());
                    }
                    SF.frmPropertyGrid.propertyGrid1.SelectedObject = SF.frmDataGrid.OperationTemp;
                    SF.frmPropertyGrid.propertyGrid1.ExpandAllGridItems();
                }
            }
        }

        [DisplayName("设置"), Category("参数"), Description(""), ReadOnly(false)]
        [TypeConverter(typeof(ParamListConverter<GetValueParam>))]

        public CustomList<GetValueParam> Params { get; set; }

    }
    [TypeConverter(typeof(SerializableExpandableObjectConverter))]
    [Serializable]
    public class GetValueParam
    {
        [DisplayName("源变量索引"), Category("源变量"), Description(""), ReadOnly(false)]
        public string ValueSourceIndex { get; set; }

        [DisplayName("源变量索引二级"), Category("源变量"), Description(""), ReadOnly(false)]
        public string ValueSourceIndex2Index { get; set; }

        [DisplayName("源变量名称"), Category("源变量"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string ValueSourceName { get; set; }

        [DisplayName("源变量名称二级"), Category("源变量"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string ValueSourceName2Index { get; set; }


        [DisplayName("存储变量索引"), Category("存储变量"), Description(""), ReadOnly(false)]
        public string ValueSaveIndex { get; set; }

        [DisplayName("存储变量索引二级"), Category("存储变量"), Description(""), ReadOnly(false)]
        public string ValueSaveIndex2Index { get; set; }

        [DisplayName("存储变量名称"), Category("存储变量"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string ValueSaveName { get; set; }

        [DisplayName("存储变量名称二级"), Category("存储变量"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string ValueSaveName2Index { get; set; }

        public override string ToString()
        {
            return "";
        }
    }

    [Serializable]
    public class ModifyValue : OperationType
    {
        public ModifyValue()
        {
            OperaType = "修改寄存器";
        }

        [DisplayName("修改模式"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(ModifyType))]

        public string ModifyType { get; set; }

        [DisplayName("原变量取反"), Category("参数"), Description(""), ReadOnly(false)]

        public bool sourceR { get; set; }

        [DisplayName("修改值取反"), Category("参数"), Description(""), ReadOnly(false)]
        public bool ChangeR { get; set; }


        [DisplayName("源变量索引"), Category("A源变量"), Description(""), ReadOnly(false)]
        public string ValueSourceIndex { get; set; }

        [DisplayName("源变量索引二级"), Category("A源变量"), Description(""), ReadOnly(false)]
        public string ValueSourceIndex2Index { get; set; }

        [DisplayName("源变量名称"), Category("A源变量"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string ValueSourceName { get; set; }

        [DisplayName("源变量名称二级"), Category("A源变量"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string ValueSourceName2Index { get; set; }




        [DisplayName("修改值"), Category("B修改参数"), Description(""), ReadOnly(false)]
        public string ChangeValue { get; set; }

        [DisplayName("修改变量索引"), Category("B修改参数"), Description(""), ReadOnly(false)]
        public string ChangeValueIndex { get; set; }

        [DisplayName("修改变量索引二级"), Category("B修改参数"), Description(""), ReadOnly(false)]
        public string ChangeValueIndex2Index { get; set; }

        [DisplayName("修改变量名称"), Category("B修改参数"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string ChangeValueName { get; set; }

        [DisplayName("修改变量名称二级"), Category("B修改参数"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string ChangeValueName2Index { get; set; }



        [DisplayName("结果变量索引"), Category("C保存参数"), Description(""), ReadOnly(false)]
        public string OutputValueIndex { get; set; }

        [DisplayName("结果变量索引二级"), Category("C保存参数"), Description(""), ReadOnly(false)]
        public string OutputValueIndex2Index { get; set; }

        [DisplayName("结果变量名称"), Category("C保存参数"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string OutputValueName { get; set; }

        [DisplayName("结果变量名称二级"), Category("C保存参数"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string OutputValueName2Index { get; set; }
    }

    [Serializable]
    public class StringFormat : OperationType
    {
        public StringFormat()
        {
            OperaType = "数据拼接";
            ParamListConverter<StringFormatParam>.Name = "变量";
        }
        [DisplayName("拼接格式"), Category("参数"), Description(""), ReadOnly(false)]
        public string Format { get; set; }

        [DisplayName("存储变量索引"), Category("参数"), Description(""), ReadOnly(false)]
        public string OutputValueIndex { get; set; }
        [DisplayName("存储变量"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string OutputValueName { get; set; }
        private string count;
        [DisplayName("数量"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(Count))]
        public string Count
        {
            get { return count; }
            set
            {
                count = value;
                if (SF.isModify == ModifyKind.Operation || SF.isAddOps)
                {
                    int num = int.Parse(count);
                    ((StringFormat)SF.frmDataGrid.OperationTemp).Params = new CustomList<StringFormatParam>();
                    CustomList<StringFormatParam> temp = ((StringFormat)SF.frmDataGrid.OperationTemp).Params;
                    temp.Clear();
                    for (int i = 0; i < num; i++)
                    {
                        temp.Add(new StringFormatParam());
                    }
                    SF.frmPropertyGrid.propertyGrid1.SelectedObject = SF.frmDataGrid.OperationTemp;
                    SF.frmPropertyGrid.propertyGrid1.ExpandAllGridItems();
                }
            }
        }
        [DisplayName("设置"), Category("参数"), Description(""), ReadOnly(false)]
        [TypeConverter(typeof(ParamListConverter<StringFormatParam>))]

        public CustomList<StringFormatParam> Params { get; set; }

    }
    [TypeConverter(typeof(SerializableExpandableObjectConverter))]
    [Serializable]
    public class StringFormatParam
    {
        [DisplayName("源变量索引"), Category("参数"), Description(""), ReadOnly(false)]
        public string ValueSourceIndex { get; set; }
        [DisplayName("源变量"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string ValueSourceName { get; set; }
        public override string ToString()
        {
            return "";
        }
    }

    [Serializable]
    public class Split : OperationType
    {
        public Split()
        {
            OperaType = "字符串分割";
        }

        [DisplayName("分割符"), Category("参数"), Description(""), ReadOnly(false)]

        public char SplitMark { get; set; }

        [DisplayName("分割起始索引"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(MathcCount))]

        public string startIndex { get; set; }

        [DisplayName("分割数量"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(Count))]
        public string Count { get; set; }
        [DisplayName("源变量索引"), Category("参数"), Description(""), ReadOnly(false)]
        public string SourceValueIndex { get; set; }
        [DisplayName("源变量"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string SourceValue { get; set; }
        [DisplayName("结果变量索引"), Category("参数"), Description(""), ReadOnly(false)]
        public string OutputIndex { get; set; }
        [DisplayName("结果变量"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string Output { get; set; }
    }

    [Serializable]
    public class Replace : OperationType
    {
        public Replace()
        {
            OperaType = "字符串替换";
            evtRP += RefleshProperty;
        }
        [DisplayName("被替换字符串"), Category("A被替换字符串"), Description(""), ReadOnly(false)]
        [Browsable(true)]
        public string ReplaceStr { get; set; }
        [DisplayName("被替换字符串索引"), Category("A被替换字符串"), Description(""), ReadOnly(false)]
        [Browsable(true)]
        public string ReplaceStrIndex { get; set; }
        [DisplayName("被替换字符串变量"), Category("A被替换字符串"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        [Browsable(true)]
        public string ReplaceStrV { get; set; }

        [DisplayName("新字符串"), Category("B新字符串"), Description(""), ReadOnly(false)]
        [Browsable(true)]
        public string NewStr { get; set; }

        [DisplayName("新字符串索引"), Category("B新字符串"), Description(""), ReadOnly(false)]
        [Browsable(true)]
        public string NewStrIndex { get; set; }

        [DisplayName("新字符串变量"), Category("B新字符串"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        [Browsable(true)]
        public string NewStrV { get; set; }

        [DisplayName("替换起始索引"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(MathcCount))]
        [Browsable(true)]
        public string StartIndex { get; set; }

        [DisplayName("替换数量"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(MathcCount))]
        [Browsable(true)]
        public string Count { get; set; }

        private string replaceType = "替换指定字符";
        [DisplayName("替换类型"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(ReplaceType))]
        [Browsable(true)]
        public string ReplaceType
        {
            get => replaceType;
            set
            {
                if (replaceType != value)
                {
                    replaceType = value;
                    if (SF.isModify == ModifyKind.Operation || SF.isAddOps)
                    {
                        RefleshProperty();
                    }
                }
            }
        }

        [DisplayName("源变量索引"), Category("参数"), Description(""), ReadOnly(false)]
        [Browsable(true)]
        public string SourceValueIndex { get; set; }
        [DisplayName("源变量"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        [Browsable(true)]
        public string SourceValue { get; set; }

        [DisplayName("结果变量索引"), Category("参数"), Description(""), ReadOnly(false)]
        [Browsable(true)]
        public string OutputIndex { get; set; }
        [DisplayName("结果变量"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        [Browsable(true)]
        public string Output { get; set; }

        public void RefleshProperty()
        {
            if (this.replaceType == "替换指定字符")
            {
                SetPropertyAttribute(this, "StartIndex", typeof(BrowsableAttribute), "browsable", false);
                SetPropertyAttribute(this, "Count", typeof(BrowsableAttribute), "browsable", false);
                SetPropertyAttribute(this, "ReplaceStr", typeof(BrowsableAttribute), "browsable", true);
                SetPropertyAttribute(this, "ReplaceStrIndex", typeof(BrowsableAttribute), "browsable", true);
                SetPropertyAttribute(this, "ReplaceStrV", typeof(BrowsableAttribute), "browsable", true);
            }
            else
            {
                SetPropertyAttribute(this, "StartIndex", typeof(BrowsableAttribute), "browsable", true);
                SetPropertyAttribute(this, "Count", typeof(BrowsableAttribute), "browsable", true);
                SetPropertyAttribute(this, "ReplaceStr", typeof(BrowsableAttribute), "browsable", false);
                SetPropertyAttribute(this, "ReplaceStrIndex", typeof(BrowsableAttribute), "browsable", false);
                SetPropertyAttribute(this, "ReplaceStrV", typeof(BrowsableAttribute), "browsable", false);
            }
        }
    }
    [Serializable]
    public class SetDataStructItem : OperationType
    {
        public SetDataStructItem()
        {
            OperaType = "设置结构体数据项";
            ParamListConverter<SetDataStructItemParam>.Name = "数据";
        }
        [DisplayName("结构体索引"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(MathcCount))]
        public string StructIndex { get; set; }

        [DisplayName("数据项索引"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(MathcCount))]
        public string ItemIndex { get; set; }

        private string count;
        [DisplayName("数量"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(Count))]
        public string Count
        {
            get { return count; }
            set
            {
                count = value;
                if (SF.isModify == ModifyKind.Operation || SF.isAddOps)
                {
                    int num = int.Parse(count);
                    ((SetDataStructItem)SF.frmDataGrid.OperationTemp).Params = new CustomList<SetDataStructItemParam>();
                    CustomList<SetDataStructItemParam> temp = ((SetDataStructItem)SF.frmDataGrid.OperationTemp).Params;
                    temp.Clear();
                    for (int i = 0; i < num; i++)
                    {
                        temp.Add(new SetDataStructItemParam());
                    }
                    SF.frmPropertyGrid.propertyGrid1.SelectedObject = SF.frmDataGrid.OperationTemp;
                    SF.frmPropertyGrid.propertyGrid1.ExpandAllGridItems();
                }
            }
        }

        [DisplayName("设置"), Category("参数"), Description(""), ReadOnly(false)]
        [TypeConverter(typeof(ParamListConverter<SetDataStructItemParam>))]

        public CustomList<SetDataStructItemParam> Params { get; set; }

    }
    [TypeConverter(typeof(SerializableExpandableObjectConverter))]
    [Serializable]
    public class SetDataStructItemParam
    {
        [DisplayName("值索引"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(MathcCount))]
        public string valueIndex { get; set; }

        [DisplayName("值"), Category("参数"), Description(""), ReadOnly(false)]
        public string value { get; set; }

        public override string ToString()
        {
            return "";
        }
    }
    [Serializable]
    public class GetDataStructItem : OperationType
    {
        public GetDataStructItem()
        {
            OperaType = "获取结构体数据项";
            ParamListConverter<GetDataStructItemParam>.Name = "数据";
        }
        [DisplayName("是否获取所有项"), Category("参数"), Description(""), ReadOnly(false)]
        public bool IsAllItem { get; set; }

        [DisplayName("起始变量值"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string StartValue { get; set; }

        [DisplayName("结构体索引"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(MathcCount))]
        public string StructIndex { get; set; }

        [DisplayName("数据项索引"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(MathcCount))]
        public string ItemIndex { get; set; }

        private string count;
        [DisplayName("数量"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(Count))]
        public string Count
        {
            get { return count; }
            set
            {
                count = value;
                if (SF.isModify == ModifyKind.Operation || SF.isAddOps)
                {
                    int num = int.Parse(count);
                    ((GetDataStructItem)SF.frmDataGrid.OperationTemp).Params = new CustomList<GetDataStructItemParam>();
                    CustomList<GetDataStructItemParam> temp = ((GetDataStructItem)SF.frmDataGrid.OperationTemp).Params;
                    temp.Clear();
                    for (int i = 0; i < num; i++)
                    {
                        temp.Add(new GetDataStructItemParam());
                    }
                    SF.frmPropertyGrid.propertyGrid1.SelectedObject = SF.frmDataGrid.OperationTemp;
                    SF.frmPropertyGrid.propertyGrid1.ExpandAllGridItems();
                }
            }
        }

        [DisplayName("设置"), Category("参数"), Description(""), ReadOnly(false)]
        [TypeConverter(typeof(ParamListConverter<GetDataStructItemParam>))]

        public CustomList<GetDataStructItemParam> Params { get; set; }

    }
    [TypeConverter(typeof(SerializableExpandableObjectConverter))]
    [Serializable]
    public class GetDataStructItemParam
    {

        [DisplayName("数据项索引"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(MathcCount))]
        public string valueIndex { get; set; }

        [DisplayName("变量索引"), Category("参数"), Description(""), ReadOnly(false)]
        public string ValueIndex { get; set; }

        [DisplayName("变量索引二级"), Category("参数"), Description(""), ReadOnly(false)]
        public string ValueIndex2Index { get; set; }

        [DisplayName("变量名称"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string ValueName { get; set; }

        [DisplayName("变量名称二级"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string ValueName2Index { get; set; }

        public override string ToString()
        {
            return "";
        }
    }
    [Serializable]
    public class CopyDataStructItem : OperationType
    {
        public CopyDataStructItem()
        {
            OperaType = "复制结构体数据项";
            ParamListConverter<CopyDataStructItemParam>.Name = "数据";
        }
        [DisplayName("是否复制所有项"), Category("参数"), Description(""), ReadOnly(false)]
        public bool IsAllValue { get; set; }

        [DisplayName("源结构体索引"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(MathcCount))]
        public string SourceStructIndex { get; set; }

        [DisplayName("源数据项索引"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(MathcCount))]
        public string SourceItemIndex { get; set; }

        [DisplayName("目标结构体索引"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(MathcCount))]
        public string TargetStructIndex { get; set; }

        [DisplayName("目标数据项索引"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(MathcCount))]
        public string TargetItemIndex { get; set; }

        private string count;
        [DisplayName("数量"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(Count))]
        public string Count
        {
            get { return count; }
            set
            {
                count = value;
                if (SF.isModify == ModifyKind.Operation || SF.isAddOps)
                {
                    int num = int.Parse(count);
                    ((CopyDataStructItem)SF.frmDataGrid.OperationTemp).Params = new CustomList<CopyDataStructItemParam>();
                    CustomList<CopyDataStructItemParam> temp = ((CopyDataStructItem)SF.frmDataGrid.OperationTemp).Params;
                    temp.Clear();
                    for (int i = 0; i < num; i++)
                    {
                        temp.Add(new CopyDataStructItemParam());
                    }
                    SF.frmPropertyGrid.propertyGrid1.SelectedObject = SF.frmDataGrid.OperationTemp;
                    SF.frmPropertyGrid.propertyGrid1.ExpandAllGridItems();
                }
            }
        }

        [DisplayName("设置"), Category("参数"), Description(""), ReadOnly(false)]
        [TypeConverter(typeof(ParamListConverter<CopyDataStructItemParam>))]

        public CustomList<CopyDataStructItemParam> Params { get; set; }

    }
    [TypeConverter(typeof(SerializableExpandableObjectConverter))]
    [Serializable]
    public class CopyDataStructItemParam
    {

        [DisplayName("源数据项索引"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(MathcCount))]
        public string SourcevalueIndex { get; set; }

        [DisplayName("目标数据项索引"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(MathcCount))]
        public string Targetvalue { get; set; }

        public override string ToString()
        {
            return "";
        }
    }
    [Serializable]
    public class InsertDataStructItem : OperationType
    {
        public InsertDataStructItem()
        {
            OperaType = "插入结构体数据项";
            ParamListConverter<InsertDataStructItemParam>.Name = "数据";
        }

        [DisplayName("目标结构体名称"), Category("参数"), Description(""), ReadOnly(false)]
        public override string Name { get; set; }

        [DisplayName("目标结构体索引"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(MathcCount))]
        public string TargetStructIndex { get; set; }

        [DisplayName("目标数据项索引"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(MathcCount))]
        public string TargetItemIndex { get; set; }

        private string count;
        [DisplayName("数量"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(Count))]
        public string Count
        {
            get { return count; }
            set
            {
                count = value;
                if (SF.isModify == ModifyKind.Operation || SF.isAddOps)
                {
                    int num = int.Parse(count);
                    ((InsertDataStructItem)SF.frmDataGrid.OperationTemp).Params = new CustomList<InsertDataStructItemParam>();
                    CustomList<InsertDataStructItemParam> temp = ((InsertDataStructItem)SF.frmDataGrid.OperationTemp).Params;
                    temp.Clear();
                    for (int i = 0; i < num; i++)
                    {
                        temp.Add(new InsertDataStructItemParam());
                    }
                    SF.frmPropertyGrid.propertyGrid1.SelectedObject = SF.frmDataGrid.OperationTemp;
                    SF.frmPropertyGrid.propertyGrid1.ExpandAllGridItems();
                }
            }
        }

        [DisplayName("设置"), Category("参数"), Description(""), ReadOnly(false)]
        [TypeConverter(typeof(ParamListConverter<InsertDataStructItemParam>))]

        public CustomList<InsertDataStructItemParam> Params { get; set; }

    }
    [TypeConverter(typeof(SerializableExpandableObjectConverter))]
    [Serializable]
    public class InsertDataStructItemParam
    {
        [DisplayName("数据类型"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(SturctItemType))]
        public string Type { get; set; }

        [DisplayName("数据变量"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string ValueItem { get; set; }

        [DisplayName("数据值"), Category("参数"), Description(""), ReadOnly(false)]
        public string Value { get; set; }

        public override string ToString()
        {
            return "";
        }
    }
    [Serializable]
    public class DelDataStructItem : OperationType
    {
        public DelDataStructItem()
        {
            OperaType = "删除结构体数据项";
        }

        [DisplayName("目标结构体索引"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(MathcCount))]
        public string TargetStructIndex { get; set; }

        [DisplayName("目标数据项索引"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(MathcCount))]
        public string TargetItemIndex { get; set; }
    }

    [Serializable]
    public class FindDataStructItem : OperationType
    {
        public FindDataStructItem()
        {
            OperaType = "查找结构体数据项";
        }

        [DisplayName("目标结构体索引"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(MathcCount))]
        public string TargetStructIndex { get; set; }

        //[DisplayName("目标结构项索引"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(MathcCount))]
        //public string TargetItemIndex { get; set; }

        [DisplayName("查找类型"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(FindType))]
        public string Type { get; set; }

        [DisplayName("目标关键字"), Category("参数"), Description(""), ReadOnly(false)]
        public string key { get; set; }

        [DisplayName("结果保存地址"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string save { get; set; }

    }

    [Serializable]
    public class GetDataStructCount : OperationType
    {
        public GetDataStructCount()
        {
            OperaType = "获取结构体数量";
        }

        [DisplayName("目标结构体索引"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(MathcCount))]
        public string TargetStructIndex { get; set; }


        [DisplayName("结构体数量保存地址"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string StructCount { get; set; }

        [DisplayName("结构项数量保存地址"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string ItemCount { get; set; }

    }

    [Serializable]
    public class TcpOps : OperationType
    {
        public TcpOps()
        {
            OperaType = "网口通讯操作";
            ParamListConverter<TcpOpsParam>.Name = "网口";
        }

        private string count;
        [DisplayName("数量"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(Count))]
        public string Count
        {
            get { return count; }
            set
            {
                count = value;
                if (SF.isModify == ModifyKind.Operation || SF.isAddOps)
                {
                    int num = int.Parse(count);
                    ((TcpOps)SF.frmDataGrid.OperationTemp).Params = new CustomList<TcpOpsParam>();
                    CustomList<TcpOpsParam> temp = ((TcpOps)SF.frmDataGrid.OperationTemp).Params;
                    temp.Clear();
                    for (int i = 0; i < num; i++)
                    {
                        temp.Add(new TcpOpsParam());
                    }
                    SF.frmPropertyGrid.propertyGrid1.SelectedObject = SF.frmDataGrid.OperationTemp;
                    SF.frmPropertyGrid.propertyGrid1.ExpandAllGridItems();
                }
            }
        }
        [DisplayName("设置"), Category("参数"), Description(""), ReadOnly(false)]
        [TypeConverter(typeof(ParamListConverter<TcpOpsParam>))]

        public CustomList<TcpOpsParam> Params { get; set; }

    }
    [TypeConverter(typeof(SerializableExpandableObjectConverter))]
    [Serializable]
    public class TcpOpsParam
    {
        [DisplayName("对象名称"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(TcpItem))]

        public string Name { get; set; }

        [DisplayName("操作"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(CommunicationOps))]

        public string Ops { get; set; }
        public override string ToString()
        {
            return "";
        }
    }
    [Serializable]
    public class WaitTcp : OperationType
    {
        public WaitTcp()
        {
            OperaType = "等待网口连接";
            ParamListConverter<WaitTcpParam>.Name = "网口";
        }

        private string count;
        [DisplayName("数量"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(Count))]
        public string Count
        {
            get { return count; }
            set
            {
                count = value;
                if (SF.isModify == ModifyKind.Operation || SF.isAddOps)
                {
                    int num = int.Parse(count);
                    ((WaitTcp)SF.frmDataGrid.OperationTemp).Params = new CustomList<WaitTcpParam>();
                    CustomList<WaitTcpParam> temp = ((WaitTcp)SF.frmDataGrid.OperationTemp).Params;
                    temp.Clear();
                    for (int i = 0; i < num; i++)
                    {
                        temp.Add(new WaitTcpParam());
                    }
                    SF.frmPropertyGrid.propertyGrid1.SelectedObject = SF.frmDataGrid.OperationTemp;
                    SF.frmPropertyGrid.propertyGrid1.ExpandAllGridItems();
                }
            }
        }
        [DisplayName("设置"), Category("参数"), Description(""), ReadOnly(false)]
        [TypeConverter(typeof(ParamListConverter<WaitTcpParam>))]

        public CustomList<WaitTcpParam> Params { get; set; }

    }
    [TypeConverter(typeof(SerializableExpandableObjectConverter))]
    [Serializable]
    public class WaitTcpParam
    {
        [DisplayName("对象名称"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(TcpItem))]

        public string Name { get; set; }

        [DisplayName("超时"), Category("参数"), Description(""), ReadOnly(false)]

        public int TimeOut { get; set; }
        public override string ToString()
        {
            return "";
        }
    }

    [Serializable]
    public class SendTcpMsg : OperationType
    {
        public SendTcpMsg()
        {
            OperaType = "发送TCP通讯消息";
            TimeOut = 3000;
        }

        [DisplayName("ID"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(TcpItem))]

        public string ID { get; set; }

        [DisplayName("发送信息"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string Msg { get; set; }

        [DisplayName("是否16进制发送"), Category("参数"), Description(""), ReadOnly(false)]
        public bool isConVert { get; set; }

        [DisplayName("超时"), Category("参数"), Description(""), ReadOnly(false)]
        public int TimeOut { get; set; }

    }


    [Serializable]
    public class ReceoveTcpMsg : OperationType
    {
        public ReceoveTcpMsg()
        {
            OperaType = "接收TCP通讯消息";
        }

        [DisplayName("ID"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(TcpItem))]

        public string ID { get; set; }

        [DisplayName("接收信息"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string MsgSaveValue { get; set; }

        [DisplayName("是否16进制接收"), Category("参数"), Description(""), ReadOnly(false)]
        public bool isConVert { get; set; }

        [DisplayName("超时时间"), Category("参数"), Description(""), ReadOnly(false)]
        public int TImeOut { get; set; }


    }

    [Serializable]
    public class SerialPortOps : OperationType
    {
        public SerialPortOps()
        {
            OperaType = "串口通讯操作";
            ParamListConverter<SerialPortOpsParam>.Name = "串口";
        }

        private string count;
        [DisplayName("数量"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(Count))]
        public string Count
        {
            get { return count; }
            set
            {
                count = value;
                if (SF.isModify == ModifyKind.Operation || SF.isAddOps)
                {
                    int num = int.Parse(count);
                    ((SerialPortOps)SF.frmDataGrid.OperationTemp).Params = new CustomList<SerialPortOpsParam>();
                    CustomList<SerialPortOpsParam> temp = ((SerialPortOps)SF.frmDataGrid.OperationTemp).Params;
                    temp.Clear();
                    for (int i = 0; i < num; i++)
                    {
                        temp.Add(new SerialPortOpsParam());
                    }
                    SF.frmPropertyGrid.propertyGrid1.SelectedObject = SF.frmDataGrid.OperationTemp;
                    SF.frmPropertyGrid.propertyGrid1.ExpandAllGridItems();
                }
            }
        }
        [DisplayName("设置"), Category("参数"), Description(""), ReadOnly(false)]
        [TypeConverter(typeof(ParamListConverter<SerialPortOpsParam>))]

        public CustomList<SerialPortOpsParam> Params { get; set; }

    }
    [TypeConverter(typeof(SerializableExpandableObjectConverter))]
    [Serializable]
    public class SerialPortOpsParam
    {
        [DisplayName("对象名称"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(SerialPortItem))]

        public string Name { get; set; }

        [DisplayName("操作"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(CommunicationOps))]

        public string Ops { get; set; }
        public override string ToString()
        {
            return "";
        }
    }
    [Serializable]
    public class WaitSerialPort : OperationType
    {
        public WaitSerialPort()
        {
            OperaType = "等待串口连接";
            ParamListConverter<WaitSerialPortParam>.Name = "串口";
        }

        private string count;
        [DisplayName("数量"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(Count))]
        public string Count
        {
            get { return count; }
            set
            {
                count = value;
                if (SF.isModify == ModifyKind.Operation || SF.isAddOps)
                {
                    int num = int.Parse(count);
                    ((WaitSerialPort)SF.frmDataGrid.OperationTemp).Params = new CustomList<WaitSerialPortParam>();
                    CustomList<WaitSerialPortParam> temp = ((WaitSerialPort)SF.frmDataGrid.OperationTemp).Params;
                    temp.Clear();
                    for (int i = 0; i < num; i++)
                    {
                        temp.Add(new WaitSerialPortParam());
                    }
                    SF.frmPropertyGrid.propertyGrid1.SelectedObject = SF.frmDataGrid.OperationTemp;
                    SF.frmPropertyGrid.propertyGrid1.ExpandAllGridItems();
                }
            }
        }
        [DisplayName("设置"), Category("参数"), Description(""), ReadOnly(false)]
        [TypeConverter(typeof(ParamListConverter<WaitSerialPortParam>))]

        public CustomList<WaitSerialPortParam> Params { get; set; }

    }
    [TypeConverter(typeof(SerializableExpandableObjectConverter))]
    [Serializable]
    public class WaitSerialPortParam
    {
        [DisplayName("对象名称"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(SerialPortItem))]

        public string Name { get; set; }

        [DisplayName("超时"), Category("参数"), Description(""), ReadOnly(false)]

        public int TimeOut { get; set; }
        public override string ToString()
        {
            return "";
        }
    }

    [Serializable]
    public class SendSerialPortMsg : OperationType
    {
        public SendSerialPortMsg()
        {
            OperaType = "发送串口通讯消息";
            TimeOut = 3000;
        }

        [DisplayName("ID"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(SerialPortItem))]

        public string ID { get; set; }

        [DisplayName("发送信息"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string Msg { get; set; }

        [DisplayName("是否16进制发送"), Category("参数"), Description(""), ReadOnly(false)]
        public bool isConVert { get; set; }

        [DisplayName("超时"), Category("参数"), Description(""), ReadOnly(false)]
        public int TimeOut { get; set; }

    }


    [Serializable]
    public class ReceoveSerialPortMsg : OperationType
    {
        public ReceoveSerialPortMsg()
        {
            OperaType = "接收串口通讯消息";
        }

        [DisplayName("ID"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(SerialPortItem))]

        public string ID { get; set; }

        [DisplayName("接收信息"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string MsgSaveValue { get; set; }

        [DisplayName("是否16进制接收"), Category("参数"), Description(""), ReadOnly(false)]
        public bool isConVert { get; set; }

        [DisplayName("超时时间"), Category("参数"), Description(""), ReadOnly(false)]
        public int TImeOut { get; set; }


    }

    [Serializable]
    public class SendReceoveCommMsg : OperationType
    {
        public SendReceoveCommMsg()
        {
            OperaType = "发送与接收";
            CommType = "TCP";
            TimeOut = 3000;
        }

        [DisplayName("通讯类型"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(CommType))]
        public string CommType { get; set; }

        [DisplayName("ID"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(CommItem))]
        public string ID { get; set; }

        [DisplayName("发送信息"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string SendMsg { get; set; }

        [DisplayName("是否16进制发送"), Category("参数"), Description(""), ReadOnly(false)]
        public bool SendConvert { get; set; }

        [DisplayName("接收信息"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string ReceiveSaveValue { get; set; }

        [DisplayName("是否16进制接收"), Category("参数"), Description(""), ReadOnly(false)]
        public bool ReceiveConvert { get; set; }

        [DisplayName("超时时间"), Category("参数"), Description(""), ReadOnly(false)]
        public int TimeOut { get; set; }
    }

    [Serializable]
    public class PlcReadWrite : OperationType
    {
        public PlcReadWrite()
        {
            OperaType = "PLC读写";
            DataType = "Float";
            DataOps = "读PLC";
            Quantity = 1;
        }

        [DisplayName("PLC名字"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(PlcItem))]
        public string PlcName { get; set; }

        [DisplayName("数据类型"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(PlcDataTypeItem))]
        public string DataType { get; set; }

        [DisplayName("数据读写"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(PlcDirectionItem))]
        public string DataOps { get; set; }

        [DisplayName("PLC首地址"), Category("参数"), Description(""), ReadOnly(false)]
        public string PlcAddress { get; set; }

        [DisplayName("写入常量"), Category("参数"), Description(""), ReadOnly(false)]
        public string WriteConst { get; set; }

        [DisplayName("变量首地址"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string ValueName { get; set; }

        [DisplayName("数据数量"), Category("参数"), Description(""), ReadOnly(false)]
        public int Quantity { get; set; }
    }

    [Serializable]
    public class CreateTray : OperationType
    {
        public CreateTray()
        {
            OperaType = "创建料盘";
        }

        [DisplayName("工站名称"), Category("料盘参数设置"), Description(""), ReadOnly(false), TypeConverter(typeof(StationtItem))]
        public string StationName { get; set; }

        [DisplayName("料盘ID"), Category("料盘参数设置"), Description(""), ReadOnly(false)]
        public int TrayId { get; set; }

        [DisplayName("行数"), Category("料盘参数设置"), Description(""), ReadOnly(false)]
        public int RowCount { get; set; }

        [DisplayName("列数"), Category("料盘参数设置"), Description(""), ReadOnly(false)]
        public int ColCount { get; set; }

        [DisplayName("左上"), Category("料盘格点设置"), Description(""), ReadOnly(false), TypeConverter(typeof(StationPosDic))]
        public string PX1 { get; set; }

        [DisplayName("右上"), Category("料盘格点设置"), Description(""), ReadOnly(false), TypeConverter(typeof(StationPosDic))]
        public string PX2 { get; set; }

        [DisplayName("左下"), Category("料盘格点设置"), Description(""), ReadOnly(false), TypeConverter(typeof(StationPosDic))]
        public string PY1 { get; set; }

        [DisplayName("右下"), Category("料盘格点设置"), Description(""), ReadOnly(false), TypeConverter(typeof(StationPosDic))]
        public string PY2 { get; set; }
    }

    [Serializable]
    public class TrayRunPos : OperationType
    {
        public TrayRunPos()
        {
            OperaType = "走料盘点";
        }

        [DisplayName("工站名称"), Category("料盘参数设置"), Description(""), ReadOnly(false), TypeConverter(typeof(StationtItem))]
        public string StationName { get; set; }

        [DisplayName("料盘号"), Category("料盘参数设置"), Description(""), ReadOnly(false)]
        public int TrayId { get; set; }

        [DisplayName("料盘号索引"), Category("料盘参数设置"), Description(""), ReadOnly(false)]
        public string TrayIdValueIndex { get; set; }

        [DisplayName("料盘号索引二级"), Category("料盘参数设置"), Description(""), ReadOnly(false)]
        public string TrayIdValueIndex2Index { get; set; }

        [DisplayName("料盘号变量名称"), Category("料盘参数设置"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string TrayIdValueName { get; set; }

        [DisplayName("料盘号变量名称二级"), Category("料盘参数设置"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string TrayIdValueName2Index { get; set; }

        [DisplayName("料盘位置"), Category("料盘参数设置"), Description(""), ReadOnly(false)]
        public int TrayPos { get; set; }

        [DisplayName("料盘位置索引"), Category("料盘参数设置"), Description(""), ReadOnly(false)]
        public string TrayPosValueIndex { get; set; }

        [DisplayName("料盘位置索引二级"), Category("料盘参数设置"), Description(""), ReadOnly(false)]
        public string TrayPosValueIndex2Index { get; set; }

        [DisplayName("料盘位置变量名称"), Category("料盘参数设置"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string TrayPosValueName { get; set; }

        [DisplayName("料盘位置变量名称二级"), Category("料盘参数设置"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string TrayPosValueName2Index { get; set; }

        [DisplayName("不等待"), Category("料盘参数设置"), Description(""), ReadOnly(false)]
        public bool isUnWait { get; set; }
    }

    [Serializable]
    public class HomeRun : OperationType
    {
        public HomeRun()
        {
            OperaType = "回原";
        }

        [DisplayName("工站名称"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(StationtItem))]

        public string StationName { get; set; }
        [DisplayName("工站索引"), Category("参数"), Description(""), ReadOnly(false)]

        public int StationIndex { get; set; } = -1;

        [DisplayName("回原模式"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(StationHomeType))]
        public string StationHomeType { get; set; }

        [DisplayName("不等待"), Category("参数"), Description(""), ReadOnly(false)]
        public bool isUnWait { get; set; }

    }

    [Serializable]
    public class StationRunPos : OperationType
    {
        public StationRunPos()
        {
            OperaType = "工站走点";
            evtRP += RefleshPropertyName;
            evtRP += RefleshPropertyVel;
            timeOut = 120000;
        }
        [DisplayName("工站名称"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(StationtItem))]

        public string StationName { get; set; }
        //[DisplayName("工站索引"), Category("参数"), Description(""), ReadOnly(false)]

        //public int StationIndex { get; set; } = -1;

        [DisplayName("点位名称"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(StationPosDic))]
        public string PosName { get; set; }
        [DisplayName("点位索引"), Category("参数"), Description(""), ReadOnly(false)]

        public int PosIndex { get; set; } = -1;

        [DisplayName("不等待"), Category("参数"), Description(""), ReadOnly(false)]
        public bool isUnWait { get; set; }

        [DisplayName("检测到位"), Category("参数"), Description(""), ReadOnly(false)]
        public bool isCheckInPos { get; set; }

        [DisplayName("超时时间(ms)"), Category("参数"), Description(""), ReadOnly(false)]
        public int timeOut { get; set; }

        [DisplayName("超时时间(ms)变量"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string timeOutV { get; set; }

        private string isDisableAxis = "无禁用";
        [DisplayName("轴设置"), Category("轴设置"), Description(""), ReadOnly(false), TypeConverter(typeof(AxisDisableParam))]
        public string IsDisableAxis
        {
            get => isDisableAxis;
            set
            {
                if (isDisableAxis != value)
                {
                    isDisableAxis = value;
                    if (SF.isModify == ModifyKind.Operation || SF.isAddOps)
                    {
                        RefleshPropertyName();
                    }
                }
            }
        }

        public List<bool> GetAllValues()
        {
            List<bool> allValues = new List<bool>
        {
            Axis1,
            Axis2,
            Axis3,
            Axis4,
            Axis5,
            Axis6
            };
            return allValues;
        }
        [Browsable(false)]
        [DisplayName("轴设置"), Category("轴设置"), Description(""), ReadOnly(false)]
        public bool Axis1 { get; set; }
        [Browsable(false)]
        [DisplayName("轴设置"), Category("轴设置"), Description(""), ReadOnly(false)]
        public bool Axis2 { get; set; }
        [Browsable(false)]
        [DisplayName("轴设置"), Category("轴设置"), Description(""), ReadOnly(false)]
        public bool Axis3 { get; set; }
        [Browsable(false)]
        [DisplayName("轴设置"), Category("轴设置"), Description(""), ReadOnly(false)]
        public bool Axis4 { get; set; }
        [Browsable(false)]
        [DisplayName("轴设置"), Category("轴设置"), Description(""), ReadOnly(false)]
        public bool Axis5 { get; set; }
        [Browsable(false)]
        [DisplayName("轴设置"), Category("轴设置"), Description(""), ReadOnly(false)]
        public bool Axis6 { get; set; }

        private string changeVel = "不改变";
        [DisplayName("速度设置"), Category("速度设置"), Description(""), ReadOnly(false), TypeConverter(typeof(AxisVelParam))]
        public string ChangeVel
        {
            get => changeVel;
            set
            {
                if (changeVel != value)
                {
                    changeVel = value;
                    if (SF.isModify == ModifyKind.Operation || SF.isAddOps)
                    {
                        RefleshPropertyVel();
                    }
                }
            }
        }
        private double vel = 0; 
        [Browsable(false)]
        [DisplayName("速度"), Category("速度设置"), Description(""), ReadOnly(false)]
        public double Vel
        {
            get { return vel; }
            set
            {
                if (value >= 0 && value <= 100)
                {
                    vel = value;
                }
            }
        }
        [DisplayName("速度变量"), Category("速度设置"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        [Browsable(false)]
        public string VelV { get; set; }

        private double acc = 0;
        [Browsable(false)]
        [DisplayName("加速度"), Category("速度设置"), Description(""), ReadOnly(false)]
        public double Acc
        {
            get { return acc; }
            set
            {
                if (value >= 0 && value <= 100)
                {
                    acc = value;
                }
            }
        }
        [DisplayName("加速度变量"), Category("速度设置"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        [Browsable(false)]
        public string AccV { get; set; }

        private double dec = 0;
        [Browsable(false)]
        [DisplayName("减速度"), Category("速度设置"), Description(""), ReadOnly(false)]
        public double Dec
        {
            get { return dec; }
            set
            {
                if (value >= 0 && value <= 100)
                {
                    dec = value;
                }
            }
        }
        [DisplayName("减速度变量"), Category("速度设置"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        [Browsable(false)]
        public string DecV { get; set; }

        public void RefleshPropertyName()
        {
            string[] AxisName = { "Axis1", "Axis2", "Axis3", "Axis4", "Axis5", "Axis6" };
            if (this.isDisableAxis == "无禁用")
            {
                for (int i = 0; i < 6; i++)
                {
                    SetPropertyAttribute(this, AxisName[i], typeof(BrowsableAttribute), "browsable", false);
                }
                return;
            }

            if (SF.frmCard == null || SF.frmCard.dataStation == null)
            {
                for (int i = 0; i < 6; i++)
                {
                    SetPropertyAttribute(this, AxisName[i], typeof(BrowsableAttribute), "browsable", false);
                }
                return;
            }

            var station = SF.frmCard.dataStation.FirstOrDefault(sc => sc.Name == StationName);
            if (station == null)
            {
                for (int i = 0; i < 6; i++)
                {
                    SetPropertyAttribute(this, AxisName[i], typeof(BrowsableAttribute), "browsable", false);
                }
                return;
            }

            int stationIndex = SF.frmCard.dataStation.IndexOf(station);
            if (stationIndex != -1)
            {
                for (int j = 0; j < SF.frmCard.dataStation[stationIndex].dataAxis.axisConfigs.Count; j++)
                {
                    ushort index = (ushort)j;
                    if (SF.frmCard.dataStation[stationIndex].dataAxis.axisConfigs[j].AxisName != "-1")
                    {
                        SetPropertyAttribute(this, AxisName[j], typeof(BrowsableAttribute), "browsable", true);
                        SetDisplayName(typeof(StationRunPos), AxisName[j], SF.frmCard.dataStation[stationIndex].dataAxis.axisConfigs[j].AxisName);
                    }
                    else
                    {
                        SetPropertyAttribute(this, AxisName[j], typeof(BrowsableAttribute), "browsable", false);
                    }
                }
            }
        }

        public void RefleshPropertyVel()
        {
            if (this.changeVel == "不改变")
            {
               SetPropertyAttribute(this, "Vel", typeof(BrowsableAttribute), "browsable", false);
               SetPropertyAttribute(this, "Dec", typeof(BrowsableAttribute), "browsable", false);
               SetPropertyAttribute(this, "Acc", typeof(BrowsableAttribute), "browsable", false);

                SetPropertyAttribute(this, "VelV", typeof(BrowsableAttribute), "browsable", false);
                SetPropertyAttribute(this, "DecV", typeof(BrowsableAttribute), "browsable", false);
                SetPropertyAttribute(this, "AccV", typeof(BrowsableAttribute), "browsable", false);
            }
            else
            {

                SetPropertyAttribute(this, "Vel", typeof(BrowsableAttribute), "browsable", true);
                SetPropertyAttribute(this, "Dec", typeof(BrowsableAttribute), "browsable", true);
                SetPropertyAttribute(this, "Acc", typeof(BrowsableAttribute), "browsable", true);

                SetPropertyAttribute(this, "VelV", typeof(BrowsableAttribute), "browsable", true);
                SetPropertyAttribute(this, "DecV", typeof(BrowsableAttribute), "browsable", true);
                SetPropertyAttribute(this, "AccV", typeof(BrowsableAttribute), "browsable", true);
            }
          
        }
    }
    [Serializable]
    public class ModifyStationPos : OperationType
    {
        public ModifyStationPos()
        {
            OperaType = "点位修改";
            ModifyType = "叠加";
            evtRP += RefleshRefPosMode;
        }

        [DisplayName("工站名称"), Category("A修改参数"), Description(""), ReadOnly(false), TypeConverter(typeof(StationtItem))]
        public string StationName { get; set; }

        private string refPosName;
        [DisplayName("参考点"), Category("A修改参数"), Description(""), ReadOnly(false), TypeConverter(typeof(StationPosWithSpecial))]
        public string RefPosName
        {
            get => refPosName;
            set
            {
                if (refPosName != value)
                {
                    refPosName = value;
                    if (SF.isModify == ModifyKind.Operation || SF.isAddOps)
                    {
                        RefleshRefPosMode();
                    }
                }
            }
        }

        [DisplayName("目标点"), Category("A修改参数"), Description(""), ReadOnly(false), TypeConverter(typeof(StationPosDic))]
        public string TargetPosName { get; set; }

        [DisplayName("修改方式"), Category("B修改参数"), Description(""), ReadOnly(false), TypeConverter(typeof(PointModifyType))]
        public string ModifyType { get; set; }

        [Browsable(false)]
        [DisplayName("X"), Category("B修改参数"), Description(""), ReadOnly(false)]
        public double CustomX { get; set; }
        [Browsable(false)]
        [DisplayName("Y"), Category("B修改参数"), Description(""), ReadOnly(false)]
        public double CustomY { get; set; }
        [Browsable(false)]
        [DisplayName("Z"), Category("B修改参数"), Description(""), ReadOnly(false)]
        public double CustomZ { get; set; }
        [Browsable(false)]
        [DisplayName("U"), Category("B修改参数"), Description(""), ReadOnly(false)]
        public double CustomU { get; set; }
        [Browsable(false)]
        [DisplayName("V"), Category("B修改参数"), Description(""), ReadOnly(false)]
        public double CustomV { get; set; }
        [Browsable(false)]
        [DisplayName("W"), Category("B修改参数"), Description(""), ReadOnly(false)]
        public double CustomW { get; set; }

        public void RefleshRefPosMode()
        {
            bool useCustom = refPosName == "自定义坐标";
            SetPropertyAttribute(this, "CustomX", typeof(BrowsableAttribute), "browsable", useCustom);
            SetPropertyAttribute(this, "CustomY", typeof(BrowsableAttribute), "browsable", useCustom);
            SetPropertyAttribute(this, "CustomZ", typeof(BrowsableAttribute), "browsable", useCustom);
            SetPropertyAttribute(this, "CustomU", typeof(BrowsableAttribute), "browsable", useCustom);
            SetPropertyAttribute(this, "CustomV", typeof(BrowsableAttribute), "browsable", useCustom);
            SetPropertyAttribute(this, "CustomW", typeof(BrowsableAttribute), "browsable", useCustom);
        }
    }

    [Serializable]
    public class GetStationPos : OperationType
    {
        public GetStationPos()
        {
            OperaType = "获取工站位置";
            SourceType = "当前位置";
            SaveType = "保存到点位";
            evtRP += RefleshProperty;
            RefleshProperty();
        }

        [DisplayName("工站名称"), Category("A获取参数"), Description(""), ReadOnly(false), TypeConverter(typeof(StationtItem))]
        public string StationName { get; set; }

        private string sourceType;
        [DisplayName("获取方式"), Category("A获取参数"), Description(""), ReadOnly(false), TypeConverter(typeof(StationPosSourceType))]
        public string SourceType
        {
            get => sourceType;
            set
            {
                if (sourceType != value)
                {
                    sourceType = value;
                    if (SF.isModify == ModifyKind.Operation || SF.isAddOps)
                    {
                        RefleshProperty();
                    }
                }
            }
        }

        [DisplayName("指定点位"), Category("A获取参数"), Description(""), ReadOnly(false), TypeConverter(typeof(StationPosDic))]
        public string SourcePosName { get; set; }

        private string saveType;
        [DisplayName("保存方式"), Category("B保存参数"), Description(""), ReadOnly(false), TypeConverter(typeof(StationPosSaveType))]
        public string SaveType
        {
            get => saveType;
            set
            {
                if (saveType != value)
                {
                    saveType = value;
                    if (SF.isModify == ModifyKind.Operation || SF.isAddOps)
                    {
                        RefleshProperty();
                    }
                }
            }
        }

        [DisplayName("保存到点位"), Category("B保存参数"), Description(""), ReadOnly(false), TypeConverter(typeof(StationPosDic))]
        public string TargetPosName { get; set; }

        [DisplayName("X变量索引"), Category("C保存到变量"), Description(""), ReadOnly(false)]
        [Browsable(false)]
        public string OutputXIndex { get; set; }
        [DisplayName("X变量索引二级"), Category("C保存到变量"), Description(""), ReadOnly(false)]
        [Browsable(false)]
        public string OutputXIndex2Index { get; set; }
        [DisplayName("X变量名称"), Category("C保存到变量"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        [Browsable(false)]
        public string OutputXName { get; set; }
        [DisplayName("X变量名称二级"), Category("C保存到变量"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        [Browsable(false)]
        public string OutputXName2Index { get; set; }

        [DisplayName("Y变量索引"), Category("C保存到变量"), Description(""), ReadOnly(false)]
        [Browsable(false)]
        public string OutputYIndex { get; set; }
        [DisplayName("Y变量索引二级"), Category("C保存到变量"), Description(""), ReadOnly(false)]
        [Browsable(false)]
        public string OutputYIndex2Index { get; set; }
        [DisplayName("Y变量名称"), Category("C保存到变量"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        [Browsable(false)]
        public string OutputYName { get; set; }
        [DisplayName("Y变量名称二级"), Category("C保存到变量"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        [Browsable(false)]
        public string OutputYName2Index { get; set; }

        [DisplayName("Z变量索引"), Category("C保存到变量"), Description(""), ReadOnly(false)]
        [Browsable(false)]
        public string OutputZIndex { get; set; }
        [DisplayName("Z变量索引二级"), Category("C保存到变量"), Description(""), ReadOnly(false)]
        [Browsable(false)]
        public string OutputZIndex2Index { get; set; }
        [DisplayName("Z变量名称"), Category("C保存到变量"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        [Browsable(false)]
        public string OutputZName { get; set; }
        [DisplayName("Z变量名称二级"), Category("C保存到变量"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        [Browsable(false)]
        public string OutputZName2Index { get; set; }

        [DisplayName("U变量索引"), Category("C保存到变量"), Description(""), ReadOnly(false)]
        [Browsable(false)]
        public string OutputUIndex { get; set; }
        [DisplayName("U变量索引二级"), Category("C保存到变量"), Description(""), ReadOnly(false)]
        [Browsable(false)]
        public string OutputUIndex2Index { get; set; }
        [DisplayName("U变量名称"), Category("C保存到变量"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        [Browsable(false)]
        public string OutputUName { get; set; }
        [DisplayName("U变量名称二级"), Category("C保存到变量"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        [Browsable(false)]
        public string OutputUName2Index { get; set; }

        [DisplayName("V变量索引"), Category("C保存到变量"), Description(""), ReadOnly(false)]
        [Browsable(false)]
        public string OutputVIndex { get; set; }
        [DisplayName("V变量索引二级"), Category("C保存到变量"), Description(""), ReadOnly(false)]
        [Browsable(false)]
        public string OutputVIndex2Index { get; set; }
        [DisplayName("V变量名称"), Category("C保存到变量"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        [Browsable(false)]
        public string OutputVName { get; set; }
        [DisplayName("V变量名称二级"), Category("C保存到变量"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        [Browsable(false)]
        public string OutputVName2Index { get; set; }

        [DisplayName("W变量索引"), Category("C保存到变量"), Description(""), ReadOnly(false)]
        [Browsable(false)]
        public string OutputWIndex { get; set; }
        [DisplayName("W变量索引二级"), Category("C保存到变量"), Description(""), ReadOnly(false)]
        [Browsable(false)]
        public string OutputWIndex2Index { get; set; }
        [DisplayName("W变量名称"), Category("C保存到变量"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        [Browsable(false)]
        public string OutputWName { get; set; }
        [DisplayName("W变量名称二级"), Category("C保存到变量"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        [Browsable(false)]
        public string OutputWName2Index { get; set; }

        public void RefleshProperty()
        {
            bool useSourcePos = sourceType == "指定点位";
            SetPropertyAttribute(this, "SourcePosName", typeof(BrowsableAttribute), "browsable", useSourcePos);

            bool saveToPoint = saveType == "保存到点位";
            SetPropertyAttribute(this, "TargetPosName", typeof(BrowsableAttribute), "browsable", saveToPoint);

            bool saveToValue = saveType == "保存到变量";
            SetPropertyAttribute(this, "OutputXIndex", typeof(BrowsableAttribute), "browsable", saveToValue);
            SetPropertyAttribute(this, "OutputXIndex2Index", typeof(BrowsableAttribute), "browsable", saveToValue);
            SetPropertyAttribute(this, "OutputXName", typeof(BrowsableAttribute), "browsable", saveToValue);
            SetPropertyAttribute(this, "OutputXName2Index", typeof(BrowsableAttribute), "browsable", saveToValue);

            SetPropertyAttribute(this, "OutputYIndex", typeof(BrowsableAttribute), "browsable", saveToValue);
            SetPropertyAttribute(this, "OutputYIndex2Index", typeof(BrowsableAttribute), "browsable", saveToValue);
            SetPropertyAttribute(this, "OutputYName", typeof(BrowsableAttribute), "browsable", saveToValue);
            SetPropertyAttribute(this, "OutputYName2Index", typeof(BrowsableAttribute), "browsable", saveToValue);

            SetPropertyAttribute(this, "OutputZIndex", typeof(BrowsableAttribute), "browsable", saveToValue);
            SetPropertyAttribute(this, "OutputZIndex2Index", typeof(BrowsableAttribute), "browsable", saveToValue);
            SetPropertyAttribute(this, "OutputZName", typeof(BrowsableAttribute), "browsable", saveToValue);
            SetPropertyAttribute(this, "OutputZName2Index", typeof(BrowsableAttribute), "browsable", saveToValue);

            SetPropertyAttribute(this, "OutputUIndex", typeof(BrowsableAttribute), "browsable", saveToValue);
            SetPropertyAttribute(this, "OutputUIndex2Index", typeof(BrowsableAttribute), "browsable", saveToValue);
            SetPropertyAttribute(this, "OutputUName", typeof(BrowsableAttribute), "browsable", saveToValue);
            SetPropertyAttribute(this, "OutputUName2Index", typeof(BrowsableAttribute), "browsable", saveToValue);

            SetPropertyAttribute(this, "OutputVIndex", typeof(BrowsableAttribute), "browsable", saveToValue);
            SetPropertyAttribute(this, "OutputVIndex2Index", typeof(BrowsableAttribute), "browsable", saveToValue);
            SetPropertyAttribute(this, "OutputVName", typeof(BrowsableAttribute), "browsable", saveToValue);
            SetPropertyAttribute(this, "OutputVName2Index", typeof(BrowsableAttribute), "browsable", saveToValue);

            SetPropertyAttribute(this, "OutputWIndex", typeof(BrowsableAttribute), "browsable", saveToValue);
            SetPropertyAttribute(this, "OutputWIndex2Index", typeof(BrowsableAttribute), "browsable", saveToValue);
            SetPropertyAttribute(this, "OutputWName", typeof(BrowsableAttribute), "browsable", saveToValue);
            SetPropertyAttribute(this, "OutputWName2Index", typeof(BrowsableAttribute), "browsable", saveToValue);
        }
    }

    [Serializable]
    public class StationRunRel : OperationType
    {
        public StationRunRel()
        {
            OperaType = "偏移量";
            evtRP += RefleshPropertyName;
            evtRP += RefleshPropertyVel;
            timeOut = 120000;
        }
        private string stationName;
        [DisplayName("工站名称"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(StationtItem))]

        public string StationName
        {
            get => stationName;
            set
            {
                if (stationName != value)
                {
                    stationName = value;
                    if (SF.isModify == ModifyKind.Operation || SF.isAddOps)
                    {
                        RefleshPropertyName();
                    }
                }
            }
        }
        //[DisplayName("工站索引"), Category("参数"), Description(""), ReadOnly(false)]

        //public int StationIndex { get; set; } = -1;
        //[DisplayName("点位索引"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(StationPosDic))]
        //public string StationHomeType { get; set; }

        [DisplayName("不等待"), Category("参数"), Description(""), ReadOnly(false)]
        public bool isUnWait { get; set; }

        [DisplayName("检测到位"), Category("参数"), Description(""), ReadOnly(false)]
        public bool isCheckInPos { get; set; }

        [DisplayName("超时时间(ms)"), Category("参数"), Description(""), ReadOnly(false)]
        public int timeOut { get; set; }

        [DisplayName("超时时间(ms)变量"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string timeOutV { get; set; }

        public List<double> GetAllValues()
        {
            List<double> allValues = new List<double>
        {
            Axis1,
            Axis2,
            Axis3,
            Axis4,
            Axis5,
            Axis6
            };
            return allValues;
        }
        public List<string> GetAllValuesV()
        {
            List<string> allValues = new List<string>
        {
            Axis1V,
            Axis2V,
            Axis3V,
            Axis4V,
            Axis5V,
            Axis6V
            };
            return allValues;
        }
        [Browsable(false)]
        [DisplayName("轴设置"), Category("轴设置"), Description(""), ReadOnly(false)]
        public double Axis1 { get; set; }
        [DisplayName("轴设置变量"), Category("轴设置"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem)), Browsable(false)]
        public string Axis1V { get; set; }
        [Browsable(false)]
        [DisplayName("轴设置"), Category("轴设置"), Description(""), ReadOnly(false)]
        public double Axis2 { get; set; }
        [DisplayName("轴设置变量"), Category("轴设置"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem)), Browsable(false)]
        public string Axis2V { get; set; }
        [Browsable(false)]
        [DisplayName("轴设置"), Category("轴设置"), Description(""), ReadOnly(false)]
        public double Axis3 { get; set; }
        [DisplayName("轴设置变量"), Category("轴设置"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem)), Browsable(false)]
        public string Axis3V { get; set; }
        [Browsable(false)]
        [DisplayName("轴设置"), Category("轴设置"), Description(""), ReadOnly(false)]
        public double Axis4 { get; set; }
        [DisplayName("轴设置变量"), Category("轴设置"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem)), Browsable(false)]
        public string Axis4V { get; set; }
        [Browsable(false)]
        [DisplayName("轴设置"), Category("轴设置"), Description(""), ReadOnly(false)]
        public double Axis5 { get; set; }
        [DisplayName("轴设置变量"), Category("轴设置"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem)), Browsable(false)]
        public string Axis5V { get; set; }
        [Browsable(false)]
        [DisplayName("轴设置"), Category("轴设置"), Description(""), ReadOnly(false)]
        public double Axis6 { get; set; }
        [DisplayName("轴设置变量"), Category("轴设置"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem)), Browsable(false)]
        public string Axis6V { get; set; }

        private string changeVel = "不改变";
        [DisplayName("速度设置"), Category("速度设置"), Description(""), ReadOnly(false), TypeConverter(typeof(AxisVelParam))]
        public string ChangeVel
        {
            get => changeVel;
            set
            {
                if (changeVel != value)
                {
                    changeVel = value;
                    if (SF.isModify == ModifyKind.Operation || SF.isAddOps)
                    {
                        RefleshPropertyVel();
                    }
                }
            }
        }
        private double vel = 0;
        [Browsable(false)]
        [DisplayName("速度"), Category("速度设置"), Description(""), ReadOnly(false)]
        public double Vel
        {
            get { return vel; }
            set
            {
                if (value >= 0 && value <= 100)
                {
                    vel = value;
                }
            }
        }
        [DisplayName("速度变量"), Category("速度设置"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        [Browsable(false)]
        public string VelV { get; set; }
        private double acc = 0;
        [Browsable(false)]
        [DisplayName("加速度"), Category("速度设置"), Description(""), ReadOnly(false)]
        public double Acc
        {
            get { return acc; }
            set
            {
                if (value >= 0 && value <= 100)
                {
                    acc = value;
                }
            }
        }
        [DisplayName("加速度变量"), Category("速度设置"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        [Browsable(false)]
        public string AccV { get; set; }
        private double dec = 0;
        [Browsable(false)]
        [DisplayName("减速度"), Category("速度设置"), Description(""), ReadOnly(false)]
        public double Dec
        {
            get { return dec; }
            set
            {
                if (value >= 0 && value <= 100)
                {
                    dec = value;
                }
            }
        }
        [DisplayName("减速度变量"), Category("速度设置"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        [Browsable(false)]
        public string DecV { get; set; }
        public void RefleshPropertyVel()
        {
            if (this.changeVel == "不改变")
            {
                SetPropertyAttribute(this, "Vel", typeof(BrowsableAttribute), "browsable", false);
                SetPropertyAttribute(this, "Dec", typeof(BrowsableAttribute), "browsable", false);
                SetPropertyAttribute(this, "Acc", typeof(BrowsableAttribute), "browsable", false);

                SetPropertyAttribute(this, "VelV", typeof(BrowsableAttribute), "browsable", false);
                SetPropertyAttribute(this, "DecV", typeof(BrowsableAttribute), "browsable", false);
                SetPropertyAttribute(this, "AccV", typeof(BrowsableAttribute), "browsable", false);
            }
            else
            {

                SetPropertyAttribute(this, "Vel", typeof(BrowsableAttribute), "browsable", true);
                SetPropertyAttribute(this, "Dec", typeof(BrowsableAttribute), "browsable", true);
                SetPropertyAttribute(this, "Acc", typeof(BrowsableAttribute), "browsable", true);

                SetPropertyAttribute(this, "VelV", typeof(BrowsableAttribute), "browsable", true);
                SetPropertyAttribute(this, "DecV", typeof(BrowsableAttribute), "browsable", true);
                SetPropertyAttribute(this, "AccV", typeof(BrowsableAttribute), "browsable", true);
            }

        }
        public void RefleshPropertyName()
        {
            string[] AxisName = { "Axis1", "Axis2", "Axis3", "Axis4", "Axis5", "Axis6" };

            if (SF.frmCard == null || SF.frmCard.dataStation == null)
            {
                for (int i = 0; i < 6; i++)
                {
                    SetPropertyAttribute(this, AxisName[i], typeof(BrowsableAttribute), "browsable", false);
                    SetPropertyAttribute(this, AxisName[i] + "V", typeof(BrowsableAttribute), "browsable", false);
                }
                return;
            }

            var station = SF.frmCard.dataStation.FirstOrDefault(sc => sc.Name == StationName);

            if (station == null)
            {
                for (int i = 0; i < 6; i++)
                {
                    SetPropertyAttribute(this, AxisName[i], typeof(BrowsableAttribute), "browsable", false);
                    SetPropertyAttribute(this, AxisName[i] + "V", typeof(BrowsableAttribute), "browsable", false);
                }
                return;
            }

            int stationIndex = SF.frmCard.dataStation.IndexOf(station);
            if (stationIndex != -1)
            {
                for (int j = 0; j < SF.frmCard.dataStation[stationIndex].dataAxis.axisConfigs.Count; j++)
                {
                    ushort index = (ushort)j;
                    if (SF.frmCard.dataStation[stationIndex].dataAxis.axisConfigs[j].AxisName != "-1")
                    {
                        SetPropertyAttribute(this, AxisName[j], typeof(BrowsableAttribute), "browsable", true);
                        SetPropertyAttribute(this, AxisName[j] + "V", typeof(BrowsableAttribute), "browsable", true);
                        SetDisplayName(typeof(StationRunRel), AxisName[j], SF.frmCard.dataStation[stationIndex].dataAxis.axisConfigs[j].AxisName);
                        SetDisplayName(typeof(StationRunRel), AxisName[j] + "V", SF.frmCard.dataStation[stationIndex].dataAxis.axisConfigs[j].AxisName + "变量");
                    }
                    else
                    {
                        SetPropertyAttribute(this, AxisName[j], typeof(BrowsableAttribute), "browsable", false);
                        SetPropertyAttribute(this, AxisName[j] + "V", typeof(BrowsableAttribute), "browsable", false);
                    }
                }
            }
        }
    }
    [Serializable]
    public class SetStationVel : OperationType
    {
        public SetStationVel()
        {
            OperaType = "设置速度";
        }
        [DisplayName("工站名称"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(StationtItem))]

        public string StationName { get; set; }
        [DisplayName("工站索引"), Category("参数"), Description(""), ReadOnly(false)]

        public int StationIndex { get; set; } = -1;

        [DisplayName("设置对象"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(StationAixsItem))]

        public string SetAxisObj { get; set; }

        private double vel = 0;
        [Browsable(true)]
        [DisplayName("速度"), Category("速度设置"), Description(""), ReadOnly(false)]
        public double Vel
        {
            get { return vel; }
            set
            {
                if (value >= 0 && value <= 100)
                {
                    vel = value;
                }
            }
        }
        [DisplayName("速度变量"), Category("速度设置"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        [Browsable(true)]
        public string VelV { get; set; }
        private double acc = 0;
        [Browsable(true)]
        [DisplayName("加速度"), Category("速度设置"), Description(""), ReadOnly(false)]
        public double Acc
        {
            get { return acc; }
            set
            {
                if (value >= 0 && value <= 100)
                {
                    acc = value;
                }
            }
        }
        [DisplayName("加速度变量"), Category("速度设置"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        [Browsable(true)]
        public string AccV { get; set; }
        private double dec = 0;
        [Browsable(true)]
        [DisplayName("减速度"), Category("速度设置"), Description(""), ReadOnly(false)]
        public double Dec
        {
            get { return dec; }
            set
            {
                if (value >= 0 && value <= 100)
                {
                    dec = value;
                }
            }
        }
        [DisplayName("减速度变量"), Category("速度设置"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        [Browsable(true)]
        public string DecV { get; set; }

    }
    [Serializable]
    public class StationStop : OperationType
    {
        public StationStop()
        {
            OperaType = "停止运动";
            evtRP += RefleshPropertyName;
        }
        private string stationName;
        [DisplayName("工站名称"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(StationtItem))]


        public string StationName
        {
            get => stationName;
            set
            {
                if (stationName != value)
                {
                    stationName = value;
                    if (SF.isModify == ModifyKind.Operation || SF.isAddOps)
                    {
                        RefleshPropertyName();
                    }
                }
            }
        }

        public List<bool> GetAllValues()
        {
            List<bool> allValues = new List<bool>
            {
            Axis1,
            Axis2,
            Axis3,
            Axis4,
            Axis5,
            Axis6
            };
            return allValues;
        }
        [DisplayName("是否整体工站停止"), Category("工站设置"), Description(""), ReadOnly(false)]
        public bool isAllStop { get; set; }
        [Browsable(false)]
        [DisplayName("轴设置"), Category("轴设置"), Description(""), ReadOnly(false)]
        public bool Axis1 { get; set; }
        [Browsable(false)]
        [DisplayName("轴设置"), Category("轴设置"), Description(""), ReadOnly(false)]
        public bool Axis2 { get; set; }
        [Browsable(false)]
        [DisplayName("轴设置"), Category("轴设置"), Description(""), ReadOnly(false)]
        public bool Axis3 { get; set; }
        [Browsable(false)]
        [DisplayName("轴设置"), Category("轴设置"), Description(""), ReadOnly(false)]
        public bool Axis4 { get; set; }
        [Browsable(false)]
        [DisplayName("轴设置"), Category("轴设置"), Description(""), ReadOnly(false)]
        public bool Axis5 { get; set; }
        [Browsable(false)]
        [DisplayName("轴设置"), Category("轴设置"), Description(""), ReadOnly(false)]
        public bool Axis6 { get; set; }

        public void RefleshPropertyName()
        {
            string[] AxisName = { "Axis1", "Axis2", "Axis3", "Axis4", "Axis5", "Axis6" };

            if (SF.frmCard == null || SF.frmCard.dataStation == null)
            {
                for (int i = 0; i < 6; i++)
                {
                    SetPropertyAttribute(this, AxisName[i], typeof(BrowsableAttribute), "browsable", false);
                }
                return;
            }

            var station = SF.frmCard.dataStation.FirstOrDefault(sc => sc.Name == StationName);

            if (station == null)
            {
                for (int i = 0; i < 6; i++)
                {
                    SetPropertyAttribute(this, AxisName[i], typeof(BrowsableAttribute), "browsable", false);
                }
                return;
            }

            int stationIndex = SF.frmCard.dataStation.IndexOf(station);
            if (stationIndex != -1)
            {
                for (int j = 0; j < SF.frmCard.dataStation[stationIndex].dataAxis.axisConfigs.Count; j++)
                {
                    ushort index = (ushort)j;
                    if (SF.frmCard.dataStation[stationIndex].dataAxis.axisConfigs[j].AxisName != "-1")
                    {
                        SetPropertyAttribute(this, AxisName[j], typeof(BrowsableAttribute), "browsable", true);
                        SetDisplayName(typeof(StationStop), AxisName[j], SF.frmCard.dataStation[stationIndex].dataAxis.axisConfigs[j].AxisName);
                    }
                    else
                    {
                        SetPropertyAttribute(this, AxisName[j], typeof(BrowsableAttribute), "browsable", false);
                    }
                }
            }
        }
    }
    [Serializable]
    public class WaitStationStop : OperationType
    {
        public WaitStationStop()
        {
            OperaType = "等待运动";
            timeOut = 120000;
        }
        [DisplayName("工站名称"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(StationtItem))]

        public string StationName { get; set; }
        [DisplayName("工站索引"), Category("参数"), Description(""), ReadOnly(false)]

        public int StationIndex { get; set; } = -1;
        [DisplayName("是否等待回零"), Category("工站设置"), Description(""), ReadOnly(false)]
        public bool isWaitHome { get; set; }

        [DisplayName("超时时间(ms)"), Category("参数"), Description(""), ReadOnly(false)]
        public int timeOut { get; set; }
        [DisplayName("超时时间(ms)变量"), Category("参数"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string timeOutV { get; set; }
    }
}
