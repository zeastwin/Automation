// 模块：编辑器 / 通用 UI。
// 职责范围：编辑器共享的视觉、弹窗和 WinForms 交互基础设施。

using System.Drawing;

namespace Automation
{
    /// <summary>
    /// 平台界面的唯一颜色契约。基础界面使用深海军蓝与冷中性灰，专业蓝表达交互，
    /// 运行状态继续使用相互独立的语义色。
    /// </summary>
    internal static class UiPalette
    {
        public static readonly Color Background = Color.FromArgb(246, 248, 251);
        public static readonly Color Surface = Color.FromArgb(251, 252, 254);
        public static readonly Color SurfaceStrong = Color.White;
        public static readonly Color SurfaceSubtle = Color.FromArgb(241, 245, 249);
        public static readonly Color SurfaceHover = Color.FromArgb(239, 246, 255);
        public static readonly Color SurfacePressed = Color.FromArgb(226, 232, 240);
        public static readonly Color Input = Color.White;
        public static readonly Color InputFocused = Color.FromArgb(247, 250, 255);

        public static readonly Color TextPrimary = Color.FromArgb(15, 23, 42);
        public static readonly Color TextSecondary = Color.FromArgb(71, 85, 105);
        public static readonly Color TextMuted = Color.FromArgb(100, 116, 139);
        public static readonly Color TextDisabled = Color.FromArgb(148, 163, 184);
        public static readonly Color TextInverse = Color.White;

        public static readonly Color Stroke = Color.FromArgb(226, 232, 240);
        public static readonly Color StrokeStrong = Color.FromArgb(203, 213, 225);
        public static readonly Color Divider = Color.FromArgb(237, 242, 247);

        public static readonly Color Brand = Color.FromArgb(37, 99, 235);
        public static readonly Color BrandHover = Color.FromArgb(29, 78, 216);
        public static readonly Color BrandPressed = Color.FromArgb(30, 64, 175);
        public static readonly Color BrandAccent = Color.FromArgb(59, 130, 246);
        public static readonly Color BrandSoft = Color.FromArgb(239, 246, 255);
        public static readonly Color BrandSoftHover = Color.FromArgb(219, 234, 254);
        public static readonly Color Selection = Color.FromArgb(219, 234, 254);
        public static readonly Color SelectionText = Color.FromArgb(30, 58, 138);
        public static readonly Color Focus = Color.FromArgb(59, 130, 246);

        public static readonly Color Navigation = Color.FromArgb(15, 23, 42);
        public static readonly Color NavigationHover = Color.FromArgb(23, 32, 51);
        public static readonly Color NavigationActive = Color.FromArgb(31, 59, 82);
        public static readonly Color NavigationActiveAccent = Color.FromArgb(87, 155, 213);
        public static readonly Color NavigationBorder = Color.FromArgb(36, 50, 74);
        public static readonly Color NavigationText = Color.FromArgb(248, 250, 252);
        public static readonly Color NavigationTextMuted = Color.FromArgb(203, 213, 225);
        public static readonly Color NavigationIcon = Color.FromArgb(148, 163, 184);
        public static readonly Color NavigationAccent = Color.FromArgb(96, 165, 250);

        // 顶部菜单栏沿用独立色组，避免菜单换色影响其他以 Navigation 表达深色标题的界面。
        public static readonly Color MenuBackground = Color.FromArgb(31, 53, 53);
        public static readonly Color MenuHover = Color.FromArgb(52, 84, 84);
        public static readonly Color MenuPressed = Color.FromArgb(61, 96, 96);
        public static readonly Color MenuActive = MenuBackground;
        public static readonly Color MenuBorder = Color.White;
        public static readonly Color MenuText = SystemColors.ActiveCaption;
        public static readonly Color MenuActiveText = MenuText;
        public static readonly Color MenuIcon = MenuText;
        public static readonly Color MenuActiveAccent = MenuActiveText;

        public static readonly Color Success = Color.FromArgb(21, 128, 61);
        public static readonly Color SuccessHover = Color.FromArgb(22, 101, 52);
        public static readonly Color SuccessSoft = Color.FromArgb(240, 253, 244);
        public static readonly Color Warning = Color.FromArgb(180, 83, 9);
        public static readonly Color WarningHover = Color.FromArgb(146, 64, 14);
        public static readonly Color WarningSoft = Color.FromArgb(255, 251, 235);
        public static readonly Color Danger = Color.FromArgb(220, 38, 38);
        public static readonly Color DangerHover = Color.FromArgb(185, 28, 28);
        public static readonly Color DangerSoft = Color.FromArgb(254, 242, 242);
        public static readonly Color Info = Brand;
        public static readonly Color InfoSoft = BrandSoft;
        public static readonly Color Transition = Color.FromArgb(194, 65, 12);
        public static readonly Color TransitionSoft = Color.FromArgb(255, 247, 237);
        public static readonly Color Stopping = Color.FromArgb(190, 24, 93);
        public static readonly Color StoppingSoft = Color.FromArgb(253, 242, 248);
        public static readonly Color Breakpoint = Color.FromArgb(219, 39, 119);
        public static readonly Color BreakpointSoft = Color.FromArgb(253, 242, 248);
        public static readonly Color Disabled = Color.FromArgb(148, 163, 184);
        public static readonly Color DisabledSoft = Color.FromArgb(241, 245, 249);

        // 指令列表需要比通用禁用控件更明确的灰阶，确保整行禁用状态一眼可辨。
        public static readonly Color InstructionDisabledBackground = Color.FromArgb(216, 218, 221);
        public static readonly Color InstructionDisabledSelected = Color.FromArgb(196, 201, 207);
        public static readonly Color InstructionDisabledText = Color.FromArgb(88, 96, 105);

        public static readonly Color JumpAutomatic = Color.FromArgb(13, 148, 136);
        public static readonly Color JumpCancel = Brand;
        public static readonly Color JumpDefault = Color.FromArgb(71, 85, 105);
        public static readonly Color JumpMatch = Color.FromArgb(161, 98, 7);

        public static readonly Color HmiBackground = Color.FromArgb(241, 245, 249);
        public static readonly Color HmiHeader = Navigation;
        public static readonly Color HmiHeaderHover = NavigationHover;
        public static readonly Color HmiHeaderActive = Color.FromArgb(30, 64, 175);
        public static readonly Color HmiSection = Color.FromArgb(30, 41, 59);

        public static readonly Color ChartGrid = Stroke;
        public static readonly Color ChartLine = BrandAccent;
        public static readonly Color ChartLabel = TextMuted;
        public static readonly Color Shadow = Color.FromArgb(22, 15, 23, 42);
        public static readonly Color ShadowStrong = Color.FromArgb(36, 15, 23, 42);
    }
}
