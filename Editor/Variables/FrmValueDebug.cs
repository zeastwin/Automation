// 模块：编辑器 / 变量。
// 职责范围：变量配置、运行值调试、变量选择和提交规则。

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace Automation
{
    public partial class FrmValueDebug : Form
    {
        private sealed class ValueOption
        {
            public int Index { get; set; }
            public string Name { get; set; }
            public string Type { get; set; }

            public override string ToString()
            {
                string label = string.IsNullOrWhiteSpace(Name) ? $"索引{Index:D3}" : Name;
                return $"{Index:D3}  {label}";
            }
        }

        private readonly HashSet<int> checkIndexSet = new HashSet<int>();
        private readonly HashSet<int> editIndexSet = new HashSet<int>();
        private readonly Dictionary<int, string> debugNotes = new Dictionary<int, string>();
        private List<ValueOption> valueOptions = new List<ValueOption>();
        private bool isSyncing;
        private bool isConfigLoaded;
        private bool hasRendered;
        private bool refreshScheduled;
        private long loadedDebugConfigurationVersion = -1;
        private long renderedValueConfigurationVersion = -1;
        public FrmValueDebug()
        {
            InitializeComponent();
            ConfigureResponsiveLayout();
            Font uiFont = new Font("微软雅黑", 10.5F, FontStyle.Regular, GraphicsUnit.Point, 134);

            dgvCheck.Font = uiFont;
            dgvCheck.ColumnHeadersDefaultCellStyle.Font = new Font("微软雅黑", 10F, FontStyle.Bold, GraphicsUnit.Point, 134);
            dgvCheck.RowHeadersVisible = false;
            dgvCheck.AutoGenerateColumns = false;
            dgvCheck.AllowUserToAddRows = false;
            dgvCheck.AllowUserToResizeRows = false;
            dgvCheck.MultiSelect = false;
            dgvCheck.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgvCheck.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dgvCheck.RowTemplate.Height = 28;
            dgvCheck.EditMode = DataGridViewEditMode.EditOnEnter;
            ApplyColumnTheme(groupCheck, panelCheckBottom, dgvCheck, UiPalette.BrandSoft, UiPalette.Selection);

            dgvEdit.Font = uiFont;
            dgvEdit.ColumnHeadersDefaultCellStyle.Font = new Font("微软雅黑", 10F, FontStyle.Bold, GraphicsUnit.Point, 134);
            dgvEdit.RowHeadersVisible = false;
            dgvEdit.AutoGenerateColumns = false;
            dgvEdit.AllowUserToAddRows = false;
            dgvEdit.AllowUserToResizeRows = false;
            dgvEdit.MultiSelect = false;
            dgvEdit.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgvEdit.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dgvEdit.RowTemplate.Height = 28;
            dgvEdit.ReadOnly = false;
            dgvEdit.EditMode = DataGridViewEditMode.EditOnEnter;
            ApplyColumnTheme(groupEdit, panelEditBottom, dgvEdit, UiPalette.SuccessSoft, UiPalette.SuccessSoft);

            cboCheckVar.Font = uiFont;
            cboEditVar.Font = uiFont;
            cboCheckVar.DropDownStyle = ComboBoxStyle.DropDownList;
            cboEditVar.DropDownStyle = ComboBoxStyle.DropDownList;
            btnCheckAdd.Font = uiFont;
            btnCheckRemove.Font = uiFont;
            btnCheckRefresh.Font = uiFont;
            btnEditAdd.Font = uiFont;
            btnEditRemove.Font = uiFont;
            btnEditRefresh.Font = uiFont;
            lblCheckStatus.Font = uiFont;
            lblEditStatus.Font = uiFont;
            lblCheckIndex.Font = uiFont;
            lblEditIndex.Font = uiFont;
            ApplyButtonStyle(btnCheckAdd, true);
            ApplyButtonStyle(btnEditAdd, true);
            ApplyButtonStyle(btnCheckRemove, false);
            ApplyButtonStyle(btnEditRemove, false);
            ApplyButtonStyle(btnCheckRefresh, false);
            ApplyButtonStyle(btnEditRefresh, false);
        }

        private void ConfigureResponsiveLayout()
        {
            groupCheck.Margin = new Padding(4);
            groupEdit.Margin = new Padding(4);
            panelCheckBottom.Height = 106;
            panelEditBottom.Height = 106;

            var checkLayout = CreateActionLayout(2);
            checkLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48F));
            checkLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            checkLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82F));
            checkLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82F));
            checkLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82F));
            checkLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            checkLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            panelCheckBottom.Controls.Clear();
            checkLayout.Controls.Add(lblCheckIndex, 0, 0);
            checkLayout.Controls.Add(cboCheckVar, 1, 0);
            checkLayout.Controls.Add(btnCheckAdd, 2, 0);
            checkLayout.Controls.Add(btnCheckRemove, 3, 0);
            checkLayout.Controls.Add(btnCheckRefresh, 4, 0);
            checkLayout.Controls.Add(lblCheckStatus, 0, 1);
            checkLayout.SetColumnSpan(lblCheckStatus, 5);
            panelCheckBottom.Controls.Add(checkLayout);

            var editLayout = CreateActionLayout(2);
            editLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48F));
            editLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            editLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82F));
            editLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82F));
            editLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82F));
            editLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            editLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            panelEditBottom.Controls.Clear();
            editLayout.Controls.Add(lblEditIndex, 0, 0);
            editLayout.Controls.Add(cboEditVar, 1, 0);
            editLayout.Controls.Add(btnEditAdd, 2, 0);
            editLayout.Controls.Add(btnEditRemove, 3, 0);
            editLayout.Controls.Add(btnEditRefresh, 4, 0);
            editLayout.Controls.Add(lblEditStatus, 0, 1);
            editLayout.SetColumnSpan(lblEditStatus, 5);
            panelEditBottom.Controls.Add(editLayout);

            foreach (Control control in new Control[]
            {
                cboCheckVar, btnCheckAdd, btnCheckRemove, btnCheckRefresh,
                cboEditVar, btnEditAdd, btnEditRemove, btnEditRefresh
            })
            {
                control.Dock = DockStyle.Fill;
                control.Margin = new Padding(3, 4, 3, 4);
            }
            foreach (Label label in new[] { lblCheckIndex, lblEditIndex })
            {
                label.Dock = DockStyle.Fill;
                label.TextAlign = ContentAlignment.MiddleLeft;
                label.Margin = new Padding(3);
            }
            lblCheckStatus.Dock = DockStyle.Fill;
            lblEditStatus.Dock = DockStyle.Fill;
        }

        private static TableLayoutPanel CreateActionLayout(int rowCount)
        {
            return new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 5,
                RowCount = rowCount,
                Padding = new Padding(6, 5, 6, 3),
                Margin = Padding.Empty
            };
        }

        private static void ApplyButtonStyle(Button button, bool primary)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = primary
                ? UiPalette.Brand
                : UiPalette.StrokeStrong;
            button.BackColor = primary ? UiPalette.Brand : UiPalette.SurfaceStrong;
            button.ForeColor = primary ? UiPalette.TextInverse : UiPalette.TextPrimary;
        }

        private void ApplyColumnTheme(GroupBox groupBox, Panel bottomPanel, DataGridView grid, Color baseColor, Color headerColor)
        {
            if (groupBox != null)
            {
                groupBox.BackColor = baseColor;
            }
            if (bottomPanel != null)
            {
                bottomPanel.BackColor = baseColor;
            }
            if (grid != null)
            {
                grid.BackgroundColor = baseColor;
                grid.EnableHeadersVisualStyles = false;
                grid.ColumnHeadersDefaultCellStyle.BackColor = headerColor;
                grid.ColumnHeadersDefaultCellStyle.ForeColor = UiPalette.TextPrimary;
                grid.DefaultCellStyle.BackColor = UiPalette.Surface;
                grid.DefaultCellStyle.ForeColor = UiPalette.TextPrimary;
                grid.AlternatingRowsDefaultCellStyle.BackColor = UiPalette.Background;
            }
        }

        private void FrmValueDebug_Load(object sender, EventArgs e)
        {
            ScheduleVisibleRefresh();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible)
            {
                ScheduleVisibleRefresh();
            }
        }

        private sealed class ValueDebugRowIdentity
        {
            public int Index { get; set; }
            public Guid VariableId { get; set; }
        }

        private void ScheduleVisibleRefresh()
        {
            if (refreshScheduled || !Visible || IsDisposed || Disposing || !IsHandleCreated)
            {
                return;
            }
            refreshScheduled = true;
            if (!hasRendered)
            {
                lblCheckStatus.Text = "正在加载变量列表...";
                lblEditStatus.Text = "正在加载变量列表...";
                lblCheckStatus.ForeColor = UiPalette.TextMuted;
                lblEditStatus.ForeColor = UiPalette.TextMuted;
            }
            try
            {
                BeginInvoke((Action)(() =>
                {
                    refreshScheduled = false;
                    if (!Visible || IsDisposed || Disposing)
                    {
                        return;
                    }
                    try
                    {
                        RefreshAllLists();
                    }
                    catch (Exception ex)
                    {
                        ShowCheckError("变量列表加载失败");
                        ShowEditError("变量列表加载失败");
                        LogError($"变量调试界面刷新失败:{ex.Message}");
                    }
                }));
            }
            catch (InvalidOperationException)
            {
                refreshScheduled = false;
            }
        }

        public void RefreshAllLists()
        {
            if (Workspace.Runtime.Stores.Values == null)
            {
                ShowCheckError("变量库未初始化");
                ShowEditError("变量库未初始化");
                return;
            }
            EnsureDebugConfigLoaded();
            RefreshValueOptionsIfChanged();
            RefreshCheckRows();
            RefreshEditRows();
            hasRendered = true;
        }

        public void RefreshCheckList()
        {
            if (Workspace.Runtime.Stores.Values == null)
            {
                ShowCheckError("变量库未初始化");
                return;
            }
            EnsureDebugConfigLoaded();
            RefreshValueOptionsIfChanged();
            RefreshCheckRows();
            hasRendered = true;
        }

        private void RefreshCheckRows()
        {
            isSyncing = true;
            dgvCheck.SuspendLayout();
            try
            {
                ReconcileRows(
                    dgvCheck,
                    checkIndexSet,
                    PopulateCheckRow);
            }
            finally
            {
                dgvCheck.ResumeLayout();
                isSyncing = false;
            }
        }

        public void RefreshEditList()
        {
            if (Workspace.Runtime.Stores.Values == null)
            {
                ShowEditError("变量库未初始化");
                return;
            }
            EnsureDebugConfigLoaded();
            RefreshValueOptionsIfChanged();
            RefreshEditRows();
            hasRendered = true;
        }

        private void RefreshEditRows()
        {
            isSyncing = true;
            dgvEdit.SuspendLayout();
            try
            {
                ReconcileRows(
                    dgvEdit,
                    editIndexSet,
                    PopulateEditRow);
            }
            finally
            {
                dgvEdit.ResumeLayout();
                isSyncing = false;
            }
            UpdateEditSelection();
        }

        private void btnCheckAdd_Click(object sender, EventArgs e)
        {
            if (!TryGetSelectedIndex(cboCheckVar, out int index, out string error))
            {
                ShowCheckError(error);
                return;
            }
            if (checkIndexSet.Contains(index))
            {
                ShowCheckError("变量已在复选框调试列表中");
                return;
            }
            if (Workspace.Runtime.Stores.Values == null || !Workspace.Runtime.Stores.Values.TryGetValueByIndex(index, out DicValue value))
            {
                ShowCheckError($"变量不存在:{index:D3}");
                return;
            }
            checkIndexSet.Add(index);
            AddCheckRow(index, value);
            ShowCheckSuccess("添加成功");
            SaveDebugConfig();
        }

        private void btnCheckRemove_Click(object sender, EventArgs e)
        {
            if (dgvCheck.CurrentRow == null)
            {
                ShowCheckError("未选择复选框调试项");
                return;
            }
            if (!TryGetCheckRowIndex(dgvCheck.CurrentRow, out int index, out string error))
            {
                ShowCheckError(error);
                return;
            }
            if (MessageBox.Show("确认移除该调试项？", "移除确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }
            checkIndexSet.Remove(index);
            dgvCheck.Rows.Remove(dgvCheck.CurrentRow);
            RemoveNoteIfUnused(index);
            ShowCheckSuccess("移除成功");
            SaveDebugConfig();
        }

        private void btnCheckRefresh_Click(object sender, EventArgs e)
        {
            RefreshCheckList();
        }

        private void dgvCheck_CurrentCellDirtyStateChanged(object sender, EventArgs e)
        {
            if (dgvCheck.IsCurrentCellDirty)
            {
                dgvCheck.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        }

        private void dgvCheck_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (isSyncing)
            {
                return;
            }
            if (e.RowIndex < 0 || e.ColumnIndex != colCheckValue.Index)
            {
                return;
            }
            DataGridViewRow row = dgvCheck.Rows[e.RowIndex];
            if (!TryGetCheckRowIdentity(row, out ValueDebugRowIdentity identity, out string error))
            {
                ShowCheckError(error);
                return;
            }
            int index = identity.Index;
            bool isChecked = false;
            if (row.Cells[colCheckValue.Index].Value is bool checkedValue)
            {
                isChecked = checkedValue;
            }
            string newValue = isChecked ? "1" : "0";
            if (!TryApplyValue(identity, newValue, "变量调试-复选框列表", out string applyError))
            {
                ShowCheckError(applyError);
                RefreshCheckRow(row, index);
                return;
            }
            ShowCheckSuccess("修改成功");
        }

        private void dgvCheck_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (isSyncing)
            {
                return;
            }
            if (e.RowIndex < 0 || e.ColumnIndex != colCheckNote.Index)
            {
                return;
            }
            DataGridViewRow row = dgvCheck.Rows[e.RowIndex];
            if (!TryGetCheckRowIndex(row, out int index, out string error))
            {
                ShowCheckError(error);
                return;
            }
            string newNote = row.Cells[colCheckNote.Index].Value?.ToString() ?? string.Empty;
            if (!TryUpdateNote(index, newNote, out string noteError))
            {
                ShowCheckError(noteError);
                RefreshCheckRow(row, index);
                return;
            }
            SyncNoteToOtherList(index, newNote, dgvEdit, colEditNote, colEditIndex);
            ShowCheckSuccess("备注修改成功");
        }

        private void dgvEdit_SelectionChanged(object sender, EventArgs e)
        {
            if (isSyncing)
            {
                return;
            }
            UpdateEditSelection();
        }

        private void btnEditAdd_Click(object sender, EventArgs e)
        {
            if (!TryGetSelectedIndex(cboEditVar, out int index, out string error))
            {
                ShowEditError(error);
                return;
            }
            if (editIndexSet.Contains(index))
            {
                ShowEditError("变量已在编辑框调试列表中");
                return;
            }
            if (Workspace.Runtime.Stores.Values == null || !Workspace.Runtime.Stores.Values.TryGetValueByIndex(index, out DicValue value))
            {
                ShowEditError($"变量不存在:{index:D3}");
                return;
            }
            editIndexSet.Add(index);
            AddEditRow(index, value);
            ShowEditSuccess("添加成功");
            SaveDebugConfig();
        }

        private void btnEditRemove_Click(object sender, EventArgs e)
        {
            if (dgvEdit.CurrentRow == null)
            {
                ShowEditError("未选择编辑框调试项");
                return;
            }
            if (!TryGetEditRowIndex(dgvEdit.CurrentRow, out int index, out string error))
            {
                ShowEditError(error);
                return;
            }
            if (MessageBox.Show("确认移除该调试项？", "移除确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }
            editIndexSet.Remove(index);
            dgvEdit.Rows.Remove(dgvEdit.CurrentRow);
            RemoveNoteIfUnused(index);
            UpdateEditSelection();
            ShowEditSuccess("移除成功");
            SaveDebugConfig();
        }

        private void btnEditRefresh_Click(object sender, EventArgs e)
        {
            RefreshEditList();
        }

        private void dgvEdit_EditingControlShowing(
            object sender,
            DataGridViewEditingControlShowingEventArgs e)
        {
            if (e.Control is TextBox editor)
            {
                editor.KeyDown -= dgvEditValueEditor_KeyDown;
                editor.KeyDown += dgvEditValueEditor_KeyDown;
            }
        }

        private void dgvEditValueEditor_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter
                || dgvEdit.CurrentCell == null
                || dgvEdit.CurrentCell.ColumnIndex != colEditValue.Index)
            {
                return;
            }
            e.Handled = true;
            e.SuppressKeyPress = true;
            DataGridViewRow row = dgvEdit.CurrentRow;
            if (!TryGetEditRowIdentity(
                row,
                out ValueDebugRowIdentity identity,
                out string error))
            {
                ShowEditError(error);
                return;
            }
            int index = identity.Index;
            string newValue = (sender as TextBox)?.Text ?? string.Empty;
            if (!TryApplyValue(
                identity,
                newValue,
                "变量调试-当前值回车",
                out string applyError))
            {
                ShowEditError(applyError);
                dgvEdit.CancelEdit();
                RefreshEditRow(row, index);
                return;
            }
            dgvEdit.EndEdit();
            row.Cells[colEditValue.Index].Value = newValue;
            ShowEditSuccess("修改成功");
        }

        private void UpdateEditSelection()
        {
            if (dgvEdit.CurrentRow == null)
            {
                lblEditStatus.Text = "未选择变量";
                lblEditStatus.ForeColor = UiPalette.TextMuted;
                return;
            }
            if (!TryGetEditRowIndex(dgvEdit.CurrentRow, out int index, out _))
            {
                return;
            }
            if (Workspace.Runtime.Stores.Values == null || !Workspace.Runtime.Stores.Values.TryGetValueByIndex(index, out DicValue value))
            {
                return;
            }
            lblEditStatus.Text = "在当前值单元格中输入，按 Enter 生效";
            lblEditStatus.ForeColor = UiPalette.TextMuted;
        }

        private void AddCheckRow(int index, DicValue value)
        {
            bool wasSyncing = isSyncing;
            isSyncing = true;
            int rowIndex = dgvCheck.Rows.Add();
            DataGridViewRow row = dgvCheck.Rows[rowIndex];
            PopulateCheckRow(row, index, value);
            isSyncing = wasSyncing;
        }

        private void AddEditRow(int index, DicValue value)
        {
            int rowIndex = dgvEdit.Rows.Add();
            DataGridViewRow row = dgvEdit.Rows[rowIndex];
            PopulateEditRow(row, index, value);
        }

        private void ReconcileRows(
            DataGridView grid,
            IEnumerable<int> configuredIndexes,
            Action<DataGridViewRow, int, DicValue> populate)
        {
            int selectedIndex = TryReadRowIndex(grid.CurrentRow, out int current)
                ? current
                : -1;
            int firstDisplayedIndex = grid.FirstDisplayedScrollingRowIndex;
            var existing = grid.Rows.Cast<DataGridViewRow>()
                .Where(row => TryReadRowIndex(row, out _))
                .GroupBy(row =>
                {
                    TryReadRowIndex(row, out int index);
                    return index;
                })
                .ToDictionary(group => group.Key, group => group.First());
            var retained = new HashSet<DataGridViewRow>();
            int targetRowIndex = 0;
            foreach (int index in configuredIndexes.OrderBy(value => value))
            {
                if (!Workspace.Runtime.Stores.Values.TryGetValueByIndex(
                    index,
                    out DicValue value))
                {
                    continue;
                }
                if (!existing.TryGetValue(index, out DataGridViewRow row))
                {
                    grid.Rows.Insert(targetRowIndex, 1);
                    row = grid.Rows[targetRowIndex];
                }
                else if (row.Index != targetRowIndex)
                {
                    grid.Rows.Remove(row);
                    grid.Rows.Insert(targetRowIndex, row);
                }
                retained.Add(row);
                populate(row, index, value);
                targetRowIndex++;
            }
            foreach (DataGridViewRow stale in grid.Rows.Cast<DataGridViewRow>()
                .Where(row => !retained.Contains(row)).ToList())
            {
                grid.Rows.Remove(stale);
            }
            if (selectedIndex >= 0
                && existing.TryGetValue(selectedIndex, out DataGridViewRow selected)
                && retained.Contains(selected))
            {
                grid.CurrentCell = selected.Cells
                    .Cast<DataGridViewCell>()
                    .FirstOrDefault(cell => cell.Visible);
            }
            if (grid.Rows.Count > 0 && firstDisplayedIndex >= 0)
            {
                grid.FirstDisplayedScrollingRowIndex = Math.Min(
                    firstDisplayedIndex,
                    grid.Rows.Count - 1);
            }
        }

        private static bool TryReadRowIndex(
            DataGridViewRow row,
            out int index)
        {
            if (row?.Tag is ValueDebugRowIdentity identity)
            {
                index = identity.Index;
                return true;
            }
            index = -1;
            return false;
        }

        private void PopulateCheckRow(
            DataGridViewRow row,
            int index,
            DicValue value)
        {
            row.Tag = new ValueDebugRowIdentity
            {
                Index = index,
                VariableId = value?.Id ?? Guid.Empty
            };
            SetCellValue(row.Cells[colCheckNote.Index], GetDebugNote(index));
            SetCellValue(row.Cells[colCheckIndex.Index], index.ToString("D3"));
            SetCellValue(row.Cells[colCheckName.Index], value.Name);
            SetCellValue(row.Cells[colCheckType.Index], value.Type);
            SetCellValue(row.Cells[colCheckValue.Index], ResolveChecked(value));
        }

        private void PopulateEditRow(
            DataGridViewRow row,
            int index,
            DicValue value)
        {
            row.Tag = new ValueDebugRowIdentity
            {
                Index = index,
                VariableId = value?.Id ?? Guid.Empty
            };
            SetCellValue(row.Cells[colEditNote.Index], GetDebugNote(index));
            SetCellValue(row.Cells[colEditIndex.Index], index.ToString("D3"));
            SetCellValue(row.Cells[colEditName.Index], value.Name);
            SetCellValue(row.Cells[colEditType.Index], value.Type);
            SetCellValue(row.Cells[colEditValue.Index], value.Value);
        }

        private static void SetCellValue(
            DataGridViewCell cell,
            object value)
        {
            if (!Equals(cell.Value, value))
            {
                cell.Value = value;
            }
        }

        private void RefreshCheckRow(DataGridViewRow row, int index)
        {
            if (Workspace.Runtime.Stores.Values == null || row == null)
            {
                return;
            }
            if (!Workspace.Runtime.Stores.Values.TryGetValueByIndex(index, out DicValue value))
            {
                return;
            }
            isSyncing = true;
            row.Tag = new ValueDebugRowIdentity
            {
                Index = index,
                VariableId = value.Id
            };
            row.Cells[colCheckNote.Index].Value = GetDebugNote(index);
            row.Cells[colCheckType.Index].Value = value.Type;
            row.Cells[colCheckValue.Index].Value = ResolveChecked(value);
            isSyncing = false;
        }

        private void RefreshEditRow(DataGridViewRow row, int index)
        {
            if (Workspace.Runtime.Stores.Values == null || row == null)
            {
                return;
            }
            if (!Workspace.Runtime.Stores.Values.TryGetValueByIndex(index, out DicValue value))
            {
                return;
            }
            isSyncing = true;
            row.Tag = new ValueDebugRowIdentity
            {
                Index = index,
                VariableId = value.Id
            };
            row.Cells[colEditNote.Index].Value = GetDebugNote(index);
            row.Cells[colEditType.Index].Value = value.Type;
            row.Cells[colEditValue.Index].Value = value.Value;
            isSyncing = false;
        }

        private void dgvEdit_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (isSyncing)
            {
                return;
            }
            if (e.RowIndex < 0)
            {
                return;
            }
            DataGridViewRow row = dgvEdit.Rows[e.RowIndex];
            if (!TryGetEditRowIndex(row, out int index, out string error))
            {
                ShowEditError(error);
                return;
            }
            if (e.ColumnIndex == colEditValue.Index)
            {
                // 当前值只有按 Enter 才会写入；其他结束编辑方式恢复运行时值。
                RefreshEditRow(row, index);
                return;
            }
            if (e.ColumnIndex != colEditNote.Index)
            {
                return;
            }
            string newNote = row.Cells[colEditNote.Index].Value?.ToString() ?? string.Empty;
            if (!TryUpdateNote(index, newNote, out string noteError))
            {
                ShowEditError(noteError);
                RefreshEditRow(row, index);
                return;
            }
            SyncNoteToOtherList(index, newNote, dgvCheck, colCheckNote, colCheckIndex);
            ShowEditSuccess("备注修改成功");
        }

        private string GetDebugNote(int index)
        {
            if (debugNotes.TryGetValue(index, out string note))
            {
                return note ?? string.Empty;
            }
            return string.Empty;
        }

        private void RefreshValueOptions()
        {
            if (Workspace.Runtime.Stores.Values == null)
            {
                return;
            }
            EnsureDebugConfigLoaded();
            int checkSelected = GetSelectedIndex(cboCheckVar);
            int editSelected = GetSelectedIndex(cboEditVar);
            List<ValueOption> options = BuildValueOptions();
            if (HasSameValueOptions(options))
            {
                return;
            }
            BindOptions(cboCheckVar, options, checkSelected);
            BindOptions(cboEditVar, options, editSelected);
            valueOptions = options;
        }

        private void RefreshValueOptionsIfChanged()
        {
            long version = Workspace.Runtime.Stores.Values.ConfigurationVersion;
            if (hasRendered && renderedValueConfigurationVersion == version)
            {
                return;
            }
            RefreshValueOptions();
            renderedValueConfigurationVersion = version;
        }

        private bool HasSameValueOptions(List<ValueOption> options)
        {
            if (options == null || valueOptions.Count != options.Count)
            {
                return false;
            }
            for (int i = 0; i < options.Count; i++)
            {
                ValueOption current = valueOptions[i];
                ValueOption next = options[i];
                if (current.Index != next.Index
                    || !string.Equals(current.Name, next.Name, StringComparison.Ordinal)
                    || !string.Equals(current.Type, next.Type, StringComparison.Ordinal))
                {
                    return false;
                }
            }
            return true;
        }

        private void EnsureDebugConfigLoaded()
        {
            long storeVersion = Workspace.Runtime.Stores.ValueDebug.Version;
            if (isConfigLoaded
                && loadedDebugConfigurationVersion == storeVersion)
            {
                return;
            }
            if (Workspace.Runtime.Stores.Values == null)
            {
                return;
            }
            try
            {
                if (!isConfigLoaded
                    && !Workspace.Runtime.Stores.ValueDebug.Load(
                        Workspace.Runtime.Paths.ConfigPath,
                        Workspace.Runtime.Stores.Values,
                        out string error))
                {
                    checkIndexSet.Clear();
                    editIndexSet.Clear();
                    debugNotes.Clear();
                    LogError($"变量调试配置无效:{error}");
                    isConfigLoaded = true;
                    loadedDebugConfigurationVersion =
                        Workspace.Runtime.Stores.ValueDebug.Version;
                    return;
                }
                ValueDebugConfiguration config = Workspace.Runtime.Stores.ValueDebug.Current;
                checkIndexSet.Clear();
                editIndexSet.Clear();
                debugNotes.Clear();
                foreach (int index in config.CheckIndexes)
                {
                    checkIndexSet.Add(index);
                }
                foreach (int index in config.EditIndexes)
                {
                    editIndexSet.Add(index);
                }
                foreach (var noteItem in config.Notes)
                {
                    debugNotes[noteItem.Key] = noteItem.Value ?? string.Empty;
                }
                isConfigLoaded = true;
                loadedDebugConfigurationVersion =
                    Workspace.Runtime.Stores.ValueDebug.Version;
            }
            catch (Exception ex)
            {
                checkIndexSet.Clear();
                editIndexSet.Clear();
                debugNotes.Clear();
                LogError($"变量调试配置加载失败:{ex.Message}");
                isConfigLoaded = true;
                loadedDebugConfigurationVersion =
                    Workspace.Runtime.Stores.ValueDebug.Version;
            }
        }

        private void SaveDebugConfig()
        {
            try
            {
                List<int> checkList = new List<int>(checkIndexSet);
                List<int> editList = new List<int>(editIndexSet);
                checkList.Sort();
                editList.Sort();
                CleanNotes(checkList, editList);
                Dictionary<int, string> noteSnapshot = new Dictionary<int, string>(debugNotes);
                ValueDebugConfiguration config = new ValueDebugConfiguration
                {
                    CheckIndexes = checkList,
                    EditIndexes = editList,
                    Notes = noteSnapshot
                };
                if (!Workspace.Runtime.Stores.ValueDebug.TryCommit(
                    Workspace.Runtime.Paths.ConfigPath,
                    config,
                    Workspace.Runtime.Stores.Values,
                    out string error))
                {
                    LogError(error);
                }
            }
            catch (Exception ex)
            {
                LogError($"变量调试配置保存失败:{ex.Message}");
            }
        }

        private List<ValueOption> BuildValueOptions()
        {
            List<ValueOption> options = new List<ValueOption>();
            for (int i = 0; i < ValueConfigStore.ValueCapacity; i++)
            {
                if (!Workspace.Runtime.Stores.Values.TryGetValueByIndex(i, out DicValue value))
                {
                    continue;
                }
                options.Add(new ValueOption
                {
                    Index = i,
                    Name = value.Name,
                    Type = value.Type
                });
            }
            return options;
        }

        private static void BindOptions(ComboBox combo, List<ValueOption> options, int selectedIndex)
        {
            combo.BeginUpdate();
            combo.DataSource = null;
            List<ValueOption> bound = new List<ValueOption>(options);
            combo.DataSource = bound;
            bool hasSelected = false;
            if (selectedIndex >= 0)
            {
                for (int i = 0; i < bound.Count; i++)
                {
                    if (bound[i].Index == selectedIndex)
                    {
                        combo.SelectedIndex = i;
                        hasSelected = true;
                        break;
                    }
                }
            }
            if (!hasSelected)
            {
                combo.SelectedIndex = -1;
            }
            combo.EndUpdate();
        }

        private static int GetSelectedIndex(ComboBox combo)
        {
            if (combo.SelectedItem is ValueOption option)
            {
                return option.Index;
            }
            return -1;
        }

        private static bool TryGetSelectedIndex(ComboBox combo, out int index, out string error)
        {
            index = -1;
            error = null;
            if (combo.SelectedItem is ValueOption option)
            {
                index = option.Index;
                return true;
            }
            error = "请选择变量";
            return false;
        }

        private bool TryApplyValue(
            ValueDebugRowIdentity identity,
            string newValue,
            string source,
            out string error)
        {
            error = null;
            if (identity == null)
            {
                error = "变量调试项身份无效";
                return false;
            }
            int index = identity.Index;
            if (Workspace.Runtime.Stores.Values == null)
            {
                error = "变量库未初始化";
                return false;
            }
            if (!Workspace.Runtime.Stores.Values.TryGetValueByIndex(index, out DicValue value))
            {
                error = $"变量不存在:{index:D3}";
                return false;
            }
            if (identity.VariableId == Guid.Empty || value.Id != identity.VariableId)
            {
                error = $"变量[{index:D3}]已被替换，请刷新后重试";
                return false;
            }
            if (!TryValidateValue(value, newValue, out error))
            {
                return false;
            }
            string oldValue = value.Value;
            if (!Workspace.Runtime.Stores.Values.TryModifyValueByIndex(
                index,
                value,
                _ => newValue,
                out string updateError,
                source))
            {
                error = string.IsNullOrWhiteSpace(updateError) ? "变量写入失败" : updateError;
                return false;
            }
            string message = $"变量调试修改成功：[{index:D3}] {value.Name} {oldValue} -> {newValue}";
            if (Workspace.Runtime.ProcessEngine?.Logger != null)
            {
                Workspace.Runtime.ProcessEngine.Logger.Log(message, LogLevel.Normal);
            }
            else if (Workspace.Info != null && !Workspace.Info.IsDisposed)
            {
                Workspace.Info.PrintInfo(message, FrmInfo.Level.Normal);
            }
            return true;
        }

        private bool TryUpdateNote(int index, string newNote, out string error)
        {
            error = null;
            if (Workspace.Runtime.Stores.Values == null)
            {
                error = "变量库未初始化";
                return false;
            }
            if (!Workspace.Runtime.Stores.Values.TryGetValueByIndex(index, out DicValue value))
            {
                error = $"变量不存在:{index:D3}";
                return false;
            }
            string oldNote = GetDebugNote(index);
            string nextNote = newNote ?? string.Empty;
            if (string.Equals(oldNote, nextNote, StringComparison.Ordinal))
            {
                return true;
            }
            if (string.IsNullOrEmpty(nextNote))
            {
                debugNotes.Remove(index);
            }
            else
            {
                debugNotes[index] = nextNote;
            }
            SaveDebugConfig();
            string message = $"变量调试备注修改成功：[{index:D3}] {value.Name} {oldNote} -> {nextNote}";
            if (Workspace.Runtime.ProcessEngine?.Logger != null)
            {
                Workspace.Runtime.ProcessEngine.Logger.Log(message, LogLevel.Normal);
            }
            else if (Workspace.Info != null && !Workspace.Info.IsDisposed)
            {
                Workspace.Info.PrintInfo(message, FrmInfo.Level.Normal);
            }
            return true;
        }

        private static void SyncNoteToOtherList(int index, string newNote, DataGridView grid, DataGridViewTextBoxColumn noteColumn, DataGridViewTextBoxColumn indexColumn)
        {
            if (grid == null)
            {
                return;
            }
            for (int i = 0; i < grid.Rows.Count; i++)
            {
                DataGridViewRow row = grid.Rows[i];
                if (row == null)
                {
                    continue;
                }
                string text = row.Cells[indexColumn.Index].Value?.ToString();
                if (!int.TryParse(text, out int rowIndex))
                {
                    continue;
                }
                if (rowIndex != index)
                {
                    continue;
                }
                row.Cells[noteColumn.Index].Value = newNote;
                break;
            }
        }

        private void RemoveNoteIfUnused(int index)
        {
            if (checkIndexSet.Contains(index))
            {
                return;
            }
            if (editIndexSet.Contains(index))
            {
                return;
            }
            debugNotes.Remove(index);
        }

        private void CleanNotes(List<int> checkList, List<int> editList)
        {
            HashSet<int> allow = new HashSet<int>(checkList);
            foreach (int index in editList)
            {
                allow.Add(index);
            }
            List<int> toRemove = new List<int>();
            foreach (var item in debugNotes)
            {
                if (!allow.Contains(item.Key))
                {
                    toRemove.Add(item.Key);
                }
            }
            foreach (int index in toRemove)
            {
                debugNotes.Remove(index);
            }
        }

        private static bool ResolveChecked(DicValue value)
        {
            if (value == null)
            {
                return false;
            }
            if (string.Equals(value.Type, "double", StringComparison.OrdinalIgnoreCase))
            {
                if (double.TryParse(value.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
                {
                    return Math.Abs(number - 1D) < 0.0000001D;
                }
                return false;
            }
            return string.Equals(value.Value, "1", StringComparison.Ordinal);
        }

        private static bool TryValidateValue(DicValue value, string newValue, out string error)
        {
            error = null;
            if (value == null || string.IsNullOrEmpty(value.Name))
            {
                error = "变量不存在";
                return false;
            }
            if (newValue == null)
            {
                error = "值为空";
                return false;
            }
            if (string.Equals(value.Type, "double", StringComparison.OrdinalIgnoreCase))
            {
                if (!IsStrictNumber(newValue))
                {
                    error = "数值格式非法";
                    return false;
                }
                return true;
            }
            if (!string.Equals(value.Type, "string", StringComparison.OrdinalIgnoreCase))
            {
                error = $"变量类型无效:{value.Type}";
                return false;
            }
            return true;
        }

        private bool TryGetCheckRowIndex(DataGridViewRow row, out int index, out string error)
        {
            if (TryGetCheckRowIdentity(row, out ValueDebugRowIdentity identity, out error))
            {
                index = identity.Index;
                return true;
            }
            index = -1;
            return false;
        }

        private bool TryGetCheckRowIdentity(
            DataGridViewRow row,
            out ValueDebugRowIdentity identity,
            out string error)
        {
            identity = row?.Tag as ValueDebugRowIdentity;
            if (identity == null)
            {
                error = "变量调试项身份无效，请刷新后重试";
                return false;
            }
            error = null;
            return true;
        }

        private bool TryGetEditRowIndex(DataGridViewRow row, out int index, out string error)
        {
            if (TryGetEditRowIdentity(row, out ValueDebugRowIdentity identity, out error))
            {
                index = identity.Index;
                return true;
            }
            index = -1;
            return false;
        }

        private bool TryGetEditRowIdentity(
            DataGridViewRow row,
            out ValueDebugRowIdentity identity,
            out string error)
        {
            identity = row?.Tag as ValueDebugRowIdentity;
            if (identity == null)
            {
                error = "变量调试项身份无效，请刷新后重试";
                return false;
            }
            error = null;
            return true;
        }

        private static bool IsStrictDigits(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] < '0' || text[i] > '9')
                {
                    return false;
                }
            }
            return text.Length > 0;
        }

        private static bool IsStrictNumber(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }
            int dotCount = 0;
            int StartIndex = 0;
            if (text[0] == '-')
            {
                if (text.Length == 1)
                {
                    return false;
                }
                StartIndex = 1;
            }
            for (int i = StartIndex; i < text.Length; i++)
            {
                char ch = text[i];
                if (ch == '.')
                {
                    dotCount++;
                    if (dotCount > 1)
                    {
                        return false;
                    }
                    continue;
                }
                if (ch < '0' || ch > '9')
                {
                    return false;
                }
            }
            return true;
        }

        private void ShowCheckSuccess(string message)
        {
            lblCheckStatus.Text = message;
            lblCheckStatus.ForeColor = UiPalette.Success;
        }

        private void ShowCheckError(string message)
        {
            string logMessage = string.IsNullOrWhiteSpace(message) ? "复选框调试失败" : message;
            LogError($"复选框调试失败：{logMessage}");
            lblCheckStatus.Text = logMessage;
            lblCheckStatus.ForeColor = UiPalette.Danger;
        }

        private void ShowEditSuccess(string message)
        {
            lblEditStatus.Text = message;
            lblEditStatus.ForeColor = UiPalette.Success;
        }

        private void ShowEditError(string message)
        {
            string logMessage = string.IsNullOrWhiteSpace(message) ? "编辑框调试失败" : message;
            LogError($"编辑框调试失败：{logMessage}");
            lblEditStatus.Text = logMessage;
            lblEditStatus.ForeColor = UiPalette.Danger;
        }

        private void LogError(string message)
        {
            if (Workspace.Runtime.ProcessEngine?.Logger != null)
            {
                Workspace.Runtime.ProcessEngine.Logger.Log(message, LogLevel.Error);
            }
            else if (Workspace.Info != null && !Workspace.Info.IsDisposed)
            {
                Workspace.Info.PrintInfo(message, FrmInfo.Level.Error);
            }
        }
    }
}
