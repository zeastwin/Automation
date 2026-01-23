using System;
using System.Drawing;
using System.Windows.Forms;

namespace Automation
{
    public class FrmAppConfig : Form
    {
        private readonly TextBox txtQueueSize = new TextBox();
        private readonly Label lblPath = new Label();
        private readonly Button btnSave = new Button();
        private readonly Button btnCancel = new Button();
        private readonly Label lblTip = new Label();

        public FrmAppConfig()
        {
            InitializeLayout();
            LoadConfig();
        }

        private void InitializeLayout()
        {
            Text = "程序设置";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(520, 200);

            Label lblQueue = new Label
            {
                Text = "通讯接收队列长度",
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleLeft
            };
            txtQueueSize.Width = 240;

            Label lblPathTitle = new Label
            {
                Text = "配置文件路径",
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleLeft
            };
            lblPath.AutoSize = true;

            lblTip.Text = "提示：保存后需重启程序生效";
            lblTip.AutoSize = true;
            lblTip.ForeColor = Color.DimGray;

            btnSave.Text = "保存";
            btnSave.Width = 80;
            btnSave.Click += BtnSave_Click;

            btnCancel.Text = "关闭";
            btnCancel.Width = 80;
            btnCancel.DialogResult = DialogResult.Cancel;

            AcceptButton = btnSave;
            CancelButton = btnCancel;

            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 4,
                Padding = new Padding(12)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            layout.Controls.Add(lblQueue, 0, 0);
            layout.Controls.Add(txtQueueSize, 1, 0);
            layout.Controls.Add(lblPathTitle, 0, 1);
            layout.Controls.Add(lblPath, 1, 1);
            layout.Controls.Add(lblTip, 1, 2);

            FlowLayoutPanel buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Fill
            };
            buttons.Controls.Add(btnCancel);
            buttons.Controls.Add(btnSave);
            layout.Controls.Add(buttons, 1, 3);

            Controls.Add(layout);
        }

        private void LoadConfig()
        {
            lblPath.Text = AppConfigStorage.ConfigPath;
            if (AppConfigStorage.TryLoad(out AppConfig config, out string error))
            {
                txtQueueSize.Text = config.CommMaxMessageQueueSize.ToString();
                return;
            }
            txtQueueSize.Text = string.Empty;
            MessageBox.Show(error, "配置读取失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            string text = txtQueueSize.Text;
            if (string.IsNullOrEmpty(text))
            {
                MessageBox.Show("队列长度不能为空。", "参数错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (!IsDigits(text))
            {
                MessageBox.Show("队列长度格式非法。", "参数错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (!int.TryParse(text, out int value))
            {
                MessageBox.Show("队列长度超出范围。", "参数错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (value <= 0)
            {
                MessageBox.Show("队列长度必须大于0。", "参数错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            AppConfig config = new AppConfig
            {
                CommMaxMessageQueueSize = value
            };
            if (!AppConfigStorage.TrySave(config, out string error))
            {
                MessageBox.Show(error, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            MessageBox.Show("保存成功，重启后生效。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
        }

        private static bool IsDigits(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c < '0' || c > '9')
                {
                    return false;
                }
            }
            return text.Length > 0;
        }
    }
}
