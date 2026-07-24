using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using AForge.Video;
using AForge.Video.DirectShow;
using Accord.Video.FFMPEG;

// 模块：设备项目适配 / 旧项目视频能力。
// 职责范围：迁入旧 VideoDevices 的枚举、预览、截图和录像语义，并隐藏原生摄像头对象。
// 排查入口：无画面时先检查摄像头 Moniker 配置、禁用Video 和 x64 FFMPEG 依赖。

namespace Automation.Hmi
{
    internal sealed class LegacyVideoDeviceInfo
    {
        internal LegacyVideoDeviceInfo(string name, string moniker)
        {
            Name = name ?? string.Empty;
            Moniker = moniker ?? string.Empty;
        }

        internal string Name { get; }

        internal string Moniker { get; }

        public override string ToString()
        {
            return string.IsNullOrWhiteSpace(Name) ? Moniker : Name;
        }
    }

    internal sealed class LegacyVideoFrameEventArgs : EventArgs
    {
        internal LegacyVideoFrameEventArgs(int channel, Bitmap frame)
        {
            Channel = channel;
            Frame = frame;
        }

        internal int Channel { get; }

        /// <summary>
        /// 事件接收方取得图像所有权，使用后必须释放。
        /// </summary>
        internal Bitmap Frame { get; }
    }

    internal interface ILegacyVideoService : IDisposable
    {
        event EventHandler<LegacyVideoFrameEventArgs> FrameReady;

        IReadOnlyList<LegacyVideoDeviceInfo> GetDevices();

        bool Open(int channel, string moniker, out string error);

        void Stop(int channel);

        bool IsOpen(int channel);

        bool StartRecording(int channel, string path, out string error);

        bool StopRecording(int channel, out string error);

        bool SavePicture(int channel, string path, out string error);
    }

    /// <summary>
    /// 旧项目 VideoDevices 的新平台设备服务版本。窗体和流程消息均复用同一实例。
    /// </summary>
    internal sealed class LegacyVideoService : ILegacyVideoService
    {
        private sealed class ChannelState
        {
            internal readonly object Gate = new object();
            internal readonly Stopwatch FrameClock = Stopwatch.StartNew();
            internal readonly Queue<double> RecentFps = new Queue<double>();
            internal VideoCaptureDevice Source;
            internal VideoFileWriter Writer;
            internal Bitmap LastFrame;
            internal double Fps;
            internal bool Recording;
        }

        private readonly ChannelState[] channels =
        {
            new ChannelState(),
            new ChannelState(),
            new ChannelState(),
            new ChannelState()
        };
        private bool disposed;

        public event EventHandler<LegacyVideoFrameEventArgs> FrameReady;

        public IReadOnlyList<LegacyVideoDeviceInfo> GetDevices()
        {
            ThrowIfDisposed();
            var result = new List<LegacyVideoDeviceInfo>();
            var devices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
            foreach (FilterInfo device in devices)
            {
                result.Add(new LegacyVideoDeviceInfo(device.Name, device.MonikerString));
            }
            return result;
        }

        public bool Open(int channel, string moniker, out string error)
        {
            error = string.Empty;
            try
            {
                ThrowIfDisposed();
                ChannelState state = GetChannel(channel);
                if (string.IsNullOrWhiteSpace(moniker))
                {
                    throw new InvalidOperationException("摄像头设备 Moniker 不能为空。");
                }
                if (state.Source != null
                    && state.Source.IsRunning
                    && string.Equals(
                        state.Source.Source,
                        moniker,
                        StringComparison.Ordinal))
                {
                    return true;
                }

                Stop(channel);
                LegacyVideoDeviceInfo selected = GetDevices().FirstOrDefault(
                    item => string.Equals(item.Moniker, moniker, StringComparison.Ordinal));
                if (selected == null)
                {
                    throw new InvalidOperationException("指定的摄像头设备未找到：" + moniker);
                }

                var source = new VideoCaptureDevice(selected.Moniker);
                if (source.VideoCapabilities != null && source.VideoCapabilities.Length > 0)
                {
                    source.VideoResolution = source.VideoCapabilities[0];
                }
                source.NewFrame += (sender, args) => OnNewFrame(channel, args);
                lock (state.Gate)
                {
                    state.Source = source;
                    state.FrameClock.Restart();
                    state.RecentFps.Clear();
                    state.Fps = 0D;
                }
                source.Start();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public void Stop(int channel)
        {
            ChannelState state = GetChannel(channel);
            StopRecording(channel, out _);
            VideoCaptureDevice source;
            lock (state.Gate)
            {
                source = state.Source;
                state.Source = null;
            }
            if (source != null)
            {
                if (source.IsRunning)
                {
                    source.SignalToStop();
                    source.WaitForStop();
                }
            }
            lock (state.Gate)
            {
                state.LastFrame?.Dispose();
                state.LastFrame = null;
            }
        }

        public bool IsOpen(int channel)
        {
            ChannelState state = GetChannel(channel);
            lock (state.Gate)
            {
                return state.Source != null && state.Source.IsRunning;
            }
        }

        public bool StartRecording(int channel, string path, out string error)
        {
            error = string.Empty;
            try
            {
                ThrowIfDisposed();
                ChannelState state = GetChannel(channel);
                lock (state.Gate)
                {
                    if (state.Source == null || !state.Source.IsRunning)
                    {
                        throw new InvalidOperationException(
                            "Video_" + channel.ToString(CultureInfo.InvariantCulture)
                            + " 尚未打开。");
                    }
                    if (state.LastFrame == null)
                    {
                        throw new InvalidOperationException(
                            "摄像头尚未收到首帧，不能开始录像。");
                    }

                    CloseWriter(state);
                    string fullPath = Path.GetFullPath(path);
                    string directory = Path.GetDirectoryName(fullPath);
                    if (string.IsNullOrWhiteSpace(directory))
                    {
                        throw new InvalidOperationException("录像保存目录无效：" + path);
                    }
                    Directory.CreateDirectory(directory);
                    int fps = (int)Math.Round(state.Fps, MidpointRounding.AwayFromZero);
                    fps = Math.Max(1, Math.Min(30, fps == 0 ? 7 : fps));
                    var writer = new VideoFileWriter();
                    writer.Open(
                        fullPath,
                        state.LastFrame.Width,
                        state.LastFrame.Height,
                        fps,
                        VideoCodec.MPEG4,
                        2000000);
                    state.Writer = writer;
                    state.Recording = true;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public bool StopRecording(int channel, out string error)
        {
            error = string.Empty;
            try
            {
                ChannelState state = GetChannel(channel);
                lock (state.Gate)
                {
                    CloseWriter(state);
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public bool SavePicture(int channel, string path, out string error)
        {
            error = string.Empty;
            try
            {
                ThrowIfDisposed();
                ChannelState state = GetChannel(channel);
                Bitmap frame;
                lock (state.Gate)
                {
                    if (state.LastFrame == null)
                    {
                        throw new InvalidOperationException("摄像头尚未收到图像。");
                    }
                    frame = (Bitmap)state.LastFrame.Clone();
                }
                using (frame)
                {
                    string fullPath = Path.GetFullPath(path);
                    Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
                    frame.Save(fullPath);
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            for (int channel = 1; channel <= channels.Length; channel++)
            {
                Stop(channel);
            }
        }

        private void OnNewFrame(int channel, NewFrameEventArgs args)
        {
            ChannelState state = GetChannel(channel);
            Bitmap preview;
            lock (state.Gate)
            {
                double elapsed = state.FrameClock.Elapsed.TotalSeconds;
                state.FrameClock.Restart();
                if (elapsed > 0D)
                {
                    state.RecentFps.Enqueue(1D / elapsed);
                    while (state.RecentFps.Count > 120)
                    {
                        state.RecentFps.Dequeue();
                    }
                    state.Fps = state.RecentFps.Average();
                }

                state.LastFrame?.Dispose();
                state.LastFrame = (Bitmap)args.Frame.Clone();
                if (state.Recording && state.Writer != null)
                {
                    state.Writer.WriteVideoFrame(state.LastFrame);
                }
                preview = (Bitmap)state.LastFrame.Clone();
            }

            EventHandler<LegacyVideoFrameEventArgs> handler = FrameReady;
            if (handler == null)
            {
                preview.Dispose();
                return;
            }
            handler(this, new LegacyVideoFrameEventArgs(channel, preview));
        }

        private ChannelState GetChannel(int channel)
        {
            if (channel < 1 || channel > channels.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(channel),
                    "视频通道只支持 1 到 4。");
            }
            return channels[channel - 1];
        }

        private static void CloseWriter(ChannelState state)
        {
            state.Recording = false;
            if (state.Writer == null)
            {
                return;
            }
            state.Writer.Close();
            state.Writer.Dispose();
            state.Writer = null;
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(LegacyVideoService));
            }
        }
    }
}
