﻿namespace DataLinkNetwork2
{
    public static class C
    {
        public const int ChecksumSize = 8;
        public const int FlagSize = 8;
        public const int AddressSize = 8;
        public const int ControlSize = 16;
        public const int WindowSize = 5;

        public const int MaxFrameDataSize = 64;

        public const bool SLog = true;
        public const bool RLog = true;
        
        public const long SendTimeoutMilliseconds = 5000;
    }
}