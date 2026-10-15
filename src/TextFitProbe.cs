// Ysker-Hash-Verification - batch file checksum utility (MD5 / SHA1 / CRC32 / SHA-256)
// Copyright (c) 2026 Yuesekaer
// SPDX-License-Identifier: MIT
// See the LICENSE file in the repository root for the full license text.

// 几何探针：在若干窗口尺寸下，检查"文字随窗口缩放"是否生效、
// 以及 SHA-256（64 字符）在卡片里是否放得下。只用于诊断，不参与正式构建。
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Ysker
{
    internal static class TextFitProbe
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Env.EnableDarkMode();
            string outFile = args.Length > 0 ? args[0] : "textfit.txt";
            StringBuilder sb = new StringBuilder();

            MainForm f = new MainForm();
            f.Show();
            Application.DoEvents();

            int[] widths = { 1600, 1240, 1100, 1000 };
            int[] heights = { 900, 740, 650, 580 };
            for (int i = 0; i < widths.Length; i++)
            {
                f.ClientSize = new Size(widths[i], heights[i]);
                f.ClientSize = new Size(widths[i], heights[i]); f.PerformLayout();
                Application.DoEvents();

                FilePanel p = f.PanelPublic;
                int cardW = p.Width - 16 - SystemInformation.VerticalScrollBarWidth;
                int valueW = cardW - 12 - 8 - p.Renderer.KeyW - 6;   // 卡片内边距 + 键列 + 右侧留白
                int hashW = TextRenderer.MeasureText(new string('0', 64), p.Renderer.F.MonoBig,
                    new Size(int.MaxValue, int.MaxValue),
                    TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding).Width;

                sb.AppendLine(string.Format("窗口 {0}x{1}  列表宽={2}  字号={3}  行高={4}  卡片高={5}  值列宽={6}  64字符宽={7}  {8}",
                    widths[i], heights[i], p.Width, f.FontSizePublic.ToString("0.##"),
                    p.Renderer.LineHeight, p.Renderer.CurCardHeight(4), valueW, hashW,
                    hashW <= valueW ? "OK 放得下" : "!! 会被截断"));
            }

            // 长路径：文件名是否完整保留
            sb.AppendLine();
            sb.AppendLine("---- 长路径文件名的保留情况 ----");
            string longPath = @"C:\Users\Public\AppData\Local\Temp\Shooter\Modern Warships\LangSource__WarFlames.loc";
            int nameW = widths[3] - 2 * 330 - 0;   // 只作展示
            FilePanel pp = f.PanelPublic;
            int cw = pp.Width - 16 - SystemInformation.VerticalScrollBarWidth;
            Rectangle card = new Rectangle(8, 0, cw, 120);
            int avail = CardRenderer.NameWidth(card);
            sb.AppendLine("available name width = " + avail);
            using (Bitmap bmp = new Bitmap(Math.Max(50, card.Width), 140))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                pRendererDraw(g, longPath, pp, card, sb);
            }

            File.WriteAllText(outFile, sb.ToString(), Encoding.UTF8);
            Console.WriteLine(sb.ToString());
            f.Close();
            Application.Exit();
        }

        static void pRendererDraw(Graphics g, string path, FilePanel p, Rectangle card, StringBuilder sb)
        {
            int nl = path.LastIndexOf('\\');
            string dir = nl >= 0 ? path.Substring(0, nl) : "";
            string file = nl >= 0 ? path.Substring(nl + 1) : path;
            int rcW = CardRenderer.NameWidth(card);
            Font font = p.Renderer.F.UiBold;
            int fileW = TextRenderer.MeasureText(g, file, font, new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding).Width;
            sb.AppendLine("文件名 = " + file + "  文件名宽 = " + fileW + "  " + (fileW <= rcW ? "OK 文件名能完整显示" : "!! 文件名放不下"));
            sb.AppendLine("目录  = " + dir);
        }
    }
}
