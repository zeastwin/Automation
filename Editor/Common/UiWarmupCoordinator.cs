// 模块：编辑器 / 公共基础设施。
// 职责范围：在 UI 空闲时分批启动可替换的缓存预热任务，避免与用户输入争抢消息循环。

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;

namespace Automation
{
    internal sealed class UiWarmupCoordinator : IDisposable
    {
        private sealed class WorkItem
        {
            public string Key { get; set; }
            public int Priority { get; set; }
            public long Sequence { get; set; }
            public Action Action { get; set; }
        }

        private const int TickIntervalMs = 16;
        private const int TickBudgetMs = 6;
        private const int MaxWorkItemsPerTick = 1;
        private const int InteractionQuietPeriodMs = 80;
        private readonly Control dispatcher;
        private readonly Timer timer;
        private readonly Dictionary<string, WorkItem> pending =
            new Dictionary<string, WorkItem>(StringComparer.Ordinal);
        private long sequence;
        private DateTime deferUntilUtc;
        private bool disposed;

        public UiWarmupCoordinator(Control dispatcher)
        {
            this.dispatcher = dispatcher
                ?? throw new ArgumentNullException(nameof(dispatcher));
            timer = new Timer
            {
                Interval = TickIntervalMs
            };
            timer.Tick += Timer_Tick;
        }

        public void Schedule(string key, int priority, Action action)
        {
            if (disposed || string.IsNullOrWhiteSpace(key) || action == null)
            {
                return;
            }
            if (dispatcher.InvokeRequired)
            {
                try
                {
                    dispatcher.BeginInvoke((Action)(() =>
                        Schedule(key, priority, action)));
                }
                catch (InvalidOperationException)
                {
                }
                return;
            }
            pending[key] = new WorkItem
            {
                Key = key,
                Priority = priority,
                Sequence = ++sequence,
                Action = action
            };
            timer.Start();
        }

        public void NotifyInteraction()
        {
            if (disposed)
            {
                return;
            }
            if (dispatcher.InvokeRequired)
            {
                try
                {
                    dispatcher.BeginInvoke((Action)NotifyInteraction);
                }
                catch (InvalidOperationException)
                {
                }
                return;
            }
            deferUntilUtc = DateTime.UtcNow.AddMilliseconds(
                InteractionQuietPeriodMs);
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            if (disposed || pending.Count == 0)
            {
                timer.Stop();
                return;
            }
            if (DateTime.UtcNow < deferUntilUtc)
            {
                return;
            }

            var stopwatch = Stopwatch.StartNew();
            int executed = 0;
            do
            {
                WorkItem next = pending.Values
                    .OrderBy(item => item.Priority)
                    .ThenBy(item => item.Sequence)
                    .FirstOrDefault();
                if (next == null)
                {
                    timer.Stop();
                    return;
                }
                pending.Remove(next.Key);
                try
                {
                    next.Action();
                }
                catch
                {
                    // 预热失败由具体缓存入口记录；调度器不能影响编辑器交互。
                }
                executed++;
            }
            while (pending.Count > 0
                && executed < MaxWorkItemsPerTick
                && stopwatch.ElapsedMilliseconds < TickBudgetMs);

            if (pending.Count == 0)
            {
                timer.Stop();
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            pending.Clear();
            timer.Stop();
            timer.Tick -= Timer_Tick;
            timer.Dispose();
        }
    }
}
