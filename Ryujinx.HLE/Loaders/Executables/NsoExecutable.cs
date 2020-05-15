using LibHac;
using LibHac.Fs;
using System;

namespace Ryujinx.HLE.Loaders.Executables
{
    class NsoExecutable : Nso, IExecutable
    {
        public byte[] Program { get; }
        public Span<byte> Text => Program.AsSpan().Slice(TextOffset, (int)Sections[0].DecompressedSize);
        public Span<byte> Ro   => Program.AsSpan().Slice(RoOffset,   (int)Sections[1].DecompressedSize);
        public Span<byte> Data => Program.AsSpan().Slice(DataOffset, (int)Sections[2].DecompressedSize);

        public int TextOffset  => (int)Sections[0].MemoryOffset;
        public int RoOffset    => (int)Sections[1].MemoryOffset;
        public int DataOffset  => (int)Sections[2].MemoryOffset;
        public int BssOffset   => DataOffset + Data.Length;
        public new int BssSize => (int)base.BssSize;

        public NsoExecutable(IStorage inStorage) : base(inStorage)
        {
            Program = new byte[Sections[2].MemoryOffset + Sections[2].DecompressedSize];

            Sections[0].DecompressSection().AsSpan().CopyTo(Text);
            Sections[1].DecompressSection().AsSpan().CopyTo(Ro);
            Sections[2].DecompressSection().AsSpan().CopyTo(Data);
        }
    }
}