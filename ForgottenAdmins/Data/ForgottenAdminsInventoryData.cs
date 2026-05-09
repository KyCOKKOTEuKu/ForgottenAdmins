using ProtoBuf;

namespace ForgottenAdmins.Data;

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
internal class ForgottenAdminsInventoryData
{
    public ForgottenAdminsItemStackData[]? HotBar { get; set; }
    public ForgottenAdminsItemStackData[]? Backpack { get; set; }
    public ForgottenAdminsItemStackData[]? Crafting { get; set; }
    public ForgottenAdminsItemStackData[]? Character { get; set; }
    public ForgottenAdminsItemStackData[]? Mouse { get; set; }
}