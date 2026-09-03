using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;
using Automation.DeviceSdk;

// 模块：编辑器 / 账户安全。
// 职责范围：提供编辑器登录和管理员设置密码的短生命周期对话框，不保存或回显密码。

namespace Automation
{
    internal sealed class FrmAccountLogin : Form
    {
        private readonly IAuthenticationSession authentication;
        private readonly TextBox userName = new TextBox();
        private readonly TextBox password = new TextBox();
        private readonly Button loginButton = new Button();
        private readonly Button cancelButton = new Button();
        private readonly Image accountImage;
        private bool loginInProgress;

        public FrmAccountLogin(IAuthenticationSession authentication)
        {
            this.authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
            Text = "账户登录";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ShowIcon = false;
            ClientSize = new Size(500, 338);
            Font = new Font("Microsoft YaHei UI", 10F);
            BackColor = UiPalette.Background;

            accountImage = UiIconFactory.Create(UiIconKind.Account, UiPalette.NavigationAccent, 34);
            Controls.Add(AccountDialogVisuals.CreateHeader("账户登录", accountImage));

            Panel card = AccountDialogVisuals.CreateCard(new Rectangle(26, 104, 448, 160));
            ConfigureTextBox(userName, false, "用户名");
            ConfigureTextBox(password, true, "密码");
            AddField(card, "用户名", userName, 20);
            AddField(card, "密码", password, 82);
            Controls.Add(card);

            cancelButton.Text = "取消";
            cancelButton.Location = new Point(272, 282);
            cancelButton.Size = new Size(94, 40);
            cancelButton.DialogResult = DialogResult.Cancel;
            loginButton.Text = "登录";
            loginButton.Location = new Point(376, 282);
            loginButton.Size = new Size(98, 40);
            loginButton.AccessibleName = "登录账户";
            AccountDialogVisuals.StyleSecondaryButton(cancelButton);
            AccountDialogVisuals.StylePrimaryButton(loginButton);
            loginButton.Click += Login_Click;

            Controls.Add(cancelButton);
            Controls.Add(loginButton);
            AcceptButton = loginButton;
            CancelButton = cancelButton;
            Shown += (sender, args) => userName.Focus();
            Disposed += (sender, args) => accountImage.Dispose();
        }

        private static void ConfigureTextBox(TextBox textBox, bool secret, string accessibleName)
        {
            textBox.BorderStyle = BorderStyle.None;
            textBox.Font = new Font("Microsoft YaHei UI", 11F);
            textBox.BackColor = UiPalette.Input;
            textBox.UseSystemPasswordChar = secret;
            textBox.AccessibleName = accessibleName;
        }

        private static void AddField(Control card, string labelText, TextBox textBox, int top)
        {
            Label label = new Label
            {
                Text = labelText,
                Location = new Point(24, top),
                Size = new Size(84, 42),
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
                ForeColor = UiPalette.TextPrimary,
                TextAlign = ContentAlignment.MiddleLeft
            };
            Panel inputHost = AccountDialogVisuals.CreateInputHost(textBox);
            inputHost.SetBounds(116, top, 306, 42);
            card.Controls.Add(label);
            card.Controls.Add(inputHost);
        }

        private async void Login_Click(object sender, EventArgs e)
        {
            if (loginInProgress)
            {
                return;
            }
            string name = userName.Text;
            string secret = password.Text;
            password.Clear();
            SetLoginInProgress(true);
            LoginAttemptResult result;
            try
            {
                result = await Task.Run(() =>
                {
                    bool succeeded = authentication.Login(name, secret, out string error);
                    return new LoginAttemptResult(succeeded, error);
                });
            }
            catch (Exception ex)
            {
                result = new LoginAttemptResult(false, ex.Message);
            }
            if (IsDisposed || Disposing)
            {
                return;
            }
            SetLoginInProgress(false);
            if (!result.Succeeded)
            {
                MessageBox.Show(this, result.Error, "登录失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                password.Focus();
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        }

        private void SetLoginInProgress(bool value)
        {
            loginInProgress = value;
            userName.Enabled = !value;
            password.Enabled = !value;
            loginButton.Enabled = !value;
            cancelButton.Enabled = !value;
            UseWaitCursor = value;
        }

        private sealed class LoginAttemptResult
        {
            public LoginAttemptResult(bool succeeded, string error)
            {
                Succeeded = succeeded;
                Error = error;
            }

            public bool Succeeded { get; }
            public string Error { get; }
        }
    }

    internal sealed class FrmAccountPassword : Form
    {
        private readonly TextBox password = new TextBox();
        private readonly TextBox confirm = new TextBox();
        private readonly Image accountImage;

        public FrmAccountPassword(string title)
        {
            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ShowIcon = false;
            ClientSize = new Size(500, 338);
            Font = new Font("Microsoft YaHei UI", 10F);
            BackColor = UiPalette.Background;

            accountImage = UiIconFactory.Create(UiIconKind.Account, UiPalette.NavigationAccent, 34);
            Controls.Add(AccountDialogVisuals.CreateHeader(title, accountImage));

            Panel card = AccountDialogVisuals.CreateCard(new Rectangle(26, 104, 448, 160));
            ConfigurePasswordBox(password, "新密码");
            ConfigurePasswordBox(confirm, "确认密码");
            AddPasswordField(card, "新密码", password, 20);
            AddPasswordField(card, "确认密码", confirm, 82);
            Controls.Add(card);

            var cancel = new Button
            {
                Text = "取消",
                Location = new Point(272, 282),
                Size = new Size(94, 40),
                DialogResult = DialogResult.Cancel
            };
            var ok = new Button
            {
                Text = "确定",
                Location = new Point(376, 282),
                Size = new Size(98, 40)
            };
            AccountDialogVisuals.StyleSecondaryButton(cancel);
            AccountDialogVisuals.StylePrimaryButton(ok);
            ok.Click += Ok_Click;

            Controls.Add(cancel);
            Controls.Add(ok);
            AcceptButton = ok;
            CancelButton = cancel;
            Shown += (sender, args) => password.Focus();
            Disposed += (sender, args) => accountImage.Dispose();
        }

        public string PasswordValue { get; private set; }

        private static void ConfigurePasswordBox(TextBox textBox, string accessibleName)
        {
            textBox.BorderStyle = BorderStyle.None;
            textBox.Font = new Font("Microsoft YaHei UI", 11F);
            textBox.BackColor = UiPalette.Input;
            textBox.UseSystemPasswordChar = true;
            textBox.AccessibleName = accessibleName;
        }

        private static void AddPasswordField(Control card, string labelText, TextBox textBox, int top)
        {
            Label label = new Label
            {
                Text = labelText,
                Location = new Point(24, top),
                Size = new Size(84, 42),
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
                ForeColor = UiPalette.TextPrimary,
                TextAlign = ContentAlignment.MiddleLeft
            };
            Panel inputHost = AccountDialogVisuals.CreateInputHost(textBox);
            inputHost.SetBounds(116, top, 306, 42);
            card.Controls.Add(label);
            card.Controls.Add(inputHost);
        }

        private void Ok_Click(object sender, EventArgs e)
        {
            if (!string.Equals(password.Text, confirm.Text, StringComparison.Ordinal))
            {
                MessageBox.Show(this, "两次输入的密码不一致。", "密码错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!AccountPasswordHasher.ValidatePassword(password.Text, out string error))
            {
                MessageBox.Show(this, error, "密码错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            PasswordValue = password.Text;
            password.Clear();
            confirm.Clear();
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    internal static class AccountDialogVisuals
    {
        public static Panel CreateHeader(string titleText, Image image)
        {
            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 84,
                BackColor = UiPalette.Navigation
            };
            var icon = new PictureBox
            {
                Location = new Point(28, 22),
                Size = new Size(40, 40),
                SizeMode = PictureBoxSizeMode.CenterImage,
                Image = image
            };
            var title = new Label
            {
                Text = titleText,
                Location = new Point(82, 24),
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold),
                ForeColor = UiPalette.TextInverse
            };
            header.Controls.Add(icon);
            header.Controls.Add(title);
            return header;
        }

        public static Panel CreateCard(Rectangle bounds)
        {
            var card = new Panel
            {
                Bounds = bounds,
                BackColor = UiPalette.SurfaceStrong
            };
            card.Paint += (sender, args) =>
            {
                args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle border = new Rectangle(0, 0, Math.Max(1, card.Width - 1), Math.Max(1, card.Height - 1));
                using (GraphicsPath path = CreateRoundedPath(border, 8))
                using (Pen pen = new Pen(UiPalette.Stroke))
                {
                    args.Graphics.DrawPath(pen, path);
                }
            };
            ApplyRoundedRegion(card, 8);
            return card;
        }

        public static Panel CreateInputHost(TextBox textBox)
        {
            Color normalBorder = UiPalette.StrokeStrong;
            var host = new Panel
            {
                BackColor = normalBorder,
                Padding = new Padding(1)
            };
            var inner = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiPalette.Input
            };
            textBox.Location = new Point(11, 9);
            textBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            inner.Controls.Add(textBox);
            inner.Resize += (sender, args) => textBox.Width = Math.Max(1, inner.ClientSize.Width - 22);
            textBox.Enter += (sender, args) =>
            {
                host.BackColor = UiPalette.Focus;
                inner.BackColor = UiPalette.InputFocused;
                textBox.BackColor = UiPalette.InputFocused;
            };
            textBox.Leave += (sender, args) =>
            {
                host.BackColor = normalBorder;
                inner.BackColor = UiPalette.Input;
                textBox.BackColor = UiPalette.Input;
            };
            host.Controls.Add(inner);
            return host;
        }

        public static void StylePrimaryButton(Button button)
        {
            button.Font = new Font("Microsoft YaHei UI", 10.5F);
            button.ForeColor = UiPalette.TextInverse;
            button.BackColor = UiPalette.Brand;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = UiPalette.BrandHover;
            button.FlatAppearance.MouseDownBackColor = UiPalette.BrandPressed;
            button.UseVisualStyleBackColor = false;
        }

        public static void StyleSecondaryButton(Button button)
        {
            button.Font = new Font("Microsoft YaHei UI", 10.5F);
            button.ForeColor = UiPalette.TextPrimary;
            button.BackColor = UiPalette.SurfaceStrong;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = UiPalette.StrokeStrong;
            button.FlatAppearance.MouseOverBackColor = UiPalette.SurfaceHover;
            button.FlatAppearance.MouseDownBackColor = UiPalette.SurfacePressed;
            button.UseVisualStyleBackColor = false;
        }

        public static void ApplyRoundedRegion(Control control, int radius)
        {
            void ApplyRegion()
            {
                Rectangle bounds = new Rectangle(0, 0, Math.Max(1, control.Width), Math.Max(1, control.Height));
                using (GraphicsPath path = CreateRoundedPath(bounds, radius))
                {
                    Region previous = control.Region;
                    control.Region = new Region(path);
                    previous?.Dispose();
                }
            }
            control.Resize += (sender, args) => ApplyRegion();
            ApplyRegion();
        }

        private static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
        {
            int diameter = Math.Max(1, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
            var path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
