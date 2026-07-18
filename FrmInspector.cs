using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Automation
{
    public sealed class FrmInspector : Form
    {
        private const int EmSetCueBanner = 0x1501;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(
            IntPtr windowHandle,
            int message,
            IntPtr wordParameter,
            string longParameter);

        private readonly Panel header = new Panel();
        private readonly Label objectTitle = new Label();
        private readonly Label objectPath = new Label();
        private readonly Button operationTypeButton = new Button();
        private readonly TextBox searchBox = new TextBox();
        private readonly InspectorView inspectorView = new InspectorView();
        private readonly Panel footer = new Panel();
        private readonly Label editStatus = new Label();
        private readonly Button cancelButton = new Button();
        private readonly Button saveButton = new Button();
        private readonly IReadOnlyList<OperationType> operationTemplates;
        private ToolStripDropDown activeOperationTypePicker;
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

        public void ShowValidationError(string error)
        {
            editStatus.ForeColor = Color.FromArgb(181, 52, 43);
            editStatus.Text = string.IsNullOrWhiteSpace(error) ? "配置校验失败" : error;
            editStatus.AutoEllipsis = true;
            editStatus.Refresh();
            inspectorView.FocusFirstEditableField();
        }

        public void ClearValidationError()
        {
            UpdateEditStatus();
        }

        private void InitializeLayout()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Color.FromArgb(246, 249, 251);
            Font = new Font("Microsoft YaHei UI", 9F);
            MinimumSize = new Size(320, 320);
            Text = "配置检查器";

            header.BackColor = Color.White;
            header.Dock = DockStyle.Top;
            header.Height = 142;
            header.Paint += (sender, args) =>
            {
                using (var pen = new Pen(Color.FromArgb(220, 228, 233)))
                {
                    args.Graphics.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
                }
            };
            Controls.Add(header);

            objectTitle.AutoEllipsis = true;
            objectTitle.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
            objectTitle.ForeColor = Color.FromArgb(35, 52, 64);
            objectTitle.Text = "未选择对象";
            header.Controls.Add(objectTitle);

            objectPath.AutoEllipsis = true;
            objectPath.Font = new Font("Microsoft YaHei UI", 8.5F);
            objectPath.ForeColor = Color.FromArgb(103, 118, 128);
            header.Controls.Add(objectPath);

            operationTypeButton.AutoEllipsis = true;
            operationTypeButton.BackColor = Color.White;
            operationTypeButton.Cursor = Cursors.Hand;
            operationTypeButton.FlatAppearance.BorderColor = Color.FromArgb(201, 213, 222);
            operationTypeButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(238, 247, 252);
            operationTypeButton.FlatStyle = FlatStyle.Flat;
            operationTypeButton.Font = new Font("Microsoft YaHei UI", 9.5F);
            operationTypeButton.ForeColor = Color.FromArgb(42, 86, 110);
            operationTypeButton.TextAlign = ContentAlignment.MiddleLeft;
            operationTypeButton.Click += OperationTypeButton_Click;
            header.Controls.Add(operationTypeButton);

            searchBox.BorderStyle = BorderStyle.FixedSingle;
            searchBox.AutoSize = false;
            searchBox.Font = new Font("Microsoft YaHei UI", 9.5F);
            searchBox.ForeColor = Color.FromArgb(65, 79, 89);
            searchBox.HandleCreated += (sender, args) => SendMessage(
                searchBox.Handle,
                EmSetCueBanner,
                new IntPtr(1),
                "搜索参数、说明或字段名…");
            searchBox.TextChanged += (sender, args) => inspectorView.SetFilter(searchBox.Text);
            header.Controls.Add(searchBox);

            inspectorView.Dock = DockStyle.Fill;
            inspectorView.FieldValueChanged += (sender, args) =>
            {
                ClearValidationError();
                UpdatePresentation(inspectorView.SelectedObject);
            };
            Controls.Add(inspectorView);
            inspectorView.BringToFront();

            footer.BackColor = Color.White;
            footer.Dock = DockStyle.Bottom;
            footer.Height = 56;
            footer.Paint += (sender, args) =>
            {
                using (var pen = new Pen(Color.FromArgb(220, 228, 233)))
                {
                    args.Graphics.DrawLine(pen, 0, 0, footer.Width, 0);
                }
            };
            Controls.Add(footer);

            editStatus.AutoEllipsis = true;
            editStatus.Font = new Font("Microsoft YaHei UI", 8.5F);
            editStatus.ForeColor = Color.FromArgb(101, 116, 126);
            editStatus.TextAlign = ContentAlignment.MiddleLeft;
            footer.Controls.Add(editStatus);

            ConfigureFooterButton(cancelButton, "取消", false);
            cancelButton.Click += (sender, args) => SF.frmToolBar?.btnCancel.PerformClick();
            footer.Controls.Add(cancelButton);

            ConfigureFooterButton(saveButton, "保存", true);
            saveButton.Click += (sender, args) => SF.frmToolBar?.btnSave.PerformClick();
            footer.Controls.Add(saveButton);

            Resize += (sender, args) => LayoutControls();
            LayoutControls();
            UpdatePresentation(null);
        }

        private static void ConfigureFooterButton(Button button, string text, bool primary)
        {
            button.BackColor = primary ? Color.FromArgb(35, 121, 166) : Color.White;
            button.Cursor = Cursors.Hand;
            button.FlatAppearance.BorderColor = primary
                ? Color.FromArgb(35, 121, 166)
                : Color.FromArgb(196, 208, 217);
            button.FlatStyle = FlatStyle.Flat;
            button.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            button.ForeColor = primary ? Color.White : Color.FromArgb(61, 79, 91);
            button.Text = text;
        }

        private void LayoutControls()
        {
            int width = ClientSize.Width;
            objectTitle.SetBounds(12, 10, Math.Max(80, width - 24), 26);
            objectPath.SetBounds(12, 36, Math.Max(80, width - 24), 20);
            operationTypeButton.SetBounds(12, 62, Math.Max(100, width - 24), 32);
            searchBox.SetBounds(12, 102, Math.Max(100, width - 24), 28);

            int footerRight = footer.ClientSize.Width - 10;
            saveButton.SetBounds(Math.Max(0, footerRight - 72), 11, 72, 34);
            cancelButton.SetBounds(Math.Max(0, footerRight - 150), 11, 70, 34);
            editStatus.SetBounds(10, 8, Math.Max(40, footerRight - 164), 40);
        }

        private void UpdatePresentation(object value)
        {
            objectTitle.Text = GetObjectTitle(value);
            objectPath.Text = GetObjectPath(value);
            bool operation = value is OperationType;
            operationTypeButton.Visible = operation;
            operationTypeButton.Enabled = operation && editing;
            operationTypeButton.Text = operation
                ? ((OperationType)value).OperaType + (editing ? "    更换类型…" : string.Empty)
                : string.Empty;
            searchBox.Top = operation ? 102 : 66;
            header.Height = operation ? 142 : 106;
            UpdateEditStatus();
            LayoutControls();
        }

        private void UpdateEditStatus()
        {
            editStatus.ForeColor = editing
                ? Color.FromArgb(40, 116, 80)
                : Color.FromArgb(101, 116, 126);
            editStatus.Text = editing ? "正在编辑草稿，保存后提交配置" : "只读查看";
            saveButton.Visible = editing;
            cancelButton.Visible = editing;
        }

        private static string GetObjectTitle(object value)
        {
            if (value == null)
            {
                return "未选择对象";
            }
            if (value is ProcHead process)
            {
                return string.IsNullOrWhiteSpace(process.Name) ? "流程" : process.Name;
            }
            if (value is Step step)
            {
                return string.IsNullOrWhiteSpace(step.Name) ? "步骤" : step.Name;
            }
            if (value is OperationType operation)
            {
                return string.IsNullOrWhiteSpace(operation.Name)
                    ? operation.OperaType
                    : operation.Name;
            }
            if (value is DataStation station)
            {
                return string.IsNullOrWhiteSpace(station.Name) ? "工站" : station.Name;
            }
            if (value is IO io)
            {
                return string.IsNullOrWhiteSpace(io.Name) ? "IO" : io.Name;
            }
            if (value is FrmCard.Axis axis)
            {
                return string.IsNullOrWhiteSpace(axis.AxisName) ? "运动轴" : axis.AxisName;
            }
            if (value is FrmCard.CardHead)
            {
                return "运动控制卡";
            }
            return value.GetType().Name;
        }

        private static string GetObjectPath(object value)
        {
            if (value is OperationType operation)
            {
                int procIndex = SF.frmProc?.SelectedProcNum ?? -1;
                int stepIndex = SF.frmProc?.SelectedStepNum ?? -1;
                return procIndex >= 0 && stepIndex >= 0
                    ? $"流程 {procIndex} / 步骤 {stepIndex} / 指令 {operation.Num}"
                    : "指令配置";
            }
            if (value is Step)
            {
                return $"流程 {SF.frmProc?.SelectedProcNum ?? -1} / 步骤配置";
            }
            if (value is ProcHead)
            {
                return $"流程 {SF.frmProc?.SelectedProcNum ?? -1} / 流程配置";
            }
            return value == null ? string.Empty : "配置对象 / " + value.GetType().Name;
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
                BackColor = Color.White,
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
                saveButton.PerformClick();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                cancelButton.PerformClick();
                e.Handled = true;
            }
        }
    }
}

