using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Automation.Protocol;

namespace Automation
{
    public partial class FrmValue : Form
    {  //存放变量对象
        private const string DefaultValueType = "double";
        private const string DefaultValueText = "0";
        private static readonly Color HeaderBackColor = Color.FromArgb(238, 243, 248);
        private static readonly Color HeaderForeColor = Color.FromArgb(48, 63, 78);
        private static readonly Color GridLineColor = Color.FromArgb(203, 213, 224);
        private static readonly Color AlternateRowColor = Color.FromArgb(248, 250, 252);
        private static readonly Color SelectionBackColor = Color.FromArgb(217, 234, 250);
        private static readonly Color SelectionForeColor = Color.FromArgb(27, 43, 59);

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

        private sealed class VariableScopeChoice
        {
            public string Text { get; set; }
            public string Scope { get; set; }
            public Guid? OwnerProcId { get; set; }
            public override string ToString() => Text;
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

                btnAdd = new Button
                {
                    Text = "添加当前",
                    Font = uiFont,
                    Size = new Size(96, 30),
                    Anchor = AnchorStyles.Top | AnchorStyles.Right,
                    Location = new Point(300, 5)
                };
                btnAdd.Click += (s, e) => owner.AddMonitorFromSelection();

                btnRemove = new Button
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
        private readonly Button btnMoveIndex;
        private string selectedScope = VariableScopeContract.Public;
        private Guid? selectedOwnerProcId;
        private int pendingNewIndex = -1;
        private bool isStructViewAttached;
        private bool isValueGridReady;
        private bool isValueEditValid = true;
        private const int MaxMonitorHistoryRows = 2000;
        private const int MonitorHistoryTrimRows = 200;

        public FrmValue()
        {
            InitializeComponent();
            value.HeaderText = "当前值";
            scopeColumn = new DataGridViewTextBoxColumn
            {
                Name = "variableScope",
                HeaderText = "作用域",
                ReadOnly = true,
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
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Microsoft YaHei UI", 10F),
                HideSelection = false
            };
            scopeTree.AfterSelect += ScopeTree_AfterSelect;
            panelCommon.Controls.Add(scopeTree);
            scopeTree.BringToFront();
            labelCommon.BringToFront();

            btnNormalVariables.Text = "新增变量";
            btnSystemVariables.Visible = false;
            btnSet.Text = "移动作用域";
            btnMoveIndex = new Button
            {
                Text = "移动槽位",
                Location = new Point(1302, 6),
                Size = new Size(100, 36)
            };
            btnMoveIndex.Click += btnMoveIndex_Click;
            panel1.Controls.Add(btnMoveIndex);
            btnPrevious.Visible = false;
            BtnNext.Visible = false;
            btnCancel.Visible = false;
            Disposed += FrmValue_Disposed;
            Font uiFont = new Font("宋体", 12F, FontStyle.Regular, GraphicsUnit.Point, ((byte)(134)));
            dgvValue.Font = uiFont;
            dgvValue.ColumnHeadersDefaultCellStyle.Font = new Font("宋体", 12F, FontStyle.Bold, GraphicsUnit.Point, ((byte)(134)));
            dgvValue.SelectionMode = DataGridViewSelectionMode.ColumnHeaderSelect;
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
            ApplyAlarmConfigStyle();
        }

        private void ApplyAlarmConfigStyle()
        {
            BackColor = Color.White;
            panel1.BackColor = HeaderBackColor;
            panelCommon.BackColor = Color.White;
            panelStructHost.BackColor = Color.White;
            splitContainerMain.BackColor = GridLineColor;
            splitContainerMain.BorderStyle = BorderStyle.FixedSingle;
            labelCommon.BackColor = HeaderBackColor;
            labelCommon.ForeColor = HeaderForeColor;
            labelCommon.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold);

            listCommon.BackColor = Color.White;
            listCommon.ForeColor = HeaderForeColor;
            listCommon.BorderStyle = BorderStyle.FixedSingle;
            listCommon.Font = new Font("Microsoft YaHei UI", 10F);
            listCommon.DrawMode = DrawMode.OwnerDrawFixed;
            listCommon.ItemHeight = 30;
            listCommon.DrawItem += listCommon_DrawItem;

            ApplyGridStyle(dgvValue);
            ApplyButtonStyle(btnSearch, true, false);
            ApplyButtonStyle(btnSet, false, false);
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
            ApplyButtonStyle(btnMoveIndex, false, false);
        }

        private static void ApplyGridStyle(DataGridView grid)
        {
            grid.Font = new Font("Microsoft YaHei UI", 10F);
            grid.BackgroundColor = Color.White;
            grid.BorderStyle = BorderStyle.FixedSingle;
            grid.GridColor = GridLineColor;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.Single;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersHeight = 34;
            grid.ColumnHeadersDefaultCellStyle.BackColor = HeaderBackColor;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = HeaderForeColor;
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.RowTemplate.Height = 30;
            grid.DefaultCellStyle.BackColor = Color.White;
            grid.DefaultCellStyle.ForeColor = HeaderForeColor;
            grid.AlternatingRowsDefaultCellStyle.BackColor = AlternateRowColor;
            grid.DefaultCellStyle.SelectionBackColor = SelectionBackColor;
            grid.DefaultCellStyle.SelectionForeColor = SelectionForeColor;
        }

        private static void ApplyButtonStyle(Button button, bool primary, bool danger)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular);
            if (primary)
            {
                button.BackColor = Color.FromArgb(34, 111, 183);
                button.ForeColor = Color.White;
                button.FlatAppearance.BorderColor = Color.FromArgb(34, 111, 183);
                button.FlatAppearance.MouseOverBackColor = Color.FromArgb(43, 126, 201);
                button.FlatAppearance.MouseDownBackColor = Color.FromArgb(22, 83, 139);
            }
            else if (danger)
            {
                button.BackColor = Color.FromArgb(255, 245, 245);
                button.ForeColor = Color.FromArgb(170, 48, 48);
                button.FlatAppearance.BorderColor = Color.FromArgb(226, 176, 176);
                button.FlatAppearance.MouseOverBackColor = Color.FromArgb(253, 229, 229);
                button.FlatAppearance.MouseDownBackColor = Color.FromArgb(248, 212, 212);
            }
            else
            {
                button.BackColor = Color.White;
                button.ForeColor = HeaderForeColor;
                button.FlatAppearance.BorderColor = Color.FromArgb(190, 199, 210);
                button.FlatAppearance.MouseOverBackColor = Color.FromArgb(237, 240, 244);
                button.FlatAppearance.MouseDownBackColor = Color.FromArgb(225, 230, 236);
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

            for (int i = 0; i < ValueConfigStore.ValueCapacity; i++)
            {
                dgvValue.Rows.Add();

                dgvValue.Rows[i].Cells[0].Value = i;
                dgvValue.Rows[i].Cells[1].Value = string.Empty;
                dgvValue.Rows[i].Cells[2].Value = DefaultValueType;
                dgvValue.Rows[i].Cells[3].Value = DefaultValueText;
                dgvValue.Rows[i].Cells[4].Value = string.Empty;
                dgvValue.Rows[i].Cells[5].Value = selectedScope;
                dgvValue.Rows[i].Cells[6].Value = ResolveOwnerProcessName(selectedOwnerProcId);
                if (SF.valueStore.TryGetValueByIndex(i, out DicValue cachedValue))
                {
                    dgvValue.Rows[i].Cells[1].Value = cachedValue.Name;
                    dgvValue.Rows[i].Cells[2].Value = cachedValue.Type;
                    dgvValue.Rows[i].Cells[3].Value = cachedValue.Value;
                    dgvValue.Rows[i].Cells[4].Value = cachedValue.Note;
                    dgvValue.Rows[i].Cells[5].Value = cachedValue.Scope;
                    dgvValue.Rows[i].Cells[6].Value = ResolveOwnerProcessName(cachedValue.OwnerProcId);
                }
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
                for (int i = 0; i < ValueConfigStore.ValueCapacity; i++)
                {
                    if (SF.valueStore.TryGetValueByIndex(i, out DicValue cachedValue))
                    {
                        dgvValue.Rows[i].Cells[0].Value = i;
                        dgvValue.Rows[i].Cells[1].Value = cachedValue.Name;
                        dgvValue.Rows[i].Cells[2].Value = cachedValue.Type;
                        dgvValue.Rows[i].Cells[3].Value = cachedValue.Value;
                        dgvValue.Rows[i].Cells[4].Value = cachedValue.Note;
                        dgvValue.Rows[i].Cells[5].Value = cachedValue.Scope;
                        dgvValue.Rows[i].Cells[6].Value = ResolveOwnerProcessName(cachedValue.OwnerProcId);
                    }
                    else
                    {
                        dgvValue.Rows[i].Cells[0].Value = i;
                        dgvValue.Rows[i].Cells[1].Value = string.Empty;
                        dgvValue.Rows[i].Cells[2].Value = DefaultValueType;
                        dgvValue.Rows[i].Cells[3].Value = DefaultValueText;
                        dgvValue.Rows[i].Cells[4].Value = string.Empty;
                        dgvValue.Rows[i].Cells[5].Value = selectedScope;
                        dgvValue.Rows[i].Cells[6].Value = ResolveOwnerProcessName(selectedOwnerProcId);
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
            TreeNode publicNode = new TreeNode("公共变量")
            {
                Tag = new VariableScopeSelection { Scope = VariableScopeContract.Public }
            };
            TreeNode systemNode = new TreeNode("系统变量")
            {
                Tag = new VariableScopeSelection { Scope = VariableScopeContract.System }
            };
            TreeNode processRoot = new TreeNode("流程私有变量");
            foreach (Proc proc in SF.frmProc?.procsList ?? new List<Proc>())
            {
                if (proc?.head == null || proc.head.Id == Guid.Empty) continue;
                processRoot.Nodes.Add(new TreeNode(proc.head.Name ?? proc.head.Id.ToString("D"))
                {
                    Tag = new VariableScopeSelection
                    {
                        Scope = VariableScopeContract.Process,
                        OwnerProcId = proc.head.Id
                    }
                });
            }
            scopeTree.Nodes.Add(publicNode);
            scopeTree.Nodes.Add(processRoot);
            scopeTree.Nodes.Add(systemNode);
            processRoot.Expand();
            TreeNode selectedNode = scopeTree.Nodes.Cast<TreeNode>()
                .SelectMany(FlattenTree)
                .FirstOrDefault(node => node.Tag is VariableScopeSelection selection
                    && string.Equals(selection.Scope, selectedScope, StringComparison.Ordinal)
                    && selection.OwnerProcId == selectedOwnerProcId)
                ?? publicNode;
            scopeTree.SelectedNode = selectedNode;
            selectedNode.EnsureVisible();
            scopeTree.EndUpdate();
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
            pendingNewIndex = -1;
            ApplyScopeFilter();
        }

        private void ApplyScopeFilter()
        {
            if (dgvValue.Rows.Count == 0) return;
            dgvValue.CurrentCell = null;
            int visibleCount = 0;
            for (int index = 0; index < dgvValue.Rows.Count; index++)
            {
                bool visible = index == pendingNewIndex;
                bool systemReadOnly = false;
                if (SF.valueStore.TryGetValueByIndex(index, out DicValue value))
                {
                    visible = string.Equals(value.Scope, selectedScope, StringComparison.Ordinal)
                        && (!string.Equals(selectedScope, VariableScopeContract.Process, StringComparison.Ordinal)
                            || value.OwnerProcId == selectedOwnerProcId);
                    systemReadOnly = ValueConfigStore.IsSystemValueIndex(value.Index);
                }
                DataGridViewRow row = dgvValue.Rows[index];
                row.Visible = visible;
                row.ReadOnly = false;
                if (systemReadOnly)
                {
                    for (int column = 0; column < row.Cells.Count; column++)
                    {
                        row.Cells[column].ReadOnly = column != 3;
                    }
                }
                else
                {
                    for (int column = 0; column < row.Cells.Count; column++)
                    {
                        row.Cells[column].ReadOnly = column == 0 || column >= 5;
                    }
                }
                if (visible) visibleCount++;
            }
            labelCommon.Text = string.Equals(selectedScope, VariableScopeContract.Process, StringComparison.Ordinal)
                ? $"{ResolveOwnerProcessName(selectedOwnerProcId)} 私有变量({visibleCount})"
                : (string.Equals(selectedScope, VariableScopeContract.System, StringComparison.Ordinal)
                    ? $"系统变量({visibleCount})"
                    : $"公共变量({visibleCount})");
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
            int selectedProcIndex = SF.frmProc?.SelectedProcNum ?? -1;
            if (selectedProcIndex >= 0 && selectedProcIndex < (SF.frmProc?.procsList?.Count ?? 0))
            {
                LocateProcessVariables(SF.frmProc.procsList[selectedProcIndex].head.Id);
            }
            AttachDataStructView();
            SetDefaultStructPanelRatio();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible || SF.frmProc?.procsList == null) return;
            int selectedProcIndex = SF.frmProc.SelectedProcNum;
            if (selectedProcIndex >= 0 && selectedProcIndex < SF.frmProc.procsList.Count)
            {
                LocateProcessVariables(SF.frmProc.procsList[selectedProcIndex].head.Id);
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
            int targetLeftWidth = (int)(splitContainerMain.Width * 0.75);
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
            if (!TryGetSingleSelectedRowIndex(out int rowIndex))
            {
                return;
            }
            DataGridViewRow row = dgvValue.Rows[rowIndex];
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
            if (!TryCommitVariableRow(
                rowIndex, nameToUse, clipboardItem.Type, clipboardItem.Value, clipboardItem.Note,
                selectedScope, selectedOwnerProcId, out string commitError))
            {
                MessageBox.Show("粘贴失败:" + commitError);
                return;
            }
            row.Cells[1].Value = nameToUse;
            row.Cells[2].Value = clipboardItem.Type;
            row.Cells[3].Value = clipboardItem.Value;
            row.Cells[4].Value = clipboardItem.Note;
            row.Cells[5].Value = selectedScope;
            row.Cells[6].Value = ResolveOwnerProcessName(selectedOwnerProcId);
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
                    if (!TryCommitVariableRow(
                        num, key, type, value, note, selectedScope, selectedOwnerProcId,
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
                    dataGridView.Rows[e.RowIndex].Cells[5].Value = selectedScope;
                    dataGridView.Rows[e.RowIndex].Cells[6].Value = ResolveOwnerProcessName(selectedOwnerProcId);
                    pendingNewIndex = -1;
                    ApplyScopeFilter();
                    isValueEditValid = true;
                }
            }
        }

        private void dgvValue_CellMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && isValueEditValid)
                FreshFrmValue();
        }

        private void btnSet_Click(object sender, EventArgs e)
        {
            if (!SF.CanEditProcStructure()) return;
            if (!TryGetSingleSelectedRowIndex(out int index)
                || !SF.valueStore.TryGetValueByIndex(index, out DicValue variable))
            {
                MessageBox.Show("请选择需要移动作用域的变量。");
                return;
            }
            if (ValueConfigStore.IsSystemValueIndex(variable.Index))
            {
                MessageBox.Show("系统变量配置只读。");
                return;
            }
            var choices = new List<VariableScopeChoice>
            {
                new VariableScopeChoice { Text = "公共变量", Scope = VariableScopeContract.Public }
            };
            choices.AddRange((SF.frmProc?.procsList ?? new List<Proc>())
                .Where(proc => proc?.head != null && proc.head.Id != Guid.Empty)
                .Select(proc => new VariableScopeChoice
                {
                    Text = $"流程私有变量 / {proc.head.Name}",
                    Scope = VariableScopeContract.Process,
                    OwnerProcId = proc.head.Id
                }));
            using (var dialog = new Form
            {
                Text = "移动变量作用域",
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MinimizeBox = false,
                MaximizeBox = false,
                ClientSize = new Size(420, 125)
            })
            using (var selector = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(20, 20),
                Width = 380,
                DataSource = choices
            })
            using (var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Location = new Point(230, 70), Width = 80 })
            using (var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Location = new Point(320, 70), Width = 80 })
            {
                selector.SelectedItem = choices.FirstOrDefault(choice =>
                    string.Equals(choice.Scope, variable.Scope, StringComparison.Ordinal)
                    && choice.OwnerProcId == variable.OwnerProcId) ?? choices[0];
                dialog.Controls.Add(selector);
                dialog.Controls.Add(ok);
                dialog.Controls.Add(cancel);
                dialog.AcceptButton = ok;
                dialog.CancelButton = cancel;
                if (dialog.ShowDialog(this) != DialogResult.OK
                    || !(selector.SelectedItem is VariableScopeChoice choice)) return;

                int usageCount = CountVariableUsages(variable);
                if (MessageBox.Show(
                    $"变量[{variable.Name}]存在{usageCount}个已知引用。移动后引用文本保持不变，不可访问的流程将变为 incomplete。是否提交？",
                    "确认移动作用域", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                {
                    return;
                }
                Dictionary<string, DicValue> draft = SF.valueStore.BuildSaveData();
                DicValue updated = ObjectGraphCloner.Clone(draft[variable.Name]);
                updated.Scope = choice.Scope;
                updated.OwnerProcId = choice.OwnerProcId;
                draft[variable.Name] = updated;
                if (!SF.valueStore.TryCommitConfiguration(SF.ConfigPath, draft, out string error))
                {
                    MessageBox.Show(error, "移动作用域失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                selectedScope = choice.Scope;
                selectedOwnerProcId = choice.OwnerProcId;
                FreshFrmValue();
                LocateValueIndex(updated.Index);
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
            if (e.ColumnIndex == 4)
            {
                dgvValue[e.ColumnIndex, e.RowIndex].Style.Alignment = DataGridViewContentAlignment.MiddleLeft;
            }
            else
            {
                dgvValue[e.ColumnIndex, e.RowIndex].Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            }

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
            SF.frmSearch4Value.StartPosition = FormStartPosition.CenterScreen;
            SF.frmSearch4Value.Show();
            SF.frmSearch4Value.BringToFront();
            SF.frmSearch4Value.WindowState = FormWindowState.Normal;
            SF.frmSearch4Value.textBox1.Focus();
        }

        private void btnNormalVariables_Click(object sender, EventArgs e)
        {
            if (string.Equals(selectedScope, VariableScopeContract.System, StringComparison.Ordinal))
            {
                MessageBox.Show("系统变量配置只读。");
                return;
            }
            for (int index = 0; index < ValueConfigStore.NormalValueCapacity; index++)
            {
                if (SF.valueStore.TryGetValueByIndex(index, out _)) continue;
                pendingNewIndex = index;
                ApplyScopeFilter();
                dgvValue.Rows[index].Cells[3].Value = DefaultValueText;
                dgvValue.Rows[index].Cells[5].Value = selectedScope;
                dgvValue.Rows[index].Cells[6].Value = ResolveOwnerProcessName(selectedOwnerProcId);
                dgvValue.CurrentCell = dgvValue.Rows[index].Cells[1];
                dgvValue.FirstDisplayedScrollingRowIndex = index;
                dgvValue.BeginEdit(true);
                return;
            }
            MessageBox.Show("普通变量区没有空闲槽位。");
        }

        private void btnSystemVariables_Click(object sender, EventArgs e)
        {
            // 按钮已隐藏；系统变量通过左侧作用域树查看。
        }

        private void btnMoveIndex_Click(object sender, EventArgs e)
        {
            if (!SF.CanEditProcStructure()) return;
            if (!TryGetSingleSelectedRowIndex(out int currentIndex)
                || !SF.valueStore.TryGetValueByIndex(currentIndex, out DicValue variable))
            {
                MessageBox.Show("请选择需要移动槽位的变量。");
                return;
            }
            if (ValueConfigStore.IsSystemValueIndex(variable.Index))
            {
                MessageBox.Show("系统变量配置只读。");
                return;
            }
            using (var dialog = new Form
            {
                Text = "移动变量槽位",
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MinimizeBox = false,
                MaximizeBox = false,
                ClientSize = new Size(360, 125)
            })
            using (var target = new NumericUpDown
            {
                Minimum = 0,
                Maximum = ValueConfigStore.NormalValueCapacity - 1,
                Value = variable.Index,
                Location = new Point(20, 20),
                Width = 320
            })
            using (var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Location = new Point(170, 70), Width = 80 })
            using (var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Location = new Point(260, 70), Width = 80 })
            {
                dialog.Controls.Add(target);
                dialog.Controls.Add(ok);
                dialog.Controls.Add(cancel);
                dialog.AcceptButton = ok;
                dialog.CancelButton = cancel;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                int targetIndex = (int)target.Value;
                if (targetIndex == variable.Index) return;
                if (SF.valueStore.TryGetValueByIndex(targetIndex, out DicValue occupied))
                {
                    MessageBox.Show($"槽位{targetIndex}已被变量[{occupied.Name}]占用。");
                    return;
                }
                int usageCount = CountVariableUsages(variable);
                if (MessageBox.Show(
                    $"变量[{variable.Name}]存在{usageCount}个已知引用。移动后索引引用文本保持不变，受影响流程会变为 incomplete。是否提交？",
                    "确认移动槽位", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                {
                    return;
                }
                Dictionary<string, DicValue> draft = SF.valueStore.BuildSaveData();
                DicValue updated = ObjectGraphCloner.Clone(draft[variable.Name]);
                updated.Index = targetIndex;
                draft[variable.Name] = updated;
                if (!SF.valueStore.TryCommitConfiguration(SF.ConfigPath, draft, out string error))
                {
                    MessageBox.Show(error, "移动槽位失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                FreshFrmValue();
                LocateValueIndex(targetIndex);
            }
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
            if (current != null && ValueConfigStore.IsSystemValueIndex(current.Index))
            {
                error = "系统变量配置只读。";
                return false;
            }
            if (current != null) draft.Remove(current.Name);
            DicValue updated = current == null ? new DicValue() : ObjectGraphCloner.Clone(current);
            if (updated.Id == Guid.Empty) updated.Id = Guid.NewGuid();
            updated.Index = index;
            updated.Name = name;
            updated.Type = type;
            updated.Scope = scope;
            updated.OwnerProcId = ownerProcId;
            updated.Value = currentValue;
            updated.Note = note;
            draft[name] = updated;
            return SF.valueStore.TryCommitConfiguration(
                SF.ConfigPath,
                draft,
                out error,
                new Dictionary<string, string>(StringComparer.Ordinal) { [name] = currentValue },
                "变量页保存当前值");
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
