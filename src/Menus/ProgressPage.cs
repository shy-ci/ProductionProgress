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
    private sealed record Line(ProgressRow Row, string Name, string FullName, string Right, string From, string To, double? Fraction);
    private readonly ITranslationHelper text;
    private readonly Func<IReadOnlyCollection<ProgressRow>> getRows;
    private readonly Func<long> getRevision;
    private readonly Func<string?> getStatus;
    private readonly Action opened;
    private readonly GameMenu menu;
    private readonly Dictionary<long, string> itemNames = new();
    private List<Line> lines = new();
    private List<ProgressRow> ordered = new();
    private long revision = -1, clock = -1;
    private string language = "";
    private int scroll, ticks;
    private const float DetailScale = 0.85f;
    private string? hoveredName;
    private ProgressLayout layout;
    private readonly Color track = new(178, 148, 98), trackLight = new(213, 180, 121), trackShadow = new(134, 98, 57);
    private readonly Color fill = new(113, 149, 61), fillLight = new(161, 184, 91), fillShadow = new(77, 111, 42);
    private readonly Color ready = new(65, 106, 45);

    public ProgressPage(GameMenu menu, ITranslationHelper text, Func<IReadOnlyCollection<ProgressRow>> getRows,
        Func<long> getRevision, Func<string?> getStatus, Action opened)
        : base(menu.xPositionOnScreen, menu.yPositionOnScreen, menu.width, menu.height)
    {
        this.text = text; this.getRows = getRows; this.getRevision = getRevision;
        this.getStatus = getStatus; this.opened = opened; this.menu = menu;
        layout = CreateLayout();
    }

    public void Open() { opened(); revision = -1; Refresh(); }
    private int VisibleRows => layout.VisibleRows;
    public void ReleaseResources() { itemNames.Clear(); ordered.Clear(); lines.Clear(); }
    private ProgressLayout CreateLayout() => ProgressLayout.Create(menu.xPositionOnScreen, menu.yPositionOnScreen,
        width, height, menu.tabs.Select(t => t.bounds.Bottom).DefaultIfEmpty(menu.yPositionOnScreen).Max(),
        spaceToClearTopBorder, borderWidth,
        (int)Math.Ceiling(Game1.smallFont.MeasureString(text.Get("growing") + "Ag").Y),
        (int)Math.Ceiling(Game1.smallFont.MeasureString(text.Get("growing") + "Ag").Y * DetailScale));
    public void Scroll(int delta)
    {
        int next = Math.Clamp(scroll + delta, 0, Math.Max(0, ordered.Count - VisibleRows));
        if (next != scroll) { scroll = next; clock = -1; Refresh(); }
    }

    public override void receiveScrollWheelAction(int direction) => Scroll(direction > 0 ? -2 : 2);
    public override void receiveGamePadButton(Buttons button)
    {
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
        if (getStatus() != null) return; // don't sort partial snapshots hidden by the loading message
        long now = ProgressMath.ProcessingClock(Game1.Date.TotalDays, Game1.timeOfDay);
        string currentLanguage = LocalizedContentManager.CurrentLanguageCode.ToString();
        long currentRevision = getRevision();
        if (currentRevision == revision && clock == now && language == currentLanguage) return;
        if (language != currentLanguage || currentRevision != revision)
        {
            itemNames.Clear();
            // Sorting is triggered by a changed batch, never by the ten-minute clock alone.
            ordered = getRows().OrderBy(r => r.State).ThenBy(r => r.Kind).ThenBy(r => r.Name, StringComparer.CurrentCulture)
                .ThenBy(r => r.Location, StringComparer.Ordinal).ThenBy(r => r.Id).ToList();
            scroll = Math.Clamp(scroll, 0, Math.Max(0, ordered.Count - VisibleRows));
        }
        language = currentLanguage; revision = currentRevision; clock = now;
        // Only visible rows need localized item creation, percentage text and layout.
        lines = ordered.Skip(scroll).Take(VisibleRows).Select(row =>
        {
            if (!itemNames.TryGetValue(row.Id, out string? name))
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
                    name = item.DisplayName;
                }
                catch { name = string.IsNullOrWhiteSpace(row.Name) ? text.Get("unknown-item").ToString() : row.Name; }
                itemNames[row.Id] = name;
            }
            string activity = row.Kind == ProductionKind.Aging ? text.Get("aging").ToString() : "";
            string fullName = name + (activity.Length == 0 ? "" : "(" + activity + ")") + "(" + row.Count + ")";
            string title = NameFitting.Fit(name, activity, row.Count, layout.NameWidth, value => Game1.smallFont.MeasureString(value).X);
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
                ProductionKind.Regrowth => text.Get(row.State == ProductionState.Ready ? "growth-complete" : "regrowing"),
                ProductionKind.Tapper => text.Get(row.State == ProductionState.Ready ? "collection-complete" : "collecting"),
                ProductionKind.Aging => Quality(row.FromQuality),
                _ => text.Get(row.State == ProductionState.Ready ? "processing-complete" : "processing")
            };
            string to = text.Get(row.State == ProductionState.Ready ? "completion-date-unknown" : "eta-unknown");
            if (row.DisplayDate(now) is CompletionDate finish)
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
            return new Line(row, title, fullName, right, from, to, fraction);
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
        bool scrollable = ordered.Count > VisibleRows;
        // Match SocialPage: keep the native partition and its padding behind row content.
        for (int i = 0; i < lines.Count; i++)
            drawHorizontalPartition(b, top + (i + 1) * layout.RowHeight - 36, small: true);
        for (int i = 0; i < lines.Count; i++)
        {
            Line line = lines[i]; int y = top + i * layout.RowHeight;
            b.DrawString(Game1.smallFont, line.Name, new Vector2(left, y + layout.NameOffset), Game1.textColor);
            float rightWidth = Game1.smallFont.MeasureString(line.Right).X * DetailScale;
            DrawDetail(b, line.Right, layout.ProgressLeft + layout.ProgressWidth - rightWidth, y + layout.DetailOffset,
                Game1.textColor);
            var rect = new Rectangle(layout.BarLeft, y + layout.BarOffset, layout.BarWidth, ProgressLayout.BarHeight);
            DrawPixelBar(b, rect, track, trackLight, trackShadow);
            if (line.Fraction is double amount && amount > 0)
                DrawPixelBar(b, new Rectangle(rect.X, rect.Y, (int)(rect.Width * amount), rect.Height),
                    line.Row.State == ProductionState.Ready ? ready : fill, fillLight, fillShadow);
            if (line.Row.State is ProductionState.Working or ProductionState.Ready)
            {
                string from = Fit(line.From, (layout.ProgressWidth - rightWidth - 12) / DetailScale);
                DrawDetail(b, from, layout.ProgressLeft, y + layout.DetailOffset, Game1.textColor);
                string to = Fit(line.To, layout.TimeWidth);
                b.DrawString(Game1.smallFont, to, new Vector2(layout.TimeLeft + layout.TimeWidth - Game1.smallFont.MeasureString(to).X,
                    y + layout.NameOffset), Game1.textColor);
            }
        }
        if (scrollable)
        {
            int heightAvailable = VisibleRows * layout.RowHeight - 10;
            int thumbHeight = Math.Max(24, heightAvailable * VisibleRows / ordered.Count);
            int thumbY = top + (int)((heightAvailable - thumbHeight) * (double)scroll / Math.Max(1, ordered.Count - VisibleRows));
            int barX = layout.Left + layout.Width + 12;
            b.Draw(Game1.staminaRect, new Rectangle(barX, top, 6, heightAvailable), track);
            b.Draw(Game1.staminaRect, new Rectangle(barX, thumbY, 6, thumbHeight), fill);
        }
        if (hoveredName != null) drawHoverText(b, hoveredName, Game1.smallFont);
    }

    public override void performHoverAction(int x, int y)
    {
        hoveredName = null;
        for (int i = 0; i < lines.Count; i++)
        {
            Line line = lines[i];
            var bounds = new Rectangle(layout.Left, layout.ListTop + i * layout.RowHeight + layout.NameOffset,
                layout.NameWidth, layout.BarOffset - layout.NameOffset - 2);
            if (line.Name != line.FullName && bounds.Contains(x, y)) { hoveredName = line.FullName; break; }
        }
    }

    private static void DrawDetail(SpriteBatch b, string value, float x, int y, Color color)
        => b.DrawString(Game1.smallFont, value, new Vector2(x, y), color,
            0, Vector2.Zero, DetailScale, SpriteEffects.None, 0.9f);

    private static void DrawPixelBar(SpriteBatch b, Rectangle bounds, Color body, Color highlight, Color shadow)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        int step = Math.Min(2, Math.Min(bounds.Width / 4, bounds.Height / 5));
        if (step == 0) { b.Draw(Game1.staminaRect, bounds, body); return; }
        // Integer-pixel bands form clipped, stepped caps and a shallow bevel.
        // The bar uses the same texture batch as the page; no per-frame texture allocation.
        b.Draw(Game1.staminaRect, new Rectangle(bounds.X + step * 2, bounds.Y, bounds.Width - step * 4, step), highlight);
        b.Draw(Game1.staminaRect, new Rectangle(bounds.X + step, bounds.Y + step, bounds.Width - step * 2, step), body);
        b.Draw(Game1.staminaRect, new Rectangle(bounds.X, bounds.Y + step * 2, bounds.Width, bounds.Height - step * 4), body);
        b.Draw(Game1.staminaRect, new Rectangle(bounds.X + step, bounds.Bottom - step * 2, bounds.Width - step * 2, step), body);
        b.Draw(Game1.staminaRect, new Rectangle(bounds.X + step * 2, bounds.Bottom - step, bounds.Width - step * 4, step), shadow);
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
