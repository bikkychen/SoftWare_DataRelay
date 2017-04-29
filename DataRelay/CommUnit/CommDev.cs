using System;
using System.Threading;
using System.Windows;
using System.ComponentModel;
using System.Diagnostics;

namespace TSNY
{
    public interface I_Comm
    {
        //接口只提供方法的签名，其它继承于它的类也可以称为接口，继承于接口的类必须实现接口中定义的所有属性、方法和事件
        //接口是处理相似任务的最好方法，如果有几个类要继承同一个接口干相近似的事，则可以用接口，如果只有一类继承一个接口，则意义不大，可直接在类中实现相关方法或事件
        //接口不能实例化，即不能new，但可以申明接口变量并赋值，没有构造和析构函数，

        bool InitDevice();//初始化设备,并配置通讯接口,在接口中只定义不实现，即只有签名没有实现代码

        bool SearchDevice();//查找设备,在接口中只定义不实现，即只有签名没有实现代码

        bool OpenDevice();//打开通讯设备,在接口中只定义不实现，即只有签名没有实现代码

        bool CloseDevice();//关闭通讯设备,在接口中只定义不实现，即只有签名没有实现代码

        bool IsConnected { get; }//连接状态,接口中只定义属性，不实现属性

        event EventHandler OnConnected;//此为系统自带的委托，在接口中也只定义不实现，即只有签名没有实现代码

        event EventHandler OnDisconnected;//此为系统自带的委托，在接口中也只定义不实现，即只有签名没有实现代码
    }

    public enum CommResult
    {
        Success, USBOpenDeviceError, USBDisconnect,ReadFrameBufError, ReadFrameBufLenthError,
        SendFrameBufError,USBSendLenthOve,USBSetTimeoutError,ReadFrameHeadError,
        WIFINotOpen,BluetoothNotOpen, BluetoothSendFail, BluetoothReceiveTimeout,RS232NotOpen, RS232ReceiveTimeout,WIFIReceiveTimeout,OperationConflict, Exception,
        SendFrameFail, USBReceiveTimeout, DMXCheckError, ReturnCmdError, DataLenError,CrcCheckerror,
        ParaError,Processing, ThreadBusy,FrameNotSend,FrameNotReceived, WIFISendUDPFail,JXYCheckCError
    }

    public enum CommOperations
    {
        DMXGetVersion, ZKBGetVersion, JXYReset, JXYGetVer, JXYCableVol, JXYTpAmplitude,JXYTpWaveform, JXYSamplePT, JXYSampleF, JXYMotor1P, JXYMotor1N, JXYMotor2P,
        JXYMotor2N, JXYMotorStop, JXYGetMotorStatus, JXYSampleAll, JXYGetCoefficient, JXYSetCoefficient, JXYFormat, JXYGetTestInfo,
        JXYGetTestData, JXYGetCoefficient2, JXYSetCoefficient2, JXYGetCoefficientAll,JXYGetMotorThr, JXYSetMotor1Thr, JXYSetMotor2Thr, JXYOpenMotorPwr,
        DMXSetGear, DMXGetGear, DMXGetWaveform,DMXSetBaud,DMXGetBaud, JXYGetInsInfo, JXYSetInsInfo ,None
    }

    public class CommDev : I_Comm
    {
        #region 高精度定时所用系统函数和相关变量
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        extern static short QueryPerformanceCounter(ref long x);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        extern static short QueryPerformanceFrequency(ref long x);
        long Frequency = 0;
        long Counter = 0, Counter2 = 0;
        #endregion

        #region 类成员变量和构造函数
        private IntPtr _usbHandle;//设备句柄
        private uint _deviceIndex;//设备号，当有N个设备同时连接PC时，设备号从0到N-1自动排列
        public MessageForm userMessage = new MessageForm();//消息窗体
        private byte[] CmdReturnBytes = { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        public byte[] VerStr = new byte[64];

        public byte[] CmdReturn
        {
            get { return this.CmdReturnBytes; }
        }

        public uint DeviceIndex
        {
            get { return _deviceIndex; }
            set { _deviceIndex = value; }
        }

        MainWindow _mw;
        BackgroundWorker bgWorker_DelData = new BackgroundWorker();//实例化后台工作者线程，用于整机格式化的长耗时操作
        BackgroundWorker bgWorker_GetPT = new BackgroundWorker();//实例化后台工作者线程，用于读取压力系数
        BackgroundWorker bgWorker_SetPT = new BackgroundWorker();//实例化后台工作者线程，用于下发压力系数
        BackgroundWorker bgWorker_GetF = new BackgroundWorker();//实例化后台工作者线程，用于提取流量系数
        BackgroundWorker bgWorker_SetF = new BackgroundWorker();//实例化后台工作者线程，用于下发流量系数
        BackgroundWorker bgWorker_GetPTData = new BackgroundWorker();//实例化后台工作者线程，用于提取压力标定数据
        BackgroundWorker bgWorker_GetPTF = new BackgroundWorker();//实例化后台工作者线程，用于提取全部系数
        BackgroundWorker bgWorker_GetTestInfo = new BackgroundWorker();//实例化后台工作者线程，用于提取测试信息
        BackgroundWorker bgWorker_GetWaveform = new BackgroundWorker();//实例化后台工作者线程，用于提取信号波形
        BackgroundWorker bgWorker_GetTpWaveform = new BackgroundWorker();//实例化后台工作者线程，用于提取信号波形
        BackgroundWorker bgWorker_GetInsInfo = new BackgroundWorker();//实例化后台工作者线程，
        BackgroundWorker bgWorker_SetInsInfo = new BackgroundWorker();//实例化后台工作者线程 

        public CommDev(MainWindow mw)//类构造函数
        {
            _isConnected = false;
            _usbHandle = new IntPtr(-1);
            _deviceIndex = 0;
            _mw = mw;

            bgWorker_DelData.WorkerReportsProgress = true;//允许报告线程进度
            bgWorker_DelData.WorkerSupportsCancellation = true;//线程允许中止取消
            bgWorker_DelData.DoWork += DoWork_Handler_DelData;//注册线程开始方法
            bgWorker_DelData.ProgressChanged += ProgressChanged_Handler_DelData;//注册报告线程进度方法
            bgWorker_DelData.RunWorkerCompleted += RunWorkerCompleted_Handler_DelData;//注册线程结束方法

            bgWorker_GetPT.WorkerReportsProgress = true;//允许报告线程进度
            bgWorker_GetPT.WorkerSupportsCancellation = true;//线程允许中止取消
            bgWorker_GetPT.DoWork += DoWork_Handler_GetPT;//注册线程开始方法
            bgWorker_GetPT.ProgressChanged += ProgressChanged_Handler_GetPT;//注册报告线程进度方法
            bgWorker_GetPT.RunWorkerCompleted += RunWorkerCompleted_Handler_GetPT;//注册线程结束方法

            bgWorker_SetPT.WorkerReportsProgress = true;//允许报告线程进度
            bgWorker_SetPT.WorkerSupportsCancellation = true;//线程允许中止取消
            bgWorker_SetPT.DoWork += DoWork_Handler_SetPT;//注册线程开始方法
            bgWorker_SetPT.ProgressChanged += ProgressChanged_Handler_SetPT;//注册报告线程进度方法
            bgWorker_SetPT.RunWorkerCompleted += RunWorkerCompleted_Handler_SetPT;//注册线程结束方法

            bgWorker_GetF.WorkerReportsProgress = true;//允许报告线程进度
            bgWorker_GetF.WorkerSupportsCancellation = true;//线程允许中止取消
            bgWorker_GetF.DoWork += DoWork_Handler_GetF;//注册线程开始方法
            bgWorker_GetF.ProgressChanged += ProgressChanged_Handler_GetF;//注册报告线程进度方法
            bgWorker_GetF.RunWorkerCompleted += RunWorkerCompleted_Handler_GetF;//注册线程结束方法

            bgWorker_SetF.WorkerReportsProgress = true;//允许报告线程进度
            bgWorker_SetF.WorkerSupportsCancellation = true;//线程允许中止取消
            bgWorker_SetF.DoWork += DoWork_Handler_SetF;//注册线程开始方法
            bgWorker_SetF.ProgressChanged += ProgressChanged_Handler_SetF;//注册报告线程进度方法
            bgWorker_SetF.RunWorkerCompleted += RunWorkerCompleted_Handler_SetF;//注册线程结束方法

            bgWorker_GetPTData.WorkerReportsProgress = true;//允许报告线程进度
            bgWorker_GetPTData.WorkerSupportsCancellation = true;//线程允许中止取消
            bgWorker_GetPTData.DoWork += DoWork_Handler_GetPTData;//注册线程开始方法
            bgWorker_GetPTData.ProgressChanged += ProgressChanged_Handler_GetPTData;//注册报告线程进度方法
            bgWorker_GetPTData.RunWorkerCompleted += RunWorkerCompleted_Handler_GetPTData;//注册线程结束方法

            bgWorker_GetPTF.WorkerReportsProgress = true;//允许报告线程进度
            bgWorker_GetPTF.WorkerSupportsCancellation = true;//线程允许中止取消
            bgWorker_GetPTF.DoWork += DoWork_Handler_GetPTF;//注册线程开始方法
            bgWorker_GetPTF.ProgressChanged += ProgressChanged_Handler_GetPTF;//注册报告线程进度方法
            bgWorker_GetPTF.RunWorkerCompleted += RunWorkerCompleted_Handler_GetPTF;//注册线程结束方法

            bgWorker_GetTestInfo.WorkerReportsProgress = true;//允许报告线程进度
            bgWorker_GetTestInfo.WorkerSupportsCancellation = true;//线程允许中止取消
            bgWorker_GetTestInfo.DoWork += DoWork_Handler_GetTestInfo;//注册线程开始方法
            bgWorker_GetTestInfo.ProgressChanged += ProgressChanged_Handler_GetTestInfo;//注册报告线程进度方法
            bgWorker_GetTestInfo.RunWorkerCompleted += RunWorkerCompleted_Handler_GetTestInfo;//注册线程结束方法

            bgWorker_GetWaveform.WorkerReportsProgress = true;//允许报告线程进度
            bgWorker_GetWaveform.WorkerSupportsCancellation = true;//线程允许中止取消
            bgWorker_GetWaveform.DoWork += DoWork_Handler_GetWaveform;//注册线程开始方法
            bgWorker_GetWaveform.ProgressChanged += ProgressChanged_Handler_GetWaveform;//注册报告线程进度方法
            bgWorker_GetWaveform.RunWorkerCompleted += RunWorkerCompleted_Handler_GetWaveform;//注册线程结束方法

            bgWorker_GetTpWaveform.WorkerReportsProgress = true;//允许报告线程进度
            bgWorker_GetTpWaveform.WorkerSupportsCancellation = true;//线程允许中止取消
            bgWorker_GetTpWaveform.DoWork += DoWork_Handler_GetTpWaveform;//注册线程开始方法
            bgWorker_GetTpWaveform.ProgressChanged += ProgressChanged_Handler_GetTpWaveform;//注册报告线程进度方法
            bgWorker_GetTpWaveform.RunWorkerCompleted += RunWorkerCompleted_Handler_GetTpWaveform;//注册线程结束方法

            bgWorker_GetInsInfo.WorkerReportsProgress = true;//允许报告线程进度
            bgWorker_GetInsInfo.WorkerSupportsCancellation = true;//线程允许中止取消
            bgWorker_GetInsInfo.DoWork += DoWork_Handler_GetInsInfo;//注册线程开始方法
            bgWorker_GetInsInfo.ProgressChanged += ProgressChanged_Handler_GetInsInfo;//注册报告线程进度方法
            bgWorker_GetInsInfo.RunWorkerCompleted += RunWorkerCompleted_Handler_GetInsInfo;//注册线程结束方法

            bgWorker_SetInsInfo.WorkerReportsProgress = true;//允许报告线程进度
            bgWorker_SetInsInfo.WorkerSupportsCancellation = true;//线程允许中止取消
            bgWorker_SetInsInfo.DoWork += DoWork_Handler_SetInsInfo;//注册线程开始方法
            bgWorker_SetInsInfo.ProgressChanged += ProgressChanged_Handler_SetInsInfo;//注册报告线程进度方法
            bgWorker_SetInsInfo.RunWorkerCompleted += RunWorkerCompleted_Handler_SetInsInfo;//注册线程结束方法


        }

        #endregion

        #region 导入通讯所需要的DLL
        //打开设备
        [System.Runtime.InteropServices.DllImport("CH375DLL")]
        private static extern IntPtr CH375OpenDevice(uint index);
        //关闭设备
        [System.Runtime.InteropServices.DllImport("CH375DLL")]
        private static extern void CH375CloseDevice(uint index);
        //发送数据
        [System.Runtime.InteropServices.DllImport("CH375DLL")]
        private static extern bool CH375WriteData(IntPtr handle, byte[] sdata, ref int length);
        //接收数据
        [System.Runtime.InteropServices.DllImport("CH375DLL")]
        private static extern bool CH375ReadData(IntPtr handle, byte[] SYDATa,ref int length);
        //设置接收和发送超时，值为0xffffffff为不超时
        [System.Runtime.InteropServices.DllImport("CH375DLL")]
        public static extern bool CH375SetTimeout(uint index, uint iWritetimeout, uint iReadtimeout);
        //查询内部下传缓冲区中的剩余数据包个数(尚未发送),成功返回数据包个数,出错返回-1
        [System.Runtime.InteropServices.DllImport("CH375DLL")]
        public static extern long CH375QueryBufDownload(uint index);
        // 查询内部上传缓冲区中的已有数据包个数,成功返回数据包个数,出错返回-1
        [System.Runtime.InteropServices.DllImport("CH375DLL")]
        public static extern long CH375QueryBufUpload(uint index);
        // 获取USB设备ID,返回数据中,低16位为厂商ID,高16位为产品ID,错误时返回全0(无效ID)
        [System.Runtime.InteropServices.DllImport("CH375DLL")]
        public static extern ulong CH375GetUsbID(uint index);
        #endregion

        #region I_CH374Comm 成员

        private bool _isConnected;//仪器是否连接PC

        public bool InitDevice()
        {
            return false;
        }

        public bool SearchDevice()
        {
            bool bConnect = false;
            if (_deviceIndex < 0)
            {
                return false;
            }           
            else
            {
                bConnect = Search();
                if (bConnect == false)
                {
                    _isConnected = false;
                    _usbHandle = new IntPtr(-1);
                }
                return bConnect;
            }
        }

        private bool Search() //查找通讯设备，非U盘设备
        {
            IntPtr myHandle = new IntPtr(-1);
            myHandle = CH375OpenDevice(_deviceIndex);
            if (myHandle.ToInt32() == -1)
                return false;
            else
                return true;
        }

        public bool OpenDevice()
        {
            if (_isConnected == true)
                return _isConnected;
            _usbHandle = CH375OpenDevice(_deviceIndex);
            if (_usbHandle.ToInt32() == -1)
            {
                _isConnected = false;
                return false;
            }
            else
            {
                _isConnected = true;
                return true;
            }
        }

        public bool CloseDevice()
        {
            CH375CloseDevice(_deviceIndex);
            _usbHandle = IntPtr.Zero;
            _isConnected = false;
            return true;
        }

        public bool IsConnected
        {
            get { return _isConnected; }
        }

        public event EventHandler OnConnected;

        public event EventHandler OnDisconnected;

        #endregion  

        #region CTZK相关代码
        object operationlocker = new object();
        CommOperations curoperation = CommOperations.None;
        private byte addr1 = 0;
        private byte addr2 = 0;
        private byte cmd = 0;
        private byte cmd_2 = 0;
        private byte cmd_3 = 0;
        private byte cmd_4 = 0;
        private byte cmd_5 = 0;
        private int datalen = 0;
        private int datalen_2 = 0;
        private int datalen_3 = 0;
        private int datalen_4 = 0;
        private int datalen_5 = 0;
        private byte[] dat = new byte[7];
        private byte[] dat_2 = new byte[7];
        private byte[] dat_3 = new byte[7];
        private byte[] dat_4 = new byte[7];
        private byte[] dat_5 = new byte[7];
        private byte[] temp = new byte[64];
        UInt16 BlockCount;
        byte[] COE;//用于存放测试数据
        byte[] PTF=new byte[256];//用于存放全部系数
        byte[] PT = new byte[128];//用于存放压力温度系数
        byte[] F = new byte[128];//用于存放流量系数
        byte[] TESTINFO = new byte[256];//用于存放测试信息
        byte[] wave;
        int[] tpwave = new int[1800];//换能器波形
        byte[] INSINFO = new byte[320];//井下仪仪器信息

        #region 命令定义及超时
        private const byte DMXGetVersionCmd = 0x00;//读地面仪固件版本信息,返回1字节
        private const uint DMXGetVersionTimeout = 1000;

        private const byte DMXSetGearCmd = 0x10;//地面仪设置放大倍数档位，带一个字节的数据
        private const uint DMXSetGearTimeout = 1000;

        private const byte DMXGetGearCmd = 0x20;//读地面仪放大倍数档位,返回1字节
        private const uint DMXGetGearTimeout = 1000;

        private const byte DMXGetWaveformCmd = 0x30;//读地面仪接收信号波形,每帧返回7字节
        private const uint DMXGetWaveformTimeout = 1000;

        private const byte DMXSetBaudCmd = 0x40;//设置地面仪波特率,每帧返回7字节
        private const uint DMXSetBaudTimeout = 1000;

        private const byte DMXGetBaudCmd = 0x50;//读取地面仪波特率,每帧返回7字节
        private const uint DMXGetBaudTimeout = 1000;

        private const byte JXYResetCmd = 0x10;//井下仪复位,返回2字节
        private const uint JXYResetTimeout = 1000;

        private const byte JXYGetVerCmd = 0x20;//井下仪版本,返回2字节
        private const uint JXYGetVerTimeout = 1000;

        private const byte JXYCableVolCmd = 0x30;//总线电压,返回2字节
        private const uint JXYCableVolTimeout = 1000;

        private const byte JXYSamplePTCmd = 0x40;//压力温度采样,返回4字节
        private const uint JXYSamplePTTimeout = 1000;//分2帧上传 

        private const byte JXYSampleFCmd = 0x50;//流量采样,返回8字节
        private const uint JXYSampleFTimeout = 1000;//分4帧上传 

        private const byte JXYMotor1PCmd = 0x61;//张定位臂,返回2字节
        private const uint JXYMotor1PTimeout = 2000;

        private const byte JXYMotor1NCmd = 0x62;//收定位臂,返回2字节
        private const uint JXYMotor1NTimeout = 2000;

        private const byte JXYMotor2PCmd = 0x71;//配注调大,返回2字节
        private const uint JXYMotor2PTimeout = 2000;

        private const byte JXYMotor2NCmd = 0x72;//配注调小,返回2字节
        private const uint JXYMotor2NTimeout = 2000;

        private const byte JXYOpenMotorPwrCmd = 0x80;//开电机电源,返回2字节
        private const uint JXYOpenMotorPwrTimeout = 1000;

        private const byte JXYGetMotorStatusCmd = 0x81;//电机状态,返回4字节
        private const uint JXYGetMotorStatusTimeout = 1000;

        private const byte JXYMotorStopCmd = 0x82;//电机停止,返回2字节
        private const uint JXYMotorStopTimeout = 1000;

        private const byte JXYGetMotorThrCmd = 0x83;//读取电机堵转电流,返回2字节
        private const uint JXYGetMotorThrTimeout = 500;

        private const byte JXYSetMotor1ThrCmd = 0x84;//设置收放电机堵转电流,返回2字节
        private const uint JXYSetMotor1ThrTimeout = 500;

        private const byte JXYSetMotor2ThrCmd = 0x8a;//设置调节电机堵转电流,返回2字节
        private const uint JXYSetMotor2ThrTimeout = 500;

        private const byte JXYGetTestInfoCmd = 0x90;//上传测试信息
        private const uint JXYGetTestInfoTimeout = 500;

        private const byte JXYGetTestDataCmd = 0x91;//上传测试数据
        private const uint JXYGetTestDataTimeout = 500;

        private const byte JXYFormatCmd = 0x92;//删除测试数据，流量板整机格式化
        private const uint JXYFormatTimeout = 500;

        private const byte JXYGetCoefficientAllCmd = 0x93;//提取全部系数，共256字节128帧
        private const uint JXYGetCoefficientAllTimeout = 500; 

        private const byte JXYSetCoefficientPTCmd = 0xA0;//下发压力温度标定系数，共128字节128帧
        private const uint JXYSetCoefficientPTTimeout = 500; 
        private const byte JXYGetCoefficientPTCmd = 0xA1;//提取压力温度标定系数，共128字节64帧
        private const uint JXYGetCoefficientPTTimeout = 500; 

        private const byte JXYSetCoefficientFCmd = 0xB0;//下发流量标定系数，共128字节128帧
        private const uint JXYSetCoefficientFTimeout = 500;
        private const byte JXYGetCoefficientFCmd = 0xB1;//提取流量标定系数，共128字节64帧
        private const uint JXYGetCoefficientFTimeout = 500; 

        private const byte JXYSampleAllCmd = 0xC0;//采样全部参数,返回16字节8帧
        private const uint JXYSampleAllTimeout1 = 1000;//分8帧上传，第一帧1000ms 
        private const uint JXYSampleAllTimeout2 = 1000;//分8帧上传， 以后每帧500ms 

        private const byte JXYGetInsInfoCmd = 0xC5;//上提仪器信息
        private const uint JXYGetInsInfoTimeout = 500;

        private const byte JXYSetInsInfoCmd = 0xCA;//下发仪器信息
        private const uint JXYSetInsInfoTimeout = 1000;


        private const byte JXYTpAmplitudeCmd = 0xEE;//换能器幅值，8字节共4帧上传
        private const uint JXYTpAmplitudeTimeout = 1000;

        private const byte JXYTpWaveformCmd = 0xF0;//换能器幅值，1800帧
        private const uint JXYTpWaveformTimeout = 1000;
        #endregion

     
        public void Clear374Buf()
        {
            //循环读清空缓存多余数据
            if ((_mw.CommStyle == CommStyleEnum.USB) && (_mw.USBFlag == true))//CH374模式下
            {
                CH375SetTimeout(_deviceIndex, 100, 10);
                int length = 64;
                while (length > 0)
                {
                    CH375ReadData(_usbHandle, temp, ref length);
                }
            }
        }

        public CommResult SendFrame(byte addr1, byte addr2, byte cmd, int datalen, byte[] dat)//帧发送，addr1:0-地面仪，1-井下仪；addr2：0-主控板，1-采集板
        {
            if ((_mw.CommStyle == CommStyleEnum.USB) && (_mw.USBFlag == true))//CH374模式下
            {
                if (_isConnected == false)
                    return CommResult.USBDisconnect;

                if (_usbHandle.ToInt32() <= 0)
                    return CommResult.USBOpenDeviceError;

                if (datalen > 7)//数据字节长度不能大于7，因为USB每帧16字节中有9字节的额外开销，则数据字节长度最大只能有7字节
                {
                    return CommResult.USBSendLenthOve;
                }

                if(( datalen>0) && (dat == null))
                {
                    return CommResult.SendFrameBufError;
                }

                Clear374Buf();//清空缓存多余数据

                byte[] sdata = new byte[16];
                sdata[0] = 0x7e;//帧头
                sdata[1] = 0x7e;//帧头
                sdata[2] = addr1;//地址1
                sdata[3] = addr2;//地址2
                sdata[4] = cmd;//命令
                sdata[5] = (byte)(datalen);//数据长度低字节
                sdata[6] = (byte)(datalen >> 8);//数据长度高字节

                int i;
                for (i = 0; i < datalen; i++)
                {
                    sdata[i + 7] = dat[i];
                }
                sdata[i + 7] = 0x55;//校验低字节
                sdata[i + 8] = 0xaa;//校验高字节

                for (i = (datalen + 9); i < 16; i++)
                {
                    sdata[i] = 0x00;//不足16字节时后面的字节补0
                }

                int sCount = 16;//发送16字节固定字节长度的帧
                if (CH375WriteData(_usbHandle, sdata, ref sCount) && sCount == (16))
                {
                    return CommResult.Success;
                }
                else
                    return CommResult.SendFrameFail;
            }
            else if(_mw.CommStyle == CommStyleEnum.RS232) //RS232模式下
            {
                if (_mw.RS232_port.IsOpen == false)
                {
                    //MessageBox.Show("端口未打开！");
                    return CommResult.RS232NotOpen;
                }

                if ((datalen > 0) && (dat == null))
                {
                    return CommResult.SendFrameBufError;
                }

                _mw.RS232_port.DiscardInBuffer();

                byte[] sdata = new byte[datalen+9];
                sdata[0] = 0x7e;//帧头
                sdata[1] = 0x7e;//帧头
                sdata[2] = addr1;//地址1
                sdata[3] = addr2;//地址2
                sdata[4] = cmd;//命令
                sdata[5] = (byte)(datalen);//数据长度低字节
                sdata[6] = (byte)(datalen >> 8);//数据长度高字节

                int i;
                for (i = 0; i < datalen; i++)
                {
                    sdata[i + 7] = dat[i];
                }
                sdata[i + 7] = 0x55;//校验低字节
                sdata[i + 8] = 0xaa;//校验高字节
              //  Debug.WriteLine("A:" + System.DateTime.Now.ToString() + ":" + System.DateTime.Now.Millisecond.ToString());
                _mw.RS232_port.Write(sdata, 0, sdata.Length);
                _mw.thisFrameEnum = FrameEnum.IDEL;//发送完毕后立即置状态为空闲状态，准备接收反馈帧
                return CommResult.Success;
            }
            //else if (_mw.CommStyle == CommStyleEnum.Bluetooth) //Bluetooth模式下
            //{
            //    if (_mw.accData == null)
            //    {
            //        return CommResult.BluetoothNotOpen;
            //    }

            //    if ((datalen > 0) && (dat == null))
            //    {
            //        return CommResult.SendFrameBufError;
            //    }

            //    byte[] sdata = new byte[datalen + 9];
            //    sdata[0] = 0x7e;//帧头
            //    sdata[1] = 0x7e;//帧头
            //    sdata[2] = addr1;//地址1
            //    sdata[3] = addr2;//地址2
            //    sdata[4] = cmd;//命令
            //    sdata[5] = (byte)(datalen);//数据长度低字节
            //    sdata[6] = (byte)(datalen >> 8);//数据长度高字节

            //    int i;
            //    for (i = 0; i < datalen; i++)
            //    {
            //        sdata[i + 7] = dat[i];
            //    }
            //    sdata[i + 7] = 0x55;//校验低字节
            //    sdata[i + 8] = 0xaa;//校验高字节
            //                        //  Debug.WriteLine("A:" + System.DateTime.Now.ToString() + ":" + System.DateTime.Now.Millisecond.ToString());
            //                        // _mw.RS232_port.Write(sdata, 0, sdata.Length);

            //    try
            //    {
            //        _mw.WriteBLE(_mw.accData, sdata);
            //        _mw.thisFrameEnum = FrameEnum.IDEL;//发送完毕后立即置状态为空闲状态，准备接收反馈帧
            //        return CommResult.Success;
            //    }
            //    catch
            //    {
            //        return CommResult.BluetoothSendFail;
            //    }
            //}
            else if (_mw.CommStyle == CommStyleEnum.WIFI) //WIFI模式下
            {
                if (_mw.myUDPServer==null)
                {
                    return CommResult.WIFINotOpen;
                }

                if ((datalen > 0) && (dat == null))
                {
                    return CommResult.SendFrameBufError;
                }

                _mw.myUDPServer.ReceiveBytes = null;//先清空接收缓存

                byte[] sdata = new byte[datalen + 9];
                sdata[0] = 0x7e;//帧头
                sdata[1] = 0x7e;//帧头
                sdata[2] = addr1;//地址1
                sdata[3] = addr2;//地址2
                sdata[4] = cmd;//命令
                sdata[5] = (byte)(datalen);//数据长度低字节
                sdata[6] = (byte)(datalen >> 8);//数据长度高字节

                int i;
                for (i = 0; i < datalen; i++)
                {
                    sdata[i + 7] = dat[i];
                }
                sdata[i + 7] = 0x55;//校验低字节
                sdata[i + 8] = 0xaa;//校验高字节
                                    //  Debug.WriteLine("A:" + System.DateTime.Now.ToString() + ":" + System.DateTime.Now.Millisecond.ToString());
                                    // _mw.RS232_port.Write(sdata, 0, sdata.Length);

                try
                {
                    _mw.myUDPServer.ReceiveUdpClient.Send(sdata, sdata.Length, _mw.myUDPServer.RemoteIPEndPoint);
                    _mw.thisFrameEnum = FrameEnum.IDEL;//发送完毕后立即置状态为空闲状态，准备接收反馈帧
                    return CommResult.Success;
                }
                catch
                {
                    return CommResult.WIFISendUDPFail;
                }
            }

            return CommResult.FrameNotSend;
        }

        public CommResult SendFrame2(byte addr1, byte addr2, byte cmd, int datalen, byte[] dat)//帧发送，用于测试
        {
            if ((_mw.CommStyle == CommStyleEnum.USB) && (_mw.USBFlag == true))//CH374模式下
            {
                if (_isConnected == false)
                    return CommResult.USBDisconnect;

                if (_usbHandle.ToInt32() <= 0)
                    return CommResult.USBOpenDeviceError;

                if (datalen > 7)//数据字节长度不能大于7，因为USB每帧16字节中有9字节的额外开销，则数据字节长度最大只能有7字节
                {
                    return CommResult.USBSendLenthOve;
                }

                if ((datalen > 0) && (dat == null))
                {
                    return CommResult.SendFrameBufError;
                }

                Clear374Buf();//清空缓存多余数据

                byte[] sdata = new byte[16];
                sdata[0] = 0xe7;//帧头
                sdata[1] = 0xe7;//帧头
                sdata[2] = addr1;//地址1
                sdata[3] = addr2;//地址2
                sdata[4] = cmd;//命令
                sdata[5] = (byte)(datalen);//数据长度低字节
                sdata[6] = (byte)(datalen >> 8);//数据长度高字节

                int i;
                for (i = 0; i < datalen; i++)
                {
                    sdata[i + 7] = dat[i];
                }
                sdata[i + 7] = 0x55;//校验低字节
                sdata[i + 8] = 0xaa;//校验高字节

                for (i = (datalen + 9); i < 16; i++)
                {
                    sdata[i] = 0x00;//不足16字节时后面的字节补0
                }

                int sCount = 16;//发送16字节固定字节长度的帧
                if (CH375WriteData(_usbHandle, sdata, ref sCount) && sCount == (16))
                {
                    return CommResult.Success;
                }
                else
                    return CommResult.SendFrameFail;
            }
            else if (_mw.CommStyle == CommStyleEnum.RS232) //RS232模式下
            {
                if (_mw.RS232_port.IsOpen == false)
                {
                    //MessageBox.Show("端口未打开！");
                    return CommResult.RS232NotOpen;
                }

                if ((datalen > 0) && (dat == null))
                {
                    return CommResult.SendFrameBufError;
                }

                _mw.RS232_port.DiscardInBuffer();

                byte[] sdata = new byte[datalen + 9];
                sdata[0] = 0xe7;//帧头
                sdata[1] = 0xe7;//帧头
                sdata[2] = addr1;//地址1
                sdata[3] = addr2;//地址2
                sdata[4] = cmd;//命令
                sdata[5] = (byte)(datalen);//数据长度低字节
                sdata[6] = (byte)(datalen >> 8);//数据长度高字节

                int i;
                for (i = 0; i < datalen; i++)
                {
                    sdata[i + 7] = dat[i];
                }
                sdata[i + 7] = 0x55;//校验低字节
                sdata[i + 8] = 0xaa;//校验高字节
                                    //  Debug.WriteLine("A:" + System.DateTime.Now.ToString() + ":" + System.DateTime.Now.Millisecond.ToString());
                _mw.RS232_port.Write(sdata, 0, sdata.Length);
                _mw.thisFrameEnum = FrameEnum.IDEL;//发送完毕后立即置状态为空闲状态，准备接收反馈帧
                return CommResult.Success;
            }
            else if (_mw.CommStyle == CommStyleEnum.WIFI) //WIFI模式下
            {
                if (_mw.myUDPServer == null)
                {
                    return CommResult.WIFINotOpen;
                }

                if ((datalen > 0) && (dat == null))
                {
                    return CommResult.SendFrameBufError;
                }

                _mw.myUDPServer.ReceiveBytes = null;//先清空接收缓存

                byte[] sdata = new byte[datalen + 9];
                sdata[0] = 0xe7;//帧头
                sdata[1] = 0xe7;//帧头
                sdata[2] = addr1;//地址1
                sdata[3] = addr2;//地址2
                sdata[4] = cmd;//命令
                sdata[5] = (byte)(datalen);//数据长度低字节
                sdata[6] = (byte)(datalen >> 8);//数据长度高字节

                int i;
                for (i = 0; i < datalen; i++)
                {
                    sdata[i + 7] = dat[i];
                }
                sdata[i + 7] = 0x55;//校验低字节
                sdata[i + 8] = 0xaa;//校验高字节
                                    //  Debug.WriteLine("A:" + System.DateTime.Now.ToString() + ":" + System.DateTime.Now.Millisecond.ToString());
                                    // _mw.RS232_port.Write(sdata, 0, sdata.Length);

                try
                {
                    _mw.myUDPServer.ReceiveUdpClient.Send(sdata, sdata.Length, _mw.myUDPServer.RemoteIPEndPoint);
                    _mw.thisFrameEnum = FrameEnum.IDEL;//发送完毕后立即置状态为空闲状态，准备接收反馈帧
                    return CommResult.Success;
                }
                catch
                {
                    return CommResult.WIFISendUDPFail;
                }
            }

            return CommResult.FrameNotSend;
        }
        public CommResult ReadFrame(ref byte addr1, ref byte addr2, ref byte cmd, ref int datalen, ref byte[] dat, uint timeout)//帧接收
        {
            if ((_mw.CommStyle == CommStyleEnum.USB) && (_mw.USBFlag == true))//CH374模式下
            {
                if (_isConnected == false)
                    return CommResult.USBDisconnect;

                if (_usbHandle.ToInt32() <= 0)
                    return CommResult.USBOpenDeviceError;

                int length = 16;
                byte[] data = new byte[16];

                if (dat == null)
                { return CommResult.ReadFrameBufError; }

             

                if (CH375SetTimeout(_deviceIndex, 100, timeout) == false)//设置读超时时间
                {
                    return CommResult.USBSetTimeoutError;
                }

                if (CH375ReadData(_usbHandle, data, ref length) && (length == 16))
                {
                    if ((data[0] == 0xe7) && (data[1] == 0xe7))
                    {
                        addr1 = data[2];//地址1
                        addr2 = data[3];//地址2
                        cmd = data[4];//命令
                        datalen = data[5] + data[6] * 256;//有效数据长度

                        if (dat.Length < datalen)//每帧最多可能接收7字节有效数据，如传递进来的缓冲数组太小则不能进行读操作
                        {
                            return CommResult.ReadFrameBufLenthError;
                        }

                        for (int i = 0; i < datalen; i++)
                        {
                            dat[i] = data[i + 7];
                        }
                        return CommResult.Success;
                    }
                    else
                    {
                        return CommResult.ReadFrameHeadError;
                    }
                }
                else
                    return CommResult.USBReceiveTimeout;
            }
            else if (_mw.CommStyle == CommStyleEnum.RS232) //RS232模式下
            {
                if (dat == null)
                { return CommResult.ReadFrameBufError; }

                QueryPerformanceFrequency(ref Frequency);
                QueryPerformanceCounter(ref Counter);

                while (true)//超时时间内死循环读串中，如果读到了一帧完整数据，则可提前结束返回
                {
                    //_mw.RS232Receive();
                    QueryPerformanceCounter(ref Counter2);
                    if(_mw.thisFrameEnum == FrameEnum.OK)//收到了个完整帧后跳出
                    {
                    //    Debug.WriteLine("B:" + System.DateTime.Now.ToString() + ":" + System.DateTime.Now.Millisecond.ToString());
                        _mw.thisFrameEnum = FrameEnum.IDEL;//接收到完整帧后立即置状态位为空闲
                        if ((_mw.thisFrameStruc.head1 == 0xe7) && (_mw.thisFrameStruc.head2 == 0xe7))
                        {
                            addr1 = _mw.thisFrameStruc.addr1;//地址1
                            addr2 = _mw.thisFrameStruc.addr2;//地址2
                            cmd = _mw.thisFrameStruc.comm;//命令
                            datalen = _mw.thisFrameStruc.datalen;//有效数据长度
                    
                            for (int i = 0; i < datalen; i++)
                            {
                                dat[i] = _mw.thisFrameStruc.databuf[i];
                            }
                            return CommResult.Success;
                        }
                        else
                        {
                            return CommResult.ReadFrameHeadError;
                        }
                    }
                    if ((Counter2 - Counter) * 1000 > (Frequency * timeout))//精确定时timeout (ms)，超时后跳出
                    {
                        return CommResult.RS232ReceiveTimeout;
                    }
                }
                
            }
            //else if (_mw.CommStyle == CommStyleEnum.Bluetooth) //Bluetooth模式下
            //{
            //    if (dat == null)
            //    { return CommResult.ReadFrameBufError; }

            //    QueryPerformanceFrequency(ref Frequency);
            //    QueryPerformanceCounter(ref Counter);

            //    while (true)//超时时间内死循环读串中，如果读到了一帧完整数据，则可提前结束返回
            //    {
            //        QueryPerformanceCounter(ref Counter2);
            //        if (_mw.thisFrameEnum == FrameEnum.OK)//收到了个完整帧后跳出
            //        {
            //            //    Debug.WriteLine("B:" + System.DateTime.Now.ToString() + ":" + System.DateTime.Now.Millisecond.ToString());
            //            _mw.thisFrameEnum = FrameEnum.IDEL;//接收到完整帧后立即置状态位为空闲
            //            if ((_mw.thisFrameStruc.head1 == 0xe7) && (_mw.thisFrameStruc.head2 == 0xe7))
            //            {
            //                addr1 = _mw.thisFrameStruc.addr1;//地址1
            //                addr2 = _mw.thisFrameStruc.addr2;//地址2
            //                cmd = _mw.thisFrameStruc.comm;//命令
            //                datalen = _mw.thisFrameStruc.datalen;//有效数据长度

            //                for (int i = 0; i < datalen; i++)
            //                {
            //                    dat[i] = _mw.thisFrameStruc.databuf[i];
            //                }
            //                return CommResult.Success;
            //            }
            //            else
            //            {
            //                return CommResult.ReadFrameHeadError;
            //            }
            //        }
            //        if ((Counter2 - Counter) * 1000 > (Frequency * timeout))//精确定时timeout (ms)，超时后跳出
            //        {
            //            return CommResult.BluetoothReceiveTimeout;
            //        }
            //    }
            //}
            else if (_mw.CommStyle == CommStyleEnum.WIFI) //WIFI模式下
            {
                if (dat == null)
                { return CommResult.ReadFrameBufError; }

                QueryPerformanceFrequency(ref Frequency);
                QueryPerformanceCounter(ref Counter);

                while (true)//超时时间内死循环读串中，如果读到了一帧完整数据，则可提前结束返回
                {
                    //_mw.RS232Receive();
                    QueryPerformanceCounter(ref Counter2);
                    if (_mw.thisFrameEnum == FrameEnum.OK)//收到了个完整帧后跳出
                    {
                        //    Debug.WriteLine("B:" + System.DateTime.Now.ToString() + ":" + System.DateTime.Now.Millisecond.ToString());
                        _mw.thisFrameEnum = FrameEnum.IDEL;//接收到完整帧后立即置状态位为空闲
                        if ((_mw.thisFrameStruc.head1 == 0xe7) && (_mw.thisFrameStruc.head2 == 0xe7))
                        {
                            addr1 = _mw.thisFrameStruc.addr1;//地址1
                            addr2 = _mw.thisFrameStruc.addr2;//地址2
                            cmd = _mw.thisFrameStruc.comm;//命令
                            datalen = _mw.thisFrameStruc.datalen;//有效数据长度

                            for (int i = 0; i < datalen; i++)
                            {
                                dat[i] = _mw.thisFrameStruc.databuf[i];
                            }
                            return CommResult.Success;
                        }
                        else
                        {
                            return CommResult.ReadFrameHeadError;
                        }
                    }
                    if ((Counter2 - Counter) * 1000 > (Frequency * timeout))//精确定时timeout (ms)，超时后跳出
                    {
                        return CommResult.WIFIReceiveTimeout;
                    }
                }

            }
            return CommResult.FrameNotReceived;
        }

        public bool CRCCheck(byte[] dat)
        {
            return true;
        }

        void crc16(byte[] r_data, int length, ref byte crc16hi, ref byte crc16lo)
        {
            byte cl, ch;
            byte savehi, savelo;
            int ii, flag;
            crc16hi = 0xFF;
            crc16lo = 0xFF;
            cl = 0x1;
            ch = 0xA0;
            for (ii = 0; ii < length - 2; ii++)
            {
                crc16lo = (byte)(crc16lo ^ r_data[ii]);
                for (flag = 0; flag < 8; flag++)
                {
                    savehi = crc16hi;
                    savelo = crc16lo;
                    crc16hi = (byte)(crc16hi >> 1);
                    crc16lo = (byte)(crc16lo >> 1);
                    if ((savehi & 0x01) == 0x01)
                        crc16lo = (byte)(crc16lo | 0x80);
                    if ((savelo & 0x01) == 0x01)
                    {
                        crc16hi = (byte)(crc16hi ^ ch);
                        crc16lo = (byte)(crc16lo ^ cl);
                    }
                }
            }
        }
        public CommResult DMXGetVersion(ref byte ver)//获取地面仪版本
        {
            lock (operationlocker)
            {
                if (curoperation == CommOperations.None)
                    curoperation = CommOperations.DMXGetVersion;
                else
                    return CommResult.OperationConflict;
            }

            CommResult r;
            try
            {
                r = SendFrame(0, 0, DMXGetVersionCmd, 0, null);
                if (r == CommResult.Success)
                {
                    r = ReadFrame(ref addr1, ref addr2, ref cmd, ref datalen, ref dat, DMXGetVersionTimeout);
                    if (r == CommResult.Success)
                    {
                        if (CRCCheck(dat) == false)
                        {
                            r = CommResult.DMXCheckError;
                        }
                        else
                        {
                            if (cmd == (DMXGetVersionCmd >> 4))
                            {
                                if (datalen == 1)
                                {
                                    ver = dat[0];
                                    r = CommResult.Success;
                                }
                                else
                                {
                                    r = CommResult.DataLenError;
                                }
                            }
                            else
                                r = CommResult.ReturnCmdError;
                        }
                    }
                }             
            }
            catch
            {
                lock (operationlocker)
                {
                    curoperation = CommOperations.None;
                }
                return CommResult.Exception;
            }

            lock (operationlocker)
            {
                curoperation = CommOperations.None;
            }

            return r;
        }

        public CommResult JXYReset(ref byte h, ref byte l)//井下仪复位
        {
            lock (operationlocker)
            {
                if (curoperation == CommOperations.None)
                    curoperation = CommOperations.JXYReset;
                else
                    return CommResult.OperationConflict;
            }


            CommResult r;
            try
            {
                r = SendFrame(1, 0, JXYResetCmd, 0, null);
                if (r == CommResult.Success)
                {
                    r = ReadFrame(ref addr1, ref addr2, ref cmd, ref datalen, ref dat, JXYResetTimeout);
                    if (r == CommResult.Success)
                    {
                        if (CRCCheck(dat) == false)
                        {
                            r = CommResult.DMXCheckError;
                        }
                        else
                        {
                            if (cmd == (JXYResetCmd >> 4))
                            {
                                if (datalen == 2)
                                {
                                    h = dat[1];
                                    l = dat[0];
                                    r = CommResult.Success;
                                }
                                else
                                {
                                    r = CommResult.DataLenError;
                                }
                            }
                            else
                                r = CommResult.ReturnCmdError;
                        }
                    }
                }
            }
            catch
            {
                lock (operationlocker)
                {
                    curoperation = CommOperations.None;
                }
                return CommResult.Exception;
            }

            lock (operationlocker)
            {
                curoperation = CommOperations.None;
            }

            return r;
        }

        public CommResult JXYGetVer(ref byte h, ref byte l)//井下仪版本
        {
            lock (operationlocker)
            {
                if (curoperation == CommOperations.None)
                    curoperation = CommOperations.JXYGetVer;
                else
                    return CommResult.OperationConflict;
            }

            CommResult r;
            try
            {
                r = SendFrame(1, 0, JXYGetVerCmd, 0, null);
                if (r == CommResult.Success)
                {
                    r = ReadFrame(ref addr1, ref addr2, ref cmd, ref datalen, ref dat, JXYGetVerTimeout);
                    if (r == CommResult.Success)
                    {
                        if (CRCCheck(dat) == false)
                        {
                            r = CommResult.DMXCheckError;
                        }
                        else
                        {
                            if (cmd == (JXYGetVerCmd >> 4))
                            {
                                if (datalen == 2)
                                {
                                    h = dat[1];
                                    l = dat[0];
                                    r = CommResult.Success;
                                }
                                else
                                {
                                    r = CommResult.DataLenError;
                                }
                            }
                            else
                                r = CommResult.ReturnCmdError;
                        }
                    }
                }
            }
            catch
            {
                lock (operationlocker)
                {
                    curoperation = CommOperations.None;
                }
                return CommResult.Exception;
            }

            lock (operationlocker)
            {
                curoperation = CommOperations.None;
            }

            return r;
        }

        public CommResult JXYOpenMotorPwr(ref byte h, ref byte l)//开电机电源
        {
            lock (operationlocker)
            {
                if (curoperation == CommOperations.None)
                    curoperation = CommOperations.JXYOpenMotorPwr;
                else
                    return CommResult.OperationConflict;
            }


            CommResult r;
            try
            {
                r = SendFrame(1, 0, JXYOpenMotorPwrCmd, 0, null);
                if (r == CommResult.Success)
                {
                    r = ReadFrame(ref addr1, ref addr2, ref cmd, ref datalen, ref dat, JXYOpenMotorPwrTimeout);
                    if (r == CommResult.Success)
                    {
                        if (CRCCheck(dat) == false)
                        {
                            r = CommResult.DMXCheckError;
                        }
                        else
                        {
                            if (cmd == (JXYOpenMotorPwrCmd >> 4))
                            {
                                if (datalen == 2)
                                {
                                    h = dat[1];
                                    l = dat[0];
                                    r = CommResult.Success;
                                }
                                else
                                {
                                    r = CommResult.DataLenError;
                                }
                            }
                            else
                                r = CommResult.ReturnCmdError;
                        }
                    }
                }
            }
            catch
            {
                lock (operationlocker)
                {
                    curoperation = CommOperations.None;
                }
                return CommResult.Exception;
            }

            lock (operationlocker)
            {
                curoperation = CommOperations.None;
            }

            return r;
        }

        public CommResult JXYCableVol(ref byte h, ref byte l)//总线电压
        {
            lock (operationlocker)
            {
                if (curoperation == CommOperations.None)
                    curoperation = CommOperations.JXYCableVol;
                else
                    return CommResult.OperationConflict;
            }

            CommResult r;
            try
            {
                r = SendFrame(1, 0, JXYCableVolCmd, 0, null);
                if (r == CommResult.Success)
                {
                    r = ReadFrame(ref addr1, ref addr2, ref cmd, ref datalen, ref dat, JXYCableVolTimeout);
                    if (r == CommResult.Success)
                    {
                        if (CRCCheck(dat) == false)
                        {
                            r = CommResult.DMXCheckError;
                        }
                        else
                        {
                            if (cmd == (JXYCableVolCmd >> 4))
                            {
                                if (datalen == 2)
                                {
                                    h = dat[1];
                                    l = dat[0];
                                    r = CommResult.Success;
                                }
                                else
                                {
                                    r = CommResult.DataLenError;
                                }
                            }
                            else
                                r = CommResult.ReturnCmdError;
                        }
                    }
                }
            }
            catch
            {
                lock (operationlocker)
                {
                    curoperation = CommOperations.None;
                }
                return CommResult.Exception;
            }

            lock (operationlocker)
            {
                curoperation = CommOperations.None;
            }

            return r;
        }

        public CommResult JXYTpAmplitude(ref byte h1, ref byte l1, ref byte h2, ref byte l2, ref byte h3, ref byte l3, ref byte h4, ref byte l4)//换能器幅值
        {
            lock (operationlocker)
            {
                if (curoperation == CommOperations.None)
                    curoperation = CommOperations.JXYTpAmplitude;
                else
                    return CommResult.OperationConflict;
            }

            CommResult r;
            try
            {
                r = SendFrame(1, 0, JXYTpAmplitudeCmd, 0, null);
                if (r == CommResult.Success)
                {
                    r = ReadFrame(ref addr1, ref addr2, ref cmd, ref datalen, ref dat, JXYTpAmplitudeTimeout);
                    if (r == CommResult.Success)
                    {
                        r = ReadFrame(ref addr1, ref addr2, ref cmd_2, ref datalen_2, ref dat_2, JXYTpAmplitudeTimeout);
                        if (r == CommResult.Success)
                        {
                            r = ReadFrame(ref addr1, ref addr2, ref cmd_3, ref datalen_3, ref dat_3, JXYTpAmplitudeTimeout);
                            if (r == CommResult.Success)
                            {
                                r = ReadFrame(ref addr1, ref addr2, ref cmd_4, ref datalen_4, ref dat_4, JXYTpAmplitudeTimeout);
                                if (r == CommResult.Success)
                                {
                                    if ((CRCCheck(dat) == false) || (CRCCheck(dat_2) == false) || (CRCCheck(dat_3) == false) || (CRCCheck(dat_4) == false))
                                    {
                                        r = CommResult.DMXCheckError;
                                    }
                                    else
                                    {
                                        if ((cmd == (JXYTpAmplitudeCmd >> 4)) && (cmd_2 == (JXYTpAmplitudeCmd >> 4)) && (cmd_3 == (JXYTpAmplitudeCmd >> 4)) && (cmd_4 == (JXYTpAmplitudeCmd >> 4)))
                                        {
                                            if ((datalen == 2) && (datalen_2 == 2) && (datalen_3 == 2) && (datalen_4 == 2))
                                            {
                                                h1 = dat[1];
                                                l1 = dat[0];
                                                h2 = dat_2[1];
                                                l2 = dat_2[0];
                                                h3 = dat_3[1];
                                                l3 = dat_3[0];
                                                h4 = dat_4[1];
                                                l4 = dat_4[0];
                                                r = CommResult.Success;
                                            }
                                            else
                                            {
                                                r = CommResult.DataLenError;
                                            }
                                        }
                                        else
                                            r = CommResult.ReturnCmdError;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                lock (operationlocker)
                {
                    curoperation = CommOperations.None;
                }
                return CommResult.Exception;
            }

            lock (operationlocker)
            {
                curoperation = CommOperations.None;
            }

            return r;
        }
     
        public CommResult JXYSamplePT(ref byte h1, ref byte l1, ref byte h2, ref byte l2)//压力温度采样
        {
            lock (operationlocker)
            {
                if (curoperation == CommOperations.None)
                    curoperation = CommOperations.JXYSamplePT;
                else
                    return CommResult.OperationConflict;
            }

            CommResult r;
            try
            {
                r = SendFrame(1, 0, JXYSamplePTCmd, 0, null);
                if (r == CommResult.Success)
                {
                    r = ReadFrame(ref addr1, ref addr2, ref cmd, ref datalen, ref dat, JXYSamplePTTimeout);
                    if (r == CommResult.Success)
                    {
                        r = ReadFrame(ref addr1, ref addr2, ref cmd_2, ref datalen_2, ref dat_2, JXYSamplePTTimeout);
                        if (r == CommResult.Success)
                        {
                            if ((CRCCheck(dat) == false) || (CRCCheck(dat_2) == false))
                            {
                                r = CommResult.DMXCheckError;
                            }
                            else
                            {
                                if ((cmd == (JXYSamplePTCmd >> 4)) && (cmd_2 == (JXYSamplePTCmd >> 4)))
                                {
                                    if ((datalen == 2) && (datalen_2 == 2))
                                    {
                                        h1 = dat[1];
                                        l1 = dat[0];
                                        h2 = dat_2[1];
                                        l2 = dat_2[0];
                                        r = CommResult.Success;
                                    }
                                    else
                                    {
                                        r = CommResult.DataLenError;
                                    }
                                }
                                else
                                    r = CommResult.ReturnCmdError;
                            }
                        }
                    }
                }
            }
            catch
            {
                lock (operationlocker)
                {
                    curoperation = CommOperations.None;
                }
                return CommResult.Exception;
            }

            lock (operationlocker)
            {
                curoperation = CommOperations.None;
            }

            return r;
        }

        public CommResult JXYSampleF(ref byte h1, ref byte l1, ref byte h2, ref byte l2, ref byte h3, ref byte l3, ref byte h4, ref byte l4)//流量采样
        {
            lock (operationlocker)
            {
                if (curoperation == CommOperations.None)
                    curoperation = CommOperations.JXYSampleF;
                else
                    return CommResult.OperationConflict;
            }

            CommResult r;
            try
            {
                r = SendFrame(1, 0, JXYSampleFCmd, 0, null);
                if (r == CommResult.Success)
                {
                    r = ReadFrame(ref addr1, ref addr2, ref cmd, ref datalen, ref dat, JXYSampleFTimeout);
                    if (r == CommResult.Success)
                    {
                        r = ReadFrame(ref addr1, ref addr2, ref cmd_2, ref datalen_2, ref dat_2, JXYSampleFTimeout);
                        if (r == CommResult.Success)
                        {
                            r = ReadFrame(ref addr1, ref addr2, ref cmd_3, ref datalen_3, ref dat_3, JXYSampleFTimeout);
                            if (r == CommResult.Success)
                            {
                                r = ReadFrame(ref addr1, ref addr2, ref cmd_4, ref datalen_4, ref dat_4, JXYSampleFTimeout);
                                if (r == CommResult.Success)
                                {
                                    r = ReadFrame(ref addr1, ref addr2, ref cmd_5, ref datalen_5, ref dat_5, JXYSampleFTimeout);//读帧校验CRC16
                                    if (r == CommResult.Success)
                                    {
                                        if ((CRCCheck(dat) == false) || (CRCCheck(dat_2) == false) || (CRCCheck(dat_3) == false) || (CRCCheck(dat_4) == false) || (CRCCheck(dat_5) == false))
                                        {
                                            r = CommResult.DMXCheckError;
                                        }
                                        else
                                        {
                                            if ((cmd == (JXYSampleFCmd >> 4)) && (cmd_2 == (JXYSampleFCmd >> 4)) && (cmd_3 == (JXYSampleFCmd >> 4)) && (cmd_4 == (JXYSampleFCmd >> 4)) && (cmd_5 == (JXYSampleFCmd >> 4)) )
                                            {
                                                if ((datalen == 2) && (datalen_2 == 2) && (datalen_3 == 2) && (datalen_4 == 2) && (datalen_5 == 2) )
                                                {
                                                    byte[] CRCData = new byte[8];
                                                    byte h=0, l=0;
                                                    CRCData[0] = dat[0];   CRCData[1] = dat[1];
                                                    CRCData[2] = dat_2[0]; CRCData[3] = dat_2[1];
                                                    CRCData[4] = dat_3[0]; CRCData[5] = dat_3[1];
                                                    CRCData[6] = dat_4[0]; CRCData[7] = dat_4[1];
                                                    crc16(CRCData, 8, ref h, ref l);
                                                    if ((l == dat_5[0]) && (h == dat_5[1]))
                                                    {
                                                        h1 = dat[1];
                                                        l1 = dat[0];
                                                        h2 = dat_2[1];
                                                        l2 = dat_2[0];
                                                        h3 = dat_3[1];
                                                        l3 = dat_3[0];
                                                        h4 = dat_4[1];
                                                        l4 = dat_4[0];
                                                        r = CommResult.Success;
                                                    }
                                                   else
                                                    {
                                                        r = CommResult.JXYCheckCError;
                                                    }
                                                }
                                                else
                                                {
                                                    r = CommResult.DataLenError;
                                                }
                                            }
                                            else
                                                r = CommResult.ReturnCmdError;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                lock (operationlocker)
                {
                    curoperation = CommOperations.None;
                }
                return CommResult.Exception;
            }

            lock (operationlocker)
            {
                curoperation = CommOperations.None;
            }

            return r;
        }

        public CommResult JXYMotor1P(ref byte h, ref byte l)//收放电机正转
        {
            lock (operationlocker)
            {
                if (curoperation == CommOperations.None)
                    curoperation = CommOperations.JXYMotor1P;
                else
                    return CommResult.OperationConflict;
            }

            CommResult r;
            try
            {
                r = SendFrame(1, 0, JXYMotor1PCmd, 0, null);
                if (r == CommResult.Success)
                {
                    r = ReadFrame(ref addr1, ref addr2, ref cmd, ref datalen, ref dat, JXYMotor1PTimeout);
                    if (r == CommResult.Success)
                    {
                        if (CRCCheck(dat) == false)
                        {
                            r = CommResult.DMXCheckError;
                        }
                        else
                        {
                            if (cmd == (JXYMotor1PCmd >> 4))
                            {
                                if (datalen == 2)
                                {
                                    h = dat[1];
                                    l = dat[0];
                                    r = CommResult.Success;
                                }
                                else
                                {
                                    r = CommResult.DataLenError;
                                }
                            }
                            else
                                r = CommResult.ReturnCmdError;
                        }
                    }
                }
            }
            catch
            {
                lock (operationlocker)
                {
                    curoperation = CommOperations.None;
                }
                return CommResult.Exception;
            }

            lock (operationlocker)
            {
                curoperation = CommOperations.None;
            }

            return r;
        }

        public CommResult JXYMotor1N(ref byte h, ref byte l)//收放电机反转
        {
            lock (operationlocker)
            {
                if (curoperation == CommOperations.None)
                    curoperation = CommOperations.JXYMotor1N;
                else
                    return CommResult.OperationConflict;
            }

            CommResult r;
            try
            {
                r = SendFrame(1, 0, JXYMotor1NCmd, 0, null);
                if (r == CommResult.Success)
                {
                    r = ReadFrame(ref addr1, ref addr2, ref cmd, ref datalen, ref dat, JXYMotor1NTimeout);
                    if (r == CommResult.Success)
                    {
                        if (CRCCheck(dat) == false)
                        {
                            r = CommResult.DMXCheckError;
                        }
                        else
                        {
                            if (cmd == (JXYMotor1NCmd >> 4))
                            {
                                if (datalen == 2)
                                {
                                    h = dat[1];
                                    l = dat[0];
                                    r = CommResult.Success;
                                }
                                else
                                {
                                    r = CommResult.DataLenError;
                                }
                            }
                            else
                                r = CommResult.ReturnCmdError;
                        }
                    }
                }
            }
            catch
            {
                lock (operationlocker)
                {
                    curoperation = CommOperations.None;
                }
                return CommResult.Exception;
            }

            lock (operationlocker)
            {
                curoperation = CommOperations.None;
            }

            return r;
        }

        public CommResult JXYMotor2P(ref byte h, ref byte l)//调节电机正转
        {

           if (curoperation != CommOperations.None)
            {
                _mw.RecordSave(3, "操作冲突，正在排队等待。。。", false);
            }
            
            while (curoperation != CommOperations.None);//当总线正忙时，死等

            lock (operationlocker)
            {
                if (curoperation == CommOperations.None)
                    curoperation = CommOperations.JXYMotor2P;
                else
                    return CommResult.OperationConflict;
            }

            System.Threading.Thread.Sleep(50);//让总线休息50ms再下发数据比较好

            
            CommResult r;
            try
            {
                r = SendFrame(1, 0, JXYMotor2PCmd, 0, null);
                if (r == CommResult.Success)
                {
                    r = ReadFrame(ref addr1, ref addr2, ref cmd, ref datalen, ref dat, JXYMotor2PTimeout);
                    if (r == CommResult.Success)
                    {
                        if (CRCCheck(dat) == false)
                        {
                            r = CommResult.DMXCheckError;
                        }
                        else
                        {
                            if (cmd == (JXYMotor2PCmd >> 4))
                            {
                                if (datalen == 2)
                                {
                                    h = dat[1];
                                    l = dat[0];
                                    r = CommResult.Success;
                                }
                                else
                                {
                                    r = CommResult.DataLenError;
                                }
                            }
                            else
                                r = CommResult.ReturnCmdError;
                        }
                    }
                }
            }
            catch
            {
                lock (operationlocker)
                {
                    curoperation = CommOperations.None;
                }
                return CommResult.Exception;
            }

            lock (operationlocker)
            {
                curoperation = CommOperations.None;
            }

            
            return r;
        }

        public CommResult JXYMotor2N(ref byte h, ref byte l)//调节电机反转
        {
            if (curoperation != CommOperations.None)
            {
                _mw.RecordSave(3, "操作冲突，正在排队等待。。。", false);
            }

            while (curoperation != CommOperations.None) ;//当总线正忙时，死等

            lock (operationlocker)
            {
                if (curoperation == CommOperations.None)
                    curoperation = CommOperations.JXYMotor2N;
                else
                    return CommResult.OperationConflict;
            }

            System.Threading.Thread.Sleep(50);//让总线休息50ms再下发数据比较好


            CommResult r;
            try
            {
                r = SendFrame(1, 0, JXYMotor2NCmd, 0, null);
                if (r == CommResult.Success)
                {
                    r = ReadFrame(ref addr1, ref addr2, ref cmd, ref datalen, ref dat, JXYMotor2NTimeout);
                    if (r == CommResult.Success)
                    {
                        if (CRCCheck(dat) == false)
                        {
                            r = CommResult.DMXCheckError;
                        }
                        else
                        {
                            if (cmd == (JXYMotor2NCmd >> 4))
                            {
                                if (datalen == 2)
                                {
                                    h = dat[1];
                                    l = dat[0];
                                    r = CommResult.Success;
                                }
                                else
                                {
                                    r = CommResult.DataLenError;
                                }
                            }
                            else
                                r = CommResult.ReturnCmdError;
                        }
                    }
                }
            }
            catch
            {
                lock (operationlocker)
                {
                    curoperation = CommOperations.None;
                }
                return CommResult.Exception;
            }

            lock (operationlocker)
            {
                curoperation = CommOperations.None;
            }

            System.Threading.Thread.Sleep(50);//刚收到数据最好再休息50ms，怕后面有其它线程马上就下发数据

            return r;
        }

        public CommResult JXYMotorStop(ref byte h, ref byte l)//电机停止
        {
            if (curoperation != CommOperations.None)
            {
                _mw.RecordSave(3, "操作冲突，正在排队等待。。。", false);
            }
            while (curoperation != CommOperations.None) ;//当总线正忙时，死等

            lock (operationlocker)
            {
                if (curoperation == CommOperations.None)
                    curoperation = CommOperations.JXYMotorStop;
                else
                    return CommResult.OperationConflict;
            }

            System.Threading.Thread.Sleep(50);//让总线休息50ms再下发数据比较好


            CommResult r;
            try
            {
                r = SendFrame(1, 0, JXYMotorStopCmd, 0, null);
                if (r == CommResult.Success)
                {
                    r = ReadFrame(ref addr1, ref addr2, ref cmd, ref datalen, ref dat, JXYMotorStopTimeout);
                    if (r == CommResult.Success)
                    {
                        if (CRCCheck(dat) == false)
                        {
                            r = CommResult.DMXCheckError;
                        }
                        else
                        {
                            if (cmd == (JXYMotorStopCmd >> 4))
                            {
                                if (datalen == 2)
                                {
                                    h = dat[1];
                                    l = dat[0];
                                    r = CommResult.Success;
                                }
                                else
                                {
                                    r = CommResult.DataLenError;
                                }
                            }
                            else
                                r = CommResult.ReturnCmdError;
                        }
                    }
                }
            }
            catch
            {
                lock (operationlocker)
                {
                    curoperation = CommOperations.None;
                }
                return CommResult.Exception;
            }

            lock (operationlocker)
            {
                curoperation = CommOperations.None;
            }

            System.Threading.Thread.Sleep(50);//刚收到数据最好再休息50ms，怕后面有其它线程马上就下发数据

            return r;
        }

        public CommResult JXYGetMotorThr(ref byte h, ref byte l)//获取电机堵转阈值
        {

            lock (operationlocker)
            {
                if (curoperation == CommOperations.None)
                    curoperation = CommOperations.JXYGetMotorThr;
                else
                    return CommResult.OperationConflict;
            }

            CommResult r;
            try
            {
                r = SendFrame(1, 0, JXYGetMotorThrCmd, 0, null);
                if (r == CommResult.Success)
                {
                    r = ReadFrame(ref addr1, ref addr2, ref cmd, ref datalen, ref dat, JXYGetMotorThrTimeout);
                    if (r == CommResult.Success)
                    {
                        if (CRCCheck(dat) == false)
                        {
                            r = CommResult.DMXCheckError;
                        }
                        else
                        {
                            if (cmd == (JXYGetMotorThrCmd >> 4))
                            {
                                if (datalen == 2)
                                {
                                    h = dat[1];
                                    l = dat[0];
                                    r = CommResult.Success;
                                }
                                else
                                {
                                    r = CommResult.DataLenError;
                                }
                            }
                            else
                                r = CommResult.ReturnCmdError;
                        }
                    }
                }
            }
            catch
            {
                lock (operationlocker)
                {
                    curoperation = CommOperations.None;
                }
                return CommResult.Exception;
            }

            lock (operationlocker)
            {
                curoperation = CommOperations.None;
            }

            return r;
        }

        public CommResult JXYSetMotor1Thr(byte step,ref byte h, ref byte l)//设置收放b电机堵转阈值
        {
            if(step>5)
            {
                return CommResult.ParaError;
            }


            lock (operationlocker)
            {
                if (curoperation == CommOperations.None)
                    curoperation = CommOperations.JXYSetMotor1Thr;
                else
                    return CommResult.OperationConflict;
            }

            CommResult r;
            try
            {
                r = SendFrame(1, 0, (byte)(JXYSetMotor1ThrCmd + step), 0, null);
                if (r == CommResult.Success)
                {
                    r = ReadFrame(ref addr1, ref addr2, ref cmd, ref datalen, ref dat, JXYSetMotor1ThrTimeout);
                    if (r == CommResult.Success)
                    {
                        if (CRCCheck(dat) == false)
                        {
                            r = CommResult.DMXCheckError;
                        }
                        else
                        {
                            if (cmd == (JXYSetMotor1ThrCmd >> 4))
                            {
                                if (datalen == 2)
                                {
                                    h = dat[1];
                                    l = dat[0];
                                    r = CommResult.Success;
                                }
                                else
                                {
                                    r = CommResult.DataLenError;
                                }
                            }
                            else
                                r = CommResult.ReturnCmdError;
                        }
                    }
                }
            }
            catch
            {
                lock (operationlocker)
                {
                    curoperation = CommOperations.None;
                }
                return CommResult.Exception;
            }

            lock (operationlocker)
            {
                curoperation = CommOperations.None;
            }

            return r;
        }

        public CommResult JXYSetMotor2Thr(byte step, ref byte h, ref byte l)//设置调节电机堵转阈值
        {
            if (step > 5)
            {
                return CommResult.ParaError;
            }

            lock (operationlocker)
            {
                if (curoperation == CommOperations.None)
                    curoperation = CommOperations.JXYSetMotor2Thr;
                else
                    return CommResult.OperationConflict;
            }


            CommResult r;
            try
            {
                r = SendFrame(1, 0, (byte)(JXYSetMotor2ThrCmd + step), 0, null);
                if (r == CommResult.Success)
                {
                    r = ReadFrame(ref addr1, ref addr2, ref cmd, ref datalen, ref dat, JXYSetMotor2ThrTimeout);
                    if (r == CommResult.Success)
                    {
                        if (CRCCheck(dat) == false)
                        {
                            r = CommResult.DMXCheckError;
                        }
                        else
                        {
                            if (cmd == (JXYSetMotor2ThrCmd >> 4))
                            {
                                if (datalen == 2)
                                {
                                    h = dat[1];
                                    l = dat[0];
                                    r = CommResult.Success;
                                }
                                else
                                {
                                    r = CommResult.DataLenError;
                                }
                            }
                            else
                                r = CommResult.ReturnCmdError;
                        }
                    }
                }
            }
            catch
            {
                lock (operationlocker)
                {
                    curoperation = CommOperations.None;
                }
                return CommResult.Exception;
            }

            lock (operationlocker)
            {
                curoperation = CommOperations.None;
            }

            return r;
        }

        public CommResult JXYGetMotorStatus(ref byte h1, ref byte l1, ref byte h2, ref byte l2)//电机状态
        {
            lock (operationlocker)
            {
                if (curoperation == CommOperations.None)
                    curoperation = CommOperations.JXYGetMotorStatus;
                else
                    return CommResult.OperationConflict;
            }

            CommResult r;
            try
            {
                r = SendFrame(1, 0, JXYGetMotorStatusCmd, 0, null);
                if (r == CommResult.Success)
                {
                    r = ReadFrame(ref addr1, ref addr2, ref cmd, ref datalen, ref dat, JXYGetMotorStatusTimeout);
                    if (r == CommResult.Success)
                    {
                        r = ReadFrame(ref addr1, ref addr2, ref cmd_2, ref datalen_2, ref dat_2, JXYGetMotorStatusTimeout);
                        if (r == CommResult.Success)
                        {
                            if ((CRCCheck(dat) == false) || (CRCCheck(dat_2) == false))
                            {
                                r = CommResult.DMXCheckError;
                            }
                            else
                            {
                                if ((cmd == (JXYGetMotorStatusCmd >> 4)) && (cmd_2 == (JXYGetMotorStatusCmd >> 4)))
                                {
                                    if ((datalen == 2) && (datalen_2 == 2))
                                    {
                                        h1 = dat[1];
                                        l1 = dat[0];
                                        h2 = dat_2[1];
                                        l2 = dat_2[0];
                                        r = CommResult.Success;
                                    }
                                    else
                                    {
                                        r = CommResult.DataLenError;
                                    }
                                }
                                else
                                    r = CommResult.ReturnCmdError;
                            }
                        }
                    }
                }
            }
            catch
            {
                lock (operationlocker)
                {
                    curoperation = CommOperations.None;
                }
                return CommResult.Exception;
            }

            lock (operationlocker)
            {
                curoperation = CommOperations.None;
            }

            return r;
        }

        public CommResult DMXSetGear(byte gear, ref byte re)//地面仪设置放大倍数档位
        {
            if (gear > 2)
            {
                return CommResult.ParaError;
            }


            lock (operationlocker)
            {
                if (curoperation == CommOperations.None)
                    curoperation = CommOperations.DMXSetGear;
                else
                    return CommResult.OperationConflict;
            }


            CommResult r;
            byte[] dg = new byte[1];
            dg[0] = gear;
            try
            {
                r = SendFrame(0, 0, (byte)(DMXSetGearCmd), 1, dg);
                if (r == CommResult.Success)
                {
                    r = ReadFrame(ref addr1, ref addr2, ref cmd, ref datalen, ref dat, DMXSetGearTimeout);
                    if (r == CommResult.Success)
                    {
                        if (CRCCheck(dat) == false)
                        {
                            r = CommResult.DMXCheckError;
                        }
                        else
                        {
                            if (cmd == (DMXSetGearCmd))
                            {
                                if (datalen == 1)
                                {
                                    re = dat[0];
                                    r = CommResult.Success;
                                }
                                else
                                {
                                    r = CommResult.DataLenError;
                                }
                            }
                            else
                                r = CommResult.ReturnCmdError;
                        }
                    }
                }
            }
            catch
            {
                lock (operationlocker)
                {
                    curoperation = CommOperations.None;
                }
                return CommResult.Exception;
            }

            lock (operationlocker)
            {
                curoperation = CommOperations.None;
            }

            return r;
        }

        public CommResult DMXSetBaud(int baud, ref byte re)//地面仪设置波特率
        {
            if (baud < 0)
            {
                return CommResult.ParaError;
            }

          
            lock (operationlocker)
            {
                if (curoperation == CommOperations.None)
                    curoperation = CommOperations.DMXSetBaud;
                else
                    return CommResult.OperationConflict;
            }


            CommResult r;
            byte[] dg = new byte[2];
            dg[0] =(byte) (baud&0xff);
            dg[1] = (byte)((baud>>8) & 0xff);
            try
            {
                r = SendFrame(0, 0, (byte)(DMXSetBaudCmd), 2, dg);
                if (r == CommResult.Success)
                {
                    r = ReadFrame(ref addr1, ref addr2, ref cmd, ref datalen, ref dat, DMXSetBaudTimeout);
                    if (r == CommResult.Success)
                    {
                        if (CRCCheck(dat) == false)
                        {
                            r = CommResult.DMXCheckError;
                        }
                        else
                        {
                            if (cmd == (DMXSetBaudCmd))
                            {
                                if (datalen == 1)
                                {
                                    re = dat[0];
                                    r = CommResult.Success;
                                }
                                else
                                {
                                    r = CommResult.DataLenError;
                                }
                            }
                            else
                                r = CommResult.ReturnCmdError;
                        }
                    }
                }
            }
            catch
            {
                lock (operationlocker)
                {
                    curoperation = CommOperations.None;
                }
                return CommResult.Exception;
            }

            lock (operationlocker)
            {
                curoperation = CommOperations.None;
            }

            return r;
        }

        public CommResult DMXGetGear(ref byte gear)//查看地面仪放大倍数档位
        {

            lock (operationlocker)
            {
                if (curoperation == CommOperations.None)
                    curoperation = CommOperations.DMXGetGear;
                else
                    return CommResult.OperationConflict;
            }

            CommResult r;
            try
            {
                r = SendFrame(0, 0, DMXGetGearCmd, 0, null);
                if (r == CommResult.Success)
                {
                    r = ReadFrame(ref addr1, ref addr2, ref cmd, ref datalen, ref dat, DMXGetGearTimeout);
                    if (r == CommResult.Success)
                    {
                        if (CRCCheck(dat) == false)
                        {
                            r = CommResult.DMXCheckError;
                        }
                        else
                        {
                            if (cmd == (DMXGetGearCmd ))
                            {
                                if (datalen == 1)
                                {
                                    gear = dat[0];
                                    r = CommResult.Success;
                                }
                                else
                                {
                                    r = CommResult.DataLenError;
                                }
                            }
                            else
                                r = CommResult.ReturnCmdError;
                        }
                    }
                }
            }
            catch
            {
                lock (operationlocker)
                {
                    curoperation = CommOperations.None;
                }
                return CommResult.Exception;
            }

            lock (operationlocker)
            {
                curoperation = CommOperations.None;
            }

            return r;
        }

        public CommResult DMXGetBaud(ref byte h, ref byte l)//查看地面仪波特率
        {

            lock (operationlocker)
            {
                if (curoperation == CommOperations.None)
                    curoperation = CommOperations.DMXGetBaud;
                else
                    return CommResult.OperationConflict;
            }

            CommResult r;
            try
            {
                r = SendFrame(0, 0, DMXGetBaudCmd, 0, null);
                if (r == CommResult.Success)
                {
                    r = ReadFrame(ref addr1, ref addr2, ref cmd, ref datalen, ref dat, DMXGetBaudTimeout);
                    if (r == CommResult.Success)
                    {
                        if (CRCCheck(dat) == false)
                        {
                            r = CommResult.DMXCheckError;
                        }
                        else
                        {
                            if (cmd == (DMXGetBaudCmd))
                            {
                                if (datalen == 2)
                                {
                                    h = dat[1];
                                    l = dat[0];
                                    r = CommResult.Success;
                                }
                                else
                                {
                                    r = CommResult.DataLenError;
                                }
                            }
                            else
                                r = CommResult.ReturnCmdError;
                        }
                    }
                }
            }
            catch
            {
                lock (operationlocker)
                {
                    curoperation = CommOperations.None;
                }
                return CommResult.Exception;
            }

            lock (operationlocker)
            {
                curoperation = CommOperations.None;
            }

            return r;
        }
        public CommResult DMXGetWaveform()//提取地面箱接收信号波形
        {

            lock (operationlocker)
            {
                if (curoperation == CommOperations.None)
                    curoperation = CommOperations.DMXGetWaveform;
                else
                    return CommResult.OperationConflict;
            }

            CommResult r;
            try
            {
                r = SendFrame(0, 0, DMXGetWaveformCmd, 0, null);
                if (r == CommResult.Success)
                {
                    if (!bgWorker_GetWaveform.IsBusy)
                    {
                        r = CommResult.Processing;
                        bgWorker_GetWaveform.RunWorkerAsync();//后台工作者线程进行异步操作
                    }
                    else
                    {
                        r = CommResult.ThreadBusy;
                    }
                }
            }
            catch
            {
                lock (operationlocker)
                {
                    curoperation = CommOperations.None;
                }
                r = CommResult.Exception;
                return CommResult.Exception;
            }

            lock (operationlocker)
            {
                curoperation = CommOperations.None;
            }
            return r;
        }

        public CommResult JXYGetInsInfo()//提取井下仪仪器信息
        {

            lock (operationlocker)
            {
                if (curoperation == CommOperations.None)
                    curoperation = CommOperations.JXYGetInsInfo;
                else
                    return CommResult.OperationConflict;
            }

            CommResult r;
            try
            {
                r = SendFrame(1, 0, JXYGetInsInfoCmd, 0, null);
                if (r == CommResult.Success)
                {
                    if (!bgWorker_GetInsInfo.IsBusy)
                    {
                        r = CommResult.Processing;
                        bgWorker_GetInsInfo.RunWorkerAsync();//后台工作者线程进行异步操作
                    }
                    else
                    {
                        r = CommResult.ThreadBusy;
                    }
                }
            }
            catch
            {
                lock (operationlocker)
                {
                    curoperation = CommOperations.None;
                }
                r = CommResult.Exception;
                return CommResult.Exception;
            }

            lock (operationlocker)
            {
                curoperation = CommOperations.None;
            }
            return r;
        }

        public CommResult JXYSetInsInfo(byte[] sendbytes)//下发井下仪仪器信息
        {
            lock (operationlocker)
            {
                if (curoperation == CommOperations.None)
                    curoperation = CommOperations.JXYSetInsInfo;
                else
                    return CommResult.OperationConflict;
            }

            CommResult r;
            try
            {
                r = SendFrame(1, 0, JXYSetInsInfoCmd, 0, null);
                if (r == CommResult.Success)
                {

                    if (!bgWorker_SetInsInfo.IsBusy)
                    {
                        INSINFO = sendbytes;
                        r = CommResult.Processing;
                        bgWorker_SetInsInfo.RunWorkerAsync();//后台工作者线程进行异步操作
                    }
                    else
                    {
                        r = CommResult.ThreadBusy;
                    }
                }
            }
            catch
            {
                lock (operationlocker)
                {
                    curoperation = CommOperations.None;
                }
                r = CommResult.Exception;
                return CommResult.Exception;
            }

            lock (operationlocker)
            {
                curoperation = CommOperations.None;
            }

            return r;
        }

        public CommResult JXYTpWaveform(int ch)//提取换能器波形
        {

            lock (operationlocker)
            {
                if (curoperation == CommOperations.None)
                    curoperation = CommOperations.JXYTpWaveform;
                else
                    return CommResult.OperationConflict;
            }

            CommResult r;
            try
            {
                r = SendFrame(1, 0, (byte)(JXYTpWaveformCmd+ch), 0, null);
                if (r == CommResult.Success)
                {
                    if (!bgWorker_GetTpWaveform.IsBusy)
                    {
                        r = CommResult.Processing;
                        bgWorker_GetTpWaveform.RunWorkerAsync();//后台工作者线程进行异步操作
                    }
                    else
                    {
                        r = CommResult.ThreadBusy;
                    }
                }
            }
            catch
            {
                lock (operationlocker)
                {
                    curoperation = CommOperations.None;
                }
                r = CommResult.Exception;
                return CommResult.Exception;
            }

            lock (operationlocker)
            {
                curoperation = CommOperations.None;
            }
            return r;
        }
        public CommResult JXYSampleAll( ref byte[] b0,ref byte[] b1, ref byte[] b2, ref byte[] b3, ref byte[] b4, ref byte[] b5, ref byte[] b6, ref byte[] b7)//采样全部参数
        {
            lock (operationlocker)
            {
                if (curoperation == CommOperations.None)
                    curoperation = CommOperations.JXYSampleAll;
                else
                    return CommResult.OperationConflict;
            }

            CommResult r;
            try
            {
                r = SendFrame(1, 0, JXYSampleAllCmd, 0, null);
                if (r == CommResult.Success)
                {
                    r = ReadFrame(ref addr1, ref addr2, ref cmd, ref datalen, ref dat, JXYSampleAllTimeout1);
                    if (r == CommResult.Success)
                    {
                        b0[0] = dat[0];b0[1] = dat[1];
                        r = ReadFrame(ref addr1, ref addr2, ref cmd_2, ref datalen_2, ref dat, JXYSampleAllTimeout2);
                        if (r == CommResult.Success)
                        {
                            b1[0] = dat[0]; b1[1] = dat[1];
                            r = ReadFrame(ref addr1, ref addr2, ref cmd_2, ref datalen_2, ref dat, JXYSampleAllTimeout2);
                            if (r == CommResult.Success)
                            {
                                b2[0] = dat[0]; b2[1] = dat[1];
                                r = ReadFrame(ref addr1, ref addr2, ref cmd_2, ref datalen_2, ref dat, JXYSampleAllTimeout2);
                                if (r == CommResult.Success)
                                {
                                    b3[0] = dat[0]; b3[1] = dat[1];
                                    r = ReadFrame(ref addr1, ref addr2, ref cmd_2, ref datalen_2, ref dat, JXYSampleAllTimeout2);
                                    if (r == CommResult.Success)
                                    {
                                        b4[0] = dat[0]; b4[1] = dat[1];
                                        r = ReadFrame(ref addr1, ref addr2, ref cmd_2, ref datalen_2, ref dat, JXYSampleAllTimeout2);
                                        if (r == CommResult.Success)
                                        {
                                            b5[0] = dat[0]; b5[1] = dat[1];
                                            r = ReadFrame(ref addr1, ref addr2, ref cmd_2, ref datalen_2, ref dat, JXYSampleAllTimeout2);
                                            if (r == CommResult.Success)
                                            {
                                                b6[0] = dat[0]; b6[1] = dat[1];
                                                r = ReadFrame(ref addr1, ref addr2, ref cmd_2, ref datalen_2, ref dat, JXYSampleAllTimeout2);
                                                if (r == CommResult.Success)
                                                {
                                                    b7[0] = dat[0]; b7[1] = dat[1];
                                                    r = ReadFrame(ref addr1, ref addr2, ref cmd_2, ref datalen_2, ref dat, JXYSampleAllTimeout2);
                                                    if (r == CommResult.Success)
                                                    {
                                                        byte[] CRCData = new byte[8];
                                                        byte h = 0, l = 0;   
                                                        CRCData[0] = b0[0]; CRCData[1] = b0[1];
                                                        CRCData[2] = b1[0]; CRCData[3] = b1[1];
                                                        CRCData[4] = b2[0]; CRCData[5] = b2[1];
                                                        CRCData[6] = b3[0]; CRCData[7] = b3[1];
                                                        CRCData[8] = b4[0]; CRCData[9] = b4[1];
                                                        CRCData[10] = b5[0]; CRCData[11] = b5[1];
                                                        CRCData[12] = b6[0]; CRCData[13] = b6[1];
                                                        CRCData[14] = b7[0]; CRCData[15] = b7[1];
                                                        crc16(CRCData, 16, ref h, ref l);
                                                        if( (l == dat[0]) && (h == dat[1]) )
                                                        {
                                                            r = CommResult.Success;
                                                        }
                                                        else
                                                        {
                                                            r = CommResult.JXYCheckCError;
                                                        }                                                         
                                                    }                                                       
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                lock (operationlocker)
                {
                    curoperation = CommOperations.None;
                }
                return CommResult.Exception;
            }

            lock (operationlocker)
            {
                curoperation = CommOperations.None;
            }

            return r;
        }

        public CommResult JXYGetCoefficient()//提取压力温度系数
        {
            lock (operationlocker)
            {
                if (curoperation == CommOperations.None)
                    curoperation = CommOperations.JXYGetCoefficient;
                else
                    return CommResult.OperationConflict;
            }

            CommResult r;
            try
            {
                r = SendFrame(1, 0, JXYGetCoefficientPTCmd, 0, null);
                if (r == CommResult.Success)
                {
                    if (!bgWorker_GetPT.IsBusy)
                    {
                        r = CommResult.Processing;
                        bgWorker_GetPT.RunWorkerAsync();//后台工作者线程进行异步操作
                    }
                    else
                    {
                        r = CommResult.ThreadBusy;
                    }    
                }
            }
            catch
            {
                lock (operationlocker)
                {
                    curoperation = CommOperations.None;
                }
                r= CommResult.Exception;
                return CommResult.Exception;
            }

            lock (operationlocker)
            {
                curoperation = CommOperations.None;
            }

            return r;
        }

        public CommResult JXYSetCoefficient( byte[] sendbytes)//下发压力温度系数
        {
            lock (operationlocker)
            {
                if (curoperation == CommOperations.None)
                    curoperation = CommOperations.JXYSetCoefficient;
                else
                    return CommResult.OperationConflict;
            }

            CommResult r;
           
            try
            {
                r = SendFrame(1, 0, JXYSetCoefficientPTCmd, 0, null);
                if (r == CommResult.Success)
                {
                    if (!bgWorker_SetPT.IsBusy)
                    {
                        PT = sendbytes;
                        r = CommResult.Processing;
                        bgWorker_SetPT.RunWorkerAsync();//后台工作者线程进行异步操作
                    }
                    else
                    {
                        r = CommResult.ThreadBusy;
                    }
                }
            }
            catch
            {
                lock (operationlocker)
                {
                    curoperation = CommOperations.None;
                }
                r = CommResult.Exception;
                return CommResult.Exception;
            }

            lock (operationlocker)
            {
                curoperation = CommOperations.None;
            }

            return r;
        }

        public CommResult JXYGetCoefficient2()//提取流量系数
        {
            lock (operationlocker)
            {
                if (curoperation == CommOperations.None)
                    curoperation = CommOperations.JXYGetCoefficient2;
                else
                    return CommResult.OperationConflict;
            }

            CommResult r;
            try
            {
                r = SendFrame(1, 0, JXYGetCoefficientFCmd, 0, null);
                if (r == CommResult.Success)
                {
                    if (!bgWorker_GetF.IsBusy)
                    {
                        r = CommResult.Processing;
                        bgWorker_GetF.RunWorkerAsync();//后台工作者线程进行异步操作
                    }
                    else
                    {
                        r = CommResult.ThreadBusy;
                    }
                }
            }
            catch
            {
                lock (operationlocker)
                {
                    curoperation = CommOperations.None;
                }
                r = CommResult.Exception;
                return CommResult.Exception;
            }

            lock (operationlocker)
            {
                curoperation = CommOperations.None;
            }
            return r;
        }

        public CommResult JXYSetCoefficient2(byte[] sendbytes)//下发流量系数
        {
            lock (operationlocker)
            {
                if (curoperation == CommOperations.None)
                    curoperation = CommOperations.JXYSetCoefficient2;
                else
                    return CommResult.OperationConflict;
            }

            CommResult r;
            try
            {
                r = SendFrame(1, 0, JXYSetCoefficientFCmd, 0, null);
                if (r == CommResult.Success)
                {

                    if (!bgWorker_SetF.IsBusy)
                    {
                        F = sendbytes;
                        r = CommResult.Processing;
                        bgWorker_SetF.RunWorkerAsync();//后台工作者线程进行异步操作
                    }
                    else
                    {
                        r = CommResult.ThreadBusy;
                    }
                }
            }
            catch
            {
                lock (operationlocker)
                {
                    curoperation = CommOperations.None;
                }
                r = CommResult.Exception;
                return CommResult.Exception;
            }

            lock (operationlocker)
            {
                curoperation = CommOperations.None;
            }

            return r;
        }

        public CommResult JXYGetCoefficientAll()//提取全部系数
        {
            lock (operationlocker)
            {
                if (curoperation == CommOperations.None)
                    curoperation = CommOperations.JXYGetCoefficientAll;
                else
                    return CommResult.OperationConflict;
            }

            CommResult r;
            try
            {
                r = SendFrame(1, 0, JXYGetCoefficientAllCmd, 0, null);
                if (r == CommResult.Success)
                {
                    if (!bgWorker_GetPTF.IsBusy)
                    {
                        r = CommResult.Processing;
                        bgWorker_GetPTF.RunWorkerAsync();//后台工作者线程进行异步操作
                    }
                    else
                    {
                        r = CommResult.ThreadBusy;
                    }
                }               
            }
            catch
            {
                lock (operationlocker)
                {
                    curoperation = CommOperations.None;
                }
                r = CommResult.Exception;
                return CommResult.Exception;
            }

            lock (operationlocker)
            {
                curoperation = CommOperations.None;
            }
            return r;
        }

        public CommResult JXYFormat()//删除测试数据，整机格式化
        {
            lock (operationlocker)
            {
                if (curoperation == CommOperations.None)
                    curoperation = CommOperations.JXYGetCoefficient2;
                else
                    return CommResult.OperationConflict;
            }

            CommResult r;

            try
            {
                r = SendFrame(1, 0, JXYFormatCmd, 0, null);
                if (r == CommResult.Success)
                {
                    if (!bgWorker_DelData.IsBusy)
                    {
                        r = CommResult.Processing;
                        bgWorker_DelData.RunWorkerAsync();//后台工作者线程进行异步操作
                    }
                    else
                    {
                        r = CommResult.ThreadBusy;
                    }
                }
                else
                {
                    lock (operationlocker)
                    {
                        curoperation = CommOperations.None;
                    }
                     
                }
            }
            catch
            {
                lock (operationlocker)
                {
                    curoperation = CommOperations.None;
                }
                return CommResult.Exception;
            }

            return r;
        }

        public CommResult JXYGetTestInfo()//提取测试信息
        {
            lock (operationlocker)
            {
                if (curoperation == CommOperations.None)
                    curoperation = CommOperations.JXYGetTestInfo;
                else
                    return CommResult.OperationConflict;
            }

            CommResult r;
            try
            {
                r = SendFrame(1, 0, JXYGetTestInfoCmd, 0, null);
                if (r == CommResult.Success)
                {
                    if (!bgWorker_GetTestInfo.IsBusy)
                    {
                        r = CommResult.Processing;
                        bgWorker_GetTestInfo.RunWorkerAsync();//后台工作者线程进行异步操作
                    }
                    else
                    {
                        r = CommResult.ThreadBusy;
                    }
                }
            }
            catch
            {
                lock (operationlocker)
                {
                    curoperation = CommOperations.None;
                }
                r = CommResult.Exception;
            }

            lock (operationlocker)
            {
                curoperation = CommOperations.None;
            }

            return r;
        }

        public CommResult JXYGetTestData(UInt16 blockindex,UInt16  blockcount,ref byte[] coe)//提取测试数据，此方法中只做一些前期工作，大量的数据提取放在单独的后台线程中处理
        {
            lock (operationlocker)
            {
                if (curoperation == CommOperations.None)
                    curoperation = CommOperations.JXYGetTestData;
                else
                    return CommResult.OperationConflict;
            }

            BlockCount = blockcount;
            COE = coe;
            CommResult r;
            try
            {
                QueryPerformanceFrequency(ref Frequency);
                r = SendFrame(1, 0, JXYGetTestDataCmd, 0, null);
                if (r == CommResult.Success)//发送提取测试数据命令
                {
                    r = CommResult.SendFrameFail;                 
                    QueryPerformanceCounter(ref Counter);
                    while (true)
                    {
                        QueryPerformanceCounter(ref Counter2);
                        if ((Counter2 - Counter) * 1000 > (Frequency * 40))//精确定时 
                        {
                            Counter = Counter2;
                            break;
                        }
                    }
                    r = SendFrame(1, 0, (byte)(blockindex), 0, null);
                    if (r == CommResult.Success)//发送块索引低字节
                    {
                        r = CommResult.SendFrameFail;
                        QueryPerformanceCounter(ref Counter);
                        while (true)
                        {
                            QueryPerformanceCounter(ref Counter2);
                            if ((Counter2 - Counter) * 1000 > (Frequency * 40)) //精确定时
                            {
                                Counter = Counter2;
                                break;
                            }
                        }
                        r = SendFrame(1, 0, (byte)(blockindex >> 8), 0, null);
                        if (r == CommResult.Success)//发送块索引高字节
                        {
                            r = CommResult.SendFrameFail;
                            QueryPerformanceCounter(ref Counter);
                            while (true)
                            {
                                QueryPerformanceCounter(ref Counter2);
                                if ((Counter2 - Counter) * 1000 > (Frequency * 40))//精确定时 
                                {
                                    Counter = Counter2;
                                    break;
                                }
                            }
                            r = SendFrame(1, 0, (byte)(blockcount), 0, null);
                            if (r == CommResult.Success)//发送包数低字节
                            {
                                r = CommResult.SendFrameFail;
                                QueryPerformanceCounter(ref Counter);
                                while (true)
                                {
                                    QueryPerformanceCounter(ref Counter2);
                                    if ((Counter2 - Counter) * 1000 > (Frequency * 40))//精确定时20ms
                                    {
                                        Counter = Counter2;
                                        break;
                                    }
                                }
                                r = SendFrame(1, 0, (byte)(blockcount >> 8), 0, null);
                                if ( r== CommResult.Success)//发送包数高字节
                                {                           
                                    if (!bgWorker_DelData.IsBusy)
                                    {
                                        r = CommResult.Processing;
                                        bgWorker_GetPTData.RunWorkerAsync();//后台工作者线程进行异步操作
                                    }
                                    else
                                    {
                                        r = CommResult.ThreadBusy;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                lock (operationlocker)
                {
                    curoperation = CommOperations.None;
                }
                r = CommResult.Exception;
                return CommResult.Exception;
            }

            lock (operationlocker)
            {
                curoperation = CommOperations.None;
            }

            return r;
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            bgWorker_DelData.CancelAsync();
        }
        DateTime StartTime,StopTime;

        private void DoWork_Handler_DelData(object sender, DoWorkEventArgs args)//线程实际操作方法，该方法在非UI线程中异步执行 
        {
            CommResult r;

            _mw.LockFlag = true;

            BackgroundWorker worker = sender as BackgroundWorker;
            _mw.PBS2.PS = 0;
            _mw.PBS2.MV = 500;//50秒超时等待
            StartTime = DateTime.Now;
            System.Windows.Forms.Application.DoEvents();//在此强制更新界面，防止其它地方将进度条属性更改后界面不能及时更新
            for (int i = 1; i <= _mw.PBS2.MV; i++)
            {
                r = ReadFrame(ref addr1, ref addr2, ref cmd, ref datalen, ref dat, JXYFormatTimeout);
                if (r == CommResult.Success)
                {
                    break;
                }

                if (worker.CancellationPending)
                {
                    args.Cancel = true;
                    break;
                }
                else
                {
                    worker.ReportProgress(i);
                    Thread.Sleep(100);
                }
            }
        }
        private void ProgressChanged_Handler_DelData(object sender, ProgressChangedEventArgs args)//线程状态变化响应方法，该方法在UI线程中同步执行
        {
            _mw.PBS2.PS = args.ProgressPercentage;
            //System.Windows.Forms.Application.DoEvents();//由于UI线程没有阻塞，在此不用强制刷新主界面
        }
        private void RunWorkerCompleted_Handler_DelData(object sender, RunWorkerCompletedEventArgs args)//线程结束方法，该方法在UI线程中同步执行
        {
            CommResult r;
            if (args.Cancelled)
            {
                _mw.RecordSave(3, "删除数据取消", true);
            }
            else
            {
                if(_mw.PBS2.PS< _mw.PBS2.MV)
                {
                    StopTime = DateTime.Now;
                    _mw.RecordSave(3, string.Format("删除数据成功，实际耗时：{0}分{1}秒", (StopTime - StartTime).Minutes, (StopTime - StartTime).Seconds), true);
                }
                else
                {
                    _mw.RecordSave(3, "删除数据失败，读取返回帧超时", true);
                }
                
                //r = ReadFrame(ref addr1, ref addr2, ref cmd, ref datalen, ref dat, JXYFormatTimeout);
                //if (r == CommResult.Success)
                //{
                //    if (CRCCheck(dat) == false)
                //    {
                //        _mw.RecordSave(3, "上传帧校验错误", true);
                //    }
                //    else
                //    {
                //        if (cmd == (JXYFormatCmd >> 4))
                //        {
                //            if (datalen == 2)
                //            {
                //                if ((dat[0] == 0) && (dat[1] == 0))
                //                {
                //                    _mw.RecordSave(3, "删除数据成功", true);
                //                }
                //                else
                //                {
                //                    _mw.RecordSave(3, "返回数据错误(0x" + dat[1].ToString("X2") + dat[0].ToString("X2") + ")", true);
                //                }
                //            }
                //            else
                //            {
                //                _mw.RecordSave(3, "返回帧数据长度错误(0x" + datalen.ToString("X2") + ")", true);
                //            }
                //        }
                //        else
                //        {
                //            _mw.RecordSave(3, "返回帧命令码错误(0x" + cmd.ToString("X2") + ")", true);
                //        }
                //    }
                //}
                //else
                //{
                //    _mw.RecordSave(3, "读取返回帧超时", true);
                //}
            }
            _mw.PBS2.PS = 0;//进度条归零
            lock (operationlocker)
            {
                curoperation = CommOperations.None;//状态机复位
            }
            _mw.LockFlag = false;
        }

        private void DoWork_Handler_SetPT(object sender, DoWorkEventArgs args)//线程实际操作方法，该方法在非UI线程中异步执行 
        {
            CommResult r;
            _mw.LockFlag = true;
            BackgroundWorker worker = sender as BackgroundWorker;

            _mw.PBS2.PS = 0;
            _mw.PBS2.MV = 128;//总需要提取的帧数，设置成进度条最大值

            System.Windows.Forms.Application.DoEvents();//在此强制更新界面，防止其它地方将进度条属性更改后界面不能及时更新

            QueryPerformanceFrequency(ref Frequency);
            QueryPerformanceCounter(ref Counter);

            while (true)
            {
                if (worker.CancellationPending)
                {
                    args.Cancel = true;
                    break;
                }
                else
                {
                    for (int i = 0; i < 128; i++)
                    {
                        while (true)
                        {
                            QueryPerformanceCounter(ref Counter2);
                            if ((Counter2 - Counter) * 1000 > (Frequency * 40))//精确定时40ms
                            {
                                Counter = Counter2;
                                break;
                            }
                        }

                        r = SendFrame(1, 0, PT[i], 0, null);
                        if (r == CommResult.Success)
                        {
                            System.Windows.Forms.Application.DoEvents();
                            worker.ReportProgress(i + 1);
                        }
                        else
                        {
                            args.Cancel = true;
                            break;
                        }
                    }
                    break;
                }
            }   
        }
        private void ProgressChanged_Handler_SetPT(object sender, ProgressChangedEventArgs args)//线程状态变化响应方法，该方法在UI线程中同步执行
        {
            _mw.PBS2.PS = args.ProgressPercentage;
        }
        private void RunWorkerCompleted_Handler_SetPT(object sender, RunWorkerCompletedEventArgs args)//线程结束方法，该方法在UI线程中同步执行
        {
            if (args.Cancelled)
            {
                _mw.RecordSave(3, "下发压力温度系数取消或中止", true);
            }
            else
            {
                CommResult r;
                byte h = 0;
                byte l = 0;
                r = ReadFrame(ref addr1, ref addr2, ref cmd, ref datalen, ref dat, JXYSetCoefficientPTTimeout);
                if (r == CommResult.Success)
                {
                    if (CRCCheck(dat) == false)
                    {
                        r = CommResult.DMXCheckError;
                    }
                    else
                    {
                        if (cmd == (JXYSetCoefficientPTCmd >> 4))
                        {
                            if (datalen == 2)
                            {
                                h = dat[1];
                                l = dat[0];
                                r = CommResult.Success;
                            }
                            else
                            {
                                r = CommResult.DataLenError;
                            }
                        }
                        else
                            r = CommResult.ReturnCmdError;
                    }
                }

                if (r == CommResult.Success)
                {
                    if ((h == 0x00) && (l == 0x00))
                    {
                        _mw.RecordSave(2, "下发压力温度系数成功。", true);
                    }
                    else
                    {
                        _mw.RecordSave(3, string.Format("下发压力温度系数失败！数据已发送完毕，下位机返回帧：0x{0:X4}", h * 256 + l), true);
                    }
                }
                else
                {

                    _mw.RecordSave(3, "下发压力温度系数失败！发送数据完毕，但获取反馈帧时出错。" + _mw.CommError(r), true);
                }
            }

            lock (operationlocker)
            {
                curoperation = CommOperations.None;//状态机复位
            }

            _mw.LockFlag = false;
            _mw.PBS2.PS = 0;//进度条归零       
        }

        private void DoWork_Handler_GetPT(object sender, DoWorkEventArgs args)//线程实际操作方法，该方法在非UI线程中异步执行 
        {
            CommResult r;
            _mw.LockFlag = true;
            BackgroundWorker worker = sender as BackgroundWorker;

            _mw.PBS2.PS = 0;
            _mw.PBS2.MV = 64;//总需要提取的帧数，设置成进度条最大值

            System.Windows.Forms.Application.DoEvents();//在此强制更新界面，防止其它地方将进度条属性更改后界面不能及时更新

            while (true)
            {
                if (worker.CancellationPending)
                {
                    args.Cancel = true;
                    break;
                }
                else
                {
                    for (int i = 0; i < 64; i++)
                    {
                        r = ReadFrame(ref addr1, ref addr2, ref cmd, ref datalen, ref dat, JXYGetCoefficientPTTimeout);
                        if (r == CommResult.Success)
                        {
                            PT[i * 2 + 1] = dat[1];
                            PT[i * 2 + 0] = dat[0];
                            System.Windows.Forms.Application.DoEvents();
                            worker.ReportProgress(i + 1);
                        }
                        else
                        {
                            args.Cancel = true;
                            break;
                        }
                    }
                    break;
                }
            }
        }
        private void ProgressChanged_Handler_GetPT(object sender, ProgressChangedEventArgs args)//线程状态变化响应方法，该方法在UI线程中同步执行
        {
            _mw.PBS2.PS = args.ProgressPercentage;
        }
        private void RunWorkerCompleted_Handler_GetPT(object sender, RunWorkerCompletedEventArgs args)//线程结束方法，该方法在UI线程中同步执行
        {
            if (args.Cancelled)
            {
                _mw.RecordSave(3, "提取压力温度系数取消或中止", true);
            }
            else
            {
                _mw.RecordSave(3, "提取压力温度系数完毕", true);
                _mw.PTView(PT);
            }

            lock (operationlocker)
            {
                curoperation = CommOperations.None;//状态机复位
            }

            _mw.LockFlag = false;
            _mw.PBS2.PS = 0;//进度条归零
        }

        private void DoWork_Handler_SetF(object sender, DoWorkEventArgs args)//线程实际操作方法，该方法在非UI线程中异步执行 
        {
            CommResult r;
            _mw.LockFlag = true;
            BackgroundWorker worker = sender as BackgroundWorker;

            _mw.PBS3.PS = 0;
            _mw.PBS3.MV = 128;//总需要提取的帧数，设置成进度条最大值

            System.Windows.Forms.Application.DoEvents();//在此强制更新界面，防止其它地方将进度条属性更改后界面不能及时更新

            QueryPerformanceFrequency(ref Frequency);
            QueryPerformanceCounter(ref Counter);

            while (true)
            {
                if (worker.CancellationPending)
                {
                    args.Cancel = true;
                    break;
                }
                else
                {
                    for (int i = 0; i < 128; i++)
                    {
                        while (true)
                        {
                            QueryPerformanceCounter(ref Counter2);
                            if ((Counter2 - Counter) * 1000 > (Frequency * 40))//精确定时40ms
                            {
                                Counter = Counter2;
                                break;
                            }
                        }

                        r = SendFrame(1, 0, F[i], 0, null);
                        if (r == CommResult.Success)
                        {
                            System.Windows.Forms.Application.DoEvents();
                            worker.ReportProgress(i + 1);
                        }
                        else
                        {
                            args.Cancel = true;
                            break;
                        }
                    }
                    break;
                }
            }
        }
        private void ProgressChanged_Handler_SetF(object sender, ProgressChangedEventArgs args)//线程状态变化响应方法，该方法在UI线程中同步执行
        {
            _mw.PBS3.PS = args.ProgressPercentage;
        }
        private void RunWorkerCompleted_Handler_SetF(object sender, RunWorkerCompletedEventArgs args)//线程结束方法，该方法在UI线程中同步执行
        {
            if (args.Cancelled)
            {
                _mw.RecordSave(3, "下发流量系数取消或中止", true);
            }
            else
            {
                CommResult r;
                byte h = 0;
                byte l = 0;
                r = ReadFrame(ref addr1, ref addr2, ref cmd, ref datalen, ref dat, JXYSetCoefficientFTimeout);
                if (r == CommResult.Success)
                {
                    if (CRCCheck(dat) == false)
                    {
                        r = CommResult.DMXCheckError;
                    }
                    else
                    {
                        if (cmd == (JXYSetCoefficientFCmd >> 4))
                        {
                            if (datalen == 2)
                            {
                                h = dat[1];
                                l = dat[0];
                                r = CommResult.Success;
                            }
                            else
                            {
                                r = CommResult.DataLenError;
                            }
                        }
                        else
                            r = CommResult.ReturnCmdError;
                    }
                }
                else
                    r = CommResult.USBReceiveTimeout;

                if (r == CommResult.Success)
                {
                    if ((h == 0x00) && (l == 0x00))
                    {
                        _mw.RecordSave(2, "下发流量系数成功。", true);
                    }
                    else
                    {
                        _mw.RecordSave(3, string.Format("下发流量系数失败！数据已发送完毕，下位机返回帧：0x{0:X4}", h * 256 + l), true);
                    }
                }
                else
                {

                    _mw.RecordSave(3, "下发流量系数失败！发送数据完毕，但获取反馈帧时出错。" + _mw.CommError(r), true);
                }
            }

            lock (operationlocker)
            {
                curoperation = CommOperations.None;//状态机复位
            }

            _mw.LockFlag = false;
            _mw.PBS3.PS = 0;//进度条归零
        }

        private void DoWork_Handler_GetF(object sender, DoWorkEventArgs args)//线程实际操作方法，该方法在非UI线程中异步执行 
        {
            CommResult r;
            _mw.LockFlag = true;
            BackgroundWorker worker = sender as BackgroundWorker;

            _mw.PBS3.PS = 0;
            _mw.PBS3.MV = 64;//总需要提取的帧数，设置成进度条最大值

            System.Windows.Forms.Application.DoEvents();//在此强制更新界面，防止其它地方将进度条属性更改后界面不能及时更新

            while (true)
            {
                if (worker.CancellationPending)
                {
                    args.Cancel = true;
                    break;
                }
                else
                {
                    for (int i = 0; i < 64; i++)
                    {
                        r = ReadFrame(ref addr1, ref addr2, ref cmd, ref datalen, ref dat, JXYGetCoefficientFTimeout);
                        if (r== CommResult.Success)
                        {
                            F[i * 2 + 1] = dat[1];
                            F[i * 2 + 0] = dat[0];
                            System.Windows.Forms.Application.DoEvents();
                            worker.ReportProgress(i + 1);
                        }
                        else
                        {
                            args.Cancel = true;
                            break;
                        }
                    }
                    break;
                }
            }
        }
        private void ProgressChanged_Handler_GetF(object sender, ProgressChangedEventArgs args)//线程状态变化响应方法，该方法在UI线程中同步执行
        {
            _mw.PBS3.PS = args.ProgressPercentage;
        }
        private void RunWorkerCompleted_Handler_GetF(object sender, RunWorkerCompletedEventArgs args)//线程结束方法，该方法在UI线程中同步执行
        {
            if (args.Cancelled)
            {
                _mw.RecordSave(3, "提取流量系数取消或中止", true);
            }
            else
            {
                _mw.RecordSave(3, "提取流量系数完毕", true);
                _mw.FView(F);
            }

            lock (operationlocker)
            {
                curoperation = CommOperations.None;//状态机复位
            }

            _mw.LockFlag = false;
            _mw.PBS3.PS = 0;//进度条归零
        }

        private void DoWork_Handler_GetPTData(object sender, DoWorkEventArgs args)//线程实际操作方法，该方法在非UI线程中异步执行 
        {
            CommResult r;
            _mw.LockFlag = true;
            BackgroundWorker worker = sender as BackgroundWorker;

            _mw.PBS2.PS = 0;
            _mw.PBS2.MV = BlockCount * 64 / 2;//总需要提取的帧数，设置成进度条最大值

            System.Windows.Forms.Application.DoEvents();//在此强制更新界面，防止其它地方将进度条属性更改后界面不能及时更新

            while(true)
            {
                if (worker.CancellationPending)
                {
                    args.Cancel = true;
                    break;
                }
                else
                {                 
                    for (int i = 0; i < (BlockCount * 64 / 2); i++)
                    {
                        r = ReadFrame(ref addr1, ref addr2, ref cmd, ref datalen, ref dat, JXYGetTestDataTimeout);
                        if (r == CommResult.Success)
                        {
                            COE[i * 2 + 1] = dat[1];
                            COE[i * 2 + 0] = dat[0];
                         
                            System.Windows.Forms.Application.DoEvents();
                            worker.ReportProgress(i+1);
                        }
                        else
                        {
                            args.Cancel = true;
                            break;
                        }
                    }
                    break;
                }
            }
        }
        private void ProgressChanged_Handler_GetPTData(object sender, ProgressChangedEventArgs args)//线程状态变化响应方法，该方法在UI线程中同步执行
        {
            _mw.PBS2.PS = args.ProgressPercentage;
        }
        private void RunWorkerCompleted_Handler_GetPTData(object sender, RunWorkerCompletedEventArgs args)//线程结束方法，该方法在UI线程中同步执行
        {
            if (args.Cancelled)
            {
                _mw.RecordSave(3, "提取数据取消或中止", true);
            }
            else
            {                            
                _mw.RecordSave(3, "提取数据完毕", true);
                _mw.PTDataView();
            }
           
            lock (operationlocker)
            {
                curoperation = CommOperations.None;//状态机复位
            }

            _mw.LockFlag = false;
            _mw.PBS2.PS = 0;//进度条归零
        }


        private void DoWork_Handler_GetPTF(object sender, DoWorkEventArgs args)//线程实际操作方法，该方法在非UI线程中异步执行 
        {
            CommResult r;
            _mw.LockFlag = true;
            BackgroundWorker worker = sender as BackgroundWorker;

            _mw.PBS4001.PS = 0;
            _mw.PBS4001.MV = 128;//总需要提取的帧数，设置成进度条最大值

            System.Windows.Forms.Application.DoEvents();//在此强制更新界面，防止其它地方将进度条属性更改后界面不能及时更新

            while (true)
            {
                if (worker.CancellationPending)
                {
                    args.Cancel = true;
                    break;
                }
                else
                {
                    for (int i = 0; i < 128; i++)
                    {
                        r = ReadFrame(ref addr1, ref addr2, ref cmd, ref datalen, ref dat, JXYGetCoefficientAllTimeout);
                        if (r == CommResult.Success)
                        {
                            PTF[i * 2 + 1] = dat[1];
                            PTF[i * 2 + 0] = dat[0];
                            System.Windows.Forms.Application.DoEvents();
                            worker.ReportProgress(i + 1);
                        }
                        else
                        {
                            args.Cancel = true;
                            break;
                        }
                    }
                    break;
                }
            }
        }
        private void ProgressChanged_Handler_GetPTF(object sender, ProgressChangedEventArgs args)//线程状态变化响应方法，该方法在UI线程中同步执行
        {
            _mw.PBS4001.PS = args.ProgressPercentage;
        }
        private void RunWorkerCompleted_Handler_GetPTF(object sender, RunWorkerCompletedEventArgs args)//线程结束方法，该方法在UI线程中同步执行
        {
            if (args.Cancelled)
            {
                _mw.RecordSave(3, "提取整机系数取消或中止", true);
            }
            else
            {
                _mw.RecordSave(3, "提取整机系数完毕。", true);
                _mw.PTFView(PTF);
                _mw.Status_FileName1.Content = "已提取";
            }

            lock (operationlocker)
            {
                curoperation = CommOperations.None;//状态机复位
            }

            _mw.LockFlag = false;
            _mw.PBS4001.PS = 0;//进度条归零
        }

        private void DoWork_Handler_GetTestInfo(object sender, DoWorkEventArgs args)//线程实际操作方法，该方法在非UI线程中异步执行 
        {
            CommResult r;
            _mw.LockFlag = true;
            BackgroundWorker worker = sender as BackgroundWorker;

            _mw.PBS2.PS = 0;
            _mw.PBS2.MV = 128;//总需要提取的帧数，设置成进度条最大值

            System.Windows.Forms.Application.DoEvents();//在此强制更新界面，防止其它地方将进度条属性更改后界面不能及时更新

            while (true)
            {
                if (worker.CancellationPending)
                {
                    args.Cancel = true;
                    break;
                }
                else
                {
                    for (int i = 0; i < 128; i++)
                    {
                        r = ReadFrame(ref addr1, ref addr2, ref cmd, ref datalen, ref dat, JXYGetTestInfoTimeout);
                        if (r == CommResult.Success)
                        {
                            TESTINFO[i * 2 + 1] = dat[1];
                            TESTINFO[i * 2 + 0] = dat[0];
                            System.Windows.Forms.Application.DoEvents();
                            worker.ReportProgress(i + 1);
                        }
                        else
                        {
                            args.Cancel = true;
                            break;
                        }
                    }
                    break;
                }
            }
        }
        private void ProgressChanged_Handler_GetTestInfo(object sender, ProgressChangedEventArgs args)//线程状态变化响应方法，该方法在UI线程中同步执行
        {
            _mw.PBS2.PS = args.ProgressPercentage;
        }
        private void RunWorkerCompleted_Handler_GetTestInfo(object sender, RunWorkerCompletedEventArgs args)//线程结束方法，该方法在UI线程中同步执行
        {
            if (args.Cancelled)
            {
                _mw.RecordSave(3, "提取测试信息取消或中止", true);
            }
            else
            {
                _mw.RecordSave(3, "提取测试信息完毕", true);
                _mw.TestInfoView(TESTINFO);           
            }

            lock (operationlocker)
            {
                curoperation = CommOperations.None;//状态机复位
            }

            _mw.LockFlag = false;
            _mw.PBS2.PS = 0;//进度条归零
        }

        private void DoWork_Handler_GetWaveform(object sender, DoWorkEventArgs args)//线程实际操作方法，该方法在非UI线程中异步执行 
        {
            CommResult r;
            _mw.LockFlag = true;
            BackgroundWorker worker = sender as BackgroundWorker;

            _mw.PBS6.PS = 0;
            _mw.PBS6.MV = 160;//总需要提取的帧数，设置成进度条最大值

            System.Windows.Forms.Application.DoEvents();//在此强制更新界面，防止其它地方将进度条属性更改后界面不能及时更新

             wave = new byte[160 * 7];//用于存放信号波形

            while (true)
            {
                if (worker.CancellationPending)
                {
                    args.Cancel = true;
                    break;
                }
                else
                {
                    for (int i = 0; i < 160; i++)
                    {
                        r = ReadFrame(ref addr1, ref addr2, ref cmd, ref datalen, ref dat, DMXGetWaveformTimeout);
                        if (r == CommResult.Success)
                        {
                            wave[i * 7 + 0] = dat[0];
                            wave[i * 7 + 1] = dat[1];
                            wave[i * 7 + 2] = dat[2];
                            wave[i * 7 + 3] = dat[3];
                            wave[i * 7 + 4] = dat[4];
                            wave[i * 7 + 5] = dat[5];
                            wave[i * 7 + 6] = dat[6];
                            System.Windows.Forms.Application.DoEvents();
                            worker.ReportProgress(i + 1);
                        }
                        else
                        {
                            args.Cancel = true;
                            break;
                        }
                        //以下打印时通讯不可靠，帧间隔约为37ms，不打印时通讯可靠
                      //  Debug.WriteLine(System.DateTime.Now.ToString() + ":" + System.DateTime.Now.Millisecond.ToString());
                    }
                    break;
                }
            }
        }
        private void ProgressChanged_Handler_GetWaveform(object sender, ProgressChangedEventArgs args)//线程状态变化响应方法，该方法在UI线程中同步执行
        {
            _mw.PBS6.PS = args.ProgressPercentage;
        }
        private void RunWorkerCompleted_Handler_GetWaveform(object sender, RunWorkerCompletedEventArgs args)//线程结束方法，该方法在UI线程中同步执行
        {
            if (args.Cancelled)
            {
                _mw.RecordSave(3, "提取信号波形取消或中止", true);
            }
            else
            {
                _mw.WaveformView(wave);
                _mw.RecordSave(3, "提取信号波形完毕", true);
                _mw.TabItem601.IsSelected = true;

            }

            lock (operationlocker)
            {
                curoperation = CommOperations.None;//状态机复位
            }

            _mw.LockFlag = false;
            _mw.PBS6.PS = 0;//进度条归零
        }


        private void DoWork_Handler_GetTpWaveform(object sender, DoWorkEventArgs args)//线程实际操作方法，该方法在非UI线程中异步执行 
        {
            CommResult r;
            _mw.LockFlag = true;
            BackgroundWorker worker = sender as BackgroundWorker;

            _mw.PBS3.PS = 0;
            _mw.PBS3.MV = 1800;//总需要提取的帧数，设置成进度条最大值

            System.Windows.Forms.Application.DoEvents();//在此强制更新界面，防止其它地方将进度条属性更改后界面不能及时更新

            tpwave = new int[1800];//用于存放换能器波形

            while (true)
            {
                if (worker.CancellationPending)
                {
                    args.Cancel = true;
                    break;
                }
                else
                {
                    for (int i = 0; i < 1800; i++)
                    {
                        r = ReadFrame(ref addr1, ref addr2, ref cmd, ref datalen, ref dat, JXYTpWaveformCmd);
                        if (r == CommResult.Success)
                        {
                            tpwave[i] = PointerConvert.ToInt16(dat,0);                  
                            System.Windows.Forms.Application.DoEvents();
                            worker.ReportProgress(i + 1);
                        }
                        else
                        {
                            args.Cancel = true;
                            break;
                        }
                        //以下打印时通讯不可靠，帧间隔约为37ms，不打印时通讯可靠
                        //  Debug.WriteLine(System.DateTime.Now.ToString() + ":" + System.DateTime.Now.Millisecond.ToString());
                    }
                    break;
                }
            }
        }
        private void ProgressChanged_Handler_GetTpWaveform(object sender, ProgressChangedEventArgs args)//线程状态变化响应方法，该方法在UI线程中同步执行
        {
            _mw.PBS3.PS = args.ProgressPercentage;
        }
        private void RunWorkerCompleted_Handler_GetTpWaveform(object sender, RunWorkerCompletedEventArgs args)//线程结束方法，该方法在UI线程中同步执行
        {
            if (args.Cancelled)
            {
                _mw.RecordSave(3, "提取换能器波形取消或中止", true);
            }
            else
            {
                _mw.TpWaveformView(tpwave);
                _mw.RecordSave(3, "提取换能器波形完毕", true);

            }

            lock (operationlocker)
            {
                curoperation = CommOperations.None;//状态机复位
            }

            _mw.LockFlag = false;
            _mw.PBS3.PS = 0;//进度条归零
        }

        private void DoWork_Handler_GetInsInfo(object sender, DoWorkEventArgs args)//线程实际操作方法，该方法在非UI线程中异步执行 
        {
            CommResult r;
            _mw.LockFlag = true;
            BackgroundWorker worker = sender as BackgroundWorker;

            _mw.PBS6.PS = 0;
            _mw.PBS6.MV = 160;//总需要提取的帧数，设置成进度条最大值

            System.Windows.Forms.Application.DoEvents();//在此强制更新界面，防止其它地方将进度条属性更改后界面不能及时更新

            INSINFO = new byte[320]; 

            while (true)
            {
                if (worker.CancellationPending)
                {
                    args.Cancel = true;
                    break;
                }
                else
                {
                    for (int i = 0; i < 160; i++)
                    {
                        r = ReadFrame(ref addr1, ref addr2, ref cmd, ref datalen, ref dat, JXYGetInsInfoTimeout);
                        if (r == CommResult.Success)
                        {
                            INSINFO[i * 2 + 1] = dat[1];
                            INSINFO[i * 2 + 0] = dat[0];
                            System.Windows.Forms.Application.DoEvents();
                            worker.ReportProgress(i + 1);
                        }
                        else
                        {
                            args.Cancel = true;
                            break;
                        }
                        //以下打印时通讯不可靠，帧间隔约为37ms，不打印时通讯可靠
                        //  Debug.WriteLine(System.DateTime.Now.ToString() + ":" + System.DateTime.Now.Millisecond.ToString());
                    }
                    break;
                }
            }
        }
        private void ProgressChanged_Handler_GetInsInfo(object sender, ProgressChangedEventArgs args)//线程状态变化响应方法，该方法在UI线程中同步执行
        {
            _mw.PBS6.PS = args.ProgressPercentage;
        }
        private void RunWorkerCompleted_Handler_GetInsInfo(object sender, RunWorkerCompletedEventArgs args)//线程结束方法，该方法在UI线程中同步执行
        {
            if (args.Cancelled)
            {
                _mw.RecordSave(3, "提取井下仪仪器信息取消或中止", true);
            }
            else
            {
                _mw.InsInfoView(INSINFO);
                _mw.RecordSave(3, "提取井下仪仪器信息完毕", true);

            }

            lock (operationlocker)
            {
                curoperation = CommOperations.None;//状态机复位
            }

            _mw.LockFlag = false;
            _mw.PBS6.PS = 0;//进度条归零
        }

        private void DoWork_Handler_SetInsInfo(object sender, DoWorkEventArgs args)//线程实际操作方法，该方法在非UI线程中异步执行 
        {
            CommResult r;
            _mw.LockFlag = true;
            BackgroundWorker worker = sender as BackgroundWorker;

            _mw.PBS6.PS = 0;
            _mw.PBS6.MV = 320;//总需要提取的帧数，设置成进度条最大值

            System.Windows.Forms.Application.DoEvents();//在此强制更新界面，防止其它地方将进度条属性更改后界面不能及时更新

            QueryPerformanceFrequency(ref Frequency);
            QueryPerformanceCounter(ref Counter);

            while (true)
            {
                if (worker.CancellationPending)
                {
                    args.Cancel = true;
                    break;
                }
                else
                {
                    for (int i = 0; i < 320; i++)
                    {
                        while (true)
                        {
                            QueryPerformanceCounter(ref Counter2);
                            if ((Counter2 - Counter) * 1000 > (Frequency * 40))//精确定时40ms
                            {
                                Counter = Counter2;
                                break;
                            }
                        }

                        r = SendFrame(1, 0, INSINFO[i], 0, null);
                        if (r == CommResult.Success)
                        {
                            System.Windows.Forms.Application.DoEvents();
                            worker.ReportProgress(i + 1);
                        }
                        else
                        {
                            args.Cancel = true;
                            break;
                        }
                    }
                    break;
                }
            }
        }
        private void ProgressChanged_Handler_SetInsInfo(object sender, ProgressChangedEventArgs args)//线程状态变化响应方法，该方法在UI线程中同步执行
        {
            _mw.PBS6.PS = args.ProgressPercentage;
        }
        private void RunWorkerCompleted_Handler_SetInsInfo(object sender, RunWorkerCompletedEventArgs args)//线程结束方法，该方法在UI线程中同步执行
        {
            if (args.Cancelled)
            {
                _mw.RecordSave(3, "下发井下仪仪器信息取消或中止", true);
            }
            else
            {
                CommResult r;
                byte h = 0;
                byte l = 0;
                r = ReadFrame(ref addr1, ref addr2, ref cmd, ref datalen, ref dat, JXYSetInsInfoTimeout);
                if (r == CommResult.Success)
                {
                    if (CRCCheck(dat) == false)
                    {
                        r = CommResult.DMXCheckError;
                    }
                    else
                    {
                        if (cmd == (JXYSetInsInfoCmd >> 4))
                        {
                            if (datalen == 2)
                            {
                                h = dat[1];
                                l = dat[0];
                                r = CommResult.Success;
                            }
                            else
                            {
                                r = CommResult.DataLenError;
                            }
                        }
                        else
                            r = CommResult.ReturnCmdError;
                    }
                }
                else
                    r = CommResult.USBReceiveTimeout;

                if (r == CommResult.Success)
                {
                    if ((h == 0x00) && (l == 0x00))
                    {
                        _mw.RecordSave(2, "下发井下仪仪器信息成功。", true);
                    }
                    else
                    {
                        _mw.RecordSave(3, string.Format("下发井下仪仪器信息失败！数据已发送完毕，下位机返回帧：0x{0:X4}", h * 256 + l), true);
                    }
                }
                else
                {

                    _mw.RecordSave(3, "下发井下仪仪器信息失败！发送数据完毕，但获取反馈帧时出错。" + _mw.CommError(r), true);
                }
            }

            lock (operationlocker)
            {
                curoperation = CommOperations.None;//状态机复位
            }

            _mw.LockFlag = false;
            _mw.PBS6.PS = 0;//进度条归零
        }


        #endregion
    }
}
