// 模块：编辑器 / 通用 UI。
// 职责范围：编辑器共享的视觉、弹窗和 WinForms 交互基础设施。

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
        private readonly PlatformSafetyCoordinator safety;
        private readonly IPlatformEditorUiAdapter editorUi;
        private CheckBox optionCheckBox;
        private int completionState;

        public bool isChoiced => Volatile.Read(ref completionState) != 0;

        internal static bool ShowConfirmationWithOption(
            PlatformRuntime runtime,
            string headText,
            string msg,
            string optionText,
            bool optionCheckedByDefault,
            string confirmText,
            string cancelText,
            out bool optionChecked)
        {
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            bool confirmed = false;
            using (var dialog = new Message(
                runtime.Safety,
                runtime.EditorUi,
                headText,
                msg,
                () => confirmed = true,
                null,
                confirmText,
                cancelText,
                true,
                false))
            {
                dialog.ConfigureOption(optionText, optionCheckedByDefault);
                dialog.PresentDeferred(true);
                optionChecked = dialog.optionCheckBox.Checked;
            }
            return confirmed;
        }

        public Message(PlatformRuntime runtime, string headText, string msg,
            EventHandler1 eventHandler1, string btnTxt1, bool isWait)
            : this(runtime?.Safety, runtime?.EditorUi, headText, msg,
                eventHandler1, btnTxt1, isWait, true)
        {
        }

        internal Message(PlatformSafetyCoordinator safety, IPlatformEditorUiAdapter editorUi,
            string headText, string msg, EventHandler1 eventHandler1, string btnTxt1,
            bool isWait, bool presentImmediately)
        {
            this.safety = safety ?? throw new ArgumentNullException(nameof(safety));
            this.editorUi = editorUi;
            InitializeComponent();
            ConfigureContent(headText, msg);
            btn1.Visible = false;
            btn2.Visible = true;
            btn3.Visible = false;
            btn2.Text = btnTxt1;
            btn2Action = eventHandler1;
            closeFallback = eventHandler1;
            AcceptButton = btn2;
            if (presentImmediately)
            {
                Present(isWait);
            }
        }

        public Message(PlatformRuntime runtime, string headText, string msg,
            EventHandler1 eventHandler1, EventHandler1 eventHandler2,
            string btnTxt1, string btnTxt2, bool isWait)
            : this(runtime?.Safety, runtime?.EditorUi, headText, msg,
                eventHandler1, eventHandler2, btnTxt1, btnTxt2, isWait, true)
        {
        }

        internal Message(PlatformSafetyCoordinator safety, IPlatformEditorUiAdapter editorUi,
            string headText, string msg, EventHandler1 eventHandler1, EventHandler1 eventHandler2,
            string btnTxt1, string btnTxt2, bool isWait, bool presentImmediately)
        {
            this.safety = safety ?? throw new ArgumentNullException(nameof(safety));
            this.editorUi = editorUi;
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
            ApplyPrimaryButtonStyle(btn1, btnTxt1);
            ApplySecondaryButtonStyle(btn3);
            AcceptButton = IsDangerAction(btnTxt1) ? btn3 : btn1;
            CancelButton = btn3;
            if (presentImmediately)
            {
                Present(isWait);
            }
        }

        public Message(PlatformRuntime runtime, string headText, string msg,
            EventHandler1 eventHandler1, EventHandler1 eventHandler2,
            EventHandler1 eventHandler3, string btnTxt1, string btnTxt2, string btnTxt3, bool isWait)
            : this(runtime?.Safety, runtime?.EditorUi, headText, msg, eventHandler1, eventHandler2, eventHandler3,
                btnTxt1, btnTxt2, btnTxt3, isWait, true)
        {
        }

        internal Message(PlatformSafetyCoordinator safety, IPlatformEditorUiAdapter editorUi,
            string headText, string msg, EventHandler1 eventHandler1, EventHandler1 eventHandler2,
            EventHandler1 eventHandler3, string btnTxt1, string btnTxt2, string btnTxt3,
            bool isWait, bool presentImmediately)
        {
            this.safety = safety ?? throw new ArgumentNullException(nameof(safety));
            this.editorUi = editorUi;
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
            if (presentImmediately)
            {
                Present(isWait);
            }
        }

        public Message()
        {
            InitializeComponent();
            ConfigureContent("提示", string.Empty);
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

        private void ConfigureOption(string optionText, bool isChecked)
        {
            float scale = DeviceDpi > 0 ? DeviceDpi / 96F : 1F;
            optionCheckBox = new CheckBox
            {
                AutoEllipsis = true,
                BackColor = UiPalette.SurfaceStrong,
                Checked = isChecked,
                Dock = DockStyle.Fill,
                Font = new Font("Microsoft YaHei UI", 10F),
                ForeColor = UiPalette.TextPrimary,
                Margin = new Padding(0, (int)(10 * scale), 0, 0),
                Text = optionText ?? string.Empty,
                TextAlign = ContentAlignment.MiddleLeft,
                UseVisualStyleBackColor = false
            };
            contentLayout.RowCount = 3;
            contentLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, (int)(44 * scale)));
            contentLayout.Controls.Add(optionCheckBox, 0, 2);
            AdjustDialogSize();
        }

        private void Present(bool modal)
        {
            AdjustDialogSize();
            Form owner = Form.ActiveForm;
            if (owner == this || owner is Message || owner == null || owner.IsDisposed || !owner.Visible)
            {
                owner = editorUi?.DialogOwner as Form;
            }
            StartPosition = FormStartPosition.CenterScreen;
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

        internal void PresentDeferred(bool modal)
        {
            Present(modal);
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
            int optionWidth = optionCheckBox?.GetPreferredSize(Size.Empty).Width ?? 0;
            int buttonAreaWidth = panelBtn.Padding.Horizontal;
            foreach (Button button in new[] { btn1, btn2, btn3 })
            {
                if (button.Visible)
                {
                    buttonAreaWidth += button.GetPreferredSize(Size.Empty).Width + button.Margin.Horizontal;
                }
            }
            int measuredContentWidth = Math.Max(measured.Width, optionWidth);
            int desiredWidth = Math.Max(minWidth,
                Math.Min(maxWidth, Math.Max(measuredContentWidth + (int)(88 * scale), buttonAreaWidth)));
            int optionAreaHeight = optionCheckBox == null ? 0 : (int)(52 * scale);
            int chromeHeight = (int)(174 * scale) + optionAreaHeight;
            int minHeight = Math.Max((int)(260 * scale) + optionAreaHeight, MinimumSize.Height);
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
            button.BackColor = danger ? UiPalette.Danger : UiPalette.Brand;
            button.ForeColor = UiPalette.TextInverse;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = danger ? UiPalette.Danger : UiPalette.Focus;
            button.FlatAppearance.MouseDownBackColor = danger ? UiPalette.DangerHover : UiPalette.BrandPressed;
            button.Font = new Font(button.Font, FontStyle.Bold);
        }

        private static void ApplySecondaryButtonStyle(Button button)
        {
            button.BackColor = UiPalette.SurfaceStrong;
            button.ForeColor = UiPalette.TextPrimary;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = UiPalette.StrokeStrong;
            button.FlatAppearance.MouseOverBackColor = UiPalette.DisabledSoft;
            button.FlatAppearance.MouseDownBackColor = UiPalette.Stroke;
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
                safety?.Lock($"弹窗操作回调异常：{ex.Message}");
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
                    safety?.Lock($"弹窗关闭回调异常：{ex.Message}");
                }
            }
            btn1Action = null;
            btn2Action = null;
            btn3Action = null;
            closeFallback = null;
        }
    }
}
