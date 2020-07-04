using System.Runtime.InteropServices;
namespace Ryujinx.HLE.HOS.Services.Audio.AudioRendererManager
{
    [StructLayout(LayoutKind.Sequential, Size = 0x18, Pack = 1)]
    struct MixVolume
    {
        public float vol1;
        public float vol2;
        public float vol3;
        public float vol4;
        public float vol5;
        public float vol6;
        public float vol7;
        public float vol8;
        public float vol9;
        public float vol10;
        public float vol11;
        public float vol12;
        public float vol13;
        public float vol14;
        public float vol15;
        public float vol16;
        public float vol17;
        public float vol18;
        public float vol19;
        public float vol20;
        public float vol21;
        public float vol22;
        public float vol23;
        public float vol24;
    }
}