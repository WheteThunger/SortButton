using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core.Plugins;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Game.Rust.Cui;
using Pool = Facepunch.Pool;
using System.Collections.Generic;
using System.Linq;
using System;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Sort Button", "WhiteThunder, MON@H", "2.4.2")]
    [Description("Adds a sort button to storage boxes, allowing you to sort items by name or category")]
    internal class SortButton : CovalencePlugin
    {
        #region Fields

        private Configuration _config;

        [PluginReference]
        private readonly Plugin Clans, Friends;

        private const string PermissionUse = "sortbutton.use";
        private const string GUIPanelName = "UISortButton";

        private const int MaxRows = 8;
        private const float BaseYOffset = 113.5f;
        private const float YOffsetPerRow = 62;
        private const float SortButtonWidth = 79;
        private const float SortOrderButtonWidthString = 17;
        private const string ButtonHeightString = "23";

        // When calculating sort button position, do it based on the loot panel to simplify configuration.
        private readonly Dictionary<string, string> OffsetYByLootPanel = new()
        {
            ["dropboxcontents"] = (BaseYOffset + YOffsetPerRow * 2).ToString(),
            ["furnace"] = "277",
            ["generic"] = (BaseYOffset + YOffsetPerRow * 6).ToString(),
            ["genericsmall"] = (BaseYOffset + YOffsetPerRow).ToString(),
            ["largefurnace"] = "395",
            ["toolcupboard"] = "595",
            ["vendingmachine.storage"] = (BaseYOffset + YOffsetPerRow * 5).ToString(),
        };

        private readonly string[] OffsetYByRow = new string[MaxRows];

        // Since 2020/08, some loot panels still use 21px, while most other panels use 23px.
        private readonly Dictionary<string, string> HeightOverrideByLootPanel = new()
        {
            ["animal-storage"] = "21",
            ["dropboxcontents"] = "21",
            ["furnace"] = "21",
            ["largefurnace"] = "21",
            ["toolcupboard"] = "21.5",
            ["vendingmachine.storage"] = "21",
        };

        // When players sort by category, use numeric sort order for faster comparisons.
        private int[] _itemCategoryToSortIndex;

        // When players do not have data, use this shared object to avoid unnecessary heap allocations.
        private PlayerData _defaultPlayerData;

        // Cache the UI JSON to improve performance.
        private string _cachedUI;

        // Parameterize the cached UI JSON with the arguments stored in this array.
        private readonly string[] _uiArguments = new string[6];

        // Keep track of UI viewers to reduce unnecessary calls to destroy the UI.
        private readonly HashSet<ulong> _uiViewers = new();

        #endregion Fields

        #region OxideHooks

        private void Init()
        {
            UnsubscribeFromHooks();
            RegisterPermissions();
            AddCommands();
            LoadData();
            SetupItemCategories();

            _defaultPlayerData = new PlayerData
            {
                Enabled = _config.DefaultEnabled,
                SortByCategory = _config.DefaultSortByCategory,
            };

            for (var i = 0; i < MaxRows; i++)
            {
                OffsetYByRow[i] = (BaseYOffset + YOffsetPerRow * (i + 1)).ToString();
            }
        }

        private void OnServerInitialized()
        {
            _config.OnServerInitialized(this);

            SubscribeToHooks();
        }

        private void Unload()
        {
            foreach (var activePlayer in BasePlayer.activePlayerList)
            {
                DestroyUi(activePlayer);
            }
        }

        private void OnLootEntity(BasePlayer basePlayer, BaseEntity entity)
        {
            HandleOnLootEntity(basePlayer, entity, delay: true);
        }

        private void OnPlayerLootEnd(PlayerLoot inventory)
        {
            var player = inventory.baseEntity;
            if (player != null)
            {
                DestroyUi(player);
            }
        }

        // Only using this hook because some plugins don't call OnPlayerLootEnd.
        private void OnLootEntityEnd(BasePlayer player, BaseCombatEntity entity)
        {
            DestroyUi(player);
        }

        #endregion OxideHooks

        #region Commands

        private void CmdSortButton(IPlayer player, string cmd, string[] args)
        {
            if (player.IsServer)
                return;

            var basePlayer = player.Object as BasePlayer;

            if (!permission.UserHasPermission(basePlayer.UserIDString, PermissionUse))
            {
                PlayerSendMessage(basePlayer, Lang(LangKeys.Error.NoPermission, basePlayer.UserIDString));
                return;
            }

            var playerData = GetPlayerData(basePlayer.userID, createIfMissing: true);

            if (args == null || args.Length == 0)
            {
                playerData.Enabled = !playerData.Enabled;
                SaveData();

                var enabledOrDisabledMessage = playerData.Enabled
                    ? Lang(LangKeys.Format.Enabled, basePlayer.UserIDString)
                    : Lang(LangKeys.Format.Disabled, basePlayer.UserIDString);

                PlayerSendMessage(basePlayer, Lang(LangKeys.Info.ButtonStatus, basePlayer.UserIDString, enabledOrDisabledMessage));
                return;
            }

            switch (args[0].ToLower())
            {
                case "sort":
                case "type":
                    playerData.SortByCategory = !playerData.SortByCategory;
                    SaveData();

                    var sortTypeLangKey = playerData.SortByCategory
                        ? LangKeys.Format.Category
                        : LangKeys.Format.Name;

                    PlayerSendMessage(basePlayer, Lang(LangKeys.Info.SortType, basePlayer.UserIDString, Lang(sortTypeLangKey, basePlayer.UserIDString)));
                    return;
            }

            PlayerSendMessage(basePlayer, Lang(LangKeys.Info.Help, basePlayer.UserIDString, _config.Commands[0]));
        }

        [Command("sortbutton.order")]
        private void Command_SortType(IPlayer player)
        {
            if (player.IsServer || !player.HasPermission(PermissionUse))
                return;

            var basePlayer = player.Object as BasePlayer;
            var playerData = GetPlayerData(basePlayer.userID, createIfMissing: true);

            playerData.SortByCategory = !playerData.SortByCategory;
            SaveData();

            RecreateSortButton(basePlayer);
        }

        [Command("sortbutton.sort")]
        private void Command_Sort(IPlayer player)
        {
            if (player.IsServer || !player.HasPermission(PermissionUse))
                return;

            var basePlayer = player.Object as BasePlayer;
            var containers = basePlayer.inventory.loot.containers;

            // Sorting loot panels with multiple containers is not supported at this time.
            if (containers.Count != 1)
                return;

            var entitySource = basePlayer.inventory.loot.entitySource;

            // Verify the container is supported.
            var containerConfiguration = _config.GetContainerConfiguration(entitySource);
            if (containerConfiguration is not { Enabled: true })
                return;

            // Verify entity-specific checks like for drop boxes and vending machines.
            if (!CanPlayerSortEntity(basePlayer, entitySource))
                return;

            // Verify the player hasn't disabled the sort button.
            var playerData = GetPlayerData(basePlayer.userID);
            if (!playerData.Enabled)
                return;

            // If the container is owned by another player, verify the looter is authorized to sort.
            var ownerID = entitySource.OwnerID;
            if (_config.CheckOwnership && ownerID != 0 && !IsAlly(basePlayer.userID, ownerID))
                return;

            foreach (var container in basePlayer.inventory.loot.containers)
            {
                if (!IsSortableContainer(container))
                    continue;

                SortContainer(container, basePlayer, playerData.SortByCategory);
            }
        }

        #endregion Commands

        #region Helpers

        private void UnsubscribeFromHooks()
        {
            Unsubscribe(nameof(OnLootEntity));
            Unsubscribe(nameof(OnPlayerLootEnd));
        }

        private void SubscribeToHooks()
        {
            Subscribe(nameof(OnLootEntity));
            Subscribe(nameof(OnPlayerLootEnd));
        }

        private void SetupItemCategories()
        {
            var itemCategories = Enum.GetValues(typeof(ItemCategory)).Cast<ItemCategory>().ToList();
            itemCategories.Sort((a, b) => a.ToString().CompareTo(b.ToString()));

            _itemCategoryToSortIndex = new int[itemCategories.Count];

            for (var i = 0; i < itemCategories.Count; i++)
            {
                var itemCategory = itemCategories[i];
                _itemCategoryToSortIndex[(int)itemCategory] = i;
            }
        }

        private void RegisterPermissions()
        {
            permission.RegisterPermission(PermissionUse, this);
        }

        private void AddCommands()
        {
            if (_config.Commands.Count == 0)
            {
                _config.Commands = new List<string>() { "sortbutton" };
                SaveConfig();
            }

            foreach (var command in _config.Commands)
            {
                AddCovalenceCommand(command, nameof(CmdSortButton));
            }
        }

        private bool IsPluginLoaded(Plugin plugin) => plugin is { IsLoaded: true };

        private bool IsAlly(ulong playerId, ulong targetId)
        {
            if (playerId == targetId || IsOnSameTeam(playerId, targetId))
                return true;

            var playerIdString = playerId.ToString();
            var targetIdString = targetId.ToString();

            return IsClanMemberOrAlly(playerIdString, targetIdString)
                || IsFriend(playerIdString, targetIdString);
        }

        private bool IsClanMemberOrAlly(string playerId, string targetId)
        {
            if (_config.UseClans)
            {
                if (IsPluginLoaded(Clans))
                    return Clans.Call<bool>("IsMemberOrAlly", playerId, targetId);

                PrintError("UseClans is set to true, but plugin Clans is not loaded!");
            }

            return false;
        }

        private bool IsFriend(string playerId, string targetId)
        {
            if (_config.UseFriends)
            {
                if (IsPluginLoaded(Friends))
                    return Friends.Call<bool>("HasFriend", targetId, playerId);

                PrintError("UseFriends is set to true, but plugin Friends is not loaded!");
            }

            return false;
        }

        private bool IsOnSameTeam(ulong playerId, ulong targetId)
        {
            if (!_config.UseTeams)
                return false;

            var playerTeam = RelationshipManager.ServerInstance.FindPlayersTeam(playerId);
            return playerTeam?.members.Contains(targetId) ?? false;
        }

        private void PlayerSendMessage(BasePlayer player, string message)
        {
            message = Lang(LangKeys.Format.Prefix, player.UserIDString) + message;
            player.SendConsoleCommand("chat.add", 2, _config.SteamIDIcon, message);
        }

        #endregion Helpers

        #region Core Methods

        private void HandleOnLootEntityDelayed(BasePlayer basePlayer, BaseEntity entity, string offsetXString, bool sortByCategory)
        {
            // Sorting loot panels with multiple containers is not supported at this time.
            if (basePlayer.inventory.loot.containers.Count != 1)
                return;

            var container = basePlayer.inventory.loot.containers.FirstOrDefault();

            // Don't show the sort button for the ridable horse equipment inventory.
            if ((entity is RidableHorse horse && container != horse.storageInventory))
                return;

            var lootPanelName = DetermineLootPanelName(entity);
            if (!TryDetermineYOffset(container, lootPanelName, out var offsetYString))
                return;

            if (!HeightOverrideByLootPanel.TryGetValue(lootPanelName, out var heightString))
            {
                heightString = ButtonHeightString;
            }

            CreateButtonUI(basePlayer, offsetXString, offsetYString, heightString, sortByCategory);
        }

        private void HandleOnLootEntity(BasePlayer basePlayer, BaseEntity entity, bool delay = true)
        {
            if (basePlayer == null
                || !permission.UserHasPermission(basePlayer.UserIDString, PermissionUse))
                return;

            // Verify the container is supported.
            var containerConfiguration = _config.GetContainerConfiguration(entity);
            if (containerConfiguration == null || !containerConfiguration.Enabled)
                return;

            // Verify entity-specific checks like for drop boxes and vending machines.
            if (!CanPlayerSortEntity(basePlayer, entity))
                return;

            // Verify the player hasn't disabled the sort button.
            var playerData = GetPlayerData(basePlayer.userID);
            if (!playerData.Enabled)
                return;

            // If the container is owned by another player, verify the looter is authorized to sort.
            var ownerID = entity.OwnerID;
            if (_config.CheckOwnership && ownerID != 0 && !IsAlly(basePlayer.userID, ownerID))
                return;

            var offsetXString = containerConfiguration.OffsetXString;
            var sortByCategory = playerData.SortByCategory;

            if (delay)
            {
                // Delay showing the sort button, so that we can determine which containers the player is actually viewing.
                NextTick(() =>
                {
                    if (basePlayer == null
                        || basePlayer.IsDestroyed
                        || entity == null
                        || entity.IsDestroyed)
                        return;

                    HandleOnLootEntityDelayed(basePlayer, entity, offsetXString, sortByCategory);
                });
            }
            else
            {
                HandleOnLootEntityDelayed(basePlayer, entity, offsetXString, sortByCategory);
            }
        }

        private bool IsSortableContainer(ItemContainer container)
        {
            if (container.IsLocked()
                || container.PlayerItemInputBlocked()
                || container.HasFlag(ItemContainer.Flag.IsPlayer)
                || container.capacity <= 1)
                return false;

            return true;
        }

        private bool CanPlayerSortEntity(BasePlayer basePlayer, BaseEntity entity)
        {
            if (entity == null || entity.IsDestroyed)
                return false;

            var dropBox = entity as DropBox;
            if ((object)dropBox != null)
                return dropBox.PlayerBehind(basePlayer);

            var vendingMachine = entity as VendingMachine;
            if ((object)vendingMachine != null)
                return vendingMachine.PlayerBehind(basePlayer);

            return true;
        }

        private string DetermineLootPanelName(BaseEntity entity)
        {
            return entity switch
            {
                Mailbox mailbox => mailbox.ownerPanel,
                StorageContainer storageContainer => storageContainer.panelName,
                RidableHorse horse => horse.storagePanelName,
                _ => "generic_resizable",
            };
        }

        private bool TryDetermineYOffset(ItemContainer container, string lootPanelName, out string offsetYString)
        {
            if (lootPanelName == "generic_resizable" || lootPanelName == "animal-storage")
            {
                var numRows = Math.Min(1 + (container.capacity - 1) / 6, MaxRows);
                offsetYString = OffsetYByRow[numRows - 1];
                return true;
            }

            if (OffsetYByLootPanel.TryGetValue(lootPanelName, out offsetYString))
                return true;

            return false;
        }

        private int CompareItems(Item a, Item b, bool byCategory = false)
        {
            if (byCategory)
            {
                var categoryIndex = _itemCategoryToSortIndex[(int)a.info.category];
                var otherCategoryIndex = _itemCategoryToSortIndex[(int)b.info.category];

                var categoryComparison = categoryIndex.CompareTo(otherCategoryIndex);
                if (categoryComparison != 0)
                    return categoryComparison;
            }

            var nameComparison = a.info.displayName.translated.CompareTo(b.info.displayName.translated);
            if (nameComparison != 0)
                return nameComparison;

            return a.amount.CompareTo(b.amount);
        }

        private void SortContainer(ItemContainer container, BasePlayer initiator, bool byCategory)
        {
            var itemList = Pool.GetList<Item>();

            if (container.entityOwner is BuildingPrivlidge)
            {
                for (var i = container.itemList.Count - 1; i >= 0; i--)
                {
                    var item = container.itemList[i];
                    if (item.position >= 24)
                        continue;

                    item.RemoveFromContainer();
                    itemList.Add(item);
                }
            }
            else
            {
                for (var i = container.itemList.Count - 1; i >= 0; i--)
                {
                    var item = container.itemList[i];
                    item.RemoveFromContainer();
                    itemList.Add(item);
                }
            }

            if (byCategory)
            {
                itemList.Sort((a, b) => CompareItems(a, b, byCategory: true));
            }
            else
            {
                itemList.Sort((a, b) => CompareItems(a, b, byCategory: false));
            }

            foreach (Item item in itemList)
            {
                if (!item.MoveToContainer(container))
                {
                    initiator.GiveItem(item);
                }
            }

            Pool.FreeList(ref itemList);
        }

        #endregion Core Methods

        #region GUI

        private void CreateButtonUI(BasePlayer player, string offsetXString, string offsetYString, string heightString, bool sortByCategory)
        {
            if (!_uiViewers.Add(player.userID))
                return;

            if (_cachedUI == null)
            {
                var elements = new CuiElementContainer();

                elements.Add(new CuiPanel
                {
                    Image =
                    {
                        Color = "0 0 0 0"
                    },
                    RectTransform =
                    {
                        AnchorMin = "0.5 0",
                        AnchorMax = "0.5 0",
                        OffsetMin = "{0} {1}",
                        OffsetMax = "{0} {1}",
                    },
                    CursorEnabled = false,
                }, "Overlay", GUIPanelName);

                elements.Add(new CuiButton
                {
                    Button =
                    {
                        Command = "sortbutton.order",
                        Color = "{2}",
                    },
                    RectTransform =
                    {
                        AnchorMin = "0 0",
                        AnchorMax = "0 0",
                        OffsetMin = "0 0",
                        OffsetMax = $"{SortOrderButtonWidthString} {{3}}",
                    },
                    Text =
                    {
                        Text = "{4}",
                        FontSize = 12,
                        Align = TextAnchor.MiddleCenter,
                        Color = "0.77 0.92 0.67 0.8",
                    },
                }, GUIPanelName);

                elements.Add(new CuiButton
                {
                    Button =
                    {
                        Command = "sortbutton.sort",
                        Color = "0.41 0.50 0.25 0.8",
                    },
                    RectTransform =
                    {
                        AnchorMin = "0 0",
                        AnchorMax = "0 0",
                        OffsetMin = $"{SortOrderButtonWidthString} 0",
                        OffsetMax = $"{SortOrderButtonWidthString + SortButtonWidth} {{3}}",
                    },
                    Text =
                    {
                        Text = "{5}",
                        FontSize = 12,
                        Align = TextAnchor.MiddleCenter,
                        Color = "0.77 0.92 0.67 0.8",
                    },
                }, GUIPanelName);

                _cachedUI = CuiHelper.ToJson(elements);

                // Escape braces for string.Format.
                _cachedUI = _cachedUI.Replace("{", "{{").Replace("}", "}}");

                for (var i = 0; i < _uiArguments.Length; i++)
                {
                    // Unescape braces for intended placeholders.
                    _cachedUI = _cachedUI.Replace("{{" + i + "}}", "{" + i + "}");
                }
            }

            _uiArguments[0] = offsetXString;
            _uiArguments[1] = offsetYString;

            // Order button color.
            _uiArguments[2] = sortByCategory ? "0.75 0.43 0.18 0.8" : "0.26 0.58 0.80 0.8";

            // Button height.
            _uiArguments[3] = heightString;

            // Order button text.
            _uiArguments[4] = sortByCategory ? "C" : "N";

            // Sort button text.
            _uiArguments[5] = lang.GetMessage(LangKeys.Format.ButtonText, this, player.UserIDString);

            CuiHelper.AddUi(player, string.Format(_cachedUI, _uiArguments));
        }

        private void RecreateSortButton(BasePlayer player)
        {
            DestroyUi(player);

            var storage = player.inventory.loot?.entitySource as StorageContainer;
            if ((object)storage != null)
            {
                HandleOnLootEntity(player, storage, delay: false);
            }
        }

        private void DestroyUi(BasePlayer player)
        {
            if (!_uiViewers.Remove(player.userID))
                return;

            CuiHelper.DestroyUi(player, GUIPanelName);
        }

        #endregion GUI

        #region DataFile

        private StoredData _storedData;

        private class StoredData
        {
            public readonly Hash<ulong, PlayerData> PlayerData = new();
        }

        private class PlayerData
        {
            public bool Enabled;
            public bool SortByCategory;
        }

        private PlayerData GetPlayerData(ulong userID, bool createIfMissing = false)
        {
            var playerData = _storedData.PlayerData[userID];
            if (playerData != null)
                return playerData;

            if (createIfMissing)
            {
                playerData = new PlayerData()
                {
                    Enabled = _config.DefaultEnabled,
                    SortByCategory = _config.DefaultSortByCategory,
                };

                _storedData.PlayerData[userID] = playerData;

                return playerData;
            }

            return _defaultPlayerData;
        }

        private void LoadData()
        {
            try
            {
                _storedData = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(Name);
            }
            catch
            {
                ClearData();
            }
        }

        private void SaveData() => Interface.Oxide.DataFileSystem.WriteObject(Name, _storedData);

        private void ClearData()
        {
            _storedData = new StoredData();
            SaveData();
        }

        #endregion DataFile

        #region Configuration

        private class ContainerConfiguration
        {
            [JsonProperty("Enabled")]
            public bool Enabled = true;

            [JsonProperty("OffsetX")]
            public float OffsetX = 476.5f;

            [JsonIgnore]
            private string _offsetXString;

            [JsonIgnore]
            public string OffsetXString
            {
                get
                {
                    if (_offsetXString == null)
                    {
                        _offsetXString = OffsetX.ToString();
                    }

                    return _offsetXString;
                }
            }
        }

        private class Configuration : BaseConfiguration
        {
            private static HashSet<string> OldRemovedPrefabs = new()
            {
                "assets/rust.ai/nextai/testridablehorse.prefab",
                "assets/content/vehicles/horse/ridablehorse2.prefab",
                "assets/content/vehicles/horse/_old/testridablehorse.prefab",
            };

            [JsonProperty("Default enabled")]
            public bool DefaultEnabled = true;

            [JsonProperty("Default sort by category")]
            public bool DefaultSortByCategory = true;

            [JsonProperty("Check ownership")]
            public bool CheckOwnership = true;

            [JsonProperty("Use Clans")]
            public bool UseClans = true;

            [JsonProperty("Use Friends")]
            public bool UseFriends = true;

            [JsonProperty("Use Teams")]
            public bool UseTeams = true;

            [JsonProperty("Chat steamID icon")]
            public ulong SteamIDIcon = 0;

            [JsonProperty("Chat command", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> Commands = new()
            {
                "sortbutton"
            };

            [JsonProperty("Containers by short prefab name")]
            private Dictionary<string, ContainerConfiguration> ContainersByPrefabPath = new Dictionary<string, ContainerConfiguration>
            {
                ["assets/content/vehicles/boats/rhib/subents/rhib_storage.prefab"] = new(),
                ["assets/content/vehicles/boats/rowboat/subents/rowboat_storage.prefab"] = new(),
                ["assets/content/vehicles/modularcar/subents/modular_car_1mod_storage.prefab"] = new(),
                ["assets/content/vehicles/modularcar/subents/modular_car_camper_storage.prefab"] = new(),
                ["assets/content/vehicles/snowmobiles/subents/snowmobileitemstorage.prefab"] = new(),
                ["assets/content/vehicles/submarine/subents/submarineitemstorage.prefab"] = new(),
                ["assets/prefabs/deployable/composter/composter.prefab"] = new(),
                ["assets/prefabs/deployable/dropbox/dropbox.deployed.prefab"] = new(),
                ["assets/prefabs/deployable/fridge/fridge.deployed.prefab"] = new(),
                ["assets/prefabs/deployable/hitch & trough/hitchtrough.deployed.prefab"] = new(),
                ["assets/prefabs/deployable/hot air balloon/subents/hab_storage.prefab"] = new(),
                ["assets/prefabs/deployable/large wood storage/box.wooden.large.prefab"] = new(),
                ["assets/prefabs/deployable/large wood storage/skins/medieval_large_wood_box/medieval.box.wooden.large.prefab"] = new(),
                ["assets/prefabs/deployable/small stash/small_stash_deployed.prefab"] = new(),
                ["assets/prefabs/deployable/tool cupboard/cupboard.tool.deployed.prefab"] = new(),
                ["assets/prefabs/deployable/tool cupboard/retro/cupboard.tool.retro.deployed.prefab"] = new(),
                ["assets/prefabs/deployable/tool cupboard/shockbyte/cupboard.tool.shockbyte.deployed.prefab"] = new(),
                ["assets/prefabs/deployable/vendingmachine/vendingmachine.deployed.prefab"] = new(),
                ["assets/prefabs/deployable/woodenbox/woodbox_deployed.prefab"] = new(),
                ["assets/prefabs/misc/halloween/coffin/coffinstorage.prefab"] = new(),
                ["assets/prefabs/misc/decor_dlc/storagebarrel/storage_barrel_b.prefab"] = new(),
                ["assets/prefabs/misc/decor_dlc/storagebarrel/storage_barrel_c.prefab"] = new(),
                ["assets/prefabs/deployable/large wood storage/skins/jungle_dlc_large_wood_box/jungle_dlc_storage_horizontal/wicker_barrel.prefab"] = new(),
                ["assets/prefabs/deployable/large wood storage/skins/jungle_dlc_large_wood_box/jungle_dlc_storage_vertical/bamboo_barrel.prefab"] = new(),
                ["assets/prefabs/deployable/large wood storage/skins/abyss_dlc_large_wood_box/abyss_dlc_storage_vertical/abyss_barrel_vertical.prefab"] = new(),
                ["assets/prefabs/deployable/large wood storage/skins/abyss_dlc_large_wood_box/abyss_dlc_storage_horizontal/abyss_barrel_horizontal.prefab"] = new(),
                ["assets/content/vehicles/horse/ridablehorse.prefab"] = new(),
            };

            [JsonProperty("Containers by skin ID")]
            private Dictionary<ulong, ContainerConfiguration> ContainersBySkinId = new();

            [JsonIgnore]
            private Dictionary<uint, ContainerConfiguration> ContainersByPrefabId = new();

            public void OnServerInitialized(SortButton plugin)
            {
                List<string> prefabsToRemove = null;

                foreach (var (prefabPath, containerConfig) in ContainersByPrefabPath)
                {
                    var baseEntity = GameManager.server.FindPrefab(prefabPath)?.GetComponent<BaseEntity>();
                    if (baseEntity == null)
                    {
                        if (OldRemovedPrefabs.Contains(prefabPath))
                        {
                            prefabsToRemove ??= new List<string>();
                            prefabsToRemove.Add(prefabPath);
                        }
                        else
                        {
                            plugin.LogError($"Invalid prefab in configuration: {prefabPath}");
                        }

                        continue;
                    }

                    ContainersByPrefabId[baseEntity.prefabID] = containerConfig;
                }

                if (prefabsToRemove?.Count > 0)
                {
                    foreach (var prefabPath in prefabsToRemove)
                    {
                        ContainersByPrefabPath.Remove(prefabPath);
                    }

                    if (!UsingDefaults)
                    {
                        plugin.SaveConfig();
                    }
                }
            }

            public ContainerConfiguration GetContainerConfiguration(BaseEntity entity)
            {
                if (entity.skinID != 0 && ContainersBySkinId.TryGetValue(entity.skinID, out var containerConfiguration))
                    return containerConfiguration;

                if (ContainersByPrefabId.TryGetValue(entity.prefabID, out containerConfiguration))
                    return containerConfiguration;

                return null;
            }
        }

        #region Configuration Helpers

        private class BaseConfiguration
        {
            [JsonIgnore]
            public bool UsingDefaults;

            public string ToJson() => JsonConvert.SerializeObject(this);

            public Dictionary<string, object> ToDictionary() => JsonHelper.Deserialize(ToJson()) as Dictionary<string, object>;
        }

        private static class JsonHelper
        {
            public static object Deserialize(string json) => ToObject(JToken.Parse(json));

            private static object ToObject(JToken token)
            {
                switch (token.Type)
                {
                    case JTokenType.Object:
                        return token.Children<JProperty>()
                                    .ToDictionary(prop => prop.Name,
                                                  prop => ToObject(prop.Value));

                    case JTokenType.Array:
                        return token.Select(ToObject).ToList();

                    default:
                        return ((JValue)token).Value;
                }
            }
        }

        private bool MaybeUpdateConfig(BaseConfiguration config)
        {
            var currentWithDefaults = config.ToDictionary();
            var currentRaw = Config.ToDictionary(x => x.Key, x => x.Value);
            return MaybeUpdateConfigSection(currentWithDefaults, currentRaw);
        }

        private bool MaybeUpdateConfigSection(Dictionary<string, object> currentWithDefaults, Dictionary<string, object> currentRaw)
        {
            var changed = false;

            foreach (var key in currentWithDefaults.Keys)
            {
                if (currentRaw.TryGetValue(key, out var currentRawValue))
                {
                    var currentDictValue = currentRawValue as Dictionary<string, object>;

                    if (currentWithDefaults[key] is Dictionary<string, object> defaultDictValue)
                    {
                        if (currentDictValue == null)
                        {
                            currentRaw[key] = currentWithDefaults[key];
                            changed = true;
                        }
                        else if (MaybeUpdateConfigSection(defaultDictValue, currentDictValue))
                        {
                            changed = true;
                        }
                    }
                }
                else
                {
                    currentRaw[key] = currentWithDefaults[key];
                    changed = true;
                }
            }

            return changed;
        }

        protected override void LoadDefaultConfig() => _config = new Configuration();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<Configuration>();
                if (_config == null)
                {
                    throw new JsonException();
                }

                if (MaybeUpdateConfig(_config))
                {
                    LogWarning("Configuration appears to be outdated; updating and saving");
                    SaveConfig();
                }
            }
            catch (Exception e)
            {
                LogError(e.Message);
                LogWarning($"Configuration file {Name}.json is invalid; using defaults");
                LoadDefaultConfig();
                _config.UsingDefaults = true;
            }
        }

        protected override void SaveConfig()
        {
            Log($"Configuration changes saved to {Name}.json");
            Config.WriteObject(_config, true);
        }

        #endregion Configuration Helpers

        #endregion Configuration

        #region Localization

        private string Lang(string key, string userIDString = null, params object[] args)
        {
            try
            {
                return string.Format(lang.GetMessage(key, this, userIDString), args);
            }
            catch (Exception ex)
            {
                PrintError($"Lang Key '{key}' threw exception:\n{ex}");
                throw;
            }
        }

        private static class LangKeys
        {
            public static class Error
            {
                private const string Base = nameof(Error) + ".";
                public const string NoPermission = Base + nameof(NoPermission);
            }

            public static class Info
            {
                private const string Base = nameof(Info) + ".";
                public const string ButtonStatus = Base + nameof(ButtonStatus);
                public const string Help = Base + nameof(Help);
                public const string SortType = Base + nameof(SortType);
            }

            public static class Format
            {
                private const string Base = nameof(Format) + ".";
                public const string ButtonText = Base + nameof(ButtonText);
                public const string Category = Base + nameof(Category);
                public const string Disabled = Base + nameof(Disabled);
                public const string Enabled = Base + nameof(Enabled);
                public const string Name = Base + nameof(Name);
                public const string Prefix = Base + nameof(Prefix);
            }
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                [LangKeys.Error.NoPermission] = "You do not have permission to use this command",
                [LangKeys.Format.ButtonText] = "Sort",
                [LangKeys.Format.Category] = "<color=#D2691E>Category</color>",
                [LangKeys.Format.Disabled] = "<color=#B22222>Disabled</color>",
                [LangKeys.Format.Enabled] = "<color=#228B22>Enabled</color>",
                [LangKeys.Format.Name] = "<color=#00BFFF>Name</color>",
                [LangKeys.Format.Prefix] = "<color=#00FF00>[Sort Button]</color>: ",
                [LangKeys.Info.ButtonStatus] = "Sort Button is now {0}",
                [LangKeys.Info.SortType] = "Sort Type is now {0}",
                [LangKeys.Info.Help] = "List Commands:\n" +
                "<color=#FFFF00>/{0}</color> - Enable/Disable Sort Button.\n" +
                "<color=#FFFF00>/{0} <sort | type></color> - change sort type.",
            }, this);
            lang.RegisterMessages(new Dictionary<string, string>
            {
                [LangKeys.Error.NoPermission] = "У вас нет разрешения на использование этой команды",
                [LangKeys.Format.ButtonText] = "Сортировать",
                [LangKeys.Format.Category] = "<color=#D2691E>Категория</color>",
                [LangKeys.Format.Disabled] = "<color=#B22222>Отключена</color>",
                [LangKeys.Format.Enabled] = "<color=#228B22>Включена</color>",
                [LangKeys.Format.Name] = "<color=#00BFFF>Имя</color>",
                [LangKeys.Format.Prefix] = "<color=#00FF00>[Sort Button]</color>: ",
                [LangKeys.Info.ButtonStatus] = "Кнопка сортировки теперь {0}",
                [LangKeys.Info.SortType] = "Тип сортировки теперь {0}",
                [LangKeys.Info.Help] = "Список команд:\n" +
                "<color=#FFFF00>/{0}</color> - Включить/Отключить кнопку сортировки.\n" +
                "<color=#FFFF00>/{0} <sort | type></color> - изменить тип сортировки.",
            }, this, "ru");
        }

        #endregion Localization
    }
}
