// 模块：编辑器 / 流程。
// 职责范围：流程导航、指令表、对象选择、搜索和编辑动作。
// 排查入口：流程列表显示异常先核对 ProcessSelection 的稳定 ID 与索引，再检查 Repository；不要从其他窗体反读状态。

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static Automation.OperationTypePartial;
using static System.Windows.Forms.AxHost;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Button;

namespace Automation
{
    public partial class FrmProc : Form
    {
        private const int ProcessOutlineImageSize = 18;

        private readonly ProcessDefinitionRepository processDefinitionRepository;
        internal ProcessEditorSelectionState SelectionState { get; } = new ProcessEditorSelectionState();
        public List<Proc> procsList => processDefinitionRepository.Items;
        public int SelectedProcNum
        {
            get => SelectionState.ProcIndex;
            set => SelectionState.SelectProcess(value, GetProcId(value));
        }
        public int SelectedStepNum
        {
            get => SelectionState.StepIndex;
            set => SelectionState.SelectStep(value, GetStepId(SelectedProcNum, value));
        }

        private readonly Dictionary<Guid, int> procIndexMap = new Dictionary<Guid, int>();
        private ImageList procStateImages;
        private Font processRowFont;
        private Font stepRowFont;

        private bool suppressContextMenuOnce = false;
        private Proc clipboardProc;
        private Step clipboardStep;
        private bool restoringOutlineState;

        public event EventHandler UserSelectionChanged;

        public FrmProc()
            : this(new ProcessDefinitionRepository())
        {
        }

        public FrmProc(ProcessDefinitionRepository processDefinitionRepository)
        {
            this.processDefinitionRepository = processDefinitionRepository
                ?? throw new ArgumentNullException(nameof(processDefinitionRepository));
            InitializeComponent();
            ConfigureProcessOutlineAppearance();
            ConfigureProcContextMenu();
            Disposed += FrmProc_Disposed;
            processOutline.UserSelectionChanged += ProcessOutline_UserSelectionChanged;
        }

        private void ConfigureProcContextMenu()
        {
            contextMenuStrip1.SuspendLayout();
            try
            {
                contextMenuStrip1.Items.Clear();
                contextMenuStrip1.AutoSize = true;
                contextMenuStrip1.MinimumSize = Size.Empty;
                contextMenuStrip1.Padding = new Padding(3);
                contextMenuStrip1.ImageScalingSize = new Size(20, 20);
                contextMenuStrip1.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular);
                contextMenuStrip1.BackColor = UiPalette.SurfaceStrong;
                contextMenuStrip1.ForeColor = UiPalette.TextPrimary;
                contextMenuStrip1.ShowCheckMargin = false;
                contextMenuStrip1.ShowImageMargin = true;
                contextMenuStrip1.Renderer = new ProcContextMenuRenderer();

                AddProc.Image = CreateProcMenuIcon(ProcMenuIconKind.AddProc);
                AddStep.Image = CreateProcMenuIcon(ProcMenuIconKind.AddStep);
                startProc.Image = CreateProcMenuIcon(ProcMenuIconKind.Start);
                Modify.Image = CreateProcMenuIcon(ProcMenuIconKind.Edit);
                CopyProcStep.Image = CreateProcMenuIcon(ProcMenuIconKind.Copy);
                PasteProcStep.Image = CreateProcMenuIcon(ProcMenuIconKind.Paste);
                ToggleDisable.Image = CreateProcMenuIcon(ProcMenuIconKind.Disable);
                Remove.Image = CreateProcMenuIcon(ProcMenuIconKind.Delete);
                Remove.ForeColor = UiPalette.Danger;

                foreach (ToolStripMenuItem item in new[]
                {
                    AddProc, AddStep, startProc, Modify, CopyProcStep,
                    PasteProcStep, ToggleDisable, Remove
                })
                {
                    item.AutoSize = true;
                    item.Padding = new Padding(4, 2, 5, 2);
                    item.Margin = Padding.Empty;
                }

                contextMenuStrip1.Items.Add(AddProc);
                contextMenuStrip1.Items.Add(AddStep);
                contextMenuStrip1.Items.Add(CreateProcMenuSeparator());
                contextMenuStrip1.Items.Add(startProc);
                contextMenuStrip1.Items.Add(CreateProcMenuSeparator());
                contextMenuStrip1.Items.Add(Modify);
                contextMenuStrip1.Items.Add(CopyProcStep);
                contextMenuStrip1.Items.Add(PasteProcStep);
                contextMenuStrip1.Items.Add(CreateProcMenuSeparator());
                contextMenuStrip1.Items.Add(ToggleDisable);
                contextMenuStrip1.Items.Add(CreateProcMenuSeparator());
                contextMenuStrip1.Items.Add(Remove);
            }
            finally
            {
                contextMenuStrip1.ResumeLayout(false);
            }
        }

        private static ToolStripSeparator CreateProcMenuSeparator()
        {
            return new ToolStripSeparator
            {
                AutoSize = true,
                Margin = Padding.Empty
            };
        }

        private enum ProcMenuIconKind
        {
            AddProc,
            AddStep,
            Start,
            Edit,
            Copy,
            Paste,
            Disable,
            Delete
        }

        private static Bitmap CreateProcMenuIcon(ProcMenuIconKind kind)
        {
            Bitmap bitmap = new Bitmap(20, 20);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (Pen pen = new Pen(GetProcMenuIconColor(kind), 1.7F))
            {
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                switch (kind)
                {
                    case ProcMenuIconKind.AddProc:
                        graphics.DrawRectangle(pen, 4, 5, 12, 10);
                        graphics.DrawLine(pen, 10, 7, 10, 13);
                        graphics.DrawLine(pen, 7, 10, 13, 10);
                        break;
                    case ProcMenuIconKind.AddStep:
                        graphics.DrawRectangle(pen, 4, 4, 12, 12);
                        graphics.DrawLine(pen, 7, 8, 13, 8);
                        graphics.DrawLine(pen, 7, 12, 11, 12);
                        break;
                    case ProcMenuIconKind.Start:
                        graphics.DrawPolygon(pen, new[] { new Point(7, 5), new Point(15, 10), new Point(7, 15) });
                        break;
                    case ProcMenuIconKind.Edit:
                        graphics.DrawLine(pen, 5, 15, 14, 6);
                        graphics.DrawLine(pen, 5, 15, 8, 14);
                        graphics.DrawLine(pen, 12, 5, 15, 8);
                        break;
                    case ProcMenuIconKind.Copy:
                        graphics.DrawRectangle(pen, 6, 4, 9, 10);
                        graphics.DrawRectangle(pen, 4, 7, 9, 9);
                        break;
                    case ProcMenuIconKind.Paste:
                        graphics.DrawRectangle(pen, 5, 5, 10, 11);
                        graphics.DrawRectangle(pen, 8, 3, 4, 4);
                        break;
                    case ProcMenuIconKind.Disable:
                        graphics.DrawEllipse(pen, 4, 4, 12, 12);
                        graphics.DrawLine(pen, 6, 14, 14, 6);
                        break;
                    case ProcMenuIconKind.Delete:
                        graphics.DrawRectangle(pen, 6, 7, 8, 9);
                        graphics.DrawLine(pen, 5, 6, 15, 6);
                        graphics.DrawLine(pen, 8, 4, 12, 4);
                        break;
                }
            }
            return bitmap;
        }

        private static Color GetProcMenuIconColor(ProcMenuIconKind kind)
        {
            switch (kind)
            {
                case ProcMenuIconKind.Start: return UiPalette.Success;
                case ProcMenuIconKind.Disable: return UiPalette.Transition;
                case ProcMenuIconKind.Delete: return UiPalette.Danger;
                default: return UiPalette.Brand;
            }
        }

        private sealed class ProcContextMenuRenderer : ToolStripProfessionalRenderer
        {
            public ProcContextMenuRenderer() : base(new ProcContextMenuColorTable())
            {
                RoundedEdges = false;
            }

            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                Rectangle bounds = new Rectangle(4, 1, e.Item.Width - 8, e.Item.Height - 2);
                if (e.Item.Selected && e.Item.Enabled)
                {
                    using (Brush brush = new SolidBrush(UiPalette.BrandSoft))
                    {
                        e.Graphics.FillRectangle(brush, bounds);
                    }
                }
            }
        }

        private sealed class ProcContextMenuColorTable : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground => UiPalette.SurfaceStrong;
            public override Color ImageMarginGradientBegin => UiPalette.SurfaceStrong;
            public override Color ImageMarginGradientMiddle => UiPalette.SurfaceStrong;
            public override Color ImageMarginGradientEnd => UiPalette.SurfaceStrong;
            public override Color MenuBorder => UiPalette.StrokeStrong;
            public override Color SeparatorDark => UiPalette.Divider;
            public override Color SeparatorLight => UiPalette.SurfaceStrong;
        }

        private void FrmProc_Disposed(object sender, EventArgs e)
        {
            procStateImages?.Dispose();
            procStateImages = null;
            processOutline.UserSelectionChanged -= ProcessOutline_UserSelectionChanged;
            processRowFont?.Dispose();
            processRowFont = null;
            stepRowFont?.Dispose();
            stepRowFont = null;
        }

        private void ConfigureProcessOutlineAppearance()
        {
            processRowFont = ProcessPageFont.Create(11F, FontStyle.Bold);
            stepRowFont = ProcessPageFont.Create(10.5F, FontStyle.Regular);
            processOutline.Font = processRowFont;
            processOutline.ProcessFont = processRowFont;
            processOutline.StepFont = stepRowFont;
            processOutline.ForeColor = UiPalette.TextPrimary;
            processOutline.BackColor = UiPalette.Background;

            procStateImages = new ImageList
            {
                ColorDepth = ColorDepth.Depth32Bit,
                ImageSize = new Size(ProcessOutlineImageSize, ProcessOutlineImageSize),
                TransparentColor = Color.Transparent
            };
            AddProcStateImage("proc-ready", ProcessOutlineIconKind.Ready);
            AddProcStateImage("proc-stopped", ProcessOutlineIconKind.Stopped);
            AddProcStateImage("proc-running", ProcessOutlineIconKind.Running);
            AddProcStateImage("proc-paused", ProcessOutlineIconKind.Paused);
            AddProcStateImage("proc-single", ProcessOutlineIconKind.SingleStep);
            AddProcStateImage("proc-alarm", ProcessOutlineIconKind.Alarming);
            AddProcStateImage("proc-pausing", ProcessOutlineIconKind.Pausing);
            AddProcStateImage("proc-stopping", ProcessOutlineIconKind.Stopping);
            AddProcStateImage("proc-empty", ProcessOutlineIconKind.EmptyProc);
            AddProcStateImage("proc-empty-disabled", ProcessOutlineIconKind.EmptyProcDisabled);
            AddProcStateImage("disabled", ProcessOutlineIconKind.Disabled);
            AddProcStateImage("step", ProcessOutlineIconKind.Step);
            AddProcStateImage("step-empty", ProcessOutlineIconKind.EmptyStep);
            AddProcStateImage("step-empty-disabled", ProcessOutlineIconKind.EmptyStepDisabled);
            AddProcStateImage("step-running", ProcessOutlineIconKind.StepRunning);
            AddProcStateImage("step-paused", ProcessOutlineIconKind.StepPaused);
            AddProcStateImage("step-single", ProcessOutlineIconKind.StepSingle);
            AddProcStateImage("step-alarm", ProcessOutlineIconKind.StepAlarming);
            processOutline.StateImages = procStateImages;
        }

        private void AddProcStateImage(string key, ProcessOutlineIconKind kind)
        {
            procStateImages.Images.Add(key, ProcessOutlineIconFactory.Create(kind, ProcessOutlineImageSize));
        }

        private static string GetProcImageKey(Proc proc, EngineSnapshot snapshot)
        {
            bool empty = (proc?.steps?.Count ?? 0) == 0;
            if (proc?.head?.Disable == true)
            {
                return empty ? "proc-empty-disabled" : "disabled";
            }
            if (empty)
            {
                return "proc-empty";
            }
            switch (snapshot?.State ?? ProcRunState.Ready)
            {
                case ProcRunState.Ready:
                    return "proc-ready";
                case ProcRunState.Running:
                    return "proc-running";
                case ProcRunState.Paused:
                    return "proc-paused";
                case ProcRunState.SingleStep:
                    return "proc-single";
                case ProcRunState.Alarming:
                    return "proc-alarm";
                case ProcRunState.Pausing:
                    return "proc-pausing";
                case ProcRunState.Stopping:
                    return "proc-stopping";
                case ProcRunState.Stopped:
                    return "proc-stopped";
                default:
                    return "proc-stopped";
            }
        }

        private static string GetStepImageKey(Proc proc, Step step, int stepIndex, EngineSnapshot snapshot)
        {
            bool empty = (step?.Ops?.Count ?? 0) == 0;
            if (proc?.head?.Disable == true || step?.Disable == true)
            {
                return empty ? "step-empty-disabled" : "disabled";
            }
            if (snapshot == null || snapshot.State.IsInactive() || snapshot.StepIndex != stepIndex)
            {
                return empty ? "step-empty" : "step";
            }
            switch (snapshot.State)
            {
                case ProcRunState.Alarming:
                    return "step-alarm";
                case ProcRunState.Paused:
                case ProcRunState.Pausing:
                    return "step-paused";
                case ProcRunState.SingleStep:
                    return "step-single";
                default:
                    return "step-running";
            }
        }

        internal bool UpdateProcessSnapshot(EngineSnapshot snapshot)
        {
            if (snapshot == null || processOutline.IsDisposed)
            {
                return false;
            }
            if (processOutline.InvokeRequired)
            {
                processOutline.BeginInvoke((Action)(() => UpdateProcessSnapshot(snapshot)));
                return false;
            }

            int procIndex;
            if (snapshot.ProcId != Guid.Empty)
            {
                if (!procIndexMap.TryGetValue(snapshot.ProcId, out procIndex))
                {
                    return false;
                }
            }
            else
            {
                procIndex = snapshot.ProcIndex;
            }
            if (procIndex < 0 || procIndex >= procsList.Count)
            {
                return false;
            }

            Proc proc = procsList[procIndex];
            Guid procId = proc?.head?.Id ?? Guid.Empty;
            if (procId == Guid.Empty
                || !processOutline.TryGetProcess(procId, out ProcessOutlineItem procItem))
            {
                return false;
            }

            string processText = BuildProcRowTextWithState(procIndex, proc, snapshot);
            string processImageKey = GetProcImageKey(proc, snapshot);
            Color processTextColor = GetProcessTextColor(proc, snapshot);
            bool displayChanged = procItem.ProcIndex != procIndex
                || !string.Equals(procItem.Text, processText, StringComparison.Ordinal)
                || !string.Equals(procItem.ImageKey, processImageKey, StringComparison.Ordinal)
                || procItem.ForeColor.ToArgb() != processTextColor.ToArgb();
            procItem.ProcIndex = procIndex;
            procItem.Text = processText;
            procItem.ImageKey = processImageKey;
            procItem.ForeColor = processTextColor;

            int stepCount = proc?.steps?.Count ?? 0;
            for (int i = 0; i < stepCount; i++)
            {
                Step step = proc.steps[i];
                if (step?.Id == Guid.Empty
                    || !processOutline.TryGetStep(
                        procId,
                        step.Id,
                        out ProcessOutlineItem stepItem))
                {
                    continue;
                }
                string stepImageKey = GetStepImageKey(proc, step, i, snapshot);
                Color stepTextColor = proc?.head?.Disable == true || step.Disable
                    ? UiPalette.TextMuted
                    : UiPalette.TextPrimary;
                displayChanged |= stepItem.ProcIndex != procIndex
                    || stepItem.StepIndex != i
                    || !string.Equals(
                        stepItem.ImageKey,
                        stepImageKey,
                        StringComparison.Ordinal)
                    || stepItem.ForeColor.ToArgb() != stepTextColor.ToArgb();
                stepItem.ProcIndex = procIndex;
                stepItem.StepIndex = i;
                stepItem.ImageKey = stepImageKey;
                stepItem.ForeColor = stepTextColor;
            }
            if (displayChanged)
            {
                processOutline.InvalidateProcess(procId);
            }
            return displayChanged;
        }

        internal bool TryGetProcIndex(Guid procId, out int procIndex)
        {
            return procIndexMap.TryGetValue(procId, out procIndex);
        }

        private static Color GetProcessTextColor(Proc proc, EngineSnapshot snapshot)
        {
            if (proc?.head?.Disable == true)
            {
                return UiPalette.TextMuted;
            }
            switch (snapshot?.State ?? ProcRunState.Ready)
            {
                case ProcRunState.Running:
                    return UiPalette.Success;
                case ProcRunState.Ready:
                case ProcRunState.Stopped:
                    return UiPalette.TextPrimary;
                case ProcRunState.Paused:
                case ProcRunState.Pausing:
                    return UiPalette.Warning;
                case ProcRunState.SingleStep:
                    return UiPalette.Focus;
                case ProcRunState.Alarming:
                case ProcRunState.Stopping:
                    return UiPalette.Danger;
                default:
                    return UiPalette.TextPrimary;
            }
        }

        private bool TrySaveNewProc(ProcHead head, int selectedProcIndex)
        {
            if (head == null)
            {
                return false;
            }
            // 打开属性编辑器后流程状态仍可能变化，提交前必须重新检查一次流程列表门禁。
            if (!Workspace.Runtime.ProcessEditing.CanEditStructure())
            {
                return false;
            }
            Proc proc = new Proc { head = head };
            int insertIndex;
            if (selectedProcIndex == -1)
            {
                insertIndex = procsList.Count;
            }
            else
            {
                if (selectedProcIndex < 0 || selectedProcIndex >= procsList.Count)
                {
                    return false;
                }
                insertIndex = selectedProcIndex + 1;
            }
            List<string> errors = new List<string>();
            ProcessDefinitionService.NormalizeProc(
                insertIndex, proc, errors, Workspace.Runtime.CreateProcessValidationContext());
            if (errors.Count > 0)
            {
                MessageBox.Show(string.Join("\r\n", errors.Distinct()));
                return false;
            }
            List<Proc> draftProcesses = procsList.Select(ObjectGraphCloner.Clone).ToList();
            draftProcesses.Insert(insertIndex, proc);
            Dictionary<string, DicValue> draftVariables = Workspace.Runtime.Stores.Values.BuildSaveData();
            if (!Workspace.Runtime.ProcessVariableConfiguration.TryCommit(
                draftProcesses,
                draftVariables,
                "新增流程",
                out string commitError))
            {
                MessageBox.Show(commitError, "新增流程失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            return true;
        }
       
        private bool TrySaveNewStep(Step step, int procIndex, int selectedStepIndex)
        {
            if (procIndex < 0 || procIndex >= procsList.Count || step == null)
            {
                return false;
            }
            Step stepToInsert = ObjectGraphCloner.Clone(step);
            if (stepToInsert.Id == Guid.Empty)
            {
                stepToInsert.Id = Guid.NewGuid();
            }
            Proc before = ObjectGraphCloner.Clone(procsList[procIndex]);
            Proc draft = ObjectGraphCloner.Clone(procsList[procIndex]);
            int insertIndex;
            if (selectedStepIndex == -1)
            {
                insertIndex = draft.steps.Count;
            }
            else
            {
                if (selectedStepIndex < 0 || selectedStepIndex >= draft.steps.Count)
                {
                    return false;
                }
                insertIndex = selectedStepIndex + 1;
            }
            draft.steps.Insert(insertIndex, stepToInsert);
            ProcessEditingService.RewriteGotoTargets(before, draft, procIndex);
            if (!ProcessEditingService.TryCommitProcDraft(Workspace.Runtime,
                procIndex, draft, out string commitError, "新增步骤"))
            {
                MessageBox.Show(commitError, "新增步骤失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            return true;
        }
        public bool RebuildWorkConfig(int StartIndex = 0)
        {
            bool success = ProcessWorkDirectoryTransaction.Rebuild(Workspace.Runtime.Paths.WorkPath, procsList, StartIndex,
                out string error, out bool rollbackFailed);
            if (!success)
            {
                RefreshProcList();
                if (rollbackFailed)
                {
                    Workspace.Runtime.Safety.Lock(error);
                }
                MessageBox.Show(error, "流程配置提交失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            RefreshProcList();
            if (!Workspace.Runtime.Editor.History.IsReplaying)
            {
                Workspace.Runtime.Editor.History.Clear();
            }
            return true;
        }
        public void RefreshProcList()
        {
            RefreshProcList(true);
        }

        internal void RefreshProcListFromStore()
        {
            RefreshProcList(false);
        }

        private void RefreshProcList(bool reloadConfiguration)
        {
            ProcessOutlineItem currentSelectedItem = processOutline.SelectedOutlineItem;
            bool synchronizeRestoredSelection = false;
            Guid selectedProcId = currentSelectedItem?.ProcId ?? Guid.Empty;
            Guid selectedStepId = currentSelectedItem?.StepId ?? Guid.Empty;
            if (selectedProcId == Guid.Empty)
            {
                selectedProcId = GetProcId(SelectedProcNum);
                selectedStepId = GetStepId(SelectedProcNum, SelectedStepNum);
            }
            ProcessOutlineScrollAnchor scrollAnchor =
                processOutline.ScrollAnchor;
            var expandedProcIds = new HashSet<Guid>(
                processOutline.ExpandedProcIds);

            List<Proc> procsListTemp;
            List<string> loadErrors;

            restoringOutlineState = true;
            try
            {
                Workspace.Runtime.Readiness.ProcConfigFaulted = false;

                string recoveryMessage = null;
                if (reloadConfiguration)
                {
                    procsListTemp = ProcessWorkDirectoryTransaction.Load(
                        Workspace.Runtime.Paths.WorkPath,
                        Workspace.Runtime.CreateProcessValidationContext(),
                        out loadErrors,
                        out recoveryMessage);
                }
                else
                {
                    procsListTemp = processDefinitionRepository.Items.ToList();
                    loadErrors = new List<string>();
                }
                if (!string.IsNullOrWhiteSpace(recoveryMessage)
                    && Workspace.Info != null
                    && !Workspace.Info.IsDisposed)
                {
                    Workspace.Info.PrintInfo(
                        recoveryMessage,
                        FrmInfo.Level.Error);
                }
                if (reloadConfiguration)
                {
                    processDefinitionRepository.ReplaceAll(procsListTemp);
                }
                processOutline.ReplaceItems(
                    BuildOutlineItems(procsListTemp),
                    expandedProcIds,
                    selectedProcId,
                    selectedStepId,
                    scrollAnchor);
                editorWorkspace?.DataGrid?.dataGridView1
                    ?.RebuildJumpLinkCaches(procsList);
                synchronizeRestoredSelection = processOutline.HasSelection;
                if (!synchronizeRestoredSelection)
                {
                    SelectedProcNum = -1;
                    SelectedStepNum = -1;
                    RefreshCurrentBinding();
                }
            }
            finally
            {
                restoringOutlineState = false;
            }
            if (reloadConfiguration && Workspace.Runtime.ProcessEngine?.Context != null)
            {
                bool allStopped = true;
                int runtimeCount = Workspace.Runtime.ProcessEngine.Context.Procs?.Count ?? 0;
                for (int i = 0; i < runtimeCount; i++)
                {
                    EngineSnapshot snapshot = Workspace.Runtime.ProcessEngine.GetSnapshot(i);
                    if (snapshot != null && !snapshot.State.IsInactive())
                    {
                        allStopped = false;
                        break;
                    }
                }
                if (allStopped)
                {
                    List<Proc> runtimeProcs = new List<Proc>(procsList.Count);
                    for (int i = 0; i < procsList.Count; i++)
                    {
                        runtimeProcs.Add(ObjectGraphCloner.Clone(procsList[i]));
                    }
                    Workspace.Runtime.ProcessEngine.Context.Procs = runtimeProcs;
                    Workspace.Runtime.ProcessEngine.ClearPendingProcUpdates();
                }
            }
            if (synchronizeRestoredSelection)
            {
                SynchronizeRetainedSelection();
            }
            if (loadErrors.Count > 0)
            {
                Workspace.Runtime.Readiness.ProcConfigFaulted = true;
                string reason = "流程配置加载失败，所有流程已停止且禁止启动。请处理以下报警：\r\n"
                    + string.Join("\r\n", loadErrors.Distinct());
                Workspace.Runtime.Safety.StopAllProcesses(reason);
                MessageBox.Show(reason, "流程配置错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            Workspace.Main?.RefreshProcessFlowGraph();
        }

        /// <summary>
        /// 流程内容修改后的局部界面刷新。不会重新读取全部流程文件，也不会清空流程导航。
        /// </summary>
        public void RefreshProcView(int procIndex)
        {
            if (InvokeRequired)
            {
                BeginInvoke((Action)(() => RefreshProcView(procIndex)));
                return;
            }
            if (procIndex < 0 || procIndex >= procsList.Count || processOutline.IsDisposed)
            {
                return;
            }

            Proc proc = procsList[procIndex];
            ProcessOutlineItem selectedItem = processOutline.SelectedOutlineItem;
            Guid selectedProcId = selectedItem?.ProcId ?? GetProcId(SelectedProcNum);
            Guid selectedStepId = selectedItem?.StepId
                ?? GetStepId(SelectedProcNum, SelectedStepNum);
            ProcessOutlineScrollAnchor scrollAnchor =
                processOutline.ScrollAnchor;
            var expandedProcIds = new HashSet<Guid>(
                processOutline.ExpandedProcIds);
            restoringOutlineState = true;
            try
            {
                processOutline.ReplaceItems(
                    BuildOutlineItems(procsList),
                    expandedProcIds,
                    selectedProcId,
                    selectedStepId,
                    scrollAnchor);
            }
            finally
            {
                restoringOutlineState = false;
            }

            editorWorkspace?.DataGrid?.dataGridView1?.RebuildJumpLinkCache(procIndex, proc);
            SynchronizeRetainedSelection();
            Workspace.Main?.RefreshProcessFlowGraph();
        }

        private IReadOnlyList<ProcessOutlineItem> BuildOutlineItems(
            IReadOnlyList<Proc> processes)
        {
            var items = new List<ProcessOutlineItem>();
            procIndexMap.Clear();
            for (int procIndex = 0; procIndex < (processes?.Count ?? 0); procIndex++)
            {
                Proc proc = processes[procIndex];
                Guid procId = proc?.head?.Id ?? Guid.Empty;
                if (procId == Guid.Empty)
                {
                    continue;
                }
                EngineSnapshot snapshot = Workspace.Runtime.ProcessEngine?.GetSnapshot(procIndex);
                items.Add(new ProcessOutlineItem
                {
                    ProcId = procId,
                    StepId = Guid.Empty,
                    ProcIndex = procIndex,
                    StepIndex = -1,
                    Text = snapshot == null
                        ? BuildProcRowText(procIndex, proc)
                        : BuildProcRowTextWithState(procIndex, proc, snapshot),
                    ImageKey = GetProcImageKey(proc, snapshot),
                    ForeColor = snapshot == null
                        ? proc?.head?.Disable == true
                            ? UiPalette.TextMuted
                            : UiPalette.TextPrimary
                        : GetProcessTextColor(proc, snapshot),
                    HasChildren = (proc?.steps?.Count ?? 0) > 0
                });
                procIndexMap[procId] = procIndex;

                IReadOnlyList<Step> steps = proc?.steps
                    ?? (IReadOnlyList<Step>)Array.Empty<Step>();
                for (int stepIndex = 0; stepIndex < steps.Count; stepIndex++)
                {
                    Step step = steps[stepIndex];
                    Guid stepId = step?.Id ?? Guid.Empty;
                    if (stepId == Guid.Empty)
                    {
                        continue;
                    }
                    items.Add(new ProcessOutlineItem
                    {
                        ProcId = procId,
                        StepId = stepId,
                        ProcIndex = procIndex,
                        StepIndex = stepIndex,
                        Text = BuildStepRowText(procIndex, stepIndex, step),
                        ImageKey = GetStepImageKey(
                            proc,
                            step,
                            stepIndex,
                            snapshot),
                        ForeColor = proc?.head?.Disable == true || step.Disable
                            ? UiPalette.TextMuted
                            : UiPalette.TextPrimary,
                        HasChildren = false
                    });
                }
            }
            return items;
        }

        /// <summary>
        /// 全量刷新结束后按稳定 ID 的最终选中行同步索引并重新绑定正式对象。
        /// 列表重建期间不会发出选择事件，因此在最终数据源确定后集中同步一次。
        /// </summary>
        private void SynchronizeRetainedSelection()
        {
            ProcessOutlineItem selectedItem = processOutline.SelectedOutlineItem;
            if (selectedItem == null)
            {
                SelectedProcNum = -1;
                SelectedStepNum = -1;
                RefreshCurrentBinding();
                ApplySelectedProcessRunState();
                return;
            }

            int procIndex = selectedItem.ProcIndex;
            int stepIndex = selectedItem.StepIndex;
            if (procIndex < 0 || procIndex >= procsList.Count
                || stepIndex >= (procsList[procIndex]?.steps?.Count ?? 0))
            {
                SelectedProcNum = -1;
                SelectedStepNum = -1;
                RefreshCurrentBinding();
                ApplySelectedProcessRunState();
                return;
            }

            SelectedProcNum = procIndex;
            SelectedStepNum = stepIndex;
            RefreshCurrentBinding();
            ApplySelectedProcessRunState();
        }

        private void ApplySelectedProcessRunState()
        {
            EngineSnapshot snapshot = Workspace.Runtime.ProcessEngine != null
                && SelectedProcNum >= 0
                ? Workspace.Runtime.ProcessEngine.GetSnapshot(SelectedProcNum)
                : null;
            Workspace.ToolBar?.ApplyProcessRunState(
                snapshot?.State ?? ProcRunState.Ready);
        }

        private Guid GetProcId(int procIndex)
        {
            return procIndex >= 0 && procIndex < procsList.Count
                ? procsList[procIndex]?.head?.Id ?? Guid.Empty
                : Guid.Empty;
        }

        private Guid GetStepId(int procIndex, int stepIndex)
        {
            return procIndex >= 0 && procIndex < procsList.Count
                && stepIndex >= 0 && stepIndex < (procsList[procIndex]?.steps?.Count ?? 0)
                ? procsList[procIndex].steps[stepIndex]?.Id ?? Guid.Empty
                : Guid.Empty;
        }

        /// <summary>
        /// AI 改动流程后，重新绑定当前选中流程/步骤的数据到 FrmDataGrid，
        /// 使指令列表立即反映最新内容，无需用户手动重新选中节点。
        /// </summary>
        public void RefreshCurrentBinding()
        {
            if (IsDisposed || Disposing || !IsHandleCreated)
            {
                return;
            }
            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke((Action)RefreshCurrentBinding);
                }
                catch (InvalidOperationException)
                {
                }
                return;
            }
            InstructionListView grid = Workspace.DataGrid?.dataGridView1;
            FrmInspector inspector = Workspace.Inspector;
            inspector?.BeginUpdate();
            try
            {
                OperationType selectedOp = grid?.GetOperation(grid.CurrentIndex);
                Guid selectedOpId = selectedOp?.Id ?? Guid.Empty;
                int firstVisibleRow = -1;
                if (grid != null && grid.OperationCount > 0)
                {
                    try
                    {
                        firstVisibleRow = grid.FirstVisibleIndex;
                    }
                    catch (InvalidOperationException)
                    {
                    }
                }
                if (SelectedProcNum < 0 || SelectedProcNum >= procsList.Count)
                {
                    bindingSource.DataSource = null;
                    if (grid != null)
                    {
                        grid.SetFlowContext(-1, -1, null);
                    }
                    return;
                }
                object fallbackInspectorObject;
                bool bindingDataSourceChanged;
                if (SelectedStepNum >= 0 && SelectedStepNum < procsList[SelectedProcNum].steps.Count)
                {
                    Step selectedStep = procsList[SelectedProcNum].steps[SelectedStepNum];
                    grid?.SetFlowContext(SelectedProcNum, SelectedStepNum, procsList[SelectedProcNum]);
                    bindingDataSourceChanged = !ReferenceEquals(bindingSource.DataSource, selectedStep.Ops);
                    bindingSource.DataSource = selectedStep.Ops;
                    fallbackInspectorObject = selectedStep;
                }
                else
                {
                    grid?.SetFlowContext(SelectedProcNum, -1, procsList[SelectedProcNum]);
                    bindingDataSourceChanged = bindingSource.DataSource != null;
                    bindingSource.DataSource = null;
                    fallbackInspectorObject = procsList[SelectedProcNum].head;
                }
                if (!bindingDataSourceChanged)
                {
                    bindingSource.ResetBindings(false);
                }
                bool restoredOperation = false;
                if (grid != null && !grid.IsDisposed)
                {
                    // DataSource 属性内部还会核对 BindingSource.List 的真实引用。
                    // 即使 BindingSource 对象未变，也必须经过该入口修复漏掉换源通知后的旧列表。
                    grid.DataSource = bindingSource;
                    if (selectedOpId != Guid.Empty)
                    {
                        int selectedRowIndex = Enumerable.Range(0, grid.OperationCount)
                            .FirstOrDefault(index => grid.GetOperation(index)?.Id == selectedOpId);
                        if (selectedRowIndex >= 0 && selectedRowIndex < grid.OperationCount
                            && grid.GetOperation(selectedRowIndex)?.Id == selectedOpId)
                        {
                            if (grid.CurrentIndex != selectedRowIndex)
                            {
                                grid.SelectSingle(selectedRowIndex);
                            }
                            Workspace.DataGrid.iSelectedRow = selectedRowIndex;
                            Workspace.DataGrid.ShowOperationProperties(selectedRowIndex, false);
                            restoredOperation = true;
                        }
                    }
                    if (firstVisibleRow >= 0 && firstVisibleRow < grid.OperationCount)
                    {
                        try
                        {
                            grid.SetFirstVisibleIndex(firstVisibleRow);
                        }
                        catch (InvalidOperationException)
                        {
                        }
                    }
                }
                if (!restoredOperation && inspector != null && !inspector.IsDisposed)
                {
                    inspector.ShowObject(fallbackInspectorObject);
                }
            }
            finally
            {
                inspector?.EndUpdate();
            }
        }

        private void AddProc_Click(object sender, EventArgs e)
        {   
            if (!Workspace.Runtime.ProcessEditing.CanEditStructure())
            {
                return;
            }
            int selectedProcIndex = SelectedProcNum;
            var draftHead = new ProcHead();

            processOutline.Enabled = false;
            Workspace.Runtime.Editor.Begin(new EditSession<ProcHead>("新增流程", draftHead,
                draft => string.IsNullOrWhiteSpace(draft.Name) ? "流程名称为空。" : null,
                draft =>
                {
                    if (!TrySaveNewProc(draft, selectedProcIndex))
                    {
                        throw new InvalidOperationException("新增流程保存失败。");
                    }
                    processOutline.Enabled = true;
                },
                () => processOutline.Enabled = true));
        }

        private void AddStep_Click(object sender, EventArgs e)
        {
            if (processOutline.HasSelection)
            {
                if (!Workspace.Runtime.ProcessEditing.CanEditProcess(Workspace.Proc.SelectedProcNum))
                {
                    return;
                }
                int procIndex = SelectedProcNum;
                int selectedStepIndex = SelectedStepNum;
                var draftStep = new Step();
                processOutline.Enabled = false;
                Workspace.Runtime.Editor.Begin(new EditSession<Step>("新增步骤", draftStep,
                    draft => string.IsNullOrWhiteSpace(draft.Name) ? "步骤名称为空。" : null,
                    draft =>
                    {
                        if (SelectedProcNum != procIndex)
                        {
                            throw new InvalidOperationException("流程选择已变化，拒绝保存步骤。");
                        }
                        if (!TrySaveNewStep(draft, procIndex, selectedStepIndex))
                        {
                            throw new InvalidOperationException("新增步骤保存失败。");
                        }
                        processOutline.Enabled = true;
                    },
                    () => processOutline.Enabled = true));
            }
            else
            {
                MessageBox.Show("请选择需要添加子节点");
            }

        }

        private void Remove_Click(object sender, EventArgs e)
        {
            if (!processOutline.HasSelection)
            {
                MessageBox.Show("请选择需要删除的流程或步骤");
                return;
            }
            if (SelectedStepNum < 0)
            {
                if (!Workspace.Runtime.ProcessEditing.CanEditStructure())
                {
                    return;
                }
                int procIndex = SelectedProcNum;
                if (procIndex < 0 || procIndex >= procsList.Count)
                {
                    return;
                }
                string procName = procsList[procIndex]?.head?.Name;
                if (string.IsNullOrWhiteSpace(procName))
                {
                    procName = $"索引{procIndex}";
                }
                Guid procId = procsList[procIndex]?.head?.Id ?? Guid.Empty;
                List<DicValue> ownedVariables = Workspace.Runtime.Stores.Values?.GetValuesSnapshot()
                    .Where(value => value != null
                        && ValueConfigStore.IsProcessValue(value)
                        && value.OwnerProcId == procId)
                    .ToList() ?? new List<DicValue>();
                string ownedVariableText = ownedVariables.Count == 0
                    ? string.Empty
                    : $"\r\n\r\n该流程拥有{ownedVariables.Count}个私有变量：\r\n"
                        + string.Join("、", ownedVariables.Select(value => value.Name))
                        + "\r\n\r\n取消勾选时，这些变量将保留并转为公共变量。";
                bool confirmed = Message.ShowConfirmationWithOption(
                    Workspace.Runtime,
                    "删除流程",
                    $"确定删除流程【{procName}】？{ownedVariableText}",
                    $"删除该流程拥有的私有变量（{ownedVariables.Count}个）",
                    true,
                    "删除",
                    "取消",
                    out bool deleteOwnedVariables);
                if (!confirmed)
                {
                    return;
                }
                if (procIndex < 0 || procIndex >= procsList.Count)
                {
                    return;
                }
                List<Proc> draftProcesses = procsList.Select(ObjectGraphCloner.Clone).ToList();
                Guid deletedProcId = draftProcesses[procIndex]?.head?.Id ?? Guid.Empty;
                draftProcesses.RemoveAt(procIndex);
                Dictionary<string, DicValue> draftVariables = Workspace.Runtime.Stores.Values.BuildSaveData();
                if (deleteOwnedVariables)
                {
                    ProcessVariableLifecycleService.RemoveOwnedVariables(
                        draftVariables, new[] { deletedProcId });
                }
                else
                {
                    ProcessVariableLifecycleService.ConvertOwnedVariablesToPublic(
                        draftVariables, new[] { deletedProcId });
                }
                string historyDescription = deleteOwnedVariables
                    ? "删除流程并删除私有变量"
                    : "删除流程并将私有变量转为公共变量";
                if (!Workspace.Runtime.ProcessVariableConfiguration.TryCommit(
                    draftProcesses, draftVariables, historyDescription, out string deleteError))
                {
                    MessageBox.Show(deleteError, "删除流程失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }
            else
            {
                if (!Workspace.Runtime.ProcessEditing.CanEditProcess(SelectedProcNum))
                {
                    return;
                }
                int procIndex = SelectedProcNum;
                int stepIndex = SelectedStepNum;
                if (procIndex < 0 || procIndex >= procsList.Count)
                {
                    return;
                }
                if (stepIndex < 0 || stepIndex >= procsList[procIndex].steps.Count)
                {
                    return;
                }
                string procName = procsList[procIndex]?.head?.Name;
                if (string.IsNullOrWhiteSpace(procName))
                {
                    procName = $"索引{procIndex}";
                }
                string stepName = procsList[procIndex].steps[stepIndex]?.Name;
                if (string.IsNullOrWhiteSpace(stepName))
                {
                    stepName = $"索引{stepIndex}";
                }
                bool confirmed = false;
                new Message(Workspace.Runtime,
                    "删除步骤",
                    $"确定删除步骤【{stepName}】？\r\n所属流程：【{procName}】",
                    () => confirmed = true,
                    null,
                    "删除",
                    "取消",
                    true);
                if (!confirmed)
                {
                    return;
                }
                if (procIndex < 0 || procIndex >= procsList.Count
                    || stepIndex < 0 || stepIndex >= procsList[procIndex].steps.Count)
                {
                    return;
                }
                Proc before = ObjectGraphCloner.Clone(procsList[procIndex]);
                Proc draft = ObjectGraphCloner.Clone(procsList[procIndex]);
                draft.steps.RemoveAt(stepIndex);
                ProcessEditingService.RewriteGotoTargets(before, draft, procIndex);
                if (!ProcessEditingService.TryCommitProcDraft(Workspace.Runtime,
                    procIndex, draft, out string commitError, "删除步骤"))
                {
                    MessageBox.Show(commitError, "删除步骤失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
     
    
        public BindingSource bindingSource = new BindingSource();

        private void ProcessOutline_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (restoringOutlineState)
            {
                return;
            }
            ProcessOutlineItem item = processOutline.SelectedOutlineItem;
            bool selectionChanged = item == null
                ? SelectedProcNum != -1 || SelectedStepNum != -1
                : item.ProcIndex != SelectedProcNum || item.StepIndex != SelectedStepNum;
            if (selectionChanged
                && (Workspace.Runtime.Editor.IsAddingOperations
                    || Workspace.Runtime.Editor.ModifyKind == ModifyKind.Operation))
            {
                if (Control.MouseButtons == MouseButtons.Right)
                {
                    suppressContextMenuOnce = true;
                }
                restoringOutlineState = true;
                try
                {
                    Guid procId = GetProcId(SelectedProcNum);
                    Guid stepId = GetStepId(SelectedProcNum, SelectedStepNum);
                    if (procId == Guid.Empty)
                    {
                        processOutline.ClearSelection();
                    }
                    else
                    {
                        processOutline.SelectIdentity(procId, stepId, true);
                    }
                }
                finally
                {
                    restoringOutlineState = false;
                }
                if (Workspace.Info != null && !Workspace.Info.IsDisposed)
                {
                    Workspace.Info.PrintInfo(
                        "新增或编辑指令中，禁止切换流程。",
                        FrmInfo.Level.Error);
                }
                else
                {
                    MessageBox.Show("新增或编辑指令中，禁止切换流程。");
                }
                return;
            }

            if (item == null)
            {
                SelectedProcNum = -1;
                SelectedStepNum = -1;
                Workspace.DataGrid.iSelectedRow = -1;
                Workspace.DataGrid.OperationTemp = null;
                RefreshCurrentBinding();
                ApplySelectedProcessRunState();
                return;
            }

            if (item.ProcIndex < 0 || item.ProcIndex >= procsList.Count
                || item.StepIndex >= (procsList[item.ProcIndex]?.steps?.Count ?? 0))
            {
                restoringOutlineState = true;
                processOutline.ClearSelection();
                restoringOutlineState = false;
                SelectedProcNum = -1;
                SelectedStepNum = -1;
                RefreshCurrentBinding();
                ApplySelectedProcessRunState();
                return;
            }

            SelectedProcNum = item.ProcIndex;
            SelectedStepNum = item.StepIndex;
            Workspace.DataGrid.iSelectedRow = -1;
            RefreshCurrentBinding();
            ApplySelectedProcessRunState();
        }

        private void ProcessOutline_UserSelectionChanged(object sender, EventArgs e)
        {
            suppressContextMenuOnce = false;
            UserSelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        private void Modify_Click(object sender, EventArgs e)
        {
            if (SelectedProcNum < 0)
            {
                MessageBox.Show("请选择流程或步骤");
                return;
            }
            if (!Workspace.Runtime.ProcessEditing.CanEditProcess(Workspace.Proc.SelectedProcNum))
            {
                return;
            }
            int procIndex = SelectedProcNum;
            int stepIndex = SelectedStepNum;
            object selected = Workspace.Inspector.SelectedObject;
            processOutline.Enabled = false;
            if (selected is ProcHead sourceHead)
            {
                ProcHead draft = ObjectGraphCloner.Clone(sourceHead);
                Workspace.Runtime.Editor.Begin(new EditSession<ProcHead>("修改流程", draft,
                    item => string.IsNullOrWhiteSpace(item.Name) ? "流程名称为空。" : null,
                    item =>
                    {
                        Proc procDraft = ObjectGraphCloner.Clone(procsList[procIndex]);
                        procDraft.head = item;
                        if (!ProcessEditingService.TryCommitProcDraft(Workspace.Runtime,
                            procIndex, procDraft, out string commitError, "修改流程"))
                        {
                            throw new InvalidOperationException(commitError);
                        }
                        processOutline.Enabled = true;
                    }, () => processOutline.Enabled = true));
            }
            else if (selected is Step sourceStep && stepIndex >= 0)
            {
                Step draft = ObjectGraphCloner.Clone(sourceStep);
                Workspace.Runtime.Editor.Begin(new EditSession<Step>("修改步骤", draft,
                    item => string.IsNullOrWhiteSpace(item.Name) ? "步骤名称为空。" : null,
                    item =>
                    {
                        Proc procDraft = ObjectGraphCloner.Clone(procsList[procIndex]);
                        procDraft.steps[stepIndex] = item;
                        if (!ProcessEditingService.TryCommitProcDraft(Workspace.Runtime,
                            procIndex, procDraft, out string commitError, "修改步骤"))
                        {
                            throw new InvalidOperationException(commitError);
                        }
                        processOutline.Enabled = true;
                    }, () => processOutline.Enabled = true));
            }
            else
            {
                processOutline.Enabled = true;
                MessageBox.Show("当前流程编辑对象无效。");
            }
        }

        private void startProc_Click(object sender, EventArgs e)
        {
            if (SelectedProcNum != -1)
            {
                if (SelectedProcNum >= 0 && SelectedProcNum < procsList.Count && procsList[SelectedProcNum]?.head?.Disable == true)
                {
                    MessageBox.Show("流程已禁用，无法启动。");
                    return;
                }
                int procIndex = SelectedProcNum;
                if (!Workspace.Runtime.ProcessEngine.TryValidateProcessInactive(procIndex, out string stateError))
                {
                    MessageBox.Show(stateError, "流程尚未结束", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                string procName = procsList[procIndex]?.head?.Name;
                if (string.IsNullOrWhiteSpace(procName))
                {
                    procName = "未命名";
                }
                string message = $"确认启动流程：{procIndex}-{procName}";
                Message confirmForm = new Message(Workspace.Runtime, "启动确认", message,
                    () =>
                    {
                        if (Workspace.Runtime.ProcessEngine.StartProc(null, procIndex))
                        {
                            return;
                        }
                        string error = Workspace.Runtime.ProcessEngine.TryValidateProcessInactive(procIndex, out string inactiveError)
                            ? "流程启动请求未被内核接受，请查看流程日志。"
                            : inactiveError;
                        MessageBox.Show(error, "流程启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    },
                    () => { },
                    "启动", "取消", false);
                confirmForm.txtMsg.Font = new Font("微软雅黑", 20F, FontStyle.Bold);
                confirmForm.txtMsg.ForeColor = UiPalette.Danger;
            }
        }

        private void contextMenuStrip1_Opening(object sender, CancelEventArgs e)
        {
            if (suppressContextMenuOnce)
            {
                suppressContextMenuOnce = false;
                e.Cancel = true;
                return;
            }
            UpdateToggleDisableMenu();
            UpdateCopyPasteMenu();
        }

        private void UpdateToggleDisableMenu()
        {
            if (ToggleDisable == null)
            {
                return;
            }
            if (!processOutline.HasSelection)
            {
                ToggleDisable.Enabled = false;
                ToggleDisable.Text = "禁用";
                return;
            }
            bool isStep = SelectedStepNum >= 0;
            int procIndex = SelectedProcNum;
            int stepIndex = SelectedStepNum;
            bool isDisabled = false;
            if (procIndex >= 0 && procIndex < procsList.Count)
            {
                if (isStep)
                {
                    if (stepIndex >= 0 && stepIndex < procsList[procIndex].steps.Count)
                    {
                        isDisabled = procsList[procIndex].steps[stepIndex]?.Disable == true;
                    }
                }
                else
                {
                    isDisabled = procsList[procIndex]?.head?.Disable == true;
                }
            }
            ToggleDisable.Text = isDisabled
                ? (isStep ? "启用步骤" : "启用流程")
                : (isStep ? "禁用步骤" : "禁用流程");
            ToggleDisable.Enabled = true;
        }

        private void UpdateCopyPasteMenu()
        {
            if (CopyProcStep != null)
            {
                CopyProcStep.Enabled = processOutline.HasSelection;
            }
            if (PasteProcStep != null)
            {
                PasteProcStep.Enabled = clipboardProc != null || clipboardStep != null;
            }
        }

        private void CopyProcStep_Click(object sender, EventArgs e)
        {
            if (!processOutline.HasSelection)
            {
                MessageBox.Show("请选择需要复制的流程或步骤。");
                return;
            }
            if (SelectedStepNum < 0)
            {
                int procIndex = SelectedProcNum;
                if (procIndex < 0 || procIndex >= procsList.Count)
                {
                    MessageBox.Show("流程索引无效，无法复制。");
                    return;
                }
                clipboardProc = ObjectGraphCloner.Clone(procsList[procIndex]);
                clipboardStep = null;
            }
            else
            {
                int procIndex = SelectedProcNum;
                int stepIndex = SelectedStepNum;
                if (procIndex < 0 || procIndex >= procsList.Count)
                {
                    MessageBox.Show("流程索引无效，无法复制步骤。");
                    return;
                }
                if (stepIndex < 0 || stepIndex >= procsList[procIndex].steps.Count)
                {
                    MessageBox.Show("步骤索引无效，无法复制。");
                    return;
                }
                clipboardStep = ObjectGraphCloner.Clone(procsList[procIndex].steps[stepIndex]);
                clipboardProc = null;
            }
            UpdateCopyPasteMenu();
        }

        private void PasteProcStep_Click(object sender, EventArgs e)
        {
            if (clipboardProc != null)
            {
                PasteProc();
                return;
            }
            if (clipboardStep != null)
            {
                PasteStep();
                return;
            }
            MessageBox.Show("剪贴板为空，无法粘贴。");
        }

        private void PasteProc()
        {
            if (!Workspace.Runtime.ProcessEditing.CanEditStructure())
            {
                return;
            }
            Proc newProc = ObjectGraphCloner.Clone(clipboardProc);
            ResetProcIdentity(newProc);
            UpdateCopiedProcName(newProc);
            int insertIndex = SelectedProcNum >= 0 ? SelectedProcNum + 1 : procsList.Count;
            if (insertIndex < 0 || insertIndex > procsList.Count)
            {
                insertIndex = procsList.Count;
            }
            ProcessEditingService.AdaptGotoProcIndex(newProc, insertIndex);
            Dictionary<string, DicValue> draftVariables = Workspace.Runtime.Stores.Values.BuildSaveData();
            ProcessVariableCopyResult variableCopy;
            try
            {
                variableCopy = ProcessVariableLifecycleService.CopyPrivateVariables(
                    clipboardProc.head.Id, newProc.head.Id, newProc, draftVariables);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "复制流程失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            List<string> errors = new List<string>();
            ProcessDefinitionService.NormalizeProc(
                insertIndex, newProc, errors, Workspace.Runtime.CreateProcessValidationContext());
            if (errors.Count > 0)
            {
                MessageBox.Show(string.Join("\r\n", errors.Distinct()));
                return;
            }
            if (variableCopy.Mappings.Count > 0)
            {
                string mappingText = string.Join("\r\n", variableCopy.Mappings.Select(mapping =>
                    $"{mapping.SourceName}[{mapping.SourceIndex}] → {mapping.Name}[{mapping.Index}]"));
                string warningText = variableCopy.Warnings.Count == 0
                    ? string.Empty
                    : "\r\n\r\n警告：\r\n" + string.Join("\r\n", variableCopy.Warnings);
                if (MessageBox.Show(
                    "将复制以下私有变量并改写可确定引用：\r\n" + mappingText + warningText,
                    "确认复制流程变量",
                    MessageBoxButtons.OKCancel,
                    variableCopy.Warnings.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning)
                    != DialogResult.OK)
                {
                    return;
                }
            }
            List<Proc> draftProcesses = procsList.Select(ObjectGraphCloner.Clone).ToList();
            draftProcesses.Insert(insertIndex, newProc);
            if (!Workspace.Runtime.ProcessVariableConfiguration.TryCommit(
                draftProcesses, draftVariables, "复制流程", out string copyError))
            {
                MessageBox.Show(copyError, "复制流程失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            TrySelectProcessStep(insertIndex, -1);
        }

        private void PasteStep()
        {
            if (SelectedProcNum < 0 || SelectedProcNum >= procsList.Count)
            {
                MessageBox.Show("请选择需要粘贴到的流程。");
                return;
            }
            if (!Workspace.Runtime.ProcessEditing.CanEditProcess(SelectedProcNum))
            {
                return;
            }
            Step newStep = ObjectGraphCloner.Clone(clipboardStep);
            ResetStepIdentity(newStep);
            int procIndex = SelectedProcNum;
            int insertIndex = SelectedStepNum >= 0 ? SelectedStepNum + 1 : procsList[procIndex].steps.Count;
            if (insertIndex < 0 || insertIndex > procsList[procIndex].steps.Count)
            {
                insertIndex = procsList[procIndex].steps.Count;
            }
            Proc before = ObjectGraphCloner.Clone(procsList[procIndex]);
            Proc draft = ObjectGraphCloner.Clone(procsList[procIndex]);
            draft.steps.Insert(insertIndex, newStep);
            ProcessEditingService.RewriteGotoTargets(before, draft, procIndex);
            if (!ProcessEditingService.TryCommitProcDraft(Workspace.Runtime,
                procIndex, draft, out string commitError, "粘贴步骤"))
            {
                MessageBox.Show(commitError, "粘贴步骤失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            TrySelectProcessStep(procIndex, insertIndex);
        }

        private void ResetProcIdentity(Proc proc)
        {
            if (proc == null)
            {
                return;
            }
            if (proc.head == null)
            {
                proc.head = new ProcHead();
            }
            proc.head.Id = Guid.NewGuid();
            if (proc.steps == null)
            {
                proc.steps = new List<Step>();
            }
            foreach (Step step in proc.steps)
            {
                ResetStepIdentity(step);
            }
        }

        private void UpdateCopiedProcName(Proc proc)
        {
            if (proc?.head == null)
            {
                return;
            }
            string sourceName = proc.head.Name;
            if (string.IsNullOrWhiteSpace(sourceName))
            {
                sourceName = "流程";
            }
            string baseName = GetProcNameBase(sourceName);
            int maxSuffix = 0;
            foreach (Proc item in procsList)
            {
                string name = item?.head?.Name;
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }
                if (string.Equals(name, baseName, StringComparison.Ordinal))
                {
                    maxSuffix = Math.Max(maxSuffix, 0);
                    continue;
                }
                if (name.StartsWith(baseName + "_", StringComparison.Ordinal))
                {
                    string suffixText = name.Substring(baseName.Length + 1);
                    if (int.TryParse(suffixText, out int suffix) && suffix > maxSuffix)
                    {
                        maxSuffix = suffix;
                    }
                }
            }
            proc.head.Name = $"{baseName}_{maxSuffix + 1}";
        }

        private string GetProcNameBase(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "流程";
            }
            int lastUnderscore = name.LastIndexOf('_');
            if (lastUnderscore <= 0 || lastUnderscore >= name.Length - 1)
            {
                return name;
            }
            string suffixText = name.Substring(lastUnderscore + 1);
            if (!int.TryParse(suffixText, out _))
            {
                return name;
            }
            return name.Substring(0, lastUnderscore);
        }

        private void ResetStepIdentity(Step step)
        {
            if (step == null)
            {
                return;
            }
            step.Id = Guid.NewGuid();
            if (step.Ops == null)
            {
                step.Ops = new List<OperationType>();
            }
            foreach (OperationType op in step.Ops)
            {
                if (op != null)
                {
                    op.Id = Guid.NewGuid();
                }
            }
        }

        public void SelectAiContext(int procIndex, int stepIndex)
        {
            if (procIndex < 0)
            {
                processOutline.ClearSelection();
                SelectedProcNum = -1;
                SelectedStepNum = -1;
                if (Workspace.DataGrid != null) Workspace.DataGrid.iSelectedRow = -1;
                return;
            }
            TrySelectProcessStep(procIndex, stepIndex);
            if (Workspace.DataGrid != null) Workspace.DataGrid.iSelectedRow = -1;
        }

        internal bool TrySelectProcessStep(int procIndex, int stepIndex)
        {
            if (procIndex < 0 || procIndex >= procsList.Count)
            {
                return false;
            }
            Guid procId = GetProcId(procIndex);
            Guid stepId = GetStepId(procIndex, stepIndex);
            if (procId == Guid.Empty
                || stepIndex >= 0 && stepId == Guid.Empty)
            {
                return false;
            }
            return processOutline.SelectIdentity(procId, stepId, true)
                && SelectedProcNum == procIndex
                && SelectedStepNum == (stepId == Guid.Empty ? -1 : stepIndex);
        }

        internal void FocusProcessOutline()
        {
            processOutline.Focus();
        }

        private void ToggleDisable_Click(object sender, EventArgs e)
        {
            if (!processOutline.HasSelection)
            {
                return;
            }
            bool isStep = SelectedStepNum >= 0;
            int procIndex = SelectedProcNum;
            int stepIndex = SelectedStepNum;
            if (procIndex < 0 || procIndex >= procsList.Count)
            {
                return;
            }
            if (!Workspace.Runtime.ProcessEditing.CanEditProcess(procIndex))
            {
                return;
            }
            EngineSnapshot snapshot = Workspace.Runtime.ProcessEngine?.GetSnapshot(procIndex);
            if (snapshot != null && !snapshot.State.IsInactive())
            {
                MessageBox.Show("流程运行中禁止禁用或启用。");
                return;
            }
            Proc draft = ObjectGraphCloner.Clone(procsList[procIndex]);
            if (draft == null)
            {
                return;
            }
            if (isStep)
            {
                if (stepIndex < 0 || stepIndex >= draft.steps.Count)
                {
                    return;
                }
                Step step = draft.steps[stepIndex];
                if (step == null)
                {
                    return;
                }
                step.Disable = !step.Disable;
            }
            else
            {
                if (draft.head == null)
                {
                    draft.head = new ProcHead();
                }
                draft.head.Disable = !draft.head.Disable;
            }
            string historyDescription = isStep
                ? "切换步骤禁用状态"
                : "切换流程禁用状态";
            if (!ProcessEditingService.TryCommitProcDraft(Workspace.Runtime,
                procIndex, draft, out string commitError, historyDescription))
            {
                MessageBox.Show(commitError, "更新禁用状态失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private string BuildProcRowText(int procIndex, Proc proc)
        {
            if (procIndex < 0)
            {
                return string.Empty;
            }
            string procName = ResolveProcName(procIndex, proc, null);
            return BuildProcRowTextCore(procIndex, procName, string.Empty);
        }

        internal string BuildProcRowTextWithState(int procIndex, Proc proc, EngineSnapshot snapshot)
        {
            if (procIndex < 0)
            {
                return string.Empty;
            }
            string procName = ResolveProcName(procIndex, proc, snapshot);
            string suffix = snapshot?.IsBreakpoint == true ? "|断点" : string.Empty;
            return BuildProcRowTextCore(procIndex, procName, suffix);
        }

        private string BuildProcRowTextCore(int procIndex, string procName, string suffix)
        {
            if (procIndex < 0)
            {
                return string.Empty;
            }
            string safeName = string.IsNullOrWhiteSpace(procName) ? $"流程{procIndex}" : procName;
            string text = $"{procIndex}：{safeName}";
            if (!string.IsNullOrWhiteSpace(suffix))
            {
                text += suffix;
            }
            return text;
        }

        private string ResolveProcName(int procIndex, Proc proc, EngineSnapshot snapshot)
        {
            string name = snapshot?.ProcName;
            if (string.IsNullOrWhiteSpace(name))
            {
                name = string.IsNullOrWhiteSpace(proc?.head?.Name) ? $"流程{procIndex}" : proc.head.Name;
            }
            return name;
        }

        private string BuildStepRowText(int procIndex, int stepIndex, Step step)
        {
            if (procIndex < 0 || stepIndex < 0)
            {
                return string.Empty;
            }
            string stepName = string.IsNullOrWhiteSpace(step?.Name) ? $"步骤{stepIndex}" : step.Name;
            return $"{stepIndex}：{stepName}";
        }
    }

    internal static class ProcessPageFont
    {
        private static readonly string FamilyName = ResolveFamilyName();

        public static Font Create(float size, FontStyle style)
        {
            try
            {
                return new Font(FamilyName, size, style, GraphicsUnit.Point);
            }
            catch
            {
                return new Font("Microsoft YaHei UI", size, style, GraphicsUnit.Point);
            }
        }

        private static string ResolveFamilyName()
        {
            string[] preferredFamilies = { "Noto Sans SC", "Microsoft YaHei UI", "DengXian" };
            try
            {
                using (var fonts = new System.Drawing.Text.InstalledFontCollection())
                {
                    var installedNames = new HashSet<string>(
                        fonts.Families.Select(family => family.Name),
                        StringComparer.OrdinalIgnoreCase);
                    return preferredFamilies.First(installedNames.Contains);
                }
            }
            catch
            {
                return "Microsoft YaHei UI";
            }
        }
    }

}
