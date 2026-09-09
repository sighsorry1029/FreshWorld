using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FreshWorld.Engine;

internal static class Program
{
    private static int count;
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Test(string name, Action body) { body(); count++; Console.WriteLine("PASS " + name); }
    private static void Reject(Action body)
    {
        try { body(); }
        catch (InvalidDataException) { return; }
        throw new Exception("Malformed TCData was not rejected");
    }
    private static byte[] Create(IEnumerable<int>? heights = null, IEnumerable<int>? paint = null, int paintWidth = 65, int operations = 41)
    {
        var heightCells = new HashSet<int>(heights ?? Array.Empty<int>());
        var paintCells = new HashSet<int>(paint ?? Array.Empty<int>());
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(1); writer.Write(operations); writer.Write(123f); writer.Write(456f); writer.Write(789f); writer.Write(4f);
        writer.Write(65 * 65);
        for (var i = 0; i < 65 * 65; i++)
        {
            writer.Write(heightCells.Contains(i));
            if (heightCells.Contains(i)) { writer.Write(i + 0.125f); writer.Write(-i - 0.25f); }
        }
        writer.Write(paintWidth * paintWidth);
        for (var i = 0; i < paintWidth * paintWidth; i++)
        {
            writer.Write(paintCells.Contains(i));
            if (paintCells.Contains(i)) { writer.Write(0.1f); writer.Write(0.2f); writer.Write(0.3f); writer.Write(0.4f); }
        }
        return stream.ToArray();
    }
    private static (int Operations, Dictionary<int, float[]> Height, Dictionary<int, float[]> Paint, int PaintCount, float X, float Y, float Z, float Radius) Read(byte[] data)
    {
        using var stream = new MemoryStream(data); using var reader = new BinaryReader(stream);
        Assert(reader.ReadInt32() == 1, "version changed"); var operations = reader.ReadInt32();
        var x = reader.ReadSingle(); var y = reader.ReadSingle(); var z = reader.ReadSingle(); var radius = reader.ReadSingle();
        var heights = new Dictionary<int, float[]>(); var paints = new Dictionary<int, float[]>();
        var heightCount = reader.ReadInt32();
        for (var i = 0; i < heightCount; i++) if (reader.ReadBoolean()) heights[i] = new[] { reader.ReadSingle(), reader.ReadSingle() };
        var paintCount = reader.ReadInt32();
        for (var i = 0; i < paintCount; i++) if (reader.ReadBoolean()) paints[i] = new[] { reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle() };
        Assert(stream.Position == stream.Length, "trailing output bytes");
        return (operations, heights, paints, paintCount, x, y, z, radius);
    }
    private static void SetInt(byte[] data, int at, int value) => BitConverter.GetBytes(value).CopyTo(data, at);

    private static int Main()
    {
        try
        {
            Test("radius clears local height and paint while preserving outside payloads", () =>
            {
                var center = 32 * 65 + 32; var remote = 10 * 65 + 10;
                var source = Create(new[] { center, remote }, new[] { center, remote }); var copy = (byte[])source.Clone();
                var edit = TerrainDataCodec.ResetCircle(source, 100, 200, 100, 200, 1);
                var before = Read(source); var after = Read(edit.Data);
                Assert(edit.ChangedCells == 2 && after.Operations == 42, "local cells/revision");
                Assert(!after.Height.ContainsKey(center) && !after.Paint.ContainsKey(center), "center remained modified");
                Assert(after.Height[remote].SequenceEqual(before.Height[remote]) && after.Paint[remote].SequenceEqual(before.Paint[remote]), "outside payload changed");
                Assert(source.SequenceEqual(copy), "input buffer mutated");
                Assert(after.X == 100 && after.Y == 456 && after.Z == 200 && after.Radius == 1, "native refresh metadata");
            });
            Test("height vertices and paint centers follow their separate game coordinates", () =>
            {
                var center = 32 * 65 + 32;
                var source = Create(new[] { center }, new[] { center });
                var vertex = Read(TerrainDataCodec.ResetCircle(source, 0, 0, 0, 0, 0.25f).Data);
                Assert(vertex.Height.Count == 0 && vertex.Paint.Count == 1, "height center should be at 0,0");
                var paint = Read(TerrainDataCodec.ResetCircle(source, 0, 0, 0.5f, 0.5f, 0.25f).Data);
                Assert(paint.Height.Count == 1 && paint.Paint.Count == 0, "paint center should be at 0.5,0.5");
            });
            Test("legacy 64-square paint coordinates and serialization stay compatible", () =>
            {
                var atCenter = 32 * 64 + 32; var elsewhere = 31 * 64 + 32;
                var source = Create(paint: new[] { atCenter, elsewhere }, paintWidth: 64);
                var edit = TerrainDataCodec.ResetCircle(source, 0, 0, 0.5f, 0.5f, 0.25f); var after = Read(edit.Data);
                Assert(after.PaintCount == 4096 && !after.Paint.ContainsKey(atCenter) && after.Paint.ContainsKey(elsewhere), "legacy row stride or layout changed");
                Assert(edit.ChangedCells == 1 && after.Operations == 42, "legacy changed count");
            });
            Test("edge radius is exclusive and no change preserves exact bytes and revision", () =>
            {
                var source = Create(new[] { 32 * 65 + 33 });
                var edit = TerrainDataCodec.ResetCircle(source, 0, 0, 0, 0, 1);
                Assert(!edit.Changed && ReferenceEquals(edit.Data, source) && Read(edit.Data).Operations == 41, "point exactly on radius changed");
                var zero = TerrainDataCodec.ResetCircle(source, 0, 0, 1, 0, 0);
                Assert(!zero.Changed && ReferenceEquals(zero.Data, source), "zero radius changed data");
            });
            Test("cardinal border repairs only height seam and preserves paint and interior", () =>
            {
                var all = Enumerable.Range(0, 65 * 65).ToArray(); var source = Create(all, all);
                var edit = TerrainDataCodec.ResetBorders(source, BorderDirection.North | BorderDirection.East);
                var after = Read(edit.Data);
                Assert(edit.ChangedCells == 129 && after.Height.Count == 65 * 65 - 129, "two seams should share one corner");
                Assert(after.Height.ContainsKey(32 * 65 + 32), "interior erased");
                Assert(after.Paint.Count == 65 * 65, "border repair erased paint");
                Assert(after.Height.Keys.All(i => i / 65 != 64 && i % 65 != 64), "requested border retained");
            });
            Test("diagonal border flags repair only their respective corner", () =>
            {
                foreach (var entry in new[]
                {
                    (BorderDirection.NorthEast, 64 * 65 + 64), (BorderDirection.SouthEast, 64),
                    (BorderDirection.SouthWest, 0), (BorderDirection.NorthWest, 64 * 65)
                })
                {
                    var source = Create(Enumerable.Range(0, 65 * 65)); var edit = TerrainDataCodec.ResetBorders(source, entry.Item1);
                    var after = Read(edit.Data);
                    Assert(edit.ChangedCells == 1 && !after.Height.ContainsKey(entry.Item2) && after.Height.Count == 4224, "diagonal removed wrong cells");
                }
            });
            Test("repeat repair does not increment revision again", () =>
            {
                var first = TerrainDataCodec.ResetBorders(Create(new[] { 0 }), BorderDirection.West);
                var second = TerrainDataCodec.ResetBorders(first.Data, BorderDirection.West);
                Assert(first.Changed && !second.Changed && ReferenceEquals(first.Data, second.Data), "repeat changed bytes");
                Assert(Read(second.Data).Operations == 42, "repeat incremented revision");
            });
            Test("every truncation of modified TCData rejects without touching input", () =>
            {
                var source = Create(new[] { 0, 2112, 4224 }, new[] { 0, 2112, 4224 });
                for (var length = 0; length < source.Length; length++)
                {
                    var truncated = source.Take(length).ToArray(); var before = (byte[])truncated.Clone();
                    Reject(() => TerrainDataCodec.ResetBorders(truncated, BorderDirection.North));
                    Assert(truncated.SequenceEqual(before), "malformed input was mutated");
                }
            });
            Test("unknown versions, dimensions, flags, trailing data and nonfinite payloads reject", () =>
            {
                var source = Create(new[] { 0 });
                var version = (byte[])source.Clone(); SetInt(version, 0, 2); Reject(() => TerrainDataCodec.ResetCircle(version, 0, 0, 0, 0, 20));
                var heights = (byte[])source.Clone(); SetInt(heights, 24, 4096); Reject(() => TerrainDataCodec.ResetBorders(heights, BorderDirection.West));
                var paint = Create(); SetInt(paint, 28 + 4225, 4000); Reject(() => TerrainDataCodec.ResetBorders(paint, BorderDirection.West));
                var flag = (byte[])source.Clone(); flag[28] = 2; Reject(() => TerrainDataCodec.ResetBorders(flag, BorderDirection.West));
                var trailing = source.Concat(new byte[] { 0 }).ToArray(); Reject(() => TerrainDataCodec.ResetBorders(trailing, BorderDirection.West));
                var nonfinite = (byte[])source.Clone(); BitConverter.GetBytes(float.NaN).CopyTo(nonfinite, 29); Reject(() => TerrainDataCodec.ResetBorders(nonfinite, BorderDirection.West));
            });
            Test("operation count overflow rejects only when a modification would occur", () =>
            {
                var source = Create(new[] { 0 }, operations: int.MaxValue);
                Assert(!TerrainDataCodec.ResetBorders(source, BorderDirection.East).Changed, "no-op rejected at maximum counter");
                Reject(() => TerrainDataCodec.ResetBorders(source, BorderDirection.West));
                Assert(Read(source).Height.Count == 1 && Read(source).Operations == int.MaxValue, "overflow mutated input");
            });
            Console.WriteLine($"All {count} terrain codec regression checks passed.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
