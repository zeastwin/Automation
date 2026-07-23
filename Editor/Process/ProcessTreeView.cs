// 模块：编辑器 / 流程。
// 职责范围：保持流程树的水平视口稳定，避免选择节点时原生控件自动横向滚动。

using System.Windows.Forms;

namespace Automation
{
    internal sealed class ProcessTreeView : TreeView
    {
        private const int TvsNoHorizontalScroll = 0x8000;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.Style |= TvsNoHorizontalScroll;
                return parameters;
            }
        }
    }
}
