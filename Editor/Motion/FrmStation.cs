// 模块：编辑器 / 运动。
// 职责范围：控制卡、工站和手动运动的配置与交互。

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
        private MotionStationState? lastRobotState;
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
            Color borderColor = UiPalette.Stroke;
            BackColor = UiPalette.SurfaceStrong;
            panel1.BackColor = UiPalette.SurfaceStrong;
            panel2.BackColor = borderColor;
            panel2.Padding = new Padding(1, 0, 0, 0);
            panel3.BackColor = UiPalette.SurfaceStrong;
            panelPointTools.Height = 44;
            panelPointTools.BackColor = UiPalette.SurfaceSubtle;
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
                UiPalette.TextPrimary,
                UiPalette.StrokeStrong,
                UiPalette.DisabledSoft);
            btnPointEdit.BackColor = UiPalette.SurfaceStrong;
            ConfigurePointButton(
                btnPointSave,
                UiPalette.SurfaceStrong,
                UiPalette.Brand,
                UiPalette.Focus);
            btnPointSave.BackColor = UiPalette.Brand;
            ConfigurePointButton(
                btnPointCancel,
                UiPalette.TextPrimary,
                UiPalette.StrokeStrong,
                UiPalette.DisabledSoft);
            btnPointCancel.BackColor = UiPalette.SurfaceStrong;
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
            button.BackColor = UiPalette.SurfaceStrong;
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
            grid.BackgroundColor = UiPalette.SurfaceStrong;
            grid.GridColor = UiPalette.Stroke;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            grid.ColumnHeadersHeight = headerHeight;
            grid.RowTemplate.Height = rowHeight;
            grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = UiPalette.SurfaceSubtle,
                ForeColor = UiPalette.TextPrimary,
                Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold),
                Alignment = DataGridViewContentAlignment.MiddleCenter,
                SelectionBackColor = UiPalette.SurfaceSubtle,
                SelectionForeColor = UiPalette.TextPrimary
            };
            grid.DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = UiPalette.SurfaceStrong,
                ForeColor = UiPalette.TextPrimary,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular),
                Alignment = DataGridViewContentAlignment.MiddleCenter,
                SelectionBackColor = UiPalette.Selection,
                SelectionForeColor = UiPalette.Navigation
            };
            grid.AlternatingRowsDefaultCellStyle.BackColor = UiPalette.Input;
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

            int stationIndex = Workspace.Control.CurrentStationIndex;
            if (stationIndex == -1)
            {
                return;
            }
            if (Workspace.Card.dataStation == null || stationIndex >= Workspace.Card.dataStation.Count)
            {
                return;
            }

            DataStation station = Workspace.Card.dataStation[stationIndex];
            if (station.Type != StationType.Axis)
            {
                axisConfigSignature = $"robot:{station.Type}";
                lastRobotState = null;
                return;
            }

            List<AxisConfig> axisConfigs = station.dataAxis?.axisConfigs;
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
                if (Workspace.CurrentPage != 1)
                {
                    return;
                }
                if (Workspace.Runtime.Motion == null)
                {
                    return;
                }

                int stationIndex = Workspace.Control.CurrentStationIndex;
                if (stationIndex == -1)
                {
                    return;
                }
                if (Workspace.Card.dataStation == null || stationIndex >= Workspace.Card.dataStation.Count)
                {
                    return;
                }

                DataStation station = Workspace.Card.dataStation[stationIndex];
                if (station.Type != StationType.Axis)
                {
                    RefreshRobotStationState(stationIndex);
                    return;
                }
                if (!Workspace.Runtime.Motion.IsCardInitialized)
                {
                    return;
                }

                List<AxisConfig> axisConfigs = station.dataAxis?.axisConfigs;
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
                    if (Workspace.Runtime.ProcessEngine?.Context?.AxisStatuses == null
                        || !Workspace.Runtime.ProcessEngine.Context.AxisStatuses.TryGet((ushort)selectCard, (ushort)selectAxis,
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

                if (Workspace.Control.temp?.dataAxis?.axisConfigs != null)
                {
                    int displayCount = Math.Min(Workspace.Control.temp.dataAxis.axisConfigs.Count,
                        Math.Min(Workspace.Control.PosTextBox.Count,
                            Math.Min(Workspace.Control.pictureBoxes.Count, Workspace.Control.VelLabel.Count)));
                    for (int i = 0; i < displayCount; i++)
                    {
                        AxisConfig axisConfig = Workspace.Control.temp.dataAxis.axisConfigs[i];
                        if (axisConfig == null || axisConfig.AxisName == "-1" || axisConfig.axis == null)
                        {
                            Workspace.Control.PosTextBox[i].Text = "--";
                            Workspace.Control.pictureBoxes[i].Image = null;
                            Workspace.Control.VelLabel[i].Text = "--";
                            continue;
                        }
                        if (!ushort.TryParse(axisConfig.CardNum, out ushort cardNum))
                        {
                            continue;
                        }
                        if (axisConfig.axis.AxisNum < 0 || axisConfig.axis.AxisNum > ushort.MaxValue)
                        {
                            Workspace.Control.PosTextBox[i].Text = "--";
                            Workspace.Control.pictureBoxes[i].Image = null;
                            Workspace.Control.VelLabel[i].Text = "--";
                            continue;
                        }
                        ushort axisNum = (ushort)axisConfig.axis.AxisNum;
                        if (Workspace.Runtime.ProcessEngine?.Context?.AxisStatuses != null
                            && Workspace.Runtime.ProcessEngine.Context.AxisStatuses.TryGet(cardNum, axisNum, out AxisStatusSnapshot snapshot)
                            && snapshot.IsDetailFresh(AxisStatusCache.UiDetailMaxAgeMilliseconds))
                        {
                            Workspace.Control.PosTextBox[i].Text = snapshot.Position.ToString();
                            Workspace.Control.pictureBoxes[i].Image = snapshot.ServoOn ? validImage : invalidImage;
                            Workspace.Control.VelLabel[i].Text = snapshot.Speed.ToString();
                        }
                        else
                        {
                            Workspace.Control.PosTextBox[i].Text = "--";
                            Workspace.Control.pictureBoxes[i].Image = null;
                            Workspace.Control.VelLabel[i].Text = "--";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (!stateTimerErrorReported)
                {
                    stateTimerErrorReported = true;
                    if (Workspace.Info != null)
                    {
                        Workspace.Info.PrintInfo($"工站状态刷新异常：{ex.Message}", FrmInfo.Level.Error);
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

            if (Workspace.Runtime.Stores.Cards.TryGetAxis(cardNum, axisNum, out Axis axis))
            {
                Workspace.Runtime.ManualMotion.ConfigureAxis((ushort)cardNum, (ushort)axisNum,
                    new ManualMotionParameters(0,
                        axis.SpeedMax * (dataStation.ManualSpeedPercent / 100d),
                        axis.AccMax, axis.DecMax, 0, 0, axis.PulseToMM));
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
                // FrmMain 会先构造页面、再统一挂接 EditorWorkspace；构造期不能通过
                // Require 属性读取尚未挂接的工作区。进入页面后的编辑切换会再次刷新此状态。
                bool isRobotStation = editorWorkspace?.Control?.temp != null
                    && editorWorkspace.Control.temp.Type != StationType.Axis;
                ClearData.Enabled = enable && !isRobotStation;
                ClearData.Text = isRobotStation
                    ? "需在机器人控制器删除点位"
                    : "清除数据";
                ClearData.ToolTipText = isRobotStation
                    ? "机器人点位删除尚未接入控制器契约，本页面禁止只清除本地数据。"
                    : string.Empty;
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

        private void ResetPointBinding(DataStation station)
        {
            List<DataPos> source = station?.ListDataPos ?? new List<DataPos>();
            List<DataPos> visible = station != null && station.Type != StationType.Axis
                ? source.Take(DataStation.RobotPointCapacity).ToList()
                : source;
            if (Workspace.Control?.bindingSource != null)
            {
                Workspace.Control.bindingSource.DataSource = visible;
                Workspace.Control.bindingSource.ResetBindings(false);
                dataGridView1.DataSource = Workspace.Control.bindingSource;
                return;
            }
            dataGridView1.DataSource = null;
            dataGridView1.DataSource = visible;
        }

        private void CapturePointSnapshot()
        {
            if (Workspace.Control?.temp == null)
            {
                pointSnapshot.Clear();
                pointEditStation = null;
                pointEditStationIndex = -1;
                return;
            }
            pointSnapshot = CloneDataPosList(Workspace.Control.temp.ListDataPos);
            pointEditStation = Workspace.Control.temp;
            pointEditStationIndex = Workspace.Control.comboBox1.SelectedIndex;
        }

        private void RestorePointSnapshot()
        {
            if (pointEditStation == null)
            {
                return;
            }
            pointEditStation.ListDataPos = CloneDataPosList(pointSnapshot);
            pointEditStation.dicDataPos = BuildDataPosDictionary(pointEditStation.ListDataPos);
            if (Workspace.Control?.temp == pointEditStation || Workspace.Control?.comboBox1?.SelectedIndex == pointEditStationIndex)
            {
                ResetPointBinding(pointEditStation);
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
            if (Workspace.Control?.temp == null)
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
            Workspace.Control?.bindingSource?.EndEdit();
            DataStation station = pointEditStation ?? Workspace.Control?.temp;
            if (station == null)
            {
                MessageBox.Show("未选择工站，无法保存。");
                SetPointEditMode(false);
                ClearPointSnapshot();
                return;
            }
            if (station.Type != StationType.Axis)
            {
                if (!TrySaveRobotPointChanges(station))
                {
                    return;
                }
                SetPointEditMode(false);
                ClearPointSnapshot();
                return;
            }
            RebuildPointDictionary(station);
            if (!Workspace.Runtime.Stores.Stations.TryPersistCurrent(
                    Workspace.Runtime.Paths.ConfigPath, out _))
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

        private bool TrySaveRobotPointChanges(DataStation station)
        {
            List<DataPos> editedPoints = CloneDataPosList(station.ListDataPos);
            IGrouping<string, DataPos> duplicateName = editedPoints
                .Take(DataStation.RobotPointCapacity)
                .Where(point => point != null && !string.IsNullOrWhiteSpace(point.Name))
                .GroupBy(point => point.Name, StringComparer.Ordinal)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicateName != null)
            {
                MessageBox.Show($"机器人点位名称重复：{duplicateName.Key}。请修改后再保存。",
                    "机器人点位保存", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (pointEditStationIndex < 0 || pointEditStationIndex > short.MaxValue)
            {
                MessageBox.Show("机器人工站索引无效，无法保存点位。", "机器人点位保存",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            Dictionary<int, DataPos> snapshotByIndex = pointSnapshot
                .Where(point => point != null)
                .GroupBy(point => point.Index)
                .ToDictionary(group => group.Key, group => group.First());
            var changedPoints = new List<DataPos>();
            int visibleCount = Math.Min(DataStation.RobotPointCapacity, editedPoints.Count);
            for (int i = 0; i < visibleCount; i++)
            {
                DataPos current = editedPoints[i];
                DataPos previous = null;
                if (current != null)
                {
                    snapshotByIndex.TryGetValue(current.Index, out previous);
                }
                else if (i < pointSnapshot.Count)
                {
                    previous = pointSnapshot[i];
                }
                if (ArePointConfigurationsEqual(previous, current))
                {
                    continue;
                }
                if (previous?.IsMotionReady == true
                    && (current == null || string.IsNullOrWhiteSpace(current.Name)))
                {
                    MessageBox.Show("机器人点位需在机器人控制器删除点位，本页面不会只清除本地数据。",
                        "机器人点位删除", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return false;
                }
                if (current?.IsMotionReady == true)
                {
                    changedPoints.Add((DataPos)current.Clone());
                }
            }

            // SaveStationPoint 会持久化整个 StationStore。同步期间先恢复已提交基线，
            // 再逐个并入成功点，防止首个成功就把尚未写入控制器的后续编辑落盘。
            List<DataPos> committedPoints = CloneDataPosList(pointSnapshot);
            ReplaceStationPoints(station, committedPoints);
            foreach (DataPos changedPoint in changedPoints.OrderBy(point => point.Index))
            {
                if (!PrepareRobotPointForSave(station, changedPoint)
                    || !Workspace.Runtime.ManualMotion.TrySaveStationPoint(
                        (short)pointEditStationIndex, changedPoint))
                {
                    RestoreRobotPendingEdits(station, editedPoints, committedPoints);
                    MessageBox.Show(
                        $"机器人点位“{changedPoint.Name}”未同步，保存已停止。已成功的点位保持提交，其余编辑可重试或取消。",
                        "机器人点位保存", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
                committedPoints = CloneDataPosList(station.ListDataPos);
            }

            // 名称规划点不写机器人，但仍按当前平台契约保存为“待示教”。
            ReplaceStationPoints(station, editedPoints);
            if (!Workspace.Runtime.Stores.Stations.TryPersistCurrent(
                    Workspace.Runtime.Paths.ConfigPath, out string persistError))
            {
                RestoreRobotPendingEdits(station, editedPoints, committedPoints);
                MessageBox.Show(
                    $"机器人点位配置保存失败：{persistError}。控制器已确认的点位保持提交，其余编辑可重试或取消。",
                    "机器人点位保存", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            CapturePointSnapshot();
            ResetPointBinding(station);
            return true;
        }

        private bool PrepareRobotPointForSave(DataStation station, DataPos target)
        {
            if (station?.ListDataPos == null || target == null
                || target.Index < 0 || target.Index >= DataStation.RobotPointCapacity)
            {
                return false;
            }
            int listIndex = station.ListDataPos.FindIndex(
                point => point != null && point.Index == target.Index);
            if (listIndex < 0)
            {
                return false;
            }

            // 名称可能在本次编辑中改变。控制器只按索引保存坐标，因此保留旧坐标作为
            // SaveStationPoint 的补偿基线，只把配置槽名称切到新名称以通过一致性校验。
            DataPos prepared = (DataPos)station.ListDataPos[listIndex].Clone();
            prepared.Name = target.Name;
            station.ListDataPos[listIndex] = prepared;
            RebuildPointDictionary(station);
            return true;
        }

        private void RestoreRobotPendingEdits(DataStation station, List<DataPos> editedPoints,
            List<DataPos> committedPoints)
        {
            pointSnapshot = CloneDataPosList(committedPoints);
            ReplaceStationPoints(station, editedPoints);
            ResetPointBinding(station);
        }

        private void ReplaceStationPoints(DataStation station, List<DataPos> points)
        {
            station.ListDataPos = CloneDataPosList(points);
            RebuildPointDictionary(station);
        }

        private static bool ArePointConfigurationsEqual(DataPos left, DataPos right)
        {
            JToken leftToken = left == null ? JValue.CreateNull() : JToken.FromObject(left);
            JToken rightToken = right == null ? JValue.CreateNull() : JToken.FromObject(right);
            return JToken.DeepEquals(leftToken, rightToken);
        }

        private void btnPointCancel_Click(object sender, EventArgs e)
        {
            if (!isPointEditing)
            {
                return;
            }
            dataGridView1.CancelEdit();
            Workspace.Control?.bindingSource?.CancelEdit();
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
                if (Workspace.Control.temp == null)
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
                    KeyValuePair<string, DataPos> oldEntry = Workspace.Control.temp.dicDataPos.FirstOrDefault(item => item.Value != null && item.Value.Index == dataPos.Index);
                    string oldName = oldEntry.Key ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(newName))
                    {
                        MessageBox.Show("点位名称不能为空。");
                        editedRow.Cells[1].Value = oldName;
                        dataPos.Name = oldName;
                        return;
                    }
                    if (oldName != newName && Workspace.Control.temp.dicDataPos.TryGetValue(newName, out DataPos existed) && existed != null && existed.Index != dataPos.Index)
                    {
                        MessageBox.Show($"点位名称已存在：{newName}");
                        editedRow.Cells[1].Value = oldName;
                        dataPos.Name = oldName;
                        return;
                    }
                    if (!string.IsNullOrEmpty(oldName) && oldName != newName)
                    {
                        Workspace.Control.temp.dicDataPos.Remove(oldName);
                    }
                    dataPos.Name = newName;
                    if (string.IsNullOrEmpty(oldName))
                    {
                        // 只录入名称是在规划点位；坐标列编辑或“取点”后才成为已示教点位。
                        dataPos.IsTaught = false;
                    }
                    Workspace.Control.temp.dicDataPos[newName] = dataPos;
                }
                else if (e.ColumnIndex >= 2 && e.ColumnIndex <= 7
                    && !string.IsNullOrWhiteSpace(dataPos.Name))
                {
                    string coordinateText = Convert.ToString(
                        editedRow.Cells[e.ColumnIndex].Value,
                        System.Globalization.CultureInfo.CurrentCulture);
                    if (double.TryParse(coordinateText,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.CurrentCulture,
                            out double coordinate)
                        && !double.IsNaN(coordinate) && !double.IsInfinity(coordinate))
                    {
                        dataPos.IsTaught = true;
                    }
                }

                if (dataPos.Index >= 0 && dataPos.Index < Workspace.Control.temp.ListDataPos.Count)
                {
                    Workspace.Control.temp.ListDataPos[dataPos.Index] = dataPos;
                }
                dataGridView.InvalidateRow(e.RowIndex);
            }
        }

        private void RefreshRobotStationState(int stationIndex)
        {
            MotionStationStatus status = Workspace.Runtime.Motion.GetStationStatus((short)stationIndex);
            if (status == null)
            {
                return;
            }

            bool hasPosition = status.State != MotionStationState.Uninitialized
                && status.State != MotionStationState.Disconnected;
            int displayCount = Math.Min(6,
                Math.Min(Workspace.Control.PosTextBox.Count,
                    Math.Min(Workspace.Control.pictureBoxes.Count, Workspace.Control.VelLabel.Count)));
            for (int i = 0; i < displayCount; i++)
            {
                Workspace.Control.PosTextBox[i].Text = hasPosition && status.Position.Count > i
                    ? status.Position[i].ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)
                    : "--";
                Workspace.Control.pictureBoxes[i].Image = hasPosition
                    ? status.IsServoEnabled ? validImage : invalidImage
                    : null;
                Workspace.Control.VelLabel[i].Text = "--";
            }

            if (lastRobotState != status.State)
            {
                lastRobotState = status.State;
                Workspace.Control.RefreshMotionControlAvailability();
            }
        }

        private void Touch_Click(object sender, EventArgs e)
        {
            if (!isPointEditing)
            {
                MessageBox.Show("请先点击编辑。");
                return;
            }
            if (Workspace.Control.temp == null)
            {
                return;
            }
            if (iSelectedRow < 0 || iSelectedRow >= dataGridView1.Rows.Count)
            {
                return;
            }

            DataStation station = Workspace.Control.temp;
            if (Workspace.Runtime.Motion == null
                || station.Type == StationType.Axis && !Workspace.Runtime.Motion.IsCardInitialized)
            {
                MessageBox.Show("运动控制卡未初始化，无法取点。", "工站取点",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (station.Type != StationType.Axis)
            {
                if (!(dataGridView1.Rows[iSelectedRow].DataBoundItem is DataPos robotTaughtPoint)
                    || string.IsNullOrWhiteSpace(robotTaughtPoint.Name))
                {
                    MessageBox.Show("请先为点位设置名称。", "工站取点",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // SaveStationPoint 会持久化整个 StationStore。先以编辑开始时的已提交快照为基线，
                // 只让本次取点进入控制器与磁盘；其他尚未同步的表格编辑继续留在当前编辑会话。
                List<DataPos> pendingEdits = CloneDataPosList(station.ListDataPos);
                List<DataPos> committedPoints = CloneDataPosList(pointSnapshot);
                DataPos teachRequest = (DataPos)robotTaughtPoint.Clone();
                ReplaceStationPoints(station, committedPoints);
                if (!PrepareRobotPointForSave(station, teachRequest))
                {
                    RestoreRobotPendingEdits(station, pendingEdits, committedPoints);
                    MessageBox.Show("机器人点位配置与编辑快照不一致，无法取点。", "工站取点",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (!Workspace.Runtime.ManualMotion.TryTeachStationPoint(
                        (short)Workspace.Control.CurrentStationIndex,
                        teachRequest,
                        out DataPos robotCapturedPoint))
                {
                    RestoreRobotPendingEdits(station, pendingEdits, committedPoints);
                    return;
                }

                committedPoints = CloneDataPosList(station.ListDataPos);
                int pendingIndex = pendingEdits.FindIndex(
                    point => point != null && point.Index == robotCapturedPoint.Index);
                if (pendingIndex >= 0)
                {
                    pendingEdits[pendingIndex] = (DataPos)robotCapturedPoint.Clone();
                }
                RestoreRobotPendingEdits(station, pendingEdits, committedPoints);
                MessageBox.Show("机器人取点已同步并保存。", "工站取点",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var positions = new List<(int columnIndex, double value)>();
            try
            {
                for (int i = 0; i < Workspace.Control.temp.dataAxis.axisConfigs.Count; i++)
                {
                    AxisConfig axisConfig = Workspace.Control.temp.dataAxis.axisConfigs[i];
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
                    positions.Add((2 + i, Workspace.Runtime.Motion.GetAxisPos(cardNum, (ushort)axisConfig.axis.AxisNum)));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"读取轴位置失败，本次未写入任何点位：{ex.Message}", "工站取点",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (positions.Count == 0)
            {
                MessageBox.Show("工站没有已配置的有效轴，无法完成取点。", "工站取点",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            foreach (var position in positions)
            {
                dataGridView1.Rows[iSelectedRow].Cells[position.columnIndex].Value =
                    position.value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            if (dataGridView1.Rows[iSelectedRow].DataBoundItem is DataPos taughtPoint
                && !string.IsNullOrWhiteSpace(taughtPoint.Name))
            {
                taughtPoint.IsTaught = true;
                dataGridView1.Refresh();
            }
        }

        private void MovePoint_Click(object sender, EventArgs e)
        {
            DataStation station = Workspace.Control.temp;
            if (station == null)
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
            DataPos selectedPoint = dataGridView1.Rows[iSelectedRow].DataBoundItem as DataPos;
            if (selectedPoint == null)
            {
                return;
            }
            if (selectedPoint.IsTaught == false)
            {
                MessageBox.Show("该点位仅完成名称规划，尚未人工示教坐标，不能执行移动。", "点位未示教",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            // 轴工站与机器人工站统一走工站运行时，保留 3.0 的 GO 语义；
            // ProcessEngine 会按站型原子占用实际物理轴，页面不再逐轴拼装命令。
            Workspace.Runtime.ManualMotion.TryMoveStationToPoint(
                (short)Workspace.Control.CurrentStationIndex,
                selectedPoint,
                station.ManualSpeedPercent,
                StationMoveMode.Go,
                false);
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
            if (Workspace.Control?.temp != null
                && Workspace.Control.temp.Type != StationType.Axis)
            {
                MessageBox.Show("机器人点位需在机器人控制器删除点位，本页面不会只清除本地数据。",
                    "机器人点位删除", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show("确认清除选中的点位数据？", "清除确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }
            DataGridViewRow rowToClear = dataGridView1.Rows[iSelectedRow];
            DataPos dataPos = rowToClear.DataBoundItem as DataPos;
            if (dataPos != null && Workspace.Control.temp != null)
            {
                string oldName = dataPos.Name;
                if (!string.IsNullOrWhiteSpace(oldName) && Workspace.Control.temp.dicDataPos.ContainsKey(oldName))
                {
                    Workspace.Control.temp.dicDataPos.Remove(oldName);
                }
                dataPos.Name = string.Empty;
                dataPos.IsTaught = null;
                dataPos.X = -1;
                dataPos.Y = -1;
                dataPos.Z = -1;
                dataPos.U = -1;
                dataPos.V = -1;
                dataPos.W = -1;
                if (dataPos.Index >= 0 && dataPos.Index < Workspace.Control.temp.ListDataPos.Count)
                {
                    Workspace.Control.temp.ListDataPos[dataPos.Index] = dataPos;
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
            if (Workspace.Control.temp == null)
            {
                return;
            }
            if (iSelectedRow < 0 || iSelectedRow >= Workspace.Control.temp.ListDataPos.Count)
            {
                return;
            }
            if (ListDataPos4Copy.Count == 0)
            {
                return;
            }

            int visibleCapacity = Workspace.Control.temp.Type == StationType.Axis
                ? Workspace.Control.temp.ListDataPos.Count
                : Math.Min(
                    DataStation.RobotPointCapacity,
                    Workspace.Control.temp.ListDataPos.Count);
            int maxPasteCount = Math.Min(ListDataPos4Copy.Count, visibleCapacity - iSelectedRow);
            if (maxPasteCount <= 0)
            {
                return;
            }

            HashSet<string> replaceNames = new HashSet<string>();
            for (int i = 0; i < maxPasteCount; i++)
            {
                DataPos oldPos = Workspace.Control.temp.ListDataPos[iSelectedRow + i];
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
                if (Workspace.Control.temp.dicDataPos.ContainsKey(source.Name) && !replaceNames.Contains(source.Name))
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
                string oldName = Workspace.Control.temp.ListDataPos[targetIndex]?.Name;
                if (!string.IsNullOrEmpty(oldName) && Workspace.Control.temp.dicDataPos.ContainsKey(oldName))
                {
                    Workspace.Control.temp.dicDataPos.Remove(oldName);
                }
                deepCopy[i].Index = targetIndex;
                Workspace.Control.temp.dicDataPos[deepCopy[i].Name] = deepCopy[i];
                Workspace.Control.temp.ListDataPos[targetIndex] = deepCopy[i];
            }
            int rowCountAfterPaste = iSelectedRow + deepCopy.Count;
            for (int i = iSelectedRow; i < rowCountAfterPaste && i < dataGridView1.Rows.Count; i++)
            {
                dataGridView1.Rows[i].DefaultCellStyle.BackColor = UiPalette.Danger;
            }
        }
    }
}
