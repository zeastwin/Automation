using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.SqlTypes;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static Automation.OperationTypePartial;

namespace Automation
{
    public partial class FrmSearch : Form
    {
        public FrmSearch()
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

        private void FrmSearch_FormClosing(object sender, FormClosingEventArgs e)
        {
            e.Cancel = true;
            SF.frmDataGrid.ClearAllRowColors();
            this.Hide();
        }
       
        private void btnSearch_Click(object sender, EventArgs e)
        {
            int Count = 0;
            dataGridView1.Rows.Clear();
            for (int i = 0; i < SF.frmProc.procsList.Count; i++)
            {
                for(int j = 0;j< SF.frmProc.procsList[i].steps.Count;j++)
                {
                    for (int k = 0; k < SF.frmProc.procsList[i].steps[j].Ops.Count; k++)
                    {
                        OperationType obj = SF.frmProc.procsList[i].steps[j].Ops[k];

                        Type objectType = obj.GetType();
                        PropertyInfo[] properties = objectType.GetProperties();

                        foreach (PropertyInfo property in properties)
                        {
                            string propertyName = property.Name;
                            object propertyValue = property.GetValue(obj);
                            if (propertyValue == null)
                                continue;
                            if (checkBox1.Checked)
                            {
                                bool stringsAreEqual = checkBox2.Checked? propertyValue.ToString() == textBox1.Text:string.Equals(propertyValue.ToString(), textBox1.Text, StringComparison.OrdinalIgnoreCase);
                                if (stringsAreEqual)
                                {
                                    dataGridView1.Rows.Add();
                                    dataGridView1.Rows[Count].Cells[0].Value = Count;
                                    dataGridView1.Rows[Count].Cells[1].Value = obj.Name;
                                    dataGridView1.Rows[Count].Cells[2].Value = $"{i}-{j}-{k}";
                                    Count++;
                                }
                            }
                            else
                            {
                                bool containsSubstring = checkBox2.Checked ? propertyValue.ToString().Contains(textBox1.Text): propertyValue.ToString().ToLower().Contains(textBox1.Text.ToLower());
                                if (containsSubstring)
                                {
                                    dataGridView1.Rows.Add();
                                    dataGridView1.Rows[Count].Cells[0].Value = Count;
                                    dataGridView1.Rows[Count].Cells[1].Value = obj.Name;
                                    dataGridView1.Rows[Count].Cells[2].Value = $"{i}-{j}-{k}";
                                    Count++;
                                }
                            }

                        }
                    }
                }
            }

        }

        private void dataGridView1_CellMouseDoubleClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.RowIndex >= 0)
            {
                DataGridViewCell cell = dataGridView1.Rows[e.RowIndex].Cells[2];
                string cellValue = cell.Value.ToString();

                string[] values = cellValue.Split('-');

                SF.frmDataGrid.SelectChildNode(int.Parse(values[0]), int.Parse(values[1]));
                SF.frmDataGrid.ScrollRowToCenter(int.Parse(values[2]));
                SF.frmDataGrid.ClearAllRowColors();
                SF.frmDataGrid.SetRowColor(int.Parse(values[2]), Color.Red);
            }
        }

        private void FrmSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                SF.frmSearch.btnSearch.PerformClick();
            }
        }
    }
}
