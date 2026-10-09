namespace LogEase.Api.Integrations.Kimai;

/// <summary>
/// Encodes Kimai numeric IDs into Guids so existing GraphQL UUID fields keep working.
/// Project Guids start with 00000001-… Activity Guids start with 00000002-…
/// </summary>
public static class KimaiIdCodec
{
    private static readonly Guid ProjectNamespace = Guid.Parse("00000001-0000-4000-8000-000000000000");
    private static readonly Guid ActivityNamespace = Guid.Parse("00000002-0000-4000-8000-000000000000");

    public static Guid ToProjectGuid(int kimaiProjectId) =>
        Encode(ProjectNamespace, kimaiProjectId);

    public static Guid ToActivityGuid(int kimaiActivityId) =>
        Encode(ActivityNamespace, kimaiActivityId);

    public static bool TryGetProjectId(Guid id, out int kimaiProjectId) =>
        TryDecode(id, ProjectNamespace, out kimaiProjectId);

    public static bool TryGetActivityId(Guid id, out int kimaiActivityId) =>
        TryDecode(id, ActivityNamespace, out kimaiActivityId);

    public static bool IsKimaiId(Guid id) =>
        TryGetProjectId(id, out _) || TryGetActivityId(id, out _);

    private static Guid Encode(Guid ns, int value)
    {
        var bytes = ns.ToByteArray();
        var idBytes = BitConverter.GetBytes(value);
        // last 4 bytes hold the Kimai int (little-endian)
        Buffer.BlockCopy(idBytes, 0, bytes, 12, 4);
        return new Guid(bytes);
    }

    private static bool TryDecode(Guid id, Guid ns, out int value)
    {
        value = 0;
        var bytes = id.ToByteArray();
        var nsBytes = ns.ToByteArray();
        for (var i = 0; i < 12; i++)
        {
            if (bytes[i] != nsBytes[i]) return false;
        }

        value = BitConverter.ToInt32(bytes, 12);
        return value > 0;
    }
}
