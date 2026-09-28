using System;
using System.Globalization;
using System.IO;

namespace PerspexCharacterAuthority;

// The published Legends fork stores no VLCharacterPersistence sidecar. The
// Perspex patcher stores its class in Player.m_customData instead.
internal static class VLBridge
{
    private const int Schema = 1;
    private const string ClassKey = "Perspex.Legends.Class";

    internal static ExtensionPayload EmptyState() => Encode(0);

    internal static ExtensionPayload Export(Player player)
    {
        if (player == null) return null;
        var selected = player.m_customData.TryGetValue(ClassKey, out var value) &&
                       int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed : 0;
        return Encode(selected);
    }

    internal static void Reset(Player player) => Apply(player, 0);

    internal static void Import(Player player, ExtensionPayload payload)
    {
        if (player == null || payload?.SchemaVersion != Schema || payload.Data?.Length != 8) return;
        using var reader = new BinaryReader(new MemoryStream(payload.Data, false));
        if (reader.ReadInt32() != Schema) return;
        Apply(player, reader.ReadInt32());
    }

    private static ExtensionPayload Encode(int selected)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
        {
            writer.Write(Schema);
            writer.Write(selected);
        }
        return new ExtensionPayload { SchemaVersion = Schema, Data = stream.ToArray() };
    }

    private static void Apply(Player player, int selected)
    {
        if (player == null) return;
        player.m_customData[ClassKey] = selected.ToString(CultureInfo.InvariantCulture);
        try { PerspexModpackPatcher.LegendsStatePatch.ApplyAuthorityClass(player, selected); }
        catch (Exception error)
        {
            PerspexCharacterAuthorityPlugin.Instance?.WarnExternal("Legends class import failed: " + error.Message);
        }
    }
}
