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
        set
        {
            if (Set(ref _selectedSave, value))
                Raise(nameof(CanOpenBackups));
        }
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

    internal void RebuildTree() =>
        Tree = TreeBuilder.Build(Objects, ShowAll, Filter, PendingInventories, TreeBuilder.ExpandedKeys(Tree));

    IReadOnlyList<InventoryView> _selected = [];

    /// <summary>Shows these inventories in the pane: one slot, or every inventory of a block.</summary>
    public void Select(IReadOnlyList<InventoryView> inventories)
    {
        _selected = inventories;
        RefreshPane();
    }

    bool _hasWorld;
    public bool HasWorld
    {
        get => _hasWorld;
        private set
        {
            if (Set(ref _hasWorld, value))
                RaiseSave();
        }
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
        private set
        {
            if (Set(ref _busy, value))
                RaiseSave();
        }
    }

    bool _gameRunning;
    public bool GameRunning
    {
        get => _gameRunning;
        private set
        {
            if (!Set(ref _gameRunning, value))
                return;
            Raise(nameof(GameState));
            Raise(nameof(CanStartGame));
            RaiseSave();
        }
    }

    public string GameState => GameRunning ? "game running" : "game not running";

    int _pendingCount;
    public int PendingCount
    {
        get => _pendingCount;
        internal set
        {
            if (Set(ref _pendingCount, value))
                RaiseSave();
        }
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

    public void ShowEditError(string message) => EditError = message;

    public void ShowStatus(string message) => Status = message;

    public void RefreshGameRunning() => GameRunning = GameRunningCheck();

    public async Task LoadAsync(SaveInfo save)
    {
        Busy = true;
        Error = null;
        var watch = Stopwatch.StartNew();
        try
        {
            Status = "Reading save";
            Select([]);
            var (file, archive) = await Worker.Run(() =>
            {
                _world = null;
                var file = new FileInfo(save.File);
                file.Refresh();
                return (file, SaveArchive.Load(save.File));
            });
            Status = "Reading game content";
            var loaded = await Worker.Run(() =>
            {
                _content ??= GameContent.Load(GameInstall.RootDir);
                _world = World.From(save.File, archive, _content, file);
                _plan = new EditPlan(_world);
                var objects = Snapshot.Of(_world);
                var stateful = _content.Items.Items.Where(i => _world.HasState(i.Guid)).Select(i => i.Guid).ToHashSet();
                var states = new Dictionary<Guid, StateInfo>();
                foreach (var item in stateful)
                {
                    try
                    {
                        var (codec, template) = _plan.ValueCodec(item);
                        states[item] = new StateInfo(codec.Max(template), codec.WholeNumbers, Percent: !codec.WholeNumbers);
                    }
                    catch (Exception e) when (e is NotSupportedException or ArgumentException)
                    {
                        // no codec: New and Copy only
                    }
                }
                return (objects, Catalog: _content.Items.Items.OrderBy(i => i.Name).ToList(), stateful, states);
            });
            Catalog = loaded.Catalog;
            _catalogByGuid = null;
            StatefulItems = loaded.stateful;
            States = loaded.states;
            SetEdits([], [], loaded.objects);
            Objects = loaded.objects;
            HasWorld = true;
            LoadedSave = save;
            Status = $"Loaded in {watch.Elapsed.TotalSeconds:0.0}s";
        }
        catch (Exception e)
        {
            SetEdits([], [], []);
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

    // ---- editing ----

    EditPlan? _plan; // worker thread

    public IReadOnlyList<ItemInfo> Catalog { get; private set; } = [];
    public ISet<Guid> StatefulItems { get; private set; } = new HashSet<Guid>();
    public IReadOnlyDictionary<Guid, StateInfo> States { get; private set; } = new Dictionary<Guid, StateInfo>();

    IReadOnlyList<Edit> _edits = [];
    public IReadOnlyList<Edit> Edits
    {
        get => _edits;
        private set => Set(ref _edits, value);
    }

    IReadOnlyList<Override> _overrides = [];
    public IReadOnlyList<Override> Overrides
    {
        get => _overrides;
        private set => Set(ref _overrides, value);
    }

    IReadOnlyList<ObjectView> _previewObjects = [];

    IReadOnlyList<InventoryViewModel> _sections = [];

    /// <summary>The selected inventories as they will be after the pending edits, one section each.</summary>
    public IReadOnlyList<InventoryViewModel> Sections
    {
        get => _sections;
        private set
        {
            if (Set(ref _sections, value))
                Raise(nameof(HasPane));
        }
    }

    public bool HasPane => Sections.Count > 0;

    /// <summary>Section headings and rows in one list, for the pane's single list box.</summary>
    public ObservableCollection<object> PaneEntries { get; } = [];

    /// <summary>The picked rows as copy text, in the order the pane shows them.</summary>
    public string CopyRows(IEnumerable<RowViewModel> picked)
    {
        var set = picked.ToList();
        return string.Join(Environment.NewLine, Sections.Select(s => s.CopyRows(set)).Where(t => t.Length > 0));
    }

    string? _editError;
    public string? EditError
    {
        get => _editError;
        private set => Set(ref _editError, value);
    }

    void SetEdits(IReadOnlyList<Edit> edits, IReadOnlyList<Override> overrides, IReadOnlyList<ObjectView> preview)
    {
        Edits = edits;
        Overrides = overrides;
        _previewObjects = preview;
        PendingCount = edits.Count;
        PendingInventories = edits.Select(e => e.Inventory).ToHashSet();
        RebuildTree();
        RefreshPane();
    }

    void RefreshPane()
    {
        var previous = Sections;
        Sections = _selected.Select(loaded =>
        {
            var preview = _previewObjects.SelectMany(o => o.Inventories).FirstOrDefault(i => i.Index == loaded.Index) ?? loaded;
            return InventoryViewModel.Build(loaded, preview, Edits, Overrides, ItemName, States, previous.FirstOrDefault(p => p.Index == loaded.Index), ItemOf);
        }).ToList();
        PaneItems.Sync(PaneEntries, PaneItems.Layout(Sections));
    }

    public string ItemName(Guid item) => ItemOf(item)?.Name ?? item.ToString();

    Dictionary<Guid, ItemInfo>? _catalogByGuid;

    public ItemInfo? ItemOf(Guid item) =>
        (_catalogByGuid ??= Catalog.ToDictionary(i => i.Guid)).GetValueOrDefault(item);

    /// <summary>The item's total in an inventory including pending edits.</summary>
    public decimal CurrentTotal(int inventory, Guid item) =>
        Edits.OfType<SetTotal>().FirstOrDefault(s => s.Inventory == inventory && s.Item == item)?.Amount
        ?? Objects.SelectMany(o => o.Inventories).First(i => i.Index == inventory).Rows.Where(r => r.Item == item).Sum(r => r.Amount);

    /// <summary>Stacks of an item in the loaded save, to copy their state.</summary>
    public IEnumerable<(InventoryView Inventory, RowView Row)> StacksOf(Guid item) =>
        Objects.SelectMany(o => o.Inventories).SelectMany(i => i.Rows.Where(r => r.Item == item && r.Stack != null).Select(r => (i, r)));

    /// <summary>Whether the slot takes the item, and the slot of the same block that does when it doesn't.</summary>
    public Task<(bool? Accepts, int? Other, string Message)> CheckSlotAsync(int inventory, Guid item) => Worker.Run(() =>
    {
        var info = _content!.Items.Find(item)!;
        return (_world!.Accepts(inventory, info), _world.SlotThatTakes(inventory, info), _world.Rejection(inventory, info));
    });

    public Task ApplyAsync(Edit edit) => Change(plan => plan.Apply(edit));

    public Task RevertAsync(Edit edit) => Change(plan => plan.Revert(edit));

    public Task DiscardAsync() => Change(plan => plan.Clear());

    async Task Change(Action<EditPlan> change)
    {
        if (Busy)
        {
            EditError = "Wait until saving or loading is done.";
            return;
        }
        EditError = null;
        try
        {
            Edits = await Worker.Run(() =>
            {
                change(_plan!);
                return _plan!.Edits.ToList();
            });
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException)
        {
            EditError = e.Message;
            return;
        }
        PendingCount = Edits.Count;
        PendingInventories = Edits.Select(e => e.Inventory).ToHashSet();
        RefreshPane();
        await SchedulePreview();
    }

    CancellationTokenSource? _previewDelay;

    // previews take about half a second, so quick edits share one
    async Task SchedulePreview()
    {
        _previewDelay?.Cancel();
        var delay = _previewDelay = new CancellationTokenSource();
        try
        {
            await Task.Delay(300, delay.Token);
        }
        catch (TaskCanceledException)
        {
            return;
        }
        Status = "Updating preview";
        var (edits, overrides, preview) = await Worker.Run(() =>
        {
            var world = _plan!.Preview();
            return (_plan.Edits.ToList(), _plan.Overrides(world), Snapshot.Of(world));
        });
        if (delay.IsCancellationRequested)
            return;
        SetEdits(edits, overrides, preview);
        Status = edits.Count == 0 ? "No pending changes" : $"{edits.Count} pending";
    }

    // ---- saving ----

    internal BackupStore Backups { get; init; } = BackupStore.Default;

    internal Func<bool> GameRunningCheck { get; init; } = SaveWriter.GameRunning;

    /// <summary>Backups belong to the picked save, so a save that fails to load can still be restored.</summary>
    public bool CanOpenBackups => SelectedSave != null;

    public bool CanStartGame => !GameRunning;

    bool _saving;

    /// <summary>The save the world and pending edits belong to, which can differ from the picker while switching.</summary>
    public SaveInfo? LoadedSave { get; private set; }

    // a running game doesn't disable Save, the save prompt explains it and asks once
    public bool CanSave => HasWorld && PendingCount > 0 && !Busy;

    public string SaveLabel => PendingCount > 0 ? $"Save ({PendingCount})" : "Save";

    void RaiseSave()
    {
        Raise(nameof(CanSave));
        Raise(nameof(SaveLabel));
    }

    public static SaveDecision Decide(bool gameRunning, bool changedOnDisk, int overrides, bool writeAnyway, bool allowRunningGame) =>
        gameRunning && !allowRunningGame ? new SaveDecision.GameRunning()
        : changedOnDisk ? new SaveDecision.Reload()
        : overrides > 0 && !writeAnyway ? new SaveDecision.Confirm()
        : new SaveDecision.Write();

    /// <summary>What saving would do now, with the rule-breaking edits freshly computed (a preview may still be pending).</summary>
    public Task<(SaveDecision Decision, IReadOnlyList<Override> Overrides)> CheckSaveAsync(bool writeAnyway, bool allowRunningGame = false) => Worker.Run(() =>
    {
        var overrides = _plan!.Overrides(_plan.Preview());
        return (Decide(GameRunningCheck(), _world!.ChangedOnDisk(), overrides.Count, writeAnyway, allowRunningGame), overrides);
    });

    /// <summary>Builds the edited save, checks it against the edits, writes it with a backup and reloads it.</summary>
    public async Task<string> WriteAsync(bool writeAnyway, bool allowRunningGame = false)
    {
        if (_saving)
            throw new InvalidOperationException("Drydock is already saving.");
        _saving = true;
        Busy = true;
        try
        {
            Status = "Writing save";
            var backup = await Worker.Run(() =>
            {
                // decided in the same job that writes, so nothing can change in between
                var overrides = _plan!.Overrides(_plan.Preview());
                bool running = GameRunningCheck();
                switch (Decide(running, _world!.ChangedOnDisk(), overrides.Count, writeAnyway, allowRunningGame))
                {
                    case SaveDecision.GameRunning:
                        throw new InvalidOperationException(SaveWriter.GameRunningReason);
                    case SaveDecision.Reload:
                        throw new InvalidOperationException("The game saved this world since Drydock read it. Nothing was written.");
                    case SaveDecision.Confirm:
                        throw new InvalidOperationException("Some changes go past what the game allows. Nothing was written.");
                }
                var built = _plan.Replay().Build();
                if (_plan.Check(_world!.WithArchive(built)) is { } problem)
                    throw new InvalidOperationException($"Nothing was written. {problem}");
                return SaveWriter.Write(built, _world.Path, Backups, allowRunningGame || !running);
            });
            await LoadAsync(LoadedSave!);
            Status = "Saved, backup made";
            return backup;
        }
        finally
        {
            _saving = false;
            Busy = false;
        }
    }

    /// <summary>Puts a backup back and loads the save. Pending edits are dropped.</summary>
    public async Task RestoreAsync(Backup backup, SaveInfo save, bool allowRunningGame = false)
    {
        var saveDir = Path.GetDirectoryName(save.File)!;
        await Worker.Run(() => Backups.Restore(backup, saveDir, allowRunningGame));
        await LoadAsync(save);
    }

    public void Dispose() => Worker.Dispose();
}

public abstract record SaveDecision
{
    /// <summary>The game runs. Writing is only safe if it's at the main menu or has another world loaded, which the user must confirm.</summary>
    public sealed record GameRunning : SaveDecision;

    /// <summary>The game saved over the file since it was loaded. Writing would lose that.</summary>
    public sealed record Reload : SaveDecision;

    /// <summary>Some edits break game rules and need "write anyway".</summary>
    public sealed record Confirm : SaveDecision;

    public sealed record Write : SaveDecision;
}
