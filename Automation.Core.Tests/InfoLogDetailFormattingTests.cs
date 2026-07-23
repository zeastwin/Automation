using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Automation.Core.Tests
{
    [TestClass]
    public class InfoLogDetailFormattingTests
    {
        [TestMethod]
        public void FormatInfoLogDetail_ReadinessErrorsBecomeNumberedItems()
        {
            string result = FrmInfo.FormatInfoLogDetail(
                "[2026-07-23 11时49分03秒]",
                "：启动流程失败：流程配置尚不可运行：步骤 0 指令 0 [初始化计数器] 的 ValueSourceName 引用的变量不存在；步骤 0 指令 1 [初始化计数器] 的 OutputValueName 引用的变量不存在",
                FrmInfo.Level.Error);

            StringAssert.Contains(result, "时间    2026-07-23 11时49分03秒");
            StringAssert.Contains(result, "级别    错误");
            StringAssert.Contains(result, "摘要\r\n启动流程失败：流程配置尚不可运行：");
            StringAssert.Contains(result, "问题明细（2）");
            StringAssert.Contains(result, "1. 步骤 0 指令 0");
            StringAssert.Contains(result, "2. 步骤 0 指令 1");
        }

        [TestMethod]
        public void FormatInfoLogDetail_NormalMessageKeepsOriginalContent()
        {
            string result = FrmInfo.FormatInfoLogDetail(
                "[2026-07-23 12时00分00秒]",
                "：平台初始化完成。",
                FrmInfo.Level.Normal);

            StringAssert.Contains(result, "级别    信息");
            StringAssert.Contains(result, "日志内容\r\n平台初始化完成。");
            Assert.IsFalse(result.Contains("问题明细"));
        }
    }
}
