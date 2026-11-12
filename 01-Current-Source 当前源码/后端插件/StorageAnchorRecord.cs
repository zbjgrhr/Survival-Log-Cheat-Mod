using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace SurvivalLogTrainer
{
    // Metadata only. Item contents remain owned and serialized by the native game.
    internal sealed class StorageAnchorRecord
    {
        internal const string Prefix = "SLT_STORAGE_V1:";
        internal string Id, Label;
        internal int Map, FurnitureConfig;
        internal int Width, Height;
        internal static bool ValidSize(int w, int h) { return w >= 1 && w <= 50 && h >= 1 && h <= 50; }
        internal static bool Fits(int w, int h, int x, int y, int iw, int ih)
        { return ValidSize(w, h) && x >= 0 && y >= 0 && iw > 0 && ih > 0 && (long)x + iw <= w && (long)y + ih <= h; }
        internal float X, Y, Z;
        internal string NativeName { get { return Prefix + Id + ":" + Label; } }

        internal bool Matches(int config, int map, float x, float y, float z)
        {
            if (FurnitureConfig <= 0 || config != FurnitureConfig || map != Map) return false;
            double dx = (double)x - X, dy = (double)y - Y, dz = (double)z - Z;
            return dx * dx + dy * dy + dz * dz <= 0.0001; // 1 cm; no nearest-cabinet fallback.
        }

        internal static bool TryName(string value, out string id, out string label)
        {
            id = label = "";
            if (value == null || !value.StartsWith(Prefix, StringComparison.Ordinal)) return false;
            string tail = value.Substring(Prefix.Length);
            if (tail.Length < 34 || tail[32] != ':') return false;
            Guid parsed;
            if (!Guid.TryParseExact(tail.Substring(0, 32), "N", out parsed)) return false;
            id = parsed.ToString("N"); label = tail.Substring(33);
            return ValidLabel(label);
        }

        internal static bool ValidLabel(string label)
        {
            if (string.IsNullOrWhiteSpace(label) || label.Length > 24) return false;
            foreach (char c in label) if (char.IsControl(c)) return false;
            return true;
        }

        internal static string Encode(string s) { return Convert.ToBase64String(Encoding.UTF8.GetBytes(s)); }
        internal static string Decode(string s) { return new UTF8Encoding(false, true).GetString(Convert.FromBase64String(s)); }
        private static string F(float value) { return value.ToString("R", CultureInfo.InvariantCulture); }
        private static float Float(string value)
        {
            float f = float.Parse(value, CultureInfo.InvariantCulture);
            if (float.IsNaN(f) || float.IsInfinity(f)) throw new InvalidDataException("储物点坐标无效");
            return f;
        }

        internal static List<StorageAnchorRecord> Read(string path, string save)
        {
            var result = new List<StorageAnchorRecord>();
            if (!File.Exists(path)) return result;
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            bool legacy = lines.Length > 0 && lines[0] == "SLT_ANCHORS_1|" + Encode(save);
            bool sized = lines.Length > 0 && lines[0] == "SLT_ANCHORS_3|" + Encode(save);
            if (lines.Length == 0 || (!legacy && !sized && lines[0] != "SLT_ANCHORS_2|" + Encode(save)))
                throw new InvalidDataException("储物点索引不属于当前存档，未执行");
            var seen = new HashSet<string>();
            for (int i = 1; i < lines.Length; i++)
            {
                if (lines[i].Length == 0) continue;
                string[] p = lines[i].Split('|'); Guid id;
                if (p.Length != (legacy ? 6 : sized ? 9 : 7) || !Guid.TryParseExact(p[0], "N", out id) || !seen.Add(p[0]))
                    throw new InvalidDataException("储物点索引损坏，未覆盖原文件");
                var r = new StorageAnchorRecord { Id = id.ToString("N"), Label = Decode(p[1]),
                    Map = int.Parse(p[2], CultureInfo.InvariantCulture), X = Float(p[3]), Y = Float(p[4]), Z = Float(p[5]),
                    FurnitureConfig = legacy ? 0 : int.Parse(p[6], CultureInfo.InvariantCulture) };
                if (!ValidLabel(r.Label) || r.Map <= 0) throw new InvalidDataException("储物点记录无效");
                if (!legacy && r.FurnitureConfig <= 0) throw new InvalidDataException("储物点缺少真实家具配置");
                if (sized) { r.Width = int.Parse(p[7]); r.Height = int.Parse(p[8]); }
                if ((r.Width != 0 || r.Height != 0) && !ValidSize(r.Width, r.Height)) throw new InvalidDataException("储物点尺寸无效");
                foreach (var old in result)
                    if (r.Matches(old.FurnitureConfig, old.Map, old.X, old.Y, old.Z))
                        throw new InvalidDataException("多个储物点索引指向同一容器，已停止");
                result.Add(r);
            }
            return result;
        }

        internal static void Write(string path, string save, List<StorageAnchorRecord> records)
        {
            var text = new StringBuilder("SLT_ANCHORS_3|" + Encode(save) + "\n");
            foreach (var r in records)
            {
                if (r.FurnitureConfig <= 0) throw new InvalidDataException("旧版储物点索引不能自动转换，请保留原文件供检查");
                if ((r.Width != 0 || r.Height != 0) && !ValidSize(r.Width, r.Height)) throw new InvalidDataException("储物点尺寸无效");
                text.Append(r.Id).Append('|').Append(Encode(r.Label)).Append('|').Append(r.Map)
                    .Append('|').Append(F(r.X)).Append('|').Append(F(r.Y)).Append('|').Append(F(r.Z)).Append('|').Append(r.FurnitureConfig)
                    .Append('|').Append(r.Width).Append('|').Append(r.Height).Append('\n');
            }
            string temp = path + ".new";
            File.WriteAllText(temp, text.ToString(), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
            else File.Move(temp, path);
        }
    }
}
