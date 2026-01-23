using System;
using System.Collections.Generic;

namespace Automation
{
    public readonly struct TrayPoint
    {
        public TrayPoint(int order, int row, int col, double x, double y, double z, double u, double v, double w)
        {
            Order = order;
            Row = row;
            Col = col;
            X = x;
            Y = y;
            Z = z;
            U = u;
            V = v;
            W = w;
        }

        public int Order { get; }
        public int Row { get; }
        public int Col { get; }
        public double X { get; }
        public double Y { get; }
        public double Z { get; }
        public double U { get; }
        public double V { get; }
        public double W { get; }
    }

    public sealed class TrayPointGrid
    {
        public TrayPointGrid(string stationName, int trayId, int rowCount, int colCount, List<TrayPoint> points)
        {
            StationName = stationName;
            TrayId = trayId;
            RowCount = rowCount;
            ColCount = colCount;
            Points = points ?? new List<TrayPoint>();
        }

        public string StationName { get; }
        public int TrayId { get; }
        public int RowCount { get; }
        public int ColCount { get; }
        public List<TrayPoint> Points { get; }

        public TrayPointGrid Clone()
        {
            return new TrayPointGrid(StationName, TrayId, RowCount, ColCount, new List<TrayPoint>(Points));
        }
    }

    public sealed class TrayPointStore
    {
        private readonly object dataLock = new object();
        private readonly Dictionary<string, TrayPointGrid> grids = new Dictionary<string, TrayPointGrid>();

        public bool TrySave(TrayPointGrid grid, out string error)
        {
            error = null;
            if (grid == null)
            {
                error = "料盘缓存为空";
                return false;
            }
            if (string.IsNullOrWhiteSpace(grid.StationName))
            {
                error = "工站名称为空";
                return false;
            }
            if (grid.TrayId < 0)
            {
                error = $"料盘ID无效:{grid.TrayId}";
                return false;
            }
            if (grid.RowCount <= 0 || grid.ColCount <= 0)
            {
                error = $"料盘行列数无效:行{grid.RowCount},列{grid.ColCount}";
                return false;
            }
            int expectedCount;
            try
            {
                expectedCount = checked(grid.RowCount * grid.ColCount);
            }
            catch (OverflowException)
            {
                error = "料盘点位数量溢出";
                return false;
            }
            if (grid.Points == null)
            {
                error = "料盘点位为空";
                return false;
            }
            if (grid.Points.Count != expectedCount)
            {
                error = $"料盘点位数量不一致:{grid.Points.Count}";
                return false;
            }
            string key = BuildKey(grid.StationName, grid.TrayId);
            lock (dataLock)
            {
                grids[key] = grid.Clone();
            }
            return true;
        }

        public bool TryGet(string stationName, int trayId, out TrayPointGrid grid)
        {
            grid = null;
            if (string.IsNullOrWhiteSpace(stationName) || trayId < 0)
            {
                return false;
            }
            string key = BuildKey(stationName, trayId);
            lock (dataLock)
            {
                if (!grids.TryGetValue(key, out TrayPointGrid cached))
                {
                    return false;
                }
                grid = cached?.Clone();
                return grid != null;
            }
        }

        private static string BuildKey(string stationName, int trayId)
        {
            return $"{stationName}::{trayId}";
        }
    }
}
