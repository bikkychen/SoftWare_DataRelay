using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CommUnit
{
   public class UDPClientClass
    {
        UDPClientClass(int port)
        {
            byte[] data = new byte[1024];
            //string input, stringData;

            ////构建TCP 服务器
            //RecordSave(2, string.Format("This is a Client, host name is {0}", Dns.GetHostName()), false);

            ////设置服务IP，设置TCP端口号
            //IPEndPoint ip = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 8001);

            ////定义网络类型，数据连接类型和网络协议UDP
            //Socket server = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

            //string welcome = "你好! ";
            //data = Encoding.ASCII.GetBytes(welcome);
            //server.SendTo(data, data.Length, SocketFlags.None, ip);
            //IPEndPoint sender = new IPEndPoint(IPAddress.Any, 0);
            //EndPoint Remote = (EndPoint)sender;

            //data = new byte[1024];
            ////对于不存在的IP地址，加入此行代码后，可以在指定时间内解除阻塞模式限制
            //int recv = server.ReceiveFrom(data, ref Remote);
            //RecordSave(2, string.Format("Message received from {0}: ", Remote.ToString()), false);
            //RecordSave(2, string.Format(Encoding.ASCII.GetString(data, 0, recv)), false);
            ////  while (true)
            //{
            //    input = "0123456789";

            //    server.SendTo(Encoding.ASCII.GetBytes(input), Remote);
            //    data = new byte[1024];
            //    recv = server.ReceiveFrom(data, ref Remote);
            //    stringData = Encoding.ASCII.GetString(data, 0, recv);
            //    RecordSave(2, (stringData), false);
            //}
            //RecordSave(2, ("Stopping Client."), false);
            //server.Close();
        }
    }
}
