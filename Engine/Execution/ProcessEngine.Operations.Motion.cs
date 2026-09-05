using System;
// 模块：引擎 / 执行。
// 职责范围：负责运行绑定、调度、状态管理以及各类流程指令的确定性执行。

using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Automation.MotionControl;
using System.Diagnostics;
using Newtonsoft.Json.Linq;
using System.Numerics;

namespace Automation
{
    public partial class ProcessEngine
    {
        private short GetStationRuntimeIndex(DataStation station, ProcHandle evt)
        {
            int index = Context?.Stations?.IndexOf(station) ?? -1;
            if (index < 0 || index > short.MaxValue)
            {
                throw CreateAlarmException(evt, $"工站运行索引无效:{station?.Name}");
            }
            return (short)index;
        }

        private void EnsureStationCommandSucceeded(
            ProcHandle evt,
            DataStation station,
            string operation,
            MotionStationResult result)
        {
            if (result == MotionStationResult.Success)
            {
                return;
            }
            string message = $"工站{station?.Name}{operation}失败:{result}";
            MarkAlarm(evt, message);
            throw CreateAlarmException(evt, message);
        }

        private double ResolveStationTimeout(
            ProcHandle evt,
            int configuredMilliseconds,
            string variableName,
            string operationName)
        {
            double timeout = configuredMilliseconds > 0
                ? configuredMilliseconds
                : Context.ValueStore.GetValueByNameForProcess(variableName, evt.procId).GetDValue();
            if (timeout <= 0 || double.IsNaN(timeout) || double.IsInfinity(timeout)
                || timeout > int.MaxValue)
            {
                throw CreateAlarmException(evt, $"{operationName}超时配置无效");
            }
            return timeout;
        }

        private void WaitRobotStation(
            ProcHandle evt,
            DataStation station,
            short stationIndex,
            bool waitForHome,
            int timeoutMilliseconds,
            string operation)
        {
            using (evt.CancellationToken.Register(() =>
            {
                try
                {
                    Context.Motion.StopStation(stationIndex, false);
                }
                catch (Exception ex)
                {
                    Logger?.Log($"取消{operation}时停止工站失败:{station.Name} {ex.Message}", LogLevel.Error);
                }
            }))
            {
                MotionStationResult result = Context.Motion.WaitStationMotion(
                    stationIndex, waitForHome, -1, timeoutMilliseconds);
                if (!evt.CancellationToken.IsCancellationRequested)
                {
                    EnsureStationCommandSucceeded(evt, station, operation, result);
                }
            }
        }

        private void CheckRobotStationInPoint(
            ProcHandle evt,
            DataStation station,
            short stationIndex,
            DataPos target,
            IReadOnlyList<bool> disabledAxes)
        {
            MotionStationResult result = Context.Motion.GetStationPosition(
                stationIndex, 0, out DataPos current);
            EnsureStationCommandSucceeded(evt, station, "读取当前位置", result);
            if (current == null)
            {
                throw CreateAlarmException(evt, $"工站{station.Name}未返回当前位置");
            }
            IReadOnlyList<double> currentValues = current.GetAllValues();
            IReadOnlyList<double> targetValues = target.GetAllValues();
            for (int axis = 0; axis < 6; axis++)
            {
                if (disabledAxes != null && disabledAxes.Count > axis && disabledAxes[axis])
                {
                    continue;
                }
                double tolerance = station.PositionTolerances != null
                    && station.PositionTolerances.Length > axis
                    ? station.PositionTolerances[axis]
                    : 0.01;
                if (Math.Abs(currentValues[axis] - targetValues[axis]) > tolerance)
                {
                    throw CreateAlarmException(evt,
                        $"工站{station.Name}的{axis + 1}通道未到位，当前:{currentValues[axis]}，目标:{targetValues[axis]}，精度:{tolerance}");
                }
            }
        }

        public bool RunHomeRun(ProcHandle evt, HomeRun homeRun)
        {
            DataStation station;
            if (Context.Stations == null)
            {
                MarkAlarm(evt, "工站列表为空");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (homeRun.StationIndex != -1)
            {
                if (homeRun.StationIndex < 0 || homeRun.StationIndex >= Context.Stations.Count)
                {
                    MarkAlarm(evt, $"工站索引无效:{homeRun.StationIndex}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                station = Context.Stations[homeRun.StationIndex];
            }
            else
            {
                station = Context.Stations.FirstOrDefault(sc => sc.Name == homeRun.StationName);
            }
            if (station == null)
            {
                MarkAlarm(evt, $"找不到工站:{homeRun.StationName}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (station.Type != StationType.Axis)
            {
                if (Context.Motion == null)
                {
                    throw CreateAlarmException(evt, "机器人工站运行时未初始化");
                }
                short runtimeStationIndex = GetStationRuntimeIndex(station, evt);
                if (!TryAcquireStationMotionResource(evt, runtimeStationIndex, out string stationResourceError))
                {
                    throw CreateAlarmException(evt, stationResourceError);
                }
                MotionStationResult result = Context.Motion.HomeStation(
                    runtimeStationIndex,
                    -1,
                    false,
                    string.Equals(homeRun.StationHomeType, "轴按优先顺序回", StringComparison.Ordinal));
                EnsureStationCommandSucceeded(evt, station, "回原", result);
                if (!homeRun.ContinueWithoutWaiting)
                {
                    WaitRobotStation(evt, station, runtimeStationIndex, true, 120000, "回原");
                }
                return true;
            }
            if (Context.Motion == null || Context.CardStore == null
                || station.dataAxis?.axisConfigs == null || station.dataAxis.axisConfigs.Count == 0)
            {
                throw CreateAlarmException(evt, "工站回零配置或运动控制未初始化");
            }
            List<long> homeResources = new List<long>();
            foreach (AxisConfig axisConfig in station.dataAxis.axisConfigs)
            {
                if (axisConfig?.AxisName == "-1")
                {
                    continue;
                }
                if (axisConfig?.axis == null || !ushort.TryParse(axisConfig.CardNum, out ushort cardNum)
                    || !Context.CardStore.TryGetAxis(cardNum, axisConfig.axis.AxisNum, out Axis axisInfo)
                    || axisInfo.PulseToMM <= 0
                    || axisInfo.AccMax <= 0 || axisInfo.DecMax <= 0
                    || !double.TryParse(axisInfo.HomeSpeed, out double homeSpeed) || homeSpeed <= 0)
                {
                    throw CreateAlarmException(evt, $"工站回零轴配置无效:{axisConfig?.AxisName}");
                }
                homeResources.Add(BuildMotionResourceKey(cardNum, (ushort)axisConfig.axis.AxisNum));
            }
            if (!TryAcquireMotionResources(evt, homeResources, out string homeResourceError))
            {
                throw CreateAlarmException(evt, homeResourceError);
            }
                int stationIndex = Context.Stations.IndexOf(station);
                if (stationIndex != -1)
                {
                    Task task = Task.Run(() =>
                    {
                        try
                        {
                            if (evt.CancellationToken.IsCancellationRequested)
                            {
                                return;
                            }
                            if (homeRun.StationHomeType == "轴按优先顺序回")
                                HomeStationBySeq(stationIndex, evt, homeRun.ContinueWithoutWaiting);
                            else
                                HomeStationByAll(stationIndex, evt, homeRun.ContinueWithoutWaiting);
                        }
                        catch (OperationCanceledException)
                        {
                        }
                        catch (Exception ex)
                        {
                            ReportHomeAlarm(evt, ex.Message, homeRun.ContinueWithoutWaiting);
                        }
                    }, evt.CancellationToken);
                    evt.RunningTasks.Add(task);
                    Delay(500, evt);
                    if (!homeRun.ContinueWithoutWaiting)
                    {
                        Stopwatch stopwatch = Stopwatch.StartNew();
                        bool isInPos = false;
                        while (!evt.CancellationToken.IsCancellationRequested
                            )
                        {
                            if (stopwatch.ElapsedMilliseconds > 120000)
                            {    
                                MarkAlarm(evt, homeRun.Name + "运动超时");
                                throw CreateAlarmException(evt, evt?.alarmMsg);
                            }
                            for (int i = 0; i < 6; i++)
                            {
                                if (station.dataAxis.axisConfigs[i].AxisName == "-1")
                                    continue;
                                if (!int.TryParse(station.dataAxis.axisConfigs[i].CardNum, out int cardNum))
                                {
                                    MarkAlarm(evt, $"卡号无效:{station.dataAxis.axisConfigs[i].CardNum}");
                                    throw CreateAlarmException(evt, evt?.alarmMsg);
                                }
                                AxisConfig axisConfig = station.dataAxis.axisConfigs[i];
                                ushort axisNum = (ushort)axisConfig.axis.AxisNum;
                                if (Context.Motion.GetInPos((ushort)cardNum, axisNum)
                                    && Context.Motion.HomeStatus((ushort)cardNum, axisNum))
                                {
                                    isInPos = true;
                                }
                                else
                                {
                                    isInPos = false;
                                    break;
                                }
                            }
                            if (isInPos)
                            {
                                break;
                            }
                            Delay(1, evt);
                        }
                    }
                }
            return true;
        }

        private sealed class AxisStationMoveBinding
        {
            public int Channel { get; set; }

            public ushort Card { get; set; }

            public ushort Axis { get; set; }
        }

        private sealed class AxisStationMovePlan
        {
            public short StationIndex { get; set; }

            public List<AxisStationMoveBinding> Bindings { get; set; }

            public bool[] InactiveChannels { get; set; }
        }

        private AxisStationMovePlan ReserveAxisStationMove(
            ProcHandle evt,
            DataStation station,
            IReadOnlyList<bool> disabledChannels,
            string operationName)
        {
            if (Context.Motion == null || Context.CardStore == null)
            {
                throw CreateAlarmException(evt, "运动控制或控制卡配置未初始化");
            }
            if (station?.dataAxis?.axisConfigs == null
                || station.dataAxis.axisConfigs.Count < 6
                || (disabledChannels != null && disabledChannels.Count < 6))
            {
                throw CreateAlarmException(evt, $"{operationName}工站轴配置不完整");
            }
            if (station.CoordinateSystem > CoordinatedLinearMoveRequest.MaximumCoordinateSystem)
            {
                throw CreateAlarmException(evt,
                    $"{operationName}坐标系无效:{station.CoordinateSystem}");
            }

            var bindings = new List<AxisStationMoveBinding>();
            var inactiveChannels = Enumerable.Repeat(true, 6).ToArray();
            for (int channel = 0; channel < 6; channel++)
            {
                if (disabledChannels != null && disabledChannels[channel])
                {
                    continue;
                }
                AxisConfig axisConfig = station.dataAxis.axisConfigs[channel];
                if (axisConfig?.AxisName == "-1")
                {
                    continue;
                }
                if (axisConfig?.axis == null
                    || !ushort.TryParse(axisConfig.CardNum, out ushort card)
                    || axisConfig.axis.AxisNum < 0
                    || axisConfig.axis.AxisNum > ushort.MaxValue)
                {
                    throw CreateAlarmException(evt,
                        $"{operationName}工站通道{channel + 1}卡号或轴配置无效");
                }
                ushort axis = (ushort)axisConfig.axis.AxisNum;
                if (!Context.CardStore.TryGetAxis(card, axis, out Axis configuredAxis)
                    || configuredAxis.PulseToMM <= 0
                    || configuredAxis.SpeedMax <= 0
                    || configuredAxis.AccMax <= 0
                    || configuredAxis.DecMax <= 0)
                {
                    throw CreateAlarmException(evt,
                        $"{operationName}工站通道{channel + 1}物理轴配置无效:{card}-{axis}");
                }
                bindings.Add(new AxisStationMoveBinding
                {
                    Channel = channel,
                    Card = card,
                    Axis = axis
                });
                inactiveChannels[channel] = false;
            }
            if (bindings.GroupBy(item => BuildMotionResourceKey(item.Card, item.Axis))
                .Any(group => group.Count() > 1))
            {
                throw CreateAlarmException(evt, $"{operationName}的参与轴配置重复");
            }
            if (bindings.Select(item => item.Card).Distinct().Count() > 1)
            {
                throw CreateAlarmException(evt, $"{operationName}的参与轴必须位于同一张控制卡");
            }

            short stationIndex = GetStationRuntimeIndex(station, evt);
            if (!TryAcquireStationMotionResource(evt, stationIndex, out string stationError))
            {
                throw CreateAlarmException(evt, stationError);
            }
            if (!TryAcquireMotionResources(
                    evt,
                    bindings.Select(item => BuildMotionResourceKey(item.Card, item.Axis)),
                    out string axisError))
            {
                throw CreateAlarmException(evt, axisError);
            }
            if (bindings.Count > 0
                && !TryAcquireCoordinateSystem(
                    evt,
                    bindings[0].Card,
                    station.CoordinateSystem,
                    out string coordinateError))
            {
                throw CreateAlarmException(evt, coordinateError);
            }
            return new AxisStationMovePlan
            {
                StationIndex = stationIndex,
                Bindings = bindings,
                InactiveChannels = inactiveChannels
            };
        }

        private void ApplyAxisStationMoveSpeed(
            ProcHandle evt,
            DataStation station,
            AxisStationMovePlan plan,
            string operationName,
            bool useOperationSpeed,
            double configuredVelocity,
            string velocityVariable,
            double configuredAcceleration,
            string accelerationVariable,
            double configuredDeceleration,
            string decelerationVariable)
        {
            if (plan.Bindings.Count == 0)
            {
                return;
            }

            double velocity;
            double acceleration;
            double deceleration;
            if (useOperationSpeed)
            {
                if (Context.ValueStore == null
                    && (configuredVelocity == 0
                        || configuredAcceleration == 0
                        || configuredDeceleration == 0))
                {
                    throw CreateAlarmException(evt, $"{operationName}速度变量库未初始化");
                }
                velocity = configuredVelocity == 0
                    ? Context.ValueStore.GetValueByNameForProcess(velocityVariable, evt.procId).GetDValue()
                    : configuredVelocity;
                acceleration = configuredAcceleration == 0
                    ? Context.ValueStore.GetValueByNameForProcess(accelerationVariable, evt.procId).GetDValue()
                    : configuredAcceleration;
                deceleration = configuredDeceleration == 0
                    ? Context.ValueStore.GetValueByNameForProcess(decelerationVariable, evt.procId).GetDValue()
                    : configuredDeceleration;
            }
            else
            {
                AxisMotionParameters[] parameters = plan.Bindings
                    .Select(item => Context.AxisMotionParameters.Get(item.Card, item.Axis))
                    .ToArray();
                // 3.0 的协调 Move 只有一档速度；多轴百分比按最小值合并，确保不突破任一轴上限。
                velocity = parameters.Min(item => item.SpeedPercent);
                acceleration = parameters.Min(item => item.AccelerationPercent);
                deceleration = parameters.Min(item => item.DecelerationPercent);
            }
            if (velocity <= 0 || velocity > 100
                || acceleration <= 0 || acceleration > 100
                || deceleration <= 0 || deceleration > 100
                || double.IsNaN(velocity) || double.IsInfinity(velocity)
                || double.IsNaN(acceleration) || double.IsInfinity(acceleration)
                || double.IsNaN(deceleration) || double.IsInfinity(deceleration))
            {
                throw CreateAlarmException(evt,
                    $"{operationName}速度、加速能力和减速能力必须在1%到100%之间");
            }
            EnsureStationCommandSucceeded(
                evt,
                station,
                "设置协调运动速度",
                Context.Motion.SetStationSpeed(
                    plan.StationIndex,
                    velocity,
                    acceleration,
                    deceleration,
                    -1,
                    StationSpeedType.Move));
        }

        private DataStation ResolveContinuousPathStation(
            ProcHandle evt,
            string stationName,
            string operationName)
        {
            if (Context.Motion == null || Context.Stations == null)
            {
                throw CreateAlarmException(evt, "连续轨迹运动运行时或工站列表未初始化");
            }
            DataStation station = Context.Stations.FirstOrDefault(item => item != null
                && string.Equals(item.Name, stationName, StringComparison.Ordinal));
            if (station == null)
            {
                throw CreateAlarmException(evt, $"{operationName}找不到工站:{stationName}");
            }
            return station;
        }

        private DataPos ResolveContinuousPathPoint(
            ProcHandle evt,
            DataStation station,
            string pointName,
            int pointIndex,
            string role)
        {
            DataPos point = pointIndex >= 0
                ? station.ListDataPos?.FirstOrDefault(item => item != null && item.Index == pointIndex)
                : station.ListDataPos?.FirstOrDefault(item => item != null
                    && string.Equals(item.Name, pointName, StringComparison.Ordinal));
            if (point == null)
            {
                throw CreateAlarmException(evt,
                    $"工站{station.Name}的{role}不存在:{(pointIndex >= 0 ? pointIndex.ToString() : pointName)}");
            }
            if (!point.IsMotionReady)
            {
                throw CreateAlarmException(evt,
                    $"工站{station.Name}的{role}[{point.Name}]名称为空或尚未人工示教坐标");
            }
            return point;
        }

        private short ReserveContinuousPathResources(
            ProcHandle evt,
            DataStation station,
            string operationName)
        {
            if (station.Type == StationType.Axis)
            {
                return ReserveAxisStationMove(evt, station, null, operationName).StationIndex;
            }
            short stationIndex = GetStationRuntimeIndex(station, evt);
            if (!TryAcquireStationMotionResource(evt, stationIndex, out string resourceError))
            {
                throw CreateAlarmException(evt, resourceError);
            }
            return stationIndex;
        }

        private void ClearContinuousPathWhenRequested(
            ProcHandle evt,
            DataStation station,
            short stationIndex,
            bool clearPreviousPath)
        {
            if (!clearPreviousPath)
            {
                return;
            }
            EnsureStationCommandSucceeded(
                evt,
                station,
                "清除未启动连续轨迹",
                Context.Motion.ClearStationContinuousPath(stationIndex));
        }

        private void StartContinuousPathAndWait(
            ProcHandle evt,
            DataStation station,
            short stationIndex,
            bool continueWithoutWaiting,
            int timeoutMs,
            string timeoutVariableName,
            string operationName)
        {
            EnsureStationCommandSucceeded(
                evt,
                station,
                "启动连续轨迹",
                Context.Motion.StartStationContinuousMove(stationIndex));
            if (continueWithoutWaiting)
            {
                return;
            }
            int timeout = checked((int)ResolveStationTimeout(
                evt, timeoutMs, timeoutVariableName, operationName));
            WaitRobotStation(
                evt, station, stationIndex, false, timeout, "连续轨迹运动");
        }

        public bool RunAddContinuousLine(
            ProcHandle evt,
            AddContinuousLineOperation operation)
        {
            DataStation station = ResolveContinuousPathStation(
                evt, operation.StationName, operation.Name);
            DataPos target = ResolveContinuousPathPoint(
                evt,
                station,
                operation.TargetPointName,
                operation.TargetPointIndex,
                "连续直线目标点");
            short stationIndex = ReserveContinuousPathResources(evt, station, operation.Name);
            ClearContinuousPathWhenRequested(
                evt, station, stationIndex, operation.ClearPreviousPath);
            EnsureStationCommandSucceeded(
                evt,
                station,
                "添加连续直线",
                Context.Motion.AddStationContinuousLine(stationIndex, target));
            if (operation.StartAfterAdding)
            {
                StartContinuousPathAndWait(
                    evt,
                    station,
                    stationIndex,
                    operation.ContinueWithoutWaiting,
                    operation.TimeoutMs,
                    operation.TimeoutVariableName,
                    operation.Name);
            }
            return true;
        }

        public bool RunAddContinuousThreePointArc(
            ProcHandle evt,
            AddContinuousThreePointArcOperation operation)
        {
            DataStation station = ResolveContinuousPathStation(
                evt, operation.StationName, operation.Name);
            DataPos start = ResolveContinuousPathPoint(
                evt, station, operation.StartPointName, operation.StartPointIndex, "圆弧起点A");
            DataPos middle = ResolveContinuousPathPoint(
                evt, station, operation.MiddlePointName, operation.MiddlePointIndex, "圆弧中间点B");
            DataPos target = ResolveContinuousPathPoint(
                evt, station, operation.TargetPointName, operation.TargetPointIndex, "圆弧目标点C");
            short stationIndex = ReserveContinuousPathResources(evt, station, operation.Name);
            ClearContinuousPathWhenRequested(
                evt, station, stationIndex, operation.ClearPreviousPath);
            EnsureStationCommandSucceeded(
                evt,
                station,
                "添加三点圆弧",
                Context.Motion.AddStationContinuousArc(stationIndex, start, middle, target));
            if (operation.StartAfterAdding)
            {
                StartContinuousPathAndWait(
                    evt,
                    station,
                    stationIndex,
                    operation.ContinueWithoutWaiting,
                    operation.TimeoutMs,
                    operation.TimeoutVariableName,
                    operation.Name);
            }
            return true;
        }

        public bool RunAddContinuousCenterArc(
            ProcHandle evt,
            AddContinuousCenterArcOperation operation)
        {
            DataStation station = ResolveContinuousPathStation(
                evt, operation.StationName, operation.Name);
            if (station.Type != StationType.Axis)
            {
                throw CreateAlarmException(evt, "圆心式连续圆弧只适用于雷赛轴工站");
            }
            DataPos center = ResolveContinuousPathPoint(
                evt, station, operation.CenterPointName, operation.CenterPointIndex, "圆心点");
            DataPos target = ResolveContinuousPathPoint(
                evt, station, operation.TargetPointName, operation.TargetPointIndex, "圆弧目标点");
            short stationIndex = ReserveContinuousPathResources(evt, station, operation.Name);
            ClearContinuousPathWhenRequested(
                evt, station, stationIndex, operation.ClearPreviousPath);
            EnsureStationCommandSucceeded(
                evt,
                station,
                "添加圆心圆弧",
                Context.Motion.AddStationContinuousArcCenterRadius(
                    stationIndex,
                    target,
                    center,
                    0,
                    operation.Circle,
                    operation.CounterClockwise));
            if (operation.StartAfterAdding)
            {
                StartContinuousPathAndWait(
                    evt,
                    station,
                    stationIndex,
                    operation.ContinueWithoutWaiting,
                    operation.TimeoutMs,
                    operation.TimeoutVariableName,
                    operation.Name);
            }
            return true;
        }

        public bool RunAddContinuousRadiusArc(
            ProcHandle evt,
            AddContinuousRadiusArcOperation operation)
        {
            DataStation station = ResolveContinuousPathStation(
                evt, operation.StationName, operation.Name);
            if (station.Type != StationType.Axis)
            {
                throw CreateAlarmException(evt, "半径式连续圆弧只适用于雷赛轴工站");
            }
            if (operation.Radius <= 0 || double.IsNaN(operation.Radius)
                || double.IsInfinity(operation.Radius))
            {
                throw CreateAlarmException(evt, $"连续圆弧半径无效:{operation.Radius}");
            }
            DataPos target = ResolveContinuousPathPoint(
                evt, station, operation.TargetPointName, operation.TargetPointIndex, "圆弧目标点");
            short stationIndex = ReserveContinuousPathResources(evt, station, operation.Name);
            ClearContinuousPathWhenRequested(
                evt, station, stationIndex, operation.ClearPreviousPath);
            EnsureStationCommandSucceeded(
                evt,
                station,
                "添加半径圆弧",
                Context.Motion.AddStationContinuousArcCenterRadius(
                    stationIndex,
                    target,
                    null,
                    operation.Radius,
                    operation.Circle,
                    operation.CounterClockwise));
            if (operation.StartAfterAdding)
            {
                StartContinuousPathAndWait(
                    evt,
                    station,
                    stationIndex,
                    operation.ContinueWithoutWaiting,
                    operation.TimeoutMs,
                    operation.TimeoutVariableName,
                    operation.Name);
            }
            return true;
        }

        public bool RunStartContinuousMove(
            ProcHandle evt,
            StartContinuousMoveOperation operation)
        {
            DataStation station = ResolveContinuousPathStation(
                evt, operation.StationName, operation.Name);
            short stationIndex = ReserveContinuousPathResources(evt, station, operation.Name);
            StartContinuousPathAndWait(
                evt,
                station,
                stationIndex,
                operation.ContinueWithoutWaiting,
                operation.TimeoutMs,
                operation.TimeoutVariableName,
                operation.Name);
            return true;
        }

        public bool RunStationRunPos(ProcHandle evt, StationRunPos stationRunPos)
        {
            DataStation station;
            //if (stationRunPos.StationIndex != -1)
            //{
            //    station = Context.Stations[stationRunPos.StationIndex];
            //}
            //else
            //{
            station = Context.Stations.FirstOrDefault(sc => sc.Name == stationRunPos.StationName);
            //}

            if (station == null)
            {
                MarkAlarm(evt, $"找不到工站:{stationRunPos.StationName}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            DataPos posItems;
            if (stationRunPos.PosIndex != -1)
            {
                if (station.ListDataPos == null || stationRunPos.PosIndex < 0 || stationRunPos.PosIndex >= station.ListDataPos.Count)
                {
                    MarkAlarm(evt, $"工站点位索引无效:{stationRunPos.PosIndex}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                posItems = station.ListDataPos[stationRunPos.PosIndex];
            }
            else
            {
                posItems = station.ListDataPos.FirstOrDefault(sc => sc.Name == stationRunPos.PosName);
            }
            if (posItems == null)
            {
                MarkAlarm(evt, $"工站点位不存在:{stationRunPos.PosName}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (posItems.IsTaught == false)
            {
                MarkAlarm(evt, $"工站点位尚未人工示教坐标:{posItems.Name}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }

            if (station.Type != StationType.Axis)
            {
                if (Context.Motion == null || Context.ValueStore == null)
                {
                    throw CreateAlarmException(evt, "机器人工站运行时或变量库未初始化");
                }
                short stationIndex = GetStationRuntimeIndex(station, evt);
                if (!TryAcquireStationMotionResource(evt, stationIndex, out string stationResourceError))
                {
                    throw CreateAlarmException(evt, stationResourceError);
                }
                if (stationRunPos.ChangeVel == "改变速度")
                {
                    double velocity = stationRunPos.Vel == 0
                        ? Context.ValueStore.GetValueByNameForProcess(stationRunPos.VelV, evt.procId).GetDValue()
                        : stationRunPos.Vel;
                    double acceleration = stationRunPos.Acc == 0
                        ? Context.ValueStore.GetValueByNameForProcess(stationRunPos.AccV, evt.procId).GetDValue()
                        : stationRunPos.Acc;
                    double deceleration = stationRunPos.Dec == 0
                        ? Context.ValueStore.GetValueByNameForProcess(stationRunPos.DecV, evt.procId).GetDValue()
                        : stationRunPos.Dec;
                    EnsureStationCommandSucceeded(evt, station, "设置速度",
                        Context.Motion.SetStationSpeed(stationIndex, velocity, acceleration,
                            deceleration, -1, StationSpeedType.Joint));
                }
                bool[] disabledAxes = stationRunPos.IsDisableAxis == "有禁用"
                    ? stationRunPos.GetAllValues().ToArray()
                    : null;
                EnsureStationCommandSucceeded(evt, station, "走点",
                    Context.Motion.MoveStationToPoint(
                        stationIndex, posItems, StationMoveMode.Go, disabledAxes, 0));
                if (!stationRunPos.ContinueWithoutWaiting)
                {
                    int timeout = checked((int)ResolveStationTimeout(
                        evt, stationRunPos.TimeoutMs, stationRunPos.TimeoutVariableName, stationRunPos.Name));
                    WaitRobotStation(evt, station, stationIndex, false, timeout, "走点");
                    if (stationRunPos.CheckInPosition && !evt.CancellationToken.IsCancellationRequested)
                    {
                        CheckRobotStationInPoint(evt, station, stationIndex, posItems, disabledAxes);
                    }
                }
                return true;
            }

            bool[] axisDisabled = stationRunPos.IsDisableAxis == "有禁用"
                ? stationRunPos.GetAllValues().ToArray()
                : null;
            AxisStationMovePlan plan = ReserveAxisStationMove(
                evt,
                station,
                axisDisabled,
                stationRunPos.Name);
            ApplyAxisStationMoveSpeed(
                evt,
                station,
                plan,
                stationRunPos.Name,
                stationRunPos.ChangeVel == "改变速度",
                stationRunPos.Vel,
                stationRunPos.VelV,
                stationRunPos.Acc,
                stationRunPos.AccV,
                stationRunPos.Dec,
                stationRunPos.DecV);
            EnsureStationCommandSucceeded(
                evt,
                station,
                "协调走点",
                Context.Motion.MoveStationToPoint(
                    plan.StationIndex,
                    posItems,
                    StationMoveMode.Move,
                    plan.InactiveChannels,
                    0));
            if (!stationRunPos.ContinueWithoutWaiting)
            {
                int timeout = checked((int)ResolveStationTimeout(
                    evt,
                    stationRunPos.TimeoutMs,
                    stationRunPos.TimeoutVariableName,
                    stationRunPos.Name));
                WaitRobotStation(
                    evt,
                    station,
                    plan.StationIndex,
                    false,
                    timeout,
                    "协调走点");
                if (stationRunPos.CheckInPosition
                    && !evt.CancellationToken.IsCancellationRequested)
                {
                    CheckRobotStationInPoint(
                        evt,
                        station,
                        plan.StationIndex,
                        posItems,
                        plan.InactiveChannels);
                }
            }
            return true;
        }

        public bool RunCreateTray(ProcHandle evt, CreateTray createTray)
        {
            if (createTray == null)
            {
                MarkAlarm(evt, "创建料盘参数为空");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (Context.Stations == null)
            {
                MarkAlarm(evt, "工站列表为空");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (string.IsNullOrWhiteSpace(createTray.StationName))
            {
                MarkAlarm(evt, "工站名称为空");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }

            DataStation station = Context.Stations.FirstOrDefault(sc => sc.Name == createTray.StationName);
            if (station == null)
            {
                MarkAlarm(evt, $"找不到工站:{createTray.StationName}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (createTray.RowCount <= 0 || createTray.ColCount <= 0)
            {
                MarkAlarm(evt, $"料盘行列数无效:行{createTray.RowCount},列{createTray.ColCount}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (createTray.TrayId < 0)
            {
                MarkAlarm(evt, $"料盘ID无效:{createTray.TrayId}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }

            if (string.IsNullOrWhiteSpace(createTray.PX1)
                || string.IsNullOrWhiteSpace(createTray.PX2)
                || string.IsNullOrWhiteSpace(createTray.PY1)
                || string.IsNullOrWhiteSpace(createTray.PY2))
            {
                MarkAlarm(evt, "料盘格点名称未完整设置");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }

            if (station.ListDataPos == null || station.ListDataPos.Count == 0)
            {
                MarkAlarm(evt, $"工站点位列表为空:{createTray.StationName}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }

            DataPos px1 = station.ListDataPos.FirstOrDefault(pos => pos != null && pos.Name == createTray.PX1);
            DataPos px2 = station.ListDataPos.FirstOrDefault(pos => pos != null && pos.Name == createTray.PX2);
            DataPos py1 = station.ListDataPos.FirstOrDefault(pos => pos != null && pos.Name == createTray.PY1);
            DataPos py2 = station.ListDataPos.FirstOrDefault(pos => pos != null && pos.Name == createTray.PY2);

            if (px1 == null || px2 == null || py1 == null || py2 == null)
            {
                MarkAlarm(evt, $"料盘参考点不存在:左上={createTray.PX1},右上={createTray.PX2},左下={createTray.PY1},右下={createTray.PY2}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            DataPos untaughtTrayPoint = new[] { px1, px2, py1, py2 }
                .FirstOrDefault(point => point.IsTaught == false);
            if (untaughtTrayPoint != null)
            {
                MarkAlarm(evt, $"料盘参考点尚未人工示教坐标:{untaughtTrayPoint.Name}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }

            List<double> px1Values = px1.GetAllValues();
            List<double> px2Values = px2.GetAllValues();
            List<double> py1Values = py1.GetAllValues();
            List<double> py2Values = py2.GetAllValues();
            if (px1Values.Count != 6 || px2Values.Count != 6 || py1Values.Count != 6 || py2Values.Count != 6)
            {
                MarkAlarm(evt, "料盘参考点轴数量异常");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }

            double posTolerance = 1e-6;
            bool sameOrigin = true;
            for (int i = 0; i < 6; i++)
            {
                if (Math.Abs(px1Values[i] - py1Values[i]) > posTolerance)
                {
                    sameOrigin = false;
                    break;
                }
            }

            int totalCount;
            try
            {
                totalCount = checked(createTray.RowCount * createTray.ColCount);
            }
            catch (OverflowException)
            {
                MarkAlarm(evt, "料盘点位数量溢出");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (totalCount <= 0)
            {
                MarkAlarm(evt, $"料盘点位数量无效:{totalCount}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (Context.TrayPointStore == null)
            {
                MarkAlarm(evt, "料盘缓存未初始化");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }

            List<TrayPoint> points = new List<TrayPoint>(totalCount);
            double colDen = Math.Max(1, createTray.ColCount - 1);
            double rowDen = Math.Max(1, createTray.RowCount - 1);

            for (int row = 0; row < createTray.RowCount; row++)
            {
                double v = createTray.RowCount == 1 ? 0 : row / (double)(createTray.RowCount - 1);
                for (int col = 0; col < createTray.ColCount; col++)
                {
                    double u = createTray.ColCount == 1 ? 0 : col / (double)(createTray.ColCount - 1);
                    int order = row * createTray.ColCount + col + 1;
                    TrayPoint point;
                    if (sameOrigin)
                    {
                        point = new TrayPoint(
                            order,
                            row + 1,
                            col + 1,
                            px1Values[0] + (px2Values[0] - px1Values[0]) / colDen * col + (py2Values[0] - py1Values[0]) / rowDen * row,
                            px1Values[1] + (px2Values[1] - px1Values[1]) / colDen * col + (py2Values[1] - py1Values[1]) / rowDen * row,
                            px1Values[2] + (px2Values[2] - px1Values[2]) / colDen * col + (py2Values[2] - py1Values[2]) / rowDen * row,
                            px1Values[3] + (px2Values[3] - px1Values[3]) / colDen * col + (py2Values[3] - py1Values[3]) / rowDen * row,
                            px1Values[4] + (px2Values[4] - px1Values[4]) / colDen * col + (py2Values[4] - py1Values[4]) / rowDen * row,
                            px1Values[5] + (px2Values[5] - px1Values[5]) / colDen * col + (py2Values[5] - py1Values[5]) / rowDen * row);
                    }
                    else
                    {
                        double u1 = 1 - u;
                        double v1 = 1 - v;
                        double uv00 = u1 * v1;
                        double uv10 = u * v1;
                        double uv01 = u1 * v;
                        double uv11 = u * v;
                        point = new TrayPoint(
                            order,
                            row + 1,
                            col + 1,
                            px1Values[0] * uv00 + px2Values[0] * uv10 + py1Values[0] * uv01 + py2Values[0] * uv11,
                            px1Values[1] * uv00 + px2Values[1] * uv10 + py1Values[1] * uv01 + py2Values[1] * uv11,
                            px1Values[2] * uv00 + px2Values[2] * uv10 + py1Values[2] * uv01 + py2Values[2] * uv11,
                            px1Values[3] * uv00 + px2Values[3] * uv10 + py1Values[3] * uv01 + py2Values[3] * uv11,
                            px1Values[4] * uv00 + px2Values[4] * uv10 + py1Values[4] * uv01 + py2Values[4] * uv11,
                            px1Values[5] * uv00 + px2Values[5] * uv10 + py1Values[5] * uv01 + py2Values[5] * uv11);
                    }
                    points.Add(point);
                }
            }

            TrayPointGrid grid = new TrayPointGrid(createTray.StationName, createTray.TrayId, createTray.RowCount, createTray.ColCount, points);
            if (station.Type != StationType.Axis)
            {
                if (Context.Motion == null)
                {
                    throw CreateAlarmException(evt, "机器人工站运行时未初始化");
                }
                short stationIndex = GetStationRuntimeIndex(station, evt);
                if (!TryAcquireStationMotionResource(evt, stationIndex, out string stationResourceError))
                {
                    throw CreateAlarmException(evt, stationResourceError);
                }
                EnsureStationCommandSucceeded(evt, station, "创建料盘",
                    Context.Motion.CreateStationTray(
                        stationIndex,
                        createTray.TrayId,
                        createTray.RowCount,
                        createTray.ColCount,
                        new[] { px1, px2, py1, py2 }));
            }
            if (!Context.TrayPointStore.TrySave(grid, out string cacheError))
            {
                MarkAlarm(evt, $"料盘缓存失败:{cacheError}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }

            return true;
        }

        public bool RunTrayRunPos(ProcHandle evt, TrayRunPos trayRunPos)
        {
            if (trayRunPos == null)
            {
                MarkAlarm(evt, "走料盘点参数为空");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (Context.Stations == null)
            {
                MarkAlarm(evt, "工站列表为空");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (string.IsNullOrWhiteSpace(trayRunPos.StationName))
            {
                MarkAlarm(evt, "工站名称为空");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (Context.TrayPointStore == null)
            {
                MarkAlarm(evt, "料盘缓存未初始化");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }

            int trayId = trayRunPos.TrayId;
            int trayPos = trayRunPos.TrayPos;
            ValueConfigStore valueStore = Context.ValueStore;
            bool hasTrayIdRef = !string.IsNullOrWhiteSpace(trayRunPos.TrayIdValueIndex)
                || !string.IsNullOrWhiteSpace(trayRunPos.TrayIdValueIndex2Index)
                || !string.IsNullOrWhiteSpace(trayRunPos.TrayIdValueName)
                || !string.IsNullOrWhiteSpace(trayRunPos.TrayIdValueName2Index);
            if (hasTrayIdRef)
            {
                if (trayRunPos.TrayId != 0)
                {
                    throw CreateAlarmException(evt, "料盘号配置冲突");
                }
                if (!ValueRef.TryCreate(trayRunPos.TrayIdValueIndex, trayRunPos.TrayIdValueIndex2Index, trayRunPos.TrayIdValueName, trayRunPos.TrayIdValueName2Index, false, "料盘号", out ValueRef trayIdRef, out string trayIdRefError))
                {
                    throw CreateAlarmException(evt, trayIdRefError);
                }
                if (!trayIdRef.TryResolveValue(valueStore, "料盘号", evt.procId, out DicValue trayIdValue, out string trayIdResolveError))
                {
                    throw CreateAlarmException(evt, trayIdResolveError);
                }
                string trayIdText = trayIdValue?.Value;
                if (string.IsNullOrWhiteSpace(trayIdText))
                {
                    throw CreateAlarmException(evt, "料盘号变量值为空");
                }
                if (!int.TryParse(trayIdText, out trayId))
                {
                    throw CreateAlarmException(evt, $"料盘号变量值不是有效整数:{trayIdText}");
                }
            }
            bool hasTrayPosRef = !string.IsNullOrWhiteSpace(trayRunPos.TrayPosValueIndex)
                || !string.IsNullOrWhiteSpace(trayRunPos.TrayPosValueIndex2Index)
                || !string.IsNullOrWhiteSpace(trayRunPos.TrayPosValueName)
                || !string.IsNullOrWhiteSpace(trayRunPos.TrayPosValueName2Index);
            if (hasTrayPosRef)
            {
                if (trayRunPos.TrayPos != 0)
                {
                    throw CreateAlarmException(evt, "料盘位置配置冲突");
                }
                if (!ValueRef.TryCreate(trayRunPos.TrayPosValueIndex, trayRunPos.TrayPosValueIndex2Index, trayRunPos.TrayPosValueName, trayRunPos.TrayPosValueName2Index, false, "料盘位置", out ValueRef trayPosRef, out string trayPosRefError))
                {
                    throw CreateAlarmException(evt, trayPosRefError);
                }
                if (!trayPosRef.TryResolveValue(valueStore, "料盘位置", evt.procId, out DicValue trayPosValue, out string trayPosResolveError))
                {
                    throw CreateAlarmException(evt, trayPosResolveError);
                }
                string trayPosText = trayPosValue?.Value;
                if (string.IsNullOrWhiteSpace(trayPosText))
                {
                    throw CreateAlarmException(evt, "料盘位置变量值为空");
                }
                if (!int.TryParse(trayPosText, out trayPos))
                {
                    throw CreateAlarmException(evt, $"料盘位置变量值不是有效整数:{trayPosText}");
                }
            }

            if (trayId < 0)
            {
                MarkAlarm(evt, $"料盘号无效:{trayId}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (trayPos <= 0)
            {
                MarkAlarm(evt, $"料盘位置无效:{trayPos}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }

            DataStation station = Context.Stations.FirstOrDefault(sc => sc.Name == trayRunPos.StationName);
            if (station == null)
            {
                MarkAlarm(evt, $"找不到工站:{trayRunPos.StationName}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (!Context.TrayPointStore.TryGet(trayRunPos.StationName, trayId, out TrayPointGrid grid) || grid == null)
            {
                MarkAlarm(evt, $"料盘缓存不存在:工站{trayRunPos.StationName},料盘号{trayId}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (grid.Points == null || grid.Points.Count == 0)
            {
                MarkAlarm(evt, $"料盘点位为空:工站{trayRunPos.StationName},料盘号{trayId}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            bool hasTarget = false;
            TrayPoint target = default;
            foreach (TrayPoint point in grid.Points)
            {
                if (point.Order == trayPos)
                {
                    target = point;
                    hasTarget = true;
                    break;
                }
            }
            if (!hasTarget)
            {
                MarkAlarm(evt, $"料盘位置超出范围:{trayPos}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }

            var calculatedPoint = new DataPos(-1)
            {
                Name = "料盘点",
                X = target.X,
                Y = target.Y,
                Z = target.Z,
                U = target.U,
                V = target.V,
                W = target.W,
                IsTaught = true
            };

            if (station.Type != StationType.Axis)
            {
                if (Context.Motion == null)
                {
                    throw CreateAlarmException(evt, "机器人工站运行时未初始化");
                }
                short stationIndex = GetStationRuntimeIndex(station, evt);
                if (!TryAcquireStationMotionResource(evt, stationIndex, out string stationResourceError))
                {
                    throw CreateAlarmException(evt, stationResourceError);
                }
                EnsureStationCommandSucceeded(evt, station, "料盘点运动",
                    Context.Motion.MoveStationTrayPoint(
                        stationIndex,
                        trayId,
                        trayPos - 1,
                        calculatedPoint));
                if (!trayRunPos.ContinueWithoutWaiting)
                {
                    WaitRobotStation(evt, station, stationIndex, false, 120000, "料盘点运动");
                }
                return true;
            }

            AxisStationMovePlan plan = ReserveAxisStationMove(
                evt,
                station,
                null,
                trayRunPos.Name);
            ApplyAxisStationMoveSpeed(
                evt,
                station,
                plan,
                trayRunPos.Name,
                false,
                0,
                null,
                0,
                null,
                0,
                null);
            EnsureStationCommandSucceeded(
                evt,
                station,
                "协调料盘点运动",
                Context.Motion.MoveStationTrayPoint(
                    plan.StationIndex,
                    trayId,
                    trayPos - 1,
                    calculatedPoint));
            if (!trayRunPos.ContinueWithoutWaiting)
            {
                WaitRobotStation(
                    evt,
                    station,
                    plan.StationIndex,
                    false,
                    120000,
                    "协调料盘点运动");
            }

            return true;
        }

        public bool RunModifyStationPos(ProcHandle evt, ModifyStationPos modifyStationPos)
        {
            if (Context?.Stations == null)
            {
                throw CreateAlarmException(evt, "工站列表为空");
            }
            if (modifyStationPos == null)
            {
                throw CreateAlarmException(evt, "点位修改参数为空");
            }
            if (string.IsNullOrWhiteSpace(modifyStationPos.StationName))
            {
                throw CreateAlarmException(evt, "工站名称为空");
            }
            if (string.IsNullOrWhiteSpace(modifyStationPos.RefPosName))
            {
                throw CreateAlarmException(evt, "参考点为空");
            }
            if (string.IsNullOrWhiteSpace(modifyStationPos.TargetPosName))
            {
                throw CreateAlarmException(evt, "目标点为空");
            }
            if (string.IsNullOrWhiteSpace(modifyStationPos.ModifyType))
            {
                throw CreateAlarmException(evt, "修改方式为空");
            }

            DataStation station = Context.Stations.FirstOrDefault(sc => sc.Name == modifyStationPos.StationName);
            if (station == null)
            {
                throw CreateAlarmException(evt, $"找不到工站:{modifyStationPos.StationName}");
            }
            if (station.ListDataPos == null || station.ListDataPos.Count == 0)
            {
                throw CreateAlarmException(evt, $"工站点位列表为空:{modifyStationPos.StationName}");
            }

            DataPos targetPos = station.ListDataPos.FirstOrDefault(sc => sc != null && sc.Name == modifyStationPos.TargetPosName);
            if (targetPos == null)
            {
                throw CreateAlarmException(evt, $"目标点不存在:{modifyStationPos.TargetPosName}");
            }
            if (targetPos.IsTaught == false
                && !string.Equals(modifyStationPos.ModifyType, "替换", StringComparison.Ordinal))
            {
                throw CreateAlarmException(evt, $"叠加修改的目标点尚未人工示教坐标:{modifyStationPos.TargetPosName}");
            }

            double[] refValues = new double[6];
            bool[] refAvailable = new bool[6];
            if (modifyStationPos.RefPosName == "自定义坐标")
            {
                refValues[0] = modifyStationPos.CustomX;
                refValues[1] = modifyStationPos.CustomY;
                refValues[2] = modifyStationPos.CustomZ;
                refValues[3] = modifyStationPos.CustomU;
                refValues[4] = modifyStationPos.CustomV;
                refValues[5] = modifyStationPos.CustomW;
                for (int i = 0; i < 6; i++)
                {
                    refAvailable[i] = true;
                }
            }
            else if (modifyStationPos.RefPosName == "当前位置")
            {
                if (Context.Motion == null)
                {
                    throw CreateAlarmException(evt, "运动控制未初始化");
                }
                if (station.Type != StationType.Axis)
                {
                    short stationIndex = GetStationRuntimeIndex(station, evt);
                    if (!TryAcquireStationMotionResource(evt, stationIndex, out string stationResourceError))
                    {
                        throw CreateAlarmException(evt, stationResourceError);
                    }
                    EnsureStationCommandSucceeded(evt, station, "读取当前位置",
                        Context.Motion.GetStationPosition(stationIndex, 0, out DataPos current));
                    if (current == null || current.GetAllValues().Count < 6)
                    {
                        throw CreateAlarmException(evt, $"工站{station.Name}未返回完整当前位置");
                    }
                    IReadOnlyList<double> currentValues = current.GetAllValues();
                    for (int i = 0; i < 6; i++)
                    {
                        refValues[i] = currentValues[i];
                        refAvailable[i] = true;
                    }
                }
                else
                {
                    if (Context.CardStore == null)
                    {
                        throw CreateAlarmException(evt, "运动控制卡配置未初始化");
                    }
                if (station.dataAxis == null || station.dataAxis.axisConfigs == null || station.dataAxis.axisConfigs.Count < 6)
                {
                    throw CreateAlarmException(evt, $"工站轴配置无效:{modifyStationPos.StationName}");
                }
                for (int i = 0; i < 6; i++)
                {
                    AxisConfig axisConfig = station.dataAxis.axisConfigs[i];
                    if (axisConfig == null)
                    {
                        throw CreateAlarmException(evt, $"工站轴配置为空:{modifyStationPos.StationName}");
                    }
                    if (axisConfig.AxisName == "-1")
                    {
                        refValues[i] = 0;
                        refAvailable[i] = false;
                        continue;
                    }
                    if (!ushort.TryParse(axisConfig.CardNum, out ushort cardNum))
                    {
                        throw CreateAlarmException(evt, $"工站：{modifyStationPos.StationName} 轴卡号无效:{axisConfig.CardNum}");
                    }
                    Axis axisInfo = axisConfig.axis;
                    if (axisInfo == null)
                    {
                        if (!Context.CardStore.TryGetAxisByName(cardNum, axisConfig.AxisName, out axisInfo))
                        {
                            throw CreateAlarmException(evt, $"工站：{modifyStationPos.StationName} 轴配置不存在:{axisConfig.AxisName}");
                        }
                    }
                    int axisNum = axisInfo.AxisNum;
                    if (axisNum < 0)
                    {
                        throw CreateAlarmException(evt, $"工站：{modifyStationPos.StationName} 轴索引无效:{axisConfig.AxisName}");
                    }
                    double axisPos;
                    try
                    {
                        axisPos = Context.Motion.GetAxisPos(cardNum, (ushort)axisNum);
                    }
                    catch (Exception ex)
                    {
                        throw CreateAlarmException(evt, $"读取当前位置失败:{axisConfig.AxisName}", ex);
                    }
                    refValues[i] = axisPos;
                    refAvailable[i] = true;
                }
                }
            }
            else
            {
                DataPos refPos = station.ListDataPos.FirstOrDefault(sc => sc != null && sc.Name == modifyStationPos.RefPosName);
                if (refPos == null)
                {
                    throw CreateAlarmException(evt, $"参考点不存在:{modifyStationPos.RefPosName}");
                }
                if (refPos.IsTaught == false)
                {
                    throw CreateAlarmException(evt, $"参考点尚未人工示教坐标:{modifyStationPos.RefPosName}");
                }
                List<double> posValues = refPos.GetAllValues();
                if (posValues == null || posValues.Count < 6)
                {
                    throw CreateAlarmException(evt, $"参考点数据无效:{modifyStationPos.RefPosName}");
                }
                for (int i = 0; i < 6; i++)
                {
                    refValues[i] = posValues[i];
                    refAvailable[i] = true;
                }
            }

            double[] targetValues = new double[6]
            {
                targetPos.X,
                targetPos.Y,
                targetPos.Z,
                targetPos.U,
                targetPos.V,
                targetPos.W
            };

            if (modifyStationPos.ModifyType == "替换")
            {
                for (int i = 0; i < 6; i++)
                {
                    if (refAvailable[i])
                    {
                        targetValues[i] = refValues[i];
                    }
                }
            }
            else if (modifyStationPos.ModifyType == "叠加")
            {
                for (int i = 0; i < 6; i++)
                {
                    if (refAvailable[i])
                    {
                        targetValues[i] += refValues[i];
                    }
                }
            }
            else
            {
                throw CreateAlarmException(evt, $"修改方式无效:{modifyStationPos.ModifyType}");
            }

            if (Context.Motion == null)
            {
                throw CreateAlarmException(evt, "运动控制未初始化");
            }
            DataPos updatedPoint = (DataPos)targetPos.Clone();
            updatedPoint.X = targetValues[0];
            updatedPoint.Y = targetValues[1];
            updatedPoint.Z = targetValues[2];
            updatedPoint.U = targetValues[3];
            updatedPoint.V = targetValues[4];
            updatedPoint.W = targetValues[5];
            updatedPoint.IsTaught = true;
            short targetStationIndex = GetStationRuntimeIndex(station, evt);
            if (!TryAcquireStationMotionResource(evt, targetStationIndex, out string targetResourceError))
            {
                throw CreateAlarmException(evt, targetResourceError);
            }
            EnsureStationCommandSucceeded(evt, station, "保存点位",
                Context.Motion.SaveStationPoint(targetStationIndex, updatedPoint));

            return true;
        }

        public bool RunGetStationPos(ProcHandle evt, GetStationPos getStationPos)
        {
            if (Context?.Stations == null)
            {
                throw CreateAlarmException(evt, "工站列表为空");
            }
            if (getStationPos == null)
            {
                throw CreateAlarmException(evt, "获取工站位置参数为空");
            }
            if (string.IsNullOrWhiteSpace(getStationPos.StationName))
            {
                throw CreateAlarmException(evt, "工站名称为空");
            }
            if (string.IsNullOrWhiteSpace(getStationPos.SourceType))
            {
                throw CreateAlarmException(evt, "获取方式为空");
            }
            if (string.IsNullOrWhiteSpace(getStationPos.SaveType))
            {
                throw CreateAlarmException(evt, "保存方式为空");
            }

            DataStation station = Context.Stations.FirstOrDefault(sc => sc.Name == getStationPos.StationName);
            if (station == null)
            {
                throw CreateAlarmException(evt, $"找不到工站:{getStationPos.StationName}");
            }

            double[] values = new double[6];
            bool[] available = new bool[6];
            DataPos sourcePoint = null;
            if (getStationPos.SourceType == "当前位置")
            {
                if (Context.Motion == null)
                {
                    throw CreateAlarmException(evt, "运动控制未初始化");
                }
                if (station.Type != StationType.Axis)
                {
                    short stationIndex = GetStationRuntimeIndex(station, evt);
                    if (!TryAcquireStationMotionResource(evt, stationIndex, out string stationResourceError))
                    {
                        throw CreateAlarmException(evt, stationResourceError);
                    }
                    EnsureStationCommandSucceeded(evt, station, "读取当前位置",
                        Context.Motion.GetStationPosition(stationIndex, 0, out sourcePoint));
                    if (sourcePoint == null || sourcePoint.GetAllValues().Count < 6)
                    {
                        throw CreateAlarmException(evt, $"工站{station.Name}未返回完整当前位置");
                    }
                    IReadOnlyList<double> currentValues = sourcePoint.GetAllValues();
                    for (int i = 0; i < 6; i++)
                    {
                        values[i] = currentValues[i];
                        available[i] = true;
                    }
                }
                else
                {
                    if (Context.CardStore == null)
                    {
                        throw CreateAlarmException(evt, "运动控制卡配置未初始化");
                    }
                if (station.dataAxis == null || station.dataAxis.axisConfigs == null || station.dataAxis.axisConfigs.Count < 6)
                {
                    throw CreateAlarmException(evt, $"工站轴配置无效:{getStationPos.StationName}");
                }
                for (int i = 0; i < 6; i++)
                {
                    AxisConfig axisConfig = station.dataAxis.axisConfigs[i];
                    if (axisConfig == null)
                    {
                        throw CreateAlarmException(evt, $"工站轴配置为空:{getStationPos.StationName}");
                    }
                    if (axisConfig.AxisName == "-1")
                    {
                        available[i] = false;
                        continue;
                    }
                    if (!ushort.TryParse(axisConfig.CardNum, out ushort cardNum))
                    {
                        throw CreateAlarmException(evt, $"工站：{getStationPos.StationName} 轴卡号无效:{axisConfig.CardNum}");
                    }
                    Axis axisInfo = axisConfig.axis;
                    if (axisInfo == null)
                    {
                        if (!Context.CardStore.TryGetAxisByName(cardNum, axisConfig.AxisName, out axisInfo))
                        {
                            throw CreateAlarmException(evt, $"工站：{getStationPos.StationName} 轴配置不存在:{axisConfig.AxisName}");
                        }
                    }
                    int axisNum = axisInfo.AxisNum;
                    if (axisNum < 0)
                    {
                        throw CreateAlarmException(evt, $"工站：{getStationPos.StationName} 轴索引无效:{axisConfig.AxisName}");
                    }
                    double axisPos;
                    try
                    {
                        axisPos = Context.Motion.GetAxisPos(cardNum, (ushort)axisNum);
                    }
                    catch (Exception ex)
                    {
                        throw CreateAlarmException(evt, $"读取当前位置失败:{axisConfig.AxisName}", ex);
                    }
                    values[i] = axisPos;
                    available[i] = true;
                }
                }
            }
            else if (getStationPos.SourceType == "指定点位")
            {
                if (string.IsNullOrWhiteSpace(getStationPos.SourcePosName))
                {
                    throw CreateAlarmException(evt, "指定点位为空");
                }
                if (station.ListDataPos == null || station.ListDataPos.Count == 0)
                {
                    throw CreateAlarmException(evt, $"工站点位列表为空:{getStationPos.StationName}");
                }
                DataPos sourcePos = station.ListDataPos.FirstOrDefault(sc => sc != null && sc.Name == getStationPos.SourcePosName);
                if (sourcePos == null)
                {
                    throw CreateAlarmException(evt, $"指定点位不存在:{getStationPos.SourcePosName}");
                }
                if (sourcePos.IsTaught == false)
                {
                    throw CreateAlarmException(evt, $"指定点位尚未人工示教坐标:{getStationPos.SourcePosName}");
                }
                List<double> sourceValues = sourcePos.GetAllValues();
                if (sourceValues == null || sourceValues.Count < 6)
                {
                    throw CreateAlarmException(evt, $"指定点位数据无效:{getStationPos.SourcePosName}");
                }
                for (int i = 0; i < 6; i++)
                {
                    values[i] = sourceValues[i];
                    available[i] = true;
                }
                sourcePoint = sourcePos;
            }
            else
            {
                throw CreateAlarmException(evt, $"获取方式无效:{getStationPos.SourceType}");
            }

            if (getStationPos.SaveType == "保存到点位")
            {
                if (string.IsNullOrWhiteSpace(getStationPos.TargetPosName))
                {
                    throw CreateAlarmException(evt, "保存点位为空");
                }
                if (station.ListDataPos == null || station.ListDataPos.Count == 0)
                {
                    throw CreateAlarmException(evt, $"工站点位列表为空:{getStationPos.StationName}");
                }
                DataPos targetPos = station.ListDataPos.FirstOrDefault(sc => sc != null && sc.Name == getStationPos.TargetPosName);
                if (targetPos == null)
                {
                    throw CreateAlarmException(evt, $"保存点位不存在:{getStationPos.TargetPosName}");
                }
                if (Context.Motion == null)
                {
                    throw CreateAlarmException(evt, "运动控制未初始化");
                }
                DataPos updatedPoint = (DataPos)targetPos.Clone();
                if (available[0]) updatedPoint.X = values[0];
                if (available[1]) updatedPoint.Y = values[1];
                if (available[2]) updatedPoint.Z = values[2];
                if (available[3]) updatedPoint.U = values[3];
                if (available[4]) updatedPoint.V = values[4];
                if (available[5]) updatedPoint.W = values[5];
                if (sourcePoint?.Pose != null)
                {
                    updatedPoint.Pose = (short[])sourcePoint.Pose.Clone();
                }
                updatedPoint.IsTaught = true;
                short stationIndex = GetStationRuntimeIndex(station, evt);
                if (!TryAcquireStationMotionResource(evt, stationIndex, out string stationResourceError))
                {
                    throw CreateAlarmException(evt, stationResourceError);
                }
                EnsureStationCommandSucceeded(evt, station, "保存点位",
                    Context.Motion.SaveStationPoint(stationIndex, updatedPoint));
                return true;
            }

            if (getStationPos.SaveType == "保存到变量")
            {
                ValueConfigStore valueStore = Context.ValueStore;
                if (valueStore == null)
                {
                    throw CreateAlarmException(evt, "变量库未初始化");
                }
                string source = evt?.GetOperationSource();

                bool SaveAxisValue(string label, bool hasValue, double axisValue, string index, string index2Index, string name, string name2Index)
                {
                    if (!ValueRef.TryCreate(index, index2Index, name, name2Index, true, label, out ValueRef valueRef, out string refError))
                    {
                        throw CreateAlarmException(evt, refError);
                    }
                    if (valueRef.IsEmpty)
                    {
                        return false;
                    }
                    if (!hasValue)
                    {
                        throw CreateAlarmException(evt, $"{label}无法获取当前位置");
                    }
                    if (!valueRef.TryResolveValue(valueStore, label, evt.procId, out DicValue valueItem, out string resolveError))
                    {
                        throw CreateAlarmException(evt, resolveError);
                    }
                    if (!valueStore.SetValueByIndexForProcess(valueItem.Index, axisValue.ToString(), evt.procId, source))
                    {
                        string valueName = string.IsNullOrWhiteSpace(valueItem.Name) ? $"索引{valueItem.Index}" : valueItem.Name;
                        throw CreateAlarmException(evt, $"保存变量失败:{valueName}");
                    }
                    return true;
                }

                bool savedAny = false;
                savedAny |= SaveAxisValue("X变量", available[0], values[0], getStationPos.OutputXIndex, getStationPos.OutputXIndex2Index, getStationPos.OutputXName, getStationPos.OutputXName2Index);
                savedAny |= SaveAxisValue("Y变量", available[1], values[1], getStationPos.OutputYIndex, getStationPos.OutputYIndex2Index, getStationPos.OutputYName, getStationPos.OutputYName2Index);
                savedAny |= SaveAxisValue("Z变量", available[2], values[2], getStationPos.OutputZIndex, getStationPos.OutputZIndex2Index, getStationPos.OutputZName, getStationPos.OutputZName2Index);
                savedAny |= SaveAxisValue("U变量", available[3], values[3], getStationPos.OutputUIndex, getStationPos.OutputUIndex2Index, getStationPos.OutputUName, getStationPos.OutputUName2Index);
                savedAny |= SaveAxisValue("V变量", available[4], values[4], getStationPos.OutputVIndex, getStationPos.OutputVIndex2Index, getStationPos.OutputVName, getStationPos.OutputVName2Index);
                savedAny |= SaveAxisValue("W变量", available[5], values[5], getStationPos.OutputWIndex, getStationPos.OutputWIndex2Index, getStationPos.OutputWName, getStationPos.OutputWName2Index);
                if (!savedAny)
                {
                    throw CreateAlarmException(evt, "保存变量未配置");
                }
                return true;
            }

            throw CreateAlarmException(evt, $"保存方式无效:{getStationPos.SaveType}");
        }

        public bool RunStationRunRel(ProcHandle evt, StationRunRel stationRunRel)
        {

            DataStation station;
            //if (stationRunRel.StationIndex != -1)
            //{
            //    station = Context.Stations[stationRunRel.StationIndex];
            //}
            //else
            //{
            station = Context.Stations.FirstOrDefault(sc => sc.Name == stationRunRel.StationName);
            //}

            if (station == null)
            {
                MarkAlarm(evt, $"找不到工站:{stationRunRel.StationName}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (station.Type != StationType.Axis)
            {
                if (Context.Motion == null || Context.ValueStore == null)
                {
                    throw CreateAlarmException(evt, "机器人工站运行时或变量库未初始化");
                }
                List<double> configuredOffsets = stationRunRel.GetAllValues();
                List<string> offsetVariables = stationRunRel.GetAllValuesV();
                if (configuredOffsets == null || configuredOffsets.Count < 6
                    || offsetVariables == null || offsetVariables.Count < 6)
                {
                    throw CreateAlarmException(evt, "机器人工站偏移参数不完整");
                }
                var offsets = new double[6];
                for (int axis = 0; axis < offsets.Length; axis++)
                {
                    offsets[axis] = configuredOffsets[axis] == 0
                        ? Context.ValueStore.GetValueByNameForProcess(offsetVariables[axis], evt.procId).GetDValue()
                        : configuredOffsets[axis];
                    if (double.IsNaN(offsets[axis]) || double.IsInfinity(offsets[axis]))
                    {
                        throw CreateAlarmException(evt, $"机器人工站{axis + 1}通道偏移无效");
                    }
                }
                short stationIndex = GetStationRuntimeIndex(station, evt);
                if (!TryAcquireStationMotionResource(evt, stationIndex, out string stationResourceError))
                {
                    throw CreateAlarmException(evt, stationResourceError);
                }
                if (stationRunRel.ChangeVel == "改变速度")
                {
                    double velocity = stationRunRel.Vel == 0
                        ? Context.ValueStore.GetValueByNameForProcess(stationRunRel.VelV, evt.procId).GetDValue()
                        : stationRunRel.Vel;
                    double acceleration = stationRunRel.Acc == 0
                        ? Context.ValueStore.GetValueByNameForProcess(stationRunRel.AccV, evt.procId).GetDValue()
                        : stationRunRel.Acc;
                    double deceleration = stationRunRel.Dec == 0
                        ? Context.ValueStore.GetValueByNameForProcess(stationRunRel.DecV, evt.procId).GetDValue()
                        : stationRunRel.Dec;
                    EnsureStationCommandSucceeded(evt, station, "设置速度",
                        Context.Motion.SetStationSpeed(stationIndex, velocity, acceleration,
                            deceleration, -1, StationSpeedType.Joint));
                }
                DataPos expected = null;
                if (stationRunRel.CheckInPosition)
                {
                    EnsureStationCommandSucceeded(evt, station, "读取偏移前位置",
                        Context.Motion.GetStationPosition(stationIndex, 0, out expected));
                    if (expected == null)
                    {
                        throw CreateAlarmException(evt, $"工站{station.Name}未返回偏移前位置");
                    }
                    expected = (DataPos)expected.Clone();
                    expected.X += offsets[0];
                    expected.Y += offsets[1];
                    expected.Z += offsets[2];
                    expected.U += offsets[3];
                    expected.V += offsets[4];
                    expected.W += offsets[5];
                }
                EnsureStationCommandSucceeded(evt, station, "偏移运动",
                    Context.Motion.MoveStationOffset(stationIndex, -1, offsets, StationMoveMode.Go));
                if (!stationRunRel.ContinueWithoutWaiting)
                {
                    int timeout = checked((int)ResolveStationTimeout(
                        evt, stationRunRel.TimeoutMs, stationRunRel.TimeoutVariableName, stationRunRel.Name));
                    WaitRobotStation(evt, station, stationIndex, false, timeout, "偏移运动");
                    if (expected != null && !evt.CancellationToken.IsCancellationRequested)
                    {
                        CheckRobotStationInPoint(evt, station, stationIndex, expected, null);
                    }
                }
                return true;
            }
            List<double> axisConfiguredOffsets = stationRunRel.GetAllValues();
            List<string> axisOffsetVariables = stationRunRel.GetAllValuesV();
            if (axisConfiguredOffsets == null || axisConfiguredOffsets.Count < 6
                || axisOffsetVariables == null || axisOffsetVariables.Count < 6)
            {
                throw CreateAlarmException(evt, "工站相对运动配置不完整");
            }
            if (Context.ValueStore == null && axisConfiguredOffsets.Any(value => value == 0))
            {
                throw CreateAlarmException(evt, "工站相对运动变量库未初始化");
            }
            var axisOffsets = new double[6];
            for (int channel = 0; channel < axisOffsets.Length; channel++)
            {
                axisOffsets[channel] = axisConfiguredOffsets[channel] == 0
                    ? Context.ValueStore.GetValueByNameForProcess(
                        axisOffsetVariables[channel], evt.procId).GetDValue()
                    : axisConfiguredOffsets[channel];
                if (double.IsNaN(axisOffsets[channel]) || double.IsInfinity(axisOffsets[channel]))
                {
                    throw CreateAlarmException(evt,
                        $"工站{station.Name}通道{channel + 1}偏移无效");
                }
            }

            AxisStationMovePlan plan = ReserveAxisStationMove(
                evt,
                station,
                null,
                stationRunRel.Name);
            ApplyAxisStationMoveSpeed(
                evt,
                station,
                plan,
                stationRunRel.Name,
                stationRunRel.ChangeVel == "改变速度",
                stationRunRel.Vel,
                stationRunRel.VelV,
                stationRunRel.Acc,
                stationRunRel.AccV,
                stationRunRel.Dec,
                stationRunRel.DecV);
            DataPos expectedAxisPosition = null;
            if (stationRunRel.CheckInPosition)
            {
                EnsureStationCommandSucceeded(
                    evt,
                    station,
                    "读取偏移前位置",
                    Context.Motion.GetStationPosition(
                        plan.StationIndex,
                        0,
                        out expectedAxisPosition));
                if (expectedAxisPosition == null)
                {
                    throw CreateAlarmException(evt,
                        $"工站{station.Name}未返回偏移前位置");
                }
                expectedAxisPosition = (DataPos)expectedAxisPosition.Clone();
                expectedAxisPosition.X += axisOffsets[0];
                expectedAxisPosition.Y += axisOffsets[1];
                expectedAxisPosition.Z += axisOffsets[2];
                expectedAxisPosition.U += axisOffsets[3];
                expectedAxisPosition.V += axisOffsets[4];
                expectedAxisPosition.W += axisOffsets[5];
            }
            EnsureStationCommandSucceeded(
                evt,
                station,
                "协调偏移运动",
                Context.Motion.MoveStationOffset(
                    plan.StationIndex,
                    -1,
                    axisOffsets,
                    StationMoveMode.Move));
            if (!stationRunRel.ContinueWithoutWaiting)
            {
                int timeout = checked((int)ResolveStationTimeout(
                    evt,
                    stationRunRel.TimeoutMs,
                    stationRunRel.TimeoutVariableName,
                    stationRunRel.Name));
                WaitRobotStation(
                    evt,
                    station,
                    plan.StationIndex,
                    false,
                    timeout,
                    "协调偏移运动");
                if (expectedAxisPosition != null
                    && !evt.CancellationToken.IsCancellationRequested)
                {
                    CheckRobotStationInPoint(
                        evt,
                        station,
                        plan.StationIndex,
                        expectedAxisPosition,
                        plan.InactiveChannels);
                }
            }
            return true;
        }
        public bool RunSetStationVel(ProcHandle evt, SetStationVel setStationVel)
        {
            DataStation station;
            if (setStationVel.StationIndex != -1)
            {
                if (Context.Stations == null || setStationVel.StationIndex < 0
                    || setStationVel.StationIndex >= Context.Stations.Count)
                {
                    MarkAlarm(evt, $"工站索引无效:{setStationVel.StationIndex}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                station = Context.Stations[setStationVel.StationIndex];
            }
            else
            {
                station = Context.Stations.FirstOrDefault(sc => sc.Name == setStationVel.StationName);
            }

            if (station == null)
            {
                MarkAlarm(evt, $"找不到工站:{setStationVel.StationName}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            double Vel = 0;
            double Acc = 0;
            double Dec = 0;

                Vel = setStationVel.Vel == 0 ? Context.ValueStore.GetValueByNameForProcess(setStationVel.VelV, evt.procId).GetDValue() : setStationVel.Vel;
                Acc = setStationVel.Acc == 0 ? Context.ValueStore.GetValueByNameForProcess(setStationVel.AccV, evt.procId).GetDValue() : setStationVel.Acc;
                Dec = setStationVel.Dec == 0 ? Context.ValueStore.GetValueByNameForProcess(setStationVel.DecV, evt.procId).GetDValue() : setStationVel.Dec;

                if (Vel <= 0 || Vel > 100 || Acc <= 0 || Acc > 100 || Dec <= 0 || Dec > 100
                    || double.IsNaN(Vel) || double.IsInfinity(Vel)
                    || double.IsNaN(Acc) || double.IsInfinity(Acc)
                    || double.IsNaN(Dec) || double.IsInfinity(Dec))
                {
                    throw CreateAlarmException(evt, "自动生产速度、加速能力和减速能力必须在1%到100%之间。");
                }

                if (station.Type != StationType.Axis)
                {
                    short stationIndex = GetStationRuntimeIndex(station, evt);
                    if (!TryAcquireStationMotionResource(evt, stationIndex, out string stationResourceError))
                    {
                        throw CreateAlarmException(evt, stationResourceError);
                    }
                    short channel = -1;
                    if (!string.Equals(setStationVel.SetAxisObj, "工站", StringComparison.Ordinal))
                    {
                        string[] channelNames = { "X", "Y", "Z", "U", "V", "W" };
                        int channelIndex = Array.FindIndex(channelNames,
                            name => string.Equals(name, setStationVel.SetAxisObj, StringComparison.OrdinalIgnoreCase));
                        if (channelIndex < 0)
                        {
                            throw CreateAlarmException(evt,
                                $"机器人工站速度设置对象无效:{setStationVel.SetAxisObj}");
                        }
                        channel = (short)channelIndex;
                    }
                    EnsureStationCommandSucceeded(evt, station, "设置速度",
                        Context.Motion.SetStationSpeed(stationIndex, Vel, Acc, Dec,
                            channel, StationSpeedType.Joint));
                    return true;
                }

                if (setStationVel.SetAxisObj == "工站")
                {
                    for (int i = 0; i < 6; i++)
                    {
                        if (station.dataAxis.axisConfigs[i].AxisName != "-1")
                        {
                            if (!ushort.TryParse(station.dataAxis.axisConfigs[i].CardNum, out ushort cardNum))
                            {
                                MarkAlarm(evt, $"卡号无效:{station.dataAxis.axisConfigs[i].CardNum}");
                                throw CreateAlarmException(evt, evt?.alarmMsg);
                            }
                            ushort axisNum = (ushort)station.dataAxis.axisConfigs[i].axis.AxisNum;

                            if (!Context.CardStore.TryGetAxis(cardNum, axisNum, out _))
                            {
                                MarkAlarm(evt, $"工站：{setStationVel.StationName} {cardNum}号卡{axisNum}号轴配置不存在");
                                throw CreateAlarmException(evt, evt?.alarmMsg);
                            }
                            Context.AxisMotionParameters.Set(cardNum, axisNum, Vel, Acc, Dec);
                        }
                    }
                }
                else
                {
                    AxisConfig axisInfo = station.dataAxis.axisConfigs.FirstOrDefault(sc => sc.AxisName == setStationVel.SetAxisObj);
                    if (axisInfo == null)
                    {
                        MarkAlarm(evt, $"工站：{setStationVel.StationName} 轴配置不存在");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }
                    if (!int.TryParse(axisInfo.CardNum, out int cardNum))
                    {
                        MarkAlarm(evt, $"卡号无效:{axisInfo.CardNum}");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }
                    int axisNum = axisInfo.axis.AxisNum;
                    if (!Context.CardStore.TryGetAxis(cardNum, axisNum, out _))
                    {
                        MarkAlarm(evt, $"工站：{setStationVel.StationName} {cardNum}号卡{axisNum}号轴配置不存在");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }
                    Context.AxisMotionParameters.Set((ushort)cardNum, (ushort)axisNum, Vel, Acc, Dec);
                }
            return true;
        }
        public bool RunStationStop(ProcHandle evt, StationStop stationStop)
        {
            DataStation station = Context.Stations.FirstOrDefault(sc => sc.Name == stationStop.StationName);

            if (station == null)
            {
                MarkAlarm(evt, $"找不到工站:{stationStop.StationName}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (station.Type != StationType.Axis)
            {
                short stationIndex = GetStationRuntimeIndex(station, evt);
                if (!TryAcquireStationMotionResource(evt, stationIndex, out string stationResourceError))
                {
                    throw CreateAlarmException(evt, stationResourceError);
                }
                // 机器人六个通道属于同一个控制器动作，任一“单轴停止”都必须按整站停止处理。
                EnsureStationCommandSucceeded(evt, station, "停止",
                    Context.Motion.StopStation(stationIndex, false));
                return true;
            }
            if (stationStop.StopEntireStation)
            {
                StopStation(station, evt);
            }
            else
            {
                List<bool> AxisParams = stationStop.GetAllValues();
                var axes = new List<AxisCommandRequest>();
                for (int i = 0; i < 6; i++)
                {
                    if (AxisParams[i] == true)
                    {
                        if (!int.TryParse(station.dataAxis.axisConfigs[i].CardNum, out int cardNum))
                        {
                            MarkAlarm(evt, $"卡号无效:{station.dataAxis.axisConfigs[i].CardNum}");
                            throw CreateAlarmException(evt, evt?.alarmMsg);
                        }
                        int axisNum = station.dataAxis.axisConfigs[i].axis.AxisNum;
                        axes.Add(new AxisCommandRequest((ushort)cardNum, (ushort)axisNum, AxisCommandKind.Motion));
                    }
                }
                if (!TryAcquireMotionResources(evt,
                    axes.Select(item => BuildMotionResourceKey(item.Card, item.Axis)), out string resourceError))
                {
                    throw CreateAlarmException(evt, resourceError);
                }
                StopAxesAndWait(axes, evt, 30000);
            }
            return true;
        }
        public bool RunWaitStationStop(ProcHandle evt, WaitStationStop waitStationStop)
        {
            DataStation station;
            if (waitStationStop.StationIndex != -1)
            {
                if (Context.Stations == null || waitStationStop.StationIndex < 0
                    || waitStationStop.StationIndex >= Context.Stations.Count)
                {
                    MarkAlarm(evt, $"工站索引无效:{waitStationStop.StationIndex}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                station = Context.Stations[waitStationStop.StationIndex];
            }
            else
            {
                station = Context.Stations.FirstOrDefault(sc => sc.Name == waitStationStop.StationName);
            }
            if (station == null)
            {
                MarkAlarm(evt, $"找不到工站:{waitStationStop.StationName}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            if (station.Type != StationType.Axis)
            {
                short stationIndex = GetStationRuntimeIndex(station, evt);
                if (!TryAcquireStationMotionResource(evt, stationIndex, out string stationResourceError))
                {
                    throw CreateAlarmException(evt, stationResourceError);
                }
                int timeout = checked((int)ResolveStationTimeout(
                    evt,
                    waitStationStop.TimeoutMs,
                    waitStationStop.TimeoutVariableName,
                    waitStationStop.Name));
                WaitRobotStation(
                    evt,
                    station,
                    stationIndex,
                    waitStationStop.WaitForHomeCompleted,
                    timeout,
                    "等待运动");
                return true;
            }
            List<ushort> cardNums = new List<ushort>();
            List<ushort> axisNums = new List<ushort>();
            for (int i = 0; i < 6; i++)
            {
                if (station.dataAxis.axisConfigs[i].AxisName != "-1")
                {
                    if (!ushort.TryParse(station.dataAxis.axisConfigs[i].CardNum, out ushort cardNum))
                    {
                        MarkAlarm(evt, $"卡号无效:{station.dataAxis.axisConfigs[i].CardNum}");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }
                    ushort axisNum = (ushort)station.dataAxis.axisConfigs[i].axis.AxisNum;
                    cardNums.Add(cardNum);
                    axisNums.Add(axisNum);
                }
            }
            Stopwatch stopwatch = Stopwatch.StartNew();
            double time;
            if (waitStationStop.TimeoutMs > 0)
                time = waitStationStop.TimeoutMs;
            else
            {

                time = Context.ValueStore.GetValueByNameForProcess(waitStationStop.TimeoutVariableName, evt.procId).GetDValue();
            }
            if (time <= 0)
            {
                MarkAlarm(evt, $"{waitStationStop.Name}超时配置无效");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            while (!evt.CancellationToken.IsCancellationRequested
                )
            {
                bool isInPos = false;

                if (stopwatch.ElapsedMilliseconds > time)
                {
                    MarkAlarm(evt, waitStationStop.Name + "等待超时");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                for (int i = 0; i < cardNums.Count; i++)
                {
                    if (waitStationStop.WaitForHomeCompleted)
                    {
                        if (!Context.CardStore.TryGetAxis(cardNums[i], axisNums[i], out Axis axisInfo))
                        {
                            MarkAlarm(evt, $"工站：{waitStationStop.Name} {cardNums[i]}号卡{axisNums[i]}号轴配置不存在");
                            throw CreateAlarmException(evt, evt?.alarmMsg);
                        }
                        if (Context.Motion.GetInPos(cardNums[i], axisNums[i])
                            && Context.Motion.HomeStatus(cardNums[i], axisNums[i]))
                        {
                            isInPos = true;
                        }
                        else
                        {
                            isInPos = false;
                            break;
                        }
                    }
                    else
                    {
                        if (Context.Motion.GetInPos(cardNums[i], axisNums[i]))
                        {
                            isInPos = true;
                        }
                        else
                        {
                            isInPos = false;
                            break;
                        }
                    }
                }
                if (isInPos)
                    break;
                Delay(1, evt);
            }
            return true;
        }

        private void StopStation(DataStation station, ProcHandle evt)
        {
            if (station == null || Context.Motion == null)
            {
                return;
            }
            var axes = new List<AxisCommandRequest>();
            for (int i = 0; i < 6; i++)
            {
                if (station.dataAxis.axisConfigs[i].AxisName != "-1")
                {
                    if (!ushort.TryParse(station.dataAxis.axisConfigs[i].CardNum, out ushort cardNum))
                    {
                        throw new InvalidOperationException($"卡号无效:{station.dataAxis.axisConfigs[i].CardNum}");
                    }
                    axes.Add(new AxisCommandRequest(cardNum,
                        (ushort)station.dataAxis.axisConfigs[i].axis.AxisNum,
                        AxisCommandKind.Motion));
                }
            }
            if (!TryAcquireMotionResources(evt,
                axes.Select(item => BuildMotionResourceKey(item.Card, item.Axis)), out string resourceError))
            {
                throw CreateAlarmException(evt, resourceError);
            }
            StopAxesAndWait(axes, evt, 30000);
        }

        private void StopAxesAndWait(IReadOnlyCollection<AxisCommandRequest> axes, ProcHandle evt,
            int timeoutMilliseconds)
        {
            if (axes == null || axes.Count == 0)
            {
                return;
            }
            AxisCommandRequest[] targets = axes
                .GroupBy(item => BuildMotionResourceKey(item.Card, item.Axis))
                .Select(group => group.First())
                .ToArray();
            try
            {
                foreach (AxisCommandRequest target in targets)
                {
                    Context.Motion.StopOneAxis(target.Card, target.Axis, 0);
                }
            }
            catch (Exception ex)
            {
                foreach (AxisCommandRequest target in targets)
                {
                    try
                    {
                        Context.Motion.StopOneAxis(target.Card, target.Axis, 1);
                    }
                    catch
                    {
                    }
                }
                Context.Safety.Lock($"工站停止指令下发失败，目标轴已尝试急停:{ex.Message}");
                throw CreateAlarmException(evt, "工站停止指令下发失败，系统已锁定。");
            }
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (targets.Any(target => !Context.Motion.GetInPos(target.Card, target.Axis)))
            {
                if (stopwatch.ElapsedMilliseconds > timeoutMilliseconds)
                {
                    foreach (AxisCommandRequest target in targets)
                    {
                        Context.Motion.StopOneAxis(target.Card, target.Axis, 1);
                    }
                    Context.Safety.Lock("工站停止超时，所有目标轴已急停。");
                    throw CreateAlarmException(evt, "工站停止超时，所有目标轴已急停并锁定系统。");
                }
                Thread.Sleep(5);
            }
        }

        private Exception HandlePartialAxisStartFailure(IReadOnlyCollection<AxisCommandRequest> startedAxes,
            ProcHandle evt, Exception commandException)
        {
            if (startedAxes == null || startedAxes.Count == 0)
            {
                return commandException;
            }
            try
            {
                StopAxesAndWait(startedAxes, evt, 30000);
                return new InvalidOperationException(
                    $"多轴指令部分下发失败，已启动轴均已停止:{commandException.Message}", commandException);
            }
            catch (Exception stopException)
            {
                string message = $"多轴指令部分下发失败且安全停止失败:{commandException.Message}; {stopException.Message}";
                Context.Safety.Lock(message);
                return new InvalidOperationException(message, commandException);
            }
        }

        private void ReportHomeAlarm(ProcHandle evt, string message, bool stopOnAlarm)
        {
            MarkAlarm(evt, message);
            if (stopOnAlarm && evt != null)
            {
                HandleAlarm(null, evt);
                return;
            }
            Logger?.Log(message, LogLevel.Error);
        }

        private void HomeStationBySeq(int dataStationIndex, ProcHandle evt, bool stopOnAlarm)
        {
            if (Context.Stations == null || dataStationIndex < 0 || dataStationIndex >= Context.Stations.Count)
            {
                ReportHomeAlarm(evt, $"工站索引无效:{dataStationIndex}", stopOnAlarm);
                return;
            }
            DataStation station = Context.Stations[dataStationIndex];
            List<AxisName> seq = station.homeSeq.axisSeq;
            for (int i = 0; i < 6; i++)
            {
                if (evt.CancellationToken.IsCancellationRequested)
                {
                    return;
                }
                foreach (var item in station.dataAxis.axisConfigs)
                {
                    if (evt.CancellationToken.IsCancellationRequested)
                    {
                        return;
                    }
                    if (item.AxisName == seq[i].Name && item.AxisName != "-1")
                    {
                        if (!ushort.TryParse(item.CardNum, out ushort cardNum))
                        {
                            ReportHomeAlarm(evt, $"卡号无效:{item.CardNum}", stopOnAlarm);
                            return;
                        }
                        if (!HomeSingleAxis(cardNum, (ushort)item.axis.AxisNum, evt, stopOnAlarm))
                        {
                            return;
                        }
                        break;
                    }
                }
            }
            for (int j = 0; j < station.dataAxis.axisConfigs.Count; j++)
            {
                ushort index = (ushort)j;
                if (station.dataAxis.axisConfigs[j].AxisName != "-1")
                {
                    if (!ushort.TryParse(station.dataAxis.axisConfigs[index].CardNum, out ushort cardNum))
                    {
                        ReportHomeAlarm(evt, $"卡号无效:{station.dataAxis.axisConfigs[index].CardNum}", stopOnAlarm);
                        return;
                    }
                    if (Context.Motion != null
                        && !Context.Motion.HomeStatus(cardNum,
                            (ushort)station.dataAxis.axisConfigs[index].axis.AxisNum))
                    {
                        Task task = Task.Run(() =>
                        {
                            if (evt.CancellationToken.IsCancellationRequested)
                            {
                                return;
                            }
                            if (!ushort.TryParse(station.dataAxis.axisConfigs[index].CardNum, out ushort innerCardNum))
                            {
                                ReportHomeAlarm(evt, $"卡号无效:{station.dataAxis.axisConfigs[index].CardNum}", stopOnAlarm);
                                return;
                            }
                            HomeSingleAxis(innerCardNum,
                                (ushort)station.dataAxis.axisConfigs[index].axis.AxisNum, evt, stopOnAlarm);
                        }, evt.CancellationToken);
                        evt.RunningTasks.Add(task);
                    }
                }
            }
        }

        private void HomeStationByAll(int dataStationIndex, ProcHandle evt, bool stopOnAlarm)
        {
            if (Context.Stations == null || dataStationIndex < 0 || dataStationIndex >= Context.Stations.Count)
            {
                ReportHomeAlarm(evt, $"工站索引无效:{dataStationIndex}", stopOnAlarm);
                return;
            }
            DataStation station = Context.Stations[dataStationIndex];
            for (int j = 0; j < station.dataAxis.axisConfigs.Count; j++)
            {
                if (evt.CancellationToken.IsCancellationRequested)
                {
                    return;
                }
                ushort index = (ushort)j;
                if (station.dataAxis.axisConfigs[j].AxisName != "-1")
                {
                    Task task = Task.Run(() =>
                    {
                        if (evt.CancellationToken.IsCancellationRequested)
                        {
                            return;
                        }
                        if (!ushort.TryParse(station.dataAxis.axisConfigs[index].CardNum, out ushort cardNum))
                        {
                            ReportHomeAlarm(evt, $"卡号无效:{station.dataAxis.axisConfigs[index].CardNum}", stopOnAlarm);
                            return;
                        }
                        HomeSingleAxis(cardNum,
                            (ushort)station.dataAxis.axisConfigs[index].axis.AxisNum, evt, stopOnAlarm);
                    }, evt.CancellationToken);
                    evt.RunningTasks.Add(task);
                }
            }
        }

        private bool HomeSingleAxis(ushort cardNum, ushort axis, ProcHandle evt, bool stopOnAlarm)
        {
            if (Context.Motion == null || Context.CardStore == null)
            {
                ReportHomeAlarm(evt, "运动控制未初始化", stopOnAlarm);
                return false;
            }
            if (evt.CancellationToken.IsCancellationRequested)
            {
                return false;
            }
            if (!Context.Motion.GetInPos(cardNum, axis))
            {
                ReportHomeAlarm(evt, $"轴未到位，禁止回零:{cardNum}-{axis}", stopOnAlarm);
                return false;
            }
            if (!Context.CardStore.TryGetAxis(cardNum, axis, out Axis axisInfo))
            {
                ReportHomeAlarm(evt, $"轴配置不存在:{cardNum}-{axis}", stopOnAlarm);
                return false;
            }
            if (axisInfo.PulseToMM <= 0
                || !double.TryParse(axisInfo.HomeSpeed, out double homeSpeed) || homeSpeed <= 0
                || axisInfo.AccMax <= 0 || axisInfo.DecMax <= 0)
            {
                ReportHomeAlarm(evt, $"轴回零参数无效:{cardNum}-{axis}", stopOnAlarm);
                return false;
            }
            if (!TryAcquireMotionResource(evt, cardNum, axis, out string resourceError))
            {
                ReportHomeAlarm(evt, resourceError, stopOnAlarm);
                return false;
            }

            int sfc = 10;
            Stopwatch homeStopwatch = Stopwatch.StartNew();
            using (evt.CancellationToken.Register(() =>
            {
                try
                {
                    Context.Motion.StopOneAxis(cardNum, axis, 0);
                }
                catch (Exception ex)
                {
                    Logger?.Log($"取消回零时停止轴失败:{cardNum}-{axis} {ex.Message}", LogLevel.Error);
                }
            }))
            {
                while (!evt.CancellationToken.IsCancellationRequested)
                {
                    if (homeStopwatch.ElapsedMilliseconds > 120000)
                    {
                        Context.Motion.StopOneAxis(cardNum, axis, 0);
                        ReportHomeAlarm(evt, $"轴回零超时:{cardNum}-{axis}", stopOnAlarm);
                        return false;
                    }
                    switch (sfc)
                    {
                        case 10:
                            using (Context.Motion.ValidateAxesForCommand(new[]
                            {
                                new AxisCommandRequest(cardNum, axis, AxisCommandKind.Home)
                            }))
                            {
                                Context.Motion.SetMovParam(cardNum, axis, 0, homeSpeed, axisInfo.AccMax,
                                    axisInfo.DecMax, 0, 0, axisInfo.PulseToMM);
                                Context.Motion.SettHomeParam(
                                    cardNum,
                                    axis,
                                    0,
                                    1,
                                    axisInfo.HomeMethod > 0 ? (ushort)axisInfo.HomeMethod : (ushort)0);
                                Context.Motion.StartHome(cardNum, axis);
                            }
                            if (!WaitDelay(20, evt.CancellationToken))
                            {
                                return false;
                            }
                            sfc = 20;
                            break;
                        case 20:
                            uint ioStatus = Context.Motion.GetAxisIoStatus(cardNum, axis);
                            if ((ioStatus & 1u) != 0 || (ioStatus & (1u << 3)) != 0)
                            {
                                Context.Motion.StopOneAxis(cardNum, axis, 1);
                                string signal = (ioStatus & 1u) != 0 ? "伺服报警" : "急停信号有效";
                                ReportHomeAlarm(evt,
                                    $"回零过程中{signal}，轴已急停:{cardNum}-{axis}", stopOnAlarm);
                                return false;
                            }
                            if (Context.Motion.GetInPos(cardNum, axis))
                            {
                                if (!WaitDelay(300, evt.CancellationToken))
                                {
                                    return false;
                                }
                                if (!Context.Motion.HomeStatus(cardNum, axis))
                                {
                                    ReportHomeAlarm(evt, "控制卡报告回零失败。", stopOnAlarm);
                                    return false;
                                }
                                Context.Motion.CleanPos(cardNum, axis);
                                return true;
                            }
                            if (!WaitDelay(20, evt.CancellationToken))
                            {
                                return false;
                            }
                            break;
                    }
                }
            }
            return false;
        }

        private bool WaitDelay(int milliSecond, CancellationToken token)
        {
            if (milliSecond <= 0)
            {
                return true;
            }
            if (token.IsCancellationRequested)
            {
                return false;
            }
            try
            {
                Task.Delay(milliSecond, token).GetAwaiter().GetResult();
                return !token.IsCancellationRequested;
            }
            catch (TaskCanceledException)
            {
                return false;
            }
        }

    }
}
