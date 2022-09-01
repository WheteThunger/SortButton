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
    [Info("Sort Button", "MON@H", "2.0.0")]
    [Description("Adds a sort button to storage boxes, allowing you to sort items by name or category")]
    internal class SortButton : CovalencePlugin
    {
        #region Fields

        private Configuration _configData;

        [PluginReference]
        private readonly Plugin Clans, Friends;

        private const string PermissionUse = "sortbutton.use";
        private const string GUIPanelName = "UISortButton";

        private const float BaseYOffset = 112;
        private const float YOffsetPerRow = 62;
        private const float SortButtonWidth = 79;
        private const float SortOrderButtonWidthString = 17;
        private const string ButtonHeightString = "23";

        // When calculating sort button position, do it based on the loot panel to simplify configuration.
        private readonly Dictionary<string, string> OffsetYByLootPanel = new Dictionary<string, string>
        {
            ["dropboxcontents"] = (BaseYOffset + YOffsetPerRow * 2).ToString(),
            ["furnace"] = "277",
            ["generic"] = (BaseYOffset + YOffsetPerRow * 6).ToString(),
            ["genericsmall"] = (BaseYOffset + YOffsetPerRow).ToString(),
            ["largefurnace"] = "395",
            ["toolcupboard"] = "560",
            ["vendingmachine.storage"] = (BaseYOffset + YOffsetPerRow * 5).ToString(),
        };

        private readonly string[] OffsetYByRow = new string[]
        {
            (BaseYOffset + YOffsetPerRow * 1).ToString(),
            (BaseYOffset + YOffsetPerRow * 2).ToString(),
            (BaseYOffset + YOffsetPerRow * 3).ToString(),
            (BaseYOffset + YOffsetPerRow * 4).ToString(),
            (BaseYOffset + YOffsetPerRow * 5).ToString(),
            (BaseYOffset + YOffsetPerRow * 6).ToString(),
            (BaseYOffset + YOffsetPerRow * 7).ToString(),
        };

        // Since 2020/08, some loot panels still use 21px, while most other panels use 23px.
        private readonly Dictionary<string, string> HeightOverrideByLootPanel = new Dictionary<string, string>
        {
            ["dropboxcontents"] = "21",
            ["furnace"] = "21",
            ["largefurnace"] = "21",
            ["toolcupboard"] = "21",
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
                Enabled = _configData.DefaultEnabled,
                SortByCategory = _configData.DefaultSortByCategory,
            };
        }

        private void OnServerInitialized()
        {
            _configData.OnServerInitialized(this);

            SubscribeToHooks();
        }

        private void Unload()
        {
            foreach (BasePlayer activePlayer in BasePlayer.activePlayerList)
            {
                DestroyUi(activePlayer);
            }
        }

        private void OnLootEntity(BasePlayer basePlayer, StorageContainer entity)
        {
            HandleOnLootEntity(basePlayer, entity, delay: true);
        }

        private void OnPlayerLootEnd(PlayerLoot inventory)
        {
            BasePlayer player = inventory.baseEntity;
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

            BasePlayer basePlayer = player.Object as BasePlayer;

            if (!permission.UserHasPermission(basePlayer.UserIDString, PermissionUse))
            {
                PlayerSendMessage(basePlayer, Lang(LangKeys.Error.NoPermission, basePlayer.UserIDString));
                return;
            }

            PlayerData playerData = GetPlayerData(basePlayer.userID, createIfMissing: true);

            if (args == null || args.Length == 0)
            {
                playerData.Enabled = !playerData.Enabled;
                SaveData();

                string enabledOrDisabledMessage = playerData.Enabled
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

                    string sortTypeLangKey = playerData.SortByCategory == true
                        ? LangKeys.Format.Category
                        : LangKeys.Format.Name;

                    PlayerSendMessage(basePlayer, Lang(LangKeys.Info.SortType, basePlayer.UserIDString, Lang(sortTypeLangKey, basePlayer.UserIDString)));
                    return;
            }

            PlayerSendMessage(basePlayer, Lang(LangKeys.Info.Help, basePlayer.UserIDString, _configData.Commands[0]));
        }

        [Command("sortbutton.order")]
        private void Command_SortType(IPlayer player)
        {
            if (player.IsServer || !player.HasPermission(PermissionUse))
                return;

            BasePlayer basePlayer = player.Object as BasePlayer;
            PlayerData playerData = GetPlayerData(basePlayer.userID, createIfMissing: true);

            playerData.SortByCategory = !playerData.SortByCategory;
            SaveData();

            RecreateSortButton(basePlayer);
        }

        [Command("sortbutton.sort")]
        private void Command_Sort(IPlayer player)
        {
            if (player.IsServer || !player.HasPermission(PermissionUse))
                return;

            BasePlayer basePlayer = player.Object as BasePlayer;
            List<ItemContainer> containers = basePlayer.inventory.loot.containers;

            // Sorting loot panels with multiple containers is not supported at this time.
            if (containers.Count != 1)
                return;

            BaseEntity entitySource = basePlayer.inventory.loot.entitySource;

            // Verify the container is supported.
            ContainerConfiguration containerConfiguration = _configData.GetContainerConfiguration(entitySource);
            if (containerConfiguration == null || !containerConfiguration.Enabled)
                return;

            // Verify entity-specific checks like for drop boxes and vending machines.
            if (!CanPlayerSortEntity(basePlayer, entitySource))
                return;

            // Verify the player hasn't disabled the sort button.
            PlayerData playerData = GetPlayerData(basePlayer.userID);
            if (!playerData.Enabled)
                return;

            // If the container is owned by another player, verify the looter is authorized to sort.
            ulong ownerID = entitySource.OwnerID;
            if (_configData.CheckOwnership && ownerID != 0 && !IsAlly(basePlayer.userID, ownerID))
                return;

            foreach (ItemContainer container in basePlayer.inventory.loot.containers)
            {
                if (!CanPlayerSortContainer(basePlayer, container))
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
            List<ItemCategory> itemCategories = Enum.GetValues(typeof(ItemCategory)).Cast<ItemCategory>().ToList();
            itemCategories.Sort((a, b) => a.ToString().CompareTo(b.ToString()));

            _itemCategoryToSortIndex = new int[itemCategories.Count];

            for (int i = 0; i < itemCategories.Count; i++)
            {
                ItemCategory itemCategory = itemCategories[i];
                _itemCategoryToSortIndex[(int)itemCategory] = i;
            }
        }

        private void RegisterPermissions()
        {
            permission.RegisterPermission(PermissionUse, this);
        }

        private void AddCommands()
        {
            if (_configData.Commands.Count == 0)
            {
                _configData.Commands = new List<string>() { "sortbutton" };
                SaveConfig();
            }

            foreach (string command in _configData.Commands)
            {
                AddCovalenceCommand(command, nameof(CmdSortButton));
            }
        }

        private bool IsPluginLoaded(Plugin plugin) => plugin != null && plugin.IsLoaded;

        private bool IsAlly(ulong playerId, ulong targetId)
        {
            if (playerId == targetId || IsOnSameTeam(playerId, targetId))
                return true;

            string playerIdString = playerId.ToString();
            string targetIdString = targetId.ToString();

            return IsClanMemberOrAlly(playerIdString, targetIdString)
                || IsFriend(playerIdString, targetIdString);
        }

        private bool IsClanMemberOrAlly(string playerId, string targetId)
        {
            if (_configData.UseClans)
            {
                if (IsPluginLoaded(Clans))
                {
                    return Clans.Call<bool>("IsMemberOrAlly", playerId, targetId);
                }
                else
                {
                    PrintError("UseClans is set to true, but plugin Clans is not loaded!");
                }
            }

            return false;
        }

        private bool IsFriend(string playerId, string targetId)
        {
            if (_configData.UseFriends)
            {
                if (IsPluginLoaded(Friends))
                {
                    return Friends.Call<bool>("HasFriend", targetId, playerId);
                }
                else
                {
                    PrintError("UseFriends is set to true, but plugin Friends is not loaded!");
                }
            }

            return false;
        }

        private bool IsOnSameTeam(ulong playerId, ulong targetId)
        {
            if (!_configData.UseTeams)
                return false;

            RelationshipManager.PlayerTeam playerTeam = RelationshipManager.ServerInstance.FindPlayersTeam(playerId);
            return playerTeam?.members.Contains(targetId) ?? false;
        }

        private void PlayerSendMessage(BasePlayer player, string message)
        {
            message = Lang(LangKeys.Format.Prefix, player.UserIDString) + message;
            player.SendConsoleCommand("chat.add", 2, _configData.SteamIDIcon, message);
        }

        #endregion Helpers

        #region Core Methods

        private void HandleOnLootEntityDelayed(BasePlayer basePlayer, StorageContainer entity, string offsetXString, bool sortByCategory)
        {
            // Sorting loot panels with multiple containers is not supported at this time.
            if (basePlayer.inventory.loot.containers.Count != 1)
                return;

            ItemContainer container = basePlayer.inventory.loot.containers.FirstOrDefault();

            string lootPanelName = DetermineLootPanelName(entity);
            string offsetYString;
            if (!TryDetermineYOffset(container, lootPanelName, out offsetYString))
                return;

            string heightString;
            if (!HeightOverrideByLootPanel.TryGetValue(lootPanelName, out heightString))
            {
                heightString = ButtonHeightString;
            }

            CreateButtonUI(basePlayer, offsetXString, offsetYString, heightString, sortByCategory);
        }

        private void HandleOnLootEntity(BasePlayer basePlayer, StorageContainer entity, bool delay = true)
        {
            if (basePlayer == null
                || !permission.UserHasPermission(basePlayer.UserIDString, PermissionUse))
                return;

            // Verify the container is supported.
            ContainerConfiguration containerConfiguration = _configData.GetContainerConfiguration(entity);
            if (containerConfiguration == null || !containerConfiguration.Enabled)
                return;

            // Verify entity-specific checks like for drop boxes and vending machines.
            if (!CanPlayerSortEntity(basePlayer, entity))
                return;

            // Verify the player hasn't disabled the sort button.
            PlayerData playerData = GetPlayerData(basePlayer.userID);
            if (!playerData.Enabled)
                return;

            // If the container is owned by another player, verify the looter is authorized to sort.
            ulong ownerID = entity.OwnerID;
            if (_configData.CheckOwnership && ownerID != 0 && !IsAlly(basePlayer.userID, ownerID))
                return;

            string offsetXString = containerConfiguration.OffsetXString;
            bool sortByCategory = playerData.SortByCategory;

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

        private bool CanPlayerSortContainer(BasePlayer player, ItemContainer container)
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

            DropBox dropBox = entity as DropBox;
            if ((object)dropBox != null)
                return dropBox.PlayerBehind(basePlayer);

            VendingMachine vendingMachine = entity as VendingMachine;
            if ((object)vendingMachine != null)
                return vendingMachine.PlayerBehind(basePlayer);

            return true;
        }

        private string DetermineLootPanelName(StorageContainer entity)
        {
            return (entity as Mailbox)?.ownerPanel
                ?? (entity as StorageContainer)?.panelName
                ?? "generic_resizable";
        }

        private bool TryDetermineYOffset(ItemContainer container, string lootPanelName, out string offsetYString)
        {
            if (lootPanelName == "generic_resizable")
            {
                int numRows = Math.Min(1 + (container.capacity - 1) / 6, 7);
                offsetYString = OffsetYByRow[numRows - 1];
                return true;
            }

            if (OffsetYByLootPanel.TryGetValue(lootPanelName, out offsetYString))
                return true;

            offsetYString = null;
            return false;
        }

        private int CompareItems(Item a, Item b, bool byCategory = false)
        {
            if (byCategory)
            {
                int categoryIndex = _itemCategoryToSortIndex[(int)a.info.category];
                int otherCategoryIndex = _itemCategoryToSortIndex[(int)b.info.category];

                int categoryComparison = categoryIndex.CompareTo(otherCategoryIndex);
                if (categoryComparison != 0)
                    return categoryComparison;
            }

            int nameComparison = a.info.displayName.translated.CompareTo(b.info.displayName.translated);
            if (nameComparison != 0)
                return nameComparison;

            return a.amount.CompareTo(b.amount);
        }

        private void SortContainer(ItemContainer container, BasePlayer initiator, bool byCategory)
        {
            List<Item> itemList = Pool.GetList<Item>();

            if (container.entityOwner is BuildingPrivlidge)
            {
                for (int i = container.itemList.Count - 1; i >= 0; i--)
                {
                    Item item = container.itemList[i];
                    if (item.position >= 24)
                        continue;

                    item.RemoveFromContainer();
                    itemList.Add(item);
                }
            }
            else
            {
                for (int i = container.itemList.Count - 1; i >= 0; i--)
                {
                    Item item = container.itemList[i];
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
            if (_cachedUI == null)
            {
                CuiElementContainer elements = new CuiElementContainer();

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
                    }
                }, GUIPanelName);

                _cachedUI = CuiHelper.ToJson(elements);

                // Escape braces for string.Format.
                _cachedUI = _cachedUI.Replace("{", "{{").Replace("}", "}}");

                for (int i = 0; i < _uiArguments.Length; i++)
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

            StorageContainer storage = player.inventory.loot?.entitySource as StorageContainer;
            if ((object)storage != null)
            {
                HandleOnLootEntity(player, storage, delay: false);
            }
        }

        private static void DestroyUi(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, GUIPanelName);
        }

        #endregion GUI

        #region DataFile

        private StoredData _storedData;

        private class StoredData
        {
            public readonly Hash<ulong, PlayerData> PlayerData = new Hash<ulong, PlayerData>();
        }

        private class PlayerData
        {
            public bool Enabled;
            public bool SortByCategory;
        }

        private PlayerData GetPlayerData(ulong userID, bool createIfMissing = false)
        {
            PlayerData playerData = _storedData.PlayerData[userID];
            if (playerData != null)
                return playerData;

            if (createIfMissing)
            {
                playerData = new PlayerData()
                {
                    Enabled = _configData.DefaultEnabled,
                    SortByCategory = _configData.DefaultSortByCategory,
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
            public List<string> Commands = new List<string>()
            {
                "sortbutton"
            };

            [JsonProperty("Containers by short prefab name")]
            private Dictionary<string, ContainerConfiguration> ContainersByShortPrefabName = new Dictionary<string, ContainerConfiguration>
            {
                ["assets/content/vehicles/boats/rhib/subents/rhib_storage.prefab"] = new ContainerConfiguration(),
                ["assets/content/vehicles/boats/rowboat/subents/rowboat_storage.prefab"] = new ContainerConfiguration(),
                ["assets/content/vehicles/modularcar/subents/modular_car_1mod_storage.prefab"] = new ContainerConfiguration(),
                ["assets/content/vehicles/modularcar/subents/modular_car_camper_storage.prefab"] = new ContainerConfiguration(),
                ["assets/content/vehicles/snowmobiles/subents/snowmobileitemstorage.prefab"] = new ContainerConfiguration(),
                ["assets/content/vehicles/submarine/subents/submarineitemstorage.prefab"] = new ContainerConfiguration(),
                ["assets/prefabs/deployable/composter/composter.prefab"] = new ContainerConfiguration(),
                ["assets/prefabs/deployable/dropbox/dropbox.deployed.prefab"] = new ContainerConfiguration(),
                ["assets/prefabs/deployable/fridge/fridge.deployed.prefab"] = new ContainerConfiguration(),
                ["assets/prefabs/deployable/hitch & trough/hitchtrough.deployed.prefab"] = new ContainerConfiguration(),
                ["assets/prefabs/deployable/hot air balloon/subents/hab_storage.prefab"] = new ContainerConfiguration(),
                ["assets/prefabs/deployable/large wood storage/box.wooden.large.prefab"] = new ContainerConfiguration(),
                ["assets/prefabs/deployable/small stash/small_stash_deployed.prefab"] = new ContainerConfiguration(),
                ["assets/prefabs/deployable/tool cupboard/cupboard.tool.deployed.prefab"] = new ContainerConfiguration(),
                ["assets/prefabs/deployable/vendingmachine/vendingmachine.deployed.prefab"] = new ContainerConfiguration(),
                ["assets/prefabs/deployable/woodenbox/woodbox_deployed.prefab"] = new ContainerConfiguration(),
                ["assets/prefabs/misc/halloween/coffin/coffinstorage.prefab"] = new ContainerConfiguration(),
            };

            [JsonProperty("Containers by skin ID")]
            private Dictionary<ulong, ContainerConfiguration> ContainersBySkinId = new Dictionary<ulong, ContainerConfiguration>();

            [JsonIgnore]
            private Dictionary<uint, ContainerConfiguration> ContainersByPrefabId = new Dictionary<uint, ContainerConfiguration>();

            public void OnServerInitialized(SortButton plugin)
            {
                foreach (KeyValuePair<string, ContainerConfiguration> entry in ContainersByShortPrefabName)
                {
                    uint prefabId = StringPool.Get(entry.Key);
                    if (prefabId == 0)
                    {
                        plugin.LogError($"Invalid prefab in configuration: {entry.Key}");
                        continue;
                    }

                    ContainersByPrefabId[prefabId] = entry.Value;
                }
            }

            public ContainerConfiguration GetContainerConfiguration(BaseEntity entity)
            {
                ContainerConfiguration containerConfiguration;
                if (entity.skinID != 0 && ContainersBySkinId.TryGetValue(entity.skinID, out containerConfiguration))
                {
                    return containerConfiguration;
                }

                if (ContainersByPrefabId.TryGetValue(entity.prefabID, out containerConfiguration))
                {
                    return containerConfiguration;
                }

                return null;
            }
        }

        #region Configuration Helpers

        private class BaseConfiguration
        {
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
            Dictionary<string, object> currentWithDefaults = config.ToDictionary();
            Dictionary<string, object> currentRaw = Config.ToDictionary(x => x.Key, x => x.Value);
            return MaybeUpdateConfigSection(currentWithDefaults, currentRaw);
        }

        private bool MaybeUpdateConfigSection(Dictionary<string, object> currentWithDefaults, Dictionary<string, object> currentRaw)
        {
            bool changed = false;

            foreach (string key in currentWithDefaults.Keys)
            {
                object currentRawValue;
                if (currentRaw.TryGetValue(key, out currentRawValue))
                {
                    Dictionary<string, object> defaultDictValue = currentWithDefaults[key] as Dictionary<string, object>;
                    Dictionary<string, object> currentDictValue = currentRawValue as Dictionary<string, object>;

                    if (defaultDictValue != null)
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

        protected override void LoadDefaultConfig() => _configData = new Configuration();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _configData = Config.ReadObject<Configuration>();
                if (_configData == null)
                {
                    throw new JsonException();
                }

                if (MaybeUpdateConfig(_configData))
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
            }
        }

        protected override void SaveConfig()
        {
            Log($"Configuration changes saved to {Name}.json");
            Config.WriteObject(_configData, true);
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