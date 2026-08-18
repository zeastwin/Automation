// 模块：编辑器 / AI。
// 职责范围：AI 前台、ACP 会话、预演确认与对话渲染。
// 文件职责：流程结构独立放大窗口；无边框，占屏幕工作区约三分之二，拖动工具栏移动。

using Microsoft.Web.WebView2.WinForms;
using Newtonsoft.Json.Linq;
using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Automation
{
    public sealed partial class FrmAiAssistant
    {
        private FrmFlowZoom flowZoomWindow;

        // 在独立窗口中显示流程结构；重复点击更新同一窗口内容，不叠加窗口。
        private void OpenFlowZoomWindow(string flowCardsHtml)
        {
            if (string.IsNullOrWhiteSpace(flowCardsHtml))
            {
                return;
            }
            if (flowZoomWindow == null || flowZoomWindow.IsDisposed)
            {
                flowZoomWindow = new FrmFlowZoom();
            }
            flowZoomWindow.ShowFlow(flowCardsHtml);
            flowZoomWindow.Activate();
        }
    }

    // 流程结构放大窗口：无边框，占屏幕工作区约三分之二并居中；拖动工具栏移动，Esc/页面按钮关闭。
    internal sealed class FrmFlowZoom : Form
    {
        private const int WMNclButtonDown = 0xA1;
        private const int HtCaption = 0x2;

        private readonly WebView2 webView = new WebView2 { Dock = DockStyle.Fill };
        private string pendingFlowHtml;
        private bool webViewReady;

        public FrmFlowZoom()
        {
            UiBranding.Apply(this);
            Text = "流程结构";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            // 无边框窗口占主屏工作区三分之二并居中。
            Rectangle workArea = Screen.PrimaryScreen?.WorkingArea
                ?? Screen.FromHandle(Handle).WorkingArea;
            Size = new Size(workArea.Width * 2 / 3, workArea.Height * 2 / 3);
            Location = new Point(
                workArea.X + (workArea.Width - Width) / 2,
                workArea.Y + (workArea.Height - Height) / 2);
            Controls.Add(webView);
            Load += async (sender, e) => await InitializeWebViewAsync();
            FormClosed += (sender, e) => webView.Dispose();
        }

        // 显示流程卡片 HTML；WebView 就绪前先缓存，就绪后每次调用重新导航替换内容。
        public void ShowFlow(string flowCardsHtml)
        {
            pendingFlowHtml = flowCardsHtml;
            if (webViewReady)
            {
                NavigateToFlow();
            }
            else
            {
                Show();
            }
        }

        private async System.Threading.Tasks.Task InitializeWebViewAsync()
        {
            try
            {
                // 与主对话 WebView 共享同一用户数据目录（同进程共享浏览器进程）。
                string webViewUserDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Automation", "WebView2");
                Directory.CreateDirectory(webViewUserDataFolder);
                Microsoft.Web.WebView2.Core.CoreWebView2Environment webViewEnvironment =
                    await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(
                        null,
                        webViewUserDataFolder);
                await webView.EnsureCoreWebView2Async(webViewEnvironment);
                webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                webView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = true;
                webView.CoreWebView2.WebMessageReceived += (sender, e) =>
                {
                    try
                    {
                        JObject message = JObject.Parse(e.WebMessageAsJson);
                        string messageType = message["type"]?.Value<string>();
                        if (string.Equals(messageType, "closeFlowZoom", StringComparison.Ordinal))
                        {
                            Close();
                        }
                        else if (string.Equals(messageType, "dragFlowZoom", StringComparison.Ordinal))
                        {
                            BeginDragByCaption();
                        }
                    }
                    catch
                    {
                        // 非法消息直接忽略，不影响窗口。
                    }
                };
                webViewReady = true;
                if (!string.IsNullOrEmpty(pendingFlowHtml))
                {
                    NavigateToFlow();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "流程放大窗口初始化失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // 无边框窗口拖动：进入系统标题栏拖动循环，由系统处理移动与边缘吸附。
        private void BeginDragByCaption()
        {
            ReleaseCapture();
            SendMessage(Handle, WMNclButtonDown, HtCaption, 0);
        }

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        private void NavigateToFlow()
        {
            webView.CoreWebView2.NavigateToString(FrmAiAssistant.BuildFlowZoomPageHtml(pendingFlowHtml));
        }
    }
}
