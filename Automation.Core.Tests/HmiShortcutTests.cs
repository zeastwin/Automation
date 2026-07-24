using System;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using System.Windows.Forms;
using Automation.DeviceSdk;
using Automation.Hmi;
using Microsoft.VisualStudio.TestTools.UnitTesting;

// 模块：核心测试 / HMI 快捷键。
// 职责范围：固化设备 HMI 唤出平台编辑器的按键契约。

namespace Automation.Core.Tests
{
    [TestClass]
    [DoNotParallelize]
    public sealed class HmiShortcutTests
    {
        [TestMethod]
        [TestCategory("Desktop")]
        public void AltZ_ShowsPlatformEditorAndConsumesShortcut()
        {
            StaTestRunner.Run(() =>
            {
                var platformProxy = new RecordingPlatformProxy();
                using (var form = new FrmHmiMain())
                {
                    FieldInfo platformField = typeof(FrmHmiMain).GetField(
                        "platform",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    MethodInfo processCmdKey = typeof(FrmHmiMain).GetMethod(
                        "ProcessCmdKey",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.IsNotNull(platformField);
                    Assert.IsNotNull(processCmdKey);
                    platformField.SetValue(form, platformProxy.Platform);
                    try
                    {
                        var message = new System.Windows.Forms.Message();
                        object[] arguments =
                        {
                            message,
                            Keys.Alt | Keys.Z
                        };
                        bool handled = (bool)processCmdKey.Invoke(form, arguments);

                        Assert.IsTrue(handled, "Alt+Z 应由 HMI 消费，不能继续传给当前输入控件。");
                        Assert.AreEqual(
                            1,
                            platformProxy.ShowPlatformEditorCallCount,
                            "Alt+Z 应准确唤出一次平台编辑器。");
                    }
                    finally
                    {
                        platformField.SetValue(form, null);
                    }
                }
            }, TimeSpan.FromSeconds(10));
        }

        private sealed class RecordingPlatformProxy : RealProxy
        {
            internal RecordingPlatformProxy()
                : base(typeof(IAutomationPlatform))
            {
                Platform = (IAutomationPlatform)GetTransparentProxy();
            }

            internal IAutomationPlatform Platform { get; }

            internal int ShowPlatformEditorCallCount { get; private set; }

            public override IMessage Invoke(IMessage message)
            {
                var call = (IMethodCallMessage)message;
                if (call.MethodName == nameof(IAutomationPlatform.ShowPlatformEditor))
                {
                    ShowPlatformEditorCallCount++;
                }
                return new ReturnMessage(
                    null,
                    null,
                    0,
                    call.LogicalCallContext,
                    call);
            }
        }
    }
}
