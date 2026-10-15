// Ysker-Hash-Verification - batch file checksum utility (MD5 / SHA1 / CRC32 / SHA-256)
// Copyright (c) 2026 Yuesekaer
// SPDX-License-Identifier: MIT
// See the LICENSE file in the repository root for the full license text.

// 设计预览：把整个界面用同一个渲染内核直接画到位图上。
// 说明：WinForms 自带的 DrawToBitmap 抓 UserPaint 自绘面板会返回空白（已用像素实验确认），
// 所以这里不用真机截图，而是“按同一套配色常量 + 同一个 CardRenderer 直接绘制”，
// 在没有交互桌面的环境里也能核对排版与配色。
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Ysker
{
    internal static class PreviewShot
    {
        const int W = 1240, H = 740;
        const int TitleH = 40, BarH = 0, BotH = 34, SideW = 330, StripH = 44;
        const int BtnW = 46;

        static UiFonts F;
        static CardRenderer R;

        public static void Draw(string outDir)
        {
            Directory.CreateDirectory(outDir);
            F = new UiFonts(10.5f, 10f);
            R = new CardRenderer(F, CardRenderer.MeasureKeyWidth(F, new string[] { "MD5：", "SHA1：", "CRC32：", "SHA-256：" }));

            var demo = DemoData();

            Shot(Path.Combine(outDir, "preview-01-all.png"), demo, Algo.All, true, null, -1, -1f);
            Shot(Path.Combine(outDir, "preview-02-md5-only.png"), demo, new string[] { Algo.MD5 }, false, null, -1, -1f);
            // 0.5 秒正好是最绿的一帧
            Shot(Path.Combine(outDir, "preview-03-flash.png"), demo, Algo.All, true, demo[0].Hashes[Algo.MD5], -1, 2.3f);
            Shot(Path.Combine(outDir, "preview-04-none-checked.png"), new List<FileItem>(), new string[] { }, true, null, -1, -1f);
            Shot(Path.Combine(outDir, "preview-05-progress.png"), demo, Algo.All, true, null, 42, -1f);
            Shot(Path.Combine(outDir, "preview-06-titlebar-hover.png"), demo, Algo.All, true, null, -1, -1f, 1);
            Shot(Path.Combine(outDir, "preview-07-titlebar-close.png"), demo, Algo.All, true, null, -1, -1f, 2);
        }

        #region 演示数据

        static List<FileItem> DemoData()
        {
            var list = new List<FileItem>();
            list.Add(Make(@"C:\Game\MOD\Patch_A.ysker", "1c3c5960ac771ed7e715524b607dde39",
                "d35ec8b94f542a0428d34f601c08dd1c5486f03d", "ebe75a4"));
            list.Add(Make(@"C:\Game\MOD\Patch_A_copy.ysker", "1c3c5960ac771ed7e715524b607dde39",
                "d35ec8b94f542a0428d34f601c08dd1c5486f03d", "ebe75a4"));
            list.Add(Make(@"C:\Game\MOD\DemoApp.exe", "11256b010411e9723bc5e37098e3fae3",
                "8b96522a122edb67b1083a9b3a3c61e11a8c6adb5", "f124c923"));
            list.Add(Make(@"C:\Game\MOD\readme.txt", "855ea08dc9ea3e4ec94c4c7fe4046b32",
                "180e6e4693c8efb4d5b1f46270767238bbf5df94", "5a9c05d9"));
            list.Add(Make(@"C:\Game\MOD\voice.bank", "6cd91c0f2e4c15a1e31a2d0b9f7e2214",
                "3a1f7c9c1b0e4d8a7f2c6b5e9d0a4f8c2b7e6d1a", "7d21e0aa"));
            return list;
        }

        static FileItem Make(string path, string md5, string sha1, string crc)
        {
            var f = new FileItem();
            f.Path = path;
            f.Size = 4198 + path.Length * 37;
            f.Computed = true;
            f.Hashes[Algo.MD5] = md5;
            f.Hashes[Algo.SHA1] = sha1;
            f.Hashes[Algo.CRC32] = crc;
            f.Hashes[Algo.SHA256] = Sha256Stub(path);
            return f;
        }

        static string Sha256Stub(string seed)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes("Ysker-preview-" + seed));
                StringBuilder sb = new StringBuilder();
                foreach (byte b in h) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        #endregion

        static void Shot(string path, List<FileItem> files, string[] keys, bool upper, string compareValue,
                         int busyPercent, float flashTime, int winBtnHover = -1)
        {
            using (Bitmap bmp = new Bitmap(W, H))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                g.SmoothingMode = SmoothingMode.None;
                using (SolidBrush b = new SolidBrush(Pal.Bg)) g.FillRectangle(b, 0, 0, W, H);

                int listW = W - SideW;   // 侧栏占满右侧通高：标题栏/状态栏只画在侧栏左边
                DrawTitleBar(g, winBtnHover, listW);
                DrawToolbar(g);
                DrawStatus(g, keys, files, busyPercent, W);   // 状态栏横跨整窗（与真机一致）

                int listTop = TitleH + BarH;
                int listH = H - listTop - BotH - StripH;
                DrawList(g, new Rectangle(0, listTop, listW, listH), files, keys, upper, compareValue, busyPercent, flashTime);
                DrawSidebar(g, new Rectangle(listW, TitleH, SideW, H - TitleH - BotH - StripH), keys, compareValue, upper);    // 侧栏只占中间
                DrawStrip(g, listW);

                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        /// <summary>文件操作条：五个按钮横排，宽度按可用宽度动态分配（与真机 LayoutStrip 同一算法）</summary>
        static void DrawStrip(Graphics g, int width)
        {
            int top = H - BotH - StripH;
            using (SolidBrush b = new SolidBrush(Pal.Bg)) g.FillRectangle(b, 0, top, width, StripH);
            using (Pen p = new Pen(Pal.Border)) g.DrawLine(p, 12, top, width - 12, top);

            string[] labels = { "选择文件", "选择文件夹", "清空列表", "开 始 校 验" };
            bool[] primary = { true, true, false, true };
            float[] sizes = { 9.5f, 9.5f, 9.5f, 11f };
            bool[] bold = { false, false, false, true };

            int gap = 8, totalGap = gap * (labels.Length - 1);
            int avail = width - 24, need = 0;
            int[] wt = new int[labels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                using (Font f = Fonts.Ui(sizes[i], bold[i] ? FontStyle.Bold : FontStyle.Regular))
                {
                    wt[i] = Math.Max(40, TextRenderer.MeasureText(labels[i], f).Width + 16);
                    need += wt[i];
                }
            }
            int room = Math.Max(0, avail - totalGap);
            int x = 12;
            for (int i = 0; i < labels.Length; i++)
            {
                int bw = (int)((long)room * wt[i] / need);
                if (bw < 40) bw = 40;
                if (i == labels.Length - 1) bw = Math.Max(40, width - 12 - x);
                var rc = new Rectangle(x, top + 6, bw, StripH - 12);
                using (SolidBrush b = new SolidBrush(primary[i] ? Pal.Btn : Pal.PanelBg)) g.FillRectangle(b, rc);
                if (!primary[i])
                    using (Pen p2 = new Pen(Pal.Border)) g.DrawRectangle(p2, rc.Left, rc.Top, rc.Width - 1, rc.Height - 1);
                using (Font f = Fonts.Ui(sizes[i], bold[i] ? FontStyle.Bold : FontStyle.Regular))
                    TextRenderer.DrawText(g, labels[i], f, rc, Pal.Text,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                x += bw + gap;
            }
        }
        #region 标题栏（默认白色，最小化/最大化悬停 #2eaf3d，关闭悬停 #c83c26）

        static void DrawTitleBar(Graphics g, int hover, int width)
        {
            using (SolidBrush b = new SolidBrush(Pal.Bg)) g.FillRectangle(b, 0, 0, W, TitleH);

            using (Font fb = new Font(Fonts.Family, 15f, FontStyle.Bold))
                Txt(g, "Ysker", fb, Pal.Btn, 16, 0, 58, TitleH, false, true);
            using (Font f2 = new Font(Fonts.Family, 11f, FontStyle.Regular))
                Txt(g, "-文件校验", f2, Pal.Text, 72, 0, 90, TitleH, false, true);
            Txt(g, AppInfo.Version, Fonts.Ui(9.5f, FontStyle.Regular), Pal.Muted, 158, 0, 60, TitleH, false, true);

            for (int i = 0; i < 4; i++)
            {
                var rc = new Rectangle(width - 8 - BtnW * (4 - i), 0, BtnW, TitleH);
                if (hover == i)
                    using (SolidBrush b = new SolidBrush(i == 2 ? Pal.Del : Pal.WinBtn)) g.FillRectangle(b, rc);

                int cx = rc.Left + rc.Width / 2, cy = rc.Top + rc.Height / 2;
                using (Pen p = new Pen(Pal.Text, 1.6f))
                {
                    if (i == 0) g.DrawLine(p, cx - 7, cy + 4, cx + 7, cy + 4);            // 最小化
                    else if (i == 1) g.DrawRectangle(p, cx - 7, cy - 6, 14, 12);          // 最大化
                    else if (i == 3)                                                      // 置于顶层（图钉）
                    {
                        g.DrawLine(p, cx - 6, cy - 7, cx + 6, cy - 7);
                        g.DrawLine(p, cx - 6, cy - 7, cx - 2, cy + 1);
                        g.DrawLine(p, cx + 6, cy - 7, cx + 2, cy + 1);
                        g.DrawLine(p, cx - 2, cy + 1, cx + 2, cy + 1);
                        g.DrawLine(p, cx, cy + 1, cx, cy + 7);
                    }
                    else { g.DrawLine(p, cx - 7, cy - 6, cx + 7, cy + 6); g.DrawLine(p, cx + 7, cy - 6, cx - 7, cy + 6); }   // 关闭
                }
            }
        }

        #endregion

        #region 工具条

        static void DrawToolbar(Graphics g)
        {
            // 顶部不再有任何按钮，只留一行只读状态摘要
            int y = TitleH;
            // 顶部不再有状态行：所有状态都在底部状态栏
        }

        #endregion

        #region 状态栏

        static void DrawStatus(Graphics g, string[] keys, List<FileItem> files, int busyPercent, int width)
        {
            int y = H - BotH;
            using (SolidBrush b = new SolidBrush(Pal.PanelBg)) g.FillRectangle(b, 0, y, width, BotH);
            using (Pen p = new Pen(Pal.Border)) g.DrawLine(p, 0, y, width, y);

            string left;
            bool warn = false;
            if (busyPercent >= 0) left = "正在校验中...  " + busyPercent + "%";
            else if (keys.Length == 0) { left = AppInfo.HintNoneChecked; warn = true; }
            else if (files.Count >= 2 && files[0].Hashes[Algo.MD5] == files[1].Hashes[Algo.MD5])
                left = files[0].Path + " 与 " + files[1].Path + " 一致";
            else left = "校验完成：" + files.Count + " 个文件（全部正常）    显示项：" + string.Join(" / ", keys);

            Txt(g, left, F.Ui, warn ? Color.FromArgb(0xFF, 0x6B, 0x5C) : Pal.Muted, 14, y, width - 310, BotH, false, true);

            int done = 0; foreach (FileItem f in files) if (f.Computed) done++;
            string algo = keys.Length == 0 ? "未选校验值" : string.Join("+", keys);
            string right = files.Count + " 文件 ｜ 已校验 " + done + " ｜ 选中 0 ｜ " + algo + " ｜ 大写 ｜ 框选：关   ";
            Txt(g, right, F.Small, Pal.Muted, width - 290, y, 280, BotH, true, true);
        }

        #endregion

        #region 卡片列表

        static void DrawList(Graphics g, Rectangle area, List<FileItem> files, string[] keys, bool upper,
                             string compareValue, int busyPercent, float flashTime)
        {
            using (SolidBrush b = new SolidBrush(Pal.Bg)) g.FillRectangle(b, area);
            int y = area.Top + 6;

            if (busyPercent >= 0)
            {
                Rectangle br = new Rectangle(10, y, area.Width - 26, 26);
                using (SolidBrush b = new SolidBrush(Pal.PanelBg)) g.FillRectangle(b, br);
                using (Pen p = new Pen(Pal.Btn)) g.DrawRectangle(p, br.Left, br.Top, br.Width - 1, br.Height - 1);
                Txt(g, "  正在校验中...  " + busyPercent + "%   (2/5)  DemoApp.exe", F.Ui, Pal.Text,
                    br.Left + 4, br.Top, br.Width - 8, br.Height, false, true);
                y += 30;
            }

            if (keys.Length == 0)
            {
                Center(g, "把文件 / 文件夹拖到这里吧 (｡･ω･｡)", F.Ui, new SolidBrush(Pal.Muted),
                    new Rectangle(area.Left, area.Top + area.Height / 2 - 42, area.Width, 30));
                Center(g, "也可以点上面的「选择文件」「选择文件夹」", Fonts.Ui(9f, FontStyle.Regular),
                    new SolidBrush(Color.FromArgb(0x77, 0x77, 0x77)),
                    new Rectangle(area.Left, area.Top + area.Height / 2 - 14, area.Width, 26));
                return;
            }

            int cardW = area.Width - 16 - SystemInformation.VerticalScrollBarWidth;

            var order = new List<object>();
            if (files.Count >= 2 && files[0].Hashes[Algo.MD5] == files[1].Hashes[Algo.MD5])
            {
                order.Add(files[0]); order.Add(files[1]);
                order.Add("———  以上 2 个文件完全一致（" + string.Join("+", keys) + "）  ———");
                for (int i = 2; i < files.Count; i++) order.Add(files[i]);
            }
            else foreach (FileItem f in files) order.Add(f);

            foreach (object o in order)
            {
                if (o is string)
                {
                    var rc = new Rectangle(8, y, cardW, CardRenderer.DividerHeight);
                    var dv = new CardView(); dv.IsDivider = true; dv.DividerText = (string)o;
                    R.DrawCard(g, rc, dv);
                    y += rc.Height + 6;
                    continue;
                }

                var fi = (FileItem)o;
                var card = new Rectangle(8, y, cardW, CardRenderer.CardHeight(keys.Length));
                var v = new CardView();
                v.Path = fi.Path;
                v.SizeText = fi.SizeText;
                v.Computed = true;
                v.Uppercase = upper;
                for (int k = 0; k < keys.Length; k++)
                {
                    string h;
                    if (!fi.Hashes.TryGetValue(keys[k], out h)) continue;
                    v.Keys.Add(Algo.Key(keys[k]));
                    v.Values.Add(h);
                    v.Flash.Add(0f);
                }
                if (flashTime >= 0f)
                {
                    // 预览某一帧：每行依次亮（行间相隔 1.24 秒 = 0.3 亮 + 0.6 保持 + 0.3 灭 + 0.04 间隔），
                    // 默认展示“第三行刚亮到最绿”的那一帧，能同时看到亮/半亮/已灭。
                    for (int k = 0; k < keys.Length; k++)
                    {
                        if (k >= v.Flash.Count) break;
                        v.Flash[k] = CardRenderer.RampIntensity(flashTime - k * 1.24f);
                    }
                    R.DrawCard(g, card, v, Pal.Flash, 0f, -1);
                }
                else R.DrawCard(g, card, v);
                DrawCardButtons(g, card, true);
                y += card.Height + 6;
            }

            Rectangle track = new Rectangle(area.Right - SystemInformation.VerticalScrollBarWidth,
                area.Top, SystemInformation.VerticalScrollBarWidth, area.Height);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(0xF0, 0xF0, 0xF0))) g.FillRectangle(b, track);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(0xCD, 0xCD, 0xCD)))
                g.FillRectangle(b, new Rectangle(track.Left + 1, track.Top + 2, track.Width - 2, 90));
        }

        static void DrawCardButtons(Graphics g, Rectangle rc, bool computed)
        {
            Rectangle dr = CardRenderer.DelRect(rc);
            using (SolidBrush b = new SolidBrush(Pal.Del)) g.FillRectangle(b, dr);
            Center(g, "×", F.Char, new SolidBrush(Pal.Text), dr);

            if (computed)
            {
                Rectangle cr = CardRenderer.CopyRect(rc);
                using (SolidBrush b = new SolidBrush(Pal.Btn)) g.FillRectangle(b, cr);
                Center(g, "复制", F.Small, new SolidBrush(Pal.Text), cr);
            }
        }

        #endregion

        #region 侧栏

        static void DrawSidebar(Graphics g, Rectangle area, string[] keys, string compareValue, bool upper)
        {
            using (SolidBrush b = new SolidBrush(Pal.Bg)) g.FillRectangle(b, area);

            int x = area.Left + 12;
            int w = SideW - 24;
            int y = area.Top + 10;

            // ---- 校验值选择：2x2 ----
            SideTitle(g, "校验值选择", x, y); y += 24;
            Para(g, "默认只勾 MD5；取消勾选，对应的那一行会立刻消失。", x + 2, y, w - 4); y += 22;
            int halfW = (w - 6) / 2;
            Toggle(g, Algo.MD5, x, y, halfW, Has(keys, Algo.MD5));
            Toggle(g, Algo.SHA1, x + halfW + 6, y, w - halfW - 6, Has(keys, Algo.SHA1));
            y += 34;
            Toggle(g, Algo.CRC32, x, y, halfW, Has(keys, Algo.CRC32));
            Toggle(g, Algo.SHA256, x + halfW + 6, y, w - halfW - 6, Has(keys, Algo.SHA256));
            y += 34;
            using (SolidBrush b = new SolidBrush(Pal.Border)) g.FillRectangle(b, x + 2, y, w - 4, 1); y += 11;

            // ---- 待对比值 ----
            SideTitle(g, "待对比值", x, y); y += 24;
            var box = new Rectangle(x, y, w, 31);
            using (SolidBrush b = new SolidBrush(Pal.InputBg)) g.FillRectangle(b, box);
            using (Pen p = new Pen(Pal.Border)) g.DrawRectangle(p, box.Left, box.Top, box.Width - 1, box.Height - 1);
            using (Font cf = new Font(UiFonts.Find("Consolas", "Courier New"), 9.5f))
                Txt(g, string.IsNullOrEmpty(compareValue) ? "粘贴一个值：MD5 / SHA1 / CRC32 / SHA-256" : compareValue,
                    cf, string.IsNullOrEmpty(compareValue) ? Color.FromArgb(0x80, 0x80, 0x80) : Pal.Text,
                    box.Left + 7, box.Top, box.Width - 12, box.Height, false, true);
            y += 35;
            Btn(g, "比  对", new Rectangle(x, y, w, 33), true); y += 36;
            using (SolidBrush b = new SolidBrush(Pal.Border)) g.FillRectangle(b, x + 2, y, w - 4, 1); y += 11;

            // ---- 显示 ----
            SideTitle(g, "显示", x, y); y += 24;
            Btn(g, "复制选中哈希值", new Rectangle(x, y, w, 32), true); y += 36;
            Btn(g, "复制全部哈希值", new Rectangle(x, y, w, 30), true); y += 34;
            Btn(g, "导出结果为 txt", new Rectangle(x, y, w, 30), false); y += 34;
            // 同文件分类（默认勾选：绿底 √）
            using (SolidBrush b = new SolidBrush(Pal.BoxOn)) g.FillRectangle(b, x, y, w, 30);
            using (Font f3 = Fonts.Ui(9.5f, FontStyle.Regular))
                TextRenderer.DrawText(g, "√  同文件分类（一致时分组）", f3, new Rectangle(x, y, w, 30), Pal.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            y += 34;
            Btn(g, "大小写：" + (upper ? "大写" : "小写"), new Rectangle(x, y, w, 30), false); y += 34;
            using (SolidBrush b = new SolidBrush(Pal.Border)) g.FillRectangle(b, x + 2, y, w - 4, 1); y += 11;

            // 框选开关在侧栏（点一下即多选）
            using (SolidBrush b = new SolidBrush(Pal.PanelBg)) g.FillRectangle(b, x, y, w, 30);
            using (Pen p = new Pen(Pal.Border)) g.DrawRectangle(p, x, y, w - 1, 29);
            using (Font f2 = Fonts.Ui(9.5f, FontStyle.Regular))
                TextRenderer.DrawText(g, "框选（点一下即多选）", f2, new Rectangle(x, y, w, 30), Pal.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            y += 34;
            using (SolidBrush b3 = new SolidBrush(Pal.Border)) g.FillRectangle(b3, x + 2, y, w - 4, 1); y += 11;

            Para(g,
                "小技巧：" + "\r\n" +
                "· 卡片上的「×」移出该文件、「复制」只复制这一个" + "\r\n" +
                "· 框选开启后点卡片即多选，Delete 批量移除" + "\r\n" +
                "· 框选关闭时在哈希值上横向划过即可选中，Ctrl+C 复制" + "\r\n" +
                "· 拖文件/文件夹进窗口即可添加；F5 重新校验" + "\r\n" +
                " 本工具制作：Yuesekaer ",
                x + 2, y, w - 4);
        }

        static bool Has(string[] keys, string k)
        {
            foreach (string s in keys) if (s == k) return true;
            return false;
        }

        #endregion

        #region 基础绘制

        static void Txt(Graphics g, string s, Font f, Color c, int x, int y, int w, int h, bool right, bool vcenter)
        {
            TextFormatFlags fl = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
            if (right) fl |= TextFormatFlags.Right;
            if (vcenter) fl |= TextFormatFlags.VerticalCenter;
            TextRenderer.DrawText(g, s, f, new Rectangle(x, y, w, h), c, fl);
        }

        static void Txt(Graphics g, string s, Font f, Color c, int x, int y)
        {
            TextRenderer.DrawText(g, s, f, new Point(x, y), c, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }

        static void Center(Graphics g, string s, Font f, SolidBrush b, Rectangle rc)
        {
            TextRenderer.DrawText(g, s, f, rc, b.Color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }

        static void Btn(Graphics g, string text, Rectangle rc, bool primary)
        {
            using (SolidBrush b = new SolidBrush(primary ? Pal.Btn : Pal.PanelBg)) g.FillRectangle(b, rc);
            if (!primary)
                using (Pen p = new Pen(Pal.Border)) g.DrawRectangle(p, rc.Left, rc.Top, rc.Width - 1, rc.Height - 1);
            TextRenderer.DrawText(g, text, F.Ui, rc, Pal.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }

        static void Toggle(Graphics g, string text, int x, int y, int w, bool on)
        {
            var rc = new Rectangle(x, y, w, 34);
            using (SolidBrush b = new SolidBrush(on ? Pal.Btn : Pal.PanelBg)) g.FillRectangle(b, rc);
            using (Pen p = new Pen(on ? Pal.Btn : Pal.Border)) g.DrawRectangle(p, rc.Left, rc.Top, rc.Width - 1, rc.Height - 1);
            TextRenderer.DrawText(g, on ? "√  " + text : text, F.Ui, rc, on ? Pal.Text : Pal.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }

        static void Rect(Graphics g, Rectangle rc, Brush fill, Color line, float lw)
        {
            if (fill != null) g.FillRectangle(fill, rc);
            if (lw > 0)
                using (Pen p = new Pen(line, lw)) g.DrawRectangle(p, rc.Left, rc.Top, rc.Width - 1, rc.Height - 1);
        }

        static void SideTitle(Graphics g, string text, int x, int y)
        {
            Txt(g, text, Fonts.Ui(11.5f, FontStyle.Bold), Pal.Btn, x, y);
        }

        static void Para(Graphics g, string text, int x, int y, int w)
        {
            TextRenderer.DrawText(g, text, F.Small, new Rectangle(x, y, w, 200), Pal.Muted,
                TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak);
        }

        #endregion
    }
}
