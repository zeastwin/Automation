using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Automation.DeviceSdk;

// 模块：平台内置 HMI / 旧项目 CCD 页面。
// 职责范围：保留旧 UI_VideoPage 的窗体结构和交互逻辑，仅通过视频适配服务接入 Automation。

namespace Automation.Hmi
{
    internal sealed partial class LegacyVideoPage : Form
    {
        private readonly GroupBox[] videoGroups;
        private readonly ComboBox[] deviceSelectors;
        private readonly PictureBox[] previews;
        private readonly Button[] startButtons;
        private readonly Button[] stopButtons;
        private readonly string[] openedMonikers = new string[4];
        private IAutomationPlatform platform;
        private ILegacyVideoService videoService;
        private Control maximizedGroup;

        internal LegacyVideoPage()
        {
            InitializeComponent();
            videoGroups = new[] { groupBox1, groupBox2, groupBox3, groupBox4 };
            deviceSelectors = new[] { deviceSelector1, deviceSelector2, deviceSelector3, deviceSelector4 };
            previews = new[] { previewBox1, previewBox2, previewBox3, previewBox4 };
            startButtons = new[] { startButton1, startButton2, startButton3, startButton4 };
            stopButtons = new[] { stopButton1, stopButton2, stopButton3, stopButton4 };
            for (int index = 0; index < previews.Length; index++)
            {
                previews[index].Tag = videoGroups[index];
            }
        }

        internal void AttachPlatform(IAutomationPlatform platform)
        {
            AttachPlatform(platform, null);
        }

        internal void AttachPlatform(
            IAutomationPlatform platform,
            ILegacyVideoService videoService)
        {
            this.platform = platform;
            if (this.videoService != null)
            {
                this.videoService.FrameReady -= VideoService_FrameReady;
            }
            this.videoService = videoService;
            if (this.videoService != null)
            {
                this.videoService.FrameReady += VideoService_FrameReady;
                RefreshDeviceList();
            }
            RefreshRuntimeView();
        }

        internal void RefreshRuntimeView()
        {
            if (platform == null)
            {
                return;
            }

            bool disabled = TryReadInteger("禁用Video", out int disabledValue)
                && disabledValue != 0;
            for (int index = 0; index < deviceSelectors.Length; index++)
            {
                string variableName = "摄像头设备" + (index + 1);
                string configured = TryReadString(variableName, out string value)
                    ? value
                    : string.Empty;
                ComboBox selector = deviceSelectors[index];
                if (!string.IsNullOrWhiteSpace(configured)
                    && selector.Items
                        .OfType<LegacyVideoDeviceInfo>()
                        .All(item => !string.Equals(
                            item.Moniker,
                            configured,
                            StringComparison.Ordinal)))
                {
                    selector.Items.Add(new LegacyVideoDeviceInfo(
                        "已配置设备（当前未枚举）",
                        configured));
                }
                if (!selector.DroppedDown)
                {
                    SelectConfiguredDevice(selector, configured);
                }
                selector.Enabled = !disabled;
                startButtons[index].Enabled = !disabled
                    && !string.IsNullOrWhiteSpace(configured)
                    && videoService != null;
                stopButtons[index].Enabled = !disabled
                    && videoService != null
                    && videoService.IsOpen(index + 1);
                if ((disabled || string.IsNullOrWhiteSpace(configured))
                    && videoService != null
                    && videoService.IsOpen(index + 1))
                {
                    videoService.Stop(index + 1);
                    openedMonikers[index] = null;
                    Image previous = previews[index].Image;
                    previews[index].Image = null;
                    previous?.Dispose();
                }
                if (!disabled
                    && videoService != null
                    && !string.IsNullOrWhiteSpace(configured)
                    && !videoService.IsOpen(index + 1)
                    && !string.Equals(
                        openedMonikers[index],
                        configured,
                        StringComparison.Ordinal))
                {
                    TryOpen(index + 1, configured, showError: false);
                }
            }
        }

        private void DeviceSelector_Commit(object sender, EventArgs e)
        {
            if (platform == null
                || !(sender is ComboBox selector)
                || !(selector.SelectedItem is LegacyVideoDeviceInfo selected))
            {
                return;
            }
            int videoId = Convert.ToInt32(selector.Tag, CultureInfo.InvariantCulture);
            string value = selected.Moniker;
            if (!platform.Values.Set("摄像头设备" + videoId, value, out string error))
            {
                MessageBox.Show(FindForm(), error, "摄像头配置失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (string.IsNullOrWhiteSpace(value))
            {
                videoService?.Stop(videoId);
                openedMonikers[videoId - 1] = null;
                return;
            }
            TryOpen(videoId, value, showError: true);
        }

        private void DeviceSelector_DropDown(object sender, EventArgs e)
        {
            RefreshDeviceList();
        }

        private void StartButton_Click(object sender, EventArgs e)
        {
            if (!(sender is Button button))
            {
                return;
            }
            int videoId = Convert.ToInt32(button.Tag, CultureInfo.InvariantCulture);
            if (deviceSelectors[videoId - 1].SelectedItem is LegacyVideoDeviceInfo selected
                && !string.IsNullOrWhiteSpace(selected.Moniker))
            {
                TryOpen(videoId, selected.Moniker, showError: true);
            }
        }

        private void StopButton_Click(object sender, EventArgs e)
        {
            if (!(sender is Button button))
            {
                return;
            }
            int videoId = Convert.ToInt32(button.Tag, CultureInfo.InvariantCulture);
            videoService?.Stop(videoId);
            openedMonikers[videoId - 1] = null;
            Image previous = previews[videoId - 1].Image;
            previews[videoId - 1].Image = null;
            previous?.Dispose();
            RefreshRuntimeView();
        }

        private void Preview_DoubleClick(object sender, EventArgs e)
        {
            if (!(sender is PictureBox preview) || !(preview.Tag is GroupBox selected))
            {
                return;
            }
            if (maximizedGroup == null)
            {
                foreach (GroupBox group in videoGroups)
                {
                    group.Visible = ReferenceEquals(group, selected);
                }
                videoLayout.SetColumn(selected, 0);
                videoLayout.SetRow(selected, 0);
                videoLayout.SetColumnSpan(selected, 2);
                videoLayout.SetRowSpan(selected, 2);
                maximizedGroup = selected;
                return;
            }

            videoLayout.SetColumnSpan(selected, 1);
            videoLayout.SetRowSpan(selected, 1);
            for (int index = 0; index < videoGroups.Length; index++)
            {
                GroupBox group = videoGroups[index];
                videoLayout.SetColumn(group, index % 2);
                videoLayout.SetRow(group, index / 2);
                group.Visible = true;
            }
            maximizedGroup = null;
        }

        private void RefreshDeviceList()
        {
            if (videoService == null)
            {
                return;
            }
            IReadOnlyList<LegacyVideoDeviceInfo> devices;
            try
            {
                devices = videoService.GetDevices();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    FindForm(),
                    ex.Message,
                    "摄像头枚举失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            foreach (ComboBox selector in deviceSelectors)
            {
                string selectedMoniker =
                    (selector.SelectedItem as LegacyVideoDeviceInfo)?.Moniker;
                selector.BeginUpdate();
                selector.Items.Clear();
                selector.Items.Add(new LegacyVideoDeviceInfo("未配置", string.Empty));
                selector.Items.AddRange(devices.Cast<object>().ToArray());
                SelectConfiguredDevice(selector, selectedMoniker);
                selector.EndUpdate();
            }
        }

        private static void SelectConfiguredDevice(ComboBox selector, string moniker)
        {
            if (string.IsNullOrWhiteSpace(moniker))
            {
                selector.SelectedIndex = selector.Items.Count > 0 ? 0 : -1;
                return;
            }
            for (int index = 0; index < selector.Items.Count; index++)
            {
                if (selector.Items[index] is LegacyVideoDeviceInfo device
                    && string.Equals(device.Moniker, moniker, StringComparison.Ordinal))
                {
                    selector.SelectedIndex = index;
                    return;
                }
            }
            selector.SelectedIndex = -1;
        }

        private void TryOpen(int channel, string moniker, bool showError)
        {
            if (videoService == null)
            {
                return;
            }
            if (videoService.Open(channel, moniker, out string error))
            {
                openedMonikers[channel - 1] = moniker;
                startButtons[channel - 1].Enabled = false;
                stopButtons[channel - 1].Enabled = true;
                return;
            }
            openedMonikers[channel - 1] = moniker;
            if (showError)
            {
                MessageBox.Show(
                    FindForm(),
                    error,
                    "摄像头打开失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void VideoService_FrameReady(
            object sender,
            LegacyVideoFrameEventArgs e)
        {
            if (IsDisposed || Disposing)
            {
                e.Frame.Dispose();
                return;
            }
            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke((Action)(() => ApplyVideoFrame(e)));
                }
                catch (InvalidOperationException)
                {
                    e.Frame.Dispose();
                }
                return;
            }
            ApplyVideoFrame(e);
        }

        private void ApplyVideoFrame(LegacyVideoFrameEventArgs e)
        {
            PictureBox preview = previews[e.Channel - 1];
            Image previous = preview.Image;
            preview.Image = e.Frame;
            previous?.Dispose();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                components?.Dispose();
                if (videoService != null)
                {
                    videoService.FrameReady -= VideoService_FrameReady;
                }
                foreach (PictureBox preview in previews)
                {
                    preview?.Image?.Dispose();
                }
            }
            base.Dispose(disposing);
        }

        private bool TryReadString(string name, out string value)
        {
            value = string.Empty;
            if (!platform.Values.TryGet(name, out ValueSnapshot snapshot, out _)
                || snapshot == null)
            {
                return false;
            }
            value = snapshot.Value ?? string.Empty;
            return true;
        }

        private bool TryReadInteger(string name, out int value)
        {
            value = 0;
            return TryReadString(name, out string text)
                && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }
    }
}

