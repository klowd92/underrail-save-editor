using System;
using System.Windows;

using UnderrailSaveEditor.App.Services;

namespace UnderrailSaveEditor.App
{
    public partial class App : Application
    {
        public App()
        {
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            Dialogs.Show($"Unhandled exception: {e.Exception.Message}\n\n{e.Exception.StackTrace}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception;
            Dialogs.Show($"Fatal error: {ex?.Message}\n\n{ex?.StackTrace}",
                "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
