using System;
using System.Windows.Forms;

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
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            // Opt into dark mode so native scroll bars render dark (Windows 11).
            DWS_Lite.lib.Win11Visual.EnableDarkModeForApp();
            Application.Run(new MainDwsForm(args));
        }
    }
}
