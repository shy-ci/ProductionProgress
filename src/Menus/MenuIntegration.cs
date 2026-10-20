using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.ItemTypeDefinitions;
using StardewValley.Menus;
using MossHarbor.ProductionProgress.Core;

namespace MossHarbor.ProductionProgress.Menus;

internal sealed class MenuIntegration
{
    private const int PreferredTabId = 876410;
    private int tabId;
    private readonly IModHelper helper;
    private readonly Func<IReadOnlyCollection<ProgressRow>> rows;
    private readonly Func<long> revision;
    private readonly Func<string?> status;
    private readonly Action opened;
    private readonly IMonitor monitor;
    private GameMenu? menu;
    private ProgressPage? page;
    private ProductionMenu? productionMenu;
    private ClickableComponent? tab, skills, social, beforeTab;
    private readonly Dictionary<ClickableComponent, OwnedValue<Rectangle>> boundsChanges = new();
    private OwnedValue<int>? beforeRight, socialLeft;
    private IClickableMenu? skillsPage, socialPage, beforePage, lastPage;
    private bool resumeAfterResize;
    private int resumeTicks;
    private ParsedItemData? icon;
    private bool IsSelected => productionMenu != null && ReferenceEquals(Game1.activeClickableMenu, productionMenu);
    private bool IsActive => ReferenceEquals(Game1.activeClickableMenu, menu) || IsSelected;

    public MenuIntegration(IModHelper helper, Func<IReadOnlyCollection<ProgressRow>> rows,
        Func<long> revision, Func<string?> status, Action opened, IMonitor monitor)
    { this.helper = helper; this.rows = rows; this.revision = revision; this.status = status; this.opened = opened; this.monitor = monitor; }

    public void MenuChanged(IClickableMenu? next)
    {
        if (ReferenceEquals(next, productionMenu) && productionMenu != null) return;
        if (next is GameMenu gameMenu) Attach(gameMenu);
        else Detach();
    }

    public void Attach(GameMenu gameMenu)
    {
        if (ReferenceEquals(menu, gameMenu)) return;
        bool resume = resumeAfterResize;
        Detach();
        skills = UniqueTab(gameMenu, "skills");
        social = UniqueTab(gameMenu, "social");
        if (skills == null || social == null) return;
        skillsPage = ResolvePage(gameMenu, skills);
        socialPage = ResolvePage(gameMenu, social);
        if (skillsPage == null || socialPage == null || ReferenceEquals(skillsPage, socialPage)) return;
        menu = gameMenu;
        Rectangle position = social.bounds;
        if (position.Width <= 0 || position.Height <= 0) { Detach(); return; }
        beforeTab = menu.tabs.Where(t => t.bounds.Right <= position.Left && SameRow(t.bounds, position))
            .OrderByDescending(t => t.bounds.Right).FirstOrDefault();
        beforePage = beforeTab == null ? null : ResolvePage(menu, beforeTab);
        if (beforeTab == null || beforePage == null) { Detach(); return; }
        int previousRight = beforeTab.bounds.Right;
        int step = position.Width + Math.Max(0, position.Left - previousRight);
        foreach (ClickableComponent item in menu.tabs)
        {
            if (item.bounds.X >= position.X && SameRow(item.bounds, position))
            {
                Rectangle shifted = new(item.bounds.X + step, item.bounds.Y, item.bounds.Width, item.bounds.Height);
                boundsChanges[item] = new OwnedValue<Rectangle>(item.bounds, shifted);
                item.bounds = shifted;
            }
        }
        tabId = MenuOwnership.AvailableId(Components(menu).Select(c => c.myID), PreferredTabId);
        tab = new ClickableComponent(position, "MossHarbor.ProductionProgress", helper.Translation.Get("title"))
        { myID = tabId, leftNeighborID = beforeTab.myID, rightNeighborID = social.myID, downNeighborID = -99998,
            tryDefaultIfNoDownNeighborExists = true, fullyImmutable = true };
        page = new ProgressPage(menu, helper.Translation, rows, revision, status, opened);
        menu.pages.Add(page); // native page numbers and the native tabs list remain unchanged
        icon = ItemRegistry.GetDataOrErrorItem("(O)262");
        resumeAfterResize = resume; resumeTicks = 2;
        lastPage = null; UpdateNavigation();
    }

    public void Detach()
    {
        if (menu != null)
        {
            foreach (var pair in boundsChanges) pair.Key.bounds = pair.Value.Restore(pair.Key.bounds);
            if (beforeTab != null && beforeRight != null) beforeTab.rightNeighborID = beforeRight.Restore(beforeTab.rightNeighborID);
            if (social != null && socialLeft != null) social.leftNeighborID = socialLeft.Restore(social.leftNeighborID);
            foreach (IClickableMenu p in menu.pages) p.allClickableComponents?.Remove(tab!);
            IClickableMenu? selected = CurrentPage(menu);
            int ownIndex = page == null ? -1 : MenuOwnership.IndexOfReference(menu.pages, page);
            if (ownIndex >= 0)
            {
                menu.pages.RemoveAt(ownIndex);
                int next = selected == page ? (skillsPage == null ? -1 : MenuOwnership.IndexOfReference(menu.pages, skillsPage))
                    : selected == null ? -1 : MenuOwnership.IndexOfReference(menu.pages, selected);
                if (next >= 0) menu.currentTab = next;
            }
        }
        page?.ReleaseResources();
        menu = null; page = null; productionMenu = null; tab = null; skills = null; social = null; beforeTab = null; icon = null;
        boundsChanges.Clear(); beforeRight = null; socialLeft = null;
        skillsPage = null; socialPage = null; beforePage = null; lastPage = null; resumeAfterResize = false;
    }

    public void UpdateNavigation(bool tick = false)
    {
        if (menu == null || tab == null || !IsActive) return;
        if (!ValidateMapping()) return;
        if (tick && resumeAfterResize && --resumeTicks <= 0)
        {
            resumeAfterResize = false; Open(false); return;
        }
        IClickableMenu? current = IsSelected ? page : CurrentPage(menu);
        if (current == null || ReferenceEquals(current, lastPage)) return;
        lastPage = current;
        current.allClickableComponents ??= new List<ClickableComponent>();
        if (Components(menu).Any(c => !ReferenceEquals(c, tab) && c.myID == tabId))
        {
            tabId = MenuOwnership.AvailableId(Components(menu).Select(c => c.myID), PreferredTabId);
            tab.myID = tabId;
        }
        if (!current.allClickableComponents.Contains(tab)) current.allClickableComponents.Add(tab);
        if (beforeRight == null && beforeTab!.rightNeighborID == social!.myID)
        { beforeRight = new(beforeTab.rightNeighborID, tabId); beforeTab.rightNeighborID = tabId; }
        else if (beforeRight?.TryUpdate(beforeTab!.rightNeighborID, tabId) == true) beforeTab.rightNeighborID = tabId;
        if (socialLeft == null && social!.leftNeighborID == beforeTab!.myID)
        { socialLeft = new(social.leftNeighborID, tabId); social.leftNeighborID = tabId; }
        else if (socialLeft?.TryUpdate(social!.leftNeighborID, tabId) == true) social.leftNeighborID = tabId;
    }

    public void Input(ButtonPressedEventArgs e)
    {
        if (menu == null || page == null || tab == null || !IsActive
            || menu.GetChildMenu() != null || GameMenu.forcePreventClose) return;
        if (helper.Input.IsSuppressed(e.Button)) return;
        if (!ValidateMapping()) return;
        Vector2 point = e.Cursor.ScreenPixels;
        bool select = !menu.invisible && (e.Button is SButton.MouseLeft or SButton.ControllerA) && tab.bounds.Contains((int)point.X, (int)point.Y);
        bool right = e.Button == SButton.RightTrigger, left = e.Button == SButton.LeftTrigger;
        if (IsSelected && (left || right))
        {
            helper.Input.Suppress(e.Button);
            SelectNative(left ? beforePage : socialPage);
        }
        else if (select
            || (!IsSelected && right && beforeRight?.Owns(beforeTab!.rightNeighborID) == true && ReferenceEquals(CurrentPage(menu), beforePage))
            || (!IsSelected && left && socialLeft?.Owns(social!.leftNeighborID) == true && ReferenceEquals(CurrentPage(menu), socialPage)))
        {
            helper.Input.Suppress(e.Button);
            if (!menu.readyToClose()) return;
            Open();
        }
        else if (IsSelected && (e.Button is SButton.DPadDown or SButton.DPadUp))
        {
            helper.Input.Suppress(e.Button); page.Scroll(e.Button == SButton.DPadDown ? 1 : -1);
        }
    }

    private void Open(bool playSound = true)
    {
        if (menu == null || page == null || tab == null || IsSelected) return;
        if (!ValidateMapping()) return;
        // Keep a valid native index outside the bounded draw/input forwarding calls.
        if (!ChangeToPage(skillsPage, false)) return;
        productionMenu = new ProductionMenu(menu, page, SelectNative,
            right => SelectNative(right ? socialPage : beforePage), Resize, Unavailable);
        Game1.activeClickableMenu = productionMenu;
        page.Open(); page.populateClickableComponentList(); menu.AddTabsToClickableComponents(page);
        lastPage = null; UpdateNavigation();
        page.currentlySnappedComponent = tab;
        if (Game1.options.SnappyMenus) page.snapCursorToCurrentSnappedComponent();
        if (playSound) Game1.playSound("smallSelect");
    }

    private void SelectNative(IClickableMenu? target)
    {
        if (menu == null) return;
        if (!ChangeToPage(target, true)) return;
        productionMenu = null;
        Game1.activeClickableMenu = menu;
        lastPage = null; UpdateNavigation();
    }

    private void Resize()
    {
        if (menu == null) return;
        // SetWindowSize will now rebuild its native menu normally. Let other mods observe
        // MenuChanged and adjust the replacement before trying to reopen our page.
        productionMenu = null; resumeAfterResize = true; resumeTicks = 2;
        Game1.activeClickableMenu = menu;
    }

    private bool ChangeToPage(IClickableMenu? target, bool sound)
    {
        if (menu == null || target == null) return false;
        // changeTab takes a tab-list position, then maps its name to a page internally.
        // The tab position and the page position are not interchangeable.
        int pagePosition = MenuOwnership.IndexOfReference(menu.pages, target);
        if (pagePosition < 0) return false;
        var matches = menu.tabs.Where(t => ReferenceEquals(ResolvePage(menu, t), target)).ToArray();
        if (matches.Length != 1) return false;
        int tabPosition = MenuOwnership.IndexOfReference(menu.tabs, matches[0]);
        menu.changeTab(tabPosition, sound);
        return ReferenceEquals(CurrentPage(menu), target);
    }

    private void Unavailable()
    {
        bool selected = IsSelected;
        monitor.Log("Production menu mapping or layout changed; detaching this entry to avoid using a stale index or overlapping a registered tab.", LogLevel.Warn);
        Detach();
        if (selected) Game1.exitActiveMenu();
    }

    private static ClickableComponent? UniqueTab(GameMenu menu, string name)
    {
        var matches = menu.tabs.Where(t => t.name == name).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }
    private static IClickableMenu? CurrentPage(GameMenu menu)
        => menu.currentTab >= 0 && menu.currentTab < menu.pages.Count ? menu.pages[menu.currentTab] : null;
    private static IClickableMenu? ResolvePage(GameMenu menu, ClickableComponent tab)
    {
        int index = menu.getTabNumberFromName(tab.name);
        return index >= 0 && index < menu.pages.Count ? menu.pages[index] : null;
    }
    private static IEnumerable<ClickableComponent> Components(GameMenu menu)
        => menu.tabs.Concat(menu.pages.SelectMany(p => p.allClickableComponents ?? Enumerable.Empty<ClickableComponent>()));
    private static bool SameRow(Rectangle a, Rectangle b) => a.Top < b.Bottom && a.Bottom > b.Top;

    private bool ValidateMapping()
    {
        if (menu == null || page == null || tab == null || skills == null || social == null || beforeTab == null) return false;
        if (MenuOwnership.IndexOfReference(menu.pages, page) < 0
            || MenuOwnership.IndexOfReference(menu.tabs, skills) < 0 || MenuOwnership.IndexOfReference(menu.tabs, social) < 0
            || MenuOwnership.IndexOfReference(menu.tabs, beforeTab) < 0 || !ReferenceEquals(ResolvePage(menu, beforeTab), beforePage)
            || !ReferenceEquals(ResolvePage(menu, skills), skillsPage) || !ReferenceEquals(ResolvePage(menu, social), socialPage)
            || menu.tabs.Any(t => t.bounds.Intersects(tab.bounds)))
        {
            Unavailable(); return false;
        }
        return true;
    }

    public void Draw(SpriteBatch b)
    {
        if (menu == null || tab == null || icon == null || !IsActive
            || menu.GetChildMenu() != null || menu.invisible) return;
        if (!ValidateMapping()) return;
        bool selected = IsSelected;
        float scale = tab.bounds.Width / 64f;
        int y = tab.bounds.Y + (selected ? (int)(8 * scale) : 0);
        Rectangle Part(int x, int top, int width, int height) => new(tab.bounds.X + (int)(x * scale),
            y + (int)(top * scale), Math.Max(1, (int)(width * scale)), Math.Max(1, (int)(height * scale)));
        // Native skills-tab background is the blank portrait tab at (16,368).
        b.Draw(Game1.mouseCursors, Part(0, 0, 64, 64), new Rectangle(16, 368, 16, 16), Color.White);
        b.Draw(icon.GetTexture(), Part(16, 16, 32, 32), icon.GetSourceRect(0, null), Color.White);
        Vector2 mouse = helper.Input.GetCursorPosition().ScreenPixels;
        if (!selected && tab.bounds.Contains((int)mouse.X, (int)mouse.Y))
        {
            IClickableMenu.drawHoverText(b, helper.Translation.Get("title"), Game1.smallFont);
            menu.drawMouse(b);
        }
    }
}
