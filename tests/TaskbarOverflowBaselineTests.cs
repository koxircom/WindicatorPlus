using System;
using System.Drawing;
using System.IO;
using System.Reflection;

namespace WindicatorPlus.Tests
{
    public static class TaskbarOverflowBaselineTests
    {
        [STAThread]
        public static int Main(string[] args)
        {
            string targetPath = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--target", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    targetPath = args[++i];
                }
            }

            if (string.IsNullOrEmpty(targetPath))
            {
                Console.WriteLine("用法: TaskbarOverflowBaselineTests.exe --target <WindicatorPlus.exe 路径>");
                return 2;
            }

            targetPath = Path.GetFullPath(targetPath);
            if (!File.Exists(targetPath))
            {
                Console.WriteLine("[ERROR] 目标程序集未找到: " + targetPath);
                return 2;
            }

            string targetDir = Path.GetDirectoryName(targetPath);
            AppDomain.CurrentDomain.AssemblyResolve += (sender, resolveArgs) =>
            {
                string asmName = new AssemblyName(resolveArgs.Name).Name + ".dll";
                string candidate = Path.Combine(targetDir, asmName);
                if (File.Exists(candidate))
                {
                    return Assembly.LoadFrom(candidate);
                }
                return null;
            };

            Assembly asm;
            try
            {
                byte[] bytes = File.ReadAllBytes(targetPath);
                asm = Assembly.Load(bytes);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[ERROR] 加载程序集失败: " + ex.Message);
                return 2;
            }

            Type monitorType = asm.GetType("WindicatorPlus.TaskbarSpaceMonitor");
            if (monitorType == null)
            {
                Console.WriteLine("[ERROR] 未在目标程序集中找到类型 WindicatorPlus.TaskbarSpaceMonitor: " + targetPath);
                return 2;
            }

            MethodInfo evalMethod = monitorType.GetMethod(
                "EvaluateTaskbarSnapshot",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new Type[] { typeof(Rectangle), typeof(Rectangle), typeof(int), typeof(int) },
                null);

            if (evalMethod == null)
            {
                Console.WriteLine("[ERROR] 未找到 EvaluateTaskbarSnapshot 静态方法: " + targetPath);
                return 2;
            }

            Rectangle tbRect = new Rectangle(0, 1002, 1680, 48);
            Rectangle trRect = new Rectangle(1225, 1002, 455, 48);

            // 1. 基础有效性验证 (正常非饱和场景: maxAppRight=1100 <= trayLeft=1225)
            // 确保测试对象是真实、可执行的程序集，而非在正常逻辑上就崩溃
            object normalSnap = evalMethod.Invoke(null, new object[] { tbRect, trRect, 5, 1100 });
            Type snapType = normalSnap.GetType();
            bool normalValid = (bool)snapType.GetProperty("IsValid").GetValue(normalSnap, null);
            int normalMaxApp = (int)snapType.GetProperty("MaxAppRight").GetValue(normalSnap, null);
            int normalTrayLeft = (int)snapType.GetProperty("TrayLeft").GetValue(normalSnap, null);

            if (!normalValid || normalMaxApp != 1100 || normalTrayLeft != 1225)
            {
                Console.WriteLine(string.Format("[FATAL ERROR] 目标程序集基础评估异常 (maxAppRight=1100): IsValid={0}, MaxAppRight={1}, TrayLeft={2}",
                    normalValid, normalMaxApp, normalTrayLeft));
                return 2;
            }

            // 2. 饱和边界场景: maxAppRight=1300 > trayLeft=1225
            // 旧版 (v1.3.14): maxAppRight > trayLeft 直接 return TaskbarSpaceSnapshot.Invalid()
            // 新版 (v1.3.15): 安全饱和处理，将 maxAppRight 封顶为 trayLeft (1225)，返回 IsValid=true 的快照
            object satSnap = evalMethod.Invoke(null, new object[] { tbRect, trRect, 5, 1300 });
            bool satValid = (bool)snapType.GetProperty("IsValid").GetValue(satSnap, null);
            int satMaxApp = (int)snapType.GetProperty("MaxAppRight").GetValue(satSnap, null);
            int satTrayLeft = (int)snapType.GetProperty("TrayLeft").GetValue(satSnap, null);

            Console.WriteLine("==================================================");
            Console.WriteLine("[TASKBAR OVERFLOW BASELINE TEST]");
            Console.WriteLine("Target Assembly: " + targetPath);
            Console.WriteLine(string.Format("Input Parameters: Taskbar={0}, Tray={1}, ValidButtons=5, MaxAppRight=1300", tbRect, trRect));
            Console.WriteLine(string.Format("Snapshot Output: IsValid={0}, MaxAppRight={1}, TrayLeft={2}", satValid, satMaxApp, satTrayLeft));
            Console.WriteLine("==================================================");

            if (!satValid)
            {
                Console.WriteLine("[BASELINE DEFECT OBSERVED] Saturated taskbar space snapshot was rejected as Invalid (IsValid=False).");
                Console.WriteLine("Defect details: In old baseline, maxAppRight > trayLeft (1300 > 1225) causes entire snapshot to be discarded as Invalid, failing to recognize taskbar saturation.");
                Console.WriteLine("Status: RED (Defect Confirmed, Exit Code: 1)");
                return 1;
            }

            if (satMaxApp != 1225)
            {
                Console.WriteLine(string.Format("[BASELINE DEFECT OBSERVED] Saturated maxAppRight was {0}, expected clamped at trayLeft (1225).", satMaxApp));
                Console.WriteLine("Status: RED (Defect Confirmed, Exit Code: 1)");
                return 1;
            }

            Console.WriteLine("[BASELINE TEST PASSED] Saturated taskbar space snapshot correctly accepted and capped: MaxAppRight=1225, TrayLeft=1225.");
            Console.WriteLine("Status: GREEN (Fix Confirmed, Exit Code: 0)");
            return 0;
        }
    }
}
