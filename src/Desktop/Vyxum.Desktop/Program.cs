using System.Diagnostics;
using Microsoft.Web.WebView2.Core;

namespace Vyxum.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var settings = DesktopSettings.Load(args);

        using var instance = SingleInstance.TryAcquire();
        if (instance is null)
        {
            return;
        }

        try
        {
            _ = CoreWebView2Environment.GetAvailableBrowserVersionString();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            // Without the WebView2 runtime the same web application still works in the default browser.
            OpenInBrowser(settings.Url);
            return;
        }

        ApplicationConfiguration.Initialize();
        var form = new MainForm(settings);
        instance.ListenForActivation(form.ActivateFromAnotherInstance);
        Application.Run(form);
    }

    internal static void OpenInBrowser(Uri url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Nothing sensible is left to do without a default browser.
        }
    }
}
