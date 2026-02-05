using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Automation
{
    public partial class ProcessEngine
    {
        private const string WarmDisplayLogDirectory = @"D:\AutomationLogs\WarmDisplyLog";
        private const string WarmDisplayLogHeader = "报警代码\t报警内容\t报警类别\t开始时间\t结束时间\t报警时间(s)\t报警位置(x-x-x)";
        private static readonly object warmDisplayLogLock = new object();

        private void WriteWarmDisplayLog(ProcHandle evt, string alarmCode, string alarmContent, string alarmCategory, DateTime alarmStartTime, DateTime alarmEndTime)
        {
            if (evt == null)
            {
                throw new InvalidOperationException("报警位置为空");
            }

            TimeSpan alarmDuration = alarmEndTime - alarmStartTime;
            string alarmLocation = $"{evt.procNum}-{evt.stepNum}-{evt.opsNum}";
            string startText = alarmStartTime.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
            string endText = alarmEndTime.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
            string durationText = alarmDuration.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture);

            string EscapeField(string value)
            {
                if (string.IsNullOrEmpty(value))
                {
                    return string.Empty;
                }
                bool needQuote = value.IndexOfAny(new[] { '"', '\t', '\r', '\n' }) >= 0;
                string escaped = value.Replace("\"", "\"\"");
                return needQuote ? $"\"{escaped}\"" : escaped;
            }

            string logLine = string.Join("\t", new[]
            {
                EscapeField(alarmCode ?? string.Empty),
                EscapeField(alarmContent ?? string.Empty),
                EscapeField(alarmCategory ?? string.Empty),
                EscapeField(startText),
                EscapeField(endText),
                EscapeField(durationText),
                EscapeField(alarmLocation)
            });

            lock (warmDisplayLogLock)
            {
                if (string.IsNullOrWhiteSpace(WarmDisplayLogDirectory))
                {
                    throw new InvalidOperationException("报警提示日志路径无效");
                }
                if (File.Exists(WarmDisplayLogDirectory) && !Directory.Exists(WarmDisplayLogDirectory))
                {
                    string backupName = $"{WarmDisplayLogDirectory}_{DateTime.Now:yyyyMMddHHmmssfff}.bak";
                    File.Move(WarmDisplayLogDirectory, backupName);
                }
                Directory.CreateDirectory(WarmDisplayLogDirectory);
                string filePath = BuildWarmDisplayLogFilePath(alarmStartTime);
                bool needHeader = !File.Exists(filePath) || new FileInfo(filePath).Length == 0;
                using (FileStream stream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read))
                using (StreamWriter writer = new StreamWriter(stream, Encoding.UTF8))
                {
                    if (needHeader)
                    {
                        writer.WriteLine(WarmDisplayLogHeader);
                    }
                    writer.WriteLine(logLine);
                }
            }
        }

        private string BuildWarmDisplayLogFilePath(DateTime alarmStartTime)
        {
            string fileName = alarmStartTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".csv";
            return Path.Combine(WarmDisplayLogDirectory, fileName);
        }

        public bool RunPopupDialog(ProcHandle evt, PopupDialog popup)
        {
            if (popup == null)
            {
                MarkAlarm(evt, "弹框指令为空");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }
            CancellationToken token = evt?.CancellationToken ?? CancellationToken.None;
            AlarmInfo alarmInfo = null;

            bool hasSecondButton;
            bool hasThirdButton;
            switch (popup.PopupType)
            {
                case "弹是":
                    hasSecondButton = false;
                    hasThirdButton = false;
                    break;
                case "弹是与否":
                    hasSecondButton = true;
                    hasThirdButton = false;
                    break;
                case "弹是与否与取消":
                    hasSecondButton = true;
                    hasThirdButton = true;
                    break;
                default:
                    MarkAlarm(evt, $"弹框类型无效:{popup.PopupType}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
            }

            string messageText;
            string btn1Text = null;
            string btn2Text = null;
            string btn3Text = null;
            if (popup.InfoType == "报警信息库")
            {
                if (Context.AlarmInfoStore == null)
                {
                    MarkAlarm(evt, "报警信息库未初始化");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                if (string.IsNullOrWhiteSpace(popup.PopupAlarmInfoID))
                {
                    MarkAlarm(evt, "报警信息ID为空");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                if (!int.TryParse(popup.PopupAlarmInfoID, out int alarmIndex))
                {
                    MarkAlarm(evt, $"报警信息ID无效:{popup.PopupAlarmInfoID}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                if (!Context.AlarmInfoStore.TryGetByIndex(alarmIndex, out alarmInfo)
                    || alarmInfo == null
                    || string.IsNullOrWhiteSpace(alarmInfo.Name))
                {
                    MarkAlarm(evt, $"报警信息不存在:{popup.PopupAlarmInfoID}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                messageText = alarmInfo.Note;
                if (string.IsNullOrWhiteSpace(messageText))
                {
                    MarkAlarm(evt, $"报警信息内容为空:{popup.PopupAlarmInfoID}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                btn1Text = string.IsNullOrWhiteSpace(alarmInfo.Btn1) ? "确定" : alarmInfo.Btn1;
                btn2Text = string.IsNullOrWhiteSpace(alarmInfo.Btn2) ? "否" : alarmInfo.Btn2;
                btn3Text = string.IsNullOrWhiteSpace(alarmInfo.Btn3) ? "取消" : alarmInfo.Btn3;
            }
            else if (popup.InfoType == "自定义提示信息")
            {
                messageText = popup.PopupMessage;
                if (string.IsNullOrWhiteSpace(messageText))
                {
                    MarkAlarm(evt, "弹框提示信息为空");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
            }
            else if (popup.InfoType == "变量类型")
            {
                if (Context.ValueStore == null)
                {
                    MarkAlarm(evt, "变量库未初始化");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                if (string.IsNullOrWhiteSpace(popup.PopupMessageValue))
                {
                    MarkAlarm(evt, "提示变量为空");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                if (!Context.ValueStore.TryGetValueByName(popup.PopupMessageValue, out DicValue value) || value == null)
                {
                    MarkAlarm(evt, $"提示变量不存在:{popup.PopupMessageValue}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                if (!string.Equals(value.Type, "string", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(value.Type, "double", StringComparison.OrdinalIgnoreCase))
                {
                    MarkAlarm(evt, $"提示变量类型无效:{popup.PopupMessageValue}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                messageText = value.Value;
                if (string.IsNullOrWhiteSpace(messageText))
                {
                    MarkAlarm(evt, "提示变量内容为空");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
            }
            else
            {
                MarkAlarm(evt, $"提示信息类型无效:{popup.InfoType}");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }

            if (popup.InfoType != "报警信息库")
            {
                btn1Text = popup.Btn1Text;
                if (string.IsNullOrWhiteSpace(btn1Text))
                {
                    MarkAlarm(evt, "按钮1文本为空");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                if (hasSecondButton)
                {
                    btn2Text = popup.Btn2Text;
                    if (string.IsNullOrWhiteSpace(btn2Text))
                    {
                        MarkAlarm(evt, "按钮2文本为空");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }
                }
                if (hasThirdButton)
                {
                    btn3Text = popup.Btn3Text;
                    if (string.IsNullOrWhiteSpace(btn3Text))
                    {
                        MarkAlarm(evt, "按钮3文本为空");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }
                }
            }

            if (popup.DelayClose && popup.DelayCloseTimeMs <= 0)
            {
                MarkAlarm(evt, "延时关闭时间无效");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }

            if (popup.SaveToAlarmFile)
            {
                Logger?.Log($"弹框提示:{messageText}", LogLevel.Error);
            }

            string title = string.IsNullOrWhiteSpace(popup.Name) ? "提示" : popup.Name;
            bool alarmLightEnabled = string.Equals(popup.AlarmLightEnable, "启用", StringComparison.Ordinal);
            bool hasBuzzerIo = !string.IsNullOrWhiteSpace(popup.BuzzerIo);
            string buzzerTimeType = popup.BuzzerTimeType;

            void SetOutput(string ioName, bool value, string label)
            {
                if (string.IsNullOrWhiteSpace(ioName))
                {
                    return;
                }
                if (Context.IoMap == null || !Context.IoMap.TryGetValue(ioName, out IO io))
                {
                    MarkAlarm(evt, $"{label}未配置:{ioName}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
                if (Context.Motion == null || !Context.Motion.SetIO(io, value))
                {
                    MarkAlarm(evt, $"{label}输出失败:{ioName}");
                    throw CreateAlarmException(evt, evt?.alarmMsg);
                }
            }

            if (alarmLightEnabled)
            {
                SetOutput(popup.BuzzerIo, true, "蜂鸣器IO");
                SetOutput(popup.RedLightIo, true, "红灯IO");
                SetOutput(popup.YellowLightIo, true, "黄灯IO");
                SetOutput(popup.GreenLightIo, true, "绿灯IO");

                if (hasBuzzerIo)
                {
                    if (buzzerTimeType != "自定义时间" && buzzerTimeType != "持续蜂鸣")
                    {
                        MarkAlarm(evt, $"蜂鸣时间类型无效:{popup.BuzzerTimeType}");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }
                    if (buzzerTimeType == "自定义时间" && popup.BuzzerTimeMs <= 0)
                    {
                        MarkAlarm(evt, "蜂鸣时间无效");
                        throw CreateAlarmException(evt, evt?.alarmMsg);
                    }
                }
            }

            Control invoker = UiInvoker;
            if (invoker == null || invoker.IsDisposed)
            {
                MarkAlarm(evt, "弹框界面句柄无效");
                throw CreateAlarmException(evt, evt?.alarmMsg);
            }

            var tcs = new TaskCompletionSource<AlarmDecision>();
            Message dialog = null;

            void ShowDialog()
            {
                if (hasThirdButton)
                {
                    dialog = new Message(title, messageText,
                        () => tcs.TrySetResult(AlarmDecision.Goto1),
                        () => tcs.TrySetResult(AlarmDecision.Goto2),
                        () => tcs.TrySetResult(AlarmDecision.Goto3),
                        btn1Text, btn2Text, btn3Text, false);
                }
                else if (hasSecondButton)
                {
                    dialog = new Message(title, messageText,
                        () => tcs.TrySetResult(AlarmDecision.Goto1),
                        () => tcs.TrySetResult(AlarmDecision.Goto2),
                        btn1Text, btn2Text, false);
                }
                else
                {
                    dialog = new Message(title, messageText, () => tcs.TrySetResult(AlarmDecision.Goto1), btn1Text, false);
                }

                dialog.BackColor = popup.PopupBackColor;
                dialog.txtMsg.ForeColor = popup.PopupFontColor;
                dialog.txtMsg.BackColor = popup.PopupBackColor;
                if (UiInvoker is FrmMain main)
                {
                    int procIndex = evt?.procNum ?? -1;
                    main.RegisterProcPopup(procIndex, dialog, () =>
                    {
                        if (!tcs.Task.IsCompleted)
                        {
                            tcs.TrySetResult(AlarmDecision.Ignore);
                        }
                        if (dialog.IsDisposed)
                        {
                            return;
                        }
                        if (dialog.InvokeRequired)
                        {
                            dialog.BeginInvoke((Action)(() => dialog.btnCanel()));
                        }
                        else
                        {
                            dialog.btnCanel();
                        }
                    });
                }

                if (popup.DelayClose)
                {
                    System.Windows.Forms.Timer closeTimer = new System.Windows.Forms.Timer();
                    closeTimer.Interval = popup.DelayCloseTimeMs;
                    closeTimer.Tick += (sender, args) =>
                    {
                        closeTimer.Stop();
                        closeTimer.Dispose();
                        if (dialog != null && !dialog.IsDisposed && !dialog.isChoiced)
                        {
                            dialog.btnCanel();
                        }
                        tcs.TrySetResult(AlarmDecision.Ignore);
                    };
                    closeTimer.Start();
                }
            }

            DateTime alarmStartTime = DateTime.Now;
            if (invoker.InvokeRequired)
            {
                invoker.BeginInvoke((Action)ShowDialog);
            }
            else
            {
                ShowDialog();
            }

            AlarmDecision decision = AlarmDecision.Ignore;
            Task<AlarmDecision> decisionTask = tcs.Task;
            bool isCanceled = false;

            try
            {
                if (alarmLightEnabled && hasBuzzerIo && buzzerTimeType == "自定义时间")
                {
                    Task delayTask = Task.Delay(popup.BuzzerTimeMs, token);
                    Task completed = Task.WhenAny(decisionTask, delayTask).GetAwaiter().GetResult();
                    if (completed == delayTask)
                    {
                        SetOutput(popup.BuzzerIo, false, "蜂鸣器IO");
                    }
                }

                if (evt != null && evt.CancellationToken.CanBeCanceled)
                {
                    decisionTask.Wait(evt.CancellationToken);
                }
                else
                {
                    decisionTask.Wait();
                }
                decision = decisionTask.Result;
            }
            catch (OperationCanceledException)
            {
                if (dialog != null && !dialog.IsDisposed)
                {
                    if (dialog.InvokeRequired)
                    {
                        dialog.BeginInvoke((Action)(() => dialog.btnCanel()));
                    }
                    else
                    {
                        dialog.btnCanel();
                    }
                }
                tcs.TrySetResult(AlarmDecision.Ignore);
                isCanceled = true;
            }
            finally
            {
                if (alarmLightEnabled)
                {
                    SetOutput(popup.BuzzerIo, false, "蜂鸣器IO");
                    SetOutput(popup.RedLightIo, false, "红灯IO");
                    SetOutput(popup.YellowLightIo, false, "黄灯IO");
                    SetOutput(popup.GreenLightIo, false, "绿灯IO");
                }
            }

            if (popup.SaveToAlarmFile)
            {
                try
                {
                    WriteWarmDisplayLog(
                        evt,
                        alarmInfo?.Name ?? string.Empty,
                        messageText ?? string.Empty,
                        alarmInfo?.Category ?? string.Empty,
                        alarmStartTime,
                        DateTime.Now);
                }
                catch (Exception ex)
                {
                    MarkAlarm(evt, $"报警提示日志写入失败:{ex.Message}");
                    throw CreateAlarmException(evt, evt?.alarmMsg, ex);
                }
            }

            if (isCanceled)
            {
                return false;
            }

            switch (decision)
            {
                case AlarmDecision.Goto1:
                    if (!string.IsNullOrWhiteSpace(popup.PopupGoto1))
                    {
                        evt.isGoto = true;
                        if (!TryExecuteGoto(popup.PopupGoto1, evt, out string gotoError))
                        {
                            MarkAlarm(evt, gotoError);
                            throw CreateAlarmException(evt, evt?.alarmMsg);
                        }
                    }
                    break;
                case AlarmDecision.Goto2:
                    if (!string.IsNullOrWhiteSpace(popup.PopupGoto2))
                    {
                        evt.isGoto = true;
                        if (!TryExecuteGoto(popup.PopupGoto2, evt, out string gotoError))
                        {
                            MarkAlarm(evt, gotoError);
                            throw CreateAlarmException(evt, evt?.alarmMsg);
                        }
                    }
                    break;
                case AlarmDecision.Goto3:
                    if (!string.IsNullOrWhiteSpace(popup.PopupGoto3))
                    {
                        evt.isGoto = true;
                        if (!TryExecuteGoto(popup.PopupGoto3, evt, out string gotoError))
                        {
                            MarkAlarm(evt, gotoError);
                            throw CreateAlarmException(evt, evt?.alarmMsg);
                        }
                    }
                    break;
            }

            return true;
        }
    }
}
