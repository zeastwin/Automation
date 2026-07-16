using System;
using System.Collections.Generic;

namespace Automation.DeviceSdk
{
    public static class PlatformApiInfo
    {
        public const string ApiVersion = "1.0";
    }

    public enum PlatformRuntimeStatus
    {
        Created = 0,
        Initializing = 1,
        Ready = 2,
        Faulted = 3,
        ShuttingDown = 4,
        Stopped = 5
    }

    public enum ProcessRuntimeStatus
    {
        Stopped = 0,
        Paused = 1,
        SingleStep = 2,
        Running = 3,
        Alarming = 4,
        Pausing = 5,
        Stopping = 6
    }

    public sealed class ValueSnapshot
    {
        public int Index { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
        public string Value { get; set; }
        public string Note { get; set; }
    }

    public sealed class ValueChangedEventArgs : EventArgs
    {
        public int Index { get; set; }
        public string Name { get; set; }
        public string OldValue { get; set; }
        public string NewValue { get; set; }
        public string Source { get; set; }
        public DateTime ChangedAt { get; set; }
    }

    public sealed class ProcessSnapshot
    {
        public int Index { get; set; }
        public Guid Id { get; set; }
        public string Name { get; set; }
        public ProcessRuntimeStatus State { get; set; }
        public int StepIndex { get; set; }
        public int OperationIndex { get; set; }
        public bool Disabled { get; set; }
        public bool IsAlarm { get; set; }
        public string AlarmMessage { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public sealed class ProcessChangedEventArgs : EventArgs
    {
        public ProcessSnapshot Process { get; set; }
    }

    public sealed class PlatformRuntimeStatusChangedEventArgs : EventArgs
    {
        public PlatformRuntimeStatusChangedEventArgs(PlatformRuntimeStatus status, string message)
        {
            Status = status;
            Message = message ?? string.Empty;
        }

        public PlatformRuntimeStatus Status { get; }

        public string Message { get; }
    }

    public interface IValueStore
    {
        event EventHandler<ValueChangedEventArgs> Changed;

        IReadOnlyList<string> GetNames();

        bool TryGet(string name, out ValueSnapshot value, out string error);

        bool TryGet(int index, out ValueSnapshot value, out string error);

        bool Set(string name, object value, out string error);

        bool Monitor(string name, bool enabled, out string error);
    }

    public interface IProcessStore
    {
        event EventHandler<ProcessChangedEventArgs> Changed;

        IReadOnlyList<ProcessSnapshot> GetAll();

        bool Start(int processIndex, out string error);

        bool Pause(int processIndex, out string error);

        bool Resume(int processIndex, out string error);

        bool Stop(int processIndex, out string error);

        bool StopAll(out string error);
    }

    public interface IAutomationPlatform : IDisposable
    {
        event EventHandler<PlatformRuntimeStatusChangedEventArgs> RuntimeStatusChanged;

        PlatformRuntimeStatus RuntimeStatus { get; }

        string ApiVersion { get; }

        string PlatformVersion { get; }

        string RuntimeMessage { get; }

        IValueStore Values { get; }

        IProcessStore Processes { get; }

        bool Initialize(out string error);

        void RegisterCustomFunction(string name, Action function);

        void ShowPlatformEditor();

        void HidePlatformEditor();

        void NotifyInteractionUiReady();

        void Shutdown();
    }
}
