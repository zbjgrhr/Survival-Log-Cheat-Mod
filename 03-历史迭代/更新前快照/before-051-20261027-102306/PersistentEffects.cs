using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using GameCore.HotUpdate;
using GameCore.HotUpdate.Battle.Logic;
using HarmonyLib;

namespace SurvivalLogTrainer
{
    public sealed partial class Plugin
    {
        internal static bool PersistAllSettings, NoExploreExposure;
        // Only continuous options belong here. Commands and resource totals must never replay.
        private static readonly string[] FixedBooleanFields = {
            "InfiniteStamina", "InfiniteSatiety", "InfiniteMorale", "InfiniteShelfLife",
            "InfiniteUses", "InfiniteDurability", "InfiniteGeneration", "InfiniteBattery",
            "InfiniteGeneratorFuel", "InfiniteCookingFuel", "InstantPlants", "ZeroTravelTime",
            "NoExploreExposure"
        };

        internal static string SerializeExtraSettings()
        {
            var text = new StringBuilder("persist_all=" + B(PersistAllSettings) + "\n");
            text.Append("fixed_move=" + MoveMultiplier.ToString(CultureInfo.InvariantCulture) + "\n");
            text.Append("fixed_time=" + TimeMultiplier.ToString(CultureInfo.InvariantCulture) + "\n");
            text.Append("fixed_skill=" + SkillMultiplier.ToString(CultureInfo.InvariantCulture) + "\n");
            foreach (string name in FixedBooleanFields)
                text.Append("fixed_" + name + "=" + B((bool)typeof(Plugin).GetField(name, BindingFlags.NonPublic | BindingFlags.Static).GetValue(null)) + "\n");
            return text.ToString();
        }

        internal static void LoadExtraSettings(string text, bool applyNativeTime)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string line in text.Split('\n'))
            {
                int index = line.IndexOf('=');
                if (index > 0) values[line.Substring(0, index)] = line.Substring(index + 1).Trim();
            }
            string value;
            PersistAllSettings = values.TryGetValue("persist_all", out value) && value == "1";
            foreach (string name in FixedBooleanFields)
                typeof(Plugin).GetField(name, BindingFlags.NonPublic | BindingFlags.Static).SetValue(null,
                    PersistAllSettings && values.TryGetValue("fixed_" + name, out value) && value == "1");
            MoveMultiplier = ReadFixedFloat(values, "fixed_move", 1f, 20f);
            TimeMultiplier = ReadFixedFloat(values, "fixed_time", 0.25f, 20f);
            SkillMultiplier = (int)ReadFixedFloat(values, "fixed_skill", 1f, 20f);
            if (applyNativeTime)
            {
                var time = BattleLogicWorld.IsInstanceCreated ? BattleLogicWorld.Instance?._GameTimeManager : null;
                if (time != null) time.GmSetCustomGameSpeed(TimeMultiplier);
            }
        }

        private static float ReadFixedFloat(Dictionary<string, string> values, string key, float min, float max)
        {
            string value;
            float parsed;
            return PersistAllSettings && values.TryGetValue(key, out value) &&
                float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) &&
                !float.IsNaN(parsed) && !float.IsInfinity(parsed) && parsed >= min && parsed <= max ? parsed : 1f;
        }
    }

    [HarmonyPatch(typeof(ExploreManager), "Update")]
    internal static class ExploreLockPatch
    {
        // IL2CPP inlines the getter in Update. Scope the native flag to this call,
        // preserving external settings and allowing alarm/UI/story updates to run.
        private static void Prefix(out bool __state)
        {
            __state = Plugin.NoExploreExposure && !WebGm.LockExploreExposure;
            if (__state) WebGm.LockExploreExposure = true;
        }
        private static void Finalizer(bool __state)
        {
            if (__state) WebGm.LockExploreExposure = false;
        }
    }

    [HarmonyPatch(typeof(ExploreManager), "AddExposure", new Type[] { typeof(int) })]
    internal static class ExplorePositivePatch
    {
        private static bool Prefix(int value) => !Plugin.NoExploreExposure || value <= 0;
    }
}
