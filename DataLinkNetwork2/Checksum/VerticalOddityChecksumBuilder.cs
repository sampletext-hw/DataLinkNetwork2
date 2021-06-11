using System.Collections;
using System.Linq;
using DataLinkNetwork2.Abstractions;
using DataLinkNetwork2.BitArrayRoutine;

namespace DataLinkNetwork2.Checksum
{
    public class VerticalOddityChecksumBuilder : IChecksumBuilder
    {
        public BitArray Build(BitArray data)
        {
            BitArray result = new BitArray(C.ChecksumSize);

            if (data.Length == 0)
            {
                return result;
            }
                
            bool res = data[0];
            for (var i = 1; i < data.Count; i++)
            {
                res ^= data[i];
            }

            result[0] = res;
            return result;
        }
    }
}