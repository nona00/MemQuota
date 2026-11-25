using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MemQuota
{
    internal sealed class TrayApplicationContext : ApplicationContext
    {
        private readonly int threshold;
        private readonly NotifyIcon trayIcon;
        private readonly ContextMenuStrip menu;
        private readonly MemoryPopup popup;
        private readonly Timer sampleTimer;
        private readonly Timer animationTimer;
        private readonly Timer hoverTimer;
        private Icon currentIcon;
        private MemorySnapshot snapshot;
        private double displayedPercent;
        private bool sampling;
        private bool closing;
        private bool popupPinned;
        private bool suppressHover;
        private Point lastTrayPoint;
        private DateTime lastTrayMove;
        private int lastDrawnPercent = -1;
        private bool lastDrawnWarning;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr handle);

        public TrayApplicationContext(int threshold)
        {
            this.threshold = threshold;
            popup = new MemoryPopup();
            menu = new ContextMenuStrip();
            menu.Items.Add("Exit", null, delegate { ExitThread(); });
            trayIcon = new NotifyIcon { Text = "MemQuota: Checking memory", ContextMenuStrip = menu };
            trayIcon.MouseMove += TrayIconMouseMove;
            trayIcon.MouseClick += TrayIconMouseClick;
            UpdateIcon();
            trayIcon.Visible = true;

            sampleTimer = new Timer { Interval = 100 };
            sampleTimer.Tick += async delegate
            {
                sampleTimer.Interval = 2000;
                await RefreshSnapshot();
            };
            animationTimer = new Timer { Interval = 80 };
            animationTimer.Tick += delegate { Animate(); };
            hoverTimer = new Timer { Interval = 200 };
            hoverTimer.Tick += delegate { CheckHover(); };
            sampleTimer.Start();
            animationTimer.Start();
            hoverTimer.Start();
        }

        private async Task RefreshSnapshot()
        {
            if (sampling || closing) return;
            sampling = true;
            try
            {
                MemorySnapshot result = await Task.Run(() => MemoryMonitor.Capture());
                if (closing) return;
                bool firstSnapshot = snapshot == null;
                snapshot = result;
                if (firstSnapshot)
                {
                    displayedPercent = result.LoadPercent;
                    UpdateIcon();
                }
                popup.SetSnapshot(result);
                trayIcon.Text = string.Format("MemQuota: {0}% memory used", result.LoadPercent);
            }
            catch (Exception ex)
            {
                if (!closing)
                {
                    snapshot = null;
                    displayedPercent = 0;
                    UpdateIcon();
                    popup.SetError(string.Format("Memory query failed: {0} (0x{1:X8})", ex.GetType().Name, ex.HResult));
                    trayIcon.Text = "MemQuota: Memory query failed";
                }
            }
            finally
            {
                sampling = false;
            }
        }

        private void Animate()
        {
            if (snapshot == null) return;
            double difference = snapshot.LoadPercent - displayedPercent;
            displayedPercent = Math.Abs(difference) < 0.1 ? snapshot.LoadPercent : displayedPercent + difference * 0.08;
            UpdateIcon();
        }

        private void UpdateIcon()
        {
            int percent = (int)Math.Round(displayedPercent);
            bool warning = snapshot != null && snapshot.LoadPercent >= threshold;
            if (percent == lastDrawnPercent && warning == lastDrawnWarning) return;
            lastDrawnPercent = percent;
            lastDrawnWarning = warning;

            using (var bitmap = new Bitmap(16, 16))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (var background = new SolidBrush(Color.FromArgb(29, 39, 51)))
            using (var fill = new SolidBrush(warning ? Color.FromArgb(239, 68, 68) : Color.FromArgb(35, 155, 245)))
            using (var grid = new Pen(Color.FromArgb(110, 159, 178, 195)))
            using (var border = new Pen(Color.FromArgb(190, 201, 212)))
            {
                graphics.SmoothingMode = SmoothingMode.None;
                graphics.FillRectangle(background, 2, 1, 12, 14);
                int height = (int)Math.Round(12 * Math.Max(0, Math.Min(100, displayedPercent)) / 100);
                if (height > 0) graphics.FillRectangle(fill, 3, 14 - height, 10, height);
                for (int y = 3; y < 14; y += 2)
                    graphics.DrawLine(grid, 3, y, 12, y);
                graphics.DrawRectangle(border, 2, 1, 11, 13);
                IntPtr handle = bitmap.GetHicon();
                try
                {
                    Icon newIcon;
                    using (Icon temporary = Icon.FromHandle(handle)) newIcon = (Icon)temporary.Clone();
                    trayIcon.Icon = newIcon;
                    Icon previous = currentIcon;
                    currentIcon = newIcon;
                    if (previous != null) previous.Dispose();
                }
                finally
                {
                    DestroyIcon(handle);
                }
            }
        }

        private void TrayIconMouseMove(object sender, MouseEventArgs e)
        {
            lastTrayPoint = Cursor.Position;
            lastTrayMove = DateTime.UtcNow;
            if (!suppressHover) ShowPopup();
        }

        private void TrayIconMouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            popupPinned = !popupPinned || !popup.Visible;
            if (popupPinned)
            {
                suppressHover = false;
                ShowPopup();
                popup.BringToFront();
            }
            else
            {
                popup.Hide();
                suppressHover = true;
                lastTrayPoint = Cursor.Position;
                lastTrayMove = DateTime.UtcNow;
            }
        }

        private void ShowPopup()
        {
            if (!popup.Visible)
            {
                lastTrayPoint = Cursor.Position;
                lastTrayMove = DateTime.UtcNow;
                popup.ShowNear(lastTrayPoint);
            }
        }

        private void CheckHover()
        {
            Point cursor = Cursor.Position;
            if (suppressHover)
            {
                if (cursor != lastTrayPoint && (DateTime.UtcNow - lastTrayMove).TotalMilliseconds > 900)
                    suppressHover = false;
                return;
            }
            if (!popup.Visible || popupPinned) return;
            if (popup.Bounds.Contains(cursor) || cursor == lastTrayPoint)
            {
                lastTrayMove = DateTime.UtcNow;
                return;
            }
            if ((DateTime.UtcNow - lastTrayMove).TotalMilliseconds > 900) popup.Hide();
        }

        protected override void ExitThreadCore()
        {
            closing = true;
            sampleTimer.Stop();
            animationTimer.Stop();
            hoverTimer.Stop();
            trayIcon.Visible = false;
            trayIcon.Dispose();
            if (currentIcon != null) currentIcon.Dispose();
            sampleTimer.Dispose();
            animationTimer.Dispose();
            hoverTimer.Dispose();
            popup.Dispose();
            menu.Dispose();
            base.ExitThreadCore();
        }
    }
}
