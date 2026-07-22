using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace Automation
{
    public partial class FrmMain
    {
        private void EditorTreeSelectionChanged(object sender, TreeViewEventArgs e)
        {
            if (e.Action != TreeViewAction.Unknown)
            {
                RecordCurrentEditorLocation();
            }
        }

        private void EditorOperationListMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left
                && frmDataGrid.dataGridView1.IndexFromPoint(e.Location) >= 0)
            {
                RecordCurrentEditorLocation();
            }
        }

        private void EditorOperationListKeyUp(object sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Up:
                case Keys.Down:
                case Keys.Home:
                case Keys.End:
                case Keys.PageUp:
                case Keys.PageDown:
                    RecordCurrentEditorLocation();
                    break;
            }
        }

        private void RecordCurrentEditorLocation()
        {
            if (editorNavigationChanging)
            {
                return;
            }
            EditorNavigationLocation location = CaptureCurrentEditorLocation();
            if (location == null)
            {
                return;
            }
            if (currentEditorLocation == null)
            {
                currentEditorLocation = location;
                UpdateEditorNavigationActions();
                return;
            }
            if (currentEditorLocation.SameAs(location))
            {
                return;
            }
            PushUnique(editorBackHistory, currentEditorLocation);
            currentEditorLocation = location;
            editorForwardHistory.Clear();
            UpdateEditorNavigationActions();
        }

        private EditorNavigationLocation CaptureCurrentEditorLocation()
        {
            int procIndex = frmProc?.SelectedProcNum ?? -1;
            if (frmProc?.procsList == null
                || procIndex < 0
                || procIndex >= frmProc.procsList.Count)
            {
                return null;
            }
            Proc proc = frmProc.procsList[procIndex];
            Guid procId = proc?.head?.Id ?? Guid.Empty;
            if (procId == Guid.Empty)
            {
                return null;
            }
            int stepIndex = frmProc.SelectedStepNum;
            Step step = stepIndex >= 0 && stepIndex < (proc.steps?.Count ?? 0)
                ? proc.steps[stepIndex]
                : null;
            OperationType operation = null;
            int operationIndex = frmDataGrid?.dataGridView1?.CurrentIndex ?? -1;
            if (step != null
                && operationIndex >= 0
                && operationIndex < (step.Ops?.Count ?? 0))
            {
                operation = step.Ops[operationIndex];
            }
            return new EditorNavigationLocation
            {
                ProcId = procId,
                StepId = step?.Id ?? Guid.Empty,
                OpId = operation?.Id ?? Guid.Empty
            };
        }

        private static void PushUnique(
            Stack<EditorNavigationLocation> history,
            EditorNavigationLocation location)
        {
            if (location != null && (history.Count == 0 || !history.Peek().SameAs(location)))
            {
                history.Push(location);
            }
        }

        internal bool NavigateEditorBack()
        {
            return NavigateEditorHistory(editorBackHistory, editorForwardHistory);
        }

        internal bool NavigateEditorForward()
        {
            return NavigateEditorHistory(editorForwardHistory, editorBackHistory);
        }

        private bool NavigateEditorHistory(
            Stack<EditorNavigationLocation> sourceHistory,
            Stack<EditorNavigationLocation> destinationHistory)
        {
            if (!CanUseEditorNavigation())
            {
                return false;
            }
            EditorNavigationLocation source = CaptureCurrentEditorLocation() ?? currentEditorLocation;
            while (sourceHistory.Count > 0)
            {
                EditorNavigationLocation target = sourceHistory.Pop();
                if (target == null || target.SameAs(source))
                {
                    continue;
                }
                if (!TryNavigateToEditorLocation(target))
                {
                    continue;
                }
                PushUnique(destinationHistory, source);
                currentEditorLocation = CaptureCurrentEditorLocation() ?? target;
                UpdateEditorNavigationActions();
                return true;
            }
            UpdateEditorNavigationActions();
            return false;
        }

        private bool CanUseEditorNavigation()
        {
            return !IsDisposed
                && !Disposing
                && Runtime.Editor.ActiveSession == null
                && workspacePageHost != null
                && ReferenceEquals(workspacePageHost.ActivePage, editorWorkspacePage)
                && Form.ActiveForm == this;
        }

        private bool NavigateToEditorLocation(EditorNavigationLocation target, bool recordSource)
        {
            if (target == null || Runtime.Editor.ActiveSession != null)
            {
                return false;
            }
            EditorNavigationLocation source = CaptureCurrentEditorLocation() ?? currentEditorLocation;
            if (!TryNavigateToEditorLocation(target))
            {
                return false;
            }
            if (recordSource && source != null && !source.SameAs(target))
            {
                PushUnique(editorBackHistory, source);
                editorForwardHistory.Clear();
            }
            currentEditorLocation = CaptureCurrentEditorLocation() ?? target;
            UpdateEditorNavigationActions();
            return true;
        }

        private bool TryNavigateToEditorLocation(EditorNavigationLocation target)
        {
            if (!TryResolveEditorLocation(target, out int procIndex, out int stepIndex))
            {
                return false;
            }
            editorNavigationChanging = true;
            try
            {
                ShowEditorWorkspace();
                frmProc.SelectAiContext(procIndex, stepIndex);
                if (target.OpId != Guid.Empty)
                {
                    if (!frmDataGrid.SelectOperationForNavigation(target.OpId))
                    {
                        return false;
                    }
                    frmDataGrid.dataGridView1.Focus();
                }
                else
                {
                    frmDataGrid.iSelectedRow = -1;
                    frmDataGrid.dataGridView1.ClearSelection();
                    frmProc.proc_treeView.Focus();
                }
                return true;
            }
            finally
            {
                editorNavigationChanging = false;
            }
        }

        private bool TryResolveEditorLocation(
            EditorNavigationLocation location,
            out int procIndex,
            out int stepIndex)
        {
            procIndex = -1;
            stepIndex = -1;
            if (location == null || location.ProcId == Guid.Empty || frmProc?.procsList == null)
            {
                return false;
            }
            procIndex = frmProc.procsList.FindIndex(proc => proc?.head?.Id == location.ProcId);
            if (procIndex < 0)
            {
                return false;
            }
            if (location.StepId == Guid.Empty)
            {
                return location.OpId == Guid.Empty;
            }
            Proc proc = frmProc.procsList[procIndex];
            stepIndex = proc.steps?.FindIndex(step => step?.Id == location.StepId) ?? -1;
            if (stepIndex < 0)
            {
                return false;
            }
            return location.OpId == Guid.Empty
                || proc.steps[stepIndex].Ops?.Any(operation => operation?.Id == location.OpId) == true;
        }

        private void UpdateEditorNavigationActions()
        {
            bool navigationEnabled = Runtime.Editor.ActiveSession == null;
            frmToolBar?.SetNavigationAvailability(
                navigationEnabled && editorBackHistory.Count > 0,
                navigationEnabled && editorForwardHistory.Count > 0);
        }

        internal void RefreshEditorNavigationActions()
        {
            UpdateEditorNavigationActions();
        }

        public void ShowWorkspacePage(Form page)
        {
            if (page == null || page.IsDisposed)
            {
                return;
            }
            if (!workspacePageHost.Controls.Contains(page))
            {
                page.FormBorderStyle = FormBorderStyle.None;
                page.ShowIcon = false;
                page.ShowInTaskbar = false;
                page.TopLevel = false;
                page.Dock = DockStyle.Fill;
                workspacePageHost.Controls.Add(page);
                page.Show();
            }
            workspacePageHost.ShowPage(page);
            page.Focus();
        }

        public void ShowProcessFlowGraph()
        {
            if (flowGraphUnavailable)
            {
                MessageBox.Show(this, "流程图模块当前不可用，请查看运行信息中的报警。", "流程图", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try
            {
                if (frmProcessFlow == null || frmProcessFlow.IsDisposed)
                {
                    frmProcessFlow = new FrmProcessFlow(this);
                }
                ShowWorkspacePage(frmProcessFlow);
                frmProcessFlow.ShowProjectGraph();
            }
            catch (Exception ex)
            {
                flowGraphUnavailable = true;
                string message = "流程图模块初始化失败，平台其他功能继续可用：" + ex.Message;
                dataRun?.Logger?.Log(message, LogLevel.Error);
                frmInfo?.PrintInfo(message, FrmInfo.Level.Error);
                MessageBox.Show(this, message, "流程图", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        public void RefreshProcessFlowGraph()
        {
            if (frmProcessFlow != null && !frmProcessFlow.IsDisposed && frmProcessFlow.Visible)
            {
                frmProcessFlow.RefreshCurrentGraph();
            }
        }

        public void ShowDataBreakpoints()
        {
            if (frmDataBreakpoints == null || frmDataBreakpoints.IsDisposed)
            {
                frmDataBreakpoints = new FrmDataBreakpoints(this, dataBreakpointService);
                frmDataBreakpoints.FormClosed += (sender, args) => frmDataBreakpoints = null;
            }
            if (!frmDataBreakpoints.Visible)
            {
                frmDataBreakpoints.Show(this);
            }
            if (frmDataBreakpoints.WindowState == FormWindowState.Minimized)
            {
                frmDataBreakpoints.WindowState = FormWindowState.Normal;
            }
            frmDataBreakpoints.BringToFront();
            frmDataBreakpoints.Activate();
        }

        public bool NavigateToFlowOperation(Guid procId, Guid stepId, Guid opId)
        {
            return NavigateToEditorLocation(new EditorNavigationLocation
            {
                ProcId = procId,
                StepId = stepId,
                OpId = opId
            }, true);
        }

        internal bool NavigateToDataBreakpointTrigger(DataBreakpointHit hit, out string error)
        {
            error = null;
            if (hit == null)
            {
                error = "断点命中数据为空。";
                return false;
            }
            if (hit.TriggerProcId == Guid.Empty)
            {
                error = $"触发源来自“{hit.TriggerDescription}”，没有可定位的流程指令位置。";
                return false;
            }
            bool navigated = NavigateToEditorLocation(new EditorNavigationLocation
            {
                ProcId = hit.TriggerProcId,
                StepId = hit.TriggerStepId,
                OpId = hit.TriggerOperationId
            }, true);
            if (!navigated)
            {
                error = "触发源对应的流程、步骤或指令已经不存在，无法定位。";
                return false;
            }
            Activate();
            return true;
        }

    }
}
