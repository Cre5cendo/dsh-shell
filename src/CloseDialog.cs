using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DshShell
{
    /// <summary>关闭询问对话框：圆角卡片 + DSH 蓝主按钮，三分支（最小化到托盘 / 退出 / 取消）。</summary>
    internal sealed class CloseDialog : Form
    {
        public const int ChoiceTray = 0;
        public const int ChoiceQuit = 1;
        public const int ChoiceCancel = 2;

        // 与 DSH 界面一致的配色
        private static readonly Color Accent = Color.FromArgb(77, 107, 254);
        private static readonly Color AccentHover = Color.FromArgb(101, 128, 255);
        private static readonly Color CardBack = Color.FromArgb(32, 33, 36);
        private static readonly Color BorderColor = Color.FromArgb(58, 60, 66);
        private static readonly Color TextMain = Color.FromArgb(236, 238, 242);
        private static readonly Color TextDim = Color.FromArgb(150, 155, 164);

        private int choice = ChoiceCancel;

        private CloseDialog(bool stopsServer)
        {
            Text = "关闭 DeepSeek Harness";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            ClientSize = new Size(468, 236);
            BackColor = CardBack;
            ForeColor = TextMain;
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular);
            DoubleBuffered = true;
            KeyPreview = true;

            Label title = new Label();
            title.Text = "要关闭 DeepSeek Harness 吗？";
            title.Font = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold);
            title.ForeColor = TextMain;
            title.BackColor = Color.Transparent;
            title.Location = new Point(26, 24);
            title.Size = new Size(416, 30);
            title.MouseDown += OnCardMouseDown;
            Controls.Add(title);

            Label subtitle = new Label();
            subtitle.Text = "选择一个动作，或者取消继续使用";
            subtitle.ForeColor = TextDim;
            subtitle.BackColor = Color.Transparent;
            subtitle.Location = new Point(28, 56);
            subtitle.Size = new Size(416, 20);
            subtitle.MouseDown += OnCardMouseDown;
            Controls.Add(subtitle);

            Panel divider = new Panel();
            divider.BackColor = BorderColor;
            divider.Location = new Point(26, 88);
            divider.Size = new Size(416, 1);
            Controls.Add(divider);

            Label detail = new Label();
            detail.Text = stopsServer
                ? "最小化到托盘 · 窗口收进托盘，后台任务继续运行\r\n退出 · 结束本次 DSH 服务进程，正在执行的任务会中断"
                : "最小化到托盘 · 窗口收进托盘，后台任务继续运行\r\n退出 · 关闭外壳窗口，DSH 服务保持在后台";
            detail.ForeColor = TextDim;
            detail.BackColor = Color.Transparent;
            detail.Location = new Point(28, 102);
            detail.Size = new Size(416, 44);
            detail.MouseDown += OnCardMouseDown;
            Controls.Add(detail);

            // 按钮宽度各自贴合文字，整排右边缘与上方正文的右边界对齐
            int rightEdge = 26 + 416;

            RoundButton cancel = new RoundButton("取消", ButtonStyle.Outline, 38);
            cancel.Click += delegate { choice = ChoiceCancel; DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancel);
            cancel.Location = new Point(rightEdge - cancel.Width, 172);

            RoundButton quit = new RoundButton("退出 DSH", ButtonStyle.Danger, 38);
            quit.Click += delegate { choice = ChoiceQuit; DialogResult = DialogResult.OK; Close(); };
            Controls.Add(quit);
            quit.Location = new Point(cancel.Left - 10 - quit.Width, 172);

            RoundButton tray = new RoundButton("最小化到托盘", ButtonStyle.Primary, 38);
            tray.Click += delegate { choice = ChoiceTray; DialogResult = DialogResult.OK; Close(); };
            Controls.Add(tray);
            tray.Location = new Point(quit.Left - 10 - tray.Width, 172);

            AcceptButton = tray;
            CancelButton = cancel;

            MouseDown += OnCardMouseDown;
            Resize += delegate { ApplyRoundedRegion(); };
            Load += delegate { ApplyRoundedRegion(); };
        }

        private void OnCardMouseDown(object sender, MouseEventArgs args)
        {
            if (args.Button != MouseButtons.Left)
            {
                return;
            }

            Native.ReleaseCapture();
            Native.SendMessage(Handle, Native.WM_NCLBUTTONDOWN, (IntPtr)Native.HTCAPTION, IntPtr.Zero);
        }

        private void ApplyRoundedRegion()
        {
            try
            {
                using (GraphicsPath path = new GraphicsPath())
                {
                    int radius = 14;
                    path.AddArc(0, 0, radius * 2, radius * 2, 180, 90);
                    path.AddArc(Width - radius * 2 - 1, 0, radius * 2, radius * 2, 270, 90);
                    path.AddArc(Width - radius * 2 - 1, Height - radius * 2 - 1, radius * 2, radius * 2, 0, 90);
                    path.AddArc(0, Height - radius * 2 - 1, radius * 2, radius * 2, 90, 90);
                    path.CloseFigure();
                    Region old = Region;
                    Region = new Region(path);
                    if (old != null)
                    {
                        old.Dispose();
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        protected override void OnPaint(PaintEventArgs args)
        {
            base.OnPaint(args);
            using (Pen pen = new Pen(BorderColor))
            {
                args.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
        }

        protected override void OnHandleCreated(EventArgs args)
        {
            base.OnHandleCreated(args);
            try
            {
                int round = Native.DWMWCP_ROUND;
                Native.DwmSetWindowAttribute(Handle, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
                int dark = 1;
                Native.DwmSetWindowAttribute(Handle, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            }
            catch (Exception)
            {
            }
        }

        /// <summary>返回 ChoiceTray / ChoiceQuit / ChoiceCancel。</summary>
        public static int Ask(IWin32Window owner, bool stopsServer)
        {
            CloseDialog dialog = new CloseDialog(stopsServer);
            dialog.ShowDialog(owner);
            int result = dialog.choice;
            dialog.Dispose();
            return result;
        }

        private enum ButtonStyle
        {
            Primary,
            Outline,
            Danger
        }

        /// <summary>自绘圆角按钮：主按钮填充 DSH 蓝，其余描边，悬停时变亮。</summary>
        private sealed class RoundButton : Button
        {
            private readonly ButtonStyle style;
            private bool hovering;

            public RoundButton(string text, ButtonStyle style, int height)
            {
                this.style = style;
                Text = text;
                FlatStyle = FlatStyle.Flat;
                FlatAppearance.BorderSize = 0;
                BackColor = CardBack;
                ForeColor = TextMain;
                Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Regular);
                Cursor = Cursors.Hand;
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);

                // 宽度跟着文字走（左右各留 17px 内边距），不再用写死的宽度
                int textWidth = TextRenderer.MeasureText(text, Font).Width;
                int width = Math.Max(78, textWidth + 34);
                Size = new Size(width, height);
            }

            protected override void OnMouseEnter(EventArgs args)
            {
                hovering = true;
                Invalidate();
                base.OnMouseEnter(args);
            }

            protected override void OnMouseLeave(EventArgs args)
            {
                hovering = false;
                Invalidate();
                base.OnMouseLeave(args);
            }

            protected override void OnPaint(PaintEventArgs args)
            {
                Graphics graphics = args.Graphics;
                // 先把卡片底色铺满整块，圆角之外才不会露出白色默认底
                graphics.Clear(BackColor);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
                int radius = 9;

                using (GraphicsPath path = new GraphicsPath())
                {
                    path.AddArc(bounds.X, bounds.Y, radius * 2, radius * 2, 180, 90);
                    path.AddArc(bounds.Right - radius * 2, bounds.Y, radius * 2, radius * 2, 270, 90);
                    path.AddArc(bounds.Right - radius * 2, bounds.Bottom - radius * 2, radius * 2, radius * 2, 0, 90);
                    path.AddArc(bounds.X, bounds.Bottom - radius * 2, radius * 2, radius * 2, 90, 90);
                    path.CloseFigure();

                    if (style == ButtonStyle.Primary)
                    {
                        using (SolidBrush brush = new SolidBrush(hovering ? AccentHover : Accent))
                        {
                            graphics.FillPath(brush, path);
                        }
                    }
                    else
                    {
                        Color fill = hovering
                            ? (style == ButtonStyle.Danger ? Color.FromArgb(214, 69, 69) : Color.FromArgb(52, 54, 60))
                            : Color.FromArgb(42, 44, 49);
                        using (SolidBrush brush = new SolidBrush(fill))
                        {
                            graphics.FillPath(brush, path);
                        }

                        using (Pen pen = new Pen(hovering && style == ButtonStyle.Danger ? Color.FromArgb(214, 69, 69) : BorderColor))
                        {
                            graphics.DrawPath(pen, path);
                        }
                    }
                }

                TextRenderer.DrawText(
                    graphics,
                    Text,
                    Font,
                    new Rectangle(0, 0, Width, Height),
                    style == ButtonStyle.Primary ? Color.White : TextMain,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
    }
}
