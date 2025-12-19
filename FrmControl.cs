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
using csLTDMC;
using static System.Collections.Specialized.BitVector32;

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
        public List<System.Windows.Forms.Label> StateLabel = new List<System.Windows.Forms.Label>();
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

            StateLabel.Add(AxisState1);
            StateLabel.Add(AxisState2);
            StateLabel.Add(AxisState3);
            StateLabel.Add(AxisState4);
            StateLabel.Add(AxisState5);
            StateLabel.Add(AxisState6);


            //InintMovParam();

        }

     //   public List<DataGridViewTextBoxColumn> dataGridViewTextBoxColumns = new List<DataGridViewTextBoxColumn>();
        public DataStation temp;
        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (comboBox1.SelectedIndex != -1)
            {
                temp = (DataStation)(comboBox1.SelectedItem);
                trackBar1.Value = (int)(temp.Vel*100);
                label6.Text = trackBar1.Value.ToString() + "%";
                bindingSource.DataSource = SF.frmCard.dataStation[comboBox1.SelectedIndex].ListDataPos;
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

            }

        }
    
       //轴按顺序回原
        public async Task HomeStationByseq(int dataStationIndex)
        {
            for (int i = 0; i < 6; i++)
            {
                List<AxisName> Seq = SF.frmCard.dataStation[dataStationIndex].homeSeq.axisSeq;
                foreach (var item in SF.frmCard.dataStation[dataStationIndex].dataAxis.axisConfigs)
                {
                    if (item.AxisName == Seq[i].Name && item.AxisName != "-1")
                    {
                        Task task = Task.Run(() =>
                        {
                            HomeSingleAxis(ushort.Parse(item.CardNum), (ushort)item.axis.AxisNum);
                        });
                        Axis axisInfo = SF.frmCard.card.controlCards[int.Parse(item.CardNum)].axis[i];
                      
                        await task;

                        if (axisInfo.State == Axis.Status.NotReady)
                        {
                            MessageBox.Show($"卡{item.CardNum}轴{i}回零失败,工站回零动作终止。");
                            return;
                        }
                        else
                        {
                            break ;
                        }
                    }
                }
            }
            for (int j = 0; j < SF.frmCard.dataStation[dataStationIndex].dataAxis.axisConfigs.Count; j++)
            {
                ushort index = (ushort)j;
                if (SF.frmCard.dataStation[dataStationIndex].dataAxis.axisConfigs[j].AxisName != "-1" && !SF.motion.HomeStatus(ushort.Parse(SF.frmCard.dataStation[dataStationIndex].dataAxis.axisConfigs[index].CardNum), (ushort)(SF.frmCard.dataStation[dataStationIndex].dataAxis.axisConfigs[index].axis.AxisNum)))
                {
                    Task task = Task.Run(() =>
                    {
                        HomeSingleAxis(ushort.Parse(SF.frmCard.dataStation[dataStationIndex].dataAxis.axisConfigs[index].CardNum), (ushort)(SF.frmCard.dataStation[dataStationIndex].dataAxis.axisConfigs[index].axis.AxisNum));
                    });
                }
            }
        }

        //所有轴同步回
        public async Task HomeStationByAll(int dataStationIndex)
        {
            for (int j = 0; j < SF.frmCard.dataStation[dataStationIndex].dataAxis.axisConfigs.Count; j++)
            {
                ushort index = (ushort)j;
                if (SF.frmCard.dataStation[dataStationIndex].dataAxis.axisConfigs[j].AxisName != "-1")
                {
                    Task task = Task.Run(() =>
                    {
                        HomeSingleAxis(ushort.Parse(SF.frmCard.dataStation[dataStationIndex].dataAxis.axisConfigs[index].CardNum), (ushort)(SF.frmCard.dataStation[dataStationIndex].dataAxis.axisConfigs[index].axis.AxisNum));
                    });
                }
            }
        }
        public void HomeSingleAxis(ushort cardNum, ushort axis)
        {
            if (!SF.motion.GetInPos(cardNum, axis))
                return;
            ushort dir = 0;
            Axis axisInfo = SF.frmCard.card.controlCards[cardNum].axis[axis];
            axisInfo.State = Axis.Status.Run;
            if (axisInfo != null)
            {
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
                int IOindexTemp = IOindex ==2?3:2;


                while (axisInfo.State == Axis.Status.Run)
                {
                    switch (sfc)
                    {
                        case 1:
                            SF.motion.SetMovParam(cardNum, axis, 0, double.Parse(axisInfo.LimitSpeed), axisInfo.AccMax, axisInfo.DecMax, 0, 0, axisInfo.PulseToMM);
                            SF.motion.Jog(cardNum,axis, dir);
                            Task.Delay(20);
                            sfc = 2;
                            break;
                        case 2:
                            if (SF.mainfrm.StateDic[cardNum][axis][SF.mainfrm.StateDic[cardNum][axis].Length - IOindex] == '1')
                            {
                                sfc = 10;
                            }
                            if (SF.mainfrm.StateDic[cardNum][axis][SF.mainfrm.StateDic[cardNum][axis].Length - IOindexTemp] == '1')
                            {
                                SF.Delay(1000);
                                if(SF.mainfrm.StateDic[cardNum][axis][SF.mainfrm.StateDic[cardNum][axis].Length - IOindexTemp] == '1')
                                {
                                    MessageBox.Show("限位方向错误，回零失败。");
                                    sfc = 0;
                                }
                                
                            }
                            Task.Delay(20); 
                            break;
                        case 10:
                            SF.motion.SetMovParam(cardNum, axis, 0, double.Parse(axisInfo.HomeSpeed), axisInfo.AccMax, axisInfo.DecMax, 0, 0, axisInfo.PulseToMM);
                            if (axisInfo.HomeType != "从当前位回零")
                            {
                                SF.motion.SettHomeParam(cardNum, axis, dir, 1, 1);
                            }
                            SF.motion.StartHome(cardNum,axis);
                            Task.Delay(20);
                            sfc = 20;
                            break;
                        case 20:
                            if (SF.motion.GetInPos(cardNum,axis))
                            {
                                Task.Delay(300);
                                if(SF.motion.HomeStatus(cardNum, axis) == true)
                                {
                                    SF.motion.CleanPos(cardNum, axis);
                                    axisInfo.State = 0;
                                    sfc = 0;
                                }
                                else
                                {
                                    MessageBox.Show("限位方向错误，回零失败。");
                                    axisInfo.State = Axis.Status.NotReady;
                                    sfc = 0;
                                    return;
                                }
                                
                            }
                            Task.Delay(20);
                            break;

                    }
                }
                
            }
        }
        private void FrmControl_Load(object sender, EventArgs e)
        {
          
        }

        private void btnHome1_Click(object sender, EventArgs e)
        {
            Task task = Task.Run(() =>
            {
                HomeSingleAxis(ushort.Parse(temp.dataAxis.axisConfig1.CardNum), (ushort)temp.dataAxis.axisConfig1.axis.AxisNum);
            });
        }

        private void btnHome2_Click(object sender, EventArgs e)
        {
            Task task = Task.Run(() =>
            {
                HomeSingleAxis(ushort.Parse(temp.dataAxis.axisConfig2.CardNum), (ushort)temp.dataAxis.axisConfig2.axis.AxisNum);
            });
        }

        private void btnHome3_Click(object sender, EventArgs e)
        {
            Task task = Task.Run(() =>
            {
                HomeSingleAxis(ushort.Parse(temp.dataAxis.axisConfig3.CardNum), (ushort)temp.dataAxis.axisConfig3.axis.AxisNum);
            });
        }

        private void btnHome4_Click(object sender, EventArgs e)
        {
            Task task = Task.Run(() =>
            {
                HomeSingleAxis(ushort.Parse(temp.dataAxis.axisConfig4.CardNum), (ushort)temp.dataAxis.axisConfig4.axis.AxisNum);
            });
        }

        private void btnHome5_Click(object sender, EventArgs e)
        {
            Task task = Task.Run(() =>
            {
                HomeSingleAxis(ushort.Parse(temp.dataAxis.axisConfig5.CardNum), (ushort)temp.dataAxis.axisConfig5.axis.AxisNum);
            });
        }

        private void btnHome6_Click(object sender, EventArgs e)
        {
            Task task = Task.Run(() =>
            {
                HomeSingleAxis(ushort.Parse(temp.dataAxis.axisConfig6.CardNum), (ushort)temp.dataAxis.axisConfig6.axis.AxisNum);
            });
        }

        private void Handle1_MouseDown(object sender, MouseEventArgs e)
        {
            SF.frmStation.SetStationParam(temp,0);
            if (radioButton3.Checked)
                SF.motion.Jog(ushort.Parse(temp.dataAxis.axisConfig1.CardNum),(ushort)temp.dataAxis.axisConfig1.axis.AxisNum, 1);
            if (txtMovPos1.Text == "")
                return;
            if (radioButton2.Checked)
                SF.motion.Mov(ushort.Parse(temp.dataAxis.axisConfig1.CardNum), (ushort)temp.dataAxis.axisConfig1.axis.AxisNum, double.Parse(txtMovPos1.Text), 0, false);
            if (radioButton1.Checked)
                SF.motion.Mov(ushort.Parse(temp.dataAxis.axisConfig1.CardNum), (ushort)temp.dataAxis.axisConfig1.axis.AxisNum, double.Parse(txtMovPos1.Text), 1, false);

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
                SF.motion.Jog(ushort.Parse(temp.dataAxis.axisConfig1.CardNum), (ushort)temp.dataAxis.axisConfig1.axis.AxisNum, 0);
            if (txtMovPos1.Text == "")
                return;
            if (radioButton2.Checked)
                SF.motion.Mov(ushort.Parse(temp.dataAxis.axisConfig1.CardNum), (ushort)temp.dataAxis.axisConfig1.axis.AxisNum, -double.Parse(txtMovPos1.Text), 0, false);
            if (radioButton1.Checked)
                SF.motion.Mov(ushort.Parse(temp.dataAxis.axisConfig1.CardNum), (ushort)temp.dataAxis.axisConfig1.axis.AxisNum, double.Parse(txtMovPos1.Text), 1, false);
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
                SF.motion.Jog(ushort.Parse(temp.dataAxis.axisConfig2.CardNum), (ushort)temp.dataAxis.axisConfig2.axis.AxisNum, 1);
            if (txtMovPos2.Text == "")
                return;
            if (radioButton2.Checked)
                SF.motion.Mov(ushort.Parse(temp.dataAxis.axisConfig2.CardNum), (ushort)temp.dataAxis.axisConfig2.axis.AxisNum, double.Parse(txtMovPos2.Text), 0, false);
            if (radioButton1.Checked)
                SF.motion.Mov(ushort.Parse(temp.dataAxis.axisConfig2.CardNum), (ushort)temp.dataAxis.axisConfig2.axis.AxisNum, double.Parse(txtMovPos2.Text), 1, false);
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
                SF.motion.Jog(ushort.Parse(temp.dataAxis.axisConfig2.CardNum), (ushort)temp.dataAxis.axisConfig2.axis.AxisNum, 0);
            if (txtMovPos2.Text == "")
                return;
            if (radioButton2.Checked)
                SF.motion.Mov(ushort.Parse(temp.dataAxis.axisConfig2.CardNum), (ushort)temp.dataAxis.axisConfig2.axis.AxisNum, -double.Parse(txtMovPos2.Text), 0, false);
            if (radioButton1.Checked)
                SF.motion.Mov(ushort.Parse(temp.dataAxis.axisConfig2.CardNum), (ushort)temp.dataAxis.axisConfig2.axis.AxisNum, double.Parse(txtMovPos2.Text), 1, false);

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
                SF.motion.Jog(ushort.Parse(temp.dataAxis.axisConfig3.CardNum), (ushort)temp.dataAxis.axisConfig3.axis.AxisNum, 1);
            if (txtMovPos3.Text == "")
                return;
            if (radioButton2.Checked)
                SF.motion.Mov(ushort.Parse(temp.dataAxis.axisConfig3.CardNum), (ushort)temp.dataAxis.axisConfig3.axis.AxisNum, double.Parse(txtMovPos3.Text), 0, false);
            if (radioButton1.Checked)
                SF.motion.Mov(ushort.Parse(temp.dataAxis.axisConfig3.CardNum), (ushort)temp.dataAxis.axisConfig3.axis.AxisNum, double.Parse(txtMovPos3.Text), 1, false);
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
                SF.motion.Jog(ushort.Parse(temp.dataAxis.axisConfig3.CardNum), (ushort)temp.dataAxis.axisConfig3.axis.AxisNum, 0);
            if (txtMovPos3.Text == "")
                return;
            if (radioButton2.Checked)
                SF.motion.Mov(ushort.Parse(temp.dataAxis.axisConfig3.CardNum), (ushort)temp.dataAxis.axisConfig3.axis.AxisNum, -double.Parse(txtMovPos3.Text), 0, false);
            if (radioButton1.Checked)
                SF.motion.Mov(ushort.Parse(temp.dataAxis.axisConfig3.CardNum), (ushort)temp.dataAxis.axisConfig3.axis.AxisNum, double.Parse(txtMovPos3.Text), 1, false);

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
                SF.motion.Jog(ushort.Parse(temp.dataAxis.axisConfig4.CardNum), (ushort)temp.dataAxis.axisConfig4.axis.AxisNum, 1);
            if (txtMovPos4.Text == "")
                return;
            if (radioButton2.Checked)
                SF.motion.Mov(ushort.Parse(temp.dataAxis.axisConfig4.CardNum), (ushort)temp.dataAxis.axisConfig4.axis.AxisNum, double.Parse(txtMovPos4.Text), 0, false);
            if (radioButton1.Checked)
                SF.motion.Mov(ushort.Parse(temp.dataAxis.axisConfig4.CardNum), (ushort)temp.dataAxis.axisConfig4.axis.AxisNum, double.Parse(txtMovPos4.Text), 1, false);
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
                SF.motion.Jog(ushort.Parse(temp.dataAxis.axisConfig4.CardNum), (ushort)temp.dataAxis.axisConfig4.axis.AxisNum, 0);
            if (txtMovPos4.Text == "")
                return;
            if (radioButton2.Checked)
                SF.motion.Mov(ushort.Parse(temp.dataAxis.axisConfig4.CardNum), (ushort)temp.dataAxis.axisConfig4.axis.AxisNum, -double.Parse(txtMovPos4.Text), 0, false);
            if (radioButton1.Checked)
                SF.motion.Mov(ushort.Parse(temp.dataAxis.axisConfig4.CardNum), (ushort)temp.dataAxis.axisConfig4.axis.AxisNum, double.Parse(txtMovPos4.Text), 1, false);

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
                SF.motion.Jog(ushort.Parse(temp.dataAxis.axisConfig5.CardNum), (ushort)temp.dataAxis.axisConfig5.axis.AxisNum, 1);
            if (txtMovPos5.Text == "")
                return;
            if (radioButton2.Checked)
                SF.motion.Mov(ushort.Parse(temp.dataAxis.axisConfig5.CardNum), (ushort)temp.dataAxis.axisConfig5.axis.AxisNum, double.Parse(txtMovPos5.Text), 0, false);
            if (radioButton1.Checked)
                SF.motion.Mov(ushort.Parse(temp.dataAxis.axisConfig5.CardNum), (ushort)temp.dataAxis.axisConfig5.axis.AxisNum, double.Parse(txtMovPos5.Text), 1, false);
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
                SF.motion.Jog(ushort.Parse(temp.dataAxis.axisConfig5.CardNum), (ushort)temp.dataAxis.axisConfig5.axis.AxisNum, 0);
            if (txtMovPos5.Text == "")
                return;
            if (radioButton2.Checked)
                SF.motion.Mov(ushort.Parse(temp.dataAxis.axisConfig5.CardNum), (ushort)temp.dataAxis.axisConfig5.axis.AxisNum, -double.Parse(txtMovPos5.Text), 0, false);
            if (radioButton1.Checked)
                SF.motion.Mov(ushort.Parse(temp.dataAxis.axisConfig5.CardNum), (ushort)temp.dataAxis.axisConfig5.axis.AxisNum, double.Parse(txtMovPos5.Text), 1, false);

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
                SF.motion.Jog(ushort.Parse(temp.dataAxis.axisConfig6.CardNum), (ushort)temp.dataAxis.axisConfig6.axis.AxisNum, 1);
            if (txtMovPos6.Text == "")
                return;
            if (radioButton2.Checked)
                SF.motion.Mov(ushort.Parse(temp.dataAxis.axisConfig6.CardNum), (ushort)temp.dataAxis.axisConfig6.axis.AxisNum, double.Parse(txtMovPos6.Text), 0, false);
            if (radioButton1.Checked)
                SF.motion.Mov(ushort.Parse(temp.dataAxis.axisConfig6.CardNum), (ushort)temp.dataAxis.axisConfig6.axis.AxisNum, double.Parse(txtMovPos6.Text), 1, false);
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
                SF.motion.Jog(ushort.Parse(temp.dataAxis.axisConfig6.CardNum), (ushort)temp.dataAxis.axisConfig6.axis.AxisNum, 0);
            if (txtMovPos6.Text == "")
                return;
            if (radioButton2.Checked)
                SF.motion.Mov(ushort.Parse(temp.dataAxis.axisConfig6.CardNum), (ushort)temp.dataAxis.axisConfig6.axis.AxisNum, -double.Parse(txtMovPos6.Text), 0, false);
            if (radioButton1.Checked)
                SF.motion.Mov(ushort.Parse(temp.dataAxis.axisConfig6.CardNum), (ushort)temp.dataAxis.axisConfig6.axis.AxisNum, double.Parse(txtMovPos6.Text), 1, false);

        }

        private void Handle12_MouseUp(object sender, MouseEventArgs e)
        {
            if (radioButton3.Checked)
                SF.motion.StopOneAxis(ushort.Parse(temp.dataAxis.axisConfig6.CardNum), (ushort)temp.dataAxis.axisConfig6.axis.AxisNum, 0);
        }

        private void btnStationHome_Click(object sender, EventArgs e)
        {
            HomeStationByseq(comboBox1.SelectedIndex);
        
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
                    temp.dataAxis.axisConfigs[i].axis.SetState(Axis.Status.Ready);
                    SF.motion.StopOneAxis(ushort.Parse(temp.dataAxis.axisConfigs[i].CardNum), (ushort)temp.dataAxis.axisConfigs[i].axis.AxisNum, 0);
                }             
            }
        }

        public void StopAxis(int card,int axis)
        {
            SF.frmCard.card.controlCards[card].axis[axis].SetState(Axis.Status.Ready);
            SF.motion.StopOneAxis((ushort)card, (ushort)axis, 0);
        }
    }

}
