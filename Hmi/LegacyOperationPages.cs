using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Automation.DeviceSdk;

// 模块：平台内置 HMI / 旧设备 OpDlg 子页面。
// 职责范围：复刻旧 UI_MainPage、UI_LogPage、UI_Hive_View、压力和扭力曲线页的界面职责。
// 排查入口：页面无数据时先检查 EquipmentProcessMessageService 快照和对应平台变量。

namespace Automation.Hmi
{
    internal sealed class LegacyAlarmTickerControl : UserControl
    {
        private readonly Label textLabel;
        private readonly Timer moveTimer;
        private string lastText = string.Empty;

        public LegacyAlarmTickerControl()
        {
            BackColor = Color.White;
            BorderStyle = BorderStyle.Fixed3D;
            DoubleBuffered = true;
            textLabel = new Label
            {
                AutoSize = true,
                Font = new Font("宋体", 21.75F),
                ForeColor = Color.Red,
                Location = new Point(Width, 20),
                Text = string.Empty
            };
            Controls.Add(textLabel);
            moveTimer = new Timer { Interval = 80 };
            moveTimer.Tick += MoveTimer_Tick;
            moveTimer.Start();
            Resize += (sender, args) =>
            {
                textLabel.Top = Math.Max(0, (ClientSize.Height - textLabel.Height) / 2);
            };
        }

        internal void SetMessages(IEnumerable<string> messages)
        {
            string text = string.Join(
                "                              ",
                (messages ?? Enumerable.Empty<string>())
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Distinct(StringComparer.Ordinal));
            if (string.Equals(lastText, text, StringComparison.Ordinal))
            {
                return;
            }
            lastText = text;
            textLabel.Text = text;
            textLabel.Left = ClientSize.Width;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                moveTimer.Dispose();
            }
            base.Dispose(disposing);
        }

        private void MoveTimer_Tick(object sender, EventArgs e)
        {
            if (textLabel.TextLength() == 0)
            {
                return;
            }
            textLabel.Left -= 6;
            if (textLabel.Right < 0)
            {
                textLabel.Left = ClientSize.Width;
            }
        }
    }

    internal sealed class LegacyMainPageControl : UserControl
    {
        private readonly DataGridView inputGrid;
        private readonly DataGridView outputGrid;
        private readonly DataGridView alarmGrid;
        private readonly RichTextBox runLog;
        private IAutomationPlatform platform;
        private EquipmentProcessMessageService service;
        private long renderedRevision = -1;

        public LegacyMainPageControl()
        {
            Dock = DockStyle.Fill;
            BackColor = SystemColors.Control;
            var root = new TableLayoutPanel
            {
                ColumnCount = 2,
                RowCount = 3,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 46.86F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 6.28F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 46.86F));

            inputGrid = CreateGrid();
            outputGrid = CreateGrid();
            alarmGrid = CreateGrid();
            runLog = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("宋体", 9F)
            };

            root.Controls.Add(CreateGroup("进站", inputGrid), 0, 0);
            root.Controls.Add(CreateGroup("出站", outputGrid), 1, 0);
            root.Controls.Add(CreateFileToolbar(), 0, 1);
            root.Controls.Add(new Panel { Dock = DockStyle.Fill }, 1, 1);
            root.Controls.Add(runLog, 0, 2);
            root.Controls.Add(CreateGroup("报警信息", alarmGrid), 1, 2);
            Controls.Add(root);

            inputGrid.ContextMenuStrip = CreateInputMenu();
            outputGrid.ContextMenuStrip = CreateOutputMenu();
            alarmGrid.ContextMenuStrip = CreateAlarmMenu();
            inputGrid.CellFormatting += ProductionGrid_CellFormatting;
            outputGrid.CellFormatting += ProductionGrid_CellFormatting;
        }

        internal void Attach(IAutomationPlatform platform, EquipmentProcessMessageService service)
        {
            this.platform = platform;
            this.service = service;
        }

        internal void Render(EquipmentProcessMessageSnapshot snapshot)
        {
            if (snapshot == null || snapshot.Revision == renderedRevision)
            {
                return;
            }
            renderedRevision = snapshot.Revision;
            inputGrid.DataSource = new BindingList<EquipmentProductionRecord>(
                snapshot.InputRecords.ToList());
            outputGrid.DataSource = new BindingList<EquipmentProductionRecord>(
                snapshot.OutputRecords.ToList());
            alarmGrid.DataSource = new BindingList<EquipmentAlarmRecord>(
                snapshot.Alarms.ToList());
            runLog.Text = string.Join(Environment.NewLine, snapshot.Logs);
            if (runLog.TextLength > 0)
            {
                runLog.SelectionStart = runLog.TextLength;
                runLog.ScrollToCaret();
            }
        }

        private Control CreateFileToolbar()
        {
            var panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = Padding.Empty,
                Margin = Padding.Empty
            };
            panel.Controls.Add(CreateToolbarButton(
                "查看产品信息",
                () => OpenLogFolder("Production")));
            panel.Controls.Add(CreateToolbarButton(
                "查看视觉图片",
                () => OpenLogFolder("Vision")));
            panel.Controls.Add(CreateToolbarButton(
                "查看Log信息",
                () => OpenLogFolder(string.Empty)));
            return panel;
        }

        private Button CreateToolbarButton(string text, Action action)
        {
            var button = new Button
            {
                AutoSize = true,
                Dock = DockStyle.Left,
                FlatStyle = FlatStyle.Standard,
                Font = new Font("微软雅黑", 9F),
                Text = text
            };
            button.Click += (sender, args) => action();
            return button;
        }

        private ContextMenuStrip CreateInputMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("清空数据", null, (sender, args) =>
            {
                inputGrid.DataSource = new BindingList<EquipmentProductionRecord>();
            });
            return menu;
        }

        private ContextMenuStrip CreateOutputMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("清空数据", null, (sender, args) =>
            {
                outputGrid.DataSource = new BindingList<EquipmentProductionRecord>();
            });
            menu.Items.Add("MES_查询", null, (sender, args) =>
                ExecuteManualMessage("MES流程信息||消息 进站MES查询"));
            menu.Items.Add("MES_过站", null, (sender, args) =>
                ExecuteManualMessage("MES流程信息||消息 MES过站"));
            menu.Items.Add("PDCA补传", null, (sender, args) =>
                ExecuteManualMessage("PDCA流程信息||消息 PDCA上传"));
            return menu;
        }

        private ContextMenuStrip CreateAlarmMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("删除选中", null, (sender, args) =>
            {
                if (alarmGrid.CurrentRow != null && !alarmGrid.CurrentRow.IsNewRow)
                {
                    alarmGrid.Rows.Remove(alarmGrid.CurrentRow);
                }
            });
            menu.Items.Add("清空报警", null, (sender, args) =>
            {
                alarmGrid.DataSource = new BindingList<EquipmentAlarmRecord>();
            });
            return menu;
        }

        private void ExecuteManualMessage(string message)
        {
            if (service == null)
            {
                return;
            }
            try
            {
                service.ExecuteMessage(message);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    ex.Message,
                    "执行失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void OpenLogFolder(string child)
        {
            string root = Path.Combine(@"D:\AutomationLogs", "Hmi", "Equipment");
            string path = string.IsNullOrWhiteSpace(child) ? root : Path.Combine(root, child);
            try
            {
                Directory.CreateDirectory(path);
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "打开目录失败");
            }
        }

        private static GroupBox CreateGroup(string text, Control content)
        {
            var group = new GroupBox
            {
                Dock = DockStyle.Fill,
                Text = text,
                Padding = new Padding(3)
            };
            group.Controls.Add(content);
            return group;
        }

        private static DataGridView CreateGrid()
        {
            return new DataGridView
            {
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AutoGenerateColumns = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
                Dock = DockStyle.Fill,
                MultiSelect = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };
        }

        private static void ProductionGrid_CellFormatting(
            object sender,
            DataGridViewCellFormattingEventArgs e)
        {
            var grid = (DataGridView)sender;
            if (e.RowIndex < 0
                || !(grid.Rows[e.RowIndex].DataBoundItem is EquipmentProductionRecord item))
            {
                return;
            }
            grid.Rows[e.RowIndex].DefaultCellStyle.ForeColor =
                item.IsFailure ? Color.Red : Color.Green;
        }
    }

    internal sealed class LegacyLogPageControl : UserControl
    {
        private readonly RichTextBox queryMes;
        private readonly RichTextBox addMes;
        private readonly RichTextBox pdca;
        private readonly RichTextBox info;
        private long renderedRevision = -1;

        public LegacyLogPageControl()
        {
            Dock = DockStyle.Fill;
            var root = new TableLayoutPanel
            {
                ColumnCount = 4,
                RowCount = 1,
                Dock = DockStyle.Fill
            };
            for (int i = 0; i < 4; i++)
            {
                root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            }
            queryMes = CreateLogBox();
            addMes = CreateLogBox();
            pdca = CreateLogBox();
            info = CreateLogBox();
            root.Controls.Add(CreateGroup("Mes进站检查", queryMes), 0, 0);
            root.Controls.Add(CreateGroup("Mes出站过站", addMes), 1, 0);
            root.Controls.Add(CreateGroup("PDCA", pdca), 2, 0);
            root.Controls.Add(CreateGroup("Info", info), 3, 0);
            Controls.Add(root);
        }

        internal void Render(EquipmentProcessMessageSnapshot snapshot)
        {
            if (snapshot == null || renderedRevision == snapshot.Revision)
            {
                return;
            }
            renderedRevision = snapshot.Revision;
            string[] logs = snapshot.Logs.ToArray();
            SetLog(queryMes, logs.Where(line =>
                line.IndexOf("MES进站", StringComparison.OrdinalIgnoreCase) >= 0
                || line.IndexOf("MES查询", StringComparison.OrdinalIgnoreCase) >= 0));
            SetLog(addMes, logs.Where(line =>
                line.IndexOf("MES出站", StringComparison.OrdinalIgnoreCase) >= 0
                || line.IndexOf("MES过站", StringComparison.OrdinalIgnoreCase) >= 0));
            SetLog(pdca, logs.Where(line =>
                line.IndexOf("PDCA", StringComparison.OrdinalIgnoreCase) >= 0));
            SetLog(info, logs);
        }

        private static void SetLog(RichTextBox box, IEnumerable<string> values)
        {
            box.Text = string.Join(
                Environment.NewLine,
                values.TakeLastCompatible(2000));
            box.SelectionStart = box.TextLength;
            box.ScrollToCaret();
        }

        private static RichTextBox CreateLogBox()
        {
            return new RichTextBox
            {
                BackColor = Color.White,
                BorderStyle = BorderStyle.None,
                Dock = DockStyle.Fill,
                Font = new Font("宋体", 9F),
                ReadOnly = true
            };
        }

        private static GroupBox CreateGroup(string text, Control content)
        {
            var group = new GroupBox { Dock = DockStyle.Fill, Text = text };
            group.Controls.Add(content);
            return group;
        }
    }

    internal sealed class LegacyHivePageControl : UserControl
    {
        private readonly LegacySummaryChartControl stateChart;
        private readonly LegacySummaryChartControl errorChart;
        private readonly Label inputValue;
        private readonly Label outputValue;
        private readonly Label yieldValue;
        private readonly Label passValue;
        private readonly Label failValue;
        private readonly Label uphValue;
        private readonly Label ctValue;
        private readonly Label stnValue;
        private readonly Label hiveConnection;
        private readonly Label mesConnection;
        private readonly Label pdcaConnection;
        private readonly ListView parameterList;

        public LegacyHivePageControl()
        {
            Dock = DockStyle.Fill;
            BackColor = Color.White;
            var root = new TableLayoutPanel
            {
                ColumnCount = 2,
                RowCount = 1,
                Dock = DockStyle.Fill
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32F));

            var chartRoot = new TableLayoutPanel
            {
                ColumnCount = 1,
                RowCount = 2,
                Dock = DockStyle.Fill
            };
            chartRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            chartRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            stateChart = new LegacySummaryChartControl { Title = "State Changes" };
            errorChart = new LegacySummaryChartControl { Title = "Error Statistics" };
            chartRoot.Controls.Add(stateChart, 0, 0);
            chartRoot.Controls.Add(errorChart, 0, 1);

            var right = new TableLayoutPanel
            {
                AutoScroll = true,
                ColumnCount = 2,
                RowCount = 18,
                Dock = DockStyle.Fill,
                Padding = new Padding(4)
            };
            right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48F));
            right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52F));
            inputValue = AddMetric(right, 0, "Input");
            outputValue = AddMetric(right, 1, "Output");
            yieldValue = AddMetric(right, 2, "Yield");
            passValue = AddMetric(right, 3, "Pass");
            failValue = AddMetric(right, 4, "Fail");
            uphValue = AddMetric(right, 5, "UPH");
            ctValue = AddMetric(right, 6, "CT");
            stnValue = AddMetric(right, 7, "STN");

            var dashboardTitle = new Label
            {
                Dock = DockStyle.Fill,
                Font = new Font("微软雅黑", 10F, FontStyle.Bold),
                Text = "Parameter Dashboard",
                TextAlign = ContentAlignment.MiddleLeft
            };
            right.Controls.Add(dashboardTitle, 0, 8);
            right.SetColumnSpan(dashboardTitle, 2);
            parameterList = new ListView
            {
                Dock = DockStyle.Fill,
                FullRowSelect = true,
                GridLines = true,
                View = View.Details
            };
            parameterList.Columns.Add("KeyName", 120);
            parameterList.Columns.Add("Value", 90);
            parameterList.Columns.Add("LSL", 55);
            parameterList.Columns.Add("USL", 55);
            right.Controls.Add(parameterList, 0, 9);
            right.SetColumnSpan(parameterList, 2);
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            hiveConnection = AddConnection(right, 10, "HIVE");
            mesConnection = AddConnection(right, 11, "MES");
            pdcaConnection = AddConnection(right, 12, "PDCA");
            var logButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight
            };
            logButtons.Controls.Add(CreateLogButton("Machine Log", "Machine"));
            logButtons.Controls.Add(CreateLogButton("HIVE Log", "Hive"));
            logButtons.Controls.Add(CreateLogButton("MES Log", "Mes"));
            right.Controls.Add(logButtons, 0, 13);
            right.SetColumnSpan(logButtons, 2);

            root.Controls.Add(chartRoot, 0, 0);
            root.Controls.Add(right, 1, 0);
            Controls.Add(root);
        }

        internal void Render(
            IAutomationPlatform platform,
            EquipmentProcessMessageSnapshot snapshot)
        {
            if (platform == null || snapshot == null)
            {
                return;
            }
            int pass = snapshot.GoodTotal;
            int fail = snapshot.DefectTotal;
            inputValue.Text = snapshot.InputTotal.ToString(CultureInfo.InvariantCulture);
            outputValue.Text = snapshot.OutputTotal.ToString(CultureInfo.InvariantCulture);
            passValue.Text = pass.ToString(CultureInfo.InvariantCulture);
            failValue.Text = fail.ToString(CultureInfo.InvariantCulture);
            yieldValue.Text = snapshot.OutputTotal == 0
                ? "--"
                : ((double)pass / snapshot.OutputTotal).ToString("P1", CultureInfo.InvariantCulture);
            double cycle = ReadDouble(platform, "产品信息(CT)");
            ctValue.Text = cycle <= 0 ? "--" : cycle.ToString("0.00 s", CultureInfo.InvariantCulture);
            uphValue.Text = cycle <= 0
                ? "--"
                : (3600D / cycle).ToString("0", CultureInfo.InvariantCulture);
            stnValue.Text = ReadString(platform, "设备名称", "--");

            SetConnection(hiveConnection, ReadDouble(platform, "HIVE连接"));
            SetConnection(mesConnection, ReadDouble(platform, "MES连接"));
            SetConnection(pdcaConnection, ReadDouble(platform, "PDCA连接"));
            stateChart.SetValues(new Dictionary<string, double>
            {
                ["运行"] = platform.Processes.GetAll().Count(item => item.State == ProcessRuntimeStatus.Running),
                ["暂停"] = platform.Processes.GetAll().Count(item => item.State == ProcessRuntimeStatus.Paused),
                ["停止"] = platform.Processes.GetAll().Count(item => item.State == ProcessRuntimeStatus.Stopped),
                ["报警"] = platform.Processes.GetAll().Count(item => item.IsAlarm)
            });
            errorChart.SetValues(
                snapshot.Alarms
                    .GroupBy(item => string.IsNullOrWhiteSpace(item.Position) ? "其他" : item.Position)
                    .ToDictionary(group => group.Key, group => (double)group.Count()));

            parameterList.BeginUpdate();
            parameterList.Items.Clear();
            AddParameter(platform, "MachineName");
            AddParameter(platform, "Line");
            AddParameter(platform, "Station");
            AddParameter(platform, "FixID");
            AddParameter(platform, "工作模式");
            AddParameter(platform, "产品信息(SN)");
            AddParameter(platform, "产品信息(CT)");
            parameterList.EndUpdate();
        }

        private void AddParameter(IAutomationPlatform platform, string name)
        {
            parameterList.Items.Add(new ListViewItem(new[]
            {
                name,
                ReadString(platform, name, string.Empty),
                string.Empty,
                string.Empty
            }));
        }

        private static Label AddMetric(TableLayoutPanel panel, int row, string title)
        {
            panel.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Font = new Font("微软雅黑", 9F, FontStyle.Bold),
                Text = title,
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, row);
            var value = new Label
            {
                Dock = DockStyle.Fill,
                Font = new Font("微软雅黑", 10F),
                Text = "--",
                TextAlign = ContentAlignment.MiddleRight
            };
            panel.Controls.Add(value, 1, row);
            return value;
        }

        private static Label AddConnection(
            TableLayoutPanel panel,
            int row,
            string title)
        {
            panel.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = title,
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, row);
            var value = new Label
            {
                Dock = DockStyle.Fill,
                Font = new Font("微软雅黑", 9F, FontStyle.Bold),
                Text = "Disconnected",
                TextAlign = ContentAlignment.MiddleRight
            };
            panel.Controls.Add(value, 1, row);
            return value;
        }

        private static void SetConnection(Label label, double state)
        {
            bool connected = Math.Abs(state) < double.Epsilon;
            label.Text = connected ? "Connected" : "Disconnected";
            label.ForeColor = connected ? Color.Green : Color.Red;
        }

        private static Button CreateLogButton(string text, string folder)
        {
            var button = new Button { AutoSize = true, Text = text };
            button.Click += (sender, args) =>
            {
                string path = Path.Combine(
                    @"D:\AutomationLogs",
                    "Hmi",
                    "Equipment",
                    folder);
                Directory.CreateDirectory(path);
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            };
            return button;
        }

        private static string ReadString(
            IAutomationPlatform platform,
            string name,
            string fallback)
        {
            return platform.Values.TryGet(name, out ValueSnapshot value, out _)
                && value != null
                ? value.Value
                : fallback;
        }

        private static double ReadDouble(IAutomationPlatform platform, string name)
        {
            string raw = ReadString(platform, name, string.Empty);
            return double.TryParse(
                raw,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double value)
                ? value
                : 0D;
        }
    }

    internal sealed class LegacyCurvePageControl : UserControl
    {
        private readonly bool torqueMode;
        private readonly LegacyCurveChartControl firstChart;
        private readonly LegacyCurveChartControl secondChart;

        internal LegacyCurvePageControl(bool torqueMode)
        {
            this.torqueMode = torqueMode;
            Dock = DockStyle.Fill;
            var root = new TableLayoutPanel
            {
                ColumnCount = 1,
                RowCount = 2,
                Dock = DockStyle.Fill
            };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            firstChart = new LegacyCurveChartControl();
            secondChart = new LegacyCurveChartControl();
            root.Controls.Add(firstChart, 0, 0);
            root.Controls.Add(secondChart, 0, 1);
            Controls.Add(root);
        }

        internal void Render(IAutomationPlatform platform)
        {
            if (platform == null)
            {
                return;
            }
            if (torqueMode)
            {
                RenderTorque(platform, 1, firstChart);
                RenderTorque(platform, 2, secondChart);
            }
            else
            {
                RenderPressure(platform, "A工位", firstChart);
                RenderPressure(platform, "B工位", secondChart);
            }
        }

        private static void RenderPressure(
            IAutomationPlatform platform,
            string station,
            LegacyCurveChartControl chart)
        {
            string suffix = "压力曲线_" + station;
            List<double> x = ParseNumbers(ReadString(
                platform,
                "X轴压力曲线数据-" + suffix));
            List<double> y = ParseNumbers(ReadString(
                platform,
                "Y轴压力曲线数据-" + suffix));
            int count = Math.Min(x.Count, y.Count);
            var points = new List<PointF>();
            for (int i = Math.Min(10, count); i < count; i++)
            {
                points.Add(new PointF((float)x[i], (float)y[i]));
                if (i + 1 < count && x[i] > x[i + 1] + 0.02D)
                {
                    break;
                }
            }
            string sn = ReadString(platform, "SN_Code-" + suffix);
            double peak = ReadDouble(platform, "B2B峰值-" + suffix);
            double valley = ReadDouble(platform, "B2B谷值-" + suffix);
            chart.SetData(
                $"{station}保压  SN:{sn}  峰值:{peak:0.###}  谷值:{valley:0.###}",
                "压力",
                points,
                peak,
                valley);
        }

        private static void RenderTorque(
            IAutomationPlatform platform,
            int screwId,
            LegacyCurveChartControl chart)
        {
            List<double> raw = ParseNumbers(ReadString(
                platform,
                $"电批接受数据-电批_{screwId}"));
            var points = new List<PointF>();
            for (int i = 0; i + 1 < raw.Count; i += 2)
            {
                points.Add(new PointF((float)raw[i], (float)raw[i + 1]));
            }
            double maxAngle = ReadDouble(platform, $"电批最大角度-电批_{screwId}");
            double maxTorque = ReadDouble(platform, $"电批最大扭力-电批_{screwId}");
            if (points.Count == 0 && (maxAngle != 0 || maxTorque != 0))
            {
                points.Add(new PointF(0, 0));
                points.Add(new PointF((float)maxAngle, (float)maxTorque));
            }
            chart.SetData(
                $"电批{screwId}  SN:{ReadString(platform, $"SN_Code-电批_{screwId}")}"
                + $"  最大角度:{maxAngle:0.###}  最大扭力:{maxTorque:0.###}",
                "Torque",
                points,
                maxTorque,
                null);
        }

        private static string ReadString(IAutomationPlatform platform, string name)
        {
            return platform.Values.TryGet(name, out ValueSnapshot value, out _)
                && value != null
                ? value.Value
                : string.Empty;
        }

        private static double ReadDouble(IAutomationPlatform platform, string name)
        {
            return double.TryParse(
                ReadString(platform, name),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double value)
                ? value
                : 0D;
        }

        private static List<double> ParseNumbers(string value)
        {
            var numbers = new List<double>();
            foreach (string token in (value ?? string.Empty).Split(
                new[] { ',', ';', '|', '\r', '\n', '\t', ' ' },
                StringSplitOptions.RemoveEmptyEntries))
            {
                if (double.TryParse(
                    token,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double number))
                {
                    numbers.Add(number);
                }
            }
            return numbers;
        }
    }

    internal sealed class LegacyCurveChartControl : Control
    {
        private IReadOnlyList<PointF> points = Array.Empty<PointF>();
        private string chartTitle = string.Empty;
        private string seriesName = string.Empty;
        private double? firstMarker;
        private double? secondMarker;

        public LegacyCurveChartControl()
        {
            BackColor = Color.White;
            Dock = DockStyle.Fill;
            DoubleBuffered = true;
            Font = new Font("微软雅黑", 9F);
            Margin = new Padding(3);
        }

        internal void SetData(
            string title,
            string series,
            IReadOnlyList<PointF> values,
            double? marker1,
            double? marker2)
        {
            chartTitle = title ?? string.Empty;
            seriesName = series ?? string.Empty;
            points = values ?? Array.Empty<PointF>();
            firstMarker = marker1;
            secondMarker = marker2;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics graphics = e.Graphics;
            graphics.Clear(Color.White);
            graphics.DrawRectangle(Pens.Gray, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
            graphics.DrawString(
                chartTitle,
                new Font(Font, FontStyle.Bold),
                Brushes.Black,
                new PointF(12, 8));
            var plot = new RectangleF(55, 36, Math.Max(10, Width - 75), Math.Max(10, Height - 62));
            graphics.DrawLine(Pens.Black, plot.Left, plot.Bottom, plot.Right, plot.Bottom);
            graphics.DrawLine(Pens.Black, plot.Left, plot.Top, plot.Left, plot.Bottom);
            graphics.DrawString(seriesName, Font, Brushes.Blue, plot.Right - 70, plot.Top + 2);

            if (points.Count < 2)
            {
                graphics.DrawString("No Data", Font, Brushes.Gray, plot.Left + 12, plot.Top + 12);
                return;
            }
            float minX = points.Min(point => point.X);
            float maxX = points.Max(point => point.X);
            float minY = points.Min(point => point.Y);
            float maxY = points.Max(point => point.Y);
            if (Math.Abs(maxX - minX) < float.Epsilon)
            {
                maxX = minX + 1;
            }
            if (Math.Abs(maxY - minY) < float.Epsilon)
            {
                maxY = minY + 1;
            }
            PointF Map(PointF point)
            {
                return new PointF(
                    plot.Left + ((point.X - minX) / (maxX - minX)) * plot.Width,
                    plot.Bottom - ((point.Y - minY) / (maxY - minY)) * plot.Height);
            }
            PointF[] mapped = points.Select(Map).ToArray();
            using (var pen = new Pen(Color.RoyalBlue, 2F))
            {
                graphics.DrawLines(pen, mapped);
            }
            DrawMarker(graphics, plot, minY, maxY, firstMarker, Color.Red, "Max");
            DrawMarker(graphics, plot, minY, maxY, secondMarker, Color.DarkOrange, "Min");
        }

        private void DrawMarker(
            Graphics graphics,
            RectangleF plot,
            float minY,
            float maxY,
            double? value,
            Color color,
            string caption)
        {
            if (!value.HasValue)
            {
                return;
            }
            float y = plot.Bottom
                - ((float)value.Value - minY) / (maxY - minY) * plot.Height;
            if (y < plot.Top || y > plot.Bottom)
            {
                return;
            }
            using (var pen = new Pen(color) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
            using (var brush = new SolidBrush(color))
            {
                graphics.DrawLine(pen, plot.Left, y, plot.Right, y);
                graphics.DrawString($"{caption}:{value:0.###}", Font, brush, plot.Left + 4, y - 17);
            }
        }
    }

    internal sealed class LegacySummaryChartControl : Control
    {
        private IReadOnlyDictionary<string, double> values =
            new Dictionary<string, double>();

        public LegacySummaryChartControl()
        {
            BackColor = Color.White;
            Dock = DockStyle.Fill;
            DoubleBuffered = true;
            Font = new Font("微软雅黑", 9F);
        }

        internal string Title { get; set; }

        internal void SetValues(IReadOnlyDictionary<string, double> values)
        {
            this.values = values ?? new Dictionary<string, double>();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.Clear(Color.White);
            e.Graphics.DrawRectangle(Pens.LightGray, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
            e.Graphics.DrawString(
                Title ?? string.Empty,
                new Font(Font, FontStyle.Bold),
                Brushes.Black,
                10,
                8);
            if (values.Count == 0)
            {
                e.Graphics.DrawString("No Data", Font, Brushes.Gray, 24, 40);
                return;
            }
            double maximum = Math.Max(1D, values.Values.DefaultIfEmpty().Max());
            int index = 0;
            float availableWidth = Math.Max(1, Width - 70);
            float barWidth = availableWidth / values.Count;
            foreach (KeyValuePair<string, double> pair in values)
            {
                float height = (float)(pair.Value / maximum) * Math.Max(1, Height - 85);
                var rectangle = new RectangleF(
                    45 + index * barWidth + 6,
                    Height - 35 - height,
                    Math.Max(4, barWidth - 12),
                    height);
                using (var brush = new SolidBrush(index % 2 == 0
                    ? Color.SteelBlue
                    : Color.MediumSeaGreen))
                {
                    e.Graphics.FillRectangle(brush, rectangle);
                }
                e.Graphics.DrawString(
                    pair.Value.ToString("0", CultureInfo.InvariantCulture),
                    Font,
                    Brushes.Black,
                    rectangle.Left,
                    rectangle.Top - 18);
                e.Graphics.DrawString(
                    pair.Key,
                    Font,
                    Brushes.Black,
                    rectangle.Left,
                    Height - 32);
                index++;
            }
        }
    }

    internal static class LegacyOperationPageExtensions
    {
        internal static IEnumerable<T> TakeLastCompatible<T>(
            this IEnumerable<T> values,
            int count)
        {
            var queue = new Queue<T>();
            foreach (T value in values ?? Enumerable.Empty<T>())
            {
                queue.Enqueue(value);
                if (queue.Count > count)
                {
                    queue.Dequeue();
                }
            }
            return queue;
        }

        internal static int TextLength(this Label label)
        {
            return label?.Text?.Length ?? 0;
        }
    }
}
