using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Reflection;
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
        private readonly Timer stateTimer = new Timer();
        private bool stateTimerInitialized = false;
        private bool stateTimerErrorReported = false;
        private string axisConfigSignature = string.Empty;
        private int[] axisRowMap = Array.Empty<int>();
        private bool isPointEditing = false;
        private List<DataPos> pointSnapshot = new List<DataPos>();
        private DataStation pointEditStation;
        private int pointEditStationIndex = -1;
        public bool IsPointEditing => isPointEditing;
        //public int SelectCard = 0;
        public FrmStation()
        {
            InitializeComponent();

            Type dgvType = this.dataGridView1.GetType();
            PropertyInfo pi = dgvType.GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
            pi.SetValue(this.dataGridView1, true, null);

            dataGridView1.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;

            dataGridView1.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dataGridView1.ReadOnly = true;

            Type dgvType2 = this.dataGridView2.GetType();
            PropertyInfo pi2 = dgvType2.GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
            pi2.SetValue(this.dataGridView2, true, null);

            dataGridView2.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            dataGridView2.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dataGridView2.ReadOnly = true;

            FormClosing += FrmStation_FormClosing;
            VisibleChanged += FrmStation_VisibleChanged;
            ParentChanged += FrmStation_ParentChanged;

            SetPointEditMode(false);
        }

        private void FrmStation_Load(object sender, EventArgs e)
        {
            RefleshFrmStation();

        }
        public Dictionary<int, char[]> StateDicTemp = new Dictionary<int, char[]>();
        public void RefleshDgvState()
        {
            dataGridView2.Rows.Clear();
            StateDicTemp.Clear();
            axisRowMap = Array.Empty<int>();

            int stationIndex = SF.frmControl.comboBox1.SelectedIndex;
            if (stationIndex == -1)
            {
                return;
            }
            if (SF.frmCard.dataStation == null || stationIndex >= SF.frmCard.dataStation.Count)
            {
                return;
            }

            List<AxisConfig> axisConfigs = SF.frmCard.dataStation[stationIndex].dataAxis.axisConfigs;
            if (axisConfigs == null)
            {
                return;
            }
            axisConfigSignature = BuildAxisConfigSignature(axisConfigs);
            axisRowMap = new int[axisConfigs.Count];
            for (int i = 0; i < axisRowMap.Length; i++)
            {
                axisRowMap[i] = -1;
            }

            for (int i = 0; i < axisConfigs.Count; i++)
            {
                AxisConfig axisConfig = axisConfigs[i];
                if (axisConfig == null || axisConfig.axis == null)
                {
                    continue;
                }
                if (!int.TryParse(axisConfig.CardNum, out int selectCard) || selectCard < 0)
                {
                    continue;
                }
                int selectAxis = axisConfig.axis.AxisNum;
                if (selectAxis < 0)
                {
                    continue;
                }

                axisRowMap[i] = dataGridView2.Rows.Add();
                StateDicTemp[i] = Array.Empty<char>();
            }
        }
        public System.Drawing.Image validImage = Properties.Resources.vaild;
        public System.Drawing.Image invalidImage = Properties.Resources.invalid;
        public void RefleshFrmStation()
        {
            RefleshDgvState();
            stateTimerErrorReported = false;
            if (!stateTimerInitialized)
            {
                stateTimer.Interval = 100;
                stateTimer.Tick += StateTimer_Tick;
                stateTimerInitialized = true;
            }
            if (!stateTimer.Enabled)
            {
                stateTimer.Start();
            }
        }

        private string BuildAxisConfigSignature(List<AxisConfig> axisConfigs)
        {
            if (axisConfigs == null)
            {
                return string.Empty;
            }
            StringBuilder signature = new StringBuilder(axisConfigs.Count * 16);
            for (int i = 0; i < axisConfigs.Count; i++)
            {
                AxisConfig axisConfig = axisConfigs[i];
                if (axisConfig == null)
                {
                    signature.Append("|null;");
                    continue;
                }
                signature.Append(axisConfig.CardNum);
                signature.Append('|');
                signature.Append(axisConfig.AxisName);
                signature.Append('|');
                signature.Append(axisConfig.axis == null ? -1 : axisConfig.axis.AxisNum);
                signature.Append(';');
            }
            return signature.ToString();
        }

        private void StateTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                if (SF.curPage != 1)
                {
                    return;
                }

                int stationIndex = SF.frmControl.comboBox1.SelectedIndex;
                if (stationIndex == -1)
                {
                    return;
                }
                if (SF.frmCard.dataStation == null || stationIndex >= SF.frmCard.dataStation.Count)
                {
                    return;
                }

                List<AxisConfig> axisConfigs = SF.frmCard.dataStation[stationIndex].dataAxis.axisConfigs;
                if (axisConfigs == null)
                {
                    return;
                }
                string currentSignature = BuildAxisConfigSignature(axisConfigs);
                if (currentSignature != axisConfigSignature)
                {
                    RefleshDgvState();
                    return;
                }
                if (axisRowMap.Length != axisConfigs.Count)
                {
                    RefleshDgvState();
                    return;
                }

                for (int i = 0; i < axisConfigs.Count; i++)
                {
                    AxisConfig axisConfig = axisConfigs[i];
                    if (axisConfig == null || axisConfig.axis == null)
                    {
                        continue;
                    }
                    if (!int.TryParse(axisConfig.CardNum, out int selectCard) || selectCard < 0)
                    {
                        continue;
                    }
                    int selectAxis = axisConfig.axis.AxisNum;
                    if (selectAxis < 0)
                    {
                        continue;
                    }
                    char[] state = null;
                    lock (SF.mainfrm.StateDicLock)
                    {
                        if (selectCard < SF.mainfrm.StateDic.Count)
                        {
                            Dictionary<int, char[]> axisStates = SF.mainfrm.StateDic[selectCard];
                            if (axisStates != null)
                            {
                                axisStates.TryGetValue(selectAxis, out state);
                            }
                        }
                    }
                    if (state == null)
                    {
                        continue;
                    }

                    int rowIndex = axisRowMap.Length > i ? axisRowMap[i] : -1;
                    if (rowIndex < 0 || rowIndex >= dataGridView2.Rows.Count)
                    {
                        continue;
                    }
                    if (StateDicTemp.TryGetValue(i, out char[] cached) && cached.SequenceEqual(state))
                    {
                        continue;
                    }
                    if (state.Length < 10)
                    {
                        StateDicTemp[i] = (char[])state.Clone();
                        continue;
                    }

                    DataGridViewRow row = dataGridView2.Rows[rowIndex];
                    row.Cells[0].Value = $"({axisConfig.axis.AxisName})";
                    row.Cells[1].Value = state[state.Length - 1] == '1' ? validImage : invalidImage;
                    row.Cells[2].Value = state[state.Length - 2] == '1' ? validImage : invalidImage;
                    row.Cells[3].Value = state[state.Length - 3] == '1' ? validImage : invalidImage;
                    row.Cells[4].Value = state[state.Length - 4] == '1' ? validImage : invalidImage;
                    row.Cells[5].Value = state[state.Length - 5] == '1' ? validImage : invalidImage;
                    row.Cells[6].Value = state[state.Length - 7] == '1' ? validImage : invalidImage;
                    row.Cells[7].Value = state[state.Length - 8] == '1' ? validImage : invalidImage;
                    row.Cells[8].Value = state[state.Length - 9] == '1' ? validImage : invalidImage;
                    row.Cells[9].Value = state[state.Length - 10] == '1' ? validImage : invalidImage;
                    StateDicTemp[i] = (char[])state.Clone();
                }

                if (SF.frmControl.temp != null)
                {
                    for (int i = 0; i < 6; i++)
                    {
                        AxisConfig axisConfig = SF.frmControl.temp.dataAxis.axisConfigs[i];
                        if (axisConfig.AxisName == "-1" || axisConfig.axis == null)
                        {
                            continue;
                        }
                        if (!ushort.TryParse(axisConfig.CardNum, out ushort cardNum))
                        {
                            continue;
                        }
                        ushort axisNum = (ushort)axisConfig.axis.AxisNum;
                        SF.frmControl.PosTextBox[i].Text = SF.motion.GetAxisPos(cardNum, axisNum).ToString();
                        SF.frmControl.pictureBoxes[i].Image = SF.motion.GetAxisSevon(cardNum, axisNum) ? validImage : invalidImage;
                        SF.frmControl.VelLabel[i].Text = SF.motion.GetAxisCurSpeed(cardNum, axisNum).ToString();
                        SF.frmControl.StateLabel[i].Text = axisConfig.axis.State.ToString();
                    }
                }
            }
            catch (Exception ex)
            {
                if (!stateTimerErrorReported)
                {
                    stateTimerErrorReported = true;
                    if (SF.frmInfo != null)
                    {
                        SF.frmInfo.PrintInfo($"工站状态刷新异常：{ex.Message}", FrmInfo.Level.Error);
                    }
                }
            }
        }

        private void FrmStation_VisibleChanged(object sender, EventArgs e)
        {
            if (!stateTimerInitialized)
            {
                return;
            }
            if (Visible)
            {
                if (!stateTimer.Enabled)
                {
                    stateTimer.Start();
                }
            }
            else
            {
                if (stateTimer.Enabled)
                {
                    stateTimer.Stop();
                }
            }
        }

        private void FrmStation_ParentChanged(object sender, EventArgs e)
        {
            if (!stateTimerInitialized)
            {
                return;
            }
            if (Parent == null)
            {
                if (stateTimer.Enabled)
                {
                    stateTimer.Stop();
                }
                return;
            }
            if (Visible && !stateTimer.Enabled)
            {
                stateTimer.Start();
            }
        }

        private void FrmStation_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (stateTimer.Enabled)
            {
                stateTimer.Stop();
            }
            if (stateTimerInitialized)
            {
                stateTimer.Tick -= StateTimer_Tick;
                stateTimerInitialized = false;
            }
            stateTimer.Dispose();
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

            if (dataStation == null || dataStation.dataAxis == null || dataStation.dataAxis.axisConfigs == null || AxisIndex < 0 || AxisIndex >= dataStation.dataAxis.axisConfigs.Count)
            {
                return;
            }

            AxisConfig axisConfig = dataStation.dataAxis.axisConfigs[AxisIndex];
            if (axisConfig.AxisName == "-1" || axisConfig.axis == null)
            {
                return;
            }
            if (!int.TryParse(axisConfig.CardNum, out int cardNum) || cardNum < 0)
            {
                return;
            }
            int axisNum = axisConfig.axis.AxisNum;
            if (axisNum < 0)
            {
                return;
            }

            if (SF.cardStore.TryGetAxis(cardNum, axisNum, out FrmCard.Axis axis))
            {
                SF.motion.SetMovParam((ushort)cardNum, (ushort)axisNum, 0, axis.SpeedMax * dataStation.Vel, axis.AccMax, axis.DecMax, 0, 0, axis.PulseToMM);
            }

        }

        private void SetPointEditMode(bool enable)
        {
            isPointEditing = enable;
            if (dataGridView1 != null)
            {
                dataGridView1.ReadOnly = !enable;
            }
            if (index != null)
            {
                index.ReadOnly = true;
            }
            if (btnPointEdit != null)
            {
                btnPointEdit.Enabled = !enable;
            }
            if (btnPointSave != null)
            {
                btnPointSave.Enabled = enable;
            }
            if (btnPointCancel != null)
            {
                btnPointCancel.Enabled = enable;
            }
            if (Touch != null)
            {
                Touch.Enabled = enable;
            }
            if (ClearData != null)
            {
                ClearData.Enabled = enable;
            }
            if (Paste != null)
            {
                Paste.Enabled = enable;
            }
        }

        private List<DataPos> CloneDataPosList(List<DataPos> source)
        {
            if (source == null)
            {
                return new List<DataPos>();
            }
            List<DataPos> clone = new List<DataPos>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                DataPos item = source[i];
                clone.Add(item == null ? null : (DataPos)item.Clone());
            }
            return clone;
        }

        private Dictionary<string, DataPos> BuildDataPosDictionary(List<DataPos> source)
        {
            Dictionary<string, DataPos> dict = new Dictionary<string, DataPos>();
            if (source == null)
            {
                return dict;
            }
            foreach (DataPos pos in source)
            {
                if (pos == null || string.IsNullOrWhiteSpace(pos.Name))
                {
                    continue;
                }
                dict[pos.Name] = pos;
            }
            return dict;
        }

        private void ResetPointBinding(List<DataPos> list)
        {
            if (SF.frmControl?.bindingSource != null)
            {
                SF.frmControl.bindingSource.DataSource = list;
                SF.frmControl.bindingSource.ResetBindings(false);
                dataGridView1.DataSource = SF.frmControl.bindingSource;
                return;
            }
            dataGridView1.DataSource = null;
            dataGridView1.DataSource = list;
        }

        private void CapturePointSnapshot()
        {
            if (SF.frmControl?.temp == null)
            {
                pointSnapshot.Clear();
                pointEditStation = null;
                pointEditStationIndex = -1;
                return;
            }
            pointSnapshot = CloneDataPosList(SF.frmControl.temp.ListDataPos);
            pointEditStation = SF.frmControl.temp;
            pointEditStationIndex = SF.frmControl.comboBox1.SelectedIndex;
        }

        private void RestorePointSnapshot()
        {
            if (pointEditStation == null)
            {
                return;
            }
            pointEditStation.ListDataPos = CloneDataPosList(pointSnapshot);
            pointEditStation.dicDataPos = BuildDataPosDictionary(pointEditStation.ListDataPos);
            if (SF.frmControl?.temp == pointEditStation || SF.frmControl?.comboBox1?.SelectedIndex == pointEditStationIndex)
            {
                ResetPointBinding(pointEditStation.ListDataPos);
            }
        }

        private void RebuildPointDictionary(DataStation station)
        {
            if (station == null)
            {
                return;
            }
            station.dicDataPos = BuildDataPosDictionary(station.ListDataPos);
        }

        private void ClearPointSnapshot()
        {
            pointSnapshot.Clear();
            pointEditStation = null;
            pointEditStationIndex = -1;
        }

        private void btnPointEdit_Click(object sender, EventArgs e)
        {
            if (isPointEditing)
            {
                return;
            }
            if (SF.frmControl?.temp == null)
            {
                MessageBox.Show("未选择工站，无法编辑。");
                return;
            }
            CapturePointSnapshot();
            SetPointEditMode(true);
        }

        private void btnPointSave_Click(object sender, EventArgs e)
        {
            if (!isPointEditing)
            {
                return;
            }
            dataGridView1.EndEdit();
            SF.frmControl?.bindingSource?.EndEdit();
            DataStation station = pointEditStation ?? SF.frmControl?.temp;
            if (station == null)
            {
                MessageBox.Show("未选择工站，无法保存。");
                SetPointEditMode(false);
                ClearPointSnapshot();
                return;
            }
            RebuildPointDictionary(station);
            SF.mainfrm.SaveAsJson(SF.ConfigPath, "DataStation", SF.frmCard.dataStation);
            SetPointEditMode(false);
            ClearPointSnapshot();
        }

        private void btnPointCancel_Click(object sender, EventArgs e)
        {
            if (!isPointEditing)
            {
                return;
            }
            dataGridView1.CancelEdit();
            SF.frmControl?.bindingSource?.CancelEdit();
            RestorePointSnapshot();
            SetPointEditMode(false);
            ClearPointSnapshot();
        }


        private void dataGridView1_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0)
            {
                if (!isPointEditing)
                {
                    return;
                }
                DataGridView dataGridView = (DataGridView)sender;
                if (SF.frmControl.temp == null)
                {
                    return;
                }
                DataGridViewRow editedRow = dataGridView.Rows[e.RowIndex];
                DataPos dataPos = editedRow.DataBoundItem as DataPos;
                if (dataPos == null)
                {
                    return;
                }

                if (e.ColumnIndex == 1)
                {
                    object cellValue = editedRow.Cells[1].Value;
                    string newName = cellValue == null ? string.Empty : cellValue.ToString();
                    KeyValuePair<string, DataPos> oldEntry = SF.frmControl.temp.dicDataPos.FirstOrDefault(item => item.Value != null && item.Value.Index == dataPos.Index);
                    string oldName = oldEntry.Key ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(newName))
                    {
                        MessageBox.Show("点位名称不能为空。");
                        editedRow.Cells[1].Value = oldName;
                        dataPos.Name = oldName;
                        return;
                    }
                    if (oldName != newName && SF.frmControl.temp.dicDataPos.TryGetValue(newName, out DataPos existed) && existed != null && existed.Index != dataPos.Index)
                    {
                        MessageBox.Show($"点位名称已存在：{newName}");
                        editedRow.Cells[1].Value = oldName;
                        dataPos.Name = oldName;
                        return;
                    }
                    if (!string.IsNullOrEmpty(oldName) && oldName != newName)
                    {
                        SF.frmControl.temp.dicDataPos.Remove(oldName);
                    }
                    dataPos.Name = newName;
                    SF.frmControl.temp.dicDataPos[newName] = dataPos;
                }

                if (dataPos.Index >= 0 && dataPos.Index < SF.frmControl.temp.ListDataPos.Count)
                {
                    SF.frmControl.temp.ListDataPos[dataPos.Index] = dataPos;
                }
            }
        }

        private void Touch_Click(object sender, EventArgs e)
        {
            if (!isPointEditing)
            {
                MessageBox.Show("请先点击编辑。");
                return;
            }
            if (SF.frmControl.temp == null)
            {
                return;
            }
            if (iSelectedRow < 0 || iSelectedRow >= dataGridView1.Rows.Count)
            {
                return;
            }

            bool hasInvalid = false;
            for (int i = 0; i < SF.frmControl.temp.dataAxis.axisConfigs.Count; i++)
            {
                AxisConfig axisConfig = SF.frmControl.temp.dataAxis.axisConfigs[i];
                if (axisConfig.AxisName == "-1" || axisConfig.axis == null)
                {
                    hasInvalid = true;
                    continue;
                }
                if (!ushort.TryParse(axisConfig.CardNum, out ushort cardNum))
                {
                    hasInvalid = true;
                    continue;
                }
                int axisNum = axisConfig.axis.AxisNum;
                if (axisNum < 0)
                {
                    hasInvalid = true;
                    continue;
                }
                dataGridView1.Rows[iSelectedRow].Cells[2 + i].Value = SF.motion.GetAxisPos(cardNum, (ushort)axisNum).ToString();
            }
            if (hasInvalid)
            {
                MessageBox.Show("存在无效轴配置，已跳过部分轴取点。");
            }
        }

        private void MovePoint_Click(object sender, EventArgs e)
        {
            if (SF.frmControl.temp == null)
            {
                return;
            }
            if (iSelectedRow < 0 || iSelectedRow >= dataGridView1.Rows.Count)
            {
                return;
            }
            if (dataGridView1.Rows[iSelectedRow].Cells[1].Value == null || string.IsNullOrWhiteSpace(dataGridView1.Rows[iSelectedRow].Cells[1].Value.ToString()))
            {
                MessageBox.Show("点位名称为空，无法移动。");
                return;
            }
            bool hasInvalid = false;
            for (int i = 0; i < SF.frmControl.temp.dataAxis.axisConfigs.Count; i++)
            {
                AxisConfig axisConfig = SF.frmControl.temp.dataAxis.axisConfigs[i];
                if (axisConfig.AxisName == "-1" || axisConfig.axis == null)
                {
                    hasInvalid = true;
                    continue;
                }
                if (!ushort.TryParse(axisConfig.CardNum, out ushort cardNum))
                {
                    hasInvalid = true;
                    continue;
                }
                int axisNumValue = axisConfig.axis.AxisNum;
                if (axisNumValue < 0)
                {
                    hasInvalid = true;
                    continue;
                }
                object cellValue = dataGridView1.Rows[iSelectedRow].Cells[2 + i].Value;
                if (cellValue == null || !double.TryParse(cellValue.ToString(), out double targetPos))
                {
                    hasInvalid = true;
                    continue;
                }
                ushort axisNum = (ushort)axisNumValue;
                SF.frmStation.SetStationParam(SF.frmControl.temp, i);
                SF.motion.Mov(cardNum, axisNum, targetPos, 1, false);
            }
            if (hasInvalid)
            {
                MessageBox.Show("存在无效点位或轴配置，部分轴未执行移动。");
            }
        }

        private void ClearData_Click(object sender, EventArgs e)
        {
            if (!isPointEditing)
            {
                MessageBox.Show("请先点击编辑。");
                return;
            }
            if (iSelectedRow < 0 || iSelectedRow >= dataGridView1.Rows.Count)
            {
                return;
            }
            if (MessageBox.Show("确认清除选中的点位数据？", "清除确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }
            DataGridViewRow rowToClear = dataGridView1.Rows[iSelectedRow];
            DataPos dataPos = rowToClear.DataBoundItem as DataPos;
            if (dataPos != null && SF.frmControl.temp != null)
            {
                string oldName = dataPos.Name;
                if (!string.IsNullOrWhiteSpace(oldName) && SF.frmControl.temp.dicDataPos.ContainsKey(oldName))
                {
                    SF.frmControl.temp.dicDataPos.Remove(oldName);
                }
                dataPos.Name = string.Empty;
                dataPos.X = -1;
                dataPos.Y = -1;
                dataPos.Z = -1;
                dataPos.U = -1;
                dataPos.V = -1;
                dataPos.W = -1;
                if (dataPos.Index >= 0 && dataPos.Index < SF.frmControl.temp.ListDataPos.Count)
                {
                    SF.frmControl.temp.ListDataPos[dataPos.Index] = dataPos;
                }
            }

            for (int i = 1; i < rowToClear.Cells.Count; i++)
            {
                rowToClear.Cells[i].Value = null;
            }
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
            iSelectedRow = e.RowIndex >= 0 ? e.RowIndex : -1;
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
                DataPos source = dataGridView1.Rows[selectedRowIndexes4Copy[i]].DataBoundItem as DataPos;
                if (source == null)
                {
                    continue;
                }
                DataPos dataItem = (DataPos)source.Clone();
                dataItem.Name = dataItem.Name + "1";
                ListDataPos4Copy.Add(dataItem);
            }
        }
        public void Pastes()
        {
            if (!isPointEditing)
            {
                MessageBox.Show("请先点击编辑。");
                return;
            }
            if (SF.frmControl.temp == null)
            {
                return;
            }
            if (iSelectedRow < 0 || iSelectedRow >= SF.frmControl.temp.ListDataPos.Count)
            {
                return;
            }
            if (ListDataPos4Copy.Count == 0)
            {
                return;
            }

            int maxPasteCount = Math.Min(ListDataPos4Copy.Count, SF.frmControl.temp.ListDataPos.Count - iSelectedRow);
            if (maxPasteCount <= 0)
            {
                return;
            }

            HashSet<string> replaceNames = new HashSet<string>();
            for (int i = 0; i < maxPasteCount; i++)
            {
                DataPos oldPos = SF.frmControl.temp.ListDataPos[iSelectedRow + i];
                if (oldPos != null && !string.IsNullOrWhiteSpace(oldPos.Name))
                {
                    replaceNames.Add(oldPos.Name);
                }
            }
            HashSet<string> newNames = new HashSet<string>();
            for (int i = 0; i < maxPasteCount; i++)
            {
                DataPos source = ListDataPos4Copy[i];
                if (source == null || string.IsNullOrWhiteSpace(source.Name))
                {
                    MessageBox.Show("粘贴失败：存在空名称点位，请先命名后再复制/粘贴。");
                    return;
                }
                if (!newNames.Add(source.Name))
                {
                    MessageBox.Show($"粘贴失败：名称重复（{source.Name}），请先修改名称。");
                    return;
                }
                if (SF.frmControl.temp.dicDataPos.ContainsKey(source.Name) && !replaceNames.Contains(source.Name))
                {
                    MessageBox.Show($"粘贴失败：名称重复（{source.Name}），请先修改名称。");
                    return;
                }
            }

            List<DataPos> deepCopy = new List<DataPos>(maxPasteCount);
            for (int i = 0; i < maxPasteCount; i++)
            {
                deepCopy.Add((DataPos)ListDataPos4Copy[i].Clone());
            }

            for (int i = 0; i < deepCopy.Count; i++)
            {
                int targetIndex = iSelectedRow + i;
                string oldName = SF.frmControl.temp.ListDataPos[targetIndex]?.Name;
                if (!string.IsNullOrEmpty(oldName) && SF.frmControl.temp.dicDataPos.ContainsKey(oldName))
                {
                    SF.frmControl.temp.dicDataPos.Remove(oldName);
                }
                deepCopy[i].Index = targetIndex;
                SF.frmControl.temp.dicDataPos[deepCopy[i].Name] = deepCopy[i];
                SF.frmControl.temp.ListDataPos[targetIndex] = deepCopy[i];
            }
            int rowCountAfterPaste = iSelectedRow + deepCopy.Count;
            for (int i = iSelectedRow; i < rowCountAfterPaste && i < dataGridView1.Rows.Count; i++)
            {
                dataGridView1.Rows[i].DefaultCellStyle.BackColor = Color.Red;
            }
        }
    }
}
