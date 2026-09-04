// 模块：编辑器 / 运动。
// 职责范围：控制卡、工站和手动运动的配置与交互。

using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Dynamic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.StartPanel;
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

      
        
        private readonly StationDefinitionStore stationDefinitionStore;
        public List<DataStation> dataStation => stationDefinitionStore.Items;
        //存放临时工站信息
        public DataStation dataStationTemp;

        public FrmCard()
            : this(new StationDefinitionStore())
        {
        }

        public FrmCard(StationDefinitionStore stationDefinitionStore)
        {
            this.stationDefinitionStore = stationDefinitionStore
                ?? throw new ArgumentNullException(nameof(stationDefinitionStore));
            InitializeComponent();
            editKey = EditKey.None;
            this.treeView1.HideSelection = false;
            this.treeView2.HideSelection = false;
            ApplyCardStyle();
            contextMenuStrip1.Opening += contextMenuStrip1_Opening;
            contextMenuStrip2.Opening += contextMenuStrip2_Opening;

        }
        public bool IsCardRootSelected => editKey.Kind == EditKind.CardRoot;

        private static T CloneForEdit<T>(T source)
        {
            return ObjectGraphCloner.Clone(source);
        }

        /// <summary>
        /// 运动配置写盘后的统一收口：第一时间关闭运动门，正式内存替换失败仍向上抛出；
        /// 提示或界面刷新失败只记录降级，不能把已经提交的配置伪装成提交失败。
        /// </summary>
        internal static void CompleteCommittedMotionConfiguration(
            PlatformReadinessState readiness,
            Action applyCommittedState,
            Action<string> logError,
            params Action[] bestEffortActions)
        {
            if (readiness == null)
            {
                throw new ArgumentNullException(nameof(readiness));
            }
            readiness.MotionConfigRestartRequired = true;
            applyCommittedState?.Invoke();
            foreach (Action action in bestEffortActions ?? Array.Empty<Action>())
            {
                if (action == null)
                {
                    continue;
                }
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    try
                    {
                        logError?.Invoke(
                            $"运动配置已经提交，但提交后界面刷新或提示失败:{ex.Message}");
                    }
                    catch
                    {
                    }
                }
            }
        }

        private IDisposable BeginMotionConfigurationCommit(string reason)
        {
            PlatformRuntime runtime = Workspace.Runtime
                ?? throw new InvalidOperationException("平台运行时尚未初始化。");
            if (!runtime.Maintenance.TryBegin(reason, out IDisposable lease, out string beginError))
            {
                throw new InvalidOperationException(beginError);
            }
            try
            {
                if (runtime.ProcessEngine == null)
                {
                    throw new InvalidOperationException("流程引擎尚未初始化，不能提交运动配置。");
                }
                if (!runtime.ProcessEngine.TryValidateMotionConfigurationIdle(out string idleError))
                {
                    throw new InvalidOperationException(idleError);
                }
                return lease;
            }
            catch
            {
                lease.Dispose();
                throw;
            }
        }

        private string ValidateNewCard(CardHead draft)
        {
            return TryBuildNewCardCandidates(
                draft,
                out _,
                out _,
                out string error)
                ? null
                : error;
        }

        private string ValidateCardHeadEdit(int cardIndex, CardHead draft)
        {
            if (draft == null)
            {
                return "控制卡参数为空。";
            }
            if (draft.AxisCount < 0 || draft.InputCount < 0 || draft.OutputCount < 0)
            {
                return "控制卡轴数和IO数量不能为负数。";
            }
            return TryBuildCardHeadEditCandidates(
                cardIndex,
                draft,
                out _,
                out _,
                out string error)
                ? null
                : error;
        }

        private string ValidateAxisEdit(int cardIndex, int axisIndex, Axis draft)
        {
            return TryBuildAxisEditCandidate(
                cardIndex,
                axisIndex,
                draft,
                out _,
                out string error)
                ? null
                : error;
        }

        private bool TryBuildAxisEditCandidate(
            int cardIndex,
            int axisIndex,
            Axis draft,
            out Card candidateCard,
            out string error)
        {
            candidateCard = null;
            error = null;
            Card currentCard = Workspace.Runtime.Stores.Cards.CardData;
            if (draft == null
                || currentCard?.controlCards == null
                || cardIndex < 0
                || cardIndex >= currentCard.controlCards.Count
                || currentCard.controlCards[cardIndex]?.axis == null
                || axisIndex < 0
                || axisIndex >= currentCard.controlCards[cardIndex].axis.Count)
            {
                error = $"控制卡或轴索引无效:{cardIndex}-{axisIndex}";
                return false;
            }

            candidateCard = CloneForEdit(currentCard);
            candidateCard.controlCards[cardIndex].axis[axisIndex] = CloneForEdit(draft);
            var candidateStore = new CardConfigStore();
            candidateStore.SetCard(candidateCard);
            if (!candidateStore.TryValidateAllAxes(out List<string> cardErrors))
            {
                error = "控制卡配置校验失败：" + string.Join("；", cardErrors);
                return false;
            }
            List<DataStation> candidateStations = CloneForEdit(
                dataStation ?? new List<DataStation>());
            if (!candidateStore.TryValidateStations(candidateStations, out List<string> stationErrors))
            {
                error = "修改轴会破坏现有工站配置：" + string.Join("；", stationErrors);
                return false;
            }
            return true;
        }

        private bool TryBuildCardHeadEditCandidates(
            int cardIndex,
            CardHead draft,
            out Card candidateCard,
            out List<List<IO>> candidateIoMap,
            out string error)
        {
            candidateCard = null;
            candidateIoMap = null;
            error = null;
            if (draft == null || controlCardTemp == null)
            {
                error = "控制卡编辑草稿为空。";
                return false;
            }
            Card currentCard = Workspace.Runtime.Stores.Cards.CardData;
            if (currentCard?.controlCards == null
                || cardIndex < 0
                || cardIndex >= currentCard.controlCards.Count)
            {
                error = $"控制卡索引无效:{cardIndex}";
                return false;
            }

            ControlCard candidateControlCard = CloneForEdit(controlCardTemp);
            candidateControlCard.cardHead = CloneForEdit(draft);
            candidateControlCard.axis = candidateControlCard.axis ?? new List<Axis>();
            while (candidateControlCard.axis.Count > draft.AxisCount)
            {
                candidateControlCard.axis.RemoveAt(candidateControlCard.axis.Count - 1);
            }
            while (candidateControlCard.axis.Count < draft.AxisCount)
            {
                int axisIndex = candidateControlCard.axis.Count;
                candidateControlCard.axis.Add(new Axis
                {
                    AxisName = $"Axis{axisIndex}",
                    AxisNum = axisIndex
                });
            }

            candidateCard = CloneForEdit(currentCard);
            candidateCard.controlCards[cardIndex] = candidateControlCard;
            var candidateStore = new CardConfigStore();
            candidateStore.SetCard(candidateCard);
            if (!candidateStore.TryValidateAllAxes(out List<string> cardErrors))
            {
                error = "控制卡配置校验失败：" + string.Join("；", cardErrors);
                return false;
            }

            List<DataStation> candidateStations = CloneForEdit(
                dataStation ?? new List<DataStation>());
            if (!candidateStore.TryValidateStations(candidateStations, out List<string> stationErrors))
            {
                error = "修改轴数量会破坏现有工站配置：" + string.Join("；", stationErrors);
                return false;
            }
            if (!Workspace.Runtime.Stores.IoConfiguration.TryCreateResizedCardMap(
                    cardIndex,
                    draft.InputCount,
                    draft.OutputCount,
                    out candidateIoMap,
                    out error))
            {
                return false;
            }
            if (!candidateStore.TryValidateIoMap(candidateIoMap, out string cardIoError))
            {
                error = cardIoError;
                return false;
            }
            return true;
        }

        private bool TryBuildNewCardCandidates(
            CardHead draft,
            out Card candidateCard,
            out List<List<IO>> candidateIoMap,
            out string error)
        {
            candidateCard = null;
            candidateIoMap = null;
            error = null;
            if (draft == null || controlCardTemp == null)
            {
                error = "控制卡编辑草稿为空。";
                return false;
            }
            if (draft.AxisCount < 0 || draft.InputCount < 0 || draft.OutputCount < 0)
            {
                error = "控制卡轴数和IO数量不能为负数。";
                return false;
            }

            Card currentCard = Workspace.Runtime.Stores.Cards.CardData;
            if (currentCard?.controlCards == null)
            {
                error = "控制卡配置为空。";
                return false;
            }
            if (currentCard.controlCards.Count != 0)
            {
                error = "当前平台只允许配置一张雷赛总线卡。";
                return false;
            }

            ControlCard candidateControlCard = CloneForEdit(controlCardTemp);
            candidateControlCard.cardHead = CloneForEdit(draft);
            candidateControlCard.axis = new List<Axis>();
            for (int axisIndex = 0; axisIndex < draft.AxisCount; axisIndex++)
            {
                candidateControlCard.axis.Add(new Axis
                {
                    AxisName = $"Axis{axisIndex}",
                    AxisNum = axisIndex
                });
            }
            candidateCard = CloneForEdit(currentCard);
            candidateCard.controlCards.Add(candidateControlCard);

            var candidateStore = new CardConfigStore();
            candidateStore.SetCard(candidateCard);
            if (!candidateStore.TryValidateAllAxes(out List<string> cardErrors))
            {
                error = "控制卡配置校验失败：" + string.Join("；", cardErrors);
                return false;
            }
            List<DataStation> candidateStations = CloneForEdit(
                dataStation ?? new List<DataStation>());
            if (!candidateStore.TryValidateStations(candidateStations, out List<string> stationErrors))
            {
                error = "新增控制卡与现有工站配置冲突：" + string.Join("；", stationErrors);
                return false;
            }
            if (!Workspace.Runtime.Stores.IoConfiguration.TryCreateResizedCardMap(
                    0,
                    draft.InputCount,
                    draft.OutputCount,
                    out candidateIoMap,
                    out error))
            {
                return false;
            }
            if (!candidateStore.TryValidateIoMap(candidateIoMap, out string cardIoError))
            {
                error = cardIoError;
                return false;
            }
            return true;
        }

        private bool TryBuildRemovedCardCandidates(
            int cardIndex,
            out Card candidateCard,
            out List<List<IO>> candidateIoMap,
            out string error)
        {
            candidateCard = null;
            candidateIoMap = null;
            error = null;
            Card currentCard = Workspace.Runtime.Stores.Cards.CardData;
            if (currentCard?.controlCards == null
                || cardIndex < 0
                || cardIndex >= currentCard.controlCards.Count)
            {
                error = $"控制卡索引无效:{cardIndex}";
                return false;
            }

            candidateCard = CloneForEdit(currentCard);
            candidateCard.controlCards.RemoveAt(cardIndex);
            candidateIoMap = Workspace.Runtime.Stores.IoConfiguration.CreateSnapshot();
            if (cardIndex >= candidateIoMap.Count)
            {
                error = $"{cardIndex}号控制卡缺少对应IO分组。";
                return false;
            }
            candidateIoMap.RemoveAt(cardIndex);
            for (int nextCardIndex = 0; nextCardIndex < candidateIoMap.Count; nextCardIndex++)
            {
                List<IO> items = candidateIoMap[nextCardIndex] ?? new List<IO>();
                for (int flatIndex = 0; flatIndex < items.Count; flatIndex++)
                {
                    items[flatIndex].CardNum = nextCardIndex;
                    items[flatIndex].Index = flatIndex;
                }
            }

            var candidateStore = new CardConfigStore();
            candidateStore.SetCard(candidateCard);
            if (!candidateStore.TryValidateAllAxes(out List<string> cardErrors))
            {
                error = "控制卡配置校验失败：" + string.Join("；", cardErrors);
                return false;
            }
            List<DataStation> candidateStations = CloneForEdit(
                dataStation ?? new List<DataStation>());
            if (!candidateStore.TryValidateStations(candidateStations, out List<string> stationErrors))
            {
                error = "删除控制卡会破坏现有工站配置：" + string.Join("；", stationErrors);
                return false;
            }
            if (!candidateStore.TryValidateIoMap(candidateIoMap, out string cardIoError))
            {
                error = cardIoError;
                return false;
            }
            return true;
        }

        private void ApplyCardStyle()
        {
            BackColor = UiPalette.SurfaceStrong;
            groupBox1.ForeColor = UiPalette.TextPrimary;
            groupBox2.ForeColor = UiPalette.TextPrimary;
            foreach (System.Windows.Forms.TreeView tree in new[] { treeView1, treeView2 })
            {
                tree.BackColor = UiPalette.SurfaceStrong;
                tree.ForeColor = UiPalette.TextPrimary;
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
            AddCard.Enabled = editKey.Kind == EditKind.CardRoot
                && Workspace.Runtime.Stores.Cards.GetControlCardCount() == 0;
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

        public void RefreshCardTree()
        {
            treeView1.Nodes.Clear();
            TreeNode treeNode = new TreeNode("控制卡");
            treeView1.Nodes.Add(treeNode);
            int cardCount = Workspace.Runtime.Stores.Cards.GetControlCardCount();
            if (cardCount == 0)
            {
                return;
            }
            for (int i = 0; i < cardCount; i++)
            {
                TreeNode chnode = new TreeNode(i + "号卡：");
                treeView1.Nodes[0].Nodes.Add(chnode);
                if (!Workspace.Runtime.Stores.Cards.TryGetControlCard(i, out ControlCard controlCard))
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
            if (!AuthorizeConfiguration(
                Automation.DeviceSdk.PlatformPermissionCodes.HardwareConfigure,
                "新增控制卡"))
            {
                return;
            }
            //新建控制卡
            if (IsCardRootSelected)
            {
                if (Workspace.Runtime.Stores.Cards.GetControlCardCount() != 0)
                {
                    MessageBox.Show("当前平台只允许配置一张雷赛总线卡。", "控制卡配置",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                controlCardTemp = new ControlCard();
                treeView1.Enabled = false;
                treeView2.Enabled = false;
                Workspace.Runtime.Editor.Begin(new EditSession<CardHead>("新增控制卡", controlCardTemp.cardHead,
                    ValidateNewCard,
                    draft =>
                    {
                        using (BeginMotionConfigurationCommit("新增控制卡配置"))
                        {
                            if (!TryBuildNewCardCandidates(
                                    draft,
                                    out Card candidateCard,
                                    out List<List<IO>> candidateIoMap,
                                    out string candidateError))
                            {
                                throw new InvalidOperationException(candidateError);
                            }
                            var settings = new JsonSerializerSettings
                            {
                                TypeNameHandling = TypeNameHandling.All
                            };
                            using (var batch = new ConfigurationBatchWriter(Workspace.Runtime.Paths.ConfigPath))
                            {
                                batch.AddJson("card.json", candidateCard, settings);
                                batch.AddJson("IOMap.json", candidateIoMap, settings);
                                batch.Commit();
                            }

                            CompleteCommittedMotionConfiguration(
                                Workspace.Runtime.Readiness,
                                () =>
                                {
                                    Workspace.Runtime.Stores.Cards.SetCard(candidateCard);
                                    if (!Workspace.Runtime.Stores.IoConfiguration.TryReplaceMap(
                                            candidateIoMap,
                                            out string ioReplaceError))
                                    {
                                        Workspace.Runtime.Safety.Lock(
                                            $"控制卡与IO配置已提交，但IO正式内存替换失败:{ioReplaceError}");
                                        throw new InvalidOperationException(ioReplaceError);
                                    }
                                    if (!Workspace.Runtime.Stores.Cards.TryValidateStations(
                                            dataStation ?? new List<DataStation>(),
                                            out List<string> reboundStationErrors))
                                    {
                                        string rebindError = string.Join("；", reboundStationErrors);
                                        Workspace.Runtime.Safety.Lock(
                                            $"控制卡配置已提交，但工站轴引用重绑定失败:{rebindError}");
                                        throw new InvalidOperationException(rebindError);
                                    }
                                },
                                message => Workspace.Runtime.ProcessEngine?.Logger?.Log(
                                    message, LogLevel.Error),
                                () => Workspace.Main.RequireRestartAfterMotionConfigurationChange(),
                                FinishDraftEdit,
                                RefreshCardTree,
                                Workspace.IO.RefreshIODgv,
                                Workspace.Main.ResetAxisRuntimeState);
                        }
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

                    if (Workspace.Runtime.Stores.Cards.TryGetControlCard(editKey.CardIndex.Value, out ControlCard controlCard))
                    {
                        Workspace.Inspector.ShowObject(controlCard.cardHead);
                    }

                    Workspace.IO.RefreshIODgv();
                }
                else if(level == 3)
                {
                    editKey.Kind = EditKind.Axis;
                    editKey.CardIndex = treeView1.SelectedNode.Parent.Index;
                    editKey.AxisIndex = treeView1.SelectedNode.Index;

                    if (Workspace.Runtime.Stores.Cards.TryGetAxis(editKey.CardIndex.Value, editKey.AxisIndex.Value, out Axis axis))
                    {
                        Workspace.Inspector.ShowObject(axis);
                    }
                    if (!Workspace.IO.IsDisplayingCard(editKey.CardIndex.Value))
                    {
                        Workspace.IO.RefreshIODgv();
                    }
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
            if (!AuthorizeConfiguration(
                Automation.DeviceSdk.PlatformPermissionCodes.HardwareConfigure,
                "修改控制卡或轴配置"))
            {
                return;
            }
            if (TryGetSelectedAxisIndex(out int cardIndex, out int axisIndex)
                && Workspace.Runtime.Stores.Cards.TryGetAxis(cardIndex, axisIndex, out Axis sourceAxis))
            {
                axisTemp = CloneForEdit(sourceAxis);
                treeView1.Enabled = false;
                treeView2.Enabled = false;
                Workspace.Runtime.Editor.Begin(new EditSession<Axis>("修改轴", axisTemp,
                    draft => ValidateAxisEdit(cardIndex, axisIndex, draft),
                    draft =>
                    {
                        using (BeginMotionConfigurationCommit("修改轴配置"))
                        {
                            if (!TryBuildAxisEditCandidate(
                                    cardIndex,
                                    axisIndex,
                                    draft,
                                    out Card candidateCard,
                                    out string candidateError))
                            {
                                throw new InvalidOperationException(candidateError);
                            }
                            var candidateStore = new CardConfigStore();
                            candidateStore.SetCard(candidateCard);
                            if (!candidateStore.Save(
                                    Workspace.Runtime.Paths.ConfigPath,
                                    true,
                                    out string saveError))
                            {
                                throw new InvalidOperationException(saveError);
                            }
                            // 编辑阶段只保存配置，不调用实体运动卡；参数在下次启动时统一加载并生效。
                            CompleteCommittedMotionConfiguration(
                                Workspace.Runtime.Readiness,
                                () =>
                                {
                                    Workspace.Runtime.Stores.Cards.SetCard(candidateCard);
                                    if (!Workspace.Runtime.Stores.Cards.TryValidateStations(
                                            dataStation ?? new List<DataStation>(),
                                            out List<string> reboundStationErrors))
                                    {
                                        string rebindError = string.Join("；", reboundStationErrors);
                                        Workspace.Runtime.Safety.Lock(
                                            $"轴配置已提交，但工站轴引用重绑定失败:{rebindError}");
                                        throw new InvalidOperationException(rebindError);
                                    }
                                },
                                message => Workspace.Runtime.ProcessEngine?.Logger?.Log(
                                    message, LogLevel.Error),
                                () => Workspace.Main.RequireRestartAfterMotionConfigurationChange(),
                                FinishDraftEdit,
                                RefreshCardTree,
                                Workspace.Main.ResetAxisRuntimeState);
                        }
                    }, FinishDraftEdit));
            }
            else if (TryGetSelectedCardIndex(out cardIndex)
                && Workspace.Runtime.Stores.Cards.TryGetControlCard(cardIndex, out ControlCard sourceCard))
            {
                controlCardTemp = CloneForEdit(sourceCard);
                treeView1.Enabled = false;
                treeView2.Enabled = false;
                Workspace.Runtime.Editor.Begin(new EditSession<CardHead>("修改控制卡", controlCardTemp.cardHead,
                    draft => ValidateCardHeadEdit(cardIndex, draft),
                    draft =>
                    {
                        using (BeginMotionConfigurationCommit("修改控制卡配置"))
                        {
                            if (!TryBuildCardHeadEditCandidates(
                                    cardIndex,
                                    draft,
                                    out Card candidateCard,
                                    out List<List<IO>> candidateIoMap,
                                    out string candidateError))
                            {
                                throw new InvalidOperationException(candidateError);
                            }
                            var settings = new JsonSerializerSettings
                            {
                                TypeNameHandling = TypeNameHandling.All
                            };
                            using (var batch = new ConfigurationBatchWriter(
                                Workspace.Runtime.Paths.ConfigPath))
                            {
                                batch.AddJson("card.json", candidateCard, settings);
                                batch.AddJson("IOMap.json", candidateIoMap, settings);
                                batch.Commit();
                            }

                            CompleteCommittedMotionConfiguration(
                                Workspace.Runtime.Readiness,
                                () =>
                                {
                                    // 双文件事务成功后再替换正式内存，避免卡点数与 IOMap 暂时失配。
                                    Workspace.Runtime.Stores.Cards.SetCard(candidateCard);
                                    if (!Workspace.Runtime.Stores.IoConfiguration.TryReplaceMap(
                                            candidateIoMap,
                                            out string ioReplaceError))
                                    {
                                        Workspace.Runtime.Safety.Lock(
                                            $"控制卡与IO配置已提交，但IO正式内存替换失败:{ioReplaceError}");
                                        throw new InvalidOperationException(ioReplaceError);
                                    }
                                    if (!Workspace.Runtime.Stores.Cards.TryValidateStations(
                                            dataStation ?? new List<DataStation>(),
                                            out List<string> reboundStationErrors))
                                    {
                                        string rebindError = string.Join("；", reboundStationErrors);
                                        Workspace.Runtime.Safety.Lock(
                                            $"控制卡与IO配置已提交，但工站轴引用重绑定失败:{rebindError}");
                                        throw new InvalidOperationException(rebindError);
                                    }
                                },
                                message => Workspace.Runtime.ProcessEngine?.Logger?.Log(
                                    message, LogLevel.Error),
                                () => Workspace.Main.RequireRestartAfterMotionConfigurationChange(),
                                FinishDraftEdit,
                                RefreshCardTree,
                                Workspace.IO.RefreshIODgv,
                                Workspace.Main.ResetAxisRuntimeState);
                        }
                    }, FinishDraftEdit));
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
            if (!AuthorizeConfiguration(
                Automation.DeviceSdk.PlatformPermissionCodes.HardwareConfigure,
                "删除控制卡"))
            {
                return;
            }
            if (editKey.Kind == EditKind.Card && TryGetSelectedCardIndex(out int cardIndex))
            {
                if (MessageBox.Show("确认删除选中的控制卡？", "删除确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                {
                    return;
                }
                using (BeginMotionConfigurationCommit("删除控制卡配置"))
                {
                    if (!TryBuildRemovedCardCandidates(
                            cardIndex,
                            out Card candidateCard,
                            out List<List<IO>> candidateIoMap,
                            out string candidateError))
                    {
                        throw new InvalidOperationException(candidateError);
                    }
                    var settings = new JsonSerializerSettings
                    {
                        TypeNameHandling = TypeNameHandling.All
                    };
                    using (var batch = new ConfigurationBatchWriter(Workspace.Runtime.Paths.ConfigPath))
                    {
                        batch.AddJson("card.json", candidateCard, settings);
                        batch.AddJson("IOMap.json", candidateIoMap, settings);
                        batch.Commit();
                    }

                    CompleteCommittedMotionConfiguration(
                        Workspace.Runtime.Readiness,
                        () =>
                        {
                            Workspace.Runtime.Stores.Cards.SetCard(candidateCard);
                            if (!Workspace.Runtime.Stores.IoConfiguration.TryReplaceMap(
                                    candidateIoMap,
                                    out string ioReplaceError))
                            {
                                Workspace.Runtime.Safety.Lock(
                                    $"控制卡删除已提交，但IO正式内存替换失败:{ioReplaceError}");
                                throw new InvalidOperationException(ioReplaceError);
                            }
                            if (!Workspace.Runtime.Stores.Cards.TryValidateStations(
                                    dataStation ?? new List<DataStation>(),
                                    out List<string> reboundStationErrors))
                            {
                                string rebindError = string.Join("；", reboundStationErrors);
                                Workspace.Runtime.Safety.Lock(
                                    $"控制卡删除已提交，但工站轴引用重绑定失败:{rebindError}");
                                throw new InvalidOperationException(rebindError);
                            }
                        },
                        message => Workspace.Runtime.ProcessEngine?.Logger?.Log(
                            message, LogLevel.Error),
                        () => Workspace.Main.RequireRestartAfterMotionConfigurationChange(),
                        Workspace.Card.RefreshCardTree,
                        Workspace.Main.ResetAxisRuntimeState,
                        () => Workspace.IO.dgvIO.Rows.Clear(),
                        Workspace.IO.RefreshIODgv);
                }
            }
        }

        private void AddStation_Click(object sender, EventArgs e)
        {
            if (!AuthorizeConfiguration(
                Automation.DeviceSdk.PlatformPermissionCodes.MotionConfigure,
                "新增工站"))
            {
                return;
            }
            dataStationTemp = new DataStation(false);
            treeView1.Enabled = false;
            treeView2.Enabled = false;
            Workspace.Runtime.Editor.Begin(new EditSession<DataStation>("新增工站", dataStationTemp,
                draft =>
                {
                    List<DataStation> candidate = CloneForEdit(
                        dataStation ?? new List<DataStation>());
                    candidate.Add(CloneForEdit(draft));
                    return Workspace.Runtime.Stores.Cards.TryValidateStations(candidate, out List<string> errors)
                        ? null : string.Join("\r\n", errors);
                },
                draft =>
                {
                    using (BeginMotionConfigurationCommit("新增工站配置"))
                    {
                        List<DataStation> candidate = CloneForEdit(
                            dataStation ?? new List<DataStation>());
                        candidate.Add(CloneForEdit(draft));
                        if (!Workspace.Runtime.Stores.Cards.TryValidateStations(
                                candidate,
                                out List<string> validationErrors))
                        {
                            throw new InvalidOperationException(
                                string.Join("；", validationErrors));
                        }
                        if (!stationDefinitionStore.TryCommit(
                                Workspace.Runtime.Paths.ConfigPath, candidate, out string error))
                        {
                            throw new InvalidOperationException(error);
                        }
                        CompleteCommittedMotionConfiguration(
                            Workspace.Runtime.Readiness,
                            () =>
                            {
                                if (Workspace.Runtime.ProcessEngine?.Context != null)
                                {
                                    Workspace.Runtime.ProcessEngine.Context.Stations = dataStation;
                                }
                            },
                            message => Workspace.Runtime.ProcessEngine?.Logger?.Log(
                                message, LogLevel.Error),
                            () => Workspace.Main.RequireRestartAfterMotionConfigurationChange(),
                            FinishDraftEdit,
                            RefreshStationTree);
                    }
                }, FinishDraftEdit));
            Workspace.Inspector.RefreshObject();
        }

        private void ModifyStation_Click(object sender, EventArgs e)
        {
            if (!AuthorizeConfiguration(
                Automation.DeviceSdk.PlatformPermissionCodes.MotionConfigure,
                "修改工站"))
            {
                return;
            }
            if (TryGetSelectedStationIndex(out int stationIndex))
            {
                dataStationTemp = CloneForEdit(dataStation[stationIndex]);
                treeView1.Enabled = false;
                treeView2.Enabled = false;
                Workspace.Runtime.Editor.Begin(new EditSession<DataStation>("修改工站", dataStationTemp,
                    draft =>
                    {
                        List<DataStation> candidate = CloneForEdit(dataStation);
                        candidate[stationIndex] = CloneForEdit(draft);
                        return Workspace.Runtime.Stores.Cards.TryValidateStations(candidate, out List<string> errors)
                            ? null : string.Join("\r\n", errors);
                    },
                    draft =>
                    {
                        using (BeginMotionConfigurationCommit("修改工站配置"))
                        {
                            List<DataStation> candidate = CloneForEdit(dataStation);
                            candidate[stationIndex] = CloneForEdit(draft);
                            if (!Workspace.Runtime.Stores.Cards.TryValidateStations(
                                    candidate,
                                    out List<string> validationErrors))
                            {
                                throw new InvalidOperationException(
                                    string.Join("；", validationErrors));
                            }
                            if (!stationDefinitionStore.TryCommit(
                                    Workspace.Runtime.Paths.ConfigPath, candidate, out string error))
                            {
                                throw new InvalidOperationException(error);
                            }
                            CompleteCommittedMotionConfiguration(
                                Workspace.Runtime.Readiness,
                                () =>
                                {
                                    if (Workspace.Runtime.ProcessEngine?.Context != null)
                                    {
                                        Workspace.Runtime.ProcessEngine.Context.Stations = dataStation;
                                    }
                                },
                                message => Workspace.Runtime.ProcessEngine?.Logger?.Log(
                                    message, LogLevel.Error),
                                () => Workspace.Main.RequireRestartAfterMotionConfigurationChange(),
                                FinishDraftEdit,
                                RefreshStationTree);
                        }
                    }, FinishDraftEdit));
                Workspace.Inspector.RefreshObject();
            }
           
           
        }

        private void RemoveStation_Click(object sender, EventArgs e)
        {
            if (!AuthorizeConfiguration(
                Automation.DeviceSdk.PlatformPermissionCodes.MotionConfigure,
                "删除工站"))
            {
                return;
            }
            if (TryGetSelectedStationIndex(out int stationIndex))
            {
                if (MessageBox.Show("确认删除选中的工站？", "删除确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                {
                    return;
                }
                using (BeginMotionConfigurationCommit("删除工站配置"))
                {
                    List<DataStation> candidate = CloneForEdit(dataStation);
                    candidate.RemoveAt(stationIndex);
                    if (!Workspace.Runtime.Stores.Cards.TryValidateStations(
                            candidate,
                            out List<string> validationErrors))
                    {
                        throw new InvalidOperationException(
                            string.Join("；", validationErrors));
                    }
                    if (!stationDefinitionStore.TryCommit(
                            Workspace.Runtime.Paths.ConfigPath, candidate, out string error))
                    {
                        throw new InvalidOperationException(error);
                    }

                    CompleteCommittedMotionConfiguration(
                        Workspace.Runtime.Readiness,
                        () =>
                        {
                            if (Workspace.Runtime.ProcessEngine?.Context != null)
                            {
                                Workspace.Runtime.ProcessEngine.Context.Stations = dataStation;
                            }
                        },
                        message => Workspace.Runtime.ProcessEngine?.Logger?.Log(
                            message, LogLevel.Error),
                        () => Workspace.Main.RequireRestartAfterMotionConfigurationChange(),
                        Workspace.Card.RefreshStationTree);
                }
            }
        }

        private bool AuthorizeConfiguration(string permission, string action)
        {
            if (Workspace.Runtime.Accounts.Authorize(permission, action, out string error))
            {
                return true;
            }
            MessageBox.Show(error, "权限不足", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        private void treeView2_AfterSelect(object sender, TreeViewEventArgs e)
        {
            editKey.StationIndex = treeView2.SelectedNode.Index;
            Workspace.Inspector.ShowObject(dataStation[editKey.StationIndex.Value]);
          
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
            PlatformRuntime runtime = context?.GetService(typeof(PlatformRuntime)) as PlatformRuntime;
            int count = runtime?.Stores.Cards.GetControlCardCount() ?? 0;
            return new StandardValuesCollection(Enumerable.Range(0, count)
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
            var names = new List<string>();
            PlatformRuntime runtime = context?.GetService(typeof(PlatformRuntime)) as PlatformRuntime;
            if (context?.Instance is AxisConfig config
                && int.TryParse(config.CardNum, out int cardNum)
                && runtime?.Stores.Cards.TryGetControlCard(cardNum, out ControlCard controlCard) == true)
            {
                names.AddRange(controlCard.axis.Select(item => item.AxisName));
            }
            return new StandardValuesCollection(names);
        }
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
        {
            return true;
        }
    }
    /// <summary>机器人工站直接引用当前平台已有的 TCP 通讯对象。</summary>
    public class RobotCommunicationItem : StringConverter
    {
        public override bool GetStandardValuesSupported(ITypeDescriptorContext context)
        {
            return true;
        }

        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
        {
            PlatformRuntime runtime = context?.GetService(typeof(PlatformRuntime)) as PlatformRuntime;
            var names = new List<string> { string.Empty };
            if (runtime?.Stores?.Communication != null)
            {
                names.AddRange(runtime.Stores.Communication.GetSocketSnapshot()
                    .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Name))
                    .Select(item => item.Name)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
            }
            return new StandardValuesCollection(names);
        }

        public override bool GetStandardValuesExclusive(ITypeDescriptorContext context)
        {
            return true;
        }
    }
}
