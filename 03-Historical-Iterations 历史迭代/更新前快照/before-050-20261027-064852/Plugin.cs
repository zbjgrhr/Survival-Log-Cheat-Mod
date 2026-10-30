using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using GameCore.HotUpdate;
using GameCore.HotUpdate.Battle.Logic;
using GameCore.HotUpdate.ReduxUI;
using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace SurvivalLogTrainer
{
    [BepInPlugin("local.survivallog.safe-trainer", "Survival Log Safe Trainer", "0.4.2")]
    [BepInProcess("SurvivalLog.exe")]
    public sealed class Plugin : BasePlugin
    {
        internal static Plugin Instance;
        internal static readonly string Session = Guid.NewGuid().ToString("N");
        internal static readonly string BridgeDirectory = Path.Combine(Paths.ConfigPath, "SurvivalLogTrainer");
        internal static readonly string CommandPath = Path.Combine(BridgeDirectory, "command.txt");
        internal static readonly string StatusPath = Path.Combine(BridgeDirectory, "status.txt");
        internal static readonly string PersistentDirectory = Path.Combine(BridgeDirectory, "save-settings");
        internal static int Epoch;
        internal static float MoveMultiplier = 1f;
        internal static float TimeMultiplier = 1f;
        internal static int SkillMultiplier = 1;
        internal static bool InfiniteStamina, InfiniteSatiety, InfiniteMorale, InfiniteBurden;
        internal static bool InfiniteShelfLife, InfiniteUses, InfiniteDurability;
        internal static bool InfiniteGeneration, InfiniteBattery, InfiniteGeneratorFuel, InfiniteCookingFuel, InstantPlants;
        internal static bool InfiniteStorageBurden, ZeroTravelTime, PersistStorageSettings;
        internal static int BagWidth, BagHeight;
        internal static int StorageWidth, StorageHeight;
        internal static Config_Bag ActiveBagConfig;
        internal static int OriginalBagWidth, OriginalBagHeight;
        internal static int AppliedExtraBurden;
        internal static long BagHookCalls, BurdenHookCalls;
        internal static long TimeHookCalls;
        internal static float LastTimeBaseDelta, LastTimeEffectiveDelta;
        internal static long MoveHookCalls;
        internal static float LastBaseMoveSpeed;
        internal static float LastEffectiveMoveSpeed;
        internal static long LastSequence;
        internal static string LastResult = "就绪";
        internal static string PatchErrors = "";
        internal static DateTime LastLeaseUtc = DateTime.MinValue;
        internal static IntPtr LastLeaderPointer;
        internal static IntPtr PlayerBagPointer;
        internal static long PlayerOwnerId;
        internal static int PlayerBagConfigId;
        internal static long StorageConfigHits;
        internal static long TravelTimeHookCalls;
        internal static long ShelfLifeHookCalls;
        internal static string CurrentPersistentSaveId = "";
        internal static string CurrentPersistentPath = "";
        internal static bool PendingUnconfiguredGeometryRestore;
        internal sealed class StorageOriginal
        {
            internal Config_Bag Config;
            internal int Width, Height, Burden;
        }
        internal static readonly Dictionary<IntPtr, StorageOriginal> StorageOriginals = new Dictionary<IntPtr, StorageOriginal>();
        private static DateTime nextTickUtc;

        public override void Load()
        {
            Instance = this;
            Directory.CreateDirectory(BridgeDirectory);
            Directory.CreateDirectory(PersistentDirectory);
            try
            {
                var harmony = new Harmony("local.survivallog.safe-trainer");
                Patch(harmony, typeof(MoveSpeedPatch));
                Patch(harmony, typeof(SaveBoundaryPatch));
                Patch(harmony, typeof(PlayerAttributePatch));
                Patch(harmony, typeof(ItemRotPatch));
                Patch(harmony, typeof(ItemUsesPatch));
                Patch(harmony, typeof(FurnitureDamagePatch));
                Patch(harmony, typeof(PowerGenerationPatch));
                Patch(harmony, typeof(BatteryPatch));
                Patch(harmony, typeof(GeneratorFuelPatch));
                Patch(harmony, typeof(CookingFuelPatch));
                Patch(harmony, typeof(PlantGrowthPatch));
                Patch(harmony, typeof(SkillExpPatch));
                Patch(harmony, typeof(TimeSaveGuardPatch));
                Patch(harmony, typeof(StorageBagConfigPatch));
                Patch(harmony, typeof(PlayerBagInitPatch));
                Patch(harmony, typeof(LeadingRoleCreationPatch));
                Patch(harmony, typeof(TravelTimePatch));
                // Bag size, burden and time deliberately use the game's own APIs.
                // Their former low-level hooks caused a native CoreCLR access violation.
                AddComponent<TrainerLoop>();
                Log.LogInfo("Safe trainer bridge loaded; all effects initially off.");
            }
            catch (Exception ex)
            {
                Log.LogError("Safe trainer initialization failed: " + ex);
                throw;
            }
        }

        private void Patch(Harmony harmony, Type type)
        {
            try { harmony.PatchAll(type); }
            catch (Exception ex)
            {
                PatchErrors += (PatchErrors.Length == 0 ? "" : ",") + type.Name;
                Log.LogError("Feature patch failed " + type.Name + ": " + ex);
            }
        }

        internal static void ResetRuntime(string reason, bool preservePersistentStorage = false)
        {
            bool preserveStorage = preservePersistentStorage && PersistStorageSettings;
            RestoreDirectEffects(preserveStorage);
            if (!preserveStorage)
            {
                RestoreBagConfig();
                RestoreStorageConfigs();
            }
            MoveMultiplier = 1f;
            TimeMultiplier = 1f;
            SkillMultiplier = 1;
            InfiniteStamina = InfiniteSatiety = InfiniteMorale = false;
            if (!preserveStorage) InfiniteBurden = false;
            InfiniteShelfLife = InfiniteUses = InfiniteDurability = false;
            InfiniteGeneration = InfiniteBattery = InfiniteGeneratorFuel = InfiniteCookingFuel = InstantPlants = false;
            ZeroTravelTime = false;
            if (!preserveStorage)
            {
                InfiniteStorageBurden = false;
                PersistStorageSettings = false;
                BagWidth = BagHeight = 0;
                StorageWidth = StorageHeight = 0;
            }
            BagHookCalls = BurdenHookCalls = 0;
            TimeHookCalls = 0;
            LastTimeBaseDelta = LastTimeEffectiveDelta = 0f;
            PlayerBagPointer = IntPtr.Zero;
            PlayerOwnerId = 0;
            PlayerBagConfigId = 0;
            StorageConfigHits = 0;
            TravelTimeHookCalls = 0;
            ShelfLifeHookCalls = 0;
            if (!preserveStorage) AppliedExtraBurden = 0;
            MoveHookCalls = 0;
            LastBaseMoveSpeed = 0f;
            LastEffectiveMoveSpeed = 0f;
            LastLeaseUtc = DateTime.MinValue;
            Epoch++;
            LastResult = "运行时效果已重置：" + reason;
        }

        internal static string GetCurrentSaveId()
        {
            try
            {
                var manager = GameSaveManager.Instance;
                if (manager == null) return "";
                var save = manager.GetGameSave();
                string fileName = save == null ? "" : (string)save.FileName;
                if (string.IsNullOrWhiteSpace(fileName)) fileName = (string)manager.GetLastPlayFileName();
                return fileName == null ? "" : fileName.Trim();
            }
            catch (Exception ex)
            {
                Instance?.Log.LogError("Save identity lookup failed: " + ex);
                return "";
            }
        }

        internal static string GetPersistentPath(string saveId)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(saveId));
                var name = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash) name.Append(value.ToString("x2", CultureInfo.InvariantCulture));
                return Path.Combine(PersistentDirectory, name + ".cfg");
            }
        }

        internal static void ApplyInfiniteBurden(LeadingRole leader, bool enabled)
        {
            var playerBag = leader == null ? null : AgentTools.GetAgentComponent<BagComponent>(leader);
            if (playerBag == null) return;
            if (AppliedExtraBurden > 0)
            {
                playerBag.RemoveExtraBurden(AppliedExtraBurden);
                AppliedExtraBurden = 0;
            }
            if (enabled)
            {
                const int targetBurden = 100000000;
                int natural = playerBag.GetMaxBurden();
                AppliedExtraBurden = Math.Max(0, targetBurden - natural);
                if (AppliedExtraBurden > 0) playerBag.AddExtraBurden(AppliedExtraBurden);
            }
            InfiniteBurden = enabled;
        }

        internal static void LoadPersistentSettings(LeadingRole leader, bool force = false, string explicitSaveId = null)
        {
            string saveId = string.IsNullOrWhiteSpace(explicitSaveId) ? GetCurrentSaveId() : explicitSaveId.Trim();
            if (string.IsNullOrEmpty(saveId)) return;
            if (!force && !PendingUnconfiguredGeometryRestore && saveId == CurrentPersistentSaveId) return;

            CurrentPersistentSaveId = saveId;
            CurrentPersistentPath = GetPersistentPath(saveId);
            int bagWidth = 0, bagHeight = 0, storageWidth = 0, storageHeight = 0;
            bool burden = false, storageBurden = false, persist = false;

            try
            {
                if (File.Exists(CurrentPersistentPath))
                {
                    foreach (string rawLine in File.ReadAllLines(CurrentPersistentPath, Encoding.UTF8))
                    {
                        int split = rawLine.IndexOf('=');
                        if (split <= 0) continue;
                        string key = rawLine.Substring(0, split).Trim();
                        string value = rawLine.Substring(split + 1).Trim();
                        int parsed;
                        switch (key)
                        {
                            case "bag_width": if (int.TryParse(value, out parsed)) bagWidth = parsed; break;
                            case "bag_height": if (int.TryParse(value, out parsed)) bagHeight = parsed; break;
                            case "burden": burden = value == "1"; break;
                            case "storage_width": if (int.TryParse(value, out parsed)) storageWidth = parsed; break;
                            case "storage_height": if (int.TryParse(value, out parsed)) storageHeight = parsed; break;
                            case "storage_burden": storageBurden = value == "1"; break;
                            case "persist": persist = value == "1"; break;
                        }
                    }
                }

                PersistStorageSettings = persist;
                if (!persist)
                {
                    // StartBattle runs before item deserialization.  Keep the old,
                    // larger geometry until the new save is fully loaded; shrinking
                    // in this window is what made overflow items drop to the floor.
                    if (leader == null)
                    {
                        PendingUnconfiguredGeometryRestore = true;
                        return;
                    }
                    PendingUnconfiguredGeometryRestore = false;
                    RestoreBagConfig();
                    RestoreStorageConfigs();
                    BagWidth = BagHeight = StorageWidth = StorageHeight = 0;
                    InfiniteBurden = InfiniteStorageBurden = false;
                    AppliedExtraBurden = 0;
                    return;
                }

                PendingUnconfiguredGeometryRestore = false;

                bool validBag = bagWidth >= 1 && bagHeight >= 1 && bagWidth <= 50 && bagHeight <= 50 && (long)bagWidth * bagHeight <= 1000;
                bool validStorage = storageWidth >= 1 && storageHeight >= 1 && storageWidth <= 50 && storageHeight <= 50 && (long)storageWidth * storageHeight <= 1000;
                BagWidth = validBag ? bagWidth : 0;
                BagHeight = validBag ? bagHeight : 0;
                StorageWidth = validStorage ? storageWidth : 0;
                StorageHeight = validStorage ? storageHeight : 0;
                InfiniteStorageBurden = storageBurden;

                if (leader != null && BagWidth > 0 && BagHeight > 0) ApplyBagSize(leader, BagWidth, BagHeight);
                if (leader != null) ApplyInfiniteBurden(leader, burden);
                ReapplyStorageConfigs();
                if (File.Exists(CurrentPersistentPath)) LastResult = "已恢复当前存档的背包与储物设置";
            }
            catch (Exception ex)
            {
                Instance?.Log.LogError("Persistent settings load failed: " + ex);
                LastResult = "当前存档的持久设置读取失败";
            }
        }

        internal static bool SavePersistentSettings()
        {
            string saveId = GetCurrentSaveId();
            if (string.IsNullOrEmpty(saveId)) return false;
            CurrentPersistentSaveId = saveId;
            CurrentPersistentPath = GetPersistentPath(saveId);
            try
            {
                Directory.CreateDirectory(PersistentDirectory);
                string text = "version=1\n" +
                    "save=" + Convert.ToBase64String(Encoding.UTF8.GetBytes(saveId)) + "\n" +
                    "persist=1\n" +
                    "bag_width=" + BagWidth + "\n" +
                    "bag_height=" + BagHeight + "\n" +
                    "burden=" + B(InfiniteBurden) + "\n" +
                    "storage_width=" + StorageWidth + "\n" +
                    "storage_height=" + StorageHeight + "\n" +
                    "storage_burden=" + B(InfiniteStorageBurden) + "\n";
                File.WriteAllText(CurrentPersistentPath, text, new UTF8Encoding(false));
                return true;
            }
            catch (Exception ex)
            {
                Instance?.Log.LogError("Persistent settings save failed: " + ex);
                return false;
            }
        }

        internal static void ClearPersistentSettings()
        {
            try
            {
                string saveId = GetCurrentSaveId();
                string path = string.IsNullOrEmpty(saveId) ? CurrentPersistentPath : GetPersistentPath(saveId);
                if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path);
                CurrentPersistentSaveId = saveId;
                CurrentPersistentPath = string.IsNullOrEmpty(saveId) ? "" : GetPersistentPath(saveId);
                PersistStorageSettings = false;
            }
            catch (Exception ex) { Instance?.Log.LogError("Persistent settings clear failed: " + ex); }
        }

        internal static void RestoreDirectEffects(bool preservePersistentBurden = false)
        {
            if (!preservePersistentBurden)
            {
                try
                {
                    var leader = GetLeader();
                    var bag = leader == null ? null : AgentTools.GetAgentComponent<BagComponent>(leader);
                    if (bag != null && AppliedExtraBurden > 0) bag.RemoveExtraBurden(AppliedExtraBurden);
                }
                catch (Exception ex) { Instance?.Log.LogError("Burden restore failed: " + ex); }
                AppliedExtraBurden = 0;
            }
            try
            {
                if (BattleLogicWorld.IsInstanceCreated && BattleLogicWorld.Instance?._GameTimeManager != null)
                {
                    var time = BattleLogicWorld.Instance._GameTimeManager;
                    if (TimeMultiplier != 1f) time.GmSetCustomGameSpeed(1f);
                }
            }
            catch (Exception ex) { Instance?.Log.LogError("Time restore failed: " + ex); }
        }

        internal static bool IsPlayerBagConfig(Config_Bag config)
        {
            if (config == null) return false;
            string name = (((string)config.Name ?? "") + " " + ((string)config.Name_Local ?? "")).ToLowerInvariant();
            return name.Contains("背包") || name.Contains("backpack") || name.Contains("player bag");
        }

        internal static void ApplyPrimedPlayerBagConfig(Config_Bag config)
        {
            if (!PersistStorageSettings || BagWidth <= 0 || BagHeight <= 0 || config == null ||
                config.Size == null || config.Size.Count < 2 || !IsPlayerBagConfig(config)) return;
            ApplyKnownPlayerBagConfig(config);
        }

        internal static void ApplyKnownPlayerBagConfig(Config_Bag config)
        {
            if (!PersistStorageSettings || BagWidth <= 0 || BagHeight <= 0 || config == null ||
                config.Size == null || config.Size.Count < 2) return;
            if (ActiveBagConfig == null || ActiveBagConfig.Pointer != config.Pointer)
            {
                ActiveBagConfig = config;
                OriginalBagWidth = config.Size[0];
                OriginalBagHeight = config.Size[1];
            }
            config.Size[0] = Math.Max(BagWidth, OriginalBagWidth);
            config.Size[1] = Math.Max(BagHeight, OriginalBagHeight);
        }

        internal static void RestoreBagConfig()
        {
            try
            {
                if (ActiveBagConfig != null && ActiveBagConfig.Size != null && ActiveBagConfig.Size.Count >= 2)
                {
                    ActiveBagConfig.Size[0] = OriginalBagWidth;
                    ActiveBagConfig.Size[1] = OriginalBagHeight;
                }
            }
            catch (Exception ex) { Instance?.Log.LogError("Bag size restore failed: " + ex); }
            ActiveBagConfig = null;
            OriginalBagWidth = OriginalBagHeight = 0;
        }

        internal static bool ApplyBagSize(LeadingRole leader, int width, int height)
        {
            var bag = AgentTools.GetAgentComponent<BagComponent>(leader);
            if (bag == null) return false;
            int configId = bag.GetBagConfigId();
            if (configId <= 0 || ConfigManager.Instance == null) return false;
            var config = ConfigManager.Instance.Get_Config_Bag(configId);
            if (config == null || config.Size == null || config.Size.Count < 2) return false;
            if (ActiveBagConfig == null || ActiveBagConfig.Pointer != config.Pointer)
            {
                RestoreBagConfig();
                ActiveBagConfig = config;
                OriginalBagWidth = config.Size[0];
                OriginalBagHeight = config.Size[1];
            }
            config.Size[0] = Math.Max(width, OriginalBagWidth);
            config.Size[1] = Math.Max(height, OriginalBagHeight);
            BagWidth = config.Size[0]; BagHeight = config.Size[1];
            return true;
        }

        internal static bool IsStorageConfig(Config_Bag config)
        {
            if (config == null || config.Pointer == IntPtr.Zero || config.Size == null || config.Size.Count < 2) return false;
            if (PlayerBagConfigId > 0 && config.ID == PlayerBagConfigId) return false;
            string name = ((string)config.Name ?? "") + " " + ((string)config.Name_Local ?? "");
            string lower = name.ToLowerInvariant();
            if (lower.Contains("背包") || lower.Contains("backpack") || lower.Contains("player bag")) return false;
            string[] keywords = { "收纳", "置物", "储物", "冰箱", "冰柜", "冷柜", "货架", "柜子", "柜", "shelf", "rack", "fridge", "freezer", "refrigerator", "cabinet", "storage", "chest", "locker", "cooler" };
            foreach (string keyword in keywords) if (lower.Contains(keyword)) return true;
            return false;
        }

        internal static void ApplyStorageConfig(Config_Bag config)
        {
            if (config == null) return;
            if ((StorageWidth <= 0 || StorageHeight <= 0) && !InfiniteStorageBurden && !StorageOriginals.ContainsKey(config.Pointer)) return;
            if (!IsStorageConfig(config)) return;
            try
            {
                StorageOriginal original;
                if (!StorageOriginals.TryGetValue(config.Pointer, out original))
                {
                    original = new StorageOriginal { Config = config, Width = config.Size[0], Height = config.Size[1], Burden = config.Burden };
                    StorageOriginals[config.Pointer] = original;
                }
                if (StorageWidth > 0 && StorageHeight > 0)
                {
                    config.Size[0] = Math.Max(StorageWidth, original.Width);
                    config.Size[1] = Math.Max(StorageHeight, original.Height);
                }
                else
                {
                    config.Size[0] = original.Width;
                    config.Size[1] = original.Height;
                }
                config.Burden = InfiniteStorageBurden ? 100000000 : original.Burden;
                StorageConfigHits++;
            }
            catch (Exception ex) { Instance?.Log.LogError("Storage config apply failed: " + ex); }
        }

        internal static void ReapplyStorageConfigs()
        {
            foreach (var entry in new List<StorageOriginal>(StorageOriginals.Values)) ApplyStorageConfig(entry.Config);
        }

        internal static void RestoreStorageConfigs()
        {
            foreach (StorageOriginal original in new List<StorageOriginal>(StorageOriginals.Values))
            {
                try
                {
                    if (original.Config != null && original.Config.Size != null && original.Config.Size.Count >= 2)
                    {
                        original.Config.Size[0] = original.Width;
                        original.Config.Size[1] = original.Height;
                        original.Config.Burden = original.Burden;
                    }
                }
                catch { }
            }
            StorageOriginals.Clear();
        }

        internal static bool IsPlayerBag(BagComponent instance)
            => PlayerBagPointer != IntPtr.Zero && PlayerBagPointer == instance.Pointer;

        internal static LeadingRole GetLeader()
        {
            if (!BattleLogicWorld.IsInstanceCreated) return null;
            var world = BattleLogicWorld.Instance;
            return world?._AgentManager?.GetLeadingRole();
        }

        internal static AttributeComponent GetPlayerAttribute()
        {
            var leader = GetLeader();
            return leader == null ? null : AgentTools.GetAgentComponent<AttributeComponent>(leader);
        }

        internal static void Tick()
        {
            DateTime now = DateTime.UtcNow;
            if (now < nextTickUtc) return;
            nextTickUtc = now.AddMilliseconds(250);
            try
            {
                var leader = GetLeader();
                IntPtr pointer = leader == null ? IntPtr.Zero : leader.Pointer;
                bool leaderChanged = pointer != LastLeaderPointer;
                if (leaderChanged)
                {
                    // Ordinary map changes also replace the LeadingRole object.
                    // Fixed container geometry must never shrink in that gap.
                    ResetRuntime("角色或场景切换", true);
                    AppliedExtraBurden = 0; // belonged to the previous bag instance
                    LastLeaderPointer = pointer;
                }
                if (leader != null)
                {
                    var bag = AgentTools.GetAgentComponent<BagComponent>(leader);
                    PlayerBagPointer = bag == null ? IntPtr.Zero : bag.Pointer;
                    PlayerBagConfigId = bag == null ? 0 : bag.GetBagConfigId();
                    PlayerOwnerId = BattleLogicWorld.Instance._AgentManager.GetLeadingRoleId();
                    LoadPersistentSettings(leader, leaderChanged);
                }
                if (HasLeaseBoundEffect() && now - LastLeaseUtc > TimeSpan.FromSeconds(6))
                {
                    ResetRuntime("面板已断开", true);
                    if (leader != null) LoadPersistentSettings(leader, true);
                }
                try { HandleCommand(now); }
                catch (IOException) { /* Panel is replacing the command file; retry next tick. */ }
                try { WriteStatus(leader); }
                catch (IOException) { /* Panel is reading status; retry next tick. */ }
            }
            catch (Exception ex)
            {
                LastResult = "后台错误：" + ex.GetType().Name;
                Instance.Log.LogError(ex);
            }
        }

        internal static bool HasLeaseBoundEffect() => MoveMultiplier != 1f || TimeMultiplier != 1f || SkillMultiplier != 1 ||
            InfiniteStamina || InfiniteSatiety || InfiniteMorale || InfiniteShelfLife || InfiniteUses ||
            InfiniteDurability || InfiniteGeneration || InfiniteBattery || InfiniteGeneratorFuel || InfiniteCookingFuel ||
            InstantPlants || ZeroTravelTime || (!PersistStorageSettings &&
            (InfiniteBurden || BagWidth > 0 || StorageWidth > 0 || StorageHeight > 0 || InfiniteStorageBurden));

        private static void HandleCommand(DateTime now)
        {
            if (!File.Exists(CommandPath)) return;
            string command;
            using (var stream = new FileStream(CommandPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
                command = reader.ReadToEnd();
            string[] parts = command.Trim().Split('|');
            if (parts.Length != 5 || parts[0] != Session) return;
            if (!int.TryParse(parts[1], out int epoch) || epoch != Epoch) return;
            if (!long.TryParse(parts[2], out long sequence) || sequence <= LastSequence) return;
            LastSequence = sequence;
            LastLeaseUtc = now;
            string verb = parts[3];
            if (verb == "PING") return;
            if (verb == "RESET")
            {
                var resetLeader = GetLeader();
                ResetRuntime("手动还原临时效果", true);
                if (resetLeader != null) LoadPersistentSettings(resetLeader, true);
                return;
            }

            if (!BattleLogicWorld.IsInstanceCreated) { LastResult = "未进入存档"; return; }
            var world = BattleLogicWorld.Instance;
            var leader = world?._AgentManager?.GetLeadingRole();
            if (leader == null) { LastResult = "未进入存档"; return; }
            int amount;
            switch (verb)
            {
                case "ADD_GOLD":
                    if (!int.TryParse(parts[4], out amount) || amount != 100000) { LastResult = "金币参数无效"; return; }
                    var user = AgentTools.GetAgentComponent<UserComponent>(leader);
                    if (user == null || user.Money > int.MaxValue - amount) { LastResult = "金币已达上限或角色未就绪"; return; }
                    int beforeGold = user.Money;
                    leader.AddGold(amount);
                    LastResult = user.Money == beforeGold + amount ? "金币 +100000 已生效" : "金币调用后未观察到预期变化";
                    break;
                case "ADD_FAITH":
                    if (!int.TryParse(parts[4], out amount) || amount != 6) { LastResult = "信念参数无效"; return; }
                    var faith = world._FaithManager;
                    if (faith == null || faith.GetFaithPoint() > int.MaxValue - amount) { LastResult = "信念已达上限或存档未就绪"; return; }
                    int beforeFaith = faith.GetFaithPoint();
                    bool accepted = faith.AddFaithPoint(amount, "SafeTrainer");
                    LastResult = accepted && faith.GetFaithPoint() == beforeFaith + amount ? "信念 +6 已生效" : "信念调用后未观察到预期变化";
                    break;
                case "ADD_SURVIVAL":
                    if (!int.TryParse(parts[4], out amount) || amount != 100) { LastResult = "生存点参数无效"; return; }
                    var survival = world._SurvivalResultsManager;
                    if (survival == null || survival.GetSurvivalPoint() > int.MaxValue - amount) { LastResult = "生存点已达上限或存档未就绪"; return; }
                    int beforeSurvival = survival.GetSurvivalPoint();
                    survival.AddSurvivalPoint(amount);
                    LastResult = survival.GetSurvivalPoint() == beforeSurvival + amount ? "生存点 +100 已生效" : "生存点调用后未观察到预期变化";
                    break;
                case "RECOVER_NEARBY_DROPS":
                    LastResult = RecoverNearbyDrops(world, leader, 6f);
                    break;
                case "START_BREW":
                    // Use the same Redux action as the brewing page's native Start
                    // button.  The game keeps the currently opened barrel in
                    // State_Web_Brew and performs its normal recipe/status checks.
                    Ac_Brew_Start.SendAction();
                    LastResult = "已向当前打开的酿酒桶发送开始酿酒命令";
                    break;
                case "MOVE":
                    if (!float.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float multiplier) ||
                        multiplier < 1f || multiplier > 20f) { LastResult = "移速倍率需在 1–20 之间"; return; }
                    if (GetPlayerAttribute() == null) { LastResult = "角色属性未就绪"; return; }
                    MoveMultiplier = multiplier;
                    MoveHookCalls = 0;
                    LastBaseMoveSpeed = 0f;
                    LastEffectiveMoveSpeed = 0f;
                    LastResult = "移速设为 " + multiplier.ToString("0.##", CultureInfo.InvariantCulture) + "x（仅当前存档运行时）";
                    break;
                case "TIME":
                    if (!float.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float timeScale) || timeScale < 0.25f || timeScale > 20f) { LastResult = "时间倍率需在 0.25–20 之间"; return; }
                    if (world._GameTimeManager == null) { LastResult = "时间管理器未就绪"; return; }
                    world._GameTimeManager.GmSetCustomGameSpeed(timeScale);
                    TimeMultiplier = timeScale;
                    TimeHookCalls = 0;
                    LastResult = "时间流逝设为 " + timeScale.ToString("0.##", CultureInfo.InvariantCulture) + "x（游戏原生接口）";
                    break;
                case "ADD_TIME":
                    if (!int.TryParse(parts[4], out amount) || (amount != 3600 && amount != -3600)) { LastResult = "时间调整参数无效"; return; }
                    if (world._GameTimeManager == null) { LastResult = "时间管理器未就绪"; return; }
                    LastResult = AdjustOneHour(world._GameTimeManager, amount > 0 ? 1 : -1);
                    break;
                case "SKILL_MULT":
                    if (!int.TryParse(parts[4], out amount) || amount < 1 || amount > 20) { LastResult = "技能倍率需在 1–20 之间"; return; }
                    SkillMultiplier = amount;
                    LastResult = "技能熟练度获取设为 " + amount + "x";
                    break;
                case "BAG_SIZE":
                    var dimensions = parts[4].ToLowerInvariant().Split('x');
                    if (dimensions.Length != 2 || !int.TryParse(dimensions[0], out int width) || !int.TryParse(dimensions[1], out int height) ||
                        width < 1 || height < 1 || width > 50 || height > 50 || (long)width * height > 1000)
                    { LastResult = "背包尺寸需为正整数，且不超过 50×50、总格数 1000"; return; }
                    if (!ApplyBagSize(leader, width, height)) { LastResult = "背包配置未就绪，尺寸未更改"; return; }
                    BagHookCalls = 0;
                    if (PersistStorageSettings) SavePersistentSettings();
                    LastResult = "背包尺寸设为 " + BagWidth + "×" + BagHeight +
                        (PersistStorageSettings ? "；已固定到当前存档" : "；未开启固定") +
                        "；还原前请先移走超出原尺寸的物品";
                    break;
                case "STORAGE_SIZE":
                    var storageDimensions = parts[4].ToLowerInvariant().Split('x');
                    if (storageDimensions.Length != 2 || !int.TryParse(storageDimensions[0], out int storageWidth) || !int.TryParse(storageDimensions[1], out int storageHeight) ||
                        storageWidth < 1 || storageHeight < 1 || storageWidth > 50 || storageHeight > 50 || (long)storageWidth * storageHeight > 1000)
                    { LastResult = "储物尺寸需为正整数，且不超过 50×50、总格数 1000"; return; }
                    StorageWidth = storageWidth;
                    StorageHeight = storageHeight;
                    ReapplyStorageConfigs();
                    if (PersistStorageSettings) SavePersistentSettings();
                    LastResult = "储物工具尺寸设为 " + StorageWidth + "×" + StorageHeight +
                        (PersistStorageSettings ? "；已固定到当前存档" : "；未开启固定") + "；打开或重新打开容器后应用";
                    break;
                case "RESET_STORAGE":
                    RestoreStorageConfigs();
                    StorageWidth = StorageHeight = 0;
                    InfiniteStorageBurden = false;
                    if (PersistStorageSettings) SavePersistentSettings();
                    LastResult = "储物工具尺寸和负重已还原";
                    break;
                case "TOGGLE":
                    bool enabled;
                    if (parts[4].Length < 3 || (parts[4][0] != '0' && parts[4][0] != '1') || parts[4][1] != ':') { LastResult = "开关参数无效"; return; }
                    enabled = parts[4][0] == '1';
                    switch (parts[4].Substring(2))
                    {
                        case "stamina": InfiniteStamina = enabled; break;
                        case "satiety": InfiniteSatiety = enabled; break;
                        case "morale": InfiniteMorale = enabled; break;
                        case "burden":
                            if (AgentTools.GetAgentComponent<BagComponent>(leader) == null) { LastResult = "背包组件未就绪"; return; }
                            ApplyInfiniteBurden(leader, enabled);
                            if (PersistStorageSettings) SavePersistentSettings();
                            break;
                        case "shelf":
                            if (world._ItemManager == null || world._GameTimeManager == null) { LastResult = "物品或时间管理器未就绪"; return; }
                            if (enabled)
                            {
                                InfiniteShelfLife = true;
                                ShelfLifeHookCalls = 0;
                                LastResult = EnablePermanentShelfLife(world._ItemManager, world._GameTimeManager.GetTotalSeconds() / 3600);
                            }
                            else
                            {
                                InfiniteShelfLife = false;
                                world._ItemManager.RefreshAllItemTimeScale();
                                LastResult = "无限保质期已关闭；储存环境倍率已恢复";
                            }
                            return;
                        case "uses": InfiniteUses = enabled; break;
                        case "durability": InfiniteDurability = enabled; break;
                        case "generation": InfiniteGeneration = enabled; break;
                        case "battery": InfiniteBattery = enabled; break;
                        case "generatorfuel": InfiniteGeneratorFuel = enabled; break;
                        case "cookingfuel": InfiniteCookingFuel = enabled; break;
                        case "plants": InstantPlants = enabled; break;
                        case "storageburden":
                            InfiniteStorageBurden = enabled;
                            ReapplyStorageConfigs();
                            if (PersistStorageSettings) SavePersistentSettings();
                            break;
                        case "persiststorage":
                            if (enabled)
                            {
                                PersistStorageSettings = true;
                                if (!SavePersistentSettings()) { PersistStorageSettings = false; LastResult = "当前存档标识未就绪，暂时无法固定"; return; }
                                LastResult = "已固定当前存档的背包与储物设置";
                            }
                            else
                            {
                                ClearPersistentSettings();
                                LastResult = "已取消固定；当前数值保持到本次游戏结束";
                            }
                            return;
                        case "zerotravel": ZeroTravelTime = enabled; TravelTimeHookCalls = 0; break;
                        default: LastResult = "未知开关"; return;
                    }
                    LastResult = parts[4].Substring(2) + (enabled ? " 已开启" : " 已关闭");
                    break;
                case "MAX_SKILLS":
                    if (world._ProficiencyManager == null) { LastResult = "技能管理器未就绪"; return; }
                    for (int i = 1; i <= 6; i++)
                    {
                        var skill = (ProficiencyType)i;
                        int level = world._ProficiencyManager.GetLevel(skill);
                        if (level < 5) world._ProficiencyManager.AddLevel(skill, 5 - level, false);
                    }
                    LastResult = "六项技能已尝试提升至 5 级（写入当前存档）";
                    break;
                case "UNLOCK_CODEX":
                    if (world._CodexManager == null) { LastResult = "图鉴管理器未就绪"; return; }
                    world._CodexManager.GmUnlockAll();
                    LastResult = "已调用全部图鉴解锁（可能影响全局记录）";
                    break;
                case "UNLOCK_ACHIEVEMENTS":
                    if (world._AchievementManager == null) { LastResult = "成就管理器未就绪"; return; }
                    LastResult = "成就解锁返回：" + world._AchievementManager.GmUnlockAll();
                    break;
                default:
                    LastResult = "未知命令";
                    break;
            }
        }

        internal static string RecoverNearbyDrops(BattleLogicWorld world, LeadingRole leader, float radius)
        {
            if (world == null || leader == null || world._DropItemManager == null) return "地面物品管理器未就绪";
            var transform = AgentTools.GetAgentComponent<TransformComponent>(leader);
            if (transform == null) return "角色位置未就绪";

            Vector3 center = transform.Position;
            var drops = new Il2CppSystem.Collections.Generic.List<DropItem>();
            world._DropItemManager.GetNearbyDropItems(center, radius, drops);
            if (drops.Count == 0) return "附近没有可取回的地面物品";

            var candidates = new List<KeyValuePair<float, long>>();
            for (int i = 0; i < drops.Count; i++)
            {
                DropItem candidate = drops[i];
                if (candidate == null || !candidate.HasItem) continue;
                Vector3 offset = candidate.Position - center;
                float squared = offset.sqrMagnitude;
                if (squared <= radius * radius)
                    candidates.Add(new KeyValuePair<float, long>(squared, candidate.InstanceId));
            }

            if (candidates.Count == 0) return "6 米内没有可取回的地面物品；请站到家具旁再试";
            candidates.Sort((left, right) => left.Key.CompareTo(right.Key));

            int picked = 0, failed = 0;
            foreach (var candidate in candidates)
            {
                // The native pickup path removes the ground model and writes the
                // item into the current save.  A failed item is skipped so a large
                // stack cannot prevent smaller nearby items from filling free cells.
                if (world._DropItemManager.TryPickupItem(candidate.Value, false)) picked++;
                else failed++;
            }

            if (picked == 0) return "未取回物品：背包已满、格子不合适或物品暂不可拾取";
            if (failed == 0) return "已取回附近全部 " + picked + " 件地面物品";
            return "已取回 " + picked + " 件；另有 " + failed + " 件因背包空间或格子不足而保留在地面";
        }

        internal static void FreshenPerishable(ItemManager manager, ItemData item, Config_Item config, int currentHour)
        {
            if (manager == null || item == null || config == null || config.Life <= 0) return;
            int fullLifeHours = config.Life > int.MaxValue / 24 ? int.MaxValue : config.Life * 24;
            item.StartTime = currentHour;
            item.TimeLeft = fullLifeHours;
            item.OriginalTimeLeft = fullLifeHours;
            item.TimeLeftFrac = 0f;
            manager.RefreshItemTimeScale(item);
        }

        internal static string EnablePermanentShelfLife(ItemManager manager, int currentHour)
        {
            if (manager == null || ConfigManager.Instance == null) return "物品配置尚未就绪";
            manager.RefreshAllItemTimeScale();

            var all = ConfigManager.Instance.Get_Config_Item_All();
            if (all == null) return "物品配置表尚未就绪";
            var perishable = new List<Config_Item>();
            var reverse = new Dictionary<int, int>();
            var ambiguous = new HashSet<int>();

            foreach (var pair in all)
            {
                Config_Item config = pair.Value;
                if (config == null || config.Life <= 0) continue;
                perishable.Add(config);
                if (config.It_Rot_Product_Id == null) continue;
                for (int i = 0; i < config.It_Rot_Product_Id.Count; i++)
                {
                    int productId = config.It_Rot_Product_Id[i];
                    if (productId <= 0 || productId == config.ID) continue;
                    int existing;
                    if (reverse.TryGetValue(productId, out existing) && existing != config.ID)
                        ambiguous.Add(productId);
                    else
                        reverse[productId] = config.ID;
                }
            }
            foreach (int productId in ambiguous) reverse.Remove(productId);

            int freshened = 0, restored = 0, skippedAmbiguous = 0;
            var refreshedOwners = new HashSet<long>();
            foreach (Config_Item config in perishable)
            {
                var items = manager.GetItemsByConfigId(config.ID);
                if (items == null) continue;
                for (int i = 0; i < items.Count; i++)
                {
                    ItemData item = items[i];
                    if (item == null) continue;
                    FreshenPerishable(manager, item, config, currentHour);
                    refreshedOwners.Add(item.OwnerId);
                    freshened++;
                }
            }

            foreach (var mapping in reverse)
            {
                Config_Item rottenConfig = ConfigManager.Instance.Get_Config_Item(mapping.Key);
                Config_Item originalConfig = ConfigManager.Instance.Get_Config_Item(mapping.Value);
                if (originalConfig == null || originalConfig.Life <= 0) continue;
                // If the target is itself a normal perishable item, it may be a
                // legitimate ingredient rather than a converted rotten product.
                if (rottenConfig != null && rottenConfig.Life > 0) continue;
                var rottenItems = manager.GetItemsByConfigId(mapping.Key);
                if (rottenItems == null || rottenItems.Count == 0) continue;
                var snapshot = new List<ItemData>();
                for (int i = 0; i < rottenItems.Count; i++) if (rottenItems[i] != null) snapshot.Add(rottenItems[i]);
                foreach (ItemData item in snapshot)
                {
                    item.ItemConfigId = originalConfig.ID;
                    FreshenPerishable(manager, item, originalConfig, currentHour);
                    refreshedOwners.Add(item.OwnerId);
                    restored++;
                }
            }

            foreach (int productId in ambiguous)
            {
                var items = manager.GetItemsByConfigId(productId);
                if (items != null) skippedAmbiguous += items.Count;
            }
            foreach (long ownerId in refreshedOwners) manager.RefreshFurnitureBagSlotState(ownerId);

            return "永久保鲜已开启：刷新 " + freshened + " 件；安全还原腐坏物 " + restored + " 件" +
                (skippedAmbiguous > 0 ? "；另有 " + skippedAmbiguous + " 件来源不唯一，未自动还原" : "");
        }

        internal static string AdjustOneHour(GameTimeManager timeManager, int direction)
        {
            if (timeManager == null || timeManager.Timer == null) return "时间计时器未就绪";

            const int hourSeconds = 3600;
            int beforeTotal = timeManager.GetTotalSeconds();
            var timer = timeManager.Timer;
            var countDown = timer.TryCast<CountDownTimer>();

            // CountDownTimer.GetTime reports elapsed display seconds.  Extending the
            // countdown is the game's supported way to move backwards past its
            // original upper limit; it updates both remaining and total time.
            if (countDown != null)
            {
                if (direction < 0)
                {
                    float beforeRemain = timeManager.GetRemainHourFloat();
                    timeManager.AddExtraCountDownTime(hourSeconds);
                    float afterRemain = timeManager.GetRemainHourFloat();
                    return afterRemain >= beforeRemain + 0.99f
                        ? "囤货剩余时间 +1 小时已生效"
                        : "倒计时未接受增加 1 小时";
                }

                float internalHour = Toolset.GetGameTime_Float(hourSeconds);
                if (!(internalHour > 0f)) return "游戏时间换算失败";
                timeManager.CostTime(internalHour, false);
                int afterTotal = timeManager.GetTotalSeconds();
                return afterTotal >= beforeTotal + hourSeconds - 2
                    ? "囤货剩余时间 -1 小时已生效"
                    : "倒计时未接受减少 1 小时";
            }

            // CountUpTimer.SetTime takes display seconds and performs the game's
            // own internal conversion.  It is required for a precise backwards
            // step and for borrowing one hour across a day boundary.
            var countUp = timer.TryCast<CountUpTimer>();
            if (countUp != null)
            {
                long targetTotal = (long)beforeTotal + direction * (long)hourSeconds;
                if (targetTotal < 0) return "已到游戏时间起点，不能继续回拨";

                if (direction > 0)
                {
                    float internalHour = Toolset.GetGameTime_Float(hourSeconds);
                    if (!(internalHour > 0f)) return "游戏时间换算失败";
                    timeManager.CostTime(internalHour, false);
                }
                else
                {
                    int targetDay = (int)(targetTotal / 86400L);
                    int targetTime = (int)(targetTotal % 86400L);
                    countUp.SetTime(targetTime, targetDay);
                }

                int afterTotal = timeManager.GetTotalSeconds();
                long expected = targetTotal;
                return Math.Abs((long)afterTotal - expected) <= 2L
                    ? (direction > 0
                        ? "正式阶段时间 +1 小时已生效"
                        : "正式阶段时间 -1 小时已生效；已发生的事件不会回滚")
                    : "正式阶段计时器未接受此次调整";
            }

            return "当前游戏阶段不支持调整时间";
        }

        private static void WriteStatus(LeadingRole leader)
        {
            int gold = -1, faith = -1, survival = -1;
            int bagWeight = -1, bagBurden = -1, gameSeconds = -1;
            if (leader != null)
            {
                var user = AgentTools.GetAgentComponent<UserComponent>(leader);
                if (user != null) gold = user.Money;
                var bag = AgentTools.GetAgentComponent<BagComponent>(leader);
                if (bag != null) { bagWeight = bag.GetBagWeight(); bagBurden = bag.GetMaxBurden(); }
                var world = BattleLogicWorld.Instance;
                if (world?._GameTimeManager != null)
                {
                    gameSeconds = world._GameTimeManager.GetTotalSeconds();
                }
                if (world?._FaithManager != null) faith = world._FaithManager.GetFaithPoint();
                if (world?._SurvivalResultsManager != null) survival = world._SurvivalResultsManager.GetSurvivalPoint();
            }
            string status = "session=" + Session + "\n" +
                "pid=" + Process.GetCurrentProcess().Id + "\n" +
                "epoch=" + Epoch + "\n" +
                "ready=" + (leader != null ? "1" : "0") + "\n" +
                "gold=" + gold + "\nfaith=" + faith + "\nsurvival=" + survival + "\n" +
                "move=" + MoveMultiplier.ToString("0.##", CultureInfo.InvariantCulture) + "\n" +
                "time=" + TimeMultiplier.ToString("0.##", CultureInfo.InvariantCulture) + "\n" +
                "zerotravel=" + B(ZeroTravelTime) + "\ntravel_time_calls=" + TravelTimeHookCalls + "\n" +
                "skill_mult=" + SkillMultiplier + "\n" +
                "bag_size=" + BagWidth + "x" + BagHeight + "\n" +
                "bag_original=" + OriginalBagWidth + "x" + OriginalBagHeight + "\n" +
                "storage_size=" + StorageWidth + "x" + StorageHeight + "\n" +
                "persiststorage=" + B(PersistStorageSettings) + "\n" +
                "storage_configs=" + StorageOriginals.Count + "\n" +
                "storage_hits=" + StorageConfigHits + "\n" +
                "bag_hook_calls=" + BagHookCalls + "\nburden_hook_calls=" + BurdenHookCalls + "\n" +
                "bag_weight=" + bagWeight + "\nbag_burden=" + bagBurden + "\n" +
                "game_seconds=" + gameSeconds + "\n" +
                "time_hook_calls=" + TimeHookCalls + "\ntime_base_delta=" + LastTimeBaseDelta.ToString("0.###", CultureInfo.InvariantCulture) +
                "\ntime_effective_delta=" + LastTimeEffectiveDelta.ToString("0.###", CultureInfo.InvariantCulture) + "\n" +
                "patch_errors=" + PatchErrors + "\n" +
                "stamina=" + B(InfiniteStamina) + "\nsatiety=" + B(InfiniteSatiety) + "\nmorale=" + B(InfiniteMorale) + "\nburden=" + B(InfiniteBurden) + "\n" +
                "shelf=" + B(InfiniteShelfLife) + "\nshelf_hook_calls=" + ShelfLifeHookCalls + "\nuses=" + B(InfiniteUses) + "\ndurability=" + B(InfiniteDurability) + "\n" +
                "generation=" + B(InfiniteGeneration) + "\nbattery=" + B(InfiniteBattery) + "\ngeneratorfuel=" + B(InfiniteGeneratorFuel) + "\ncookingfuel=" + B(InfiniteCookingFuel) + "\nplants=" + B(InstantPlants) + "\n" +
                "storageburden=" + B(InfiniteStorageBurden) + "\n" +
                "move_hook_calls=" + MoveHookCalls + "\n" +
                "move_base=" + LastBaseMoveSpeed.ToString("0.###", CultureInfo.InvariantCulture) + "\n" +
                "move_effective=" + LastEffectiveMoveSpeed.ToString("0.###", CultureInfo.InvariantCulture) + "\n" +
                "sequence=" + LastSequence + "\nresult=" + LastResult.Replace("\n", " ") + "\n" +
                "utc=" + DateTime.UtcNow.Ticks + "\n";
            string temporaryPath = StatusPath + ".new";
            File.WriteAllText(temporaryPath, status, new UTF8Encoding(false));
            if (File.Exists(StatusPath)) File.Replace(temporaryPath, StatusPath, null);
            else File.Move(temporaryPath, StatusPath);
        }
        private static string B(bool value) => value ? "1" : "0";
    }

    public sealed class TrainerLoop : MonoBehaviour
    {
        public TrainerLoop(IntPtr pointer) : base(pointer) { }
        private void Update() { Plugin.Tick(); }
    }

    [HarmonyPatch(typeof(AttributeComponent), "GetTotalValue_Float", new[] { typeof(AttrName) })]
    internal static class MoveSpeedPatch
    {
        private static void Postfix(AttributeComponent __instance, AttrName attrName, ref float __result)
        {
            if (attrName != AttrName.MoveSpeed || Plugin.MoveMultiplier == 1f) return;
            try
            {
                var playerAttr = Plugin.GetPlayerAttribute();
                if (playerAttr != null && playerAttr.Pointer == __instance.Pointer)
                {
                    Plugin.LastBaseMoveSpeed = __result;
                    __result *= Plugin.MoveMultiplier;
                    Plugin.LastEffectiveMoveSpeed = __result;
                    Plugin.MoveHookCalls++;
                }
            }
            catch { }
        }
    }

    [HarmonyPatch]
    internal static class SaveBoundaryPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(AgentManager), "StartBattle")]
        private static void OnStartBattle(GameSaveData gameSave)
        {
            Plugin.ResetRuntime("进入场景", true);
            string saveId = gameSave == null ? "" : (string)gameSave.FileName;
            Plugin.LoadPersistentSettings(null, true, saveId);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(AgentManager), "EndBattle")]
        private static void OnEndBattle()
        {
            // EndBattle may still save/transfer items after this prefix.  Shrinking
            // here would classify expanded cells as overflow.
            Plugin.ResetRuntime("离开场景", true);
        }
    }

    [HarmonyPatch(typeof(AttributeComponent), "GetTotalValue_Int", new[] { typeof(AttrName) })]
    internal static class PlayerAttributePatch
    {
        private static void Postfix(AttributeComponent __instance, AttrName attrName, ref int __result)
        {
            try
            {
                var player = Plugin.GetPlayerAttribute();
                if (player == null || player.Pointer != __instance.Pointer) return;
                AttrName max;
                if (attrName == AttrName.Stamina && Plugin.InfiniteStamina) max = AttrName.MaxStamina;
                else if (attrName == AttrName.Satiety && Plugin.InfiniteSatiety) max = AttrName.MaxSatiety;
                else if (attrName == AttrName.Morale && Plugin.InfiniteMorale) max = AttrName.MaxMorale;
                else return;
                __result = Math.Max(__result, __instance.GetTotalValue_Int(max));
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(BagComponent), "GetMaxBurden")]
    internal static class BagBurdenPatch
    {
        private static void Postfix(BagComponent __instance, ref int __result)
        {
            if (!Plugin.InfiniteBurden) return;
            try
            {
                if (Plugin.IsPlayerBag(__instance)) { __result = 100000000; Plugin.BurdenHookCalls++; }
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(BagComponent), "GetTotalBurden")]
    internal static class BagTotalBurdenPatch
    {
        private static void Postfix(BagComponent __instance, ref int __result)
        { try { if (Plugin.InfiniteBurden && Plugin.IsPlayerBag(__instance)) { __result = 100000000; Plugin.BurdenHookCalls++; } } catch { } }
    }

    [HarmonyPatch(typeof(BagComponent), "GetOverweightThreshold")]
    internal static class BagThresholdPatch
    {
        private static void Postfix(BagComponent __instance, ref int __result)
        { try { if (Plugin.InfiniteBurden && Plugin.IsPlayerBag(__instance)) { __result = 100000000; Plugin.BurdenHookCalls++; } } catch { } }
    }

    [HarmonyPatch(typeof(BagComponent), "GetRemainingBurden")]
    internal static class BagRemainingPatch
    {
        private static void Postfix(BagComponent __instance, ref int __result)
        { try { if (Plugin.InfiniteBurden && Plugin.IsPlayerBag(__instance)) { __result = 100000000; Plugin.BurdenHookCalls++; } } catch { } }
    }

    [HarmonyPatch(typeof(BagComponent), "CanAddWeight")]
    internal static class BagCanAddPatch
    {
        private static void Postfix(BagComponent __instance, ref bool __result)
        { try { if (Plugin.InfiniteBurden && Plugin.IsPlayerBag(__instance)) { __result = true; Plugin.BurdenHookCalls++; } } catch { } }
    }

    [HarmonyPatch(typeof(BagComponent), "GetHeavyLoadRatio")]
    internal static class BagHeavyRatioPatch
    {
        private static void Postfix(BagComponent __instance, ref float __result)
        { try { if (Plugin.InfiniteBurden && Plugin.IsPlayerBag(__instance)) { __result = 0f; Plugin.BurdenHookCalls++; } } catch { } }
    }

    [HarmonyPatch(typeof(ItemManager), "CheckAndProcessRot")]
    internal static class ItemRotPatch
    {
        private static bool Prefix(ItemManager __instance, ItemData item, Config_Item config, int currentHour, ref bool __result)
        {
            if (!Plugin.InfiniteShelfLife || item == null || config == null || config.Life <= 0) return true;
            Plugin.FreshenPerishable(__instance, item, config, currentHour);
            __result = false;
            Plugin.ShelfLifeHookCalls++;
            return false;
        }
    }

    [HarmonyPatch(typeof(ItemData), "get_IsInfiniteUse")]
    internal static class ItemUsesPatch
    {
        private static void Postfix(ItemData __instance, ref bool __result)
        { if (Plugin.InfiniteUses && __instance.MaxUseTimes > 0) __result = true; }
    }

    [HarmonyPatch(typeof(FurnitureDurabilityComponent), "TakeDamage")]
    internal static class FurnitureDamagePatch
    {
        private static bool Prefix(int damage, bool isRepair) => !Plugin.InfiniteDurability || damage <= 0 || isRepair;
    }

    [HarmonyPatch(typeof(PowerManager), "CalculateGeneration")]
    internal static class PowerGenerationPatch
    {
        private static void Postfix(ref float __result) { if (Plugin.InfiniteGeneration) __result = Math.Max(__result, 100000f); }
    }

    [HarmonyPatch(typeof(PowerManager), "ConsumeStoredPower")]
    internal static class BatteryPatch
    {
        private static bool Prefix() => !Plugin.InfiniteBattery;
    }

    [HarmonyPatch(typeof(PowerManager), "ConsumeGeneratorFuel")]
    internal static class GeneratorFuelPatch
    {
        private static bool Prefix() => !Plugin.InfiniteGeneratorFuel;
    }

    [HarmonyPatch(typeof(CookingComponent), "UpdateFuelFromProgress")]
    internal static class CookingFuelPatch
    {
        private static void Prefix(ref float fuelRate) { if (Plugin.InfiniteCookingFuel) fuelRate = 0f; }
    }

    [HarmonyPatch(typeof(PlantComponent), "GetActualGrowthTotalSeconds")]
    internal static class PlantGrowthPatch
    {
        private static void Postfix(ref int __result) { if (Plugin.InstantPlants) __result = 1; }
    }

    [HarmonyPatch(typeof(ProficiencyManager), "AddExp", new[] { typeof(ProficiencyType), typeof(string), typeof(int), typeof(int), typeof(int), typeof(ProficiencyExpSource) })]
    internal static class SkillExpPatch
    {
        private static void Prefix(ref int baseExp, ref int discoveryExp)
        {
            if (Plugin.SkillMultiplier == 1) return;
            baseExp = baseExp > int.MaxValue / Plugin.SkillMultiplier ? int.MaxValue : baseExp * Plugin.SkillMultiplier;
            discoveryExp = discoveryExp > int.MaxValue / Plugin.SkillMultiplier ? int.MaxValue : discoveryExp * Plugin.SkillMultiplier;
        }
    }

    [HarmonyPatch(typeof(GameTimeManager), "SaveGame", new[] { typeof(SaveChildData) })]
    internal static class TimeSaveGuardPatch
    {
        private static void Prefix(GameTimeManager __instance, out float __state)
        {
            __state = Plugin.TimeMultiplier;
            try { if (__state != 1f) __instance.GmSetCustomGameSpeed(1f); } catch { }
        }

        private static void Postfix(GameTimeManager __instance, float __state)
        {
            try { if (__state != 1f) __instance.GmSetCustomGameSpeed(__state); } catch { }
        }
    }

    [HarmonyPatch(typeof(ConfigManager), "Get_Config_Bag", new[] { typeof(int) })]
    internal static class StorageBagConfigPatch
    {
        private static void Postfix(Config_Bag __result)
        {
            try
            {
                Plugin.ApplyPrimedPlayerBagConfig(__result);
                Plugin.ApplyStorageConfig(__result);
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(BagComponent), "OnInit", new[] { typeof(int) })]
    internal static class PlayerBagInitPatch
    {
        private static void Postfix(BagComponent __instance, int bagConfigId)
        {
            if (!Plugin.PersistStorageSettings || Plugin.BagWidth <= 0 || Plugin.BagHeight <= 0) return;
            try
            {
                if (!BattleLogicWorld.IsInstanceCreated || BattleLogicWorld.Instance?._AgentManager == null) return;
                long leaderId = BattleLogicWorld.Instance._AgentManager.GetLeadingRoleId();
                if (leaderId == 0 || __instance.ownerInstanceId != leaderId) return;
                Plugin.PlayerBagConfigId = bagConfigId;
                Plugin.ApplyKnownPlayerBagConfig(ConfigManager.Instance?.Get_Config_Bag(bagConfigId));
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(AgentManager), "CreateLeadingRole")]
    internal static class LeadingRoleCreationPatch
    {
        private static void Postfix(LeadingRole __result)
        {
            if (__result == null || !Plugin.PersistStorageSettings || Plugin.BagWidth <= 0 || Plugin.BagHeight <= 0) return;
            try
            {
                var bag = AgentTools.GetAgentComponent<BagComponent>(__result);
                if (bag == null) return;
                int configId = bag.GetBagConfigId();
                Plugin.PlayerBagConfigId = configId;
                Plugin.ApplyKnownPlayerBagConfig(ConfigManager.Instance?.Get_Config_Bag(configId));
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(MapPointRules), "GetTravelTime", new[] { typeof(Config_MapPoint) })]
    internal static class TravelTimePatch
    {
        private static void Postfix(ref int __result)
        {
            if (!Plugin.ZeroTravelTime) return;
            __result = 0;
            Plugin.TravelTimeHookCalls++;
        }
    }

    [HarmonyPatch(typeof(GameTimeManager), "Update", new[] { typeof(float), typeof(float) })]
    internal static class TimeSpeedPatch
    {
        private static void Prefix(ref float deltaTime, ref float unscaledDeltaTime)
        {
            if (Plugin.TimeMultiplier == 1f) return;
            Plugin.LastTimeBaseDelta = unscaledDeltaTime;
            deltaTime *= Plugin.TimeMultiplier;
            unscaledDeltaTime *= Plugin.TimeMultiplier;
            Plugin.LastTimeEffectiveDelta = unscaledDeltaTime;
            Plugin.TimeHookCalls++;
        }
    }

    [HarmonyPatch(typeof(ItemManager), "GetOwnerBagSize")]
    internal static class BagSizePatch
    {
        private static void Postfix(long ownerId, ref Vector2Int __result)
        {
            if (Plugin.BagWidth == 0 || Plugin.BagHeight == 0) return;
            try
            {
                if (Plugin.PlayerOwnerId != 0 && ownerId == Plugin.PlayerOwnerId)
                {
                    __result = new Vector2Int(Math.Max(__result.x, Plugin.BagWidth), Math.Max(__result.y, Plugin.BagHeight));
                    Plugin.BagHookCalls++;
                }
            }
            catch { }
        }
    }
}
