using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DWS_Lite.lib
{
    /// <summary>
    /// Windows 11 visual integration.
    ///
    /// Provides:
    ///  - Native window chrome (rounded corners, dark/light title bar) via DWM.
    ///  - A light/dark colour palette matching the Windows 11 design language,
    ///    applied recursively over the control tree.
    ///
    /// Everything is guarded: on older Windows the DWM attributes simply fail and
    /// the classic look is kept.
    /// </summary>
    internal static class Win11Visual
    {
        // DWM attribute ids (undocumented but stable since Windows 11 22000)
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        // Windows 11 22000+ frame colours. Without these the non-client border is
        // drawn white, which reads as a bright hairline around a dark window.
        private const int DWMWA_BORDER_COLOR = 34;
        private const int DWMWA_CAPTION_COLOR = 35;
        private const int DWMWA_TEXT_COLOR = 36;

        private const int DWMWCP_ROUND = 2;
        private const int DWMWCP_SQUARE = 0;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        // ---- Windows 11 palette ----
        public static readonly Color LightSurface = Color.FromArgb(243, 243, 243); // #F3F3F3
        public static readonly Color LightContent = Color.FromArgb(255, 255, 255); // #FFFFFF
        public static readonly Color LightText = Color.FromArgb(26, 26, 26);       // #1A1A1A
        public static readonly Color LightTextSecondary = Color.FromArgb(92, 92, 92);
        public static readonly Color LightButton = Color.FromArgb(255, 255, 255);
        public static readonly Color LightButtonBorder = Color.FromArgb(138, 138, 138);
        public static readonly Color LightField = Color.FromArgb(255, 255, 255);
        public static readonly Color LightFieldBorder = Color.FromArgb(138, 138, 138);

        public static readonly Color DarkSurface = Color.FromArgb(32, 32, 32);      // #202020
        public static readonly Color DarkContent = Color.FromArgb(43, 43, 43);      // #2B2B2B
        public static readonly Color DarkText = Color.FromArgb(255, 255, 255);
        public static readonly Color DarkTextSecondary = Color.FromArgb(201, 201, 201);
        public static readonly Color DarkButton = Color.FromArgb(46, 46, 46);      // #2E2E2E
        public static readonly Color DarkButtonBorder = Color.FromArgb(93, 93, 93);
        public static readonly Color DarkField = Color.FromArgb(43, 43, 43);
        public static readonly Color DarkFieldBorder = Color.FromArgb(93, 93, 93);
        // A barely-lighter grey so an input reads as a field without a border.
        // TextBox/ComboBox borders are painted by the OS and stay light even when
        // BackColor/ForeColor are dark, so on dark we drop the border and use this.
        public static readonly Color DarkFieldFill = Color.FromArgb(51, 51, 51); // #333333

        public static readonly Color AccentLight = Color.FromArgb(0, 103, 192);    // #0067C0
        public static readonly Color AccentOnLight = Color.FromArgb(255, 255, 255);
        public static readonly Color AccentDark = Color.FromArgb(76, 194, 255);    // #4CC2FF
        public static readonly Color AccentOnDark = Color.FromArgb(0, 0, 0);

        /// <summary>Title-bar / window accent source (used for the border).</summary>
        public static Color Accent(bool dark)
        {
            return dark ? AccentDark : AccentLight;
        }

        /// <summary>Background for the custom caption strip and its caption buttons.</summary>
        public static Color CaptionBackColor(bool dark)
        {
            return dark ? DarkSurface : LightSurface;
        }

        /// <summary>Window/tab-strip surface colour.</summary>
        public static Color SurfaceColor(bool dark)
        {
            return dark ? DarkSurface : LightSurface;
        }

        /// <summary>
        /// Colour of the 1px frame DWM draws around the window. The full accent reads
        /// as a bright neon hairline around a dark window, so dark mode gets a muted,
        /// desaturated version that still ties to the accent.
        /// </summary>
        public static Color FrameBorder(bool dark)
        {
            return dark ? Color.FromArgb(38, 62, 84) : Color.FromArgb(138, 138, 138);
        }

        /// <summary>Install Segoe UI Variable when present, else Segoe UI.</summary>
        public static string ModernFontFamily()
        {
            try
            {
                var f = new FontFamily("Segoe UI Variable Text");
                if (f.IsStyleAvailable(FontStyle.Regular)) return "Segoe UI Variable Text";
            }
            // ReSharper disable once EmptyGeneralCatchClause
            catch (Exception)
            {
            }
            return "Segoe UI";
        }

        /// <summary>Returns the HRESULT so callers can report failures.</summary>
        private static int SetDwm(IntPtr handle, int attr, int value)
        {
            int v = value;
            try
            {
                return DwmSetWindowAttribute(handle, attr, ref v, sizeof(int));
            }
            // ReSharper disable once EmptyGeneralCatchClause
            catch (DllNotFoundException)
            {
                return -1;
            }
            // ReSharper disable once EmptyGeneralCatchClause
            catch (EntryPointNotFoundException)
            {
                return -1;
            }
        }

        /// <summary>COLORREF is 0x00BBGGRR, not RGB.</summary>
        private static int ToColorRef(Color c)
        {
            return (c.B << 16) | (c.G << 8) | c.R;
        }

        /// <summary>
        /// Apply the native Windows 11 chrome to a form: rounded corners, dark/light
        /// title bar, and a frame/border colour that matches the palette. Without the
        /// frame colours DWM keeps drawing the default white hairline around the
        /// window, which looks wrong against a dark surface.
        /// <paramref name="log"/> receives the HRESULT of each call: the frame colour
        /// attributes are write-only on current builds, so DwmGetWindowAttribute cannot
        /// verify them and the return code is the only evidence available.
        /// No-op on Windows 10 and older.
        /// </summary>
        public static void ApplyWin11Chrome(Form form, int buildNumber, bool darkTitleBar, Action<string> log)
        {
            if (form == null || buildNumber < 22000) return;

            var surface = darkTitleBar ? DarkSurface : LightSurface;
            var text = darkTitleBar ? DarkText : LightText;

            int hrCorners = SetDwm(form.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUND);
            int hrDark = SetDwm(form.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, darkTitleBar ? 1 : 0);
            int hrBorder = SetDwm(form.Handle, DWMWA_BORDER_COLOR, ToColorRef(FrameBorder(darkTitleBar)));
            int hrCaption = SetDwm(form.Handle, DWMWA_CAPTION_COLOR, ToColorRef(surface));
            int hrText = SetDwm(form.Handle, DWMWA_TEXT_COLOR, ToColorRef(text));

            if (log != null)
            {
                log(string.Format(
                    "DWM chrome: corners=0x{0:X8} darkTitleBar=0x{1:X8} border=0x{2:X8} caption=0x{3:X8} text=0x{4:X8}",
                    hrCorners, hrDark, hrBorder, hrCaption, hrText));
            }
        }

        /// <summary>Restore squared corners (classic look).</summary>
        public static void ApplySquareCorners(Form form, int buildNumber, Action<string> log)
        {
            if (form == null || buildNumber < 22000) return;
            // DWMWA_COLOR_NONE restores the system colour.
            log(string.Format(
                "DWM chrome reset: corners=0x{0:X8} darkTitleBar=0x{1:X8} border=0x{2:X8} caption=0x{3:X8} text=0x{4:X8}",
                SetDwm(form.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_SQUARE),
                SetDwm(form.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, 0),
                SetDwm(form.Handle, DWMWA_BORDER_COLOR, unchecked((int)0xFFFFFFFE)),
                SetDwm(form.Handle, DWMWA_CAPTION_COLOR, unchecked((int)0xFFFFFFFE)),
                SetDwm(form.Handle, DWMWA_TEXT_COLOR, unchecked((int)0xFFFFFFFE))));
        }

        private static bool IsPrimaryButton(Button b)
        {
            var n = b.Name ?? string.Empty;
            return n.IndexOf("Destroy", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Confirm", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsDangerButton(Button b)
        {
            var n = b.Name ?? string.Empty;
            return n.IndexOf("Disable", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Recursively apply a Windows 11 light or dark palette over a control tree.
        /// Controls painted by code (custom chrome) keep their own colours.
        /// </summary>
        public static void ApplyPalette(Control parent, bool dark)
        {
            if (parent == null) return;

            var surface = dark ? DarkSurface : LightSurface;
            var content = dark ? DarkContent : LightContent;
            var text = dark ? DarkText : LightText;
            var textSecondary = dark ? DarkTextSecondary : LightTextSecondary;
            var buttonBg = dark ? DarkButton : LightButton;
            var buttonBorder = dark ? DarkButtonBorder : LightButtonBorder;
            var field = dark ? DarkField : LightField;
            var fieldBorder = dark ? DarkFieldBorder : LightFieldBorder;

            foreach (Control c in parent.Controls)
            {
                var name = c.Name ?? string.Empty;

                // The caption strip IS the visible title bar. The form drops
                // WS_CAPTION (custom chrome), so DWM's dark title bar has nothing to
                // paint; leaving this control alone keeps it at SystemColors.Control,
                // which is what made the title bar stay light next to a dark body.
                if (name.StartsWith("CaptionWindow", StringComparison.OrdinalIgnoreCase))
                {
                    c.BackColor = surface;
                    c.ForeColor = text;
                    ApplyPalette(c, dark);
                    continue;
                }

                // The rest of the custom chrome keeps its own colours: the 1px border
                // picture boxes and the image-based caption buttons.
                if (name.StartsWith("Border", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("_CloseButton", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("MinimizeButton", StringComparison.OrdinalIgnoreCase))
                {
                    ApplyPalette(c, dark);
                    continue;
                }

                var btn = c as Button;
                if (btn != null)
                {
                    btn.FlatStyle = FlatStyle.Flat;
                    btn.FlatAppearance.BorderColor = buttonBorder;
                    btn.FlatAppearance.BorderSize = 1;
                    btn.UseVisualStyleBackColor = false;
                    if (IsPrimaryButton(btn))
                    {
                        btn.BackColor = dark ? AccentDark : AccentLight;
                        btn.ForeColor = dark ? AccentOnDark : AccentOnLight;
                        btn.FlatAppearance.BorderColor = dark ? AccentDark : AccentLight;
                    }
                    else if (IsDangerButton(btn))
                    {
                        btn.BackColor = Color.FromArgb(196, 43, 28);
                        btn.ForeColor = Color.White;
                    }
                    else
                    {
                        btn.BackColor = buttonBg;
                        btn.ForeColor = text;
                    }
                    ApplyPalette(c, dark);
                    continue;
                }

                var tab = c as TabControl;
                if (tab != null)
                {
                    tab.BackColor = surface;
                    tab.ForeColor = text;
                    // The tab strip is painted by the OS visual style and ignores
                    // BackColor, which is why it stayed light next to a dark body.
                    ThemeTabStrip(tab, dark);
                    // TabPage objects live in TabPages; walk them explicitly so the
                    // palette always reaches the page contents.
                    foreach (TabPage tp in tab.TabPages) ApplyPalette(tp, dark);
                    ApplyPalette(c, dark);
                    continue;
                }

                var tabPage = c as TabPage;
                if (tabPage != null)
                {
                    tabPage.BackColor = content;
                    tabPage.ForeColor = text;
                    ApplyPalette(c, dark);
                    continue;
                }

                var panel = c as Panel;
                if (panel != null)
                {
                    // Content panels that hold controls use the content colour.
                    panel.BackColor = content;
                    panel.ForeColor = text;
                    ApplyPalette(c, dark);
                    continue;
                }

                var group = c as GroupBox;
                if (group != null)
                {
                    group.BackColor = surface;
                    group.ForeColor = text;
                    ApplyPalette(c, dark);
                    continue;
                }

                var textbox = c as TextBoxBase;
                if (textbox != null)
                {
                    if (dark)
                    {
                        // A FixedSingle border is painted by the OS and stays light,
                        // which is the white hairline seen around the Change Log.
                        textbox.BorderStyle = BorderStyle.None;
                        textbox.BackColor = DarkFieldFill;
                    }
                    else
                    {
                        textbox.BorderStyle = BorderStyle.FixedSingle;
                        textbox.BackColor = field;
                    }
                    textbox.ForeColor = text;
                    ApplyPalette(c, dark);
                    continue;
                }

                var combo = c as ComboBox;
                if (combo != null)
                {
                    combo.BackColor = dark ? DarkFieldFill : field;
                    combo.ForeColor = text;
                    // The drop-down popup is a separate window that keeps the system
                    // colours unless the items are owner-drawn.
                    ThemeComboBox(combo, dark);
                    ApplyPalette(c, dark);
                    continue;
                }

                var list = c as ListControl;
                if (list != null)
                {
                    list.BackColor = field;
                    list.ForeColor = text;
                    ApplyPalette(c, dark);
                    continue;
                }

                var rich = c as RichTextBox;
                if (rich != null)
                {
                    rich.BackColor = field;
                    rich.ForeColor = text;
                    ApplyPalette(c, dark);
                    continue;
                }

                var check = c as CheckBox;
                if (check != null)
                {
                    check.ForeColor = text;
                    ApplyPalette(c, dark);
                    continue;
                }

                var radio = c as RadioButton;
                if (radio != null)
                {
                    radio.ForeColor = text;
                    ApplyPalette(c, dark);
                    continue;
                }

                var link = c as LinkLabel;
                if (link != null)
                {
                    link.ForeColor = dark ? AccentDark : AccentLight;
                    link.LinkColor = dark ? AccentDark : AccentLight;
                    // Without these, one visited/disabled link falls back to the
                    // designer's dark grey and disappears on a dark surface.
                    link.VisitedLinkColor = dark ? AccentDark : AccentLight;
                    link.DisabledLinkColor = dark ? DarkTextSecondary : LightTextSecondary;
                    ApplyPalette(c, dark);
                    continue;
                }

                var label = c as Label;
                if (label != null)
                {
                    // Status labels are set by code; leave them alone.
                    if (!name.StartsWith("StatusCommands", StringComparison.OrdinalIgnoreCase))
                    {
                        label.ForeColor = text;
                    }
                    ApplyPalette(c, dark);
                    continue;
                }

                ApplyPalette(c, dark);
            }
        }

        /// <summary>Apply the modern UI font over the whole control tree.</summary>
        public static void ApplyModernFont(Control parent, string family)
        {
            if (parent == null) return;
            foreach (Control c in parent.Controls)
            {
                try
                {
                    if (!string.IsNullOrEmpty(c.Name) &&
                        c.Name.StartsWith("CaptionWindow", StringComparison.OrdinalIgnoreCase))
                    {
                        ApplyModernFont(c, family);
                        continue;
                    }

                    var size = c.Font.Size;
                    var style = c.Font.Style;
                    c.Font = new Font(family, size <= 0 ? 9f : size, style);
                }
                // ReSharper disable once EmptyGeneralCatchClause
                catch (Exception)
                {
                }
                ApplyModernFont(c, family);
            }
        }

        /// <summary>
        /// True when a colour is (close to) the dark surface, so draw handlers can
        /// decide which palette is live without keeping state.
        /// </summary>
        private static bool IsDark(Color c)
        {
            return c.ToArgb() == DarkSurface.ToArgb();
        }

        /// <summary>
        /// Palette flag for every control we owner-draw. The dark/light mode cannot
        /// be read back from a control's own colours: a TabControl keeps a colour of
        /// its own that is not DarkSurface, and a combo box field is deliberately a
        /// different shade. Inferring it made the handlers paint the light palette
        /// over a dark window, which is what left the tab strip white.
        /// </summary>
        private static readonly Dictionary<Control, bool> OwnerDrawDark = new Dictionary<Control, bool>();

        private static bool IsOwnerDrawDark(Control c)
        {
            bool dark;
            if (OwnerDrawDark.TryGetValue(c, out dark)) return dark;
            return c != null && IsDark(c.BackColor);
        }

        /// <summary>Invalidate a control and its children.</summary>
        private static void Invalidate(Control c)
        {
            if (c == null) return;
            try
            {
                c.Invalidate(true);
                foreach (Control child in c.Controls) Invalidate(child);
            }
            // ReSharper disable once EmptyGeneralCatchClause
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Owner-draw a TabControl so the tab strip follows the palette. The strip is
        /// painted by the Windows visual style and ignores BackColor, which is why it
        /// stayed light against a dark body. Safe to call repeatedly: the handlers
        /// are detached before they are attached.
        /// </summary>
        public static void ThemeTabStrip(TabControl tab, bool dark)
        {
            if (tab == null) return;
            try
            {
                tab.DrawMode = TabDrawMode.OwnerDrawFixed;
                OwnerDrawDark[tab] = dark;

                tab.DrawItem -= TabControl_DrawItem;
                tab.DrawItem += TabControl_DrawItem;
                Invalidate(tab);
            }
            // ReSharper disable once EmptyGeneralCatchClause
            catch (Exception)
            {
            }
        }


        private static void TabControl_DrawItem(object sender, DrawItemEventArgs e)
        {
            var tab = sender as TabControl;
            if (tab == null || e.Index < 0 || e.Index >= tab.TabPages.Count) return;

            var dark = IsOwnerDrawDark(tab);
            var surface = dark ? DarkSurface : LightSurface;
            var content = dark ? DarkContent : LightContent;
            var text = dark ? DarkText : LightText;
            // Every tab label uses the full text colour: on dark the secondary grey
            // was too faint to read, so only the selected tab stands out by its
            // underline and background.
            var accent = dark ? AccentDark : AccentLight;

            var r = tab.GetTabRect(e.Index);
            bool selected = (e.Index == tab.SelectedIndex);
            var page = tab.TabPages[e.Index];

            using (var bg = new SolidBrush(selected ? content : surface))
            {
                e.Graphics.FillRectangle(bg, r);
            }

            // Windows 11 marks the selected tab with a short accent underline.
            if (selected)
            {
                using (var pen = new Pen(accent, 2f))
                {
                    e.Graphics.DrawLine(pen, r.Left + 8, r.Bottom - 1, r.Right - 8, r.Bottom - 1);
                }
            }

            TextRenderer.DrawText(
                e.Graphics,
                page == null ? string.Empty : (page.Text ?? string.Empty),
                e.Font,
                r,
                text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        /// <summary>
        /// Owner-draw a ComboBox so the drop-down list follows the palette: the popup
        /// is a separate window that keeps the system colours otherwise.
        /// </summary>
        public static void ThemeComboBox(ComboBox combo, bool dark)
        {
            if (combo == null) return;
            try
            {
                combo.DrawMode = DrawMode.OwnerDrawFixed;
                OwnerDrawDark[combo] = dark;
                combo.DrawItem -= ComboBox_DrawItem;
                combo.DrawItem += ComboBox_DrawItem;
            }
            // ReSharper disable once EmptyGeneralCatchClause
            catch (Exception)
            {
            }
        }

        private static void ComboBox_DrawItem(object sender, DrawItemEventArgs e)
        {
            var combo = sender as ComboBox;
            if (combo == null || e.Index < 0 || e.Index >= combo.Items.Count) return;

            var dark = IsOwnerDrawDark(combo);
            var field = dark ? DarkFieldFill : LightField;
            var text = dark ? DarkText : LightText;
            var hover = dark ? DarkButton : LightButton;
            var accent = dark ? AccentDark : AccentLight;

            bool selected = (e.State & DrawItemState.Selected) != 0;
            using (var bg = new SolidBrush(selected ? hover : field))
            {
                e.Graphics.FillRectangle(bg, e.Bounds);
            }

            TextRenderer.DrawText(
                e.Graphics,
                combo.Items[e.Index] == null ? string.Empty : combo.Items[e.Index].ToString(),
                e.Font,
                e.Bounds,
                text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left);

            if (selected)
            {
                using (var pen = new Pen(accent))
                {
                    e.Graphics.DrawRectangle(pen, new Rectangle(e.Bounds.X, e.Bounds.Y, e.Bounds.Width - 1, e.Bounds.Height - 1));
                }
            }
            e.DrawFocusRectangle();
        }

        /// <summary>
        /// A subtle static accent border colour, used by the animated-border thread
        /// when the modern (non-rainbow) look is active.
        /// </summary>
        public static Color BorderTint(bool dark, float phase)
        {
            var baseColor = dark ? AccentDark : AccentLight;
            // gentle 20% pulse so it does not look like a solid slab
            var f = 0.80f + 0.20f * (float)Math.Sin(phase);
            return Color.FromArgb(
                Clamp255(baseColor.R * f),
                Clamp255(baseColor.G * f),
                Clamp255(baseColor.B * f));
        }

        private static int Clamp255(float v)
        {
            var i = (int)v;
            return i < 0 ? 0 : (i > 255 ? 255 : i);
        }
    }
}
