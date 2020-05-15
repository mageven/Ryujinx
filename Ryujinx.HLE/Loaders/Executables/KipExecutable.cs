using LibHac;
using LibHac.Fs;
using LibHac.Loader;
using System;

namespace Ryujinx.HLE.Loaders.Executables
{
    class KipExecutable : Kip, IExecutable
    {
        public byte[] Program { get; }
        public Span<byte> Text => Program.AsSpan().Slice(TextOffset, (int)Header.Sections[0].DecompressedSize);
        public Span<byte> Ro   => Program.AsSpan().Slice(RoOffset,   (int)Header.Sections[1].DecompressedSize);
        public Span<byte> Data => Program.AsSpan().Slice(DataOffset, (int)Header.Sections[2].DecompressedSize);

        public int TextOffset => Header.Sections[0].OutOffset;
        public int RoOffset   => Header.Sections[1].OutOffset;
        public int DataOffset => Header.Sections[2].OutOffset;
        public int BssOffset  => Header.Sections[3].OutOffset;
        public int BssSize    => Header.Sections[3].DecompressedSize;

        public int[] Capabilities { get; }

        public KipExecutable(IStorage inStorage) : base(inStorage)
        {
            Capabilities = new int[32];

            for (int index = 0; index < Capabilities.Length; index++)
            {
                Capabilities[index] = BitConverter.ToInt32(Header.Capabilities, index * 4);
            }

            Program = new byte[Header.Sections[2].OutOffset + Header.Sections[2].DecompressedSize];

            DecompressSection(0).AsSpan().CopyTo(Text);
            DecompressSection(1).AsSpan().CopyTo(Ro);
            DecompressSection(2).AsSpan().CopyTo(Data);
        }
    }
}