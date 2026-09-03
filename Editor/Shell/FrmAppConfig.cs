// 模块：编辑器 / 外壳。
// 职责范围：页面装配、菜单、工具栏、导航、生命周期和程序设置。

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Automation.DeviceSdk;

namespace Automation
{
    public class FrmAppConfig : Form
    {
        private readonly PlatformRuntime runtime;
        private readonly TextBox txtQueueSize = new TextBox();
        private readonly CheckBox chkPerformanceAnalysis = new CheckBox();
        private readonly CheckBox chkRuntimeDiagnostics = new CheckBox();
        private readonly ComboBox cmbStartupView = new ComboBox();
        private readonly TextBox txtConfigPath = new TextBox();
        private readonly Button btnSave = new Button();
        private readonly Button btnCancel = new Button();
        private readonly Button btnAccounts = new Button();
        private readonly ToolTip toolTip = new ToolTip();
        private readonly UiHoverAnimator hoverAnimator = new UiHoverAnimator();
        private readonly List<Image> ownedImages = new List<Image>();

        public FrmAppConfig()
            : this(null)
        {
        }

        internal FrmAppConfig(PlatformRuntime runtime)
        {
            this.runtime = runtime;
            InitializeLayout();
            LoadConfig();
            if (runtime != null)
            {
                runtime.Accounts.Changed += Accounts_Changed;
                RefreshAccess();
            }
            Disposed += (sender, args) =>
            {
                if (runtime != null)
                {
                    runtime.Accounts.Changed -= Accounts_Changed;
                }
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
            ShowIcon = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular);
            BackColor = UiPalette.InputFocused;
            ClientSize = new Size(620, 530);

            Panel header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 78,
                BackColor = UiPalette.Navigation
            };
            PictureBox headerIcon = new PictureBox
            {
                Location = new Point(24, 22),
                Size = new Size(32, 32),
                SizeMode = PictureBoxSizeMode.CenterImage,
                Image = CreateOwnedImage(UiIconKind.Settings, UiPalette.NavigationAccent, 28)
            };
            Label title = new Label
            {
                Text = "程序设置",
                Location = new Point(68, 23),
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold),
                ForeColor = UiPalette.TextInverse
            };
            header.Controls.Add(headerIcon);
            header.Controls.Add(title);

            Panel settingsCard = new Panel
            {
                Location = new Point(22, 96),
                Size = new Size(576, 358),
                BackColor = UiPalette.SurfaceStrong,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            settingsCard.Paint += (sender, args) =>
            {
                using (Pen pen = new Pen(UiPalette.Stroke))
                {
                    args.Graphics.DrawRectangle(pen, 0, 0, settingsCard.ClientSize.Width - 1, settingsCard.ClientSize.Height - 1);
                }
            };

            TableLayoutPanel fields = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Padding = new Padding(22, 8, 22, 8),
                ColumnCount = 2,
                RowCount = 6
            };
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44F));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56F));
            for (int i = 0; i < 6; i++)
            {
                fields.RowStyles.Add(new RowStyle(SizeType.Percent, 16.6667F));
            }

            fields.Controls.Add(CreateFieldLabel("通讯接收队列长度"), 0, 0);
            txtQueueSize.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Regular);
            txtQueueSize.TextAlign = HorizontalAlignment.Left;
            fields.Controls.Add(CreateInputHost(txtQueueSize), 1, 0);

            fields.Controls.Add(CreateFieldLabel("运行时性能分析"), 0, 1);
            chkPerformanceAnalysis.Text = "启用性能分析";
            chkPerformanceAnalysis.AutoSize = true;
            chkPerformanceAnalysis.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Regular);
            fields.Controls.Add(CreateInputHost(chkPerformanceAnalysis), 1, 1);

            fields.Controls.Add(CreateFieldLabel("程序启动界面"), 0, 2);
            cmbStartupView.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbStartupView.FlatStyle = FlatStyle.Flat;
            cmbStartupView.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Regular);
            cmbStartupView.Items.Add(new StartupViewItem(AutomationStartupView.Hmi, "HMI 操作界面"));
            cmbStartupView.Items.Add(new StartupViewItem(AutomationStartupView.PlatformEditor, "平台编辑器"));
            fields.Controls.Add(CreateInputHost(cmbStartupView), 1, 2);

            fields.Controls.Add(CreateFieldLabel("智能诊断中心"), 0, 3);
            chkRuntimeDiagnostics.Text = "启用智能诊断中心";
            chkRuntimeDiagnostics.AutoSize = true;
            chkRuntimeDiagnostics.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Regular);
            fields.Controls.Add(CreateInputHost(chkRuntimeDiagnostics), 1, 3);

            fields.Controls.Add(CreateFieldLabel("配置文件"), 0, 4);
            txtConfigPath.ReadOnly = true;
            txtConfigPath.ShortcutsEnabled = true;
            txtConfigPath.Font = new Font("Consolas", 9.5F, FontStyle.Regular);
            txtConfigPath.BackColor = UiPalette.SurfaceStrong;
            fields.Controls.Add(CreateInputHost(txtConfigPath), 1, 4);
            toolTip.SetToolTip(txtConfigPath, "单击后可使用 Ctrl+C 复制完整路径");

            fields.Controls.Add(CreateFieldLabel("账户与权限"), 0, 5);
            btnAccounts.Text = "打开账户管理";
            btnAccounts.Dock = DockStyle.Fill;
            btnAccounts.FlatStyle = FlatStyle.Flat;
            btnAccounts.Margin = new Padding(0, 9, 0, 9);
            btnAccounts.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular);
            btnAccounts.ForeColor = UiPalette.TextPrimary;
            btnAccounts.BackColor = UiPalette.SurfaceStrong;
            btnAccounts.FlatAppearance.BorderSize = 1;
            btnAccounts.FlatAppearance.BorderColor = UiPalette.StrokeStrong;
            btnAccounts.FlatAppearance.MouseOverBackColor = UiPalette.SurfaceHover;
            btnAccounts.FlatAppearance.MouseDownBackColor = UiPalette.SurfacePressed;
            btnAccounts.UseVisualStyleBackColor = false;
            btnAccounts.Click += BtnAccounts_Click;
            fields.Controls.Add(btnAccounts, 1, 5);
            settingsCard.Controls.Add(fields);

            btnSave.Text = "保存设置";
            btnSave.SetBounds(492, 472, 106, 38);
            btnSave.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnSave.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Regular);
            btnSave.ForeColor = UiPalette.TextInverse;
            btnSave.BackColor = UiPalette.Brand;
            btnSave.FlatStyle = FlatStyle.Flat;
            btnSave.FlatAppearance.BorderSize = 0;
            btnSave.FlatAppearance.MouseOverBackColor = btnSave.BackColor;
            btnSave.FlatAppearance.MouseDownBackColor = btnSave.BackColor;
            btnSave.Image = CreateOwnedImage(UiIconKind.Save, UiPalette.TextInverse, 18);
            btnSave.TextImageRelation = TextImageRelation.ImageBeforeText;
            btnSave.Padding = new Padding(3, 0, 3, 0);
            btnSave.Click += BtnSave_Click;
            Color saveBackColor = btnSave.BackColor;
            hoverAnimator.Attach(btnSave, () => saveBackColor, UiPalette.BrandHover, true);

            btnCancel.Text = "关闭";
            btnCancel.SetBounds(394, 472, 88, 38);
            btnCancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnCancel.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Regular);
            btnCancel.ForeColor = UiPalette.TextPrimary;
            btnCancel.BackColor = UiPalette.SurfaceStrong;
            btnCancel.FlatStyle = FlatStyle.Flat;
            btnCancel.FlatAppearance.BorderSize = 1;
            btnCancel.FlatAppearance.BorderColor = UiPalette.StrokeStrong;
            btnCancel.FlatAppearance.MouseOverBackColor = btnCancel.BackColor;
            btnCancel.FlatAppearance.MouseDownBackColor = btnCancel.BackColor;
            btnCancel.DialogResult = DialogResult.Cancel;
            hoverAnimator.Attach(btnCancel, () => UiPalette.SurfaceStrong, UiPalette.SurfaceHover, true);

            AcceptButton = btnSave;
            CancelButton = btnCancel;

            Controls.Add(btnSave);
            Controls.Add(btnCancel);
            Controls.Add(settingsCard);
            Controls.Add(header);
        }

        private static Label CreateFieldLabel(string text)
        {
            return new Label
            {
                Text = text,
                Dock = DockStyle.Fill,
                Padding = new Padding(2, 0, 0, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold),
                ForeColor = UiPalette.TextPrimary
            };
        }

        private Panel CreateInputHost(Control input)
        {
            Color borderColor = UiPalette.StrokeStrong;
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
                BackColor = UiPalette.SurfaceStrong
            };
            if (input is TextBoxBase textBox)
            {
                textBox.BorderStyle = BorderStyle.None;
            }
            input.BackColor = UiPalette.SurfaceStrong;
            input.Location = input is ComboBox ? new Point(8, 7) : new Point(10, 8);
            input.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            inner.Controls.Add(input);
            inner.Resize += (sender, args) =>
            {
                input.Width = Math.Max(1, inner.ClientSize.Width - input.Left - 8);
            };
            input.Enter += (sender, args) => host.BackColor = UiPalette.BrandAccent;
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

        private void LoadConfig()
        {
            txtConfigPath.Text = AppConfigStorage.ConfigPath;
            if (AppConfigStorage.TryLoad(out AppConfig config, out string error))
            {
                txtQueueSize.Text = config.CommMaxMessageQueueSize.ToString();
                chkPerformanceAnalysis.Checked = config.EnablePerformanceAnalysis;
                chkRuntimeDiagnostics.Checked = config.EnableRuntimeDiagnostics;
                SelectStartupView(config.StartupView);
                return;
            }
            txtQueueSize.Text = string.Empty;
            chkPerformanceAnalysis.Checked = false;
            chkRuntimeDiagnostics.Checked = false;
            cmbStartupView.SelectedIndex = -1;
            MessageBox.Show(error, "配置读取失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            if (runtime != null && !runtime.Accounts.Authorize(
                PlatformPermissionCodes.ApplicationConfigure,
                "保存程序设置",
                out string permissionError))
            {
                MessageBox.Show(permissionError, "权限不足", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
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
            if (!(cmbStartupView.SelectedItem is StartupViewItem startupViewItem))
            {
                MessageBox.Show("请选择程序启动界面。", "参数错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            AppConfig config = new AppConfig
            {
                CommMaxMessageQueueSize = value,
                StartupView = startupViewItem.View,
                EnablePerformanceAnalysis = chkPerformanceAnalysis.Checked,
                EnableRuntimeDiagnostics = chkRuntimeDiagnostics.Checked
            };
            if (!AppConfigStorage.TrySave(config, out string error))
            {
                MessageBox.Show(error, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            MessageBox.Show(
                "保存成功。",
                "提示",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }

        private void BtnAccounts_Click(object sender, EventArgs e)
        {
            if (runtime == null)
            {
                MessageBox.Show("账户服务不可用。", "权限不足", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!runtime.Accounts.Authorize(
                PlatformPermissionCodes.AccountManage,
                "打开账户管理",
                out string error))
            {
                MessageBox.Show(error, "权限不足", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            AccountSessionSnapshot current = runtime.Accounts.CurrentUser;
            if (current == null || current.Level != AccountLevel.SystemAdministrator)
            {
                MessageBox.Show("只有系统管理员可以管理账户。", "权限不足", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            using (var form = new FrmAccountManagement(runtime.Accounts))
            {
                form.ShowDialog(this);
            }
        }

        private void Accounts_Changed(object sender, AccountSessionChangedEventArgs e)
        {
            RefreshAccess();
        }

        private void RefreshAccess()
        {
            AccountSessionSnapshot current = runtime?.Accounts.CurrentUser;
            btnAccounts.Enabled = current != null
                && current.Level == AccountLevel.SystemAdministrator
                && runtime.Accounts.CheckPermission(PlatformPermissionCodes.AccountManage, out _);
            if (runtime == null)
            {
                return;
            }
            bool canConfigure = runtime.Accounts.CheckPermission(
                PlatformPermissionCodes.ApplicationConfigure, out _);
            txtQueueSize.Enabled = canConfigure;
            chkPerformanceAnalysis.Enabled = canConfigure;
            chkRuntimeDiagnostics.Enabled = canConfigure;
            cmbStartupView.Enabled = canConfigure;
            btnSave.Enabled = canConfigure;
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
