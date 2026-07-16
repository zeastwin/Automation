using System;
using System.Collections.Generic;
using Automation.DeviceSdk;
using DeviceValueChangedEventArgs = Automation.DeviceSdk.ValueChangedEventArgs;

namespace Automation
{
    internal sealed class PlatformValueStoreFacade : IValueStore
    {
        private readonly AutomationPlatformHost platform;

        public PlatformValueStoreFacade(AutomationPlatformHost platform)
        {
            this.platform = platform ?? throw new ArgumentNullException(nameof(platform));
            platform.ValueChanged += Platform_ValueChanged;
        }

        public event EventHandler<DeviceValueChangedEventArgs> Changed;

        public IReadOnlyList<string> GetNames()
        {
            return platform.GetValueNames();
        }

        public bool TryGet(string name, out ValueSnapshot value, out string error)
        {
            value = null;
            if (!platform.TryGetValue(name, out PlatformValueSnapshot snapshot, out error))
            {
                return false;
            }
            value = Map(snapshot);
            return true;
        }

        public bool TryGet(int index, out ValueSnapshot value, out string error)
        {
            value = null;
            if (!platform.TryGetValue(index, out PlatformValueSnapshot snapshot, out error))
            {
                return false;
            }
            value = Map(snapshot);
            return true;
        }

        public bool Set(string name, object value, out string error)
        {
            return platform.TrySetValue(name, value, out error);
        }

        public bool Monitor(string name, bool enabled, out string error)
        {
            return platform.TryMonitorValue(name, enabled, out error);
        }

        private void Platform_ValueChanged(object sender, ValueChangedEventArgs e)
        {
            Changed?.Invoke(this, new DeviceValueChangedEventArgs
            {
                Index = e.Index,
                Name = e.Name,
                OldValue = e.OldValue,
                NewValue = e.NewValue,
                Source = e.Source,
                ChangedAt = e.ChangedAt
            });
        }

        private static ValueSnapshot Map(PlatformValueSnapshot source)
        {
            return new ValueSnapshot
            {
                Index = source.Index,
                Name = source.Name,
                Type = source.Type,
                Value = source.Value,
                Note = source.Note
            };
        }
    }
}
