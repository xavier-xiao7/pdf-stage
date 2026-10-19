using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace PdfStage
{
    internal static class Diagnostics
    {
        static readonly object Gate = new object();
        internal static readonly string LogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PdfStage", "errors.log");
        internal static void Record(Exception exception)
        {
            if (exception == null) return;
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
                    if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 1024 * 1024)
                        File.WriteAllText(LogPath, String.Empty);
                    string entry = DateTimeOffset.Now.ToString("o") + " | PDF Stage " + Assembly.GetExecutingAssembly().GetName().Version + Environment.NewLine + exception + Environment.NewLine + Environment.NewLine;
                    File.AppendAllText(LogPath, entry, Encoding.UTF8);
                }
            }
            catch { /* Diagnostics must never raise a second exception. */ }
        }
    }
}
