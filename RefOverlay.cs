// RefOverlay - 参考图叠加器 (C# 5 / .NET Framework 4.x，系统自带运行时，无需安装)
// 把一张参考图变成可缩放 / 旋转 / 调透明度 / 点击穿透的半透明浮层，
// 盖在 SketchUp、Photoshop、Rhino 等任意窗口上，用来人工比对透视角度。

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace RefOverlay
{
    // ------------------------------------------------------------------ 原生 API
    internal static class Native
    {
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_LAYERED = 0x00080000;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_NOACTIVATE = 0x08000000;
        public const int ULW_ALPHA = 0x00000002;
        public const int WM_HOTKEY = 0x0312;
        public const int WM_ERASEBKGND = 0x0014;
        public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
        public const uint PW_RENDERFULLCONTENT = 0x00000002;
        public const uint GA_ROOT = 2;
        public const int MOD_ALT = 0x0001;
        public const int MOD_CONTROL = 0x0002;
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOACTIVATE = 0x0010;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }

        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE { public int cx, cy; public SIZE(int w, int h) { cx = w; cy = h; } }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;
            public int Width { get { return Right - Left; } }
            public int Height { get { return Bottom - Top; } }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
        {
            public uint biSize; public int biWidth; public int biHeight;
            public ushort biPlanes; public ushort biBitCount;
            public uint biCompression; public uint biSizeImage;
            public int biXPelsPerMeter; public int biYPelsPerMeter;
            public uint biClrUsed; public uint biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RGBQUAD { public byte b, g, r, reserved; }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFO { public BITMAPINFOHEADER bmiHeader; public RGBQUAD bmiColors; }

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("user32.dll")] public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextLength(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder text, int count);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);
        [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int processId);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT rect, int size);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hDC);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateRoundRectRgn(int l, int t, int r, int b, int w, int h);
        [DllImport("user32.dll")] public static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr hObject);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);

        // 把一张 32bpp 位图（含 per-pixel alpha）整体贴到分层窗口，同时应用全局不透明度
        public static bool ApplyLayered(IntPtr hwnd, Bitmap bmp, int opacityPercent, int x, int y)
        {
            if (hwnd == IntPtr.Zero || bmp == null) return false;
            int W = bmp.Width, H = bmp.Height;
            if (W <= 0 || H <= 0 || (long)W * H > 40000000L) return false;

            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memDc = CreateCompatibleDC(screenDc);
            IntPtr hDib = IntPtr.Zero;
            IntPtr oldObj = IntPtr.Zero;
            try
            {
                BITMAPINFO bmi = new BITMAPINFO();
                bmi.bmiHeader.biSize = (uint)Marshal.SizeOf(typeof(BITMAPINFOHEADER));
                bmi.bmiHeader.biWidth = W;
                bmi.bmiHeader.biHeight = -H;
                bmi.bmiHeader.biPlanes = 1;
                bmi.bmiHeader.biBitCount = 32;
                bmi.bmiHeader.biCompression = 0;

                IntPtr bits;
                hDib = CreateDIBSection(memDc, ref bmi, 0, out bits, IntPtr.Zero, 0);
                if (hDib == IntPtr.Zero) return false;
                oldObj = SelectObject(memDc, hDib);

                float alpha = Math.Max(0, Math.Min(100, opacityPercent)) / 100f;
                byte[] buf = new byte[W * 4 * H];
                BitmapData data = bmp.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    int stride = data.Stride;
                    if (stride == W * 4)
                    {
                        Marshal.Copy(data.Scan0, buf, 0, buf.Length);
                    }
                    else
                    {
                        byte[] row = new byte[W * 4];
                        for (int yy = 0; yy < H; yy++)
                        {
                            Marshal.Copy(new IntPtr(data.Scan0.ToInt64() + (long)yy * stride), row, 0, W * 4);
                            Buffer.BlockCopy(row, 0, buf, yy * W * 4, W * 4);
                        }
                    }
                }
                finally { bmp.UnlockBits(data); }

                // BGRA 直通 alpha -> 预乘 alpha
                for (int i = 0; i + 3 < buf.Length; i += 4)
                {
                    int a = buf[i + 3];
                    if (a == 0) { buf[i] = 0; buf[i + 1] = 0; buf[i + 2] = 0; continue; }
                    int na = (int)(a * alpha + 0.5f);
                    buf[i + 3] = (byte)na;
                    if (na == 0) { buf[i] = 0; buf[i + 1] = 0; buf[i + 2] = 0; continue; }
                    if (na != 255)
                    {
                        buf[i] = (byte)(buf[i] * na / 255);
                        buf[i + 1] = (byte)(buf[i + 1] * na / 255);
                        buf[i + 2] = (byte)(buf[i + 2] * na / 255);
                    }
                }
                Marshal.Copy(buf, 0, bits, buf.Length);

                POINT dst = new POINT(x, y);
                SIZE size = new SIZE(W, H);
                POINT src = new POINT(0, 0);
                BLENDFUNCTION bf = new BLENDFUNCTION();
                bf.BlendOp = 0;
                bf.BlendFlags = 0;
                bf.SourceConstantAlpha = 255;
                bf.AlphaFormat = 1;
                return UpdateLayeredWindow(hwnd, screenDc, ref dst, ref size, memDc, ref src, 0, ref bf, ULW_ALPHA);
            }
            finally
            {
                if (oldObj != IntPtr.Zero) SelectObject(memDc, oldObj);
                if (hDib != IntPtr.Zero) DeleteObject(hDib);
                if (memDc != IntPtr.Zero) DeleteDC(memDc);
                if (screenDc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screenDc);
            }
        }
    }

    // ------------------------------------------------------------------ 图像变换
    internal static class Composer
    {
        public const long MaxArea = 24000000L;

        public static void Measure(Image src, ref double zoom, double angleDeg, out int outW, out int outH)
        {
            double rad = angleDeg * Math.PI / 180.0;
            double c = Math.Abs(Math.Cos(rad)), s = Math.Abs(Math.Sin(rad));
            while (true)
            {
                double sc = zoom;
                int w = Math.Max(1, (int)Math.Round(src.Width * sc));
                int h = Math.Max(1, (int)Math.Round(src.Height * sc));
                int W = Math.Max(1, (int)Math.Ceiling(w * c + h * s));
                int H = Math.Max(1, (int)Math.Ceiling(w * s + h * c));
                if ((long)W * H <= MaxArea || zoom < 0.08f) { outW = W; outH = H; zoom = sc; return; }
                zoom *= 0.85;
            }
        }

        public static Bitmap Compose(Image src, double zoom, double angleDeg, bool flipH, bool flipV, out int outW, out int outH)
        {
            double z = zoom;
            Measure(src, ref z, angleDeg, out outW, out outH);
            int dw = Math.Max(1, (int)Math.Round(src.Width * z));
            int dh = Math.Max(1, (int)Math.Round(src.Height * z));

            Bitmap bmp = new Bitmap(outW, outH, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.CompositingMode = CompositingMode.SourceCopy;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TranslateTransform(outW / 2f, outH / 2f);
                if (angleDeg != 0) g.RotateTransform((float)angleDeg);
                g.ScaleTransform(flipH ? -1f : 1f, flipV ? -1f : 1f);
                g.DrawImage(src, new Rectangle(-dw / 2, -dh / 2, dw, dh), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel);
            }
            return bmp;
        }
    }

    // ------------------------------------------------------------------ 抓图
    internal static class Capturer
    {
        public static Bitmap LoadImageFile(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            using (MemoryStream ms = new MemoryStream(bytes))
            using (Image im = Image.FromStream(ms))
            {
                Bitmap bmp = new Bitmap(im.Width, im.Height, PixelFormat.Format32bppArgb);
                // 注意：这里必须用 GraphicsUnit.Pixel 的矩形重载。
                // DrawImageUnscaled 是按「物理尺寸」画的，会拿源图的 DPI 去换算目标 Graphics 的 DPI：
                // 300dpi 的图只会画到 96/300 那么大（缩在左上角，看着像被裁掉），
                // 72dpi 的图会被放大 96/72 倍（右边下边被切掉）。用 Pixel 单位就完全不管 DPI。
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(im, new Rectangle(0, 0, im.Width, im.Height), 0, 0, im.Width, im.Height, GraphicsUnit.Pixel);
                }
                return bmp;
            }
        }

        public static Bitmap Capture(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return null;
            Native.RECT r = new Native.RECT();
            int hr = Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out r, Marshal.SizeOf(typeof(Native.RECT)));
            if (hr != 0)
            {
                if (!Native.GetWindowRect(hwnd, out r)) return null;
            }
            int w = r.Width, h = r.Height;
            if (w <= 8 || h <= 8 || w > 16000 || h > 16000) return null;

            Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            bool ok;
            using (Graphics g = Graphics.FromImage(bmp))
            {
                IntPtr hdc = g.GetHdc();
                try { ok = Native.PrintWindow(hwnd, hdc, Native.PW_RENDERFULLCONTENT); }
                finally { g.ReleaseHdc(hdc); }
            }
            if (!ok || IsBlank(bmp)) return null;
            return bmp;
        }

        private static bool IsBlank(Bitmap bmp)
        {
            int stepX = Math.Max(1, bmp.Width / 24);
            int stepY = Math.Max(1, bmp.Height / 24);
            int nonZero = 0;
            for (int y = 0; y < bmp.Height; y += stepY)
            {
                for (int x = 0; x < bmp.Width; x += stepX)
                {
                    Color c = bmp.GetPixel(x, y);
                    if (c.A != 0 && (c.R | c.G | c.B) != 0) nonZero++;
                }
            }
            return nonZero < 3;
        }

        public static List<KeyValuePair<IntPtr, string>> ListWindows(IntPtr exclude)
        {
            List<KeyValuePair<IntPtr, string>> list = new List<KeyValuePair<IntPtr, string>>();
            int myPid = Process.GetCurrentProcess().Id;
            Native.EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                try
                {
                    if (!Native.IsWindowVisible(h)) return true;
                    if (h == exclude) return true;
                    int len = Native.GetWindowTextLength(h);
                    if (len <= 0) return true;
                    StringBuilder sb = new StringBuilder(len + 2);
                    Native.GetWindowText(h, sb, sb.Capacity);
                    string title = sb.ToString().Trim();
                    if (title.Length == 0) return true;

                    int pid;
                    Native.GetWindowThreadProcessId(h, out pid);
                    if (pid == myPid) return true;

                    int ex = Native.GetWindowLong(h, Native.GWL_EXSTYLE);
                    if ((ex & Native.WS_EX_TOOLWINDOW) != 0) return true;

                    Native.RECT r;
                    if (!Native.GetWindowRect(h, out r)) return true;
                    if (r.Width < 240 || r.Height < 160) return true;

                    StringBuilder cls = new StringBuilder(256);
                    Native.GetClassName(h, cls, cls.Capacity);
                    string cn = cls.ToString();
                    if (cn == "Progman" || cn == "WorkerW" || cn == "Shell_TrayWnd" || cn == "Windows.UI.Core.CoreWindow") return true;

                    list.Add(new KeyValuePair<IntPtr, string>(h, title));
                }
                catch { }
                return true;
            }, IntPtr.Zero);
            return list;
        }
    }

    // ------------------------------------------------------------------ 半透明浮层
    internal sealed class Overlay : Form
    {
        private readonly Image _img;
        private double _zoom = 1.0;
        private int _opacity = 70;
        private double _angle;
        private bool _flipH, _flipV, _locked, _blinkDim;
        private bool _rendering;
        private Bitmap _cached;
        private double _cz = -1, _ca;
        private bool _cfh, _cfv;
        private int _w = 1, _h = 1;
        private readonly Timer _debounce;

        public Overlay(Image image)
        {
            _img = image;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.Black;
            Cursor = Cursors.SizeAll;
            SetStyle(ControlStyles.Opaque | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            SetBounds(-8000, -8000, 8, 8);
            _debounce = new Timer();
            _debounce.Interval = 40;
            _debounce.Tick += delegate { _debounce.Stop(); Render(); };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_ERASEBKGND) { m.Result = (IntPtr)1; return; }
            base.WndProc(ref m);
        }

        public Image Image { get { return _img; } }
        public int OpacityPercent { get { return _opacity; } set { _opacity = Math.Max(0, Math.Min(100, value)); Request(); } }
        public double Zoom { get { return _zoom; } set { _zoom = Math.Max(0.03, Math.Min(6.0, value)); Request(); } }
        public double Angle { get { return _angle; } set { _angle = value; Request(); } }
        public bool FlipH { get { return _flipH; } set { _flipH = value; Request(); } }
        public bool FlipV { get { return _flipV; } set { _flipV = value; Request(); } }

        public bool Locked
        {
            get { return _locked; }
            set
            {
                _locked = value;
                if (!IsHandleCreated) return;
                int ex = Native.GetWindowLong(Handle, Native.GWL_EXSTYLE);
                if (_locked) ex |= Native.WS_EX_TRANSPARENT | Native.WS_EX_NOACTIVATE;
                else ex &= ~(Native.WS_EX_TRANSPARENT | Native.WS_EX_NOACTIVATE);
                ex |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW;
                Native.SetWindowLong(Handle, Native.GWL_EXSTYLE, ex);
            }
        }

        private int EffectiveOpacity { get { return _blinkDim ? 0 : _opacity; } }

        public void ToggleBlink()
        {
            _blinkDim = !_blinkDim;
            Request();
        }

        public bool IsBlinkDimmed { get { return _blinkDim; } }

        public Rectangle TargetArea()
        {
            // 优先用控制面板所在的那块屏，避免鼠标正好停在副屏上时浮层跑到另一块屏去
            if (HomeArea.Width > 0) return HomeArea;
            Screen s = Screen.FromPoint(Cursor.Position);
            if (s == null) s = Screen.PrimaryScreen;
            return s.WorkingArea;
        }

        public Rectangle HomeArea = Rectangle.Empty;

        public double FitScale()
        {
            Rectangle wa = TargetArea();
            double s = Math.Min(wa.Width * 0.92 / _img.Width, wa.Height * 0.92 / _img.Height);
            return Math.Max(0.05, Math.Min(6.0, s));
        }

        public void ShowOverlay()
        {
            double s = FitScale();
            _zoom = s;
            int W, H;
            Composer.Measure(_img, ref s, _angle, out W, out H);
            Rectangle wa = TargetArea();
            int cx = wa.X + wa.Width / 2;
            int cy = wa.Y + wa.Height / 2;
            _w = W; _h = H;
            SetBounds(cx - W / 2, cy - H / 2, W, H);
            if (!Visible) base.Show();
            int ex = Native.GetWindowLong(Handle, Native.GWL_EXSTYLE);
            ex |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW;
            Native.SetWindowLong(Handle, Native.GWL_EXSTYLE, ex);
            Render(cx, cy);
        }

        public void Request()
        {
            if (!IsHandleCreated) return;
            _debounce.Stop();
            _debounce.Start();
        }

        public void Center()
        {
            Rectangle wa = TargetArea();
            Render(wa.X + wa.Width / 2, wa.Y + wa.Height / 2);
        }

        public void Render() { Render(Left + Width / 2, Top + Height / 2); }

        private Bitmap EnsureComposed(out int W, out int H)
        {
            if (_cached != null && _cz == _zoom && _ca == _angle && _cfh == _flipH && _cfv == _flipV)
            {
                W = _cached.Width; H = _cached.Height;
                return _cached;
            }
            if (_cached != null) { _cached.Dispose(); _cached = null; }
            double z = _zoom;
            _cached = Composer.Compose(_img, z, _angle, _flipH, _flipV, out W, out H);
            _cz = _zoom; _ca = _angle; _cfh = _flipH; _cfv = _flipV;
            return _cached;
        }

        public void Render(int cx, int cy)
        {
            if (!IsHandleCreated || _rendering) return;
            _rendering = true;
            try
            {
                int W, H;
                Bitmap bmp = EnsureComposed(out W, out H);
                _w = W; _h = H;
                int nx = cx - W / 2;
                int ny = cy - H / 2;
                SetBounds(nx, ny, W, H);
                Native.ApplyLayered(Handle, bmp, EffectiveOpacity, nx, ny);
            }
            finally { _rendering = false; }
        }

        private bool _dragging;
        private Point _dragOffset;

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                _dragging = true;
                _dragOffset = new Point(e.X, e.Y);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging || _locked) return;
            Point p = PointToScreen(e.Location);
            int nx = p.X - _dragOffset.X;
            int ny = p.Y - _dragOffset.Y;
            SetBounds(nx, ny, _w, _h);
            Native.ApplyLayered(Handle, EnsureComposed(out _w, out _h), EffectiveOpacity, nx, ny);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = false;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (_locked) return;
            double f = e.Delta > 0 ? 1.04 : 1 / 1.04;
            Zoom = _zoom * f;
            Render();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _debounce.Stop();
                _debounce.Dispose();
                if (_cached != null) { _cached.Dispose(); _cached = null; }
            }
            base.Dispose(disposing);
        }
    }

    // ------------------------------------------------------------------ 视觉规范
    // 取色自 BYCJ Design 主界面：白底 / #F9FAFB 面板底 / #E5E7EB 细边 / 靛蓝 #5065EF 主色
    internal static class Palette
    {
        public static readonly Color Bg         = Color.FromArgb(0xFF, 0xFF, 0xFF);
        public static readonly Color BgAlt      = Color.FromArgb(0xF9, 0xFA, 0xFB);
        public static readonly Color Border     = Color.FromArgb(0xE5, 0xE7, 0xEB);
        public static readonly Color Border2    = Color.FromArgb(0xD1, 0xD5, 0xDB);
        public static readonly Color Text       = Color.FromArgb(0x10, 0x18, 0x28);
        public static readonly Color TextSub    = Color.FromArgb(0x4A, 0x55, 0x65);
        public static readonly Color TextMuted  = Color.FromArgb(0x9C, 0xA3, 0xAF);
        public static readonly Color Accent     = Color.FromArgb(0x50, 0x65, 0xEF);
        public static readonly Color AccentHov  = Color.FromArgb(0x43, 0x56, 0xD8);
        public static readonly Color AccentDown = Color.FromArgb(0x38, 0x49, 0xC0);
        public static readonly Color AccentTint = Color.FromArgb(0xED, 0xF0, 0xFE);
        public static readonly Color Hover      = Color.FromArgb(0xF3, 0xF4, 0xF6);
        public static readonly Color HoverDeep  = Color.FromArgb(0xE9, 0xEB, 0xEF);
        public static readonly Color Track      = Color.FromArgb(0xE5, 0xE7, 0xEB);
        public static readonly Color Danger     = Color.FromArgb(0xE5, 0x48, 0x4D);
        public static readonly Color DangerTint = Color.FromArgb(0xFD, 0xEC, 0xEC);
    }

    internal static class Gfx
    {
        public static GraphicsPath Round(Rectangle r, int rad)
        {
            GraphicsPath p = new GraphicsPath();
            int d = rad * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            if (rad <= 0 || d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void Fill(Graphics g, Rectangle r, int rad, Color c)
        {
            using (GraphicsPath p = Round(r, rad))
            using (SolidBrush b = new SolidBrush(c))
                g.FillPath(b, p);
        }

        public static void Stroke(Graphics g, Rectangle r, int rad, Color c)
        {
            Rectangle rr = new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1);
            using (GraphicsPath p = Round(rr, rad))
            using (Pen pen = new Pen(c, 1f))
                g.DrawPath(pen, p);
        }

        public static void Text(Graphics g, string s, Font f, Rectangle r, Color c, TextFormatFlags flags)
        {
            TextRenderer.DrawText(g, s, f, r, c, flags | TextFormatFlags.NoPadding);
        }

        public const TextFormatFlags Center =
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;
        public const TextFormatFlags LeftMid =
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;
        public const TextFormatFlags RightMid =
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;
    }

    // ------------------------------------------------------------------ 自绘控件
    internal sealed class RsButton : Control
    {
        public enum Kind { Secondary, Primary, Ghost }
        public enum Mark { None, Min, Close }

        public Kind Style = Kind.Secondary;
        public Mark Glyph = Mark.None;
        public bool DangerHover;
        public Color Surface = Palette.Bg;

        private bool _hover, _down;

        public RsButton(string text) : this(text, Kind.Secondary) { }

        public RsButton(string text, Kind kind)
        {
            Style = kind;
            Text = text;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Palette.Bg;
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
            Cursor = Cursors.Hand;
            Height = 28;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _down = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush bg = new SolidBrush(Surface)) g.FillRectangle(bg, ClientRectangle);

            Rectangle r = new Rectangle(0, 0, Width, Height);
            Color fill = Color.Empty;
            if (Style == Kind.Primary)
                fill = _down ? Palette.AccentDown : (_hover ? Palette.AccentHov : Palette.Accent);
            else if (_down)
                fill = Palette.HoverDeep;
            else if (_hover)
                fill = DangerHover ? Palette.DangerTint : Palette.Hover;

            if (!fill.IsEmpty) Gfx.Fill(g, r, 7, fill);
            if (Style == Kind.Secondary) Gfx.Stroke(g, r, 7, Palette.Border);

            Color fg = Style == Kind.Primary ? Color.White
                     : (DangerHover && _hover ? Palette.Danger : (Style == Kind.Ghost ? Palette.TextSub : Palette.Text));

            if (Glyph == Mark.None)
            {
                Gfx.Text(g, Text, Font, r, fg, Gfx.Center);
            }
            else
            {
                using (Pen pen = new Pen(fg, 1.4f))
                {
                    int cx = Width / 2, cy = Height / 2;
                    if (Glyph == Mark.Min) g.DrawLine(pen, cx - 5, cy, cx + 5, cy);
                    else
                    {
                        g.DrawLine(pen, cx - 4, cy - 4, cx + 4, cy + 4);
                        g.DrawLine(pen, cx + 4, cy - 4, cx - 4, cy + 4);
                    }
                }
            }
        }
    }

    internal sealed class RsSlider : Control
    {
        private int _min, _max, _val;
        private bool _drag;
        public event EventHandler ValueChanged;

        public RsSlider(int min, int max)
        {
            _min = min;
            _max = Math.Max(max, min + 1);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Palette.Bg;
            Height = 22;
            Cursor = Cursors.Hand;
        }

        public int Minimum { get { return _min; } set { _min = value; if (_max <= _min) _max = _min + 1; Invalidate(); } }
        public int Maximum { get { return _max; } set { _max = Math.Max(value, _min + 1); Invalidate(); } }

        public int Value
        {
            get { return _val; }
            set
            {
                int v = Math.Max(_min, Math.Min(_max, value));
                if (v == _val) return;
                _val = v;
                Invalidate();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }

        private int ThumbX
        {
            get
            {
                int pad = 8;
                int range = Width - pad * 2;
                if (range <= 0) return pad;
                double t = (_val - _min) / (double)(_max - _min);
                return pad + (int)Math.Round(range * t);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush bg = new SolidBrush(Palette.Bg)) g.FillRectangle(bg, ClientRectangle);

            int pad = 8, cy = Height / 2;
            int w = Width - pad * 2;
            if (w < 8) return;
            Rectangle track = new Rectangle(pad, cy - 2, w, 4);
            Gfx.Fill(g, track, 2, Palette.Track);

            int tx = ThumbX;
            int filled = tx - pad;
            if (filled > 1) Gfx.Fill(g, new Rectangle(track.X, track.Y, filled, 4), 2, Palette.Accent);

            using (SolidBrush white = new SolidBrush(Color.White)) g.FillEllipse(white, tx - 7, cy - 7, 14, 14);
            using (Pen pen = new Pen(Palette.Accent, 2f)) g.DrawEllipse(pen, tx - 6, cy - 6, 12, 12);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            _drag = true;
            Capture = true;
            SetFromX(e.X);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_drag) SetFromX(e.X);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _drag = false;
            Capture = false;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Value = _val + (e.Delta > 0 ? 1 : -1);
        }

        private void SetFromX(int x)
        {
            int pad = 8, range = Width - pad * 2;
            if (range <= 0) return;
            double t = (x - pad) / (double)range;
            if (t < 0) t = 0; else if (t > 1) t = 1;
            Value = _min + (int)Math.Round(t * (_max - _min));
        }
    }

    internal sealed class RsCheck : Control
    {
        private bool _checked;
        public event EventHandler CheckedChanged;

        public RsCheck(string text)
        {
            Text = text;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Palette.Bg;
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
            Height = 22;
            Cursor = Cursors.Hand;
        }

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked == value) return;
                _checked = value;
                Invalidate();
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            }
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            Checked = !Checked;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush bg = new SolidBrush(Palette.Bg)) g.FillRectangle(bg, ClientRectangle);

            Rectangle box = new Rectangle(0, (Height - 15) / 2, 15, 15);
            if (_checked)
            {
                Gfx.Fill(g, box, 4, Palette.Accent);
                using (Pen pen = new Pen(Color.White, 1.8f))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    g.DrawLines(pen, new Point[] {
                        new Point(box.X + 3, box.Y + 8),
                        new Point(box.X + 6, box.Y + 11),
                        new Point(box.X + 12, box.Y + 4)
                    });
                }
            }
            else
            {
                Gfx.Fill(g, box, 4, Color.White);
                Gfx.Stroke(g, box, 4, Palette.Border2);
            }
            Gfx.Text(g, Text, Font, new Rectangle(21, 0, Width - 21, Height), Palette.Text, Gfx.LeftMid);
        }
    }

    internal sealed class RsStepper : Control
    {
        private int _min, _max, _val;
        public event EventHandler ValueChanged;

        public RsStepper(int min, int max)
        {
            _min = min;
            _max = max;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Palette.Bg;
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
            Height = 26;
            Cursor = Cursors.Hand;
        }

        public int Minimum { get { return _min; } }
        public int Maximum { get { return _max; } }

        public int Value
        {
            get { return _val; }
            set
            {
                int v = Math.Max(_min, Math.Min(_max, value));
                if (v == _val) return;
                _val = v;
                Invalidate();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush bg = new SolidBrush(Palette.Bg)) g.FillRectangle(bg, ClientRectangle);

            Rectangle r = new Rectangle(0, 0, Width, Height);
            Gfx.Fill(g, r, 7, Color.White);
            Gfx.Stroke(g, r, 7, Palette.Border);

            int cy = Height / 2;
            using (Pen pen = new Pen(Palette.TextSub, 1.4f))
            {
                int lx = 13;
                g.DrawLine(pen, lx - 4, cy, lx + 4, cy);
                int rx = Width - 13;
                g.DrawLine(pen, rx - 4, cy, rx + 4, cy);
                g.DrawLine(pen, rx, cy - 4, rx, cy + 4);
            }
            Rectangle mid = new Rectangle(26, 0, Width - 52, Height);
            Gfx.Text(g, _val.ToString(), Font, mid, Palette.Text, Gfx.Center);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Value = _val + (e.X < Width / 2 ? -1 : 1);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Value = _val + (e.Delta > 0 ? 1 : -1);
        }
    }

    // 底部推广条：点一下打开 BYCJ Design 的 AI 生图站点（带 ref，方便后台统计来路）
    internal sealed class RsAdStrip : Control
    {
        public string Headline = "免费 AI 生图";
        public string Note = "新用户注册送 500 积分";
        public string Action = "前往 →";

        private static readonly Font FHead = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold, GraphicsUnit.Point);
        private static readonly Font FNote = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
        private static readonly Font FChip = new Font("Microsoft YaHei UI", 7.5f, FontStyle.Regular, GraphicsUnit.Point);
        private static readonly Font FAction = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold, GraphicsUnit.Point);
        private static readonly Color HoverFill = Color.FromArgb(0xE2, 0xE7, 0xFC);
        private static readonly Color ChipFill = Color.FromArgb(0xD8, 0xDF, 0xFB);

        private bool _hover;

        public RsAdStrip()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Palette.AccentTint;
            Cursor = Cursors.Hand;
            Height = 30;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush bg = new SolidBrush(Palette.Bg)) g.FillRectangle(bg, ClientRectangle);

            Gfx.Fill(g, new Rectangle(0, 0, Width, Height), 8, _hover ? HoverFill : Palette.AccentTint);

            Rectangle chip = new Rectangle(10, (Height - 16) / 2, 34, 16);
            Gfx.Fill(g, chip, 3, ChipFill);
            Gfx.Text(g, "推广", FChip, chip, Palette.Accent, Gfx.Center);

            Size hs = TextRenderer.MeasureText(Headline, FHead, Size.Empty, TextFormatFlags.NoPadding);
            Gfx.Text(g, Headline, FHead, new Rectangle(52, 0, hs.Width + 6, Height), Palette.Accent, Gfx.LeftMid);

            int noteX = 58 + hs.Width;
            Gfx.Text(g, "· " + Note, FNote, new Rectangle(noteX, 0, Math.Max(10, Width - noteX - 84), Height), Palette.TextSub, Gfx.LeftMid);

            Gfx.Text(g, Action, FAction, new Rectangle(Width - 78, 0, 68, Height), Palette.Accent, Gfx.RightMid);
        }
    }

    // ------------------------------------------------------------------ 控制面板
    internal sealed class PanelForm : Form
    {
        private Overlay _overlay;
        private Image _current;

        private RsButton _btnOpen, _btnGrab, _btnCap, _btnBlink, _btnFit, _btnCenter, _btnHide, _btnExit;
        private RsButton _btnMin, _btnClose;
        private RsSlider _trkOpacity, _trkZoom;
        private RsStepper _numRot;
        private RsCheck _chkH, _chkV, _chkLock;
        private Label _lblOpVal, _lblZoomVal, _lblStatus, _lblHint;
        private RsAdStrip _ad;

        // 推广入口：ref 参数用于后台区分来路。若你的注册流程用的是别的参数名（如 ?invite= / ?from=），
        // 只改这一行即可，不需要动别的地方。
        private const string AdUrl = "https://ai.bycjdesign.com/?ref=bycj-freeplugin&utm_source=freeplugin&utm_medium=win";
        private Timer _grabTimer;
        private int _countdown;
        private bool _sync;
        private ToolTip _tips;
        private bool _dragging;
        private Point _dragAt;
        private readonly Font _fTitle = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold, GraphicsUnit.Point);
        private readonly Font _fSmall = new Font("Microsoft YaHei UI", 8f, FontStyle.Regular, GraphicsUnit.Point);

        public PanelForm(string startImage)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Text = "参考图叠加器";
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;
            MinimizeBox = false;
            TopMost = true;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(566, 330);
            BackColor = Palette.Bg;
            Font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Regular, GraphicsUnit.Point);
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.X + 40, wa.Y + 40);
            AllowDrop = true;

            // ---- 图标栏按钮（自绘，跟系统的关闭按钮不一样的风格）----
            _btnMin = new RsButton("", RsButton.Kind.Ghost);
            _btnMin.Glyph = RsButton.Mark.Min;
            _btnMin.Surface = Palette.BgAlt;
            _btnMin.SetBounds(492, 6, 30, 22);
            _btnMin.Click += delegate { WindowState = FormWindowState.Minimized; };

            _btnClose = new RsButton("", RsButton.Kind.Ghost);
            _btnClose.Glyph = RsButton.Mark.Close;
            _btnClose.Surface = Palette.BgAlt;
            _btnClose.DangerHover = true;
            _btnClose.SetBounds(526, 6, 30, 22);
            _btnClose.Click += delegate { Close(); };

            // ---- 第 1 行：来源 ----
            _btnOpen = new RsButton("打开参考图…", RsButton.Kind.Primary);
            _btnOpen.SetBounds(16, 44, 132, 28);
            _btnGrab = new RsButton("抓取窗口 3s");
            _btnGrab.SetBounds(156, 44, 116, 28);
            _btnCap = new RsButton("选择窗口 ▾");
            _btnCap.SetBounds(280, 44, 116, 28);

            // ---- 第 2/3 行：两个自绘滑块 ----
            _trkOpacity = new RsSlider(5, 100);
            _trkOpacity.SetBounds(16, 102, 534, 22);
            _trkOpacity.Value = 70;
            _trkOpacity.ValueChanged += OnOpacityChanged;

            _trkZoom = new RsSlider(5, 300);
            _trkZoom.SetBounds(16, 152, 534, 22);
            _trkZoom.Value = 100;
            _trkZoom.ValueChanged += OnZoomChanged;

            _lblOpVal = MakeValue("70%", 446, 84, 104);
            _lblZoomVal = MakeValue("100%", 446, 134, 104);

            // ---- 第 4 行：旋转 / 翻转 / 锁定 / 闪烁 ----
            _numRot = new RsStepper(-180, 180);
            _numRot.SetBounds(56, 182, 104, 26);
            _numRot.ValueChanged += OnRotChanged;

            _chkH = new RsCheck("水平翻转");
            _chkH.SetBounds(176, 184, 86, 22);
            _chkV = new RsCheck("垂直翻转");
            _chkV.SetBounds(268, 184, 86, 22);
            _chkLock = new RsCheck("锁定穿透");
            _chkLock.SetBounds(360, 184, 86, 22);
            _chkLock.CheckedChanged += OnLockChanged;
            _chkH.CheckedChanged += OnFlipChanged;
            _chkV.CheckedChanged += OnFlipChanged;
            _btnBlink = new RsButton("闪烁对比");
            _btnBlink.SetBounds(452, 181, 98, 28);

            // ---- 第 5 行：操作按钮 ----
            _btnFit = new RsButton("铺满屏幕");
            _btnFit.SetBounds(16, 216, 86, 28);
            _btnCenter = new RsButton("居中");
            _btnCenter.SetBounds(108, 216, 68, 28);
            _btnHide = new RsButton("隐藏浮层");
            _btnHide.SetBounds(182, 216, 90, 28);
            _btnExit = new RsButton("退出", RsButton.Kind.Ghost);
            _btnExit.SetBounds(476, 216, 74, 28);

            // ---- 第 6 行：状态 + 热键 ----
            _lblStatus = new Label();
            _lblStatus.SetBounds(16, 252, 534, 18);
            _lblStatus.ForeColor = Palette.TextSub;
            _lblStatus.Font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Regular, GraphicsUnit.Point);
            _lblStatus.Text = "拖一张图片进来，或点「打开参考图…」";

            _lblHint = new Label();
            _lblHint.SetBounds(16, 270, 534, 16);
            _lblHint.ForeColor = Palette.TextMuted;
            _lblHint.Font = _fSmall;
            _lblHint.Text = "Ctrl+Alt+↑/↓ 透明度   ←/→ 缩放   L 锁定   B 闪烁   F 铺满   H 显示/隐藏   S 抓窗口";

            // ---- 底部：推广条（免费 AI 生图入口）----
            _ad = new RsAdStrip();
            _ad.SetBounds(14, 292, 538, 30);
            _ad.Click += delegate { OpenAd(); };

            Controls.AddRange(new Control[] {
                _btnMin, _btnClose,
                _btnOpen, _btnGrab, _btnCap,
                MakeCaption("不透明度", 16, 84, 120), _trkOpacity, _lblOpVal,
                MakeCaption("缩放", 16, 134, 120), _trkZoom, _lblZoomVal,
                MakeCaption("旋转", 16, 186, 36), _numRot,
                _chkH, _chkV, _chkLock, _btnBlink,
                _btnFit, _btnCenter, _btnHide, _btnExit,
                _lblStatus, _lblHint, _ad
            });

            // 控件太多放不下说明文字，用悬浮提示补充
            _tips = new ToolTip();
            _tips.AutoPopDelay = 12000;
            _tips.ShowAlways = true;
            _tips.SetToolTip(_btnGrab, "点一下，3 秒内把鼠标移到 SketchUp 视口上，自动抓成浮层");
            _tips.SetToolTip(_btnCap, "从当前所有窗口里挑一个抓成浮层");
            _tips.SetToolTip(_chkLock, "锁定后鼠标会穿过浮层，直接操作下面的 SketchUp；解锁才能拖动浮层");
            _tips.SetToolTip(_btnBlink, "在当前透明度和 0% 之间来回切换，靠闪动找角度差");
            _tips.SetToolTip(_btnHide, "临时藏起浮层（Ctrl+Alt+H 同效）");
            _tips.SetToolTip(_btnFit, "按比例铺满当前屏幕（Ctrl+Alt+F 同效）");

            _btnOpen.Click += OnOpen;
            _btnGrab.Click += OnGrabUnderCursor;
            _btnCap.Click += OnCaptureSelected;
            _btnBlink.Click += delegate { if (_overlay != null) _overlay.ToggleBlink(); };
            _btnFit.Click += delegate { if (_overlay != null) { _overlay.Zoom = _overlay.FitScale(); _overlay.Center(); SyncZoom(); } };
            _btnCenter.Click += delegate { if (_overlay != null) _overlay.Center(); };
            _btnHide.Click += delegate { ToggleOverlay(); };
            _btnExit.Click += delegate { Close(); };

            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;
            Activated += delegate { RaiseSelf(); };

            Shown += delegate
            {
                ApplyRoundRegion();
                if (!string.IsNullOrEmpty(startImage)) LoadFile(startImage);
            };
            FormClosed += delegate { Application.Exit(); };
            KeyPreview = true;
            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape && _overlay != null) { ToggleOverlay(); }
            };
        }

        // ---- 无边框窗口：圆角 + 阴影 + 自绘标题栏 ----
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= 0x00020000;   // CS_DROPSHADOW
                return cp;
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ApplyRoundRegion();
        }

        private void ApplyRoundRegion()
        {
            if (!IsHandleCreated) return;
            IntPtr rgn = Native.CreateRoundRectRgn(0, 0, Width + 1, Height + 1, 17, 17);
            Native.SetWindowRgn(Handle, rgn, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (SolidBrush b = new SolidBrush(Palette.Bg)) g.FillRectangle(b, ClientRectangle);
            using (SolidBrush b = new SolidBrush(Palette.BgAlt)) g.FillRectangle(b, 0, 0, Width, 34);
            using (Pen p = new Pen(Palette.Border, 1f)) g.DrawLine(p, 0, 33.5f, Width, 33.5f);

            using (SolidBrush b = new SolidBrush(Palette.Accent)) g.FillEllipse(b, 16, 13, 8, 8);
            Gfx.Text(g, "参考图叠加器", _fTitle, new Rectangle(31, 0, 200, 34), Palette.Text, Gfx.LeftMid);
            Gfx.Text(g, "RefOverlay", _fSmall, new Rectangle(126, 0, 120, 34), Palette.TextMuted, Gfx.LeftMid);

            // 整窗外描边（白色背景上也能看出边界）
            Gfx.Stroke(g, new Rectangle(0, 0, Width, Height), 8, Palette.Border);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && e.Y < 34)
            {
                _dragging = true;
                _dragAt = e.Location;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging) return;
            Point p = PointToScreen(e.Location);
            Location = new Point(p.X - _dragAt.X, p.Y - _dragAt.Y);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = false;
        }

        // WinForms 的 TrackBar 高度由系统固定为 45px（AutoSize=true 时），
        // 强行 AutoSize=false + 小高度会让滑块画在轨道最左边，所以滑块换成了自绘的 RsSlider。
        private Label MakeCaption(string text, int x, int y, int w)
        {
            Label l = new Label();
            l.Text = text;
            l.SetBounds(x, y, w, 16);
            l.AutoSize = false;
            l.TextAlign = ContentAlignment.MiddleLeft;
            l.ForeColor = Palette.TextSub;
            l.Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
            return l;
        }

        private Label MakeValue(string text, int x, int y, int w)
        {
            Label l = new Label();
            l.Text = text;
            l.SetBounds(x, y, w, 16);
            l.AutoSize = false;
            l.TextAlign = ContentAlignment.MiddleRight;
            l.ForeColor = Palette.Text;
            l.Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold, GraphicsUnit.Point);
            return l;
        }

        private void RaiseSelf()
        {
            if (!IsHandleCreated) return;
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        private void SetStatus(string s)
        {
            _lblStatus.Text = s;
        }

        private void OpenAd()
        {
            try
            {
                Process.Start(AdUrl);
                SetStatus("已打开浏览器：ai.bycjdesign.com（新注册送 500 积分）");
            }
            catch (Exception ex)
            {
                SetStatus("打开链接失败：" + ex.Message);
            }
        }

        // ---- 打开 / 拖入 / 抓取
        private void OnOpen(object sender, EventArgs e)
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = "选择参考图";
                d.Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff|所有文件|*.*";
                d.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                if (d.ShowDialog(this) == DialogResult.OK) LoadFile(d.FileName);
            }
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null && files.Length > 0) LoadFile(files[0]);
        }

        private void LoadFile(string path)
        {
            try
            {
                Cursor = Cursors.WaitCursor;
                Bitmap bmp = Capturer.LoadImageFile(path);
                SetImage(bmp, Path.GetFileName(path));
            }
            catch (Exception ex)
            {
                SetStatus("打开失败：" + ex.Message);
            }
            finally { Cursor = Cursors.Default; }
        }

        private void SetImage(Bitmap bmp, string label)
        {
            if (bmp == null) return;
            if (_overlay != null)
            {
                _overlay.Hide();
                _overlay.Dispose();
                _overlay = null;
            }
            if (_current != null) { _current.Dispose(); _current = null; }
            _current = bmp;
            _overlay = new Overlay(bmp);
            _overlay.HomeArea = Screen.FromControl(this).WorkingArea;
            _overlay.ShowOverlay();
            SyncAll();
            _btnHide.Text = "隐藏浮层";
            SetStatus(string.Format("已载入 {0}  ({1}×{2})", label, bmp.Width, bmp.Height));
            RaiseSelf();
        }

        private void ToggleOverlay()
        {
            if (_overlay == null) return;
            if (_overlay.Visible) { _overlay.Hide(); _btnHide.Text = "显示浮层"; }
            else { _overlay.Show(); _overlay.Request(); _btnHide.Text = "隐藏浮层"; }
            RaiseSelf();
        }

        private void OnGrabUnderCursor(object sender, EventArgs e)
        {
            if (_grabTimer != null) return;
            _countdown = 3;
            SetStatus("3 秒后抓取鼠标下的窗口…");
            _grabTimer = new Timer();
            _grabTimer.Interval = 1000;
            _grabTimer.Tick += delegate
            {
                _countdown--;
                if (_countdown > 0) { SetStatus(string.Format("{0} 秒后抓取鼠标下的窗口…", _countdown)); return; }
                _grabTimer.Stop();
                _grabTimer.Dispose();
                _grabTimer = null;
                Native.POINT pt = new Native.POINT(Cursor.Position.X, Cursor.Position.Y);
                IntPtr h = Native.WindowFromPoint(pt);
                h = Native.GetAncestor(h, Native.GA_ROOT);
                CaptureHandle(h);
            };
            _grabTimer.Start();
        }

        // 点「选择窗口 ▾」弹出窗口列表，选哪个就抓哪个（省掉一个拉低颜值的原生 ComboBox）
        private void OnCaptureSelected(object sender, EventArgs e)
        {
            List<KeyValuePair<IntPtr, string>> list = Capturer.ListWindows(Handle);
            if (list.Count == 0) { SetStatus("没有找到可抓取的窗口"); return; }

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.ShowImageMargin = false;
            menu.ShowCheckMargin = false;
            menu.RenderMode = ToolStripRenderMode.System;
            menu.Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
            foreach (KeyValuePair<IntPtr, string> kv in list)
            {
                IntPtr hwnd = kv.Key;
                string t = kv.Value;
                if (t.Length > 52) t = t.Substring(0, 52) + "…";
                ToolStripMenuItem item = new ToolStripMenuItem(t);
                item.Click += delegate { CaptureHandle(hwnd); };
                menu.Items.Add(item);
            }
            menu.Show(_btnCap, new Point(0, _btnCap.Height + 4));
        }

        private void CaptureHandle(IntPtr h)
        {
            if (h == IntPtr.Zero || h == Handle) { SetStatus("没找到目标窗口"); return; }
            Cursor = Cursors.WaitCursor;
            try
            {
                Bitmap bmp = Capturer.Capture(h);
                if (bmp == null) { SetStatus("抓取失败：窗口最小化、被遮挡，或它拒绝 PrintWindow"); return; }
                SetImage(bmp, "窗口截图");
            }
            finally { Cursor = Cursors.Default; }
        }

        // ---- 参数联动
        private void OnOpacityChanged(object sender, EventArgs e)
        {
            _lblOpVal.Text = _trkOpacity.Value + "%";
            if (_sync || _overlay == null) return;
            _overlay.OpacityPercent = _trkOpacity.Value;
        }

        private void OnZoomChanged(object sender, EventArgs e)
        {
            _lblZoomVal.Text = _trkZoom.Value + "%";
            if (_sync || _overlay == null) return;
            _overlay.Zoom = _trkZoom.Value / 100.0;
        }

        private void OnRotChanged(object sender, EventArgs e)
        {
            if (_sync || _overlay == null) return;
            _overlay.Angle = (double)_numRot.Value;
        }

        private void OnLockChanged(object sender, EventArgs e)
        {
            if (_sync || _overlay == null) return;
            _overlay.Locked = _chkLock.Checked;
            SetStatus(_chkLock.Checked ? "已锁定：鼠标会穿过浮层，直接操作下面的 SketchUp" : "已解锁：可以拖动 / 滚轮缩放浮层");
        }

        private void SyncAll()
        {
            _sync = true;
            try
            {
                _trkOpacity.Value = Math.Max(_trkOpacity.Minimum, Math.Min(_trkOpacity.Maximum, _overlay.OpacityPercent));
                _lblOpVal.Text = _trkOpacity.Value + "%";
                int z = (int)Math.Round(_overlay.Zoom * 100);
                z = Math.Max(_trkZoom.Minimum, Math.Min(_trkZoom.Maximum, z));
                _trkZoom.Value = z;
                _lblZoomVal.Text = z + "%";
                _numRot.Value = (int)Math.Round(_overlay.Angle);
                _chkH.Checked = _overlay.FlipH;
                _chkV.Checked = _overlay.FlipV;
                _chkLock.Checked = _overlay.Locked;
            }
            finally { _sync = false; }
        }

        private void SyncZoom()
        {
            if (_overlay == null) return;
            _sync = true;
            try
            {
                int z = (int)Math.Round(_overlay.Zoom * 100);
                z = Math.Max(_trkZoom.Minimum, Math.Min(_trkZoom.Maximum, z));
                _trkZoom.Value = z;
                _lblZoomVal.Text = z + "%";
            }
            finally { _sync = false; }
        }

        private void OnFlipChanged(object sender, EventArgs e)
        {
            if (_overlay == null) return;
            _overlay.FlipH = _chkH.Checked;
            _overlay.FlipV = _chkV.Checked;
        }

        // ---- 全局热键
        private const int HK_OP_UP = 1, HK_OP_DOWN = 2, HK_ZM_IN = 3, HK_ZM_OUT = 4,
                          HK_LOCK = 5, HK_HIDE = 6, HK_BLINK = 7, HK_FIT = 8, HK_GRAB = 9;
        private int _hkOk;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyRoundRegion();
            RegisterGlobalHotKeys();
        }

        private void RegisterGlobalHotKeys()
        {
            _hkOk = 0;
            int mods = Native.MOD_CONTROL | Native.MOD_ALT;
            int[,] keys = new int[,] {
                { HK_OP_UP, 0x26 }, { HK_OP_DOWN, 0x28 }, { HK_ZM_IN, 0x27 }, { HK_ZM_OUT, 0x25 },
                { HK_LOCK, 0x4C }, { HK_HIDE, 0x48 }, { HK_BLINK, 0x42 }, { HK_FIT, 0x46 }, { HK_GRAB, 0x53 }
            };
            for (int i = 0; i < keys.GetLength(0); i++)
            {
                if (Native.RegisterHotKey(Handle, keys[i, 0], mods, keys[i, 1])) _hkOk++;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                switch (id)
                {
                    case HK_OP_UP: if (_overlay != null) { _overlay.OpacityPercent = _overlay.OpacityPercent + 5; SyncAll(); } break;
                    case HK_OP_DOWN: if (_overlay != null) { _overlay.OpacityPercent = _overlay.OpacityPercent - 5; SyncAll(); } break;
                    case HK_ZM_IN: if (_overlay != null) { _overlay.Zoom = _overlay.Zoom * 1.05; SyncZoom(); } break;
                    case HK_ZM_OUT: if (_overlay != null) { _overlay.Zoom = _overlay.Zoom / 1.05; SyncZoom(); } break;
                    case HK_LOCK: if (_overlay != null) { _chkLock.Checked = !_overlay.Locked; } break;
                    case HK_HIDE: ToggleOverlay(); break;
                    case HK_BLINK: if (_overlay != null) _overlay.ToggleBlink(); break;
                    case HK_FIT: if (_overlay != null) { _overlay.Zoom = _overlay.FitScale(); _overlay.Center(); SyncZoom(); } break;
                    case HK_GRAB: OnGrabUnderCursor(this, EventArgs.Empty); break;
                }
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            for (int i = 1; i <= 9; i++) Native.UnregisterHotKey(Handle, i);
            if (_overlay != null) { _overlay.Dispose(); _overlay = null; }
            if (_current != null) { _current.Dispose(); _current = null; }
            base.OnFormClosing(e);
        }
    }

    // ------------------------------------------------------------------ 入口
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Native.SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (args.Length >= 3 && args[0] == "--compose")
            {
                // 自检：--compose <in> <out> <zoom> <angle> <flipHV>
                Image src = Capturer.LoadImageFile(args[1]);
                double zoom = double.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture);
                double angle = double.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture);
                string hv = args.Length > 5 ? args[5] : "--";
                int W, H;
                using (Bitmap bmp = Composer.Compose(src, zoom, angle, hv.IndexOf('h') >= 0, hv.IndexOf('v') >= 0, out W, out H))
                {
                    bmp.Save(args[2], ImageFormat.Png);
                    Console.WriteLine("OK " + W + "x" + H);
                }
                return;
            }

            string start = null;
            foreach (string a in args) if (File.Exists(a)) start = a;
            Application.Run(new PanelForm(start));
        }
    }
}
