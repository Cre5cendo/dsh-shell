using System.Drawing;
using System.Windows.Forms;

namespace DshShell
{
    /// <summary>托盘右键菜单的深色主题渲染器：近黑底、悬停灰块、无图标栏、底部细边框。</summary>
    internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        internal static readonly Color Back = Color.FromArgb(26, 26, 28);
        internal static readonly Color Hover = Color.FromArgb(58, 60, 66);
        internal static readonly Color Border = Color.FromArgb(66, 68, 74);
        internal static readonly Color Separator = Color.FromArgb(52, 54, 60);
        internal static readonly Color Text = Color.FromArgb(232, 234, 238);
        internal static readonly Color TextDim = Color.FromArgb(146, 150, 158);

        public DarkMenuRenderer()
            : base(new DarkColorTable())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (SolidBrush brush = new SolidBrush(Back))
            {
                e.Graphics.FillRectangle(brush, e.AffectedBounds);
            }
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (Pen pen = new Pen(Border))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            }
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
            // 图标栏保留（勾号画在这里），但底色跟菜单一致，不要那圈渐变
            using (SolidBrush brush = new SolidBrush(Back))
            {
                e.Graphics.FillRectangle(brush, e.AffectedBounds);
            }
        }

        /// <summary>自己画勾：默认的勾是深色，在近黑底上根本看不见。</summary>
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Rectangle bounds = e.ImageRectangle;
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            using (Pen pen = new Pen(Color.FromArgb(122, 162, 255), 2f))
            {
                e.Graphics.DrawLines(pen, new Point[]
                {
                    new Point(bounds.Left + 2, bounds.Top + bounds.Height / 2),
                    new Point(bounds.Left + bounds.Width / 2 - 1, bounds.Bottom - 4),
                    new Point(bounds.Right - 1, bounds.Top + 4)
                });
            }
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            Rectangle bounds = new Rectangle(2, 1, e.Item.Width - 4, e.Item.Height - 2);
            Color fill = (e.Item.Selected || e.Item.Pressed) && e.Item.Enabled ? Hover : Back;
            using (SolidBrush brush = new SolidBrush(fill))
            {
                e.Graphics.FillRectangle(brush, bounds);
            }
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Text : TextDim;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            Rectangle bounds = e.Item.Bounds;
            using (Pen pen = new Pen(Separator))
            {
                e.Graphics.DrawLine(pen, bounds.Left + 12, bounds.Height / 2, bounds.Right - 12, bounds.Height / 2);
            }
        }

        private sealed class DarkColorTable : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return Back; } }
            public override Color MenuBorder { get { return Border; } }
            public override Color MenuItemBorder { get { return Hover; } }
            public override Color MenuItemSelected { get { return Hover; } }
            public override Color MenuItemSelectedGradientBegin { get { return Hover; } }
            public override Color MenuItemSelectedGradientEnd { get { return Hover; } }
            public override Color ImageMarginGradientBegin { get { return Back; } }
            public override Color ImageMarginGradientMiddle { get { return Back; } }
            public override Color ImageMarginGradientEnd { get { return Back; } }
            public override Color SeparatorDark { get { return Separator; } }
            public override Color SeparatorLight { get { return Back; } }
        }
    }
}
