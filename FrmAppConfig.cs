using System;
using System.Drawing;
using System.Windows.Forms;

namespace Automation
{
    public class FrmAppConfig : Form
    {
        private readonly TextBox txtQueueSize = new TextBox();
        private readonly ComboBox cmbRuntimeMode = new ComboBox();
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
            ClientSize = new Size(520, 235);

            Label lblQueue = new Label
            {
                Text = "通讯接收队列长度",
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleLeft
            };
            txtQueueSize.Width = 240;

            Label lblRuntimeMode = new Label
            {
                Text = "运行模式",
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleLeft
            };
            cmbRuntimeMode.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbRuntimeMode.Width = 240;
            cmbRuntimeMode.Items.Add(new RuntimeModeItem(AutomationRuntimeMode.Hardware, "正常模式"));
            cmbRuntimeMode.Items.Add(new RuntimeModeItem(AutomationRuntimeMode.Simulation, "仿真模式"));

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
                RowCount = 5,
                Padding = new Padding(12)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            layout.Controls.Add(lblQueue, 0, 0);
            layout.Controls.Add(txtQueueSize, 1, 0);
            layout.Controls.Add(lblRuntimeMode, 0, 1);
            layout.Controls.Add(cmbRuntimeMode, 1, 1);
            layout.Controls.Add(lblPathTitle, 0, 2);
            layout.Controls.Add(lblPath, 1, 2);
            layout.Controls.Add(lblTip, 1, 3);

            FlowLayoutPanel buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Fill
            };
            buttons.Controls.Add(btnCancel);
            buttons.Controls.Add(btnSave);
            layout.Controls.Add(buttons, 1, 4);

            Controls.Add(layout);
        }

        private void LoadConfig()
        {
            lblPath.Text = AppConfigStorage.ConfigPath;
            if (AppConfigStorage.TryLoad(out AppConfig config, out string error))
            {
                txtQueueSize.Text = config.CommMaxMessageQueueSize.ToString();
                SelectRuntimeMode(config.RuntimeMode);
                return;
            }
            txtQueueSize.Text = string.Empty;
            cmbRuntimeMode.SelectedIndex = -1;
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
            if (!(cmbRuntimeMode.SelectedItem is RuntimeModeItem runtimeModeItem))
            {
                MessageBox.Show("请选择运行模式。", "参数错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            AppConfig config = new AppConfig
            {
                CommMaxMessageQueueSize = value,
                RuntimeMode = runtimeModeItem.Mode
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

        private void SelectRuntimeMode(AutomationRuntimeMode mode)
        {
            for (int i = 0; i < cmbRuntimeMode.Items.Count; i++)
            {
                if (((RuntimeModeItem)cmbRuntimeMode.Items[i]).Mode == mode)
                {
                    cmbRuntimeMode.SelectedIndex = i;
                    return;
                }
            }
            cmbRuntimeMode.SelectedIndex = -1;
        }

        private sealed class RuntimeModeItem
        {
            public RuntimeModeItem(AutomationRuntimeMode mode, string displayName)
            {
                Mode = mode;
                DisplayName = displayName;
            }

            public AutomationRuntimeMode Mode { get; }
            public string DisplayName { get; }

            public override string ToString()
            {
                return DisplayName;
            }
        }
    }
}
