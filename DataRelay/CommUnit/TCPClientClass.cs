using System;
using System.Text;
using System.Windows.Forms;
using System.Net.Sockets;
using System.Threading;
using System.Net;

namespace CommUnit
{

    public class TCPClientClass
    {
        static public TcpClient client;
        private Thread client_th;
        public NetworkStream ns;
        public IPAddress MyIP;
        public int MyPort;
        public IPAddress ServerIP;
        public int ServerPort;
        public delegate void MessageHandler( byte[] receiveBytes);//定义委托事件
        public event MessageHandler MessageArrived;

        public TCPClientClass(int port)
        {
            //获取本机可用IP地址
            IPAddress[] ips = Dns.GetHostAddresses(Dns.GetHostName());
            //foreach (IPAddress ipa in ips)
            //{
            //    if (ipa.AddressFamily == AddressFamily.InterNetwork)
            //    {
            //        MyIP = ipa;//获取本地IP地址
            //        break;
            //    }
            //}
            if(ips.Length>0)
            {
                MyIP = ips[ips.Length-1];
            }

            MyPort = port;

            if(client!=null)
            {
                client.Close();//先释放占用的端口
            }
            
            // client = new TcpClient(ip, port);//此处为TCP服务器的IP地址和端口，只有指定了正确的地址端口且服务器开启的情况下才能连接成功，否则抛出异常。
            IPEndPoint iep = new IPEndPoint(MyIP, MyPort);//指定客户端地址与端口
            client = new TcpClient(iep);//用端点来初始化，则此处是客户端(本机)IP地址和端口
        }

        public bool Connect2Server(string ip,int port,out Exception mye)
        {
            mye = null;
            if(client!=null)
            {
                if(client.Connected==true)
                {
                    return true;
                }
            }

            ServerIP = IPAddress.Parse(ip);
            ServerPort = port;
            try
            {             
                client.Connect(ServerIP, ServerPort);

                ns = client.GetStream();
                client_th = new Thread(new ThreadStart(AcceptMsg));
                client_th.Start();
              //  Console.Write("连接成功");
                return true;           
            }
            catch (System.Exception e)
            {
                // Console.Write(e.ToString());
                mye = e;
                return false;
            }
        }
        private void AcceptMsg()
        {
           

           // StreamReader sr = new StreamReader(ns);//流读写器
            //字组处理
            while (true)
            {
                try
                {
                    if(client.Connected==false)//此方法好像无法判断服务器端主动关闭连接的情况，服务器端关闭连接后客户端需要再次手动连接才能收到数据
                    {
                        MessageBox.Show("TCP客户端失去连接！");
                        break;
                    }
                    byte[] bytes = new byte[1024];
                    int bytesread = ns.Read(bytes, 0, bytes.Length);//此方法会阻塞，但服务器端关闭连接后仍会马上返回只是没有数据，于是需要额外的判断连接状态
                    //显示
                   
                    //string msg = Encoding.Default.GetString(bytes, 0, bytesread);                  
                    //  Console.Write(msg);
                    if(bytesread>0)
                    {
                        MessageArrived(bytes);//调用事件，通知所有注册了该事件的方法执行
                    }                   
                    ns.Flush();
                    // ns.Close();
                   // SendMsg(msg);
                }
                catch
                {
                    MessageBox.Show("TCP客户端接收数据失错！");
                    break;
                }
            }
        }

        private void SendMsg(String str)
        {
            if (client == null)
                return;
            Byte[] sendBytes = Encoding.Default.GetBytes(str);
            ns.Write(sendBytes, 0, sendBytes.Length);
            ns.Flush();
           // ns.Close();
           // Console.Write( "Msg Sent!");
        }
    }
}
