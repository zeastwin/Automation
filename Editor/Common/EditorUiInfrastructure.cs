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
            Invalidate(true);
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

