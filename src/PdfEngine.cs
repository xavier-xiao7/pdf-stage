using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace PdfStage
{
    internal static class PdfNative
    {
        const string Dll = "pdfium.dll";
        [DllImport(Dll)] internal static extern void FPDF_InitLibrary();
        [DllImport(Dll)] internal static extern IntPtr FPDF_LoadMemDocument64(IntPtr data, UIntPtr size, byte[] password);
        [DllImport(Dll)] internal static extern uint FPDF_GetLastError();
        [DllImport(Dll)] internal static extern int FPDF_GetPageCount(IntPtr doc);
        [DllImport(Dll)] internal static extern IntPtr FPDF_LoadPage(IntPtr doc, int index);
        [DllImport(Dll)] internal static extern float FPDF_GetPageWidthF(IntPtr page);
        [DllImport(Dll)] internal static extern float FPDF_GetPageHeightF(IntPtr page);
        [DllImport(Dll)] internal static extern void FPDF_ClosePage(IntPtr page);
        [DllImport(Dll)] internal static extern void FPDF_CloseDocument(IntPtr doc);
        [DllImport(Dll)] internal static extern IntPtr FPDFBitmap_CreateEx(int w, int h, int format, IntPtr buffer, int stride);
        [DllImport(Dll)] internal static extern void FPDFBitmap_Destroy(IntPtr bitmap);
        [DllImport(Dll)] internal static extern void FPDFBitmap_FillRect(IntPtr bitmap, int x, int y, int w, int h, uint color);
        [DllImport(Dll)] internal static extern void FPDF_RenderPageBitmap(IntPtr bitmap, IntPtr page, int x, int y, int w, int h, int rotate, int flags);
        [DllImport(Dll)] internal static extern void FPDF_DestroyLibrary();
    }

    internal sealed class OpenResult
    {
        internal int Id, Count;
        internal uint Error;
        internal string Path, Message;
    }
    internal sealed class RenderRequest
    {
        internal int DocId, Page, Width, Height, Token;
    }
    internal sealed class RenderResult
    {
        internal RenderRequest Request;
        internal Bitmap Image;
        internal double Ratio;
        internal string Error;
    }

    // Every PDFium call runs on this single worker; no native document crosses threads.
    internal sealed class PdfEngine : IDisposable
    {
        sealed class OpenRequest { internal string Path, Password; internal int Id; }
        readonly object gate = new object();
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        readonly Thread worker;
        OpenRequest pendingOpen;
        RenderRequest pendingRender;
        bool stopped;
        IntPtr document, bytes;
        int documentId;
        internal Action<OpenResult> Opened;
        internal Action<RenderResult> Rendered;

        internal PdfEngine()
        {
            worker = new Thread(Work) { IsBackground = true, Name = "PDF render worker" };
        }
        internal void Start() { worker.Start(); }
        internal void Open(string path, string password, int id)
        {
            lock (gate) { pendingOpen = new OpenRequest { Path = path, Password = password, Id = id }; pendingRender = null; }
            wake.Set();
        }
        internal void Render(RenderRequest request)
        {
            lock (gate) { pendingRender = request; }
            wake.Set();
        }
        void Work()
        {
            bool initialized = false;
            try
            {
                PdfNative.FPDF_InitLibrary(); initialized = true;
                while (true)
                {
                    wake.WaitOne();
                    OpenRequest open; RenderRequest render;
                    lock (gate)
                    {
                        if (stopped) break;
                        open = pendingOpen; pendingOpen = null;
                        render = pendingRender; pendingRender = null;
                    }
                    if (open != null) Load(open);
                    if (render != null && render.DocId == documentId && document != IntPtr.Zero) Paint(render);
                }
            }
            catch (Exception ex)
            {
                Action<OpenResult> callback = Opened;
                if (callback != null) callback(new OpenResult { Id = -1, Message = "PDF 引擎无法启动。请将整个软件文件夹解压后运行。\n" + ex.Message });
            }
            finally
            {
                CloseDocument();
                if (initialized) PdfNative.FPDF_DestroyLibrary();
                wake.Dispose();
            }
        }
        void CloseDocument()
        {
            if (document != IntPtr.Zero) { PdfNative.FPDF_CloseDocument(document); document = IntPtr.Zero; }
            if (bytes != IntPtr.Zero) { Marshal.FreeHGlobal(bytes); bytes = IntPtr.Zero; }
        }
        void Load(OpenRequest request)
        {
            IntPtr newBytes = IntPtr.Zero, newDoc = IntPtr.Zero;
            OpenResult result = new OpenResult { Id = request.Id, Path = request.Path };
            try
            {
                // Read into native memory in chunks: supports Unicode paths, releases the file lock,
                // and avoids a second managed allocation of the entire PDF.
                using (FileStream file = new FileStream(request.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    long length = file.Length;
                    if (length == 0) throw new IOException("这个文件是空的。");
                    if (length > 1024L * 1024 * 1024) throw new IOException("第一版支持 1 GB 以内的 PDF，请先压缩或拆分此文件。");
                    newBytes = Marshal.AllocHGlobal(new IntPtr(length));
                    byte[] buffer = new byte[1024 * 1024];
                    long offset = 0;
                    while (offset < length)
                    {
                        int count = file.Read(buffer, 0, (int)Math.Min(buffer.Length, length - offset));
                        if (count == 0) throw new IOException("读取过程中，文件已被修改。请重新打开。");
                        Marshal.Copy(buffer, 0, new IntPtr(newBytes.ToInt64() + offset), count);
                        offset += count;
                    }
                    byte[] password = request.Password == null ? null : Encoding.UTF8.GetBytes(request.Password + "\0");
                    newDoc = PdfNative.FPDF_LoadMemDocument64(newBytes, new UIntPtr((ulong)length), password);
                }
                if (newDoc == IntPtr.Zero)
                {
                    result.Error = PdfNative.FPDF_GetLastError();
                    result.Message = result.Error == 4 ? "此 PDF 需要密码，或密码不正确。" : "无法读取此 PDF；文件可能损坏或格式不受支持。";
                }
                else
                {
                    result.Count = PdfNative.FPDF_GetPageCount(newDoc);
                    if (result.Count < 1) throw new IOException("此 PDF 没有可展示的页面。");
                    CloseDocument(); document = newDoc; bytes = newBytes; documentId = request.Id;
                    newDoc = IntPtr.Zero; newBytes = IntPtr.Zero;
                }
            }
            catch (Exception ex) { result.Message = ex.Message; }
            finally
            {
                if (newDoc != IntPtr.Zero) PdfNative.FPDF_CloseDocument(newDoc);
                if (newBytes != IntPtr.Zero) Marshal.FreeHGlobal(newBytes);
            }
            Action<OpenResult> callback = Opened;
            if (callback != null) callback(result);
        }
        void Paint(RenderRequest request)
        {
            RenderResult result = new RenderResult { Request = request };
            IntPtr page = IntPtr.Zero, nativeBitmap = IntPtr.Zero;
            Bitmap bitmap = null; BitmapData data = null;
            try
            {
                page = PdfNative.FPDF_LoadPage(document, request.Page);
                if (page == IntPtr.Zero) throw new IOException("这一页暂时无法读取，请换一页重试。");
                double w = PdfNative.FPDF_GetPageWidthF(page), h = PdfNative.FPDF_GetPageHeightF(page);
                if (w <= 0 || h <= 0 || Double.IsNaN(w) || Double.IsNaN(h)) throw new IOException("页面尺寸不正确。");
                result.Ratio = w / h;
                double scale = Math.Min(Math.Min(request.Width, 4096) / w, Math.Min(request.Height, 4096) / h);
                int width = Math.Max(1, (int)Math.Round(w * scale)), height = Math.Max(1, (int)Math.Round(h * scale));
                bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                nativeBitmap = PdfNative.FPDFBitmap_CreateEx(width, height, 4, data.Scan0, data.Stride);
                if (nativeBitmap == IntPtr.Zero) throw new IOException("页面图像创建失败。");
                PdfNative.FPDFBitmap_FillRect(nativeBitmap, 0, 0, width, height, 0xffffffff);
                PdfNative.FPDF_RenderPageBitmap(nativeBitmap, page, 0, 0, width, height, 0, 1);
                PdfNative.FPDFBitmap_Destroy(nativeBitmap); nativeBitmap = IntPtr.Zero;
                bitmap.UnlockBits(data); data = null;
                result.Image = bitmap; bitmap = null;
            }
            catch (Exception ex) { result.Error = ex.Message; }
            finally
            {
                if (nativeBitmap != IntPtr.Zero) PdfNative.FPDFBitmap_Destroy(nativeBitmap);
                if (data != null) bitmap.UnlockBits(data);
                if (bitmap != null) bitmap.Dispose();
                if (page != IntPtr.Zero) PdfNative.FPDF_ClosePage(page);
            }
            Action<RenderResult> callback = Rendered;
            if (callback != null) callback(result); else if (result.Image != null) result.Image.Dispose();
        }
        public void Dispose()
        {
            lock (gate) { stopped = true; pendingOpen = null; pendingRender = null; }
            // Wake only while alive: failed initialization may already have disposed the event.
            try { wake.Set(); } catch (ObjectDisposedException) { }
        }
    }
}
