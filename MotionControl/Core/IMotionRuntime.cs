using System;
// 模块：运动控制 / 核心。
// 职责范围：定义统一运动运行契约、运动协调和轴状态缓存。
// 边界说明：业务层只依赖此契约，不得依赖 LTDMC 返回码或具体驱动对象。

using System.Collections.Generic;

namespace Automation.MotionControl
{
    /// <summary>轴命令的安全校验类别；回原允许的前置状态与普通运动不同。</summary>
    public enum AxisCommandKind
    {
        Motion,
        Home
    }

    /// <summary>一次命令前需要共同校验的控制卡、轴和命令类别。</summary>
    public sealed class AxisCommandRequest
    {
        public ushort Card { get; }
        public ushort Axis { get; }
        public AxisCommandKind Kind { get; }

        public AxisCommandRequest(ushort card, ushort axis, AxisCommandKind kind)
        {
            Card = card;
            Axis = axis;
            Kind = kind;
        }
    }

    /// <summary>多轴直线插补请求；Axes 与 Positions 必须一一对应。</summary>
    public sealed class CoordinatedLinearMoveRequest
    {
        public ushort Card { get; set; }
        public ushort CoordinateSystem { get; set; }
        public IReadOnlyList<ushort> Axes { get; set; }
        public IReadOnlyList<double> Positions { get; set; }
        public ushort PositionMode { get; set; }
        public double MaxVelocity { get; set; }
        public double AccelerationTime { get; set; }
        public double DecelerationTime { get; set; }
    }

    /// <summary>批量输出中的单点目标状态。</summary>
    public sealed class IoOutputCommand
    {
        public IO Io { get; }
        public bool TargetState { get; }

        public IoOutputCommand(IO io, bool targetState)
        {
            Io = io ?? throw new ArgumentNullException(nameof(io));
            TargetState = targetState;
        }
    }

    /// <summary>流程引擎与调试界面共用的数字 IO 运行契约。</summary>
    public interface IIoRuntime
    {
        bool SetIO(IO io, bool isOpen);
        bool SetOutputs(IReadOnlyList<IoOutputCommand> commands);
        bool GetOutIO(IO io, ref bool value);
        bool GetInIO(IO io, ref bool value);
    }

    /// <summary>
    /// 与具体运动卡解耦的运行契约。调用运动命令前必须持有
    /// <see cref="ValidateAxesForCommand"/> 返回的校验作用域。
    /// </summary>
    public interface IMotionRuntime
    {
        bool IsCardInitialized { get; }
        void InitCardType();
        bool InitCard();
        void SettHomeParam(ushort card, ushort axis, ushort dir, ushort speed, ushort homeMode);
        void StartHome(ushort card, ushort axis);
        void CleanPos(ushort card, ushort axis);
        double GetAxisPos(ushort card, ushort axis);
        void SetMovParam(ushort card, ushort axis, double minVel, double maxVel, double acc, double dec, double stopVel, double sPara, int equiv);
        void Mov(ushort card, ushort axis, double distance, ushort positionMode, bool wait);
        void MoveCoordinatedLinear(CoordinatedLinearMoveRequest request);
        bool IsCoordinatedLinearDone(ushort card, ushort coordinateSystem);
        void StopCoordinatedLinear(ushort card, ushort coordinateSystem, ushort stopMode);
        void Jog(ushort card, ushort axis, ushort direction);
        void StopOneAxis(ushort card, ushort axis, ushort stopMode);
        void StopConnect();
        bool HomeStatus(ushort card, ushort axis);
        bool GetInPos(ushort card, ushort axis);
        bool GetAxisSevon(ushort card, ushort axis);
        void SetAxisSevon(ushort card, ushort axis, bool isSevon);
        void DownLoadConfig();
        void SetAllAxisSevonOn();
        void SetAllAxisEquiv();
        void ResetAxisAlarm(ushort card, ushort axis);
        double GetAxisCurSpeed(ushort card, ushort axis);
        uint GetAxisIoStatus(ushort card, ushort axis);
        ushort GetAxisAlarmCode(ushort card, ushort axis);
        /// <summary>
        /// 在当前线程校验安全锁、复位、报警、急停、使能及回原状态；返回值释放后校验不再有效。
        /// </summary>
        IDisposable ValidateAxesForCommand(IReadOnlyCollection<AxisCommandRequest> requests);
    }
}
