using System;
using System.Collections.Generic;
using System.ComponentModel;
using static Automation.OperationTypePartial;

namespace Automation
{
    public enum ProcChangeKind
    {
        Modified,
        Added,
        Deleted
    }

    //存放单个流程信息
    [Serializable]
    public class Proc
    {
        public ProcHead head;
        public List<Step> steps = new List<Step>();
    }
    [Serializable]
    public class ProcHead
    {
        public ProcHead()
        {
            ParamListConverter<PauseIoParam>.Name = "暂停IO";
            ParamListConverter<PauseValueParam>.Name = "暂停变量";
            PauseIoParams = new CustomList<PauseIoParam>();
            PauseValueParams = new CustomList<PauseValueParam>();
            Id = Guid.NewGuid();
        }

        [Browsable(false)]
        public Guid Id { get; set; }

        [DisplayName("流程名称"), Category("流程信息"), Description(""), ReadOnly(false)]
        public string Name { get; set; }

        [DisplayName("自启动"), Category("流程信息"), Description("启动程序时自动运行"), ReadOnly(false)]
        public bool AutoStart { get; set; }

        [DisplayName("禁用"), Category("流程信息"), Description(""), ReadOnly(false)]
        public bool Disable { get; set; }

        private string pauseIoCount;
        [DisplayName("暂停IO数"), Category("暂停信号"), Description(""), ReadOnly(false), TypeConverter(typeof(PauseCountItem))]
        public string PauseIoCount
        {
            get { return pauseIoCount; }
            set
            {
                pauseIoCount = value;
                if (SF.frmPropertyGrid?.propertyGrid1?.SelectedObject != this)
                {
                    return;
                }
                if (!int.TryParse(pauseIoCount, out int count) || count <= 0)
                {
                    PauseIoParams?.Clear();
                }
                else
                {
                    if (PauseIoParams == null)
                    {
                        PauseIoParams = new CustomList<PauseIoParam>();
                    }
                    PauseIoParams.Clear();
                    for (int i = 0; i < count; i++)
                    {
                        PauseIoParams.Add(new PauseIoParam());
                    }
                }
                SF.frmPropertyGrid.propertyGrid1.SelectedObject = this;
                SF.frmPropertyGrid.propertyGrid1.ExpandAllGridItems();
            }
        }

        [DisplayName("暂停IO"), Category("暂停信号"), Description(""), ReadOnly(false)]
        [TypeConverter(typeof(ParamListConverter<PauseIoParam>))]
        public CustomList<PauseIoParam> PauseIoParams { get; set; }

        private string pauseValueCount;
        [DisplayName("暂停变量数"), Category("暂停信号"), Description(""), ReadOnly(false), TypeConverter(typeof(PauseCountItem))]
        public string PauseValueCount
        {
            get { return pauseValueCount; }
            set
            {
                pauseValueCount = value;
                if (SF.frmPropertyGrid?.propertyGrid1?.SelectedObject != this)
                {
                    return;
                }
                if (!int.TryParse(pauseValueCount, out int count) || count <= 0)
                {
                    PauseValueParams?.Clear();
                }
                else
                {
                    if (PauseValueParams == null)
                    {
                        PauseValueParams = new CustomList<PauseValueParam>();
                    }
                    PauseValueParams.Clear();
                    for (int i = 0; i < count; i++)
                    {
                        PauseValueParams.Add(new PauseValueParam());
                    }
                }
                SF.frmPropertyGrid.propertyGrid1.SelectedObject = this;
                SF.frmPropertyGrid.propertyGrid1.ExpandAllGridItems();
            }
        }

        [DisplayName("暂停变量"), Category("暂停信号"), Description(""), ReadOnly(false)]
        [TypeConverter(typeof(ParamListConverter<PauseValueParam>))]
        public CustomList<PauseValueParam> PauseValueParams { get; set; }

    }

    [TypeConverter(typeof(SerializableExpandableObjectConverter))]
    [Serializable]
    public class PauseIoParam
    {
        [DisplayName("名称"), Category("暂停信号"), Description(""), ReadOnly(false), TypeConverter(typeof(IoInItem))]
        public string IOName { get; set; }

        public override string ToString()
        {
            return "";
        }
    }

    [TypeConverter(typeof(SerializableExpandableObjectConverter))]
    [Serializable]
    public class PauseValueParam
    {
        [DisplayName("变量名称"), Category("暂停信号"), Description(""), ReadOnly(false), TypeConverter(typeof(ValueItem))]
        public string ValueName { get; set; }

        public override string ToString()
        {
            return "";
        }
    }
    [Serializable]
    public class Step
    {
        [Browsable(false)]
        public Guid Id { get; set; }

        [Browsable(false)]
        [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
        public string AiKey { get; set; }

        [DisplayName("步骤名称"), Category("步骤信息"), Description(""), ReadOnly(false)]
        public string Name { get; set; }

        [DisplayName("禁用"), Category("步骤信息"), Description(""), ReadOnly(false)]
        public bool Disable { get; set; }

        public List<OperationType> Ops = new List<OperationType>();
    }
}
