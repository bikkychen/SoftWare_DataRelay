using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using System.Net;
using System.Threading;

namespace CommUnit
{
    /// <summary>
    /// UDP服务器对象
    /// </summary>
    public class UDPServerClass
    {
        //public delegate void MessageHandler(string Message);//定义委托事件
        public delegate void MessageHandler(byte[] receiveBytes, int receiveLen);//定义委托事件
        public event MessageHandler MessageArrived;
        public UdpClient ReceiveUdpClient;

        /// <summary>
        /// 侦听端口名称
        /// </summary>
        public int PortName;

        /// <summary>
        /// 本地地址
        /// </summary>
        public IPEndPoint LocalIPEndPoint;


   
        public IPEndPoint RemoteIPEndPoint;
        private byte[] ReceiveBytes;

        /// <summary>
        /// 日志记录
        /// </summary>
        public StringBuilder Note_StringBuilder;
        /// <summary>
        /// 本地IP地址
        /// </summary>
        public IPAddress MyIPAddress;

        public UDPServerClass(int port)
        {
            //获取本机可用IP地址
            IPAddress[] ips = Dns.GetHostAddresses(Dns.GetHostName());
            foreach (IPAddress ipa in ips)
            {
                if (ipa.AddressFamily == AddressFamily.InterNetwork)
                {
                    MyIPAddress = ipa;//获取本地IP地址
                   // break;//此处不能跳出，要一直找，找到最后一个因特网地址即为当前正在联网的IP，前面的都是虚拟IP
                }
            }
          
            Note_StringBuilder = new StringBuilder();
            PortName = port;
            LocalIPEndPoint = new IPEndPoint(MyIPAddress, PortName);
            try
            {
                ReceiveUdpClient = new UdpClient(LocalIPEndPoint);
            }
            catch (Exception e)
            {
                return;
            }
            // RemoteIPEndPoint = new IPEndPoint(IPAddress.Any, 0);
           // RemoteIPEndPoint = new IPEndPoint(IPAddress.Broadcast, 8080);//向远程所有机器广播，但端口指定为8080
           
            RemoteIPEndPoint = new IPEndPoint(new IPAddress(0x0104a8c0), 8080);//192.168.4.1
        }
    
        public void Thread_Listen()
        {
            //创建一个线程接收远程主机发来的信息
            Thread myThread = new Thread(ReceiveData);
            myThread.IsBackground = true;
            myThread.Start();
        }

        /// <summary>
        /// 接收数据
        /// </summary>
        private void ReceiveData()
        {                   
            while (true)
            {
                try
                {
                    //关闭udpClient 时此句会产生异常
                    ReceiveBytes = ReceiveUdpClient.Receive(ref RemoteIPEndPoint);//这儿死等待，直到有数据包收到，里面包含了发送端的地址和端口
                    MessageArrived( ReceiveBytes, ReceiveBytes.Length);//调用事件，通知所有注册了该事件的方法执行
                    //string receiveMessage = Encoding.Default.GetString(receiveBytes, 0, receiveBytes.Length);
                    ////receiveMessage = ASCIIEncoding.ASCII.GetString(receiveBytes, 0, receiveBytes.Length);
                    //MessageArrived(string.Format("{0}来自{1}:{2}", DateTime.Now.ToString(), remote, receiveMessage));//调用事件，通知所有注册了该事件的方法执行
                    //try
                    //{
                    //    Byte[] sendBytes = Encoding.ASCII.GetBytes("Retuen OK\r\n");

                    //    ReceiveUdpClient.Send(sendBytes, sendBytes.Length, remote);
                    //}
                    //catch (Exception e)
                    //{
                    //    break;
                    //}
                }
                catch
                {
                    break;
                }
            }
        }

        /// <summary>
        /// 添加日志信息到Note_StringBuilder
        /// </summary>
        public void AddMessage_Note_StringBuilder()
        {

        }

        public void UDPServerSend(byte[] sendBytes, int len)
        {
            ReceiveUdpClient.Send(sendBytes, len, RemoteIPEndPoint);
        }
    }
}

 