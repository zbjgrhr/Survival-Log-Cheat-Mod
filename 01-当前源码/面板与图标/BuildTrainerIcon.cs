using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

internal static class BuildTrainerIcon
{
    private static void Main(string[] args)
    {
        int[] sizes = { 16, 32, 48, 64, 128, 256 };
        byte[][] images = new byte[sizes.Length][];
        for (int i = 0; i < sizes.Length; i++) {
            int s = sizes[i];
            using (var bitmap = new Bitmap(s, s))
            using (var graphics = Graphics.FromImage(bitmap)) {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.Clear(Color.Transparent);
                float scale = s / 64F;
                graphics.ScaleTransform(scale, scale);
                using (var background = new SolidBrush(Color.FromArgb(20, 35, 40)))
                    graphics.FillEllipse(background, 2, 2, 60, 60);
                using (var outline = new Pen(Color.FromArgb(163, 214, 154), 2.8F))
                    graphics.DrawEllipse(outline, 3.5F, 3.5F, 57, 57);
                var bolt = new[] {
                    new PointF(37, 12), new PointF(22, 34),
                    new PointF(31, 34), new PointF(26, 52),
                    new PointF(44, 27), new PointF(35, 27)
                };
                using (var fill = new SolidBrush(Color.FromArgb(208, 239, 159)))
                    graphics.FillPolygon(fill, bolt);
                using (var buffer = new MemoryStream()) {
                    bitmap.Save(buffer, ImageFormat.Png);
                    images[i] = buffer.ToArray();
                }
            }
        }
        using (var file = File.Create(args[0]))
        using (var writer = new BinaryWriter(file)) {
            writer.Write((ushort)0);
            writer.Write((ushort)1);
            writer.Write((ushort)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++) {
                writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                writer.Write((byte)0);
                writer.Write((byte)0);
                writer.Write((ushort)1);
                writer.Write((ushort)32);
                writer.Write((uint)images[i].Length);
                writer.Write((uint)offset);
                offset += images[i].Length;
            }
            foreach (byte[] image in images) writer.Write(image);
        }
    }
}
