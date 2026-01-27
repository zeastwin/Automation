using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace Automation
{
    public partial class FrmValue : Form
    {  //存放变量对象
        private sealed class CommonValueItem
        {
            public int Index { get; set; }
            public string Name { get; set; }

            public override string ToString()
            {
                string text = string.IsNullOrWhiteSpace(Name) ? $"索引{Index}" : Name;
                return $"{Index:D3}  {text}";
            }
        }

        private sealed class ValueClipboardItem
        {
            public string Name { get; set; }
            public string Type { get; set; }
            public string Value { get; set; }
            public string Note { get; set; }
        }

        private sealed class ValueMonitorForm : Form
        {
            private readonly FrmValue owner;
            private readonly Panel topPanel;
            private readonly Button btnAdd;
            private readonly Button btnRemove;
            private readonly Label labelTitle;
            private readonly DataGridView dgvMonitor;

            public ValueMonitorForm(FrmValue owner)
            {
                this.owner = owner;
                Font uiFont = new Font("黑体", 12F, FontStyle.Regular, GraphicsUnit.Point, ((byte)(134)));
                Text = "变量监控";
                StartPosition = FormStartPosition.CenterScreen;
                Size = new Size(780, 520);
                MinimumSize = new Size(680, 400);

                topPanel = new Panel
                {
                    Dock = DockStyle.Top,
                    Height = 40,
                    BackColor = SystemColors.ControlLight
                };

                labelTitle = new Label
                {
                    Dock = DockStyle.Left,
                    Width = 180,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Font = new Font("黑体", 12.5F, FontStyle.Bold, GraphicsUnit.Point, ((byte)(134))),
                    Text = "变量监控(0)"
                };

                btnAdd = new Button
                {
                    Text = "添加当前",
                    Font = uiFont,
                    Size = new Size(96, 30),
                    Anchor = AnchorStyles.Top | AnchorStyles.Right,
                    Location = new Point(300, 5)
                };
                btnAdd.Click += (s, e) => owner.AddMonitorFromSelection();

                btnRemove = new Button
                {
                    Text = "移除选中",
                    Font = uiFont,
                    Size = new Size(96, 30),
                    Anchor = AnchorStyles.Top | AnchorStyles.Right,
                    Location = new Point(400, 5)
                };
                btnRemove.Click += (s, e) => owner.RemoveMonitorFromMonitorSelection();

                topPanel.Controls.Add(btnRemove);
                topPanel.Controls.Add(btnAdd);
                topPanel.Controls.Add(labelTitle);

                dgvMonitor = new DataGridView
                {
                    Dock = DockStyle.Fill,
                    AllowUserToAddRows = false,
                    AllowUserToResizeRows = false,
                    AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                    BackgroundColor = SystemColors.ControlLight,
                    ColumnHeadersHeight = 32,
                    ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                    ReadOnly = true,
                    RowHeadersVisible = false,
                    RowTemplate = { Height = 26 },
                    SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                    Font = uiFont
                };
                dgvMonitor.ColumnHeadersDefaultCellStyle.Font = new Font("黑体", 12F, FontStyle.Bold, GraphicsUnit.Point, ((byte)(134)));
                dgvMonitor.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                dgvMonitor.Columns.Add(new DataGridViewTextBoxColumn
                {
                    Name = "monitorIndex",
                    HeaderText = "编号",
                    MinimumWidth = 60,
                    FillWeight = 12
                });
                dgvMonitor.Columns.Add(new DataGridViewTextBoxColumn
                {
                    Name = "monitorName",
                    HeaderText = "名称",
                    MinimumWidth = 90,
                    FillWeight = 18
                });
                dgvMonitor.Columns.Add(new DataGridViewTextBoxColumn
                {
                    Name = "monitorValue",
                    HeaderText = "值",
                    MinimumWidth = 80,
                    FillWeight = 16
                });
                dgvMonitor.Columns.Add(new DataGridViewTextBoxColumn
                {
                    Name = "monitorSource",
                    HeaderText = "来源",
                    MinimumWidth = 120,
                    FillWeight = 24
                });
                dgvMonitor.Columns.Add(new DataGridViewTextBoxColumn
                {
                    Name = "monitorTime",
                    HeaderText = "时间",
                    MinimumWidth = 140,
                    FillWeight = 30
                });

                Controls.Add(dgvMonitor);
                Controls.Add(topPanel);
            }

            public DataGridView Grid => dgvMonitor;
            public Label TitleLabel => labelTitle;

            protected override void OnFormClosing(FormClosingEventArgs e)
            {
                e.Cancel = true;
                owner.StopAllMonitor();
                Hide();
            }
        }

        private readonly HashSet<int> monitorIndexSet = new HashSet<int>();
        private ValueClipboardItem clipboardItem;
        private bool isValueStoreHooked;
        private ValueMonitorForm monitorForm;
        private bool isStructViewAttached;

        public FrmValue()
        {
            InitializeComponent();
            Font uiFont = new Font("黑体", 12F, FontStyle.Regular, GraphicsUnit.Point, ((byte)(134)));
            dgvValue.Font = uiFont;
            dgvValue.ColumnHeadersDefaultCellStyle.Font = new Font("黑体", 12F, FontStyle.Bold, GraphicsUnit.Point, ((byte)(134)));
            dgvValue.SelectionMode = DataGridViewSelectionMode.ColumnHeaderSelect;
            dgvValue.Columns[0].SortMode = DataGridViewColumnSortMode.NotSortable;
            dgvValue.Columns[0].ReadOnly = true;
            dgvValue.RowHeadersVisible = false;
            dgvValue.AutoGenerateColumns = false;


            Type dgvType = this.dgvValue.GetType();
            PropertyInfo pi = dgvType.GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
            pi.SetValue(this.dgvValue, true, null);

            dgvValue.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            dgvValue.RowTemplate.Height = 28;
        }

        private void FrmValue_FormClosing(object sender, FormClosingEventArgs e)
        {
            e.Cancel = true;
            this.Hide();
            if (monitorForm != null && !monitorForm.IsDisposed)
            {
                StopAllMonitor();
                monitorForm.Hide();
            }
        }
        //从文件更新变量表
        public void RefreshDic()
        {
            SF.valueStore.Load(SF.ConfigPath);
            EnsureValueStoreHooked();

            RefreshValue();

            SF.isFinBulidFrmValue = true;

        }
  
        public void RefreshValue()
        {
            dgvValue.Rows.Clear();

            for (int i = 0; i < ValueConfigStore.ValueCapacity; i++)
            {
                dgvValue.Rows.Add();

                dgvValue.Rows[i].Cells[0].Value = i;
                if (SF.valueStore.TryGetValueByIndex(i, out DicValue cachedValue))
                {
                    dgvValue.Rows[i].Cells[1].Value = cachedValue.Name;
                    dgvValue.Rows[i].Cells[2].Value = cachedValue.Type;
                    dgvValue.Rows[i].Cells[3].Value = cachedValue.Value;
                    dgvValue.Rows[i].Cells[4].Value = cachedValue.Note;
                }
            }
            RefreshCommonList();
            RefreshMonitorTitle();
            RefreshMonitorRows();
        }
        //刷新变量界面
        public void FreshFrmValue()
        {
            for (int i = 0; i < ValueConfigStore.ValueCapacity; i++)
            {
                if (SF.valueStore.TryGetValueByIndex(i, out DicValue cachedValue))
                {
                    dgvValue.Rows[i].Cells[0].Value = i;
                    dgvValue.Rows[i].Cells[1].Value = cachedValue.Name;
                    dgvValue.Rows[i].Cells[2].Value = cachedValue.Type;
                    dgvValue.Rows[i].Cells[3].Value = cachedValue.Value;
                    dgvValue.Rows[i].Cells[4].Value = cachedValue.Note;
                }
            }
            RefreshCommonList();
            RefreshMonitorTitle();
            RefreshMonitorRows();

        }
        /*=============================================================================================*/
        private void FrmValue_Load(object sender, EventArgs e)
        {
            EnsureValueStoreHooked();
            AttachDataStructView();
            SetDefaultStructPanelRatio();
        }

        private void AttachDataStructView()
        {
            if (isStructViewAttached || panelStructHost == null)
            {
                return;
            }
            if (SF.frmdataStruct == null || SF.frmdataStruct.IsDisposed)
            {
                return;
            }

            SF.frmdataStruct.TopLevel = false;
            SF.frmdataStruct.FormBorderStyle = FormBorderStyle.None;
            SF.frmdataStruct.Dock = DockStyle.Fill;

            if (!panelStructHost.Controls.Contains(SF.frmdataStruct))
            {
                panelStructHost.Controls.Add(SF.frmdataStruct);
            }

            SF.frmdataStruct.Show();
            SF.frmdataStruct.BringToFront();
            isStructViewAttached = true;
        }

        private void SetDefaultStructPanelRatio()
        {
            if (splitContainerMain == null || splitContainerMain.Width <= 0)
            {
                return;
            }
            int targetLeftWidth = (int)(splitContainerMain.Width * 0.75);
            int minLeftWidth = splitContainerMain.Panel1MinSize;
            int maxLeftWidth = splitContainerMain.Width - splitContainerMain.Panel2MinSize - splitContainerMain.SplitterWidth;
            if (targetLeftWidth < minLeftWidth)
            {
                targetLeftWidth = minLeftWidth;
            }
            if (targetLeftWidth > maxLeftWidth)
            {
                targetLeftWidth = maxLeftWidth;
            }
            splitContainerMain.SplitterDistance = targetLeftWidth;
        }

        private void dgvValue_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (SF.isFinBulidFrmValue)
            {
                // 确保值变化发生在单元格中而不是在行标题或列标题
                if (e.RowIndex >= 0 && e.ColumnIndex >= 0)
                {
                    if (e.ColumnIndex == 2)
                    {
                        DataGridViewTextBoxCell textboxCell = new DataGridViewTextBoxCell();
                        dgvValue[e.ColumnIndex + 1, e.RowIndex] = textboxCell;
                    }
                   
                }
            }
           
        }

        private void dgvValue_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true; // 阻止默认行为 防止选择条向下切换
                return;
            }
            if (dgvValue.IsCurrentCellInEditMode)
            {
                return;
            }
            if (e.Control && e.KeyCode == Keys.C)
            {
                CopySelectedValueRow();
                e.Handled = true;
                return;
            }
            if (e.Control && e.KeyCode == Keys.V)
            {
                PasteToSelectedValueRow();
                e.Handled = true;
                return;
            }
            if (e.KeyCode == Keys.Delete)
            {
                ClearSelectedValueRows();
                e.Handled = true;
                return;
            }
        }

        private bool TryGetSingleSelectedRowIndex(out int rowIndex)
        {
            rowIndex = -1;
            if (dgvValue.SelectedRows != null && dgvValue.SelectedRows.Count > 0)
            {
                if (dgvValue.SelectedRows.Count > 1)
                {
                    MessageBox.Show("一次只能操作一行");
                    return false;
                }
                rowIndex = dgvValue.SelectedRows[0].Index;
                return rowIndex >= 0;
            }
            if (dgvValue.CurrentCell == null)
            {
                MessageBox.Show("没有选定的变量");
                return false;
            }
            rowIndex = dgvValue.CurrentCell.RowIndex;
            return rowIndex >= 0;
        }

        private List<int> GetSelectedRowIndexes()
        {
            HashSet<int> indexes = new HashSet<int>();
            if (dgvValue.SelectedRows != null && dgvValue.SelectedRows.Count > 0)
            {
                foreach (DataGridViewRow row in dgvValue.SelectedRows)
                {
                    if (row != null && row.Index >= 0)
                    {
                        indexes.Add(row.Index);
                    }
                }
            }
            else if (dgvValue.CurrentCell != null)
            {
                indexes.Add(dgvValue.CurrentCell.RowIndex);
            }
            return new List<int>(indexes);
        }

        private bool TryValidateClipboardData(string name, string type, string value, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(name))
            {
                error = "名称为空";
                return false;
            }
            if (type != "double" && type != "string")
            {
                error = "类型无效";
                return false;
            }
            if (string.IsNullOrEmpty(value))
            {
                error = "值为空";
                return false;
            }
            if (type == "double" && !double.TryParse(value, out _))
            {
                error = "值不是有效数字";
                return false;
            }
            return true;
        }

        private void CopySelectedValueRow()
        {
            if (!TryGetSingleSelectedRowIndex(out int rowIndex))
            {
                return;
            }
            if (!SF.valueStore.TryGetValueByIndex(rowIndex, out DicValue value))
            {
                MessageBox.Show("当前行没有可复制的变量");
                return;
            }
            if (!TryValidateClipboardData(value.Name, value.Type, value.Value, out string error))
            {
                MessageBox.Show($"复制失败:{error}");
                return;
            }
            clipboardItem = new ValueClipboardItem
            {
                Name = value.Name,
                Type = value.Type,
                Value = value.Value,
                Note = value.Note
            };
        }

        private void PasteToSelectedValueRow()
        {
            if (clipboardItem == null)
            {
                MessageBox.Show("没有可粘贴的数据");
                return;
            }
            if (!TryGetSingleSelectedRowIndex(out int rowIndex))
            {
                return;
            }
            DataGridViewRow row = dgvValue.Rows[rowIndex];
            string nameToUse = BuildPasteName(clipboardItem.Name);
            if (string.IsNullOrEmpty(nameToUse))
            {
                MessageBox.Show("粘贴名称无效");
                return;
            }
            if (!TryValidateClipboardData(nameToUse, clipboardItem.Type, clipboardItem.Value, out string error))
            {
                MessageBox.Show($"粘贴失败:{error}");
                return;
            }
            if (!SF.valueStore.TrySetValue(rowIndex, nameToUse, clipboardItem.Type, clipboardItem.Value, clipboardItem.Note, "变量表粘贴"))
            {
                MessageBox.Show("粘贴失败:名称已存在或数据无效");
                return;
            }
            row.Cells[1].Value = nameToUse;
            row.Cells[2].Value = clipboardItem.Type;
            row.Cells[3].Value = clipboardItem.Value;
            row.Cells[4].Value = clipboardItem.Note;
        }

        private string BuildPasteName(string baseName)
        {
            if (string.IsNullOrWhiteSpace(baseName))
            {
                return null;
            }
            string name = baseName.Trim();
            int suffix = 1;
            while (suffix < 100000)
            {
                string candidate = $"{name}{suffix}";
                if (!SF.valueStore.TryGetValueByName(candidate, out _))
                {
                    return candidate;
                }
                suffix++;
            }
            return null;
        }

        private void ClearSelectedValueRows()
        {
            List<int> indexes = GetSelectedRowIndexes();
            if (indexes.Count == 0)
            {
                MessageBox.Show("没有选定的变量");
                return;
            }
            bool hasFailure = false;
            foreach (int index in indexes)
            {
                if (!SF.valueStore.ClearValueByIndex(index, "变量表清除"))
                {
                    hasFailure = true;
                    continue;
                }
                DataGridViewRow row = dgvValue.Rows[index];
                row.Cells[1].Value = null;
                row.Cells[2].Value = null;
                row.Cells[3].Value = null;
                row.Cells[4].Value = null;
            }
            RefreshCommonList();
            if (hasFailure)
            {
                MessageBox.Show("清除数据失败");
            }
        }

        public bool CheckRowCellsHaveValue(DataGridView dataGridView, int rowIndex)
        {
            int colsCount = dataGridView.ColumnCount;

            for (int colIndex = 0; colIndex < colsCount-1; colIndex++)
            {
                object cellValue = dataGridView.Rows[rowIndex].Cells[colIndex].Value;
                if (cellValue == null || cellValue.ToString() == "")
                {
                    return false;
                }
            }

            return true;
        }

        private void dgvValue_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (SF.isFinBulidFrmValue)
            {
                // 确保值变化发生在单元格中而不是在行标题或列标题
                if (e.RowIndex >= 0 && e.ColumnIndex >= 0)
                {
                    DataGridView dataGridView = (DataGridView)sender;
                    bool isEffective = CheckRowCellsHaveValue(dataGridView,e.RowIndex);
                    SF.isEndEdit = isEffective;
                    if (isEffective)
                    {
                        int num = (int)dataGridView.Rows[e.RowIndex].Cells[0].Value;
                        string key = (string)dataGridView.Rows[e.RowIndex].Cells[1].Value;
                        string type = (string)dataGridView.Rows[e.RowIndex].Cells[2].Value; 
                        string note = (string)dataGridView.Rows[e.RowIndex].Cells[4].Value;

                        if (string.IsNullOrEmpty(key))
                        {
                            dgvValue[1, e.RowIndex].Value = null;
                            dgvValue[2, e.RowIndex].Value = null;
                            dgvValue[3, e.RowIndex].Value = null;
                            dgvValue[4, e.RowIndex].Value = null;
                            return;
                        }

                        if (type != "double" && type != "string")
                        {
                            dgvValue[1, e.RowIndex].Value = null;
                            dgvValue[2, e.RowIndex].Value = null;
                            dgvValue[3, e.RowIndex].Value = null;
                            dgvValue[4, e.RowIndex].Value = null;
                            return;
                        }

                        string value = dataGridView.Rows[e.RowIndex].Cells[3].Value?.ToString();
                        if (string.IsNullOrEmpty(value))
                        {
                            dgvValue[1, e.RowIndex].Value = null;
                            dgvValue[2, e.RowIndex].Value = null;
                            dgvValue[3, e.RowIndex].Value = null;
                            dgvValue[4, e.RowIndex].Value = null;
                            return;
                        }

                        if (type == "double")
                        {
                            if (!double.TryParse(value, out double doubleValue2))
                            {
                            
                                dgvValue[1,e.RowIndex].Value = null;
                                dgvValue[2,e.RowIndex].Value = null;
                                dgvValue[3,e.RowIndex].Value = null;
                                dgvValue[4,e.RowIndex].Value = null;         
                                return;
                            }
                        }
                        if (!SF.valueStore.TrySetValue(num, key, type, value, note, "变量表编辑"))
                        {
                            dgvValue[1, e.RowIndex].Value = null;
                            dgvValue[2, e.RowIndex].Value = null;
                            dgvValue[3, e.RowIndex].Value = null;
                            dgvValue[4, e.RowIndex].Value = null;
                            return;
                        }
                    }

                }
            }
        }

        private void dgvValue_CellMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && SF.isEndEdit==true)
                FreshFrmValue();
        }

        private void btnSet_Click(object sender, EventArgs e)
        {
            if (dgvValue.CurrentCell != null)
            {
                // 获取当前选定单元格的行列索引
                int selectedRowIndex = dgvValue.CurrentCell.RowIndex;
                int selectedColumnIndex = dgvValue.CurrentCell.ColumnIndex;
                if(selectedColumnIndex == 0 && selectedRowIndex >= 0)
                {
                    SF.valueStore.ToggleMark(selectedRowIndex);
                    SF.valueStore.Save(SF.ConfigPath);
                    RefreshCommonList();
                }
            }
            else
            {
                MessageBox.Show("没有选定的单元格");
            }
        }
        int currentIndex = -1;
        private void btnPrevious_Click(object sender, EventArgs e)
        {
            int previousIndex = -1;
            int startIndex = currentIndex - 1;
            if (startIndex < 0)
            {
                startIndex = ValueConfigStore.ValueCapacity - 1;
            }
            for (int i = startIndex; i >= 0; i--)
            {
                if (SF.valueStore.IsMarked(i))
                {
                    previousIndex = i;
                    break;
                }
            }
            if (previousIndex == -1)
            {
                for (int i = ValueConfigStore.ValueCapacity - 1; i >= 0; i--)
                {
                    if (SF.valueStore.IsMarked(i))
                    {
                        previousIndex = i;
                        break;
                    }
                }
            }
            if (previousIndex != -1)
            {
                currentIndex = previousIndex;
                dgvValue.FirstDisplayedScrollingRowIndex = currentIndex;
            }

        }

        private void BtnNext_Click(object sender, EventArgs e)
        {
            int nextIndex = -1;
            int startIndex = currentIndex + 1;
            if (startIndex < 0)
            {
                startIndex = 0;
            }
            for (int i = startIndex; i < ValueConfigStore.ValueCapacity; i++)
            {
                if (SF.valueStore.IsMarked(i))
                {
                    nextIndex = i;
                    break;
                }
            }
            if (nextIndex == -1)
            {
                for (int i = 0; i < ValueConfigStore.ValueCapacity; i++)
                {
                    if (SF.valueStore.IsMarked(i))
                    {
                        nextIndex = i;
                        break;
                    }
                }
            }
            if (nextIndex != -1)
            {
                currentIndex = nextIndex;
                dgvValue.FirstDisplayedScrollingRowIndex = currentIndex;
            }
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            SF.valueStore.ClearMarks();
            SF.valueStore.Save(SF.ConfigPath);
            dgvValue.Refresh();
            RefreshCommonList();
        }

        private void btnCopy_Click(object sender, EventArgs e)
        {
            CopySelectedValueRow();
        }

        private void btnPaste_Click(object sender, EventArgs e)
        {
            PasteToSelectedValueRow();
        }

        private void btnClearData_Click(object sender, EventArgs e)
        {
            ClearSelectedValueRows();
        }

        private void dgvValue_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.ColumnIndex == 4)
            {
                dgvValue[e.ColumnIndex, e.RowIndex].Style.Alignment = DataGridViewContentAlignment.MiddleLeft;
            }
            else
            {
                dgvValue[e.ColumnIndex, e.RowIndex].Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            }

            if (e.ColumnIndex == 0 && e.RowIndex >= 0)
            {
                // 获取当前行对应的数据项
                if (SF.valueStore.IsMarked(e.RowIndex))
                {
                    //  SetRowColor(e.RowIndex, dataGridView1.DefaultCellStyle.BackColor);
                    e.CellStyle.BackColor = Color.Red;
                }
                else
                {
                    // 如果不是断点，保持默认颜色
                    // SetRowColor(e.RowIndex, dataGridView1.DefaultCellStyle.BackColor);
                    e.CellStyle.BackColor = Color.White;
                }
            }

        }

        private void btnSearch_Click(object sender, EventArgs e)
        {
            SF.frmSearch4Value.StartPosition = FormStartPosition.CenterScreen;
            SF.frmSearch4Value.Show();
            SF.frmSearch4Value.BringToFront();
            SF.frmSearch4Value.WindowState = FormWindowState.Normal;
            SF.frmSearch4Value.textBox1.Focus();
        }

        private void RefreshCommonList()
        {
            if (listCommon == null)
            {
                return;
            }
            listCommon.BeginUpdate();
            listCommon.Items.Clear();
            int count = 0;
            for (int i = 0; i < ValueConfigStore.ValueCapacity; i++)
            {
                if (!SF.valueStore.IsMarked(i))
                {
                    continue;
                }
                string name = null;
                if (SF.valueStore.TryGetValueByIndex(i, out DicValue value))
                {
                    name = value?.Name;
                }
                listCommon.Items.Add(new CommonValueItem
                {
                    Index = i,
                    Name = name
                });
                count++;
            }
            labelCommon.Text = $"常用变量({count})";
            listCommon.EndUpdate();
        }

        private void EnsureValueStoreHooked()
        {
            if (isValueStoreHooked)
            {
                return;
            }
            if (SF.valueStore == null)
            {
                return;
            }
            SF.valueStore.ValueChanged += ValueStore_ValueChanged;
            isValueStoreHooked = true;
        }

        private void EnsureMonitorForm()
        {
            if (monitorForm != null && !monitorForm.IsDisposed)
            {
                return;
            }
            monitorForm = new ValueMonitorForm(this);
            monitorForm.Owner = this;
        }

        private void ValueStore_ValueChanged(object sender, ValueChangedEventArgs e)
        {
            if (IsHandleCreated && InvokeRequired)
            {
                BeginInvoke(new Action<object, ValueChangedEventArgs>(ValueStore_ValueChanged), sender, e);
                return;
            }
            if (e == null)
            {
                return;
            }
            if (!monitorIndexSet.Contains(e.Index))
            {
                return;
            }
            if (monitorForm == null || monitorForm.IsDisposed)
            {
                return;
            }
            DataGridView grid = monitorForm.Grid;
            int rowIndex = grid.Rows.Add();
            DataGridViewRow row = grid.Rows[rowIndex];
            row.Cells[0].Value = e.Index;
            row.Cells[1].Value = e.Name;
            row.Cells[2].Value = e.NewValue;
            row.Cells[3].Value = e.Source;
            row.Cells[4].Value = e.ChangedAt.ToString("yyyy-MM-dd HH:mm:ss");
        }

        private void RefreshMonitorTitle()
        {
            if (monitorForm == null || monitorForm.IsDisposed)
            {
                return;
            }
            monitorForm.TitleLabel.Text = $"变量监控({monitorIndexSet.Count})";
        }

        private void RefreshMonitorRows()
        {
            if (monitorForm == null || monitorForm.IsDisposed)
            {
                return;
            }
            // 监控改为记录历史，不刷新既有记录
        }

        private void AddMonitor(int index)
        {
            if (index < 0 || index >= ValueConfigStore.ValueCapacity)
            {
                return;
            }
            EnsureMonitorForm();
            DataGridView grid = monitorForm.Grid;
            if (monitorIndexSet.Contains(index))
            {
                return;
            }
            if (!SF.valueStore.TryGetValueByIndex(index, out DicValue value))
            {
                return;
            }
            SF.valueStore.SetMonitorFlag(index, true);
            monitorIndexSet.Add(index);
            int rowIndex = grid.Rows.Add();
            DataGridViewRow row = grid.Rows[rowIndex];
            row.Cells[0].Value = index;
            row.Cells[1].Value = value?.Name;
            row.Cells[2].Value = value?.Value;
            row.Cells[3].Value = value?.LastChangedBy;
            row.Cells[4].Value = value != null && value.LastChangedAt != default
                ? value.LastChangedAt.ToString("yyyy-MM-dd HH:mm:ss")
                : string.Empty;
            RefreshMonitorTitle();
        }

        private void RemoveMonitor(int index)
        {
            if (!monitorIndexSet.Contains(index))
            {
                return;
            }
            monitorIndexSet.Remove(index);
            SF.valueStore.SetMonitorFlag(index, false);
            RefreshMonitorTitle();
        }

        private void StopAllMonitor()
        {
            if (monitorIndexSet.Count == 0)
            {
                RefreshMonitorTitle();
                SF.valueStore.SetMonitorEnabled(false);
                return;
            }
            List<int> indexes = new List<int>(monitorIndexSet);
            foreach (int index in indexes)
            {
                SF.valueStore.SetMonitorFlag(index, false);
            }
            monitorIndexSet.Clear();
            if (monitorForm != null && !monitorForm.IsDisposed)
            {
                monitorForm.Grid.Rows.Clear();
            }
            RefreshMonitorTitle();
            SF.valueStore.SetMonitorEnabled(false);
        }

        private void btnMonitor_Click(object sender, EventArgs e)
        {
            EnsureMonitorForm();
            SF.valueStore.SetMonitorEnabled(true);
            monitorForm.Show();
            monitorForm.BringToFront();
            RefreshMonitorTitle();
            RefreshMonitorRows();
        }

        private void btnMonitorAdd_Click(object sender, EventArgs e)
        {
            AddMonitorFromSelection();
            if (monitorForm != null && !monitorForm.IsDisposed)
            {
                SF.valueStore.SetMonitorEnabled(true);
                monitorForm.Show();
                monitorForm.BringToFront();
            }
        }

        private void btnMonitorRemove_Click(object sender, EventArgs e)
        {
            if (dgvValue.CurrentCell == null)
            {
                MessageBox.Show("没有选定的变量");
                return;
            }
            int rowIndex = dgvValue.CurrentCell.RowIndex;
            RemoveMonitor(rowIndex);
        }

        private void AddMonitorFromSelection()
        {
            if (dgvValue.CurrentCell == null)
            {
                MessageBox.Show("没有选定的变量");
                return;
            }
            int rowIndex = dgvValue.CurrentCell.RowIndex;
            AddMonitor(rowIndex);
        }

        private void RemoveMonitorFromMonitorSelection()
        {
            if (monitorForm == null || monitorForm.IsDisposed)
            {
                MessageBox.Show("监控窗口未打开");
                return;
            }
            DataGridView grid = monitorForm.Grid;
            if (grid.CurrentRow == null)
            {
                MessageBox.Show("没有选定的监控项");
                return;
            }
            object cellValue = grid.CurrentRow.Cells[0].Value;
            if (cellValue == null || !int.TryParse(cellValue.ToString(), out int index))
            {
                MessageBox.Show("监控项编号无效");
                return;
            }
            RemoveMonitor(index);
        }

        private void listCommon_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (listCommon.SelectedItem is CommonValueItem item)
            {
                int index = item.Index;
                if (index >= 0 && index < dgvValue.Rows.Count)
                {
                    currentIndex = index;
                    dgvValue.ClearSelection();
                    dgvValue.CurrentCell = dgvValue.Rows[index].Cells[0];
                    dgvValue.Rows[index].Selected = true;
                    dgvValue.FirstDisplayedScrollingRowIndex = index;
                }
            }
        }

    }
}
