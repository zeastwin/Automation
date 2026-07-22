using Microsoft.Win32.SafeHandles;
// 模块：运行时 / 基础设施。
// 职责范围：提供不承载业务规则的计时与对象图辅助能力。

using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

namespace Automation
{
    internal sealed class HighResolutionWaiter : IDisposable
    {
        private const uint CreateWaitableTimerHighResolution = 0x00000002;
        private const uint TimerAllAccess = 0x001F0003;
        private const uint WaitObject0 = 0;
        private const uint WaitFailed = 0xFFFFFFFF;
        private readonly CancellationToken cancellationToken;
        private readonly WaitHandle cancellationWaitHandle;
        private readonly SafeWaitHandle timerHandle;
        private readonly IntPtr[] waitHandles = new IntPtr[2];
        private int disposed;

        public HighResolutionWaiter(CancellationToken cancellationToken)
        {
            this.cancellationToken = cancellationToken;
            cancellationWaitHandle = cancellationToken.WaitHandle;
            IntPtr handle;
            try
            {
                handle = CreateWaitableTimerEx(IntPtr.Zero, null, CreateWaitableTimerHighResolution, TimerAllAccess);
                if (handle == IntPtr.Zero || handle == new IntPtr(-1))
                {
                    handle = CreateWaitableTimer(IntPtr.Zero, false, null);
                }
            }
            catch (EntryPointNotFoundException)
            {
                handle = CreateWaitableTimer(IntPtr.Zero, false, null);
            }
            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "创建高精度等待定时器失败");
            }
            timerHandle = new SafeWaitHandle(handle, true);
            waitHandles[0] = cancellationWaitHandle.SafeWaitHandle.DangerousGetHandle();
            waitHandles[1] = timerHandle.DangerousGetHandle();
        }

        public bool Wait(int milliseconds)
        {
            if (milliseconds <= 0)
            {
                return !cancellationToken.IsCancellationRequested;
            }
            if (Volatile.Read(ref disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(HighResolutionWaiter));
            }
            long dueTime = -Math.Max(1L, milliseconds * 10000L);
            if (!SetWaitableTimer(timerHandle.DangerousGetHandle(), ref dueTime, 0, IntPtr.Zero, IntPtr.Zero, false))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "设置高精度等待定时器失败");
            }
            uint result = WaitForMultipleObjects((uint)waitHandles.Length, waitHandles, false, uint.MaxValue);
            if (result == WaitFailed)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "等待高精度定时器失败");
            }
            return result == WaitObject0 + 1 && !cancellationToken.IsCancellationRequested;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }
            timerHandle.Dispose();
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWaitableTimerEx(IntPtr timerAttributes, string timerName,
            uint flags, uint desiredAccess);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWaitableTimer(IntPtr timerAttributes, bool manualReset, string timerName);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWaitableTimer(IntPtr timer, ref long dueTime, int period,
            IntPtr completionRoutine, IntPtr completionArgument, bool resume);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForMultipleObjects(uint count, IntPtr[] handles,
            [MarshalAs(UnmanagedType.Bool)] bool waitAll, uint milliseconds);
    }
}
