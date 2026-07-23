using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Automation.Core.Tests
{
    [TestClass]
    [DoNotParallelize]
    public sealed class InfoLogLayoutTests
    {
        [TestMethod]
        [TestCategory("Desktop")]
        public void ContentAndBottomTabs_UseSeparateNonOverlappingRows()
        {
            StaTestRunner.Run(() =>
            {
                using (var form = new FrmInfo { ClientSize = new Size(800, 220) })
                {
                    TableLayoutPanel layout = GetField<TableLayoutPanel>(form, "infoContentLayout");
                    Panel content = GetField<Panel>(form, "infoContentHost");
                    Panel tabs = GetField<Panel>(form, "infoTabBar");

                    form.PerformLayout();
                    layout.PerformLayout();

                    Assert.AreEqual(content.Bottom, tabs.Top,
                        "日志内容区底部必须与页签顶部直接相接。");
                    Assert.AreEqual(layout.ClientSize.Height, tabs.Bottom,
                        "页签必须占用独立的底部布局行。");
                }
            }, TimeSpan.FromSeconds(10));
        }

        private static T GetField<T>(object owner, string name)
            where T : class
        {
            return owner.GetType()
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(owner) as T
                ?? throw new InvalidOperationException($"未找到字段：{name}");
        }
    }
}
