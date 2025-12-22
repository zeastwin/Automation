using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace Automation
{
    public partial class FrmValue : Form
    {  //存放变量对象

        public FrmValue()
        {
            InitializeComponent();
            dgvValue.SelectionMode = DataGridViewSelectionMode.ColumnHeaderSelect;
            dgvValue.Columns[0].SortMode = DataGridViewColumnSortMode.NotSortable;
            dgvValue.Columns[0].ReadOnly = true;
            dgvValue.RowHeadersVisible = false;
            dgvValue.AutoGenerateColumns = false;
        

            Type dgvType = this.dgvValue.GetType();
            PropertyInfo pi = dgvType.GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
            pi.SetValue(this.dgvValue, true, null);

            dgvValue.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            dgvValue.RowTemplate.Height = 20;
        }

        private void FrmValue_FormClosing(object sender, FormClosingEventArgs e)
        {
            e.Cancel = true;
            this.Hide();
        }
        //从文件更新变量表
        public void RefreshDic()
        {
            SF.valueStore.Load(SF.ConfigPath);

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

        }
        /*=============================================================================================*/
        private void FrmValue_Load(object sender, EventArgs e)
        {
          
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
                        if (!SF.valueStore.TrySetValue(num, key, type, value, note))
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
        }

        private void dgvValue_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            dgvValue[e.ColumnIndex, e.RowIndex].Style.Alignment = DataGridViewContentAlignment.MiddleCenter;

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

    }
}
