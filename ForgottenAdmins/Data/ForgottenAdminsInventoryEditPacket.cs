using ProtoBuf;

namespace ForgottenAdmins.Data;

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
internal class ForgottenAdminsInventoryEditPacket
{
    public string TargetPlayerUid { get; set; } = string.Empty;
    public ForgottenAdminsInventoryData? Inventory { get; set; }
}
