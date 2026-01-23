using System;
using System.Reflection;
using System.Windows.Forms;

namespace Automation
{
    public partial class FrmAlarmConfig : Form
    {
        private readonly BindingSource alarmBindingSource = new BindingSource();
        private bool isLoading = false;

        public FrmAlarmConfig()
        {
            InitializeComponent();
            dataGridView1.SelectionMode = DataGridViewSelectionMode.ColumnHeaderSelect;
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

            Type dgvType = dataGridView1.GetType();
            PropertyInfo pi = dgvType.GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
            pi.SetValue(dataGridView1, true, null);

            dataGridView1.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            dataGridView1.RowValidating += dataGridView1_RowValidating;
            dataGridView1.RowValidated += dataGridView1_RowValidated;
            dataGridView1.DataError += dataGridView1_DataError;
        }

        public void RefreshAlarmInfo()
        {
            if (SF.alarmInfoStore == null)
            {
                SF.alarmInfoStore = new AlarmInfoStore();
            }

            isLoading = true;
            SF.alarmInfoStore.Load(SF.ConfigPath);
            alarmBindingSource.DataSource = SF.alarmInfoStore.Alarms;
            dataGridView1.DataSource = alarmBindingSource;
            alarmBindingSource.ResetBindings(false);
            isLoading = false;
        }

        private void FrmAlarmConfig_FormClosing(object sender, FormClosingEventArgs e)
        {
            e.Cancel = true;
            Hide();
        }

        private void dataGridView1_CellMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                dataGridView1.EndEdit();
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
            AlarmInfo alarm = row.DataBoundItem as AlarmInfo;
            if (alarm == null)
            {
                return;
            }

            string nameValue = alarm.Name?.Trim();
            string noteValue = alarm.Note?.Trim();
            bool hasName = !string.IsNullOrEmpty(nameValue);
            bool hasNote = !string.IsNullOrEmpty(noteValue);

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
            AlarmInfo alarm = row.DataBoundItem as AlarmInfo;
            if (alarm == null)
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

            SF.alarmInfoStore.Save(SF.ConfigPath);
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
