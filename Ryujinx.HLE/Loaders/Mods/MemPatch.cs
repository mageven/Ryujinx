using Ryujinx.Cpu;
using System;
using System.Collections.Generic;
using System.Linq;

using Ryujinx.Common.Logging;

namespace Ryujinx.HLE.Loaders.Mods
{
    public class MemPatch
    {
        readonly Dictionary<uint, byte[]> _patches = new Dictionary<uint, byte[]>();

        /// <summary>
        /// Adds a patch to specified offset. Overwrites if already present. 
        /// </summary>
        /// <param name="offset">Memory offset</param>
        /// <param name="patch">The patch to add</param>
        public void Add(uint offset, byte[] patch)
        {
            _patches[offset] = patch;
        }

        /// <summary>
        /// Adds all patches from an existing mempatch
        /// </summary>
        /// <param name="patches">The MemPatch patches to add</param>
        public void AddFrom(MemPatch patches)
        {
            if (patches == null)
            {
                return;
            }

            foreach (var (patchOffset, patch) in patches._patches)
            {
                _patches[patchOffset] = patch;
            }
        }

        /// <summary>
        /// Adds a patch in the form of an RLE/Fill mode.
        /// </summary>
        /// <param name="offset">Memory offset</param>
        /// <param name="length"The fill length</param>
        /// <param name="filler">The byte to fill</param>
        public void AddFill(uint offset, int length, byte filler)
        {
            // TODO: Can be made space efficient by changing `_patches`
            // Should suffice for now
            byte[] patch = new byte[length];
            patch.AsSpan().Fill(filler);
            _patches[offset] = patch;
        }

        /// <summary>
        /// Applies all the patches added to this instance via the IMemoryManager.
        /// </summary>
        /// <remarks>
        /// Patches are applied in ascending order of offsets to guarantee
        /// overlapping patches always apply the same way.
        /// </remarks>
        /// <param name="memory">The span of bytes to patch</param>
        /// <param name="maxSize">The maximum size of the slice of patchable memory</param>
        /// <param name="protectedOffset">A secondary offset used in special cases (NSO header)</param>
        public void Apply(Span<byte> memory, int protectedOffset = 0)
        {
            foreach (var (offset, patch) in _patches.OrderBy(item => item.Key))
            {
                int patchOffset = (int)offset;
                int patchSize = patch.Length;

                if (patchOffset < protectedOffset)
                {
                    continue; // Add warning?
                }

                patchOffset -= protectedOffset;

                if (patchOffset + patchSize > memory.Length)
                {
                    patchSize = memory.Length - (int)patchOffset; // Add warning?
                }

                Logger.PrintInfo(LogClass.Loader, $"Patching address .text+{patchOffset:x} <= {BitConverter.ToString(patch).Replace('-', ' ')} len={patchSize}");

                patch.AsSpan().Slice(0, patchSize).CopyTo(memory.Slice(patchOffset, patchSize));
            }
        }
    }
}