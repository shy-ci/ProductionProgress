using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Menus;
using StardewValley.TerrainFeatures;
using MossHarbor.ProductionProgress.Core;
using MossHarbor.ProductionProgress.Menus;
using MossHarbor.ProductionProgress.Tracking;

namespace MossHarbor.ProductionProgress;

public sealed class ModEntry : Mod
{
    private ModConfig config = null!;
    private ProductionTracker? tracker;
    private readonly RemoteView remote = new();
    private PerScreen<MenuIntegration> menus = null!;
    private readonly Queue<(ChangePacket Packet, long[] Players)> outgoing = new();
    private readonly HashSet<long> requestedSnapshots = new();
    private string session = "";
    private long sequence;
    private long cachedRevision = -1;
    private List<ProgressRow> cachedRows = new();
    private bool lastInitializing;
    private int ticks;
    private bool dayTransition;

    public override void Entry(IModHelper helper)
    {
        config = helper.ReadConfig<ModConfig>();
        if (!config.Enabled) return;
        menus = new PerScreen<MenuIntegration>(() => new MenuIntegration(helper, Rows, Revision, Status, RequestSnapshot, Monitor));
        helper.Events.GameLoop.SaveLoaded += SaveLoaded;
        helper.Events.GameLoop.DayEnding += (_, _) =>
        { if (Context.IsMainPlayer) { dayTransition = true; tracker?.BeginOvernight(); } };
        helper.Events.GameLoop.DayStarted += (_, _) => { if (Context.IsMainPlayer) { dayTransition = false; tracker?.NewDay(); } };
        helper.Events.GameLoop.TimeChanged += (_, _) => { if (Context.IsMainPlayer) tracker?.SetClock(); };
        helper.Events.GameLoop.UpdateTicking += (_, _) => { if (Context.IsWorldReady) menus.Value.BeforeUpdate(); };
        helper.Events.GameLoop.UpdateTicked += Update;
        helper.Events.GameLoop.Saving += (_, _) =>
        {
            if (!Context.IsMainPlayer || tracker == null) return;
            helper.Data.WriteSaveData("cycle-totals", tracker.CaptureCycles());
            helper.Data.WriteSaveData("completion-dates", tracker.CaptureCompletions(dayTransition));
        };
        helper.Events.GameLoop.ReturnedToTitle += (_, _) =>
        {
            menus.Value.Detach();
            if (Context.ScreenId == 0) { tracker?.Dispose(); tracker = null; remote.Clear(); outgoing.Clear(); requestedSnapshots.Clear(); cachedRows.Clear(); cachedRevision = -1; }
        };
        helper.Events.World.ObjectListChanged += (_, e) =>
        {
            if (!Context.IsMainPlayer || tracker == null) return;
            foreach (var pair in e.Removed) tracker.QueueObject(e.Location, pair.Key);
            foreach (var pair in e.Added) tracker.QueueObject(e.Location, pair.Key);
        };
        helper.Events.World.TerrainFeatureListChanged += (_, e) =>
        {
            if (!Context.IsMainPlayer || tracker == null) return;
            foreach (var pair in e.Removed) if (pair.Value is HoeDirt) tracker.QueueSoil(e.Location, pair.Key);
            foreach (var pair in e.Added) if (pair.Value is HoeDirt) tracker.QueueSoil(e.Location, pair.Key);
        };
        helper.Events.World.BuildingListChanged += (_, _) => { if (Context.IsMainPlayer) tracker?.DiscoverLocations(); };
        helper.Events.World.LocationListChanged += (_, e) =>
        {
            if (!Context.IsMainPlayer || tracker == null) return;
            foreach (GameLocation location in e.Removed) tracker.RemoveLocation(location);
            tracker.DiscoverLocations();
        };
        helper.Events.Display.MenuChanged += (_, e) =>
        {
            if (Context.IsWorldReady) menus.Value.MenuChanged(e.NewMenu);
            else menus.Value.Detach();
        };
        helper.Events.Display.RenderingActiveMenu += (_, e) => { if (Context.IsWorldReady) menus.Value.BeforeDraw(e.SpriteBatch); };
        helper.Events.Display.RenderedActiveMenu += (_, e) => { if (Context.IsWorldReady) menus.Value.AfterDraw(e.SpriteBatch); };
        helper.Events.Input.ButtonPressed += (_, e) => { if (Context.IsWorldReady) menus.Value.Input(e); };
        helper.Events.Multiplayer.ModMessageReceived += Message;
        helper.Events.Multiplayer.PeerConnected += (_, e) =>
        {
            if (Context.IsMainPlayer && e.Peer.GetMod(ModManifest.UniqueID) != null) SendFull(new[] { e.Peer.PlayerID });
            else if (e.Peer.IsHost) RequestSnapshot();
        };
        helper.ConsoleCommands.Add("production_progress_status", "Show production tracker counters without modifying the world.", (_, _) =>
            Monitor.Log(tracker == null ? "Client view; no local world scan." : $"Objects={tracker.TrackedObjects}; batches={tracker.Store.BatchCount}; pending={tracker.PendingChanges}; deadlines={tracker.ScheduledTasks}; initializing={tracker.Initializing}", LogLevel.Info));
        helper.ConsoleCommands.Add("production_progress_inspect", "Host only: stand beside a missing machine, then run this to log nearby objects and cached progress. Read-only.", (_, _) =>
        {
            if (!Context.IsWorldReady || !Context.IsMainPlayer || tracker == null)
            {
                Monitor.Log("Run this command on the host after loading the farm, while standing beside the affected machine.", LogLevel.Info);
                return;
            }
            tracker.InspectNearby(Game1.currentLocation, Game1.player.Tile);
        });
    }

    private void SaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        remote.Clear(); cachedRevision = -1; dayTransition = false;
        if (Context.IsMainPlayer)
        {
            tracker?.Dispose(); tracker = new ProductionTracker(config, Monitor);
            tracker.Load(Helper.Data.ReadSaveData<Dictionary<string, SavedCycle>>("cycle-totals"),
                Helper.Data.ReadSaveData<SavedCompletions>("completion-dates"));
            session = Guid.NewGuid().ToString("N"); sequence = 0; outgoing.Clear(); requestedSnapshots.Clear(); lastInitializing = true;
        }
        else RequestSnapshot();
    }

    private void Update(object? sender, UpdateTickedEventArgs e)
    {
        if (!Context.IsWorldReady) return;
        menus.Value.UpdateNavigation(tick: true);
        if (!Context.IsMainPlayer || tracker == null || dayTransition) return;
        tracker.Update();
        ticks++;
        if (outgoing.Count == 0 && requestedSnapshots.Count > 0)
        {
            long[] targets = requestedSnapshots.Where(id => Helper.Multiplayer.GetConnectedPlayer(id)?.GetMod(ModManifest.UniqueID) != null).ToArray();
            requestedSnapshots.Clear();
            Enqueue(new ChangePacket { Full = true, Initializing = tracker.Initializing, Rows = tracker.Store.Snapshot() }, targets);
        }
        else if (outgoing.Count == 0 && ticks % 15 == 0 && (tracker.Store.HasChanges || tracker.Initializing != lastInitializing))
        {
            var packet = tracker.Store.TakeChanges(tracker.Initializing);
            lastInitializing = tracker.Initializing;
            long[] peers = Helper.Multiplayer.GetConnectedPlayers().Where(p => p.GetMod(ModManifest.UniqueID) != null && !p.IsSplitScreen)
                .Select(p => p.PlayerID).ToArray();
            Enqueue(packet, peers);
        }
        if (outgoing.TryDequeue(out var message))
            Helper.Multiplayer.SendMessage(message.Packet, "progress", new[] { ModManifest.UniqueID }, message.Players);
    }

    private long Revision() => tracker?.Store.Revision ?? remote.Revision;
    private IReadOnlyCollection<ProgressRow> Rows()
    {
        if (tracker == null) return remote.Rows;
        if (cachedRevision != tracker.Store.Revision) { cachedRows = tracker.Store.Snapshot(); cachedRevision = tracker.Store.Revision; }
        return cachedRows;
    }
    private string? Status()
    {
        if (tracker != null) return tracker.Initializing ? Helper.Translation.Get("initializing").ToString() : null;
        if (!remote.HasSnapshot)
        {
            var host = Helper.Multiplayer.GetConnectedPlayers().FirstOrDefault(p => p.IsHost);
            return Helper.Translation.Get(host?.GetMod(ModManifest.UniqueID) == null ? "host-required" : "synchronizing");
        }
        return remote.Initializing ? Helper.Translation.Get("initializing").ToString() : null;
    }

    private void RequestSnapshot()
    {
        if (!Context.IsWorldReady || tracker != null) return;
        var host = Helper.Multiplayer.GetConnectedPlayers().FirstOrDefault(p => p.IsHost);
        if (host?.GetMod(ModManifest.UniqueID) != null)
            Helper.Multiplayer.SendMessage(new { Protocol = 1 }, "request", new[] { ModManifest.UniqueID }, new[] { host.PlayerID });
    }

    private void Message(object? sender, ModMessageReceivedEventArgs e)
    {
        if (!Context.IsWorldReady || e.FromModID != ModManifest.UniqueID) return;
        if (e.Type == "request" && Context.IsMainPlayer) SendFull(new[] { e.FromPlayerID });
        else if (e.Type == "progress" && !Context.IsMainPlayer && tracker == null && Helper.Multiplayer.GetConnectedPlayer(e.FromPlayerID)?.IsHost == true)
        {
            try { remote.Apply(e.ReadAs<ChangePacket>()); }
            catch (Exception ex) { Monitor.Log("Unable to read production snapshot: " + ex, LogLevel.Warn); }
        }
    }

    private void SendFull(long[] players)
    {
        if (tracker == null) return;
        foreach (long player in players) requestedSnapshots.Add(player);
    }

    private void Enqueue(ChangePacket source, long[] players)
    {
        if (players.Length == 0) return;
        const int chunk = 200;
        int rowIndex = 0, removeIndex = 0;
        bool first = true;
        do
        {
            var packet = new ChangePacket { Session = session, Revision = ++sequence, Full = first && source.Full };
            while (packet.Rows.Count + packet.Removed.Count < chunk && removeIndex < source.Removed.Count) packet.Removed.Add(source.Removed[removeIndex++]);
            while (packet.Rows.Count + packet.Removed.Count < chunk && rowIndex < source.Rows.Count) packet.Rows.Add(source.Rows[rowIndex++]);
            bool more = rowIndex < source.Rows.Count || removeIndex < source.Removed.Count;
            packet.Initializing = source.Initializing || (source.Full && more);
            outgoing.Enqueue((packet, players)); first = false;
        } while (rowIndex < source.Rows.Count || removeIndex < source.Removed.Count);
    }
}
