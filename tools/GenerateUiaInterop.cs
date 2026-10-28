using System;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace WindicatorPlus.Tools
{
    public class GenerateUiaInterop : ITypeLibImporterNotifySink
    {
        [DllImport("oleaut32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void LoadTypeLibEx(
            string strTypeLibName,
            int regKind,
            [MarshalAs(UnmanagedType.Interface)] out object typeLib);

        public void ReportEvent(ImporterEventKind eventKind, int eventCode, string eventMsg)
        {
        }

        public Assembly ResolveRef(object typeLib)
        {
            return null;
        }

        public static int Main(string[] args)
        {
            if (args == null || args.Length == 0 || string.IsNullOrEmpty(args[0]))
            {
                Console.Error.WriteLine("Usage: GenerateUiaInterop <output-assembly-path>");
                return 1;
            }

            try
            {
                string targetFullPath = Path.GetFullPath(args[0]);
                string targetDir = Path.GetDirectoryName(targetFullPath);
                if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                string sysDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
                string uiaCorePath = Path.Combine(sysDir, "UIAutomationCore.dll");
                if (!File.Exists(uiaCorePath))
                {
                    Console.Error.WriteLine("Error: UIAutomationCore.dll not found in System32: " + uiaCorePath);
                    return 2;
                }

                object typeLibObj;
                // 2 = REGKIND_NONE (do not register in registry)
                LoadTypeLibEx(uiaCorePath, 2, out typeLibObj);
                if (typeLibObj == null)
                {
                    Console.Error.WriteLine("Error: Failed to load typelib from: " + uiaCorePath);
                    return 3;
                }

                string targetFileName = Path.GetFileName(targetFullPath);
                string asmNamespace = "UIAutomationClientInterop";

                string prevDir = Environment.CurrentDirectory;
                try
                {
                    if (!string.IsNullOrEmpty(targetDir))
                    {
                        Environment.CurrentDirectory = targetDir;
                    }

                    TypeLibConverter converter = new TypeLibConverter();
                    GenerateUiaInterop sink = new GenerateUiaInterop();
                    AssemblyBuilder asmBuilder = converter.ConvertTypeLibToAssembly(
                        typeLibObj,
                        targetFileName,
                        TypeLibImporterFlags.None,
                        sink,
                        null,
                        null,
                        asmNamespace,
                        new Version(1, 0, 0, 0));

                    asmBuilder.Save(targetFileName);
                }
                finally
                {
                    Environment.CurrentDirectory = prevDir;
                }

                if (!File.Exists(targetFullPath))
                {
                    Console.Error.WriteLine("Error: Output file was not found at expected location: " + targetFullPath);
                    return 4;
                }

                Console.WriteLine("Generated UIA Interop successfully: " + targetFullPath);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("GenerateUiaInterop failed: " + ex);
                return 5;
            }
        }
    }
}
