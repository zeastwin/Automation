using System;
using System.Drawing;
using System.Windows.Forms;
using Automation.DeviceSdk;

namespace Automation.Hmi
{
    /// <summary>
    /// RuntimeSummaryPanel —— 调试页面顶部面板
    /// 显示平台状态、流程总数以及运行中/暂停/停止的流程数量。
    /// 数据仅通过 IAutomationPlatform 公开能力获取。
    /// </summary>
    public class RuntimeSummaryPanel : Panel
    {
        private readonly Label lblPlatformStateTitle;
        private readonly Label lblPlatformStateValue;
        private readonly Label lblTotalTitle;
        private readonly Label lblTotalValue;
        private readonly Label lblRunningTitle;
        private readonly Label lblRunningValue;
        private readonly Label lblPausedTitle;
        private readonly Label lblPausedValue;
        private readonly Label lblStoppedTitle;
        private readonly Label lblStoppedValue;

        private int lastTotal = -1;
        private int lastRunning = -1;
        private int lastPaused = -1;
        private int lastStopped = -1;
        private string lastStateText = string.Empty;

        public RuntimeSummaryPanel()
        {
            BackColor = Color.FromArgb(24, 38, 54);
            MinimumSize = new Size(0, 80);
            Height = 80;

            // 平台状态
            lblPlatformStateTitle = CreateStatTitle("平台状态");
            lblPlatformStateValue = CreateStatValue("--");

            // 流程总数
            lblTotalTitle = CreateStatTitle("流程总数");
            lblTotalValue = CreateStatValue("--");

            // 运行中
            lblRunningTitle = CreateStatTitle("运行中");
            lblRunningValue = CreateStatValue("--");

            // 暂停
            lblPausedTitle = CreateStatTitle("暂停");
            lblPausedValue = CreateStatValue("--");

            // 停止
            lblStoppedTitle = CreateStatTitle("停止");
            lblStoppedValue = CreateStatValue("--");

            // 用 FlowLayoutPanel 实现水平等间距排列
            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(12, 0, 12, 0),
                BackColor = Color.Transparent
            };

            flow.Controls.Add(MakeStatGroup(lblPlatformStateTitle, lblPlatformStateValue));
            flow.Controls.Add(CreateSeparator());
            flow.Controls.Add(MakeStatGroup(lblTotalTitle, lblTotalValue));
            flow.Controls.Add(CreateSeparator());
            flow.Controls.Add(MakeStatGroup(lblRunningTitle, lblRunningValue));
            flow.Controls.Add(CreateSeparator());
            flow.Controls.Add(MakeStatGroup(lblPausedTitle, lblPausedValue));
            flow.Controls.Add(CreateSeparator());
            flow.Controls.Add(MakeStatGroup(lblStoppedTitle, lblStoppedValue));

            Controls.Add(flow);
        }

        /// <summary>
        /// 用 IAutomationPlatform 的数据刷新状态显示
        /// </summary>
        public void RefreshFromHost(IAutomationPlatform platform)
        {
            if (platform == null)
                return;

            // 平台状态
            string stateText = platform.RuntimeStatus switch
            {
                PlatformRuntimeStatus.Created => "已创建",
                PlatformRuntimeStatus.Initializing => "初始化中",
                PlatformRuntimeStatus.Ready => "就绪",
                PlatformRuntimeStatus.Faulted => $"故障: {platform.RuntimeMessage}",
                PlatformRuntimeStatus.ShuttingDown => "关闭中",
                PlatformRuntimeStatus.Stopped => "已停止",
                _ => "未知"
            };
            if (stateText != lastStateText)
            {
                lblPlatformStateValue.Text = stateText;
                lastStateText = stateText;
            }

            // 获取全部流程信息
            try
            {
                var processes = platform.Processes.GetAll();
                int total = processes.Count;
                int running = 0, paused = 0, stopped = 0;
                foreach (var proc in processes)
                {
                    switch (proc.State)
                    {
                        case ProcessRuntimeStatus.Running:
                            running++;
                            break;
                        case ProcessRuntimeStatus.Paused:
                        case ProcessRuntimeStatus.Pausing:
                            paused++;
                            break;
                        case ProcessRuntimeStatus.Stopped:
                        case ProcessRuntimeStatus.Stopping:
                            stopped++;
                            break;
                        case ProcessRuntimeStatus.Alarming:
                            // 报警状态按运行中计数（仍在执行但处于报警）
                            running++;
                            break;
                        case ProcessRuntimeStatus.SingleStep:
                            running++;
                            break;
                    }
                }

                if (total != lastTotal)
                {
                    lblTotalValue.Text = total.ToString();
                    lastTotal = total;
                }
                if (running != lastRunning)
                {
                    lblRunningValue.Text = running.ToString();
                    lastRunning = running;
                }
                if (paused != lastPaused)
                {
                    lblPausedValue.Text = paused.ToString();
                    lastPaused = paused;
                }
                if (stopped != lastStopped)
                {
                    lblStoppedValue.Text = stopped.ToString();
                    lastStopped = stopped;
                }
            }
            catch
            {
                // 平台尚未就绪时静默处理
            }
        }

        private static Label CreateStatTitle(string text)
        {
            return new Label
            {
                Text = text,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular),
                ForeColor = Color.FromArgb(150, 180, 210),
                TextAlign = ContentAlignment.BottomCenter,
                Dock = DockStyle.Fill,
                AutoSize = false,
                Height = 22
            };
        }

        private static Label CreateStatValue(string text)
        {
            return new Label
            {
                Text = text,
                Font = new Font("Microsoft YaHei UI", 20F, FontStyle.Bold),
                ForeColor = Color.White,
                TextAlign = ContentAlignment.TopCenter,
                Dock = DockStyle.Fill,
                AutoSize = false,
                Height = 40
            };
        }

        private static Panel MakeStatGroup(Label title, Label value)
        {
            var group = new Panel
            {
                Width = 120,
                Height = 70,
                BackColor = Color.Transparent
            };
            title.Top = 4;
            title.Left = 0;
            title.Width = group.Width;
            value.Top = 24;
            value.Left = 0;
            value.Width = group.Width;
            group.Controls.Add(title);
            group.Controls.Add(value);
            return group;
        }

        private static Panel CreateSeparator()
        {
            return new Panel
            {
                Width = 1,
                Height = 50,
                BackColor = Color.FromArgb(60, 80, 100),
                Margin = new Padding(0, 10, 0, 10)
            };
        }
    }
}
