using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Drawing.Drawing2D;
using static Automation.OperationTypePartial;

namespace Automation
{
    public partial class FrmDataGrid : Form
    {
        internal const string OperationAddressDragFormat = "Automation.OperationAddress";

        private const int MenuIconSize = 24;
        private const int MenuIconRenderSize = 18;
        private const int EmLineScroll = 0x00B6;

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr windowHandle, int message, IntPtr wordParameter, IntPtr longParameter);

        // 当前检查器中的指令；只读查看引用源对象，进入编辑时替换为隔离草稿。
        public OperationType OperationTemp;
        //鼠标选定的行数
        public int iSelectedRow = -1;

        private int lastHighlightedRow = -1;
        private int lastHighlightedProc = -1;
        private int lastHighlightedStep = -1;
        private ProcRunState lastHighlightedState = ProcRunState.Stopped;
        private bool lastHighlightedBreakpoint;
        private bool lastHighlightActive = false;
        private bool singleStepFollowPending;
        private int singleStepFollowProcIndex = -1;
        private bool contextMenuByMouse = false;
        private int contextMenuRowIndex = -1;
        private readonly ToolStripMenuItem viewCustomFunctionCode = new ToolStripMenuItem();

        // 数据网格变动动效：AI 改动当前显示的流程后，闪烁整个网格提示用户。
        private System.Windows.Forms.Timer gridFlashTimer;
        private Color gridFlashColor;
        private int gridFlashCount;
        private const int GridFlashMaxCount = 6;
        // 行级闪烁目标列表：(行索引, 颜色)。为空时闪烁整个 grid。
        private List<(int rowIndex, Color color)> flashTargetRows;
        //记录要复制行的index
        public List<int> selectedRowIndexes4Copy = new List<int>();
        //
        public List<int> selectedRowIndexes4Del = new List<int>();

        public FrmDataGrid()
        {
            InitializeComponent();
            Disposed += FrmDataGrid_Disposed;
            InitContextMenuIcons();
            ConfigureContextMenu();
            contextMenuStrip2.KeyDown += contextMenuStrip2_KeyDown;
            contextMenuStrip2.Opening += contextMenuStrip2_Opening;
            dataGridView1.SelectedIndexChanged += dataGridView1_SelectedIndexChanged;
            dataGridView1.JumpLinkClicked += dataGridView1_JumpLinkClicked;
            dataGridView1.MouseMove += dataGridView1_MouseMove;
            dataGridView1.MouseUp += dataGridView1_MouseUp;
        }

        private void dataGridView1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (IsOperationAddressDragEnabled())
            {
                return;
            }
            int rowIndex = dataGridView1.CurrentIndex;
            iSelectedRow = rowIndex;
            if (rowIndex >= 0
                && SF.frmProc != null
                && SF.frmInspector != null
                && !SF.frmInspector.IsDisposed)
            {
                // 先把列表选中态立即绘制出来，再同步更新检查器；检查器内部按对象身份去重。
                dataGridView1.Update();
                ShowOperationProperties(rowIndex);
            }
        }

        private void dataGridView1_JumpLinkClicked(
            object sender,
            InstructionListView.JumpLinkClickedEventArgs e)
        {
            if (SF.frmProc == null
                || SF.frmProc.IsDisposed
                || e.ProcIndex < 0
                || e.ProcIndex >= SF.frmProc.procsList.Count
                || e.ProcIndex != SF.frmProc.SelectedProcNum)
            {
                return;
            }

            int targetStepIndex = e.IsOutgoing ? e.TargetStepIndex : e.SourceStepIndex;
            int targetOpIndex = e.IsOutgoing ? e.TargetOpIndex : e.SourceOpIndex;
            Proc proc = SF.frmProc.procsList[e.ProcIndex];
            if (targetStepIndex < 0
                || targetStepIndex >= (proc?.steps?.Count ?? 0)
                || targetOpIndex < 0
                || targetOpIndex >= (proc.steps[targetStepIndex]?.Ops?.Count ?? 0))
            {
                return;
            }

            FrmInspector inspector = SF.frmInspector;
            inspector?.BeginUpdate();
            try
            {
                dataGridView1.BeginLinkedNavigation();
                try
                {
                    if (SF.frmProc.SelectedStepNum != targetStepIndex)
                    {
                        SelectChildNode(e.ProcIndex, targetStepIndex);
                    }
                    if (SF.frmProc.SelectedProcNum != e.ProcIndex
                        || SF.frmProc.SelectedStepNum != targetStepIndex
                        || targetOpIndex >= dataGridView1.OperationCount)
                    {
                        return;
                    }

                    dataGridView1.SelectSingle(targetOpIndex);
                    iSelectedRow = targetOpIndex;
                    dataGridView1.EnsureIndexVisible(targetOpIndex);
                }
                finally
                {
                    dataGridView1.EndLinkedNavigation();
                }
            }
            finally
            {
                inspector?.EndUpdate();
            }
        }

        private void InitContextMenuIcons()
        {
            SetStartOps.Image = CreateMenuIcon(MenuIconType.StartPoint);
            viewCustomFunctionCode.Image = CreateMenuIcon(MenuIconType.Code);
            Add.Image = CreateMenuIcon(MenuIconType.Add);
            Modify.Image = CreateMenuIcon(MenuIconType.Edit);
            SetStopPoint.Image = CreateMenuIcon(MenuIconType.Breakpoint);
            Enable.Image = CreateMenuIcon(MenuIconType.Toggle);
            Delete.Image = CreateMenuIcon(MenuIconType.Delete);
            copy.Image = CreateMenuIcon(MenuIconType.Copy);
            paste.Image = CreateMenuIcon(MenuIconType.Paste);
            Others.Image = CreateMenuIcon(MenuIconType.More);
            CProgramCopy.Image = CreateMenuIcon(MenuIconType.Copy);
            CProgramPaste.Image = CreateMenuIcon(MenuIconType.Paste);
            SetStopPoint.ShortcutKeyDisplayString = "X";
            Enable.ShortcutKeyDisplayString = "U";
            copy.ShortcutKeyDisplayString = "Ctrl+C";
            paste.ShortcutKeyDisplayString = "Ctrl+V";
            Delete.ShortcutKeyDisplayString = "Del";
        }

        private enum MenuIconType
        {
            StartPoint,
            Add,
            Edit,
            Breakpoint,
            Toggle,
            Delete,
            Copy,
            Paste,
            More,
            Code
        }

        private static Bitmap CreateMenuIcon(MenuIconType iconType)
        {
            Bitmap bitmap = new Bitmap(MenuIconSize, MenuIconSize);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                g.TranslateTransform(2F, 2F);
                Color color = GetMenuIconColor(iconType);
                using (Pen pen = new Pen(color, 1.8F))
                using (Brush brush = new SolidBrush(color))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    pen.LineJoin = LineJoin.Round;
                    switch (iconType)
                    {
                        case MenuIconType.StartPoint:
                            g.DrawLine(pen, 6, 3, 6, 17);
                            g.FillPolygon(brush, new[] { new Point(6, 4), new Point(15, 7), new Point(6, 10) });
                            break;
                        case MenuIconType.Add:
                            g.DrawLine(pen, 10, 4, 10, 16);
                            g.DrawLine(pen, 4, 10, 16, 10);
                            break;
                        case MenuIconType.Edit:
                            g.DrawLine(pen, 5, 15, 15, 5);
                            g.FillPolygon(brush, new[] { new Point(14, 4), new Point(17, 3), new Point(16, 6) });
                            break;
                        case MenuIconType.Breakpoint:
                            g.DrawEllipse(pen, 4, 4, 12, 12);
                            g.FillEllipse(brush, 8, 8, 4, 4);
                            break;
                        case MenuIconType.Toggle:
                            g.DrawArc(pen, 4, 4, 12, 12, 40, 280);
                            g.DrawLine(pen, 10, 2, 10, 8);
                            break;
                        case MenuIconType.Delete:
                            g.DrawLine(pen, 5, 6, 15, 6);
                            g.DrawLine(pen, 8, 3, 12, 3);
                            g.DrawRectangle(pen, 6, 7, 8, 10);
                            g.DrawLine(pen, 9, 9, 9, 15);
                            g.DrawLine(pen, 12, 9, 12, 15);
                            break;
                        case MenuIconType.Copy:
                            DrawMenuRoundedRectangle(g, pen, new Rectangle(7, 4, 10, 12), 2);
                            DrawMenuRoundedRectangle(g, pen, new Rectangle(3, 8, 10, 10), 2);
                            break;
                        case MenuIconType.Paste:
                            DrawMenuRoundedRectangle(g, pen, new Rectangle(4, 5, 12, 13), 2);
                            g.DrawLine(pen, 8, 3, 12, 3);
                            g.DrawLine(pen, 8, 3, 7, 6);
                            g.DrawLine(pen, 12, 3, 13, 6);
                            g.DrawLine(pen, 7, 10, 13, 10);
                            g.DrawLine(pen, 7, 14, 11, 14);
                            break;
                        case MenuIconType.More:
                            g.FillEllipse(brush, 3, 8, 4, 4);
                            g.FillEllipse(brush, 8, 8, 4, 4);
                            g.FillEllipse(brush, 13, 8, 4, 4);
                            break;
                        case MenuIconType.Code:
                            g.DrawLines(pen, new[] { new Point(8, 5), new Point(4, 10), new Point(8, 15) });
                            g.DrawLines(pen, new[] { new Point(12, 5), new Point(16, 10), new Point(12, 15) });
                            break;
                    }
                }
            }
            return bitmap;
        }

        private static void DrawMenuRoundedRectangle(
            Graphics graphics,
            Pen pen,
            Rectangle bounds,
            int radius)
        {
            int diameter = radius * 2;
            using (var path = new GraphicsPath())
            {
                path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
                path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
                path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
                path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
                path.CloseFigure();
                graphics.DrawPath(pen, path);
            }
        }

        private static Color GetMenuIconColor(MenuIconType iconType)
        {
            switch (iconType)
            {
                case MenuIconType.Add: return UiPalette.Success;
                case MenuIconType.Edit: return UiPalette.Brand;
                case MenuIconType.Delete:
                case MenuIconType.Breakpoint: return UiPalette.Danger;
                case MenuIconType.Toggle: return UiPalette.Warning;
                case MenuIconType.StartPoint: return UiPalette.JumpCancel;
                case MenuIconType.More: return UiPalette.JumpCancel;
                case MenuIconType.Paste: return UiPalette.JumpAutomatic;
                default: return UiPalette.TextSecondary;
            }
        }

        private void ConfigureContextMenu()
        {
            contextMenuStrip2.SuspendLayout();
            try
            {
                contextMenuStrip2.Items.Clear();
                contextMenuStrip2.AutoSize = true;
                contextMenuStrip2.MinimumSize = new Size(180, 0);
                contextMenuStrip2.Padding = new Padding(3, 3, 3, 3);
                contextMenuStrip2.ImageScalingSize = new Size(20, 20);
                contextMenuStrip2.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular);
                contextMenuStrip2.BackColor = UiPalette.SurfaceStrong;
                contextMenuStrip2.ForeColor = UiPalette.TextPrimary;
                contextMenuStrip2.Renderer = new InstructionContextMenuRenderer();
                contextMenuStrip2.ShowCheckMargin = false;
                contextMenuStrip2.ShowImageMargin = true;

                Add.Text = "新建指令";
                Modify.Text = "编辑指令";
                Delete.Text = "删除指令";
                copy.Text = "复制指令";
                paste.Text = "粘贴指令";
                Others.Text = "其他";
                CProgramCopy.Text = "复制到跨程序剪贴板";
                CProgramPaste.Text = "从跨程序剪贴板粘贴";
                SetStartOps.Text = "设为调试启动点";
                viewCustomFunctionCode.Text = "查看代码";
                viewCustomFunctionCode.Visible = false;
                viewCustomFunctionCode.Click += ViewCustomFunctionCode_Click;
                Delete.ForeColor = UiPalette.Danger;

                ApplyContextMenuItemStyle(new[]
                {
                    Add, Modify, Delete, copy, paste, Others,
                    CProgramCopy, CProgramPaste, SetStartOps, SetStopPoint, Enable,
                    viewCustomFunctionCode
                });

                contextMenuStrip2.Items.Add(Add);
                contextMenuStrip2.Items.Add(Modify);
                contextMenuStrip2.Items.Add(viewCustomFunctionCode);
                contextMenuStrip2.Items.Add(CreateContextMenuSeparator());
                contextMenuStrip2.Items.Add(copy);
                contextMenuStrip2.Items.Add(paste);
                contextMenuStrip2.Items.Add(Others);
                separatorStartOps.AutoSize = false;
                separatorStartOps.Size = new Size(202, 5);
                separatorStartOps.Margin = Padding.Empty;
                contextMenuStrip2.Items.Add(separatorStartOps);
                contextMenuStrip2.Items.Add(SetStartOps);
                contextMenuStrip2.Items.Add(SetStopPoint);
                contextMenuStrip2.Items.Add(Enable);
                contextMenuStrip2.Items.Add(CreateContextMenuSeparator());
                contextMenuStrip2.Items.Add(Delete);
            }
            finally
            {
                contextMenuStrip2.ResumeLayout(false);
            }
        }

        private static void ApplyContextMenuItemStyle(IEnumerable<ToolStripMenuItem> items)
        {
            foreach (ToolStripMenuItem item in items)
            {
                item.AutoSize = true;
                item.Padding = new Padding(4, 2, 5, 2);
                item.Margin = Padding.Empty;
            }
        }

        private static ToolStripSeparator CreateContextMenuSeparator()
        {
            return new ToolStripSeparator
            {
                AutoSize = false,
                Size = new Size(202, 5),
                Margin = Padding.Empty
            };
        }

        private sealed class InstructionContextMenuRenderer : ToolStripProfessionalRenderer
        {
            public InstructionContextMenuRenderer()
                : base(new InstructionContextMenuColorTable())
            {
                RoundedEdges = false;
            }

            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                if (!e.Item.Selected)
                {
                    return;
                }
                Rectangle bounds = new Rectangle(3, 2, Math.Max(1, e.Item.Width - 6), Math.Max(1, e.Item.Height - 4));
                bool destructive = e.Item.ForeColor.R > 160 && e.Item.ForeColor.G < 90;
                Color background = destructive
                    ? UiPalette.DangerSoft
                    : UiPalette.BrandSoft;
                Color accent = destructive
                    ? UiPalette.Danger
                    : UiPalette.Brand;
                using (GraphicsPath path = CreateMenuItemPath(bounds, 5))
                using (SolidBrush brush = new SolidBrush(background))
                using (SolidBrush accentBrush = new SolidBrush(accent))
                {
                    e.Graphics.FillPath(brush, path);
                    e.Graphics.FillRectangle(accentBrush, bounds.Left, bounds.Top + 5, 3, Math.Max(1, bounds.Height - 10));
                }
            }

            protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
            {
                if (e.Image == null)
                {
                    return;
                }

                int x = 7;
                int y = Math.Max(0, (e.Item.Height - MenuIconRenderSize) / 2);
                if (e.Item.Enabled)
                {
                    e.Graphics.DrawImage(e.Image, new Rectangle(x, y, MenuIconRenderSize, MenuIconRenderSize));
                }
                else
                {
                    using (var disabledImage = new Bitmap(MenuIconRenderSize, MenuIconRenderSize))
                    using (Graphics graphics = Graphics.FromImage(disabledImage))
                    {
                        graphics.DrawImage(e.Image, new Rectangle(0, 0, MenuIconRenderSize, MenuIconRenderSize));
                        ControlPaint.DrawImageDisabled(e.Graphics, disabledImage, x, y, UiPalette.SurfaceStrong);
                    }
                }
            }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                if (e.Item is ToolStripMenuItem)
                {
                    bool isMenuText = string.Equals(e.Text, e.Item.Text, StringComparison.Ordinal);
                    e.TextRectangle = isMenuText
                        ? new Rectangle(35, 0, Math.Max(1, e.Item.Width - 78), e.Item.Height)
                        : new Rectangle(
                            e.TextRectangle.X,
                            0,
                            e.TextRectangle.Width,
                            e.Item.Height);
                    e.TextFormat = (isMenuText ? TextFormatFlags.Left : TextFormatFlags.Right)
                        | TextFormatFlags.VerticalCenter
                        | TextFormatFlags.SingleLine
                        | TextFormatFlags.NoPadding;
                    e.TextColor = e.Item.Enabled ? e.Item.ForeColor : UiPalette.TextDisabled;
                }
                base.OnRenderItemText(e);
            }

            protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
            {
                int y = e.Item.Height / 2;
                using (Pen pen = new Pen(UiPalette.Divider))
                {
                    e.Graphics.DrawLine(pen, 12, y, Math.Max(12, e.Item.Width - 12), y);
                }
            }

            private static GraphicsPath CreateMenuItemPath(Rectangle bounds, int radius)
            {
                int diameter = radius * 2;
                var path = new GraphicsPath();
                path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
                path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
                path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
                path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
                path.CloseFigure();
                return path;
            }
        }

        private sealed class InstructionContextMenuColorTable : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground => UiPalette.SurfaceStrong;
            public override Color ImageMarginGradientBegin => UiPalette.Surface;
            public override Color ImageMarginGradientMiddle => UiPalette.Surface;
            public override Color ImageMarginGradientEnd => UiPalette.Surface;
            public override Color MenuBorder => UiPalette.Stroke;
            public override Color MenuItemBorder => Color.Transparent;
            public override Color MenuItemSelected => Color.Transparent;
            public override Color MenuItemPressedGradientBegin => UiPalette.BrandSoft;
            public override Color MenuItemPressedGradientMiddle => UiPalette.BrandSoft;
            public override Color MenuItemPressedGradientEnd => UiPalette.BrandSoft;
            public override Color SeparatorDark => UiPalette.Divider;
            public override Color SeparatorLight => UiPalette.SurfaceStrong;
        }

        private void contextMenuStrip2_Opening(object sender, CancelEventArgs e)
        {
            if (IsOperationAddressDragEnabled())
            {
                e.Cancel = true;
                return;
            }
            if (dataGridView1 == null)
            {
                return;
            }

            int rowIndex = -1;
            if (contextMenuByMouse)
            {
                rowIndex = contextMenuRowIndex;
            }
            else
            {
                Point clientPoint = dataGridView1.PointToClient(Cursor.Position);
                rowIndex = dataGridView1.IndexFromPoint(clientPoint);
                if (rowIndex < 0 && dataGridView1.CurrentIndex >= 0)
                {
                    rowIndex = dataGridView1.CurrentIndex;
                }
            }
            contextMenuByMouse = false;
            contextMenuRowIndex = -1;

            OperationType selectedOperation = null;
            if (rowIndex >= 0 && rowIndex < dataGridView1.OperationCount)
            {
                iSelectedRow = rowIndex;
                if (!dataGridView1.SelectedIndices.Contains(rowIndex))
                {
                    dataGridView1.SelectSingle(rowIndex);
                }
                selectedOperation = dataGridView1.GetOperation(rowIndex);
                if (selectedOperation != null)
                {
                    SetStopPoint.Text = selectedOperation.IsBreakpoint ? "取消断点" : "设置断点";
                    SetStopPoint.Checked = selectedOperation.IsBreakpoint;
                    Enable.Text = selectedOperation.Disable ? "启用指令" : "禁用指令";
                    Enable.Checked = false;
                }
            }
            else
            {
                iSelectedRow = -1;
                dataGridView1.ClearSelection();
                SetStopPoint.Text = "设置断点";
                SetStopPoint.Checked = false;
                Enable.Text = "禁用指令";
            }

            bool hasSelection = selectedOperation != null;
            viewCustomFunctionCode.Visible = selectedOperation is CallCustomFunc;
            bool hasStep = SF.frmProc != null
                && SF.frmProc.SelectedProcNum >= 0
                && SF.frmProc.SelectedProcNum < SF.frmProc.procsList.Count
                && SF.frmProc.SelectedStepNum >= 0
                && SF.frmProc.SelectedStepNum < SF.frmProc.procsList[SF.frmProc.SelectedProcNum].steps.Count;
            Add.Enabled = hasStep;
            Modify.Enabled = hasSelection;
            Delete.Enabled = hasSelection;
            copy.Enabled = hasSelection;
            paste.Enabled = hasStep && ListOperationType4Copy.Count > 0;
            CProgramCopy.Enabled = hasSelection;
            CProgramPaste.Enabled = hasStep && HasCrossProgramClipboardData();
            Others.Enabled = CProgramCopy.Enabled || CProgramPaste.Enabled;
            SetStartOps.Enabled = hasSelection;
            SetStopPoint.Enabled = hasSelection;
            Enable.Enabled = hasSelection;

            if (selectedOperation?.Disable == true)
            {
                Enable.ForeColor = UiPalette.Success;
            }
            else
            {
                Enable.ForeColor = UiPalette.TextPrimary;
            }
        }

        private void ViewCustomFunctionCode_Click(object sender, EventArgs e)
        {
            CallCustomFunc customFunction = iSelectedRow >= 0
                ? dataGridView1.GetOperation(iSelectedRow) as CallCustomFunc
                : null;
            if (customFunction == null || string.IsNullOrWhiteSpace(customFunction.FunctionName))
            {
                MessageBox.Show("当前指令未指定已注册的自定义函数。", "查看代码",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            const string resourceName = "Automation.Hmi.CustomFunctions.Source";
            Assembly entryAssembly = Assembly.GetEntryAssembly();
            Assembly platformAssembly = typeof(FrmDataGrid).Assembly;
            Stream sourceStream = entryAssembly?.GetManifestResourceStream(resourceName);
            if (sourceStream == null && !ReferenceEquals(entryAssembly, platformAssembly))
            {
                sourceStream = platformAssembly.GetManifestResourceStream(resourceName);
            }
            if (sourceStream == null)
            {
                MessageBox.Show("当前执行程序未包含自定义函数源码快照。", "查看代码",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string code;
            try
            {
                using (sourceStream)
                using (var reader = new StreamReader(sourceStream, Encoding.UTF8, true))
                {
                    code = reader.ReadToEnd();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("读取内嵌源码失败：" + ex.Message, "查看代码",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var codeBox = new RichTextBox
            {
                BackColor = UiPalette.Surface,
                BorderStyle = BorderStyle.None,
                DetectUrls = false,
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 10.5F, FontStyle.Regular),
                HideSelection = false,
                ReadOnly = true,
                Text = code,
                WordWrap = false
            };
            var sourceLabel = new Label
            {
                AutoEllipsis = true,
                BackColor = UiPalette.SurfaceStrong,
                Dock = DockStyle.Top,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular),
                ForeColor = UiPalette.TextSecondary,
                Height = 34,
                Padding = new Padding(10, 8, 10, 0),
                Text = "内嵌源码快照 · 构建时 Hmi/CustomFunctions.cs"
            };
            var viewer = new Form
            {
                BackColor = UiPalette.SurfaceStrong,
                MinimizeBox = false,
                MinimumSize = new Size(720, 480),
                ShowIcon = false,
                ShowInTaskbar = false,
                Size = new Size(920, 680),
                StartPosition = FormStartPosition.CenterParent,
                Text = "自定义函数代码 - " + customFunction.FunctionName
            };
            viewer.Controls.Add(codeBox);
            viewer.Controls.Add(sourceLabel);

            // RichTextBox 会把换行规范化为 CRLF，定位索引必须以控件实际文本为准。
            code = codeBox.Text;

            int registrationIndex = code.IndexOf(
                "RegisterCustomFunction(\"" + customFunction.FunctionName + "\"",
                StringComparison.Ordinal);
            if (registrationIndex >= 0)
            {
                registrationIndex += "RegisterCustomFunction(\"".Length;
            }
            else
            {
                registrationIndex = code.IndexOf("\"" + customFunction.FunctionName + "\"", StringComparison.Ordinal);
            }
            if (registrationIndex >= 0)
            {
                int targetIndex = registrationIndex;
                string targetDescription = customFunction.FunctionName + " 的注册语句";
                int statementEnd = code.IndexOf(';', registrationIndex);
                statementEnd = statementEnd < 0
                    ? Math.Min(code.Length, registrationIndex + 2000)
                    : statementEnd + 1;
                string registrationStatement = code.Substring(
                    registrationIndex,
                    statementEnd - registrationIndex);
                Match implementationReference = Regex.Match(
                    registrationStatement,
                    @"=>\s*(?:\{\s*)?(?:return\s+)?(?:await\s+)?(?<target>[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)\s*\(");
                if (!implementationReference.Success)
                {
                    implementationReference = Regex.Match(
                        registrationStatement,
                        @",\s*(?<target>[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)\s*\)");
                }
                if (implementationReference.Success)
                {
                    string implementationTarget = implementationReference.Groups["target"].Value;
                    string methodName = implementationTarget.Split('.').Last();
                    Match methodDeclaration = Regex.Match(
                        code,
                        @"^[ \t]*(?:public|private|internal|protected)\s+"
                        + @"(?:(?:static|async|unsafe|virtual|override|sealed|new|partial)\s+)*"
                        + @"[A-Za-z_]\w*(?:\s*<[^\r\n>]+>)?(?:\[\])?\s+"
                        + Regex.Escape(methodName)
                        + @"\s*\(",
                        RegexOptions.Multiline);
                    if (methodDeclaration.Success)
                    {
                        targetIndex = methodDeclaration.Index;
                        targetDescription = customFunction.FunctionName + " 的实现方法 " + methodName;
                    }
                }

                int lineStart = code.LastIndexOf('\n', targetIndex);
                lineStart = lineStart < 0 ? 0 : lineStart + 1;
                int lineEnd = code.IndexOf('\n', targetIndex);
                lineEnd = lineEnd < 0 ? code.Length : lineEnd;
                if (lineEnd > lineStart && code[lineEnd - 1] == '\r')
                {
                    lineEnd--;
                }
                string targetLineText = code.Substring(lineStart, lineEnd - lineStart);
                int lineNumber = 1;
                for (int i = 0; i < lineStart; i++)
                {
                    if (code[i] == '\n')
                    {
                        lineNumber++;
                    }
                }
                sourceLabel.Text = "已定位到 " + targetDescription
                    + " · 第 " + lineNumber + " 行 · 内嵌源码快照";
                viewer.Shown += (shownSender, shownArgs) =>
                {
                    int controlLineStart = codeBox.Find(
                        targetLineText,
                        RichTextBoxFinds.MatchCase);
                    if (controlLineStart < 0)
                    {
                        return;
                    }
                    codeBox.Select(controlLineStart, Math.Max(1, targetLineText.Length));
                    codeBox.SelectionBackColor = UiPalette.WarningSoft;
                    codeBox.Focus();
                    codeBox.ScrollToCaret();
                    int targetLine = codeBox.GetLineFromCharIndex(controlLineStart);
                    int firstVisibleCharacter = codeBox.GetCharIndexFromPosition(new Point(1, 1));
                    int firstVisibleLine = codeBox.GetLineFromCharIndex(firstVisibleCharacter);
                    int desiredFirstLine = Math.Max(0, targetLine - 3);
                    SendMessage(
                        codeBox.Handle,
                        EmLineScroll,
                        IntPtr.Zero,
                        new IntPtr(desiredFirstLine - firstVisibleLine));
                    codeBox.Select(controlLineStart, 0);
                };
            }
            else
            {
                sourceLabel.Text = "未在内嵌源码中找到 " + customFunction.FunctionName + " 的注册语句";
                sourceLabel.ForeColor = UiPalette.Transition;
            }
            using (viewer)
            {
                viewer.ShowDialog(this);
            }
        }

        private static bool HasCrossProgramClipboardData()
        {
            try
            {
                return Clipboard.ContainsData(OperationClipboardService.Format);
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                return false;
            }
        }

        private void contextMenuStrip2_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.X && e.KeyCode != Keys.U)
            {
                return;
            }

            e.Handled = true;
            e.SuppressKeyPress = true;

            if (SF.frmProc == null)
            {
                if (SF.frmInfo != null && !SF.frmInfo.IsDisposed)
                {
                    SF.frmInfo.PrintInfo("快捷键：流程界面未初始化，无法操作。", FrmInfo.Level.Error);
                }
                return;
            }

            if (SF.frmProc.SelectedProcNum < 0 || SF.frmProc.SelectedStepNum < 0)
            {
                if (SF.frmInfo != null && !SF.frmInfo.IsDisposed)
                {
                    SF.frmInfo.PrintInfo("快捷键：未选择流程或步骤。", FrmInfo.Level.Error);
                }
                return;
            }

            if (iSelectedRow < 0 || iSelectedRow >= dataGridView1.OperationCount)
            {
                if (SF.frmInfo != null && !SF.frmInfo.IsDisposed)
                {
                    SF.frmInfo.PrintInfo("快捷键：未选择指令。", FrmInfo.Level.Error);
                }
                return;
            }

            if (!SF.CanEditProc(SF.frmProc.SelectedProcNum))
            {
                if (SF.frmInfo != null && !SF.frmInfo.IsDisposed)
                {
                    SF.frmInfo.PrintInfo("快捷键：当前流程运行中禁止编辑。", FrmInfo.Level.Error);
                }
                return;
            }

            OperationType dataItem = dataGridView1.GetOperation(iSelectedRow);
            if (dataItem == null)
            {
                if (SF.frmInfo != null && !SF.frmInfo.IsDisposed)
                {
                    SF.frmInfo.PrintInfo("快捷键：指令数据为空，无法操作。", FrmInfo.Level.Error);
                }
                return;
            }

            if (e.KeyCode == Keys.X)
            {
                SetStopPoint_Click(sender, EventArgs.Empty);
                string action = dataItem.IsBreakpoint ? "已设置断点" : "已取消断点";
                string opName = string.IsNullOrWhiteSpace(dataItem.Name) ? "未命名" : dataItem.Name;
                if (SF.frmInfo != null && !SF.frmInfo.IsDisposed)
                {
                    SF.frmInfo.PrintInfo($"快捷键：{SF.frmProc.SelectedProcNum}-{SF.frmProc.SelectedStepNum}-{iSelectedRow} {opName} {action}", FrmInfo.Level.Normal);
                }
                return;
            }

            Enable_Click(sender, EventArgs.Empty);
            string enableAction = dataItem.Disable ? "已禁用" : "已启用";
            string enableOpName = string.IsNullOrWhiteSpace(dataItem.Name) ? "未命名" : dataItem.Name;
            if (SF.frmInfo != null && !SF.frmInfo.IsDisposed)
            {
                SF.frmInfo.PrintInfo($"快捷键：{SF.frmProc.SelectedProcNum}-{SF.frmProc.SelectedStepNum}-{iSelectedRow} {enableOpName} {enableAction}", FrmInfo.Level.Normal);
            }
        }

        public void UpdateHighlight(EngineSnapshot snapshot)
        {
            try
            {
                if (IsDisposed || !IsHandleCreated)
                {
                    return;
                }

                if (SF.frmProc == null || SF.mainfrm == null)
                {
                    return;
                }

                if (SF.mainfrm.WindowState == FormWindowState.Minimized || !SF.mainfrm.ContainsFocus)
                {
                    ClearLastHighlight();
                    return;
                }

                if (SF.curPage != 0)
                {
                    ClearLastHighlight();
                    return;
                }

                if (SF.mainfrm.IsDisposed || !SF.mainfrm.IsHandleCreated || !SF.mainfrm.Visible)
                {
                    return;
                }

                int selectedProc = SF.frmProc.SelectedProcNum;
                if (selectedProc < 0)
                {
                    ClearLastHighlight();
                    return;
                }

                if (snapshot == null || snapshot.ProcIndex != selectedProc || snapshot.State == ProcRunState.Stopped)
                {
                    ClearLastHighlight();
                    return;
                }

                if (snapshot.State == ProcRunState.Paused || snapshot.State == ProcRunState.SingleStep)
                {
                    if (singleStepFollowPending)
                    {
                        if (selectedProc != singleStepFollowProcIndex)
                        {
                            singleStepFollowPending = false;
                            singleStepFollowProcIndex = -1;
                        }
                        else
                        {
                            if (SF.frmProc.SelectedStepNum != snapshot.StepIndex)
                            {
                                if (snapshot.StepIndex >= 0
                                    && selectedProc >= 0
                                    && selectedProc < SF.frmProc.proc_treeView.Nodes.Count
                                    && snapshot.StepIndex < SF.frmProc.proc_treeView.Nodes[selectedProc].Nodes.Count)
                                {
                                    SelectChildNode(selectedProc, snapshot.StepIndex);
                                }
                            }
                            singleStepFollowPending = false;
                            singleStepFollowProcIndex = -1;
                        }
                    }
                }

                if (SF.frmProc.SelectedStepNum != snapshot.StepIndex)
                {
                    ClearLastHighlight();
                    return;
                }

                int rowIndex = snapshot.OpIndex;
                if (rowIndex < 0 || rowIndex >= dataGridView1.OperationCount)
                {
                    ClearLastHighlight();
                    return;
                }

                if (!lastHighlightActive
                    || rowIndex != lastHighlightedRow
                    || selectedProc != lastHighlightedProc
                    || snapshot.StepIndex != lastHighlightedStep
                    || snapshot.State != lastHighlightedState
                    || snapshot.IsBreakpoint != lastHighlightedBreakpoint)
                {
                    if (lastHighlightActive && lastHighlightedRow >= 0 && lastHighlightedRow < dataGridView1.OperationCount)
                    {
                        ClearRowColor(lastHighlightedRow);
                        dataGridView1.InvalidateRow(lastHighlightedRow);
                    }

                    Color highlightColor = GetRuntimeRowBackColor(snapshot.State, snapshot.IsBreakpoint);
                    SetRowColor(rowIndex, highlightColor);
                    dataGridView1.InvalidateRow(rowIndex);
                    lastHighlightActive = true;
                    lastHighlightedRow = rowIndex;
                    lastHighlightedProc = selectedProc;
                    lastHighlightedStep = snapshot.StepIndex;
                    lastHighlightedState = snapshot.State;
                    lastHighlightedBreakpoint = snapshot.IsBreakpoint;
                    dataGridView1.SetRuntimeState(rowIndex, snapshot.State, snapshot.IsBreakpoint);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ObjectDisposedException)
            {
            }
            catch (Exception ex)
            {
                SF.frmInfo.PrintInfo(ex.Message, FrmInfo.Level.Error);
            }
        }

        public void RequestSingleStepFollow(int procIndex)
        {
            singleStepFollowProcIndex = procIndex;
            singleStepFollowPending = procIndex >= 0;
        }

        private void ClearLastHighlight()
        {
            if (lastHighlightActive && lastHighlightedRow >= 0 && lastHighlightedRow < dataGridView1.OperationCount)
            {
                ClearRowColor(lastHighlightedRow);
                dataGridView1.InvalidateRow(lastHighlightedRow);
            }
            lastHighlightActive = false;
            lastHighlightedRow = -1;
            lastHighlightedProc = -1;
            lastHighlightedStep = -1;
            lastHighlightedState = ProcRunState.Stopped;
            lastHighlightedBreakpoint = false;
            dataGridView1.ClearRuntimeState();
        }

        private static Color GetRuntimeRowBackColor(ProcRunState state, bool isBreakpoint)
        {
            if (isBreakpoint)
            {
                return UiPalette.DangerSoft;
            }
            switch (state)
            {
                case ProcRunState.Running:
                    return UiPalette.SuccessSoft;
                case ProcRunState.Paused:
                case ProcRunState.Pausing:
                    return UiPalette.WarningSoft;
                case ProcRunState.SingleStep:
                    return UiPalette.InfoSoft;
                case ProcRunState.Alarming:
                    return UiPalette.DangerSoft;
                case ProcRunState.Stopping:
                    return UiPalette.StoppingSoft;
                default:
                    return UiPalette.SurfaceSubtle;
            }
        }

        public void SelectChildNode(int parentIndex, int childIndex)
        {

            TreeNode parentNode = SF.frmProc.proc_treeView.Nodes[parentIndex];
            if (childIndex >= 0 && childIndex < parentNode.Nodes.Count)
            {
                Invoke(new Action(() =>
                {
                    SF.frmProc.proc_treeView.SelectedNode = parentNode.Nodes[childIndex];

                }));
            }

        }
        public void ScrollRowToCenter(int rowIndex)
        {
            Invoke(new Action(() =>
            {
                if (rowIndex >= 0 && rowIndex < dataGridView1.OperationCount)
                {
                    dataGridView1.EnsureIndexVisible(rowIndex);
                }
            }));
        }
        public void ClearAllRowColors()
        {
            dataGridView1.ClearRowColors();
        }

        /// <summary>
        /// 闪烁整个数据网格的所有行，提示用户当前显示的流程/步骤被 AI 改动。
        /// kind 决定闪烁颜色：Modified=橙黄、Added=浅绿、Deleted=浅红。
        /// 仅在当前网格显示的流程/步骤是被改动的流程时调用。
        /// </summary>
        public void FlashGrid(ProcChangeKind kind)
        {
            if (IsDisposed || Disposing || !IsHandleCreated)
            {
                return;
            }
            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke((Action)(() => FlashGrid(kind)));
                }
                catch (InvalidOperationException)
                {
                }
                return;
            }

            // 停止之前未完成的闪烁
            if (gridFlashTimer != null)
            {
                gridFlashTimer.Stop();
                gridFlashTimer.Dispose();
                gridFlashTimer = null;
            }
            ClearAllRowColors();

            gridFlashColor = kind == ProcChangeKind.Added ? UiPalette.SuccessSoft
                           : kind == ProcChangeKind.Deleted ? UiPalette.DangerSoft
                           : UiPalette.WarningSoft;
            gridFlashCount = 0;

            gridFlashTimer = new System.Windows.Forms.Timer();
            gridFlashTimer.Interval = 300;
            gridFlashTimer.Tick += GridFlashTimer_Tick;
            gridFlashTimer.Start();
        }

        private void GridFlashTimer_Tick(object sender, EventArgs e)
        {
            if (dataGridView1 == null || dataGridView1.IsDisposed)
            {
                gridFlashTimer?.Stop();
                return;
            }
            if (gridFlashCount >= GridFlashMaxCount)
            {
                ClearAllRowColors();
                gridFlashTimer.Stop();
                gridFlashTimer.Dispose();
                gridFlashTimer = null;
                flashTargetRows = null;
                return;
            }
            bool setColor = (gridFlashCount % 2 == 0);
            if (flashTargetRows != null && flashTargetRows.Count > 0)
            {
                // 行级闪烁：只闪烁目标行
                foreach (var (rowIndex, color) in flashTargetRows)
                {
                    if (rowIndex >= 0 && rowIndex < dataGridView1.OperationCount)
                    {
                        dataGridView1.SetRowColor(rowIndex, setColor ? color : Color.Empty);
                    }
                }
            }
            else
            {
                // 整体闪烁：闪烁所有行
                Color c = setColor ? gridFlashColor : Color.Empty;
                dataGridView1.SetAllRowsColor(c);
            }
            gridFlashCount++;
        }

        /// <summary>
        /// 只闪烁被修改的行。从 affectedOps 中筛选当前步骤匹配的 opIndex，只闪烁这些行。
        /// kind 决定颜色：Modified=橙黄、Added=浅绿、Deleted=浅红。
        /// </summary>
        public void FlashRows(List<(int stepIndex, int opIndex, ProcChangeKind kind)> affectedOps)
        {
            if (IsDisposed || Disposing || !IsHandleCreated)
            {
                return;
            }
            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke((Action)(() => FlashRows(affectedOps)));
                }
                catch (InvalidOperationException)
                {
                }
                return;
            }

            int currentStepIndex = SF.frmProc.SelectedStepNum;
            var targetRows = new List<(int rowIndex, Color color)>();
            foreach (var (stepIndex, opIndex, kind) in affectedOps)
            {
                if (stepIndex != currentStepIndex || opIndex < 0)
                {
                    continue;
                }
                Color color = kind == ProcChangeKind.Added ? UiPalette.SuccessSoft
                            : kind == ProcChangeKind.Deleted ? UiPalette.DangerSoft
                            : UiPalette.WarningSoft;
                targetRows.Add((opIndex, color));
            }

            if (targetRows.Count == 0)
            {
                return; // 当前步骤没有被修改的指令，不闪烁
            }

            // 新增指令位于当前可视区域之外时自动跟随一次，便于观察 AI 的插入过程；
            // 修改和删除不改变用户滚动位置。
            int addedRowIndex = targetRows
                .Where(item => item.color == UiPalette.SuccessSoft)
                .Select(item => item.rowIndex)
                .DefaultIfEmpty(-1)
                .First();
            if (addedRowIndex >= 0 && addedRowIndex < dataGridView1.OperationCount
                && !dataGridView1.IsIndexVisible(addedRowIndex))
            {
                try
                {
                    dataGridView1.EnsureIndexVisible(addedRowIndex);
                }
                catch (InvalidOperationException)
                {
                }
            }

            // 停止之前未完成的闪烁
            if (gridFlashTimer != null)
            {
                gridFlashTimer.Stop();
                gridFlashTimer.Dispose();
                gridFlashTimer = null;
            }
            ClearAllRowColors();

            flashTargetRows = targetRows;
            gridFlashCount = 0;

            gridFlashTimer = new System.Windows.Forms.Timer();
            gridFlashTimer.Interval = 300;
            gridFlashTimer.Tick += GridFlashTimer_Tick;
            gridFlashTimer.Start();
        }

        public void SetRowColor(int rowIndex, Color color)
        {
            if (rowIndex >= 0 && rowIndex < dataGridView1.OperationCount)
            {
                dataGridView1.SetRowColor(rowIndex, color);
            }

        }

        public void ClearRowColor(int rowIndex)
        {
            if (rowIndex >= 0 && rowIndex < dataGridView1.OperationCount)
            {
                dataGridView1.SetRowColor(rowIndex, Color.Empty);
            }
        }

        private void FrmDataGrid_Disposed(object sender, EventArgs e)
        {
            dataGridView1.SelectedIndexChanged -= dataGridView1_SelectedIndexChanged;
            dataGridView1.JumpLinkClicked -= dataGridView1_JumpLinkClicked;
            dataGridView1.MouseMove -= dataGridView1_MouseMove;
            dataGridView1.MouseUp -= dataGridView1_MouseUp;
            if (gridFlashTimer != null)
            {
                gridFlashTimer.Stop();
                gridFlashTimer.Tick -= GridFlashTimer_Tick;
                gridFlashTimer.Dispose();
                gridFlashTimer = null;
            }
            flashTargetRows = null;
        }
        private void Add_Click(object sender, EventArgs e)
        {
            if (!SF.CanEditProc(SF.frmProc.SelectedProcNum))
            {
                return;
            }
            if (SF.frmProc.SelectedProcNum < 0 || SF.frmProc.SelectedStepNum < 0)
            {
                MessageBox.Show("请先选择流程步骤。");
                return;
            }
            if (SF.frmProc.SelectedProcNum >= SF.frmProc.procsList.Count
                || SF.frmProc.SelectedStepNum >= SF.frmProc.procsList[SF.frmProc.SelectedProcNum].steps.Count)
            {
                MessageBox.Show("流程或步骤索引无效，无法新增指令。");
                return;
            }
            if (dataGridView1.GetSelectedIndexes().Count == 0)
                iSelectedRow = -1;
            OperationTemp = new HomeRun() { Num = iSelectedRow == -1 ? dataGridView1.OperationCount : iSelectedRow + 1 };
            OperationTemp.RefleshPropertyAlarm();
            SF.frmInspector.ShowObject(OperationTemp);
            SF.isAddOps = true;
            BeginOperationEditSession(true);
            // 编辑期间保留指令表交互，用作跳转地址的拖拽来源。
        }

        private void Delete_Click(object sender, EventArgs e)
        {
            if (SF.frmProc.SelectedProcNum < 0 || SF.frmProc.SelectedStepNum < 0)
            {
                return;
            }
            if (!SF.CanEditProc(SF.frmProc.SelectedProcNum))
            {
                return;
            }
            // int count = 0;
            selectedRowIndexes4Del.Clear();
            selectedRowIndexes4Del.AddRange(dataGridView1.GetSelectedIndexes());
            selectedRowIndexes4Del.Sort();
            if (selectedRowIndexes4Del.Count == 0)
            {
                return;
            }

            int procIndex = SF.frmProc.SelectedProcNum;
            int stepIndex = SF.frmProc.SelectedStepNum;
            if (procIndex < 0 || procIndex >= SF.frmProc.procsList.Count)
            {
                MessageBox.Show("流程索引无效，无法删除指令。");
                return;
            }
            if (stepIndex < 0 || stepIndex >= SF.frmProc.procsList[procIndex].steps.Count)
            {
                MessageBox.Show("步骤索引无效，无法删除指令。");
                return;
            }
            Proc proc = SF.frmProc.procsList[procIndex];
            Step step = proc.steps[stepIndex];
            string procName = string.IsNullOrWhiteSpace(proc?.head?.Name) ? $"索引{procIndex}" : proc.head.Name;
            string stepName = string.IsNullOrWhiteSpace(step?.Name) ? $"索引{stepIndex}" : step.Name;
            string warnMsg;
            if (selectedRowIndexes4Del.Count == 1)
            {
                int opIndex = selectedRowIndexes4Del[0];
                OperationType op = step?.Ops != null && opIndex >= 0 && opIndex < step.Ops.Count ? step.Ops[opIndex] : null;
                string opType = op?.OperaType ?? "未知类型";
                string opName = string.IsNullOrWhiteSpace(op?.Name) ? "未命名" : op.Name;
                string opText = $"{opIndex}({opType}) {opName}";
                warnMsg = $"警告：即将删除指令【{opText}】\r\n所属流程：【{procName}】\r\n所属步骤：【{stepName}】\r\n删除后可在当前会话使用 Ctrl+Z 撤销，确认删除？";
            }
            else
            {
                warnMsg = $"警告：即将删除{selectedRowIndexes4Del.Count}条指令\r\n所属流程：【{procName}】\r\n所属步骤：【{stepName}】\r\n删除后可在当前会话使用 Ctrl+Z 撤销，确认删除？";
            }
            DialogResult confirmResult = MessageBox.Show(
                this,
                warnMsg,
                "删除指令确认",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (confirmResult != DialogResult.Yes)
            {
                return;
            }

            Proc before = ObjectGraphCloner.Clone(proc);
            Proc draft = ObjectGraphCloner.Clone(proc);
            for (int i = selectedRowIndexes4Del.Count - 1; i >= 0; i--)
            {
                int index = selectedRowIndexes4Del[i];
                if (index >= 0 && index < draft.steps[stepIndex].Ops.Count)
                {
                    draft.steps[stepIndex].Ops.RemoveAt(index);
                }
            }
            ProcessEditingService.RewriteGotoTargets(before, draft, procIndex);
            if (!ProcessEditingService.TryCommitProcDraft(
                procIndex, draft, out string commitError, "删除指令"))
            {
                MessageBox.Show(commitError, "删除指令失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
        }

        private void Modify_Click(object sender, EventArgs e)
        {
            if (!SF.CanEditProc(SF.frmProc.SelectedProcNum))
            {
                return;
            }
            if (SF.frmProc.SelectedProcNum < 0 || SF.frmProc.SelectedStepNum < 0)
            {
                MessageBox.Show("请先选择流程步骤。");
                return;
            }
            int procIndex = SF.frmProc.SelectedProcNum;
            int stepIndex = SF.frmProc.SelectedStepNum;
            if (procIndex < 0 || procIndex >= SF.frmProc.procsList.Count)
            {
                MessageBox.Show("流程索引无效，无法编辑指令。");
                return;
            }
            if (stepIndex < 0 || stepIndex >= SF.frmProc.procsList[procIndex].steps.Count)
            {
                MessageBox.Show("步骤索引无效，无法编辑指令。");
                return;
            }
            int opCount = SF.frmProc.procsList[procIndex].steps[stepIndex].Ops.Count;
            if (iSelectedRow < 0 || iSelectedRow >= opCount)
            {
                MessageBox.Show("请选择需要编辑的指令。");
                return;
            }
            OperationType selectedOperation = SF.frmProc.procsList[procIndex]
                .steps[stepIndex].Ops[iSelectedRow];
            if (selectedOperation == null)
            {
                MessageBox.Show("当前指令为空，无法编辑。");
                return;
            }
            OperationTemp = (OperationType)selectedOperation.Clone();
            OperationTemp.RefreshInspector?.Invoke();
            TypeDescriptor.Refresh(OperationTemp);
            BeginOperationEditSession(false);
            SF.frmProc.Enabled = false;
        }

        private void dataGridView1_KeyDown(object sender, KeyEventArgs e)
        {
            if (SF.TryHandleEditorHistoryShortcut(dataGridView1, e))
            {
                return;
            }
            if (IsOperationAddressDragEnabled())
            {
                return;
            }
            if (e.Control && e.KeyCode == Keys.C)
            {
                if (dataGridView1.GetSelectedIndexes().Count > 0)
                {
                    Copy();
                }

                e.Handled = true;
            }
            if (e.Control && e.KeyCode == Keys.V)
            {

                Paste();

                e.Handled = true;
            }
            if (e.KeyCode == Keys.Enter)
            {

                e.Handled = true; // 阻止默认行为 防止选择条向下切换

            }
            if (e.KeyCode == Keys.Delete)
            {
                Delete_Click(sender, EventArgs.Empty);
                e.Handled = true;
            }
        }

        private void SetStartOps_Click(object sender, EventArgs e)
        {
            if (SF.frmProc.SelectedProcNum < 0 || SF.frmProc.SelectedStepNum < 0)
            {
                MessageBox.Show("请先选择流程步骤。");
                return;
            }
            if (SF.frmProc.SelectedProcNum >= SF.frmProc.procsList.Count
                || SF.frmProc.SelectedStepNum >= SF.frmProc.procsList[SF.frmProc.SelectedProcNum].steps.Count)
            {
                MessageBox.Show("流程或步骤索引无效，无法设置启动点。");
                return;
            }
            int opCount = SF.frmProc.procsList[SF.frmProc.SelectedProcNum].steps[SF.frmProc.SelectedStepNum].Ops.Count;
            if (iSelectedRow < 0 || iSelectedRow >= opCount)
            {
                MessageBox.Show("请先选择需要设为启动点的指令。");
                return;
            }
            ProcRunState startState = ProcRunState.SingleStep;
            EngineSnapshot startSnapshot = SF.DR.GetSnapshot(SF.frmProc.SelectedProcNum);
            if (startSnapshot != null && startSnapshot.State == ProcRunState.Paused)
            {
                startState = ProcRunState.Paused;
            }

            if (SF.frmProc.SelectedProcNum >= 0)
            {
                SF.DR.Stop(SF.frmProc.SelectedProcNum);
            }

            SF.DR.StartProcAt(
                null,
                SF.frmProc.SelectedProcNum,
                SF.frmProc.SelectedStepNum,
                iSelectedRow,
                startState);

            Invoke(new Action(() =>
            {
                SF.frmToolBar.SetPauseButtonAction(true);
                SF.frmToolBar.btnPause.Enabled = startState != ProcRunState.Paused;
                SF.frmToolBar.SingleRun.Enabled = startState == ProcRunState.SingleStep;
            }));
        }

        private void SetStopPoint_Click(object sender, EventArgs e)
        {
            if (SF.frmProc.SelectedProcNum < 0 || SF.frmProc.SelectedStepNum < 0)
            {
                return;
            }
            if (!SF.CanEditProc(SF.frmProc.SelectedProcNum))
            {
                return;
            }
            if (iSelectedRow >= 0 && iSelectedRow < dataGridView1.OperationCount)
            {
                int procIndex = SF.frmProc.SelectedProcNum;
                int stepIndex = SF.frmProc.SelectedStepNum;
                Proc draft = ObjectGraphCloner.Clone(SF.frmProc.procsList[procIndex]);
                if (stepIndex >= 0 && stepIndex < draft.steps.Count
                    && iSelectedRow < draft.steps[stepIndex].Ops.Count)
                {
                    OperationType operation = draft.steps[stepIndex].Ops[iSelectedRow];
                    operation.IsBreakpoint = !operation.IsBreakpoint;
                    if (!ProcessEditingService.TryCommitProcDraft(
                        procIndex, draft, out string error, "修改指令断点"))
                    {
                        MessageBox.Show(error, "断点修改失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }
        List<OperationType> ListOperationType4Copy = new List<OperationType>();
        public void Copy()
        {
            selectedRowIndexes4Copy.Clear();
            ListOperationType4Copy.Clear();
            selectedRowIndexes4Copy.AddRange(dataGridView1.GetSelectedIndexes());
            selectedRowIndexes4Copy.Sort();
            for (int i = 0; i < selectedRowIndexes4Copy.Count; i++)
            {
                OperationType boundItem = dataGridView1.GetOperation(selectedRowIndexes4Copy[i]);
                if (boundItem == null)
                {
                    continue;
                }
                OperationType dataItem = (OperationType)boundItem.Clone();
                dataItem.Num = -1;
                ListOperationType4Copy.Add(dataItem);
            }
        }
        public void Paste()
        {
            if (!TryPasteOperations(ListOperationType4Copy, out int insertIndex, out int insertedCount, out string error))
            {
                if (!string.IsNullOrWhiteSpace(error))
                {
                    MessageBox.Show(error, "粘贴指令失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                return;
            }
            HighlightInsertedRows(insertIndex, insertedCount);
        }

        private bool TryPasteOperations(IEnumerable<OperationType> source, out int insertIndex, out int insertedCount, out string error)
        {
            insertIndex = -1;
            insertedCount = 0;
            error = null;
            int procIndex = SF.frmProc.SelectedProcNum;
            int stepIndex = SF.frmProc.SelectedStepNum;
            if (procIndex < 0 || stepIndex < 0)
            {
                error = "请先选择流程步骤。";
                return false;
            }
            if (!SF.CanEditProc(procIndex))
            {
                return false;
            }
            if (procIndex >= SF.frmProc.procsList.Count
                || stepIndex >= SF.frmProc.procsList[procIndex].steps.Count)
            {
                error = "流程或步骤索引无效，无法粘贴指令。";
                return false;
            }
            List<OperationType> copiedOperations = OperationClipboardService.PrepareForPaste(source, procIndex);
            if (copiedOperations == null || copiedOperations.Count == 0)
            {
                return false;
            }
            insertIndex = iSelectedRow + 1;
            Proc current = SF.frmProc.procsList[procIndex];
            int opCount = current.steps[stepIndex].Ops.Count;
            if (insertIndex < 0 || insertIndex > opCount)
            {
                error = "当前指令索引无效，无法粘贴。";
                return false;
            }
            Proc before = ObjectGraphCloner.Clone(current);
            Proc draft = ObjectGraphCloner.Clone(current);
            draft.steps[stepIndex].Ops.InsertRange(insertIndex, copiedOperations);
            ProcessEditingService.RewriteGotoTargets(before, draft, procIndex);
            if (!ProcessEditingService.TryCommitProcDraft(
                procIndex, draft, out error, "粘贴指令"))
            {
                return false;
            }
            insertedCount = copiedOperations.Count;
            return true;
        }

        private void HighlightInsertedRows(int insertIndex, int insertedCount)
        {
            for (int i = insertIndex; i < insertIndex + insertedCount && i < dataGridView1.OperationCount; i++)
            {
                dataGridView1.SetRowColor(i, UiPalette.SuccessSoft);
            }
        }
        private void copy_Click(object sender, EventArgs e)
        {
            if (dataGridView1.GetSelectedIndexes().Count > 0)
            {
                Copy();
            }
        }
        private void paste_Click(object sender, EventArgs e)
        {
            Paste();
        }
        private int dragIndex = -1;
        private int addressDragIndex = -1;
        private Point addressDragStart;
        private void dataGridView1_MouseDown(object sender, MouseEventArgs e)
        {
            int rowIndex = dataGridView1.IndexFromPoint(e.Location);
            if (e.Button == MouseButtons.Left && IsOperationAddressDragEnabled())
            {
                addressDragIndex = rowIndex;
                addressDragStart = e.Location;
                return;
            }
            if (e.Button == MouseButtons.Right)
            {
                contextMenuByMouse = true;
                contextMenuRowIndex = rowIndex;
                if (rowIndex < 0)
                {
                    iSelectedRow = -1;
                    dataGridView1.ClearSelection();
                }
                else
                {
                    if (!dataGridView1.SelectedIndices.Contains(rowIndex))
                    {
                        dataGridView1.SelectSingle(rowIndex);
                    }
                    iSelectedRow = rowIndex;
                }
            }
            else if (e.Button == MouseButtons.Left && rowIndex >= 0)
            {
                iSelectedRow = rowIndex;
            }
            if (ModifierKeys == Keys.Alt && e.Button == MouseButtons.Left)
            {
                dragIndex = rowIndex;
                if (dragIndex >= 0)
                {
                    dataGridView1.DoDragDrop(dragIndex, DragDropEffects.Move);
                }
            }
        }

        private void dataGridView1_MouseMove(object sender, MouseEventArgs e)
        {
            if (addressDragIndex < 0
                || e.Button != MouseButtons.Left
                || !IsOperationAddressDragEnabled())
            {
                return;
            }

            Size dragSize = SystemInformation.DragSize;
            Rectangle dragBounds = new Rectangle(
                addressDragStart.X - dragSize.Width / 2,
                addressDragStart.Y - dragSize.Height / 2,
                dragSize.Width,
                dragSize.Height);
            if (dragBounds.Contains(e.Location))
            {
                return;
            }

            int procIndex = SF.frmProc?.SelectedProcNum ?? -1;
            int stepIndex = SF.frmProc?.SelectedStepNum ?? -1;
            int sourceIndex = addressDragIndex;
            addressDragIndex = -1;
            if (procIndex < 0
                || stepIndex < 0
                || procIndex >= SF.frmProc.procsList.Count
                || stepIndex >= SF.frmProc.procsList[procIndex].steps.Count
                || sourceIndex >= SF.frmProc.procsList[procIndex].steps[stepIndex].Ops.Count)
            {
                return;
            }

            var dragData = new DataObject();
            dragData.SetData(OperationAddressDragFormat, $"{procIndex}-{stepIndex}-{sourceIndex}");
            dataGridView1.DoDragDrop(dragData, DragDropEffects.Copy);
        }

        private void dataGridView1_MouseUp(object sender, MouseEventArgs e)
        {
            addressDragIndex = -1;
        }

        private static bool IsOperationAddressDragEnabled()
        {
            return SF.ActiveEditSession?.Draft is OperationType
                && (SF.isModify == ModifyKind.Operation || SF.isAddOps);
        }

        private void dataGridView1_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (IsOperationAddressDragEnabled())
            {
                return;
            }
            int rowIndex = dataGridView1.IndexFromPoint(e.Location);
            if (rowIndex < 0)
            {
                return;
            }
            iSelectedRow = rowIndex;
            Modify_Click(sender, EventArgs.Empty);
        }

        private void ShowOperationProperties(int rowIndex)
        {
            int procIndex = SF.frmProc.SelectedProcNum;
            int stepIndex = SF.frmProc.SelectedStepNum;
            if (rowIndex < 0
                || procIndex < 0
                || stepIndex < 0
                || procIndex >= SF.frmProc.procsList.Count
                || stepIndex >= SF.frmProc.procsList[procIndex].steps.Count
                || rowIndex >= SF.frmProc.procsList[procIndex].steps[stepIndex].Ops.Count)
            {
                return;
            }

            OperationType operation = SF.frmProc.procsList[procIndex].steps[stepIndex].Ops[rowIndex];
            if (operation == null)
            {
                return;
            }
            FrmInspector inspector = SF.frmInspector;
            if (ReferenceEquals(OperationTemp, operation)
                && ReferenceEquals(inspector.SelectedObject, operation))
            {
                return;
            }
            OperationTemp = operation;
            inspector.BeginUpdate();
            try
            {
                OperationTemp.RefreshInspector?.Invoke();
                TypeDescriptor.Refresh(OperationTemp);
                inspector.ShowObject(OperationTemp);
            }
            finally
            {
                inspector.EndUpdate();
            }
        }

        public bool SelectOperationForNavigation(Guid opId)
        {
            if (opId == Guid.Empty)
            {
                return false;
            }
            int rowIndex = Enumerable.Range(0, dataGridView1.OperationCount)
                .FirstOrDefault(index => dataGridView1.GetOperation(index)?.Id == opId);
            if (rowIndex < 0 || rowIndex >= dataGridView1.OperationCount
                || dataGridView1.GetOperation(rowIndex)?.Id != opId)
            {
                return false;
            }
            iSelectedRow = rowIndex;
            dataGridView1.SelectSingle(rowIndex);
            dataGridView1.Update();
            ShowOperationProperties(rowIndex);
            return true;
        }

        private void dataGridView1_DragDrop(object sender, DragEventArgs e)
        {
            int procIndex = SF.frmProc.SelectedProcNum;
            int stepIndex = SF.frmProc.SelectedStepNum;
            if (!SF.CanEditProc(procIndex))
            {
                dragIndex = -1;
                return;
            }
            if (procIndex < 0 || procIndex >= SF.frmProc.procsList.Count
                || stepIndex < 0 || stepIndex >= SF.frmProc.procsList[procIndex].steps.Count)
            {
                dragIndex = -1;
                return;
            }
            if (dragIndex >= 0)
            {
                Point p = dataGridView1.PointToClient(new Point(e.X, e.Y));
                int targetIndex = dataGridView1.IndexFromPoint(p);
                Proc current = SF.frmProc.procsList[procIndex];

                if (targetIndex >= 0 && targetIndex < current.steps[stepIndex].Ops.Count
                    && dragIndex < current.steps[stepIndex].Ops.Count && targetIndex != dragIndex)
                {
                    Proc before = ObjectGraphCloner.Clone(current);
                    Proc draft = ObjectGraphCloner.Clone(current);
                    OperationType draggedItem = draft.steps[stepIndex].Ops[dragIndex];
                    draft.steps[stepIndex].Ops.RemoveAt(dragIndex);
                    draft.steps[stepIndex].Ops.Insert(targetIndex, draggedItem);
                    ProcessEditingService.RewriteGotoTargets(before, draft, procIndex);
                    if (!ProcessEditingService.TryCommitProcDraft(
                        procIndex, draft, out string commitError, "移动指令"))
                    {
                        MessageBox.Show(commitError, "移动指令失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }

            dragIndex = -1;
        }

        private void dataGridView1_DragOver(object sender, DragEventArgs e)
        {
            e.Effect = DragDropEffects.Move;
        }

        private void BeginOperationEditSession(bool isAdd)
        {
            int procIndex = SF.frmProc.SelectedProcNum;
            int stepIndex = SF.frmProc.SelectedStepNum;
            int selectedRow = iSelectedRow;
            SF.isAddOps = isAdd;
            SF.isModify = isAdd ? ModifyKind.None : ModifyKind.Operation;
            SF.BeginEditSession(new EditSession<OperationType>(isAdd ? "新增指令" : "修改指令", OperationTemp,
                draft =>
                {
                    if (draft == null)
                    {
                        return "指令草稿为空。";
                    }
                    Proc proc = procIndex >= 0 && procIndex < SF.frmProc.procsList.Count
                        ? SF.frmProc.procsList[procIndex]
                        : null;
                    return ProcessDefinitionService.TryValidateOperationGoto(draft, procIndex, proc, out string error)
                        ? null
                        : error;
                },
                draft =>
                {
                    if (procIndex < 0 || procIndex >= SF.frmProc.procsList.Count
                        || stepIndex < 0 || stepIndex >= SF.frmProc.procsList[procIndex].steps.Count)
                    {
                        throw new InvalidOperationException("流程或步骤索引已失效。");
                    }
                    Proc before = ObjectGraphCloner.Clone(SF.frmProc.procsList[procIndex]);
                    Proc procDraft = ObjectGraphCloner.Clone(SF.frmProc.procsList[procIndex]);
                    Step step = procDraft.steps[stepIndex];
                    int targetIndex;
                    if (isAdd)
                    {
                        draft.Id = Guid.NewGuid();
                        targetIndex = selectedRow < 0 ? step.Ops.Count : selectedRow + 1;
                        step.Ops.Insert(targetIndex, draft);
                    }
                    else
                    {
                        if (selectedRow < 0 || selectedRow >= step.Ops.Count)
                        {
                            throw new InvalidOperationException("指令索引已失效。");
                        }
                        OperationType original = step.Ops[selectedRow];
                        draft.Id = original?.Id != Guid.Empty ? original.Id : Guid.NewGuid();
                        step.Ops[selectedRow] = draft;
                        targetIndex = selectedRow;
                    }
                    ProcessEditingService.RewriteGotoTargets(before, procDraft, procIndex);
                    if (isAdd)
                    {
                        ProcessEditingService.RewriteAddedOperationGotoTargetsFromPreviousLayout(
                            draft, before, procDraft, procIndex);
                    }
                    if (!ProcessEditingService.TryCommitProcDraft(
                        procIndex,
                        procDraft,
                        out string commitError,
                        isAdd ? "新增指令" : "修改指令"))
                    {
                        throw new InvalidOperationException(commitError);
                    }
                    OperationTemp = (OperationType)SF.frmProc.procsList[procIndex].steps[stepIndex].Ops[targetIndex].Clone();
                    iSelectedRow = targetIndex;
                    dataGridView1.Enabled = true;
                    SF.frmProc.Enabled = true;
                    SF.isAddOps = false;
                    SF.isModify = ModifyKind.None;
                },
                () =>
                {
                    OperationTemp = null;
                    dataGridView1.Enabled = true;
                    SF.frmProc.Enabled = true;
                    SF.isAddOps = false;
                    SF.isModify = ModifyKind.None;
                }));
        }

        private void Enable_Click(object sender, EventArgs e)
        {
            int procIndex = SF.frmProc.SelectedProcNum;
            int stepIndex = SF.frmProc.SelectedStepNum;
            if (iSelectedRow >= 0 && procIndex >= 0 && stepIndex >= 0)
            {
                if (!SF.CanEditProc(procIndex)
                    || procIndex >= SF.frmProc.procsList.Count
                    || stepIndex >= SF.frmProc.procsList[procIndex].steps.Count
                    || iSelectedRow >= SF.frmProc.procsList[procIndex].steps[stepIndex].Ops.Count)
                {
                    return;
                }
                Proc draft = ObjectGraphCloner.Clone(SF.frmProc.procsList[procIndex]);
                OperationType operation = draft.steps[stepIndex].Ops[iSelectedRow];
                if (operation != null)
                {
                    operation.Disable = !operation.Disable;
                    if (!ProcessEditingService.TryCommitProcDraft(
                        procIndex, draft, out string commitError, "切换指令禁用状态"))
                    {
                        MessageBox.Show(commitError, "更新指令状态失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void CProgramCopy_Click(object sender, EventArgs e)
        {
            if (dataGridView1.GetSelectedIndexes().Count > 0)
            {
                selectedRowIndexes4Copy.Clear();
                ListOperationType4Copy.Clear();
                selectedRowIndexes4Copy.AddRange(dataGridView1.GetSelectedIndexes());
                selectedRowIndexes4Copy.Sort();
                for (int i = 0; i < selectedRowIndexes4Copy.Count; i++)
                {
                    OperationType dataItem = (OperationType)dataGridView1.GetOperation(selectedRowIndexes4Copy[i]).Clone();
                    dataItem.Num = -1;
                    ListOperationType4Copy.Add(dataItem);
                }

                string json = OperationClipboardService.Serialize(ListOperationType4Copy);
                Clipboard.SetData(OperationClipboardService.Format, json);
            }
        }

        private void CProgramPaste_Click(object sender, EventArgs e)
        {
            try
            {
                if (!SF.CanEditProc(SF.frmProc.SelectedProcNum))
                {
                    return;
                }
                if (SF.frmProc.SelectedProcNum < 0 || SF.frmProc.SelectedStepNum < 0)
                {
                    MessageBox.Show("请先选择流程步骤。");
                    return;
                }
                if (SF.frmProc.SelectedProcNum >= SF.frmProc.procsList.Count
                    || SF.frmProc.SelectedStepNum >= SF.frmProc.procsList[SF.frmProc.SelectedProcNum].steps.Count)
                {
                    MessageBox.Show("流程或步骤索引无效，无法粘贴指令。");
                    return;
                }
                List<OperationType> deepCopy = null;
                if (Clipboard.ContainsData(OperationClipboardService.Format))
                {
                    string json = Clipboard.GetData(OperationClipboardService.Format) as string;
                    deepCopy = OperationClipboardService.Deserialize(json);
                }
                if (deepCopy == null)
                {
                    return;
                }
                if (!TryPasteOperations(deepCopy, out int insertIndex, out int insertedCount, out string error))
                {
                    if (!string.IsNullOrWhiteSpace(error))
                    {
                        MessageBox.Show(error, "粘贴指令失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    return;
                }
                HighlightInsertedRows(insertIndex, insertedCount);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

    }
}
