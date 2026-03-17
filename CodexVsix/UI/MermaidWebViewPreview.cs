using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CodexVsix.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace CodexVsix.UI;

internal sealed class MermaidWebViewPreview : Grid
{
    private const string MermaidAssetFileName = "mermaid.min.js";
    private const string MermaidHostFileName = "mermaid-preview.html";
    private const string MermaidBundleResourceName = "CodexVsix.UI.Assets.mermaid.min.js";
    private const string MermaidAssetsHostName = "appassets.codexvsix.local";
    private const int MermaidBootstrapTimeoutMs = 8000;
    private static readonly Lazy<string> MermaidBundle = new(LoadMermaidBundle);
    private static readonly Lazy<string> MermaidHostHtml = new(BuildHostHtml);
    private readonly string _code;
    private readonly WebView2 _webView;
    private readonly Image _snapshotImage;
    private readonly Border _statusHost;
    private readonly TextBlock _statusText;
    private readonly DispatcherTimer _bootstrapTimer;
    private readonly LocalizationService _localization;
    private bool _isInitialized;
    private bool _snapshotRequested;
    private bool _isDisposed;
    private bool _renderReady;
    private bool _hasTerminalState;

    public MermaidWebViewPreview(string code)
    {
        _code = code ?? string.Empty;
        _localization = new LocalizationService();
        Height = 180;
        MinHeight = 120;
        Margin = new Thickness(0, 2, 0, 0);
        VerticalAlignment = VerticalAlignment.Top;
        ClipToBounds = true;

        _webView = new WebView2
        {
            Visibility = Visibility.Visible,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
            Height = Height
        };
        _webView.SetValue(UIElement.OpacityProperty, 0d);

        _snapshotImage = new Image
        {
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly
        };

        _statusText = new TextBlock
        {
            Text = _localization.MermaidLoadingPreview,
            Foreground = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center
        };

        _statusHost = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(24, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(36, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14),
            Child = _statusText
        };

        _bootstrapTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(MermaidBootstrapTimeoutMs + 2000)
        };
        _bootstrapTimer.Tick += OnBootstrapTimerTick;

        Children.Add(_snapshotImage);
        Children.Add(_webView);
        Children.Add(_statusHost);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_renderReady && !_isDisposed)
        {
            _statusHost.Visibility = Visibility.Collapsed;
            _snapshotImage.Visibility = Visibility.Collapsed;
            _webView.Visibility = Visibility.Visible;
            _webView.SetValue(UIElement.OpacityProperty, 1d);
            _webView.IsHitTestVisible = true;
            return;
        }

        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;
        _ = InitializeAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_snapshotImage.Source is not null || _hasTerminalState)
        {
            DisposeWebView();
        }
    }

    private async Task InitializeAsync()
    {
        try
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CodexVsix",
                "WebView2");
            Directory.CreateDirectory(userDataFolder);
            var assetsFolder = EnsureLocalAssets();

            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
            await _webView.EnsureCoreWebView2Async(environment);
            _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            _webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            _webView.CoreWebView2.Settings.IsZoomControlEnabled = false;
            _webView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;
            _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                MermaidAssetsHostName,
                assetsFolder,
                CoreWebView2HostResourceAccessKind.Allow);
            ResetBootstrapTimer();
            _webView.CoreWebView2.Navigate(BuildPreviewUri(_code));
        }
        catch (Exception ex)
        {
            Trace.WriteLine(ex);
            Fail(_localization.MermaidInitFailed);
        }
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.IsSuccess)
        {
            return;
        }

        Fail(string.Format(CultureInfo.CurrentUICulture, _localization.MermaidLoadFailedFormat, e.WebErrorStatus));
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var message = e.TryGetWebMessageAsString() ?? string.Empty;
        if (message.StartsWith("height:", StringComparison.Ordinal))
        {
            var rawHeight = message.Substring("height:".Length);
            if (double.TryParse(rawHeight, NumberStyles.Float, CultureInfo.InvariantCulture, out var height))
            {
                ApplyMeasuredHeight(Math.Max(120, height));
            }

            return;
        }

        if (string.Equals(message, "ready", StringComparison.Ordinal))
        {
            _renderReady = true;
            StopBootstrapTimer();
            _statusHost.Visibility = Visibility.Collapsed;
            _snapshotImage.Visibility = Visibility.Collapsed;
            _webView.Visibility = Visibility.Visible;
            _webView.SetValue(UIElement.OpacityProperty, 1d);
            _webView.IsHitTestVisible = true;

            return;
        }

        if (message.StartsWith("error:", StringComparison.Ordinal))
        {
            var detail = message.Substring("error:".Length).Trim();
            Fail(string.IsNullOrWhiteSpace(detail)
                ? _localization.MermaidRenderFailed
                : string.Format(CultureInfo.CurrentUICulture, _localization.MermaidRenderFailedFormat, detail));
        }
    }

    private void ShowStatus(string text)
    {
        _statusText.Text = text;
        _statusHost.Visibility = Visibility.Visible;
        _snapshotImage.Visibility = Visibility.Collapsed;
        _webView.Visibility = Visibility.Visible;
        _webView.SetValue(UIElement.OpacityProperty, 0d);
        _webView.IsHitTestVisible = false;
    }

    private async Task FreezePreviewAsync()
    {
        try
        {
            if (_isDisposed || _webView.CoreWebView2 is null)
            {
                return;
            }

            using var stream = new MemoryStream();
            await _webView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
            stream.Position = 0;

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();

            _hasTerminalState = true;
            StopBootstrapTimer();
            _snapshotImage.Source = image;
            _snapshotImage.Visibility = Visibility.Visible;
            _statusHost.Visibility = Visibility.Collapsed;
            _webView.Visibility = Visibility.Hidden;
            DisposeWebView();
            ApplyMeasuredHeight(Math.Max(120, image.Height));
        }
        catch (Exception ex)
        {
            if (!IsLoaded && !_isDisposed)
            {
                _snapshotRequested = false;
                return;
            }

            Trace.WriteLine(ex);
            Fail(_localization.MermaidFreezeFailed);
        }
    }

    private void DisposeWebView()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        StopBootstrapTimer();
        if (_webView.CoreWebView2 is not null)
        {
            _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
            _webView.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
        }

        _webView.Dispose();
    }

    private void OnBootstrapTimerTick(object? sender, EventArgs e)
    {
        if (_hasTerminalState || _renderReady || _snapshotImage.Source is not null)
        {
            StopBootstrapTimer();
            return;
        }

        Fail(_localization.MermaidLoadTimeout);
    }

    private void ResetBootstrapTimer()
    {
        if (_hasTerminalState || _renderReady || _snapshotImage.Source is not null)
        {
            return;
        }

        _bootstrapTimer.Stop();
        _bootstrapTimer.Start();
    }

    private void StopBootstrapTimer()
    {
        _bootstrapTimer.Stop();
    }

    private void Fail(string text)
    {
        _hasTerminalState = true;
        ShowStatus(text);
        DisposeWebView();
    }

    private void ApplyMeasuredHeight(double height)
    {
        Height = height;
        if (!_isDisposed)
        {
            _webView.Height = height;
        }

        InvalidateMeasure();
        InvalidateArrange();
        UpdateLayout();

        var parent = Parent as FrameworkElement;
        while (parent is not null)
        {
            parent.InvalidateMeasure();
            parent.InvalidateArrange();
            parent = parent.Parent as FrameworkElement;
        }
    }

    private static string BuildPreviewUri(string code)
    {
        var bytes = Encoding.UTF8.GetBytes(code ?? string.Empty);
        var encoded = Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        return $"https://{MermaidAssetsHostName}/{MermaidHostFileName}?code={encoded}";
    }

    private static string BuildHostHtml()
    {
        const string htmlTemplate = """
<!DOCTYPE html>
<html>
<head>
  <meta charset="utf-8" />
  <style>
    html, body {
      margin: 0;
      padding: 0;
      background: transparent;
      overflow: hidden;
    }

    body {
      font-family: "Segoe UI", sans-serif;
      color: #ffffff;
    }

    #root {
      width: 100%;
      padding: 4px 0 0;
      box-sizing: border-box;
      display: flex;
      justify-content: center;
      align-items: flex-start;
    }

    #error {
      display: none;
      margin: 4px 0 0;
      padding: 12px 14px;
      box-sizing: border-box;
      border-radius: 10px;
      background: rgba(255, 255, 255, 0.08);
      border: 1px solid rgba(255, 255, 255, 0.12);
    }

    svg {
      display: block;
      max-width: 100%;
      height: auto;
    }
  </style>
</head>
<body>
  <div id="root"></div>
  <div id="error"></div>
  <script src="./mermaid.min.js"></script>
  <script>
    const root = document.getElementById('root');
    const errorNode = document.getElementById('error');
    let observer = null;
    let mutationObserver = null;
    let lastHeight = 0;
    let isFinished = false;
    let heightSyncHandle = 0;
    let heightSyncUntil = 0;
    const timeoutId = window.setTimeout(() => {
      fail('Timeout ao inicializar o Mermaid oficial.');
    }, __BOOTSTRAP_TIMEOUT_MS__);

    function postMessage(message) {
      window.chrome?.webview?.postMessage(message);
    }

    function decodeCode() {
      const encoded = new URLSearchParams(window.location.search).get('code') || '';
      if (!encoded) {
        return '';
      }

      const normalized = encoded.replace(/-/g, '+').replace(/_/g, '/');
      const padding = normalized.length % 4 === 0
        ? ''
        : '='.repeat(4 - (normalized.length % 4));
      const binary = window.atob(normalized + padding);
      const bytes = Uint8Array.from(binary, ch => ch.charCodeAt(0));
      return new TextDecoder('utf-8').decode(bytes);
    }

    function resolveMermaidApi() {
      const candidates = [
        window.mermaid,
        window.mermaid?.default,
        window.__esbuild_esm_mermaid_nm?.mermaid?.default,
        window.__esbuild_esm_mermaid_nm?.mermaid
      ];

      for (const candidate of candidates) {
        if (candidate && typeof candidate.initialize === 'function' && typeof candidate.render === 'function') {
          return candidate;
        }
      }

      return null;
    }

    function fail(detail) {
      if (isFinished) {
        return;
      }

      isFinished = true;
      window.clearTimeout(timeoutId);
      errorNode.style.display = 'block';
      errorNode.textContent = detail;
      watchHeight();
      scheduleHeightSync(1200);
      postMessage(`error:${detail}`);
    }

    function measureHeight() {
      const svg = root.querySelector('svg');
      const candidates = [
        document.documentElement?.scrollHeight || 0,
        document.body?.scrollHeight || 0,
        document.documentElement?.offsetHeight || 0,
        document.body?.offsetHeight || 0,
        root.scrollHeight || 0,
        Math.ceil(root.getBoundingClientRect().height),
        Math.ceil(errorNode.getBoundingClientRect().height),
        svg ? Math.ceil(svg.getBoundingClientRect().height) : 0
      ];

      return Math.max(120, ...candidates) + 12;
    }

    function postHeight() {
      window.requestAnimationFrame(() => {
        const measuredHeight = measureHeight();
        if (Math.abs(measuredHeight - lastHeight) > 1) {
          lastHeight = measuredHeight;
          postMessage(`height:${measuredHeight}`);
        }
      });
    }

    function scheduleHeightSync(durationMs) {
      heightSyncUntil = Math.max(heightSyncUntil, Date.now() + durationMs);
      if (heightSyncHandle) {
        return;
      }

      const tick = () => {
        postHeight();
        if (Date.now() < heightSyncUntil) {
          heightSyncHandle = window.requestAnimationFrame(tick);
          return;
        }

        heightSyncHandle = 0;
      };

      heightSyncHandle = window.requestAnimationFrame(tick);
    }

    function watchHeight() {
      if (observer) {
        observer.disconnect();
      }
      if (mutationObserver) {
        mutationObserver.disconnect();
      }

      observer = new ResizeObserver(() => postHeight());
      observer.observe(document.body);
      observer.observe(document.documentElement);
      observer.observe(root);
      if (root.firstElementChild) {
        observer.observe(root.firstElementChild);
      }

      mutationObserver = new MutationObserver(() => scheduleHeightSync(1200));
      mutationObserver.observe(root, { childList: true, subtree: true, attributes: true });
    }

    window.addEventListener('error', (event) => {
      const detail = event?.error?.message || event?.message || '__MERMAID_SCRIPT_ERROR__';
      fail(detail);
    });
    window.addEventListener('resize', () => scheduleHeightSync(1200));

    (async () => {
      try {
        const code = decodeCode();
        const mermaid = resolveMermaidApi();
        if (!mermaid) {
          throw new Error('Bundle local do Mermaid não foi carregado.');
        }

        mermaid.initialize({
          startOnLoad: false,
          securityLevel: 'loose',
          theme: 'base',
          themeVariables: {
            background: 'transparent',
            fontFamily: 'Segoe UI',
            primaryColor: '#21252c',
            primaryTextColor: '#ffffff',
            primaryBorderColor: '#d8d8d8',
            lineColor: '#ffffff',
            secondaryColor: '#21252c',
            secondaryTextColor: '#ffffff',
            secondaryBorderColor: '#d8d8d8',
            tertiaryColor: '#21252c',
            tertiaryTextColor: '#ffffff',
            tertiaryBorderColor: '#d8d8d8',
            mainBkg: '#21252c',
            nodeBorder: '#d8d8d8',
            clusterBkg: 'transparent',
            edgeLabelBackground: '#6b7f95'
          },
          flowchart: {
            useMaxWidth: true,
            htmlLabels: true
          }
        });

        const renderResult = await mermaid.render(`mermaid-${Date.now()}`, code);
        root.innerHTML = renderResult.svg;
        renderResult.bindFunctions?.(root);

        const svg = root.querySelector('svg');
        if (svg) {
          svg.removeAttribute('width');
          svg.style.maxWidth = '100%';
          svg.style.height = 'auto';
        }

        if (isFinished) {
          return;
        }

        isFinished = true;
        window.clearTimeout(timeoutId);
        watchHeight();
        scheduleHeightSync(2000);
        postMessage('ready');
      } catch (error) {
        const detail = error?.message || '__MERMAID_RENDER_FAILED__';
        fail(detail);
      }
    })();
  </script>
</body>
</html>
""";

        return htmlTemplate
            .Replace("__BOOTSTRAP_TIMEOUT_MS__", MermaidBootstrapTimeoutMs.ToString(CultureInfo.InvariantCulture))
            .Replace("__MERMAID_SCRIPT_ERROR__", new LocalizationService().MermaidPreviewScriptError)
            .Replace("__MERMAID_RENDER_FAILED__", new LocalizationService().MermaidRenderFailed);
    }

    private static string EnsureLocalAssets()
    {
        var assetsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexVsix",
            "WebView2Assets");
        Directory.CreateDirectory(assetsFolder);

        var mermaidBundlePath = Path.Combine(assetsFolder, MermaidAssetFileName);
        WriteIfDifferent(mermaidBundlePath, MermaidBundle.Value);

        var mermaidHostPath = Path.Combine(assetsFolder, MermaidHostFileName);
        WriteIfDifferent(mermaidHostPath, MermaidHostHtml.Value);

        return assetsFolder;
    }

    private static void WriteIfDifferent(string path, string content)
    {
        if (File.Exists(path))
        {
            var existing = File.ReadAllText(path);
            if (string.Equals(existing, content, StringComparison.Ordinal))
            {
                return;
            }
        }

        File.WriteAllText(path, content, new UTF8Encoding(false));
    }

    private static string LoadMermaidBundle()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(MermaidBundleResourceName);
        if (stream is null)
        {
            var localization = new LocalizationService();
            throw new InvalidOperationException(string.Format(localization.Culture, localization.MermaidBundleNotFoundFormat, MermaidBundleResourceName));
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
