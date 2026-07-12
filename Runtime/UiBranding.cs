using System;
using System.Drawing;
using System.Windows.Forms;

namespace Automation
{
    internal static class UiBranding
    {
        private static readonly object SyncRoot = new object();
        private static Icon applicationIcon;
        private static bool initialized;

        public static void Initialize()
        {
            lock (SyncRoot)
            {
                if (initialized)
                {
                    return;
                }
                initialized = true;
                Application.Idle += Application_Idle;
            }
            ApplyToOpenForms();
        }

        public static void Apply(Form form)
        {
            if (form == null || form.IsDisposed)
            {
                return;
            }
            Icon icon = GetApplicationIcon();
            if (icon != null)
            {
                form.Icon = icon;
            }
        }

        private static void Application_Idle(object sender, EventArgs e)
        {
            ApplyToOpenForms();
        }

        private static void ApplyToOpenForms()
        {
            foreach (Form form in Application.OpenForms)
            {
                Apply(form);
            }
        }

        private static Icon GetApplicationIcon()
        {
            if (applicationIcon != null)
            {
                return applicationIcon;
            }
            lock (SyncRoot)
            {
                if (applicationIcon == null)
                {
                    applicationIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                }
                return applicationIcon;
            }
        }
    }
}
