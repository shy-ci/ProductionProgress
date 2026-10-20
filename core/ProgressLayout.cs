namespace MossHarbor.ProductionProgress.Core;

public sealed record ProgressLayout(int Left, int Width, int ListTop, int RowHeight,
    int NameWidth, int ProgressLeft, int ProgressWidth, int TimeLeft, int TimeWidth,
    int BarLeft, int BarWidth, int NameOffset, int BarOffset, int DetailOffset, int VisibleRows)
{
    public const int BarHeight = 14;
    public const int ColumnGap = 20;

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
        int rowHeight = detailOffset + detailHeight + 8;
        int nameWidth = width / 2;
        int timeWidth = width * 35 / 100;
        int timeLeft = left + width - timeWidth;
        int barLeft = left;
        int barWidth = width;
        int rows = width < 240 ? 0 : Math.Max(0, (menuY + menuHeight - inset - listTop) / rowHeight);
        return new(left, width, listTop, rowHeight, nameWidth, barLeft, barWidth,
            timeLeft, timeWidth, barLeft, barWidth, nameOffset, barOffset, detailOffset, rows);
    }
}
