using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static Automation.FrmCard;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.StartPanel;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

namespace Automation
{
    public enum EditKind
    {
        None,
        CardRoot,
        Card,
        Axis
    }

    public struct EditKey
    {
        public EditKind Kind { get; set; }
        public int? CardIndex { get; set; }
        public int? AxisIndex { get; set; }
        public int? StationIndex { get; set; }
        public bool IsNewCard { get; set; }
        public bool IsNewStation { get; set; }

        public static EditKey None => new EditKey { Kind = EditKind.None };
        public static EditKey CardRoot => new EditKey { Kind = EditKind.CardRoot };
        public static EditKey Card(int cardIndex) => new EditKey { Kind = EditKind.Card, CardIndex = cardIndex };
        public static EditKey Axis(int cardIndex, int axisIndex) => new EditKey { Kind = EditKind.Axis, CardIndex = cardIndex, AxisIndex = axisIndex };
    }

    public partial class FrmCard : Form
    {
        //存放所有轴卡信息 
        //存放临时控制卡信息
        public ControlCard controlCardTemp;
        //存放临时轴信息
        public Axis axisTemp;
        public EditKey editKey = EditKey.None;

        public static List<string> axisItme = new List<string>();
      
        
        public List<DataStation> dataStation;
        //存放临时工站信息
        public DataStation dataStationTemp;

        public FrmCard()
        {
            InitializeComponent();
            editKey = EditKey.None;
            this.treeView1.HideSelection = false;
            this.treeView2.HideSelection = false;

        }
        public bool IsNewCard => editKey.IsNewCard;
        public bool IsNewStation => editKey.IsNewStation;
        public bool IsCardRootSelected => editKey.Kind == EditKind.CardRoot;

        public bool TryGetSelectedCardIndex(out int cardIndex)
        {
            if (editKey.CardIndex.HasValue)
            {
                cardIndex = editKey.CardIndex.Value;
                return true;
            }
            cardIndex = -1;
            return false;
        }

        public bool TryGetSelectedAxisIndex(out int cardIndex, out int axisIndex)
        {
            if (editKey.CardIndex.HasValue && editKey.AxisIndex.HasValue)
            {
                cardIndex = editKey.CardIndex.Value;
                axisIndex = editKey.AxisIndex.Value;
                return true;
            }
            cardIndex = -1;
            axisIndex = -1;
            return false;
        }

        public bool TryGetSelectedStationIndex(out int stationIndex)
        {
            if (editKey.StationIndex.HasValue)
            {
                stationIndex = editKey.StationIndex.Value;
                return true;
            }
            stationIndex = -1;
            return false;
        }

        private void ClearCardSelection()
        {
            editKey.CardIndex = null;
            editKey.AxisIndex = null;
            editKey.Kind = EditKind.None;
        }

        private void ClearStationSelection()
        {
            editKey.StationIndex = null;
        }

        public void EndNewCard()
        {
            editKey.IsNewCard = false;
        }

        public void EndNewStation()
        {
            editKey.IsNewStation = false;
        }
        public void RefreshCardTree()
        {
            treeView1.Nodes.Clear();
            TreeNode treeNode = new TreeNode("控制卡");
            treeView1.Nodes.Add(treeNode);
            int cardCount = SF.cardStore.GetControlCardCount();
            if (cardCount == 0)
            {
                return;
            }
            for (int i = 0; i < cardCount; i++)
            {
                TreeNode chnode = new TreeNode(i + "号卡：");
                treeView1.Nodes[0].Nodes.Add(chnode);
                if (!SF.cardStore.TryGetControlCard(i, out ControlCard controlCard))
                {
                    continue;
                }
                if (controlCard.axis == null)
                {
                    continue;
                }
                for (int j = 0; j < controlCard.axis.Count; j++)
                {
                    TreeNode chnodes = new TreeNode(j + ":" + controlCard.axis[j].AxisName.ToString() + ":");
                    treeView1.Nodes[0].Nodes[i].Nodes.Add(chnodes);
                }
            }

            treeView1.ExpandAll();
        }

 
        //存放单个轴卡信息
        public class Card
        {
            public List<ControlCard> controlCards = new List<ControlCard>();
        }
        public class ControlCard
        {
            public CardHead cardHead = new CardHead();
            public List<Axis> axis = new List<Axis>();
        }
        public class CardHead
        {
            [DisplayName("轴数量"), Category("卡参数"), Description(""), ReadOnly(false)]
            public int AxisCount { get; set; }
            [DisplayName("输入IO数量"), Category("卡参数"), Description(""), ReadOnly(false)]
            public int InputCount { get; set; }
            [DisplayName("输出IO数量"), Category("卡参数"), Description(""), ReadOnly(false)]
            public int OutputCount { get; set; }
            [DisplayName("卡类型"), Category("卡参数"), Description(""), ReadOnly(false)]
            public string CardType { get; set; }
        }
        public class Axis
        {
            [DisplayName("轴名称"), Category("A基本参数"), Description(""), ReadOnly(false)]
            public string AxisName { get; set; }

            [DisplayName("轴号"), Category("A基本参数"), Description(""), ReadOnly(true)]
            public int AxisNum { get; set; }
            //[DisplayName("轴类型"), Category("A基本参数"), Description(""), ReadOnly(false)]
            //public int AxisType { get; set; }
            [DisplayName("单位毫米脉冲"), Category("A基本参数"), Description(""), ReadOnly(false)]
            public int PulseToMM { get; set; }
            [DisplayName("回原模式"), Category("A基本参数"), Description(""), ReadOnly(false), TypeConverter(typeof(HomeTypeItem))]
            public string HomeType { get; set; }
            //[DisplayName("编码器类型"), Category("A基本参数"), Description(""), ReadOnly(false)]
            //public int EncoderType { get; set; }
            //[DisplayName("报警索引"), Category("A基本参数"), Description(""), ReadOnly(false)]
            //public int WarnIndex { get; set; }
            //[DisplayName("从站ID"), Category("A基本参数"), Description(""), ReadOnly(false)]
            //public int SlaveStation { get; set; }
            //[DisplayName("驱动器回原模式"), Category("A基本参数"), Description(""), ReadOnly(false)]
            //public string DriverHomeType { get; set; }


            //[DisplayName("回原前偏移量"), Category("B回原参数"), Description(""), ReadOnly(false)]
            //public int OffsetBeforeHome { get; set; }
            //[DisplayName("回原搜索距离"), Category("B回原参数"), Description(""), ReadOnly(false)]
            //public int HomeDistance { get; set; }
            //[DisplayName("回原后偏移量"), Category("B回原参数"), Description(""), ReadOnly(false)]
            //public int OffsetAfterHome { get; set; }
            [DisplayName("回原速度"), Category("B回原参数"), Description(""), ReadOnly(false)]
            public string HomeSpeed { get; set; }
            [DisplayName("找限位速度"), Category("B回原参数"), Description(""), ReadOnly(false)]
            public string LimitSpeed { get; set; }

            

            [DisplayName("速度说明"), Category("C运动参数"), Description(""), ReadOnly(false)]
            public int SpeedInfo { get; set; }

            [DisplayName("最大速度"), Category("C运动参数"), Description(""), ReadOnly(false)]
            public int SpeedMax { get; set; }
            [DisplayName("加速度时间"), Category("C运动参数"), Description(""), ReadOnly(false)]
            public double AccMax { get; set; }
            [DisplayName("减速度时间"), Category("C运动参数"), Description(""), ReadOnly(false)]
            public double DecMax { get; set; }
            [Browsable(false)]
            public double Vel { get; set; } = 1;

            // 0 已就绪， 1 运动中 -1 未就绪

            [Browsable(false)]
            [JsonIgnore]
            public Status State { get; set; } = 0;

            public Status GetState()
            {
                return State;
            }
            public void SetState(Status state)
            {
                State = state;
            }
            public enum Status
            {
                NotReady = -1,
                Ready,
                Run,
            }
            //运行过程中使用的速度参数
            [JsonIgnore]
            public double SpeedRun = 100;
            [JsonIgnore]
            public double AccRun = 100;
            [JsonIgnore]
            public double DecRun = 100;

            public List<double> GetVelRun()
            {
                List<double> allValues = new List<double>
        {
            SpeedRun,
            AccRun,
            DecRun,
            };
                return allValues;
            }
        }
        private void FrmCard_Load(object sender, EventArgs e)
        {
            RefreshCardTree();
            RefreshStationTree();
            if (treeView1.Nodes.Count != 0&& treeView1.Nodes[0].Nodes.Count!=0)
            {
                TreeNode firstNode = treeView1.Nodes[0].Nodes[0];
                if (firstNode != null)
                {
                    // 选择第一个节点
                    treeView1.SelectedNode = firstNode;
                }
            }
          
        }

        private void AddCard_Click(object sender, EventArgs e)
        {
            //新建控制卡
            if (IsCardRootSelected)
            {
                controlCardTemp = new ControlCard();
                SF.frmPropertyGrid.propertyGrid1.SelectedObject = controlCardTemp.cardHead;
                editKey.IsNewCard = true;
                SF.BeginEdit(ModifyKind.None);
            }
        }
        private int GetNodeLevel(TreeNode node)
        {
            int level = 0; 
            while (node != null)
            {
                level++; 
                node = node.Parent; 
            }
            return level; 
        }
        private void treeView1_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (treeView1.SelectedNode != null)
            {
                int level = GetNodeLevel(treeView1.SelectedNode);
                if (level == 1)
                {
                    editKey.Kind = EditKind.CardRoot;
                    editKey.CardIndex = null;
                    editKey.AxisIndex = null;
                }
                else if(level == 2)
                {
                    editKey.Kind = EditKind.Card;
                    editKey.CardIndex = treeView1.SelectedNode.Index;
                    editKey.AxisIndex = null;

                    if (SF.cardStore.TryGetControlCard(editKey.CardIndex.Value, out ControlCard controlCard))
                    {
                        SF.frmPropertyGrid.propertyGrid1.SelectedObject = controlCard.cardHead;
                    }

                    SF.frmIO.RefreshIODgv();
                }
                else if(level == 3)
                {
                    editKey.Kind = EditKind.Axis;
                    editKey.CardIndex = treeView1.SelectedNode.Parent.Index;
                    editKey.AxisIndex = treeView1.SelectedNode.Index;

                    if (SF.cardStore.TryGetAxis(editKey.CardIndex.Value, editKey.AxisIndex.Value, out Axis axis))
                    {
                        SF.frmPropertyGrid.propertyGrid1.SelectedObject = axis;
                    }
                    SF.frmIO.RefreshIODgv();
                }
                treeView2.SelectedNode = null;
            }
        }

        private void treeView1_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Right)
            {
                var treeView = (System.Windows.Forms.TreeView)sender;
                var clickedNode = treeView.GetNodeAt(e.Location);

                if (clickedNode == null) // 点击的是空白区域
                {
                    treeView.SelectedNode = null; // 取消当前节点选择
                    ClearCardSelection();
                }
                if (clickedNode != null)
                {
                    // 选择右键点击的节点
                    treeView.SelectedNode = clickedNode;
                }
            }
        }

        private void Modify_Click(object sender, EventArgs e)
        {
            if (TryGetSelectedAxisIndex(out _, out _))
            {
                SF.BeginEdit(ModifyKind.Axis);
            }
            else if (TryGetSelectedCardIndex(out _))
            {
                SF.BeginEdit(ModifyKind.ControlCard);
            }
        }
        public void RefreshStationList()
        {
            treeView2.Nodes.Clear();

            if (!Directory.Exists(SF.ConfigPath))
            {
                Directory.CreateDirectory(SF.ConfigPath);
            }

            List<DataStation> dataStationsTemp = SF.mainfrm.ReadJson<List<DataStation>>(SF.ConfigPath, "DataStation");
            if (dataStationsTemp != null)
                dataStation = dataStationsTemp;
            if (SF.DR?.Context != null)
            {
                SF.DR.Context.Stations = dataStation;
            }

        }

        public void RefreshStationTree()
        {
            if (dataStation == null)
            {
                return;
            }
            for (int i = 0; i < dataStation.Count; i++)
            {
                TreeNode chnode = new TreeNode(i + "工站："+ dataStation[i].Name);
                treeView2.Nodes.Add(chnode);

            }
            
        }
        private void Remove_Click(object sender, EventArgs e)
        {
            if (TryGetSelectedCardIndex(out int cardIndex))
            {
                if (MessageBox.Show("确认删除选中的控制卡？", "删除确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                {
                    return;
                }
                if (!SF.cardStore.RemoveControlCardAt(cardIndex))
                {
                    return;
                }
                SF.frmIO.IOMap.RemoveAt(cardIndex);
                    for (int i = 0;i < SF.frmIO.IOMap.Count; i++)
                    {
                        for (int j = 0; j < SF.frmIO.IOMap[i].Count; j++)
                        {
                            SF.frmIO.IOMap[i][j].CardNum = i;
                        }
                    }
                SF.cardStore.Save(SF.ConfigPath);
                SF.frmCard.RefreshCardTree();
                SF.mainfrm.SaveAsJson(SF.ConfigPath, "IOMap", SF.frmIO.IOMap);

                SF.frmIO.dgvIO.Rows.Clear();

                SF.frmIO.RefreshIODgv();
            }
        }

        private void AddStation_Click(object sender, EventArgs e)
        {
            dataStationTemp = new DataStation(false);
            if (dataStation == null)
            {
                dataStation = new List<DataStation>();
            }
            dataStation.Add(dataStationTemp);
            SF.frmPropertyGrid.propertyGrid1.SelectedObject = dataStationTemp;
            SF.frmPropertyGrid.propertyGrid1.ExpandAllGridItems();
            editKey.IsNewStation = true;
            SF.BeginEdit(ModifyKind.None);
        }

        private void ModifyStation_Click(object sender, EventArgs e)
        {
            if (TryGetSelectedStationIndex(out int stationIndex))
            {
                SF.BeginEdit(ModifyKind.Station);

                dataStation[stationIndex].dataAxis.axisConfigs[0] = dataStation[stationIndex].dataAxis.axisConfig1;
                dataStation[stationIndex].dataAxis.axisConfigs[1] = dataStation[stationIndex].dataAxis.axisConfig2;
                dataStation[stationIndex].dataAxis.axisConfigs[2] = dataStation[stationIndex].dataAxis.axisConfig3;
                dataStation[stationIndex].dataAxis.axisConfigs[3] = dataStation[stationIndex].dataAxis.axisConfig4;
                dataStation[stationIndex].dataAxis.axisConfigs[4] = dataStation[stationIndex].dataAxis.axisConfig5;
                dataStation[stationIndex].dataAxis.axisConfigs[5] = dataStation[stationIndex].dataAxis.axisConfig6;

                dataStation[stationIndex].homeSeq.axisSeq[0] = dataStation[stationIndex].homeSeq.AxisName1;
                dataStation[stationIndex].homeSeq.axisSeq[1] = dataStation[stationIndex].homeSeq.AxisName2;
                dataStation[stationIndex].homeSeq.axisSeq[2] = dataStation[stationIndex].homeSeq.AxisName3;
                dataStation[stationIndex].homeSeq.axisSeq[3] = dataStation[stationIndex].homeSeq.AxisName4;
                dataStation[stationIndex].homeSeq.axisSeq[4] = dataStation[stationIndex].homeSeq.AxisName5;
                dataStation[stationIndex].homeSeq.axisSeq[5] = dataStation[stationIndex].homeSeq.AxisName6;
            }
           
           
        }

        private void RemoveStation_Click(object sender, EventArgs e)
        {
            if (TryGetSelectedStationIndex(out int stationIndex))
            {
                if (MessageBox.Show("确认删除选中的工站？", "删除确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                {
                    return;
                }
                dataStation.RemoveAt(stationIndex);
                SF.mainfrm.SaveAsJson(SF.ConfigPath, "DataStation", dataStation);

                SF.frmCard.RefreshStationList();
                SF.frmCard.RefreshStationTree();
            }
        }

        private void treeView2_AfterSelect(object sender, TreeViewEventArgs e)
        {
            editKey.StationIndex = treeView2.SelectedNode.Index;
            SF.frmPropertyGrid.propertyGrid1.SelectedObject = dataStation[editKey.StationIndex.Value];
            SF.frmPropertyGrid.propertyGrid1.ExpandAllGridItems();
          
        }

        private void treeView2_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Right)
            {
                var treeView = (System.Windows.Forms.TreeView)sender;
                var clickedNode = treeView.GetNodeAt(e.Location);

                if (clickedNode == null) // 点击的是空白区域
                {
                    treeView.SelectedNode = null; // 取消当前节点选择
                    ClearStationSelection();
                }
                if (clickedNode != null)
                {
                    // 选择右键点击的节点
                    treeView.SelectedNode = clickedNode;
                }
            }
        }
    }
    public class CardItem : StringConverter
    {
        public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
        {
            return true;
        }

        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
        {
            return new StandardValuesCollection(Enumerable.Range(0, SF.cardStore.GetControlCardCount())
            .Select(index => index.ToString())
            .ToList());
        }
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
        {
            return true;
        }
    }
    public class AxisItem : StringConverter
    {
        public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
        {
            return true;
        }

        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
        {
            return new StandardValuesCollection(axisItme);
        }
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
        {
            return true;
        }
    }
    public class HomeTypeItem : StringConverter
    {
        public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
        {
            return true;
        }

        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
        {
            return new StandardValuesCollection(new List<string>() { "从正限位回零", "从负限位回零", "从当前位回零" });
        }
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
        {
            return true;
        }
    }
    public class DataStation
    {
        [DisplayName("站名"), Category("A基本参数"), Description(""), ReadOnly(false)]
        public string Name { get; set; }

        [DisplayName("轴配置"), Category("B工站配置"), Description(""), ReadOnly(false), TypeConverter(typeof(ExpandableObjectConverter))]

        public DataAxis dataAxis { get; set; }


        [DisplayName("轴回原顺序"), Category("B工站配置"), Description(""), ReadOnly(false), TypeConverter(typeof(ExpandableObjectConverter))]

        public HomeSeq homeSeq { get; set; }

       // [Browsable(false)]
       // public List<DataPos> dataPosList { get; set; }
        [Browsable(false)]
        public Dictionary<string, DataPos> dicDataPos { get; set; }

        [Browsable(false)]
        public List<Axis> axes;

        [Browsable(false)]
        public List<DataPos> ListDataPos;

        [Browsable(false)]
        public double Vel { get; set; } = 1;

        //工站状态 1 表示运动，0表示停止
        [Browsable(false)]
        [JsonIgnore]
        public Status State { get; set; } = 0;

        public Status GetState()
        {
            return State;
        }
        public void SetState(Status state)
        {
            State = state;
        }
        public enum Status
        {
            NotReady = -1,
            Ready,
            Run,
        }
        public DataStation(bool isnull)
        {
            dataAxis = new DataAxis(Name);
            homeSeq = new HomeSeq(Name);
            dicDataPos = new Dictionary<string, DataPos>();
            ListDataPos = new List<DataPos>();

            if (isnull == false)
            {
                for (int i = 0; i < 400; i++)
                {
                    DataPos dataPos = dicDataPos.Values.FirstOrDefault(item => item.Index == i);
                    if (dataPos != null)
                    {
                        ListDataPos.Add(dataPos);
                    }
                    else
                    {
                        ListDataPos.Add(new DataPos(i));
                    }

                }
            }
          
        }


    }
    [Serializable]
    public class DataPos : ICloneable
    {
        public int Index { get; set; }
        public string Name { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double U { get; set; }
        public double V { get; set; }
        public double W { get; set; }

        public List<double> GetAllValues()
        {
            List<double> allValues = new List<double>
        {
            X,
            Y,
            Z,
            U,
            V,
            W
            };
            return allValues;
        }

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
        public DataPos(int index)
        {
            X = 0;
            Y = 0;
            Z = 0;
            U = 0;
            V = 0;
            W = 0;
            Index = index;
            Name = "";
         
        }
        
    }
    public class DataAxis
    {
        [Browsable(false)]
        public string Name { get; set; }
        public override string ToString()
        {
            return "";
        }
     
        [DisplayName("轴1"), Description(""), ReadOnly(false), TypeConverter(typeof(ExpandableObjectConverter))]

        public AxisConfig axisConfig1 { get; set; }

        [DisplayName("轴2"), Description(""), ReadOnly(false), TypeConverter(typeof(ExpandableObjectConverter))]

        public AxisConfig axisConfig2 { get; set; }

        [DisplayName("轴3"), Description(""), ReadOnly(false), TypeConverter(typeof(ExpandableObjectConverter))]

        public AxisConfig axisConfig3 { get; set; }

        [DisplayName("轴4"), Description(""), ReadOnly(false), TypeConverter(typeof(ExpandableObjectConverter))]
        public AxisConfig axisConfig4 { get; set; }

        [DisplayName("轴5"), Description(""), ReadOnly(false), TypeConverter(typeof(ExpandableObjectConverter))]

        public AxisConfig axisConfig5 { get; set; }

        [DisplayName("轴6"), Description(""), ReadOnly(false), TypeConverter(typeof(ExpandableObjectConverter))]
        public AxisConfig axisConfig6 { get; set; }
        [Browsable(false)]
        public List<AxisConfig> axisConfigs = new List<AxisConfig>();
        

        public DataAxis(string name)
        {
            axisConfig1 = new AxisConfig();
            axisConfig2 = new AxisConfig();
            axisConfig3 = new AxisConfig();
            axisConfig4 = new AxisConfig();
            axisConfig5 = new AxisConfig();
            axisConfig6 = new AxisConfig();
            axisConfigs.Add(axisConfig1);
            axisConfigs.Add(axisConfig2);
            axisConfigs.Add(axisConfig3);
            axisConfigs.Add(axisConfig4);
            axisConfigs.Add(axisConfig5);
            axisConfigs.Add(axisConfig6);

            Name = name;
        }
    }
    public class CategoriesSortedByClassDefinitionConverter : ExpandableObjectConverter
    {
        [AttributeUsage(AttributeTargets.Class)]
        public class ElementsSameOrderAttribute : Attribute
        {
            public bool IsSameOrder { get; set; } = true;
            public ElementsSameOrderAttribute(bool isSameOrder = true)
            {
                IsSameOrder = isSameOrder;
            }
        }
       
      
    }
    
    public class AxisConfig
    {
      
        public override string ToString()
        {
            return "";
        }

        private string cardNum;
        [DisplayName("卡编号"), Description(""), ReadOnly(false), TypeConverter(typeof(CardItem))]

        public string CardNum
        {
            get { return cardNum; }
            set
            {
                cardNum = value;
                if ((SF.isModify == ModifyKind.Station || CardNum != null) && value != "-1")
                {
                    int num = int.Parse(cardNum);
                    if (CardNum != null && SF.cardStore.TryGetControlCard(num, out ControlCard controlCard))
                    {
                        axisItme.Clear();
                        foreach (var item in controlCard.axis)
                        {
                            axisItme.Add(item.AxisName);
                        }
                    }

                }
            }
        }

        [Browsable(false)]
        public Axis axis { get; set; } = null;
        public string axisName;
        [DisplayName("轴名称"), Description(""), ReadOnly(false), TypeConverter(typeof(AxisItem))]
        public string AxisName
        {
            get { return axisName; }
            set
            {
                axisName = value;
                if ((SF.isModify == ModifyKind.Station || SF.frmCard.IsNewStation) && value != "-1")
                {
                    if (SF.cardStore.TryGetAxisByName(int.Parse(CardNum), value, out Axis axis))
                    {
                        this.axis = axis;
                    }
                }
            }
        }
        public AxisConfig( )
        {
            AxisName = "-1";
            CardNum = "-1";
        }
    }
    public class AxisName
    {
        public override string ToString()
        {
            return "";
        }
        [DisplayName("回原轴"), Description(""), ReadOnly(false), TypeConverter(typeof(AxisItem))]
        public string Name { get; set; }

        public AxisName()
        {
            Name = "-1";
        }

    }
    public class HomeSeq
    {
        [Browsable(false)]
        public string Name { get; set; }

        [DisplayName("第1回原轴"), Description(""), ReadOnly(false), TypeConverter(typeof(ExpandableObjectConverter))]
        public AxisName AxisName1 { get; set; }

        [DisplayName("第2回原轴"), Description(""), ReadOnly(false), TypeConverter(typeof(ExpandableObjectConverter))]
        public AxisName AxisName2 { get; set; }

        [DisplayName("第3回原轴"), Description(""), ReadOnly(false), TypeConverter(typeof(ExpandableObjectConverter))]
        public AxisName AxisName3 { get; set; }
        [DisplayName("第4回原轴"), Description(""), ReadOnly(false), TypeConverter(typeof(ExpandableObjectConverter))]
        public AxisName AxisName4 { get; set; }
        [DisplayName("第5回原轴"), Description(""), ReadOnly(false), TypeConverter(typeof(ExpandableObjectConverter))]
        public AxisName AxisName5 { get; set; }
        [DisplayName("第6回原轴"), Description(""), ReadOnly(false), TypeConverter(typeof(ExpandableObjectConverter))]
        public AxisName AxisName6 { get; set; }

        [Browsable(false)]
        public List<AxisName> axisSeq = new List<AxisName>(); 

        public override string ToString()
        {
            return "";
        }
        public HomeSeq(string name)
        {
            AxisName1 = new AxisName();
            AxisName2 = new AxisName();
            AxisName3 = new AxisName();
            AxisName4 = new AxisName();
            AxisName5 = new AxisName();
            AxisName6 = new AxisName();
            axisSeq.Add(AxisName1);
            axisSeq.Add(AxisName2);
            axisSeq.Add(AxisName3);
            axisSeq.Add(AxisName4);
            axisSeq.Add(AxisName5);
            axisSeq.Add(AxisName6);
            Name = name;
        }
    }
}
