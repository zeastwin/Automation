using Newtonsoft.Json.Linq;
using System;
using System.Collections;
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
using static System.Windows.Forms.VisualStyles.VisualStyleElement.StartPanel;

namespace Automation
{
    public partial class FrmValue : Form
    {  //存放变量对象
        public const int ValueCapacity = 1000;

        private readonly object valueLock = new object();
        private readonly DicValue[] values = new DicValue[ValueCapacity];
        private readonly Dictionary<string, int> nameIndex = new Dictionary<string, int>();

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

            ResetValues();
        }

        private void FrmValue_FormClosing(object sender, FormClosingEventArgs e)
        {
            e.Cancel = true;
            this.Hide();
        }
        //从文件更新变量表
        public void RefreshDic()
        {
            if (!Directory.Exists(SF.ConfigPath))
            {
                Directory.CreateDirectory(SF.ConfigPath);
            }
            string valuePath = Path.Combine(SF.ConfigPath, "value.json");
            if (!File.Exists(valuePath))
            {
                Dictionary<string, DicValue> emptyValues = new Dictionary<string, DicValue>();
                SF.mainfrm.SaveAsJson(SF.ConfigPath, "value", emptyValues);
            }
            Dictionary<string, DicValue> loadValues = SF.mainfrm.ReadJson<Dictionary<string, DicValue>>(SF.ConfigPath, "value");
            LoadFromDictionary(loadValues);

            RefreshValue();

            SF.isFinBulidFrmValue = true;

        }
  
        public void RefreshValue()
        {
            dgvValue.Rows.Clear();

            for (int i = 0; i < ValueCapacity; i++)
            {
                dgvValue.Rows.Add();

                DicValue cachedValue = values[i];
                dgvValue.Rows[i].Cells[0].Value = i;
                if (!string.IsNullOrEmpty(cachedValue.Name))
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
            for (int i = 0; i < ValueCapacity; i++)
            {
                DicValue cachedValue = values[i];
                if (!string.IsNullOrEmpty(cachedValue.Name))
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

        private void ResetValues()
        {
            for (int i = 0; i < ValueCapacity; i++)
            {
                values[i] = new DicValue { Index = i };
            }
            nameIndex.Clear();
        }

        private void LoadFromDictionary(Dictionary<string, DicValue> source)
        {
            lock (valueLock)
            {
                ResetValues();
                if (source == null)
                {
                    return;
                }
                foreach (var item in source)
                {
                    if (item.Value == null)
                    {
                        continue;
                    }
                    if (string.IsNullOrEmpty(item.Key))
                    {
                        continue;
                    }
                    int index = item.Value.Index;
                    if (index < 0 || index >= ValueCapacity)
                    {
                        continue;
                    }
                    if (!string.IsNullOrEmpty(values[index].Name))
                    {
                        continue;
                    }
                    item.Value.Name = item.Key;
                    if (item.Value.Type != "double" && item.Value.Type != "string")
                    {
                        item.Value.Type = "string";
                    }
                    values[index] = item.Value;
                    nameIndex[item.Key] = index;
                }
            }
        }

        public DicValue GetValueByIndex(int index)
        {
            if (index < 0 || index >= ValueCapacity)
            {
                throw new ArgumentOutOfRangeException(nameof(index), $"索引超出范围:{index}");
            }
            lock (valueLock)
            {
                if (string.IsNullOrEmpty(values[index].Name))
                {
                    throw new KeyNotFoundException($"未找到索引变量:{index}");
                }
                return values[index];
            }
        }

        public DicValue GetValueByName(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("变量名不能为空", nameof(key));
            }
            lock (valueLock)
            {
                if (!nameIndex.TryGetValue(key, out int index))
                {
                    throw new KeyNotFoundException($"未找到变量:{key}");
                }
                return values[index];
            }
        }

        public bool TryGetValueByIndex(int index, out DicValue value)
        {
            value = null;
            if (index < 0 || index >= ValueCapacity)
            {
                return false;
            }
            lock (valueLock)
            {
                if (string.IsNullOrEmpty(values[index].Name))
                {
                    return false;
                }
                value = values[index];
                return true;
            }
        }

        public bool TryGetValueByName(string key, out DicValue value)
        {
            value = null;
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }
            lock (valueLock)
            {
                if (!nameIndex.TryGetValue(key, out int index))
                {
                    return false;
                }
                value = values[index];
                return true;
            }
        }

        public List<string> GetValueNames()
        {
            lock (valueLock)
            {
                return nameIndex.Keys.ToList();
            }
        }

        public Dictionary<string, DicValue> BuildSaveData()
        {
            Dictionary<string, DicValue> data = new Dictionary<string, DicValue>();
            lock (valueLock)
            {
                foreach (var item in nameIndex)
                {
                    DicValue value = values[item.Value];
                    if (value == null || string.IsNullOrEmpty(value.Name))
                    {
                        continue;
                    }
                    data[item.Key] = value;
                }
            }
            return data;
        }

        public double get_D_ValueByIndex(int index)
        {
            double result;
            if (index < 0 || index >= ValueCapacity)
            {
                return -97654321;
            }
            lock (valueLock)
            {
                if (double.TryParse(values[index].Value, out result))
                {
                    return  result;
                }
                else
                {
                    return -97654321;
                }
            }
        }
        public string get_Str_ValueByIndex(int index)
        {
            if (index < 0 || index >= ValueCapacity)
            {
                return null;
            }
            lock (valueLock)
            {
                return values[index].Value;
            }
        }

        public double get_D_ValueByName(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return -97654321;
            }
            lock (valueLock)
            {
                if (nameIndex.TryGetValue(key, out int index))
                {
                    double result;
                    if (double.TryParse(values[index].Value, out result))
                    {
                        return result;
                    }
                }
                return -97654321;
            }
        }
        public string get_Str_ValueByName(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }
            lock (valueLock)
            {
                if (nameIndex.TryGetValue(key, out int index))
                {
                    return values[index].Value;
                }
            }
            return null;

        }
        public bool setValueByName(string key,object newValue)
        {
            if (string.IsNullOrEmpty(key) || newValue == null)
            {
                return false;
            }
            lock (valueLock)
            {
                if (nameIndex.TryGetValue(key, out int index))
                {
                    DicValue value = values[index];
                    value.Value = newValue.ToString();
                    return true;
                }
                return false;
            }
        }
        public bool setValueByIndex(int index,object newValue)
        {
            if (index < 0 || index >= ValueCapacity || newValue == null)
            {
                return false;
            }
            lock (valueLock)
            {
                if (string.IsNullOrEmpty(values[index].Name))
                {
                    return false;
                }
                values[index].Value = newValue.ToString();
                return true;
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
                        lock (valueLock)
                        {
                            if (nameIndex.TryGetValue(key, out int existIndex) && existIndex != num)
                            {
                                dgvValue[1, e.RowIndex].Value = null;
                                dgvValue[2, e.RowIndex].Value = null;
                                dgvValue[3, e.RowIndex].Value = null;
                                dgvValue[4, e.RowIndex].Value = null;
                                return;
                            }

                            DicValue currentValue = values[num];
                            if (!string.IsNullOrEmpty(currentValue.Name) && currentValue.Name != key)
                            {
                                nameIndex.Remove(currentValue.Name);
                            }
                            currentValue.Name = key;
                            currentValue.Index = num;
                            currentValue.Type = type;
                            currentValue.Note = note;
                            currentValue.Value = value;

                            nameIndex[key] = num;
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
                    values[selectedRowIndex].isMark= !values[selectedRowIndex].isMark;
                    SF.mainfrm.SaveAsJson(SF.ConfigPath, "value", BuildSaveData());
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
                startIndex = ValueCapacity - 1;
            }
            for (int i = startIndex; i >= 0; i--)
            {
                if (values[i].isMark)
                {
                    previousIndex = i;
                    break;
                }
            }
            if (previousIndex == -1)
            {
                for (int i = ValueCapacity - 1; i >= 0; i--)
                {
                    if (values[i].isMark)
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
            for (int i = startIndex; i < ValueCapacity; i++)
            {
                if (values[i].isMark)
                {
                    nextIndex = i;
                    break;
                }
            }
            if (nextIndex == -1)
            {
                for (int i = 0; i < ValueCapacity; i++)
                {
                    if (values[i].isMark)
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
            for (int i = 0; i < ValueCapacity; i++)
            {
                values[i].isMark = false;
            }
            SF.mainfrm.SaveAsJson(SF.ConfigPath, "value", BuildSaveData());
            dgvValue.Refresh();
        }

        private void dgvValue_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            dgvValue[e.ColumnIndex, e.RowIndex].Style.Alignment = DataGridViewContentAlignment.MiddleCenter;

            if (e.ColumnIndex == 0 && e.RowIndex >= 0)
            {
                // 获取当前行对应的数据项
                if (values[e.RowIndex].isMark)
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
    public class DicValue
    {
        public int Index { get; set; }

        public string Type { get; set; }

        public string Name { get; set; }

        public string Value { get; set; }

        public string Note { get; set; }
        public bool isMark { get; set; }

        public double GetDValue()
        {
            return double.Parse(Value);
        }

        public string GetCValue()
        {
            return Value;
        }
    }
}
