using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Automation.DeviceSdk;
using Automation.Hmi;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DeviceValueChangedEventArgs = Automation.DeviceSdk.ValueChangedEventArgs;

// 模块：核心测试 / 旧设备 OpDlg 迁移。
// 职责范围：固化 OpDlg 到 HmiHomePage 的页面映射及 NotifyInfo.Name 同名函数契约。

namespace Automation.Core.Tests
{
    [TestClass]
    [DoNotParallelize]
    public sealed class LegacyOpDlgMigrationTests
    {
        [TestMethod]
        public void RegisteredFunctions_UseExactLegacyMessagesWithoutRelayVariable()
        {
            var service = new EquipmentProcessMessageService(new FakeValueStore());
            IReadOnlyList<string> names = service.GetRegisteredFunctionNames();

            CollectionAssert.Contains(
                names.ToList(),
                "主流程信息||消息 数据本地保存-进站位");
            CollectionAssert.Contains(
                names.ToList(),
                "MES流程信息||消息 进站MES查询");
            CollectionAssert.Contains(
                names.ToList(),
                "HIVE流程信息||HIVE报警监控");
            CollectionAssert.DoesNotContain(names.ToList(), "执行流程消息");
            Assert.IsFalse(names.Any(name => name.Contains("流程处理消息")));
            Assert.AreEqual(60, names.Count);
            Assert.AreEqual(names.Count, names.Distinct(StringComparer.Ordinal).Count());
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void HmiHomePage_MapsOpDlgAndKeepsFiveInnerPages()
        {
            StaTestRunner.Run(() =>
            {
                using (var page = new HmiHomePage())
                {
                    page.CreateControl();
                    string[] expectedButtons =
                    {
                        "主页面",
                        "查看Log",
                        "Hive",
                        "压力曲线图",
                        "扭力曲线图"
                    };
                    List<string> texts = Descendants(page)
                        .OfType<Button>()
                        .Select(button => button.Text)
                        .ToList();
                    foreach (string text in expectedButtons)
                    {
                        CollectionAssert.Contains(texts, text);
                    }
                    Assert.IsTrue(Descendants(page).Any(control =>
                        control is LegacyAlarmTickerControl));
                    Assert.IsTrue(Descendants(page).OfType<GroupBox>().Any(group =>
                        group.Text == "生产参数"));
                }
            }, TimeSpan.FromSeconds(10));
        }

        [TestMethod]
        public void MesDisabled_PreservesOldProjectBypassResult()
        {
            var values = new FakeValueStore();
            values.Add("SN_Code-进站位", "SN-001");
            values.Add("工作模式", 1);
            values.Add("禁用MES", 1);
            values.Add("MES查询结果-进站位", 0);
            values.Add("MES查询备注-进站位", string.Empty);
            var service = new EquipmentProcessMessageService(values);

            service.ExecuteMessage("MES流程信息||消息 进站MES查询");

            Assert.AreEqual("11", values.Get("MES查询结果-进站位"));
            Assert.AreEqual(
                "检查结果：11(禁用MES默认OK)",
                values.Get("MES查询备注-进站位"));
            Assert.AreEqual(1, service.GetSnapshot().InputRecords.Count);
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void HmiShell_MapsLegacyFormMainPagesAndActions()
        {
            StaTestRunner.Run(() =>
            {
                using (var form = new FrmHmiMain())
                {
                    form.CreateControl();
                    string[] expectedButtons =
                    {
                        "主页",
                        "调试",
                        "CCD",
                        "报警",
                        "数据",
                        "启动",
                        "停止",
                        "暂停",
                        "Excel",
                        "Log"
                    };
                    List<string> buttonTexts = Descendants(form)
                        .OfType<Button>()
                        .Select(button => (button.Text ?? string.Empty)
                            .Replace("\r", string.Empty)
                            .Replace("\n", string.Empty))
                        .ToList();
                    foreach (string expected in expectedButtons)
                    {
                        Assert.IsTrue(
                            buttonTexts.Any(text => text.EndsWith(expected, StringComparison.Ordinal)),
                            "缺少旧 FormMain 按钮：" + expected);
                    }

                    Label brand = Descendants(form)
                        .OfType<Label>()
                        .Single(label => label.Name == "lblAutomationBrand");
                    Assert.AreEqual("Automation", brand.Text);
                    Assert.AreEqual("Automation - HMI", form.Text);
                    TableLayoutPanel commandBar = Descendants(form)
                        .OfType<TableLayoutPanel>()
                        .Single(layout => layout.Name == "commandBar");
                    Assert.IsTrue(
                        commandBar.Controls
                            .OfType<Button>()
                            .Where(button => expectedButtons.Any(expected =>
                                (button.Text ?? string.Empty).EndsWith(
                                    expected,
                                    StringComparison.Ordinal)))
                            .All(button =>
                                button.Font.Size <= 9.5F
                                && button.Image != null
                                && button.Image.Width >= 36
                                && button.TextImageRelation == TextImageRelation.ImageAboveText),
                        "顶部按钮应延续旧项目的大图标、小文字布局，不能用放大字号替代图标。");
                    Assert.AreEqual(
                        SizeType.Absolute,
                        ((TableLayoutPanel)commandBar.Parent).RowStyles[0].SizeType);
                    Assert.AreEqual(
                        81F,
                        ((TableLayoutPanel)commandBar.Parent).RowStyles[0].Height,
                        "顶部栏应保持旧项目的 81px 紧凑高度。");
                    Assert.IsFalse(
                        Descendants(form).Any(control =>
                            string.Equals(control.Name, "btnLogin", StringComparison.Ordinal)),
                        "当前平台没有登录机制，顶部栏不应保留登录按钮。");
                    TableLayoutPanel statusLayout = Descendants(form)
                        .OfType<TableLayoutPanel>()
                        .Single(layout => layout.Name == "statusLayout");
                    Assert.AreEqual(1, statusLayout.RowCount);
                    Assert.IsTrue(
                        statusLayout.Controls
                            .OfType<Label>()
                            .Any(label => label.Name == "lblFixtureStatus"),
                        "移除登录按钮后，设备状态应占满右上角状态区域。");

                    Assert.IsTrue(Descendants(form).Any(control => control is HmiHomePage));
                    Assert.IsTrue(Descendants(form).Any(control => control is HmiDebugPage));
                    Assert.IsTrue(Descendants(form).Any(control => control is LegacyVideoPage));
                    Assert.IsTrue(Descendants(form).Any(control => control is AlarmHistoryPage));
                    Assert.IsTrue(Descendants(form).Any(control => control is LegacyDataPage));
                    Assert.IsTrue(Descendants(form).Any(control => control is LegacyExcelPage));
                    Assert.IsTrue(Descendants(form).Any(control => control is LegacyLogPage));
                }
            }, TimeSpan.FromSeconds(10));
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void HmiHome_RemovesLanguageSelectorAndRestoresStateShowModule()
        {
            StaTestRunner.Run(() =>
            {
                using (var home = new HmiHomePage())
                using (var stateShow = new HmiStateShowForm())
                {
                    Assert.IsFalse(
                        Descendants(home).Any(control =>
                            string.Equals(control.Name, "languageLabel", StringComparison.Ordinal)
                            || string.Equals(control.Name, "languageCombo", StringComparison.Ordinal)),
                        "首页不应继续显示旧语言选择模块。");

                    string[] expectedStates =
                    {
                        "Running",
                        "Planned downtime",
                        "Idle",
                        "Engineering",
                        "Manually Downtime",
                        "ResetError"
                    };
                    CollectionAssert.AreEqual(
                        expectedStates,
                        Descendants(stateShow)
                            .OfType<Button>()
                            .Select(button => button.Text)
                            .ToArray(),
                        "StateShow 应恢复旧项目的六种设备状态。");

                    stateShow.ApplyStatus(4);
                    Button plannedDowntime = Descendants(stateShow)
                        .OfType<Button>()
                        .Single(button => button.Text == "Planned downtime");
                    Assert.AreEqual(
                        Color.FromArgb(18, 100, 255).ToArgb(),
                        plannedDowntime.BackColor.ToArgb(),
                        "设备状态 4 应高亮 Planned downtime。");
                }
            }, TimeSpan.FromSeconds(10));
        }

        [TestMethod]
        [TestCategory("Desktop")]
        public void DebugAndVideoPages_KeepLegacyPageStructure()
        {
            StaTestRunner.Run(() =>
            {
                using (var debug = new HmiDebugPage())
                using (var video = new LegacyVideoPage())
                {
                    debug.CreateControl();
                    video.CreateControl();
                    string[] debugEntries =
                    {
                        "MES", "PDCA", "Hive", "PLC",
                        "FingerPrint", "Tools", "Set", "Database"
                    };
                    List<string> debugButtons = Descendants(debug)
                        .OfType<Button>()
                        .Select(button => button.Text)
                        .ToList();
                    foreach (string entry in debugEntries)
                    {
                        CollectionAssert.Contains(debugButtons, entry);
                    }
                    Assert.AreEqual(
                        4,
                        Descendants(video)
                            .OfType<GroupBox>()
                            .Count(group => group.Text.StartsWith("Video", StringComparison.Ordinal)));
                }
            }, TimeSpan.FromSeconds(10));
        }

        private static IEnumerable<Control> Descendants(Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child;
                foreach (Control nested in Descendants(child))
                {
                    yield return nested;
                }
            }
        }

        private sealed class FakeValueStore : IValueStore
        {
            private readonly List<ValueSnapshot> values = new List<ValueSnapshot>();

            public event EventHandler<DeviceValueChangedEventArgs> Changed;

            public void Add(string name, object value)
            {
                values.Add(new ValueSnapshot
                {
                    Id = Guid.NewGuid(),
                    Index = values.Count,
                    Name = name,
                    Type = value is string ? "string" : "double",
                    Value = Convert.ToString(value, CultureInfo.InvariantCulture),
                    Scope = "public"
                });
            }

            public string Get(string name)
            {
                return values.Single(item => item.Name == name).Value;
            }

            public IReadOnlyList<string> GetNames()
            {
                return values.Select(item => item.Name).ToList();
            }

            public bool TryGet(string name, out ValueSnapshot value, out string error)
            {
                value = values.SingleOrDefault(item =>
                    string.Equals(item.Name, name, StringComparison.Ordinal));
                error = value == null ? "变量不存在" : null;
                return value != null;
            }

            public bool TryGet(int index, out ValueSnapshot value, out string error)
            {
                value = values.SingleOrDefault(item => item.Index == index);
                error = value == null ? "变量不存在" : null;
                return value != null;
            }

            public bool Set(string name, object value, out string error)
            {
                if (!TryGet(name, out ValueSnapshot snapshot, out error))
                {
                    return false;
                }
                string oldValue = snapshot.Value;
                snapshot.Value = Convert.ToString(value, CultureInfo.InvariantCulture);
                Changed?.Invoke(this, new DeviceValueChangedEventArgs
                {
                    Id = snapshot.Id,
                    Index = snapshot.Index,
                    Name = snapshot.Name,
                    OldValue = oldValue,
                    NewValue = snapshot.Value,
                    ChangedAt = DateTime.Now
                });
                return true;
            }

            public bool Set(int index, object newValue, out string error)
            {
                if (!TryGet(index, out ValueSnapshot value, out error))
                {
                    return false;
                }
                return Set(value.Name, newValue, out error);
            }

            public bool Monitor(string name, bool enabled, out string error)
            {
                return TryGet(name, out _, out error);
            }
        }
    }
}
