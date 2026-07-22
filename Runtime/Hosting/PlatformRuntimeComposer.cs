using System;
// 模块：运行时 / 宿主组合。
// 职责范围：负责平台入口、实例组合、初始化、路径和宿主对外生命周期。
// 排查入口：设备实现选择只由 AutomationRuntimeOptions 决定；仿真与真实硬件装配错误从 Compose 定位。

using System.Collections.Generic;
using Automation.MotionControl;
using Automation.Simulation;

namespace Automation
{
    /// <summary>
    /// 平台内核的组合结果。UI 只负责提供交互端口和日志实现，
    /// 设备、EngineContext、流程引擎及手动运动服务由本组合器统一创建。
    /// </summary>
    internal sealed class PlatformRuntimeComposition
    {
        public PlatformRuntimeComposition(
            ProcessEngine processEngine,
            IMotionRuntime motion,
            IIoRuntime io)
        {
            ProcessEngine = processEngine ?? throw new ArgumentNullException(nameof(processEngine));
            Motion = motion ?? throw new ArgumentNullException(nameof(motion));
            Io = io ?? throw new ArgumentNullException(nameof(io));
        }

        public ProcessEngine ProcessEngine { get; }
        public IMotionRuntime Motion { get; }
        public IIoRuntime Io { get; }
    }

    /// <summary>
    /// 无 WinForms 依赖的平台内核组合器。同一 PlatformRuntime 只允许组合一次。
    /// </summary>
    internal static class PlatformRuntimeComposer
    {
        public static PlatformRuntimeComposition Compose(
            PlatformRuntime runtime,
            IProcessPopupService popupService,
            IAlarmHandler alarmHandler,
            ILogger logger)
        {
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            if (popupService == null) throw new ArgumentNullException(nameof(popupService));
            if (alarmHandler == null) throw new ArgumentNullException(nameof(alarmHandler));
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            if (runtime.ProcessEngine != null || runtime.Motion != null || runtime.Io != null)
            {
                throw new InvalidOperationException("当前平台实例已经完成内核组合。");
            }

            IMotionRuntime motion;
            IIoRuntime io;
            SimulationGatewayClient simulationGateway = null;
            if (AutomationRuntimeOptions.Current.IsSimulation)
            {
                simulationGateway = new SimulationGatewayClient();
                motion = simulationGateway;
                io = simulationGateway;
            }
            else
            {
                var hardwareMotion = new MotionCtrl(
                    runtime.Stores.Values,
                    runtime.Stores.Cards,
                    runtime.Safety,
                    runtime.Readiness);
                motion = hardwareMotion;
                io = hardwareMotion;
            }

            var engineContext = new EngineContext
            {
                Procs = new List<Proc>(),
                ValueStore = runtime.Stores.Values,
                DataStructStore = runtime.Stores.DataStructures,
                TrayPointStore = runtime.Stores.TrayPoints,
                CardStore = runtime.Stores.Cards,
                Motion = motion,
                Io = io,
                Comm = runtime.Communication,
                CommunicationStore = runtime.Stores.Communication,
                PlcRuntime = runtime.PlcRuntime,
                PlcStore = runtime.Stores.Plc,
                Paths = runtime.Paths,
                AlarmInfoStore = runtime.Stores.Alarms,
                IoMap = runtime.Stores.IoConfiguration.ByName,
                Stations = runtime.Stores.Stations.Items,
                SocketInfos = new List<SocketInfo>(),
                SerialPortInfos = new List<SerialPortInfo>(),
                CustomFunc = runtime.CustomFunctions,
                AxisStatuses = new AxisStatusCache(),
                AxisMotionParameters = new AxisMotionParameterStore(),
                Safety = runtime.Safety,
                Maintenance = runtime.Maintenance,
                Readiness = runtime.Readiness,
                ValidationContextFactory = runtime.CreateProcessValidationContext,
                PopupService = popupService
            };
            var processEngine = new ProcessEngine(engineContext)
            {
                Logger = logger,
                AlarmHandler = alarmHandler
            };

            runtime.ProcessEngine = processEngine;
            runtime.ProcessControl = new ProcessRuntimeControl(processEngine);
            runtime.Motion = motion;
            runtime.Io = io;
            runtime.ManualMotion = new ManualMotionService(
                motion,
                processEngine,
                runtime.Stores.Values,
                () => runtime.Readiness.MotionConfigRestartRequired,
                runtime.Safety.Lock);
            runtime.Devices = new PlatformDeviceCoordinator(runtime, simulationGateway);

            return new PlatformRuntimeComposition(processEngine, motion, io);
        }
    }
}
