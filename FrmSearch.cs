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
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static Automation.OperationTypePartial;

namespace Automation
{
    public partial class FrmSearch : Form
    {
        private CancellationTokenSource _searchCts;

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
            _searchCts?.Cancel();
            SF.frmDataGrid.ClearAllRowColors();
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
        }
       
        private async void btnSearch_Click(object sender, EventArgs e)
        {
            dataGridView1.Rows.Clear();
            _searchCts?.Cancel();
            CancellationTokenSource cts = new CancellationTokenSource();
            _searchCts = cts;
            btnSearch.Enabled = false;

            string keyword = textBox1.Text;
            bool isExactMatch = checkBox1.Checked;
            StringComparison comparison = checkBox2.Checked ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            List<SearchTarget> targets = new List<SearchTarget>();
            for (int i = 0; i < SF.frmProc.procsList.Count; i++)
            {
                for (int j = 0; j < SF.frmProc.procsList[i].steps.Count; j++)
                {
                    for (int k = 0; k < SF.frmProc.procsList[i].steps[j].Ops.Count; k++)
                    {
                        targets.Add(new SearchTarget
                        {
                            ProcIndex = i,
                            StepIndex = j,
                            OpIndex = k,
                            Op = SF.frmProc.procsList[i].steps[j].Ops[k]
                        });
                    }
                }
            }

            try
            {
                List<SearchResult> results = await Task.Run(() =>
                {
                    List<SearchResult> list = new List<SearchResult>();
                    int count = 0;
                    foreach (SearchTarget target in targets)
                    {
                        cts.Token.ThrowIfCancellationRequested();
                        if (target.Op == null)
                            continue;
                        OperationType obj = target.Op;

                        Type objectType = obj.GetType();
                        PropertyInfo[] properties = objectType.GetProperties();

                        foreach (PropertyInfo property in properties)
                        {
                            cts.Token.ThrowIfCancellationRequested();
                            object propertyValue = property.GetValue(obj);
                            if (propertyValue == null)
                                continue;
                            string value = propertyValue.ToString();
                            bool isMatch = isExactMatch
                                ? string.Equals(value, keyword, comparison)
                                : value.IndexOf(keyword, comparison) >= 0;
                            if (isMatch)
                            {
                                list.Add(new SearchResult
                                {
                                    Index = count,
                                    Name = obj.Name,
                                    Position = $"{target.ProcIndex}-{target.StepIndex}-{target.OpIndex}"
                                });
                                count++;
                            }
                        }
                    }

                    return list;
                }, cts.Token);

                if (!ReferenceEquals(_searchCts, cts) || cts.IsCancellationRequested)
                    return;

                for (int i = 0; i < results.Count; i++)
                {
                    dataGridView1.Rows.Add();
                    dataGridView1.Rows[i].Cells[0].Value = results[i].Index;
                    dataGridView1.Rows[i].Cells[1].Value = results[i].Name;
                    dataGridView1.Rows[i].Cells[2].Value = results[i].Position;
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                if (ReferenceEquals(_searchCts, cts))
                {
                    _searchCts = null;
                    btnSearch.Enabled = true;
                }
                cts.Dispose();
            }

        }

        private void dataGridView1_CellMouseDoubleClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.RowIndex >= 0 && e.RowIndex < dataGridView1.Rows.Count)
            {
                DataGridViewCell cell = dataGridView1.Rows[e.RowIndex].Cells[2];
                string[] values = Convert.ToString(cell.Value)?.Split('-');
                if (values == null || values.Length != 3
                    || !int.TryParse(values[0], out int procIndex)
                    || !int.TryParse(values[1], out int stepIndex)
                    || !int.TryParse(values[2], out int opIndex))
                {
                    SF.frmInfo?.PrintInfo("搜索结果位置格式无效，无法定位流程。", FrmInfo.Level.Error);
                    return;
                }

                SF.frmDataGrid.SelectChildNode(procIndex, stepIndex);
                SF.frmDataGrid.ScrollRowToCenter(opIndex);
                SF.frmDataGrid.ClearAllRowColors();
                SF.frmDataGrid.SetRowColor(opIndex, Color.LightGreen);
            }
        }

        private void FrmSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                SF.frmSearch.btnSearch.PerformClick();
            }
        }

        private class SearchResult
        {
            public int Index { get; set; }
            public string Name { get; set; }
            public string Position { get; set; }
        }

        private class SearchTarget
        {
            public int ProcIndex { get; set; }
            public int StepIndex { get; set; }
            public int OpIndex { get; set; }
            public OperationType Op { get; set; }
        }
    }
}
