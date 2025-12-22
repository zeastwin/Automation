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
    public partial class FrmSearch4Value : Form
    {
        public FrmSearch4Value()
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
          //  SF.frmDataGrid.ClearAllRowColors();
            this.Hide();
        }
       
        private void btnSearch_Click(object sender, EventArgs e)
        {
            dataGridView1.Rows.Clear();
            for (int k = 0; k < FrmValue.ValueCapacity; k++)
            {
                DicValue obj = SF.frmValue.GetValueByIndex(k);

                if (string.IsNullOrEmpty(obj.Name))
                    continue;
                if (checkBox1.Checked)
                {
                    bool stringsAreEqual = checkBox2.Checked ? obj.Name == textBox1.Text : string.Equals(obj.Name.ToString(), textBox1.Text, StringComparison.OrdinalIgnoreCase);
                    if (stringsAreEqual)
                    {
                        dataGridView1.Rows.Add();
                        dataGridView1.Rows[dataGridView1.Rows.Count-1].Cells[0].Value = k;
                        dataGridView1.Rows[dataGridView1.Rows.Count - 1].Cells[1].Value = obj.Name;
                    }
                }
                else
                {
                    bool containsSubstring = checkBox2.Checked ? obj.Name.Contains(textBox1.Text) : obj.Name.ToString().ToLower().Contains(textBox1.Text.ToLower());
                    if (containsSubstring)
                    {
                        dataGridView1.Rows.Add();
                        dataGridView1.Rows[dataGridView1.Rows.Count - 1].Cells[0].Value = k;
                        dataGridView1.Rows[dataGridView1.Rows.Count - 1].Cells[1].Value = obj.Name;
                    }
                }
            }

        }

        private void dataGridView1_CellMouseDoubleClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.RowIndex >= 0)
            {
                DataGridViewCell cell = dataGridView1.Rows[e.RowIndex].Cells[0];
                int cellValue = (int)cell.Value;

                SF.frmValue.dgvValue.FirstDisplayedScrollingRowIndex = cellValue;
            }
        }

        private void FrmSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                SF.frmSearch4Value.btnSearch.PerformClick();
            }
        }
    }
}
