using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;
using MossHarbor.ProductionProgress.Core;
using MossHarbor.ProductionProgress.Menus;

sealed class Helper : IModHelper
{
    public InputHelper Input { get; } = new();
    public TranslationHelper Translation { get; } = new();
}
sealed class Monitor : IMonitor
{
    public readonly List<string> Messages = new();
    public void Log(string text, LogLevel level) => Messages.Add(text);
}
static class Program
{
    static int checks;
    static void Check(bool pass, string message)
    { checks++; if (!pass) throw new Exception(message); }
    static GameMenu Native(int current = 0, int extra = 0)
    {
        var menu = new GameMenu { currentTab = current };
        string[] names = { "inventory", "skills", "social", "map", "crafting", "animals", "powers", "collections", "options", "exit" };
        for (int i = 0; i < names.Length; i++)
        {
            menu.Mappings[names[i]] = i;
            menu.tabs.Add(new(new(100 + i * 64, 16, 64, 64), names[i], names[i])
                { myID = 100 + i, leftNeighborID = 99 + i, rightNeighborID = 101 + i });
            menu.pages.Add(i == 1 ? new SkillsPage() : i == 2 ? new SocialPage() : i == 7 ? new CollectionsPage() : new IClickableMenu());
        }
        for (int i = 0; i < extra; i++) menu.pages.Add(new());
        return menu;
    }
    static (MenuIntegration E, GameMenu M, Helper H, Monitor Log, ProgressPage P) Setup(int current = 0, int extra = 0)
    {
        Game1.options = new(); Game1.input = new(); Game1.oldPadState = new(); GameMenu.forcePreventClose = false;
        var m = Native(current, extra); Game1.activeClickableMenu = m;
        var h = new Helper(); var log = new Monitor();
        var e = new MenuIntegration(h, () => Array.Empty<ProgressRow>(), () => 0, () => null, () => { }, log);
        e.Attach(m);
        return (e, m, h, log, m.pages.OfType<ProgressPage>().Single());
    }
    static ButtonPressedEventArgs Click(SButton b = SButton.MouseLeft) => new()
        { Button = b, Cursor = new() { ScreenPixels = new(250, 40) } };
    static bool Selected(GameMenu m, ProgressPage p) => ReferenceEquals(Game1.activeClickableMenu, m) && ReferenceEquals(m.pages[m.currentTab], p);
    static void Frame(MenuIntegration e, GameMenu m, Helper h, bool left, bool right)
    {
        // SMAPI retains suppression until physical release; native dispatch reads
        // the overridden state using the game's digital trigger edges.
        if (!left) h.Input.Suppressed.Remove(SButton.LeftTrigger);
        if (!right) h.Input.Suppressed.Remove(SButton.RightTrigger);
        var raw = new GamePadState(left && !h.Input.IsSuppressed(SButton.LeftTrigger), right && !h.Input.IsSuppressed(SButton.RightTrigger));
        Game1.input.State = raw; e.BeforeUpdate();
        var effective = new GamePadState(raw.Left && !h.Input.IsSuppressed(SButton.LeftTrigger), raw.Right && !h.Input.IsSuppressed(SButton.RightTrigger));
        if (effective.Right && !Game1.oldPadState.Right) m.changeTab(m.currentTab + 1, false);
        else if (effective.Left && !Game1.oldPadState.Left) m.changeTab(m.currentTab - 1, false);
        Game1.oldPadState = effective; e.UpdateNavigation(tick: true);
    }
    static Type PageType(string assembly, string name)
    {
        var a = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(new(assembly), System.Reflection.Emit.AssemblyBuilderAccess.Run);
        var t = a.DefineDynamicModule(assembly).DefineType(name, System.Reflection.TypeAttributes.Public, typeof(IClickableMenu));
        t.DefineDefaultConstructor(System.Reflection.MethodAttributes.Public); return t.CreateType()!;
    }
    static IClickableMenu New(Type type) => (IClickableMenu)Activator.CreateInstance(type)!;
    static void Main()
    {
        var proxy = PageType("UltraSmooth", "UltraSmooth.Optimizers.SmoothTabProxy");
        var space = PageType("SpaceCore", "SpaceCore.Interface.NewSkillsPage");
        var unknown = PageType("UnlistedMod", "UnlistedMod.CustomSkillsPage");
        // No mod registry or compatibility ID is supplied. Proxy streaming stays
        // enabled, while known and unknown skill replacement classes are tested.
        foreach (int extra in new[] { 0, 1, 3 })
        foreach (int current in new[] { 0, 1, 2, 9 })
        foreach (Type? type in new Type?[] { null, proxy, space, unknown })
        {
            var (e, m, h, log, p) = Setup(current, extra);
            if (type != null) { m.pages[1] = New(type); m.pages[2] = New(proxy); }
            e.UpdateNavigation();
            var skills = m.pages[1]; int activations = 0;
            m.BeforeChangeTab = (menu, index) => { activations++; if (menu.pages[index].GetType() == proxy) menu.pages[index] = index == 1 ? New(space) : new SocialPage(); };
            e.Input(Click());
            Check(Selected(m, p), "native root renders selected own page");
            Check(ReferenceEquals(m.pages[1], skills) && activations == 0, "entry never selects or materializes Skills");
            Check(m.tabs.Count == 10 && m.getTabNumberFromName("social") == 2, "native tab indices unchanged");
            Check(m.tabs[1].bounds.X == 164 && m.tabs[2].bounds.X == 292, "visual slot 2 reserved");
            var b = new SpriteBatch(); e.BeforeDraw(b); m.draw(b); e.AfterDraw(b); m.update(new());
            Check(p.Draws == 1 && p.Updates == 1 && b.Events.Contains("progress-content"), "native dispatch reaches own content once");
            m.pages[1] = New(unknown); m.pages[2] = New(proxy); e.UpdateNavigation();
            Check(Selected(m, p) && log.Messages.Count == 0, "neighbor replacement cannot invalidate own page");
            Game1.options.gamepadControls = true;
            Frame(e, m, h, true, false);
            Check(m.currentTab == 1 && activations == 1, "LT exits through native changeTab");
            Frame(e, m, h, true, false);
            Check(m.currentTab == 1 && activations == 1, "held LT doesn't repeat");
            Frame(e, m, h, false, false); Frame(e, m, h, false, true);
            Check(Selected(m, p) && activations == 1, "RT enters own page without visiting Social");
            Frame(e, m, h, false, true);
            Check(Selected(m, p), "held entry trigger doesn't leave page");
            Frame(e, m, h, false, false); Frame(e, m, h, false, true);
            Check(m.currentTab == 2 && m.pages[2] is SocialPage && activations == 2, "RT activates unbypassed Social proxy via native hook");
            Frame(e, m, h, false, false); Frame(e, m, h, true, false);
            Check(Selected(m, p), "LT returns directly from Social");
            Check(log.Messages.Count == 0, "normal combination is silent");
            e.Detach();
            Check(m.pages.Count == 10 + extra && m.tabs[2].bounds.X == 228 && p.Released, "cleanup restores only owned additions");
        }
        AnalogTriggers(); ModalAndOwnership(); ResizeAndMap(); Protection();
        Console.WriteLine($"PASS: {checks} native-host integration assertions against actual MenuIntegration.cs; recording UI doubles, no game or mod binaries loaded.");
    }
    static void AnalogTriggers()
    {
        foreach (bool left in new[] { false, true })
        {
            var (e,m,h,log,p) = Setup(left ? 2 : 1); Game1.options.gamepadControls = true;
            SButton button = left ? SButton.LeftTrigger : SButton.RightTrigger;
            foreach (float level in new[] { 0f, 0.1f, 0.3f, 1f, 0.3f, 0.1f, 0f })
            {
                // SInputState removes overrides at its own 0.2 threshold. SDL's
                // native digital flags may remain down below that threshold.
                if (level <= 0.2f) h.Input.Suppressed.Remove(button);
                bool raw = level > 0 && !h.Input.IsSuppressed(button);
                Game1.input.State = new(left && raw, !left && raw); e.BeforeUpdate();
                bool effective = raw && !h.Input.IsSuppressed(button);
                if (effective && (left ? !Game1.oldPadState.Left : !Game1.oldPadState.Right))
                    m.changeTab(m.currentTab + (left ? -1 : 1), false);
                Game1.oldPadState = new(left && effective, !left && effective);
                e.UpdateNavigation(tick:true);
                if (level > 0) Check(Selected(m,p), "shallow/deep/partial release cannot skip Progress");
            }
            Frame(e,m,h,left,!left);
            Check(!Selected(m,p) && m.NativeChanges == 1 && log.Messages.Count == 0, "full release permits one next transition");
            e.Detach();
        }
    }
    static void ModalAndOwnership()
    {
        var (e, m, h, log, p) = Setup();
        foreach (bool pageChild in new[] { false, true })
        {
            if (pageChild) m.pages[0].Child = new(); else m.Child = new();
            var b = new SpriteBatch(); e.BeforeDraw(b); e.AfterDraw(b); e.Input(Click());
            Check(b.Events.Count == 2 && !Selected(m, p) && h.Input.Suppressed.Count == 0, "modal keeps background entry and blocks click-through");
            m.Child = null; m.pages[0].Child = null;
        }
        m.currentTab = 7; var collection = (CollectionsPage)m.pages[7]; collection.letterviewerSubMenu = new();
        var letter = new SpriteBatch(); e.BeforeDraw(letter); letter.Events.Add("letter"); e.AfterDraw(letter); e.Input(Click());
        Check(letter.Events.SequenceEqual(new[] { "tab", "tab", "letter" }) && !Selected(m, p), "letter covers entry, no icon hidden or repainted above it");
        collection.letterviewerSubMenu = null; m.currentTab = 0;
        m.hoverText = "Social"; var normal = new SpriteBatch(); e.BeforeDraw(normal); normal.Events.Add("native"); e.AfterDraw(normal);
        Check(normal.Events.SequenceEqual(new[] { "tab", "tab", "native", "tab", "tab", "tooltip:Social", "mouse" }), "original layered rendering retained");
        m.pages[0].CanClose = false; e.Input(Click()); Check(!Selected(m, p), "readyToClose refusal respected");
        m.pages[0].CanClose = true; h.Input.Suppressed.Clear(); GameMenu.forcePreventClose = true; e.Input(Click());
        Check(!Selected(m, p), "forcePreventClose respected"); GameMenu.forcePreventClose = false;
        h.Input.Suppress(SButton.MouseLeft); e.Input(Click()); Check(!Selected(m, p), "other mod suppression respected");
        h.Input.Suppressed.Clear(); e.Input(Click()); m.receiveKeyPress(Keys.Escape);
        Check(Game1.activeClickableMenu == null, "native escape closes own page");
        e.MenuChanged(null); Check(p.Released, "native close releases page");
        var (clickEntry, clickMenu, clickHelper, _, clickPage) = Setup(); clickEntry.Input(Click());
        clickMenu.receiveLeftClick(1020, 20); Check(Game1.activeClickableMenu == null, "native X closes own page"); clickEntry.Detach();
        var (controller, cm, ch, _, cp) = Setup(); Game1.options.gamepadControls = true;
        ch.Input.Suppress(SButton.RightTrigger); cm.currentTab = 1; Frame(controller, cm, ch, false, true);
        Check(!Selected(cm, cp), "other mod trigger suppression respected"); controller.Detach();
    }
    static void ResizeAndMap()
    {
        foreach (bool notifyFirst in new[] { false, true })
        {
            var (e, m, h, log, p) = Setup(extra: 2); e.Input(Click()); e.BeforeWindowResize();
            Check(m.currentTab < 10 && m.lastOpenedNonMapTab < 10 && m.NativeChanges == 0, "resize fallback never passes appended index to native constructor or activates Skills");
            var next = Native(m.currentTab, 3); Game1.activeClickableMenu = next;
            if (notifyFirst) e.MenuChanged(next);
            e.AfterWindowResize(); if (!notifyFirst) e.MenuChanged(next);
            Check(next.pages[next.currentTab] is ProgressPage && p.Released && log.Messages.Count == 0, "resize restores page with either notification order"); e.Detach();
            var (map, mm, mh, ml, mp) = Setup(extra: 1); map.Input(Click()); mh.Input.Suppressed.Clear(); mm.changeTab(3, false);
            map.Input(Click()); Check(mm.lastOpenedNonMapTab < 10, "map return cannot construct from appended index");
            var returnMenu = Native(mm.lastOpenedNonMapTab); Game1.activeClickableMenu = returnMenu;
            if (notifyFirst) map.MenuChanged(returnMenu);
            map.UpdateNavigation(tick: true); if (!notifyFirst) map.MenuChanged(returnMenu);
            Check(returnMenu.pages[returnMenu.currentTab] is ProgressPage && ml.Messages.Count == 0, "map return restores own page"); map.Detach();
        }
        var (entry, menu, helper, _, page) = Setup(); entry.Input(Click());
        var independent = new IClickableMenu(); Game1.activeClickableMenu = independent; entry.MenuChanged(independent); entry.AfterWindowResize();
        Check(ReferenceEquals(Game1.activeClickableMenu, independent) && page.Released, "unrelated root is never hijacked");
    }
    static void Protection()
    {
        void Reject(Action<GameMenu, ProgressPage> mutate, string reason)
        {
            var (e, m, h, log, p) = Setup(); e.Input(Click()); mutate(m, p); e.BeforeUpdate();
            Check(log.Messages.Count == 1 && log.Messages[0].Contains("Reason=" + reason), "specific diagnostic: " + reason);
            Check(!m.pages.Contains(p) && m.currentTab >= 0 && m.currentTab < m.pages.Count
                && m.lastOpenedNonMapTab >= 0 && m.lastOpenedNonMapTab < m.pages.Count, "invalid owned page safely detached including map-return index");
            e.BeforeUpdate(); e.UpdateNavigation(); e.BeforeDraw(new()); e.AfterDraw(new());
            Check(log.Messages.Count == 1, "diagnostic doesn't spam");
        }
        Reject((m,p) => m.pages.Remove(p), "progress-page-invalid");
        Reject((m,p) => m.pages.Add(p), "progress-page-invalid");
        Reject((m,p) => m.tabs.RemoveAt(1), "anchor-tab-missing");
        Reject((m,p) => m.tabs.Insert(0,new(new(0,16,64,64),"extra","")), "tab-order-changed");
        Reject((m,p) => m.Mappings["skills"] = m.pages.IndexOf(p), "progress-mapping-collision");
        Reject((m,p) => m.tabs[2].bounds = new(228,16,64,64), "tab-overlap");
        Reject((m,p) => m.currentTab = 900, "current-page-invalid");
        var (e, m, h, log, p) = Setup(extra: 1);
        var other = m.pages[10]; m.pages.Remove(other); e.Input(Click());
        Check(Selected(m,p) && m.currentTab == 10 && log.Messages.Count == 0, "own index resolved after unrelated page removal");
        m.pages.Add(new()); e.UpdateNavigation(); Check(Selected(m,p), "unrelated appended page does not steal selection");
        Game1.options.gamepadControls = true; m.Mappings["skills"] = -1; Frame(e,m,h,true,false);
        Check(log.Messages.Single().Contains("target-mapping-invalid"), "invalid exit target rejected when actually used");
        var (keep, km, kh, _, kp) = Setup(); km.tabs[2].bounds = new(900,16,64,64); keep.Detach();
        Check(km.tabs[2].bounds.X == 900, "cleanup preserves another mod's later layout change");
    }
}
