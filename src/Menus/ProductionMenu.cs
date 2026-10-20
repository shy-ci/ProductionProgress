using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;
using MossHarbor.ProductionProgress.Core;

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
    private readonly Action unavailable;

    public ProductionMenu(GameMenu native, ProgressPage page, Action<IClickableMenu?> selectNative,
        Action<bool> adjacent, Action resized, Action unavailable)
        : base(native.xPositionOnScreen, native.yPositionOnScreen, native.width, native.height)
    {
        this.native = native; this.page = page; this.selectNative = selectNative;
        this.adjacent = adjacent; this.resized = resized; this.unavailable = unavailable;
        upperRightCloseButton = native.upperRightCloseButton;
    }

    private void Forward(Action action)
    {
        int pageIndex = MenuOwnership.IndexOfReference(native.pages, page);
        if (pageIndex < 0) { unavailable(); return; }
        IClickableMenu? original = native.currentTab >= 0 && native.currentTab < native.pages.Count ? native.pages[native.currentTab] : null;
        if (original == null) { unavailable(); return; }
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
        if (lostOriginal) { unavailable(); return; }
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
        // Also handle native repeat delivery, independent of SMAPI ButtonPressed suppression.
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
    public override void automaticSnapBehavior(int direction, int oldRegion, int oldId)
        => Forward(() => native.automaticSnapBehavior(direction, oldRegion, oldId));
}
