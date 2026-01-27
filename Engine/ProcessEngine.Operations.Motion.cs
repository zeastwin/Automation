using System;
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
using System.Windows.Forms;
using Automation.MotionControl;
using static Automation.FrmProc;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Button;
using static Automation.FrmCard;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.TaskbarClock;
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
            station.SetState(DataStation.Status.Run);
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
                                HomeStationBySeq(stationIndex, evt, homeRun.isUnWait);
                            else
                                HomeStationByAll(stationIndex, evt, homeRun.isUnWait);
                        }
                        catch (OperationCanceledException)
                        {
                        }
                        catch (Exception ex)
                        {
                            ReportHomeAlarm(evt, ex.Message, homeRun.isUnWait);
                        }
                    }, evt.CancellationToken);
                    evt.RunningTasks.Add(task);
                    Delay(500, evt);
                    if (!homeRun.isUnWait)
                    {
                        Stopwatch stopwatch = Stopwatch.StartNew();
                        bool isInPos = false;
                        while (evt.CancellationToken.IsCancellationRequested == false
                            && !evt.CancellationToken.IsCancellationRequested
                            && station.GetState() == DataStation.Status.Run)
                        {
                            if (stopwatch.ElapsedMilliseconds > 120000)
                            {
                                MarkAlarm(evt, homeRun.Name + "运动超时");
                                station.SetState(DataStation.Status.NotReady);
                                throw CreateAlarmException(evt, evt?.alarmMsg);
                            }
                            for (int i = 0; i < 6; i++)
                            {
                                if (station.dataAxis.axisConfigs[i].AxisName == "-1")
                                    continue;
                                if (Context.CardStore.TryGetAxis(int.Parse(station.dataAxis.axisConfigs[i].CardNum), i, out Axis axisInfo) && axisInfo.State == Axis.Status.Ready)
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
            station.SetState(DataStation.Status.Run);
            DataPos posItems;
            if (stationRunPos.PosIndex != -1)
            {
                posItems = station.ListDataPos[stationRunPos.PosIndex];
            }
            else
            {
                posItems = station.ListDataPos.FirstOrDefault(sc => sc.Name == stationRunPos.PosName);
            }
            if (posItems == null)
            {
                MarkAlarm(evt, $"工站点位不存在:{stationRunPos.PosName}");
                station.SetState(DataStation.Status.NotReady);
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }

                    List<double> Poses = posItems.GetAllValues();
                    List<bool> AxisDisableInfos = stationRunPos.GetAllValues();
                    List<double> TargetPos = new List<double>();
                    double Vel = 0;
                    double Acc = 0;
                    double Dec = 0;
                    for (int i = 0; i < 6; i++)
                    {
                        if (stationRunPos.IsDisableAxis == "有禁用")
                        {
                            if (AxisDisableInfos[i] == true)
                                continue;
                        }
                        if (station.dataAxis.axisConfigs[i].AxisName != "-1")
                        {
                            ushort cardNum = ushort.Parse(station.dataAxis.axisConfigs[i].CardNum);
                            ushort axisNum = (ushort)station.dataAxis.axisConfigs[i].axis.AxisNum;
                            if (!Context.CardStore.TryGetAxis(cardNum, axisNum, out Axis axisInfo))
                            {
                                MarkAlarm(evt, $"工站：{stationRunPos.Name} {cardNum}号卡{axisNum}号轴配置不存在");
                                station.SetState(DataStation.Status.NotReady);
                                throw CreateAlarmException(evt, evt?.alarmMsg);
                            }
                            if (stationRunPos.ChangeVel == "改变速度")
                            {
                                double VelTemp = 0;
                                double AccTemp = 0;
                                double DecTemp = 0;

                                VelTemp = stationRunPos.Vel == 0 ? Context.ValueStore.GetValueByName(stationRunPos.VelV).GetDValue() : stationRunPos.Vel;
                                AccTemp = stationRunPos.Acc == 0 ? Context.ValueStore.GetValueByName(stationRunPos.AccV).GetDValue() : stationRunPos.Acc;
                                DecTemp = stationRunPos.Dec == 0 ? Context.ValueStore.GetValueByName(stationRunPos.DecV).GetDValue() : stationRunPos.Dec;


                                Vel = axisInfo.SpeedMax * (VelTemp / 100);
                                Acc = axisInfo.AccMax / (AccTemp / 100);
                                Dec = axisInfo.DecMax / (DecTemp / 100);
                            }
                            else
                            {
                                Vel = axisInfo.SpeedMax * (axisInfo.SpeedRun / 100);
                                Acc = axisInfo.AccMax / (axisInfo.AccRun / 100);
                                Dec = axisInfo.DecMax / (axisInfo.DecRun / 100);
                            }
                            Context.Motion.SetMovParam(cardNum, axisNum, 0, Vel, Acc, Dec, 0, 0, axisInfo.PulseToMM);

                            Context.Motion.Mov(ushort.Parse(station.dataAxis.axisConfigs[i].CardNum), (ushort)station.dataAxis.axisConfigs[i].axis.AxisNum, Poses[i], 1, false);
                        }
                    }
                    if (!stationRunPos.isUnWait)
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
                                ushort cardNum = ushort.Parse(station.dataAxis.axisConfigs[i].CardNum);
                                ushort axisNum = (ushort)station.dataAxis.axisConfigs[i].axis.AxisNum;
                                cardNums.Add(cardNum);
                                axisNums.Add(axisNum);
                                TargetPos.Add(Poses[i]);
                            }
                        }
                        Stopwatch stopwatch = Stopwatch.StartNew();
                        bool isInPos = false;
                        double time;
                        if (stationRunPos.timeOut > 0)
                            time = stationRunPos.timeOut;
                        else
                        {

                            time = Context.ValueStore.GetValueByName(stationRunPos.timeOutV).GetDValue();
                        }
                        if (time <= 0)
                        {
                            MarkAlarm(evt, $"{stationRunPos.Name}超时配置无效");
                            station.SetState(DataStation.Status.NotReady);
                            throw CreateAlarmException(evt, evt?.alarmMsg);
                        }

                        while (evt.CancellationToken.IsCancellationRequested == false
                            && !evt.CancellationToken.IsCancellationRequested
                            && cardNums.Count != 0
                            && station.GetState() == DataStation.Status.Run)
                        {
                            if (stopwatch.ElapsedMilliseconds > time)
                            {
                                MarkAlarm(evt, stationRunPos.Name + "运动超时");
                                station.SetState(DataStation.Status.NotReady);
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
                                break;
                            Delay(5, evt);
                        }
                        if (stationRunPos.isCheckInPos)
                        {
                            for (int i = 0; i < cardNums.Count; i++)
                            {
                                if (!Context.CardStore.TryGetAxis(cardNums[i], axisNums[i], out Axis axisInfo))
                                {
                                    MarkAlarm(evt, $"工站：{stationRunPos.Name} {cardNums[i]}号卡{axisNums[i]}号轴配置不存在");
                                    throw CreateAlarmException(evt, evt?.alarmMsg);
                                }
                                if (((Context.Motion.GetAxisPos(cardNums[i], axisNums[i]) / axisInfo.PulseToMM - TargetPos[i])) > 0.01)
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
                if (!trayIdRef.TryResolveValue(valueStore, "料盘号", out DicValue trayIdValue, out string trayIdResolveError))
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
                if (!trayPosRef.TryResolveValue(valueStore, "料盘位置", out DicValue trayPosValue, out string trayPosResolveError))
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

            station.SetState(DataStation.Status.Run);
            List<double> targetPos = new List<double> { target.X, target.Y, target.Z, target.U, target.V, target.W };
            List<ushort> cardNums = new List<ushort>();
            List<ushort> axisNums = new List<ushort>();

            for (int i = 0; i < 6; i++)
            {
                if (station.dataAxis.axisConfigs[i].AxisName != "-1")
                {
                    ushort cardNum = ushort.Parse(station.dataAxis.axisConfigs[i].CardNum);
                    ushort axisNum = (ushort)station.dataAxis.axisConfigs[i].axis.AxisNum;
                    if (!Context.CardStore.TryGetAxis(cardNum, axisNum, out Axis axisInfo))
                    {
                        MarkAlarm(evt, $"工站：{trayRunPos.Name} {cardNum}号卡{axisNum}号轴配置不存在");
                        station.SetState(DataStation.Status.NotReady);
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }
                    double vel = axisInfo.SpeedMax * (axisInfo.SpeedRun / 100);
                    double acc = axisInfo.AccMax / (axisInfo.AccRun / 100);
                    double dec = axisInfo.DecMax / (axisInfo.DecRun / 100);
                    Context.Motion.SetMovParam(cardNum, axisNum, 0, vel, acc, dec, 0, 0, axisInfo.PulseToMM);
                    Context.Motion.Mov(cardNum, axisNum, targetPos[i], 1, false);
                    cardNums.Add(cardNum);
                    axisNums.Add(axisNum);
                }
            }

            if (!trayRunPos.isUnWait)
            {
                Stopwatch stopwatch = Stopwatch.StartNew();
                const int timeout = 120000;
                bool isInPos = false;
                while (evt.CancellationToken.IsCancellationRequested == false
                    && !evt.CancellationToken.IsCancellationRequested
                    && cardNums.Count != 0
                    && station.GetState() == DataStation.Status.Run)
                {
                    if (stopwatch.ElapsedMilliseconds > timeout)
                    {
                        MarkAlarm(evt, trayRunPos.Name + "运动超时");
                        station.SetState(DataStation.Status.NotReady);
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
                string source = evt == null ? null : $"{evt.procNum}-{evt.stepNum}-{evt.opsNum}";

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
                    if (!valueRef.TryResolveValue(valueStore, label, out DicValue valueItem, out string resolveError))
                    {
                        throw CreateAlarmException(evt, resolveError);
                    }
                    if (!valueStore.setValueByIndex(valueItem.Index, axisValue.ToString(), source))
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
            station.SetState(DataStation.Status.Run);
                double Vel = 0;
                double Acc = 0;
                double Dec = 0;
                List<double> TargetPos = new List<double>();
                List<string> TargetPosV = new List<string>();
                List<ushort> cardNums = new List<ushort>();
                List<ushort> axisNums = new List<ushort>();

                TargetPos = stationRunRel.GetAllValues();
                TargetPosV = stationRunRel.GetAllValuesV();

                for (int i = 0; i < 6; i++)
                {
                    if (station.dataAxis.axisConfigs[i].AxisName != "-1")
                    {
                        ushort cardNum = ushort.Parse(station.dataAxis.axisConfigs[i].CardNum);
                        ushort axisNum = (ushort)station.dataAxis.axisConfigs[i].axis.AxisNum;
                        cardNums.Add(cardNum);
                        axisNums.Add(axisNum);
                        if (!Context.CardStore.TryGetAxis(cardNum, axisNum, out Axis axisInfo))
                        {
                            MarkAlarm(evt, $"工站：{stationRunRel.Name} {cardNum}号卡{axisNum}号轴配置不存在");
                            station.SetState(DataStation.Status.NotReady);
                            throw CreateAlarmException(evt, evt?.alarmMsg);
                        }
                        if (stationRunRel.ChangeVel == "改变速度")
                        {
                            double VelTemp = 0;
                            double AccTemp = 0;
                            double DecTemp = 0;

                            VelTemp = stationRunRel.Vel == 0 ? Context.ValueStore.GetValueByName(stationRunRel.VelV).GetDValue() : stationRunRel.Vel;
                            AccTemp = stationRunRel.Acc == 0 ? Context.ValueStore.GetValueByName(stationRunRel.AccV).GetDValue() : stationRunRel.Acc;
                            DecTemp = stationRunRel.Dec == 0 ? Context.ValueStore.GetValueByName(stationRunRel.DecV).GetDValue() : stationRunRel.Dec;


                            Vel = axisInfo.SpeedMax * (VelTemp / 100);
                            Acc = axisInfo.AccMax / (AccTemp / 100);
                            Dec = axisInfo.DecMax / (DecTemp / 100);

                        }
                        else
                        {
                            Vel = axisInfo.SpeedMax * (axisInfo.SpeedRun / 100);
                            Acc = axisInfo.AccMax / (axisInfo.AccRun / 100);
                            Dec = axisInfo.DecMax / (axisInfo.DecRun / 100);
                        }
                        Context.Motion.SetMovParam(cardNum, axisNum, 0, Vel, Acc, Dec, 0, 0, axisInfo.PulseToMM);

                        double DistanceTemp = 0;
                        DistanceTemp = TargetPos[i] == 0 ? Context.ValueStore.GetValueByName(TargetPosV[i]).GetDValue() : TargetPos[i];

                        Context.Motion.Mov(ushort.Parse(station.dataAxis.axisConfigs[i].CardNum), (ushort)station.dataAxis.axisConfigs[i].axis.AxisNum, DistanceTemp, 0, false);
                    }
                }
                if (!stationRunRel.isUnWait)
                {

                    Stopwatch stopwatch = Stopwatch.StartNew();
                    bool isInPos = false;
                    double time;
                    if (stationRunRel.timeOut > 0)
                        time = stationRunRel.timeOut;
                    else
                    {

                        time = Context.ValueStore.GetValueByName(stationRunRel.timeOutV).GetDValue();
                    }
                    if (time <= 0)
                    {
                        MarkAlarm(evt, $"{stationRunRel.Name}超时配置无效");
                        station.SetState(DataStation.Status.NotReady);
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }
                    while (evt.CancellationToken.IsCancellationRequested == false
                        && !evt.CancellationToken.IsCancellationRequested
                        && station.GetState() == DataStation.Status.Run)
                    {
                        if (stopwatch.ElapsedMilliseconds > time)
                        {
                            MarkAlarm(evt, stationRunRel.Name + "运动超时");
                            station.SetState(DataStation.Status.NotReady);
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
                            break;
                        Delay(5, evt);
                    }
                    if (stationRunRel.isCheckInPos)
                    {
                        for (int i = 0; i < cardNums.Count; i++)
                        {
                            if (!Context.CardStore.TryGetAxis(cardNums[i], axisNums[i], out Axis axisInfo))
                            {
                                MarkAlarm(evt, $"工站：{stationRunRel.Name} {cardNums[i]}号卡{axisNums[i]}号轴配置不存在");
                                throw CreateAlarmException(evt, evt?.alarmMsg);
                            }
                            if (((Context.Motion.GetAxisPos(cardNums[i], axisNums[i]) / axisInfo.PulseToMM - TargetPos[i])) > 0.01)
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

                Vel = setStationVel.Vel == 0 ? Context.ValueStore.GetValueByName(setStationVel.VelV).GetDValue() : setStationVel.Vel;
                Acc = setStationVel.Acc == 0 ? Context.ValueStore.GetValueByName(setStationVel.AccV).GetDValue() : setStationVel.Acc;
                Dec = setStationVel.Dec == 0 ? Context.ValueStore.GetValueByName(setStationVel.DecV).GetDValue() : setStationVel.Dec;

                if (setStationVel.SetAxisObj == "工站")
                {
                    for (int i = 0; i < 6; i++)
                    {
                        if (station.dataAxis.axisConfigs[i].AxisName != "-1")
                        {
                            ushort cardNum = ushort.Parse(station.dataAxis.axisConfigs[i].CardNum);
                            ushort axisNum = (ushort)station.dataAxis.axisConfigs[i].axis.AxisNum;

                            if (!Context.CardStore.TryGetAxis(cardNum, axisNum, out Axis axisInfo))
                            {
                                MarkAlarm(evt, $"工站：{setStationVel.StationName} {cardNum}号卡{axisNum}号轴配置不存在");
                                throw CreateAlarmException(evt, evt?.alarmMsg);
                            }
                            axisInfo.SpeedRun = Vel;
                            axisInfo.AccRun = Acc;
                            axisInfo.DecRun = Dec;
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
                    int cardNum = int.Parse(axisInfo.CardNum);
                    int axisNum = axisInfo.axis.AxisNum;
                    if (!Context.CardStore.TryGetAxis(cardNum, axisNum, out Axis axisConfig))
                    {
                        MarkAlarm(evt, $"工站：{setStationVel.StationName} {cardNum}号卡{axisNum}号轴配置不存在");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }
                    axisConfig.SpeedRun = Vel;
                    axisConfig.AccRun = Acc;
                    axisConfig.DecRun = Dec;
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
            if (stationStop.isAllStop)
            {
                StopStation(station);
            }
            else
            {
                List<bool> AxisParams = stationStop.GetAllValues();
                for (int i = 0; i < 6; i++)
                {
                    if (AxisParams[i] == true)
                    {
                        int cardNum = int.Parse(station.dataAxis.axisConfigs[i].CardNum);
                        int axisNum = station.dataAxis.axisConfigs[i].axis.AxisNum;
                        StopAxis(cardNum, axisNum);
                    }
                }
            }
            return true;
        }
        public bool RunWaitStationStop(ProcHandle evt, WaitStationStop waitStationStop)
        {
            DataStation station;
            if (waitStationStop.StationIndex != -1)
            {
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
            station.SetState(DataStation.Status.Run);
            List<ushort> cardNums = new List<ushort>();
            List<ushort> axisNums = new List<ushort>();
            for (int i = 0; i < 6; i++)
            {
                if (station.dataAxis.axisConfigs[i].AxisName != "-1")
                {
                    ushort cardNum = ushort.Parse(station.dataAxis.axisConfigs[i].CardNum);
                    ushort axisNum = (ushort)station.dataAxis.axisConfigs[i].axis.AxisNum;
                    cardNums.Add(cardNum);
                    axisNums.Add(axisNum);
                }
            }
            Stopwatch stopwatch = Stopwatch.StartNew();
            double time;
            if (waitStationStop.timeOut > 0)
                time = waitStationStop.timeOut;
            else
            {

                time = Context.ValueStore.GetValueByName(waitStationStop.timeOutV).GetDValue();
            }
            if (time <= 0)
            {
                MarkAlarm(evt, $"{waitStationStop.Name}超时配置无效");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            while (evt.CancellationToken.IsCancellationRequested == false
                && !evt.CancellationToken.IsCancellationRequested
                && station.GetState() == DataStation.Status.Run)
            {
                bool isInPos = false;

                if (stopwatch.ElapsedMilliseconds > time)
                {
                    MarkAlarm(evt, waitStationStop.Name + "等待超时");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                for (int i = 0; i < cardNums.Count; i++)
                {
                    if (waitStationStop.isWaitHome)
                    {
                        if (!Context.CardStore.TryGetAxis(cardNums[i], axisNums[i], out Axis axisInfo))
                        {
                            MarkAlarm(evt, $"工站：{waitStationStop.Name} {cardNums[i]}号卡{axisNums[i]}号轴配置不存在");
                            throw CreateAlarmException(evt, evt?.alarmMsg);
                        }
                        if (Context.Motion.HomeStatus(cardNums[i], axisNums[i]) && axisInfo.GetState() == Axis.Status.Ready)
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

        private void StopStation(DataStation station)
        {
            if (station == null || Context.Motion == null)
            {
                return;
            }
            for (int i = 0; i < 6; i++)
            {
                if (station.dataAxis.axisConfigs[i].AxisName != "-1")
                {
                    station.SetState(DataStation.Status.Ready);
                    station.dataAxis.axisConfigs[i].axis.SetState(Axis.Status.Ready);
                    Context.Motion.StopOneAxis(ushort.Parse(station.dataAxis.axisConfigs[i].CardNum),
                        (ushort)station.dataAxis.axisConfigs[i].axis.AxisNum,
                        0);
                }
            }
        }

        private void StopAxis(int card, int axis)
        {
            if (Context.CardStore != null && Context.CardStore.TryGetAxis(card, axis, out Axis axisInfo))
            {
                axisInfo.SetState(Axis.Status.Ready);
            }
            Context.Motion?.StopOneAxis((ushort)card, (ushort)axis, 0);
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
                        HomeSingleAxis(ushort.Parse(item.CardNum), (ushort)item.axis.AxisNum, evt, stopOnAlarm);
                        if (Context.CardStore == null || !Context.CardStore.TryGetAxis(int.Parse(item.CardNum), i, out Axis axisInfo))
                        {
                            ReportHomeAlarm(evt, $"卡{item.CardNum}轴{i}配置不存在，工站回零动作终止。", stopOnAlarm);
                            return;
                        }

                        if (axisInfo.State == Axis.Status.NotReady)
                        {
                            ReportHomeAlarm(evt, $"卡{item.CardNum}轴{i}回零失败,工站回零动作终止。", stopOnAlarm);
                            return;
                        }
                        break;
                    }
                }
            }
            for (int j = 0; j < station.dataAxis.axisConfigs.Count; j++)
            {
                ushort index = (ushort)j;
                if (station.dataAxis.axisConfigs[j].AxisName != "-1"
                    && Context.Motion != null
                    && !Context.Motion.HomeStatus(ushort.Parse(station.dataAxis.axisConfigs[index].CardNum),
                        (ushort)station.dataAxis.axisConfigs[index].axis.AxisNum))
                {
                    Task task = Task.Run(() =>
                    {
                        if (evt.CancellationToken.IsCancellationRequested)
                        {
                            return;
                        }
                        HomeSingleAxis(ushort.Parse(station.dataAxis.axisConfigs[index].CardNum),
                            (ushort)station.dataAxis.axisConfigs[index].axis.AxisNum, evt, stopOnAlarm);
                    }, evt.CancellationToken);
                    evt.RunningTasks.Add(task);
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
                        HomeSingleAxis(ushort.Parse(station.dataAxis.axisConfigs[index].CardNum),
                            (ushort)station.dataAxis.axisConfigs[index].axis.AxisNum, evt, stopOnAlarm);
                    }, evt.CancellationToken);
                    evt.RunningTasks.Add(task);
                }
            }
        }

        private void HomeSingleAxis(ushort cardNum, ushort axis, ProcHandle evt, bool stopOnAlarm)
        {
            if (Context.Motion == null || Context.CardStore == null)
            {
                ReportHomeAlarm(evt, "运动控制未初始化", stopOnAlarm);
                return;
            }
            if (evt.CancellationToken.IsCancellationRequested)
            {
                return;
            }
            if (!Context.Motion.GetInPos(cardNum, axis))
            {
                ReportHomeAlarm(evt, $"轴未到位，禁止回零:{cardNum}-{axis}", stopOnAlarm);
                return;
            }
            ushort dir = 0;
            if (!Context.CardStore.TryGetAxis(cardNum, axis, out Axis axisInfo))
            {
                ReportHomeAlarm(evt, $"轴配置不存在:{cardNum}-{axis}", stopOnAlarm);
                return;
            }
            axisInfo.State = Axis.Status.Run;
            int sfc = 1;
            if (axisInfo.HomeType == "从当前位回零")
            {
                sfc = 10;
            }
            int IOindex = 3;
            if (axisInfo.HomeType == "从正限位回零")
            {
                dir = 1;
                IOindex = 2;
            }
            int IOindexTemp = IOindex == 2 ? 3 : 2;

            while (axisInfo.State == Axis.Status.Run
                && !evt.CancellationToken.IsCancellationRequested)
            {
                switch (sfc)
                {
                    case 1:
                        Context.Motion.SetMovParam(cardNum, axis, 0, double.Parse(axisInfo.LimitSpeed), axisInfo.AccMax,
                            axisInfo.DecMax, 0, 0, axisInfo.PulseToMM);
                        Context.Motion.Jog(cardNum, axis, dir);
                        if (!WaitDelay(20, evt.CancellationToken))
                        {
                            return;
                        }
                        sfc = 2;
                        break;
                    case 2:
                        if (GetAxisStateBit(cardNum, axis, IOindex))
                        {
                            sfc = 10;
                        }
                        if (GetAxisStateBit(cardNum, axis, IOindexTemp))
                        {
                            if (!WaitDelay(1000, evt.CancellationToken))
                            {
                                return;
                            }
                            if (GetAxisStateBit(cardNum, axis, IOindexTemp))
                            {
                                ReportHomeAlarm(evt, "限位方向错误，回零失败。", stopOnAlarm);
                                axisInfo.State = Axis.Status.NotReady;
                                return;
                            }

                        }
                        if (!WaitDelay(20, evt.CancellationToken))
                        {
                            return;
                        }
                        break;
                    case 10:
                        Context.Motion.SetMovParam(cardNum, axis, 0, double.Parse(axisInfo.HomeSpeed), axisInfo.AccMax,
                            axisInfo.DecMax, 0, 0, axisInfo.PulseToMM);
                        if (axisInfo.HomeType != "从当前位回零")
                        {
                            Context.Motion.SettHomeParam(cardNum, axis, dir, 1, 1);
                        }
                        Context.Motion.StartHome(cardNum, axis);
                        if (!WaitDelay(20, evt.CancellationToken))
                        {
                            return;
                        }
                        sfc = 20;
                        break;
                    case 20:
                        if (Context.Motion.GetInPos(cardNum, axis))
                        {
                            if (!WaitDelay(300, evt.CancellationToken))
                            {
                                return;
                            }
                            if (Context.Motion.HomeStatus(cardNum, axis) == true)
                            {
                                Context.Motion.CleanPos(cardNum, axis);
                                axisInfo.State = 0;
                                sfc = 0;
                            }
                            else
                            {
                                ReportHomeAlarm(evt, "限位方向错误，回零失败。", stopOnAlarm);
                                axisInfo.State = Axis.Status.NotReady;
                                sfc = 0;
                                return;
                            }

                        }
                        if (!WaitDelay(20, evt.CancellationToken))
                        {
                            return;
                        }
                        break;

                }
            }
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

        private bool GetAxisStateBit(ushort cardNum, ushort axis, int bitIndex)
        {
            if (bitIndex <= 0)
            {
                return false;
            }
            return Context.AxisStateBitGetter != null && Context.AxisStateBitGetter(cardNum, axis, bitIndex);
        }
    }
}
