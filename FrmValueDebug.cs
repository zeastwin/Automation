using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;
using Newtonsoft.Json;

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

        private sealed class ValueDebugConfig
        {
            public List<int> CheckIndexes { get; set; }
            public List<int> EditIndexes { get; set; }
            public Dictionary<int, string> Notes { get; set; }
        }

        private readonly HashSet<int> checkIndexSet = new HashSet<int>();
        private readonly HashSet<int> editIndexSet = new HashSet<int>();
        private readonly Dictionary<int, string> debugNotes = new Dictionary<int, string>();
        private bool isSyncing;
        private bool isConfigLoaded;
        private const string DebugConfigFileName = "value_debug.json";

        public FrmValueDebug()
        {
            InitializeComponent();
            Font uiFont = new Font("黑体", 12F, FontStyle.Regular, GraphicsUnit.Point, ((byte)(134)));

            dgvCheck.Font = uiFont;
            dgvCheck.ColumnHeadersDefaultCellStyle.Font = new Font("黑体", 12F, FontStyle.Bold, GraphicsUnit.Point, ((byte)(134)));
            dgvCheck.RowHeadersVisible = false;
            dgvCheck.AutoGenerateColumns = false;
            dgvCheck.AllowUserToAddRows = false;
            dgvCheck.AllowUserToResizeRows = false;
            dgvCheck.MultiSelect = false;
            dgvCheck.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgvCheck.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dgvCheck.RowTemplate.Height = 28;
            dgvCheck.EditMode = DataGridViewEditMode.EditOnEnter;
            ApplyColumnTheme(groupCheck, panelCheckBottom, dgvCheck, Color.FromArgb(244, 248, 252), Color.FromArgb(233, 241, 247));

            dgvEdit.Font = uiFont;
            dgvEdit.ColumnHeadersDefaultCellStyle.Font = new Font("黑体", 12F, FontStyle.Bold, GraphicsUnit.Point, ((byte)(134)));
            dgvEdit.RowHeadersVisible = false;
            dgvEdit.AutoGenerateColumns = false;
            dgvEdit.AllowUserToAddRows = false;
            dgvEdit.AllowUserToResizeRows = false;
            dgvEdit.MultiSelect = false;
            dgvEdit.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgvEdit.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dgvEdit.RowTemplate.Height = 28;
            dgvEdit.ReadOnly = false;
            ApplyColumnTheme(groupEdit, panelEditBottom, dgvEdit, Color.FromArgb(244, 252, 248), Color.FromArgb(232, 246, 238));

            cboCheckVar.Font = uiFont;
            cboEditVar.Font = uiFont;
            cboCheckVar.DropDownStyle = ComboBoxStyle.DropDownList;
            cboEditVar.DropDownStyle = ComboBoxStyle.DropDownList;
            txtEditValue.Font = uiFont;
            btnCheckAdd.Font = uiFont;
            btnCheckRemove.Font = uiFont;
            btnCheckRefresh.Font = uiFont;
            btnEditAdd.Font = uiFont;
            btnEditRemove.Font = uiFont;
            btnEditApply.Font = uiFont;
            btnEditRefresh.Font = uiFont;
            lblCheckStatus.Font = uiFont;
            lblEditStatus.Font = uiFont;
            lblCheckIndex.Font = uiFont;
            lblEditIndex.Font = uiFont;
            lblEditValue.Font = uiFont;
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
                grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.Black;
                grid.DefaultCellStyle.BackColor = Color.FromArgb(250, 252, 255);
                grid.DefaultCellStyle.ForeColor = Color.Black;
                grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(240, 245, 250);
            }
        }

        private void FrmValueDebug_Load(object sender, EventArgs e)
        {
            RefreshCheckList();
            RefreshEditList();
        }

        public void RefreshCheckList()
        {
            if (SF.valueStore == null)
            {
                ShowCheckError("变量库未初始化");
                return;
            }
            EnsureDebugConfigLoaded();
            RefreshValueOptions();
            isSyncing = true;
            dgvCheck.Rows.Clear();
            foreach (int index in checkIndexSet)
            {
                if (!SF.valueStore.TryGetValueByIndex(index, out DicValue value))
                {
                    continue;
                }
                AddCheckRow(index, value);
            }
            isSyncing = false;
        }

        public void RefreshEditList()
        {
            if (SF.valueStore == null)
            {
                ShowEditError("变量库未初始化");
                return;
            }
            EnsureDebugConfigLoaded();
            RefreshValueOptions();
            isSyncing = true;
            dgvEdit.Rows.Clear();
            foreach (int index in editIndexSet)
            {
                if (!SF.valueStore.TryGetValueByIndex(index, out DicValue value))
                {
                    continue;
                }
                AddEditRow(index, value);
            }
            isSyncing = false;
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
            if (SF.valueStore == null || !SF.valueStore.TryGetValueByIndex(index, out DicValue value))
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
            if (!TryGetCheckRowIndex(row, out int index, out string error))
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
            if (!TryApplyValue(index, newValue, "变量调试-复选框列表", out string applyError))
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
            if (SF.valueStore == null || !SF.valueStore.TryGetValueByIndex(index, out DicValue value))
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
            editIndexSet.Remove(index);
            dgvEdit.Rows.Remove(dgvEdit.CurrentRow);
            RemoveNoteIfUnused(index);
            UpdateEditSelection();
            ShowEditSuccess("移除成功");
            SaveDebugConfig();
        }

        private void btnEditApply_Click(object sender, EventArgs e)
        {
            ApplyEditValue("变量调试-应用按钮");
        }

        private void btnEditRefresh_Click(object sender, EventArgs e)
        {
            RefreshEditList();
        }

        private void txtEditValue_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                ApplyEditValue("变量调试-回车");
            }
        }

        private void ApplyEditValue(string source)
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
            string newValue = txtEditValue.Text;
            if (!TryApplyValue(index, newValue, source, out string applyError))
            {
                ShowEditError(applyError);
                RefreshEditRow(dgvEdit.CurrentRow, index);
                return;
            }
            dgvEdit.CurrentRow.Cells[colEditValue.Index].Value = newValue;
            ShowEditSuccess("修改成功");
        }

        private void UpdateEditSelection()
        {
            if (dgvEdit.CurrentRow == null)
            {
                txtEditValue.Text = string.Empty;
                lblEditStatus.Text = "未选择变量";
                lblEditStatus.ForeColor = Color.DimGray;
                return;
            }
            if (!TryGetEditRowIndex(dgvEdit.CurrentRow, out int index, out _))
            {
                txtEditValue.Text = string.Empty;
                return;
            }
            if (SF.valueStore == null || !SF.valueStore.TryGetValueByIndex(index, out DicValue value))
            {
                txtEditValue.Text = string.Empty;
                return;
            }
            isSyncing = true;
            txtEditValue.Text = value.Value;
            isSyncing = false;
            lblEditStatus.Text = "已选择变量";
            lblEditStatus.ForeColor = Color.DimGray;
        }

        private void AddCheckRow(int index, DicValue value)
        {
            bool wasSyncing = isSyncing;
            isSyncing = true;
            int rowIndex = dgvCheck.Rows.Add();
            DataGridViewRow row = dgvCheck.Rows[rowIndex];
            row.Cells[colCheckNote.Index].Value = GetDebugNote(index);
            row.Cells[colCheckIndex.Index].Value = index.ToString("D3");
            row.Cells[colCheckName.Index].Value = value.Name;
            row.Cells[colCheckType.Index].Value = value.Type;
            row.Cells[colCheckValue.Index].Value = ResolveChecked(value);
            isSyncing = wasSyncing;
        }

        private void AddEditRow(int index, DicValue value)
        {
            int rowIndex = dgvEdit.Rows.Add();
            DataGridViewRow row = dgvEdit.Rows[rowIndex];
            row.Cells[colEditNote.Index].Value = GetDebugNote(index);
            row.Cells[colEditIndex.Index].Value = index.ToString("D3");
            row.Cells[colEditName.Index].Value = value.Name;
            row.Cells[colEditType.Index].Value = value.Type;
            row.Cells[colEditValue.Index].Value = value.Value;
        }

        private void RefreshCheckRow(DataGridViewRow row, int index)
        {
            if (SF.valueStore == null || row == null)
            {
                return;
            }
            if (!SF.valueStore.TryGetValueByIndex(index, out DicValue value))
            {
                return;
            }
            isSyncing = true;
            row.Cells[colCheckNote.Index].Value = GetDebugNote(index);
            row.Cells[colCheckType.Index].Value = value.Type;
            row.Cells[colCheckValue.Index].Value = ResolveChecked(value);
            isSyncing = false;
        }

        private void RefreshEditRow(DataGridViewRow row, int index)
        {
            if (SF.valueStore == null || row == null)
            {
                return;
            }
            if (!SF.valueStore.TryGetValueByIndex(index, out DicValue value))
            {
                return;
            }
            isSyncing = true;
            row.Cells[colEditNote.Index].Value = GetDebugNote(index);
            row.Cells[colEditType.Index].Value = value.Type;
            row.Cells[colEditValue.Index].Value = value.Value;
            txtEditValue.Text = value.Value;
            isSyncing = false;
        }

        private void dgvEdit_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (isSyncing)
            {
                return;
            }
            if (e.RowIndex < 0 || e.ColumnIndex != colEditNote.Index)
            {
                return;
            }
            DataGridViewRow row = dgvEdit.Rows[e.RowIndex];
            if (!TryGetEditRowIndex(row, out int index, out string error))
            {
                ShowEditError(error);
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
            if (SF.valueStore == null)
            {
                return;
            }
            EnsureDebugConfigLoaded();
            int checkSelected = GetSelectedIndex(cboCheckVar);
            int editSelected = GetSelectedIndex(cboEditVar);
            List<ValueOption> options = BuildValueOptions();
            BindOptions(cboCheckVar, options, checkSelected);
            BindOptions(cboEditVar, options, editSelected);
        }

        private void EnsureDebugConfigLoaded()
        {
            if (isConfigLoaded)
            {
                return;
            }
            if (SF.valueStore == null)
            {
                return;
            }
            string path = GetDebugConfigPath();
            if (string.IsNullOrWhiteSpace(path))
            {
                LogError("变量调试配置路径无效");
                return;
            }
            if (!File.Exists(path))
            {
                isConfigLoaded = true;
                return;
            }
            try
            {
                string json = File.ReadAllText(path);
                ValueDebugConfig config = JsonConvert.DeserializeObject<ValueDebugConfig>(json);
                if (!TryValidateConfig(config, out string error))
                {
                    checkIndexSet.Clear();
                    editIndexSet.Clear();
                    debugNotes.Clear();
                    LogError($"变量调试配置无效:{error}");
                    isConfigLoaded = true;
                    return;
                }
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
            }
            catch (Exception ex)
            {
                checkIndexSet.Clear();
                editIndexSet.Clear();
                debugNotes.Clear();
                LogError($"变量调试配置加载失败:{ex.Message}");
                isConfigLoaded = true;
            }
        }

        private bool TryValidateConfig(ValueDebugConfig config, out string error)
        {
            error = null;
            if (config == null)
            {
                error = "配置为空";
                return false;
            }
            if (config.CheckIndexes == null || config.EditIndexes == null || config.Notes == null)
            {
                error = "配置字段缺失";
                return false;
            }
            if (!TryValidateIndexList(config.CheckIndexes, out error))
            {
                return false;
            }
            if (!TryValidateIndexList(config.EditIndexes, out error))
            {
                return false;
            }
            if (!TryValidateNotes(config, out error))
            {
                return false;
            }
            return true;
        }

        private bool TryValidateIndexList(List<int> indexes, out string error)
        {
            error = null;
            HashSet<int> unique = new HashSet<int>();
            foreach (int index in indexes)
            {
                if (!TryValidateIndex(index, out error))
                {
                    return false;
                }
                if (!unique.Add(index))
                {
                    error = $"索引重复:{index:D3}";
                    return false;
                }
            }
            return true;
        }

        private bool TryValidateNotes(ValueDebugConfig config, out string error)
        {
            error = null;
            HashSet<int> allow = new HashSet<int>(config.CheckIndexes);
            foreach (int index in config.EditIndexes)
            {
                allow.Add(index);
            }
            foreach (var item in config.Notes)
            {
                if (!allow.Contains(item.Key))
                {
                    error = $"备注索引未在调试列表中:{item.Key:D3}";
                    return false;
                }
                if (item.Value == null)
                {
                    error = $"备注为空:{item.Key:D3}";
                    return false;
                }
            }
            return true;
        }

        private bool TryValidateIndex(int index, out string error)
        {
            error = null;
            if (index < 0 || index >= ValueConfigStore.ValueCapacity)
            {
                error = $"索引超出范围:{index}";
                return false;
            }
            if (SF.valueStore == null || !SF.valueStore.TryGetValueByIndex(index, out _))
            {
                error = $"变量不存在:{index:D3}";
                return false;
            }
            return true;
        }

        private void SaveDebugConfig()
        {
            string path = GetDebugConfigPath();
            if (string.IsNullOrWhiteSpace(path))
            {
                LogError("变量调试配置路径无效");
                return;
            }
            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                List<int> checkList = new List<int>(checkIndexSet);
                List<int> editList = new List<int>(editIndexSet);
                checkList.Sort();
                editList.Sort();
                CleanNotes(checkList, editList);
                Dictionary<int, string> noteSnapshot = new Dictionary<int, string>(debugNotes);
                ValueDebugConfig config = new ValueDebugConfig
                {
                    CheckIndexes = checkList,
                    EditIndexes = editList,
                    Notes = noteSnapshot
                };
                string json = JsonConvert.SerializeObject(config, Formatting.Indented);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                LogError($"变量调试配置保存失败:{ex.Message}");
            }
        }

        private string GetDebugConfigPath()
        {
            if (string.IsNullOrWhiteSpace(SF.ConfigPath))
            {
                return null;
            }
            return Path.Combine(SF.ConfigPath, DebugConfigFileName);
        }

        private static List<ValueOption> BuildValueOptions()
        {
            List<ValueOption> options = new List<ValueOption>();
            for (int i = 0; i < ValueConfigStore.ValueCapacity; i++)
            {
                if (!SF.valueStore.TryGetValueByIndex(i, out DicValue value))
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

        private bool TryApplyValue(int index, string newValue, string source, out string error)
        {
            error = null;
            if (SF.valueStore == null)
            {
                error = "变量库未初始化";
                return false;
            }
            if (!SF.valueStore.TryGetValueByIndex(index, out DicValue value))
            {
                error = $"变量不存在:{index:D3}";
                return false;
            }
            if (!TryValidateValue(value, newValue, out error))
            {
                return false;
            }
            string oldValue = value.Value;
            if (!SF.valueStore.TryModifyValueByIndex(index, _ => newValue, out string updateError, source))
            {
                error = string.IsNullOrWhiteSpace(updateError) ? "变量写入失败" : updateError;
                return false;
            }
            string message = $"变量调试修改成功：[{index:D3}] {value.Name} {oldValue} -> {newValue}";
            if (SF.DR?.Logger != null)
            {
                SF.DR.Logger.Log(message, LogLevel.Normal);
            }
            else if (SF.frmInfo != null && !SF.frmInfo.IsDisposed)
            {
                SF.frmInfo.PrintInfo(message, FrmInfo.Level.Normal);
            }
            return true;
        }

        private bool TryUpdateNote(int index, string newNote, out string error)
        {
            error = null;
            if (SF.valueStore == null)
            {
                error = "变量库未初始化";
                return false;
            }
            if (!SF.valueStore.TryGetValueByIndex(index, out DicValue value))
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
            if (SF.DR?.Logger != null)
            {
                SF.DR.Logger.Log(message, LogLevel.Normal);
            }
            else if (SF.frmInfo != null && !SF.frmInfo.IsDisposed)
            {
                SF.frmInfo.PrintInfo(message, FrmInfo.Level.Normal);
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

        private static bool TryParseIndex(string text, out int index, out string error)
        {
            index = -1;
            error = null;
            if (string.IsNullOrEmpty(text))
            {
                error = "索引不能为空";
                return false;
            }
            if (!IsStrictDigits(text))
            {
                error = "索引格式非法";
                return false;
            }
            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out index))
            {
                error = "索引格式非法";
                return false;
            }
            if (index < 0 || index >= ValueConfigStore.ValueCapacity)
            {
                error = "索引超出范围";
                return false;
            }
            return true;
        }

        private bool TryGetCheckRowIndex(DataGridViewRow row, out int index, out string error)
        {
            index = -1;
            error = null;
            if (row == null)
            {
                error = "未选择变量";
                return false;
            }
            string text = row.Cells[colCheckIndex.Index].Value?.ToString();
            if (!TryParseIndex(text, out index, out error))
            {
                return false;
            }
            return true;
        }

        private bool TryGetEditRowIndex(DataGridViewRow row, out int index, out string error)
        {
            index = -1;
            error = null;
            if (row == null)
            {
                error = "未选择变量";
                return false;
            }
            string text = row.Cells[colEditIndex.Index].Value?.ToString();
            if (!TryParseIndex(text, out index, out error))
            {
                return false;
            }
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
            int startIndex = 0;
            if (text[0] == '-')
            {
                if (text.Length == 1)
                {
                    return false;
                }
                startIndex = 1;
            }
            for (int i = startIndex; i < text.Length; i++)
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
            lblCheckStatus.ForeColor = Color.ForestGreen;
        }

        private void ShowCheckError(string message)
        {
            string logMessage = string.IsNullOrWhiteSpace(message) ? "复选框调试失败" : message;
            LogError($"复选框调试失败：{logMessage}");
            lblCheckStatus.Text = logMessage;
            lblCheckStatus.ForeColor = Color.Red;
        }

        private void ShowEditSuccess(string message)
        {
            lblEditStatus.Text = message;
            lblEditStatus.ForeColor = Color.ForestGreen;
        }

        private void ShowEditError(string message)
        {
            string logMessage = string.IsNullOrWhiteSpace(message) ? "编辑框调试失败" : message;
            LogError($"编辑框调试失败：{logMessage}");
            lblEditStatus.Text = logMessage;
            lblEditStatus.ForeColor = Color.Red;
        }

        private void LogError(string message)
        {
            if (SF.DR?.Logger != null)
            {
                SF.DR.Logger.Log(message, LogLevel.Error);
            }
            else if (SF.frmInfo != null && !SF.frmInfo.IsDisposed)
            {
                SF.frmInfo.PrintInfo(message, FrmInfo.Level.Error);
            }
        }
    }
}
