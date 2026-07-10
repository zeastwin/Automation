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
using static Automation.FrmCard;
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
        public System.Drawing.Image validImage = Properties.Resources.vaild;
        public System.Drawing.Image invalidImage = Properties.Resources.invalid;
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
            if (SF.frmStation != null && SF.frmStation.IsPointEditing)
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
            if (SF.frmCard == null || SF.frmCard.dataStation == null || selectedIndex >= SF.frmCard.dataStation.Count)
            {
                temp = null;
                lastStationIndex = -1;
                return;
            }

            temp = SF.frmCard.dataStation[selectedIndex];
            trackBar1.Value = (int)(temp.Vel * 100);
            label6.Text = trackBar1.Value.ToString() + "%";
            bindingSource.DataSource = temp.ListDataPos;
            SF.frmStation.dataGridView1.DataSource = bindingSource;
            SF.frmStation.dataGridView1.Columns[0].HeaderText = "索引";
            SF.frmStation.dataGridView1.Columns[1].HeaderText = "名称";
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
                SF.frmStation.dataGridView1.Columns[i + 2].HeaderText = temp.dataAxis.axisConfigs[i].AxisName;
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

            SF.frmStation.RefleshDgvState();
            lastStationIndex = selectedIndex;
        }
    
        //轴按顺序回原
        public async Task HomeStationByseq(int dataStationIndex)
        {
            if (SF.frmCard?.dataStation == null || dataStationIndex < 0 || dataStationIndex >= SF.frmCard.dataStation.Count)
            {
                throw new InvalidOperationException("工站索引无效");
            }
            DataStation station = SF.frmCard.dataStation[dataStationIndex];
            if (station.homeSeq?.axisSeq == null || station.dataAxis?.axisConfigs == null)
            {
                throw new InvalidOperationException("工站回零配置不完整");
            }
            station.SetState(DataStation.Status.NotReady);
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
            station.SetState(DataStation.Status.Ready);
        }

        //所有轴同步回
        public async Task HomeStationByAll(int dataStationIndex)
        {
            if (SF.frmCard?.dataStation == null || dataStationIndex < 0 || dataStationIndex >= SF.frmCard.dataStation.Count)
            {
                throw new InvalidOperationException("工站索引无效");
            }
            DataStation station = SF.frmCard.dataStation[dataStationIndex];
            if (station.dataAxis?.axisConfigs == null)
            {
                throw new InvalidOperationException("工站回零配置不完整");
            }
            station.SetState(DataStation.Status.NotReady);
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
            station.SetState(DataStation.Status.Ready);
        }

        public void HomeSingleAxis(ushort cardNum, ushort axis)
        {
            if (SF.DR == null || !SF.DR.TryValidateStartGate(out _))
            {
                throw new InvalidOperationException("系统尚未复位完成，禁止手动回零。");
            }
            if (!SF.DR.TryAcquireManualMotionResource(cardNum, axis, out string resourceError))
            {
                throw new InvalidOperationException(resourceError);
            }
            Axis axisInfo = null;
            bool completed = false;
            try
            {
            if (!SF.motion.GetInPos(cardNum, axis))
            {
                throw new InvalidOperationException($"轴正在运动，禁止启动回零:{cardNum}-{axis}");
            }
            ushort dir = 0;
            if (SF.cardStore == null || !SF.cardStore.TryGetAxis(cardNum, axis, out axisInfo)
                || axisInfo.PulseToMM <= 0 || axisInfo.AccMax <= 0 || axisInfo.DecMax <= 0
                || !double.TryParse(axisInfo.LimitSpeed, out double limitSpeed) || limitSpeed <= 0
                || !double.TryParse(axisInfo.HomeSpeed, out double homeSpeed) || homeSpeed <= 0)
            {
                throw new InvalidOperationException($"轴回零参数无效:{cardNum}-{axis}");
            }
            int sfc = 1;
            if (axisInfo.HomeType == "从当前位回零")
            {
                sfc = 10;
            }
            int IOindex = 3;
            if (axisInfo.HomeType == "从正限位回零")
            {
                dir = 1;
                IOindex = 2;
            }
            int IOindexTemp = IOindex == 2 ? 3 : 2;
            Stopwatch timeout = Stopwatch.StartNew();
            while (!completed)
            {
                if (!SF.DR.TryValidateStartGate(out _))
                {
                    throw new InvalidOperationException("回零过程中复位状态失效");
                }
                if (timeout.ElapsedMilliseconds > 120000)
                {
                    throw new TimeoutException($"轴回零超时:{cardNum}-{axis}");
                }
                switch (sfc)
                {
                    case 1:
                        using (SF.motion.ValidateAxesForCommand(new[]
                        {
                            new AxisCommandRequest(cardNum, axis, AxisCommandKind.Home)
                        }))
                        {
                            SF.motion.SetMovParam(cardNum, axis, 0, limitSpeed, axisInfo.AccMax, axisInfo.DecMax, 0, 0, axisInfo.PulseToMM);
                            SF.motion.Jog(cardNum, axis, dir);
                        }
                        Thread.Sleep(20);
                        sfc = 2;
                        break;
                    case 2:
                        if (GetAxisStateBit(cardNum, axis, IOindexTemp))
                        {
                            Thread.Sleep(1000);
                            if (GetAxisStateBit(cardNum, axis, IOindexTemp))
                            {
                                throw new InvalidOperationException("限位方向错误，回零失败。");
                            }
                        }
                        if (GetAxisStateBit(cardNum, axis, IOindex))
                        {
                            SF.motion.StopOneAxis(cardNum, axis, 0);
                            while (!SF.motion.GetInPos(cardNum, axis))
                            {
                                Thread.Sleep(5);
                            }
                            sfc = 10;
                            break;
                        }
                        Thread.Sleep(20);
                        break;
                    case 10:
                        using (SF.motion.ValidateAxesForCommand(new[]
                        {
                            new AxisCommandRequest(cardNum, axis, AxisCommandKind.Home)
                        }))
                        {
                            SF.motion.SetMovParam(cardNum, axis, 0, homeSpeed, axisInfo.AccMax, axisInfo.DecMax, 0, 0, axisInfo.PulseToMM);
                            if (axisInfo.HomeType != "从当前位回零")
                            {
                                SF.motion.SettHomeParam(cardNum, axis, dir, 1, 1);
                            }
                            SF.motion.StartHome(cardNum, axis);
                        }
                        Thread.Sleep(20);
                        sfc = 20;
                        break;
                    case 20:
                        if (SF.motion.GetInPos(cardNum, axis))
                        {
                            Thread.Sleep(300);
                            if (SF.motion.HomeStatus(cardNum, axis))
                            {
                                SF.motion.CleanPos(cardNum, axis);
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
                        SF.motion?.StopOneAxis(cardNum, axis, 0);
                    }
                    catch (Exception ex)
                    {
                        SF.DR?.Logger?.Log($"手动回零失败后停止轴异常:{cardNum}-{axis} {ex.Message}", LogLevel.Error);
                    }
                }
                SF.DR?.ReleaseManualMotionResource(cardNum, axis);
            }
        }

        private bool GetAxisStateBit(ushort cardNum, ushort axis, int bitIndex)
        {
            if (bitIndex <= 0)
            {
                return false;
            }
            lock (SF.mainfrm.StateDicLock)
            {
                if (cardNum >= SF.mainfrm.StateDic.Count)
                {
                    return false;
                }
                Dictionary<int, char[]> axisStates = SF.mainfrm.StateDic[cardNum];
                if (axisStates == null || !axisStates.TryGetValue(axis, out char[] state) || state == null)
                {
                    return false;
                }
                if (state.Length < bitIndex)
                {
                    return false;
                }
                return state[state.Length - bitIndex] == '1';
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
            SF.frmStation.SetStationParam(temp,0);
            if (radioButton3.Checked)
            {
                SF.motion.Jog(ushort.Parse(temp.dataAxis.axisConfig1.CardNum),(ushort)temp.dataAxis.axisConfig1.axis.AxisNum, 1);
                return;
            }
            if (TryGetStepDistance(txtMovPos1.Text, out double distance))
            {
                SF.motion.Mov(ushort.Parse(temp.dataAxis.axisConfig1.CardNum), (ushort)temp.dataAxis.axisConfig1.axis.AxisNum, distance, 0, false);
                return;
            }
            if (radioButton1.Checked)
            {
                if (txtMovPos1.Text == "")
                {
                    return;
                }
                SF.motion.Mov(ushort.Parse(temp.dataAxis.axisConfig1.CardNum), (ushort)temp.dataAxis.axisConfig1.axis.AxisNum, double.Parse(txtMovPos1.Text), 1, false);
            }

        }

        private void Handle1_MouseUp(object sender, MouseEventArgs e)
        {
            if (radioButton3.Checked)
                SF.motion.StopOneAxis(ushort.Parse(temp.dataAxis.axisConfig1.CardNum), (ushort)temp.dataAxis.axisConfig1.axis.AxisNum, 0);
        }

        private void Handle2_MouseDown(object sender, MouseEventArgs e)
        {
            SF.frmStation.SetStationParam(temp, 0);
            if (radioButton3.Checked)
            {
                SF.motion.Jog(ushort.Parse(temp.dataAxis.axisConfig1.CardNum), (ushort)temp.dataAxis.axisConfig1.axis.AxisNum, 0);
                return;
            }
            if (TryGetStepDistance(txtMovPos1.Text, out double distance))
            {
                SF.motion.Mov(ushort.Parse(temp.dataAxis.axisConfig1.CardNum), (ushort)temp.dataAxis.axisConfig1.axis.AxisNum, -distance, 0, false);
                return;
            }
            if (radioButton1.Checked)
            {
                if (txtMovPos1.Text == "")
                {
                    return;
                }
                SF.motion.Mov(ushort.Parse(temp.dataAxis.axisConfig1.CardNum), (ushort)temp.dataAxis.axisConfig1.axis.AxisNum, double.Parse(txtMovPos1.Text), 1, false);
            }
        }

        private void Handle2_MouseUp(object sender, MouseEventArgs e)
        {
            if (radioButton3.Checked)
                SF.motion.StopOneAxis(ushort.Parse(temp.dataAxis.axisConfig1.CardNum), (ushort)temp.dataAxis.axisConfig1.axis.AxisNum, 0);
        }

        private void Handle3_MouseDown(object sender, MouseEventArgs e)
        {
            SF.frmStation.SetStationParam(temp, 1);
            if (radioButton3.Checked)
            {
                SF.motion.Jog(ushort.Parse(temp.dataAxis.axisConfig2.CardNum), (ushort)temp.dataAxis.axisConfig2.axis.AxisNum, 1);
                return;
            }
            if (TryGetStepDistance(txtMovPos2.Text, out double distance))
            {
                SF.motion.Mov(ushort.Parse(temp.dataAxis.axisConfig2.CardNum), (ushort)temp.dataAxis.axisConfig2.axis.AxisNum, distance, 0, false);
                return;
            }
            if (radioButton1.Checked)
            {
                if (txtMovPos2.Text == "")
                {
                    return;
                }
                SF.motion.Mov(ushort.Parse(temp.dataAxis.axisConfig2.CardNum), (ushort)temp.dataAxis.axisConfig2.axis.AxisNum, double.Parse(txtMovPos2.Text), 1, false);
            }
        }

        private void Handle3_MouseUp(object sender, MouseEventArgs e)
        {
            if (radioButton3.Checked)
                SF.motion.StopOneAxis(ushort.Parse(temp.dataAxis.axisConfig2.CardNum), (ushort)temp.dataAxis.axisConfig2.axis.AxisNum, 0);
        }
        private void Handle4_MouseDown(object sender, MouseEventArgs e)
        {
            SF.frmStation.SetStationParam(temp, 1);
            if (radioButton3.Checked)
            {
                SF.motion.Jog(ushort.Parse(temp.dataAxis.axisConfig2.CardNum), (ushort)temp.dataAxis.axisConfig2.axis.AxisNum, 0);
                return;
            }
            if (TryGetStepDistance(txtMovPos2.Text, out double distance))
            {
                SF.motion.Mov(ushort.Parse(temp.dataAxis.axisConfig2.CardNum), (ushort)temp.dataAxis.axisConfig2.axis.AxisNum, -distance, 0, false);
                return;
            }
            if (radioButton1.Checked)
            {
                if (txtMovPos2.Text == "")
                {
                    return;
                }
                SF.motion.Mov(ushort.Parse(temp.dataAxis.axisConfig2.CardNum), (ushort)temp.dataAxis.axisConfig2.axis.AxisNum, double.Parse(txtMovPos2.Text), 1, false);
            }

        }

        private void Handle4_MouseUp(object sender, MouseEventArgs e)
        {
            if (radioButton3.Checked)
                SF.motion.StopOneAxis(ushort.Parse(temp.dataAxis.axisConfig2.CardNum), (ushort)temp.dataAxis.axisConfig2.axis.AxisNum, 0);
        }
        private void Handle5_MouseDown(object sender, MouseEventArgs e)
        {
            SF.frmStation.SetStationParam(temp, 2);
            if (radioButton3.Checked)
            {
                SF.motion.Jog(ushort.Parse(temp.dataAxis.axisConfig3.CardNum), (ushort)temp.dataAxis.axisConfig3.axis.AxisNum, 1);
                return;
            }
            if (TryGetStepDistance(txtMovPos3.Text, out double distance))
            {
                SF.motion.Mov(ushort.Parse(temp.dataAxis.axisConfig3.CardNum), (ushort)temp.dataAxis.axisConfig3.axis.AxisNum, distance, 0, false);
                return;
            }
            if (radioButton1.Checked)
            {
                if (txtMovPos3.Text == "")
                {
                    return;
                }
                SF.motion.Mov(ushort.Parse(temp.dataAxis.axisConfig3.CardNum), (ushort)temp.dataAxis.axisConfig3.axis.AxisNum, double.Parse(txtMovPos3.Text), 1, false);
            }
        }

        private void Handle5_MouseUp(object sender, MouseEventArgs e)
        {
            if (radioButton3.Checked)
                SF.motion.StopOneAxis(ushort.Parse(temp.dataAxis.axisConfig3.CardNum), (ushort)temp.dataAxis.axisConfig3.axis.AxisNum, 0);
        }

        private void Handle6_MouseDown(object sender, MouseEventArgs e)
        {
            SF.frmStation.SetStationParam(temp, 2);
            if (radioButton3.Checked)
            {
                SF.motion.Jog(ushort.Parse(temp.dataAxis.axisConfig3.CardNum), (ushort)temp.dataAxis.axisConfig3.axis.AxisNum, 0);
                return;
            }
            if (TryGetStepDistance(txtMovPos3.Text, out double distance))
            {
                SF.motion.Mov(ushort.Parse(temp.dataAxis.axisConfig3.CardNum), (ushort)temp.dataAxis.axisConfig3.axis.AxisNum, -distance, 0, false);
                return;
            }
            if (radioButton1.Checked)
            {
                if (txtMovPos3.Text == "")
                {
                    return;
                }
                SF.motion.Mov(ushort.Parse(temp.dataAxis.axisConfig3.CardNum), (ushort)temp.dataAxis.axisConfig3.axis.AxisNum, double.Parse(txtMovPos3.Text), 1, false);
            }

        }

        private void Handle6_MouseUp(object sender, MouseEventArgs e)
        {
            if (radioButton3.Checked)
                SF.motion.StopOneAxis(ushort.Parse(temp.dataAxis.axisConfig3.CardNum), (ushort)temp.dataAxis.axisConfig3.axis.AxisNum, 0);
        }

        private void Handle7_MouseDown(object sender, MouseEventArgs e)
        {
            SF.frmStation.SetStationParam(temp, 3);
            if (radioButton3.Checked)
            {
                SF.motion.Jog(ushort.Parse(temp.dataAxis.axisConfig4.CardNum), (ushort)temp.dataAxis.axisConfig4.axis.AxisNum, 1);
                return;
            }
            if (TryGetStepDistance(txtMovPos4.Text, out double distance))
            {
                SF.motion.Mov(ushort.Parse(temp.dataAxis.axisConfig4.CardNum), (ushort)temp.dataAxis.axisConfig4.axis.AxisNum, distance, 0, false);
                return;
            }
            if (radioButton1.Checked)
            {
                if (txtMovPos4.Text == "")
                {
                    return;
                }
                SF.motion.Mov(ushort.Parse(temp.dataAxis.axisConfig4.CardNum), (ushort)temp.dataAxis.axisConfig4.axis.AxisNum, double.Parse(txtMovPos4.Text), 1, false);
            }
        }

        private void Handle7_MouseUp(object sender, MouseEventArgs e)
        {
            if (radioButton3.Checked)
                SF.motion.StopOneAxis(ushort.Parse(temp.dataAxis.axisConfig4.CardNum), (ushort)temp.dataAxis.axisConfig4.axis.AxisNum, 0);
        }

        private void Handle8_MouseDown(object sender, MouseEventArgs e)
        {
            SF.frmStation.SetStationParam(temp, 3);
            if (radioButton3.Checked)
            {
                SF.motion.Jog(ushort.Parse(temp.dataAxis.axisConfig4.CardNum), (ushort)temp.dataAxis.axisConfig4.axis.AxisNum, 0);
                return;
            }
            if (TryGetStepDistance(txtMovPos4.Text, out double distance))
            {
                SF.motion.Mov(ushort.Parse(temp.dataAxis.axisConfig4.CardNum), (ushort)temp.dataAxis.axisConfig4.axis.AxisNum, -distance, 0, false);
                return;
            }
            if (radioButton1.Checked)
            {
                if (txtMovPos4.Text == "")
                {
                    return;
                }
                SF.motion.Mov(ushort.Parse(temp.dataAxis.axisConfig4.CardNum), (ushort)temp.dataAxis.axisConfig4.axis.AxisNum, double.Parse(txtMovPos4.Text), 1, false);
            }

        }

        private void Handle8_MouseUp(object sender, MouseEventArgs e)
        {
            if (radioButton3.Checked)
                SF.motion.StopOneAxis(ushort.Parse(temp.dataAxis.axisConfig4.CardNum), (ushort)temp.dataAxis.axisConfig4.axis.AxisNum, 0);
        }

        private void Handle9_MouseDown(object sender, MouseEventArgs e)
        {
            SF.frmStation.SetStationParam(temp, 4);
            if (radioButton3.Checked)
            {
                SF.motion.Jog(ushort.Parse(temp.dataAxis.axisConfig5.CardNum), (ushort)temp.dataAxis.axisConfig5.axis.AxisNum, 1);
                return;
            }
            if (TryGetStepDistance(txtMovPos5.Text, out double distance))
            {
                SF.motion.Mov(ushort.Parse(temp.dataAxis.axisConfig5.CardNum), (ushort)temp.dataAxis.axisConfig5.axis.AxisNum, distance, 0, false);
                return;
            }
            if (radioButton1.Checked)
            {
                if (txtMovPos5.Text == "")
                {
                    return;
                }
                SF.motion.Mov(ushort.Parse(temp.dataAxis.axisConfig5.CardNum), (ushort)temp.dataAxis.axisConfig5.axis.AxisNum, double.Parse(txtMovPos5.Text), 1, false);
            }
        }

        private void Handle9_MouseUp(object sender, MouseEventArgs e)
        {
            if (radioButton3.Checked)
                SF.motion.StopOneAxis(ushort.Parse(temp.dataAxis.axisConfig5.CardNum), (ushort)temp.dataAxis.axisConfig5.axis.AxisNum, 0);
        }

        private void Handle10_MouseDown(object sender, MouseEventArgs e)
        {
            SF.frmStation.SetStationParam(temp, 4);
            if (radioButton3.Checked)
            {
                SF.motion.Jog(ushort.Parse(temp.dataAxis.axisConfig5.CardNum), (ushort)temp.dataAxis.axisConfig5.axis.AxisNum, 0);
                return;
            }
            if (TryGetStepDistance(txtMovPos5.Text, out double distance))
            {
                SF.motion.Mov(ushort.Parse(temp.dataAxis.axisConfig5.CardNum), (ushort)temp.dataAxis.axisConfig5.axis.AxisNum, -distance, 0, false);
                return;
            }
            if (radioButton1.Checked)
            {
                if (txtMovPos5.Text == "")
                {
                    return;
                }
                SF.motion.Mov(ushort.Parse(temp.dataAxis.axisConfig5.CardNum), (ushort)temp.dataAxis.axisConfig5.axis.AxisNum, double.Parse(txtMovPos5.Text), 1, false);
            }

        }

        private void Handle10_MouseUp(object sender, MouseEventArgs e)
        {
            if (radioButton3.Checked)
                SF.motion.StopOneAxis(ushort.Parse(temp.dataAxis.axisConfig5.CardNum), (ushort)temp.dataAxis.axisConfig5.axis.AxisNum, 0);
        }

        private void Handle11_MouseDown(object sender, MouseEventArgs e)
        {
            SF.frmStation.SetStationParam(temp, 5);
            if (radioButton3.Checked)
            {
                SF.motion.Jog(ushort.Parse(temp.dataAxis.axisConfig6.CardNum), (ushort)temp.dataAxis.axisConfig6.axis.AxisNum, 1);
                return;
            }
            if (TryGetStepDistance(txtMovPos6.Text, out double distance))
            {
                SF.motion.Mov(ushort.Parse(temp.dataAxis.axisConfig6.CardNum), (ushort)temp.dataAxis.axisConfig6.axis.AxisNum, distance, 0, false);
                return;
            }
            if (radioButton1.Checked)
            {
                if (txtMovPos6.Text == "")
                {
                    return;
                }
                SF.motion.Mov(ushort.Parse(temp.dataAxis.axisConfig6.CardNum), (ushort)temp.dataAxis.axisConfig6.axis.AxisNum, double.Parse(txtMovPos6.Text), 1, false);
            }
        }

        private void Handle11_MouseUp(object sender, MouseEventArgs e)
        {
            if (radioButton3.Checked)
                SF.motion.StopOneAxis(ushort.Parse(temp.dataAxis.axisConfig6.CardNum), (ushort)temp.dataAxis.axisConfig6.axis.AxisNum, 0);
        }

        private void Handle12_MouseDown(object sender, MouseEventArgs e)
        {
            SF.frmStation.SetStationParam(temp, 5);
            if (radioButton3.Checked)
            {
                SF.motion.Jog(ushort.Parse(temp.dataAxis.axisConfig6.CardNum), (ushort)temp.dataAxis.axisConfig6.axis.AxisNum, 0);
                return;
            }
            if (TryGetStepDistance(txtMovPos6.Text, out double distance))
            {
                SF.motion.Mov(ushort.Parse(temp.dataAxis.axisConfig6.CardNum), (ushort)temp.dataAxis.axisConfig6.axis.AxisNum, -distance, 0, false);
                return;
            }
            if (radioButton1.Checked)
            {
                if (txtMovPos6.Text == "")
                {
                    return;
                }
                SF.motion.Mov(ushort.Parse(temp.dataAxis.axisConfig6.CardNum), (ushort)temp.dataAxis.axisConfig6.axis.AxisNum, double.Parse(txtMovPos6.Text), 1, false);
            }

        }

        private void Handle12_MouseUp(object sender, MouseEventArgs e)
        {
            if (radioButton3.Checked)
                SF.motion.StopOneAxis(ushort.Parse(temp.dataAxis.axisConfig6.CardNum), (ushort)temp.dataAxis.axisConfig6.axis.AxisNum, 0);
        }

        private async void btnStationHome_Click(object sender, EventArgs e)
        {
            try
            {
                await HomeStationByseq(comboBox1.SelectedIndex);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "工站回零失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void btnStop_Click(object sender, EventArgs e)
        {
            StopStation(temp);
        }

        private void trackBar1_MouseUp(object sender, MouseEventArgs e)
        {
            if (temp == null)
                return;
            label6.Text = trackBar1.Value.ToString() + "%";
            temp.Vel = (double)(trackBar1.Value)/100;
           // SF.frmStation.SetStationParam(temp);
            SF.mainfrm.SaveAsJson(SF.ConfigPath, "DataStation", SF.frmCard.dataStation);
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
            int index = SF.frmControl.pictureBoxes.IndexOf((PictureBox)sender);
            if (SF.frmControl.temp.dataAxis.axisConfigs[index].CardNum == "-1")
                return;
            bool isSevon = SF.motion.GetAxisSevon(ushort.Parse(SF.frmControl.temp.dataAxis.axisConfigs[index].CardNum),(ushort)SF.frmControl.temp.dataAxis.axisConfigs[index].axis.AxisNum);
            if (!isSevon)
            {
                if (DialogResult.OK == MessageBox.Show($"确定开轴{index}使能吗？", "提示", MessageBoxButtons.OKCancel))
                {
                    SF.motion.SetAxisSevon(ushort.Parse(SF.frmControl.temp.dataAxis.axisConfigs[index].CardNum), (ushort)SF.frmControl.temp.dataAxis.axisConfigs[index].axis.AxisNum,true);
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
                    SF.motion.SetAxisSevon(ushort.Parse(SF.frmControl.temp.dataAxis.axisConfigs[index].CardNum), (ushort)SF.frmControl.temp.dataAxis.axisConfigs[index].axis.AxisNum, false);
                }
                else
                {
                    return;
                }
            }
            
        }

        private void btnReSet_Click(object sender, EventArgs e)
        {
            SF.motion.CleanAlarm();
        }
        public void StopStation(DataStation temp)
        {
            for (int i = 0; i < 6; i++)
            {
                if (temp.dataAxis.axisConfigs[i].AxisName != "-1")
                {
                    temp.SetState(DataStation.Status.Ready);
                    SF.motion.StopOneAxis(ushort.Parse(temp.dataAxis.axisConfigs[i].CardNum), (ushort)temp.dataAxis.axisConfigs[i].axis.AxisNum, 0);
                }             
            }
        }

        public void StopAxis(int card,int axis)
        {
            SF.motion.StopOneAxis((ushort)card, (ushort)axis, 0);
        }
    }

}
