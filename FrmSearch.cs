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
using Newtonsoft.Json;
using static Automation.OperationTypePartial;

namespace Automation
{
    public partial class FrmSearch : Form
    {
        private CancellationTokenSource _searchCts;
        private readonly Label statusLabel = new Label();

        public FrmSearch()
        {
            InitializeComponent();
            dataGridView1.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dataGridView1.Columns[0].SortMode = DataGridViewColumnSortMode.NotSortable;
            dataGridView1.RowHeadersVisible = false;
            dataGridView1.AutoGenerateColumns = false;


            Type dgvType = this.dataGridView1.GetType();
            PropertyInfo pi = dgvType.GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
            pi.SetValue(this.dataGridView1, true, null);

            dataGridView1.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            ApplySearchStyle();
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
            string keyword = textBox1.Text.Trim();
            if (keyword.Length == 0)
            {
                statusLabel.Text = "请输入搜索内容";
                textBox1.Focus();
                return;
            }

            _searchCts?.Cancel();
            CancellationTokenSource cts = new CancellationTokenSource();
            _searchCts = cts;
            btnSearch.Enabled = false;
            statusLabel.Text = "正在生成搜索快照...";

            bool isExactMatch = checkBox1.Checked;
            StringComparison comparison = checkBox2.Checked ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            List<SearchTarget> targets = new List<SearchTarget>();
            for (int i = 0; i < SF.frmProc.procsList.Count; i++)
            {
                Proc proc = SF.frmProc.procsList[i];
                if (proc?.steps == null)
                {
                    continue;
                }
                for (int j = 0; j < proc.steps.Count; j++)
                {
                    Step step = proc.steps[j];
                    if (step?.Ops == null)
                    {
                        continue;
                    }
                    for (int k = 0; k < step.Ops.Count; k++)
                    {
                        OperationType operation = step.Ops[k];
                        if (operation == null)
                        {
                            continue;
                        }
                        targets.Add(new SearchTarget
                        {
                            ProcIndex = i,
                            StepIndex = j,
                            OpIndex = k,
                            Name = operation.Name ?? string.Empty,
                            Fields = BuildSearchFields(operation)
                        });
                    }
                }
            }

            try
            {
                List<SearchResult> results = await Task.Run(() =>
                {
                    List<SearchResult> list = new List<SearchResult>();
                    foreach (SearchTarget target in targets)
                    {
                        cts.Token.ThrowIfCancellationRequested();
                        List<SearchField> matchedFields = new List<SearchField>();
                        foreach (SearchField field in target.Fields)
                        {
                            cts.Token.ThrowIfCancellationRequested();
                            bool isMatch = isExactMatch
                                ? string.Equals(field.Value, keyword, comparison)
                                : field.Value.IndexOf(keyword, comparison) >= 0;
                            if (isMatch)
                            {
                                matchedFields.Add(field);
                            }
                        }
                        if (matchedFields.Count == 0)
                        {
                            continue;
                        }
                        string matchSummary = string.Join("；", matchedFields.Take(3)
                            .Select(field => $"{field.Name}={TrimDisplayValue(field.Value)}"));
                        if (matchedFields.Count > 3)
                        {
                            matchSummary += $"；另有{matchedFields.Count - 3}项";
                        }
                        list.Add(new SearchResult
                        {
                            Name = target.Name,
                            Position = $"{target.ProcIndex}-{target.StepIndex}-{target.OpIndex}",
                            MatchSummary = matchSummary
                        });
                    }

                    return list;
                }, cts.Token);

                if (!ReferenceEquals(_searchCts, cts) || cts.IsCancellationRequested)
                    return;

                dataGridView1.Rows.Clear();
                for (int i = 0; i < results.Count; i++)
                {
                    dataGridView1.Rows.Add();
                    dataGridView1.Rows[i].Cells[0].Value = i + 1;
                    dataGridView1.Rows[i].Cells[1].Value = results[i].Name;
                    dataGridView1.Rows[i].Cells[2].Value = results[i].Position + "  " + results[i].MatchSummary;
                    dataGridView1.Rows[i].Tag = results[i].Position;
                }
                statusLabel.Text = $"共扫描 {targets.Count} 条指令，找到 {results.Count} 条结果";
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                statusLabel.Text = "搜索失败：" + ex.Message;
                SF.DR?.Logger?.Log($"流程搜索失败:{ex}", LogLevel.Error);
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
                string[] values = Convert.ToString(dataGridView1.Rows[e.RowIndex].Tag)?.Split('-');
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
            public string Name { get; set; }
            public string Position { get; set; }
            public string MatchSummary { get; set; }
        }

        private void ApplySearchStyle()
        {
            panel1.BackColor = Color.FromArgb(245, 247, 250);
            btnSearch.FlatStyle = FlatStyle.Flat;
            btnSearch.FlatAppearance.BorderColor = Color.FromArgb(63, 126, 181);
            btnSearch.FlatAppearance.BorderSize = 1;
            btnSearch.FlatAppearance.MouseOverBackColor = Color.FromArgb(52, 110, 164);
            btnSearch.FlatAppearance.MouseDownBackColor = Color.FromArgb(40, 91, 138);
            btnSearch.BackColor = Color.FromArgb(63, 126, 181);
            btnSearch.ForeColor = Color.White;
            btnSearch.Cursor = Cursors.Hand;
            dataGridView1.BackgroundColor = Color.White;
            dataGridView1.BorderStyle = BorderStyle.None;
            dataGridView1.GridColor = Color.FromArgb(222, 228, 234);
            dataGridView1.EnableHeadersVisualStyles = false;
            dataGridView1.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(238, 243, 248);
            dataGridView1.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(48, 63, 78);
            dataGridView1.DefaultCellStyle.SelectionBackColor = Color.FromArgb(217, 234, 250);
            dataGridView1.DefaultCellStyle.SelectionForeColor = Color.FromArgb(27, 43, 59);
            dataGridView1.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 252);

            statusLabel.Dock = DockStyle.Bottom;
            statusLabel.Height = 24;
            statusLabel.Padding = new Padding(6, 0, 6, 0);
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            statusLabel.ForeColor = Color.DimGray;
            statusLabel.BackColor = Color.FromArgb(245, 247, 250);
            statusLabel.Text = "输入内容后按 Enter 搜索，双击结果定位";
            Controls.Add(statusLabel);
            statusLabel.BringToFront();
        }

        private static List<SearchField> BuildSearchFields(OperationType operation)
        {
            var fields = new List<SearchField>();
            foreach (PropertyDescriptor descriptor in TypeDescriptor.GetProperties(operation).Cast<PropertyDescriptor>())
            {
                object value;
                try
                {
                    value = descriptor.GetValue(operation);
                }
                catch
                {
                    continue;
                }
                if (value == null)
                {
                    continue;
                }

                string text;
                if (value is string || value.GetType().IsPrimitive || value is decimal || value is Enum)
                {
                    text = Convert.ToString(value);
                }
                else
                {
                    try
                    {
                        text = JsonConvert.SerializeObject(value);
                    }
                    catch
                    {
                        text = Convert.ToString(value);
                    }
                }
                if (!string.IsNullOrEmpty(text))
                {
                    fields.Add(new SearchField
                    {
                        Name = string.IsNullOrWhiteSpace(descriptor.DisplayName) ? descriptor.Name : descriptor.DisplayName,
                        Value = text
                    });
                }
            }
            return fields;
        }

        private static string TrimDisplayValue(string value)
        {
            const int maxLength = 80;
            string singleLine = (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ");
            return singleLine.Length <= maxLength ? singleLine : singleLine.Substring(0, maxLength) + "...";
        }

        private class SearchTarget
        {
            public int ProcIndex { get; set; }
            public int StepIndex { get; set; }
            public int OpIndex { get; set; }
            public string Name { get; set; }
            public List<SearchField> Fields { get; set; }
        }

        private class SearchField
        {
            public string Name { get; set; }
            public string Value { get; set; }
        }
    }
}
