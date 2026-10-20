using Netcode;

namespace MossHarbor.ProductionProgress.Tracking;

internal sealed class Subscriptions : IDisposable
{
    private readonly List<Action> unbind = new();

    public void Watch<T, TField>(NetFieldBase<T, TField> field, Action changed)
        where TField : NetFieldBase<T, TField>
    {
        void Handler(TField _, T oldValue, T newValue) => changed();
        field.fieldChangeEvent += Handler;
        field.fieldChangeVisibleEvent += Handler;
        unbind.Add(() => { field.fieldChangeEvent -= Handler; field.fieldChangeVisibleEvent -= Handler; });
    }

    public void WatchValues<T, TField>(NetFieldBase<T, TField> field, Action<T, T> changed)
        where TField : NetFieldBase<T, TField>
    {
        void Handler(TField _, T oldValue, T newValue) => changed(oldValue, newValue);
        field.fieldChangeEvent += Handler;
        field.fieldChangeVisibleEvent += Handler;
        unbind.Add(() => { field.fieldChangeEvent -= Handler; field.fieldChangeVisibleEvent -= Handler; });
    }

    public void Watch(NetIntList list, Action changed)
    {
        NetList<int, NetInt>.ElementChangedEvent element = (_, _, _, _) => changed();
        NetList<int, NetInt>.ArrayReplacedEvent array = (_, _, _) => changed();
        list.OnElementChanged += element; list.OnArrayReplaced += array;
        unbind.Add(() => { list.OnElementChanged -= element; list.OnArrayReplaced -= array; });
    }

    public void Dispose()
    {
        foreach (Action action in unbind) action();
        unbind.Clear();
    }
}
