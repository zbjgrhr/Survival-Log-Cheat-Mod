using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using GameCore.HotUpdate;
using GameCore.HotUpdate.Battle.Logic;
using GameCore.HotUpdate.ReduxUI;

namespace SurvivalLogTrainer
{
    public sealed partial class Plugin
    {
        private static void BackupWorldAction(string action)
        {
            string source = GameSaveManager.Instance.GetLastPlaySaveFilePath();
            if (string.IsNullOrEmpty(source) || !File.Exists(source))
                throw new IOException("找不到当前存档文件，未执行地区操作");
            string folder = Path.Combine(BridgeDirectory, "world-backups", DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + action);
            Directory.CreateDirectory(folder);
            File.Copy(source, Path.Combine(folder, Path.GetFileName(source)), false);
        }

        private static string RestoreRegions()
        {
            var save = GameSaveManager.Instance.GetGameSave();
            var world = BattleLogicWorld.Instance;
            if (save == null || save.CurSave == null || world?._TagManager == null) return "存档尚未就绪";
            if (save.IsSubGame) return "请先返回家中，再恢复地区";
            var fallen = save.FallenMapPointIds;
            if (fallen == null || fallen.Count == 0) return "当前没有沦陷地区";
            var ids = new List<int>();
            for (int i = 0; i < fallen.Count; i++) ids.Add(fallen[i]);
            BackupWorldAction("restore-regions");
            Ac_TradeUI_Close.SendAction();
            Ac_ExploreMap_Close.SendAction();
            int tradeCount = 0;
            foreach (int id in ids)
            {
                var map = ConfigManager.Instance.Get_Config_MapPoint(id);
                if (map != null && map.ShelfShopId > 0) tradeCount++;
                // This counter is the map's recorded fall tier, not world time.
                GameCounter.SetCounter("endless.fall_tier_" + id, 0);
            }
            fallen.Clear();
            var tags = new Il2CppSystem.Collections.Generic.List<string>();
            // Added only when the final trade point falls; otherwise the drone entrance remains hidden.
            int sealedCount = world._TagManager.GetTagCount("EndlessAllTradePointsFallen");
            for (int i = 0; i < sealedCount; i++) tags.Add("EndlessAllTradePointsFallen");
            if (tags.Count > 0) world._TagManager.RemoveTags(tags);
            Instance.Log.LogInfo("Restored fallen map IDs: " + string.Join(",", ids));
            return "已恢复 " + ids.Count + " 个地区（交易点 " + tradeCount + "）；重新打开地图查看";
        }

        private static string RefillRegions()
        {
            var save = GameSaveManager.Instance.GetGameSave();
            if (save == null || save.CurSave == null) return "存档尚未就绪";
            if (save.IsSubGame) return "请先返回家中，再补满地区资源";
            var child = save.CurSave;
            var stockPlan = new Dictionary<int, string>();
            int regionCount = 0, itemKinds = 0;
            var maps = ConfigManager.Instance.GetAll_Config_MapPoint().GetEnumerator();
            var cursor = maps.Cast<Il2CppSystem.Collections.IEnumerator>();
            while (cursor.MoveNext())
            {
                var map = maps.Current;
                if (map == null || map.ShelfShopId <= 0) continue;
                regionCount++;
                if (stockPlan.ContainsKey(map.ShelfShopId)) continue;
                var shop = ConfigManager.Instance.Get_Config_Shop(map.ShelfShopId);
                if (shop == null || shop.ItemIdList == null || shop.ItemIdList.Count == 0) continue;
                string oldJson = child.ActiveShelfStock != null && child.ActiveShelfStock.ContainsKey(map.ShelfShopId)
                    ? (string)child.ActiveShelfStock[map.ShelfShopId] : "{}";
                var stock = Reducer_Web_TradeUI.ParsePicks(oldJson);
                var merged = new SortedDictionary<int, int>();
                if (stock != null)
                {
                    var enumerator = stock.GetEnumerator();
                    while (enumerator.MoveNext()) merged[enumerator.Current.Key] = enumerator.Current.Value;
                }
                for (int i = 0; i < shop.ItemIdList.Count; i++)
                {
                    int id = shop.ItemIdList[i];
                    if (id <= 0) continue;
                    int full = shop.ItemCountList != null && i < shop.ItemCountList.Count ? shop.ItemCountList[i] : 1;
                    full = Math.Max(1, full);
                    int old;
                    merged[id] = Math.Max(merged.TryGetValue(id, out old) ? old : 0, full);
                    itemKinds++;
                }
                var json = new StringBuilder("{");
                foreach (var pair in merged)
                {
                    if (json.Length > 1) json.Append(',');
                    json.Append('"').Append(pair.Key).Append("\":").Append(pair.Value);
                }
                json.Append('}');
                // Round-trip through the game's parser before any stock is changed.
                if (Reducer_Web_TradeUI.ParsePicks(json.ToString()).Count != merged.Count)
                    return "库存格式检查失败，未执行补货";
                stockPlan[map.ShelfShopId] = json.ToString();
            }
            if (stockPlan.Count == 0) return "没有找到地区货架配置，未执行补货";
            BackupWorldAction("refill-regions");
            Ac_TradeUI_Close.SendAction();
            Ac_ExploreMap_Close.SendAction();
            if (child.ActiveShelfStock == null) child.ActiveShelfStock = new Il2CppSystem.Collections.Generic.Dictionary<int, string>();
            if (child.ActiveShelfSnapshots == null) child.ActiveShelfSnapshots = new Il2CppSystem.Collections.Generic.Dictionary<int, string>();
            if (child.ActiveShelfRestockDay == null) child.ActiveShelfRestockDay = new Il2CppSystem.Collections.Generic.Dictionary<int, int>();
            int day = BattleLogicWorld.Instance._GameTimeManager.GetDay();
            foreach (var pair in stockPlan)
            {
                child.ActiveShelfStock[pair.Key] = pair.Value;
                child.ActiveShelfSnapshots[pair.Key] = pair.Value;
                child.ActiveShelfRestockDay[pair.Key] = day;
            }
            Instance.Log.LogInfo("Refilled map shelves=" + stockPlan.Count + ", configured items=" + itemKinds);
            return "已补满 " + stockPlan.Count + " 处地区货架（涵盖 " + regionCount + " 个地图点）；重新打开地图查看";
        }
    }
}
