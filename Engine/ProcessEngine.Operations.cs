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
        public bool ExecuteOperation(ProcHandle evt, object operation)
        {
            if (operation == null)
            {
                string message = "操作为空";
                MarkAlarm(evt, message);
                Logger?.Log(message, LogLevel.Error);
                return false;
            }
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

                    case PopupDialog popupDialog:
                        return RunPopupDialog(evt, popupDialog);

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
                        {
                            string message = $"操作类型不支持:{operation.GetType().Name}";
                            MarkAlarm(evt, message);
                            Logger?.Log(message, LogLevel.Error);
                            return false;
                        }
                }
            }
            catch (Exception ex)
            {
                MarkAlarm(evt, ex.Message);
                Logger?.Log(ex.Message, LogLevel.Error);
                return false;
            }
        }

    }
}
