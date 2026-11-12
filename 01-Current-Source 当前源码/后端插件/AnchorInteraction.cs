using System;
using System.Collections.Generic;
using GameCore.HotUpdate;
using GameCore.HotUpdate.Battle.Logic;
using GameCore.HotUpdate.ReduxUI;
using UnityEngine;

namespace SurvivalLogTrainer
{
    public sealed partial class Plugin
    {
        internal static bool AnchorInteractionEnabled;
        private static string interactionHint = "", interactionSave = "", nearbyAnchor = "";
        private static int interactionEpoch = -1;
        private static DateTime nextAnchorScan, nextAnchorRead, nextAnchorOpen;
        private static List<StorageAnchorRecord> interactionRecords = new List<StorageAnchorRecord>();
        private static GUIStyle anchorHintStyle;
        private static bool anchorHintUnavailable;

        // UI only: no new inventory, scene object or save hook. Never holds native
        // furniture references across frames. The existing resolver verifies on open.
        internal static void TickAnchorInteraction()
        {
            if (!AnchorInteractionEnabled || !Application.isFocused)
            { interactionHint = ""; nearbyAnchor = ""; return; }
            try
            {
                bool pressed = Input.GetKeyDown(KeyCode.F6) && !Input.GetKey(KeyCode.LeftAlt) && !Input.GetKey(KeyCode.RightAlt) && !Input.GetKey(KeyCode.LeftControl) && !Input.GetKey(KeyCode.RightControl);
                DateTime now = DateTime.UtcNow;
                if (!pressed && now < nextAnchorScan) return;
                nextAnchorScan = now.AddMilliseconds(250);
                interactionHint = ""; nearbyAnchor = "";
                if (!BattleLogicWorld.IsInstanceCreated || !ReduxUISystem.IsInstanceCreated) return;
                if (ReduxUISystem.Instance.HasModalUIOpenExcept(null)) return;
                var leader = GetLeader();
                if (leader == null) return;
                var save = GameSaveManager.Instance.GetGameSave();
                if (save == null || save.CurSave == null || save.IsSubGame) return;
                string id = GetCurrentSaveId();
                if (string.IsNullOrEmpty(id)) return;
                var user = AgentTools.GetAgentComponent<UserComponent>(leader);
                var position = AgentTools.GetAgentComponent<TransformComponent>(leader);
                if (user == null || position == null || !BattleLogicWorld.Instance._AgentManager.IsHomeMap(user.MapConfigId)) return;
                if (interactionSave != id || interactionEpoch != Epoch || now >= nextAnchorRead)
                {
                    interactionRecords = GetAnchorRecords();
                    interactionSave = id; interactionEpoch = Epoch;
                    nextAnchorRead = now.AddSeconds(2);
                }
                StorageAnchorRecord nearest = null;
                float best = 2.5f * 2.5f;
                foreach (var record in interactionRecords)
                {
                    if (record.Map != user.MapConfigId) continue;
                    float distance = (new Vector3(record.X, record.Y, record.Z) - position.Position).sqrMagnitude;
                    if (distance <= best) { best = distance; nearest = record; }
                }
                if (nearest == null) return;
                var native = GetNativeAnchors(new List<StorageAnchorRecord> { nearest });
                if (!native.ContainsKey(nearest.Id)) return;
                nearbyAnchor = nearest.Id;
                interactionHint = "[F6] 打开储物：" + nearest.Label + "  ·  " + Math.Sqrt(best).ToString("0.0") + "米";
                if (pressed && now >= nextAnchorOpen)
                {
                    nextAnchorOpen = now.AddSeconds(1);
                    // Resolve again at the action boundary; never use a cached owner ID.
                    LastResult = OpenAnchor(nearbyAnchor);
                    Instance.Log.LogInfo("In-game storage interaction: " + LastResult);
                    interactionHint = "";
                }
            }
            catch (Exception ex)
            {
                AnchorInteractionEnabled = false;
                interactionHint = "";
                LastResult = "游戏内储物交互已停止：" + ex.Message;
                Instance.Log.LogError("Anchor interaction disabled: " + ex);
            }
        }

        internal static void DrawAnchorInteraction()
        {
            if (anchorHintUnavailable || !AnchorInteractionEnabled || string.IsNullOrEmpty(interactionHint) || !Application.isFocused) return;
            try
            {
                if (anchorHintStyle == null)
                {
                    // Use Unity's existing skin. OS font creation is stripped in
                    // this IL2CPP build and throws NotSupportedException.
                    anchorHintStyle = new GUIStyle();
                    anchorHintStyle.font = GUI.skin.label.font;
                    anchorHintStyle.fontSize = 18;
                    anchorHintStyle.alignment = TextAnchor.MiddleCenter;
                    anchorHintStyle.normal.textColor = new Color(.65f, 1f, .8f);
                }
                float width = Math.Min(620, Screen.width - 32);
                // Display only: mouse clicks are never captured or forwarded.
                var rect = new Rect((Screen.width - width) / 2, Screen.height - 125, width, 45);
                GUI.Box(rect, "");
                GUI.Label(rect, interactionHint, anchorHintStyle);
            }
            catch (Exception ex)
            {
                // Rendering is optional. A broken hint must never turn off F6
                // or change the user's saved interaction preference.
                anchorHintUnavailable = true;
                Instance.Log.LogWarning("Anchor hint unavailable; F6 remains enabled: " + ex);
            }
        }
    }
}


