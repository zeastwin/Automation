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
using Automation.MotionControl;

namespace Automation
{
    public partial class FrmStation : Form
    {
        private const int StationControlPreferredWidth = 856;
        private const int PointTableMinimumWidth = 300;

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
        private bool contextMenuByMouse = false;
        private int contextMenuRowIndex = -1;
        public bool IsPointEditing => isPointEditing;
        //public int SelectCard = 0;
        public FrmStation()
        {
            InitializeComponent();
            ConfigureAppearance();

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
            dataGridView1.MouseDown += dataGridView1_MouseDown;
            contextMenuStrip1.Opening += contextMenuStrip1_Opening;

            FormClosing += FrmStation_FormClosing;
            VisibleChanged += FrmStation_VisibleChanged;
            ParentChanged += FrmStation_ParentChanged;
            Resize += FrmStation_Resize;

            SetPointEditMode(false);
            UpdateResponsiveLayout();
        }

        private void ConfigureAppearance()
        {
            Color borderColor = Color.FromArgb(222, 228, 234);
            BackColor = Color.White;
            panel1.BackColor = Color.White;
            panel2.BackColor = borderColor;
            panel2.Padding = new Padding(1, 0, 0, 0);
            panel3.BackColor = Color.White;
            panelPointTools.Height = 44;
            panelPointTools.BackColor = Color.FromArgb(238, 243, 248);
            panelPointTools.Paint += (sender, args) =>
            {
                using (Pen pen = new Pen(borderColor))
                {
                    args.Graphics.DrawLine(
                        pen,
                        0,
                        panelPointTools.ClientSize.Height - 1,
                        panelPointTools.ClientSize.Width,
                        panelPointTools.ClientSize.Height - 1);
                }
            };

            ConfigurePointButton(
                btnPointEdit,
                Color.FromArgb(48, 63, 78),
                Color.FromArgb(190, 199, 210),
                Color.FromArgb(237, 240, 244));
            btnPointEdit.BackColor = Color.White;
            ConfigurePointButton(
                btnPointSave,
                Color.White,
                Color.FromArgb(34, 111, 183),
                Color.FromArgb(43, 126, 201));
            btnPointSave.BackColor = Color.FromArgb(34, 111, 183);
            ConfigurePointButton(
                btnPointCancel,
                Color.FromArgb(48, 63, 78),
                Color.FromArgb(190, 199, 210),
                Color.FromArgb(237, 240, 244));
            btnPointCancel.BackColor = Color.White;
            btnPointEdit.SetBounds(10, 8, 72, 28);
            btnPointSave.SetBounds(88, 8, 72, 28);
            btnPointCancel.SetBounds(166, 8, 72, 28);

            ConfigureGrid(dataGridView1, 34, 28);
            ConfigureGrid(dataGridView2, 32, 28);
        }

        private static void ConfigurePointButton(
            System.Windows.Forms.Button button,
            Color foreColor,
            Color borderColor,
            Color hoverColor)
        {
            button.BackColor = Color.White;
            button.ForeColor = foreColor;
            button.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = borderColor;
            button.FlatAppearance.MouseOverBackColor = hoverColor;
            button.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(hoverColor, 0.04F);
            button.UseVisualStyleBackColor = false;
        }

        private static void ConfigureGrid(DataGridView grid, int headerHeight, int rowHeight)
        {
            grid.EnableHeadersVisualStyles = false;
            grid.BorderStyle = BorderStyle.FixedSingle;
            grid.BackgroundColor = Color.White;
            grid.GridColor = Color.FromArgb(222, 228, 234);
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            grid.ColumnHeadersHeight = headerHeight;
            grid.RowTemplate.Height = rowHeight;
            grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(238, 243, 248),
                ForeColor = Color.FromArgb(48, 63, 78),
                Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold),
                Alignment = DataGridViewContentAlignment.MiddleCenter,
                SelectionBackColor = Color.FromArgb(238, 243, 248),
                SelectionForeColor = Color.FromArgb(48, 63, 78)
            };
            grid.DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.White,
                ForeColor = Color.FromArgb(48, 63, 78),
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular),
                Alignment = DataGridViewContentAlignment.MiddleCenter,
                SelectionBackColor = Color.FromArgb(217, 234, 250),
                SelectionForeColor = Color.FromArgb(27, 43, 59)
            };
            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 252);
        }

        private void FrmStation_Resize(object sender, EventArgs e)
        {
            UpdateResponsiveLayout();
        }

        private void UpdateResponsiveLayout()
        {
            int pointTableWidth = Math.Max(PointTableMinimumWidth,
                ClientSize.Width - StationControlPreferredWidth);
            panel2.Width = Math.Min(pointTableWidth, ClientSize.Width);
        }

        private void FrmStation_Load(object sender, EventArgs e)
        {
            RefleshFrmStation();

        }
        public void RefleshDgvState()
        {
            dataGridView2.Rows.Clear();
            axisRowMap = Array.Empty<int>();

            int stationIndex = SF.frmControl.CurrentStationIndex;
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
            }
        }
        public System.Drawing.Image validImage = UiStatusImages.CreateValidImage();
        public System.Drawing.Image invalidImage = UiStatusImages.CreateInvalidImage();
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
                if (SF.motion == null || !SF.motion.IsCardInitialized)
                {
                    return;
                }

                int stationIndex = SF.frmControl.CurrentStationIndex;
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
                    int rowIndex = axisRowMap.Length > i ? axisRowMap[i] : -1;
                    if (rowIndex < 0 || rowIndex >= dataGridView2.Rows.Count)
                    {
                        continue;
                    }
                    DataGridViewRow row = dataGridView2.Rows[rowIndex];
                    row.Cells[0].Value = $"({axisConfig.axis.AxisName})";
                    if (SF.DR?.Context?.AxisStatuses == null
                        || !SF.DR.Context.AxisStatuses.TryGet((ushort)selectCard, (ushort)selectAxis,
                            out AxisStatusSnapshot snapshot)
                        || !snapshot.IsIoFresh(AxisStatusCache.UiIoMaxAgeMilliseconds))
                    {
                        for (int cellIndex = 1; cellIndex <= 9; cellIndex++)
                        {
                            row.Cells[cellIndex].Value = null;
                        }
                        continue;
                    }
                    row.Cells[1].Value = snapshot.IsSignalOn(1) ? validImage : invalidImage;
                    row.Cells[2].Value = snapshot.IsSignalOn(2) ? validImage : invalidImage;
                    row.Cells[3].Value = snapshot.IsSignalOn(3) ? validImage : invalidImage;
                    row.Cells[4].Value = snapshot.IsSignalOn(4) ? validImage : invalidImage;
                    row.Cells[5].Value = snapshot.IsSignalOn(5) ? validImage : invalidImage;
                    row.Cells[6].Value = snapshot.IsSignalOn(7) ? validImage : invalidImage;
                    row.Cells[7].Value = snapshot.IsSignalOn(8) ? validImage : invalidImage;
                    row.Cells[8].Value = snapshot.IsSignalOn(9) ? validImage : invalidImage;
                    row.Cells[9].Value = snapshot.IsSignalOn(10) ? validImage : invalidImage;
                }

                if (SF.frmControl.temp?.dataAxis?.axisConfigs != null)
                {
                    int displayCount = Math.Min(SF.frmControl.temp.dataAxis.axisConfigs.Count,
                        Math.Min(SF.frmControl.PosTextBox.Count,
                            Math.Min(SF.frmControl.pictureBoxes.Count, SF.frmControl.VelLabel.Count)));
                    for (int i = 0; i < displayCount; i++)
                    {
                        AxisConfig axisConfig = SF.frmControl.temp.dataAxis.axisConfigs[i];
                        if (axisConfig == null || axisConfig.AxisName == "-1" || axisConfig.axis == null)
                        {
                            SF.frmControl.PosTextBox[i].Text = "--";
                            SF.frmControl.pictureBoxes[i].Image = null;
                            SF.frmControl.VelLabel[i].Text = "--";
                            continue;
                        }
                        if (!ushort.TryParse(axisConfig.CardNum, out ushort cardNum))
                        {
                            continue;
                        }
                        if (axisConfig.axis.AxisNum < 0 || axisConfig.axis.AxisNum > ushort.MaxValue)
                        {
                            SF.frmControl.PosTextBox[i].Text = "--";
                            SF.frmControl.pictureBoxes[i].Image = null;
                            SF.frmControl.VelLabel[i].Text = "--";
                            continue;
                        }
                        ushort axisNum = (ushort)axisConfig.axis.AxisNum;
                        if (SF.DR?.Context?.AxisStatuses != null
                            && SF.DR.Context.AxisStatuses.TryGet(cardNum, axisNum, out AxisStatusSnapshot snapshot)
                            && snapshot.IsDetailFresh(AxisStatusCache.UiDetailMaxAgeMilliseconds))
                        {
                            SF.frmControl.PosTextBox[i].Text = snapshot.Position.ToString();
                            SF.frmControl.pictureBoxes[i].Image = snapshot.ServoOn ? validImage : invalidImage;
                            SF.frmControl.VelLabel[i].Text = snapshot.Speed.ToString();
                        }
                        else
                        {
                            SF.frmControl.PosTextBox[i].Text = "--";
                            SF.frmControl.pictureBoxes[i].Image = null;
                            SF.frmControl.VelLabel[i].Text = "--";
                        }
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
                SF.motion.StageManualMotionParameters((ushort)cardNum, (ushort)axisNum, 0,
                    axis.SpeedMax * (dataStation.ManualSpeedPercent / 100), axis.AccMax, axis.DecMax,
                    0, 0, axis.PulseToMM);
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
            if (!AtomicJsonFileStore.Save(SF.ConfigPath, "DataStation", SF.frmCard.dataStation))
            {
                RestorePointSnapshot();
                MessageBox.Show("点位配置保存失败，已恢复到编辑前状态。", "保存失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetPointEditMode(false);
                ClearPointSnapshot();
                return;
            }
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

            if (SF.motion == null || !SF.motion.IsCardInitialized)
            {
                MessageBox.Show("运动控制卡未初始化，无法取点。", "工站取点",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var positions = new List<(int columnIndex, double value)>();
            try
            {
                for (int i = 0; i < SF.frmControl.temp.dataAxis.axisConfigs.Count; i++)
                {
                    AxisConfig axisConfig = SF.frmControl.temp.dataAxis.axisConfigs[i];
                    if (axisConfig == null || axisConfig.AxisName == "-1")
                    {
                        continue;
                    }
                    if (axisConfig.axis == null
                        || !ushort.TryParse(axisConfig.CardNum, out ushort cardNum)
                        || axisConfig.axis.AxisNum < 0 || axisConfig.axis.AxisNum > ushort.MaxValue
                        || 2 + i >= dataGridView1.Columns.Count)
                    {
                        MessageBox.Show($"第{i + 1}个轴配置无效，本次未写入任何点位。", "工站取点",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    positions.Add((2 + i, SF.motion.GetAxisPos(cardNum, (ushort)axisConfig.axis.AxisNum)));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"读取轴位置失败，本次未写入任何点位：{ex.Message}", "工站取点",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            foreach (var position in positions)
            {
                dataGridView1.Rows[iSelectedRow].Cells[position.columnIndex].Value =
                    position.value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        private void MovePoint_Click(object sender, EventArgs e)
        {
            DataStation station = SF.frmControl.temp;
            if (station?.dataAxis?.axisConfigs == null)
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
            var commands = new List<(ushort card, ushort axis, double target, FrmCard.Axis config)>();
            for (int i = 0; i < station.dataAxis.axisConfigs.Count; i++)
            {
                AxisConfig axisConfig = station.dataAxis.axisConfigs[i];
                if (axisConfig == null || axisConfig.AxisName == "-1")
                {
                    continue;
                }
                if (axisConfig.axis == null)
                {
                    MessageBox.Show($"第{i + 1}个轴配置无效，未执行任何轴移动。");
                    return;
                }
                if (!ushort.TryParse(axisConfig.CardNum, out ushort cardNum))
                {
                    MessageBox.Show($"第{i + 1}个轴卡号无效，未执行任何轴移动。");
                    return;
                }
                int axisNumValue = axisConfig.axis.AxisNum;
                if (axisNumValue < 0 || axisNumValue > ushort.MaxValue)
                {
                    MessageBox.Show($"第{i + 1}个轴编号无效，未执行任何轴移动。");
                    return;
                }
                if (2 + i >= dataGridView1.Columns.Count)
                {
                    MessageBox.Show("点位列与工站轴配置不一致，未执行任何轴移动。");
                    return;
                }
                object cellValue = dataGridView1.Rows[iSelectedRow].Cells[2 + i].Value;
                if (cellValue == null || !double.TryParse(cellValue.ToString(),
                        System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture,
                        out double targetPos)
                    || double.IsNaN(targetPos) || double.IsInfinity(targetPos))
                {
                    MessageBox.Show($"第{i + 1}个轴点位无效，未执行任何轴移动。");
                    return;
                }
                ushort axisNum = (ushort)axisNumValue;
                if (!SF.cardStore.TryGetAxis(cardNum, axisNum, out FrmCard.Axis runtimeAxis) || runtimeAxis == null)
                {
                    MessageBox.Show($"未找到第{i + 1}个轴的运行配置，未执行任何轴移动。");
                    return;
                }
                commands.Add((cardNum, axisNum, targetPos, runtimeAxis));
            }

            if (commands.Count == 0)
            {
                MessageBox.Show("工站没有可执行的轴，未执行移动。");
                return;
            }

            var startedAxes = new List<(ushort card, ushort axis)>();
            try
            {
                List<AxisCommandRequest> requests = commands
                    .Select(command => new AxisCommandRequest(command.card, command.axis, AxisCommandKind.Motion))
                    .ToList();
                if (!SF.DR.TryReserveManualMotionResources(requests, out IDisposable resourceLease, out string resourceError))
                {
                    throw new InvalidOperationException(resourceError);
                }
                using (resourceLease)
                using (SF.motion.ValidateAxesForCommand(requests))
                {
                    foreach (var command in commands)
                    {
                        SF.motion.SetMovParam(command.card, command.axis, 0,
                            command.config.SpeedMax * (station.ManualSpeedPercent / 100),
                            command.config.AccMax, command.config.DecMax, 0, 0, command.config.PulseToMM);
                    }
                    foreach (var command in commands)
                    {
                        SF.motion.Mov(command.card, command.axis, command.target, 1, false);
                        startedAxes.Add((command.card, command.axis));
                    }
                }
            }
            catch (Exception ex)
            {
                var stopErrors = new List<string>();
                foreach (var startedAxis in startedAxes)
                {
                    try
                    {
                        SF.motion.StopOneAxis(startedAxis.card, startedAxis.axis, 0);
                    }
                    catch (Exception stopException)
                    {
                        stopErrors.Add($"{startedAxis.card}-{startedAxis.axis}:{stopException.Message}");
                    }
                }
                if (stopErrors.Count > 0)
                {
                    SF.SetSecurityLock("工站移动异常后停止轴失败：" + string.Join(";", stopErrors));
                }
                MessageBox.Show($"工站移动失败，已停止本次已启动轴：{ex.Message}", "工站移动",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
            if (e.RowIndex < 0 || e.RowIndex >= dataGridView1.Rows.Count)
            {
                if (e.Button == MouseButtons.Right)
                {
                    iSelectedRow = -1;
                    dataGridView1.ClearSelection();
                }
                return;
            }
            if (e.Button == MouseButtons.Right)
            {
                iSelectedRow = e.RowIndex;
                dataGridView1.ClearSelection();
                dataGridView1.Rows[e.RowIndex].Selected = true;
                dataGridView1.CurrentCell = dataGridView1.Rows[e.RowIndex].Cells[0];
                return;
            }
            iSelectedRow = e.RowIndex;
        }

        private void dataGridView1_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                contextMenuByMouse = true;
                contextMenuRowIndex = dataGridView1.HitTest(e.X, e.Y).RowIndex;
            }
        }

        private void contextMenuStrip1_Opening(object sender, CancelEventArgs e)
        {
            int rowIndex;
            if (contextMenuByMouse)
            {
                rowIndex = contextMenuRowIndex;
            }
            else
            {
                Point clientPoint = dataGridView1.PointToClient(Cursor.Position);
                rowIndex = dataGridView1.HitTest(clientPoint.X, clientPoint.Y).RowIndex;
            }
            contextMenuByMouse = false;
            contextMenuRowIndex = -1;

            if (rowIndex < 0 || rowIndex >= dataGridView1.Rows.Count)
            {
                iSelectedRow = -1;
                dataGridView1.ClearSelection();
                e.Cancel = true;
                return;
            }

            iSelectedRow = rowIndex;
            dataGridView1.ClearSelection();
            dataGridView1.Rows[rowIndex].Selected = true;
            dataGridView1.CurrentCell = dataGridView1.Rows[rowIndex].Cells[0];
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
