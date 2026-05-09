using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ForgottenAdmins.Data;
using ForgottenAdmins.GUI;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.Common;
using Vintagestory.Server;

[assembly: ModInfo("ForgottenAdmins",
    Description = "ForgottenAdmins server administration panel",
    Authors = new[] { "KyCOK_KOTEuKu" },
    Version = "1.3.3")]

namespace ForgottenAdmins;

public class ForgottenAdmins : ModSystem
{
    private PlayerMenu _dialog = null!;
    private ICoreClientAPI _capi = null!;
    private ICoreServerAPI _sapi = null!;

    private IClientNetworkChannel _clientNetworkChannel = null!;
    private IServerNetworkChannel _serverNetworkChannel = null!;
    private readonly List<string> _receivedServerData = new();
    
    private Dictionary<string,ServerPlayer> _offlinePlayerData = new Dictionary<string,ServerPlayer>();
    private ForgottenAdminsInventoryData _emptyInventory = null!;
    private BlockPos _nullBlockPos = null!;
    private ForgottenAdminsConfig _config = new();

    public override void StartClientSide(ICoreClientAPI api)
    {
        _clientNetworkChannel = api.Network.RegisterChannel("forgottenadmins");
        _clientNetworkChannel.RegisterMessageType(typeof(string));
        _clientNetworkChannel.RegisterMessageType(typeof(ForgottenAdminsData));
        _clientNetworkChannel.RegisterMessageType(typeof(ForgottenAdminsServerData));
        _clientNetworkChannel.SetMessageHandler<ForgottenAdminsData>(OnReceiveData);
        _clientNetworkChannel.SetMessageHandler<ForgottenAdminsServerData>(OnReceiveServerData);

        _dialog = new PlayerMenu(api, _clientNetworkChannel);

        _capi = api;
        _capi.Input.RegisterHotKey("forgottenadminsplayermenu", "ForgottenAdmins", GlKeys.P,
            HotkeyType.GUIOrOtherControls);
        _capi.Input.SetHotKeyHandler("forgottenadminsplayermenu", ToggleGui);
    }

    private void OnReceiveServerData(ForgottenAdminsServerData data)
    {
        _dialog.ForgottenAdminsServerData = data;
    }

    private void OnReceiveData(ForgottenAdminsData data)
    {
        _dialog.OnReceiveData(data);
        if (!_dialog.IsOpened() && _dialog.IsOpen)
        {
            _dialog.TryOpen();
        }
    }

    private bool ToggleGui(KeyCombination comb)
    {
        if (_dialog.IsOpened())
        {
            _dialog.TryClose();
        }
        else
        {
            _dialog.IsOpen = true;
            if (string.IsNullOrEmpty(_dialog.PlayerUid))
            {   
                _dialog.PlayerUid = _capi.World.Player.PlayerUID;
            }
            _clientNetworkChannel.SendPacket(_dialog.PlayerUid);
        }

        return true;
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        _sapi = api;
        _config = api.LoadModConfig<ForgottenAdminsConfig>("ForgottenAdmins/coordinates.json") ?? new ForgottenAdminsConfig();
        _config.PlayerCoordinates ??= new Dictionary<string, List<ForgottenAdminsCustomCoordinate>>();
        _serverNetworkChannel = api.Network.RegisterChannel("forgottenadmins");
        _serverNetworkChannel.RegisterMessageType(typeof(string));
        _serverNetworkChannel.RegisterMessageType(typeof(ForgottenAdminsData));
        _serverNetworkChannel.RegisterMessageType(typeof(ForgottenAdminsServerData));
        _serverNetworkChannel.SetMessageHandler<string>(OnRequestPlayerdata);
        _sapi.Event.PlayerDisconnect += OnPlayerDisconnect;
        
        _emptyInventory = new ForgottenAdminsInventoryData
        {
            HotBar = ToPacket(new DummyInventory(_sapi, 12)),
            Backpack = ToPacket(new DummyInventory(_sapi, 4)),
            Crafting = ToPacket(new DummyInventory(_sapi, 10)),
            Character = ToPacket(new DummyInventory(_sapi, 15)),
            Mouse = ToPacket(new DummyInventory(_sapi, 1)),
        };
        _nullBlockPos = new BlockPos(1);
    }

    private void OnPlayerDisconnect(IServerPlayer byplayer)
    {
        if (_receivedServerData.Remove(byplayer.PlayerUID) && _receivedServerData.Count == 0 && _offlinePlayerData.Count > 0)
        {
            _offlinePlayerData.Clear();
        }
    }

    private void OnRequestPlayerdata(IServerPlayer fromPlayer, string playerUid)
    {
        if (!fromPlayer.HasPrivilege(Privilege.commandplayer) && !fromPlayer.HasPrivilege("forgottenadmins"))
            return;

        if (playerUid.StartsWith("action|", StringComparison.Ordinal))
        {
            HandleAction(fromPlayer, playerUid);
            return;
        }

        var player = (IServerPlayer?)_sapi.World.PlayerByUid(playerUid);

        // load offline playerdata
        player ??= LoadOfflinePlayer(playerUid);
        if (player == null)
        {
            SendEmptyPlayerData(fromPlayer, playerUid);
            return;
        }

        float health = 15, maxHealth = 15, saturation = 1500, maxSaturation = 1500;
        float drunkLevel = ReadFloatAttribute(player.Entity.WatchedAttributes, "intoxication", "alcohol", "drunkLevel", "drunklevel");
        float bodyTemperature = ReadBodyTemperature(player.Entity.WatchedAttributes);
        var healthTree = player.Entity.WatchedAttributes.GetTreeAttribute("health");
        if (healthTree != null)
        {
            health = healthTree.TryGetFloat("currenthealth") ?? 15;
            maxHealth = healthTree.TryGetFloat("maxhealth") ?? 15;
        }

        var hungerTree = player.Entity.WatchedAttributes.GetTreeAttribute("hunger");
        if (hungerTree != null)
        {
            saturation = hungerTree.TryGetFloat("currentsaturation") ?? 1500;
            maxSaturation = hungerTree.TryGetFloat("maxsaturation") ?? 1500;
        }

        var forgottenadminsInventoryData = new ForgottenAdminsInventoryData
        {
            HotBar = ToPacket(player.InventoryManager.GetHotbarInventory()),
            Backpack = ToPacket(player.InventoryManager.GetOwnInventory(GlobalConstants.backpackInvClassName)),
            Crafting = ToPacket(player.InventoryManager.GetOwnInventory(GlobalConstants.craftingInvClassName)),
            Character = ToPacket(player.InventoryManager.GetOwnInventory(GlobalConstants.characterInvClassName)),
            Mouse = ToPacket(player.InventoryManager.GetOwnInventory(GlobalConstants.mousecursorInvClassName))
        };

        var players = GetPlayers();

        var data = new ForgottenAdminsData
        {
            ForgottenAdminsInventoryData = forgottenadminsInventoryData,
            Health = health,
            MaxHealth = maxHealth,
            Saturation = saturation,
            MaxSaturation = maxSaturation,
            DrunkLevel = drunkLevel,
            BodyTemperature = bodyTemperature,
            CurrentGameMode = ((ServerWorldPlayerData)player.WorldData).GameMode,
            FreeMove = player.WorldData.FreeMove,
            NoClip = player.WorldData.NoClip,
            PlayerUid = player.PlayerUID,
            PlayerName = player.PlayerName ?? _sapi.PlayerData.PlayerDataByUid[playerUid].LastKnownPlayername,
            Position = player.Entity.Pos.AsBlockPos,
            MoveSpeedMultiplier = player.WorldData.MoveSpeedMultiplier,
            Privileges = player.Privileges,
            Class = player.Entity.WatchedAttributes.GetString("characterClass"),
            RespawnUses = player.GetSpawnPosition(false).UsesLeft,
            ExtraLandClaimAllowance = player.ServerData.ExtraLandClaimAllowance,
            ExtraLandClaimAreas = player.ServerData.ExtraLandClaimAreas,
            Role = player.ServerData.RoleCode,
            LandClaims = _sapi.WorldManager.SaveGame.LandClaims.Where(l => l.OwnedByPlayerUid != null && l.OwnedByPlayerUid.Equals(playerUid)).ToList(),
            Players = players,
            CustomCoordinates = GetPlayerCoordinates(playerUid)
        };

        _serverNetworkChannel.SendPacket(data, fromPlayer);

        if (_receivedServerData.Contains(fromPlayer.PlayerUID)) return;
        
        _receivedServerData.Add(fromPlayer.PlayerUID);
        var forgottenadminsServerData = new ForgottenAdminsServerData
        {
            Roles = _sapi.Server.Config.Roles.Select(r => r.Code).ToArray()
        };
        _serverNetworkChannel.SendPacket(forgottenadminsServerData, fromPlayer);
    }


    private List<ForgottenAdminsCustomCoordinate> GetPlayerCoordinates(string playerUid)
    {
        if (_config.PlayerCoordinates.TryGetValue(playerUid, out var list))
        {
            return list;
        }
        return new List<ForgottenAdminsCustomCoordinate>();
    }

    private void SaveConfig()
    {
        _sapi.StoreModConfig(_config, "ForgottenAdmins/coordinates.json");
    }

    private void HandleAction(IServerPlayer admin, string packet)
    {
        var parts = packet.Split('|');
        if (parts.Length < 3) return;
        var action = parts[1];
        var targetUid = parts[2];
        var target = _sapi.World.PlayerByUid(targetUid) as IServerPlayer;

        switch (action)
        {
            case "savecoordfromtarget":
            case "savecoord":
            {
                if (parts.Length < 4) return;

                var name = Uri.UnescapeDataString(parts[3]).Trim();
                if (string.IsNullOrWhiteSpace(name)) name = "Позиция игрока";

                int x, y, z;

                // Основной путь: клиент передает отображаемую позицию выбранного игрока.
                // Это работает и для офлайн-игроков, если их позиция уже есть в панели.
                if (parts.Length >= 7
                    && int.TryParse(parts[4], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out x)
                    && int.TryParse(parts[5], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out y)
                    && int.TryParse(parts[6], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out z))
                {
                    // ok
                }
                else if (target?.Entity != null)
                {
                    var pos = target.Entity.Pos.AsBlockPos;
                    x = pos.X;
                    y = pos.Y;
                    z = pos.Z;
                }
                else
                {
                    return;
                }

                _config.PlayerCoordinates ??= new Dictionary<string, List<ForgottenAdminsCustomCoordinate>>();

                if (!_config.PlayerCoordinates.TryGetValue(targetUid, out var list) || list == null)
                {
                    list = new List<ForgottenAdminsCustomCoordinate>();
                    _config.PlayerCoordinates[targetUid] = list;
                }

                var existing = list.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (existing == null)
                {
                    list.Add(new ForgottenAdminsCustomCoordinate { Name = name, X = x, Y = y, Z = z });
                }
                else
                {
                    existing.X = x;
                    existing.Y = y;
                    existing.Z = z;
                }

                SaveConfig();
                OnRequestPlayerdata(admin, targetUid);
                return;
            }
            case "delcoord":
            {
                if (parts.Length < 4) return;
                var name = Uri.UnescapeDataString(parts[3]);
                if (_config.PlayerCoordinates.TryGetValue(targetUid, out var list))
                {
                    list.RemoveAll(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                    SaveConfig();
                }
                OnRequestPlayerdata(admin, targetUid);
                return;
            }
            case "tpme":
            case "tpplayer":
            {
                if (parts.Length < 4) return;
                if (!_config.PlayerCoordinates.TryGetValue(targetUid, out var list)) return;
                var name = Uri.UnescapeDataString(parts[3]);
                var coord = list.FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (coord == null) return;
                var who = action == "tpme" ? admin : target;
                TeleportPlayer(who, coord.X, coord.Y, coord.Z);
                return;
            }
            case "sethealth":
            {
                if (target?.Entity == null || parts.Length < 4 || !float.TryParse(parts[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)) return;
                SetHealth(target, value);
                OnRequestPlayerdata(admin, targetUid);
                return;
            }
            case "setsaturation":
            {
                if (target?.Entity == null || parts.Length < 4 || !float.TryParse(parts[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)) return;
                SetHunger(target, value);
                OnRequestPlayerdata(admin, targetUid);
                return;
            }
            case "setdrunk":
            {
                if (target?.Entity == null || parts.Length < 4 || !float.TryParse(parts[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)) return;
                SetFloatAttribute(target.Entity.WatchedAttributes, value, "intoxication", "alcohol", "drunkLevel", "drunklevel");
                target.Entity.WatchedAttributes.MarkPathDirty("intoxication");
                OnRequestPlayerdata(admin, targetUid);
                return;
            }
            case "setbodytemp":
            {
                if (target?.Entity == null || parts.Length < 4 || !float.TryParse(parts[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)) return;
                SetBodyTemperature(target, value);
                OnRequestPlayerdata(admin, targetUid);
                return;
            }
        }
    }

    private static float ReadBodyTemperature(ITreeAttribute watchedAttributes)
    {
        // Vanilla Vintage Story stores player body temperature in a tree attribute:
        // WatchedAttributes["bodyTemp"]["bodytemp"].
        // Reading top-level "bodytemp" returns 0, which made the UI look like it was reset.
        var bodyTempTree = watchedAttributes.GetTreeAttribute("bodyTemp");
        if (bodyTempTree != null)
        {
            var bodyTemp = bodyTempTree.TryGetFloat("bodytemp")
                ?? bodyTempTree.TryGetFloat("current")
                ?? bodyTempTree.TryGetFloat("value")
                ?? bodyTempTree.TryGetFloat("temperature");

            if (bodyTemp != null) return bodyTemp.Value;
        }

        return ReadFloatAttribute(watchedAttributes, "bodyTemperature", "bodytemp", "temperature");
    }

    private static void SetBodyTemperature(IServerPlayer player, float value)
    {
        var watchedAttributes = player.Entity.WatchedAttributes;

        var bodyTempTree = watchedAttributes.GetTreeAttribute("bodyTemp") ?? new TreeAttribute();
        bodyTempTree.SetFloat("bodytemp", value);
        bodyTempTree.SetFloat("current", value);
        bodyTempTree.SetFloat("value", value);
        bodyTempTree.SetFloat("temperature", value);

        watchedAttributes.SetAttribute("bodyTemp", bodyTempTree);
        watchedAttributes.MarkPathDirty("bodyTemp");

        // Compatibility fallback for mods/older versions that may read a flat key.
        watchedAttributes.SetFloat("bodyTemperature", value);
        watchedAttributes.MarkPathDirty("bodyTemperature");
    }

    private static float ReadFloatAttribute(ITreeAttribute tree, params string[] keys)
    {
        foreach (var key in keys)
        {
            var v = tree.TryGetFloat(key);
            if (v != null) return v.Value;
            var sub = tree.GetTreeAttribute(key);
            var current = sub?.TryGetFloat("current") ?? sub?.TryGetFloat("value") ?? sub?.TryGetFloat("level");
            if (current != null) return current.Value;
        }
        return 0;
    }

    private static void SetFloatAttribute(ITreeAttribute tree, float value, params string[] keys)
    {
        foreach (var key in keys)
        {
            tree.SetFloat(key, value);
            var sub = tree.GetTreeAttribute(key);
            if (sub != null)
            {
                sub.SetFloat("current", value);
                sub.SetFloat("value", value);
                sub.SetFloat("level", value);
            }
        }
    }

    private static void SetHealth(IServerPlayer player, float value)
    {
        var healthTree = player.Entity.WatchedAttributes.GetTreeAttribute("health") ?? new TreeAttribute();
        healthTree.SetFloat("currenthealth", value);
        healthTree.SetFloat("maxhealth", Math.Max(value, healthTree.TryGetFloat("maxhealth") ?? value));
        player.Entity.WatchedAttributes.SetAttribute("health", healthTree);
        player.Entity.WatchedAttributes.MarkPathDirty("health");
    }

    private static void SetHunger(IServerPlayer player, float value)
    {
        var hungerTree = player.Entity.WatchedAttributes.GetTreeAttribute("hunger") ?? new TreeAttribute();
        hungerTree.SetFloat("currentsaturation", value);
        hungerTree.SetFloat("maxsaturation", Math.Max(value, hungerTree.TryGetFloat("maxsaturation") ?? value));
        player.Entity.WatchedAttributes.SetAttribute("hunger", hungerTree);
        player.Entity.WatchedAttributes.MarkPathDirty("hunger");
    }

    private static void TeleportPlayer(IServerPlayer? player, int x, int y, int z)
    {
        player?.Entity?.TeleportToDouble(x + 0.5, y, z + 0.5);
    }

    private void SendEmptyPlayerData(IServerPlayer fromPlayer, string playerUid)
    {
        var players = GetPlayers();
        _sapi.PlayerData.PlayerDataByUid.TryGetValue(playerUid, out var player);
        
        var data = new ForgottenAdminsData
        {
            ForgottenAdminsInventoryData = _emptyInventory,
            Health = -1,
            MaxHealth = -1,
            Saturation = -1,
            MaxSaturation = -1,
            DrunkLevel = -1,
            BodyTemperature = -1,
            CurrentGameMode = EnumGameMode.Guest,
            FreeMove = false,
            NoClip = false,
            PlayerUid = playerUid,
            PlayerName = player?.LastKnownPlayername ?? "unknown",
            Position = _nullBlockPos,
            MoveSpeedMultiplier = -1,
            Privileges = new []{"none"},
            Class = "unknown",
            RespawnUses = -1,
            ExtraLandClaimAllowance = player?.ExtraLandClaimAllowance ?? 0,
            ExtraLandClaimAreas = player?.ExtraLandClaimAreas ?? 0,
            Role = player?.RoleCode ?? "unknown",
            LandClaims = _sapi.WorldManager.SaveGame.LandClaims.Where(l => l.OwnedByPlayerUid != null && l.OwnedByPlayerUid.Equals(playerUid)).ToList(),
            Players = players,
            CustomCoordinates = GetPlayerCoordinates(playerUid)
        };
        _serverNetworkChannel.SendPacket(data, fromPlayer);

        if (_receivedServerData.Contains(fromPlayer.PlayerUID)) return;
        
        _receivedServerData.Add(fromPlayer.PlayerUID);
        var forgottenadminsServerData = new ForgottenAdminsServerData
        {
            Roles = _sapi.Server.Config.Roles.Select(r => r.Code).ToArray()
        };
        _serverNetworkChannel.SendPacket(forgottenadminsServerData, fromPlayer);
    }

    private Dictionary<string, string> GetPlayers()
    {
        var players = new Dictionary<string,string>();
        foreach (var p in _sapi.PlayerData.PlayerDataByUid)
        {
            var isOnline = _sapi.World.AllOnlinePlayers.Any(po => po.PlayerUID.Equals(p.Value.PlayerUID));
            players.Add(p.Value.PlayerUID, isOnline ? p.Value.LastKnownPlayername : p.Value.LastKnownPlayername + " (Offline)");    
        }

        // for (int i = 0; i < 10; i++)
        // {
        //     players.Add($"player-{i}",$"player-{i}");
        // }
        players = players.OrderBy(pair => (pair.Value.EndsWith("Offline)"), pair.Value)).ToDictionary(p => p.Key, p => p.Value);
        return players;
    }

    private IServerPlayer? LoadOfflinePlayer(string playerUid)
    {
        if (_offlinePlayerData.TryGetValue(playerUid, out var offlinePlayer))
        {
            return offlinePlayer;
        }
        
        try
        {
            var server = (ServerMain)_sapi.World;
            var chunkThread =
                typeof(ServerMain).GetField("chunkThread", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(server)
                    as ChunkServerThread;
            var gameDatabase =
                typeof(ChunkServerThread).GetField("gameDatabase", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.GetValue(chunkThread) as GameDatabase;
            var playerData = gameDatabase?.GetPlayerData(playerUid);
            if (playerData == null || playerData.Length == 0)
            {
                return null;
            }
            ServerWorldPlayerData? playerWorldData;
            playerWorldData = SerializerUtil.Deserialize<ServerWorldPlayerData>(playerData);
            playerWorldData.Init(server);
            var serverPlayer = new ServerPlayer(server, playerWorldData);
            var initMethod = typeof(ServerPlayer).GetMethod("Init", BindingFlags.Instance | BindingFlags.NonPublic);
            initMethod?.Invoke(serverPlayer, null);
            if (serverPlayer.InventoryManager.GetOwnInventory(GlobalConstants.backpackInvClassName) is
                InventoryPlayerBackpacks bp)
            {
                var bagInv = typeof(InventoryPlayerBackpacks).GetField("bagInv", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.GetValue(bp) as BagInventory;
                var bagSlots = typeof(InventoryPlayerBackpacks).GetField("bagSlots", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.GetValue(bp) as ItemSlot[];
                bagInv?.ReloadBagInventory(bp,bagSlots);
            }

            _offlinePlayerData.TryAdd(playerUid, serverPlayer);

            return serverPlayer;    
        }
        catch (Exception e)
        {
            Mod.Logger.Error($"Failed loading offline player data for {playerUid}");
            Mod.Logger.Error(e);
            return null;
        }
    }

    private static ForgottenAdminsItemStackData[]? ToPacket(IInventory? inventory)
    {
        if (inventory == null)
        {
            return null;
        }
        var itemStacks = new ForgottenAdminsItemStackData[inventory.Count];
        for (var i = 0; i < inventory.Count; i++)
        {
            if (inventory[i].Itemstack != null)
            {
                var ms = new MemoryStream();
                var writer = new BinaryWriter(ms);

                inventory[i].Itemstack.Attributes.ToBytes(writer);

                itemStacks[i] = new ForgottenAdminsItemStackData
                {
                    ItemClass = (int)inventory[i].Itemstack.Class,
                    ItemId = inventory[i].Itemstack.Id,
                    StackSize = inventory[i].Itemstack.StackSize,
                    Attributes = ms.ToArray()
                };
            }
            else
            {
                itemStacks[i] = new ForgottenAdminsItemStackData
                {
                    ItemClass = -1,
                    ItemId = 0,
                    StackSize = 0
                };
            }

        }
        return itemStacks;
    }
    public static ItemStack? FromPacket(ForgottenAdminsItemStackData fromPacket, IWorldAccessor resolver)
    {
        var attributes = new TreeAttribute();
        if (fromPacket.Attributes != null)
        {
            var ms = new MemoryStream(fromPacket.Attributes);
            var reader = new BinaryReader(ms);
            attributes.FromBytes(reader);
        }

        if(fromPacket.ItemClass == -1)
        {
            return null;
        }

        return new ItemStack(
            fromPacket.ItemId,
            (EnumItemClass)fromPacket.ItemClass,
            fromPacket.StackSize,
            attributes,
            resolver
        );
    }
}