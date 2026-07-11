using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace Automation
{
    public partial class FrmAlarmConfig : Form
    {
        private readonly BindingSource alarmBindingSource = new BindingSource();
        private DataGridView alarmGrid;
        private TextBox txtSearch;
        private CheckBox chkConfiguredOnly;
        private Label lblCount;
        private Label lblEditorTitle;
        private Label lblDirty;
        private TextBox txtName;
        private ComboBox cmbCategory;
        private TextBox txtNote;
        private TextBox txtBtn1;
        private TextBox txtBtn2;
        private TextBox txtBtn3;
        private Button btnSave;
        private Button btnClear;
        private Button btnFirstEmpty;
        private Button btnReload;
        private int currentIndex = -1;
        private bool isLoading;
        private bool isDirty;
        private bool externalChangePending;
        private bool suppressSelection;

        public FrmAlarmConfig()
        {
            InitializeComponent();
            BuildInterface();
        }

        private void BuildInterface()
        {
            foreach (Control control in Controls.Cast<Control>().ToArray())
            {
                control.Dispose();
            }
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Color.FromArgb(244, 247, 250);
            MinimumSize = new Size(980, 620);
            Size = new Size(1180, 720);

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            root.Controls.Add(BuildHeader(), 0, 0);
            root.Controls.Add(BuildContent(), 0, 1);
            lblCount = new Label
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(14, 0, 0, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(86, 101, 116),
                BackColor = Color.FromArgb(233, 239, 245)
            };
            root.Controls.Add(lblCount, 0, 2);
            Controls.Add(root);
        }

        private Control BuildHeader()
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            panel.Controls.Add(new Label
            {
                AutoSize = true,
                Location = new Point(20, 12),
                Text = "报警信息配置",
                Font = new Font("Microsoft YaHei UI", 17F, FontStyle.Bold),
                ForeColor = Color.FromArgb(28, 44, 60)
            });
            panel.Controls.Add(new Label
            {
                AutoSize = true,
                Location = new Point(22, 47),
                Text = "固定编号供流程引用；通过搜索定位，修改后显式保存",
                ForeColor = Color.FromArgb(102, 116, 132)
            });
            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                AutoSize = true,
                WrapContents = false,
                Padding = new Padding(0, 16, 14, 0)
            };
            btnFirstEmpty = CreateButton("定位空槽位", false);
            btnReload = CreateButton("重新加载", false);
            btnFirstEmpty.Click += BtnFirstEmpty_Click;
            btnReload.Click += BtnReload_Click;
            actions.Controls.Add(btnFirstEmpty);
            actions.Controls.Add(btnReload);
            panel.Controls.Add(actions);
            return panel;
        }

        private Control BuildContent()
        {
            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                FixedPanel = FixedPanel.Panel1,
                SplitterDistance = 520,
                SplitterWidth = 6,
                Padding = new Padding(12)
            };
            split.Panel1.Controls.Add(BuildListPanel());
            split.Panel2.Controls.Add(BuildEditorPanel());
            return split;
        }

        private Control BuildListPanel()
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            var filter = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Padding = new Padding(10, 9, 8, 6),
                WrapContents = false
            };
            filter.Controls.Add(new Label
            {
                AutoSize = true,
                Text = "搜索",
                Margin = new Padding(0, 5, 8, 0),
                ForeColor = Color.FromArgb(74, 89, 104)
            });
            txtSearch = new TextBox { Width = 245 };
            chkConfiguredOnly = new CheckBox
            {
                AutoSize = true,
                Checked = true,
                Text = "仅已配置",
                Margin = new Padding(12, 5, 0, 0)
            };
            txtSearch.TextChanged += FilterChanged;
            chkConfiguredOnly.CheckedChanged += FilterChanged;
            filter.Controls.Add(txtSearch);
            filter.Controls.Add(chkConfiguredOnly);

            alarmGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                AutoGenerateColumns = false,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                RowTemplate = { Height = 34 }
            };
            alarmGrid.Columns.Add(CreateColumn(nameof(AlarmInfo.Index), "编号", 18));
            alarmGrid.Columns.Add(CreateColumn(nameof(AlarmInfo.Name), "名称", 38));
            alarmGrid.Columns.Add(CreateColumn(nameof(AlarmInfo.Category), "分类", 28));
            alarmGrid.ColumnHeadersHeight = 38;
            alarmGrid.EnableHeadersVisualStyles = false;
            alarmGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(230, 237, 244);
            alarmGrid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(48, 63, 78);
            alarmGrid.ColumnHeadersDefaultCellStyle.Font = new Font(Font, FontStyle.Bold);
            alarmGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(217, 234, 250);
            alarmGrid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(27, 43, 59);
            typeof(DataGridView).GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(alarmGrid, true, null);
            alarmGrid.SelectionChanged += AlarmGrid_SelectionChanged;
            layout.Controls.Add(filter, 0, 0);
            layout.Controls.Add(alarmGrid, 0, 1);
            return layout;
        }

        private Control BuildEditorPanel()
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(20) };
            var layout = new TableLayoutPanel { Dock = DockStyle.Top, Height = 510, ColumnCount = 2, RowCount = 8 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 94F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 142F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66F));
            lblEditorTitle = new Label
            {
                Dock = DockStyle.Fill,
                Text = "请选择报警槽位",
                Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold),
                ForeColor = Color.FromArgb(35, 52, 69),
                TextAlign = ContentAlignment.MiddleLeft
            };
            lblDirty = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight,
                ForeColor = Color.FromArgb(204, 112, 25)
            };
            layout.Controls.Add(lblEditorTitle, 0, 0);
            layout.SetColumnSpan(lblEditorTitle, 1);
            layout.Controls.Add(lblDirty, 1, 0);
            txtName = new TextBox { Dock = DockStyle.Fill };
            cmbCategory = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown };
            txtNote = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical };
            txtBtn1 = new TextBox { Dock = DockStyle.Fill };
            txtBtn2 = new TextBox { Dock = DockStyle.Fill };
            txtBtn3 = new TextBox { Dock = DockStyle.Fill };
            AddEditorRow(layout, 1, "名称 *", txtName);
            AddEditorRow(layout, 2, "分类", cmbCategory);
            AddEditorRow(layout, 3, "报警信息 *", txtNote);
            AddEditorRow(layout, 4, "按钮 1", txtBtn1);
            AddEditorRow(layout, 5, "按钮 2", txtBtn2);
            AddEditorRow(layout, 6, "按钮 3", txtBtn3);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            btnSave = CreateButton("保存报警", true);
            btnClear = CreateButton("清空槽位", false);
            btnSave.Click += BtnSave_Click;
            btnClear.Click += BtnClear_Click;
            actions.Controls.Add(btnSave);
            actions.Controls.Add(btnClear);
            layout.Controls.Add(actions, 0, 7);
            layout.SetColumnSpan(actions, 2);
            panel.Controls.Add(layout);
            foreach (Control input in new Control[] { txtName, cmbCategory, txtNote, txtBtn1, txtBtn2, txtBtn3 })
            {
                input.TextChanged += EditorValueChanged;
            }
            SetEditorEnabled(false);
            return panel;
        }

        private static void AddEditorRow(TableLayoutPanel layout, int row, string label, Control control)
        {
            layout.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = label,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(74, 89, 104)
            }, 0, row);
            control.Margin = new Padding(0, 8, 8, 7);
            layout.Controls.Add(control, 1, row);
        }

        private static DataGridViewColumn CreateColumn(string property, string title, float weight)
        {
            return new DataGridViewTextBoxColumn
            {
                DataPropertyName = property,
                HeaderText = title,
                FillWeight = weight,
                SortMode = DataGridViewColumnSortMode.Automatic
            };
        }

        private static Button CreateButton(string text, bool primary)
        {
            var button = new Button
            {
                AutoSize = true,
                MinimumSize = new Size(104, 36),
                FlatStyle = FlatStyle.Flat,
                Text = text,
                BackColor = primary ? Color.FromArgb(36, 112, 184) : Color.White,
                ForeColor = primary ? Color.White : Color.FromArgb(49, 63, 77),
                Margin = new Padding(6, 4, 0, 0)
            };
            button.FlatAppearance.BorderSize = primary ? 0 : 1;
            button.FlatAppearance.BorderColor = Color.FromArgb(190, 201, 213);
            return button;
        }

        public void RefreshAlarmInfo()
        {
            if (isDirty)
            {
                externalChangePending = true;
                lblDirty.Text = "● 外部配置已更新，请重新加载";
                btnSave.Enabled = false;
                return;
            }
            if (SF.alarmInfoStore == null)
            {
                SF.alarmInfoStore = new AlarmInfoStore();
            }
            isLoading = true;
            try
            {
                SF.alarmInfoStore.Load(SF.ConfigPath);
                externalChangePending = false;
                RefreshCategoryOptions();
                ApplyFilter(currentIndex);
            }
            finally
            {
                isLoading = false;
                SetDirty(false);
            }
        }

        private void ApplyFilter(int preferredIndex = -1)
        {
            if (SF.alarmInfoStore == null)
            {
                return;
            }
            string keyword = txtSearch.Text.Trim();
            IEnumerable<AlarmInfo> query = SF.alarmInfoStore.Alarms;
            if (chkConfiguredOnly.Checked)
            {
                query = query.Where(item => !string.IsNullOrWhiteSpace(item.Name));
            }
            if (!string.IsNullOrEmpty(keyword))
            {
                query = query.Where(item => item.Index.ToString().Contains(keyword)
                    || Contains(item.Name, keyword) || Contains(item.Category, keyword)
                    || Contains(item.Note, keyword));
            }
            List<AlarmInfo> rows = query.ToList();
            suppressSelection = true;
            alarmBindingSource.DataSource = rows;
            alarmGrid.DataSource = alarmBindingSource;
            suppressSelection = false;
            int configured = SF.alarmInfoStore.Alarms.Count(item => !string.IsNullOrWhiteSpace(item.Name));
            lblCount.Text = $"显示 {rows.Count} 条 · 已配置 {configured}/{AlarmInfoStore.AlarmCapacity} · 空闲 {AlarmInfoStore.AlarmCapacity - configured}";
            SelectIndex(preferredIndex);
        }

        private static bool Contains(string source, string keyword)
        {
            return source?.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void FilterChanged(object sender, EventArgs e)
        {
            if (!isLoading && ConfirmDiscardChanges())
            {
                ApplyFilter(currentIndex);
            }
        }

        private void AlarmGrid_SelectionChanged(object sender, EventArgs e)
        {
            if (suppressSelection || alarmGrid.CurrentRow?.DataBoundItem is not AlarmInfo alarm)
            {
                return;
            }
            if (alarm.Index != currentIndex && !ConfirmDiscardChanges())
            {
                SelectIndex(currentIndex);
                return;
            }
            LoadEditor(alarm);
        }

        private void LoadEditor(AlarmInfo alarm)
        {
            isLoading = true;
            currentIndex = alarm.Index;
            lblEditorTitle.Text = $"报警 #{alarm.Index}";
            txtName.Text = alarm.Name ?? string.Empty;
            cmbCategory.Text = alarm.Category ?? string.Empty;
            txtNote.Text = alarm.Note ?? string.Empty;
            txtBtn1.Text = alarm.Btn1 ?? string.Empty;
            txtBtn2.Text = alarm.Btn2 ?? string.Empty;
            txtBtn3.Text = alarm.Btn3 ?? string.Empty;
            SetEditorEnabled(true);
            isLoading = false;
            SetDirty(false);
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            if (externalChangePending)
            {
                MessageBox.Show(this, "报警配置已被外部工具更新，请重新加载后再修改。", "配置已变化",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (currentIndex < 0 || SF.alarmInfoStore == null)
            {
                return;
            }
            try
            {
                SF.alarmInfoStore.UpdateAlarm(currentIndex, txtName.Text, cmbCategory.Text,
                    txtBtn1.Text, txtBtn2.Text, txtBtn3.Text, txtNote.Text);
                SF.alarmInfoStore.Save(SF.ConfigPath);
                RefreshCategoryOptions();
                SetDirty(false);
                ApplyFilter(currentIndex);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "报警配置校验失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void BtnClear_Click(object sender, EventArgs e)
        {
            if (currentIndex < 0 || SF.alarmInfoStore == null)
            {
                return;
            }
            if (MessageBox.Show(this, $"确认清空报警 #{currentIndex}？流程中对该编号的引用不会自动删除。",
                "清空报警槽位", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }
            SF.alarmInfoStore.ClearAlarm(currentIndex);
            SF.alarmInfoStore.Save(SF.ConfigPath);
            SetDirty(false);
            ApplyFilter(-1);
            ClearEditor();
        }

        private void BtnFirstEmpty_Click(object sender, EventArgs e)
        {
            if (!ConfirmDiscardChanges() || SF.alarmInfoStore == null)
            {
                return;
            }
            AlarmInfo empty = SF.alarmInfoStore.Alarms.FirstOrDefault(item => string.IsNullOrWhiteSpace(item.Name));
            if (empty == null)
            {
                MessageBox.Show(this, "报警槽位已全部使用。", "报警配置", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            chkConfiguredOnly.Checked = false;
            txtSearch.Text = empty.Index.ToString();
            ApplyFilter(empty.Index);
        }

        private void BtnReload_Click(object sender, EventArgs e)
        {
            if (!ConfirmDiscardChanges())
            {
                return;
            }
            SetDirty(false);
            RefreshAlarmInfo();
        }

        private void RefreshCategoryOptions()
        {
            string current = cmbCategory.Text;
            string[] categories = SF.alarmInfoStore.Alarms
                .Select(item => item.Category?.Trim())
                .Where(value => !string.IsNullOrEmpty(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value)
                .ToArray();
            cmbCategory.BeginUpdate();
            cmbCategory.Items.Clear();
            cmbCategory.Items.AddRange(categories);
            cmbCategory.EndUpdate();
            cmbCategory.Text = current;
        }

        private void SelectIndex(int index)
        {
            if (index < 0)
            {
                return;
            }
            foreach (DataGridViewRow row in alarmGrid.Rows)
            {
                if (row.DataBoundItem is AlarmInfo alarm && alarm.Index == index)
                {
                    suppressSelection = true;
                    row.Selected = true;
                    alarmGrid.CurrentCell = row.Cells[0];
                    suppressSelection = false;
                    LoadEditor(alarm);
                    return;
                }
            }
        }

        private void EditorValueChanged(object sender, EventArgs e)
        {
            if (!isLoading && currentIndex >= 0)
            {
                SetDirty(true);
            }
        }

        private void SetDirty(bool dirty)
        {
            isDirty = dirty;
            lblDirty.Text = externalChangePending ? "● 外部配置已更新，请重新加载"
                : dirty ? "● 有未保存修改" : string.Empty;
            btnSave.Enabled = currentIndex >= 0 && dirty && !externalChangePending;
        }

        private bool ConfirmDiscardChanges()
        {
            return !isDirty || MessageBox.Show(this, "当前报警有未保存修改，确认放弃？", "未保存修改",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }

        private void SetEditorEnabled(bool enabled)
        {
            foreach (Control control in new Control[] { txtName, cmbCategory, txtNote, txtBtn1, txtBtn2, txtBtn3, btnClear })
            {
                control.Enabled = enabled;
            }
            btnSave.Enabled = enabled && isDirty;
        }

        private void ClearEditor()
        {
            isLoading = true;
            currentIndex = -1;
            lblEditorTitle.Text = "请选择报警槽位";
            txtName.Clear();
            cmbCategory.Text = string.Empty;
            txtNote.Clear();
            txtBtn1.Clear();
            txtBtn2.Clear();
            txtBtn3.Clear();
            isLoading = false;
            SetDirty(false);
            SetEditorEnabled(false);
        }

        private void FrmAlarmConfig_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                if (!ConfirmDiscardChanges())
                {
                    e.Cancel = true;
                    return;
                }
                e.Cancel = true;
                Hide();
            }
        }

        private void dataGridView1_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
        }
    }
}
