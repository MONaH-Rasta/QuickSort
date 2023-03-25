using System;
using System.Collections.Generic;

using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Game.Rust.Cui;
using UnityEngine;

using Pool = Facepunch.Pool;

/*
7. Use offsets instead of anchors for all the child UI elements (my preference).
8. Is it necessary to use OnEntityDeath / OnEntityKill / OnPlayerSleep?
Nowadays I think the loot hooks are sufficiently comprehensive.
Do need OnPlayerLootEnd and possibly OnLootEntityEnd (for edge cases).
*/

namespace Oxide.Plugins
{
    [Info("Quick Sort", "MON@H", "1.8.1")]
    [Description("Adds a GUI that allows players to quickly sort items into containers")]
    public class QuickSort : RustPlugin
    {
        #region Variables

        private const string GUIPanelName = "QuickSortUI";
        private const string PermissionAutoLootAll = "quicksort.autolootall";
        private const string PermissionLootAll = "quicksort.lootall";
        private const string PermissionUse = "quicksort.use";

        private readonly Hash<int, string> _cacheUiJson = new Hash<int, string>();
        private readonly Hash<string, int> _cacheLanguageIDs = new Hash<string, int>();
        private readonly HashSet<uint> _cacheContainersExcluded = new HashSet<uint>();
        // Keep track of UI viewers to reduce unnecessary calls to destroy the UI.
        private readonly HashSet<ulong> _uiViewers = new HashSet<ulong>();

        private object[] _noteInv = new object[2];
        // When players do not have data, use this shared object to avoid unnecessary heap allocations.
        private PlayerData _defaultPlayerData;

        #endregion Variables

        #region Initialization

        private void Init()
        {
            UnsubscribeHooks();
            RegisterPermissions();
            AddCommands();
        }

        private void OnServerInitialized()
        {
            CreateCache();
            LoadData();
            SubscribeHooks();
        }

        private void Unload()
        {
            foreach (BasePlayer activePlayer in BasePlayer.activePlayerList)
            {
                UiDestroy(activePlayer);
            }
        }

        #endregion Initialization

        #region Configuration

        private ConfigData _configData;

        private class ConfigData
        {
            [JsonProperty(PropertyName = "Global settings")]
            public GlobalConfiguration GlobalSettings = new GlobalConfiguration();

            [JsonProperty(PropertyName = "Custom UI Settings")]
            public UiConfiguration CustomUISettings = new UiConfiguration();

            public class GlobalConfiguration
            {
                [JsonProperty(PropertyName = "Default enabled")]
                public bool DefaultEnabled = true;

                [JsonProperty(PropertyName = "Default UI style (center, lite, right, custom)")]
                public string DefaultUiStyle = "right";

                [JsonProperty(PropertyName = "Loot all delay in seconds (0 to disable)")]
                public int LootAllDelay = 0;

                [JsonProperty(PropertyName = "Enable loot all on the sleepers")]
                public bool LootSleepers = false;

                [JsonProperty(PropertyName = "Auto loot all enabled by default")]
                public bool AutoLootAll = false;

                [JsonProperty(PropertyName = "Default enabled container types")]
                public PlayerContainers Containers = new PlayerContainers();

                [JsonProperty(PropertyName = "Chat steamID icon")]
                public ulong SteamIDIcon = 0;

                [JsonProperty(PropertyName = "Chat command")]
                public string[] Commands = new[] { "qs", "quicksort" };

                [JsonProperty(PropertyName = "Excluded containers", ObjectCreationHandling = ObjectCreationHandling.Replace)]
                public List<string> ContainersExcluded = new List<string>()
                {
                    "assets/prefabs/deployable/single shot trap/guntrap.deployed.prefab",
                    "assets/prefabs/npc/autoturret/autoturret_deployed.prefab",
                    "assets/prefabs/npc/flame turret/flameturret.deployed.prefab",
                    "assets/prefabs/npc/sam_site_turret/sam_site_turret_deployed.prefab",
                    "assets/prefabs/npc/sam_site_turret/sam_static.prefab",
                };
            }

            public class UiConfiguration
            {
                public string AnchorsMin = "0.5 1.0";
                public string AnchorsMax = "0.5 1.0";
                public string OffsetsMin = "192 -137";
                public string OffsetsMax = "573 0";
                public string Color = "0.5 0.5 0.5 0.33";
                public string ButtonsColor = "0.75 0.43 0.18 0.8";
                public string LootAllColor = "0.41 0.50 0.25 0.8";
                public string TextColor = "0.77 0.92 0.67 0.8";
                public int TextSize = 16;
                public int CategoriesTextSize = 14;
            }
        }

        public class PlayerContainers
        {
            public bool Belt = false;
            public bool Main = true;
            public bool Wear = false;
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _configData = Config.ReadObject<ConfigData>();
                if (_configData == null)
                {
                    LoadDefaultConfig();
                    SaveConfig();
                }
            }
            catch
            {
                PrintError("The configuration file is corrupted");
                LoadDefaultConfig();
                SaveConfig();
            }
        }

        protected override void LoadDefaultConfig()
        {
            PrintWarning("Creating a new configuration file");
            _configData = new ConfigData();
        }

        protected override void SaveConfig() => Config.WriteObject(_configData);

        #endregion Configuration

        #region DataFile

        private StoredData _storedData;

        private class StoredData
        {
            public readonly Hash<ulong, PlayerData> PlayerData = new Hash<ulong, PlayerData>();
        }

        public class PlayerData
        {
            public bool Enabled;
            public bool AutoLootAll;
            public string UiStyle;
            public PlayerContainers Containers;
        }

        public void LoadData()
        {
            _storedData = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(Name);

            List<ulong> toRemove = Pool.GetList<ulong>();
            foreach (KeyValuePair<ulong, PlayerData> playerData in _storedData.PlayerData)
            {
                if (!playerData.Value.AutoLootAll.Equals(_defaultPlayerData.AutoLootAll))
                {
                    continue;
                }
                if (!playerData.Value.Enabled.Equals(_defaultPlayerData.Enabled))
                {
                    continue;
                }
                if (!playerData.Value.UiStyle.Equals(_defaultPlayerData.UiStyle))
                {
                    continue;
                }
                if (!playerData.Value.Containers.Belt.Equals(_defaultPlayerData.Containers.Belt))
                {
                    continue;
                }
                if (!playerData.Value.Containers.Main.Equals(_defaultPlayerData.Containers.Main))
                {
                    continue;
                }
                if (!playerData.Value.Containers.Wear.Equals(_defaultPlayerData.Containers.Wear))
                {
                    continue;
                }
                toRemove.Add(playerData.Key);
            }

            if (toRemove.Count > 0)
            {
                for (int i = 0; i < toRemove.Count; i++)
                {
                    _storedData.PlayerData.Remove(toRemove[i]);
                }
                Puts($"Removed {toRemove.Count} players with default settings from datafile.");
                SaveData();
            }

            Pool.FreeList(ref toRemove);
        }

        public void SaveData() => Interface.Oxide.DataFileSystem.WriteObject(Name, _storedData);

        public PlayerData GetPlayerData(ulong userID)
        {
            return _storedData.PlayerData[userID] ?? _defaultPlayerData;
        }

        #endregion DataFile

        #region Localization

        public string Lang(string key, string userIDString = null, params object[] args)
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
                public const string Syntax = Base + nameof(Syntax);
            }

            public static class Info
            {
                private const string Base = nameof(Info) + ".";
                public const string QuickSort = Base + nameof(QuickSort);
                public const string Style = Base + nameof(Style);
                public const string AutoLootAll = Base + nameof(AutoLootAll);
                public const string ContainerType = Base + nameof(ContainerType);
            }

            public static class Format
            {
                private const string Base = nameof(Format) + ".";
                public const string All = Base + nameof(All);
                public const string Ammo = Base + nameof(Ammo);
                public const string Attire = Base + nameof(Attire);
                public const string Components = Base + nameof(Components);
                public const string Construction = Base + nameof(Construction);
                public const string Deployables = Base + nameof(Deployables);
                public const string Deposit = Base + nameof(Deposit);
                public const string Disabled = Base + nameof(Disabled);
                public const string Electrical = Base + nameof(Electrical);
                public const string Enabled = Base + nameof(Enabled);
                public const string Existing = Base + nameof(Existing);
                public const string Food = Base + nameof(Food);
                public const string LootAll = Base + nameof(LootAll);
                public const string Medical = Base + nameof(Medical);
                public const string Misc = Base + nameof(Misc);
                public const string Prefix = Base + nameof(Prefix);
                public const string Resources = Base + nameof(Resources);
                public const string Tools = Base + nameof(Tools);
                public const string Traps = Base + nameof(Traps);
                public const string Weapons = Base + nameof(Weapons);
            }
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                [LangKeys.Error.NoPermission] = "You do not have permission to use this command!",
                [LangKeys.Format.All] = "All",
                [LangKeys.Format.Ammo] = "Ammo",
                [LangKeys.Format.Attire] = "Attire",
                [LangKeys.Format.Components] = "Components",
                [LangKeys.Format.Construction] = "Construction",
                [LangKeys.Format.Deployables] = "Deployables",
                [LangKeys.Format.Deposit] = "Deposit",
                [LangKeys.Format.Disabled] = "<color=#B22222>Disabled</color>",
                [LangKeys.Format.Electrical] = "Electrical",
                [LangKeys.Format.Enabled] = "<color=#228B22>Enabled</color>",
                [LangKeys.Format.Existing] = "Existing",
                [LangKeys.Format.Food] = "Food",
                [LangKeys.Format.LootAll] = "Loot All",
                [LangKeys.Format.Medical] = "Medical",
                [LangKeys.Format.Misc] = "Misc",
                [LangKeys.Format.Prefix] = "<color=#00FF00>[Quick Sort]</color>: ",
                [LangKeys.Format.Resources] = "Resources",
                [LangKeys.Format.Tools] = "Tools",
                [LangKeys.Format.Traps] = "Traps",
                [LangKeys.Format.Weapons] = "Weapons",
                [LangKeys.Info.AutoLootAll] = "Automated looting is now {0}",
                [LangKeys.Info.ContainerType] = "Quick Sort for container type {0} is now {1}",
                [LangKeys.Info.QuickSort] = "Quick Sort GUI is now {0}",
                [LangKeys.Info.Style] = "Quick Sort GUI style is now {0}",

                [LangKeys.Error.Syntax] = "List Commands:\n" +
                "<color=#FFFF00>/{0} on</color> - Enable GUI\n" +
                "<color=#FFFF00>/{0} off</color> - Disable GUI\n" +
                "<color=#FFFF00>/{0} auto</color> - Enable/Disable automated looting\n" +
                "<color=#FFFF00>/{0} <s | style> <center | lite | right | custom></color> - change GUI style\n" +
                "<color=#FFFF00>/{0} <c | conatiner> <main | wear | belt></color> - add/remove container type from the sort",
            }, this);
            lang.RegisterMessages(new Dictionary<string, string>
            {
                [LangKeys.Error.NoPermission] = "У вас нет разрешения на использование этой команды!",
                [LangKeys.Format.All] = "Всё",
                [LangKeys.Format.Ammo] = "Патроны",
                [LangKeys.Format.Attire] = "Одежда",
                [LangKeys.Format.Components] = "Компоненты",
                [LangKeys.Format.Construction] = "Конструкции",
                [LangKeys.Format.Deployables] = "Развертываемые",
                [LangKeys.Format.Deposit] = "Положить",
                [LangKeys.Format.Disabled] = "<color=#B22222>Отключена</color>",
                [LangKeys.Format.Electrical] = "Электричество",
                [LangKeys.Format.Enabled] = "<color=#228B22>Включена</color>",
                [LangKeys.Format.Existing] = "Существующие",
                [LangKeys.Format.Food] = "Еда",
                [LangKeys.Format.LootAll] = "Забрать всё",
                [LangKeys.Format.Medical] = "Медикаменты",
                [LangKeys.Format.Misc] = "Разное",
                [LangKeys.Format.Prefix] = "<color=#00FF00>[Быстрая сортировка]</color>: ",
                [LangKeys.Format.Resources] = "Ресурсы",
                [LangKeys.Format.Tools] = "Инструменты",
                [LangKeys.Format.Traps] = "Ловушки",
                [LangKeys.Format.Weapons] = "Оружие",
                [LangKeys.Info.AutoLootAll] = "Забирать всё автоматически теперь {0}",
                [LangKeys.Info.ContainerType] = "Быстрая сортировка для типа контейнера {0} теперь {1}",
                [LangKeys.Info.QuickSort] = "GUI быстрой сортировки теперь {0}",
                [LangKeys.Info.Style] = "Стиль GUI быстрой сортировки теперь {0}",

                [LangKeys.Error.Syntax] = "Список команд:\n" +
                "<color=#FFFF00>/{0} on</color> - Включить GUI\n" +
                "<color=#FFFF00>/{0} off</color> - Отключить GUI\n" +
                "<color=#FFFF00>/{0} auto</color> - Включить/Отключить забирать всё автоматически.\n" +
                "<color=#FFFF00>/{0} <s | style> <center | lite | right | custom></color> - изменить стиль GUI быстрой сортировки.\n" +
                "<color=#FFFF00>/{0} <c | conatiner> <main | wear | belt></color> - добавить/удалить тип контейнера для сортировки.",
            }, this, "ru");
        }

        #endregion Localization

        #region Oxide Hooks

        private void OnLootPlayer(BasePlayer player) => UiCreate(player);

        private void OnLootEntity(BasePlayer player, BaseEntity entity)
        {
            if (entity.IsValid() && !IsContainerExcluded(player, entity))
            {
                HandleLootEntity(player);
            }
        }

        private void OnPlayerLootEnd(PlayerLoot inventory)
        {
            BasePlayer player = inventory.baseEntity;

            if (player.IsValid())
            {
                UiDestroy(player);
            }
        }

        private void OnEntityDeath(BasePlayer player, HitInfo info)
        {
            if (player.IsValid() && !player.IsNpc && player.userID.IsSteamId())
            {
                UiDestroy(player);
            }
        }

        private void OnEntityKill(BasePlayer player)
        {
            if (player.IsValid() && !player.IsNpc && player.userID.IsSteamId())
            {
                UiDestroy(player);
            }
        }

        private void OnPlayerSleep(BasePlayer player) => UiDestroy(player);

        #endregion Oxide Hooks

        #region Commands

        private void CmdQuickSort(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, PermissionUse))
            {
                PlayerSendMessage(player, Lang(LangKeys.Error.NoPermission, player.UserIDString));
                return;
            }

            PlayerData playerData = _storedData.PlayerData[player.userID];

            if (playerData == null)
            {
                playerData = _defaultPlayerData;
                _storedData.PlayerData[player.userID] = playerData;
            }

            if (args == null || args.Length == 0)
            {
                PlayerSendMessage(player, Lang(LangKeys.Error.Syntax, player.UserIDString, _configData.GlobalSettings.Commands[0]));
                return;
            }

            switch (args[0].ToLower())
            {
                case "on":
                    if (!playerData.Enabled)
                    {
                        playerData.Enabled = true;
                        SaveData();
                    }
                    PlayerSendMessage(player, Lang(LangKeys.Info.QuickSort, player.UserIDString, Lang(LangKeys.Format.Enabled, player.UserIDString)));
                    return;
                case "off":
                    if (playerData.Enabled)
                    {
                        playerData.Enabled = false;
                        SaveData();
                    }
                    PlayerSendMessage(player, Lang(LangKeys.Info.QuickSort, player.UserIDString, Lang(LangKeys.Format.Disabled, player.UserIDString)));
                    return;
                case "auto":
                    playerData.AutoLootAll = !playerData.AutoLootAll;
                    SaveData();
                    PlayerSendMessage(player, Lang(LangKeys.Info.AutoLootAll, player.UserIDString, playerData.AutoLootAll ? Lang(LangKeys.Format.Enabled, player.UserIDString) : Lang(LangKeys.Format.Disabled, player.UserIDString)));
                    return;
                case "s":
                case "style":
                    {
                        if (args.Length > 1)
                        {
                            switch (args[1].ToLower())
                            {
                                case "center":
                                case "lite":
                                case "right":
                                case "custom":
                                    {
                                        playerData.UiStyle = args[1].ToLower();
                                        SaveData();
                                        PlayerSendMessage(player, Lang(LangKeys.Info.Style, player.UserIDString, args[1].ToLower()));
                                        return;
                                    }
                            }
                        }
                        break;
                    }
                case "c":
                case "container":
                    {
                        if (args.Length > 1)
                        {
                            switch (args[1].ToLower())
                            {
                                case "main":
                                    {
                                        bool flag = false;
                                        if (args.Length > 2 && bool.TryParse(args[2], out flag))
                                        {
                                            playerData.Containers.Main = flag;
                                        }
                                        else
                                        {
                                            playerData.Containers.Main = !playerData.Containers.Main;
                                        }
                                        SaveData();

                                        PlayerSendMessage(player, Lang(LangKeys.Info.ContainerType, player.UserIDString, "main", playerData.Containers.Main ? Lang(LangKeys.Format.Enabled, player.UserIDString) : Lang(LangKeys.Format.Disabled, player.UserIDString)));
                                        return;
                                    }
                                case "wear":
                                    {
                                        bool flag = false;
                                        if (args.Length > 2 && bool.TryParse(args[2], out flag))
                                        {
                                            playerData.Containers.Wear = flag;
                                        }
                                        else
                                        {
                                            playerData.Containers.Wear = !playerData.Containers.Wear;
                                        }
                                        SaveData();

                                        PlayerSendMessage(player, Lang(LangKeys.Info.ContainerType, player.UserIDString, "wear", playerData.Containers.Wear ? Lang(LangKeys.Format.Enabled, player.UserIDString) : Lang(LangKeys.Format.Disabled, player.UserIDString)));
                                        return;
                                    }
                                case "belt":
                                    {
                                        bool flag = false;
                                        if (args.Length > 2 && bool.TryParse(args[2], out flag))
                                        {
                                            playerData.Containers.Belt = flag;
                                        }
                                        else
                                        {
                                            playerData.Containers.Belt = !playerData.Containers.Belt;
                                        }
                                        SaveData();

                                        PlayerSendMessage(player, Lang(LangKeys.Info.ContainerType, player.UserIDString, "belt", playerData.Containers.Belt ? Lang(LangKeys.Format.Enabled, player.UserIDString) : Lang(LangKeys.Format.Disabled, player.UserIDString)));
                                        return;
                                    }
                            }
                        }
                        break;
                    }
            }

            PlayerSendMessage(player, Lang(LangKeys.Error.Syntax, player.UserIDString, _configData.GlobalSettings.Commands[0]));
        }

        [ConsoleCommand("quicksortgui")]
        private void SortCommand(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player.IsValid() && permission.UserHasPermission(player.UserIDString, PermissionUse))
            {
                try
                {
                    SortItems(player, arg.Args);
                }
                catch { }
            }
        }

        [ConsoleCommand("quicksortgui.lootall")]
        private void LootAllCommand(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player.IsValid() && permission.UserHasPermission(player.UserIDString, PermissionLootAll))
            {
                timer.Once(_configData.GlobalSettings.LootAllDelay, () => LootAll(player));
            }
        }

        [ConsoleCommand("quicksortgui.lootdelay")]
        private void LootDelayCommand(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg.Player();
            if (player.IsValid() && player.IsAdmin)
            {
                int x;
                if (int.TryParse(arg.Args[0], out x))
                {
                    _configData.GlobalSettings.LootAllDelay = x;
                    SaveConfig();
                }
            }
        }

        #endregion Commands

        #region Loot Handling

        public void HandleLootEntity(BasePlayer player)
        {//We need this to wait for container initialization
            NextTick(() =>
            {
                if (permission.UserHasPermission(player.UserIDString, PermissionAutoLootAll)
                && AutoLootAll(player))
                {
                    return;
                }

                UiCreate(player);
            });
        }

        public bool AutoLootAll(BasePlayer player)
        {
            PlayerData playerData = GetPlayerData(player.userID);

            if (!playerData.AutoLootAll)
            {
                return false;
            }

            List<ItemContainer> containers = GetLootedInventory(player);

            if (containers == null)
            {
                return false;
            }

            int fullyLooted = 0;
            foreach (ItemContainer itemContainer in containers)
            {
                if (itemContainer.HasFlag(ItemContainer.Flag.NoItemInput))
                {
                    LootAll(player);
                    if (itemContainer.IsEmpty())
                    {
                        fullyLooted++;
                    }
                }
            }

            if (fullyLooted > 0 && fullyLooted == containers.Count)
            {
                player.EndLooting();
                // HACK: Send empty respawn information to fully close the player inventory (toggle backpack closed)
                player.ClientRPCPlayer(null, player, "OnRespawnInformation");
                return true;
            }

            return false;
        }

        public void LootAll(BasePlayer player)
        {
            List<ItemContainer> containers = GetLootedInventory(player);

            if (containers == null)
            {
                return;
            }

            if (!_configData.GlobalSettings.LootSleepers && IsOwnerSleeper(containers[0]))
            {
                return;
            }

            List<Item> itemsSelected = Pool.GetList<Item>();

            foreach (ItemContainer itemContainer in containers)
            {
                for (int i = 0; i < itemContainer.itemList.Count; i++)
                {
                    itemsSelected.Add(itemContainer.itemList[i]);
                }
            }

            itemsSelected.Sort((item1, item2) => item2.info.itemid.CompareTo(item1.info.itemid));

            foreach (Item item in itemsSelected)
            {
                int amount = item.amount;

                if (item.MoveToContainer(player.inventory.containerMain))
                {
                    _noteInv[0] = item.info.itemid;
                    _noteInv[1] = amount;
                    player.Command("note.inv", _noteInv);
                    continue;
                }

                if (item.MoveToContainer(player.inventory.containerBelt))
                {
                    _noteInv[0] = item.info.itemid;
                    _noteInv[1] = amount;
                    player.Command("note.inv", _noteInv);
                    continue;
                }

                int movedAmount = amount - item.amount;

                if (movedAmount > 0)
                {
                    _noteInv[0] = item.info.itemid;
                    _noteInv[1] = movedAmount;
                    player.Command("note.inv", _noteInv);
                }
            }

            Pool.FreeList(ref itemsSelected);
        }

        public void SortItems(BasePlayer player, string[] args)
        {
            if (!player.IsValid())
            {
                return;
            }

            PlayerContainers type = GetPlayerData(player.userID).Containers;
            ItemContainer container = GetLootedInventory(player)[0];
            ItemContainer playerMain = player.inventory?.containerMain;
            ItemContainer playerWear = player.inventory?.containerWear;
            ItemContainer playerBelt = player.inventory?.containerBelt;

            if (container == null || playerMain == null || container.HasFlag(ItemContainer.Flag.NoItemInput))
            {
                return;
            }

            List<Item> itemsSelected = Pool.GetList<Item>();

            if (args == null)
            {
                if (_configData.GlobalSettings.Containers.Main && (type == null || type.Main))
                {
                    for (int i = 0; i < playerMain.itemList.Count; i++)
                    {
                        itemsSelected.Add(playerMain.itemList[i]);
                    }
                }

                if (playerWear != null && _configData.GlobalSettings.Containers.Wear && type != null && type.Wear)
                {
                    for (int i = 0; i < playerWear.itemList.Count; i++)
                    {
                        itemsSelected.Add(playerWear.itemList[i]);
                    }
                }

                if (playerBelt != null && _configData.GlobalSettings.Containers.Belt && type != null && type.Belt)
                {
                    for (int i = 0; i < playerBelt.itemList.Count; i++)
                    {
                        itemsSelected.Add(playerBelt.itemList[i]);
                    }
                }
            }
            else
            {
                if (args[0].Equals("existing"))
                {
                    if (_configData.GlobalSettings.Containers.Main && (type == null || type.Main))
                    {
                        AddExistingItems(itemsSelected, playerMain, container);
                    }

                    if (playerWear != null && _configData.GlobalSettings.Containers.Wear && type != null && type.Wear)
                    {
                        AddExistingItems(itemsSelected, playerWear, container);
                    }

                    if (playerBelt != null && _configData.GlobalSettings.Containers.Belt && type != null && type.Belt)
                    {
                        AddExistingItems(itemsSelected, playerBelt, container);
                    }
                }
                else
                {
                    ItemCategory category = StringToItemCategory(args[0]);
                    if (_configData.GlobalSettings.Containers.Main && (type == null || type.Main))
                    {
                        AddItemsOfType(itemsSelected, playerMain, category);
                    }

                    if (playerWear != null && _configData.GlobalSettings.Containers.Wear && type != null && type.Wear)
                    {
                        AddItemsOfType(itemsSelected, playerWear, category);
                    }

                    if (playerBelt != null && _configData.GlobalSettings.Containers.Belt && type != null && type.Belt)
                    {
                        AddItemsOfType(itemsSelected, playerBelt, category);
                    }
                }
            }

            itemsSelected.Sort((item1, item2) => item2.info.itemid.CompareTo(item1.info.itemid));

            MoveItems(itemsSelected, container);

            Pool.FreeList(ref itemsSelected);
        }

        public void AddExistingItems(List<Item> list, ItemContainer primary, ItemContainer secondary)
        {
            if (primary == null || secondary == null)
            {
                return;
            }

            foreach (Item primaryItem in primary.itemList)
            {
                foreach (Item secondaryItem in secondary.itemList)
                {
                    if (primaryItem.info.itemid == secondaryItem.info.itemid)
                    {
                        list.Add(primaryItem);
                        break;
                    }
                }
            }
        }

        public void AddItemsOfType(List<Item> list, ItemContainer container, ItemCategory category)
        {
            foreach (Item item in container.itemList)
            {
                if (item.info.category == category)
                {
                    list.Add(item);
                }
            }
        }

        public List<ItemContainer> GetLootedInventory(BasePlayer player)
        {
            PlayerLoot playerLoot = player.inventory.loot;

            if (playerLoot != null && playerLoot.IsLooting())
            {
                List<ItemContainer> containers = playerLoot.containers;

                foreach (ItemContainer container in containers)
                {
                    BaseEntity entity = container.entityOwner;

                    if (entity.IsValid() && IsContainerExcluded(player, entity))
                    {
                        return null;
                    }
                }

                return containers;
            }

            return null;
        }

        public void MoveItems(IEnumerable<Item> items, ItemContainer to)
        {
            foreach (Item item in items)
            {
                item.MoveToContainer(to);
            }
        }

        public ItemCategory StringToItemCategory(string categoryName)
        {
            string[] categoryNames = Enum.GetNames(typeof(ItemCategory));

            for (int i = 0; i < categoryNames.Length; i++)
            {
                if (categoryName.ToLower().Equals(categoryNames[i].ToLower()))
                {
                    return (ItemCategory)i;
                }
            }

            return (ItemCategory)categoryNames.Length;
        }

        public bool IsContainerExcluded(BasePlayer player, BaseEntity entity)
        {
            if (entity is ShopFront || entity is BigWheelBettingTerminal || entity is NPCVendingMachine)
            {
                return true;
            }

            if (entity is VendingMachine)
            {
                VendingMachine vendingMachine = (VendingMachine)entity;

                if (!vendingMachine.PlayerBehind(player))
                {
                    return true;
                }
            }

            if (entity is DropBox)
            {
                DropBox dropBox = (DropBox)entity;

                if (!dropBox.PlayerBehind(player))
                {
                    return true;
                }
            }

            if (entity is IItemContainerEntity)
            {
                IItemContainerEntity container = (IItemContainerEntity)entity;

                if (container.inventory.IsLocked())
                {
                    return true;
                }
            }

            if (_cacheContainersExcluded.Contains(entity.prefabID))
            {
                return true;
            }

            if (Interface.CallHook("QuickSortExcluded", player, entity) != null)
            {
                return true;
            }

            return false;
        }

        public static bool IsOwnerSleeper(ItemContainer container)
        {
            BasePlayer playerOwner = container.playerOwner;
            if ((object)playerOwner == null || !IsPlayerContainer(container, playerOwner))
                return false;

            return playerOwner.IsSleeping();
        }

        public static bool IsPlayerContainer(ItemContainer container, BasePlayer player)
        {
            return player.inventory.containerMain == container
                || player.inventory.containerBelt == container
                || player.inventory.containerWear == container;
        }

        #endregion Loot Handling

        #region Helpers

        public void UnsubscribeHooks()
        {
            Unsubscribe(nameof(OnEntityDeath));
            Unsubscribe(nameof(OnEntityKill));
            Unsubscribe(nameof(OnLootEntity));
            Unsubscribe(nameof(OnLootPlayer));
            Unsubscribe(nameof(OnPlayerLootEnd));
            Unsubscribe(nameof(OnPlayerSleep));
        }

        public void SubscribeHooks()
        {
            //Subscribe(nameof(OnEntityDeath));
            //Subscribe(nameof(OnEntityKill));
            Subscribe(nameof(OnLootEntity));
            Subscribe(nameof(OnLootPlayer));
            Subscribe(nameof(OnPlayerLootEnd));
            //Subscribe(nameof(OnPlayerSleep));
        }

        public void RegisterPermissions()
        {
            permission.RegisterPermission(PermissionAutoLootAll, this);
            permission.RegisterPermission(PermissionLootAll, this);
            permission.RegisterPermission(PermissionUse, this);
        }

        public void AddCommands()
        {
            if (_configData.GlobalSettings.Commands.Length == 0)
            {
                _configData.GlobalSettings.Commands = new[] { "qs" };
                SaveConfig();
            }

            foreach (string command in _configData.GlobalSettings.Commands)
            {
                cmd.AddChatCommand(command, this, nameof(CmdQuickSort));
            }
        }

        public void CreateCache()
        {
            _defaultPlayerData = new PlayerData
            {
                Enabled = _configData.GlobalSettings.DefaultEnabled,
                AutoLootAll = _configData.GlobalSettings.AutoLootAll,
                UiStyle = _configData.GlobalSettings.DefaultUiStyle,
                Containers = _configData.GlobalSettings.Containers,
            };

            foreach (string container in _configData.GlobalSettings.ContainersExcluded)
            {
                _cacheContainersExcluded.Add(StringPool.Get(container));
            }

            int id = 0;
            foreach (string language in lang.GetLanguages(this))
            {
                _cacheLanguageIDs[language] = ++id;
            }
        }

        public void PlayerSendMessage(BasePlayer player, string message)
        {
            player.SendConsoleCommand("chat.add", 2, _configData.GlobalSettings.SteamIDIcon, string.IsNullOrEmpty(Lang(LangKeys.Format.Prefix, player.UserIDString)) ? message : Lang(LangKeys.Format.Prefix, player.UserIDString) + message);
        }

        public int GetUiId(BasePlayer player, PlayerData playerData)
        {
            int id = 0;
            switch (playerData.UiStyle)
            {
                case "center":
                    id += 1;
                    break;
                case "lite":
                    id += 2;
                    break;
                case "right":
                    id += 3;
                    break;
                case "custom":
                    id += 4;
                    break;
            }
            //Max. value = 2
            id *= 10;
            if (permission.UserHasPermission(player.UserIDString, PermissionLootAll))
            {
                id += 1;
            }
            //Max. value = 7117
            id *= 10000;
            id += _cacheLanguageIDs[lang.GetLanguage(player.UserIDString)];

            return id;
        }

        #endregion Helpers

        #region User Interface

        public void UiCreate(BasePlayer player)
        {
            if (!permission.UserHasPermission(player.UserIDString, PermissionUse))
            {
                return;
            }

            PlayerData playerData = GetPlayerData(player.userID);

            if (!playerData.Enabled)
            {
                return;
            }

            UiDestroy(player);

            if (!_uiViewers.Add(player.userID))
            {
                return;
            }

            int uiId = GetUiId(player, playerData);
            string cachedJson = _cacheUiJson[uiId];

            if (string.IsNullOrWhiteSpace(cachedJson))
            {
                switch (playerData.UiStyle)
                {
                    case "center":
                        cachedJson = UiGetJsonCenter(player);
                        break;
                    case "lite":
                        cachedJson = UiGetJsonLite(player);
                        break;
                    case "right":
                        cachedJson = UiGetJsonRight(player);
                        break;
                    case "custom":
                        cachedJson = UiGetJsonCustom(player);
                        break;
                }
                _cacheUiJson[uiId] = cachedJson;
            }

            CuiHelper.AddUi(player, cachedJson);
        }

        public void UiDestroy(BasePlayer player)
        {
            if (!_uiViewers.Remove(player.userID))
                return;

            CuiHelper.DestroyUi(player, GUIPanelName);
        }

        #region UI Custom

        public string UiGetJsonCustom(BasePlayer player)
        {
            CuiElementContainer elements = new CuiElementContainer();
            ConfigData.UiConfiguration customUISettings = _configData.CustomUISettings;

            string panel = elements.Add(new CuiPanel
            {
                Image = { Color = customUISettings.Color },
                RectTransform = {
                    AnchorMin = customUISettings.AnchorsMin,
                    AnchorMax = customUISettings.AnchorsMax,
                    OffsetMin = customUISettings.OffsetsMin,
                    OffsetMax = customUISettings.OffsetsMax
                }
            }, "Hud.Menu", GUIPanelName);

            //left
            elements.Add(new CuiLabel
            {
                Text = { Text = Lang(LangKeys.Format.Deposit, player.UserIDString), FontSize = customUISettings.TextSize, Align = TextAnchor.MiddleCenter, Color = customUISettings.TextColor },
                RectTransform = { AnchorMin = "0.02 0.8", AnchorMax = "0.35 1" }
            }, panel);

            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui existing", Color = customUISettings.ButtonsColor },
                RectTransform = { AnchorMin = "0.02 0.6", AnchorMax = "0.35 0.8" },
                Text = { Text = Lang(LangKeys.Format.Existing, player.UserIDString), FontSize = customUISettings.TextSize, Align = TextAnchor.MiddleCenter, Color = customUISettings.TextColor }
            }, panel);

            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui", Color = customUISettings.ButtonsColor },
                RectTransform = { AnchorMin = "0.02 0.35", AnchorMax = "0.35 0.55" },
                Text = { Text = Lang(LangKeys.Format.All, player.UserIDString), FontSize = customUISettings.TextSize, Align = TextAnchor.MiddleCenter, Color = customUISettings.TextColor }
            }, panel);

            if (permission.UserHasPermission(player.UserIDString, PermissionLootAll))
            {
                elements.Add(new CuiButton
                {
                    Button = { Command = "quicksortgui.lootall", Color = customUISettings.LootAllColor },
                    RectTransform = { AnchorMin = "0.02 0.05", AnchorMax = "0.35 0.3" },
                    Text = { Text = Lang(LangKeys.Format.LootAll, player.UserIDString), FontSize = customUISettings.TextSize, Align = TextAnchor.MiddleCenter, Color = customUISettings.TextColor }
                }, panel);
            }

            //center
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui weapon", Color = customUISettings.ButtonsColor },
                RectTransform = { AnchorMin = "0.37 0.818", AnchorMax = "0.65 0.949" },
                Text = { Text = Lang(LangKeys.Format.Weapons, player.UserIDString), FontSize = customUISettings.CategoriesTextSize, Align = TextAnchor.MiddleCenter, Color = customUISettings.TextColor }
            }, panel);

            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui ammunition", Color = customUISettings.ButtonsColor },
                RectTransform = { AnchorMin = "0.37 0.664", AnchorMax = "0.65 0.796" },
                Text = { Text = Lang(LangKeys.Format.Ammo, player.UserIDString), FontSize = customUISettings.CategoriesTextSize, Align = TextAnchor.MiddleCenter, Color = customUISettings.TextColor }
            }, panel);

            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui medical", Color = customUISettings.ButtonsColor },
                RectTransform = { AnchorMin = "0.37 0.511", AnchorMax = "0.65 0.642" },
                Text = { Text = Lang(LangKeys.Format.Medical, player.UserIDString), FontSize = customUISettings.CategoriesTextSize, Align = TextAnchor.MiddleCenter, Color = customUISettings.TextColor }
            }, panel);

            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui attire", Color = customUISettings.ButtonsColor },
                RectTransform = { AnchorMin = "0.37 0.358", AnchorMax = "0.65 0.489" },
                Text = { Text = Lang(LangKeys.Format.Attire, player.UserIDString), FontSize = customUISettings.CategoriesTextSize, Align = TextAnchor.MiddleCenter, Color = customUISettings.TextColor }
            }, panel);

            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui resources", Color = customUISettings.ButtonsColor },
                RectTransform = { AnchorMin = "0.37 0.204", AnchorMax = "0.65 0.336" },
                Text = { Text = Lang(LangKeys.Format.Resources, player.UserIDString), FontSize = customUISettings.CategoriesTextSize, Align = TextAnchor.MiddleCenter, Color = customUISettings.TextColor }
            }, panel);

            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui component", Color = customUISettings.ButtonsColor },
                RectTransform = { AnchorMin = "0.37 0.051", AnchorMax = "0.65 0.182" },
                Text = { Text = Lang(LangKeys.Format.Components, player.UserIDString), FontSize = customUISettings.CategoriesTextSize, Align = TextAnchor.MiddleCenter, Color = customUISettings.TextColor }
            }, panel);

            //right
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui construction", Color = customUISettings.ButtonsColor },
                RectTransform = { AnchorMin = "0.67 0.818", AnchorMax = "0.98 0.949" },
                Text = { Text = Lang(LangKeys.Format.Construction, player.UserIDString), FontSize = customUISettings.CategoriesTextSize, Align = TextAnchor.MiddleCenter, Color = customUISettings.TextColor }
            }, panel);

            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui items", Color = customUISettings.ButtonsColor },
                RectTransform = { AnchorMin = "0.67 0.664", AnchorMax = "0.98 0.796" },
                Text = { Text = Lang(LangKeys.Format.Deployables, player.UserIDString), FontSize = customUISettings.CategoriesTextSize, Align = TextAnchor.MiddleCenter, Color = customUISettings.TextColor }
            }, panel);

            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui tool", Color = customUISettings.ButtonsColor },
                RectTransform = { AnchorMin = "0.67 0.511", AnchorMax = "0.98 0.642" },
                Text = { Text = Lang(LangKeys.Format.Tools, player.UserIDString), FontSize = customUISettings.CategoriesTextSize, Align = TextAnchor.MiddleCenter, Color = customUISettings.TextColor }
            }, panel);

            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui food", Color = customUISettings.ButtonsColor },
                RectTransform = { AnchorMin = "0.67 0.358", AnchorMax = "0.82 0.489" },
                Text = { Text = Lang(LangKeys.Format.Food, player.UserIDString), FontSize = customUISettings.CategoriesTextSize, Align = TextAnchor.MiddleCenter, Color = customUISettings.TextColor }
            }, panel);

            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui misc", Color = customUISettings.ButtonsColor },
                RectTransform = { AnchorMin = "0.83 0.358", AnchorMax = "0.98 0.489" },
                Text = { Text = Lang(LangKeys.Format.Misc, player.UserIDString), FontSize = customUISettings.CategoriesTextSize, Align = TextAnchor.MiddleCenter, Color = customUISettings.TextColor }
            }, panel);

            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui traps", Color = customUISettings.ButtonsColor },
                RectTransform = { AnchorMin = "0.67 0.204", AnchorMax = "0.98 0.336" },
                Text = { Text = Lang(LangKeys.Format.Traps, player.UserIDString), FontSize = customUISettings.CategoriesTextSize, Align = TextAnchor.MiddleCenter, Color = customUISettings.TextColor }
            }, panel);

            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui electrical", Color = customUISettings.ButtonsColor },
                RectTransform = { AnchorMin = "0.67 0.051", AnchorMax = "0.98 0.182" },
                Text = { Text = Lang(LangKeys.Format.Electrical, player.UserIDString), FontSize = customUISettings.CategoriesTextSize, Align = TextAnchor.MiddleCenter, Color = customUISettings.TextColor }
            }, panel);

            return CuiHelper.ToJson(elements);
        }

        #endregion UI Custom

        #region UI Center

        public string UiGetJsonCenter(BasePlayer player)
        {
            CuiElementContainer elements = new CuiElementContainer();

            string panel = elements.Add(new CuiPanel
            {
                Image = { Color = "0.5 0.5 0.5 0.33" },
                RectTransform = {
                    AnchorMin = "0.5 0.0",
                    AnchorMax = "0.5 0.0",
                    OffsetMin = "-198 472",
                    OffsetMax = "182 626"
                }
            }, "Hud.Menu", GUIPanelName);

            //left
            elements.Add(new CuiLabel
            {
                Text = { Text = Lang(LangKeys.Format.Deposit, player.UserIDString), FontSize = 16, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8" },
                RectTransform = { AnchorMin = "0.02 0.8", AnchorMax = "0.35 1" }
            }, panel);

            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui existing", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0.02 0.6", AnchorMax = "0.35 0.8" },
                Text = { Text = Lang(LangKeys.Format.Existing, player.UserIDString), FontSize = 16, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8" }
            }, panel);

            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0.02 0.35", AnchorMax = "0.35 0.55" },
                Text = { Text = Lang(LangKeys.Format.All, player.UserIDString), FontSize = 16, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8" }
            }, panel);

            if (permission.UserHasPermission(player.UserIDString, PermissionLootAll))
            {
                elements.Add(new CuiButton
                {
                    Button = { Command = "quicksortgui.lootall", Color = "0.41 0.50 0.25 0.8" },
                    RectTransform = { AnchorMin = "0.02 0.05", AnchorMax = "0.35 0.3" },
                    Text = { Text = Lang(LangKeys.Format.LootAll, player.UserIDString), FontSize = 16, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8" }
                }, panel);
            }

            //center
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui weapon", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0.37 0.818", AnchorMax = "0.65 0.949" },
                Text = { Text = Lang(LangKeys.Format.Weapons, player.UserIDString), FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8" }
            }, panel);

            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui ammunition", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0.37 0.664", AnchorMax = "0.65 0.796" },
                Text = { Text = Lang(LangKeys.Format.Ammo, player.UserIDString), FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8" }
            }, panel);

            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui medical", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0.37 0.511", AnchorMax = "0.65 0.642" },
                Text = { Text = Lang(LangKeys.Format.Medical, player.UserIDString), FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8" }
            }, panel);

            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui attire", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0.37 0.358", AnchorMax = "0.65 0.489" },
                Text = { Text = Lang(LangKeys.Format.Attire, player.UserIDString), FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8" }
            }, panel);

            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui resources", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0.37 0.204", AnchorMax = "0.65 0.336" },
                Text = { Text = Lang(LangKeys.Format.Resources, player.UserIDString), FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8" }
            }, panel);

            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui component", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0.37 0.051", AnchorMax = "0.65 0.182" },
                Text = { Text = Lang(LangKeys.Format.Components, player.UserIDString), FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8" }
            }, panel);

            //right
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui construction", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0.67 0.818", AnchorMax = "0.98 0.949" },
                Text = { Text = Lang(LangKeys.Format.Construction, player.UserIDString), FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8" }
            }, panel);

            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui items", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0.67 0.664", AnchorMax = "0.98 0.796" },
                Text = { Text = Lang(LangKeys.Format.Deployables, player.UserIDString), FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8" }
            }, panel);

            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui tool", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0.67 0.511", AnchorMax = "0.98 0.642" },
                Text = { Text = Lang(LangKeys.Format.Tools, player.UserIDString), FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8" }
            }, panel);

            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui food", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0.67 0.358", AnchorMax = "0.82 0.489" },
                Text = { Text = Lang(LangKeys.Format.Food, player.UserIDString), FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8" }
            }, panel);

            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui misc", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0.83 0.358", AnchorMax = "0.98 0.489" },
                Text = { Text = Lang(LangKeys.Format.Misc, player.UserIDString), FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8" }
            }, panel);

            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui traps", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0.67 0.204", AnchorMax = "0.98 0.336" },
                Text = { Text = Lang(LangKeys.Format.Traps, player.UserIDString), FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8" }
            }, panel);

            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui electrical", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0.67 0.051", AnchorMax = "0.98 0.182" },
                Text = { Text = Lang(LangKeys.Format.Electrical, player.UserIDString), FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8" }
            }, panel);

            return CuiHelper.ToJson(elements);
        }

        #endregion UI Center

        #region UI Lite

        public string UiGetJsonLite(BasePlayer player)
        {
            CuiElementContainer elements = new CuiElementContainer();

            string panel = elements.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0" },
                RectTransform = {
                    AnchorMin = "0.5 0.0",
                    AnchorMax = "0.5 0.0",
                    OffsetMin = "-56 340",
                    OffsetMax = "179 359"
                }
            }, "Hud.Menu", GUIPanelName);

            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui existing", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "0.44 1" },
                Text = { Text = Lang(LangKeys.Format.Existing, player.UserIDString), FontSize = 13, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8" }
            }, panel);

            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0.46 0", AnchorMax = "0.60 1" },
                Text = { Text = Lang(LangKeys.Format.All, player.UserIDString), FontSize = 13, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8" }
            }, panel);

            if (permission.UserHasPermission(player.UserIDString, PermissionLootAll))
            {
                elements.Add(new CuiButton
                {
                    Button = { Command = "quicksortgui.lootall", Color = "0.41 0.50 0.25 0.8" },
                    RectTransform = { AnchorMin = "0.62 0", AnchorMax = "1 1" },
                    Text = { Text = Lang(LangKeys.Format.LootAll, player.UserIDString), FontSize = 13, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8" }
                }, panel);
            }

            return CuiHelper.ToJson(elements);
        }

        #endregion UI Lite

        #region UI Right

        public string UiGetJsonRight(BasePlayer player)
        {
            CuiElementContainer elements = new CuiElementContainer();

            string panel = elements.Add(new CuiPanel
            {
                Image = { Color = "0.3 0.3 0.3 1" },
                RectTransform = {
                    AnchorMin = "0.5 1.0",
                    AnchorMax = "0.5 1.0",
                    OffsetMin = "192.5 -88.7",
                    OffsetMax = "572.5 0"
                }
            }, "Hud.Menu", GUIPanelName);

            /*left
            elements.Add(new CuiLabel
            {
                Text = { Text = Lang(LangKeys.Format.Deposit, player.UserIDString), FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8" },
                RectTransform = { AnchorMin = "0.02 0.8", AnchorMax = "0.35 1" }
            }, panel);*/

            //First column
            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui weapon", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0.0 0.765", AnchorMax = "0.239 0.98" },
                Text = { Text = Lang(LangKeys.Format.Weapons, player.UserIDString), FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8" }
            }, panel);

            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui existing", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0.0 0.51", AnchorMax = "0.239 0.725" },
                Text = { Text = Lang(LangKeys.Format.Existing, player.UserIDString), FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8" }
            }, panel);

            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0.0 0.255", AnchorMax = "0.239 0.47" },
                Text = { Text = Lang(LangKeys.Format.All, player.UserIDString), FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8" }
            }, panel);

            if (permission.UserHasPermission(player.UserIDString, PermissionLootAll))
            {
                elements.Add(new CuiButton
                {
                    Button = { Command = "quicksortgui.lootall", Color = "0.41 0.50 0.25 1" },
                    RectTransform = { AnchorMin = "0.0 0.0", AnchorMax = "0.239 0.215" },
                    Text = { Text = Lang(LangKeys.Format.LootAll, player.UserIDString), FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8" }
                }, panel);
            }

            //Second column

            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui ammunition", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0.249 0.765", AnchorMax = "0.488 0.98" },
                Text = { Text = Lang(LangKeys.Format.Ammo, player.UserIDString), FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8" }
            }, panel);

            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui medical", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0.249 0.51", AnchorMax = "0.488 0.725" },
                Text = { Text = Lang(LangKeys.Format.Medical, player.UserIDString), FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8" }
            }, panel);

            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui attire", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0.249 0.255", AnchorMax = "0.488 0.47" },
                Text = { Text = Lang(LangKeys.Format.Attire, player.UserIDString), FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8" }
            }, panel);

            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui resources", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0.249 0.0", AnchorMax = "0.488 0.215" },
                Text = { Text = Lang(LangKeys.Format.Resources, player.UserIDString), FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8" }
            }, panel);

            //Third column

            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui component", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0.498 0.765", AnchorMax = "0.737 0.98" },
                Text = { Text = Lang(LangKeys.Format.Components, player.UserIDString), FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8" }
            }, panel);

            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui construction", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0.498 0.51", AnchorMax = "0.737 0.725" },
                Text = { Text = Lang(LangKeys.Format.Construction, player.UserIDString), FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8" }
            }, panel);

            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui items", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0.498 0.255", AnchorMax = "0.737 0.47" },
                Text = { Text = Lang(LangKeys.Format.Deployables, player.UserIDString), FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8" }
            }, panel);

            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui tool", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0.498 0.0", AnchorMax = "0.737 0.215" },
                Text = { Text = Lang(LangKeys.Format.Tools, player.UserIDString), FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8" }
            }, panel);

            //Fourth column

            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui food", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0.747 0.765", AnchorMax = "0.997 0.98" },
                Text = { Text = Lang(LangKeys.Format.Food, player.UserIDString), FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8" }
            }, panel);

            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui misc", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0.747 0.51", AnchorMax = "0.997 0.725" },
                Text = { Text = Lang(LangKeys.Format.Misc, player.UserIDString), FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8" }
            }, panel);

            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui traps", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0.747 0.255", AnchorMax = "0.997 0.47" },
                Text = { Text = Lang(LangKeys.Format.Traps, player.UserIDString), FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8" }
            }, panel);

            elements.Add(new CuiButton
            {
                Button = { Command = "quicksortgui electrical", Color = "0.75 0.43 0.18 0.8" },
                RectTransform = { AnchorMin = "0.747 0.0", AnchorMax = "0.997 0.215" },
                Text = { Text = Lang(LangKeys.Format.Electrical, player.UserIDString), FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "0.77 0.92 0.67 0.8" }
            }, panel);

            return CuiHelper.ToJson(elements);
        }

        #endregion UI Right

        #endregion User Interface
    }
}