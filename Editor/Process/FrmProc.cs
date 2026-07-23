// 模块：编辑器 / 流程。
// 职责范围：流程树、指令表、对象选择、搜索和导航。
// 排查入口：树节点显示异常先核对 ProcessSelection 的稳定 ID 与索引，再检查 Repository；不要从其他窗体反读状态。

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
using System.Runtime.InteropServices;
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
        private const int TvmSetExtendedStyle = 0x112C;
        private const int TvsExDoubleBuffer = 0x0004;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(
            IntPtr windowHandle,
            int message,
            IntPtr wordParameter,
            IntPtr longParameter);

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

        private readonly object procNodeMapLock = new object();
        private readonly Dictionary<Guid, TreeNode> procNodeMap = new Dictionary<Guid, TreeNode>();
        private readonly Dictionary<Guid, int> procIndexMap = new Dictionary<Guid, int>();
        private ImageList procStateImages;
        private Font procNodeFont;
        private Font stepNodeFont;

        // 流程节点变动动效：AI 改动流程后在 proc_treeView 上闪烁对应节点，让用户直观看到改动位置。
        private System.Windows.Forms.Timer procFlashTimer;
        private TreeNode procFlashNode;
        private Color procFlashColor;
        private int procFlashCount;
        private const int ProcFlashMaxCount = 6;

        private bool suppressContextMenuOnce = false;
        private Proc clipboardProc;
        private Step clipboardStep;
        private bool restoringTreeState;

        public FrmProc()
            : this(new ProcessDefinitionRepository())
        {
        }

        public FrmProc(ProcessDefinitionRepository processDefinitionRepository)
        {
            this.processDefinitionRepository = processDefinitionRepository
                ?? throw new ArgumentNullException(nameof(processDefinitionRepository));
            InitializeComponent();
            ConfigureProcTreeAppearance();
            ConfigureProcContextMenu();
            Disposed += FrmProc_Disposed;
            proc_treeView.HandleCreated += ProcTreeView_HandleCreated;
            this.proc_treeView.HideSelection = false;
            typeof(Control).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(proc_treeView, true, null);
            proc_treeView.BeforeSelect += proc_treeView_BeforeSelect;
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
            if (procFlashTimer != null)
            {
                procFlashTimer.Stop();
                procFlashTimer.Tick -= ProcFlashTimer_Tick;
                procFlashTimer.Dispose();
                procFlashTimer = null;
            }
            procFlashNode = null;
            procStateImages?.Dispose();
            procStateImages = null;
            proc_treeView.HandleCreated -= ProcTreeView_HandleCreated;
            proc_treeView.DrawNode -= ProcTreeView_DrawNode;
            procNodeFont?.Dispose();
            procNodeFont = null;
            stepNodeFont?.Dispose();
            stepNodeFont = null;
        }

        private void ConfigureProcTreeAppearance()
        {
            procNodeFont = ProcessPageFont.Create(13.5F, FontStyle.Bold);
            stepNodeFont = ProcessPageFont.Create(13F, FontStyle.Regular);
            // TreeView 使用基准字体计算节点标签边界。基准字体小于流程节点粗体时，
            // WinForms 会在仍有可用宽度的情况下裁掉流程名称末尾。
            proc_treeView.Font = procNodeFont;
            proc_treeView.ForeColor = UiPalette.TextPrimary;
            proc_treeView.BackColor = UiPalette.Background;
            proc_treeView.BorderStyle = BorderStyle.None;
            proc_treeView.ItemHeight = 30;
            proc_treeView.Indent = 24;
            proc_treeView.ShowLines = false;
            proc_treeView.ShowRootLines = false;
            proc_treeView.DrawMode = TreeViewDrawMode.OwnerDrawAll;
            proc_treeView.DrawNode += ProcTreeView_DrawNode;

            procStateImages = new ImageList
            {
                ColorDepth = ColorDepth.Depth32Bit,
                ImageSize = new Size(20, 20),
                TransparentColor = Color.Transparent
            };
            AddProcStateImage("proc-ready", ProcTreeIconKind.Ready);
            AddProcStateImage("proc-stopped", ProcTreeIconKind.Stopped);
            AddProcStateImage("proc-running", ProcTreeIconKind.Running);
            AddProcStateImage("proc-paused", ProcTreeIconKind.Paused);
            AddProcStateImage("proc-single", ProcTreeIconKind.SingleStep);
            AddProcStateImage("proc-alarm", ProcTreeIconKind.Alarming);
            AddProcStateImage("proc-pausing", ProcTreeIconKind.Pausing);
            AddProcStateImage("proc-stopping", ProcTreeIconKind.Stopping);
            AddProcStateImage("proc-empty", ProcTreeIconKind.EmptyProc);
            AddProcStateImage("proc-empty-disabled", ProcTreeIconKind.EmptyProcDisabled);
            AddProcStateImage("disabled", ProcTreeIconKind.Disabled);
            AddProcStateImage("step", ProcTreeIconKind.Step);
            AddProcStateImage("step-empty", ProcTreeIconKind.EmptyStep);
            AddProcStateImage("step-empty-disabled", ProcTreeIconKind.EmptyStepDisabled);
            AddProcStateImage("step-running", ProcTreeIconKind.StepRunning);
            AddProcStateImage("step-paused", ProcTreeIconKind.StepPaused);
            AddProcStateImage("step-single", ProcTreeIconKind.StepSingle);
            AddProcStateImage("step-alarm", ProcTreeIconKind.StepAlarming);
            proc_treeView.ImageList = procStateImages;
            ApplyNativeTreeDoubleBuffer();
        }

        private void ProcTreeView_DrawNode(object sender, DrawTreeNodeEventArgs e)
        {
            bool selected = (e.State & TreeNodeStates.Selected) == TreeNodeStates.Selected
                || ReferenceEquals(proc_treeView.SelectedNode, e.Node);
            if (!selected)
            {
                e.DrawDefault = true;
                return;
            }

            int rowHeight = Math.Max(proc_treeView.ItemHeight, e.Bounds.Height);
            int rowTop = e.Bounds.Top;
            var rowBounds = new Rectangle(
                0,
                rowTop,
                Math.Max(1, proc_treeView.ClientSize.Width),
                rowHeight);
            using (var selectionBrush = new SolidBrush(UiPalette.Selection))
            using (var accentBrush = new SolidBrush(UiPalette.Brand))
            {
                e.Graphics.FillRectangle(selectionBrush, rowBounds);
                e.Graphics.FillRectangle(accentBrush, 0, rowTop, 3, rowHeight);
            }

            const int imageSize = 20;
            int imageLeft = Math.Max(5, e.Bounds.Left - imageSize - 2);
            var imageBounds = new Rectangle(
                imageLeft,
                rowTop + Math.Max(0, (rowHeight - imageSize) / 2),
                imageSize,
                imageSize);
            string imageKey = string.IsNullOrEmpty(e.Node.SelectedImageKey)
                ? e.Node.ImageKey
                : e.Node.SelectedImageKey;
            if (proc_treeView.ImageList != null
                && !string.IsNullOrEmpty(imageKey)
                && proc_treeView.ImageList.Images.ContainsKey(imageKey))
            {
                e.Graphics.DrawImage(proc_treeView.ImageList.Images[imageKey], imageBounds);
            }

            if (e.Node.Nodes.Count > 0 && imageLeft >= 22)
            {
                DrawSelectedNodeChevron(
                    e.Graphics,
                    imageLeft - 11,
                    rowTop + rowHeight / 2,
                    e.Node.IsExpanded);
            }

            Font font = e.Node.NodeFont ?? proc_treeView.Font;
            var textBounds = new Rectangle(
                Math.Max(e.Bounds.Left, imageBounds.Right + 2),
                rowTop,
                Math.Max(1, proc_treeView.ClientSize.Width - e.Bounds.Left - 8),
                rowHeight);
            TextRenderer.DrawText(
                e.Graphics,
                e.Node.Text,
                font,
                textBounds,
                UiPalette.SelectionText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis
                    | TextFormatFlags.NoPadding);
        }

        private static void DrawSelectedNodeChevron(
            Graphics graphics,
            int centerX,
            int centerY,
            bool expanded)
        {
            using (var pen = new Pen(UiPalette.SelectionText, 1.4F)
            {
                StartCap = System.Drawing.Drawing2D.LineCap.Round,
                EndCap = System.Drawing.Drawing2D.LineCap.Round
            })
            {
                if (expanded)
                {
                    graphics.DrawLine(pen, centerX - 3, centerY - 1, centerX, centerY + 2);
                    graphics.DrawLine(pen, centerX, centerY + 2, centerX + 3, centerY - 1);
                    return;
                }
                graphics.DrawLine(pen, centerX - 1, centerY - 3, centerX + 2, centerY);
                graphics.DrawLine(pen, centerX + 2, centerY, centerX - 1, centerY + 3);
            }
        }

        private void ProcTreeView_HandleCreated(object sender, EventArgs e)
        {
            ApplyNativeTreeDoubleBuffer();
        }

        private void ApplyNativeTreeDoubleBuffer()
        {
            if (!proc_treeView.IsHandleCreated || proc_treeView.IsDisposed)
            {
                return;
            }
            SendMessage(
                proc_treeView.Handle,
                TvmSetExtendedStyle,
                new IntPtr(TvsExDoubleBuffer),
                new IntPtr(TvsExDoubleBuffer));
        }

        private void AddProcStateImage(string key, ProcTreeIconKind kind)
        {
            // ImageList 在创建原生句柄时才读取源图像，图像由 ImageList 统一持有并释放。
            // 此处提前释放会在 TreeView.OnHandleCreated 中触发 GDI+“参数无效”。
            procStateImages.Images.Add(key, ProcTreeIconFactory.Create(kind, 20));
        }

        private static void SetNodeImage(TreeNode node, string imageKey)
        {
            if (node == null || string.IsNullOrEmpty(imageKey))
            {
                return;
            }
            if (!string.Equals(node.ImageKey, imageKey, StringComparison.Ordinal))
            {
                node.ImageKey = imageKey;
            }
            if (!string.Equals(node.SelectedImageKey, imageKey, StringComparison.Ordinal))
            {
                node.SelectedImageKey = imageKey;
            }
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

        internal void UpdateProcStateIcons(int procIndex, EngineSnapshot snapshot)
        {
            if (proc_treeView.IsDisposed || procIndex < 0 || procIndex >= procsList.Count
                || procIndex >= proc_treeView.Nodes.Count)
            {
                return;
            }
            if (proc_treeView.InvokeRequired)
            {
                proc_treeView.BeginInvoke((Action)(() => UpdateProcStateIcons(procIndex, snapshot)));
                return;
            }

            Proc proc = procsList[procIndex];
            TreeNode procNode = proc_treeView.Nodes[procIndex];
            SetNodeImage(procNode, GetProcImageKey(proc, snapshot));
            int stepCount = Math.Min(proc?.steps?.Count ?? 0, procNode.Nodes.Count);
            for (int i = 0; i < stepCount; i++)
            {
                TreeNode stepNode = procNode.Nodes[i];
                SetNodeImage(stepNode, GetStepImageKey(proc, proc.steps[i], i, snapshot));
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
            TreeNode currentSelectedNode = proc_treeView.SelectedNode;
            Guid selectedProcId = currentSelectedNode?.Parent == null
                ? ReadNodeId(currentSelectedNode)
                : ReadNodeId(currentSelectedNode.Parent);
            Guid selectedStepId = currentSelectedNode?.Parent == null
                ? Guid.Empty
                : ReadNodeId(currentSelectedNode);
            if (selectedProcId == Guid.Empty)
            {
                selectedProcId = GetProcId(SelectedProcNum);
                selectedStepId = GetStepId(SelectedProcNum, SelectedStepNum);
            }
            Guid topProcId = proc_treeView.TopNode?.Parent == null
                ? ReadNodeId(proc_treeView.TopNode)
                : ReadNodeId(proc_treeView.TopNode.Parent);
            var expandedProcIds = new HashSet<Guid>();
            foreach (TreeNode node in proc_treeView.Nodes)
            {
                Guid id = ReadNodeId(node);
                if (node.IsExpanded && id != Guid.Empty)
                {
                    expandedProcIds.Add(id);
                }
            }

            List<Proc> procsListTemp;
            List<string> loadErrors;

            restoringTreeState = true;
            proc_treeView.BeginUpdate();
            try
            {
            proc_treeView.Nodes.Clear();
            lock (procNodeMapLock)
            {
                procNodeMap.Clear();
                procIndexMap.Clear();
            }
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
            if (!string.IsNullOrWhiteSpace(recoveryMessage) && Workspace.Info != null && !Workspace.Info.IsDisposed)
            {
                Workspace.Info.PrintInfo(recoveryMessage, FrmInfo.Level.Error);
            }
            for (int i = 0; i < procsListTemp.Count; i++)
            {
                Proc proc = procsListTemp[i];

                EngineSnapshot procSnapshot = Workspace.Runtime.ProcessEngine?.GetSnapshot(i);
                TreeNode treeNode = new TreeNode(BuildProcNodeText(i, proc))
                {
                    NodeFont = procNodeFont
                };
                SetNodeImage(treeNode, GetProcImageKey(proc, procSnapshot));
                if (proc?.head?.Disable == true)
                {
                    treeNode.ForeColor = UiPalette.DisabledSoft;
                }
                Guid procId = proc?.head?.Id ?? Guid.Empty;
                if (procId != Guid.Empty)
                {
                    treeNode.Tag = procId;
                    lock (procNodeMapLock)
                    {
                        procNodeMap[procId] = treeNode;
                        procIndexMap[procId] = i;
                    }
                }
                proc_treeView.Nodes.Add(treeNode);

                if (proc.steps != null)
                {
                    for (int j = 0; j < proc.steps.Count; j++)
                    {
                        Step step = proc.steps[j];
                        TreeNode chnode = new TreeNode(BuildStepNodeText(i, j, step))
                        {
                            NodeFont = stepNodeFont
                        };
                        chnode.Tag = step?.Id ?? Guid.Empty;
                        SetNodeImage(chnode, GetStepImageKey(proc, step, j, procSnapshot));
                        if (proc?.head?.Disable == true || proc.steps[j]?.Disable == true)
                        {
                            chnode.ForeColor = UiPalette.DisabledSoft;
                        }
                        proc_treeView.Nodes[i].Nodes.Add(chnode);
                    }
                }
            }
            if (reloadConfiguration)
            {
                processDefinitionRepository.ReplaceAll(procsListTemp);
            }
            RestoreTreeState(selectedProcId, selectedStepId, topProcId, expandedProcIds);
            }
            finally
            {
                proc_treeView.EndUpdate();
                restoringTreeState = false;
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
        /// 流程内容修改后的局部界面刷新。不会重新读取全部流程文件，也不会清空流程树。
        /// </summary>
        public void RefreshProcView(int procIndex)
        {
            if (InvokeRequired)
            {
                BeginInvoke((Action)(() => RefreshProcView(procIndex)));
                return;
            }
            if (procIndex < 0 || procIndex >= procsList.Count || proc_treeView.IsDisposed)
            {
                return;
            }

            Proc proc = procsList[procIndex];
            TreeNode procNode = procIndex < proc_treeView.Nodes.Count ? proc_treeView.Nodes[procIndex] : null;
            if (procNode == null)
            {
                RefreshProcList();
                return;
            }

            bool wasExpanded = procNode.IsExpanded;
            Guid selectedStepId = GetStepId(SelectedProcNum, SelectedStepNum);
            proc_treeView.BeginUpdate();
            try
            {
                procNode.Text = BuildProcNodeText(procIndex, proc);
                procNode.Tag = proc?.head?.Id ?? Guid.Empty;
                procNode.NodeFont = procNodeFont;
                procNode.ForeColor = proc?.head?.Disable == true ? UiPalette.DisabledSoft : proc_treeView.ForeColor;

                int stepCount = proc?.steps?.Count ?? 0;
                bool structureChanged = procNode.Nodes.Count != stepCount;
                if (!structureChanged)
                {
                    for (int i = 0; i < stepCount; i++)
                    {
                        if (ReadNodeId(procNode.Nodes[i]) != (proc.steps[i]?.Id ?? Guid.Empty))
                        {
                            structureChanged = true;
                            break;
                        }
                    }
                }
                if (structureChanged)
                {
                    procNode.Nodes.Clear();
                    for (int i = 0; i < stepCount; i++)
                    {
                        Step step = proc.steps[i];
                        var stepNode = new TreeNode(BuildStepNodeText(procIndex, i, step))
                        {
                            Tag = step?.Id ?? Guid.Empty,
                            NodeFont = stepNodeFont,
                            ForeColor = proc?.head?.Disable == true || step?.Disable == true
                                ? UiPalette.DisabledSoft
                                : proc_treeView.ForeColor
                        };
                        procNode.Nodes.Add(stepNode);
                    }
                }
                else
                {
                    for (int i = 0; i < stepCount; i++)
                    {
                        Step step = proc.steps[i];
                        procNode.Nodes[i].Text = BuildStepNodeText(procIndex, i, step);
                        procNode.Nodes[i].NodeFont = stepNodeFont;
                        procNode.Nodes[i].ForeColor = proc?.head?.Disable == true || step?.Disable == true
                            ? UiPalette.DisabledSoft
                            : proc_treeView.ForeColor;
                    }
                }
                if (wasExpanded)
                {
                    procNode.Expand();
                }
                if (SelectedProcNum == procIndex && selectedStepId != Guid.Empty)
                {
                    TreeNode selectedStepNode = procNode.Nodes.Cast<TreeNode>()
                        .FirstOrDefault(node => ReadNodeId(node) == selectedStepId);
                    if (selectedStepNode != null)
                    {
                        proc_treeView.SelectedNode = selectedStepNode;
                        SelectedStepNum = selectedStepNode.Index;
                    }
                }
            }
            finally
            {
                proc_treeView.EndUpdate();
            }

            if (SelectedProcNum == procIndex)
            {
                RefreshCurrentBinding();
            }
            UpdateProcStateIcons(procIndex, Workspace.Runtime.ProcessEngine?.GetSnapshot(procIndex));
            Workspace.Main?.RefreshProcessFlowGraph();
        }

        private void RestoreTreeState(Guid selectedProcId, Guid selectedStepId, Guid topProcId, HashSet<Guid> expandedProcIds)
        {
            TreeNode selectedNode = null;
            TreeNode topNode = null;
            foreach (TreeNode procNode in proc_treeView.Nodes)
            {
                Guid procId = ReadNodeId(procNode);
                if (expandedProcIds.Contains(procId))
                {
                    procNode.Expand();
                }
                if (procId == topProcId)
                {
                    topNode = procNode;
                }
                if (procId != selectedProcId)
                {
                    continue;
                }
                selectedNode = procNode;
                if (selectedStepId != Guid.Empty)
                {
                    selectedNode = procNode.Nodes.Cast<TreeNode>()
                        .FirstOrDefault(node => ReadNodeId(node) == selectedStepId) ?? procNode;
                }
            }
            proc_treeView.SelectedNode = selectedNode;
            if (topNode != null)
            {
                proc_treeView.TopNode = topNode;
            }
            if (selectedNode == null)
            {
                SelectedProcNum = -1;
                SelectedStepNum = -1;
                RefreshCurrentBinding();
            }
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

        private static Guid ReadNodeId(TreeNode node)
        {
            return node?.Tag is Guid id ? id : Guid.Empty;
        }

        /// <summary>
        /// 在 proc_treeView 上闪烁指定流程节点，提示用户该流程被 AI 改动。
        /// kind 决定闪烁颜色：Modified=橙黄、Added=浅绿、Deleted=浅红。
        /// </summary>
        public void FlashProcNode(int procIndex, ProcChangeKind kind)
        {
            if (IsDisposed || Disposing || !IsHandleCreated)
            {
                return;
            }
            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke((Action)(() => FlashProcNode(procIndex, kind)));
                }
                catch (InvalidOperationException)
                {
                }
                return;
            }

            // 停止之前未完成的闪烁，恢复节点背景色
            if (procFlashTimer != null)
            {
                procFlashTimer.Stop();
                procFlashTimer.Dispose();
                procFlashTimer = null;
            }
            if (procFlashNode != null && procFlashNode.TreeView != null)
            {
                procFlashNode.BackColor = Color.Empty;
            }
            procFlashNode = null;

            if (procIndex < 0 || procIndex >= proc_treeView.Nodes.Count)
            {
                return;
            }

            procFlashNode = proc_treeView.Nodes[procIndex];
            procFlashColor = kind == ProcChangeKind.Added ? UiPalette.SuccessSoft
                           : kind == ProcChangeKind.Deleted ? UiPalette.DangerSoft
                           : UiPalette.WarningSoft;
            procFlashCount = 0;
            // 后台流程变化不抢占用户当前树滚动位置；当前流程或新增流程才滚动到提示节点。
            if (SelectedProcNum == procIndex || kind == ProcChangeKind.Added)
            {
                procFlashNode.EnsureVisible();
            }

            procFlashTimer = new System.Windows.Forms.Timer();
            procFlashTimer.Interval = 300;
            procFlashTimer.Tick += ProcFlashTimer_Tick;
            procFlashTimer.Start();
        }

        private void ProcFlashTimer_Tick(object sender, EventArgs e)
        {
            if (procFlashNode == null || procFlashNode.TreeView == null)
            {
                procFlashTimer?.Stop();
                return;
            }
            if (procFlashCount >= ProcFlashMaxCount)
            {
                procFlashNode.BackColor = Color.Empty;
                procFlashNode = null;
                procFlashTimer.Stop();
                procFlashTimer.Dispose();
                procFlashTimer = null;
                return;
            }
            procFlashNode.BackColor = (procFlashCount % 2 == 0) ? procFlashColor : Color.Empty;
            procFlashCount++;
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
                if (SelectedStepNum >= 0 && SelectedStepNum < procsList[SelectedProcNum].steps.Count)
                {
                    Step selectedStep = procsList[SelectedProcNum].steps[SelectedStepNum];
                    grid?.SetFlowContext(SelectedProcNum, SelectedStepNum, procsList[SelectedProcNum]);
                    bindingSource.DataSource = selectedStep.Ops;
                    fallbackInspectorObject = selectedStep;
                }
                else
                {
                    grid?.SetFlowContext(SelectedProcNum, -1, procsList[SelectedProcNum]);
                    bindingSource.DataSource = null;
                    fallbackInspectorObject = procsList[SelectedProcNum].head;
                }
                bindingSource.ResetBindings(false);
                bool restoredOperation = false;
                if (grid != null && !grid.IsDisposed)
                {
                    if (!ReferenceEquals(grid.DataSource, bindingSource))
                    {
                        grid.DataSource = bindingSource;
                    }
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

            proc_treeView.Enabled = false;
            Workspace.Runtime.Editor.Begin(new EditSession<ProcHead>("新增流程", draftHead,
                draft => string.IsNullOrWhiteSpace(draft.Name) ? "流程名称为空。" : null,
                draft =>
                {
                    if (!TrySaveNewProc(draft, selectedProcIndex))
                    {
                        throw new InvalidOperationException("新增流程保存失败。");
                    }
                    proc_treeView.Enabled = true;
                },
                () => proc_treeView.Enabled = true));
        }

        private void AddStep_Click(object sender, EventArgs e)
        {

            TreeNode selectdnode = proc_treeView.SelectedNode;
            if (selectdnode != null)
            {
                if (!Workspace.Runtime.ProcessEditing.CanEditProcess(Workspace.Proc.SelectedProcNum))
                {
                    return;
                }
                int procIndex = SelectedProcNum;
                int selectedStepIndex = SelectedStepNum;
                var draftStep = new Step();
                proc_treeView.Enabled = false;
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
                        proc_treeView.Enabled = true;
                    },
                    () => proc_treeView.Enabled = true));
            }
            else
            {
                MessageBox.Show("请选择需要添加子节点");
            }

        }

        private void Remove_Click(object sender, EventArgs e)
        {
            TreeNode selectnode = proc_treeView.SelectedNode;
            if (selectnode == null)
            {
                MessageBox.Show("请选择需要删除的流程或步骤");
                return;
            }
            TreeNode parentnode = selectnode.Parent;
            if (parentnode == null)
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
        private void proc_treeView_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (proc_treeView.SelectedNode != null)
            {
                
                if (proc_treeView.SelectedNode.Parent != null)
                {
                    SelectedProcNum = proc_treeView.SelectedNode.Parent.Index;

                    SelectedStepNum = proc_treeView.SelectedNode.Index;

                    Workspace.DataGrid.iSelectedRow = -1;

                    if (SelectedProcNum < 0 || SelectedProcNum >= procsList.Count)
                    {
                        MessageBox.Show("流程索引无效，无法加载步骤。");
                        SelectedProcNum = -1;
                        SelectedStepNum = -1;
                        bindingSource.DataSource = null;
                        Workspace.DataGrid.dataGridView1.SetFlowContext(-1, -1, null);
                        Workspace.DataGrid.dataGridView1.DataSource = null;
                        Workspace.Inspector.ClearObject();
                        return;
                    }
                    if (SelectedStepNum < 0 || SelectedStepNum >= procsList[SelectedProcNum].steps.Count)
                    {
                        MessageBox.Show("步骤索引无效，无法加载指令。");
                        SelectedStepNum = -1;
                        bindingSource.DataSource = null;
                        Workspace.DataGrid.dataGridView1.SetFlowContext(SelectedProcNum, -1, procsList[SelectedProcNum]);
                        Workspace.DataGrid.dataGridView1.DataSource = null;
                        Workspace.Inspector.ClearObject();
                        return;
                    }
                    Workspace.DataGrid.dataGridView1.SetFlowContext(
                        SelectedProcNum,
                        SelectedStepNum,
                        procsList[SelectedProcNum]);
                    bindingSource.DataSource = procsList[SelectedProcNum].steps[SelectedStepNum].Ops;

                    Workspace.Inspector.ShowObject(procsList[SelectedProcNum].steps[Workspace.Proc.SelectedStepNum]);


                }
                else
                {
                    SelectedProcNum = proc_treeView.SelectedNode.Index;

                    SelectedStepNum = -1;

                    if (SelectedProcNum < 0 || SelectedProcNum >= procsList.Count)
                    {
                        MessageBox.Show("流程索引无效，无法加载。");
                        SelectedProcNum = -1;
                        bindingSource.DataSource = null;
                        Workspace.DataGrid.dataGridView1.SetFlowContext(-1, -1, null);
                        Workspace.DataGrid.dataGridView1.DataSource = null;
                        Workspace.Inspector.ClearObject();
                        return;
                    }
                    Workspace.DataGrid.dataGridView1.SetFlowContext(
                        SelectedProcNum,
                        -1,
                        procsList[SelectedProcNum]);
                    bindingSource.DataSource = null;

                    Workspace.Inspector.ShowObject(procsList[SelectedProcNum].head);


                }
                Workspace.DataGrid.dataGridView1.DataSource = bindingSource;

                if (Workspace.Runtime.ProcessEngine != null && Workspace.Proc.SelectedProcNum != -1)
                {
                    EngineSnapshot snapshot = Workspace.Runtime.ProcessEngine.GetSnapshot(Workspace.Proc.SelectedProcNum);
                    if (snapshot != null && (snapshot.State == ProcRunState.Running || snapshot.State == ProcRunState.Alarming))
                    {
                        Workspace.ToolBar.SetPauseButtonAction(false);
                    }
                    else if (snapshot != null && (snapshot.State == ProcRunState.Paused || snapshot.State == ProcRunState.SingleStep))
                    {
                        Workspace.ToolBar.SetPauseButtonAction(true);
                    }
                    else
                    {
                        Workspace.ToolBar.SetPauseButtonAction(false);
                    }
                    if (snapshot != null)
                    {
                        Workspace.ToolBar.btnPause.Enabled = snapshot.State != ProcRunState.Paused;
                        Workspace.ToolBar.SingleRun.Enabled = snapshot.State == ProcRunState.SingleStep;
                    }
                    else
                    {
                        Workspace.ToolBar.btnPause.Enabled = true;
                        Workspace.ToolBar.SingleRun.Enabled = true;
                    }
                }
                else
                {
                    Workspace.ToolBar.SetPauseButtonAction(false);
                    Workspace.ToolBar.btnPause.Enabled = true;
                    Workspace.ToolBar.SingleRun.Enabled = true;
                }
              
            }
        }

        private void proc_treeView_BeforeSelect(object sender, TreeViewCancelEventArgs e)
        {
            if (restoringTreeState)
            {
                return;
            }
            if (Workspace.Runtime.Editor.IsAddingOperations || Workspace.Runtime.Editor.ModifyKind == ModifyKind.Operation)
            {
                if (proc_treeView.SelectedNode != e.Node)
                {
                    e.Cancel = true;
                    if (Workspace.Info != null && !Workspace.Info.IsDisposed)
                    {
                        Workspace.Info.PrintInfo("新增或编辑指令中，禁止切换流程。", FrmInfo.Level.Error);
                    }
                    else
                    {
                        MessageBox.Show("新增或编辑指令中，禁止切换流程。");
                    }
                }
            }
        }

        private void proc_treeView_MouseDown(object sender, MouseEventArgs e)
        {
            suppressContextMenuOnce = false;
            if (Workspace.Runtime.Editor.IsAddingOperations || Workspace.Runtime.Editor.ModifyKind == ModifyKind.Operation)
            {
                if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Right)
                {
                    var treeView = (System.Windows.Forms.TreeView)sender;
                    var clickedNode = treeView.HitTest(e.Location).Node;
                    if (clickedNode == null || clickedNode != treeView.SelectedNode)
                    {
                        if (e.Button == MouseButtons.Right)
                        {
                            suppressContextMenuOnce = true;
                        }
                        if (Workspace.Info != null && !Workspace.Info.IsDisposed)
                        {
                            Workspace.Info.PrintInfo("新增或编辑指令中，禁止切换流程。", FrmInfo.Level.Error);
                        }
                        else
                        {
                            MessageBox.Show("新增或编辑指令中，禁止切换流程。");
                        }
                        return;
                    }
                }
            }
            if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Right)
            {
                var treeView = (System.Windows.Forms.TreeView)sender;
                var clickedNode = treeView.HitTest(e.Location).Node;

                if (clickedNode == null) // 点击的是空白区域
                {
                    treeView.SelectedNode = null; // 取消当前节点选择

                    SelectedProcNum = -1;

                    SelectedStepNum = -1;
                    Workspace.DataGrid.iSelectedRow = -1;
                    Workspace.DataGrid.OperationTemp = null;
                    bindingSource.DataSource = null;
                    Workspace.DataGrid.dataGridView1.SetFlowContext(-1, -1, null);
                    Workspace.DataGrid.dataGridView1.DataSource = null;
                    Workspace.Inspector.ClearObject();
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
            proc_treeView.Enabled = false;
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
                        proc_treeView.Enabled = true;
                    }, () => proc_treeView.Enabled = true));
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
                        proc_treeView.Enabled = true;
                    }, () => proc_treeView.Enabled = true));
            }
            else
            {
                proc_treeView.Enabled = true;
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
            TreeNode node = proc_treeView.SelectedNode;
            if (node == null)
            {
                ToggleDisable.Enabled = false;
                ToggleDisable.Text = "禁用";
                return;
            }
            bool isStep = node.Parent != null;
            int procIndex = isStep ? node.Parent.Index : node.Index;
            int stepIndex = isStep ? node.Index : -1;
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
                CopyProcStep.Enabled = proc_treeView.SelectedNode != null;
            }
            if (PasteProcStep != null)
            {
                PasteProcStep.Enabled = clipboardProc != null || clipboardStep != null;
            }
        }

        private void CopyProcStep_Click(object sender, EventArgs e)
        {
            TreeNode node = proc_treeView.SelectedNode;
            if (node == null)
            {
                MessageBox.Show("请选择需要复制的流程或步骤。");
                return;
            }
            if (node.Parent == null)
            {
                int procIndex = node.Index;
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
                int procIndex = node.Parent.Index;
                int stepIndex = node.Index;
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
            SelectProcNode(insertIndex, -1);
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
            SelectProcNode(procIndex, insertIndex);
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
                proc_treeView.SelectedNode = null;
                SelectedProcNum = -1;
                SelectedStepNum = -1;
                if (Workspace.DataGrid != null) Workspace.DataGrid.iSelectedRow = -1;
                return;
            }
            SelectProcNode(procIndex, stepIndex);
            if (Workspace.DataGrid != null) Workspace.DataGrid.iSelectedRow = -1;
        }

        private void SelectProcNode(int procIndex, int stepIndex)
        {
            if (proc_treeView == null || proc_treeView.Nodes.Count == 0)
            {
                return;
            }
            if (procIndex < 0 || procIndex >= proc_treeView.Nodes.Count)
            {
                return;
            }
            TreeNode target = proc_treeView.Nodes[procIndex];
            if (stepIndex >= 0 && stepIndex < target.Nodes.Count)
            {
                target = target.Nodes[stepIndex];
            }
            proc_treeView.SelectedNode = target;
            target.EnsureVisible();
        }

        private void ToggleDisable_Click(object sender, EventArgs e)
        {
            TreeNode node = proc_treeView.SelectedNode;
            if (node == null)
            {
                return;
            }
            bool isStep = node.Parent != null;
            int procIndex = isStep ? node.Parent.Index : node.Index;
            int stepIndex = isStep ? node.Index : -1;
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

        private string BuildProcNodeText(int procIndex)
        {
            if (procIndex < 0 || procIndex >= procsList.Count)
            {
                return string.Empty;
            }
            Proc proc = procsList[procIndex];
            return BuildProcNodeText(procIndex, proc);
        }

        private string BuildStepNodeText(int procIndex, int stepIndex)
        {
            if (procIndex < 0 || procIndex >= procsList.Count)
            {
                return string.Empty;
            }
            if (stepIndex < 0 || stepIndex >= procsList[procIndex].steps.Count)
            {
                return string.Empty;
            }
            Step step = procsList[procIndex].steps[stepIndex];
            return BuildStepNodeText(procIndex, stepIndex, step);
        }

        private string BuildProcNodeText(int procIndex, Proc proc)
        {
            if (procIndex < 0)
            {
                return string.Empty;
            }
            string procName = ResolveProcName(procIndex, proc, null);
            return BuildProcNodeTextCore(procIndex, procName, string.Empty);
        }

        internal string BuildProcNodeTextWithState(int procIndex, Proc proc, EngineSnapshot snapshot)
        {
            if (procIndex < 0)
            {
                return string.Empty;
            }
            string procName = ResolveProcName(procIndex, proc, snapshot);
            string suffix = snapshot?.IsBreakpoint == true ? "|断点" : string.Empty;
            return BuildProcNodeTextCore(procIndex, procName, suffix);
        }

        internal bool TryGetProcNode(Guid procId, out TreeNode node, out int procIndex)
        {
            node = null;
            procIndex = -1;
            if (procId == Guid.Empty)
            {
                return false;
            }
            lock (procNodeMapLock)
            {
                if (procNodeMap.TryGetValue(procId, out TreeNode cachedNode)
                    && procIndexMap.TryGetValue(procId, out int cachedIndex))
                {
                    node = cachedNode;
                    procIndex = cachedIndex;
                    return true;
                }
            }
            return false;
        }

        private string BuildProcNodeTextCore(int procIndex, string procName, string suffix)
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

        private string BuildStepNodeText(int procIndex, int stepIndex, Step step)
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
