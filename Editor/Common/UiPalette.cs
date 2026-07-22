// 模块：编辑器 / 通用 UI。
// 职责范围：编辑器共享的视觉、弹窗和 WinForms 交互基础设施。

using System.Drawing;

namespace Automation
{
    /// <summary>
    /// 平台界面的唯一颜色契约。基础界面使用冷调蓝灰，运行状态使用独立语义色。
    /// </summary>
    internal static class UiPalette
    {
        public static readonly Color Background = Color.FromArgb(244, 247, 250);
        public static readonly Color Surface = Color.FromArgb(251, 252, 254);
        public static readonly Color SurfaceStrong = Color.White;
        public static readonly Color SurfaceSubtle = Color.FromArgb(238, 243, 246);
        public static readonly Color SurfaceHover = Color.FromArgb(230, 239, 244);
        public static readonly Color SurfacePressed = Color.FromArgb(220, 232, 238);
        public static readonly Color Input = Color.FromArgb(248, 250, 252);
        public static readonly Color InputFocused = Color.FromArgb(241, 247, 251);

        public static readonly Color TextPrimary = Color.FromArgb(38, 57, 70);
        public static readonly Color TextSecondary = Color.FromArgb(82, 102, 115);
        public static readonly Color TextMuted = Color.FromArgb(96, 116, 128);
        public static readonly Color TextDisabled = Color.FromArgb(135, 150, 158);
        public static readonly Color TextInverse = Color.FromArgb(246, 250, 252);

        public static readonly Color Stroke = Color.FromArgb(216, 226, 232);
        public static readonly Color StrokeStrong = Color.FromArgb(185, 200, 209);
        public static readonly Color Divider = Color.FromArgb(227, 234, 239);

        public static readonly Color Brand = Color.FromArgb(34, 111, 183);
        public static readonly Color BrandHover = Color.FromArgb(28, 97, 159);
        public static readonly Color BrandPressed = Color.FromArgb(21, 74, 121);
        public static readonly Color BrandAccent = Color.FromArgb(43, 139, 192);
        public static readonly Color BrandSoft = Color.FromArgb(228, 242, 249);
        public static readonly Color BrandSoftHover = Color.FromArgb(216, 235, 245);
        public static readonly Color Selection = Color.FromArgb(217, 234, 247);
        public static readonly Color SelectionText = Color.FromArgb(22, 76, 112);
        public static readonly Color Focus = Color.FromArgb(43, 126, 201);

        public static readonly Color Navigation = Color.FromArgb(27, 43, 59);
        public static readonly Color NavigationHover = Color.FromArgb(34, 55, 73);
        public static readonly Color NavigationActive = Color.FromArgb(36, 59, 78);
        public static readonly Color NavigationBorder = Color.FromArgb(49, 75, 91);
        public static readonly Color NavigationText = Color.FromArgb(221, 233, 239);
        public static readonly Color NavigationTextMuted = Color.FromArgb(168, 188, 199);
        public static readonly Color NavigationAccent = Color.FromArgb(10, 132, 255);

        public static readonly Color Success = Color.FromArgb(37, 122, 85);
        public static readonly Color SuccessHover = Color.FromArgb(31, 104, 72);
        public static readonly Color SuccessSoft = Color.FromArgb(231, 244, 237);
        public static readonly Color Warning = Color.FromArgb(150, 96, 13);
        public static readonly Color WarningHover = Color.FromArgb(126, 80, 11);
        public static readonly Color WarningSoft = Color.FromArgb(255, 242, 214);
        public static readonly Color Danger = Color.FromArgb(185, 54, 61);
        public static readonly Color DangerHover = Color.FromArgb(158, 41, 49);
        public static readonly Color DangerSoft = Color.FromArgb(251, 234, 236);
        public static readonly Color Info = Brand;
        public static readonly Color InfoSoft = BrandSoft;
        public static readonly Color Transition = Color.FromArgb(165, 82, 18);
        public static readonly Color TransitionSoft = Color.FromArgb(252, 235, 221);
        public static readonly Color Stopping = Color.FromArgb(159, 62, 62);
        public static readonly Color StoppingSoft = Color.FromArgb(249, 235, 235);
        public static readonly Color Breakpoint = Color.FromArgb(167, 60, 98);
        public static readonly Color BreakpointSoft = Color.FromArgb(250, 234, 240);
        public static readonly Color Disabled = Color.FromArgb(139, 154, 163);
        public static readonly Color DisabledSoft = Color.FromArgb(238, 242, 244);

        public static readonly Color JumpAutomatic = Color.FromArgb(0, 127, 134);
        public static readonly Color JumpCancel = Color.FromArgb(118, 81, 168);
        public static readonly Color JumpDefault = Color.FromArgb(82, 107, 121);
        public static readonly Color JumpMatch = Color.FromArgb(145, 112, 14);

        public static readonly Color HmiBackground = Color.FromArgb(232, 238, 242);
        public static readonly Color HmiHeader = Color.FromArgb(24, 42, 56);
        public static readonly Color HmiHeaderHover = Color.FromArgb(32, 57, 74);
        public static readonly Color HmiHeaderActive = Color.FromArgb(31, 111, 159);
        public static readonly Color HmiSection = Color.FromArgb(36, 75, 90);

        public static readonly Color ChartGrid = Color.FromArgb(216, 226, 232);
        public static readonly Color ChartLine = Color.FromArgb(43, 126, 201);
        public static readonly Color ChartLabel = Color.FromArgb(96, 116, 128);
        public static readonly Color Shadow = Color.FromArgb(24, 27, 50, 68);
        public static readonly Color ShadowStrong = Color.FromArgb(40, 27, 50, 68);
    }
}
