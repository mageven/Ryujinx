using System.Runtime.InteropServices;

namespace Ryujinx.HLE.HOS.Services.Audio.AudioRendererManager
{
    [StructLayout(LayoutKind.Sequential, Size = 0x70, Pack = 1)]
    struct VoiceChannelResourceIn
    {
        public int id;
        public MixVolume mix_volume;
        bool is_used;
    }
}