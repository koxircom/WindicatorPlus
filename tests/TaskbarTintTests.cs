using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using WindicatorPlus;

namespace WindicatorPlus.Tests
{
    public class TaskbarTintTests
    {
        [STAThread]
        public static int Main()
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("[TEST RUNNER] Windicator+ Taskbar Tint Tests");
            Console.WriteLine("==================================================");

            try
            {
                TestColorParsing();
                TestOverlayProperties();
                TestConfigApplication();

                Console.WriteLine("==================================================");
                Console.WriteLine("ALL TASKBAR TINT TESTS PASSED SUCCESSFULLY");
                Console.WriteLine("==================================================");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL: " + ex.Message);
                Console.WriteLine(ex.StackTrace);
                return 1;
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        private static void TestColorParsing()
        {
            Color blue = TaskbarTintOverlay.ParseHexColor("#2563EB", Color.Empty);
            Assert(blue.R == 0x25 && blue.G == 0x63 && blue.B == 0xEB, "Hex #2563EB must parse correctly");

            Color violet = TaskbarTintOverlay.ParseHexColor("#7C3AED", Color.Empty);
            Assert(violet.R == 0x7C && violet.G == 0x3A && violet.B == 0xED, "Hex #7C3AED must parse correctly");

            Color fallback = TaskbarTintOverlay.ParseHexColor("invalid", Color.Red);
            Assert(fallback == Color.Red, "Invalid hex must return fallback color");

            Console.WriteLine("PASS: TestColorParsing");
        }

        private static void TestOverlayProperties()
        {
            AppConfig config = new AppConfig();
            config.TaskbarTintEnabled = true;
            config.TaskbarTintOpacity = 20;

            using (TaskbarTintOverlay overlay = new TaskbarTintOverlay(config, 1))
            {
                overlay.StartPosition = FormStartPosition.Manual;
                overlay.Location = new Point(-20000, -20000);
                overlay.ShowInTaskbar = false;

                CreateParams cp = (CreateParams)typeof(TaskbarTintOverlay)
                    .GetProperty("CreateParams", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(overlay, null);

                Assert((cp.ExStyle & NativeMethods.WS_EX_TRANSPARENT) != 0,
                    "TaskbarTintOverlay must have WS_EX_TRANSPARENT (100% click-through)");
                Assert((cp.ExStyle & NativeMethods.WS_EX_LAYERED) != 0,
                    "TaskbarTintOverlay must have WS_EX_LAYERED");
                Assert((cp.ExStyle & NativeMethods.WS_EX_NOACTIVATE) != 0,
                    "TaskbarTintOverlay must have WS_EX_NOACTIVATE");

                Console.WriteLine("PASS: TestOverlayProperties (100% penetration confirmed)");
            }
        }

        private static void TestConfigApplication()
        {
            AppConfig config = new AppConfig();
            config.TaskbarTintEnabled = true;
            config.SetTaskbarTintColor(1, "#2563EB");
            config.SetTaskbarTintColor(2, "#7C3AED");

            Assert(config.GetTaskbarTintColor(1) == "#2563EB", "Desktop 1 must return set color");
            Assert(config.GetTaskbarTintColor(2) == "#7C3AED", "Desktop 2 must return set color");

            Console.WriteLine("PASS: TestConfigApplication");
        }
    }
}
