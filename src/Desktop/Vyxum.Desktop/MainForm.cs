using System.Drawing;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Vyxum.Desktop;

/// <summary>The only window: a waiting view while the web application starts, then the application itself in WebView2.</summary>
internal sealed class MainForm : Form
{
        // Colors from tasarim/TASARIM.md.
    private static readonly Color Background = ColorTranslator.FromHtml("#FBFCFC");
    private static readonly Color Ink = ColorTranslator.FromHtml("#101820");
    private static readonly Color Secondary = ColorTranslator.FromHtml("#4A5560");
    private static readonly Color Accent = ColorTranslator.FromHtml("#0B4F6C");

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(2) };

    private readonly DesktopSettings _settings;
    private readonly WebView2 _webView = new() { Dock = DockStyle.Fill, Visible = false, DefaultBackgroundColor = Background };
    private readonly Panel _waitPanel = new() { Dock = DockStyle.Fill, BackColor = Background, Padding = new Padding(48, 48, 48, 24) };
    private readonly Label _status = new() { AutoSize = true, ForeColor = Ink, Font = new Font("Segoe UI", 20f, FontStyle.Regular) };
    private readonly Label _detail = new() { AutoSize = true, ForeColor = Secondary, Font = new Font("Segoe UI", 10f), Margin = new Padding(0, 8, 0, 0) };
    private readonly Button _retry = new()
    {
        AutoSize = true,
        MinimumSize = new Size(160, 44),
        FlatStyle = FlatStyle.Flat,
        BackColor = Accent,
        ForeColor = Color.White,
        Font = new Font("Segoe UI", 10f, FontStyle.Bold),
        Margin = new Padding(0, 24, 0, 0),
        Visible = false,
    };

    private CancellationTokenSource? _wait;
    private bool _fullScreen;
    private bool _shown;

    public MainForm(DesktopSettings settings)
    {
        _settings = settings;
        Text = "Vyxum";
        BackColor = Background;
        StartPosition = FormStartPosition.WindowsDefaultLocation;
        MinimumSize = new Size(640, 480);
        WindowState = FormWindowState.Maximized;
        KeyPreview = true;
        try
        {
            Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? Application.ExecutablePath);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            // The default form icon is acceptable.
        }

        _retry.FlatAppearance.BorderSize = 0;
        _retry.Text = Strings.Get("TryAgain");
        _retry.Click += (_, _) => StartWaiting();

        var rule = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Ink };
        var stack = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Background,
            Padding = new Padding(0, 24, 0, 0),
        };
        stack.Controls.AddRange([_status, _detail, _retry]);
        _waitPanel.Controls.Add(stack);
        _waitPanel.Controls.Add(rule);

        Controls.Add(_webView);
        Controls.Add(_waitPanel);
    }

    /// <summary>Called from a thread-pool thread when a second launch asks for this window.</summary>
    public void ActivateFromAnotherInstance()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        BeginInvoke(() =>
        {
            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Maximized;
            }

            Activate();
        });
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_settings.FullScreen)
        {
            SetFullScreen(true);
        }

        _shown = true;
        StartWaiting();
        await InitializeWebViewAsync();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _wait?.Cancel();
        base.OnFormClosing(e);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.F11)
        {
            SetFullScreen(!_fullScreen);
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void SetFullScreen(bool on)
    {
        if (on == _fullScreen)
        {
            return;
        }

        _fullScreen = on;
        SuspendLayout();
        // Going through Normal makes a maximized borderless window cover the taskbar.
        WindowState = FormWindowState.Normal;
        FormBorderStyle = on ? FormBorderStyle.None : FormBorderStyle.Sizable;
        WindowState = FormWindowState.Maximized;
        ResumeLayout();
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            var userData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vyxum", "WebView2");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userData);
            await _webView.EnsureCoreWebView2Async(environment);
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or InvalidOperationException or System.Runtime.InteropServices.COMException or ArgumentException)
        {
            Program.OpenInBrowser(_settings.Url);
            Close();
            return;
        }

        var core = _webView.CoreWebView2;
#if DEBUG
        core.Settings.AreDefaultContextMenusEnabled = true;
        core.Settings.AreDevToolsEnabled = true;
#else
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
#endif
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = true;

        core.NavigationStarting += (_, args) =>
        {
            if (Uri.TryCreate(args.Uri, UriKind.Absolute, out var target) && !DesktopSettings.IsSameOrigin(target, _settings.Url))
            {
                args.Cancel = true;
                OpenExternally(target);
            }
        };
        core.NewWindowRequested += (_, args) =>
        {
            args.Handled = true;
            if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var target))
            {
                return;
            }

            if (DesktopSettings.IsSameOrigin(target, _settings.Url))
            {
                core.Navigate(target.AbsoluteUri);
            }
            else
            {
                OpenExternally(target);
            }
        };
        core.NavigationCompleted += (_, args) =>
        {
            // The server went away (for example it restarts after a database change): wait for it again.
            if (!args.IsSuccess
                && args.WebErrorStatus is CoreWebView2WebErrorStatus.CannotConnect or CoreWebView2WebErrorStatus.ConnectionAborted
                    or CoreWebView2WebErrorStatus.ConnectionReset or CoreWebView2WebErrorStatus.Disconnected or CoreWebView2WebErrorStatus.Timeout
                && !_waitPanel.Visible)
            {
                StartWaiting();
            }
        };
        // The WebView2 control keeps the keyboard focus, so F11 is reported by the page and handled here.
        await core.AddScriptToExecuteOnDocumentCreatedAsync(
            "window.addEventListener('keydown', e => { if (e.key === 'F11') { e.preventDefault(); window.chrome.webview.postMessage('toggle-fullscreen'); } }, true);");
        core.WebMessageReceived += (_, args) =>
        {
            if (args.TryGetWebMessageAsString() == "toggle-fullscreen"
                && Uri.TryCreate(args.Source, UriKind.Absolute, out var source)
                && DesktopSettings.IsSameOrigin(source, _settings.Url))
            {
                SetFullScreen(!_fullScreen);
            }
        };

        _webReady = true;
        NavigateIfReady();
    }

    private bool _webReady;
    private bool _serverReady;

    private async void StartWaiting()
    {
        _wait?.Cancel();
        var source = _wait = new CancellationTokenSource();

        _serverReady = false;
        _webView.Visible = false;
        _waitPanel.Visible = true;
        _retry.Visible = false;
        _status.Text = Strings.Get("Starting");
        _detail.Text = Strings.Get("StartingDetail", _settings.Url.GetLeftPart(UriPartial.Authority));

        try
        {
            var healthy = await HealthProbe.WaitAsync(Http, _settings.Url, HealthProbe.DefaultInterval, HealthProbe.DefaultTimeout, source.Token);
            if (healthy)
            {
                _serverReady = true;
                NavigateIfReady();
            }
            else
            {
                _status.Text = Strings.Get("Failed");
                _detail.Text = Strings.Get("FailedDetail", _settings.Url.GetLeftPart(UriPartial.Authority));
                _retry.Visible = true;
                _retry.Focus();
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer wait, or the window is closing.
        }
    }

    private void NavigateIfReady()
    {
        if (!_shown || !_webReady || !_serverReady)
        {
            return;
        }

        _webView.CoreWebView2.Navigate(_settings.Url.AbsoluteUri);
        _webView.Visible = true;
        _waitPanel.Visible = false;
        _webView.Focus();
    }

    private static void OpenExternally(Uri target)
    {
        if (target.Scheme is "http" or "https")
        {
            Program.OpenInBrowser(target);
        }
    }
}
