using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Reflection;

[assembly: AssemblyTitle("PDF Stage · 无边框演示")]
[assembly: AssemblyProduct("PDF Stage")]
[assembly: AssemblyVersion("1.0.1.0")]

namespace PdfStage
{
    internal static class Program
    {
        [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [STAThread]
        static void Main(string[] args)
        {
            try { if (!SetProcessDpiAwarenessContext(new IntPtr(-4))) SetProcessDPIAware(); }
            catch (EntryPointNotFoundException) { SetProcessDPIAware(); }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs e)
            {
                Diagnostics.Record(e.Exception);
                MessageBox.Show("操作未完成。错误记录已保存，可用于排查。\n" + e.Exception.Message + "\n\n" + Diagnostics.LogPath, "PDF Stage", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e) { Diagnostics.Record(e.ExceptionObject as Exception); };
            try { Application.Run(new StageForm(args.Length > 0 ? args[0] : null)); }
            catch (Exception ex)
            {
                Diagnostics.Record(ex);
                Environment.ExitCode = 1;
                MessageBox.Show("程序发生错误，请重新启动。诊断记录位于：\n" + Diagnostics.LogPath + "\n\n" + ex.Message, "PDF Stage", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
