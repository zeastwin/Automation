using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;
using Automation.DeviceSdk;

// 模块：平台内置 HMI / 账户登录。
// 职责范围：只通过公开 SDK 登录和退出；账户配置仍由平台编辑器负责。

namespace Automation.Hmi
{
    internal sealed class LegacyFingerprintControl : UserControl
    {
        private IAutomationPlatform platform;
        private readonly TextBox userName;
        private readonly TextBox password;
        private readonly Button loginButton;
        private readonly Button logoutButton;
        private readonly Label status;
        private readonly Panel statusPanel;
        private readonly Panel accountCard;
        private readonly Image accountImage;
        private bool loginInProgress;

        internal LegacyFingerprintControl()
        {
            BackColor = UiPalette.HmiBackground;
            Font = new Font("Microsoft YaHei UI", 10F);

            accountCard = new Panel
            {
                Size = new Size(640, 376),
                BackColor = UiPalette.SurfaceStrong
            };
            accountCard.Paint += AccountCard_Paint;
            ApplyRoundedRegion(accountCard, 10);

            accountImage = UiIconFactory.Create(UiIconKind.Account, UiPalette.NavigationAccent, 36);
            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 84,
                BackColor = UiPalette.Navigation
            };
            var icon = new PictureBox
            {
                Location = new Point(30, 20),
                Size = new Size(44, 44),
                SizeMode = PictureBoxSizeMode.CenterImage,
                Image = accountImage
            };
            var title = new Label
            {
                Text = "账户登录",
                Location = new Point(90, 24),
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold),
                ForeColor = UiPalette.TextInverse
            };
            header.Controls.Add(icon);
            header.Controls.Add(title);
            accountCard.Controls.Add(header);

            userName = CreateTextBox(false, "用户名");
            password = CreateTextBox(true, "密码");
            AddField("用户名", userName, 108);
            AddField("密码", password, 168);

            loginButton = new Button
            {
                Text = "登录",
                Location = new Point(150, 236),
                Size = new Size(218, 42),
                AccessibleName = "登录账户"
            };
            logoutButton = new Button
            {
                Text = "退出登录",
                Location = new Point(380, 236),
                Size = new Size(222, 42),
                AccessibleName = "退出当前账户"
            };
            StylePrimaryButton(loginButton);
            StyleSecondaryButton(logoutButton);
            accountCard.Controls.Add(loginButton);
            accountCard.Controls.Add(logoutButton);

            statusPanel = new Panel
            {
                Location = new Point(38, 304),
                Size = new Size(564, 50),
                BackColor = UiPalette.SurfaceSubtle
            };
            ApplyRoundedRegion(statusPanel, 7);
            status = new Label
            {
                Location = new Point(16, 12),
                Size = new Size(532, 24),
                Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold),
                ForeColor = UiPalette.TextSecondary,
                Text = "当前未登录",
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
            statusPanel.Controls.Add(status);
            accountCard.Controls.Add(statusPanel);
            Controls.Add(accountCard);

            loginButton.Click += LoginButton_Click;
            logoutButton.Click += LogoutButton_Click;
            password.KeyDown += Password_KeyDown;
            Resize += (sender, args) => LayoutAccountCard();
            Disposed += (sender, args) =>
            {
                DetachPlatform();
                accountImage.Dispose();
            };
            LayoutAccountCard();
        }

        internal void Attach(IAutomationPlatform value)
        {
            DetachPlatform();
            platform = value;
            if (platform?.Authentication != null)
            {
                platform.Authentication.Changed += Authentication_Changed;
            }
            RefreshView();
        }

        internal void RefreshView()
        {
            AccountSessionSnapshot current = platform?.Authentication?.CurrentUser;
            bool loggedIn = current != null;
            userName.Enabled = !loggedIn && !loginInProgress;
            password.Enabled = !loggedIn && !loginInProgress;
            loginButton.Enabled = !loggedIn && !loginInProgress && platform?.Authentication != null;
            logoutButton.Enabled = loggedIn && !loginInProgress;
            status.Text = loggedIn
                ? $"{current.UserName}  ·  {GetLevelText(current.Level)}"
                : "当前未登录";
            status.ForeColor = loggedIn ? UiPalette.Success : UiPalette.TextSecondary;
            statusPanel.BackColor = loggedIn ? UiPalette.SuccessSoft : UiPalette.SurfaceSubtle;
        }

        private void AddField(string labelText, TextBox textBox, int top)
        {
            var label = new Label
            {
                Text = labelText,
                Location = new Point(38, top),
                Size = new Size(96, 42),
                Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold),
                ForeColor = UiPalette.TextPrimary,
                TextAlign = ContentAlignment.MiddleLeft
            };
            Panel inputHost = CreateInputHost(textBox);
            inputHost.SetBounds(150, top, 452, 42);
            accountCard.Controls.Add(label);
            accountCard.Controls.Add(inputHost);
        }

        private static TextBox CreateTextBox(bool secret, string accessibleName)
        {
            return new TextBox
            {
                BorderStyle = BorderStyle.None,
                Font = new Font("Microsoft YaHei UI", 11F),
                BackColor = UiPalette.Input,
                UseSystemPasswordChar = secret,
                AccessibleName = accessibleName
            };
        }

        private static Panel CreateInputHost(TextBox textBox)
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
            textBox.Location = new Point(12, 9);
            textBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            inner.Controls.Add(textBox);
            inner.Resize += (sender, args) => textBox.Width = Math.Max(1, inner.ClientSize.Width - 24);
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

        private static void StylePrimaryButton(Button button)
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

        private static void StyleSecondaryButton(Button button)
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

        private void LayoutAccountCard()
        {
            accountCard.Left = Math.Max(12, (ClientSize.Width - accountCard.Width) / 2);
            accountCard.Top = Math.Max(12, (ClientSize.Height - accountCard.Height) / 2);
        }

        private void AccountCard_Paint(object sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle border = new Rectangle(0, 0, Math.Max(1, accountCard.Width - 1), Math.Max(1, accountCard.Height - 1));
            using (GraphicsPath path = CreateRoundedPath(border, 10))
            using (Pen pen = new Pen(UiPalette.Stroke))
            {
                e.Graphics.DrawPath(pen, path);
            }
        }

        private async void LoginButton_Click(object sender, EventArgs e)
        {
            IAuthenticationSession authentication = platform?.Authentication;
            if (authentication == null || loginInProgress)
            {
                return;
            }
            string name = userName.Text;
            string secret = password.Text;
            password.Clear();
            loginInProgress = true;
            RefreshView();
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
            loginInProgress = false;
            RefreshView();
            if (!result.Succeeded)
            {
                MessageBox.Show(FindForm(), result.Error, "登录失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                password.Focus();
                return;
            }
        }

        private void LogoutButton_Click(object sender, EventArgs e)
        {
            platform?.Authentication?.Logout();
            password.Clear();
            RefreshView();
        }

        private void Password_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && loginButton.Enabled)
            {
                e.SuppressKeyPress = true;
                LoginButton_Click(loginButton, EventArgs.Empty);
            }
        }

        private void Authentication_Changed(object sender, AccountSessionChangedEventArgs e)
        {
            if (IsHandleCreated && InvokeRequired)
            {
                BeginInvoke((Action)RefreshView);
                return;
            }
            RefreshView();
        }

        private void DetachPlatform()
        {
            if (platform?.Authentication != null)
            {
                platform.Authentication.Changed -= Authentication_Changed;
            }
            platform = null;
        }

        private static string GetLevelText(AccountLevel level)
        {
            switch (level)
            {
                case AccountLevel.Operator: return "操作员";
                case AccountLevel.Engineer: return "工程师";
                case AccountLevel.SystemAdministrator: return "系统管理员";
                default: return level.ToString();
            }
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

        private static void ApplyRoundedRegion(Control control, int radius)
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
