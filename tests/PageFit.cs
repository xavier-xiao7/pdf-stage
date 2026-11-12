using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

// Runs the packaged form (not a mock), sends a file drop, and inspects actual client pixels.
internal static class PageFit
{
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    static readonly BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Wait(Func<bool> ready)
    {
        DateTime end = DateTime.UtcNow.AddSeconds(8);
        while (!ready() && DateTime.UtcNow < end) { Application.DoEvents(); Thread.Sleep(10); }
        if (!ready()) throw new Exception("Window did not finish rendering");
        Application.DoEvents();
    }
    static void Verify(Form f, string label, string png, bool fixture)
    {
        Bitmap frame = (Bitmap)f.GetType().GetField("frame", flags).GetValue(f);
        Size s = f.ClientSize;
        using (Bitmap b = new Bitmap(s.Width,s.Height))
        using (Graphics g = Graphics.FromImage(b))
        {
            IntPtr dc = g.GetHdc(); bool ok = PrintWindow(f.Handle,dc,1); g.ReleaseHdc(dc);
            if (!ok) throw new Exception("Window capture failed");
            if (png != null) b.Save(png,ImageFormat.Png);
            // The regression fixture has colored page corners and white edge midpoints.
            bool whiteEdges = b.GetPixel(s.Width/2,0).R >= 248 && b.GetPixel(s.Width/2,s.Height-1).R >= 248 &&
                b.GetPixel(0,s.Height/2).R >= 248 && b.GetPixel(s.Width-1,s.Height/2).R >= 248;
            Console.WriteLine(label + ": client=" + s + " frame=" + frame.Size + " top/bottom=" +
                b.GetPixel(s.Width/2,0).R + "/" + b.GetPixel(s.Width/2,s.Height-1).R);
            if (s != frame.Size) throw new Exception("Window does not fit rendered page");
            if (fixture && (!whiteEdges || b.GetPixel(2,2).R<240 || b.GetPixel(2,2).G>20 ||
                b.GetPixel(s.Width-3,s.Height-3).B<240 || b.GetPixel(s.Width-3,s.Height-3).R>20))
                throw new Exception("Letterbox, crop or missing content at page edges");
        }
    }
    [STAThread] static int Main(string[] args)
    {
        try
        {
            SetProcessDpiAwarenessContext(new IntPtr(-4)); Application.EnableVisualStyles();
            Type t=Assembly.LoadFrom(Path.GetFullPath(args[0])).GetType("PdfStage.StageForm");
            bool fixture=args.Length<4;
            using (Form f=(Form)Activator.CreateInstance(t,flags,null,new object[] {null},null))
            {
                f.Show(); f.Bounds=new Rectangle(120,80,630,390);
                DataObject data=new DataObject(DataFormats.FileDrop,new string[]{Path.GetFullPath(args[1])});
                typeof(Control).GetMethod("OnDragDrop",flags).Invoke(f,new object[]{new DragEventArgs(data,0,0,0,DragDropEffects.Copy,DragDropEffects.Copy)});
                Wait(delegate{return (int)t.GetField("renderedPage",flags).GetValue(f)==0;});
                Verify(f,"drop",args[2]=="-" ? null : args[2],fixture);
                if (fixture)
                {
                    int[] pages={1,2,0,1,0};
                    Size first=f.ClientSize;
                    foreach(int p in pages)
                    {
                        t.GetMethod("Navigate",flags).Invoke(f,new object[]{p});
                        Wait(delegate{return (int)t.GetField("renderedPage",flags).GetValue(f)==p;});
                        // A small preview can appear first, followed by a full-resolution render.
                        Wait(delegate{return ((Bitmap)t.GetField("frame",flags).GetValue(f)).Size==f.ClientSize;});
                        Verify(f,"page "+(p+1),null,true);
                        if(p==0 && f.ClientSize!=first) throw new Exception("Page turns progressively shrink the window");
                    }
                    f.Size=new Size(780,600);
                    Wait(delegate{return ((Bitmap)t.GetField("frame",flags).GetValue(f)).Size==f.ClientSize;});
                    Verify(f,"resized",null,true);
                }
                f.Close();
            }
            return 0;
        }
        catch(Exception e){Console.WriteLine(e);return 1;}
    }
}
