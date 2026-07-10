using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static Automation.FrmCard;
using static Automation.OperationTypePartial;

namespace Automation
{
    public partial class FrmIO : Form
    {
        public List<List<IO>> IOMap = new List<List<IO>>();
        public Dictionary<string,IO> DicIO = new Dictionary<string,IO>();
        public System.Drawing.Image validImage = Properties.Resources.vaild;
        public System.Drawing.Image invalidImage = Properties.Resources.invalid;
        public int iSelectedIORow = -1;
        public List<string> IoOutItems = new List<string>();
        public List<string> IoInItems = new List<string>();
        public List<string> IoItems = new List<string>();
        private const int IoMonitorIntervalMs = 200;
        private const int IoMonitorStaleTimeoutMs = 2000;
        private readonly object ioMonitorLifecycleLock = new object();
        private System.Windows.Forms.Timer ioMonitorUiTimer;
        private CancellationTokenSource ioMonitorCts;
        private Task ioMonitorTask;
        private volatile bool ioMonitorEnabled;
        private IoMonitorRequest ioMonitorRequest;
        private IoMonitorSnapshot pendingIoMonitorSnapshot;
        private int ioMonitorApplyScheduled;
        private long lastIoMonitorErrorUtcTicks;
        private long lastIoMonitorSnapshotUtcTicks;
        private bool ioMonitorStaleShown;

        private sealed class IoMonitorItem
        {
            public int RowIndex { get; set; }
            public IO Source { get; set; }
            public IO HardwareRequest { get; set; }
            public bool IsInput { get; set; }
        }

        private sealed class IoMonitorRequest
        {
            public int CardIndex { get; set; }
            public IoMonitorItem[] Items { get; set; }
        }

        private struct IoMonitorValue
        {
            public bool Success { get; set; }
            public bool Value { get; set; }
        }

        private sealed class IoMonitorSnapshot
        {
            public IoMonitorRequest Request { get; set; }
            public IoMonitorValue[] Values { get; set; }
        }
        public FrmIO()
        {
            InitializeComponent();

            dgvIO.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgvIO.ReadOnly = true;
            dgvIO.Columns[0].SortMode = DataGridViewColumnSortMode.NotSortable;
            dgvIO.RowHeadersVisible = false;
            dgvIO.AutoGenerateColumns = false;
            dgvIO.RowsDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;

            Type dgvType = this.dgvIO.GetType();
            PropertyInfo pi = dgvType.GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic);
            pi.SetValue(this.dgvIO, true, null);

            dgvIO.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            dgvIO.KeyDown += dgvIO_KeyDown;
            FormClosing += FrmIO_FormClosing;
            Disposed += FrmIO_Disposed;
        }

        public bool IsIOMonitoring => ioMonitorEnabled;
        public void RefleshIODic()
        {
            var dictionary = new Dictionary<string, IO>(StringComparer.Ordinal);
            var outputNames = new List<string>();
            var inputNames = new List<string>();
            var allNames = new List<string>();
            if (IOMap == null)
            {
                SF.SetSecurityLock("IO配置为空，禁止加载IO字典。");
                return;
            }
            foreach (List<IO> list in IOMap)
            {
                if (list == null)
                {
                    SF.SetSecurityLock("IO配置包含空卡列表，禁止加载IO字典。");
                    return;
                }
                foreach (IO item in list)
                {
                    if (item == null || string.IsNullOrWhiteSpace(item.Name))
                    {
                        continue;
                    }
                    if (dictionary.ContainsKey(item.Name))
                    {
                        SF.SetSecurityLock($"IO名称重复：{item.Name}");
                        return;
                    }
                    dictionary.Add(item.Name, item);
                    if (item.IOType == "通用输出")
                    {
                        outputNames.Add(item.Name);
                    }
                    if (item.IOType == "通用输入")
                    {
                        inputNames.Add(item.Name);
                    }
                    allNames.Add(item.Name);
                }
            }
            DicIO.Clear();
            foreach (KeyValuePair<string, IO> pair in dictionary)
            {
                DicIO.Add(pair.Key, pair.Value);
            }
            IoOutItems.Clear();
            IoOutItems.AddRange(outputNames);
            IoInItems.Clear();
            IoInItems.AddRange(inputNames);
            IoItems.Clear();
            IoItems.AddRange(allNames);
        }
        //从文件更新表
        public void RefreshIOMap()
        {

            if (!Directory.Exists(SF.ConfigPath))
            {
                Directory.CreateDirectory(SF.ConfigPath);
            }
            if (!File.Exists(SF.ConfigPath + "IOMap.json"))
            {
                IOMap = new List<List<IO>>();
                SF.mainfrm.SaveAsJson(SF.ConfigPath, "IOMap", IOMap);
            }
            List<List<IO>> loadedMap = SF.mainfrm.ReadJson<List<List<IO>>>(SF.ConfigPath, "IOMap");
            if (loadedMap == null)
            {
                IOMap = new List<List<IO>>();
                SF.SetSecurityLock("IO配置文件为空或格式无效。");
                RefreshIODgv();
                return;
            }
            IOMap = loadedMap;
            RefreshIODgv();
        }

        public void RefreshIODgv()
        {
            Interlocked.Exchange(ref ioMonitorRequest, null);
            if (IsDisposed || Disposing)
            {
                return;
            }
            RefleshIODic();
            if (SF.frmCard.TryGetSelectedCardIndex(out int cardIndex))
            {
                if (SF.cardStore.TryGetCardHead(cardIndex, out CardHead cardHead) && IOMap.Count > cardIndex)
                {
                    int inputCount = cardHead.InputCount;
                    int outputCount = cardHead.OutputCount;

                    List<IO> cacheIOs = IOMap[cardIndex];

                    WriteIODgv(inputCount, outputCount, cacheIOs);
                    RefreshIoMonitorRequest();
                    return;
                }
            }
            if (SF.isModify != ModifyKind.IO)
            {
                dgvIO.Rows.Clear();
            }
        }

        public void WriteIODgv(int inputCount, int outputCount, List<IO> cacheIOs)
        {
            if (cacheIOs == null || inputCount < 0 || outputCount < 0)
            {
                return;
            }
            int totalCount;
            try
            {
                totalCount = checked(inputCount + outputCount);
            }
            catch (OverflowException)
            {
                SF.SetSecurityLock("IO数量配置溢出，禁止加载IO界面。");
                return;
            }
            if (totalCount != cacheIOs.Count)
            {
                SF.SetSecurityLock($"IO配置数量不一致：需要{totalCount}，实际{cacheIOs.Count}。");
                return;
            }

            dgvIO.SuspendLayout();
            try
            {
                if (SF.isModify != ModifyKind.IO)
                {
                    dgvIO.Rows.Clear();
                    if (totalCount > 0)
                    {
                        dgvIO.Rows.Add(totalCount);
                    }
                }
                if (dgvIO.Rows.Count < totalCount)
                {
                    SF.SetSecurityLock("IO界面行数与配置数量不一致，禁止继续刷新。");
                    return;
                }
                for (int i = 0; i < totalCount; i++)
                {
                    IO cacheIO = cacheIOs[i];
                    DataGridViewRow row = dgvIO.Rows[i];
                    if (cacheIO == null)
                    {
                        for (int columnIndex = 0; columnIndex < row.Cells.Count; columnIndex++)
                        {
                            row.Cells[columnIndex].Value = null;
                        }
                        row.Cells[1].ToolTipText = "IO配置为空";
                        continue;
                    }
                    row.Cells[0].Value = cacheIO.Index;
                    row.Cells[1].Value = cacheIO.Status ? validImage : invalidImage;
                    row.Cells[1].ToolTipText = string.Empty;
                    row.Cells[2].Value = cacheIO.Name;
                    row.Cells[3].Value = cacheIO.CardNum;
                    row.Cells[4].Value = cacheIO.Module;
                    row.Cells[5].Value = cacheIO.IOIndex;
                    row.Cells[6].Value = cacheIO.IOType;
                    row.Cells[7].Value = cacheIO.UsedType;
                    row.Cells[8].Value = cacheIO.EffectLevel;
                    row.Cells[9].Value = cacheIO.Note;
                }
            }
            finally
            {
                dgvIO.ResumeLayout();
            }
        }
        //刷新IO界面
        public void FreshFrmIO()
        {
            RefreshIODgv();
        }

        private void dgvIO_CellMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            

        }

        private void dgvIO_CellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.RowIndex >= 0)
            {
                if (!SF.frmCard.TryGetSelectedCardIndex(out int cardIndex)
                    || IOMap == null || cardIndex < 0 || cardIndex >= IOMap.Count
                    || IOMap[cardIndex] == null || e.RowIndex >= IOMap[cardIndex].Count)
                {
                    iSelectedIORow = -1;
                    return;
                }
                dgvIO.ClearSelection();
                dgvIO.Rows[e.RowIndex].Selected = true;
                SF.frmPropertyGrid.propertyGrid1.SelectedObject = IOMap[cardIndex][e.RowIndex];
            }

            iSelectedIORow = e.RowIndex;
        }

        private void Modify_Click(object sender, EventArgs e)
        {
            if (iSelectedIORow != -1)
            {
                SF.BeginEdit(ModifyKind.IO);
                RefreshIoMonitorRequest();
            }
          
        }

        private void dgvIO_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.V)
            {
                PasteNames_Click(sender, EventArgs.Empty);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private void PasteNames_Click(object sender, EventArgs e)
        {
            if (!Clipboard.ContainsText())
            {
                MessageBox.Show("剪贴板没有可用文本。");
                return;
            }
            List<string> names = ParseClipboardNames(Clipboard.GetText());
            if (names.Count == 0)
            {
                MessageBox.Show("剪贴板内容为空。");
                return;
            }
            if (iSelectedIORow < 0)
            {
                MessageBox.Show("请先选择起始行。");
                return;
            }
            if (!SF.frmCard.TryGetSelectedCardIndex(out int cardIndex))
            {
                MessageBox.Show("请先选择控制卡。");
                return;
            }
            if (IOMap == null || cardIndex < 0 || cardIndex >= IOMap.Count || IOMap[cardIndex] == null)
            {
                MessageBox.Show("IO列表为空。");
                return;
            }

            List<IO> cacheIOs = IOMap[cardIndex];
            if (iSelectedIORow >= cacheIOs.Count)
            {
                MessageBox.Show("起始行超出范围。");
                return;
            }

            int maxPasteCount = Math.Min(names.Count, cacheIOs.Count - iSelectedIORow);
            if (maxPasteCount <= 0)
            {
                return;
            }

            HashSet<string> existingNames = new HashSet<string>();
            foreach (List<IO> list in IOMap)
            {
                if (list == null)
                {
                    continue;
                }
                foreach (IO io in list)
                {
                    if (io == null || string.IsNullOrWhiteSpace(io.Name))
                    {
                        continue;
                    }
                    existingNames.Add(io.Name);
                }
            }
            for (int i = 0; i < maxPasteCount; i++)
            {
                IO oldIo = cacheIOs[iSelectedIORow + i];
                if (oldIo != null && !string.IsNullOrWhiteSpace(oldIo.Name))
                {
                    existingNames.Remove(oldIo.Name);
                }
            }

            HashSet<string> newNames = new HashSet<string>();
            for (int i = 0; i < maxPasteCount; i++)
            {
                string name = names[i];
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }
                if (!newNames.Add(name) || existingNames.Contains(name))
                {
                    MessageBox.Show($"粘贴失败：名称重复（{name}），请先修改名称。");
                    return;
                }
            }

            for (int i = 0; i < maxPasteCount; i++)
            {
                IO io = cacheIOs[iSelectedIORow + i];
                if (io != null)
                {
                    string name = names[i];
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        io.Name = "";
                        io.Module = 0;
                        io.UsedType = "通用";
                        io.EffectLevel = "正常";
                        io.Note = "";
                        io.IsRemark = false;
                    }
                    else
                    {
                        io.Name = name;
                    }
                }
            }

            SF.mainfrm.SaveAsJson(SF.ConfigPath, "IOMap", IOMap);
            RefreshIODgv();
        }

        private void ClearSelected_Click(object sender, EventArgs e)
        {
            if (!SF.frmCard.TryGetSelectedCardIndex(out int cardIndex))
            {
                MessageBox.Show("请先选择控制卡。");
                return;
            }
            if (IOMap == null || cardIndex < 0 || cardIndex >= IOMap.Count || IOMap[cardIndex] == null)
            {
                MessageBox.Show("IO列表为空。");
                return;
            }

            List<int> rowIndexes = new List<int>();
            foreach (DataGridViewRow row in dgvIO.SelectedRows)
            {
                if (row.Index >= 0)
                {
                    rowIndexes.Add(row.Index);
                }
            }
            if (rowIndexes.Count == 0 && iSelectedIORow >= 0)
            {
                rowIndexes.Add(iSelectedIORow);
            }
            if (rowIndexes.Count == 0)
            {
                return;
            }
            if (MessageBox.Show("确认清除选中的IO配置？", "清除确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }

            List<IO> cacheIOs = IOMap[cardIndex];
            foreach (int index in rowIndexes)
            {
                if (index < 0 || index >= cacheIOs.Count)
                {
                    continue;
                }
                IO io = cacheIOs[index];
                if (io == null)
                {
                    continue;
                }
                io.Name = "";
                io.Module = 0;
                io.UsedType = "通用";
                io.EffectLevel = "正常";
                io.Note = "";
                io.IsRemark = false;
            }

            SF.mainfrm.SaveAsJson(SF.ConfigPath, "IOMap", IOMap);
            RefreshIODgv();
        }

        private void InvertInput_Click(object sender, EventArgs e)
        {
            SetIoEffectLevel("通用输入", "取反");
        }

        private void InvertOutput_Click(object sender, EventArgs e)
        {
            SetIoEffectLevel("通用输出", "取反");
        }

        private void SetIoEffectLevel(string ioType, string effectLevel)
        {
            if (!SF.frmCard.TryGetSelectedCardIndex(out int cardIndex))
            {
                MessageBox.Show("请先选择控制卡。");
                return;
            }
            if (IOMap == null || cardIndex < 0 || cardIndex >= IOMap.Count || IOMap[cardIndex] == null)
            {
                MessageBox.Show("IO列表为空。");
                return;
            }

            bool hasMatched = false;
            List<IO> cacheIOs = IOMap[cardIndex];
            for (int i = 0; i < cacheIOs.Count; i++)
            {
                IO io = cacheIOs[i];
                if (io == null || io.IOType != ioType)
                {
                    continue;
                }
                io.EffectLevel = effectLevel;
                hasMatched = true;
            }
            if (!hasMatched)
            {
                MessageBox.Show("未找到对应类型IO。");
                return;
            }

            SF.mainfrm.SaveAsJson(SF.ConfigPath, "IOMap", IOMap);
            RefreshIODgv();
        }

        public bool ToggleIOMonitor()
        {
            if (ioMonitorEnabled)
            {
                StopIOMonitor();
                return false;
            }
            return StartIOMonitor();
        }

        public void StopIOMonitor()
        {
            ioMonitorEnabled = false;
            Interlocked.Exchange(ref ioMonitorRequest, null);
            Interlocked.Exchange(ref pendingIoMonitorSnapshot, null);
            ioMonitorUiTimer?.Stop();
        }

        private bool StartIOMonitor()
        {
            ioMonitorEnabled = true;
            RefreshIoMonitorRequest();
            IoMonitorRequest request = Volatile.Read(ref ioMonitorRequest);
            if (request == null || request.Items == null || request.Items.Length == 0)
            {
                ioMonitorEnabled = false;
                MessageBox.Show("当前没有可监控的IO，或IO运行时尚未就绪。", "IO监控",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
            lastIoMonitorSnapshotUtcTicks = DateTime.UtcNow.Ticks;
            ioMonitorStaleShown = false;
            if (ioMonitorUiTimer == null)
            {
                ioMonitorUiTimer = new System.Windows.Forms.Timer { Interval = 500 };
                ioMonitorUiTimer.Tick += IoMonitorUiTimer_Tick;
            }
            ioMonitorUiTimer.Start();
            EnsureIoMonitorWorkerStarted();
            return true;
        }

        private void EnsureIoMonitorWorkerStarted()
        {
            lock (ioMonitorLifecycleLock)
            {
                if (ioMonitorTask != null && !ioMonitorTask.IsCompleted)
                {
                    return;
                }
                ioMonitorCts?.Dispose();
                ioMonitorCts = new CancellationTokenSource();
                CancellationToken token = ioMonitorCts.Token;
                ioMonitorTask = Task.Run(() => IoMonitorLoopAsync(token), token);
            }
        }

        private void RefreshIoMonitorRequest()
        {
            if (!ioMonitorEnabled || IsDisposed || Disposing || !IsHandleCreated
                || SF.io == null
                || !SF.frmCard.TryGetSelectedCardIndex(out int cardIndex)
                || IOMap == null || cardIndex < 0 || cardIndex >= IOMap.Count
                || IOMap[cardIndex] == null)
            {
                Interlocked.Exchange(ref ioMonitorRequest, null);
                return;
            }

            List<IO> cacheIOs = IOMap[cardIndex];
            int rowCount = Math.Min(dgvIO.Rows.Count, cacheIOs.Count);
            var items = new List<IoMonitorItem>(rowCount);
            for (int i = 0; i < rowCount; i++)
            {
                IO io = cacheIOs[i];
                if (io == null)
                {
                    continue;
                }
                bool isInput = string.Equals(io.IOType, "通用输入", StringComparison.Ordinal);
                bool isOutput = string.Equals(io.IOType, "通用输出", StringComparison.Ordinal);
                if (!isInput && !isOutput)
                {
                    continue;
                }
                items.Add(new IoMonitorItem
                {
                    RowIndex = i,
                    Source = io,
                    IsInput = isInput,
                    HardwareRequest = new IO
                    {
                        Index = io.Index,
                        CardNum = io.CardNum,
                        Module = io.Module,
                        IOIndex = io.IOIndex,
                        IOType = io.IOType
                    }
                });
            }
            Interlocked.Exchange(ref ioMonitorRequest, new IoMonitorRequest
            {
                CardIndex = cardIndex,
                Items = items.ToArray()
            });
        }

        private async Task IoMonitorLoopAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    IoMonitorRequest request = Volatile.Read(ref ioMonitorRequest);
                    if (!ioMonitorEnabled || request == null || request.Items == null || request.Items.Length == 0)
                    {
                        await Task.Delay(50, token).ConfigureAwait(false);
                        continue;
                    }

                    IoMonitorSnapshot snapshot = CollectIoMonitorSnapshot(request, token);
                    if (ioMonitorEnabled && ReferenceEquals(request, Volatile.Read(ref ioMonitorRequest)))
                    {
                        PostIoMonitorSnapshot(snapshot);
                    }
                    await Task.Delay(IoMonitorIntervalMs, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                ReportIoMonitorError($"IO监控后台任务异常：{ex.Message}");
            }
        }

        private IoMonitorSnapshot CollectIoMonitorSnapshot(IoMonitorRequest request, CancellationToken token)
        {
            var values = new IoMonitorValue[request.Items.Length];
            var ioRuntime = SF.io;
            for (int i = 0; i < request.Items.Length; i++)
            {
                if (token.IsCancellationRequested || !ioMonitorEnabled
                    || !ReferenceEquals(request, Volatile.Read(ref ioMonitorRequest)))
                {
                    break;
                }
                IoMonitorItem item = request.Items[i];
                bool value = false;
                bool success = false;
                try
                {
                    if (ioRuntime != null)
                    {
                        success = item.IsInput
                            ? ioRuntime.GetInIO(item.HardwareRequest, ref value)
                            : ioRuntime.GetOutIO(item.HardwareRequest, ref value);
                    }
                }
                catch (Exception ex)
                {
                    ReportIoMonitorError($"IO读取异常：{item.HardwareRequest.CardNum}-{item.HardwareRequest.IOIndex} {ex.Message}");
                }
                if (!success)
                {
                    ReportIoMonitorError($"IO状态读取失败：{item.HardwareRequest.CardNum}-{item.HardwareRequest.IOIndex}");
                }
                values[i] = new IoMonitorValue
                {
                    Success = success,
                    Value = value
                };
            }
            return new IoMonitorSnapshot
            {
                Request = request,
                Values = values
            };
        }

        private void PostIoMonitorSnapshot(IoMonitorSnapshot snapshot)
        {
            if (IsDisposed || Disposing || !IsHandleCreated)
            {
                return;
            }
            Interlocked.Exchange(ref pendingIoMonitorSnapshot, snapshot);
            if (Interlocked.Exchange(ref ioMonitorApplyScheduled, 1) != 0)
            {
                return;
            }
            try
            {
                BeginInvoke((Action)ApplyPendingIoMonitorSnapshot);
            }
            catch (InvalidOperationException)
            {
                Interlocked.Exchange(ref ioMonitorApplyScheduled, 0);
            }
        }

        private void ApplyPendingIoMonitorSnapshot()
        {
            IoMonitorSnapshot snapshot = Interlocked.Exchange(ref pendingIoMonitorSnapshot, null);
            if (snapshot != null)
            {
                ApplyIoMonitorSnapshot(snapshot);
            }
            Interlocked.Exchange(ref ioMonitorApplyScheduled, 0);
            if (Volatile.Read(ref pendingIoMonitorSnapshot) != null)
            {
                PostIoMonitorSnapshot(Interlocked.Exchange(ref pendingIoMonitorSnapshot, null));
            }
        }

        private void ApplyIoMonitorSnapshot(IoMonitorSnapshot snapshot)
        {
            if (!ioMonitorEnabled || snapshot == null || snapshot.Request == null
                || !ReferenceEquals(snapshot.Request, Volatile.Read(ref ioMonitorRequest))
                || snapshot.Values == null
                || !SF.frmCard.TryGetSelectedCardIndex(out int selectedCardIndex)
                || selectedCardIndex != snapshot.Request.CardIndex)
            {
                return;
            }

            IoMonitorItem[] items = snapshot.Request.Items;
            int count = Math.Min(items.Length, snapshot.Values.Length);
            lastIoMonitorSnapshotUtcTicks = DateTime.UtcNow.Ticks;
            ioMonitorStaleShown = false;
            for (int i = 0; i < count; i++)
            {
                IoMonitorItem item = items[i];
                if (item.RowIndex < 0 || item.RowIndex >= dgvIO.Rows.Count)
                {
                    continue;
                }
                IoMonitorValue monitorValue = snapshot.Values[i];
                DataGridViewCell cell = dgvIO.Rows[item.RowIndex].Cells[1];
                if (!monitorValue.Success)
                {
                    if (cell.Value != null)
                    {
                        cell.Value = null;
                    }
                    if (!string.Equals(cell.ToolTipText, "IO状态读取失败", StringComparison.Ordinal))
                    {
                        cell.ToolTipText = "IO状态读取失败";
                    }
                    continue;
                }
                item.Source.Status = monitorValue.Value;
                System.Drawing.Image nextImage = monitorValue.Value ? validImage : invalidImage;
                if (!ReferenceEquals(cell.Value, nextImage))
                {
                    cell.Value = nextImage;
                }
                if (!string.IsNullOrEmpty(cell.ToolTipText))
                {
                    cell.ToolTipText = string.Empty;
                }
            }
        }

        private void IoMonitorUiTimer_Tick(object sender, EventArgs e)
        {
            if (!ioMonitorEnabled || ioMonitorStaleShown)
            {
                return;
            }
            long elapsedTicks = DateTime.UtcNow.Ticks - lastIoMonitorSnapshotUtcTicks;
            if (elapsedTicks < TimeSpan.FromMilliseconds(IoMonitorStaleTimeoutMs).Ticks)
            {
                return;
            }
            ioMonitorStaleShown = true;
            for (int i = 0; i < dgvIO.Rows.Count; i++)
            {
                DataGridViewCell cell = dgvIO.Rows[i].Cells[1];
                cell.Value = null;
                cell.ToolTipText = "IO监控数据已过期";
            }
            ReportIoMonitorError("IO监控超过2秒未收到新快照，界面状态已标记为过期。");
        }

        private void ReportIoMonitorError(string message)
        {
            long nowTicks = DateTime.UtcNow.Ticks;
            long lastTicks = Interlocked.Read(ref lastIoMonitorErrorUtcTicks);
            if (nowTicks - lastTicks < TimeSpan.FromSeconds(5).Ticks)
            {
                return;
            }
            if (Interlocked.CompareExchange(ref lastIoMonitorErrorUtcTicks, nowTicks, lastTicks) == lastTicks)
            {
                SF.frmInfo?.PrintInfo(message, FrmInfo.Level.Error);
            }
        }

        private void StopIoMonitorWorker()
        {
            CancellationTokenSource cts;
            Task task;
            lock (ioMonitorLifecycleLock)
            {
                cts = ioMonitorCts;
                task = ioMonitorTask;
                ioMonitorCts = null;
                ioMonitorTask = null;
            }
            if (cts == null)
            {
                return;
            }
            cts.Cancel();
            if (task == null)
            {
                cts.Dispose();
                return;
            }
            task.ContinueWith(completedTask =>
            {
                _ = completedTask.Exception;
                cts.Dispose();
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        private void FrmIO_FormClosing(object sender, FormClosingEventArgs e)
        {
            StopIOMonitor();
            StopIoMonitorWorker();
        }

        private void FrmIO_Disposed(object sender, EventArgs e)
        {
            StopIOMonitor();
            StopIoMonitorWorker();
            if (ioMonitorUiTimer != null)
            {
                ioMonitorUiTimer.Tick -= IoMonitorUiTimer_Tick;
                ioMonitorUiTimer.Dispose();
                ioMonitorUiTimer = null;
            }
        }

        private List<string> ParseClipboardNames(string text)
        {
            List<string> names = new List<string>();
            if (string.IsNullOrWhiteSpace(text))
            {
                return names;
            }
            string normalized = text.Replace("\r\n", "\n").Replace("\r", "\n");
            string[] lines = normalized.Split('\n');
            foreach (string raw in lines)
            {
                string line = raw ?? string.Empty;
                string name = line;
                int tabIndex = line.IndexOf('\t');
                if (tabIndex >= 0)
                {
                    name = line.Substring(0, tabIndex);
                }
                else
                {
                    int commaIndex = line.IndexOf(',');
                    if (commaIndex >= 0)
                    {
                        name = line.Substring(0, commaIndex);
                    }
                }
                name = name.Trim().Trim('"');
                names.Add(name);
            }
            if (!normalized.EndsWith("\n\n", StringComparison.Ordinal) && names.Count > 0 && string.IsNullOrEmpty(names[names.Count - 1]))
            {
                names.RemoveAt(names.Count - 1);
            }
            if (names.Count > 1)
            {
                string header = names[0];
                if (header == "IO名称" || header == "名称" || header.Equals("Name", StringComparison.OrdinalIgnoreCase))
                {
                    names.RemoveAt(0);
                }
            }
            return names;
        }

    }

    public class IO
    {
 
        [DisplayName("编号"), Category("常规"), Description(""), ReadOnly(true), Browsable(true)]
        public int Index { get; set; }
        [Browsable(false)]
        public bool Status { get; set; }
        [DisplayName("名称"), Category("设置"), Description(""), ReadOnly(false), Browsable(true)]
        public string Name { get; set; }
        [DisplayName("卡号"), Category("常规"), Description(""), ReadOnly(true), Browsable(true)]
        public int CardNum { get; set; }
        [DisplayName("模块(从站)号"), Category("设置"), Description(""), ReadOnly(false), Browsable(true)]
        public int  Module { get; set; }
        [DisplayName("IO编号"), Category("常规"), Description(""), ReadOnly(true), Browsable(true)]
        public string IOIndex { get; set; }
        [DisplayName("IO类型"), Category("常规"), Description(""), ReadOnly(true), Browsable(true)]
        public string IOType { get; set; }
        [DisplayName("使用类型"), Category("设置"), Description(""), ReadOnly(false), Browsable(true), TypeConverter(typeof(IOUsedItem))]
        public string UsedType { get; set; }
        [DisplayName("电平"), Category("设置"), Description(""), ReadOnly(false), Browsable(true), TypeConverter(typeof(IOLevelItem))]
        public string EffectLevel { get; set; }
        [DisplayName("备注"), Category("设置"), Description(""), ReadOnly(false), Browsable(true)]
        public string Note { get; set; }
        [Browsable(false)]
        public bool IsRemark { get; set; }
       
        public IO()
        {
            Name = "";
        }
        public IO CloneForDebug()
        {
            return new IO
            {
                Index = Index,
                Name = Name,
                CardNum = CardNum,
                Module = Module,
                IOIndex = IOIndex,
                IOType = IOType,
                UsedType = UsedType,
                EffectLevel = EffectLevel,
                Note = Note,
                IsRemark = IsRemark
            };
        }

    }
}
