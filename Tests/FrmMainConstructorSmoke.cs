using System;
// 模块：测试 / 编辑器构造冒烟。
// 职责范围：验证 FrmMain 构造阶段不隐式初始化平台或依赖已存在的静态窗体。
// 排查入口：失败优先检查构造函数、Designer 初始化和 EditorWorkspace 装配时机。

namespace Automation.Tests
{
    internal static class FrmMainConstructorSmoke
    {
        [STAThread]
        private static int Main()
        {
            try
            {
                using (var form = new FrmMain())
                {
                }
                Console.WriteLine("FrmMain constructor: PASS");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }
    }
}
