using System;
using System.Collections.Generic;
using System.Linq;

namespace DWS_Lite.lib
{
    /// <summary>
    /// Catalog of UWP/AppX packages grouped by UI checkbox, using EXACT package names.
    /// Wildcards (e.g. "*note*") are avoided on purpose: on Windows 11 they also match
    /// core apps (Notepad, Sticky Notes, WindowsNotepad...).
    /// IDs cross-checked against the Win11Debloat app catalog (2025).
    /// </summary>
    internal static class Win11Apps
    {
        /// <summary>
        /// Microsoft Store product IDs (winget --source msstore), keyed by package name.
        /// Each ID was checked against the product's PackageFamilyName. Packages missing here
        /// are not sold in the Store (system components) and cannot be reinstalled this way.
        /// </summary>
        public static readonly Dictionary<string, string> StoreIds =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Clipchamp.Clipchamp", "9P1J8S7CCWWT" },
            { "Microsoft.BingNews", "9WZDNCRFHVFW" },
            { "Microsoft.BingSearch", "9NZBF4GT040C" },
            { "Microsoft.BingWeather", "9WZDNCRFJ3Q2" },
            { "Microsoft.Copilot", "9NHT9RB2F4HD" },
            { "Microsoft.GamingApp", "9MV0B5HZVK9Z" },
            { "Microsoft.Microsoft3DViewer", "9NBLGGH42THS" },
            { "Microsoft.MicrosoftSolitaireCollection", "9WZDNCRFHWD2" },
            { "Microsoft.MicrosoftStickyNotes", "9NBLGGH4QGHW" },
            { "Microsoft.Office.OneNote", "9WZDNCRFHVJL" },
            { "Microsoft.OutlookForWindows", "9NRX63209R7B" },
            { "Microsoft.PowerAutomateDesktop", "9NFTCH6J7FHV" },
            { "Microsoft.StartExperiencesApp", "9PC1H9VN18CM" },
            { "Microsoft.Windows.Photos", "9WZDNCRFJBH4" },
            { "Microsoft.WindowsCamera", "9WZDNCRFJBBG" },
            { "Microsoft.WindowsSoundRecorder", "9WZDNCRFHWKN" },
            { "Microsoft.XboxGamingOverlay", "9NZKPSTSNW4P" },
            { "Microsoft.ZuneMusic", "9WZDNCRFJ3PT" },
            { "Microsoft.ZuneVideo", "9WZDNCRFJ3P2" },
            { "MicrosoftCorporationII.MicrosoftFamily", "9PDJDJS743XF" },
            { "MicrosoftCorporationII.QuickAssist", "9P7BP5VNWKX5" },
        };

        // checkBoxDeleteApp3d
        public static readonly string[] App3d =
        {
            "Microsoft.3DBuilder",
            "Microsoft.Microsoft3DViewer",
            "Microsoft.Print3D"
        };

        // checkBoxDeleteAppCamera
        public static readonly string[] AppCamera =
        {
            "Microsoft.WindowsCamera"
        };

        // checkBoxDeleteMailCalendarMaps
        public static readonly string[] MailCalendarMaps =
        {
            "Microsoft.windowscommunicationsapps",
            "Microsoft.WindowsMaps"
        };

        // checkBoxDeleteAppBing (news/finance/sports/weather apps)
        public static readonly string[] AppBing =
        {
            "Microsoft.BingNews",
            "Microsoft.BingWeather",
            "Microsoft.BingFinance",
            "Microsoft.BingSports",
            "Microsoft.BingFoodAndDrink",
            "Microsoft.BingHealthAndFitness",
            "Microsoft.BingTravel",
            "Microsoft.BingTranslator"
        };

        // checkBoxDeleteAppZune (Groove Music / Movies & TV)
        public static readonly string[] AppZune =
        {
            "Microsoft.ZuneMusic",
            "Microsoft.ZuneVideo"
        };

        // checkBoxDeleteAppPeopleOneNote
        public static readonly string[] PeopleOneNote =
        {
            "Microsoft.People",
            "Microsoft.Office.OneNote",
            "Microsoft.MicrosoftStickyNotes"
        };

        // checkBoxDeleteAppPhone (Phone Link)
        public static readonly string[] AppPhone =
        {
            "Microsoft.YourPhone",
            "MicrosoftWindows.CrossDevice"
        };

        // checkBoxDeleteAppPhotos
        public static readonly string[] AppPhotos =
        {
            "Microsoft.Windows.Photos"
        };

        // checkBoxDeleteAppSolit
        public static readonly string[] AppSolitaire =
        {
            "Microsoft.MicrosoftSolitaireCollection"
        };

        // checkBoxDeleteAppVoice
        public static readonly string[] AppVoiceRecorder =
        {
            "Microsoft.WindowsSoundRecorder"
        };

        // checkBoxDeleteAppXBOX (old companion + modern gaming app + overlays;
        // TCUI and IdentityProvider are kept: games and the Store depend on them)
        public static readonly string[] AppXbox =
        {
            "Microsoft.XboxApp",
            "Microsoft.GamingApp",
            "Microsoft.XboxGameOverlay",
            "Microsoft.XboxGamingOverlay"
        };

        // checkBoxDeleteAppCopilot (Windows 11 AI)
        public static readonly string[] AppCopilot =
        {
            "Microsoft.Copilot",
            "Microsoft.Windows.AIHub",
            "Microsoft.Windows.Ai.Copilot.Provider"
        };

        // checkBoxDeleteAppTeams (old personal + new Teams)
        public static readonly string[] AppTeams =
        {
            "MicrosoftTeams",
            "MSTeams"
        };

        // checkBoxDeleteAppOutlook (new Outlook for Windows)
        public static readonly string[] AppOutlook =
        {
            "Microsoft.OutlookForWindows"
        };

        // checkBoxDeleteAppClipchamp
        public static readonly string[] AppClipchamp =
        {
            "Clipchamp.Clipchamp"
        };

        // checkBoxDeleteAppWidgets (Widgets feed + platform + web experience pack)
        public static readonly string[] AppWidgets =
        {
            "Microsoft.WidgetsPlatformRuntime",
            "Microsoft.StartExperiencesApp",
            "MicrosoftWindows.Client.WebExperience"
        };

        // checkBoxDeleteAppWin11Misc (preinstalled noise; all safely removable)
        public static readonly string[] AppWin11Misc =
        {
            "Microsoft.PCManager",
            "Microsoft.Getstarted",
            "Microsoft.WindowsFeedbackHub",
            "Microsoft.BingSearch",
            "MicrosoftCorporationII.MicrosoftFamily",
            "MicrosoftCorporationII.QuickAssist",
            "Microsoft.PowerAutomateDesktop",
            "Microsoft.MicrosoftOfficeHub"
        };

        /// <summary>All catalog groups; used by the "delete all" button.</summary>
        public static IEnumerable<string> AllCatalogApps()
        {
            return App3d
                .Concat(AppCamera)
                .Concat(MailCalendarMaps)
                .Concat(AppBing)
                .Concat(AppZune)
                .Concat(PeopleOneNote)
                .Concat(AppPhone)
                .Concat(AppPhotos)
                .Concat(AppSolitaire)
                .Concat(AppVoiceRecorder)
                .Concat(AppXbox)
                .Concat(AppCopilot)
                .Concat(AppTeams)
                .Concat(AppOutlook)
                .Concat(AppClipchamp)
                .Concat(AppWidgets)
                .Concat(AppWin11Misc);
        }
    }
}
