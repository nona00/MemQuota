using System;
using System.Configuration;
using System.Globalization;
using System.Windows.Forms;

namespace MemQuota
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            int threshold;
            string value = ConfigurationManager.AppSettings["WarningThresholdPercent"];
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out threshold) ||
                threshold < 1 || threshold > 100)
            {
                MessageBox.Show("WarningThresholdPercent must be an integer between 1 and 100.",
                    "MemQuota", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            Application.Run(new TrayApplicationContext(threshold));
        }
    }
}
