// 模块：编辑器 / 变量。
// 职责范围：变量配置、运行值调试、变量选择和提交规则。

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Automation
{
    public partial class FrmValueDebug : Form
    {
        private sealed class ValueOption
        {
            public Guid VariableId { get; set; }
            public int Index { get; set; }
            public string Name { get; set; }
            public string Type { get; set; }

            public override string ToString()
            {
                string label = string.IsNullOrWhiteSpace(Name) ? $"索引{Index:D3}" : Name;
                return $"{Index:D3}  {label}";
            }
        }

        private readonly HashSet<Guid> checkVariableIdSet = new HashSet<Guid>();
        private readonly HashSet<Guid> editVariableIdSet = new HashSet<Guid>();
        private readonly Dictionary<Guid, string> debugNotes =
            new Dictionary<Guid, string>();
        private readonly Timer runtimeValueRefreshTimer;
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
            if (components == null)
            {
                components = new System.ComponentModel.Container();
            }
            runtimeValueRefreshTimer = new Timer(components)
            {
                Interval = 200
            };
            runtimeValueRefreshTimer.Tick +=
                (sender, args) => RefreshDisplayedRuntimeValues();
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
            if (!Visible)
            {
                runtimeValueRefreshTimer?.Stop();
                return;
            }
            runtimeValueRefreshTimer?.Start();
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
            if (!EnsureDebugConfigLoaded(out string error))
            {
                ShowCheckError(error);
                ShowEditError(error);
                return;
            }
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
            if (!EnsureDebugConfigLoaded(out string error))
            {
                ShowCheckError(error);
                return;
            }
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
                    checkVariableIdSet,
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
            if (!EnsureDebugConfigLoaded(out string error))
            {
                ShowEditError(error);
                return;
            }
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
                    editVariableIdSet,
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
            if (!TryGetSelectedVariable(
                cboCheckVar,
                out Guid variableId,
                out string error))
            {
                ShowCheckError(error);
                return;
            }
            if (checkVariableIdSet.Contains(variableId))
            {
                ShowCheckError("变量已在复选框调试列表中");
                return;
            }
            if (!Workspace.Runtime.VariableDebug.TryGetValue(
                variableId,
                out _))
            {
                ShowCheckError("变量不存在或已删除");
                return;
            }
            var checkVariableIds = new HashSet<Guid>(checkVariableIdSet)
            {
                variableId
            };
            if (!TryCommitDebugConfiguration(
                CreateConfiguration(
                    checkVariableIds,
                    editVariableIdSet,
                    debugNotes),
                out error))
            {
                ShowCheckError(error);
                return;
            }
            RefreshCheckRows();
            ShowCheckSuccess("添加成功");
        }

        private void btnCheckRemove_Click(object sender, EventArgs e)
        {
            if (dgvCheck.CurrentRow == null)
            {
                ShowCheckError("未选择复选框调试项");
                return;
            }
            if (!TryGetCheckRowIdentity(
                dgvCheck.CurrentRow,
                out ValueDebugRowIdentity identity,
                out string error))
            {
                ShowCheckError(error);
                return;
            }
            if (MessageBox.Show("确认移除该调试项？", "移除确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }
            var checkVariableIds = new HashSet<Guid>(checkVariableIdSet);
            checkVariableIds.Remove(identity.VariableId);
            if (!TryCommitDebugConfiguration(
                CreateConfiguration(
                    checkVariableIds,
                    editVariableIdSet,
                    debugNotes),
                out error))
            {
                ShowCheckError(error);
                return;
            }
            RefreshCheckRows();
            ShowCheckSuccess("移除成功");
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
            bool isChecked = false;
            if (row.Cells[colCheckValue.Index].Value is bool checkedValue)
            {
                isChecked = checkedValue;
            }
            string newValue = isChecked ? "1" : "0";
            if (!TryApplyValue(identity, newValue, "变量调试-复选框列表", out string applyError))
            {
                ShowCheckError(applyError);
                RefreshCheckRow(row, identity.VariableId);
                return;
            }
            RefreshCheckRow(row, identity.VariableId);
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
            if (!TryGetCheckRowIdentity(
                row,
                out ValueDebugRowIdentity identity,
                out string error))
            {
                ShowCheckError(error);
                return;
            }
            string newNote = row.Cells[colCheckNote.Index].Value?.ToString() ?? string.Empty;
            if (!TryUpdateNote(
                identity.VariableId,
                newNote,
                out string noteError))
            {
                ShowCheckError(noteError);
                RefreshCheckRow(row, identity.VariableId);
                return;
            }
            RefreshCheckRows();
            RefreshEditRows();
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
            if (!TryGetSelectedVariable(
                cboEditVar,
                out Guid variableId,
                out string error))
            {
                ShowEditError(error);
                return;
            }
            if (editVariableIdSet.Contains(variableId))
            {
                ShowEditError("变量已在编辑框调试列表中");
                return;
            }
            if (!Workspace.Runtime.VariableDebug.TryGetValue(
                variableId,
                out _))
            {
                ShowEditError("变量不存在或已删除");
                return;
            }
            var editVariableIds = new HashSet<Guid>(editVariableIdSet)
            {
                variableId
            };
            if (!TryCommitDebugConfiguration(
                CreateConfiguration(
                    checkVariableIdSet,
                    editVariableIds,
                    debugNotes),
                out error))
            {
                ShowEditError(error);
                return;
            }
            RefreshEditRows();
            ShowEditSuccess("添加成功");
        }

        private void btnEditRemove_Click(object sender, EventArgs e)
        {
            if (dgvEdit.CurrentRow == null)
            {
                ShowEditError("未选择编辑框调试项");
                return;
            }
            if (!TryGetEditRowIdentity(
                dgvEdit.CurrentRow,
                out ValueDebugRowIdentity identity,
                out string error))
            {
                ShowEditError(error);
                return;
            }
            if (MessageBox.Show("确认移除该调试项？", "移除确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }
            var editVariableIds = new HashSet<Guid>(editVariableIdSet);
            editVariableIds.Remove(identity.VariableId);
            if (!TryCommitDebugConfiguration(
                CreateConfiguration(
                    checkVariableIdSet,
                    editVariableIds,
                    debugNotes),
                out error))
            {
                ShowEditError(error);
                return;
            }
            RefreshEditRows();
            ShowEditSuccess("移除成功");
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
            string newValue = (sender as TextBox)?.Text ?? string.Empty;
            if (!TryApplyValue(
                identity,
                newValue,
                "变量调试-当前值回车",
                out string applyError))
            {
                ShowEditError(applyError);
                dgvEdit.CancelEdit();
                RefreshEditRow(row, identity.VariableId);
                return;
            }
            dgvEdit.EndEdit();
            RefreshEditRow(row, identity.VariableId);
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
            if (!TryGetEditRowIdentity(
                dgvEdit.CurrentRow,
                out ValueDebugRowIdentity identity,
                out _))
            {
                return;
            }
            if (!Workspace.Runtime.VariableDebug.TryGetValue(
                identity.VariableId,
                out _))
            {
                lblEditStatus.Text = "变量已删除，可移除该调试项";
                lblEditStatus.ForeColor = UiPalette.Danger;
                return;
            }
            lblEditStatus.Text = "在当前值单元格中输入，按 Enter 生效";
            lblEditStatus.ForeColor = UiPalette.TextMuted;
        }

        private void ReconcileRows(
            DataGridView grid,
            IEnumerable<Guid> configuredVariableIds,
            Action<DataGridViewRow, Guid, DicValue> populate)
        {
            Guid selectedVariableId =
                TryReadRowVariableId(grid.CurrentRow, out Guid current)
                    ? current
                    : Guid.Empty;
            int firstDisplayedIndex = grid.FirstDisplayedScrollingRowIndex;
            var existing = grid.Rows.Cast<DataGridViewRow>()
                .Where(row => TryReadRowVariableId(row, out _))
                .GroupBy(row =>
                {
                    TryReadRowVariableId(row, out Guid variableId);
                    return variableId;
                })
                .ToDictionary(group => group.Key, group => group.First());
            Dictionary<Guid, DicValue> valuesById =
                Workspace.Runtime.VariableDebug.GetVariablesSnapshot()
                    .Where(value => value != null
                        && value.Id != Guid.Empty)
                    .ToDictionary(value => value.Id, value => value);
            var retained = new HashSet<DataGridViewRow>();
            int targetRowIndex = 0;
            var configured = (configuredVariableIds ?? Enumerable.Empty<Guid>())
                .Select(variableId =>
                {
                    valuesById.TryGetValue(
                        variableId,
                        out DicValue value);
                    return new
                    {
                        VariableId = variableId,
                        Value = value
                    };
                })
                .OrderBy(item => item.Value == null ? 1 : 0)
                .ThenBy(item => item.Value?.Index ?? int.MaxValue)
                .ThenBy(item => item.VariableId)
                .ToList();
            foreach (var item in configured)
            {
                if (!existing.TryGetValue(
                    item.VariableId,
                    out DataGridViewRow row))
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
                populate(row, item.VariableId, item.Value);
                targetRowIndex++;
            }
            foreach (DataGridViewRow stale in grid.Rows.Cast<DataGridViewRow>()
                .Where(row => !retained.Contains(row)).ToList())
            {
                grid.Rows.Remove(stale);
            }
            if (selectedVariableId != Guid.Empty
                && existing.TryGetValue(
                    selectedVariableId,
                    out DataGridViewRow selected)
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

        private static bool TryReadRowVariableId(
            DataGridViewRow row,
            out Guid variableId)
        {
            if (row?.Tag is ValueDebugRowIdentity identity
                && identity.VariableId != Guid.Empty)
            {
                variableId = identity.VariableId;
                return true;
            }
            variableId = Guid.Empty;
            return false;
        }

        private void PopulateCheckRow(
            DataGridViewRow row,
            Guid variableId,
            DicValue value)
        {
            row.Tag = new ValueDebugRowIdentity
            {
                Index = value?.Index ?? -1,
                VariableId = variableId
            };
            SetCellValue(
                row.Cells[colCheckNote.Index],
                GetDebugNote(variableId));
            SetCellValue(
                row.Cells[colCheckIndex.Index],
                value == null ? string.Empty : value.Index.ToString("D3"));
            SetCellValue(
                row.Cells[colCheckName.Index],
                value?.Name ?? $"已删除变量 ({ShortVariableId(variableId)})");
            SetCellValue(
                row.Cells[colCheckType.Index],
                value?.Type ?? "失效");
            SetCellValue(row.Cells[colCheckValue.Index], ResolveChecked(value));
            row.Cells[colCheckValue.Index].ReadOnly = value == null;
        }

        private void PopulateEditRow(
            DataGridViewRow row,
            Guid variableId,
            DicValue value)
        {
            row.Tag = new ValueDebugRowIdentity
            {
                Index = value?.Index ?? -1,
                VariableId = variableId
            };
            SetCellValue(
                row.Cells[colEditNote.Index],
                GetDebugNote(variableId));
            SetCellValue(
                row.Cells[colEditIndex.Index],
                value == null ? string.Empty : value.Index.ToString("D3"));
            SetCellValue(
                row.Cells[colEditName.Index],
                value?.Name ?? $"已删除变量 ({ShortVariableId(variableId)})");
            SetCellValue(
                row.Cells[colEditType.Index],
                value?.Type ?? "失效");
            SetCellValue(
                row.Cells[colEditValue.Index],
                value?.Value ?? string.Empty);
            row.Cells[colEditValue.Index].ReadOnly = value == null;
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

        private void RefreshCheckRow(DataGridViewRow row, Guid variableId)
        {
            if (Workspace.Runtime.Stores.Values == null || row == null)
            {
                return;
            }
            Workspace.Runtime.VariableDebug.TryGetValue(
                variableId,
                out DicValue value);
            bool wasSyncing = isSyncing;
            isSyncing = true;
            try
            {
                PopulateCheckRow(row, variableId, value);
            }
            finally
            {
                isSyncing = wasSyncing;
            }
        }

        private void RefreshEditRow(DataGridViewRow row, Guid variableId)
        {
            if (Workspace.Runtime.Stores.Values == null || row == null)
            {
                return;
            }
            Workspace.Runtime.VariableDebug.TryGetValue(
                variableId,
                out DicValue value);
            bool wasSyncing = isSyncing;
            isSyncing = true;
            try
            {
                PopulateEditRow(row, variableId, value);
            }
            finally
            {
                isSyncing = wasSyncing;
            }
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
            if (!TryGetEditRowIdentity(
                row,
                out ValueDebugRowIdentity identity,
                out string error))
            {
                ShowEditError(error);
                return;
            }
            if (e.ColumnIndex == colEditValue.Index)
            {
                // 当前值只有按 Enter 才会写入；其他结束编辑方式恢复运行时值。
                RefreshEditRow(row, identity.VariableId);
                return;
            }
            if (e.ColumnIndex != colEditNote.Index)
            {
                return;
            }
            string newNote = row.Cells[colEditNote.Index].Value?.ToString() ?? string.Empty;
            if (!TryUpdateNote(
                identity.VariableId,
                newNote,
                out string noteError))
            {
                ShowEditError(noteError);
                RefreshEditRow(row, identity.VariableId);
                return;
            }
            RefreshCheckRows();
            RefreshEditRows();
            ShowEditSuccess("备注修改成功");
        }

        private string GetDebugNote(Guid variableId)
        {
            if (debugNotes.TryGetValue(variableId, out string note))
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
            Guid checkSelected = GetSelectedVariableId(cboCheckVar);
            Guid editSelected = GetSelectedVariableId(cboEditVar);
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
                if (current.VariableId != next.VariableId
                    || current.Index != next.Index
                    || !string.Equals(current.Name, next.Name, StringComparison.Ordinal)
                    || !string.Equals(current.Type, next.Type, StringComparison.Ordinal))
                {
                    return false;
                }
            }
            return true;
        }

        private bool EnsureDebugConfigLoaded(out string error)
        {
            error = null;
            long storeVersion =
                Workspace.Runtime.VariableDebug.ConfigurationVersion;
            if (isConfigLoaded
                && loadedDebugConfigurationVersion == storeVersion)
            {
                return true;
            }
            if (Workspace.Runtime.Stores.Values == null)
            {
                error = "变量库未初始化。";
                return false;
            }
            try
            {
                ValueDebugConfiguration configuration;
                long version;
                if (!isConfigLoaded)
                {
                    if (!Workspace.Runtime.VariableDebug.TryLoadConfiguration(
                        out configuration,
                        out version,
                        out error))
                    {
                        LogError($"变量调试配置加载失败:{error}");
                        return false;
                    }
                }
                else
                {
                    configuration =
                        Workspace.Runtime.VariableDebug.GetConfigurationSnapshot(
                            out version);
                }
                ApplyConfiguration(configuration, version);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                LogError($"变量调试配置加载失败:{ex.Message}");
                return false;
            }
        }

        private void ApplyConfiguration(
            ValueDebugConfiguration configuration,
            long version)
        {
            if (configuration == null)
            {
                throw new InvalidOperationException("变量调试配置为空。");
            }
            checkVariableIdSet.Clear();
            editVariableIdSet.Clear();
            debugNotes.Clear();
            foreach (Guid variableId in configuration.CheckVariableIds)
            {
                checkVariableIdSet.Add(variableId);
            }
            foreach (Guid variableId in configuration.EditVariableIds)
            {
                editVariableIdSet.Add(variableId);
            }
            foreach (KeyValuePair<Guid, string> note in configuration.Notes)
            {
                debugNotes[note.Key] = note.Value ?? string.Empty;
            }
            isConfigLoaded = true;
            loadedDebugConfigurationVersion = version;
        }

        private bool TryCommitDebugConfiguration(
            ValueDebugConfiguration candidate,
            out string error)
        {
            error = null;
            if (!isConfigLoaded)
            {
                error = "变量调试配置尚未加载，请刷新后重试。";
                return false;
            }
            if (!Workspace.Runtime.VariableDebug.TryCommitConfiguration(
                candidate,
                loadedDebugConfigurationVersion,
                out ValueDebugConfiguration committed,
                out long committedVersion,
                out error))
            {
                return false;
            }
            ApplyConfiguration(committed, committedVersion);
            return true;
        }

        private static ValueDebugConfiguration CreateConfiguration(
            IEnumerable<Guid> checkVariableIds,
            IEnumerable<Guid> editVariableIds,
            IReadOnlyDictionary<Guid, string> notes)
        {
            List<Guid> checkIds = (checkVariableIds ?? Enumerable.Empty<Guid>())
                .Distinct()
                .OrderBy(variableId => variableId)
                .ToList();
            List<Guid> editIds = (editVariableIds ?? Enumerable.Empty<Guid>())
                .Distinct()
                .OrderBy(variableId => variableId)
                .ToList();
            var allowedVariableIds = new HashSet<Guid>(checkIds);
            allowedVariableIds.UnionWith(editIds);
            Dictionary<Guid, string> noteSnapshot =
                (notes ?? new Dictionary<Guid, string>())
                .Where(item => allowedVariableIds.Contains(item.Key)
                    && !string.IsNullOrEmpty(item.Value))
                .ToDictionary(
                    item => item.Key,
                    item => item.Value);
            return new ValueDebugConfiguration
            {
                CheckVariableIds = checkIds,
                EditVariableIds = editIds,
                Notes = noteSnapshot
            };
        }

        private List<ValueOption> BuildValueOptions()
        {
            return Workspace.Runtime.VariableDebug.GetVariablesSnapshot()
                .Where(value => value != null && value.Id != Guid.Empty)
                .OrderBy(value => value.Index)
                .Select(value => new ValueOption
                {
                    VariableId = value.Id,
                    Index = value.Index,
                    Name = value.Name,
                    Type = value.Type
                })
                .ToList();
        }

        private static void BindOptions(
            ComboBox combo,
            List<ValueOption> options,
            Guid selectedVariableId)
        {
            combo.BeginUpdate();
            combo.DataSource = null;
            List<ValueOption> bound = new List<ValueOption>(options);
            combo.DataSource = bound;
            bool hasSelected = false;
            if (selectedVariableId != Guid.Empty)
            {
                for (int i = 0; i < bound.Count; i++)
                {
                    if (bound[i].VariableId == selectedVariableId)
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

        private static Guid GetSelectedVariableId(ComboBox combo)
        {
            if (combo.SelectedItem is ValueOption option)
            {
                return option.VariableId;
            }
            return Guid.Empty;
        }

        private static bool TryGetSelectedVariable(
            ComboBox combo,
            out Guid variableId,
            out string error)
        {
            variableId = Guid.Empty;
            error = null;
            if (combo.SelectedItem is ValueOption option
                && option.VariableId != Guid.Empty)
            {
                variableId = option.VariableId;
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
            return Workspace.Runtime.VariableDebug.TryApplyValue(
                identity.VariableId,
                newValue,
                source,
                out error);
        }

        private bool TryUpdateNote(
            Guid variableId,
            string newNote,
            out string error)
        {
            error = null;
            string oldNote = GetDebugNote(variableId);
            string nextNote = newNote ?? string.Empty;
            if (string.Equals(oldNote, nextNote, StringComparison.Ordinal))
            {
                return true;
            }
            var notes = new Dictionary<Guid, string>(debugNotes);
            if (string.IsNullOrEmpty(nextNote))
            {
                notes.Remove(variableId);
            }
            else
            {
                notes[variableId] = nextNote;
            }
            if (!TryCommitDebugConfiguration(
                CreateConfiguration(
                    checkVariableIdSet,
                    editVariableIdSet,
                    notes),
                out error))
            {
                return false;
            }
            Workspace.Runtime.VariableDebug.TryGetValue(
                variableId,
                out DicValue value);
            string target = value == null
                ? variableId.ToString("D")
                : $"[{value.Index:D3}] {value.Name}";
            string message =
                $"变量调试备注修改成功：{target} {oldNote} -> {nextNote}";
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

        private static bool ResolveChecked(DicValue value)
        {
            if (value == null)
            {
                return false;
            }
            if (string.Equals(value.Type, "double", StringComparison.OrdinalIgnoreCase))
            {
                if (double.TryParse(value.Value, out double number))
                {
                    return Math.Abs(number - 1D) < 0.0000001D;
                }
                return false;
            }
            return string.Equals(value.Value, "1", StringComparison.Ordinal);
        }

        private bool TryGetCheckRowIdentity(
            DataGridViewRow row,
            out ValueDebugRowIdentity identity,
            out string error)
        {
            identity = row?.Tag as ValueDebugRowIdentity;
            if (identity == null || identity.VariableId == Guid.Empty)
            {
                error = "变量调试项身份无效，请刷新后重试";
                return false;
            }
            error = null;
            return true;
        }

        private bool TryGetEditRowIdentity(
            DataGridViewRow row,
            out ValueDebugRowIdentity identity,
            out string error)
        {
            identity = row?.Tag as ValueDebugRowIdentity;
            if (identity == null || identity.VariableId == Guid.Empty)
            {
                error = "变量调试项身份无效，请刷新后重试";
                return false;
            }
            error = null;
            return true;
        }

        private static string ShortVariableId(Guid variableId)
        {
            string text = variableId.ToString("N");
            return text.Length <= 8 ? text : text.Substring(0, 8);
        }

        private void RefreshDisplayedRuntimeValues()
        {
            if (!Visible
                || !isConfigLoaded
                || Workspace.Runtime.Stores.Values == null)
            {
                return;
            }
            try
            {
                bool structureChanged =
                    loadedDebugConfigurationVersion
                        != Workspace.Runtime.VariableDebug.ConfigurationVersion
                    || renderedValueConfigurationVersion
                        != Workspace.Runtime.VariableDebug.ValueConfigurationVersion;
                if (structureChanged)
                {
                    if (dgvCheck.IsCurrentCellInEditMode
                        || dgvEdit.IsCurrentCellInEditMode)
                    {
                        return;
                    }
                    RefreshAllLists();
                    return;
                }

                int dirtyCheckRow = dgvCheck.IsCurrentCellDirty
                    && dgvCheck.CurrentCell != null
                        ? dgvCheck.CurrentCell.RowIndex
                        : -1;
                int editingValueRow = dgvEdit.IsCurrentCellInEditMode
                    && dgvEdit.CurrentCell != null
                    && dgvEdit.CurrentCell.ColumnIndex == colEditValue.Index
                        ? dgvEdit.CurrentCell.RowIndex
                        : -1;
                bool wasSyncing = isSyncing;
                isSyncing = true;
                try
                {
                    int checkRowIndex = dgvCheck.Rows.GetFirstRow(
                        DataGridViewElementStates.Displayed);
                    while (checkRowIndex >= 0)
                    {
                        DataGridViewRow row = dgvCheck.Rows[checkRowIndex];
                        if (row.Index == dirtyCheckRow
                            || !TryReadRowVariableId(
                                row,
                                out Guid variableId)
                            || !Workspace.Runtime.VariableDebug.TryGetValue(
                                variableId,
                                out DicValue value))
                        {
                            checkRowIndex = dgvCheck.Rows.GetNextRow(
                                checkRowIndex,
                                DataGridViewElementStates.Displayed);
                            continue;
                        }
                        SetCellValue(
                            row.Cells[colCheckValue.Index],
                            ResolveChecked(value));
                        checkRowIndex = dgvCheck.Rows.GetNextRow(
                            checkRowIndex,
                            DataGridViewElementStates.Displayed);
                    }
                    int editRowIndex = dgvEdit.Rows.GetFirstRow(
                        DataGridViewElementStates.Displayed);
                    while (editRowIndex >= 0)
                    {
                        DataGridViewRow row = dgvEdit.Rows[editRowIndex];
                        if (row.Index == editingValueRow
                            || !TryReadRowVariableId(
                                row,
                                out Guid variableId)
                            || !Workspace.Runtime.VariableDebug.TryGetValue(
                                variableId,
                                out DicValue value))
                        {
                            editRowIndex = dgvEdit.Rows.GetNextRow(
                                editRowIndex,
                                DataGridViewElementStates.Displayed);
                            continue;
                        }
                        SetCellValue(
                            row.Cells[colEditValue.Index],
                            value.Value);
                        editRowIndex = dgvEdit.Rows.GetNextRow(
                            editRowIndex,
                            DataGridViewElementStates.Displayed);
                    }
                }
                finally
                {
                    isSyncing = wasSyncing;
                }
            }
            catch (Exception ex)
            {
                LogError($"变量调试运行值刷新失败:{ex.Message}");
            }
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
