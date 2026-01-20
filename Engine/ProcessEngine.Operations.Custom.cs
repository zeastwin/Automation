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
        public bool RunCustomFunc(ProcHandle evt, CallCustomFunc callCustomFunc)
        {
            string funcName = callCustomFunc.Name;

            if (Context.CustomFunc == null)
            {
                evt.isAlarm = true;
                evt.alarmMsg = "自定义函数未初始化";
                throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
            }

            bool success = Context.CustomFunc.RunFunc(funcName);
            if (!success)
            {
                evt.isAlarm = true;
                evt.alarmMsg = $"找不到自定义函数:{funcName}";
                throw new InvalidOperationException(evt?.alarmMsg ?? "执行失败");
            }
            return true;
        }
    }
}
