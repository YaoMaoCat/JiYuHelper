using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace JiYuHelper
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private Window? _window;

        /// <summary>主窗口句柄 (供文件选择器等 WinRT 控件初始化)</summary>
        public static IntPtr MainWindowHandle { get; private set; }

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            InitializeComponent();
            UnhandledException += OnUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandled;
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            try
            {
                _window = new MainWindow();
                _window.Activate();
                MainWindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(_window);
            }
            catch (Exception ex)
            {
                LogCrash("OnLaunched", ex);
                throw;
            }
        }

        // ---------- 崩溃诊断: 写入 exe 目录 crash.log ----------

        private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            LogCrash("XAML UnhandledException", e.Exception);
        }

        private static void OnDomainUnhandled(object sender, System.UnhandledExceptionEventArgs e)
        {
            LogCrash("AppDomain UnhandledException", e.ExceptionObject as Exception);
        }

        /// <summary>将异常写入程序目录 crash.log (启动闪退定位用)</summary>
        private static void LogCrash(string tag, Exception? ex)
        {
            try
            {
                string path = System.IO.Path.Combine(AppContext.BaseDirectory, "crash.log");
                File.AppendAllText(path,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {tag}: {ex}\n" +
                    new string('-', 60) + "\n");
            }
            catch { /* 日志写入失败则忽略 */ }
        }
    }
}
