using Microsoft.Win32;
using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace SteamPlaylogViewer;

public partial class MainWindow : Window
{
    private bool _pageReady = false;

    private string _logPath = "";
    private FileSystemWatcher? _watcher;
    private DispatcherTimer? _debounceTimer;
    private DateTime _lastReloadUtc = DateTime.MinValue;

    private DispatcherTimer? _pollTimer;
    private long _lastSize = -1;
    private DateTime _lastWriteUtc = DateTime.MinValue;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (_, __) => await InitAsync();
        Closed += (_, __) => Cleanup();
    }

    private void Cleanup()
    {
        try { _watcher?.Dispose(); } catch { }
        _watcher = null;
        _debounceTimer?.Stop();
        _pollTimer?.Stop();
    }

    private static string LoadEmbeddedHtml()
    {
        var asm = Assembly.GetExecutingAssembly();
        var res = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(".Assets.index.html", StringComparison.OrdinalIgnoreCase));
        if (res == null)
            throw new FileNotFoundException("Embedded HTML not found", ".Assets.index.html");

        using var stream = asm.GetManifestResourceStream(res)!;
        using var sr = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return sr.ReadToEnd();
    }

    private async System.Threading.Tasks.Task InitAsync()
    {
        try
        {
            _logPath = SteamLocator.ResolveLogPath();

            await Web.EnsureCoreWebView2Async();

            Web.CoreWebView2.WebMessageReceived += async (_, e) =>
            {
                try
                {
                    var raw = e.TryGetWebMessageAsString();
                    if (string.IsNullOrWhiteSpace(raw)) return;

                    using var doc = JsonDocument.Parse(raw);
                    var type = doc.RootElement.TryGetProperty("type", out var t) ? t.GetString() : null;

                    if (type == "reset")
                    {
                        await ReloadNowAsync("リセット", fromReset: true);
                        return;
                    }

                    if (type == "exportCsv")
                    {
                        var csv = doc.RootElement.TryGetProperty("csv", out var c) ? c.GetString() ?? "" : "";
                        await ExportCsvAsync(csv);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log(ex);
                }
            };

            Web.CoreWebView2.NavigationCompleted += async (_, e) =>
            {
                _pageReady = e.IsSuccess;
                if (_pageReady)
                {
                    SetupWatchers();
                    await ReloadNowAsync("初回読み込み", fromReset: false);
                }
                else
                {
                    MessageBox.Show("UIの読み込みに失敗しました。", "SteamPlaylogViewer",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };

            Web.NavigateToString(LoadEmbeddedHtml());
        }
        catch (Exception ex)
        {
            Logger.Log(ex);
            MessageBox.Show(
                "起動に失敗しました。WebView2 Runtime が入っていない可能性があります。\n\n" +
                ex.Message + "\n\nLog: " + Logger.LogPath,
                "SteamPlaylogViewer",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async System.Threading.Tasks.Task ExportCsvAsync(string csv)
    {
        var dlg = new SaveFileDialog
        {
            Filter = "CSV (*.csv)|*.csv",
            FileName = "steam_sessions.csv",
            Title = "CSVとして保存"
        };
        if (dlg.ShowDialog(this) != true) return;

        // UTF-8 BOM for Excel
        File.WriteAllText(dlg.FileName, csv, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        await ToastAsync("CSVを保存しました", "exportBtn");
    }

    private void SetupWatchers()
    {
        _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
        _debounceTimer.Tick += async (_, __) =>
        {
            _debounceTimer?.Stop();
            await ReloadNowAsync("自動更新", fromReset: false);
        };

        try
        {
            var dir = Path.GetDirectoryName(_logPath);
            var file = Path.GetFileName(_logPath);
            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir) && !string.IsNullOrWhiteSpace(file))
            {
                _watcher = new FileSystemWatcher(dir, file)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                    EnableRaisingEvents = true,
                    IncludeSubdirectories = false
                };

                void Arm()
                {
                    _debounceTimer?.Stop();
                    _debounceTimer?.Start();
                }

                _watcher.Changed += (_, __) => Arm();
                _watcher.Created += (_, __) => Arm();
                _watcher.Renamed += (_, __) => Arm();
            }
        }
        catch { }

        // light polling fallback
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _pollTimer.Tick += async (_, __) =>
        {
            try
            {
                var fi = new FileInfo(_logPath);
                if (!fi.Exists) return;
                var w = fi.LastWriteTimeUtc;
                var s = fi.Length;
                if (w != _lastWriteUtc || s != _lastSize)
                {
                    _lastWriteUtc = w;
                    _lastSize = s;
                    await ReloadNowAsync("自動更新", fromReset: false);
                }
            }
            catch { }
        };
        _pollTimer.Start();
    }

    private async System.Threading.Tasks.Task ReloadNowAsync(string label, bool fromReset)
    {
        if (!_pageReady) return;

        var now = DateTime.UtcNow;
        if (!fromReset && (now - _lastReloadUtc).TotalMilliseconds < 900) return;
        _lastReloadUtc = now;

        if (!File.Exists(_logPath))
        {
            await ToastAsync($"ログが見つかりません: {_logPath}", "resetBtn");
            return;
        }

        string text;
        try
        {
            using var fs = new FileStream(_logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            text = await sr.ReadToEndAsync();
        }
        catch (IOException)
        {
            await System.Threading.Tasks.Task.Delay(150);
            using var fs = new FileStream(_logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            text = await sr.ReadToEndAsync();
        }

        var argText = JsonSerializer.Serialize(text);
        var argLabel = JsonSerializer.Serialize(label);

        await Web.ExecuteScriptAsync($"loadTextAndParse({argText}, {argLabel});");

        if (fromReset)
        {
            await ToastAsync("Steam gameprocess_logを再読み込みしました", "resetBtn");
        }
    }

    private async System.Threading.Tasks.Task ToastAsync(string message, string targetId)
    {
        var msgJson = JsonSerializer.Serialize(message);
        var idJson = JsonSerializer.Serialize(targetId);

        var js = "(function(){"
                 + "const msg=" + msgJson + ";"
                 + "const id=" + idJson + ";"
                 + "const el=document.getElementById(id)||document.getElementById('exportBtn');"
                 + "try{ if(typeof showTipNear==='function' && el) showTipNear(el,msg); else alert(msg); }catch(_){ try{alert(msg);}catch(__){} }"
                 + "})()";

        try { await Web.ExecuteScriptAsync(js); } catch { }
        await System.Threading.Tasks.Task.CompletedTask;
    }
}
