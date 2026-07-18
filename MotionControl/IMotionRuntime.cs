using System;
using System.Collections.Generic;

namespace Automation.MotionControl
{
    public enum AxisCommandKind
    {
        Motion,
        Home
    }

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

    public interface IIoRuntime
    {
        bool SetIO(IO io, bool isOpen);
        bool SetOutputs(IReadOnlyList<IoOutputCommand> commands);
        bool GetOutIO(IO io, ref bool value);
        bool GetInIO(IO io, ref bool value);
    }

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
        void StageManualMotionParameters(ushort card, ushort axis, double minVel, double maxVel, double acc, double dec, double stopVel, double sPara, int equiv);
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
        IDisposable ValidateAxesForCommand(IReadOnlyCollection<AxisCommandRequest> requests);
    }
}
