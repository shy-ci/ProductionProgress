using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;
using MossHarbor.ProductionProgress.Core;
using SObject = StardewValley.Object;

namespace MossHarbor.ProductionProgress.Menus;

internal sealed class ProgressPage : IClickableMenu
{
    private sealed record DisplayItem(string Name, Item? Icon);
    private sealed record Line(ProgressRow Row, Item? Icon, string Name, string FullName, string Right, string From, string To, double? Fraction);
    private readonly ITranslationHelper text;
    private readonly Func<IReadOnlyCollection<ProgressRow>> getRows;
    private readonly Func<long> getRevision;
    private readonly Func<string?> getStatus;
    private readonly Action opened;
    private readonly IClickableMenu menu;
    private readonly Func<int> getTabsBottom;
    private readonly Dictionary<long, DisplayItem> displayItems = new();
    private List<Line> lines = new();
    private List<ProgressDisplayGroup> ordered = new();
    private long revision = -1, clock = -1;
    private long groupedDay = -1;
    private string language = "";
    private int scroll, lineOffset, ticks;
    private bool draggingScroll;
    private int scrollGrabOffset, scrollPointerY;
    private const float DetailScale = 0.85f;
    private string? hoveredName;
    private ProgressLayout layout;
    private readonly record struct BarSwatches(Rectangle Body, Rectangle Highlight, Rectangle Shadow);
    private static readonly Rectangle ScrollRunnerSource = new(403, 383, 6, 6);
    private static readonly Rectangle ScrollThumbSource = new(435, 463, 6, 10);
    // Source pixels are from the native scrollbar's body, light and dark regions.
    // Drawing those pixels preserves any content-pack recolor without GPU readback.
    private static readonly BarSwatches TrackSwatches = new(
        Pixel(ScrollRunnerSource, 1, 1), Pixel(ScrollRunnerSource, 1, 2), Pixel(ScrollRunnerSource, 4, 1));
    private static readonly BarSwatches FillSwatches = new(
        Pixel(ScrollThumbSource, 2, 2), Pixel(ScrollThumbSource, 2, 1), Pixel(ScrollThumbSource, 1, 3));

    private static Rectangle Pixel(Rectangle source, int x, int y) => new(source.X + x, source.Y + y, 1, 1);

    public ProgressPage(IClickableMenu menu, ITranslationHelper text, Func<IReadOnlyCollection<ProgressRow>> getRows,
        Func<long> getRevision, Func<string?> getStatus, Action opened, Func<int> getTabsBottom)
        : base(menu.xPositionOnScreen, menu.yPositionOnScreen, menu.width, menu.height)
    {
        this.text = text; this.getRows = getRows; this.getRevision = getRevision;
        this.getStatus = getStatus; this.opened = opened; this.menu = menu;
        this.getTabsBottom = getTabsBottom;
        layout = CreateLayout();
    }

    public void Open() { draggingScroll = false; opened(); revision = -1; Refresh(); }
    private int VisibleRows => layout.VisibleRows;
    public void ReleaseResources() { draggingScroll = false; displayItems.Clear(); ordered.Clear(); lines.Clear(); }
    private ProgressLayout CreateLayout() => ProgressLayout.Create(menu.xPositionOnScreen, menu.yPositionOnScreen,
        width, height, getTabsBottom(),
        spaceToClearTopBorder, borderWidth + spaceToClearSideBorder,
        (int)Math.Ceiling(Game1.smallFont.MeasureString(text.Get("growing") + "Ag").Y),
        (int)Math.Ceiling(Game1.smallFont.MeasureString(text.Get("growing") + "Ag").Y * DetailScale));

    public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
    {
        // Menu hosts position themselves before forwarding the resize to their page.
        xPositionOnScreen = menu.xPositionOnScreen; yPositionOnScreen = menu.yPositionOnScreen;
        width = menu.width; height = menu.height;
        draggingScroll = false;
        Refresh();
    }
    public void Scroll(int delta)
    {
        SetScroll(scroll + delta * layout.RowHeight);
    }

    private void SetScroll(int offset)
    {
        int next = Math.Clamp(offset, 0, Scrollbar.MaximumOffset);
        if (next != scroll) { scroll = next; clock = -1; Refresh(); }
    }

    public override void receiveScrollWheelAction(int direction) => Scroll(direction > 0 ? -2 : 2);

    private int ScrollbarX => menu.xPositionOnScreen + menu.width + 16;
    private int ScrollbarHeight => layout.ListHeight;
    private ScrollMetrics Scrollbar => ScrollMetrics.Create(layout.ListTop, ScrollbarHeight, ordered.Count * layout.RowHeight, scroll);
    private Rectangle ListBounds => new(menu.xPositionOnScreen, layout.ListTop, menu.width, layout.ListHeight);

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        Refresh();
        ScrollMetrics metrics = Scrollbar;
        if (getStatus() != null || !metrics.Visible) return;
        int before = scroll;
        if (new Rectangle(ScrollbarX, layout.ListTop, 44, ScrollMetrics.ArrowHeight).Contains(x, y)) Scroll(-1);
        else if (new Rectangle(ScrollbarX, layout.ListTop + ScrollbarHeight - ScrollMetrics.ArrowHeight,
            44, ScrollMetrics.ArrowHeight).Contains(x, y)) Scroll(1);
        else if (new Rectangle(ScrollbarX, metrics.RunnerTop, 44, metrics.RunnerHeight).Contains(x, y))
        {
            draggingScroll = true;
            // Grabbing the thumb preserves the pointer offset; a track click centres it.
            bool onThumb = y >= metrics.ThumbTop && y < metrics.ThumbTop + ScrollMetrics.ThumbHeight;
            scrollGrabOffset = onThumb ? y - metrics.ThumbTop : ScrollMetrics.ThumbHeight / 2;
            scrollPointerY = y;
            if (!onThumb) SetScroll(metrics.OffsetAt(y, scrollGrabOffset));
        }
        if (playSound && scroll != before) Game1.playSound("shwip");
    }

    public override void leftClickHeld(int x, int y)
    {
        if (!draggingScroll) return;
        ScrollMetrics metrics = Scrollbar;
        if (getStatus() != null || !metrics.Visible) { draggingScroll = false; return; }
        if (y == scrollPointerY) return;
        scrollPointerY = y;
        SetScroll(metrics.OffsetAt(y, scrollGrabOffset));
    }

    public override void releaseLeftClick(int x, int y) => draggingScroll = false;

    public override void applyMovementKey(int direction)
    {
        // Native snappy input sends both the initial press and held repeats here.
        if (direction == 0) Scroll(-1);
        else if (direction == 2) Scroll(1);
        else base.applyMovementKey(direction);
    }

    public override void receiveGamePadButton(Buttons button)
    {
        // In snappy mode the same press also arrives as a mapped movement key.
        // Match receiveKeyPress's raw flags: Options.SnappyMenus additionally
        // depends on mouse buttons and would allow double scrolling while clicked.
        if (Game1.options.snappyMenus && Game1.options.gamepadControls) return;
        if (button is Buttons.DPadDown or Buttons.LeftThumbstickDown) Scroll(1);
        else if (button is Buttons.DPadUp or Buttons.LeftThumbstickUp) Scroll(-1);
    }

    public override void update(GameTime time)
    {
        base.update(time);
        // At most four row/layout refreshes per second; drawing never reads world objects.
        if (++ticks % 15 == 0) Refresh();
    }

    private void Refresh()
    {
        ProgressLayout nextLayout = CreateLayout();
        if (nextLayout != layout) { layout = nextLayout; revision = -1; }
        if (getStatus() != null) { draggingScroll = false; return; } // don't sort partial snapshots hidden by the loading message
        long now = ProgressMath.ProcessingClock(Game1.Date.TotalDays, Game1.timeOfDay);
        string currentLanguage = LocalizedContentManager.CurrentLanguageCode.ToString();
        long currentRevision = getRevision();
        if (currentRevision == revision && clock == now && language == currentLanguage) return;
        if (language != currentLanguage || currentRevision != revision || now / 1600 != groupedDay)
        {
            displayItems.Clear();
            // Project precise batches into display days only when data/language/date changes.
            // No world scan or per-frame grouping; percentage refresh uses visible representatives.
            ordered = ProgressDisplayGroups.Create(getRows(), now).OrderBy(g => g.Current.State)
                .ThenBy(g => g.Current.Kind).ThenBy(g => g.Current.Name, StringComparer.CurrentCulture)
                .ThenBy(g => g.Current.DisplayDate(now)?.Day ?? int.MaxValue)
                .ThenBy(g => g.Current.DisplayDate(now)?.TimeOfDay ?? 0).ThenBy(g => g.Current.Id).ToList();
            groupedDay = now / 1600;
            scroll = Math.Clamp(scroll, 0, Scrollbar.MaximumOffset);
        }
        if (!Scrollbar.Visible) draggingScroll = false;
        language = currentLanguage; revision = currentRevision; clock = now;
        // Include partially visible rows at both edges; hidden rows still aren't formatted.
        var range = layout.VisibleRange(ordered.Count, scroll);
        lineOffset = range.Offset;
        lines = ordered.Skip(range.FirstIndex).Take(range.Count).Select(group =>
        {
            ProgressRow row = group.Current;
            if (!displayItems.TryGetValue(row.Id, out DisplayItem? display))
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(row.ItemId) || ItemRegistry.GetData(row.ItemId) is not { IsErrorItem: false })
                        throw new InvalidOperationException("Missing item identity");
                    Item item = ItemRegistry.Create(row.ItemId);
                    if (item is SObject obj && row.PreserveKind >= 0)
                    {
                        obj.preserve.Value = (SObject.PreserveType)row.PreserveKind;
                        obj.preservedParentSheetIndex.Value = row.FlavorId;
                    }
                    // The output identity supplies both the localized name and native icon.
                    // Keep this display-only item; never retain or mutate a live machine output.
                    display = new DisplayItem(item.DisplayName, item);
                }
                catch
                {
                    display = new DisplayItem(string.IsNullOrWhiteSpace(row.Name) ? text.Get("unknown-item").ToString() : row.Name, null);
                }
                displayItems[row.Id] = display;
            }
            string name = display.Name;
            string activity = row.Kind == ProductionKind.Aging ? text.Get("aging").ToString() : "";
            string fullName = name + (activity.Length == 0 ? "" : "(" + activity + ")") + "(" + group.Quantity + ")";
            string title = NameFitting.Fit(name, activity, group.Quantity, layout.NameWidth, value => Game1.smallFont.MeasureString(value).X);
            if (group.CurrentCount < group.Count)
                fullName += "\n" + text.Get("batch-count-help", new { current = group.CurrentCount, total = group.Count });
            double? fraction = row.Fraction(now);
            bool aging = row.Kind == ProductionKind.Aging;
            bool crop = row.Kind is ProductionKind.Crop or ProductionKind.Regrowth;
            string target = aging ? Quality(row.ToQuality) : text.Get(crop ? "mature" : "finish").ToString();
            string right = row.State == ProductionState.Ready
                ? text.Get("progress-target", new { percent = "100", target = aging ? Quality(row.ToQuality) : text.Get("done").ToString() })
                : row.State == ProductionState.Withered ? text.Get("withered")
                : fraction.HasValue ? text.Get("progress-target", new { percent = Math.Floor(fraction.Value * 100).ToString("0"), target }) : text.Get("unknown");
            string from = row.Kind switch
            {
                ProductionKind.Crop => text.Get(row.State == ProductionState.Ready ? "growth-complete" : "growing"),
                ProductionKind.Regrowth => text.Get(row.State == ProductionState.Ready ? "growth-complete" : "growing"),
                ProductionKind.Tapper => text.Get(row.State == ProductionState.Ready ? "collection-complete" : "collecting"),
                ProductionKind.Aging => Quality(row.FromQuality),
                _ => text.Get(row.State == ProductionState.Ready ? "processing-complete" : "processing")
            };
            string to = row.State == ProductionState.Ready ? "" : text.Get("eta-unknown").ToString();
            if (row.RemainingSunnyDays() is int sunnyDays)
                to = text.Get("sunny-days-remaining", new { days = sunnyDays });
            else if (row.DisplayDate(now) is CompletionDate finish)
            {
                var date = new WorldDate(Game1.Date) { TotalDays = finish.Day };
                string season = text.Get("season-" + date.SeasonKey);
                string dateLabel = text.Get(date.Year == Game1.Date.Year ? "date-short" : "date-year",
                    new { season, day = date.DayOfMonth, year = date.Year });
                to = dateLabel;
                if (finish.TimeOfDay is int timeOfDay)
                {
                    int hour = timeOfDay / 100;
                    string time = (hour % 24).ToString("00") + ":" + (timeOfDay % 100).ToString("00");
                    if (hour >= 24) time = text.Get("clock-night", new { time });
                    to = text.Get("clock-date", new { date = dateLabel, time });
                }
            }
            return new Line(row, display.Icon, title, fullName, right, from, to, fraction);
        }).ToList();
        hoveredName = null;
    }

    private string Quality(int quality) => text.Get(quality switch { 1 => "silver", 2 => "gold", 4 => "iridium", _ => "normal" });

    public override void draw(SpriteBatch b)
    {
        if (VisibleRows == 0)
        {
            b.DrawString(Game1.smallFont, text.Get("small-window"), new Vector2(layout.Left, layout.ListTop), Game1.textColor);
            return;
        }
        string? status = getStatus();
        int left = layout.Left;
        int top = layout.ListTop;
        if (status != null || lines.Count == 0)
        {
            b.DrawString(Game1.smallFont, Fit(status ?? text.Get("empty"), layout.Width), new Vector2(left, top + layout.NameOffset), Game1.textColor);
            return;
        }
        DrawClippedRows(b);
        ScrollMetrics metrics = Scrollbar;
        if (metrics.Visible)
        {
            // OptionsPage/SocialPage sprites from the current (possibly reskinned) game atlas.
            // Read Game1.mouseCursors at draw time so replacement textures aren't held stale.
            drawTextureBox(b, Game1.mouseCursors, ScrollRunnerSource,
                ScrollbarX + 12, metrics.RunnerTop, 24, metrics.RunnerHeight, Color.White, 4f, false);
            b.Draw(Game1.mouseCursors, new Rectangle(ScrollbarX, top, 44, ScrollMetrics.ArrowHeight),
                new Rectangle(421, 459, 11, 12), Color.White);
            b.Draw(Game1.mouseCursors, new Rectangle(ScrollbarX, top + ScrollbarHeight - ScrollMetrics.ArrowHeight,
                44, ScrollMetrics.ArrowHeight), new Rectangle(421, 472, 11, 12), Color.White);
            b.Draw(Game1.mouseCursors, new Rectangle(ScrollbarX + 12, metrics.ThumbTop, 24, ScrollMetrics.ThumbHeight),
                ScrollThumbSource, Color.White);
        }
        if (hoveredName != null) drawHoverText(b, hoveredName, Game1.smallFont);
    }

    public override void performHoverAction(int x, int y)
    {
        hoveredName = null;
        if (!ListBounds.Contains(x, y)) return;
        for (int i = 0; i < lines.Count; i++)
        {
            Line line = lines[i];
            var bounds = new Rectangle(layout.NameLeft, layout.ListTop + i * layout.RowHeight - lineOffset + layout.NameOffset,
                layout.NameWidth, layout.BarOffset - layout.NameOffset - 2);
            if (line.Name != line.FullName && bounds.Contains(x, y)) { hoveredName = line.FullName; break; }
        }
    }

    private void DrawClippedRows(SpriteBatch b)
    {
        // GameMenu supplies an untransformed UI batch. As in SocialPage, use the
        // game's scissor-enabled rasterizer, then restore the batch before tooltips/cursor.
        b.End();
        GraphicsDevice device = b.GraphicsDevice;
        Rectangle previousScissor = device.ScissorRectangle;
        RasterizerState previousRasterizer = device.RasterizerState;
        Rectangle clip = Rectangle.Intersect(ListBounds, device.Viewport.Bounds);
        if (previousRasterizer.ScissorTestEnable) clip = Rectangle.Intersect(clip, previousScissor);
        device.ScissorRectangle = clip;
        b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, Utility.ScissorEnabled);
        try
        {
            int top = layout.ListTop - lineOffset;
            // Keep native partitions behind the content, clipping them at the same edges.
            for (int i = 0; i < lines.Count; i++)
                drawHorizontalPartition(b, top + (i + 1) * layout.RowHeight - 36, small: true);
            for (int i = 0; i < lines.Count; i++)
            {
                Line line = lines[i]; int y = top + i * layout.RowHeight;
                // drawInMenu centres items in a native 64px slot even when scaled.
                // Compensate for that inset so the 48px icon starts at the name's top.
                const int iconInset = (64 - ProgressLayout.IconSize) / 2;
                line.Icon?.drawInMenu(b, new Vector2(layout.Left - iconInset, y + layout.NameOffset - iconInset),
                    ProgressLayout.IconSize / 64f, 1f, 0.9f, StackDrawType.Hide, Color.White, drawShadow: false);
                b.DrawString(Game1.smallFont, line.Name, new Vector2(layout.NameLeft, y + layout.NameOffset),
                    Color.Lerp(Game1.textColor, Color.Black, 0.15f));
                float rightWidth = Game1.smallFont.MeasureString(line.Right).X * DetailScale;
                DrawDetail(b, line.Right, layout.ProgressLeft + layout.ProgressWidth - rightWidth, y + layout.DetailOffset,
                    Game1.textColor);
                var rect = new Rectangle(layout.BarLeft, y + layout.BarOffset, layout.BarWidth, ProgressLayout.BarHeight);
                DrawPixelBar(b, rect, TrackSwatches);
                if (line.Fraction is double amount && amount > 0)
                    DrawPixelBar(b, new Rectangle(rect.X, rect.Y, (int)(rect.Width * amount), rect.Height), FillSwatches);
                if (line.Row.State is ProductionState.Working or ProductionState.Ready)
                {
                    string from = Fit(line.From, (layout.ProgressWidth - rightWidth - 12) / DetailScale);
                    DrawDetail(b, from, layout.ProgressLeft, y + layout.DetailOffset, Game1.textColor);
                    string to = Fit(line.To, layout.TimeWidth);
                    b.DrawString(Game1.smallFont, to, new Vector2(layout.TimeLeft + layout.TimeWidth - Game1.smallFont.MeasureString(to).X,
                        y + layout.NameOffset), Game1.textColor);
                }
            }
        }
        finally
        {
            b.End();
            device.ScissorRectangle = previousScissor;
            b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, previousRasterizer);
        }
    }

    private static void DrawDetail(SpriteBatch b, string value, float x, int y, Color color)
        => b.DrawString(Game1.smallFont, value, new Vector2(x, y), color,
            0, Vector2.Zero, DetailScale, SpriteEffects.None, 0.9f);

    private static void DrawPixelBar(SpriteBatch b, Rectangle bounds, BarSwatches swatches)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        Texture2D texture = Game1.mouseCursors;
        int step = Math.Min(2, Math.Min(bounds.Width / 4, bounds.Height / 5));
        if (step == 0) { b.Draw(texture, bounds, swatches.Body, Color.White); return; }
        // Integer-pixel bands form clipped, stepped caps and a shallow bevel.
        // Use the current game atlas on every draw, never a copied texture or cached RGB.
        b.Draw(texture, new Rectangle(bounds.X + step * 2, bounds.Y, bounds.Width - step * 4, step), swatches.Highlight, Color.White);
        b.Draw(texture, new Rectangle(bounds.X + step, bounds.Y + step, bounds.Width - step * 2, step), swatches.Body, Color.White);
        b.Draw(texture, new Rectangle(bounds.X, bounds.Y + step * 2, bounds.Width, bounds.Height - step * 4), swatches.Body, Color.White);
        b.Draw(texture, new Rectangle(bounds.X + step, bounds.Bottom - step * 2, bounds.Width - step * 2, step), swatches.Body, Color.White);
        b.Draw(texture, new Rectangle(bounds.X + step * 2, bounds.Bottom - step, bounds.Width - step * 4, step), swatches.Shadow, Color.White);
    }

    private static string Fit(string value, float width)
    {
        if (Game1.smallFont.MeasureString(value).X <= width) return value;
        if (Game1.smallFont.MeasureString("...").X > width) return "";
        int end = value.Length;
        while (end > 0 && Game1.smallFont.MeasureString(value[..end] + "...").X > width) end--;
        return value[..end] + "...";
    }
}
