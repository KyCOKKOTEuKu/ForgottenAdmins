using System.Collections.Generic;
using ProtoBuf;

namespace ForgottenAdmins.Data;

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class ForgottenAdminsCustomCoordinate
{
    public string Name { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
}

public class ForgottenAdminsConfig
{
    public Dictionary<string, List<ForgottenAdminsCustomCoordinate>> PlayerCoordinates { get; set; } = new();
}
