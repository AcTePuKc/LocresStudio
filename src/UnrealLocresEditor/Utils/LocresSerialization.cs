using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace UnrealLocresEditor.Utils
{
    internal enum LocresVersion : byte
    {
        Legacy = 0,
        Compact = 1,
        Optimized = 2,
        CityHash = 3,
    }

    internal sealed class LocresEntryData
    {
        public required string NamespaceName { get; init; }
        public required string Key { get; init; }
        public required string Translation { get; set; }
        public required uint SourceHash { get; init; }
    }

    internal sealed class LocresNamespaceData
    {
        public required string Name { get; init; }
        public List<LocresEntryData> Entries { get; } = new();
    }

    internal sealed class LocresFileData
    {
        public const string KeySeparator = "/";

        private static readonly byte[] Magic =
        {
            0x0E, 0x14, 0x74, 0x75, 0x67, 0x4A, 0x03, 0xFC,
            0x4A, 0x15, 0x90, 0x9D, 0xC3, 0x37, 0x7F, 0x1B
        };

        public LocresVersion Version { get; set; } = LocresVersion.CityHash;
        public List<LocresNamespaceData> Namespaces { get; } = new();

        public IEnumerable<LocresEntryData> EnumerateEntries() => Namespaces.SelectMany(ns => ns.Entries);

        public static LocresFileData Read(string path)
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

            var file = new LocresFileData();
            var magic = reader.ReadBytes(Magic.Length);
            ulong stringsOffset = 0;

            if (magic.SequenceEqual(Magic))
            {
                file.Version = (LocresVersion)reader.ReadByte();
                stringsOffset = reader.ReadUInt64();
            }
            else
            {
                file.Version = LocresVersion.Legacy;
                stream.Position = 0;
            }

            var strings = new List<string>();
            if (file.Version >= LocresVersion.Compact)
            {
                stream.Position = (long)stringsOffset;
                var stringCount = reader.ReadUInt32();
                for (var i = 0; i < stringCount; i++)
                {
                    strings.Add(ReadUeString(reader));
                    if (file.Version >= LocresVersion.Optimized)
                    {
                        reader.ReadUInt32();
                    }
                }
            }

            stream.Position = file.Version >= LocresVersion.Compact ? 25 : 0;
            if (file.Version >= LocresVersion.Optimized)
            {
                reader.ReadUInt32();
            }

            var namespaceCount = reader.ReadUInt32();
            for (var i = 0; i < namespaceCount; i++)
            {
                if (file.Version >= LocresVersion.Optimized)
                {
                    reader.ReadUInt32();
                }

                var namespaceName = ReadUeString(reader);
                var namespaceData = new LocresNamespaceData { Name = namespaceName };
                var keyCount = reader.ReadUInt32();

                for (var j = 0; j < keyCount; j++)
                {
                    if (file.Version >= LocresVersion.Optimized)
                    {
                        reader.ReadUInt32();
                    }

                    var key = ReadUeString(reader);
                    var sourceHash = reader.ReadUInt32();
                    var translation = file.Version >= LocresVersion.Compact
                        ? strings[(int)reader.ReadUInt32()]
                        : ReadUeString(reader);

                    namespaceData.Entries.Add(new LocresEntryData
                    {
                        NamespaceName = namespaceName,
                        Key = key,
                        Translation = translation,
                        SourceHash = sourceHash,
                    });
                }

                file.Namespaces.Add(namespaceData);
            }

            return file;
        }

        public void Write(string path)
        {
            using var stream = File.Create(path);
            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

            var stringTable = BuildStringTable();

            if (Version >= LocresVersion.Compact)
            {
                writer.Write(Magic);
                writer.Write((byte)Version);
                writer.Write((ulong)0);
            }

            if (Version == LocresVersion.Legacy)
            {
                WriteLegacy(writer);
                return;
            }

            if (Version >= LocresVersion.Optimized)
            {
                writer.Write((uint)EnumerateEntries().Count());
            }

            writer.Write((uint)Namespaces.Count);
            foreach (var namespaceData in Namespaces)
            {
                if (Version == LocresVersion.CityHash)
                {
                    writer.Write(LocresCityHash.CityHash64Utf16ToUInt32(namespaceData.Name));
                }
                else if (Version >= LocresVersion.Optimized)
                {
                    writer.Write(LocresCrc32.StrCrc32(namespaceData.Name));
                }

                WriteUeString(writer, namespaceData.Name);
                writer.Write((uint)namespaceData.Entries.Count);

                foreach (var entry in namespaceData.Entries)
                {
                    if (Version == LocresVersion.CityHash)
                    {
                        writer.Write(LocresCityHash.CityHash64Utf16ToUInt32(entry.Key));
                    }
                    else if (Version >= LocresVersion.Optimized)
                    {
                        writer.Write(LocresCrc32.StrCrc32(entry.Key));
                    }

                    WriteUeString(writer, entry.Key);
                    writer.Write(entry.SourceHash);
                    writer.Write((uint)stringTable.Indexes[entry.Translation]);
                }
            }

            var stringsOffset = stream.Position;
            stream.Position = 17;
            writer.Write((ulong)stringsOffset);
            stream.Position = stringsOffset;

            writer.Write((uint)stringTable.OrderedStrings.Count);
            foreach (var translation in stringTable.OrderedStrings)
            {
                WriteUeString(writer, translation);
                if (Version >= LocresVersion.Optimized)
                {
                    writer.Write((uint)stringTable.ReferenceCounts[translation]);
                }
            }
        }

        public static string ComposeDisplayKey(string namespaceName, string key)
        {
            return string.IsNullOrEmpty(namespaceName)
                ? key
                : $"{namespaceName}{KeySeparator}{key}";
        }

        public static (string NamespaceName, string Key) ParseDisplayKey(string displayKey)
        {
            if (string.IsNullOrWhiteSpace(displayKey))
            {
                return (string.Empty, string.Empty);
            }

            var separatorIndex = displayKey.IndexOf(KeySeparator, StringComparison.Ordinal);
            if (separatorIndex < 0)
            {
                return (string.Empty, displayKey.Trim());
            }

            if (separatorIndex == 0)
            {
                return (string.Empty, displayKey.Substring(1).Trim());
            }

            return (
                displayKey.Substring(0, separatorIndex).Trim(),
                displayKey.Substring(separatorIndex + 1).Trim()
            );
        }

        private void WriteLegacy(BinaryWriter writer)
        {
            writer.Write((uint)Namespaces.Count);
            foreach (var namespaceData in Namespaces)
            {
                WriteUeString(writer, namespaceData.Name, forceUnicode: true);
                writer.Write((uint)namespaceData.Entries.Count);
                foreach (var entry in namespaceData.Entries)
                {
                    WriteUeString(writer, entry.Key);
                    writer.Write(entry.SourceHash);
                    WriteUeString(writer, entry.Translation);
                }
            }
        }

        private (List<string> OrderedStrings, Dictionary<string, int> Indexes, Dictionary<string, int> ReferenceCounts) BuildStringTable()
        {
            var orderedStrings = new List<string>();
            var indexes = new Dictionary<string, int>(StringComparer.Ordinal);
            var referenceCounts = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var entry in EnumerateEntries())
            {
                var translation = entry.Translation ?? string.Empty;
                if (indexes.ContainsKey(translation))
                {
                    referenceCounts[translation]++;
                    continue;
                }

                indexes[translation] = orderedStrings.Count;
                referenceCounts[translation] = 1;
                orderedStrings.Add(translation);
            }

            return (orderedStrings, indexes, referenceCounts);
        }

        private static string ReadUeString(BinaryReader reader)
        {
            var length = reader.ReadInt32();
            if (length == 0)
            {
                return string.Empty;
            }

            if (length > 0)
            {
                var bytes = reader.ReadBytes(length);
                return Encoding.ASCII.GetString(bytes).TrimEnd('\0');
            }

            var unicodeBytes = reader.ReadBytes(-length * 2);
            return Encoding.Unicode.GetString(unicodeBytes).TrimEnd('\0');
        }

        private static void WriteUeString(BinaryWriter writer, string value, bool forceUnicode = false)
        {
            value ??= string.Empty;
            var terminated = value + '\0';

            if (!forceUnicode && terminated.All(c => c <= sbyte.MaxValue))
            {
                writer.Write((uint)terminated.Length);
                writer.Write(Encoding.ASCII.GetBytes(terminated));
                return;
            }

            var unicodeBytes = Encoding.Unicode.GetBytes(terminated);
            writer.Write(-(unicodeBytes.Length / 2));
            writer.Write(unicodeBytes);
        }
    }

    internal static class LocresCrc32
    {
        private static readonly uint[] CrcTable =
        {
            0x00000000, 0x77073096, 0xee0e612c, 0x990951ba, 0x076dc419, 0x706af48f, 0xe963a535, 0x9e6495a3,
            0x0edb8832, 0x79dcb8a4, 0xe0d5e91e, 0x97d2d988, 0x09b64c2b, 0x7eb17cbd, 0xe7b82d07, 0x90bf1d91,
            0x1db71064, 0x6ab020f2, 0xf3b97148, 0x84be41de, 0x1adad47d, 0x6ddde4eb, 0xf4d4b551, 0x83d385c7,
            0x136c9856, 0x646ba8c0, 0xfd62f97a, 0x8a65c9ec, 0x14015c4f, 0x63066cd9, 0xfa0f3d63, 0x8d080df5,
            0x3b6e20c8, 0x4c69105e, 0xd56041e4, 0xa2677172, 0x3c03e4d1, 0x4b04d447, 0xd20d85fd, 0xa50ab56b,
            0x35b5a8fa, 0x42b2986c, 0xdbbbc9d6, 0xacbcf940, 0x32d86ce3, 0x45df5c75, 0xdcd60dcf, 0xabd13d59,
            0x26d930ac, 0x51de003a, 0xc8d75180, 0xbfd06116, 0x21b4f4b5, 0x56b3c423, 0xcfba9599, 0xb8bda50f,
            0x2802b89e, 0x5f058808, 0xc60cd9b2, 0xb10be924, 0x2f6f7c87, 0x58684c11, 0xc1611dab, 0xb6662d3d,
            0x76dc4190, 0x01db7106, 0x98d220bc, 0xefd5102a, 0x71b18589, 0x06b6b51f, 0x9fbfe4a5, 0xe8b8d433,
            0x7807c9a2, 0x0f00f934, 0x9609a88e, 0xe10e9818, 0x7f6a0dbb, 0x086d3d2d, 0x91646c97, 0xe6635c01,
            0x6b6b51f4, 0x1c6c6162, 0x856530d8, 0xf262004e, 0x6c0695ed, 0x1b01a57b, 0x8208f4c1, 0xf50fc457,
            0x65b0d9c6, 0x12b7e950, 0x8bbeb8ea, 0xfcb9887c, 0x62dd1ddf, 0x15da2d49, 0x8cd37cf3, 0xfbd44c65,
            0x4db26158, 0x3ab551ce, 0xa3bc0074, 0xd4bb30e2, 0x4adfa541, 0x3dd895d7, 0xa4d1c46d, 0xd3d6f4fb,
            0x4369e96a, 0x346ed9fc, 0xad678846, 0xda60b8d0, 0x44042d73, 0x33031de5, 0xaa0a4c5f, 0xdd0d7cc9,
            0x5005713c, 0x270241aa, 0xbe0b1010, 0xc90c2086, 0x5768b525, 0x206f85b3, 0xb966d409, 0xce61e49f,
            0x5edef90e, 0x29d9c998, 0xb0d09822, 0xc7d7a8b4, 0x59b33d17, 0x2eb40d81, 0xb7bd5c3b, 0xc0ba6cad,
            0xedb88320, 0x9abfb3b6, 0x03b6e20c, 0x74b1d29a, 0xead54739, 0x9dd277af, 0x04db2615, 0x73dc1683,
            0xe3630b12, 0x94643b84, 0x0d6d6a3e, 0x7a6a5aa8, 0xe40ecf0b, 0x9309ff9d, 0x0a00ae27, 0x7d079eb1,
            0xf00f9344, 0x8708a3d2, 0x1e01f268, 0x6906c2fe, 0xf762575d, 0x806567cb, 0x196c3671, 0x6e6b06e7,
            0xfed41b76, 0x89d32be0, 0x10da7a5a, 0x67dd4acc, 0xf9b9df6f, 0x8ebeeff9, 0x17b7be43, 0x60b08ed5,
            0xd6d6a3e8, 0xa1d1937e, 0x38d8c2c4, 0x4fdff252, 0xd1bb67f1, 0xa6bc5767, 0x3fb506dd, 0x48b2364b,
            0xd80d2bda, 0xaf0a1b4c, 0x36034af6, 0x41047a60, 0xdf60efc3, 0xa867df55, 0x316e8eef, 0x4669be79,
            0xcb61b38c, 0xbc66831a, 0x256fd2a0, 0x5268e236, 0xcc0c7795, 0xbb0b4703, 0x220216b9, 0x5505262f,
            0xc5ba3bbe, 0xb2bd0b28, 0x2bb45a92, 0x5cb36a04, 0xc2d7ffa7, 0xb5d0cf31, 0x2cd99e8b, 0x5bdeae1d,
            0x9b64c2b0, 0xec63f226, 0x756aa39c, 0x026d930a, 0x9c0906a9, 0xeb0e363f, 0x72076785, 0x05005713,
            0x95bf4a82, 0xe2b87a14, 0x7bb12bae, 0x0cb61b38, 0x92d28e9b, 0xe5d5be0d, 0x7cdcefb7, 0x0bdbdf21,
            0x86d3d2d4, 0xf1d4e242, 0x68ddb3f8, 0x1fda836e, 0x81be16cd, 0xf6b9265b, 0x6fb077e1, 0x18b74777,
            0x88085ae6, 0xff0f6a70, 0x66063bca, 0x11010b5c, 0x8f659eff, 0xf862ae69, 0x616bffd3, 0x166ccf45,
            0xa00ae278, 0xd70dd2ee, 0x4e048354, 0x3903b3c2, 0xa7672661, 0xd06016f7, 0x4969474d, 0x3e6e77db,
            0xaed16a4a, 0xd9d65adc, 0x40df0b66, 0x37d83bf0, 0xa9bcae53, 0xdebb9ec5, 0x47b2cf7f, 0x30b5ffe9,
            0xbdbdf21c, 0xcabac28a, 0x53b39330, 0x24b4a3a6, 0xbad03605, 0xcdd70693, 0x54de5729, 0x23d967bf,
            0xb3667a2e, 0xc4614ab8, 0x5d681b02, 0x2a6f2b94, 0xb40bbe37, 0xc30c8ea1, 0x5a05df1b, 0x2d02ef8d
        };

        public static uint StrCrc32(string value, uint crc = 0)
        {
            crc = ~crc;
            foreach (var rune in value.EnumerateRunes())
            {
                var ch = (uint)rune.Value;
                for (var i = 0; i < 4; i++)
                {
                    crc = (crc >> 8) ^ CrcTable[(crc ^ ch) & 0xFF];
                    ch >>= 8;
                }
            }

            return ~crc;
        }
    }

    internal static class LocresCityHash
    {
        private const ulong K0 = 0xc3a5c85c97cb3127UL;
        private const ulong K1 = 0xb492b66fbe98f273UL;
        private const ulong K2 = 0x9ae16a3b2f90404fUL;
        private const ulong HashMul = 0x9ddfea08eb382d69UL;

        public static uint CityHash64Utf16ToUInt32(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return 0;
            }

            var hash = CityHash64(Encoding.Unicode.GetBytes(value));
            return unchecked((uint)((uint)hash + ((uint)(hash >> 32) * 23)));
        }

        private static ulong CityHash64(byte[] data)
        {
            var length = data.Length;
            if (length <= 32)
            {
                return length <= 16 ? HashLen0To16(data, 0, length) : HashLen17To32(data, length);
            }

            if (length <= 64)
            {
                return HashLen33To64(data, length);
            }

            var x = Fetch64(data, length - 40);
            var y = Fetch64(data, length - 16) + Fetch64(data, length - 56);
            var z = HashLen16(Fetch64(data, length - 48) + (ulong)length, Fetch64(data, length - 24));

            var v = WeakHashLen32WithSeeds(data, length - 64, (ulong)length, z);
            var w = WeakHashLen32WithSeeds(data, length - 32, y + K1, x);
            x = unchecked(x * K1 + Fetch64(data, 0));

            var offset = 0;
            var chunkLength = (length - 1) & ~63;
            while (chunkLength != 0)
            {
                x = unchecked(Rotate(x + y + v.Low + Fetch64(data, offset + 8), 37) * K1);
                y = unchecked(Rotate(y + v.High + Fetch64(data, offset + 48), 42) * K1);
                x ^= w.High;
                y = unchecked(y + v.Low + Fetch64(data, offset + 40));
                z = unchecked(Rotate(z + w.Low, 33) * K1);

                v = WeakHashLen32WithSeeds(data, offset, unchecked(v.High * K1), unchecked(x + w.Low));
                w = WeakHashLen32WithSeeds(data, offset + 32, unchecked(z + w.High), unchecked(y + Fetch64(data, offset + 16)));
                (z, x) = (x, z);
                offset += 64;
                chunkLength -= 64;
            }

            return HashLen16(
                HashLen16(v.Low, w.Low) + ShiftMix(y) * K1 + z,
                HashLen16(v.High, w.High) + x
            );
        }

        private static ulong HashLen0To16(byte[] data, int offset, int length)
        {
            if (length >= 8)
            {
                var mul = K2 + (ulong)length * 2;
                var a = Fetch64(data, offset) + K2;
                var b = Fetch64(data, offset + length - 8);
                var c = unchecked(Rotate(b, 37) * mul + a);
                var d = unchecked((Rotate(a, 25) + b) * mul);
                return HashLen16(c, d, mul);
            }

            if (length >= 4)
            {
                var mul = K2 + (ulong)length * 2;
                return HashLen16((ulong)length + (Fetch32(data, offset) << 3), Fetch32(data, offset + length - 4), mul);
            }

            if (length > 0)
            {
                var a = data[offset];
                var b = data[offset + (length >> 1)];
                var c = data[offset + length - 1];
                var y = (uint)(a + (b << 8));
                var z = (uint)(length + (c << 2));
                return unchecked(ShiftMix(y * K2 ^ z * K0) * K2);
            }

            return K2;
        }

        private static ulong HashLen17To32(byte[] data, int length)
        {
            var mul = K2 + (ulong)length * 2;
            var a = unchecked(Fetch64(data, 0) * K1);
            var b = Fetch64(data, 8);
            var c = unchecked(Fetch64(data, length - 8) * mul);
            var d = unchecked(Fetch64(data, length - 16) * K2);

            return HashLen16(
                unchecked(Rotate(a + b, 43) + Rotate(c, 30) + d),
                unchecked(a + Rotate(b + K2, 18) + c),
                mul
            );
        }

        private static ulong HashLen33To64(byte[] data, int length)
        {
            var mul = K2 + (ulong)length * 2;
            var a = unchecked(Fetch64(data, 0) * K2);
            var b = Fetch64(data, 8);
            var c = Fetch64(data, length - 24);
            var d = Fetch64(data, length - 32);
            var e = unchecked(Fetch64(data, 16) * K2);
            var f = unchecked(Fetch64(data, 24) * 9);
            var g = Fetch64(data, length - 8);
            var h = unchecked(Fetch64(data, length - 16) * mul);

            var u = unchecked(Rotate(a + g, 43) + (Rotate(b, 30) + c) * 9);
            var v = unchecked((a + g) ^ d) + f + 1;
            var w = unchecked(ByteSwap((u + v) * mul) + h);
            var x = unchecked(Rotate(e + f, 42) + c);
            var y = unchecked(ByteSwap((v + w) * mul) + g) * mul;
            var z = unchecked(e + f + c);
            a = unchecked(ByteSwap((x + z) * mul + y) + b);
            b = unchecked(ShiftMix((z + a) * mul + d + h) * mul);
            return unchecked(b + x);
        }

        private static (ulong Low, ulong High) WeakHashLen32WithSeeds(byte[] data, int offset, ulong a, ulong b)
        {
            var w = Fetch64(data, offset);
            var x = Fetch64(data, offset + 8);
            var y = Fetch64(data, offset + 16);
            var z = Fetch64(data, offset + 24);

            a += w;
            b = Rotate(b + a + z, 21);
            var c = a;
            a += x + y;
            b += Rotate(a, 44);
            return (unchecked(a + z), unchecked(b + c));
        }

        private static ulong HashLen16(ulong u, ulong v, ulong? mul = null)
        {
            if (mul == null)
            {
                return Hash128To64(u, v);
            }

            var a = unchecked((u ^ v) * mul.Value);
            a ^= a >> 47;
            var b = unchecked((v ^ a) * mul.Value);
            b ^= b >> 47;
            b = unchecked(b * mul.Value);
            return b;
        }

        private static ulong Hash128To64(ulong first, ulong second)
        {
            var a = unchecked((first ^ second) * HashMul);
            a ^= a >> 47;
            var b = unchecked((second ^ a) * HashMul);
            b ^= b >> 47;
            b = unchecked(b * HashMul);
            return b;
        }

        private static uint Fetch32(byte[] data, int index) => BitConverter.ToUInt32(data, index);
        private static ulong Fetch64(byte[] data, int index) => BitConverter.ToUInt64(data, index);
        private static ulong Rotate(ulong value, int shift) => shift == 0 ? value : (value >> shift) | (value << (64 - shift));
        private static ulong ShiftMix(ulong value) => value ^ (value >> 47);
        private static ulong ByteSwap(ulong value)
        {
            return
                (value >> 56) |
                ((value & 0x00FF000000000000UL) >> 40) |
                ((value & 0x0000FF0000000000UL) >> 24) |
                ((value & 0x000000FF00000000UL) >> 8) |
                ((value & 0x00000000FF000000UL) << 8) |
                ((value & 0x0000000000FF0000UL) << 24) |
                ((value & 0x000000000000FF00UL) << 40) |
                (value << 56);
        }
    }
}
