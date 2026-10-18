using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DWS_Lite.lib
{
    /// <summary>
    /// CheckBox whose disabled state keeps readable text on the dark palette.
    ///
    /// The system paints a disabled CheckBox with ControlPaint.DrawStringDisabled, which
    /// ignores ForeColor. Only the disabled dark state is painted here; enabled and light
    /// states use the native paint. Owner paint is switched on only while needed.
    /// </summary>
    internal class ThemedCheckBox : CheckBox
    {
        private const int BoxSize = 16;
        private const int TextGap = 8;

        private bool _dark;

        /// <summary>Set by the palette. Only dark + disabled takes the owner-drawn path.</summary>
        public bool Dark
        {
            get { return _dark; }
            set
            {
                _dark = value;
                UpdatePaintMode();
            }
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            UpdatePaintMode();
        }

        private bool OwnerDrawn
        {
            get { return _dark && !Enabled; }
        }

        private void UpdatePaintMode()
        {
            bool owner = OwnerDrawn;
            SetStyle(ControlStyles.UserPaint, owner);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, owner);
            Invalidate();
            Refresh();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (!OwnerDrawn)
            {
                base.OnPaintBackground(e);
                return;
            }

            using (var brush = new SolidBrush(BackColor))
            {
                e.Graphics.FillRectangle(brush, ClientRectangle);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (!OwnerDrawn)
            {
                base.OnPaint(e);
                return;
            }

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // With AllPaintingInWmPaint the background is not erased for us; without this
            // fill every repaint draws the text on top of the previous one (ghosting).
            using (var background = new SolidBrush(BackColor))
            {
                g.FillRectangle(background, ClientRectangle);
            }

            var box = new Rectangle(1, (Height - BoxSize) / 2, BoxSize, BoxSize);
            DrawBox(g, box);

            var textRect = new Rectangle(box.Right + TextGap, 0, Math.Max(0, Width - box.Right - TextGap), Height);
            TextRenderer.DrawText(g, Text, Font, textRect, Win11Visual.DarkTextDisabled,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
        }

        private void DrawBox(Graphics g, Rectangle box)
        {
            bool filled = CheckState != CheckState.Unchecked;
            Color fill = filled ? Win11Visual.DarkCheckDisabledBorder : Color.FromArgb(56, 56, 56);
            Color stroke = Win11Visual.DarkCheckDisabledBorder;
            Color mark = Color.FromArgb(43, 43, 43);

            var rect = new RectangleF(box.X + 0.5f, box.Y + 0.5f, box.Width - 1, box.Height - 1);
            using (var path = RoundedRect(rect, 3f))
            {
                using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
                using (var pen = new Pen(stroke, 1f)) g.DrawPath(pen, path);
            }

            using (var pen = new Pen(mark, 2f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                if (CheckState == CheckState.Checked)
                {
                    g.DrawLines(pen, new[]
                    {
                        new PointF(box.X + 4f, box.Y + 8f),
                        new PointF(box.X + 7f, box.Y + 11f),
                        new PointF(box.X + 12f, box.Y + 5f),
                    });
                }
                else if (CheckState == CheckState.Indeterminate)
                {
                    g.DrawLine(pen, box.X + 4f, box.Y + box.Height / 2f, box.X + box.Width - 4f, box.Y + box.Height / 2f);
                }
            }
        }

        private static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}
