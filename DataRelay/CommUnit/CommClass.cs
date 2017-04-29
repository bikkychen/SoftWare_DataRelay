#define BLE

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommUnit;
using System.Net;



using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Devices.Enumeration;
#if BLE
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Bluetooth;
#endif
namespace NewFilterBoard.CommUnit
{
    using CommUnit;

    public class CommClass
    {


        #region 命令定义及超时
        private const byte GetVersionCmd = 0x00;//固件版本,返回1字节
        private const uint GetVersionTimeout = 200;
        
        private const byte FirmHandshakeCmd = 0x10;//固件更新握手
        private const uint FirmHandshakeTimeout = 200;

        private const byte FirmPageWriteCmd = 0x11;//固件更新页写
        private const uint FirmPageWriteTimeout = 200;

        private const byte FirmBlockEreaseCmd = 0x12;//固件更新块擦除
        private const uint FirmBlockEreaseTimeout = 200;

        private const byte FirmBreakeCmd = 0x13;//固件更新跳转
        private const uint FirmBreakeTimeout = 200;

        //字节0:通讯样式，0:RS232,1:WIFI,2:BLE,3:TouchScreen,4:USB
        //字节1：RS232模式
        //字节2：WIWI模式，0:UDP,1:TCP
        //字节3：BLE模式
        //字节4：TouchScreen模式
        //字节5：USB模式
        private const byte CommStyleCmd = 0x20;//通讯样式及模式，共11字节，前6字节有效，第一字节为通讯样式，后五字节为对应的通讯模式     
        private const uint CommStyleTimeout = 200;

        private const byte GetDataCmd = 0x30;//提取采样数据，返回若干字节

        private const byte WaitStartCmd = 0x40;//击发信号，返回1字节，该字节为0表示没击发，返回1表示已击发，可随后提取采样数据了
        private const uint WaitStartTimeout = 200;

        private const byte StopSampleCmd = 0x60;//停止采样
        private const uint StopSampleTimeout = 200;

        //字节0：单点大小（字节数）
        //字节1-2：采样率，单位sps，无符号短整型数，低字节在前
        //字节3：采样时间，单位秒
        //字节4-5：采样击发门限，无符号短整型数，低字节在前
        //字节6：放大倍数，0-8，0为自适应，1-8为档位数
        //字节7：采样极性，0：单极性，1：双极性
        //字节8：采样量程，1：采样5V，1：采样10V
        //字节9：双串口转发，0：禁止，1：使能
        //字节10：保留
        private const byte InsParaCmd = 0x50;//参数参数，共11字节
        private const uint InsParaTimeout = 200;
        #endregion

        #region 高精度定时所用系统函数和相关变量
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        extern static short QueryPerformanceCounter(ref long x);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        extern static short QueryPerformanceFrequency(ref long x);
        long Frequency = 0;
        long Counter = 0, Counter2 = 0;
        #endregion

        #region 类成员变量
        public delegate void BLELink_delegate(string str);
        public event BLELink_delegate BLELinkNoti;
        public System.IO.Ports.SerialPort RS232_port = new System.IO.Ports.SerialPort();

        public bool BLEConnected = false;

        public DeviceInformationCollection BLEDevices;
#if BLE
        public GattCharacteristic accData;//蓝牙BLE设备的自定义特征，用于数据读写
#endif
        public bool USBConnected = false;

        object operationlocker = new object();
        NewFilterBoard.CommUnit.CommOperations curoperation = NewFilterBoard.CommUnit.CommOperations.None;

        public NewFilterBoard.CommUnit.UDPServerClass myUDPServer;
        public TCPClientClass myTCPClient;
        public NewFilterBoard.CommUnit.TCPServerClass myTCPServer;

        public NewFilterBoard.CommUnit.CommStyleEnum CommStyle = NewFilterBoard.CommUnit.CommStyleEnum.WIFI;
        public NewFilterBoard.CommUnit.WIFIModeEnum WIFIMode = NewFilterBoard.CommUnit.WIFIModeEnum.UDP;
        public NewFilterBoard.CommUnit.FrameStateEnum FrameState = NewFilterBoard.CommUnit.FrameStateEnum.IDEL;
        public NewFilterBoard.CommUnit.FrameStruc RxFrame = new NewFilterBoard.CommUnit.FrameStruc();
#endregion

        public void ComReceive(object sender,System.IO.Ports.SerialDataReceivedEventArgs e)
        {
            System.Threading.Thread.Sleep(10);
            if(RS232_port.IsOpen==false)
            {
                return;
            }
            int n = RS232_port.BytesToRead;
            if(n==0)
            {
                return;
            }

            try
            {
                byte[] b = new byte[n];
                for (int j = 0; j < n; j++)
                {
                    b[j] = (byte)RS232_port.ReadByte();
                }
                StreamDataReceive(b, n);
            }
            catch
            {

            }
        }
        public NewFilterBoard.CommUnit.CommResult SendFrame(NewFilterBoard.CommUnit.FrameStruc ThisTxFrame)//帧发送，addr1:0-地面仪，1-井下仪；addr2：0-主控板，1-采集板
        {
            FrameState = NewFilterBoard.CommUnit.FrameStateEnum.IDEL;

            if ((CommStyle == NewFilterBoard.CommUnit.CommStyleEnum.USB) )//CH374模式下
            {

                 if (USBConnected == false)
                    return CommResult.USBDisconnect;

                //if (_usbHandle.ToInt32() <= 0)
                //    return CommResult.USBOpenDeviceError;

                //if (datalen > 7)//数据字节长度不能大于7，因为USB每帧16字节中有9字节的额外开销，则数据字节长度最大只能有7字节
                //{
                //    return CommResult.USBSendLenthOve;
                //}

                //if ((datalen > 0) && (dat == null))
                //{
                //    return CommResult.SendFrameBufError;
                //}

                //Clear374Buf();//清空缓存多余数据

                //byte[] sdata = new byte[16];
                //sdata[0] = 0x7e;//帧头
                //sdata[1] = 0x7e;//帧头
                //sdata[2] = addr1;//地址1
                //sdata[3] = addr2;//地址2
                //sdata[4] = cmd;//命令
                //sdata[5] = (byte)(datalen);//数据长度低字节
                //sdata[6] = (byte)(datalen >> 8);//数据长度高字节

                //int i;
                //for (i = 0; i < datalen; i++)
                //{
                //    sdata[i + 7] = dat[i];
                //}
                //sdata[i + 7] = 0x55;//校验低字节
                //sdata[i + 8] = 0xaa;//校验高字节

                //for (i = (datalen + 9); i < 16; i++)
                //{
                //    sdata[i] = 0x00;//不足16字节时后面的字节补0
                //}

                //int sCount = 16;//发送16字节固定字节长度的帧
                //if (CH375WriteData(_usbHandle, sdata, ref sCount) && sCount == (16))
                //{
                //    return CommResult.Success;
                //}
                //else
                //    return CommResult.SendFrameFail;

            }
            else if (CommStyle == NewFilterBoard.CommUnit.CommStyleEnum.RS232) //RS232模式下
            {
                if (RS232_port.IsOpen == false)
                {
                    //MessageBox.Show("端口未打开！");
                    return NewFilterBoard.CommUnit.CommResult.RS232NotOpen;
                }

                if ((ThisTxFrame.datalen > 0) && (ThisTxFrame.databuf == null))
                {
                    return NewFilterBoard.CommUnit.CommResult.SendFrameBufError;
                }

                RS232_port.DiscardInBuffer();

                byte[] sdata = new byte[ThisTxFrame.datalen + 9];
                sdata[0] = 0x7e;//帧头
                sdata[1] = 0x7e;//帧头
                sdata[2] = ThisTxFrame.addr1;//地址1
                sdata[3] = ThisTxFrame.addr2;//地址2
                sdata[4] = ThisTxFrame.cmd;//命令
                sdata[5] = (byte)(ThisTxFrame.datalen);//数据长度低字节
                sdata[6] = (byte)(ThisTxFrame.datalen >> 8);//数据长度高字节

                int i;
                for (i = 0; i < ThisTxFrame.datalen; i++)
                {
                    sdata[i + 7] = ThisTxFrame.databuf[i];
                }
                sdata[i + 7] = 0x55;//校验低字节
                sdata[i + 8] = 0xaa;//校验高字节
                                    //  Debug.WriteLine("A:" + System.DateTime.Now.ToString() + ":" + System.DateTime.Now.Millisecond.ToString());
                   
                RS232_port.Write(sdata, 0, sdata.Length);
                FrameState = NewFilterBoard.CommUnit.FrameStateEnum.IDEL;//发送完毕后立即置状态为空闲状态，准备接收反馈帧
                return NewFilterBoard.CommUnit.CommResult.Success;
            }
#if BLE
            else if (CommStyle == CommStyleEnum.Bluetooth) //Bluetooth模式下
            {
                if (accData == null)
                {
                    return CommResult.BluetoothNotOpen;
                }

                if ((ThisTxFrame.datalen > 0) && (ThisTxFrame.databuf == null))
                {
                    return CommResult.SendFrameBufError;
                }

                byte[] sdata = new byte[ThisTxFrame.datalen + 9];
                sdata[0] = 0x7e;//帧头
                sdata[1] = 0x7e;//帧头
                sdata[2] = ThisTxFrame.addr1;//地址1
                sdata[3] = ThisTxFrame.addr2;//地址2
                sdata[4] = ThisTxFrame.cmd;//命令
                sdata[5] = (byte)(ThisTxFrame.datalen);//数据长度低字节
                sdata[6] = (byte)(ThisTxFrame.datalen >> 8);//数据长度高字节

                int i;
                for (i = 0; i < ThisTxFrame.datalen; i++)
                {
                    sdata[i + 7] = ThisTxFrame.databuf[i];
                }
                sdata[i + 7] = 0x55;//校验低字节
                sdata[i + 8] = 0xaa;//校验高字节
                                    //  Debug.WriteLine("A:" + System.DateTime.Now.ToString() + ":" + System.DateTime.Now.Millisecond.ToString());
                                    // _mw.RS232_port.Write(sdata, 0, sdata.Length);

                try
                {
                    WriteBLE(accData, sdata);
                    FrameState = NewFilterBoard.CommUnit.FrameStateEnum.IDEL;//发送完毕后立即置状态为空闲状态，准备接收反馈帧
                    return CommResult.Success;
                }
                catch
                {
                    return CommResult.BluetoothSendFail;
                }
            }
#endif
            else if (CommStyle == NewFilterBoard.CommUnit.CommStyleEnum.WIFI) //WIFI模式下
            {
                if (myUDPServer == null)
                {
                    return NewFilterBoard.CommUnit.CommResult.WIFINotOpen;
                }

                if ((ThisTxFrame.datalen > 0) && (ThisTxFrame.databuf == null))
                {
                    return NewFilterBoard.CommUnit.CommResult.SendFrameBufError;
                }

                //myUDPServer.ReceiveBytes = null;//先清空接收缓存

                byte[] sdata = new byte[ThisTxFrame.datalen + 9];
                sdata[0] = 0x7e;//帧头
                sdata[1] = 0x7e;//帧头
                sdata[2] = ThisTxFrame.addr1;//地址1
                sdata[3] = ThisTxFrame.addr2;//地址2
                sdata[4] = ThisTxFrame.cmd;//命令
                sdata[5] = (byte)(ThisTxFrame.datalen);//数据长度低字节
                sdata[6] = (byte)(ThisTxFrame.datalen >> 8);//数据长度高字节

                int i;
                for (i = 0; i < ThisTxFrame.datalen; i++)
                {
                    sdata[i + 7] = ThisTxFrame.databuf[i];
                }
                sdata[i + 7] = 0x55;//校验低字节
                sdata[i + 8] = 0xaa;//校验高字节
                                    //  Debug.WriteLine("A:" + System.DateTime.Now.ToString() + ":" + System.DateTime.Now.Millisecond.ToString());
                                    // _mw.RS232_port.Write(sdata, 0, sdata.Length);

                try
                {
                    if(WIFIMode == NewFilterBoard.CommUnit.WIFIModeEnum.UDP)
                    {
                        myUDPServer.UDPServerSend(sdata, sdata.Length);
                        FrameState = NewFilterBoard.CommUnit.FrameStateEnum.IDEL;//发送完毕后立即置状态为空闲状态，准备接收反馈帧
                        return NewFilterBoard.CommUnit.CommResult.Success;
                    }
                    else if (WIFIMode == NewFilterBoard.CommUnit.WIFIModeEnum.TCP)
                    {
                        myTCPServer.TCPServerSend(sdata, sdata.Length);
                        FrameState = NewFilterBoard.CommUnit.FrameStateEnum.IDEL;//发送完毕后立即置状态为空闲状态，准备接收反馈帧
                        return NewFilterBoard.CommUnit.CommResult.Success;
                    }              
                }
                catch
                {
                    return NewFilterBoard.CommUnit.CommResult.WIFISendFail;
                }
            }

            return NewFilterBoard.CommUnit.CommResult.FrameNotSend;
        }

        public NewFilterBoard.CommUnit.CommResult ReadFrame(ref NewFilterBoard.CommUnit.FrameStruc ThisRxFrame, uint timeout)//帧接收
        {
            if ((CommStyle == NewFilterBoard.CommUnit.CommStyleEnum.USB) )//USB模式下
            {
                if (USBConnected == false)
                    return CommResult.USBDisconnect;
/*
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
                    return CommResult.USBReceiveTimeout;*/
            }
            else if (CommStyle == NewFilterBoard.CommUnit.CommStyleEnum.RS232) //RS232模式下
            {
                if (RxFrame.databuf == null)
                { return NewFilterBoard.CommUnit.CommResult.ReadFrameBufError; }

                QueryPerformanceFrequency(ref Frequency);
                QueryPerformanceCounter(ref Counter);

                while (true)//超时时间内死循环读串中，如果读到了一帧完整数据，则可提前结束返回
                {
                     
                    QueryPerformanceCounter(ref Counter2);
                    if (FrameState == NewFilterBoard.CommUnit.FrameStateEnum.OK)//收到了个完整帧后跳出
                    {
                        //    Debug.WriteLine("B:" + System.DateTime.Now.ToString() + ":" + System.DateTime.Now.Millisecond.ToString());
                        FrameState = NewFilterBoard.CommUnit.FrameStateEnum.IDEL;//接收到完整帧后立即置状态位为空闲
                        if ((RxFrame.head1 == 0xe7) && (RxFrame.head2 == 0xe7))
                        {
                            ThisRxFrame = RxFrame;
                            return NewFilterBoard.CommUnit.CommResult.Success;
                        }
                        else
                        {
                            return NewFilterBoard.CommUnit.CommResult.ReadFrameHeadError;
                        }
                    }
                    if ((Counter2 - Counter) * 1000 > (Frequency * timeout))//精确定时timeout (ms)，超时后跳出
                    {
                        return NewFilterBoard.CommUnit.CommResult.RS232ReceiveTimeout;
                    }
                }

            }
            else if (CommStyle == CommStyleEnum.Bluetooth) //Bluetooth模式下
            {
                if (RxFrame.databuf == null)
                { return CommResult.ReadFrameBufError; }

                QueryPerformanceFrequency(ref Frequency);
                QueryPerformanceCounter(ref Counter);

                while (true)//超时时间内死循环读串中，如果读到了一帧完整数据，则可提前结束返回
                {
                    //System.Diagnostics.Debug.WriteLine("A:" + System.DateTime.Now.ToString() + ":" + System.DateTime.Now.Millisecond.ToString() );
                   // ReadBLE(accData);
                   // System.Diagnostics.Debug.WriteLine("C:" + System.DateTime.Now.ToString() + ":" + System.DateTime.Now.Millisecond.ToString() );
                   
                    QueryPerformanceCounter(ref Counter2);
                    if (FrameState == NewFilterBoard.CommUnit.FrameStateEnum.OK)//收到了个完整帧后跳出
                    {
                      //  System.Diagnostics.Debug.WriteLine("D:" + System.DateTime.Now.ToString() + ":" + System.DateTime.Now.Millisecond.ToString());
                        FrameState = NewFilterBoard.CommUnit.FrameStateEnum.IDEL;//接收到完整帧后立即置状态位为空闲
                        if ((RxFrame.head1 == 0xe7) && (RxFrame.head2 == 0xe7))
                        {
                            ThisRxFrame = RxFrame;
                            return NewFilterBoard.CommUnit.CommResult.Success;
                        }
                        else
                        {
                            return NewFilterBoard.CommUnit.CommResult.ReadFrameHeadError;
                        }
                    }
                    if ((Counter2 - Counter) * 1000 > (Frequency * timeout))//精确定时timeout (ms)，超时后跳出
                    {
                        return NewFilterBoard.CommUnit.CommResult.BluetoothReceiveTimeout;
                    }
                }
            }
            else if (CommStyle == NewFilterBoard.CommUnit.CommStyleEnum.WIFI) //WIFI模式下
            {
                if (RxFrame.databuf == null)
                { return NewFilterBoard.CommUnit.CommResult.ReadFrameBufError; }

                QueryPerformanceFrequency(ref Frequency);
                QueryPerformanceCounter(ref Counter);

                while (true)//超时时间内死循环读串中，如果读到了一帧完整数据，则可提前结束返回
                {
                    QueryPerformanceCounter(ref Counter2);
                    if (FrameState == NewFilterBoard.CommUnit.FrameStateEnum.OK)//收到了个完整帧后跳出
                    {
                        //    Debug.WriteLine("B:" + System.DateTime.Now.ToString() + ":" + System.DateTime.Now.Millisecond.ToString());
                        FrameState = NewFilterBoard.CommUnit.FrameStateEnum.IDEL;//接收到完整帧后立即置状态位为空闲
                        if ((RxFrame.head1 == 0xe7) && (RxFrame.head2 == 0xe7))
                        {
                            ThisRxFrame = RxFrame;
                            return NewFilterBoard.CommUnit.CommResult.Success;
                        }
                        else
                        {
                            return NewFilterBoard.CommUnit.CommResult.ReadFrameHeadError;
                        }
                    }
                    if ((Counter2 - Counter) * 1000 > (Frequency * timeout))//精确定时timeout (ms)，超时后跳出
                    {
                        return NewFilterBoard.CommUnit.CommResult.WIFIReceiveTimeout;
                    }
                }

            }
            return NewFilterBoard.CommUnit.CommResult.FrameNotReceived;
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

        public bool CRCCheck(FrameStruc dat)
        {
            return true;
        }

        public string CommError(NewFilterBoard.CommUnit.CommResult r)
        {
            string str = "";
            switch (r)
            {
                case NewFilterBoard.CommUnit.CommResult.Success:
                    str = "  (操作成功)";
                    break;
                case NewFilterBoard.CommUnit.CommResult.USBOpenDeviceError:
                    str = "  (打开USB设备失败)";
                    break;
                case NewFilterBoard.CommUnit.CommResult.USBDisconnect:
                    str = "  (地面仪USB未连接)";
                    break;
                case NewFilterBoard.CommUnit.CommResult.USBSetTimeoutError:
                    str = "  (USB设置超时失败)";
                    break;
                case NewFilterBoard.CommUnit.CommResult.USBSendLenthOve:
                    str = "  (USB下发数据长度超界)";
                    break;
                case NewFilterBoard.CommUnit.CommResult.SendFrameBufError:
                    str = "  (下发缓存错误)";
                    break;
                case NewFilterBoard.CommUnit.CommResult.ReadFrameBufLenthError:
                    str = "  (接收缓存长度错误)";
                    break;
                case NewFilterBoard.CommUnit.CommResult.ReadFrameBufError:
                    str = "  (接收缓存错误)";
                    break;
                case NewFilterBoard.CommUnit.CommResult.RS232NotOpen:
                    str = "  (串口端口未打开)";
                    break;
                case NewFilterBoard.CommUnit.CommResult.WIFINotOpen:
                    str = "  (WIFI端口未打开)";
                    break;
                case NewFilterBoard.CommUnit.CommResult.BluetoothNotOpen:
                    str = "  (蓝牙BLE端口未打开)";
                    break;
                case NewFilterBoard.CommUnit.CommResult.BluetoothSendFail:
                    str = "  (蓝牙BLE发送数据失败)";
                    break;
                case NewFilterBoard.CommUnit.CommResult.BluetoothReceiveTimeout:
                    str = "  (蓝牙BLE接收超时)";
                    break;
                case NewFilterBoard.CommUnit.CommResult.RS232ReceiveTimeout:
                    str = "  (串口接收帧超时)";
                    break;
                case NewFilterBoard.CommUnit.CommResult.WIFIReceiveTimeout:
                    str = "  (WIFI接收帧超时)";
                    break;
                case NewFilterBoard.CommUnit.CommResult.OperationConflict:
                    str = "  (操作冲突)";
                    break;
                case NewFilterBoard.CommUnit.CommResult.Exception:
                    str = "  (系统抛出异常)";
                    break;
                case NewFilterBoard.CommUnit.CommResult.SendFrameFail:
                    str = "  (下发帧失败)";
                    break;
                case NewFilterBoard.CommUnit.CommResult.USBReceiveTimeout:
                    str = "  (USB接收帧超时)";
                    break;
                case NewFilterBoard.CommUnit.CommResult.ReturnCmdError:
                    str = "  (返回帧命令码错误)";
                    break;
                case NewFilterBoard.CommUnit.CommResult.ReadFrameHeadError:
                    str = "  (返回帧帧头错误)";
                    break;
                case NewFilterBoard.CommUnit.CommResult.DataLenError:
                    str = "  (返回帧数据长度错误)";
                    break;
                case NewFilterBoard.CommUnit.CommResult.DataCheckError:
                    str = "  (地面帧校验错误)";
                    break;
                case NewFilterBoard.CommUnit.CommResult.JXYCheckCError:
                    str = "  (井下帧校验错误)";
                    break;
                case NewFilterBoard.CommUnit.CommResult.ThreadBusy:
                    str = "  (后台线程正忙)";
                    break;
                case NewFilterBoard.CommUnit.CommResult.ParaError:
                    str = "  (参数错误)";
                    break;
                case NewFilterBoard.CommUnit.CommResult.Processing:
                    str = "  (线程进行中)";
                    break;
                case NewFilterBoard.CommUnit.CommResult.FrameNotSend:
                    str = "  (没有进行发送操作)";
                    break;
                case NewFilterBoard.CommUnit.CommResult.FrameNotReceived:
                    str = "  (没有进行接收操作)";
                    break;
                case NewFilterBoard.CommUnit.CommResult.WIFISendFail:
                    str = "  (WIFI发送帧失败)";
                    break;
                default:
                    str = "  （未知错误）";
                    break;
            }
            return str;
        }

        public void StreamDataInterpretation(byte[] dat)//流数据解释
        {
            byte b;

            for (int i = 0; i < dat.Length; i++)
            {
                b = dat[i];

                switch (FrameState)
                {
                    case NewFilterBoard.CommUnit.FrameStateEnum.IDEL:
                        RxFrame.datalencn = 0;
                        if (b == 0xe7)
                        {
                            FrameState = NewFilterBoard.CommUnit.FrameStateEnum.HEAD1;
                            RxFrame.head1 = b;
                        }
                        else
                            FrameState = NewFilterBoard.CommUnit.FrameStateEnum.IDEL;
                        break;
                    case NewFilterBoard.CommUnit.FrameStateEnum.HEAD1:
                        if (b == 0xe7)
                        {
                            FrameState = NewFilterBoard.CommUnit.FrameStateEnum.HEAD2;
                            RxFrame.head2 = b;
                        }
                        else
                            FrameState = NewFilterBoard.CommUnit.FrameStateEnum.IDEL;
                        break;
                    case NewFilterBoard.CommUnit.FrameStateEnum.HEAD2:
                        FrameState = NewFilterBoard.CommUnit.FrameStateEnum.ADDR1;
                        RxFrame.addr1 = b;
                        break;
                    case NewFilterBoard.CommUnit.FrameStateEnum.ADDR1:
                        FrameState = NewFilterBoard.CommUnit.FrameStateEnum.ADDR2;
                        RxFrame.addr2 = b;
                        break;
                    case NewFilterBoard.CommUnit.FrameStateEnum.ADDR2:
                        FrameState = NewFilterBoard.CommUnit.FrameStateEnum.COMM;
                        RxFrame.cmd = b;
                        break;
                    case NewFilterBoard.CommUnit.FrameStateEnum.COMM:
                        FrameState = NewFilterBoard.CommUnit.FrameStateEnum.DATALEN1;
                        RxFrame.datalen = b;
                        break;
                    case NewFilterBoard.CommUnit.FrameStateEnum.DATALEN1:
                        RxFrame.datalen += (UInt16)(b * 256);
                        RxFrame.datalentem = RxFrame.datalen;
                        if (RxFrame.datalen == 0)
                            FrameState = NewFilterBoard.CommUnit.FrameStateEnum.CHECK1;
                        else
                            FrameState = NewFilterBoard.CommUnit.FrameStateEnum.DATABUF;
                        break;
                    case NewFilterBoard.CommUnit.FrameStateEnum.DATABUF:
                        RxFrame.databuf[RxFrame.datalencn] = b;
                        RxFrame.datalencn++;
                        RxFrame.datalentem--;
                        if (RxFrame.datalentem == 0)
                        {
                            FrameState = NewFilterBoard.CommUnit.FrameStateEnum.CHECK1;
                        }
                        break;
                    case NewFilterBoard.CommUnit.FrameStateEnum.CHECK1:
                        FrameState = NewFilterBoard.CommUnit.FrameStateEnum.CHECK2;
                        RxFrame.check1 = b;
                        break;
                    case NewFilterBoard.CommUnit.FrameStateEnum.CHECK2:
                        FrameState = NewFilterBoard.CommUnit.FrameStateEnum.OK;
                        RxFrame.check2 = b;
                        break;
                }
            }
        }

        public void StreamDataReceive(byte[] dat ,int len)//流数据解释分离程序
        {

            byte b = 0;
            for (int i = 0; i < len; i++)
            {
                b = dat[i];
                switch (FrameState)
                {
                    case NewFilterBoard.CommUnit.FrameStateEnum.IDEL:
                        RxFrame.datalencn = 0;
                        if (b == 0xe7)
                        {
                            FrameState = NewFilterBoard.CommUnit.FrameStateEnum.HEAD1;
                            RxFrame.head1 = b;
                        }
                        else
                            FrameState = NewFilterBoard.CommUnit.FrameStateEnum.IDEL;
                        break;
                    case NewFilterBoard.CommUnit.FrameStateEnum.HEAD1:
                        if (b == 0xe7)
                        {
                            FrameState = NewFilterBoard.CommUnit.FrameStateEnum.HEAD2;
                            RxFrame.head2 = b;
                        }
                        else
                            FrameState = NewFilterBoard.CommUnit.FrameStateEnum.IDEL;
                        break;
                    case NewFilterBoard.CommUnit.FrameStateEnum.HEAD2:
                        FrameState = NewFilterBoard.CommUnit.FrameStateEnum.ADDR1;
                        RxFrame.addr1 = b;
                        break;
                    case NewFilterBoard.CommUnit.FrameStateEnum.ADDR1:
                        FrameState = NewFilterBoard.CommUnit.FrameStateEnum.ADDR2;
                        RxFrame.addr2 = b;
                        break;
                    case NewFilterBoard.CommUnit.FrameStateEnum.ADDR2:
                        FrameState = NewFilterBoard.CommUnit.FrameStateEnum.COMM;
                        RxFrame.cmd = b;
                        break;
                    case NewFilterBoard.CommUnit.FrameStateEnum.COMM:
                        FrameState = NewFilterBoard.CommUnit.FrameStateEnum.DATALEN1;
                        RxFrame.datalen = b;
                        break;
                    case NewFilterBoard.CommUnit.FrameStateEnum.DATALEN1:
                        RxFrame.datalen += (UInt16)(b * 256);
                        RxFrame.datalentem = RxFrame.datalen;
                        if (RxFrame.datalen == 0)
                            FrameState = NewFilterBoard.CommUnit.FrameStateEnum.CHECK1;
                        else
                            FrameState = NewFilterBoard.CommUnit.FrameStateEnum.DATABUF;
                        break;
                    case NewFilterBoard.CommUnit.FrameStateEnum.DATABUF:
                        RxFrame.databuf[RxFrame.datalencn] = b;
                        RxFrame.datalencn++;
                        RxFrame.datalentem--;
                        if (RxFrame.datalentem == 0)
                        {
                            FrameState = NewFilterBoard.CommUnit.FrameStateEnum.CHECK1;
                        }
                        break;
                    case NewFilterBoard.CommUnit.FrameStateEnum.CHECK1:
                        FrameState = NewFilterBoard.CommUnit.FrameStateEnum.CHECK2;
                        RxFrame.check1 = b;
                        break;
                    case NewFilterBoard.CommUnit.FrameStateEnum.CHECK2:
                        if(CRCCheck(RxFrame)==true)
                        {
                            FrameState = NewFilterBoard.CommUnit.FrameStateEnum.OK;
                            RxFrame.check2 = b;
                        }
                        else
                        {
                            FrameState = NewFilterBoard.CommUnit.FrameStateEnum.IDEL;
                        }                     
                        break;
                }
            }
        }

#region WIFI 
        public void UDPServer(int port)//启动UDP服务器
        {
            myUDPServer = new NewFilterBoard.CommUnit.UDPServerClass(port);//启动UDP服务器
            myUDPServer.MessageArrived += new NewFilterBoard.CommUnit.UDPServerClass.MessageHandler(UDPServer_MessageArrived);//注册UDP服务器接收数据响应事件
            myUDPServer.Thread_Listen();//开启一个独立线程监听UDP服务器接收到的数据   
        }

        public void TCPServer(int port)//启动TCP服务器
        {
            myTCPServer = new NewFilterBoard.CommUnit.TCPServerClass(port);//启动TCP服务器
            myTCPServer.MessageArrived += new NewFilterBoard.CommUnit.TCPServerClass.MessageHandler(TCPServer_MessageArrived);//注册TCP服务器接收数据响应事件
            myTCPServer.ListenBegin();//开启一个独立线程监听UDP服务器接收到的数据   
        }
        void UDPServer_MessageArrived(byte[] receiveBytes, int receiveLen)//UDP服务器的接收中断服务程序 ，即注册了一个事件到UDPServerClass类中，这个类收到数据后会触发下面这个事件
        {
            StreamDataReceive(receiveBytes, receiveLen);//收到数据后调用流数据处理程序进行解释分离
                                //该事件在非主线程中运行，则在屏幕上打印东西要用委托来异步执行
                                //string receiveMessage = Encoding.Default.GetString(receiveBytes, 0, receiveBytes.Length);
                                // UDP_ReceiveData_Print(receiveMessage);//这样做行不通，要用下面的做法
                                // this.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Normal, new DelegateChangeText(UDP_ReceiveData_Print), receiveMessage);
                                //以下是匿名委托，效果与上面的一样
                                //this.Dispatcher.Invoke(new Predicate<object>(delegate (object myText)
                                //{
                                //    myText = text;
                                //    return true; //返回值
                                //}), text);
                                //}
        }

        void TCPServer_MessageArrived(byte[] receiveBytes, int receiveLen)//UDP服务器的接收中断服务程序
        {
            StreamDataReceive(receiveBytes, receiveLen);
        }
        #endregion

#region BLE

        public async void FundBLE()//查找蓝牙设备
        {
#if BLE
            //加入
            //Find the devices that expose the service
            try
            {
                //根据蓝牙协议获取services
                //Generic Access:00001800
                //Generic Attribute:00001801
                //Device Infomation:0000180a
                //Unknown Service:0000ffe0
                BLEDevices = await DeviceInformation.FindAllAsync(GattDeviceService.GetDeviceSelectorFromUuid(GattServiceUuids.GenericAccess));//此行与下面行的意义完全等同                                                                                                                                           // DeviceInformationCollection devices = await DeviceInformation.FindAllAsync(GattDeviceService.GetDeviceSelectorFromUuid(GattCharacteristic.ConvertShortIdToUuid(0x1800)));             
            }
            catch
            {

            }
#endif
        }

        public async void OpenBLE(int index)
        {
            BLEConnected = false;
#if BLE
            accData = null;

            try
            {
                if (BLEDevices.Count < (index + 1))
                {
                    //RecordSave(2, "没有发现蓝牙BLE设备!", false);                  
                    return;
                }

                //Connect to the service
                string str = BLEDevices[index].Id;//Id="\\\\?\\BTHLEDevice#{00001800-0000-1000-8000-00805f9b34fb}_Dev_VID&01000d_PID&0000_REV&0110_00158300b5fb#8&3423ba61&9&0001#{6e3bb679-4372-40c8-9eaa-4509df260cd8}"	
                GattDeviceService service = await GattDeviceService.FromIdAsync(str);
                if (service == null)
                {
                    //RecordSave(2, "蓝牙BLE设备没有提供通用服务!", false);
                    return;
                }

                //Obtain the characteristic we want to interact with
                var characteristic = service.GetCharacteristics(GattCharacteristic.ConvertShortIdToUuid(0x2A00))[0];//2A00
                GattReadResult x = await characteristic.ReadValueAsync(BluetoothCacheMode.Uncached);
                if (x == null)
                {
                    //RecordSave(2, "无法获取蓝牙BLE设备的设备名!", false);
                    return;
                }
                var deviceNameBytes = (x).Value.ToArray();
                if (deviceNameBytes == null)
                {
                    //RecordSave(2, "蓝牙BLE设备的设备名为空!", false);
                    return;
                }

                var deviceName = Encoding.UTF8.GetString(deviceNameBytes, 0, deviceNameBytes.Length);
                //RecordSave(2, "已发现蓝牙BLE设备，设备名:" + deviceName, false);

                //////////////以下为查找蓝牙BLE设备的读数据服务和特征/////////////////////////////
                //RecordSave(2, "蓝牙BLE设备自定义服务和特征查找中...", false);
                var devices2 = await DeviceInformation.FindAllAsync(GattDeviceService.GetDeviceSelectorFromUuid(GattCharacteristic.ConvertShortIdToUuid((ushort)0xffe0)));
                if (devices2.Count == 0)
                {
                    //RecordSave(2, "未发现蓝牙BLE设备自定义服务！", false);
                    return;
                }

                int ii = -1;
                for (int i = 0; i < devices2.Count; i++)
                {
                    if (devices2[i].Name == BLEDevices[index].Name)
                    {
                        ii = i;
                        i = devices2.Count;
                    }
                }

                if (ii < 0)
                {
                    return;
                }

                //Connect to the service
                GattDeviceService accService = await GattDeviceService.FromIdAsync(devices2[ii].Id);
                if (accService == null)
                {
                    //RecordSave(2, "蓝牙BLE设备自定义服务为空！", false);
                    return;
                }
                else
                {
                    //RecordSave(2, "已找到蓝牙BLE设备自定义服务！", false);
                }
                accData = accService.GetCharacteristics(GattCharacteristic.ConvertShortIdToUuid((ushort)0xffe1))[0];
                if (accData == null)
                {
                    //RecordSave(2, "蓝牙BLE设备自定义特征为空！", false);
                    return;
                }
                else
                {
                    //RecordSave(2, "已找到蓝牙BLE设备自定义特征！", false);
                }

                accData.ValueChanged += accData_ValueChanged;//挂接事件
                GattCommunicationStatus xx = await accData.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify);//这行必须要，否则不都能实时监测到接收到的蓝牙BLE数据

                //RecordSave(2, "蓝牙BLE设备连接成功！", false);
                BLEConnected = true;
                if (BLELinkNoti != null)
                {
                    BLELinkNoti(BLEDevices[index].Name);//通知主界面蓝牙已连接
                }
            }
            catch
            {

            }
#endif
        }
#if BLE
        public async void ReadBLE(GattCharacteristic accData)//从蓝牙BLE设备读数据
        {
            //1、读完后该子程序还能进来，不知何故。
            //2、有时还能把发送的字节也读出来
            //    3、不知如何清空已读过的数据
            try
            {
                if (accData != null)
                {
                    GattReadResult x = (await accData.ReadValueAsync());

                    // var deviceNameBytes = x.Value.ToArray();
                    if (x.Status == GattCommunicationStatus.Success)
                    {
                        byte[] values = x.Value.ToArray();
                        if (values != null)
                        {
                            StreamDataReceive(values, values.Length);
                            System.Diagnostics.Debug.WriteLine("B:" + System.DateTime.Now.ToString() + ":" + System.DateTime.Now.Millisecond.ToString() + "  len=" + values.Length.ToString());
                        }
                    }
                    //string str;
                    // str = BitConverter.ToString(deviceNameBytes);//转换后字节用十六进制表示，字节之间用"-"自动隔开
                    // str = BitConverter.ToString(deviceNameBytes).Replace("-", "");
                    //RecordSave(2, "蓝牙BLE设备读取的结果:" + str, false);
                }
                else
                {
                    //RecordSave(2, "蓝牙BLE设备自定义特征为空，无法进行读数据操作!", false);
                }
            }
            catch (Exception ex)
            {
                //RecordSave(2, "蓝牙BLE设备读数据异常!", false);
            }
        }

        public async void WriteBLE(GattCharacteristic accData, byte[] xxt)//写数据到蓝牙BLE设备
        {
            try
            {
                if (accData != null)
                {
                    //GattCommunicationStatus x = await accData.WriteValueAsync(xxt.AsBuffer(), GattWriteOption.WriteWithResponse);
                    GattCommunicationStatus x = await accData.WriteValueAsync(xxt.AsBuffer(), GattWriteOption.WriteWithResponse);
                    if (x.ToString() == "Success")
                    {
                        //RecordSave(2, "蓝牙BLE写数据成功。", false);
                    }

                }
                else
                {
                    //RecordSave(2, "蓝牙BLE设备自定义特征为空，无法进行写数据操作!", false);
                }

            }
            catch (Exception ex)
            {
                //RecordSave(2, "蓝牙BLE设备写数据异常!", false);
            }
        }

        private async void accData_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)//实时监测蓝牙BLE设备的数据通道
        {
            try
            {
                //该行经常抛出异常“提供给请求操作的用户缓冲区无效”，原因暂不知
                GattReadResult x = await sender.ReadValueAsync();//BluetoothCacheMode.Uncached
                if (x.Status == GattCommunicationStatus.Success)
                {
                    byte[] values = x.Value.ToArray();
                    if (values != null)
                    {
                        StreamDataReceive(values, values.Length);
                        // System.Diagnostics.Debug.WriteLine("B:" + System.DateTime.Now.ToString() + ":" + System.DateTime.Now.Millisecond.ToString() + "  len=" + values.Length.ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("B:" + System.DateTime.Now.ToString() + ":" + System.DateTime.Now.Millisecond.ToString() + "  ex=" + ex.ToString());
            }
        }
#endif
#endregion

        public NewFilterBoard.CommUnit.CommResult GetVersion(ref byte ver)//获取固件版本
        {
            lock (operationlocker)
            {
                if (curoperation == NewFilterBoard.CommUnit.CommOperations.None)
                    curoperation = NewFilterBoard.CommUnit.CommOperations.GetVersion;
                else
                    return NewFilterBoard.CommUnit.CommResult.OperationConflict;
            }

            NewFilterBoard.CommUnit.CommResult r;
            try
            {
                NewFilterBoard.CommUnit.FrameStruc ThisTxFrame = new NewFilterBoard.CommUnit.FrameStruc() { addr1 = 0, addr2 = 0, cmd = GetVersionCmd, datalen = 0, databuf = null };
                r = SendFrame(ThisTxFrame);
                if (r == NewFilterBoard.CommUnit.CommResult.Success)
                {
                    NewFilterBoard.CommUnit.FrameStruc ThisRxFrame=null;
                    r = ReadFrame(ref ThisRxFrame, GetVersionTimeout);
                    if (r == NewFilterBoard.CommUnit.CommResult.Success)
                    {
                        if (ThisRxFrame.cmd == GetVersionCmd)
                        {
                            if (ThisRxFrame.datalen == 1)
                            {
                                ver = ThisRxFrame.databuf[0];
                                r = NewFilterBoard.CommUnit.CommResult.Success;
                            }
                            else
                            {
                                r = NewFilterBoard.CommUnit.CommResult.DataLenError;
                            }
                        }
                        else
                            r = NewFilterBoard.CommUnit.CommResult.ReturnCmdError;
                    }
                }
            }
            catch(Exception ex)
            {
                lock (operationlocker)
                {
                    curoperation = NewFilterBoard.CommUnit.CommOperations.None;
                }
                return NewFilterBoard.CommUnit.CommResult.Exception;
            }

            lock (operationlocker)
            {
                curoperation = NewFilterBoard.CommUnit.CommOperations.None;
            }

            return r;
        }

        public NewFilterBoard.CommUnit.CommResult GetData(int dotindex,int dotlen,int dotsize,ref byte[] dat,uint timeout)//提取采样数据
        {
            lock (operationlocker)
            {
                if (curoperation == NewFilterBoard.CommUnit.CommOperations.None)
                    curoperation = NewFilterBoard.CommUnit.CommOperations.GetData;
                else
                    return NewFilterBoard.CommUnit.CommResult.OperationConflict;
            }

            NewFilterBoard.CommUnit.CommResult r;
            try
            {
                NewFilterBoard.CommUnit.FrameStruc ThisTxFrame = new NewFilterBoard.CommUnit.FrameStruc() { addr1 = 0, addr2 = 0, cmd = GetDataCmd, datalen = 6 };
                ThisTxFrame.databuf[0] = (byte)dotindex;
                ThisTxFrame.databuf[1] = (byte)(dotindex>>8);
                ThisTxFrame.databuf[2] = (byte)(dotindex >> 16);
                ThisTxFrame.databuf[3] = (byte)(dotindex >> 24);
                ThisTxFrame.databuf[4] = (byte)dotlen;
                ThisTxFrame.databuf[5] = (byte)(dotlen >> 8);
                r = SendFrame(ThisTxFrame);
                if (r == NewFilterBoard.CommUnit.CommResult.Success)
                {
                    NewFilterBoard.CommUnit.FrameStruc ThisRxFrame = null;
                    r = ReadFrame(ref ThisRxFrame, timeout);
                    if (r == NewFilterBoard.CommUnit.CommResult.Success)
                    {
                        if (ThisRxFrame.cmd == GetDataCmd)
                        {
                            if (ThisRxFrame.datalen == dotlen* dotsize)
                            {
                                Array.Copy(ThisRxFrame.databuf, dat, ThisRxFrame.datalen);
                                r = NewFilterBoard.CommUnit.CommResult.Success;
                            }
                            else
                            {
                                r = NewFilterBoard.CommUnit.CommResult.DataLenError;
                            }
                        }
                        else
                            r = NewFilterBoard.CommUnit.CommResult.ReturnCmdError;                     
                    }
                }
            }
            catch
            {
                lock (operationlocker)
                {
                    curoperation = NewFilterBoard.CommUnit.CommOperations.None;
                }
                return NewFilterBoard.CommUnit.CommResult.Exception;
            }

            lock (operationlocker)
            {
                curoperation = NewFilterBoard.CommUnit.CommOperations.None;
            }

            return r;
        }

        public NewFilterBoard.CommUnit.CommResult CommStyleMode(byte[] txdat )//设置通讯样式/模式
        {
            lock (operationlocker)
            {
                if (curoperation == NewFilterBoard.CommUnit.CommOperations.None)
                    curoperation = NewFilterBoard.CommUnit.CommOperations.CommStyle;
                else
                    return NewFilterBoard.CommUnit.CommResult.OperationConflict;
            }

            NewFilterBoard.CommUnit.CommResult r;
            try
            {
                NewFilterBoard.CommUnit.FrameStruc ThisTxFrame = new NewFilterBoard.CommUnit.FrameStruc() { addr1 = 0, addr2 = 0, cmd = CommStyleCmd, datalen = 11 };
                for (int i = 0; i < 11; i++)
                {
                    ThisTxFrame.databuf[i] = txdat[i];
                }
                r = SendFrame(ThisTxFrame);
                if (r == NewFilterBoard.CommUnit.CommResult.Success)
                {
                    NewFilterBoard.CommUnit.FrameStruc ThisRxFrame = null;
                    r = ReadFrame(ref ThisRxFrame, CommStyleTimeout);
                    if (r == NewFilterBoard.CommUnit.CommResult.Success)
                    {
                        if (ThisRxFrame.cmd == CommStyleCmd)
                        {
                            if (ThisRxFrame.datalen == 0)
                            {
                                r = NewFilterBoard.CommUnit.CommResult.Success;
                            }
                            else
                            {
                                r = NewFilterBoard.CommUnit.CommResult.DataLenError;
                            }
                        }
                        else
                            r = NewFilterBoard.CommUnit.CommResult.ReturnCmdError;                     
                    }
                }
            }
            catch
            {
                lock (operationlocker)
                {
                    curoperation = NewFilterBoard.CommUnit.CommOperations.None;
                }
                return NewFilterBoard.CommUnit.CommResult.Exception;
            }

            lock (operationlocker)
            {
                curoperation = NewFilterBoard.CommUnit.CommOperations.None;
            }

            return r;
        }

        public NewFilterBoard.CommUnit.CommResult CommStyleMode( ref byte[] rxdat)//查看通讯样式/模式
        {
            lock (operationlocker)
            {
                if (curoperation == NewFilterBoard.CommUnit.CommOperations.None)
                    curoperation = NewFilterBoard.CommUnit.CommOperations.CommStyle;
                else
                    return NewFilterBoard.CommUnit.CommResult.OperationConflict;
            }

            NewFilterBoard.CommUnit.CommResult r;
            try
            {
                NewFilterBoard.CommUnit.FrameStruc ThisTxFrame = new NewFilterBoard.CommUnit.FrameStruc() { addr1 = 0, addr2 = 0, cmd = CommStyleCmd, datalen = 0 };

                r = SendFrame(ThisTxFrame);
                if (r == NewFilterBoard.CommUnit.CommResult.Success)
                {
                    NewFilterBoard.CommUnit.FrameStruc ThisRxFrame = null;
                    r = ReadFrame(ref ThisRxFrame, CommStyleTimeout);
                    if (r == NewFilterBoard.CommUnit.CommResult.Success)
                    {
                        if (ThisRxFrame.cmd == CommStyleCmd)
                        {
                            if (ThisRxFrame.datalen == 11)
                            {
                                for(int i=0;i<11;i++)
                                {
                                    rxdat[i] = ThisRxFrame.databuf[i];
                                }
                                r = NewFilterBoard.CommUnit.CommResult.Success;
                            }
                            else
                            {
                                r = NewFilterBoard.CommUnit.CommResult.DataLenError;
                            }
                        }
                        else
                            r = NewFilterBoard.CommUnit.CommResult.ReturnCmdError;
                    }
                }
            }
            catch
            {
                lock (operationlocker)
                {
                    curoperation = NewFilterBoard.CommUnit.CommOperations.None;
                }
                return NewFilterBoard.CommUnit.CommResult.Exception;
            }

            lock (operationlocker)
            {
                curoperation = NewFilterBoard.CommUnit.CommOperations.None;
            }

            return r;
        }

        public NewFilterBoard.CommUnit.CommResult WaitStart(ref byte d)//查询击发信号
        {
            lock (operationlocker)
            {
                if (curoperation == NewFilterBoard.CommUnit.CommOperations.None)
                    curoperation = NewFilterBoard.CommUnit.CommOperations.WaitStart;
                else
                    return NewFilterBoard.CommUnit.CommResult.OperationConflict;
            }

            NewFilterBoard.CommUnit.CommResult r;
            try
            {
                NewFilterBoard.CommUnit.FrameStruc ThisTxFrame = new NewFilterBoard.CommUnit.FrameStruc() { addr1 = 0, addr2 = 0, cmd = WaitStartCmd, datalen = 0, databuf = null };
                r = SendFrame(ThisTxFrame);
                if (r == NewFilterBoard.CommUnit.CommResult.Success)
                {
                    NewFilterBoard.CommUnit.FrameStruc ThisRxFrame = null;
                    r = ReadFrame(ref ThisRxFrame, WaitStartTimeout);
                    if (r == NewFilterBoard.CommUnit.CommResult.Success)
                    {
                        if (ThisRxFrame.cmd == WaitStartCmd)
                        {
                            if (ThisRxFrame.datalen == 1)
                            {
                                d = ThisRxFrame.databuf[0];
                                r = NewFilterBoard.CommUnit.CommResult.Success;
                            }
                            else
                            {
                                r = NewFilterBoard.CommUnit.CommResult.DataLenError;
                            }
                        }
                        else
                            r = NewFilterBoard.CommUnit.CommResult.ReturnCmdError;
                    }
                }
            }
            catch
            {
                lock (operationlocker)
                {
                    curoperation = NewFilterBoard.CommUnit.CommOperations.None;
                }
                return NewFilterBoard.CommUnit.CommResult.Exception;
            }

            lock (operationlocker)
            {
                curoperation = NewFilterBoard.CommUnit.CommOperations.None;
            }

            return r;
        }

        public NewFilterBoard.CommUnit.CommResult StopSample()//停止采样
        {
            lock (operationlocker)
            {
                if (curoperation == NewFilterBoard.CommUnit.CommOperations.None)
                    curoperation = NewFilterBoard.CommUnit.CommOperations.StopSample;
                else
                    return NewFilterBoard.CommUnit.CommResult.OperationConflict;
            }

            NewFilterBoard.CommUnit.CommResult r;
            try
            {
                NewFilterBoard.CommUnit.FrameStruc ThisTxFrame = new NewFilterBoard.CommUnit.FrameStruc() { addr1 = 0, addr2 = 0, cmd = StopSampleCmd, datalen = 0, databuf = null };
                r = SendFrame(ThisTxFrame);
                if (r == NewFilterBoard.CommUnit.CommResult.Success)
                {
                    NewFilterBoard.CommUnit.FrameStruc ThisRxFrame = null;
                    r = ReadFrame(ref ThisRxFrame, StopSampleTimeout);
                    if (r == NewFilterBoard.CommUnit.CommResult.Success)
                    {
                        if (ThisRxFrame.cmd == StopSampleCmd)
                        {
                            if (ThisRxFrame.datalen == 0)
                            {
                                r = NewFilterBoard.CommUnit.CommResult.Success;
                            }
                            else
                            {
                                r = NewFilterBoard.CommUnit.CommResult.DataLenError;
                            }
                        }
                        else
                            r = NewFilterBoard.CommUnit.CommResult.ReturnCmdError;
                    }
                }
            }
            catch
            {
                lock (operationlocker)
                {
                    curoperation = NewFilterBoard.CommUnit.CommOperations.None;
                }
                return NewFilterBoard.CommUnit.CommResult.Exception;
            }

            lock (operationlocker)
            {
                curoperation = NewFilterBoard.CommUnit.CommOperations.None;
            }

            return r;
        }

        public NewFilterBoard.CommUnit.CommResult InsPara(ref byte[] rxdat)//查看仪器参数
        {
            lock (operationlocker)
            {
                if (curoperation == NewFilterBoard.CommUnit.CommOperations.None)
                    curoperation = NewFilterBoard.CommUnit.CommOperations.SamplePara;
                else
                    return NewFilterBoard.CommUnit.CommResult.OperationConflict;
            }

            NewFilterBoard.CommUnit.CommResult r;
            try
            {
                NewFilterBoard.CommUnit.FrameStruc ThisTxFrame = new NewFilterBoard.CommUnit.FrameStruc() { addr1 = 0, addr2 = 0, cmd = InsParaCmd, datalen = 0, databuf = null };
                r = SendFrame(ThisTxFrame);
                if (r == NewFilterBoard.CommUnit.CommResult.Success)
                {
                    NewFilterBoard.CommUnit.FrameStruc ThisRxFrame = null;
                    r = ReadFrame(ref ThisRxFrame, InsParaTimeout);
                    if (r == NewFilterBoard.CommUnit.CommResult.Success)
                    {
                        if (ThisRxFrame.cmd == InsParaCmd)
                        {
                            if (ThisRxFrame.datalen == 41)
                            {
                                for (int i = 0; i < 41; i++)
                                {
                                    rxdat[i] = ThisRxFrame.databuf[i];
                                }
                                r = NewFilterBoard.CommUnit.CommResult.Success;
                            }
                            else
                            {
                                r = NewFilterBoard.CommUnit.CommResult.DataLenError;
                            }
                        }
                        else
                            r = NewFilterBoard.CommUnit.CommResult.ReturnCmdError;
                    }
                }
            }
            catch
            {
                lock (operationlocker)
                {
                    curoperation = NewFilterBoard.CommUnit.CommOperations.None;
                }
                return NewFilterBoard.CommUnit.CommResult.Exception;
            }

            lock (operationlocker)
            {
                curoperation = NewFilterBoard.CommUnit.CommOperations.None;
            }

            return r;
        }

        public NewFilterBoard.CommUnit.CommResult InsPara(byte[] txdat)//设置仪器参数
        {
            lock (operationlocker)
            {
                if (curoperation == NewFilterBoard.CommUnit.CommOperations.None)
                    curoperation = NewFilterBoard.CommUnit.CommOperations.SamplePara;
                else
                    return NewFilterBoard.CommUnit.CommResult.OperationConflict;
            }

            NewFilterBoard.CommUnit.CommResult r;
            try
            {
                NewFilterBoard.CommUnit.FrameStruc ThisTxFrame = new NewFilterBoard.CommUnit.FrameStruc() { addr1 = 0, addr2 = 0, cmd = InsParaCmd, datalen = 41 };
                for (int i = 0; i < 41; i++)
                {
                    ThisTxFrame.databuf[i] = txdat[i];
                }
                r = SendFrame(ThisTxFrame);
                if (r == NewFilterBoard.CommUnit.CommResult.Success)
                {
                    NewFilterBoard.CommUnit.FrameStruc ThisRxFrame = null;
                    r = ReadFrame(ref ThisRxFrame, InsParaTimeout);
                    if (r == NewFilterBoard.CommUnit.CommResult.Success)
                    {
                        if (ThisRxFrame.cmd == InsParaCmd)
                        {
                            if (ThisRxFrame.datalen == 0)
                            {
                                r = NewFilterBoard.CommUnit.CommResult.Success;
                            }
                            else
                            {
                                r = NewFilterBoard.CommUnit.CommResult.DataLenError;
                            }
                        }
                        else
                            r = NewFilterBoard.CommUnit.CommResult.ReturnCmdError;
                    }
                }
            }
            catch
            {
                lock (operationlocker)
                {
                    curoperation = NewFilterBoard.CommUnit.CommOperations.None;
                }
                return NewFilterBoard.CommUnit.CommResult.Exception;
            }

            lock (operationlocker)
            {
                curoperation = NewFilterBoard.CommUnit.CommOperations.None;
            }

            return r;
        }

#region 固件更新
        public NewFilterBoard.CommUnit.CommResult FirmHandshake( )//固件更新握手
        {
            lock (operationlocker)
            {
                if (curoperation == NewFilterBoard.CommUnit.CommOperations.None)
                    curoperation = NewFilterBoard.CommUnit.CommOperations.FirmHandshake;
                else
                    return NewFilterBoard.CommUnit.CommResult.OperationConflict;
            }

            NewFilterBoard.CommUnit.CommResult r;
            try
            {
                NewFilterBoard.CommUnit.FrameStruc ThisTxFrame = new NewFilterBoard.CommUnit.FrameStruc() { addr1 = 0, addr2 = 0, cmd = FirmHandshakeCmd, datalen = 0, databuf = null };
                r = SendFrame(ThisTxFrame);
                if (r == NewFilterBoard.CommUnit.CommResult.Success)
                {
                    NewFilterBoard.CommUnit.FrameStruc ThisRxFrame = null;
                    r = ReadFrame(ref ThisRxFrame, FirmHandshakeTimeout);
                    if (r == NewFilterBoard.CommUnit.CommResult.Success)
                    {
                        if (ThisRxFrame.cmd == FirmHandshakeCmd)
                        {
                            if (ThisRxFrame.datalen == 0)
                            {
                                r = NewFilterBoard.CommUnit.CommResult.Success;
                            }
                            else
                            {
                                r = NewFilterBoard.CommUnit.CommResult.DataLenError;
                            }
                        }
                        else
                            r = NewFilterBoard.CommUnit.CommResult.ReturnCmdError;
                    }
                }
            }
            catch
            {
                lock (operationlocker)
                {
                    curoperation = NewFilterBoard.CommUnit.CommOperations.None;
                }
                return NewFilterBoard.CommUnit.CommResult.Exception;
            }

            lock (operationlocker)
            {
                curoperation = NewFilterBoard.CommUnit.CommOperations.None;
            }

            return r;
        }

        public NewFilterBoard.CommUnit.CommResult FirmBreake()//固件更新跳转
        {
            lock (operationlocker)
            {
                if (curoperation == NewFilterBoard.CommUnit.CommOperations.None)
                    curoperation = NewFilterBoard.CommUnit.CommOperations.FirmBreake;
                else
                    return NewFilterBoard.CommUnit.CommResult.OperationConflict;
            }

            NewFilterBoard.CommUnit.CommResult r;
            try
            {
                NewFilterBoard.CommUnit.FrameStruc ThisTxFrame = new NewFilterBoard.CommUnit.FrameStruc() { addr1 = 0, addr2 = 0, cmd = FirmBreakeCmd, datalen = 0, databuf = null };
                r = SendFrame(ThisTxFrame);
                if (r == NewFilterBoard.CommUnit.CommResult.Success)
                {
                    NewFilterBoard.CommUnit.FrameStruc ThisRxFrame = null;
                    r = ReadFrame(ref ThisRxFrame, FirmBreakeTimeout);
                    if (r == NewFilterBoard.CommUnit.CommResult.Success)
                    {
                        if (ThisRxFrame.cmd == FirmBreakeCmd)
                        {
                            if (ThisRxFrame.datalen == 0)
                            {
                                r = NewFilterBoard.CommUnit.CommResult.Success;
                            }
                            else
                            {
                                r = NewFilterBoard.CommUnit.CommResult.DataLenError;
                            }
                        }
                        else
                            r = NewFilterBoard.CommUnit.CommResult.ReturnCmdError;
                    }
                }
            }
            catch
            {
                lock (operationlocker)
                {
                    curoperation = NewFilterBoard.CommUnit.CommOperations.None;
                }
                return NewFilterBoard.CommUnit.CommResult.Exception;
            }

            lock (operationlocker)
            {
                curoperation = NewFilterBoard.CommUnit.CommOperations.None;
            }

            return r;
        }

        public NewFilterBoard.CommUnit.CommResult FirmBlockErease(byte blockcn,ref byte rt)//固件更新块擦除
        {
            lock (operationlocker)
            {
                if (curoperation == NewFilterBoard.CommUnit.CommOperations.None)
                    curoperation = NewFilterBoard.CommUnit.CommOperations.FirmBlockErease;
                else
                    return NewFilterBoard.CommUnit.CommResult.OperationConflict;
            }

            NewFilterBoard.CommUnit.CommResult r;
            try
            {
                NewFilterBoard.CommUnit.FrameStruc ThisTxFrame = new NewFilterBoard.CommUnit.FrameStruc() { addr1 = 0, addr2 = 0, cmd = FirmBlockEreaseCmd, datalen = 1 };
 
                ThisTxFrame.databuf[0] = blockcn;
               
                r = SendFrame(ThisTxFrame);
                if (r == NewFilterBoard.CommUnit.CommResult.Success)
                {
                    NewFilterBoard.CommUnit.FrameStruc ThisRxFrame = null;
                    r = ReadFrame(ref ThisRxFrame, (uint)blockcn*2500);//每块给2.5s的擦除时间
                    if (r == NewFilterBoard.CommUnit.CommResult.Success)
                    {
                        if (ThisRxFrame.cmd == FirmBlockEreaseCmd)
                        {
                            if (ThisRxFrame.datalen == 1)
                            {
                                rt= ThisRxFrame.databuf[0]; ;
                                r = NewFilterBoard.CommUnit.CommResult.Success;
                            }
                            else
                            {
                                r = NewFilterBoard.CommUnit.CommResult.DataLenError;
                            }
                        }
                        else
                            r = NewFilterBoard.CommUnit.CommResult.ReturnCmdError;
                    }
                }
            }
            catch
            {
                lock (operationlocker)
                {
                    curoperation = NewFilterBoard.CommUnit.CommOperations.None;
                }
                return NewFilterBoard.CommUnit.CommResult.Exception;
            }

            lock (operationlocker)
            {
                curoperation = NewFilterBoard.CommUnit.CommOperations.None;
            }

            return r;
        }

        public NewFilterBoard.CommUnit.CommResult FirmPageWrite(byte[] dat, ushort len, ref byte[] rt)//固件更新页写
        {
            lock (operationlocker)
            {
                if (curoperation == NewFilterBoard.CommUnit.CommOperations.None)
                    curoperation = NewFilterBoard.CommUnit.CommOperations.FirmPageWrite;
                else
                    return NewFilterBoard.CommUnit.CommResult.OperationConflict;
            }

            NewFilterBoard.CommUnit.CommResult r;
            try
            {
                NewFilterBoard.CommUnit.FrameStruc ThisTxFrame = new NewFilterBoard.CommUnit.FrameStruc() { addr1 = 0, addr2 = 0, cmd = FirmPageWriteCmd, datalen = len };

                for (int i = 0; i < len; i++)
                {
                    ThisTxFrame.databuf[i] = dat[i];
                }

                r = SendFrame(ThisTxFrame);
                if (r == NewFilterBoard.CommUnit.CommResult.Success)
                {
                    NewFilterBoard.CommUnit.FrameStruc ThisRxFrame = null;
                    r = ReadFrame(ref ThisRxFrame, FirmPageWriteTimeout);
                    if (r == NewFilterBoard.CommUnit.CommResult.Success)
                    {
                        if (ThisRxFrame.cmd == FirmPageWriteCmd)
                        {
                            if (ThisRxFrame.datalen == 3)
                            {
                                rt[0] = ThisRxFrame.databuf[0];
                                rt[1] = ThisRxFrame.databuf[1];
                                rt[2] = ThisRxFrame.databuf[2];
                                r = NewFilterBoard.CommUnit.CommResult.Success;
                            }
                            else
                            {
                                r = NewFilterBoard.CommUnit.CommResult.DataLenError;
                            }
                        }
                        else
                            r = NewFilterBoard.CommUnit.CommResult.ReturnCmdError;
                    }
                }
            }
            catch
            {
                lock (operationlocker)
                {
                    curoperation = NewFilterBoard.CommUnit.CommOperations.None;
                }
                return NewFilterBoard.CommUnit.CommResult.Exception;
            }

            lock (operationlocker)
            {
                curoperation = NewFilterBoard.CommUnit.CommOperations.None;
            }

            return r;
        }
#endregion
    }

}
