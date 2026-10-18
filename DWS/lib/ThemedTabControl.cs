using System;
using System.Drawing;
using System.Windows.Forms;

namespace DWS_Lite.lib
{
    /// <summary>
    /// TabControl whose tab strip background follows BackColor.
    ///
    /// Under the Windows visual style the strip area that no tab covers (the space
    /// after the last tab) is painted in the system colour and ignores BackColor.
    /// The Paint event does not help here: it is only raised when UserPaint is set,
    /// and setting UserPaint stops the base control from drawing its tabs. So the
    /// remainder of the strip is filled right after the native WM_PAINT has drawn the
    /// strip and the owner-drawn tabs, excluding the tab rectangles themselves.
    /// </summary>
    internal class ThemedTabControl : TabControl
    {
        private const int WM_PAINT = 0x000F;

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WM_PAINT) PaintStripRemainder();
        }

        private void PaintStripRemainder()
        {
            if (TabCount == 0 || Width <= 0) return;

            // DisplayRectangle starts below the strip, so everything above it is strip.
            var strip = new Rectangle(0, 0, Width, DisplayRectangle.Top);
            if (strip.Height <= 0) return;

            using (var region = new Region(strip))
            {
                for (int i = 0; i < TabCount; i++)
                {
                    region.Exclude(GetTabRect(i));
                }

                using (var g = Graphics.FromHwnd(Handle))
                using (var brush = new SolidBrush(BackColor))
                {
                    g.FillRegion(brush, region);
                }
            }
        }
    }
}
