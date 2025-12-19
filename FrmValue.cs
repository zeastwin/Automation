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
        public Dictionary<string, DicValue> dicValues; 
        public List<DicValue> dicValuesList = new List<DicValue>();

        public FrmValue()
        {
            InitializeComponent();
            dgvValue.SelectionMode = DataGridViewSelectionMode.ColumnHeaderSelect;
            dgvValue.Columns[0].SortMode = DataGridViewColumnSortMode.NotSortable;
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

            if (!Directory.Exists(SF.ConfigPath))
            {
                Directory.CreateDirectory(SF.ConfigPath);
            }
            if(!File.Exists(SF.ConfigPath+"value.json"))
            {
                SF.frmValue.dicValues = new Dictionary<string, DicValue>();
                SF.mainfrm.SaveAsJson(SF.ConfigPath, "value", SF.frmValue.dicValues);
            }
            dicValues = SF.mainfrm.ReadJson<Dictionary<string, DicValue>>(SF.ConfigPath, "value");

            RefreshValue();

            SF.isFinBulidFrmValue = true;

        }
  
        public void RefreshValue()
        {
            dgvValue.Rows.Clear();
            dicValuesList.Clear();

            for (int i = 0; i < 1000; i++)
            {
                dgvValue.Rows.Add();

                DicValue cachedValue = dicValues.Values.FirstOrDefault(item => item.Index == i);

                if (cachedValue != null)
                {
                    dgvValue.Rows[i].Cells[0].Value = cachedValue.Index;
                    dgvValue.Rows[i].Cells[1].Value = cachedValue.Name;
                    dgvValue.Rows[i].Cells[2].Value = cachedValue.Type;
                    dgvValue.Rows[i].Cells[3].Value = cachedValue.Value;
                    dgvValue.Rows[i].Cells[4].Value = cachedValue.Note;
                    dicValuesList.Add(cachedValue);
                  
                }
                else
                {
                    dgvValue.Rows[i].Cells[0].Value = i;
                    dicValuesList.Add(new DicValue());
                }
            }
        }
        //刷新变量界面
        public void FreshFrmValue()
        {
            for (int i = 0; i < 1000; i++)
            {
                DicValue cachedValue = dicValues.Values.FirstOrDefault(item => item.Index == i);

                if (cachedValue != null)
                {
                    dgvValue.Rows[i].Cells[0].Value = cachedValue.Index;
                    dgvValue.Rows[i].Cells[1].Value = cachedValue.Name;
                    dgvValue.Rows[i].Cells[2].Value = cachedValue.Type;
                    dgvValue.Rows[i].Cells[3].Value = cachedValue.Value;
                    dgvValue.Rows[i].Cells[4].Value = cachedValue.Note;
                }
            }

        }
        /*=============================================================================================*/

        public double get_D_ValueByIndex(int index)
        {
            double result;
            if (double.TryParse(dicValuesList[index].Value, out result))
            {
                return  result;
            }
            else
            {
                return -97654321;
            }
            return -97654321;
        }
        public string get_Str_ValueByIndex(int index)
        {
            return  dicValuesList[index].Value;
        }

        public double get_D_ValueByName(string key)
        {
            if (dicValues.TryGetValue(key, out DicValue value))
            {
                double result;
                if (double.TryParse(value.Value, out result))
                {
                    return result;
                }
                else
                {
                    return -97654321;
                } 
            }
            return -97654321;
        }
        public string get_Str_ValueByName(string key)
        {
            if (dicValues.TryGetValue(key, out DicValue value))
            {
              return value.Value;
            }
            return null;

        }
        public bool get_B_ValueByName(string key)
        {
            if (dicValues.TryGetValue(key, out DicValue value))
            {
                return value.Value.ToString() == "TRUE" ? true : false;
            }
            return false;
        }
        public bool setValueByName(string key,object newValue)
        {
            if (dicValues.TryGetValue(key, out DicValue value))
            {
                value.Value = newValue.ToString();
                return true;
            }
            return false;
        }
        public bool setValueByIndex(int index,object newValue)
        {
            if (dicValuesList[index].Value != null)
            {
                dicValuesList[index].Value = newValue.ToString();
                return true;
            }
            return false;
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
                    DataGridView dataGridView = (DataGridView)sender;
                    if (e.ColumnIndex == 2)
                    {
                        DataGridViewComboBoxCell comboCell = (DataGridViewComboBoxCell)dataGridView.Rows[e.RowIndex].Cells[e.ColumnIndex];
                        // 获取选中的值
                        object selectedValue = comboCell.Value;

                        if (selectedValue != null)
                        {
                            // 获取选中值在下拉框选项中的索引
                            int selectedIndex = comboCell.Items.IndexOf(selectedValue);

                            if (selectedIndex == 2)
                            {
                                DataGridViewComboBoxCell comboBoxCell = new DataGridViewComboBoxCell();
                                comboBoxCell.Items.Add("TRUE");
                                comboBoxCell.Items.Add("FALSE");
                                comboBoxCell.Value = "FALSE"; // 设置默认选项
                                dgvValue[e.ColumnIndex + 1, e.RowIndex] = comboBoxCell;
                            }
                            else
                            {
                                DataGridViewTextBoxCell TextboxCell = new DataGridViewTextBoxCell();
                                dgvValue[e.ColumnIndex + 1, e.RowIndex] = TextboxCell;
                            }
                        }

                       
                     
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

                        if (dicValues.ContainsKey(key))
                        {
                            if(dicValues[key].Index != num)
                            {
                                dgvValue[1, e.RowIndex].Value = null;
                                dgvValue[2, e.RowIndex].Value = null;
                                dgvValue[3, e.RowIndex].Value = null;
                                dgvValue[4, e.RowIndex].Value = null;
                                return;
                            }
                        }

                        // 获取选中值在下拉框选项中的索引
                        int selectedIndex = -1;

                        if (type == "double")
                        {
                            selectedIndex = 0;
                        }
                        else if (type == "string")
                        {
                            selectedIndex = 1;
                        }
                        else if(type =="bool")
                        {
                            selectedIndex = 2;
                        }

                        string value=null;

                        if (selectedIndex == 0)
                        {
                            value = dataGridView.Rows[e.RowIndex].Cells[3].Value.ToString();
                            string newValue = dataGridView.Rows[e.RowIndex].Cells[3].Value.ToString();
                            if (double.TryParse(newValue, out double doubleValue2) == false)
                            {
                            
                                dgvValue[1,e.RowIndex].Value = null;
                                dgvValue[2,e.RowIndex].Value = null;
                                dgvValue[3,e.RowIndex].Value = null;
                                dgvValue[4,e.RowIndex].Value = null;         
                                return;
                            }
                        }
                        else if (selectedIndex==1|| selectedIndex == 2)
                        {
                            value = dataGridView.Rows[e.RowIndex].Cells[3].Value.ToString();
                        }

                        DicValue dic =  new DicValue() { Name=key,Index = num, Type = type, Note = note, Value = value };

                        if (dicValues.ContainsKey(key))
                        {
                            dicValues[key].Name=key;
                            dicValues[key].Index=num;
                            dicValues[key].Type=type;
                            dicValues[key].Note=note;
                            dicValues[key].Value=value;
                         
                        }
                        else
                        {
                            if (dicValuesList[num].Name != null)
                            {
                                if (dicValues.ContainsKey(dicValuesList[num].Name))
                                {
                                    dicValues.Remove(dicValuesList[num].Name);
                                }
                            }
                            dicValues.Add(key, dic);
                            dicValuesList[num] = dic;
                      
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
                    dicValuesList[selectedRowIndex].isMark= !dicValuesList[selectedRowIndex].isMark;
                    SF.mainfrm.SaveAsJson(SF.ConfigPath, "value", SF.frmValue.dicValues);
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
            if(currentIndex - 1 < 0)
            {
                int nextIndex = dicValuesList.FindLastIndex(obj => obj.isMark);
                if (nextIndex > 0)
                {
                    currentIndex = nextIndex;
                    dgvValue.FirstDisplayedScrollingRowIndex = currentIndex;
                    return;
                }
            }
            int previousIndex = dicValuesList.FindLastIndex(currentIndex-1, obj => obj.isMark);

            if (previousIndex != -1)
            {
                // 标记找到的对象
                currentIndex = previousIndex;
                dgvValue.FirstDisplayedScrollingRowIndex = currentIndex;
            }
            else
            {
                int nextIndex = dicValuesList.FindLastIndex(obj => obj.isMark);
                if (nextIndex > 0)
                {
                    currentIndex = nextIndex;
                    dgvValue.FirstDisplayedScrollingRowIndex = currentIndex;
                }
            }

        }

        private void BtnNext_Click(object sender, EventArgs e)
        {
            int nextIndex = dicValuesList.FindIndex(currentIndex + 1, obj => obj.isMark);

            if (nextIndex != -1)
            {
                // 标记下一个对象
                currentIndex = nextIndex;
                dgvValue.FirstDisplayedScrollingRowIndex = currentIndex;
            }
            else
            {

                int previousIndex = dicValuesList.FindIndex(0, obj => obj.isMark);
                if (previousIndex != -1)
                {
                    currentIndex = previousIndex;
                    dgvValue.FirstDisplayedScrollingRowIndex = currentIndex;
                }
                   

            }
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            for (int i = 0; i < dicValuesList.Count; i++)
            {
                dicValuesList[i].isMark = false;
            }
            SF.mainfrm.SaveAsJson(SF.ConfigPath, "value", SF.frmValue.dicValues);
            dgvValue.Refresh();
        }

        private void dgvValue_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            dgvValue[e.ColumnIndex, e.RowIndex].Style.Alignment = DataGridViewContentAlignment.MiddleCenter;

            if (e.ColumnIndex == 0 && e.RowIndex >= 0)
            {
                // 获取当前行对应的数据项
                if (dicValuesList[e.RowIndex].isMark)
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
