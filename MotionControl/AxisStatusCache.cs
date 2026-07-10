using System;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Automation.MotionControl
{
    // 状态缓存只用于界面显示和普通信号观察；运动命令门禁必须直接读取硬件状态。
    public sealed class AxisStatusSnapshot
    {
        internal AxisStatusSnapshot(
            ushort card,
            ushort axis,
            uint ioStatus,
            long ioUpdatedTicks,
            bool isStopped,
            bool isHomed,
            bool servoOn,
            double position,
            double speed,
            ushort alarmCode,
            long detailUpdatedTicks)
        {
            Card = card;
            Axis = axis;
            IoStatus = ioStatus;
            IoUpdatedTicks = ioUpdatedTicks;
            IsStopped = isStopped;
            IsHomed = isHomed;
            ServoOn = servoOn;
            Position = position;
            Speed = speed;
            AlarmCode = alarmCode;
            DetailUpdatedTicks = detailUpdatedTicks;
        }

        public ushort Card { get; }
        public ushort Axis { get; }
        public uint IoStatus { get; }
        public bool IsStopped { get; }
        public bool IsHomed { get; }
        public bool ServoOn { get; }
        public double Position { get; }
        public double Speed { get; }
        public ushort AlarmCode { get; }
        internal long IoUpdatedTicks { get; }
        internal long DetailUpdatedTicks { get; }

        public bool Alarm => (IoStatus & 1u) != 0;
        public bool PositiveLimit => (IoStatus & (1u << 1)) != 0;
        public bool NegativeLimit => (IoStatus & (1u << 2)) != 0;
        public bool EmergencyStop => (IoStatus & (1u << 3)) != 0;
        public bool Origin => (IoStatus & (1u << 4)) != 0;
        public bool PositiveSoftLimit => (IoStatus & (1u << 6)) != 0;
        public bool NegativeSoftLimit => (IoStatus & (1u << 7)) != 0;

        public bool IsSignalOn(int signalNumber)
        {
            if (signalNumber <= 0 || signalNumber > 32)
            {
                throw new ArgumentOutOfRangeException(nameof(signalNumber));
            }
            return (IoStatus & (1u << (signalNumber - 1))) != 0;
        }

        public bool IsIoFresh(int maxAgeMilliseconds)
        {
            return IsFresh(IoUpdatedTicks, maxAgeMilliseconds);
        }

        public bool IsDetailFresh(int maxAgeMilliseconds)
        {
            return IsFresh(DetailUpdatedTicks, maxAgeMilliseconds);
        }

        private static bool IsFresh(long updatedTicks, int maxAgeMilliseconds)
        {
            if (updatedTicks <= 0 || maxAgeMilliseconds < 0)
            {
                return false;
            }
            long elapsed = Stopwatch.GetTimestamp() - updatedTicks;
            return elapsed >= 0 && elapsed <= maxAgeMilliseconds * (double)Stopwatch.Frequency / 1000d;
        }
    }

    public sealed class AxisStatusCache
    {
        public const int SafetyIoMaxAgeMilliseconds = 100;
        public const int UiIoMaxAgeMilliseconds = 200;
        public const int UiDetailMaxAgeMilliseconds = 300;

        private readonly ConcurrentDictionary<long, AxisStatusSnapshot> snapshots =
            new ConcurrentDictionary<long, AxisStatusSnapshot>();

        public void UpdateIo(ushort card, ushort axis, uint ioStatus)
        {
            long key = BuildKey(card, axis);
            long now = Stopwatch.GetTimestamp();
            snapshots.AddOrUpdate(
                key,
                _ => new AxisStatusSnapshot(card, axis, ioStatus, now, false, false, false, 0, 0, 0, 0),
                (_, current) => new AxisStatusSnapshot(
                    card, axis, ioStatus, now, current.IsStopped, current.IsHomed, current.ServoOn,
                    current.Position, current.Speed, current.AlarmCode, current.DetailUpdatedTicks));
        }

        public void UpdateDetails(
            ushort card,
            ushort axis,
            bool isStopped,
            bool isHomed,
            bool servoOn,
            double position,
            double speed,
            ushort alarmCode)
        {
            long key = BuildKey(card, axis);
            long now = Stopwatch.GetTimestamp();
            snapshots.AddOrUpdate(
                key,
                _ => new AxisStatusSnapshot(card, axis, 0, 0, isStopped, isHomed, servoOn,
                    position, speed, alarmCode, now),
                (_, current) => new AxisStatusSnapshot(
                    card, axis, current.IoStatus, current.IoUpdatedTicks, isStopped, isHomed, servoOn,
                    position, speed, alarmCode, now));
        }

        public bool TryGet(ushort card, ushort axis, out AxisStatusSnapshot snapshot)
        {
            return snapshots.TryGetValue(BuildKey(card, axis), out snapshot);
        }

        public AxisStatusSnapshot GetRequired(ushort card, ushort axis)
        {
            if (!TryGet(card, axis, out AxisStatusSnapshot snapshot))
            {
                throw new InvalidOperationException($"轴状态缓存不存在:{card}-{axis}");
            }
            return snapshot;
        }

        public bool GetRequiredSignal(ushort card, ushort axis, int signalNumber, int maxAgeMilliseconds)
        {
            AxisStatusSnapshot snapshot = GetRequired(card, axis);
            if (!snapshot.IsIoFresh(maxAgeMilliseconds))
            {
                throw new InvalidOperationException($"轴IO状态缓存已过期:{card}-{axis}");
            }
            return snapshot.IsSignalOn(signalNumber);
        }

        public void Clear()
        {
            snapshots.Clear();
        }

        private static long BuildKey(ushort card, ushort axis)
        {
            return ((long)card << 32) | axis;
        }
    }

    public sealed class AxisMotionParameters
    {
        public AxisMotionParameters(double speedPercent, double accelerationPercent, double decelerationPercent)
        {
            SpeedPercent = speedPercent;
            AccelerationPercent = accelerationPercent;
            DecelerationPercent = decelerationPercent;
        }

        public double SpeedPercent { get; }
        public double AccelerationPercent { get; }
        public double DecelerationPercent { get; }
    }

    public sealed class AxisMotionParameterStore
    {
        private static readonly AxisMotionParameters Default = new AxisMotionParameters(100, 100, 100);
        private readonly ConcurrentDictionary<long, AxisMotionParameters> parameters =
            new ConcurrentDictionary<long, AxisMotionParameters>();

        public AxisMotionParameters Get(ushort card, ushort axis)
        {
            return parameters.TryGetValue(BuildKey(card, axis), out AxisMotionParameters value) ? value : Default;
        }

        public void Set(ushort card, ushort axis, double speedPercent, double accelerationPercent, double decelerationPercent)
        {
            ValidatePercent(speedPercent, nameof(speedPercent));
            ValidatePercent(accelerationPercent, nameof(accelerationPercent));
            ValidatePercent(decelerationPercent, nameof(decelerationPercent));
            parameters[BuildKey(card, axis)] =
                new AxisMotionParameters(speedPercent, accelerationPercent, decelerationPercent);
        }

        public void Clear()
        {
            parameters.Clear();
        }

        private static void ValidatePercent(double value, string name)
        {
            if (value <= 0 || value > 100 || double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(name, "轴运行百分比必须大于0且不超过100。");
            }
        }

        private static long BuildKey(ushort card, ushort axis)
        {
            return ((long)card << 32) | axis;
        }
    }
}
