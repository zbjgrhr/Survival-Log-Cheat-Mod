using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace SurvivalLogTrainer
{
    // UI text only. No save or item geometry is changed by this patch.
    internal static class InventoryScrollPatch
    {
        internal static string Apply(string source, string patch)
        {
            string result = source.Replace("\r\n", "\n");
            result = result.Replace(".grid-wrapper::-webkit-scrollbar { width:12px; height:12px;",
                ".grid-wrapper::-webkit-scrollbar { display:block!important; width:12px!important; height:12px!important;");
            string[] pairs = patch.Trim().Split('\n');
            if (pairs.Length != 12) throw new InvalidDataException("滚动补丁资源不完整");
            for (int i = 0; i < pairs.Length; i += 2)
            {
                string before = Encoding.UTF8.GetString(Convert.FromBase64String(pairs[i].Trim()));
                string after = Encoding.UTF8.GetString(Convert.FromBase64String(pairs[i + 1].Trim()));
                if (result.Contains(after)) continue;
                int first = result.IndexOf(before, StringComparison.Ordinal);
                if (first < 0 || result.IndexOf(before, first + before.Length, StringComparison.Ordinal) >= 0)
                    throw new InvalidDataException("游戏背包页面已变化，未覆盖；需先适配滚动界面");
                result = result.Replace(before, after);
            }
            return result;
        }

        internal static void Ensure(string gameRoot, string backupRoot)
        {
            string path = Path.Combine(gameRoot, "SurvivalLog_Data", "StreamingAssets", "WebUI", "UI", "BackpackUI", "BackpackUI.html");
            string source = File.ReadAllText(path, Encoding.UTF8);
            string patch;
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SurvivalLogTrainer.InventoryScroll.patch"))
            using (var reader = new StreamReader(stream, Encoding.UTF8)) patch = reader.ReadToEnd();
            string result = Apply(source, patch);
            if (result == source.Replace("\r\n", "\n")) return;
            Directory.CreateDirectory(backupRoot);
            string hash;
            using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "");
            string backup = Path.Combine(backupRoot, "BackpackUI-" + hash + ".html");
            if (!File.Exists(backup)) File.Copy(path, backup);
            string temp = path + ".trainer-new";
            File.WriteAllText(temp, result, new UTF8Encoding(false));
            File.Replace(temp, path, null);
        }
    }
}
