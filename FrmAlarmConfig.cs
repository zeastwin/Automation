using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace Automation
{
    public partial class FrmAlarmConfig : Form
    {
        private readonly BindingSource alarmBindingSource = new BindingSource();
        private bool isLoading;

        public FrmAlarmConfig()
        {
            InitializeComponent();
            dataGridView1.SelectionMode = DataGridViewSelectionMode.CellSelect;
            dataGridView1.EditMode = DataGridViewEditMode.EditOnEnter;
            dataGridView1.Columns[0].SortMode = DataGridViewColumnSortMode.NotSortable;
            dataGridView1.Columns[0].ReadOnly = true;
            dataGridView1.RowHeadersVisible = false;
            dataGridView1.AutoGenerateColumns = false;

            index.DataPropertyName = nameof(AlarmInfo.Index);
            name.DataPropertyName = nameof(AlarmInfo.Name);
            category.DataPropertyName = nameof(AlarmInfo.Category);
            operaType.DataPropertyName = nameof(AlarmInfo.Btn1);
            btn2.DataPropertyName = nameof(AlarmInfo.Btn2);
            btn3.DataPropertyName = nameof(AlarmInfo.Btn3);
            Note.DataPropertyName = nameof(AlarmInfo.Note);

            PropertyInfo doubleBuffered = dataGridView1.GetType().GetProperty(
                "DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
            doubleBuffered?.SetValue(dataGridView1, true, null);

            ApplyLightStyle();
            dataGridView1.RowValidating += dataGridView1_RowValidating;
            dataGridView1.RowValidated += dataGridView1_RowValidated;
            dataGridView1.DataError += dataGridView1_DataError;
        }

        private void ApplyLightStyle()
        {
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = UiPalette.SurfaceStrong;
            dataGridView1.BackgroundColor = UiPalette.SurfaceStrong;
            dataGridView1.BorderStyle = BorderStyle.FixedSingle;
            dataGridView1.GridColor = UiPalette.Stroke;
            dataGridView1.EnableHeadersVisualStyles = false;
            dataGridView1.ColumnHeadersHeight = 34;
            dataGridView1.ColumnHeadersDefaultCellStyle.BackColor = UiPalette.SurfaceSubtle;
            dataGridView1.ColumnHeadersDefaultCellStyle.ForeColor = UiPalette.TextPrimary;
            dataGridView1.ColumnHeadersDefaultCellStyle.Font = new Font(Font, FontStyle.Bold);
            dataGridView1.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            dataGridView1.RowTemplate.Height = 30;
            dataGridView1.DefaultCellStyle.BackColor = UiPalette.SurfaceStrong;
            dataGridView1.AlternatingRowsDefaultCellStyle.BackColor = UiPalette.Input;
            dataGridView1.DefaultCellStyle.SelectionBackColor = UiPalette.Selection;
            dataGridView1.DefaultCellStyle.SelectionForeColor = UiPalette.Navigation;
        }

        public void RefreshAlarmInfo()
        {
            if (SF.alarmInfoStore == null)
            {
                SF.alarmInfoStore = new AlarmInfoStore();
            }

            isLoading = true;
            try
            {
                SF.alarmInfoStore.Load(SF.ConfigPath);
                alarmBindingSource.DataSource = SF.alarmInfoStore.Alarms;
                dataGridView1.DataSource = alarmBindingSource;
                alarmBindingSource.ResetBindings(false);
            }
            finally
            {
                isLoading = false;
            }
        }

        private void FrmAlarmConfig_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
        }

        private void dataGridView1_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0)
            {
                dataGridView1.Rows[e.RowIndex].ErrorText = string.Empty;
            }
        }

        private void dataGridView1_RowValidating(object sender, DataGridViewCellCancelEventArgs e)
        {
            if (isLoading || e.RowIndex < 0)
            {
                return;
            }

            DataGridViewRow row = dataGridView1.Rows[e.RowIndex];
            if (!(row.DataBoundItem is AlarmInfo alarm))
            {
                return;
            }

            bool hasName = !string.IsNullOrWhiteSpace(alarm.Name);
            bool hasNote = !string.IsNullOrWhiteSpace(alarm.Note);
            if (!hasName && !hasNote)
            {
                row.ErrorText = string.Empty;
                return;
            }
            if (!hasName || !hasNote)
            {
                row.ErrorText = "名称与信息必须同时填写。";
                e.Cancel = true;
                return;
            }
            row.ErrorText = string.Empty;
        }

        private void dataGridView1_RowValidated(object sender, DataGridViewCellEventArgs e)
        {
            if (isLoading || e.RowIndex < 0 || SF.alarmInfoStore == null)
            {
                return;
            }

            DataGridViewRow row = dataGridView1.Rows[e.RowIndex];
            if (!(row.DataBoundItem is AlarmInfo alarm))
            {
                return;
            }

            alarm.Index = e.RowIndex;
            alarm.Name = alarm.Name?.Trim();
            alarm.Category = alarm.Category?.Trim();
            alarm.Note = alarm.Note?.Trim();
            alarm.Btn1 = alarm.Btn1?.Trim();
            alarm.Btn2 = alarm.Btn2?.Trim();
            alarm.Btn3 = alarm.Btn3?.Trim();
            try
            {
                SF.alarmInfoStore.Save(SF.ConfigPath);
                row.ErrorText = string.Empty;
            }
            catch (Exception ex)
            {
                row.ErrorText = "保存失败：" + ex.Message;
                MessageBox.Show(row.ErrorText, "报警配置保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void dataGridView1_DataError(object sender, DataGridViewDataErrorEventArgs e)
        {
            e.ThrowException = false;
            if (e.RowIndex >= 0)
            {
                dataGridView1.Rows[e.RowIndex].ErrorText = "数据格式错误。";
            }
        }
    }
}
