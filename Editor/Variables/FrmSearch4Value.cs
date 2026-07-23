// 模块：编辑器 / 变量。
// 职责范围：变量配置、运行值调试、变量选择和提交规则。

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
            string searchText = textBox1.Text ?? string.Empty;
            Dictionary<Guid, string> processNames = Workspace.Runtime.Stores.Processes.Items
                .Where(proc => proc?.head != null && proc.head.Id != Guid.Empty)
                .GroupBy(proc => proc.head.Id)
                .ToDictionary(group => group.Key, group => group.First().head.Name ?? string.Empty);
            dataGridView1.SuspendLayout();
            try
            {
                dataGridView1.Rows.Clear();
                for (int k = 0; k < ValueConfigStore.ValueCapacity; k++)
                {
                    if (!Workspace.Runtime.Stores.Values.TryGetValueByIndex(k, out DicValue obj))
                    {
                        continue;
                    }
                    string variableName = obj.Name ?? string.Empty;
                    bool matched;
                    if (checkBox1.Checked)
                    {
                        matched = checkBox2.Checked
                            ? string.Equals(variableName, searchText, StringComparison.Ordinal)
                            : string.Equals(variableName, searchText, StringComparison.OrdinalIgnoreCase);
                    }
                    else
                    {
                        matched = variableName.IndexOf(
                            searchText,
                            checkBox2.Checked ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase) >= 0;
                    }
                    if (!matched) continue;
                    int rowIndex = dataGridView1.Rows.Add();
                    DataGridViewRow row = dataGridView1.Rows[rowIndex];
                    row.Cells[0].Value = k;
                    row.Cells[1].Value = variableName;
                    row.Cells[2].Value = obj.Scope;
                    row.Cells[3].Value = obj.OwnerProcId.HasValue
                        && processNames.TryGetValue(obj.OwnerProcId.Value, out string processName)
                            ? processName
                            : string.Empty;
                }
            }
            finally
            {
                dataGridView1.ResumeLayout();
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
                Workspace.Value.LocateValueIndex(rowIndex);
            }
        }

        private void FrmSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                Workspace.Search4Value.btnSearch.PerformClick();
            }
        }
    }
}
