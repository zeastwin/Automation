using System;
using System.Threading;

namespace Automation.Kernel
{
    public sealed class RunnerSyncController
    {
        private readonly ManualResetEvent _run;
        private readonly ManualResetEvent _tick;
        private readonly ManualResetEvent _tok;

        public RunnerSyncController(ManualResetEvent run, ManualResetEvent tick, ManualResetEvent tok)
        {
            _run = run ?? throw new ArgumentNullException(nameof(run));
            _tick = tick ?? throw new ArgumentNullException(nameof(tick));
            _tok = tok ?? throw new ArgumentNullException(nameof(tok));
        }

        public void EnterBreakpoint()
        {
            _run.Reset();
            _tick.Reset();
            _tok.Set();
        }

        public void Continue()
        {
            _run.Set();
            _tick.Set();
            _tok.Set();
        }

        public void StepOnce()
        {
            _run.Set();
            _tok.Reset();
            _tick.Set();
            Automation.SF.Delay(10);
            _tick.Reset();
            _tok.Set();
        }

        public void ForceWakeForStop()
        {
            _run.Set();
            _tick.Set();
            _tok.Set();
        }

        public void WaitForContinue()
        {
            _run.WaitOne();
            _tick.WaitOne();
            _tok.WaitOne();
        }
    }
}
