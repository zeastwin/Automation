using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Button;

namespace Automation
{
    public partial class FrmInfo : Form
    {
     
        public FrmInfo()
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

        private void FrmInfo_Load(object sender, EventArgs e)
        {
           
        }

        private void dataGridView1_CellMouseDoubleClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.RowIndex >= 0)
            {
                DataGridViewCell cell = dataGridView1.Rows[e.RowIndex].Cells[1];
                string cellValue = cell.Value.ToString();

                string[] values = cellValue.Split(new string[] { "---" }, StringSplitOptions.None);

                SF.frmDataGrid.SelectChildNode(int.Parse(values[0]), int.Parse(values[1]));
                SF.frmDataGrid.ScrollRowToCenter(int.Parse(values[2]));
                SF.frmDataGrid.SetRowColor(SF.DR.ProcHandles[SF.frmProc.SelectedProcNum].opsNum, Color.LightBlue);
            }
        }
        [Browsable(false)]
        [JsonIgnore]
        public Level level { get; set; } = 0;

        public Level GetState()
        {
            return level;
        }
        public void SetState(Level level)
        {
            this.level = level;
        }
        public enum Level
        {
            Error = 0,
            Normal,
        }
        // InfoLevel 信息级别
        // 0 红色报警
        // 1 普通信息
        public void PrintInfo(string str, Level InfoLevel)
        {
            if (SF.frmInfo.IsDisposed)
                return;
            Invoke(new Action(() =>
            {
                int length = ReceiveTextBox.TextLength;
                str = $"[{DateTime.Now.ToString("yyyy-MM-dd HH时mm分ss秒")}]：{str}\r\n";
                ReceiveTextBox.AppendText(str);

                Color color = Color.Black;
                if(InfoLevel == Level.Error)
                {
                    color = Color.Red;
                }
                else if(InfoLevel == Level.Normal)
                {
                    color = Color.Black;
                }
                ReceiveTextBox.Select(length, str.Length);
                ReceiveTextBox.SelectionBackColor = color;
                ReceiveTextBox.ScrollToCaret();
            }));
        }
    }
}
