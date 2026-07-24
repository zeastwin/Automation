// 模块：编辑器 / 外壳。
// 职责范围：页面装配、菜单、工具栏、导航、生命周期和程序设置。
// 文件职责：维护编辑器页面导航和流程对象定位。

using System;
using System.Collections.Generic;
using System.Drawing;
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
                uiWarmupCoordinator.NotifyInteraction();
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
            uiWarmupCoordinator.NotifyInteraction();
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

        internal void ToggleWorkspacePageWindow(Form page)
        {
            if (!IsDetachableWorkspacePage(page) || page.IsDisposed)
            {
                return;
            }
            if (page.TopLevel && page.Visible)
            {
                AttachWorkspacePage(page, true);
                return;
            }
            DetachWorkspacePage(page);
        }

        internal void ReattachWorkspacePageAfterClose(Form page)
        {
            if (IsDetachableWorkspacePage(page) && page.TopLevel && !page.IsDisposed)
            {
                AttachWorkspacePage(page, false);
            }
        }

        internal bool TryActivateDetachedWorkspacePage(Form page)
        {
            if (!IsDetachableWorkspacePage(page) || !page.TopLevel || !page.Visible || page.IsDisposed)
            {
                return false;
            }
            if (page.WindowState == FormWindowState.Minimized)
            {
                page.WindowState = FormWindowState.Normal;
            }
            page.Activate();
            page.BringToFront();
            return true;
        }

        private bool IsDetachableWorkspacePage(Form page)
        {
            return ReferenceEquals(page, frmValue) || ReferenceEquals(page, frmIODebug);
        }

        private void DetachWorkspacePage(Form page)
        {
            page.Hide();
            bool wasActive = workspacePageHost.ReleasePage(page);
            page.Dock = DockStyle.None;
            page.TopLevel = true;
            page.FormBorderStyle = FormBorderStyle.Sizable;
            page.ShowIcon = true;
            page.ShowInTaskbar = true;
            page.MaximizeBox = true;
            page.MinimizeBox = true;
            page.StartPosition = FormStartPosition.Manual;
            page.Text = ReferenceEquals(page, frmValue) ? "变量" : "I/O 调试";

            Rectangle workingArea = Screen.FromControl(this).WorkingArea;
            int maximumWidth = Math.Max(1, workingArea.Width - 80);
            int maximumHeight = Math.Max(1, workingArea.Height - 80);
            int preferredWidth = Math.Min(1440, workingArea.Width * 4 / 5);
            int preferredHeight = Math.Min(810, workingArea.Height * 4 / 5);
            int detachedWidth = Math.Min(maximumWidth, Math.Max(page.MinimumSize.Width, preferredWidth));
            int detachedHeight = Math.Min(maximumHeight, Math.Max(page.MinimumSize.Height, preferredHeight));
            page.Bounds = new Rectangle(
                workingArea.Left + Math.Max(0, (workingArea.Width - detachedWidth) / 2),
                workingArea.Top + Math.Max(0, (workingArea.Height - detachedHeight) / 2),
                detachedWidth,
                detachedHeight);
            SetWorkspacePageDetachedState(page, true);
            page.Show(this);
            page.Activate();

            if (wasActive)
            {
                frmMenu.ShowProcessWorkspace();
            }
        }

        private void AttachWorkspacePage(Form page, bool activate)
        {
            page.Hide();
            page.Owner = null;
            page.WindowState = FormWindowState.Normal;
            page.Dock = DockStyle.None;
            page.FormBorderStyle = FormBorderStyle.None;
            page.ShowIcon = false;
            page.ShowInTaskbar = false;
            page.TopLevel = false;
            page.Dock = DockStyle.Fill;
            if (!workspacePageHost.Controls.Contains(page))
            {
                workspacePageHost.Controls.Add(page);
            }
            page.Visible = false;
            SetWorkspacePageDetachedState(page, false);
            if (activate)
            {
                frmMenu.ShowDetachableWorkspacePage(page);
            }
        }

        private static void SetWorkspacePageDetachedState(Form page, bool detached)
        {
            if (page is FrmValue valuePage)
            {
                valuePage.SetWorkspaceDetached(detached);
            }
            else if (page is FrmIODebug ioDebugPage)
            {
                ioDebugPage.SetWorkspaceDetached(detached);
            }
        }

        public void ShowWorkspacePage(Form page)
        {
            if (page == null || page.IsDisposed)
            {
                return;
            }
            if (TryActivateDetachedWorkspacePage(page))
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
            }
            workspacePageHost.ShowPage(page);
            page.Focus();
            if (page is FrmValue valuePage)
            {
                valuePage.RefreshFromUserActivation();
            }
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

        internal void PrewarmProcessFlowGraphs()
        {
            if (frmProcessFlow == null || frmProcessFlow.IsDisposed)
            {
                if (platformInitialized && !flowGraphUnavailable)
                {
                    QueueProcessFlowHostPrewarm();
                }
                return;
            }
            foreach (Proc process in Runtime.Stores.Processes.Items.ToList())
            {
                Guid procId = process?.head?.Id ?? Guid.Empty;
                if (procId != Guid.Empty)
                {
                    ScheduleProcessFlowGraphPrewarm(procId);
                }
            }
        }

        internal void PrewarmProcessFlowGraph(int procIndex)
        {
            if (procIndex < 0
                || procIndex >= Runtime.Stores.Processes.Items.Count)
            {
                return;
            }
            Guid procId = Runtime.Stores.Processes.Items[procIndex]?.head?.Id
                ?? Guid.Empty;
            if (procId == Guid.Empty)
            {
                return;
            }
            ScheduleProcessFlowGraphPrewarm(procId);
        }

        private void ScheduleProcessFlowGraphPrewarm(Guid procId)
        {
            uiWarmupCoordinator.Schedule(
                "process-flow:" + procId.ToString("N"),
                30,
                () =>
                {
                    if (frmProcessFlow == null || frmProcessFlow.IsDisposed)
                    {
                        return;
                    }
                    int currentIndex = Runtime.Stores.Processes.Items
                        .FindIndex(process => process?.head?.Id == procId);
                    if (currentIndex >= 0)
                    {
                        frmProcessFlow.PrewarmProcessGraph(currentIndex);
                    }
                });
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
