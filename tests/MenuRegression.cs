using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static class MenuRegression
{
    delegate bool EnumWindow(IntPtr h, IntPtr p);
    [DllImport("user32.dll")] static extern bool EnumThreadWindows(uint id, EnumWindow callback, IntPtr p);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] static extern IntPtr SetActiveWindow(IntPtr window);
    static int checks, result;
    static Form form;
    static void Check(bool condition, string text)
    {
        if (!condition) throw new Exception("FAIL: " + text);
        checks++; Console.WriteLine("PASS: " + text);
    }
    static ContextMenuStrip ActiveMenu()
    {
        ContextMenuStrip found = null;
        EnumThreadWindows(GetCurrentThreadId(), delegate(IntPtr h, IntPtr p)
        {
            ContextMenuStrip menu = Control.FromHandle(h) as ContextMenuStrip;
            if (menu != null && menu.Visible) found = menu;
            return true;
        }, IntPtr.Zero);
        if (found == null) throw new Exception("No visible context menu");
        return found;
    }
    static ContextMenuStrip OpenMenu()
    {
        typeof(Control).GetMethod("OnMouseUp", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(form, new object[] { new MouseEventArgs(MouseButtons.Right, 1, 25, 25, 0) });
        return ActiveMenu();
    }
    static async Task Wait(Func<bool> ready, string text)
    {
        DateTime end = DateTime.UtcNow.AddSeconds(10);
        while (!ready() && DateTime.UtcNow < end) await Task.Delay(20);
        Check(ready(), text);
    }
    static async Task ClickItem(string prefix)
    {
        ContextMenuStrip menu = OpenMenu();
        ToolStripItem target = null;
        foreach (ToolStripItem item in menu.Items) if (item.Text.StartsWith(prefix)) target = item;
        Check(target != null && target.Enabled, "menu command available: " + prefix);
        Rectangle bounds = target.Bounds;
        int x = bounds.X + bounds.Width / 2, y = bounds.Y + bounds.Height / 2;
        IntPtr point = new IntPtr((y << 16) | x);
        PostMessage(menu.Handle, 0x200, IntPtr.Zero, point);
        PostMessage(menu.Handle, 0x201, new IntPtr(1), point);
        PostMessage(menu.Handle, 0x202, IntPtr.Zero, point);
        await Task.Delay(60);
    }
    static async void Run(object sender, EventArgs args)
    {
        try
        {
            await Wait(delegate { return form.Text.EndsWith("1/3"); }, "real application event loop opens demo");
            ContextMenuStrip first = OpenMenu();
            // Reproduces the user's crash: Closed fired before WinForms has finished
            // SetVisibleCore(false), so disposing here invalidates its live window handle.
            first.Close(ToolStripDropDownCloseReason.AppFocusChange);
            await Task.Delay(30);
            Check(!first.Visible, "menu closes on focus change without ObjectDisposedException");
            ContextMenuStrip escapeMenu = OpenMenu();
            PostMessage(escapeMenu.Handle, 0x100, new IntPtr((int)Keys.Escape), IntPtr.Zero);
            await Wait(delegate { return !escapeMenu.Visible; }, "real Escape key closes menu safely");
            ContextMenuStrip outsideMenu = OpenMenu();
            IntPtr outsidePoint = new IntPtr((form.ClientSize.Height - 20) << 16 | (form.ClientSize.Width - 20));
            PostMessage(form.Handle, 0x201, new IntPtr(1), outsidePoint);
            PostMessage(form.Handle, 0x202, IntPtr.Zero, outsidePoint);
            await Wait(delegate { return !outsideMenu.Visible; }, "clicking presentation outside menu closes it safely");
            ContextMenuStrip activationMenu = OpenMenu();
            using (Form notes = new Form { Text = "Menu activation regression", Size = new Size(250, 140) })
            {
                notes.Show(); SetActiveWindow(notes.Handle);
                PostMessage(form.Handle, 0, IntPtr.Zero, IntPtr.Zero);
                await Wait(delegate { return !activationMenu.Visible; }, "actual window activation change passes menu message filter");
                notes.Close();
            }
            SetActiveWindow(form.Handle);
            ToolStripDropDownCloseReason[] reasons = { ToolStripDropDownCloseReason.AppClicked, ToolStripDropDownCloseReason.Keyboard, ToolStripDropDownCloseReason.AppFocusChange };
            for (int i = 0; i < 150; i++)
            {
                ContextMenuStrip menu = OpenMenu();
                menu.Close(reasons[i % reasons.Length]);
                form.Location = new Point(120 + i % 25, 80 + i % 20);
                form.Size = new Size(800 + i % 31, 450 + i % 29);
                PostMessage(form.Handle, 0x100, new IntPtr(i % 2 == 0 ? (int)Keys.Right : (int)Keys.Left), IntPtr.Zero);
                await Task.Delay(10);
            }
            Check(!form.IsDisposed, "150 menu-close / move / resize / page-turn cycles survive");
            PostMessage(form.Handle, 0x100, new IntPtr((int)Keys.Home), IntPtr.Zero);
            await Wait(delegate { return form.Text.EndsWith("1/3"); }, "navigation remains responsive after stress");
            await ClickItem("下一页");
            await Wait(delegate { return form.Text.EndsWith("2/3"); }, "actual menu click turns page after closing menu");
            await ClickItem("窗口贴合");
            Check(Math.Abs(form.Width / (double)form.Height - 16.0 / 9) < .01, "menu fit-window action works");
            await ClickItem("跳转页面");
            await Wait(delegate { return Application.OpenForms.Count > 1; }, "menu can open a modal page dialog");
            Form dialog = null;
            foreach (Form candidate in Application.OpenForms) if (candidate != form) dialog = candidate;
            if (dialog != null) dialog.Close();
            await Task.Delay(60);
            Check(!form.IsDisposed, "closing modal dialog returns safely to presentation");
            await ClickItem("最小化");
            Check(form.WindowState == FormWindowState.Minimized, "menu minimize survives activation change");
            form.WindowState = FormWindowState.Normal;
            await Task.Delay(60);
            await ClickItem("退出");
            // Application.Run returns as soon as the deferred menu command closes the form.
        }
        catch (Exception ex)
        {
            result = 1; Console.WriteLine(ex.ToString());
            if (!form.IsDisposed) form.Close();
        }
    }
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            SetProcessDpiAwarenessContext(new IntPtr(-4));
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Assembly app = Assembly.LoadFrom(Path.GetFullPath(args[0]));
            form = (Form)Activator.CreateInstance(app.GetType("PdfStage.StageForm"), BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { Path.GetFullPath(args[1]) }, null);
            form.Bounds = new Rectangle(120, 80, 800, 450);
            form.Shown += Run;
            Application.Run(form);
            if (result == 0) Check(form.IsDisposed, "menu exit closes and disposes owner normally");
            if (result == 0) Console.WriteLine("ALL PASSED: " + checks + " menu regression checks; real message loop exited normally");
            return result;
        }
        catch (Exception ex) { Console.WriteLine(ex.ToString()); return 1; }
    }
}
