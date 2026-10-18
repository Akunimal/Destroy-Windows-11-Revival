using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DWS_Lite.lib
{
    /// <summary>
    /// Windows 11 style on/off switch: a pill track with a sliding knob and the caption
    /// on its right. It is a CheckBox underneath, so Checked, CheckedChanged, Click and
    /// keyboard toggling behave exactly as before.
    ///
    /// Always owner-drawn, and every paint starts by filling the whole client area, so
    /// nothing from a previous frame is left behind.
    /// </summary>
    internal class ToggleSwitch : CheckBox
    {
        private const int TrackWidth = 40;
        private const int TrackHeight = 20;
        private const int TextGap = 10;

        private bool _dark;

        public ToggleSwitch()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }

        /// <summary>Set by the palette.</summary>
        public bool Dark
        {
            get { return _dark; }
            set
            {
                _dark = value;
                Invalidate();
            }
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            var text = TextRenderer.MeasureText(Text ?? string.Empty, Font);
            return new Size(TrackWidth + TextGap + text.Width + 2, Math.Max(TrackHeight + 4, text.Height + 4));
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // Everything is painted in OnPaint.
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (var background = new SolidBrush(BackColor))
            {
                g.FillRectangle(background, ClientRectangle);
            }

            Color accent = Win11Visual.Accent(_dark);
            Color off = _dark ? Win11Visual.DarkCheckBorder : Win11Visual.LightButtonBorder;
            Color caption = Enabled
                ? ForeColor
                : (_dark ? Win11Visual.DarkTextDisabled : Win11Visual.LightTextSecondary);

            var track = new RectangleF(1.5f, (Height - TrackHeight) / 2f, TrackWidth, TrackHeight);
            using (var path = Pill(track))
            {
                if (Checked)
                {
                    using (var brush = new SolidBrush(accent)) g.FillPath(brush, path);
                }
                else
                {
                    using (var pen = new Pen(off, 1.5f)) g.DrawPath(pen, path);
                }
            }

            const float knob = TrackHeight - 8;
            float knobX = Checked ? track.Right - 4 - knob : track.X + 4;
            Color knobColor = Checked ? (_dark ? Win11Visual.AccentOnDark : Color.White) : off;
            using (var brush = new SolidBrush(knobColor))
            {
                g.FillEllipse(brush, knobX, track.Y + 4, knob, knob);
            }

            var textRect = new Rectangle(TrackWidth + TextGap, 0, Math.Max(0, Width - TrackWidth - TextGap), Height);
            TextRenderer.DrawText(g, Text, Font, textRect, caption,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

            if (Focused && ShowFocusCues)
            {
                ControlPaint.DrawFocusRectangle(g, textRect);
            }
        }

        private static GraphicsPath Pill(RectangleF r)
        {
            var p = new GraphicsPath();
            float d = r.Height;
            p.AddArc(r.X, r.Y, d, d, 90, 180);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 180);
            p.CloseFigure();
            return p;
        }
    }
}
