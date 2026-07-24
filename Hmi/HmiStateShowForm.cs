// 模块：平台内置 HMI / Hive 状态显示。
// 职责范围：复刻旧项目 StateShow 的六态设备状态窗口，通过公开平台变量读取当前状态。

using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using Automation.DeviceSdk;

namespace Automation.Hmi
{
    internal sealed class HmiStateShowForm : Form
    {
        private readonly IAutomationPlatform platform;
        private readonly Timer refreshTimer;
        private readonly Button[] stateButtons;

        internal HmiStateShowForm()
            : this(null)
        {
        }

        internal HmiStateShowForm(IAutomationPlatform platform)
        {
            this.platform = platform;
            Text = "StateShow";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(385, 496);
            MinimumSize = new Size(320, 420);
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Microsoft YaHei UI", 9F);

            var layout = new TableLayoutPanel
            {
                Name = "stateLayout",
                BackColor = SystemColors.ControlLightLight,
                CellBorderStyle = TableLayoutPanelCellBorderStyle.OutsetDouble,
                ColumnCount = 1,
                RowCount = 6,
                Dock = DockStyle.Fill,
                Margin = new Padding(2)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (int index = 0; index < 6; index++)
            {
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F / 6F));
            }

            string[] stateNames =
            {
                "Running",
                "Planned downtime",
                "Idle",
                "Engineering",
                "Manually Downtime",
                "ResetError"
            };
            stateButtons = new Button[stateNames.Length];
            for (int index = 0; index < stateNames.Length; index++)
            {
                var button = new Button
                {
                    Name = "stateButton" + index.ToString(CultureInfo.InvariantCulture),
                    Dock = DockStyle.Fill,
                    Margin = new Padding(2),
                    Font = new Font("Microsoft YaHei UI", 14F),
                    Text = stateNames[index],
                    UseVisualStyleBackColor = false
                };
                stateButtons[index] = button;
                layout.Controls.Add(button, 0, index);
            }
            Controls.Add(layout);

            refreshTimer = new Timer { Interval = 250 };
            refreshTimer.Tick += RefreshTimer_Tick;
            Shown += (sender, args) =>
            {
                RefreshState();
                refreshTimer.Start();
            };
            FormClosing += (sender, args) => refreshTimer.Stop();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                refreshTimer?.Dispose();
            }
            base.Dispose(disposing);
        }

        private void RefreshTimer_Tick(object sender, EventArgs e)
        {
            RefreshState();
        }

        private void RefreshState()
        {
            int status = 0;
            if (platform?.Values != null
                && platform.Values.TryGet("设备状态", out ValueSnapshot snapshot, out _)
                && snapshot != null)
            {
                double.TryParse(
                    snapshot.Value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double value);
                status = Convert.ToInt32(value);
            }
            ApplyStatus(status);
        }

        internal void ApplyStatus(int status)
        {
            foreach (Button button in stateButtons)
            {
                button.BackColor = SystemColors.Control;
                button.ForeColor = SystemColors.ControlText;
            }

            switch (status)
            {
                case 1:
                    stateButtons[0].BackColor = Color.FromArgb(0, 255, 0);
                    break;
                case 2:
                    stateButtons[2].BackColor = Color.FromArgb(235, 255, 15);
                    break;
                case 3:
                    stateButtons[3].BackColor = Color.FromArgb(204, 18, 216);
                    break;
                case 4:
                    stateButtons[1].BackColor = Color.FromArgb(18, 100, 255);
                    stateButtons[1].ForeColor = Color.White;
                    break;
                case 5:
                    stateButtons[4].BackColor = Color.FromArgb(255, 15, 25);
                    stateButtons[4].ForeColor = Color.White;
                    break;
                default:
                    stateButtons[5].BackColor = Color.White;
                    break;
            }
        }
    }
}
