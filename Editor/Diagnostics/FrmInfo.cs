// 模块：编辑器 / 诊断。
// 职责范围：运行日志、状态、流程图、断点、性能和事故诊断页面。

using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Automation
{
    public partial class FrmInfo : Form
    {
        private readonly List<ProcStatusCellCache> statusCellCache = new List<ProcStatusCellCache>();
        private System.Windows.Forms.Timer statusTimer;
        private bool statusPageActive;
        private bool statusPageInitialized;
        private int statusGroupCount = 1;
        private int lastStatusProcCount = -1;
        private const int StatusColumnsPerGroup = 4;
        private const int StatusMinGroupWidth = 320;
        private const int MaxInfoLogEntries = 200;
        private const int MaxPendingInfoLogEntries = 2000;
        private const int InfoFlushIntervalMs = 100;
        private const int InfoFlushBatchSize = 256;
        private const int InfoAutoScrollIdleMs = 20000;
        private const int SB_VERT = 1;
        private const uint SIF_ALL = 0x17;
        private ContextMenuStrip infoMenu;
        private ToolStripMenuItem menuClearInfo;
        private readonly ConcurrentQueue<InfoLogEntry> pendingInfoQueue = new ConcurrentQueue<InfoLogEntry>();
        private int pendingInfoCount;
        private readonly FixedRingBuffer<InfoLogEntry> infoLogBuffer = new FixedRingBuffer<InfoLogEntry>(MaxInfoLogEntries);
        private System.Windows.Forms.Timer infoFlushTimer;
        private System.Windows.Forms.Timer infoAutoScrollTimer;
        private bool infoAutoScrollPausedByUser;
        private DateTime infoLastInteractionUtc;
        private ImageList infoRowHeightImages;
        private Bitmap infoRowHeightBitmap;
        private Panel infoTabBar;
        private Panel infoContentFrame;
        private Panel infoContentHost;
        private Button infoTabButton;
        private Button statusTabButton;
        private Panel infoTabIndicator;
        private bool statusTabSelected;

        public FrmInfo()
        {
            InitializeComponent();
            ConfigureAppearance();
            Disposed += FrmInfo_Disposed;
        }

        private void ConfigureAppearance()
        {
            BackColor = UiPalette.Background;
            tabPage2.BackColor = UiPalette.SurfaceStrong;
            tabPage2.UseVisualStyleBackColor = false;
            tabPage2.Padding = new Padding(1);
            tabPageStatus.BackColor = UiPalette.SurfaceStrong;
            tabPageStatus.UseVisualStyleBackColor = false;
            tabPageStatus.Padding = new Padding(1);

            lvInfoLog.BorderStyle = BorderStyle.None;
            lvInfoLog.BackColor = UiPalette.Background;
            lvInfoLog.ForeColor = UiPalette.TextPrimary;
            lvInfoLog.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular);
            infoRowHeightImages = new ImageList
            {
                ColorDepth = ColorDepth.Depth32Bit,
                ImageSize = new Size(1, 26),
                TransparentColor = Color.Transparent
            };
            infoRowHeightBitmap = new Bitmap(1, 26);
            infoRowHeightImages.Images.Add(infoRowHeightBitmap);
            lvInfoLog.SmallImageList = infoRowHeightImages;

            panelStatusTools.Visible = false;
            panelStatusTools.Height = 0;

            infoContentFrame = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(1),
                BackColor = UiPalette.StrokeStrong
            };
            Controls.Remove(tabControl1);
            tabPage2.Controls.Remove(lvInfoLog);
            tabPageStatus.Controls.Remove(dgvProcStatus);
            infoContentHost = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(6),
                BackColor = UiPalette.Background
            };
            lvInfoLog.Dock = DockStyle.Fill;
            dgvProcStatus.Dock = DockStyle.Fill;
            infoContentHost.Controls.Add(dgvProcStatus);
            infoContentHost.Controls.Add(lvInfoLog);
            infoContentFrame.Controls.Add(infoContentHost);
            Controls.Add(infoContentFrame);

            infoTabBar = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 35,
                BackColor = UiPalette.BrandSoft
            };
            infoTabBar.Paint += (sender, args) =>
            {
                using (Pen pen = new Pen(UiPalette.Stroke))
                {
                    args.Graphics.DrawLine(pen, 0, 0, infoTabBar.ClientSize.Width, 0);
                }
            };
            infoTabButton = CreateInfoTabButton("运行信息", 0);
            statusTabButton = CreateInfoTabButton("流程状态", 84);
            infoTabButton.Click += (sender, args) =>
            {
                SelectInfoPage(false);
            };
            statusTabButton.Click += (sender, args) =>
            {
                SelectInfoPage(true);
            };
            infoTabIndicator = new Panel
            {
                BackColor = UiPalette.BrandAccent,
                Height = 3,
                Enabled = false
            };
            infoTabBar.Controls.Add(infoTabButton);
            infoTabBar.Controls.Add(statusTabButton);
            infoTabBar.Controls.Add(infoTabIndicator);
            infoContentFrame.Controls.Add(infoTabBar);
            infoTabBar.BringToFront();
            UpdateInfoTabButtons();

            dgvProcStatus.BorderStyle = BorderStyle.None;
            dgvProcStatus.BackgroundColor = UiPalette.Background;
            dgvProcStatus.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            dgvProcStatus.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            dgvProcStatus.GridColor = UiPalette.Stroke;
            dgvProcStatus.ColumnHeadersHeight = 32;
            dgvProcStatus.RowTemplate.Height = 28;
            dgvProcStatus.ColumnHeadersDefaultCellStyle.BackColor = UiPalette.SurfaceSubtle;
            dgvProcStatus.ColumnHeadersDefaultCellStyle.ForeColor = UiPalette.TextPrimary;
            dgvProcStatus.ColumnHeadersDefaultCellStyle.SelectionBackColor = UiPalette.SurfaceSubtle;
            dgvProcStatus.ColumnHeadersDefaultCellStyle.SelectionForeColor = UiPalette.TextPrimary;
            dgvProcStatus.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            dgvProcStatus.DefaultCellStyle.BackColor = UiPalette.SurfaceStrong;
            dgvProcStatus.DefaultCellStyle.ForeColor = UiPalette.TextPrimary;
            dgvProcStatus.DefaultCellStyle.SelectionBackColor = UiPalette.Selection;
            dgvProcStatus.DefaultCellStyle.SelectionForeColor = UiPalette.SelectionText;
            dgvProcStatus.DefaultCellStyle.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular);
            dgvProcStatus.AlternatingRowsDefaultCellStyle.BackColor = UiPalette.Surface;
        }

        private Button CreateInfoTabButton(string text, int left)
        {
            Button button = new Button
            {
                Text = text,
                Location = new Point(left, 1),
                Size = new Size(84, 34),
                BackColor = UiPalette.BrandSoft,
                ForeColor = UiPalette.TextSecondary,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular),
                FlatStyle = FlatStyle.Flat,
                TabStop = false,
                UseVisualStyleBackColor = false
            };
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = UiPalette.StrokeStrong;
            button.FlatAppearance.MouseOverBackColor = UiPalette.BrandSoftHover;
            button.FlatAppearance.MouseDownBackColor = UiPalette.BrandSoftHover;
            return button;
        }

        private void UpdateInfoTabButtons()
        {
            if (infoTabButton == null || statusTabButton == null || infoTabIndicator == null)
            {
                return;
            }
            bool infoSelected = !statusTabSelected;
            infoTabButton.BackColor = infoSelected
                ? UiPalette.SurfaceStrong
                : UiPalette.BrandSoft;
            statusTabButton.BackColor = infoSelected
                ? UiPalette.BrandSoft
                : UiPalette.SurfaceStrong;
            infoTabButton.ForeColor = infoSelected
                ? UiPalette.Brand
                : UiPalette.TextSecondary;
            statusTabButton.ForeColor = infoSelected
                ? UiPalette.TextSecondary
                : UiPalette.Brand;
            Button selectedButton = infoSelected ? infoTabButton : statusTabButton;
            infoTabIndicator.SetBounds(selectedButton.Left + 14, 1, selectedButton.Width - 28, 3);
            infoTabIndicator.BringToFront();
            if (infoContentHost != null)
            {
                lvInfoLog.Visible = infoSelected;
                dgvProcStatus.Visible = !infoSelected;
                if (infoSelected)
                {
                    lvInfoLog.BringToFront();
                }
                else
                {
                    dgvProcStatus.BringToFront();
                }
            }
        }

        private void SelectInfoPage(bool selectStatus)
        {
            statusTabSelected = selectStatus;
            int selectedIndex = selectStatus ? 1 : 0;
            if (tabControl1.SelectedIndex != selectedIndex)
            {
                tabControl1.SelectedIndex = selectedIndex;
            }
            UpdateInfoTabButtons();
            UpdateStatusTimerState();
            ActiveControl = null;
        }

        private void FrmInfo_Disposed(object sender, EventArgs e)
        {
            if (statusTimer != null)
            {
                statusTimer.Stop();
                statusTimer.Tick -= StatusTimer_Tick;
                statusTimer.Dispose();
                statusTimer = null;
            }
            if (infoFlushTimer != null)
            {
                infoFlushTimer.Stop();
                infoFlushTimer.Tick -= InfoFlushTimer_Tick;
                infoFlushTimer.Dispose();
                infoFlushTimer = null;
            }
            if (infoAutoScrollTimer != null)
            {
                infoAutoScrollTimer.Stop();
                infoAutoScrollTimer.Tick -= InfoAutoScrollTimer_Tick;
                infoAutoScrollTimer.Dispose();
                infoAutoScrollTimer = null;
            }

            lvInfoLog.RetrieveVirtualItem -= lvInfoLog_RetrieveVirtualItem;
            lvInfoLog.Resize -= lvInfoLog_Resize;
            lvInfoLog.MouseWheel -= lvInfoLog_MouseWheel;
            lvInfoLog.MouseDown -= lvInfoLog_MouseDown;
            lvInfoLog.MouseDoubleClick -= lvInfoLog_MouseDoubleClick;
            lvInfoLog.KeyDown -= lvInfoLog_KeyDown;
            tabControl1.SelectedIndexChanged -= tabControl1_SelectedIndexChanged;
            VisibleChanged -= FrmInfo_VisibleChanged;
            dgvProcStatus.CellDoubleClick -= dgvProcStatus_CellDoubleClick;
            dgvProcStatus.SizeChanged -= dgvProcStatus_SizeChanged;

            if (infoMenu != null)
            {
                lvInfoLog.ContextMenuStrip = null;
                infoMenu.Dispose();
                infoMenu = null;
                menuClearInfo = null;
            }
            lvInfoLog.SmallImageList = null;
            infoRowHeightImages?.Dispose();
            infoRowHeightImages = null;
            infoRowHeightBitmap?.Dispose();
            infoRowHeightBitmap = null;
        }

        private void FrmInfo_Load(object sender, EventArgs e)
        {
            InitializeStatusPage();
            InitializeInfoMenu();
            InitializeInfoStreamBehavior();
        }

        private void btnClearInfo_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("确认清空信息记录？", "清空确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }
            ClearInfoEntries();
        }

        private void ClearInfoEntries()
        {
            while (pendingInfoQueue.TryDequeue(out _))
            {
                Interlocked.Decrement(ref pendingInfoCount);
            }
            infoLogBuffer.Clear();
            RefreshInfoListView();
            infoAutoScrollPausedByUser = false;
        }

        private void InitializeInfoMenu()
        {
            if (infoMenu != null)
            {
                return;
            }
            infoMenu = new ContextMenuStrip();
            ToolStripMenuItem menuCopyInfo = new ToolStripMenuItem("复制");
            menuCopyInfo.Click += menuCopyInfo_Click;
            infoMenu.Items.Add(menuCopyInfo);
            infoMenu.Items.Add(new ToolStripSeparator());
            menuClearInfo = new ToolStripMenuItem("清空");
            menuClearInfo.Click += btnClearInfo_Click;
            infoMenu.Items.Add(menuClearInfo);
            lvInfoLog.ContextMenuStrip = infoMenu;
        }

        private void menuCopyInfo_Click(object sender, EventArgs e)
        {
            CopySelectedInfo();
        }

        private void CopySelectedInfo()
        {
            if (lvInfoLog == null || lvInfoLog.IsDisposed || infoLogBuffer.Count == 0)
            {
                return;
            }
            ListView.SelectedIndexCollection indices = lvInfoLog.SelectedIndices;
            if (indices == null || indices.Count == 0)
            {
                return;
            }
            var sb = new StringBuilder();
            foreach (int index in indices)
            {
                if (index < 0 || index >= infoLogBuffer.Count)
                {
                    continue;
                }
                InfoLogEntry entry = infoLogBuffer[index];
                sb.Append(entry.TimeText).Append(entry.Message).Append("\r\n");
            }
            string text = sb.ToString().TrimEnd('\r', '\n');
            if (text.Length > 0)
            {
                try { Clipboard.SetText(text); }
                catch { /* 剪贴板被占用，忽略 */ }
            }
        }

        private void InitializeInfoStreamBehavior()
        {
            if (infoAutoScrollTimer != null || infoFlushTimer != null)
            {
                return;
            }
            lvInfoLog.RetrieveVirtualItem += lvInfoLog_RetrieveVirtualItem;
            lvInfoLog.Resize += lvInfoLog_Resize;
            lvInfoLog.MouseWheel += lvInfoLog_MouseWheel;
            lvInfoLog.MouseDown += lvInfoLog_MouseDown;
            lvInfoLog.MouseDoubleClick += lvInfoLog_MouseDoubleClick;
            lvInfoLog.KeyDown += lvInfoLog_KeyDown;

            infoFlushTimer = new System.Windows.Forms.Timer();
            infoFlushTimer.Interval = InfoFlushIntervalMs;
            infoFlushTimer.Tick += InfoFlushTimer_Tick;
            infoFlushTimer.Start();

            infoAutoScrollTimer = new System.Windows.Forms.Timer();
            infoAutoScrollTimer.Interval = 500;
            infoAutoScrollTimer.Tick += InfoAutoScrollTimer_Tick;
            infoAutoScrollTimer.Start();
            RefreshInfoListView();
            UpdateInfoLogColumns();
        }

        private void lvInfoLog_RetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e)
        {
            if (e.ItemIndex < 0 || e.ItemIndex >= infoLogBuffer.Count)
            {
                e.Item = new ListViewItem(string.Empty);
                return;
            }
            InfoLogEntry entry = infoLogBuffer[e.ItemIndex];
            ListViewItem item = new ListViewItem(entry.TimeText);
            item.UseItemStyleForSubItems = false;
            item.SubItems.Add(entry.Message);
            item.SubItems[0].BackColor = GetInfoPrefixColor(entry.Level);
            item.SubItems[0].ForeColor = GetInfoPrefixForeColor(entry.Level);
            item.SubItems[1].BackColor = e.ItemIndex % 2 == 0
                ? UiPalette.SurfaceStrong
                : UiPalette.Surface;
            item.SubItems[1].ForeColor = entry.Level == Level.Error
                ? UiPalette.DangerHover
                : UiPalette.TextPrimary;
            e.Item = item;
        }

        private void lvInfoLog_Resize(object sender, EventArgs e)
        {
            UpdateInfoLogColumns();
        }

        private void lvInfoLog_MouseWheel(object sender, MouseEventArgs e)
        {
            OnInfoStreamUserInteraction();
        }

        private void lvInfoLog_MouseDown(object sender, MouseEventArgs e)
        {
            OnInfoStreamUserInteraction();
        }

        private void lvInfoLog_KeyDown(object sender, KeyEventArgs e)
        {
            if (e != null && e.Control && e.KeyCode == Keys.C)
            {
                CopySelectedInfo();
                e.Handled = true;
                return;
            }
            OnInfoStreamUserInteraction();
        }

        private void lvInfoLog_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || lvInfoLog == null || lvInfoLog.IsDisposed)
            {
                return;
            }
            ListViewHitTestInfo hit = lvInfoLog.HitTest(e.Location);
            if (hit?.Item == null)
            {
                return;
            }
            int index = hit.Item.Index;
            if (index < 0 || index >= infoLogBuffer.Count)
            {
                return;
            }
            InfoLogEntry entry = infoLogBuffer[index];
            string content = $"{entry.TimeText}{entry.Message}";
            MessageBox.Show(this, content, "日志全文", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OnInfoStreamUserInteraction()
        {
            infoLastInteractionUtc = DateTime.UtcNow;
            infoAutoScrollPausedByUser = !IsInfoLogEndVisible();
        }

        private void InfoFlushTimer_Tick(object sender, EventArgs e)
        {
            if (IsDisposed || lvInfoLog == null || lvInfoLog.IsDisposed)
            {
                return;
            }
            bool hasNewEntry = false;
            bool wasAtEnd = !infoAutoScrollPausedByUser && IsInfoLogEndVisible();
            int flushCount = 0;
            while (flushCount < InfoFlushBatchSize && pendingInfoQueue.TryDequeue(out InfoLogEntry entry))
            {
                Interlocked.Decrement(ref pendingInfoCount);
                infoLogBuffer.Add(entry);
                hasNewEntry = true;
                flushCount++;
            }
            if (!hasNewEntry)
            {
                return;
            }
            RefreshInfoListView();
            if (!infoAutoScrollPausedByUser && wasAtEnd)
            {
                ScrollInfoToBottom();
            }
        }

        private void InfoAutoScrollTimer_Tick(object sender, EventArgs e)
        {
            if (!infoAutoScrollPausedByUser)
            {
                return;
            }
            if ((DateTime.UtcNow - infoLastInteractionUtc).TotalMilliseconds < InfoAutoScrollIdleMs)
            {
                return;
            }
            infoAutoScrollPausedByUser = false;
            ScrollInfoToBottom();
        }

        private bool IsInfoLogEndVisible()
        {
            if (lvInfoLog == null || lvInfoLog.IsDisposed || lvInfoLog.VirtualListSize <= 0)
            {
                return true;
            }
            if (!TryGetVerticalScrollInfo(lvInfoLog, out SCROLLINFO scrollInfo))
            {
                return true;
            }
            return scrollInfo.nPos + (int)scrollInfo.nPage >= scrollInfo.nMax;
        }

        private static bool TryGetVerticalScrollInfo(Control control, out SCROLLINFO scrollInfo)
        {
            scrollInfo = default;
            if (control == null || control.IsDisposed || !control.IsHandleCreated)
            {
                return false;
            }
            SCROLLINFO info = new SCROLLINFO
            {
                cbSize = (uint)Marshal.SizeOf(typeof(SCROLLINFO)),
                fMask = SIF_ALL
            };
            if (!GetScrollInfo(control.Handle, SB_VERT, ref info))
            {
                return false;
            }
            scrollInfo = info;
            return true;
        }

        private void ScrollInfoToBottom()
        {
            if (lvInfoLog == null || lvInfoLog.IsDisposed || lvInfoLog.VirtualListSize <= 0)
            {
                return;
            }
            lvInfoLog.EnsureVisible(lvInfoLog.VirtualListSize - 1);
        }

        private void RefreshInfoListView()
        {
            if (lvInfoLog == null || lvInfoLog.IsDisposed)
            {
                return;
            }
            lvInfoLog.VirtualListSize = infoLogBuffer.Count;
            lvInfoLog.Invalidate();
        }

        private void UpdateInfoLogColumns()
        {
            if (lvInfoLog == null || lvInfoLog.IsDisposed || lvInfoLog.Columns.Count < 2)
            {
                return;
            }
            int timeWidth = 180;
            lvInfoLog.Columns[0].Width = timeWidth;
            int msgWidth = lvInfoLog.ClientSize.Width - timeWidth - 4;
            lvInfoLog.Columns[1].Width = Math.Max(120, msgWidth);
        }

        private static Color GetInfoPrefixColor(Level level)
        {
            if (level == Level.Error)
            {
                return UiPalette.DangerSoft;
            }
            return UiPalette.BrandSoft;
        }

        private static Color GetInfoPrefixForeColor(Level level)
        {
            return level == Level.Error
                ? UiPalette.Danger
                : UiPalette.TextSecondary;
        }


        [Browsable(false)]
        [JsonIgnore]
        public Level level { get; set; } = 0;

        public Level GetState()
        {
            return level;
        }
        public void SetState(Level level)
        {
            this.level = level;
        }
        public enum Level
        {
            Error = 0,
            Normal,
        }
        // InfoLevel 信息级别
        // 0 红色报警
        // 1 普通信息
        public void PrintInfo(string str, Level InfoLevel)
        {
            if (IsDisposed)
            {
                return;
            }
            pendingInfoQueue.Enqueue(new InfoLogEntry
            {
                TimeText = $"[{DateTime.Now:yyyy-MM-dd HH时mm分ss秒}]",
                Message = $"：{str}",
                Level = InfoLevel
            });
            Interlocked.Increment(ref pendingInfoCount);
            while (Volatile.Read(ref pendingInfoCount) > MaxPendingInfoLogEntries
                && pendingInfoQueue.TryDequeue(out _))
            {
                Interlocked.Decrement(ref pendingInfoCount);
            }
        }

        public IReadOnlyList<InfoLogSnapshot> GetInfoLogTail(int maxCount)
        {
            if (maxCount <= 0)
            {
                return new List<InfoLogSnapshot>();
            }
            if (IsDisposed)
            {
                return new List<InfoLogSnapshot>();
            }
            if (InvokeRequired)
            {
                return (IReadOnlyList<InfoLogSnapshot>)Invoke(new Func<int, IReadOnlyList<InfoLogSnapshot>>(GetInfoLogTail), maxCount);
            }

            bool appendedPending = false;
            while (pendingInfoQueue.TryDequeue(out InfoLogEntry entry))
            {
                Interlocked.Decrement(ref pendingInfoCount);
                infoLogBuffer.Add(entry);
                appendedPending = true;
            }
            if (appendedPending)
            {
                RefreshInfoListView();
            }

            int count = Math.Min(maxCount, infoLogBuffer.Count);
            int start = Math.Max(0, infoLogBuffer.Count - count);
            List<InfoLogSnapshot> result = new List<InfoLogSnapshot>(count);
            for (int i = start; i < infoLogBuffer.Count; i++)
            {
                InfoLogEntry item = infoLogBuffer[i];
                result.Add(new InfoLogSnapshot
                {
                    TimeText = item.TimeText,
                    Message = item.Message,
                    Level = item.Level
                });
            }
            return result;
        }

        private void InitializeStatusPage()
        {
            if (statusPageInitialized)
            {
                return;
            }
            statusPageInitialized = true;
            statusTimer = new System.Windows.Forms.Timer();
            statusTimer.Interval = 300;
            statusTimer.Tick += StatusTimer_Tick;
            tabControl1.SelectedIndexChanged += tabControl1_SelectedIndexChanged;
            VisibleChanged += FrmInfo_VisibleChanged;
            dgvProcStatus.CellDoubleClick += dgvProcStatus_CellDoubleClick;
            dgvProcStatus.SizeChanged += dgvProcStatus_SizeChanged;
            RebuildStatusColumns(GetStatusGroupCount(0));
            UpdateStatusTimerState();
        }

        private void FrmInfo_VisibleChanged(object sender, EventArgs e)
        {
            UpdateStatusTimerState();
        }

        private void tabControl1_SelectedIndexChanged(object sender, EventArgs e)
        {
            statusTabSelected = tabControl1.SelectedIndex == 1;
            UpdateInfoTabButtons();
            UpdateStatusTimerState();
        }

        private void StatusTimer_Tick(object sender, EventArgs e)
        {
            if (!statusPageActive)
            {
                return;
            }
            RefreshProcStatus();
        }

        private void dgvProcStatus_SizeChanged(object sender, EventArgs e)
        {
            if (!statusPageActive)
            {
                return;
            }
            RefreshProcStatus();
        }

        private void UpdateStatusTimerState()
        {
            if (statusTimer == null)
            {
                return;
            }
            bool shouldRun = IsStatusPageVisible();
            if (statusPageActive == shouldRun)
            {
                return;
            }
            statusPageActive = shouldRun;
            if (statusPageActive)
            {
                RefreshProcStatus();
                statusTimer.Start();
            }
            else
            {
                statusTimer.Stop();
            }
        }

        private bool IsStatusPageVisible()
        {
            return Visible && statusTabSelected;
        }

        private void RefreshProcStatus()
        {
            bool layoutSuspended = false;
            try
            {
                if (IsDisposed || dgvProcStatus == null || dgvProcStatus.IsDisposed)
                {
                    return;
                }
                if (Workspace.Runtime.ProcessEngine == null)
                {
                    ClearStatusRows();
                    return;
                }
                IReadOnlyList<EngineSnapshot> snapshots = Workspace.Runtime.ProcessEngine.GetSnapshots();
                if (snapshots == null)
                {
                    ClearStatusRows();
                    return;
                }
                int procCount = snapshots.Count;
                int groupCount = GetStatusGroupCount(procCount);
                bool layoutChanged = groupCount != statusGroupCount;
                bool countChanged = procCount != lastStatusProcCount;
                if (layoutChanged)
                {
                    statusGroupCount = groupCount;
                    RebuildStatusColumns(groupCount);
                }
                dgvProcStatus.SuspendLayout();
                layoutSuspended = true;
                int rowCount = GetRowCount(procCount, groupCount);
                EnsureStatusRowCount(rowCount);
                if (layoutChanged || countChanged)
                {
                    ResetStatusCellCache(procCount);
                    ClearStatusCells();
                }
                else
                {
                    EnsureStatusCellCache(procCount);
                }
                for (int i = 0; i < procCount; i++)
                {
                    UpdateStatusCell(i, snapshots[i]);
                }
                lastStatusProcCount = procCount;
            }
            catch (Exception ex)
            {
                PrintInfo($"流程状态刷新失败：{ex.Message}", Level.Error);
                if (statusTimer != null)
                {
                    statusTimer.Stop();
                }
                statusPageActive = false;
            }
            finally
            {
                if (layoutSuspended)
                {
                    dgvProcStatus.ResumeLayout();
                }
            }
        }

        private int GetRowCount(int procCount, int groupCount)
        {
            if (groupCount <= 0)
            {
                throw new InvalidOperationException("流程状态列布局异常");
            }
            if (procCount <= 0)
            {
                return 0;
            }
            return (procCount + groupCount - 1) / groupCount;
        }

        private int GetStatusGroupCount(int procCount)
        {
            int width = dgvProcStatus.ClientSize.Width;
            if (width <= 0)
            {
                return 1;
            }
            int groupCount = Math.Max(1, width / StatusMinGroupWidth);
            if (procCount > 0)
            {
                groupCount = Math.Min(groupCount, procCount);
            }
            return Math.Max(1, groupCount);
        }

        private void RebuildStatusColumns(int groupCount)
        {
            if (groupCount <= 0)
            {
                throw new InvalidOperationException("流程状态列布局异常");
            }
            dgvProcStatus.Columns.Clear();
            for (int i = 0; i < groupCount; i++)
            {
                AddStatusColumn(i, StatusColumnKind.Proc, "流程", 25F, DataGridViewContentAlignment.MiddleLeft);
                AddStatusColumn(i, StatusColumnKind.State, "状态", 15F, DataGridViewContentAlignment.MiddleCenter);
                AddStatusColumn(i, StatusColumnKind.Position, "位置", 20F, DataGridViewContentAlignment.MiddleCenter);
                AddStatusColumn(i, StatusColumnKind.OpName, "指令", 40F, DataGridViewContentAlignment.MiddleLeft);
            }
        }

        private void AddStatusColumn(int groupIndex, StatusColumnKind kind, string headerText, float fillWeight,
            DataGridViewContentAlignment alignment)
        {
            DataGridViewTextBoxColumn column = new DataGridViewTextBoxColumn();
            column.ReadOnly = true;
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
            column.HeaderText = headerText;
            column.FillWeight = fillWeight;
            column.Tag = new StatusColumnTag(groupIndex, kind);
            column.DefaultCellStyle.Alignment = alignment;
            if (kind != StatusColumnKind.State)
            {
                column.DefaultCellStyle.BackColor = GetStatusGroupBackColor(groupIndex);
            }
            if (kind == StatusColumnKind.OpName)
            {
                column.DividerWidth = 3;
            }
            dgvProcStatus.Columns.Add(column);
        }

        private Color GetStatusGroupBackColor(int groupIndex)
        {
            return groupIndex % 2 == 0
                ? UiPalette.SurfaceStrong
                : UiPalette.Surface;
        }

        private void EnsureStatusRowCount(int targetCount)
        {
            if (targetCount < 0)
            {
                throw new InvalidOperationException("流程状态行数异常");
            }
            while (dgvProcStatus.Rows.Count < targetCount)
            {
                dgvProcStatus.Rows.Add();
            }
            while (dgvProcStatus.Rows.Count > targetCount)
            {
                int lastIndex = dgvProcStatus.Rows.Count - 1;
                dgvProcStatus.Rows.RemoveAt(lastIndex);
            }
        }

        private void ResetStatusCellCache(int procCount)
        {
            statusCellCache.Clear();
            for (int i = 0; i < procCount; i++)
            {
                statusCellCache.Add(new ProcStatusCellCache());
            }
        }

        private void EnsureStatusCellCache(int procCount)
        {
            while (statusCellCache.Count < procCount)
            {
                statusCellCache.Add(new ProcStatusCellCache());
            }
            while (statusCellCache.Count > procCount)
            {
                statusCellCache.RemoveAt(statusCellCache.Count - 1);
            }
        }

        private void ClearStatusRows()
        {
            if (dgvProcStatus.Rows.Count == 0)
            {
                return;
            }
            dgvProcStatus.Rows.Clear();
            statusCellCache.Clear();
            lastStatusProcCount = -1;
        }

        private void ClearStatusCells()
        {
            if (dgvProcStatus.Rows.Count == 0 || dgvProcStatus.Columns.Count == 0)
            {
                return;
            }
            foreach (DataGridViewRow row in dgvProcStatus.Rows)
            {
                foreach (DataGridViewCell cell in row.Cells)
                {
                    cell.Value = null;
                    cell.Style.ForeColor = Color.Empty;
                    cell.Style.SelectionForeColor = Color.Empty;
                    cell.Style.BackColor = Color.Empty;
                    cell.Style.SelectionBackColor = Color.Empty;
                }
            }
        }

        private void UpdateStatusCell(int procIndex, EngineSnapshot snapshot)
        {
            if (snapshot == null || procIndex < 0 || procIndex >= statusCellCache.Count)
            {
                return;
            }
            int groupIndex = statusGroupCount <= 0 ? 0 : procIndex % statusGroupCount;
            int rowIndex = statusGroupCount <= 0 ? 0 : procIndex / statusGroupCount;
            int baseColumn = groupIndex * StatusColumnsPerGroup;
            if (rowIndex < 0 || rowIndex >= dgvProcStatus.Rows.Count)
            {
                return;
            }
            if (baseColumn < 0 || baseColumn + StatusColumnsPerGroup - 1 >= dgvProcStatus.Columns.Count)
            {
                return;
            }
            DataGridViewRow row = dgvProcStatus.Rows[rowIndex];
            ProcStatusCellCache cache = statusCellCache[procIndex];

            string procName = GetProcDisplayName(snapshot.ProcIndex, snapshot.ProcName);
            string stateText = GetStateText(snapshot.State);
            string positionText = GetPositionText(snapshot);
            string opName = GetOpName(snapshot.ProcIndex, snapshot.StepIndex, snapshot.OpIndex);
            bool performanceAbnormal = snapshot.Performance?.AbnormalCpuLoopDetected == true;
            if (snapshot.Performance?.Enabled == true)
            {
                opName = $"{opName} | CPU {snapshot.Performance.ThreadCpuPercent:F1}% | {snapshot.Performance.OperationsPerSecond:F0} 指令/s";
                if (performanceAbnormal)
                {
                    opName += " | 性能异常：持续占满单核";
                }
            }
            Color stateColor = GetStateColor(snapshot.State);
            Color stateBackColor = GetStateBackColor(snapshot.State);

            if (!string.Equals(cache.ProcName, procName, StringComparison.Ordinal))
            {
                row.Cells[baseColumn + 0].Value = procName;
                cache.ProcName = procName;
            }
            if (!string.Equals(cache.StateText, stateText, StringComparison.Ordinal))
            {
                row.Cells[baseColumn + 1].Value = stateText;
                row.Cells[baseColumn + 1].ToolTipText = stateText;
                cache.StateText = stateText;
            }
            if (!string.Equals(cache.PositionText, positionText, StringComparison.Ordinal))
            {
                row.Cells[baseColumn + 2].Value = positionText;
                cache.PositionText = positionText;
            }
            if (!string.Equals(cache.OpName, opName, StringComparison.Ordinal))
            {
                row.Cells[baseColumn + 3].Value = opName;
                row.Cells[baseColumn + 3].ToolTipText = opName;
                cache.OpName = opName;
            }
            if (cache.PerformanceAbnormal != performanceAbnormal)
            {
                row.Cells[baseColumn + 3].Style.ForeColor = performanceAbnormal
                    ? UiPalette.Danger
                    : Color.Empty;
                cache.PerformanceAbnormal = performanceAbnormal;
            }
            if (cache.StateColor != stateColor)
            {
                row.Cells[baseColumn + 1].Style.ForeColor = stateColor;
                row.Cells[baseColumn + 1].Style.SelectionForeColor = stateColor;
                cache.StateColor = stateColor;
            }
            if (cache.StateBackColor != stateBackColor)
            {
                row.Cells[baseColumn + 1].Style.BackColor = stateBackColor;
                row.Cells[baseColumn + 1].Style.SelectionBackColor = stateBackColor;
                cache.StateBackColor = stateBackColor;
            }

            cache.ProcIndex = snapshot.ProcIndex;
            cache.StepIndex = snapshot.StepIndex;
            cache.OpIndex = snapshot.OpIndex;
        }

        private string GetProcDisplayName(int procIndex, string snapshotName)
        {
            string procName = snapshotName;
            if (string.IsNullOrWhiteSpace(procName) && Workspace.Proc?.procsList != null && procIndex >= 0
                && procIndex < Workspace.Proc.procsList.Count)
            {
                procName = Workspace.Proc.procsList[procIndex]?.head?.Name;
            }
            if (string.IsNullOrWhiteSpace(procName))
            {
                procName = $"索引{procIndex}";
            }
            return procName;
        }

        private string GetStateText(ProcRunState state)
        {
            switch (state)
            {
                case ProcRunState.Stopped:
                    return "停止";
                case ProcRunState.Paused:
                    return "暂停";
                case ProcRunState.SingleStep:
                    return "单步";
                case ProcRunState.Running:
                    return "运行";
                case ProcRunState.Alarming:
                    return "报警中";
                case ProcRunState.Pausing:
                    return "暂停中";
                case ProcRunState.Stopping:
                    return "停止中";
                default:
                    return "未知";
            }
        }

        private Color GetStateColor(ProcRunState state)
        {
            switch (state)
            {
                case ProcRunState.Running:
                    return UiPalette.Success;
                case ProcRunState.Paused:
                case ProcRunState.Pausing:
                case ProcRunState.SingleStep:
                    return UiPalette.Warning;
                case ProcRunState.Alarming:
                    return UiPalette.Danger;
                case ProcRunState.Stopping:
                    return UiPalette.Danger;
                case ProcRunState.Stopped:
                    return UiPalette.TextMuted;
                default:
                    return UiPalette.TextMuted;
            }
        }

        private Color GetStateBackColor(ProcRunState state)
        {
            switch (state)
            {
                case ProcRunState.Running:
                    return UiPalette.SuccessSoft;
                case ProcRunState.Paused:
                case ProcRunState.Pausing:
                case ProcRunState.SingleStep:
                    return UiPalette.WarningSoft;
                case ProcRunState.Alarming:
                    return UiPalette.DangerSoft;
                case ProcRunState.Stopping:
                    return UiPalette.DangerSoft;
                case ProcRunState.Stopped:
                    return UiPalette.DisabledSoft;
                default:
                    return UiPalette.DisabledSoft;
            }
        }

        private string GetPositionText(EngineSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return "-";
            }
            if (snapshot.StepIndex < 0 || snapshot.OpIndex < 0)
            {
                return "-";
            }
            return $"{snapshot.ProcIndex}-{snapshot.StepIndex}-{snapshot.OpIndex}";
        }

        private string GetOpName(int procIndex, int stepIndex, int opIndex)
        {
            if (procIndex < 0 || stepIndex < 0 || opIndex < 0)
            {
                return "-";
            }
            if (Workspace.Proc?.procsList == null || procIndex >= Workspace.Proc.procsList.Count)
            {
                return "-";
            }
            Proc proc = Workspace.Proc.procsList[procIndex];
            if (proc?.steps == null || stepIndex >= proc.steps.Count)
            {
                return "-";
            }
            Step step = proc.steps[stepIndex];
            if (step?.Ops == null || opIndex >= step.Ops.Count)
            {
                return "-";
            }
            OperationType op = step.Ops[opIndex];
            if (op == null)
            {
                return "-";
            }
            if (!string.IsNullOrWhiteSpace(op.Name))
            {
                return op.Name;
            }
            if (!string.IsNullOrWhiteSpace(op.OperaType))
            {
                return op.OperaType;
            }
            return "未命名";
        }

        private void dgvProcStatus_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                return;
            }
            StatusColumnTag tag = dgvProcStatus.Columns[e.ColumnIndex].Tag as StatusColumnTag;
            if (tag == null || tag.Kind != StatusColumnKind.Position)
            {
                return;
            }
            if (statusGroupCount <= 0)
            {
                return;
            }
            int procIndex = e.RowIndex * statusGroupCount + tag.GroupIndex;
            if (procIndex < 0 || procIndex >= statusCellCache.Count)
            {
                PrintInfo("当前位置数据无效，无法跳转。", Level.Error);
                return;
            }
            ProcStatusCellCache cache = statusCellCache[procIndex];
            JumpToOperation(cache.ProcIndex, cache.StepIndex, cache.OpIndex);
        }

        private void JumpToOperation(int procIndex, int stepIndex, int opIndex)
        {
            if (procIndex < 0 || stepIndex < 0 || opIndex < 0)
            {
                PrintInfo("当前位置无效，无法跳转。", Level.Error);
                return;
            }
            if (Workspace.Runtime.Editor.ActiveSession != null)
            {
                PrintInfo("当前处于编辑状态，禁止跳转。", Level.Error);
                return;
            }
            if (Workspace.Menu == null || Workspace.Proc == null || Workspace.DataGrid == null || Workspace.Inspector == null)
            {
                PrintInfo("流程界面未就绪，无法跳转。", Level.Error);
                return;
            }
            if (Workspace.Proc.procsList == null || procIndex >= Workspace.Proc.procsList.Count)
            {
                PrintInfo("流程索引超出范围，无法跳转。", Level.Error);
                return;
            }
            Proc proc = Workspace.Proc.procsList[procIndex];
            if (proc?.steps == null || stepIndex >= proc.steps.Count)
            {
                PrintInfo("步骤索引超出范围，无法跳转。", Level.Error);
                return;
            }
            Step step = proc.steps[stepIndex];
            if (step?.Ops == null || opIndex >= step.Ops.Count)
            {
                PrintInfo("指令索引超出范围，无法跳转。", Level.Error);
                return;
            }

            TreeView tree = Workspace.Proc.proc_treeView;
            if (tree == null || procIndex >= tree.Nodes.Count || stepIndex >= tree.Nodes[procIndex].Nodes.Count)
            {
                PrintInfo("流程树未就绪，无法跳转。", Level.Error);
                return;
            }
            tree.SelectedNode = tree.Nodes[procIndex].Nodes[stepIndex];
            if (Workspace.Proc.SelectedProcNum != procIndex || Workspace.Proc.SelectedStepNum != stepIndex)
            {
                PrintInfo("流程选择被阻止，无法跳转。", Level.Error);
                return;
            }

            if (!TrySelectOperationInGrid(opIndex))
            {
                PrintInfo("指令行未就绪，无法跳转。", Level.Error);
            }
        }

        private bool TrySelectOperationInGrid(int opIndex)
        {
            InstructionListView grid = Workspace.DataGrid.dataGridView1;
            if (grid == null || opIndex < 0 || opIndex >= grid.OperationCount)
            {
                return false;
            }
            grid.SelectSingle(opIndex);
            Workspace.DataGrid.iSelectedRow = opIndex;
            Workspace.DataGrid.ScrollRowToCenter(opIndex);

            if (Workspace.Proc?.procsList == null)
            {
                return true;
            }
            int procIndex = Workspace.Proc.SelectedProcNum;
            int stepIndex = Workspace.Proc.SelectedStepNum;
            if (procIndex < 0 || stepIndex < 0 || procIndex >= Workspace.Proc.procsList.Count)
            {
                return true;
            }
            Proc proc = Workspace.Proc.procsList[procIndex];
            if (proc?.steps == null || stepIndex >= proc.steps.Count)
            {
                return true;
            }
            Step step = proc.steps[stepIndex];
            if (step?.Ops == null || opIndex >= step.Ops.Count)
            {
                return true;
            }
            OperationType op = step.Ops[opIndex];
            if (op == null)
            {
                return true;
            }
            Workspace.DataGrid.OperationTemp = (OperationType)op.Clone();
            EditorServiceRegistry.AttachGraph(
                Workspace.DataGrid.OperationTemp, Workspace.Runtime);
            Workspace.DataGrid.OperationTemp.RefreshInspector?.Invoke();
            Workspace.Inspector.ShowObject(Workspace.DataGrid.OperationTemp);
            return true;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SCROLLINFO
        {
            public uint cbSize;
            public uint fMask;
            public int nMin;
            public int nMax;
            public uint nPage;
            public int nPos;
            public int nTrackPos;
        }

        [DllImport("user32.dll")]
        private static extern bool GetScrollInfo(IntPtr hWnd, int nBar, ref SCROLLINFO lpsi);

        private sealed class InfoLogEntry
        {
            public string TimeText { get; set; }
            public string Message { get; set; }
            public Level Level { get; set; }
        }

        public sealed class InfoLogSnapshot
        {
            public string TimeText { get; set; }

            public string Message { get; set; }

            public Level Level { get; set; }
        }

        private sealed class FixedRingBuffer<T>
        {
            private readonly T[] items;
            private int start;
            private int count;

            public FixedRingBuffer(int capacity)
            {
                if (capacity <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(capacity));
                }
                items = new T[capacity];
            }

            public int Count => count;

            public void Add(T item)
            {
                if (count < items.Length)
                {
                    items[(start + count) % items.Length] = item;
                    count++;
                    return;
                }
                items[start] = item;
                start = (start + 1) % items.Length;
            }

            public void Clear()
            {
                start = 0;
                count = 0;
            }

            public T this[int index]
            {
                get
                {
                    if (index < 0 || index >= count)
                    {
                        throw new ArgumentOutOfRangeException(nameof(index));
                    }
                    return items[(start + index) % items.Length];
                }
            }
        }

        private sealed class ProcStatusCellCache
        {
            public int ProcIndex = -1;
            public int StepIndex = -1;
            public int OpIndex = -1;
            public string ProcName;
            public string StateText;
            public string PositionText;
            public string OpName;
            public Color StateColor = Color.Empty;
            public Color StateBackColor = Color.Empty;
            public bool PerformanceAbnormal;
        }

        private enum StatusColumnKind
        {
            Proc = 0,
            State = 1,
            Position = 2,
            OpName = 3
        }

        private sealed class StatusColumnTag
        {
            public StatusColumnTag(int groupIndex, StatusColumnKind kind)
            {
                GroupIndex = groupIndex;
                Kind = kind;
            }

            public int GroupIndex { get; }
            public StatusColumnKind Kind { get; }
        }
    }
}
