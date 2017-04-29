using System;
using System.Collections.Generic;
using System.Linq;

namespace NewFilterBoard.CommUnit
{

    public enum CommResult
    {
        Success, USBOpenDeviceError, USBDisconnect, ReadFrameBufError, ReadFrameBufLenthError,
        SendFrameBufError, USBSendLenthOve, USBSetTimeoutError, ReadFrameHeadError,
        WIFINotOpen, BluetoothNotOpen, BluetoothSendFail, BluetoothReceiveTimeout, RS232NotOpen, RS232ReceiveTimeout, WIFIReceiveTimeout, OperationConflict, Exception,
        SendFrameFail, USBReceiveTimeout, DataCheckError, ReturnCmdError, DataLenError, CrcCheckerror,
        FirmHandshakeFalse, FirmPageWriteFalse, FirmPageWriteLenErr, FirmPageWriteIndexErr,FirmBlockEreaseFalse, FirmBreakeFalse,
        ParaError, Processing, ThreadBusy, FrameNotSend, FrameNotReceived, WIFISendFail, JXYCheckCError
    }
}
