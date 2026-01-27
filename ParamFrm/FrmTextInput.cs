using System;
using System.Drawing;
using System.Windows.Forms;

namespace Automation.ParamFrm
{
    public class FrmTextInput : Form
    {
        private readonly TextBox textBoxValue;
        private readonly Label labelTitle;
        private readonly Button btnOk;
        private readonly Button btnCancel;

        public string InputText => textBoxValue.Text;

        public FrmTextInput(string title, string labelText, string defaultValue = "")
        {
            Text = title;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Width = 420;
            Height = 170;

            labelTitle = new Label
            {
                AutoSize = true,
                Location = new Point(16, 18),
                Text = labelText
            };

            textBoxValue = new TextBox
            {
                Location = new Point(18, 42),
                Width = 360,
                Text = defaultValue ?? string.Empty
            };

            btnOk = new Button
            {
                Text = "确定",
                Location = new Point(210, 82)
            };
            btnOk.Click += OnOkClick;

            btnCancel = new Button
            {
                Text = "取消",
                Location = new Point(300, 82),
                DialogResult = DialogResult.Cancel
            };

            Controls.Add(labelTitle);
            Controls.Add(textBoxValue);
            Controls.Add(btnOk);
            Controls.Add(btnCancel);

            AcceptButton = btnOk;
            CancelButton = btnCancel;
            Shown += OnShown;
        }

        private void OnOkClick(object sender, EventArgs e)
        {
            string value = (textBoxValue.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                MessageBox.Show("名称不能为空");
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        }

        private void OnShown(object sender, EventArgs e)
        {
            textBoxValue.SelectAll();
            textBoxValue.Focus();
        }
    }
}
