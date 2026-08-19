using Newtonsoft.Json;
// 模块：Bridge / 服务。
// 职责范围：实现 Named Pipe 请求的路由、投影、诊断、预演和事务提交。
// 导航提示：本文件只持有组合状态；请求入口看 Routing，参数规则看 ProtocolSupport，业务实现看对应 partial。

using Newtonsoft.Json.Linq;
using Automation.Protocol;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.IO;

namespace Automation.Bridge
{
    internal sealed partial class AutomationBridgeService
    {
        private const int MaxOverviewOperationCount = 300;
        private const int MaxDetailOperationCount = 100;
        private const int MaxBatchReadOperationCount = 25;
        // 19:45批次实测单轮输入已达5.6万到7.5万token；单个256KB结果足以独占同量级上下文。
        // 详情能力保留，通过摘要、分页和稳定ID精确读取把单次模型结果控制在64KB内。
        private const int MaxProcDetailUtf8Bytes = 64 * 1024;
        private const int MaxBatchReadUtf8Bytes = 64 * 1024;
        private const int MaxStepDetailOperationCount = 100;
        private const int MaxSnapshotPageSize = 100;
        private const int DefaultSnapshotPageSize = 50;
        private const int MaxDiagnosticFindingPageSize = 100;
        private const int DefaultDiagnosticFindingPageSize = 50;
        private const int MaxAuditFindingPageSize = 300;
        private const int DefaultAuditFindingPageSize = 100;
        private const int MaxDiagnosticEvidencePageSize = 100;
        private const int DefaultDiagnosticEvidencePageSize = 40;
        private const int MaxInfoLogCount = 100;
        private const int DefaultInfoLogCount = 30;
        private static readonly LocalFileLogger bridgeErrorLogger = new LocalFileLogger(
            Path.Combine(@"D:\AutomationLogs", "Bridge"));
        private static readonly JsonSerializerSettings migrationContractJsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };

        private readonly FrmMain owner;
        private readonly PlatformRuntime runtime;
        private readonly Func<DateTime> previewUtcNow;
        private readonly TimeSpan previewLifetime;
        private readonly object previewLock = new object();
        private readonly Dictionary<string, PreviewApprovalRecord> previewRecords =
            new Dictionary<string, PreviewApprovalRecord>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, DiagnosticProcIndex> diagnosticIndexes =
            new Dictionary<int, DiagnosticProcIndex>();
        // 用户在预演确认对话框勾选“续建自动确认”后，同一批流程的后续 ChangeSet 预演自动确认；
        // 手动取消任一预演即结束该作用域，超时自动失效。
        private readonly HashSet<string> continuationAutoApproveProcIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private DateTime continuationAutoApproveExpiresUtc = DateTime.MinValue;
        private static readonly TimeSpan ContinuationAutoApproveLifetime = TimeSpan.FromHours(2);

        public AutomationBridgeService(FrmMain owner)
            : this(owner, () => DateTime.UtcNow, TimeSpan.FromMinutes(30))
        {
        }

        internal AutomationBridgeService(
            FrmMain owner,
            Func<DateTime> previewUtcNow,
            TimeSpan previewLifetime)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            this.previewUtcNow = previewUtcNow ?? throw new ArgumentNullException(nameof(previewUtcNow));
            if (previewLifetime <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(previewLifetime));
            }
            this.previewLifetime = previewLifetime;
            runtime = owner.Runtime;
        }

        /// <summary>
        /// 流程被 AI 改动提交后，按流程/步骤/指令的精确定位通知编辑器：
        /// 流程树始终闪烁被改流程/步骤节点（未展开时先展开并滚动到位），
        /// 指令表仅在用户当前浏览的流程命中时闪烁对应指令行。
        /// 颜色由 kind 决定：Modified=橙黄、Added=浅绿、Deleted=浅红。
        /// </summary>
        private void NotifyProcessChanges(IReadOnlyList<ProcessChangeNotice> notices)
        {
            if (notices == null || notices.Count == 0)
            {
                return;
            }
            foreach (ProcessChangeNotice notice in notices)
            {
                diagnosticIndexes.Remove(notice.ProcIndex);
            }
            runtime.EditorUi?.NotifyProcessChanged(notices);
        }

        [System.Diagnostics.DebuggerNonUserCode]
        private sealed class BridgeRequestException : Exception
        {
            public BridgeRequestException(int statusCode, string code, string message, string details = null)
                : base(message)
            {
                StatusCode = statusCode;
                Code = code;
                Details = details;
            }

            public int StatusCode { get; }

            public string Code { get; }

            public string Details { get; }
        }

        private sealed class PreviewApprovalRecord
        {
            public string PreviewId { get; set; }

            public JObject Patch { get; set; }

            public DateTime CreatedAtUtc { get; set; }

            public DateTime ExpiresAtUtc { get; set; }

            public bool Confirmed { get; set; }

            public bool Rejected { get; set; }

            public bool IsChangeSetPreview { get; set; }

            // 前台用户通过确认对话框/HTTP 回调确认（区别于自动批准）；
            // 已由用户确认的冻结预演不得再被替换或追加修正。
            public bool ConfirmedByForeground { get; set; }

            // 由“续建自动确认”作用域自动确认（用户已在首阶段授权同一批流程）。
            public bool ContinuationAutoApproved { get; set; }

            public AiChangeSetCompileResult AiChangeSetPreview { get; set; }

            public MigrationConfigurationPreview MigrationConfigurationPreview { get; set; }

            public string BaseStateHash { get; set; }

            public DateTime? ConfirmedAtUtc { get; set; }
        }

        private sealed class CommReferenceCatalog
        {
            public List<string> All { get; set; } = new List<string>();

            public List<string> Tcp { get; set; } = new List<string>();

            public List<string> Serial { get; set; } = new List<string>();
        }
    }
}
