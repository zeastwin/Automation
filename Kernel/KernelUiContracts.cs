using System;

namespace Automation.Kernel
{
    public enum AlarmDialogResult
    {
        None = 0,
        Button1 = 1,
        Button2 = 2,
        Button3 = 3
    }

    public sealed class AlarmDialogRequest
    {
        public AlarmDialogRequest(int processIndex, string title, string message, string[] buttons)
        {
            ProcessIndex = processIndex;
            Title = title ?? string.Empty;
            Message = message ?? string.Empty;
            Buttons = buttons ?? Array.Empty<string>();
        }

        public int ProcessIndex { get; }
        public string Title { get; }
        public string Message { get; }
        public string[] Buttons { get; }
    }
}
