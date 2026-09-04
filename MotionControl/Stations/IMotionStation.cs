// 模块：运动控制 / 工站运行时。
// 职责范围：定义轴工站、EPSON 与汇川机器人共同遵循的六轴运行契约。

using System.Collections.Generic;

namespace Automation.MotionControl
{
    public enum StationMoveMode
    {
        Go = 0,
        Move = 1,
        Jog = 2
    }

    public enum StationAxisMoveMode
    {
        Absolute = 0,
        Relative = 1,
        RelativeByEncoder = 2
    }

    public enum StationSpeedType
    {
        Global = 0,
        Joint = 1,
        Move = 2
    }

    public enum MotionStationState
    {
        Uninitialized = 0,
        Disconnected = 1,
        Idle = 2,
        Moving = 3,
        Faulted = 4
    }

    public enum MotionStationResult
    {
        Success = 0,
        InvalidConfiguration = 1,
        InvalidParameter = 2,
        NotInitialized = 3,
        NotConnected = 4,
        Busy = 5,
        SendFailed = 6,
        ReceiveFailed = 7,
        Timeout = 8,
        CommandRejected = 9,
        BaseFunctionError = 10,
        /// <summary>设备点表与平台配置在补偿回滚后仍无法恢复一致。</summary>
        InconsistentState = 11
    }

    public sealed class MotionStationStatus
    {
        private readonly double[] position = new double[6];

        public MotionStationState State { get; internal set; } = MotionStationState.Uninitialized;

        public IReadOnlyList<double> Position => position;

        public string LastError { get; internal set; } = string.Empty;

        public bool IsHomed { get; internal set; }

        public bool IsServoEnabled { get; internal set; }

        public bool HasAlarm { get; internal set; }

        public int WarningAxis { get; internal set; } = -1;

        internal void SetPosition(IReadOnlyList<double> source)
        {
            if (source == null)
            {
                return;
            }
            int count = source.Count < position.Length ? source.Count : position.Length;
            for (int i = 0; i < count; i++)
            {
                position[i] = source[i];
            }
        }
    }

    /// <summary>
    /// 3.0 工站模型的当前契约。机器人与轴组都以 XYZUVW 六通道工站参与手动和流程运动。
    /// </summary>
    internal interface IMotionStation
    {
        MotionStationResult Initialize();

        MotionStationResult Release();

        MotionStationResult Home(short axis = -1, bool wait = true, bool group = false);

        MotionStationResult SetSpeed(
            double velocity,
            double acceleration,
            double deceleration,
            short axis = -1,
            StationSpeedType type = StationSpeedType.Joint);

        MotionStationResult MoveToPoint(
            DataPos point,
            StationMoveMode mode,
            bool[] disabledAxes = null,
            short tool = 0);

        MotionStationResult MoveOffset(
            int basePointIndex,
            IReadOnlyList<double> offsets,
            StationMoveMode mode = StationMoveMode.Go);

        MotionStationResult AxisMotion(
            short axis,
            double offset,
            StationAxisMoveMode mode = StationAxisMoveMode.Relative,
            short tool = 0);

        MotionStationResult WaitMoveFinish(
            bool isHome = false,
            int axis = -1,
            int timeoutMs = 120000);

        MotionStationResult GetCurrentPosition(short tool, out DataPos position);

        /// <summary>
        /// 把已确认点位同步到工站点表。轴工站只校验平台点位，机器人同时写入控制器点表。
        /// 平台配置文件的提交与补偿回滚由 MotionCtrl 统一协调。
        /// </summary>
        MotionStationResult SavePoint(DataPos point);

        /// <summary>登记料盘参考点；机器人保留 3.0 控制器侧料盘语义。</summary>
        MotionStationResult CreateTray(
            int trayId,
            int rowCount,
            int columnCount,
            IReadOnlyList<DataPos> referencePoints);

        /// <summary>执行料盘点运动；position 为从零开始的料盘位置。</summary>
        MotionStationResult MoveTrayPoint(int trayId, int position, DataPos calculatedPoint);

        MotionStationResult Stop(bool emergency = false);

        MotionStationStatus GetStatus();
    }
}
