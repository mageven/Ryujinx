using LibHac.Fs;
using LibHac.Loader;
using System;

namespace Ryujinx.HLE.Loaders.Executables
{
    class KipExecutable : KipReader, IExecutable
    {
        public byte[] Program { get; }
        public Span<byte> Text => Program.AsSpan().Slice(TextOffset, Segments[0].Size);
        public Span<byte> Ro   => Program.AsSpan().Slice(RoOffset,   Segments[1].Size);
        public Span<byte> Data => Program.AsSpan().Slice(DataOffset, Segments[2].Size);

        public int TextOffset => Segments[0].MemoryOffset;
        public int RoOffset   => Segments[1].MemoryOffset;
        public int DataOffset => Segments[2].MemoryOffset;
        public int BssOffset  => Segments[3].MemoryOffset;
        public int BssSize    => Segments[3].Size;

        public new int[] Capabilities { get; }

        public KipExecutable(IFile kipFile)
        {
            Initialize(kipFile);

            for (int i = 0; i < base.Capabilities.Length; ++i)
            {
                Capabilities[i] = (int)base.Capabilities[i];
            }

            Program = new byte[Segments[2].MemoryOffset + Segments[2].Size];

            ReadSegment(SegmentType.Text, Text);
            ReadSegment(SegmentType.Ro, Ro);
            ReadSegment(SegmentType.Data, Data);
        }
    }
}