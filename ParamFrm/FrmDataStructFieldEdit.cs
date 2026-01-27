using System;
using System.Drawing;
using System.Windows.Forms;

namespace Automation.ParamFrm
{
    public class FrmDataStructFieldEdit : Form
    {
        private readonly int structIndex;
        private readonly int itemIndex;
        private readonly int fieldIndex;
        private readonly bool isNew;
        private readonly DataStructValueType originalType;
        private readonly string originalName;

        private TextBox textBoxIndex;
        private TextBox textBoxName;
        private ComboBox comboType;
        private TextBox textBoxValue;
        private Button btnOk;
        private Button btnCancel;

        public int ActualFieldIndex { get; private set; } = -1;

        public FrmDataStructFieldEdit(int structIndex, int itemIndex)
        {
            this.structIndex = structIndex;
            this.itemIndex = itemIndex;
            fieldIndex = -1;
            isNew = true;
            originalType = DataStructValueType.Text;
            originalName = string.Empty;
            InitializeUi("新增字段");
            textBoxIndex.Text = "自动";
            comboType.SelectedIndex = 0;
        }

        public FrmDataStructFieldEdit(int structIndex, int itemIndex, int fieldIndex, string fieldName, DataStructValueType fieldType, string fieldValue)
        {
            this.structIndex = structIndex;
            this.itemIndex = itemIndex;
            this.fieldIndex = fieldIndex;
            isNew = false;
            originalType = fieldType;
            originalName = fieldName ?? string.Empty;
            InitializeUi("编辑字段");
            textBoxIndex.Text = fieldIndex.ToString();
            textBoxName.Text = fieldName ?? string.Empty;
            comboType.SelectedIndex = fieldType == DataStructValueType.Text ? 0 : 1;
            textBoxValue.Text = fieldValue ?? string.Empty;
        }

        private void InitializeUi(string title)
        {
            Text = title;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Width = 420;
            Height = 260;

            Label labelIndex = new Label
            {
                AutoSize = true,
                Location = new Point(16, 18),
                Text = "索引"
            };

            textBoxIndex = new TextBox
            {
                Location = new Point(80, 15),
                Width = 300,
                ReadOnly = true
            };

            Label labelName = new Label
            {
                AutoSize = true,
                Location = new Point(16, 54),
                Text = "字段名"
            };

            textBoxName = new TextBox
            {
                Location = new Point(80, 50),
                Width = 300
            };

            Label labelType = new Label
            {
                AutoSize = true,
                Location = new Point(16, 90),
                Text = "类型"
            };

            comboType = new ComboBox
            {
                Location = new Point(80, 86),
                Width = 120,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            comboType.Items.Add("string");
            comboType.Items.Add("double");

            Label labelValue = new Label
            {
                AutoSize = true,
                Location = new Point(16, 126),
                Text = "值"
            };

            textBoxValue = new TextBox
            {
                Location = new Point(80, 122),
                Width = 300
            };

            btnOk = new Button
            {
                Text = "确定",
                Location = new Point(210, 168)
            };
            btnOk.Click += BtnOk_Click;

            btnCancel = new Button
            {
                Text = "取消",
                Location = new Point(300, 168),
                DialogResult = DialogResult.Cancel
            };

            Controls.Add(labelIndex);
            Controls.Add(textBoxIndex);
            Controls.Add(labelName);
            Controls.Add(textBoxName);
            Controls.Add(labelType);
            Controls.Add(comboType);
            Controls.Add(labelValue);
            Controls.Add(textBoxValue);
            Controls.Add(btnOk);
            Controls.Add(btnCancel);

            AcceptButton = btnOk;
            CancelButton = btnCancel;
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            string fieldName = (textBoxName.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(fieldName))
            {
                MessageBox.Show("字段名称不能为空");
                return;
            }
            if (comboType.SelectedIndex < 0)
            {
                MessageBox.Show("请选择字段类型");
                return;
            }
            DataStructValueType type = comboType.SelectedIndex == 0 ? DataStructValueType.Text : DataStructValueType.Number;
            string value = textBoxValue.Text ?? string.Empty;

            if (type == DataStructValueType.Number && !double.TryParse(value, out _))
            {
                MessageBox.Show("数值格式错误");
                return;
            }

            if (isNew)
            {
                if (!SF.dataStructStore.AddField(structIndex, itemIndex, fieldName, type, value, -1, out int actualIndex, out string error))
                {
                    MessageBox.Show(error);
                    return;
                }
                ActualFieldIndex = actualIndex;
                DialogResult = DialogResult.OK;
                Close();
                return;
            }

            if (!string.Equals(originalName, fieldName, StringComparison.Ordinal))
            {
                if (!SF.dataStructStore.RenameField(structIndex, itemIndex, fieldIndex, fieldName, out string renameError))
                {
                    MessageBox.Show(renameError);
                    return;
                }
            }

            if (originalType != type)
            {
                if (!SF.dataStructStore.SetFieldType(structIndex, itemIndex, fieldIndex, type, out string message))
                {
                    MessageBox.Show(message);
                    return;
                }
                if (!string.IsNullOrEmpty(message))
                {
                    MessageBox.Show(message);
                }
            }

            if (!SF.dataStructStore.SetFieldValue(structIndex, itemIndex, fieldIndex, type, value, out string valueError))
            {
                MessageBox.Show(valueError);
                return;
            }

            ActualFieldIndex = fieldIndex;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
