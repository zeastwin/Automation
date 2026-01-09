using System.Threading;

namespace Automation.Kernel
{
    internal sealed class ProcessStateSignal
    {
        private readonly ManualResetEventSlim _running = new ManualResetEventSlim(false);
        private readonly ManualResetEventSlim _stopped = new ManualResetEventSlim(true);

        public ManualResetEventSlim RunningEvent => _running;
        public ManualResetEventSlim StoppedEvent => _stopped;

        public void Update(ProcessState state)
        {
            if (state == ProcessState.Stopped || state == ProcessState.Unknown)
            {
                _stopped.Set();
                _running.Reset();
            }
            else
            {
                _running.Set();
                _stopped.Reset();
            }
        }
    }
}
