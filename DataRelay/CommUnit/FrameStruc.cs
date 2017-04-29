using System;
using System.Collections.Generic;
using System.Linq;

namespace CommUnit
{

    public class FrameStruc
    {
        public byte tidl,tidh,sidl,sidh,head1, head2, addr1, addr2, cmd, check1, check2;
        public UInt16 datalen, datalentem, datalencn;
        public byte[] databuf = new byte[2048];
    }
}
