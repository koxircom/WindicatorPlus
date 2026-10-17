using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using WindicatorPlus;

namespace WindicatorPlus.Tests
{
    public class TaskbarClickMenuTests
    {
        [STAThread]
        public static int Main()
        {
            Console.WriteLine("==================================================");
            Console.WriteLine("[TEST RUNNER] Windicator+ Taskbar Click Menu Tests");
            Console.WriteLine("==================================================");

            try
            {
                TestEventTriggerWhenDocked();
                TestNoTriggerWhenDesktopUnmodified();
                TestDragDoesNotTriggerMenu();

                Console.WriteLine("==================================================");
                Console.WriteLine("ALL TASKBAR CLICK MENU TESTS PASSED SUCCESSFULLY");
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

        private static void SetField(object obj, string name, object val)
        {
            FieldInfo fi = obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (fi == null) throw new Exception("Field not found: " + name);
            fi.SetValue(obj, val);
        }

        private static void TestEventTriggerWhenDocked()
        {
            using (WatermarkWindow win = new WatermarkWindow())
            {
                bool eventFired = false;
                win.RequestContextMenu += (s, e) => { eventFired = true; };

                SetField(win, "_isTaskbarDocked", true);
                SetField(win, "_actualScreenRect", new Rectangle(2000, 2088, 160, 72));

                MethodInfo mi = typeof(WatermarkWindow).GetMethod("OnMouseUp", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert(mi != null, "OnMouseUp must exist");

                // Left click on taskbar watermark
                MouseEventArgs eLeft = new MouseEventArgs(MouseButtons.Left, 1, 50, 36, 0);
                mi.Invoke(win, new object[] { eLeft });

                Assert(eventFired, "Left click on taskbar watermark must trigger RequestContextMenu");

                // Reset and test right click
                eventFired = false;
                SetField(win, "_lastMenuTriggerTick", 0L); // Reset debounce
                MouseEventArgs eRight = new MouseEventArgs(MouseButtons.Right, 1, 50, 36, 0);
                mi.Invoke(win, new object[] { eRight });

                Assert(eventFired, "Right click on taskbar watermark must trigger RequestContextMenu");
                Console.WriteLine("PASS: TestEventTriggerWhenDocked (Left and Right Click trigger menu)");
            }
        }

        private static void TestNoTriggerWhenDesktopUnmodified()
        {
            using (WatermarkWindow win = new WatermarkWindow())
            {
                bool eventFired = false;
                win.RequestContextMenu += (s, e) => { eventFired = true; };

                // Desktop mode: NOT taskbar docked
                SetField(win, "_isTaskbarDocked", false);
                SetField(win, "_actualScreenRect", new Rectangle(100, 100, 300, 150));

                MethodInfo mi = typeof(WatermarkWindow).GetMethod("OnMouseUp", BindingFlags.Instance | BindingFlags.NonPublic);

                MouseEventArgs eLeft = new MouseEventArgs(MouseButtons.Left, 1, 50, 36, 0);
                mi.Invoke(win, new object[] { eLeft });

                Assert(!eventFired, "Click on desktop watermark must NOT trigger RequestContextMenu (keeps 100% penetration)");
                Console.WriteLine("PASS: TestNoTriggerWhenDesktopUnmodified (Preserves 100% desktop penetration)");
            }
        }

        private static void TestDragDoesNotTriggerMenu()
        {
            using (WatermarkWindow win = new WatermarkWindow())
            {
                bool eventFired = false;
                win.RequestContextMenu += (s, e) => { eventFired = true; };

                SetField(win, "_isTaskbarDocked", true);
                SetField(win, "_isDragging", true); // In drag operation

                MethodInfo mi = typeof(WatermarkWindow).GetMethod("OnMouseUp", BindingFlags.Instance | BindingFlags.NonPublic);

                MouseEventArgs eLeft = new MouseEventArgs(MouseButtons.Left, 1, 50, 36, 0);
                mi.Invoke(win, new object[] { eLeft });

                Assert(!eventFired, "Releasing mouse after dragging must NOT trigger RequestContextMenu");
                Console.WriteLine("PASS: TestDragDoesNotTriggerMenu (Dragging does not trigger menu)");
            }
        }
    }
}
