using System;
using System.Collections.Generic;
using System.Linq;

namespace NewFilterBoard.CommUnit
{

    public enum CommOperations
    {
        GetVersion, DataTrans, CommStyle, GetData, WaitStart, StopSample,SamplePara,FirmHandshake,FirmPageWrite,FirmBlockErease,FirmBreake, None
    }
}
