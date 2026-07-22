using System;
// 模块：运动控制 / 核心。
// 职责范围：定义统一运动运行契约、运动协调和轴状态缓存。
// 安全边界：命令先经过 Readiness、Safety 和 ValidateAxesForCommand；驱动事件只在 InitCardType 中绑定。

using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using csLTDMC;
using static Automation.MotionControl.MotionCtrl;

namespace Automation.MotionControl
{
    public class MotionCtrl : IMotionRuntime, IIoRuntime
    {
        private readonly ValueConfigStore valueStore;
        private readonly CardConfigStore cardStore;
        private readonly PlatformSafetyCoordinator safety;
        private readonly PlatformReadinessState readiness;
        public LS ls;

        public MotionCtrl(ValueConfigStore valueStore, CardConfigStore cardStore,
            PlatformSafetyCoordinator safety, PlatformReadinessState readiness)
        {
            this.valueStore = valueStore ?? throw new ArgumentNullException(nameof(valueStore));
            this.cardStore = cardStore ?? throw new ArgumentNullException(nameof(cardStore));
            this.safety = safety ?? throw new ArgumentNullException(nameof(safety));
            this.readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
        }

        public delegate ushort InitCardHandler();
        public delegate bool SetIOHandler(IO io, bool isOpen);
        public delegate bool SetOutputsHandler(IReadOnlyList<IoOutputCommand> commands);
        public delegate bool GetOutIOHandler(IO io, ref bool value);
        public delegate bool GetInIOHandler(IO io, ref bool value);
        public delegate void SettHomeParamHandler(ushort card,ushort axis, ushort dir, ushort speed, ushort homeMode);
        public delegate void StartHomeHandler(ushort card, ushort axis);
        public delegate void CleanPosHandler(ushort card, ushort axis);
        public delegate double GetAxisPosHandler(ushort card, ushort axis);
        public delegate void SetMovParamHandler(ushort card,ushort axis, double minVel, double dMaxVel, double acc, double dec, double dStopVel, double dS_para,int equiv);
        public delegate void MovHandler(ushort card, ushort axis, double dDist, ushort sPosi_mode, bool wait);
        public delegate void MoveCoordinatedLinearHandler(CoordinatedLinearMoveRequest request);
        public delegate bool IsCoordinatedLinearDoneHandler(ushort card, ushort coordinateSystem);
        public delegate void StopCoordinatedLinearHandler(ushort card, ushort coordinateSystem, ushort stopMode);
        public delegate void JogHandler(ushort card, ushort axis, ushort sDir);
        public delegate void StopOneAxisHandler(ushort card, ushort axis, ushort stop_mode);
        public delegate void StopConnectHandler();
        public delegate bool HomeStatusHandler(ushort card, ushort axis);
        public delegate bool GetInPosHandler(ushort card, ushort axis);
        public delegate bool GetAxisSevonHandler(ushort card, ushort axis);
        public delegate void SetAxisSevonHandler(ushort card, ushort axis, bool isSevon);
        public delegate void DownLoadConfigHandler();
        public delegate void SetAllAxisSevonOnHandler();
        public delegate void SetAllAxisEquivHandler();
        public delegate void ResetAxisAlarmHandler(ushort card, ushort axis);
        public delegate double GetAxisCurSpeedHandler(ushort card, ushort axis);
        public delegate ushort GetAxisAlarmCodeHandler(ushort card, ushort axis);
        public delegate uint GetAxisIoStatusHandler(ushort card, ushort axis);

        public event InitCardHandler initCard;
        public event SetIOHandler setIO;
        public event SetOutputsHandler setOutputs;
        public event GetOutIOHandler getOutIO;
        public event GetInIOHandler getInIO;
        public event SettHomeParamHandler settHomeParam;
        public event StartHomeHandler startHome;
        public event CleanPosHandler cleanPos;
        public event GetAxisPosHandler getAxisPos;
        public event SetMovParamHandler setMovParam;
        public event MovHandler mov;
        public event MoveCoordinatedLinearHandler moveCoordinatedLinear;
        public event IsCoordinatedLinearDoneHandler isCoordinatedLinearDone;
        public event StopCoordinatedLinearHandler stopCoordinatedLinear;
        public event JogHandler jog;
        public event StopOneAxisHandler stopOneAxis;
        public event StopConnectHandler stopConnect;
        public event HomeStatusHandler homeStatus;
        public event GetInPosHandler getInPos;
        public event GetAxisSevonHandler getAxisSevon;
        public event SetAxisSevonHandler setAxisSevon;
        public event DownLoadConfigHandler downLoadConfig;
        public event SetAllAxisSevonOnHandler setAllAxisSevonOn;
        public event SetAllAxisEquivHandler setAllAxisEquiv;
        public event ResetAxisAlarmHandler resetAxisAlarm;
        public event GetAxisCurSpeedHandler getAxisCurSpeed;
        public event GetAxisAlarmCodeHandler getAxisAlarmCode;
        public event GetAxisIoStatusHandler getAxisIoStatus;
        public bool IsCardInitialized { get; private set; }

        [ThreadStatic]
        private static HashSet<long> validatedCommands;

        private sealed class CommandValidationLease : IDisposable
        {
            private HashSet<long> commands;

            public CommandValidationLease(HashSet<long> commands)
            {
                this.commands = commands;
            }

            public void Dispose()
            {
                HashSet<long> current = commands;
                commands = null;
                if (current != null && ReferenceEquals(validatedCommands, current))
                {
                    validatedCommands = null;
                }
            }
        }

        private void EnsureCardInitialized()
        {
            if (!IsCardInitialized || ls == null)
            {
                throw new InvalidOperationException("运动控制卡未初始化");
            }
        }

        private void EnsureResetCompleted()
        {
            if (safety.IsLocked
                || !valueStore.TryGetValueByName("复位状态", out DicValue resetValue)
                || resetValue == null
                || !string.Equals(resetValue.Type, "double", StringComparison.OrdinalIgnoreCase)
                || !double.TryParse(resetValue.Value, out double resetRaw)
                || resetRaw != (double)ResetStatus.ResetCompleted)
            {
                throw new InvalidOperationException("系统尚未复位完成，禁止轴运动。");
            }
        }

        public IDisposable ValidateAxesForCommand(IReadOnlyCollection<AxisCommandRequest> requests)
        {
            if (readiness.MotionConfigRestartRequired)
            {
                throw new InvalidOperationException("运动设备配置已变更，必须重启程序后才能执行轴运动。");
            }
            EnsureCardInitialized();
            if (requests == null || requests.Count == 0)
            {
                throw new ArgumentException("轴状态校验列表为空。", nameof(requests));
            }
            HashSet<long> commands = new HashSet<long>();
            foreach (AxisCommandRequest request in requests)
            {
                if (request == null)
                {
                    throw new InvalidOperationException("轴状态校验项为空。");
                }
                long key = BuildCommandKey(request.Card, request.Axis, request.Kind);
                if (!commands.Add(key))
                {
                    continue;
                }
                uint ioStatus = GetAxisIoStatus(request.Card, request.Axis);
                if ((ioStatus & 1u) != 0)
                {
                    ushort alarmCode = GetAxisAlarmCode(request.Card, request.Axis);
                    string codeText = alarmCode == ushort.MaxValue ? "驱动器未提供详细码(ALM=ON)" : alarmCode.ToString();
                    throw new InvalidOperationException($"轴存在伺服报警:{request.Card}-{request.Axis},错误码:{codeText}");
                }
                if ((ioStatus & (1u << 3)) != 0)
                {
                    throw new InvalidOperationException($"轴急停信号有效:{request.Card}-{request.Axis}");
                }
                if (!GetInPos(request.Card, request.Axis))
                {
                    throw new InvalidOperationException($"轴正在运动:{request.Card}-{request.Axis}");
                }
                if (!GetAxisSevon(request.Card, request.Axis))
                {
                    throw new InvalidOperationException($"轴未使能:{request.Card}-{request.Axis}");
                }
                if (request.Kind == AxisCommandKind.Motion && !HomeStatus(request.Card, request.Axis))
                {
                    throw new InvalidOperationException($"轴尚未回原完成:{request.Card}-{request.Axis}");
                }
            }
            // 校验结果只在当前线程、当前 using 作用域有效，避免“先校验、状态变化后很久再执行”的窗口。
            validatedCommands = commands;
            return new CommandValidationLease(commands);
        }

        private static long BuildCommandKey(ushort card, ushort axis, AxisCommandKind kind)
        {
            return ((long)kind << 48) | ((long)card << 32) | axis;
        }

        private void EnsureCommandValidated(ushort card, ushort axis, AxisCommandKind kind, bool allowHomeJog)
        {
            long key = BuildCommandKey(card, axis, kind);
            if (validatedCommands != null && validatedCommands.Remove(key))
            {
                return;
            }
            if (allowHomeJog && validatedCommands != null
                && validatedCommands.Remove(BuildCommandKey(card, axis, AxisCommandKind.Home)))
            {
                return;
            }
            using (ValidateAxesForCommand(new[] { new AxisCommandRequest(card, axis, kind) }))
            {
            }
        }

        public bool InitCard()
        {
            initCard?.Invoke();
            IsCardInitialized = ls != null && ls.IsCardInitialized;
            return IsCardInitialized;
        }
        public bool SetIO(IO io, bool isOpen)
        {
            return (bool)setIO?.Invoke(io, isOpen);
        }
        public bool SetOutputs(IReadOnlyList<IoOutputCommand> commands)
        {
            return setOutputs?.Invoke(commands) == true;
        }
        public bool GetOutIO(IO io, ref bool value)
        {
            return (bool)getOutIO?.Invoke(io, ref value);
        }
        public bool GetInIO(IO io, ref bool value)
        {
            return (bool)getInIO?.Invoke(io, ref value);
        }
        public void SettHomeParam(ushort card,ushort axis, ushort dir, ushort speed, ushort homeMode)
        {
            EnsureCardInitialized();
            settHomeParam?.Invoke(card, axis, dir, speed, homeMode);
        }
        public void StartHome(ushort card, ushort axis)
        {
            EnsureCardInitialized();
            EnsureResetCompleted();
            EnsureCommandValidated(card, axis, AxisCommandKind.Home, false);
            startHome?.Invoke(card, axis);
        }
        public void CleanPos(ushort card, ushort axis)
        {
            EnsureCardInitialized();
            cleanPos?.Invoke(card, axis);
        }
        public double GetAxisPos(ushort card, ushort axis)
        {
            EnsureCardInitialized();
            return (double)(getAxisPos?.Invoke(card, axis));
        }
        public void SetMovParam(ushort card ,ushort axis, double minVel, double dMaxVel, double acc, double dec, double dStopVel, double dS_para,int equiv)
        {
            EnsureCardInitialized();
            setMovParam?.Invoke(card, axis, minVel, dMaxVel, acc, dec, dStopVel, dS_para, equiv);
        }
        public void Mov(ushort card, ushort axis, double dDist, ushort sPosi_mode, bool wait)
        {
            EnsureCardInitialized();
            EnsureResetCompleted();
            EnsureCommandValidated(card, axis, AxisCommandKind.Motion, false);
            mov?.Invoke(card, axis, dDist, sPosi_mode, false);
            if (wait)
            {
                while (!GetInPos(card, axis))
                {
                    Thread.Sleep(5);
                }
            }
        }

        public void MoveCoordinatedLinear(CoordinatedLinearMoveRequest request)
        {
            EnsureCardInitialized();
            EnsureResetCompleted();
            if (request?.Axes == null || request.Positions == null || request.Axes.Count == 0
                || request.Axes.Count != request.Positions.Count)
            {
                throw new ArgumentException("协调直线运动轴或位置列表无效。", nameof(request));
            }
            for (int i = 0; i < request.Axes.Count; i++)
            {
                EnsureCommandValidated(request.Card, request.Axes[i], AxisCommandKind.Motion, false);
            }
            (moveCoordinatedLinear
                ?? throw new InvalidOperationException("协调直线运动接口未初始化"))
                .Invoke(request);
        }

        public bool IsCoordinatedLinearDone(ushort card, ushort coordinateSystem)
        {
            EnsureCardInitialized();
            return isCoordinatedLinearDone?.Invoke(card, coordinateSystem)
                ?? throw new InvalidOperationException("协调直线运动状态接口未初始化");
        }

        public void StopCoordinatedLinear(ushort card, ushort coordinateSystem, ushort stopMode)
        {
            EnsureCardInitialized();
            (stopCoordinatedLinear
                ?? throw new InvalidOperationException("协调直线运动停止接口未初始化"))
                .Invoke(card, coordinateSystem, stopMode);
        }

        public void Jog(ushort card, ushort axis, ushort sDir)
        {
            EnsureCardInitialized();
            EnsureResetCompleted();
            EnsureCommandValidated(card, axis, AxisCommandKind.Motion, true);
            jog?.Invoke(card, axis, sDir);
        }
        public void StopOneAxis(ushort card, ushort axis, ushort stop_mode)
        {
            EnsureCardInitialized();
            stopOneAxis?.Invoke(card, axis, stop_mode);
        }
        public void StopConnect()
        {
            try
            {
                stopConnect?.Invoke();
            }
            finally
            {
                IsCardInitialized = false;
            }
        }
        public bool HomeStatus(ushort card, ushort axis)
        {
            EnsureCardInitialized();
            return (bool)homeStatus?.Invoke(card, axis);
        }
        public bool GetInPos(ushort card, ushort axis)
        {
            EnsureCardInitialized();
            return (bool)getInPos?.Invoke(card, axis);
        }
        public bool GetAxisSevon(ushort card, ushort axis)
        {
            return (bool)getAxisSevon?.Invoke(card, axis);
        }
        public void SetAxisSevon(ushort card, ushort axis, bool isSevon)
        {
            setAxisSevon?.Invoke(card, axis, isSevon);
        }
        public void DownLoadConfig()
        {
            downLoadConfig?.Invoke();
        }
        public void SetAllAxisSevonOn()
        {
            setAllAxisSevonOn?.Invoke();
        }
        public void SetAllAxisEquiv()
        {
            setAllAxisEquiv?.Invoke();
        }
        public void ResetAxisAlarm(ushort card, ushort axis)
        {
            EnsureCardInitialized();
            resetAxisAlarm?.Invoke(card, axis);
        }
        public double GetAxisCurSpeed(ushort card, ushort axis)
        {
            return (double)getAxisCurSpeed?.Invoke(card, axis);
        }
        public uint GetAxisIoStatus(ushort card, ushort axis)
        {
            EnsureCardInitialized();
            return getAxisIoStatus?.Invoke(card, axis)
                ?? throw new InvalidOperationException("轴IO状态读取接口未初始化");
        }
        public ushort GetAxisAlarmCode(ushort card, ushort axis)
        {
            EnsureCardInitialized();
            return getAxisAlarmCode?.Invoke(card, axis)
                ?? throw new InvalidOperationException("轴报警码读取接口未初始化");
        }
        public void InitCardType()
        {
            ls = new LS(cardStore);
            initCard = ls.InitCard;
            setIO = ls.SetIO;
            setOutputs = ls.SetOutputs;
            getInIO = ls.GetInIO;
            getOutIO = ls.GetOutIO;
            settHomeParam = ls.SettHomeParam;
            startHome = ls.StartHome;
            cleanPos += ls.CleanPos;
            cleanPos += ls.CleanPosEncoder;
            getAxisPos = ls.GetAxisPosEncoder;
            setMovParam = ls.SetMovParam;
            mov = ls.Mov;
            moveCoordinatedLinear = ls.MoveCoordinatedLinear;
            isCoordinatedLinearDone = ls.IsCoordinatedLinearDone;
            stopCoordinatedLinear = ls.StopCoordinatedLinear;
            jog = ls.Jog;
            stopOneAxis = ls.StopOneAxis;
            stopConnect = ls.StopConnect;
            homeStatus = ls.HomeStatus;
            getInPos = ls.GetInPos;
            getAxisSevon = ls.GetAxisSevon;
            setAxisSevon = ls.SetAxisSevon;
            downLoadConfig = ls.DownLoadConfig;
            setAllAxisSevonOn = ls.SetAllAxisSevonOn;
            setAllAxisEquiv = ls.SetAllAxisEquiv;
            resetAxisAlarm = ls.ResetAxisAlarm;
            getAxisCurSpeed = ls.GetAxisCurSpeed;
            getAxisAlarmCode = ls.GetAxisAlarmCode;
            getAxisIoStatus = ls.GetAxisIoStatus;
        }

    }
}
