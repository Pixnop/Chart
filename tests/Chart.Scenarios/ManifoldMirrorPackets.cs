// Copies of the packets Manifold sends on its "manifold:dims" channel, reduced to the fields
// these scenarios read. Manifold keeps the real types internal; Atlas matches a channel message
// by full type name, so the namespace and the type names must stay those of Manifold.dll, and
// the member numbers those of its wire format.
namespace Manifold.Internal.Networking;

using ProtoBuf;

[ProtoContract]
internal sealed class ManifestSnapshotPacket
{
    [ProtoMember(1)]
    public List<DimensionDescriptor> Dimensions { get; set; } = new();
}

[ProtoContract]
internal sealed class DimensionDescriptor
{
    [ProtoMember(1)]
    public string Code { get; set; } = string.Empty;

    [ProtoMember(7)]
    public List<MetadataEntry> Metadata { get; set; } = new();
}

[ProtoContract]
internal sealed class MetadataEntry
{
    [ProtoMember(1)]
    public string Key { get; set; } = string.Empty;

    [ProtoMember(3)]
    public long IntegerValue { get; set; }
}
