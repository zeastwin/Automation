// 模块：编辑器 / 外壳。
// 职责范围：页面装配、菜单、工具栏、导航、生命周期和程序设置。
// 状态所有权：窗体协作和当前选择从 Workspace 获取；Attach 前访问失败表示构造顺序错误，不应改成静态兜底。

using System;
using System.Collections.Generic;

namespace Automation
{
    internal interface IEditorWorkspaceParticipant
    {
        void AttachEditorWorkspace(EditorWorkspace workspace);
    }

    /// <summary>
    /// 平台编辑器的窗体组合根。窗体之间通过同一实例协作，不读取全局静态窗体。
    /// </summary>
    internal sealed class EditorWorkspace
    {
        public FrmMain Main { get; }
        public FrmDataGrid DataGrid { get; }
        public FrmMenu Menu { get; }
        public FrmProc Proc { get; }
        public FrmInspector Inspector { get; }
        public FrmToolBar ToolBar { get; }
        public FrmValue Value { get; }
        public FrmValueDebug ValueDebug { get; }
        public FrmAiAssistant AiAssistant { get; }
        public FrmIO IO { get; }
        public FrmCard Card { get; }
        public FrmControl Control { get; }
        public FrmStation Station { get; }
        public FrmDataStruct DataStruct { get; }
        public FrmIODebug IODebug { get; }
        public FrmCommunication Communication { get; }
        public FrmState State { get; }
        public FrmAlarmConfig AlarmConfig { get; }
        public FrmSearch Search { get; }
        public FrmSearch4Value Search4Value { get; }
        public FrmInfo Info { get; }
        public FrmPlc Plc { get; private set; }
        public FrmVersionManager VersionManager { get; private set; }
        public PlatformRuntime Runtime { get; }
        public ProcessEditorSelectionState ProcessSelection { get; }
        public IReadOnlyList<Proc> ProcessDefinitions => Runtime.Stores.Processes.Items;
        public int CurrentPage { get; set; }

        public EditorWorkspace(FrmMain main, PlatformRuntime runtime)
        {
            Main = main ?? throw new ArgumentNullException(nameof(main));
            Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            DataGrid = main.frmDataGrid;
            Menu = main.frmMenu;
            Proc = main.frmProc;
            ProcessSelection = Proc.SelectionState;
            Inspector = main.frmInspector;
            ToolBar = main.frmToolBar;
            Value = main.frmValue;
            ValueDebug = main.frmValueDebug;
            AiAssistant = main.frmAiAssistant;
            IO = main.frmIO;
            Card = main.frmCard;
            Control = main.frmControl;
            Station = main.frmStation;
            DataStruct = main.frmdataStruct;
            IODebug = main.frmIODebug;
            Communication = main.frmCommunication;
            State = main.frmState;
            AlarmConfig = main.frmAlarmConfig;
            Search = main.frmSearch;
            Search4Value = main.frmSearch4Value;
            Info = main.frmInfo;
            Plc = main.frmPlc;

            Attach(DataGrid);
            Attach(Menu);
            Attach(Proc);
            Attach(Inspector);
            Attach(ToolBar);
            Attach(Value);
            Attach(ValueDebug);
            Attach(AiAssistant);
            Attach(IO);
            Attach(Card);
            Attach(Control);
            Attach(Station);
            Attach(DataStruct);
            Attach(IODebug);
            Attach(Communication);
            Attach(State);
            Attach(AlarmConfig);
            Attach(Search);
            Attach(Search4Value);
            Attach(Info);
            Attach(Plc);
        }

        public FrmPlc GetOrCreatePlc()
        {
            if (Plc == null || Plc.IsDisposed)
            {
                Plc = new FrmPlc();
                Main.frmPlc = Plc;
                Attach(Plc);
            }
            return Plc;
        }

        public FrmVersionManager GetOrCreateVersionManager()
        {
            if (VersionManager == null || VersionManager.IsDisposed)
            {
                VersionManager = new FrmVersionManager();
                Attach(VersionManager);
            }
            return VersionManager;
        }

        private void Attach(IEditorWorkspaceParticipant participant)
        {
            participant?.AttachEditorWorkspace(this);
        }
    }

    internal static class EditorWorkspaceGuard
    {
        public static EditorWorkspace Require(EditorWorkspace workspace, string participantName)
        {
            return workspace ?? throw new InvalidOperationException(
                $"{participantName}尚未挂接平台编辑器工作区。");
        }
    }

    public partial class FrmDataGrid : IEditorWorkspaceParticipant
    {
        private EditorWorkspace editorWorkspace;
        internal EditorWorkspace Workspace => EditorWorkspaceGuard.Require(editorWorkspace, nameof(FrmDataGrid));
        void IEditorWorkspaceParticipant.AttachEditorWorkspace(EditorWorkspace workspace) => editorWorkspace = workspace;
    }

    public partial class FrmMenu : IEditorWorkspaceParticipant
    {
        private EditorWorkspace editorWorkspace;
        internal EditorWorkspace Workspace => EditorWorkspaceGuard.Require(editorWorkspace, nameof(FrmMenu));
        void IEditorWorkspaceParticipant.AttachEditorWorkspace(EditorWorkspace workspace) => editorWorkspace = workspace;
    }

    public partial class FrmProc : IEditorWorkspaceParticipant
    {
        private EditorWorkspace editorWorkspace;
        internal EditorWorkspace Workspace => EditorWorkspaceGuard.Require(editorWorkspace, nameof(FrmProc));
        void IEditorWorkspaceParticipant.AttachEditorWorkspace(EditorWorkspace workspace) => editorWorkspace = workspace;
    }

    public sealed partial class FrmInspector : IEditorWorkspaceParticipant
    {
        private EditorWorkspace editorWorkspace;
        internal EditorWorkspace Workspace => EditorWorkspaceGuard.Require(editorWorkspace, nameof(FrmInspector));
        void IEditorWorkspaceParticipant.AttachEditorWorkspace(EditorWorkspace workspace) => editorWorkspace = workspace;
    }

    public partial class FrmToolBar : IEditorWorkspaceParticipant
    {
        private EditorWorkspace editorWorkspace;
        internal EditorWorkspace Workspace => EditorWorkspaceGuard.Require(editorWorkspace, nameof(FrmToolBar));
        void IEditorWorkspaceParticipant.AttachEditorWorkspace(EditorWorkspace workspace)
        {
            editorWorkspace = workspace;
            OnEditorWorkspaceAttached();
        }
    }

    public partial class FrmValue : IEditorWorkspaceParticipant
    {
        private EditorWorkspace editorWorkspace;
        internal EditorWorkspace Workspace => EditorWorkspaceGuard.Require(editorWorkspace, nameof(FrmValue));
        void IEditorWorkspaceParticipant.AttachEditorWorkspace(EditorWorkspace workspace)
        {
            editorWorkspace = workspace;
            OnEditorWorkspaceAttached();
        }
    }

    public partial class FrmValueDebug : IEditorWorkspaceParticipant
    {
        private EditorWorkspace editorWorkspace;
        internal EditorWorkspace Workspace => EditorWorkspaceGuard.Require(editorWorkspace, nameof(FrmValueDebug));
        void IEditorWorkspaceParticipant.AttachEditorWorkspace(EditorWorkspace workspace) => editorWorkspace = workspace;
    }

    public sealed partial class FrmAiAssistant : IEditorWorkspaceParticipant
    {
        private EditorWorkspace editorWorkspace;
        internal EditorWorkspace Workspace => EditorWorkspaceGuard.Require(editorWorkspace, nameof(FrmAiAssistant));
        void IEditorWorkspaceParticipant.AttachEditorWorkspace(EditorWorkspace workspace) => editorWorkspace = workspace;
    }

    public partial class FrmIO : IEditorWorkspaceParticipant
    {
        private EditorWorkspace editorWorkspace;
        internal EditorWorkspace Workspace => EditorWorkspaceGuard.Require(editorWorkspace, nameof(FrmIO));
        void IEditorWorkspaceParticipant.AttachEditorWorkspace(EditorWorkspace workspace) => editorWorkspace = workspace;
    }

    public partial class FrmCard : IEditorWorkspaceParticipant
    {
        private EditorWorkspace editorWorkspace;
        internal EditorWorkspace Workspace => EditorWorkspaceGuard.Require(editorWorkspace, nameof(FrmCard));
        void IEditorWorkspaceParticipant.AttachEditorWorkspace(EditorWorkspace workspace) => editorWorkspace = workspace;
    }

    public partial class FrmControl : IEditorWorkspaceParticipant
    {
        private EditorWorkspace editorWorkspace;
        internal EditorWorkspace Workspace => EditorWorkspaceGuard.Require(editorWorkspace, nameof(FrmControl));
        void IEditorWorkspaceParticipant.AttachEditorWorkspace(EditorWorkspace workspace)
        {
            editorWorkspace = workspace;
            OnEditorWorkspaceAttached();
        }
    }

    public partial class FrmStation : IEditorWorkspaceParticipant
    {
        private EditorWorkspace editorWorkspace;
        internal EditorWorkspace Workspace => EditorWorkspaceGuard.Require(editorWorkspace, nameof(FrmStation));
        void IEditorWorkspaceParticipant.AttachEditorWorkspace(EditorWorkspace workspace) => editorWorkspace = workspace;
    }

    public partial class FrmDataStruct : IEditorWorkspaceParticipant
    {
        private EditorWorkspace editorWorkspace;
        internal EditorWorkspace Workspace => EditorWorkspaceGuard.Require(editorWorkspace, nameof(FrmDataStruct));
        void IEditorWorkspaceParticipant.AttachEditorWorkspace(EditorWorkspace workspace) => editorWorkspace = workspace;
    }

    public partial class FrmIODebug : IEditorWorkspaceParticipant
    {
        private EditorWorkspace editorWorkspace;
        internal EditorWorkspace Workspace => EditorWorkspaceGuard.Require(editorWorkspace, nameof(FrmIODebug));
        void IEditorWorkspaceParticipant.AttachEditorWorkspace(EditorWorkspace workspace)
        {
            editorWorkspace = workspace;
            OnEditorWorkspaceAttached();
        }
    }

    public partial class FrmCommunication : IEditorWorkspaceParticipant
    {
        private EditorWorkspace editorWorkspace;
        internal EditorWorkspace Workspace => EditorWorkspaceGuard.Require(editorWorkspace, nameof(FrmCommunication));
        void IEditorWorkspaceParticipant.AttachEditorWorkspace(EditorWorkspace workspace) => editorWorkspace = workspace;
    }

    public partial class FrmState : IEditorWorkspaceParticipant
    {
        private EditorWorkspace editorWorkspace;
        internal EditorWorkspace Workspace => EditorWorkspaceGuard.Require(editorWorkspace, nameof(FrmState));
        void IEditorWorkspaceParticipant.AttachEditorWorkspace(EditorWorkspace workspace) => editorWorkspace = workspace;
    }

    public partial class FrmAlarmConfig : IEditorWorkspaceParticipant
    {
        private EditorWorkspace editorWorkspace;
        internal EditorWorkspace Workspace => EditorWorkspaceGuard.Require(editorWorkspace, nameof(FrmAlarmConfig));
        void IEditorWorkspaceParticipant.AttachEditorWorkspace(EditorWorkspace workspace) => editorWorkspace = workspace;
    }

    public partial class FrmSearch : IEditorWorkspaceParticipant
    {
        private EditorWorkspace editorWorkspace;
        internal EditorWorkspace Workspace => EditorWorkspaceGuard.Require(editorWorkspace, nameof(FrmSearch));
        void IEditorWorkspaceParticipant.AttachEditorWorkspace(EditorWorkspace workspace) => editorWorkspace = workspace;
    }

    public partial class FrmSearch4Value : IEditorWorkspaceParticipant
    {
        private EditorWorkspace editorWorkspace;
        internal EditorWorkspace Workspace => EditorWorkspaceGuard.Require(editorWorkspace, nameof(FrmSearch4Value));
        void IEditorWorkspaceParticipant.AttachEditorWorkspace(EditorWorkspace workspace) => editorWorkspace = workspace;
    }

    public partial class FrmInfo : IEditorWorkspaceParticipant
    {
        private EditorWorkspace editorWorkspace;
        internal EditorWorkspace Workspace => EditorWorkspaceGuard.Require(editorWorkspace, nameof(FrmInfo));
        void IEditorWorkspaceParticipant.AttachEditorWorkspace(EditorWorkspace workspace) => editorWorkspace = workspace;
    }

    public sealed partial class FrmPlc : IEditorWorkspaceParticipant
    {
        private EditorWorkspace editorWorkspace;
        internal EditorWorkspace Workspace => EditorWorkspaceGuard.Require(editorWorkspace, nameof(FrmPlc));
        void IEditorWorkspaceParticipant.AttachEditorWorkspace(EditorWorkspace workspace) => editorWorkspace = workspace;
    }

    public sealed partial class FrmVersionManager : IEditorWorkspaceParticipant
    {
        private EditorWorkspace editorWorkspace;
        internal EditorWorkspace Workspace => EditorWorkspaceGuard.Require(editorWorkspace, nameof(FrmVersionManager));
        void IEditorWorkspaceParticipant.AttachEditorWorkspace(EditorWorkspace workspace) => editorWorkspace = workspace;
    }
}
