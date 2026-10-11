using System;
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

        public static readonly Color AccentLight = Color.FromArgb(0, 103, 192);    // #0067C0
        public static readonly Color AccentOnLight = Color.FromArgb(255, 255, 255);
        public static readonly Color AccentDark = Color.FromArgb(76, 194, 255);    // #4CC2FF
        public static readonly Color AccentOnDark = Color.FromArgb(0, 0, 0);

        /// <summary>Title-bar / window accent source (used for the border).</summary>
        public static Color Accent(bool dark)
        {
            return dark ? AccentDark : AccentLight;
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

        private static void SetDwm(IntPtr handle, int attr, int value)
        {
            int v = value;
            try
            {
                DwmSetWindowAttribute(handle, attr, ref v, sizeof(int));
            }
            // ReSharper disable once EmptyGeneralCatchClause
            catch (DllNotFoundException)
            {
            }
            // ReSharper disable once EmptyGeneralCatchClause
            catch (EntryPointNotFoundException)
            {
            }
        }

        /// <summary>
        /// Apply the native Windows 11 chrome to a form: rounded corners and a
        /// dark or light title bar. No-op on Windows 10 and older.
        /// </summary>
        public static void ApplyWin11Chrome(Form form, int buildNumber, bool darkTitleBar)
        {
            if (form == null || buildNumber < 22000) return;
            SetDwm(form.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_ROUND);
            SetDwm(form.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, darkTitleBar ? 1 : 0);
        }

        /// <summary>Restore squared corners (classic look).</summary>
        public static void ApplySquareCorners(Form form, int buildNumber)
        {
            if (form == null || buildNumber < 22000) return;
            SetDwm(form.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, DWMWCP_SQUARE);
            SetDwm(form.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, 0);
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

                // Custom-drawn window chrome keeps its own colours.
                if (name.StartsWith("CaptionWindow", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("Border", StringComparison.OrdinalIgnoreCase)
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
                    textbox.BackColor = field;
                    textbox.ForeColor = text;
                    textbox.BorderStyle = BorderStyle.FixedSingle;
                    ApplyPalette(c, dark);
                    continue;
                }

                var combo = c as ComboBox;
                if (combo != null)
                {
                    combo.BackColor = field;
                    combo.ForeColor = text;
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

        /// <summary><summary>
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
