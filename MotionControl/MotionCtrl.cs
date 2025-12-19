using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Automation.MotionControl.MotionCtrl;

namespace Automation.MotionControl
{
    public class MotionCtrl
    {
        public LS ls;

        public delegate ushort InitCardHandler();
        public delegate bool SetIOHandler(IO io, bool isOpen);
        public delegate bool GetOutIOHandler(IO io, ref bool value);
        public delegate bool GetInIOHandler(IO io, ref bool value);
        public delegate void SettHomeParamHandler(ushort card,ushort axis, ushort dir, ushort speed, ushort homeMode);
        public delegate void StartHomeHandler(ushort card, ushort axis);
        public delegate void CleanPosHandler(ushort card, ushort axis);
        public delegate double GetAxisPosHandler(ushort card, ushort axis);
        public delegate void SetMovParamHandler(ushort card,ushort axis, double minVel, double dMaxVel, double acc, double dec, double dStopVel, double dS_para,int equiv);
        public delegate void MovHandler(ushort card, ushort axis, double dDist, ushort sPosi_mode, bool wait);
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
        public delegate void CleanAlarmHandler();
        public delegate double GetAxisCurSpeedHandler(ushort card, ushort axis);

        public event InitCardHandler initCard;
        public event SetIOHandler setIO;
        public event GetOutIOHandler getOutIO;
        public event GetInIOHandler getInIO;
        public event SettHomeParamHandler settHomeParam;
        public event StartHomeHandler startHome;
        public event CleanPosHandler cleanPos;
        public event GetAxisPosHandler getAxisPos;
        public event SetMovParamHandler setMovParam;
        public event MovHandler mov;
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
        public event CleanAlarmHandler cleanAlarm;
        public event GetAxisCurSpeedHandler getAxisCurSpeed;

        public void InitCard()
        {
            initCard?.Invoke();
        }
        public bool SetIO(IO io, bool isOpen)
        {
            return (bool)setIO?.Invoke(io, isOpen);
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
            settHomeParam?.Invoke(card, axis,  dir,  speed,  homeMode);
        }
        public void StartHome(ushort card, ushort axis)
        {
            startHome?.Invoke(card , axis);
        }
        public void CleanPos(ushort card, ushort axis)
        {
            cleanPos?.Invoke(card, axis);
        }
        public double GetAxisPos(ushort card, ushort axis)
        {
           return (double)(getAxisPos?.Invoke(card, axis));
        }
        public void SetMovParam(ushort card ,ushort axis, double minVel, double dMaxVel, double acc, double dec, double dStopVel, double dS_para,int equiv)
        {
            setMovParam?.Invoke(card,axis, minVel, dMaxVel, acc, dec, dStopVel,dS_para, equiv);
        }
        public void Mov(ushort card, ushort axis, double dDist, ushort sPosi_mode, bool wait)
        {
            mov?.Invoke( card,axis,  dDist,  sPosi_mode,  wait);
        }
        public void Jog(ushort card, ushort axis, ushort sDir)
        {
            jog?.Invoke( card,axis,  sDir);
        }
        public void StopOneAxis(ushort card, ushort axis, ushort stop_mode)
        {
            stopOneAxis?.Invoke(card, axis,  stop_mode);
        }
        public void StopConnect()
        {
            stopConnect?.Invoke();
        }
        public bool HomeStatus(ushort card, ushort axis)
        {
            return (bool)homeStatus?.Invoke(card, axis);
        }
        public bool GetInPos(ushort card, ushort axis)
        {
            return (bool)getInPos?.Invoke(card, axis);
        }
        public bool GetAxisSevon(ushort card, ushort axis)
        {
            return (bool)getAxisSevon?.Invoke(card, axis);
        }
        public void SetAxisSevon(ushort card, ushort axis, bool isSevon)
        {
             setAxisSevon?.Invoke(card, axis,isSevon);
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
        public void CleanAlarm()
        {
            cleanAlarm?.Invoke();
        }
        public double GetAxisCurSpeed(ushort card, ushort axis)
        {
            return (double)getAxisCurSpeed?.Invoke( card, axis);
        }
        public void InitCardType()
        {
            ls = new LS();
            initCard = ls.InitCard;
            setIO = ls.SetIO;
            getInIO = ls.GetInIO;
            getOutIO = ls.GetOutIO;
            settHomeParam = ls.SettHomeParam;
            startHome = ls.StartHome;
            cleanPos += ls.CleanPos;
            cleanPos += ls.CleanPosEncoder;
            getAxisPos = ls.GetAxisPosEncoder;
            setMovParam = ls.SetMovParam;
            mov = ls.Mov;
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
            cleanAlarm = ls.CleanAlarm;
            getAxisCurSpeed = ls.GetAxisCurSpeed;
        }

    }
}
