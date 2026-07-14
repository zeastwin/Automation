using System;
using System.Collections.Generic;
using System.Linq;

namespace Automation
{
    public enum PlcDeviceProfile
    {
        GenericModbusTcp = 0,
        InovanceModbusTcp = 1
    }

    public enum PlcArea
    {
        Coil = 0,
        DiscreteInput = 1,
        HoldingRegister = 2,
        InputRegister = 3
    }

    public enum PlcDataType
    {
        String = 0,
        Boolean = 1,
        Byte = 2,
        UShort = 3,
        Short = 4,
        UInt = 5,
        Int = 6,
        Float = 7,
        Double = 8
    }

    /// <summary>
    /// 跨多个Modbus寄存器的数值字节序。
    /// </summary>
    public enum PlcDataFormat
    {
        ABCD = 0,
        BADC = 1,
        CDAB = 2,
        DCBA = 3
    }

    public enum PlcMapDirection
    {
        ReadFromPlc = 0,
        WriteToPlc = 1,
        Bidirectional = 2
    }

    public enum PlcMapPriority
    {
        Low = 0,
        Medium = 1,
        High = 2
    }

    public enum PlcRuntimeState
    {
        Uninitialized = 0,
        Connecting = 1,
        Ready = 2,
        Mapping = 3,
        Stopped = 4,
        Faulted = 5
    }

    public enum PlcMapRuntimeState
    {
        Idle = 0,
        Normal = 1,
        Conflict = 2,
        Faulted = 3
    }

    public enum PlcConflictResolution
    {
        UsePlcValue = 0,
        UseLocalValue = 1
    }

    public enum PlcMappingAction
    {
        Reinitialize = 0,
        Start = 1,
        Stop = 2
    }

    public enum PlcAccessAction
    {
        Read = 0,
        Write = 1
    }

    public enum PlcReadMode
    {
        DiscreteItems = 0,
        ContinuousBatch = 1
    }

    public enum PlcWriteSource
    {
        Variables = 0,
        Constant = 1
    }

    [Serializable]
    public sealed class PlcConfiguration
    {
        public int SchemaVersion { get; set; } = 1;
        public List<PlcDeviceConfig> Devices { get; set; } = new List<PlcDeviceConfig>();
    }

    [Serializable]
    public sealed class PlcDeviceConfig
    {
        public string Name { get; set; } = string.Empty;
        public PlcDeviceProfile Profile { get; set; } = PlcDeviceProfile.GenericModbusTcp;
        public string IpAddress { get; set; } = string.Empty;
        public int Port { get; set; } = 502;
        public int UnitId { get; set; } = 1;
        public int ConnectTimeoutMs { get; set; } = 1000;
        public int ReceiveTimeoutMs { get; set; } = 3000;
        public bool AutoConnect { get; set; } = true;
        public int ScanIntervalMs { get; set; } = 50;
        public string DataFormat { get; set; } = "CDAB";
        public bool IsStringReverse { get; set; }
        public bool AddressStartWithZero { get; set; } = true;
        public string StatusVariableName { get; set; } = string.Empty;
        public List<PlcMapConfig> Mappings { get; set; } = new List<PlcMapConfig>();

        public static PlcDeviceConfig Create(PlcDeviceProfile profile)
        {
            return new PlcDeviceConfig
            {
                Profile = profile,
                IsStringReverse = profile == PlcDeviceProfile.InovanceModbusTcp
            };
        }
    }

    [Serializable]
    public sealed class PlcMapConfig
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
        public PlcArea Area { get; set; } = PlcArea.HoldingRegister;
        public int StartAddress { get; set; }
        public PlcDataType DataType { get; set; } = PlcDataType.Float;
        public PlcMapDirection Direction { get; set; } = PlcMapDirection.ReadFromPlc;
        public PlcMapPriority Priority { get; set; } = PlcMapPriority.High;
        public int ElementCount { get; set; } = 1;
        public int StringByteLength { get; set; }
        public List<string> VariableNames { get; set; } = new List<string>();
        public double ChangeTolerance { get; set; }
    }

    public sealed class PlcDeviceRuntimeSnapshot
    {
        public string DeviceName { get; set; }
        public PlcRuntimeState State { get; set; }
        public string LastError { get; set; }
        public DateTime? LastCommunicationUtc { get; set; }
        public long LastScanElapsedMs { get; set; }
        public IReadOnlyList<PlcMapRuntimeSnapshot> Mappings { get; set; }
    }

    public sealed class PlcMapRuntimeSnapshot
    {
        public string MapId { get; set; }
        public string MapName { get; set; }
        public PlcMapRuntimeState State { get; set; }
        public string Message { get; set; }
        public object[] PlcValues { get; set; }
        public object[] LocalValues { get; set; }
    }

    public sealed class PlcRuntimeEventArgs : EventArgs
    {
        public PlcRuntimeEventArgs(string deviceName, string message, bool isAlarm)
        {
            DeviceName = deviceName ?? string.Empty;
            Message = message ?? string.Empty;
            IsAlarm = isAlarm;
        }

        public string DeviceName { get; }
        public string Message { get; }
        public bool IsAlarm { get; }
    }

    internal static class PlcModelClone
    {
        public static PlcConfiguration Clone(PlcConfiguration source)
        {
            return new PlcConfiguration
            {
                SchemaVersion = source?.SchemaVersion ?? 1,
                Devices = source?.Devices?.Select(Clone).ToList() ?? new List<PlcDeviceConfig>()
            };
        }

        public static PlcDeviceConfig Clone(PlcDeviceConfig source)
        {
            if (source == null) return null;
            return new PlcDeviceConfig
            {
                Name = source.Name,
                Profile = source.Profile,
                IpAddress = source.IpAddress,
                Port = source.Port,
                UnitId = source.UnitId,
                ConnectTimeoutMs = source.ConnectTimeoutMs,
                ReceiveTimeoutMs = source.ReceiveTimeoutMs,
                AutoConnect = source.AutoConnect,
                ScanIntervalMs = source.ScanIntervalMs,
                DataFormat = source.DataFormat,
                IsStringReverse = source.IsStringReverse,
                AddressStartWithZero = source.AddressStartWithZero,
                StatusVariableName = source.StatusVariableName,
                Mappings = source.Mappings?.Select(Clone).ToList() ?? new List<PlcMapConfig>()
            };
        }

        public static PlcMapConfig Clone(PlcMapConfig source)
        {
            if (source == null) return null;
            return new PlcMapConfig
            {
                Id = source.Id,
                Name = source.Name,
                Enabled = source.Enabled,
                Area = source.Area,
                StartAddress = source.StartAddress,
                DataType = source.DataType,
                Direction = source.Direction,
                Priority = source.Priority,
                ElementCount = source.ElementCount,
                StringByteLength = source.StringByteLength,
                VariableNames = source.VariableNames?.ToList() ?? new List<string>(),
                ChangeTolerance = source.ChangeTolerance
            };
        }
    }
}
