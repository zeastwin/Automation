// 模块：编辑器 / 流程。
// 职责范围：流程树、指令表、对象选择、搜索和导航。

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Automation
{
    public sealed partial class FrmInspector : Form
    {
        private readonly Panel header = new Panel();
        private readonly InspectorIconButton operationTypeButton = new InspectorIconButton();
        private readonly InspectorView inspectorView = new InspectorView();
        private readonly Panel actionBar = new Panel();
        private readonly IReadOnlyList<OperationType> operationTemplates;
        private ToolStripDropDown activeOperationTypePicker;
        private Button saveButton;
        private Button cancelButton;
        private bool editing;

        public FrmInspector()
        {
            operationTemplates = OperationDefinitionRegistry.CreateAll();
            InitializeLayout();
            KeyPreview = true;
            KeyDown += FrmInspector_KeyDown;
            Disposed += (sender, args) => activeOperationTypePicker?.Dispose();
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
            EditorServiceRegistry.AttachGraph(value, Workspace.Runtime);
            bool allowEdit = Workspace.Runtime.Editor.ActiveSession != null
                && ReferenceEquals(Workspace.Runtime.Editor.ActiveSession.Draft, value);
            editing = allowEdit;
            inspectorView.SetObject(value, allowEdit);
            UpdatePresentation(value);
        }

        public void ClearObject()
        {
            editing = false;
            inspectorView.SetObject(null, false);
            UpdatePresentation(null);
        }

        public void SetEditingState(bool allowEdit)
        {
            editing = allowEdit;
            inspectorView.SetEditable(allowEdit);
            UpdatePresentation(inspectorView.SelectedObject);
        }

        public void RefreshObject()
        {
            inspectorView.RefreshDocument();
            UpdatePresentation(inspectorView.SelectedObject);
        }

        public void BeginUpdate()
        {
            SuspendLayout();
            inspectorView.BeginUpdate();
        }

        public void EndUpdate()
        {
            inspectorView.EndUpdate();
            ResumeLayout(true);
        }

        private void InitializeLayout()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = UiPalette.Background;
            Font = InspectorFonts.Regular9;
            MinimumSize = new Size(320, 320);
            Text = "配置检查器";

            header.BackColor = UiPalette.Surface;
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

            operationTypeButton.AutoEllipsis = true;
            operationTypeButton.BackColor = UiPalette.BrandSoft;
            operationTypeButton.Cursor = Cursors.Hand;
            operationTypeButton.FlatAppearance.BorderSize = 0;
            operationTypeButton.FlatAppearance.MouseOverBackColor = UiPalette.BrandSoftHover;
            operationTypeButton.FlatAppearance.MouseDownBackColor = UiPalette.Selection;
            operationTypeButton.FlatStyle = FlatStyle.Flat;
            operationTypeButton.Font = InspectorFonts.Regular9;
            operationTypeButton.ForeColor = UiPalette.Brand;
            operationTypeButton.IconKind = InspectorIconKind.Edit;
            operationTypeButton.Padding = new Padding(9, 0, 6, 0);
            operationTypeButton.TextAlign = ContentAlignment.MiddleLeft;
            operationTypeButton.Click += OperationTypeButton_Click;
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
                        0,
                        actionBar.Width,
                        0);
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
                int actionBarHeight = Math.Min(48, Math.Max(0, contentBottom));
                contentBottom -= actionBarHeight;
                actionBar.SetBounds(0, contentBottom, width, actionBarHeight);
            }
            if (inspectorView.SelectedObject is OperationType)
            {
                header.SetBounds(0, contentTop, width, 38);
                contentTop += 38;
            }
            inspectorView.SetBounds(
                0,
                contentTop,
                width,
                Math.Max(0, contentBottom - contentTop));
            if (inspectorView.SelectedObject is OperationType)
            {
                operationTypeButton.SetBounds(8, 4, Math.Max(100, width - 16), 30);
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
            bool operation = value is OperationType;
            header.Visible = operation;
            operationTypeButton.Visible = operation;
            operationTypeButton.Enabled = operation && editing;
            operationTypeButton.IconKind = editing
                ? InspectorIconKind.Edit
                : InspectorIconKind.Operation;
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
            List<OperationType> templates = operationTemplates.ToList();
            if (templates.Count == 0)
            {
                return;
            }

            Rectangle anchorBounds = anchorControl.RectangleToScreen(anchorControl.ClientRectangle);
            Rectangle workingArea = Screen.FromRectangle(anchorBounds).WorkingArea;
            int pickerWidth = Math.Min(550, Math.Max(420, workingArea.Width - 24));
            var pickerPanel = new OperationTypePickerPanel(templates)
            {
                Size = new Size(pickerWidth, 300)
            };
            pickerPanel.PerformLayout();
            pickerPanel.RefreshPickerLayout();
            int pickerHeight = Math.Min(
                pickerPanel.ContentHeight,
                Math.Min(480, Math.Max(320, workingArea.Height - 24)));
            pickerPanel.Height = pickerHeight;

            var host = new ToolStripControlHost(pickerPanel)
            {
                AutoSize = false,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                Size = pickerPanel.Size
            };
            var dropDown = new InstantToolStripDropDown
            {
                AutoClose = true,
                AutoSize = false,
                BackColor = UiPalette.Surface,
                DropShadowEnabled = false,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                Renderer = new BorderlessDropDownRenderer(),
                Size = pickerPanel.Size
            };
            dropDown.Items.Add(host);
            pickerPanel.OperationSelected += operation =>
            {
                if (editing && inspectorView.SelectedObject is OperationType current)
                {
                    ReplaceOperationType(current, operation);
                }
                dropDown.Close(ToolStripDropDownCloseReason.ItemClicked);
            };
            pickerPanel.CancelRequested += () =>
                dropDown.Close(ToolStripDropDownCloseReason.Keyboard);
            dropDown.Closed += (sender, args) =>
            {
                if (ReferenceEquals(activeOperationTypePicker, dropDown))
                {
                    activeOperationTypePicker = null;
                }
                if (!anchorControl.IsDisposed && anchorControl.IsHandleCreated)
                {
                    anchorControl.BeginInvoke(new Action(dropDown.Dispose));
                }
                else
                {
                    dropDown.Dispose();
                }
            };
            activeOperationTypePicker = dropDown;
            dropDown.ShowInstant(
                anchorControl,
                new Point(Math.Min(0, anchorControl.Width - pickerWidth), anchorControl.Height),
                pickerPanel);
            pickerPanel.FocusPicker();
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
                cancelButton?.PerformClick();
                e.Handled = true;
            }
        }
    }
}
