using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Automation
{
    public sealed class FrmInspector : Form
    {
        private readonly Panel header = new Panel();
        private readonly InspectorIconButton operationTypeButton = new InspectorIconButton();
        private readonly InspectorView inspectorView = new InspectorView();
        private readonly Panel actionBar = new Panel();
        private readonly IReadOnlyList<OperationType> operationTemplates;
        private ToolStripDropDown activeOperationTypePicker;
        private Button saveButton;
        private Button cancelButton;
        private bool fontWarningShown;
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
            UpdateActionBar();
            LayoutControls();
        }

        public void ShowObject(object value)
        {
            bool allowEdit = SF.ActiveEditSession != null
                && ReferenceEquals(SF.ActiveEditSession.Draft, value);
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
            BackColor = InspectorPalette.Background;
            Font = InspectorFonts.Regular9;
            MinimumSize = new Size(320, 320);
            Text = "配置检查器";

            header.BackColor = InspectorPalette.Surface;
            header.Dock = DockStyle.None;
            header.Height = 0;
            header.Visible = false;
            header.Paint += (sender, args) =>
            {
                using (var pen = new Pen(InspectorPalette.Stroke))
                {
                    args.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
                }
            };
            Controls.Add(header);

            operationTypeButton.AutoEllipsis = true;
            operationTypeButton.BackColor = InspectorPalette.BrandSoft;
            operationTypeButton.Cursor = Cursors.Hand;
            operationTypeButton.FlatAppearance.BorderSize = 0;
            operationTypeButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(226, 233, 255);
            operationTypeButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(216, 225, 253);
            operationTypeButton.FlatStyle = FlatStyle.Flat;
            operationTypeButton.Font = InspectorFonts.Regular9;
            operationTypeButton.ForeColor = InspectorPalette.Brand;
            operationTypeButton.IconKind = InspectorIconKind.Edit;
            operationTypeButton.Padding = new Padding(9, 0, 6, 0);
            operationTypeButton.TextAlign = ContentAlignment.MiddleLeft;
            operationTypeButton.Click += OperationTypeButton_Click;
            header.Controls.Add(operationTypeButton);

            inspectorView.Dock = DockStyle.None;
            inspectorView.FieldValueChanged += (sender, args) =>
            {
                UpdatePresentation(inspectorView.SelectedObject);
            };
            Controls.Add(inspectorView);
            inspectorView.BringToFront();

            actionBar.BackColor = InspectorPalette.Surface;
            actionBar.Dock = DockStyle.None;
            actionBar.Height = 46;
            actionBar.Visible = false;
            actionBar.Paint += (sender, args) =>
            {
                using (var pen = new Pen(InspectorPalette.Stroke))
                {
                    args.Graphics.DrawLine(
                        pen,
                        0,
                        actionBar.Height - 1,
                        actionBar.Width,
                        actionBar.Height - 1);
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
            button.Cursor = Cursors.Hand;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = primary
                ? InspectorPalette.BrandHover
                : Color.FromArgb(237, 240, 244);
            button.FlatAppearance.MouseDownBackColor = primary
                ? Color.FromArgb(57, 79, 196)
                : Color.FromArgb(229, 233, 239);
            button.FlatStyle = FlatStyle.Flat;
            button.Font = InspectorFonts.Bold9;
            button.ImageAlign = ContentAlignment.MiddleCenter;
            button.TextAlign = ContentAlignment.MiddleCenter;
            button.TextImageRelation = TextImageRelation.ImageBeforeText;
            button.Padding = Padding.Empty;
            button.Margin = Padding.Empty;
            button.TabStop = true;
            button.UseVisualStyleBackColor = false;
            button.EnabledChanged += (sender, args) =>
                UpdateActionButtonAppearance(button, primary);
            UpdateActionButtonAppearance(button, primary);
        }

        private static void UpdateActionButtonAppearance(Button button, bool primary)
        {
            bool enabled = button.Enabled;
            button.BackColor = enabled && primary
                ? InspectorPalette.Brand
                : InspectorPalette.SurfaceSubtle;
            button.ForeColor = enabled
                ? primary ? Color.White : InspectorPalette.TextPrimary
                : InspectorPalette.TextDisabled;
            Image previousImage = button.Image;
            button.Image = UiIconFactory.Create(
                primary ? UiIconKind.Save : UiIconKind.Cancel,
                enabled
                    ? primary ? Color.White : InspectorPalette.TextSecondary
                    : InspectorPalette.TextDisabled,
                17);
            previousImage?.Dispose();
        }

        private void LayoutControls()
        {
            int width = ClientSize.Width;
            int contentTop = 0;
            bool showActionBar = saveButton != null && cancelButton != null;
            if (showActionBar)
            {
                actionBar.SetBounds(0, contentTop, width, 46);
                contentTop += 46;
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
                Math.Max(0, ClientSize.Height - contentTop));
            if (inspectorView.SelectedObject is OperationType)
            {
                operationTypeButton.SetBounds(8, 4, Math.Max(100, width - 16), 30);
            }

            if (saveButton != null && cancelButton != null)
            {
                const int padding = 6;
                const int gap = 6;
                int available = Math.Max(100, actionBar.ClientSize.Width - padding * 2 - gap);
                int saveWidth = available / 2;
                int cancelWidth = available - saveWidth;
                saveButton.SetBounds(padding, 6, saveWidth, 34);
                cancelButton.SetBounds(padding + saveWidth + gap, 6, cancelWidth, 34);
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

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (fontWarningShown
                || string.IsNullOrWhiteSpace(InspectorFonts.LoadFailureMessage))
            {
                return;
            }
            fontWarningShown = true;
            MessageBox.Show(
                this,
                InspectorFonts.LoadFailureMessage,
                "Inspector 字体资源异常",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
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
            var dropDown = new ToolStripDropDown
            {
                AutoClose = true,
                AutoSize = false,
                BackColor = InspectorPalette.Surface,
                DropShadowEnabled = true,
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
            dropDown.Show(
                anchorControl,
                new Point(Math.Min(0, anchorControl.Width - pickerWidth), anchorControl.Height));
            pickerPanel.FocusPicker();
        }

        private void ReplaceOperationType(OperationType current, OperationType template)
        {
            ModifyKind originalModifyKind = SF.isModify;
            bool originalIsAddOps = SF.isAddOps;
            OperationType draft;
            try
            {
                SF.isModify = ModifyKind.None;
                SF.isAddOps = false;
                draft = (OperationType)template.Clone();
            }
            finally
            {
                SF.isModify = originalModifyKind;
                SF.isAddOps = originalIsAddOps;
            }
            draft.Num = current.Num;
            draft.RefreshInspector?.Invoke();
            SF.frmDataGrid.OperationTemp = draft;
            SF.ReplaceActiveEditDraft(draft);
            ShowObject(draft);
        }

        private void FrmInspector_KeyDown(object sender, KeyEventArgs e)
        {
            if (!editing)
            {
                return;
            }
            if (e.Control && e.KeyCode == Keys.Enter)
            {
                saveButton?.PerformClick();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                cancelButton?.PerformClick();
                e.Handled = true;
            }
        }
    }
}
