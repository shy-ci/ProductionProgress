using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI.Events;
using StardewValley.Menus;

namespace MossHarbor.ProductionProgress.Menus;

// Minimal consumer contract for Better Game Menu 1.0.1's public API.
// SMAPI maps this interface; no BetterGameMenu.dll reference or private reflection.
// https://github.com/KhloeLeclair/StardewMods/blob/main/BetterGameMenu/IBetterGameMenu.cs
public interface IBetterGameMenuApi
{
    public delegate void DrawDelegate(SpriteBatch batch, Rectangle bounds);
    public delegate void MenuCreatedDelegate(IClickableMenu menu);
    public delegate void TabChangedDelegate(ITabChangedEvent evt);

    void RegisterTab(string id, int order, Func<string> getDisplayName,
        Func<(DrawDelegate DrawMethod, bool DrawBackground)> getIcon, int priority,
        Func<IClickableMenu, IClickableMenu> getPageInstance,
        Func<DrawDelegate?>? getDecoration = null, Func<bool>? getTabVisible = null,
        Func<bool>? getMenuInvisible = null, Func<int, int>? getWidth = null,
        Func<int, int>? getHeight = null,
        Func<(IClickableMenu Menu, IClickableMenu OldPage), IClickableMenu?>? onResize = null,
        Action<IClickableMenu>? onClose = null);

    IClickableMenu? GetCurrentPage(IClickableMenu menu);
    void OnMenuCreated(MenuCreatedDelegate handler, EventPriority priority = EventPriority.Normal);
    void OnTabChanged(TabChangedDelegate handler, EventPriority priority = EventPriority.Normal);
}

// Keep the complete event shape: SMAPI's delegate proxy also validates the
// reverse conversion, even when this consumer never reads OldTab.
public interface ITabChangedEvent
{
    IClickableMenu Menu { get; }
    string Tab { get; }
    string OldTab { get; }
}
