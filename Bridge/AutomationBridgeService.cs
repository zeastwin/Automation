using Newtonsoft.Json;
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
        private readonly object previewLock = new object();
        private readonly Dictionary<string, PreviewApprovalRecord> previewRecords =
            new Dictionary<string, PreviewApprovalRecord>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, DiagnosticProcIndex> diagnosticIndexes =
            new Dictionary<int, DiagnosticProcIndex>();

        public AutomationBridgeService(FrmMain owner)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            runtime = owner.Runtime;
        }

        /// <summary>
        /// 流程被 AI 改动后，在 FrmProc 节点和 FrmDataGrid 上触发闪烁动效，让用户直观看到改动位置。
        /// kind 决定颜色：Modified=橙黄、Added=浅绿、Deleted=浅红。
        /// FrmDataGrid 仅在用户当前正在浏览的流程 == 被改动的流程时才闪烁，
        /// 避免用户在查看别的流程时被打扰；FrmProc 节点闪烁不受影响（始终提示哪个流程被改）。
        /// affectedOps 非空时走行级闪烁（只闪被改动的指令行），为空时走整体闪烁。
        /// </summary>
        private void NotifyProcChanged(int procIndex, ProcChangeKind kind, List<(int stepIndex, int opIndex, ProcChangeKind kind)> affectedOps = null)
        {
            diagnosticIndexes.Remove(procIndex);
            runtime.EditorUi?.NotifyProcessChanged(procIndex, kind, affectedOps);
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

            public string PatchHash { get; set; }

            public int ProcIndex { get; set; }

            public string BaseProcId { get; set; }

            public DateTime CreatedAtUtc { get; set; }

            public DateTime ExpiresAtUtc { get; set; }

            public bool Confirmed { get; set; }

            public bool Rejected { get; set; }

            public bool IsChangeSetPreview { get; set; }

            public string ReplacedPreviewId { get; set; }

            public Proc DraftProc { get; set; }

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
