using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DWS_Lite.lib
{
    /// <summary>
    /// CheckBox drawn with the dark palette in every state.
    ///
    /// The system paints a disabled CheckBox with ControlPaint.DrawStringDisabled, which
    /// ignores ForeColor, and its box stays light. In dark mode this control paints the
    /// box and the text itself, enabled or not. The owner-paint mode depends only on the
    /// theme, never on Enabled: switching it per state made enabled boxes disappear.
    /// </summary>
    internal class ThemedCheckBox : CheckBox
    {
        private const int BoxSize = 16;
        private const int TextGap = 8;

        private bool _dark;

        /// <summary>Set by the palette. Dark = owner-drawn in every state; light = native.</summary>
        public bool Dark
        {
            get { return _dark; }
            set
            {
                _dark = value;
                SetStyle(ControlStyles.UserPaint, value);
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value);
                Invalidate();
            }
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Invalidate();
        }

        protected override void OnCheckedChanged(EventArgs e)
        {
            base.OnCheckedChanged(e);
            Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (!_dark)
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
            if (!_dark)
            {
                base.OnPaint(e);
                return;
            }

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Full background fill first: with AllPaintingInWmPaint nothing else erases the
            // previous frame, so skipping this leaves ghost text behind.
            using (var background = new SolidBrush(BackColor))
            {
                g.FillRectangle(background, ClientRectangle);
            }

            var box = new Rectangle(1, (Height - BoxSize) / 2, BoxSize, BoxSize);
            DrawBox(g, box);

            Color textColor = Enabled ? ForeColor : Win11Visual.DarkTextDisabled;
            var textRect = new Rectangle(box.Right + TextGap, 0, Math.Max(0, Width - box.Right - TextGap), Height);
            TextRenderer.DrawText(g, Text, Font, textRect, textColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);

            if (Focused && ShowFocusCues)
            {
                ControlPaint.DrawFocusRectangle(g, textRect);
            }
        }

        private void DrawBox(Graphics g, Rectangle box)
        {
            bool filled = CheckState != CheckState.Unchecked;

            Color fill;
            Color stroke;
            Color mark;
            if (!Enabled)
            {
                fill = filled ? Win11Visual.DarkCheckDisabledBorder : Color.FromArgb(56, 56, 56);
                stroke = Win11Visual.DarkCheckDisabledBorder;
                mark = Color.FromArgb(43, 43, 43);
            }
            else if (filled)
            {
                fill = Win11Visual.AccentDark;
                stroke = Win11Visual.AccentDark;
                mark = Win11Visual.AccentOnDark;
            }
            else
            {
                fill = Win11Visual.DarkField;
                stroke = Win11Visual.DarkCheckBorder;
                mark = Win11Visual.DarkText;
            }

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
