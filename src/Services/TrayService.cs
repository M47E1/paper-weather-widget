using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace PaperWeatherWidget.Services
{
    public sealed class TrayService : IDisposable
    {
        private NotifyIcon notifyIcon;
        private ToolStripMenuItem showToggleItem;
        private ToolStripMenuItem refreshItem;
        private ToolStripMenuItem topmostItem;
        private ToolStripMenuItem settingsItem;
        private ToolStripMenuItem exitItem;
        private bool disposed;

        public event Action ToggleShowRequested;
        public event Action RefreshRequested;
        public event Action ToggleTopmostRequested;
        public event Action OpenSettingsRequested;
        public event Action ExitRequested;

        public TrayService()
        {
            notifyIcon = new NotifyIcon();
            notifyIcon.Icon = LoadTrayIcon();
            notifyIcon.Text = "Paper Weather";
            notifyIcon.Visible = true;
            notifyIcon.MouseClick += OnTrayMouseClick;

            var menu = new ContextMenuStrip();
            showToggleItem = new ToolStripMenuItem("Show widget");
            showToggleItem.Click += delegate { if (ToggleShowRequested != null) { ToggleShowRequested(); } };

            refreshItem = new ToolStripMenuItem("Refresh now");
            refreshItem.Click += delegate { if (RefreshRequested != null) { RefreshRequested(); } };

            topmostItem = new ToolStripMenuItem("Always on top");
            topmostItem.CheckOnClick = false;
            topmostItem.Click += delegate { if (ToggleTopmostRequested != null) { ToggleTopmostRequested(); } };

            settingsItem = new ToolStripMenuItem("Settings");
            settingsItem.Click += delegate { if (OpenSettingsRequested != null) { OpenSettingsRequested(); } };

            exitItem = new ToolStripMenuItem("Exit");
            exitItem.Click += delegate { if (ExitRequested != null) { ExitRequested(); } };

            menu.Items.Add(showToggleItem);
            menu.Items.Add(refreshItem);
            menu.Items.Add(topmostItem);
            menu.Items.Add(settingsItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exitItem);
            notifyIcon.ContextMenuStrip = menu;
        }

        private void OnTrayMouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && ToggleShowRequested != null)
            {
                ToggleShowRequested();
            }
        }

        public void UpdateState(string lang, bool isExpanded, bool isTopmost, string tooltip)
        {
            if (disposed || notifyIcon == null) { return; }
            try
            {
                showToggleItem.Text = isExpanded ? LocalizationService.Ui(lang, "DrawerCollapse") : LocalizationService.Ui(lang, "DrawerExpand");
                refreshItem.Text = LocalizationService.Ui(lang, "RefreshNow");
                topmostItem.Text = LocalizationService.Ui(lang, "PinTopmost");
                topmostItem.Checked = isTopmost;
                settingsItem.Text = LocalizationService.Ui(lang, "Settings");
                exitItem.Text = LocalizationService.Ui(lang, "Exit");

                var tip = String.IsNullOrWhiteSpace(tooltip) ? LocalizationService.Ui(lang, "AppTitle") : tooltip;
                if (tip.Length > 63) { tip = tip.Substring(0, 63); }
                notifyIcon.Text = tip;
            }
            catch
            {
            }
        }

        private static Icon LoadTrayIcon()
        {
            try
            {
                var exe = Assembly.GetEntryAssembly();
                if (exe != null && File.Exists(exe.Location))
                {
                    var extracted = Icon.ExtractAssociatedIcon(exe.Location);
                    if (extracted != null) { return extracted; }
                }
            }
            catch
            {
            }
            return SystemIcons.Information;
        }

        public void Dispose()
        {
            if (disposed) { return; }
            disposed = true;
            try
            {
                if (notifyIcon != null)
                {
                    notifyIcon.Visible = false;
                    notifyIcon.Dispose();
                    notifyIcon = null;
                }
            }
            catch
            {
            }
        }
    }
}
