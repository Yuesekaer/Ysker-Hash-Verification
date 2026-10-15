// Ysker-Hash-Verification - batch file checksum utility (MD5 / SHA1 / CRC32 / SHA-256)
// Copyright (c) 2026 Yuesekaer
// SPDX-License-Identifier: MIT
// See the LICENSE file in the repository root for the full license text.

// 运行期布局探针（只用于诊断，不参与正式构建）
using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Ysker
{
    internal static class LayoutProbe
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Run(args.Length > 0 ? args[0] : "layout.txt");
        }

        public static void Run(string outFile)
        {
            StringBuilder sb = new StringBuilder();
            MainForm f = new MainForm();
            // 允许指定窗口尺寸（模拟全屏/缩放后的真机）
            string[] parts = Environment.GetCommandLineArgs();
            if (parts.Length >= 4)
            {
                int w, h;
                if (int.TryParse(parts[2], out w) && int.TryParse(parts[3], out h))
                    f.ClientSize = new Size(w, h);
            }
            f.Show();
            Application.DoEvents();
            sb.AppendLine("请求后 ClientSize = " + f.ClientSize);
            sb.AppendLine("LayoutShell 被调用次数 = " + MainForm.LayoutShellCalls);
            sb.AppendLine("---- LayoutShell 轨迹 ----");
            foreach (string s in MainForm.DiagLog) sb.AppendLine("  " + s);

            sb.AppendLine("ClientSize = " + f.ClientSize);
            Dump(sb, "TitleBar", f.Controls["__none__"] as Control);
            foreach (Control c in f.Controls)
            {
                sb.AppendLine(string.Format("  控件 {0,-22} Type={1,-18} Bounds={2} Visible={3} Dock={4}",
                    c.Name == "" ? "(无名)" : c.Name, c.GetType().Name, c.Bounds, c.Visible, c.Dock));
            }
            sb.AppendLine();
            sb.AppendLine("---- 关键字段 ----");
            DumpField(sb, "f._titleBar", GetField(f, "_titleBar"));
            DumpField(sb, "f._sidePanel", GetField(f, "_sidePanel"));
            DumpField(sb, "f._bottomPanel", GetField(f, "_bottomPanel"));
            DumpField(sb, "f._btnStrip", GetField(f, "_btnStrip"));
            DumpField(sb, "f._panel", GetField(f, "_panel"));

            Control strip = GetField(f, "_btnStrip");
            if (strip != null)
            {
                sb.AppendLine();
                sb.AppendLine("---- 操作条子控件 ----");
                foreach (Control c in strip.Controls)
                    sb.AppendLine(string.Format("  {0,-10} Bounds={1} Text=\"{2}\"", c.GetType().Name, c.Bounds, c.Text));
                sb.AppendLine("  strip.ClientSize = " + strip.ClientSize + "  strip.Parent = " + (strip.Parent == null ? "null" : strip.Parent.GetType().Name));
            }

            // 强制再摆一次，看看有没有变化
            f.LayoutShellPublic();
            Application.DoEvents();
            sb.AppendLine();
            sb.AppendLine("---- 调用 LayoutShellPublic() 之后 ----");
            DumpField(sb, "f._btnStrip", GetField(f, "_btnStrip"));
            if (strip != null)
                foreach (Control c in strip.Controls)
                    sb.AppendLine(string.Format("  {0,-10} Bounds={1}", c.GetType().Name, c.Bounds));

            // 试着抓一张图（UserPaint 面板可能是空白，但能看出位置）
            try
            {
                using (Bitmap bmp = new Bitmap(f.ClientSize.Width, f.ClientSize.Height))
                {
                    f.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                    bmp.Save(Path.ChangeExtension(outFile, ".png"), System.Drawing.Imaging.ImageFormat.Png);
                    sb.AppendLine("已保存截图 " + Path.ChangeExtension(outFile, ".png"));
                }
            }
            catch (Exception ex) { sb.AppendLine("截图失败：" + ex.Message); }

            File.WriteAllText(outFile, sb.ToString(), Encoding.UTF8);
            f.Close();
            Application.Exit();
        }

        static Control GetField(MainForm f, string name)
        {
            System.Reflection.FieldInfo fi = typeof(MainForm).GetField(name,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            return fi == null ? null : fi.GetValue(f) as Control;
        }

        static void Dump(StringBuilder sb, string label, Control c) { }

        static void DumpField(StringBuilder sb, string label, Control c)
        {
            if (c == null) { sb.AppendLine(label + " = null"); return; }
            sb.AppendLine(string.Format("{0,-18} Bounds={1} Visible={2} Parent={3}",
                label, c.Bounds, c.Visible, c.Parent == null ? "null" : c.Parent.GetType().Name));
        }
    }
}
