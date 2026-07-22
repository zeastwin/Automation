// 模块：编辑器 / 通用 UI。
// 职责范围：编辑器共享的视觉、弹窗和 WinForms 交互基础设施。

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Automation
{
    /// <summary>
    /// 进程级 WinForms 交互协调器。不依赖 FrmMain；HMI 与平台编辑器共用同一实例。
    /// </summary>
    internal sealed class WinFormsProcessInteractionCoordinator :
        IProcessPopupService,
        IAlarmHandler,
        IDisposable
    {
        private sealed class PopupItem
        {
            public int ProcIndex { get; set; }
            public ProcessPopupRequest Request { get; set; }
            public CancellationToken CancellationToken { get; set; }
            public TaskCompletionSource<AlarmDecision> Completion { get; set; }
            public Message Dialog { get; set; }
        }

        private readonly PlatformRuntime runtime;
        private readonly int uiThreadId;
        private readonly Control dispatcher;
        private readonly object popupLock = new object();
        private readonly Queue<PopupItem> pending = new Queue<PopupItem>();
        private readonly Dictionary<int, List<PopupItem>> active =
            new Dictionary<int, List<PopupItem>>();
        private bool uiReady;
        private bool disposed;
        private ProcessEngine engine;
        private int popupAlarmCount;

        public WinFormsProcessInteractionCoordinator(PlatformRuntime runtime)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            uiThreadId = Thread.CurrentThread.ManagedThreadId;
            dispatcher = new Control();
            _ = dispatcher.Handle;
        }

        public int PopupAlarmCount => Math.Max(0, Volatile.Read(ref popupAlarmCount));

        public event Action PopupAlarmCountChanged;

        public void AttachEngine(ProcessEngine processEngine)
        {
            if (processEngine == null) throw new ArgumentNullException(nameof(processEngine));
            if (ReferenceEquals(engine, processEngine))
            {
                return;
            }
            if (engine != null)
            {
                engine.SnapshotChanged -= HandleSnapshotChanged;
            }
            engine = processEngine;
            engine.SnapshotChanged += HandleSnapshotChanged;
        }

        public Task<AlarmDecision> ShowAsync(
            ProcessPopupRequest request,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            ValidateRequest(request);
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<AlarmDecision>(cancellationToken);
            }

            var item = new PopupItem
            {
                ProcIndex = request.ProcIndex,
                Request = request,
                CancellationToken = cancellationToken,
                Completion = new TaskCompletionSource<AlarmDecision>(
                    TaskCreationOptions.RunContinuationsAsynchronously)
            };
            Post(() => EnqueueOnUiThread(item));
            return item.Completion.Task;
        }

        public Task<AlarmDecision> HandleAsync(AlarmContext context)
        {
            if (context == null)
            {
                return Task.FromResult(AlarmDecision.Stop);
            }
            int buttonCount;
            switch (context.AlarmType)
            {
                case "弹框确定":
                    buttonCount = 1;
                    break;
                case "弹框确定与否":
                    buttonCount = 2;
                    break;
                case "弹框确定与否与取消":
                    buttonCount = 3;
                    break;
                default:
                    return Task.FromException<AlarmDecision>(
                        new InvalidOperationException($"不支持的流程报警弹框类型:{context.AlarmType}"));
            }

            Interlocked.Increment(ref popupAlarmCount);
            PopupAlarmCountChanged?.Invoke();
            Task<AlarmDecision> task;
            try
            {
                task = ShowAsync(new ProcessPopupRequest
                {
                    ProcIndex = context.ProcIndex,
                    Title = $"发生报警:{context.ProcIndex}---{context.StepIndex}---{context.OpIndex}",
                    Message = context.Note,
                    Button1 = context.Btn1,
                    Button2 = context.Btn2,
                    Button3 = context.Btn3,
                    ButtonCount = buttonCount
                }, CancellationToken.None);
            }
            catch (Exception ex)
            {
                task = Task.FromException<AlarmDecision>(ex);
            }
            task.ContinueWith(_ =>
            {
                int count = Interlocked.Decrement(ref popupAlarmCount);
                if (count < 0)
                {
                    Interlocked.Exchange(ref popupAlarmCount, 0);
                }
                PopupAlarmCountChanged?.Invoke();
            }, TaskScheduler.Default);
            return task;
        }

        public void NotifyUiReady()
        {
            Post(() =>
            {
                uiReady = true;
                ShowPending();
            });
        }

        public void Post(Action action)
        {
            if (action == null || disposed)
            {
                return;
            }
            if (Thread.CurrentThread.ManagedThreadId == uiThreadId)
            {
                action();
                return;
            }
            if (dispatcher.IsDisposed || !dispatcher.IsHandleCreated)
            {
                return;
            }
            try
            {
                dispatcher.BeginInvoke(action);
            }
            catch (InvalidOperationException)
            {
            }
        }

        public void CloseProcess(int procIndex)
        {
            Post(() => CloseProcessOnUiThread(procIndex));
        }

        public void CloseAll()
        {
            Post(CloseAllOnUiThread);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            if (Thread.CurrentThread.ManagedThreadId != uiThreadId)
            {
                throw new InvalidOperationException("流程交互协调器必须在创建它的 UI 线程释放。");
            }
            disposed = true;
            if (engine != null)
            {
                engine.SnapshotChanged -= HandleSnapshotChanged;
                engine = null;
            }
            CloseAllOnUiThread();
            dispatcher.Dispose();
        }

        private void EnqueueOnUiThread(PopupItem item)
        {
            if (disposed || item.CancellationToken.IsCancellationRequested)
            {
                CompleteCanceled(item);
                return;
            }
            if (!uiReady)
            {
                lock (popupLock)
                {
                    pending.Enqueue(item);
                }
                return;
            }
            ShowPopup(item);
        }

        private void ShowPending()
        {
            List<PopupItem> items;
            lock (popupLock)
            {
                items = pending.ToList();
                pending.Clear();
            }
            foreach (PopupItem item in items)
            {
                if (item.CancellationToken.IsCancellationRequested)
                {
                    CompleteCanceled(item);
                }
                else
                {
                    ShowPopup(item);
                }
            }
        }

        private void ShowPopup(PopupItem item)
        {
            try
            {
                Message dialog = CreateDialog(item);
                item.Dialog = dialog;
                lock (popupLock)
                {
                    if (!active.TryGetValue(item.ProcIndex, out List<PopupItem> items))
                    {
                        items = new List<PopupItem>();
                        active[item.ProcIndex] = items;
                    }
                    items.Add(item);
                }
                CancellationTokenRegistration registration = default;
                if (item.CancellationToken.CanBeCanceled)
                {
                    registration = item.CancellationToken.Register(() => Post(() =>
                    {
                        CompleteCanceled(item);
                        CloseDialog(item);
                    }));
                }
                dialog.FormClosed += (sender, args) =>
                {
                    registration.Dispose();
                    Unregister(item);
                };
                dialog.PresentDeferred(false);
            }
            catch (Exception ex)
            {
                item.Completion.TrySetException(ex);
                CloseDialog(item);
            }
        }

        private Message CreateDialog(PopupItem item)
        {
            ProcessPopupRequest request = item.Request;
            Message dialog;
            switch (request.ButtonCount)
            {
                case 1:
                    dialog = new Message(runtime.Safety, runtime.EditorUi,
                        request.Title, request.Message,
                        () => item.Completion.TrySetResult(AlarmDecision.Goto1),
                        request.Button1, false, false);
                    break;
                case 2:
                    dialog = new Message(runtime.Safety, runtime.EditorUi,
                        request.Title, request.Message,
                        () => item.Completion.TrySetResult(AlarmDecision.Goto1),
                        () => item.Completion.TrySetResult(AlarmDecision.Goto2),
                        request.Button1, request.Button2, false, false);
                    break;
                default:
                    dialog = new Message(runtime.Safety, runtime.EditorUi,
                        request.Title, request.Message,
                        () => item.Completion.TrySetResult(AlarmDecision.Goto1),
                        () => item.Completion.TrySetResult(AlarmDecision.Goto2),
                        () => item.Completion.TrySetResult(AlarmDecision.Goto3),
                        request.Button1, request.Button2, request.Button3, false, false);
                    break;
            }
            if (request.AutoCloseMilliseconds.HasValue)
            {
                var timer = new System.Windows.Forms.Timer
                {
                    Interval = request.AutoCloseMilliseconds.Value
                };
                timer.Tick += (sender, args) =>
                {
                    timer.Stop();
                    timer.Dispose();
                    CloseDialog(item);
                    item.Completion.TrySetResult(AlarmDecision.Ignore);
                };
                dialog.FormClosed += (sender, args) =>
                {
                    timer.Stop();
                    timer.Dispose();
                };
                timer.Start();
            }
            return dialog;
        }

        private void HandleSnapshotChanged(EngineSnapshot snapshot)
        {
            if (snapshot != null && snapshot.State.IsInactive())
            {
                CloseProcess(snapshot.ProcIndex);
            }
        }

        private void CloseProcessOnUiThread(int procIndex)
        {
            List<PopupItem> activeItems;
            List<PopupItem> pendingItems = new List<PopupItem>();
            lock (popupLock)
            {
                activeItems = active.TryGetValue(procIndex, out List<PopupItem> items)
                    ? items.ToList()
                    : new List<PopupItem>();
                if (pending.Count > 0)
                {
                    var retained = new Queue<PopupItem>();
                    while (pending.Count > 0)
                    {
                        PopupItem item = pending.Dequeue();
                        if (item.ProcIndex == procIndex) pendingItems.Add(item);
                        else retained.Enqueue(item);
                    }
                    while (retained.Count > 0) pending.Enqueue(retained.Dequeue());
                }
            }
            foreach (PopupItem item in pendingItems)
            {
                CompleteCanceled(item);
            }
            foreach (PopupItem item in activeItems)
            {
                CompleteCanceled(item);
                CloseDialog(item);
            }
        }

        private void CloseAllOnUiThread()
        {
            List<PopupItem> items;
            lock (popupLock)
            {
                items = active.Values.SelectMany(value => value).Concat(pending).ToList();
                active.Clear();
                pending.Clear();
            }
            foreach (PopupItem item in items)
            {
                CompleteCanceled(item);
                CloseDialog(item);
            }
        }

        private void Unregister(PopupItem item)
        {
            lock (popupLock)
            {
                if (!active.TryGetValue(item.ProcIndex, out List<PopupItem> items))
                {
                    return;
                }
                items.Remove(item);
                if (items.Count == 0)
                {
                    active.Remove(item.ProcIndex);
                }
            }
        }

        private static void CloseDialog(PopupItem item)
        {
            Message dialog = item?.Dialog;
            if (dialog != null && !dialog.IsDisposed && !dialog.Disposing)
            {
                dialog.btnCanel();
            }
        }

        private static void CompleteCanceled(PopupItem item)
        {
            if (item.CancellationToken.IsCancellationRequested)
            {
                item.Completion.TrySetCanceled(item.CancellationToken);
            }
            else
            {
                item.Completion.TrySetCanceled();
            }
        }

        private static void ValidateRequest(ProcessPopupRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.ProcIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(request.ProcIndex));
            }
            if (request.ButtonCount < 1 || request.ButtonCount > 3)
            {
                throw new ArgumentOutOfRangeException(nameof(request.ButtonCount));
            }
            if (request.AutoCloseMilliseconds.HasValue && request.AutoCloseMilliseconds.Value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(request.AutoCloseMilliseconds));
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(WinFormsProcessInteractionCoordinator));
            }
        }
    }
}
