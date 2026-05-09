using ProtoBuf;

namespace ForgottenAdmins.Data;

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
internal class ForgottenAdminsServerData
{
    public string[]? Roles { get; set; }
}