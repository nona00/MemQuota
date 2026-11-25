using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MemQuota
{
    internal sealed class MemoryPopup : Form
    {
        private readonly Dictionary<string, Icon> icons = new Dictionary<string, Icon>(StringComparer.OrdinalIgnoreCase);
        private readonly Font titleFont = new Font("Segoe UI", 13, FontStyle.Bold);
        private readonly Font headerFont = new Font("Segoe UI", 9, FontStyle.Bold);
        private readonly Font bodyFont = new Font("Segoe UI", 9, FontStyle.Regular);
        private readonly Font smallFont = new Font("Segoe UI", 8, FontStyle.Regular);
        private readonly List<Button> freeButtons = new List<Button>();
        private readonly ToolTip toolTip = new ToolTip();
        private readonly Panel listViewport;
        private readonly Panel listContent;
        private readonly Button expandButton;
        private MemorySnapshot snapshot;
        private ApplicationMemory pressedApplication;
        private ApplicationMemory pressedIconApplication;
        private int pressedIconRow = -1;
        private bool expanded;
        private string error;

        public bool IsActionInProgress { get; private set; }
        public event EventHandler HideRequested;

        public MemoryPopup()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(420, 457);
            BackColor = Color.FromArgb(22, 30, 43);
            DoubleBuffered = true;
            listViewport = new Panel { Location = new Point(0, 78), Size = new Size(420, 315), AutoScroll = true };
            listContent = new Panel { Location = Point.Empty, Size = new Size(400, 315) };
            listContent.Paint += PaintApplications;
            listContent.MouseDown += ApplicationIconMouseDown;
            listContent.MouseUp += ApplicationIconMouseUp;
            listContent.MouseMove += ApplicationIconMouseMove;
            listViewport.Controls.Add(listContent);
            Controls.Add(listViewport);
            expandButton = new Button
            {
                Text = "\u21c8",
                Font = titleFont,
                ForeColor = Color.White,
                BackColor = BackColor,
                FlatStyle = FlatStyle.Flat,
                Bounds = new Rectangle(324, 10, 40, 35),
                AccessibleName = "Expand application list",
                TabStop = false
            };
            expandButton.FlatAppearance.BorderSize = 0;
            expandButton.Click += delegate { SetExpanded(!expanded); };
            toolTip.SetToolTip(expandButton, "Expand application list");
            Controls.Add(expandButton);
            var hideButton = new Button
            {
                Text = "\u2212",
                Font = titleFont,
                ForeColor = Color.White,
                BackColor = BackColor,
                FlatStyle = FlatStyle.Flat,
                Bounds = new Rectangle(364, 10, 40, 35),
                AccessibleName = "Hide popup",
                TabStop = false
            };
            hideButton.FlatAppearance.BorderSize = 0;
            hideButton.Click += delegate { HideRequested?.Invoke(this, EventArgs.Empty); };
            toolTip.SetToolTip(hideButton, "Hide popup");
            Controls.Add(hideButton);
        }

        private void SetExpanded(bool value)
        {
            expanded = value;
            Rectangle area = Screen.FromControl(this).WorkingArea;
            int bottom = Math.Min(Bottom, area.Bottom);
            int height = expanded ? area.Height : Math.Min(457, area.Height);
            Bounds = new Rectangle(Math.Max(area.Left, Math.Min(Left, area.Right - Width)),
                expanded ? area.Top : Math.Max(area.Top, bottom - height), Width, height);
            listViewport.Size = new Size(ClientSize.Width, Math.Max(0, ClientSize.Height - 142));
            expandButton.Text = expanded ? "\u21ca" : "\u21c8";
            expandButton.AccessibleName = expanded ? "Collapse application list" : "Expand application list";
            toolTip.SetToolTip(expandButton, expandButton.AccessibleName);
            UpdateButtons();
            Invalidate();
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= 0x08000000 | 0x00000080;
                return parameters;
            }
        }

        public void SetSnapshot(MemorySnapshot value)
        {
            snapshot = value;
            error = null;
            UpdateButtons();
            var paths = new HashSet<string>(value.Applications.Where(item => !string.IsNullOrEmpty(item.ExecutablePath))
                .Select(item => item.ExecutablePath), StringComparer.OrdinalIgnoreCase);
            foreach (string path in icons.Keys.Where(path => !paths.Contains(path)).ToArray())
            {
                icons[path].Dispose();
                icons.Remove(path);
            }
            Invalidate();
        }

        public void SetError(string message)
        {
            snapshot = null;
            error = message;
            UpdateButtons();
            Invalidate();
        }

        private void UpdateButtons()
        {
            int count = snapshot == null ? 0 : (expanded ? snapshot.Applications.Count : Math.Min(5, snapshot.Applications.Count));
            int scrollY = -listViewport.AutoScrollPosition.Y;
            while (freeButtons.Count < count)
            {
                var button = new Button
                {
                    Text = "Free Mem",
                    Font = headerFont,
                    ForeColor = Color.White,
                    BackColor = Color.FromArgb(37, 99, 170),
                    FlatStyle = FlatStyle.Flat,
                    Size = new Size(96, 42),
                    TabStop = false
                };
                button.FlatAppearance.BorderSize = 0;
                button.MouseDown += delegate(object sender, MouseEventArgs e)
                {
                    if (e.Button == MouseButtons.Left) pressedApplication = ((Button)sender).Tag as ApplicationMemory;
                };
                button.Click += FreeMemoryClick;
                listContent.Controls.Add(button);
                freeButtons.Add(button);
            }
            for (int i = 0; i < freeButtons.Count; i++)
            {
                Button button = freeButtons[i];
                ApplicationMemory application = i < count ? snapshot.Applications[i] : null;
                button.Location = new Point(284, 2 + i * 63);
                button.Tag = application;
                button.Visible = application != null;
                button.Enabled = application != null && !IsActionInProgress;
                string estimate = application != null && application.EstimatedReleasableBytes.HasValue
                    ? string.Format("({0:N0} MB)", application.EstimatedReleasableBytes.Value / 1048576.0)
                    : null;
                button.Text = estimate == null ? "Free Mem" : "Free Mem\n" + estimate;
                button.AccessibleName = application == null ? "Free memory" : "Free memory: " + application.Name;
                toolTip.SetToolTip(button, "Ask Windows to trim the working sets. The amount shown is the estimated private memory freed on exit, not the expected savings from trimming.");
            }
            listContent.Size = new Size(400, Math.Max(listViewport.ClientSize.Height, count * 63));
            listViewport.AutoScrollPosition = new Point(0, scrollY);
            listContent.Invalidate();
        }

        private int IconRowAt(Point point)
        {
            if (snapshot == null || point.X < 20 || point.X >= 52 || point.Y < 0) return -1;
            int row = point.Y / 63;
            int count = expanded ? snapshot.Applications.Count : Math.Min(5, snapshot.Applications.Count);
            return row < count && point.Y % 63 >= 3 && point.Y % 63 < 35 ? row : -1;
        }

        private void ApplicationIconMouseMove(object sender, MouseEventArgs e)
        {
            listContent.Cursor = IconRowAt(e.Location) >= 0 ? Cursors.Hand : Cursors.Default;
        }

        private void ApplicationIconMouseDown(object sender, MouseEventArgs e)
        {
            pressedIconRow = e.Button == MouseButtons.Left ? IconRowAt(e.Location) : -1;
            pressedIconApplication = pressedIconRow < 0 ? null : snapshot.Applications[pressedIconRow];
        }

        private void ApplicationIconMouseUp(object sender, MouseEventArgs e)
        {
            ApplicationMemory application = pressedIconApplication;
            int row = pressedIconRow;
            pressedIconApplication = null;
            pressedIconRow = -1;
            if (e.Button != MouseButtons.Left || application == null || IconRowAt(e.Location) != row || IsActionInProgress) return;

            string failure = MemoryMonitor.ActivateApplication(application);
            if (failure == null)
                HideRequested?.Invoke(this, EventArgs.Empty);
            else
                MessageBox.Show(this, failure, "MemQuota", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private async void FreeMemoryClick(object sender, EventArgs e)
        {
            var application = pressedApplication ?? ((Button)sender).Tag as ApplicationMemory;
            pressedApplication = null;
            if (application == null || IsActionInProgress) return;
            IsActionInProgress = true;
            UpdateButtons();
            try
            {
                string result = await Task.Run(() => MemoryMonitor.TrimWorkingSets(application));
                if (!IsDisposed) MessageBox.Show(this, result, "MemQuota", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                if (!IsDisposed)
                    MessageBox.Show(this, string.Format("Memory trimming failed: {0} (0x{1:X8})", ex.GetType().Name, ex.HResult),
                        "MemQuota", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                IsActionInProgress = false;
                if (!IsDisposed) UpdateButtons();
            }
        }

        public void ShowNear(Point point)
        {
            Rectangle area = Screen.FromPoint(point).WorkingArea;
            int x = Math.Max(area.Left, Math.Min(point.X - Width / 2, area.Right - Width));
            int height = expanded ? area.Height : Math.Min(457, area.Height);
            int desiredY = expanded ? area.Top : (point.Y < area.Top ? area.Top + 8 : point.Y - height - 12);
            int y = Math.Max(area.Top, Math.Min(desiredY, area.Bottom - height));
            Bounds = new Rectangle(x, y, Width, height);
            listViewport.Size = new Size(ClientSize.Width, Math.Max(0, ClientSize.Height - 142));
            UpdateButtons();
            Show();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var foreground = new SolidBrush(Color.FromArgb(241, 245, 249)))
            using (var muted = new SolidBrush(Color.FromArgb(159, 174, 193)))
            using (var divider = new Pen(Color.FromArgb(52, 65, 83)))
            using (var accent = new SolidBrush(Color.FromArgb(78, 165, 255)))
            {
                g.DrawString("MemQuota", titleFont, foreground, 18, 15);
                g.DrawString(expanded ? "All applications" : "Top 5 applications", smallFont, muted, 20, 45);
                g.DrawLine(divider, 18, 68, Width - 18, 68);
                if (snapshot == null) return;

                int footerY = ClientSize.Height - 61;
                g.DrawLine(divider, 18, footerY, Width - 18, footerY);
                string total = string.Format("In use: {0:N1} / {1:N1} GB  ({2} %)",
                    snapshot.UsedBytes / 1073741824.0, snapshot.TotalBytes / 1073741824.0, snapshot.LoadPercent);
                DrawText(g, total, headerFont, foreground, new Rectangle(20, footerY + 7, 380, 22));
                DrawText(g, string.Format("Available: {0:N1} GB", (snapshot.TotalBytes - snapshot.UsedBytes) / 1073741824.0),
                    smallFont, muted, new Rectangle(20, footerY + 27, 380, 16));
                if (snapshot.InaccessibleProcesses > 0)
                    DrawText(g, string.Format("{0} processes could not be read", snapshot.InaccessibleProcesses), smallFont, muted,
                        new Rectangle(20, footerY + 43, 380, 16));
            }
        }

        private void PaintApplications(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (var foreground = new SolidBrush(Color.FromArgb(241, 245, 249)))
            using (var muted = new SolidBrush(Color.FromArgb(159, 174, 193)))
            using (var accent = new SolidBrush(Color.FromArgb(78, 165, 255)))
            using (var track = new SolidBrush(Color.FromArgb(52, 65, 83)))
            {
                if (snapshot == null)
                {
                    DrawText(g, error ?? "Checking memory...", bodyFont, muted, new Rectangle(20, 7, 360, 40));
                    return;
                }
                int count = expanded ? snapshot.Applications.Count : Math.Min(5, snapshot.Applications.Count);
                for (int i = 0; i < count; i++)
                {
                    int y = i * 63;
                    if (y + 63 < e.ClipRectangle.Top || y > e.ClipRectangle.Bottom) continue;
                    ApplicationMemory app = snapshot.Applications[i];
                    Icon icon = GetIcon(app.ExecutablePath);
                    g.DrawIcon(icon ?? SystemIcons.Application, new Rectangle(20, y + 3, 32, 32));
                    string name = app.ProcessCount > 1 ? string.Format("{0} ({1})", app.Name, app.ProcessCount) : app.Name;
                    DrawText(g, name, headerFont, foreground, new Rectangle(63, y + 1, 200, 21));
                    string details = string.Format("{0:N0} MB  ·  {1:N1} %", app.Bytes / 1048576.0,
                        app.Bytes * 100.0 / snapshot.TotalBytes);
                    DrawText(g, details, bodyFont, accent, new Rectangle(63, y + 21, 215, 20));
                    const int barWidth = 215;
                    double fraction = Math.Max(0, Math.Min(1, app.Bytes / (double)snapshot.TotalBytes));
                    g.FillRectangle(track, 63, y + 45, barWidth, 8);
                    int fillWidth = (int)Math.Round(barWidth * fraction);
                    if (fillWidth > 0) g.FillRectangle(accent, 63, y + 45, fillWidth, 8);
                }
            }
        }

        private static void DrawText(Graphics graphics, string text, Font font, Brush brush, Rectangle bounds)
        {
            using (var format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                graphics.DrawString(text, font, brush, bounds, format);
        }

        private Icon GetIcon(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            Icon icon;
            if (icons.TryGetValue(path, out icon)) return icon;
            try
            {
                icon = Icon.ExtractAssociatedIcon(path);
                if (icon != null) icons.Add(path, icon);
                return icon;
            }
            catch (ArgumentException) { return null; }
            catch (IOException) { return null; }
            catch (Win32Exception) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (System.Security.SecurityException) { return null; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (Icon icon in icons.Values) icon.Dispose();
                icons.Clear();
                toolTip.Dispose();
                titleFont.Dispose();
                headerFont.Dispose();
                bodyFont.Dispose();
                smallFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
