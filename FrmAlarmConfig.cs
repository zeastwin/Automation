using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Automation
{
    public partial class FrmAlarmConfig : Form
    {
        public List<AlarmInfo> alarmInfos;
        public bool isFinBulidFrmAlarmInfo = false;
        //标志是否完成编辑
        public bool isEndEdit = true;
        public FrmAlarmConfig()
        {
            InitializeComponent();
            dataGridView1.SelectionMode = DataGridViewSelectionMode.ColumnHeaderSelect;
            dataGridView1.Columns[0].SortMode = DataGridViewColumnSortMode.NotSortable;
            dataGridView1.RowHeadersVisible = false;
            dataGridView1.AutoGenerateColumns = false;


            Type dgvType = this.dataGridView1.GetType();
            PropertyInfo pi = dgvType.GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
            pi.SetValue(this.dataGridView1, true, null);

            dataGridView1.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        }
        //从文件更新报警信息表
        public void RefreshAlarmInfo()
        {

            if (!Directory.Exists(SF.ConfigPath))
            {
                Directory.CreateDirectory(SF.ConfigPath);
            }
            if (!File.Exists(SF.ConfigPath + "AlarmInfo.json"))
            {
                SF.frmAlarmConfig.alarmInfos = new List<AlarmInfo>();
                SF.mainfrm.SaveAsJson(SF.ConfigPath, "AlarmInfo", SF.frmAlarmConfig.alarmInfos);
            }
            alarmInfos = SF.mainfrm.ReadJson<List<AlarmInfo>>(SF.ConfigPath, "AlarmInfo");

            RefreshAlarmDgv();

        }

        public void RefreshAlarmDgv()
        {
            dataGridView1.Rows.Clear();

            for (int i = 0; i < 1000; i++)
            {
                dataGridView1.Rows.Add();
                dataGridView1.Rows[i].Cells[0].Value = alarmInfos[i].Index;
                dataGridView1.Rows[i].Cells[1].Value = alarmInfos[i].Name;
                dataGridView1.Rows[i].Cells[2].Value = alarmInfos[i].Btn1;
                dataGridView1.Rows[i].Cells[3].Value = alarmInfos[i].Btn2;
                dataGridView1.Rows[i].Cells[4].Value = alarmInfos[i].Btn3;
                dataGridView1.Rows[i].Cells[5].Value = alarmInfos[i].Note;
            }
            isFinBulidFrmAlarmInfo = true;
        }
        //刷新变量界面
        public void FreshFrmAlarmInfo()
        {
            for (int i = 0; i < 1000; i++)
            {
                dataGridView1.Rows[i].Cells[0].Value = alarmInfos[i].Index;
                dataGridView1.Rows[i].Cells[1].Value = alarmInfos[i].Name;
                dataGridView1.Rows[i].Cells[2].Value = alarmInfos[i].Btn1;
                dataGridView1.Rows[i].Cells[3].Value = alarmInfos[i].Btn2;
                dataGridView1.Rows[i].Cells[4].Value = alarmInfos[i].Btn3;
                dataGridView1.Rows[i].Cells[5].Value = alarmInfos[i].Note;
            }
        }
        private void FrmAlarmConfig_FormClosing(object sender, FormClosingEventArgs e)
        {
            e.Cancel = true;
            this.Hide();
        }

        private void dataGridView1_CellMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && isEndEdit == true)
                FreshFrmAlarmInfo();
        }

        private void dataGridView1_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (isFinBulidFrmAlarmInfo)
            {
                // 确保值变化发生在单元格中而不是在行标题或列标题
                if (e.RowIndex >= 0 && e.ColumnIndex >= 0)
                {
                    DataGridView dataGridView = (DataGridView)sender;
                    bool isEffective = CheckRowCellsHaveValue(dataGridView, e.RowIndex);
                    isEndEdit = isEffective;
                    if (isEffective)
                    {
                        int index = (int)dataGridView.Rows[e.RowIndex].Cells[0].Value;
                        string name = (string)dataGridView.Rows[e.RowIndex].Cells[1].Value;
                        string btn1 = (string)dataGridView.Rows[e.RowIndex].Cells[2].Value;
                        string btn2 = (string)dataGridView.Rows[e.RowIndex].Cells[3].Value;
                        string btn3 = (string)dataGridView.Rows[e.RowIndex].Cells[4].Value;
                        string note = (string)dataGridView.Rows[e.RowIndex].Cells[5].Value;
                        AlarmInfo alarm = new AlarmInfo() { Index= index,Name = name, Btn1 =btn1, Btn2 = btn2, Btn3 = btn3, Note = note };
                        alarmInfos[index] = alarm;
                        SF.mainfrm.SaveAsJson(SF.ConfigPath, "AlarmInfo", SF.frmAlarmConfig.alarmInfos); 
                        FreshFrmAlarmInfo();
                    }

                }
            }
        }
        public bool CheckRowCellsHaveValue(DataGridView dataGridView, int rowIndex)
        {
            int colsCount = dataGridView.ColumnCount;

            for (int colIndex = 0; colIndex < colsCount; colIndex++)
            {
                if (colIndex == 1|| colIndex == 5)
                {
                    object cellValue = dataGridView.Rows[rowIndex].Cells[colIndex].Value;
                    if (cellValue == null || cellValue.ToString() == "")
                    {
                        return false;
                    }
                }
            }
            return true;
        }
    }
    public class AlarmInfo
    {
        public int Index { get; set; }
        public string Name { get; set; }

        public string Btn1 { get; set; }

        public string Btn2 { get; set; }

        public string Btn3 { get; set; }

        public string Note { get; set; }
    }
}
