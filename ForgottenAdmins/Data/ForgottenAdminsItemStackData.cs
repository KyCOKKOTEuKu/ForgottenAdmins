using ProtoBuf;

namespace ForgottenAdmins.Data;

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class ForgottenAdminsItemStackData
{
    public int ItemClass { get; set; }

    public int ItemId { get; set; }

    public int StackSize { get; set; }

    // do not remove this even we do not need it but the game will to render items
    public byte[]? Attributes { get; set; }
}