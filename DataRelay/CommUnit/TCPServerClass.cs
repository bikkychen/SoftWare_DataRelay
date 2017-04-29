#define DBG 

using System;
using System.Net.Sockets;
using System.Net;
using System.Threading;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Data;
using System.Text;



namespace CommUnit
{
    public class aa
    {
        public bool isTimeout = false;
        public IPEndPoint EP;
        public ushort ID;
        public aa(IPEndPoint ep,ushort id)
        {
            EP = ep;
            ID = id;
        }
    }

 


    public class TCPLlientList
    {
        public TcpClient thisClient;//这里面有端点
        public ushort ID=0;//DTU或PC的ID号（0表示数据中心，大于0才是有效ID），DTU的ID上线自己向数据中心主动注册，PC的ID上线后自己向数据中心主动申请，都有可能注册不成功或申请不成功
        public string Area="";//片区
        public string WellOrUser="";//井号或用户名
        public DateTime LoginTime;//登录时间
        public DateTime lastTime;//最后刷新时间
    }

    //数据中心TCPServerClass类中只操作“仪器信息”与“帐号信息”这二个数据库，其它数据库让主界面去操作
    //TCPServerClass类只拦截DTU的ID注册和PC的ID申请这二个帧，其它帧全部交给主界面处理，主界在再决定是转发或是拦截。
    public class TCPServerClass
    {
        //后面需要优化的事：
        //为每个共享变量加不同的锁，而不是共用一把锁（已做）
        //把TCPClientArray和TCPClentTable合并（已做）
        //日志记录实时入库，而不在本地硬盘上存储
        //主界面消息类型还要细化，尤其是数据转发，是否能转，从哪儿转到哪儿，都要显示清楚明白（已做）
        //每个接收线程收到完整帧后是否应该复制一个副本后再提交给主界面进行处理？在当前一问一答的情况下，好像没必要，以后高速通讯时可以搞。

        private System.Timers.Timer OnlineTimer = new System.Timers.Timer();//客户端刷新超时检查
        private System.Timers.Timer AreawellTimer = new System.Timers.Timer();//片区井号检索周期

        private TcpListener MyTCPListener;

        private bool ClientFlag = true;//true:DTU    false:PC

        private Thread listenThread, clientBeginThread;
        private bool m_bListening = false;

        public IPAddress MyIPAddress;
        public int MyPort;
        public IPEndPoint MyIPEndPoint;

        private int RxTimeOut;
        private int OnlineTimeOut;
        private int OnlineTimeOutCheckPer;
        private bool UseDB;//是否使用数据库，如使用：日志记录在数据库、可检索片区井号、可拦截采样数据并入库，如不使用：日志记录在硬盘文件、不可检索片区井号、不拦截和入库采样点


        private object lockTCPLink = new object();//下面这个在一定客户端列表的锁
        private List<TCPLlientList> TCPClientArray;

        //type>0:数据需要处理和转发， type=0:普通消息不涉及数据转发仅增加一条日志  
        public delegate void LogArrivedHandler(int type, IPEndPoint ep, UInt16 id, string mes, string area = "", string welloruser = "", FrameStruc bufFrame = null);//定义委托事件
        public event LogArrivedHandler LogArrived_Event;

        //updateNow=true:立即更新在线客户端界面 ，用于上线、下线、注册ID、变更片区井号、变更转发端点等重大事件 
        //updateNow=false:不要求立即更新在线客户端界面，主界面有自行决定是否更新，一般用于普通消息如帧接收或帧发送，
        public delegate void TCPClientLinkHandler(bool updateNow,object lockObj, List<TCPLlientList> tcpclientlist);//定义委托事件，用于客户端连接、掉线、心跳包等实时界面更新
        public event TCPClientLinkHandler TCPClientLink_Event;


        //rxtimeout:帧字节流接收超时，单位毫秒(ms)
        //onlinetimeout:客户端连接超时，单位分钟(m)
        public TCPServerClass(bool usedb,bool gf, IPAddress ip, int port, int rxtimeout, int onlinetimeout, int onlinetimeoutcheckper, TCPClientLinkHandler tCPClientLinkHandler, LogArrivedHandler messageHandler)
        {
            try
            {
                ClientFlag = gf;
                UseDB = usedb;
                MyIPAddress = ip;
                MyPort = port;
                RxTimeOut = rxtimeout;
                MyIPEndPoint = new IPEndPoint(MyIPAddress, MyPort);

                TCPClientArray = new List<TCPLlientList>();//每次启动服务时重新初始化在线客户端列表


                if (MyTCPListener != null)
                {
                    MyTCPListener.Stop();
                }
                MyTCPListener = new TcpListener(ip, port);

                TCPClientLink_Event += tCPClientLinkHandler;
                LogArrived_Event += messageHandler;

                WaitServerStop();

                TCPClientArray.Clear();
                listenThread = new Thread(new ThreadStart(startListen));
                m_bListening = true;
                listenThread.IsBackground = true;
                listenThread.Start();//开启一个独立线程监听UDP服务器接收到的数据   

                OnlineTimeOut = onlinetimeout;//超时强制下线时间
                OnlineTimer.Stop();
                OnlineTimeOutCheckPer = onlinetimeoutcheckper;
                OnlineTimer.Interval = OnlineTimeOutCheckPer*1000;//多少秒检查一次在线情况和检索片区井号
                OnlineTimer.AutoReset = true;
                OnlineTimer.Elapsed += new System.Timers.ElapsedEventHandler(OnlineTimerISR);
                OnlineTimer.Start();

                if(UseDB==true)
                {
                    LogArrived_Event(0, new IPEndPoint(MyIPAddress, MyPort), 0, string.Format("服务器启动，使用数据库，参数：{0}秒，{1}秒，{2}秒。", RxTimeOut, OnlineTimeOut, OnlineTimeOutCheckPer));
                }
                else
                {
                    LogArrived_Event(0, new IPEndPoint(MyIPAddress, MyPort), 0, string.Format("服务器启动，不使用数据库，参数：{0}秒，{1}秒，{2}秒。", RxTimeOut, OnlineTimeOut, OnlineTimeOutCheckPer));
                }
                
                         

                TCPClientLink_Event(true,lockTCPLink, TCPClientArray);//通知主界面服务器启动，但没有一行数据，仅显示各列的标题而已
            }
            catch (Exception ex)
            {
#if DBG
                MessageBox.Show("TCPServer:类构造" + System.Environment.NewLine + ex.ToString());
#endif
            }
        }


        //帧接收字节超时中断服务
        private void RxTimerISR(object sender)
        {
            ((aa)sender).isTimeout = true;
            LogArrived_Event(0, ((aa)sender).EP, ((aa)sender).ID, "帧字节接收超时");

        }

 

        //客户端巡检中断服务，主要检查是否有客户端刷新超时（立即让其下线）和检索数据库找出已注册ID的DTU的片区和井号
        private void OnlineTimerISR(object sender, System.Timers.ElapsedEventArgs e)
        {
            try
            {
                int n = 0;
                lock (lockTCPLink)
                {
                    for (int j = TCPClientArray.Count - 1; j >= 0; j--)//从所有在线客户端列表中查找刷新超时的项
                    {
                        TimeSpan ts = DateTime.Now - TCPClientArray[j].lastTime;
                        if (ts.TotalSeconds >= OnlineTimeOut)//有某行超时了，则再查找与这行端口号一致的客户端列表(List)
                        {
                            n++;
                            LogArrived_Event(0, (IPEndPoint)TCPClientArray[j].thisClient.Client.RemoteEndPoint, TCPClientArray[j].ID, "客户端下线（刷新超时）");//通知主界面日志更新
                            TCPClientArray[j].thisClient.Close();//客户端立即关闭，但暂不删除在线客户端列表中的这一项，在接收数据线程中会删除这一对应项的
                            TCPClientArray.RemoveAt(j);//立即删除这一项
                        }
                    }
                }
                if(n>0)
                {
                    TCPClientLink_Event(true, lockTCPLink, TCPClientArray);//通知主界面，有客户端下线了，要删除一行或几行      
                }
                  
            }
            catch (Exception ex)
            {
#if DBG
                MessageBox.Show("TCPServer:客户端刷新超时中断" + System.Environment.NewLine + ex.ToString());
#endif
            }
        }



        public void TcpServerStop()
        {
            try
            {
                WaitServerStop();

                //增加一行Log,服务器停止
                LogArrived_Event(0, new IPEndPoint(MyIPAddress, MyPort), 0, "服务器停止");

            }
            catch (Exception ex)
            {
#if DBG
                MessageBox.Show("TCPServer:服务器停止" + System.Environment.NewLine + ex.ToString());
#endif
            }
        }

        private void WaitServerStop()
        {
            m_bListening = false;
            TcpClient tcpClient = new TcpClient();
            if (listenThread != null)
            {
                tcpClient.Connect(MyIPAddress, MyPort);//人为构造一个连接，让监听线程跑下去，这样才可以停止该线程
                while (listenThread.ThreadState == ThreadState.Running) ;//等待监听线程自动结束
            }
        }

        private void startListen()
        {
            try
            {
                MyTCPListener.Start();

                while (m_bListening)//死循环监听是否有新的客户端上线
                {
                    TcpClient newTcpClient = MyTCPListener.AcceptTcpClient();//在这儿死等，直到有新的客户端连接上

                    #region  如果客户端在线列表中已存在这个端点，则把先前的那个客户端关闭并删掉，并加入现在这个客户端
                    int n = 0;
                    lock (lockTCPLink)
                    {
                        for (int j = TCPClientArray.Count - 1; j >= 0; j--) 
                        {
                            if (TCPClientArray[j].thisClient.Client.RemoteEndPoint.Equals(newTcpClient.Client.RemoteEndPoint)==true)
                            {
                                n++;
                                TCPClientArray[j].thisClient.Close();//客户端立即关闭
                                TCPClientArray.RemoveAt(j);//立即删除这一项
                            }
                        }
                    }
                    #endregion

                    #region 客户端列表增加一行
                    TCPLlientList tll = new TCPLlientList();//客户端刚上线
                    tll.thisClient = newTcpClient;
                    tll.LoginTime= DateTime.Now;
                    tll.lastTime = tll.LoginTime;
                    lock (lockTCPLink)
                    {
                        TCPClientArray.Add(tll);//客户端列表中增加一项
                    }
                    TCPClientLink_Event(true, lockTCPLink, TCPClientArray);//通知主界面增加一个新的客户端
                    #endregion

                    if(n==0)
                    {
                        LogArrived_Event(0, (IPEndPoint)newTcpClient.Client.RemoteEndPoint, 0, "新客户端上线");
                    }
                    else
                    {
                        LogArrived_Event(0, (IPEndPoint)newTcpClient.Client.RemoteEndPoint, 0, "客户端上线，覆盖相同端点。");
                    }
                    

                    //这二行必须放在上面增加一行LOG的后面，否则接收数据线程里很快就把这个客户端关闭了，则上面增加LOG的语句可能执行出错，因为对象不存在了
                    clientBeginThread = new Thread(new ParameterizedThreadStart(AcceptMsg));
                    clientBeginThread.Start(newTcpClient);//为刚才新增加的客户端开一个独立线程进行监听
                }
            }
            catch (Exception ex)
            {
#if DBG
                MessageBox.Show("TCPServer:客户端监听线程" + System.Environment.NewLine + ex.ToString());
#endif
            }
            finally
            {
                if (MyTCPListener != null)
                {
                    MyTCPListener.Stop();
                }
            }
        }

        //查询所有在线客户端的ID，只有ID>0的有效客户端才能被查询到。
        public List<ushort> FindOnlineClient()
        {
            List<ushort> lb = new List<ushort>();
            lock (lockTCPLink)
            {
                for (int y = TCPClientArray.Count - 1; y >= 0; y--)
                {
                    if(TCPClientArray[y].ID>0)
                    {
                        lb.Add((ushort)(TCPClientArray[y].ID));
                    }  
                }
            }
            return lb;
        }

        //public List<byte> FindOnlineDTU(string username="")//根据用户名查询在线DTU，用户名即片区名
        //{
        //    List<byte> lb=new List<byte>();

        //    if (username == "")//无用户名是查询全部在线DTU
        //    {
        //        lock (lockTCPLink)
        //        {
        //            for (int y = TCPClientArray.Count - 1; y >= 0; y--)
        //            {
        //                long l = ((IPEndPoint)(TCPClientArray[y].thisClient.Client.RemoteEndPoint)).Address.Address;//返回该在线DTU的EP
        //                lb.Add( (byte)l);
        //                lb.Add( (byte)(l >> 8));
        //                lb.Add((byte)(l >> 16));
        //                lb.Add((byte)(l >> 24));
        //                int k = ((IPEndPoint)(TCPClientArray[y].thisClient.Client.RemoteEndPoint)).Port;
        //                lb.Add((byte)(k));
        //                lb.Add((byte)(k >> 8));
        //                lb.Add((byte)(TCPClientArray[y].ID));
        //                lb.Add((byte)((TCPClientArray[y].ID) >> 8));
                       
        //            }
        //        }
        //    }
        //    else//有用户名时只查找用户名与片区名相同的DTU
        //    {
        //        lock (lockTCPLink)
        //        {
        //            for (int y = TCPClientArray.Count - 1; y >= 0; y--)//找出要发数据的客户端，最后一个有效的会被找到，实际上应该只有一个
        //            {
        //                if (TCPClientArray[y].Area == username)
        //                {
        //                    long l = ((IPEndPoint)(TCPClientArray[y].thisClient.Client.RemoteEndPoint)).Address.Address;//返回该在线DTU的EP
        //                    lb.Add((byte)l);
        //                    lb.Add((byte)(l >> 8));
        //                    lb.Add((byte)(l >> 16));
        //                    lb.Add((byte)(l >> 24));
        //                    int k = ((IPEndPoint)(TCPClientArray[y].thisClient.Client.RemoteEndPoint)).Port;
        //                    lb.Add((byte)(k));
        //                    lb.Add((byte)(k >> 8));
        //                    lb.Add((byte)(TCPClientArray[y].ID));
        //                    lb.Add((byte)((TCPClientArray[y].ID) >> 8));
        //                }
        //            }
        //        }
        //    }

        //    return lb;
        //}

        public bool IsOnline( TcpClient c)
        {//客户端自己断开连接和服务器强行关闭连接，这儿都会马上知道，但客户端突然掉电的情况就只能等刷新超来处理了
            try
            {
                return !((c.Client.Poll(1000, SelectMode.SelectRead) && (c.Client.Available == 0)) || !c.Client.Connected);
            }
            catch(Exception ex)
            {
                return false;
            }
        }

        private void AcceptMsg(object arg)
        {           
            TcpClient client = (TcpClient)arg;
            IPEndPoint ep = (IPEndPoint)client.Client.RemoteEndPoint;         
            UInt16 id = 0;
            string area = "";
            string welloruser = "";

            FrameStateEnum FrameState = FrameStateEnum.IDEL;//每个客户端申请一个独享的接收状态机
            FrameStruc RxFrame = new FrameStruc();//每个客户端申请一个独享的接收数据结构体

            //开启一个本线程专用的定时器
            aa bb = new aa(ep,id);
            System.Threading.Timer threadTimer = new System.Threading.Timer(new TimerCallback(RxTimerISR), bb, -1, RxTimeOut);
            threadTimer.Change(-1, RxTimeOut);//永远延时启动，即暂不开始计时



            NetworkStream ns;

            //字组处理
            while ((IsOnline(client)==true) && m_bListening)//服务器主线程要关闭服务的话，该变量会为False，则此时所有接收线程都要自己立即跳出死循环并把本客户端连接关闭
            {
                try
                {
                    ns = client.GetStream();//如果客户端连接被服务器强行关闭或客户端自己掉线，则该子程序会抛出异常
                    if(ns==null)
                    {
                        break;//客户端应该已失去连接了，直接跳出
                    }
                    if(ns.CanRead==false)//这一句必须加，否则当连接关闭后DataAvailable就已释放了，下文会抛出异常
                    {
                    break;
                    }

                    if (bb.isTimeout == true)
                    {
                        threadTimer.Change(-1, RxTimeOut);//暂不计时
                        FrameState = FrameStateEnum.IDEL;//检测到字节接收超时，则把状态机复位
                        bb.isTimeout = false;
                    }

                    if (ns.DataAvailable)//判断有数据再读，否则Read会阻塞线程。后面的业务逻辑无法处理
                    {
                        byte[] ReceiveBytes = new byte[2048+13];
                        //NetworkStream关闭或客户端连接关闭，下行也会抛出异常
                        int ReceiveLen = ns.Read(ReceiveBytes, 0, ReceiveBytes.Length); //如果无数据可读，则该子程序会阻塞，但上文已做了判断，于是在此不会阻塞。                     
                        ns.Flush();

                        if(ReceiveLen>0)
                        {
                           
                            threadTimer.Change(RxTimeOut, RxTimeOut);//等一个周期后才开始计时，超时到了后置接收状态机为IDEL状态，第一个参数设为0会马上开始计时，但也会马上进入中断，这与一般定时器不同
                            
                            StreamDataReceive(ReceiveBytes, ReceiveLen, ref FrameState, ref RxFrame);
                        }
                
                        if (FrameState == FrameStateEnum.OK)//收到了一个正确的帧
                        {
                            threadTimer.Change(-1, RxTimeOut);//暂停计时                          

                            #region 更新刷新时间，用户可选择不更新界面以减少CPU压力
                            lock (lockTCPLink)
                            {
                                for (int i = TCPClientArray.Count-1; i>=0; i--)
                                {
                                    if(TCPClientArray[i].thisClient.Equals(client)==true)
                                    {
                                        TCPClientArray[i].lastTime = DateTime.Now;//更新刷新时间，客户端相符的每行都更新，实际上只可能有一行
                                    }
                                }
                            }   
                            //大量客户端同时在线且都在通讯时，这一行可屏蔽，以减少主界面刷新次数
                            TCPClientLink_Event(false,lockTCPLink, TCPClientArray);//通知主界面有一行的刷新时间更新了，不强制更新，如果用户不要求即时更新，则这一事件调用会立即返回
                            #endregion

                            ushort idt = ((ushort)(RxFrame.tidh * 256 + RxFrame.tidl));//帧中自带的目标ID
                            ushort ids = ((ushort)(RxFrame.sidh * 256 + RxFrame.sidl));//帧中自带的源ID
                            string str_mes = string.Format("接收帧(目标ID={0} 源ID={1})", idt, ids);
                            #region 转发或拦截收到的完整帧
                            if (ClientFlag == true)//DTU送来的数据
                            {
                                if (RxFrame.cmd == 0xff)//心跳包，该帧要被服务器拦截,源ID=0时只接受注册ID这一个命令
                                {
                                    Encoding enASC = Encoding.ASCII;

                                    int bn = 0;
                                    lock (lockTCPLink)
                                    {
                                        for (int i = TCPClientArray.Count - 1; i >= 0; i--)
                                        {
                                            if (TCPClientArray[i].ID == ids)
                                            {
                                                if (TCPClientArray[i].thisClient.Client.RemoteEndPoint != ep)
                                                {
                                                    bn++;//只有EP不同且ID相同的客户端才认为是心跳包注册冲突，则先注册先得，后注册不得
                                                }
                                            }
                                        }
                                    }

                                    if (bn > 0)
                                    {
                                        str_mes += string.Format("：DTU心跳包注册ID({0})失败！ID冲突。", ids);
                                        LogArrived_Event(0, ep, id, str_mes, area, welloruser, RxFrame);
                                    }
                                    else
                                    {
                                        List<int> a = new List<int>();
                                        lock (lockTCPLink)
                                        {
                                            for (int i = TCPClientArray.Count - 1; i >= 0; i--)
                                            {
                                                if (TCPClientArray[i].thisClient.Equals(client) == true)
                                                {
                                                    a.Add(i);
                                                }
                                            }
                                        }

                                        if (a.Count < 1)//EP相同的客户端没有一个在列表中，实际上这种情况应该不会出现
                                        {
                                            str_mes += string.Format("：DTU心跳包注册ID({0})失败！通讯端点没上线。", ids);
                                            LogArrived_Event(0, ep, id, str_mes, area, welloruser, RxFrame);
                                        }
                                        else if (a.Count > 1)//EP相同的客户端在列表中超过一个，实际上这种情况也不会出现
                                        {
                                            str_mes += string.Format("：DTU心跳包注册ID({0})失败！通讯端点冲突。", ids);
                                            LogArrived_Event(0, ep, id, str_mes, area, welloruser, RxFrame);
                                        }
                                        else
                                        {
                                            if (id != ids)
                                            {
                                                id = ids;//把新id给这个客户端更新
                                                str_mes += string.Format("：DTU心跳包新注册ID({0})", id );
                                                lock (lockTCPLink)
                                                {
                                                    TCPClientArray[a[0]].ID = id;//更新通讯模块ID号  
                                                }
                                                TCPClientLink_Event(true, lockTCPLink, TCPClientArray);//通知主界面有某行的ID号更新了
                                            }
                                            else
                                            {
                                                str_mes += string.Format("：DTU心跳包再注册ID({0})", id);
                                            }
                                            LogArrived_Event(0, ep, id, str_mes, area, welloruser, RxFrame);

                                            //心跳包注册ID成功，则数据中心立即给DTU回发一帧，让地面箱立即开始定时采集与上传
                                            FrameStruc txFrame = new FrameStruc();
                                            txFrame.head1 = 0xe7;
                                            txFrame.head2 = 0xe7;
                                            txFrame.tidl = (byte)(id);//目标ID低字节
                                            txFrame.tidh = ((byte)(id >> 8));//目标ID高字节
                                            txFrame.sidl = 0;//源ID，数据中心ID=0
                                            txFrame.sidh = 0;//源ID，数据中心ID=0
                                            txFrame.addr1 = 0x00;
                                            txFrame.addr2 = 0x00;
                                            txFrame.cmd = 0xff;
                                            txFrame.datalen = 0;
                                            txFrame.check1 = 0x55;
                                            txFrame.check2 = 0xaa;
                                            TCPServerSend(txFrame);
                                        }
                                    }
                                }
                                else//非心跳包，则需要进行数据处理或转发
                                {
                                    if (ids != id)
                                    {
                                        str_mes += string.Format("：源ID({0})与该端点ID({1})不符。", ids,id); ;
                                        LogArrived_Event(0, ep, id, str_mes, area, welloruser, RxFrame);
                                    }
                                    else
                                    {
                                        LogArrived_Event(1, ep, id, str_mes, area, welloruser, RxFrame);
                                    }
                                }
                            }
                            else//PC机送来的数据
                            {
                                if (RxFrame.cmd == 0xff) //申请ID，如果数据中带用户名，则要进行帐号有效性检索
                                {
                                    if (RxFrame.datalen == 0)
                                    {
                                        List<int> a = new List<int>();
                                        lock (lockTCPLink)
                                        {
                                            for (int i = TCPClientArray.Count - 1; i >= 0; i--)
                                            {
                                                if (TCPClientArray[i].thisClient.Equals(client) == true)
                                                {
                                                    a.Add(i);
                                                }
                                            }
                                        }

                                        if (a.Count < 1)//EP相同的客户端没有一个在列表中，实际上这种情况应该不会出现
                                        {
                                            str_mes += "：PC申请ID（不带用户名），失败，通讯端点没上线。";
                                            LogArrived_Event(0, ep, id, str_mes, area, welloruser, RxFrame);
                                        }
                                        else if (a.Count > 1)//EP相同的客户端在列表中超过一个，实际上这种情况也不会出现
                                        {
                                            str_mes += "：PC申请ID（不带用户名），失败，通讯端点冲突。";
                                            LogArrived_Event(0, ep, id, str_mes, area, welloruser, RxFrame);
                                        }
                                        else
                                        {
                                            ushort idnew = 0;
                                            lock (lockTCPLink)
                                            {
                                                for (int i = TCPClientArray.Count - 1; i >= 0; i--)
                                                {
                                                    if (TCPClientArray[i].thisClient.Equals(client) == true)
                                                    {
                                                        idnew = TCPClientArray[i].ID;//先找出这个客户端的ID
                                                    }
                                                }
                                            }

                                            if (idnew > 0)//已分配了Id，则不再分配
                                            {
                                                str_mes += string.Format("：PC不带用户名申请ID，已分配ID({0})", id);
                                            }
                                            else//还没分配ID，则分配一个新的ID给这个客户端 
                                            {
                                                lock (lockTCPLink)
                                                {
                                                    for (int i = TCPClientArray.Count - 1; i >= 0; i--)
                                                    {
                                                        if (TCPClientArray[i].ID > idnew)
                                                        {
                                                            idnew = TCPClientArray[i].ID;//找出当前在线PC客户端的ID号中的最大值
                                                        }
                                                    }
                                                }
                                                idnew++;//给这个客户端分配一个顺序递增的ID
                                                id = idnew;
                                                lock (lockTCPLink)
                                                {
                                                    TCPClientArray[a[0]].ID = id;//更新PC客户端ID号 
                                                }
                                                TCPClientLink_Event(true, lockTCPLink, TCPClientArray);//通知主界面有某行的ID号更新了
                                                str_mes += string.Format("：PC不带用户名申请ID，新分配ID({0})", id );
                                            }
                                            LogArrived_Event(0, ep, id, str_mes, area, welloruser, RxFrame);
                                        }
                                        //申请ID成功，则返回PC终端一帧
                                        FrameStruc txFrame = new FrameStruc();
                                        txFrame.head1 = 0xe7;
                                        txFrame.head2 = 0xe7;
                                        txFrame.tidl = (byte)(id);//目标ID低字节
                                        txFrame.tidh = ((byte)(id >> 8));//目标ID高字节
                                        txFrame.sidl = 0;//源ID，数据中心ID=0
                                        txFrame.sidh = 0;//源ID，数据中心ID=0
                                        txFrame.addr1 = 0x00;
                                        txFrame.addr2 = 0x00;
                                        txFrame.cmd = RxFrame.cmd;
                                        txFrame.datalen = 0;
                                        txFrame.check1 = 0x55;
                                        txFrame.check2 = 0xaa;
                                        TCPServerSend(txFrame);
                                    }
                                    else   
                                    {
                                        str_mes += "：PC带用户名申请ID，暂不支持。";
                                        LogArrived_Event(0, ep, id, str_mes, area, welloruser, RxFrame);
                                        TCPClientLink_Event(true, lockTCPLink, TCPClientArray);//通知主界面有某行的ID号更新了，强制更新界面，因为有用户名注册了
                                    }

                                }
                                else//其它帧，则需要进行数据处理或转发
                                {
                                    if (ids != id)
                                    {
                                        str_mes += string.Format("：源ID({0})与该端点ID({1})不符。", ids, id); ;
                                        LogArrived_Event(0, ep, id, str_mes, area, welloruser, RxFrame);
                                    }
                                    else
                                    {
                                        LogArrived_Event(1, ep, id, str_mes, area, welloruser, RxFrame);
                                    }
                                }
                            }
                            #endregion
                             
                            FrameState = FrameStateEnum.IDEL;
                        }
                    }
                }
                catch(Exception ex)
                {
#if DBG
                    MessageBox.Show("TCPServer:Client读数据" + System.Environment.NewLine + ex.ToString());
#endif
                    break;
                }
            }

             
            try//以下是连接断开后要做的事，不管是服务器主动停止服务、刷新超时后服务器强行关闭连接、还是客户端自己断开连接
            {
                if (client != null)
                {
                    #region 删除客户端表格中的某一行
                    int n = 0;
                    lock (lockTCPLink)
                    {
                        for (int y = TCPClientArray.Count - 1; y >= 0; y--)
                        {
                            if (TCPClientArray[y].thisClient.Client.RemoteEndPoint .Equals( ep)==true)
                            {
                                n++;
                                TCPClientArray.RemoveAt(y);
                                if(m_bListening==true)
                                {
                                    LogArrived_Event(0, ep, id, "客户端下线（主动）");//通知主界面日志更新
                                }
                                else
                                {
                                    LogArrived_Event(0, ep, id, "客户端下线（服务器关闭）");//通知主界面日志更新
                                } 
                            }
                        }
                    }
                    if(n>0)
                    {
                        TCPClientLink_Event(true, lockTCPLink, TCPClientArray);//通知主界面，有客户端下线了，要删除一行或几行    
                    }                     
                    #endregion

                }
            }
            catch(Exception ex)
            {
#if DBG
                MessageBox.Show("TCPServer:接收数据线程" + System.Environment.NewLine + ex.ToString());
#endif
            }
            finally
            {
                client.Close();
            }
        }

//        //tc:正要发给数据的目地客户端   tcSource：提交数据的源头客户端
//        //此发送程序用于向PC发送数据，用ep来定位目的客户端
//        //一旦发送成功，则将该客户端的Source列中增加sourceEp
//        public void TCPServerSend(IPEndPoint Txep, FrameStruc bufFrame, IPEndPoint sourceEp)
//        {
//            try
//            {
//                if (Txep == null)
//                    return;

//                if (sourceEp == null)
//                    return;

//                if (bufFrame == null)
//                    return;

//                TcpClient tc = null;
//                TCPLlientList tcc=new TCPLlientList();
//                lock (lockTCPLink)
//                {
//                    for (int y = TCPClientArray.Count - 1; y >= 0; y--)//找出要发数据的客户端，最后一个有效的会被找到，实际上应该只有一个
//                    {
//                        if (TCPClientArray[y].thisClient.Client.RemoteEndPoint .Equals( Txep)==true)
//                        {
//                            tcc = TCPClientArray[y];
//                            tc = TCPClientArray[y].thisClient;
//                        }
//                    }
//                }
  
//                if (tc == null)//指定的目的端点没有找到，退出
//                {
//                    return;
//                }

//                NetworkStream sendStream = tc.GetStream();
//                Byte[] sendBytes = new byte[bufFrame.datalen + 11];
//                sendBytes[0] = bufFrame.head1;
//                sendBytes[1] = bufFrame.head2;
//                sendBytes[2] = bufFrame.idl;
//                sendBytes[3] = bufFrame.idh;
//                sendBytes[4] = bufFrame.addr1;
//                sendBytes[5] = bufFrame.addr2;
//                sendBytes[6] = bufFrame.cmd;
//                sendBytes[7] = (byte)(bufFrame.datalen);
//                sendBytes[8] = (byte)(bufFrame.datalen >> 8);
//                int i = 0;
//                for (i = 0; i < bufFrame.datalen; i++)
//                {
//                    sendBytes[9 + i] = bufFrame.databuf[i];
//                }
//                sendBytes[9 + i] = bufFrame.check1;
//                sendBytes[10 + i] = bufFrame.check2;

//                sendStream.Write(sendBytes, 0, sendBytes.Length);
//                sendStream.Flush();

//                if (sourceEp.Equals(MyIPEndPoint)==true)
//                {
//                    LogArrived_Event(-1, (IPEndPoint)tcc.thisClient.Client.RemoteEndPoint, tcc.ID, "服务器向PC发送完整帧", tcc.Area, tcc.WellOrUser, bufFrame);
//                }
//                else
//                {
//                    LogArrived_Event(-1, (IPEndPoint)tcc.thisClient.Client.RemoteEndPoint, tcc.ID, "DTU向PC发送完整帧", tcc.Area, tcc.WellOrUser, bufFrame);
//                }

//            }
//            catch(Exception ex)
//            {
//#if DBG
//                MessageBox.Show("TCPServer:发送数据子程序1" + System.Environment.NewLine + ex.ToString());
//#endif
//            }
//        }

        //id:正要发给数据的目地客户端   tcSource：提交数据的源头客户端
        //此发送子程序用于PC向DTU，用DTU的ID来定位目的客户端
        //一旦发送成功，则将该客户端的Source列中增加sourceEp

        public void TCPServerSend(FrameStruc bufFrame)
        {
            if (bufFrame == null)
            {
                return;
            }

            try
            {
                ushort idt = (ushort)(bufFrame.tidh * 256 + bufFrame.tidl);
                ushort ids = (ushort)(bufFrame.sidh * 256 + bufFrame.sidl);

                if (idt < 1)//id<1的客户端编号为非法
                {
                    LogArrived_Event(0, MyIPEndPoint, idt, string.Format("目标ID({0})非法,发送帧失败。", idt),"","", bufFrame);
                    return;
                }
                    

                //发送前先查询该目的客户端的ID是否在在线列表里，并找出对应的EndPoint
                TCPLlientList tcc = new TCPLlientList();
                lock (lockTCPLink)
                {
                    for (int y = TCPClientArray.Count - 1; y >= 0; y--)//找出要发数据的客户端，最前面一个有效的会被找到，实际上应该只有一个
                    {
                        if (TCPClientArray[y].ID == idt)
                        {
                            tcc = TCPClientArray[y];
                        }
                    }
                }

                if (tcc.thisClient == null)
                {
                    LogArrived_Event(0, MyIPEndPoint, idt, string.Format("目标ID({0})不在线,发送帧失败。", idt), "", "", bufFrame);
                    return;
                }

                NetworkStream sendStream = tcc.thisClient.GetStream();
                Byte[] sendBytes = new byte[bufFrame.datalen + 13];
                sendBytes[0] = bufFrame.head1;
                sendBytes[1] = bufFrame.head2;
                sendBytes[2] = bufFrame.tidl;
                sendBytes[3] = bufFrame.tidh;
                sendBytes[4] = bufFrame.sidl;
                sendBytes[5] = bufFrame.sidh;
                sendBytes[6] = bufFrame.addr1;
                sendBytes[7] = bufFrame.addr2;
                sendBytes[8] = bufFrame.cmd;
                sendBytes[9] = (byte)(bufFrame.datalen);
                sendBytes[10] = (byte)(bufFrame.datalen >> 8);
                int i = 0;
                for (i = 0; i < bufFrame.datalen; i++)
                {
                    sendBytes[11 + i] = bufFrame.databuf[i];
                }
                sendBytes[11 + i] = bufFrame.check1;
                sendBytes[12 + i] = bufFrame.check2;

                sendStream.Write(sendBytes, 0, sendBytes.Length);
                sendStream.Flush();
                
                LogArrived_Event(-1, (IPEndPoint)tcc.thisClient.Client.RemoteEndPoint, tcc.ID, string.Format("发送帧(目标ID={0} 源ID={1})",idt,ids), tcc.Area, tcc.WellOrUser, bufFrame);
              
            }
            catch(Exception ex)
            {
#if DBG
                MessageBox.Show("TCPServer:发送数据" + System.Environment.NewLine + ex.ToString());
#endif
            }
        }

        //public IPEndPoint FindSourceEP(IPEndPoint ep)//用某个在线的IPEndPoint查找其转发IPEndPoint
        //{
        //    TcpClient tc = null;
        //    TCPLlientList tcc = new TCPLlientList();

        //    //先看ep是否在客户端表格中
        //    lock (lockTCPLink)
        //    {
        //        for (int y = TCPClientArray.Count - 1; y >= 0; y--)
        //        {
        //            if (TCPClientArray[y].thisClient.Client.RemoteEndPoint == ep)
        //            {
        //                tcc = TCPClientArray[y];
        //                tc = TCPClientArray[y].thisClient;
        //            }
        //        }
        //    }

        //    if(tc==null)
        //    {
        //        return null;
        //    }
           
        //    return (tcc.sEndPoint);
        //}

        public void StreamDataReceive(byte[] dat, int len,ref FrameStateEnum FrameState,ref FrameStruc RxFrame)//流数据解释分离程序
        {
            if(len<=0)
            {
                return;
            }
            //RxTimer.Start();
            byte b = 0;
            for (int i = 0; i < len; i++)
            {
                b = dat[i];
                switch (FrameState)
                {
                    case FrameStateEnum.IDEL:
                        RxFrame.datalencn = 0;
                        if(ClientFlag==true)
                        {
                            if (b == 0xe7)
                            {
                                FrameState = FrameStateEnum.HEAD1;
                                RxFrame.head1 = b;
                            }
                            else
                                FrameState = FrameStateEnum.IDEL;
                        }
                        else
                        {
                            if (b == 0x7e)
                            {
                                FrameState = FrameStateEnum.HEAD1;
                                RxFrame.head1 = b;
                            }
                            else
                                FrameState = FrameStateEnum.IDEL;
                        }
                        break;
                    case FrameStateEnum.HEAD1:
                        if(ClientFlag==true)
                        {
                            if (b == 0xe7)
                            {
                                FrameState = FrameStateEnum.HEAD2;
                                RxFrame.head2 = b;
                            }
                            else
                                FrameState = FrameStateEnum.IDEL;
                        }
                        else
                        {
                            if (b == 0x7e)
                            {
                                FrameState = FrameStateEnum.HEAD2;
                                RxFrame.head2 = b;
                            }
                            else
                                FrameState = FrameStateEnum.IDEL;
                        }
                        break;
                    case FrameStateEnum.HEAD2:
                        FrameState = FrameStateEnum.TIDL;
                        RxFrame.tidl = b;
                        break;
                    case FrameStateEnum.TIDL:
                        FrameState = FrameStateEnum.TIDH;
                        RxFrame.tidh = b;
                        break;
                    case FrameStateEnum.TIDH:
                        FrameState = FrameStateEnum.SIDL;
                        RxFrame.sidl = b;
                        break;
                    case FrameStateEnum.SIDL:
                        FrameState = FrameStateEnum.SIDH;
                        RxFrame.sidh = b;
                        break;
                    case FrameStateEnum.SIDH:
                        FrameState = FrameStateEnum.ADDR1;
                        RxFrame.addr1 = b;
                        break;
                    case FrameStateEnum.ADDR1:
                        FrameState = FrameStateEnum.ADDR2;
                        RxFrame.addr2 = b;
                        break;
                    case FrameStateEnum.ADDR2:
                        FrameState = FrameStateEnum.COMM;
                        RxFrame.cmd = b;
                        break;
                    case FrameStateEnum.COMM:
                        FrameState = FrameStateEnum.DATALEN1;
                        RxFrame.datalen = b;
                        break;
                    case FrameStateEnum.DATALEN1:
                        RxFrame.datalen += (UInt16)(b * 256);
                        RxFrame.datalentem = RxFrame.datalen;
                        if (RxFrame.datalen == 0)
                            FrameState = FrameStateEnum.CHECK1;
                        else
                            FrameState = FrameStateEnum.DATABUF;
                        break;
                    case FrameStateEnum.DATABUF:
                        RxFrame.databuf[RxFrame.datalencn] = b;
                        RxFrame.datalencn++;
                        RxFrame.datalentem--;
                        if (RxFrame.datalentem == 0)
                        {
                            FrameState = FrameStateEnum.CHECK1;
                        }
                        break;
                    case FrameStateEnum.CHECK1:
                        FrameState = FrameStateEnum.CHECK2;
                        RxFrame.check1 = b;
                        break;
                    case FrameStateEnum.CHECK2:
                        RxFrame.check2 = b;
                        if (CRCCheck(RxFrame) == true)
                        {
                            //RxTimer.Stop();
                            FrameState = FrameStateEnum.OK; 
                        }
                        else
                        {
                            FrameState = FrameStateEnum.IDEL;
                        }
                        break;
                }
            }
        }

        public bool CRCCheck(FrameStruc dat)
        {
            if( (dat.check1==0x55) && (dat.check2 == 0xaa) )
            {
                return true;
            }
            else
            {
                return false;
            }            
        }
    }
}
