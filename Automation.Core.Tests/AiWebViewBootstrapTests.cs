using System;
// 模块：核心测试 / AI 前台。
// 职责范围：验证聊天 WebView 初始化、页面导航和可交互状态桥接的最小链路。

using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Automation.Core.Tests
{
    [TestClass]
    [DoNotParallelize]
    public sealed class AiWebViewBootstrapTests
    {
        [TestMethod]
        public void FinalAnswerReveal_UsesShortCompositedAnimationWithoutCharacterReplay()
        {
            string html = typeof(FrmAiAssistant)
                .GetField(
                    "BaseConversationHtml",
                    BindingFlags.Static | BindingFlags.NonPublic)
                ?.GetValue(null) as string;

            Assert.IsFalse(string.IsNullOrWhiteSpace(html));
            StringAssert.Contains(html, "final-answer-card-in");
            StringAssert.Contains(html, "final-answer-sheen");
            StringAssert.Contains(html, "prefers-reduced-motion:reduce");
            StringAssert.Contains(html, "content.children,0,8");
            Assert.IsFalse(html.Contains("document.createTreeWalker"),
                "最终回答不得清空文本后逐字符回放。");
            Assert.IsFalse(html.Contains("typing-glint"),
                "最终回答不再使用廉价的逐字闪光光标。");
            Assert.AreEqual(
                1,
                html.Split(new[] { "function revealFinalAnswer(message)" },
                    StringSplitOptions.None).Length - 1,
                "最终回答动画入口应保持唯一。");
        }

        [TestMethod]
        public void StandardTests_ExposeEditablePromptAndPersistenceControls()
        {
            string html = typeof(FrmAiAssistant)
                .GetField(
                    "BaseConversationHtml",
                    BindingFlags.Static | BindingFlags.NonPublic)
                ?.GetValue(null) as string;

            Assert.IsFalse(string.IsNullOrWhiteSpace(html));
            StringAssert.Contains(html, "className='test-prompt'");
            StringAssert.Contains(html, "+ 增加一轮");
            StringAssert.Contains(html, "id=\"saveTestPrompts\"");
            StringAssert.Contains(html, "id=\"resetTestPrompts\"");
            StringAssert.Contains(html, "post('runStandardTests',{scenarios:scenarios");
            StringAssert.Contains(html, "post('saveStandardTestPrompts',{scenarios:scenarios}");
            StringAssert.Contains(html, "post('resetStandardTestPrompts')");
        }

        [TestMethod]
        public void StreamingSegments_UseStableMarkdownSplitWithTypewriterAndBlinkCursor()
        {
            string html = typeof(FrmAiAssistant)
                .GetField(
                    "BaseConversationHtml",
                    BindingFlags.Static | BindingFlags.NonPublic)
                ?.GetValue(null) as string;

            Assert.IsFalse(string.IsNullOrWhiteSpace(html));
            // 流式段结构：稳定 Markdown 槽 + 未完成尾部逐字打字机。
            StringAssert.Contains(html, "function updateStreamSegment");
            StringAssert.Contains(html, "function finalizeStreamSegment");
            StringAssert.Contains(html, "function promoteStreamSegment");
            StringAssert.Contains(html, "stream-raw");
            StringAssert.Contains(html, "cursor-blink");
            // 智能滚动：只有视口位于底部附近才跟随，用户上翻阅读不被打断。
            StringAssert.Contains(html, "clientHeight<140");
        }

        [TestMethod]
        public void SplitStreamingMarkdown_KeepsIncompleteBlocksInRawUntilSafeBoundary()
        {
            var method = typeof(FrmAiAssistant).GetMethod(
                "SplitStreamingMarkdown",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(method);

            // 空行是安全边界：之前已完成的块进入稳定部分，未完成段落逐字显示。
            (string stable, string raw) = InvokeSplit(method, "# 标题\n\n正文段落未结束");
            Assert.AreEqual("# 标题\n\n", stable);
            Assert.AreEqual("正文段落未结束", raw);

            // 未闭合代码围栏整体留在尾部，避免内容反复重排。
            (stable, raw) = InvokeSplit(method, "前文\n\n```\ncode");
            Assert.AreEqual("前文\n\n", stable);
            Assert.AreEqual("```\ncode", raw);

            // 围栏闭合之后边界推进。
            (stable, raw) = InvokeSplit(method, "前文\n\n```\ncode\n```\n尾部");
            Assert.AreEqual("前文\n\n```\ncode\n```\n", stable);
            Assert.AreEqual("尾部", raw);

            // 末行没有换行符时可能仍在增长，不作为稳定边界。
            (stable, raw) = InvokeSplit(method, "# 标题");
            Assert.AreEqual(string.Empty, stable);
            Assert.AreEqual("# 标题", raw);
        }

        private static (string Stable, string Raw) InvokeSplit(
            System.Reflection.MethodInfo method, string value)
        {
            object result = method.Invoke(null, new object[] { value });
            return (
                (string)result.GetType().GetField("Item1")?.GetValue(result),
                (string)result.GetType().GetField("Item2")?.GetValue(result));
        }

        [TestMethod]
        public void FlowVisualization_ZoomOpensFullScreenHostWindowBeyondWebViewViewport()
        {
            // 独立放大页：由宿主最大化窗口承载，横跨整个屏幕而不是助手面板 WebView 视口。
            var zoomPage = typeof(FrmAiAssistant).GetMethod(
                "BuildFlowZoomPageHtml",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(zoomPage);
            string page = zoomPage.Invoke(null, new object[] { "<div class=\"automation-flow-visual\"></div>" }) as string;
            Assert.IsFalse(string.IsNullOrWhiteSpace(page));
            StringAssert.Contains(page, "id=\"flowZoomStage\"");
            StringAssert.Contains(page, "id=\"canvasInner\"");
            StringAssert.Contains(page, "function applyFlowZoom");
            StringAssert.Contains(page, "function flowZoomFit");
            // 画布交互：滚轮锚点缩放、按住拖动平移、双击复位，无滚动条。
            StringAssert.Contains(page, "function flowZoomAt");
            StringAssert.Contains(page, "addEventListener('wheel'");
            StringAssert.Contains(page, "addEventListener('dblclick'");
            StringAssert.Contains(page, "滚轮缩放 · 按住拖动 · 双击复位");
            // 缩放必须是布局级 CSS zoom（矢量重排），禁止 transform:scale 纹理位图放大发糊。
            StringAssert.Contains(page, "style.zoom=flowZoomScale");
            Assert.IsFalse(page.Contains("will-change:"), "不得用合成器纹理缩放（位图拉伸发糊）。");
            Assert.IsFalse(page.Contains("transform-origin"), "不得用 transform 缩放画布。");
            StringAssert.Contains(page, "post('closeFlowZoom')");
            StringAssert.Contains(page, ".automation-flow-visual{");
            Assert.IsFalse(page.Contains("::-webkit-scrollbar"), "画布模式不得保留滚动条样式。");

            // 对话页只负责把卡片 HTML 发给宿主，不再自带弹层。
            string html = typeof(FrmAiAssistant)
                .GetField(
                    "BaseConversationHtml",
                    BindingFlags.Static | BindingFlags.NonPublic)
                ?.GetValue(null) as string;
            Assert.IsFalse(string.IsNullOrWhiteSpace(html));
            StringAssert.Contains(html, "function openFlowZoom");
            StringAssert.Contains(html, "post('openFlowZoom',{html:clone.outerHTML})");
            Assert.IsFalse(html.Contains("flowZoomOverlay"), "助手面板内不得残留旧弹层。");

            // 流程卡片标题栏包含放大入口。
            var method = typeof(FrmAiAssistant).GetMethod(
                "BuildAutomationFlowCardsHtml",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(method);
            string cards = method.Invoke(null, new object[]
            {
                "[{\"action\":\"create\",\"name\":\"P1\",\"steps\":[]}]"
            }) as string;
            StringAssert.Contains(cards, "flow-expand-button");
            StringAssert.Contains(cards, "openFlowZoom(this)");
        }

        [TestMethod]
        [TestCategory("Desktop")]
        [Timeout(30000)]
        public void Show_WhenWebViewRuntimeIsAvailable_EnablesEditorControls()
        {
            StaTestRunner.Run(RunBootstrap, TimeSpan.FromSeconds(25));
        }

        private static void RunBootstrap()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (var form = new FrmAiAssistant
            {
                Opacity = 0,
                ShowInTaskbar = false,
                FormBorderStyle = FormBorderStyle.None
            })
            {
                form.Show();
                DateTime deadline = DateTime.UtcNow.AddSeconds(15);
                object core = WaitForWebViewCore(form, deadline);
                Assert.IsNotNull(core, "AI WebView2 未在15秒内初始化。");

                MethodInfo execute = core.GetType().GetMethod(
                    "ExecuteScriptAsync", new[] { typeof(string) });
                Assert.IsNotNull(execute);
                string result = null;
                while (DateTime.UtcNow < deadline
                    && !string.Equals(result, "true", StringComparison.OrdinalIgnoreCase))
                {
                    object resultTaskObject = execute.Invoke(core, new object[]
                    {
                        "appState.canAccess===true&&appState.canEditConfig===true"
                            + "&&!document.getElementById('promptInput').disabled"
                            + "&&!document.getElementById('standardTestButton').disabled"
                            + "&&!document.getElementById('toolDiagnostic').disabled"
                            + "&&!document.getElementById('toolEditor').disabled"
                    });
                    var task = (Task)resultTaskObject;
                    PumpUntilCompleted(task, deadline);
                    if (!task.IsCompleted)
                    {
                        break;
                    }
                    result = resultTaskObject.GetType().GetProperty("Result")
                        ?.GetValue(resultTaskObject)?.ToString();
                    Application.DoEvents();
                    Thread.Sleep(20);
                }
                Assert.AreEqual("true", result?.ToLowerInvariant(),
                    "AI 页面未收到可交互状态。");
            }
        }

        private static object WaitForWebViewCore(FrmAiAssistant form, DateTime deadline)
        {
            FieldInfo webViewField = typeof(FrmAiAssistant).GetField(
                "webViewConversation", BindingFlags.Instance | BindingFlags.NonPublic);
            while (DateTime.UtcNow < deadline)
            {
                Application.DoEvents();
                Thread.Sleep(20);
                object webView = webViewField?.GetValue(form);
                object core = webView?.GetType().GetProperty("CoreWebView2")?.GetValue(webView);
                if (core != null)
                {
                    return core;
                }
            }
            return null;
        }

        private static void PumpUntilCompleted(Task task, DateTime deadline)
        {
            while (DateTime.UtcNow < deadline && !task.IsCompleted)
            {
                Application.DoEvents();
                Thread.Sleep(20);
            }
        }
    }
}
