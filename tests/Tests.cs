using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace PdfStage
{
    internal static class Tests
    {
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr h, int index);
        [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
        [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("user32.dll")] static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
        [DllImport("user32.dll")] static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        static int count;
        static string dir;
        static void Check(bool value, string message)
        {
            if (!value) throw new Exception("FAIL: " + message);
            count++; Console.WriteLine("PASS: " + message);
        }
        static object Field(object obj, string name) { return obj.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(obj); }
        static void Call(object obj, string name, params object[] args) { obj.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(obj, args); }
        static void Until(Func<bool> ready, string reason)
        {
            DateTime limit = DateTime.UtcNow.AddSeconds(10);
            while (!ready() && DateTime.UtcNow < limit) { Application.DoEvents(); Thread.Sleep(10); }
            Check(ready(), reason);
        }
        static void Pump(int ms) { DateTime end = DateTime.UtcNow.AddMilliseconds(ms); while (DateTime.UtcNow < end) { Application.DoEvents(); Thread.Sleep(5); } }
        static void Key(StageForm f, Keys key) { PostMessage(f.Handle, 0x100, new IntPtr((int)key), IntPtr.Zero); Pump(40); PostMessage(f.Handle, 0x101, new IntPtr((int)key), IntPtr.Zero); }
        static void Command(StageForm f, Keys key) { Message m = new Message(); typeof(StageForm).GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(f, new object[] { m, key }); Pump(30); }
        static void ReadyPage(StageForm f, int p) { Until(delegate { return (int)Field(f, "renderedPage") == p; }, "rendered page " + (p + 1)); }
        static void Capture(StageForm f, string file)
        {
            using (Bitmap b = new Bitmap(f.ClientSize.Width, f.ClientSize.Height))
            using (Graphics g = Graphics.FromImage(b))
            {
                IntPtr dc = g.GetHdc();
                bool result = PrintWindow(f.Handle, dc, 1);
                g.ReleaseHdc(dc);
                Check(result, "PrintWindow capture succeeds");
                b.Save(Path.Combine(dir, file), ImageFormat.Png);
                Color center = b.GetPixel(b.Width / 2, b.Height / 2);
                Check(center.A == 255, "capture is opaque");
            }
        }
        static void EngineTests()
        {
            PdfEngine engine = new PdfEngine();
            OpenResult open = null; RenderResult render = null;
            AutoResetEvent changed = new AutoResetEvent(false);
            engine.Opened = delegate(OpenResult r) { open = r; changed.Set(); };
            engine.Rendered = delegate(RenderResult r) { render = r; changed.Set(); };
            engine.Start();
            engine.Open(Path.Combine(dir, "中文 路径 test.pdf"), null, 1);
            Check(changed.WaitOne(10000) && open.Count == 3 && open.Message == null, "Unicode filename loads");
            engine.Render(new RenderRequest { DocId = 1, Page = 0, Width = 960, Height = 540, Token = 1 });
            Check(changed.WaitOne(10000) && render.Image != null && render.Error == null, "PDFium renders real PDF");
            Check(render.Image.Width == 960 && render.Image.Height == 540, "16:9 render geometry");
            Check(render.Image.GetPixel(10, 10).R < 30, "render contains PDF content");
            render.Image.Dispose();
            engine.Open(Path.Combine(dir, "invalid.pdf"), null, 2);
            Check(changed.WaitOne(10000) && open.Message != null, "damaged PDF returns error without crashing");
            engine.Render(new RenderRequest { DocId = 1, Page = 1, Width = 640, Height = 360, Token = 2 });
            Check(changed.WaitOne(10000) && render.Image != null, "previous PDF survives failed replacement"); render.Image.Dispose();
            engine.Open(Path.Combine(dir, "empty.pdf"), null, 3);
            Check(changed.WaitOne(10000) && open.Message != null, "zero-page PDF rejected");
            engine.Open(Path.Combine(dir, "does-not-exist.pdf"), null, 4);
            Check(changed.WaitOne(10000) && open.Message != null, "missing file handled");
            engine.Open(Path.Combine(dir, "mixed.pdf"), null, 5);
            Check(changed.WaitOne(10000) && open.Count == 3, "mixed page sizes load");
            engine.Render(new RenderRequest { DocId = 5, Page = 1, Width = 960, Height = 540, Token = 3 });
            Check(changed.WaitOne(10000) && render.Image.Width == 304 && render.Image.Height == 540, "portrait fits without cropping"); render.Image.Dispose();
            engine.Render(new RenderRequest { DocId = 5, Page = 2, Width = 960, Height = 540, Token = 4 });
            Check(changed.WaitOne(10000) && render.Image.Width == 540 && render.Image.Height == 540, "square fits without stretching"); render.Image.Dispose();
            engine.Dispose();
            ((Thread)Field(engine, "worker")).Join(5000);
            changed.Dispose();
        }
        [STAThread]
        static int Main(string[] args)
        {
            dir = Path.GetFullPath(args[0]);
            try
            {
                SetProcessDpiAwarenessContext(new IntPtr(-4));
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                EngineTests();
                using (StageForm form = new StageForm(null))
                {
                    form.Show(); form.Bounds = new Rectangle(120, 100, 960, 540); Pump(200);
                    Capture(form, "welcome.png");
                    Check((GetWindowLongPtr(form.Handle, -16).ToInt64() & 0x00C00000L) == 0, "native window has no caption or visible frame");
                    Check(form.ShowInTaskbar && form.Opacity == 1 && form.TransparencyKey.IsEmpty, "ordinary opaque top-level share target");
                    Call(form, "OpenFile", Path.Combine(dir, "demo.pdf"), null); ReadyPage(form, 0);
                    Capture(form, "presentation.png");
                    Key(form, Keys.Right); ReadyPage(form, 1);
                    Key(form, Keys.Space); ReadyPage(form, 2);
                    Key(form, Keys.Right); Pump(100); Check((int)Field(form, "page") == 2, "last page does not wrap");
                    Key(form, Keys.Home); ReadyPage(form, 0);
                    Key(form, Keys.Left); Check((int)Field(form, "page") == 0, "first page does not wrap");
                    SendMessage(form.Handle, 0x201, new IntPtr(1), new IntPtr((200 << 16) | 200));
                    SendMessage(form.Handle, 0x202, IntPtr.Zero, new IntPtr((200 << 16) | 200)); ReadyPage(form, 1);
                    Call(form, "OnMouseWheel", new MouseEventArgs(MouseButtons.None, 0, 100, 100, -120)); ReadyPage(form, 2);
                    Call(form, "OnMouseWheel", new MouseEventArgs(MouseButtons.None, 0, 100, 100, 120));
                    Check((int)Field(form, "page") == 2, "wheel burst debounce prevents accidental multiple turns");
                    Command(form, Keys.B); Check((bool)Field(form, "black"), "blackout enabled");
                    Command(form, Keys.Escape); Check(!(bool)Field(form, "black"), "Escape restores blackout without closing");
                    form.Size = new Size(1100, 700); Pump(400);
                    Until(delegate { Bitmap b = (Bitmap)Field(form, "frame"); return b.Width == 1100 && b.Height == 619; }, "resize rerenders sharply with preserved ratio");
                    Command(form, Keys.Control | Keys.D1); Pump(400);
                    Check(Math.Abs(form.Width / (double)form.Height - 16.0 / 9) < .005, "fit-window command matches page ratio");
                    Command(form, Keys.Control | Keys.T); Check(form.TopMost, "always-on-top toggle"); Command(form, Keys.Control | Keys.T);
                    Point old = Cursor.Position;
                    Cursor.Position = form.PointToScreen(new Point(300, 200));
                    Point location = form.Location;
                    Call(form, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 300, 200, 0));
                    Cursor.Position = new Point(Cursor.Position.X + 60, Cursor.Position.Y + 30);
                    Call(form, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 360, 230, 0));
                    Call(form, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, 360, 230, 0));
                    Check(form.Location == new Point(location.X + 60, location.Y + 30) && (int)Field(form, "page") == 2, "drag moves window without turning page");
                    Cursor.Position = old;
                    Point pt = form.PointToScreen(new Point(1,1));
                    IntPtr hit = SendMessage(form.Handle, 0x84, IntPtr.Zero, new IntPtr((pt.Y << 16) | (pt.X & 0xffff)));
                    Check(hit.ToInt32() == 13, "invisible top-left resize handle");
                    form.TopMost = true; form.BringToFront(); form.Activate(); Pump(150);
                    Size beforeResize = form.Size;
                    Point savedCursor = Cursor.Position;
                    Point corner = form.PointToScreen(new Point(form.Width - 2, form.Height - 2));
                    Cursor.Position = corner;
                    Pump(100);
                    Check(WindowFromPoint(corner) == form.Handle, "mouse test targets the visible app, without an overlapping window");
                    bool enteredResize = false;
                    form.ResizeBegin += delegate { enteredResize = true; };
                    Thread resizeInput = new Thread(delegate()
                    {
                        Thread.Sleep(150); Cursor.Position = new Point(corner.X + 40, corner.Y + 30);
                        Thread.Sleep(150); mouse_event(4, 0, 0, 0, UIntPtr.Zero);
                    });
                    resizeInput.Start(); mouse_event(2, 0, 0, 0, UIntPtr.Zero); Pump(500); resizeInput.Join();
                    Console.WriteLine("Resize input evidence: began=" + enteredResize + ", before=" + beforeResize + ", after=" + form.Size + ", foregroundIsApp=" + (GetForegroundWindow() == form.Handle) + ", cursor=" + Cursor.Position);
                    Check(form.Width > beforeResize.Width && form.Height > beforeResize.Height, "real mouse input resizes borderless native window");
                    Cursor.Position = savedCursor;
                    form.TopMost = false;
                    bool leftFree = RegisterHotKey(form.Handle, 501, 0x4003, (uint)Keys.Left);
                    bool rightFree = RegisterHotKey(form.Handle, 502, 0x4003, (uint)Keys.Right);
                    UnregisterHotKey(form.Handle, 501); UnregisterHotKey(form.Handle, 502);
                    if (leftFree && rightFree)
                    {
                        Command(form, Keys.Control | Keys.H);
                        Check((bool)Field(form, "globalKeys"), "global hotkeys register");
                        using (Form notes = new Form { Text = "Test notes", Size = new Size(240, 160) })
                        {
                            notes.Show(); notes.Activate(); Pump(100);
                            keybd_event(0x11,0,0,UIntPtr.Zero); keybd_event(0x12,0,0,UIntPtr.Zero); keybd_event(0x25,0,0,UIntPtr.Zero);
                            keybd_event(0x25,0,2,UIntPtr.Zero); keybd_event(0x12,0,2,UIntPtr.Zero); keybd_event(0x11,0,2,UIntPtr.Zero);
                            ReadyPage(form, 1);
                            Check(Form.ActiveForm == notes, "global page turn keeps focus in notes window");
                            notes.Close();
                        }
                        Command(form, Keys.Control | Keys.H);
                        Check(!(bool)Field(form, "globalKeys"), "global hotkeys unregister");
                    }
                    else Console.WriteLine("SKIP: system has reserved global shortcuts");
                    DataObject data = new DataObject(DataFormats.FileDrop, new string[] { Path.Combine(dir, "large.pdf") });
                    typeof(Control).GetMethod("OnDragDrop", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, new object[] { new DragEventArgs(data, 0, 0, 0, DragDropEffects.Copy, DragDropEffects.Copy) });
                    Until(delegate { return (int)Field(form, "pageCount") == 200 && (int)Field(form, "renderedPage") == 0; }, "drag-and-drop opens 200-page PDF");
                    for (int i = 0; i < 150; i++) Call(form, "Navigate", i);
                    ReadyPage(form, 149);
                    Check(form.Text.EndsWith("150/200"), "rapid navigation displays newest requested page");
                    Key(form, Keys.End); ReadyPage(form, 199);
                    Call(form, "OpenFile", Path.Combine(dir, "mixed.pdf"), null);
                    Until(delegate { return (int)Field(form, "pageCount") == 3 && (int)Field(form, "renderedPage") == 0; }, "document replacement resets page");
                    Key(form, Keys.Right); ReadyPage(form, 1); Capture(form, "portrait.png");
                    Call(form, "OpenFile", Path.Combine(dir, "demo.pdf"), null);
                    Call(form, "OpenFile", Path.Combine(dir, "mixed.pdf"), null);
                    Call(form, "OpenFile", Path.Combine(dir, "large.pdf"), null);
                    Until(delegate { return (int)Field(form, "pageCount") == 200 && (int)Field(form, "renderedPage") == 0; }, "rapid document replacement reaches latest selection");
                    form.Close(); Pump(200);
                }
                Console.WriteLine("ALL PASSED: " + count + " checks"); return 0;
            }
            catch (Exception ex) { Console.WriteLine(ex.ToString()); return 1; }
        }
    }
}
