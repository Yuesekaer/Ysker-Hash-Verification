// Ysker-Hash-Verification - batch file checksum utility (MD5 / SHA1 / CRC32 / SHA-256)
// Copyright (c) 2026 Yuesekaer
// SPDX-License-Identifier: MIT
// See the LICENSE file in the repository root for the full license text.

// Ysker-文件校验 - 自绘标题栏
// 原生标题栏是 Windows 画风，和软件配色差太远，所以整窗无边框、标题栏自己画：
//   默认按钮=纯白，最小化/最大化悬停=#2eaf3d，关闭悬停=#c83c26
// 同时负责：拖动窗口、双击最大化/还原、贴边吸附、四边八向缩放。
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Ysker
{
    internal sealed class TitleBar : Control
    {
        // 点击区域
        public const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14,
                         HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17, HTCAPTION = 2, HTCLIENT = 1;

        [DllImport("user32.dll")]
        static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);
        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int X, Y; }
        // DWM 的这几个属性是 Win10/Win11 才有的（圆角是 Win11）。
        // 老系统（Win7 / WinPE）上如果入口点不存在，调用会抛异常，已被 ApplyModernFrame 吞掉。
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);

        public const int BorderGrab = 6;   // 边框可拖拽宽度

        readonly Form _form;
        int _hover = -1;        // 0 最小化 1 最大化 2 关闭
        int _pressed = -1;

        public TitleBar(Form form)
        {
            _form = form;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Dock = DockStyle.Top;
            Height = 40;
            BackColor = Pal.Bg;
        }

        const int BtnW = 46;
        const int RightPad = 8;   // 四个按钮（置于顶层 / 最小化 / 最大化 / 关闭）贴窗口右缘

        Rectangle BtnRect(int which)
        {
            // 顺序（从左到右）：3=置于顶层, 0=最小化, 1=最大化, 2=关闭
            switch (which)
            {
                case 3: return new Rectangle(Math.Max(0, Width - RightPad - BtnW * 4), 0, BtnW, Height);
                case 0: return new Rectangle(Math.Max(0, Width - RightPad - BtnW * 3), 0, BtnW, Height);
                case 1: return new Rectangle(Math.Max(0, Width - RightPad - BtnW * 2), 0, BtnW, Height);
                default: return new Rectangle(Math.Max(0, Width - RightPad - BtnW), 0, BtnW, Height);
            }
        }

        int HitButton(Point p)
        {
            int[] order = { 3, 0, 1, 2 };
            foreach (int i in order)
            {
                Rectangle r = BtnRect(i);
                if (p.X >= r.Left && p.X < r.Right + ((i == 2) ? RightPad + 2 : 0)) return i;
            }
            return -1;
        }

        #region 绘制

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            using (SolidBrush b = new SolidBrush(Pal.Bg)) g.FillRectangle(b, ClientRectangle);

            // 品牌：顶栏放「Ysker-文件校验 vX.Y」+ 右侧四个窗口按钮
            using (Font fb = new Font(Fonts.Family, 15f, FontStyle.Bold))
                TextRenderer.DrawText(g, "Ysker", fb,
                    new Rectangle(16, 0, 58, Height), Pal.Btn,
                    TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            using (Font fs = new Font(Fonts.Family, 11f, FontStyle.Regular))
                TextRenderer.DrawText(g, "-文件校验", fs,
                    new Rectangle(72, 0, 90, Height), Pal.Text,
                    TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            using (Font fv = new Font(Fonts.Family, 9.5f, FontStyle.Regular))
                TextRenderer.DrawText(g, AppInfo.Version, fv,
                    new Rectangle(158, 0, 56, Height), Pal.Muted,
                    TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            for (int i = 0; i < 4; i++) DrawWinButton(g, i);
        }

        void DrawWinButton(Graphics g, int which)
        {
            Rectangle rc = BtnRect(which);
            Color bg = Pal.Bg;
            if (_pressed == which || _hover == which)
                bg = (which == 2) ? Pal.Del : Pal.WinBtn;
            // 置于顶层：开启时底色常亮，一眼看出状态
            if (which == 3 && TopMostOn && _pressed != which && _hover != which) bg = Pal.WinBtn;
            using (SolidBrush b = new SolidBrush(bg)) g.FillRectangle(b, rc);

            Color fg = Pal.Text;   // 默认白色
            int cx = rc.Left + rc.Width / 2;
            int cy = rc.Top + rc.Height / 2;
            using (Pen p = new Pen(fg, 1.4f))
            {
                if (which == 0)              // 最小化：一条横线
                {
                    g.DrawLine(p, cx - 6, cy + 4, cx + 6, cy + 4);
                }
                else if (which == 1)         // 最大化 / 还原
                {
                    if (_form.WindowState == FormWindowState.Maximized)
                    {
                        g.DrawRectangle(p, cx - 6, cy - 2, 8, 8);
                        g.DrawLine(p, cx - 4, cy - 4, cx + 4, cy - 4);
                        g.DrawLine(p, cx + 4, cy - 4, cx + 4, cy + 4);
                        g.DrawLine(p, cx + 2, cy + 4, cx + 4, cy + 4);
                    }
                    else
                    {
                        g.DrawRectangle(p, cx - 6, cy - 5, 12, 10);
                    }
                }
                else if (which == 3)         // 置于顶层：图钉（上方一个三角 + 一根针）
                {
                    g.DrawLine(p, cx - 5, cy - 6, cx + 5, cy - 6);   // 钉帽
                    g.DrawLine(p, cx - 5, cy - 6, cx - 2, cy + 1);   // 左侧收拢
                    g.DrawLine(p, cx + 5, cy - 6, cx + 2, cy + 1);
                    g.DrawLine(p, cx - 2, cy + 1, cx + 2, cy + 1);   // 底边
                    g.DrawLine(p, cx, cy + 1, cx, cy + 6);           // 针
                }
                else                         // 关闭：×
                {
                    g.DrawLine(p, cx - 6, cy - 5, cx + 6, cy + 5);
                    g.DrawLine(p, cx + 6, cy - 5, cx - 6, cy + 5);
                }
            }
        }

        /// <summary>「置于顶层」是否开启（由 MainForm 同步）</summary>
        public bool TopMostOn;

        /// <summary>自检用：标题栏按钮数量</summary>
        public int BtnCountPublic { get { return 4; } }

        /// <summary>自检用：直接点某个标题栏按钮</summary>
        public void ClickButtonPublic(int which) { PerformButton(which); }

        #endregion

        #region 交互

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int btn = HitButton(e.Location);
            if (btn >= 0)
            {
                _pressed = btn;
                Invalidate();
                return;
            }

            if (e.Button == MouseButtons.Left)
            {
                // 用系统原生拖动：最大化状态下会自动还原并跟随光标，还能贴边吸附
                ReleaseCapture();
                SendMessage(_form.Handle, 0xA1, (IntPtr)HTCAPTION, IntPtr.Zero);
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int btn = HitButton(e.Location);
            if (btn != _hover) { _hover = btn; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (_hover != -1) { _hover = -1; Invalidate(); }
            base.OnMouseLeave(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_pressed >= 0)
            {
                int btn = HitButton(e.Location);
                if (btn == _pressed) PerformButton(btn);
                _pressed = -1;
                Invalidate();
            }
            base.OnMouseUp(e);
        }

        /// <summary>执行某个标题栏按钮的动作</summary>
        void PerformButton(int btn)
        {
            if (btn == 0) _form.WindowState = FormWindowState.Minimized;
            else if (btn == 1) ToggleMax();
            else if (btn == 3) ToggleTopMost();
            else _form.Close();
        }

        /// <summary>切换「置于顶层」（默认关闭）</summary>
        public void ToggleTopMost()
        {
            TopMostOn = !TopMostOn;
            _form.TopMost = TopMostOn;
            Invalidate();
            if (TopMost != null) TopMost(this, EventArgs.Empty);
        }

        /// <summary>「置于顶层」状态变化通知（MainForm 用来更新提示）</summary>
        public event EventHandler TopMost;

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            if (HitButton(e.Location) < 0 && e.Button == MouseButtons.Left) ToggleMax();
            base.OnMouseDoubleClick(e);
        }

        public void ToggleMax()
        {
            _form.WindowState = (_form.WindowState == FormWindowState.Maximized)
                ? FormWindowState.Normal : FormWindowState.Maximized;
            Invalidate();
        }

        #endregion

        #region 无边框窗口的缩放 / 圆角

        /// <summary>把屏幕坐标翻译成 WM_NCHITTEST 的返回值，交给系统做缩放/拖动</summary>
        public static int HitTest(Form form, Point screenPt, TitleBar bar, ref bool onTitleBar)
        {
            Rectangle b = form.Bounds;
            bool left = screenPt.X < b.Left + BorderGrab;
            bool right = screenPt.X >= b.Right - BorderGrab;
            bool top = screenPt.Y < b.Top + BorderGrab;
            bool bottom = screenPt.Y >= b.Bottom - BorderGrab;

            if (form.WindowState == FormWindowState.Maximized)
            {
                left = right = top = bottom = false;
                if (bar != null && screenPt.Y <= bar.Height) onTitleBar = true;
                return HTCLIENT;
            }

            if (top && left) return HTTOPLEFT;
            if (top && right) return HTTOPRIGHT;
            if (bottom && left) return HTBOTTOMLEFT;
            if (bottom && right) return HTBOTTOMRIGHT;
            if (left) return HTLEFT;
            if (right) return HTRIGHT;
            if (top) return HTTOP;
            if (bottom) return HTBOTTOM;

            if (bar != null && screenPt.Y <= b.Top + bar.Height) onTitleBar = true;
            return HTCLIENT;
        }

        /// <summary>Win11 的圆角与深色边框；老系统上直接忽略</summary>
        public static void ApplyModernFrame(Form form)
        {
            try
            {
                int round = 2;   // DWMWCP_ROUND
                DwmSetWindowAttribute(form.Handle, 33 /*DWMWA_WINDOW_CORNER_PREFERENCE*/, ref round, sizeof(int));
                int dark = 1;    // 深色标题栏（最大化时露出的边条）
                DwmSetWindowAttribute(form.Handle, 20 /*DWMWA_USE_IMMERSIVE_DARK_MODE*/, ref dark, sizeof(int));
                // 消除无边框窗口最大化时多出来的 8px 边框
                int nc = 1;
                DwmSetWindowAttribute(form.Handle, 2 /*DWMWA_NCRENDERING_POLICY*/, ref nc, sizeof(int));
            }
            catch { }
        }

        #endregion
    }
}
