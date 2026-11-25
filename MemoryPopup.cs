using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
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
        private MemorySnapshot snapshot;
        private string error;

        public MemoryPopup()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(420, 374);
            BackColor = Color.FromArgb(22, 30, 43);
            DoubleBuffered = true;
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
            Invalidate();
        }

        public void ShowNear(Point point)
        {
            Rectangle area = Screen.FromPoint(point).WorkingArea;
            int x = Math.Max(area.Left, Math.Min(point.X - Width / 2, area.Right - Width));
            int desiredY = point.Y < area.Top ? area.Top + 8 : point.Y - Height - 12;
            int y = Math.Max(area.Top, Math.Min(desiredY, area.Bottom - Height));
            Location = new Point(x, y);
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
                g.DrawString("Memory", titleFont, foreground, 18, 15);
                g.DrawString("Top 5 applications", smallFont, muted, 20, 45);
                g.DrawLine(divider, 18, 68, Width - 18, 68);

                if (snapshot == null)
                {
                    DrawText(g, error ?? "Checking memory...", bodyFont, muted, new Rectangle(20, 85, 380, 110));
                    return;
                }

                int y = 78;
                foreach (ApplicationMemory app in snapshot.Applications)
                {
                    Icon icon = GetIcon(app.ExecutablePath);
                    g.DrawIcon(icon ?? SystemIcons.Application, new Rectangle(20, y + 3, 32, 32));
                    string name = app.ProcessCount > 1 ? string.Format("{0} ({1})", app.Name, app.ProcessCount) : app.Name;
                    DrawText(g, name, headerFont, foreground, new Rectangle(63, y + 1, 200, 21));
                    string details = string.Format("{0:N0} MB  ·  {1:N1} %", app.Bytes / 1048576.0,
                        app.Bytes * 100.0 / snapshot.TotalBytes);
                    DrawText(g, details, bodyFont, accent, new Rectangle(63, y + 21, 260, 20));
                    y += 50;
                }

                g.DrawLine(divider, 18, 331, Width - 18, 331);
                string total = string.Format("Total: {0:N0} / {1:N0} MB  ({2} %)",
                    snapshot.UsedBytes / 1048576.0, snapshot.TotalBytes / 1048576.0, snapshot.LoadPercent);
                DrawText(g, total, headerFont, foreground, new Rectangle(20, 338, 380, 22));
                if (snapshot.InaccessibleProcesses > 0)
                    DrawText(g, string.Format("{0} processes could not be read", snapshot.InaccessibleProcesses), smallFont, muted,
                        new Rectangle(20, 359, 380, 15));
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
                titleFont.Dispose();
                headerFont.Dispose();
                bodyFont.Dispose();
                smallFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
