using System;
// 模块：核心测试 / 宿主降级启动。
// 职责范围：验证设备、AI 和流程配置故障不会越权关闭平台编辑入口。

using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Automation.Core.Tests
{
    [TestClass]
    public sealed class AutomationPlatformHostDegradedStartupTests
    {
        [TestMethod]
        public void DeviceFault_DuringInitialization_DoesNotPromoteHostToFaulted()
        {
            AutomationPlatformHost host = CreateHostInState(PlatformRuntimeState.Initializing);
            try
            {
                InvokeDeviceFault(host, "雷赛卡未连接");

                Assert.AreEqual(PlatformRuntimeState.Initializing, host.State);
                Assert.AreNotEqual(PlatformRuntimeState.Faulted, host.State);
            }
            finally
            {
                DisposeWithoutRuntimeShutdown(host);
            }
        }

        [TestMethod]
        public void DeviceFault_AfterStartup_KeepsEditorCapableReadyStateAndPublishesReason()
        {
            AutomationPlatformHost host = CreateHostInState(PlatformRuntimeState.Ready);
            try
            {
                var states = new List<PlatformRuntimeStateChangedEventArgs>();
                host.RuntimeStateChanged += (sender, args) => states.Add(args);

                InvokeDeviceFault(host, "机器人连接失败");

                Assert.AreEqual(PlatformRuntimeState.Ready, host.State);
                StringAssert.Contains(host.StateMessage, "编辑器");
                StringAssert.Contains(host.StateMessage, "机器人连接失败");
                Assert.AreEqual(1, states.Count);
                Assert.AreEqual(PlatformRuntimeState.Ready, states[0].State);
            }
            finally
            {
                DisposeWithoutRuntimeShutdown(host);
            }
        }

        [TestMethod]
        public void MonitorValue_InFaultedState_RemainsAvailableForDiagnostics()
        {
            AutomationPlatformHost host = CreateHostInState(PlatformRuntimeState.Faulted);
            try
            {
                Assert.IsTrue(host.Runtime.Stores.Values.TrySetValue(
                    0,
                    "诊断变量",
                    "double",
                    "0",
                    "宿主降级测试"));

                Assert.IsTrue(
                    host.TryMonitorValue("诊断变量", true, out string error),
                    error);
                Assert.IsTrue(host.Runtime.Stores.Values.IsMonitored(0));
            }
            finally
            {
                DisposeWithoutRuntimeShutdown(host);
            }
        }

        [TestMethod]
        public void MissingSystemValueMonitor_DoesNotAbortInitialization()
        {
            AutomationPlatformHost host = CreateHostInState(PlatformRuntimeState.Initializing);
            try
            {
                MethodInfo method = typeof(AutomationPlatformHost).GetMethod(
                    "MonitorSystemValue",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(method);

                method.Invoke(host, new object[] { "不存在的系统变量" });

                Assert.AreEqual(PlatformRuntimeState.Initializing, host.State);
            }
            finally
            {
                DisposeWithoutRuntimeShutdown(host);
            }
        }

        [TestMethod]
        public void ProcessLoader_InvalidFileName_ReturnsConfigurationFaultInsteadOfThrowing()
        {
            using (var directory = new TemporaryDirectory())
            {
                string workPath = Path.Combine(directory.FullPath, "Work");
                Directory.CreateDirectory(workPath);
                File.WriteAllText(Path.Combine(workPath, "错误名称.json"), "{}");
                var runtime = new PlatformRuntime(directory.FullPath);

                List<Proc> processes = ProcessWorkDirectoryTransaction.Load(
                    workPath,
                    runtime.CreateProcessValidationContext(),
                    out List<string> errors,
                    out _);

                Assert.AreEqual(0, processes.Count);
                Assert.IsTrue(errors.Exists(error => error.Contains("流程文件名无效")));
            }
        }

        private static AutomationPlatformHost CreateHostInState(PlatformRuntimeState state)
        {
            var host = new AutomationPlatformHost();
            SetStateField(host, state);
            return host;
        }

        private static void InvokeDeviceFault(AutomationPlatformHost host, string message)
        {
            MethodInfo method = typeof(AutomationPlatformHost).GetMethod(
                "OnDeviceFaulted",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method);
            method.Invoke(host, new object[] { message });
        }

        private static void DisposeWithoutRuntimeShutdown(AutomationPlatformHost host)
        {
            SetStateField(host, PlatformRuntimeState.Stopped);
            host.Dispose();
        }

        private static void SetStateField(AutomationPlatformHost host, PlatformRuntimeState state)
        {
            FieldInfo field = typeof(AutomationPlatformHost).GetField(
                "state",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field);
            field.SetValue(host, state);
        }
    }
}
