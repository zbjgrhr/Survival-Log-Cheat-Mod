using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using BepInEx;
using GameCore.HotUpdate;
using GameCore.HotUpdate.Battle.Logic;
using GameCore.HotUpdate.ReduxUI;
using UnityEngine;

namespace SurvivalLogTrainer
{
    public sealed partial class Plugin
    {
        // No renderer scanning or component injection. AnchorCapacity restores
        // per-owner capacity before item loading; creation remains explicit.
        private static string AnchorSnapshot = "", AnchorSnapshotSave = "";
        private static int AnchorSnapshotEpoch = -1;
        private static bool? AnchorBuildCompatible;
        private const string AnchorGameHash = "D8AD5D8965EE787C8FD8B106B929A87FA322D5DAA5709EC128586F202CD78895";

        private static string RunAnchorCommand(string verb, string argument)
        {
            try
            {
                string result;
                if (verb == "ANCHOR_CREATE") result = CreateAnchor(argument);
                else if (verb == "ANCHOR_SIZE") result = ResizeAnchor(argument);
                else if (verb == "ANCHOR_DELETE") result = DeleteAnchor(argument);
                else if (verb == "ANCHOR_OPEN") result = OpenAnchor(argument);
                else if (verb == "ANCHOR_CHECK") result = CheckAnchorTemplate();
                else result = RefreshAnchors(argument == "nearby");
                Instance.Log.LogInfo("Independent storage result [" + verb + "]: " + result);
                return result;
            }
            catch (Exception ex)
            {
                Instance.Log.LogError("Independent storage command " + verb + ": " + ex);
                return "储物点操作已停止：" + ex.Message.Replace('\r', ' ').Replace('\n', ' ');
            }
        }

        private static void CheckAnchorContext()
        {
            var save = GameSaveManager.Instance.GetGameSave();
            if (save == null || save.CurSave == null || save.IsSubGame || string.IsNullOrEmpty(GetCurrentSaveId()))
                throw new InvalidOperationException("请回到家中再使用摆设储物点");
        }

        private static void CheckAnchorBuild()
        {
            if (!AnchorBuildCompatible.HasValue)
                using (var sha = SHA256.Create())
                using (var file = File.OpenRead(Path.Combine(Paths.GameRootPath, "GameAssembly.dll")))
                    AnchorBuildCompatible = BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "") == AnchorGameHash;
            if (AnchorBuildCompatible != true)
                throw new InvalidOperationException("游戏版本已变化，暂不创建新储物点；需先适配此版本");
        }

        private static List<StorageAnchorRecord> GetAnchorRecords()
        {
            string save = GetCurrentSaveId();
            return StorageAnchorRecord.Read(GetPersistentPath(save) + ".anchors", save);
        }

        private static Dictionary<string, Furniture> GetNativeAnchors(List<StorageAnchorRecord> records, bool includeVisible = false)
        {
            var result = new Dictionary<string, Furniture>();
            var list = BattleLogicWorld.Instance._AgentManager.GetAllFurnitures();
            foreach (var record in records)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var f = list[i];
                    if (f == null || f.IsDead || (!includeVisible && f.IsShow)) continue;
                    var t = AgentTools.GetAgentComponent<TransformComponent>(f);
                    if (t == null || !record.Matches(f.AgentConfigId, f.MapConfigId, t.Position.x, t.Position.y, t.Position.z)) continue;
                    if (result.ContainsKey(record.Id)) throw new InvalidOperationException("多个柜子匹配储物点“" + record.Label + "”，停止存取");
                    result.Add(record.Id, f);
                }
            }
            return result;
        }

        private static Config_Furniture FindAnchorTemplate()
        {
            var manager = BattleLogicWorld.Instance._AgentManager;
            var list = manager.GetAllFurnitures();
            var checkedIds = new HashSet<int>();
            for (int i = 0; i < list.Count; i++)
            {
                var f = list[i];
                if (f == null || f.IsDead || !f.IsShow || f.IsBagLocked || !manager.IsHomeMap(f.MapConfigId)) continue;
                var bag = AgentTools.GetAgentComponent<BagComponent>(f);
                if (bag == null || bag.GetBagConfigId() <= 0 || !checkedIds.Add(f.AgentConfigId)) continue;
                var c = ConfigManager.Instance.Get_Config_Furniture(f.AgentConfigId);
                if (c == null) continue;
                Instance.Log.LogInfo("Anchor candidate: config=" + c.ID + " name=" + c.Name_Local + " configBag=" + c.BagId +
                    " liveBag=" + bag.GetBagConfigId() + " accept=" + c.BagAcceptCategory + " electrical=" + c.IsElectrical +
                    " story=" + c.Story + " plant=" + c.PlantFurnitureID + " cook=" + c.CookFurnitureID);
                // Use a real, already initialized unrestricted home container.
                // Reject production, power, plot and loot furniture rather than
                // copying their unrelated behavior into the hidden storage point.
                if (c.BagId <= 0 || c.BagId != bag.GetBagConfigId() || c.BagAcceptCategory != 0 ||
                    c.IsElectrical || c.PlantFurnitureID > 0 || c.CookFurnitureID > 0 || c.VaseCapacity > 0 ||
                    c.RobotPlayerID > 0 || c.Story > 0 || c.ColdRate != 0 || c.PurchaseRewardSetId > 0 ||
                    c.LootRandomGroupId > 0 || c.LootRandomGroupId_P1 > 0 || c.LootRandomGroupId_P2 > 0 ||
                    c.LootRandomGroupId_P3 > 0 || c.LootRandomGroupId_P4 > 0 || string.IsNullOrEmpty(c.ResName)) continue;
                var bc = ConfigManager.Instance.Get_Config_Bag(c.BagId);
                if (bc == null || bc.Size == null || bc.Size.Count < 2 || bc.Size[0] <= 0 || bc.Size[1] <= 0) continue;
                // Resizing a shared template without persistence can shrink it
                // before the next load. Require the existing persistence switch.
                StorageOriginal original;
                if (StorageOriginals.TryGetValue(bc.Pointer, out original) &&
                    (bc.Size[0] != original.Width || bc.Size[1] != original.Height) && !PersistStorageSettings)
                    throw new InvalidOperationException("储物格数已扩大，请先开启“固定所有设置”，再创建储物点");
                return c;
            }
            return null;
        }

        private static string CheckAnchorTemplate()
        {
            CheckAnchorContext(); CheckAnchorBuild();
            var c = FindAnchorTemplate();
            if (c == null) return "未找到可用样板；请先在家中放置普通储物柜或置物架，再检查";
            var b = ConfigManager.Instance.Get_Config_Bag(c.BagId);
            return "可用样板：" + c.Name_Local + "，" + b.Size[0] + "×" + b.Size[1] + "格；检查未创建或修改物品";
        }

        private static string RefreshAnchors(bool nearby)
        {
            CheckAnchorContext();
            var records = GetAnchorRecords();
            BindAnchorCapacities(records);
            var live = GetNativeAnchors(records);
            var leader = GetLeader();
            var user = AgentTools.GetAgentComponent<UserComponent>(leader);
            var t = AgentTools.GetAgentComponent<TransformComponent>(leader);
            if (user == null || t == null) return "角色位置尚未就绪";
            var rows = new StringBuilder(); int count = 0;
            foreach (var r in records)
            {
                float distance = (new Vector3(r.X, r.Y, r.Z) - t.Position).magnitude;
                bool sameMap = r.Map == user.MapConfigId;
                if (nearby && (!sameMap || distance > 6f)) continue;
                Furniture f;
                string state = "未找到原容器（不会自动重建）";
                if (live.TryGetValue(r.Id, out f))
                {
                    var bag = AgentTools.GetAgentComponent<BagComponent>(f);
                    var config = bag == null ? null : ConfigManager.Instance.Get_Config_Bag(bag.GetBagConfigId());
                    state = config != null && config.Size != null && config.Size.Count >= 2
                        ? config.Size[0] + "×" + config.Size[1] + "格" : "容器未就绪";
                    state += sameMap ? " · " + distance.ToString("0.0", CultureInfo.InvariantCulture) + "米" : " · 其他楼层/区域";
                }
                rows.Append(r.Id).Append('\t').Append(StorageAnchorRecord.Encode(r.Label + " — " + state)).Append('\n');
                count++;
            }
            AnchorSnapshot = StorageAnchorRecord.Encode(rows.ToString());
            AnchorSnapshotSave = GetCurrentSaveId(); AnchorSnapshotEpoch = Epoch;
            return (nearby ? "附近" : "本存档") + "共 " + count + " 个储物点；选中后点击打开";
        }

        private static string CreateAnchor(string encodedName)
        {
            CheckAnchorContext(); CheckAnchorBuild();
            CheckAnchorCapacityReady();
            string[] args = encodedName.Split(','); int width, height;
            if (args.Length != 3 || !int.TryParse(args[1], out width) || !int.TryParse(args[2], out height) || !StorageAnchorRecord.ValidSize(width, height))
                return "请使用新版面板，输入 1–50 的宽、高";
            string label = StorageAnchorRecord.Decode(args[0]).Trim();
            if (!StorageAnchorRecord.ValidLabel(label)) return "名称须为 1–24 个字，不能含换行";
            var manager = BattleLogicWorld.Instance._AgentManager;
            var leader = manager.GetLeadingRole();
            var user = AgentTools.GetAgentComponent<UserComponent>(leader);
            var transform = AgentTools.GetAgentComponent<TransformComponent>(leader);
            if (user == null || transform == null || !manager.IsHomeMap(user.MapConfigId)) return "请站在家中摆设旁再创建";
            var records = GetAnchorRecords();
            if (records.Count >= 16) return "试用版最多 16 个储物点，请先验证现有储物点";
            foreach (var old in records)
            {
                if (old.Label == label) return "已有同名储物点，请从全部列表打开或换一个名称";
                if (old.Map == user.MapConfigId && (new Vector3(old.X, old.Y, old.Z) - transform.Position).sqrMagnitude < 0.25f)
                    return "这里已经登记过储物点，请先查看全部列表";
            }
            var template = FindAnchorTemplate();
            if (template == null) return "创建失败：家中没有可用普通储物柜样板；请先放置储物柜或置物架";
            string save = GetCurrentSaveId();
            var position = transform.Position;
            var record = new StorageAnchorRecord { Id = Guid.NewGuid().ToString("N"), Label = label,
                Width = width, Height = height,
                Map = user.MapConfigId, FurnitureConfig = template.ID, X = position.x, Y = position.y, Z = position.z };
            // Do not claim an existing cabinet at this position as our new one.
            if (GetNativeAnchors(new List<StorageAnchorRecord> { record }, true).Count != 0)
                return "当前位置已存在同类容器，请挪开一点再创建";
            BackupWorldAction("create-independent-storage");
            // Write intent first. A missing native object is reported, never silently
            // recreated: doing that could conceal item loss or duplicate inventories.
            records.Add(record);
            StorageAnchorRecord.Write(GetPersistentPath(save) + ".anchors", save, records);
            var created = manager.CreateFurniture(template.ID, 0, 0, record.Map, -1, position, Quaternion.identity);
            if (created == null) return "容器创建未完成，登记已保留；请勿重复创建";
            var bag = AgentTools.GetAgentComponent<BagComponent>(created);
            var location = AgentTools.GetAgentComponent<TransformComponent>(created);
            if (bag == null || bag.GetBagConfigId() != template.BagId || location == null ||
                !record.Matches(created.AgentConfigId, created.MapConfigId, location.Position.x, location.Position.y, location.Position.z))
                return "原生容器组件不完整，未开放存取；请退出且不要保存本次创建";
            created.SetVisible(false, -1);
            BindAnchorCapacities(records);
            Instance.Log.LogInfo("Independent storage created: " + record.Id + " map=" + record.Map + " owner=" + created.InstanceId);
            RefreshAnchors(false);
            return "已创建“" + label + "”；从列表打开。物品随游戏保存，请正常保存游戏";
        }

        private static string OpenAnchor(string id)
        {
            CheckAnchorContext();
            Guid guid;
            if (!Guid.TryParseExact(id, "N", out guid)) return "请先从列表选择储物点";
            var records = GetAnchorRecords(); BindAnchorCapacities(records);
            var live = GetNativeAnchors(records); Furniture f;
            if (!live.TryGetValue(id, out f)) return "未找到原储物点，未创建替代容器；请勿继续往里面存物品";
            if (f.IsBagLocked) return "匹配到的原生容器被锁定，已停止存取";
            var bag = AgentTools.GetAgentComponent<BagComponent>(f);
            if (bag == null || bag.GetBagConfigId() <= 0) return "容器尚未就绪";
            Ac_Item_SetBag.SendAction(f.InstanceId, bag.GetBagConfigId());
            Ac_Item_GetBagList.SendAction(BattleLogicWorld.Instance._AgentManager.GetLeadingRoleId(), f.InstanceId, false);
            return "已请求打开储物点；存取完成后请正常保存游戏";
        }
    }
}
