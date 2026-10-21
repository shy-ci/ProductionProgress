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
    private int skillsPageIndex, socialPageIndex, beforePageIndex;
    private readonly TabEntryTransition<ClickableComponent> tabEntry = new();
    private bool resumeAfterResize;
    private int resumeTicks;
    private ParsedItemData? icon;
    private GameMenu? hoverOwner;
    private string? suppressedHoverText;
    private bool IsSelected => productionMenu != null && ReferenceEquals(Game1.activeClickableMenu, productionMenu);
    private bool IsActive => ReferenceEquals(Game1.activeClickableMenu, menu) || IsSelected;
    // Modal content blocks input and the foreground pass, but never the background
    // pass. A collections letter is drawn inside its page, not as GameMenu's child.
    private bool CanInteractWithTabs => menu != null && IsActive && !menu.invisible
        && menu.GetChildMenu() == null && productionMenu?.GetChildMenu() == null
        && (IsSelected ? page : CurrentPage(menu))?.GetChildMenu() == null
        && CurrentPage(menu) is not CollectionsPage { letterviewerSubMenu: not null };

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
        skillsPageIndex = menu.getTabNumberFromName(skills.name);
        socialPageIndex = menu.getTabNumberFromName(social.name);
        beforePageIndex = menu.getTabNumberFromName(beforeTab.name);
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
        RestoreNativeHoverText();
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
        skillsPageIndex = socialPageIndex = beforePageIndex = -1;
        tabEntry.Reset();
    }

    public void BeforeUpdate()
    {
        tabEntry.Reset();
        if (menu == null || beforeTab == null || social == null || IsSelected || !CanInteractWithTabs
            || GameMenu.forcePreventClose
            || !Game1.options.gamepadControls) return;
        if (!ValidateMapping()) return;
        // Match Game1.updateActiveMenu/ButtonCollection's digital edges, including
        // any input already suppressed by other mods. Do not use SMAPI's 0.2 threshold.
        GamePadState state = Game1.input.GetGamePadState();
        // Tab identity survives a lazy proxy being materialized during changeTab.
        tabEntry.Capture(TabForPage(CurrentPage(menu)), beforeTab, social,
            state.IsButtonDown(Buttons.RightTrigger) && Game1.oldPadState.IsButtonUp(Buttons.RightTrigger),
            state.IsButtonDown(Buttons.LeftTrigger) && Game1.oldPadState.IsButtonUp(Buttons.LeftTrigger));
    }

    public void UpdateNavigation(bool tick = false)
    {
        if (menu == null || tab == null || !IsActive) return;
        if (!ValidateMapping()) return;
        if (tick && tabEntry.Complete(IsSelected ? null : TabForPage(CurrentPage(menu))))
        {
            Open(false); return; // the native transition has already played its selection sound
        }
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
        if (menu == null || page == null || tab == null || !CanInteractWithTabs || GameMenu.forcePreventClose) return;
        if (helper.Input.IsSuppressed(e.Button)) return;
        if (!ValidateMapping()) return;
        Vector2 point = e.Cursor.ScreenPixels;
        bool cursorOnTab = tab.bounds.Contains((int)point.X, (int)point.Y);
        IClickableMenu? focusedPage = IsSelected ? page : CurrentPage(menu);
        // In snappy mode the focused component is authoritative, not the mouse
        // position sampled before the game's controller cursor movement.
        bool select = !menu.invisible && ((e.Button == SButton.MouseLeft && cursorOnTab)
            || (e.Button == SButton.ControllerA && (Game1.options.SnappyMenus
                ? ReferenceEquals(focusedPage?.getCurrentlySnappedComponent(), tab) : cursorOnTab)));
        if (select)
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
        if (menu == null || page == null || tab == null || IsSelected || !CanInteractWithTabs || GameMenu.forcePreventClose) return;
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
        ClickableComponent? targetTab = TabForPage(target);
        if (targetTab == null) return false;
        int tabPosition = MenuOwnership.IndexOfReference(menu.tabs, targetTab);
        menu.changeTab(tabPosition, sound);
        // Ultra Smooth can replace target's proxy before native changeTab runs.
        // Validate that replacement, then check the same tab's current page.
        return ValidateMapping() && ReferenceEquals(CurrentPage(menu), ResolvePage(menu, targetTab));
    }

    private ClickableComponent? TabForPage(IClickableMenu? target)
    {
        if (menu == null || target == null) return null;
        ClickableComponent? match = null;
        foreach (ClickableComponent candidate in menu.tabs)
        {
            if (!ReferenceEquals(ResolvePage(menu, candidate), target)) continue;
            if (match != null) return null;
            match = candidate;
        }
        return match;
    }

    private void Unavailable(string reason, string detail)
    {
        bool selected = IsSelected;
        // Capture before Detach restores our bounds and removes the page. This runs
        // only on failure, once for this attachment, not on every draw/update.
        monitor.Log($"Production menu entry detached. Reason={reason}; {detail}\n{DescribeMenuState()}\nPlease include this full SMAPI log when reporting the missing Progress tab.", LogLevel.Warn);
        Detach();
        if (selected) Game1.exitActiveMenu();
    }

    internal static string DescribePage(IClickableMenu? value)
    {
        if (value == null) return "null";
        Type type = value.GetType();
        var assembly = type.Assembly.GetName();
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
        string tabs = string.Join("; ", menu.tabs.Take(limit).Select((t, i) =>
            $"{i}:{LogText(t.name)} id={t.myID} bounds={t.bounds}"
            + (boundsChanges.TryGetValue(t, out var owned) && !owned.Owns(t.bounds)
                ? $" (last-applied={owned.Applied}, before-attach={owned.Original})" : "")));
        string pages = string.Join("; ", menu.pages.Take(limit).Select((p, i) => $"{i}:{DescribePage(p)}"));
        return $"Menu state: root={DescribePage(Game1.activeClickableMenu)}; native={DescribePage(menu)}; currentTab={menu.currentTab}; progressSelected={IsSelected}; entryBounds={tab?.bounds}\n"
            + $"Expected pages: progress={DescribePage(page)}; before={DescribePage(beforePage)}; skills={DescribePage(skillsPage)}; social={DescribePage(socialPage)}\n"
            + $"Tabs ({menu.tabs.Count}, first {limit} at most): {tabs}\nPages ({menu.pages.Count}, first {limit} at most): {pages}";
    }

    private bool RejectMapping(string reason, string detail)
    {
        Unavailable(reason, detail);
        return false;
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
        if (MenuOwnership.IndexOfReference(menu.pages, page) < 0)
            return RejectMapping("progress-page-missing", "The attached Progress page is no longer in GameMenu.pages.");
        // Check tab membership first, preserving the original protection order.
        if (MenuOwnership.IndexOfReference(menu.tabs, skills) < 0)
            return RejectMapping("anchor-tab-missing", $"anchor=skills; name={LogText(skills.name)}");
        if (MenuOwnership.IndexOfReference(menu.tabs, social) < 0)
            return RejectMapping("anchor-tab-missing", $"anchor=social; name={LogText(social.name)}");
        if (MenuOwnership.IndexOfReference(menu.tabs, beforeTab) < 0)
            return RejectMapping("anchor-tab-missing", $"anchor=before; name={LogText(beforeTab.name)}");
        if (!ValidateAnchor("before", beforeTab, beforePage, beforePageIndex, out var nextBefore, out int beforeIndex)
            || !ValidateAnchor("skills", skills, skillsPage, skillsPageIndex, out var nextSkills, out int skillsIndex)
            || !ValidateAnchor("social", social, socialPage, socialPageIndex, out var nextSocial, out int socialIndex)) return false;
        foreach (ClickableComponent other in menu.tabs)
        {
            if (other.bounds.Intersects(tab.bounds))
                return RejectMapping("tab-overlap", $"tab={LogText(other.name)}; otherBounds={other.bounds}; entryBounds={tab.bounds}");
        }
        // Commit refreshed anchors only after every mapping and layout check passes.
        if (!ReferenceEquals(beforePage, nextBefore) || !ReferenceEquals(skillsPage, nextSkills)
            || !ReferenceEquals(socialPage, nextSocial))
        {
            if (!ReferenceEquals(beforePage, nextBefore)) beforePage?.allClickableComponents?.Remove(tab);
            if (!ReferenceEquals(skillsPage, nextSkills)) skillsPage?.allClickableComponents?.Remove(tab);
            if (!ReferenceEquals(socialPage, nextSocial)) socialPage?.allClickableComponents?.Remove(tab);
            lastPage = null;
        }
        beforePage = nextBefore; beforePageIndex = beforeIndex;
        skillsPage = nextSkills; skillsPageIndex = skillsIndex;
        socialPage = nextSocial; socialPageIndex = socialIndex;
        return true;
    }

    private bool ValidateAnchor(string anchor, ClickableComponent component, IClickableMenu? expected,
        int previousIndex, out IClickableMenu? actual, out int index)
    {
        index = menu!.getTabNumberFromName(component.name);
        actual = index >= 0 && index < menu.pages.Count ? menu.pages[index] : null;
        if (ReferenceEquals(actual, expected)) return true;
        if (IsUltraSmoothMaterialization(component, expected, actual, previousIndex, index)) return true;
        return RejectMapping(actual == null ? "anchor-mapping-invalid" : "anchor-page-changed",
            $"anchor={anchor}; name={LogText(component.name)}; previousIndex={previousIndex}; mappedIndex={index}; expected={DescribePage(expected)}; actual={DescribePage(actual)}");
    }

    private bool IsUltraSmoothMaterialization(ClickableComponent component, IClickableMenu? expected,
        IClickableMenu? actual, int previousIndex, int index)
    {
        // Optional interoperability with the 2.5.0 proxy lifecycle, not a binary/API
        // dependency. Never invoke its private factory or disable its configuration.
        if (expected == null || actual == null || index != previousIndex) return false;
        Type type = expected.GetType();
        if (type.FullName != "UltraSmooth.Optimizers.SmoothTabProxy" || type.Assembly.GetName().Name != "UltraSmooth") return false;
        bool correctPage = component.name switch
        {
            "skills" => actual is SkillsPage,
            "social" => actual is SocialPage,
            _ => false
        };
        return correctPage && ReferenceEquals(UniqueTab(menu!, component.name), component)
            && MenuOwnership.IndexOfReference(menu!.pages, expected) < 0
            && menu.pages.Count(p => ReferenceEquals(p, actual)) == 1;
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
