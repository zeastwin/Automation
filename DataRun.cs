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
    public class DataRun
    {
        public Thread[] threads = new Thread[100];
        //  public Task[] tasks = new Task[100];



        public ProcHandle[] ProcHandles = new ProcHandle[100];

        public DataRun()
        {
            for (int i = 0; i < ProcHandles.Length; i++)
            {
                ProcHandles[i] = new ProcHandle();
            }
        }
        public void ExecuteGoto(string GOTO, ProcHandle evt)
        {
            string[] key = GOTO.Split('-');
            evt.stepNum = int.Parse(key[1]);
            evt.opsNum = int.Parse(key[2]);
        }
        public void SetProcText(int procNum, bool isRun)
        {
            string result = isRun ? "|运行" : "|就绪";

            SF.frmProc.proc_treeView?.Invoke(new Action(() =>
            {
                SF.frmProc.proc_treeView.Nodes[procNum].Text = SF.frmProc.procsList[procNum].head.Name + result;
            }));
        }
        public void Delay(int milliSecond, ProcHandle evt)
        {
            if (milliSecond <= 0)
                return;
            int start = Environment.TickCount;
            while (Math.Abs(Environment.TickCount - start) < milliSecond && evt.State != ProcRunState.Stopped)//毫秒
            {
                Thread.Sleep(2);
            }
        }
        public void StartProc(Proc proc)
        {
            ProcHandle procHandle = new ProcHandle();
            procHandle.procNum = SF.frmProc.SelectedProcNum;
            procHandle.stepNum = 0;
            procHandle.opsNum = 0;
            procHandle.isThStop = false;
            procHandle.procName = proc.head.Name;
            ProcHandles[SF.frmProc.SelectedProcNum] = procHandle;
            //   Task task = Task.Run(() => RunProc(proc, procHandle));
            Thread th = new Thread(() => { RunProc(proc, procHandle); });
            threads[SF.frmProc.SelectedProcNum] = th;
            // tasks[SF.frmProc.SelectedProcNum] = task;
            th.Start();
        }
        public void StartProcAuto(Proc proc, int index)
        {
            ProcHandle procHandle = new ProcHandle();
            procHandle.procNum = index;
            procHandle.stepNum = 0;
            procHandle.opsNum = 0;
            procHandle.isThStop = false;
            procHandle.procName = proc.head.Name;
            ProcHandles[index] = procHandle;
            //   Task task = Task.Run(() => RunProc(proc, procHandle));
            Thread th = new Thread(() => { RunProc(proc, procHandle); });
            threads[index] = th;
            // tasks[SF.frmProc.SelectedProcNum] = task;
            th.Start();
        }
        public void RunProc(Proc proc, ProcHandle evt)
        {
            SetProcText(evt.procNum, true);
            for (int i = evt.stepNum; i < proc.steps.Count; i++)
            {
                evt.stepNum = i;
                RunStep(proc.steps[i], evt);
                if (evt.isThStop)
                    break;
                if (evt.isGoto)
                {
                    i = evt.stepNum - 1;
                    continue;
                }
                if (i != proc.steps.Count - 1)
                    evt.opsNum = 0;
            }
            evt.State = ProcRunState.Stopped;
            SetProcText(evt.procNum, false);
        }
        //运行步骤
        public bool RunStep(Step steps, ProcHandle evt)
        {

            bool bOK = true;
            for (int i = evt.opsNum; i < steps.Ops.Count; i++)
            {
                evt.isAlarm = false;
                evt.isGoto = false;
                evt.alarmMsg = null;
                if (evt.isThStop)
                    return false;
                evt.opsNum = i;
                if (steps.Ops[i].Enable)
                {
                    continue;
                }
                if (steps.Ops[i].isStopPoint)
                {
                    evt.m_evtRun.Reset();
                    evt.m_evtTik.Reset();
                    evt.m_evtTok.Set();
                    evt.State = ProcRunState.Paused;
                    SF.frmToolBar.btnPause?.Invoke(new Action(() =>
                    {
                        SF.frmToolBar.btnPause.Text = "继续";
                    }));
                }
                evt.m_evtRun.WaitOne();
                evt.m_evtTik.WaitOne();
                evt.m_evtTok.WaitOne();
                if (evt.isThStop)
                    return false;
                try
                {
                    ExecuteOperation(evt, steps.Ops[i]);
                    if (evt.isAlarm)
                    {
                        ProcRunState lastState = evt.State;
                        evt.State = ProcRunState.Alarming;
                        AlarmInfo alarmInfo = null;
                        if (!string.IsNullOrWhiteSpace(steps.Ops[i].AlarmInfoID)
                            && int.TryParse(steps.Ops[i].AlarmInfoID, out int alarmIndex)
                            && SF.alarmInfoStore != null)
                        {
                            SF.alarmInfoStore.TryGetByIndex(alarmIndex, out alarmInfo);
                        }
                        if (steps.Ops[i].AlarmType == "报警停止")
                            new Message($"发生报警:{evt.procNum}---{evt.stepNum}---{evt.opsNum}", evt.alarmMsg == null ? "流程停止" : evt.alarmMsg, () => { evt.isThStop = true; }, "确定", true);
                        if (steps.Ops[i].AlarmType == "报警忽略")
                        {

                        }
                        if (steps.Ops[i].AlarmType == "自动处理")
                        {
                            evt.isGoto = true;
                            string[] key = steps.Ops[i].Goto1.Split('-');
                            evt.stepNum = int.Parse(key[1]);
                            evt.opsNum = int.Parse(key[2]);
                        }
                        if (steps.Ops[i].AlarmType == "弹框确定")
                        {
                            evt.isGoto = true;
                            string note = !string.IsNullOrEmpty(alarmInfo?.Note) ? alarmInfo.Note : (evt.alarmMsg ?? "发生报警");
                            string btn1 = !string.IsNullOrEmpty(alarmInfo?.Btn1) ? alarmInfo.Btn1 : "确定";
                            new Message($"发生报警:{evt.procNum}---{evt.stepNum}---{evt.opsNum}", note, () => { ExecuteGoto(steps.Ops[i].Goto1, evt); }, btn1, true);
                        }
                        if (steps.Ops[i].AlarmType == "弹框确定与否")
                        {
                            evt.isGoto = true;
                            string note = !string.IsNullOrEmpty(alarmInfo?.Note) ? alarmInfo.Note : (evt.alarmMsg ?? "发生报警");
                            string btn1 = !string.IsNullOrEmpty(alarmInfo?.Btn1) ? alarmInfo.Btn1 : "确定";
                            string btn2 = !string.IsNullOrEmpty(alarmInfo?.Btn2) ? alarmInfo.Btn2 : "否";
                            new Message($"发生报警:{evt.procNum}---{evt.stepNum}---{evt.opsNum}", note, () => { ExecuteGoto(steps.Ops[i].Goto1, evt); }, () => { ExecuteGoto(steps.Ops[i].Goto2, evt); }, btn1, btn2, true);
                        }
                        if (steps.Ops[i].AlarmType == "弹框确定与否与取消")
                        {
                            evt.isGoto = true;
                            string note = !string.IsNullOrEmpty(alarmInfo?.Note) ? alarmInfo.Note : (evt.alarmMsg ?? "发生报警");
                            string btn1 = !string.IsNullOrEmpty(alarmInfo?.Btn1) ? alarmInfo.Btn1 : "确定";
                            string btn2 = !string.IsNullOrEmpty(alarmInfo?.Btn2) ? alarmInfo.Btn2 : "否";
                            string btn3 = !string.IsNullOrEmpty(alarmInfo?.Btn3) ? alarmInfo.Btn3 : "取消";
                            new Message($"发生报警:{evt.procNum}---{evt.stepNum}---{evt.opsNum}", note, () => { ExecuteGoto(steps.Ops[i].Goto1, evt); }, () => { ExecuteGoto(steps.Ops[i].Goto2, evt); }, () => { ExecuteGoto(steps.Ops[i].Goto3, evt); }, btn1, btn2, btn3, true);
                        }
                        if (evt.State == ProcRunState.Alarming)
                        {
                            evt.State = evt.isThStop ? ProcRunState.Stopped : lastState;
                        }
                    }
                    if (evt.isGoto)
                    {
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                    evt.isThStop = true;
                    return false;
                }
            }

            evt.opsNum = -1;
            return bOK;


        }


        public bool ExecuteOperation(ProcHandle evt, object operation)
        {
            try
            {
                switch (operation)
                {
                    case CallCustomFunc customFunc:
                        return RunCustomFunc(evt, customFunc);

                    case IoOperate ioOperate:
                        return RunIoOperate(evt, ioOperate);

                    case IoCheck ioCheck:
                        return RunIoCheck(evt, ioCheck);

                    case ProcOps procOps:
                        return RunProcOps(evt, procOps);

                    case WaitProc waitProc:
                        return RunWaitProc(evt, waitProc);

                    case Goto gotoOperation:
                        return RunGoto(evt, gotoOperation);

                    case ParamGoto paramGoto:
                        return RunParamGoto(evt, paramGoto);

                    case SetDataStructItem setDataStructItem:
                        return RunSetDataStructItem(evt, setDataStructItem);

                    case Delay delay:
                        return RunDelay(evt, delay);

                    case GetDataStructItem getDataStructItem:
                        return RunGetDataStructItem(evt, getDataStructItem);

                    case CopyDataStructItem copyDataStructItem:
                        return RunCopyDataStructItem(evt, copyDataStructItem);

                    case InsertDataStructItem insertDataStructItem:
                        return RunInsertDataStructItem(evt, insertDataStructItem);

                    case DelDataStructItem delDataStructItem:
                        return RunDelDataStructItem(evt, delDataStructItem);

                    case FindDataStructItem findDataStructItem:
                        return RunFindDataStructItem(evt, findDataStructItem);

                    case GetDataStructCount getDataStructCount:
                        return RunGetDataStructCount(evt, getDataStructCount);

                    case GetValue getValue:
                        return RunGetValue(evt, getValue);

                    case ModifyValue modifyValue:
                        return RunModifyValue(evt, modifyValue);

                    case StringFormat stringFormat:
                        return RunStringFormat(evt, stringFormat);

                    case Split split:
                        return RunSplit(evt, split);

                    case Replace replace:
                        return RunReplace(evt, replace);

                    case TcpOps tcpOps:
                        return RunTcpOps(evt, tcpOps);

                    case WaitTcp waitTcp:
                        return RunWaitTcp(evt, waitTcp);

                    case SendTcpMsg sendTcpMsg:
                        return RunSendTcpMsg(evt, sendTcpMsg);

                    case ReceoveTcpMsg receoveTcpMsg:
                        return RunReceoveTcpMsg(evt, receoveTcpMsg);

                    case SendSerialPortMsg sendSerialPortMsg:
                        return RunSendSerialPortMsg(evt, sendSerialPortMsg);

                    case ReceoveSerialPortMsg receoveSerialPortMsg:
                        return RunReceoveSerialPortMsg(evt, receoveSerialPortMsg);

                    case SendReceoveCommMsg sendReceoveCommMsg:
                        return RunSendReceoveCommMsg(evt, sendReceoveCommMsg);

                    case SerialPortOps serialPortOps:
                        return RunSerialPortOps(evt, serialPortOps);

                    case WaitSerialPort waitSerialPort:
                        return RunWaitSerialPort(evt, waitSerialPort);

                    case HomeRun homeRun:
                        return RunHomeRun(evt, homeRun);

                    case StationRunPos stationRunPos:
                        return RunStationRunPos(evt, stationRunPos);

                    case StationRunRel stationRunRel:
                        return RunStationRunRel(evt, stationRunRel);

                    case SetStationVel setStationVel:
                        return RunSetStationVel(evt, setStationVel);

                    case StationStop stationStop:
                        return RunStationStop(evt, stationStop);

                    case WaitStationStop waitStationStop:
                        return RunWaitStationStop(evt, waitStationStop);

                    default:
                        return false;
                }
            }
            catch (Exception e)
            {
                evt.isAlarm = true;
                evt.alarmMsg = e.Message;
                SF.frmInfo.PrintInfo(e.Message, FrmInfo.Level.Error);
                return false;
            }
        }

        public bool RunCustomFunc(ProcHandle evt, CallCustomFunc callCustomFunc)
        {
            string funcName = callCustomFunc.Name;

            evt.isAlarm = !SF.mainfrm.customFunc.RunFunc(funcName);

            return true;
        }
        public bool RunIoOperate(ProcHandle evt, IoOperate ioOperate)
        {
            int time;
            foreach (IoOutParam ioParam in ioOperate.IoParams)
            {
                time = ioParam.delayBefore;
                if (time <= 0 && !string.IsNullOrEmpty(ioParam.delayBeforeV))
                {
                    time = (int)SF.valueStore.GetValueByName(ioParam.delayBeforeV).GetDValue();
                }
                Delay(time, evt);
                if (!SF.motion.SetIO(SF.frmIO.DicIO[ioParam.IOName], ioParam.value))
                {
                    evt.isAlarm = true;
                    return false;
                }
                time = ioParam.delayAfter;
                if (time <= 0 && !string.IsNullOrEmpty(ioParam.delayAfterV))
                {
                    time = (int)SF.valueStore.GetValueByName(ioParam.delayAfterV).GetDValue();
                }
                Delay(time, evt);
            }
            return true;
        }
        public bool RunIoCheck(ProcHandle evt, IoCheck ioCheck)
        {
            bool value = false;
            //    int time;
            double timeOut;

            timeOut = ioCheck.timeOutC.TimeOut;
            if (timeOut <= 0 && !string.IsNullOrEmpty(ioCheck.timeOutC.TimeOutValue))
            {
                timeOut = (int)SF.valueStore.GetValueByName(ioCheck.timeOutC.TimeOutValue).GetDValue();
            }

            //time = ioParam.delayBefore;
            //if (time <= 0 && ioParam.delayBeforeV != "")
            //{
            //    time = (int)SF.valueStore.GetValueByName(ioParam.delayBeforeV).GetDValue();
            //}
            //Delay(time, evt);
            int start = Environment.TickCount;
            while (evt.State != ProcRunState.Stopped && timeOut > 0)
            {
                if (Math.Abs(Environment.TickCount - start) < timeOut)
                {
                    bool isCheckOff = true;
                    foreach (IoCheckParam ioParam in ioCheck.IoParams)
                    {
                        if (SF.frmIO.DicIO[ioParam.IOName].IOType == "通用输入")
                        {
                            SF.motion.GetInIO(SF.frmIO.DicIO[ioParam.IOName], ref value);

                        }
                        else
                        {
                            SF.motion.GetOutIO(SF.frmIO.DicIO[ioParam.IOName], ref value);

                        }
                        if (value != ioParam.value)
                        {
                            isCheckOff = false;
                            Delay(2, evt);
                            break;
                        }               
                    }
                    if (isCheckOff)
                    {
                        break;
                    }
                }
                else
                {
                    evt.isAlarm = true;
                    evt.alarmMsg = "检测超时";
                    return false;
                }
            }
            //time = ioParam.delayAfter;
            //if (time <= 0 && ioParam.delayAfterV != "")
            //{
            //    time = (int)SF.valueStore.GetValueByName(ioParam.delayAfterV).GetDValue();
            //}
            //Delay(time, evt);

            return true;
        }
        public bool RunProcOps(ProcHandle evt, ProcOps procOps)
        {
            bool value = false;
            foreach (procParam procParam in procOps.procParams)
            {
                Proc proc = null;
                if(!string.IsNullOrEmpty(procParam.ProcName))
                proc = SF.frmProc.procsList.FirstOrDefault(sc => sc.head.Name.ToString() == procParam.ProcName);
                else if (!string.IsNullOrEmpty(procParam.ProcValue))
                {
                    proc = SF.frmProc.procsList.FirstOrDefault(sc => sc.head.Name.ToString() == SF.valueStore.GetValueByName(procParam.ProcValue).GetCValue());
                }
                if(proc == null)
                {
                    evt.isAlarm = true;
                    evt.alarmMsg = "找不到流程";
                    return false;
                }
                if (procParam.value == "运行")
                {
                    if (evt.State != ProcRunState.Stopped)
                    {
                        evt.isAlarm = true;
                        return false;
                    }
                    int index = SF.frmProc.procsList.IndexOf(proc);
                    SF.DR.StartProcAuto(proc, index);
                    SF.DR.ProcHandles[index].m_evtRun.Set();
                    SF.DR.ProcHandles[index].m_evtTik.Set();
                    SF.DR.ProcHandles[index].m_evtTok.Set();
                    SF.DR.ProcHandles[index].State = ProcRunState.Running;

                    SF.frmProc.proc_treeView?.Invoke(new Action(() =>
                    {
                        SF.frmProc.proc_treeView.Nodes[index].Text = SF.frmProc.procsList[index].head.Name + "|运行";
                    }));
                }
                else
                {
                    if (evt.State == ProcRunState.Stopped)
                    {
                        evt.isAlarm = true;
                        return false;
                    }
                    int index = SF.frmProc.procsList.IndexOf(proc);
                    SF.DR.ProcHandles[index].isThStop = true;
                    SF.DR.ProcHandles[index].State = ProcRunState.Stopped;
                    SF.DR.ProcHandles[index].m_evtRun.Set();
                    SF.DR.ProcHandles[index].m_evtTik.Set();
                    SF.DR.ProcHandles[index].m_evtTok.Set();

                    SF.frmProc.proc_treeView?.Invoke(new Action(() =>
                    {
                        SF.frmProc.proc_treeView.Nodes[index].Text = SF.frmProc.procsList[index].head.Name + "|就绪";
                    }));
                }
                Delay(procParam.delayAfter, evt);
            }
            return true;
        }
        public bool RunWaitProc(ProcHandle evt, WaitProc waitProc)
        {
            bool value = false;
            int timeOut;
            timeOut = waitProc.timeOutC.TimeOut;
            if (timeOut <= 0 && !string.IsNullOrEmpty(waitProc.timeOutC.TimeOutValue))
            {
                timeOut = (int)SF.valueStore.GetValueByName(waitProc.timeOutC.TimeOutValue).GetDValue();
            }
            int DelayAfter;
            DelayAfter = waitProc.delayAfter;
            if (DelayAfter <= 0 && !string.IsNullOrEmpty(waitProc.delayAfterV))
            {
                DelayAfter = (int)SF.valueStore.GetValueByName(waitProc.delayAfterV).GetDValue();
            }
            int start = Environment.TickCount;
            while (evt.State != ProcRunState.Stopped)
            {
                if (timeOut > 0)
                {
                    if (Math.Abs(Environment.TickCount - start) > timeOut)
                    {
                        evt.isAlarm = true;
                        evt.alarmMsg = "等待超时";
                        break;
                    }
                }
                bool isWaitOff = true;

                foreach (WaitProcParam procParam in waitProc.Params)
                {
                    Proc proc = null;
                    if (!string.IsNullOrEmpty(procParam.ProcName))
                        proc = SF.frmProc.procsList.FirstOrDefault(sc => sc.head.Name.ToString() == procParam.ProcName);
                    else if (!string.IsNullOrEmpty(procParam.ProcValue))
                    {
                        proc = SF.frmProc.procsList.FirstOrDefault(sc => sc.head.Name.ToString() == SF.valueStore.GetValueByName(procParam.ProcValue).GetCValue());
                    }
                    if (proc == null)
                    {
                        evt.isAlarm = true;
                        evt.alarmMsg = "找不到流程";
                        return false;
                    }
                    int index = SF.frmProc.procsList.IndexOf(proc);
                    if (procParam.value == "运行")
                    {
                        if (SF.DR.ProcHandles[index].State == ProcRunState.Stopped)
                        {
                            isWaitOff = false;
                            break;
                        }
                    }
                    else if (procParam.value == "停止")
                    {
                        if (SF.DR.ProcHandles[index].State != ProcRunState.Stopped)
                        {
                            isWaitOff = false;
                            break;
                        }
                    }
                }
                if (isWaitOff)
                {
                    break;
                }
            }
            Delay(DelayAfter, evt);
            return true;
        }
        public bool RunGoto(ProcHandle evt, Goto gotoParam)
        {
            if (gotoParam.Params != null)
            {
                string value = "";
                if (!string.IsNullOrEmpty(gotoParam.ValueIndex))
                    value = SF.valueStore.GetValueByIndex(int.Parse(gotoParam.ValueIndex)).Value.ToString();
                else if (!string.IsNullOrEmpty(gotoParam.ValueIndex2Index))
                {
                    string index = SF.valueStore.GetValueByIndex(int.Parse(gotoParam.ValueIndex2Index)).Value.ToString();
                    value = SF.valueStore.GetValueByIndex(int.Parse(index)).Value.ToString();
                }
                else if (!string.IsNullOrEmpty(gotoParam.ValueName))
                {
                    value = SF.valueStore.GetValueByName(gotoParam.ValueName).Value.ToString();
                }
                else if (!string.IsNullOrEmpty(gotoParam.ValueName2Index))
                {
                    string index = SF.valueStore.GetValueByName(gotoParam.ValueName2Index).Value.ToString();
                    value = SF.valueStore.GetValueByIndex(int.Parse(index)).Value.ToString();
                }
                if(value == "")
                {
                    evt.isAlarm = true;
                    evt.alarmMsg = "匹配不到变量";
                    return false;
                }
                foreach (var item in gotoParam.Params)
                {
                    string itemValue = "";
                    if (!string.IsNullOrEmpty(item.MatchValue))
                        itemValue = item.MatchValue;
                    else if (!string.IsNullOrEmpty(item.MatchValueIndex))
                    {
                        itemValue = SF.valueStore.GetValueByIndex(int.Parse(item.MatchValueIndex)).Value.ToString();
                    }
                    else if (!string.IsNullOrEmpty(item.MatchValueV))
                    {
                        itemValue = SF.valueStore.GetValueByName(item.MatchValueV).Value.ToString();
                    }
                    if (value == itemValue)
                    {
                        string[] key = item.Goto.Split('-');
                        evt.stepNum = int.Parse(key[1]);
                        evt.opsNum = int.Parse(key[2]);
                        evt.isGoto = true;
                        return true;
                    }
                }
            }
            if (gotoParam.DefaultGoto != null)
            {
                string[] key = gotoParam.DefaultGoto.Split('-');
                evt.stepNum = int.Parse(key[1]);
                evt.opsNum = int.Parse(key[2]);
                evt.isGoto = true;
            }
            if (evt.isGoto == false)
            {
                evt.isAlarm = true;
                return false;
            }
            return true;
        }
        public bool RunParamGoto(ProcHandle evt, ParamGoto paramGoto)
        {
            if (paramGoto.Params != null)
            {
                bool isFirst = true;
                bool outPut = true;
                foreach (var item in paramGoto.Params)
                {
                    string value = "";
                    if (!string.IsNullOrEmpty(item.ValueIndex))
                        value = SF.valueStore.GetValueByIndex(int.Parse(item.ValueIndex)).Value.ToString();
                    else if (!string.IsNullOrEmpty(item.ValueIndex2Index))
                    {
                        string index = SF.valueStore.GetValueByIndex(int.Parse(item.ValueIndex2Index)).Value.ToString();
                        value = SF.valueStore.GetValueByIndex(int.Parse(index)).Value.ToString();
                    }
                    else if (!string.IsNullOrEmpty(item.ValueName))
                    {
                        value = SF.valueStore.GetValueByName(item.ValueName).Value.ToString();
                    }
                    else if (!string.IsNullOrEmpty(item.ValueName2Index))
                    {
                        string index = SF.valueStore.GetValueByName(item.ValueName2Index).Value.ToString();
                        value = SF.valueStore.GetValueByIndex(int.Parse(index)).Value.ToString();
                    }
                    bool tempValue = false;
                    if (item.JudgeMode == "值在区间左")
                    {
                        if (item.equal)
                        {
                            tempValue = item.Down >= double.Parse(value) ? true : false;
                        }
                        else
                        {
                            tempValue = item.Down > double.Parse(value) ? true : false;
                        }
                    }
                    else if (item.JudgeMode == "值在区间右")
                    {
                        if (item.equal)
                        {
                            tempValue = item.Down <= double.Parse(value) ? true : false;
                        }
                        else
                        {
                            tempValue = item.Down < double.Parse(value) ? true : false;
                        }
                    }
                    else if (item.JudgeMode == "值在区间内")
                    {
                        if (item.equal)
                        {
                            tempValue = item.Down <= double.Parse(value) && double.Parse(value) <= item.Up ? true : false;
                        }
                        else
                        {
                            tempValue = item.Down < double.Parse(value) && double.Parse(value) < item.Up ? true : false;
                        }
                    }
                    else if (item.JudgeMode == "等于特征字符")
                    {
                        tempValue = value == item.keyString ? true : false;
                    }
                    if (isFirst)
                    {
                        outPut = tempValue;
                        isFirst = false;
                    }
                    else
                    {
                        if (item.Operator == "且")
                        {
                            outPut = tempValue && outPut;
                        }
                        else
                        {
                            outPut = tempValue || outPut;
                        }
                    }
                }
                string[] key = outPut ? paramGoto.goto1?.Split('-') : paramGoto.goto2?.Split('-');
                if (key != null)
                {
                    evt.stepNum = int.Parse(key[1]);
                    evt.opsNum = int.Parse(key[2]);
                    evt.isGoto = true;
                }
            }

            return true;
        }
        public bool RunDelay(ProcHandle evt, Delay delay)
        {
            int time = 0;
            if (!string.IsNullOrEmpty(delay.timeMiniSecond))
                time = int.Parse(delay.timeMiniSecond);
            else if (!string.IsNullOrEmpty(delay.timeMiniSecondV))
            {
                time = int.Parse(SF.valueStore.GetValueByName(delay.timeMiniSecondV).Value);
            }
         
            Delay(time,evt);
            return true;
        }
        public bool RunGetValue(ProcHandle evt, GetValue getValue)
        {
            foreach (var item in getValue.Params)
            {
                //==============================================Get=====================================//
                string value = "";
                if (!string.IsNullOrEmpty(item.ValueSourceIndex))
                    value = SF.valueStore.GetValueByIndex(int.Parse(item.ValueSourceIndex)).Value.ToString();
                else if (!string.IsNullOrEmpty(item.ValueSourceIndex2Index))
                {
                    string index = SF.valueStore.GetValueByIndex(int.Parse(item.ValueSourceIndex2Index)).Value.ToString();
                    value = SF.valueStore.GetValueByIndex(int.Parse(index)).Value.ToString();
                }
                else if (!string.IsNullOrEmpty(item.ValueSourceName))
                {
                    value = SF.valueStore.GetValueByName(item.ValueSourceName).Value.ToString();
                }
                else if (!string.IsNullOrEmpty(item.ValueSourceName2Index))
                {
                    string index = SF.valueStore.GetValueByName(item.ValueSourceName2Index).Value.ToString();
                    value = SF.valueStore.GetValueByIndex(int.Parse(index)).Value.ToString();
                }
                //==============================================Save=====================================//
                if (!string.IsNullOrEmpty(item.ValueSaveIndex))
                {
                    if (!SF.valueStore.setValueByIndex(int.Parse(item.ValueSaveIndex), value))
                    {
                        evt.isAlarm = true;
                        evt.alarmMsg = $"保存变量失败:索引{item.ValueSaveIndex}";
                        return false;
                    }
                }
                else if (!string.IsNullOrEmpty(item.ValueSaveIndex2Index))
                {
                    string index = SF.valueStore.GetValueByIndex(int.Parse(item.ValueSaveIndex2Index)).Value.ToString();
                    if (!SF.valueStore.setValueByIndex(int.Parse(index), value))
                    {
                        evt.isAlarm = true;
                        evt.alarmMsg = $"保存变量失败:索引{index}";
                        return false;
                    }
                }
                else if (!string.IsNullOrEmpty(item.ValueSaveName))
                {
                    if (!SF.valueStore.setValueByName(item.ValueSaveName, value))
                    {
                        evt.isAlarm = true;
                        evt.alarmMsg = $"保存变量失败:{item.ValueSaveName}";
                        return false;
                    }
                }
                else if (!string.IsNullOrEmpty(item.ValueSaveName2Index))
                {
                    string index = SF.valueStore.GetValueByName(item.ValueSaveName2Index).Value.ToString();
                    if (!SF.valueStore.setValueByIndex(int.Parse(index), value))
                    {
                        evt.isAlarm = true;
                        evt.alarmMsg = $"保存变量失败:索引{index}";
                        return false;
                    }
                }
                
            }
            return true;
        }

        public bool RunModifyValue(ProcHandle evt, ModifyValue ops)
        {
            //==============================================GetSourceValue=====================================//
            string SourceValue = "";
            if (!string.IsNullOrEmpty(ops.ValueSourceIndex))
                SourceValue = SF.valueStore.GetValueByIndex(int.Parse(ops.ValueSourceIndex)).Value.ToString();
            else if (!string.IsNullOrEmpty(ops.ValueSourceIndex2Index))
            {
                string index = SF.valueStore.GetValueByIndex(int.Parse(ops.ValueSourceIndex2Index)).Value.ToString();
                SourceValue = SF.valueStore.GetValueByIndex(int.Parse(index)).Value.ToString();
            }
            else if (!string.IsNullOrEmpty(ops.ValueSourceName))
            {
                SourceValue = SF.valueStore.GetValueByName(ops.ValueSourceName).Value.ToString();
            }
            else if (!string.IsNullOrEmpty(ops.ValueSourceName2Index))
            {
                string index = SF.valueStore.GetValueByName(ops.ValueSourceName2Index).Value.ToString();
                SourceValue = SF.valueStore.GetValueByIndex(int.Parse(index)).Value.ToString();
            }
            if(SourceValue == "")
            {
                evt.isAlarm = true;
                evt.alarmMsg = "找不到源变量";
                return false;
            }

            //==============================================GetChangeValue=====================================//
            string ChangeValue = "";
            if (!string.IsNullOrEmpty(ops.ChangeValue))
            {
                ChangeValue = ops.ChangeValue;
            }
            else if (!string.IsNullOrEmpty(ops.ChangeValueIndex))
                ChangeValue = SF.valueStore.GetValueByIndex(int.Parse(ops.ChangeValueIndex)).Value.ToString();
            else if (!string.IsNullOrEmpty(ops.ChangeValueIndex2Index))
            {
                string index = SF.valueStore.GetValueByIndex(int.Parse(ops.ChangeValueIndex2Index)).Value.ToString();
                ChangeValue = SF.valueStore.GetValueByIndex(int.Parse(index)).Value.ToString();
            }
            else if (!string.IsNullOrEmpty(ops.ChangeValueName))
            {
                ChangeValue = SF.valueStore.GetValueByName(ops.ChangeValueName).Value.ToString();
            }
            else if (!string.IsNullOrEmpty(ops.ChangeValueName2Index))
            {
                string index = SF.valueStore.GetValueByName(ops.ChangeValueName2Index).Value.ToString();
                ChangeValue = SF.valueStore.GetValueByIndex(int.Parse(index)).Value.ToString();
            }
            if (ChangeValue == "")
            {
                evt.isAlarm = true;
                evt.alarmMsg = "找不到修改变量";
                return false;
            }

            string output = "";
            if (ops.ModifyType == "替换")
            {
                 output = ChangeValue;
            }
            else if (ops.ModifyType == "叠加")
            {
                double sourceR = ops.sourceR ? -1 : 1;
                double changeR = ops.ChangeR ? -1 : 1;

                output = (sourceR * double.Parse(SourceValue) + changeR * double.Parse(ChangeValue)).ToString();
            }
            else if (ops.ModifyType == "乘法")
            {
                double sourceR = ops.sourceR ? -1 : 1;
                double changeR = ops.ChangeR ? -1 : 1;

                output = (sourceR * double.Parse(SourceValue) * changeR * double.Parse(ChangeValue)).ToString();
            }
            else if (ops.ModifyType == "除法")
            {
                double sourceR = ops.sourceR ? -1 : 1;
                double changeR = ops.ChangeR ? -1 : 1;

                output = ((sourceR * double.Parse(SourceValue)) / (changeR * double.Parse(ChangeValue))).ToString();
            }
            else if (ops.ModifyType == "求余")
            {

                output = (double.Parse(SourceValue) %double.Parse(ChangeValue)).ToString();
            }
            else if (ops.ModifyType == "绝对值")
            {
                output = Math.Abs(double.Parse(SourceValue)).ToString();
            }

            //==============================================OutputValue=====================================//
            if (!string.IsNullOrEmpty(ops.OutputValueIndex))
            {
                if (!SF.valueStore.setValueByIndex(int.Parse(ops.OutputValueIndex), output))
                {
                    evt.isAlarm = true;
                    evt.alarmMsg = $"保存变量失败:索引{ops.OutputValueIndex}";
                    return false;
                }
            }
            else if (!string.IsNullOrEmpty(ops.OutputValueIndex2Index))
            {
                string index = SF.valueStore.GetValueByIndex(int.Parse(ops.OutputValueIndex2Index)).Value.ToString();
                if (!SF.valueStore.setValueByIndex(int.Parse(index), output))
                {
                    evt.isAlarm = true;
                    evt.alarmMsg = $"保存变量失败:索引{index}";
                    return false;
                }
            }
            else if (!string.IsNullOrEmpty(ops.OutputValueName))
            {
                if (!SF.valueStore.setValueByName(ops.OutputValueName, output))
                {
                    evt.isAlarm = true;
                    evt.alarmMsg = $"保存变量失败:{ops.OutputValueName}";
                    return false;
                }
            }
            else if (!string.IsNullOrEmpty(ops.OutputValueName2Index))
            {
                string index = SF.valueStore.GetValueByName(ops.OutputValueName2Index).Value.ToString();
                if (!SF.valueStore.setValueByIndex(int.Parse(index), output))
                {
                    evt.isAlarm = true;
                    evt.alarmMsg = $"保存变量失败:索引{index}";
                    return false;
                }
            }

            return true;

        }
        public bool RunStringFormat(ProcHandle evt, StringFormat stringFormat)
        {
            List<string> values = new List<string>();
            foreach (var item in stringFormat.Params)
            {
                //==============================================GetSourceValue=====================================//
                string SourceValue = "";
                if (!string.IsNullOrEmpty(item.ValueSourceIndex))
                    SourceValue = SF.valueStore.GetValueByIndex(int.Parse(item.ValueSourceIndex)).Value.ToString();
                else if (!string.IsNullOrEmpty(item.ValueSourceName))
                {
                    SourceValue = SF.valueStore.GetValueByName(item.ValueSourceName).Value.ToString();
                }
                values.Add(SourceValue);
            }
            try
            {
                string formattedStr = string.Format(stringFormat.Format, values.ToArray());

                if (!string.IsNullOrEmpty(stringFormat.OutputValueIndex))
                {
                    if (!SF.valueStore.setValueByIndex(int.Parse(stringFormat.OutputValueIndex), formattedStr))
                    {
                        evt.isAlarm = true;
                        evt.alarmMsg = $"格式化结果保存失败:索引{stringFormat.OutputValueIndex}";
                        return false;
                    }
                }
                else if (!string.IsNullOrEmpty(stringFormat.OutputValueName))
                {
                    if (!SF.valueStore.setValueByName(stringFormat.OutputValueName, formattedStr))
                    {
                        evt.isAlarm = true;
                        evt.alarmMsg = $"格式化结果保存失败:{stringFormat.OutputValueName}";
                        return false;
                    }
                }
               
            }
            catch (Exception ex)
            {
                evt.isAlarm = true;
                evt.alarmMsg = ex.Message;
                return false;
            }
            return true;

        }

        public bool RunSplit(ProcHandle evt, Split split)
        {
            string SourceValue = "";
            if (!string.IsNullOrEmpty(split.SourceValueIndex))
                SourceValue = SF.valueStore.GetValueByIndex(int.Parse(split.SourceValueIndex)).Value.ToString();
            else if (!string.IsNullOrEmpty(split.SourceValue))
            {
                SourceValue = SF.valueStore.GetValueByName(split.SourceValue).Value.ToString();
            }

            string[] splitArray = SourceValue.Split(split.SplitMark);

            int Startindex = int.Parse(split.startIndex);

            int SaveIndex = 0;
            if (!string.IsNullOrEmpty(split.OutputIndex))
                SaveIndex = int.Parse(split.OutputIndex);
            else if (!string.IsNullOrEmpty(split.Output))
            {
                SaveIndex = SF.valueStore.GetValueByName(split.Output).Index;
            }
            int Count = 0;
            if(!string.IsNullOrEmpty(split.Count))
            {
                Count = int.Parse(split.Count);
            }
            else
            {
                Count = splitArray.Length;
            }
            for (int i = SaveIndex; i < SaveIndex + Count; i++)
            {
                if (!SF.valueStore.setValueByIndex(i, splitArray[Startindex + i - SaveIndex]))
                {
                    evt.isAlarm = true;
                    evt.alarmMsg = $"保存变量失败:索引{i}";
                    return false;
                }
            }
            return true;

        }
        public bool RunReplace(ProcHandle evt, Replace replace)
        {
            string SourceValue = "";
            if (!string.IsNullOrEmpty(replace.SourceValueIndex))
                SourceValue = SF.valueStore.GetValueByIndex(int.Parse(replace.SourceValueIndex)).Value.ToString();
            else if (!string.IsNullOrEmpty(replace.SourceValue))
            {
                SourceValue = SF.valueStore.GetValueByName(replace.SourceValue).Value.ToString();
            }
            if (SourceValue == "")
            {
                evt.isAlarm = true;
                evt.alarmMsg = "找不到源变量";
                return false;
            }
        
            string replaceStr = "";
            if (!string.IsNullOrEmpty(replace.ReplaceStr))
            {
                replaceStr = replace.ReplaceStr;
            }
            else if (!string.IsNullOrEmpty(replace.ReplaceStrIndex))
                replaceStr = SF.valueStore.GetValueByIndex(int.Parse(replace.ReplaceStrIndex)).Value.ToString();
            else if (!string.IsNullOrEmpty(replace.ReplaceStrV))
            {
                replaceStr = SF.valueStore.GetValueByName(replace.ReplaceStrV).Value.ToString();
            }
            if (replaceStr == "")
            {
                evt.isAlarm = true;
                evt.alarmMsg = "找不到被替换字符";
                return false;
            }

            string newStr = "";
            if (!string.IsNullOrEmpty(replace.NewStr))
            {
                newStr = replace.NewStr;
            }
            else if (!string.IsNullOrEmpty(replace.NewStrIndex))
                newStr = SF.valueStore.GetValueByIndex(int.Parse(replace.NewStrIndex)).Value.ToString();
            else if (!string.IsNullOrEmpty(replace.NewStrV))
            {
                newStr = SF.valueStore.GetValueByName(replace.NewStrV).Value.ToString();
            }
            if (newStr == "")
            {
                evt.isAlarm = true;
                evt.alarmMsg = "找不到新字符";
                return false;
            }

            string str = "";

            if (replace.ReplaceType == "替换指定字符")
            {
                str = SourceValue.Replace(replaceStr, newStr);
            }
            else if (replace.ReplaceType == "替换指定区间")
            {
                string beforeSubstring = SourceValue.Substring(0, int.Parse(replace.StartIndex));
                string afterSubstring = SourceValue.Substring(int.Parse(replace.StartIndex) + int.Parse(replace.Count));

                str = beforeSubstring + newStr + afterSubstring;
            }

            if (!string.IsNullOrEmpty(replace.OutputIndex))
            {
                if (!SF.valueStore.setValueByIndex(int.Parse(replace.OutputIndex),str))
                {
                    evt.isAlarm = true;
                    evt.alarmMsg = $"保存变量失败:索引{replace.OutputIndex}";
                    return false;
                }
            }
            else if (!string.IsNullOrEmpty(replace.Output))
            {
                if (!SF.valueStore.setValueByName(replace.Output, str))
                {
                    evt.isAlarm = true;
                    evt.alarmMsg = $"保存变量失败:{replace.Output}";
                    return false;
                }
            }
            else
            {
                evt.isAlarm = true;
                evt.alarmMsg = "找不到保存变量";
                return false;
            }
            return true;

        }
        public bool RunSetDataStructItem(ProcHandle evt, SetDataStructItem setDataStructItem)
        {
            int structIndex = int.Parse(setDataStructItem.StructIndex);
            int itemIndex = int.Parse(setDataStructItem.ItemIndex);
            for (int i = 0; i < int.Parse(setDataStructItem.Count); i++)
            {
                int valueIndex = int.Parse(setDataStructItem.Params[i].valueIndex);
                if (!SF.dataStructStore.TrySetItemValueByIndex(structIndex, itemIndex, valueIndex, setDataStructItem.Params[i].value))
                {
                    evt.isAlarm = true;
                    evt.alarmMsg = $"设置数据结构失败:结构{structIndex},项{itemIndex},值{valueIndex}";
                    return false;
                }
            }
            return true;
        }

        public bool RunGetDataStructItem(ProcHandle evt, GetDataStructItem getDataStructItem)
        {

            int structIndex = int.Parse(getDataStructItem.StructIndex);
            int itemIndex = int.Parse(getDataStructItem.ItemIndex);
            if (getDataStructItem.IsAllItem)
            {
                int startIndex = SF.valueStore.GetValueByName(getDataStructItem.StartValue).Index;
                int count = SF.dataStructStore.GetItemValueCount(structIndex, itemIndex);
                for (int i = 0; i < count; i++)
                {
                    if (!SF.dataStructStore.TryGetItemValueByIndex(structIndex, itemIndex, i, out object obj))
                    {
                        evt.isAlarm = true;
                        evt.alarmMsg = $"读取数据结构失败:结构{structIndex},项{itemIndex},值{i}";
                        return false;
                    }
                    if (!SF.valueStore.setValueByIndex(startIndex + i, obj))
                    {
                        evt.isAlarm = true;
                        evt.alarmMsg = $"保存变量失败:索引{startIndex + i}";
                        return false;
                    }
                }
            }
            else
            {
                for (int i = 0; i < getDataStructItem.Params.Count; i++)
                {
                    int valueIndex = int.Parse(getDataStructItem.Params[i].valueIndex);
                    if (!SF.dataStructStore.TryGetItemValueByIndex(structIndex, itemIndex, valueIndex, out object obj))
                    {
                        evt.isAlarm = true;
                        evt.alarmMsg = $"读取数据结构失败:结构{structIndex},项{itemIndex},值{valueIndex}";
                        return false;
                    }

                    string valueName = "";
                    if (!string.IsNullOrEmpty(getDataStructItem.Params[i].ValueIndex))
                        valueName = SF.valueStore.GetValueByIndex(int.Parse(getDataStructItem.Params[i].ValueIndex)).Value.ToString();
                    else
                        valueName = SF.valueStore.GetValueByName(getDataStructItem.Params[i].ValueName).Value.ToString();

                    if (!SF.valueStore.setValueByName(valueName, obj))
                    {
                        evt.isAlarm = true;
                        evt.alarmMsg = $"保存变量失败:{valueName}";
                        return false;
                    }
                }
            }
            return true;
        }
        public bool RunCopyDataStructItem(ProcHandle evt, CopyDataStructItem copyDataStructItem)
        {
            if (copyDataStructItem.IsAllValue)
            {
                if (!SF.dataStructStore.TryCopyItemAll(int.Parse(copyDataStructItem.SourceStructIndex), int.Parse(copyDataStructItem.SourceItemIndex), int.Parse(copyDataStructItem.TargetStructIndex), int.Parse(copyDataStructItem.TargetItemIndex)))
                {
                    evt.isAlarm = true;
                    evt.alarmMsg = $"复制数据结构失败:源{copyDataStructItem.SourceStructIndex}-{copyDataStructItem.SourceItemIndex},目标{copyDataStructItem.TargetStructIndex}-{copyDataStructItem.TargetItemIndex}";
                    return false;
                }
            }
            else
            {
                for (int i = 0; i < copyDataStructItem.Params.Count; i++)
                {
                    if (!SF.dataStructStore.TryGetItemValueByIndex(int.Parse(copyDataStructItem.SourceStructIndex), int.Parse(copyDataStructItem.SourceItemIndex), int.Parse(copyDataStructItem.Params[i].SourcevalueIndex), out object obj, out DataStructValueType valueType))
                    {
                        evt.isAlarm = true;
                        evt.alarmMsg = $"读取数据结构失败:结构{copyDataStructItem.SourceStructIndex},项{copyDataStructItem.SourceItemIndex},值{copyDataStructItem.Params[i].SourcevalueIndex}";
                        return false;
                    }

                    if (obj == null)
                    {
                        evt.isAlarm = true;
                        evt.alarmMsg = $"数据结构值为空:结构{copyDataStructItem.SourceStructIndex},项{copyDataStructItem.SourceItemIndex},值{copyDataStructItem.Params[i].SourcevalueIndex}";
                        return false;
                    }

                    if (!SF.dataStructStore.TrySetItemValueByIndex(int.Parse(copyDataStructItem.TargetStructIndex), int.Parse(copyDataStructItem.TargetItemIndex), int.Parse(copyDataStructItem.Params[i].Targetvalue), obj.ToString(), valueType))
                    {
                        evt.isAlarm = true;
                        evt.alarmMsg = $"设置数据结构失败:结构{copyDataStructItem.TargetStructIndex},项{copyDataStructItem.TargetItemIndex},值{copyDataStructItem.Params[i].Targetvalue}";
                        return false;
                    }

                }

            }
            return true;
        }
        public bool RunInsertDataStructItem(ProcHandle evt, InsertDataStructItem insertDataStructItem)
        {
            DataStructItem dataStructItem = new DataStructItem
            {
                Name = insertDataStructItem.Name
            };
            for (int i = 0; i < insertDataStructItem.Params.Count; i++)
            {
                if (insertDataStructItem.Params[i].Type == "double")
                {
                    double num = -1;
                    if (insertDataStructItem.Params[i].ValueItem != null)
                        num = SF.valueStore.get_D_ValueByName(insertDataStructItem.Params[i].ValueItem);
                    else
                        num = double.Parse(insertDataStructItem.Params[i].Value);
                    dataStructItem.num[i] = num;
                }
                else
                {
                    string str = "";
                    if (insertDataStructItem.Params[i].ValueItem != null)
                        str = SF.valueStore.get_Str_ValueByName(insertDataStructItem.Params[i].ValueItem);
                    else
                        str = insertDataStructItem.Params[i].Value.ToString();
                    dataStructItem.str[i] = str;
                }
            }
            if (!SF.dataStructStore.TryInsertItem(int.Parse(insertDataStructItem.TargetStructIndex), int.Parse(insertDataStructItem.TargetItemIndex), dataStructItem))
            {
                evt.isAlarm = true;
                evt.alarmMsg = $"插入数据结构失败:结构{insertDataStructItem.TargetStructIndex},项{insertDataStructItem.TargetItemIndex}";
                return false;
            }
            return true;
        }


        public bool RunDelDataStructItem(ProcHandle evt, DelDataStructItem delDataStructItem)
        {
            int structIndex = int.Parse(delDataStructItem.TargetStructIndex);
            int itemIndex = int.Parse(delDataStructItem.TargetItemIndex);
            bool success;
            if (itemIndex >= 255)
            {
                success = SF.dataStructStore.TryRemoveLastItem(structIndex);
            }
            else if (itemIndex <= -1)
            {
                success = SF.dataStructStore.TryRemoveFirstItem(structIndex);
            }
            else
            {
                success = SF.dataStructStore.TryRemoveItemAt(structIndex, itemIndex);
            }
            if (!success)
            {
                evt.isAlarm = true;
                evt.alarmMsg = $"删除数据结构失败:结构{structIndex},项{itemIndex}";
                return false;
            }
            return true;
        }

        public bool RunFindDataStructItem(ProcHandle evt, FindDataStructItem findDataStructItem)
        {
            if (findDataStructItem.Type == "名称等于key")
            {
                if (!SF.dataStructStore.TryFindItemByName(int.Parse(findDataStructItem.TargetStructIndex), findDataStructItem.key, out string value))
                {
                    evt.isAlarm = true;
                    evt.alarmMsg = $"查找数据结构失败:结构{findDataStructItem.TargetStructIndex},key{findDataStructItem.key}";
                    return false;
                }
                if (!SF.valueStore.setValueByName(findDataStructItem.save, value))
                {
                    evt.isAlarm = true;
                    evt.alarmMsg = $"保存变量失败:{findDataStructItem.save}";
                    return false;
                }
            }
            else if (findDataStructItem.Type == "字符串等于key")
            {
                if (!SF.dataStructStore.TryFindItemByStringValue(int.Parse(findDataStructItem.TargetStructIndex), findDataStructItem.key, out string value))
                {
                    evt.isAlarm = true;
                    evt.alarmMsg = $"查找数据结构失败:结构{findDataStructItem.TargetStructIndex},key{findDataStructItem.key}";
                    return false;
                }
                if (!SF.valueStore.setValueByName(findDataStructItem.save, value))
                {
                    evt.isAlarm = true;
                    evt.alarmMsg = $"保存变量失败:{findDataStructItem.save}";
                    return false;
                }
            }
            else if (findDataStructItem.Type == "数值等于key")
            {
                if (!double.TryParse(findDataStructItem.key, out double keyValue))
                {
                    evt.isAlarm = true;
                    evt.alarmMsg = $"查找数值key无效:{findDataStructItem.key}";
                    return false;
                }
                if (!SF.dataStructStore.TryFindItemByNumberValue(int.Parse(findDataStructItem.TargetStructIndex), keyValue, out double value))
                {
                    evt.isAlarm = true;
                    evt.alarmMsg = $"查找数据结构失败:结构{findDataStructItem.TargetStructIndex},key{findDataStructItem.key}";
                    return false;
                }
                if (!SF.valueStore.setValueByName(findDataStructItem.save, value))
                {
                    evt.isAlarm = true;
                    evt.alarmMsg = $"保存变量失败:{findDataStructItem.save}";
                    return false;
                }
            }
            return true;
        }

        public bool RunGetDataStructCount(ProcHandle evt, GetDataStructCount getDataStructCount)
        {
            if (!SF.valueStore.setValueByName(getDataStructCount.StructCount, SF.dataStructStore.Count))
            {
                evt.isAlarm = true;
                evt.alarmMsg = $"保存变量失败:{getDataStructCount.StructCount}";
                return false;
            }
            if (!SF.valueStore.setValueByName(getDataStructCount.ItemCount, SF.dataStructStore.GetItemCount(int.Parse(getDataStructCount.TargetStructIndex))))
            {
                evt.isAlarm = true;
                evt.alarmMsg = $"保存变量失败:{getDataStructCount.ItemCount}";
                return false;
            }

            return true;
        }
        public bool RunTcpOps(ProcHandle evt, TcpOps tcpOps)
        {
            foreach (var op in tcpOps.Params)
            {
                SocketInfo socketInfo = SF.frmComunication.socketInfos.FirstOrDefault(sc => sc.Name == op.Name);
                if (socketInfo == null)
                {
                    evt.isAlarm = true;
                    evt.alarmMsg = $"TCP配置不存在:{op.Name}";
                    return false;
                }
                if (op.Ops == "启动")
                {
                    _ = SF.comm.StartTcpAsync(socketInfo);
                }
                else
                {
                    _ = SF.comm.StopTcpAsync(op.Name);

                }

            }
            return true;

        }

        public bool RunWaitTcp(ProcHandle evt, WaitTcp waitTcp)
        {
            foreach (var op in waitTcp.Params)
            {
                int start = Environment.TickCount;
                while (!evt.isThStop && Math.Abs(Environment.TickCount - start) < op.TimeOut)
                {
                    if (SF.comm.IsTcpActive(op.Name))
                    {
                        return true;
                    }
                    Delay(5, evt);
                }
                evt.isAlarm = true;
                evt.alarmMsg = $"等待TCP连接超时:{op.Name}";
                return false;
            }
            return true;
        }

        public bool RunSendTcpMsg(ProcHandle evt, SendTcpMsg sendTcpMsg)
        {
            bool success = SF.comm.SendTcpAsync(sendTcpMsg.ID, SF.valueStore.get_Str_ValueByName(sendTcpMsg.Msg), sendTcpMsg.isConVert)
                .GetAwaiter()
                .GetResult();
            if (!success)
            {
                evt.isAlarm = true;
                evt.alarmMsg = $"TCP发送失败:{sendTcpMsg.ID}";
                return false;
            }
            return true;
        }

        public bool RunReceoveTcpMsg(ProcHandle evt, ReceoveTcpMsg receoveTcpMsg)
        {
            if (!SF.comm.IsTcpActive(receoveTcpMsg.ID))
            {
                evt.isAlarm = true;
                evt.alarmMsg = $"TCP未连接:{receoveTcpMsg.ID}";
                return false;
            }
            SF.comm.ClearTcpMessages(receoveTcpMsg.ID);
            int start = Environment.TickCount;
            while (!evt.isThStop && Math.Abs(Environment.TickCount - start) < receoveTcpMsg.TImeOut)
            {
                if (SF.comm.TryReceiveTcp(receoveTcpMsg.ID, 50, out string msg))
                {
                    if (receoveTcpMsg.isConVert && int.TryParse(msg, out int number))
                    {
                        msg = Convert.ToString(number, 16).ToUpper();
                    }
                    if (!SF.valueStore.setValueByName(receoveTcpMsg.MsgSaveValue, msg))
                    {
                        evt.isAlarm = true;
                        evt.alarmMsg = $"保存TCP接收变量失败:{receoveTcpMsg.MsgSaveValue}";
                        return false;
                    }
                    return true;
                    }
            }
            evt.isAlarm = true;
            evt.alarmMsg = $"TCP接收超时:{receoveTcpMsg.ID}";
            return true;
        }
        public bool RunSendSerialPortMsg(ProcHandle evt, SendSerialPortMsg sendSerialPortMsg)
        {
            bool success = SF.comm.SendSerialAsync(sendSerialPortMsg.ID, SF.valueStore.get_Str_ValueByName(sendSerialPortMsg.Msg), sendSerialPortMsg.isConVert)
                .GetAwaiter()
                .GetResult();
            if (!success)
            {
                evt.isAlarm = true;
                evt.alarmMsg = $"串口发送失败:{sendSerialPortMsg.ID}";
                return false;
            }
            return true;
        }
        public bool RunReceoveSerialPortMsg(ProcHandle evt, ReceoveSerialPortMsg receoveSerialPortMsg)
        {
            if (!SF.comm.IsSerialOpen(receoveSerialPortMsg.ID))
            {
                evt.isAlarm = true;
                evt.alarmMsg = $"串口未打开:{receoveSerialPortMsg.ID}";
                return false;
            }
            SF.comm.ClearSerialMessages(receoveSerialPortMsg.ID);
            int start = Environment.TickCount;
            while (!evt.isThStop && Math.Abs(Environment.TickCount - start) < receoveSerialPortMsg.TImeOut)
            {
                if (SF.comm.TryReceiveSerial(receoveSerialPortMsg.ID, 50, out string msg))
                {
                    if (int.TryParse(msg, out int number))
                    {
                        msg = Convert.ToString(number, 16).ToUpper();
                    }
                    if (!SF.valueStore.setValueByName(receoveSerialPortMsg.MsgSaveValue, msg))
                    {
                        evt.isAlarm = true;
                        evt.alarmMsg = $"保存串口接收变量失败:{receoveSerialPortMsg.MsgSaveValue}";
                        return false;
                    }
                    return true;
                    }
            }
            evt.isAlarm = true;
            evt.alarmMsg = $"串口接收超时:{receoveSerialPortMsg.ID}";
            return true;
        }

        public bool RunSendReceoveCommMsg(ProcHandle evt, SendReceoveCommMsg sendReceoveCommMsg)
        {
            if (sendReceoveCommMsg == null || SF.comm == null || string.IsNullOrEmpty(sendReceoveCommMsg.ID))
            {
                evt.isAlarm = true;
                evt.alarmMsg = "通讯参数无效";
                return true;
            }

            string sendValue = SF.valueStore.get_Str_ValueByName(sendReceoveCommMsg.SendMsg);
            string commType = sendReceoveCommMsg.CommType ?? "TCP";

            if (string.Equals(commType, "TCP", StringComparison.OrdinalIgnoreCase))
            {
                if (!SF.comm.IsTcpActive(sendReceoveCommMsg.ID))
                {
                    evt.isAlarm = true;
                    evt.alarmMsg = $"TCP未连接:{sendReceoveCommMsg.ID}";
                    return false;
                }
                SF.comm.ClearTcpMessages(sendReceoveCommMsg.ID);
                bool sendSuccess = SF.comm.SendTcpAsync(sendReceoveCommMsg.ID, sendValue, sendReceoveCommMsg.SendConvert)
                    .GetAwaiter()
                    .GetResult();
                if (!sendSuccess)
                {
                    evt.isAlarm = true;
                    evt.alarmMsg = $"TCP发送失败:{sendReceoveCommMsg.ID}";
                    return false;
                }

                int start = Environment.TickCount;
                while (!evt.isThStop && Math.Abs(Environment.TickCount - start) < sendReceoveCommMsg.TimeOut)
                {
                    if (SF.comm.TryReceiveTcp(sendReceoveCommMsg.ID, 50, out string msg))
                    {
                        if (sendReceoveCommMsg.ReceiveConvert && int.TryParse(msg, out int number))
                        {
                            msg = Convert.ToString(number, 16).ToUpper();
                        }
                        if (!string.IsNullOrEmpty(sendReceoveCommMsg.ReceiveSaveValue))
                        {
                            if (!SF.valueStore.setValueByName(sendReceoveCommMsg.ReceiveSaveValue, msg))
                            {
                                evt.isAlarm = true;
                                evt.alarmMsg = $"保存TCP接收变量失败:{sendReceoveCommMsg.ReceiveSaveValue}";
                                return false;
                            }
                        }
                        return true;
                    }
                }
                evt.isAlarm = true;
                evt.alarmMsg = $"TCP接收超时:{sendReceoveCommMsg.ID}";
                return false;
            }

            if (string.Equals(commType, "串口", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(commType, "Serial", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(commType, "SerialPort", StringComparison.OrdinalIgnoreCase))
            {
                if (!SF.comm.IsSerialOpen(sendReceoveCommMsg.ID))
                {
                    evt.isAlarm = true;
                    evt.alarmMsg = $"串口未打开:{sendReceoveCommMsg.ID}";
                    return false;
                }
                SF.comm.ClearSerialMessages(sendReceoveCommMsg.ID);
                bool sendSuccess = SF.comm.SendSerialAsync(sendReceoveCommMsg.ID, sendValue, sendReceoveCommMsg.SendConvert)
                    .GetAwaiter()
                    .GetResult();
                if (!sendSuccess)
                {
                    evt.isAlarm = true;
                    evt.alarmMsg = $"串口发送失败:{sendReceoveCommMsg.ID}";
                    return false;
                }

                int start = Environment.TickCount;
                while (!evt.isThStop && Math.Abs(Environment.TickCount - start) < sendReceoveCommMsg.TimeOut)
                {
                    if (SF.comm.TryReceiveSerial(sendReceoveCommMsg.ID, 50, out string msg))
                    {
                        if (sendReceoveCommMsg.ReceiveConvert && int.TryParse(msg, out int number))
                        {
                            msg = Convert.ToString(number, 16).ToUpper();
                        }
                        if (!string.IsNullOrEmpty(sendReceoveCommMsg.ReceiveSaveValue))
                        {
                            if (!SF.valueStore.setValueByName(sendReceoveCommMsg.ReceiveSaveValue, msg))
                            {
                                evt.isAlarm = true;
                                evt.alarmMsg = $"保存串口接收变量失败:{sendReceoveCommMsg.ReceiveSaveValue}";
                                return false;
                            }
                        }
                        return true;
                    }
                }
                evt.isAlarm = true;
                evt.alarmMsg = $"串口接收超时:{sendReceoveCommMsg.ID}";
                return false;
            }

            evt.isAlarm = true;
            evt.alarmMsg = $"通讯类型不支持:{sendReceoveCommMsg.CommType}";
            return true;
        }

        public bool RunSerialPortOps(ProcHandle evt, SerialPortOps serialPortOps)
        {
            foreach (var op in serialPortOps.Params)
            {
                SerialPortInfo serialPortInfo = SF.frmComunication.serialPortInfos.FirstOrDefault(sc => sc.Name == op.Name);
                if (serialPortInfo == null)
                {
                    evt.isAlarm = true;
                    evt.alarmMsg = $"串口配置不存在:{op.Name}";
                    return false;
                }
                if (op.Ops == "启动")
                {
                    _ = SF.comm.StartSerialAsync(serialPortInfo);
                }
                else
                {
                    _ = SF.comm.StopSerialAsync(op.Name);
                }

            }
            return true;

        }

        public bool RunWaitSerialPort(ProcHandle evt, WaitSerialPort waitSerialPort)
        {
            foreach (var op in waitSerialPort.Params)
            {
                int start = Environment.TickCount;
                while (!evt.isThStop && Math.Abs(Environment.TickCount - start) < op.TimeOut)
                {
                    if (SF.comm.IsSerialOpen(op.Name))
                    {
                        return true;
                    }
                    Delay(5, evt);
                }
                evt.isAlarm = true;
                evt.alarmMsg = $"等待串口连接超时:{op.Name}";
                return false;
            }
            return true;
        }

        public bool RunHomeRun(ProcHandle evt, HomeRun homeRun)
        {
            DataStation station;
            if (homeRun.StationIndex != -1)
            {
                station = SF.frmCard.dataStation[homeRun.StationIndex];
            }
            else
            {
                station = SF.frmCard.dataStation.FirstOrDefault(sc => sc.Name == homeRun.StationName);
            }
            if (station != null)
            {
                station.SetState(DataStation.Status.Run);
                int stationIndex = SF.frmCard.dataStation.IndexOf(station);
                if (stationIndex != -1)
                {
                    Task task = Task.Run(() =>
                    {
                        if (homeRun.StationHomeType == "轴按优先顺序回")
                            SF.frmControl.HomeStationByseq(stationIndex);
                        else
                            SF.frmControl.HomeStationByAll(stationIndex);
                    });
                    SF.Delay(500);
                    if (!homeRun.isUnWait)
                    {
                        int start = Environment.TickCount;
                        bool isInPos = false;
                        while (evt.isThStop == false && station.GetState() == DataStation.Status.Run)
                        {
                            if (Math.Abs(Environment.TickCount - start) > 120000)
                            {
                                evt.isAlarm = true;
                                SF.frmInfo.PrintInfo(homeRun.Name + "运动超时！", FrmInfo.Level.Error);
                                station.SetState(DataStation.Status.NotReady);
                                return false;
                            }
                            for (int i = 0; i < 6; i++)
                            {
                                if (station.dataAxis.axisConfigs[i].AxisName == "-1")
                                    continue;
                                if (SF.cardStore.TryGetAxis(int.Parse(station.dataAxis.axisConfigs[i].CardNum), i, out Axis axisInfo) && axisInfo.State == Axis.Status.Ready)
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
                            SF.Delay(5);
                        }
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
            //    station = SF.frmCard.dataStation[stationRunPos.StationIndex];
            //}
            //else
            //{
            station = SF.frmCard.dataStation.FirstOrDefault(sc => sc.Name == stationRunPos.StationName);
            //}

            if (station != null)
            {
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
                if (posItems != null)
                {
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
                            if (!SF.cardStore.TryGetAxis(cardNum, axisNum, out Axis axisInfo))
                            {
                                evt.isAlarm = true;
                                SF.frmInfo.PrintInfo($"工站：{stationRunPos.Name} {cardNum}号卡{axisNum}号轴配置不存在", FrmInfo.Level.Error);
                                station.SetState(DataStation.Status.NotReady);
                                return false;
                            }
                            if (stationRunPos.ChangeVel == "改变速度")
                            {
                                double VelTemp = 0;
                                double AccTemp = 0;
                                double DecTemp = 0;

                                VelTemp = stationRunPos.Vel == 0 ? SF.valueStore.GetValueByName(stationRunPos.VelV).GetDValue() : stationRunPos.Vel;
                                AccTemp = stationRunPos.Acc == 0 ? SF.valueStore.GetValueByName(stationRunPos.AccV).GetDValue() : stationRunPos.Acc;
                                DecTemp = stationRunPos.Dec == 0 ? SF.valueStore.GetValueByName(stationRunPos.DecV).GetDValue() : stationRunPos.Dec;


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
                            SF.motion.SetMovParam(cardNum, axisNum, 0, Vel, Acc, Dec, 0, 0, axisInfo.PulseToMM);

                            SF.motion.Mov(ushort.Parse(station.dataAxis.axisConfigs[i].CardNum), (ushort)station.dataAxis.axisConfigs[i].axis.AxisNum, Poses[i], 1, false);
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
                        int start = Environment.TickCount;
                        bool isInPos = false;
                        double time;
                        if (stationRunPos.timeOut > 0)
                            time = stationRunPos.timeOut;
                        else
                        {

                            time = SF.valueStore.GetValueByName(stationRunPos.timeOutV).GetDValue();
                        }

                        while (evt.isThStop == false && cardNums.Count != 0 && station.GetState() == DataStation.Status.Run)
                        {
                            if (Math.Abs(Environment.TickCount - start) > time)
                            {
                                evt.isAlarm = true;
                                SF.frmInfo.PrintInfo(stationRunPos.Name + "运动超时！", FrmInfo.Level.Error);
                                station.SetState(DataStation.Status.NotReady);
                                return false;
                            }
                            for (int i = 0; i < cardNums.Count; i++)
                            {
                                if (SF.motion.GetInPos(cardNums[i], axisNums[i]))
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
                            SF.Delay(5);
                        }
                        if (stationRunPos.isCheckInPos)
                        {
                            for (int i = 0; i < cardNums.Count; i++)
                            {
                                if (!SF.cardStore.TryGetAxis(cardNums[i], axisNums[i], out Axis axisInfo))
                                {
                                    evt.isAlarm = true;
                                    SF.frmInfo.PrintInfo($"工站：{stationRunPos.Name} {cardNums[i]}号卡{axisNums[i]}号轴配置不存在", FrmInfo.Level.Error);
                                    return false;
                                }
                                if (((SF.motion.GetAxisPos(cardNums[i], axisNums[i]) / axisInfo.PulseToMM - TargetPos[i])) > 0.01)
                                {
                                    evt.isAlarm = true;
                                    SF.frmInfo.PrintInfo($"工站：{stationRunPos.Name} {cardNums[i]}号卡{axisNums[i]}号轴运动未到位", FrmInfo.Level.Error);
                                }
                            }
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
            //    station = SF.frmCard.dataStation[stationRunRel.StationIndex];
            //}
            //else
            //{
            station = SF.frmCard.dataStation.FirstOrDefault(sc => sc.Name == stationRunRel.StationName);
            //}

            if (station != null)
            {
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
                        if (!SF.cardStore.TryGetAxis(cardNum, axisNum, out Axis axisInfo))
                        {
                            evt.isAlarm = true;
                            SF.frmInfo.PrintInfo($"工站：{stationRunRel.Name} {cardNum}号卡{axisNum}号轴配置不存在", FrmInfo.Level.Error);
                            station.SetState(DataStation.Status.NotReady);
                            return false;
                        }
                        if (stationRunRel.ChangeVel == "改变速度")
                        {
                            double VelTemp = 0;
                            double AccTemp = 0;
                            double DecTemp = 0;

                            VelTemp = stationRunRel.Vel == 0 ? SF.valueStore.GetValueByName(stationRunRel.VelV).GetDValue() : stationRunRel.Vel;
                            AccTemp = stationRunRel.Acc == 0 ? SF.valueStore.GetValueByName(stationRunRel.AccV).GetDValue() : stationRunRel.Acc;
                            DecTemp = stationRunRel.Dec == 0 ? SF.valueStore.GetValueByName(stationRunRel.DecV).GetDValue() : stationRunRel.Dec;


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
                        SF.motion.SetMovParam(cardNum, axisNum, 0, Vel, Acc, Dec, 0, 0, axisInfo.PulseToMM);

                        double DistanceTemp = 0;
                        DistanceTemp = TargetPos[i] == 0 ? SF.valueStore.GetValueByName(TargetPosV[i]).GetDValue() : TargetPos[i];

                        SF.motion.Mov(ushort.Parse(station.dataAxis.axisConfigs[i].CardNum), (ushort)station.dataAxis.axisConfigs[i].axis.AxisNum, DistanceTemp, 0, false);
                    }
                }
                if (!stationRunRel.isUnWait)
                {

                    int start = Environment.TickCount;
                    bool isInPos = false;
                    double time;
                    if (stationRunRel.timeOut > 0)
                        time = stationRunRel.timeOut;
                    else
                    {

                        time = SF.valueStore.GetValueByName(stationRunRel.timeOutV).GetDValue();
                    }
                    while (evt.isThStop == false && station.GetState() == DataStation.Status.Run)
                    {
                        if (Math.Abs(Environment.TickCount - start) > time)
                        {
                            evt.isAlarm = true;
                            SF.frmInfo.PrintInfo(stationRunRel.Name + "运动超时！", FrmInfo.Level.Error);
                            station.SetState(DataStation.Status.NotReady);
                            return false;
                        }
                        for (int i = 0; i < cardNums.Count; i++)
                        {
                            if (SF.motion.GetInPos(cardNums[i], axisNums[i]))
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
                        SF.Delay(5);
                    }
                    if (stationRunRel.isCheckInPos)
                    {
                        for (int i = 0; i < cardNums.Count; i++)
                        {
                            if (!SF.cardStore.TryGetAxis(cardNums[i], axisNums[i], out Axis axisInfo))
                            {
                                evt.isAlarm = true;
                                SF.frmInfo.PrintInfo($"工站：{stationRunRel.Name} {cardNums[i]}号卡{axisNums[i]}号轴配置不存在", FrmInfo.Level.Error);
                                return false;
                            }
                            if (((SF.motion.GetAxisPos(cardNums[i], axisNums[i]) / axisInfo.PulseToMM - TargetPos[i])) > 0.01)
                            {
                                evt.isAlarm = true;
                                SF.frmInfo.PrintInfo($"工站：{stationRunRel.Name} {cardNums[i]}号卡{axisNums[i]}号轴运动未到位", FrmInfo.Level.Error);
                            }
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
                station = SF.frmCard.dataStation[setStationVel.StationIndex];
            }
            else
            {
                station = SF.frmCard.dataStation.FirstOrDefault(sc => sc.Name == setStationVel.StationName);
            }

            if (station != null)
            {
                double Vel = 0;
                double Acc = 0;
                double Dec = 0;

                Vel = setStationVel.Vel == 0 ? SF.valueStore.GetValueByName(setStationVel.VelV).GetDValue() : setStationVel.Vel;
                Acc = setStationVel.Acc == 0 ? SF.valueStore.GetValueByName(setStationVel.AccV).GetDValue() : setStationVel.Acc;
                Dec = setStationVel.Dec == 0 ? SF.valueStore.GetValueByName(setStationVel.DecV).GetDValue() : setStationVel.Dec;

                if (setStationVel.SetAxisObj == "工站")
                {
                    for (int i = 0; i < 6; i++)
                    {
                        if (station.dataAxis.axisConfigs[i].AxisName != "-1")
                        {
                            ushort cardNum = ushort.Parse(station.dataAxis.axisConfigs[i].CardNum);
                            ushort axisNum = (ushort)station.dataAxis.axisConfigs[i].axis.AxisNum;

                            if (!SF.cardStore.TryGetAxis(cardNum, axisNum, out Axis axisInfo))
                            {
                                evt.isAlarm = true;
                                SF.frmInfo.PrintInfo($"工站：{setStationVel.StationName} {cardNum}号卡{axisNum}号轴配置不存在", FrmInfo.Level.Error);
                                return false;
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
                        SF.frmInfo.PrintInfo($"工站：{setStationVel.StationName} 轴配置不存在", FrmInfo.Level.Error);
                        return false;
                    }
                    int cardNum = int.Parse(axisInfo.CardNum);
                    int axisNum = axisInfo.axis.AxisNum;
                    if (!SF.cardStore.TryGetAxis(cardNum, axisNum, out Axis axisConfig))
                    {
                        evt.isAlarm = true;
                        SF.frmInfo.PrintInfo($"工站：{setStationVel.StationName} {cardNum}号卡{axisNum}号轴配置不存在", FrmInfo.Level.Error);
                        return false;
                    }
                    axisConfig.SpeedRun = Vel;
                    axisConfig.AccRun = Acc;
                    axisConfig.DecRun = Dec;
                }
            }
            else
            {
                return false;
            }
            return true;
        }
        public bool RunStationStop(ProcHandle evt, StationStop stationStop)
        {
            DataStation station = SF.frmCard.dataStation.FirstOrDefault(sc => sc.Name == stationStop.StationName);

            if (station != null)
            {
                if (stationStop.isAllStop)
                {
                    SF.frmControl.StopStation(station);
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
                            SF.frmControl.StopAxis(cardNum, axisNum);
                        }
                    }
                }
            }
            else
            {
                return false;
            }
            return true;
        }
        public bool RunWaitStationStop(ProcHandle evt, WaitStationStop waitStationStop)
        {
            DataStation station;
            if (waitStationStop.StationIndex != -1)
            {
                station = SF.frmCard.dataStation[waitStationStop.StationIndex];
            }
            else
            {
                station = SF.frmCard.dataStation.FirstOrDefault(sc => sc.Name == waitStationStop.StationName);
            }
            station.SetState(DataStation.Status.Run);
            List<ushort> cardNums = new List<ushort>();
            List<ushort> axisNums = new List<ushort>();
            if (station != null)
            {
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
                int start = Environment.TickCount;
                double time;
                if (waitStationStop.timeOut > 0)
                    time = waitStationStop.timeOut;
                else
                {

                    time = SF.valueStore.GetValueByName(waitStationStop.timeOutV).GetDValue();
                }
                while (evt.isThStop == false && station.GetState() == DataStation.Status.Run)
                {
                    bool isInPos = false;

                    if (Math.Abs(Environment.TickCount - start) > time)
                    {
                        evt.isAlarm = true;
                        SF.frmInfo.PrintInfo(waitStationStop.Name + "等待超时！", FrmInfo.Level.Error);
                        return false;
                    }
                    for (int i = 0; i < cardNums.Count; i++)
                    {
                        if (waitStationStop.isWaitHome)
                        {
                            if (!SF.cardStore.TryGetAxis(cardNums[i], axisNums[i], out Axis axisInfo))
                            {
                                evt.isAlarm = true;
                                SF.frmInfo.PrintInfo($"工站：{waitStationStop.Name} {cardNums[i]}号卡{axisNums[i]}号轴配置不存在", FrmInfo.Level.Error);
                                return false;
                            }
                            if (SF.motion.HomeStatus(cardNums[i], axisNums[i]) && axisInfo.GetState() == Axis.Status.Ready)
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
                            if (SF.motion.GetInPos(cardNums[i], axisNums[i]))
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
                    SF.Delay(5);
                }
            }
            else
            {
                return false;
            }
            return true;
        }
    }
    public enum ProcRunState
    {
        Stopped = 0,
        Paused = 1,
        Running = 2,
        Alarming = 3
    }
    public class ProcHandle
    {
        public ManualResetEvent m_evtRun = new ManualResetEvent(false);
        public ManualResetEvent m_evtTik = new ManualResetEvent(false);
        public ManualResetEvent m_evtTok = new ManualResetEvent(false);

        public int procNum;
        public int stepNum;
        public int opsNum;

        public string procName;

        //流程状态
        public ProcRunState State = ProcRunState.Stopped;
        //线程终止标志位
        public bool isThStop;
        //标志是否发生了跳转
        public bool isGoto;
        //标志是否发生了报警
        public bool isAlarm;
        //自定义报警信息
        public string alarmMsg;

    }
}
