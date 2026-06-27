using System.Diagnostics;
using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace ZapretManager;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var rootPath = BundledZapretExtractor.PrepareZapretRoot();
        if (rootPath is null)
        {
            MessageBox.Show(
                "Не нашел встроенные файлы zapret и не нашел папку рядом с программой.",
                "Zapret Manager",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        Application.Run(new MainForm(new ZapretPaths(rootPath)));
    }
}

internal sealed class MainForm : Form
{
    private readonly ZapretPaths _paths;
    private readonly StrategyListPanel _strategyList;
    private readonly Label _statusLabel;
    private readonly Label _selectedLabel;
    private readonly Label _detailsLabel;
    private readonly Label _modeLabel;
    private readonly TextBox _logBox;
    private readonly ModernButton _startButton;
    private readonly ModernButton _stopButton;
    private readonly ModernButton _refreshButton;
    private readonly System.Windows.Forms.Timer _statusTimer;

    private readonly List<StrategyItem> _strategies = [];
    private readonly List<ModernButton> _actionButtons = [];
    private Process? _currentProcess;
    private StrategyItem? _selectedStrategy;

    private static readonly Color Bg = Color.FromArgb(12, 16, 24);
    private static readonly Color Panel = Color.FromArgb(20, 27, 40);
    private static readonly Color Panel2 = Color.FromArgb(25, 34, 50);
    private static readonly Color TextMain = Color.FromArgb(242, 246, 255);
    private static readonly Color TextMuted = Color.FromArgb(145, 157, 178);
    private static readonly Color Accent = Color.FromArgb(44, 197, 158);
    private static readonly Color Accent2 = Color.FromArgb(70, 130, 255);
    private static readonly Color Danger = Color.FromArgb(236, 82, 82);

    private sealed record ActionButtonSpec(string Text, Color Color, Func<Task> Action);

    public MainForm(ZapretPaths paths)
    {
        _paths = paths;

        Text = "Zapret Manager";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1080, 840);
        Size = new Size(1220, 900);
        BackColor = Bg;
        ForeColor = TextMain;
        Font = new Font("Segoe UI", 9.5f);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            BackColor = Bg,
            Padding = new Padding(18)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 360));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        var header = new Panel { Dock = DockStyle.Fill, BackColor = Bg };
        root.SetColumnSpan(header, 2);
        root.Controls.Add(header, 0, 0);

        var title = new Label
        {
            AutoSize = true,
            Text = "Zapret Manager",
            Font = new Font("Segoe UI Semibold", 24f),
            ForeColor = TextMain,
            Location = new Point(2, 2)
        };
        header.Controls.Add(title);

        var subtitle = new Label
        {
            AutoSize = true,
            Text = "Запуск стратегий для YouTube и Discord без открытых bat-окон",
            Font = new Font("Segoe UI", 10.5f),
            ForeColor = TextMuted,
            Location = new Point(5, 52)
        };
        header.Controls.Add(subtitle);

        _statusLabel = new Label
        {
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI Semibold", 10f),
            ForeColor = TextMain,
            BackColor = Color.FromArgb(52, 61, 80),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Size = new Size(180, 36),
            Location = new Point(790, 14)
        };
        header.Resize += (_, _) => _statusLabel.Left = header.ClientSize.Width - _statusLabel.Width - 4;
        header.Controls.Add(_statusLabel);

        var leftCard = new RoundedPanel { Dock = DockStyle.Fill, BackColor = Panel, Radius = 10, Padding = new Padding(14) };
        root.Controls.Add(leftCard, 0, 1);

        var leftLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, BackColor = Color.Transparent };
        leftLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        leftLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        leftCard.Controls.Add(leftLayout);

        var strategiesTitle = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Стратегии",
            Font = new Font("Segoe UI Semibold", 14f),
            ForeColor = TextMain,
            TextAlign = ContentAlignment.MiddleLeft
        };
        leftLayout.Controls.Add(strategiesTitle, 0, 0);

        _strategyList = new StrategyListPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Panel
        };
        _strategyList.StrategySelected += (_, strategy) => SelectStrategy(strategy);
        leftLayout.Controls.Add(_strategyList, 0, 1);

        var rightCard = new RoundedPanel { Dock = DockStyle.Fill, BackColor = Panel, Radius = 10, Padding = new Padding(18) };
        root.Controls.Add(rightCard, 1, 1);

        var rightLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 6,
            ColumnCount = 1,
            BackColor = Color.Transparent
        };
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 344));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rightCard.Controls.Add(rightLayout);

        _selectedLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Стратегия не выбрана",
            Font = new Font("Segoe UI Semibold", 20f),
            ForeColor = TextMain,
            TextAlign = ContentAlignment.BottomLeft
        };
        rightLayout.Controls.Add(_selectedLabel, 0, 0);

        _detailsLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "",
            Font = new Font("Segoe UI", 10.2f),
            ForeColor = TextMuted,
            TextAlign = ContentAlignment.TopLeft
        };
        rightLayout.Controls.Add(_detailsLabel, 0, 1);

        var buttonRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            BackColor = Color.Transparent
        };
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24));
        rightLayout.Controls.Add(buttonRow, 0, 2);

        _startButton = new ModernButton
        {
            Dock = DockStyle.Fill,
            Text = "Запустить",
            BaseColor = Accent,
            HoverColor = Color.FromArgb(55, 218, 177),
            PressColor = Color.FromArgb(32, 170, 134),
            ForeColor = Color.FromArgb(4, 18, 16),
            Font = new Font("Segoe UI Semibold", 13f),
            Margin = new Padding(0, 6, 10, 6)
        };
        _startButton.Click += async (_, _) => await StartSelectedStrategyAsync();
        buttonRow.Controls.Add(_startButton, 0, 0);

        _stopButton = new ModernButton
        {
            Dock = DockStyle.Fill,
            Text = "Остановить",
            BaseColor = Danger,
            HoverColor = Color.FromArgb(246, 101, 101),
            PressColor = Color.FromArgb(196, 61, 61),
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 11f),
            Margin = new Padding(0, 6, 10, 6)
        };
        _stopButton.Click += async (_, _) => await StopZapretAsync(showResult: true);
        buttonRow.Controls.Add(_stopButton, 1, 0);

        _refreshButton = new ModernButton
        {
            Dock = DockStyle.Fill,
            Text = "Обновить",
            BaseColor = Color.FromArgb(45, 56, 78),
            HoverColor = Color.FromArgb(58, 71, 98),
            PressColor = Color.FromArgb(34, 43, 62),
            ForeColor = TextMain,
            Font = new Font("Segoe UI Semibold", 10f),
            Margin = new Padding(0, 6, 0, 6)
        };
        _refreshButton.Click += (_, _) => LoadStrategies();
        buttonRow.Controls.Add(_refreshButton, 2, 0);

        _modeLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "",
            ForeColor = TextMuted,
            Font = new Font("Segoe UI", 10f),
            TextAlign = ContentAlignment.MiddleLeft
        };
        rightLayout.Controls.Add(_modeLabel, 0, 3);

        var serviceTools = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 10,
            ColumnCount = 1,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 4)
        };
        for (var i = 0; i < 5; i++)
        {
            serviceTools.RowStyles.Add(new RowStyle(SizeType.Absolute, 23));
            serviceTools.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
        }

        AddActionSection(serviceTools, 0, "Служба",
            new ActionButtonSpec("Установить", Accent2, InstallSelectedServiceAsync),
            new ActionButtonSpec("Удалить", Danger, RemoveServicesAsync),
            new ActionButtonSpec("Статус", Color.FromArgb(45, 56, 78), CheckStatusAsync));

        AddActionSection(serviceTools, 2, "Game Filter",
            new ActionButtonSpec("Выкл", Color.FromArgb(45, 56, 78), () => SetGameFilterModeAsync(null)),
            new ActionButtonSpec("TCP+UDP", Color.FromArgb(45, 56, 78), () => SetGameFilterModeAsync("all")),
            new ActionButtonSpec("TCP", Color.FromArgb(45, 56, 78), () => SetGameFilterModeAsync("tcp")),
            new ActionButtonSpec("UDP", Color.FromArgb(45, 56, 78), () => SetGameFilterModeAsync("udp")));

        AddActionSection(serviceTools, 4, "IPSet / обновления",
            new ActionButtonSpec("IPSet none", Color.FromArgb(45, 56, 78), () => SetIpSetModeAsync("none")),
            new ActionButtonSpec("IPSet loaded", Color.FromArgb(45, 56, 78), () => SetIpSetModeAsync("loaded")),
            new ActionButtonSpec("IPSet any", Color.FromArgb(45, 56, 78), () => SetIpSetModeAsync("any")),
            new ActionButtonSpec("Авто-update", Color.FromArgb(45, 56, 78), ToggleAutoUpdatesAsync));

        AddActionSection(serviceTools, 6, "Обновления",
            new ActionButtonSpec("Обновить IPSet", Color.FromArgb(45, 56, 78), UpdateIpSetAsync),
            new ActionButtonSpec("Обновить hosts", Color.FromArgb(45, 56, 78), UpdateHostsAsync),
            new ActionButtonSpec("Проверить версию", Color.FromArgb(45, 56, 78), CheckUpdatesAsync));

        AddActionSection(serviceTools, 8, "Инструменты",
            new ActionButtonSpec("Диагностика", Color.FromArgb(45, 56, 78), RunDiagnosticsAsync),
            new ActionButtonSpec("Discord тест", Accent, CheckDiscordAsync),
            new ActionButtonSpec("Кэш Discord", Color.FromArgb(45, 56, 78), ClearDiscordCacheAsync),
            new ActionButtonSpec("Убрать конфликты", Danger, RemoveConflictsAsync));

        rightLayout.Controls.Add(serviceTools, 0, 4);

        _logBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.None,
            BackColor = Panel2,
            ForeColor = Color.FromArgb(215, 224, 240),
            Font = new Font("Consolas", 9.5f),
            Margin = new Padding(0, 6, 0, 0)
        };
        rightLayout.Controls.Add(_logBox, 0, 5);

        _statusTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _statusTimer.Tick += (_, _) => RefreshRuntimeStatus();

        Load += (_, _) =>
        {
            LoadStrategies();
            RefreshRuntimeStatus();
            _statusTimer.Start();
            AppendLog("Готово. Выбери стратегию и нажми Запустить.");
            if (!IsAdministrator())
            {
                AppendLog("Важно: программа запущена не от администратора. WinDivert может не стартовать.");
            }
        };

        FormClosing += MainForm_FormClosing;
    }

    private void AddActionSection(TableLayoutPanel parent, int row, string title, params ActionButtonSpec[] specs)
    {
        var label = new Label
        {
            Dock = DockStyle.Fill,
            Text = title,
            Font = new Font("Segoe UI Semibold", 9.4f),
            ForeColor = TextMuted,
            TextAlign = ContentAlignment.BottomLeft,
            Margin = new Padding(2, 0, 0, 0)
        };
        parent.Controls.Add(label, 0, row);

        var actionRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = specs.Length,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 2, 0, 4)
        };

        for (var i = 0; i < specs.Length; i++)
        {
            actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / specs.Length));
            var spec = specs[i];
            var button = new ModernButton
            {
                Dock = DockStyle.Fill,
                Text = spec.Text,
                BaseColor = spec.Color,
                HoverColor = ControlPaint.Light(spec.Color, 0.18f),
                PressColor = ControlPaint.Dark(spec.Color, 0.12f),
                ForeColor = spec.Color == Accent ? Color.FromArgb(4, 18, 16) : TextMain,
                Font = new Font("Segoe UI Semibold", 8.9f),
                Margin = new Padding(i == 0 ? 0 : 5, 0, 0, 0)
            };
            button.Click += async (_, _) => await RunUiActionAsync(spec.Text, spec.Action);
            _actionButtons.Add(button);
            actionRow.Controls.Add(button, i, 0);
        }

        parent.Controls.Add(actionRow, 0, row + 1);
    }

    private async Task RunUiActionAsync(string title, Func<Task> action)
    {
        SetControlsBusy(true);
        try
        {
            AppendLog($"Действие: {title}");
            await action();
        }
        catch (Exception ex)
        {
            AppendLog("Ошибка: " + ex.Message);
            MessageBox.Show(ex.Message, "Zapret Manager", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetControlsBusy(false);
            RefreshRuntimeStatus();
            UpdateModeLabel();
        }
    }

    private void LoadStrategies()
    {
        _strategies.Clear();

        var files = Directory.GetFiles(_paths.RootPath, "general*.bat", SearchOption.TopDirectoryOnly)
            .OrderBy(StrategySortKey)
            .ToArray();

        foreach (var file in files)
        {
            var strategy = StrategyItem.FromFile(file);
            _strategies.Add(strategy);
        }

        _strategyList.SetItems(_strategies);

        var previous = _selectedStrategy?.FilePath;
        var selected = previous is not null
            ? _strategies.FirstOrDefault(x => string.Equals(x.FilePath, previous, StringComparison.OrdinalIgnoreCase))
            : null;

        SelectStrategy(selected ?? _strategies.FirstOrDefault());
        AppendLog($"Найдено стратегий: {_strategies.Count}.");
    }

    private void SelectStrategy(StrategyItem? strategy)
    {
        _selectedStrategy = strategy;

        _strategyList.SelectedStrategy = strategy;

        if (strategy is null)
        {
            _selectedLabel.Text = "Стратегии не найдены";
            _detailsLabel.Text = "В папке должны лежать файлы general*.bat.";
            _startButton.Enabled = false;
            return;
        }

        _selectedLabel.Text = strategy.DisplayName;
        _detailsLabel.Text = $"Файл: {strategy.FileName}\r\n{strategy.Description}";
        _startButton.Enabled = true;
        UpdateModeLabel();
    }

    private async Task StartSelectedStrategyAsync()
    {
        if (_selectedStrategy is null)
        {
            MessageBox.Show("Сначала выбери стратегию слева.", "Zapret Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!File.Exists(_paths.WinwsPath))
        {
            MessageBox.Show("Не найден bin\\winws.exe.", "Zapret Manager", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        SetControlsBusy(true);

        try
        {
            await StopZapretAsync(showResult: false);
            ZapretSetup.EnsureUserLists(_paths);
            UpdateModeLabel();
            await ZapretSetup.EnableTcpTimestampsAsync(AppendLog);

            var filter = GameFilterSettings.Load(_paths);
            var args = BatchStrategyParser.ParseArguments(_selectedStrategy.FilePath, _paths, filter);
            if (args.Count == 0)
            {
                throw new InvalidOperationException("Не смог прочитать параметры запуска из bat-файла.");
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = _paths.WinwsPath,
                WorkingDirectory = _paths.BinPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            foreach (var arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }

            _currentProcess = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            _currentProcess.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) AppendLog(e.Data); };
            _currentProcess.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) AppendLog(e.Data); };
            _currentProcess.Exited += (_, _) =>
            {
                var code = -1;
                try { code = _currentProcess?.ExitCode ?? -1; } catch { }
                BeginInvoke(() =>
                {
                    AppendLog($"winws.exe завершился. Код выхода: {code}.");
                    RefreshRuntimeStatus();
                });
            };

            if (!_currentProcess.Start())
            {
                throw new InvalidOperationException("Windows не запустила winws.exe.");
            }

            _currentProcess.BeginOutputReadLine();
            _currentProcess.BeginErrorReadLine();
            AppendLog($"Запущено: {_selectedStrategy.FileName}");
        }
        catch (Exception ex)
        {
            AppendLog("Ошибка запуска: " + ex.Message);
            MessageBox.Show(ex.Message, "Ошибка запуска", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetControlsBusy(false);
            RefreshRuntimeStatus();
        }
    }

    private async Task StopZapretAsync(bool showResult)
    {
        await CommandRunner.RunAsync("net.exe", ["stop", "zapret"], 15000);
        var stopped = await Task.Run(() => ZapretProcess.StopAll(_paths, AppendLog));
        if (showResult)
        {
            AppendLog(stopped ? "Zapret остановлен." : "Запущенный winws.exe не найден.");
        }
        _currentProcess = null;
        RefreshRuntimeStatus();
    }

    private async Task InstallSelectedServiceAsync()
    {
        if (_selectedStrategy is null)
        {
            MessageBox.Show("Сначала выбери стратегию слева.", "Zapret Manager", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ZapretSetup.EnsureUserLists(_paths);
        await ZapretSetup.EnableTcpTimestampsAsync(AppendLog);
        var filter = GameFilterSettings.Load(_paths);
        var args = BatchStrategyParser.ParseArguments(_selectedStrategy.FilePath, _paths, filter);
        await ZapretServiceManager.InstallServiceAsync(_paths, _selectedStrategy, args, AppendLog);
    }

    private async Task RemoveServicesAsync()
    {
        await ZapretServiceManager.RemoveServicesAsync(_paths, AppendLog);
        _currentProcess = null;
    }

    private async Task CheckStatusAsync()
    {
        var status = await ZapretServiceManager.GetStatusAsync(_paths);
        foreach (var line in status)
        {
            AppendLog(line);
        }
    }

    private Task SetGameFilterModeAsync(string? mode)
    {
        ZapretSettings.SetGameFilterMode(_paths, mode);
        AppendLog("Game Filter: " + GameFilterSettings.Load(_paths).Status);
        if (ZapretProcess.IsRunning(_paths))
        {
            AppendLog("Чтобы режим применился, нажми Перезапустить.");
        }

        return Task.CompletedTask;
    }

    private Task SetIpSetModeAsync(string mode)
    {
        var result = ZapretSettings.SetIpSetMode(_paths, mode);
        AppendLog(result);
        if (ZapretProcess.IsRunning(_paths))
        {
            AppendLog("Чтобы режим применился, нажми Перезапустить.");
        }

        return Task.CompletedTask;
    }

    private Task ToggleAutoUpdatesAsync()
    {
        var enabled = ZapretSettings.ToggleAutoUpdates(_paths);
        AppendLog("Авто-проверка обновлений: " + (enabled ? "включена" : "выключена"));
        return Task.CompletedTask;
    }

    private async Task UpdateIpSetAsync()
    {
        await ZapretUpdates.UpdateIpSetAsync(_paths, AppendLog);
    }

    private async Task UpdateHostsAsync()
    {
        await ZapretUpdates.UpdateHostsAsync(AppendLog);
    }

    private async Task CheckUpdatesAsync()
    {
        await ZapretUpdates.CheckAppUpdatesAsync(AppendLog);
    }

    private async Task RunDiagnosticsAsync()
    {
        await ZapretDiagnostics.RunAsync(_paths, AppendLog);
    }

    private async Task CheckDiscordAsync()
    {
        await DiscordChecker.CheckAsync(AppendLog);
    }

    private async Task ClearDiscordCacheAsync()
    {
        await ZapretDiagnostics.ClearDiscordCacheAsync(AppendLog);
    }

    private async Task RemoveConflictsAsync()
    {
        await ZapretDiagnostics.RemoveConflictingServicesAsync(AppendLog);
    }

    private void RefreshRuntimeStatus()
    {
        var running = ZapretProcess.IsRunning(_paths);
        _statusLabel.Text = running ? "ZAPRET ВКЛЮЧЕН" : "ZAPRET ВЫКЛЮЧЕН";
        _statusLabel.BackColor = running ? Color.FromArgb(25, 122, 92) : Color.FromArgb(52, 61, 80);
        _stopButton.Enabled = running;
        _startButton.Text = running ? "Перезапустить" : "Запустить";
    }

    private void UpdateModeLabel()
    {
        var filter = GameFilterSettings.Load(_paths);
        var ipset = ZapretSettings.GetIpSetStatus(_paths);
        var autoUpdates = ZapretSettings.IsAutoUpdatesEnabled(_paths) ? "вкл" : "выкл";
        _modeLabel.Text = $"Game: {filter.Status} | IPSet: {ipset} | Auto-update: {autoUpdates} | {_paths.RootPath}";
    }

    private void SetControlsBusy(bool busy)
    {
        _startButton.Enabled = !busy && _selectedStrategy is not null;
        _stopButton.Enabled = !busy;
        _refreshButton.Enabled = !busy;
        foreach (var button in _actionButtons)
        {
            button.Enabled = !busy;
        }
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
    }

    private void AppendLog(string text)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => AppendLog(text));
            return;
        }

        var line = $"[{DateTime.Now:HH:mm:ss}] {text}\r\n";
        _logBox.AppendText(line);
        if (_logBox.TextLength > 60000)
        {
            _logBox.Text = _logBox.Text[^45000..];
            _logBox.SelectionStart = _logBox.TextLength;
        }
    }

    private async void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!ZapretProcess.IsRunning(_paths))
        {
            return;
        }

        var result = MessageBox.Show(
            "Zapret сейчас работает. Остановить его перед закрытием программы?",
            "Закрытие",
            MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Question);

        if (result == DialogResult.Cancel)
        {
            e.Cancel = true;
            return;
        }

        if (result == DialogResult.Yes)
        {
            e.Cancel = true;
            await StopZapretAsync(showResult: false);
            FormClosing -= MainForm_FormClosing;
            Close();
        }
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static string StrategySortKey(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        if (string.Equals(name, "general", StringComparison.OrdinalIgnoreCase)) return "000";

        var upper = name.ToUpperInvariant();
        if (upper.StartsWith("GENERAL (ALT", StringComparison.Ordinal)
            && !upper.Contains("FAKE", StringComparison.Ordinal)
            && !upper.Contains("SIMPLE", StringComparison.Ordinal))
        {
            var digits = new string(upper["GENERAL (ALT".Length..].TakeWhile(char.IsDigit).ToArray());
            var number = string.IsNullOrEmpty(digits) ? 1 : int.Parse(digits);
            return $"100-{number:D2}-{upper}";
        }

        if (upper.Contains("SIMPLE", StringComparison.Ordinal)) return "200-" + upper;
        if (upper.Contains("FAKE", StringComparison.Ordinal)) return "300-" + upper;
        return "900-" + upper;
    }
}

internal sealed record ZapretPaths(string RootPath)
{
    public string BinPath => Path.Combine(RootPath, "bin");
    public string ListsPath => Path.Combine(RootPath, "lists");
    public string UtilsPath => Path.Combine(RootPath, "utils");
    public string WinwsPath => Path.Combine(BinPath, "winws.exe");

    public static string? FindRoot(string startPath)
    {
        var directory = new DirectoryInfo(startPath);
        while (directory is not null)
        {
            var hasZapret = File.Exists(Path.Combine(directory.FullName, "service.bat"))
                && File.Exists(Path.Combine(directory.FullName, "general.bat"))
                && File.Exists(Path.Combine(directory.FullName, "bin", "winws.exe"));

            if (hasZapret)
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    public string RootWithSlash => EnsureTrailingSlash(RootPath);
    public string BinWithSlash => EnsureTrailingSlash(BinPath);
    public string ListsWithSlash => EnsureTrailingSlash(ListsPath);

    private static string EnsureTrailingSlash(string path) => path.EndsWith(Path.DirectorySeparatorChar)
        ? path
        : path + Path.DirectorySeparatorChar;
}

internal static class BundledZapretExtractor
{
    private const string ResourcePrefix = "zapret/";
    private const string MarkerFileName = ".zapret-manager-extracted";

    public static string? PrepareZapretRoot()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resources = assembly.GetManifestResourceNames()
            .Where(x => x.StartsWith(ResourcePrefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (resources.Length == 0)
        {
            return ZapretPaths.FindRoot(AppContext.BaseDirectory);
        }

        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZapretManager",
            "zapret-runtime");

        Directory.CreateDirectory(root);
        var initialized = File.Exists(Path.Combine(root, MarkerFileName));

        foreach (var resource in resources)
        {
            var relative = resource[ResourcePrefix.Length..].Replace('/', Path.DirectorySeparatorChar);
            var target = Path.GetFullPath(Path.Combine(root, relative));
            var normalizedRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;

            if (!target.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Небезопасный путь во встроенном ресурсе: " + relative);
            }

            var normalizedRelative = relative.Replace(Path.DirectorySeparatorChar, '/');
            if (initialized && IsUserMutableResource(normalizedRelative))
            {
                continue;
            }

            using var stream = assembly.GetManifestResourceStream(resource);
            if (stream is null)
            {
                continue;
            }

            if (File.Exists(target) && !ShouldOverwrite(target, stream))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            stream.Position = 0;
            using var output = File.Create(target);
            stream.CopyTo(output);
        }

        File.WriteAllText(Path.Combine(root, MarkerFileName), DateTimeOffset.UtcNow.ToString("O"), Encoding.UTF8);
        return root;
    }

    private static bool ShouldOverwrite(string target, Stream resourceStream)
    {
        var file = new FileInfo(target);
        return file.Length != resourceStream.Length;
    }

    private static bool IsUserMutableResource(string relative)
    {
        return relative.Equals("lists/list-general-user.txt", StringComparison.OrdinalIgnoreCase)
            || relative.Equals("lists/list-exclude-user.txt", StringComparison.OrdinalIgnoreCase)
            || relative.Equals("lists/ipset-exclude-user.txt", StringComparison.OrdinalIgnoreCase)
            || relative.Equals("lists/ipset-all.txt", StringComparison.OrdinalIgnoreCase)
            || relative.Equals("lists/ipset-all.txt.backup", StringComparison.OrdinalIgnoreCase)
            || relative.Equals("utils/game_filter.enabled", StringComparison.OrdinalIgnoreCase)
            || relative.Equals("utils/check_updates.enabled", StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed record StrategyItem(string FilePath, string FileName, string DisplayName, string Description)
{
    public static StrategyItem FromFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        var name = Path.GetFileNameWithoutExtension(filePath);
        var display = name;

        if (name.StartsWith("general", StringComparison.OrdinalIgnoreCase))
        {
            display = name[7..].Trim();
            display = string.IsNullOrWhiteSpace(display) ? "GENERAL" : display.Trim(' ', '(', ')').ToUpperInvariant();
        }

        var upper = display.ToUpperInvariant();
        var description = upper switch
        {
            var x when x.Contains("FAKE TLS AUTO", StringComparison.Ordinal) => "Авто-стратегия с Fake TLS. Часто помогает, если обычные ALT не держатся.",
            var x when x.Contains("SIMPLE FAKE", StringComparison.Ordinal) => "Более простая fake-стратегия. Хороший вариант для быстрой проверки.",
            var x when x.Contains("ALT", StringComparison.Ordinal) => "Альтернативная стратегия. Пробуй разные ALT, если YouTube или Discord не открываются.",
            _ => "Базовая стратегия из этой сборки zapret."
        };

        return new StrategyItem(filePath, fileName, display, description);
    }
}

internal sealed record GameFilterSettings(string Status, string GameFilter, string Tcp, string Udp)
{
    public static GameFilterSettings Load(ZapretPaths paths)
    {
        var flag = Path.Combine(paths.UtilsPath, "game_filter.enabled");
        if (!File.Exists(flag))
        {
            return new GameFilterSettings("выключен", "12", "12", "12");
        }

        var mode = File.ReadLines(flag).FirstOrDefault()?.Trim().ToLowerInvariant() ?? "";
        return mode switch
        {
            "all" => new GameFilterSettings("включен TCP и UDP", "1024-65535", "1024-65535", "1024-65535"),
            "tcp" => new GameFilterSettings("включен TCP", "1024-65535", "1024-65535", "12"),
            _ => new GameFilterSettings("включен UDP", "1024-65535", "12", "1024-65535")
        };
    }
}

internal static class ZapretSettings
{
    private const string EmptyIpSetMarker = "203.0.113.113/32";

    public static void SetGameFilterMode(ZapretPaths paths, string? mode)
    {
        Directory.CreateDirectory(paths.UtilsPath);
        var flag = Path.Combine(paths.UtilsPath, "game_filter.enabled");

        if (mode is null)
        {
            if (File.Exists(flag))
            {
                File.Delete(flag);
            }

            return;
        }

        File.WriteAllText(flag, mode + Environment.NewLine, Encoding.ASCII);
    }

    public static string GetIpSetStatus(ZapretPaths paths)
    {
        var listFile = Path.Combine(paths.ListsPath, "ipset-all.txt");
        if (!File.Exists(listFile))
        {
            return "none";
        }

        var lines = File.ReadAllLines(listFile)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .ToArray();

        if (lines.Length == 0)
        {
            return "any";
        }

        return lines.Any(x => string.Equals(x, EmptyIpSetMarker, StringComparison.OrdinalIgnoreCase))
            ? "none"
            : "loaded";
    }

    public static string SetIpSetMode(ZapretPaths paths, string mode)
    {
        Directory.CreateDirectory(paths.ListsPath);
        var listFile = Path.Combine(paths.ListsPath, "ipset-all.txt");
        var backupFile = listFile + ".backup";
        var current = GetIpSetStatus(paths);

        if (mode == "loaded")
        {
            if (current == "loaded")
            {
                return "IPSet уже в режиме loaded.";
            }

            if (!File.Exists(backupFile))
            {
                return "Нет backup для IPSet. Нажми Обновить IPSet, потом выбери loaded.";
            }

            if (File.Exists(listFile))
            {
                File.Delete(listFile);
            }

            File.Move(backupFile, listFile);
            return "IPSet переключен в loaded.";
        }

        if ((mode == "none" || mode == "any") && current == "loaded" && File.Exists(listFile))
        {
            if (File.Exists(backupFile))
            {
                File.Delete(backupFile);
            }

            File.Move(listFile, backupFile);
        }

        if (mode == "none")
        {
            File.WriteAllText(listFile, EmptyIpSetMarker + Environment.NewLine, Encoding.ASCII);
            return "IPSet переключен в none.";
        }

        if (mode == "any")
        {
            File.WriteAllText(listFile, string.Empty, Encoding.ASCII);
            return "IPSet переключен в any.";
        }

        return "Неизвестный режим IPSet.";
    }

    public static bool IsAutoUpdatesEnabled(ZapretPaths paths)
    {
        return File.Exists(Path.Combine(paths.UtilsPath, "check_updates.enabled"));
    }

    public static bool ToggleAutoUpdates(ZapretPaths paths)
    {
        Directory.CreateDirectory(paths.UtilsPath);
        var flag = Path.Combine(paths.UtilsPath, "check_updates.enabled");
        if (File.Exists(flag))
        {
            File.Delete(flag);
            return false;
        }

        File.WriteAllText(flag, "ENABLED" + Environment.NewLine, Encoding.ASCII);
        return true;
    }
}

internal static class ZapretSetup
{
    public static void EnsureUserLists(ZapretPaths paths)
    {
        Directory.CreateDirectory(paths.ListsPath);
        Directory.CreateDirectory(paths.UtilsPath);

        WriteIfMissing(Path.Combine(paths.ListsPath, "ipset-exclude-user.txt"), "203.0.113.113/32\r\n");
        WriteIfMissing(Path.Combine(paths.ListsPath, "list-general-user.txt"), "# Never leave this file empty\r\ndomain.example.abc\r\n");
        WriteIfMissing(Path.Combine(paths.ListsPath, "list-exclude-user.txt"), "domain.example.abc\r\n");
    }

    public static async Task EnableTcpTimestampsAsync(Action<string> log)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "netsh",
                ArgumentList = { "interface", "tcp", "set", "global", "timestamps=enabled" },
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            if (process is null)
            {
                log("Не удалось запустить netsh для TCP timestamps.");
                return;
            }

            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
            {
                log("netsh не включил TCP timestamps. Если запуск без администратора, zapret может не работать.");
            }
        }
        catch (Exception ex)
        {
            log("netsh ошибка: " + ex.Message);
        }
    }

    private static void WriteIfMissing(string path, string content)
    {
        if (!File.Exists(path))
        {
            File.WriteAllText(path, content, Encoding.UTF8);
        }
    }
}

internal sealed record CommandResult(int ExitCode, string Output, string Error)
{
    public bool Success => ExitCode == 0;
    public string CombinedOutput => string.Join(Environment.NewLine, new[] { Output, Error }.Where(x => !string.IsNullOrWhiteSpace(x)));
}

internal static class CommandRunner
{
    public static async Task<CommandResult> RunAsync(string fileName, IEnumerable<string> args, int timeoutMs = 20000)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };

        foreach (var arg in args)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        var output = new StringBuilder();
        var error = new StringBuilder();

        process.Start();
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best effort cleanup.
            }

            return new CommandResult(-1, string.Empty, $"Команда {fileName} не ответила за {timeoutMs / 1000} сек.");
        }

        output.Append(await outputTask);
        error.Append(await errorTask);
        return new CommandResult(process.ExitCode, output.ToString().Trim(), error.ToString().Trim());
    }
}

internal static class ZapretServiceManager
{
    private const string ServiceName = "zapret";

    public static async Task InstallServiceAsync(ZapretPaths paths, StrategyItem strategy, IReadOnlyList<string> args, Action<string> log)
    {
        if (args.Count == 0)
        {
            throw new InvalidOperationException("Не удалось прочитать параметры стратегии.");
        }

        log("Ставлю zapret как службу Windows...");
        await CommandRunner.RunAsync("net.exe", ["stop", ServiceName], 15000);
        await CommandRunner.RunAsync("sc.exe", ["delete", ServiceName], 15000);
        ZapretProcess.StopAll(paths, log);

        var binPath = $"\"{paths.WinwsPath}\" {string.Join(" ", args.Select(QuoteForServiceBinPath))}";
        var create = await CommandRunner.RunAsync("sc.exe",
        [
            "create", ServiceName,
            "binPath=", binPath,
            "DisplayName=", "zapret",
            "start=", "auto"
        ], 20000);

        if (!create.Success)
        {
            throw new InvalidOperationException("Не удалось создать службу zapret: " + create.CombinedOutput);
        }

        await CommandRunner.RunAsync("sc.exe", ["description", ServiceName, "Zapret DPI bypass software"], 10000);

        using (var key = Registry.LocalMachine.CreateSubKey(@"System\CurrentControlSet\Services\zapret"))
        {
            key?.SetValue("zapret-discord-youtube", Path.GetFileNameWithoutExtension(strategy.FileName), RegistryValueKind.String);
        }

        var start = await CommandRunner.RunAsync("sc.exe", ["start", ServiceName], 20000);
        if (!start.Success)
        {
            log("Служба создана, но старт вернул предупреждение: " + start.CombinedOutput);
        }
        else
        {
            log($"Служба zapret установлена: {strategy.FileName}");
        }
    }

    public static async Task RemoveServicesAsync(ZapretPaths paths, Action<string> log)
    {
        log("Удаляю службу zapret и WinDivert...");
        await CommandRunner.RunAsync("net.exe", ["stop", ServiceName], 15000);
        await CommandRunner.RunAsync("sc.exe", ["delete", ServiceName], 15000);

        ZapretProcess.StopAll(paths, log);

        foreach (var service in new[] { "WinDivert", "WinDivert14" })
        {
            await CommandRunner.RunAsync("net.exe", ["stop", service], 15000);
            await CommandRunner.RunAsync("sc.exe", ["delete", service], 15000);
        }

        log("Удаление служб завершено.");
    }

    public static async Task<IReadOnlyList<string>> GetStatusAsync(ZapretPaths paths)
    {
        var result = new List<string>();
        result.Add("Стратегия службы: " + (GetInstalledStrategyName() ?? "не установлена"));
        result.Add("Служба zapret: " + await QueryServiceStateAsync(ServiceName));
        result.Add("Служба WinDivert: " + await QueryServiceStateAsync("WinDivert"));
        result.Add("Файл WinDivert64.sys: " + (Directory.GetFiles(paths.BinPath, "*.sys").Length > 0 ? "найден" : "не найден"));
        result.Add("winws.exe: " + (ZapretProcess.IsRunning(paths) ? "запущен" : "не запущен"));
        result.Add("Game Filter: " + GameFilterSettings.Load(paths).Status);
        result.Add("IPSet: " + ZapretSettings.GetIpSetStatus(paths));
        result.Add("Auto-update: " + (ZapretSettings.IsAutoUpdatesEnabled(paths) ? "включен" : "выключен"));
        return result;
    }

    public static string? GetInstalledStrategyName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"System\CurrentControlSet\Services\zapret");
            return key?.GetValue("zapret-discord-youtube") as string;
        }
        catch
        {
            return null;
        }
    }

    public static async Task<string> QueryServiceStateAsync(string serviceName)
    {
        var result = await CommandRunner.RunAsync("sc.exe", ["query", serviceName], 10000);
        if (!result.Success)
        {
            return "не установлена";
        }

        var text = result.CombinedOutput;
        if (text.Contains("RUNNING", StringComparison.OrdinalIgnoreCase)) return "работает";
        if (text.Contains("STOP_PENDING", StringComparison.OrdinalIgnoreCase)) return "останавливается";
        if (text.Contains("STOPPED", StringComparison.OrdinalIgnoreCase)) return "остановлена";
        return "найдена";
    }

    private static string QuoteForServiceBinPath(string arg)
    {
        if (arg.Length == 0)
        {
            return "\"\"";
        }

        return arg.Any(char.IsWhiteSpace) || arg.Contains('"')
            ? "\"" + arg.Replace("\"", "\\\"") + "\""
            : arg;
    }
}

internal static class ZapretUpdates
{
    private const string LocalVersion = "1.9.9c";
    private const string VersionUrl = "https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube/main/.service/version.txt";
    private const string DownloadUrl = "https://github.com/Flowseal/zapret-discord-youtube/releases/latest";
    private const string ReleaseUrlPrefix = "https://github.com/Flowseal/zapret-discord-youtube/releases/tag/";
    private const string IpSetUrl = "https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube/refs/heads/main/.service/ipset-service.txt";
    private const string HostsUrl = "https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube/refs/heads/main/.service/hosts";
    private const string HostsBegin = "# Zapret Manager hosts begin";
    private const string HostsEnd = "# Zapret Manager hosts end";

    public static async Task UpdateIpSetAsync(ZapretPaths paths, Action<string> log)
    {
        Directory.CreateDirectory(paths.ListsPath);
        var listFile = Path.Combine(paths.ListsPath, "ipset-all.txt");
        log("Загружаю IPSet...");
        var content = await DownloadStringAsync(IpSetUrl, TimeSpan.FromSeconds(12));
        await File.WriteAllTextAsync(listFile, NormalizeLineEndings(content), new UTF8Encoding(false));
        log("IPSet обновлен: " + listFile);
    }

    public static async Task UpdateHostsAsync(Action<string> log)
    {
        var hostsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "drivers", "etc", "hosts");
        var content = NormalizeLineEndings(await DownloadStringAsync(HostsUrl, TimeSpan.FromSeconds(12))).Trim();
        if (content.Length == 0)
        {
            throw new InvalidOperationException("Скачанный hosts пустой.");
        }

        var repoLines = content.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        var firstLine = repoLines.FirstOrDefault() ?? "";
        var lastLine = repoLines.LastOrDefault() ?? "";
        var existing = File.Exists(hostsFile) ? await File.ReadAllTextAsync(hostsFile, Encoding.UTF8) : "";

        if (existing.Contains(firstLine, StringComparison.OrdinalIgnoreCase)
            && existing.Contains(lastLine, StringComparison.OrdinalIgnoreCase))
        {
            log("hosts уже актуален.");
            return;
        }

        var backup = hostsFile + ".zapret-manager.bak";
        if (!File.Exists(backup) && File.Exists(hostsFile))
        {
            File.Copy(hostsFile, backup, overwrite: false);
            log("Backup hosts создан: " + backup);
        }

        existing = RemoveMarkedBlock(existing, HostsBegin, HostsEnd).TrimEnd();
        var updated = existing
            + Environment.NewLine
            + Environment.NewLine
            + HostsBegin
            + Environment.NewLine
            + content
            + Environment.NewLine
            + HostsEnd
            + Environment.NewLine;

        await File.WriteAllTextAsync(hostsFile, updated, new UTF8Encoding(false));
        log("hosts обновлен автоматически.");
    }

    public static async Task CheckAppUpdatesAsync(Action<string> log)
    {
        log("Проверяю новую версию zapret...");
        var latest = (await DownloadStringAsync(VersionUrl, TimeSpan.FromSeconds(8))).Trim();
        if (string.Equals(LocalVersion, latest, StringComparison.OrdinalIgnoreCase))
        {
            log("Установлена актуальная версия: " + LocalVersion);
            return;
        }

        log($"Доступна новая версия: {latest}. Текущая: {LocalVersion}");
        log("Страница релиза: " + ReleaseUrlPrefix + latest);
        Process.Start(new ProcessStartInfo(DownloadUrl) { UseShellExecute = true });
    }

    private static async Task<string> DownloadStringAsync(string url, TimeSpan timeout)
    {
        using var client = new HttpClient { Timeout = timeout };
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    private static string NormalizeLineEndings(string text) => text.Replace("\r\n", "\n").Replace("\n", "\r\n");

    private static string RemoveMarkedBlock(string text, string begin, string end)
    {
        var beginIndex = text.IndexOf(begin, StringComparison.OrdinalIgnoreCase);
        var endIndex = text.IndexOf(end, StringComparison.OrdinalIgnoreCase);
        if (beginIndex < 0 || endIndex < beginIndex)
        {
            return text;
        }

        endIndex += end.Length;
        while (endIndex < text.Length && (text[endIndex] == '\r' || text[endIndex] == '\n'))
        {
            endIndex++;
        }

        return text.Remove(beginIndex, endIndex - beginIndex);
    }
}

internal static class ZapretDiagnostics
{
    private static readonly string[] ConflictServices = ["GoodbyeDPI", "discordfix_zapret", "winws1", "winws2"];

    public static async Task RunAsync(ZapretPaths paths, Action<string> log)
    {
        log("Диагностика запущена.");
        log("Base Filtering Engine: " + await ZapretServiceManager.QueryServiceStateAsync("BFE"));
        await CheckProxyAsync(log);
        await CheckTcpTimestampsAsync(log);
        CheckProcess("AdguardSvc", "Adguard может мешать Discord.", log);

        var services = await GetAllServicesTextAsync();
        LogServiceSearch(services, "Killer", "Killer services найдены, возможен конфликт.", log);
        LogServiceSearch(services, "Intel", "Intel Connectivity Network Service может конфликтовать.", log, ["Connectivity", "Network"]);
        LogServiceSearch(services, "SmartByte", "SmartByte найден, возможен конфликт.", log);
        LogServiceSearch(services, "VPN", "VPN services найдены, выключи VPN если Discord не работает.", log);
        LogServiceSearch(services, "TracSrvWrapper", "Check Point найден, возможен конфликт.", log);
        LogServiceSearch(services, "EPWD", "Check Point EPWD найден, возможен конфликт.", log);

        log("WinDivert64.sys: " + (Directory.GetFiles(paths.BinPath, "*.sys").Length > 0 ? "найден" : "не найден"));
        log("Secure DNS: " + (HasSecureDnsConfigured() ? "настроен в Windows" : "не найден в Windows, проверь Secure DNS в браузере"));
        CheckHostsForDiscord(log);

        var conflicts = await FindConflictingServicesAsync();
        if (conflicts.Count > 0)
        {
            log("Конфликтующие службы: " + string.Join(", ", conflicts) + ". Можно нажать Убрать конфликты.");
        }
        else
        {
            log("Конфликтующие службы не найдены.");
        }

        if (!ZapretProcess.IsRunning(paths)
            && (await ZapretServiceManager.QueryServiceStateAsync("WinDivert")).Contains("работает", StringComparison.OrdinalIgnoreCase))
        {
            log("WinDivert работает без winws.exe. Пробую удалить WinDivert...");
            await CommandRunner.RunAsync("net.exe", ["stop", "WinDivert"], 10000);
            await CommandRunner.RunAsync("sc.exe", ["delete", "WinDivert"], 10000);
        }

        log("Диагностика завершена.");
    }

    public static async Task ClearDiscordCacheAsync(Action<string> log)
    {
        foreach (var process in Process.GetProcessesByName("Discord"))
        {
            try
            {
                log("Закрываю Discord.exe...");
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
            catch (Exception ex)
            {
                log("Не удалось закрыть Discord: " + ex.Message);
            }
            finally
            {
                process.Dispose();
            }
        }

        var discordDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "discord");
        foreach (var name in new[] { "Cache", "Code Cache", "GPUCache" })
        {
            var dir = Path.Combine(discordDir, name);
            if (!Directory.Exists(dir))
            {
                log(dir + " не найден.");
                continue;
            }

            try
            {
                Directory.Delete(dir, recursive: true);
                log("Удалено: " + dir);
            }
            catch (Exception ex)
            {
                log("Не удалось удалить " + dir + ": " + ex.Message);
            }
        }
    }

    public static async Task RemoveConflictingServicesAsync(Action<string> log)
    {
        var conflicts = await FindConflictingServicesAsync();
        if (conflicts.Count == 0)
        {
            log("Конфликтующие службы не найдены.");
        }

        foreach (var service in conflicts)
        {
            log("Удаляю конфликтующую службу: " + service);
            await CommandRunner.RunAsync("net.exe", ["stop", service], 10000);
            await CommandRunner.RunAsync("sc.exe", ["delete", service], 10000);
        }

        foreach (var service in new[] { "WinDivert", "WinDivert14" })
        {
            await CommandRunner.RunAsync("net.exe", ["stop", service], 10000);
            await CommandRunner.RunAsync("sc.exe", ["delete", service], 10000);
        }

        log("Очистка конфликтов завершена.");
    }

    private static async Task CheckProxyAsync(Action<string> log)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
            var enabled = Convert.ToInt32(key?.GetValue("ProxyEnable") ?? 0) == 1;
            var server = key?.GetValue("ProxyServer") as string;
            log(enabled ? "System proxy включен: " + server : "Proxy: выключен");
        }
        catch (Exception ex)
        {
            log("Proxy check: " + ex.Message);
        }

        await Task.CompletedTask;
    }

    private static async Task CheckTcpTimestampsAsync(Action<string> log)
    {
        var show = await CommandRunner.RunAsync("netsh.exe", ["interface", "tcp", "show", "global"], 10000);
        if (show.CombinedOutput.Contains("timestamps", StringComparison.OrdinalIgnoreCase)
            && show.CombinedOutput.Contains("enabled", StringComparison.OrdinalIgnoreCase))
        {
            log("TCP timestamps: включены");
            return;
        }

        log("TCP timestamps: выключены, включаю...");
        var set = await CommandRunner.RunAsync("netsh.exe", ["interface", "tcp", "set", "global", "timestamps=enabled"], 10000);
        log(set.Success ? "TCP timestamps включены." : "Не удалось включить TCP timestamps.");
    }

    private static void CheckProcess(string processName, string warning, Action<string> log)
    {
        var found = Process.GetProcessesByName(processName).Length > 0;
        log(found ? warning : processName + ": не найден");
    }

    private static async Task<string> GetAllServicesTextAsync()
    {
        var result = await CommandRunner.RunAsync("sc.exe", ["query", "state=", "all"], 15000);
        return result.CombinedOutput;
    }

    private static void LogServiceSearch(string servicesText, string term, string warning, Action<string> log, string[]? extraTerms = null)
    {
        var found = servicesText.Contains(term, StringComparison.OrdinalIgnoreCase)
            && (extraTerms is null || extraTerms.All(x => servicesText.Contains(x, StringComparison.OrdinalIgnoreCase)));
        log(found ? warning : term + ": не найден");
    }

    private static bool HasSecureDnsConfigured()
    {
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(@"System\CurrentControlSet\Services\Dnscache\InterfaceSpecificParameters");
            return root is not null && HasDohFlags(root);
        }
        catch
        {
            return false;
        }
    }

    private static bool HasDohFlags(RegistryKey key)
    {
        var value = key.GetValue("DohFlags");
        if (value is not null && Convert.ToInt32(value) > 0)
        {
            return true;
        }

        foreach (var name in key.GetSubKeyNames())
        {
            using var child = key.OpenSubKey(name);
            if (child is not null && HasDohFlags(child))
            {
                return true;
            }
        }

        return false;
    }

    private static void CheckHostsForDiscord(Action<string> log)
    {
        var hostsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "drivers", "etc", "hosts");
        if (!File.Exists(hostsFile))
        {
            log("hosts: файл не найден");
            return;
        }

        var text = File.ReadAllText(hostsFile);
        var hasDiscord = text.Contains("discord.com", StringComparison.OrdinalIgnoreCase)
            || text.Contains("discordapp.com", StringComparison.OrdinalIgnoreCase);
        log(hasDiscord ? "hosts: есть записи Discord, это может быть нормально после обновления hosts" : "hosts: лишних Discord записей не найдено");
    }

    private static async Task<List<string>> FindConflictingServicesAsync()
    {
        var found = new List<string>();
        foreach (var service in ConflictServices)
        {
            var state = await ZapretServiceManager.QueryServiceStateAsync(service);
            if (!state.Contains("не установлена", StringComparison.OrdinalIgnoreCase))
            {
                found.Add(service);
            }
        }

        return found;
    }
}

internal static class DiscordChecker
{
    public static async Task CheckAsync(Action<string> log)
    {
        log("Быстрая проверка Discord...");
        await CheckDnsAsync(log);
        await CheckHttpAsync("Discord API", "https://discord.com/api/v10/gateway", log);
        await CheckHttpAsync("Discord CDN", "https://cdn.discordapp.com/", log);
        await CheckGatewayAsync(log);
        log("Проверка Discord завершена.");
    }

    private static async Task CheckDnsAsync(Action<string> log)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var addresses = await Dns.GetHostAddressesAsync("discord.com", cts.Token);
            log(addresses.Length > 0 ? "DNS discord.com: OK" : "DNS discord.com: нет адресов");
        }
        catch (Exception ex)
        {
            log("DNS discord.com: ошибка - " + ex.Message);
        }
    }

    private static async Task CheckHttpAsync(string name, string url, Action<string> log)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            var ok = (int)response.StatusCode < 500;
            log($"{name}: {(ok ? "OK" : "ошибка")} HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            log($"{name}: ошибка - {ex.Message}");
        }
    }

    private static async Task CheckGatewayAsync(Action<string> log)
    {
        try
        {
            using var ws = new ClientWebSocket();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await ws.ConnectAsync(new Uri("wss://gateway.discord.gg/?v=10&encoding=json"), cts.Token);
            log(ws.State == WebSocketState.Open ? "Discord Gateway WebSocket: OK" : "Discord Gateway WebSocket: " + ws.State);
            if (ws.State == WebSocketState.Open)
            {
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "check complete", CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            log("Discord Gateway WebSocket: ошибка - " + ex.Message);
        }
    }
}

internal static class BatchStrategyParser
{
    public static IReadOnlyList<string> ParseArguments(string batPath, ZapretPaths paths, GameFilterSettings filter)
    {
        var lines = File.ReadAllLines(batPath, Encoding.UTF8);
        var command = new StringBuilder();
        var capture = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("::", StringComparison.Ordinal))
            {
                continue;
            }

            if (!capture)
            {
                if (line.IndexOf("winws.exe", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                capture = true;
                line = TakeAfterWinws(line);
            }

            var trimmedEnd = line.TrimEnd();
            var continues = trimmedEnd.EndsWith("^", StringComparison.Ordinal);
            if (continues)
            {
                line = trimmedEnd[..^1].TrimEnd();
            }

            command.Append(' ').Append(line);

            if (!continues)
            {
                break;
            }
        }

        var expanded = ExpandVariables(command.ToString(), paths, filter);
        return SplitCommandLine(expanded);
    }

    private static string TakeAfterWinws(string line)
    {
        var index = line.IndexOf("winws.exe", StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return line;
        }

        var rest = line[(index + "winws.exe".Length)..].TrimStart();
        if (rest.StartsWith('"'))
        {
            rest = rest[1..].TrimStart();
        }

        return rest;
    }

    private static string ExpandVariables(string input, ZapretPaths paths, GameFilterSettings filter)
    {
        var result = input;
        result = ReplaceIgnoreCase(result, "%~dp0", paths.RootWithSlash);
        result = ReplaceIgnoreCase(result, "%BIN%", paths.BinWithSlash);
        result = ReplaceIgnoreCase(result, "%LISTS%", paths.ListsWithSlash);
        result = ReplaceIgnoreCase(result, "%GameFilterTCP%", filter.Tcp);
        result = ReplaceIgnoreCase(result, "%GameFilterUDP%", filter.Udp);
        result = ReplaceIgnoreCase(result, "%GameFilter%", filter.GameFilter);
        return result;
    }

    private static string ReplaceIgnoreCase(string source, string search, string replacement)
    {
        var builder = new StringBuilder();
        var index = 0;

        while (true)
        {
            var found = source.IndexOf(search, index, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
            {
                builder.Append(source, index, source.Length - index);
                return builder.ToString();
            }

            builder.Append(source, index, found - index);
            builder.Append(replacement);
            index = found + search.Length;
        }
    }

    private static IReadOnlyList<string> SplitCommandLine(string command)
    {
        var args = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < command.Length; i++)
        {
            var c = command[i];

            if (c == '^' && i + 1 < command.Length)
            {
                current.Append(command[++i]);
                continue;
            }

            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                Flush();
                continue;
            }

            current.Append(c);
        }

        Flush();
        return args;

        void Flush()
        {
            if (current.Length == 0)
            {
                return;
            }

            args.Add(current.ToString());
            current.Clear();
        }
    }
}

internal static class ZapretProcess
{
    public static bool IsRunning(ZapretPaths paths)
    {
        var processes = FindWinwsProcesses(paths);
        var running = processes.Count > 0;
        foreach (var process in processes)
        {
            process.Dispose();
        }

        return running;
    }

    public static bool StopAll(ZapretPaths paths, Action<string> log)
    {
        var stopped = false;
        foreach (var process in FindWinwsProcesses(paths))
        {
            try
            {
                log($"Останавливаю winws.exe, PID {process.Id}.");
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
                stopped = true;
            }
            catch (Exception ex)
            {
                log($"Не удалось остановить PID {process.Id}: {ex.Message}");
            }
            finally
            {
                process.Dispose();
            }
        }

        return stopped;
    }

    private static List<Process> FindWinwsProcesses(ZapretPaths paths)
    {
        var result = new List<Process>();
        var expected = Path.GetFullPath(paths.WinwsPath);
        foreach (var process in Process.GetProcessesByName("winws"))
        {
            string? modulePath = null;
            try
            {
                modulePath = process.MainModule?.FileName;
            }
            catch
            {
                // Elevated builds should see the path. If Windows denies it, avoid killing an unknown process.
            }

            if (modulePath is not null
                && string.Equals(Path.GetFullPath(modulePath), expected, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(process);
            }
            else
            {
                process.Dispose();
            }
        }

        return result;
    }
}

internal class RoundedPanel : Panel
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Radius { get; set; } = 8;

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = DrawingHelpers.RoundedRect(ClientRectangle with { Width = ClientRectangle.Width - 1, Height = ClientRectangle.Height - 1 }, Radius);
        using var brush = new SolidBrush(BackColor);
        using var border = new Pen(Color.FromArgb(38, 48, 68));
        e.Graphics.FillPath(brush, path);
        e.Graphics.DrawPath(border, path);
    }
}

internal class ModernButton : Button
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BaseColor { get; set; } = Color.FromArgb(45, 56, 78);

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color HoverColor { get; set; } = Color.FromArgb(58, 71, 98);

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color PressColor { get; set; } = Color.FromArgb(34, 43, 62);

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Radius { get; set; } = 8;

    private bool _hovered;
    private bool _pressed;

    public ModernButton()
    {
        DoubleBuffered = true;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        _pressed = true;
        Invalidate();
        base.OnMouseDown(mevent);
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(mevent);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(DrawingHelpers.ResolveBackColor(this));
        var color = !Enabled ? Color.FromArgb(50, 58, 72) : _pressed ? PressColor : _hovered ? HoverColor : BaseColor;

        using var path = DrawingHelpers.RoundedRect(ClientRectangle with { Width = ClientRectangle.Width - 1, Height = ClientRectangle.Height - 1 }, Radius);
        using var brush = new SolidBrush(color);
        e.Graphics.FillPath(brush, path);

        TextRenderer.DrawText(
            e.Graphics,
            Text,
            Font,
            ClientRectangle,
            Enabled ? ForeColor : Color.FromArgb(120, 130, 148),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
    }
}

internal sealed class StrategyListPanel : Control
{
    private const int ItemHeight = 72;
    private const int ItemGap = 10;
    private const int ScrollbarWidth = 10;

    private readonly List<StrategyItem> _items = [];
    private int _selectedIndex = -1;
    private int _hoverIndex = -1;
    private int _scrollY;
    private bool _draggingThumb;
    private int _dragStartY;
    private int _dragStartScrollY;

    public event EventHandler<StrategyItem>? StrategySelected;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public StrategyItem? SelectedStrategy
    {
        get => _selectedIndex >= 0 && _selectedIndex < _items.Count ? _items[_selectedIndex] : null;
        set
        {
            _selectedIndex = value is null
                ? -1
                : _items.FindIndex(x => string.Equals(x.FilePath, value.FilePath, StringComparison.OrdinalIgnoreCase));
            EnsureSelectedVisible();
            Invalidate();
        }
    }

    public StrategyListPanel()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
    }

    public void SetItems(IEnumerable<StrategyItem> items)
    {
        _items.Clear();
        _items.AddRange(items);
        _selectedIndex = Math.Clamp(_selectedIndex, -1, _items.Count - 1);
        _hoverIndex = -1;
        ClampScroll();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(DrawingHelpers.ResolveBackColor(this));

        var needsScroll = ContentHeight > ClientSize.Height;
        var itemWidth = ClientSize.Width - (needsScroll ? ScrollbarWidth + 10 : 2);
        var y = -_scrollY;

        for (var i = 0; i < _items.Count; i++)
        {
            var itemRect = new Rectangle(0, y, Math.Max(10, itemWidth), ItemHeight);
            if (itemRect.Bottom >= 0 && itemRect.Top <= ClientSize.Height)
            {
                DrawItem(e.Graphics, itemRect, _items[i], i == _selectedIndex, i == _hoverIndex);
            }

            y += ItemHeight + ItemGap;
        }

        if (needsScroll)
        {
            DrawScrollbar(e.Graphics);
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        _scrollY -= Math.Sign(e.Delta) * 56;
        ClampScroll();
        Invalidate();
        base.OnMouseWheel(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_draggingThumb)
        {
            var track = ScrollbarTrack;
            var thumbHeight = ThumbHeight(track.Height);
            var maxThumbY = Math.Max(1, track.Height - thumbHeight);
            var deltaY = e.Y - _dragStartY;
            _scrollY = _dragStartScrollY + (int)Math.Round(deltaY * (double)MaxScroll / maxThumbY);
            ClampScroll();
            Invalidate();
            return;
        }

        var newHover = HitTestItem(e.Location);
        if (newHover != _hoverIndex)
        {
            _hoverIndex = newHover;
            Invalidate();
        }

        Cursor = IsOverScrollbar(e.Location) ? Cursors.Default : Cursors.Hand;
        base.OnMouseMove(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();

        if (IsOverScrollbarThumb(e.Location))
        {
            _draggingThumb = true;
            _dragStartY = e.Y;
            _dragStartScrollY = _scrollY;
            Capture = true;
            return;
        }

        var index = HitTestItem(e.Location);
        if (index >= 0)
        {
            _selectedIndex = index;
            StrategySelected?.Invoke(this, _items[index]);
            Invalidate();
        }

        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _draggingThumb = false;
        Capture = false;
        base.OnMouseUp(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (!_draggingThumb)
        {
            _hoverIndex = -1;
            Cursor = Cursors.Default;
            Invalidate();
        }

        base.OnMouseLeave(e);
    }

    protected override void OnResize(EventArgs e)
    {
        ClampScroll();
        base.OnResize(e);
    }

    private void DrawItem(Graphics graphics, Rectangle rect, StrategyItem strategy, bool selected, bool hovered)
    {
        var fill = selected
            ? Color.FromArgb(28, 92, 88)
            : hovered
                ? Color.FromArgb(32, 43, 60)
                : Color.FromArgb(24, 32, 47);
        var borderColor = selected ? Color.FromArgb(44, 197, 158) : Color.FromArgb(38, 48, 66);

        using var path = DrawingHelpers.RoundedRect(rect, 8);
        using var brush = new SolidBrush(fill);
        using var border = new Pen(borderColor);
        graphics.FillPath(brush, path);
        graphics.DrawPath(border, path);

        if (selected)
        {
            using var accent = new SolidBrush(Color.FromArgb(44, 197, 158));
            graphics.FillRectangle(accent, new Rectangle(rect.Left, rect.Top + 13, 4, rect.Height - 26));
        }

        using var titleFont = new Font("Segoe UI Semibold", 10.8f);
        using var subFont = new Font("Segoe UI", 8.6f);
        var titleRect = new Rectangle(rect.Left + 18, rect.Top + 12, rect.Width - 32, 24);
        var subRect = new Rectangle(rect.Left + 18, rect.Top + 39, rect.Width - 32, 20);

        TextRenderer.DrawText(graphics, strategy.DisplayName, titleFont, titleRect, Color.FromArgb(246, 249, 255), TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(graphics, strategy.FileName, subFont, subRect, Color.FromArgb(156, 172, 199), TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }

    private void DrawScrollbar(Graphics graphics)
    {
        var track = ScrollbarTrack;
        using var trackPath = DrawingHelpers.RoundedRect(track, 4);
        using var trackBrush = new SolidBrush(Color.FromArgb(31, 40, 56));
        graphics.FillPath(trackBrush, trackPath);

        var thumb = ScrollbarThumb;
        using var thumbPath = DrawingHelpers.RoundedRect(thumb, 4);
        using var thumbBrush = new SolidBrush(_draggingThumb ? Color.FromArgb(88, 106, 138) : Color.FromArgb(66, 82, 112));
        graphics.FillPath(thumbBrush, thumbPath);
    }

    private int HitTestItem(Point point)
    {
        if (point.X < 0 || point.X > ClientSize.Width - ScrollbarWidth - 8)
        {
            return -1;
        }

        var y = point.Y + _scrollY;
        if (y < 0)
        {
            return -1;
        }

        var stride = ItemHeight + ItemGap;
        var index = y / stride;
        var localY = y % stride;
        return index >= 0 && index < _items.Count && localY < ItemHeight ? index : -1;
    }

    private void EnsureSelectedVisible()
    {
        if (_selectedIndex < 0)
        {
            return;
        }

        var itemTop = _selectedIndex * (ItemHeight + ItemGap);
        var itemBottom = itemTop + ItemHeight;
        if (itemTop < _scrollY)
        {
            _scrollY = itemTop;
        }
        else if (itemBottom > _scrollY + ClientSize.Height)
        {
            _scrollY = itemBottom - ClientSize.Height;
        }

        ClampScroll();
    }

    private bool IsOverScrollbar(Point point) => ContentHeight > ClientSize.Height && ScrollbarTrack.Contains(point);

    private bool IsOverScrollbarThumb(Point point) => ContentHeight > ClientSize.Height && ScrollbarThumb.Contains(point);

    private Rectangle ScrollbarTrack => new(ClientSize.Width - ScrollbarWidth, 2, 6, Math.Max(1, ClientSize.Height - 4));

    private Rectangle ScrollbarThumb
    {
        get
        {
            var track = ScrollbarTrack;
            var height = ThumbHeight(track.Height);
            var maxThumbY = Math.Max(1, track.Height - height);
            var y = track.Top + (MaxScroll == 0 ? 0 : (int)Math.Round(_scrollY / (double)MaxScroll * maxThumbY));
            return new Rectangle(track.Left, y, track.Width, height);
        }
    }

    private int ThumbHeight(int trackHeight)
    {
        if (trackHeight <= 42)
        {
            return Math.Max(1, trackHeight);
        }

        return Math.Clamp((int)Math.Round(ClientSize.Height / (double)ContentHeight * trackHeight), 42, trackHeight);
    }

    private int ContentHeight => _items.Count == 0 ? 0 : _items.Count * ItemHeight + (_items.Count - 1) * ItemGap;

    private int MaxScroll => Math.Max(0, ContentHeight - ClientSize.Height);

    private void ClampScroll() => _scrollY = Math.Clamp(_scrollY, 0, MaxScroll);
}

internal sealed class StrategyButton : Button
{
    public StrategyItem Strategy { get; }

    private bool _hovered;
    private bool _isSelected;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            _isSelected = value;
            Invalidate();
        }
    }

    public StrategyButton(StrategyItem strategy)
    {
        Strategy = strategy;
        DoubleBuffered = true;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        Cursor = Cursors.Hand;
        TextAlign = ContentAlignment.MiddleLeft;
        Font = new Font("Segoe UI Semibold", 11.5f);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(DrawingHelpers.ResolveBackColor(this));
        var rect = ClientRectangle with { Width = ClientRectangle.Width - 1, Height = ClientRectangle.Height - 1 };
        var fill = IsSelected
            ? Color.FromArgb(31, 82, 86)
            : _hovered
                ? Color.FromArgb(34, 44, 62)
                : Color.FromArgb(25, 34, 50);

        using var path = DrawingHelpers.RoundedRect(rect, 8);
        using var brush = new SolidBrush(fill);
        using var border = new Pen(IsSelected ? Color.FromArgb(44, 197, 158) : Color.FromArgb(40, 50, 70));
        e.Graphics.FillPath(brush, path);
        e.Graphics.DrawPath(border, path);

        if (IsSelected)
        {
            using var accent = new SolidBrush(Color.FromArgb(44, 197, 158));
            e.Graphics.FillRectangle(accent, new Rectangle(0, 13, 4, Height - 26));
        }

        var titleRect = new Rectangle(16, 12, Width - 32, 26);
        var subRect = new Rectangle(16, 40, Width - 32, 20);
        using var subFont = new Font("Segoe UI", 8.5f);
        TextRenderer.DrawText(e.Graphics, Strategy.DisplayName, Font, titleRect, Color.FromArgb(242, 246, 255), TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(e.Graphics, Strategy.FileName, subFont, subRect, Color.FromArgb(145, 157, 178), TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }

    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
    }
}

internal static class DrawingHelpers
{
    public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));

        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static Color ResolveBackColor(Control control)
    {
        for (var current = control.Parent; current is not null; current = current.Parent)
        {
            if (current.BackColor != Color.Transparent)
            {
                return current.BackColor;
            }
        }

        return Color.FromArgb(12, 16, 24);
    }
}
