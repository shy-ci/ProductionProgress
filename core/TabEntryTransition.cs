namespace MossHarbor.ProductionProgress.Core;

// Observe one native tab change, then insert our page only if that exact change occurred.
// This avoids treating SMAPI's separate analog-trigger threshold as another button press.
public sealed class TabEntryTransition<T> where T : class
{
    private T? expectedPage;

    public void Capture(T? current, T before, T after, bool nextPressed, bool previousPressed)
    {
        expectedPage = null;
        if (nextPressed == previousPressed) return;
        if (nextPressed && ReferenceEquals(current, before)) expectedPage = after;
        else if (previousPressed && ReferenceEquals(current, after)) expectedPage = before;
    }

    public bool Complete(T? current)
    {
        T? expected = expectedPage;
        expectedPage = null;
        return expected != null && ReferenceEquals(current, expected);
    }

    public void Reset() => expectedPage = null;
}
