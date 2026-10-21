using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;
using MossHarbor.ProductionProgress.Core;
using System.Runtime.CompilerServices;

namespace MossHarbor.ProductionProgress.Menus;

// SetWindowSize rebuilds a GameMenu using currentTab as a native index. A separate
// root while our page is active keeps that constructor from seeing an appended index.
internal sealed class ProductionMenu : IClickableMenu
{
    private readonly GameMenu native;
    private readonly ProgressPage page;
    private readonly Action<IClickableMenu?> selectNative;
    private readonly Action<bool> adjacent;
    private readonly Action resized;
    private readonly Action<string, string> unavailable;

    public ProductionMenu(GameMenu native, ProgressPage page, Action<IClickableMenu?> selectNative,
        Action<bool> adjacent, Action resized, Action<string, string> unavailable)
        : base(native.xPositionOnScreen, native.yPositionOnScreen, native.width, native.height)
    {
        this.native = native; this.page = page; this.selectNative = selectNative;
        this.adjacent = adjacent; this.resized = resized; this.unavailable = unavailable;
        upperRightCloseButton = native.upperRightCloseButton;
    }

    private void Forward(Action action, [CallerMemberName] string operation = "")
    {
        int pageIndex = MenuOwnership.IndexOfReference(native.pages, page);
        if (pageIndex < 0)
        {
            unavailable("forward-progress-page-missing", $"operation={operation}; Progress page was removed before forwarding.");
            return;
        }
        IClickableMenu? original = native.currentTab >= 0 && native.currentTab < native.pages.Count ? native.pages[native.currentTab] : null;
        if (original == null)
        {
            unavailable("forward-current-page-invalid", $"operation={operation}; currentTab={native.currentTab}; pageCount={native.pages.Count}");
            return;
        }
        IClickableMenu? after = page;
        bool lostOriginal = false;
        try
        {
            native.currentTab = pageIndex; action();
            after = native.currentTab >= 0 && native.currentTab < native.pages.Count ? native.pages[native.currentTab] : null;
        }
        finally
        {
            int restored = MenuOwnership.IndexOfReference(native.pages, original);
            if (restored >= 0) native.currentTab = restored;
            else lostOriginal = true;
        }
        if (lostOriginal)
        {
            unavailable("forward-original-page-lost", $"operation={operation}; original={MenuIntegration.DescribePage(original)} was removed during forwarding.");
            return;
        }
        if (!ReferenceEquals(after, page)) selectNative(after);
    }

    public override void update(GameTime time) => Forward(() => native.update(time));
    public override void draw(SpriteBatch b) => Forward(() => native.draw(b));
    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        if (native.upperRightCloseButton?.containsPoint(x, y) == true) { Close(); return; }
        Forward(() => native.receiveLeftClick(x, y, playSound));
    }
    public override void receiveRightClick(int x, int y, bool playSound = true) => Forward(() => native.receiveRightClick(x, y, playSound));
    public override void receiveScrollWheelAction(int direction) => Forward(() => native.receiveScrollWheelAction(direction));
    public override void leftClickHeld(int x, int y) => Forward(() => native.leftClickHeld(x, y));
    public override void releaseLeftClick(int x, int y) => Forward(() => native.releaseLeftClick(x, y));
    public override void receiveKeyPress(Keys key)
    {
        if (key == Keys.Escape || Game1.options.doesInputListContain(Game1.options.menuButton, key)) { Close(); return; }
        Forward(() => native.receiveKeyPress(key));
    }
    public override void receiveGamePadButton(Buttons button)
    {
        // LT/RT have one owner: the game's native press dispatch, not SMAPI ButtonPressed.
        if (button is Buttons.B or Buttons.Back or Buttons.Start) Close();
        else if (button == Buttons.LeftTrigger) adjacent(false);
        else if (button == Buttons.RightTrigger) adjacent(true);
        else Forward(() => native.receiveGamePadButton(button));
    }
    private void Close()
    {
        if (ReferenceEquals(Game1.activeClickableMenu, this) && !GameMenu.forcePreventClose && readyToClose())
            exitThisMenu(); // the active root owns closing; forwarding to the inactive GameMenu cannot close it
    }
    public override void performHoverAction(int x, int y) => Forward(() => native.performHoverAction(x, y));
    public override bool readyToClose() => page.readyToClose();
    public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds) => resized();
    public override void snapToDefaultClickableComponent() => page.snapToDefaultClickableComponent();
    public override ClickableComponent getCurrentlySnappedComponent() => page.getCurrentlySnappedComponent();
    public override void setCurrentlySnappedComponentTo(int id) => page.setCurrentlySnappedComponentTo(id);
    // Game1 routes snappy movement to the active root unless it is a GameMenu.
    // The page owns the focus and components; this wrapper has neither.
    public override void applyMovementKey(int direction) => page.applyMovementKey(direction);
    public override void snapCursorToCurrentSnappedComponent() => page.snapCursorToCurrentSnappedComponent();
    public override void automaticSnapBehavior(int direction, int oldRegion, int oldId)
        => Forward(() => native.automaticSnapBehavior(direction, oldRegion, oldId));
}
