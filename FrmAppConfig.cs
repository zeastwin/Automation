using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Automation
{
    public class FrmAppConfig : Form
    {
        private readonly TextBox txtQueueSize = new TextBox();
        private readonly ComboBox cmbRuntimeMode = new ComboBox();
        private readonly ComboBox cmbStartupView = new ComboBox();
        private readonly TextBox txtConfigPath = new TextBox();
        private readonly Button btnSave = new Button();
        private readonly Button btnCancel = new Button();
        private readonly ToolTip toolTip = new ToolTip();
        private readonly UiHoverAnimator hoverAnimator = new UiHoverAnimator();
        private readonly List<Image> ownedImages = new List<Image>();

        public FrmAppConfig()
        {
            InitializeLayout();
            LoadConfig();
            Disposed += (sender, args) =>
            {
                hoverAnimator.Dispose();
                toolTip.Dispose();
                foreach (Image image in ownedImages)
                {
                    image.Dispose();
                }
                ownedImages.Clear();
            };
        }

        private void InitializeLayout()
        {
            Text = "程序设置";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular);
            BackColor = Color.FromArgb(243, 246, 248);
            ClientSize = new Size(620, 500);

            Panel header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 78,
                BackColor = Color.FromArgb(52, 58, 64)
            };
            PictureBox headerIcon = new PictureBox
            {
                Location = new Point(24, 22),
                Size = new Size(32, 32),
                SizeMode = PictureBoxSizeMode.CenterImage,
                Image = CreateOwnedImage(UiIconKind.Settings, Color.FromArgb(105, 202, 241), 28)
            };
            Label title = new Label
            {
                Text = "程序设置",
                Location = new Point(68, 15),
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold),
                ForeColor = Color.White
            };
            Label subtitle = new Label
            {
                Text = "配置启动界面、平台通讯与运行环境",
                Location = new Point(70, 45),
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular),
                ForeColor = Color.FromArgb(185, 198, 207)
            };
            header.Controls.Add(headerIcon);
            header.Controls.Add(title);
            header.Controls.Add(subtitle);

            Panel settingsCard = new Panel
            {
                Location = new Point(22, 96),
                Size = new Size(576, 270),
                BackColor = Color.White,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            settingsCard.Paint += (sender, args) =>
            {
                using (Pen pen = new Pen(Color.FromArgb(222, 228, 233)))
                {
                    args.Graphics.DrawRectangle(pen, 0, 0, settingsCard.ClientSize.Width - 1, settingsCard.ClientSize.Height - 1);
                }
            };
            ApplyRoundedRegion(settingsCard, 8);

            TableLayoutPanel fields = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Padding = new Padding(22, 8, 22, 8),
                ColumnCount = 2,
                RowCount = 4
            };
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 53F));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 47F));
            fields.RowStyles.Add(new RowStyle(SizeType.Percent, 25F));
            fields.RowStyles.Add(new RowStyle(SizeType.Percent, 25F));
            fields.RowStyles.Add(new RowStyle(SizeType.Percent, 25F));
            fields.RowStyles.Add(new RowStyle(SizeType.Percent, 25F));

            fields.Controls.Add(CreateFieldDescription(
                "通讯接收队列长度",
                "限制通讯消息在内存中的最大排队数量"), 0, 0);
            txtQueueSize.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Regular);
            txtQueueSize.TextAlign = HorizontalAlignment.Left;
            fields.Controls.Add(CreateInputHost(txtQueueSize), 1, 0);

            fields.Controls.Add(CreateFieldDescription(
                "运行模式",
                "选择真实硬件环境或离线仿真环境"), 0, 1);
            cmbRuntimeMode.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbRuntimeMode.FlatStyle = FlatStyle.Flat;
            cmbRuntimeMode.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Regular);
            cmbRuntimeMode.Items.Add(new RuntimeModeItem(AutomationRuntimeMode.Hardware, "正常模式"));
            cmbRuntimeMode.Items.Add(new RuntimeModeItem(AutomationRuntimeMode.Simulation, "仿真模式"));
            fields.Controls.Add(CreateInputHost(cmbRuntimeMode), 1, 1);

            fields.Controls.Add(CreateFieldDescription(
                "程序启动界面",
                "下次启动时优先显示平台或 HMI"), 0, 2);
            cmbStartupView.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbStartupView.FlatStyle = FlatStyle.Flat;
            cmbStartupView.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Regular);
            cmbStartupView.Items.Add(new StartupViewItem(AutomationStartupView.Hmi, "HMI 操作界面"));
            cmbStartupView.Items.Add(new StartupViewItem(AutomationStartupView.PlatformEditor, "平台编辑器"));
            fields.Controls.Add(CreateInputHost(cmbStartupView), 1, 2);

            fields.Controls.Add(CreateFieldDescription(
                "配置文件",
                "当前程序实例实际读取的配置位置"), 0, 3);
            txtConfigPath.ReadOnly = true;
            txtConfigPath.ShortcutsEnabled = true;
            txtConfigPath.Font = new Font("Consolas", 9.5F, FontStyle.Regular);
            txtConfigPath.BackColor = Color.White;
            fields.Controls.Add(CreateInputHost(txtConfigPath), 1, 3);
            toolTip.SetToolTip(txtConfigPath, "单击后可使用 Ctrl+C 复制完整路径");
            settingsCard.Controls.Add(fields);

            Panel restartNotice = new Panel
            {
                Location = new Point(22, 382),
                Size = new Size(576, 42),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.FromArgb(255, 248, 232)
            };
            ApplyRoundedRegion(restartNotice, 7);
            PictureBox noticeIcon = new PictureBox
            {
                Location = new Point(14, 11),
                Size = new Size(20, 20),
                SizeMode = PictureBoxSizeMode.CenterImage,
                Image = CreateOwnedImage(UiIconKind.Alarm, Color.FromArgb(185, 116, 21), 18)
            };
            Label restartText = new Label
            {
                Text = "保存后的配置将在下次启动程序时生效。",
                Location = new Point(42, 10),
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular),
                ForeColor = Color.FromArgb(126, 83, 25)
            };
            restartNotice.Controls.Add(noticeIcon);
            restartNotice.Controls.Add(restartText);

            btnSave.Text = "保存设置";
            btnSave.SetBounds(492, 443, 106, 38);
            btnSave.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnSave.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Regular);
            btnSave.ForeColor = Color.White;
            btnSave.BackColor = Color.FromArgb(22, 121, 170);
            btnSave.FlatStyle = FlatStyle.Flat;
            btnSave.FlatAppearance.BorderSize = 0;
            btnSave.FlatAppearance.MouseOverBackColor = btnSave.BackColor;
            btnSave.FlatAppearance.MouseDownBackColor = btnSave.BackColor;
            btnSave.Image = CreateOwnedImage(UiIconKind.Save, Color.White, 18);
            btnSave.TextImageRelation = TextImageRelation.ImageBeforeText;
            btnSave.Padding = new Padding(3, 0, 3, 0);
            btnSave.Click += BtnSave_Click;
            ApplyRoundedRegion(btnSave, 6);
            Color saveBackColor = btnSave.BackColor;
            hoverAnimator.Attach(btnSave, () => saveBackColor, Color.FromArgb(16, 103, 147), true);

            btnCancel.Text = "关闭";
            btnCancel.SetBounds(394, 443, 88, 38);
            btnCancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnCancel.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Regular);
            btnCancel.ForeColor = Color.FromArgb(55, 69, 78);
            btnCancel.BackColor = Color.White;
            btnCancel.FlatStyle = FlatStyle.Flat;
            btnCancel.FlatAppearance.BorderSize = 1;
            btnCancel.FlatAppearance.BorderColor = Color.FromArgb(199, 210, 218);
            btnCancel.FlatAppearance.MouseOverBackColor = btnCancel.BackColor;
            btnCancel.FlatAppearance.MouseDownBackColor = btnCancel.BackColor;
            btnCancel.DialogResult = DialogResult.Cancel;
            ApplyRoundedRegion(btnCancel, 6);
            hoverAnimator.Attach(btnCancel, () => Color.White, Color.FromArgb(230, 236, 240), true);

            AcceptButton = btnSave;
            CancelButton = btnCancel;

            Controls.Add(btnSave);
            Controls.Add(btnCancel);
            Controls.Add(restartNotice);
            Controls.Add(settingsCard);
            Controls.Add(header);
        }

        private Panel CreateFieldDescription(string title, string description)
        {
            Panel panel = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            Label titleLabel = new Label
            {
                Text = title,
                Location = new Point(0, 11),
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(42, 55, 64)
            };
            Label descriptionLabel = new Label
            {
                Text = description,
                Location = new Point(0, 35),
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 8.75F, FontStyle.Regular),
                ForeColor = Color.FromArgb(126, 139, 148)
            };
            panel.Controls.Add(titleLabel);
            panel.Controls.Add(descriptionLabel);
            return panel;
        }

        private Panel CreateInputHost(Control input)
        {
            Color borderColor = Color.FromArgb(199, 210, 218);
            Panel host = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 12, 0, 12),
                BackColor = borderColor,
                Padding = new Padding(1)
            };
            Panel inner = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White
            };
            if (input is TextBoxBase textBox)
            {
                textBox.BorderStyle = BorderStyle.None;
            }
            input.BackColor = Color.White;
            input.Location = input is ComboBox ? new Point(8, 7) : new Point(10, 8);
            input.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            inner.Controls.Add(input);
            inner.Resize += (sender, args) =>
            {
                input.Width = Math.Max(1, inner.ClientSize.Width - input.Left - 8);
            };
            input.Enter += (sender, args) => host.BackColor = Color.FromArgb(60, 157, 202);
            input.Leave += (sender, args) => host.BackColor = borderColor;
            host.Controls.Add(inner);
            return host;
        }

        private Image CreateOwnedImage(UiIconKind icon, Color color, int size)
        {
            Image image = UiIconFactory.Create(icon, color, size);
            ownedImages.Add(image);
            return image;
        }

        private static void ApplyRoundedRegion(Control control, int radius)
        {
            void ApplyRegion()
            {
                int diameter = radius * 2;
                using (GraphicsPath path = new GraphicsPath())
                {
                    Rectangle bounds = new Rectangle(0, 0, Math.Max(1, control.Width), Math.Max(1, control.Height));
                    path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
                    path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
                    path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
                    path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
                    path.CloseFigure();
                    Region previous = control.Region;
                    control.Region = new Region(path);
                    previous?.Dispose();
                }
            }
            control.Resize += (sender, args) => ApplyRegion();
            ApplyRegion();
        }

        private void LoadConfig()
        {
            txtConfigPath.Text = AppConfigStorage.ConfigPath;
            if (AppConfigStorage.TryLoad(out AppConfig config, out string error))
            {
                txtQueueSize.Text = config.CommMaxMessageQueueSize.ToString();
                SelectRuntimeMode(config.RuntimeMode);
                SelectStartupView(config.StartupView);
                return;
            }
            txtQueueSize.Text = string.Empty;
            cmbRuntimeMode.SelectedIndex = -1;
            cmbStartupView.SelectedIndex = -1;
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
            if (!(cmbStartupView.SelectedItem is StartupViewItem startupViewItem))
            {
                MessageBox.Show("请选择程序启动界面。", "参数错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            AppConfig config = new AppConfig
            {
                CommMaxMessageQueueSize = value,
                RuntimeMode = runtimeModeItem.Mode,
                StartupView = startupViewItem.View
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

        private void SelectStartupView(AutomationStartupView view)
        {
            for (int i = 0; i < cmbStartupView.Items.Count; i++)
            {
                if (((StartupViewItem)cmbStartupView.Items[i]).View == view)
                {
                    cmbStartupView.SelectedIndex = i;
                    return;
                }
            }
            cmbStartupView.SelectedIndex = -1;
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

        private sealed class StartupViewItem
        {
            public StartupViewItem(AutomationStartupView view, string displayName)
            {
                View = view;
                DisplayName = displayName;
            }

            public AutomationStartupView View { get; }
            public string DisplayName { get; }

            public override string ToString()
            {
                return DisplayName;
            }
        }
    }
}
