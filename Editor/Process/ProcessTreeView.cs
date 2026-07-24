// 模块：编辑器 / 流程。
// 职责范围：保持流程树的水平视口稳定，避免选择节点时原生控件自动横向滚动。

using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Automation
{
    internal sealed class ProcessTreeView : TreeView
    {
        private const int TvsNoHorizontalScroll = 0x8000;
        private const int TvsNoToolTips = 0x0080;
        private const int TvmSetExtendedStyle = 0x112C;
        private const int TvsExDoubleBuffer = 0x0004;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(
            IntPtr windowHandle,
            int message,
            IntPtr wordParameter,
            IntPtr longParameter);

        public ProcessTreeView()
        {
            // TreeView 是原生控件。交互时同时启用 WinForms 与原生缓冲，
            // 合并背景擦除和节点绘制，避免展开、选择时出现中间空白帧。
            SetStyle(
                ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.AllPaintingInWmPaint,
                true);
            DoubleBuffered = true;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                // 原生 TreeView 会为被截断的标签自动创建提示窗体。
                // 展开后鼠标仍停在节点区域时，提示窗体反复显隐会被感知为节点闪烁。
                parameters.Style |= TvsNoHorizontalScroll | TvsNoToolTips;
                return parameters;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            SendMessage(
                Handle,
                TvmSetExtendedStyle,
                new IntPtr(TvsExDoubleBuffer),
                new IntPtr(TvsExDoubleBuffer));
        }
    }
}
