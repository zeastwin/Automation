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
                evt.isAlarm = true;
                evt.alarmMsg = "工站列表为空";
                Logger?.Log(evt.alarmMsg, LogLevel.Error);
                throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
            }
            if (homeRun.StationIndex != -1)
            {
                if (homeRun.StationIndex < 0 || homeRun.StationIndex >= Context.Stations.Count)
                {
                    evt.isAlarm = true;
                    evt.alarmMsg = $"工站索引无效:{homeRun.StationIndex}";
                    Logger?.Log(evt.alarmMsg, LogLevel.Error);
                    throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
                }
                station = Context.Stations[homeRun.StationIndex];
            }
            else
            {
                station = Context.Stations.FirstOrDefault(sc => sc.Name == homeRun.StationName);
            }
            if (station == null)
            {
                evt.isAlarm = true;
                evt.alarmMsg = $"找不到工站:{homeRun.StationName}";
                Logger?.Log(evt.alarmMsg, LogLevel.Error);
                throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
            }
            station.SetState(DataStation.Status.Run);
                int stationIndex = Context.Stations.IndexOf(station);
                if (stationIndex != -1)
                {
                    Task task = Task.Run(() =>
                    {
                        if (evt.CancellationToken.IsCancellationRequested)
                        {
                            return;
                        }
                        if (homeRun.StationHomeType == "轴按优先顺序回")
                            HomeStationBySeq(stationIndex, evt);
                        else
                            HomeStationByAll(stationIndex, evt);
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
                                evt.isAlarm = true;
                                evt.alarmMsg = homeRun.Name + "运动超时";
                                Logger?.Log(homeRun.Name + "运动超时！", LogLevel.Error);
                                station.SetState(DataStation.Status.NotReady);
                                throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
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
                evt.isAlarm = true;
                evt.alarmMsg = $"找不到工站:{stationRunPos.StationName}";
                Logger?.Log(evt.alarmMsg, LogLevel.Error);
                throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
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
                evt.isAlarm = true;
                evt.alarmMsg = $"工站点位不存在:{stationRunPos.PosName}";
                Logger?.Log(evt.alarmMsg, LogLevel.Error);
                station.SetState(DataStation.Status.NotReady);
                throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
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
                                evt.isAlarm = true;
                                evt.alarmMsg = $"工站：{stationRunPos.Name} {cardNum}号卡{axisNum}号轴配置不存在";
                                Logger?.Log(evt.alarmMsg, LogLevel.Error);
                                station.SetState(DataStation.Status.NotReady);
                                throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
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
                            evt.isAlarm = true;
                            evt.alarmMsg = $"{stationRunPos.Name}超时配置无效";
                            Logger?.Log(evt.alarmMsg, LogLevel.Error);
                            station.SetState(DataStation.Status.NotReady);
                            throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
                        }

                        while (evt.CancellationToken.IsCancellationRequested == false
                            && !evt.CancellationToken.IsCancellationRequested
                            && cardNums.Count != 0
                            && station.GetState() == DataStation.Status.Run)
                        {
                            if (stopwatch.ElapsedMilliseconds > time)
                            {
                                evt.isAlarm = true;
                                evt.alarmMsg = stationRunPos.Name + "运动超时";
                                Logger?.Log(stationRunPos.Name + "运动超时！", LogLevel.Error);
                                station.SetState(DataStation.Status.NotReady);
                                throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
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
                                    evt.isAlarm = true;
                                    evt.alarmMsg = $"工站：{stationRunPos.Name} {cardNums[i]}号卡{axisNums[i]}号轴配置不存在";
                                    Logger?.Log(evt.alarmMsg, LogLevel.Error);
                                    throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
                                }
                                if (((Context.Motion.GetAxisPos(cardNums[i], axisNums[i]) / axisInfo.PulseToMM - TargetPos[i])) > 0.01)
                                {
                                    evt.isAlarm = true;
                                    evt.alarmMsg = $"工站：{stationRunPos.Name} {cardNums[i]}号卡{axisNums[i]}号轴运动未到位";
                                    Logger?.Log(evt.alarmMsg, LogLevel.Error);
                                    throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
                                }
                            }
                        }
                    }

            return true;
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
                evt.isAlarm = true;
                evt.alarmMsg = $"找不到工站:{stationRunRel.StationName}";
                Logger?.Log(evt.alarmMsg, LogLevel.Error);
                throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
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
                            evt.isAlarm = true;
                            evt.alarmMsg = $"工站：{stationRunRel.Name} {cardNum}号卡{axisNum}号轴配置不存在";
                            Logger?.Log(evt.alarmMsg, LogLevel.Error);
                            station.SetState(DataStation.Status.NotReady);
                            throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
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
                        evt.isAlarm = true;
                        evt.alarmMsg = $"{stationRunRel.Name}超时配置无效";
                        Logger?.Log(evt.alarmMsg, LogLevel.Error);
                        station.SetState(DataStation.Status.NotReady);
                        throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
                    }
                    while (evt.CancellationToken.IsCancellationRequested == false
                        && !evt.CancellationToken.IsCancellationRequested
                        && station.GetState() == DataStation.Status.Run)
                    {
                        if (stopwatch.ElapsedMilliseconds > time)
                        {
                            evt.isAlarm = true;
                            evt.alarmMsg = stationRunRel.Name + "运动超时";
                            Logger?.Log(stationRunRel.Name + "运动超时！", LogLevel.Error);
                            station.SetState(DataStation.Status.NotReady);
                            throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
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
                                evt.isAlarm = true;
                                evt.alarmMsg = $"工站：{stationRunRel.Name} {cardNums[i]}号卡{axisNums[i]}号轴配置不存在";
                                Logger?.Log(evt.alarmMsg, LogLevel.Error);
                                throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
                            }
                            if (((Context.Motion.GetAxisPos(cardNums[i], axisNums[i]) / axisInfo.PulseToMM - TargetPos[i])) > 0.01)
                            {
                                evt.isAlarm = true;
                                evt.alarmMsg = $"工站：{stationRunRel.Name} {cardNums[i]}号卡{axisNums[i]}号轴运动未到位";
                                Logger?.Log(evt.alarmMsg, LogLevel.Error);
                                throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
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
                evt.isAlarm = true;
                evt.alarmMsg = $"找不到工站:{setStationVel.StationName}";
                Logger?.Log(evt.alarmMsg, LogLevel.Error);
                throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
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
                                evt.isAlarm = true;
                                evt.alarmMsg = $"工站：{setStationVel.StationName} {cardNum}号卡{axisNum}号轴配置不存在";
                                Logger?.Log(evt.alarmMsg, LogLevel.Error);
                                throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
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
                        evt.isAlarm = true;
                        evt.alarmMsg = $"工站：{setStationVel.StationName} 轴配置不存在";
                        Logger?.Log(evt.alarmMsg, LogLevel.Error);
                        throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
                    }
                    int cardNum = int.Parse(axisInfo.CardNum);
                    int axisNum = axisInfo.axis.AxisNum;
                    if (!Context.CardStore.TryGetAxis(cardNum, axisNum, out Axis axisConfig))
                    {
                        evt.isAlarm = true;
                        evt.alarmMsg = $"工站：{setStationVel.StationName} {cardNum}号卡{axisNum}号轴配置不存在";
                        Logger?.Log(evt.alarmMsg, LogLevel.Error);
                        throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
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
                evt.isAlarm = true;
                evt.alarmMsg = $"找不到工站:{stationStop.StationName}";
                Logger?.Log(evt.alarmMsg, LogLevel.Error);
                throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
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
                evt.isAlarm = true;
                evt.alarmMsg = $"找不到工站:{waitStationStop.StationName}";
                Logger?.Log(evt.alarmMsg, LogLevel.Error);
                throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
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
                evt.isAlarm = true;
                evt.alarmMsg = $"{waitStationStop.Name}超时配置无效";
                Logger?.Log(evt.alarmMsg, LogLevel.Error);
                throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
            }
            while (evt.CancellationToken.IsCancellationRequested == false
                && !evt.CancellationToken.IsCancellationRequested
                && station.GetState() == DataStation.Status.Run)
            {
                bool isInPos = false;

                if (stopwatch.ElapsedMilliseconds > time)
                {
                    evt.isAlarm = true;
                    evt.alarmMsg = waitStationStop.Name + "等待超时";
                    Logger?.Log(waitStationStop.Name + "等待超时！", LogLevel.Error);
                    throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
                }
                for (int i = 0; i < cardNums.Count; i++)
                {
                    if (waitStationStop.isWaitHome)
                    {
                        if (!Context.CardStore.TryGetAxis(cardNums[i], axisNums[i], out Axis axisInfo))
                        {
                            evt.isAlarm = true;
                            evt.alarmMsg = $"工站：{waitStationStop.Name} {cardNums[i]}号卡{axisNums[i]}号轴配置不存在";
                            Logger?.Log(evt.alarmMsg, LogLevel.Error);
                            throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
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

        private void HomeStationBySeq(int dataStationIndex, ProcHandle evt)
        {
            if (Context.Stations == null || dataStationIndex < 0 || dataStationIndex >= Context.Stations.Count)
            {
                evt.isAlarm = true;
                evt.alarmMsg = $"工站索引无效:{dataStationIndex}";
                Logger?.Log(evt.alarmMsg, LogLevel.Error);
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
                        HomeSingleAxis(ushort.Parse(item.CardNum), (ushort)item.axis.AxisNum, evt);
                        if (Context.CardStore == null || !Context.CardStore.TryGetAxis(int.Parse(item.CardNum), i, out Axis axisInfo))
                        {
                            evt.isAlarm = true;
                            evt.alarmMsg = $"卡{item.CardNum}轴{i}配置不存在，工站回零动作终止。";
                            Logger?.Log(evt.alarmMsg, LogLevel.Error);
                            return;
                        }

                        if (axisInfo.State == Axis.Status.NotReady)
                        {
                            evt.isAlarm = true;
                            evt.alarmMsg = $"卡{item.CardNum}轴{i}回零失败,工站回零动作终止。";
                            Logger?.Log(evt.alarmMsg, LogLevel.Error);
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
                            (ushort)station.dataAxis.axisConfigs[index].axis.AxisNum, evt);
                    }, evt.CancellationToken);
                    evt.RunningTasks.Add(task);
                }
            }
        }

        private void HomeStationByAll(int dataStationIndex, ProcHandle evt)
        {
            if (Context.Stations == null || dataStationIndex < 0 || dataStationIndex >= Context.Stations.Count)
            {
                evt.isAlarm = true;
                evt.alarmMsg = $"工站索引无效:{dataStationIndex}";
                Logger?.Log(evt.alarmMsg, LogLevel.Error);
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
                            (ushort)station.dataAxis.axisConfigs[index].axis.AxisNum, evt);
                    }, evt.CancellationToken);
                    evt.RunningTasks.Add(task);
                }
            }
        }

        private void HomeSingleAxis(ushort cardNum, ushort axis, ProcHandle evt)
        {
            if (Context.Motion == null || Context.CardStore == null)
            {
                evt.isAlarm = true;
                evt.alarmMsg = "运动控制未初始化";
                Logger?.Log(evt.alarmMsg, LogLevel.Error);
                return;
            }
            if (evt.CancellationToken.IsCancellationRequested)
            {
                return;
            }
            if (!Context.Motion.GetInPos(cardNum, axis))
            {
                evt.isAlarm = true;
                evt.alarmMsg = $"轴未到位，禁止回零:{cardNum}-{axis}";
                Logger?.Log(evt.alarmMsg, LogLevel.Error);
                return;
            }
            ushort dir = 0;
            if (!Context.CardStore.TryGetAxis(cardNum, axis, out Axis axisInfo))
            {
                evt.isAlarm = true;
                evt.alarmMsg = $"轴配置不存在:{cardNum}-{axis}";
                Logger?.Log(evt.alarmMsg, LogLevel.Error);
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
                                Logger?.Log("限位方向错误，回零失败。", LogLevel.Error);
                                evt.isAlarm = true;
                                evt.alarmMsg = "限位方向错误，回零失败。";
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
                                Logger?.Log("限位方向错误，回零失败。", LogLevel.Error);
                                evt.isAlarm = true;
                                evt.alarmMsg = "限位方向错误，回零失败。";
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
