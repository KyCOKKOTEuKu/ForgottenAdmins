using System.Collections.Generic;
using ProtoBuf;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ForgottenAdmins.Data;

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
internal class ForgottenAdminsData
{
    public EnumGameMode CurrentGameMode;
    public string? PlayerUid;
    public string? Class;
    public float Health { get; set; }
    public float MaxHealth { get; set; }
    public float Saturation { get; set; }
    public float MaxSaturation { get; set; }
    public float DrunkLevel { get; set; }
    public float BodyTemperature { get; set; }
    public bool FreeMove { get; set; }
    public bool NoClip { get; set; }
    public float MoveSpeedMultiplier { get; set; }
    public string[]? Privileges { get; set; }
    public int RespawnUses { get; internal set; }
    public int ExtraLandClaimAllowance { get; set; }
    public int ExtraLandClaimAreas { get; set; }
    public string? Role { get; internal set; }
    public ForgottenAdminsInventoryData? ForgottenAdminsInventoryData { get; set; }
    public string? PlayerName { get; set; }
    public BlockPos? Position { get; set; }
    
    public List<LandClaim>? LandClaims { get; set; }
    public Dictionary<string, string>? Players { get; set; }
    public List<ForgottenAdminsCustomCoordinate>? CustomCoordinates { get; set; }
}