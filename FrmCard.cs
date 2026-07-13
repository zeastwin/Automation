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
            ApplyCardStyle();
            contextMenuStrip1.Opening += contextMenuStrip1_Opening;
            contextMenuStrip2.Opening += contextMenuStrip2_Opening;

        }
        public bool IsNewCard => editKey.IsNewCard;
        public bool IsNewStation => editKey.IsNewStation;
        public bool IsCardRootSelected => editKey.Kind == EditKind.CardRoot;

        private static T CloneForEdit<T>(T source)
        {
            return ObjectGraphCloner.Clone(source);
        }

        private void ApplyCardStyle()
        {
            BackColor = Color.White;
            groupBox1.ForeColor = Color.FromArgb(48, 63, 78);
            groupBox2.ForeColor = Color.FromArgb(48, 63, 78);
            foreach (System.Windows.Forms.TreeView tree in new[] { treeView1, treeView2 })
            {
                tree.BackColor = Color.White;
                tree.ForeColor = Color.FromArgb(48, 63, 78);
                tree.BorderStyle = BorderStyle.None;
                tree.Font = new Font("微软雅黑", 10F, FontStyle.Regular, GraphicsUnit.Point, 134);
                tree.ItemHeight = 27;
                tree.ShowNodeToolTips = true;
            }
            foreach (ContextMenuStrip menu in new[] { contextMenuStrip1, contextMenuStrip2 })
            {
                menu.Font = new Font("微软雅黑", 9F, FontStyle.Regular, GraphicsUnit.Point, 134);
                menu.ShowImageMargin = false;
            }
        }

        private void contextMenuStrip1_Opening(object sender, CancelEventArgs e)
        {
            AddCard.Enabled = editKey.Kind == EditKind.CardRoot;
            Modify.Enabled = editKey.Kind == EditKind.Card || editKey.Kind == EditKind.Axis;
            Remove.Enabled = editKey.Kind == EditKind.Card;
        }

        private void contextMenuStrip2_Opening(object sender, CancelEventArgs e)
        {
            bool stationSelected = editKey.StationIndex.HasValue;
            AddStation.Enabled = true;
            ModifyStation.Enabled = stationSelected;
            RemoveStation.Enabled = stationSelected;
        }

        private void FinishDraftEdit()
        {
            treeView1.Enabled = true;
            treeView2.Enabled = true;
            editKey.IsNewCard = false;
            editKey.IsNewStation = false;
            controlCardTemp = null;
            axisTemp = null;
            dataStationTemp = null;
        }

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
            [DisplayName("回原搜索方向"), Category("A基本参数"), Description("控制卡搜索原点的方向；回原模式固定为一次回零加回找。"), ReadOnly(false), TypeConverter(typeof(HomeDirectionItem))]
            public string HomeDirection { get; set; }
            //[DisplayName("编码器类型"), Category("A基本参数"), Description(""), ReadOnly(false)]
            //public int EncoderType { get; set; }
            //[DisplayName("报警索引"), Category("A基本参数"), Description(""), ReadOnly(false)]
            //public int WarnIndex { get; set; }
            //[DisplayName("从站ID"), Category("A基本参数"), Description(""), ReadOnly(false)]
            //public int SlaveStation { get; set; }
            //[DisplayName("回原前偏移量"), Category("B回原参数"), Description(""), ReadOnly(false)]
            //public int OffsetBeforeHome { get; set; }
            //[DisplayName("回原搜索距离"), Category("B回原参数"), Description(""), ReadOnly(false)]
            //public int HomeDistance { get; set; }
            //[DisplayName("回原后偏移量"), Category("B回原参数"), Description(""), ReadOnly(false)]
            //public int OffsetAfterHome { get; set; }
            [DisplayName("回原速度"), Category("B回原参数"), Description(""), ReadOnly(false)]
            public string HomeSpeed { get; set; }
            [DisplayName("速度说明"), Category("C运动参数"), Description(""), ReadOnly(false)]
            public int SpeedInfo { get; set; }

            [DisplayName("最大速度"), Category("C运动参数"), Description(""), ReadOnly(false)]
            public int SpeedMax { get; set; }
            [DisplayName("加速度时间"), Category("C运动参数"), Description(""), ReadOnly(false)]
            public double AccMax { get; set; }
            [DisplayName("减速度时间"), Category("C运动参数"), Description(""), ReadOnly(false)]
            public double DecMax { get; set; }
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
                treeView1.Enabled = false;
                treeView2.Enabled = false;
                SF.BeginEditSession(new EditSession<CardHead>("新增控制卡", controlCardTemp.cardHead,
                    draft => draft.AxisCount < 0 || draft.InputCount < 0 || draft.OutputCount < 0
                        ? "控制卡轴数和IO数量不能为负数。" : null,
                    draft =>
                    {
                        for (int i = 0; i < draft.AxisCount; i++)
                        {
                            controlCardTemp.axis.Add(new Axis { AxisName = $"Axis{i}", AxisNum = i });
                        }
                        int newCardIndex = SF.cardStore.AddControlCard(controlCardTemp);
                        var ioItems = new List<IO>();
                        for (int i = 0; i < draft.InputCount; i++)
                        {
                            ioItems.Add(new IO
                            {
                                Index = i,
                                CardNum = newCardIndex,
                                Module = 0,
                                IOIndex = i.ToString(),
                                IOType = "通用输入",
                                UsedType = "通用",
                                EffectLevel = "正常"
                            });
                        }
                        for (int i = 0; i < draft.OutputCount; i++)
                        {
                            ioItems.Add(new IO
                            {
                                Index = draft.InputCount + i,
                                CardNum = newCardIndex,
                                Module = 0,
                                IOIndex = i.ToString(),
                                IOType = "通用输出",
                                UsedType = "通用",
                                EffectLevel = "正常"
                            });
                        }
                        SF.frmIO.IOMap.Add(ioItems);
                        try
                        {
                            var settings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.All };
                            using (var batch = new ConfigurationBatchWriter(SF.ConfigPath))
                            {
                                batch.AddJson("card.json", SF.cardStore.CardData, settings);
                                batch.AddJson("IOMap.json", SF.frmIO.IOMap, settings);
                                batch.Commit();
                            }
                        }
                        catch
                        {
                            SF.cardStore.RemoveControlCardAt(newCardIndex);
                            SF.frmIO.IOMap.RemoveAt(newCardIndex);
                            SF.SetSecurityLock("控制卡与IO配置事务提交失败，禁止继续运行，需检查配置文件。");
                            throw;
                        }
                        FinishDraftEdit();
                        RefreshCardTree();
                        SF.frmIO.RefreshIODgv();
                        SF.mainfrm.ResetAxisRuntimeState();
                        SF.mainfrm.RequireRestartAfterMotionConfigurationChange();
                    }, FinishDraftEdit));
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
            if (TryGetSelectedAxisIndex(out int cardIndex, out int axisIndex)
                && SF.cardStore.TryGetAxis(cardIndex, axisIndex, out Axis sourceAxis))
            {
                axisTemp = CloneForEdit(sourceAxis);
                treeView1.Enabled = false;
                treeView2.Enabled = false;
                SF.BeginEditSession(new EditSession<Axis>("修改轴", axisTemp,
                    draft => SF.cardStore.TryValidateAxis(cardIndex, axisIndex, draft, out string error) ? null : error,
                    draft =>
                    {
                        SF.cardStore.ReplaceAxis(cardIndex, axisIndex, draft);
                        if (!SF.cardStore.Save(SF.ConfigPath, false))
                        {
                            SF.cardStore.ReplaceAxis(cardIndex, axisIndex, sourceAxis);
                            throw new InvalidOperationException("轴配置保存失败。");
                        }
                        if (SF.cardStore.TryValidateAllAxes(out _))
                        {
                            try
                            {
                                SF.motion.SetAllAxisEquiv();
                            }
                            catch
                            {
                                SF.mainfrm.RequireRestartAfterMotionConfigurationChange();
                                throw;
                            }
                        }
                        FinishDraftEdit();
                        RefreshCardTree();
                        SF.mainfrm.ResetAxisRuntimeState();
                    }, FinishDraftEdit));
            }
            else if (TryGetSelectedCardIndex(out cardIndex)
                && SF.cardStore.TryGetControlCard(cardIndex, out ControlCard sourceCard))
            {
                controlCardTemp = CloneForEdit(sourceCard);
                treeView1.Enabled = false;
                treeView2.Enabled = false;
                SF.BeginEditSession(new EditSession<CardHead>("修改控制卡", controlCardTemp.cardHead,
                    draft => draft.AxisCount < 0 || draft.InputCount < 0 || draft.OutputCount < 0
                        ? "控制卡轴数和IO数量不能为负数。" : null,
                    draft =>
                    {
                        while (controlCardTemp.axis.Count > draft.AxisCount)
                        {
                            controlCardTemp.axis.RemoveAt(controlCardTemp.axis.Count - 1);
                        }
                        while (controlCardTemp.axis.Count < draft.AxisCount)
                        {
                            int axisIndex = controlCardTemp.axis.Count;
                            controlCardTemp.axis.Add(new Axis { AxisName = $"Axis{axisIndex}", AxisNum = axisIndex });
                        }
                        SF.cardStore.ReplaceControlCard(cardIndex, controlCardTemp);
                        if (!SF.cardStore.Save(SF.ConfigPath, false))
                        {
                            SF.cardStore.ReplaceControlCard(cardIndex, sourceCard);
                            throw new InvalidOperationException("控制卡配置保存失败。");
                        }
                        SF.mainfrm.RequireRestartAfterMotionConfigurationChange();
                        FinishDraftEdit();
                        RefreshCardTree();
                        SF.mainfrm.ResetAxisRuntimeState();
                    }, FinishDraftEdit));
            }
        }
        public void RefreshStationList()
        {
            treeView2.Nodes.Clear();

            if (!Directory.Exists(SF.ConfigPath))
            {
                Directory.CreateDirectory(SF.ConfigPath);
            }

            string filePath = Path.Combine(SF.ConfigPath, "DataStation.json");
            if (!File.Exists(filePath))
            {
                dataStation = new List<DataStation>();
                if (!AtomicJsonFileStore.Save(SF.ConfigPath, "DataStation", dataStation))
                {
                    throw new InvalidOperationException($"工站配置模板生成失败:{filePath}");
                }
            }

            List<DataStation> dataStationsTemp = AtomicJsonFileStore.Read<List<DataStation>>(SF.ConfigPath, "DataStation");
            if (dataStationsTemp != null)
            {
                dataStation = dataStationsTemp;
            }
            if (!SF.cardStore.TryValidateStations(dataStation, out List<string> stationErrors))
            {
                SF.DR?.Logger?.Log("工站配置加载校验失败：" + string.Join("; ", stationErrors), LogLevel.Error);
                MessageBox.Show("工站配置校验失败：\r\n" + string.Join("\r\n", stationErrors),
                    "工站配置错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            if (SF.DR?.Context != null)
            {
                SF.DR.Context.Stations = dataStation;
            }

        }

        public void RefreshStationTree()
        {
            treeView2.Nodes.Clear();
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
            if (editKey.Kind == EditKind.Card && TryGetSelectedCardIndex(out int cardIndex))
            {
                if (MessageBox.Show("确认删除选中的控制卡？", "删除确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                {
                    return;
                }
                Card cardBackup = CloneForEdit(SF.cardStore.CardData);
                List<List<IO>> ioBackup = CloneForEdit(SF.frmIO.IOMap);
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
                try
                {
                    var settings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.All };
                    using (var batch = new ConfigurationBatchWriter(SF.ConfigPath))
                    {
                        batch.AddJson("card.json", SF.cardStore.CardData, settings);
                        batch.AddJson("IOMap.json", SF.frmIO.IOMap, settings);
                        batch.Commit();
                    }
                }
                catch
                {
                    SF.cardStore.SetCard(cardBackup);
                    SF.frmIO.IOMap = ioBackup;
                    SF.SetSecurityLock("删除控制卡配置事务失败，正式内存已恢复，需检查配置文件。");
                    throw;
                }
                SF.frmCard.RefreshCardTree();
                SF.mainfrm.RequireRestartAfterMotionConfigurationChange();
                SF.mainfrm.ResetAxisRuntimeState();
                SF.frmIO.dgvIO.Rows.Clear();

                SF.frmIO.RefreshIODgv();
            }
        }

        private void AddStation_Click(object sender, EventArgs e)
        {
            dataStationTemp = new DataStation(false);
            treeView1.Enabled = false;
            treeView2.Enabled = false;
            SF.BeginEditSession(new EditSession<DataStation>("新增工站", dataStationTemp,
                draft =>
                {
                    var candidate = new List<DataStation>(dataStation ?? new List<DataStation>()) { draft };
                    return SF.cardStore.TryValidateStations(candidate, out List<string> errors)
                        ? null : string.Join("\r\n", errors);
                },
                draft =>
                {
                    if (dataStation == null)
                    {
                        dataStation = new List<DataStation>();
                    }
                    dataStation.Add(draft);
                    if (!AtomicJsonFileStore.Save(SF.ConfigPath, "DataStation", dataStation))
                    {
                        dataStation.Remove(draft);
                        throw new InvalidOperationException("工站配置保存失败。");
                    }
                    if (SF.DR?.Context != null)
                    {
                        SF.DR.Context.Stations = dataStation;
                    }
                    FinishDraftEdit();
                    RefreshStationTree();
                }, FinishDraftEdit));
            SF.frmPropertyGrid.propertyGrid1.ExpandAllGridItems();
        }

        private void ModifyStation_Click(object sender, EventArgs e)
        {
            if (TryGetSelectedStationIndex(out int stationIndex))
            {
                dataStationTemp = CloneForEdit(dataStation[stationIndex]);
                treeView1.Enabled = false;
                treeView2.Enabled = false;
                SF.BeginEditSession(new EditSession<DataStation>("修改工站", dataStationTemp,
                    draft =>
                    {
                        var candidate = new List<DataStation>(dataStation);
                        candidate[stationIndex] = draft;
                        return SF.cardStore.TryValidateStations(candidate, out List<string> errors)
                            ? null : string.Join("\r\n", errors);
                    },
                    draft =>
                    {
                        DataStation original = dataStation[stationIndex];
                        dataStation[stationIndex] = draft;
                        if (!AtomicJsonFileStore.Save(SF.ConfigPath, "DataStation", dataStation))
                        {
                            dataStation[stationIndex] = original;
                            throw new InvalidOperationException("工站配置保存失败。");
                        }
                        if (SF.DR?.Context != null)
                        {
                            SF.DR.Context.Stations = dataStation;
                        }
                        FinishDraftEdit();
                        RefreshStationTree();
                    }, FinishDraftEdit));
                SF.frmPropertyGrid.propertyGrid1.ExpandAllGridItems();
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
                DataStation removed = dataStation[stationIndex];
                dataStation.RemoveAt(stationIndex);
                if (!AtomicJsonFileStore.Save(SF.ConfigPath, "DataStation", dataStation))
                {
                    dataStation.Insert(stationIndex, removed);
                    throw new InvalidOperationException("删除工站保存失败，正式内存已恢复。");
                }

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
    public class HomeDirectionItem : StringConverter
    {
        public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
        {
            return true;
        }

        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
        {
            return new StandardValuesCollection(new List<string>() { "正向", "负向" });
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
        public List<DataPos> ListDataPos;

        private double manualSpeedPercent = 10;
        [Browsable(false)]
        public double ManualSpeedPercent
        {
            get => manualSpeedPercent;
            set
            {
                if (value < 1 || value > 100 || double.IsNaN(value) || double.IsInfinity(value))
                {
                    throw new ArgumentOutOfRangeException(nameof(value), "手动调试速度百分比必须在1到100之间。");
                }
                manualSpeedPercent = value;
            }
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
            return ObjectGraphCloner.Clone(this);
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
        [JsonIgnore]
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
                if (value != "-1" && CardNum != null && SF.cardStore != null)
                {
                    if (int.TryParse(cardNum, out int num)
                        && SF.cardStore.TryGetControlCard(num, out ControlCard controlCard))
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
                if (value != "-1"
                    && SF.ActiveEditSession?.Draft is DataStation
                    && SF.cardStore != null
                    && int.TryParse(CardNum, out int cardNum))
                {
                    if (SF.cardStore.TryGetAxisByName(cardNum, value, out Axis axis))
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
        [JsonIgnore]
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
