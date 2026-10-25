namespace MossHarbor.ProductionProgress.Core;

public sealed record ProgressLayout(int Left, int Width, int ListTop, int RowHeight,
    int NameWidth, int ProgressLeft, int ProgressWidth, int TimeLeft, int TimeWidth,
    int BarLeft, int BarWidth, int NameOffset, int BarOffset, int DetailOffset, int ListHeight)
{
    public const int BarHeight = 14;
    public const int ColumnGap = 20;
    public const int IconSize = 48;
    public const int IconGap = 16;
    public int NameLeft => Left + IconSize + IconGap;
    // SDV 1.6.8 drawDialogueBox draws the bottom 64px tile at menuBottom - 64.
    // Maps/MenuTiles' bottom edge starts at tile y=28: content ends at bottom - 36.
    // The scrollbar arrow's bottom - 16 is the OUTER edge, not a content boundary.
    public const int BottomInset = 36;
    public int VisibleRows => ListHeight / RowHeight;

    public (int FirstIndex, int Count, int Offset) VisibleRange(int totalRows, int scrollOffset)
    {
        if (VisibleRows == 0 || totalRows <= 0) return (0, 0, 0);
        int scroll = Math.Clamp(scrollOffset, 0, Math.Max(0, totalRows * RowHeight - ListHeight));
        int first = scroll / RowHeight;
        int offset = scroll % RowHeight;
        int count = Math.Min(totalRows - first, (ListHeight + offset + RowHeight - 1) / RowHeight);
        return (first, count, offset);
    }

    public static ProgressLayout Create(int menuX, int menuY, int menuWidth, int menuHeight,
        int tabsBottom, int clearTop, int sideInset, int textHeight, int detailHeight)
    {
        int inset = Math.Max(16, sideInset);
        int width = Math.Max(0, menuWidth - inset * 2);
        int left = menuX + inset;
        int listTop = Math.Max(menuY + clearTop, tabsBottom + 16);
        const int nameOffset = 4;
        int barOffset = nameOffset + textHeight + 2;
        int detailOffset = barOffset + BarHeight + 2;
        int rowHeight = Math.Max(detailOffset + detailHeight + 8, nameOffset + IconSize + 8);
        int contentWidth = Math.Max(0, width - IconSize - IconGap);
        int nameWidth = contentWidth / 2;
        int timeWidth = contentWidth * 35 / 100;
        int timeLeft = left + width - timeWidth;
        int barLeft = left + IconSize + IconGap;
        int barWidth = contentWidth;
        int listHeight = contentWidth < 240 ? 0 : Math.Max(0, menuY + menuHeight - BottomInset - listTop);
        return new(left, width, listTop, rowHeight, nameWidth, barLeft, barWidth,
            timeLeft, timeWidth, barLeft, barWidth, nameOffset, barOffset, detailOffset, listHeight);
    }
}
