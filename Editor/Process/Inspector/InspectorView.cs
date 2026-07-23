// 模块：编辑器 / 流程 / Inspector。
// 职责范围：指令属性定义、编辑控件、选择器和值转换。

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Automation
{
    internal sealed class InspectorView : UserControl
    {
        private const int MaxCachedPages = 8;
        private const int WmSetRedraw = 0x000B;

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(
            IntPtr windowHandle,
            int message,
            IntPtr wordParameter,
            IntPtr longParameter);

        private readonly InspectorFlowPanel content = new InspectorFlowPanel();
        private readonly Label emptyLabel = new Label();
        private readonly ToolTip descriptionToolTip = new ToolTip
        {
            AutoPopDelay = 10000,
            InitialDelay = 450,
            ReshowDelay = 100,
            ShowAlways = true
        };
        private readonly List<InspectorSectionControl> sectionControls
            = new List<InspectorSectionControl>();
        private readonly Dictionary<string, CachedInspectorPage> pageCache
            = new Dictionary<string, CachedInspectorPage>(StringComparer.Ordinal);
        private readonly InspectorSelectionPickerPrewarmSession selectionPickerPrewarmSession =
            new InspectorSelectionPickerPrewarmSession();
        private InspectorDocument document;
        private object selectedObject;
        private bool editable;
        private bool selectionPickerPrewarmPending;
        private long cacheSequence;
        private int updateDepth;
        private bool refreshPending;
        private bool redrawSuspended;
        private bool layoutRequired;

        public InspectorView()
        {
            BackColor = UiPalette.SurfaceStrong;
            DoubleBuffered = true;

            content.AutoScroll = true;
            content.BackColor = BackColor;
            content.Dock = DockStyle.Fill;
            content.FlowDirection = FlowDirection.TopDown;
            content.Padding = new Padding(3);
            content.WrapContents = false;
            Controls.Add(content);

            emptyLabel.AutoSize = false;
            emptyLabel.BackColor = BackColor;
            emptyLabel.Dock = DockStyle.Fill;
            emptyLabel.Font = InspectorFonts.Regular10;
            emptyLabel.ForeColor = UiPalette.TextSecondary;
            emptyLabel.Text = "选择流程、步骤、指令或配置对象后，\r\n可在这里查看和编辑参数。";
            emptyLabel.TextAlign = ContentAlignment.MiddleCenter;
            Controls.Add(emptyLabel);

            Resize += (sender, args) => UpdateContentWidths();
        }

        public object SelectedObject => selectedObject;

        public event EventHandler FieldValueChanged;

        public void SetObject(object value, bool allowEdit)
        {
            bool objectChanged = !ReferenceEquals(selectedObject, value);
            selectedObject = value;
            if (objectChanged || document == null)
            {
                editable = allowEdit;
                if (allowEdit)
                {
                    selectionPickerPrewarmSession.Reset();
                }
                else
                {
                    ClearSelectionPickerPrewarm();
                }
                InspectorDocument next = InspectorDefinitionBuilder.Build(selectedObject);
                if (CanRebind(next))
                {
                    Rebind(next);
                }
                else
                {
                    Rebuild(next);
                }
                QueueSelectionPickerPrewarm();
                return;
            }
            SetEditable(allowEdit);
            RefreshValues();
        }

        public void SetEditable(bool allowEdit)
        {
            bool editingChanged = editable != allowEdit;
            editable = allowEdit;
            foreach (InspectorSectionControl section in sectionControls)
            {
                section.SetEditable(allowEdit);
            }
            if (!allowEdit)
            {
                ClearSelectionPickerPrewarm();
                return;
            }
            if (editingChanged)
            {
                selectionPickerPrewarmSession.Reset();
            }
            QueueSelectionPickerPrewarm();
        }

        private void QueueSelectionPickerPrewarm()
        {
            if (!editable
                || selectionPickerPrewarmPending
                || IsDisposed
                || !IsHandleCreated)
            {
                return;
            }
            selectionPickerPrewarmPending = true;
            BeginInvoke((Action)(() =>
            {
                selectionPickerPrewarmPending = false;
                if (!editable || IsDisposed)
                {
                    return;
                }
                foreach (InspectorSectionControl section in sectionControls)
                {
                    section.PrewarmSelectionPickers(selectionPickerPrewarmSession);
                }
            }));
        }

        private void ClearSelectionPickerPrewarm()
        {
            selectionPickerPrewarmSession.Clear();
            foreach (InspectorSectionControl section in sectionControls)
            {
                section.PrewarmSelectionPickers(null);
            }
        }

        public void BeginUpdate()
        {
            updateDepth++;
            if (updateDepth == 1)
            {
                if (IsHandleCreated)
                {
                    SendMessage(Handle, WmSetRedraw, IntPtr.Zero, IntPtr.Zero);
                    redrawSuspended = true;
                }
                SuspendLayout();
                content.SuspendLayout();
            }
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
                content.ResumeLayout(false);
                ResumeLayout(false);
                if (layoutRequired)
                {
                    bool scrollWasVisible = content.VerticalScroll.Visible;
                    UpdateContentWidths();
                    content.PerformLayout();
                    if (scrollWasVisible != content.VerticalScroll.Visible)
                    {
                        UpdateContentWidths();
                        content.PerformLayout();
                    }
                    PerformLayout();
                }
            }
            finally
            {
                layoutRequired = false;
                if (redrawSuspended)
                {
                    if (IsHandleCreated)
                    {
                        SendMessage(Handle, WmSetRedraw, new IntPtr(1), IntPtr.Zero);
                        Invalidate(true);
                    }
                    redrawSuspended = false;
                }
            }
        }

        public void RefreshDocument()
        {
            if (selectedObject == null)
            {
                Rebuild();
                return;
            }
            InspectorDocument next = InspectorDefinitionBuilder.Build(selectedObject);
            if (document == null || !string.Equals(
                document.Signature,
                next.Signature,
                StringComparison.Ordinal))
            {
                Rebuild(next);
                return;
            }
            Rebind(next, false);
        }

        private void Rebuild(InspectorDocument next = null)
        {
            BeginUpdate();
            try
            {
                layoutRequired = true;
                StoreCurrentPage();
                document = next ?? InspectorDefinitionBuilder.Build(selectedObject);
                emptyLabel.Visible = selectedObject == null || document.Sections.Count == 0;
                content.Visible = !emptyLabel.Visible;
                if (emptyLabel.Visible)
                {
                    emptyLabel.BringToFront();
                    return;
                }

                if (TryRestorePage(document, out CachedInspectorPage page))
                {
                    sectionControls.AddRange(page.Sections);
                    int restoredWidth = GetContentWidth();
                    for (int index = 0; index < sectionControls.Count; index++)
                    {
                        sectionControls[index].Width = restoredWidth;
                        sectionControls[index].Rebind(document.Sections[index], editable);
                    }
                    content.Controls.AddRange(sectionControls.Cast<Control>().ToArray());
                    content.AutoScrollPosition = page.ScrollPosition;
                    return;
                }

                var builtSections = new List<Control>();
                int targetWidth = GetContentWidth();
                foreach (InspectorSectionDefinition section in document.Sections)
                {
                    if (section.Fields.Count == 0)
                    {
                        continue;
                    }
                    InspectorSectionControl sectionControl;
                    if (TryRestoreSection(section, out InspectorSectionControl restoredSection))
                    {
                        sectionControl = restoredSection;
                        sectionControl.Rebind(section, editable);
                    }
                    else
                    {
                        sectionControl = new InspectorSectionControl(
                            section,
                            editable,
                            descriptionToolTip);
                        sectionControl.FieldValueChanged += Editor_FieldValueChanged;
                        sectionControl.SizeChanged += SectionControl_SizeChanged;
                    }
                    sectionControl.Width = targetWidth;
                    sectionControls.Add(sectionControl);
                    builtSections.Add(sectionControl);
                }
                content.Controls.AddRange(builtSections.ToArray());
                content.AutoScrollPosition = Point.Empty;
            }
            finally
            {
                EndUpdate();
                QueueSelectionPickerPrewarm();
            }
        }

        private bool CanRebind(InspectorDocument next)
        {
            if (document == null || next == null
                || !string.Equals(document.Signature, next.Signature, StringComparison.Ordinal)
                || sectionControls.Count != next.Sections.Count)
            {
                return false;
            }
            for (int index = 0; index < sectionControls.Count; index++)
            {
                if (!sectionControls[index].CanRebind(next.Sections[index]))
                {
                    return false;
                }
            }
            return true;
        }

        private void Rebind(InspectorDocument next, bool suspendRedraw = true)
        {
            if (suspendRedraw)
            {
                BeginUpdate();
            }
            try
            {
                document = next;
                for (int index = 0; index < sectionControls.Count; index++)
                {
                    sectionControls[index].Rebind(next.Sections[index], editable);
                }
            }
            finally
            {
                if (suspendRedraw)
                {
                    EndUpdate();
                }
            }
        }

        private void StoreCurrentPage()
        {
            Point scrollPosition = new Point(
                Math.Abs(content.AutoScrollPosition.X),
                Math.Abs(content.AutoScrollPosition.Y));
            content.Controls.Clear();
            if (document == null || string.IsNullOrEmpty(document.Signature)
                || sectionControls.Count == 0)
            {
                DisposeSections(sectionControls);
                sectionControls.Clear();
                return;
            }

            if (pageCache.TryGetValue(document.Signature, out CachedInspectorPage duplicate))
            {
                pageCache.Remove(document.Signature);
                DisposeSections(duplicate.Sections);
            }
            pageCache[document.Signature] = new CachedInspectorPage(
                document.Signature,
                sectionControls.ToList(),
                scrollPosition,
                ++cacheSequence);
            sectionControls.Clear();

            while (pageCache.Count > MaxCachedPages)
            {
                CachedInspectorPage oldest = pageCache.Values
                    .OrderBy(page => page.LastUsed).First();
                pageCache.Remove(oldest.Signature);
                DisposeSections(oldest.Sections);
            }
        }

        private bool TryRestorePage(
            InspectorDocument next,
            out CachedInspectorPage page)
        {
            var candidates = new List<CachedInspectorPage>();
            if (pageCache.TryGetValue(next.Signature, out CachedInspectorPage exact))
            {
                candidates.Add(exact);
            }
            candidates.AddRange(pageCache.Values
                .Where(candidate => !ReferenceEquals(candidate, exact))
                .OrderByDescending(candidate => candidate.LastUsed));
            foreach (CachedInspectorPage candidate in candidates)
            {
                if (candidate.Sections.Count != next.Sections.Count)
                {
                    continue;
                }
                bool compatible = true;
                for (int index = 0; index < candidate.Sections.Count; index++)
                {
                    if (!candidate.Sections[index].CanRebind(next.Sections[index]))
                    {
                        compatible = false;
                        break;
                    }
                }
                if (compatible)
                {
                    pageCache.Remove(candidate.Signature);
                    page = candidate;
                    page.LastUsed = ++cacheSequence;
                    return true;
                }
            }
            page = null;
            return false;
        }

        private bool TryRestoreSection(
            InspectorSectionDefinition definition,
            out InspectorSectionControl section)
        {
            foreach (CachedInspectorPage page in pageCache.Values
                .OrderByDescending(candidate => candidate.LastUsed).ToList())
            {
                section = page.Sections.FirstOrDefault(candidate =>
                    candidate.CanRebind(definition));
                if (section == null)
                {
                    continue;
                }
                page.Sections.Remove(section);
                page.LastUsed = ++cacheSequence;
                if (page.Sections.Count == 0)
                {
                    pageCache.Remove(page.Signature);
                }
                return true;
            }
            section = null;
            return false;
        }

        private static void DisposeSections(IEnumerable<InspectorSectionControl> sections)
        {
            foreach (InspectorSectionControl section in sections)
            {
                section.Dispose();
            }
        }

        private void Editor_FieldValueChanged(object sender, EventArgs e)
        {
            FieldValueChanged?.Invoke(this, EventArgs.Empty);
            if (!IsHandleCreated || IsDisposed || refreshPending)
            {
                return;
            }
            refreshPending = true;
            BeginInvoke((Action)(() =>
            {
                refreshPending = false;
                if (!IsDisposed)
                {
                    RefreshDocument();
                }
            }));
        }

        private void RefreshValues()
        {
            foreach (InspectorSectionControl section in sectionControls)
            {
                section.RefreshValues();
            }
        }

        private void UpdateContentWidths()
        {
            int width = GetContentWidth();
            foreach (InspectorSectionControl section in sectionControls)
            {
                section.Width = width;
            }
        }

        private int GetContentWidth()
        {
            return Math.Max(220, content.ClientSize.Width - content.Padding.Horizontal
                - (content.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                selectionPickerPrewarmSession.Dispose();
                foreach (CachedInspectorPage page in pageCache.Values)
                {
                    DisposeSections(page.Sections);
                }
                pageCache.Clear();
                descriptionToolTip.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            QueueSelectionPickerPrewarm();
        }

        private void SectionControl_SizeChanged(object sender, EventArgs e)
        {
            if (updateDepth > 0)
            {
                layoutRequired = true;
            }
        }

        private sealed class CachedInspectorPage
        {
            public CachedInspectorPage(
                string signature,
                List<InspectorSectionControl> sections,
                Point scrollPosition,
                long lastUsed)
            {
                Signature = signature;
                Sections = sections;
                ScrollPosition = scrollPosition;
                LastUsed = lastUsed;
            }

            public string Signature { get; }
            public List<InspectorSectionControl> Sections { get; }
            public Point ScrollPosition { get; }
            public long LastUsed { get; set; }
        }
    }

}
