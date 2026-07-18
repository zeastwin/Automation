using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace Automation
{
    /// <summary>
    /// 独立性能分析页面。WebView2 只负责呈现，每秒读取一次流程引擎已有的性能快照。
    /// </summary>
    public sealed class FrmPerformanceAnalysis : Form
    {
        private const string PageResourceName = "Automation.Assets.PerformanceAnalysis.index.html";

        private readonly FrmMain owner;
        private readonly WebView2 webView;
        private readonly Panel fallbackPanel;
        private readonly Label fallbackMessage;
        private readonly Timer refreshTimer = new Timer { Interval = 1000 };
        private CoreWebView2 coreWebView;
        private bool pageReady;

        public FrmPerformanceAnalysis(FrmMain owner)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            Text = "运行时性能分析";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1050, 680);
            Size = new Size(1420, 860);
            BackColor = UiPalette.Background;
            Font = new Font("Microsoft YaHei UI", 9F);
            UiBranding.Apply(this);

            webView = new WebView2
            {
                Dock = DockStyle.Fill,
                CreationProperties = new CoreWebView2CreationProperties
                {
                    UserDataFolder = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Automation",
                        "WebView2")
                }
            };
            fallbackMessage = new Label
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(48),
                Font = new Font("Microsoft YaHei UI", 11F),
                ForeColor = UiPalette.TextSecondary,
                TextAlign = ContentAlignment.MiddleCenter
            };
            fallbackPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(80),
                BackColor = UiPalette.Background,
                Visible = false
            };
            fallbackPanel.Controls.Add(fallbackMessage);
            Controls.Add(fallbackPanel);
            Controls.Add(webView);

            Load += HandleLoad;
            Shown += (sender, args) =>
            {
                PublishSnapshot();
                refreshTimer.Start();
            };
            FormClosed += HandleFormClosed;
            refreshTimer.Tick += (sender, args) => PublishSnapshot();
        }

        private async void HandleLoad(object sender, EventArgs e)
        {
            try
            {
                await webView.EnsureCoreWebView2Async();
                coreWebView = webView.CoreWebView2;
                coreWebView.Settings.AreDefaultContextMenusEnabled = false;
                coreWebView.Settings.AreDevToolsEnabled = false;
                coreWebView.Settings.AreBrowserAcceleratorKeysEnabled = false;
                coreWebView.WebMessageReceived += HandleWebMessageReceived;
                coreWebView.NavigateToString(ReadPageHtml());
            }
            catch (Exception ex)
            {
                DisableWebPage("性能分析页面初始化失败：" + ex.Message, ex);
            }
        }

        private void HandleWebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                JObject request = JObject.Parse(e.TryGetWebMessageAsString());
                if (string.Equals(request["action"]?.Value<string>(), "ready", StringComparison.Ordinal))
                {
                    pageReady = true;
                    PublishSnapshot();
                }
            }
            catch (Exception ex)
            {
                owner.dataRun?.Logger?.Log("性能分析页面交互失败：" + ex.Message, LogLevel.Error);
            }
        }

        private void PublishSnapshot()
        {
            if (!pageReady || coreWebView == null || IsDisposed) return;
            try
            {
                IReadOnlyList<EngineSnapshot> snapshots = owner.dataRun?.GetSnapshots()
                    ?? Array.Empty<EngineSnapshot>();
                var processes = new JArray();
                double totalRate = 0;
                double totalCpu = 0;
                int runningCount = 0;
                int abnormalCount = 0;
                long totalOperations = 0;
                foreach (EngineSnapshot snapshot in snapshots.OrderBy(item => item.ProcIndex))
                {
                    ProcessPerformanceSnapshot performance = snapshot.Performance;
                    double rate = performance?.OperationsPerSecond ?? 0;
                    double cpu = performance?.ThreadCpuPercent ?? 0;
                    bool abnormal = performance?.AbnormalCpuLoopDetected == true;
                    totalRate += rate;
                    totalCpu += cpu;
                    totalOperations += performance?.OperationCount ?? 0;
                    if (snapshot.State != ProcRunState.Stopped) runningCount++;
                    if (abnormal) abnormalCount++;
                    processes.Add(new JObject
                    {
                        ["index"] = snapshot.ProcIndex,
                        ["name"] = string.IsNullOrWhiteSpace(snapshot.ProcName)
                            ? $"流程{snapshot.ProcIndex}"
                            : snapshot.ProcName,
                        ["state"] = snapshot.State.ToString(),
                        ["stateText"] = LocalizeState(snapshot.State),
                        ["operationCount"] = performance?.OperationCount ?? 0,
                        ["operationsPerSecond"] = rate,
                        ["threadCpuPercent"] = cpu,
                        ["averageOperationMicroseconds"] = performance?.AverageOperationMicroseconds ?? 0,
                        ["maxOperationMicroseconds"] = performance?.MaxOperationMicroseconds ?? 0,
                        ["sampleCount"] = performance?.OperationDurationSampleCount ?? 0,
                        ["samplingInterval"] = performance?.OperationDurationSamplingInterval ?? 0,
                        ["abnormal"] = abnormal,
                        ["alarmMessage"] = snapshot.AlarmMessage ?? string.Empty
                    });
                }

                AutomationRuntimeOptions options = AutomationRuntimeOptions.Current;
                var payload = new JObject
                {
                    ["type"] = "performanceSnapshot",
                    ["analysisEnabled"] = options.PerformanceAnalysisEnabled,
                    ["runtimeDiagnosticsEnabled"] = owner.RuntimeDiagnosticsEnabled,
                    ["updatedAt"] = DateTime.Now.ToString("HH:mm:ss"),
                    ["totalOperations"] = totalOperations,
                    ["totalOperationsPerSecond"] = totalRate,
                    ["totalThreadCpuPercent"] = totalCpu,
                    ["runningCount"] = runningCount,
                    ["abnormalCount"] = abnormalCount,
                    ["processes"] = processes
                };
                coreWebView.PostWebMessageAsString(payload.ToString(Formatting.None));
            }
            catch (Exception ex)
            {
                owner.dataRun?.Logger?.Log("刷新性能分析页面失败：" + ex.Message, LogLevel.Error);
            }
        }

        private void HandleFormClosed(object sender, FormClosedEventArgs e)
        {
            refreshTimer.Stop();
            refreshTimer.Dispose();
            if (coreWebView != null)
            {
                coreWebView.WebMessageReceived -= HandleWebMessageReceived;
                coreWebView = null;
            }
        }

        private void DisableWebPage(string message, Exception exception)
        {
            refreshTimer.Stop();
            fallbackMessage.Text = "运行时性能分析暂不可用\r\n\r\n" + message;
            fallbackPanel.Visible = true;
            fallbackPanel.BringToFront();
            webView.Visible = false;
            owner.dataRun?.Logger?.Log(message + "\r\n" + exception, LogLevel.Error);
        }

        private static string ReadPageHtml()
        {
            Assembly assembly = typeof(FrmPerformanceAnalysis).Assembly;
            using (Stream stream = assembly.GetManifestResourceStream(PageResourceName)
                ?? throw new InvalidOperationException("性能分析页面资源缺失。"))
            using (var reader = new StreamReader(stream))
            {
                return reader.ReadToEnd();
            }
        }

        private static string LocalizeState(ProcRunState state)
        {
            switch (state)
            {
                case ProcRunState.Running: return "运行中";
                case ProcRunState.Paused: return "已暂停";
                case ProcRunState.Pausing: return "暂停中";
                case ProcRunState.SingleStep: return "单步";
                case ProcRunState.Alarming: return "报警";
                default: return "已停止";
            }
        }
    }
}
