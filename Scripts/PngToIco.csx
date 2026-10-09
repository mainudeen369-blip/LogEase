using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

var png = args[0];
var ico = args[1];
using var src = Image.FromFile(png);
var sizes = new[] { 16, 32, 48, 256 };
using var ms = new MemoryStream();
using var bw = new BinaryWriter(ms);
bw.Write((ushort)0);
bw.Write((ushort)1);
bw.Write((ushort)sizes.Length);
var blobs = new List<byte[]>();
var offset = 6 + 16 * sizes.Length;
foreach (var s in sizes)
{
    using var bmp = new Bitmap(s, s);
    using var g = Graphics.FromImage(bmp);
    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
    g.Clear(Color.Transparent);
    g.DrawImage(src, 0, 0, s, s);
    using var pngMs = new MemoryStream();
    bmp.Save(pngMs, ImageFormat.Png);
    var bytes = pngMs.ToArray();
    blobs.Add(bytes);
    bw.Write((byte)(s >= 256 ? 0 : s));
    bw.Write((byte)(s >= 256 ? 0 : s));
    bw.Write((byte)0);
    bw.Write((byte)0);
    bw.Write((ushort)1);
    bw.Write((ushort)32);
    bw.Write(bytes.Length);
    bw.Write(offset);
    offset += bytes.Length;
}
foreach (var b in blobs) bw.Write(b);
File.WriteAllBytes(ico, ms.ToArray());
Console.WriteLine($"Wrote {ico} ({new FileInfo(ico).Length} bytes)");
