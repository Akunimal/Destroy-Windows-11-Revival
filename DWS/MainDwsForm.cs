using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Resources;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using DWS_Lite.lang;
using DWS_Lite.lib;
using DWS_Lite.Properties;
using Microsoft.Win32;

namespace DWS_Lite
{
    public sealed partial class MainDwsForm : Form
    {
        private const string LogFileName = "DWS.log";
        // ReSharper disable once InconsistentNaming
        public const int WM_NCLBUTTONDOWN = 0xA1;
        // ReSharper disable once InconsistentNaming
        public const int HT_CAPTION = 0x2;
        // ReSharper disable once InconsistentNaming
        private const int CS_DROPSHADOW = 0x00020000;
        private readonly string _systemPath = Path.GetPathRoot(Environment.SystemDirectory);
        private bool _destroyFlag;
        // ReSharper disable once CollectionNeverQueried.Local
        // ReSharper disable once FieldCanBeMadeReadOnly.Local
        private List<string> _errorsList = new List<string>();
        private int _fatalErrors;
        private ResourceManager _rm;
        private string _system32Location;
        private bool _win10 = true;
        private bool _win11;
        private int _buildNumber;
        // Windows 11 look: rounded corners, modern palette and font, static
        // accent border instead of the original animated rainbow border.
        private bool _win11ModernLook = true;
        // Dark is the default look: the original flat light theme felt dated next to
        // Windows 11's dark title bar, and the user asked for the app to open in dark.
        private bool _win11DarkTheme = true;

        public MainDwsForm(string[] args)
        {

            InitializeComponent();
            DoubleBuffered = true;
            // Re create log file
            RecreateLogFile(LogFileName);
            // Check windows version
            CheckWindowsVersion();
            // Windows 11 visual integration (rounded corners, dark title bar,
            // modern palette + font). No-op on Windows 10 and older.
            ApplyWindows11Look();
            //Check SYSNATIVE (x64)
            _SetShellSys32Path();
            ProfessionalModeSet(false);
            CheckEnableOrDisableUac();
            /*
             * Get icon
             */
            try
            {
                Icon = Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location);
                Text +=
                    $"v{FileVersionInfo.GetVersionInfo(System.Reflection.Assembly.GetExecutingAssembly().Location).FileVersion}";
            }
            catch
            {
                _OutPut("Error get icon or version.", LogLevel.Error);
            }
            SetLanguage(_GetLang(args)); // set language
            ChangeLanguage(); // change language
            StealthMode(args); //check args
            new Thread(AnimateBackground).Start(); // animate border (new thread)
        }

        // Auto-update removed in the DWS 11 fork: the old check downloaded an
        // AssemblyInfo.cs over plain HTTP from the upstream repo (MITM risk).
        // Check releases on GitHub manually instead.

        public override string Text
        {
            get { return base.Text; }
            set
            {
                base.Text = value;
                CaptionWindow.Text = value;
            }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ClassStyle |= CS_DROPSHADOW;
                return cp;
            }
        }

        /* 
         * Feature animation is calculated borders.
         */

        private void AnimateBackground()
        {
            try
            {
                while (true)
                {
                    for (float i = 0; i < 1f; i += 0.01f)
                    {
                        Thread.Sleep(50); // thread slleep. Too fast.
                        ChangeBorderColor(Rainbow(i)); //call change border function
                    }
                }
            }
                // ReSharper disable once EmptyGeneralCatchClause
                // ReSharper disable once UnusedVariable
            catch (Exception ex)
            {
                _OutPut($"Error in AnimateBackground: {ex.Message}", LogLevel.Debug);
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            /*
             * Check windows system restore Enabled status
             */
            if (WindowsUtil.SystemRestore_Status() == 0) _OutPut("Windows Restore DISABLE", LogLevel.Warning);
        }

        /*
         * Check function arguments
         */

        private void StealthMode(IEnumerable<string> args)
        {
            var currentArgs = args as string[] ?? args.ToArray();
            foreach (var currentArg in currentArgs)
            {
                if (currentArg.IndexOf("/deleteapp=", StringComparison.Ordinal) > -1)
                {
                    DeleteWindows10MetroApp(currentArg.Replace("/deleteapp=", null));
                }
                if (currentArg.IndexOf("/destroy", StringComparison.Ordinal) > -1)
                {
                    WindowState = FormWindowState.Minimized;
                    ShowInTaskbar = false;
                    _destroyFlag = true;
                    //Windows 10
                    if (_win10) DestroyWindowsSpyingMainThread();
                    else Dws78MainThread();
                }
                if (currentArg.IndexOf("/deleteonedrive", StringComparison.Ordinal) > -1)
                {
                    DeleteOneDrive();
                }
            }
            if (currentArgs.Any())
            Process.GetCurrentProcess().Kill();
        }

        /*
         * Constant
         */

        private void _SetShellSys32Path()
        {
            if (File.Exists(_systemPath + @"Windows\Sysnative\cmd.exe"))
            {
                _system32Location = _systemPath + @"Windows\Sysnative\";
            }
            else
            {
                _system32Location = _systemPath + @"Windows\System32\";
            }
        }

        /*
         * Check windows build number: Windows 11 (>= 22000), Windows 10 (>= 10240), or Windows 7/8.1
         */

        private void CheckWindowsVersion()
        {
            var windowsBuildNumber = WindowsUtil.GetWindowsBuildNumber();
            _buildNumber = windowsBuildNumber;
            if (windowsBuildNumber < 7600)
            {
                if (MessageBox.Show(@"Minimum windows version - 7\nExit from the program?", @"Error",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Error) == DialogResult.Yes)
                    Process.GetCurrentProcess().Kill();
            }
            // Windows 11 is build 22000 and up (21H2+); 24H2/25H2 are 26100+
            if (windowsBuildNumber >= 22000)
            {
                _win11 = true;
                _OutPut($"Windows 11 detected (build {windowsBuildNumber}).", LogLevel.Info);
            }
            // The dark-mode toggle only makes sense on Windows 11.
            checkBoxWin11DarkMode.Visible = _win11;
            // check Win 10/11 vs 7/8.1
            if (windowsBuildNumber >= 10000) return;
            _win10 = false;
            Windows78Panel.Enabled = true;
            Windows78Panel.Visible = true;
            Win10SettingsPanel.Enabled = false;
            Win10SettingsPanel.Visible = false;
            checkBoxDeleteWindows10Apps.Enabled = false;
            btnDestroyWindowsSpying.Visible = false;
            btnDestroyWindows78Spy.Visible = true;
            for (var i = 0; i < _updatesnumberlist.Length; i++)
            {
                checkedListBoxUpdatesW78.Items.Add("KB" + _updatesnumberlist[i]);
                checkedListBoxUpdatesW78.SetItemChecked(i, true);
            }
            //------------------------------------------
        }

        /// <summary>
        /// Applies the Windows 11 look: rounded corners, a matching title bar, the
        /// modern palette and font. On Windows 10 / 7 / 8.1 nothing happens and the
        /// original flat look is kept.
        /// </summary>
        private void ApplyWindows11Look()
        {
            if (!_win11 || !_win11ModernLook) return;

            try
            {
                Win11Visual.ApplyWin11Chrome(this, _buildNumber, _win11DarkTheme);
                Win11Visual.ApplyModernFont(this, Win11Visual.ModernFontFamily());
                Win11Visual.ApplyPalette(this, _win11DarkTheme);

                // The original 1px picture-box borders clash with rounded corners.
                BorderUP.Visible = false;
                BorderDown.Visible = false;
                BorderLeft.Visible = false;
                BorderR.Visible = false;

                // Dark mode is a UI preference, not a professional-only toggle.
                checkBoxWin11DarkMode.Enabled = true;
                checkBoxWin11DarkMode.Checked = _win11DarkTheme;

                _OutPut($"Windows 11 look applied ({(_win11DarkTheme ? "dark" : "light")} palette).", LogLevel.Info);
            }
            catch (Exception ex)
            {
                _OutPut($"Error applying Windows 11 look: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>Switches between the Windows 11 dark and light palette at runtime.</summary>
        private void SetWindows11DarkTheme(bool dark)
        {
            _win11DarkTheme = dark;
            if (!_win11 || !_win11ModernLook) return;

            Win11Visual.ApplyWin11Chrome(this, _buildNumber, dark);
            Win11Visual.ApplyPalette(this, dark);
            _OutPut($"Windows 11 dark theme {(dark ? "enabled" : "disabled")}.", LogLevel.Info);
        }

        private void checkBoxWin11DarkMode_CheckedChanged(object sender, EventArgs e)
        {
            SetWindows11DarkTheme(checkBoxWin11DarkMode.Checked);
        }

        private void CheckEnableOrDisableUac()
        {
            if (WindowsUtil.UAC_Status())
            {
                btnEnableUac.Enabled = true;
                btnDisableUac.Enabled = false;
            }
            else
            {
                btnEnableUac.Enabled = false;
                btnDisableUac.Enabled = true;
            }

            if (WindowsUtil.SystemRestore_Status() != 0) return;
            checkBoxCreateSystemRestorePoint.Checked = false;
            checkBoxCreateSystemRestorePoint.Enabled = false;
        }

        private void btnDestroyWindowsSpying_Click(object sender, EventArgs e)
        {
            StartDestroyWindowsSpying();
        }

        private void Progressbaradd(int numberadd)
        {
            try
            {
                Invoke(new MethodInvoker(delegate
                {
                    try
                    {
                        ProgressBarStatus.Value += numberadd;
                    }
                    catch (Exception)
                    {
                        // ignored
                    }
                }));
            }
            catch (Exception)
            {
                try
                {
                    ProgressBarStatus.Value += numberadd;
                }
                catch (Exception)
                {
                    // ignored
                }
            }
        }

        private void EnableOrDisableTab(bool enableordisable)
        {
            try
            {
                Invoke(new MethodInvoker(delegate
                {
                    ControlBox = enableordisable;
                    _CloseButton.Enabled = enableordisable;
                    btnDestroyWindowsSpying.Enabled = enableordisable;
                    tabPageSettings.Enabled = enableordisable;
                    tabPageUtilites.Enabled = enableordisable;
                }));
            }
            catch (Exception)
            {
                ControlBox = enableordisable;
                tabPageMain.Enabled = enableordisable;
                tabPageSettings.Enabled = enableordisable;
                tabPageUtilites.Enabled = enableordisable;
            }
        }

        public void RecreateLogFile(string logfilename)
        {
            try
            {
                if (!File.Exists(logfilename))
                {
                    File.Create(logfilename).Close();
                }
                else
                {
                    File.Delete(logfilename);
                    File.Create(logfilename).Close();
                }
            }
                // ReSharper disable once EmptyGeneralCatchClause
                // ReSharper disable once UnusedVariable
            catch (Exception ex)
            {
                _OutPut(ex.Message, LogLevel.Debug);
            }
        }

        public void DeleteFile(string filepath)
        {
            RunCmd($"/c del /F /Q \"{filepath}\"");
        }

        public void RunCmd(string args)
        {
            ProcStartargs(Paths.ShellCmdLocation, args);
        }

        private void ProcStartargs(string name, string args)
        {
            try
            {
                var proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = name,
                        Arguments = args,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true,
                        StandardOutputEncoding = Encoding.GetEncoding(866)
                    }
                };
                proc.Start();
                // ReSharper disable once NotAccessedVariable
                string line = null;
                while (!proc.StandardOutput.EndOfStream)
                {
                    line += Environment.NewLine + proc.StandardOutput.ReadLine();
                }
                proc.WaitForExit();
#if DEBUG
                _OutPut($"Start: {name} {args}{Environment.NewLine}Output: {line}", LogLevel.Debug);
#endif
            }
                // ReSharper disable once UnusedVariable
            catch (Exception ex)
            {
                _OutPut($"Error start prog {name} {args}", LogLevel.Error);
                _OutPut(ex.Message, LogLevel.Debug);
                _fatalErrors++;
                _errorsList.Add($"Error start prog {name} {args}");
            }
        }

        private static void CreateRestorePoint(string description)
        {
            var oScope = new ManagementScope("\\\\localhost\\root\\default");
            var oPath = new ManagementPath("SystemRestore");
            var oGetOp = new ObjectGetOptions();
            var oProcess = new ManagementClass(oScope, oPath, oGetOp);

            var oInParams =
                oProcess.GetMethodParameters("CreateRestorePoint");
            oInParams["Description"] = description;
            oInParams["RestorePointType"] = 12; // MODIFY_SETTINGS
            oInParams["EventType"] = 100;

            oProcess.InvokeMethod("CreateRestorePoint", oInParams, null);
        }

        private static string GetWindowsBuildVersion()
        {
            // в value массив из байт
            var value = $"\r\nWindows Version: {WindowsUtil.GetProductName()}\r\nBuild: {WindowsUtil.GetSystemBuild()}";
            return value;
        }

        private void DeleteWindows10MetroApp(string appname)
        {
            ProcStartargs("powershell",
                $"-command \"Get-AppxPackage *{appname}* | Remove-AppxPackage\"");
        }

        private void StartDestroyWindowsSpying()
        {
            _errorsList.Clear();
            _fatalErrors = 0;
            EnableOrDisableTab(false);
            SetCompleteText(true);
            _OutPut($"Starting: {DateTime.Now}.");
            _OutPut(GetWindowsBuildVersion());
            _OutPutSplit();
            ProgressBarStatus.Value = 0;
            new Thread(DestroyWindowsSpyingMainThread).Start();
        }

        private void DestroyWindowsSpyingMainThread()
        {
            // Snapshot every registry value we are about to change, so the whole
            // run can be reverted from the Utilities tab.
            try
            {
                _OutPut("Backing up the registry values DWS is going to change...");
                RegistryBackup.CreateBackup(m => _OutPut(m, LogLevel.Debug));
            }
            catch (Exception ex)
            {
                _OutPut("Warning: could not create a registry backup before applying changes.", LogLevel.Warning);
                _OutPut(ex.Message, LogLevel.Debug);
            }
            if (checkBoxCreateSystemRestorePoint.Checked)
            {
                try
                {
                    var restorepointName = $"DestroyWindowsSpying {DateTime.Now}";
                    _OutPut($"Creating restore point {restorepointName}...");
                    CreateRestorePoint(restorepointName);
                    _OutPut($"Restore point {restorepointName} created.");
                }
                    // ReSharper disable once UnusedVariable
                catch (Exception ex)
                {
                    _OutPut("Error creating restore point.");
                    _OutPut(ex.Message, LogLevel.Debug);
                }
            }
            Progressbaradd(10);
            if (checkBoxKeyLoggerAndTelemetry.Checked)
            {
                // DISABLE TELEMETRY
                _OutPut("Disable telemetry...");
                DigTrackFullRemove();
                // DELETE KEYLOGGER
                _OutPut("Delete keylogger...");
                RunCmd(
                    "/c reg add \"HKEY_LOCAL_MACHINE\\SOFTWARE\\Policies\\Microsoft\\Windows\\Windows Search\" /v \"AllowCortana\" /t REG_DWORD /d 0 /f ");
                    // disable Cortana;
                _OutPut("Cortana disable #1");
            }
            Progressbaradd(15); //25
            if (checkBoxAddToHosts.Checked)
            {
                AddSpyDomainsToHosts();
            }
            // Legacy blackhole routes to 2015 spy IPs - EXPERT MODE ONLY (off by default)
            if (checkBoxBlockSpyIPs.Checked)
            {
                BlockIpAddr();
            }
            Progressbaradd(20); //45
            if (checkBoxDisablePrivateSettings.Checked)
            {
                string[] regkeyvalandother =
                {
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\DeviceAccess\Global\{21157C1F-2651-4CC1-90CA-1F28B02263F6}",
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\DeviceAccess\Global\{2EEF81BE-33FA-4800-9670-1CD474972C3F}",
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\DeviceAccess\Global\{7D7E8402-7C54-4821-A34E-AEEFD62DED93}",
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\DeviceAccess\Global\{992AFA70-6F47-4148-B3E9-3003349C1548}",
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\DeviceAccess\Global\{9D9E0118-1807-4F2E-96E4-2CE57142E196}",
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\DeviceAccess\Global\{A8804298-2D5F-42E3-9531-9C8C39EB29CE}",
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\DeviceAccess\Global\{B19F89AF-E3EB-444B-8DEA-202575A71599}",
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\DeviceAccess\Global\{BFA794E4-F964-4FDB-90F6-51056BFE4B44}",
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\DeviceAccess\Global\{C1D23ACC-752B-43E5-8448-8D0E519CD6D6}",
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\DeviceAccess\Global\{D89823BA-7180-4B81-B50C-7E471E6121A3}",
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\DeviceAccess\Global\{E5323777-F976-4f5b-9B55-B94699C46E44}",
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\DeviceAccess\Global\{E6AD100E-5F4E-44CD-BE0F-2265D88D14F5}",
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\DeviceAccess\Global\{E83AF229-8640-4D18-A213-E22675EBB2C3}",
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\DeviceAccess\Global\LooselyCoupled"
                };
                foreach (var currentRegKey in regkeyvalandother)
                {
                    SetRegValueHkcu(currentRegKey, "Value", "Deny", RegistryValueKind.String);
                }
                SetRegValueHkcu(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Search", "CortanaEnabled", "0",
                    RegistryValueKind.DWord);
                SetRegValueHkcu(@"SOFTWARE\Microsoft\InputPersonalization", "RestrictImplicitInkCollection", "1",
                    RegistryValueKind.DWord);
                SetRegValueHklm(@"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "DisableWebSearch", "1",
                    RegistryValueKind.DWord);
                SetRegValueHklm(@"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "ConnectedSearchUseWeb", "0",
                    RegistryValueKind.DWord);
                SetRegValueHklm(@"SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors", "DisableLocation", "1",
                    RegistryValueKind.DWord);
                SetRegValueHklm(@"SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors",
                    "DisableWindowsLocationProvider", "1", RegistryValueKind.DWord);
                SetRegValueHklm(@"SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors", "DisableLocationScripting",
                    "1", RegistryValueKind.DWord);
                SetRegValueHklm(@"SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors", "DisableSensors", "1",
                    RegistryValueKind.DWord);
                SetRegValueHklm(@"SYSTEM\CurrentControlSet\Services\lfsvc\Service\Configuration", "Status", "0",
                    RegistryValueKind.DWord);
                SetRegValueHklm(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Sensor\Overrides\{BFA794E4-F964-4FDB-90F6-51056BFE4B44}",
                    "SensorPermissionState", "0", RegistryValueKind.DWord);
                SetRegValueHkcu(@"SOFTWARE\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod", "0", RegistryValueKind.DWord);
                SetRegValueHkcu(@"SOFTWARE\Microsoft\Siuf\Rules", "PeriodInNanoSeconds", "0", RegistryValueKind.DWord);
                SetRegValueHkcu(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Search", "BingSearchEnabled", "0",
                    RegistryValueKind.DWord);
                SetRegValueHklm(@"SOFTWARE\Policies\Microsoft\Windows\TabletPC", "PreventHandwritingDataSharing", "1",
                    RegistryValueKind.DWord);
                SetRegValueHklm(@"SOFTWARE\Policies\Microsoft\Windows\HandwritingErrorReports",
                    "PreventHandwritingErrorReports", "1", RegistryValueKind.DWord);
                SetRegValueHklm(@"SOFTWARE\Policies\Microsoft\Windows\AppCompat", "DisableInventory", "1",
                    RegistryValueKind.DWord);
                SetRegValueHklm(@"SOFTWARE\Policies\Microsoft\Windows\Personalization", "NoLockScreenCamera", "1",
                    RegistryValueKind.DWord);
                SetRegValueHklm(@"SOFTWARE\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", "0",
                    RegistryValueKind.DWord);
                SetRegValueHkcu(@"SOFTWARE\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", "0",
                    RegistryValueKind.DWord);
                SetRegValueHkcu(@"SOFTWARE\Microsoft\Input\TIPC", "Enabled", "0", RegistryValueKind.DWord);
                SetRegValueHklm(@"SOFTWARE\Policies\Microsoft\Biometrics", "Enabled", "0", RegistryValueKind.DWord);
                SetRegValueHklm(@"SOFTWARE\Policies\Microsoft\Windows\CredUI", "DisablePasswordReveal", "1",
                    RegistryValueKind.DWord);
                SetRegValueHkcu(@"SOFTWARE\Microsoft\Windows\CurrentVersion\SettingSync", "SyncPolicy", "5",
                    RegistryValueKind.DWord);
                SetRegValueHkcu(@"SOFTWARE\Microsoft\Windows\CurrentVersion\SettingSync\Groups\Personalization",
                    "Enabled", "0", RegistryValueKind.DWord);
                SetRegValueHkcu(@"SOFTWARE\Microsoft\Windows\CurrentVersion\SettingSync\Groups\BrowserSettings",
                    "Enabled", "0", RegistryValueKind.DWord);
                SetRegValueHkcu(@"SOFTWARE\Microsoft\Windows\CurrentVersion\SettingSync\Groups\Credentials", "Enabled",
                    "0", RegistryValueKind.DWord);
                SetRegValueHkcu(@"SOFTWARE\Microsoft\Windows\CurrentVersion\SettingSync\Groups\Language", "Enabled", "0",
                    RegistryValueKind.DWord);
                SetRegValueHkcu(@"SOFTWARE\Microsoft\Windows\CurrentVersion\SettingSync\Groups\Accessibility", "Enabled",
                    "0", RegistryValueKind.DWord);
                SetRegValueHkcu(@"SOFTWARE\Microsoft\Windows\CurrentVersion\SettingSync\Groups\Windows", "Enabled", "0",
                    RegistryValueKind.DWord);
                _OutPut("Disable private settings");
            }
            Progressbaradd(10); //55
            if (checkBoxDisableWindowsDefender.Checked)
            {
                try
                {
                    // NOTE: DisableAntiSpyware (used by old DWS) has been IGNORED
                    // by Windows since 1903 and is flagged by malware scanners.
                    // We only set MAPS/sample-submission off here. On Windows 11
                    // Tamper Protection may block even these - enable it off first.
                    SetRegValueHklm(@"SOFTWARE\Policies\Microsoft\Windows Defender\Spynet", "SpyNetReporting", "0",
                        RegistryValueKind.DWord);
                    SetRegValueHklm(@"SOFTWARE\Policies\Microsoft\Windows Defender\Spynet", "SubmitSamplesConsent", "2",
                        RegistryValueKind.DWord);
                    SetRegValueHklm(@"SOFTWARE\Policies\Microsoft\Windows Defender\Spynet", "MAPSReporting", "0",
                        RegistryValueKind.DWord);
                    SetRegValueHklm(@"SOFTWARE\Policies\Microsoft\MRT", "DontReportInfectionInformation", "1",
                        RegistryValueKind.DWord);
                    _OutPut("Disable Defender cloud/MAPS reporting (Tamper Protection may block on Win11).");
                    SetRegValueHklm(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer", "SmartScreenEnabled", "Off",
                        RegistryValueKind.String);
                    _OutPut("Disable smart screen.");
                }
                catch (Exception ex)
                {
                    _OutPut("Error disable windows Defender or Smart Screen", LogLevel.Error);
                    _OutPut(ex.Message, LogLevel.Debug);
                    _fatalErrors++;
                    _errorsList.Add($"Error disable Windows Defender or Smart Screen. Message: {ex.Message}");
                }
            }
            Progressbaradd(5); //60
            if (checkBoxWin11Tweaks.Checked && _win11)
            {
                ApplyWin11Tweaks();
            }
            Progressbaradd(5); //65
            if (checkBoxSetDefaultPhoto.Checked)
            {
                SetRegValueHkcu(@"Software\Classes\.ico", null, "PhotoViewer.FileAssoc.Tiff", RegistryValueKind.String);
                SetRegValueHkcu(@"Software\Classes\.tiff", null, "PhotoViewer.FileAssoc.Tiff", RegistryValueKind.String);
                SetRegValueHkcu(@"Software\Classes\.bmp", null, "PhotoViewer.FileAssoc.Tiff", RegistryValueKind.String);
                SetRegValueHkcu(@"Software\Classes\.png", null, "PhotoViewer.FileAssoc.Tiff", RegistryValueKind.String);
                SetRegValueHkcu(@"Software\Classes\.gif", null, "PhotoViewer.FileAssoc.Tiff", RegistryValueKind.String);
                SetRegValueHkcu(@"Software\Classes\.jpeg", null, "PhotoViewer.FileAssoc.Tiff", RegistryValueKind.String);
                SetRegValueHkcu(@"Software\Classes\.jpg", null, "PhotoViewer.FileAssoc.Tiff", RegistryValueKind.String);
                _OutPut("Set Default PhotoViewer");
            }
            Progressbaradd(10); //70
            if (checkBoxSPYTasks.Checked)
            {
                DisableSpyingTasks();
            }
            Progressbaradd(10); //80
            if (checkBoxDeleteWindows10Apps.Checked)
            {
                RemoveWindows10Apps();
            }
            Progressbaradd(20); //100
            EnableOrDisableTab(true);
            try
            {
                Invoke(new MethodInvoker(delegate { SetCompleteText(); }));
            }
            catch (Exception)
            {
                try
                {
                    SetCompleteText();
                }
                    // ReSharper disable once UnusedVariable
                    // ReSharper disable once EmptyGeneralCatchClause
                catch (Exception ex)
                {
                    _OutPut(ex.Message, LogLevel.Debug);
                }
            }
        }

        private void DigTrackFullRemove()
        {
            RunCmd("/c net stop DiagTrack ");
            RunCmd("/c net stop diagnosticshub.standardcollector.service ");
            RunCmd("/c net stop dmwappushservice ");
            RunCmd("/c net stop WMPNetworkSvc ");
            RunCmd("/c sc config DiagTrack start=disabled ");
            RunCmd("/c sc config diagnosticshub.standardcollector.service start=disabled ");
            RunCmd("/c sc config dmwappushservice start=disabled ");
            RunCmd("/c sc config WMPNetworkSvc start=disabled ");
            RunCmd(
                "/c REG ADD HKLM\\SYSTEM\\ControlSet001\\Control\\WMI\\AutoLogger\\AutoLogger-Diagtrack-Listener /v Start /t REG_DWORD /d 0 /f");
            // DELETING services (sc delete) is a hard action: Windows 11 re-registers
            // DiagTrack on update and breakage is possible. Expert mode only.
            if (checkBoxDeleteSpyServices.Checked)
            {
                RunCmd("/c sc delete dmwappushsvc");
                RunCmd("/c sc delete \"Diagnostics Tracking Service\"");
                RunCmd("/c sc delete diagtrack");
                _OutPut("Telemetry services DELETED (expert mode).", LogLevel.Warning);
            }
            RunCmd(
                "/c reg add \"HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Device Metadata\" /v \"PreventDeviceMetadataFromNetwork\" /t REG_DWORD /d 1 /f ");
            RunCmd(
                "/c reg add \"HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\DataCollection\" /v \"AllowTelemetry\" /t REG_DWORD /d 0 /f ");
            RunCmd(
                "/c reg add \"HKEY_LOCAL_MACHINE\\SOFTWARE\\Policies\\Microsoft\\MRT\" /v \"DontOfferThroughWUAU\" /t REG_DWORD /d 1 /f ");
            RunCmd(
                "/c reg add \"HKEY_LOCAL_MACHINE\\SOFTWARE\\Policies\\Microsoft\\SQMClient\\Windows\" /v \"CEIPEnable\" /t REG_DWORD /d 0 /f ");
            RunCmd(
                "/c reg add \"HKEY_LOCAL_MACHINE\\SOFTWARE\\Policies\\Microsoft\\Windows\\AppCompat\" /v \"AITEnable\" /t REG_DWORD /d 0 /f ");
            RunCmd(
                "/c reg add \"HKEY_LOCAL_MACHINE\\SOFTWARE\\Policies\\Microsoft\\Windows\\AppCompat\" /v \"DisableUAR\" /t REG_DWORD /d 1 /f ");
            RunCmd(
                "/c reg add \"HKEY_LOCAL_MACHINE\\SOFTWARE\\Policies\\Microsoft\\Windows\\DataCollection\" /v \"AllowTelemetry\" /t REG_DWORD /d 0 /f ");
            RunCmd(
                "/c reg add \"HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Control\\WMI\\AutoLogger\\AutoLogger-Diagtrack-Listener\" /v \"Start\" /t REG_DWORD /d 0 /f ");
            RunCmd(
                "/c reg add \"HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Control\\WMI\\AutoLogger\\SQMLogger\" /v \"Start\" /t REG_DWORD /d 0 /f ");
            RunCmd(
                "/c reg add \"HKEY_CURRENT_USER\\SOFTWARE\\Microsoft\\Siuf\\Rules\" /v \"NumberOfSIUFInPeriod\" /t REG_DWORD /d 0 /f ");
            RunCmd(
                "/c reg delete \"HKEY_CURRENT_USER\\SOFTWARE\\Microsoft\\Siuf\\Rules\" /v \"PeriodInNanoSeconds\" /f ");
        }

        private void SetCompleteText(bool start = false)
        {
            if (_destroyFlag)
                return;
            if (start)
            {
                StatusCommandsLable.Text = @"Destroy Windows 11 Spying";
                StatusCommandsLable.ForeColor = Color.Black;
            }
            else
            {
                if (_fatalErrors == 0)
                {
                    StatusCommandsLable.Text = $"Destroy Windows 11 Spying - {GetTranslateText("Complete")}!";
                    StatusCommandsLable.ForeColor = Color.DarkGreen;
                    if (MessageBox.Show(GetTranslateText("CompleteMSG"), GetTranslateText("Info"),
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    Process.Start("shutdown.exe", "-r -t 0");
                    Process.GetCurrentProcess().Kill();
                }
                else
                {
                    StatusCommandsLable.Text = $"Destroy Windows 11 Spying - errors: {_fatalErrors}";
                    StatusCommandsLable.ForeColor = Color.Red;
                    try
                    {
                        var errorsMsg = _errorsList.Aggregate<string, string>(null,
                            (current, errorMsg) => current + (errorMsg + "\r\n"));
                        var errorFilePath = Path.GetTempPath() + @"\errors.log";
                        File.Create(errorFilePath).Close();
                        File.WriteAllText(errorFilePath, errorsMsg);
                        Process.Start(errorFilePath);
                    }
                    catch
                    {
                        // ignored
                    }
                    if (MessageBox.Show(string.Format(GetTranslateText("ErrorMSG"), _fatalErrors),
                        GetTranslateText("Info"),
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                    Process.Start("shutdown.exe", "-r -t 0");
                    Process.GetCurrentProcess().Kill();
                }
            }
        }

        private void DisableSpyingTasks()
        {
            string[] disabletaskslist =
            {
                @"Microsoft\Office\Office 15 Subscription Heartbeat",
                @"Microsoft\Office\Office ClickToRun Service Monitor",
                @"Microsoft\Office\OfficeTelemetry\AgentFallBack2016",
                @"Microsoft\Office\OfficeTelemetry\OfficeTelemetryAgentLogOn2016",
                @"Microsoft\Office\OfficeTelemetryAgentFallBack",
                @"Microsoft\Office\OfficeTelemetryAgentFallBack2016",
                @"Microsoft\Office\OfficeTelemetryAgentLogOn",
                @"Microsoft\Office\OfficeTelemetryAgentLogOn2016",
                @"Microsoft\Windows\AppID\SmartScreenSpecific",
                @"Microsoft\Windows\Application Experience\AitAgent",
                @"Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser",
                @"Microsoft\Windows\Application Experience\ProgramDataUpdater",
                @"Microsoft\Windows\Application Experience\StartupAppTask",
                @"Microsoft\Windows\Autochk\Proxy",
                @"Microsoft\Windows\CloudExperienceHost\CreateObjectTask",
                @"Microsoft\Windows\Customer Experience Improvement Program\BthSQM",
                @"Microsoft\Windows\Customer Experience Improvement Program\Consolidator",
                @"Microsoft\Windows\Customer Experience Improvement Program\KernelCeipTask",
                @"Microsoft\Windows\Customer Experience Improvement Program\Uploader",
                @"Microsoft\Windows\Customer Experience Improvement Program\UsbCeip",
                @"Microsoft\Windows\DiskDiagnostic\Microsoft-Windows-DiskDiagnosticDataCollector",
                @"Microsoft\Windows\DiskFootprint\Diagnostics",
                @"Microsoft\Windows\FileHistory\File History (maintenance mode)",
                @"Microsoft\Windows\Maintenance\WinSAT",
                @"Microsoft\Windows\Media Center\ActivateWindowsSearch",
                @"Microsoft\Windows\Media Center\ConfigureInternetTimeService",
                @"Microsoft\Windows\Media Center\DispatchRecoveryTasks",
                @"Microsoft\Windows\Media Center\ehDRMInit",
                @"Microsoft\Windows\Media Center\InstallPlayReady",
                @"Microsoft\Windows\Media Center\mcupdate",
                @"Microsoft\Windows\Media Center\MediaCenterRecoveryTask",
                @"Microsoft\Windows\Media Center\ObjectStoreRecoveryTask",
                @"Microsoft\Windows\Media Center\OCURActivate",
                @"Microsoft\Windows\Media Center\OCURDiscovery",
                @"Microsoft\Windows\Media Center\PBDADiscovery",
                @"Microsoft\Windows\Media Center\PBDADiscoveryW1",
                @"Microsoft\Windows\Media Center\PBDADiscoveryW2",
                @"Microsoft\Windows\Media Center\PvrRecoveryTask",
                @"Microsoft\Windows\Media Center\PvrScheduleTask",
                @"Microsoft\Windows\Media Center\RegisterSearch",
                @"Microsoft\Windows\Media Center\ReindexSearchRoot",
                @"Microsoft\Windows\Media Center\SqlLiteRecoveryTask",
                @"Microsoft\Windows\Media Center\UpdateRecordPath",
                @"Microsoft\Windows\PI\Sqm-Tasks",
                @"Microsoft\Windows\Power Efficiency Diagnostics\AnalyzeSystem",
                @"Microsoft\Windows\Shell\FamilySafetyMonitor",
                @"Microsoft\Windows\Shell\FamilySafetyRefresh",
                @"Microsoft\Windows\Shell\FamilySafetyUpload",
                @"Microsoft\Windows\Windows Error Reporting\QueueReporting"
            };
            foreach (var currentTask in disabletaskslist)
            {
                ProcStartargs("SCHTASKS", $"/Change /TN \"{currentTask}\" /disable");
                _OutPut("Disabled task: " + currentTask);
            }
        }

        private void AddSpyDomainsToHosts()
        {
            try
            {
                // Telemetry/ad/tracking endpoints as of 2025 (Windows 11 era).
                // Removed obsolete 2015 entries, typo duplicates, and domains
                // that break core connectivity (NCSI, Windows Update, MS account).
                string[] hostsdomains =
                {
                    // Telemetry ingestion (Windows 10/11)
                    "v10.events.data.microsoft.com",
                    "v20.events.data.microsoft.com",
                    "browser.events.data.microsoft.com",
                    "self.events.data.microsoft.com",
                    "events.data.microsoft.com",
                    "functional.events.data.microsoft.com",
                    "telemetry.microsoft.com",
                    "watson.telemetry.microsoft.com",
                    "watson.ppe.telemetry.microsoft.com",
                    "modern.watson.telemetry.microsoft.com",
                    "df.telemetry.microsoft.com",
                    "oca.telemetry.microsoft.com",
                    "sqm.telemetry.microsoft.com",
                    "sqm.df.telemetry.microsoft.com",
                    "telecommand.telemetry.microsoft.com",
                    "telemetry.appex.bing.net",
                    "telemetry.urs.microsoft.com",
                    "ca.telemetry.microsoft.com",
                    "i1.services.social.microsoft.com",
                    "compatexchange.cloudapp.net",
                    "settings.data.microsoft.com",
                    "settings-win.data.microsoft.com",
                    "settings-sandbox.data.microsoft.com",
                    "vortex.data.microsoft.com",
                    "vortex-win.data.microsoft.com",
                    "vortex-sandbox.data.microsoft.com",
                    "v10.vortex-win.data.microsoft.com",
                    "vortex-bn2.metron.live.com.nsatc.net",
                    "vortex-cy2.metron.live.com.nsatc.net",
                    "wes.df.telemetry.microsoft.com",
                    "reports.wes.df.telemetry.microsoft.com",
                    "services.wes.df.telemetry.microsoft.com",
                    "feedback.microsoft-hohm.com",
                    "feedback.search.microsoft.com",
                    "feedback.windows.com",
                    "diagnostics.support.microsoft.com",
                    "pre.footprintpredict.com",
                    "survey.watson.microsoft.com",
                    "watson.live.com",
                    "watson.microsoft.com",
                    "statsfe1.ws.microsoft.com",
                    "statsfe2.ws.microsoft.com",
                    "statsfe2.update.microsoft.com.akadns.net",
                    "choice.microsoft.com",
                    "choice.microsoft.com.nsatc.net",
                    "spynet2.microsoft.com",
                    "spynetalt.microsoft.com",
                    "redir.metaservices.microsoft.com",
                    "ceuswatcab01.blob.core.windows.net",
                    "ceuswatcab02.blob.core.windows.net",
                    // Bing advertising / search telemetry
                    "ads.msn.com",
                    "ads1.msn.com",
                    "a.ads1.msn.com",
                    "b.ads1.msn.com",
                    "a.ads2.msn.com",
                    "a.ads2.msads.net",
                    "b.ads2.msads.net",
                    "ads1.msads.net",
                    "a.rad.msn.com",
                    "b.rad.msn.com",
                    "rad.msn.com",
                    "live.rads.msn.com",
                    "rad.live.com",
                    "flex.msn.com",
                    "g.msn.com",
                    "h1.msn.com",
                    "ac3.msn.com",
                    "preview.msn.com",
                    "c.msn.com",
                    // Ad networks
                    "ad.doubleclick.net",
                    "adnexus.net",
                    "adnxs.com",
                    "m.adnxs.com",
                    "secure.adnxs.com",
                    "s0.2mdn.net",
                    "static.2mdn.net",
                    "bs.serving-sys.com",
                    "msntest.serving-sys.com",
                    "aidps.atdmt.com",
                    "c.atdmt.com",
                    "cdn.atdmt.com",
                    "db3aqu.atdmt.com",
                    "ec.atdmt.com",
                    "view.atdmt.com",
                    "aka-cdn-ns.adtech.de",
                    "secure.flashtalking.com",
                    // Skype ads
                    "pricelist.skype.com",
                    // Misc tracking-adjacent
                    "ssw.live.com",
                    "s.gateway.messenger.live.com"
                };
                var hostslocation = _system32Location + @"drivers\etc\hosts";
                File.SetAttributes(hostslocation, FileAttributes.Normal);
                var existing = File.ReadAllText(hostslocation);
                var sb = new StringBuilder(existing);
                if (!existing.EndsWith(Environment.NewLine))
                    sb.Append(Environment.NewLine);
                var added = 0;
                foreach (var currentHostsDomain in hostsdomains)
                {
                    if (existing.IndexOf(currentHostsDomain, StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    sb.Append("0.0.0.0 ").Append(currentHostsDomain).Append(Environment.NewLine);
                    added++;
                    _OutPut($"Add to hosts - {currentHostsDomain}");
                }
                File.WriteAllText(hostslocation, sb.ToString());
                _OutPut($"Hosts file updated: {added} new entries. (Firewall is NOT touched - by design)");
            }
            catch (Exception ex)
            {
                _errorsList.Add($"Error add to hosts. Message: {ex.Message}");
                _fatalErrors++;
                _OutPut("Error add HOSTS", LogLevel.Error);
                _OutPut(ex.Message, LogLevel.Debug);
            }
            RunCmd("/c ipconfig /flushdns");
        }

        // Windows 11 privacy/AI tweaks. Registry keys verified against the
        // Win11Debloat project (2025). Only applied when build >= 22000.
        private void ApplyWin11Tweaks()
        {
            try
            {
                // ---- Copilot (taskbar + policy) ----
                SetRegValueHkcu(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowCopilotButton",
                    "0", RegistryValueKind.DWord);
                SetRegValueHkcu(@"Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", "1",
                    RegistryValueKind.DWord);
                SetRegValueHklm(@"Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", "1",
                    RegistryValueKind.DWord);
                _OutPut("Windows 11: Copilot disabled.");
                // ---- Recall / Windows AI (snapshots) ----
                SetRegValueHkcu(@"Software\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", "1",
                    RegistryValueKind.DWord);
                SetRegValueHklm(@"Software\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", "1",
                    RegistryValueKind.DWord);
                SetRegValueHklm(@"Software\Policies\Microsoft\Windows\WindowsAI", "AllowRecallEnablement", "0",
                    RegistryValueKind.DWord);
                SetRegValueHklm(@"Software\Policies\Microsoft\Windows\WindowsAI", "TurnOffSavingSnapshots", "1",
                    RegistryValueKind.DWord);
                _OutPut("Windows 11: Recall / AI data analysis disabled.");
                // ---- Bing in Search + Cortana ----
                SetRegValueHkcu(@"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", "1",
                    RegistryValueKind.DWord);
                SetRegValueHklm(@"Software\Policies\Microsoft\Windows\Windows Search", "AllowCortana", "0",
                    RegistryValueKind.DWord);
                SetRegValueHklm(@"Software\Policies\Microsoft\Windows\Windows Search", "CortanaConsent", "0",
                    RegistryValueKind.DWord);
                _OutPut("Windows 11: Bing/Cortana in search disabled.");
                // ---- Widgets (News and Interests) ----
                SetRegValueHklm(@"Software\Policies\Microsoft\Dsh", "AllowNewsAndInterests", "0",
                    RegistryValueKind.DWord);
                SetRegValueHklm(@"Software\Policies\Microsoft\Dsh", "DisableWidgetsBoard", "0",
                    RegistryValueKind.DWord);
                SetRegValueHklm(@"Software\Policies\Microsoft\Dsh", "DisableWidgetsOnLockScreen", "0",
                    RegistryValueKind.DWord);
                _OutPut("Windows 11: Widgets disabled.");
                // ---- Start menu / Explorer ads & suggestions ----
                SetRegValueHkcu(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
                    "SubscribedContent-338388SpotlightEnabled", "0", RegistryValueKind.DWord);
                SetRegValueHkcu(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
                    "SubscribedContent-353694Enabled", "0", RegistryValueKind.DWord);
                SetRegValueHkcu(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
                    "SubscribedContent-353696Enabled", "0", RegistryValueKind.DWord);
                SetRegValueHkcu(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
                    "SystemPaneSuggestionsEnabled", "0", RegistryValueKind.DWord);
                _OutPut("Windows 11: Start/Explorer ads and suggestions disabled.");
                // ---- AI service auto-start ----
                SetRegValueHklm(@"SYSTEM\CurrentControlSet\Services\WSAIFabricSvc", "Start", "3",
                    RegistryValueKind.DWord);
                _OutPut("Windows 11: AI fabric service set to manual start.");
                // ---- Prevent auto BitLocker/device encryption (24H2) ----
                SetRegValueHklm(@"SYSTEM\CurrentControlSet\Control\BitLocker", "PreventDeviceEncryption", "1",
                    RegistryValueKind.DWord);
                _OutPut("Windows 11: auto device encryption prevention set.");
                _OutPut("Windows 11 tweaks applied.");
            }
            catch (Exception ex)
            {
                _OutPut("Error applying Windows 11 tweaks", LogLevel.Error);
                _OutPut(ex.Message, LogLevel.Debug);
                _fatalErrors++;
                _errorsList.Add($"Error applying Windows 11 tweaks. Message: {ex.Message}");
            }
        }

        // Removes packages by EXACT name for all users and also removes the
        // provisioned (new-user) copy so they don't come back on new accounts.
        private void DeleteAppxGroup(string[] packageNames, string logName)
        {
            if (packageNames == null || packageNames.Length == 0) return;
            var namesList = string.Join(",", packageNames.Select(n => $"'{n}'"));
            // PowerShell: exact match on Name, remove for every user + provisioned.
            var ps =
                $"$n=@({namesList}); " +
                "foreach($x in $n){" +
                "Get-AppxPackage -AllUsers -Name $x -EA SilentlyContinue | Remove-AppxPackage -AllUsers -EA SilentlyContinue; " +
                "Get-AppxPackage -Name $x -EA SilentlyContinue | Remove-AppxPackage -EA SilentlyContinue; " +
                "Get-AppxProvisionedPackage -Online -EA SilentlyContinue | Where-Object {$_.DisplayName -eq $x} | Remove-AppxProvisionedPackage -Online -EA SilentlyContinue" +
                "}";
            ProcStartargs("powershell", $"-NoProfile -ExecutionPolicy Bypass -Command \"{ps}\"");
            _OutPut($"Delete apps ({logName}): {string.Join(", ", packageNames)}");
        }

        private void RemoveWindows10Apps()
        {
            if (checkBoxDeleteApp3d.Checked)
                DeleteAppxGroup(Win11Apps.App3d, "3D");
            if (checkBoxDeleteAppCamera.Checked)
                DeleteAppxGroup(Win11Apps.AppCamera, "Camera");
            if (checkBoxDeleteMailCalendarMaps.Checked)
                DeleteAppxGroup(Win11Apps.MailCalendarMaps, "Mail/Calendar/Maps");
            if (checkBoxDeleteAppBing.Checked)
                DeleteAppxGroup(Win11Apps.AppBing, "Bing apps");
            if (checkBoxDeleteAppZune.Checked)
                DeleteAppxGroup(Win11Apps.AppZune, "Groove/FilmTV");
            if (checkBoxDeleteAppPeopleOneNote.Checked)
                DeleteAppxGroup(Win11Apps.PeopleOneNote, "People/OneNote");
            if (checkBoxDeleteAppPhone.Checked)
                DeleteAppxGroup(Win11Apps.AppPhone, "Phone Link");
            if (checkBoxDeleteAppPhotos.Checked)
                DeleteAppxGroup(Win11Apps.AppPhotos, "Photos");
            if (checkBoxDeleteAppSolit.Checked)
                DeleteAppxGroup(Win11Apps.AppSolitaire, "Solitaire");
            if (checkBoxDeleteAppVoice.Checked)
                DeleteAppxGroup(Win11Apps.AppVoiceRecorder, "Voice Recorder");
            if (checkBoxDeleteAppXBOX.Checked)
                DeleteAppxGroup(Win11Apps.AppXbox, "Xbox");
            // ---- Windows 11 additions ----
            if (checkBoxDeleteAppCopilot.Checked)
                DeleteAppxGroup(Win11Apps.AppCopilot, "Copilot/AI");
            if (checkBoxDeleteAppTeams.Checked)
                DeleteAppxGroup(Win11Apps.AppTeams, "Teams");
            if (checkBoxDeleteAppOutlook.Checked)
                DeleteAppxGroup(Win11Apps.AppOutlook, "Outlook (new)");
            if (checkBoxDeleteAppClipchamp.Checked)
                DeleteAppxGroup(Win11Apps.AppClipchamp, "Clipchamp");
            if (checkBoxDeleteAppWidgets.Checked)
                DeleteAppxGroup(Win11Apps.AppWidgets, "Widgets");
            if (checkBoxDeleteAppWin11Misc.Checked)
                DeleteAppxGroup(Win11Apps.AppWin11Misc, "Win11 misc");
        }

        private void checkBoxDeleteWindows10Apps_CheckedChanged(object sender, EventArgs e)
        {
            checkBoxDeleteApp3d.Enabled = checkBoxDeleteWindows10Apps.Checked;
            checkBoxDeleteAppBing.Enabled = checkBoxDeleteWindows10Apps.Checked;
            checkBoxDeleteAppCamera.Enabled = checkBoxDeleteWindows10Apps.Checked;
            checkBoxDeleteAppPeopleOneNote.Enabled = checkBoxDeleteWindows10Apps.Checked;
            checkBoxDeleteAppPhone.Enabled = checkBoxDeleteWindows10Apps.Checked;
            checkBoxDeleteAppPhotos.Enabled = checkBoxDeleteWindows10Apps.Checked;
            checkBoxDeleteAppSolit.Enabled = checkBoxDeleteWindows10Apps.Checked;
            checkBoxDeleteAppVoice.Enabled = checkBoxDeleteWindows10Apps.Checked;
            checkBoxDeleteAppXBOX.Enabled = checkBoxDeleteWindows10Apps.Checked;
            checkBoxDeleteAppZune.Enabled = checkBoxDeleteWindows10Apps.Checked;
            checkBoxDeleteMailCalendarMaps.Enabled = checkBoxDeleteWindows10Apps.Checked;
            // Windows 11 groups
            checkBoxDeleteAppCopilot.Enabled = checkBoxDeleteWindows10Apps.Checked;
            checkBoxDeleteAppTeams.Enabled = checkBoxDeleteWindows10Apps.Checked;
            checkBoxDeleteAppOutlook.Enabled = checkBoxDeleteWindows10Apps.Checked;
            checkBoxDeleteAppClipchamp.Enabled = checkBoxDeleteWindows10Apps.Checked;
            checkBoxDeleteAppWidgets.Enabled = checkBoxDeleteWindows10Apps.Checked;
            checkBoxDeleteAppWin11Misc.Enabled = checkBoxDeleteWindows10Apps.Checked;
        }

        private void btnDeleteAllWindows10Apps_Click(object sender, EventArgs e)
        {
            if (
                MessageBox.Show(GetTranslateText("Really"), GetTranslateText("Question"), MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) ==
                DialogResult.Yes)
            {
                EnableOrDisableTab(false);
                MessageBox.Show(GetTranslateText("PressOkAndWait15"));
                new Thread(() =>
                {
                    // Delete the whole curated catalog (exact IDs) instead of a
                    // blind "*:*" wildcard, which nuked the Store and core apps.
                    DeleteAppxGroup(Win11Apps.AllCatalogApps().ToArray(), "all catalog apps");
                    Invoke(new MethodInvoker(delegate
                    {
                        EnableOrDisableTab(true);
                        MessageBox.Show(GetTranslateText("Complete"), GetTranslateText("Info"), MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }));
                }).Start();
            }
            else
            {
                MessageBox.Show(@"=(", @"%(");
            }
        }

        private void btnRestoreSystem_Click(object sender, EventArgs e)
        {
            Process.Start(_system32Location + "rstrui.exe");
        }

        private void btnEnableUac_Click(object sender, EventArgs e)
        {
            SetRegValueHklm(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System\", "EnableLUA", "1",
                RegistryValueKind.DWord);
            _OutPut("Enable UAC");
            CheckEnableOrDisableUac();
            if (
                MessageBox.Show(GetTranslateText("CompleteMSG"), GetTranslateText("Info"), MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) == DialogResult.Yes)
            {
                Process.Start("shutdown.exe", "-r -t 0");
            }
        }

        private void btnDisableUac_Click(object sender, EventArgs e)
        {
            SetRegValueHklm(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System\", "EnableLUA", "0",
                RegistryValueKind.DWord);
            _OutPut("Disable UAC");
            CheckEnableOrDisableUac();
            if (
                MessageBox.Show(GetTranslateText("CompleteMSG"), GetTranslateText("Info"), MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) == DialogResult.Yes)
            {
                Process.Start("shutdown.exe", "-r -t 0");
            }
        }

        private void btnEnableWindowsUpdate_Click(object sender, EventArgs e)
        {
            ProcStartargs("powershell", "-command \"Set-Service -Name wuauserv -StartupType Automatic\"");
            RunCmd("/c net start wuauserv");
            RunCmd("/c netsh advfirewall firewall delete rule name=\"WindowsUpdateBlock\"");
            _OutPut("Windows Update enabled");
        }

        private void btnDisableWindowsUpdate_Click(object sender, EventArgs e)
        {
            RunCmd("/c net stop wuauserv");
            ProcStartargs("powershell", "-command \"Set-Service -Name wuauserv -StartupType Disabled\"");
            RunCmd("/c netsh advfirewall firewall delete rule name=\"WindowsUpdateBlock\"");
            RunCmd(
                "/c netsh advfirewall firewall add rule name=\"WindowsUpdateBlock\" dir=out interface=any action=block service=wuauserv");

            _OutPut("Windows Update disabled");
        }

        private void btnOpenAndEditHosts_Click(object sender, EventArgs e)
        {
            ProcStartargs("notepad", _system32Location + @"drivers\etc\hosts");
        }

        /// <summary>Manual "backup the registry now" action.</summary>
        private void btnBackupRegistry_Click(object sender, EventArgs e)
        {
            try
            {
                var folder = RegistryBackup.CreateBackup(m => _OutPut(m, LogLevel.Debug));
                MessageBox.Show(
                    $"Registry backup created in:\n\n{folder}\n\nUse \"Restore registry backup...\" to undo the changes.",
                    "Backup complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                _OutPut("Error creating registry backup", LogLevel.Error);
                _OutPut(ex.Message, LogLevel.Debug);
                MessageBox.Show($"Could not create a registry backup:\n{ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>Undo a previous backup. Newest backup is offered by default.</summary>
        private void btnRestoreRegistryBackup_Click(object sender, EventArgs e)
        {
            var backups = RegistryBackup.ListBackups();
            if (backups.Count == 0)
            {
                MessageBox.Show(
                    "No registry backup was found on this computer yet.\n\n" +
                    "Run the tool once (or press \"Backup registry now\") so a backup is created automatically.",
                    "Nothing to restore", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var newest = backups[0];
            var older = backups.Count - 1;
            var answer = MessageBox.Show(
                $"Restore the registry backup from\n\n{Path.GetFileName(newest)}\n\n" +
                "This puts back the previous values of every setting DWS changed, and removes the ones that did not exist before." +
                (older > 0
                    ? $"\n\n{older} older backup(s) also exist in the same folder if you need them."
                    : string.Empty) +
                "\n\nContinue?",
                "Restore registry backup", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (answer != DialogResult.Yes) return;

            try
            {
                RegistryBackup.Restore(newest, m => _OutPut(m, LogLevel.Debug));
                _OutPut($"Registry backup restored from {newest}.", LogLevel.Info);
                MessageBox.Show(
                    $"Registry restored from:\n{newest}\n\nSome settings only take effect after signing out and back in, or after a reboot.",
                    "Restore complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                _OutPut("Error restoring registry backup", LogLevel.Error);
                _OutPut(ex.Message, LogLevel.Debug);
                MessageBox.Show($"Could not restore the backup:\n{ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void DestroyWindowsSpyingMainForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            Process.GetCurrentProcess().Kill();
        }

        private void DestroyWindowsSpyingMainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            Process.GetCurrentProcess().Kill();
        }

        private void btnProfessionalMode_Click(object sender, EventArgs e)
        {
            ProfessionalModeSet(btnProfessionalMode.Checked);
            Text = btnProfessionalMode.Checked
                ? $"{Text}  !Professional mode!"
                : Text.Replace("  !Professional mode!", string.Empty);
        }

        private void ProfessionalModeSet(bool enableordisable)
        {
            checkBoxKeyLoggerAndTelemetry.Enabled = enableordisable;
            checkBoxAddToHosts.Enabled = enableordisable;
            checkBoxAddToHosts78.Enabled = enableordisable;
            checkBoxDisablePrivateSettings.Enabled = enableordisable;
            checkBoxDisableWindowsDefender.Enabled = enableordisable;
            checkBoxSetDefaultPhoto.Enabled = enableordisable;
            checkBoxSPYTasks.Enabled = enableordisable;
            checkBoxSPYTasks78.Enabled = enableordisable;
            checkBoxDeleteWindows78Updates.Enabled = enableordisable;
            checkBoxDeleteGWX.Enabled = enableordisable;
            btnDeleteAllWindows10Apps.Enabled = _win10 && enableordisable;
            groupBoxUACEdit.Enabled = enableordisable;
            btnDeleteMetroAppsInfo.Enabled = enableordisable;
            btnDeleteOneDrive.Enabled = enableordisable;
            checkBoxDelKeyloggerTW78.Enabled = enableordisable;
            // Windows 11 tweaks + expert-mode destructive toggles
            checkBoxWin11Tweaks.Enabled = enableordisable;
            checkBoxDeleteSpyServices.Enabled = enableordisable;
            checkBoxBlockSpyIPs.Enabled = enableordisable;
            checkedListBoxUpdatesW78.Enabled = enableordisable && checkBoxDeleteWindows78Updates.Checked;
            if (WindowsUtil.SystemRestore_Status() == 0)
            {
                checkBoxCreateSystemRestorePoint.Checked = false;
                checkBoxCreateSystemRestorePoint.Enabled = false;
            }
            else
            {
                checkBoxCreateSystemRestorePoint.Enabled = enableordisable;
            }
        }

        private void btnDeleteMetroAppsInfo_Click(object sender, EventArgs e)
        {
            MessageBox.Show(@"Delete apps: Calculator, Windows Store, Windows Feedback, and other METRO apps.", @"Info",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void btnDeleteOneDrive_Click(object sender, EventArgs e)
        {
            new Thread(DeleteOneDrive).Start();
        }

        void DeleteOneDrive()
        {

            EnableOrDisableTab(false);
            try
            {
                RunCmd("/c taskkill /f /im OneDrive.exe > NUL 2>&1");
                RunCmd("/c ping 127.0.0.1 -n 5 > NUL 2>&1");
                if (File.Exists(_systemPath + @"Windows\System32\OneDriveSetup.exe"))
                    ProcStartargs(_systemPath + @"Windows\System32\OneDriveSetup.exe", "/uninstall");
                if (File.Exists(_systemPath + @"Windows\SysWOW64\OneDriveSetup.exe"))
                    ProcStartargs(_systemPath + @"Windows\SysWOW64\OneDriveSetup.exe", "/uninstall");
                RunCmd("/c ping 127.0.0.1 -n 5 > NUL 2>&1");
                RunCmd("/c rd \"%USERPROFILE%\\OneDrive\" /Q /S > NUL 2>&1");
                RunCmd("/c rd \"C:\\OneDriveTemp\" /Q /S > NUL 2>&1");
                RunCmd("/c rd \"%LOCALAPPDATA%\\Microsoft\\OneDrive\" /Q /S > NUL 2>&1");
                RunCmd("/c rd \"%PROGRAMDATA%\\Microsoft OneDrive\" /Q /S > NUL 2>&1");
                RunCmd(
                    "/c REG DELETE \"HKEY_CLASSES_ROOT\\CLSID\\{018D5C66-4533-4307-9B53-224DE2ED1FE6}\" /f > NUL 2>&1");
                RunCmd(
                    "/c REG DELETE \"HKEY_CLASSES_ROOT\\Wow6432Node\\CLSID\\{018D5C66-4533-4307-9B53-224DE2ED1FE6}\" /f > NUL 2>&1");

                SetRegValueHklm(@"SOFTWARE\Policies\Microsoft\Windows\OneDrive", "DisableFileSyncNGSC", "1",
                    RegistryValueKind.DWord);
            }
            catch (Exception ex)
            {
                Invoke(new MethodInvoker(delegate
                {
                    MessageBox.Show(ex.Message, GetTranslateText("Error"), MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }));
                    _OutPut(ex.Message, LogLevel.Debug);
            }
            Invoke(new MethodInvoker(delegate
            {
                MessageBox.Show(GetTranslateText("Complete"), GetTranslateText("Info"), MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }));
            EnableOrDisableTab(true);
        }

        private void linkLabelSourceCode_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            // DWS 11 Revival: source link now points at this repo. The original
            // upstream projects (Nummer, spinda) are credited in README/About.
            Process.Start("https://github.com/Akunimal/Destroy-Windows-11-Revival");
        }

        private void btnRemoveOldFirewallRules_Click(object sender, EventArgs e)
        {
            string[] rulename =
            {
                "MS Spynet block 1",
                "MS Spynet block 2",
                "MS telemetry block 1",
                "MS telemetry block 2",
                "185.13.160.61_Block",
                "184.86.56.12_Block",
                "204.79.197.200_Block" // bing.com
            };
            foreach (var hostname in rulename)
            {
                RunCmd($"/c netsh advfirewall firewall delete rule name=\"{hostname}\"");
            }

            MessageBox.Show(GetTranslateText("Complete"), GetTranslateText("Info"));
        }

        private void comboBoxLanguageSelect_SelectedIndexChanged(object sender, EventArgs e)
        {
            switch (comboBoxLanguageSelect.Text.Split('|')[0].Replace(" ", ""))
            {
                case "ru-RU":
                    _rm = ru_RU.ResourceManager;
                    ChangeLanguage();
                    break;
                case "fa-IR":
                    _rm = fa_IR.ResourceManager;
                    ChangeLanguage();
                    break;
                case "fr-FR":
                    _rm = fr_FR.ResourceManager;
                    ChangeLanguage();
                    break;
                case "es-ES":
                    _rm = es_ES.ResourceManager;
                    ChangeLanguage();
                    break;
                case "pt-BR":
                    _rm = pt_BR.ResourceManager;
                    ChangeLanguage();
                    break;
                case "de-DE":
                    _rm = de_DE.ResourceManager;
                    ChangeLanguage();
                    break;
                case "pl-PL":
                    _rm = pl_PL.ResourceManager;
                    ChangeLanguage();
                    break;
                case "it-CH":
                    _rm = it_IT.ResourceManager;
                    ChangeLanguage();
                    break;
                case "cs-CZ":
                    _rm = cs_CZ.ResourceManager;
                    ChangeLanguage();
                    break;
                case "zh-CN":
                    _rm = zh_CN.ResourceManager;
                    ChangeLanguage();
                    break;
                case "tr-TR":
                    _rm = tr_TR.ResourceManager;
                    ChangeLanguage();
                    break;
                case "ar-LY":
                    _rm = ar_LY.ResourceManager;
                    ChangeLanguage();
                    break;
                case "nl-NL":
                    _rm = nl_NL.ResourceManager;
                    ChangeLanguage();
                    break;
                case "uk-UA":
                    _rm = uk_UA.ResourceManager;
                    ChangeLanguage();
                    break;
                case "lt-LT":
                    _rm = lt_LT.ResourceManager;
                    ChangeLanguage();
                    break;
                case "ja-JP":
                    _rm = ja_JP.ResourceManager;
                    ChangeLanguage();
                    break;
                default:
                    _rm = en_US.ResourceManager;
                    ChangeLanguage();
                    break;
            }
        }

        private void btnFixRotateScreen_Click(object sender, EventArgs e)
        {
            SetRegValueHklm(@"SOFTWARE\Policies\Microsoft\Windows\LocationAndSensors", "DisableSensors", "0", RegistryValueKind.DWord);
            MessageBox.Show(GetTranslateText("Complete"), @"Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void btnDestroyWindows78Spy_Click(object sender, EventArgs e)
        {
            btnDestroyWindows78Spy.Enabled = false;
            _CloseButton.Enabled = false;
            _fatalErrors = 0;
            new Thread(Dws78MainThread).Start();
        }

        private void Dws78MainThread()
        {
            if (checkBoxAddToHosts78.Checked)
            {
                AddSpyDomainsToHosts();
            }
            if (checkBoxSPYTasks78.Checked)
            {
                DisableSpyingTasks();
            }
            if (checkBoxDeleteWindows78Updates.Checked)
            {
                DeleteUpdatesWin78();
            }
            if (checkBoxDeleteGWX.Checked)
            {
                GwxDelete();
            }
            if (checkBoxDelKeyloggerTW78.Checked)
            {
                DigTrackFullRemove();
            }
            if (_destroyFlag)
                return;
            Invoke(new MethodInvoker(delegate
            {
                btnDestroyWindows78Spy.Enabled = true;
                _CloseButton.Enabled = true;
                MessageBox.Show(GetTranslateText("Complete"), GetTranslateText("Info"), MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }));
        }

        private void GwxDelete()
        {
            try
            {
                var windowsIdentityUser = WindowsIdentity.GetCurrent();
                var gwxDir = Environment.SystemDirectory + @"\GWX";
                if (windowsIdentityUser != null)
                {
                    var userName = windowsIdentityUser.Name.Split('\\')[1];
                    if (Directory.Exists(gwxDir))
                    {
                        RunCmd("/c TASKKILL /F /IM gwx.exe");
                        RunCmd($"/c takeown /f \"{gwxDir}\" /d y");
                        RunCmd($"/c icacls \"{gwxDir}\" /grant {userName}:F /q");
                        RunCmd($"/c rmdir /s /q {gwxDir}");
                        _OutPut("Delete GWX");
                    }
                    else
                    {
                        _OutPut("GWX NOT FOUND", LogLevel.Warning);
                    }
                }
                else
                {
                    _OutPut("Error delete GWX #1221", LogLevel.Error);
                }
            }
            catch (Exception ex)
            {
                _fatalErrors++;
                _OutPut(ex.Message, LogLevel.Error);
            }
        }

        private readonly string[] _updatesnumberlist =
        {
            "2952664",
            "2976978",
            "2990214",
            "3021917",
            "3035583",
            "3042058",
            "3044374",
            "3050265",
            "3065987",
            "3065988",
            "3068708",
            "3075249",
            "3075851",
            "3075853",
            "3080149",
            "3083324",
            "3083325",
            "3083710",
            "3083711",
            "3088195",
            "3093513",
            "3093983",
            "3102810",
            "3112336",
            "971033",
            "976932"
            // THX rgadguard
        };

        private void DeleteUpdatesWin78()
        {
            for (var i = 0; i < checkedListBoxUpdatesW78.Items.Count; i++)
            {
                if (!checkedListBoxUpdatesW78.GetItemChecked(i)) continue;
                var updateNumber = Convert.ToInt32(checkedListBoxUpdatesW78.Items[i].ToString().Replace("KB", null));
                RunCmd($"/c start /wait wusa /uninstall /norestart /quiet /kb:{updateNumber}");
                _OutPut($"Remove and Hide update KB{updateNumber}");
            }
        }


        // LEGACY (2015 IP list). Kept for the Win7/8 path and expert mode only.
        // DWS 11 does NOT touch Windows Firewall (netsh) anymore and does NOT
        // block explorer.exe / WSearch - that broke Store, updates and Explorer
        // on Windows 10/11. Blocking is done via persistent blackhole routes only.
        private void BlockIpAddr()
        {
            string[] ipAddr =
            {
                "104.96.147.3",
                "111.221.29.177",
                "111.221.29.253",
                "131.253.40.37",
                "134.170.115.60",
                "134.170.165.248",
                "134.170.165.253",
                "134.170.185.70",
                "134.170.30.202",
                "137.116.81.24",
                "137.117.235.16",
                "157.55.129.21",
                "157.55.133.204",
                "157.55.240.220",
                "157.56.106.189",
                "157.56.121.89",
                "157.56.91.77",
                "157.56.96.54",
                "168.63.108.233",
                "191.232.139.2",
                "191.232.139.254",
                "191.232.80.58",
                "191.232.80.62",
                "191.237.208.126",
                "2.22.61.43",
                "2.22.61.66",
                "204.79.197.200",
                "207.46.101.29",
                "207.46.114.58",
                "207.46.223.94",
                "207.68.166.254",
                "212.30.134.204",
                "212.30.134.205",
                "23.102.21.4",
                "23.218.212.69",
                "23.223.20.82",
                "23.57.101.163",
                "23.57.107.163",
                "23.57.107.27",
                "23.99.10.11",
                "64.4.23.101",
                "64.4.54.22",
                "64.4.54.32",
                "64.4.6.100",
                "65.39.117.230",
                "65.52.100.11",
                "65.52.100.7",
                "65.52.100.9",
                "65.52.100.91",
                "65.52.100.92",
                "65.52.100.93",
                "65.52.100.94",
                "65.52.108.29",
                "65.52.108.33",
                "65.55.108.23",
                "65.55.138.114",
                "65.55.138.126",
                "65.55.138.186",
                "65.55.252.63",
                "65.55.252.71",
                "65.55.252.92",
                "65.55.252.93",
                "65.55.29.238",
                "65.55.39.10",
                "77.67.29.176"
            };
            foreach (var currentIpAddr in ipAddr)
            {
                // Persistent route to 0.0.0.0 = blackhole. No firewall involved.
                RunCmd($"/c route -p ADD {currentIpAddr} MASK 255.255.255.255 0.0.0.0");
                _OutPut($"Add persistent route (blackhole): {currentIpAddr}");
            }
            _OutPut("IP list blocked via routes. Firewall untouched.");
        }

        private void btnDisableOfficeUpdate_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show(
                @"Office 2016 may stop working after these actions.
Are you sure?", @"Warning", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.No)
            {
                return;
            }
            try
            {
                var windowsIdentityUser = WindowsIdentity.GetCurrent();
                if (windowsIdentityUser != null)
                {
                    var userName = windowsIdentityUser.Name.Split('\\')[1];
                    MessageBox.Show(GetTranslateText("FindOffice16FileT"), GetTranslateText("Info"),
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    var opnFileDialog = new OpenFileDialog
                    {
                        InitialDirectory = @"C:\Program Files\Microsoft Office\root\Office16\",
                        Filter = @"msosync.exe|msosync.exe"
                    };
                    var officePath = @"C:\Program Files\Microsoft Office\root\Office16\msosync.exe";
                    if (opnFileDialog.ShowDialog() == DialogResult.OK)
                    {
                        if (File.Exists(opnFileDialog.FileName))
                        {
                            officePath = opnFileDialog.FileName;
                        }
                    }

                    RunCmd("/c TASKKILL /F /IM msosync.exe");
                    RunCmd($"/c takeown /f \"{officePath}\" /d y");
                    RunCmd($"/c icacls \"{officePath}\" /grant {userName}:F /q");
                    var fileOffice = File.ReadAllBytes(officePath);
                    fileOffice = StringToByteArray(ByteArrayToString(fileOffice).Replace("68747470", "78747470"));
                    // find "http" and replace to "xttp".
                    File.WriteAllBytes(officePath, fileOffice);
                    _OutPut("Complete");
                }
            }
            catch (Exception ex)
            {
                _OutPut(ex.Message, LogLevel.Error);
            }
        }

        public static byte[] StringToByteArray(string hex)
        {
            return Enumerable.Range(0, hex.Length)
                .Where(x => x%2 == 0)
                .Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
                .ToArray();
        }

        public static string ByteArrayToString(byte[] ba)
        {
            var hex = BitConverter.ToString(ba);
            return hex.Replace("-", "");
        }

        private void CaptionWindow_Click(object sender, EventArgs e)
        {
        }

        private void CloseButton_Click(object sender, EventArgs e)
        {
            Process.GetCurrentProcess().Kill();
        }

        private void MinimizeButton_Click(object sender, EventArgs e)
        {
            WindowState = FormWindowState.Minimized;
        }

        private void CaptionWindow_MouseDown(object sender, MouseEventArgs e)
        {
            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
        }

        [DllImport("user32.dll")]
        // ReSharper disable once InconsistentNaming
        public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        private void CloseButton_MouseEnter(object sender, EventArgs e)
        {
            _closeButtonState = 2;
            _CloseButton.Refresh();
            _CloseButton.BackColor = Color.Azure;
        }

        private void CloseButton_MouseLeave(object sender, EventArgs e)
        {
            _closeButtonState = 0;
            _CloseButton.Refresh();
            _CloseButton.BackColor = Color.White;
        }

        private void MinimizeButton_MouseEnter(object sender, EventArgs e)
        {
            _minimizeButtonState = 2;
            MinimizeButton.Refresh();
            MinimizeButton.BackColor = Color.Azure;
        }

        private void MinimizeButton_MouseLeave(object sender, EventArgs e)
        {
            _minimizeButtonState = 0;
            MinimizeButton.Refresh();
            MinimizeButton.BackColor = Color.White;
        }

        private void MinimizeButton_Paint(object sender, PaintEventArgs e)
        {
            var myCurrentPaint = _minimizeButtonState == 0
                ? Resources.minimize
                : _minimizeButtonState == 1 ? Resources.minimize1 : Resources.minimize2;
            e.Graphics.DrawImage(myCurrentPaint, MinimizeButton.Width - Resources.minimize.Width - 5,
                MinimizeButton.Height - Resources.minimize.Height - 7);
        }

        private void CloseButton_Paint(object sender, PaintEventArgs e)
        {
            var myCurrentPaint = _closeButtonState == 0
                ? Resources.close
                : _closeButtonState == 1 ? Resources.close1 : Resources.close2;
            e.Graphics.DrawImage(myCurrentPaint, _CloseButton.Width - Resources.close.Width - 5,
                _CloseButton.Height - Resources.close.Height - 7);
        }

        private void CaptionWindow_Paint(object sender, PaintEventArgs e)
        {
            e.Graphics.FillEllipse(new SolidBrush(Color.WhiteSmoke), 3,3,23,23);
            e.Graphics.DrawImage(Icon.ToBitmap(), 6, 5, 19, 19);
        }

        private void ChangeBorderColor(Color cl)
        {
            // With the Windows 11 look the 1px borders are removed in favour of the
            // native rounded window chrome, so there is nothing to recolour.
            if (_win11 && _win11ModernLook) return;
            try
            {
                Invoke(new MethodInvoker(delegate
                {
                    BorderDown.BackColor = cl;
                    BorderLeft.BackColor = cl;
                    BorderR.BackColor = cl;
                    BorderUP.BackColor = cl;
                }));
            }
            catch (Exception)
            {
                // ignored
            }
        }

        public static Color Rainbow(float progress)
        {
            var div = (Math.Abs(progress%1)*6);
            var ascending = (int) ((div%1)*255);
            var descending = 255 - ascending;

            switch ((int) div)
            {
                case 0:
                    return Color.FromArgb(255, 255, ascending, 0);
                case 1:
                    return Color.FromArgb(255, descending, 255, 0);
                case 2:
                    return Color.FromArgb(255, 0, 255, ascending);
                case 3:
                    return Color.FromArgb(255, 0, descending, 255);
                case 4:
                    return Color.FromArgb(255, ascending, 0, 255);
                default: // case 5:
                    return Color.FromArgb(255, 255, 0, descending);
            }
        }

        private int _closeButtonState;
        private int _minimizeButtonState;

        private void CloseButton_MouseDown(object sender, MouseEventArgs e)
        {
            _closeButtonState = 1;
            _CloseButton.Refresh();
            _CloseButton.BackColor = Color.FromArgb(255, 0, 0);
        }

        private void MinimizeButton_MouseDown(object sender, MouseEventArgs e)
        {
            _minimizeButtonState = 1;
            MinimizeButton.Refresh();
            MinimizeButton.BackColor = Color.CornflowerBlue;
        }

        #region Language

        private string _GetLang(IEnumerable<string> args)
        {
            string languageName = null;
            // check args lang
            foreach (
                var currentArg in args.Where(currentArg => currentArg.IndexOf("/lang=", StringComparison.Ordinal) > -1))
            {
                languageName = currentArg.Replace("/lang=", null);
            }
            return languageName;
        }

        private void SetLanguage(string currentlang = null)
        {
            if (currentlang == null)
            {
                currentlang = CultureInfo.CurrentUICulture.Name.ToLower();
            }
            if (currentlang.IndexOf("ru", StringComparison.Ordinal) > -1)
            {
                _rm = ru_RU.ResourceManager; // change resource language manager.
                comboBoxLanguageSelect.Text = @"ru-RU | Русский"; // set combobox text language.
            }
            else if (currentlang.IndexOf("fa", StringComparison.Ordinal) > -1)
            {
                _rm = fr_FR.ResourceManager;
                comboBoxLanguageSelect.Text = @"fa-IR | Persian";
            }
            else if (currentlang.IndexOf("fr", StringComparison.Ordinal) > -1)
            {
                _rm = fr_FR.ResourceManager;
                comboBoxLanguageSelect.Text = @"fr-FR | French";
            }
            else if (currentlang.IndexOf("es", StringComparison.Ordinal) > -1)
            {
                _rm = es_ES.ResourceManager;
                comboBoxLanguageSelect.Text = @"es-ES | Spanish";
            }
            else if (currentlang.IndexOf("pt", StringComparison.Ordinal) > -1)
            {
                _rm = pt_BR.ResourceManager;
                comboBoxLanguageSelect.Text = @"pt-BR | Portuguese";
            }
            else if (currentlang.IndexOf("de", StringComparison.Ordinal) > -1)
            {
                _rm = de_DE.ResourceManager;
                comboBoxLanguageSelect.Text = @"de-DE | German";
            }
            else if (currentlang.IndexOf("pl", StringComparison.Ordinal) > -1)
            {
                _rm = pl_PL.ResourceManager;
                comboBoxLanguageSelect.Text = @"pl-PL | Polish";
            }
            else if (currentlang.IndexOf("it", StringComparison.Ordinal) > -1)
            {
                _rm = it_IT.ResourceManager;
                comboBoxLanguageSelect.Text = @"it-CH | Italian";
            }
            else if (currentlang.IndexOf("cs", StringComparison.Ordinal) > -1)
            {
                _rm = cs_CZ.ResourceManager;
                comboBoxLanguageSelect.Text = @"cs-CZ | Czech";
            }
            else if (currentlang.IndexOf("cn", StringComparison.Ordinal) > -1)
            {
                _rm = zh_CN.ResourceManager;
                comboBoxLanguageSelect.Text = @"zh-CN | 中文(简体)";
            }
            else if (currentlang.IndexOf("tr", StringComparison.Ordinal) > -1)
            {
                _rm = tr_TR.ResourceManager;
                comboBoxLanguageSelect.Text = @"tr-TR | Turkish";
            }
            else if (currentlang.IndexOf("ar", StringComparison.Ordinal) > -1)
            {
                _rm = ar_LY.ResourceManager;
                comboBoxLanguageSelect.Text = @"ar-LY | Arabic";
            }
            else if (currentlang.IndexOf("nl", StringComparison.Ordinal) > -1)
            {
                _rm = nl_NL.ResourceManager;
                comboBoxLanguageSelect.Text = @"nl-NL | Dutch";
            }
            else if (currentlang.IndexOf("UA", StringComparison.Ordinal) > -1)
            {
                _rm = uk_UA.ResourceManager;
                comboBoxLanguageSelect.Text = @"uk-UA | Українська";
            }
            else if (currentlang.IndexOf("lt", StringComparison.Ordinal) > -1)
            {
                _rm = lt_LT.ResourceManager;
                comboBoxLanguageSelect.Text = @"lt-LT | Lithuanian";
            }
            else if (currentlang.IndexOf("ja", StringComparison.Ordinal) > -1)
            {
                _rm = ja_JP.ResourceManager;
                comboBoxLanguageSelect.Text = @"ja-JP | 日本語";
            }
            else
            {
                _rm = en_US.ResourceManager;
                comboBoxLanguageSelect.Text = @"en-US | English";
            }
        }

        private void ChangeLanguage()
        {
            // Transtale all controls
            ReadmeRichTextBox.Text = GetTranslateText("ReadMeTextBox");
            btnFixRotateScreen.Text = GetTranslateText("btnFixRotateScreen");
            tabPageMain.Text = GetTranslateText("tabPageMain");
            tabPageAbout.Text = GetTranslateText("tabPageAbout");
            tabPageReadMe.Text = GetTranslateText("tabPageReadMe");
            tabPageSettings.Text = GetTranslateText("tabPageSettings");
            tabPageUtilites.Text = GetTranslateText("tabPageUtilites");
            btnDeleteAllWindows10Apps.Text = GetTranslateText("btnDeleteAllWindows10Apps");
            btnDeleteOneDrive.Text = GetTranslateText("btnDeleteOneDrive");
            btnOpenAndEditHosts.Text = GetTranslateText("btnOpenAndEditHosts");
            btnProfessionalMode.Text = GetTranslateText("btnProfessionalMode");
            btnRestoreSystem.Text = GetTranslateText("btnRestoreSystem");
            checkBoxAddToHosts.Text = GetTranslateText("checkBoxAddToHosts");
            checkBoxAddToHosts78.Text = GetTranslateText("checkBoxAddToHosts");
            checkBoxCreateSystemRestorePoint.Text = GetTranslateText("checkBoxCreateSystemRestorePoint");
            checkBoxDeleteWindows10Apps.Text = GetTranslateText("checkBoxDeleteWindows10Apps");
            checkBoxDisablePrivateSettings.Text = GetTranslateText("checkBoxDisablePrivateSettings");
            checkBoxDisableWindowsDefender.Text = GetTranslateText("checkBoxDisableWindowsDefender");
            checkBoxKeyLoggerAndTelemetry.Text = GetTranslateText("checkBoxKeyLoggerAndTelemetry");
            checkBoxDelKeyloggerTW78.Text = GetTranslateText("checkBoxKeyLoggerAndTelemetry");
            checkBoxSetDefaultPhoto.Text = GetTranslateText("checkBoxSetDefaultPhoto");
            checkBoxSPYTasks.Text = GetTranslateText("checkBoxSPYTasks");
            checkBoxSPYTasks78.Text = GetTranslateText("checkBoxSPYTasks");
            checkBoxDeleteWindows78Updates.Text = GetTranslateText("checkBoxDeleteWindows78Updates");
            checkBoxDeleteGWX.Text = GetTranslateText("checkBoxDeleteGWX");
            labelUninstallUpdates.Text = GetTranslateText("labelUninstallUpdates");
            labelInfoDeleteMetroApps.Text = GetTranslateText("labelInfoDeleteMetroApps");
            btnEnableUac.Text = $"{GetTranslateText("Enable")} UAC";
            btnDisableUac.Text = $"{GetTranslateText("Disable")} UAC";
            btnDisableOfficeUpdate.Text = $"{GetTranslateText("Disable")} Office 2016 Telemetry";
            btnDisableWindowsUpdate.Text = $"{GetTranslateText("Disable")} Windows Update";
            btnEnableWindowsUpdate.Text = $"{GetTranslateText("Enable")} Windows Update";
            checkBoxDeleteApp3d.Text = $"{GetTranslateText("Delete")} Builder 3D";
            checkBoxDeleteAppCamera.Text = $"{GetTranslateText("Delete")} Camera";
            checkBoxDeleteMailCalendarMaps.Text = $"{GetTranslateText("Delete")} Mail, Calendar, Maps";
            checkBoxDeleteAppBing.Text = $"{GetTranslateText("Delete")} Money, Sports, News, Weather";
            checkBoxDeleteAppZune.Text = $"{GetTranslateText("Delete")} Groove Music, Film TV";
            checkBoxDeleteAppPeopleOneNote.Text = $"{GetTranslateText("Delete")} People, OneNote";
            checkBoxDeleteAppPhone.Text = $"{GetTranslateText("Delete")} Phone Companion";
            checkBoxDeleteAppPhotos.Text = $"{GetTranslateText("Delete")} Photos";
            checkBoxDeleteAppSolit.Text = $"{GetTranslateText("Delete")} Solitaire Collection";
            checkBoxDeleteAppVoice.Text = $"{GetTranslateText("Delete")} Voice Recorder";
            checkBoxDeleteAppXBOX.Text = $"{GetTranslateText("Delete")} XBOX";
            // Windows 11 additions (fallback to en-US if key missing)
            checkBoxDeleteAppCopilot.Text = GetTranslateText("checkBoxDeleteAppCopilot");
            checkBoxDeleteAppTeams.Text = GetTranslateText("checkBoxDeleteAppTeams");
            checkBoxDeleteAppOutlook.Text = GetTranslateText("checkBoxDeleteAppOutlook");
            checkBoxDeleteAppClipchamp.Text = GetTranslateText("checkBoxDeleteAppClipchamp");
            checkBoxDeleteAppWidgets.Text = GetTranslateText("checkBoxDeleteAppWidgets");
            checkBoxDeleteAppWin11Misc.Text = GetTranslateText("checkBoxDeleteAppWin11Misc");
            checkBoxWin11Tweaks.Text = GetTranslateText("checkBoxWin11Tweaks");
            checkBoxDeleteSpyServices.Text = GetTranslateText("checkBoxDeleteSpyServices");
            checkBoxBlockSpyIPs.Text = GetTranslateText("checkBoxBlockSpyIPs");
            checkBoxWin11DarkMode.Text = GetTranslateText("checkBoxWin11DarkMode");
            btnRemoveOldFirewallRules.Text = GetTranslateText("RemoveAllOldFirewallRules");
            btnBackupRegistry.Text = GetTranslateText("btnBackupRegistry");
            btnRestoreRegistryBackup.Text = GetTranslateText("btnRestoreRegistryBackup");
        }

        private string GetTranslateText(string name)
        {
            try
            {
                var textupdate = _rm.GetString(name);
                if (textupdate != null)
                {
                    return textupdate;
                }
                else
                {
                    return en_US.ResourceManager.GetString(name);
                }
            }
                // ReSharper disable once UnusedVariable
            catch (Exception ex)
            {
                _OutPut($"Error get translate {name}. \nError: {ex.Message}", LogLevel.Debug);
                return en_US.ResourceManager.GetString(name);
            }
        }

        #endregion

        #region Output

        private void LogOutputTextBox_TextChanged(object sender, EventArgs e)
        {
            LogOutputTextBox.SelectionStart = LogOutputTextBox.Text.Length;
            LogOutputTextBox.ScrollToCaret();
        }

        private void _OutPutSplit()
        {
            try
            {
                Invoke(new MethodInvoker(OutPutSplitInvoke));
            }
            catch
            {
                try
                {
                    OutPutSplitInvoke();
                }
                catch (Exception ex)
                {
                    _fatalErrors++;
                    _errorsList.Add($"Error in outputsplit. Message: {ex.Message}");
                }
            }
        }

        private void _OutPut(string str, LogLevel logLevel = LogLevel.Info)
        {
            try
            {
                Invoke(new MethodInvoker(delegate { _OutPutInvoke(str, logLevel); }));
            }
            catch
            {
                try
                {
                    _OutPutInvoke(str, logLevel);
                }
                catch (Exception ex)
                {
                    _fatalErrors++;
                    _errorsList.Add($"Error in output. Message: {ex.Message}");
                }
            }
        }

        private enum LogLevel // thx TRoskop
        {
            Info,
            Warning,
            Error,
            FatalError,
            Debug
        };

        private void _OutPutInvoke(string str, LogLevel logLevel)
        {
            if (logLevel == LogLevel.Debug && string.IsNullOrEmpty(str))
                return;

            switch (logLevel)
            {
                case LogLevel.Info:
                    str = "[INFO] " + str;
                    break;
                case LogLevel.Warning:
                    str = "[WARNING] " + str;
                    break;
                case LogLevel.Error:
                    str = "[ERROR] " + str;
                    break;
                case LogLevel.FatalError:
                    str = "[!! FATAL ERROR !!] " + str;
                    break;
                case LogLevel.Debug:
                    str = "[DEBUG] " + str;
                    break;
            }
            File.WriteAllText(LogFileName, File.ReadAllText(LogFileName) + str + Environment.NewLine);
            Console.WriteLine(str);
            LogOutputTextBox.Text += str + Environment.NewLine;
        }

        private void OutPutSplitInvoke()
        {
            var splittext = $"=========================={Environment.NewLine}";
            File.WriteAllText(LogFileName, File.ReadAllText(LogFileName) + splittext);
            LogOutputTextBox.Text += splittext;
        }

        #endregion

        #region registry

        private void SetRegValueHkcu(string regkeyfolder, string paramname, string paramvalue, RegistryValueKind keytype)
        {
            var registryKey = Registry.CurrentUser.CreateSubKey(regkeyfolder);
            registryKey?.Close();
            var myKey = Registry.CurrentUser.OpenSubKey(regkeyfolder, RegistryKeyPermissionCheck.ReadWriteSubTree,
                RegistryRights.FullControl);
            try
            {
                myKey?.SetValue(paramname, paramvalue, keytype);
            }
            catch (Exception ex)
            {
                _fatalErrors++;
                _errorsList.Add($"Error SetRegValueHkcu. Message: {ex.Message}");
                _OutPut($"{GetTranslateText("Error")}: {ex.Message}", LogLevel.Error);
            }

            myKey?.Close();
        }

        private void SetRegValueHklm(string regkeyfolder, string paramname, string paramvalue, RegistryValueKind keytype)
        {
            var registryKey = Registry.LocalMachine.CreateSubKey(regkeyfolder);
            registryKey?.Close();
            var myKey = Registry.LocalMachine.OpenSubKey(regkeyfolder,
                RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryRights.FullControl);
            try
            {
                myKey?.SetValue(paramname, paramvalue, keytype);
            }
            catch (Exception ex)
            {
                _fatalErrors++;
                _errorsList.Add($"Error SetRegValueHklm. Message: {ex.Message}");
                _OutPut($"{GetTranslateText("Error")}: {ex.Message}", LogLevel.Error);
            }
            myKey?.Close();
        }

        #endregion

        private void checkBoxDeleteWindows78Updates_CheckedChanged(object sender, EventArgs e)
        {
            checkedListBoxUpdatesW78.Enabled = checkBoxDeleteWindows78Updates.Checked;
        }

        private void DestroyWindowsSpyingMainForm_Activated(object sender, EventArgs e)
        {
            CaptionWindow.ForeColor = Color.FromArgb(64, 64, 64);
        }

        private void DestroyWindowsSpyingMainForm_Deactivate(object sender, EventArgs e)
        {
            CaptionWindow.ForeColor = Color.FromArgb(164, 164, 164);
        }
    }
}
