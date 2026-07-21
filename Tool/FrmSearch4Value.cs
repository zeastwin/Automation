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
            dataGridView1.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "scope",
                HeaderText = "作用域",
                FillWeight = 35F,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            dataGridView1.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "ownerProcess",
                HeaderText = "所属流程",
                FillWeight = 55F,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            Width = 650;
            dataGridView1.RowHeadersVisible = false;
            dataGridView1.AutoGenerateColumns = false;


            Type dgvType = this.dataGridView1.GetType();
            PropertyInfo pi = dgvType.GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
            pi.SetValue(this.dataGridView1, true, null);

            dataGridView1.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        }

        private void FrmSearch_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
        }
       
        private void btnSearch_Click(object sender, EventArgs e)
        {
            dataGridView1.Rows.Clear();
            for (int k = 0; k < ValueConfigStore.ValueCapacity; k++)
            {
                if (!SF.valueStore.TryGetValueByIndex(k, out DicValue obj))
                {
                    continue;
                }
                bool matched;
                if (checkBox1.Checked)
                {
                    matched = checkBox2.Checked
                        ? obj.Name == textBox1.Text
                        : string.Equals(obj.Name, textBox1.Text, StringComparison.OrdinalIgnoreCase);
                }
                else
                {
                    matched = checkBox2.Checked
                        ? obj.Name.Contains(textBox1.Text)
                        : obj.Name.IndexOf(textBox1.Text, StringComparison.OrdinalIgnoreCase) >= 0;
                }
                if (!matched) continue;
                int rowIndex = dataGridView1.Rows.Add();
                DataGridViewRow row = dataGridView1.Rows[rowIndex];
                row.Cells[0].Value = k;
                row.Cells[1].Value = obj.Name;
                row.Cells[2].Value = obj.Scope;
                row.Cells[3].Value = AutomationPlatformHost.ResolveOwnerProcessName(obj.OwnerProcId);
            }

        }

        private void dataGridView1_CellMouseDoubleClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.RowIndex >= 0 && e.RowIndex < dataGridView1.Rows.Count)
            {
                DataGridViewCell cell = dataGridView1.Rows[e.RowIndex].Cells[0];
                if (!int.TryParse(Convert.ToString(cell.Value), out int rowIndex)
                    || rowIndex < 0 || rowIndex >= ValueConfigStore.ValueCapacity)
                {
                    return;
                }
                SF.frmValue.LocateValueIndex(rowIndex);
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
