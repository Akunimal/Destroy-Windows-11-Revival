using System;
using System.Drawing;
using System.Windows.Forms;

namespace DWS_Lite.lib
{
    /// <summary>
    /// TabControl that paints its whole surface in BackColor.
    ///
    /// The Windows visual style paints the tab strip, the 3px frame around the page
    /// area and the gap after the last tab in the system colour, and it does so after
    /// any fill done in WM_PAINT, so those pixels stayed light. With UserPaint set the
    /// native painting is skipped: the control fills its client area, then raises the
    /// owner-draw tab callbacks itself. The tab pages are child windows and paint
    /// themselves on top.
    /// </summary>
    internal class ThemedTabControl : TabControl
    {
        private Color _backColor = SystemColors.Control;

        public ThemedTabControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        /// <summary>
        /// The base TabControl ignores BackColor assignments (it stays SystemColors.Control),
        /// which is what painted the light frame. The value is kept here and used in OnPaint.
        /// </summary>
        public override Color BackColor
        {
            get { return _backColor; }
            set
            {
                _backColor = value;
                Invalidate();
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // Everything is painted in OnPaint.
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            if (ClientRectangle.Width <= 0 || ClientRectangle.Height <= 0) return;

            using (var brush = new SolidBrush(BackColor))
            {
                g.FillRectangle(brush, ClientRectangle);
            }

            for (int i = 0; i < TabCount; i++)
            {
                OnDrawItem(new DrawItemEventArgs(g, Font, GetTabRect(i), i, DrawItemState.None));
            }
        }
    }
}
