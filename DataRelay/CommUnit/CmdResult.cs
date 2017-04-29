using System;
using System.Collections.Generic;
using System.Text;

namespace TSNY
{
    public enum CmdResult
    {
        Success, Fail,Unexpected,SetFail,
        OperationConflict,Disconnect,
        FrameSyncbitError, FrameCheckError,
        Timeout,
        SensorBoardCommunicationFail,
        MotoFreeze200, MotoFreeze800, MotoTurning, MotoAlreadyStart, MotoAlreadyStop,
       // MotoOweVoltage,
        CoeCheckFail, DirectionCheckWarning,
        MotoLessDemarcate,MotoNotDemarcate,MotoHaveDSQ
    }
    public enum CmdButtonResult
    {
        StartTest,StopTest,
        DrawArmMoto,EmitArmMoto,StopArmMoto,
        UpTurnMoto,DownTurnMoto,StopTurnMoto,
        AptitudeTurnMoto,StopAptitudeMoto,UsualTurn,DramTurn,
        MagnetismChecked
    }
}
