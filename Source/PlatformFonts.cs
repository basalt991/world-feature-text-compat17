using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace WorldFeatureTextCompat17
{
    public sealed class SelectedFont
    {
        public string Path;
        public int FaceIndex;
        public byte[] Data;
        public string Name => System.IO.Path.GetFileNameWithoutExtension(Path) + "/face" + FaceIndex;
    }

    // Reads installed fonts only. No registry, CoreText, Win32 or native font library
    // is required: Waylith's own FreeType rasterizes the resulting SFNT bytes.
    public static class PlatformFonts
    {
        public static string[] CandidatePaths(bool mac, string windowsFonts)
        {
            if (mac) return new[] {
                "/System/Library/Fonts/AppleSDGothicNeo.ttc",
                "/System/Library/Fonts/Supplemental/AppleGothic.ttf",
                "/System/Library/Fonts/AppleGothic.ttf",
                "/System/Library/Fonts/Supplemental/Arial Unicode.ttf"
            };
            return new[] { System.IO.Path.Combine(windowsFonts, "malgun.ttf") };
        }

        public static SelectedFont Load(IEnumerable<char> required)
        {
            bool mac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
            if (!mac && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                throw new PlatformNotSupportedException("World text compatibility currently targets Windows and macOS.");
            return Resolve(CandidatePaths(mac, mac ? "" : Environment.GetFolderPath(Environment.SpecialFolder.Fonts)),
                File.Exists, ReadFile, required);
        }

        private static byte[] ReadFile(string path)
        {
            using (var stream = File.OpenRead(path))
            {
                if (stream.Length < 12 || stream.Length > 256 * 1024 * 1024)
                    throw new InvalidDataException("Invalid font file size.");
                var data = new byte[checked((int)stream.Length)];
                stream.ReadExactly(data);
                return data;
            }
        }

        public static SelectedFont Resolve(IEnumerable<string> paths, Func<string, bool> exists,
            Func<string, byte[]> read, IEnumerable<char> required)
        {
            char[] characters = required.Where(c => !char.IsControl(c)).Distinct().ToArray();
            var errors = new List<string>();
            foreach (string path in paths)
            {
                if (!exists(path)) continue;
                try
                {
                    byte[] data = read(path);
                    int face = SelectFace(data, characters);
                    if (face < 0) { errors.Add(System.IO.Path.GetFileName(path) + ": missing required glyphs"); continue; }
                    return new SelectedFont { Path = path, FaceIndex = face, Data = ExtractFace(data, face) };
                }
                catch (Exception e) when (e is IOException || e is InvalidDataException || e is UnauthorizedAccessException || e is OverflowException || e is ArgumentException)
                { errors.Add(System.IO.Path.GetFileName(path) + ": " + e.Message); }
            }
            throw new FileNotFoundException("World text compatibility: no installed font covers the world names. " +
                "Windows requires Malgun Gothic; macOS requires Apple SD Gothic Neo or AppleGothic. " + string.Join("; ", errors));
        }

        private sealed class Table { public uint Tag; public int Offset, Length; }
        private static void Bounds(byte[] data, int offset, int length)
        {
            if (offset < 0 || length < 0 || offset > data.Length - length) throw new InvalidDataException("Invalid font table bounds.");
        }
        private static int U16(byte[] d, int o) { Bounds(d, o, 2); return d[o] << 8 | d[o + 1]; }
        private static uint U32(byte[] d, int o) { Bounds(d, o, 4); return (uint)d[o] << 24 | (uint)d[o + 1] << 16 | (uint)d[o + 2] << 8 | d[o + 3]; }
        private static void Put16(byte[] d, int o, int n) { d[o] = (byte)(n >> 8); d[o + 1] = (byte)n; }
        private static void Put32(byte[] d, int o, uint n) { d[o] = (byte)(n >> 24); d[o + 1] = (byte)(n >> 16); d[o + 2] = (byte)(n >> 8); d[o + 3] = (byte)n; }
        private static int[] Faces(byte[] data)
        {
            Bounds(data, 0, 12);
            if (U32(data, 0) != 0x74746366) return new[] { 0 };
            int count = checked((int)U32(data, 8));
            if (count < 1 || count > 256) throw new InvalidDataException("Invalid font collection count.");
            Bounds(data, 12, count * 4);
            return Enumerable.Range(0, count).Select(i => checked((int)U32(data, 12 + i * 4))).ToArray();
        }
        private static Table[] Tables(byte[] data, int face)
        {
            Bounds(data, face, 12);
            uint signature = U32(data, face);
            if (signature != 0x00010000 && signature != 0x4F54544F && signature != 0x74727565)
                throw new InvalidDataException("Unsupported SFNT signature.");
            int count = U16(data, face + 4);
            if (count < 1 || count > 512) throw new InvalidDataException("Invalid font table count.");
            Bounds(data, checked(face + 12), count * 16);
            var tables = new Table[count];
            for (int i = 0; i < count; i++)
            {
                int r = checked(face + 12 + i * 16);
                var table = new Table { Tag = U32(data, r), Offset = checked((int)U32(data, r + 8)), Length = checked((int)U32(data, r + 12)) };
                Bounds(data, table.Offset, table.Length); tables[i] = table;
            }
            return tables;
        }

        public static int SelectFace(byte[] data, IEnumerable<char> required)
        {
            var faces = Faces(data);
            char[] characters = required.Where(c => !char.IsControl(c)).Distinct().ToArray();
            int selected = -1, distance = int.MaxValue;
            for (int i = 0; i < faces.Length; i++)
            {
                var tables = Tables(data, faces[i]);
                var cmap = tables.FirstOrDefault(t => t.Tag == 0x636D6170);
                if (cmap == null) continue;
                var glyph = GlyphLookup(data, cmap);
                if (!characters.All(glyph)) continue;
                var os2 = tables.FirstOrDefault(t => t.Tag == 0x4F532F32);
                int weight = os2 != null && os2.Length >= 6 ? U16(data, os2.Offset + 4) : 400;
                int delta = Math.Abs(weight - 400); // Collections may start with UltraLight, not Regular.
                if (delta < distance) { selected = i; distance = delta; }
            }
            return selected;
        }

        private static Func<char, bool> GlyphLookup(byte[] source, Table table)
        {
            var d = new byte[table.Length]; Array.Copy(source, table.Offset, d, 0, d.Length);
            int count = U16(d, 2), chosen = -1, format = 0;
            Bounds(d, 4, checked(count * 8));
            for (int i = 0; i < count; i++)
            {
                int r = 4 + i * 8, platform = U16(d, r);
                if (platform != 0 && platform != 3) continue;
                int offset = checked((int)U32(d, r + 4)), candidate = U16(d, offset);
                if ((candidate == 12 && format != 12) || (candidate == 4 && format == 0)) { chosen = offset; format = candidate; }
            }
            if (chosen < 0) return c => false;
            if (format == 12)
            {
                int groups = checked((int)U32(d, checked(chosen + 12)));
                Bounds(d, checked(chosen + 16), checked(groups * 12));
                return c => {
                    int lo = 0, hi = groups - 1;
                    while (lo <= hi)
                    {
                        int mid = lo + (hi - lo) / 2, r = chosen + 16 + mid * 12;
                        uint first = U32(d, r), last = U32(d, r + 4);
                        if (c < first) hi = mid - 1;
                        else if (c > last) lo = mid + 1;
                        else return U32(d, r + 8) + c - first != 0;
                    }
                    return false;
                };
            }
            int segments = U16(d, chosen + 6) / 2;
            int ends = chosen + 14, starts = chosen + 16 + segments * 2,
                deltas = chosen + 16 + segments * 4, ranges = chosen + 16 + segments * 6;
            Bounds(d, ends, segments * 2); Bounds(d, ranges, segments * 2);
            return c => {
                int lo = 0, hi = segments - 1;
                while (lo <= hi)
                {
                    int mid = lo + (hi - lo) / 2, first = U16(d, starts + mid * 2), last = U16(d, ends + mid * 2);
                    if (c < first) hi = mid - 1;
                    else if (c > last) lo = mid + 1;
                    else
                    {
                        int delta = unchecked((short)U16(d, deltas + mid * 2)), range = U16(d, ranges + mid * 2);
                        int glyph = range == 0 ? c : U16(d, checked(ranges + mid * 2 + range + (c - first) * 2));
                        return (range == 0 || glyph != 0) && ((glyph + delta) & 0xFFFF) != 0;
                    }
                }
                return false;
            };
        }

        public static byte[] ExtractFace(byte[] source, int index)
        {
            var faces = Faces(source);
            if (index < 0 || index >= faces.Length) throw new InvalidDataException("Invalid font face index.");
            var tables = Tables(source, faces[index]).Where(t => t.Tag != 0x44534947).OrderBy(t => t.Tag).ToArray();
            if (faces.Length == 1 && faces[0] == 0) return source;
            int total = 12 + tables.Length * 16;
            foreach (var t in tables) total = checked(total + ((t.Length + 3) & ~3));
            if (total > 64 * 1024 * 1024) throw new InvalidDataException("Font face is too large.");
            var result = new byte[total]; Array.Copy(source, faces[index], result, 0, 4);
            Put16(result, 4, tables.Length);
            int power = 1, selector = 0; while (power * 2 <= tables.Length) { power *= 2; selector++; }
            Put16(result, 6, power * 16); Put16(result, 8, selector); Put16(result, 10, tables.Length * 16 - power * 16);
            int cursor = 12 + tables.Length * 16, head = -1;
            for (int i = 0; i < tables.Length; i++)
            {
                var t = tables[i]; Array.Copy(source, t.Offset, result, cursor, t.Length);
                if (t.Tag == 0x68656164 && t.Length >= 12) { Put32(result, cursor + 8, 0); head = cursor; }
                int r = 12 + i * 16;
                Put32(result, r, t.Tag); Put32(result, r + 4, Sum(result, cursor, (t.Length + 3) & ~3));
                Put32(result, r + 8, (uint)cursor); Put32(result, r + 12, (uint)t.Length);
                cursor += (t.Length + 3) & ~3;
            }
            if (head >= 0) Put32(result, head + 8, unchecked(0xB1B0AFBAu - Sum(result, 0, result.Length)));
            return result;
        }
        private static uint Sum(byte[] data, int offset, int length)
        {
            uint sum = 0; for (int i = 0; i < length; i += 4) sum = unchecked(sum + U32(data, offset + i)); return sum;
        }
    }
}
