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
            while (Math.Abs(Environment.TickCount - start) < milliSecond && evt.isRun != 0)//毫秒
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
            evt.isRun = 0;
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
                    evt.isRun = 1;
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
                    #region
                    //if (steps.Ops[i] is CallCustomFunc)
                    //{
                    //    CallCustomFunc temp = (CallCustomFunc)steps.Ops[i];
                    //    bOK = RunCustomFunc(evt, temp);
                    //}
                    //else if (steps.Ops[i] is IoOperate)
                    //{
                    //    IoOperate temp = (IoOperate)steps.Ops[i];
                    //    bOK = RunIoOperate(evt, temp);
                    //}
                    //else if (steps.Ops[i] is IoCheck)
                    //{
                    //    IoCheck temp = (IoCheck)steps.Ops[i];
                    //    bOK = RunIoCheck(evt, temp);
                    //}
                    //else if (steps.Ops[i] is ProcOps)
                    //{
                    //    ProcOps temp = (ProcOps)steps.Ops[i];
                    //    bOK = RunProcOps(evt, temp);
                    //}
                    //else if (steps.Ops[i] is WaitProc)
                    //{
                    //    WaitProc temp = (WaitProc)steps.Ops[i];
                    //    bOK = RunWaitProc(evt, temp);
                    //}
                    //else if (steps.Ops[i] is Goto)
                    //{
                    //    Goto temp = (Goto)steps.Ops[i];
                    //    bOK = RunGoto(evt, temp);
                    //}
                    //else if (steps.Ops[i] is ParamGoto)
                    //{
                    //    ParamGoto temp = (ParamGoto)steps.Ops[i];
                    //    bOK = RunParamGoto(evt, temp);
                    //}
                    //else if (steps.Ops[i] is SetDataStructItem)
                    //{
                    //    SetDataStructItem temp = (SetDataStructItem)steps.Ops[i];
                    //    bOK = RunSetDataStructItem(evt, temp);
                    //}
                    //else if (steps.Ops[i] is Delay)
                    //{
                    //    Delay temp = (Delay)steps.Ops[i];
                    //    bOK = RunDelay(evt, temp);
                    //}
                    //else if (steps.Ops[i] is GetDataStructItem)
                    //{
                    //    GetDataStructItem temp = (GetDataStructItem)steps.Ops[i];
                    //    bOK = RunGetDataStructItem(evt, temp);
                    //}
                    //else if (steps.Ops[i] is CopyDataStructItem)
                    //{
                    //    CopyDataStructItem temp = (CopyDataStructItem)steps.Ops[i];
                    //    bOK = RunCopyDataStructItem(evt, temp);
                    //}
                    //else if (steps.Ops[i] is InsertDataStructItem)
                    //{
                    //    InsertDataStructItem temp = (InsertDataStructItem)steps.Ops[i];
                    //    bOK = RunInsertDataStructItem(evt, temp);
                    //}
                    //else if (steps.Ops[i] is DelDataStructItem)
                    //{
                    //    DelDataStructItem temp = (DelDataStructItem)steps.Ops[i];
                    //    bOK = RunDelDataStructItem(evt, temp);
                    //}
                    //else if (steps.Ops[i] is FindDataStructItem)
                    //{
                    //    FindDataStructItem temp = (FindDataStructItem)steps.Ops[i];
                    //    bOK = RunFindDataStructItem(evt, temp);
                    //}
                    //else if (steps.Ops[i] is GetDataStructCount)
                    //{
                    //    GetDataStructCount temp = (GetDataStructCount)steps.Ops[i];
                    //    bOK = RunGetDataStructCount(evt, temp);
                    //}
                    //else if (steps.Ops[i] is GetValue)
                    //{
                    //    GetValue temp = (GetValue)steps.Ops[i];
                    //    bOK = RunGetValue(evt, temp);
                    //}
                    //else if (steps.Ops[i] is ModifyValue)
                    //{
                    //    ModifyValue temp = (ModifyValue)steps.Ops[i];
                    //    bOK = RunModifyValue(evt, temp);
                    //}
                    //else if (steps.Ops[i] is StringFormat)
                    //{
                    //    StringFormat temp = (StringFormat)steps.Ops[i];
                    //    bOK = RunStringFormat(evt, temp);
                    //}
                    //else if (steps.Ops[i] is Split)
                    //{
                    //    Split temp = (Split)steps.Ops[i];
                    //    bOK = RunSplit(evt, temp);
                    //}
                    //else if (steps.Ops[i] is Replace)
                    //{
                    //    Replace temp = (Replace)steps.Ops[i];
                    //    bOK = RunReplace(evt, temp);
                    //}
                    //else if (steps.Ops[i] is TcpOps)
                    //{
                    //    TcpOps temp = (TcpOps)steps.Ops[i];
                    //    bOK = RunTcpOps(evt, temp);
                    //}
                    //else if (steps.Ops[i] is WaitTcp)
                    //{
                    //    WaitTcp temp = (WaitTcp)steps.Ops[i];
                    //    bOK = RunWaitTcp(evt, temp);
                    //}
                    //else if (steps.Ops[i] is SendTcpMsg)
                    //{
                    //    SendTcpMsg temp = (SendTcpMsg)steps.Ops[i];
                    //    bOK = RunSendTcpMsg(evt, temp);
                    //}
                    //else if (steps.Ops[i] is ReceoveTcpMsg)
                    //{
                    //    ReceoveTcpMsg temp = (ReceoveTcpMsg)steps.Ops[i];
                    //    bOK = RunReceoveTcpMsg(evt, temp);
                    //}
                    //else if (steps.Ops[i] is SendSerialPortMsg)
                    //{
                    //    SendSerialPortMsg temp = (SendSerialPortMsg)steps.Ops[i];
                    //    bOK = RunSendSerialPortMsg(evt, temp);
                    //}
                    //else if (steps.Ops[i] is ReceoveSerialPortMsg)
                    //{
                    //    ReceoveSerialPortMsg temp = (ReceoveSerialPortMsg)steps.Ops[i];
                    //    bOK = RunReceoveSerialPortMsg(evt, temp);
                    //}
                    //else if (steps.Ops[i] is SerialPortOps)
                    //{
                    //    SerialPortOps temp = (SerialPortOps)steps.Ops[i];
                    //    bOK = RunSerialPortOps(evt, temp);
                    //}
                    //else if (steps.Ops[i] is WaitSerialPort)
                    //{
                    //    WaitSerialPort temp = (WaitSerialPort)steps.Ops[i];
                    //    bOK = RunWaitSerialPort(evt, temp);
                    //}
                    #endregion
                    if (evt.isAlarm)
                    {
                        AlarmInfo alarmInfo = null;
                        if (steps.Ops[i].AlarmInfoID != null)
                            alarmInfo = SF.frmAlarmConfig.alarmInfos[int.Parse(steps.Ops[i].AlarmInfoID)];
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
                            new Message($"发生报警:{evt.procNum}---{evt.stepNum}---{evt.opsNum}", alarmInfo.Note, () => { ExecuteGoto(steps.Ops[i].Goto1, evt); }, !string.IsNullOrEmpty(alarmInfo.Btn1) ? alarmInfo.Btn1 : "确定", true);
                        }
                        if (steps.Ops[i].AlarmType == "弹框确定与否")
                        {
                            evt.isGoto = true;
                            new Message($"发生报警:{evt.procNum}---{evt.stepNum}---{evt.opsNum}", alarmInfo.Note, () => { ExecuteGoto(steps.Ops[i].Goto1, evt); }, () => { ExecuteGoto(steps.Ops[i].Goto2, evt); }, !string.IsNullOrEmpty(alarmInfo.Btn1) ? alarmInfo.Btn1 : "确定", !string.IsNullOrEmpty(alarmInfo.Btn2) ? alarmInfo.Btn2 : "否", true);
                        }
                        if (steps.Ops[i].AlarmType == "弹框确定与否与取消")
                        {
                            evt.isGoto = true;
                            new Message($"发生报警:{evt.procNum}---{evt.stepNum}---{evt.opsNum}", alarmInfo.Note, () => { ExecuteGoto(steps.Ops[i].Goto1, evt); }, () => { ExecuteGoto(steps.Ops[i].Goto2, evt); }, () => { ExecuteGoto(steps.Ops[i].Goto3, evt); }, !string.IsNullOrEmpty(alarmInfo.Btn1) ? alarmInfo.Btn1 : "确定", !string.IsNullOrEmpty(alarmInfo.Btn2) ? alarmInfo.Btn2 : "否", !string.IsNullOrEmpty(alarmInfo.Btn3) ? alarmInfo.Btn3 : "取消", true);
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
                    time = (int)SF.frmValue.GetValueByName(ioParam.delayBeforeV).GetDValue();
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
                    time = (int)SF.frmValue.GetValueByName(ioParam.delayAfterV).GetDValue();
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
                timeOut = (int)SF.frmValue.GetValueByName(ioCheck.timeOutC.TimeOutValue).GetDValue();
            }

            //time = ioParam.delayBefore;
            //if (time <= 0 && ioParam.delayBeforeV != "")
            //{
            //    time = (int)SF.frmValue.GetValueByName(ioParam.delayBeforeV).GetDValue();
            //}
            //Delay(time, evt);
            int start = Environment.TickCount;
            while (evt.isRun != 0 && timeOut > 0)
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
            //    time = (int)SF.frmValue.GetValueByName(ioParam.delayAfterV).GetDValue();
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
                    proc = SF.frmProc.procsList.FirstOrDefault(sc => sc.head.Name.ToString() == SF.frmValue.GetValueByName(procParam.ProcValue).GetCValue());
                }
                if(proc == null)
                {
                    evt.isAlarm = true;
                    evt.alarmMsg = "找不到流程";
                    return false;
                }
                if (procParam.value == "运行")
                {
                    if (evt.isRun != 0)
                    {
                        evt.isAlarm = true;
                        return false;
                    }
                    int index = SF.frmProc.procsList.IndexOf(proc);
                    SF.DR.StartProcAuto(proc, index);
                    SF.DR.ProcHandles[index].m_evtRun.Set();
                    SF.DR.ProcHandles[index].m_evtTik.Set();
                    SF.DR.ProcHandles[index].m_evtTok.Set();
                    SF.DR.ProcHandles[index].isRun = 2;

                    SF.frmProc.proc_treeView?.Invoke(new Action(() =>
                    {
                        SF.frmProc.proc_treeView.Nodes[index].Text = SF.frmProc.procsList[index].head.Name + "|运行";
                    }));
                }
                else
                {
                    if (evt.isRun == 0)
                    {
                        evt.isAlarm = true;
                        return false;
                    }
                    int index = SF.frmProc.procsList.IndexOf(proc);
                    SF.DR.ProcHandles[index].isThStop = true;
                    SF.DR.ProcHandles[index].isRun = 0;
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
                timeOut = (int)SF.frmValue.GetValueByName(waitProc.timeOutC.TimeOutValue).GetDValue();
            }
            int DelayAfter;
            DelayAfter = waitProc.delayAfter;
            if (DelayAfter <= 0 && !string.IsNullOrEmpty(waitProc.delayAfterV))
            {
                DelayAfter = (int)SF.frmValue.GetValueByName(waitProc.delayAfterV).GetDValue();
            }
            int start = Environment.TickCount;
            while (evt.isRun != 0)
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
                        proc = SF.frmProc.procsList.FirstOrDefault(sc => sc.head.Name.ToString() == SF.frmValue.GetValueByName(procParam.ProcValue).GetCValue());
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
                        if (SF.DR.ProcHandles[index].isRun == 0)
                        {
                            isWaitOff = false;
                            break;
                        }
                    }
                    else if (procParam.value == "停止")
                    {
                        if (SF.DR.ProcHandles[index].isRun != 0)
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
                    value = SF.frmValue.GetValueByIndex(int.Parse(gotoParam.ValueIndex)).Value.ToString();
                else if (!string.IsNullOrEmpty(gotoParam.ValueIndex2Index))
                {
                    string index = SF.frmValue.GetValueByIndex(int.Parse(gotoParam.ValueIndex2Index)).Value.ToString();
                    value = SF.frmValue.GetValueByIndex(int.Parse(index)).Value.ToString();
                }
                else if (!string.IsNullOrEmpty(gotoParam.ValueName))
                {
                    value = SF.frmValue.GetValueByName(gotoParam.ValueName).Value.ToString();
                }
                else if (!string.IsNullOrEmpty(gotoParam.ValueName2Index))
                {
                    string index = SF.frmValue.GetValueByName(gotoParam.ValueName2Index).Value.ToString();
                    value = SF.frmValue.GetValueByIndex(int.Parse(index)).Value.ToString();
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
                        itemValue = SF.frmValue.GetValueByIndex(int.Parse(item.MatchValueIndex)).Value.ToString();
                    }
                    else if (!string.IsNullOrEmpty(item.MatchValueV))
                    {
                        itemValue = SF.frmValue.GetValueByName(item.MatchValueV).Value.ToString();
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
                        value = SF.frmValue.GetValueByIndex(int.Parse(item.ValueIndex)).Value.ToString();
                    else if (!string.IsNullOrEmpty(item.ValueIndex2Index))
                    {
                        string index = SF.frmValue.GetValueByIndex(int.Parse(item.ValueIndex2Index)).Value.ToString();
                        value = SF.frmValue.GetValueByIndex(int.Parse(index)).Value.ToString();
                    }
                    else if (!string.IsNullOrEmpty(item.ValueName))
                    {
                        value = SF.frmValue.GetValueByName(item.ValueName).Value.ToString();
                    }
                    else if (!string.IsNullOrEmpty(item.ValueName2Index))
                    {
                        string index = SF.frmValue.GetValueByName(item.ValueName2Index).Value.ToString();
                        value = SF.frmValue.GetValueByIndex(int.Parse(index)).Value.ToString();
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
                time = int.Parse(SF.frmValue.GetValueByName(delay.timeMiniSecondV).Value);
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
                    value = SF.frmValue.GetValueByIndex(int.Parse(item.ValueSourceIndex)).Value.ToString();
                else if (!string.IsNullOrEmpty(item.ValueSourceIndex2Index))
                {
                    string index = SF.frmValue.GetValueByIndex(int.Parse(item.ValueSourceIndex2Index)).Value.ToString();
                    value = SF.frmValue.GetValueByIndex(int.Parse(index)).Value.ToString();
                }
                else if (!string.IsNullOrEmpty(item.ValueSourceName))
                {
                    value = SF.frmValue.GetValueByName(item.ValueSourceName).Value.ToString();
                }
                else if (!string.IsNullOrEmpty(item.ValueSourceName2Index))
                {
                    string index = SF.frmValue.GetValueByName(item.ValueSourceName2Index).Value.ToString();
                    value = SF.frmValue.GetValueByIndex(int.Parse(index)).Value.ToString();
                }
                //==============================================Save=====================================//
                if (!string.IsNullOrEmpty(item.ValueSaveIndex))
                    SF.frmValue.setValueByIndex(int.Parse(item.ValueSaveIndex), value);
                else if (!string.IsNullOrEmpty(item.ValueSaveIndex2Index))
                {
                    string index = SF.frmValue.GetValueByIndex(int.Parse(item.ValueSaveIndex2Index)).Value.ToString();
                    SF.frmValue.setValueByIndex(int.Parse(index), value);
                }
                else if (!string.IsNullOrEmpty(item.ValueSaveName))
                {
                    SF.frmValue.setValueByName(item.ValueSaveName, value);
                }
                else if (!string.IsNullOrEmpty(item.ValueSaveName2Index))
                {
                    string index = SF.frmValue.GetValueByName(item.ValueSaveName2Index).Value.ToString();
                    SF.frmValue.setValueByIndex(int.Parse(index), value);
                }
                
            }
            return true;
        }

        public bool RunModifyValue(ProcHandle evt, ModifyValue ops)
        {
            //==============================================GetSourceValue=====================================//
            string SourceValue = "";
            if (!string.IsNullOrEmpty(ops.ValueSourceIndex))
                SourceValue = SF.frmValue.GetValueByIndex(int.Parse(ops.ValueSourceIndex)).Value.ToString();
            else if (!string.IsNullOrEmpty(ops.ValueSourceIndex2Index))
            {
                string index = SF.frmValue.GetValueByIndex(int.Parse(ops.ValueSourceIndex2Index)).Value.ToString();
                SourceValue = SF.frmValue.GetValueByIndex(int.Parse(index)).Value.ToString();
            }
            else if (!string.IsNullOrEmpty(ops.ValueSourceName))
            {
                SourceValue = SF.frmValue.GetValueByName(ops.ValueSourceName).Value.ToString();
            }
            else if (!string.IsNullOrEmpty(ops.ValueSourceName2Index))
            {
                string index = SF.frmValue.GetValueByName(ops.ValueSourceName2Index).Value.ToString();
                SourceValue = SF.frmValue.GetValueByIndex(int.Parse(index)).Value.ToString();
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
                ChangeValue = SF.frmValue.GetValueByIndex(int.Parse(ops.ChangeValueIndex)).Value.ToString();
            else if (!string.IsNullOrEmpty(ops.ChangeValueIndex2Index))
            {
                string index = SF.frmValue.GetValueByIndex(int.Parse(ops.ChangeValueIndex2Index)).Value.ToString();
                ChangeValue = SF.frmValue.GetValueByIndex(int.Parse(index)).Value.ToString();
            }
            else if (!string.IsNullOrEmpty(ops.ChangeValueName))
            {
                ChangeValue = SF.frmValue.GetValueByName(ops.ChangeValueName).Value.ToString();
            }
            else if (!string.IsNullOrEmpty(ops.ChangeValueName2Index))
            {
                string index = SF.frmValue.GetValueByName(ops.ChangeValueName2Index).Value.ToString();
                ChangeValue = SF.frmValue.GetValueByIndex(int.Parse(index)).Value.ToString();
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
                SF.frmValue.setValueByIndex(int.Parse(ops.OutputValueIndex), output);
            else if (!string.IsNullOrEmpty(ops.OutputValueIndex2Index))
            {
                string index = SF.frmValue.GetValueByIndex(int.Parse(ops.OutputValueIndex2Index)).Value.ToString();
                SF.frmValue.setValueByIndex(int.Parse(index), output);
            }
            else if (!string.IsNullOrEmpty(ops.OutputValueName))
            {
                SF.frmValue.setValueByName(ops.OutputValueName, output);
            }
            else if (!string.IsNullOrEmpty(ops.OutputValueName2Index))
            {
                string index = SF.frmValue.GetValueByName(ops.OutputValueName2Index).Value.ToString();
                SF.frmValue.setValueByIndex(int.Parse(index), output);
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
                    SourceValue = SF.frmValue.GetValueByIndex(int.Parse(item.ValueSourceIndex)).Value.ToString();
                else if (!string.IsNullOrEmpty(item.ValueSourceName))
                {
                    SourceValue = SF.frmValue.GetValueByName(item.ValueSourceName).Value.ToString();
                }
                values.Add(SourceValue);
            }
            try
            {
                string formattedStr = string.Format(stringFormat.Format, values.ToArray());

                if (!string.IsNullOrEmpty(stringFormat.OutputValueIndex))
                {
                    SF.frmValue.setValueByIndex(int.Parse(stringFormat.OutputValueIndex), formattedStr);
                }
                else if (!string.IsNullOrEmpty(stringFormat.OutputValueName))
                {
                    SF.frmValue.setValueByName(stringFormat.OutputValueName, formattedStr);
                }
               
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
            return true;

        }

        public bool RunSplit(ProcHandle evt, Split split)
        {
            string SourceValue = "";
            if (!string.IsNullOrEmpty(split.SourceValueIndex))
                SourceValue = SF.frmValue.GetValueByIndex(int.Parse(split.SourceValueIndex)).Value.ToString();
            else if (!string.IsNullOrEmpty(split.SourceValue))
            {
                SourceValue = SF.frmValue.GetValueByName(split.SourceValue).Value.ToString();
            }

            string[] splitArray = SourceValue.Split(split.SplitMark);

            int Startindex = int.Parse(split.startIndex);

            int SaveIndex = 0;
            if (!string.IsNullOrEmpty(split.OutputIndex))
                SaveIndex = int.Parse(split.OutputIndex);
            else if (!string.IsNullOrEmpty(split.Output))
            {
                SaveIndex = SF.frmValue.GetValueByName(split.Output).Index;
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
                SF.frmValue.setValueByIndex(i, splitArray[Startindex + i - SaveIndex]);
            }
            return true;

        }
        public bool RunReplace(ProcHandle evt, Replace replace)
        {
            string SourceValue = "";
            if (!string.IsNullOrEmpty(replace.SourceValueIndex))
                SourceValue = SF.frmValue.GetValueByIndex(int.Parse(replace.SourceValueIndex)).Value.ToString();
            else if (!string.IsNullOrEmpty(replace.SourceValue))
            {
                SourceValue = SF.frmValue.GetValueByName(replace.SourceValue).Value.ToString();
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
                replaceStr = SF.frmValue.GetValueByIndex(int.Parse(replace.ReplaceStrIndex)).Value.ToString();
            else if (!string.IsNullOrEmpty(replace.ReplaceStrV))
            {
                replaceStr = SF.frmValue.GetValueByName(replace.ReplaceStrV).Value.ToString();
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
                newStr = SF.frmValue.GetValueByIndex(int.Parse(replace.NewStrIndex)).Value.ToString();
            else if (!string.IsNullOrEmpty(replace.NewStrV))
            {
                newStr = SF.frmValue.GetValueByName(replace.NewStrV).Value.ToString();
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
                SF.frmValue.setValueByIndex(int.Parse(replace.OutputIndex),str);
            else if (!string.IsNullOrEmpty(replace.Output))
            {
                SF.frmValue.setValueByName(replace.Output, str);
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
            for (int i = 0; i < int.Parse(setDataStructItem.Count); i++)
            {
                SF.frmdataStruct.Set_StructItem_byIndex(int.Parse(setDataStructItem.StructIndex), int.Parse(setDataStructItem.ItemIndex), int.Parse(setDataStructItem.Params[i].valueIndex), setDataStructItem.Params[i].value);
            }
            return true;
        }

        public bool RunGetDataStructItem(ProcHandle evt, GetDataStructItem getDataStructItem)
        {

            if (getDataStructItem.IsAllItem)
            {
                int Startindex = SF.frmValue.GetValueByName(getDataStructItem.StartValue).Index;
                for (int i = 0; i < SF.frmdataStruct.Get_StructItemCount_byIndex(int.Parse(getDataStructItem.StructIndex), int.Parse(getDataStructItem.ItemIndex)); i++)
                {
                    object obj = SF.frmdataStruct.Get_StructItem_byIndex(int.Parse(getDataStructItem.StructIndex), int.Parse(getDataStructItem.ItemIndex), i);

                    string value = "";
                    if (!string.IsNullOrEmpty(getDataStructItem.Params[i].ValueIndex))
                        value = SF.frmValue.GetValueByIndex(int.Parse(getDataStructItem.Params[i].ValueIndex)).Value.ToString();
                    else
                        value = SF.frmValue.GetValueByName(getDataStructItem.Params[i].ValueName).Value.ToString();


                    SF.frmValue.setValueByIndex(Startindex + i, obj);

                }
            }
            else
            {
                for (int i = 0; i < getDataStructItem.Params.Count; i++)
                {
                    object obj = SF.frmdataStruct.Get_StructItem_byIndex(int.Parse(getDataStructItem.StructIndex), int.Parse(getDataStructItem.ItemIndex), int.Parse(getDataStructItem.Params[i].valueIndex));

                    string value = "";
                    if (!string.IsNullOrEmpty(getDataStructItem.Params[i].ValueIndex))
                        value = SF.frmValue.GetValueByIndex(int.Parse(getDataStructItem.Params[i].ValueIndex)).Value.ToString();
                    else
                        value = SF.frmValue.GetValueByName(getDataStructItem.Params[i].ValueName).Value.ToString();

                    SF.frmValue.setValueByName(value, obj);

                }
            }
            return true;
        }
        public bool RunCopyDataStructItem(ProcHandle evt, CopyDataStructItem copyDataStructItem)
        {
            if (copyDataStructItem.IsAllValue)
            {
                DataStructItem ds = SF.frmdataStruct.dataStructs[int.Parse(copyDataStructItem.SourceStructIndex)].dataStructItems[int.Parse(copyDataStructItem.SourceItemIndex)];
                SF.frmdataStruct.dataStructs[int.Parse(copyDataStructItem.TargetStructIndex)].dataStructItems[int.Parse(copyDataStructItem.TargetItemIndex)] = ds;
            }
            else
            {
                for (int i = 0; i < copyDataStructItem.Params.Count; i++)
                {
                    object obj = SF.frmdataStruct.Get_StructItem_byIndex(int.Parse(copyDataStructItem.SourceStructIndex), int.Parse(copyDataStructItem.SourceItemIndex), int.Parse(copyDataStructItem.Params[i].SourcevalueIndex));

                    SF.frmdataStruct.Set_StructItem_byIndex(int.Parse(copyDataStructItem.TargetStructIndex), int.Parse(copyDataStructItem.TargetItemIndex), int.Parse(copyDataStructItem.Params[i].Targetvalue), obj.ToString());

                }

            }
            return true;
        }
        public bool RunInsertDataStructItem(ProcHandle evt, InsertDataStructItem insertDataStructItem)
        {
            DataStructItem dataStructItem = new DataStructItem();
            dataStructItem.str = new Dictionary<int, string>();
            dataStructItem.num = new Dictionary<int, double>();
            dataStructItem.Name = insertDataStructItem.Name;
            SF.frmdataStruct.dataStructs[int.Parse(insertDataStructItem.TargetStructIndex)].dataStructItems.Insert(int.Parse(insertDataStructItem.TargetItemIndex), dataStructItem);
            for (int i = 0; i < insertDataStructItem.Params.Count; i++)
            {
                if (insertDataStructItem.Params[i].Type == "double")
                {
                    double num = -1;
                    if (insertDataStructItem.Params[i].ValueItem != null)
                        num = SF.frmValue.get_D_ValueByName(insertDataStructItem.Params[i].ValueItem);
                    else
                        num = double.Parse(insertDataStructItem.Params[i].Value);
                    SF.frmdataStruct.Set_StructItem_byIndex(int.Parse(insertDataStructItem.TargetStructIndex), int.Parse(insertDataStructItem.TargetItemIndex), i, num.ToString());
                }
                else
                {
                    string str = "";
                    if (insertDataStructItem.Params[i].ValueItem != null)
                        str = SF.frmValue.get_Str_ValueByName(insertDataStructItem.Params[i].ValueItem);
                    else
                        str = insertDataStructItem.Params[i].Value.ToString();

                    SF.frmdataStruct.Set_StructItem_byIndex(int.Parse(insertDataStructItem.TargetStructIndex), int.Parse(insertDataStructItem.TargetItemIndex), i, str);
                }
            }
            return true;
        }


        public bool RunDelDataStructItem(ProcHandle evt, DelDataStructItem delDataStructItem)
        {
            if (int.Parse(delDataStructItem.TargetStructIndex) >= 255)
                SF.frmdataStruct.dataStructs[int.Parse(delDataStructItem.TargetStructIndex)].dataStructItems.RemoveAt(SF.frmdataStruct.dataStructs[int.Parse(delDataStructItem.TargetStructIndex)].dataStructItems.Count - 1);
            else if (int.Parse(delDataStructItem.TargetStructIndex) <= -1)
            {
                if (SF.frmdataStruct.dataStructs[int.Parse(delDataStructItem.TargetStructIndex)].dataStructItems.Count != 0)
                    SF.frmdataStruct.dataStructs[int.Parse(delDataStructItem.TargetStructIndex)].dataStructItems.RemoveAt(0);
            }
            else
            {
                SF.frmdataStruct.dataStructs[int.Parse(delDataStructItem.TargetStructIndex)].dataStructItems.RemoveAt(int.Parse(delDataStructItem.TargetItemIndex));

            }
            return true;
        }

        public bool RunFindDataStructItem(ProcHandle evt, FindDataStructItem findDataStructItem)
        {
            if (findDataStructItem.Type == "名称等于key")
            {
                DataStructItem dst = SF.frmdataStruct.dataStructs[int.Parse(findDataStructItem.TargetStructIndex)].dataStructItems.FirstOrDefault(sc => sc.Name.ToString() == findDataStructItem.key);
                if (dst != null)
                    SF.frmValue.setValueByName(findDataStructItem.save, dst.Name);
            }
            else if (findDataStructItem.Type == "字符串等于key")
            {
                var matchingItems = SF.frmdataStruct.dataStructs[int.Parse(findDataStructItem.TargetStructIndex)].dataStructItems.Where(item => item.str.ContainsValue(findDataStructItem.key)).ToList();

                foreach (var item in matchingItems)
                {
                    foreach (var kvp in item.str)
                    {
                        if (kvp.Value == findDataStructItem.key)
                        {
                            SF.frmValue.setValueByName(findDataStructItem.save, kvp.Value);
                        }
                    }
                }
            }
            else if (findDataStructItem.Type == "数值等于key")
            {
                var matchingItems = SF.frmdataStruct.dataStructs[int.Parse(findDataStructItem.TargetStructIndex)].dataStructItems.Where(item => item.num.ContainsValue(double.Parse(findDataStructItem.key))).ToList();

                foreach (var item in matchingItems)
                {
                    foreach (var kvp in item.num)
                    {
                        if (kvp.Value == double.Parse(findDataStructItem.key))
                        {
                            SF.frmValue.setValueByName(findDataStructItem.save, kvp.Value);
                        }
                    }
                }
            }
            return true;
        }

        public bool RunGetDataStructCount(ProcHandle evt, GetDataStructCount getDataStructCount)
        {
            SF.frmValue.setValueByName(getDataStructCount.StructCount, SF.frmdataStruct.dataStructs.Count);
            SF.frmValue.setValueByName(getDataStructCount.ItemCount, SF.frmdataStruct.dataStructs[int.Parse(getDataStructCount.TargetStructIndex)].dataStructItems.Count);

            return true;
        }
        public bool RunTcpOps(ProcHandle evt, TcpOps tcpOps)
        {
            foreach (var op in tcpOps.Params)
            {
                SocketInfo socketInfo = SF.frmComunication.socketInfos.FirstOrDefault(sc => sc.Name == op.Name);
                if (op.Ops == "启动")
                {
                    if (socketInfo.Type.ToString() == "Client")
                    {
                        Task receTask = Task.Run(() => SF.frmComunication.TryConnect(socketInfo));
                    }
                    else
                    {
                        Task receTask = Task.Run(() => SF.frmComunication.StartServer(socketInfo));
                    }
                }
                else
                {
                    Socketer socketClient = SF.frmComunication.socketers.FirstOrDefault(sc => sc.SocketInfo.Name.ToString() == op.Name);
                    socketClient.socket.Dispose();
                    socketClient.socket.Close();
                    SF.frmComunication.socketers.Remove(socketClient);

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
                    Socketer socketer = SF.frmComunication.socketers.FirstOrDefault(sc => sc.SocketInfo.Name == op.Name);
                    if (socketer != null)
                    {
                        return true;
                    }
                    Delay(5, evt);
                }
            }
            return true;
        }

        public bool RunSendTcpMsg(ProcHandle evt, SendTcpMsg sendTcpMsg)
        {
            Socketer socketer = SF.frmComunication.socketers.FirstOrDefault(sc => sc.SocketInfo.Name == sendTcpMsg.ID);
            if (socketer != null)
            {
                socketer.isRun.Reset();
                SF.frmComunication.SendSocketMessage(socketer, SF.frmValue.get_Str_ValueByName(sendTcpMsg.Msg), sendTcpMsg);
            }
            return true;
        }

        public bool RunReceoveTcpMsg(ProcHandle evt, ReceoveTcpMsg receoveTcpMsg)
        {
            Socketer socketer = SF.frmComunication.socketers.FirstOrDefault(sc => sc.SocketInfo.Name == receoveTcpMsg.ID);
            if (socketer != null)
            {
                socketer.Msg = null;
                socketer.isRun.Set();
                int start = Environment.TickCount;
                while (!evt.isThStop && Math.Abs(Environment.TickCount - start) < receoveTcpMsg.TImeOut)
                {
                    if (socketer.Msg != null)
                    {
                        if (receoveTcpMsg.isConVert && int.TryParse(socketer.Msg, out int number))
                        {
                            socketer.Msg = Convert.ToString(number, 16).ToUpper();
                        }
                        SF.frmValue.setValueByName(receoveTcpMsg.MsgSaveValue, socketer.Msg);
                        return true;
                    }
                    Delay(5, evt);
                }
            }
            return true;
        }
        public bool RunSendSerialPortMsg(ProcHandle evt, SendSerialPortMsg sendSerialPortMsg)
        {
            SerialPorter serialPorter = SF.frmComunication.serialPorters.FirstOrDefault(sc => sc.serialPortInfo.Name == sendSerialPortMsg.ID);
            if (serialPorter != null)
            {
                serialPorter.isRun.Reset();
                SF.frmComunication.SendSerialPortMessage(serialPorter, SF.frmValue.get_Str_ValueByName(sendSerialPortMsg.Msg), sendSerialPortMsg);
            }
            return true;
        }
        public bool RunReceoveSerialPortMsg(ProcHandle evt, ReceoveSerialPortMsg receoveSerialPortMsg)
        {
            SerialPorter serialPorter = SF.frmComunication.serialPorters.FirstOrDefault(sc => sc.serialPortInfo.Name == receoveSerialPortMsg.ID);
            if (serialPorter != null)
            {
                serialPorter.Msg = null;
                serialPorter.isRun.Set();
                int start = Environment.TickCount;
                while (!evt.isThStop && Math.Abs(Environment.TickCount - start) < receoveSerialPortMsg.TImeOut)
                {
                    if (serialPorter.Msg != null)
                    {
                        if (int.TryParse(serialPorter.Msg, out int number))
                        {
                            serialPorter.Msg = Convert.ToString(number, 16).ToUpper();
                        }
                        SF.frmValue.setValueByName(receoveSerialPortMsg.MsgSaveValue, serialPorter.Msg);
                        return true;
                    }
                    Delay(5, evt);
                }
            }
            return true;
        }

        public bool RunSerialPortOps(ProcHandle evt, SerialPortOps serialPortOps)
        {
            foreach (var op in serialPortOps.Params)
            {
                SerialPortInfo serialPortInfo = SF.frmComunication.serialPortInfos.FirstOrDefault(sc => sc.Name == op.Name);
                if (op.Ops == "启动")
                {
                    Task receTask = Task.Run(() => SF.frmComunication.ConnectSerialPort(serialPortInfo));
                }
                else
                {
                    SerialPorter serialPorter = SF.frmComunication.serialPorters.FirstOrDefault(sc => sc.serialPortInfo.Name.ToString() == op.Name);
                    serialPorter.serialPort.Close();
                    SF.frmComunication.serialPorters.Remove(serialPorter);
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
                    SerialPorter serialPorter = SF.frmComunication.serialPorters.FirstOrDefault(sc => sc.serialPortInfo.Name == op.Name);
                    if (serialPorter != null)
                    {
                        return true;
                    }
                    Delay(5, evt);
                }
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

                                VelTemp = stationRunPos.Vel == 0 ? SF.frmValue.GetValueByName(stationRunPos.VelV).GetDValue() : stationRunPos.Vel;
                                AccTemp = stationRunPos.Acc == 0 ? SF.frmValue.GetValueByName(stationRunPos.AccV).GetDValue() : stationRunPos.Acc;
                                DecTemp = stationRunPos.Dec == 0 ? SF.frmValue.GetValueByName(stationRunPos.DecV).GetDValue() : stationRunPos.Dec;


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

                            time = SF.frmValue.GetValueByName(stationRunPos.timeOutV).GetDValue();
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

                            VelTemp = stationRunRel.Vel == 0 ? SF.frmValue.GetValueByName(stationRunRel.VelV).GetDValue() : stationRunRel.Vel;
                            AccTemp = stationRunRel.Acc == 0 ? SF.frmValue.GetValueByName(stationRunRel.AccV).GetDValue() : stationRunRel.Acc;
                            DecTemp = stationRunRel.Dec == 0 ? SF.frmValue.GetValueByName(stationRunRel.DecV).GetDValue() : stationRunRel.Dec;


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
                        DistanceTemp = TargetPos[i] == 0 ? SF.frmValue.GetValueByName(TargetPosV[i]).GetDValue() : TargetPos[i];

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

                        time = SF.frmValue.GetValueByName(stationRunRel.timeOutV).GetDValue();
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

                Vel = setStationVel.Vel == 0 ? SF.frmValue.GetValueByName(setStationVel.VelV).GetDValue() : setStationVel.Vel;
                Acc = setStationVel.Acc == 0 ? SF.frmValue.GetValueByName(setStationVel.AccV).GetDValue() : setStationVel.Acc;
                Dec = setStationVel.Dec == 0 ? SF.frmValue.GetValueByName(setStationVel.DecV).GetDValue() : setStationVel.Dec;

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

                    time = SF.frmValue.GetValueByName(waitStationStop.timeOutV).GetDValue();
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
    public class ProcHandle
    {
        public ManualResetEvent m_evtRun = new ManualResetEvent(false);
        public ManualResetEvent m_evtTik = new ManualResetEvent(false);
        public ManualResetEvent m_evtTok = new ManualResetEvent(false);

        public int procNum;
        public int stepNum;
        public int opsNum;

        public string procName;

        //   0  停止
        //   1  暂停
        //   2  运行
        public int isRun;
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
