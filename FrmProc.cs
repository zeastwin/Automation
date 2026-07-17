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

        //存放所有流程信息
        public List<Proc> procsList = new List<Proc> ();
        public int SelectedProcNum { get; set; }
        public int SelectedStepNum { get; set; }

        private static readonly Color DisabledNodeColor = Color.Gainsboro;
        private const string DisabledTag = "[禁用]";
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
        {
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
                contextMenuStrip1.BackColor = Color.White;
                contextMenuStrip1.ForeColor = Color.FromArgb(42, 55, 63);
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
                Remove.ForeColor = Color.FromArgb(188, 55, 64);

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
                case ProcMenuIconKind.Start: return Color.FromArgb(43, 145, 93);
                case ProcMenuIconKind.Disable: return Color.FromArgb(214, 126, 30);
                case ProcMenuIconKind.Delete: return Color.FromArgb(205, 62, 70);
                default: return Color.FromArgb(61, 112, 139);
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
                    using (Brush brush = new SolidBrush(Color.FromArgb(226, 241, 248)))
                    {
                        e.Graphics.FillRectangle(brush, bounds);
                    }
                }
            }
        }

        private sealed class ProcContextMenuColorTable : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground => Color.White;
            public override Color ImageMarginGradientBegin => Color.White;
            public override Color ImageMarginGradientMiddle => Color.White;
            public override Color ImageMarginGradientEnd => Color.White;
            public override Color MenuBorder => Color.FromArgb(178, 188, 193);
            public override Color SeparatorDark => Color.FromArgb(226, 232, 235);
            public override Color SeparatorLight => Color.White;
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
            procNodeFont?.Dispose();
            procNodeFont = null;
            stepNodeFont?.Dispose();
            stepNodeFont = null;
        }

        private void ConfigureProcTreeAppearance()
        {
            procNodeFont = ProcessPageFont.Create(10.5F, FontStyle.Bold);
            stepNodeFont = ProcessPageFont.Create(10F, FontStyle.Regular);
            // TreeView 使用基准字体计算节点标签边界。基准字体小于流程节点粗体时，
            // WinForms 会在仍有可用宽度的情况下裁掉流程名称末尾。
            proc_treeView.Font = procNodeFont;
            proc_treeView.ForeColor = Color.FromArgb(38, 50, 58);
            proc_treeView.BackColor = Color.FromArgb(246, 249, 251);
            proc_treeView.BorderStyle = BorderStyle.None;
            proc_treeView.ItemHeight = 28;
            proc_treeView.Indent = 24;
            proc_treeView.ShowLines = false;
            proc_treeView.ShowRootLines = false;

            procStateImages = new ImageList
            {
                ColorDepth = ColorDepth.Depth32Bit,
                ImageSize = new Size(20, 20),
                TransparentColor = Color.Transparent
            };
            AddProcStateImage("proc-stopped", ProcTreeIconKind.Stopped);
            AddProcStateImage("proc-running", ProcTreeIconKind.Running);
            AddProcStateImage("proc-paused", ProcTreeIconKind.Paused);
            AddProcStateImage("proc-single", ProcTreeIconKind.SingleStep);
            AddProcStateImage("proc-alarm", ProcTreeIconKind.Alarming);
            AddProcStateImage("proc-pausing", ProcTreeIconKind.Pausing);
            AddProcStateImage("proc-stopping", ProcTreeIconKind.Stopping);
            AddProcStateImage("disabled", ProcTreeIconKind.Disabled);
            AddProcStateImage("step", ProcTreeIconKind.Step);
            AddProcStateImage("step-running", ProcTreeIconKind.StepRunning);
            AddProcStateImage("step-paused", ProcTreeIconKind.StepPaused);
            AddProcStateImage("step-single", ProcTreeIconKind.StepSingle);
            AddProcStateImage("step-alarm", ProcTreeIconKind.StepAlarming);
            proc_treeView.ImageList = procStateImages;
            ApplyNativeTreeDoubleBuffer();
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
            if (proc?.head?.Disable == true)
            {
                return "disabled";
            }
            switch (snapshot?.State ?? ProcRunState.Stopped)
            {
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
                default:
                    return "proc-stopped";
            }
        }

        private static string GetStepImageKey(Proc proc, Step step, int stepIndex, EngineSnapshot snapshot)
        {
            if (proc?.head?.Disable == true || step?.Disable == true)
            {
                return "disabled";
            }
            if (snapshot == null || snapshot.State == ProcRunState.Stopped || snapshot.StepIndex != stepIndex)
            {
                return "step";
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
            if (!SF.CanEditProcStructure())
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
            ProcessDefinitionService.NormalizeProc(insertIndex, proc, errors);
            if (errors.Count > 0)
            {
                MessageBox.Show(string.Join("\r\n", errors.Distinct()));
                return false;
            }
            procsList.Insert(insertIndex, proc);
            if (!RebuildWorkConfig(insertIndex))
            {
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
            if (!ProcessEditingService.TryCommitProcDraft(procIndex, draft, out string commitError))
            {
                MessageBox.Show(commitError, "新增步骤失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            return true;
        }
        public bool RebuildWorkConfig(int startIndex = 0)
        {
            bool success = ProcessConfigStore.Rebuild(SF.workPath, procsList, startIndex,
                out string error, out bool rollbackFailed);
            if (!success)
            {
                RefreshProcList();
                if (rollbackFailed)
                {
                    SF.SetSecurityLock(error);
                }
                MessageBox.Show(error, "流程配置提交失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            RefreshProcList();
            return true;
        }
        public void RefreshProcList()
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

            List<Proc> procsListTemp = new List<Proc>();
            List<string> loadErrors = new List<string>();

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
            SF.ProcConfigFaulted = false;

            string path = SF.workPath.TrimEnd('\\');

            if (!ProcessConfigStore.RecoverIfNeeded(path, out string recoveryMessage))
            {
                loadErrors.Add(recoveryMessage);
            }
            else if (!string.IsNullOrWhiteSpace(recoveryMessage) && SF.frmInfo != null && !SF.frmInfo.IsDisposed)
            {
                SF.frmInfo.PrintInfo(recoveryMessage, FrmInfo.Level.Error);
            }

            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            Dictionary<int, string> indexMap = ProcessDefinitionService.BuildProcFileIndexMap(path, out int maxIndex);
            loadErrors.AddRange(ProcessDefinitionService.ValidateProcFileContinuity(indexMap, maxIndex));

            var procIds = new HashSet<Guid>();
            for (int i = 0; i <= maxIndex; i++)
            {
                Proc proc = null;
                if (indexMap.ContainsKey(i))
                {
                    proc = AtomicJsonFileStore.Read<Proc>(SF.workPath, i.ToString());
                }
                if (proc == null)
                {
                    loadErrors.Add($"流程文件加载失败：{i}.json");
                    proc = new Proc();
                }

                ProcessDefinitionService.NormalizeProc(i, proc, loadErrors);
                if (proc?.head?.Id != Guid.Empty && !procIds.Add(proc.head.Id))
                {
                    loadErrors.Add($"流程{i}的ID重复：{proc.head.Id:D}");
                }
                procsListTemp.Add(proc);

                EngineSnapshot procSnapshot = SF.DR?.GetSnapshot(i);
                TreeNode treeNode = new TreeNode(BuildProcNodeText(i, proc))
                {
                    NodeFont = procNodeFont
                };
                SetNodeImage(treeNode, GetProcImageKey(proc, procSnapshot));
                if (proc?.head?.Disable == true)
                {
                    treeNode.ForeColor = DisabledNodeColor;
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
                        TreeNode chnode = new TreeNode(BuildStepNodeText(i, j, proc, step))
                        {
                            NodeFont = stepNodeFont
                        };
                        chnode.Tag = step?.Id ?? Guid.Empty;
                        SetNodeImage(chnode, GetStepImageKey(proc, step, j, procSnapshot));
                        if (proc?.head?.Disable == true || proc.steps[j]?.Disable == true)
                        {
                            chnode.ForeColor = DisabledNodeColor;
                        }
                        proc_treeView.Nodes[i].Nodes.Add(chnode);
                    }
                }
            }
            procsList = procsListTemp;
            RestoreTreeState(selectedProcId, selectedStepId, topProcId, expandedProcIds);
            }
            finally
            {
                proc_treeView.EndUpdate();
                restoringTreeState = false;
            }

            if (SF.DR?.Context != null)
            {
                bool allStopped = true;
                int runtimeCount = SF.DR.Context.Procs?.Count ?? 0;
                for (int i = 0; i < runtimeCount; i++)
                {
                    EngineSnapshot snapshot = SF.DR.GetSnapshot(i);
                    if (snapshot != null && snapshot.State != ProcRunState.Stopped)
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
                    SF.DR.Context.Procs = runtimeProcs;
                    SF.DR.ClearPendingProcUpdates();
                }
            }
            if (loadErrors.Count > 0)
            {
                SF.ProcConfigFaulted = true;
                string reason = "流程配置加载失败，所有流程已停止且禁止启动。请处理以下报警：\r\n"
                    + string.Join("\r\n", loadErrors.Distinct());
                SF.StopAllProcs(reason);
                MessageBox.Show(reason, "流程配置错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
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
                procNode.ForeColor = proc?.head?.Disable == true ? DisabledNodeColor : proc_treeView.ForeColor;

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
                        var stepNode = new TreeNode(BuildStepNodeText(procIndex, i, proc, step))
                        {
                            Tag = step?.Id ?? Guid.Empty,
                            NodeFont = stepNodeFont,
                            ForeColor = proc?.head?.Disable == true || step?.Disable == true
                                ? DisabledNodeColor
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
                        procNode.Nodes[i].Text = BuildStepNodeText(procIndex, i, proc, step);
                        procNode.Nodes[i].NodeFont = stepNodeFont;
                        procNode.Nodes[i].ForeColor = proc?.head?.Disable == true || step?.Disable == true
                            ? DisabledNodeColor
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
            UpdateProcStateIcons(procIndex, SF.DR?.GetSnapshot(procIndex));
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
            procFlashColor = kind == ProcChangeKind.Added ? Color.LightGreen
                           : kind == ProcChangeKind.Deleted ? Color.LightPink
                           : Color.Khaki;
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
            InstructionListView grid = SF.frmDataGrid?.dataGridView1;
            FrmPropertyGrid propertyEditor = SF.frmPropertyGrid;
            propertyEditor?.BeginVisualUpdate();
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
            if (SelectedStepNum >= 0 && SelectedStepNum < procsList[SelectedProcNum].steps.Count)
            {
                grid?.SetFlowContext(SelectedProcNum, SelectedStepNum, procsList[SelectedProcNum]);
                bindingSource.DataSource = procsList[SelectedProcNum].steps[SelectedStepNum].Ops;
                if (SF.frmPropertyGrid != null && !SF.frmPropertyGrid.IsDisposed)
                {
                    SF.frmPropertyGrid.propertyGrid1.SelectedObject = procsList[SelectedProcNum].steps[SelectedStepNum];
                }
            }
            else
            {
                grid?.SetFlowContext(SelectedProcNum, -1, procsList[SelectedProcNum]);
                bindingSource.DataSource = null;
                if (SF.frmPropertyGrid != null && !SF.frmPropertyGrid.IsDisposed)
                {
                    SF.frmPropertyGrid.propertyGrid1.SelectedObject = procsList[SelectedProcNum].head;
                }
            }
            bindingSource.ResetBindings(false);
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
                        grid.SelectSingle(selectedRowIndex);
                        SF.frmDataGrid.iSelectedRow = selectedRowIndex;
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
            }
            finally
            {
                propertyEditor?.EndVisualUpdate();
            }
        }

        private void AddProc_Click(object sender, EventArgs e)
        {   
            if (!SF.CanEditProcStructure())
            {
                return;
            }
            int selectedProcIndex = SelectedProcNum;
            var draftHead = new ProcHead();

            proc_treeView.Enabled = false;
            SF.BeginEditSession(new EditSession<ProcHead>("新增流程", draftHead,
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
                if (!SF.CanEditProc(SF.frmProc.SelectedProcNum))
                {
                    return;
                }
                int procIndex = SelectedProcNum;
                int selectedStepIndex = SelectedStepNum;
                var draftStep = new Step();
                proc_treeView.Enabled = false;
                SF.BeginEditSession(new EditSession<Step>("新增步骤", draftStep,
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
                if (!SF.CanEditProcStructure())
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
                string warnMsg = $"警告：即将删除流程【{procName}】\r\n此操作不可恢复，确认删除？";
                DialogResult result = MessageBox.Show(
                    this,
                    warnMsg,
                    "删除流程确认",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);
                if (result != DialogResult.Yes)
                {
                    return;
                }
                if (procIndex < 0 || procIndex >= procsList.Count)
                {
                    return;
                }
                procsList.RemoveAt(procIndex);
                if (!RebuildWorkConfig(procIndex))
                {
                    return;
                }
            }
            else
            {
                if (!SF.CanEditProc(SelectedProcNum))
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
                string warnMsg = $"警告：即将删除步骤【{stepName}】\r\n所属流程：【{procName}】\r\n此操作不可恢复，确认删除？";
                DialogResult result = MessageBox.Show(
                    this,
                    warnMsg,
                    "删除步骤确认",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);
                if (result != DialogResult.Yes)
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
                if (!ProcessEditingService.TryCommitProcDraft(procIndex, draft, out string commitError))
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

                    SF.frmDataGrid.iSelectedRow = -1;

                    if (SelectedProcNum < 0 || SelectedProcNum >= procsList.Count)
                    {
                        MessageBox.Show("流程索引无效，无法加载步骤。");
                        SelectedProcNum = -1;
                        SelectedStepNum = -1;
                        bindingSource.DataSource = null;
                        SF.frmDataGrid.dataGridView1.SetFlowContext(-1, -1, null);
                        SF.frmDataGrid.dataGridView1.DataSource = null;
                        SF.frmPropertyGrid.propertyGrid1.SelectedObject = null;
                        return;
                    }
                    if (SelectedStepNum < 0 || SelectedStepNum >= procsList[SelectedProcNum].steps.Count)
                    {
                        MessageBox.Show("步骤索引无效，无法加载指令。");
                        SelectedStepNum = -1;
                        bindingSource.DataSource = null;
                        SF.frmDataGrid.dataGridView1.SetFlowContext(SelectedProcNum, -1, procsList[SelectedProcNum]);
                        SF.frmDataGrid.dataGridView1.DataSource = null;
                        SF.frmPropertyGrid.propertyGrid1.SelectedObject = null;
                        return;
                    }
                    SF.frmDataGrid.dataGridView1.SetFlowContext(
                        SelectedProcNum,
                        SelectedStepNum,
                        procsList[SelectedProcNum]);
                    bindingSource.DataSource = procsList[SelectedProcNum].steps[SelectedStepNum].Ops;

                    SF.frmPropertyGrid.propertyGrid1.SelectedObject =procsList[SelectedProcNum].steps[SF.frmProc.SelectedStepNum];


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
                        SF.frmDataGrid.dataGridView1.SetFlowContext(-1, -1, null);
                        SF.frmDataGrid.dataGridView1.DataSource = null;
                        SF.frmPropertyGrid.propertyGrid1.SelectedObject = null;
                        return;
                    }
                    SF.frmDataGrid.dataGridView1.SetFlowContext(
                        SelectedProcNum,
                        -1,
                        procsList[SelectedProcNum]);
                    bindingSource.DataSource = null;

                    SF.frmPropertyGrid.propertyGrid1.SelectedObject = procsList[SelectedProcNum].head;
                    SF.frmPropertyGrid.propertyGrid1.ExpandAllGridItems();


                }
                SF.frmDataGrid.dataGridView1.DataSource = bindingSource;

                if (SF.DR != null && SF.frmProc.SelectedProcNum != -1)
                {
                    EngineSnapshot snapshot = SF.DR.GetSnapshot(SF.frmProc.SelectedProcNum);
                    if (snapshot != null && (snapshot.State == ProcRunState.Running || snapshot.State == ProcRunState.Alarming))
                    {
                        SF.frmToolBar.btnPause.Text = "暂停";
                    }
                    else if (snapshot != null && (snapshot.State == ProcRunState.Paused || snapshot.State == ProcRunState.SingleStep))
                    {
                        SF.frmToolBar.btnPause.Text = "继续";
                    }
                    else
                    {
                        SF.frmToolBar.btnPause.Text = "暂停";
                    }
                    if (snapshot != null)
                    {
                        SF.frmToolBar.btnPause.Enabled = snapshot.State != ProcRunState.Paused;
                        SF.frmToolBar.SingleRun.Enabled = snapshot.State == ProcRunState.SingleStep;
                    }
                    else
                    {
                        SF.frmToolBar.btnPause.Enabled = true;
                        SF.frmToolBar.SingleRun.Enabled = true;
                    }
                }
                else
                {
                    SF.frmToolBar.btnPause.Text = "暂停";
                    SF.frmToolBar.btnPause.Enabled = true;
                    SF.frmToolBar.SingleRun.Enabled = true;
                }
              
            }
        }

        private void proc_treeView_BeforeSelect(object sender, TreeViewCancelEventArgs e)
        {
            if (restoringTreeState)
            {
                return;
            }
            if (SF.isAddOps || SF.isModify == ModifyKind.Operation)
            {
                if (proc_treeView.SelectedNode != e.Node)
                {
                    e.Cancel = true;
                    if (SF.frmInfo != null && !SF.frmInfo.IsDisposed)
                    {
                        SF.frmInfo.PrintInfo("新增或编辑指令中，禁止切换流程。", FrmInfo.Level.Error);
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
            if (SF.isAddOps || SF.isModify == ModifyKind.Operation)
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
                        if (SF.frmInfo != null && !SF.frmInfo.IsDisposed)
                        {
                            SF.frmInfo.PrintInfo("新增或编辑指令中，禁止切换流程。", FrmInfo.Level.Error);
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
                    SF.frmDataGrid.iSelectedRow = -1;
                    SF.frmDataGrid.OperationTemp = null;
                    bindingSource.DataSource = null;
                    SF.frmDataGrid.dataGridView1.SetFlowContext(-1, -1, null);
                    SF.frmDataGrid.dataGridView1.DataSource = null;
                    SF.frmPropertyGrid.propertyGrid1.SelectedObject = null;
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
            if (!SF.CanEditProc(SF.frmProc.SelectedProcNum))
            {
                return;
            }
            int procIndex = SelectedProcNum;
            int stepIndex = SelectedStepNum;
            object selected = SF.frmPropertyGrid.propertyGrid1.SelectedObject;
            proc_treeView.Enabled = false;
            if (selected is ProcHead sourceHead)
            {
                ProcHead draft = ObjectGraphCloner.Clone(sourceHead);
                SF.BeginEditSession(new EditSession<ProcHead>("修改流程", draft,
                    item => string.IsNullOrWhiteSpace(item.Name) ? "流程名称为空。" : null,
                    item =>
                    {
                        Proc procDraft = ObjectGraphCloner.Clone(procsList[procIndex]);
                        procDraft.head = item;
                        if (!ProcessEditingService.TryCommitProcDraft(procIndex, procDraft, out string commitError))
                        {
                            throw new InvalidOperationException(commitError);
                        }
                        proc_treeView.Enabled = true;
                    }, () => proc_treeView.Enabled = true));
            }
            else if (selected is Step sourceStep && stepIndex >= 0)
            {
                Step draft = ObjectGraphCloner.Clone(sourceStep);
                SF.BeginEditSession(new EditSession<Step>("修改步骤", draft,
                    item => string.IsNullOrWhiteSpace(item.Name) ? "步骤名称为空。" : null,
                    item =>
                    {
                        Proc procDraft = ObjectGraphCloner.Clone(procsList[procIndex]);
                        procDraft.steps[stepIndex] = item;
                        if (!ProcessEditingService.TryCommitProcDraft(procIndex, procDraft, out string commitError))
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
                string procName = procsList[procIndex]?.head?.Name;
                if (string.IsNullOrWhiteSpace(procName))
                {
                    procName = "未命名";
                }
                string message = $"确认启动流程：{procIndex}-{procName}";
                Message confirmForm = new Message("启动确认", message,
                    () => SF.DR.StartProc(null, procIndex),
                    () => { },
                    "启动", "取消", false);
                confirmForm.txtMsg.Font = new Font("微软雅黑", 20F, FontStyle.Bold);
                confirmForm.txtMsg.ForeColor = Color.Red;
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
            if (!SF.CanEditProcStructure())
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
            List<string> errors = new List<string>();
            ProcessDefinitionService.NormalizeProc(insertIndex, newProc, errors);
            if (errors.Count > 0)
            {
                MessageBox.Show(string.Join("\r\n", errors.Distinct()));
                return;
            }
            procsList.Insert(insertIndex, newProc);
            if (!RebuildWorkConfig(insertIndex))
            {
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
            if (!SF.CanEditProc(SelectedProcNum))
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
            if (!ProcessEditingService.TryCommitProcDraft(procIndex, draft, out string commitError))
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
                if (SF.frmDataGrid != null) SF.frmDataGrid.iSelectedRow = -1;
                return;
            }
            SelectProcNode(procIndex, stepIndex);
            if (SF.frmDataGrid != null) SF.frmDataGrid.iSelectedRow = -1;
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
            if (!SF.CanEditProc(procIndex))
            {
                return;
            }
            EngineSnapshot snapshot = SF.DR?.GetSnapshot(procIndex);
            if (snapshot != null && snapshot.State != ProcRunState.Stopped)
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
            if (!ProcessEditingService.TryCommitProcDraft(procIndex, draft, out string commitError))
            {
                MessageBox.Show(commitError, "更新禁用状态失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void UpdateProcNodeStyle(int procIndex)
        {
            if (procIndex < 0 || procIndex >= proc_treeView.Nodes.Count || procIndex >= procsList.Count)
            {
                return;
            }
            bool disabled = procsList[procIndex]?.head?.Disable == true;
            TreeNode node = proc_treeView.Nodes[procIndex];
            if (node != null)
            {
                node.ForeColor = disabled ? DisabledNodeColor : Color.Black;
                node.Text = BuildProcNodeText(procIndex);
                node.NodeFont = procNodeFont;
                SetNodeImage(node, GetProcImageKey(procsList[procIndex], SF.DR?.GetSnapshot(procIndex)));
            }
        }

        private void UpdateStepNodeStyle(int procIndex, int stepIndex)
        {
            if (procIndex < 0 || procIndex >= proc_treeView.Nodes.Count || procIndex >= procsList.Count)
            {
                return;
            }
            if (stepIndex < 0 || stepIndex >= procsList[procIndex].steps.Count)
            {
                return;
            }
            if (stepIndex >= proc_treeView.Nodes[procIndex].Nodes.Count)
            {
                return;
            }
            bool disabled = procsList[procIndex]?.head?.Disable == true || procsList[procIndex].steps[stepIndex]?.Disable == true;
            TreeNode node = proc_treeView.Nodes[procIndex].Nodes[stepIndex];
            if (node != null)
            {
                node.ForeColor = disabled ? DisabledNodeColor : Color.Black;
                node.Text = BuildStepNodeText(procIndex, stepIndex);
                node.NodeFont = stepNodeFont;
                SetNodeImage(
                    node,
                    GetStepImageKey(
                        procsList[procIndex],
                        procsList[procIndex].steps[stepIndex],
                        stepIndex,
                        SF.DR?.GetSnapshot(procIndex)));
            }
        }

        private void UpdateStepNodeStylesForProc(int procIndex)
        {
            if (procIndex < 0 || procIndex >= procsList.Count)
            {
                return;
            }
            int stepCount = procsList[procIndex].steps.Count;
            for (int i = 0; i < stepCount; i++)
            {
                UpdateStepNodeStyle(procIndex, i);
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
            Proc proc = procsList[procIndex];
            return BuildStepNodeText(procIndex, stepIndex, proc, step);
        }

        private string BuildProcNodeText(int procIndex, Proc proc)
        {
            if (procIndex < 0)
            {
                return string.Empty;
            }
            string procName = ResolveProcName(procIndex, proc, null);
            bool disabled = proc?.head?.Disable == true;
            return BuildProcNodeTextCore(procIndex, procName, disabled, string.Empty);
        }

        internal string BuildProcNodeTextWithState(int procIndex, Proc proc, EngineSnapshot snapshot)
        {
            if (procIndex < 0)
            {
                return string.Empty;
            }
            string procName = ResolveProcName(procIndex, proc, snapshot);
            bool disabled = proc?.head?.Disable == true;
            string suffix = string.Empty;
            if (snapshot != null)
            {
                suffix = $"|{GetProcStateText(snapshot.State)}";
                if (snapshot.IsBreakpoint)
                {
                    suffix += "|断点";
                }
            }
            return BuildProcNodeTextCore(procIndex, procName, disabled, suffix);
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

        private string BuildProcNodeTextCore(int procIndex, string procName, bool disabled, string suffix)
        {
            if (procIndex < 0)
            {
                return string.Empty;
            }
            string safeName = string.IsNullOrWhiteSpace(procName) ? $"流程{procIndex}" : procName;
            string tag = disabled ? DisabledTag : string.Empty;
            string text = $"{procIndex}：{tag}{safeName}";
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

        private static string GetProcStateText(ProcRunState state)
        {
            switch (state)
            {
                case ProcRunState.Stopped:
                    return "停止";
                case ProcRunState.Paused:
                    return "暂停";
                case ProcRunState.SingleStep:
                    return "单步";
                case ProcRunState.Running:
                    return "运行";
                case ProcRunState.Alarming:
                    return "报警中";
                case ProcRunState.Pausing:
                    return "暂停中";
                case ProcRunState.Stopping:
                    return "停止中";
                default:
                    return "未知";
            }
        }

        private string BuildStepNodeText(int procIndex, int stepIndex, Proc proc, Step step)
        {
            if (procIndex < 0 || stepIndex < 0)
            {
                return string.Empty;
            }
            string stepName = string.IsNullOrWhiteSpace(step?.Name) ? $"步骤{stepIndex}" : step.Name;
            bool disabled = proc?.head?.Disable == true || step?.Disable == true;
            string tag = disabled ? DisabledTag : string.Empty;
            return $"{stepIndex}：{tag}{stepName}";
        }
        public Tuple<int, int, int> FindOperationTypeIndex(OperationType hash)
        {
            
            for (int procIndex = 0; procIndex < procsList.Count; procIndex++)
            {
                Proc proc = procsList[procIndex];
                for (int stepIndex = 0; stepIndex < proc.steps.Count; stepIndex++)
                {
                    Step step = proc.steps[stepIndex];
                    for (int opIndex = 0; opIndex < step.Ops.Count; opIndex++)
                    {
                        if (step.Ops[opIndex] == hash)
                        {
                            return new Tuple<int, int, int>(procIndex, stepIndex, opIndex);
                        }
                    }
                }
            }
            return new Tuple<int, int, int>(-1, -1, -1);
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
