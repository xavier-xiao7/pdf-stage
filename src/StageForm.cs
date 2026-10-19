using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PdfStage
{
    internal sealed class StageForm : Form
    {
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attribute, ref int value, int size);
        readonly Timer resizeTimer = new Timer { Interval = 110 };
        readonly Timer wheelTimer = new Timer { Interval = 180 };
        readonly ContextMenuStrip presentationMenu;
        readonly Font menuFont = new Font("Microsoft YaHei UI", 10);
        readonly PdfEngine engine;
        readonly string initialPath;
        Bitmap frame;
        string filePath, openingPath, queuedPath, queuedPassword;
        int docId, openId, pageCount, page, renderedPage = -1, renderToken;
        bool loading, closing, dragging, black, globalKeys, wheelReady = true;
        double pageRatio = 16.0 / 9.0;
        Point mouseStart, windowStart;
        bool pressed;
        Rectangle openButton;
        readonly string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PdfStage", "window.txt");
        internal StageForm(string path)
        {
            initialPath = path;
            Text = "PDF Stage · 无边框演示";
            FormBorderStyle = FormBorderStyle.None;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.FromArgb(16, 20, 29);
            MinimumSize = new Size(320, 180);
            Size = new Size(960, 540);
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            AllowDrop = true;
            KeyPreview = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            RestoreWindow();
            presentationMenu = CreatePresentationMenu();
            engine = new PdfEngine();
            engine.Opened = OnOpenedFromWorker;
            engine.Rendered = OnRenderedFromWorker;
            engine.Start();
            resizeTimer.Tick += delegate { resizeTimer.Stop(); RequestRender(); };
            wheelTimer.Tick += delegate { wheelReady = true; wheelTimer.Stop(); };
            Shown += delegate { if (!String.IsNullOrEmpty(initialPath)) OpenFile(initialPath, null); };
            DragEnter += delegate(object sender, DragEventArgs e) { e.Effect = HasPdf(e.Data) ? DragDropEffects.Copy : DragDropEffects.None; };
            DragDrop += delegate(object sender, DragEventArgs e) { if (HasPdf(e.Data)) OpenFile(((string[])e.Data.GetData(DataFormats.FileDrop))[0], null); };
        }
        static bool HasPdf(IDataObject data)
        {
            if (!data.GetDataPresent(DataFormats.FileDrop)) return false;
            string[] files = data.GetData(DataFormats.FileDrop) as string[];
            return files != null && files.Length > 0 && String.Equals(Path.GetExtension(files[0]), ".pdf", StringComparison.OrdinalIgnoreCase);
        }
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.Style |= 0x00040000; // WS_THICKFRAME enables the native resize loop.
                return cp;
            }
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // Square, opaque top-level window: no transparent corners in application capture.
            try
            {
                int value = 1; DwmSetWindowAttribute(Handle, 33, ref value, 4);
                value = unchecked((int)0xfffffffe); DwmSetWindowAttribute(Handle, 34, ref value, 4);
            }
            catch (DllNotFoundException) { }
        }
        void Dispatch(Action action, Bitmap owned)
        {
            try
            {
                if (closing || IsDisposed || !IsHandleCreated) { if (owned != null) owned.Dispose(); return; }
                BeginInvoke((Action)delegate { if (closing) { if (owned != null) owned.Dispose(); } else action(); });
            }
            catch (InvalidOperationException) { if (owned != null) owned.Dispose(); }
        }
        void OnOpenedFromWorker(OpenResult result) { Dispatch(delegate { OnOpened(result); }, null); }
        void OnRenderedFromWorker(RenderResult result) { Dispatch(delegate { OnRendered(result); }, result.Image); }
        void OpenFile(string path, string password)
        {
            try { path = Path.GetFullPath(path); }
            catch (Exception ex) { ShowError(ex.Message); return; }
            // Serialize replacements so an intermediate successful document remains a valid
            // fallback if a newer file fails. Keep only the most recent queued selection.
            if (loading) { queuedPath = path; queuedPassword = password; return; }
            loading = true; openingPath = path; openId++; renderToken++;
            engine.Open(path, password, openId);
            Invalidate();
        }
        void OnOpened(OpenResult result)
        {
            if (result.Id != openId && result.Id != -1) return;
            loading = false;
            if (queuedPath != null && result.Id != -1)
            {
                if (result.Message == null) ApplyDocument(result);
                string next = queuedPath, password = queuedPassword;
                queuedPath = null; queuedPassword = null;
                OpenFile(next, password); return;
            }
            if (result.Error == 4)
            {
                string password = TextPrompt("打开加密 PDF", "请输入 PDF 密码", "", true);
                if (password != null) OpenFile(openingPath, password);
                else RequestRender();
                Invalidate(); return;
            }
            if (result.Message != null) { ShowError(result.Message); RequestRender(); Invalidate(); return; }
            ApplyDocument(result);
            RequestRender(); Invalidate();
        }
        void ApplyDocument(OpenResult result)
        {
            docId = result.Id; filePath = result.Path; pageCount = result.Count;
            page = 0; renderedPage = -1; black = false;
        }
        void RequestRender()
        {
            if (closing || loading || pageCount == 0 || WindowState == FormWindowState.Minimized) return;
            engine.Render(new RenderRequest { DocId = docId, Page = page, Width = ClientSize.Width, Height = ClientSize.Height, Token = ++renderToken });
        }
        void OnRendered(RenderResult result)
        {
            if (result.Request.Token != renderToken || result.Request.DocId != docId)
            { if (result.Image != null) result.Image.Dispose(); return; }
            if (result.Error != null) { ShowError(result.Error); return; }
            Bitmap old = frame; frame = result.Image;
            renderedPage = result.Request.Page; pageRatio = result.Ratio;
            if (old != null) old.Dispose();
            Text = String.Format("PDF Stage · {0} · {1}/{2}", Path.GetFileName(filePath), renderedPage + 1, pageCount);
            Invalidate();
        }
        void Navigate(int target)
        {
            if (pageCount == 0 || loading) return;
            target = Math.Max(0, Math.Min(pageCount - 1, target));
            if (target == page) return;
            page = target; RequestRender();
        }
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (resizeTimer != null) { resizeTimer.Stop(); resizeTimer.Start(); }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            if (frame != null)
            {
                g.Clear(Color.Black);
                if (black) return;
                double scale = Math.Min(ClientSize.Width / (double)frame.Width, ClientSize.Height / (double)frame.Height);
                int w = (int)Math.Round(frame.Width * scale), h = (int)Math.Round(frame.Height * scale);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(frame, new Rectangle((ClientSize.Width - w) / 2, (ClientSize.Height - h) / 2, w, h));
                return;
            }
            g.Clear(BackColor);
            float unit = Math.Max(0.5f, Math.Min(1.4f, Math.Min(ClientSize.Width / 800f, ClientSize.Height / 500f)));
            int cx = ClientSize.Width / 2, cy = ClientSize.Height / 2;
            using (Pen pen = new Pen(Color.FromArgb(100, 214, 185), 2 * unit))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.DrawRectangle(pen, cx - 21 * unit, cy - 158 * unit, 42 * unit, 53 * unit);
                g.DrawLine(pen, cx - 10 * unit, cy - 139 * unit, cx + 10 * unit, cy - 139 * unit);
                g.DrawLine(pen, cx - 10 * unit, cy - 128 * unit, cx + 6 * unit, cy - 128 * unit);
            }
            DrawText(g, "PDF Stage", 28 * unit, FontStyle.Bold, Color.White, new RectangleF(0, cy - 88 * unit, Width, 48 * unit));
            DrawText(g, "把舞台留给内容", 12 * unit, FontStyle.Regular, Color.FromArgb(167, 180, 198), new RectangleF(0, cy - 38 * unit, Width, 32 * unit));
            openButton = new Rectangle(cx - (int)(100 * unit), cy + (int)(18 * unit), (int)(200 * unit), (int)(46 * unit));
            using (Brush b = new SolidBrush(Color.FromArgb(100, 214, 185))) g.FillRectangle(b, openButton);
            DrawText(g, loading ? "正在打开…" : "打开 PDF", 12 * unit, FontStyle.Bold, Color.FromArgb(16, 30, 30), openButton);
            DrawText(g, "也可将 PDF 拖入窗口  ·  Ctrl + O", 10 * unit, FontStyle.Regular, Color.FromArgb(167, 180, 198), new RectangleF(0, cy + 79 * unit, Width, 28 * unit));
            if (Height > 280)
                DrawText(g, "拖动页面移动窗口   /   拖动边缘调整大小   /   右键打开菜单", 9 * unit, FontStyle.Regular, Color.FromArgb(119, 137, 158), new RectangleF(0, Height - 43 * unit, Width, 26 * unit));
        }
        static void DrawText(Graphics g, string text, float size, FontStyle style, Color color, RectangleF bounds)
        {
            using (Font font = new Font("Microsoft YaHei UI", size, style))
            using (Brush brush = new SolidBrush(color))
            using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(text, font, brush, bounds, format);
        }
        protected override void WndProc(ref Message m)
        {
            // Retain native resize behavior while making the entire window client content.
            if (m.Msg == 0x83) { m.Result = IntPtr.Zero; return; } // WM_NCCALCSIZE
            if (m.Msg == 0x84 && WindowState == FormWindowState.Normal)
            {
                long value = m.LParam.ToInt64();
                Point p = PointToClient(new Point((short)(value & 0xffff), (short)((value >> 16) & 0xffff)));
                int grip = 7;
                bool l = p.X < grip, r = p.X >= ClientSize.Width - grip, t = p.Y < grip, b = p.Y >= ClientSize.Height - grip;
                int hit = t ? (l ? 13 : r ? 14 : 12) : b ? (l ? 16 : r ? 17 : 15) : l ? 10 : r ? 11 : 1;
                m.Result = new IntPtr(hit); return;
            }
            if (m.Msg == 0x0312)
            {
                if (m.WParam.ToInt32() == 101) Navigate(page - 1);
                if (m.WParam.ToInt32() == 102) Navigate(page + 1);
                return;
            }
            base.WndProc(ref m);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                pressed = true; dragging = false; mouseStart = Cursor.Position; windowStart = Location; Capture = true;
            }
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!pressed) return;
            Point delta = new Point(Cursor.Position.X - mouseStart.X, Cursor.Position.Y - mouseStart.Y);
            if (!dragging && (Math.Abs(delta.X) > SystemInformation.DragSize.Width / 2 || Math.Abs(delta.Y) > SystemInformation.DragSize.Height / 2)) dragging = true;
            if (dragging && WindowState == FormWindowState.Normal) Location = new Point(windowStart.X + delta.X, windowStart.Y + delta.Y);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Right) { ShowMenu(e.Location); return; }
            if (e.Button == MouseButtons.XButton1) { Navigate(page - 1); return; }
            if (e.Button == MouseButtons.XButton2) { Navigate(page + 1); return; }
            if (e.Button != MouseButtons.Left || !pressed) return;
            bool clicked = !dragging;
            pressed = false; Capture = false;
            if (!clicked) return;
            if (frame == null) { if (openButton.Contains(e.Location) && !loading) ChooseFile(); }
            else Navigate(page + ((ModifierKeys & Keys.Shift) != 0 ? -1 : 1));
        }
        protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (!Capture) pressed = false; }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (!wheelReady || e.Delta == 0) return;
            wheelReady = false; wheelTimer.Start(); Navigate(page + (e.Delta < 0 ? 1 : -1));
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys key)
        {
            switch (key)
            {
                case Keys.Right: case Keys.Down: case Keys.PageDown: case Keys.Space: Navigate(page + 1); return true;
                case Keys.Left: case Keys.Up: case Keys.PageUp: case Keys.Back: case Keys.Shift | Keys.Space: Navigate(page - 1); return true;
                case Keys.Home: Navigate(0); return true;
                case Keys.End: Navigate(pageCount - 1); return true;
                case Keys.Control | Keys.O: ChooseFile(); return true;
                case Keys.Control | Keys.G: GoToPage(); return true;
                case Keys.Control | Keys.D1: FitWindow(); return true;
                case Keys.Control | Keys.T: TopMost = !TopMost; return true;
                case Keys.Control | Keys.H: ToggleHotkeys(); return true;
                case Keys.B: if (frame != null) { black = !black; Invalidate(); } return true;
                case Keys.F1: Help(); return true;
                case Keys.Escape: if (black) { black = false; Invalidate(); } else ShowMenu(new Point(Width / 2, Height / 2)); return true;
            }
            return base.ProcessCmdKey(ref msg, key);
        }
        void ChooseFile()
        {
            using (OpenFileDialog dialog = new OpenFileDialog { Filter = "PDF 文档 (*.pdf)|*.pdf", Title = "选择要演示的 PDF", CheckFileExists = true })
                if (dialog.ShowDialog(this) == DialogResult.OK) OpenFile(dialog.FileName, null);
        }
        void GoToPage()
        {
            if (pageCount == 0) return;
            string value = TextPrompt("跳转页面", "页码（1–" + pageCount + "）", (page + 1).ToString(), false);
            if (value == null) return;
            int number;
            if (!Int32.TryParse(value, out number) || number < 1 || number > pageCount) { ShowError("请输入范围内的整数页码。"); return; }
            Navigate(number - 1);
        }
        void FitWindow()
        {
            if (frame == null) return;
            Rectangle area = Screen.FromControl(this).WorkingArea;
            WindowState = FormWindowState.Normal;
            int w = Width, h = (int)Math.Round(w / pageRatio);
            if (h > area.Height) { h = area.Height; w = (int)Math.Round(h * pageRatio); }
            if (w > area.Width) { w = area.Width; h = (int)Math.Round(w / pageRatio); }
            Size = new Size(Math.Max(320, w), Math.Max(180, h));
            Location = new Point(Math.Max(area.Left, Math.Min(Left, area.Right - Width)), Math.Max(area.Top, Math.Min(Top, area.Bottom - Height)));
        }
        void Place(bool right)
        {
            Rectangle area = Screen.FromControl(this).WorkingArea;
            WindowState = FormWindowState.Normal;
            int w = area.Width / 2, h = Math.Max(180, Math.Min(area.Height, (int)Math.Round(w / pageRatio)));
            Bounds = new Rectangle(right ? area.Right - w : area.Left, area.Top + (area.Height - h) / 2, w, h);
        }
        void ToggleHotkeys()
        {
            if (globalKeys) { UnregisterHotKey(Handle, 101); UnregisterHotKey(Handle, 102); globalKeys = false; return; }
            bool left = RegisterHotKey(Handle, 101, 0x4003, (uint)Keys.Left);
            bool right = RegisterHotKey(Handle, 102, 0x4003, (uint)Keys.Right);
            globalKeys = left && right;
            if (!globalKeys) { UnregisterHotKey(Handle, 101); UnregisterHotKey(Handle, 102); ShowError("Ctrl + Alt + 方向键已被其他程序使用。普通翻页键仍可正常使用。"); }
        }
        void ShowMenu(Point point)
        {
            if (!closing && !presentationMenu.Visible) presentationMenu.Show(this, point);
        }
        ContextMenuStrip CreatePresentationMenu()
        {
            // Keep one menu alive for the lifetime of this form. Closed is raised while
            // WinForms still needs the handle in SetVisibleCore(false); never dispose there.
            ContextMenuStrip menu = new ContextMenuStrip { ShowImageMargin = false, Font = menuFont };
            ToolStripMenuItem pageLabel = Add(menu, "PDF Stage · 无边框演示", null, false);
            menu.Items.Add(new ToolStripSeparator());
            Add(menu, "打开 PDF…                         Ctrl+O", ChooseFile, true);
            ToolStripMenuItem previous = Add(menu, "上一页                                      ←", delegate { Navigate(page - 1); }, false);
            ToolStripMenuItem next = Add(menu, "下一页                                      →", delegate { Navigate(page + 1); }, false);
            ToolStripMenuItem jump = Add(menu, "跳转页面…                         Ctrl+G", GoToPage, false);
            menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem fit = Add(menu, "窗口贴合页面比例              Ctrl+1", FitWindow, false);
            Add(menu, "摆在屏幕左侧", delegate { Place(false); }, true);
            Add(menu, "摆在屏幕右侧", delegate { Place(true); }, true);
            ToolStripMenuItem pin = Add(menu, "窗口置顶                             Ctrl+T", delegate { TopMost = !TopMost; }, true);
            ToolStripMenuItem hotkeys = Add(menu, "看讲稿时也能翻页              Ctrl+H", ToggleHotkeys, true);
            ToolStripMenuItem blackout = Add(menu, "暂时黑屏                                      B", delegate { black = !black; Invalidate(); }, false);
            menu.Items.Add(new ToolStripSeparator());
            Add(menu, "使用说明                                     F1", Help, true);
            Add(menu, "最小化", delegate { WindowState = FormWindowState.Minimized; }, true);
            Add(menu, "退出                                      Alt+F4", Close, true);
            menu.Opening += delegate
            {
                pageLabel.Text = pageCount > 0 ? String.Format("第 {0} / {1} 页", page + 1, pageCount) : "PDF Stage · 无边框演示";
                previous.Enabled = pageCount > 0 && page > 0 && !loading;
                next.Enabled = pageCount > 0 && page < pageCount - 1 && !loading;
                jump.Enabled = pageCount > 0 && !loading;
                fit.Enabled = frame != null;
                pin.Checked = TopMost;
                hotkeys.Checked = globalKeys;
                blackout.Enabled = frame != null;
                blackout.Checked = black;
            };
            return menu;
        }
        ToolStripMenuItem Add(ContextMenuStrip menu, string text, Action action, bool enabled)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text) { Enabled = enabled };
            if (action != null) item.Click += delegate
            {
                // Finish the menu's native close sequence before opening dialogs, changing
                // activation, or closing the owner window. Avoid re-entering its message filter.
                if (!closing && IsHandleCreated) BeginInvoke((Action)delegate { if (!closing) action(); });
            };
            menu.Items.Add(item); return item;
        }
        void Help()
        {
            MessageBox.Show(this,
                "1. 打开或拖入 PDF。\n2. 拖动页面移动窗口，拖动边缘调整大小。\n3. Zoom 共享屏幕时，选择「PDF Stage」窗口。\n\n" +
                "单击 / 滚轮向下 / → / 空格：下一页\nShift + 单击 / 滚轮向上 / ←：上一页\nHome / End：第一页 / 最后一页\nCtrl + G：跳转页码\nCtrl + 1：窗口贴合页面比例\nCtrl + T：切换窗口置顶\nB：黑屏 / 恢复\n右键 / Esc：菜单；Alt + F4：退出\n\n" +
                "看讲稿时也能翻页：先按 Ctrl + H 启用，再使用\nCtrl + Alt + ← / →，即使焦点在讲稿窗口也有效。\n\n" +
                "页面始终保持比例、完整显示。比例不一致时保留黑边。\n菜单和对话框可能被共享；建议共享前完成文件选择。\n共享期间请勿最小化演示窗口。", "PDF Stage · 使用说明", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        string TextPrompt(string title, string label, string initial, bool password)
        {
            using (Form dialog = new Form { Text = title, Size = new Size(380, 180), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false, Font = new Font("Microsoft YaHei UI", 10) })
            {
                Label caption = new Label { Text = label, Left = 18, Top = 15, Width = 330 };
                TextBox input = new TextBox { Text = initial, Left = 20, Top = 44, Width = 320, UseSystemPasswordChar = password };
                Button ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Left = 174, Top = 88, Width = 78 };
                Button cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Left = 262, Top = 88, Width = 78 };
                dialog.Controls.AddRange(new Control[] { caption, input, ok, cancel }); dialog.AcceptButton = ok; dialog.CancelButton = cancel;
                dialog.Shown += delegate { input.Focus(); input.SelectAll(); };
                return dialog.ShowDialog(this) == DialogResult.OK ? input.Text : null;
            }
        }
        void ShowError(string message) { MessageBox.Show(this, message, "PDF Stage", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        void RestoreWindow()
        {
            try
            {
                string[] values = File.ReadAllText(settingsPath).Split(',');
                Rectangle r = new Rectangle(Int32.Parse(values[0]), Int32.Parse(values[1]), Int32.Parse(values[2]), Int32.Parse(values[3]));
                Rectangle area = Screen.FromRectangle(r).WorkingArea;
                r.Width = Math.Min(area.Width, Math.Max(320, r.Width)); r.Height = Math.Min(area.Height, Math.Max(180, r.Height));
                r.X = Math.Max(area.Left, Math.Min(r.X, area.Right - r.Width)); r.Y = Math.Max(area.Top, Math.Min(r.Y, area.Bottom - r.Height));
                StartPosition = FormStartPosition.Manual; Bounds = r;
            }
            catch { }
        }
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            closing = true;
            presentationMenu.Close();
            presentationMenu.Dispose();
            menuFont.Dispose();
            UnregisterHotKey(Handle, 101); UnregisterHotKey(Handle, 102);
            resizeTimer.Dispose(); wheelTimer.Dispose();
            engine.Dispose();
            if (frame != null) frame.Dispose();
            try
            {
                Rectangle r = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
                Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));
                File.WriteAllText(settingsPath, String.Join(",", r.X, r.Y, r.Width, r.Height));
            }
            catch { }
            base.OnFormClosed(e);
        }
    }
}
