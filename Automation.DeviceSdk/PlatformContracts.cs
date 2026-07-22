using System;
using System.Collections.Generic;
// 模块：设备 SDK / 平台公开契约。
// 职责范围：设备 HMI 通过本文件读取变量、控制流程和观察平台状态。
// 排查入口：调用失败先看 out error；状态变化只认快照和事件，设备项目不要反向访问平台窗体或 Runtime。

namespace Automation.DeviceSdk
{
    /// <summary>平台宿主生命周期状态；Faulted 仍可能显示 HMI，但受影响能力会被运行闸门禁止。</summary>
    public enum PlatformRuntimeStatus
    {
        Created = 0,
        Initializing = 1,
        Ready = 2,
        Faulted = 3,
        ShuttingDown = 4,
        Stopped = 5
    }

    /// <summary>设备 HMI 可观察的流程状态投影，与平台内部状态数值保持一致。</summary>
    public enum ProcessRuntimeStatus
    {
        Stopped = 0,
        Paused = 1,
        SingleStep = 2,
        Running = 3,
        Alarming = 4,
        Pausing = 5,
        Stopping = 6
    }

    /// <summary>一次只读变量快照；后续变化通过 <see cref="IValueStore.Changed"/> 获取。</summary>
    public sealed class ValueSnapshot
    {
        public Guid Id { get; set; }
        public int Index { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
        public string Value { get; set; }
        public string Scope { get; set; }
        public Guid? OwnerProcId { get; set; }
        public string OwnerProcName { get; set; }
        public string Note { get; set; }
    }

    /// <summary>变量变化事实，包含来源和变化时间，适合定位是谁修改了运行值。</summary>
    public sealed class ValueChangedEventArgs : EventArgs
    {
        public Guid Id { get; set; }
        public int Index { get; set; }
        public string Name { get; set; }
        public string Scope { get; set; }
        public Guid? OwnerProcId { get; set; }
        public string OwnerProcName { get; set; }
        public string OldValue { get; set; }
        public string NewValue { get; set; }
        public string Source { get; set; }
        public DateTime ChangedAt { get; set; }
    }

    /// <summary>流程当前执行位置、报警和可选性能采样的只读快照。</summary>
    public sealed class ProcessSnapshot
    {
        public int Index { get; set; }
        public Guid Id { get; set; }
        public string Name { get; set; }
        public ProcessRuntimeStatus State { get; set; }
        public int StepIndex { get; set; }
        public int OperationIndex { get; set; }
        public bool Disabled { get; set; }
        public bool IsAlarm { get; set; }
        public string AlarmMessage { get; set; }
        public DateTime UpdatedAt { get; set; }
        public bool PerformanceAnalysisEnabled { get; set; }
        public long OperationCount { get; set; }
        public double OperationsPerSecond { get; set; }
        public double ThreadCpuPercent { get; set; }
        public double AverageOperationMicroseconds { get; set; }
        public double MaxOperationMicroseconds { get; set; }
        public long OperationDurationSampleCount { get; set; }
        public int OperationDurationSamplingInterval { get; set; }
        public bool AbnormalCpuLoopDetected { get; set; }
    }

    /// <summary>流程状态变化事件参数。</summary>
    public sealed class ProcessChangedEventArgs : EventArgs
    {
        public ProcessSnapshot Process { get; set; }
    }

    /// <summary>平台状态变化事件参数；Message 是当前已验证状态说明，不是异常堆栈。</summary>
    public sealed class PlatformRuntimeStatusChangedEventArgs : EventArgs
    {
        public PlatformRuntimeStatusChangedEventArgs(PlatformRuntimeStatus status, string message)
        {
            Status = status;
            Message = message ?? string.Empty;
        }

        public PlatformRuntimeStatus Status { get; }

        public string Message { get; }
    }

    /// <summary>
    /// 设备 HMI 使用的变量门面。TryGet/Set 返回 false 时，<c>error</c> 给出可展示的失败原因。
    /// </summary>
    public interface IValueStore
    {
        /// <summary>变量变化事件在平台 UI 线程触发，可直接刷新 WinForms 控件。</summary>
        event EventHandler<ValueChangedEventArgs> Changed;

        /// <summary>返回当前可见变量名快照。</summary>
        IReadOnlyList<string> GetNames();

        /// <summary>按精确名称读取变量。</summary>
        bool TryGet(string name, out ValueSnapshot value, out string error);

        /// <summary>按当前配置索引读取变量；跨配置版本持有身份时优先使用名称或快照 Id。</summary>
        bool TryGet(int index, out ValueSnapshot value, out string error);

        /// <summary>设置单个运行值，不修改变量定义。</summary>
        bool Set(string name, object value, out string error);

        /// <summary>按当前配置索引设置单个运行值，不修改变量定义。</summary>
        bool Set(int index, object value, out string error);

        /// <summary>启用或取消该变量的变化通知；不影响变量本身读写。</summary>
        bool Monitor(string name, bool enabled, out string error);
    }

    /// <summary>设备 HMI 使用的流程运行门面；不提供流程结构编辑能力。</summary>
    public interface IProcessStore
    {
        /// <summary>流程状态事件在平台 UI 线程触发，可直接刷新 WinForms 控件。</summary>
        event EventHandler<ProcessChangedEventArgs> Changed;

        /// <summary>返回全部流程当前快照。</summary>
        IReadOnlyList<ProcessSnapshot> GetAll();

        /// <summary>启动停止态流程；流程仍在运行时返回 false，并保留当前实例。</summary>
        bool Start(int processIndex, out string error);

        /// <summary>请求流程暂停。</summary>
        bool Pause(int processIndex, out string error);

        /// <summary>恢复已暂停流程。</summary>
        bool Resume(int processIndex, out string error);

        /// <summary>请求停止单个流程。</summary>
        bool Stop(int processIndex, out string error);

        /// <summary>请求停止全部流程；用于设备 HMI 的正常控制，不替代平台安全停机链。</summary>
        bool StopAll(out string error);
    }

    /// <summary>
    /// 设备工程访问 Automation 平台的唯一入口。由宿主创建和释放，业务页面只持有接口引用。
    /// </summary>
    public interface IAutomationPlatform : IDisposable
    {
        /// <summary>平台状态事件在平台 UI 线程触发，可直接刷新 WinForms 控件。</summary>
        event EventHandler<PlatformRuntimeStatusChangedEventArgs> RuntimeStatusChanged;

        /// <summary>当前平台生命周期状态。</summary>
        PlatformRuntimeStatus RuntimeStatus { get; }

        /// <summary>SDK 契约版本，用于判断设备工程与平台接口是否匹配。</summary>
        string ApiVersion { get; }

        /// <summary>当前 Automation 平台产品版本。</summary>
        string PlatformVersion { get; }

        /// <summary>当前状态的简短说明；初始化失败详情优先读取 Initialize 的 error。</summary>
        string RuntimeMessage { get; }

        /// <summary>变量运行门面。</summary>
        IValueStore Values { get; }

        /// <summary>流程运行门面。</summary>
        IProcessStore Processes { get; }

        /// <summary>在宿主 UI 线程初始化平台；失败返回 false，已创建资源由平台统一清理。</summary>
        bool Initialize(out string error);

        /// <summary>初始化前注册设备业务函数；同名函数的冲突由平台返回明确异常。</summary>
        void RegisterCustomFunction(string name, Action function);

        /// <summary>显示平台编辑器；仅用于授权的配置和维护场景。</summary>
        void ShowPlatformEditor();

        /// <summary>隐藏平台编辑器，不停止平台运行时。</summary>
        void HidePlatformEditor();

        /// <summary>显示运行诊断页；功能是否可用由平台配置决定。</summary>
        void ShowRuntimeDiagnostics();

        /// <summary>显示性能分析页；性能采样不改变流程运行状态。</summary>
        void ShowPerformanceAnalysis();

        /// <summary>通知平台设备交互窗口已可用，使等待中的弹窗请求能够继续。</summary>
        void NotifyInteractionUiReady();

        /// <summary>进入幂等关闭链；重复调用不会创建第二条资源释放流程。</summary>
        void Shutdown();
    }
}
