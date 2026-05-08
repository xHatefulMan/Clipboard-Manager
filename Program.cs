using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ClipboardManager
{
    internal static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        static readonly Mutex mutex = new Mutex(false, "ClipboardManager_SingleInstance");
        static readonly string signalFile = Path.Combine(Path.GetTempPath(), "ClipboardManager_show.signal");
        static Form1 mainForm;

        [STAThread]
        static void Main()
        {
            bool hasHandle = false;
            try { hasHandle = mutex.WaitOne(100, false); }
            catch (AbandonedMutexException) { hasHandle = true; }

            if (!hasHandle)
            {
                // Instance déjà active → envoie signal pour ouvrir la fenêtre
                try { File.WriteAllText(signalFile, "show"); } catch { }
                return;
            }

            try
            {
                SetProcessDPIAware();
                ApplicationConfiguration.Initialize();

                bool startedWithWindows = Environment.GetCommandLineArgs().Contains("--startup");
                mainForm = new Form1();

                // Surveille le fichier signal
                var signalWatcher = new System.Windows.Forms.Timer { Interval = 300 };
                signalWatcher.Tick += (s, e) =>
                {
                    try
                    {
                        if (File.Exists(signalFile))
                        {
                            File.Delete(signalFile);
                            mainForm.ShowFromExternal();
                        }
                    }
                    catch { }
                };
                signalWatcher.Start();

                if (startedWithWindows)
                {
                    // Démarrage Windows → arrière-plan silencieux
                    mainForm.ShowInTaskbar = false;
                    mainForm.Opacity = 0;
                    mainForm.Show();
                    mainForm.Hide();
                    mainForm.Opacity = 1;
                }
                else
                {
                    // Lancement manuel → affiche la fenêtre
                    mainForm.Show();
                }

                Application.Run();
            }
            finally
            {
                if (hasHandle) mutex.ReleaseMutex();
            }
        }
    }
}