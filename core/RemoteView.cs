namespace MossHarbor.ProductionProgress.Core;

public sealed class RemoteView
{
    private readonly Dictionary<long, ProgressRow> rows = new();
    private string session = "";
    public long Revision { get; private set; }
    public bool HasSnapshot { get; private set; }
    public bool Initializing { get; private set; } = true;
    public IReadOnlyCollection<ProgressRow> Rows => rows.Values;

    public void Apply(ChangePacket packet)
    {
        if (packet.Protocol != 1) return;
        if (HasSnapshot && packet.Session == session && packet.Revision <= Revision) return;
        if (!packet.Full && (packet.Session != session || !HasSnapshot || packet.Revision <= Revision)) return;
        if (packet.Full)
        {
            rows.Clear(); session = packet.Session; HasSnapshot = true;
        }
        foreach (long id in packet.Removed) rows.Remove(id);
        foreach (ProgressRow row in packet.Rows) rows[row.Id] = row;
        Revision = packet.Revision; Initializing = packet.Initializing;
    }

    public void Clear() { rows.Clear(); session = ""; Revision = 0; HasSnapshot = false; Initializing = true; }
}
