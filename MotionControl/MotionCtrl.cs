using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using csLTDMC;
using static Automation.MotionControl.MotionCtrl;

namespace Automation.MotionControl
{
    public class MotionCtrl : IMotionRuntime, IIoRuntime
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
        public bool IsCardInitialized { get; private set; }

        private void EnsureCardInitialized()
        {
            if (!IsCardInitialized || ls == null)
            {
                throw new InvalidOperationException("运动控制卡未初始化");
            }
        }

        private static bool EnsureResetCompleted()
        {
            if (SF.SecurityLocked
                || SF.valueStore == null
                || !SF.valueStore.TryGetValueByName("复位状态", out DicValue resetValue)
                || resetValue == null
                || !string.Equals(resetValue.Type, "double", StringComparison.OrdinalIgnoreCase)
                || !double.TryParse(resetValue.Value, out double resetRaw)
                || resetRaw != (double)ResetStatus.ResetCompleted)
            {
                const string message = "系统尚未复位完成，禁止手动运动。";
                if (Application.MessageLoop)
                {
                    MessageBox.Show(message, "运动门禁", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
                throw new InvalidOperationException(message);
            }
            return true;
        }

        private static bool TryAcquireManualMotionResource(ushort card, ushort axis)
        {
            if (!Application.MessageLoop)
            {
                return true;
            }
            if (SF.DR == null)
            {
                MessageBox.Show("流程内核未初始化，禁止手动运动。", "运动门禁", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (SF.DR.TryAcquireManualMotionResource(card, axis, out string error))
            {
                return true;
            }
            MessageBox.Show(error, "运动资源占用", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
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
            settHomeParam?.Invoke(card, axis,  dir,  speed,  homeMode);
        }
        public void StartHome(ushort card, ushort axis)
        {
            EnsureCardInitialized();
            if (!EnsureResetCompleted())
            {
                return;
            }
            if (!TryAcquireManualMotionResource(card, axis))
            {
                return;
            }
            try
            {
                startHome?.Invoke(card , axis);
            }
            catch
            {
                SF.DR?.ReleaseManualMotionResource(card, axis);
                throw;
            }
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
            setMovParam?.Invoke(card,axis, minVel, dMaxVel, acc, dec, dStopVel,dS_para, equiv);
        }
        public void Mov(ushort card, ushort axis, double dDist, ushort sPosi_mode, bool wait)
        {
            EnsureCardInitialized();
            if (!EnsureResetCompleted())
            {
                return;
            }
            if (!TryAcquireManualMotionResource(card, axis))
            {
                return;
            }
            bool manualOperation = Application.MessageLoop;
            try
            {
                mov?.Invoke(card, axis, dDist, sPosi_mode, wait);
                if (wait)
                {
                    SF.DR?.ReleaseManualMotionResource(card, axis);
                }
                else if (manualOperation)
                {
                    _ = MonitorManualMoveCompletionAsync(card, axis);
                }
            }
            catch
            {
                SF.DR?.ReleaseManualMotionResource(card, axis);
                throw;
            }
        }

        private async Task MonitorManualMoveCompletionAsync(ushort card, ushort axis)
        {
            bool safeToRelease = false;
            try
            {
                DateTime deadline = DateTime.UtcNow.AddSeconds(120);
                while (DateTime.UtcNow < deadline)
                {
                    if ((bool)getInPos?.Invoke(card, axis))
                    {
                        safeToRelease = true;
                        return;
                    }
                    await Task.Delay(10).ConfigureAwait(false);
                }
                throw new TimeoutException($"手动运动超时:{card}-{axis}");
            }
            catch (Exception ex)
            {
                try
                {
                    stopOneAxis?.Invoke(card, axis, 0);
                    SF.DR?.ReleaseManualMotionResource(card, axis);
                    safeToRelease = false;
                    SF.SetSecurityLock($"手动运动监控异常，轴已停止:{card}-{axis} {ex.Message}");
                }
                catch (Exception stopException)
                {
                    SF.SetSecurityLock($"手动运动监控失败且停止轴失败:{card}-{axis} {ex.Message}; {stopException.Message}");
                }
            }
            finally
            {
                if (safeToRelease)
                {
                    SF.DR?.ReleaseManualMotionResource(card, axis);
                }
            }
        }
        public void Jog(ushort card, ushort axis, ushort sDir)
        {
            EnsureCardInitialized();
            if (!EnsureResetCompleted())
            {
                return;
            }
            if (!TryAcquireManualMotionResource(card, axis))
            {
                return;
            }
            try
            {
                jog?.Invoke(card, axis, sDir);
            }
            catch
            {
                SF.DR?.ReleaseManualMotionResource(card, axis);
                throw;
            }
        }
        public void StopOneAxis(ushort card, ushort axis, ushort stop_mode)
        {
            EnsureCardInitialized();
            stopOneAxis?.Invoke(card, axis,  stop_mode);
            SF.DR?.ReleaseManualMotionResource(card, axis);
        }
        public void StopConnect()
        {
            stopConnect?.Invoke();
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
        public uint GetAxisIoStatus(ushort card, ushort axis)
        {
            if (!IsCardInitialized)
            {
                throw new InvalidOperationException("运动控制卡未初始化");
            }
            return LTDMC.dmc_axis_io_status(card, axis);
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
