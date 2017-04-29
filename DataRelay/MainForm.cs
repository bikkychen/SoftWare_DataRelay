using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using CommUnit;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Collections;
using System.Net.NetworkInformation;
using System.Data.SqlClient;
using System.Data.Sql;
using System.Data.Common;

namespace DataRelay
{


    public partial class MainForm : Form
    {
        public TCPServerClass TCPServer_DTU, TCPServer_PC;
        private object lockGridViewDTU = new object();
        private object lockGridViewPC = new object();
        private object lockDB = new object();//数据库操作锁

        private bool UseDB_DTU,UseDB_PC;//是否使用数据库，如使用：日志记录在数据库、可检索片区井号、可拦截采样数据并入库，如不使用：日志记录在硬盘文件、不可检索片区井号、不拦截和入库采样点


        private SqlConnection SqlConnection_Log_DTU, SqlConnection_Log_PC,SqlConnection_AllData,SqlConnection_WellInfo,SqlConnection_WellData;
        private DbCommand DbCommand_Log_DTU, DbCommand_Log_PC, DbCommand_AllData, DbCommand_WellInfo, DbCommand_WellData;
        private string DataBase_Log_DTU = "", DataBase_Log_PC = "", DataBase_AllData = "", DataBase_WellInfo = "", DataBase_WellData = "";//存储选择的数据库
        private string DataTable_Log_DTU = "", DataTable_Log_PC = "", DataTable_AllData = "", DataTable_WellInfo = "", DataTable_WellData = "";//存储选择的数据集
      
        public MainForm()
        {
            InitializeComponent();
          //  CheckForIllegalCrossThreadCalls = false;//不要这个句的话表格不能自动刷新
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            this.NotifyIcon1.Text = this.Text;//设置托盘文本    
        }

        //显示本机所有IP地址
        private void comboBox_DropDown(object sender, EventArgs e)
        {
            ((ComboBox)(sender)).Items.Clear();
            IPAddress MyIPAddress = IPAddress.Parse("0.0.0.0");
            //获取本机可用IP地址
            IPAddress[] ips = Dns.GetHostAddresses(Dns.GetHostName());
            foreach (IPAddress ipa in ips)
            {
                if (ipa.AddressFamily == AddressFamily.InterNetwork)
                {
                    ((ComboBox)(sender)).Items.Add(ipa.ToString());
                }
            }
        }

        //表格控件自动加行号
        private void dataGridView_RowPostPaint(object sender, DataGridViewRowPostPaintEventArgs e)
        {
            try
            {
                DataGridView dv = (DataGridView)sender;
                Color color = dv.RowHeadersDefaultCellStyle.ForeColor;
                if (dv.Rows[e.RowIndex].Selected)
                    color = dv.RowHeadersDefaultCellStyle.SelectionForeColor;
                else
                    color = dv.RowHeadersDefaultCellStyle.ForeColor;
                using (SolidBrush b = new SolidBrush(color))
                {
                    e.Graphics.DrawString((e.RowIndex + 1).ToString(), e.InheritedRowStyle.Font, b, e.RowBounds.Location.X + 20, e.RowBounds.Location.Y + 6);
                }
            }
            catch
            {

            }
        }

        #region 响应服务器的事件
        //用字符串解析出IP地址和端口
        private void GetByteFromIPEndpoint(string str, ref byte[] bt)
        {
            try
            {
                int a1, a2, a3, a4;
                a1 = str.IndexOf(".");
                a2 = str.IndexOf(".", a1 + 1);
                a3 = str.IndexOf(".", a2 + 1);
                a4 = str.IndexOf(":", a3 + 1);

                bt[0] = byte.Parse(str.Substring(0, a1));
                bt[1] = byte.Parse(str.Substring(a1 + 1, a2 - a1 - 1));
                bt[2] = byte.Parse(str.Substring(a2 + 1, a3 - a2 - 1));
                bt[3] = byte.Parse(str.Substring(a3 + 1, a4 - a3 - 1));
                bt[4] = (byte)(ushort.Parse(str.Substring(a4 + 1)));
                bt[5] = (byte)((ushort.Parse(str.Substring(a4 + 1))) >> 8);
            }
            catch
            {

            }
        }

        //调用委托更新主界面的四个表格控件
        static private Object Lock_Log_DTU = new Object();//线程锁，锁住DTU日志表格
        static private Object Lock_Log_PC = new Object();//线程锁，锁住PC日志

        private delegate void UpdateGridView_LogArrived_delegate(DataGridView dg, string[] strmes, object lockObj);
        private void UpdateGridView_LogArrived(DataGridView dg, string[] strmes, object lockObj)
        {
            try
            {
                if (dg.InvokeRequired)
                {
                    UpdateGridView_LogArrived_delegate d = new UpdateGridView_LogArrived_delegate(UpdateGridView_LogArrived);
                    dg.Invoke(d, new Object[] { dg, strmes, lockObj });
                }
                else
                {                     
                    try
                    {
                        lock (lockObj)
                        {
                            dg.Rows.Add(strmes);//Log表格中增加一行

                            if (dg.Rows.Count > 99)//为节省内存，最大显示99条日志
                            {
                                dg.Rows.RemoveAt(0);
                            }

                            if (dg.RowCount > 0)//这一句不能省
                            {
                                dg.FirstDisplayedScrollingRowIndex = dg.RowCount - 1;//始终显示最后一行
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }
        }

        private delegate void UpdateGridView_ClientLink_delegate(DataGridView dg, DataTable dt, object lockObj);
        private void UpdateGridView_ClientLink( DataGridView dg, DataTable dt, object lockObj)
        {
            try
            {
                if (dg.InvokeRequired)
                {
                    UpdateGridView_ClientLink_delegate d = new UpdateGridView_ClientLink_delegate(UpdateGridView_ClientLink);
                    dg.Invoke(d, new Object[] { dg, dt, lockObj });
                }
                else
                {
                    try
                    {
                        lock (lockObj)
                        {
                            dg.DataSource = null;
                            dg.DataSource = dt;

                            if (dg.RowCount > 0)//这一句不能省
                            {
                                dg.FirstDisplayedScrollingRowIndex = dg.RowCount - 1;//始终显示最后一行
                            }

                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }
        }

        void Event_LogArrived_DTU(int type, IPEndPoint ep, UInt16 id, string mes, string area = "", string welloruser = "", FrameStruc bufFrame = null)//TCP_DTU服务器的Log服务程序
        {
            #region 更新表格界面
            string[] strmes = new string[7];
            strmes[0] = DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss");//时间    
           // strmes[0] = DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss fff");//时间  ，不能到毫秒，否则数据库日志不能入库  
            strmes[1] = ep.ToString();//端点
            strmes[2] = id.ToString();//ID
            strmes[3] = area;//片区
            strmes[4] = welloruser;//井号
            strmes[5] = mes;//消息
            if (bufFrame == null)//没有数据可显示
            {
                strmes[6] = "";
            }
            else
            {
                string st = "";
                st += string.Format("{0} ", bufFrame.head1.ToString("X2"));
                st += string.Format("{0} ", bufFrame.head2.ToString("X2"));
                st += string.Format("{0} ", bufFrame.tidl.ToString("X2"));
                st += string.Format("{0} ", bufFrame.tidh.ToString("X2"));
                st += string.Format("{0} ", bufFrame.sidl.ToString("X2"));
                st += string.Format("{0} ", bufFrame.sidh.ToString("X2"));
                st += string.Format("{0} ", bufFrame.addr1.ToString("X2"));
                st += string.Format("{0} ", bufFrame.addr2.ToString("X2"));
                st += string.Format("{0} ", bufFrame.cmd.ToString("X2"));
                st += string.Format("{0} ", ((bufFrame.datalen) & 0xff).ToString("X2"));
                st += string.Format("{0} ", ((bufFrame.datalen) >> 8).ToString("X2"));
                for (int i = 0; i < bufFrame.datalen; i++)
                {
                    st += string.Format("{0} ", bufFrame.databuf[i].ToString("X2"));
                }
                st += string.Format("{0} ", bufFrame.check1.ToString("X2"));
                st += string.Format("{0} ", bufFrame.check2.ToString("X2"));
                strmes[6] = st;
            }
            UpdateGridView_LogArrived(dataGridView3, strmes, Lock_Log_DTU);
            #endregion

            #region 日志存盘
            if (checkBox4.Checked == true)
            {
                //StreamWriter sw = new StreamWriter(@"D://Log.txt");
                StreamWriter fs3 = new StreamWriter(Application.StartupPath + "\\Log_DTU.txt", true);
                string strw = "";
                for (int i = 0; i < strmes.Length; i++)
                {
                    strw += strmes[i];
                    strw += "\t";
                }
                fs3.WriteLine(strw);
                fs3.Close();
            }
            #endregion

            #region 日志入库
            SQLSaveLog_DTU(strmes[0], strmes[1], strmes[2], strmes[3], strmes[4], strmes[5], strmes[6]);
            #endregion   

            #region 数据处理
            if (type > 0)//需要进行数据处理或转发
            {
                if (bufFrame != null)
                {
                    if (TCPServer_PC != null)
                    {
                        TCPServer_PC.TCPServerSend(bufFrame);//DTU向PC转发
                    }

                    if(bufFrame.cmd==0x03)//采样命令，需要将数据入库
                    {

                    }
                }
            }
            #endregion
        }

        void Event_LogArrived_PC(int type, IPEndPoint ep, UInt16 id, string mes, string area = "", string welloruser = "", FrameStruc bufFrame = null)//TCP_PC服务器的Log服务程序
        {
            #region 更新表格界面
            string[] strmes = new string[7];
            strmes[0] = DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss");//时间    
            // strmes[0] = DateTime.Now.ToString("yyyy-MM-dd hh:mm:ss fff");//时间  ，不能到毫秒，否则数据库日志不能入库  
            strmes[1] = ep.ToString();//端点
            strmes[2] = id.ToString();//ID
            strmes[3] = area;//片区
            strmes[4] = welloruser;//用户名
            strmes[5] = mes;//消息
            if (bufFrame == null)//没有数据可显示
            {
                strmes[6] = "";
            }
            else
            {
                string st = "";
                st += string.Format("{0} ", bufFrame.head1.ToString("X2"));
                st += string.Format("{0} ", bufFrame.head2.ToString("X2"));
                st += string.Format("{0} ", bufFrame.tidl.ToString("X2"));
                st += string.Format("{0} ", bufFrame.tidh.ToString("X2"));
                st += string.Format("{0} ", bufFrame.sidl.ToString("X2"));
                st += string.Format("{0} ", bufFrame.sidh.ToString("X2"));
                st += string.Format("{0} ", bufFrame.addr1.ToString("X2"));
                st += string.Format("{0} ", bufFrame.addr2.ToString("X2"));
                st += string.Format("{0} ", bufFrame.cmd.ToString("X2"));
                st += string.Format("{0} ", ((bufFrame.datalen) & 0xff).ToString("X2"));
                st += string.Format("{0} ", ((bufFrame.datalen) >> 8).ToString("X2"));
                for (int i = 0; i < bufFrame.datalen; i++)
                {
                    st += string.Format("{0} ", bufFrame.databuf[i].ToString("X2"));
                }
                st += string.Format("{0} ", bufFrame.check1.ToString("X2"));
                st += string.Format("{0} ", bufFrame.check2.ToString("X2"));
                strmes[6] = st;
            }
            UpdateGridView_LogArrived(dataGridView4, strmes, Lock_Log_PC);
            #endregion

            #region 日志存盘
            if (checkBox7.Checked == true)
            {
                StreamWriter fs3 = new StreamWriter(Application.StartupPath + "\\Log_PC.txt", true);
                string strw = "";
                for (int i = 0; i < strmes.Length; i++)
                {
                    strw += strmes[i];
                    strw += "\t";
                }
                fs3.WriteLine(strw);
                fs3.Close();
            }
            #endregion

            #region 日志入库
            SQLSaveLog_PC(strmes[0], strmes[1], strmes[2], strmes[3], strmes[4], strmes[5], strmes[6]);
            #endregion

            #region 数据处理
            if (type > 0)//需要进行数据处理或转发
            {
                if (bufFrame != null)
                {
                    switch (bufFrame.cmd)
                    {
                        case 0xfe:
                            #region 查询在线DTU数量及ID
                            if (TCPServer_DTU != null)
                            {
                                ushort n = 0;
                                FrameStruc txFrame = new FrameStruc();
                                txFrame.head1 = 0xe7;
                                txFrame.head2 = 0xe7;
                                txFrame.tidl = (byte)bufFrame.sidl;
                                txFrame.tidh = (byte)(bufFrame.sidl >> 8);
                                txFrame.sidl = 0x00;
                                txFrame.sidh = 0x00;
                                txFrame.addr1 = 0x00;
                                txFrame.addr2 = 0x00;
                                txFrame.cmd = bufFrame.cmd;
                                txFrame.datalen = 0;//默认0字节

                                List<ushort> bt = null;
                                bt = TCPServer_DTU.FindOnlineClient();
                                if (bt != null)
                                {
                                    n = (ushort)(bt.Count);
                                    for (int k = 0; k < n; k++)
                                    {
                                        txFrame.databuf[k * 2] = (byte)bt[k];
                                        txFrame.databuf[k * 2 + 1] = (byte)(bt[k] >> 8);
                                    }
                                }
                                txFrame.datalen = (ushort)(n * 2);
                                txFrame.check1 = 0x55;
                                txFrame.check2 = 0xaa;
                                if (TCPServer_PC != null)
                                {
                                    TCPServer_PC.TCPServerSend(txFrame);//数据中心向PC回发查询结果
                                }
                            }
                            #endregion
                            break;
                        case 0xfd:
                            #region 查询指定DTU详情
                            #endregion
                            break;
                        default:
                            if (TCPServer_DTU != null)
                            {
                                TCPServer_DTU.TCPServerSend(bufFrame);//PC向DTU转发
                            }
                            break;
                    }
                }
            }
            #endregion 
        }

        void Event_ClientLinkFuc_DTU(bool updateNow, object lockObj, List<TCPLlientList> tcpclientlist)//TCP_DTU服务器的客户端列表事件响应
        {           
            if (updateNow == true)//立即更新界面
            {
                DataTable OnlineTable_DTU = new DataTable();//在线客户端更新时每次新建一个数据表，然后绑定到表格控件。
                OnlineTable_DTU.Columns.Add("端点", System.Type.GetType("System.String"));
                OnlineTable_DTU.Columns.Add("ID", System.Type.GetType("System.String"));
                OnlineTable_DTU.Columns.Add("片区", System.Type.GetType("System.String"));
                OnlineTable_DTU.Columns.Add("井号", System.Type.GetType("System.String"));
                OnlineTable_DTU.Columns.Add("上线时间", System.Type.GetType("System.String"));
                OnlineTable_DTU.Columns.Add("最后刷新时间", System.Type.GetType("System.String"));
                 lock (lockObj)
                    {
                    OnlineTable_DTU.Rows.Clear();
                    for (int i = 0; i < tcpclientlist.Count; i++)//将在线客户端列表转换成表格
                    {
                        DataRow dr = OnlineTable_DTU.NewRow();
                        dr[0] = tcpclientlist[i].thisClient.Client.RemoteEndPoint.ToString();
                        dr[1] = tcpclientlist[i].ID.ToString();
                        dr[2] = tcpclientlist[i].Area;
                        dr[3] = tcpclientlist[i].WellOrUser;
                        dr[4] = tcpclientlist[i].LoginTime.ToString();
                        dr[5] = tcpclientlist[i].lastTime.ToString();
                        OnlineTable_DTU.Rows.Add(dr);
                    }
                }
                UpdateGridView_ClientLink(dataGridView1, OnlineTable_DTU, lockGridViewDTU);//肯定是要更新界面，但是否更新最后一行按用户要求来办
            }
            else//选择性更新，一般是普通消息如帧接收或帧发送，上线、下线、注册ID、变更片区井号、变更转发端点等重大事件都会用上面的立即更新
            {
                if (checkBox1.Checked == true)//如果用户要求更新最后一行，则更新全部界面包括最后一行
                {
                    DataTable OnlineTable_DTU = new DataTable();//在线客户端更新时每次新建一个数据表，然后绑定到表格控件。                  
                    OnlineTable_DTU.Columns.Add("端点", System.Type.GetType("System.String"));
                    OnlineTable_DTU.Columns.Add("ID", System.Type.GetType("System.String"));
                    OnlineTable_DTU.Columns.Add("片区", System.Type.GetType("System.String"));
                    OnlineTable_DTU.Columns.Add("井号", System.Type.GetType("System.String"));
                    OnlineTable_DTU.Columns.Add("上线时间", System.Type.GetType("System.String"));
                    OnlineTable_DTU.Columns.Add("最后刷新时间", System.Type.GetType("System.String"));
                    lock (lockObj)
                         {
                        OnlineTable_DTU.Rows.Clear();
                        for (int i = 0; i < tcpclientlist.Count; i++)//将在线客户端列表转换成表格
                        {
                            DataRow dr = OnlineTable_DTU.NewRow();
                            dr[0] = tcpclientlist[i].thisClient.Client.RemoteEndPoint.ToString();
                            dr[1] = tcpclientlist[i].ID.ToString();
                            dr[2] = tcpclientlist[i].Area;
                            dr[3] = tcpclientlist[i].WellOrUser;
                            dr[4] = tcpclientlist[i].LoginTime.ToString();
                            dr[5] = tcpclientlist[i].lastTime.ToString();
                            OnlineTable_DTU.Rows.Add(dr);
                        }
                    }
                    UpdateGridView_ClientLink( dataGridView1, OnlineTable_DTU, lockGridViewDTU);
                }
                else//如果用户不要求更新最后一行，则整个界面都不更新，以免界面闪烁
                {

                }
            }
        }

        void Event_ClientLinkFuc_PC(bool updateNow, object lockObj, List<TCPLlientList> tcpclientlist)//TCP_PC服务器的客户端列表事件响应
        {           
            if (updateNow == true)//立即更新界面
            {
                DataTable OnlineTable_PC = new DataTable();//在线客户端更新时每次新建一个数据表，然后绑定到表格控件。             
                OnlineTable_PC.Columns.Add("端点", System.Type.GetType("System.String"));
                OnlineTable_PC.Columns.Add("ID", System.Type.GetType("System.String"));
                OnlineTable_PC.Columns.Add("片区", System.Type.GetType("System.String"));//将来每个片区会分配几个用户名，故PC客户端也有片区一说
                OnlineTable_PC.Columns.Add("用户名", System.Type.GetType("System.String"));//将来PC客户端向数据中心请求ID时要自报帐号和密码，数据中心与数据库上的信息核对，有效帐号才给予分配ID
                OnlineTable_PC.Columns.Add("上线时间", System.Type.GetType("System.String"));
                OnlineTable_PC.Columns.Add("最后刷新时间", System.Type.GetType("System.String"));
                lock (lockObj)
                {
                    OnlineTable_PC.Rows.Clear();
                    for (int i = 0; i < tcpclientlist.Count; i++)//将在线客户端列表转换成表格
                    {
                        DataRow dr = OnlineTable_PC.NewRow();
                        dr[0] = tcpclientlist[i].thisClient.Client.RemoteEndPoint.ToString();
                        dr[1] = tcpclientlist[i].ID.ToString();
                        dr[2] = tcpclientlist[i].Area;
                        dr[3] = tcpclientlist[i].WellOrUser;
                        dr[4] = tcpclientlist[i].LoginTime.ToString();
                        dr[5] = tcpclientlist[i].lastTime.ToString();
                        OnlineTable_PC.Rows.Add(dr);
                    }
                }
                UpdateGridView_ClientLink(dataGridView2, OnlineTable_PC, lockGridViewPC);//肯定是要更新界面，但是否更新最后一行按用户要求来办
            }
            else//选择性更新，一般是普通消息如帧接收或帧发送，上线、下线、注册ID、变更片区井号、变更转发端点等重大事件都会用上面的立即更新
            {
                if (checkBox2.Checked == true)//如果用户要求更新最后一行，则更新全部界面包括最后一行
                {
                    DataTable OnlineTable_PC = new DataTable();//在线客户端更新时每次新建一个数据表，然后绑定到表格控件。
                    OnlineTable_PC.Columns.Add("端点", System.Type.GetType("System.String"));
                    OnlineTable_PC.Columns.Add("ID", System.Type.GetType("System.String"));
                    OnlineTable_PC.Columns.Add("片区", System.Type.GetType("System.String"));//将来每个片区会分配几个用户名，故PC客户端也有片区一说
                    OnlineTable_PC.Columns.Add("用户名", System.Type.GetType("System.String"));//将来PC客户端向数据中心请求ID时要自报帐号和密码，数据中心与数据库上的信息核对，有效帐号才给予分配ID
                    OnlineTable_PC.Columns.Add("上线时间", System.Type.GetType("System.String"));
                    OnlineTable_PC.Columns.Add("最后刷新时间", System.Type.GetType("System.String"));
                    lock (lockObj)
                    {
                        OnlineTable_PC.Rows.Clear();
                        for (int i = 0; i < tcpclientlist.Count; i++)//将在线客户端列表转换成表格
                        {
                            DataRow dr = OnlineTable_PC.NewRow();
                            dr[0] = tcpclientlist[i].thisClient.Client.RemoteEndPoint.ToString();
                            dr[1] = tcpclientlist[i].ID.ToString();
                            dr[2] = tcpclientlist[i].Area;
                            dr[3] = tcpclientlist[i].WellOrUser;
                            dr[4] = tcpclientlist[i].LoginTime.ToString();
                            dr[5] = tcpclientlist[i].lastTime.ToString();
                            OnlineTable_PC.Rows.Add(dr);
                        }
                    }
                    UpdateGridView_ClientLink(dataGridView2, OnlineTable_PC, lockGridViewPC);
                }
                else//如果用户不要求更新最后一行，则整个界面都不更新，以免界面闪烁
                {

                }
            }
        }
        #endregion

        #region 测试相关数据库是否可用
        private bool TestDataBase1()//测试采样备份数据库是否可用
        {
            return true;
        }

        private bool TestDataBase2()//测试仪器信息数据库是否可用
        {
            return true;
        }

        private bool TestDataBase3()//测试DTU日志数据库是否可用
        {
            return true;
        }

        private bool TestDataBase4()//测试PC日志数据库是否可用
        {
            return true;
        }
        #endregion

        #region TCP服务器启动与停止


        private void button1_Click(object sender, EventArgs e)
        {
            if(comboBox1.Text=="")
            {
                MessageBox.Show("请选择有效IP。");
                return;
            }

            UseDB_DTU = checkBox3.Checked;
            if (UseDB_DTU == true)
            {
                if (TestDataBase1() == false)
                {
                    MessageBox.Show("数据备份数据库不可用，TCP_DTU服务启动失败！");
                    return;
                }
                if (TestDataBase2() == false)
                {
                    MessageBox.Show("仪器信息数据库不可用，TCP_DTU服务启动失败！");
                    return;
                }
                if (TestDataBase3() == false)
                {
                    MessageBox.Show("日志记录_DTU数据库不可用，TCP_DTU服务启动失败！");
                    return;
                }
            }

            try
            {
                IPAddress MyIPAddress = IPAddress.Parse(comboBox1.Text);
                int port = int.Parse(comboBox3.Text);

                dataGridView3.Columns.Clear();
                dataGridView3.Columns.Add("时间", "时间");
                dataGridView3.Columns.Add("端点", "端点");
                dataGridView3.Columns.Add("ID", "ID");
                dataGridView3.Columns.Add("片区", "片区");
                dataGridView3.Columns.Add("井号", "井号");
                dataGridView3.Columns.Add("消息", "消息");
                dataGridView3.Columns.Add("数据", "数据");

                
                TCPServer_DTU = new TCPServerClass(UseDB_DTU,true,MyIPAddress, port, int.Parse(textBox1.Text), int.Parse(textBox2.Text),int.Parse(textBox7.Text), Event_ClientLinkFuc_DTU, Event_LogArrived_DTU);//启动GPRS模块连接服务器，帧字节超时1000ms，客户端掉线超时3分钟
                button1.Enabled = false;
                textBox1.Enabled = false;
                textBox2.Enabled = false;
                textBox7.Enabled = false;
                comboBox3.Enabled = false;
                button3.Enabled = true; 
            }
            catch(Exception ex)
            {
                MessageBox.Show("发生意外异常，TCP_DTU服务启动失败！");
            }
               
        }

        private void button2_Click(object sender, EventArgs e)
        {
            if (comboBox13.Text == "")
            {
                MessageBox.Show("请选择有效IP。");
                return;
            }

            UseDB_PC = checkBox3.Checked;
            if (UseDB_PC == true)
            {
                if (TestDataBase4() == false)
                {
                    MessageBox.Show("日志记录_PC数据库不可用，TCP_PC服务启动失败！");
                    return;
                }
            }

            try
            {
                IPAddress MyIPAddress = IPAddress.Parse(comboBox13.Text);
                int port = int.Parse(comboBox4.Text);

                dataGridView4.Columns.Clear();
                dataGridView4.Columns.Add("时间", "时间");
                dataGridView4.Columns.Add("端点", "端点");
                dataGridView4.Columns.Add("ID", "ID");
                dataGridView4.Columns.Add("片区", "片区");
                dataGridView4.Columns.Add("用户名", "用户名");
                dataGridView4.Columns.Add("消息", "消息");
                dataGridView4.Columns.Add("数据", "数据");

                TCPServer_PC = new TCPServerClass(UseDB_PC,false, MyIPAddress, port, int.Parse(textBox3.Text), int.Parse(textBox4.Text), int.Parse(textBox9.Text), Event_ClientLinkFuc_PC, Event_LogArrived_PC);//启动PC连接服务器，帧字节超时1000ms，客户端掉线超时3分钟
                button2.Enabled = false;
                textBox3.Enabled = false;
                textBox4.Enabled = false;
                textBox9.Enabled = false;
                comboBox4.Enabled = false;
                button4.Enabled = true;
    
            }
            catch(Exception ex)
            {
                MessageBox.Show("发生意外异常，TCP_PC服务启动失败！");
            }
        }

        private void button3_Click(object sender, EventArgs e)
        {
            if(TCPServer_DTU!=null)
            {  
                TCPServer_DTU.TcpServerStop();
                button1.Enabled = true;
                textBox1.Enabled = true;
                textBox2.Enabled = true;
                textBox7.Enabled = true;
                button3.Enabled = false;
               
            }
        }

        private void button4_Click(object sender, EventArgs e)
        {
            if (TCPServer_PC != null)
            {
                TCPServer_PC.TcpServerStop();
                button2.Enabled = true;
                textBox3.Enabled = true;
                textBox4.Enabled = true;
                textBox9.Enabled = true;
                button4.Enabled = false;

            }
        }
        #endregion

        #region 这几个事件要响应，但可不写任何代码，否则DataGridView控件有时会出错
        private void dataGridView1_DataError(object sender, DataGridViewDataErrorEventArgs e)
        {

        }

        private void dataGridView3_DataError(object sender, DataGridViewDataErrorEventArgs e)
        {

        }

        private void dataGridView2_DataError(object sender, DataGridViewDataErrorEventArgs e)
        {

        }

        private void dataGridView4_DataError(object sender, DataGridViewDataErrorEventArgs e)
        {

        }
        #endregion

        #region 查找本机空闲端口

        /// <summary> 
        /// 检查指定端口是否已用
        /// </summary> 
        /// <param name="port"></param> 
        /// <returns></returns> 
        public bool PortIsAvailable(int port)
        {
            bool isAvailable = true;

            IList portUsed = PortIsUsed();

            foreach (int p in portUsed)
            {
                if (p == port)
                {
                    isAvailable = false; break;
                }
            }

            return isAvailable;
        }

        /// <summary> 
        /// 获取第一个可用的端口号 
        /// </summary> 
        /// <returns></returns> 
        private int GetFirstAvailablePort()
        {
            int MAX_PORT = 65535; //系统tcp/udp端口数最大是65535 
            int BEGIN_PORT = 5000;//从这个端口开始检测 

            for (int i = BEGIN_PORT; i < MAX_PORT; i++)
            {
                if (PortIsAvailable(i)) return i;
            }

            return -1;
        }

        /// <summary> 
        /// 获取操作系统已用的端口号 
        /// </summary> 
        /// <returns></returns> 
        private IList PortIsUsed()
        {
            //获取本地计算机的网络连接和通信统计数据的信息 
            IPGlobalProperties ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();

            //返回本地计算机上的所有Tcp监听程序 
            IPEndPoint[] ipsTCP = ipGlobalProperties.GetActiveTcpListeners();

            //返回本地计算机上的所有UDP监听程序 
            IPEndPoint[] ipsUDP = ipGlobalProperties.GetActiveUdpListeners();

            //返回本地计算机上的Internet协议版本4(IPV4 传输控制协议(TCP)连接的信息。 
            TcpConnectionInformation[] tcpConnInfoArray = ipGlobalProperties.GetActiveTcpConnections();

            IList allPorts = new ArrayList();
            foreach (IPEndPoint ep in ipsTCP) allPorts.Add(ep.Port);
            foreach (IPEndPoint ep in ipsUDP) allPorts.Add(ep.Port);
            foreach (TcpConnectionInformation conn in tcpConnInfoArray) allPorts.Add(conn.LocalEndPoint.Port);

            return allPorts;
        }

        private void comboBox3_DropDown(object sender, EventArgs e)
        {
            comboBox3.Items.Clear();
            for (int i=9000;i<9050;i++)
            {
                if(PortIsAvailable(i)==true)
                {
                    comboBox3.Items.Add(i);
                }
            }
        }

        private void comboBox4_MouseDown(object sender, MouseEventArgs e)
        {
            comboBox4.Items.Clear();
            for (int i = 9000; i < 9050; i++)
            {
                if (PortIsAvailable(i) == true)
                {
                    comboBox4.Items.Add(i);
                }
            }
        }

        #endregion

        #region 数据库操作
        //数据库IP
        private void comboBox2_DropDown(object sender, EventArgs e)
        {
            comboBox2.Items.Clear();
            comboBox2.Items.Add("112.74.89.168,50000");
            IPAddress MyIPAddress = IPAddress.Parse("0.0.0.0");
            //获取本机可用IP地址
            IPAddress[] ips = Dns.GetHostAddresses(Dns.GetHostName());
            foreach (IPAddress ipa in ips)
            {
                if (ipa.AddressFamily == AddressFamily.InterNetwork)
                {
                    comboBox2.Items.Add(ipa.ToString());
                }
            }
        }

        //数据库登录
        private void button5_Click(object sender, EventArgs e)
        {
            //登录成功后至少打开5组SqlConnection和DbCommand，分别用于全部数据、单井信息、日志记录DTU、日志记录PC和井号数据这五个数据库的独立操作
            //前四个数据是专用的，下拉列表框中名称有过滤，只显示指定的某一个数据集
            //其它数据集不包括前面四个数据集
            //每个数据集都有独立锁，不同线程操作时不会冲突
            //根据仪器信息还需要打开多组SqlConnection和DbCommand，每个片区打开一组
            if (((Button)sender).Text == "登录数据库")
            {
                try
                {
                    string SQLConnectionStr = "Data Source=" + comboBox2.Text + "," + textBox10.Text + ";" + "User ID=" + textBox5.Text + ";" + "Password=" + textBox6.Text + ";";

                    SqlConnection_Log_DTU = new SqlConnection(SQLConnectionStr);
                    if (SqlConnection_Log_DTU != null)
                    {
                        SqlConnection_Log_DTU.Open();
                        DbCommand_Log_DTU = SqlConnection_Log_DTU.CreateCommand();
                    }

                    SqlConnection_Log_PC = new SqlConnection(SQLConnectionStr);
                    if (SqlConnection_Log_PC != null)
                    {
                        SqlConnection_Log_PC.Open();
                        DbCommand_Log_PC = SqlConnection_Log_PC.CreateCommand();
                    }

                    SqlConnection_AllData = new SqlConnection(SQLConnectionStr);
                    if (SqlConnection_AllData != null)
                    {
                        SqlConnection_AllData.Open();
                        DbCommand_AllData= SqlConnection_AllData.CreateCommand();
                    }

                    SqlConnection_WellInfo = new SqlConnection(SQLConnectionStr);
                    if (SqlConnection_WellInfo != null)
                    {
                        SqlConnection_WellInfo.Open();
                        DbCommand_WellInfo = SqlConnection_WellInfo.CreateCommand();
                    }

                    SqlConnection_WellData = new SqlConnection(SQLConnectionStr);
                    if (SqlConnection_WellData != null)
                    {
                        SqlConnection_WellData.Open();
                        DbCommand_WellData = SqlConnection_WellData.CreateCommand();
                    }

                    ((Button)sender).Text = "断开数据库";
                    tableLayoutPanel4.Enabled = true;
                    comboBox2.Enabled = false;
                    textBox5.Enabled = false;
                    textBox6.Enabled = false;
                    textBox10.Enabled = false;
                    checkBox3.Enabled = false;
                    MessageBox.Show("数据库登录成功。");
                }
                catch (Exception ea)
                {
                    MessageBox.Show("数据库登录失败！" + ea.ToString());
                }
            }
            else
            {
                if(button1.Enabled==false)
                {
                    MessageBox.Show("请先停止TCP_DTU服务器。");
                    return;
                }

                if (button2.Enabled == false)
                {
                    MessageBox.Show("请先停止TCP_PC服务器。");
                    return;
                }

                try
                {
                    if (SqlConnection_Log_DTU.State == ConnectionState.Open)
                    {
                        SqlConnection_Log_DTU.Close();
                    }

                    if (SqlConnection_Log_PC.State == ConnectionState.Open)
                    {
                        SqlConnection_Log_PC.Close();
                    }

                    if (SqlConnection_AllData.State == ConnectionState.Open)
                    {
                        SqlConnection_AllData.Close();
                    }

                    if (SqlConnection_WellInfo.State == ConnectionState.Open)
                    {
                        SqlConnection_WellInfo.Close();
                    }

                    if (SqlConnection_WellData.State == ConnectionState.Open)
                    {
                        SqlConnection_WellData.Close();
                    }

                    ((Button)sender).Text = "登录数据库";
                    tableLayoutPanel4.Enabled = false;
                    comboBox2.Enabled = true;
                    textBox5.Enabled = true;
                    textBox6.Enabled = true;
                    textBox10.Enabled = true;
                    checkBox3.Enabled = true;
                    MessageBox.Show("数据库断开成功。");
                }
                catch (Exception ea)
                {
                    MessageBox.Show("数据库断开失败！" + ea.ToString());
                }
            }

        }

        //是否使用数据库
        private void checkBox3_CheckedChanged(object sender, EventArgs e)
        {
            button5.Enabled = ((CheckBox)(sender)).Checked;

        }

        //判断某数据连接是否在线
        private bool SQLServerIsConnected(SqlConnection sc, DbCommand dc)
        {
            if (sc == null)
            {
                return false;
            }

            if (sc.State != ConnectionState.Open)
            {
                return false;
            }

            if (dc == null)
            {
                return false;
            }

            return true;
        }

        //枚举数据库
        private bool EnumDataBase(SqlConnection sc, DbCommand dc,object cb)
        {
            if (SQLServerIsConnected(sc,dc) == false)
            {
                return false;
            }

            ((ComboBox)cb).Items.Clear();

            try
            {
                dc.CommandText = "select [name] from [sysdatabases] order by [name]";
                dc.CommandType = CommandType.Text;
                SqlDataReader thisSqlDataReader;
                thisSqlDataReader = (SqlDataReader)dc.ExecuteReader();
                while (true)
                {
                    if (thisSqlDataReader.Read() == true)
                    {
                        // if (thisSqlDataReader["name"].ToString() == "测试片区")
                        {
                            ((ComboBox)cb).Items.Add(thisSqlDataReader["name"].ToString());
                        }
                    }
                    else
                    {
                        break;
                    }
                }
                thisSqlDataReader.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
                return false;
            }
            return true;
        }

        //使用数据库
        private bool UseDataBase(SqlConnection sc, DbCommand dc, object cb, ref string db)
        {
            if (SQLServerIsConnected(sc, dc) == false)
            {
                return false;
            }

            db = ((ComboBox)cb).Text;

            try
            {
                if (((ComboBox)cb).Text != string.Empty)
                {
                    lock (lockDB)
                    {
                        dc.CommandText = "use [" + db + "]";
                        dc.CommandType = CommandType.Text;
                        dc.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
                return false;
            }
            return true;
        }

        //枚举数据表
        private bool EnumDataTable(SqlConnection sc, DbCommand dc, object cb)
        {
            if (SQLServerIsConnected(sc, dc) == false)
            {
                return false;
            }

            try
            {
                //sysobjects为刚才所打开的数据集
                dc.CommandText = "select * from [sysobjects] where [type] = 'u' order by [name]";
                dc.CommandType = CommandType.Text;
                SqlDataReader thisSqlDataReader;
                thisSqlDataReader = (SqlDataReader)dc.ExecuteReader();
                ((ComboBox)cb).Items.Clear();
                ((ComboBox)cb).Text = string.Empty;
                while (true)
                {
                    if (thisSqlDataReader.Read() == true)
                    {
                        ((ComboBox)cb).Items.Add(thisSqlDataReader["name"].ToString());
                    }
                    else
                    {
                        break;
                    }
                }
                thisSqlDataReader.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
                return false;

            }
            return true;
        }

        //选择数据表
        private bool SelectDataTable(SqlConnection sc, DbCommand dc, object cb, ref string dt)
        {
            if (SQLServerIsConnected(sc, dc) == false)
            {
                return false;
            }
            dt = ((ComboBox)cb).SelectedItem.ToString();
            return true;
        }

        //查看数据记录
        private int ViewDataRecord(SqlConnection sc, DbCommand dc, string db, string dt)
        {
            if (SQLServerIsConnected(sc, dc) == false)
            {
                return 0;
            }

            int n = 0;
            try
            {
                DataSet thisDataSet;
                thisDataSet = new DataSet();
                DataAdapter thisDataAdapter;

                var SQLString = "select * from [" + dt + "]";
                thisDataAdapter = new SqlDataAdapter(SQLString, sc);

                n=thisDataAdapter.Fill(thisDataSet);

                if (thisDataSet.Tables.Count > 0)
                {
                    Server_dataGridView.DataSource = thisDataSet.Tables[0];
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
                return 0;
            }
            return n;
        }
 

        #region 全部数据 
        private void comboBox12_DropDown(object sender, EventArgs e)
        {
            EnumDataBase(SqlConnection_AllData, DbCommand_AllData, sender);
        }

        private void comboBox12_SelectedValueChanged_1(object sender, EventArgs e)
        {
            UseDataBase(SqlConnection_AllData, DbCommand_AllData, sender, ref DataBase_AllData);
        }

        private void comboBox11_DropDown(object sender, EventArgs e)
        {
            EnumDataTable(SqlConnection_AllData, DbCommand_AllData, sender);
        }

        private void comboBox11_SelectedValueChanged_1(object sender, EventArgs e)
        {
            SelectDataTable(SqlConnection_AllData, DbCommand_AllData, sender, ref DataTable_AllData);
        }

        private void button8_Click(object sender, EventArgs e)//查找数据表中的记录
        {
            ViewDataRecord(SqlConnection_AllData, DbCommand_AllData, DataBase_AllData, DataTable_AllData);
        }

        private bool SQLSaveAllData(DateTime dt,Int16[] p,Int16[] t,Int16[] f,byte[] k,byte[] u, DateTime dts,  Int16 id, string area = "", string well = "")
        {
            if (SQLServerIsConnected(SqlConnection_AllData, DbCommand_AllData) == false)
            {
                return false;
            }

            if (DataBase_AllData != string.Empty)
            {
                DbCommand_AllData.CommandText = "use [" + DataBase_AllData + "]";
                DbCommand_AllData.CommandType = CommandType.Text;
                DbCommand_AllData.ExecuteNonQuery();
            }
            else
            {
                return false;
            }

            if (DataTable_AllData == string.Empty)
            {

                return false;
            }

            try
            {

                //DataTable名称加[]是为了防止名称中有非法字符，这样搞的话可以用文件名中的非法字符给数据表起名
                //增加的值如果是数字可直接写，是日期或字符串的话要加单引号' '
                int i = 0;
                lock (lockDB)
                {
                   // var strSQL1 = "insert into[" + DataTable_Log_PC + "](时间,压力1,温度1,流量1,阀门1,电压1,压力2,温度2,流量2,阀门2,电压2,压力3,温度3,流量3,阀门3,电压3,压力4,温度4,流量4,阀门4,电压4,压力5,温度5,流量5,阀门5,电压5,压力6,温度6,流量6,阀门6,电压6,压力7,温度7,流量7,阀门7,电压7,服务器时间,通讯模块ID,片区,井号) values('" + dt.ToString() + "'," p[0],"'" + dts.ToString() + "'," + id +"'" + area + "','" + well  + "')";
                   // var thisCommand = new SqlCommand(strSQL1, SqlConnection_AllData);
                   // i = thisCommand.ExecuteNonQuery();
                }
                if (i < 1)
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                // MessageBox.Show(ex.Message);
                return false;
            }

            return true;
        }
        #endregion

        #region 单井信息
        private void comboBox6_DropDown(object sender, EventArgs e)
        {
            EnumDataBase(SqlConnection_WellInfo, DbCommand_WellInfo, sender);
        }

        private void comboBox6_SelectedValueChanged_1(object sender, EventArgs e)
        {
            UseDataBase(SqlConnection_WellInfo, DbCommand_WellInfo, sender, ref DataBase_WellInfo);
        }

        private void comboBox5_DropDown(object sender, EventArgs e)
        {
            EnumDataTable(SqlConnection_WellInfo, DbCommand_WellInfo, sender);
        }

        private void comboBox5_SelectedValueChanged_1(object sender, EventArgs e)
        {
            SelectDataTable(SqlConnection_WellInfo, DbCommand_WellInfo, sender, ref DataTable_WellInfo);
        }

        private void button9_Click(object sender, EventArgs e)//查找数据表中的记录
        {
            ViewDataRecord(SqlConnection_WellInfo, DbCommand_WellInfo, DataBase_WellInfo, DataTable_WellInfo);
        }
        #endregion

        #region 日志记录DTU 
        private void comboBox8_DropDown(object sender, EventArgs e)//枚举数据库
        {
            EnumDataBase(SqlConnection_Log_DTU, DbCommand_Log_DTU, sender);
        }

        private void comboBox8_SelectedValueChanged(object sender, EventArgs e)//使用数据库
        {
            UseDataBase(SqlConnection_Log_DTU, DbCommand_Log_DTU,sender, ref DataBase_Log_DTU);
        }

        private void comboBox7_DropDown(object sender, EventArgs e)//枚举数据表
        {
            EnumDataTable(SqlConnection_Log_DTU, DbCommand_Log_DTU, sender);
        }

        private void comboBox7_SelectedValueChanged(object sender, EventArgs e)//选择数据表
        {
            SelectDataTable(SqlConnection_Log_DTU, DbCommand_Log_DTU, sender, ref DataTable_Log_DTU);
        }

        private void button10_Click(object sender, EventArgs e)//查看数据记录
        {
            ViewDataRecord(SqlConnection_Log_DTU, DbCommand_Log_DTU, DataBase_Log_DTU, DataTable_Log_DTU);
        }

        private bool SQLSaveLog_DTU(string datetime, string ep="", string id="",  string area="" , string well = "", string mes="", string data = "")
        {
            if (SQLServerIsConnected(SqlConnection_Log_DTU, DbCommand_Log_DTU) == false)
            {
                return false;
            }

            if (DataBase_Log_DTU != string.Empty) 
            {
                DbCommand_Log_DTU.CommandText = "use [" + DataBase_Log_DTU + "]";
                DbCommand_Log_DTU.CommandType = CommandType.Text;
                DbCommand_Log_DTU.ExecuteNonQuery();
            }
            else
            {
                return false;
            }

            if (DataTable_Log_DTU == string.Empty)
            {

                return false;
            }

            try
            {

                //DataTable名称加[]是为了防止名称中有非法字符，这样搞的话可以用文件名中的非法字符给数据表起名
                //增加的值如果是数字可直接写，是日期或字符串的话要加单引号' '
                int i = 0;
                lock (lockDB)
                {
                    var strSQL1 = "insert into[" + DataTable_Log_DTU + "](时间,端点,ID,片区,井号,消息,数据) values('" + datetime + "','" + ep + "','" + id + "','" + area + "','" + well + "','" + mes + "','" + data + "')";
                    var thisCommand = new SqlCommand(strSQL1, SqlConnection_Log_DTU);
                    i = thisCommand.ExecuteNonQuery();
                }
                if(i<1)
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                // MessageBox.Show(ex.Message);
                return false;
            }

            return true;
        }
        #endregion

        #region 日志记录PC
        private void comboBox10_DropDown(object sender, EventArgs e)//枚举数据库
        {
            EnumDataBase(SqlConnection_Log_PC, DbCommand_Log_PC, sender);
        }

        private void comboBox10_SelectedValueChanged(object sender, EventArgs e)//使用数据库
        {
            UseDataBase(SqlConnection_Log_PC, DbCommand_Log_PC, sender, ref DataBase_Log_PC);
        }

        private void comboBox9_DropDown(object sender, EventArgs e)//枚举数据表
        {
            EnumDataTable(SqlConnection_Log_PC, DbCommand_Log_PC, sender);
        }

        private void comboBox9_SelectedValueChanged(object sender, EventArgs e)//选择数据表
        {
            SelectDataTable(SqlConnection_Log_PC, DbCommand_Log_PC, sender, ref DataTable_Log_PC);
        }

        private void button11_Click(object sender, EventArgs e)//查找数据表中的记录
        {
            ViewDataRecord(SqlConnection_Log_PC, DbCommand_Log_PC, DataBase_Log_PC, DataTable_Log_PC);
        }

        private bool SQLSaveLog_PC(string datetime, string ep = "", string id = "", string area = "", string user = "", string mes = "", string data = "")
        {
            if (SQLServerIsConnected(SqlConnection_Log_PC, DbCommand_Log_PC) == false)
            {
                return false;
            }

            if (DataBase_Log_PC != string.Empty)
            {
                DbCommand_Log_PC.CommandText = "use [" + DataBase_Log_PC + "]";
                DbCommand_Log_PC.CommandType = CommandType.Text;
                DbCommand_Log_PC.ExecuteNonQuery();
            }
            else
            {
                return false;
            }

            if (DataTable_Log_PC == string.Empty)
            {

                return false;
            }

            try
            {

                //DataTable名称加[]是为了防止名称中有非法字符，这样搞的话可以用文件名中的非法字符给数据表起名
                //增加的值如果是数字可直接写，是日期或字符串的话要加单引号' '
                int i = 0;
                lock (lockDB)
                {
                    var strSQL1 = "insert into[" + DataTable_Log_PC + "](时间,端点,ID,片区,用户名,消息,数据) values('" + datetime + "','" + ep + "','" + id + "','" + area + "','" + user + "','" + mes + "','" + data + "')";
                    var thisCommand = new SqlCommand(strSQL1, SqlConnection_Log_PC);
                    i = thisCommand.ExecuteNonQuery();
                }
                if (i < 1)
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                // MessageBox.Show(ex.Message);
                return false;
            }

            return true;
        }
        #endregion

        #region 井号数据
        private void comboBox15_DropDown(object sender, EventArgs e)
        {
            EnumDataBase(SqlConnection_WellData, DbCommand_WellData, sender);
        }

        private void comboBox15_SelectedValueChanged(object sender, EventArgs e)
        {
            UseDataBase(SqlConnection_WellData, DbCommand_WellData, sender, ref DataBase_WellData);
        }

        private void comboBox14_DropDown(object sender, EventArgs e)
        {
            EnumDataTable(SqlConnection_WellData, DbCommand_WellData, sender);
        }

        private void comboBox14_SelectedIndexChanged(object sender, EventArgs e)
        {
            SelectDataTable(SqlConnection_WellData, DbCommand_WellData, sender, ref DataTable_WellData);
        }

        private void button6_Click(object sender, EventArgs e)
        {
            ViewDataRecord(SqlConnection_WellData, DbCommand_WellData, DataBase_WellData, DataTable_WellData);
        }
        #endregion
        #endregion

        #region 软件最小化时隐藏到右下角
        private void MainForm_SizeChanged(object sender, EventArgs e)
        {
            if (this.WindowState == FormWindowState.Minimized)
            {
                this.Visible = false;//隐藏窗体
                this.NotifyIcon1.Visible = true;//显示托盘图标
                this.NotifyIcon1.Text = this.Text;//设置托盘文本                
            }
        }


        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {// 关闭时暂不处理，直接关掉退出，以后可在状态图标里加上右键退出
           // 添加托盘图标的右键菜单
           // （具体代码略）
           //     "退出"菜单：Application.Exit();
           // "显示窗口"菜单：参考STEP4
           //PS: 解决了设置“关闭时隐藏到系统托盘”时，
           // 点击“退出”菜单也无法退出的问题。
           // ——无论是用this.Close(); 还是Application.Exit(); 都无法退出！
           // ——解决办法是，在关闭窗口事件中判断关闭原因 / 
           //     来源（e.CloseReason），若为CloseReason.UserClosing则为点击了窗口右上角的关闭按钮，
           // 否则可能是点击了"退出"菜单，则不执行隐藏到托

            //注意判断关闭事件Reason来源于窗体按钮，否则用菜单退出时无法退出!
            //if (e.CloseReason == CloseReason.UserClosing)
            //{
            //    e.Cancel = true;    //取消"关闭窗口"事件
            //    this.WindowState = FormWindowState.Minimized;    //使关闭时窗口向右下角缩小的效果
            //    NotifyIcon1.Visible = true;
            //    this.Hide();
            //    return;
            //}
        }

        private void NotifyIcon1_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            NotifyIcon1.Visible = false;
            this.Show();
            WindowState = FormWindowState.Normal;
            this.Focus();
        }
        #endregion
    }
}
