using System;
using System.Collections.Generic;
using GameCore.HotUpdate;
using GameCore.HotUpdate.Battle.Logic;
using HarmonyLib;
using GameCore.HotUpdate.ReduxUI;

namespace SurvivalLogTrainer
{
    public sealed partial class Plugin
    {
        // Runtime-only bag definitions. Native furniture IDs and original configs
        // are never replaced. Dimensions live in the per-save anchor sidecar.
        private static readonly Dictionary<int, Config_Bag> AnchorBags = new Dictionary<int, Config_Bag>();
        private static readonly Dictionary<long, int> AnchorOwners = new Dictionary<long, int>();
        private static readonly Dictionary<string, int> AnchorIds = new Dictionary<string, int>();
        internal static bool IsAnchorBag(int id) { return AnchorBags.ContainsKey(id); }
        internal static bool TryAnchorBag(int id, out Config_Bag bag) { return AnchorBags.TryGetValue(id, out bag); }
        internal static int AnchorBagForOwner(long owner) { int id; return AnchorOwners.TryGetValue(owner, out id) ? id : 0; }
        internal static void ClearAnchorOwners() { AnchorOwners.Clear(); }

        internal static bool ItemLoadUnsafe;

        internal static void RestoreAnchorCapacity(GameSaveData gameSave)
        {
            AnchorOwners.Clear();
            AnchorBags.Clear();
            AnchorIds.Clear();
            if (gameSave == null || string.IsNullOrEmpty(gameSave.FileName)) return;
            string save = gameSave.FileName;
            var records = StorageAnchorRecord.Read(GetPersistentPath(save) + ".anchors", save);
            bool sized = records.Exists(r => r.Width > 0);
            if (!sized) return;
            // Restore existing containers using managed APIs; the creation-only binary
            // guard must never abort the game's entire item-loading pipeline.
            BindAnchorCapacities(records);
            Instance.Log.LogInfo("Restored anchor capacities before item load: " + AnchorOwners.Count);
        }

        private static void BindAnchorCapacities(List<StorageAnchorRecord> records)
        {
            var live = GetNativeAnchors(records);
            for (int i = 0; i < records.Count; i++)
            {
                var r = records[i]; Furniture f;
                if (r.Width == 0 || !live.TryGetValue(r.Id, out f)) continue;
                // Do not renumber surviving points after a deletion in this scene.
                int id;
                if (!AnchorIds.TryGetValue(r.Id, out id))
                { id = 2100000000 + AnchorIds.Count; AnchorIds.Add(r.Id, id); }
                var originalFurniture = ConfigManager.Instance.Get_Config_Furniture(r.FurnitureConfig);
                var original = ConfigManager.Instance.Get_Config_Bag(originalFurniture.BagId);
                var bag = AgentTools.GetAgentComponent<BagComponent>(f);
                if (original == null || bag == null) throw new InvalidOperationException("储物点容量无法恢复，请勿保存游戏");
                if (ConfigManager.Instance._Config_Bag_Dict.ContainsKey(id))
                    throw new InvalidOperationException("独立容量编号冲突，未修改原配置");
                Config_Bag custom;
                if (!AnchorBags.TryGetValue(id, out custom))
                {
                    custom = new Config_Bag { ID = id, Name = "SLT_ANCHOR", Name_Local = r.Label,
                        UseOwnerName = original.UseOwnerName, Burden = original.Burden,
                        Size = new Il2CppSystem.Collections.Generic.List<int>() };
                    custom.Size.Add(r.Width); custom.Size.Add(r.Height);
                    AnchorBags.Add(id, custom);
                }
                else { custom.Size[0] = r.Width; custom.Size[1] = r.Height; }
                AnchorOwners[f.InstanceId] = id;
                bag.SetBagConfigId(id);
            }
        }

        private static void CheckAnchorCapacityReady()
        {
            CheckAnchorBuild();
            if (PatchErrors.Contains("AnchorCapacity") || PatchErrors.Contains("AnchorConfig"))
                throw new InvalidOperationException("独立容量补丁未加载，不能修改格数");
            if (!EnsureInventoryScroll()) throw new InvalidOperationException(LastResult);
        }

        private static string ResizeAnchor(string argument)
        {
            CheckAnchorContext(); CheckAnchorCapacityReady();
            if (ReduxUISystem.IsInstanceCreated && ReduxUISystem.Instance.HasModalUIOpenExcept(null))
                return "请先关闭游戏内背包、储物或其他菜单，再修改尺寸";
            string[] p = argument.Split(','); int w, h;
            if (p.Length != 3 || !int.TryParse(p[1], out w) || !int.TryParse(p[2], out h) || !StorageAnchorRecord.ValidSize(w, h))
                return "宽、高须为 1–50 的整数";
            var records = GetAnchorRecords();
            var record = records.Find(r => r.Id == p[0]); Furniture f;
            if (record == null || !GetNativeAnchors(records).TryGetValue(record.Id, out f)) return "未找到选中的原储物点";
            var currentBag = AgentTools.GetAgentComponent<BagComponent>(f);
            var currentConfig = ConfigManager.Instance.Get_Config_Bag(currentBag.GetBagConfigId());
            if (w < currentConfig.Size[0] || h < currentConfig.Size[1])
            {
                // A live inventory may already be empty while the last game save
                // still contains items. Never shrink over that older snapshot.
                var saved = GameSaveManager.Instance.GetGameSave().CurSave;
                foreach (var chapter in saved.ChapterAgentMap)
                    foreach (var agent in chapter.Value)
                        if (agent != null && agent.Position != null && agent.Position.Length >= 3 &&
                            record.Matches(agent.AgentConfigId, agent.MapConfigId, agent.Position[0], agent.Position[1], agent.Position[2]) &&
                            agent.ItemList != null && agent.ItemList.Count > 0)
                            return "缩小前请清空该储物点并正常保存游戏，再回来修改；避免旧存档物品越界";
            }
            var items = BattleLogicWorld.Instance._ItemManager.Cache;
            foreach (var pair in items)
            {
                var item = pair.Value;
                if (item == null || item.OwnerId != f.InstanceId) continue;
                if (!StorageAnchorRecord.Fits(w, h, item.BagPos.x, item.BagPos.y, item.ItemSize.x, item.ItemSize.y))
                    return "未修改：有物品超出新尺寸，请先移走边缘物品或整理后重试";
            }
            BackupWorldAction("resize-independent-storage");
            record.Width = w; record.Height = h;
            StorageAnchorRecord.Write(GetPersistentPath(GetCurrentSaveId()) + ".anchors", GetCurrentSaveId(), records);
            BindAnchorCapacities(records);
            RefreshAnchors(false);
            return "“" + record.Label + "”已设为 " + w + "×" + h + "；重新打开储物界面生效，尺寸随当前存档保留";
        }

        private static string DeleteAnchor(string id)
        {
            CheckAnchorContext(); CheckAnchorBuild();
            if (ReduxUISystem.IsInstanceCreated && ReduxUISystem.Instance.HasModalUIOpenExcept(null))
                return "请先关闭游戏内背包、储物或其他菜单，再删除储物点";
            var records = GetAnchorRecords();
            var record = records.Find(r => r.Id == id);
            if (record == null) return "未找到选中的储物点";
            // Check the last native save as well as live contents. An unloaded or
            // emptied-but-unsaved container must not lose its recovery index.
            var saved = GameSaveManager.Instance.GetGameSave().CurSave;
            foreach (var chapter in saved.ChapterAgentMap)
                foreach (var agent in chapter.Value)
                    if (agent != null && agent.Position != null && agent.Position.Length >= 3 &&
                        record.Matches(agent.AgentConfigId, agent.MapConfigId, agent.Position[0], agent.Position[1], agent.Position[2]) &&
                        agent.ItemList != null && agent.ItemList.Count > 0)
                        return "未删除：请清空该储物点并正常保存游戏，再删除";
            Furniture f;
            bool found = GetNativeAnchors(records).TryGetValue(id, out f);
            long owner = found ? f.InstanceId : 0;
            if (found)
                foreach (var pair in BattleLogicWorld.Instance._ItemManager.Cache)
                    if (pair.Value != null && pair.Value.OwnerId == owner)
                        return "未删除：储物点里还有物品，请先全部取出并保存";
            BackupWorldAction("delete-empty-independent-storage");
            if (found)
            {
                BattleLogicWorld.Instance._AgentManager.RemoveFurniture(owner);
                if (GetNativeAnchors(new List<StorageAnchorRecord> { record }).ContainsKey(id))
                    return "原生储物点尚未移除，保留索引，请勿重复操作";
                AnchorOwners.Remove(owner);
            }
            records.Remove(record);
            StorageAnchorRecord.Write(GetPersistentPath(GetCurrentSaveId()) + ".anchors", GetCurrentSaveId(), records);
            interactionRecords = new List<StorageAnchorRecord>();
            nextAnchorRead = DateTime.MinValue;
            interactionHint = ""; nearbyAnchor = "";
            RefreshAnchors(false);
            return "已删除“" + record.Label + "”；原家具未改变，请正常保存游戏";
        }
    }

    [HarmonyPatch(typeof(ConfigManager), "Get_Config_Bag", new[] { typeof(int) })]
    internal static class AnchorConfigPatch
    {
        private static bool Prefix(int __0, ref Config_Bag __result)
        {
            Config_Bag bag;
            if (!Plugin.TryAnchorBag(__0, out bag)) return true;
            __result = bag; return false;
        }
    }

    // Both GetOwnerBagSize and GetOwnerChainMaxBagSize call this integer
    // resolver before consulting shared furniture definitions. No Vector2Int ABI hook.
    [HarmonyPatch(typeof(ItemManager), "GetSyntheticBagId")]
    internal static class AnchorCapacityOwnerPatch
    {
        private static bool Prefix(long __0, ref int __result)
        {
            int id = Plugin.AnchorBagForOwner(__0);
            if (id == 0) return true;
            __result = id; return false;
        }
    }

    [HarmonyPatch(typeof(ItemManager), "LoadItemsFromSave")]
    internal static class AnchorCapacityLoadPatch
    {
        private static void Prefix(GameSaveData gameSave)
        {
            Plugin.ItemLoadUnsafe = false;
            try { Plugin.RestoreAnchorCapacity(gameSave); }
            catch (Exception ex)
            {
                Plugin.ItemLoadUnsafe = true;
                Plugin.LastResult = "储物容量恢复失败，已禁止保存；请退出并保留日志";
                Plugin.Instance.Log.LogError(Plugin.LastResult + "\n" + ex);
            }
        }
    }
}


namespace SurvivalLogTrainer
{
    [HarmonyPatch(typeof(GameSaveManager), "IsWorldSafeToPersist")]
    internal static class ItemLoadSaveGuardPatch
    {
        private static bool Prefix(ref bool __result)
        {
            if (!Plugin.ItemLoadUnsafe) return true;
            __result = false;
            return false;
        }
    }
}
