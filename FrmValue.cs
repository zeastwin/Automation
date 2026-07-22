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
        private const int TvmSetExtendedStyle = 0x112C;
        private const int TvsExDoubleBuffer = 0x0004;

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

        private sealed class VariableGridViewState
        {
            public int FirstDisplayedSlotIndex { get; set; }
            public int CurrentSlotIndex { get; set; }
            public int CurrentColumnIndex { get; set; }
        }

        private enum MaterialButtonTone
        {
            Primary,
            Tonal,
            Danger
        }

        private sealed class BufferedTreeView : TreeView
        {
            public BufferedTreeView()
            {
                SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            }

            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                SendMessage(
                    Handle,
                    TvmSetExtendedStyle,
                    new IntPtr(TvsExDoubleBuffer),
                    new IntPtr(TvsExDoubleBuffer));
            }
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
                Color parentBackColor = Parent?.BackColor ?? UiPalette.Surface;
                pevent.Graphics.Clear(parentBackColor);
                pevent.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                pevent.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

                Color backColor;
                Color textColor;
                Color borderColor;
                if (!Enabled)
                {
                    backColor = UiPalette.DisabledSoft;
                    textColor = UiPalette.TextDisabled;
                    borderColor = UiPalette.Stroke;
                }
                else if (Tone == MaterialButtonTone.Primary)
                {
                    backColor = mouseDown ? UiPalette.BrandPressed : mouseOver ? UiPalette.BrandHover : UiPalette.Brand;
                    textColor = UiPalette.SurfaceStrong;
                    borderColor = backColor;
                }
                else if (Tone == MaterialButtonTone.Danger)
                {
                    backColor = mouseDown
                        ? UiPalette.DangerSoft
                        : mouseOver ? UiPalette.DangerSoft : UiPalette.DangerSoft;
                    textColor = UiPalette.Danger;
                    borderColor = UiPalette.DangerSoft;
                }
                else
                {
                    backColor = mouseDown
                        ? UiPalette.Stroke
                        : mouseOver ? UiPalette.SurfacePressed : UiPalette.SurfaceSubtle;
                    textColor = UiPalette.TextPrimary;
                    borderColor = UiPalette.Stroke;
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
                BackColor = UiPalette.SurfaceSubtle;
            }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                e.Graphics.Clear(Parent?.BackColor ?? UiPalette.Surface);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                RectangleF bounds = new RectangleF(0.75F, 0.75F, Math.Max(1, Width - 1.5F), Math.Max(1, Height - 1.5F));
                using (GraphicsPath path = CreateRoundedPath(bounds, 11F))
                using (var brush = new SolidBrush(BackColor))
                using (var pen = new Pen(UiPalette.Stroke, 1F))
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
                    BackColor = UiPalette.SurfaceSubtle
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
                    BackgroundColor = UiPalette.SurfaceSubtle,
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
                BackColor = UiPalette.SurfaceStrong;
                topPanel.BackColor = UiPalette.Background;
                labelTitle.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold);
                labelTitle.ForeColor = UiPalette.TextPrimary;
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
        private readonly Button btnClearSearch;
        private readonly TextBox txtVariableSearch;
        private readonly Label lblEmptyState;
        private readonly ToolTip uiToolTip;
        private readonly Timer variableSearchTimer;
        private readonly Timer runtimeValueRefreshTimer;
        private readonly Font scopeGroupFont;
        private readonly Font scopeChevronFont;
        private readonly DicValue[] variableSnapshot = new DicValue[ValueConfigStore.ValueCapacity];
        private readonly int[] variableRowLoadedVersions = new int[ValueConfigStore.ValueCapacity];
        private readonly int[] displayRowByVariableSlot = new int[ValueConfigStore.ValueCapacity];
        private List<int> displayedVariableSlots = new List<int>();
        private Dictionary<Guid, string> processNamesById = new Dictionary<Guid, string>();
        private List<VariableScopeOption> variableScopeOptions = new List<VariableScopeOption>();
        private readonly Dictionary<string, VariableGridViewState> variableGridViewStates =
            new Dictionary<string, VariableGridViewState>(StringComparer.Ordinal);
        private string selectedScope = AllVariableScopes;
        private Guid? selectedOwnerProcId;
        private TreeNode hoveredScopeNode;
        private bool isStructViewAttached;
        private bool isValueGridReady;
        private bool isValueEditValid = true;
        private bool isScopeFilterPending;
        private bool refreshScopeTreePending;
        private bool isScopeSelectionApplyPending;
        private bool suppressScopeSelectionChanged;
        private bool hasVariableSnapshot;
        private bool variableGridRowsInitialized;
        private bool isRebuildingVariableRows;
        private bool configurationRefreshPending;
        private int variableGridPresentationVersion = 1;
        private string appliedGridScopeKey;
        private int variableGridUpdateDepth;
        private bool variableGridRedrawSuspended;
        private const int MaxMonitorHistoryRows = 2000;
        private const int MonitorHistoryTrimRows = 200;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        [DllImport("user32.dll", EntryPoint = "SendMessageW")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        public FrmValue()
        {
            InitializeComponent();
            for (int i = 0; i < displayRowByVariableSlot.Length; i++)
            {
                displayRowByVariableSlot[i] = -1;
            }
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
            dgvValue.Columns.Add(scopeColumn);

            listCommon.Visible = false;
            scopeTree = new BufferedTreeView
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
                BackColor = UiPalette.SurfaceSubtle
            };
            scopeTree.AfterSelect += ScopeTree_AfterSelect;
            scopeTree.DrawNode += ScopeTree_DrawNode;
            scopeTree.NodeMouseClick += ScopeTree_NodeMouseClick;
            scopeTree.MouseMove += ScopeTree_MouseMove;
            scopeTree.MouseLeave += ScopeTree_MouseLeave;
            scopeGroupFont = new Font(scopeTree.Font, FontStyle.Bold);
            scopeChevronFont = new Font("Microsoft YaHei UI", 11F);

            btnNormalVariables.Text = "新增变量";
            btnSystemVariables.Visible = false;
            btnClearSearch = new MaterialButton { Text = "×" };
            txtVariableSearch = new TextBox();
            lblEmptyState = new Label();
            uiToolTip = new ToolTip
            {
                AutoPopDelay = 6000,
                InitialDelay = 350,
                ReshowDelay = 100
            };
            variableSearchTimer = new Timer { Interval = 160 };
            variableSearchTimer.Tick += (sender, args) =>
            {
                variableSearchTimer.Stop();
                ApplyScopeFilter();
            };
            runtimeValueRefreshTimer = new Timer { Interval = 150 };
            runtimeValueRefreshTimer.Tick += (sender, args) => RefreshDisplayedRuntimeValues();
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
            type.SortMode = DataGridViewColumnSortMode.NotSortable;
            dgvValue.RowHeadersVisible = false;
            dgvValue.AutoGenerateColumns = false;
            dgvValue.Scroll += (sender, args) =>
            {
                if (variableGridUpdateDepth == 0) RefreshViewportRows();
            };
            dgvValue.Resize += (sender, args) =>
            {
                if (variableGridUpdateDepth == 0) RefreshViewportRows();
            };
            DoubleBuffered = true;

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
                BackColor = UiPalette.Surface,
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
                BackColor = UiPalette.Surface
            };
            ConfigureToolbarButton(btnSearch, "搜索", 72);
            btnSearch.Dock = DockStyle.Right;
            btnSearch.Margin = Padding.Empty;

            var searchField = new MaterialInputHost
            {
                Dock = DockStyle.Fill,
                Height = 40,
                Padding = Padding.Empty,
                BackColor = UiPalette.SurfaceSubtle
            };
            txtVariableSearch.Dock = DockStyle.None;
            txtVariableSearch.AutoSize = false;
            txtVariableSearch.BorderStyle = BorderStyle.None;
            txtVariableSearch.BackColor = UiPalette.SurfaceSubtle;
            txtVariableSearch.ForeColor = UiPalette.TextPrimary;
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
            searchPanel.Controls.Add(new Panel { Dock = DockStyle.Right, Width = 8, BackColor = UiPalette.Surface });
            searchPanel.Controls.Add(btnSearch);
            panel1.Controls.Add(actions);
            panel1.Controls.Add(searchPanel);

            panelCommon.Controls.Clear();
            panelCommon.Width = 272;
            panelCommon.Padding = new Padding(8, 0, 8, 8);
            panelCommon.BackColor = UiPalette.SurfaceSubtle;

            panelCommon.Controls.Add(scopeTree);

            var gridHost = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12, 10, 12, 12),
                BackColor = UiPalette.Surface
            };
            var gridCard = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiPalette.SurfaceStrong,
                BorderStyle = BorderStyle.None
            };
            dgvValue.Dock = DockStyle.Fill;
            dgvValue.BorderStyle = BorderStyle.None;
            lblEmptyState.Dock = DockStyle.Fill;
            lblEmptyState.BackColor = UiPalette.SurfaceStrong;
            lblEmptyState.ForeColor = UiPalette.TextSecondary;
            lblEmptyState.Font = new Font("Microsoft YaHei UI", 10F);
            lblEmptyState.TextAlign = ContentAlignment.MiddleCenter;
            lblEmptyState.Visible = false;
            var gridBody = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiPalette.SurfaceStrong
            };
            gridBody.Controls.Add(dgvValue);
            gridBody.Controls.Add(lblEmptyState);
            gridCard.Controls.Add(gridBody);
            gridHost.Controls.Add(gridCard);
            splitContainerMain.Panel1.Controls.Clear();
            splitContainerMain.Panel1.Controls.Add(gridHost);
            splitContainerMain.Panel1.Controls.Add(panelCommon);

            panelStructHost.Dock = DockStyle.Fill;
            panelStructHost.Padding = new Padding(8);
            panelStructHost.BackColor = UiPalette.Surface;
            splitContainerMain.Panel2.Controls.Clear();
            splitContainerMain.Panel2.Controls.Add(panelStructHost);
            splitContainerMain.SplitterWidth = 1;
            splitContainerMain.BorderStyle = BorderStyle.None;
            Text = "变量管理";

            index.HeaderText = "槽位";
            index.Width = 72;
            name.Width = 190;
            type.Width = 96;
            value.Width = 160;
            scopeColumn.Width = 170;
            scopeColumn.DisplayIndex = 4;
            Note.DisplayIndex = 5;
            Note.MinimumWidth = 160;
            Note.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;

            uiToolTip.SetToolTip(txtVariableSearch, "跨作用域搜索名称、槽位、类型、备注或所属流程");
            uiToolTip.SetToolTip(btnMonitorAdd, "把所选变量加入运行值监控");

            EnableDoubleBuffer(panel1);
            EnableDoubleBuffer(splitContainerMain);
            EnableDoubleBuffer(splitContainerMain.Panel1);
            EnableDoubleBuffer(splitContainerMain.Panel2);
            EnableDoubleBuffer(panelCommon);
            EnableDoubleBuffer(scopeTree);
            EnableDoubleBuffer(gridHost);
            EnableDoubleBuffer(gridCard);
            EnableDoubleBuffer(gridBody);

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
            BackColor = UiPalette.Surface;
            panel1.BackColor = UiPalette.Surface;
            panelCommon.BackColor = UiPalette.SurfaceSubtle;
            panelStructHost.BackColor = UiPalette.Surface;
            splitContainerMain.BackColor = UiPalette.Stroke;
            splitContainerMain.BorderStyle = BorderStyle.None;
            listCommon.BackColor = UiPalette.SurfaceStrong;
            listCommon.ForeColor = UiPalette.TextPrimary;
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
            EnableDoubleBuffer(grid);
            grid.Font = new Font("Microsoft YaHei UI", 10F);
            grid.BackgroundColor = UiPalette.SurfaceStrong;
            grid.BorderStyle = BorderStyle.None;
            grid.GridColor = UiPalette.Stroke;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersHeight = 42;
            grid.ColumnHeadersDefaultCellStyle.BackColor = UiPalette.Background;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = UiPalette.TextPrimary;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = UiPalette.Background;
            grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = UiPalette.TextPrimary;
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
            grid.RowTemplate.Height = 38;
            grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            grid.DefaultCellStyle.BackColor = UiPalette.SurfaceStrong;
            grid.DefaultCellStyle.ForeColor = UiPalette.TextPrimary;
            grid.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.DefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
            grid.AlternatingRowsDefaultCellStyle.BackColor = UiPalette.Input;
            grid.DefaultCellStyle.SelectionBackColor = UiPalette.Selection;
            grid.DefaultCellStyle.SelectionForeColor = UiPalette.SelectionText;
            if (grid.Columns.Contains("Note"))
            {
                grid.Columns["Note"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            }
        }

        private static void EnableDoubleBuffer(Control control)
        {
            PropertyInfo property = typeof(Control).GetProperty(
                "DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
            property?.SetValue(control, true, null);
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
                button.BackColor = UiPalette.Brand;
                button.ForeColor = UiPalette.TextInverse;
                button.FlatAppearance.BorderColor = UiPalette.Brand;
                button.FlatAppearance.MouseOverBackColor = UiPalette.BrandHover;
                button.FlatAppearance.MouseDownBackColor = UiPalette.BrandPressed;
            }
            else if (danger)
            {
                button.BackColor = UiPalette.DangerSoft;
                button.ForeColor = UiPalette.Danger;
                button.FlatAppearance.BorderColor = UiPalette.DangerSoft;
                button.FlatAppearance.MouseOverBackColor = UiPalette.DangerSoft;
                button.FlatAppearance.MouseDownBackColor = UiPalette.DangerSoft;
            }
            else
            {
                button.BackColor = UiPalette.SurfaceSubtle;
                button.ForeColor = UiPalette.TextPrimary;
                button.FlatAppearance.BorderColor = UiPalette.Stroke;
                button.FlatAppearance.MouseOverBackColor = UiPalette.SurfacePressed;
                button.FlatAppearance.MouseDownBackColor = UiPalette.Stroke;
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
            using (var background = new SolidBrush(selected ? UiPalette.Selection
                : e.Index % 2 == 0 ? UiPalette.SurfaceStrong : UiPalette.Input))
            using (var foreground = new SolidBrush(selected ? UiPalette.SelectionText : UiPalette.TextPrimary))
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
            runtimeValueRefreshTimer?.Dispose();
            variableSearchTimer?.Dispose();
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
            scopeGroupFont?.Dispose();
            scopeChevronFont?.Dispose();
            uiToolTip?.Dispose();
        }
        //从文件更新变量表
        public void RefreshDic()
        {
            Workspace.Runtime.Stores.Values.Load(Workspace.Runtime.Paths.ConfigPath, Workspace.Proc?.procsList);
            RefreshFromStore();
        }

        internal void RefreshFromStore()
        {
            EnsureValueStoreHooked();

            RefreshValue();

            isValueGridReady = true;
        }
  
        public void RefreshValue()
        {
            bool allowGridEvents = isValueGridReady;
            isValueGridReady = false;
            BeginVariableGridUpdate();
            try
            {
                RefreshVariableSnapshot();
                RefreshVariableScopeOptions();
                EnsureVariableGridRowsInitialized();
                RefreshScopeTree();
                ApplyScopeFilter();
                RefreshMonitorTitle();
                RefreshMonitorRows();
            }
            finally
            {
                EndVariableGridUpdate();
                isValueGridReady = allowGridEvents;
            }
        }

        private void EnsureVariableGridRowsInitialized()
        {
            if (variableGridRowsInitialized
                && dgvValue.Rows.Count == ValueConfigStore.ValueCapacity)
            {
                return;
            }

            isRebuildingVariableRows = true;
            try
            {
                dgvValue.Rows.Clear();
                displayedVariableSlots.Clear();
                for (int slotIndex = 0; slotIndex < ValueConfigStore.ValueCapacity; slotIndex++)
                {
                    displayedVariableSlots.Add(slotIndex);
                    displayRowByVariableSlot[slotIndex] = slotIndex;
                }
                dgvValue.Rows.Add(ValueConfigStore.ValueCapacity);
                variableGridRowsInitialized = true;
                AdvanceVariableGridPresentationVersion();
            }
            finally
            {
                isRebuildingVariableRows = false;
            }
        }
        //刷新变量界面
        public void FreshFrmValue()
        {
            if (!Visible && isValueGridReady)
            {
                configurationRefreshPending = true;
                return;
            }
            configurationRefreshPending = false;
            bool allowRefresh = isValueGridReady;
            isValueGridReady = false;
            BeginVariableGridUpdate();
            try
            {
                if (!variableGridRowsInitialized)
                {
                    RefreshValue();
                    return;
                }
                RefreshVariableSnapshot();
                RefreshVariableScopeOptions();
                RefreshScopeTree();
                ApplyScopeFilter();
                RefreshMonitorTitle();
                RefreshMonitorRows();
            }
            finally
            {
                EndVariableGridUpdate();
                isValueGridReady = allowRefresh;
            }
        }

        private void RefreshVariableSnapshot()
        {
            Array.Clear(variableSnapshot, 0, variableSnapshot.Length);
            foreach (DicValue variable in Workspace.Runtime.Stores.Values?.GetValuesSnapshot() ?? new List<DicValue>())
            {
                if (variable != null && variable.Index >= 0 && variable.Index < variableSnapshot.Length)
                {
                    variableSnapshot[variable.Index] = variable;
                }
            }
            hasVariableSnapshot = true;
            AdvanceVariableGridPresentationVersion();
        }

        private void UpdateVariableGridRow(int rowIndex, int slotIndex, DicValue variable)
        {
            DataGridViewRow row = dgvValue.Rows[rowIndex];
            SetCellValueIfChanged(row.Cells[0], slotIndex);
            SetCellValueIfChanged(row.Cells[1], variable?.Name ?? string.Empty);
            SetCellValueIfChanged(row.Cells[2], variable?.Type ?? DefaultValueType);
            SetCellValueIfChanged(row.Cells[3], variable?.Value ?? DefaultValueText);
            SetCellValueIfChanged(row.Cells[4], variable?.Note ?? string.Empty);
            ConfigureVariableScopeCell(row, slotIndex, variable);
            ApplyVariableRowEditingState(row, variable);
            variableRowLoadedVersions[slotIndex] = variableGridPresentationVersion;
        }

        private void RefreshViewportRows()
        {
            if (!hasVariableSnapshot || dgvValue.Rows.Count == 0)
            {
                return;
            }
            var rowIndexes = new List<int>();
            int firstDisplayed = dgvValue.Rows.GetFirstRow(DataGridViewElementStates.Displayed);
            if (firstDisplayed < 0)
            {
                return;
            }
            int rowIndex = firstDisplayed;
            while (rowIndex >= 0)
            {
                rowIndexes.Add(rowIndex);
                rowIndex = dgvValue.Rows.GetNextRow(rowIndex, DataGridViewElementStates.Displayed);
            }

            int nearbyIndex = firstDisplayed;
            for (int count = 0; count < 8; count++)
            {
                nearbyIndex = dgvValue.Rows.GetPreviousRow(nearbyIndex, DataGridViewElementStates.Visible);
                if (nearbyIndex < 0) break;
                rowIndexes.Add(nearbyIndex);
            }
            nearbyIndex = rowIndexes[0];
            int lastDisplayed = dgvValue.Rows.GetLastRow(DataGridViewElementStates.Displayed);
            if (lastDisplayed >= 0)
            {
                nearbyIndex = lastDisplayed;
            }
            for (int count = 0; count < 12; count++)
            {
                nearbyIndex = dgvValue.Rows.GetNextRow(nearbyIndex, DataGridViewElementStates.Visible);
                if (nearbyIndex < 0) break;
                rowIndexes.Add(nearbyIndex);
            }

            int editingRowIndex = dgvValue.IsCurrentCellInEditMode && dgvValue.CurrentCell != null
                ? dgvValue.CurrentCell.RowIndex
                : -1;
            bool allowGridEvents = isValueGridReady;
            isValueGridReady = false;
            BeginVariableGridUpdate();
            try
            {
                foreach (int index in rowIndexes)
                {
                    if (index != editingRowIndex)
                    {
                        int slotIndex = GetVariableSlotIndex(index);
                        if (slotIndex < 0) continue;
                        if (variableSnapshot[slotIndex] != null
                            && Workspace.Runtime.Stores.Values.TryGetValueByIndex(slotIndex, out DicValue liveVariable))
                        {
                            variableSnapshot[slotIndex].Value = liveVariable.Value;
                        }
                        if (variableRowLoadedVersions[slotIndex] != variableGridPresentationVersion)
                        {
                            UpdateVariableGridRow(index, slotIndex, variableSnapshot[slotIndex]);
                        }
                        else
                        {
                            SetCellValueIfChanged(
                                dgvValue.Rows[index].Cells[3],
                                variableSnapshot[slotIndex]?.Value ?? DefaultValueText);
                        }
                    }
                }
            }
            finally
            {
                EndVariableGridUpdate();
                isValueGridReady = allowGridEvents;
            }
        }

        private int GetVariableSlotIndex(int displayRowIndex)
        {
            return displayRowIndex >= 0 && displayRowIndex < displayedVariableSlots.Count
                ? displayedVariableSlots[displayRowIndex]
                : -1;
        }

        private int GetVariableDisplayRowIndex(int slotIndex)
        {
            return slotIndex >= 0 && slotIndex < displayRowByVariableSlot.Length
                ? displayRowByVariableSlot[slotIndex]
                : -1;
        }

        public int GetSelectedVariableSlotIndex()
        {
            return GetVariableSlotIndex(dgvValue.CurrentCell?.RowIndex ?? -1);
        }

        private static void SetCellValueIfChanged(DataGridViewCell cell, object value)
        {
            if (!Equals(cell.Value, value))
            {
                cell.Value = value;
            }
        }

        private void AdvanceVariableGridPresentationVersion()
        {
            if (variableGridPresentationVersion == int.MaxValue)
            {
                Array.Clear(variableRowLoadedVersions, 0, variableRowLoadedVersions.Length);
                variableGridPresentationVersion = 1;
                return;
            }
            variableGridPresentationVersion++;
        }

        private void BeginVariableGridUpdate()
        {
            if (variableGridUpdateDepth++ != 0)
            {
                return;
            }
            dgvValue.SuspendLayout();
            variableGridRedrawSuspended = dgvValue.IsHandleCreated;
            if (variableGridRedrawSuspended)
            {
                SendMessage(dgvValue.Handle, WmSetRedraw, IntPtr.Zero, IntPtr.Zero);
            }
        }

        private void EndVariableGridUpdate()
        {
            if (variableGridUpdateDepth <= 0)
            {
                return;
            }
            if (variableGridUpdateDepth > 1)
            {
                variableGridUpdateDepth--;
                return;
            }
            try
            {
                dgvValue.ResumeLayout();
                if (variableGridRedrawSuspended && dgvValue.IsHandleCreated)
                {
                    SendMessage(dgvValue.Handle, WmSetRedraw, new IntPtr(1), IntPtr.Zero);
                }
                variableGridRedrawSuspended = false;
                dgvValue.Invalidate();
            }
            finally
            {
                variableGridUpdateDepth = 0;
            }
        }

        private void RefreshScopeTree()
        {
            if (scopeTree == null) return;
            if (!hasVariableSnapshot)
            {
                RefreshVariableSnapshot();
                RefreshVariableScopeOptions();
            }
            scopeTree.BeginUpdate();
            try
            {
                scopeTree.Nodes.Clear();
                List<DicValue> variables = variableSnapshot.Where(value => value != null).ToList();
                int publicCount = 0;
                int systemCount = 0;
                int processCount = 0;
                var processVariableCounts = new Dictionary<Guid, int>();
                foreach (DicValue variable in variables)
                {
                    if (string.Equals(variable.Scope, VariableScopeContract.Public, StringComparison.Ordinal))
                    {
                        publicCount++;
                    }
                    else if (string.Equals(variable.Scope, VariableScopeContract.System, StringComparison.Ordinal))
                    {
                        systemCount++;
                    }
                    else if (string.Equals(variable.Scope, VariableScopeContract.Process, StringComparison.Ordinal))
                    {
                        processCount++;
                        if (variable.OwnerProcId.HasValue)
                        {
                            processVariableCounts.TryGetValue(variable.OwnerProcId.Value, out int ownedCount);
                            processVariableCounts[variable.OwnerProcId.Value] = ownedCount + 1;
                        }
                    }
                }

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
                IEnumerable<Proc> processes = (Workspace.Proc?.procsList ?? new List<Proc>())
                    .Where(proc => proc?.head != null && proc.head.Id != Guid.Empty)
                    .OrderBy(proc => proc.head.Name ?? string.Empty, StringComparer.CurrentCultureIgnoreCase);
                foreach (Proc proc in processes)
                {
                    processVariableCounts.TryGetValue(proc.head.Id, out int ownedCount);
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
                    processRoot.Nodes.Add(new TreeNode("暂无流程"));
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
                suppressScopeSelectionChanged = true;
                try
                {
                    scopeTree.SelectedNode = selectedNode;
                    selectedNode.EnsureVisible();
                }
                finally
                {
                    suppressScopeSelectionChanged = false;
                }
            }
            finally
            {
                scopeTree.EndUpdate();
            }
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
            bool hovered = selectable && e.Node == hoveredScopeNode;

            using (var background = new SolidBrush(scopeTree.BackColor))
            {
                e.Graphics.FillRectangle(background, new Rectangle(0, e.Bounds.Top, scopeTree.ClientSize.Width, scopeTree.ItemHeight));
            }
            if ((selected || hovered) && rowBounds.Width > 0 && rowBounds.Height > 0)
            {
                using (GraphicsPath path = CreateRoundedPath(rowBounds, 10))
                using (var selectionBrush = new SolidBrush(
                    selected ? UiPalette.Selection : UiPalette.SurfacePressed))
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
            Font font = groupNode ? scopeGroupFont : scopeTree.Font;
            Color textColor = placeholderNode
                ? UiPalette.TextSecondary
                : selected ? UiPalette.SelectionText : UiPalette.TextPrimary;
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
                TextRenderer.DrawText(
                    e.Graphics,
                    e.Node.IsExpanded ? "⌄" : "›",
                    scopeChevronFont,
                    chevronBounds,
                    UiPalette.TextSecondary,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
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
            if (suppressScopeSelectionChanged) return;
            if (!(e.Node?.Tag is VariableScopeSelection selection)) return;

            if (!isScopeSelectionApplyPending)
            {
                SaveVariableGridViewState();
            }
            selectedScope = selection.Scope;
            selectedOwnerProcId = selection.OwnerProcId;

            // AfterSelect运行在鼠标消息内部。先完成树节点绘制，再把表格筛选放到下一条UI消息，
            // 避免1200行显隐处理阻塞左侧选中反馈。
            scopeTree.Invalidate();
            scopeTree.Update();
            ScheduleScopeSelectionApply();
        }

        private void ScopeTree_MouseMove(object sender, MouseEventArgs e)
        {
            TreeNode node = scopeTree.GetNodeAt(e.Location);
            TreeNode hoverTarget = node?.Tag is VariableScopeSelection ? node : null;
            if (hoveredScopeNode == hoverTarget)
            {
                return;
            }
            TreeNode previous = hoveredScopeNode;
            hoveredScopeNode = hoverTarget;
            scopeTree.Cursor = hoveredScopeNode == null ? Cursors.Default : Cursors.Hand;
            InvalidateScopeNode(previous);
            InvalidateScopeNode(hoveredScopeNode);
        }

        private void ScopeTree_MouseLeave(object sender, EventArgs e)
        {
            if (hoveredScopeNode == null)
            {
                return;
            }
            TreeNode previous = hoveredScopeNode;
            hoveredScopeNode = null;
            scopeTree.Cursor = Cursors.Default;
            InvalidateScopeNode(previous);
        }

        private void InvalidateScopeNode(TreeNode node)
        {
            if (node == null)
            {
                return;
            }
            scopeTree.Invalidate(new Rectangle(
                0,
                node.Bounds.Top,
                scopeTree.ClientSize.Width,
                scopeTree.ItemHeight));
        }

        private void ScheduleScopeSelectionApply()
        {
            if (isScopeSelectionApplyPending || IsDisposed || Disposing || !IsHandleCreated)
            {
                return;
            }
            isScopeSelectionApplyPending = true;
            try
            {
                BeginInvoke((Action)(() =>
                {
                    isScopeSelectionApplyPending = false;
                    if (IsDisposed || Disposing || !Visible)
                    {
                        return;
                    }
                    BeginVariableGridUpdate();
                    try
                    {
                        ApplyScopeFilter();
                        RestoreVariableGridViewState();
                        RefreshDisplayedRuntimeValues();
                    }
                    finally
                    {
                        EndVariableGridUpdate();
                    }
                }));
            }
            catch (InvalidOperationException)
            {
                isScopeSelectionApplyPending = false;
            }
        }

        private void SaveVariableGridViewState()
        {
            if (dgvValue == null || dgvValue.Rows.Count == 0)
            {
                return;
            }
            int firstDisplayedRowIndex = -1;
            try
            {
                firstDisplayedRowIndex = dgvValue.FirstDisplayedScrollingRowIndex;
            }
            catch (InvalidOperationException)
            {
            }
            variableGridViewStates[BuildVariableGridViewStateKey(selectedScope, selectedOwnerProcId)] =
                new VariableGridViewState
                {
                    FirstDisplayedSlotIndex = GetVariableSlotIndex(firstDisplayedRowIndex),
                    CurrentSlotIndex = GetSelectedVariableSlotIndex(),
                    CurrentColumnIndex = dgvValue.CurrentCell?.ColumnIndex ?? -1
                };
        }

        private void RestoreVariableGridViewState()
        {
            if (!variableGridViewStates.TryGetValue(
                BuildVariableGridViewStateKey(selectedScope, selectedOwnerProcId),
                out VariableGridViewState state)
                || dgvValue.Rows.Count == 0)
            {
                return;
            }
            int currentRowIndex = GetVariableDisplayRowIndex(state.CurrentSlotIndex);
            if (currentRowIndex >= 0 && dgvValue.Rows[currentRowIndex].Visible)
            {
                int columnIndex = state.CurrentColumnIndex >= 0
                    && state.CurrentColumnIndex < dgvValue.Columns.Count
                    ? state.CurrentColumnIndex
                    : 0;
                dgvValue.CurrentCell = dgvValue.Rows[currentRowIndex].Cells[columnIndex];
                dgvValue.Rows[currentRowIndex].Selected = true;
            }
            int firstDisplayedRowIndex = GetVariableDisplayRowIndex(state.FirstDisplayedSlotIndex);
            if (firstDisplayedRowIndex >= 0 && dgvValue.Rows[firstDisplayedRowIndex].Visible)
            {
                try
                {
                    dgvValue.FirstDisplayedScrollingRowIndex = firstDisplayedRowIndex;
                }
                catch (InvalidOperationException)
                {
                }
            }
            RefreshViewportRows();
        }

        private static string BuildVariableGridViewStateKey(string scope, Guid? ownerProcId)
        {
            return $"{scope}:{ownerProcId?.ToString("D") ?? string.Empty}";
        }

        private void txtVariableSearch_TextChanged(object sender, EventArgs e)
        {
            variableSearchTimer.Stop();
            if (string.IsNullOrWhiteSpace(txtVariableSearch.Text))
            {
                ApplyScopeFilter();
                return;
            }
            variableSearchTimer.Start();
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
            EnsureVariableGridRowsInitialized();
            string searchText = txtVariableSearch?.Text?.Trim() ?? string.Empty;
            bool searching = searchText.Length > 0;
            bool allScopesSelected = string.Equals(selectedScope, AllVariableScopes, StringComparison.Ordinal);
            var targetVisibility = new bool[ValueConfigStore.ValueCapacity];
            int visibleCount = 0;
            for (int slotIndex = 0; slotIndex < ValueConfigStore.ValueCapacity; slotIndex++)
            {
                bool visible = ShouldDisplayVariableRow(
                    slotIndex, searching, searchText, allScopesSelected);
                targetVisibility[slotIndex] = visible;
                if (visible) visibleCount++;
            }
            string gridScopeKey = BuildVariableGridViewStateKey(selectedScope, selectedOwnerProcId);
            if (!string.Equals(appliedGridScopeKey, gridScopeKey, StringComparison.Ordinal))
            {
                appliedGridScopeKey = gridScopeKey;
                AdvanceVariableGridPresentationVersion();
            }
            BeginVariableGridUpdate();
            try
            {
                int currentRowIndex = dgvValue.CurrentCell?.RowIndex ?? -1;
                if (currentRowIndex >= 0 && !targetVisibility[currentRowIndex])
                {
                    dgvValue.CurrentCell = null;
                }
                for (int rowIndex = 0; rowIndex < ValueConfigStore.ValueCapacity; rowIndex++)
                {
                    DataGridViewRow row = dgvValue.Rows[rowIndex];
                    bool targetVisible = targetVisibility[rowIndex];
                    if (row.Visible != targetVisible)
                    {
                        row.Visible = targetVisible;
                    }
                }
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
                RefreshViewportRows();
            }
            finally
            {
                EndVariableGridUpdate();
            }
        }

        private bool ShouldDisplayVariableRow(
            int index, bool searching, string searchText, bool allScopesSelected)
        {
            DicValue variable = variableSnapshot[index];
            if (variable == null)
            {
                return !searching
                    && (allScopesSelected
                        || string.Equals(selectedScope, VariableScopeContract.System, StringComparison.Ordinal)
                        && ValueConfigStore.IsSystemValueIndex(index));
            }
            if (searching)
            {
                return IsVariableSearchMatch(variable, searchText);
            }
            return allScopesSelected
                || string.Equals(selectedScope, VariableScopeContract.System, StringComparison.Ordinal)
                && ValueConfigStore.IsSystemValueIndex(index)
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
                            ApplyScopeFilter();
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
            List<Proc> processes = (Workspace.Proc?.procsList ?? new List<Proc>())
                .Where(proc => proc?.head != null && proc.head.Id != Guid.Empty)
                .OrderBy(proc => proc.head.Name ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            var names = new Dictionary<Guid, string>();
            foreach (Proc proc in processes)
            {
                names[proc.head.Id] = proc.head.Name ?? proc.head.Id.ToString("D");
            }
            processNamesById = names;

            var options = new List<VariableScopeOption>
            {
                new VariableScopeOption
                {
                    Key = VariableScopeContract.Public,
                    Text = "公共变量",
                    Scope = VariableScopeContract.Public
                }
            };
            options.AddRange(processes.Select(proc => new VariableScopeOption
                {
                    Key = BuildVariableScopeOptionKey(VariableScopeContract.Process, proc.head.Id),
                    Text = $"流程私有 · {proc.head.Name}",
                    Scope = VariableScopeContract.Process,
                    OwnerProcId = proc.head.Id
                }));
            bool optionsChanged = options.Count != variableScopeOptions.Count;
            for (int i = 0; !optionsChanged && i < options.Count; i++)
            {
                VariableScopeOption current = variableScopeOptions[i];
                VariableScopeOption updated = options[i];
                optionsChanged = !string.Equals(current.Key, updated.Key, StringComparison.Ordinal)
                    || !string.Equals(current.Text, updated.Text, StringComparison.Ordinal)
                    || !string.Equals(current.Scope, updated.Scope, StringComparison.Ordinal)
                    || current.OwnerProcId != updated.OwnerProcId;
            }
            if (optionsChanged)
            {
                variableScopeOptions = options;
                AdvanceVariableGridPresentationVersion();
            }
        }

        private void ConfigureVariableScopeCell(DataGridViewRow row, int slotIndex, DicValue variable)
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
                        comboCell.DisplayMember = nameof(VariableScopeOption.Text);
                        comboCell.ValueMember = nameof(VariableScopeOption.Key);
                        comboCell.DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton;
                        comboCell.DisplayStyleForCurrentCellOnly = true;
                        comboCell.FlatStyle = FlatStyle.Flat;
                    }
                    if (!ReferenceEquals(comboCell.DataSource, variableScopeOptions))
                    {
                        comboCell.DataSource = variableScopeOptions;
                    }
                    SetCellValueIfChanged(comboCell, optionKey);
                    bool readOnly = !string.Equals(selectedScope, AllVariableScopes, StringComparison.Ordinal);
                    if (comboCell.ReadOnly != readOnly) comboCell.ReadOnly = readOnly;
                    return;
                }
            }

            var textCell = row.Cells[5] as DataGridViewTextBoxCell;
            if (textCell == null)
            {
                textCell = new DataGridViewTextBoxCell();
                row.Cells[5] = textCell;
            }
            string scopeText = variable == null
                ? ValueConfigStore.IsSystemValueIndex(slotIndex) ? "系统" : "未配置"
                : FormatVariableScope(variable);
            SetCellValueIfChanged(textCell, scopeText);
            if (!textCell.ReadOnly) textCell.ReadOnly = true;
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
            if (row.Cells[1].ReadOnly != protectedSystemVariable)
            {
                row.Cells[1].ReadOnly = protectedSystemVariable;
            }
            if (row.Cells[2].ReadOnly != protectedSystemVariable)
            {
                row.Cells[2].ReadOnly = protectedSystemVariable;
            }
        }

        private bool IsVariableSearchMatch(DicValue value, string searchText)
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

        private string FormatVariableScope(DicValue value)
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

        private string ResolveOwnerProcessName(Guid? ownerProcId)
        {
            if (!ownerProcId.HasValue) return string.Empty;
            return processNamesById.TryGetValue(ownerProcId.Value, out string processName)
                ? processName
                : string.Empty;
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
            if (!Visible)
            {
                runtimeValueRefreshTimer?.Stop();
                return;
            }
            if (configurationRefreshPending
                || !hasVariableSnapshot
                || !variableGridRowsInitialized)
            {
                FreshFrmValue();
            }
            else
            {
                RefreshViewportRows();
                RefreshDisplayedRuntimeValues();
            }
            runtimeValueRefreshTimer?.Start();
            if (IsHandleCreated)
            {
                BeginInvoke((Action)(() =>
                {
                    if (!IsDisposed && !Disposing && Visible)
                    {
                        RefreshViewportRows();
                    }
                }));
            }
        }

        private void RefreshDisplayedRuntimeValues()
        {
            if (!Visible || !isValueGridReady || Workspace.Runtime.Stores.Values == null || dgvValue.Rows.Count == 0)
            {
                return;
            }
            int editingRowIndex = dgvValue.IsCurrentCellInEditMode && dgvValue.CurrentCell != null
                ? dgvValue.CurrentCell.RowIndex
                : -1;
            var updates = new List<KeyValuePair<int, string>>();
            int rowIndex = dgvValue.Rows.GetFirstRow(DataGridViewElementStates.Displayed);
            while (rowIndex >= 0)
            {
                int slotIndex = GetVariableSlotIndex(rowIndex);
                if (rowIndex != editingRowIndex
                    && slotIndex >= 0
                    && Workspace.Runtime.Stores.Values.TryGetValueByIndex(slotIndex, out DicValue variable))
                {
                    string currentValue = variable.Value ?? string.Empty;
                    DataGridViewCell valueCell = dgvValue.Rows[rowIndex].Cells[3];
                    if (!string.Equals(Convert.ToString(valueCell.Value), currentValue, StringComparison.Ordinal))
                    {
                        if (variableSnapshot[slotIndex] != null)
                        {
                            variableSnapshot[slotIndex].Value = currentValue;
                        }
                        updates.Add(new KeyValuePair<int, string>(rowIndex, currentValue));
                    }
                }
                rowIndex = dgvValue.Rows.GetNextRow(rowIndex, DataGridViewElementStates.Displayed);
            }
            if (updates.Count == 0)
            {
                return;
            }

            bool allowGridEvents = isValueGridReady;
            isValueGridReady = false;
            BeginVariableGridUpdate();
            try
            {
                foreach (KeyValuePair<int, string> update in updates)
                {
                    SetCellValueIfChanged(dgvValue.Rows[update.Key].Cells[3], update.Value);
                }
            }
            finally
            {
                EndVariableGridUpdate();
                isValueGridReady = allowGridEvents;
            }
        }

        private void AttachDataStructView()
        {
            if (isStructViewAttached || panelStructHost == null)
            {
                return;
            }
            if (Workspace.DataStruct == null || Workspace.DataStruct.IsDisposed)
            {
                return;
            }

            Workspace.DataStruct.TopLevel = false;
            Workspace.DataStruct.FormBorderStyle = FormBorderStyle.None;
            Workspace.DataStruct.Dock = DockStyle.Fill;

            if (!panelStructHost.Controls.Contains(Workspace.DataStruct))
            {
                panelStructHost.Controls.Add(Workspace.DataStruct);
            }

            Workspace.DataStruct.Show();
            Workspace.DataStruct.BringToFront();
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
            if (Workspace.Runtime.Editor.TryHandleHistoryShortcut(dgvValue, e))
            {
                return;
            }
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

        private bool TryGetSingleSelectedSlotIndex(out int slotIndex)
        {
            slotIndex = -1;
            if (dgvValue.SelectedRows != null && dgvValue.SelectedRows.Count > 0)
            {
                if (dgvValue.SelectedRows.Count > 1)
                {
                    MessageBox.Show("一次只能操作一行");
                    return false;
                }
                slotIndex = GetVariableSlotIndex(dgvValue.SelectedRows[0].Index);
                return slotIndex >= 0;
            }
            if (dgvValue.CurrentCell == null)
            {
                MessageBox.Show("没有选定的变量");
                return false;
            }
            slotIndex = GetSelectedVariableSlotIndex();
            return slotIndex >= 0;
        }

        private List<int> GetSelectedSlotIndexes()
        {
            HashSet<int> indexes = new HashSet<int>();
            if (dgvValue.SelectedRows != null && dgvValue.SelectedRows.Count > 0)
            {
                foreach (DataGridViewRow row in dgvValue.SelectedRows)
                {
                    if (row != null && row.Index >= 0)
                    {
                        int slotIndex = GetVariableSlotIndex(row.Index);
                        if (slotIndex >= 0) indexes.Add(slotIndex);
                    }
                }
            }
            else if (dgvValue.CurrentCell != null)
            {
                int slotIndex = GetSelectedVariableSlotIndex();
                if (slotIndex >= 0) indexes.Add(slotIndex);
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
            if (!TryGetSingleSelectedSlotIndex(out int rowIndex))
            {
                return;
            }
            if (!Workspace.Runtime.Stores.Values.TryGetValueByIndex(rowIndex, out DicValue value))
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
            if (!Workspace.Runtime.ProcessEditing.CanEditStructure()) return;
            if (clipboardItem == null)
            {
                MessageBox.Show("没有可粘贴的数据");
                return;
            }
            if (!TryGetSingleSelectedSlotIndex(out int rowIndex)) return;
            Workspace.Runtime.Stores.Values.TryGetValueByIndex(rowIndex, out DicValue targetVariable);
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
                "粘贴变量",
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
                if (!Workspace.Runtime.Stores.Values.TryGetValueByName(candidate, out _))
                {
                    return candidate;
                }
                suffix++;
            }
            return null;
        }

        private void ClearSelectedValueRows(bool requireConfirm = false)
        {
            if (!Workspace.Runtime.ProcessEditing.CanEditStructure()) return;
            List<int> indexes = GetSelectedSlotIndexes();
            if (indexes.Count == 0)
            {
                MessageBox.Show("没有选定的变量");
                return;
            }
            List<DicValue> variables = indexes
                .Select(index => Workspace.Runtime.Stores.Values.TryGetValueByIndex(index, out DicValue value) ? value : null)
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
            Dictionary<string, DicValue> draft = Workspace.Runtime.Stores.Values.BuildSaveData();
            foreach (DicValue variable in variables)
            {
                draft.Remove(variable.Name);
            }
            if (!Workspace.Runtime.Stores.Values.TryCommitConfiguration(
                Workspace.Runtime.Paths.ConfigPath,
                draft,
                out string error,
                historyDescription: "删除变量"))
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
            num = GetVariableSlotIndex(rowIndex);
            if (num < 0)
            {
                return false;
            }
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
                    int editedSlotIndex = GetVariableSlotIndex(e.RowIndex);
                    if (editedSlotIndex < 0) return;
                    if (e.ColumnIndex == 3
                        && Workspace.Runtime.Stores.Values.TryGetValueByIndex(editedSlotIndex, out DicValue existing))
                    {
                        string editedValue = dataGridView.Rows[e.RowIndex].Cells[3].Value?.ToString() ?? string.Empty;
                        if (!Workspace.Runtime.Stores.Values.setValueByIndex(existing.Index, editedValue, "变量页设置当前值"))
                        {
                            isValueEditValid = false;
                            MessageBox.Show($"变量[{existing.Name}]当前值无效。", "设置当前值失败",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                            FreshFrmValue();
                            return;
                        }
                        if (variableSnapshot[editedSlotIndex] != null)
                        {
                            variableSnapshot[editedSlotIndex].Value = editedValue;
                        }
                        isValueEditValid = true;
                        return;
                    }
                    if (!Workspace.Runtime.ProcessEditing.CanEditStructure())
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
                        && Workspace.Runtime.Stores.Values.TryGetValueByIndex(num, out DicValue renamedVariable)
                        && !string.Equals(renamedVariable.Name, key, StringComparison.Ordinal))
                    {
                        int usageCount = CountVariableUsages(renamedVariable);
                        if (usageCount > 0)
                        {
                            bool confirmed = false;
                            new Message(Workspace.Runtime,
                                "确认重命名变量",
                                $"变量[{renamedVariable.Name}]存在{usageCount}个已知引用。重命名后引用文本保持不变，受影响流程会变为 incomplete。是否提交？",
                                () => confirmed = true,
                                () => { },
                                "是(Y)",
                                "否(N)",
                                true);
                            if (!confirmed)
                            {
                                isValueEditValid = false;
                                FreshFrmValue();
                                return;
                            }
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
                    if (Workspace.Runtime.Stores.Values.TryGetValueByIndex(num, out DicValue scopedVariable))
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
                            ConfigureVariableScopeCell(dataGridView.Rows[e.RowIndex], num, scopedVariable);
                            return;
                        }
                        bool scopeChanged = !string.Equals(
                                scopedVariable.Scope, targetScope, StringComparison.Ordinal)
                            || scopedVariable.OwnerProcId != targetOwnerProcId;
                        if (scopeChanged)
                        {
                            int usageCount = CountVariableUsages(scopedVariable);
                            if (usageCount > 0)
                            {
                                bool confirmed = false;
                                new Message(Workspace.Runtime,
                                    "确认更改变量归属",
                                    $"变量[{scopedVariable.Name}]存在{usageCount}个已知引用。更改归属后，不可访问该变量的流程会变为 incomplete。是否提交？",
                                    () => confirmed = true,
                                    () => { },
                                    "是(Y)",
                                    "否(N)",
                                    true);
                                if (!confirmed)
                                {
                                    isValueEditValid = false;
                                    ConfigureVariableScopeCell(dataGridView.Rows[e.RowIndex], num, scopedVariable);
                                    return;
                                }
                            }
                        }
                    }
                    if (!TryCommitVariableRow(
                        num, key, type, value, note, targetScope, targetOwnerProcId,
                        scopedVariable == null,
                        scopedVariable == null ? "新增变量" : "修改变量",
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
                    ConfigureVariableScopeCell(dataGridView.Rows[e.RowIndex], num, committedVariable);
                    ApplyVariableRowEditingState(dataGridView.Rows[e.RowIndex], committedVariable);
                    RefreshVariableSnapshot();
                    ScheduleScopeFilter(true);
                    isValueEditValid = true;
                }
            }
        }

        private void dgvValue_CellMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
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
                if (Workspace.Runtime.Stores.Values.IsMarked(i))
                {
                    previousIndex = i;
                    break;
                }
            }
            if (previousIndex == -1)
            {
                for (int i = ValueConfigStore.ValueCapacity - 1; i >= 0; i--)
                {
                    if (Workspace.Runtime.Stores.Values.IsMarked(i))
                    {
                        previousIndex = i;
                        break;
                    }
                }
            }
            if (previousIndex != -1)
            {
                currentIndex = previousIndex;
                int rowIndex = GetVariableDisplayRowIndex(currentIndex);
                if (rowIndex >= 0)
                {
                    dgvValue.FirstDisplayedScrollingRowIndex = rowIndex;
                    RefreshViewportRows();
                }
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
                if (Workspace.Runtime.Stores.Values.IsMarked(i))
                {
                    nextIndex = i;
                    break;
                }
            }
            if (nextIndex == -1)
            {
                for (int i = 0; i < ValueConfigStore.ValueCapacity; i++)
                {
                    if (Workspace.Runtime.Stores.Values.IsMarked(i))
                    {
                        nextIndex = i;
                        break;
                    }
                }
            }
            if (nextIndex != -1)
            {
                currentIndex = nextIndex;
                int rowIndex = GetVariableDisplayRowIndex(currentIndex);
                if (rowIndex >= 0)
                {
                    dgvValue.FirstDisplayedScrollingRowIndex = rowIndex;
                    RefreshViewportRows();
                }
            }
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            Workspace.Runtime.Stores.Values.ClearMarks();
            Workspace.Runtime.Stores.Values.Save(Workspace.Runtime.Paths.ConfigPath);
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
            if (isRebuildingVariableRows) return;
            int slotIndex = GetVariableSlotIndex(e.RowIndex);
            DataGridViewCell currentCell = dgvValue.CurrentCell;
            if (hasVariableSnapshot
                && slotIndex >= 0
                && variableRowLoadedVersions[slotIndex] != variableGridPresentationVersion
                && e.ColumnIndex >= 0
                && e.ColumnIndex <= scopeColumn.Index
                && !(dgvValue.IsCurrentCellInEditMode
                    && currentCell != null
                    && currentCell.RowIndex == e.RowIndex
                    && currentCell.ColumnIndex == e.ColumnIndex))
            {
                DicValue variable = variableSnapshot[slotIndex];
                switch (e.ColumnIndex)
                {
                    case 0:
                        e.Value = slotIndex;
                        break;
                    case 1:
                        e.Value = variable?.Name ?? string.Empty;
                        break;
                    case 2:
                        e.Value = variable?.Type ?? DefaultValueType;
                        break;
                    case 3:
                        e.Value = variable?.Value ?? DefaultValueText;
                        break;
                    case 4:
                        e.Value = variable?.Note ?? string.Empty;
                        break;
                    case 5:
                        if (!(dgvValue.Rows[e.RowIndex].Cells[e.ColumnIndex] is DataGridViewComboBoxCell))
                        {
                            e.Value = variable == null
                                ? ValueConfigStore.IsSystemValueIndex(slotIndex) ? "系统" : "未配置"
                                : FormatVariableScope(variable);
                        }
                        break;
                }
            }
            if (e.ColumnIndex == 0 && slotIndex >= 0)
            {
                // 获取当前行对应的数据项
                if (Workspace.Runtime.Stores.Values.IsMarked(slotIndex))
                {
                    e.CellStyle.BackColor = UiPalette.DangerSoft;
                    e.CellStyle.ForeColor = UiPalette.DangerHover;
                }
                else
                {
                    e.CellStyle.BackColor = e.RowIndex % 2 == 0 ? UiPalette.SurfaceStrong : UiPalette.Input;
                    e.CellStyle.ForeColor = UiPalette.TextPrimary;
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
            if (index < 0 || index >= ValueConfigStore.ValueCapacity)
            {
                return;
            }
            if (Workspace.Runtime.Stores.Values.TryGetValueByIndex(index, out DicValue value))
            {
                selectedScope = value.Scope;
                selectedOwnerProcId = value.OwnerProcId;
                RefreshScopeTree();
            }
            else
            {
                selectedScope = AllVariableScopes;
                selectedOwnerProcId = null;
                RefreshScopeTree();
            }
            ApplyScopeFilter();
            int rowIndex = GetVariableDisplayRowIndex(index);
            if (rowIndex < 0) return;
            dgvValue.ClearSelection();
            dgvValue.CurrentCell = dgvValue.Rows[rowIndex].Cells[0];
            dgvValue.FirstDisplayedScrollingRowIndex = rowIndex;
            RefreshViewportRows();
            dgvValue.Focus();
        }

        private void RefreshCommonList()
        {
            ApplyScopeFilter();
        }

        private int CountVariableUsages(DicValue variable)
        {
            int count = 0;
            foreach (Proc proc in Workspace.Proc?.procsList ?? new List<Proc>())
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

        private bool TryCommitVariableRow(
            int index,
            string name,
            string type,
            string currentValue,
            string note,
            string scope,
            Guid? ownerProcId,
            bool setCurrentValue,
            string historyDescription,
            out string error)
        {
            error = null;
            Dictionary<string, DicValue> draft = Workspace.Runtime.Stores.Values.BuildSaveData();
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
            return Workspace.Runtime.Stores.Values.TryCommitConfiguration(
                Workspace.Runtime.Paths.ConfigPath,
                draft,
                out error,
                setCurrentValue
                    ? new Dictionary<string, string>(StringComparer.Ordinal) { [name] = currentValue }
                    : null,
                setCurrentValue ? "变量页保存当前值" : null,
                historyDescription);
        }

        private void EnsureValueStoreHooked()
        {
            if (isValueStoreHooked)
            {
                return;
            }
            if (Workspace.Runtime.Stores.Values == null)
            {
                return;
            }
            hookedValueStore = Workspace.Runtime.Stores.Values;
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
            if (!Workspace.Runtime.Stores.Values.TryGetValueByIndex(index, out DicValue value))
            {
                return;
            }
            if (monitoredVariableIds.Contains(value.Id)) return;
            Workspace.Runtime.Stores.Values.SetMonitorFlag(index, true);
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
            if (!Workspace.Runtime.Stores.Values.TryGetValueByIndex(index, out DicValue value)) return;
            RemoveMonitor(value.Id);
        }

        private void RemoveMonitor(Guid variableId)
        {
            if (!monitoredVariableIds.Remove(variableId)) return;
            Workspace.Runtime.Stores.Values.SetMonitorFlag(variableId, false);
            RefreshMonitorTitle();
        }

        private void StopAllMonitor()
        {
            if (monitoredVariableIds.Count == 0)
            {
                RefreshMonitorTitle();
                Workspace.Runtime.Stores.Values.SetMonitorEnabled(false);
                return;
            }
            List<Guid> variableIds = new List<Guid>(monitoredVariableIds);
            foreach (Guid variableId in variableIds)
            {
                Workspace.Runtime.Stores.Values.SetMonitorFlag(variableId, false);
            }
            monitoredVariableIds.Clear();
            if (monitorForm != null && !monitorForm.IsDisposed)
            {
                monitorForm.Grid.Rows.Clear();
            }
            RefreshMonitorTitle();
            Workspace.Runtime.Stores.Values.SetMonitorEnabled(false);
        }

        private void btnMonitor_Click(object sender, EventArgs e)
        {
            EnsureMonitorForm();
            Workspace.Runtime.Stores.Values.SetMonitorEnabled(true);
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
                Workspace.Runtime.Stores.Values.SetMonitorEnabled(true);
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
            int slotIndex = GetSelectedVariableSlotIndex();
            if (slotIndex >= 0) RemoveMonitor(slotIndex);
        }

        private void AddMonitorFromSelection()
        {
            if (dgvValue.CurrentCell == null)
            {
                MessageBox.Show("没有选定的变量");
                return;
            }
            int slotIndex = GetSelectedVariableSlotIndex();
            if (slotIndex < 0 || !Workspace.Runtime.Stores.Values.TryGetValueByIndex(slotIndex, out _))
            {
                MessageBox.Show("当前槽位尚未配置变量。", "加入监控",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            AddMonitor(slotIndex);
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
                int rowIndex = GetVariableDisplayRowIndex(index);
                if (rowIndex >= 0)
                {
                    currentIndex = index;
                    dgvValue.ClearSelection();
                    dgvValue.CurrentCell = dgvValue.Rows[rowIndex].Cells[0];
                    dgvValue.Rows[rowIndex].Selected = true;
                    dgvValue.FirstDisplayedScrollingRowIndex = rowIndex;
                    RefreshViewportRows();
                }
            }
        }

    }
}
