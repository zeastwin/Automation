using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Design;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.Design;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using System.Xml.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using static System.Windows.Forms.AxHost;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Button;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Globalization;
using static System.Collections.Specialized.BitVector32;
using System.Diagnostics;
using Automation.MotionControl;

namespace Automation
{
    public partial class FrmControl : Form
    {
        public System.Drawing.Image validImage = UiStatusImages.CreateValidImage();
        public System.Drawing.Image invalidImage = UiStatusImages.CreateInvalidImage();
        public BindingSource bindingSource = new BindingSource();
        public List<System.Windows.Forms.TextBox> PosTextBox = new List<System.Windows.Forms.TextBox>();
        public List<System.Windows.Forms.PictureBox> pictureBoxes = new List<System.Windows.Forms.PictureBox>();
        public List<System.Windows.Forms.Label> VelLabel = new List<System.Windows.Forms.Label>();
        private bool suppressStationChange = false;
        private int lastStationIndex = -1;
        public int CurrentStationIndex => lastStationIndex;
        public FrmControl()
        {
            InitializeComponent();
            ConfigureAppearance();
            PosTextBox.Add(txtPos1);
            PosTextBox.Add(txtPos2);
            PosTextBox.Add(txtPos3);
            PosTextBox.Add(txtPos4);
            PosTextBox.Add(txtPos5);
            PosTextBox.Add(txtPos6);

            pictureBoxes.Add(pictureBox1);
            pictureBoxes.Add(pictureBox2);
            pictureBoxes.Add(pictureBox3);
            pictureBoxes.Add(pictureBox4);
            pictureBoxes.Add(pictureBox5);
            pictureBoxes.Add(pictureBox6);

            VelLabel.Add(AxisVel1);
            VelLabel.Add(AxisVel2);
            VelLabel.Add(AxisVel3);
            VelLabel.Add(AxisVel4);
            VelLabel.Add(AxisVel5);
            VelLabel.Add(AxisVel6);


            //InintMovParam();

        }

        private void ConfigureAppearance()
        {
            Color textColor = UiPalette.TextPrimary;
            Color mutedTextColor = UiPalette.TextSecondary;
            Color borderColor = UiPalette.StrokeStrong;
            BackColor = UiPalette.Input;
            groupBox1.BackColor = UiPalette.SurfaceSubtle;
            groupBox1.ForeColor = mutedTextColor;
            groupBox2.BackColor = UiPalette.Input;
            groupBox2.ForeColor = mutedTextColor;
            groupBox2.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular);

            comboBox1.BackColor = UiPalette.SurfaceStrong;
            comboBox1.ForeColor = textColor;
            comboBox1.FlatStyle = FlatStyle.Flat;
            comboBox1.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular);
            comboBox1.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBox1.DrawMode = DrawMode.OwnerDrawFixed;
            comboBox1.ItemHeight = 27;
            comboBox1.DropDownHeight = 280;
            comboBox1.DrawItem += comboBox1_DrawItem;
            label24.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular);
            label24.AutoSize = false;
            label24.TextAlign = ContentAlignment.MiddleLeft;
            label25.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular);
            label25.Text = "手动速度：";
            label25.AutoSize = false;
            label25.TextAlign = ContentAlignment.MiddleLeft;
            label6.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            label6.ForeColor = UiPalette.Brand;
            label6.AutoSize = false;
            label6.TextAlign = ContentAlignment.MiddleLeft;
            Action alignTopControls = () =>
            {
                int comboHeight = comboBox1.PreferredHeight;
                int comboTop = Math.Max(0, (groupBox1.ClientSize.Height - comboHeight) / 2);
                label24.SetBounds(10, 0, 58, groupBox1.ClientSize.Height);
                comboBox1.SetBounds(68, comboTop, 125, comboHeight);
                label25.SetBounds(207, 0, 78, groupBox1.ClientSize.Height);
                label6.SetBounds(282, 0, 54, groupBox1.ClientSize.Height);
                trackBar1.SetBounds(
                    334,
                    Math.Max(0, (groupBox1.ClientSize.Height - 30) / 2),
                    142,
                    30);
            };
            groupBox1.Resize += (sender, args) => alignTopControls();
            alignTopControls();

            Label[] rowLabels = { label5, label4, label13, label3, label2, label1 };
            foreach (Label label in rowLabels)
            {
                label.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold);
                label.ForeColor = UiPalette.TextPrimary;
            }

            Label[] axisNameLabels = { AxisName1, AxisName2, AxisName3, AxisName4, AxisName5, AxisName6 };
            foreach (Label label in axisNameLabels)
            {
                label.BackColor = UiPalette.SurfaceSubtle;
                label.ForeColor = UiPalette.TextPrimary;
                label.BorderStyle = BorderStyle.FixedSingle;
                label.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
            }

            var positionBoxes = new[] { txtPos1, txtPos2, txtPos3, txtPos4, txtPos5, txtPos6 };
            foreach (var textBox in positionBoxes)
            {
                textBox.BackColor = UiPalette.SurfaceStrong;
                textBox.ForeColor = textColor;
                textBox.BorderStyle = BorderStyle.FixedSingle;
                textBox.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular);
            }

            foreach (Label label in new[] { AxisVel1, AxisVel2, AxisVel3, AxisVel4, AxisVel5, AxisVel6 })
            {
                label.ForeColor = UiPalette.TextSecondary;
                label.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular);
            }

            foreach (var textBox in new[] { txtMovPos1, txtMovPos2, txtMovPos3, txtMovPos4, txtMovPos5, txtMovPos6 })
            {
                textBox.BackColor = UiPalette.SurfaceStrong;
                textBox.ForeColor = textColor;
                textBox.BorderStyle = BorderStyle.FixedSingle;
                textBox.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular);
            }
            foreach (Label label in new[] { label18, label19, label20, label21, label22, label23 })
            {
                label.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular);
                label.ForeColor = mutedTextColor;
            }
            foreach (var radioButton in new[]
            {
                radioButton1, radioButton2, radioButton3,
                radioButton4, radioButton5, radioButton6
            })
            {
                radioButton.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular);
                radioButton.ForeColor = textColor;
                radioButton.FlatStyle = FlatStyle.Flat;
            }

            foreach (var button in new[] { btnHome1, btnHome2, btnHome3, btnHome4, btnHome5, btnHome6 })
            {
                ConfigureControlButton(
                    button,
                    UiPalette.TextPrimary,
                    borderColor,
                    UiPalette.DisabledSoft);
                button.BackColor = UiPalette.InputFocused;
            }
            foreach (var button in new[]
            {
                Handle1, Handle2, Handle3, Handle4, Handle5, Handle6,
                Handle7, Handle8, Handle9, Handle10, Handle11, Handle12
            })
            {
                ConfigureControlButton(
                    button,
                    UiPalette.TextPrimary,
                    borderColor,
                    UiPalette.DisabledSoft);
                button.BackColor = UiPalette.SurfaceHover;
            }

            ConfigureControlButton(
                btnStationHome,
                UiPalette.TextPrimary,
                UiPalette.StrokeStrong,
                UiPalette.DisabledSoft);
            btnStationHome.BackColor = UiPalette.BrandSoft;
            ConfigureControlButton(
                btnStop,
                UiPalette.DangerHover,
                UiPalette.Danger,
                UiPalette.DangerSoft);
            btnStop.BackColor = UiPalette.DangerSoft;
            ConfigureControlButton(
                btnReSet,
                UiPalette.WarningHover,
                UiPalette.Warning,
                UiPalette.WarningSoft);
            btnReSet.BackColor = UiPalette.WarningSoft;
        }

        internal void OnEditorWorkspaceAttached()
        {
            RefreshMotionControlAvailability();
        }

        private void comboBox1_DrawItem(object sender, DrawItemEventArgs e)
        {
            Color backColor = (e.State & DrawItemState.Selected) == DrawItemState.Selected
                ? UiPalette.Selection
                : UiPalette.SurfaceStrong;
            Color foreColor = (e.State & DrawItemState.Disabled) == DrawItemState.Disabled
                ? UiPalette.TextDisabled
                : UiPalette.TextPrimary;
            using (SolidBrush background = new SolidBrush(backColor))
            {
                e.Graphics.FillRectangle(background, e.Bounds);
            }
            if (e.Index >= 0)
            {
                Rectangle textBounds = new Rectangle(
                    e.Bounds.Left + 9,
                    e.Bounds.Top,
                    Math.Max(0, e.Bounds.Width - 12),
                    e.Bounds.Height);
                TextRenderer.DrawText(
                    e.Graphics,
                    comboBox1.GetItemText(comboBox1.Items[e.Index]),
                    comboBox1.Font,
                    textBounds,
                    foreColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
            e.DrawFocusRectangle();
        }

        public void RefreshMotionControlAvailability()
        {
            Label[] axisLabels = { AxisName1, AxisName2, AxisName3, AxisName4, AxisName5, AxisName6 };
            System.Windows.Forms.Button[] homeButtons =
            {
                btnHome1, btnHome2, btnHome3, btnHome4, btnHome5, btnHome6
            };
            System.Windows.Forms.Button[] moveButtons =
            {
                Handle1, Handle2, Handle3, Handle4, Handle5, Handle6,
                Handle7, Handle8, Handle9, Handle10, Handle11, Handle12
            };
            PictureBox[] enableButtons =
            {
                pictureBox1, pictureBox2, pictureBox3, pictureBox4, pictureBox5, pictureBox6
            };
            bool motionReady = Workspace.Runtime.Motion?.IsCardInitialized == true && !Workspace.Runtime.Readiness.MotionConfigRestartRequired;
            bool hasAvailableAxis = false;

            for (int i = 0; i < axisLabels.Length; i++)
            {
                string axisName = axisLabels[i].Text?.Trim() ?? string.Empty;
                AxisConfig axisConfig = temp?.dataAxis?.axisConfigs != null
                    && i < temp.dataAxis.axisConfigs.Count
                    ? temp.dataAxis.axisConfigs[i]
                    : null;
                bool configured = axisConfig?.axis != null
                    && ushort.TryParse(axisConfig.CardNum, out _)
                    && axisName.Length > 0
                    && axisName != "-1"
                    && axisName.Any(character => character != '-');
                bool operationEnabled = configured && motionReady;
                hasAvailableAxis |= operationEnabled;
                axisLabels[i].BackColor = configured
                    ? UiPalette.SurfaceSubtle
                    : UiPalette.Input;
                axisLabels[i].ForeColor = configured
                    ? UiPalette.TextPrimary
                    : UiPalette.TextDisabled;

                homeButtons[i].BackColor = configured
                    ? UiPalette.InputFocused
                    : UiPalette.Background;
                homeButtons[i].ForeColor = configured
                    ? UiPalette.TextPrimary
                    : UiPalette.TextDisabled;
                homeButtons[i].Enabled = operationEnabled;
                enableButtons[i].Enabled = operationEnabled;

                for (int buttonOffset = 0; buttonOffset < 2; buttonOffset++)
                {
                    System.Windows.Forms.Button moveButton = moveButtons[i * 2 + buttonOffset];
                    moveButton.BackColor = configured
                        ? UiPalette.SurfaceHover
                        : UiPalette.Background;
                    moveButton.ForeColor = configured
                        ? UiPalette.TextPrimary
                        : UiPalette.TextDisabled;
                    moveButton.Enabled = operationEnabled;
                }
            }

            btnStationHome.Enabled = hasAvailableAxis;
            btnStop.Enabled = hasAvailableAxis;
            btnReSet.Enabled = hasAvailableAxis;
        }

        private static void ConfigureControlButton(
            System.Windows.Forms.Button button,
            Color foreColor,
            Color borderColor,
            Color hoverColor)
        {
            button.BackColor = UiPalette.SurfaceStrong;
            button.ForeColor = foreColor;
            button.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = borderColor;
            button.FlatAppearance.MouseOverBackColor = hoverColor;
            button.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(hoverColor, 0.05F);
            button.UseVisualStyleBackColor = false;
        }

     //   public List<DataGridViewTextBoxColumn> dataGridViewTextBoxColumns = new List<DataGridViewTextBoxColumn>();
        public DataStation temp;
        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (suppressStationChange)
            {
                return;
            }
            if (comboBox1.SelectedIndex == lastStationIndex)
            {
                return;
            }
            if (Workspace.Station != null && Workspace.Station.IsPointEditing)
            {
                suppressStationChange = true;
                comboBox1.SelectedIndex = lastStationIndex;
                suppressStationChange = false;
                MessageBox.Show("编辑状态下不允许切换工站，请先保存或取消。");
                return;
            }
            if (comboBox1.DroppedDown)
            {
                return;
            }
            ApplyStationSelection(comboBox1.SelectedIndex);
        }

        private void comboBox1_DropDownClosed(object sender, EventArgs e)
        {
            if (suppressStationChange)
            {
                return;
            }
            ApplyStationSelection(comboBox1.SelectedIndex);
        }

        private void ApplyStationSelection(int selectedIndex)
        {
            if (selectedIndex == lastStationIndex)
            {
                return;
            }
            if (selectedIndex == -1)
            {
                temp = null;
                lastStationIndex = -1;
                return;
            }
            if (Workspace.Card == null || Workspace.Card.dataStation == null || selectedIndex >= Workspace.Card.dataStation.Count)
            {
                temp = null;
                lastStationIndex = -1;
                return;
            }

            temp = Workspace.Card.dataStation[selectedIndex];
            trackBar1.Maximum = 100;
            trackBar1.Minimum = 1;
            trackBar1.Value = (int)temp.ManualSpeedPercent;
            label6.Text = trackBar1.Value.ToString() + "%";
            bindingSource.DataSource = temp.ListDataPos;
            Workspace.Station.dataGridView1.DataSource = bindingSource;
            Workspace.Station.dataGridView1.Columns[0].HeaderText = "索引";
            Workspace.Station.dataGridView1.Columns[1].HeaderText = "名称";
            AxisName1.Text = "-------";
            AxisName2.Text = "-------";
            AxisName3.Text = "-------";
            AxisName4.Text = "-------";
            AxisName5.Text = "-------";
            AxisName6.Text = "-------";
            Handle1.Text = "---------";
            Handle2.Text = "---------";
            Handle3.Text = "---------";
            Handle4.Text = "---------";
            Handle5.Text = "---------";
            Handle6.Text = "---------";
            Handle7.Text = "---------";
            Handle8.Text = "---------";
            Handle9.Text = "---------";
            Handle10.Text = "---------";
            Handle11.Text = "---------";
            Handle12.Text = "---------";
            pictureBox1.Image = invalidImage;
            pictureBox2.Image = invalidImage;
            pictureBox3.Image = invalidImage;
            pictureBox4.Image = invalidImage;
            pictureBox5.Image = invalidImage;
            pictureBox6.Image = invalidImage;
            for (int i = 0; i < 6; i++)
            {
                Workspace.Station.dataGridView1.Columns[i + 2].HeaderText = temp.dataAxis.axisConfigs[i].AxisName;
            }

            AxisName1.Text = temp.dataAxis.axisConfig1.AxisName;
            AxisName2.Text = temp.dataAxis.axisConfig2.AxisName;
            AxisName3.Text = temp.dataAxis.axisConfig3.AxisName;
            AxisName4.Text = temp.dataAxis.axisConfig4.AxisName;
            AxisName5.Text = temp.dataAxis.axisConfig5.AxisName;
            AxisName6.Text = temp.dataAxis.axisConfig6.AxisName;

            if (temp.dataAxis.axisConfig1.AxisName != "-1")
            {
                Handle1.Text = temp.dataAxis.axisConfig1.AxisName + "+";
                Handle2.Text = temp.dataAxis.axisConfig1.AxisName + "-";
            }
            if (temp.dataAxis.axisConfig2.AxisName != "-1")
            {
                Handle3.Text = temp.dataAxis.axisConfig2.AxisName + "+";
                Handle4.Text = temp.dataAxis.axisConfig2.AxisName + "-";
            }
            if (temp.dataAxis.axisConfig3.AxisName != "-1")
            {
                Handle5.Text = temp.dataAxis.axisConfig3.AxisName + "+";
                Handle6.Text = temp.dataAxis.axisConfig3.AxisName + "-";
            }
            if (temp.dataAxis.axisConfig4.AxisName != "-1")
            {
                Handle7.Text = temp.dataAxis.axisConfig4.AxisName + "+";
                Handle8.Text = temp.dataAxis.axisConfig4.AxisName + "-";
            }
            if (temp.dataAxis.axisConfig5.AxisName != "-1")
            {
                Handle9.Text = temp.dataAxis.axisConfig5.AxisName + "+";
                Handle10.Text = temp.dataAxis.axisConfig5.AxisName + "-";
            }
            if (temp.dataAxis.axisConfig6.AxisName != "-1")
            {
                Handle11.Text = temp.dataAxis.axisConfig6.AxisName + "+";
                Handle12.Text = temp.dataAxis.axisConfig6.AxisName + "-";
            }

            RefreshMotionControlAvailability();

            Workspace.Station.RefleshDgvState();
            lastStationIndex = selectedIndex;
        }
    
        //轴按顺序回原
        public async Task HomeStationByseq(int dataStationIndex)
        {
            if (Workspace.Card?.dataStation == null || dataStationIndex < 0 || dataStationIndex >= Workspace.Card.dataStation.Count)
            {
                throw new InvalidOperationException("工站索引无效");
            }
            DataStation station = Workspace.Card.dataStation[dataStationIndex];
            if (station.homeSeq?.axisSeq == null || station.dataAxis?.axisConfigs == null)
            {
                throw new InvalidOperationException("工站回零配置不完整");
            }
            foreach (AxisName sequenceItem in station.homeSeq.axisSeq)
            {
                if (sequenceItem == null || sequenceItem.Name == "-1")
                {
                    continue;
                }
                AxisConfig axisConfig = station.dataAxis.axisConfigs.FirstOrDefault(item => item?.AxisName == sequenceItem.Name);
                if (axisConfig?.axis == null || !ushort.TryParse(axisConfig.CardNum, out ushort cardNum))
                {
                    throw new InvalidOperationException($"回零轴配置无效:{sequenceItem.Name}");
                }
                await Task.Run(() => HomeSingleAxis(cardNum, (ushort)axisConfig.axis.AxisNum));
            }
        }

        //所有轴同步回
        public async Task HomeStationByAll(int dataStationIndex)
        {
            if (Workspace.Card?.dataStation == null || dataStationIndex < 0 || dataStationIndex >= Workspace.Card.dataStation.Count)
            {
                throw new InvalidOperationException("工站索引无效");
            }
            DataStation station = Workspace.Card.dataStation[dataStationIndex];
            if (station.dataAxis?.axisConfigs == null)
            {
                throw new InvalidOperationException("工站回零配置不完整");
            }
            List<Task> tasks = new List<Task>();
            foreach (AxisConfig axisConfig in station.dataAxis.axisConfigs)
            {
                if (axisConfig?.AxisName == "-1")
                {
                    continue;
                }
                if (axisConfig?.axis == null || !ushort.TryParse(axisConfig.CardNum, out ushort cardNum))
                {
                    throw new InvalidOperationException($"回零轴配置无效:{axisConfig?.AxisName}");
                }
                ushort axisNum = (ushort)axisConfig.axis.AxisNum;
                tasks.Add(Task.Run(() => HomeSingleAxis(cardNum, axisNum)));
            }
            await Task.WhenAll(tasks);
        }

        public void HomeSingleAxis(ushort cardNum, ushort axis)
        {
            if (Workspace.Runtime.ProcessEngine == null || !Workspace.Runtime.ProcessEngine.TryValidateStartGate(out _))
            {
                throw new InvalidOperationException("系统尚未复位完成，禁止手动回零。");
            }
            if (!Workspace.Runtime.ProcessEngine.TryAcquireManualMotionResource(cardNum, axis, out string resourceError))
            {
                throw new InvalidOperationException(resourceError);
            }
            Axis axisInfo = null;
            bool completed = false;
            try
            {
            if (!Workspace.Runtime.Motion.GetInPos(cardNum, axis))
            {
                throw new InvalidOperationException($"轴正在运动，禁止启动回零:{cardNum}-{axis}");
            }
            ushort dir = 0;
            if (Workspace.Runtime.Stores.Cards == null || !Workspace.Runtime.Stores.Cards.TryGetAxis(cardNum, axis, out axisInfo)
                || axisInfo.PulseToMM <= 0 || axisInfo.AccMax <= 0 || axisInfo.DecMax <= 0
                || !double.TryParse(axisInfo.HomeSpeed, out double homeSpeed) || homeSpeed <= 0)
            {
                throw new InvalidOperationException($"轴回零参数无效:{cardNum}-{axis}");
            }
            int sfc = 10;
            if (axisInfo.HomeDirection == "正向")
            {
                dir = 1;
            }
            Stopwatch timeout = Stopwatch.StartNew();
            while (!completed)
            {
                if (!Workspace.Runtime.ProcessEngine.TryValidateStartGate(out _))
                {
                    throw new InvalidOperationException("回零过程中复位状态失效");
                }
                if (timeout.ElapsedMilliseconds > 120000)
                {
                    throw new TimeoutException($"轴回零超时:{cardNum}-{axis}");
                }
                switch (sfc)
                {
                    case 10:
                        using (Workspace.Runtime.Motion.ValidateAxesForCommand(new[]
                        {
                            new AxisCommandRequest(cardNum, axis, AxisCommandKind.Home)
                        }))
                        {
                            Workspace.Runtime.Motion.SetMovParam(cardNum, axis, 0, homeSpeed, axisInfo.AccMax, axisInfo.DecMax, 0, 0, axisInfo.PulseToMM);
                            Workspace.Runtime.Motion.SettHomeParam(cardNum, axis, dir, 1, 1);
                            Workspace.Runtime.Motion.StartHome(cardNum, axis);
                        }
                        Thread.Sleep(20);
                        sfc = 20;
                        break;
                    case 20:
                        EnsureHomeHardwareSafety(Workspace.Runtime.Motion, cardNum, axis);
                        if (Workspace.Runtime.Motion.GetInPos(cardNum, axis))
                        {
                            Thread.Sleep(300);
                            if (Workspace.Runtime.Motion.HomeStatus(cardNum, axis))
                            {
                                Workspace.Runtime.Motion.CleanPos(cardNum, axis);
                                completed = true;
                            }
                            else
                            {
                                throw new InvalidOperationException("控制卡报告回零失败。");
                            }
                        }
                        Thread.Sleep(20);
                        break;
                }
            }
            }
            finally
            {
                if (!completed)
                {
                    try
                    {
                        Workspace.Runtime.Motion?.StopOneAxis(cardNum, axis, 0);
                    }
                    catch (Exception ex)
                    {
                        Workspace.Runtime.ProcessEngine?.Logger?.Log($"手动回零失败后停止轴异常:{cardNum}-{axis} {ex.Message}", LogLevel.Error);
                    }
                }
                Workspace.Runtime.ProcessEngine?.ReleaseManualMotionResource(cardNum, axis);
            }
        }

        private static void EnsureHomeHardwareSafety(IMotionRuntime motion, ushort cardNum, ushort axis)
        {
            uint ioStatus = motion.GetAxisIoStatus(cardNum, axis);
            if ((ioStatus & 1u) != 0)
            {
                motion.StopOneAxis(cardNum, axis, 1);
                throw new InvalidOperationException($"回零过程中伺服报警，轴已急停:{cardNum}-{axis}");
            }
            if ((ioStatus & (1u << 3)) != 0)
            {
                motion.StopOneAxis(cardNum, axis, 1);
                throw new InvalidOperationException($"回零过程中急停信号有效，轴已急停:{cardNum}-{axis}");
            }
        }
        private void FrmControl_Load(object sender, EventArgs e)
        {
          
        }

        private async Task RunManualAxisHomeAsync(AxisConfig axisConfig)
        {
            try
            {
                if (axisConfig?.axis == null || !ushort.TryParse(axisConfig.CardNum, out ushort cardNum))
                {
                    throw new InvalidOperationException("回零轴配置无效");
                }
                await Task.Run(() => HomeSingleAxis(cardNum, (ushort)axisConfig.axis.AxisNum));
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "手动回零失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async void btnHome1_Click(object sender, EventArgs e)
        {
            await RunManualAxisHomeAsync(temp?.dataAxis?.axisConfig1);
        }

        private async void btnHome2_Click(object sender, EventArgs e)
        {
            await RunManualAxisHomeAsync(temp?.dataAxis?.axisConfig2);
        }

        private async void btnHome3_Click(object sender, EventArgs e)
        {
            await RunManualAxisHomeAsync(temp?.dataAxis?.axisConfig3);
        }

        private async void btnHome4_Click(object sender, EventArgs e)
        {
            await RunManualAxisHomeAsync(temp?.dataAxis?.axisConfig4);
        }

        private async void btnHome5_Click(object sender, EventArgs e)
        {
            await RunManualAxisHomeAsync(temp?.dataAxis?.axisConfig5);
        }

        private async void btnHome6_Click(object sender, EventArgs e)
        {
            await RunManualAxisHomeAsync(temp?.dataAxis?.axisConfig6);
        }

        private bool TryGetStepDistance(string customText, out double distance)
        {
            if (radioButton2.Checked)
            {
                distance = 0.1;
                return true;
            }
            if (radioButton4.Checked)
            {
                distance = 1;
                return true;
            }
            if (radioButton5.Checked)
            {
                distance = 10;
                return true;
            }
            if (radioButton6.Checked)
            {
                if (string.IsNullOrWhiteSpace(customText)
                    || !double.TryParse(customText, NumberStyles.Float, CultureInfo.InvariantCulture, out distance))
                {
                    MessageBox.Show("自定义寸动距离无效。");
                    distance = 0;
                    return false;
                }
                return true;
            }
            distance = 0;
            return false;
        }

        private void Handle1_MouseDown(object sender, MouseEventArgs e)
        {
            Workspace.Station.SetStationParam(temp,0);
            if (radioButton3.Checked)
            {
                Workspace.Runtime.ManualMotion.TryJog(ushort.Parse(temp.dataAxis.axisConfig1.CardNum),(ushort)temp.dataAxis.axisConfig1.axis.AxisNum, 1);
                return;
            }
            if (TryGetStepDistance(txtMovPos1.Text, out double distance))
            {
                Workspace.Runtime.ManualMotion.TryMove(ushort.Parse(temp.dataAxis.axisConfig1.CardNum), (ushort)temp.dataAxis.axisConfig1.axis.AxisNum, distance, 0, false);
                return;
            }
            if (radioButton1.Checked)
            {
                if (txtMovPos1.Text == "")
                {
                    return;
                }
                Workspace.Runtime.ManualMotion.TryMove(ushort.Parse(temp.dataAxis.axisConfig1.CardNum), (ushort)temp.dataAxis.axisConfig1.axis.AxisNum, double.Parse(txtMovPos1.Text), 1, false);
            }

        }

        private void Handle1_MouseUp(object sender, MouseEventArgs e)
        {
            if (radioButton3.Checked)
                Workspace.Runtime.ManualMotion.TryStop(ushort.Parse(temp.dataAxis.axisConfig1.CardNum), (ushort)temp.dataAxis.axisConfig1.axis.AxisNum, 0);
        }

        private void Handle2_MouseDown(object sender, MouseEventArgs e)
        {
            Workspace.Station.SetStationParam(temp, 0);
            if (radioButton3.Checked)
            {
                Workspace.Runtime.ManualMotion.TryJog(ushort.Parse(temp.dataAxis.axisConfig1.CardNum), (ushort)temp.dataAxis.axisConfig1.axis.AxisNum, 0);
                return;
            }
            if (TryGetStepDistance(txtMovPos1.Text, out double distance))
            {
                Workspace.Runtime.ManualMotion.TryMove(ushort.Parse(temp.dataAxis.axisConfig1.CardNum), (ushort)temp.dataAxis.axisConfig1.axis.AxisNum, -distance, 0, false);
                return;
            }
            if (radioButton1.Checked)
            {
                if (txtMovPos1.Text == "")
                {
                    return;
                }
                Workspace.Runtime.ManualMotion.TryMove(ushort.Parse(temp.dataAxis.axisConfig1.CardNum), (ushort)temp.dataAxis.axisConfig1.axis.AxisNum, double.Parse(txtMovPos1.Text), 1, false);
            }
        }

        private void Handle2_MouseUp(object sender, MouseEventArgs e)
        {
            if (radioButton3.Checked)
                Workspace.Runtime.ManualMotion.TryStop(ushort.Parse(temp.dataAxis.axisConfig1.CardNum), (ushort)temp.dataAxis.axisConfig1.axis.AxisNum, 0);
        }

        private void Handle3_MouseDown(object sender, MouseEventArgs e)
        {
            Workspace.Station.SetStationParam(temp, 1);
            if (radioButton3.Checked)
            {
                Workspace.Runtime.ManualMotion.TryJog(ushort.Parse(temp.dataAxis.axisConfig2.CardNum), (ushort)temp.dataAxis.axisConfig2.axis.AxisNum, 1);
                return;
            }
            if (TryGetStepDistance(txtMovPos2.Text, out double distance))
            {
                Workspace.Runtime.ManualMotion.TryMove(ushort.Parse(temp.dataAxis.axisConfig2.CardNum), (ushort)temp.dataAxis.axisConfig2.axis.AxisNum, distance, 0, false);
                return;
            }
            if (radioButton1.Checked)
            {
                if (txtMovPos2.Text == "")
                {
                    return;
                }
                Workspace.Runtime.ManualMotion.TryMove(ushort.Parse(temp.dataAxis.axisConfig2.CardNum), (ushort)temp.dataAxis.axisConfig2.axis.AxisNum, double.Parse(txtMovPos2.Text), 1, false);
            }
        }

        private void Handle3_MouseUp(object sender, MouseEventArgs e)
        {
            if (radioButton3.Checked)
                Workspace.Runtime.ManualMotion.TryStop(ushort.Parse(temp.dataAxis.axisConfig2.CardNum), (ushort)temp.dataAxis.axisConfig2.axis.AxisNum, 0);
        }
        private void Handle4_MouseDown(object sender, MouseEventArgs e)
        {
            Workspace.Station.SetStationParam(temp, 1);
            if (radioButton3.Checked)
            {
                Workspace.Runtime.ManualMotion.TryJog(ushort.Parse(temp.dataAxis.axisConfig2.CardNum), (ushort)temp.dataAxis.axisConfig2.axis.AxisNum, 0);
                return;
            }
            if (TryGetStepDistance(txtMovPos2.Text, out double distance))
            {
                Workspace.Runtime.ManualMotion.TryMove(ushort.Parse(temp.dataAxis.axisConfig2.CardNum), (ushort)temp.dataAxis.axisConfig2.axis.AxisNum, -distance, 0, false);
                return;
            }
            if (radioButton1.Checked)
            {
                if (txtMovPos2.Text == "")
                {
                    return;
                }
                Workspace.Runtime.ManualMotion.TryMove(ushort.Parse(temp.dataAxis.axisConfig2.CardNum), (ushort)temp.dataAxis.axisConfig2.axis.AxisNum, double.Parse(txtMovPos2.Text), 1, false);
            }

        }

        private void Handle4_MouseUp(object sender, MouseEventArgs e)
        {
            if (radioButton3.Checked)
                Workspace.Runtime.ManualMotion.TryStop(ushort.Parse(temp.dataAxis.axisConfig2.CardNum), (ushort)temp.dataAxis.axisConfig2.axis.AxisNum, 0);
        }
        private void Handle5_MouseDown(object sender, MouseEventArgs e)
        {
            Workspace.Station.SetStationParam(temp, 2);
            if (radioButton3.Checked)
            {
                Workspace.Runtime.ManualMotion.TryJog(ushort.Parse(temp.dataAxis.axisConfig3.CardNum), (ushort)temp.dataAxis.axisConfig3.axis.AxisNum, 1);
                return;
            }
            if (TryGetStepDistance(txtMovPos3.Text, out double distance))
            {
                Workspace.Runtime.ManualMotion.TryMove(ushort.Parse(temp.dataAxis.axisConfig3.CardNum), (ushort)temp.dataAxis.axisConfig3.axis.AxisNum, distance, 0, false);
                return;
            }
            if (radioButton1.Checked)
            {
                if (txtMovPos3.Text == "")
                {
                    return;
                }
                Workspace.Runtime.ManualMotion.TryMove(ushort.Parse(temp.dataAxis.axisConfig3.CardNum), (ushort)temp.dataAxis.axisConfig3.axis.AxisNum, double.Parse(txtMovPos3.Text), 1, false);
            }
        }

        private void Handle5_MouseUp(object sender, MouseEventArgs e)
        {
            if (radioButton3.Checked)
                Workspace.Runtime.ManualMotion.TryStop(ushort.Parse(temp.dataAxis.axisConfig3.CardNum), (ushort)temp.dataAxis.axisConfig3.axis.AxisNum, 0);
        }

        private void Handle6_MouseDown(object sender, MouseEventArgs e)
        {
            Workspace.Station.SetStationParam(temp, 2);
            if (radioButton3.Checked)
            {
                Workspace.Runtime.ManualMotion.TryJog(ushort.Parse(temp.dataAxis.axisConfig3.CardNum), (ushort)temp.dataAxis.axisConfig3.axis.AxisNum, 0);
                return;
            }
            if (TryGetStepDistance(txtMovPos3.Text, out double distance))
            {
                Workspace.Runtime.ManualMotion.TryMove(ushort.Parse(temp.dataAxis.axisConfig3.CardNum), (ushort)temp.dataAxis.axisConfig3.axis.AxisNum, -distance, 0, false);
                return;
            }
            if (radioButton1.Checked)
            {
                if (txtMovPos3.Text == "")
                {
                    return;
                }
                Workspace.Runtime.ManualMotion.TryMove(ushort.Parse(temp.dataAxis.axisConfig3.CardNum), (ushort)temp.dataAxis.axisConfig3.axis.AxisNum, double.Parse(txtMovPos3.Text), 1, false);
            }

        }

        private void Handle6_MouseUp(object sender, MouseEventArgs e)
        {
            if (radioButton3.Checked)
                Workspace.Runtime.ManualMotion.TryStop(ushort.Parse(temp.dataAxis.axisConfig3.CardNum), (ushort)temp.dataAxis.axisConfig3.axis.AxisNum, 0);
        }

        private void Handle7_MouseDown(object sender, MouseEventArgs e)
        {
            Workspace.Station.SetStationParam(temp, 3);
            if (radioButton3.Checked)
            {
                Workspace.Runtime.ManualMotion.TryJog(ushort.Parse(temp.dataAxis.axisConfig4.CardNum), (ushort)temp.dataAxis.axisConfig4.axis.AxisNum, 1);
                return;
            }
            if (TryGetStepDistance(txtMovPos4.Text, out double distance))
            {
                Workspace.Runtime.ManualMotion.TryMove(ushort.Parse(temp.dataAxis.axisConfig4.CardNum), (ushort)temp.dataAxis.axisConfig4.axis.AxisNum, distance, 0, false);
                return;
            }
            if (radioButton1.Checked)
            {
                if (txtMovPos4.Text == "")
                {
                    return;
                }
                Workspace.Runtime.ManualMotion.TryMove(ushort.Parse(temp.dataAxis.axisConfig4.CardNum), (ushort)temp.dataAxis.axisConfig4.axis.AxisNum, double.Parse(txtMovPos4.Text), 1, false);
            }
        }

        private void Handle7_MouseUp(object sender, MouseEventArgs e)
        {
            if (radioButton3.Checked)
                Workspace.Runtime.ManualMotion.TryStop(ushort.Parse(temp.dataAxis.axisConfig4.CardNum), (ushort)temp.dataAxis.axisConfig4.axis.AxisNum, 0);
        }

        private void Handle8_MouseDown(object sender, MouseEventArgs e)
        {
            Workspace.Station.SetStationParam(temp, 3);
            if (radioButton3.Checked)
            {
                Workspace.Runtime.ManualMotion.TryJog(ushort.Parse(temp.dataAxis.axisConfig4.CardNum), (ushort)temp.dataAxis.axisConfig4.axis.AxisNum, 0);
                return;
            }
            if (TryGetStepDistance(txtMovPos4.Text, out double distance))
            {
                Workspace.Runtime.ManualMotion.TryMove(ushort.Parse(temp.dataAxis.axisConfig4.CardNum), (ushort)temp.dataAxis.axisConfig4.axis.AxisNum, -distance, 0, false);
                return;
            }
            if (radioButton1.Checked)
            {
                if (txtMovPos4.Text == "")
                {
                    return;
                }
                Workspace.Runtime.ManualMotion.TryMove(ushort.Parse(temp.dataAxis.axisConfig4.CardNum), (ushort)temp.dataAxis.axisConfig4.axis.AxisNum, double.Parse(txtMovPos4.Text), 1, false);
            }

        }

        private void Handle8_MouseUp(object sender, MouseEventArgs e)
        {
            if (radioButton3.Checked)
                Workspace.Runtime.ManualMotion.TryStop(ushort.Parse(temp.dataAxis.axisConfig4.CardNum), (ushort)temp.dataAxis.axisConfig4.axis.AxisNum, 0);
        }

        private void Handle9_MouseDown(object sender, MouseEventArgs e)
        {
            Workspace.Station.SetStationParam(temp, 4);
            if (radioButton3.Checked)
            {
                Workspace.Runtime.ManualMotion.TryJog(ushort.Parse(temp.dataAxis.axisConfig5.CardNum), (ushort)temp.dataAxis.axisConfig5.axis.AxisNum, 1);
                return;
            }
            if (TryGetStepDistance(txtMovPos5.Text, out double distance))
            {
                Workspace.Runtime.ManualMotion.TryMove(ushort.Parse(temp.dataAxis.axisConfig5.CardNum), (ushort)temp.dataAxis.axisConfig5.axis.AxisNum, distance, 0, false);
                return;
            }
            if (radioButton1.Checked)
            {
                if (txtMovPos5.Text == "")
                {
                    return;
                }
                Workspace.Runtime.ManualMotion.TryMove(ushort.Parse(temp.dataAxis.axisConfig5.CardNum), (ushort)temp.dataAxis.axisConfig5.axis.AxisNum, double.Parse(txtMovPos5.Text), 1, false);
            }
        }

        private void Handle9_MouseUp(object sender, MouseEventArgs e)
        {
            if (radioButton3.Checked)
                Workspace.Runtime.ManualMotion.TryStop(ushort.Parse(temp.dataAxis.axisConfig5.CardNum), (ushort)temp.dataAxis.axisConfig5.axis.AxisNum, 0);
        }

        private void Handle10_MouseDown(object sender, MouseEventArgs e)
        {
            Workspace.Station.SetStationParam(temp, 4);
            if (radioButton3.Checked)
            {
                Workspace.Runtime.ManualMotion.TryJog(ushort.Parse(temp.dataAxis.axisConfig5.CardNum), (ushort)temp.dataAxis.axisConfig5.axis.AxisNum, 0);
                return;
            }
            if (TryGetStepDistance(txtMovPos5.Text, out double distance))
            {
                Workspace.Runtime.ManualMotion.TryMove(ushort.Parse(temp.dataAxis.axisConfig5.CardNum), (ushort)temp.dataAxis.axisConfig5.axis.AxisNum, -distance, 0, false);
                return;
            }
            if (radioButton1.Checked)
            {
                if (txtMovPos5.Text == "")
                {
                    return;
                }
                Workspace.Runtime.ManualMotion.TryMove(ushort.Parse(temp.dataAxis.axisConfig5.CardNum), (ushort)temp.dataAxis.axisConfig5.axis.AxisNum, double.Parse(txtMovPos5.Text), 1, false);
            }

        }

        private void Handle10_MouseUp(object sender, MouseEventArgs e)
        {
            if (radioButton3.Checked)
                Workspace.Runtime.ManualMotion.TryStop(ushort.Parse(temp.dataAxis.axisConfig5.CardNum), (ushort)temp.dataAxis.axisConfig5.axis.AxisNum, 0);
        }

        private void Handle11_MouseDown(object sender, MouseEventArgs e)
        {
            Workspace.Station.SetStationParam(temp, 5);
            if (radioButton3.Checked)
            {
                Workspace.Runtime.ManualMotion.TryJog(ushort.Parse(temp.dataAxis.axisConfig6.CardNum), (ushort)temp.dataAxis.axisConfig6.axis.AxisNum, 1);
                return;
            }
            if (TryGetStepDistance(txtMovPos6.Text, out double distance))
            {
                Workspace.Runtime.ManualMotion.TryMove(ushort.Parse(temp.dataAxis.axisConfig6.CardNum), (ushort)temp.dataAxis.axisConfig6.axis.AxisNum, distance, 0, false);
                return;
            }
            if (radioButton1.Checked)
            {
                if (txtMovPos6.Text == "")
                {
                    return;
                }
                Workspace.Runtime.ManualMotion.TryMove(ushort.Parse(temp.dataAxis.axisConfig6.CardNum), (ushort)temp.dataAxis.axisConfig6.axis.AxisNum, double.Parse(txtMovPos6.Text), 1, false);
            }
        }

        private void Handle11_MouseUp(object sender, MouseEventArgs e)
        {
            if (radioButton3.Checked)
                Workspace.Runtime.ManualMotion.TryStop(ushort.Parse(temp.dataAxis.axisConfig6.CardNum), (ushort)temp.dataAxis.axisConfig6.axis.AxisNum, 0);
        }

        private void Handle12_MouseDown(object sender, MouseEventArgs e)
        {
            Workspace.Station.SetStationParam(temp, 5);
            if (radioButton3.Checked)
            {
                Workspace.Runtime.ManualMotion.TryJog(ushort.Parse(temp.dataAxis.axisConfig6.CardNum), (ushort)temp.dataAxis.axisConfig6.axis.AxisNum, 0);
                return;
            }
            if (TryGetStepDistance(txtMovPos6.Text, out double distance))
            {
                Workspace.Runtime.ManualMotion.TryMove(ushort.Parse(temp.dataAxis.axisConfig6.CardNum), (ushort)temp.dataAxis.axisConfig6.axis.AxisNum, -distance, 0, false);
                return;
            }
            if (radioButton1.Checked)
            {
                if (txtMovPos6.Text == "")
                {
                    return;
                }
                Workspace.Runtime.ManualMotion.TryMove(ushort.Parse(temp.dataAxis.axisConfig6.CardNum), (ushort)temp.dataAxis.axisConfig6.axis.AxisNum, double.Parse(txtMovPos6.Text), 1, false);
            }

        }

        private void Handle12_MouseUp(object sender, MouseEventArgs e)
        {
            if (radioButton3.Checked)
                Workspace.Runtime.ManualMotion.TryStop(ushort.Parse(temp.dataAxis.axisConfig6.CardNum), (ushort)temp.dataAxis.axisConfig6.axis.AxisNum, 0);
        }

        private async void btnStationHome_Click(object sender, EventArgs e)
        {
            try
            {
                int stationIndex = comboBox1.SelectedIndex;
                bool hasHomeSequence = stationIndex >= 0
                    && Workspace.Card?.dataStation != null
                    && stationIndex < Workspace.Card.dataStation.Count
                    && Workspace.Card.dataStation[stationIndex].homeSeq?.axisSeq?.Any(item => item?.Name != "-1") == true;

                if (hasHomeSequence)
                {
                    await HomeStationByseq(stationIndex);
                }
                else
                {
                    await HomeStationByAll(stationIndex);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "工站回零失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async void btnStop_Click(object sender, EventArgs e)
        {
            try
            {
                await StopStationAsync(temp);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "工站停止失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void trackBar1_MouseUp(object sender, MouseEventArgs e)
        {
            if (temp == null)
                return;
            double originalPercent = temp.ManualSpeedPercent;
            label6.Text = trackBar1.Value.ToString() + "%";
            temp.ManualSpeedPercent = trackBar1.Value;
            if (!Workspace.Runtime.Stores.Stations.TryPersistCurrent(
                    Workspace.Runtime.Paths.ConfigPath, out string error))
            {
                temp.ManualSpeedPercent = originalPercent;
                MessageBox.Show(error, "工站配置", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void txtMovPos1_KeyPress(object sender, KeyPressEventArgs e)
        {
            System.Windows.Forms.TextBox textBox = (System.Windows.Forms.TextBox)sender;
            if(e.KeyChar == '.'&& textBox.Text == "")
                e.Handled = true;
            if ((e.KeyChar < '0' || e.KeyChar > '9') && e.KeyChar != '.' && e.KeyChar != '-' && e.KeyChar != (char)Keys.Back) // 判断按下的键是否为数字、小数点、负号或退格键
            {
                e.Handled = true;
            }
            else if (e.KeyChar == '.' && textBox.Text.Contains('.')) // 判断小数点个数是否超过1个
            {
                e.Handled = true;
            }
            else if (e.KeyChar == '-' && textBox.SelectionStart != 0) // 判断负号位置是否正确
            {
                e.Handled = true;
            }
        }

        private void pictureBox1_Click(object sender, EventArgs e)
        {
            int index = Workspace.Control.pictureBoxes.IndexOf((PictureBox)sender);
            if (Workspace.Control.temp.dataAxis.axisConfigs[index].CardNum == "-1")
                return;
            bool isSevon = Workspace.Runtime.Motion.GetAxisSevon(ushort.Parse(Workspace.Control.temp.dataAxis.axisConfigs[index].CardNum),(ushort)Workspace.Control.temp.dataAxis.axisConfigs[index].axis.AxisNum);
            if (!isSevon)
            {
                if (DialogResult.OK == MessageBox.Show($"确定开轴{index}使能吗？", "提示", MessageBoxButtons.OKCancel))
                {
                    Workspace.Runtime.Motion.SetAxisSevon(ushort.Parse(Workspace.Control.temp.dataAxis.axisConfigs[index].CardNum), (ushort)Workspace.Control.temp.dataAxis.axisConfigs[index].axis.AxisNum,true);
                }
                else
                {
                    return;
                }
            }
            else
            {
                if (DialogResult.OK == MessageBox.Show($"确定断轴{index}使能吗？", "提示", MessageBoxButtons.OKCancel))
                {
                    Workspace.Runtime.Motion.SetAxisSevon(ushort.Parse(Workspace.Control.temp.dataAxis.axisConfigs[index].CardNum), (ushort)Workspace.Control.temp.dataAxis.axisConfigs[index].axis.AxisNum, false);
                }
                else
                {
                    return;
                }
            }
            
        }

        private async void btnReSet_Click(object sender, EventArgs e)
        {
            try
            {
                await ResetStationAsync(temp);
                MessageBox.Show("当前工站报警复位完成。", "工站复位", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "工站复位失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task StopStationAsync(DataStation station)
        {
            List<AxisCommandRequest> axes = GetStationAxisRequests(station);
            if (!Workspace.Runtime.ProcessEngine.TryReserveManualMotionResources(axes, out IDisposable lease, out string error))
            {
                throw new InvalidOperationException(error);
            }
            using (lease)
            {
                await Task.Run(() => StopAxesAndWait(axes, 30000));
            }
        }

        private async Task ResetStationAsync(DataStation station)
        {
            List<AxisCommandRequest> axes = GetStationAxisRequests(station);
            if (!Workspace.Runtime.ProcessEngine.TryReserveManualMotionResources(axes, out IDisposable lease, out string error))
            {
                throw new InvalidOperationException(error);
            }
            using (lease)
            {
                await Task.Run(() =>
                {
                    StopAxesAndWait(axes, 30000);
                    foreach (AxisCommandRequest request in axes)
                    {
                        Workspace.Runtime.Motion.ResetAxisAlarm(request.Card, request.Axis);
                    }
                    Thread.Sleep(200);
                    foreach (AxisCommandRequest request in axes)
                    {
                        uint ioStatus = Workspace.Runtime.Motion.GetAxisIoStatus(request.Card, request.Axis);
                        if ((ioStatus & 1u) != 0)
                        {
                            throw new InvalidOperationException($"轴报警复位后仍然有效:{request.Card}-{request.Axis}");
                        }
                        if ((ioStatus & (1u << 3)) != 0)
                        {
                            throw new InvalidOperationException($"轴急停信号尚未解除:{request.Card}-{request.Axis}");
                        }
                        if (!Workspace.Runtime.Motion.GetInPos(request.Card, request.Axis))
                        {
                            throw new InvalidOperationException($"轴复位后未处于停止状态:{request.Card}-{request.Axis}");
                        }
                        if (!Workspace.Runtime.Motion.GetAxisSevon(request.Card, request.Axis))
                        {
                            throw new InvalidOperationException($"轴报警已清除，但伺服尚未使能:{request.Card}-{request.Axis}");
                        }
                        if (!Workspace.Runtime.Motion.HomeStatus(request.Card, request.Axis))
                        {
                            throw new InvalidOperationException($"轴报警已清除，但需要重新回原:{request.Card}-{request.Axis}");
                        }
                    }
                });
            }
        }

        private static List<AxisCommandRequest> GetStationAxisRequests(DataStation station)
        {
            if (station?.dataAxis?.axisConfigs == null)
            {
                throw new InvalidOperationException("工站轴配置为空。");
            }
            var axes = new List<AxisCommandRequest>();
            foreach (AxisConfig axisConfig in station.dataAxis.axisConfigs)
            {
                if (axisConfig?.AxisName == "-1")
                {
                    continue;
                }
                if (axisConfig?.axis == null || !ushort.TryParse(axisConfig.CardNum, out ushort card))
                {
                    throw new InvalidOperationException($"工站轴配置无效:{axisConfig?.CardNum}");
                }
                axes.Add(new AxisCommandRequest(card, (ushort)axisConfig.axis.AxisNum, AxisCommandKind.Motion));
            }
            if (axes.Count == 0)
            {
                throw new InvalidOperationException("工站没有配置任何轴。");
            }
            return axes.GroupBy(item => ((long)item.Card << 32) | item.Axis).Select(group => group.First()).ToList();
        }

        private void StopAxesAndWait(IReadOnlyCollection<AxisCommandRequest> axes, int timeoutMilliseconds)
        {
            try
            {
                foreach (AxisCommandRequest request in axes)
                {
                    Workspace.Runtime.Motion.StopOneAxis(request.Card, request.Axis, 0);
                }
            }
            catch (Exception ex)
            {
                foreach (AxisCommandRequest request in axes)
                {
                    try
                    {
                        Workspace.Runtime.Motion.StopOneAxis(request.Card, request.Axis, 1);
                    }
                    catch
                    {
                    }
                }
                Workspace.Runtime.Safety.Lock($"工站停止指令下发失败，目标轴已尝试急停:{ex.Message}");
                throw new InvalidOperationException("工站停止指令下发失败，系统已锁定。", ex);
            }
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (axes.Any(request => !Workspace.Runtime.Motion.GetInPos(request.Card, request.Axis)))
            {
                if (stopwatch.ElapsedMilliseconds > timeoutMilliseconds)
                {
                    foreach (AxisCommandRequest request in axes)
                    {
                        Workspace.Runtime.Motion.StopOneAxis(request.Card, request.Axis, 1);
                    }
                    Workspace.Runtime.Safety.Lock("工站停止超时，所有目标轴已急停。");
                    throw new TimeoutException("工站停止超时，所有目标轴已急停并锁定系统。");
                }
                Thread.Sleep(5);
            }
        }

        public void StopAxis(int card,int axis)
        {
            Workspace.Runtime.ManualMotion.TryStop((ushort)card, (ushort)axis, 0);
        }
    }

}
