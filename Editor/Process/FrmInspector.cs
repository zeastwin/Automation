// 模块：编辑器 / 流程。
// 职责范围：流程导航、指令表、对象选择、搜索和导航。

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Automation
{
    public sealed partial class FrmInspector : Form
    {
        private const int WmSetRedraw = 0x000B;

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(
            IntPtr windowHandle,
            int message,
            IntPtr wordParameter,
            IntPtr longParameter);

        private readonly Panel header = new Panel();
        private readonly Label operationTypeLabel = new Label();
        private readonly InspectorIconButton operationTypeButton = new InspectorIconButton();
        private readonly InspectorView inspectorView = new InspectorView();
        private readonly Panel actionBar = new Panel();
        private readonly IReadOnlyList<OperationType> operationTemplates;
        private ToolStripDropDown activeOperationTypePicker;
        private OperationTypePickerPanel operationTypePickerPanel;
        private ToolStripControlHost operationTypePickerHost;
        private InstantToolStripDropDown operationTypePickerDropDown;
        private Button saveButton;
        private Button cancelButton;
        private bool editing;
        private object presentedObject;
        private int updateDepth;
        private bool redrawSuspended;

        public FrmInspector()
        {
            operationTemplates = OperationDefinitionRegistry.CreateAll();
            InspectorDefinitionBuilder.Prewarm(operationTemplates.Cast<object>());
            InitializeLayout();
            InitializeOperationTypePicker();
            KeyPreview = true;
            AutoValidate = AutoValidate.EnablePreventFocusChange;
            KeyDown += FrmInspector_KeyDown;
            Disposed += (sender, args) =>
            {
                activeOperationTypePicker = null;
                operationTypePickerDropDown?.Dispose();
                operationTypePickerDropDown = null;
                operationTypePickerHost = null;
                operationTypePickerPanel = null;
            };
        }

        public object SelectedObject => inspectorView.SelectedObject;

        public void AttachEditActions(Button saveAction, Button cancelAction)
        {
            if (saveAction == null)
            {
                throw new ArgumentNullException(nameof(saveAction));
            }
            if (cancelAction == null)
            {
                throw new ArgumentNullException(nameof(cancelAction));
            }
            saveButton = saveAction;
            cancelButton = cancelAction;
            ConfigureActionButton(saveButton, true);
            ConfigureActionButton(cancelButton, false);
            actionBar.Controls.Add(saveButton);
            actionBar.Controls.Add(cancelButton);
            AcceptButton = saveButton;
            UpdateActionBar();
            LayoutControls();
        }

        public void ShowObject(object value)
        {
            ShowObject(value, false);
        }

        internal void ShowObject(object value, bool graphAlreadyAttached)
        {
            if (!graphAlreadyAttached)
            {
                EditorServiceRegistry.AttachGraph(value, Workspace.Runtime);
            }
            BeginUpdate();
            try
            {
                editing = IsActiveDraft(value);
                inspectorView.SetObject(value, editing);
                UpdatePresentation(value);
            }
            finally
            {
                EndUpdate();
            }
        }

        public void ClearObject()
        {
            BeginUpdate();
            try
            {
                editing = false;
                inspectorView.SetObject(null, false);
                UpdatePresentation(null);
            }
            finally
            {
                EndUpdate();
            }
        }

        public void SetEditingState(bool allowEdit)
        {
            BeginUpdate();
            try
            {
                editing = allowEdit
                    && IsActiveDraft(inspectorView.SelectedObject);
                inspectorView.SetEditable(editing);
                UpdatePresentation(inspectorView.SelectedObject);
            }
            finally
            {
                EndUpdate();
            }
        }

        public void RefreshObject()
        {
            BeginUpdate();
            try
            {
                inspectorView.RefreshDocument();
                UpdatePresentation(inspectorView.SelectedObject);
            }
            finally
            {
                EndUpdate();
            }
        }

        public void BeginUpdate()
        {
            updateDepth++;
            if (updateDepth != 1)
            {
                return;
            }
            if (IsHandleCreated)
            {
                SendMessage(Handle, WmSetRedraw, IntPtr.Zero, IntPtr.Zero);
                redrawSuspended = true;
            }
            SuspendLayout();
            inspectorView.BeginUpdate();
        }

        public void EndUpdate()
        {
            if (updateDepth <= 0)
            {
                return;
            }
            updateDepth--;
            if (updateDepth != 0)
            {
                return;
            }
            try
            {
                inspectorView.EndUpdate();
                ResumeLayout(false);
                LayoutControls();
                PerformLayout();
            }
            finally
            {
                if (redrawSuspended)
                {
                    if (IsHandleCreated)
                    {
                        SendMessage(
                            Handle,
                            WmSetRedraw,
                            new IntPtr(1),
                            IntPtr.Zero);
                        Invalidate(true);
                    }
                    redrawSuspended = false;
                }
            }
        }

        internal bool TryCommitPendingEdit(out string error)
        {
            error = null;
            if (editorWorkspace?.Runtime?.Editor?.ActiveSession == null)
            {
                return true;
            }
            if (!editing || !IsActiveDraft(inspectorView.SelectedObject))
            {
                error = "检查器中的对象已不是当前编辑草稿，请重新打开编辑。";
                return false;
            }
            if (ValidateChildren(
                ValidationConstraints.Enabled
                    | ValidationConstraints.Visible))
            {
                return true;
            }
            error = "部分字段尚未通过输入校验，请修正红色提示后再保存。";
            return false;
        }

        private bool IsActiveDraft(object value)
        {
            return value != null
                && editorWorkspace?.Runtime?.Editor?.ActiveSession != null
                && ReferenceEquals(
                    editorWorkspace.Runtime.Editor.ActiveSession.Draft,
                    value);
        }

        private void InitializeLayout()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = UiPalette.SurfaceStrong;
            Font = InspectorFonts.Regular9;
            MinimumSize = new Size(320, 320);
            Text = "配置检查器";

            header.BackColor = UiPalette.SurfaceStrong;
            header.Dock = DockStyle.None;
            header.Height = 0;
            header.Visible = false;
            header.Paint += (sender, args) =>
            {
                using (var pen = new Pen(UiPalette.Stroke))
                {
                    args.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
                }
            };
            Controls.Add(header);

            operationTypeLabel.AutoEllipsis = true;
            operationTypeLabel.BackColor = UiPalette.SurfaceStrong;
            operationTypeLabel.BorderStyle = BorderStyle.None;
            operationTypeLabel.Font = InspectorFonts.Bold9;
            operationTypeLabel.ForeColor = UiPalette.TextPrimary;
            operationTypeLabel.Padding = new Padding(8, 0, 4, 0);
            operationTypeLabel.Text = "指令类型：";
            operationTypeLabel.TextAlign = ContentAlignment.MiddleLeft;
            header.Controls.Add(operationTypeLabel);

            operationTypeButton.AutoEllipsis = true;
            operationTypeButton.BackColor = UiPalette.SurfaceStrong;
            operationTypeButton.BorderColor = UiPalette.Stroke;
            operationTypeButton.BorderWidth = 1F;
            operationTypeButton.Cursor = Cursors.Hand;
            operationTypeButton.FlatAppearance.BorderSize = 0;
            operationTypeButton.FlatAppearance.MouseOverBackColor = UiPalette.SurfaceHover;
            operationTypeButton.FlatAppearance.MouseDownBackColor = UiPalette.SurfacePressed;
            operationTypeButton.FlatStyle = FlatStyle.Flat;
            operationTypeButton.Font = InspectorFonts.Bold95;
            operationTypeButton.ForeColor = UiPalette.TextPrimary;
            operationTypeButton.DisabledForeColor = UiPalette.TextPrimary;
            operationTypeButton.IconKind = InspectorIconKind.None;
            operationTypeButton.Padding = new Padding(9, 0, 8, 0);
            operationTypeButton.TextAlign = ContentAlignment.MiddleLeft;
            operationTypeButton.MouseDown += (sender, args) =>
            {
                if (args.Button == MouseButtons.Left)
                {
                    OperationTypeButton_Click(sender, EventArgs.Empty);
                }
            };
            header.Controls.Add(operationTypeButton);

            inspectorView.Dock = DockStyle.None;
            inspectorView.FieldValueChanged += (sender, args) =>
            {
                Workspace.Runtime.Editor.CaptureSnapshot();
                UpdatePresentation(inspectorView.SelectedObject);
            };
            Controls.Add(inspectorView);
            inspectorView.BringToFront();

            actionBar.BackColor = UiPalette.Surface;
            actionBar.Dock = DockStyle.None;
            actionBar.Height = 48;
            actionBar.Margin = Padding.Empty;
            actionBar.Padding = Padding.Empty;
            actionBar.Visible = false;
            actionBar.Paint += (sender, args) =>
            {
                using (var pen = new Pen(UiPalette.Stroke))
                {
                    args.Graphics.DrawLine(
                        pen,
                        0,
                        Math.Max(0, actionBar.Height - 1),
                        actionBar.Width,
                        Math.Max(0, actionBar.Height - 1));
                }
            };
            Controls.Add(actionBar);
            Controls.SetChildIndex(actionBar, 0);
            Controls.SetChildIndex(header, 1);

            Resize += (sender, args) => LayoutControls();
            LayoutControls();
            UpdatePresentation(null);
        }

        private static void ConfigureActionButton(Button button, bool primary)
        {
            button.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            button.AutoSize = false;
            button.CausesValidation = primary;
            button.Cursor = Cursors.Hand;
            button.Dock = DockStyle.None;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = primary
                ? UiPalette.BrandHover
                : UiPalette.DisabledSoft;
            button.FlatAppearance.MouseDownBackColor = primary
                ? UiPalette.Brand
                : UiPalette.Stroke;
            button.FlatStyle = FlatStyle.Flat;
            button.Font = InspectorFonts.Bold95;
            button.ImageAlign = ContentAlignment.MiddleRight;
            button.TextAlign = ContentAlignment.MiddleLeft;
            button.TextImageRelation = TextImageRelation.ImageBeforeText;
            button.Padding = Padding.Empty;
            button.Margin = Padding.Empty;
            button.TabStop = true;
            button.UseCompatibleTextRendering = false;
            button.UseVisualStyleBackColor = false;
            button.EnabledChanged += (sender, args) =>
                UpdateActionButtonAppearance(button, primary);
            UpdateActionButtonAppearance(button, primary);
        }

        private static void UpdateActionButtonAppearance(Button button, bool primary)
        {
            bool enabled = button.Enabled;
            button.BackColor = enabled && primary
                ? UiPalette.Brand
                : UiPalette.SurfaceSubtle;
            button.ForeColor = enabled
                ? primary ? UiPalette.SurfaceStrong : UiPalette.TextPrimary
                : UiPalette.TextDisabled;
            Image previousImage = button.Image;
            button.Image = UiIconFactory.Create(
                primary ? UiIconKind.Save : UiIconKind.Cancel,
                enabled
                    ? primary ? UiPalette.SurfaceStrong : UiPalette.TextSecondary
                    : UiPalette.TextDisabled,
                24);
            previousImage?.Dispose();
        }

        private void LayoutControls()
        {
            int width = ClientSize.Width;
            int contentTop = 0;
            int contentBottom = ClientSize.Height;
            bool showActionBar = saveButton != null && cancelButton != null;
            if (showActionBar)
            {
                int actionBarHeight = Math.Min(
                    48,
                    Math.Max(0, contentBottom - contentTop));
                actionBar.SetBounds(0, contentTop, width, actionBarHeight);
                contentTop += actionBarHeight;
            }
            if (presentedObject is OperationType)
            {
                header.SetBounds(0, contentTop, width, 38);
                contentTop += 38;
            }
            inspectorView.SetBounds(
                0,
                contentTop,
                width,
                Math.Max(0, contentBottom - contentTop));
            if (presentedObject is OperationType)
            {
                int labelWidth = TextRenderer.MeasureText(
                        operationTypeLabel.Text,
                        operationTypeLabel.Font,
                        Size.Empty,
                        TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width
                    + operationTypeLabel.Padding.Horizontal + 20;
                operationTypeLabel.SetBounds(8, 4, labelWidth, 30);
                operationTypeButton.SetBounds(
                    8 + labelWidth + 8,
                    4,
                    Math.Max(100, width - 24 - labelWidth),
                    30);
            }

            if (saveButton != null && cancelButton != null)
            {
                int saveWidth = actionBar.ClientSize.Width / 2;
                int cancelWidth = actionBar.ClientSize.Width - saveWidth;
                saveButton.SetBounds(0, 0, saveWidth, actionBar.ClientSize.Height);
                cancelButton.SetBounds(
                    saveWidth,
                    0,
                    cancelWidth,
                    actionBar.ClientSize.Height);
            }
        }

        private void UpdatePresentation(object value)
        {
            presentedObject = value;
            bool operation = value is OperationType;
            header.Visible = operation;
            operationTypeLabel.Visible = operation;
            operationTypeButton.Visible = operation;
            operationTypeButton.Enabled = operation && editing;
            operationTypeButton.IconKind = InspectorIconKind.None;
            operationTypeButton.ShowDropDownArrow = editing;
            operationTypeButton.Text = operation
                ? ((OperationType)value).OperaType
                : string.Empty;
            operationTypeButton.AccessibleName = operation ? "更换指令类型" : string.Empty;
            header.Height = operation ? 38 : 0;
            header.Invalidate();
            UpdateActionBar();
            LayoutControls();
        }

        private void UpdateActionBar()
        {
            actionBar.Visible = saveButton != null && cancelButton != null;
            bool sessionActive =
                editorWorkspace?.Runtime?.Editor?.ActiveSession != null;
            if (saveButton != null)
            {
                saveButton.Enabled = editing;
            }
            if (cancelButton != null)
            {
                cancelButton.Enabled = sessionActive;
            }
        }

        private void OperationTypeButton_Click(object sender, EventArgs e)
        {
            if (!editing || !(inspectorView.SelectedObject is OperationType))
            {
                return;
            }
            ShowOperationTypePicker(operationTypeButton);
        }

        private void ShowOperationTypePicker(Control anchorControl)
        {
            activeOperationTypePicker?.Close();
            if (operationTypePickerPanel == null
                || operationTypePickerPanel.IsDisposed
                || operationTypePickerDropDown == null
                || operationTypePickerDropDown.IsDisposed)
            {
                return;
            }

            Rectangle anchorBounds = anchorControl.RectangleToScreen(anchorControl.ClientRectangle);
            Rectangle workingArea = Screen.FromRectangle(anchorBounds).WorkingArea;
            int pickerWidth = Math.Min(550, Math.Max(420, workingArea.Width - 24));
            operationTypePickerPanel.Size = new Size(pickerWidth, 300);
            operationTypePickerPanel.PerformLayout();
            operationTypePickerPanel.RefreshPickerLayout();
            int pickerHeight = Math.Min(
                operationTypePickerPanel.ContentHeight,
                Math.Min(480, Math.Max(320, workingArea.Height - 24)));
            operationTypePickerPanel.Height = pickerHeight;
            operationTypePickerHost.Size = operationTypePickerPanel.Size;
            operationTypePickerDropDown.Size = operationTypePickerPanel.Size;

            activeOperationTypePicker = operationTypePickerDropDown;
            operationTypePickerDropDown.ShowInstant(
                anchorControl,
                new Point(Math.Min(0, anchorControl.Width - pickerWidth), anchorControl.Height),
                operationTypePickerPanel);
            operationTypePickerPanel.FocusPicker();
        }

        private void InitializeOperationTypePicker()
        {
            List<OperationType> templates = operationTemplates.ToList();
            if (templates.Count == 0)
            {
                return;
            }
            operationTypePickerPanel = new OperationTypePickerPanel(templates)
            {
                Size = new Size(550, 300)
            };
            operationTypePickerPanel.PerformLayout();
            operationTypePickerPanel.RefreshPickerLayout();
            operationTypePickerHost = new ToolStripControlHost(operationTypePickerPanel)
            {
                AutoSize = false,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                Size = operationTypePickerPanel.Size
            };
            operationTypePickerDropDown = new InstantToolStripDropDown
            {
                AutoClose = true,
                AutoSize = false,
                BackColor = UiPalette.Surface,
                DropShadowEnabled = false,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                Renderer = new BorderlessDropDownRenderer(),
                Size = operationTypePickerPanel.Size
            };
            operationTypePickerDropDown.Items.Add(operationTypePickerHost);
            operationTypePickerPanel.OperationSelected += operation =>
            {
                if (editing && inspectorView.SelectedObject is OperationType current)
                {
                    ReplaceOperationType(current, operation);
                }
                operationTypePickerDropDown.Close(
                    ToolStripDropDownCloseReason.ItemClicked);
            };
            operationTypePickerPanel.CancelRequested += () =>
                operationTypePickerDropDown.Close(
                    ToolStripDropDownCloseReason.Keyboard);
            operationTypePickerDropDown.Closed += (sender, args) =>
            {
                if (ReferenceEquals(
                    activeOperationTypePicker,
                    operationTypePickerDropDown))
                {
                    activeOperationTypePicker = null;
                }
            };
        }

        private void ReplaceOperationType(OperationType current, OperationType template)
        {
            ModifyKind originalModifyKind = Workspace.Runtime.Editor.ModifyKind;
            bool originalIsAddOps = Workspace.Runtime.Editor.IsAddingOperations;
            OperationType draft;
            try
            {
                Workspace.Runtime.Editor.ModifyKind = ModifyKind.None;
                Workspace.Runtime.Editor.IsAddingOperations = false;
                draft = (OperationType)template.Clone();
            }
            finally
            {
                Workspace.Runtime.Editor.ModifyKind = originalModifyKind;
                Workspace.Runtime.Editor.IsAddingOperations = originalIsAddOps;
            }
            draft.Num = current.Num;
            EditorServiceRegistry.AttachGraph(draft, Workspace.Runtime);
            draft.RefreshInspector?.Invoke();
            Workspace.DataGrid.OperationTemp = draft;
            Workspace.Runtime.Editor.ReplaceDraft(draft);
        }

        private void FrmInspector_KeyDown(object sender, KeyEventArgs e)
        {
            if (Workspace.Runtime.Editor.TryHandleHistoryShortcut(this, e))
            {
                return;
            }
            if (!editing)
            {
                return;
            }
            if (e.KeyCode == Keys.Escape)
            {
                Control focused = FindFocusedControl(inspectorView);
                if (focused is TextBoxBase
                    || focused is ComboBox
                    || activeOperationTypePicker?.Visible == true)
                {
                    return;
                }
                cancelButton?.PerformClick();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private static Control FindFocusedControl(Control root)
        {
            if (root == null || !root.ContainsFocus)
            {
                return null;
            }
            Control current = root;
            while (current != null)
            {
                Control focusedChild = current.Controls
                    .Cast<Control>()
                    .FirstOrDefault(child => child.ContainsFocus);
                if (focusedChild == null)
                {
                    return current.Focused ? current : null;
                }
                current = focusedChild;
            }
            return null;
        }
    }
}
