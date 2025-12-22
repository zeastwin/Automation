using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Automation.ParamFrm
{
    public partial class FrmDataStructSet : Form
    {
        public List<ComboBox> dynamicComboBoxs = new List<ComboBox>();
        public List<TextBox> dynamicTextBoxs = new List<TextBox>();
        public DataStruct dt;
        public DataStructHandle dataStructHandle;

        public FrmDataStructSet()
        {
            InitializeComponent();
            textBox2.Visible = false;
            label2.Visible = false;
            Height = 100;
            Width = 550;
            Text = "新建数据结构";
        }

        public FrmDataStructSet(string name)
        {
            InitializeComponent();
            textBox2.Visible = false;
            label2.Visible = false;
            Height = 100;
            Width = 550;
            Text = "修改数据结构";
            textBox1.Text = name;
        }
        public FrmDataStructSet(DataStruct dt, DataStructHandle dataStructHandle)
        {
            InitializeComponent();
            textBox2.Visible = true;
            label2.Visible = true;
            Height = 500;
            Width = 530;
            Text = "新建数据项";
            this.dt = dt;
            this.dataStructHandle = dataStructHandle;
        }
        public FrmDataStructSet(DataStructItem dataStructItem,DataStructHandle dataStructHandle)
        {
            InitializeComponent();
            textBox2.Visible = true;
            label2.Visible = true;
            Height = 500;
            Width = 530;
            Text = "修改数据项";
            textBox2.TextChanged -= textBox2_TextChanged;
            this.dataStructHandle = dataStructHandle;
            textBox1.Text = dataStructItem.Name;
            textBox2.Text = (dataStructItem.str.Count + dataStructItem.num.Count).ToString();
            int col = 0, row = 0;
            for (int i = 0; i < dataStructItem.str.Count + dataStructItem.num.Count; i++)
            {
                ComboBox dynamicComboBox = new ComboBox();

                dynamicComboBox.Location = new System.Drawing.Point(20 + col * 80, 40 + row * 70);
                dynamicComboBox.Size = new System.Drawing.Size(70, 25);

                dynamicComboBox.Items.Add("double");
                dynamicComboBox.Items.Add("string");

                Controls.Add(dynamicComboBox);
                dynamicComboBoxs.Add(dynamicComboBox);


                TextBox dynamicTextBox = new TextBox();

                dynamicTextBox.Location = new System.Drawing.Point(20 + col * 80, 70 + row * 70);
                dynamicTextBox.Size = new System.Drawing.Size(70, 25);

                if (dataStructItem.str.ContainsKey(i))
                {
                    string value = dataStructItem.str[i];
                    dynamicTextBox.Text = value;
                    dynamicComboBox.SelectedIndex = 1;

                }
                else if (dataStructItem.num.ContainsKey(i))
                {
                    double value = dataStructItem.num[i];
                    dynamicComboBox.SelectedIndex = 0;
                    dynamicTextBox.Text = value.ToString();
                }

                Controls.Add(dynamicTextBox);
                dynamicTextBoxs.Add(dynamicTextBox);

                col++;
                if (col > 5) { col = 0; row++; }
            }
            textBox2.TextChanged += textBox2_TextChanged;
        }
        private void textBox2_TextChanged(object sender, EventArgs e)
        {
            foreach (ComboBox item in dynamicComboBoxs)
            {
                if (Controls.Contains(item))
                    Controls.Remove(item);
            }
            foreach (TextBox item in dynamicTextBoxs)
            {
                if (Controls.Contains(item))
                    Controls.Remove(item);
            }
            dynamicComboBoxs.Clear();
            dynamicTextBoxs.Clear();
            if (int.TryParse(textBox2.Text, out int valueCount))
            {
                int col = 0, row = 0;
                for (int i = 0; i < valueCount; i++)
                {

                    ComboBox dynamicComboBox = new ComboBox();

                    dynamicComboBox.Location = new System.Drawing.Point(20 + col * 80, 40 + row * 70);
                    dynamicComboBox.Size = new System.Drawing.Size(70, 25);

                    dynamicComboBox.Items.Add("double");
                    dynamicComboBox.Items.Add("string");

                    Controls.Add(dynamicComboBox);
                    dynamicComboBoxs.Add(dynamicComboBox);
                    dynamicComboBox.SelectedIndex = 0;


                    TextBox dynamicTextBox = new TextBox();

                    dynamicTextBox.Location = new System.Drawing.Point(20 + col * 80, 70 + row * 70);
                    dynamicTextBox.Size = new System.Drawing.Size(70, 25);

                    Controls.Add(dynamicTextBox);
                    dynamicTextBoxs.Add(dynamicTextBox);
                    dynamicTextBox.Text = string.Empty;
                    col++;
                    if (col > 5) { col = 0; row++; }
                }
            }
            else
            {

            }
        }

        private void btnYes_Click(object sender, EventArgs e)
        {
            string name = textBox1.Text.Trim();
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("名称不能为空");
                return;
            }

            if (Text == "新建数据结构")
            {
                if (!SF.dataStructStore.AddStruct(name))
                {
                    MessageBox.Show("名称重复");
                    return;
                }

                SF.dataStructStore.Save(SF.ConfigPath);
                SF.frmdataStruct.RefreshDataSturctList();
                SF.frmdataStruct.RefreshDataSturctTree();


                Close();
                return;
            }
            else if (Text == "修改数据结构")
            {
                if (!SF.frmdataStruct.ModifyStruct(name))
                {
                    MessageBox.Show("名称重复");
                    return;
                }
                Close();
                return;
            }
            else if (Text == "新建数据项")
            {
                bool exactMatchExists = dt.dataStructItems.Any(dsh => dsh.Name == name);
                if (exactMatchExists)
                {
                    MessageBox.Show("名称重复");
                    return;
                }

                List<string> strings = new List<string>();
                List<double> doubles = new List<double>();

                string param = "";

                for (int i = 0; i < dynamicComboBoxs.Count; i++)
                {
                    if (dynamicComboBoxs[i].SelectedIndex == 0)
                    {
                        if (!double.TryParse(dynamicTextBoxs[i].Text, out double numValue))
                        {
                            MessageBox.Show("数值格式错误");
                            return;
                        }
                        doubles.Add(numValue);
                        param += 0;
                    }
                    else if (dynamicComboBoxs[i].SelectedIndex == 1)
                    {
                        strings.Add(dynamicTextBoxs[i].Text);
                        param += 1;
                    }

                }
                int NewRow = dataStructHandle.SelectRow + 1;
                if (dataStructHandle.SelectRow == -1)
                    NewRow = dt.dataStructItems.Count;
                SF.dataStructStore.TrySetStructItemByName(dt.Name, NewRow, name, strings, doubles, param);
                Close();
                SF.dataStructStore.Save(SF.ConfigPath);
                SF.frmdataStruct.RefreshDataSturctList();
                SF.frmdataStruct.RefreshDataSturctTree();
                SF.frmdataStruct.RefreshDataStructFrm(dataStructHandle);
                return;
            }
            else if (Text == "修改数据项")
            {

                List<string> strings = new List<string>();
                List<double> doubles = new List<double>();

                string param = "";

                for (int i = 0; i < dynamicComboBoxs.Count; i++)
                {
                    if (dynamicComboBoxs[i].SelectedIndex == 0)
                    {
                        if (!double.TryParse(dynamicTextBoxs[i].Text, out double numValue))
                        {
                            MessageBox.Show("数值格式错误");
                            return;
                        }
                        doubles.Add(numValue);
                        param += 0;
                    }
                    else if (dynamicComboBoxs[i].SelectedIndex == 1)
                    {
                        strings.Add(dynamicTextBoxs[i].Text);
                        param += 1;
                    }

                }
                int NewRow = dataStructHandle.SelectRow;
                SF.dataStructStore.TrySetStructItemByName(dt.Name, NewRow, name, strings, doubles, param);
                Close();
                SF.dataStructStore.Save(SF.ConfigPath);
                SF.frmdataStruct.RefreshDataSturctList();
                SF.frmdataStruct.RefreshDataSturctTree();
                SF.frmdataStruct.RefreshDataStructFrm(dataStructHandle);
                return;
            }


        }

        private void btnNO_Click(object sender, EventArgs e)
        {
            Close();
        }
    }

}
