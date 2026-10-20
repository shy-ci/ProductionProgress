using System.Globalization;

namespace MossHarbor.ProductionProgress.Core;

public static class NameFitting
{
    public static string Fit(string name, string activity, int count, float width, Func<string, float> measure)
    {
        string quantity = "(" + count.ToString(CultureInfo.InvariantCulture) + ")";
        string suffix = (activity.Length == 0 ? "" : "(" + activity + ")") + quantity;
        // Activity and quantity have priority over the name. Very narrow layouts retain the quantity first.
        if (measure(suffix) > width) suffix = quantity;
        if (measure(name + suffix) <= width) return name + suffix;
        const string ellipsis = "...";
        if (measure(ellipsis + suffix) > width) return suffix;
        int[] starts = StringInfo.ParseCombiningCharacters(name);
        int low = 0, high = starts.Length;
        while (low < high)
        {
            int mid = (low + high + 1) / 2;
            int end = mid == starts.Length ? name.Length : starts[mid];
            if (measure(name[..end] + ellipsis + suffix) <= width) low = mid;
            else high = mid - 1;
        }
        int length = low == starts.Length ? name.Length : starts[low];
        return name[..length] + ellipsis + suffix;
    }
}
