using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Collections;
using Newtonsoft.Json.Linq;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using System.Runtime.InteropServices;
using csLTDMC;

namespace Automation
{
    public partial class FrmStation : Form
    {
        //鼠标选定的行数
        public int iSelectedRow = -1;
        //public int SelectCard = 0;
        public FrmStation()
        {
            InitializeComponent();

            Type dgvType = this.dataGridView1.GetType();
            PropertyInfo pi = dgvType.GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
            pi.SetValue(this.dataGridView1, true, null);

            dataGridView1.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;

            dataGridView1.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dataGridView1.ReadOnly = false;

            Type dgvType2 = this.dataGridView2.GetType();
            PropertyInfo pi2 = dgvType2.GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
            pi2.SetValue(this.dataGridView2, true, null);

            dataGridView2.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            dataGridView2.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dataGridView2.ReadOnly = true;


        }

        private void FrmStation_Load(object sender, EventArgs e)
        {
            RefleshFrmStation();

        }
        public Dictionary<int, char[]> StateDicTemp = new Dictionary<int, char[]>();
        public void RefleshDgvState()
        {
            dataGridView2.Rows.Clear();
            if (SF.frmControl.comboBox1.SelectedIndex != -1)
            {
                for (int i = 0; i < SF.frmCard.dataStation[SF.frmControl.comboBox1.SelectedIndex].dataAxis.axisConfigs.Count; i++)
                {
                    int SelectCard = int.Parse(SF.frmCard.dataStation[SF.frmControl.comboBox1.SelectedIndex].dataAxis.axisConfigs[i].CardNum);
                    if (SelectCard != -1)
                    {
                        int SelectAxis = SF.frmCard.dataStation[SF.frmControl.comboBox1.SelectedIndex].dataAxis.axisConfigs[i].axis.AxisNum;
                        if (SelectAxis != -1)
                        {
                            dataGridView2.Rows.Add();
                        }

                    }
                }
            }
            for (int i = 0; i < 6; i++)
            {
                StateDicTemp[i] = new char[] { 'A' };
            }


        }
        public System.Drawing.Image validImage = Properties.Resources.vaild;
        public System.Drawing.Image invalidImage = Properties.Resources.invalid;
        public void RefleshFrmStation()
        {
            RefleshDgvState();
            Task.Run(() =>
            {
                while (true)
                {
                    if (SF.curPage == 1)
                    {

                        try
                        {
                            Invoke(new Action(() =>
                            {
                                if (SF.frmControl.comboBox1.SelectedIndex != -1)
                                {
                                    for (int i = 0; i < SF.frmCard.dataStation[SF.frmControl.comboBox1.SelectedIndex].dataAxis.axisConfigs.Count; i++)
                                    {
                                        int SelectCard = int.Parse(SF.frmCard.dataStation[SF.frmControl.comboBox1.SelectedIndex].dataAxis.axisConfigs[i].CardNum);
                                        if (SelectCard != -1)
                                        {
                                            int SelectAxis = SF.frmCard.dataStation[SF.frmControl.comboBox1.SelectedIndex].dataAxis.axisConfigs[i].axis.AxisNum;
                                            if (SelectAxis != -1)
                                            {
                                                if (!SF.mainfrm.StateDic[SelectCard][SelectAxis].SequenceEqual(StateDicTemp[SelectAxis]))
                                                {
                                                    dataGridView2.Rows[i].Cells[0].Value = $"({SF.frmCard.dataStation[SF.frmControl.comboBox1.SelectedIndex].dataAxis.axisConfigs[i].axis.AxisName})";
                                                    dataGridView2.Rows[i].Cells[1].Value = SF.mainfrm.StateDic[SelectCard][SelectAxis][SF.mainfrm.StateDic[SelectCard][SelectAxis].Length - 1] == '1' ? validImage : invalidImage;
                                                    dataGridView2.Rows[i].Cells[2].Value = SF.mainfrm.StateDic[SelectCard][SelectAxis][SF.mainfrm.StateDic[SelectCard][SelectAxis].Length - 2] == '1' ? validImage : invalidImage;
                                                    dataGridView2.Rows[i].Cells[3].Value = SF.mainfrm.StateDic[SelectCard][SelectAxis][SF.mainfrm.StateDic[SelectCard][SelectAxis].Length - 3] == '1' ? validImage : invalidImage;
                                                    dataGridView2.Rows[i].Cells[4].Value = SF.mainfrm.StateDic[SelectCard][SelectAxis][SF.mainfrm.StateDic[SelectCard][SelectAxis].Length - 4] == '1' ? validImage : invalidImage;
                                                    dataGridView2.Rows[i].Cells[5].Value = SF.mainfrm.StateDic[SelectCard][SelectAxis][SF.mainfrm.StateDic[SelectCard][SelectAxis].Length - 5] == '1' ? validImage : invalidImage;
                                                    dataGridView2.Rows[i].Cells[6].Value = SF.mainfrm.StateDic[SelectCard][SelectAxis][SF.mainfrm.StateDic[SelectCard][SelectAxis].Length - 7] == '1' ? validImage : invalidImage;
                                                    dataGridView2.Rows[i].Cells[7].Value = SF.mainfrm.StateDic[SelectCard][SelectAxis][SF.mainfrm.StateDic[SelectCard][SelectAxis].Length - 8] == '1' ? validImage : invalidImage;
                                                    dataGridView2.Rows[i].Cells[8].Value = SF.mainfrm.StateDic[SelectCard][SelectAxis][SF.mainfrm.StateDic[SelectCard][SelectAxis].Length - 9] == '1' ? validImage : invalidImage;
                                                    dataGridView2.Rows[i].Cells[9].Value = SF.mainfrm.StateDic[SelectCard][SelectAxis][SF.mainfrm.StateDic[SelectCard][SelectAxis].Length - 10] == '1' ? validImage : invalidImage;
                                                    StateDicTemp[SelectAxis] = SF.mainfrm.StateDic[SelectCard][SelectAxis];

                                                }
                                            }
                                        }
                                    }
                                    if (SF.frmControl.temp != null)
                                    {
                                        for (int i = 0; i < 6; i++)
                                        {
                                            if (SF.frmControl.temp.dataAxis.axisConfigs[i].AxisName != "-1")
                                            {
                                                SF.frmControl.PosTextBox[i].Text = SF.motion.GetAxisPos(ushort.Parse(SF.frmControl.temp.dataAxis.axisConfigs[i].CardNum), (ushort)i).ToString();
                                                SF.frmControl.pictureBoxes[i].Image = SF.motion.GetAxisSevon(ushort.Parse(SF.frmControl.temp.dataAxis.axisConfigs[i].CardNum), (ushort)i) ? validImage : invalidImage;
                                                SF.frmControl.VelLabel[i].Text = SF.motion.GetAxisCurSpeed(ushort.Parse(SF.frmControl.temp.dataAxis.axisConfigs[i].CardNum), (ushort)i).ToString(); 
                                                SF.frmControl.StateLabel[i].Text = SF.frmControl.temp.dataAxis.axisConfigs[i].axis.State.ToString();
                                            }
                                        }
                                    }
                                }
                            }));

                        }
                        catch (Exception ex)
                        {

                        }

                    }

                    SF.Delay(100);
                }

            });
        }

        public void SetAxisMotionParam()
        {
            if (SF.frmCard.dataStation != null)
            {
                for (int x = 0; x < SF.frmCard.dataStation.Count; x++)
                {
                    //   SetStationParam(SF.frmCard.dataStation[x]);
                }
            }
        }
        public void SetStationParam(DataStation dataStation, int AxisIndex)
        {

            if (dataStation.dataAxis.axisConfigs[AxisIndex].AxisName != "-1")
            {
                ushort i = ushort.Parse(dataStation.dataAxis.axisConfigs[AxisIndex].CardNum);
                ushort j = (ushort)dataStation.dataAxis.axisConfigs[AxisIndex].axis.AxisNum;

                if (SF.cardStore.TryGetAxis(i, j, out FrmCard.Axis axis))
                {
                    SF.motion.SetMovParam(i, j, 0, axis.SpeedMax * dataStation.Vel, axis.AccMax, axis.DecMax, 0, 0, axis.PulseToMM);
                }


            }

        }


        private void dataGridView1_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0)
            {
                DataGridView dataGridView = (DataGridView)sender;
                DataGridViewRow selectedRow = dataGridView.SelectedRows[0]; // 获取选中的行
                object cellValue = dataGridView.Rows[e.RowIndex].Cells[1].Value;
                if (cellValue == null || cellValue.ToString() == "")
                {
                    return;
                }
                else
                {
                    SF.frmControl.temp.dicDataPos[dataGridView.Rows[e.RowIndex].Cells[1].Value.ToString()] = (DataPos)(selectedRow.DataBoundItem);
                    SF.frmControl.temp.ListDataPos[((DataPos)selectedRow.DataBoundItem).Index] = (DataPos)(selectedRow.DataBoundItem);
                    SF.mainfrm.SaveAsJson(SF.ConfigPath, "DataStation", SF.frmCard.dataStation);
                }
            }
        }

        private void Touch_Click(object sender, EventArgs e)
        {
            for (int i = 0; i < SF.frmControl.temp.dataAxis.axisConfigs.Count; i++)
            {
                if (SF.frmControl.temp.dataAxis.axisConfigs[i].axis != null)
                {
                    dataGridView1.Rows[iSelectedRow].Cells[2 + i].Value = SF.motion.GetAxisPos(ushort.Parse(SF.frmControl.temp.dataAxis.axisConfigs[i].CardNum), (ushort)SF.frmControl.temp.dataAxis.axisConfigs[i].axis.AxisNum).ToString();
                }
            }
            SF.mainfrm.SaveAsJson(SF.ConfigPath, "DataStation", SF.frmCard.dataStation);
        }

        private void MovePoint_Click(object sender, EventArgs e)
        {
            if (dataGridView1.Rows[iSelectedRow].Cells[1].Value == null || dataGridView1.Rows[iSelectedRow].Cells[1].Value.ToString() == "")
                return;
            for (int i = 0; i < SF.frmControl.temp.dataAxis.axisConfigs.Count; i++)
            {
                if (SF.frmControl.temp.dataAxis.axisConfigs[i].axis != null)
                {
                    ushort cardNum = ushort.Parse(SF.frmControl.temp.dataAxis.axisConfigs[i].CardNum);
                    ushort axisNum = (ushort)SF.frmControl.temp.dataAxis.axisConfigs[i].axis.AxisNum;
                    SF.frmStation.SetStationParam(SF.frmControl.temp, axisNum);
                    SF.motion.Mov(cardNum, axisNum, double.Parse(dataGridView1.Rows[iSelectedRow].Cells[2 + i].Value.ToString()), 1, false);
                }
            }
        }

        private void ClearData_Click(object sender, EventArgs e)
        {
            DataGridViewRow rowToClear = dataGridView1.Rows[iSelectedRow];

            for (int i = 1; i < rowToClear.Cells.Count; i++)
            {
                rowToClear.Cells[i].Value = null;
            }

            SF.mainfrm.SaveAsJson(SF.ConfigPath, "DataStation", SF.frmCard.dataStation);
        }

        private void Copy_Click(object sender, EventArgs e)
        {
            if (dataGridView1.SelectedRows.Count > 0)
            {
                Copys();
            }
        }

        private void Paste_Click(object sender, EventArgs e)
        {
            Pastes();
        }

        private void dataGridView1_CellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            iSelectedRow = e.RowIndex;
        }
        //记录要复制行的index
        public List<int> selectedRowIndexes4Copy = new List<int>();
        List<DataPos> ListDataPos4Copy = new List<DataPos>();
        public void Copys()
        {
            selectedRowIndexes4Copy.Clear();
            ListDataPos4Copy.Clear();
            foreach (DataGridViewRow selectedRow in dataGridView1.SelectedRows)
            {
                selectedRowIndexes4Copy.Add(selectedRow.Index);
            }
            selectedRowIndexes4Copy.Sort();
            for (int i = 0; i < selectedRowIndexes4Copy.Count; i++)
            {
                DataPos dataItem = (DataPos)(dataGridView1.Rows[selectedRowIndexes4Copy[i]].DataBoundItem as DataPos).Clone();
                dataItem.Name = dataItem.Name + "1";
                ListDataPos4Copy.Add(dataItem);
            }
        }
        public void Pastes()
        {
            bool isEmptyRow = false;
            List<DataPos> deepCopy;
            // 创建一个MemoryStream来保存序列化后的数据
            using (MemoryStream stream = new MemoryStream())
            {
                IFormatter formatter = new BinaryFormatter();
                formatter.Serialize(stream, ListDataPos4Copy);
                stream.Seek(0, SeekOrigin.Begin);
                deepCopy = (List<DataPos>)formatter.Deserialize(stream);
            }

            for (int i = 0; i < deepCopy.Count; i++)
            {
                if (SF.frmControl.temp.dicDataPos.ContainsKey(SF.frmControl.temp.ListDataPos[iSelectedRow].Name))
                {
                    SF.frmControl.temp.dicDataPos.Remove(SF.frmControl.temp.ListDataPos[iSelectedRow].Name);
                }
                deepCopy[i].Index = iSelectedRow + i;
                SF.frmControl.temp.dicDataPos[deepCopy[i].Name.ToString()] = deepCopy[i];
                SF.frmControl.temp.ListDataPos[iSelectedRow + i] = deepCopy[i];
            }

            SF.mainfrm.SaveAsJson(SF.ConfigPath, "DataStation", SF.frmCard.dataStation);
            if (!isEmptyRow)
            {
                int rowCountAfterPaste = iSelectedRow + deepCopy.Count;
                int rowCountBeforePaste = iSelectedRow;
                for (int i = rowCountBeforePaste; i < rowCountAfterPaste; i++)
                {
                    dataGridView1.Rows[i].DefaultCellStyle.BackColor = Color.Red;
                }
            }

        }
    }
}
