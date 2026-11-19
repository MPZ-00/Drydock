using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using Drydock.Core;

namespace Drydock.App.ViewModels;

/// <summary>The main window's state. Everything touching the save runs on <see cref="Worker"/>, the rest only sees snapshots.</summary>
public sealed class MainViewModel : ObservableObject, IDisposable
{
    readonly string? _settingsUser, _settingsPortable, _savesRoot;
    Settings _settings;

    // worker-thread state
    GameContent? _content;
    World? _world;

    public MainViewModel(Settings settings, string? settingsUser = null, string? settingsPortable = null, string? savesRoot = null)
    {
        _settings = settings;
        _settingsUser = settingsUser;
        _settingsPortable = settingsPortable;
        _savesRoot = savesRoot;
    }

    public WorkerThread Worker { get; } = new("Drydock save worker");

    public ObservableCollection<SaveInfo> Saves { get; } = [];

    SaveInfo? _selectedSave;
    public SaveInfo? SelectedSave
    {
        get => _selectedSave;
        set => Set(ref _selectedSave, value);
    }

    IReadOnlyList<ObjectView> _objects = [];
    public IReadOnlyList<ObjectView> Objects
    {
        get => _objects;
        private set
        {
            if (Set(ref _objects, value))
                RebuildTree();
        }
    }

    string _filter = "";
    public string Filter
    {
        get => _filter;
        set
        {
            if (Set(ref _filter, value))
                RebuildTree();
        }
    }

    bool _showAll;
    public bool ShowAll
    {
        get => _showAll;
        set
        {
            if (Set(ref _showAll, value))
                RebuildTree();
        }
    }

    IReadOnlyList<TreeNode> _tree = [];
    public IReadOnlyList<TreeNode> Tree
    {
        get => _tree;
        private set => Set(ref _tree, value);
    }

    /// <summary>Inventories with pending edits, marked in the tree.</summary>
    internal ISet<int> PendingInventories { get; set; } = new HashSet<int>();

    internal void RebuildTree() => Tree = TreeBuilder.Build(Objects, ShowAll, Filter, PendingInventories);

    InventoryView? _selectedInventory;
    public InventoryView? SelectedInventory
    {
        get => _selectedInventory;
        set => Set(ref _selectedInventory, value);
    }

    bool _hasWorld;
    public bool HasWorld
    {
        get => _hasWorld;
        private set => Set(ref _hasWorld, value);
    }

    string _status = "";
    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    string? _error;
    public string? Error
    {
        get => _error;
        private set => Set(ref _error, value);
    }

    bool _busy;
    public bool Busy
    {
        get => _busy;
        private set => Set(ref _busy, value);
    }

    bool _gameRunning;
    public bool GameRunning
    {
        get => _gameRunning;
        private set
        {
            if (Set(ref _gameRunning, value))
                Raise(nameof(GameState));
        }
    }

    public string GameState => GameRunning ? "game running" : "game not running";

    int _pendingCount;
    public int PendingCount
    {
        get => _pendingCount;
        internal set => Set(ref _pendingCount, value);
    }

    public string Theme
    {
        get => _settings.Theme;
        set
        {
            if (value == _settings.Theme)
                return;
            _settings = _settings with { Theme = value };
            _settings.Save(_settingsUser, _settingsPortable);
            Raise();
            ThemeChanged?.Invoke(value);
        }
    }

    public event Action<string>? ThemeChanged;

    public void RefreshSaves()
    {
        Saves.Clear();
        foreach (var save in Core.Saves.List(_savesRoot))
            Saves.Add(save);
    }

    public void ShowError(string message) => Error = message;

    public void RefreshGameRunning() => GameRunning = SaveWriter.GameRunning();

    public async Task LoadAsync(SaveInfo save)
    {
        Busy = true;
        Error = null;
        var watch = Stopwatch.StartNew();
        try
        {
            Status = "Reading save";
            var (file, archive) = await Worker.Run(() =>
            {
                _world = null;
                var file = new FileInfo(save.File);
                file.Refresh();
                return (file, SaveArchive.Load(save.File));
            });
            Status = "Reading game content";
            Objects = await Worker.Run(() =>
            {
                _content ??= GameContent.Load(GameInstall.RootDir);
                _world = World.From(save.File, archive, _content, file);
                return Snapshot.Of(_world);
            });
            HasWorld = true;
            Status = $"Loaded in {watch.Elapsed.TotalSeconds:0.0}s";
        }
        catch (Exception e)
        {
            Objects = [];
            HasWorld = false;
            Error = $"{save.Name} can't be read: {e.Message}";
            Status = "Load failed";
        }
        finally
        {
            Busy = false;
        }
    }

    public void Dispose() => Worker.Dispose();
}
