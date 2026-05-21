using System;
using System.Collections.Generic;
using System.Linq;
using Cairo;
using ForgottenAdmins.Data;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace ForgottenAdmins.GUI;

public class PlayerMenu : GuiDialog
{
    internal string PlayerUid;
    private long _everySecondListener;
    private int _selectedPlayerName;
    private readonly IClientNetworkChannel _clientNetworkChannel;

    internal ForgottenAdminsData? ForgottenAdminsData;
    private int _currentTabIndex;
    internal ForgottenAdminsServerData? ForgottenAdminsServerData;
    private bool _updateOnceNeeded;
    private int _selectedLandClaim;
    private BlockPos? _backPosition;
    private string _playerSearchFilter;

    private bool _useRelativePos;
    private string _customCoordName = string.Empty;
    private string _selectedCustomCoordName = string.Empty;

    private GuiComposer PlayerMenuComposer
    {
        get => Composers["playerMenuDialog"];
        set => Composers["playerMenuDialog"] = value;
    }
    
    private GuiComposer? PlayerListComposer
    {
        get => Composers["playerListDialog"];
        set => Composers["playerListDialog"] = value;
    }

    public bool IsOpen { get; set; }

    private string[]? LandClaims { get; set; }

    private string[] SelectedAreas { get; set; }

    private LandClaim? SelectedClaim { get; set; }
    public string[]? SelectedClaimPermittedPlayers { get; set; }

    public List<SavegameCellEntry> PlayerListCells;
    public Dictionary<string, string> PlayerListFull;

    public IInventory HotBarInventory { get; set; }
    public IInventory BackpackInventory { get; set; }

    public IInventory MouseInventory { get; set; }

    public IInventory CharacterInventory { get; set; }

    public IInventory CraftingInventory { get; set; }


    public PlayerMenu(ICoreClientAPI capi, IClientNetworkChannel clientNetworkChannel) : base(capi)
    {
        _clientNetworkChannel = clientNetworkChannel;
        PlayerUid = string.Empty;
        SelectedAreas = new[] { string.Empty };

        HotBarInventory = CreateEditableInventory(capi, "hotbar", 12);
        BackpackInventory = CreateEditableInventory(capi, "backpack", 4);

        CraftingInventory = CreateEditableInventory(capi, "crafting", 10);
        CharacterInventory = CreateEditableInventory(capi, "character", 15);
        MouseInventory = CreateEditableInventory(capi, "mouse", 1);
    }

    public override string ToggleKeyCombinationCode => "forgottenadminsplayermenu";

    private static InventoryGeneric CreateEditableInventory(ICoreAPI api, string name, int slots)
    {
        // DummyInventory uses DummySlot, which is suitable for rendering only.
        // InventoryGeneric creates normal mutable ItemSlot instances, so GuiElementItemSlotGrid
        // can pick up, place and swap stacks with the player's cursor slot.
        return new InventoryGeneric(slots, "forgottenadmins-" + name, Guid.NewGuid().ToString("N"), api);
    }


    private void SetupDialog()
    {
        var dialogBounds = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.CenterMiddle).WithFixedAlignmentOffset(-200,0);
        var contentBounds = ElementBounds.Fixed(5, 40, 1040, 680);

        var bgBounds = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        bgBounds.BothSizing = ElementSizing.FitToChildren;
        bgBounds.WithChildren(contentBounds);

        var tabBoundsL = ElementBounds.Fixed(-180, 25, 180, 700);
        var tabs = new[]
        {
            new GuiTab { DataInt = 0, Name = "Инфо", PaddingTop = 10 },
            new GuiTab { DataInt = 1, Name = "Действия", PaddingTop = 10 },
            new GuiTab { DataInt = 2, Name = "Приваты", PaddingTop = 10 },
            new GuiTab { DataInt = 3, Name = "Клиентские моды", PaddingTop = 10 }
        };

        PlayerMenuComposer = capi.Gui.CreateCompo("playerMenuDialog", dialogBounds)
            .AddShadedDialogBG(bgBounds)
            .AddDialogTitleBar("Меню игрока", OnTitleBarCloseClicked)
            .AddVerticalTabs(tabs, tabBoundsL, OnTabClicked, "verticalTabs");

        switch (_currentTabIndex)
        {
            case 0:
            {
                ComposePlayerMenu(dialogBounds);
                break;
            }
            case 1:
            {
                ComposeCommands();
                break;
            }
            case 2:
            {
                ComposeClaims();
                break;
            }
            case 3:
            {
                ComposeClientMods();
                break;
            }
            default:
            {
                PlayerMenuComposer.Compose();
                break;
            }
        }
        var dialogBoundsList =
                bgBounds.ForkBoundingParent()
                    .WithAlignment(EnumDialogArea.LeftMiddle)
                    .WithFixedAlignmentOffset((dialogBounds.renderX + dialogBounds.OuterWidth + 10) / RuntimeEnv.GUIScale, 0);
        
        var contentBoundsList = ElementBounds.Fixed(5, 40, 360, 680);

        var bgBoundsList = ElementBounds.Fill.WithFixedPadding(GuiStyle.ElementToDialogPadding);
        bgBoundsList.BothSizing = ElementSizing.FitToChildren;
        bgBoundsList.WithChildren(contentBoundsList);

        ElementBounds clippingBounds,listBounds;
        var titleBounds = ElementBounds.Fixed(EnumDialogArea.LeftTop, 0, 0, 360, 35);
        _playerSearchFilter = string.Empty;
        PlayerListComposer = capi.Gui.CreateCompo("playerListDialog", dialogBoundsList)
            .AddShadedDialogBG(bgBoundsList)
            .AddDialogTitleBar("Список игроков", OnTitleBarCloseClicked)
            .AddTextInput(titleBounds = titleBounds.BelowCopy(10, 6).WithFixedSize(360, 30), OnSearchChanged, null, "playerSearch")
            .AddInset(bgBoundsList = titleBounds.BelowCopy(0, 3).WithFixedSize(
                contentBoundsList.fixedWidth,
                contentBoundsList.fixedHeight -3
            ))
            .AddVerticalScrollbar(OnNewScrollbarvalue, ElementStdBounds.VerticalScrollbar(bgBoundsList), "scrollbar")
            .BeginClip(clippingBounds = bgBoundsList.ForkContainingChild(3, 3, 3, 3))
            .AddCellList(listBounds = clippingBounds.ForkContainingChild(0, 0, 0, -3).WithFixedPadding(5),
                CreateCellElem, LoadPlayerCells(), "playerscells")
            .EndClip();
        
        PlayerListComposer.Compose();
        listBounds.CalcWorldBounds();
        clippingBounds.CalcWorldBounds();

        var guiElementTextInput = PlayerListComposer.GetTextInput("playerSearch");
        guiElementTextInput.SetPlaceHolderText(Lang.Get("Поиск..."));
        guiElementTextInput.OnFocusLost();
        PlayerListComposer.GetScrollbar("scrollbar").SetHeights(
            (float)(clippingBounds.fixedHeight),
            (float)(listBounds.fixedHeight)
        );

        PlayerMenuComposer.GetVerticalTab("verticalTabs").SetValue(_currentTabIndex, false);
    }


    private void ComposeClientMods()
    {
        var font = CairoFont.WhiteSmallishText();
        var titleFont = CairoFont.WhiteSmallText().WithWeight(Cairo.FontWeight.Bold);
        var playerName = ForgottenAdminsData?.PlayerName ?? "unknown";
        var mods = ForgottenAdminsData?.ClientMods ?? new List<ForgottenAdminsClientModInfo>();

        var y = 10;
        PlayerMenuComposer
            .AddStaticText($"Клиентские моды: {playerName}", titleFont, ElementBounds.Fixed(15, y, 640, 28));

        y += 34;
        PlayerMenuComposer
            .AddStaticText($"Всего модов: {mods.Count}", font, ElementBounds.Fixed(15, y, 300, 24));

        y += 36;
        PlayerMenuComposer
            .AddStaticText("ModID", titleFont, ElementBounds.Fixed(15, y, 220, 24))
            .AddStaticText("Название", titleFont, ElementBounds.Fixed(250, y, 360, 24))
            .AddStaticText("Версия", titleFont, ElementBounds.Fixed(620, y, 140, 24));

        y += 28;
        if (mods.Count == 0)
        {
            PlayerMenuComposer
                .AddStaticText("Список модов ещё не получен. Игрок должен быть онлайн и иметь установленный ForgottenAdmins на клиенте.", font, ElementBounds.Fixed(15, y, 920, 60));
        }
        else
        {
            foreach (var mod in mods.Take(20))
            {
                PlayerMenuComposer
                    .AddStaticText(mod.ModId ?? string.Empty, font, ElementBounds.Fixed(15, y, 220, 22))
                    .AddStaticText(mod.Name ?? string.Empty, font, ElementBounds.Fixed(250, y, 360, 22))
                    .AddStaticText(mod.Version ?? string.Empty, font, ElementBounds.Fixed(620, y, 140, 22));
                y += 24;
            }

            if (mods.Count > 20)
            {
                PlayerMenuComposer.AddStaticText($"...и ещё {mods.Count - 20}. Полный список записан в ModConfig/ForgottenAdmins/client-mods-log.json", font, ElementBounds.Fixed(15, y + 10, 900, 40));
            }
        }

        PlayerMenuComposer.Compose();
    }

    private void ComposeClaims()
    {
        var font = CairoFont.WhiteSmallishText();
        const int spacing2 = 15;
        var leftText = ElementBounds.Fixed(15, 5, 180, 40);
        var rightDropDown = ElementBounds.Fixed(220, 25, 450, 20);

        LandClaims = ForgottenAdminsData?.LandClaims?.Select(c => $"{c.Description}").ToArray() ?? new[] { string.Empty };
        SelectedClaim = LandClaims.Length > 0 ? ForgottenAdminsData?.LandClaims?.FirstOrDefault() : null;
        SelectedAreas = SelectedClaim?.Areas.Select(a => $"{a.Start}  to  {a.End}").ToArray() ?? new[] { string.Empty };
        SelectedClaimPermittedPlayers = SelectedClaim?.PermittedPlayerUids
            ?.Select(p => $"{capi.World.PlayerByUid(p.Key)?.PlayerName ?? p.Key} : {p.Value}").ToArray();
        if (SelectedClaimPermittedPlayers == null || SelectedClaimPermittedPlayers.Length == 0)
        {
            SelectedClaimPermittedPlayers = new[] { string.Empty };
        }
      
        const int spacing = -2;
        PlayerMenuComposer
            .AddStaticText("Игрок:", font, leftText = leftText.BelowCopy(0, spacing))
            .AddDynamicText(ForgottenAdminsData?.PlayerName ?? "ERROR getting name" , font, rightDropDown = rightDropDown.BelowCopy().WithFixedHeight(30), "playername")
            .AddIf(LandClaims.Length > 0)
            .AddStaticText("Claims:", font, leftText = leftText.BelowCopy(0, spacing + 10))
            .AddDropDown(LandClaims, LandClaims, _selectedLandClaim, OnSelectionClaims,
                rightDropDown = rightDropDown.BelowCopy(0, 10).WithFixedHeight(30), "claims")
            .AddStaticText("Защита:", font, leftText = leftText.BelowCopy(0, spacing))
            .AddDynamicText(SelectedClaim?.ProtectionLevel.ToString() ?? string.Empty, font,
                rightDropDown = rightDropDown.BelowCopy(0, spacing2 - 3), "protection")
            .AddStaticText("Зоны:", font, leftText = leftText.BelowCopy(0, spacing))
            .AddDropDown(SelectedAreas, SelectedAreas, 0, OnSelectionChangeVoid,
                rightDropDown = rightDropDown.BelowCopy(0, 10).WithFixedHeight(30), "areas")
            .AddStaticText("Допущенные:", font, leftText = leftText.BelowCopy(0, spacing))
            .AddDropDown(SelectedClaimPermittedPlayers, SelectedClaimPermittedPlayers, 0, OnSelectionChangeVoid,
                rightDropDown = rightDropDown.BelowCopy(0, 10).WithFixedHeight(30), "permittedplayers")
            .AddStaticText("Доступ всем:", font, leftText.BelowCopy(0, spacing))
            .AddDynamicText(SelectedClaim?.AllowUseEveryone.ToString() ?? string.Empty, font,
                rightDropDown.BelowCopy(0, spacing2 - 8), "allowuseeveryone")
            .EndIf();
        try
        {
            PlayerMenuComposer.Compose();
        }
        catch (Exception e)
        {
            capi.Logger.Error(e.Message, e);
        }
    }
    

    private void OnSearchChanged(string searchText)
    {
        _playerSearchFilter = searchText;
        LoadFilteredPlayerList();
        PlayerListComposer.GetCellList<SavegameCellEntry>("playerscells").ReloadCells(PlayerListCells);
    }

    public void LoadFilteredPlayerList()
    {
        if (ForgottenAdminsData?.Players == null)
        {
            PlayerListCells = new List<SavegameCellEntry>();
            PlayerListFull = new Dictionary<string, string>();
        }
        else
        {
            PlayerListFull = ForgottenAdminsData.Players;
        }

        PlayerListCells = PlayerListFull
            .Where(p => string.IsNullOrEmpty(_playerSearchFilter) || p.Key.Contains(_playerSearchFilter, StringComparison.InvariantCultureIgnoreCase))
            .Select(p => new SavegameCellEntry() { Title = p.Value, DetailText = p.Key}).ToList() ?? new List<SavegameCellEntry>();
        UpdatePlayerSelectionIndex();

        PlayerListComposer?.GetCellList<SavegameCellEntry>("playerscells")?.ReloadCells(PlayerListCells);
    }

    private List<SavegameCellEntry> LoadPlayerCells()
    {
        LoadFilteredPlayerList();
        return PlayerListCells;
    }
    
    private void OnNewScrollbarvalue(float value)
    {
        var bounds = PlayerListComposer.GetCellList<SavegameCellEntry>("playerscells").Bounds;
        bounds.fixedY = 0 - value;

        bounds.CalcWorldBounds();
    }

    private IGuiElementCell CreateCellElem(SavegameCellEntry cell, ElementBounds bounds)
    {
        var cellElem = new GuiElementMainMenuCell(capi, cell, bounds);
        cellElem.ShowModifyIcons = false;
        cellElem.OnMouseDownOnCellLeft = OnClickCellLeft;
        return cellElem;
    }

    private void OnClickCellLeft(int obj)
    {
        foreach (var cellEntry in PlayerListCells)
        {
            cellEntry.Selected = false;
        }

        PlayerListCells[obj].Selected = true;
        _selectedPlayerName = obj;
        PlayerUid = PlayerListCells[obj].DetailText;
        var playerName = PlayerMenuComposer.GetDynamicText("playername");
        playerName?.SetNewText(PlayerListCells[obj].Title);
        _clientNetworkChannel.SendPacket(PlayerUid);

        _updateOnceNeeded = true;
    }


    private string[] CustomCoordNames()
    {
        var list = ForgottenAdminsData?.CustomCoordinates;
        if (list == null || list.Count == 0) return new[] { "Нет сохранённых точек" };
        return list.Select(c => c.Name).ToArray();
    }

    private string CurrentCoordName()
    {
        var names = CustomCoordNames();
        if (string.IsNullOrWhiteSpace(_selectedCustomCoordName) || !names.Contains(_selectedCustomCoordName))
        {
            _selectedCustomCoordName = names[0];
        }
        return _selectedCustomCoordName;
    }

    private string EscapePacket(string value) => Uri.EscapeDataString(value ?? string.Empty);

    private string FloatValue(string key)
    {
        try
        {
            return PlayerMenuComposer.GetNumberInput(key)?.GetValue().ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "0";
        }
        catch
        {
            return "0";
        }
    }

    private bool SendAction(string action, params string[] args)
    {
        if (ForgottenAdminsData?.PlayerUid == null) return true;

        _clientNetworkChannel.SendPacket("action|" + action + "|" + ForgottenAdminsData.PlayerUid + (args.Length > 0 ? "|" + string.Join("|", args) : string.Empty));

        // Не отправляем мгновенный повторный запрос: сервер сам вернет обновленные данные после действия.
        // Мгновенный запрос мог приходить раньше сохранения и затирать выпадающий список старым состоянием.
        return true;
    }

    private void OnCustomCoordSelected(string code, bool selected)
    {
        if (selected) _selectedCustomCoordName = code;
    }

    private void OnCoordNameChanged(string value)
    {
        _customCoordName = value;
    }

    private bool OnSaveCustomCoordClick()
    {
        if (ForgottenAdminsData?.Position == null) return true;

        var name = string.IsNullOrWhiteSpace(_customCoordName) ? $"Позиция игрока {DateTime.Now:HH-mm-ss}" : _customCoordName.Trim();
        _selectedCustomCoordName = name;

        var pos = ForgottenAdminsData.Position;
        return SendAction(
            "savecoordfromtarget",
            EscapePacket(name),
            pos.X.ToString(System.Globalization.CultureInfo.InvariantCulture),
            pos.Y.ToString(System.Globalization.CultureInfo.InvariantCulture),
            pos.Z.ToString(System.Globalization.CultureInfo.InvariantCulture)
        );
    }

    private bool OnDeleteCustomCoordClick()
    {
        var name = CurrentCoordName();
        if (name == "Нет сохранённых точек") return true;
        return SendAction("delcoord", EscapePacket(name));
    }

    private bool OnTpMeToCustomCoordClick()
    {
        var name = CurrentCoordName();
        if (name == "Нет сохранённых точек") return true;
        return SendAction("tpme", EscapePacket(name));
    }

    private bool OnTpPlayerToCustomCoordClick()
    {
        var name = CurrentCoordName();
        if (name == "Нет сохранённых точек") return true;
        return SendAction("tpplayer", EscapePacket(name));
    }

    private bool OnSetHealthClick() => SendAction("sethealth", FloatValue("effecthealth"));
    private bool OnSetSaturationClick() => SendAction("setsaturation", FloatValue("effectsaturation"));
    private bool OnSetDrunkClick() => SendAction("setdrunk", FloatValue("effectdrunk"));
    private bool OnSetBodyTempClick() => SendAction("setbodytemp", FloatValue("effectbodytemp"));

    private void ComposeCommands()
    {
        var font = CairoFont.WhiteSmallishText();
        var fontBtn = CairoFont.ButtonText().WithFontSize(20);
        var leftText = ElementBounds.Fixed(15, 5, 180, 40);
        var rightDropDown = ElementBounds.Fixed(200, 25, 150, 20);

        const int spacing = -2;
        var gameModes = Enum.GetNames(typeof(EnumGameMode));
        var roleIndex = ForgottenAdminsServerData?.Roles.IndexOf(ForgottenAdminsData?.Role) ?? 0;
        PlayerMenuComposer.AddStaticText("Игрок:", font, leftText = leftText.BelowCopy(0, spacing))
            .AddDynamicText(ForgottenAdminsData?.PlayerName ?? "ERROR getting name" , font, rightDropDown = rightDropDown.BelowCopy().WithFixedHeight(30), "playername")
            .AddStaticText("Режим:", font, leftText = leftText.BelowCopy(0, spacing + 10))
            .AddDropDown(gameModes, gameModes, (int)(ForgottenAdminsData?.CurrentGameMode ?? EnumGameMode.Survival), OnSelectionChangeVoid,
                rightDropDown = rightDropDown.BelowCopy(0, 10).WithFixedHeight(30), "gamemodes");
        var buttonBounds = ElementBounds.Fixed(rightDropDown.fixedX + rightDropDown.fixedWidth,
            rightDropDown.fixedY, 100, 20);
        PlayerMenuComposer
            .AddButton("ОК", OnSetGameMode, buttonBounds = buttonBounds.BelowCopy(10, -20), fontBtn);

        PlayerMenuComposer
            .AddStaticText("Роль:", font, leftText = leftText.BelowCopy())
            .AddDropDown(ForgottenAdminsServerData?.Roles, ForgottenAdminsServerData?.Roles, roleIndex, OnSelectionChangeVoid,
                rightDropDown = rightDropDown.BelowCopy(0, 10).WithFixedHeight(30), "roles");
        PlayerMenuComposer
            .AddButton("ОК", OnRoleSet, buttonBounds = buttonBounds.BelowCopy(0, 20), fontBtn);

        PlayerMenuComposer
            .AddStaticText("Доп. приваты:", font, leftText = leftText.BelowCopy())
            .AddHoverText(
                "player specific extra land claim allowance, independent of the allowance set by the role. (default: 0)",
                font, 600, leftText)
            .AddNumberInput(rightDropDown = rightDropDown.BelowCopy(0, 10).WithFixedHeight(30), OnTextChanged,
                key: "landclaimallowance");
        PlayerMenuComposer
            .AddButton("ОК", OnSetLandClaimAllowance, buttonBounds = buttonBounds.BelowCopy(0, 20), fontBtn);

        PlayerMenuComposer
            .AddStaticText("Доп. зоны:", font, leftText = leftText.BelowCopy())
            .AddHoverText(
                "player specific extra land claim max areas, independent of the max areas set by the role. (default: 0)",
                font, 600, leftText)
            .AddNumberInput(rightDropDown.BelowCopy(0, 10).WithFixedHeight(30), OnTextChanged,
                key: "landclaimmaxareas");
        PlayerMenuComposer
            .AddButton("ОК", OnSetLandClaimMaxAreas, buttonBounds.BelowCopy(0, 20), fontBtn);


        leftText = leftText.BelowCopy(0, 30);

        var buttonBounds2 = ElementBounds.Fixed(15, leftText.fixedY, 100, 20);
        var buttonBounds3 = ElementBounds.Fixed(15, leftText.fixedY + 40, 100, 20);
        PlayerMenuComposer
            .AddButton("Бан", OnBanClick, buttonBounds2.FlatCopy(), fontBtn)
            .AddButton("Кик", OnKickClick, buttonBounds2 = buttonBounds2.RightCopy(10), fontBtn)
            .AddButton("К игроку", OnTpToPlayerClick, buttonBounds2 = buttonBounds2.RightCopy(20), fontBtn)
            .AddHoverText("Teleport to selected player", font, 300,
                buttonBounds2)
            .AddButton("К себе", OnTpPlayerToMeClick, buttonBounds2 = buttonBounds2.RightCopy(10), fontBtn)
            .AddHoverText("Телепортировать выбранного игрока к себе", font, 300,
                buttonBounds2)
            .AddButton("Спавн я", OnSpawnMeClick, buttonBounds3 = buttonBounds3.FlatCopy(), fontBtn)
            .AddHoverText("Телепортировать администратора на спавн", font, 300, buttonBounds3)
            .AddButton("Спавн игрок", OnSpawnPlayerClick, buttonBounds3 = buttonBounds3.RightCopy(10), fontBtn)
            .AddHoverText("Телепортировать выбранного игрока на спавн", font, 300, buttonBounds3)
            .AddButton("Класс", OnCharSelClick, buttonBounds3 = buttonBounds3.RightCopy(10), fontBtn)
            .AddHoverText("Allows the player to re-select their class after doing so already.", font, 300,
                buttonBounds3)
            ;

        var sectionY = buttonBounds3.fixedY + 60;
        var coords = CustomCoordNames();
        var coordLabelBounds = ElementBounds.Fixed(15, sectionY, 160, 28);
        var coordInputBounds = ElementBounds.Fixed(180, sectionY + 2, 260, 28);
        var coordDropBounds = ElementBounds.Fixed(455, sectionY + 2, 300, 28);
        var coordButtonBounds = ElementBounds.Fixed(180, sectionY + 42, 150, 25);
        PlayerMenuComposer
            .AddStaticText("Имя точки:", font, coordLabelBounds)
            .AddTextInput(coordInputBounds, OnCoordNameChanged, null, "customcoordname")
            .AddDropDown(coords, coords, Math.Max(0, Array.IndexOf(coords, CurrentCoordName())), OnCustomCoordSelected, coordDropBounds, "customcoords")
            .AddButton("Сохранить позицию игрока", OnSaveCustomCoordClick, coordButtonBounds.FlatCopy().WithFixedWidth(190), fontBtn)
            .AddButton("Я → точка", OnTpMeToCustomCoordClick, coordButtonBounds = ElementBounds.Fixed(380, sectionY + 42, 120, 25), fontBtn)
            .AddButton("Игрок → точка", OnTpPlayerToCustomCoordClick, coordButtonBounds = ElementBounds.Fixed(510, sectionY + 42, 145, 25), fontBtn)
            .AddButton("Удалить", OnDeleteCustomCoordClick, coordButtonBounds = ElementBounds.Fixed(665, sectionY + 42, 90, 25), fontBtn)
            .AddHoverText("Сохраняет текущую позицию выбранного игрока. Ручной ввод координат больше не используется: вводится только имя точки.", font, 620, coordLabelBounds);

        var effY = sectionY + 90;
        var effLabel = ElementBounds.Fixed(15, effY, 160, 28);
        var effInput = ElementBounds.Fixed(180, effY + 2, 110, 25);
        var effButton = ElementBounds.Fixed(305, effY, 80, 25);
        PlayerMenuComposer
            .AddStaticText("Здоровье:", font, effLabel.FlatCopy())
            .AddNumberInput(effInput.FlatCopy(), OnTextChanged, key: "effecthealth")
            .AddButton("ОК", OnSetHealthClick, effButton.FlatCopy(), fontBtn)
            .AddStaticText("Голод/сытость:", font, effLabel = effLabel.BelowCopy(0, 12))
            .AddNumberInput(effInput = effInput.BelowCopy(0, 12), OnTextChanged, key: "effectsaturation")
            .AddButton("ОК", OnSetSaturationClick, effButton = effButton.BelowCopy(0, 12), fontBtn)
            .AddStaticText("Опьянение:", font, effLabel = effLabel.BelowCopy(0, 12))
            .AddNumberInput(effInput = effInput.BelowCopy(0, 12), OnTextChanged, key: "effectdrunk")
            .AddButton("ОК", OnSetDrunkClick, effButton = effButton.BelowCopy(0, 12), fontBtn)
            .AddStaticText("Темп. тела:", font, effLabel = effLabel.BelowCopy(0, 12))
            .AddNumberInput(effInput = effInput.BelowCopy(0, 12), OnTextChanged, key: "effectbodytemp")
            .AddButton("ОК", OnSetBodyTempClick, effButton = effButton.BelowCopy(0, 12), fontBtn)
            .AddHoverText("Изменение здоровья, сытости, уровня опьянения и температуры тела применяется к выбранному игроку серверной частью мода.", font, 520, effLabel);

        var leftText2 = ElementBounds.Fixed(15, effY + 185, 105, 40);
        var rightDropDown2 = ElementBounds.Fixed(120, leftText2.fixedY + 10, 220, 20);
        PlayerMenuComposer
            .AddStaticText("Назад:", font, leftText2 = leftText2.BelowCopy())
            .AddHoverText("When first opened this position is saved so you can TP here. Use set to update to current position.",font , 300, leftText2)
            .AddDynamicText(GetPosString(_backPosition), font, rightDropDown2 = rightDropDown2.BelowCopy(0, 10).WithFixedHeight(30),
                key: "tpbackpos");
        var buttonBounds4 = ElementBounds.Fixed(buttonBounds.fixedX - buttonBounds.fixedWidth, rightDropDown2.fixedY, 100, 20);
        PlayerMenuComposer
            .AddButton("ОК", OnTpBackSet, buttonBounds4 = buttonBounds4.RightCopy(), fontBtn)
            .AddButton("ТП назад", OnTpBack, buttonBounds4.RightCopy(10), fontBtn);

        PlayerMenuComposer.Compose();

        PlayerMenuComposer.GetNumberInput("landclaimallowance").SetValue(ForgottenAdminsData?.ExtraLandClaimAllowance ?? 0);
        PlayerMenuComposer.GetNumberInput("landclaimmaxareas").SetValue(ForgottenAdminsData?.ExtraLandClaimAreas ?? 0);
        PlayerMenuComposer.GetNumberInput("effecthealth")?.SetValue(ForgottenAdminsData?.Health ?? 0);
        PlayerMenuComposer.GetNumberInput("effectsaturation")?.SetValue(ForgottenAdminsData?.Saturation ?? 0);
        PlayerMenuComposer.GetNumberInput("effectdrunk")?.SetValue(ForgottenAdminsData?.DrunkLevel ?? 0);
        PlayerMenuComposer.GetNumberInput("effectbodytemp")?.SetValue(ForgottenAdminsData?.BodyTemperature ?? 0);
    }

    private bool OnTpBackSet()
    {
        _backPosition = capi.World.Player.Entity.Pos.AsBlockPos;
        PlayerMenuComposer.GetDynamicText("tpbackpos").SetNewText(GetPosString(_backPosition));
        return true;
    }

    private bool OnTpBack()
    {
        if (_backPosition != null)
            capi.SendChatMessage($"/tp ={_backPosition.X} {_backPosition.Y} ={_backPosition.Z}");
        return true;
    }


    private bool OnSpawnMeClick()
    {
        var spawn = capi.World.DefaultSpawnPosition;
        capi.SendChatMessage($"/tp {capi.World.Player.PlayerName} ={(int)spawn.X} {(int)spawn.Y} ={(int)spawn.Z}");
        return true;
    }

    private bool OnSpawnPlayerClick()
    {
        var spawn = capi.World.DefaultSpawnPosition;
        capi.SendChatMessage($"/tp {ForgottenAdminsData?.PlayerName} ={(int)spawn.X} {(int)spawn.Y} ={(int)spawn.Z}");
        return true;
    }

    private bool OnCharSelClick()
    {
        capi.SendChatMessage($"/player {ForgottenAdminsData?.PlayerName} allowcharselonce");
        return true;
    }

    private bool OnRoleSet()
    {
        var role = PlayerMenuComposer.GetDropDown("roles").SelectedValue.ToLower();
        capi.SendChatMessage($"/player {ForgottenAdminsData?.PlayerName} role {role}");
        return true;
    }

    private bool OnSetLandClaimAllowance()
    {
        var num = (int)PlayerMenuComposer.GetNumberInput("landclaimallowance").GetValue();
        capi.SendChatMessage($"/player {ForgottenAdminsData?.PlayerName} landclaimallowance {num}");
        return true;
    }

    private bool OnSetLandClaimMaxAreas()
    {
        var num = (int)PlayerMenuComposer.GetNumberInput("landclaimmaxareas").GetValue();
        capi.SendChatMessage($"/player {ForgottenAdminsData?.PlayerName} landclaimmaxareas {num}");
        return true;
    }

    private static void OnTextChanged(string obj)
    {
    }

    private bool OnSetGameMode()
    {
        var gameMode = PlayerMenuComposer.GetDropDown("gamemodes").SelectedValue.ToLower();

        capi.SendChatMessage($"/gamemode {ForgottenAdminsData?.PlayerName} {gameMode}");
        return true;
    }

    private void ComposePlayerMenu(ElementBounds dialogBounds)
    {
        var leftText = ElementBounds.Fixed(15, 5, 150, 40);
        var rightDropDown = ElementBounds.Fixed(170, 25, 300, 20);

        var offhandBounds = ElementStdBounds.SlotGrid(EnumDialogArea.LeftFixed, 20, 0, 10, 1);
        var hotBarBounds = ElementStdBounds.SlotGrid(EnumDialogArea.LeftFixed, 80, 0, 10, 1);
        var backpackBounds = ElementStdBounds.SlotGrid(EnumDialogArea.RightFixed, -180, 0, 4, 1)
            .WithFixedAlignmentOffset(-10, 0);

        var rows = BackpackInventory.Count > 0 ? (int)Math.Ceiling(BackpackInventory.Count / 6f) : 0;

        const double pad = 3.0;
        var craftingGridBounds = ElementStdBounds.SlotGrid(EnumDialogArea.RightFixed, -292, 50, 3, 3);

        const int spacing = -2;
        const int spacing2 = 15;

        var font = CairoFont.WhiteSmallishText();

        PlayerMenuComposer.AddStaticText("Игрок:", font, leftText = leftText.BelowCopy(0, spacing))
            .AddDynamicText(ForgottenAdminsData?.PlayerName ?? "ERROR getting name" , font, rightDropDown = rightDropDown.BelowCopy().WithFixedHeight(30), "playername")
            .AddStaticText("GM / FM / NC:", font, leftText = leftText.BelowCopy(0, spacing), "gmt")
            ;
        PlayerMenuComposer.AddDynamicText(
                $"{ForgottenAdminsData?.CurrentGameMode} / {ForgottenAdminsData?.FreeMove} / {ForgottenAdminsData?.NoClip}", font,
                rightDropDown = rightDropDown.BelowCopy(), "gm")
            ;
        PlayerMenuComposer.AddHoverText("Gamemod / FreeMove / NoClip", font, 300, leftText)
            ;
        PlayerMenuComposer.AddStaticText("Права:", font, leftText = leftText.BelowCopy(0, spacing))
            .AddDropDown(ForgottenAdminsData?.Privileges, ForgottenAdminsData?.Privileges, 0,
                OnSelectionChangeVoid,
                rightDropDown = rightDropDown.BelowCopy(0, spacing2 - 10), "privileges")
            .AddStaticText("UID:", font, leftText = leftText.BelowCopy(0, spacing))
            .AddDynamicText(ForgottenAdminsData?.PlayerUid, font,
                rightDropDown = rightDropDown.BelowCopy(0, spacing2 - 3), "uid")
            .AddStaticText("Здоровье:", font, leftText = leftText.BelowCopy(0, spacing))
            .AddDynamicText($"{ForgottenAdminsData?.Health} / {ForgottenAdminsData?.MaxHealth}", font,
                rightDropDown = rightDropDown.BelowCopy(0, spacing2 - 8), "health")
            .AddStaticText("Сытость:", font, leftText = leftText.BelowCopy(0, spacing))
            .AddDynamicText($"{ForgottenAdminsData?.Saturation} / {ForgottenAdminsData?.MaxSaturation}", font,
                rightDropDown = rightDropDown.BelowCopy(0, spacing2 - 5), "hunger")
            .AddStaticText("Поз. / скорость:", font, leftText = leftText.BelowCopy(0, spacing))
            .AddDynamicText(GetPosString(ForgottenAdminsData?.Position) + " / " + ForgottenAdminsData?.MoveSpeedMultiplier, font,
                rightDropDown = rightDropDown.BelowCopy(0, spacing2 - 8).WithFixedWidth(600), "pos")
            .AddHoverText("Position / MoveSpeed", font, 300, leftText)
            .AddStaticText("Класс:", font, leftText = leftText.BelowCopy(0, spacing))
            .AddDynamicText(ForgottenAdminsData?.Class, font,
                rightDropDown = rightDropDown.BelowCopy(0, spacing2 - 8).WithFixedWidth(600), "class")
            .AddStaticText("Роль:", font, leftText = leftText.BelowCopy(0, spacing))
            .AddDynamicText(ForgottenAdminsData?.Role, font,
                rightDropDown = rightDropDown.BelowCopy(0, spacing2 - 8).WithFixedWidth(600), "role")
            .AddStaticText("Респавн:", font, leftText = leftText.BelowCopy(0, spacing))
            .AddHoverText("Respawn uses left if set (99 usually means none is set)", font, 300, leftText)
            .AddDynamicText(ForgottenAdminsData?.RespawnUses.ToString(), font,
                rightDropDown.BelowCopy(0, spacing2 - 8).WithFixedWidth(600), "respawn")
            .AddStaticText("Инвентарь:", font, leftText = leftText.BelowCopy(0, spacing))
            .AddStaticText("Можно перетаскивать предметы между слотами. Изменения сразу отправляются на сервер.", font, ElementBounds.Fixed(15, 635, 760, 24));

        leftText = leftText.BelowCopy();

        var y = leftText.BelowCopy().fixedY - 40;
        offhandBounds.fixedY = y;
        hotBarBounds.fixedY = y;
        backpackBounds.fixedY = y;
        const int posSlot = 470;

        var slotGridBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, posSlot + pad, 210 + pad, 6, 4)
            .FixedGrow(2 * pad, 2 * pad);
        var fullGridBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, 0, 0, 6, rows);

        var creativeClippingBounds = slotGridBounds.ForkBoundingParent();
        var insetBounds = creativeClippingBounds.ForkBoundingParent(6, 3, 0, 3);
        var scrollbarBounds = ElementStdBounds.VerticalScrollbar(insetBounds).WithParent(dialogBounds);

        scrollbarBounds.fixedOffsetX -= 2;
        scrollbarBounds.fixedWidth = 15;


        PlayerMenuComposer.AddItemSlotGrid(HotBarInventory, SendInvPacket, 1, new[] { 11 }, offhandBounds,
                "offhandgrid")
            .AddItemSlotGrid(HotBarInventory, SendInvPacket, 10, Enumerable.Range(0, Math.Min(HotBarInventory.Count, 10)).ToArray(),
                hotBarBounds, "hotbargrid")
            .AddItemSlotGrid(BackpackInventory, SendInvPacket, 4, Enumerable.Range(0, Math.Min(BackpackInventory.Count, 4)).ToArray(), backpackBounds,
                "backpackgrid")
            .AddInset(insetBounds)
            .BeginClip(creativeClippingBounds)
            .AddItemSlotGridExcl(BackpackInventory, SendInvPacket, 6, Enumerable.Range(0, Math.Min(BackpackInventory.Count, 4)).ToArray(), fullGridBounds,
                "slotgrid")
            .EndClip()
            .AddVerticalScrollbar(OnNewScrollbarValue, scrollbarBounds, "scrollbar");

        PlayerMenuComposer.AddItemSlotGrid(CraftingInventory, SendInvPacket, 3, Enumerable.Range(0, Math.Min(CraftingInventory.Count, 9)).ToArray(),
                craftingGridBounds.FlatCopy(),
                "craftinggrid")
            .AddItemSlotGrid(CraftingInventory, SendInvPacket, 1, new[] { 9 },
                craftingGridBounds = craftingGridBounds.RightCopy(0, 50), "outputslot");

        PlayerMenuComposer.AddItemSlotGrid(MouseInventory, SendInvPacket, 1, new[] { 0 },
            craftingGridBounds.RightCopy(-40), "mouseslot");

        PlayerMenuComposer.GetSlotGrid("offhandgrid").CanClickSlot = CanClickSlot;
        PlayerMenuComposer.GetSlotGrid("hotbargrid").CanClickSlot = CanClickSlot;
        PlayerMenuComposer.GetSlotGrid("backpackgrid").CanClickSlot = CanClickSlot;
        PlayerMenuComposer.GetSlotGridExcl("slotgrid").CanClickSlot = CanClickSlot;

        PlayerMenuComposer.GetSlotGrid("craftinggrid").CanClickSlot = CanClickSlot;
        PlayerMenuComposer.GetSlotGrid("outputslot").CanClickSlot = CanClickSlot;

        PlayerMenuComposer.GetSlotGrid("mouseslot").CanClickSlot = CanClickSlot;


        const int cx = 820;
        const int cy = 170;
        var leftArmorSlotBoundsHead =
            ElementStdBounds.SlotGrid(EnumDialogArea.None, cx, cy + pad, 1, 1).FixedGrow(0, pad);
        var leftSlotBounds = ElementStdBounds.SlotGrid(EnumDialogArea.None, cx, cy + pad, 1, 6).FixedGrow(0, pad);
        leftSlotBounds.FixedRightOf(leftArmorSlotBoundsHead, 10);

        var leftArmorSlotBoundsBody = ElementStdBounds.SlotGrid(EnumDialogArea.None, cx, cy + pad + 102, 1, 1)
            .FixedGrow(0, pad);
        leftSlotBounds.FixedRightOf(leftArmorSlotBoundsBody, 10);

        var leftArmorSlotBoundsLegs = ElementStdBounds.SlotGrid(EnumDialogArea.None, cx, cy + pad + 204, 1, 1)
            .FixedGrow(0, pad);
        leftSlotBounds.FixedRightOf(leftArmorSlotBoundsLegs, 10);

        var rightSlotBounds =
            ElementStdBounds.SlotGrid(EnumDialogArea.None, cx, cy + pad, 1, 6).FixedGrow(0, pad);
        rightSlotBounds.FixedRightOf(leftSlotBounds, 10);

        leftSlotBounds.fixedHeight -= 6;
        rightSlotBounds.fixedHeight -= 6;
        PlayerMenuComposer
            .AddItemSlotGrid(CharacterInventory, SendInvPacket, 1, new[] { 12 }, leftArmorSlotBoundsHead,
                "armorSlotsHead")
            .AddItemSlotGrid(CharacterInventory, SendInvPacket, 1, new[] { 13 }, leftArmorSlotBoundsBody,
                "armorSlotsBody")
            .AddItemSlotGrid(CharacterInventory, SendInvPacket, 1, new[] { 14 }, leftArmorSlotBoundsLegs,
                "armorSlotsLegs")
            .AddItemSlotGrid(CharacterInventory, SendInvPacket, 1, new[] { 0, 1, 2, 11, 3, 4 }, leftSlotBounds,
                "leftSlots")
            .AddItemSlotGrid(CharacterInventory, SendInvPacket, 1, new[] { 6, 7, 8, 10, 5, 9 }, rightSlotBounds,
                "rightSlots");

        PlayerMenuComposer.Compose();


        PlayerMenuComposer.GetScrollbar("scrollbar").SetHeights(
            (float)slotGridBounds.fixedHeight,
            (float)(fullGridBounds.fixedHeight + pad)
        );

        PlayerMenuComposer.GetSlotGrid("armorSlotsHead").CanClickSlot = CanClickSlot;
        PlayerMenuComposer.GetSlotGrid("armorSlotsBody").CanClickSlot = CanClickSlot;
        PlayerMenuComposer.GetSlotGrid("armorSlotsLegs").CanClickSlot = CanClickSlot;
        PlayerMenuComposer.GetSlotGrid("leftSlots").CanClickSlot = CanClickSlot;
        PlayerMenuComposer.GetSlotGrid("rightSlots").CanClickSlot = CanClickSlot;
    }

    private string GetPosString(BlockPos? pos)
    {
        var outPos = pos;
        if (_useRelativePos)
        {
            outPos = pos?.SubCopy((int)capi.World.DefaultSpawnPosition.X, 0, (int)capi.World.DefaultSpawnPosition.Z);
        }
        return outPos?.ToString() ?? "-";
    }

    private void OnTabClicked(int arg1, GuiTab tab)
    {
        _currentTabIndex = tab.DataInt;
        PlayerMenuComposer.GetVerticalTab("verticalTabs").SetValue(_currentTabIndex, false);
        SetupDialog();
    }

    private void OnNewScrollbarValue(float value)
    {
        if (!IsOpened()) return;
        var bounds = PlayerMenuComposer.GetSlotGridExcl("slotgrid").Bounds;
        bounds.fixedY = 10 - GuiElementItemSlotGridBase.unscaledSlotPadding - value;

        bounds.CalcWorldBounds();
    }

    private bool OnKickClick()
    {
        if (ForgottenAdminsData?.PlayerUid?.Equals(capi.World.Player.PlayerUID) == true)
        {
            capi.ShowChatMessage("Don't kick yourself :P");
            return true;
        }

        capi.SendChatMessage($"/kick {ForgottenAdminsData?.PlayerName} by admin");
        PlayerUid = capi.World.Player.PlayerUID;
        UpdatePlayerSelectionIndex();
        _updateOnceNeeded = true;
        return true;
    }

    private bool OnBanClick()
    {
        if (ForgottenAdminsData?.PlayerUid?.Equals(capi.World.Player.PlayerUID) == true)
        {
            capi.ShowChatMessage("Don't ban yourself :P");
            return true;
        }

        capi.SendChatMessage($"/ban {ForgottenAdminsData?.PlayerName} 50 year by admin");
        PlayerUid = capi.World.Player.PlayerUID;
        UpdatePlayerSelectionIndex();
        _updateOnceNeeded = true;
        return true;
    }

    private void UpdatePlayerSelectionIndex()
    {
        for (var i = 0; i < ForgottenAdminsData?.Players?.Count; i++)
        {
            if (ForgottenAdminsData?.Players?.Keys.ElementAt(i).Equals(PlayerUid) != true) continue;

            _selectedPlayerName = i;
            foreach (var cellEntry in PlayerListCells)
            {
                cellEntry.Selected = cellEntry.DetailText.Equals(PlayerUid);
            }
            var playerName = PlayerMenuComposer.GetDynamicText("playername");
            if(playerName != null)
            {
                playerName.SetNewText(ForgottenAdminsData?.PlayerName ?? "ERROR get name");
                playerName.RecomposeText();
            }
            break;
        }
    }

    private static bool CanClickSlot(int slotId)
    {
        return true;
    }

    private bool OnTpToPlayerClick()
    {
        capi.SendChatMessage($"/tp {capi.World.Player.PlayerName} {ForgottenAdminsData?.PlayerName}");
        return true;
    }

    private bool OnTpPlayerToMeClick()
    {
        capi.SendChatMessage($"/tp {ForgottenAdminsData?.PlayerName} {capi.World.Player.PlayerName}");
        return true;
    }

    private void SendInvPacket(object obj)
    {
        if (ForgottenAdminsData?.PlayerUid == null) return;

        // GuiElementItemSlotGrid performs click/drag operations through the local player's
        // real mouse cursor slot. If we only send the visible target inventories here, a
        // picked-up stack has already been removed from the target slot but is not present
        // in our preview MouseInventory yet. The server then receives an empty source slot
        // and the item looks like it disappeared.
        //
        // Before sending the edit packet, mirror the local cursor slot into the preview
        // mouse inventory. On the server this is applied to the target player's mouse cursor
        // inventory, so the stack survives the first click and can be placed by the next one.
        SyncLocalMouseCursorToPreviewMouseInventory();

        _clientNetworkChannel.SendPacket(new ForgottenAdminsInventoryEditPacket
        {
            TargetPlayerUid = ForgottenAdminsData.PlayerUid,
            Inventory = new ForgottenAdminsInventoryData
            {
                HotBar = ForgottenAdmins.ToPacket(HotBarInventory),
                Backpack = ForgottenAdmins.ToPacket(BackpackInventory),
                Crafting = ForgottenAdmins.ToPacket(CraftingInventory),
                Character = ForgottenAdmins.ToPacket(CharacterInventory),
                Mouse = ForgottenAdmins.ToPacket(MouseInventory)
            }
        });
    }

    private void SyncLocalMouseCursorToPreviewMouseInventory()
    {
        if (MouseInventory.Count <= 0) return;

        try
        {
            var localMouseInventory = capi.World.Player?.InventoryManager
                ?.GetOwnInventory(GlobalConstants.mousecursorInvClassName);

            var localMouseStack = localMouseInventory != null && localMouseInventory.Count > 0
                ? localMouseInventory[0].Itemstack
                : null;

            MouseInventory[0].Itemstack = localMouseStack?.Clone();
            MouseInventory[0].MarkDirty();
        }
        catch
        {
            // If the local cursor inventory cannot be resolved for some reason, keep the
            // previous preview mouse slot instead of throwing from a GUI click handler.
        }
    }

    private static void OnSelectionChangeVoid(string code, bool selected)
    {
    }

    private void OnSelectionClaims(string code, bool selected)
    {
        // protection
        //     areas
        // permittedplayers
        LandClaims = ForgottenAdminsData?.LandClaims?.Select(c => $"{c.Description}").ToArray() ?? new[] { string.Empty };
        var index = 0;
        for (var i = 0; i < LandClaims?.Length; i++)
        {
            if (!LandClaims[i].Equals(code)) continue;

            _selectedLandClaim = index = i;
            break;
        }

        SelectedClaim = ForgottenAdminsData?.LandClaims?.ElementAt(index);
        UpdateClaimsMenu();
    }

    private void UpdateClaimsMenu()
    {
        PlayerMenuComposer.GetDropDown("claims")?.SetList(LandClaims, LandClaims);
        PlayerMenuComposer.GetDropDown("claims")?.SetSelectedIndex(_selectedLandClaim);

        SelectedAreas = SelectedClaim?.Areas?.Select(a => $"{a.Start}  to  {a.End}").ToArray() ?? new[] { string.Empty };
        SelectedClaimPermittedPlayers = SelectedClaim?.PermittedPlayerUids
            .Select(p => $"{capi.World.PlayerByUid(p.Key)?.PlayerName ?? p.Key} : {p.Value}").ToArray();

        if (SelectedClaimPermittedPlayers == null || SelectedClaimPermittedPlayers.Length == 0)
        {
            SelectedClaimPermittedPlayers = new[] { string.Empty };
        }

        if (SelectedClaim != null)
        {
            PlayerMenuComposer.GetDynamicText("protection")?.SetNewText(SelectedClaim.ProtectionLevel.ToString());
            PlayerMenuComposer.GetDynamicText("areasnum")?.SetNewText(SelectedClaim?.Areas?.Count.ToString());
        }

        PlayerMenuComposer.GetDropDown("permittedplayers")
            ?.SetList(SelectedClaimPermittedPlayers, SelectedClaimPermittedPlayers);
        PlayerMenuComposer.GetDropDown("permittedplayers")?.SetSelectedIndex(0);
        PlayerMenuComposer.GetDropDown("areas")?.SetList(SelectedAreas, SelectedAreas);
        PlayerMenuComposer.GetDropDown("areas")?.SetSelectedIndex(0);
    }

    public override bool TryOpen()
    {
        if (ForgottenAdminsData?.Players?.Count > _selectedPlayerName)
        {
            PlayerUid = ForgottenAdminsData?.Players.ElementAt(_selectedPlayerName).Key ?? capi.World.Player.PlayerUID;
        }
        else
        {
            PlayerUid = ForgottenAdminsData?.Players?.First().Key ?? capi.World.Player.PlayerUID;
            _selectedPlayerName = 0;
        }

        _everySecondListener = capi.Event.RegisterGameTickListener(OnEvery1s, 1000);

        _backPosition ??= capi.World.Player?.Entity?.Pos?.AsBlockPos ?? new BlockPos(0);
        _useRelativePos = capi.Settings.Bool.Get("forgottenadminsUseRelativePos");

        SetupDialog();
        return base.TryOpen();
    }

    private void OnEvery1s(float obj)
    {
        if (!IsOpened()) return;

        UpdatePlayerData();
    }

    private void UpdateCommands()
    {
        if (ForgottenAdminsData == null) return;
        PlayerMenuComposer.GetNumberInput("landclaimallowance")?.SetValue(ForgottenAdminsData.ExtraLandClaimAllowance);
        PlayerMenuComposer.GetNumberInput("landclaimmaxareas")?.SetValue(ForgottenAdminsData.ExtraLandClaimAreas);

        PlayerMenuComposer.GetDropDown("gamemodes")?.SetSelectedIndex((int)ForgottenAdminsData.CurrentGameMode);
        PlayerMenuComposer.GetDropDown("roles")?.SetSelectedIndex(ForgottenAdminsServerData?.Roles.IndexOf(ForgottenAdminsData.Role) ?? 0);
        var coordNames = CustomCoordNames();
        PlayerMenuComposer.GetDropDown("customcoords")?.SetList(coordNames, coordNames);
        PlayerMenuComposer.GetNumberInput("effecthealth")?.SetValue(ForgottenAdminsData.Health);
        PlayerMenuComposer.GetNumberInput("effectsaturation")?.SetValue(ForgottenAdminsData.Saturation);
        PlayerMenuComposer.GetNumberInput("effectdrunk")?.SetValue(ForgottenAdminsData.DrunkLevel);
        PlayerMenuComposer.GetNumberInput("effectbodytemp")?.SetValue(ForgottenAdminsData.BodyTemperature);
    }

    private void UpdatePlayerData()
    {
        var guiPlayerNames = PlayerMenuComposer.GetDropDown("playernames");
        guiPlayerNames?.SetList(ForgottenAdminsData?.Players?.Keys.ToArray(), ForgottenAdminsData?.Players?.Values.ToArray());

        _clientNetworkChannel.SendPacket(PlayerUid);
    }

    private void UpdatePlayerMenu()
    {
        if (ForgottenAdminsData == null) return;

        var priv = PlayerMenuComposer.GetDropDown("privileges");
        priv?.SetList(ForgottenAdminsData.Privileges, ForgottenAdminsData.Privileges);
        if (priv?.SelectedIndices[0] > ForgottenAdminsData?.Privileges?.Length)
        {
            priv.SetSelectedIndex(0);
        }

        var gm = PlayerMenuComposer.GetDynamicText("gm");
        gm?.SetNewTextAsync(
            $"{ForgottenAdminsData?.CurrentGameMode} / {ForgottenAdminsData?.FreeMove} / {ForgottenAdminsData?.NoClip}");

        var pos = PlayerMenuComposer.GetDynamicText("pos");
        if (capi.World.Config.GetBool("allowCoordinateHud"))
        {
            pos?.SetNewTextAsync(GetPosString(ForgottenAdminsData?.Position) + " / " + ForgottenAdminsData?.MoveSpeedMultiplier);
        }
        else
        {
            pos?.SetNewTextAsync("Disabled / " + ForgottenAdminsData?.MoveSpeedMultiplier);
        }

        var playerClass = PlayerMenuComposer.GetDynamicText("class");
        playerClass?.SetNewTextAsync(ForgottenAdminsData?.Class);

        var role = PlayerMenuComposer.GetDynamicText("role");
        role?.SetNewTextAsync(ForgottenAdminsData?.Role);

        var respawn = PlayerMenuComposer.GetDynamicText("respawn");
        respawn?.SetNewTextAsync(ForgottenAdminsData?.RespawnUses.ToString());

        var uid = PlayerMenuComposer.GetDynamicText("uid");
        uid.SetNewTextAsync(ForgottenAdminsData?.PlayerUid);

        var healthText = PlayerMenuComposer.GetDynamicText("health");
        healthText?.SetNewTextAsync($"{ForgottenAdminsData?.Health} / {ForgottenAdminsData?.MaxHealth}");
        var hunger = PlayerMenuComposer.GetDynamicText("hunger");
        hunger?.SetNewTextAsync($"{ForgottenAdminsData?.Saturation} / {ForgottenAdminsData?.MaxSaturation}");
        if (ForgottenAdminsData?.ForgottenAdminsInventoryData?.Backpack?.Length != BackpackInventory.Count)
        {
            BackpackInventory = CreateEditableInventory(capi, "backpack", ForgottenAdminsData?.ForgottenAdminsInventoryData?.Backpack?.Length ?? 0);
            SetupDialog();
        }

        for (var i = 0; i < ForgottenAdminsData?.ForgottenAdminsInventoryData?.HotBar?.Length; i++)
        {
            UpdateSlot(HotBarInventory[i], ForgottenAdminsData.ForgottenAdminsInventoryData.HotBar[i]);
        }

        for (var i = 0; i < ForgottenAdminsData?.ForgottenAdminsInventoryData?.Backpack?.Length; i++)
        {
            UpdateSlot(BackpackInventory[i], ForgottenAdminsData.ForgottenAdminsInventoryData.Backpack[i]);
        }

        for (var i = 0; i < ForgottenAdminsData?.ForgottenAdminsInventoryData?.Character?.Length; i++)
        {
            // do not update slots that we do not know about (other mods may add more slots)
            if (i < CharacterInventory.Count)
            {
                UpdateSlot(CharacterInventory[i], ForgottenAdminsData.ForgottenAdminsInventoryData.Character[i]);
            }
        }

        for (var i = 0; i < ForgottenAdminsData?.ForgottenAdminsInventoryData?.Crafting?.Length; i++)
        {
            UpdateSlot(CraftingInventory[i], ForgottenAdminsData.ForgottenAdminsInventoryData.Crafting[i]);
        }

        if (ForgottenAdminsData?.ForgottenAdminsInventoryData?.Mouse?[0] != null)
            UpdateSlot(MouseInventory[0], ForgottenAdminsData.ForgottenAdminsInventoryData.Mouse[0]);
    }

    public override bool TryClose()
    {
        IsOpen = false;
        capi.Event.UnregisterGameTickListener(_everySecondListener);
        return base.TryClose();
    }

    private void OnTitleBarCloseClicked()
    {
        TryClose();
    }

    internal void OnReceiveData(ForgottenAdminsData data)
    {
        // store old player list for search
        PlayerListFull = ForgottenAdminsData?.Players ?? new Dictionary<string, string>();
        // update date
        ForgottenAdminsData = data;
        if (!IsOpened()) return;

        if(PlayerListFull.Count != ForgottenAdminsData?.Players?.Count)
            LoadFilteredPlayerList();

        switch (_currentTabIndex)
        {
            case 0:
            {
                UpdatePlayerMenu();
                break;
            }
            case 1:
            {
                if (_updateOnceNeeded)
                {
                    _updateOnceNeeded = false;
                    UpdateCommands();
                }

                break;
            }
            case 2:
            {
                if (_updateOnceNeeded)
                {
                    _updateOnceNeeded = false;
                    LandClaims = ForgottenAdminsData?.LandClaims?.Select(c => $"{c.Description}").ToArray() ?? new[] { string.Empty };
                    SelectedClaim = ForgottenAdminsData?.LandClaims?.FirstOrDefault();
                    _selectedLandClaim = 0;
                    UpdateClaimsMenu();
                }

                break;
            }
            case 3:
            {
                if (_updateOnceNeeded)
                {
                    _updateOnceNeeded = false;
                    SetupDialog();
                }
                break;
            }
        }
    }

    public void UpdateSlot(ItemSlot itemSlot, ForgottenAdminsItemStackData forgottenadminsItemStackData)
    {
        var newStack = ForgottenAdmins.FromPacket(forgottenadminsItemStackData, capi.World);
        var didUpdate = newStack == null != (itemSlot.Itemstack == null) ||
                        (newStack != null && !newStack.Equals(capi.World, itemSlot.Itemstack,
                            GlobalConstants.IgnoredStackAttributes));
        itemSlot.Itemstack = newStack;
        if (didUpdate)
        {
            itemSlot.MarkDirty();
        }
    }
}
