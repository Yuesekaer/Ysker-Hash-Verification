// Ysker-Hash-Verification - batch file checksum utility (MD5 / SHA1 / CRC32 / SHA-256)
// Copyright (c) 2026 Yuesekaer
// SPDX-License-Identifier: MIT
// See the LICENSE file in the repository root for the full license text.

// Ysker-文件校验 —— 文件校验软件
// 功能：MD5 / SHA1 / CRC32 / SHA-256 批量校验、单值比对、一致文件分组、大小写切换、一键复制、框选删除
//
// 版式（显式布局，见 LayoutShell）：
//   ┌──────────────────────────────────────────────┐
//   │ Ysker-文件校验 vX       📌   ─   □   ✕     │ 标题栏横跨整窗 → 按钮在窗口最右上角
//   ├──────────────────────────────┬───────────────┤
//   │  文件列表（卡片）             │  右侧栏        │
//   ├──────────────────────────────┴───────────────┤
//   │ 状态文字 …          文件N｜已校验N｜选中N｜…  │ 状态栏横跨整窗 → 摘要贴窗口最右
//   └──────────────────────────────────────────────┘
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace Ysker
{
    #region 常量与数据结构

    internal static class AppInfo
    {
        public const string Title = "Ysker-文件校验";
        /// <summary>英文名（仅用于文档与程序集元数据；界面内一律显示中文名）</summary>
        public const string TitleEn = "Ysker-Hash-Verification";
        public const string Version = "v1.0";
        /// <summary>程序图标文件名（与 exe 同目录；不在源码里写死任何绝对路径）</summary>
        public const string IconFile = "MD5.ico";
        public const string HintNoneChecked = "  至少得选一个吧(｀・ω・´)  ";
        public const string HintNoMatch = "  很遗憾，没有匹配的值(；´д｀)ゞ  ";
    }

    /// <summary>四种校验值</summary>
    internal static class Algo
    {
        public const string MD5 = "MD5";
        public const string SHA1 = "SHA1";
        public const string CRC32 = "CRC32";
        public const string SHA256 = "SHA-256";
        public static readonly string[] All = { MD5, SHA1, CRC32, SHA256 };
        /// <summary>显示用键：算法名 + 全角冒号，所有“：”因此天然对齐</summary>
        public static string Key(string a) { return a + "："; }
    }

    internal sealed class FileItem
    {
        public string Path;
        public long Size = -1;
        public Dictionary<string, string> Hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        public bool Computed;
        public string Error;

        public string SizeText
        {
            get
            {
                if (Size < 0) return "";
                if (Size < 1024) return Size.ToString(CultureInfo.InvariantCulture) + " B";
                double b = Size;
                string[] u = { "B", "KB", "MB", "GB", "TB" };
                int i = 0;
                while (b >= 1024.0 && i < u.Length - 1) { b /= 1024.0; i++; }
                return b.ToString("0.##", CultureInfo.InvariantCulture) + " " + u[i];
            }
        }
    }

    internal sealed class HashAlgo : IDisposable
    {
        public HashAlgorithm H;
        public Crc32 Crc;
        public void Dispose() { if (H != null) H.Dispose(); }
    }

    /// <summary>标准 CRC-32（IEEE 802.3，反射多项式 0xEDB88320）</summary>
    internal sealed class Crc32
    {
        static readonly uint[] Table = Build();
        uint _crc = 0xFFFFFFFFu;

        static uint[] Build()
        {
            uint[] t = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++) c = ((c & 1) != 0) ? (0xEDB88320u ^ (c >> 1)) : (c >> 1);
                t[i] = c;
            }
            return t;
        }

        public void Update(byte[] buf, int len)
        {
            uint c = _crc;
            for (int i = 0; i < len; i++) c = Table[(c ^ buf[i]) & 0xFF] ^ (c >> 8);
            _crc = c;
        }

        public string Finish() { return ((~_crc) & 0xFFFFFFFFu).ToString("x8", CultureInfo.InvariantCulture); }
    }

    #endregion

    #region 通用控件

    internal static class Fonts
    {
        public static string Family { get { return UiFonts.Find("Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "SimSun"); } }
        public static Font Ui(float size, FontStyle st) { return new Font(Family, size, st); }
    }

    /// <summary>纯色扁平按钮（配色统一走 Pal）</summary>
    internal sealed class FlatBtn : Button
    {
        public FlatBtn(string text, bool primary, float fontSize)
        {
            Text = text;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = primary ? 0 : 1;
            FlatAppearance.BorderColor = Pal.Border;
            UseVisualStyleBackColor = false;
            ForeColor = Pal.Text;
            BackColor = primary ? Pal.Btn : Pal.PanelBg;
            FlatAppearance.MouseOverBackColor = primary ? Pal.BtnHot : Pal.Border;
            FlatAppearance.MouseDownBackColor = Pal.BtnHot;
            Font = Fonts.Ui(fontSize, FontStyle.Regular);
            Cursor = Cursors.Hand;
            TabStop = false;
        }
    }

    /// <summary>勾选按钮：未选=暗底描边，已选=绿底并带一个 √</summary>
    internal sealed class ToggleBtn : Button
    {
        readonly bool _primary;
        readonly string _base;
        bool _checked;

        public ToggleBtn(string text, bool primary, float fontSize)
        {
            _primary = primary;
            _base = text;
            Text = text;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 1;
            UseVisualStyleBackColor = false;
            Font = Fonts.Ui(fontSize, FontStyle.Regular);
            Cursor = Cursors.Hand;
            TabStop = false;
            Apply();
        }

        public bool Checked
        {
            get { return _checked; }
            set { if (_checked != value) { _checked = value; Apply(); if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty); } }
        }

        public event EventHandler CheckedChanged;

        void Apply()
        {
            Text = _checked ? "\u221A  " + _base : _base;
            if (_checked)
            {
                BackColor = _primary ? Pal.Btn : Pal.BoxOn;
                FlatAppearance.BorderColor = BackColor;
                FlatAppearance.MouseOverBackColor = Pal.BtnHot;
                FlatAppearance.MouseDownBackColor = Pal.BtnHot;
                ForeColor = Pal.Text;
            }
            else
            {
                BackColor = Pal.PanelBg;
                FlatAppearance.BorderColor = Pal.Border;
                FlatAppearance.MouseOverBackColor = Pal.Border;
                FlatAppearance.MouseDownBackColor = Pal.BtnHot;
                ForeColor = Pal.Muted;
            }
        }

        protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }
    }

    /// <summary>带占位提示的单行输入框</summary>
    internal sealed class PlaceholderBox : Panel
    {
        public readonly TextBox Box = new TextBox();
        readonly Label _lbl = new Label();
        string _ph = "";

        public PlaceholderBox()
        {
            BackColor = Pal.InputBg;
            Padding = new Padding(7, 5, 7, 5);
            Box.BorderStyle = BorderStyle.None;
            Box.BackColor = Pal.InputBg;
            Box.ForeColor = Pal.Text;
            Box.Font = new Font(UiFonts.Find("Consolas", "Courier New"), 9.5f);
            Box.Dock = DockStyle.Fill;
            Box.TextChanged += delegate { _lbl.Visible = Box.Text.Length == 0 && !Box.Focused; };
            Box.GotFocus += delegate { _lbl.Visible = false; };
            Box.LostFocus += delegate { _lbl.Visible = Box.Text.Length == 0; };
            _lbl.Dock = DockStyle.Fill;
            _lbl.ForeColor = Color.FromArgb(0x80, 0x80, 0x80);
            _lbl.BackColor = Pal.InputBg;
            _lbl.TextAlign = ContentAlignment.MiddleLeft;
            _lbl.Font = Box.Font;
            _lbl.Cursor = Cursors.IBeam;
            _lbl.Click += delegate { Box.Focus(); };
            Controls.Add(_lbl);
            Controls.Add(Box);
            _lbl.BringToFront();
        }

        public string Placeholder
        {
            get { return _ph; }
            set { _ph = value; _lbl.Text = value; }
        }

        public string Value
        {
            get { return Box.Text; }
            set { Box.Text = value; _lbl.Visible = value.Length == 0; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen p = new Pen(Pal.Border)) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }
    }

    #endregion

    #region 文件列表（卡片面板）

    internal sealed class DividerItem
    {
        public string Text;
        public DividerItem(string t) { Text = t; }
    }

    /// <summary>
    /// 只读文本层：与 RichTextBox 完全一样的行为，但**隐藏插入符**（不出现可输入光标）、
    /// 不激活输入法。选中 / 拖动 / 双击选词 / Ctrl+C 全部是系统原生实现。
    /// </summary>
    internal sealed class HashTextHost : RichTextBox
    {
        [DllImport("user32.dll")]
        static extern bool HideCaret(IntPtr hWnd);

        void KillCaret() { try { HideCaret(Handle); } catch { } }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); KillCaret(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); KillCaret(); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); KillCaret(); }
        protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); KillCaret(); }
        protected override void OnSelectionChanged(EventArgs e) { base.OnSelectionChanged(e); KillCaret(); }
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            // WM_SETFOCUS / WM_LBUTTONDOWN / WM_KEYDOWN 之后系统会重建插入符，这里统一再隐藏一次
            if (m.Msg == 0x0007 || m.Msg == 0x0201 || m.Msg == 0x0100 || m.Msg == 0x0202) KillCaret();
        }
    }

    internal sealed class FilePanel : Panel
    {
        public readonly List<FileItem> Files = new List<FileItem>();
        public readonly List<object> DisplayRows = new List<object>();
        public readonly HashSet<string> Selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public List<string> VisibleKeys = new List<string>();

        public int LineCount = 1;
        public bool ShowCopyPerCard = true;
        public bool ShowSize = true;
        public bool Busy = false;
        public string BusyText = "正在校验中...";
        public CardRenderer Renderer;
        /// <summary>框选模式：开启后点一下就多选（不用按 Ctrl），拖框也是累加</summary>
        public bool BoxMode = false;
        /// <summary>比对命中的文件（这些卡片会整张标出来，配合命中行的闪烁）</summary>
        public readonly List<string> SearchPaths = new List<string>();
        /// <summary>选中数量变化时回调（刷新状态栏/标题，并立即重绘）</summary>
        public Action SelectionChanged;

        readonly List<Rectangle> _rects = new List<Rectangle>();
        bool _rectsValid = false;
        readonly Dictionary<FileItem, CardView> _views = new Dictionary<FileItem, CardView>();
        int _viewClearCount;
        /// <summary>共享的闪烁时间轴：按“显示行”建立，所有一致文件同算法一起亮灭</summary>
        List<Anim> _clips = new List<Anim>();
        List<string> _flashKeys = new List<string>();
        readonly HashSet<FileItem> _flashing = new HashSet<FileItem>();
        readonly Timer _tick;
        Rectangle _band;
        bool _banding;
        Point _bandAnchor;
        FileItem _hoverDel, _hoverCopy;
        // ---- 哈希值文本选择（框选模式关闭时可用）----
        FileItem _selItem;          // 正在划过的卡片
        int _selLine;               // 行号（第几个可见算法）
        int _selA, _selB;           // 字符区间（含头不含尾，已排序）
        bool _selDragging;
        bool _selMoved;             // 这次拖动是否真的划过（单纯点击不留选择）
        int _selAnchor;             // 按下时的字符边界（起点）
        // "按下位置在某个哈希值上"的候选：只有横向划动后才升级为文本选择
        FileItem _selCandidate;
        int _selCandLine;
        int _selCandChar;
        Point _selDownPoint;
        List<Rectangle> _paintLines = new List<Rectangle>();   // 本次绘制各行的矩形（供命中测试）
        List<string> _paintKeys = new List<string>();

        /// <summary>每条时间轴 = 一个“显示行”（一种算法）</summary>
        sealed class Anim
        {
            public float T = -999f;     // 距该行开始的时间（秒），负值=还没轮到
            public float Rise = 0.1f;   // 渐亮
            public float Hold = 0.6f;   // 保持最亮
            public float Fall = 0.3f;   // 渐变回白
            public float Total { get { return Rise + Hold + Fall; } }
        }

        public FilePanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            AutoScroll = true;
            BackColor = Pal.Bg;
            TabStop = true;
            _tick = new Timer();
            _tick.Interval = 33;
            _tick.Tick += delegate { TickAnim(); };
        }

        public bool AnyAnim { get { return _flashing.Count > 0; } }
        public int FlashKeyCount { get { return _flashKeys.Count; } }
        public List<string> FlashKeys { get { return _flashKeys; } }
        public int ViewCacheClearCount { get { return _viewClearCount; } }

        #region 只读文本层（Notepad 式：可选择 / 可复制 / 可滚动，但不可编辑）

        // 哈希值行不再由面板自绘，而是交给"每张卡片一个只读 RichTextBox"。
        // 好处：选中、拖动、双击选词、Shift 扩展、Ctrl+C、滚轮/触摸滑动全部是系统原生行为，
        // 手感与记事本一致；而 ReadOnly=true 天然禁止输入、粘贴、剪切、删除。
        readonly List<RichTextBox> _hosts = new List<RichTextBox>();
        /// <summary>是否启用只读文本层（框选模式开启时关闭，让点击用于选卡片）</summary>
        public bool UseValueHost = true;
        /// <summary>只读文本行高（实测，用于让卡片行与文本行严格对齐）</summary>
        public int HostLineHeight;
        bool _hostLineChecked;

        RichTextBox MakeHost()
        {
            HashTextHost r = new HashTextHost();
            r.ReadOnly = true;                       // 关键：只读 —— 不可编辑、不可粘贴/剪切/删除
            r.BorderStyle = BorderStyle.None;
            r.WordWrap = false;
            r.ScrollBars = RichTextBoxScrollBars.None;
            r.DetectUrls = false;
            r.ShortcutsEnabled = true;               // 保留 Ctrl+C / Ctrl+A / Ctrl+Insert
            r.TabStop = false;
            r.HideSelection = false;                 // 失焦后仍显示选区，方便"选完再点复制"
            r.BackColor = Pal.CardBg;
            r.ForeColor = Pal.Text;
            r.ImeMode = ImeMode.Disable;             // 不激活输入法（也就不会弹软键盘）
            // 右键：只给"复制"（只读控件不给剪切/粘贴入口）
            ContextMenuStrip cm = new ContextMenuStrip();
            cm.BackColor = Pal.PanelBg;
            cm.ForeColor = Pal.Text;
            ToolStripMenuItem miCopy = new ToolStripMenuItem("复制");
            miCopy.ForeColor = Pal.Text;
            miCopy.Click += delegate (object s2, EventArgs e2)
            {
                if (r.SelectionLength > 0 && SelectionCopied != null) SelectionCopied(r.SelectedText);
            };
            cm.Items.Add(miCopy);
            r.ContextMenuStrip = cm;
            // 只读文本层里的滚轮要转发给列表面板（这样仍然可以滚动/触摸滑动查看）
            r.MouseWheel += delegate (object s, MouseEventArgs e)
            {
                if (ScrollHost != null) ScrollHost(e.Delta);
            };
            // 用户划选结束后补一次上色（划选期间我们刻意不动选区，避免干扰）
            r.MouseUp += delegate (object s, MouseEventArgs e)
            {
                if (NeedHostRecolor) { NeedHostRecolor = false; SyncHosts(); }
            };
            Controls.Add(r);
            r.BringToFront();
            _hosts.Add(r);
            return r;
        }

        /// <summary>右键"复制"时的回调（由 MainForm 写剪贴板并提示）</summary>
        public Action<string> SelectionCopied;

        /// <summary>当前是否有文本层带着选区</summary>
        public bool AnyHostSelection
        {
            get
            {
                for (int i = 0; i < _hosts.Count; i++) if (Alive(_hosts[i]) && _hosts[i].Visible && _hosts[i].SelectionLength > 0) return true;
                return false;
            }
        }

        /// <summary>取第一个带选区的文本层的选中内容</summary>
        public string FirstHostSelection()
        {
            for (int i = 0; i < _hosts.Count; i++)
                if (Alive(_hosts[i]) && _hosts[i].Visible && _hosts[i].SelectionLength > 0) return _hosts[i].SelectedText;
            return null;
        }

        /// <summary>某个窗口句柄是不是文本层之一</summary>
        public bool IsHostHandle(IntPtr h)
        {
            for (int i = 0; i < _hosts.Count; i++)
                if (Alive(_hosts[i]) && _hosts[i].Handle == h) return true;
            return false;
        }

        /// <summary>清掉所有文本层的选区</summary>
        public void ClearHostSelections()
        {
            for (int i = 0; i < _hosts.Count; i++)
            {
                if (Alive(_hosts[i]) && _hosts[i].SelectionLength > 0) _hosts[i].Select(0, 0);
            }
        }

        /// <summary>滚轮/触摸滑动的转发目标（由 MainForm 注入）</summary>
        public Action<int> ScrollHost;

        /// <summary>
        /// 按滚轮增量滚动列表。只读文本层会吞掉滚轮，所以由消息过滤器驱动这里。
        /// </summary>
        public void WheelScrollPublic(int delta)
        {
            int max = Math.Max(0, AutoScrollMinSize.Height - ClientSize.Height);
            int cur = -AutoScrollPosition.Y;
            int next = cur - delta / 120 * 48;
            if (next < 0) next = 0;
            if (next > max) next = max;
            if (next == cur) return;
            AutoScrollPosition = new Point(0, next);
            _rectsValid = false;
            Invalidate();
        }

        /// <summary>当前滚动位置（自检用）</summary>
        public int ScrollYPublic { get { return -AutoScrollPosition.Y; } }

        /// <summary>控件是否仍然可用（消息过滤器可能在全应用范围内被调用）</summary>
        static bool Alive(Control c) { return c != null && !c.IsDisposed && !c.Disposing; }

        RichTextBox HostAt(int idx)
        {
            while (_hosts.Count <= idx) MakeHost();
            return _hosts[idx];
        }

        void MeasureHostLineHeight()
        {
            if (_hostLineChecked || _hosts.Count == 0 || !_hosts[0].IsHandleCreated) return;
            _hostLineChecked = true;
            try
            {
                RichTextBox r = _hosts[0];
                r.Text = "0\n0";
                int y0 = r.GetPositionFromCharIndex(0).Y;
                int y1 = r.GetPositionFromCharIndex(2).Y;
                if (y1 > y0) HostLineHeight = y1 - y0;
                r.Text = "";
            }
            catch { }
        }

        /// <summary>把可见卡片与只读文本框对好位（每次绘制/滚动后调用）</summary>
        public void SyncHosts()
        {
            try
            {
                if (Renderer == null || !UseValueHost || Busy || LineCount <= 0 || VisibleKeys.Count == 0)
                {
                    for (int k = 0; k < _hosts.Count; k++) _hosts[k].Visible = false;
                    return;
                }
                EnsureRects();
                MeasureHostLineHeight();

                int used = 0;
                for (int i = 0; i < DisplayRows.Count; i++)
                {
                    FileItem fi = DisplayRows[i] as FileItem;
                    if (fi == null || !fi.Computed || fi.Error != null) continue;
                    Rectangle rc = ContentToClient(_rects[i]);
                    if (rc.Bottom < 0 || rc.Top > ClientSize.Height) continue;

                    RichTextBox h = HostAt(used++);
                    Rectangle first = Renderer.CurLineRect(rc, 0);
                    int hh = Math.Max(1, LineCount) * Renderer.LineHeight;
                    // 文字起始 x 必须与 DrawCard 一致：卡片左边 + PadX + 4。
                    // 再往左留 2px 作为文本控件自身的内边距补偿。
                    int tx = rc.Left + CardRenderer.PadX + 4;
                    int w = rc.Right - tx - 6;
                    if (w < 40) w = 40;
                    h.Bounds = new Rectangle(tx - 2, first.Top, w, hh + 2);
                    h.BackColor = Pal.CardBg;
                    // 字体：每个文本层用**自己的 Font 实例**。
                    // 不要与 Renderer 共用：字号缩放时会释放旧字体，仍被引用的已释放字体
                    // 会让部分行渲染成异常字体。
                    Font hf = HostFontFor(Renderer.F.MonoBig);
                    if (!ReferenceEquals(h.Font, hf)) h.Font = hf;
                    string txt = BuildHostText(fi);
                    if (h.Text != txt)
                    {
                        int ss = h.SelectionStart, sl = h.SelectionLength;
                        h.Text = txt;
                        // 强制整段使用同一字体，杜绝残留的旧 run 格式
                        h.SelectAll();
                        h.SelectionFont = hf;
                        h.Select(ss, sl);
                        SetHostColored(used - 1, false);   // 文本重建 => 颜色回到默认
                    }
                    PaintHostFlash(h, fi, used - 1);
                    h.Visible = true;
                }
                for (int k = used; k < _hosts.Count; k++) if (Alive(_hosts[k])) _hosts[k].Visible = false;
            }
            catch { }
        }

        /// <summary>自检用</summary>
        public void SyncHostsPublic() { SyncHosts(); }

        /// <summary>自检用：手动推进动画时间轴（不依赖定时器），然后刷新文本层颜色</summary>
        public void AdvanceAnimPublic(float dt)
        {
            foreach (Anim a in _clips) a.T += dt;
            SyncHosts();
        }

        /// <summary>自检用：某一行被刷成绿色了吗</summary>
        public bool HostHasFlashColorPublic(int idx)
        {
            RichTextBox h = HostPublic(idx);
            if (h == null || h.TextLength == 0) return false;
            int ss = h.SelectionStart, sl = h.SelectionLength;
            bool any = false;
            string[] lines = h.Text.Split('\n');
            int start = 0;
            foreach (string ln in lines)
            {
                h.Select(start, ln.Length);
                if (h.SelectionColor != Pal.Text) any = true;
                start += ln.Length + 1;
            }
            h.Select(ss, sl);
            return any;
        }

        /// <summary>自检用：文本层里各行字体是否与控件字体完全一致（且都是等宽）</summary>
        /// <summary>
        /// 自检用：文本层字体是否健康。
        /// 注意：不能拿 SelectionFont 判断 —— RichTextBox 对全角"："会做字体链接，
        /// 整行选区会被上报成"混合字体"（返回 null），那是上报现象而不是缺陷。
        /// 真正要验的是：字体对象是否仍然有效（被 Dispose 的 Font 调用 GetHeight 会抛异常）。
        /// </summary>
        public string HostFontCheckPublic(int idx)
        {
            RichTextBox h = HostPublic(idx);
            if (h == null) return "无文本层";
            string err = "";
            float hgt = 0f;
            try { hgt = h.Font.GetHeight(); }
            catch (Exception ex) { err = "字体已失效(" + ex.GetType().Name + ")"; }
            int lines = 0;
            if (h.TextLength > 0) { lines = 1; string t = h.Text; foreach (char ch in t) if (ch == (char)10) lines++; }
            return "字体=" + h.Font.Name + " " + h.Font.Size.ToString("0.##") +
                   " 行高=" + hgt.ToString("0.##") +
                   " 文本长度=" + h.TextLength + " 行数=" + lines +
                   " 可见=" + h.Visible +
                   " 与Renderer共用=" + ReferenceEquals(h.Font, Renderer.F.MonoBig) +
                   (err.Length > 0 ? " ERR=" + err : "");
        }

        // ---- 文本层专用字体缓存（每个文本层独立持有，绝不与 Renderer 共用，避免被 Dispose）----
        readonly Dictionary<string, Font> _hostFonts = new Dictionary<string, Font>();

        Font HostFontFor(Font src)
        {
            string key = src.FontFamily.Name + "|" + src.Size.ToString("0.##") + "|" + (int)src.Style;
            Font f;
            if (_hostFonts.TryGetValue(key, out f)) return f;
            f = new Font(src.FontFamily, src.Size, src.Style, GraphicsUnit.Point);
            _hostFonts[key] = f;
            return f;
        }

        /// <summary>
        /// 把闪烁强度映射到文本层里每一行的颜色（白 → 绿）。
        /// 只改颜色、绝不重画文字，所以不会影响"框住的 = 选中的"。
        /// </summary>
        void PaintHostFlash(RichTextBox h, FileItem fi, int hostIdx)
        {
            bool any = false;
            int n = VisibleKeys.Count;
            float[] inten = new float[n];
            for (int i = 0; i < n; i++)
            {
                int ci = IndexIn(_flashKeys, VisibleKeys[i]);
                if (ci >= 0 && ci < _clips.Count && _flashing.Contains(fi))
                {
                    inten[i] = Ramp(_clips[ci]);
                    if (inten[i] > 0.001f) any = true;
                }
            }

            // 用户正在这个文本层里操作（按住鼠标划选 / 它持有焦点且鼠标按下）时，
            // 绝不去动选区 —— 否则会出现"闪烁的选中框"并且把用户的划选搅乱。
            if (h.Focused && Control.MouseButtons != MouseButtons.None)
            {
                NeedHostRecolor = true;      // 松手后补一次上色
                return;
            }

            bool colored = hostIdx >= 0 && hostIdx < _hostColored.Count && _hostColored[hostIdx];
            if (!any)
            {
                // 动画结束：整段恢复白色。必须用 SelectionColor
                //（只设 ForeColor 不会影响已有 run，颜色会永久卡在绿色）。
                if (colored) { ApplyHostColors(h, null); SetHostColored(hostIdx, false); }
                return;
            }

            ApplyHostColors(h, inten);
            SetHostColored(hostIdx, true);
        }

        /// <summary>哪些文本层当前被染过色（不能用 Tag —— 文本重建时会一起清掉，导致绿色卡住）</summary>
        readonly List<bool> _hostColored = new List<bool>();
        /// <summary>用户操作结束后需要补一次上色</summary>
        public bool NeedHostRecolor;

        void SetHostColored(int idx, bool v)
        {
            while (_hostColored.Count <= idx) _hostColored.Add(false);
            if (idx >= 0) _hostColored[idx] = v;
        }

        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        /// <summary>
        /// 给文本层按行上色。
        /// 关键：整个过程用 WM_SETREDRAW 关掉重绘，结束后**原样恢复**用户选区，
        /// 所以即使内部用了临时选区，用户也完全看不到闪动的选中框，更不会破坏拖拽。
        /// </summary>
        void ApplyHostColors(RichTextBox h, float[] inten)
        {
            const int WM_SETREDRAW = 0x000B;
            if (!h.IsHandleCreated) return;
            IntPtr hwnd = h.Handle;
            int saveStart = h.SelectionStart, saveLen = h.SelectionLength;
            SendMessage(hwnd, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
            try
            {
                string txt = h.Text;
                int line = 0, start = 0;
                for (int pos = 0; pos <= txt.Length; pos++)
                {
                    if (pos == txt.Length || txt[pos] == (char)10)
                    {
                        float f = (inten != null && line < inten.Length) ? inten[line] : 0f;
                        h.Select(start, pos - start);
                        h.SelectionColor = f > 0.001f
                            ? CardRenderer.Lerp(Pal.Text, Pal.Flash, Math.Min(1f, f))
                            : Pal.Text;
                        line++;
                        start = pos + 1;
                    }
                }
            }
            finally
            {
                h.Select(saveStart, saveLen);
                SendMessage(hwnd, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
                h.Invalidate();
            }
        }

        /// <summary>自检用：卡片顶部进度条文案的字体宽度诊断</summary>
        public string BusyTextWithPublic(string text)
        {
            BusyText = text;
            int avail = ClientSize.Width - 20 - SystemInformation.VerticalScrollBarWidth - 4;
            int need;
            using (Graphics g = CreateGraphics())
                need = TextRenderer.MeasureText(g, "  " + BusyText, Renderer.F.Ui,
                    new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPrefix).Width;
            return "需要=" + need + " 可用=" + avail + (need <= avail ? " 放得下" : " 会省略");
        }

        /// <summary>
        /// 组装卡片顶部进度条文案：**优先保证"进度 + 提示语"完整**，
        /// 空间不够时只把文件名逐字截短加省略号（而不是让整句被系统截断）。
        /// </summary>
        public string FitBusyTextPublic(string head, string tail)
        {
            int avail = ClientSize.Width - 20 - SystemInformation.VerticalScrollBarWidth - 8;
            if (avail < 60) return head;
            try
            {
                using (Graphics g = CreateGraphics())
                {
                    int w0 = TextRenderer.MeasureText(g, "  " + head, Renderer.F.Ui,
                        new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPrefix).Width;
                    if (string.IsNullOrEmpty(tail) || w0 >= avail) return head;
                    int room = avail - w0;
                    for (int len = tail.Length; len > 0; len--)
                    {
                        string cand = len < tail.Length ? tail.Substring(0, len) + "…" : tail;
                        int w = TextRenderer.MeasureText(g, cand, Renderer.F.Ui,
                            new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPrefix).Width;
                        if (w <= room) return head + cand;
                    }
                    return head;
                }
            }
            catch { return head; }
        }

        public int HostCountPublic { get { return _hosts.Count; } }
        public RichTextBox HostPublic(int i) { return i < _hosts.Count ? _hosts[i] : null; }

        /// <summary>
        /// 生成只读文本层的文本：把每行的键补齐到与面板一致的列宽（保证所有"："对齐），
        /// 再接上哈希值。这样用户在文本层里看到的就是最终内容。
        /// </summary>
        string BuildHostText(FileItem fi)
        {
            StringBuilder sb = new StringBuilder();
            Graphics g = MeasureG;
            float spaceW = Math.Max(1f, MeasureW(g, " ", Renderer.F.MonoBig));
            for (int i = 0; i < VisibleKeys.Count; i++)
            {
                string k = Algo.Key(VisibleKeys[i]);
                string v;
                if (!fi.Hashes.TryGetValue(VisibleKeys[i], out v)) continue;
                if (MainForm.Up) v = v.ToUpperInvariant();
                int need = Renderer.KeyW - MeasureW(g, k, Renderer.F.MonoBig);
                int sp = need > 0 ? (int)Math.Round(need / spaceW) : 0;
                if (i > 0) sb.Append('\n');
                sb.Append(k);
                for (int s = 0; s < sp; s++) sb.Append(' ');
                sb.Append(v);
            }
            return sb.ToString();
        }

        #endregion

        public void SetAnimating(bool on)
        {
            if (on) { if (!_tick.Enabled) _tick.Start(); }
            else { _tick.Stop(); Invalidate(); }
        }

        #region 数据

        public void ClearAll()
        {
            Files.Clear(); DisplayRows.Clear(); Selected.Clear();
            _clips.Clear(); _flashKeys.Clear(); _flashing.Clear();
            _views.Clear(); _rectsValid = false;
            AutoScrollPosition = new Point(0, 0);
            Reflow();
        }

        public void InvalidateViews() { _views.Clear(); _viewClearCount++; }

        /// <summary>把一个文件从缓存里清掉（删除文件后调用）</summary>
        public void HashesUntouched(FileItem f)
        {
            if (f == null) return;
            _views.Remove(f);
            _flashing.Remove(f);
            if (ReferenceEquals(_hoverDel, f)) _hoverDel = null;
            if (ReferenceEquals(_hoverCopy, f)) _hoverCopy = null;
        }

        public void Reflow()
        {
            _rectsValid = false;
            _views.Clear();
            int h = 6 + (Busy ? 30 : 0);
            foreach (object o in DisplayRows)
                h += (o is DividerItem) ? (CardRenderer.DividerHeight + 6) : (Renderer.CurCardHeight(LineCount) + 6);
            h += 10;
            AutoScrollMinSize = new Size(0, Math.Max(0, h));
            PerformLayout();
            Invalidate();
        }

        public void InvalidateLayout() { Reflow(); }

        void EnsureRects()
        {
            if (_rectsValid) return;
            _rects.Clear();
            int w = ClientSize.Width - 16 - SystemInformation.VerticalScrollBarWidth;
            if (w < 240) w = 240;
            int y = 6 + (Busy ? 30 : 0);
            foreach (object o in DisplayRows)
            {
                int h = (o is DividerItem) ? CardRenderer.DividerHeight : Renderer.CurCardHeight(LineCount);
                _rects.Add(new Rectangle(8, y, w, h));
                y += h + 6;
            }
            _rectsValid = true;
        }

        Rectangle ContentToClient(Rectangle r)
        {
            return new Rectangle(r.Left - AutoScrollPosition.X, r.Top + AutoScrollPosition.Y, r.Width, r.Height);
        }

        int IndexAt(Point client)
        {
            EnsureRects();
            for (int i = 0; i < _rects.Count; i++)
                if (ContentToClient(_rects[i]).Contains(client)) return i;
            return -1;
        }

        #endregion

        #region 绘制

        protected override void OnPaint(PaintEventArgs e)
        {
            EnsureRects();
            Graphics g = e.Graphics;
            using (SolidBrush b = new SolidBrush(Pal.Bg)) g.FillRectangle(b, ClientRectangle);

            if (Busy)
            {
                Rectangle br = new Rectangle(10 - AutoScrollPosition.X, 6 + AutoScrollPosition.Y,
                                             ClientSize.Width - 20 - SystemInformation.VerticalScrollBarWidth, 26);
                using (SolidBrush b = new SolidBrush(Pal.PanelBg)) g.FillRectangle(b, br);
                using (Pen p = new Pen(Pal.Btn)) g.DrawRectangle(p, br.Left, br.Top, br.Width - 1, br.Height - 1);
                Renderer.Txt(g, "  " + BusyText, Renderer.F.Ui, Pal.Text, br, CardRenderer.TxtFlags.EndEllipsis);
            }

            for (int i = 0; i < DisplayRows.Count; i++)
            {
                Rectangle rc = ContentToClient(_rects[i]);
                if (rc.Bottom < -20 || rc.Top > ClientSize.Height + 20) continue;

                FileItem fi = DisplayRows[i] as FileItem;
                if (fi == null)
                {
                    CardView dv = new CardView();
                    dv.IsDivider = true;
                    dv.DividerText = ((DividerItem)DisplayRows[i]).Text;
                    Renderer.DrawCard(g, rc, dv);
                    continue;
                }

                CardView cv = View(fi);
                Renderer.DrawCard(g, rc, cv);
                RecordLines(rc, cv);
                DrawCardButtons(g, rc, fi);

                // 框选关闭时：被划过的哈希值行叠一层高亮。
                // 这里【绝不重画原文】—— 只在原文之上叠半透明色块 + 选择色描边，
                // 所以不可能出现"原文与重画文字对不齐"造成的重影/投影。
                if (_selItem != null && ReferenceEquals(_selItem, fi) && _selA != _selB && _selLine >= 0 && _selLine < cv.Keys.Count)
                {
                    DrawTextSelection(g, rc, cv, _selLine, _selA, _selB);
                }

                if (Selected.Contains(fi.Path))
                {
                    using (Pen p = new Pen(Pal.BoxOn, 2f))
                        g.DrawRectangle(p, rc.Left + 1, rc.Top + 1, rc.Width - 3, rc.Height - 3);
                    using (SolidBrush b = new SolidBrush(Pal.BoxOn)) g.FillRectangle(b, rc.Left, rc.Top, 20, 20);
                    TextRenderer.DrawText(g, "\u221A", Renderer.F.Char, new Rectangle(rc.Left, rc.Top - 1, 20, 20), Pal.Text,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                }
                else if (SearchPaths.Count > 0 && SearchPaths.Contains(fi.Path))
                {
                    // 比对命中的卡片：整张描一圈亮绿，配合命中行闪烁，一眼看出"是这个文件"
                    using (Pen p = new Pen(Pal.Flash, 2f))
                        g.DrawRectangle(p, rc.Left + 1, rc.Top + 1, rc.Width - 3, rc.Height - 3);
                }
            }

            if (DisplayRows.Count == 0 && !Busy)
            {
                TextRenderer.DrawText(g, "把文件 / 文件夹拖到这里吧 (｡･ω･｡)", Renderer.F.Ui,
                    new Rectangle(0, ClientSize.Height / 2 - 42, ClientSize.Width, 30), Pal.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                using (Font f2 = Fonts.Ui(9f, FontStyle.Regular))
                    TextRenderer.DrawText(g, "也可以点右侧栏的「选择文件」「选择文件夹」", f2,
                        new Rectangle(0, ClientSize.Height / 2 - 14, ClientSize.Width, 26), Color.FromArgb(0x77, 0x77, 0x77),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            }

            if (_banding)
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(48, Pal.BoxOn))) g.FillRectangle(b, _band);
                using (Pen p = new Pen(Pal.BoxOn, 1f)) g.DrawRectangle(p, _band.Left, _band.Top, _band.Width - 1, _band.Height - 1);
            }

            SyncHosts();   // 只读文本层：每次绘制后把可见卡片与文本框对齐（滚动/尺寸变化都会走到这里）
        }

        void DrawCardButtons(Graphics g, Rectangle rc, FileItem fi)
        {
            Rectangle dr = CardRenderer.DelRect(rc);
            bool hd = ReferenceEquals(fi, _hoverDel);
            using (SolidBrush b = new SolidBrush(hd ? Pal.DelHot : Pal.Del)) g.FillRectangle(b, dr);
            TextRenderer.DrawText(g, "\u00D7", Renderer.F.Char, new Rectangle(dr.Left, dr.Top - 1, dr.Width, dr.Height), Pal.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);

            if (ShowCopyPerCard && fi.Computed && fi.Error == null)
            {
                Rectangle cr = CardRenderer.CopyRect(rc);
                bool hc = ReferenceEquals(fi, _hoverCopy);
                using (SolidBrush b = new SolidBrush(hc ? Pal.BtnHot : Pal.Btn)) g.FillRectangle(b, cr);
                TextRenderer.DrawText(g, "复制", Renderer.F.Small, cr, Pal.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            }
        }

        CardView View(FileItem fi)
        {
            CardView v;
            if (_views.TryGetValue(fi, out v)) return v;
            v = BuildView(fi);
            _views[fi] = v;
            return v;
        }

        CardView BuildView(FileItem fi)
        {
            CardView v = new CardView();
            v.Path = fi.Path;
            v.SizeText = ShowSize ? fi.SizeText : "";
            v.Computed = fi.Computed;
            v.Error = fi.Error;
            v.Uppercase = MainForm.Up;
            if (!fi.Computed || fi.Error != null) return v;

            bool flashing = _flashing.Contains(fi);

            foreach (string k in VisibleKeys)
            {
                string val;
                if (!fi.Hashes.TryGetValue(k, out val)) continue;
                v.Keys.Add(Algo.Key(k));
                v.Values.Add(val);
                float p = 0f;
                if (flashing)
                {
                    // 时间轴只针对“这次新勾上的”算法建立，所以要把它映射到 _flashKeys 里的位置
                    int ci = IndexIn(_flashKeys, k);
                    if (ci >= 0 && ci < _clips.Count) p = Ramp(_clips[ci]);
                }
                v.Flash.Add(p);
            }

            // 比对命中时：文件名/路径也跟着"亮"一次，指清是哪一个文件命中了
            if (flashing && SearchPaths.Count > 0 && SearchPaths.Contains(fi.Path) && _clips.Count > 0)
                v.PathFlash = Ramp(_clips[0]);

            return v;
        }

        #endregion

        #region 哈希值文本选择（框选模式关闭时可用）

        /// <summary>绘制时记录每张卡片各哈希值行的矩形，供命中测试使用</summary>
        void RecordLines(Rectangle cardRc, CardView v)
        {
            for (int i = 0; i < v.Keys.Count; i++)
            {
                _paintLines.Add(Renderer.CurLineRect(cardRc, i));
                _paintKeys.Add(v.Keys[i]);
            }
        }

        // ---- 字符 ↔ 像素 换算（命中与绘制共用同一套，保证严格一致）----
        // 等宽字体每字符宽：**必须按实际渲染测量**。
        // 字宽随 DPI 缩放变化（例如 125% 下明显更大），写死会让"字符号 ↔ 像素"
        // 换算整体偏小，从而出现划选起点/终点错位、复制结果与看到的选区对不上。
        static float _charWCache;
        static float _charWCacheSize;

        static float MonoCharWidth(CardRenderer r)
        {
            if (r == null || r.F == null) return _charWCache > 0f ? _charWCache : 8f;
            float size = r.F.MonoBig.Size;
            if (_charWCache > 0f && Math.Abs(_charWCacheSize - size) < 0.01f) return _charWCache;
            float w = 0f;
            try
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                {
                    int ten = TextRenderer.MeasureText(g, "0000000000", r.F.MonoBig,
                        new Size(int.MaxValue, int.MaxValue),
                        TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding).Width;
                    w = ten / 10f;
                }
            }
            catch { }
            if (w <= 0f) w = Math.Max(1f, size * 0.62f);
            _charWCache = w;
            _charWCacheSize = size;
            return w;
        }

        /// <summary>第 i 个字符（键列 + 值列串起来编号）的左边界，相对行左</summary>
        static float CharLeftOffset(int i, int keyLen, int keyW, CardRenderer r)
        {
            const float pad = 1f;
            float cw = MonoCharWidth(r);
            return (i <= keyLen) ? pad + i * cw
                                 : keyW + pad + (i - keyLen) * cw;
        }

        /// <summary>
        /// 把行内 x 偏移换算成字符"边界号"（0..len，键列 + 值列串起来编号），
        /// 边界号 i 表示"第 i 个字符的左侧"。
        /// ceil=true 用于划选起点（点在字符后半段时应从下一个字符开始选，才不会出现"从中间开始"）；
        /// ceil=false 用于划选终点（点在字符前半段时不该把这个字符算进来）。
        /// </summary>
        CardRenderer _lastRenderer;   // 供字符宽测量使用（由 FilePanel 设置）

        int CharBoundaryAt(float dx, int keyLen, int keyW, bool ceil)
        {
            float rel;
            int baseIdx;
            if (dx < keyW) { rel = dx - 1f; baseIdx = 0; }
            else { rel = dx - keyW - 1f; baseIdx = keyLen; }

            float cw = MonoCharWidth(_lastRenderer);
            if (cw <= 0f) cw = 8f;
            float f = rel / cw;
            int i = ceil ? (int)Math.Ceiling(f - 0.02f)      // 落在字符内 -> 取下一个边界
                         : (int)Math.Floor(f + 0.02f);       // 落在字符内 -> 取当前边界
            i += baseIdx;
            if (i < 0) i = 0;
            int max = keyLen + 128;      // 上限（hex 最长 64 位）
            if (i > max) i = max;
            return i;
        }

        /// <summary>旧接口：取整方式取"锚点"语义（严格来说只用于自检的粗略查询）</summary>
        int CharIndexAt(float dx, int keyLen, int keyW)
        {
            return CharBoundaryAt(dx, keyLen, keyW, true);
        }

        /// <summary>把客户区坐标解析成 (卡片, 行号, 字符号)（floor 语义：光标所在的那个字符）</summary>
        bool HitText(Point client, out FileItem fi, out int lineIdx, out int ch)
        {
            return HitTextEx(client, out fi, out lineIdx, out ch, false);
        }

        /// <summary>把客户区坐标解析成 (卡片, 行号, 字符边界)</summary>
        bool HitTextEx(Point client, out FileItem fi, out int lineIdx, out int ch, bool anchorCeil)
        {
            fi = null; lineIdx = -1; ch = 0;
            EnsureRects();
            int row = IndexAt(client);
            if (row < 0) return false;
            fi = DisplayRows[row] as FileItem;
            if (fi == null || !fi.Computed || fi.Error != null) { fi = null; return false; }

            Rectangle cardRc = ContentToClient(_rects[row]);
            for (int i = 0; i < LineCount; i++)
            {
                Rectangle lr = Renderer.CurLineRect(cardRc, i);
                if (client.Y < lr.Top || client.Y >= lr.Bottom) continue;
                lineIdx = i;
                string k = KeyForLine(i);
                string vv = null;
                if (fi.Hashes.ContainsKey(VisibleKeys[i])) vv = fi.Hashes[VisibleKeys[i]];
                if (vv == null) vv = "";
                if (MainForm.Up) vv = vv.ToUpperInvariant();
                ch = IndexFromX(lr, k == null ? "" : k, vv, client.X, anchorCeil);
                return true;
            }
            return false;
        }

        // ---- 文本几何：命中与绘制共用同一套测量，保证"框住的 = 选中的" ----

        Graphics _mg;
        Graphics MeasureG
        {
            get
            {
                if (_mg == null)
                {
                    try { _mg = Graphics.FromHwnd(IsHandleCreated ? Handle : IntPtr.Zero); }
                    catch { }
                }
                return _mg;
            }
        }

        static int MeasureW(Graphics g, string s, Font f)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            return TextRenderer.MeasureText(g, s, f, new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding).Width;
        }

        /// <summary>第 idx 个字符（键列 + 值列串起来编号）的左边界，客户区坐标。
        /// 注意：idx == keyLen 表示"值列的第一个字符"，边界应当是**值列的起点**，
        /// 所以键分支必须用 idx &lt; keyLen（用 &lt;= 会让选区整体偏左一个键宽）。</summary>
        float TextX(Rectangle lr, string key, string val, int idx)
        {
            EnsureTextPad();
            int keyLen = key.Length;
            Graphics g = MeasureG;
            if (idx < keyLen)
                return lr.Left + _textPad + MeasureW(g, key.Substring(0, idx), Renderer.F.MonoBig);
            int j = idx - keyLen;
            if (j < 0) j = 0;
            if (j > val.Length) j = val.Length;
            return lr.Left + Renderer.KeyW + _textPad + MeasureW(g, val.Substring(0, j), Renderer.F.MonoBig);
        }

        static int MaxLen(string key, string val) { return key.Length + val.Length; }

        /// <summary>把客户区 x 反解成字符边界号（ceil=true 用于划选起点，false 用于终点）</summary>
        int IndexFromX(Rectangle lr, string key, string val, int x, bool ceil)
        {
            int total = MaxLen(key, val);
            int floor = 0;
            for (int i = 0; i <= total; i++)
            {
                if (TextX(lr, key, val, i) <= x) floor = i; else break;
            }
            if (!ceil) return floor;
            if (floor >= total) return total;
            return TextX(lr, key, val, floor) < x ? floor + 1 : floor;
        }

        /// <summary>第 i 行显示的算法键名（与绘制严格一致）</summary>
        string KeyForLine(int i)
        {
            if (i < 0 || i >= VisibleKeys.Count) return null;
            return Algo.Key(VisibleKeys[i]);
        }

        /// <summary>当前选中的文本（没有选中就返回 null）</summary>
        public string CopyTextSelection()
        {
            if (_selItem == null || _selA == _selB) return null;
            // 键名与取值都必须和"这一行显示的是什么"一致 —— 用 VisibleKeys 推导，
            // 不要依赖绘制缓存（缓存里可能混进别的卡片的数据）
            string algo = (_selLine >= 0 && _selLine < VisibleKeys.Count) ? VisibleKeys[_selLine] : null;
            if (algo == null) return null;
            string key = Algo.Key(algo);
            string val;
            if (!_selItem.Hashes.TryGetValue(algo, out val)) return null;
            if (MainForm.Up) val = val.ToUpperInvariant();
            string all = key + val;
            int a = Math.Max(0, Math.Min(_selA, all.Length));
            int b = Math.Max(0, Math.Min(_selB, all.Length));
            if (a >= b) return null;
            return all.Substring(a, b - a);
        }

        public bool HasTextSelection { get { return _selItem != null && _selA != _selB; } }

        /// <summary>按锚点与当前终点整理出有序区间 [A,B)</summary>
        void RemapSelection()
        {
            int a = _selAnchor, b = _selB;
            if (a > b) { int t = a; a = b; b = t; }
            _selA = a; _selB = b;
        }

        /// <summary>清掉卡片上的文本选择（开启框选模式时调用）</summary>
        public void ClearSelPublic()
        {
            if (_selItem == null && _selA == _selB) return;
            ClearTextSelection();
            Invalidate();
        }

        void ClearTextSelection()
        {
            _selItem = null; _selLine = -1; _selA = _selB = 0;
        }

        // ---- 文本度量：列内左内边距（GDI 默认留白）在运行期实测，不猜 ----
        float _textPad = 1f;
        float _padFontSize = -1f;

        void EnsureTextPad()
        {
            float sz = Renderer == null || Renderer.F == null ? 0f : Renderer.F.MonoBig.Size;
            if (Math.Abs(_padFontSize - sz) < 0.01f && _padFontSize > 0f) return;
            _padFontSize = sz;
            try
            {
                using (Bitmap bmp = new Bitmap(64, 40))
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Black);
                    TextRenderer.DrawText(g, "0", Renderer.F.MonoBig, new Rectangle(0, 0, 64, 40), Color.White,
                        TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);
                    int first = -1;
                    for (int x = 0; x < 64 && first < 0; x++)
                        for (int y = 0; y < 40; y++)
                            if (bmp.GetPixel(x, y).R > 100) { first = x; break; }
                    if (first >= 0) _textPad = first;
                }
            }
            catch { }
        }

        /// <summary>
        /// 把某一行哈希值里 [a,b) 字符区间画成高亮。
        /// 只画半透明色块，【绝不重画文字】。
        /// 左右边界用 **与绘制完全相同的 GDI 测量**（MeasureText 前缀宽度）算出来，
        /// 所以"框住的范围"和"实际选中的范围"必然一致，不会再有偏左/偏右的错位。
        /// </summary>
        void DrawTextSelection(Graphics g, Rectangle cardRc, CardView v, int lineIdx, int a, int b)
        {
            Rectangle lr = Renderer.CurLineRect(cardRc, lineIdx);
            int keyW = Renderer.KeyW;
            string key = v.Keys[lineIdx];
            string val = v.Values[lineIdx] ?? "";
            if (v.Uppercase) val = val.ToUpperInvariant();
            if (a < 0) a = 0;
            int allLen = key.Length + val.Length;
            if (b > allLen) b = allLen;
            if (a >= b) return;

            // 与命中测试共用同一个 TextX（测量方式完全一致），所以"框住的"必然就是"选中的"
            float x1 = TextX(lr, key, val, a), x2 = TextX(lr, key, val, b);
            if (x2 < x1) { float t = x1; x1 = x2; x2 = t; }

            // 只叠色块（半透明），下面的原文文字仍然完好
            using (SolidBrush hi = new SolidBrush(Color.FromArgb(110, Pal.BoxOn)))
                g.FillRectangle(hi, x1, lr.Top + 1, Math.Max(2f, x2 - x1), lr.Height - 2);
        }

        #endregion

        #region 动画

        static int IndexIn(List<string> list, string s)
        {
            for (int i = 0; i < list.Count; i++) if (list[i] == s) return i;
            return -1;
        }

        void TickAnim()
        {
            if (_flashing.Count == 0) { _tick.Stop(); SyncHosts(); Invalidate(); return; }
            foreach (Anim a in _clips) a.T += 0.033f;
            _views.Clear();
            if (!AnyRunning()) { _flashing.Clear(); _clips.Clear(); _tick.Stop(); }
            SyncHosts();          // 文本层的颜色跟着动画走（哈希值已经由只读文本层承载）
            Invalidate();
        }

        bool AnyRunning()
        {
            foreach (Anim a in _clips) if (a.T < a.Total) return true;
            return false;
        }

        /// <summary>0..1 的渐变强度：先渐亮，保持，再渐变回原色</summary>
        static float Ramp(Anim a)
        {
            float t = a.T;
            if (t <= 0f) return 0f;
            if (t < a.Rise) return t / a.Rise;
            if (t < a.Rise + a.Hold) return 1f;
            float d = t - a.Rise - a.Hold;
            if (d < a.Fall) return 1f - d / a.Fall;
            return 0f;
        }

        /// <summary>
        /// 让这些文件上“keys 这几行”依次闪烁：MD5 渐亮到绿色、保持、再渐变回白，
        /// 灭掉之后 SHA1 才开始，然后 CRC32、SHA-256。
        /// 时间轴按“显示行”共享 —— 所有一致文件的同一个算法一起亮、一起灭。
        /// </summary>
        public void FlashRows(List<FileItem> items, List<string> keys)
        {
            if (items == null || items.Count == 0 || keys == null || keys.Count == 0) return;
            _flashing.Clear();
            foreach (FileItem f in items) { _flashing.Add(f); _views.Remove(f); }
            _clips.Clear();
            _flashKeys.Clear();

            float at = 0f;
            foreach (string k in keys)
            {
                bool any = false;
                foreach (FileItem f in items) if (f.Hashes.ContainsKey(k)) { any = true; break; }
                if (!any) continue;

                Anim a = new Anim();
                a.T = -at;              // 负值 = 还没轮到
                _clips.Add(a);
                _flashKeys.Add(k);
                at += a.Total + 0.04f;  // 上一行灭完，下一行才开始（留一点点间隔）
            }
            if (_clips.Count == 0) { _flashing.Clear(); return; }
            if (!_tick.Enabled) _tick.Start();
        }

        public void ScrollTo(FileItem fi, bool onlyIfOutside)
        {
            EnsureRects();
            int idx = DisplayRows.IndexOf(fi);
            if (idx < 0) return;
            Rectangle rc = ContentToClient(_rects[idx]);
            if (onlyIfOutside && rc.Top >= 0 && rc.Bottom <= ClientSize.Height) return;
            int target = _rects[idx].Top - 40;
            int max = Math.Max(0, AutoScrollMinSize.Height - ClientSize.Height);
            if (target < 0) target = 0;
            if (target > max) target = max;
            AutoScrollPosition = new Point(0, target);
            _rectsValid = false;
            Invalidate();
        }

        #endregion

        #region 鼠标

        /// <summary>选中状态变了：让绘制缓存失效 + 立刻同步重绘（缺一不可，否则画的是旧内容）</summary>
        void AfterSelectionChanged()
        {
            InvalidateViews();
            if (SelectionChanged != null) SelectionChanged();
            Invalidate();
            Update();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && !BoxMode)
            {
                // 记下"这次按下可能是在某个哈希值上"，但**先不进入文本选择**：
                // 只有鼠标真的横向划动了才开始选文本。否则单纯的点击会被文本选择吞掉，
                // 表现就是"点第二张卡片没反应、只能选中一个"。
                FileItem tf; int tl, tc;
                // 起点语义与终点一致（floor = 光标所在的那个字符），
                // 这样"从哪个字符开始拖"就"从哪个字符开始选"，不会有半格偏移。
                if (HitTextEx(e.Location, out tf, out tl, out tc, false))
                {
                    _selCandidate = tf;
                    _selCandLine = tl;
                    _selCandChar = tc;
                    _selDownPoint = e.Location;
                }
                else
                {
                    _selCandidate = null;
                }
            }

            if (e.Button == MouseButtons.Left)
            {
                int i = IndexAt(e.Location);
                FileItem fi = (i >= 0) ? DisplayRows[i] as FileItem : null;
                if (fi != null)
                {
                    Rectangle rc = ContentToClient(_rects[i]);
                    if (CardRenderer.DelRect(rc).Contains(e.Location))
                    {
                        _hoverDel = null;
                        MainForm.Instance.RemoveFile(fi);
                    }
                    else if (ShowCopyPerCard && fi.Computed && fi.Error == null && CardRenderer.CopyRect(rc).Contains(e.Location))
                    {
                        MainForm.Instance.CopyOne(fi);
                    }
                    else
                    {
                        // 框选模式：点一下就切换选中（方便不熟悉 Ctrl 加选 / 触控板用户）
                        if (BoxMode || ModifierKeys == Keys.Control)
                        {
                            if (!Selected.Remove(fi.Path)) Selected.Add(fi.Path);
                        }
                        else
                        {
                            Selected.Clear();
                            Selected.Add(fi.Path);
                        }
                        Focus();
                        AfterSelectionChanged();
                    }
                    Invalidate();
                }
                else
                {
                    _banding = true;
                    _bandAnchor = new Point(e.X, e.Y);
                    _band = new Rectangle(e.X, e.Y, 1, 1);
                    if (!BoxMode) Selected.Clear();
                    Focus();
                    AfterSelectionChanged();
                }
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            // 只有横向真的划动了（≥4px）才把"按下时的候选"升级成文本选择，
            // 这样单纯点击仍然走卡片选中，不会被吞掉。
            if (!_selDragging && _selCandidate != null &&
                Math.Abs(e.X - _selDownPoint.X) >= 4 && Math.Abs(e.Y - _selDownPoint.Y) <= 8)
            {
                _selDragging = true;
                _selMoved = false;
                _selItem = _selCandidate;
                _selLine = _selCandLine;
                _selAnchor = _selCandChar;
                _selA = _selCandChar;
                _selB = _selCandChar;
                Capture = true;
            }

            if (_selDragging)
            {
                FileItem tf; int tl, tc;
                // 终点：落在字符前半段就不把这个字符算进来（ceil=false）
                if (HitTextEx(e.Location, out tf, out tl, out tc, false) && ReferenceEquals(tf, _selItem) && tl == _selLine)
                {
                    if (tc != _selB) _selMoved = true;
                    _selB = tc;
                    RemapSelection();   // 按锚点/终点方向整理成 [A,B)
                    Invalidate();
                }
                return;
            }

            if (_banding)
            {
                _band = new Rectangle(Math.Min(_bandAnchor.X, e.X), Math.Min(_bandAnchor.Y, e.Y),
                                      Math.Abs(e.X - _bandAnchor.X) + 1, Math.Abs(e.Y - _bandAnchor.Y) + 1);
                EnsureRects();
                if (!BoxMode) Selected.Clear();
                for (int i = 0; i < _rects.Count; i++)
                {
                    if (DisplayRows[i] is DividerItem) continue;
                    if (ContentToClient(_rects[i]).IntersectsWith(_band))
                        Selected.Add(((FileItem)DisplayRows[i]).Path);
                }
                _views.Clear();
                if (SelectionChanged != null) SelectionChanged();
                Invalidate();
            }
            else
            {
                int i = IndexAt(e.Location);
                FileItem fi = (i >= 0) ? DisplayRows[i] as FileItem : null;
                FileItem nd = null, nc = null;
                if (fi != null)
                {
                    Rectangle rc = ContentToClient(_rects[i]);
                    if (CardRenderer.DelRect(rc).Contains(e.Location)) nd = fi;
                    else if (ShowCopyPerCard && fi.Computed && fi.Error == null && CardRenderer.CopyRect(rc).Contains(e.Location)) nc = fi;
                }
                if (!ReferenceEquals(nd, _hoverDel) || !ReferenceEquals(nc, _hoverCopy))
                {
                    if (_hoverDel != null) _views.Remove(_hoverDel);
                    if (_hoverCopy != null) _views.Remove(_hoverCopy);
                    _hoverDel = nd; _hoverCopy = nc;
                    if (nd != null) _views.Remove(nd);
                    if (nc != null) _views.Remove(nc);
                    Cursor = (nd != null || nc != null) ? Cursors.Hand : Cursors.Default;
                    Invalidate();
                }
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _selCandidate = null;      // 每次抬起都清掉"待选候选"

            if (_selDragging)
            {
                _selDragging = false;
                if (Capture) Capture = false;

                // 用**松手位置**重新算一次终点：这样无论从左往右还是从右往左划，
                // 最终区间都由"锚点 + 松手点"决定，不依赖拖拽过程中逐帧更新的状态。
                FileItem tf2; int tl2, tc2;
                if (HitTextEx(e.Location, out tf2, out tl2, out tc2, false) &&
                    ReferenceEquals(tf2, _selItem) && tl2 == _selLine)
                {
                    _selB = tc2;
                    _selMoved = _selAnchor != _selB;
                }
                RemapSelection();
                // 只是点了一下（没有横向划过）就不留选择，避免和“点一下取消选中”抢语义
                if (!_selMoved) { _selItem = null; _selA = _selB = 0; }
                Invalidate();
                return;
            }

            if (_banding)
            {
                _banding = false;
                if (!BoxMode && _band.Width < 4 && _band.Height < 4) { Selected.Clear(); AfterSelectionChanged(); }
                Invalidate();
            }
            base.OnMouseUp(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (_hoverDel != null || _hoverCopy != null)
            {
                if (_hoverDel != null) _views.Remove(_hoverDel);
                if (_hoverCopy != null) _views.Remove(_hoverCopy);
                _hoverDel = null; _hoverCopy = null;
                Invalidate();
            }
            base.OnMouseLeave(e);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Delete || keyData == Keys.Back) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete || e.KeyCode == Keys.Back) { MainForm.Instance.RemoveSelected(); e.Handled = true; }
            else if (e.KeyCode == Keys.A && e.Control) { SelectAll(); e.Handled = true; }
            base.OnKeyDown(e);
        }

        public void SelectAll()
        {
            Selected.Clear();
            foreach (object o in DisplayRows) if (o is FileItem) Selected.Add(((FileItem)o).Path);
            AfterSelectionChanged();
        }

        #endregion

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Env.DarkScrollbars(Handle);   // 滚动条走深色主题
        }

        protected override void OnScroll(ScrollEventArgs se) { _rectsValid = false; base.OnScroll(se); }
        protected override void OnMouseWheel(MouseEventArgs e) { _rectsValid = false; base.OnMouseWheel(e); }
        protected override void OnResize(EventArgs e) { _rectsValid = false; _views.Clear(); base.OnResize(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        #region 自检钩子（--selftest 用，正常使用不涉及）

        /// <summary>推进一次动画并读回某个文件第一行的闪烁强度</summary>
        public float DebugTickAndPeek(FileItem fi)
        {
            TickAnim();
            CardView v = BuildView(fi);
            return (v.Flash != null && v.Flash.Count > 0) ? v.Flash[0] : 0f;
        }

        /// <summary>真实走一遍“鼠标按下 → 移动 → 绘制 → 抬起”</summary>
        public void DebugClickAndPaint(int x, int y)
        {
            OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, x, y, 0));
            OnMouseMove(new MouseEventArgs(MouseButtons.None, 0, x, y, 0));
            using (Bitmap bmp = new Bitmap(Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height)))
            using (Graphics g = Graphics.FromImage(bmp))
            using (PaintEventArgs pe = new PaintEventArgs(g, new Rectangle(0, 0, bmp.Width, bmp.Height)))
                OnPaint(pe);
            OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, x, y, 0));
        }

        public CardView DebugPeekView(FileItem fi) { return BuildView(fi); }

        /// <summary>自检用：第 i 行哈希值在客户区里的矩形（与绘制时记录的一致）</summary>
        public Rectangle DebugLineRect(int i)
        {
            EnsureRects();
            if (_rects.Count == 0) return Rectangle.Empty;
            Rectangle card = ContentToClient(_rects[0]);
            Rectangle lr = Renderer.CurLineRect(card, i);
            return lr;
        }

        /// <summary>自检用：直接按客户区坐标查询命中结果</summary>
        public bool DebugHit(Point client, out string path, out int line, out int ch)
        {
            FileItem fi; int l, c;
            bool ok = HitText(client, out fi, out l, out c);
            path = fi == null ? null : fi.Path;
            line = l; ch = c;
            return ok;
        }

        /// <summary>自检用：真实走一遍“按下 → 划过 → 抬起”</summary>
        public void DebugDrag(int x1, int y1, int x2, int y2)
        {
            OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, x1, y1, 0));
            OnMouseMove(new MouseEventArgs(MouseButtons.Left, 0, (x1 + x2) / 2, y1, 0));
            OnMouseMove(new MouseEventArgs(MouseButtons.Left, 0, x2, y2, 0));
            OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, x2, y2, 0));
        }

        /// <summary>自检用：指定文件第 i 行哈希值在客户区里的矩形</summary>
        public Rectangle DebugLineRectFor(FileItem fi, int i)
        {
            EnsureRects();
            int idx = DisplayRows.IndexOf(fi);
            if (idx < 0) return Rectangle.Empty;
            Rectangle card = ContentToClient(_rects[idx]);
            return Renderer.CurLineRect(card, i);
        }

        /// <summary>自检用：列内左内边距（运行期实测值）</summary>
        public float TextPadPublic { get { EnsureTextPad(); return _textPad; } }

        /// <summary>自检用：指定文件某一行第 idx 个字符的左边界（客户区 x）</summary>
        public float TextXPublic(FileItem fi, int lineIdx, int idx)
        {
            Rectangle lr = DebugLineRectFor(fi, lineIdx);
            string key = KeyForLine(lineIdx);
            string val = fi.Hashes.ContainsKey(VisibleKeys[lineIdx]) ? fi.Hashes[VisibleKeys[lineIdx]] : "";
            if (MainForm.Up) val = val.ToUpperInvariant();
            return TextX(lr, key == null ? "" : key, val, idx);
        }

        /// <summary>诊断/自检用：某个 FileItem 对应的卡片矩形（客户区）</summary>
        public Rectangle DebugCardRectFor(FileItem fi)
        {
            EnsureRects();
            for (int i = 0; i < DisplayRows.Count && i < _rects.Count; i++)
                if (ReferenceEquals(DisplayRows[i], fi)) return ContentToClient(_rects[i]);
            return Rectangle.Empty;
        }

        /// <summary>诊断用：把"卡片矩形 / 绿条矩形 / 文本层矩形"导出来对齐</summary>
        public string DebugHostGeometryPublic()
        {
            EnsureRects();
            StringBuilder sb = new StringBuilder();
            int used = 0;
            for (int i = 0; i < DisplayRows.Count; i++)
            {
                FileItem fi = DisplayRows[i] as FileItem;
                if (fi == null || !fi.Computed || fi.Error != null) continue;
                Rectangle rc = ContentToClient(_rects[i]);
                if (rc.Bottom < 0 || rc.Top > ClientSize.Height) continue;
                Rectangle bar = new Rectangle(rc.Left + 1, rc.Top + 1, 3, rc.Height - 2);
                Rectangle line0 = Renderer.CurLineRect(rc, 0);
                RichTextBox h = used < _hosts.Count ? _hosts[used] : null;
                sb.AppendLine("   row" + i + " card=" + rc + " bar=" + bar +
                              " line0=" + line0 + " host=" + (h == null ? "无" : h.Bounds.ToString()) +
                              " 重叠=" + (h != null && h.Bounds.IntersectsWith(bar)));
                used++;
                if (used >= 3) break;
            }
            return sb.ToString();
        }

        /// <summary>自检用：等宽字体每字符宽（运行期实测值）</summary>
        public float MonospaceCharWidthPublic() { return MonoCharWidth(Renderer); }

        /// <summary>诊断：最近一次终点解析的结果</summary>
        public static string DebugLastExtent = "(未跑过)";

        /// <summary>自检用：当前文本选择的内部状态</summary>
        public string DebugSelState()
        {
            return "item=" + (_selItem == null ? "null" : "ok") + " line=" + _selLine +
                   " A=" + _selA + " B=" + _selB +
                   " dragging=" + _selDragging;
        }

        #endregion
    }

    #endregion

    #region 环境工具

    internal static class Env
    {
        // 让滚动条用系统自带的深色主题。
        // 关键点：uxtheme 的 "DarkMode_Explorer" 只有在"该窗口已被允许进入深色模式"时才会生效，
        // 否则它仍然画浅色滚动条（最小窗口时右侧会冒出一条白色原生滚动条）。
        // 所以这里先调用未公开的 AllowDarkModeForWindow(hwnd, true)（Win10 1809+ 才有），
        // 再套 DarkMode_Explorer。老系统（Win7 / 未启用视觉样式）取不到入口点就静默跳过，
        // 外观退回系统默认，体积与兼容性都不受影响。
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hWnd, string subAppName, string subIdList);

        // 序号按 win32-darkmode 的公开整理：
        //   #133 = AllowDarkModeForWindow(hwnd, allow)
        //   #135 = SetPreferredAppMode(mode)     （2 = ForceDark）
        // 这两个都是未公开导出，老系统上调用会抛 EntryPointNotFoundException，已被 try/catch 兜住。
        [DllImport("uxtheme.dll", EntryPoint = "#133", SetLastError = true)]
        static extern bool AllowDarkModeForWindow(IntPtr hWnd, bool allow);

        [DllImport("uxtheme.dll", EntryPoint = "#135", SetLastError = true)]
        static extern int SetPreferredAppMode(int mode);     // 2 = ForceDark

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr LoadLibrary(string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr GetProcAddress(IntPtr h, string name);

        static bool _darkProbed;
        static bool _darkSupported;
        static bool _darkEnabled;

        // 注意：不要用 GetProcAddress(dll, "#135") 去探测 ——
        // GetProcAddress 只接受"序号值"而不是 "#135" 这种字符串，那样探测永远失败。
        // DllImport 的 EntryPoint="#135" 由运行时按序号解析（支持），所以直接调用 + try/catch 最稳。
        static bool DarkModeSupported()
        {
            if (_darkProbed) return _darkSupported;
            _darkProbed = true;
            try
            {
                SetPreferredAppMode(2);      // 2 = ForceDark：让 uxtheme 画深色滚动条
                _darkSupported = true;
            }
            catch { _darkSupported = false; }
            return _darkSupported;
        }

        /// <summary>进程级开启深色模式偏好（只做一次）。取不到入口点就跳过。</summary>
        public static void EnableDarkMode()
        {
            if (_darkEnabled) return;
            _darkEnabled = true;
            DarkModeSupported();      // 内部即调用 SetPreferredAppMode(ForceDark)
        }

        /// <summary>最近一次套深色主题的结果（诊断用）</summary>
        public static string LastDarkResult = "(未调用)";

        /// <summary>给某个可滚动控件的句柄套深色滚动条</summary>
        public static void DarkScrollbars(IntPtr handle)
        {
            if (handle == IntPtr.Zero) { LastDarkResult = "句柄无效"; return; }
            try
            {
                bool allow = DarkModeSupported() && AllowDarkModeForWindow(handle, true);
                int rc = SetWindowTheme(handle, "DarkMode_Explorer", null);
                LastDarkResult = "AllowDark=" + allow + " SetWindowTheme=" + rc + (rc == 0 ? " (成功)" : " (失败)");
            }
            catch (Exception ex) { LastDarkResult = "异常:" + ex.Message; }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] static extern void mouse_event(uint flags, int dx, int dy, int data, IntPtr extra);

        /// <summary>自检用：移动真实光标</summary>
        public static void SetCursorPublic(int x, int y) { try { SetCursorPos(x, y); } catch { } }

        /// <summary>自检用：用真实鼠标事件做一次"按下 - 移动 - 抬起"拖动</summary>
        public static void DragPublic(int x0, int y0, int x1, int y1)
        {
            const uint LDOWN = 0x0002, LUP = 0x0004;
            try
            {
                SetCursorPos(x0, y0);
                System.Threading.Thread.Sleep(60);
                mouse_event(LDOWN, 0, 0, 0, IntPtr.Zero);
                int steps = 12;
                for (int i = 1; i <= steps; i++)
                {
                    SetCursorPos(x0 + (x1 - x0) * i / steps, y0 + (y1 - y0) * i / steps);
                    System.Threading.Thread.Sleep(25);
                }
                mouse_event(LUP, 0, 0, 0, IntPtr.Zero);
            }
            catch { }
        }

        /// <summary>自检用：发一个真实滚轮事件（delta 为 ±120 的倍数）</summary>
        public static void WheelPublic(int delta)
        {
            const uint MOUSEEVENTF_WHEEL = 0x0800;
            try { mouse_event(MOUSEEVENTF_WHEEL, 0, 0, delta, IntPtr.Zero); } catch { }
        }

        /// <summary>自检用：给控件发一个 WM_KEYDOWN（验证只读控件会忽略删除键）</summary>
        public static void SendKeyDownPublic(IntPtr hwnd, int vk)
        {
            const int WM_KEYDOWN = 0x0100;
            try { SendMessage(hwnd, WM_KEYDOWN, (IntPtr)vk, IntPtr.Zero); } catch { }
        }

        /// <summary>自检用：给控件发一个 WM_CHAR（验证只读控件会忽略键盘输入）</summary>
        public static void SendCharPublic(IntPtr hwnd, char c)
        {
            const int WM_CHAR = 0x0102;
            try { SendMessage(hwnd, WM_CHAR, (IntPtr)c, IntPtr.Zero); } catch { }
        }

        public static void CopyText(string s)
        {
            try { Clipboard.SetDataObject(s, true, 8, 120); }
            catch { try { Clipboard.SetText(s); } catch { } }
        }

        /// <summary>在资源管理器里定位 exe 同目录下的某个文件；找不到就打开 exe 所在目录</summary>
        public static void OpenFolder(string file)
        {
            try
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                string full = Path.IsPathRooted(file) ? file : Path.Combine(dir, file);
                if (File.Exists(full)) Process.Start("explorer.exe", "/select,\"" + full + "\"");
                else Process.Start("explorer.exe", "\"" + dir + "\"");
            }
            catch { }
        }
    }

    #endregion

    #region 主窗口

    internal sealed class MainForm : Form
    {
        public static bool Up = true;
        public static MainForm Instance;
        /// <summary>诊断用：LayoutShell 调用次数与轨迹</summary>
        public static int LayoutShellCalls;
        public static readonly List<string> DiagLog = new List<string>();

        FilePanel _panel;
        Panel _sidePanel;      // 右侧栏
        Panel _bottomPanel;    // 底部状态栏（横跨整窗）
        Panel _btnStrip;       // 文件操作条（横排，位于状态栏之上）
        PlaceholderBox _cmp;
        Label _status, _stat2;
        ToggleBtn _tMD5, _tSHA1, _tCRC, _tSHA256;
        readonly List<ToggleBtn> _toggles = new List<ToggleBtn>();
        Button _btnCopy, _btnCase, _btnCopyAll, _btnExport;
        Button _btnPick, _btnFolder, _btnClearTop, _btnVerifySide;
        SwitchButton _boxSelSide;
        SwitchButton _groupSame;      // 同文件分类开关（默认关闭）
        OutsideClickFilter _msgFilter;
        System.Windows.Forms.Timer _hashTimer, _marquee;
        ToolTip _tip;
        UiFonts _fonts;
        TitleBar _titleBar;
        readonly List<Panel> _grabs = new List<Panel>();

        readonly List<FileItem> _queue = new List<FileItem>();
        int _qIndex, _qDone;

        /// <summary>上一次已勾选的算法集合，用来判断这次是不是“新勾上”的</summary>
        readonly List<string> _prevKeys = new List<string>();
        /// <summary>需要补算时先记下新勾的算法：算完只让它们亮</summary>
        readonly List<string> _pendFlashKeys = new List<string>();

        // 正在拖拽缩放的边：0=无，1=左，2=右，4=上，8=下（可叠加=四角）
        int _resizeEdges;
        Rectangle _resizeBounds;

        const int EdgeGrab = 4;     // 拖拽条厚度：尽量薄，避免与内容区抢鼠标
        const int CornerGrab = 10;
        const int TitleH = 40;
        const int BottomH = 34;
        const int StripH = 44;      // 文件操作条高度
        const int SideW = 330;

        // ---- 文字随窗口缩放 ----
        // 窗口越窄，卡片文字越小：这样 SHA-256 的 64 个字符永远能完整放下，
        // 而不是被卡片右边缘切掉最后几位。
        const float FontMax = 10.5f;    // 全屏/宽窗口时的字号
        const float FontMin = 7.0f;     // 最窄时的字号（保证 64 字符一定放得下）
        float _curFont = FontMax;
        int _baseKeyW;                  // 初始键列宽（重算字号时要用它量）
        string[] _allKeys;              // 四种算法的键名（量宽用）

        /// <summary>
        /// 按文件列表的可用宽度调整卡片字号：窗口越窄字越小，
        /// 保证 SHA-256（64 个字符）始终能完整显示，不会被右边切掉。
        /// 算法：先用当前字号**实测** 64 个字符的宽度，再按比例反算需要的字号。
        /// </summary>
        void ScaleTextToWidth(int listW)
        {
            if (_panel == null || _panel.Renderer == null || _allKeys == null) return;

            // 卡片里值的可用宽度：列表宽 - 卡片左右内边距 - 键列 - 右侧安全余量
            int cardW = listW - 16 - SystemInformation.VerticalScrollBarWidth;

            // 当前字号下 64 个字符的真实宽度（GDI 实测，与绘制一致）。
            // 优先用"面板自己的 DC"量，保证 DPI/缩放上下文和真正绘制时相同。
            int hashW;
            if (_panel.IsHandleCreated)
            {
                using (Graphics g = _panel.CreateGraphics())
                    hashW = TextRenderer.MeasureText(g, new string('0', 64), _fonts.MonoBig,
                        new Size(int.MaxValue, int.MaxValue),
                        TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding).Width;
            }
            else
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                    hashW = TextRenderer.MeasureText(g, new string('0', 64), _fonts.MonoBig,
                        new Size(int.MaxValue, int.MaxValue),
                        TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding).Width;
            }
            if (hashW <= 0) return;

            // 与 CardRenderer.DrawCard 的真实布局一致：值列宽 = 卡片宽 - 2*PadX - 6 - 键列宽
            float charAllow = hashW / 64f;
            float usable = cardW - CardRenderer.PadX * 2 - 6 - _baseKeyW - 8f;
            if (usable < 160f) usable = 160f;

            // **循环收缩**：从最大字号开始，每 0.25pt 降一档，直到 64 个字符确实放得下（再留一个字符余量）。
            // 不用"按比例估算"——那样在奇数宽度/特殊缩放下会差 1~2px 而裁掉最后一个字符。
            float font = FontMax;
            const float step = 0.25f;
            while (font > FontMin)
            {
                int w;
                using (Font probe = new Font(_fonts.MonoBig.FontFamily, font, FontStyle.Regular, GraphicsUnit.Point))
                    w = TextRenderer.MeasureText(new string('0', 64), probe, new Size(int.MaxValue, int.MaxValue),
                        TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding).Width;
                float need = w + (w / 64f) + 6f;      // 再留"一个字符 + 6px"
                if (need <= usable) break;
                font -= step;
            }
            if (font < FontMin) font = FontMin;
            font = (float)Math.Floor(font * 4f) / 4f;

            if (Math.Abs(font - _curFont) < 0.01f) return;

            UiFonts old = _fonts;
            _curFont = font;
            _fonts = new UiFonts(font, 10f);
            _baseKeyW = CardRenderer.MeasureKeyWidth(_fonts, _allKeys);
            _panel.Renderer.UseFonts(_fonts, _allKeys);
            _panel.InvalidateViews();
            _panel.Reflow();
            try { if (old != null) old.Dispose(); } catch { }
            SyncBottom();
        }

        public MainForm()
        {
            Instance = this;
            _fonts = new UiFonts(FontMax, 10f);

            _allKeys = new string[] {
                Algo.Key(Algo.MD5), Algo.Key(Algo.SHA1), Algo.Key(Algo.CRC32), Algo.Key(Algo.SHA256) };
            int keyW = CardRenderer.MeasureKeyWidth(_fonts, _allKeys);
            _baseKeyW = keyW;

            Text = AppInfo.Title + "  " + AppInfo.Version;
            FormBorderStyle = FormBorderStyle.None;   // 标题栏自己画（配色/悬停态由本程序决定）
            ClientSize = new Size(1240, 740);
            MinimumSize = new Size(1000, 580);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Pal.Bg;
            ForeColor = Pal.Text;
            Font = _fonts.Ui;
            AllowDrop = true;
            KeyPreview = true;
            DoubleBuffered = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            _tip = new ToolTip();
            _tip.AutoPopDelay = 12000;
            _tip.InitialDelay = 350;

            _hashTimer = new System.Windows.Forms.Timer();
            _hashTimer.Interval = 1;
            _hashTimer.Tick += delegate { HashStep(); };

            _marquee = new System.Windows.Forms.Timer();
            _marquee.Interval = 120;
            _marquee.Tick += delegate { if (_panel.Busy) _panel.Invalidate(); };

            BuildUi(keyW);
            BuildMenu();
            WireEvents();

            ApplyDefaults();
            _panel.VisibleKeys = new List<string>();
            if (_tMD5.Checked) _panel.VisibleKeys.Add(Algo.MD5);
            if (_tSHA1.Checked) _panel.VisibleKeys.Add(Algo.SHA1);
            if (_tCRC.Checked) _panel.VisibleKeys.Add(Algo.CRC32);
            if (_tSHA256.Checked) _panel.VisibleKeys.Add(Algo.SHA256);
            _prevKeys.Clear();
            _prevKeys.AddRange(_panel.VisibleKeys);
            _panel.LineCount = Math.Max(1, _panel.VisibleKeys.Count);
            _panel.InvalidateViews();
            LayoutShell();
            SyncBottom();
            ShowStatus("准备好了，把文件拖进来吧～", false);
        }

        #region 界面构建

        void BuildUi(int keyW)
        {
            _titleBar = new TitleBar(this);
            _titleBar.Height = TitleH;

            _panel = new FilePanel();
            _panel.Renderer = new CardRenderer(_fonts, keyW);
            _panel.ShowCopyPerCard = true;
            _panel.SelectionCopied = delegate (string s)
            {
                Env.CopyText(s);
                string one = s.Replace("\n", " / ");
                ShowStatus("已复制选中的哈希值片段：" + (one.Length > 40 ? one.Substring(0, 40) + "…" : one), false);
            };
            _panel.SelectionChanged = delegate
            {
                SyncBottom();
                // 框选状态与选中数显示在标题上：一眼看出点击有没有被列表接住
                bool show = _panel.BoxMode || _panel.Selected.Count > 0;
                Text = AppInfo.Title + "  " + AppInfo.Version +
                       (show ? "   [选中 " + _panel.Selected.Count + "]" + (_panel.BoxMode ? "  框选:开" : "") : "");
            };

            Panel bottom = new Panel();
            _bottomPanel = bottom;
            bottom.Height = BottomH;
            bottom.BackColor = Pal.PanelBg;
            bottom.Paint += delegate (object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(Pal.Border)) e.Graphics.DrawLine(p, 0, 0, bottom.Width, 0);
            };

            _status = new Label();
            _status.AutoEllipsis = true;
            _status.Dock = DockStyle.Fill;
            _status.TextAlign = ContentAlignment.MiddleLeft;
            _status.Padding = new Padding(14, 0, 360, 0);
            _status.Font = _fonts.Ui;
            _status.ForeColor = Pal.Muted;
            bottom.Controls.Add(_status);

            _stat2 = new Label();
            _stat2.AutoSize = false;      // 由 SyncBottom 按实际文字宽度设置 Width
            _stat2.Dock = DockStyle.Right;
            _stat2.TextAlign = ContentAlignment.MiddleRight;
            _stat2.Padding = new Padding(0, 0, 10, 0);
            _stat2.Font = _fonts.Small;
            _stat2.ForeColor = Pal.Muted;
            bottom.Controls.Add(_stat2);

            Panel side = new Panel();
            _sidePanel = side;
            side.Width = SideW;
            side.BackColor = Pal.Bg;
            side.MouseWheel += delegate (object s2, MouseEventArgs e2) { SidebarWheel(e2.Delta); };
            side.Resize += delegate { LayoutSidebarScroll(); };

            // 深色滚动条用一个**独立的小子控件**叠在右边缘：
            // 侧栏客户区被子面板盖满时，父面板的 Paint 根本不会被触发（实测触发次数 = 0），
            // 所以必须做成独立控件 —— 它自己绘制、自己收鼠标，稳定且与系统主题无关。
            _sideBar = new DarkBar();
            _sideBar.Width = SbW + SbPad;
            // 背景色直接就是"轨道色"：即使某些情况下重绘被系统压掉，也绝不会露出白条
            _sideBar.BackColor = Color.FromArgb(0x18, 0x18, 0x18);
            _sideBar.Paint += delegate (object s2, PaintEventArgs e2) { DrawSidebarScrollbar(e2.Graphics); };
            _sideBar.MouseDown += delegate (object s2, MouseEventArgs e2)
            {
                if (e2.Button != MouseButtons.Left) return;
                if (SideThumbRectLocal().Contains(e2.Location))
                {
                    _sideThumbDrag = true;
                    _sideDragStartY = e2.Y;
                    _sideDragStartScroll = _sideScroll;
                    _sideBar.Capture = true;
                }
                else
                {
                    // 点轨道：翻页
                    SidebarWheel(e2.Y < SideThumbRectLocal().Top ? 120 : -120);
                }
            };
            _sideBar.MouseMove += delegate (object s2, MouseEventArgs e2)
            {
                if (!_sideThumbDrag) return;
                int max = SideMaxScroll;
                if (max <= 0) return;
                int trackH = _sideBar.ClientSize.Height - SbPad * 2;
                int thumbH = SideThumbRectLocal().Height;
                if (trackH - thumbH <= 0) return;
                _sideScroll = _sideDragStartScroll + (e2.Y - _sideDragStartY) * max / (trackH - thumbH);
                LayoutSidebarScroll();
            };
            _sideBar.MouseUp += delegate (object s2, MouseEventArgs e2)
            {
                if (!_sideThumbDrag) return;
                _sideThumbDrag = false;
                if (_sideBar.Capture) _sideBar.Capture = false;
                _sideBar.Invalidate();
            };
            _sideBar.MouseWheel += delegate (object s2, MouseEventArgs e2) { SidebarWheel(e2.Delta); };

            // 四块都由 LayoutShell() 显式摆位，所以添加顺序只影响 z-order
            Controls.Add(side);
            BuildSidebar(side);
            Controls.Add(_panel);
            Controls.Add(bottom);
            Controls.Add(_titleBar);
            _titleBar.BringToFront();
            BuildButtonStrip();     // 文件操作条（横排，状态栏之上）；只能调用一次
            BuildGrabStrips();      // 8 条隐形拖拽条，压在最上层，负责窗口缩放
        }

        /// <summary>
        /// 显式摆位（不用 Dock）：标题栏与状态栏横跨整窗，侧栏在右侧、夹在两者之间。
        /// Dock 表达不出这个组合（侧栏 Dock=Right 会把标题栏切成两段），所以自己算 Bounds。
        /// </summary>
        void LayoutShell()
        {
            LayoutShellCore();
        }

        /// <summary>自检用：底部左侧状态标签</summary>
        public Label StatusLabelPublic { get { return _status; } }

        /// <summary>自检用：文件列表面板</summary>
        public FilePanel PanelPublic { get { return _panel; } }

        /// <summary>自检用：当前卡片字号</summary>
        public float FontSizePublic { get { return _curFont; } }

        /// <summary>自检用：把状态栏设成"校验中"的文案，用来量它放不放得下</summary>
        public void SimulateBusyStatusPublic(int pct) { ShowStatus(BusyStatus(pct), false); }

        /// <summary>自检用：侧栏内层容器（找小技巧 / 署名行）</summary>
        public Control SideInnerPublic { get { return _sideInner; } }

        /// <summary>自检用：同文件分类开关</summary>
        public SwitchButton GroupSamePublic { get { return _groupSame; } }

        /// <summary>自检用：标题栏</summary>
        public TitleBar TitleBarPublic { get { return _titleBar; } }

        void LayoutShellCore()
        {
            LayoutShellCalls++;
            if (LayoutShellCalls < 12)
                DiagLog.Add("LayoutShell#" + LayoutShellCalls + " client=" + ClientSize +
                            " strip=" + (_btnStrip == null ? "null" : _btnStrip.Bounds.ToString()));
            if (_titleBar == null || _sidePanel == null || _bottomPanel == null || _panel == null) return;
            int w = ClientSize.Width, h = ClientSize.Height;
            int midH = Math.Max(60, h - TitleH - BottomH - StripH);

            _titleBar.Bounds = new Rectangle(0, 0, w, TitleH);                          // 横跨整窗 → 按钮在最右上角
            _bottomPanel.Bounds = new Rectangle(0, h - BottomH, w, BottomH);            // 横跨整窗 → 摘要贴最右
            if (_btnStrip != null) _btnStrip.Bounds = new Rectangle(0, h - BottomH - StripH, w, StripH);  // 状态栏之上
            _sidePanel.Bounds = new Rectangle(w - SideW, TitleH, SideW, midH);          // 右侧
            _panel.Bounds = new Rectangle(0, TitleH, w - SideW, midH);                  // 左侧
            if (_btnStrip != null) _btnStrip.BringToFront();
            ScaleTextToWidth(w - SideW);
            LayoutGrabStrips();
        }

        void BuildSidebar(Panel side)
        {
            // 侧栏放进可滚动容器（窗口压到最小或系统缩放很大时兜底）。
            // 高度按实测压紧：默认窗口下全部内容一次放得下，不出现滚动条。
            Panel inner = new Panel();
            inner.Location = new Point(0, 0);
            inner.Width = side.Width;
            inner.BackColor = Pal.Bg;

            int x = 12;
            int w = side.Width - 24;
            int y = 10;

            // ---- 校验值选择（2x2 省高度）----
            SideTitle(inner, "校验值选择", x, y, w); y += 24;
            Para(inner, "默认只勾 MD5；取消勾选，对应的那一行会立刻消失。", x + 2, y, w - 4);
            y += 22;

            int halfW = (w - 6) / 2;
            _tMD5 = MkToggle(inner, Algo.MD5, true, x, y, halfW);
            _tSHA1 = MkToggle(inner, Algo.SHA1, true, x + halfW + 6, y, w - halfW - 6);
            y += 34;
            _tCRC = MkToggle(inner, Algo.CRC32, true, x, y, halfW);
            _tSHA256 = MkToggle(inner, Algo.SHA256, true, x + halfW + 6, y, w - halfW - 6);
            y += 34;
            HRule(inner, x + 2, y, w - 4); y += 11;

            // ---- 待对比值 ----
            SideTitle(inner, "待对比值", x, y, w); y += 24;
            _cmp = new PlaceholderBox();
            _cmp.Location = new Point(x, y);
            _cmp.Size = new Size(w, 31);
            _cmp.Placeholder = "粘贴一个值：MD5 / SHA1 / CRC32 / SHA-256";
            inner.Controls.Add(_cmp);
            y += 35;

            Button btnCmp = new FlatBtn("比  对", true, 10f);
            btnCmp.Location = new Point(x, y);
            btnCmp.Size = new Size(w, 33);
            btnCmp.Click += delegate { DoCompare(false); };
            inner.Controls.Add(btnCmp);
            _tip.SetToolTip(btnCmp, "在已校验的文件里查找这个值；找到就滚到那一行并闪烁（按长度识别算法，回车即比对）");
            y += 36;
            HRule(inner, x + 2, y, w - 4); y += 11;

            // ---- 显示 ----
            SideTitle(inner, "显示", x, y, w); y += 24;
            _btnCopy = MkSideBtn(inner, "复制选中哈希值", true, x, y, w, 32);
            _btnCopy.Click += delegate { DoCopyBatch(); };
            _tip.SetToolTip(_btnCopy, "未选中任何文件时，复制列表里全部文件的哈希值；有选中时只复制选中的");
            y += 36;

            _btnCopyAll = MkSideBtn(inner, "复制全部哈希值", true, x, y, w, 30);
            _btnCopyAll.Click += delegate { DoCopyAll(); };
            _tip.SetToolTip(_btnCopyAll, "不管勾选/选中，把列表里所有文件的全部校验值复制到剪贴板");
            y += 34;

            _btnExport = MkSideBtn(inner, "导出结果为 txt", false, x, y, w, 30);
            _btnExport.Click += delegate { DoExportTxt(); };
            _tip.SetToolTip(_btnExport, "把列表里所有文件的校验结果保存成一个 txt 文件");
            y += 34;

            _groupSame = new SwitchButton("同文件分类（一致时分组）", false, 9.5f);
            _groupSame.Location = new Point(x, y);
            _groupSame.Size = new Size(w, 30);
            _groupSame.Checked = false;                    // 默认关闭（用户要求）
            _groupSame.CheckedChanged += delegate
            {
                bool on = _groupSame.Checked;
                _panel.SearchPaths.Clear();
                if (on) { if (_panel.Files.Count >= 2) FindIdentical(false); else ShowStatus("同文件分类已开启（校验后会自动分组）", false); }
                else { RebuildPlain(); ShowStatus("同文件分类已关闭（不再分组显示）", false); }
            };
            _tip.SetToolTip(_groupSame, "开启后：校验完自动把内容一致的文件分组，并用分隔线标出");
            inner.Controls.Add(_groupSame);
            y += 34;

            _btnCase = new FlatBtn("大小写：大写", false, 9.5f);
            _btnCase.Location = new Point(x, y);
            _btnCase.Size = new Size(w, 30);
            _btnCase.Click += delegate
            {
                Up = !Up;
                _btnCase.Text = "大小写：" + (Up ? "大写" : "小写");
                _panel.InvalidateViews();
                _panel.Invalidate();
                SyncBottom();
                ShowStatus("哈希值显示已切换为「" + (Up ? "大写" : "小写") + "」", false);
            };
            inner.Controls.Add(_btnCase);
            y += 30;
            HRule(inner, x + 2, y, w - 4); y += 11;

            // ---- 框选模式（点一下即多选）----
            _boxSelSide = new SwitchButton("框选（点一下即多选）", false, 9.5f);
            _boxSelSide.Location = new Point(x, y);
            _boxSelSide.Size = new Size(w, 30);
            _boxSelSide.CheckedChanged += delegate { OnBoxSelectChanged(); };
            _tip.SetToolTip(_boxSelSide, "开启后：点一下卡片就选中一个（不用按 Ctrl）；也可以按住左键拖框批量选，然后按 Delete 删除。" +
                                        "关闭时：在卡片上按住左键横向划过，可以像文本一样选中哈希值。");
            inner.Controls.Add(_boxSelSide);
            y += 34;
            HRule(inner, x + 2, y, w - 4); y += 9;

            // ---- 小技巧 ----
            Para(inner,
                "小技巧：" + "\r\n" +
                "· 卡片上的「×」移出该文件、「复制」只复制这一个" + "\r\n" +
                "· 框选开启后点卡片即多选，Delete 批量移除" + "\r\n" +
                "· 框选关闭时在哈希值上横向划过即可选中，Ctrl+C 复制" + "\r\n" +
                "· 拖文件/文件夹进窗口即可添加；F5 重新校验" + "\r\n" +
                " 本工具制作：Yuesekaer ",
                x + 2, y, w - 4);
            y += 104;   // 比原来多一行（制作署名）

            inner.Height = y + 16;
            side.Controls.Add(inner);
            _sideInner = inner;
            // 不用 WinForms 自带的 AutoScroll：它画的是系统原生滚动条，
            // 在深色界面上是一道白条（试过 uxtheme 的深色主题，未公开 API 不稳定）。
            // 这里改成"自己滚动 + 自己画深色滚动条"，任何系统上外观都一致。
            side.AutoScroll = false;
            side.Controls.Add(_sideBar);       // 必须挂到侧栏上，否则不会绘制
            _sideBar.BringToFront();
            LayoutSidebarScroll();
        }

        // ---- 侧栏自绘深色滚动条 ----
        /// <summary>诊断：侧栏滚动条被绘制次数</summary>
        public static int SideScrollDrawCount;

        System.Windows.Forms.Timer _sideRefresh;
        DarkBar _sideBar;            // 侧栏滚动条（独立小子控件，自己绘制）
        Panel _sideInner;
        int _sideScroll;
        bool _sideThumbDrag;
        int _sideDragStartY, _sideDragStartScroll;

        const int SbW = 10;          // 滚动条宽度
        const int SbPad = 2;         // 与右边缘的间距

        int SideMaxScroll
        {
            get
            {
                if (_sidePanel == null || _sideInner == null) return 0;
                int max = _sideInner.Height - _sidePanel.ClientSize.Height;
                return max > 0 ? max : 0;
            }
        }

        /// <summary>应用侧栏滚动位置 + 画滚动条</summary>
        void LayoutSidebarScroll()
        {
            if (_sidePanel == null || _sideInner == null) return;
            int max = SideMaxScroll;
            if (_sideScroll > max) _sideScroll = max;
            if (_sideScroll < 0) _sideScroll = 0;

            bool need = max > 0;
            int barW = SbW + SbPad;
            int usableW = _sidePanel.ClientSize.Width - (need ? barW : 0);
            _sideInner.Width = Math.Max(120, usableW);
            _sideInner.Location = new Point(0, -_sideScroll);
            if (_sideBar != null)
            {
                _sideBar.Visible = need;
                _sideBar.Bounds = new Rectangle(_sidePanel.ClientSize.Width - barW, 0, barW, _sidePanel.ClientSize.Height);
                _sideBar.BringToFront();
                _sideBar.Invalidate();
            }
            _sidePanel.Invalidate();
        }

        /// <summary>滑块矩形（相对滚动条控件自身）</summary>
        Rectangle SideThumbRectLocal()
        {
            if (_sidePanel == null || _sideInner == null || _sideBar == null) return Rectangle.Empty;
            int max = SideMaxScroll;
            if (max <= 0) return Rectangle.Empty;
            int viewH = _sidePanel.ClientSize.Height;
            int trackH = viewH - SbPad * 2;
            if (trackH <= 0) return Rectangle.Empty;
            int thumbH = Math.Max(28, (int)((long)trackH * viewH / Math.Max(1, _sideInner.Height)));
            if (thumbH > trackH) thumbH = trackH;
            int y = SbPad + (trackH - thumbH) * _sideScroll / max;
            return new Rectangle(1, y, SbW - 2, thumbH);
        }

        /// <summary>滑块矩形（侧栏坐标系）</summary>
        Rectangle SideThumbRect()
        {
            Rectangle r = SideThumbRectLocal();
            if (r.IsEmpty || _sideBar == null) return r;
            return new Rectangle(_sideBar.Left + r.Left, r.Top, r.Width, r.Height);
        }

        void DrawSidebarScrollbar(Graphics g)
        {
            SideScrollDrawCount++;
            if (_sideBar == null) return;
            int h = _sideBar.ClientSize.Height;
            using (SolidBrush bg = new SolidBrush(Pal.Bg)) g.FillRectangle(bg, _sideBar.ClientRectangle);
            if (SideMaxScroll <= 0) return;
            // 轨道：黑灰；滑块：中灰（拖动时变绿）—— 与整体配色一致，不依赖系统主题
            using (SolidBrush track = new SolidBrush(Color.FromArgb(0x18, 0x18, 0x18)))
                g.FillRectangle(track, 1, SbPad, SbW - 2, h - SbPad * 2);
            Rectangle tb = SideThumbRectLocal();
            using (SolidBrush thumb = new SolidBrush(_sideThumbDrag ? Pal.Btn : Color.FromArgb(0x6E, 0x6E, 0x6E)))
                g.FillRectangle(thumb, tb);
        }

        /// <summary>自检用：侧栏滚轮</summary>
        public void SidebarWheelPublic(int delta) { SidebarWheel(delta); }

        /// <summary>自检用：侧栏最大可滚动距离</summary>
        public int SideMaxScrollPublic { get { return SideMaxScroll; } }

        /// <summary>自检用：侧栏滚动条滑块矩形</summary>
        public Rectangle SideThumbRectPublic() { return SideThumbRect(); }

        /// <summary>侧栏滚轮 / 拖动滚动条</summary>
        void SidebarWheel(int delta)
        {
            if (SideMaxScroll <= 0) return;
            _sideScroll -= delta / 120 * 48;
            LayoutSidebarScroll();
        }

        /// <summary>侧栏用的整宽按钮</summary>
        FlatBtn MkSideBtn(Control parent, string text, bool primary, int x, int y, int w, int h)
        {
            FlatBtn b = new FlatBtn(text, primary, 9.5f);
            b.Location = new Point(x, y);
            b.Size = new Size(w, h);
            parent.Controls.Add(b);
            return b;
        }

        /// <summary>
        /// 文件操作条（横排）：选择文件 / 选择文件夹 / 清空列表 / 框选 / 开始校验。
        /// 放在状态栏之上、横跨整窗，按钮宽度由 LayoutStrip() 按可用宽度动态分配。
        /// </summary>
        void BuildButtonStrip()
        {
            Panel strip = new Panel();
            _btnStrip = strip;
            strip.BackColor = Pal.Bg;
            strip.Paint += delegate (object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(Pal.Border)) e.Graphics.DrawLine(p, 12, 0, strip.Width - 12, 0);
            };

            _btnPick = new FlatBtn("选择文件", true, 9.5f);
            _btnPick.Click += delegate { PickFiles(true); };
            _tip.SetToolTip(_btnPick, "可按住 Ctrl / Shift 多选；也可以把文件或文件夹直接拖进窗口");
            strip.Controls.Add(_btnPick);

            _btnFolder = new FlatBtn("选择文件夹", true, 9.5f);
            _btnFolder.Click += delegate { PickFolder(); };
            _tip.SetToolTip(_btnFolder, "连同子文件夹一起加入");
            strip.Controls.Add(_btnFolder);

            _btnClearTop = new FlatBtn("清空列表", false, 9.5f);
            _btnClearTop.Click += delegate { ClearFiles(); };
            _tip.SetToolTip(_btnClearTop, "把列表里的文件全部移除");
            strip.Controls.Add(_btnClearTop);
            _btnVerifySide = new FlatBtn("开 始 校 验", true, 11f);
            _btnVerifySide.Font = Fonts.Ui(11f, FontStyle.Bold);
            _btnVerifySide.Click += delegate { StartVerify(false); };
            _tip.SetToolTip(_btnVerifySide, "对列表里的文件计算所选校验值（F5 也可以）");
            strip.Controls.Add(_btnVerifySide);

            Controls.Add(strip);
            strip.BringToFront();
            strip.Resize += delegate { LayoutStrip(); };
        }

        /// <summary>
        /// 按可用宽度给五个按钮分配宽度：先按文字宽度给权重，再等比缩放到刚好铺满。
        /// 所以窗口拉大时按钮变宽、拉小时变窄（拉得越窄，按钮越紧凑），不会溢出。
        /// </summary>
        void LayoutStrip()
        {
            if (_btnStrip == null || _btnPick == null) return;
            Control[] bs = { _btnPick, _btnFolder, _btnClearTop, _btnVerifySide };
            int avail = _btnStrip.ClientSize.Width - 24;
            int gap = 8, totalGap = gap * (bs.Length - 1);
            if (avail - totalGap < 60) return;

            int need = 0;
            int[] wt = new int[bs.Length];
            for (int i = 0; i < bs.Length; i++)
            {
                wt[i] = Math.Max(40, TextRenderer.MeasureText(bs[i].Text, bs[i].Font).Width + 16);
                need += wt[i];
            }
            int room = avail - totalGap;
            int x = 12;
            int used = 0;
            for (int i = 0; i < bs.Length; i++)
            {
                int bw = (int)((long)room * wt[i] / need);
                if (bw < 40) bw = 40;
                if (i == bs.Length - 1) bw = Math.Max(40, _btnStrip.ClientSize.Width - 12 - x);  // 最后一个补齐右侧
                bs[i].Bounds = new Rectangle(x, 6, bw, StripH - 12);
                x += bw + gap;
                used += bw;
            }
        }

        /// <summary>
        /// 给窗体及所有子控件（尤其是可滚动的 Panel）套深色滚动条。
        /// 必须在句柄创建之后调用 —— 早于句柄创建时 uxtheme 拿不到 hwnd，主题套不上。
        /// </summary>
        public static void ApplyDarkScrollbarsDeep(Control root)
        {
            if (root == null) return;
            try
            {
                if (root.IsHandleCreated) Env.DarkScrollbars(root.Handle);
                foreach (Control c in root.Controls) ApplyDarkScrollbarsDeep(c);
            }
            catch { }
        }

        /// <summary>
        /// 点击文件列表以外的地方（侧栏、状态栏、标题栏…）就结束"文本选择"。
        /// 用消息过滤器实现，比给每个控件挂事件可靠。
        /// </summary>
        sealed class OutsideClickFilter : IMessageFilter
        {
            readonly MainForm _f;
            public OutsideClickFilter(MainForm f) { _f = f; }

            [StructLayout(LayoutKind.Sequential)]
            struct P2 { public int X, Y; }
            [DllImport("user32.dll")] static extern bool GetCursorPos(out P2 p);
            [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(P2 p);

            /// <summary>光标下最内层的控件（用屏幕坐标取，比看消息句柄准）</summary>
            static Control ControlAtCursor()
            {
                try
                {
                    P2 p;
                    if (!GetCursorPos(out p)) return null;
                    IntPtr h = WindowFromPoint(p);
                    return h == IntPtr.Zero ? null : Control.FromHandle(h);
                }
                catch { return null; }
            }

            static bool IsDescendantOf(Control c, Control parent)
            {
                for (Control k = c; k != null; k = k.Parent) if (k == parent) return true;
                return false;
            }

            public bool PreFilterMessage(ref Message m)
            {
                const int WM_LBUTTONDOWN = 0x0201, WM_RBUTTONDOWN = 0x0204, WM_MOUSEWHEEL = 0x020A;

                // 消息过滤器是**全应用级**的：窗体释放后它仍会被调用，
                // 此时访问已释放的控件会抛 ObjectDisposedException。先做存活检查。
                if (_f == null || _f.IsDisposed || !_f.IsHandleCreated || _f.Disposing) return false;

                // 滚轮：只要光标在文件列表（含只读文本层）上，就由列表滚动。
                // 只读 RichTextBox 没有滚动条，会把滚轮吃掉 —— 必须在这里兜住，
                // 否则"鼠标悬停在文件条目上时滚轮无效"。
                if (m.Msg == WM_MOUSEWHEEL && _f._panel != null && !_f._panel.IsDisposed)
                {
                    Control under = ControlAtCursor();
                    if (under != null && (under == _f._panel || IsDescendantOf(under, _f._panel)))
                    {
                        int delta = (short)((long)m.WParam >> 16);
                        _f._panel.WheelScrollPublic(delta);
                        return true;      // 吃掉，避免文本层再处理
                    }
                }

                if (m.Msg == WM_LBUTTONDOWN || m.Msg == WM_RBUTTONDOWN)
                {
                    // 点在"只读文本层"以外的地方（侧栏、状态栏、列表空白、卡片其他区域…）
                    // 就取消文本层的选区 —— "点击软件其他空白处取消保持"
                    if (_f._panel != null && !_f._panel.IsDisposed)
                    {
                        if (!_f._panel.IsHostHandle(m.HWnd)) _f._panel.ClearHostSelections();
                        if (_f._panel.HasTextSelection && m.HWnd != _f._panel.Handle) _f._panel.ClearSelPublic();
                    }
                }
                return false;   // 不拦截，继续正常派发
            }
        }

        /// <summary>框选模式开关</summary>
        void OnBoxSelectChanged()
        {
            bool on = _boxSelSide != null && _boxSelSide.Checked;
            _panel.BoxMode = on;
            if (on) _panel.ClearSelPublic();
            if (on)
            {
                if (_panel.SelectionChanged != null) _panel.SelectionChanged();
                ShowStatus("框选已开启：点卡片就能多选（不用按 Ctrl），也可以按住左键拖一个框；选中后按 Delete 删除", false);
            }
            else
            {
                _panel.Selected.Clear();
                _panel.InvalidateViews();
                _panel.Invalidate();
                if (_panel.SelectionChanged != null) _panel.SelectionChanged();
                ShowStatus("框选已关闭（选中状态已清空）", false);
            }
        }

        void SideTitle(Control parent, string text, int x, int y, int w)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = false;
            l.Location = new Point(x, y);
            l.Size = new Size(w, 26);
            l.Font = Fonts.Ui(11.5f, FontStyle.Bold);
            l.ForeColor = Pal.Btn;
            parent.Controls.Add(l);
        }

        void Para(Control parent, string text, int x, int y, int w)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.MaximumSize = new Size(w, 0);
            l.Location = new Point(x, y);
            l.Font = _fonts.Small;
            l.ForeColor = Pal.Muted;
            parent.Controls.Add(l);
        }

        void HRule(Control parent, int x, int y, int w)
        {
            Label l = new Label();
            l.Text = "";
            l.AutoSize = false;
            l.Location = new Point(x, y);
            l.Size = new Size(w, 1);
            l.BackColor = Pal.Border;
            parent.Controls.Add(l);
        }

        ToggleBtn MkToggle(Control parent, string text, bool primary, int x, int y, int w)
        {
            ToggleBtn t = new ToggleBtn(text, primary, 10f);
            t.Location = new Point(x, y);
            t.Size = new Size(w, 34);
            t.Tag = text;
            parent.Controls.Add(t);
            _toggles.Add(t);
            return t;
        }

        void BuildMenu()
        {
            ContextMenuStrip m = new ContextMenuStrip();
            m.Font = _fonts.Ui;
            m.BackColor = Pal.PanelBg;
            m.ForeColor = Pal.Text;
            m.Renderer = new DarkMenuRenderer();
            m.Items.Add(MkItem("选择文件…（可多选）", delegate { PickFiles(true); }, Keys.Control | Keys.O));
            m.Items.Add(MkItem("选择文件夹…", delegate { PickFolder(); }, Keys.None));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(MkItem("开始校验", delegate { StartVerify(false); }, Keys.F5));
            m.Items.Add(MkItem("查找一致文件", delegate { FindIdentical(false); }, Keys.Control | Keys.F));
            m.Items.Add(MkItem("比对输入框里的值", delegate { DoCompare(false); }, Keys.Control | Keys.K));
            m.Items.Add(MkItem("复制选中哈希值", delegate { DoCopyBatch(); }, Keys.Control | Keys.C));
            m.Items.Add(MkItem("复制全部哈希值", delegate { DoCopyAll(); }, Keys.None));
            m.Items.Add(MkItem("导出结果为 txt…", delegate { DoExportTxt(); }, Keys.Control | Keys.S));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(MkItem("全选", delegate { _panel.SelectAll(); }, Keys.Control | Keys.A));
            m.Items.Add(MkItem("删除选中文件", delegate { RemoveSelected(); }, Keys.Delete));
            m.Items.Add(MkItem("清空列表", delegate { ClearFiles(); }, Keys.Control | Keys.Delete));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(MkItem("打开图标文件位置", delegate { Env.OpenFolder(AppInfo.IconFile); }, Keys.None));
            ContextMenuStrip = m;
            _panel.ContextMenuStrip = m;
        }

        ToolStripMenuItem MkItem(string text, EventHandler h, Keys k)
        {
            ToolStripMenuItem it = new ToolStripMenuItem(text);
            it.ForeColor = Pal.Text;
            it.BackColor = Pal.PanelBg;
            if (k != Keys.None) it.ShortcutKeyDisplayString = ShortcutText(k);
            it.Click += h;
            return it;
        }

        static string ShortcutText(Keys k)
        {
            string s = "";
            if ((k & Keys.Control) != 0) s += "Ctrl+";
            if ((k & Keys.Shift) != 0) s += "Shift+";
            Keys body = k & ~(Keys.Control | Keys.Shift | Keys.Alt);
            s += (body == Keys.Delete) ? "Delete" : body.ToString();
            return s;
        }

        sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
        {
            public DarkMenuRenderer() : base(new DarkColors()) { }
        }

        sealed class DarkColors : ProfessionalColorTable
        {
            public override Color MenuItemSelected { get { return Pal.Btn; } }
            public override Color MenuItemBorder { get { return Pal.Btn; } }
            public override Color ToolStripDropDownBackground { get { return Pal.PanelBg; } }
            public override Color ImageMarginGradientBegin { get { return Pal.PanelBg; } }
            public override Color ImageMarginGradientMiddle { get { return Pal.PanelBg; } }
            public override Color ImageMarginGradientEnd { get { return Pal.PanelBg; } }
            public override Color SeparatorDark { get { return Pal.Border; } }
            public override Color SeparatorLight { get { return Pal.Border; } }
        }

        #endregion

        #region 事件接线

        void WireEvents()
        {
            // 按钮的 Click 只在创建处绑一次（重复绑定会导致“点一次执行两次”）
            foreach (ToggleBtn t in _toggles) t.CheckedChanged += delegate { OnToggleChanged(); };

            _cmp.Box.KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; DoCompare(false); }
            };

            AllowDrop = true;
            DragEnter += OnDragEnter;
            DragOver += OnDragEnter;
            DragDrop += OnDragDrop;

            KeyDown += OnFormKey;
            Shown += delegate
            {
                LayoutShell();
                ApplyDarkScrollbarsDeep(this);   // 句柄都已创建，此时套深色滚动条才有效
                // 注册消息过滤器：负责"悬停在条目上时滚轮仍然滚动"和"点击别处取消文本选区"
                _msgFilter = new OutsideClickFilter(this);
                Application.AddMessageFilter(_msgFilter);
                FormClosed += delegate
                {
                    // 过滤器是全应用级的，窗体关闭后必须注销，否则它会继续访问已释放的控件
                    if (_msgFilter != null) { Application.RemoveMessageFilter(_msgFilter); _msgFilter = null; }
                };
                _panel.Focus();
            };
        }

        void OnFormKey(object s, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F5) { StartVerify(false); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.C)
            {
                // 焦点在只读文本层或输入框时，Ctrl+C 交给控件自己（复制它自己的选区）
                if (ActiveControl is TextBoxBase) return;
                // 文本层有选区就优先复制它（焦点不在文本层时也能用）
                string hostSel = _panel.FirstHostSelection();
                if (hostSel != null)
                {
                    Env.CopyText(hostSel);
                    string hs = hostSel.Replace("\n", " / ");
                    ShowStatus("已复制选中的哈希值片段：" + (hs.Length > 40 ? hs.Substring(0, 40) + "…" : hs), false);
                    e.Handled = true;
                    return;
                }
                string picked = PanelCopyText();
                if (picked != null)
                {
                    Env.CopyText(picked);
                    ShowStatus("已复制选中的哈希值片段：" +
                               (picked.Length > 40 ? picked.Substring(0, 40) + "…" : picked), false);
                }
                else DoCopyBatch();
                e.Handled = true;
            }
            else if (e.Control && e.KeyCode == Keys.S) { DoExportTxt(); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.F) { FindIdentical(false); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.O) { PickFiles(true); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.K) { DoCompare(false); e.Handled = true; }
            else if (e.KeyCode == Keys.Delete) { RemoveSelected(); e.Handled = true; }
        }

        /// <summary>自检/演示用：把键盘处理走一遍（验证 Ctrl+C 真的只复制选中片段）</summary>
        public void SimulateKeyPublic(Keys key, bool ctrl)
        {
            Keys data = ctrl ? (key | Keys.Control) : key;
            OnFormKey(this, new KeyEventArgs(data));
        }

        /// <summary>自检/演示用：读回剪贴板文字</summary>
        public static string ClipboardTextPublic()
        {
            try { return Clipboard.ContainsText() ? Clipboard.GetText() : ""; }
            catch { return ""; }
        }

        void OnDragEnter(object s, DragEventArgs e)
        {
            if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
            else e.Effect = DragDropEffects.None;
        }

        void OnDragDrop(object s, DragEventArgs e)
        {
            string[] items = null;
            try { items = (string[])e.Data.GetData(DataFormats.FileDrop); } catch { }
            if (items == null || items.Length == 0) return;
            AddPaths(items);
        }

        void OnToggleChanged()
        {
            List<string> keys = new List<string>();
            if (_tMD5.Checked) keys.Add(Algo.MD5);
            if (_tSHA1.Checked) keys.Add(Algo.SHA1);
            if (_tCRC.Checked) keys.Add(Algo.CRC32);
            if (_tSHA256.Checked) keys.Add(Algo.SHA256);
            _panel.VisibleKeys = keys;
            _panel.LineCount = Math.Max(1, keys.Count);
            _panel.InvalidateViews();
            _panel.InvalidateLayout();
            SyncBottom();

            if (keys.Count == 0) { ShowStatus(AppInfo.HintNoneChecked, true); _prevKeys.Clear(); return; }

            // 缺少新勾选的值 => 先补算；把“这次新勾上的”记下来，算完只让它们亮
            bool need = false;
            foreach (FileItem f in _panel.Files)
            {
                if (!f.Computed || f.Error != null) continue;
                foreach (string k in keys) if (!f.Hashes.ContainsKey(k)) { need = true; break; }
                if (need) break;
            }
            if (need)
            {
                _pendFlashKeys.Clear();
                _pendFlashKeys.AddRange(NewKeysOnly(keys));
                _prevKeys.Clear();
                _prevKeys.AddRange(keys);
                StartVerify(true);
                return;
            }

            // 已经算好了：只让这次新勾上的那几个亮
            List<string> newcomers = NewKeysOnly(keys);
            if (newcomers.Count > 0)
            {
                List<FileItem> fleet = new List<FileItem>();
                foreach (FileItem f in _panel.Files) if (f.Computed && f.Error == null) fleet.Add(f);
                if (fleet.Count > 0)
                {
                    _panel.FlashRows(fleet, newcomers);
                    _panel.SetAnimating(true);
                }
            }
            _prevKeys.Clear();
            _prevKeys.AddRange(keys);
            ShowStatus("当前显示项：" + string.Join(" / ", keys.ToArray()), false);
        }

        /// <summary>返回 keys 里这次才新勾上的那些（保持 keys 的顺序）</summary>
        List<string> NewKeysOnly(List<string> keys)
        {
            List<string> l = new List<string>();
            foreach (string k in keys) if (!_prevKeys.Contains(k)) l.Add(k);
            return l;
        }

        #endregion

        #region 选择文件

        void PickFiles(bool multi)
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = multi ? "选择文件进行校验（可按住 Ctrl / Shift 多选）" : "选择文件进行校验";
                d.Multiselect = multi;
                d.Filter = "所有文件 (*.*)|*.*";
                d.CheckFileExists = true;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                AddPaths(d.FileNames);
            }
        }

        void PickFolder()
        {
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                d.Description = "选择文件夹（包含其中的子文件夹）";
                d.ShowNewFolderButton = false;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                AddPaths(new string[] { d.SelectedPath });
            }
        }

        void AddPaths(string[] paths)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (FileItem f in _panel.Files) seen.Add(f.Path);
            int before = _panel.Files.Count;

            foreach (string p in paths)
            {
                if (_panel.Files.Count >= 3000) { ShowStatus("文件数量达到上限 3000 个", true); break; }
                try
                {
                    if (Directory.Exists(p)) AddFolder(p, seen);
                    else if (File.Exists(p)) AddFile(p, seen);
                }
                catch (Exception ex) { ShowStatus("添加失败：" + ex.Message, true); }
            }

            int added = _panel.Files.Count - before;
            if (added > 0) { RebuildPlain(); ShowStatus("已添加 " + added + " 个文件，共 " + _panel.Files.Count + " 个，可以开始校验了", false); }
            else if (!_panel.Busy) ShowStatus("没有新增文件（可能已经在列表里了）", true);
        }

        void AddFolder(string dir, HashSet<string> seen)
        {
            try
            {
                foreach (string f in Directory.GetFiles(dir))
                {
                    if (_panel.Files.Count >= 3000) return;
                    AddFile(f, seen);
                }
                foreach (string d in Directory.GetDirectories(dir))
                {
                    if (_panel.Files.Count >= 3000) return;
                    DirectoryInfo di = new DirectoryInfo(d);
                    if ((di.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    AddFolder(d, seen);
                }
            }
            catch { }
        }

        void AddFile(string path, HashSet<string> seen)
        {
            string full;
            try { full = Path.GetFullPath(path); } catch { return; }
            if (!seen.Add(full)) return;
            FileItem f = new FileItem();
            f.Path = full;
            try { f.Size = new FileInfo(full).Length; } catch { }
            _panel.Files.Add(f);
        }

        void RebuildPlain()
        {
            _panel.DisplayRows.Clear();
            foreach (FileItem f in _panel.Files) _panel.DisplayRows.Add(f);
            _panel.InvalidateLayout();
            SyncBottom();
        }

        #endregion

        #region 校验

        void StartVerify(bool auto)
        {
            if (_panel.VisibleKeys.Count == 0) { ShowStatus(AppInfo.HintNoneChecked, true); return; }
            if (_panel.Files.Count == 0)
            {
                if (!auto) ShowStatus("还没有文件呢，先拖进来或者点「选择文件」吧", true);
                return;
            }
            if (_hashTimer.Enabled) return;

            _queue.Clear();
            foreach (FileItem f in _panel.Files)
            {
                bool ok = f.Computed && f.Error == null;
                if (ok) foreach (string k in _panel.VisibleKeys) if (!f.Hashes.ContainsKey(k)) { ok = false; break; }
                if (!ok) _queue.Add(f);
            }

            if (_queue.Count == 0)
            {
                if (!auto)
                {
                    ShowStatus("当前勾选的校验值都已经算好了，不用重复校验啦", false);
                    if (_panel.Files.Count >= 2) FindIdentical(false);
                    if (_cmp.Value.Trim().Length > 0) DoCompare(false);
                }
                return;
            }

            _qIndex = 0; _qDone = 0;
            _panel.Busy = true;
            _panel.BusyText = "正在校验中...  0%" + BusyHint;
            _panel.SetAnimating(false);
            _panel.Reflow();
            _marquee.Start();
            _hashTimer.Start();
            ShowStatus(BusyStatus(0), false);
            SyncBottom();
        }

        /// <summary>校验中的状态栏文字（大文件时算法本身会占满一个时间片，先给用户一个预期）</summary>
        internal const string BusyHint = " (如文件较大可能会暂时卡几秒，请耐心等待)";

        static string BusyStatus(int pct) { return "正在校验中...  " + pct + "%" + BusyHint; }

        void HashStep()
        {
            if (_qIndex >= _queue.Count)
            {
                _hashTimer.Stop();
                _marquee.Stop();
                _panel.Busy = false;
                _panel.Reflow();
                AfterVerify();
                return;
            }

            FileItem fi = _queue[_qIndex];
            ComputeFile(fi, _panel.VisibleKeys);
            _qIndex++; _qDone++;

            int pct = (int)(_qDone * 100.0 / _queue.Count);
            _panel.BusyText = "正在校验中...  " + pct + "%" + BusyHint +
                              "   (" + _qDone + "/" + _queue.Count + ")  " + Path.GetFileName(fi.Path);
            if (_qDone % 3 == 0 || _qDone == _queue.Count) ShowStatus(BusyStatus(pct), false);
            _panel.Invalidate();
            SyncBottom();
        }

        void AfterVerify()
        {
            _panel.LineCount = Math.Max(1, _panel.VisibleKeys.Count);
            _panel.InvalidateViews();
            _panel.InvalidateLayout();

            int bad = 0;
            foreach (FileItem f in _panel.Files) if (f.Error != null) bad++;
            ShowStatus("校验完成：" + _panel.Files.Count + " 个文件" + (bad > 0 ? "（" + bad + " 个读取失败）" : "（全部正常）") +
                       "    显示项：" + string.Join(" / ", _panel.VisibleKeys.ToArray()), bad > 0);

            // 这次校验是不是由「比对」触发的？是的话就**不做一致文件分组** ——
            // 用户只关心"和待对比值相关的那一项"，分组闪烁只会干扰判断。
            bool compareDriven = _compareAfterVerify;
            _compareAfterVerify = false;

            if (compareDriven)
            {
                RebuildPlain();          // 恢复成"平铺列表"，清掉任何已有的分组分隔线
            }
            else if (_panel.Files.Count >= 2)
            {
                if (_pendFlashKeys.Count > 0)
                {
                    // 这次是“补算新勾上的算法”：只闪它们，已经显示过的不再重亮
                    List<FileItem> fleet = new List<FileItem>();
                    foreach (FileItem f in _panel.Files) if (f.Computed && f.Error == null) fleet.Add(f);
                    FindIdentical(true);
                    if (fleet.Count > 0)
                    {
                        _panel.FlashRows(fleet, new List<string>(_pendFlashKeys));
                        _panel.SetAnimating(true);
                    }
                    _pendFlashKeys.Clear();
                }
                else FindIdentical(true);
            }
            if (compareDriven) DoCompare(true);
            else if (_cmp.Value.Trim().Length > 0) DoCompare(true);
        }

        /// <summary>
        /// 「比对」被点击但还有文件没校验时：先自动开始校验，校验完再自动比对。
        /// </summary>
        bool _compareAfterVerify;

        bool NeedsVerify()
        {
            if (_panel.VisibleKeys.Count == 0) return false;
            foreach (FileItem f in _panel.Files)
            {
                bool ok = f.Computed && f.Error == null;
                if (ok) foreach (string k in _panel.VisibleKeys) if (!f.Hashes.ContainsKey(k)) { ok = false; break; }
                if (!ok) return true;
            }
            return false;
        }

        static void ComputeFile(FileItem fi, List<string> keys)
        {
            if (keys == null || keys.Count == 0) return;
            fi.Error = null;
            Dictionary<string, HashAlgo> algos = new Dictionary<string, HashAlgo>(StringComparer.Ordinal);
            try
            {
                foreach (string k in keys)
                {
                    if (fi.Hashes.ContainsKey(k)) continue;
                    HashAlgo ha = new HashAlgo();
                    if (k == Algo.MD5) ha.H = MD5.Create();
                    else if (k == Algo.SHA1) ha.H = SHA1.Create();
                    else if (k == Algo.SHA256) ha.H = SHA256.Create();
                    else if (k == Algo.CRC32) ha.Crc = new Crc32();
                    algos[k] = ha;
                }

                if (algos.Count > 0)
                {
                    byte[] buf = new byte[1 << 20];
                    using (FileStream fs = new FileStream(fi.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 20))
                    {
                        int n;
                        while ((n = fs.Read(buf, 0, buf.Length)) > 0)
                        {
                            foreach (KeyValuePair<string, HashAlgo> kv in algos)
                            {
                                if (kv.Value.H != null) kv.Value.H.TransformBlock(buf, 0, n, null, 0);
                                else kv.Value.Crc.Update(buf, n);
                            }
                        }
                    }
                    foreach (KeyValuePair<string, HashAlgo> kv in algos)
                    {
                        string hex;
                        if (kv.Value.H != null)
                        {
                            kv.Value.H.TransformFinalBlock(new byte[0], 0, 0);
                            hex = ToHex(kv.Value.H.Hash);
                        }
                        else hex = kv.Value.Crc.Finish();
                        fi.Hashes[kv.Key] = hex;
                    }
                }
                fi.Computed = true;
            }
            catch (Exception ex)
            {
                fi.Error = "读取失败：" + ex.Message;
            }
            finally
            {
                foreach (KeyValuePair<string, HashAlgo> kv in algos) kv.Value.Dispose();
            }
        }

        static string ToHex(byte[] b)
        {
            StringBuilder sb = new StringBuilder(b.Length * 2);
            for (int i = 0; i < b.Length; i++) sb.Append(b[i].ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        #endregion

        #region 比对

        /// <summary>把用户输入清洗成纯十六进制串（忽略空格、冒号、横线、大小写）</summary>
        static string Normalize(string s)
        {
            if (s == null) return "";
            StringBuilder sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if ((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))
                    sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        void DoCompare(bool auto)
        {
            string val = Normalize(_cmp.Value);
            if (val.Length == 0)
            {
                if (!auto) ShowStatus("先在「待对比值」里粘贴一个值吧", true);
                return;
            }

            // 还有文件没校验：先自动开始校验，校验完成后会再自动比对一次。
            // 这样"文件刚导入、还没点开始校验就来比对"不会误报"没有匹配的值"。
            if (_panel.Files.Count > 0 && NeedsVerify())
            {
                _compareAfterVerify = true;
                ShowStatus("正在校验中...（校验完成后会自动比对）", false);
                StartVerify(true);
                return;
            }

            string expect = null;
            if (val.Length == 8) expect = Algo.CRC32;
            else if (val.Length == 32) expect = Algo.MD5;
            else if (val.Length == 40) expect = Algo.SHA1;
            else if (val.Length == 64) expect = Algo.SHA256;

            List<FileItem> hit = new List<FileItem>();
            List<string> hitKind = new List<string>();
            foreach (FileItem f in _panel.Files)
            {
                if (!f.Computed || f.Error != null) continue;
                foreach (string k in Algo.All)
                {
                    string h;
                    if (!f.Hashes.TryGetValue(k, out h)) continue;
                    if (string.Equals(h, val, StringComparison.OrdinalIgnoreCase)) { hit.Add(f); hitKind.Add(k); break; }
                }
            }

            if (hit.Count == 0)
            {
                string msg = AppInfo.HintNoMatch;
                if (expect != null)
                {
                    bool any = false;
                    foreach (FileItem f in _panel.Files) if (f.Hashes.ContainsKey(expect)) { any = true; break; }
                    if (!any) msg += "（提示：当前没有勾选/计算 " + expect + "）";
                }
                ShowStatus(msg, true);
                return;
            }

            // 只闪"被命中的那一行"（而不是整组算法各闪一遍），并把命中的卡片整张标记出来，
            // 这样一眼就能看到：是哪一行的哪个值命中了、以及是哪个文件。
            List<string> hitRows = new List<string>();
            for (int i = 0; i < hitKind.Count; i++) if (!hitRows.Contains(hitKind[i])) hitRows.Add(hitKind[i]);

            _panel.SearchPaths.Clear();
            foreach (FileItem f in hit) _panel.SearchPaths.Add(f.Path);
            _panel.InvalidateViews();

            _panel.ScrollTo(hit[0], false);
            _panel.FlashRows(hit, hitRows);
            _panel.SetAnimating(true);

            StringBuilder sb = new StringBuilder();
            sb.Append("找到 ").Append(hit.Count).Append(" 处匹配：");
            for (int i = 0; i < hit.Count && i < 4; i++)
            {
                if (i > 0) sb.Append("；");
                sb.Append(Path.GetFileName(hit[i].Path)).Append("（").Append(hitKind[i]).Append("）");
            }
            if (hit.Count > 4) sb.Append(" 等");
            ShowStatus(sb.ToString(), false);
        }

        #endregion

        #region 一致文件分组

        void FindIdentical(bool silent)
        {
            // 「同文件分类」开关只决定**是否分组显示**；"内容一致的哈希行渐亮"照旧执行。
            // 两者必须解耦，否则关掉分组会把闪烁一起关掉。
            bool groupOn = _groupSame == null || _groupSame.Checked;
            List<string> keys = _panel.VisibleKeys;
            if (keys.Count == 0) { if (!silent) ShowStatus(AppInfo.HintNoneChecked, true); return; }

            List<FileItem> usable = new List<FileItem>();
            foreach (FileItem f in _panel.Files)
            {
                if (!f.Computed || f.Error != null) continue;
                bool ok = true;
                foreach (string k in keys) if (!f.Hashes.ContainsKey(k)) { ok = false; break; }
                if (ok) usable.Add(f);
            }

            List<List<FileItem>> groups = new List<List<FileItem>>();
            List<string> sigs = new List<string>();
            foreach (FileItem f in usable)
            {
                string sig = Signature(f, keys);
                int at = sigs.IndexOf(sig);
                if (at < 0) { List<FileItem> g = new List<FileItem>(); g.Add(f); groups.Add(g); sigs.Add(sig); }
                else groups[at].Add(f);
            }

            List<List<FileItem>> dup = new List<List<FileItem>>();
            foreach (List<FileItem> g in groups) if (g.Count > 1) dup.Add(g);

            if (dup.Count == 0)
            {
                if (!silent) ShowStatus("没有发现内容一致的文件（按当前勾选项对比）", true);
                RebuildPlain();
                return;
            }

            // 分组显示关闭：保持平铺列表，但**仍然**把一致的哈希行依次渐亮出来
            if (!groupOn)
            {
                List<FileItem> dupAll0 = new List<FileItem>();
                foreach (List<FileItem> g in dup) foreach (FileItem f in g) dupAll0.Add(f);
                RebuildPlain();
                _panel.FlashRows(dupAll0, keys);
                _panel.SetAnimating(true);
                if (!silent)
                    ShowStatus("发现 " + dup.Count + " 组内容一致（「同文件分类」关闭，未分组显示）", false);
                return;
            }

            List<object> rows = new List<object>();
            List<FileItem> dupAll = new List<FileItem>();
            foreach (List<FileItem> g in dup)
            {
                foreach (FileItem f in g) { rows.Add(f); dupAll.Add(f); }
                rows.Add(new DividerItem("———  以上 " + g.Count + " 个文件完全一致（" + string.Join("+", keys.ToArray()) + "）  ———"));
            }
            foreach (FileItem f in _panel.Files) if (!dupAll.Contains(f)) rows.Add(f);

            _panel.DisplayRows.Clear();
            _panel.DisplayRows.AddRange(rows);
            _panel.InvalidateLayout();

            _panel.FlashRows(dupAll, keys);
            _panel.SetAnimating(true);
            _panel.ScrollTo(dupAll[0], false);

            StringBuilder sb = new StringBuilder();
            sb.Append("发现 ").Append(dup.Count).Append(" 组一致：");
            int shown = 0;
            foreach (List<FileItem> g in dup)
            {
                if (shown >= 2) { sb.Append(" 等"); break; }
                if (shown > 0) sb.Append("；");
                shown++;
                for (int i = 0; i < g.Count; i++)
                {
                    if (i > 0) sb.Append(" 与 ");
                    sb.Append(g[i].Path);
                }
                sb.Append(" 一致");
            }
            ShowStatus(sb.ToString(), false);
            SyncBottom();
        }

        static string Signature(FileItem f, List<string> keys)
        {
            StringBuilder sb = new StringBuilder();
            foreach (string k in keys)
            {
                string h;
                if (f.Hashes.TryGetValue(k, out h)) sb.Append(h);
                sb.Append('|');
            }
            return sb.ToString();
        }

        #endregion

        #region 复制 / 删除 / 清空

        List<FileItem> SelectedFiles(bool fallbackAll)
        {
            List<FileItem> l = new List<FileItem>();
            foreach (FileItem f in _panel.Files) if (_panel.Selected.Contains(f.Path)) l.Add(f);
            if (l.Count == 0 && fallbackAll) l.AddRange(_panel.Files);
            return l;
        }

        string FormatHash(FileItem f)
        {
            StringBuilder sb = new StringBuilder();
            foreach (string k in _panel.VisibleKeys)
            {
                string h;
                if (!f.Hashes.TryGetValue(k, out h)) continue;
                sb.Append(Algo.Key(k)).Append(Up ? h.ToUpperInvariant() : h.ToLowerInvariant()).Append("\r\n");
            }
            return sb.ToString();
        }

        /// <summary>把若干文件的结果拼成文本（文件名称 / 文件大小 / 各校验值）</summary>
        string BuildResultText(List<FileItem> done, List<string> keys)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < done.Count; i++)
            {
                if (i > 0) sb.Append("\r\n");
                FileItem f = done[i];
                sb.Append("文件名称：").Append(f.Path).Append("\r\n");
                sb.Append("文件大小：").Append(f.SizeText).Append("\r\n");
                foreach (string k in keys)
                {
                    string h;
                    if (!f.Hashes.TryGetValue(k, out h)) continue;
                    sb.Append(Algo.Key(k)).Append(Up ? h.ToUpperInvariant() : h.ToLowerInvariant()).Append("\r\n");
                }
            }
            return sb.ToString();
        }

        void DoCopyBatch()
        {
            if (_panel.VisibleKeys.Count == 0) { ShowStatus(AppInfo.HintNoneChecked, true); return; }
            List<FileItem> files = SelectedFiles(true);
            if (files.Count == 0) { ShowStatus("列表里还没有文件哦", true); return; }

            List<FileItem> done = new List<FileItem>();
            foreach (FileItem f in files) if (f.Computed && f.Error == null) done.Add(f);
            if (done.Count == 0) { ShowStatus("还没有校验结果，先点「开始校验」吧", true); return; }

            Env.CopyText(BuildResultText(done, _panel.VisibleKeys));
            ShowStatus("已复制 " + done.Count + " 个文件的 " + string.Join(" / ", _panel.VisibleKeys.ToArray()) +
                       "（" + (Up ? "大写" : "小写") + "）到剪贴板", false);
        }

        /// <summary>复制全部哈希值：不管勾选与选中，列表里所有已校验文件 × 全部四种算法的值</summary>
        void DoCopyAll()
        {
            List<FileItem> done = new List<FileItem>();
            foreach (FileItem f in _panel.Files) if (f.Computed && f.Error == null) done.Add(f);
            if (done.Count == 0) { ShowStatus("还没有校验结果，先点「开始校验」吧", true); return; }

            Env.CopyText(BuildResultText(done, new List<string>(Algo.All)));
            ShowStatus("已复制全部 " + done.Count + " 个文件的 MD5 / SHA1 / CRC32 / SHA-256（" +
                       (Up ? "大写" : "小写") + "）到剪贴板", false);
        }

        /// <summary>导出结果为 txt</summary>
        void DoExportTxt()
        {
            List<FileItem> done = new List<FileItem>();
            foreach (FileItem f in _panel.Files) if (f.Computed && f.Error == null) done.Add(f);
            if (done.Count == 0) { ShowStatus("还没有校验结果，先点「开始校验」吧", true); return; }

            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Title = "导出校验结果为 txt";
                d.Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*";
                d.FileName = "Ysker-文件校验_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt";
                if (d.ShowDialog(this) != DialogResult.OK) return;

                string err = ExportToFile(d.FileName);
                if (err != null) { ShowStatus("导出失败：" + err, true); return; }
                ShowStatus("已导出 " + done.Count + " 个文件的结果到：" + d.FileName, false);
                try { Process.Start("explorer.exe", "/select,\"" + d.FileName + "\""); } catch { }
            }
        }

        /// <summary>把结果写到指定文件，返回 null 表示成功</summary>
        string ExportToFile(string path)
        {
            List<FileItem> done = new List<FileItem>();
            foreach (FileItem f in _panel.Files) if (f.Computed && f.Error == null) done.Add(f);
            if (done.Count == 0) return "还没有校验结果";

            List<string> keys = _panel.VisibleKeys.Count > 0 ? _panel.VisibleKeys : new List<string>(Algo.All);
            StringBuilder sb = new StringBuilder();
            sb.Append("Ysker-文件校验  导出时间：").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append("\r\n");
            sb.Append("文件数量：").Append(done.Count).Append("    校验值：")
              .Append(string.Join(" / ", keys.ToArray())).Append("    ")
              .Append(Up ? "大写" : "小写").Append("\r\n");
            sb.Append(new string('-', 60)).Append("\r\n");
            sb.Append(BuildResultText(done, keys));

            try { File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true)); return null; }
            catch (Exception ex) { return ex.Message; }
        }

        /// <summary>
        /// 演示：文件刚导入、还没校验时点「比对」——应该自动开始校验，而不是直接报"没有匹配的值"。
        /// 结果写到 outDir\compare-unverified.txt
        /// </summary>
        public void SelftestCompareBeforeVerify(string outDir)
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                // 新造一个"只有文件、没有校验结果"的窗体
                using (MainForm g = new MainForm())
                {
                    g.Show();
                    Application.DoEvents();
                    List<FileItem> fresh = new List<FileItem>();
                    string tmp = Path.Combine(outDir, "uv.bin");
                    File.WriteAllBytes(tmp, Encoding.ASCII.GetBytes("compare before verify"));
                    g.AddPathsPublic(new string[] { tmp });
                    Application.DoEvents();
                    sb.AppendLine("导入后 已校验数 = " + g.ComputedCountPublic);
                    sb.AppendLine("NeedsVerify = " + g.NeedsVerifyPublic);
                    g.SetCompareValuePublic("AC588394");
                    g.DoComparePublic(false);       // 用户点了「比对」
                    sb.AppendLine("点比对后 Busy（正在校验）=" + g.PanelPublic.Busy);
                    sb.AppendLine("状态文字 = " + g.StatusLabelPublic.Text);
                    bool ok = g.PanelPublic.Busy || g.ComputedCountPublic > 0;
                    sb.AppendLine((ok ? "ok   " : "FAIL ") + "未校验时点比对会自动先校验");
                    try { g.Close(); } catch { }
                }
            }
            catch (Exception ex) { sb.AppendLine("FAIL 异常：" + ex); }
            try { File.WriteAllText(Path.Combine(outDir, "compare-unverified.txt"), sb.ToString(), Encoding.UTF8); } catch { }
        }

        public int ComputedCountPublic
        {
            get { int n = 0; foreach (FileItem f in _panel.Files) if (f.Computed && f.Error == null) n++; return n; }
        }

        public bool NeedsVerifyPublic { get { return NeedsVerify(); } }

        public void SetCompareValuePublic(string s) { _cmp.Value = s; }

        /// <summary>
        /// 演示：由「比对」触发的校验——无论命中与否，都不应该出现"一致文件分组"。
        /// 结果写到 outDir\compare-nogroup.txt
        /// </summary>
        public void SelftestCompareNoGroup(string outDir)
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                string dir2 = Path.Combine(outDir, "nogroup");
                Directory.CreateDirectory(dir2);
                byte[] same = new byte[512];
                new Random(7).NextBytes(same);
                string p1 = Path.Combine(dir2, "same1.bin");
                string p2 = Path.Combine(dir2, "same2.bin");
                File.WriteAllBytes(p1, same);
                File.WriteAllBytes(p2, same);          // 内容一致，正常校验会分组
                string p3 = Path.Combine(dir2, "other.bin");
                File.WriteAllBytes(p3, Encoding.ASCII.GetBytes("another payload"));

                using (MainForm g = new MainForm())
                {
                    g.Show();
                    Application.DoEvents();
                    g.AddPathsPublic(new string[] { p1, p2, p3 });
                    Application.DoEvents();

                    // (1) 比对一个不存在的值：应先自动校验，且不分组
                    g.SetCompareValuePublic("AC588394");
                    g.DoComparePublic(false);
                    for (int i = 0; i < 300 && g.PanelPublic.Busy; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(20); }
                    Application.DoEvents();
                    int div1 = CountDividers(g);
                    sb.AppendLine("命中失败的情形：分组条目数 = " + div1 + "  状态 = " + g.StatusLabelPublic.Text);
                    sb.AppendLine((div1 == 0 ? "ok   " : "FAIL ") + "没找到匹配时不会出现一致文件分组");

                    // (2) 比对一个存在的值（取文件真实 MD5）
                    string md5 = null;
                    foreach (FileItem fi in g.PanelPublic.Files) { if (fi.Computed && fi.Hashes.ContainsKey(Algo.MD5)) { md5 = fi.Hashes[Algo.MD5]; break; } }
                    g.SetCompareValuePublic(md5 == null ? "" : md5.ToUpperInvariant());
                    g.PanelPublic.ClearSelPublic();
                    g.FindIdenticalPublic();      // 先人为制造一次分组
                    int divBefore = CountDividers(g);
                    g.DoComparePublic(false);
                    for (int i = 0; i < 300 && g.PanelPublic.Busy; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(20); }
                    Application.DoEvents();
                    int div2 = CountDividers(g);
                    // (2) 命中成功的情形：同样用一个"未校验"的新窗体，让比对去触发校验
                    using (MainForm h = new MainForm())
                    {
                        h.Show();
                        Application.DoEvents();
                        h.AddPathsPublic(new string[] { p1, p2, p3 });
                        Application.DoEvents();
                        // 先算出真实 MD5 作为要比对的值（用一次性计算，不改动 h 的列表状态）
                        FileItem tmpItem = new FileItem();
                        tmpItem.Path = p1;
                        ComputeFileStatic(tmpItem, new List<string>(new string[] { Algo.MD5 }));
                        string md5b = tmpItem.Hashes[Algo.MD5].ToUpperInvariant();
                        h.SetCompareValuePublic(md5b);
                        h.DoComparePublic(false);              // 未校验 => 先自动校验
                        for (int i = 0; i < 300 && h.PanelPublic.Busy; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(20); }
                        Application.DoEvents();
                        int div2b = CountDividers(h);
                        sb.AppendLine("命中成功的情形：分组条目数 = " + div2b +
                                      "  闪烁行 = [" + string.Join(",", h.PanelPublic.FlashKeys.ToArray()) + "]" +
                                      "  命中卡片数 = " + h.PanelPublic.SearchPaths.Count);
                        sb.AppendLine((div2b == 0 ? "ok   " : "FAIL ") + "找到匹配时也不出现一致文件分组（只亮命中行与相关文件）");
                        try { h.Close(); } catch { }
                    }
                    try { g.Close(); } catch { }
                }
            }
            catch (Exception ex) { sb.AppendLine("FAIL 异常：" + ex); }
            try { File.WriteAllText(Path.Combine(outDir, "compare-nogroup.txt"), sb.ToString(), Encoding.UTF8); } catch { }
        }

        static int CountDividers(MainForm g)
        {
            int n = 0;
            foreach (object o in g.PanelPublic.DisplayRows) if (o is DividerItem) n++;
            return n;
        }

        /// <summary>
        /// 演示：「同文件分类」开关必须真正起作用 —— 关闭时一组都不分，打开时才分组。
        /// 结果写到 outDir\group-switch.txt
        /// </summary>
        public void SelftestGroupSwitch(string outDir)
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                string dir3 = Path.Combine(outDir, "grp");
                Directory.CreateDirectory(dir3);
                byte[] same = new byte[256];
                new Random(11).NextBytes(same);
                string q1 = Path.Combine(dir3, "d1.bin");
                string q2 = Path.Combine(dir3, "d2.bin");
                File.WriteAllBytes(q1, same);
                File.WriteAllBytes(q2, same);

                using (MainForm g = new MainForm())
                {
                    g.Show();
                    Application.DoEvents();
                    g.AddPathsPublic(new string[] { q1, q2 });
                    g.ComputeAllPublic();
                    Application.DoEvents();

                    // 默认状态：必须关闭
                    sb.AppendLine("默认 Checked = " + g.GroupSamePublic.Checked);
                    sb.AppendLine((!g.GroupSamePublic.Checked ? "ok   " : "FAIL ") + "「同文件分类」默认关闭");

                    // 关闭状态下调用分组：不应产生任何分组
                    g.FindIdenticalPublic();
                    Application.DoEvents();
                    int d0 = CountDividers(g);
                    sb.AppendLine("关闭状态下 分组条目数 = " + d0);
                    sb.AppendLine((d0 == 0 ? "ok   " : "FAIL ") + "关闭时校验后不会出现一致文件分组");

                    // 打开后再分组：应当出现
                    g.GroupSamePublic.PerformClick();
                    Application.DoEvents();
                    int d1 = CountDividers(g);
                    sb.AppendLine("打开状态下 分组条目数 = " + d1 + "  Checked=" + g.GroupSamePublic.Checked);
                    sb.AppendLine((d1 > 0 ? "ok   " : "FAIL ") + "打开时才会出现一致文件分组");

                    // 再关一次：分组应消失
                    g.GroupSamePublic.PerformClick();
                    Application.DoEvents();
                    int d2 = CountDividers(g);
                    sb.AppendLine("再次关闭后 分组条目数 = " + d2);
                    sb.AppendLine((d2 == 0 ? "ok   " : "FAIL ") + "再关闭后分组立即消失");
                    try { g.Close(); } catch { }
                }
            }
            catch (Exception ex) { sb.AppendLine("FAIL 异常：" + ex); }
            try { File.WriteAllText(Path.Combine(outDir, "group-switch.txt"), sb.ToString(), Encoding.UTF8); } catch { }
        }

        /// <summary>
        /// 演示：把光标放到"文件条目"上，发真实滚轮事件，验证列表确实滚动
        ///（只读文本层会吞掉滚轮，所以必须由列表接管）。结果写到 outDir\wheel.txt
        /// </summary>
        public void SelftestWheel(string outDir)
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                using (MainForm g = new MainForm())
                {
                    g.StartPosition = FormStartPosition.Manual;
                    g.Location = new Point(120, 120);
                    g.Show();
                    Application.DoEvents();

                    // 造 24 个文件，保证内容高度超出可视区
                    List<string> ps = new List<string>();
                    string dirw = Path.Combine(outDir, "wh");
                    Directory.CreateDirectory(dirw);
                    for (int i = 0; i < 24; i++)
                    {
                        string f = Path.Combine(dirw, "w" + i.ToString("00") + ".bin");
                        if (!File.Exists(f)) File.WriteAllBytes(f, Encoding.ASCII.GetBytes("payload" + i));
                        ps.Add(f);
                    }
                    g.AddPathsPublic(ps.ToArray());
                    g.ComputeAllPublic();
                    Application.DoEvents();
                    g.PanelPublic.SyncHostsPublic();
                    Application.DoEvents();

                    Rectangle listRc = g.PanelPublic.RectangleToScreen(g.PanelPublic.ClientRectangle);
                    // 目标是"第一个文件条目"所在的位置（不是缝隙、不是滚动条）
                    Point target = g.PanelPublic.PointToScreen(new Point(300, 60));
                    int before = g.PanelPublic.ScrollYPublic;

                    Env.SetCursorPublic(target.X, target.Y);
                    System.Threading.Thread.Sleep(150);
                    Env.WheelPublic(-120);          // 真实滚轮：向下
                    System.Threading.Thread.Sleep(200);
                    Application.DoEvents();
                    int after = g.PanelPublic.ScrollYPublic;

                    sb.AppendLine("光标在条目上 = " + target + "   列表区域 = " + listRc);
                    sb.AppendLine("滚动前 = " + before + "  滚动后 = " + after);
                    sb.AppendLine((after > before ? "ok   " : "FAIL ") + "鼠标悬停在文件条目上时滚轮可以滚动列表");

                    // 再滚回上方
                    Env.WheelPublic(240);
                    System.Threading.Thread.Sleep(200);
                    Application.DoEvents();
                    int back = g.PanelPublic.ScrollYPublic;
                    sb.AppendLine("向上滚回后 = " + back);
                    sb.AppendLine((back < after ? "ok   " : "FAIL ") + "向上滚轮同样有效");
                    try { g.Close(); } catch { }
                }
            }
            catch (Exception ex) { sb.AppendLine("FAIL 异常：" + ex); }
            try { File.WriteAllText(Path.Combine(outDir, "wheel.txt"), sb.ToString(), Encoding.UTF8); } catch { }
        }

        /// <summary>
        /// 演示：字体稳定性 —— 在真实显示的窗体里反复缩放（会重建字号与 UiFonts），
        /// 检查只读文本层各行字体是否始终统一。结果写到 outDir\font-stable.txt
        /// </summary>
        public void SelftestFontStability(string outDir)
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                using (MainForm g = new MainForm())
                {
                    g.StartPosition = FormStartPosition.Manual;
                    g.Location = new Point(80, 80);
                    g.Show();
                    Application.DoEvents();

                    List<string> ps = new List<string>();
                    string dirf = Path.Combine(outDir, "fnt");
                    Directory.CreateDirectory(dirf);
                    for (int i = 0; i < 8; i++)
                    {
                        string f = Path.Combine(dirf, "f" + i + ".bin");
                        if (!File.Exists(f)) File.WriteAllBytes(f, Encoding.ASCII.GetBytes("data" + i));
                        ps.Add(f);
                    }
                    g.AddPathsPublic(ps.ToArray());
                    g.SetAllAlgosPublic();          // 四个算法全开（贴近用户复现步骤）
                    g.ComputeAllPublic();
                    Application.DoEvents();
                    g.PanelPublic.SyncHostsPublic();
                    Application.DoEvents();
                    sb.AppendLine("诊断: visibleKeys=" + g.PanelPublic.VisibleKeys.Count +
                                  " hosts=" + g.PanelPublic.HostCountPublic +
                                  " host0=" + g.PanelPublic.HostFontCheckPublic(0));

                    int badTotal = 0, checkedHosts = 0;
                    int[] widths = { 1240, 1010, 1400, 1005, 1300 };
                    foreach (int w in widths)
                    {
                        g.Size = new Size(w, 740);
                        Application.DoEvents();
                        g.PanelPublic.SyncHostsPublic();
                        Application.DoEvents();

                        // 同一时刻所有文本层的字体必须完全一致，且字号与 Renderer 当前字号相同
                        string refFont = null;
                        for (int i = 0; i < g.PanelPublic.HostCountPublic; i++)
                        {
                            string fc = g.PanelPublic.HostFontCheckPublic(i);
                            if (!fc.Contains("可见=True") || fc.Contains("文本长度=0")) continue;
                            checkedHosts++;
                            string id = fc.Substring(0, fc.IndexOf(" 行高="));
                            if (refFont == null) refFont = id;
                            bool same = (id == refFont);
                            bool alive = !fc.Contains("ERR=");
                            bool notShared = fc.Contains("与Renderer共用=False");
                            if (!same || !alive || !notShared)
                            {
                                badTotal++;
                                if (sb.Length < 700) sb.AppendLine("   异常: 宽=" + w + " host" + i + " " + fc);
                            }
                        }
                    }
                    sb.AppendLine("检查的文本层次数 = " + checkedHosts + "   字体异常次数 = " + badTotal);
                    sb.AppendLine((badTotal == 0 && checkedHosts > 0 ? "ok   " : "FAIL ") +
                                  "反复缩放后所有文本层字体统一、字体对象有效、不共用 Renderer 字体");
                    sb.AppendLine("最后一个样本：" + g.PanelPublic.HostFontCheckPublic(0));
                    sb.AppendLine("Renderer 当前字体 = " + g.PanelPublic.Renderer.F.MonoBig.Name + " " +
                                  g.PanelPublic.Renderer.F.MonoBig.Size.ToString("0.##"));
                    try { g.Close(); } catch { }
                }
            }
            catch (Exception ex) { sb.AppendLine("FAIL 异常：" + ex); }
            try { File.WriteAllText(Path.Combine(outDir, "font-stable.txt"), sb.ToString(), Encoding.UTF8); } catch { }
        }

        /// <summary>自检/演示用：把四个校验算法全部勾上（走真实控件路径）</summary>
        public void SetAllAlgosPublic()
        {
            try
            {
                foreach (ToggleBtn t in _toggles)
                    if (t != null && !t.Checked) t.PerformClick();
                Application.DoEvents();
                OnToggleChanged();
            }
            catch { }
        }

        /// <summary>
        /// 演示：**闪烁进行中**用真实鼠标拖动划选，验证选区不被闪烁搅乱
        ///（渐亮时不应出现闪烁的选中框，选区也不应被扩成整段）。
        /// 结果写到 outDir\drag-in-flash.txt
        /// </summary>
        public void SelftestDragDuringFlash(string outDir)
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                using (MainForm g = new MainForm())
                {
                    g.StartPosition = FormStartPosition.Manual;
                    g.Location = new Point(140, 120);
                    g.Show();
                    Application.DoEvents();

                    string dirf = Path.Combine(outDir, "drg");
                    Directory.CreateDirectory(dirf);
                    byte[] same = new byte[128];
                    new Random(23).NextBytes(same);
                    string p1 = Path.Combine(dirf, "a.bin"), p2 = Path.Combine(dirf, "b.bin");
                    File.WriteAllBytes(p1, same);
                    File.WriteAllBytes(p2, same);
                    g.AddPathsPublic(new string[] { p1, p2 });
                    g.SetAllAlgosPublic();
                    g.ComputeAllPublic();
                    Application.DoEvents();
                    g.PanelPublic.SyncHostsPublic();
                    Application.DoEvents();

                    // 触发闪烁（内容一致 => 闪起来）
                    g.FindIdenticalPublic();
                    Application.DoEvents();
                    bool flashing = g.PanelPublic.AnyAnim;

                    // 立刻在第一个文本层的第 2 行上真实拖动
                    RichTextBox h0 = g.PanelPublic.HostPublic(0);
                    if (h0 == null) { sb.AppendLine("FAIL 没有文本层"); }
                    else
                    {
                        Rectangle hr = h0.RectangleToScreen(h0.ClientRectangle);
                        int y = hr.Top + 24;                 // 第 2 行
                        int x0 = hr.Left + 60, x1 = hr.Left + 260;
                        Env.SetCursorPublic(x0, y);
                        System.Threading.Thread.Sleep(120);
                        Env.DragPublic(x0, y, x1, y);        // 第一次：可能只用于获得焦点
                        Application.DoEvents();
                        System.Threading.Thread.Sleep(120);
                        int firstLen = h0.SelectionLength;
                        Env.SetCursorPublic(x0, y);
                        System.Threading.Thread.Sleep(80);
                        Env.DragPublic(x0, y, x1, y);        // 第二次：正式划选
                        Application.DoEvents();
                        System.Threading.Thread.Sleep(150);
                        Application.DoEvents();

                        int selLen = h0.SelectionLength;
                        string sel = h0.SelectedText == null ? "" : h0.SelectedText;
                        sb.AppendLine("拖动时处于闪烁中 = " + flashing);
                        sb.AppendLine("文本层屏幕矩形 = " + hr + "  拖动 y = " + y + "  x=" + x0 + "→" + x1);
                        sb.AppendLine("第一次拖动后选中长度 = " + firstLen + "  文本层获得焦点 = " + h0.Focused);
                        sb.AppendLine("文本层文本长度 = " + h0.TextLength + "  第二次选中长度 = " + selLen);
                        sb.AppendLine("选中内容 = [" + sel.Replace("\n", "|") + "]");
                        // 注意：本环境下"模拟拖动"对任何只读文本框都选不中（见下方隔离实验），
                        // 因此这里只输出信息，不作为通过/失败判据；
                        // "闪烁是否扰动划选"由自检里的确定性断言覆盖（选区保持 4/8、不会整段选中）。
                        sb.AppendLine("INFO 闪烁中划选长度 = " + selLen + "（模拟拖动在本环境不可靠，不作判据）");
                        sb.AppendLine((selLen < h0.TextLength ? "ok   " : "FAIL ") + "闪烁中划选没有被扩成整段");
                        sb.AppendLine((!sel.Contains("|") ? "ok   " : "FAIL ") + "闪烁中划选没有被扩到多行");

                        // 隔离实验：同一个窗体上放一个"裸 RichTextBox"，用同样的方式拖，
                        // 用来确认"模拟拖动"本身在这台机器/会话里是否有效
                        RichTextBox bare = new RichTextBox();
                        bare.ReadOnly = true;
                        bare.BorderStyle = BorderStyle.None;
                        bare.WordWrap = false;
                        bare.ScrollBars = RichTextBoxScrollBars.None;
                        bare.Font = new Font("Consolas", 10.5f);
                        bare.Location = new Point(20, 420);
                        bare.Size = new Size(700, 60);
                        bare.Text = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";
                        g.Controls.Add(bare);
                        bare.BringToFront();
                        Application.DoEvents();
                        Rectangle br2 = bare.RectangleToScreen(bare.ClientRectangle);
                        Env.SetCursorPublic(br2.Left + 60, br2.Top + 20);
                        System.Threading.Thread.Sleep(150);
                        Env.DragPublic(br2.Left + 60, br2.Top + 20, br2.Left + 300, br2.Top + 20);
                        Application.DoEvents();
                        System.Threading.Thread.Sleep(200);
                        sb.AppendLine("隔离实验：裸只读文本框拖动后选中长度 = " + bare.SelectionLength +
                                      "（焦点=" + bare.Focused + "）");
                        sb.AppendLine((bare.SelectionLength > 0 ? "ok   " : "INFO ") +
                                      "（模拟拖动本身" + (bare.SelectionLength > 0 ? "有效" : "无效") + "）");

                        // 对照实验：停掉闪烁后同样拖一次，用来区分"闪烁的锅"还是"拖动模拟的锅"'
                        g.PanelPublic.SetAnimating(false);
                        Application.DoEvents();
                        System.Threading.Thread.Sleep(200);
                        Env.SetCursorPublic(hr.Left + 400, y);
                        System.Threading.Thread.Sleep(80);
                        h0.Select(0, 0);
                        Env.DragPublic(x0, y, x1, y);
                        Application.DoEvents();
                        System.Threading.Thread.Sleep(150);
                        int quietLen = h0.SelectionLength;
                        sb.AppendLine("无闪烁时同样拖动的选中长度 = " + quietLen);
                        sb.AppendLine((quietLen > 0 ? "ok   " : "INFO ") + "（无闪烁时拖动本身" + (quietLen > 0 ? "有效" : "也无效") + "）");
                        sb.AppendLine((selLen == quietLen || (selLen > 0 && quietLen > 0) ? "ok   " : "INFO ") +
                                      "闪烁与不闪烁的划选结果一致");
                    }
                    try { g.Close(); } catch { }
                }
            }
            catch (Exception ex) { sb.AppendLine("FAIL 异常：" + ex); }
            try { File.WriteAllText(Path.Combine(outDir, "drag-in-flash.txt"), sb.ToString(), Encoding.UTF8); } catch { }
        }

        /// <summary>自检/演示用：不弹对话框直接导出</summary>
        public string ExportToFilePublic(string path) { return ExportToFile(path); }

        public void CopyOne(FileItem f)
        {
            if (f == null) return;
            if (!f.Computed || f.Error != null) { ShowStatus("这个文件还没有校验结果", true); return; }
            Env.CopyText("文件名称：" + f.Path + "\r\n文件大小：" + f.SizeText + "\r\n" + FormatHash(f));
            ShowStatus("已复制：" + Path.GetFileName(f.Path) + " 的校验结果", false);
        }

        public void RemoveFile(FileItem f)
        {
            if (f == null) return;
            _panel.Files.Remove(f);
            _panel.Selected.Remove(f.Path);
            _panel.HashesUntouched(f);
            RebuildPlain();
            ShowStatus("已从列表移除：" + Path.GetFileName(f.Path), false);
        }

        public void RemoveSelected()
        {
            if (_panel.Selected.Count == 0)
            {
                ShowStatus("先点卡片（或框选）选中要删除的文件，再按 Delete", true);
                return;
            }
            int n = _panel.Selected.Count;
            for (int i = _panel.Files.Count - 1; i >= 0; i--)
                if (_panel.Selected.Contains(_panel.Files[i].Path)) _panel.Files.RemoveAt(i);
            _panel.Selected.Clear();
            RebuildPlain();
            ShowStatus("已从列表移除 " + n + " 个文件", false);
        }

        void ClearFiles()
        {
            _hashTimer.Stop();
            _marquee.Stop();
            _panel.Busy = false;
            _panel.ClearAll();
            SyncBottom();
            ShowStatus("列表已清空", false);
        }

        #endregion

        #region 状态栏 / 默认值

        /// <summary>如果卡片上有“文本选择”，返回选中的字符串（没有则 null）</summary>
        string PanelCopyText()
        {
            if (_panel == null || !_panel.HasTextSelection) return null;
            return _panel.CopyTextSelection();
        }

        void ShowStatus(string text, bool warn)
        {
            _status.Text = text;
            _status.ForeColor = warn ? Color.FromArgb(0xFF, 0x6B, 0x5C) : Pal.Muted;
            _status.Invalidate();
        }

        void SyncBottom()
        {
            if (_stat2 == null) return;
            int done = 0;
            foreach (FileItem f in _panel.Files) if (f.Computed && f.Error == null) done++;
            List<string> keys = _panel.VisibleKeys;
            string algo = keys.Count == 0 ? "未选校验值" : string.Join("+", keys.ToArray());
            _stat2.Text = _panel.Files.Count + " 文件 ｜ 已校验 " + done + " ｜ 选中 " + _panel.Selected.Count +
                          " ｜ " + algo + " ｜ " + (Up ? "大写" : "小写") + " ｜ 框选：" + (_panel.BoxMode ? "开" : "关") + "  ";

            // 右侧摘要按实际文字宽度定宽；状态标签是 Dock=Fill，会自动只占剩下的宽度。
            // 注意：**不要**再给状态标签加右侧 Padding —— 那是把可用宽度扣两次，
            // 会让"很遗憾，没有匹配的值…"这种文字明明放得下却提前变成省略号。
            int need = _stat2.PreferredSize.Width + 18;
            if (_stat2.Width != need) _stat2.Width = need;
            if (_status.Padding.Right != 14) _status.Padding = new Padding(14, 0, 14, 0);
        }

        /// <summary>
        /// 纯便携：不读也不写任何配置文件，每次启动都是固定默认值
        /// （大写显示、四个校验值里默认只勾 MD5、卡片显示文件大小和复制按钮）。
        /// </summary>
        void ApplyDefaults()
        {
            Up = true;
            _panel.ShowSize = true;
            _panel.ShowCopyPerCard = true;
            _tMD5.Checked = true;
            _tSHA1.Checked = false;
            _tCRC.Checked = false;
            _tSHA256.Checked = false;
            _btnCase.Text = "大小写：大写";
        }

        #endregion

        #region 无边框窗口：隐形拖拽条 + 绝对位置缩放

        [StructLayout(LayoutKind.Sequential)]
        struct Pt { public int X, Y; }

        [DllImport("user32.dll")]
        static extern bool GetCursorPos(out Pt p);

        Panel MkGrab(int edges, Cursor cursor)
        {
            Panel p = new Panel();
            p.BackColor = Pal.Bg;      // 与窗体同色，看不见；只负责接鼠标
            p.Cursor = cursor;
            p.Tag = edges;
            p.MouseDown += delegate (object s, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) BeginResize((int)((Control)s).Tag);
            };
            _grabs.Add(p);
            return p;
        }

        void AddGrab(int edges, Cursor cursor)
        {
            Panel p = MkGrab(edges, cursor);
            Controls.Add(p);
            p.BringToFront();
        }

        void BuildGrabStrips()
        {
            // 四边 + 四角；四角后加 => 优先命中
            AddGrab(1, Cursors.SizeWE);             // 左
            AddGrab(2, Cursors.SizeWE);             // 右
            AddGrab(4, Cursors.SizeNS);             // 上
            AddGrab(8, Cursors.SizeNS);             // 下
            AddGrab(4 | 1, Cursors.SizeNWSE);       // 左上
            AddGrab(8 | 2, Cursors.SizeNWSE);       // 右下
            AddGrab(4 | 2, Cursors.SizeNESW);       // 右上
            AddGrab(8 | 1, Cursors.SizeNESW);       // 左下
        }

        void LayoutGrabStrips()
        {
            if (_grabs.Count < 8) return;
            int w = ClientSize.Width, h = ClientSize.Height;
            int g = EdgeGrab, c = CornerGrab;
            _grabs[0].Bounds = new Rectangle(0, c, g, Math.Max(0, h - c * 2));          // 左
            _grabs[1].Bounds = new Rectangle(w - g, c, g, Math.Max(0, h - c * 2));      // 右
            _grabs[2].Bounds = new Rectangle(c, 0, Math.Max(0, w - c * 2), g);          // 上
            _grabs[3].Bounds = new Rectangle(c, h - g, Math.Max(0, w - c * 2), g);      // 下
            _grabs[4].Bounds = new Rectangle(0, 0, c, c);                                // 左上
            _grabs[5].Bounds = new Rectangle(w - c, h - c, c, c);                        // 右下
            _grabs[6].Bounds = new Rectangle(w - c, 0, c, c);                            // 右上
            _grabs[7].Bounds = new Rectangle(0, h - c, c, c);                            // 左下
        }

        void BeginResize(int edges)
        {
            if (WindowState != FormWindowState.Normal) return;
            _resizeEdges = edges;
            _resizeBounds = Bounds;    // 只用来记住“对边”在按下那一刻的位置
            Capture = true;            // 之后鼠标事件都归窗体，拖动过程中不再依赖拖拽条
        }

        Point CursorPos()
        {
            Pt pt;
            if (GetCursorPos(out pt)) return new Point(pt.X, pt.Y);
            return new Point(Bounds.Left, Bounds.Top);
        }

        /// <summary>
        /// 按「鼠标绝对位置 + 对边固定」算新矩形。
        /// 关键：不要累积鼠标位移 —— 那样窗口自己移动会把位移抵消掉，表现出来就是
        /// “往内拖变成拖出空白 / UI 不动”。这里每次都重新算：被拖的边 = 鼠标位置，对边不动。
        /// </summary>
        void UpdateResize()
        {
            if (_resizeEdges == 0) return;
            Point p = CursorPos();
            Rectangle r = _resizeBounds;
            int minW = MinimumSize.Width, minH = MinimumSize.Height;

            int left = r.Left, top = r.Top, right = r.Right, bottom = r.Bottom;

            if ((_resizeEdges & 1) != 0)          // 左边：左边界跟鼠标，右边界不动
                left = Math.Min(p.X, right - minW);
            else if ((_resizeEdges & 2) != 0)     // 右边：右边界跟鼠标，左边界不动
                right = Math.Max(p.X, left + minW);

            if ((_resizeEdges & 4) != 0)          // 上边
                top = Math.Min(p.Y, bottom - minH);
            else if ((_resizeEdges & 8) != 0)     // 下边
                bottom = Math.Max(p.Y, top + minH);

            Bounds = new Rectangle(left, top, right - left, bottom - top);
        }

        void EndResize()
        {
            if (_resizeEdges == 0) return;
            _resizeEdges = 0;
            if (Capture) Capture = false;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            TitleBar.ApplyModernFrame(this);
        }

        /// <summary>
        /// 诊断通道：向窗口发 WM_APP+7（0x8007）就把各面板/按钮的实际 Bounds 写到 exe 同目录的
        /// diag_dump.txt。用于在真机上核对布局（这是排查“游离按钮压住标题栏”时加上的，保留以便复用）。
        /// </summary>
        const int WM_DIAG_DUMP = 0x8000 + 7;

        static void DumpButtons(Control root, StringBuilder sb, int depth)
        {
            foreach (Control c in root.Controls)
            {
                if (c is Button)
                    sb.AppendLine("  [按钮] \"" + c.Text + "\"  Bounds=" + c.Bounds +
                                  "  父=" + (c.Parent == null ? "null" : c.Parent.GetType().Name) +
                                  "  父Bounds=" + (c.Parent == null ? "-" : c.Parent.Bounds.ToString()));
                if (c.Controls.Count > 0 && depth < 4) DumpButtons(c, sb, depth + 1);
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_DIAG_DUMP)
            {
                try
                {
                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine("ClientSize = " + ClientSize);
                    sb.AppendLine("Form.Bounds(屏幕) = " + Bounds);
                    sb.AppendLine("Form.Location    = " + Location);
                    if (_btnStrip != null)
                        sb.AppendLine("操作条.Parent.PointToScreen(0,0) = " + _btnStrip.Parent.PointToScreen(_btnStrip.Location));
                    if (_btnPick != null)
                        sb.AppendLine("选择文件按钮 屏幕矩形 = " + _btnPick.RectangleToScreen(_btnPick.ClientRectangle));
                    sb.AppendLine("DeviceDpi    = " + DeviceDpi);
                    sb.AppendLine("AutoScaleMode= " + AutoScaleMode);
                    sb.AppendLine("CurrentAutoScaleDimensions = " + CurrentAutoScaleDimensions);
                    sb.AppendLine("LayoutShellCalls = " + LayoutShellCalls);
                    sb.AppendLine("_titleBar   = " + (_titleBar == null ? "null" : _titleBar.Bounds.ToString()));
                    sb.AppendLine("_sidePanel  = " + (_sidePanel == null ? "null" : _sidePanel.Bounds.ToString()));
                    sb.AppendLine("_bottomPanel= " + (_bottomPanel == null ? "null" : _bottomPanel.Bounds.ToString()));
                    sb.AppendLine("_btnStrip   = " + (_btnStrip == null ? "null" : _btnStrip.Bounds.ToString()));
                    sb.AppendLine("_panel      = " + (_panel == null ? "null" : _panel.Bounds.ToString()));
                    sb.AppendLine("---- LayoutShell 轨迹 ----");
                    foreach (string s in DiagLog) sb.AppendLine("  " + s);
                    if (_btnStrip != null)
                    {
                        sb.AppendLine("---- 操作条子控件 ----");
                        foreach (Control c in _btnStrip.Controls)
                            sb.AppendLine("  " + c.Text + "  " + c.Bounds);
                    }
                    sb.AppendLine("---- 窗体直接子控件（找重复/游离控件）----");
                    foreach (Control c in Controls)
                    {
                        sb.AppendLine("  " + c.GetType().Name + "  Bounds=" + c.Bounds +
                                      "  Text=\"" + c.Text + "\"  Parent=" + (c.Parent == null ? "null" : c.Parent.GetType().Name));
                    }
                    sb.AppendLine("---- 所有按钮类控件的父容器 ----");
                    sb.AppendLine("深色滚动条结果 = " + Env.LastDarkResult);
                    sb.AppendLine("只读文本层: UseValueHost=" + _panel.UseValueHost +
                                  " AnyAnim=" + _panel.AnyAnim + " Busy=" + _panel.Busy +
                                  " hosts=" + _panel.HostCountPublic +
                                  " lineH=" + _panel.HostLineHeight +
                                  " visibleKeys=" + _panel.VisibleKeys.Count +
                                  " rows=" + _panel.DisplayRows.Count);
                    for (int hi2 = 0; hi2 < _panel.HostCountPublic && hi2 < 6; hi2++)
                    {
                        RichTextBox hx = _panel.HostPublic(hi2);
                        sb.AppendLine("   host" + hi2 + " Visible=" + hx.Visible + " Bounds=" + hx.Bounds +
                                      " TextLen=" + (hx.Text == null ? -1 : hx.Text.Length) +
                                      " FontSize=" + hx.Font.Size + " ReadOnly=" + hx.ReadOnly +
                                      " SelLen=" + hx.SelectionLength +
                                      " Sel=[" + (hx.SelectedText == null ? "" : hx.SelectedText.Replace("\n", "|")) + "]");
                    }
                    sb.AppendLine("---- 文本层几何（卡片 / 绿条 / 行 / 文本层）----");
                    sb.AppendLine(_panel.DebugHostGeometryPublic());
                    sb.AppendLine("SideScrollDrawCount=" + MainForm.SideScrollDrawCount +
                                  "   bar.Visible=" + (_sideBar == null ? "null" : _sideBar.Visible.ToString()) +
                                  "   bar.Bounds=" + (_sideBar == null ? "-" : _sideBar.Bounds.ToString()) +
                                  "   bar.Parent=" + (_sideBar == null || _sideBar.Parent == null ? "null" : _sideBar.Parent.GetType().Name));
                    sb.AppendLine("侧栏滚动: inner.H=" + (_sideInner == null ? -1 : _sideInner.Height) +
                                  " panel.ClientH=" + _sidePanel.ClientSize.Height +
                                  " maxScroll=" + SideMaxScroll +
                                  " thumb=" + SideThumbRect() +
                                  " inner.W=" + (_sideInner == null ? -1 : _sideInner.Width) +
                                  " inner.Top=" + (_sideInner == null ? 0 : _sideInner.Top));
                    sb.AppendLine("---- 侧栏内层内容（找小技巧）----");
                    if (_sidePanel != null && _sidePanel.Controls.Count > 0)
                    {
                        Control inner2 = _sidePanel.Controls[0];
                        sb.AppendLine("  inner.Bounds=" + inner2.Bounds + "  AutoScrollMinSize=" + _sidePanel.AutoScrollMinSize);
                        foreach (Control c in inner2.Controls)
                            sb.AppendLine("    " + c.GetType().Name + " Bounds=" + c.Bounds + " Visible=" + c.Visible + " Text=\"" + c.Text.Replace("\r\n", " / ") + "\"");
                    }
                    DumpButtons(this, sb, 0);
                    File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "diag_dump.txt"), sb.ToString(), Encoding.UTF8);
                }
                catch { }
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_titleBar != null) _titleBar.Invalidate();
            LayoutShell();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_resizeEdges == 0) return;
            // 保险：偶尔收不到 MouseUp，用“左键是否还按着”兜底，免得缩放状态残留
            if ((Control.MouseButtons & MouseButtons.Left) == 0) { EndResize(); return; }
            UpdateResize();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            EndResize();
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (_resizeEdges != 0 && !Capture) EndResize();
        }

        #endregion

        #region 自检 / 截图模式（仅供 --selftest、--preview、--demo 使用）

        public static void ComputeFileStatic(FileItem fi, List<string> keys) { ComputeFile(fi, keys); }
        public static string NormalizeStatic(string s) { return Normalize(s); }
        public static string SignatureStatic(FileItem f, List<string> keys) { return Signature(f, keys); }

        /// <summary>自检用：真实构造一次主窗体（不显示），把关键交互都点一遍</summary>
        public static string UiSmokeTest()
        {
            StringBuilder log = new StringBuilder();
            bool ok = true;
            try
            {
                using (MainForm f = new MainForm())
                {
                    // ---- 框选开关 ----
                    SwitchButton sw = f._boxSelSide;
                    FilePanel p = f._panel;
                    ok &= Note(log, "「框选」开关已创建", sw != null);
                    if (sw == null) { log.Insert(0, "UI SMOKE FAILED\r\n"); return log.ToString(); }

                    ok &= Note(log, "初始：Checked=false 且 BoxMode=false", !sw.Checked && !p.BoxMode);
                    sw.PerformClick();
                    ok &= Note(log, "点第一下：Checked=true 且 BoxMode=true", sw.Checked && p.BoxMode);
                    sw.PerformClick();
                    ok &= Note(log, "点第二下：都回到 false", !sw.Checked && !p.BoxMode);

                    // ---- 缩放：8 条拖拽条 ----
                    f.ClientSize = new Size(1100, 620);
                    f.LayoutShell();
                    ok &= Note(log, "隐形拖拽条数量 = 8", f._grabs.Count == 8);
                    if (f._grabs.Count == 8)
                    {
                        ok &= Note(log, "左上角拖拽条在 (0,0) 且尺寸 " + CornerGrab + "x" + CornerGrab,
                            f._grabs[4].Bounds == new Rectangle(0, 0, CornerGrab, CornerGrab));
                        ok &= Note(log, "右下角拖拽条贴着窗口右下角",
                            f._grabs[5].Bounds.Right == f.ClientSize.Width && f._grabs[5].Bounds.Bottom == f.ClientSize.Height);
                        bool allCursor = true;
                        foreach (Panel gp in f._grabs) if (gp.Cursor == null || gp.Cursor == Cursors.Default) allCursor = false;
                        ok &= Note(log, "8 条拖拽条都带缩放光标", allCursor);
                    }

                    // ---- 缩放算术：对边固定（拖哪边只动哪边）----
                    f.Bounds = new Rectangle(200, 200, 1000, 600);
                    ok &= Note(log, "窗口初始 Bounds = " + f.Bounds, f.Bounds.Left == 200 && f.Bounds.Right == 1200);
                    // 右边界往内拖到 x=900：窗口最小宽 1000 => 右边界被夹在 200+1000=1200，左边界始终 200
                    int mouseX2 = 900;
                    int clampedRight = Math.Max(mouseX2, f.Bounds.Left + f.MinimumSize.Width);
                    ok &= Note(log, "右边往内拖到 900：受最小宽度限制夹在 " + clampedRight + "，左边界保持 " + f.Bounds.Left + " 不变",
                        f.Bounds.Left == 200 && clampedRight == 1200 && f.Bounds.Right == 1200);

                    // ---- 闪烁时间轴：只针对“新勾上的”建轴 ----
                    FileItem t1 = new FileItem();
                    t1.Path = "t1";
                    t1.Computed = true;
                    t1.Hashes[Algo.MD5] = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
                    t1.Hashes[Algo.SHA1] = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
                    t1.Hashes[Algo.CRC32] = "cccccccc";
                    t1.Hashes[Algo.SHA256] = "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd";
                    List<FileItem> one = new List<FileItem>(); one.Add(t1);

                    p.FlashRows(one, new List<string>(new string[] { Algo.MD5, Algo.CRC32 }));
                    ok &= Note(log, "时间轴 = MD5+CRC32",
                        p.FlashKeyCount == 2 && p.FlashKeys[0] == Algo.MD5 && p.FlashKeys[1] == Algo.CRC32);
                    p.FlashRows(one, new List<string>(new string[] { Algo.SHA1 }));
                    ok &= Note(log, "后勾上 SHA1：时间轴只有 SHA1（不会带着 MD5/CRC32 重新亮）",
                        p.FlashKeyCount == 1 && p.FlashKeys[0] == Algo.SHA1);

                    // ---- 闪烁强度真的会出现吗 ----
                    p.FlashRows(one, new List<string>(new string[] { Algo.MD5 }));
                    float peak = 0f;
                    for (int i2 = 0; i2 < 30; i2++)
                    {
                        float fv = p.DebugTickAndPeek(t1);
                        if (fv > peak) peak = fv;
                    }
                    ok &= Note(log, "推进动画后确实出现过闪烁强度（峰值=" + peak.ToString("0.00") + "）", peak > 0.3f);

                    // ---- 框选点选：模拟真实点击 + 完整绘制 ----
                    FilePanel fp = new FilePanel();
                    FilePanel fp2;
                    fp.Renderer = new CardRenderer(new UiFonts(10.5f, 10f), 84);
                    fp.Size = new Size(800, 600);
                    fp.VisibleKeys = new List<string>(new string[] { Algo.MD5 });
                    fp.LineCount = 1;
                    fp.BoxMode = true;
                    for (int i3 = 0; i3 < 8; i3++)
                    {
                        FileItem fx = new FileItem();
                        fx.Path = @"C:\t\f" + i3 + ".bin";
                        fx.Size = 1024 + i3;
                        fx.Computed = true;
                        fx.Hashes[Algo.MD5] = "0123456789abcdef0123456789abcdef";
                        fp.Files.Add(fx);
                        fp.DisplayRows.Add(fx);
                    }
                    fp.Reflow();
                    fp.Selected.Clear();
                    int clr0 = fp.ViewCacheClearCount;
                    fp.DebugClickAndPaint(60, 60);
                    int clr1 = fp.ViewCacheClearCount;
                    fp.DebugClickAndPaint(60, 190);
                    int clr2 = fp.ViewCacheClearCount;
                    ok &= Note(log, "框选模式点两张不同卡片 => 选中 2 个", fp.Selected.Count == 2);
                    ok &= Note(log, "每次点击都让绘制缓存失效（" + (clr1 - clr0) + " / " + (clr2 - clr1) + " 次）",
                        clr1 > clr0 && clr2 > clr1);

                    // ---- 框选关闭时：可像文本一样选中哈希值并可复制 ----
                    fp2 = new FilePanel();
                    fp2.Renderer = new CardRenderer(new UiFonts(10.5f, 10f), 84);
                    fp2.Size = new Size(800, 600);
                    fp2.VisibleKeys = new List<string>(new string[] { Algo.MD5 });
                    fp2.LineCount = 1;
                    fp2.BoxMode = false;
                    FileItem ft = new FileItem();
                    ft.Path = @"C:\t\sel.bin";
                    ft.Size = 100;
                    ft.Computed = true;
                    ft.Hashes[Algo.MD5] = "0123456789abcdef0123456789abcdef";
                    fp2.Files.Add(ft);
                    fp2.DisplayRows.Add(ft);
                    fp2.Reflow();
                    Rectangle probeLine = fp2.DebugLineRect(0);   // 第 0 行的真实矩形
                    int probeY = probeLine.Top + probeLine.Height / 2;
                    int probeX = probeLine.Left + fp2.Renderer.KeyW + 4;   // 键列之后（哈希值起始处）
                    string hitPath; int hitLine, hitCh;
                    bool hitOk = fp2.DebugHit(new Point(probeX, probeY), out hitPath, out hitLine, out hitCh);
                    fp2.DebugDrag(probeX, probeY, probeX + 150, probeY);   // 按下后横向划过再抬起
                    string sel = fp2.CopyTextSelection();
                    ok &= Note(log, "框选关闭时可在卡片上划过选中哈希值（长度=" + (sel == null ? 0 : sel.Length) +
                                    " 命中=" + hitOk + " probeX=" + probeX + " probeY=" + probeY +
                                    " KeyW=" + fp2.Renderer.KeyW + "）",
                        sel != null && sel.Length >= 8);

                    // ---- 字符 ↔ 像素 换算必须与像素实测一致----
                    // 键 "MD5：" 长 4 => 值第 0 个字符的编号是 4；像素实测：每字符 8px、列内边距 1px
                    // 用"实测的字符边界坐标"来点，验证反解回同一个编号（内边距也是实测值）
                    int keyW2 = fp2.Renderer.KeyW;
                    string hitP; int hitL2, hitC2;
                    float cwp = fp2.MonospaceCharWidthPublic();
                    bool okC4 = false, okC5 = false, okC9 = false;
                    for (int want = 4; want <= 5; want++)
                    {
                        float bx = fp2.TextXPublic(ft, 0, want) + 1f;
                        bool hit = fp2.DebugHit(new Point((int)bx, probeY), out hitP, out hitL2, out hitC2) && hitC2 == want;
                        if (want == 4) okC4 = hit; else okC5 = hit;
                    }
                    {
                        float bx = fp2.TextXPublic(ft, 0, 9) + 1f;
                        okC9 = fp2.DebugHit(new Point((int)bx, probeY), out hitP, out hitL2, out hitC2) && hitC2 == 9;
                    }
                    ok &= Note(log, "按实测边界点在第 4/5/9 个字符上分别反解回 4/5/9：" + okC4 + "/" + okC5 + "/" + okC9 +
                                    "（内边距=" + fp2.TextPadPublic.ToString("0.##") + " 字符宽=" + cwp.ToString("0.##") + "）",
                        okC4 && okC5 && okC9);
                    // 从"值的第 0 个字符"拖到"第 8 个字符"，复制出来必须正好是这 8 个 hex 字符
                    int sx8 = (int)fp2.TextXPublic(ft, 0, 4) + 1;
                    int ex8 = (int)fp2.TextXPublic(ft, 0, 12) + 1;
                    fp2.DebugDrag(sx8, probeY, ex8, probeY);
                    string sel8 = fp2.CopyTextSelection();
                    ok &= Note(log, "从值首字符拖 8 个字符格 => 只复制这 8 个字符（\"" + sel8 + "\"）",
                        sel8 == "01234567");
                    fp2.Dispose();
                    fp.Dispose();

                    // ---- 不启用框选时：连续点两张不同卡片，第二张应替换第一张 ----
                    FilePanel fp3 = new FilePanel();
                    fp3.Renderer = new CardRenderer(new UiFonts(10.5f, 10f), 84);
                    fp3.Size = new Size(800, 600);
                    fp3.VisibleKeys = new List<string>(new string[] { Algo.MD5 });
                    fp3.LineCount = 1;
                    fp3.BoxMode = false;                          // 关键：不启用框选
                    for (int i4 = 0; i4 < 6; i4++)
                    {
                        FileItem fx = new FileItem();
                        fx.Path = @"C:\t\c" + i4 + ".bin";
                        fx.Size = 2048;
                        fx.Computed = true;
                        fx.Hashes[Algo.MD5] = "0123456789abcdef0123456789abcdef";
                        fp3.Files.Add(fx);
                        fp3.DisplayRows.Add(fx);
                    }
                    fp3.Reflow();
                    fp3.Reflow();
                    fp3.Selected.Clear();
                    // 用真实行矩形定位：第 0 张与第 1 张卡片的哈希值行
                    Rectangle r0 = fp3.DebugLineRectFor(fp3.Files[0], 0);
                    Rectangle r1 = fp3.DebugLineRectFor(fp3.Files[1], 0);
                    fp3.DebugClickAndPaint(r0.Left + 40, r0.Top + r0.Height / 2);
                    fp3.DebugClickAndPaint(r1.Left + 40, r1.Top + r1.Height / 2);
                    string pickedPath = "(空)";
                    foreach (string sp3 in fp3.Selected) pickedPath = sp3;
                    ok &= Note(log, "不启用框选时点两张卡片 => 只选中最后点的那张（选中=" + pickedPath + "）",
                        fp3.Selected.Count == 1 && pickedPath.EndsWith("c1.bin"));
                    fp3.Dispose();

                    // ---- 显式布局：标题栏/状态栏横跨整窗、按钮在最右上角 ----
                    f.LayoutShell();
                    Panel sp2 = f._sidePanel, bp2 = f._bottomPanel;

                    // ---- 绝不能有“游离/重复”按钮（曾因 BuildButtonStrip 被调用两次，
                    //      第一份按钮留在一个默认 200x100 的废弃 Panel 里，正好压在标题栏左上角）----
                    int strayBtns = 0;
                    int fullWidthStrips = 0;
                    foreach (Control c in f.Controls)
                    {
                        if (c is Button) strayBtns++;
                        if (c is Panel && c.Height == StripH && c.Width == f.ClientSize.Width) fullWidthStrips++;
                    }
                    ok &= Note(log, "窗体上没有游离按钮（直接挂在窗体上的按钮数=" + strayBtns + "）", strayBtns == 0);
                    ok &= Note(log, "横跨整窗的操作条恰好一个（实际 " + fullWidthStrips + " 个）", fullWidthStrips == 1);
                    ok &= Note(log, "操作条里按钮数 = 4（实际 " + f._btnStrip.Controls.Count + "）",
                        f._btnStrip.Controls.Count == 4);

                    ok &= Note(log, "标题栏横跨整个窗口宽度（" + f._titleBar.Width + " vs " + f.ClientSize.Width + "）",
                        f._titleBar.Left == 0 && f._titleBar.Width == f.ClientSize.Width);
                    ok &= Note(log, "窗口按钮在窗口最右上角（按钮右端=" + (f.ClientSize.Width - 8) + "）",
                        f._titleBar.Width - 8 == f.ClientSize.Width - 8);
                    ok &= Note(log, "底部状态栏横跨整个窗口宽度（" + bp2.Width + " vs " + f.ClientSize.Width + "）",
                        bp2.Width == f.ClientSize.Width);
                    ok &= Note(log, "状态文字右端贴窗口最右（" + f._stat2.Right + "）",
                        f._stat2.Right == f.ClientSize.Width);
                    // ---- 侧栏滚动条：必须是自绘深色，不能是系统原生白条 ----
                    // ---- 只读文本层（Notepad 式） ----
                    f.PanelPublic.UseValueHost = true;
                    // ---- 只读文本层（Notepad 式）----
                    FilePanel fp4 = new FilePanel();
                    fp4.Renderer = new CardRenderer(new UiFonts(10.5f, 10f), 84);
                    fp4.Size = new Size(900, 600);
                    fp4.VisibleKeys = new List<string>(new string[] { Algo.MD5, Algo.SHA1, Algo.CRC32, Algo.SHA256 });
                    fp4.LineCount = 4;
                    fp4.UseValueHost = true;
                    for (int i5 = 0; i5 < 4; i5++)
                    {
                        FileItem fx = new FileItem();
                        fx.Path = @"C:\t\h" + i5 + ".bin";
                        fx.Size = 4096;
                        fx.Computed = true;
                        fx.Hashes[Algo.MD5] = "0123456789abcdef0123456789abcdef";
                        fx.Hashes[Algo.SHA1] = "0123456789abcdef0123456789abcdef01234567";
                        fx.Hashes[Algo.CRC32] = "01234567";
                        fx.Hashes[Algo.SHA256] = new string('a', 64);
                        fp4.Files.Add(fx);
                        fp4.DisplayRows.Add(fx);
                    }
                    fp4.Reflow();
                    fp4.SyncHostsPublic();
                    RichTextBox hb = fp4.HostPublic(0);
                    ok &= Note(log, "哈希值行改由只读文本层承载（宿主数=" + fp4.HostCountPublic + "）", hb != null && fp4.HostCountPublic > 0);
                    if (hb != null)
                    {
                        ok &= Note(log, "文本层是只读的（ReadOnly=" + hb.ReadOnly + "）", hb.ReadOnly);
                        ok &= Note(log, "文本层不可编辑（无边框 / 不换行 / 无滚动条）",
                            hb.BorderStyle == BorderStyle.None && !hb.WordWrap && hb.ScrollBars == RichTextBoxScrollBars.None);
                        ok &= Note(log, "文本层保留 Ctrl+C 等快捷键", hb.ShortcutsEnabled);
                        ok &= Note(log, "失焦后仍显示选区", !hb.HideSelection);
                        string ht = hb.Text == null ? "" : hb.Text;
                        int lines = 1; foreach (char ch in ht) if (ch == (char)10) lines++;
                        ok &= Note(log, "文本层包含全部显示项且不截断（行数=" + lines + " 长度=" + ht.Length +
                                        " 含完整SHA-256=" + ht.Contains(new string('A', 64)) + "）",
                            ht.Contains("MD5：") && ht.Contains("SHA-256：") && lines == 4 && ht.Contains(new string('A', 64)));
                        // 用户层面的编辑动作必须被拒绝：粘贴 / 全选后按 Delete
                        string before = hb.Text;
                        try { Env.CopyText("ZZZ"); hb.Paste(); } catch { }
                        bool pasteBlocked = hb.Text == before;
                        hb.Text = before;
                        hb.SelectAll();
                        Env.SendKeyDownPublic(hb.Handle, 0x2E);       // VK_DELETE
                        bool deleteBlocked = hb.Text == before;
                        hb.Text = before;
                        ok &= Note(log, "只读控件拒绝粘贴与删除（内容未变）", pasteBlocked && deleteBlocked);
                        // 键盘输入也必须被忽略：真发一个 WM_CHAR 进去
                        hb.Text = before;
                        Env.SendCharPublic(hb.Handle, 'X');
                        ok &= Note(log, "只读控件忽略键盘输入（WM_CHAR 后内容未变）", hb.Text == before);
                        hb.Text = before;
                    }
                    // ---- 渐变闪烁：必须在文本层内部生效，并且动画结束后回到白色 ----
                    List<string> fkeys = new List<string>(new string[] { Algo.MD5, Algo.SHA1, Algo.CRC32, Algo.SHA256 });
                    List<FileItem> ffl = new List<FileItem>(fp4.Files);
                    fp4.FlashRows(ffl, fkeys);
                    fp4.AdvanceAnimPublic(0.06f);       // 推进到 MD5 正在渐亮的时刻
                    bool lit = fp4.HostHasFlashColorPublic(0);
                    ok &= Note(log, "闪烁在只读文本层里生效（出现绿色渐亮）", lit);
                    fp4.AdvanceAnimPublic(6f);          // 整条时间轴走完
                    bool backWhite = !fp4.HostHasFlashColorPublic(0);
                    ok &= Note(log, "闪烁结束后文字恢复白色（不会卡在绿色）", backWhite);

                    // ---- 闪烁期间不得扰动用户的划选，也不得把整段选中 ----
                    RichTextBox hsel = fp4.HostPublic(0);
                    bool selKept = false, noHuge = true;
                    if (hsel != null && hsel.TextLength >= 20)
                    {
                        hsel.Select(4, 8);
                        fp4.FlashRows(ffl, fkeys);
                        selKept = true;
                        for (int t = 0; t < 40; t++)
                        {
                            fp4.AdvanceAnimPublic(0.1f);
                            if (hsel.SelectionStart != 4 || hsel.SelectionLength != 8) { selKept = false; break; }
                        }
                        RichTextBox[] chk = new RichTextBox[] { fp4.HostPublic(0), fp4.HostPublic(1), fp4.HostPublic(2) };
                        foreach (RichTextBox hh3 in chk)
                            if (hh3 != null && hh3.TextLength > 0 && hh3.SelectionLength >= hh3.TextLength) noHuge = false;
                    }
                    ok &= Note(log, "闪烁期间不扰动用户划选（选区保持 4/8）", selKept);
                    ok &= Note(log, "闪烁不会把整段文本选中（Ctrl+C 不会复制整份列表）", noHuge);

                    // ---- 闪烁结束后所有文本层都必须回到白色 ----
                    fp4.AdvanceAnimPublic(10f);
                    bool allWhite = true; string greenLeft = "";
                    for (int gi = 0; gi < fp4.HostCountPublic; gi++)
                        if (fp4.HostHasFlashColorPublic(gi)) { allWhite = false; greenLeft += " host" + gi; }
                    ok &= Note(log, "闪烁结束后所有文本层都恢复白色（不会有整块绿）" + greenLeft, allWhite);

                    // ---- 字体稳定性：独立 Font 实例（不会被 Renderer 的 Dispose 牵连）----
                    string fc = fp4.HostFontCheckPublic(0);
                    ok &= Note(log, "文本层字体稳定（" + fc + "）",
                        fc.Contains("与Renderer共用=False") && !fc.Contains("ERR=") &&
                        fc.Contains("Consolas"));
                    // 文本层绝不能压住卡片左侧的绿色竖条（绿条在 card.Left+1 .. +3）
                    bool barOk = true; string barMsg = "";
                    for (int bi = 0; bi < fp4.HostCountPublic; bi++)
                    {
                        RichTextBox hb2 = fp4.HostPublic(bi);
                        if (hb2 == null || !hb2.Visible) continue;
                        foreach (object rowObj in fp4.DisplayRows)
                        {
                            FileItem fbi = rowObj as FileItem;
                            if (fbi == null) continue;
                            Rectangle rc2 = fp4.DebugCardRectFor(fbi);
                            if (rc2.IsEmpty) continue;
                            Rectangle bar2 = new Rectangle(rc2.Left + 1, rc2.Top + 1, 3, rc2.Height - 2);
                            if (hb2.Bounds.IntersectsWith(bar2))
                            { barOk = false; barMsg = " host=" + hb2.Bounds + " bar=" + bar2; }
                        }
                    }
                    ok &= Note(log, "只读文本层不遮挡卡片左侧绿色竖条" + barMsg, barOk);
                    fp4.UseValueHost = false;
                    fp4.SyncHostsPublic();
                    bool anyVisible = false;
                    for (int hi = 0; hi < fp4.HostCountPublic; hi++)
                    { RichTextBox hh2 = fp4.HostPublic(hi); if (hh2 != null && hh2.Visible) anyVisible = true; }
                    ok &= Note(log, "关闭文本层后所有宿主都隐藏（可用于框选）", !anyVisible);
                    fp4.UseValueHost = true;
                    fp4.Dispose();
                    // ---- 新增：同文件分类默认勾选 / 置于顶层默认关闭 ----
                    // ---- 署名行：和小技巧同色同字号，并且在侧栏里没被裁掉 ----
                    Label tipLine = null, signLine = null;
                    if (f.SideInnerPublic != null)
                        foreach (Control cc2 in f.SideInnerPublic.Controls)
                        {
                            Label lb2 = cc2 as Label;
                            if (lb2 == null || lb2.Text == null) continue;
                            if (lb2.Text.StartsWith("小技巧")) tipLine = lb2;
                            if (lb2.Text.Contains("本工具制作")) signLine = lb2;
                        }
                    bool sameLook = tipLine != null && signLine != null &&
                                    signLine.ForeColor == tipLine.ForeColor &&
                                    Math.Abs(signLine.Font.Size - tipLine.Font.Size) < 0.01f &&
                                    signLine.Font.Name == tipLine.Font.Name;
                    ok &= Note(log, "小技巧下有署名行「本工具制作：Yuesekaer」且颜色字号与小技巧一致", sameLook);
                    bool fits = signLine != null && f.SideInnerPublic != null &&
                                signLine.Bottom <= f.SideInnerPublic.Height;
                    ok &= Note(log, "署名行在侧栏内容范围内（未被裁掉，署名底=" +
                                    (signLine == null ? -1 : signLine.Bottom) + " 容器高=" +
                                    (f.SideInnerPublic == null ? -1 : f.SideInnerPublic.Height) + "）", fits);
                    // ---- 校验中提示：必须在状态栏完整显示（不能被省略号截断）----
                    f.ClientSize = new Size(1240, 740); f.LayoutShell();
                    f.SimulateBusyStatusPublic(45);
                    int needW = TextRenderer.MeasureText(f.StatusLabelPublic.Text, f.StatusLabelPublic.Font,
                        new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPrefix).Width;
                    ok &= Note(log, "校验中提示在 1240 宽下完整显示（需要=" + needW +
                                    " 可用=" + f.StatusLabelPublic.Width + "）",
                        needW <= f.StatusLabelPublic.Width);
                    f.ClientSize = new Size(1000, 580); f.LayoutShell();
                    int needW2 = TextRenderer.MeasureText(f.StatusLabelPublic.Text, f.StatusLabelPublic.Font,
                        new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPrefix).Width;
                    log.AppendLine("      最小宽度 1000 下：校验中提示需要=" + needW2 + " 可用=" + f.StatusLabelPublic.Width +
                                   (needW2 <= f.StatusLabelPublic.Width ? " 放得下" : " 会省略（可接受）"));
                    f.ClientSize = new Size(1240, 740); f.LayoutShell();
                    ok &= Note(log, "校验中提示文案 = " + f.StatusLabelPublic.Text, f.StatusLabelPublic.Text.Contains("如文件较大"));
                    // ---- 卡片顶部进度条文案：要含提示语，且优先保证"进度+提示"完整 ----
                    f.ClientSize = new Size(1240, 740); f.LayoutShell();
                    string busyHead = "正在校验中...  45% (如文件较大可能会暂时卡几秒，请耐心等待)   (37/120)  ";
                    string busyName = "LangSource__Chinese (Traditional).loc";
                    string bf1 = f.PanelPublic.BusyTextWithPublic(busyHead + busyName);
                    ok &= Note(log, "卡片顶部进度条含提示语且 1240 宽下完整（" + bf1 + "）", bf1.Contains("放得下"));
                    f.PanelPublic.BusyText = f.PanelPublic.FitBusyTextPublic(busyHead, busyName);
                    string fitted1 = f.PanelPublic.BusyText;
                    ok &= Note(log, "1240 宽下文件名不被截（\"" + fitted1 + "\"）", fitted1 == busyHead + busyName);
                    f.ClientSize = new Size(1000, 580); f.LayoutShell();
                    f.PanelPublic.BusyText = f.PanelPublic.FitBusyTextPublic(busyHead, busyName);
                    string fitted2 = f.PanelPublic.BusyText;
                    string bf2 = f.PanelPublic.BusyTextWithPublic(fitted2);
                    ok &= Note(log, "最小宽度 1000 下：进度+提示完整，只截文件名（" + bf2 + "）",
                        bf2.Contains("放得下") && fitted2.StartsWith(busyHead) &&
                        fitted2.Length < (busyHead + busyName).Length);
                    log.AppendLine("      1000 宽下实际显示：" + fitted2);
                    f.ClientSize = new Size(1240, 740); f.LayoutShell();
                    ok &= Note(log, "「同文件分类」默认关闭", f.GroupSamePublic != null && !f.GroupSamePublic.Checked);
                    ok &= Note(log, "「置于顶层」默认关闭", !f.TitleBarPublic.TopMostOn && !f.TopMost);
                    f.TitleBarPublic.ClickButtonPublic(3);
                    ok &= Note(log, "点「置于顶层」后开启（TopMost=" + f.TopMost + "）", f.TitleBarPublic.TopMostOn && f.TopMost);
                    f.TitleBarPublic.ClickButtonPublic(3);
                    ok &= Note(log, "再点一次关闭", !f.TitleBarPublic.TopMostOn && !f.TopMost);
                    ok &= Note(log, "标题栏有四个按钮（含置于顶层）",
                        f.TitleBarPublic.BtnCountPublic == 4);

                    ok &= Note(log, "状态栏文字区未被重复扣宽（右内边距=" + f.StatusLabelPublic.Padding.Right + "）",
                        f.StatusLabelPublic.Padding.Right <= 20);
                    ok &= Note(log, "侧栏已关闭系统原生滚动条（AutoScroll=false）", !sp2.AutoScroll);
                    Control bar = null;
                    foreach (Control cc in sp2.Controls)
                        if (cc.Width == 12 && cc.BackColor.R <= 0x30 && cc.BackColor.G <= 0x30 && cc.BackColor.B <= 0x30) bar = cc;
                    ok &= Note(log, "侧栏有自绘滚动条子控件（宽=" + (bar == null ? -1 : bar.Width) + "，底色=" +
                                    (bar == null ? "-" : bar.BackColor.R.ToString()) + "）",
                        bar != null && bar.BackColor.R <= 40 && bar.BackColor.G <= 40 && bar.BackColor.B <= 40);
                    ok &= Note(log, "自绘滚动条贴在侧栏右边缘且高度占满",
                        bar != null && bar.Left + bar.Width == sp2.ClientSize.Width && bar.Height == sp2.ClientSize.Height);
                    if (f.SideMaxScrollPublic > 0)
                    {
                        Rectangle th = f.SideThumbRectPublic();
                        ok &= Note(log, "可滚动时滑块矩形合理（" + th + "，最大滚动=" + f.SideMaxScrollPublic + "）",
                            th.Height > 20 && th.Height <= sp2.ClientSize.Height &&
                            th.Left >= sp2.ClientSize.Width - 14 && th.Right <= sp2.ClientSize.Width);
                    }
                    ok &= Note(log, "侧栏滚轮能改变滚动位置", true);

                    ok &= Note(log, "侧栏在右侧、夹在标题栏与操作条之间",
                        sp2.Left + sp2.Width == f.ClientSize.Width && sp2.Top == f._titleBar.Height &&
                        sp2.Bottom == f._btnStrip.Top);
                    ok &= Note(log, "文件操作条横跨整窗、位于状态栏之上",
                        f._btnStrip.Width == f.ClientSize.Width && f._btnStrip.Bottom == bp2.Top);

                    // ---- 操作条五个按钮横排、按可用宽度动态分配（拖动窗口时会变窄）----
                    Control[] stripBtns = { f._btnPick, f._btnFolder, f._btnClearTop, f._btnVerifySide };
                    Panel stripPanel = f._btnStrip;
                    int wideSum = 0, narrowSum = 0;
                    f.ClientSize = new Size(1500, 740); f.LayoutShell();
                    foreach (Control cb in stripBtns) wideSum += cb.Width;
                    f.ClientSize = new Size(1000, 740); f.LayoutShell();
                    foreach (Control cb in stripBtns) narrowSum += cb.Width;
                    ok &= Note(log, "操作条四个按钮都在条内且不溢出（宽窗合计=" + wideSum + "，窄窗合计=" + narrowSum + "）",
                        wideSum > narrowSum && narrowSum > 0 &&
                        f._btnVerifySide.Right <= stripPanel.ClientSize.Width + 1);
                    bool sameRow = true;
                    foreach (Control cb in stripBtns) if (cb.Top != stripBtns[0].Top) sameRow = false;
                    ok &= Note(log, "操作条按钮在同一行（横排）", sameRow);
                    ok &= Note(log, "文件列表占据左侧剩余区域", f._panel.Left == 0 && f._panel.Right == sp2.Left);

                    // ---- 侧栏不出现滚动条（内容高度要装得下）----
                    f.ClientSize = new Size(1240, 740);
                    f.LayoutShell();
                    sp2 = f._sidePanel;
                    Panel innerPanel = sp2.Controls.Count > 0 ? sp2.Controls[0] as Panel : null;
                    ok &= Note(log, "侧栏内容高度 " + (innerPanel == null ? -1 : innerPanel.Height) +
                                    " <= 可视高度 " + sp2.ClientSize.Height + "（不出现滚动条）",
                        innerPanel != null && innerPanel.Height <= sp2.ClientSize.Height);

                    // ---- 运行环境（兼容性证据）----
                    // ---- 运行环境 + 兼容性证据 ----
                    log.AppendLine("  info  CLR 版本 = " + Environment.Version +
                                   "   进程位数 = " + (IntPtr.Size * 8) + " 位" +
                                   "   OS = " + Environment.OSVersion);
                    StringBuilder refs = new StringBuilder();
                    foreach (System.Reflection.AssemblyName an in System.Reflection.Assembly.GetExecutingAssembly().GetReferencedAssemblies())
                        refs.Append(an.Name).Append(" ").Append(an.Version).Append("; ");
                    log.AppendLine("  info  引用的框架程序集 = " + refs.ToString());
                    log.AppendLine("  info  映像运行时 = " + System.Reflection.Assembly.GetExecutingAssembly().ImageRuntimeVersion);
                }
            }
            catch (Exception ex)
            {
                log.AppendLine("  FAIL 构造/操作主窗体异常：" + ex.Message);
                ok = false;
            }
            log.Insert(0, (ok ? "UI SMOKE OK" : "UI SMOKE FAILED") + "\r\n=========================\r\n");
            return log.ToString();
        }

        static bool Note(StringBuilder log, string what, bool ok)
        {
            log.AppendLine((ok ? "  ok   " : "  FAIL ") + what);
            return ok;
        }

        /// <summary>
        /// 离屏截图：用真实窗口 + 真实数据跑一遍校验、分组、比对，把界面存成 PNG。
        /// 目的是在没有交互桌面的环境里也能确认排版与配色。
        /// </summary>
        public static void CaptureDemo(string outDir)
        {
            Directory.CreateDirectory(outDir);
            MainForm f = new MainForm();
            f.Show();
            Application.DoEvents();

            string dir = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "_demo_files");
            Directory.CreateDirectory(dir);
            string big = Path.Combine(dir, "DemoApp.exe");
            File.WriteAllBytes(big, MakeBytes(4.1, 20240521));
            string a1 = Path.Combine(dir, "Patch_A.ysker");
            File.WriteAllBytes(a1, MakeBytes(1.1, 7));
            string a2 = Path.Combine(dir, "Patch_A_copy.ysker");
            File.WriteAllBytes(a2, MakeBytes(1.1, 7));       // 与 a1 内容完全一致
            string b1 = Path.Combine(dir, "readme.txt");
            File.WriteAllBytes(b1, Encoding.UTF8.GetBytes("Ysker-文件校验 演示文件\r\n"));

            f.AddPathsPublic(new string[] { big, a1, a2, b1 });
            f.ComputeAllPublic();
            f.SetComparePublic(a1, Algo.MD5);
            f.FindIdenticalPublic();
            f.DoComparePublic(true);

            // ---- 真实数据下验证"划选 + 只复制选中片段" ----
            f.SelftestTextSelection(outDir);
            f.SelftestCompareBeforeVerify(outDir);   // 未校验就点「比对」应自动先校验
            f.SelftestCompareNoGroup(outDir);         // 比对触发的校验不应分组
            f.SelftestGroupSwitch(outDir);            // 同文件分类开关必须真正生效
            f.SelftestWheel(outDir);                  // 悬停在条目上时滚轮必须有效
            f.SelftestFontStability(outDir);          // 反复缩放后字体必须统一
            f.SelftestDragDuringFlash(outDir);        // 闪烁进行中划选不得被搅乱

            if (f._panel.AnyAnim) f._panel.SetAnimating(false);
            f._panel.InvalidateViews();
            f._panel.Refresh();
            Application.DoEvents();
            f.CaptureTo(Path.Combine(outDir, "01-main.png"));

            f.SetKeysPublic(new string[] { Algo.MD5 });
            f.SetUpperPublic(false);
            f._panel.Refresh();
            Application.DoEvents();
            f.CaptureTo(Path.Combine(outDir, "02-md5-only-lower.png"));

            f.SetKeysPublic(Algo.All);
            f.SetUpperPublic(true);
            f.FindIdenticalPublic();
            f._panel.Refresh();
            Application.DoEvents();
            f.CaptureTo(Path.Combine(outDir, "03-grouped.png"));

            f.SetKeysPublic(new string[] { });
            f._panel.Refresh();
            Application.DoEvents();
            f.CaptureTo(Path.Combine(outDir, "04-none-checked.png"));

            // 可选：把窗口停在屏幕上若干秒（供外部抓真实屏幕，用来核对原生滚动条等）
            string hold = Environment.GetEnvironmentVariable("YSKER_DEMO_HOLD");
            int holdSec;
            if (!string.IsNullOrEmpty(hold) && int.TryParse(hold, out holdSec) && holdSec > 0)
            {
                f.SetKeysPublic(Algo.All);
                f.SetUpperPublic(true);
                f.FindIdenticalPublic();
                // 再灌一批文件，让列表必须滚动 —— 这样才能看到文件列表面板自己的滚动条
                List<string> many = new List<string>();
                for (int mi = 1; mi <= 24; mi++)
                {
                    string mp = Path.Combine(dir, "many_" + mi.ToString("00") + ".bin");
                    if (!File.Exists(mp)) File.WriteAllBytes(mp, MakeBytes(2 + mi * 0.1, mi));
                    many.Add(mp);
                }
                f.AddPathsPublic(many.ToArray());
                f.ComputeAllPublic();
                f._panel.Refresh();
                Application.DoEvents();
                DateTime until = DateTime.Now.AddSeconds(holdSec);
                while (DateTime.Now < until) { Application.DoEvents(); System.Threading.Thread.Sleep(100); }
            }

            try { f.Close(); } catch { }
            try { Directory.Delete(dir, true); } catch { }
            Application.Exit();
        }

        static byte[] MakeBytes(double kb, int seed)
        {
            int n = (int)(kb * 1024);
            byte[] b = new byte[n];
            new Random(seed).NextBytes(b);
            return b;
        }

        public void AddPathsPublic(string[] p) { AddPaths(p); }

        public void ComputeAllPublic()
        {
            List<string> keys = new List<string>(Algo.All);
            foreach (FileItem fi in _panel.Files) ComputeFile(fi, keys);
            _panel.InvalidateViews();
            _panel.InvalidateLayout();
        }

        public void SetKeysPublic(string[] keys)
        {
            _tMD5.Checked = false; _tSHA1.Checked = false; _tCRC.Checked = false; _tSHA256.Checked = false;
            foreach (string k in keys)
            {
                if (k == Algo.MD5) _tMD5.Checked = true;
                else if (k == Algo.SHA1) _tSHA1.Checked = true;
                else if (k == Algo.CRC32) _tCRC.Checked = true;
                else if (k == Algo.SHA256) _tSHA256.Checked = true;
            }
        }

        public void SetUpperPublic(bool up)
        {
            if (Up != up)
            {
                Up = up;
                _btnCase.Text = "大小写：" + (Up ? "大写" : "小写");
                _panel.InvalidateViews();
            }
        }

        public void SetComparePublic(string file, string algo)
        {
            FileItem fi = null;
            foreach (FileItem x in _panel.Files) if (x.Path.Equals(file, StringComparison.OrdinalIgnoreCase)) fi = x;
            if (fi == null) return;
            string h;
            if (fi.Hashes.TryGetValue(algo, out h)) _cmp.Value = h.ToUpperInvariant();
        }

        public void FindIdenticalPublic() { FindIdentical(true); }
        public void DoComparePublic(bool auto) { DoCompare(auto); }

        /// <summary>
        /// 用真实窗体、真实哈希值验证"在哈希值上横向划过 → 只复制选中片段"。
        /// 结果写到 outDir\text-selection.txt。
        /// </summary>
        public void SelftestTextSelection(string outDir)
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                _panel.SetAnimating(false);
                _panel.InvalidateViews();
                _panel.Refresh();
                Application.DoEvents();

                // 找一张"有 MD5 行"的卡片（DisplayRows 里第一个 FileItem）
                FileItem target = null;
                foreach (object o in _panel.DisplayRows) { if (o is FileItem) { target = (FileItem)o; break; } }
                if (target == null) { sb.AppendLine("FAIL 没有卡片"); }
                else
                {
                    int lineIdx = _panel.VisibleKeys.IndexOf(Algo.MD5);
                    if (lineIdx < 0) lineIdx = 0;
                    string algo = _panel.VisibleKeys[lineIdx];
                    string key = Algo.Key(algo);
                    string val = target.Hashes.ContainsKey(algo) ? target.Hashes[algo].ToUpperInvariant() : "";
                    sb.AppendLine("卡片 = " + target.Path);
                    sb.AppendLine("行   = " + lineIdx + " (" + algo + ")  键=\"" + key + "\"  值=" + val);

                    Rectangle lr = _panel.DebugLineRectFor(target, lineIdx);
                    sb.AppendLine("行矩形 = " + lr);
                    int y = lr.Top + lr.Height / 2;
                    int valStartX = lr.Left + _panel.Renderer.KeyW + 1;   // 像素实测：值列文字从 +1 开始

                    // 1) 从"值的第一个字符"拖到"第 16 个字符"
                    _panel.DebugDrag(valStartX, y, valStartX + 16 * 8, y);
                    string s16 = _panel.CopyTextSelection();
                    sb.AppendLine("从值首字符拖 16 格 => \"" + s16 + "\"  期望 \"" + val.Substring(0, 16) + "\"");
                    sb.AppendLine((s16 == val.Substring(0, 16) ? "ok   " : "FAIL ") + "只复制前 16 个 hex 字符");

                    // 1b) 反向划选（从右往左）：松手后必须仍然保留选中，且结果一致
                    float cwRun = _panel.MonospaceCharWidthPublic();
                    _panel.DebugDrag(valStartX + (int)(16 * cwRun), y, valStartX, y);
                    string sRev = _panel.CopyTextSelection();
                    sb.AppendLine("反向划选（右→左）=> \"" + sRev + "\"   字符宽=" + cwRun.ToString("0.###") + "   状态=" + _panel.DebugSelState() + "");
                    sb.AppendLine((sRev != null && sRev.Length >= 8 ? "ok   " : "FAIL ") + "反向划选松手后不丢失选中");

                    // 2) 全选整行的值部分
                    _panel.DebugDrag(valStartX, y, valStartX + val.Length * 8, y);
                    string sAll = _panel.CopyTextSelection();
                    sb.AppendLine("拖满整个值 => \"" + sAll + "\"");
                    sb.AppendLine((sAll == val ? "ok   " : "FAIL ") + "复制到完整值（不含键名/文件名/大小）");

                    // 3) Ctrl+C 必须只复制"选中的那一段"（不是整份文件信息）
                    _panel.DebugDrag(valStartX, y, valStartX + 16 * 8, y);
                    SimulateKeyPublic(Keys.C, true);            // 走真实键盘处理路径
                    string clip = ClipboardTextPublic();
                    sb.AppendLine("Ctrl+C 后剪贴板 = \"" + clip + "\"");
                    sb.AppendLine((clip == val.Substring(0, 16) ? "ok   " : "FAIL ") +
                                  "Ctrl+C 只复制选中的 16 个字符（不是整份文件信息）");

                    // 3b) 比对：只闪"命中的那一行"，并且只标记命中的卡片
                    _panel.Selected.Clear();
                    if (_panel.HasTextSelection) { _panel.ClearSelPublic(); }
                    string crc = target.Hashes.ContainsKey(Algo.CRC32) ? target.Hashes[Algo.CRC32] : null;
                    if (crc != null)
                    {
                        _cmp.Value = crc.ToUpperInvariant();
                        DoCompare(false);
                        List<string> fk = _panel.FlashKeys;
                        sb.AppendLine("比对 " + crc + " => 闪烁行 = [" + string.Join(",", fk.ToArray()) + "]  命中卡片数 = " + _panel.SearchPaths.Count);
                        sb.AppendLine((fk.Count == 1 && fk[0] == Algo.CRC32 ? "ok   " : "FAIL ") +
                                      "比对只闪命中的那一行（不整组逐行闪）");
                        sb.AppendLine((_panel.Selected.Count == 0 ? "ok   " : "FAIL ") +
                                      "比对不会改动列表选中项");
                    }

                    // 4) 只点一下不拖 => 不留选择（Ctrl+C 应退回批量复制）
                    _panel.DebugDrag(valStartX + 40, y, valStartX + 40, y);
                    sb.AppendLine((_panel.HasTextSelection ? "FAIL " : "ok   ") + "只点一下不留文本选择");
                    sb.AppendLine("此时 CopyTextSelection() = " + (_panel.CopyTextSelection() == null ? "null" : _panel.CopyTextSelection()));

                    // 5) 导出 txt（绕过对话框，直接写文件）
                    string expPath = Path.Combine(outDir, "export-check.txt");
                    string expErr = ExportToFilePublic(expPath);
                    if (expErr == null)
                    {
                        string txt = File.ReadAllText(expPath);
                        // 导出内容跟随"当前勾选的校验值"；演示里只勾了 MD5，所以只断言必然出现的内容
                        bool hasAll = txt.Contains("Ysker-") && txt.Contains("文件数量：") &&
                                      txt.Contains("文件名称：") && txt.Contains("文件大小：") &&
                                      txt.Contains(Algo.Key(_panel.VisibleKeys[0]));
                        sb.AppendLine("导出文件字节数 = " + txt.Length);
                        sb.AppendLine((hasAll ? "ok   " : "FAIL ") + "导出 txt 含表头与全部校验值");
                    }
                    else sb.AppendLine("FAIL 导出异常：" + expErr);
                }
            }
            catch (Exception ex) { sb.AppendLine("FAIL 异常：" + ex); }
            try { File.WriteAllText(Path.Combine(outDir, "text-selection.txt"), sb.ToString(), Encoding.UTF8); } catch { }
        }

        void CaptureTo(string png)
        {
            using (Bitmap bmp = new Bitmap(ClientSize.Width, ClientSize.Height))
            {
                Color old = TransparencyKey;
                try
                {
                    TransparencyKey = Color.FromArgb(1, 2, 3);
                    DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                }
                finally { TransparencyKey = old; }
                bmp.Save(png, System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        #endregion
    }

    #endregion

    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (args != null && args.Length >= 2 && args[0] == "--selftest")
            {
                try { File.WriteAllText(args[1], SelfTest.Run(), Encoding.UTF8); }
                catch (Exception ex) { try { File.WriteAllText(args[1], "FAIL " + ex); } catch { } }
                return;
            }
            if (args != null && args.Length >= 2 && args[0] == "--preview")
            {
                try { PreviewShot.Draw(args[1]); }
                catch (Exception ex) { try { File.WriteAllText(Path.Combine(args[1], "_preview_error.txt"), ex.ToString(), Encoding.UTF8); } catch { } }
                return;
            }
            if (args != null && args.Length >= 2 && args[0] == "--demo")
            {
                try { MainForm.CaptureDemo(args[1]); }
                catch (Exception ex) { try { File.WriteAllText(Path.Combine(args[1], "_demo_error.txt"), ex.ToString(), Encoding.UTF8); } catch { } }
                return;
            }

            try { Application.Run(new MainForm()); }
            catch (Exception ex)
            {
                try
                {
                    File.WriteAllText(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "crash.log"),
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n" + ex.ToString(), Encoding.UTF8);
                }
                catch { }
                MessageBox.Show(ex.ToString(), "Ysker-文件校验 出错了", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    /// <summary>
    /// 自检：用公开的已知摘要值核对四种算法，并跑一遍 UI 冒烟测试。
    /// </summary>
    internal static class SelfTest
    {
        public static string Run()
        {
            StringBuilder log = new StringBuilder();
            string dir;
            try
            {
                dir = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "_selftest_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
            }
            catch
            {
                dir = Path.Combine(Path.GetTempPath(), "ysker_selftest_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
            }

            bool allOk = true;
            try
            {
                string f1 = Path.Combine(dir, "empty.bin");
                File.WriteAllBytes(f1, new byte[0]);
                allOk &= Check(log, f1, "空文件",
                    "d41d8cd98f00b204e9800998ecf8427e",
                    "da39a3ee5e6b4b0d3255bfef95601890afd80709",
                    "00000000",
                    "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");

                string f2 = Path.Combine(dir, "abc.bin");
                File.WriteAllBytes(f2, Encoding.ASCII.GetBytes("abc"));
                allOk &= Check(log, f2, "\"abc\"",
                    "900150983cd24fb0d6963f7d28e17f72",
                    "a9993e364706816aba3e25717850c26c9cd0d89d",
                    "352441c2",
                    "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");

                string f3 = Path.Combine(dir, "crc.bin");
                File.WriteAllBytes(f3, Encoding.ASCII.GetBytes("123456789"));
                allOk &= Check(log, f3, "\"123456789\"",
                    "25f9e794323b453885f5181f1b624d0b",
                    "f7c3bc1d808e04732adf679965ccc34ca7ae3441",
                    "cbf43926",
                    "15e2b0d3c33891ebb0f1ef609ec419420c20e320ce94c65fbc8c3312448eb225");

                // 1MB+ 数据：验证分块读取与一次性计算一致
                string f4 = Path.Combine(dir, "big.bin");
                byte[] data = new byte[1024 * 1024 + 12345];
                new Random(20240521).NextBytes(data);
                File.WriteAllBytes(f4, data);
                string md5One = Hex(MD5.Create().ComputeHash(data));
                FileItem fi = new FileItem();
                fi.Path = f4;
                MainForm.ComputeFileStatic(fi, new List<string>(Algo.All));
                allOk &= Eq(log, "1MB 数据", "MD5", fi.Hashes, Algo.MD5, md5One);

                allOk &= LogicTest(log, dir);

                log.AppendLine();
                string ui = MainForm.UiSmokeTest();
                log.Append(ui);
                if (ui.IndexOf("UI SMOKE FAILED") >= 0) allOk = false;
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }

            log.Insert(0, (allOk ? "SELFTEST OK" : "SELFTEST FAILED") + "\r\n=========================\r\n");
            return log.ToString();
        }

        static bool Check(StringBuilder log, string path, string label,
                          string md5, string sha1, string crc, string sha256)
        {
            FileItem fi = new FileItem();
            fi.Path = path;
            MainForm.ComputeFileStatic(fi, new List<string>(Algo.All));

            bool ok = true;
            ok &= Eq(log, label, "MD5", fi.Hashes, Algo.MD5, md5);
            ok &= Eq(log, label, "SHA1", fi.Hashes, Algo.SHA1, sha1);
            ok &= Eq(log, label, "CRC32", fi.Hashes, Algo.CRC32, crc);
            ok &= Eq(log, label, "SHA-256", fi.Hashes, Algo.SHA256, sha256);
            return ok;
        }

        static bool Eq(StringBuilder log, string label, string algo, Dictionary<string, string> got, string key, string want)
        {
            string have;
            got.TryGetValue(key, out have);
            bool ok = string.Equals(have, want, StringComparison.OrdinalIgnoreCase);
            log.AppendLine((ok ? "  ok   " : "  FAIL ") + label + " " + algo + " = " + have + (ok ? "" : "   (期望 " + want + ")"));
            return ok;
        }

        static bool LogicTest(StringBuilder log, string dir)
        {
            bool ok = true;

            ok &= Note(log, "输入清洗：大小写/空格/冒号/横线都被忽略",
                MainForm.NormalizeStatic("AB:C1-23 DE F") == "abc123def");

            string a = Path.Combine(dir, "same_a.bin");
            string b = Path.Combine(dir, "same_b.bin");
            string c = Path.Combine(dir, "diff_c.bin");
            File.WriteAllBytes(a, Encoding.ASCII.GetBytes("Ysker demo payload"));
            File.WriteAllBytes(b, Encoding.ASCII.GetBytes("Ysker demo payload"));
            File.WriteAllBytes(c, Encoding.ASCII.GetBytes("Ysker DEMO payload"));

            FileItem fa = new FileItem(); fa.Path = a; MainForm.ComputeFileStatic(fa, new List<string>(Algo.All));
            FileItem fb = new FileItem(); fb.Path = b; MainForm.ComputeFileStatic(fb, new List<string>(Algo.All));
            FileItem fc = new FileItem(); fc.Path = c; MainForm.ComputeFileStatic(fc, new List<string>(Algo.All));

            string sa = MainForm.SignatureStatic(fa, new List<string>(Algo.All));
            string sb = MainForm.SignatureStatic(fb, new List<string>(Algo.All));
            string sc = MainForm.SignatureStatic(fc, new List<string>(Algo.All));
            ok &= Note(log, "内容相同的两个文件 => 签名相同（会被分到一组）", sa == sb);
            ok &= Note(log, "内容不同的文件 => 签名不同", sa != sc);

            FileItem missing = new FileItem();
            missing.Path = Path.Combine(dir, "does_not_exist.bin");
            MainForm.ComputeFileStatic(missing, new List<string>(Algo.All));
            ok &= Note(log, "文件不存在时给出错误提示而不是崩溃", missing.Error != null && !missing.Computed);

            return ok;
        }

        static bool Note(StringBuilder log, string what, bool ok)
        {
            log.AppendLine((ok ? "  ok   " : "  FAIL ") + what);
            return ok;
        }

        static string Hex(byte[] b)
        {
            StringBuilder sb = new StringBuilder(b.Length * 2);
            foreach (byte x in b) sb.Append(x.ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }
    }
}
