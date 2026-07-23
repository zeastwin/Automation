using Automation.MotionControl;
// 模块：运行时 / 宿主组合。
// 职责范围：负责平台入口、实例组合、初始化、路径和宿主对外生命周期。
// 状态所有权：本对象是单实例组合根；某运行服务为空通常表示 Compose/Initialize 尚未完成或已进入关闭。

using System;
using System.Collections.Generic;
using System.Linq;

namespace Automation
{
    /// <summary>
    /// 平台实例拥有的配置 Store。调用方应优先接收所需的具体 Store，
    /// 本对象仅用于组合根装配和需要跨 Store 事务的应用服务。
    /// </summary>
    public sealed class PlatformStores
    {
        internal PlatformStores(PlatformRuntime runtime)
        {
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            Processes = new ProcessDefinitionRepository();
            IoConfiguration = new IoConfigurationStore();
            IoDebug = new IoDebugConfigurationStore();
            ValueDebug = new ValueDebugConfigurationStore();
            Stations = new StationDefinitionStore();
            Cards = new CardConfigStore();
            Values = new ValueConfigStore(runtime);
            DataStructures = new DataStructStore(runtime);
            TrayPoints = new TrayPointStore();
            Alarms = new AlarmInfoStore(runtime);
            Communication = new CommunicationConfigStore();
            Plc = new PlcConfigStore();
        }

        public ProcessDefinitionRepository Processes { get; }
        public IoConfigurationStore IoConfiguration { get; }
        public IoDebugConfigurationStore IoDebug { get; }
        public ValueDebugConfigurationStore ValueDebug { get; }
        public StationDefinitionStore Stations { get; }
        public CardConfigStore Cards { get; }
        public ValueConfigStore Values { get; }
        public DataStructStore DataStructures { get; }
        public TrayPointStore TrayPoints { get; }
        public AlarmInfoStore Alarms { get; }
        public CommunicationConfigStore Communication { get; }
        public PlcConfigStore Plc { get; }
    }

    /// <summary>
    /// 单个平台实例的运行时组合。不存在进程级 Current；由宿主创建并显式传递。
    /// </summary>
    public sealed class PlatformRuntime
    {
        public PlatformRuntime(string configPath = null)
        {
            Paths = new PlatformPaths(configPath);
            Readiness = new PlatformReadinessState();
            Maintenance = new ConfigurationMaintenanceService();
            Safety = new PlatformSafetyCoordinator(this);
            ShutdownCoordinator = new PlatformShutdownCoordinator(this);
            Editor = new EditorSessionCoordinator(this);
            Stores = new PlatformStores(this);
            ProcessEditing = new ProcessEditingPolicy(this);
            OperationEditing = new OperationEditingService(this);
            ProcessPublication = new ProcessPublicationService(this);
            ProcessVariableConfiguration = new ProcessVariableConfigurationService(this);
            VariableDebug = new VariableDebugService(this);
            CustomFunctions = new CustomFunc();
            Communication = new CommunicationHub();
            PlcRuntime = new PlcRuntimeService(Stores.Plc, Stores.Values);
            VersionService = new ConfigurationVersionService(Paths.ConfigPath, this);
        }

        public PlatformPaths Paths { get; }
        public PlatformStores Stores { get; }
        public PlatformReadinessState Readiness { get; }
        public ConfigurationMaintenanceService Maintenance { get; }
        public PlatformSafetyCoordinator Safety { get; }
        internal PlatformShutdownCoordinator ShutdownCoordinator { get; }
        public EditorSessionCoordinator Editor { get; }
        public ProcessEditingPolicy ProcessEditing { get; }
        public OperationEditingService OperationEditing { get; }
        public ProcessPublicationService ProcessPublication { get; }
        public ProcessVariableConfigurationService ProcessVariableConfiguration { get; }
        internal VariableDebugService VariableDebug { get; }

        public ProcessEngine ProcessEngine { get; internal set; }
        public IProcessRuntimeControl ProcessControl { get; internal set; }
        public IMotionRuntime Motion { get; internal set; }
        public IIoRuntime Io { get; internal set; }
        public ManualMotionService ManualMotion { get; internal set; }
        internal PlatformDeviceCoordinator Devices { get; set; }
        internal WinFormsProcessInteractionCoordinator ProcessInteraction { get; set; }
        internal PlatformSystemStatusService SystemStatus { get; set; }
        public CustomFunc CustomFunctions { get; }
        public CommunicationHub Communication { get; }
        public PlcRuntimeService PlcRuntime { get; }
        public ConfigurationVersionService VersionService { get; }
        public IPlatformEditorUiAdapter EditorUi { get; internal set; }
        internal RuntimeBlackBoxRecorder RuntimeBlackBoxRecorder { get; set; }

        public ProcessDefinitionValidationContext CreateProcessValidationContext()
        {
            Dictionary<string, DicValue> variables = Stores.Values.BuildSaveData();
            return new ProcessDefinitionValidationContext(
                variables.Keys,
                Stores.Communication.GetSocketSnapshot()
                    .Where(item => item != null).Select(item => item.Name),
                Stores.Communication.GetSerialSnapshot()
                    .Where(item => item != null).Select(item => item.Name),
                Stores.Alarms.GetValidIndices().Select(index => index.ToString()),
                Stores.Plc.GetSnapshot().Devices
                    .Where(item => item != null).Select(item => item.Name),
                variables,
                this);
        }
    }
}
