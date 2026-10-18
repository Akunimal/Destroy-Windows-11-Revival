using System;
using System.Drawing;
using System.Windows.Forms;

namespace DWS_Lite.lib
{
    /// <summary>
    /// Flat button whose disabled state keeps readable text on the dark palette.
    ///
    /// A disabled flat button is painted with ControlPaint.DrawStringDisabled, which uses
    /// system colours and ignores ForeColor. Only the disabled dark state is owner-drawn:
    /// the palette fill, the flat border, and DarkTextDisabled for the caption.
    /// </summary>
    internal class ThemedButton : Button
    {
        private bool _dark;

        /// <summary>Set by the palette. Only dark + disabled + flat takes the owner-drawn path.</summary>
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
            get { return _dark && !Enabled && FlatStyle == FlatStyle.Flat; }
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

            TextRenderer.DrawText(g, Text, Font, ClientRectangle, Win11Visual.DarkTextDisabled,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }
}
