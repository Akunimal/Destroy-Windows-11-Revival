using System;
using System.Drawing;
using System.Windows.Forms;

namespace DWS_Lite.lib
{
    /// <summary>
    /// Progress bar for the Windows 11 look.
    ///
    /// The native control ignores BackColor under visual styles, so on Windows 11 the
    /// track and fill are painted here as a thin bar in the palette accent. Until
    /// <see cref="ApplyWin11Look"/> is called it behaves exactly like the stock
    /// ProgressBar, so Windows 7/8.1/10 keep the original look.
    /// </summary>
    internal class ModernProgressBar : ProgressBar
    {
        private const int TrackHeight = 4;

        private bool _win11Look;
        private bool _dark;

        /// <summary>Switch to the owner-drawn Windows 11 bar and set its palette.</summary>
        public void ApplyWin11Look(bool dark)
        {
            _win11Look = true;
            _dark = dark;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (!_win11Look)
            {
                base.OnPaintBackground(e);
                return;
            }

            using (var brush = new SolidBrush(Win11Visual.SurfaceColor(_dark)))
            {
                e.Graphics.FillRectangle(brush, ClientRectangle);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (!_win11Look)
            {
                base.OnPaint(e);
                return;
            }

            var g = e.Graphics;
            var track = new Rectangle(0, (Height - TrackHeight) / 2, Width, TrackHeight);
            if (track.Width <= 0 || track.Height <= 0) return;

            var trackColor = _dark ? Color.FromArgb(58, 58, 58) : Color.FromArgb(224, 224, 224);
            using (var brush = new SolidBrush(trackColor))
            {
                g.FillRectangle(brush, track);
            }

            var range = Maximum - Minimum;
            if (range <= 0 || Value <= Minimum) return;

            var fraction = Math.Min(1.0, (double)(Value - Minimum) / range);
            var fillWidth = (int)Math.Round(track.Width * fraction);
            if (fillWidth <= 0) return;

            using (var brush = new SolidBrush(Win11Visual.Accent(_dark)))
            {
                g.FillRectangle(brush, track.X, track.Y, fillWidth, track.Height);
            }
        }
    }
}
