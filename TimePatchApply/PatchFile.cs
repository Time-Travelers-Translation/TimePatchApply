using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Komponent.IO;
using Komponent.IO.Streams;

namespace TimePatchApply
{
    class PatchFile
    {
        private Stream _fileStream;
        private IDictionary<string, PatchEntry> _entries;

        private PatchFile(Stream fileStream, IDictionary<string, PatchEntry> entries)
        {
            _fileStream = fileStream;
            _entries = entries;
        }

        public static PatchFile Open(string path)
        {
            var fileStream = File.OpenRead(path);
            using var br = new BinaryReaderX(fileStream, true);

            // Identify patch file
            if (br.ReadString(4) != "DIFF")
            {
                fileStream.Close();
                throw new InvalidOperationException("Given file is not a patch file.");
            }

            // Read entries
            var fileOffset = br.ReadInt32();
            var stringOffset = br.ReadInt32();
            var count = br.ReadInt32();

            fileStream.Position = fileOffset;
            var entries = ReadEntries(br, count);

            fileStream.Position = stringOffset;
            var paths = new List<string>();
            for (var i = 0; i < count; i++)
                paths.Add(br.ReadCStringASCII());

            // Create patch file instance
            return new PatchFile(fileStream, paths.Zip(entries).ToDictionary(x => x.First, y => y.Second));
        }

        public bool HasPatch(string path)
        {
            return _entries.ContainsKey(path);
        }

        public Stream GetPatch(string path)
        {
            if (!_entries.ContainsKey(path))
                throw new InvalidOperationException($"Path \"{path}\" has no patch.");

            using var patchStream = new SubStream(_fileStream, _entries[path].offset, _entries[path].length);
            using var deflateStream = new DeflateStream(patchStream, CompressionMode.Decompress);

            var output = new MemoryStream();
            deflateStream.CopyTo(output);

            output.Position = 0;
            return output;
        }

        private static PatchEntry[] ReadEntries(BinaryReaderX br, int count)
        {
            var result = new PatchEntry[count];

            for (var i = 0; i < count; i++)
                result[i] = ReadEntry(br);

            return result;
        }

        private static PatchEntry ReadEntry(BinaryReaderX br)
        {
            return new PatchEntry
            {
                offset = br.ReadInt32(),
                length = br.ReadInt32()
            };
        }
    }

    struct PatchEntry
    {
        public int offset;
        public int length;
    }
}
