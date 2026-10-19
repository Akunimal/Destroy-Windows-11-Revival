using System;
using System.Drawing;
using System.Windows.Forms;

namespace DWS_Lite.lib
{
    /// <summary>
    /// Flat button drawn with the dark palette in every state.
    ///
    /// A disabled flat button is painted with ControlPaint.DrawStringDisabled, which uses
    /// system colours and ignores ForeColor. In dark mode this control paints the fill,
    /// the flat border and the caption itself, enabled or not. The owner-paint mode
    /// depends only on the theme and the flat style, never on Enabled.
    /// </summary>
    internal class ThemedButton : Button
    {
        private bool _dark;

        /// <summary>Set by the palette. Dark + flat = owner-drawn in every state.</summary>
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
            Invalidate();
        }

        private bool OwnerDrawn
        {
            get { return _dark && FlatStyle == FlatStyle.Flat; }
        }

        private void UpdatePaintMode()
        {
            bool owner = OwnerDrawn;
            SetStyle(ControlStyles.UserPaint, owner);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, owner);
            Invalidate();
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
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var brush = new SolidBrush(BackColor))
            {
                g.FillRectangle(brush, rect);
            }

            int borderSize = Math.Max(1, FlatAppearance.BorderSize);
            using (var pen = new Pen(FlatAppearance.BorderColor, borderSize))
            {
                g.DrawRectangle(pen, rect);
            }

            Color textColor = Enabled ? ForeColor : Win11Visual.DarkTextDisabled;
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }
}
