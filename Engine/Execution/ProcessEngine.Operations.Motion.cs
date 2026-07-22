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
                            Delay(5, evt);
                        }
                    }
                }
            return true;
        }

        private CoordinatedLinearMoveRequest BuildCoordinatedLinearMove(
            string operationName,
            DataStation station,
            IReadOnlyList<ushort> cards,
            IReadOnlyList<ushort> axes,
            IReadOnlyList<double> positions,
            IReadOnlyList<double> axisVelocities,
            IReadOnlyList<double> accelerationTimes,
            IReadOnlyList<double> decelerationTimes,
            IReadOnlyList<int> equivalents,
            ushort positionMode)
        {
            int count = axes?.Count ?? 0;
            if (station == null || cards == null || positions == null
                || axisVelocities == null || accelerationTimes == null || decelerationTimes == null
                || equivalents == null || cards.Count != count || positions.Count != count
                || axisVelocities.Count != count || accelerationTimes.Count != count
                || decelerationTimes.Count != count || equivalents.Count != count)
            {
                throw new InvalidOperationException($"{operationName}协调直线运动参数不完整");
            }
            if (count == 0)
            {
                return null;
            }
            ushort card = cards[0];
            if (cards.Any(item => item != card))
            {
                throw new InvalidOperationException($"{operationName}的参与轴必须位于同一张控制卡");
            }
            if (axes.Distinct().Count() != count)
            {
                throw new InvalidOperationException($"{operationName}的参与轴配置重复");
            }
            if (station.CoordinateSystem > 1)
            {
                throw new InvalidOperationException($"{operationName}坐标系无效:{station.CoordinateSystem}");
            }

            double pathLengthSquared = 0;
            var displacements = new double[count];
            for (int i = 0; i < count; i++)
            {
                if (equivalents[i] <= 0)
                {
                    throw new InvalidOperationException($"{operationName}轴{axes[i]}脉冲当量无效");
                }
                double displacement = positionMode == 0
                    ? positions[i]
                    : positions[i] - Context.Motion.GetAxisPos(card, axes[i]) / equivalents[i];
                if (double.IsNaN(displacement) || double.IsInfinity(displacement))
                {
                    throw new InvalidOperationException($"{operationName}轴{axes[i]}位移无效");
                }
                displacements[i] = displacement;
                pathLengthSquared += displacement * displacement;
            }
            double pathLength = Math.Sqrt(pathLengthSquared);
            if (pathLength <= 1e-12)
            {
                return null;
            }

            double vectorVelocity = double.MaxValue;
            for (int i = 0; i < count; i++)
            {
                double component = Math.Abs(displacements[i]);
                if (component <= 1e-12)
                {
                    continue;
                }
                double candidate = axisVelocities[i] * pathLength / component;
                vectorVelocity = Math.Min(vectorVelocity, candidate);
            }
            double accelerationTime = accelerationTimes.Max();
            double decelerationTime = decelerationTimes.Max();
            if (vectorVelocity <= 0 || vectorVelocity == double.MaxValue
                || double.IsNaN(vectorVelocity) || double.IsInfinity(vectorVelocity)
                || accelerationTime <= 0 || decelerationTime <= 0)
            {
                throw new InvalidOperationException($"{operationName}协调直线速度参数无效");
            }
            return new CoordinatedLinearMoveRequest
            {
                Card = card,
                CoordinateSystem = station.CoordinateSystem,
                Axes = axes.ToArray(),
                Positions = positions.ToArray(),
                PositionMode = positionMode,
                MaxVelocity = vectorVelocity,
                AccelerationTime = accelerationTime,
                DecelerationTime = decelerationTime
            };
        }

        private void WaitForCoordinatedLinear(ProcHandle evt, CoordinatedLinearMoveRequest request,
            double timeoutMilliseconds, string operationName)
        {
            if (request == null)
            {
                return;
            }
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (!evt.CancellationToken.IsCancellationRequested)
            {
                if (Context.Motion.IsCoordinatedLinearDone(request.Card, request.CoordinateSystem))
                {
                    return;
                }
                if (stopwatch.ElapsedMilliseconds > timeoutMilliseconds)
                {
                    Context.Motion.StopCoordinatedLinear(request.Card, request.CoordinateSystem, 0);
                    throw new TimeoutException($"{operationName}运动超时");
                }
                Delay(5, evt);
            }
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

                    if (Context.Motion == null || Context.CardStore == null)
                    {
                        MarkAlarm(evt, "运动控制未初始化");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }
                    List<double> Poses = posItems.GetAllValues();
                    List<bool> AxisDisableInfos = stationRunPos.GetAllValues();
                    if (Poses == null || Poses.Count < 6 || AxisDisableInfos == null || AxisDisableInfos.Count < 6
                        || station.dataAxis?.axisConfigs == null || station.dataAxis.axisConfigs.Count < 6)
                    {
                        MarkAlarm(evt, "工站轴或点位配置不完整");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }
                    List<double> TargetPos = new List<double>();
                    ushort[] commandCards = new ushort[6];
                    ushort[] commandAxes = new ushort[6];
                    double[] commandVel = new double[6];
                    double[] commandAcc = new double[6];
                    double[] commandDec = new double[6];
                    int[] commandEquiv = new int[6];
                    bool[] commandEnabled = new bool[6];
                    List<long> resources = new List<long>();
                    for (int i = 0; i < 6; i++)
                    {
                        if (stationRunPos.IsDisableAxis == "有禁用" && AxisDisableInfos[i])
                        {
                            continue;
                        }
                        AxisConfig axisConfig = station.dataAxis.axisConfigs[i];
                        if (axisConfig?.AxisName == "-1")
                        {
                            continue;
                        }
                        if (axisConfig?.axis == null || !ushort.TryParse(axisConfig.CardNum, out ushort cardNum))
                        {
                            MarkAlarm(evt, $"卡号或轴配置无效:{axisConfig?.CardNum}");
                            throw CreateAlarmException(evt, evt?.alarmMsg);
                        }
                        ushort axisNum = (ushort)axisConfig.axis.AxisNum;
                        if (!Context.CardStore.TryGetAxis(cardNum, axisNum, out Axis axisInfo) || axisInfo.PulseToMM <= 0)
                        {
                            MarkAlarm(evt, $"工站：{stationRunPos.Name} {cardNum}号卡{axisNum}号轴配置无效");
                            throw CreateAlarmException(evt, evt?.alarmMsg);
                        }
                        AxisMotionParameters runtimeParameters = Context.AxisMotionParameters.Get(cardNum, axisNum);
                        double velPercent = stationRunPos.ChangeVel == "改变速度"
                            ? (stationRunPos.Vel == 0 ? Context.ValueStore.GetValueByNameForProcess(stationRunPos.VelV, evt.procId).GetDValue() : stationRunPos.Vel)
                            : runtimeParameters.SpeedPercent;
                        double accPercent = stationRunPos.ChangeVel == "改变速度"
                            ? (stationRunPos.Acc == 0 ? Context.ValueStore.GetValueByNameForProcess(stationRunPos.AccV, evt.procId).GetDValue() : stationRunPos.Acc)
                            : runtimeParameters.AccelerationPercent;
                        double decPercent = stationRunPos.ChangeVel == "改变速度"
                            ? (stationRunPos.Dec == 0 ? Context.ValueStore.GetValueByNameForProcess(stationRunPos.DecV, evt.procId).GetDValue() : stationRunPos.Dec)
                            : runtimeParameters.DecelerationPercent;
                        if (velPercent <= 0 || velPercent > 100
                            || accPercent <= 0 || accPercent > 100
                            || decPercent <= 0 || decPercent > 100
                            || double.IsNaN(velPercent) || double.IsInfinity(velPercent)
                            || double.IsNaN(accPercent) || double.IsInfinity(accPercent)
                            || double.IsNaN(decPercent) || double.IsInfinity(decPercent)
                            || double.IsNaN(Poses[i]) || double.IsInfinity(Poses[i]))
                        {
                            MarkAlarm(evt, $"工站：{stationRunPos.Name} {cardNum}号卡{axisNum}号轴运动参数无效");
                            throw CreateAlarmException(evt, evt?.alarmMsg);
                        }
                        commandCards[i] = cardNum;
                        commandAxes[i] = axisNum;
                        commandVel[i] = axisInfo.SpeedMax * (velPercent / 100);
                        commandAcc[i] = axisInfo.AccMax / (accPercent / 100);
                        commandDec[i] = axisInfo.DecMax / (decPercent / 100);
                        if (commandVel[i] <= 0 || commandAcc[i] <= 0 || commandDec[i] <= 0
                            || double.IsNaN(commandVel[i]) || double.IsInfinity(commandVel[i])
                            || double.IsNaN(commandAcc[i]) || double.IsInfinity(commandAcc[i])
                            || double.IsNaN(commandDec[i]) || double.IsInfinity(commandDec[i]))
                        {
                            MarkAlarm(evt, $"工站：{stationRunPos.Name} {cardNum}号卡{axisNum}号轴速度配置无效");
                            throw CreateAlarmException(evt, evt?.alarmMsg);
                        }
                        commandEquiv[i] = axisInfo.PulseToMM;
                        commandEnabled[i] = true;
                        resources.Add(BuildMotionResourceKey(cardNum, axisNum));
                    }
                    if (!TryAcquireMotionResources(evt, resources, out string resourceError))
                    {
                        MarkAlarm(evt, resourceError);
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }
                    List<AxisCommandRequest> motionRequests = new List<AxisCommandRequest>();
                    var activeCards = new List<ushort>();
                    var activeAxes = new List<ushort>();
                    var activeTargets = new List<double>();
                    var activeVelocities = new List<double>();
                    var activeAccelerationTimes = new List<double>();
                    var activeDecelerationTimes = new List<double>();
                    var activeEquivalents = new List<int>();
                    for (int i = 0; i < 6; i++)
                    {
                        if (commandEnabled[i])
                        {
                            motionRequests.Add(new AxisCommandRequest(commandCards[i], commandAxes[i], AxisCommandKind.Motion));
                            activeCards.Add(commandCards[i]);
                            activeAxes.Add(commandAxes[i]);
                            activeTargets.Add(Poses[i]);
                            activeVelocities.Add(commandVel[i]);
                            activeAccelerationTimes.Add(commandAcc[i]);
                            activeDecelerationTimes.Add(commandDec[i]);
                            activeEquivalents.Add(commandEquiv[i]);
                        }
                    }
                    CoordinatedLinearMoveRequest coordinatedRequest = BuildCoordinatedLinearMove(
                        stationRunPos.Name, station, activeCards, activeAxes, activeTargets,
                        activeVelocities, activeAccelerationTimes, activeDecelerationTimes,
                        activeEquivalents, 1);
                    try
                    {
                        if (coordinatedRequest != null)
                        {
                            if (!TryAcquireCoordinateSystem(evt, coordinatedRequest.Card,
                                coordinatedRequest.CoordinateSystem, out string coordinateError))
                            {
                                throw new InvalidOperationException(coordinateError);
                            }
                            using (Context.Motion.ValidateAxesForCommand(motionRequests))
                            {
                                Context.Motion.MoveCoordinatedLinear(coordinatedRequest);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        if (coordinatedRequest != null)
                        {
                            try
                            {
                                Context.Motion.StopCoordinatedLinear(coordinatedRequest.Card,
                                    coordinatedRequest.CoordinateSystem, 0);
                            }
                            catch (Exception stopException)
                            {
                                throw new InvalidOperationException(
                                    $"协调直线运动启动失败且停止失败:{ex.Message}; {stopException.Message}", stopException);
                            }
                        }
                        throw;
                    }
                    if (!stationRunPos.ContinueWithoutWaiting)
                    {
                        List<ushort> cardNums = new List<ushort>();
                        List<ushort> axisNums = new List<ushort>();
                        for (int i = 0; i < 6; i++)
                        {
                            if (stationRunPos.IsDisableAxis == "有禁用")
                            {
                                if (AxisDisableInfos[i] == true)
                                    continue;
                            }
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
                                TargetPos.Add(Poses[i]);
                            }
                        }
                        double time;
                        if (stationRunPos.TimeoutMs > 0)
                            time = stationRunPos.TimeoutMs;
                        else
                        {

                            time = Context.ValueStore.GetValueByNameForProcess(stationRunPos.TimeoutVariableName, evt.procId).GetDValue();
                        }
                        if (time <= 0)
                        {
                            MarkAlarm(evt, $"{stationRunPos.Name}超时配置无效");
                            throw CreateAlarmException(evt, evt?.alarmMsg);
                        }

                        WaitForCoordinatedLinear(evt, coordinatedRequest, time, stationRunPos.Name);
                        if (evt.CancellationToken.IsCancellationRequested)
                        {
                            return true;
                        }
                        if (stationRunPos.CheckInPosition)
                        {
                            for (int i = 0; i < cardNums.Count; i++)
                            {
                                if (!Context.CardStore.TryGetAxis(cardNums[i], axisNums[i], out Axis axisInfo))
                                {
                                    MarkAlarm(evt, $"工站：{stationRunPos.Name} {cardNums[i]}号卡{axisNums[i]}号轴配置不存在");
                                    throw CreateAlarmException(evt, evt?.alarmMsg);
                                }
                                if (Math.Abs(Context.Motion.GetAxisPos(cardNums[i], axisNums[i]) / axisInfo.PulseToMM - TargetPos[i]) > 0.01)
                                {
                                    MarkAlarm(evt, $"工站：{stationRunPos.Name} {cardNums[i]}号卡{axisNums[i]}号轴运动未到位");
                                    throw CreateAlarmException(evt, evt?.alarmMsg);
                                }
                            }
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

            if (Context.Motion == null || Context.CardStore == null)
            {
                MarkAlarm(evt, "运动控制未初始化");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }

            List<double> targetPos = new List<double> { target.X, target.Y, target.Z, target.U, target.V, target.W };
            List<ushort> cardNums = new List<ushort>();
            List<ushort> axisNums = new List<ushort>();
            List<Axis> axes = new List<Axis>();
            List<int> stationAxisIndexes = new List<int>();
            List<double> velocities = new List<double>();
            List<double> accelerations = new List<double>();
            List<double> decelerations = new List<double>();
            List<long> resources = new List<long>();
            if (station.dataAxis?.axisConfigs == null || station.dataAxis.axisConfigs.Count < 6)
            {
                throw CreateAlarmException(evt, "工站轴配置不完整");
            }
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
                    if (!Context.CardStore.TryGetAxis(cardNum, axisNum, out Axis axisInfo)
                        || axisInfo.PulseToMM <= 0
                        || double.IsNaN(targetPos[i]) || double.IsInfinity(targetPos[i]))
                    {
                        MarkAlarm(evt, $"工站：{trayRunPos.Name} {cardNum}号卡{axisNum}号轴配置无效");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }
                    cardNums.Add(cardNum);
                    axisNums.Add(axisNum);
                    axes.Add(axisInfo);
                    stationAxisIndexes.Add(i);
                    AxisMotionParameters runtimeParameters = Context.AxisMotionParameters.Get(cardNum, axisNum);
                    double velocity = axisInfo.SpeedMax * (runtimeParameters.SpeedPercent / 100);
                    double acceleration = axisInfo.AccMax / (runtimeParameters.AccelerationPercent / 100);
                    double deceleration = axisInfo.DecMax / (runtimeParameters.DecelerationPercent / 100);
                    if (velocity <= 0 || acceleration <= 0 || deceleration <= 0
                        || double.IsNaN(velocity) || double.IsInfinity(velocity)
                        || double.IsNaN(acceleration) || double.IsInfinity(acceleration)
                        || double.IsNaN(deceleration) || double.IsInfinity(deceleration))
                    {
                        throw CreateAlarmException(evt,
                            $"工站：{trayRunPos.Name} {cardNum}号卡{axisNum}号轴速度配置无效");
                    }
                    velocities.Add(velocity);
                    accelerations.Add(acceleration);
                    decelerations.Add(deceleration);
                    resources.Add(BuildMotionResourceKey(cardNum, axisNum));
                }
            }
            if (!TryAcquireMotionResources(evt, resources, out string trayResourceError))
            {
                throw CreateAlarmException(evt, trayResourceError);
            }
            List<AxisCommandRequest> trayRequests = cardNums
                .Select((card, index) => new AxisCommandRequest(card, axisNums[index], AxisCommandKind.Motion))
                .ToList();
            var startedTrayAxes = new List<AxisCommandRequest>();
            try
            {
                using (Context.Motion.ValidateAxesForCommand(trayRequests))
                {
                    for (int i = 0; i < cardNums.Count; i++)
                    {
                        Axis axisInfo = axes[i];
                        Context.Motion.SetMovParam(cardNums[i], axisNums[i], 0, velocities[i], accelerations[i], decelerations[i],
                            0, 0, axisInfo.PulseToMM);
                    }
                    for (int i = 0; i < cardNums.Count; i++)
                    {
                        int stationAxisIndex = stationAxisIndexes[i];
                        Context.Motion.Mov(cardNums[i], axisNums[i], targetPos[stationAxisIndex], 1, false);
                        startedTrayAxes.Add(new AxisCommandRequest(cardNums[i], axisNums[i], AxisCommandKind.Motion));
                    }
                }
            }
            catch (Exception ex)
            {
                throw HandlePartialAxisStartFailure(startedTrayAxes, evt, ex);
            }

            if (!trayRunPos.ContinueWithoutWaiting)
            {
                Stopwatch stopwatch = Stopwatch.StartNew();
                const int timeout = 120000;
                bool isInPos = false;
                while (!evt.CancellationToken.IsCancellationRequested
                    && cardNums.Count != 0
                    )
                {
                    if (stopwatch.ElapsedMilliseconds > timeout)
                    {
                        MarkAlarm(evt, trayRunPos.Name + "运动超时");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }
                    for (int i = 0; i < cardNums.Count; i++)
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
                    if (isInPos)
                    {
                        break;
                    }
                    Delay(5, evt);
                }
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
                if (Context.Motion == null || Context.CardStore == null)
                {
                    throw CreateAlarmException(evt, "运动控制未初始化");
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
            else
            {
                DataPos refPos = station.ListDataPos.FirstOrDefault(sc => sc != null && sc.Name == modifyStationPos.RefPosName);
                if (refPos == null)
                {
                    throw CreateAlarmException(evt, $"参考点不存在:{modifyStationPos.RefPosName}");
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

            targetPos.X = targetValues[0];
            targetPos.Y = targetValues[1];
            targetPos.Z = targetValues[2];
            targetPos.U = targetValues[3];
            targetPos.V = targetValues[4];
            targetPos.W = targetValues[5];

            if (station.dicDataPos != null && !string.IsNullOrWhiteSpace(targetPos.Name))
            {
                station.dicDataPos[targetPos.Name] = targetPos;
            }

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
            if (getStationPos.SourceType == "当前位置")
            {
                if (Context.Motion == null || Context.CardStore == null)
                {
                    throw CreateAlarmException(evt, "运动控制未初始化");
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
                if (getStationPos.SourceType == "当前位置")
                {
                    if (available[0]) targetPos.X = values[0];
                    if (available[1]) targetPos.Y = values[1];
                    if (available[2]) targetPos.Z = values[2];
                    if (available[3]) targetPos.U = values[3];
                    if (available[4]) targetPos.V = values[4];
                    if (available[5]) targetPos.W = values[5];
                }
                else
                {
                    targetPos.X = values[0];
                    targetPos.Y = values[1];
                    targetPos.Z = values[2];
                    targetPos.U = values[3];
                    targetPos.V = values[4];
                    targetPos.W = values[5];
                }
                if (targetPos.Index >= 0 && targetPos.Index < station.ListDataPos.Count)
                {
                    station.ListDataPos[targetPos.Index] = targetPos;
                }
                if (station.dicDataPos != null && !string.IsNullOrWhiteSpace(targetPos.Name))
                {
                    station.dicDataPos[targetPos.Name] = targetPos;
                }
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
                if (Context.Motion == null || Context.CardStore == null || Context.ValueStore == null)
                {
                    throw CreateAlarmException(evt, "运动控制或变量库未初始化");
                }
                List<double> TargetPos = stationRunRel.GetAllValues();
                List<string> TargetPosV = stationRunRel.GetAllValuesV();
                List<ushort> cardNums = new List<ushort>();
                List<ushort> axisNums = new List<ushort>();
                List<Axis> axes = new List<Axis>();
                List<double> distances = new List<double>();
                List<double> expectedTargets = new List<double>();
                List<double> velocities = new List<double>();
                List<double> accelerations = new List<double>();
                List<double> decelerations = new List<double>();
                List<long> resources = new List<long>();
                if (TargetPos == null || TargetPos.Count < 6 || TargetPosV == null || TargetPosV.Count < 6
                    || station.dataAxis?.axisConfigs == null || station.dataAxis.axisConfigs.Count < 6)
                {
                    throw CreateAlarmException(evt, "工站相对运动配置不完整");
                }
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
                        if (!Context.CardStore.TryGetAxis(cardNum, axisNum, out Axis axisInfo) || axisInfo.PulseToMM <= 0)
                        {
                            MarkAlarm(evt, $"工站：{stationRunRel.Name} {cardNum}号卡{axisNum}号轴配置不存在");
                            throw CreateAlarmException(evt, evt?.alarmMsg);
                        }
                        AxisMotionParameters runtimeParameters = Context.AxisMotionParameters.Get(cardNum, axisNum);
                        double velPercent = stationRunRel.ChangeVel == "改变速度"
                            ? (stationRunRel.Vel == 0 ? Context.ValueStore.GetValueByNameForProcess(stationRunRel.VelV, evt.procId).GetDValue() : stationRunRel.Vel)
                            : runtimeParameters.SpeedPercent;
                        double accPercent = stationRunRel.ChangeVel == "改变速度"
                            ? (stationRunRel.Acc == 0 ? Context.ValueStore.GetValueByNameForProcess(stationRunRel.AccV, evt.procId).GetDValue() : stationRunRel.Acc)
                            : runtimeParameters.AccelerationPercent;
                        double decPercent = stationRunRel.ChangeVel == "改变速度"
                            ? (stationRunRel.Dec == 0 ? Context.ValueStore.GetValueByNameForProcess(stationRunRel.DecV, evt.procId).GetDValue() : stationRunRel.Dec)
                            : runtimeParameters.DecelerationPercent;
                        double distance = TargetPos[i] == 0
                            ? Context.ValueStore.GetValueByNameForProcess(TargetPosV[i], evt.procId).GetDValue()
                            : TargetPos[i];
                        if (velPercent <= 0 || velPercent > 100
                            || accPercent <= 0 || accPercent > 100
                            || decPercent <= 0 || decPercent > 100
                            || double.IsNaN(velPercent) || double.IsInfinity(velPercent)
                            || double.IsNaN(accPercent) || double.IsInfinity(accPercent)
                            || double.IsNaN(decPercent) || double.IsInfinity(decPercent)
                            || double.IsNaN(distance) || double.IsInfinity(distance))
                        {
                            throw CreateAlarmException(evt, $"工站：{stationRunRel.Name} {cardNum}号卡{axisNum}号轴运动参数无效");
                        }
                        cardNums.Add(cardNum);
                        axisNums.Add(axisNum);
                        axes.Add(axisInfo);
                        distances.Add(distance);
                        expectedTargets.Add(Context.Motion.GetAxisPos(cardNum, axisNum) / axisInfo.PulseToMM + distance);
                        velocities.Add(axisInfo.SpeedMax * (velPercent / 100));
                        accelerations.Add(axisInfo.AccMax / (accPercent / 100));
                        decelerations.Add(axisInfo.DecMax / (decPercent / 100));
                        if (velocities[velocities.Count - 1] <= 0 || accelerations[accelerations.Count - 1] <= 0
                            || decelerations[decelerations.Count - 1] <= 0
                            || double.IsNaN(velocities[velocities.Count - 1]) || double.IsInfinity(velocities[velocities.Count - 1])
                            || double.IsNaN(accelerations[accelerations.Count - 1]) || double.IsInfinity(accelerations[accelerations.Count - 1])
                            || double.IsNaN(decelerations[decelerations.Count - 1]) || double.IsInfinity(decelerations[decelerations.Count - 1]))
                        {
                            throw CreateAlarmException(evt,
                                $"工站：{stationRunRel.Name} {cardNum}号卡{axisNum}号轴速度配置无效");
                        }
                        resources.Add(BuildMotionResourceKey(cardNum, axisNum));
                    }
                }
                if (!TryAcquireMotionResources(evt, resources, out string relativeResourceError))
                {
                    throw CreateAlarmException(evt, relativeResourceError);
                }
                List<AxisCommandRequest> relativeRequests = cardNums
                    .Select((card, index) => new AxisCommandRequest(card, axisNums[index], AxisCommandKind.Motion))
                    .ToList();
                CoordinatedLinearMoveRequest coordinatedRequest = BuildCoordinatedLinearMove(
                    stationRunRel.Name, station, cardNums, axisNums, distances, velocities,
                    accelerations, decelerations, axes.Select(item => item.PulseToMM).ToList(), 0);
                try
                {
                    if (coordinatedRequest != null)
                    {
                        if (!TryAcquireCoordinateSystem(evt, coordinatedRequest.Card,
                            coordinatedRequest.CoordinateSystem, out string coordinateError))
                        {
                            throw new InvalidOperationException(coordinateError);
                        }
                        using (Context.Motion.ValidateAxesForCommand(relativeRequests))
                        {
                            Context.Motion.MoveCoordinatedLinear(coordinatedRequest);
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (coordinatedRequest != null)
                    {
                        try
                        {
                            Context.Motion.StopCoordinatedLinear(coordinatedRequest.Card,
                                coordinatedRequest.CoordinateSystem, 0);
                        }
                        catch (Exception stopException)
                        {
                            throw new InvalidOperationException(
                                $"协调直线运动启动失败且停止失败:{ex.Message}; {stopException.Message}", stopException);
                        }
                    }
                    throw;
                }
                if (!stationRunRel.ContinueWithoutWaiting)
                {

                    double time;
                    if (stationRunRel.TimeoutMs > 0)
                        time = stationRunRel.TimeoutMs;
                    else
                    {

                        time = Context.ValueStore.GetValueByNameForProcess(stationRunRel.TimeoutVariableName, evt.procId).GetDValue();
                    }
                    if (time <= 0)
                    {
                        MarkAlarm(evt, $"{stationRunRel.Name}超时配置无效");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }
                    WaitForCoordinatedLinear(evt, coordinatedRequest, time, stationRunRel.Name);
                    if (evt.CancellationToken.IsCancellationRequested)
                    {
                        return true;
                    }
                    if (stationRunRel.CheckInPosition)
                    {
                        for (int i = 0; i < cardNums.Count; i++)
                        {
                            if (!Context.CardStore.TryGetAxis(cardNums[i], axisNums[i], out Axis axisInfo))
                            {
                                MarkAlarm(evt, $"工站：{stationRunRel.Name} {cardNums[i]}号卡{axisNums[i]}号轴配置不存在");
                                throw CreateAlarmException(evt, evt?.alarmMsg);
                            }
                            if (Math.Abs(Context.Motion.GetAxisPos(cardNums[i], axisNums[i]) / axisInfo.PulseToMM - expectedTargets[i]) > 0.01)
                            {
                                MarkAlarm(evt, $"工站：{stationRunRel.Name} {cardNums[i]}号卡{axisNums[i]}号轴运动未到位");
                                throw CreateAlarmException(evt, evt?.alarmMsg);
                            }
                        }
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
                Delay(5, evt);
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

            ushort dir = 0;
            int sfc = 10;
            if (axisInfo.HomeDirection == "正向")
            {
                dir = 1;
            }
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
                                Context.Motion.SettHomeParam(cardNum, axis, dir, 1, 1);
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
