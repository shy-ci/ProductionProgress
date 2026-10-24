using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MossHarbor.ProductionProgress.Core;
using MossHarbor.ProductionProgress.Menus;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

// Recording host follows BGM's public activation/resize/cleanup contract.
// The cloud run separately verifies SMAPI's actual cross-assembly API proxy.
static class BetterGameMenuChecks
{
    private sealed record Change(IClickableMenu Menu, string Tab, string OldTab = "Inventory") : ITabChangedEvent;
    private sealed class Api : IBetterGameMenuApi
    {
        public readonly Dictionary<IClickableMenu, IClickableMenu> Pages = new();
        public string? Id; public int Order;
        public Func<IClickableMenu, IClickableMenu> Factory = null!;
        public Func<(IBetterGameMenuApi.DrawDelegate, bool)> Icon = null!;
        public Func<bool> Visible = null!;
        public Action<IClickableMenu> Close = null!;
        public IBetterGameMenuApi.MenuCreatedDelegate Created = null!;
        public IBetterGameMenuApi.TabChangedDelegate Changed = null!;
        public void RegisterTab(string id, int order, Func<string> getDisplayName,
            Func<(IBetterGameMenuApi.DrawDelegate DrawMethod, bool DrawBackground)> getIcon, int priority,
            Func<IClickableMenu, IClickableMenu> getPageInstance,
            Func<IBetterGameMenuApi.DrawDelegate?>? getDecoration = null, Func<bool>? getTabVisible = null,
            Func<bool>? getMenuInvisible = null, Func<int, int>? getWidth = null, Func<int, int>? getHeight = null,
            Func<(IClickableMenu Menu, IClickableMenu OldPage), IClickableMenu?>? onResize = null,
            Action<IClickableMenu>? onClose = null)
        { Id=id; Order=order; Factory=getPageInstance; Icon=getIcon; Visible=getTabVisible!; Close=onClose!; }
        public IClickableMenu? GetCurrentPage(IClickableMenu menu) => Pages.GetValueOrDefault(menu);
        public void OnMenuCreated(IBetterGameMenuApi.MenuCreatedDelegate handler, EventPriority priority = EventPriority.Normal) => Created=handler;
        public void OnTabChanged(IBetterGameMenuApi.TabChangedDelegate handler, EventPriority priority = EventPriority.Normal) => Changed=handler;
    }

    public static void Run()
    {
        int count=0, opens=0;
        void Check(bool pass, string message) { count++; if(!pass)throw new Exception(message); }
        BetterGameMenuIntegration Entry(Helper h, Monitor m) => new(h,m,()=>Array.Empty<ProgressRow>(),()=>0,()=>null,()=>opens++);
        var helper = new Helper(); var log = new Monitor();
        Entry(helper,log).Register();
        Check(helper.ModRegistry.Reads==0 && log.Messages.Count==0,"No BGM: no API access or diagnostic");
        helper.ModRegistry.Loaded=true;
        Entry(helper,log).Register();
        Check(log.Messages.Single().Contains("API is unavailable"),"Missing API is diagnosed");
        log.Messages.Clear(); helper.ModRegistry.Failure=new InvalidOperationException("proxy mismatch");
        Entry(helper,log).Register();
        Check(log.Messages.Single().Contains("proxy mismatch"),"API errors retain their cause");
        log.Messages.Clear(); helper.ModRegistry.Failure=null;
        var api = new Api(); helper.ModRegistry.Api=api; Entry(helper,log).Register();
        Check(api.Id==BetterGameMenuIntegration.TabId && api.Order>20 && api.Order<40,"Registration is between Skills and Social");
        Context.IsWorldReady=false; Check(!api.Visible(),"No farm: do not expose Progress");
        Context.IsWorldReady=true; Check(api.Visible(),"Farm: expose Progress");
        var host = new IClickableMenu(200,100,880,680);
        var page = (ProgressPage)api.Factory(host); api.Pages[host]=page;
        Check(page.Opens==0,"Creating cached page does not activate it prematurely");
        api.Created(host); Check(page.Opens==1 && opens==1,"Menu initially on Progress activates once");
        api.Changed(new Change(host,"Social")); Check(page.Opens==1,"Other tab does not reopen Progress");
        api.Changed(new Change(host,api.Id!)); Check(page.Opens==2 && opens==2,"Cached page reactivation refreshes snapshot");
        var second = new IClickableMenu(); var secondPage=(ProgressPage)api.Factory(second); api.Pages[second]=secondPage;
        api.Created(second); Check(page.Opens==2 && secondPage.Opens==1,"Separate menu instances keep separate pages");
        var draw = new SpriteBatch(); var (icon,background)=api.Icon(); icon(draw,new(500,20,64,64));
        Check(background && draw.Rectangles.Single()==new Rectangle(516,44,32,32),"Icon uses the host-assigned bounds");
        var native = new MenuIntegration(helper,()=>Array.Empty<ProgressRow>(),()=>0,()=>null,()=>{},log);
        Game1.activeClickableMenu=host; native.MenuChanged(host); native.BeforeUpdate(); native.UpdateNavigation();
        native.BeforeDraw(draw); native.AfterDraw(draw); native.Input(new(){Button=SButton.MouseLeft});
        Check(ReferenceEquals(Game1.activeClickableMenu,host) && draw.Rectangles.Count==1 && helper.Input.Suppressed.Count==0,
            "Native adapter does not move, draw or consume BGM's input");
        api.Close(page); Check(page.Released && !secondPage.Released,"Host cleanup only releases its own page");
        Check(log.Messages.Count==1 && log.Messages[0].Contains("Registered Progress"),"Successful registration logs once");
        Console.WriteLine($"PASS: {count} Better Game Menu contract assertions; recording host, no third-party binaries loaded.");
    }
}
