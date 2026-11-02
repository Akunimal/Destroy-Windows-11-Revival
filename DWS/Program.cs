using System;
using System.Windows.Forms;
using DWS.Core.Logging;
using DWS.Core.Security;

namespace DWS_Lite
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            if (args != null && args.Length >= 2 && string.Equals(args[0], "/WORKER", StringComparison.OrdinalIgnoreCase))
            {
                // Headless privileged SYSTEM worker mode executed via Named Pipe IPC
                SystemPrivilegeBroker.RunWorkerClient(args[1], command =>
                {
                    TransactionJournal.Log(JournalEntryType.SecurityOperation, $"SYSTEM worker processed IPC command: {command}");
                    return "OK:SYSTEM_PROCESSED";
                });
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            // Opt into dark mode so native scroll bars render dark (Windows 11).
            DWS_Lite.lib.Win11Visual.EnableDarkModeForApp();
            Application.Run(new MainDwsForm(args));
        }
    }
}
