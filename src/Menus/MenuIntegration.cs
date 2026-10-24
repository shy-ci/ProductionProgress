using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.ItemTypeDefinitions;
using StardewValley.Menus;
using MossHarbor.ProductionProgress.Core;
using System.Runtime.CompilerServices;

namespace MossHarbor.ProductionProgress.Menus;

internal sealed class MenuIntegration
{
    private const int DisplayIndex = 2;
    private const int PreferredTabId = 876410;
    private readonly IModHelper helper;
    private readonly Func<IReadOnlyCollection<ProgressRow>> rows;
    private readonly Func<long> revision;
    private readonly Func<string?> status;
    private readonly Action opened;
    private readonly IMonitor monitor;
    private GameMenu? menu;
    private ProgressPage? page;
    private ClickableComponent? tab, beforeTab, afterTab;
    private IClickableMenu? lastPage;
    private int tabId;
    private readonly Dictionary<ClickableComponent, OwnedValue<Rectangle>> boundsChanges = new();
    private OwnedValue<int>? beforeRight, afterLeft;
    private bool resumeAfterResize, resumeAfterMap;
    private bool consumedLeftTrigger, consumedRightTrigger;
    private ParsedItemData? icon;
    private GameMenu? hoverOwner;
    private string? suppressedHoverText;
    private bool IsSelected => page != null && ReferenceEquals(CurrentPage(menu), page);
    private bool IsActive => menu != null && ReferenceEquals(Game1.activeClickableMenu, menu);
    private bool CanInteractWithTabs => menu != null && IsActive && !menu.invisible
        && menu.GetChildMenu() == null && CurrentPage(menu)?.GetChildMenu() == null
        && CurrentPage(menu) is not CollectionsPage { letterviewerSubMenu: not null };

    public MenuIntegration(IModHelper helper, Func<IReadOnlyCollection<ProgressRow>> rows,
        Func<long> revision, Func<string?> status, Action opened, IMonitor monitor)
    { this.helper = helper; this.rows = rows; this.revision = revision; this.status = status; this.opened = opened; this.monitor = monitor; }

    public void MenuChanged(IClickableMenu? next)
    {
        if (next is GameMenu gameMenu) Attach(gameMenu);
        else Detach();
    }

    public void Attach(GameMenu gameMenu)
    {
        if (ReferenceEquals(menu, gameMenu)) return;
        bool resume = resumeAfterResize, returnFromMap = resumeAfterMap;
        Detach();
        if (gameMenu.tabs.Count <= DisplayIndex) return;
        beforeTab = gameMenu.tabs[DisplayIndex - 1];
        afterTab = gameMenu.tabs[DisplayIndex];
        Rectangle position = afterTab.bounds;
        if (position.Width <= 0 || position.Height <= 0 || !SameRow(beforeTab.bounds, position)) return;
        menu = gameMenu;
        int step = position.Width + Math.Max(0, position.Left - beforeTab.bounds.Right);
        foreach (ClickableComponent item in menu.tabs)
        {
            if (item.bounds.X >= position.X && SameRow(item.bounds, position))
            {
                Rectangle shifted = new(item.bounds.X + step, item.bounds.Y, item.bounds.Width, item.bounds.Height);
                boundsChanges[item] = new(item.bounds, shifted);
                item.bounds = shifted;
            }
        }
        tabId = MenuOwnership.AvailableId(Components(menu).Select(c => c.myID), PreferredTabId);
        tab = new(position, "MossHarbor.ProductionProgress", helper.Translation.Get("title"))
        { myID = tabId, leftNeighborID = beforeTab.myID, rightNeighborID = afterTab.myID, downNeighborID = -99998,
            tryDefaultIfNoDownNeighborExists = true, fullyImmutable = true };
        page = new(menu, helper.Translation, rows, revision, status, opened,
            () => gameMenu.tabs.Select(t => t.bounds.Bottom).DefaultIfEmpty(gameMenu.yPositionOnScreen).Max());
        menu.pages.Add(page);
        icon = ItemRegistry.GetDataOrErrorItem("(O)262");
        resumeAfterResize = resume; resumeAfterMap = returnFromMap;
        UpdateNavigation();
    }

    public void Detach()
    {
        RestoreNativeHoverText();
        if (menu != null)
        {
            foreach (var pair in boundsChanges) pair.Key.bounds = pair.Value.Restore(pair.Key.bounds);
            if (beforeTab != null && beforeRight != null) beforeTab.rightNeighborID = beforeRight.Restore(beforeTab.rightNeighborID);
            if (afterTab != null && afterLeft != null) afterTab.leftNeighborID = afterLeft.Restore(afterTab.leftNeighborID);
            foreach (IClickableMenu p in menu.pages) p.allClickableComponents?.Remove(tab!);
            int ownIndex = page == null ? -1 : MenuOwnership.IndexOfReference(menu.pages, page);
            if (ownIndex >= 0)
            {
                IClickableMenu? current = CurrentPage(menu);
                IClickableMenu? previous = PageAt(menu, menu.lastOpenedNonMapTab);
                IClickableMenu? fallback = beforeTab == null ? null : ResolvePage(menu, beforeTab);
                menu.pages.RemoveAll(p => ReferenceEquals(p, page));
                menu.currentTab = RemainingIndex(current == page ? fallback : current);
                menu.lastOpenedNonMapTab = RemainingIndex(previous == page ? fallback : previous);
            }
        }
        if (menu != null && CurrentPage(menu) == null)
        {
            if (menu.pages.Count > 0) menu.currentTab = 0;
            else if (IsActive) Game1.exitActiveMenu();
        }
        if (menu != null && PageAt(menu, menu.lastOpenedNonMapTab) == null)
            menu.lastOpenedNonMapTab = menu.currentTab;
        page?.ReleaseResources();
        menu = null; page = null; tab = null; beforeTab = null; afterTab = null; lastPage = null; icon = null;
        beforeRight = null; afterLeft = null; boundsChanges.Clear(); resumeAfterResize = false; resumeAfterMap = false;
        consumedLeftTrigger = false; consumedRightTrigger = false;
    }

    private int RemainingIndex(IClickableMenu? target)
        => target == null ? 0 : Math.Max(0, MenuOwnership.IndexOfReference(menu!.pages, target));

    public void BeforeUpdate()
    {
        // Another host (including Better Game Menu) owns input as soon as the
        // active root changes, even before SMAPI delivers MenuChanged.
        if (!IsActive) return;
        GamePadState state = Game1.input.GetGamePadState();
        bool right = TriggerEdge(state, Buttons.RightTrigger, SButton.RightTrigger, ref consumedRightTrigger);
        bool left = TriggerEdge(state, Buttons.LeftTrigger, SButton.LeftTrigger, ref consumedLeftTrigger);
        if (menu == null || tab == null || beforeTab == null || afterTab == null || !IsActive || !ValidateMapping()) return;
        if (!CanInteractWithTabs || !Game1.options.gamepadControls) return;
        ClickableComponent? current = CurrentTabComponent();
        bool entering = ReferenceEquals(current, beforeTab) && right || ReferenceEquals(current, afterTab) && left;
        if (!IsSelected && !entering) return;
        // Match the game's digital edges, not ButtonPressed's analog threshold.
        // Consume the owned transition once, including a partial analog release.
        if (right) { helper.Input.Suppress(SButton.RightTrigger); consumedRightTrigger = true; }
        if (left) { helper.Input.Suppress(SButton.LeftTrigger); consumedLeftTrigger = true; }
        if (right == left || GameMenu.forcePreventClose || !menu.readyToClose()) return;
        if (IsSelected) ChangeToNative(right ? afterTab : beforeTab);
        else Open();
    }

    private bool TriggerEdge(GamePadState state, Buttons native, SButton button, ref bool consumed)
    {
        if (consumed)
        {
            if (helper.Input.IsSuppressed(button)) return false;
            // SMAPI can release its override below 0.2 while the game's native
            // digital flag is still down. Keep ownership until that flag releases.
            if (state.IsButtonDown(native)) { helper.Input.Suppress(button); return false; }
            consumed = false;
        }
        return !helper.Input.IsSuppressed(button) && state.IsButtonDown(native) && Game1.oldPadState.IsButtonUp(native);
    }

    public void UpdateNavigation(bool tick = false)
    {
        if (tick && resumeAfterMap)
        {
            resumeAfterMap = false;
            if (Game1.activeClickableMenu is GameMenu next && next.currentTab != GameMenu.mapTab)
            { Attach(next); Open(false); }
        }
        if (menu == null || tab == null || !IsActive || !ValidateMapping()) return;
        IClickableMenu? current = CurrentPage(menu);
        if (current == null || ReferenceEquals(current, lastPage) && current.allClickableComponents?.Contains(tab) == true) return;
        lastPage = current;
        current.allClickableComponents ??= new();
        if (Components(menu).Any(c => !ReferenceEquals(c, tab) && c.myID == tabId))
        { tabId = MenuOwnership.AvailableId(Components(menu).Select(c => c.myID), PreferredTabId); tab.myID = tabId; }
        if (!current.allClickableComponents.Contains(tab)) current.allClickableComponents.Add(tab);
        if (beforeRight == null && beforeTab!.rightNeighborID == afterTab!.myID)
        { beforeRight = new(beforeTab.rightNeighborID, tabId); beforeTab.rightNeighborID = tabId; }
        else if (beforeRight?.TryUpdate(beforeTab!.rightNeighborID, tabId) == true) beforeTab.rightNeighborID = tabId;
        if (afterLeft == null && afterTab!.leftNeighborID == beforeTab!.myID)
        { afterLeft = new(afterTab.leftNeighborID, tabId); afterTab.leftNeighborID = tabId; }
        else if (afterLeft?.TryUpdate(afterTab!.leftNeighborID, tabId) == true) afterTab.leftNeighborID = tabId;
    }

    public void Input(ButtonPressedEventArgs e)
    {
        if (menu == null || page == null || tab == null || !IsActive || helper.Input.IsSuppressed(e.Button)) return;
        // MapPage can reconstruct GameMenu from lastOpenedNonMapTab. Avoid passing
        // our appended index to that native constructor, then restore our page.
        if ((e.Button is SButton.MouseLeft or SButton.ControllerA) && !GameMenu.forcePreventClose
            && menu.GetChildMenu() == null && menu.readyToClose() && menu.currentTab == GameMenu.mapTab
            && menu.lastOpenedNonMapTab == MenuOwnership.IndexOfReference(menu.pages, page))
        {
            resumeAfterMap = true;
            menu.lastOpenedNonMapTab = 0;
        }
        if (!CanInteractWithTabs || GameMenu.forcePreventClose || !ValidateMapping()) return;
        Vector2 point = e.Cursor.ScreenPixels;
        bool cursorOnTab = tab.bounds.Contains((int)point.X, (int)point.Y);
        bool select = e.Button == SButton.MouseLeft && cursorOnTab
            || e.Button == SButton.ControllerA && (Game1.options.SnappyMenus
                ? ReferenceEquals(CurrentPage(menu)?.getCurrentlySnappedComponent(), tab) : cursorOnTab);
        if (select) { helper.Input.Suppress(e.Button); if (menu.readyToClose()) Open(); }
    }

    private void Open(bool playSound = true)
    {
        if (menu == null || page == null || tab == null || IsSelected || !CanInteractWithTabs
            || GameMenu.forcePreventClose || !menu.readyToClose() || !ValidateMapping()) return;
        // Only our own page index is stored in native selection. No neighbouring
        // page is instantiated, selected, or required to have a particular class.
        int index = MenuOwnership.IndexOfReference(menu.pages, page);
        menu.currentTab = index; menu.lastOpenedNonMapTab = index;
        menu.initializeUpperRightCloseButton(); menu.invisible = false;
        // With a native GameMenu root, populateClickableComponentList adds native tabs.
        page.Open(); page.populateClickableComponentList();
        lastPage = null; UpdateNavigation(); page.currentlySnappedComponent = tab;
        if (Game1.options.SnappyMenus) page.snapCursorToCurrentSnappedComponent();
        if (playSound) Game1.playSound("smallSelect");
    }

    private void ChangeToNative(ClickableComponent target)
    {
        if (menu == null) return;
        int index = MenuOwnership.IndexOfReference(menu.tabs, target);
        if (index < 0 || ResolvePage(menu, target) == null || ReferenceEquals(ResolvePage(menu, target), page)
            || menu.tabs.Count(t => t.name == target.name) != 1)
        { Unavailable("target-mapping-invalid", $"tab={LogText(target.name)}; tabIndex={index}"); return; }
        // Keep the native hook: lazy menu providers activate their own target here.
        menu.changeTab(index, true);
        lastPage = null; UpdateNavigation();
    }

    public void BeforeWindowResize()
    {
        if (menu == null || !IsActive || !IsSelected) return;
        resumeAfterResize = true;
        // Game1 rebuilds GameMenu using currentTab before custom pages are appended.
        // This is a numeric fallback only: don't activate or inspect its page type.
        menu.currentTab = 0;
        menu.lastOpenedNonMapTab = 0;
    }

    public void AfterWindowResize()
    {
        if (!resumeAfterResize) return;
        if (Game1.activeClickableMenu is not GameMenu next) { Detach(); return; }
        Attach(next); resumeAfterResize = false; Open(false);
    }

    private static IClickableMenu? PageAt(GameMenu? menu, int index)
        => menu != null && index >= 0 && index < menu.pages.Count ? menu.pages[index] : null;
    private static IClickableMenu? CurrentPage(GameMenu? menu) => PageAt(menu, menu?.currentTab ?? -1);
    private static IClickableMenu? ResolvePage(GameMenu menu, ClickableComponent tab)
        => PageAt(menu, menu.getTabNumberFromName(tab.name));
    private ClickableComponent? CurrentTabComponent()
    {
        if (menu == null) return null;
        var matches = menu.tabs.Where(t => menu.getTabNumberFromName(t.name) == menu.currentTab).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }
    private static IEnumerable<ClickableComponent> Components(GameMenu menu)
        => menu.tabs.Concat(menu.pages.SelectMany(p => p.allClickableComponents ?? Enumerable.Empty<ClickableComponent>()));
    private static bool SameRow(Rectangle a, Rectangle b) => a.Top < b.Bottom && a.Bottom > b.Top;

    private bool ValidateMapping()
    {
        if (menu == null || page == null || tab == null || beforeTab == null || afterTab == null) return false;
        if (menu.pages.Count(p => ReferenceEquals(p, page)) != 1)
            return RejectMapping("progress-page-invalid", "The owned Progress page is missing or duplicated.");
        if (!menu.tabs.Contains(beforeTab) || !menu.tabs.Contains(afterTab))
            return RejectMapping("anchor-tab-missing", "A neighbouring tab component was removed.");
        if (menu.tabs.Count <= DisplayIndex || !ReferenceEquals(menu.tabs[DisplayIndex - 1], beforeTab)
            || !ReferenceEquals(menu.tabs[DisplayIndex], afterTab))
            return RejectMapping("tab-order-changed", "The native tab order changed after reserving display slot 2.");
        int ownIndex = MenuOwnership.IndexOfReference(menu.pages, page);
        if (menu.tabs.Any(t => menu.getTabNumberFromName(t.name) == ownIndex))
            return RejectMapping("progress-mapping-collision", "A native tab now maps to the owned Progress page.");
        if (CurrentPage(menu) == null)
            return RejectMapping("current-page-invalid", $"currentTab={menu.currentTab}; pages={menu.pages.Count}");
        foreach (ClickableComponent other in menu.tabs)
            if (other.bounds.Intersects(tab.bounds))
                return RejectMapping("tab-overlap", $"tab={LogText(other.name)}; bounds={other.bounds}; entryBounds={tab.bounds}");
        return true;
    }

    private bool RejectMapping(string reason, string detail) { Unavailable(reason, detail); return false; }
    private void Unavailable(string reason, string detail)
    {
        monitor.Log($"Production menu entry detached. Reason={reason}; {detail}\n{DescribeMenuState()}\nPlease include this full SMAPI log when reporting the missing Progress tab.", LogLevel.Warn);
        Detach();
    }
    internal static string DescribePage(IClickableMenu? value)
    {
        if (value == null) return "null";
        Type type = value.GetType(); var assembly = type.Assembly.GetName();
        return $"{LogText(type.FullName)}#{RuntimeHelpers.GetHashCode(value):x} [{LogText(assembly.Name)} {assembly.Version}]";
    }
    private static string LogText(string? value)
    {
        string text = (value ?? "null").Replace('\r', ' ').Replace('\n', ' ');
        return text.Length <= 160 ? text : text[..160] + "...";
    }
    private string DescribeMenuState()
    {
        if (menu == null) return "Menu state: no attached GameMenu.";
        const int limit = 24;
        return $"Menu state: root={DescribePage(Game1.activeClickableMenu)}; currentTab={menu.currentTab}; progressSelected={IsSelected}; displayIndex={DisplayIndex}; ownPageIndex={(page == null ? -1 : MenuOwnership.IndexOfReference(menu.pages, page))}; entryBounds={tab?.bounds}\n"
            + $"Tabs ({menu.tabs.Count}, first {limit} at most): " + string.Join("; ", menu.tabs.Take(limit).Select((t,i) => $"{i}:{LogText(t.name)} id={t.myID} pageIndex={menu.getTabNumberFromName(t.name)} bounds={t.bounds}"))
            + $"\nPages ({menu.pages.Count}, first {limit} at most): " + string.Join("; ", menu.pages.Take(limit).Select((p,i) => $"{i}:{DescribePage(p)}"));
    }

    public void BeforeDraw(SpriteBatch b)
    {
        // Recover even if a previous draw was interrupted before RenderedActiveMenu.
        RestoreNativeHoverText();
        if (menu == null || tab == null || icon == null || !IsActive || menu.invisible) return;
        if (!ValidateMapping()) return;
        DrawTab(b);
        // A native tooltip may overlap our foreground tab. Defer only that tooltip,
        // then restore it before drawing it once above the tab. Modal draws stay native.
        if (CanInteractWithTabs && !string.IsNullOrEmpty(menu.hoverText))
        {
            hoverOwner = menu;
            suppressedHoverText = menu.hoverText;
            menu.hoverText = "";
        }
    }

    public void AfterDraw(SpriteBatch b)
    {
        RestoreNativeHoverText();
        if (menu == null || tab == null || icon == null || !CanInteractWithTabs) return;
        if (!ValidateMapping()) return;
        Rectangle visualBounds = DrawTab(b);
        if (!string.IsNullOrEmpty(menu.hoverText))
            IClickableMenu.drawHoverText(b, menu.hoverText, Game1.smallFont);
        Vector2 mouse = helper.Input.GetCursorPosition().ScreenPixels;
        if (visualBounds.Contains((int)mouse.X, (int)mouse.Y))
            IClickableMenu.drawHoverText(b, helper.Translation.Get("title"), Game1.smallFont);
        // Only the foreground pass needs a cursor after it. Letters and child menus
        // retain their native cursor draw and cover the earlier background tab.
        menu.drawMouse(b, true);
    }

    private void RestoreNativeHoverText()
    {
        // Preserve a newer value supplied by the game or another mod.
        if (hoverOwner != null && hoverOwner.hoverText == "")
            hoverOwner.hoverText = suppressedHoverText ?? "";
        hoverOwner = null;
        suppressedHoverText = null;
    }

    private Rectangle DrawTab(SpriteBatch b)
    {
        bool selected = IsSelected;
        float scale = tab!.bounds.Width / 64f;
        int y = tab.bounds.Y + (selected ? (int)(8 * scale) : 0);
        Rectangle Part(int x, int top, int width, int height) => new(tab.bounds.X + (int)(x * scale),
            y + (int)(top * scale), Math.Max(1, (int)(width * scale)), Math.Max(1, (int)(height * scale)));
        Rectangle visualBounds = Part(0, 0, 64, 64);
        // Native skills-tab background is the blank portrait tab at (16,368).
        b.Draw(Game1.mouseCursors, visualBounds, new Rectangle(16, 368, 16, 16), Color.White);
        // Native tab artwork is centred around y=40 in the 64px tile, below its top rim.
        // Keep the frame's original selected offset; only align the icon inside the frame.
        b.Draw(icon!.GetTexture(), Part(16, 24, 32, 32), icon.GetSourceRect(0, null), Color.White);
        return visualBounds;
    }
}
