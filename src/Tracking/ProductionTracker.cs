using System.Diagnostics;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Netcode;
using StardewModdingAPI;
using StardewValley;
using StardewValley.GameData.Machines;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;
using MossHarbor.ProductionProgress.Core;
using SObject = StardewValley.Object;

namespace MossHarbor.ProductionProgress.Tracking;

internal readonly record struct SourceKey(GameLocation Location, Vector2 Tile, bool IsSoil);

internal sealed class ProductionTracker : IDisposable
{
    private sealed class Source
    {
        public long Id;
        public SourceKey Key;
        public HoeDirt? Soil;
        public IndoorPot? Pot;
        public SObject? Machine;
        public Crop? BoundCrop;
        public SObject? BoundOutput;
        public readonly Subscriptions Root = new(), CropEvents = new(), OutputEvents = new();
        public readonly CompletionMemory Completion = new();
        public Action Mark = null!;
        public bool Removed, Refreshed, NewlyPlaced, Daily;
        public bool CompletionLoaded, AllowCompletionRestore = true, ObservedNewCycle, DateIncludesTime;
        public ProductionKind LastKind;
        public ProgressClock LastClock;
        public int? ReadyDay, ReadyTime;
        public double Total;
        public string Rule = "";
        public void Dispose() { Removed = true; Root.Dispose(); CropEvents.Dispose(); OutputEvents.Dispose(); }
    }

    private readonly ModConfig config;
    private readonly IMonitor monitor;
    private readonly Dictionary<SourceKey, Source> sources = new();
    private readonly List<Source?> slots = new();
    private readonly Stack<int> freeSlots = new();
    private readonly Dictionary<long, int> slotIndexes = new();
    private readonly DirtyQueue<Source> dirty = new();
    private readonly DirtyQueue<SourceKey> worldChanges = new();
    private readonly DueSchedule<Source> due = new();
    private readonly Queue<GameLocation> pendingLocations = new();
    private readonly HashSet<GameLocation> queuedLocations = new();
    private readonly HashSet<GameLocation> removedLocations = new();
    private readonly HashSet<string> warned = new();
    private IEnumerator<Action>? discovery;
    private GameLocation? discoveringLocation;
    private long nextSourceId;
    private int dailyCursor = -1, dailyEnd;
    private Dictionary<string, SavedCycle> saved = new();
    private Dictionary<string, SavedCompletion> savedCompletions = new();
    public BatchStore Store { get; } = new();
    public long Clock { get; private set; }
    private int day;
    private bool overnight;
    public bool Initializing { get; private set; }
    public int TrackedObjects => sources.Count;
    public int PendingChanges => dirty.Count + worldChanges.Count;
    public int ScheduledTasks => due.Count;

    public ProductionTracker(ModConfig config, IMonitor monitor) { this.config = config; this.monitor = monitor; }

    public void Load(Dictionary<string, SavedCycle>? cycles, SavedCompletions? completions = null)
    {
        Clear(); saved = cycles ?? new(); Initializing = true;
        SetClock();
        if (completions?.SavedDay == day) savedCompletions = completions.Records ?? new();
        DiscoverLocations();
    }

    public void SetClock()
    { day = Game1.Date.TotalDays; Clock = ProgressMath.ProcessingClock(day, Game1.timeOfDay); }

    public void DiscoverLocations()
    {
        Utility.ForEachLocation(location =>
        {
            removedLocations.Remove(location);
            if (queuedLocations.Add(location)) pendingLocations.Enqueue(location);
            return true;
        }, includeInteriors: true, includeGenerated: false);
    }

    public void NewDay()
    {
        overnight = false; SetClock(); dailyCursor = 0; dailyEnd = slots.Count;
    }

    public void BeginOvernight() => overnight = true;

    // Explicit console command only: inspect nearby objects without refreshing,
    // registering, or mutating them. No periodic world scan or extra subscriptions.
    public void InspectNearby(GameLocation location, Vector2 center)
    {
        var nearby = new List<(Vector2 Tile, SObject Object)>();
        for (int y = (int)center.Y - 3; y <= (int)center.Y + 3; y++)
            for (int x = (int)center.X - 3; x <= (int)center.X + 3; x++)
            {
                var tile = new Vector2(x, y);
                if (location.objects.TryGetValue(tile, out SObject obj)) nearby.Add((tile, obj));
            }
        monitor.Log($"Production inspection: location={location.NameOrUniqueName}; day={Game1.Date.TotalDays}; time={Game1.timeOfDay}; "
            + $"clock={Clock}; objects={TrackedObjects}; batches={Store.BatchCount}; pending={PendingChanges}; "
            + $"initializing={Initializing}; timerNotifications={config.ObserveAllTimerChanges}; nearby={nearby.Count}; showing up to 12.", LogLevel.Info);
        foreach (var entry in nearby.OrderBy(p => Vector2.DistanceSquared(p.Tile, center)).Take(12))
        {
            try
            {
                SObject obj = entry.Object;
                sources.TryGetValue(new SourceKey(location, entry.Tile, false), out Source? source);
                ProgressRow? row = source == null ? null : Store.GetSourceRow(source.Id);
                MachineData? data = obj.GetMachineData();
                MachineOutputRule? rule = data?.OutputRules?.FirstOrDefault(r => r.Id == obj.lastOutputRuleId.Value);
                SObject? output = obj.heldObject.Value;
                location.terrainFeatures.TryGetValue(entry.Tile, out TerrainFeature terrain);
                Tree? tree = terrain as Tree;
                monitor.Log("Production inspection: " + JsonSerializer.Serialize(new
                {
                    X = (int)entry.Tile.X, Y = (int)entry.Tile.Y,
                    Object = obj.QualifiedItemId, Type = obj.GetType().FullName,
                    Tapper = obj.IsTapper(), TapperTags = obj.GetContextTags().Where(t => t.StartsWith("tapper_", StringComparison.Ordinal)).ToArray(),
                    Output = output?.QualifiedItemId, Flavor = output?.preservedParentSheetIndex.Value,
                    Ready = obj.readyForHarvest.Value, Minutes = obj.MinutesUntilReady,
                    HasMachineData = data != null, Incubator = data?.IsIncubator, OvernightOnly = data?.OnlyCompleteOvernight,
                    LastRule = obj.lastOutputRuleId.Value, RuleDays = rule?.DaysUntilReady, RuleMinutes = rule?.MinutesUntilReady,
                    Outdoors = location.IsOutdoors, RainingHere = location.IsRainingHere(), SolarClock = UsesSolarClock(data),
                    TimeBlockers = data?.PreventTimePass?.Select(p => p.ToString()).ToArray(),
                    OutputMethods = rule?.OutputItem?.Select(p => p.OutputMethod).ToArray(),
                    TreeType = tree?.treeType.Value, TreeStage = tree?.growthStage.Value,
                    TapItems = tree?.GetData()?.TapItems?.Select(t => new
                        { t.ItemId, t.DaysUntilReady, Modifiers = t.DaysUntilReadyModifiers?.Count ?? 0, PreviousItems = t.PreviousItemId?.Count ?? 0 }).ToArray(),
                    Tracked = source != null, SameObject = source != null && ReferenceEquals(source.Machine, obj),
                    Refreshed = source?.Refreshed, Daily = source?.Daily, KnownTotal = source?.Total,
                    Cached = row == null ? null : new
                    {
                        row.Id, row.Count, row.ItemId, Kind = row.Kind.ToString(), State = row.State.ToString(), Clock = row.Clock.ToString(),
                        row.Total, row.Remaining, row.Due, row.ObservedDay, Fraction = row.Fraction(Clock), Finish = row.DisplayDate(Clock),
                        SunnyDaysRemaining = row.RemainingSunnyDays()
                    }
                }), LogLevel.Info);
            }
            catch (Exception ex)
            {
                monitor.Log($"Production inspection failed at {entry.Tile}: {ex}", LogLevel.Warn);
            }
        }
        if (nearby.Count == 0) monitor.Log("No placed objects within 3 tiles. Stand beside the affected tapper and run the command again.", LogLevel.Info);
    }

    public void QueueObject(GameLocation location, Vector2 tile) => worldChanges.Add(new(location, tile, false));
    public void QueueSoil(GameLocation location, Vector2 tile) => worldChanges.Add(new(location, tile, true));

    private void Reconcile(SourceKey key)
    {
        if (removedLocations.Contains(key.Location)) return;
        if (key.IsSoil)
        {
            if (key.Location.terrainFeatures.TryGetValue(key.Tile, out TerrainFeature feature) && feature is HoeDirt soil)
                AddSoil(key.Location, key.Tile, soil, newlyPlaced: true);
            else if (sources.TryGetValue(key, out Source? old) && old.Pot == null) Remove(key);
        }
        else
        {
            key.Location.objects.TryGetValue(key.Tile, out SObject? obj);
            if (sources.TryGetValue(key, out Source? old) && !ReferenceEquals(old.Machine, obj)) Remove(key);
            SourceKey potKey = key with { IsSoil = true };
            if (sources.TryGetValue(potKey, out Source? pot) && pot.Pot != null && !ReferenceEquals(pot.Pot, obj)) Remove(potKey);
            if (obj != null) AddObject(key.Location, key.Tile, obj, newlyPlaced: true);
        }
    }

    public void AddSoil(GameLocation location, Vector2 tile, HoeDirt soil, IndoorPot? pot = null, bool newlyPlaced = false)
    {
        var key = new SourceKey(location, tile, true);
        if (sources.TryGetValue(key, out Source? existing))
        {
            if (ReferenceEquals(existing.Soil, soil) && ReferenceEquals(existing.Pot, pot)) return;
            Remove(key);
        }
        var s = Register(new Source { Key = key, Soil = soil, Pot = pot, Daily = true, AllowCompletionRestore = !newlyPlaced });
        if (pot != null) s.Root.Watch(pot.hoeDirt, s.Mark);
        BindSoil(s);
    }

    public void AddObject(GameLocation location, Vector2 tile, SObject obj, bool newlyPlaced = false)
    {
        if (obj is IndoorPot pot)
        {
            AddSoil(location, tile, pot.hoeDirt.Value, pot, newlyPlaced);
            return;
        }
        // An incubator keeps its input egg in heldObject; it isn't a collectable
        // processed egg. Loose animal produce has no machine data in the first place.
        if (obj is not Cask && !obj.IsTapper())
        {
            MachineData? machineData = obj.GetMachineData();
            if (machineData == null || machineData.IsIncubator) return;
        }
        var key = new SourceKey(location, tile, false);
        if (sources.TryGetValue(key, out Source? existing))
        {
            if (ReferenceEquals(existing.Machine, obj)) return;
            Remove(key);
        }
        Source s = Register(new Source { Key = key, Machine = obj, NewlyPlaced = newlyPlaced, AllowCompletionRestore = !newlyPlaced });
        s.Root.Watch(obj.heldObject, s.Mark);
        s.Root.WatchValues(obj.readyForHarvest, (_, value) =>
        {
            if (!value) { s.Completion.BeginCycle(); s.ReadyDay = null; s.ReadyTime = null; }
            else if (!overnight && s.Refreshed && !s.ReadyDay.HasValue)
            {
                // Record at the notification, before budgeted refresh can be delayed by other objects.
                s.ReadyDay = day; s.ReadyTime = Game1.timeOfDay;
            }
            s.Mark();
        });
        s.Root.Watch(obj.lastOutputRuleId, s.Mark);
        s.Root.Watch(obj.lastInputItem, s.Mark);
        // Weather-dependent work must follow actual timer changes, not an elapsed wall clock.
        // Reuse the existing coalesced queue; ordinary machines retain their deadline schedule.
        if (config.ObserveAllTimerChanges || UsesSolarClock(obj.GetMachineData()))
            s.Root.Watch(obj.minutesUntilReady, s.Mark);
        if (obj is Cask cask)
        {
            s.Daily = true;
            s.Root.Watch(cask.daysToMature, s.Mark);
            s.Root.Watch(cask.agingRate, s.Mark);
        }
    }

    private Source Register(Source s)
    {
        s.Id = ++nextSourceId;
        s.Mark = () => { if (!s.Removed) dirty.Add(s); };
        sources.Add(s.Key, s);
        int slot = freeSlots.Count != 0 ? freeSlots.Pop() : slots.Count;
        if (slot == slots.Count) slots.Add(s); else slots[slot] = s;
        slotIndexes[s.Id] = slot;
        s.Mark(); return s;
    }

    public void RemoveSoil(GameLocation location, Vector2 tile) => Remove(new(location, tile, true));
    public void RemoveObject(GameLocation location, Vector2 tile, SObject obj)
        => Remove(new(location, tile, obj is IndoorPot));

    public void RemoveLocation(GameLocation location)
    {
        removedLocations.Add(location);
        foreach (SourceKey key in sources.Keys.Where(k => ReferenceEquals(k.Location, location)).ToArray()) Remove(key);
    }

    private void Remove(SourceKey key)
    {
        if (!sources.Remove(key, out Source? s)) return;
        s.Dispose(); due.Remove(s); Store.Remove(s.Id);
        if (slotIndexes.Remove(s.Id, out int index)) { slots[index] = null; freeSlots.Push(index); }
    }

    private void BindSoil(Source s)
    {
        // GetFields exposes the registered NetRef even though HoeDirt.netCrop is private.
        NetRef<Crop>? reference = s.Soil!.NetFields.GetFields().OfType<NetRef<Crop>>()
            .SingleOrDefault(f => f.Name.EndsWith(": netCrop", StringComparison.Ordinal));
        if (reference != null) s.Root.Watch(reference, s.Mark);
        else WarnOnce("crop-ref", "Could not find a registered crop reference; this soil will only be calibrated at day boundaries.");
        s.Root.Watch(s.Soil.fertilizer, s.Mark);
    }

    public void Update()
    {
        long start = Stopwatch.GetTimestamp();
        double budget = Math.Clamp(config.UpdateBudgetMilliseconds, 0.1, 3);
        int limit = Math.Clamp(config.MaxObjectsPerUpdate, 1, 512);
        for (int processed = 0; processed < limit; processed++)
        {
            if (processed > 0 && (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency >= budget) break;
            if (worldChanges.TryTake(out SourceKey key)) Reconcile(key);
            else if (dirty.TryTake(out Source s))
            {
                if (!s.Removed)
                {
                    try { Refresh(s); }
                    catch (Exception ex)
                    {
                        Store.Remove(s.Id);
                        WarnOnce(s.Key.ToString(), $"Unable to read production state ({s.Machine?.QualifiedItemId ?? "crop"}, "
                            + $"{s.Key.Location.NameOrUniqueName}, {s.Key.Tile}): {ex}");
                    }
                }
            }
            else if (due.TryTake(Clock, out Source elapsed)) elapsed.Mark();
            else if (dailyCursor >= 0)
            {
                if (dailyCursor < dailyEnd)
                {
                    Source? daily = slots[dailyCursor++];
                    if (daily is { Removed: false, Daily: true }) daily.Mark();
                }
                else dailyCursor = -1;
            }
            else if (!DiscoverOne()) break;
        }
        if (Initializing && discovery == null && pendingLocations.Count == 0 && PendingChanges == 0) Initializing = false;
    }

    private bool DiscoverOne()
    {
        if (discoveringLocation != null && removedLocations.Contains(discoveringLocation))
        {
            discovery?.Dispose(); discovery = null;
            queuedLocations.Remove(discoveringLocation); discoveringLocation = null;
        }
        if (discovery == null)
        {
            if (!pendingLocations.TryDequeue(out discoveringLocation)) return false;
            if (removedLocations.Contains(discoveringLocation))
            {
                queuedLocations.Remove(discoveringLocation); discoveringLocation = null; return true;
            }
            discovery = Enumerate(discoveringLocation).GetEnumerator();
        }
        try
        {
            if (discovery.MoveNext()) { discovery.Current(); return true; }
            discovery?.Dispose(); discovery = null;
            queuedLocations.Remove(discoveringLocation!); discoveringLocation = null;
        }
        catch (InvalidOperationException)
        {
            // A world edit can invalidate a collection iterator between frames.
            // Restart only this location; registration is idempotent.
            discovery?.Dispose(); discovery = null;
            pendingLocations.Enqueue(discoveringLocation!); discoveringLocation = null;
        }
        return true;
    }

    private IEnumerable<Action> Enumerate(GameLocation location)
    {
        foreach (var pair in location.terrainFeatures.Pairs)
        {
            Vector2 tile = pair.Key; TerrainFeature feature = pair.Value;
            yield return () => { if (feature is HoeDirt soil) AddSoil(location, tile, soil); };
        }
        foreach (var pair in location.objects.Pairs)
        {
            Vector2 tile = pair.Key; SObject obj = pair.Value;
            yield return () => AddObject(location, tile, obj);
        }
    }

    private void Refresh(Source s)
    {
        due.Remove(s);
        if (s.Soil != null)
        {
            if (s.Pot != null && !ReferenceEquals(s.Soil, s.Pot.hoeDirt.Value))
            {
                Remove(s.Key); AddSoil(s.Key.Location, s.Key.Tile, s.Pot.hoeDirt.Value, s.Pot, newlyPlaced: true); return;
            }
            RefreshCrop(s); return;
        }
        SObject machine = s.Machine!;
        SObject? output = machine.heldObject.Value;
        bool newCycle = !ReferenceEquals(s.BoundOutput, output);
        s.ObservedNewCycle = output != null && s.Refreshed && newCycle;
        bool observedStart = output != null && (s.NewlyPlaced || (s.Refreshed && newCycle));
        if (newCycle)
        {
            s.Completion.Reset();
            s.OutputEvents.Dispose(); s.BoundOutput = output;
            if (output != null) s.OutputEvents.Watch(output.quality, s.Mark);
            s.Total = 0;
        }
        s.NewlyPlaced = false; s.Refreshed = true;
        if (output == null) { DiscardCompletionRestore(s); Store.Remove(s.Id); return; }
        ProductionState state = machine.readyForHarvest.Value ? ProductionState.Ready : ProductionState.Working;
        if (machine is Cask cask) { RefreshCask(s, cask, output, state); return; }
        MachineData? data = machine.GetMachineData();
        MachineOutputRule? rule = data?.OutputRules?.FirstOrDefault(r => r.Id == machine.lastOutputRuleId.Value);
        bool solar = UsesSolarClock(data) && rule != null;
        bool tapper = machine.IsTapper();
        bool daily = tapper || rule?.DaysUntilReady >= 0 || data?.OnlyCompleteOvernight == true;
        s.DateIncludesTime = !daily;
        bool conditional = data?.PreventTimePass?.Count > 0;
        s.Daily = daily || conditional;
        int remaining = Math.Max(0, machine.MinutesUntilReady);
        string ruleId = machine.QualifiedItemId + ":" + machine.lastOutputRuleId.Value;
        if (s.Rule != ruleId) s.Total = 0;
        s.Rule = ruleId;
        double units = daily ? Math.Ceiling(remaining / 1600d) : remaining;
        if (state == ProductionState.Working && s.Total <= 0)
        {
            if (observedStart) s.Total = units;
            else if (saved.TryGetValue(SaveKey(s), out SavedCycle? old) && old.Fingerprint == Fingerprint(machine)
                     && old.Remaining == remaining && old.Daily == s.Daily) s.Total = old.Total;
            else s.Total = InferTotal(s, rule, data, output, daily);
            if (s.Total < units) s.Total = 0; // unknown modifiers: don't invent a denominator
        }
        long deadline = Clock + remaining;
        if (state == ProductionState.Working && !daily && !conditional)
            due.Set(s, Math.Max(Clock + 10, deadline));
        Set(s, output.QualifiedItemId, output.DisplayName, tapper ? ProductionKind.Tapper : ProductionKind.Machine,
            state, solar ? ProgressClock.SunnyMinutes : daily || conditional ? ProgressClock.Daily : ProgressClock.ProcessingMinutes,
            conditional && !solar ? 0 : s.Total, daily || conditional ? units : 0,
            daily || conditional ? 0 : deadline, output.Quality, output.Quality, 1, s.Rule,
            output.preservedParentSheetIndex.Value ?? "", output.preserve.Value.HasValue ? (int)output.preserve.Value.Value : -1);
    }

    private static bool UsesSolarClock(MachineData? data)
    {
        // Recognize the inspected vanilla solar output contract, not every battery-producing
        // machine or arbitrary conditional timer. Other rule shapes keep the unknown estimate.
        if (data?.OnlyCompleteOvernight != false || data.OutputRules is not { Count: 1 }
            || data.PreventTimePass is not { Count: 2 }
            || !data.PreventTimePass.Contains(MachineTimeBlockers.Inside)
            || !data.PreventTimePass.Contains(MachineTimeBlockers.Rain)) return false;
        MachineOutputRule rule = data.OutputRules[0];
        return rule.DaysUntilReady < 0 && rule.MinutesUntilReady < 0
            && rule.OutputItem is { Count: 1 }
            && rule.OutputItem[0].OutputMethod == "StardewValley.Object, Stardew Valley: OutputSolarPanel";
    }

    private void RefreshCrop(Source s)
    {
        Crop? crop = s.Soil!.crop;
        s.ObservedNewCycle = crop != null && s.Refreshed && !ReferenceEquals(s.BoundCrop, crop);
        s.Refreshed = true;
        if (!ReferenceEquals(s.BoundCrop, crop))
        {
            s.Completion.Reset();
            s.CropEvents.Dispose(); s.BoundCrop = crop;
            if (crop != null)
            {
                s.CropEvents.Watch(crop.currentPhase, s.Mark);
                s.CropEvents.WatchValues(crop.dayOfCurrentPhase, (oldValue, newValue) =>
                {
                    if (oldValue <= 0 && newValue > 0 && crop.fullyGrown.Value) s.Completion.BeginCycle();
                    s.Mark();
                });
                s.CropEvents.WatchValues(crop.fullyGrown, (oldValue, newValue) =>
                {
                    if (!oldValue && newValue) s.Completion.BeginCycle();
                    s.Mark();
                });
                s.CropEvents.Watch(crop.dead, s.Mark);
                s.CropEvents.Watch(crop.phaseDays, s.Mark);
                s.CropEvents.Watch(crop.indexOfHarvest, s.Mark);
                s.CropEvents.Watch(crop.forageCrop, s.Mark); s.CropEvents.Watch(crop.whichForageCrop, s.Mark);
            }
        }
        if (crop == null) { DiscardCompletionRestore(s); Store.Remove(s.Id); return; }
        string? rawId = CropIdentity.DisplayItemId(crop.forageCrop.Value, crop.isWildSeedCrop(),
            crop.indexOfHarvest.Value, crop.whichForageCrop.Value);
        if (rawId == null) { Store.Remove(s.Id); return; }
        string? itemId = ItemRegistry.QualifyItemId(rawId);
        var itemData = itemId == null ? null : ItemRegistry.GetData(itemId);
        if (itemData == null || itemData.IsErrorItem) { Store.Remove(s.Id); return; }
        string name = itemData.DisplayName;
        int regrowDays = crop.GetData()?.RegrowDays ?? -1;
        bool regrowth = crop.fullyGrown.Value && regrowDays > 0;
        var amounts = regrowth ? ((double)regrowDays, (double)Math.Max(0, crop.dayOfCurrentPhase.Value))
            : ProgressMath.Crop(crop.phaseDays.ToArray(), crop.currentPhase.Value, crop.dayOfCurrentPhase.Value);
        ProductionState state = crop.dead.Value ? ProductionState.Withered
            : s.Soil.readyForHarvest() ? ProductionState.Ready : ProductionState.Working;
        Set(s, itemId!, name, regrowth ? ProductionKind.Regrowth : ProductionKind.Crop, state, ProgressClock.Daily,
            amounts.Item1, amounts.Item2, 0, 0, 0, 1, string.Join(",", crop.phaseDays.Take(Math.Max(0, crop.phaseDays.Count - 1))));
    }

    private void RefreshCask(Source s, Cask cask, SObject output, ProductionState state)
    {
        int from = output.Quality, to = state == ProductionState.Ready ? from : cask.GetNextQuality(from);
        float start = cask.GetDaysForQuality(from), end = cask.GetDaysForQuality(to);
        double total = from >= 4 ? 0 : Math.Max(0, start - end);
        Set(s, output.QualifiedItemId, output.DisplayName, ProductionKind.Aging, state, ProgressClock.Daily,
            total, Math.Max(0, cask.daysToMature.Value - end), 0, from, to, cask.agingRate.Value, "cask",
            output.preservedParentSheetIndex.Value ?? "", output.preserve.Value.HasValue ? (int)output.preserve.Value.Value : -1);
    }

    private double InferTotal(Source s, MachineOutputRule? rule, MachineData? data, SObject output, bool daily)
    {
        if (data?.ReadyTimeModifiers?.Count > 0) return 0;
        if (rule != null && rule.OutputItem?.Any(o => !string.IsNullOrEmpty(o.OutputMethod)) != true)
            return daily ? Math.Max(0, rule.DaysUntilReady) : Math.Max(0, rule.MinutesUntilReady);
        if (s.Machine!.IsTapper() && s.Key.Location.terrainFeatures.TryGetValue(s.Key.Tile, out TerrainFeature terrain) && terrain is Tree tree)
        {
            var candidates = tree.GetData()?.TapItems?.Where(x => ItemRegistry.QualifyItemId(x.ItemId) == output.QualifiedItemId
                && x.DaysUntilReadyModifiers?.Count is not > 0 && x.PreviousItemId?.Count is not > 0).ToArray();
            if (candidates?.Length == 1)
                return Math.Max(1, (int)(candidates[0].DaysUntilReady * (s.Machine.QualifiedItemId == "(BC)264" ? 0.5 : 1)));
        }
        return 0;
    }

    private void Set(Source s, string itemId, string name, ProductionKind kind, ProductionState state,
        ProgressClock clock, double total, double remaining, long deadline, int from, int to, double rate,
        string rule, string flavor = "", int preserve = -1)
    {
        s.LastKind = kind; s.LastClock = clock;
        if (!s.CompletionLoaded)
        {
            s.CompletionLoaded = true;
            if (savedCompletions.Count > 0 && savedCompletions.Remove(CompletionKey(s), out SavedCompletion? old)
                && s.AllowCompletionRestore && state == ProductionState.Ready && old.Kind == kind
                // Earlier versions saved solar completions under the generic conditional daily clock.
                && (old.Clock == clock || clock == ProgressClock.SunnyMinutes && old.Clock == ProgressClock.Daily)
                && old.Fingerprint == CompletionFingerprint(s) && old.Day >= 0 && old.Day <= day)
                s.Completion.Restore(old.Day, old.Time, old.AgingFromQuality);
        }
        s.Completion.Observe(state, kind, s.DateIncludesTime ? ProgressClock.ProcessingMinutes : ProgressClock.Daily,
            s.ReadyDay ?? day, s.ReadyTime ?? Game1.timeOfDay, from, s.ObservedNewCycle);
        s.ObservedNewCycle = false;
        if (state == ProductionState.Ready)
        {
            if (kind == ProductionKind.Aging) from = s.Completion.AgingFromQuality ?? from;
            total = 1; remaining = 0; deadline = 0;
            s.ReadyDay = null; s.ReadyTime = null;
        }
        Store.Set(s.Id, new BatchKey(s.Key.Location.NameOrUniqueName, itemId, flavor, preserve, kind, state,
            clock, from, to, total, remaining, deadline, rate, rule,
            state == ProductionState.Ready ? s.Completion.Day : null,
            state == ProductionState.Ready ? s.Completion.TimeOfDay : null,
            state == ProductionState.Working && clock == ProgressClock.Daily ? day : null), name);
    }

    private void DiscardCompletionRestore(Source s)
    {
        s.CompletionLoaded = true;
        if (savedCompletions.Count > 0) savedCompletions.Remove(CompletionKey(s));
        s.Completion.Reset();
        s.ReadyDay = null; s.ReadyTime = null;
    }

    private static string CompletionKey(Source s) => SaveKey(s) + (s.Key.IsSoil ? "/crop" : "/machine");
    private static string CompletionFingerprint(Source s)
    {
        if (s.Machine is { } machine) return Fingerprint(machine) + ":" + machine.heldObject.Value?.Quality;
        Crop? crop = s.Soil?.crop;
        return crop == null ? "" : crop.indexOfHarvest.Value + ":" + crop.whichForageCrop.Value + ":"
            + crop.currentPhase.Value + ":" + crop.dayOfCurrentPhase.Value + ":" + crop.fullyGrown.Value + ":"
            + crop.dead.Value + ":" + string.Join(",", crop.phaseDays);
    }

    public SavedCompletions CaptureCompletions(bool overnight)
    {
        var result = new SavedCompletions { SavedDay = Game1.Date.TotalDays };
        foreach (Source s in sources.Values)
        {
            bool ready = s.Machine is { } machine
                ? ReferenceEquals(machine.heldObject.Value, s.BoundOutput) && s.BoundOutput != null && machine.readyForHarvest.Value
                : ReferenceEquals(s.Soil?.crop, s.BoundCrop) && s.BoundCrop != null && !s.BoundCrop.dead.Value && s.Soil!.readyForHarvest();
            if (!ready || !s.CompletionLoaded) continue;
            // At the vanilla overnight save, field notifications are coalesced until DayStarted.
            // Persist the observed ready transition before the new-day cache refresh.
            s.Completion.Observe(ProductionState.Ready, s.LastKind,
                s.DateIncludesTime ? ProgressClock.ProcessingMinutes : ProgressClock.Daily, s.ReadyDay ?? result.SavedDay,
                s.ReadyTime ?? (overnight ? 600 : Game1.timeOfDay), s.Machine?.heldObject.Value?.Quality ?? 0);
            s.ReadyDay = null; s.ReadyTime = null;
            if (s.Completion.Day is not int day) continue;
            result.Records[CompletionKey(s)] = new SavedCompletion { Fingerprint = CompletionFingerprint(s),
                Kind = s.LastKind, Clock = s.LastClock, Day = day, Time = s.Completion.TimeOfDay,
                AgingFromQuality = s.Completion.AgingFromQuality };
        }
        return result;
    }

    private static string SaveKey(Source s) => s.Key.Location.NameOrUniqueName + "/" + (int)s.Key.Tile.X + "/" + (int)s.Key.Tile.Y;
    private static string Fingerprint(SObject obj) => obj.QualifiedItemId + ":" + obj.lastOutputRuleId.Value + ":"
        + obj.heldObject.Value?.QualifiedItemId + ":" + obj.heldObject.Value?.preservedParentSheetIndex.Value + ":" + obj.lastInputItem.Value?.QualifiedItemId;

    public Dictionary<string, SavedCycle> CaptureCycles()
    {
        var result = new Dictionary<string, SavedCycle>();
        foreach (Source s in sources.Values)
            if (s.Machine is { } machine && machine is not Cask && machine.heldObject.Value != null && !machine.readyForHarvest.Value && s.Total > 0)
                result[SaveKey(s)] = new SavedCycle { Fingerprint = Fingerprint(machine), Total = s.Total,
                    Remaining = machine.MinutesUntilReady, Daily = s.Daily };
        return result;
    }

    private void WarnOnce(string id, string message) { if (warned.Add(id)) monitor.Log(message, LogLevel.Warn); }
    public void Dispose() => Clear();
    public void Clear()
    {
        foreach (Source s in sources.Values) s.Dispose();
        sources.Clear(); slots.Clear(); freeSlots.Clear(); slotIndexes.Clear(); dirty.Clear(); worldChanges.Clear(); due.Clear();
        discovery?.Dispose(); discovery = null; discoveringLocation = null;
        queuedLocations.Clear(); pendingLocations.Clear(); removedLocations.Clear(); saved.Clear(); savedCompletions.Clear(); warned.Clear();
        dailyCursor = -1; overnight = false; Initializing = false; Store.Clear();
    }
}

public sealed class SavedCycle
{
    public string Fingerprint { get; set; } = "";
    public double Total { get; set; }
    public int Remaining { get; set; }
    public bool Daily { get; set; }
}

public sealed class SavedCompletions
{
    public int SavedDay { get; set; } = -1;
    public Dictionary<string, SavedCompletion> Records { get; set; } = new();
}

public sealed class SavedCompletion
{
    public string Fingerprint { get; set; } = "";
    public ProductionKind Kind { get; set; }
    public ProgressClock Clock { get; set; }
    public int Day { get; set; }
    public int? Time { get; set; }
    public int? AgingFromQuality { get; set; }
}
