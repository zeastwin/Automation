// 模块：编辑器 / 通用 UI。
// 职责范围：编辑器共享的视觉、弹窗和 WinForms 交互基础设施。

using System.Drawing;
using System.Windows.Forms;

namespace Automation
{
    internal sealed class WorkspacePageHost : Panel
    {
        private Control activePage;

        internal Control ActivePage => activePage;

        public WorkspacePageHost()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw, true);
        }

        public void ShowPage(Control page)
        {
            if (page == null || page.IsDisposed || ReferenceEquals(activePage, page))
            {
                return;
            }
            SuspendLayout();
            try
            {
                if (!Controls.Contains(page))
                {
                    Controls.Add(page);
                }
                page.Dock = DockStyle.Fill;
                page.Visible = true;
                page.BringToFront();
                if (activePage != null && !activePage.IsDisposed)
                {
                    activePage.Visible = false;
                }
                activePage = page;
            }
            finally
            {
                ResumeLayout(true);
            }
        }

        public bool ReleasePage(Control page)
        {
            if (page == null)
            {
                return false;
            }
            bool wasActive = ReferenceEquals(activePage, page);
            if (wasActive)
            {
                activePage = null;
            }
            Controls.Remove(page);
            return wasActive;
        }
    }

    /// <summary>
    /// 工作区页面右上角的窗口切换按钮，仅显示图标并通过提示说明当前动作。
    /// </summary>
    internal sealed class WorkspaceWindowButton : Button
    {
        private readonly ToolTip toolTip = new ToolTip();
        private Image ownedImage;

        public WorkspaceWindowButton()
        {
            Size = new Size(38, 36);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 1;
            FlatAppearance.BorderColor = UiPalette.Stroke;
            FlatAppearance.MouseOverBackColor = UiPalette.SurfaceHover;
            FlatAppearance.MouseDownBackColor = UiPalette.SurfacePressed;
            BackColor = UiPalette.SurfaceStrong;
            Cursor = Cursors.Hand;
            ImageAlign = ContentAlignment.MiddleCenter;
            Text = string.Empty;
            TabStop = true;
            SetDetached(false);
        }

        public void SetDetached(bool detached)
        {
            Image previous = ownedImage;
            ownedImage = UiIconFactory.Create(
                detached ? UiIconKind.DockBack : UiIconKind.PopOut,
                UiPalette.TextSecondary,
                20);
            Image = ownedImage;
            previous?.Dispose();
            string action = detached ? "嵌回主界面" : "弹出窗口";
            AccessibleName = action;
            toolTip.SetToolTip(this, action);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                toolTip.Dispose();
                ownedImage?.Dispose();
                ownedImage = null;
            }
            base.Dispose(disposing);
        }
    }

    public sealed class FrmInfoLogger : ILogger
    {
        private readonly FrmInfo info;

        public FrmInfoLogger(FrmInfo info)
        {
            this.info = info;
        }

        public void Log(string message, LogLevel level)
        {
            if (info == null || info.IsDisposed)
            {
                return;
            }
            FrmInfo.Level uiLevel = level == LogLevel.Error ? FrmInfo.Level.Error : FrmInfo.Level.Normal;
            info.PrintInfo(message, uiLevel);
        }
    }
}
