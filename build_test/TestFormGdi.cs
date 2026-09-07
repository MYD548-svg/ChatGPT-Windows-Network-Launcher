using System;
using System.Drawing;
using System.Windows.Forms;

namespace ChatGPTAntiBanLauncher.Tests
{
    public class TestFormGdi
    {
        [STAThread]
        public static int Main()
        {
            Console.WriteLine("--- Testing MainForm GDI+ Instantiation and Paint ---");
            try
            {
                MainForm form = new MainForm();
                form.CreateControl();
                IntPtr handle = form.Handle;

                // Simulate layout passes and resizes
                form.Size = new Size(640, 720);
                form.PerformLayout();
                form.Refresh();

                // Small resize test
                form.Size = new Size(500, 500);
                form.PerformLayout();
                form.Refresh();

                // Restore
                form.Size = new Size(680, 800);
                form.PerformLayout();
                form.Refresh();

                form.Dispose();
                Console.WriteLine("[PASS] MainForm initialized, resized, and painted with 0 GDI+ exceptions.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[FAIL] MainForm paint failed: " + ex.ToString());
                return 1;
            }
        }
    }
}
