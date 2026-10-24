using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;
using MossHarbor.ProductionProgress.Core;

namespace MossHarbor.ProductionProgress.Menus;

internal sealed class BetterGameMenuIntegration
{
    internal const string ModId = "leclair.bettergamemenu";
    internal const string TabId = "MossHarbor.ProductionProgress";
    private readonly IModHelper helper;
    private readonly IMonitor monitor;
    private readonly Func<IReadOnlyCollection<ProgressRow>> rows;
    private readonly Func<long> revision;
    private readonly Func<string?> status;
    private readonly Action opened;
    private IBetterGameMenuApi? api;

    public BetterGameMenuIntegration(IModHelper helper, IMonitor monitor,
        Func<IReadOnlyCollection<ProgressRow>> rows, Func<long> revision,
        Func<string?> status, Action opened)
    { this.helper = helper; this.monitor = monitor; this.rows = rows; this.revision = revision; this.status = status; this.opened = opened; }

    public void Register()
    {
        if (!helper.ModRegistry.IsLoaded(ModId)) return;
        try
        {
            api = helper.ModRegistry.GetApi<IBetterGameMenuApi>(ModId);
            if (api == null)
            {
                monitor.Log("Better Game Menu is loaded but its API is unavailable; Progress cannot register with that menu. Include the full SMAPI log when reporting this.", LogLevel.Error);
                return;
            }
            api.RegisterTab(TabId, 30, () => helper.Translation.Get("title").ToString(),
                () => (DrawIcon, true), 0, CreatePage,
                getTabVisible: () => Context.IsWorldReady,
                onClose: page => ((ProgressPage)page).ReleaseResources());
            // The host caches pages. Refresh on each activation, including menus
            // constructed directly on Progress before activeClickableMenu is assigned.
            api.OnMenuCreated(Activate);
            api.OnTabChanged(e => { if (e.Tab == TabId) Activate(e.Menu); });
            monitor.Log("Registered Progress with Better Game Menu; tab layout and navigation are managed by its API.", LogLevel.Info);
        }
        catch (Exception ex)
        {
            monitor.Log($"Failed to register Progress with Better Game Menu {helper.ModRegistry.Get(ModId)?.Manifest.Version}. Include the full SMAPI log when reporting this.\n{ex}", LogLevel.Error);
        }
    }

    private IClickableMenu CreatePage(IClickableMenu host)
        => new ProgressPage(host, helper.Translation, rows, revision, status, opened,
            // BGM 1.0.1 places its first row at the native tab Y; an optional
            // second row extends upward. No tab position is changed here.
            () => host.yPositionOnScreen + IClickableMenu.tabYPositionRelativeToMenuY + 128);

    private void Activate(IClickableMenu host)
    {
        if (api?.GetCurrentPage(host) is ProgressPage page) page.Open();
    }

    private static void DrawIcon(SpriteBatch batch, Rectangle bounds)
    {
        var icon = ItemRegistry.GetDataOrErrorItem("(O)262");
        batch.Draw(icon.GetTexture(), new Rectangle(bounds.X + bounds.Width / 4,
            bounds.Y + bounds.Height * 3 / 8, bounds.Width / 2, bounds.Height / 2),
            icon.GetSourceRect(0, null), Color.White);
    }
}
