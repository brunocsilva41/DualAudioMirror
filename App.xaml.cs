using System;
using System.Windows;
using System.Windows.Threading;

namespace DualAudioMirror
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            base.OnStartup(e);
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Log("EXCEÇÃO NÃO TRATADA (interface): " + e.Exception);
            try
            {
                MessageBox.Show(
                    "Ocorreu um erro inesperado:\n\n" + e.Exception.Message +
                    "\n\nDetalhes no log:\n" + Diag.LogFilePath,
                    "DualAudioMirror",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (Exception)
            {
            }
            e.Handled = true;
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Log("EXCEÇÃO NÃO TRATADA: " + e.ExceptionObject);
        }

        private static void Log(string message)
        {
            try
            {
                Diag.Log(message);
            }
            catch (Exception)
            {
            }
        }
    }
}
