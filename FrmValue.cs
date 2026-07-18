using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Automation.Protocol;

namespace Automation
{
    public partial class FrmValue : Form
    {  //存放变量对象
        private const string DefaultValueType = "double";
        private const string DefaultValueText = "0";
        private const string AllVariableScopes = "all";
        private const int EmSetCueBanner = 0x1501;
        private const int WmSetRedraw = 0x000B;
        private static readonly Color PrimaryColor = Color.FromArgb(49, 91, 180);
        private static readonly Color PrimaryHoverColor = Color.FromArgb(61, 105, 196);
        private static readonly Color PrimaryPressedColor = Color.FromArgb(37, 72, 150);
        private static readonly Color PrimaryContainerColor = Color.FromArgb(222, 231, 255);
        private static readonly Color SurfaceColor = Color.FromArgb(252, 252, 255);
        private static readonly Color SurfaceContainerColor = Color.FromArgb(243, 244, 249);
        private static readonly Color HeaderBackColor = Color.FromArgb(245, 247, 252);
        private static readonly Color HeaderForeColor = Color.FromArgb(40, 43, 51);
        private static readonly Color SecondaryForeColor = Color.FromArgb(91, 94, 104);
        private static readonly Color GridLineColor = Color.FromArgb(221, 224, 232);
        private static readonly Color AlternateRowColor = Color.FromArgb(248, 249, 253);
        private static readonly Color SelectionBackColor = PrimaryContainerColor;
        private static readonly Color SelectionForeColor = Color.FromArgb(25, 49, 102);
        private static readonly Color ErrorColor = Color.FromArgb(186, 26, 26);
        private static readonly Color ErrorContainerColor = Color.FromArgb(255, 237, 234);

        private sealed class CommonValueItem
        {
            public int Index { get; set; }
            public string Name { get; set; }

            public override string ToString()
            {
                string text = string.IsNullOrWhiteSpace(Name) ? $"索引{Index}" : Name;
                return $"{Index:D3}  {text}";
            }
        }

        private sealed class ValueClipboardItem
        {
            public string Name { get; set; }
            public string Type { get; set; }
            public string Value { get; set; }
            public string Note { get; set; }
        }

        private sealed class VariableScopeSelection
        {
            public string Scope { get; set; }
            public Guid? OwnerProcId { get; set; }
        }

        private sealed class VariableScopeOption
        {
            public string Key { get; set; }
            public string Text { get; set; }
            public string Scope { get; set; }
            public Guid? OwnerProcId { get; set; }
        }

        private enum MaterialButtonTone
        {
            Primary,
            Tonal,
            Danger
        }

        private sealed class MaterialButton : Button
        {
            private bool mouseOver;
            private bool mouseDown;

            public MaterialButtonTone Tone { get; set; } = MaterialButtonTone.Tonal;

            public MaterialButton()
            {
                FlatStyle = FlatStyle.Flat;
                FlatAppearance.BorderSize = 0;
                UseVisualStyleBackColor = false;
                SetStyle(ControlStyles.UserPaint
                    | ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.ResizeRedraw, true);
            }

            protected override void OnMouseEnter(EventArgs e)
            {
                mouseOver = true;
                Invalidate();
                base.OnMouseEnter(e);
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                mouseOver = false;
                mouseDown = false;
                Invalidate();
                base.OnMouseLeave(e);
            }

            protected override void OnMouseDown(MouseEventArgs mevent)
            {
                if (mevent.Button == MouseButtons.Left) mouseDown = true;
                Invalidate();
                base.OnMouseDown(mevent);
            }

            protected override void OnMouseUp(MouseEventArgs mevent)
            {
                mouseDown = false;
                Invalidate();
                base.OnMouseUp(mevent);
            }

            protected override void OnEnabledChanged(EventArgs e)
            {
                Invalidate();
                base.OnEnabledChanged(e);
            }

            protected override void OnPaint(PaintEventArgs pevent)
            {
                Color parentBackColor = Parent?.BackColor ?? SurfaceColor;
                pevent.Graphics.Clear(parentBackColor);
                pevent.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                pevent.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

                Color backColor;
                Color textColor;
                Color borderColor;
                if (!Enabled)
                {
                    backColor = Color.FromArgb(241, 242, 246);
                    textColor = Color.FromArgb(153, 156, 166);
                    borderColor = Color.FromArgb(228, 230, 236);
                }
                else if (Tone == MaterialButtonTone.Primary)
                {
                    backColor = mouseDown ? PrimaryPressedColor : mouseOver ? PrimaryHoverColor : PrimaryColor;
                    textColor = Color.White;
                    borderColor = backColor;
                }
                else if (Tone == MaterialButtonTone.Danger)
                {
                    backColor = mouseDown
                        ? Color.FromArgb(255, 207, 202)
                        : mouseOver ? Color.FromArgb(255, 221, 217) : ErrorContainerColor;
                    textColor = ErrorColor;
                    borderColor = Color.FromArgb(245, 199, 195);
                }
                else
                {
                    backColor = mouseDown
                        ? Color.FromArgb(222, 226, 236)
                        : mouseOver ? Color.FromArgb(232, 235, 243) : SurfaceContainerColor;
                    textColor = HeaderForeColor;
                    borderColor = Color.FromArgb(214, 217, 226);
                }

                RectangleF bounds = new RectangleF(0.75F, 0.75F, Math.Max(1, Width - 1.5F), Math.Max(1, Height - 1.5F));
                using (GraphicsPath path = CreateRoundedPath(bounds, 9F))
                using (var brush = new SolidBrush(backColor))
                using (var pen = new Pen(borderColor, 1F))
                {
                    pevent.Graphics.FillPath(brush, path);
                    pevent.Graphics.DrawPath(pen, path);
                }
                TextRenderer.DrawText(
                    pevent.Graphics,
                    Text,
                    Font,
                    ClientRectangle,
                    textColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        }

        private sealed class MaterialInputHost : Panel
        {
            public MaterialInputHost()
            {
                SetStyle(ControlStyles.UserPaint
                    | ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.ResizeRedraw, true);
                BackColor = SurfaceContainerColor;
            }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                e.Graphics.Clear(Parent?.BackColor ?? SurfaceColor);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                RectangleF bounds = new RectangleF(0.75F, 0.75F, Math.Max(1, Width - 1.5F), Math.Max(1, Height - 1.5F));
                using (GraphicsPath path = CreateRoundedPath(bounds, 11F))
                using (var brush = new SolidBrush(BackColor))
                using (var pen = new Pen(Color.FromArgb(214, 217, 226), 1F))
                {
                    e.Graphics.FillPath(brush, path);
                    e.Graphics.DrawPath(pen, path);
                }
                base.OnPaint(e);
            }
        }

        private sealed class ValueMonitorForm : Form
        {
            private readonly FrmValue owner;
            private readonly Panel topPanel;
            private readonly Button btnAdd;
            private readonly Button btnRemove;
            private readonly Label labelTitle;
            private readonly DataGridView dgvMonitor;

            public ValueMonitorForm(FrmValue owner)
            {
                this.owner = owner;
                Font uiFont = new Font("宋体", 12F, FontStyle.Regular, GraphicsUnit.Point, ((byte)(134)));
                Text = "变量监控";
                StartPosition = FormStartPosition.CenterScreen;
                Size = new Size(780, 520);
                MinimumSize = new Size(680, 400);

                topPanel = new Panel
                {
                    Dock = DockStyle.Top,
                    Height = 40,
                    BackColor = SystemColors.ControlLight
                };

                labelTitle = new Label
                {
                    Dock = DockStyle.Left,
                    Width = 180,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Font = new Font("宋体", 12.5F, FontStyle.Bold, GraphicsUnit.Point, ((byte)(134))),
                    Text = "变量监控(0)"
                };

                btnAdd = new MaterialButton
                {
                    Text = "添加当前",
                    Font = uiFont,
                    Size = new Size(96, 30),
                    Anchor = AnchorStyles.Top | AnchorStyles.Right,
                    Location = new Point(300, 5)
                };
                btnAdd.Click += (s, e) => owner.AddMonitorFromSelection();

                btnRemove = new MaterialButton
                {
                    Text = "移除选中",
                    Font = uiFont,
                    Size = new Size(96, 30),
                    Anchor = AnchorStyles.Top | AnchorStyles.Right,
                    Location = new Point(400, 5)
                };
                btnRemove.Click += (s, e) => owner.RemoveMonitorFromMonitorSelection();

                topPanel.Controls.Add(btnRemove);
                topPanel.Controls.Add(btnAdd);
                topPanel.Controls.Add(labelTitle);

                dgvMonitor = new DataGridView
                {
                    Dock = DockStyle.Fill,
                    AllowUserToAddRows = false,
                    AllowUserToResizeRows = false,
                    AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                    BackgroundColor = SystemColors.ControlLight,
                    ColumnHeadersHeight = 32,
                    ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                    ReadOnly = true,
                    RowHeadersVisible = false,
                    RowTemplate = { Height = 26 },
                    SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                    Font = uiFont
                };
                dgvMonitor.ColumnHeadersDefaultCellStyle.Font = new Font("宋体", 12F, FontStyle.Bold, GraphicsUnit.Point, ((byte)(134)));
                dgvMonitor.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                dgvMonitor.Columns.Add(new DataGridViewTextBoxColumn
                {
                    Name = "monitorIndex",
                    HeaderText = "编号",
                    MinimumWidth = 60,
                    FillWeight = 12
                });
                dgvMonitor.Columns.Add(new DataGridViewTextBoxColumn
                {
                    Name = "monitorName",
                    HeaderText = "名称",
                    MinimumWidth = 90,
                    FillWeight = 18
                });
                dgvMonitor.Columns.Add(new DataGridViewTextBoxColumn
                {
                    Name = "monitorValue",
                    HeaderText = "值",
                    MinimumWidth = 80,
                    FillWeight = 16
                });
                dgvMonitor.Columns.Add(new DataGridViewTextBoxColumn
                {
                    Name = "monitorSource",
                    HeaderText = "来源",
                    MinimumWidth = 120,
                    FillWeight = 24
                });
                dgvMonitor.Columns.Add(new DataGridViewTextBoxColumn
                {
                    Name = "monitorTime",
                    HeaderText = "时间",
                    MinimumWidth = 140,
                    FillWeight = 30
                });

                Controls.Add(dgvMonitor);
                Controls.Add(topPanel);
                ApplyMonitorStyle();
            }

            private void ApplyMonitorStyle()
            {
                BackColor = Color.White;
                topPanel.BackColor = HeaderBackColor;
                labelTitle.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold);
                labelTitle.ForeColor = HeaderForeColor;
                ApplyButtonStyle(btnAdd, true, false);
                ApplyButtonStyle(btnRemove, false, true);
                ApplyGridStyle(dgvMonitor);
            }

            public DataGridView Grid => dgvMonitor;
            public Label TitleLabel => labelTitle;

            protected override void OnFormClosing(FormClosingEventArgs e)
            {
                owner.StopAllMonitor();
                if (e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    Hide();
                }
                base.OnFormClosing(e);
            }
        }

        private readonly HashSet<Guid> monitoredVariableIds = new HashSet<Guid>();
        private ValueClipboardItem clipboardItem;
        private bool isValueStoreHooked;
        private ValueConfigStore hookedValueStore;
        private ValueMonitorForm monitorForm;
        private readonly TreeView scopeTree;
        private readonly DataGridViewTextBoxColumn scopeColumn;
        private readonly DataGridViewTextBoxColumn ownerColumn;
        private readonly Button btnClearSearch;
        private readonly TextBox txtVariableSearch;
        private readonly TextBox txtScopeSearch;
        private readonly Label lblVariableTitle;
        private readonly Label lblVariableSubtitle;
        private readonly Label lblEmptyState;
        private readonly Label lblScopeFooter;
        private readonly ToolTip uiToolTip;
        private List<VariableScopeOption> variableScopeOptions = new List<VariableScopeOption>();
        private string selectedScope = AllVariableScopes;
        private Guid? selectedOwnerProcId;
        private bool isStructViewAttached;
        private bool isValueGridReady;
        private bool isValueEditValid = true;
        private bool isScopeFilterPending;
        private bool refreshScopeTreePending;
        private const int MaxMonitorHistoryRows = 2000;
        private const int MonitorHistoryTrimRows = 200;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        [DllImport("user32.dll", EntryPoint = "SendMessageW")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        public FrmValue()
        {
            InitializeComponent();
            btnMonitor = new MaterialButton();
            btnMonitor.Click += btnMonitor_Click;
            btnMonitorAdd = new MaterialButton();
            btnMonitorAdd.Click += btnMonitorAdd_Click;
            btnCopy = new MaterialButton();
            btnCopy.Click += btnCopy_Click;
            btnPaste = new MaterialButton();
            btnPaste.Click += btnPaste_Click;
            btnClearData = new MaterialButton { Tone = MaterialButtonTone.Danger };
            btnClearData.Click += btnClearData_Click;
            btnSearch = new MaterialButton();
            btnSearch.Click += btnSearch_Click;
            value.HeaderText = "当前值";
            scopeColumn = new DataGridViewTextBoxColumn
            {
                Name = "variableScope",
                HeaderText = "作用域",
                ReadOnly = false,
                Width = 90,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            ownerColumn = new DataGridViewTextBoxColumn
            {
                Name = "ownerProcess",
                HeaderText = "所属流程",
                ReadOnly = true,
                Width = 160,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            dgvValue.Columns.Add(scopeColumn);
            dgvValue.Columns.Add(ownerColumn);

            labelCommon.Text = "变量作用域";
            listCommon.Visible = false;
            scopeTree = new TreeView
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                Font = new Font("Microsoft YaHei UI", 10.5F),
                HideSelection = false,
                DrawMode = TreeViewDrawMode.OwnerDrawAll,
                FullRowSelect = true,
                ItemHeight = 42,
                Indent = 18,
                ShowLines = false,
                ShowPlusMinus = false,
                ShowRootLines = false,
                ShowNodeToolTips = true,
                BackColor = SurfaceContainerColor
            };
            scopeTree.AfterSelect += ScopeTree_AfterSelect;
            scopeTree.DrawNode += ScopeTree_DrawNode;
            scopeTree.NodeMouseClick += ScopeTree_NodeMouseClick;

            btnNormalVariables.Text = "新增变量";
            btnSystemVariables.Visible = false;
            btnClearSearch = new MaterialButton { Text = "×" };
            txtVariableSearch = new TextBox();
            txtScopeSearch = new TextBox();
            lblVariableTitle = new Label();
            lblVariableSubtitle = new Label();
            lblEmptyState = new Label();
            lblScopeFooter = new Label();
            uiToolTip = new ToolTip
            {
                AutoPopDelay = 6000,
                InitialDelay = 350,
                ReshowDelay = 100
            };
            btnPrevious.Visible = false;
            BtnNext.Visible = false;
            btnCancel.Visible = false;
            Disposed += FrmValue_Disposed;
            Font uiFont = new Font("宋体", 12F, FontStyle.Regular, GraphicsUnit.Point, ((byte)(134)));
            dgvValue.Font = uiFont;
            dgvValue.ColumnHeadersDefaultCellStyle.Font = new Font("宋体", 12F, FontStyle.Bold, GraphicsUnit.Point, ((byte)(134)));
            dgvValue.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgvValue.MultiSelect = true;
            dgvValue.Columns[0].SortMode = DataGridViewColumnSortMode.NotSortable;
            dgvValue.Columns[0].ReadOnly = true;
            type.DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton;
            type.DisplayStyleForCurrentCellOnly = true;
            type.FlatStyle = FlatStyle.Flat;
            dgvValue.RowHeadersVisible = false;
            dgvValue.AutoGenerateColumns = false;


            Type dgvType = this.dgvValue.GetType();
            PropertyInfo pi = dgvType.GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
            pi.SetValue(this.dgvValue, true, null);

            dgvValue.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            BuildModernLayout();
            ApplyAlarmConfigStyle();
        }

        private void BuildModernLayout()
        {
            SuspendLayout();
            panel1.SuspendLayout();
            splitContainerMain.Panel1.SuspendLayout();
            splitContainerMain.Panel2.SuspendLayout();

            panel1.Controls.Clear();
            panel1.Height = 68;
            panel1.Padding = new Padding(12, 12, 12, 10);

            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = SurfaceColor,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };

            ConfigureToolbarButton(btnMonitorAdd, "加入监控", 96);
            ConfigureToolbarButton(btnMonitor, "监控记录", 96);
            ConfigureToolbarButton(btnCopy, "复制", 68);
            ConfigureToolbarButton(btnPaste, "粘贴", 68);
            ConfigureToolbarButton(btnClearData, "删除变量", 92);

            actions.Controls.Add(btnMonitorAdd);
            actions.Controls.Add(btnMonitor);
            actions.Controls.Add(btnCopy);
            actions.Controls.Add(btnPaste);
            actions.Controls.Add(btnClearData);

            btnMonitorRemove.Visible = false;
            btnSystemVariables.Visible = false;
            btnPrevious.Visible = false;
            BtnNext.Visible = false;
            btnCancel.Visible = false;

            var searchPanel = new Panel
            {
                Dock = DockStyle.Right,
                Width = 340,
                Padding = new Padding(10, 0, 0, 0),
                BackColor = SurfaceColor
            };
            ConfigureToolbarButton(btnSearch, "搜索", 72);
            btnSearch.Dock = DockStyle.Right;
            btnSearch.Margin = Padding.Empty;

            var searchField = new MaterialInputHost
            {
                Dock = DockStyle.Fill,
                Height = 40,
                Padding = Padding.Empty,
                BackColor = SurfaceContainerColor
            };
            txtVariableSearch.Dock = DockStyle.None;
            txtVariableSearch.AutoSize = false;
            txtVariableSearch.BorderStyle = BorderStyle.None;
            txtVariableSearch.BackColor = SurfaceContainerColor;
            txtVariableSearch.ForeColor = HeaderForeColor;
            txtVariableSearch.Font = new Font("Microsoft YaHei UI", 10F);
            txtVariableSearch.AccessibleName = "搜索变量";
            txtVariableSearch.TextChanged += txtVariableSearch_TextChanged;
            txtVariableSearch.KeyDown += txtVariableSearch_KeyDown;
            SendMessage(txtVariableSearch.Handle, EmSetCueBanner, IntPtr.Zero, "搜索全部变量");
            btnClearSearch.Dock = DockStyle.None;
            btnClearSearch.Size = new Size(30, 30);
            btnClearSearch.Font = new Font("Microsoft YaHei UI", 12F);
            btnClearSearch.Cursor = Cursors.Hand;
            btnClearSearch.Click += btnClearSearch_Click;
            searchField.Controls.Add(txtVariableSearch);
            searchField.Controls.Add(btnClearSearch);
            searchField.Resize += (sender, args) =>
            {
                int textHeight = 24;
                txtVariableSearch.SetBounds(
                    14,
                    Math.Max(0, (searchField.ClientSize.Height - textHeight) / 2),
                    Math.Max(20, searchField.ClientSize.Width - 54),
                    textHeight);
                btnClearSearch.Location = new Point(
                    Math.Max(0, searchField.ClientSize.Width - 36),
                    Math.Max(0, (searchField.ClientSize.Height - btnClearSearch.Height) / 2));
            };
            searchPanel.Controls.Add(searchField);
            searchPanel.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 8, BackColor = SurfaceColor });
            searchPanel.Controls.Add(btnSearch);
            panel1.Controls.Add(actions);
            panel1.Controls.Add(searchPanel);

            panelCommon.Controls.Clear();
            panelCommon.Width = 272;
            panelCommon.Padding = new Padding(8, 0, 8, 8);
            panelCommon.BackColor = SurfaceContainerColor;

            var scopeHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 72,
                Padding = new Padding(10, 10, 8, 4),
                BackColor = SurfaceContainerColor
            };
            labelCommon.Dock = DockStyle.Top;
            labelCommon.Height = 31;
            labelCommon.Padding = Padding.Empty;
            labelCommon.Text = "变量空间";
            labelCommon.TextAlign = ContentAlignment.MiddleLeft;
            var scopeSubtitle = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 24,
                Text = "按作用域浏览与管理",
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = SecondaryForeColor
            };
            scopeHeader.Controls.Add(scopeSubtitle);
            scopeHeader.Controls.Add(labelCommon);

            var scopeFilterHost = new Panel
            {
                Dock = DockStyle.Top,
                Height = 72,
                Padding = new Padding(8, 0, 8, 7),
                BackColor = SurfaceContainerColor
            };
            var scopeFilterLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 26,
                Text = "筛选流程",
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Microsoft YaHei UI", 8.5F),
                ForeColor = SecondaryForeColor
            };
            var scopeFilterField = new MaterialInputHost
            {
                Dock = DockStyle.Fill,
                Padding = Padding.Empty,
                BackColor = Color.White
            };
            txtScopeSearch.Dock = DockStyle.None;
            txtScopeSearch.AutoSize = false;
            txtScopeSearch.BorderStyle = BorderStyle.None;
            txtScopeSearch.BackColor = Color.White;
            txtScopeSearch.Font = new Font("Microsoft YaHei UI", 9.5F);
            txtScopeSearch.AccessibleName = "筛选流程";
            txtScopeSearch.TextChanged += txtScopeSearch_TextChanged;
            SendMessage(txtScopeSearch.Handle, EmSetCueBanner, IntPtr.Zero, "输入流程名称");
            scopeFilterField.Controls.Add(txtScopeSearch);
            scopeFilterField.Resize += (sender, args) =>
            {
                int textHeight = 24;
                txtScopeSearch.SetBounds(
                    12,
                    Math.Max(0, (scopeFilterField.ClientSize.Height - textHeight) / 2),
                    Math.Max(20, scopeFilterField.ClientSize.Width - 24),
                    textHeight);
            };
            scopeFilterHost.Controls.Add(scopeFilterField);
            scopeFilterHost.Controls.Add(scopeFilterLabel);

            lblScopeFooter.Dock = DockStyle.Bottom;
            lblScopeFooter.Height = 45;
            lblScopeFooter.Padding = new Padding(10, 8, 8, 0);
            lblScopeFooter.Text = "名称与槽位在全平台唯一";
            lblScopeFooter.TextAlign = ContentAlignment.TopLeft;
            lblScopeFooter.Font = new Font("Microsoft YaHei UI", 8.5F);
            lblScopeFooter.ForeColor = SecondaryForeColor;
            lblScopeFooter.BackColor = SurfaceContainerColor;

            panelCommon.Controls.Add(scopeTree);
            panelCommon.Controls.Add(lblScopeFooter);
            panelCommon.Controls.Add(scopeFilterHost);
            panelCommon.Controls.Add(scopeHeader);

            var gridHost = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12, 10, 12, 12),
                BackColor = SurfaceColor
            };
            var gridCard = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                BorderStyle = BorderStyle.None
            };
            var gridHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 66,
                Padding = new Padding(16, 8, 16, 6),
                BackColor = Color.White
            };
            lblVariableTitle.Dock = DockStyle.Top;
            lblVariableTitle.Height = 28;
            lblVariableTitle.Font = new Font("Microsoft YaHei UI", 11.5F, FontStyle.Bold);
            lblVariableTitle.ForeColor = HeaderForeColor;
            lblVariableTitle.TextAlign = ContentAlignment.MiddleLeft;
            lblVariableSubtitle.Dock = DockStyle.Bottom;
            lblVariableSubtitle.Height = 22;
            lblVariableSubtitle.Font = new Font("Microsoft YaHei UI", 9F);
            lblVariableSubtitle.ForeColor = SecondaryForeColor;
            lblVariableSubtitle.TextAlign = ContentAlignment.MiddleLeft;
            gridHeader.Controls.Add(lblVariableSubtitle);
            gridHeader.Controls.Add(lblVariableTitle);

            dgvValue.Dock = DockStyle.Fill;
            dgvValue.BorderStyle = BorderStyle.None;
            lblEmptyState.Dock = DockStyle.Fill;
            lblEmptyState.BackColor = Color.White;
            lblEmptyState.ForeColor = SecondaryForeColor;
            lblEmptyState.Font = new Font("Microsoft YaHei UI", 10F);
            lblEmptyState.TextAlign = ContentAlignment.MiddleCenter;
            lblEmptyState.Visible = false;
            var gridBody = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White
            };
            gridBody.Controls.Add(dgvValue);
            gridBody.Controls.Add(lblEmptyState);
            gridCard.Controls.Add(gridBody);
            gridCard.Controls.Add(gridHeader);
            gridHost.Controls.Add(gridCard);
            splitContainerMain.Panel1.Controls.Clear();
            splitContainerMain.Panel1.Controls.Add(gridHost);
            splitContainerMain.Panel1.Controls.Add(panelCommon);

            var structHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 58,
                Padding = new Padding(16, 8, 12, 6),
                BackColor = HeaderBackColor
            };
            var structTitle = new Label
            {
                Dock = DockStyle.Top,
                Height = 26,
                Text = "数据结构",
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold),
                ForeColor = HeaderForeColor
            };
            var structSubtitle = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 20,
                Text = "变量相关结构定义",
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Microsoft YaHei UI", 8.5F),
                ForeColor = SecondaryForeColor
            };
            structHeader.Controls.Add(structSubtitle);
            structHeader.Controls.Add(structTitle);
            panelStructHost.Dock = DockStyle.Fill;
            panelStructHost.Padding = new Padding(8);
            panelStructHost.BackColor = SurfaceColor;
            splitContainerMain.Panel2.Controls.Clear();
            splitContainerMain.Panel2.Controls.Add(panelStructHost);
            splitContainerMain.Panel2.Controls.Add(structHeader);
            splitContainerMain.SplitterWidth = 1;
            splitContainerMain.BorderStyle = BorderStyle.None;
            Text = "变量管理";

            index.HeaderText = "槽位";
            index.Width = 72;
            name.Width = 190;
            type.Width = 96;
            value.Width = 160;
            scopeColumn.Width = 170;
            ownerColumn.Width = 140;
            scopeColumn.DisplayIndex = 4;
            ownerColumn.DisplayIndex = 5;
            Note.DisplayIndex = 6;
            Note.MinimumWidth = 160;
            Note.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

            uiToolTip.SetToolTip(txtVariableSearch, "跨作用域搜索名称、槽位、类型、备注或所属流程");
            uiToolTip.SetToolTip(txtScopeSearch, "按流程名称筛选左侧私有变量区域");
            uiToolTip.SetToolTip(btnMonitorAdd, "把所选变量加入运行值监控");

            splitContainerMain.Panel2.ResumeLayout();
            splitContainerMain.Panel1.ResumeLayout();
            panel1.ResumeLayout();
            ResumeLayout();
        }

        private static void ConfigureToolbarButton(Button button, string text, int width)
        {
            button.Text = text;
            button.Width = width;
            button.Height = 40;
            button.Margin = new Padding(3, 0, 3, 0);
            button.Anchor = AnchorStyles.None;
            button.Cursor = Cursors.Hand;
        }

        private void ApplyAlarmConfigStyle()
        {
            BackColor = SurfaceColor;
            panel1.BackColor = SurfaceColor;
            panelCommon.BackColor = SurfaceContainerColor;
            panelStructHost.BackColor = SurfaceColor;
            splitContainerMain.BackColor = GridLineColor;
            splitContainerMain.BorderStyle = BorderStyle.None;
            labelCommon.BackColor = SurfaceContainerColor;
            labelCommon.ForeColor = HeaderForeColor;
            labelCommon.Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold);

            listCommon.BackColor = Color.White;
            listCommon.ForeColor = HeaderForeColor;
            listCommon.BorderStyle = BorderStyle.FixedSingle;
            listCommon.Font = new Font("Microsoft YaHei UI", 10F);
            listCommon.DrawMode = DrawMode.OwnerDrawFixed;
            listCommon.ItemHeight = 30;
            listCommon.DrawItem += listCommon_DrawItem;

            ApplyGridStyle(dgvValue);
            ApplyButtonStyle(btnSearch, false, false);
            ApplyButtonStyle(btnPrevious, false, false);
            ApplyButtonStyle(BtnNext, false, false);
            ApplyButtonStyle(btnCancel, false, true);
            ApplyButtonStyle(btnMonitor, false, false);
            ApplyButtonStyle(btnMonitorAdd, false, false);
            ApplyButtonStyle(btnMonitorRemove, false, false);
            ApplyButtonStyle(btnCopy, false, false);
            ApplyButtonStyle(btnPaste, false, false);
            ApplyButtonStyle(btnClearData, false, true);
            ApplyButtonStyle(btnNormalVariables, false, false);
            ApplyButtonStyle(btnSystemVariables, false, false);
            scopeTree.Invalidate();
        }

        private static void ApplyGridStyle(DataGridView grid)
        {
            grid.Font = new Font("Microsoft YaHei UI", 10F);
            grid.BackgroundColor = Color.White;
            grid.BorderStyle = BorderStyle.None;
            grid.GridColor = GridLineColor;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersHeight = 42;
            grid.ColumnHeadersDefaultCellStyle.BackColor = HeaderBackColor;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = HeaderForeColor;
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.RowTemplate.Height = 38;
            grid.DefaultCellStyle.BackColor = Color.White;
            grid.DefaultCellStyle.ForeColor = HeaderForeColor;
            grid.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.DefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
            grid.AlternatingRowsDefaultCellStyle.BackColor = AlternateRowColor;
            grid.DefaultCellStyle.SelectionBackColor = SelectionBackColor;
            grid.DefaultCellStyle.SelectionForeColor = SelectionForeColor;
            if (grid.Columns.Contains("Note"))
            {
                grid.Columns["Note"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            }
        }

        private static void ApplyButtonStyle(Button button, bool primary, bool danger)
        {
            if (button is MaterialButton materialButton)
            {
                materialButton.Tone = primary
                    ? MaterialButtonTone.Primary
                    : danger ? MaterialButtonTone.Danger : MaterialButtonTone.Tonal;
                materialButton.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular);
                materialButton.Invalidate();
                return;
            }
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular);
            if (primary)
            {
                button.BackColor = PrimaryColor;
                button.ForeColor = Color.White;
                button.FlatAppearance.BorderColor = PrimaryColor;
                button.FlatAppearance.MouseOverBackColor = PrimaryHoverColor;
                button.FlatAppearance.MouseDownBackColor = PrimaryPressedColor;
            }
            else if (danger)
            {
                button.BackColor = ErrorContainerColor;
                button.ForeColor = ErrorColor;
                button.FlatAppearance.BorderColor = Color.FromArgb(255, 218, 214);
                button.FlatAppearance.MouseOverBackColor = Color.FromArgb(255, 221, 217);
                button.FlatAppearance.MouseDownBackColor = Color.FromArgb(255, 207, 202);
            }
            else
            {
                button.BackColor = SurfaceContainerColor;
                button.ForeColor = HeaderForeColor;
                button.FlatAppearance.BorderColor = GridLineColor;
                button.FlatAppearance.MouseOverBackColor = Color.FromArgb(232, 235, 243);
                button.FlatAppearance.MouseDownBackColor = Color.FromArgb(222, 226, 236);
            }
            button.UseVisualStyleBackColor = false;
        }

        private void listCommon_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0)
            {
                return;
            }
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            using (var background = new SolidBrush(selected ? SelectionBackColor
                : e.Index % 2 == 0 ? Color.White : AlternateRowColor))
            using (var foreground = new SolidBrush(selected ? SelectionForeColor : HeaderForeColor))
            {
                e.Graphics.FillRectangle(background, e.Bounds);
                string text = listCommon.Items[e.Index]?.ToString() ?? string.Empty;
                Rectangle textBounds = new Rectangle(e.Bounds.X + 10, e.Bounds.Y, e.Bounds.Width - 12, e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, text, listCommon.Font, textBounds, foreground.Color,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
            e.DrawFocusRectangle();
        }

        private void FrmValue_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (monitorForm != null && !monitorForm.IsDisposed)
            {
                StopAllMonitor();
                if (e.CloseReason == CloseReason.UserClosing)
                {
                    monitorForm.Hide();
                }
                else
                {
                    monitorForm.Dispose();
                }
            }
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
        }

        private void FrmValue_Disposed(object sender, EventArgs e)
        {
            if (isValueStoreHooked && hookedValueStore != null)
            {
                hookedValueStore.ValueChanged -= ValueStore_ValueChanged;
            }
            isValueStoreHooked = false;
            hookedValueStore = null;
            if (monitorForm != null && !monitorForm.IsDisposed)
            {
                monitorForm.Dispose();
            }
            monitorForm = null;
            uiToolTip?.Dispose();
        }
        //从文件更新变量表
        public void RefreshDic()
        {
            SF.valueStore.Load(SF.ConfigPath, SF.frmProc?.procsList);
            EnsureValueStoreHooked();

            RefreshValue();

            isValueGridReady = true;

        }
  
        public void RefreshValue()
        {
            dgvValue.Rows.Clear();
            RefreshVariableScopeOptions();

            for (int i = 0; i < ValueConfigStore.ValueCapacity; i++)
            {
                dgvValue.Rows.Add();

                dgvValue.Rows[i].Cells[0].Value = i;
                dgvValue.Rows[i].Cells[1].Value = string.Empty;
                dgvValue.Rows[i].Cells[2].Value = DefaultValueType;
                dgvValue.Rows[i].Cells[3].Value = DefaultValueText;
                dgvValue.Rows[i].Cells[4].Value = string.Empty;
                dgvValue.Rows[i].Cells[6].Value = string.Empty;
                if (SF.valueStore.TryGetValueByIndex(i, out DicValue cachedValue))
                {
                    dgvValue.Rows[i].Cells[1].Value = cachedValue.Name;
                    dgvValue.Rows[i].Cells[2].Value = cachedValue.Type;
                    dgvValue.Rows[i].Cells[3].Value = cachedValue.Value;
                    dgvValue.Rows[i].Cells[4].Value = cachedValue.Note;
                    dgvValue.Rows[i].Cells[6].Value = ResolveOwnerProcessName(cachedValue.OwnerProcId);
                }
                ConfigureVariableScopeCell(dgvValue.Rows[i], cachedValue);
                ApplyVariableRowEditingState(dgvValue.Rows[i], cachedValue);
            }
            RefreshScopeTree();
            ApplyScopeFilter();
            RefreshMonitorTitle();
            RefreshMonitorRows();
        }
        //刷新变量界面
        public void FreshFrmValue()
        {
            bool allowRefresh = isValueGridReady;
            isValueGridReady = false;
            try
            {
                if (dgvValue.Rows.Count != ValueConfigStore.ValueCapacity)
                {
                    RefreshValue();
                    return;
                }
                RefreshVariableScopeOptions();
                for (int i = 0; i < ValueConfigStore.ValueCapacity; i++)
                {
                    if (SF.valueStore.TryGetValueByIndex(i, out DicValue cachedValue))
                    {
                        dgvValue.Rows[i].Cells[0].Value = i;
                        dgvValue.Rows[i].Cells[1].Value = cachedValue.Name;
                        dgvValue.Rows[i].Cells[2].Value = cachedValue.Type;
                        dgvValue.Rows[i].Cells[3].Value = cachedValue.Value;
                        dgvValue.Rows[i].Cells[4].Value = cachedValue.Note;
                        dgvValue.Rows[i].Cells[6].Value = ResolveOwnerProcessName(cachedValue.OwnerProcId);
                        ConfigureVariableScopeCell(dgvValue.Rows[i], cachedValue);
                        ApplyVariableRowEditingState(dgvValue.Rows[i], cachedValue);
                    }
                    else
                    {
                        dgvValue.Rows[i].Cells[0].Value = i;
                        dgvValue.Rows[i].Cells[1].Value = string.Empty;
                        dgvValue.Rows[i].Cells[2].Value = DefaultValueType;
                        dgvValue.Rows[i].Cells[3].Value = DefaultValueText;
                        dgvValue.Rows[i].Cells[4].Value = string.Empty;
                        dgvValue.Rows[i].Cells[6].Value = string.Empty;
                        ConfigureVariableScopeCell(dgvValue.Rows[i], null);
                        ApplyVariableRowEditingState(dgvValue.Rows[i], null);
                    }
                }
                RefreshScopeTree();
                ApplyScopeFilter();
                RefreshMonitorTitle();
                RefreshMonitorRows();
            }
            finally
            {
                isValueGridReady = allowRefresh;
            }
        }

        private void RefreshScopeTree()
        {
            if (scopeTree == null) return;
            scopeTree.BeginUpdate();
            scopeTree.Nodes.Clear();
            List<DicValue> variables = SF.valueStore?.BuildSaveData().Values
                .Where(value => value != null)
                .ToList() ?? new List<DicValue>();
            int publicCount = variables.Count(value => string.Equals(
                value.Scope, VariableScopeContract.Public, StringComparison.Ordinal));
            int systemCount = variables.Count(value => string.Equals(
                value.Scope, VariableScopeContract.System, StringComparison.Ordinal));
            int processCount = variables.Count(value => string.Equals(
                value.Scope, VariableScopeContract.Process, StringComparison.Ordinal));

            TreeNode allNode = new TreeNode($"全部槽位    {ValueConfigStore.ValueCapacity}")
            {
                Tag = new VariableScopeSelection { Scope = AllVariableScopes }
            };
            TreeNode publicNode = new TreeNode($"公共变量    {publicCount}")
            {
                Tag = new VariableScopeSelection { Scope = VariableScopeContract.Public }
            };
            TreeNode systemNode = new TreeNode($"系统变量    {systemCount}")
            {
                Tag = new VariableScopeSelection { Scope = VariableScopeContract.System }
            };
            TreeNode processRoot = new TreeNode($"流程私有变量    {processCount}");
            string processFilter = txtScopeSearch?.Text?.Trim() ?? string.Empty;
            IEnumerable<Proc> processes = (SF.frmProc?.procsList ?? new List<Proc>())
                .Where(proc => proc?.head != null && proc.head.Id != Guid.Empty)
                .Where(proc => string.IsNullOrEmpty(processFilter)
                    || (proc.head.Name ?? string.Empty).IndexOf(processFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(proc => proc.head.Name ?? string.Empty, StringComparer.CurrentCultureIgnoreCase);
            foreach (Proc proc in processes)
            {
                int ownedCount = variables.Count(value =>
                    string.Equals(value.Scope, VariableScopeContract.Process, StringComparison.Ordinal)
                    && value.OwnerProcId == proc.head.Id);
                string processName = proc.head.Name ?? proc.head.Id.ToString("D");
                processRoot.Nodes.Add(new TreeNode($"{processName}    {ownedCount}")
                {
                    ToolTipText = processName,
                    Tag = new VariableScopeSelection
                    {
                        Scope = VariableScopeContract.Process,
                        OwnerProcId = proc.head.Id
                    }
                });
            }
            if (processRoot.Nodes.Count == 0)
            {
                processRoot.Nodes.Add(new TreeNode(string.IsNullOrEmpty(processFilter)
                    ? "暂无流程"
                    : "没有匹配的流程"));
            }
            scopeTree.Nodes.Add(allNode);
            scopeTree.Nodes.Add(publicNode);
            scopeTree.Nodes.Add(processRoot);
            scopeTree.Nodes.Add(systemNode);
            processRoot.Expand();
            TreeNode selectedNode = scopeTree.Nodes.Cast<TreeNode>()
                .SelectMany(FlattenTree)
                .FirstOrDefault(node => node.Tag is VariableScopeSelection selection
                    && string.Equals(selection.Scope, selectedScope, StringComparison.Ordinal)
                    && selection.OwnerProcId == selectedOwnerProcId)
                ?? allNode;
            scopeTree.SelectedNode = selectedNode;
            selectedNode.EnsureVisible();
            scopeTree.EndUpdate();
        }

        private void ScopeTree_DrawNode(object sender, DrawTreeNodeEventArgs e)
        {
            Rectangle rowBounds = new Rectangle(
                6,
                e.Bounds.Top + 3,
                Math.Max(0, scopeTree.ClientSize.Width - 12),
                Math.Max(0, scopeTree.ItemHeight - 6));
            bool selectable = e.Node.Tag is VariableScopeSelection;
            bool selected = selectable && e.Node == scopeTree.SelectedNode;

            using (var background = new SolidBrush(scopeTree.BackColor))
            {
                e.Graphics.FillRectangle(background, new Rectangle(0, e.Bounds.Top, scopeTree.ClientSize.Width, scopeTree.ItemHeight));
            }
            if (selected && rowBounds.Width > 0 && rowBounds.Height > 0)
            {
                using (GraphicsPath path = CreateRoundedPath(rowBounds, 10))
                using (var selectionBrush = new SolidBrush(PrimaryContainerColor))
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    e.Graphics.FillPath(selectionBrush, path);
                }
            }

            bool groupNode = e.Node.Parent == null && !selectable;
            bool placeholderNode = !selectable && e.Node.Parent != null;
            int left = e.Node.Level == 0 ? 18 : 36;
            Rectangle textBounds = new Rectangle(
                left,
                e.Bounds.Top,
                Math.Max(0, scopeTree.ClientSize.Width - left - 28),
                scopeTree.ItemHeight);
            Font font = groupNode
                ? new Font(scopeTree.Font, FontStyle.Bold)
                : scopeTree.Font;
            Color textColor = placeholderNode
                ? SecondaryForeColor
                : selected ? SelectionForeColor : HeaderForeColor;
            TextRenderer.DrawText(
                e.Graphics,
                e.Node.Text,
                font,
                textBounds,
                textColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            if (groupNode && e.Node.Nodes.Count > 0)
            {
                Rectangle chevronBounds = new Rectangle(scopeTree.ClientSize.Width - 30, e.Bounds.Top, 18, scopeTree.ItemHeight);
                using (var chevronFont = new Font("Microsoft YaHei UI", 11F))
                {
                    TextRenderer.DrawText(
                        e.Graphics,
                        e.Node.IsExpanded ? "⌄" : "›",
                        chevronFont,
                        chevronBounds,
                        SecondaryForeColor,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
                }
            }
            if (groupNode)
            {
                font.Dispose();
            }
        }

        private static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
        {
            int diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            var path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static GraphicsPath CreateRoundedPath(RectangleF bounds, float radius)
        {
            float diameter = Math.Min(radius * 2F, Math.Min(bounds.Width, bounds.Height));
            var path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private void ScopeTree_NodeMouseClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            if (e.Node?.Tag == null && e.Node?.Nodes.Count > 0)
            {
                if (e.Node.IsExpanded) e.Node.Collapse();
                else e.Node.Expand();
                scopeTree.Invalidate();
            }
        }

        private static IEnumerable<TreeNode> FlattenTree(TreeNode node)
        {
            yield return node;
            foreach (TreeNode child in node.Nodes)
            {
                foreach (TreeNode descendant in FlattenTree(child)) yield return descendant;
            }
        }

        private void ScopeTree_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (!(e.Node?.Tag is VariableScopeSelection selection)) return;
            selectedScope = selection.Scope;
            selectedOwnerProcId = selection.OwnerProcId;
            ApplyScopeFilter();
            RefreshDisplayedRuntimeValues();
        }

        private void txtScopeSearch_TextChanged(object sender, EventArgs e)
        {
            RefreshScopeTree();
        }

        private void txtVariableSearch_TextChanged(object sender, EventArgs e)
        {
            ApplyScopeFilter();
        }

        private void txtVariableSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Escape) return;
            txtVariableSearch.Clear();
            e.SuppressKeyPress = true;
        }

        private void btnClearSearch_Click(object sender, EventArgs e)
        {
            txtVariableSearch.Clear();
            txtVariableSearch.Focus();
        }

        private void ApplyScopeFilter()
        {
            if (dgvValue.Rows.Count == 0) return;
            int visibleCount = 0;
            string searchText = txtVariableSearch?.Text?.Trim() ?? string.Empty;
            bool searching = searchText.Length > 0;
            bool allScopesSelected = string.Equals(selectedScope, AllVariableScopes, StringComparison.Ordinal);
            if (dgvValue.CurrentCell != null
                && !ShouldDisplayVariableRow(
                    dgvValue.CurrentCell.RowIndex, searching, searchText, allScopesSelected))
            {
                dgvValue.CurrentCell = null;
            }
            bool suspendRedraw = dgvValue.IsHandleCreated;
            if (suspendRedraw)
            {
                SendMessage(dgvValue.Handle, WmSetRedraw, IntPtr.Zero, IntPtr.Zero);
            }
            dgvValue.SuspendLayout();
            try
            {
                for (int index = 0; index < dgvValue.Rows.Count; index++)
                {
                    bool visible = ShouldDisplayVariableRow(index, searching, searchText, allScopesSelected);
                    DataGridViewRow row = dgvValue.Rows[index];
                    if (row.Visible != visible)
                    {
                        row.Visible = visible;
                    }
                    bool scopeEditable = allScopesSelected
                        && SF.valueStore.TryGetValueByIndex(index, out DicValue editableVariable)
                        && !ValueConfigStore.IsSystemValueIndex(editableVariable.Index)
                        && row.Cells[5] is DataGridViewComboBoxCell;
                    if (row.Cells[5].ReadOnly == scopeEditable)
                    {
                        row.Cells[5].ReadOnly = !scopeEditable;
                    }
                    if (visible) visibleCount++;
                }
            }
            finally
            {
                dgvValue.ResumeLayout();
                if (suspendRedraw)
                {
                    SendMessage(dgvValue.Handle, WmSetRedraw, new IntPtr(1), IntPtr.Zero);
                    dgvValue.Invalidate();
                }
            }
            string scopeTitle = allScopesSelected
                ? "全部变量槽位"
                : string.Equals(selectedScope, VariableScopeContract.Process, StringComparison.Ordinal)
                ? $"{ResolveOwnerProcessName(selectedOwnerProcId)} · 私有变量"
                : string.Equals(selectedScope, VariableScopeContract.System, StringComparison.Ordinal)
                    ? "系统变量"
                    : "公共变量";
            lblVariableTitle.Text = searching ? $"搜索结果 · {visibleCount}" : $"{scopeTitle} · {visibleCount}";
            lblVariableSubtitle.Text = searching
                ? $"正在跨全部作用域查找“{searchText}”"
                : allScopesSelected
                    ? "显示全部固定槽位；空槽可直接输入，系统槽位也可手动维护"
                    : "左侧仅筛选显示范围，变量仍在表格中直接编辑";
            lblEmptyState.Text = searching
                ? "没有找到匹配的变量\r\n可尝试名称、槽位、类型、备注或流程名称"
                : string.Equals(selectedScope, VariableScopeContract.System, StringComparison.Ordinal)
                    ? "当前没有可显示的系统变量"
                    : "当前筛选范围没有已配置变量";
            lblEmptyState.Visible = visibleCount == 0;
            if (btnClearSearch.Visible != searching)
            {
                btnClearSearch.Visible = searching;
            }
        }

        private bool ShouldDisplayVariableRow(
            int index, bool searching, string searchText, bool allScopesSelected)
        {
            if (!SF.valueStore.TryGetValueByIndex(index, out DicValue variable))
            {
                return !searching && allScopesSelected;
            }
            if (searching)
            {
                return IsVariableSearchMatch(variable, searchText);
            }
            return allScopesSelected
                || string.Equals(variable.Scope, selectedScope, StringComparison.Ordinal)
                && (!string.Equals(selectedScope, VariableScopeContract.Process, StringComparison.Ordinal)
                    || variable.OwnerProcId == selectedOwnerProcId);
        }

        private void ScheduleScopeFilter(bool refreshScopeTree = false)
        {
            refreshScopeTreePending |= refreshScopeTree;
            if (isScopeFilterPending || IsDisposed || Disposing || !IsHandleCreated)
            {
                return;
            }
            isScopeFilterPending = true;
            try
            {
                BeginInvoke((Action)(() =>
                {
                    isScopeFilterPending = false;
                    bool rebuildScopeTree = refreshScopeTreePending;
                    refreshScopeTreePending = false;
                    if (!IsDisposed && !Disposing)
                    {
                        if (rebuildScopeTree)
                        {
                            RefreshScopeTree();
                        }
                        else
                        {
                            ApplyScopeFilter();
                        }
                    }
                }));
            }
            catch (InvalidOperationException)
            {
                isScopeFilterPending = false;
                refreshScopeTreePending = false;
            }
        }

        private void RefreshVariableScopeOptions()
        {
            var options = new List<VariableScopeOption>
            {
                new VariableScopeOption
                {
                    Key = VariableScopeContract.Public,
                    Text = "公共变量",
                    Scope = VariableScopeContract.Public
                }
            };
            options.AddRange((SF.frmProc?.procsList ?? new List<Proc>())
                .Where(proc => proc?.head != null && proc.head.Id != Guid.Empty)
                .OrderBy(proc => proc.head.Name ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
                .Select(proc => new VariableScopeOption
                {
                    Key = BuildVariableScopeOptionKey(VariableScopeContract.Process, proc.head.Id),
                    Text = $"流程私有 · {proc.head.Name}",
                    Scope = VariableScopeContract.Process,
                    OwnerProcId = proc.head.Id
                }));
            variableScopeOptions = options;
        }

        private void ConfigureVariableScopeCell(DataGridViewRow row, DicValue variable)
        {
            if (variable != null && !ValueConfigStore.IsSystemValueIndex(variable.Index))
            {
                string optionKey = BuildVariableScopeOptionKey(variable.Scope, variable.OwnerProcId);
                if (variableScopeOptions.Any(option => string.Equals(option.Key, optionKey, StringComparison.Ordinal)))
                {
                    var comboCell = row.Cells[5] as DataGridViewComboBoxCell;
                    if (comboCell == null)
                    {
                        comboCell = new DataGridViewComboBoxCell();
                        row.Cells[5] = comboCell;
                    }
                    comboCell.DataSource = variableScopeOptions;
                    comboCell.DisplayMember = nameof(VariableScopeOption.Text);
                    comboCell.ValueMember = nameof(VariableScopeOption.Key);
                    comboCell.DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton;
                    comboCell.DisplayStyleForCurrentCellOnly = true;
                    comboCell.FlatStyle = FlatStyle.Flat;
                    comboCell.Value = optionKey;
                    comboCell.ReadOnly = !string.Equals(selectedScope, AllVariableScopes, StringComparison.Ordinal);
                    return;
                }
            }

            var textCell = row.Cells[5] as DataGridViewTextBoxCell;
            if (textCell == null)
            {
                textCell = new DataGridViewTextBoxCell();
                row.Cells[5] = textCell;
            }
            textCell.Value = variable == null
                ? ValueConfigStore.IsSystemValueIndex(row.Index) ? "系统" : "未配置"
                : FormatVariableScope(variable);
            textCell.ReadOnly = true;
        }

        private bool TryResolveVariableScopeOption(
            object selectedValue, out string scope, out Guid? ownerProcId)
        {
            string optionKey = Convert.ToString(selectedValue);
            VariableScopeOption option = variableScopeOptions.FirstOrDefault(item =>
                string.Equals(item.Key, optionKey, StringComparison.Ordinal));
            scope = option?.Scope;
            ownerProcId = option?.OwnerProcId;
            return option != null;
        }

        private static string BuildVariableScopeOptionKey(string scope, Guid? ownerProcId)
        {
            return string.Equals(scope, VariableScopeContract.Process, StringComparison.Ordinal)
                ? $"{VariableScopeContract.Process}:{ownerProcId?.ToString("D") ?? string.Empty}"
                : VariableScopeContract.Public;
        }

        private static void ApplyVariableRowEditingState(DataGridViewRow row, DicValue variable)
        {
            bool protectedSystemVariable = variable != null
                && ValueConfigStore.IsProtectedValueName(variable.Name);
            row.Cells[1].ReadOnly = protectedSystemVariable;
            row.Cells[2].ReadOnly = protectedSystemVariable;
        }

        private static bool IsVariableSearchMatch(DicValue value, string searchText)
        {
            if (value == null || string.IsNullOrEmpty(searchText)) return false;
            string ownerName = ResolveOwnerProcessName(value.OwnerProcId);
            return value.Index.ToString().IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0
                || (value.Name ?? string.Empty).IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0
                || (value.Type ?? string.Empty).IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0
                || (value.Note ?? string.Empty).IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0
                || ownerName.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0
                || (value.Scope ?? string.Empty).IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string FormatVariableScope(DicValue value)
        {
            if (value == null) return "未配置";
            if (string.Equals(value.Scope, VariableScopeContract.Process, StringComparison.Ordinal))
            {
                string ownerName = ResolveOwnerProcessName(value.OwnerProcId);
                return string.IsNullOrWhiteSpace(ownerName) ? "流程私有" : $"流程私有 · {ownerName}";
            }
            if (string.Equals(value.Scope, VariableScopeContract.System, StringComparison.Ordinal)) return "系统";
            return "公共";
        }

        private static string ResolveOwnerProcessName(Guid? ownerProcId)
        {
            if (!ownerProcId.HasValue) return string.Empty;
            return (SF.frmProc?.procsList ?? new List<Proc>())
                .FirstOrDefault(proc => proc?.head?.Id == ownerProcId.Value)?.head?.Name ?? string.Empty;
        }

        public void LocateProcessVariables(Guid procId)
        {
            if (procId == Guid.Empty) return;
            selectedScope = VariableScopeContract.Process;
            selectedOwnerProcId = procId;
            RefreshScopeTree();
            ApplyScopeFilter();
        }
        /*=============================================================================================*/
        private void FrmValue_Load(object sender, EventArgs e)
        {
            EnsureValueStoreHooked();
            selectedScope = AllVariableScopes;
            selectedOwnerProcId = null;
            RefreshScopeTree();
            ApplyScopeFilter();
            AttachDataStructView();
            SetDefaultStructPanelRatio();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible) return;
            FreshFrmValue();
        }

        private void RefreshDisplayedRuntimeValues()
        {
            if (!Visible || !isValueGridReady || SF.valueStore == null || dgvValue.Rows.Count == 0)
            {
                return;
            }
            int editingRowIndex = dgvValue.IsCurrentCellInEditMode && dgvValue.CurrentCell != null
                ? dgvValue.CurrentCell.RowIndex
                : -1;
            bool allowGridEvents = isValueGridReady;
            isValueGridReady = false;
            try
            {
                int rowIndex = dgvValue.Rows.GetFirstRow(DataGridViewElementStates.Displayed);
                while (rowIndex >= 0)
                {
                    if (rowIndex != editingRowIndex
                        && SF.valueStore.TryGetValueByIndex(rowIndex, out DicValue variable))
                    {
                        string currentValue = variable.Value ?? string.Empty;
                        DataGridViewCell valueCell = dgvValue.Rows[rowIndex].Cells[3];
                        if (!string.Equals(Convert.ToString(valueCell.Value), currentValue, StringComparison.Ordinal))
                        {
                            valueCell.Value = currentValue;
                        }
                    }
                    rowIndex = dgvValue.Rows.GetNextRow(rowIndex, DataGridViewElementStates.Displayed);
                }
            }
            finally
            {
                isValueGridReady = allowGridEvents;
            }
        }

        private void AttachDataStructView()
        {
            if (isStructViewAttached || panelStructHost == null)
            {
                return;
            }
            if (SF.frmdataStruct == null || SF.frmdataStruct.IsDisposed)
            {
                return;
            }

            SF.frmdataStruct.TopLevel = false;
            SF.frmdataStruct.FormBorderStyle = FormBorderStyle.None;
            SF.frmdataStruct.Dock = DockStyle.Fill;

            if (!panelStructHost.Controls.Contains(SF.frmdataStruct))
            {
                panelStructHost.Controls.Add(SF.frmdataStruct);
            }

            SF.frmdataStruct.Show();
            SF.frmdataStruct.BringToFront();
            isStructViewAttached = true;
        }

        private void SetDefaultStructPanelRatio()
        {
            if (splitContainerMain == null || splitContainerMain.Width <= 0)
            {
                return;
            }
            int targetLeftWidth = (int)(splitContainerMain.Width * 0.80);
            int minLeftWidth = splitContainerMain.Panel1MinSize;
            int maxLeftWidth = splitContainerMain.Width - splitContainerMain.Panel2MinSize - splitContainerMain.SplitterWidth;
            if (targetLeftWidth < minLeftWidth)
            {
                targetLeftWidth = minLeftWidth;
            }
            if (targetLeftWidth > maxLeftWidth)
            {
                targetLeftWidth = maxLeftWidth;
            }
            splitContainerMain.SplitterDistance = targetLeftWidth;
        }

        private void dgvValue_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (isValueGridReady)
            {
                // 确保值变化发生在单元格中而不是在行标题或列标题
                if (e.RowIndex >= 0 && e.ColumnIndex >= 0)
                {
                    if (e.ColumnIndex == 2)
                    {
                        DataGridViewTextBoxCell textboxCell = new DataGridViewTextBoxCell();
                        dgvValue[e.ColumnIndex + 1, e.RowIndex] = textboxCell;
                    }
                   
                }
            }
           
        }

        private void dgvValue_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true; // 阻止默认行为 防止选择条向下切换
                return;
            }
            if (dgvValue.IsCurrentCellInEditMode)
            {
                return;
            }
            if (e.Control && e.KeyCode == Keys.C)
            {
                CopySelectedValueRow();
                e.Handled = true;
                return;
            }
            if (e.Control && e.KeyCode == Keys.V)
            {
                PasteToSelectedValueRow();
                e.Handled = true;
                return;
            }
            if (e.KeyCode == Keys.Delete)
            {
                ClearSelectedValueRows(true);
                e.Handled = true;
                return;
            }
        }

        private bool TryGetSingleSelectedRowIndex(out int rowIndex)
        {
            rowIndex = -1;
            if (dgvValue.SelectedRows != null && dgvValue.SelectedRows.Count > 0)
            {
                if (dgvValue.SelectedRows.Count > 1)
                {
                    MessageBox.Show("一次只能操作一行");
                    return false;
                }
                rowIndex = dgvValue.SelectedRows[0].Index;
                return rowIndex >= 0;
            }
            if (dgvValue.CurrentCell == null)
            {
                MessageBox.Show("没有选定的变量");
                return false;
            }
            rowIndex = dgvValue.CurrentCell.RowIndex;
            return rowIndex >= 0;
        }

        private List<int> GetSelectedRowIndexes()
        {
            HashSet<int> indexes = new HashSet<int>();
            if (dgvValue.SelectedRows != null && dgvValue.SelectedRows.Count > 0)
            {
                foreach (DataGridViewRow row in dgvValue.SelectedRows)
                {
                    if (row != null && row.Index >= 0)
                    {
                        indexes.Add(row.Index);
                    }
                }
            }
            else if (dgvValue.CurrentCell != null)
            {
                indexes.Add(dgvValue.CurrentCell.RowIndex);
            }
            return new List<int>(indexes);
        }

        private bool TryValidateClipboardData(string name, string type, string value, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(name))
            {
                error = "名称为空";
                return false;
            }
            if (type != "double" && type != "string")
            {
                error = "类型无效";
                return false;
            }
            if (string.IsNullOrEmpty(value))
            {
                error = "值为空";
                return false;
            }
            if (type == "double" && !double.TryParse(value, out _))
            {
                error = "值不是有效数字";
                return false;
            }
            return true;
        }

        private void CopySelectedValueRow()
        {
            if (!TryGetSingleSelectedRowIndex(out int rowIndex))
            {
                return;
            }
            if (!SF.valueStore.TryGetValueByIndex(rowIndex, out DicValue value))
            {
                MessageBox.Show("当前行没有可复制的变量");
                return;
            }
            if (!TryValidateClipboardData(value.Name, value.Type, value.Value, out string error))
            {
                MessageBox.Show($"复制失败:{error}");
                return;
            }
            clipboardItem = new ValueClipboardItem
            {
                Name = value.Name,
                Type = value.Type,
                Value = value.Value,
                Note = value.Note
            };
        }

        private void PasteToSelectedValueRow()
        {
            if (!SF.CanEditProcStructure()) return;
            if (clipboardItem == null)
            {
                MessageBox.Show("没有可粘贴的数据");
                return;
            }
            if (!TryGetSingleSelectedRowIndex(out int rowIndex)) return;
            SF.valueStore.TryGetValueByIndex(rowIndex, out DicValue targetVariable);
            if (targetVariable != null && ValueConfigStore.IsProtectedValueName(targetVariable.Name))
            {
                MessageBox.Show("系统保留变量不能被粘贴内容覆盖。", "粘贴变量",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string nameToUse = BuildPasteName(clipboardItem.Name);
            if (string.IsNullOrEmpty(nameToUse))
            {
                MessageBox.Show("粘贴名称无效");
                return;
            }
            if (!TryValidateClipboardData(nameToUse, clipboardItem.Type, clipboardItem.Value, out string error))
            {
                MessageBox.Show($"粘贴失败:{error}");
                return;
            }
            string targetScope = ValueConfigStore.IsSystemValueIndex(rowIndex)
                ? VariableScopeContract.System
                : targetVariable?.Scope
                    ?? (string.Equals(selectedScope, VariableScopeContract.Process, StringComparison.Ordinal)
                        ? VariableScopeContract.Process
                        : VariableScopeContract.Public);
            Guid? targetOwnerProcId = string.Equals(targetScope, VariableScopeContract.Process, StringComparison.Ordinal)
                ? targetVariable?.OwnerProcId ?? selectedOwnerProcId
                : null;
            if (!TryCommitVariableRow(
                rowIndex, nameToUse, clipboardItem.Type, clipboardItem.Value, clipboardItem.Note,
                targetScope,
                targetOwnerProcId,
                true,
                out string commitError))
            {
                MessageBox.Show("粘贴失败:" + commitError);
                return;
            }
            txtVariableSearch.Clear();
            FreshFrmValue();
            LocateValueIndex(rowIndex);
        }

        private string BuildPasteName(string baseName)
        {
            if (string.IsNullOrWhiteSpace(baseName))
            {
                return null;
            }
            string name = baseName.Trim();
            int suffix = 1;
            while (suffix < 100000)
            {
                string candidate = $"{name}{suffix}";
                if (!SF.valueStore.TryGetValueByName(candidate, out _))
                {
                    return candidate;
                }
                suffix++;
            }
            return null;
        }

        private void ClearSelectedValueRows(bool requireConfirm = false)
        {
            if (!SF.CanEditProcStructure()) return;
            List<int> indexes = GetSelectedRowIndexes();
            if (indexes.Count == 0)
            {
                MessageBox.Show("没有选定的变量");
                return;
            }
            List<DicValue> variables = indexes
                .Select(index => SF.valueStore.TryGetValueByIndex(index, out DicValue value) ? value : null)
                .Where(value => value != null)
                .ToList();
            if (variables.Count == 0)
            {
                MessageBox.Show("选中的槽位尚未配置变量。", "删除变量",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (variables.Any(value => ValueConfigStore.IsSystemValueIndex(value.Index)))
            {
                MessageBox.Show("系统变量配置只读。");
                return;
            }
            if (requireConfirm)
            {
                int usageCount = variables.Sum(CountVariableUsages);
                DialogResult result = MessageBox.Show(
                    $"确认删除选中的{variables.Count}个变量？检测到{usageCount}个已知引用；引用文本将保留，受影响流程会变为 incomplete。",
                    "删除变量确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (result != DialogResult.Yes)
                {
                    return;
                }
            }
            Dictionary<string, DicValue> draft = SF.valueStore.BuildSaveData();
            foreach (DicValue variable in variables)
            {
                draft.Remove(variable.Name);
            }
            if (!SF.valueStore.TryCommitConfiguration(SF.ConfigPath, draft, out string error))
            {
                MessageBox.Show(error, "删除变量失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            FreshFrmValue();
        }

        private bool TryBuildRowValueForSave(DataGridView dataGridView, int rowIndex, out int num, out string key, out string type, out string value, out string note)
        {
            num = -1;
            key = string.Empty;
            type = DefaultValueType;
            value = string.Empty;
            note = string.Empty;

            DataGridViewRow row = dataGridView.Rows[rowIndex];
            num = (int)row.Cells[0].Value;
            key = row.Cells[1].Value?.ToString()?.Trim();
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            type = row.Cells[2].Value?.ToString()?.Trim();
            if (string.IsNullOrEmpty(type))
            {
                type = DefaultValueType;
            }
            if (!string.Equals(type, "double", StringComparison.Ordinal) && !string.Equals(type, "string", StringComparison.Ordinal))
            {
                return false;
            }

            value = row.Cells[3].Value?.ToString() ?? string.Empty;
            if (string.Equals(type, "double", StringComparison.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    value = DefaultValueText;
                }
                if (!double.TryParse(value, out _))
                {
                    return false;
                }
            }

            note = row.Cells[4].Value?.ToString() ?? string.Empty;
            return true;
        }

        private void dgvValue_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (isValueGridReady)
            {
                // 确保值变化发生在单元格中而不是在行标题或列标题
                if (e.RowIndex >= 0 && e.ColumnIndex >= 0)
                {
                    DataGridView dataGridView = (DataGridView)sender;
                    if (e.ColumnIndex == 3
                        && SF.valueStore.TryGetValueByIndex(e.RowIndex, out DicValue existing))
                    {
                        string editedValue = dataGridView.Rows[e.RowIndex].Cells[3].Value?.ToString() ?? string.Empty;
                        if (!SF.valueStore.setValueByIndex(existing.Index, editedValue, "变量页设置当前值"))
                        {
                            isValueEditValid = false;
                            MessageBox.Show($"变量[{existing.Name}]当前值无效。", "设置当前值失败",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                            FreshFrmValue();
                            return;
                        }
                        isValueEditValid = true;
                        return;
                    }
                    if (!SF.CanEditProcStructure())
                    {
                        isValueEditValid = false;
                        FreshFrmValue();
                        return;
                    }
                    isValueEditValid = !string.IsNullOrWhiteSpace(dataGridView.Rows[e.RowIndex].Cells[1].Value?.ToString());
                    if (!TryBuildRowValueForSave(dataGridView, e.RowIndex, out int num, out string key, out string type, out string value, out string note))
                    {
                        isValueEditValid = false;
                        return;
                    }
                    if (e.ColumnIndex == 1
                        && SF.valueStore.TryGetValueByIndex(num, out DicValue renamedVariable)
                        && !string.Equals(renamedVariable.Name, key, StringComparison.Ordinal))
                    {
                        int usageCount = CountVariableUsages(renamedVariable);
                        if (MessageBox.Show(
                            $"变量[{renamedVariable.Name}]存在{usageCount}个已知引用。重命名后引用文本保持不变，受影响流程会变为 incomplete。是否提交？",
                            "确认重命名变量", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                        {
                            isValueEditValid = false;
                            FreshFrmValue();
                            return;
                        }
                    }
                    string targetScope = ValueConfigStore.IsSystemValueIndex(num)
                        ? VariableScopeContract.System
                        : string.Equals(selectedScope, VariableScopeContract.Process, StringComparison.Ordinal)
                            ? VariableScopeContract.Process
                            : VariableScopeContract.Public;
                    Guid? targetOwnerProcId = string.Equals(targetScope, VariableScopeContract.Process, StringComparison.Ordinal)
                        ? selectedOwnerProcId
                        : null;
                    if (SF.valueStore.TryGetValueByIndex(num, out DicValue scopedVariable))
                    {
                        targetScope = scopedVariable.Scope;
                        targetOwnerProcId = scopedVariable.OwnerProcId;
                    }
                    if (e.ColumnIndex == scopeColumn.Index)
                    {
                        if (!string.Equals(selectedScope, AllVariableScopes, StringComparison.Ordinal)
                            || ValueConfigStore.IsSystemValueIndex(num)
                            || scopedVariable == null
                            || !TryResolveVariableScopeOption(
                                dataGridView.Rows[e.RowIndex].Cells[scopeColumn.Index].Value,
                                out targetScope,
                                out targetOwnerProcId))
                        {
                            isValueEditValid = false;
                            ConfigureVariableScopeCell(dataGridView.Rows[e.RowIndex], scopedVariable);
                            return;
                        }
                        bool scopeChanged = !string.Equals(
                                scopedVariable.Scope, targetScope, StringComparison.Ordinal)
                            || scopedVariable.OwnerProcId != targetOwnerProcId;
                        if (scopeChanged)
                        {
                            int usageCount = CountVariableUsages(scopedVariable);
                            if (MessageBox.Show(
                                $"变量[{scopedVariable.Name}]存在{usageCount}个已知引用。更改归属后，不可访问该变量的流程会变为 incomplete。是否提交？",
                                "确认更改变量归属", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                            {
                                isValueEditValid = false;
                                ConfigureVariableScopeCell(dataGridView.Rows[e.RowIndex], scopedVariable);
                                return;
                            }
                        }
                    }
                    if (!TryCommitVariableRow(
                        num, key, type, value, note, targetScope, targetOwnerProcId,
                        scopedVariable == null,
                        out string commitError))
                    {
                        isValueEditValid = false;
                        MessageBox.Show(commitError, "保存变量失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                    dataGridView.Rows[e.RowIndex].Cells[1].Value = key;
                    dataGridView.Rows[e.RowIndex].Cells[2].Value = type;
                    dataGridView.Rows[e.RowIndex].Cells[3].Value = value;
                    dataGridView.Rows[e.RowIndex].Cells[4].Value = note;
                    var committedVariable = new DicValue
                    {
                        Index = num,
                        Name = key,
                        Scope = targetScope,
                        OwnerProcId = targetOwnerProcId
                    };
                    ConfigureVariableScopeCell(dataGridView.Rows[e.RowIndex], committedVariable);
                    ApplyVariableRowEditingState(dataGridView.Rows[e.RowIndex], committedVariable);
                    dataGridView.Rows[e.RowIndex].Cells[6].Value = ResolveOwnerProcessName(targetOwnerProcId);
                    ScheduleScopeFilter(true);
                    isValueEditValid = true;
                }
            }
        }

        private void dgvValue_CellMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            RefreshDisplayedRuntimeValues();
            if (e.Button == MouseButtons.Right && e.RowIndex >= 0)
            {
                dgvValue.ClearSelection();
                dgvValue.Rows[e.RowIndex].Selected = true;
                dgvValue.CurrentCell = dgvValue.Rows[e.RowIndex].Cells[Math.Max(0, e.ColumnIndex)];
            }
        }

        int currentIndex = -1;
        private void btnPrevious_Click(object sender, EventArgs e)
        {
            int previousIndex = -1;
            int StartIndex = currentIndex - 1;
            if (StartIndex < 0)
            {
                StartIndex = ValueConfigStore.ValueCapacity - 1;
            }
            for (int i = StartIndex; i >= 0; i--)
            {
                if (SF.valueStore.IsMarked(i))
                {
                    previousIndex = i;
                    break;
                }
            }
            if (previousIndex == -1)
            {
                for (int i = ValueConfigStore.ValueCapacity - 1; i >= 0; i--)
                {
                    if (SF.valueStore.IsMarked(i))
                    {
                        previousIndex = i;
                        break;
                    }
                }
            }
            if (previousIndex != -1)
            {
                currentIndex = previousIndex;
                dgvValue.FirstDisplayedScrollingRowIndex = currentIndex;
            }

        }

        private void BtnNext_Click(object sender, EventArgs e)
        {
            int nextIndex = -1;
            int StartIndex = currentIndex + 1;
            if (StartIndex < 0)
            {
                StartIndex = 0;
            }
            for (int i = StartIndex; i < ValueConfigStore.ValueCapacity; i++)
            {
                if (SF.valueStore.IsMarked(i))
                {
                    nextIndex = i;
                    break;
                }
            }
            if (nextIndex == -1)
            {
                for (int i = 0; i < ValueConfigStore.ValueCapacity; i++)
                {
                    if (SF.valueStore.IsMarked(i))
                    {
                        nextIndex = i;
                        break;
                    }
                }
            }
            if (nextIndex != -1)
            {
                currentIndex = nextIndex;
                dgvValue.FirstDisplayedScrollingRowIndex = currentIndex;
            }
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            SF.valueStore.ClearMarks();
            SF.valueStore.Save(SF.ConfigPath);
            dgvValue.Refresh();
            RefreshCommonList();
        }

        private void btnCopy_Click(object sender, EventArgs e)
        {
            CopySelectedValueRow();
        }

        private void btnPaste_Click(object sender, EventArgs e)
        {
            PasteToSelectedValueRow();
        }

        private void btnClearData_Click(object sender, EventArgs e)
        {
            ClearSelectedValueRows(true);
        }

        private void dgvValue_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.ColumnIndex == 0 && e.RowIndex >= 0)
            {
                // 获取当前行对应的数据项
                if (SF.valueStore.IsMarked(e.RowIndex))
                {
                    e.CellStyle.BackColor = Color.FromArgb(253, 229, 229);
                    e.CellStyle.ForeColor = Color.FromArgb(170, 48, 48);
                }
                else
                {
                    e.CellStyle.BackColor = e.RowIndex % 2 == 0 ? Color.White : AlternateRowColor;
                    e.CellStyle.ForeColor = HeaderForeColor;
                }
            }

        }

        private void btnSearch_Click(object sender, EventArgs e)
        {
            txtVariableSearch.Focus();
            txtVariableSearch.SelectAll();
        }

        private void btnSystemVariables_Click(object sender, EventArgs e)
        {
            // 按钮已隐藏；系统变量通过左侧作用域树查看。
        }

        public void LocateValueIndex(int index)
        {
            if (index < 0 || index >= dgvValue.Rows.Count)
            {
                return;
            }
            if (SF.valueStore.TryGetValueByIndex(index, out DicValue value))
            {
                selectedScope = value.Scope;
                selectedOwnerProcId = value.OwnerProcId;
                RefreshScopeTree();
            }
            ApplyScopeFilter();
            dgvValue.ClearSelection();
            dgvValue.CurrentCell = dgvValue.Rows[index].Cells[0];
            dgvValue.FirstDisplayedScrollingRowIndex = index;
            dgvValue.Focus();
        }

        private void RefreshCommonList()
        {
            ApplyScopeFilter();
        }

        private static int CountVariableUsages(DicValue variable)
        {
            int count = 0;
            foreach (Proc proc in SF.frmProc?.procsList ?? new List<Proc>())
            {
                foreach (OperationType operation in (proc?.steps ?? new List<Step>())
                    .Where(step => step?.Ops != null).SelectMany(step => step.Ops))
                {
                    count += VariableReferenceCatalog.Enumerate(operation).Count(reference =>
                        reference.Kind == VariableReferenceKind.Name
                            ? string.Equals(reference.Value, variable.Name, StringComparison.Ordinal)
                            : int.TryParse(reference.Value, out int index) && index == variable.Index);
                }
            }
            return count;
        }

        private static bool TryCommitVariableRow(
            int index,
            string name,
            string type,
            string currentValue,
            string note,
            string scope,
            Guid? ownerProcId,
            bool setCurrentValue,
            out string error)
        {
            error = null;
            Dictionary<string, DicValue> draft = SF.valueStore.BuildSaveData();
            DicValue current = draft.Values.FirstOrDefault(value => value?.Index == index);
            if (draft.TryGetValue(name, out DicValue sameName) && sameName.Index != index)
            {
                error = $"变量名已存在：{name}";
                return false;
            }
            if (current != null) draft.Remove(current.Name);
            DicValue updated = current == null ? new DicValue() : ObjectGraphCloner.Clone(current);
            if (updated.Id == Guid.Empty) updated.Id = Guid.NewGuid();
            updated.Index = index;
            updated.Name = name;
            updated.Type = type;
            updated.Scope = ValueConfigStore.IsSystemValueIndex(index)
                ? VariableScopeContract.System
                : scope;
            updated.OwnerProcId = ValueConfigStore.IsSystemValueIndex(index) ? null : ownerProcId;
            if (current == null || setCurrentValue)
            {
                updated.Value = currentValue;
            }
            updated.Note = note;
            draft[name] = updated;
            return SF.valueStore.TryCommitConfiguration(
                SF.ConfigPath,
                draft,
                out error,
                setCurrentValue
                    ? new Dictionary<string, string>(StringComparer.Ordinal) { [name] = currentValue }
                    : null,
                setCurrentValue ? "变量页保存当前值" : null);
        }

        private void EnsureValueStoreHooked()
        {
            if (isValueStoreHooked)
            {
                return;
            }
            if (SF.valueStore == null)
            {
                return;
            }
            hookedValueStore = SF.valueStore;
            hookedValueStore.ValueChanged += ValueStore_ValueChanged;
            isValueStoreHooked = true;
        }

        private void EnsureMonitorForm()
        {
            if (monitorForm != null && !monitorForm.IsDisposed)
            {
                return;
            }
            monitorForm = new ValueMonitorForm(this);
            monitorForm.Owner = this;
        }

        private void ValueStore_ValueChanged(object sender, ValueChangedEventArgs e)
        {
            if (IsDisposed || Disposing || !IsHandleCreated)
            {
                return;
            }
            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(new Action<object, ValueChangedEventArgs>(ValueStore_ValueChanged), sender, e);
                }
                catch (InvalidOperationException)
                {
                }
                return;
            }
            if (e == null)
            {
                return;
            }
            if (!monitoredVariableIds.Contains(e.Id))
            {
                return;
            }
            if (monitorForm == null || monitorForm.IsDisposed)
            {
                return;
            }
            DataGridView grid = monitorForm.Grid;
            if (grid.Rows.Count >= MaxMonitorHistoryRows)
            {
                int removeCount = Math.Min(MonitorHistoryTrimRows, grid.Rows.Count);
                for (int i = 0; i < removeCount; i++)
                {
                    grid.Rows.RemoveAt(0);
                }
            }
            int rowIndex = grid.Rows.Add();
            DataGridViewRow row = grid.Rows[rowIndex];
            row.Tag = e.Id;
            row.Cells[0].Value = e.Index;
            row.Cells[1].Value = e.Name;
            row.Cells[2].Value = e.NewValue;
            row.Cells[3].Value = e.Source;
            row.Cells[4].Value = e.ChangedAt.ToString("yyyy-MM-dd HH:mm:ss");
        }

        private void RefreshMonitorTitle()
        {
            if (monitorForm == null || monitorForm.IsDisposed)
            {
                return;
            }
            monitorForm.TitleLabel.Text = $"变量监控({monitoredVariableIds.Count})";
        }

        private void RefreshMonitorRows()
        {
            if (monitorForm == null || monitorForm.IsDisposed)
            {
                return;
            }
            // 监控改为记录历史，不刷新既有记录
        }

        private void AddMonitor(int index)
        {
            if (index < 0 || index >= ValueConfigStore.ValueCapacity)
            {
                return;
            }
            EnsureMonitorForm();
            DataGridView grid = monitorForm.Grid;
            if (!SF.valueStore.TryGetValueByIndex(index, out DicValue value))
            {
                return;
            }
            if (monitoredVariableIds.Contains(value.Id)) return;
            SF.valueStore.SetMonitorFlag(index, true);
            monitoredVariableIds.Add(value.Id);
            int rowIndex = grid.Rows.Add();
            DataGridViewRow row = grid.Rows[rowIndex];
            row.Tag = value.Id;
            row.Cells[0].Value = index;
            row.Cells[1].Value = value?.Name;
            row.Cells[2].Value = value?.Value;
            row.Cells[3].Value = value?.LastChangedBy;
            row.Cells[4].Value = value != null && value.LastChangedAt != default
                ? value.LastChangedAt.ToString("yyyy-MM-dd HH:mm:ss")
                : string.Empty;
            RefreshMonitorTitle();
        }

        private void RemoveMonitor(int index)
        {
            if (!SF.valueStore.TryGetValueByIndex(index, out DicValue value)) return;
            RemoveMonitor(value.Id);
        }

        private void RemoveMonitor(Guid variableId)
        {
            if (!monitoredVariableIds.Remove(variableId)) return;
            SF.valueStore.SetMonitorFlag(variableId, false);
            RefreshMonitorTitle();
        }

        private void StopAllMonitor()
        {
            if (monitoredVariableIds.Count == 0)
            {
                RefreshMonitorTitle();
                SF.valueStore.SetMonitorEnabled(false);
                return;
            }
            List<Guid> variableIds = new List<Guid>(monitoredVariableIds);
            foreach (Guid variableId in variableIds)
            {
                SF.valueStore.SetMonitorFlag(variableId, false);
            }
            monitoredVariableIds.Clear();
            if (monitorForm != null && !monitorForm.IsDisposed)
            {
                monitorForm.Grid.Rows.Clear();
            }
            RefreshMonitorTitle();
            SF.valueStore.SetMonitorEnabled(false);
        }

        private void btnMonitor_Click(object sender, EventArgs e)
        {
            EnsureMonitorForm();
            SF.valueStore.SetMonitorEnabled(true);
            monitorForm.Show();
            monitorForm.BringToFront();
            RefreshMonitorTitle();
            RefreshMonitorRows();
        }

        private void btnMonitorAdd_Click(object sender, EventArgs e)
        {
            AddMonitorFromSelection();
            if (monitorForm != null && !monitorForm.IsDisposed)
            {
                SF.valueStore.SetMonitorEnabled(true);
                monitorForm.Show();
                monitorForm.BringToFront();
            }
        }

        private void btnMonitorRemove_Click(object sender, EventArgs e)
        {
            if (dgvValue.CurrentCell == null)
            {
                MessageBox.Show("没有选定的变量");
                return;
            }
            int rowIndex = dgvValue.CurrentCell.RowIndex;
            RemoveMonitor(rowIndex);
        }

        private void AddMonitorFromSelection()
        {
            if (dgvValue.CurrentCell == null)
            {
                MessageBox.Show("没有选定的变量");
                return;
            }
            int rowIndex = dgvValue.CurrentCell.RowIndex;
            if (!SF.valueStore.TryGetValueByIndex(rowIndex, out _))
            {
                MessageBox.Show("当前槽位尚未配置变量。", "加入监控",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            AddMonitor(rowIndex);
        }

        private void RemoveMonitorFromMonitorSelection()
        {
            if (monitorForm == null || monitorForm.IsDisposed)
            {
                MessageBox.Show("监控窗口未打开");
                return;
            }
            DataGridView grid = monitorForm.Grid;
            if (grid.CurrentRow == null)
            {
                MessageBox.Show("没有选定的监控项");
                return;
            }
            if (!(grid.CurrentRow.Tag is Guid variableId) || variableId == Guid.Empty)
            {
                MessageBox.Show("监控项稳定ID无效");
                return;
            }
            RemoveMonitor(variableId);
        }

        private void listCommon_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (listCommon.SelectedItem is CommonValueItem item)
            {
                int index = item.Index;
                if (index >= 0 && index < dgvValue.Rows.Count)
                {
                    currentIndex = index;
                    dgvValue.ClearSelection();
                    dgvValue.CurrentCell = dgvValue.Rows[index].Cells[0];
                    dgvValue.Rows[index].Selected = true;
                    dgvValue.FirstDisplayedScrollingRowIndex = index;
                }
            }
        }

    }
}
