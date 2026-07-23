using System;
// 模块：核心测试 / 编辑器外壳。
// 职责范围：验证 FrmMain 构造阶段不隐式初始化平台或依赖既有静态窗体。

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Automation.Core.Tests
{
    [TestClass]
    [DoNotParallelize]
    public sealed class EditorShellConstructionTests
    {
        [TestMethod]
        [TestCategory("Desktop")]
        public void Constructor_DoesNotRequireAnInitializedPlatform()
        {
            StaTestRunner.Run(() =>
            {
                using (var form = new FrmMain())
                {
                    Assert.IsNotNull(form);
                    Assert.IsFalse(form.frmAiAssistant.IsViewLoaded,
                        "平台编辑器构造阶段不应提前加载隐藏的 AI WebView。");
                }
            }, TimeSpan.FromSeconds(10));
        }
    }
}
