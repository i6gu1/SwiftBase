using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using SwiftBase.Core.Models;
using SwiftBase.Core.Security;
using SwiftBase.Core.Services;
using SwiftBase.Models;

namespace SwiftBase.ViewModels;

public enum PaletteMode
{
    Commands,
    Connections,
}

public sealed class TableGroup : ObservableObject
{
    public TableGroup(string name, params DbTable[] items)
    {
        Name = name;
        Items = new ObservableCollection<DbTable>(items);
        Items.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(CountLabel));
            OnPropertyChanged(nameof(HasItems));
        };
    }

    public string Name { get; }
    public ObservableCollection<DbTable> Items { get; }
    public bool HasItems => Items.Count > 0;
    public string CountLabel => Items.Count.ToString("N0");
}

/// <summary>Shell state: connections, explorer, tabs, palette and toasts.</summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly CredentialStore _store = new();
    private readonly DispatcherTimer _toastTimer;
    private DatabaseSession? _session;
    private ConnectionConfig? _activeConfig;
    private bool _isDemo;
    private int _queryCounter;
    private bool _suppressDatabaseChange;
    private CancellationTokenSource? _connectCts;

    private ConnectionProfile? _activeProfile;
    private bool _isConnected;
    private bool _isConnecting;
    private string _connectionError = "";
    private string _explorerSearch = "";
    private string _selectedDatabase = "";
    private TabViewModel? _selectedTab;
    private bool _isPaletteOpen;
    private PaletteMode _paletteMode = PaletteMode.Commands;
    private string _paletteQuery = "";
    private PaletteItem? _selectedPaletteItem;
    private string _toastMessage = "";
    private bool _toastIsError;
    private bool _isToastVisible;

    public MainViewModel()
    {
        Tabs = new ObservableCollection<TabViewModel>();
        Tables = new ObservableCollection<DbTable>();
        Databases = new ObservableCollection<string>();
        Connections = new ObservableCollection<ConnectionGroup>();
        TableGroups = new ObservableCollection<TableGroup>();
        PaletteResults = new ObservableCollection<PaletteItem>();

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4.2) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            IsToastVisible = false;
        };

        ReloadConnections();
        RebuildExplorerGroups();

        NewQueryCommand = new RelayCommand(() => NewQueryTab());
        DisconnectCommand = new RelayCommand(() => _ = DisconnectAsync());
        RefreshSchemaCommand = new RelayCommand(() => _ = RefreshSchemaAsync());
        OpenPaletteCommand = new RelayCommand(() => OpenPalette(PaletteMode.Commands));
        ClosePaletteCommand = new RelayCommand(ClosePalette);
        SaveSelectedCommand = new RelayCommand(SaveSelected);
    }

    public ObservableCollection<ConnectionGroup> Connections { get; }
    public ObservableCollection<DbTable> Tables { get; }
    public ObservableCollection<TableGroup> TableGroups { get; }
    public ObservableCollection<string> Databases { get; }
    public ObservableCollection<TabViewModel> Tabs { get; }
    public ObservableCollection<PaletteItem> PaletteResults { get; }

    public ConnectionProfile? ActiveProfile
    {
        get => _activeProfile;
        private set
        {
            if (Set(ref _activeProfile, value))
            {
                OnPropertyChanged(nameof(WindowTitle));
                OnPropertyChanged(nameof(ConnectionSubtitle));
            }
        }
    }

    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (Set(ref _isConnected, value))
            {
                OnPropertyChanged(nameof(WelcomeVisible));
                OnPropertyChanged(nameof(WorkspaceVisible));
                OnPropertyChanged(nameof(WindowTitle));
                OnPropertyChanged(nameof(WorkspaceStatus));
                OnPropertyChanged(nameof(CanSwitchDatabase));
            }
        }
    }

    public bool WelcomeVisible => !IsConnected;
    public bool WorkspaceVisible => IsConnected;
    public bool CanSwitchDatabase => IsConnected && !_isDemo;

    public bool IsConnecting
    {
        get => _isConnecting;
        private set => Set(ref _isConnecting, value);
    }

    public string ConnectionError
    {
        get => _connectionError;
        private set
        {
            if (Set(ref _connectionError, value))
            {
                OnPropertyChanged(nameof(HasConnectionError));
            }
        }
    }

    public bool HasConnectionError => !string.IsNullOrWhiteSpace(ConnectionError);

    public string ConnectionSubtitle => ActiveProfile is null
        ? "Choose a connection to begin"
        : $"{ActiveProfile.EngineName} · {SelectedDatabase}";

    public string WorkspaceStatus
    {
        get
        {
            if (!IsConnected)
            {
                return "Ready";
            }

            var tableCount = Tables.Count(t => t.Kind == TableKind.Table);
            var viewCount = Tables.Count(t => t.Kind == TableKind.View);
            return $"{tableCount} tables · {viewCount} views";
        }
    }

    public string ExplorerSearch
    {
        get => _explorerSearch;
        set
        {
            if (Set(ref _explorerSearch, value))
            {
                RebuildExplorerGroups();
            }
        }
    }

    public string SelectedDatabase
    {
        get => _selectedDatabase;
        set
        {
            if (!Set(ref _selectedDatabase, value))
            {
                return;
            }

            OnPropertyChanged(nameof(ConnectionSubtitle));
            if (!_suppressDatabaseChange && IsConnected && !_isDemo && value != _session?.ActiveDatabase)
            {
                if (HasPendingChanges)
                {
                    ShowToast("Save or discard pending changes before switching databases.", true);
                    _suppressDatabaseChange = true;
                    SelectedDatabase = _session?.ActiveDatabase ?? "";
                    _suppressDatabaseChange = false;
                    return;
                }

                _ = SwitchDatabaseAsync(value);
            }
        }
    }

    public TabViewModel? SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (Set(ref _selectedTab, value))
            {
                OnPropertyChanged(nameof(HasSelectedTab));
                OnPropertyChanged(nameof(SelectedTableTab));
                OnPropertyChanged(nameof(CanSaveSelected));
            }
        }
    }

    public bool HasSelectedTab => SelectedTab is not null;
    public TableTabViewModel? SelectedTableTab => SelectedTab as TableTabViewModel;
    public bool CanSaveSelected => SelectedTab is TableTabViewModel { HasPendingChanges: true, IsLoading: false };

    public bool IsPaletteOpen
    {
        get => _isPaletteOpen;
        private set => Set(ref _isPaletteOpen, value);
    }

    public PaletteMode PaletteMode
    {
        get => _paletteMode;
        private set
        {
            if (Set(ref _paletteMode, value))
            {
                OnPropertyChanged(nameof(PaletteTitle));
                OnPropertyChanged(nameof(PalettePlaceholder));
            }
        }
    }

    public string PaletteTitle => PaletteMode == PaletteMode.Connections ? "Switch connection" : "Command palette";
    public string PalettePlaceholder => PaletteMode == PaletteMode.Connections
        ? "Search saved connections…"
        : "Jump to a table or run a command…";

    public string PaletteQuery
    {
        get => _paletteQuery;
        set
        {
            if (Set(ref _paletteQuery, value))
            {
                RefreshPaletteResults();
            }
        }
    }

    public PaletteItem? SelectedPaletteItem
    {
        get => _selectedPaletteItem;
        set => Set(ref _selectedPaletteItem, value);
    }

    public string ToastMessage
    {
        get => _toastMessage;
        private set => Set(ref _toastMessage, value);
    }

    public bool ToastIsError
    {
        get => _toastIsError;
        private set => Set(ref _toastIsError, value);
    }

    public bool IsToastVisible
    {
        get => _isToastVisible;
        private set => Set(ref _isToastVisible, value);
    }

    public string WindowTitle => IsConnected && ActiveProfile is not null
        ? $"{ActiveProfile.Name} — SwiftBase"
        : "SwiftBase";

    public RelayCommand NewQueryCommand { get; }
    public RelayCommand DisconnectCommand { get; }
    public RelayCommand RefreshSchemaCommand { get; }
    public RelayCommand OpenPaletteCommand { get; }
    public RelayCommand ClosePaletteCommand { get; }
    public RelayCommand SaveSelectedCommand { get; }

    public event Action? ConnectionDialogRequested;
    public event Action? OpenSqliteFileRequested;
    public event Action<string>? PreviewSqlRequested;

    public void RequestConnectionDialog() => ConnectionDialogRequested?.Invoke();
    public void RequestOpenSqliteFile() => OpenSqliteFileRequested?.Invoke();

    public void ReloadConnections()
    {
        Connections.Clear();
        foreach (var group in SampleData.CreateConnections())
        {
            Connections.Add(group);
        }

        var saved = _store.Load()
            .Select(ConnectionProfile.FromConfig)
            .OrderByDescending(p => p.IsProduction)
            .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var vaultFailed = _store.LastLoadFailed;
        if (saved.Count > 0)
        {
            Connections.Add(new ConnectionGroup
            {
                Name = "Saved connections",
                Items = saved,
            });
        }

        if (vaultFailed)
        {
            ShowToast("Saved connections could not be read. The vault was not changed.", true);
        }
    }

    public IEnumerable<ConnectionProfile> AllConnections =>
        Connections.SelectMany(group => group.Items);

    public async Task ConnectAsync(ConnectionProfile profile)
    {
        if (IsConnecting)
        {
            return;
        }

        if (IsConnected && HasPendingChanges)
        {
            ShowToast("Save or discard pending changes before switching connections.", true);
            return;
        }

        _connectCts?.Cancel();
        _connectCts?.Dispose();
        _connectCts = new CancellationTokenSource();
        var token = _connectCts.Token;

        IsConnecting = true;
        ConnectionError = "";
        try
        {
            if (profile.IsDemo)
            {
                await OpenDemoAsync(profile);
                return;
            }

            if (profile.Config is null)
            {
                throw new InvalidOperationException("This connection has no saved configuration.");
            }

            await OpenLiveAsync(profile, profile.Config.Database, token);
        }
        catch (OperationCanceledException)
        {
            // Superseded by another connection attempt.
        }
        catch (Exception ex)
        {
            CloseAllTabs();
            if (_session is not null)
            {
                await _session.DisposeAsync();
                _session = null;
            }

            ResetWorkspace();
            ConnectionError = ex.GetBaseException().Message;
            ShowToast("Connection failed: " + ConnectionError, true);
        }
        finally
        {
            IsConnecting = false;
        }
    }

    public async Task RegisterConnectionAsync(ConnectionConfig config)
    {
        try
        {
            _store.Upsert(config);
            ReloadConnections();
        }
        catch (Exception ex)
        {
            ShowToast("Could not save the connection: " + ex.GetBaseException().Message, true);
            return;
        }

        var profile = AllConnections.FirstOrDefault(p =>
            p.Config is not null && p.Config.Id == config.Id) ?? ConnectionProfile.FromConfig(config);
        await ConnectAsync(profile);
    }

    public async Task OpenSqliteFileAsync(string path)
    {
        var config = new ConnectionConfig
        {
            Name = Path.GetFileNameWithoutExtension(path),
            Engine = DatabaseEngine.Sqlite,
            Database = path,
            Environment = "Local",
            Host = "",
        };

        await RegisterConnectionAsync(config);
    }

    public void DeleteConnection(ConnectionProfile profile)
    {
        if (profile.Config is null || profile.IsDemo)
        {
            return;
        }

        var wasActive = ActiveProfile?.Id == profile.Id;
        if (wasActive && HasPendingChanges)
        {
            ShowToast("Save or discard pending changes before deleting the active connection.", true);
            return;
        }

        _store.Remove(profile.Config.Id);
        ReloadConnections();
        ShowToast("Connection removed");
        if (wasActive)
        {
            _ = DisconnectAsync();
        }
    }

    public async Task DisconnectAsync(bool force = false)
    {
        if (!force && HasPendingChanges)
        {
            ShowToast("Save or discard pending changes before disconnecting.", true);
            return;
        }

        CloseAllTabs();
        await DisposeSessionAsync();
        ResetWorkspace();
        ShowToast("Disconnected");
    }

    /// <summary>Synchronous cleanup for window shutdown, after the close prompt.</summary>
    public void DisconnectForShutdown()
    {
        CloseAllTabs();
        if (_session is not null)
        {
            _session.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _session = null;
        }

        ResetWorkspace();
    }

    public async Task RefreshSchemaAsync()
    {
        if (!IsConnected)
        {
            return;
        }

        if (_isDemo)
        {
            RefreshDemoSchema();
            return;
        }

        if (_session is null)
        {
            return;
        }

        try
        {
            var infos = await _session.Provider.GetSchemaAsync(_session.ActiveDatabase);
            ApplySchema(infos);
            ShowToast($"Schema refreshed · {Tables.Count:N0} objects");
        }
        catch (Exception ex)
        {
            ShowToast("Schema refresh failed: " + ex.GetBaseException().Message, true);
        }
    }

    public void OpenTable(DbTable table)
    {
        if (!IsConnected)
        {
            return;
        }

        if (table.Kind == TableKind.Function)
        {
            NewQueryTab($"select *\nfrom {table.QualifiedName}();");
            return;
        }

        var existing = Tabs.OfType<TableTabViewModel>().FirstOrDefault(t =>
            string.Equals(t.Table.Name, table.Name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(t.Table.Schema, table.Schema, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            SelectedTab = existing;
            return;
        }

        var tab = new TableTabViewModel(table, _session, SelectedDatabase, CloseTab);
        tab.PreviewRequested += sql => PreviewSqlRequested?.Invoke(sql);
        AddTab(tab);
    }

    public QueryTabViewModel NewQueryTab(string? sql = null)
    {
        _queryCounter++;
        var tab = new QueryTabViewModel(
            _session,
            _isDemo ? "demo" : SelectedDatabase,
            Tables.ToList(),
            _queryCounter,
            CloseTab);

        if (!string.IsNullOrWhiteSpace(sql))
        {
            tab.EditorText = sql;
        }

        AddTab(tab);
        return tab;
    }

    public void CloseTab(TabViewModel tab)
    {
        if (tab is TableTabViewModel { HasPendingChanges: true, IsLoading: false })
        {
            ShowToast("Save or discard pending changes before closing the tab.", true);
            return;
        }

        var index = Tabs.IndexOf(tab);
        tab.OnClosed();
        Tabs.Remove(tab);
        if (ReferenceEquals(SelectedTab, tab))
        {
            SelectedTab = Tabs.Count == 0 ? null : Tabs[Math.Clamp(index, 0, Tabs.Count - 1)];
        }

        OnPropertyChanged(nameof(HasSelectedTab));
        OnPropertyChanged(nameof(CanSaveSelected));
    }

    public void CloseAllTabs()
    {
        foreach (var tab in Tabs)
        {
            tab.OnClosed();
        }

        Tabs.Clear();
        SelectedTab = null;
    }

    public bool HasPendingChanges => Tabs.Any(tab => tab.HasPendingChanges);

    public void SaveSelected()
    {
        if (SelectedTab is TableTabViewModel table && table.SaveCommand.CanExecute(null))
        {
            table.SaveCommand.Execute(null);
        }
    }

    public void OpenPalette(PaletteMode mode)
    {
        PaletteMode = mode;
        PaletteQuery = "";
        IsPaletteOpen = true;
        RefreshPaletteResults();
    }

    public void ClosePalette()
    {
        IsPaletteOpen = false;
        PaletteQuery = "";
        PaletteResults.Clear();
    }

    public void MovePaletteSelection(int delta)
    {
        if (PaletteResults.Count == 0)
        {
            return;
        }

        var index = SelectedPaletteItem is null ? -1 : PaletteResults.IndexOf(SelectedPaletteItem);
        index = Math.Clamp(index + delta, 0, PaletteResults.Count - 1);
        SelectedPaletteItem = PaletteResults[index];
    }

    public async Task ExecutePaletteItemAsync(PaletteItem item)
    {
        ClosePalette();
        switch (item.Kind)
        {
            case PaletteKind.Connection when item.Connection is not null:
                await ConnectAsync(item.Connection);
                break;

            case PaletteKind.Table when item.Table is not null:
                OpenTable(item.Table);
                break;

            case PaletteKind.Action:
                switch (item.ActionKey)
                {
                    case "new-query":
                        NewQueryTab();
                        break;
                    case "refresh":
                        await RefreshSchemaAsync();
                        break;
                    case "disconnect":
                        await DisconnectAsync();
                        break;
                    case "new-connection":
                        ConnectionDialogRequested?.Invoke();
                        break;
                    case "open-sqlite":
                        OpenSqliteFileRequested?.Invoke();
                        break;
                    case "save":
                        SaveSelected();
                        break;
                }

                break;
        }
    }

    public void ShowToast(string message, bool isError = false)
    {
        ToastMessage = message;
        ToastIsError = isError;
        IsToastVisible = true;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private async Task DisposeSessionAsync()
    {
        if (_session is null)
        {
            return;
        }

        await _session.DisposeAsync();
        _session = null;
    }

    private void ResetWorkspace()
    {
        _activeConfig = null;
        SetDemoMode(false);
        IsConnected = false;
        ActiveProfile = null;
        ConnectionError = "";
        Tables.Clear();
        Databases.Clear();

        _suppressDatabaseChange = true;
        SelectedDatabase = "";
        _suppressDatabaseChange = false;
        RebuildExplorerGroups();
    }

    private void SetDemoMode(bool value)
    {
        if (_isDemo == value)
        {
            return;
        }

        _isDemo = value;
        OnPropertyChanged(nameof(CanSwitchDatabase));
    }

    private void RefreshDemoSchema()
    {
        Tables.Clear();
        foreach (var table in SampleData.CreateTables())
        {
            Tables.Add(table);
        }

        RebuildExplorerGroups();
        OnPropertyChanged(nameof(WorkspaceStatus));
        ShowToast($"Schema refreshed · {Tables.Count:N0} objects");
    }

    private async Task OpenDemoAsync(ConnectionProfile profile)
    {
        await DisposeSessionAsync();

        _activeConfig = null;
        SetDemoMode(true);
        CloseAllTabs();

        Tables.Clear();
        foreach (var table in SampleData.CreateTables())
        {
            Tables.Add(table);
        }

        Databases.Clear();
        if (profile.Engine == DbEngine.Sqlite)
        {
            foreach (var database in new[] { "main", "analytics", "shop" })
            {
                Databases.Add(database);
            }
        }
        else
        {
            foreach (var database in new[] { "analytics", "shop", "shop_prod" })
            {
                Databases.Add(database);
            }

            if (!Databases.Contains(profile.Database, StringComparer.OrdinalIgnoreCase))
            {
                Databases.Add(profile.Database);
            }
        }

        _suppressDatabaseChange = true;
        SelectedDatabase = profile.Engine == DbEngine.Sqlite ? "main" : profile.Database;
        _suppressDatabaseChange = false;

        ActiveProfile = profile;
        RebuildExplorerGroups();
        IsConnected = true;
        OnPropertyChanged(nameof(WorkspaceStatus));
        ShowToast($"{profile.Name} · demo workspace loaded");
    }

    private async Task OpenLiveAsync(ConnectionProfile profile, string? database, CancellationToken cancellationToken)
    {
        if (profile.Config is null)
        {
            throw new InvalidOperationException("This connection has no saved configuration.");
        }

        SetDemoMode(false);
        ConnectionError = "";

        var newSession = await DatabaseSession.OpenAsync(profile.Config, database, cancellationToken);
        try
        {
            var databases = await newSession.Provider.GetDatabasesAsync(cancellationToken);
            var activeDatabase = string.IsNullOrWhiteSpace(newSession.ActiveDatabase)
                ? databases.FirstOrDefault() ?? ""
                : newSession.ActiveDatabase;

            var infos = await newSession.Provider.GetSchemaAsync(activeDatabase, cancellationToken);

            var oldSession = _session;
            _session = newSession;
            _activeConfig = profile.Config;
            _activeConfig.Database = activeDatabase;
            CloseAllTabs();

            Databases.Clear();
            foreach (var db in databases)
            {
                Databases.Add(db);
            }

            _suppressDatabaseChange = true;
            SelectedDatabase = activeDatabase;
            _suppressDatabaseChange = false;

            ActiveProfile = profile;
            ApplySchema(infos);
            IsConnected = true;
            OnPropertyChanged(nameof(WorkspaceStatus));
            ShowToast($"Connected to {profile.Name} in {newSession.Provider.EngineName}");

            if (oldSession is not null)
            {
                await oldSession.DisposeAsync();
            }
        }
        catch
        {
            await newSession.DisposeAsync();
            throw;
        }
    }

    private void ApplySchema(IReadOnlyList<DbTableInfo> infos)
    {
        Tables.Clear();
        foreach (var info in infos
                     .OrderBy(i => i.Kind)
                     .ThenBy(i => i.Schema, StringComparer.CurrentCultureIgnoreCase)
                     .ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            Tables.Add(DbTable.FromInfo(info));
        }

        RebuildExplorerGroups();
    }

    private async Task SwitchDatabaseAsync(string database)
    {
        if (_session is null || _activeConfig is null || string.IsNullOrWhiteSpace(database) || database == _session.ActiveDatabase)
        {
            return;
        }

        var previousDatabase = _session.ActiveDatabase;
        try
        {
            var newSession = await DatabaseSession.OpenAsync(_activeConfig, database);
            var infos = await newSession.Provider.GetSchemaAsync(database);
            var oldSession = _session;
            _session = newSession;
            _activeConfig.Database = database;
            CloseAllTabs();
            ApplySchema(infos);
            await oldSession.DisposeAsync();
            ShowToast($"Switched to database {database}");
        }
        catch (Exception ex)
        {
            _suppressDatabaseChange = true;
            SelectedDatabase = previousDatabase;
            _suppressDatabaseChange = false;
            ShowToast("Could not switch database: " + ex.GetBaseException().Message, true);
        }
    }

    private void AddTab(TabViewModel tab)
    {
        Tabs.Add(tab);
        SelectedTab = tab;
    }

    private void RefreshPaletteResults()
    {
        var current = SelectedPaletteItem;
        PaletteResults.Clear();

        if (PaletteMode == PaletteMode.Connections)
        {
            foreach (var profile in AllConnections)
            {
                PaletteResults.Add(new PaletteItem
                {
                    Title = profile.Name,
                    Subtitle = $"{profile.EngineName} · {profile.Subtitle} · {profile.Environment}",
                    Glyph = profile.Badge,
                    Kind = PaletteKind.Connection,
                    Connection = profile,
                });
            }
        }
        else
        {
            foreach (var table in Tables)
            {
                PaletteResults.Add(new PaletteItem
                {
                    Title = table.Name,
                    Subtitle = $"{table.KindLabel} · {table.Schema} · {table.Columns.Count} columns",
                    Glyph = table.Glyph,
                    Kind = PaletteKind.Table,
                    Table = table,
                });
            }

            if (IsConnected)
            {
                PaletteResults.Add(new PaletteItem { Title = "New SQL query", Subtitle = "Ctrl+T · open a query tab", Glyph = "SQL", Kind = PaletteKind.Action, ActionKey = "new-query" });
                PaletteResults.Add(new PaletteItem { Title = "Refresh schema", Subtitle = "F5 · reload tables and views", Glyph = "⟳", Kind = PaletteKind.Action, ActionKey = "refresh" });
                if (CanSaveSelected)
                {
                    PaletteResults.Add(new PaletteItem { Title = "Save pending changes", Subtitle = "Ctrl+S · commit in one transaction", Glyph = "↓", Kind = PaletteKind.Action, ActionKey = "save" });
                }

                PaletteResults.Add(new PaletteItem { Title = "Disconnect", Subtitle = "Close the current connection", Glyph = "⏻", Kind = PaletteKind.Action, ActionKey = "disconnect" });
            }

            PaletteResults.Add(new PaletteItem { Title = "New connection…", Subtitle = "Add a SQLite, PostgreSQL or MySQL connection", Glyph = "+", Kind = PaletteKind.Action, ActionKey = "new-connection" });
            PaletteResults.Add(new PaletteItem { Title = "Open SQLite file…", Subtitle = "Browse for a .sqlite or .db file", Glyph = "◇", Kind = PaletteKind.Action, ActionKey = "open-sqlite" });
        }

        var query = PaletteQuery.Trim();
        if (query.Length > 0)
        {
            var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var filtered = PaletteResults
                .Where(item => terms.All(term => item.SearchText.Contains(term, StringComparison.CurrentCultureIgnoreCase)))
                .ToList();
            PaletteResults.Clear();
            foreach (var item in filtered)
            {
                PaletteResults.Add(item);
            }
        }

        SelectedPaletteItem = PaletteResults.Contains(current!) ? current : PaletteResults.FirstOrDefault();
    }

    private void RebuildExplorerGroups()
    {
        var search = ExplorerSearch.Trim();
        IEnumerable<DbTable> Filter(IEnumerable<DbTable> source) => string.IsNullOrWhiteSpace(search)
            ? source
            : source.Where(t => t.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase));

        TableGroups.Clear();
        AddGroup("TABLES", Filter(Tables.Where(t => t.Kind == TableKind.Table)));
        AddGroup("VIEWS", Filter(Tables.Where(t => t.Kind == TableKind.View)));
        AddGroup("FUNCTIONS", Filter(Tables.Where(t => t.Kind == TableKind.Function)));
        OnPropertyChanged(nameof(WorkspaceStatus));
    }

    private void AddGroup(string name, IEnumerable<DbTable> source)
    {
        var items = source.ToList();
        if (items.Count > 0 || string.IsNullOrWhiteSpace(ExplorerSearch))
        {
            TableGroups.Add(new TableGroup(name, items.ToArray()));
        }
    }
}
