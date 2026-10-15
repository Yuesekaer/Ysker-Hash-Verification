// Ysker-Hash-Verification - batch file checksum utility (MD5 / SHA1 / CRC32 / SHA-256)
// Copyright (c) 2026 Yuesekaer
// SPDX-License-Identifier: MIT
// See the LICENSE file in the repository root for the full license text.

// Ysker-文件校验 - 可渲染内核
// 只放配色、尺寸与绘制逻辑，真机与设计预览共用同一份，保证两边永远一致。
//
// 绘制约定：
//   1) 行高由字体真实行高推算（不小于 20px），不要把行高压得比字体行高更小；
//   2) 文字一律走 GDI 的 TextRenderer —— GDI+ 的 Graphics.DrawString 在
//      "矩形高度装不下一整行"时会把整串丢弃（不画也不报错），而 TextRenderer
//      对矩形高度不挑食，且与 WinForms 原生标签同一种渲染方式，风格统一。
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Ysker
{
    /// <summary>配色表</summary>
    public static class Pal
    {
        public static readonly Color Bg      = Color.FromArgb(0x1F, 0x1F, 0x1F); // 软件本体 / 自绘标题栏
        public static readonly Color CardBg  = Color.FromArgb(0x2A, 0x2A, 0x2A); // 卡片底
        public static readonly Color CardBd  = Color.FromArgb(0x3C, 0x3C, 0x3C); // 卡片描边
        public static readonly Color Btn     = Color.FromArgb(0x1E, 0xA1, 0x2D); // 按钮背景
        public static readonly Color BtnHot  = Color.FromArgb(0x15, 0xCE, 0x2A); // 按钮悬停 / 按下
        public static readonly Color Del     = Color.FromArgb(0xC8, 0x3C, 0x26); // × 背景 / 关闭按钮悬停
        public static readonly Color DelHot  = Color.FromArgb(0xE5, 0x45, 0x29); // × 悬停
        public static readonly Color Text    = Color.White;                      // 文字 / × / 标题栏按钮默认
        public static readonly Color Muted   = Color.FromArgb(0xB4, 0xB4, 0xB4); // 次要文字
        public static readonly Color Divider = Color.FromArgb(0x8E, 0x8E, 0x8E); // 分组分割线文字
        public static readonly Color Flash   = Color.FromArgb(0x2E, 0xAF, 0x3D); // 一致项闪烁（绿）
        public static readonly Color BoxOn   = Color.FromArgb(0x46, 0xC0, 0x32); // 框选选中
        public static readonly Color WinBtn  = Color.FromArgb(0x2E, 0xAF, 0x3D); // 标题栏 最小化/最大化 悬停
        public static readonly Color PanelBg = Color.FromArgb(0x24, 0x24, 0x24); // 侧栏底
        public static readonly Color InputBg = Color.FromArgb(0x2E, 0x2E, 0x2E); // 输入框底
        public static readonly Color Border  = Color.FromArgb(0x40, 0x40, 0x40);
    }

    /// <summary>绘制时用到的字体集合（创建一次，避免闪烁与句柄泄漏）</summary>
    public sealed class UiFonts : IDisposable
    {
        public readonly Font Mono;     // 哈希值（等宽，保证对齐）
        public readonly Font MonoBig;
        public readonly Font Ui;       // 中文界面文字
        public readonly Font UiBold;
        public readonly Font Small;
        public readonly Font Char;     // × 与 √
        public readonly Font Title;

        public UiFonts(float monoSize, float uiSize)
        {
            Mono = new Font(Find("Consolas", "Courier New"), monoSize, FontStyle.Regular, GraphicsUnit.Point);
            MonoBig = new Font(Mono.FontFamily, monoSize, FontStyle.Regular, GraphicsUnit.Point);
            Ui = new Font(Find("Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "SimSun"), uiSize, FontStyle.Regular, GraphicsUnit.Point);
            UiBold = new Font(Ui.FontFamily, uiSize, FontStyle.Bold, GraphicsUnit.Point);
            Small = new Font(Ui.FontFamily, Math.Max(7.5f, uiSize - 2.5f), FontStyle.Regular, GraphicsUnit.Point);
            Char = new Font(Ui.FontFamily, uiSize + 2f, FontStyle.Bold, GraphicsUnit.Point);
            Title = new Font(Ui.FontFamily, uiSize + 2f, FontStyle.Bold, GraphicsUnit.Point);
        }

        public static string Find(params string[] names)
        {
            List<string> have = new List<string>();
            foreach (FontFamily f in FontFamily.Families) have.Add(f.Name);
            foreach (string n in names) if (have.Contains(n)) return n;
            return FontFamily.GenericSansSerif.Name;
        }

        public void Dispose()
        {
            Mono.Dispose(); MonoBig.Dispose(); Ui.Dispose(); UiBold.Dispose();
            Small.Dispose(); Char.Dispose(); Title.Dispose();
        }
    }

    /// <summary>
    /// 自绘滚动条用的窄面板：开启 UserPaint + 双缓冲。
    /// （Control.SetStyle 是 protected，所以必须用一个子类来开。）
    /// </summary>
    internal sealed class DarkBar : Panel
    {
        public DarkBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
        }
    }

    /// <summary>卡片内容快照：渲染器只认这个结构</summary>
    public sealed class CardView
    {
        public string Path = "";
        public string SizeText = "";
        public bool Computed;
        public string Error;
        public bool Uppercase = true;
        /// <summary>键（"MD5：" 等），与 Lines 一一对应</summary>
        public List<string> Keys = new List<string>();
        /// <summary>值（未格式化，渲染时按 Uppercase 处理）</summary>
        public List<string> Values = new List<string>();
        /// <summary>每行的闪烁强度 0..1（由共同的时间轴决定，所以同一算法在所有文件上同步）</summary>
        public List<float> Flash = new List<float>();
        /// <summary>文件路径/文件名的闪烁强度 0..1（比对命中时，让"值"和"是哪个文件"一起亮）</summary>
        public float PathFlash;
        public bool IsDivider;
        public string DividerText = "";
    }

    /// <summary>
    /// 卡片渲染器：把一张卡片画进给定矩形，并提供按钮/文本行的命中矩形。
    /// 所有尺寸常量集中在这里，预览工具与真机因此永远一致。
    /// </summary>
    public sealed class CardRenderer
    {
        public static readonly int LineH;   // 每个哈希值一行（按字体真实行高推出来）
        public const int HeadH = 24;        // 文件名称行
        public const int FootH = 24;        // 复制按钮行
        public const int PadX = 10;
        public const int PadTop = 4;
        public const int DelW = 22;
        public const int DelH = 20;
        public const int CopyW = 62;
        public const int CopyH = 20;

        public UiFonts F;                   // 可替换：窗口变窄时整体降一档字号，避免哈希值被截断
        public int KeyW;                    // 键列宽度：最宽的键 + 间距，保证所有“：”对齐
        int _lineH = 0;                     // 当前字号下的行高（0 = 用静态默认值）
        static int _baseLineH;

        static CardRenderer()
        {
            float h = 20f;
            try
            {
                using (Font f = new Font(UiFonts.Find("Consolas", "Courier New"), 10.5f, FontStyle.Regular, GraphicsUnit.Point))
                    h = f.GetHeight() + 2f;
            }
            catch { }
            LineH = (int)Math.Ceiling(Math.Max(20f, h));
            _baseLineH = LineH;
        }

        public CardRenderer(UiFonts fonts, int keyWidth)
        {
            F = fonts;
            KeyW = keyWidth;
            _lineH = 0;
        }

        /// <summary>
        /// 换一套字号（窗口尺寸变化时调用）：行高、键列宽度都按新字号重算，
        /// 这样缩小窗口后哈希值仍然完整可见，而不是被截断。
        /// </summary>
        public void UseFonts(UiFonts fonts, IEnumerable<string> keys)
        {
            F = fonts;
            KeyW = MeasureKeyWidth(fonts, keys);
            float h = 20f;
            try { h = fonts.MonoBig.GetHeight() + 2f; } catch { }
            _lineH = (int)Math.Ceiling(Math.Max(16f, h));
        }

        /// <summary>外部（只读文本层）实测的行高；0 = 用字体推算值</summary>
        public int LineHeightOverride = 0;

        /// <summary>当前有效行高</summary>
        public int LineHeight
        {
            get { return LineHeightOverride > 0 ? LineHeightOverride : (_lineH > 0 ? _lineH : LineH); }
        }

        /// <summary>量出最宽的键（例如 "SHA-256："）在像素上的宽度——用 GDI 量，和画的时候一致</summary>
        public static int MeasureKeyWidth(UiFonts f, IEnumerable<string> keys)
        {
            int w = 0;
            foreach (string k in keys)
            {
                Size sz = TextRenderer.MeasureText(k, f.MonoBig, new Size(int.MaxValue, 100), Flags(TxtFlags.NoPadding));
                if (sz.Width > w) w = sz.Width;
            }
            return w + 6;
        }

        public static int CardHeight(int lineCount)
        {
            return PadTop + HeadH + lineCount * LineH + FootH;
        }

        /// <summary>按当前实例字号算卡片高度（窗口缩放后字号会变）</summary>
        public int CurCardHeight(int lineCount)
        {
            return PadTop + HeadH + lineCount * LineHeight + FootH;
        }

        public static int DividerHeight { get { return 32; } }

        public static Rectangle DelRect(Rectangle b)
        {
            return new Rectangle(b.Right - PadX - DelW, b.Top + 3, DelW, DelH);
        }

        public static Rectangle CopyRect(Rectangle b)
        {
            return new Rectangle(b.Right - PadX - CopyW, b.Bottom - FootH + 1, CopyW, CopyH);
        }

        public static Rectangle LineRect(Rectangle b, int index)
        {
            return new Rectangle(b.Left, b.Top + PadTop + HeadH + index * LineH, b.Width, LineH);
        }

        /// <summary>按当前实例字号算第 index 行矩形</summary>
        public Rectangle CurLineRect(Rectangle b, int index)
        {
            int lh = LineHeight;
            return new Rectangle(b.Left, b.Top + PadTop + HeadH + index * lh, b.Width, lh);
        }

        /// <summary>名称行可用的文字宽度（避让 ×）</summary>
        public static int NameWidth(Rectangle b)
        {
            return b.Width - PadX * 2 - 2 - DelW - 8;
        }

        #region 文本工具（统一走 GDI，避免 GDI+ 短矩形丢字）

        [Flags]
        public enum TxtFlags
        {
            None = 0,
            NoPadding = 1,
            PathEllipsis = 2,
            EndEllipsis = 4,
            Center = 8,
            VCenter = 16,
            Left = 0,
            Right = 0x2   // 注意：NoPadding / PathEllipsis 等标志位在 TextFormatFlags 里是唯一值，不能重名
        }

        public static TextFormatFlags Flags(TxtFlags f)
        {
            TextFormatFlags t = TextFormatFlags.NoPrefix;
            if ((f & TxtFlags.NoPadding) != 0) t |= TextFormatFlags.NoPadding;
            if ((f & TxtFlags.PathEllipsis) != 0) t |= TextFormatFlags.PathEllipsis;
            if ((f & TxtFlags.EndEllipsis) != 0) t |= TextFormatFlags.EndEllipsis;
            if ((f & TxtFlags.Center) != 0) t |= TextFormatFlags.HorizontalCenter;
            if ((f & TxtFlags.VCenter) != 0) t |= TextFormatFlags.VerticalCenter;
            if ((f & TxtFlags.Right) != 0) t |= TextFormatFlags.Right;
            return t;
        }

        /// <summary>等宽字体单字符宽度（用于把像素位置换算成字符偏移）</summary>
        public float CharWidth()
        {
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                return g.MeasureString("0", F.MonoBig, int.MaxValue, StringFormat.GenericTypographic).Width;
        }

        public static StringFormat LeftMid2()
        {
            StringFormat sf = new StringFormat(StringFormat.GenericTypographic);
            sf.Alignment = StringAlignment.Near;
            sf.LineAlignment = StringAlignment.Center;
            sf.FormatFlags |= StringFormatFlags.NoWrap;
            return sf;
        }

        public void Txt(Graphics g, string s, Font f, Color c, Rectangle rc)
        {
            TextRenderer.DrawText(g, s, f, rc, c, Flags(TxtFlags.NoPadding | TxtFlags.VCenter));
        }

        public void Txt(Graphics g, string s, Font f, Color c, Rectangle rc, TxtFlags extra)
        {
            TextRenderer.DrawText(g, s, f, rc, c, Flags(TxtFlags.NoPadding | TxtFlags.VCenter | extra));
        }

        /// <summary>
        /// 画完整路径，并保证**整条路径都显示出来**：
        /// 空间不够时先把这一行的字号**逐档缩小**（最小 6.5pt），直到整条放得下；
        /// 实在连最小字号都放不下，才退化成"目录开头 + … + 完整文件名"。
        /// 这样小窗口下优先缩字，不会一上来就把中间的路径吃掉。
        /// </summary>
        public void DrawPathSmart(Graphics g, string path, Font f, Color c, Rectangle rc)
        {
            if (string.IsNullOrEmpty(path)) return;

            // 1) 当前字号能放下 -> 直接用
            int fullW = TextRenderer.MeasureText(g, path, f, new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding).Width;
            if (fullW <= rc.Width) { Txt(g, path, f, c, rc); return; }

            // 2) 逐档缩小字号，直到整条放得下（保持字体族与粗体）
            float start = f.Size;
            for (float sz = start - 0.5f; sz >= 6.5f; sz -= 0.5f)
            {
                using (Font nf = new Font(f.FontFamily, sz, f.Style, GraphicsUnit.Point))
                {
                    int w = TextRenderer.MeasureText(g, path, nf, new Size(int.MaxValue, int.MaxValue),
                        TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding).Width;
                    if (w <= rc.Width) { Txt(g, path, nf, c, rc); return; }
                }
            }

            // 3) 最小字号都放不下 -> 目录开头 + … + 完整文件名（文件名永远不丢）
            int nl = path.LastIndexOf('\\');
            if (nl < 0) nl = path.LastIndexOf('/');
            string dir = nl >= 0 ? path.Substring(0, nl) : "";
            string file = nl >= 0 ? path.Substring(nl + 1) : path;

            int fileW = TextRenderer.MeasureText(g, file, f, new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding).Width;
            if (fileW >= rc.Width)
            {
                // 连文件名都放不下：只截文件名
                Txt(g, file, f, c, rc, TxtFlags.EndEllipsis);
                return;
            }

            int ellW = TextRenderer.MeasureText(g, "…", f, new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding).Width;
            int sepHalf = TextRenderer.MeasureText(g, "\\", f, new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding).Width;
            int dirAvail = rc.Width - fileW - ellW - sepHalf - 2;
            if (dirAvail < sepHalf * 3)
            {
                // 目录连"…\"都放不下，只画文件名（宁可不要目录，也不能丢文件名）
                Txt(g, file, f, c, rc);
                return;
            }

            // 目录保留**开头**（"C:\Users\Public\…" 比 "…\Modern Warships" 信息更全），
            // 中间用 … 省略，文件名永远完整。用实测宽度逐字符累加，避免估算偏差。
            string dirToShow = "";
            for (int take = 1; take <= dir.Length; take++)
            {
                string cand = dir.Substring(0, take);
                int w = TextRenderer.MeasureText(g, cand + "…\\" + file, f, new Size(int.MaxValue, int.MaxValue),
                    TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding).Width;
                if (w > rc.Width) break;
                dirToShow = cand;
            }
            string head = dirToShow.Length > 0 ? dirToShow + "…\\" : "";
            Txt(g, head + file, f, c, rc);
        }

        #endregion

        /// <summary>
        /// 显式指定闪烁颜色的重载（设计预览按“某一帧”渲染时用）：
        /// flashTime = 该显示行距离“自己开始闪烁”的秒数；小于 0 表示不闪烁。
        /// 节奏：0.3 秒渐亮到 flashColor → 保持 0.6 秒 → 0.3 秒渐变回原色。
        /// </summary>
        public void DrawCard(Graphics g, Rectangle b, CardView v, Color flashColor, float flashTime, int flashLine)
        {
            _flashColor = flashColor;
            _flashTime = flashTime;
            _flashLine = flashLine;
            try { DrawCard(g, b, v); }
            finally { _flashTime = -1f; _flashLine = -1; }
        }

        Color _flashColor = Pal.Flash;
        float _flashTime = -1f;
        int _flashLine = -1;

        /// <summary>0.3 渐亮 / 0.6 保持 / 0.3 渐灭 的强度曲线</summary>
        public static float RampIntensity(float t)
        {
            const float rise = 0.1f, hold = 0.6f, fall = 0.3f;
            if (t <= 0f) return 0f;
            if (t < rise) return t / rise;
            if (t < rise + hold) return 1f;
            float d = t - rise - hold;
            if (d < fall) return 1f - d / fall;
            return 0f;
        }

        public void DrawCard(Graphics g, Rectangle b, CardView v)
        {
            DrawCard(g, b, v, true);
        }

        /// <summary>drawHashes=false 时只画卡片外观/名称/大小/错误行，哈希行交给只读文本层</summary>
        public void DrawCard(Graphics g, Rectangle b, CardView v, bool drawHashes)
        {
            g.SmoothingMode = SmoothingMode.None;

            if (v.IsDivider)
            {
                using (Pen p = new Pen(Pal.Border))
                {
                    int midY = b.Top + b.Height / 2;
                    g.DrawLine(p, b.Left + 6, midY, b.Left + 22, midY);
                    g.DrawLine(p, b.Right - 22, midY, b.Right - 6, midY);
                }
                Txt(g, v.DividerText, F.Small, Pal.Divider,
                    new Rectangle(b.Left + 28, b.Top, b.Width - 56, b.Height),
                    TxtFlags.Center);
                return;
            }

            using (SolidBrush br = new SolidBrush(Pal.CardBg)) g.FillRectangle(br, b);
            using (Pen p = new Pen(Pal.CardBd)) g.DrawRectangle(p, b.Left, b.Top, b.Width - 1, b.Height - 1);
            using (SolidBrush br = new SolidBrush(v.Computed ? Pal.Btn : Pal.Border))
                g.FillRectangle(br, b.Left + 1, b.Top + 1, 3, b.Height - 2);

            int x = b.Left + PadX + 4;
            int y = b.Top + PadTop;

            // ---- 文件名称（太长时中间省略，但**文件名本身一定要保留**）----
            Rectangle nameRc = new Rectangle(x, y, NameWidth(b), HeadH);
            // 不用 GDI 的 PathEllipsis（整条放不下时它会把文字右推，看起来像错位）；
            // 自己按"目录 + 文件名"分段处理：文件名永远完整，空间不够时先截目录。
            DrawPathSmart(g, v.Path, F.UiBold, v.PathFlash > 0.01f ? FlashColor(Pal.Text, v.PathFlash) : Pal.Text, nameRc);
            y += HeadH;

            if (!string.IsNullOrEmpty(v.Error))
            {
                Txt(g, v.Error, F.Ui, Pal.DelHot, new Rectangle(x, y, b.Width - PadX * 2, LineHeight));
                return;
            }

            if (!v.Computed) return;

            // ---- 哈希值 ----
            if (drawHashes)
            {
                for (int i = 0; i < v.Keys.Count; i++)
                {
                    Rectangle lr = new Rectangle(x, y + i * LineHeight, b.Width - PadX * 2 - 6, LineHeight);
                    DrawHashLine(g, lr, v, i);
                }
            }

            // ---- 文件大小（右下角，复制按钮左侧）----
            Rectangle sr = new Rectangle(CopyRect(b).Left - 190, CopyRect(b).Top, 182, CopyH);
            Txt(g, v.SizeText, F.Small, Pal.Muted, sr, TxtFlags.Right);
        }

        public void DrawHashLine(Graphics g, Rectangle lr, CardView v, int i)
        {
            string key = v.Keys[i];
            string val = v.Values[i] ?? "";
            if (v.Uppercase) val = val.ToUpperInvariant();

            float fl = (v.Flash != null && i < v.Flash.Count) ? v.Flash[i] : 0f;
            if (_flashTime >= 0f && (i == _flashLine || _flashLine < 0))
            {
                // 预览模式：按给定时间点算强度
                fl = RampIntensity(_flashTime);
            }
            Color c = Pal.Text;
            if (fl > 0.001f) c = Lerp(Pal.Text, _flashColor, Math.Max(0f, Math.Min(1f, fl)));

            // 键：等宽字体，固定列宽 —— 所有“：”因此落在同一列
            Txt(g, key, F.MonoBig, c, new Rectangle(lr.Left, lr.Top, KeyW, lr.Height));
            // 值
            Txt(g, val, F.MonoBig, c, new Rectangle(lr.Left + KeyW, lr.Top, lr.Width - KeyW, lr.Height));
        }

        /// <summary>把基础色按闪烁强度混向闪烁色（0 = 不变，1 = 全闪）</summary>
        public Color FlashColor(Color baseColor, float fl)
        {
            if (fl <= 0.001f) return baseColor;
            return Lerp(baseColor, _flashColor, Math.Max(0f, Math.Min(1f, fl)));
        }

        public static Color Lerp(Color a, Color b, float t)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }
    }

    /// <summary>整宽开关按钮（框选模式用）：关=暗底描边，开=绿底并显示 √</summary>
    public sealed class SwitchButton : Control
    {
        readonly string _base;
        readonly bool _primary;
        bool _checked;
        bool _hover;

        public event EventHandler CheckedChanged;

        public SwitchButton(string text, bool primary, float fontSize)
        {
            _base = text;
            _primary = primary;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Font = new Font(UiFonts.Find("Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "SimSun"), fontSize, FontStyle.Regular);
            Cursor = Cursors.Hand;
            BackColor = Pal.PanelBg;
            Size = new Size(180, 30);
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

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }

        /// <summary>供自检/脚本触发一次点击（等同于用户点一下），不要求控件已显示</summary>
        public void PerformClick() { OnClick(EventArgs.Empty); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Color bg = _checked ? (_primary ? Pal.Btn : Pal.BoxOn) : (_hover ? Pal.Border : Pal.PanelBg);
            using (SolidBrush b = new SolidBrush(bg)) g.FillRectangle(b, ClientRectangle);
            if (!_checked)
                using (Pen p = new Pen(_hover ? Pal.BoxOn : Pal.Border))
                    g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);

            string text = _checked ? "\u221A  " + _base : _base;
            TextRenderer.DrawText(g, text, Font, ClientRectangle, _checked ? Pal.Text : Pal.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }
    }

    /// <summary>白色描边方框 + 选中显示 √（框选按钮）</summary>
    public sealed class BoxSelectButton : Control
    {
        bool _checked;
        bool _hover;

        public event EventHandler CheckedChanged;

        public BoxSelectButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Size = new Size(30, 30);
            Cursor = Cursors.Hand;
            BackColor = Pal.Bg;
            Font = new Font(UiFonts.Find("Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "SimSun"), 11f, FontStyle.Bold);
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

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush b = new SolidBrush(Pal.Bg)) g.FillRectangle(b, ClientRectangle);
            Color line = _checked ? Pal.BoxOn : (_hover ? Pal.BoxOn : Pal.Text);
            Rectangle r = new Rectangle(5, 5, Width - 11, Height - 11);
            using (Pen p = new Pen(line, 2f)) g.DrawRectangle(p, r);
            if (_checked)
            {
                TextRenderer.DrawText(g, "\u221A", Font, new Rectangle(0, 0, Width, Height), Pal.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
    }
}
