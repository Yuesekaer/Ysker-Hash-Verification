// Ysker-Hash-Verification - batch file checksum utility (MD5 / SHA1 / CRC32 / SHA-256)
// Copyright (c) 2026 Yuesekaer
// SPDX-License-Identifier: MIT
// See the LICENSE file in the repository root for the full license text.

// 像素级核对：把卡片画到位图，找出哈希值行的实际文字起点/字宽，
// 与 FilePanel 计算出的选中色块坐标对比，确认是否错位。
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Ysker
{
    internal static class PixProbe
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string outFile = args.Length > 0 ? args[0] : "pix.txt";
            StringBuilder sb = new StringBuilder();

            UiFonts F = new UiFonts(10.5f, 10f);
            int keyW = CardRenderer.MeasureKeyWidth(F, new string[] { "MD5：", "SHA1：", "CRC32：", "SHA-256：" });
            CardRenderer R = new CardRenderer(F, keyW);
            sb.AppendLine("KeyW = " + keyW);
            sb.AppendLine("MonoBig = " + F.MonoBig.FontFamily.Name + " " + F.MonoBig.Size + "pt");

            // 行矩形（模拟卡片内第 0 行）
            int cardTop = 20, cardLeft = 20, cardW = 900;
            Rectangle card = new Rectangle(cardLeft, cardTop, cardW, 200);
            Rectangle lr = CardRenderer.LineRect(card, 0);
            sb.AppendLine("LineRect(0) = " + lr);

            string key = "MD5：";
            string val = "B461BF34A9B05E34D82ED8D25EA059F6";

            // 1) 用与真机完全相同的方式绘制（GDI TextRenderer，两个列矩形）
            using (Bitmap bmp = new Bitmap(cardW + 40, 260))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Pal.CardBg);
                    R.Txt(g, key, F.MonoBig, Pal.Text, new Rectangle(lr.Left, lr.Top, keyW, lr.Height));
                    R.Txt(g, val, F.MonoBig, Pal.Text, new Rectangle(lr.Left + keyW, lr.Top, lr.Width - keyW, lr.Height));
                }
                // 2) 在行内逐列扫描：找出第一个"亮"像素（文字起点）
                int textLeft = -1;
                for (int x = lr.Left; x < lr.Left + lr.Width && x < bmp.Width; x++)
                {
                    bool lit = false;
                    for (int y = lr.Top; y < lr.Bottom && y < bmp.Height; y++)
                    {
                        Color c = bmp.GetPixel(x, y);
                        if (c.R > 110 && c.G > 110 && c.B > 110) { lit = true; break; }
                    }
                    if (lit) { textLeft = x; break; }
                }
                sb.AppendLine("整行第一个亮像素 x = " + textLeft + "  （相对行左 +" + (textLeft - lr.Left) + "）");

                // 键列里：找出键文字的实际起点与结尾
                int keyFirst = -1, keyLast = -1;
                for (int x = lr.Left; x < lr.Left + keyW; x++)
                {
                    bool lit = false;
                    for (int y = lr.Top; y < lr.Bottom; y++)
                    {
                        Color c = bmp.GetPixel(x, y);
                        if (c.R > 110 && c.G > 110 && c.B > 110) { lit = true; break; }
                    }
                    if (lit) { if (keyFirst < 0) keyFirst = x; keyLast = x; }
                }
                sb.AppendLine("键列文字：first=" + keyFirst + "  last=" + keyLast +
                              "  （相对行左 " + (keyFirst - lr.Left) + " .. " + (keyLast - lr.Left) + "）");

                // 值列里：找出值的实际起点
                int valFirst = -1;
                for (int x = lr.Left + keyW; x < lr.Left + lr.Width; x++)
                {
                    bool lit = false;
                    for (int y = lr.Top; y < lr.Bottom; y++)
                    {
                        Color c = bmp.GetPixel(x, y);
                        if (c.R > 110 && c.G > 110 && c.B > 110) { lit = true; break; }
                    }
                    if (lit) { valFirst = x; break; }
                }
                sb.AppendLine("值列第一个亮像素 x = " + valFirst +
                              "  （相对值列左 +" + (valFirst - (lr.Left + keyW)) + "）");
                bmp.Save(Path.ChangeExtension(outFile, ".png"), System.Drawing.Imaging.ImageFormat.Png);
            }

            // 3) 各种字宽测量方式对比
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
            {
                Size s1 = TextRenderer.MeasureText(g, val, F.MonoBig, new Size(int.MaxValue, int.MaxValue),
                    TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                Size s2 = TextRenderer.MeasureText(val, F.MonoBig);
                Size s10 = TextRenderer.MeasureText(g, "0000000000", F.MonoBig, new Size(int.MaxValue, int.MaxValue),
                    TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                float monoW = g.MeasureString("0", F.MonoBig, int.MaxValue, StringFormat.GenericTypographic).Width;
                sb.AppendLine();
                sb.AppendLine("TextRenderer(NoPadding) 32 位 hex 宽 = " + s1.Width + "  => 每字符 " + (s1.Width / 32.0).ToString("0.###"));
                sb.AppendLine("TextRenderer(默认)      32 位 hex 宽 = " + s2.Width);
                sb.AppendLine("TextRenderer(NoPadding) \"0000000000\" 宽 = " + s10.Width + "  => 每字符 " + (s10.Width / 10.0).ToString("0.###"));
                sb.AppendLine("GDI+ MeasureString(\"0\") 宽 = " + monoW.ToString("0.###"));
                Size ksz = TextRenderer.MeasureText(g, key, F.MonoBig, new Size(int.MaxValue, int.MaxValue),
                    TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                sb.AppendLine("键 \"MD5：\" TextRenderer(NoPadding) 宽 = " + ksz.Width + "（KeyW=" + keyW + "）");
            }

            File.WriteAllText(outFile, sb.ToString(), Encoding.UTF8);
            Console.WriteLine(sb.ToString());
        }
    }
}
