using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OtSnapshotGui;

internal sealed class MainForm : Form
{
    private readonly GuiSettings _settings = GuiSettings.Load();
    private readonly ListBox _servers = new();
    private readonly TextBox _serverInput = new();
    private readonly TextBox _configPath = new();
    private readonly TextBox _outputRoot = new();
    private readonly TextBox _engineExe = new();
    private readonly TextBox _console = new();
    private readonly Label _status = new() { AutoSize = true };
    private readonly Button _runButton = new() { Text = "Collect && Report", Height = 34 };
    private readonly Button _viewButton = new() { Text = "View Report", Height = 34, Enabled = false };
    private readonly Dictionary<string, Label> _badges = new(StringComparer.OrdinalIgnoreCase);
    private bool _serversFileInvalid;

    public MainForm()
    {
        Text = "OT Snapshot Reporter";
        Width = 1040;
        Height = 760;
        MinimumSize = new Size(900, 620);
        StartPosition = FormStartPosition.CenterScreen;

        BuildLayout();
        LoadSettingsToUi();
        LoadServers();
        CheckPrerequisites();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        try
        {
            SaveSettingsFromUi();
            _settings.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            MessageBox.Show($"Could not save GUI settings: {ex.Message}", "Settings", MessageBoxButtons.OK, MessageBoxIcon.Error);
            e.Cancel = true;
            return;
        }

        base.OnFormClosing(e);
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 2, RowCount = 5 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 250));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        Controls.Add(root);

        var serverBox = new GroupBox { Text = "Servers", Dock = DockStyle.Fill };
        var serverLayout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), RowCount = 3 };
        serverLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        serverLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        serverLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        _servers.Dock = DockStyle.Fill;
        _serverInput.Dock = DockStyle.Fill;
        var serverButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        var add = new Button { Text = "Add", Width = 80 };
        var remove = new Button { Text = "Remove", Width = 90 };
        add.Click += (_, _) => AddServer();
        remove.Click += (_, _) => RemoveServer();
        serverButtons.Controls.AddRange([add, remove]);
        serverLayout.Controls.Add(_servers, 0, 0);
        serverLayout.Controls.Add(_serverInput, 0, 1);
        serverLayout.Controls.Add(serverButtons, 0, 2);
        serverBox.Controls.Add(serverLayout);

        var pathBox = new GroupBox { Text = "Paths", Dock = DockStyle.Fill };
        var paths = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), ColumnCount = 3, RowCount = 3 };
        paths.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
        paths.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        paths.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42));
        AddPathRow(paths, 0, "Config", _configPath, true);
        AddPathRow(paths, 1, "Output", _outputRoot, true);
        AddPathRow(paths, 2, "Engine", _engineExe, false);
        pathBox.Controls.Add(paths);

        root.Controls.Add(serverBox, 0, 0);
        root.Controls.Add(pathBox, 1, 0);

        var summary = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, 14, 0, 0) };
        foreach (var severity in new[] { "Critical", "High", "Medium", "Low", "Info" })
        {
            var badge = new Label { Text = $"{severity}: 0", AutoSize = true, Padding = new Padding(10, 6, 10, 6), Margin = new Padding(0, 0, 10, 0), BackColor = BadgeColor(severity), ForeColor = Color.Black };
            _badges[severity] = badge;
            summary.Controls.Add(badge);
        }
        root.SetColumnSpan(summary, 2);
        root.Controls.Add(summary, 0, 1);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        _runButton.Width = 160;
        _viewButton.Width = 120;
        _runButton.Click += async (_, _) => await CollectAndReportAsync();
        _viewButton.Click += (_, _) => OpenReport();
        actions.Controls.AddRange([_runButton, _viewButton]);
        root.SetColumnSpan(actions, 2);
        root.Controls.Add(actions, 0, 2);

        _console.Dock = DockStyle.Fill;
        _console.Multiline = true;
        _console.ReadOnly = true;
        _console.ScrollBars = ScrollBars.Vertical;
        _console.Font = new Font(FontFamily.GenericMonospace, 9);
        root.SetColumnSpan(_console, 2);
        root.Controls.Add(_console, 0, 3);

        _status.Text = "Ready";
        root.SetColumnSpan(_status, 2);
        root.Controls.Add(_status, 0, 4);
    }

    private static void AddPathRow(TableLayoutPanel table, int row, string label, TextBox box, bool folder)
    {
        box.Dock = DockStyle.Fill;
        var browse = new Button { Text = "...", Dock = DockStyle.Fill };
        browse.Click += (_, _) =>
        {
            if (folder)
            {
                using var dialog = new FolderBrowserDialog();
                if (dialog.ShowDialog() == DialogResult.OK) box.Text = dialog.SelectedPath;
            }
            else
            {
                using var dialog = new OpenFileDialog { Filter = "Executables|*.exe;*.dll|All files|*.*" };
                if (dialog.ShowDialog() == DialogResult.OK) box.Text = dialog.FileName;
            }
        };
        table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        table.Controls.Add(box, 1, row);
        table.Controls.Add(browse, 2, row);
    }

    private void LoadSettingsToUi()
    {
        _configPath.Text = _settings.ConfigPath;
        _outputRoot.Text = _settings.OutputRoot;
        _engineExe.Text = _settings.EngineExe;
        _viewButton.Enabled = File.Exists(Path.Combine(_settings.LastReportPath, "index.html")) && Directory.Exists(Path.Combine(_settings.LastReportPath, "raw"));
    }

    private void SaveSettingsFromUi()
    {
        _settings.ConfigPath = _configPath.Text;
        _settings.OutputRoot = _outputRoot.Text;
        _settings.EngineExe = _engineExe.Text;
    }

    private void LoadServers()
    {
        _servers.Items.Clear();
        _serversFileInvalid = false;
        var path = ServersPath();
        if (!File.Exists(path)) return;
        try
        {
            var json = JsonNode.Parse(File.ReadAllText(path));
            if (json is not JsonObject root || root["servers"] is not JsonArray serverEntries)
            {
                throw new InvalidDataException("servers.json must contain a 'servers' array of objects with names.");
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var server in serverEntries)
            {
                var name = server?["name"]?.GetValue<string>()?.Trim();
                if (!string.IsNullOrWhiteSpace(name) && seen.Add(name)) _servers.Items.Add(name);
            }

            if (_servers.Items.Count == 0)
            {
                throw new InvalidDataException("servers.json must contain at least one non-empty server name.");
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or InvalidOperationException or IOException)
        {
            _serversFileInvalid = true;
            AppendLine($"WARNING: Could not load servers.json: {ex.Message}");
        }
    }

    private void SaveServers()
    {
        if (_serversFileInvalid)
        {
            throw new InvalidOperationException("servers.json is invalid; add or remove a server to replace it deliberately.");
        }

        if (_servers.Items.Count == 0)
        {
            throw new InvalidOperationException("servers.json must contain at least one non-empty server name.");
        }

        Directory.CreateDirectory(_configPath.Text);
        var servers = new JsonArray();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in _servers.Items)
        {
            var name = item?.ToString()?.Trim();
            if (!string.IsNullOrWhiteSpace(name) && seen.Add(name))
            {
                servers.Add(new JsonObject { ["name"] = name, ["roles"] = new JsonArray() });
            }
        }

        if (servers.Count == 0)
        {
            throw new InvalidOperationException("servers.json must contain at least one non-empty server name.");
        }

        var path = ServersPath();
        var json = new JsonObject { ["servers"] = servers }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        JsonNode.Parse(json);
        AtomicFile.WriteText(path, json);
    }

    private void AddServer()
    {
        var name = _serverInput.Text.Trim();
        if (name.Length == 0 || HasServer(name)) return;

        var previousInvalidState = _serversFileInvalid;
        var insertedIndex = _servers.Items.Count;
        _servers.Items.Add(name);
        _serversFileInvalid = false;
        if (!TrySaveServers())
        {
            _servers.Items.RemoveAt(insertedIndex);
            _serversFileInvalid = previousInvalidState;
            return;
        }

        _serverInput.Clear();
    }

    private void RemoveServer()
    {
        var selected = _servers.SelectedIndices
            .Cast<int>()
            .OrderBy(index => index)
            .Select(index => (Index: index, Value: _servers.Items[index]))
            .ToArray();
        if (selected.Length == 0) return;

        var previousInvalidState = _serversFileInvalid;
        foreach (var entry in selected.OrderByDescending(entry => entry.Index))
        {
            _servers.Items.RemoveAt(entry.Index);
        }
        _serversFileInvalid = false;
        if (TrySaveServers()) return;

        foreach (var entry in selected)
        {
            _servers.Items.Insert(entry.Index, entry.Value);
        }

        _serversFileInvalid = previousInvalidState;
        foreach (var entry in selected)
        {
            _servers.SetSelected(entry.Index, true);
        }
    }

    private bool TrySaveServers()
    {
        try
        {
            SaveServers();
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            MessageBox.Show($"Could not save servers.json: {ex.Message}", "Servers", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private async Task CollectAndReportAsync()
    {
        _console.Clear();
        _runButton.Enabled = false;
        _viewButton.Enabled = false;

        try
        {
            SaveSettingsFromUi();
            SaveServers();
            var previousReport = LatestReportFolder();
            if (previousReport is not null)
            {
                AppendLine($"Using previous report for drift: {previousReport}");
            }

            SetStatus("Running collectors...");
            var inputPath = NewCollectionPath();
            Directory.CreateDirectory(inputPath);
            await RunProcessAsync("powershell.exe", [
                "-NoProfile",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                CollectorScript(),
                "-ConfigPath",
                _configPath.Text,
                "-OutputPath",
                inputPath
            ]);
            SetStatus("Generating report...");
            await RunProcessAsync(EngineHost(), EngineArgs(inputPath, previousReport));
            var latest = LatestReportFolder();
            if (latest is null)
            {
                SetStatus("Report process completed, but no report folder was found.");
                return;
            }

            _settings.LastReportPath = latest;
            _settings.Save();
            UpdateBadges(latest);
            _viewButton.Enabled = File.Exists(Path.Combine(latest, "index.html"));
            SetStatus($"Report ready: {latest}");
        }
        catch (Exception ex)
        {
            AppendLine("ERROR: " + ex.Message);
            SetStatus("Failed");
        }
        finally
        {
            _runButton.Enabled = true;
        }
    }

    private async Task RunProcessAsync(string fileName, IEnumerable<string> arguments)
    {
        var argumentList = arguments.ToList();
        AppendLine($"> {fileName} {string.Join(" ", argumentList.Select(FormatArgumentForLog))}");
        var start = new ProcessStartInfo(fileName) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in argumentList)
        {
            start.ArgumentList.Add(argument);
        }
        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new InvalidOperationException($"Could not start {fileName}");
        var outputTask = PumpOutputAsync(process.StandardOutput);
        var errorTask = PumpOutputAsync(process.StandardError);
        await Task.WhenAll(process.WaitForExitAsync(), outputTask, errorTask);
        if (process.ExitCode != 0) throw new InvalidOperationException($"{fileName} exited with {process.ExitCode}");
    }

    private async Task PumpOutputAsync(StreamReader reader)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            AppendLine(line);
        }
    }

    private void UpdateBadges(string reportDir)
    {
        var counts = _badges.Keys.ToDictionary(x => x, _ => 0, StringComparer.OrdinalIgnoreCase);
        var summaryPath = Path.Combine(reportDir, "summary.json");
        var loaded = false;
        if (File.Exists(summaryPath))
        {
            try
            {
                var doc = JsonNode.Parse(File.ReadAllText(summaryPath));
                var jsonCounts = doc?["counts"] ?? throw new JsonException("Summary is missing counts.");
                foreach (var severity in _badges.Keys)
                {
                    counts[severity] = jsonCounts[severity]?.GetValue<int>() ?? 0;
                }

                loaded = true;
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or IOException or UnauthorizedAccessException)
            {
                foreach (var severity in _badges.Keys) counts[severity] = 0;
                AppendLine($"WARNING: Could not read report summary: {ex.Message}");
            }
        }

        if (!loaded)
        {
            var exceptionsPath = Path.Combine(reportDir, "exceptions.csv");
            if (File.Exists(exceptionsPath))
            {
                try
                {
                    foreach (var line in File.ReadLines(exceptionsPath).Skip(1))
                    {
                        var cells = SplitCsv(line);
                        if (cells.Count >= 4 && counts.ContainsKey(cells[3])) counts[cells[3]]++;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    AppendLine($"WARNING: Could not read report exceptions: {ex.Message}");
                }
            }
        }

        foreach (var (severity, label) in _badges) label.Text = $"{severity}: {counts[severity]}";
    }

    private void CheckPrerequisites()
    {
        var missing = new List<string>();
        if (!File.Exists(CollectorScript())) missing.Add("collector script");
        if (!File.Exists(_engineExe.Text)) missing.Add("engine executable");
        if (!Directory.Exists(_configPath.Text)) missing.Add("config path");
        else if (_serversFileInvalid) missing.Add("valid servers config");
        else if (!File.Exists(ServersPath())) missing.Add("servers config");
        SetStatus(missing.Count == 0 ? "Ready" : "Missing " + string.Join(", ", missing));
    }

    private void OpenReport()
    {
        try
        {
            var index = Path.HasExtension(_settings.LastReportPath)
                ? _settings.LastReportPath
                : Path.Combine(_settings.LastReportPath, "index.html");
            if (string.IsNullOrWhiteSpace(index) || !File.Exists(index))
            {
                MessageBox.Show("No report available.", "View Report", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var outputRoot = Path.GetFullPath(_outputRoot.Text);
            var outputRootWithSeparator = outputRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var resolvedIndex = Path.GetFullPath(index);
            if (!resolvedIndex.StartsWith(outputRootWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("Report path is outside the output directory.", "Security", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!resolvedIndex.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("Report path is not an HTML file.", "Security", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Process.Start(new ProcessStartInfo(resolvedIndex) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show($"Could not open report: {ex.Message}", "View Report", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private string ServersPath() => Path.Combine(_configPath.Text, "servers.json");
    private string CollectorScript() => _settings.CollectorScript;
    private string EngineHost() => _engineExe.Text.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? "dotnet" : _engineExe.Text;
    private string NewCollectionPath()
    {
        var stamp = DateTime.Now;
        while (true)
        {
            var candidate = Path.Combine(_outputRoot.Text, $"collection_{stamp:yyyy-MM-dd_HHmmss}");
            if (!Directory.Exists(candidate) && !File.Exists(candidate)) return candidate;
            stamp = stamp.AddSeconds(1);
        }
    }

    private IEnumerable<string> EngineArgs(string inputPath, string? previousPath)
    {
        if (_engineExe.Text.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            yield return _engineExe.Text;
        }

        yield return "--input";
        yield return inputPath;
        yield return "--config";
        yield return _configPath.Text;
        yield return "--output";
        yield return _outputRoot.Text;

        if (!string.IsNullOrWhiteSpace(previousPath))
        {
            yield return "--previous";
            yield return previousPath;
        }
    }

    private string? LatestReportFolder()
    {
        if (!Directory.Exists(_outputRoot.Text)) return null;

        try
        {
            return Directory.GetDirectories(_outputRoot.Text)
                .Where(x => File.Exists(Path.Combine(x, "index.html")) && Directory.Exists(Path.Combine(x, "raw")))
                .OrderByDescending(x => Path.GetFileName(x), StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppendLine($"WARNING: Could not inspect report folders: {ex.Message}");
            return null;
        }
    }

    private bool HasServer(string name) =>
        _servers.Items.Cast<object>().Any(item => string.Equals(item?.ToString(), name, StringComparison.OrdinalIgnoreCase));

    private void AppendLine(string text)
    {
        if (InvokeRequired) { BeginInvoke(() => AppendLine(text)); return; }
        _console.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}");
    }

    private void SetStatus(string text)
    {
        if (InvokeRequired) { BeginInvoke(() => SetStatus(text)); return; }
        _status.Text = "Status: " + text;
    }

    private static Color BadgeColor(string severity) => severity switch
    {
        "Critical" => Color.FromArgb(255, 210, 210),
        "High" => Color.FromArgb(255, 226, 190),
        "Medium" => Color.FromArgb(255, 245, 180),
        "Low" => Color.FromArgb(220, 238, 255),
        _ => Color.FromArgb(230, 230, 230)
    };

    private static string FormatArgumentForLog(string argument) =>
        argument.Contains(' ') ? $"\"{argument.Replace("\"", "\\\"")}\"" : argument;

    private static List<string> SplitCsv(string line)
    {
        var cells = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"' && quoted && i + 1 < line.Length && line[i + 1] == '"') { cell.Append('"'); i++; }
            else if (c == '"') quoted = !quoted;
            else if (c == ',' && !quoted) { cells.Add(cell.ToString()); cell.Clear(); }
            else cell.Append(c);
        }
        cells.Add(cell.ToString());
        return cells;
    }
}
