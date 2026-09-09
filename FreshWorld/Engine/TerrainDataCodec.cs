using System;
using System.IO;

namespace FreshWorld.Engine
{
    [Flags]
    internal enum BorderDirection
    {
        None = 0, North = 1, East = 2, South = 4, West = 8,
        NorthEast = 16, SouthEast = 32, SouthWest = 64, NorthWest = 128
    }

    internal readonly struct TerrainEdit
    {
        public byte[] Data { get; }
        public int ChangedCells { get; }
        public bool Changed => ChangedCells != 0;
        public TerrainEdit(byte[] data, int changedCells) { Data = data; ChangedCells = changedCells; }
    }

    /// <summary>
    /// Edits the uncompressed version-1 TCData package without touching its input buffer.
    /// Layout verified against TerrainComp.Save/Load in the installed Valheim assembly:
    /// 65x65 heights, and either current 65x65 or legacy 64x64 paint. Unknown formats fail closed.
    /// Radius/border reset behavior was informed by Upgrade World's ResetTerrain/ResetBorder;
    /// the independent codec uses the game's verified height-vertex and paint-cell coordinates.
    /// </summary>
    internal static class TerrainDataCodec
    {
        private const int VertexWidth = 65;
        private const int HeightCells = VertexWidth * VertexWidth;
        private const int LegacyPaintCells = 64 * 64;
        private const int MaximumBytes = 256 * 1024;

        public static TerrainEdit ResetCircle(byte[] data, float centerX, float centerZ, float resetX, float resetZ, float radius)
        {
            RequireFinite(centerX, nameof(centerX)); RequireFinite(centerZ, nameof(centerZ));
            RequireFinite(resetX, nameof(resetX)); RequireFinite(resetZ, nameof(resetZ));
            RequireFinite(radius, nameof(radius));
            if (radius < 0) throw new ArgumentOutOfRangeException(nameof(radius));
            bool Contains(int x, int z, bool paint)
            {
                // Heightmap.WorldToVertex: integer vertices at -32..32.
                // PaintCleared offsets the world point by 0.5m: paint cell centers are -31.5..32.5.
                var offset = paint ? 31.5 : 32.0;
                var dx = (double)centerX + x - offset - resetX;
                var dz = (double)centerZ + z - offset - resetZ;
                return dx * dx + dz * dz < (double)radius * radius;
            }
            return Rewrite(data, (x, z) => Contains(x, z, false), (x, z) => Contains(x, z, true), resetX, resetZ, radius);
        }

        public static TerrainEdit ResetBorders(byte[] data, BorderDirection directions, float centerX = 0, float centerZ = 0)
        {
            if (((int)directions & ~255) != 0) throw new ArgumentOutOfRangeException(nameof(directions));
            RequireFinite(centerX, nameof(centerX)); RequireFinite(centerZ, nameof(centerZ));
            bool Height(int x, int z) =>
                ((directions & BorderDirection.North) != 0 && z == 64) ||
                ((directions & BorderDirection.East) != 0 && x == 64) ||
                ((directions & BorderDirection.South) != 0 && z == 0) ||
                ((directions & BorderDirection.West) != 0 && x == 0) ||
                ((directions & BorderDirection.NorthEast) != 0 && x == 64 && z == 64) ||
                ((directions & BorderDirection.SouthEast) != 0 && x == 64 && z == 0) ||
                ((directions & BorderDirection.SouthWest) != 0 && x == 0 && z == 0) ||
                ((directions & BorderDirection.NorthWest) != 0 && x == 0 && z == 64);
            // Paint does not create a geometric seam; preserve every paint record during border repair.
            return Rewrite(data, Height, (_, __) => false, centerX, centerZ, 64);
        }

        private static TerrainEdit Rewrite(byte[] data, Func<int, int, bool> resetHeight, Func<int, int, bool> resetPaint,
            float operationX, float operationZ, float operationRadius)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.Length > MaximumBytes) throw Invalid("TCData exceeds the known version-1 size limit.");
            var reader = new Cursor(data);
            if (reader.ReadInt() != 1) throw Invalid("Unsupported TCData version; the original terrain data was left untouched.");
            var operations = reader.ReadInt();
            if (operations < 0) throw Invalid("TCData has a negative operation count.");
            reader.ReadFinite(); var operationY = reader.ReadFinite(); reader.ReadFinite();
            if (reader.ReadFinite() < 0) throw Invalid("TCData has a negative operation radius.");
            if (reader.ReadInt() != HeightCells) throw Invalid("Unsupported TCData height-grid dimensions.");

            using (var output = new MemoryStream(data.Length))
            {
                output.Write(data, 0, reader.Position);
                var changed = RewriteCells(reader, output, HeightCells, VertexWidth, 2, resetHeight);
                var paintHeader = reader.Position;
                var paintCount = reader.ReadInt();
                if (paintCount != HeightCells && paintCount != LegacyPaintCells)
                    throw Invalid("Unsupported TCData paint-grid dimensions.");
                output.Write(data, paintHeader, sizeof(int));
                changed += RewriteCells(reader, output, paintCount, paintCount == LegacyPaintCells ? 64 : VertexWidth, 4, resetPaint);
                if (reader.Position != data.Length) throw Invalid("TCData contains an unknown trailing payload.");
                if (changed == 0) return new TerrainEdit(data, 0);
                if (operations == int.MaxValue) throw Invalid("TCData operation count cannot be incremented safely.");

                // Update operation metadata as the native compiler does, so loaded clients refresh grass
                // around the edited terrain rather than around an unrelated previous player operation.
                output.Position = 4;
                using (var writer = new BinaryWriter(output, System.Text.Encoding.UTF8, true))
                {
                    writer.Write(operations + 1);
                    writer.Write(operationX); writer.Write(operationY); writer.Write(operationZ); writer.Write(operationRadius);
                }
                return new TerrainEdit(output.ToArray(), changed);
            }
        }

        private static int RewriteCells(Cursor reader, Stream output, int count, int width, int floatCount, Func<int, int, bool> reset)
        {
            var changed = 0;
            for (var index = 0; index < count; index++)
            {
                var flag = reader.ReadByte();
                if (flag > 1) throw Invalid("TCData contains an invalid modification flag.");
                if (flag == 0) { output.WriteByte(0); continue; }
                var start = reader.Position;
                for (var i = 0; i < floatCount; i++) reader.ReadFinite();
                if (reset(index % width, index / width)) { output.WriteByte(0); changed++; }
                else
                {
                    output.WriteByte(1);
                    output.Write(reader.Data, start, floatCount * sizeof(float));
                }
            }
            return changed;
        }

        private static void RequireFinite(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentOutOfRangeException(name, "Coordinate/radius must be finite.");
        }
        private static InvalidDataException Invalid(string message) => new InvalidDataException(message);

        private sealed class Cursor
        {
            public byte[] Data { get; }
            public int Position { get; private set; }
            public Cursor(byte[] data) { Data = data; }
            public byte ReadByte()
            {
                Require(1);
                return Data[Position++];
            }
            public int ReadInt()
            {
                Require(4);
                var offset = Position; Position += 4;
                return Data[offset] | (Data[offset + 1] << 8) | (Data[offset + 2] << 16) | (Data[offset + 3] << 24);
            }
            public float ReadFinite()
            {
                Require(4);
                float value;
                if (BitConverter.IsLittleEndian) value = BitConverter.ToSingle(Data, Position);
                else value = BitConverter.ToSingle(new[] { Data[Position + 3], Data[Position + 2], Data[Position + 1], Data[Position] }, 0);
                Position += 4;
                if (float.IsNaN(value) || float.IsInfinity(value)) throw Invalid("TCData contains a nonfinite value.");
                return value;
            }
            private void Require(int bytes)
            {
                if (Position > Data.Length - bytes) throw Invalid("TCData is truncated; the original terrain data was left untouched.");
            }
        }
    }
}
