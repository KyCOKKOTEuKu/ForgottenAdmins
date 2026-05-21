using System.Collections.Generic;
using ProtoBuf;

namespace ForgottenAdmins.Data;

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class ForgottenAdminsClientModInfo
{
    public string ModId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class ForgottenAdminsClientModsPacket
{
    public ForgottenAdminsClientModInfo[] Mods { get; set; } = System.Array.Empty<ForgottenAdminsClientModInfo>();
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class ForgottenAdminsRequestClientModsPacket
{
    public string Reason { get; set; } = string.Empty;
}

public class ForgottenAdminsClientModsLog
{
    public List<ForgottenAdminsClientModsLogEntry> Players { get; set; } = new();
}

public class ForgottenAdminsClientModsLogEntry
{
    public string PlayerName { get; set; } = string.Empty;
    public string PlayerUid { get; set; } = string.Empty;
    public string LastUpdated { get; set; } = string.Empty;
    public List<ForgottenAdminsClientModInfo> Mods { get; set; } = new();
}
