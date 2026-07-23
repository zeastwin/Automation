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
        public bool RunCustomFunc(ProcHandle evt, CallCustomFunc callCustomFunc)
        {
            string funcName = callCustomFunc.FunctionName;

            if (Context.CustomFunc == null)
            {
                MarkAlarm(evt, "自定义函数未初始化");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }

            CustomFunc.FunctionDelegate function =
                callCustomFunc.RuntimeBinding as CustomFunc.FunctionDelegate;
            if (function == null
                && !Context.CustomFunc.TryGetFunction(funcName, out function))
            {
                MarkAlarm(evt, $"找不到自定义函数:{funcName}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            callCustomFunc.RuntimeBinding = function;
            function();
            return true;
        }
    }
}
