using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace Automation
{
    public delegate void EventHandler1();

    public partial class Message : Form
    {
        private EventHandler1 btn1Action;
        private EventHandler1 btn2Action;
        private EventHandler1 btn3Action;
        private EventHandler1 closeFallback;
        private int completionState;

        public bool isChoiced => Volatile.Read(ref completionState) != 0;

        public Message(string headText, string msg, EventHandler1 eventHandler1, string btnTxt1, bool isWait)
        {
            InitializeComponent();
            ConfigureContent(headText, msg);
            btn1.Visible = false;
            btn2.Visible = true;
            btn3.Visible = false;
            btn2.Text = btnTxt1;
            btn2Action = eventHandler1;
            closeFallback = eventHandler1;
            AcceptButton = btn2;
            Present(isWait);
        }

        public Message(string headText, string msg, EventHandler1 eventHandler1, EventHandler1 eventHandler2,
            string btnTxt1, string btnTxt2, bool isWait)
        {
            InitializeComponent();
            ConfigureContent(headText, msg);
            btn1.Visible = true;
            btn2.Visible = false;
            btn3.Visible = true;
            btn1.Text = btnTxt1;
            btn3.Text = btnTxt2;
            btn1Action = eventHandler1;
            btn3Action = eventHandler2;
            closeFallback = eventHandler2;
            AcceptButton = btn1;
            ApplyPrimaryButtonStyle(btn1, btnTxt1);
            Present(isWait);
        }

        public Message(string headText, string msg, EventHandler1 eventHandler1, EventHandler1 eventHandler2,
            EventHandler1 eventHandler3, string btnTxt1, string btnTxt2, string btnTxt3, bool isWait)
        {
            InitializeComponent();
            ConfigureContent(headText, msg);
            btn1.Visible = true;
            btn2.Visible = true;
            btn3.Visible = true;
            btn1.Text = btnTxt1;
            btn2.Text = btnTxt2;
            btn3.Text = btnTxt3;
            btn1Action = eventHandler1;
            btn2Action = eventHandler2;
            btn3Action = eventHandler3;
            closeFallback = eventHandler3;
            AcceptButton = btn1;
            ApplyPrimaryButtonStyle(btn1, btnTxt1);
            ApplySecondaryButtonStyle(btn2);
            Present(isWait);
        }

        public Message()
        {
            InitializeComponent();
            ConfigureContent("提示", string.Empty);
        }

        public void SetMsgFrom(string msg)
        {
            txtMsg.Text = msg ?? string.Empty;
        }

        public void ApplyContentTheme(Color backgroundColor, Color foregroundColor)
        {
            panelMessage.BackColor = backgroundColor;
            contentLayout.BackColor = backgroundColor;
            txtMsg.BackColor = backgroundColor;
            txtMsg.ForeColor = foregroundColor;
            lblCaption.BackColor = backgroundColor;
            lblCaption.ForeColor = foregroundColor;
        }

        public void btnCanel()
        {
            if (IsDisposed || Disposing)
            {
                return;
            }
            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke((Action)btnCanel);
                }
                catch (InvalidOperationException)
                {
                }
                return;
            }
            Complete(null, false);
        }

        private void ConfigureContent(string headText, string message)
        {
            string title = string.IsNullOrWhiteSpace(headText) ? "提示" : headText.Trim();
            Text = title;
            lblCaption.Text = title;
            txtMsg.Text = message ?? string.Empty;
            ApplyPrimaryButtonStyle(btn2, string.Empty);
            AdjustDialogSize();
        }

        private void Present(bool modal)
        {
            AdjustDialogSize();
            Form owner = Form.ActiveForm;
            if (owner == this || owner is Message || owner == null || owner.IsDisposed || !owner.Visible)
            {
                owner = SF.mainfrm != null && !SF.mainfrm.IsDisposed && SF.mainfrm.Visible
                    ? SF.mainfrm
                    : null;
            }
            StartPosition = owner == null ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent;
            if (modal)
            {
                if (owner == null)
                {
                    ShowDialog();
                }
                else
                {
                    ShowDialog(owner);
                }
                return;
            }
            if (owner == null)
            {
                Show();
            }
            else
            {
                Show(owner);
            }
            Activate();
        }

        private void AdjustDialogSize()
        {
            Point oldCenter = new Point(Left + Width / 2, Top + Height / 2);
            Screen screen = Screen.FromControl(Form.ActiveForm ?? this);
            Rectangle workingArea = screen.WorkingArea;
            float scale = DeviceDpi > 0 ? DeviceDpi / 96F : 1F;
            int minWidth = Math.Max((int)(520 * scale), MinimumSize.Width);
            int maxWidth = Math.Max(minWidth, Math.Min((int)(920 * scale), (int)(workingArea.Width * 0.86)));
            int contentMaxWidth = Math.Max(260, maxWidth - (int)(72 * scale));
            Size measured = TextRenderer.MeasureText(txtMsg.Text.Length == 0 ? " " : txtMsg.Text,
                txtMsg.Font, new Size(contentMaxWidth, int.MaxValue),
                TextFormatFlags.TextBoxControl | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            int buttonAreaWidth = panelBtn.Padding.Horizontal;
            foreach (Button button in new[] { btn1, btn2, btn3 })
            {
                if (button.Visible)
                {
                    buttonAreaWidth += button.GetPreferredSize(Size.Empty).Width + button.Margin.Horizontal;
                }
            }
            int desiredWidth = Math.Max(minWidth,
                Math.Min(maxWidth, Math.Max(measured.Width + (int)(88 * scale), buttonAreaWidth)));
            int chromeHeight = (int)(174 * scale);
            int minHeight = Math.Max((int)(260 * scale), MinimumSize.Height);
            int maxHeight = Math.Max(minHeight, (int)(workingArea.Height * 0.72));
            int desiredHeight = Math.Max(minHeight, Math.Min(maxHeight, measured.Height + chromeHeight));
            ClientSize = new Size(desiredWidth, desiredHeight);
            PerformLayout();
            txtMsg.ScrollBars = measured.Height > txtMsg.ClientSize.Height
                ? RichTextBoxScrollBars.Vertical
                : RichTextBoxScrollBars.None;
            if (Visible)
            {
                Left = oldCenter.X - Width / 2;
                Top = oldCenter.Y - Height / 2;
            }
        }

        private void MessageContentChanged(object sender, EventArgs e)
        {
            AdjustDialogSize();
        }

        private static bool IsDangerAction(string text)
        {
            return !string.IsNullOrWhiteSpace(text)
                && (text.Contains("删除") || text.Contains("停止") || text.Contains("退出") || text.Contains("清除"));
        }

        private void ApplyPrimaryButtonStyle(Button button, string text)
        {
            bool danger = IsDangerAction(text);
            button.BackColor = danger ? Color.FromArgb(194, 57, 52) : Color.FromArgb(34, 111, 183);
            button.ForeColor = Color.White;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = danger ? Color.FromArgb(211, 70, 64) : Color.FromArgb(43, 126, 201);
            button.FlatAppearance.MouseDownBackColor = danger ? Color.FromArgb(160, 43, 39) : Color.FromArgb(22, 83, 139);
            button.Font = new Font(button.Font, FontStyle.Bold);
        }

        private static void ApplySecondaryButtonStyle(Button button)
        {
            button.BackColor = Color.White;
            button.ForeColor = Color.FromArgb(48, 59, 72);
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = Color.FromArgb(190, 199, 210);
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(237, 240, 244);
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(225, 230, 236);
            button.Font = new Font(button.Font, FontStyle.Regular);
        }

        private void Complete(EventHandler1 action, bool invokeFallback)
        {
            if (Interlocked.CompareExchange(ref completionState, 1, 0) != 0)
            {
                return;
            }
            SetButtonsEnabled(false);
            try
            {
                EventHandler1 callback = invokeFallback ? closeFallback : action;
                callback?.Invoke();
            }
            catch (Exception ex)
            {
                SF.SetSecurityLock($"弹窗操作回调异常：{ex.Message}");
            }
            finally
            {
                if (!IsDisposed)
                {
                    Close();
                }
            }
        }

        private void SetButtonsEnabled(bool enabled)
        {
            btn1.Enabled = enabled;
            btn2.Enabled = enabled;
            btn3.Enabled = enabled;
        }

        private void btn1_Click(object sender, EventArgs e)
        {
            Complete(btn1Action, false);
        }

        private void btn2_Click(object sender, EventArgs e)
        {
            Complete(btn2Action, false);
        }

        private void btn3_Click(object sender, EventArgs e)
        {
            Complete(btn3Action, false);
        }

        private void Message_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Escape)
            {
                return;
            }
            e.Handled = true;
            e.SuppressKeyPress = true;
            Complete(null, true);
        }

        private void FrmMessage_FormClosed(object sender, FormClosedEventArgs e)
        {
            if (Interlocked.CompareExchange(ref completionState, 1, 0) == 0)
            {
                try
                {
                    if (e.CloseReason == CloseReason.UserClosing || e.CloseReason == CloseReason.None)
                    {
                        closeFallback?.Invoke();
                    }
                }
                catch (Exception ex)
                {
                    SF.SetSecurityLock($"弹窗关闭回调异常：{ex.Message}");
                }
            }
            btn1Action = null;
            btn2Action = null;
            btn3Action = null;
            closeFallback = null;
        }
    }
}
