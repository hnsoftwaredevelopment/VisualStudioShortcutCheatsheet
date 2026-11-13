using System;
using System.Runtime.InteropServices;

namespace ShortcutCheatsheet.CommandTables
{
    /// <summary>
    /// A compiled command table (.cto, the resource "Menus.ctmenu") starts with "CFCT" and is compressed with
    /// Xpress Huffman. Windows can decompress that format itself (ntdll), so there is no decompressor of our own.
    /// </summary>
    /// <remarks>
    /// Compressed layout: a header of 20 bytes ("CFCT", version, total uncompressed size, 20, 0), then blocks of at
    /// most 64 KB, each with a 16-byte header (4 bytes, uncompressed size, 4 bytes, compressed size) and the data.
    /// A table larger than 64 KB, such as one that includes KnownImageIds.vsct, has several blocks.
    /// The uncompressed table has the same signature and version, with 0 where the total size was.
    /// </remarks>
    internal static class CtoDecompressor
    {
        public const int SupportedVersion = 5;
        private const int Signature = 0x54434643; // "CFCT"
        private const int HeaderSize = 20;
        private const int BlockHeaderSize = 16;
        private const int MaxSize = 64 * 1024 * 1024;
        private const int MaxBlockSize = 64 * 1024;
        private const ushort CompressionFormatXpressHuff = 4;

        /// <summary>The uncompressed table. Throws FormatException for anything that is not a version 5 table.</summary>
        public static byte[] Decompress(byte[] cto)
        {
            if (cto == null)
                throw new ArgumentNullException(nameof(cto));
            CheckSignature(cto);

            int size = BitConverter.ToInt32(cto, 8);
            if (size == 0)
                return cto; // not compressed
            if (size < 12 || size > MaxSize || cto.Length < HeaderSize)
                throw new FormatException("The command table header has an invalid size.");

            uint status = RtlGetCompressionWorkSpaceSize(CompressionFormatXpressHuff, out uint workspaceSize, out _);
            if (status != 0)
                throw new FormatException($"Windows cannot decompress this format (0x{status:X8}).");
            var workspace = new byte[workspaceSize];

            var result = new byte[size];
            int written = 0, p = HeaderSize;
            while (written < size)
            {
                if (p + BlockHeaderSize > cto.Length)
                    throw new FormatException("The command table is truncated.");
                int blockSize = BitConverter.ToInt32(cto, p + 4);
                int packed = BitConverter.ToInt32(cto, p + 12);
                p += BlockHeaderSize;
                if (blockSize <= 0 || blockSize > MaxBlockSize || blockSize > size - written || packed <= 0 || packed > cto.Length - p)
                    throw new FormatException("A block of the command table has invalid sizes.");

                var source = new byte[packed];
                Buffer.BlockCopy(cto, p, source, 0, packed);
                var block = new byte[blockSize];
                status = RtlDecompressBufferEx(CompressionFormatXpressHuff, block, (uint)blockSize, source, (uint)packed, out uint final, workspace);
                if (status != 0 || final != blockSize)
                    throw new FormatException($"The command table could not be decompressed (0x{status:X8}).");
                Buffer.BlockCopy(block, 0, result, written, blockSize);
                written += blockSize;
                p += packed;
            }

            CheckSignature(result);
            return result;
        }

        private static void CheckSignature(byte[] data)
        {
            if (data.Length < 12 || BitConverter.ToInt32(data, 0) != Signature)
                throw new FormatException("Not a command table: the CFCT signature is missing.");
            int version = BitConverter.ToInt32(data, 4);
            if (version != SupportedVersion)
                throw new FormatException($"Command table version {version} is not supported.");
        }

        [DllImport("ntdll.dll")]
        private static extern uint RtlGetCompressionWorkSpaceSize(ushort format, out uint workspaceSize, out uint fragmentWorkspaceSize);

        [DllImport("ntdll.dll")]
        private static extern uint RtlDecompressBufferEx(ushort format, byte[] uncompressed, uint uncompressedSize,
            byte[] compressed, uint compressedSize, out uint finalSize, byte[] workspace);
    }
}
