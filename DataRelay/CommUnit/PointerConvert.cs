using System;
using System.Runtime.InteropServices;

namespace NewFilterBoard.CommUnit
{
    /// <summary>
    /// PointerConvert 的摘要说明。
    /// 指针转换类
    /// 通过指针的方式更改数据类型
    /// 支持:byte <-> int/float/double
    ///      string 类型可以通过
    ///      System.Text.Encoding进行编码
    /// 用途:数据传输
    ///
    /// 作者:随飞
    /// http://www.cnblogs.com/chinasf
    /// mailluck@Gmail.com
    /// 最后更新日期:2005.5.27
    /// </summary>
    public unsafe class PointerConvert
    {
        public PointerConvert() { ;}

        /// <summary>
        /// 转换Int数据到数组
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public static byte[] ToByte(int data)
        {
            unsafe
            {
                byte* pdata = (byte*)&data;
                byte[] byteArray = new byte[sizeof(int)];
                for (int i = 0; i < sizeof(int); ++i)
                    byteArray[i] = *pdata++;
                return byteArray;
            }
        }
        /// <summary>
        /// 转换UInt数据到数组
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public static byte[] ToByte(uint data)
        {
            unsafe
            {
                byte* pdata = (byte*)&data;
                byte[] byteArray = new byte[sizeof(uint)];
                for (int i = 0; i < sizeof(uint); ++i)
                    byteArray[i] = *pdata++;
                return byteArray;
            }
        }
        /// <summary>
        /// 转换float数据到数组
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public static byte[] ToByte(float data)
        {
            unsafe
            {
                byte* pdata = (byte*)&data;
                byte[] byteArray = new byte[sizeof(float)];
                for (int i = 0; i < sizeof(float); ++i)
                    byteArray[i] = *pdata++;
                return byteArray;
            }
        }

        /// <summary>
        /// 转换double数据到数组
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public static byte[] ToByte(double data)
        {
            unsafe
            {
                byte* pdata = (byte*)&data;
                byte[] byteArray = new byte[sizeof(double)];
                for (int i = 0; i < sizeof(double); ++i)
                    byteArray[i] = *pdata++;
                return byteArray;
            }
        }

        public static void ToByte(double data, ref byte[] buffer, int offset)
        {
            unsafe
            {
                byte* pdata = (byte*)&data;
                byte[] byteArray = new byte[sizeof(double)];
                for (int i = 0; i < sizeof(double); ++i)
                    buffer[i + offset] = *pdata++;
            }
        }
        public static void ToByte(uint data, ref byte[] buffer, int offset)
        {
            unsafe
            {
                byte* pdata = (byte*)&data;
                byte[] byteArray = new byte[sizeof(uint)];
                for (int i = 0; i < sizeof(uint); ++i)
                    buffer[i + offset] = *pdata++;
            }
        }
        /// <summary>
        /// 转换数组为整形
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public static int ToInt(byte[] data)
        {
            unsafe
            {
                int n = 0;
                fixed (byte* p = data)
                {
                    n = Marshal.ReadInt32((IntPtr)p);
                }
                return n;
            }
        }
        static byte[] b4 = new byte[4];
        public static int Bit20ToInt32(byte b1, byte b2, byte b3)
        {
            b4[0] = b1;
            b4[1] = b2;
            b4[2] = b3;
            if ((b4[2] & 0x8) > 0)
            {
                b4[2] |= 0xf0;
                b4[3] = 0xff;
            }
            else
            {
                b4[2] &= 0x0f;
                b4[3] = 0;
            }
            unsafe
            {
                int n = 0;
                fixed (byte* p = b4)
                {
                    n = Marshal.ReadInt32((IntPtr)p);
                }
                return n;
            }
        }
        public static int Bit12ToInt32(byte b1, byte b2)
        {
            b4[0] = b1;
            b4[1] = b2;
            if ((b4[1] & 0x8) > 0)
            {
                b4[1] |= 0xf0;
                b4[2] = 0xff;
                b4[3] = 0xff;
            }
            else
            {
                b4[1] &= 0x0f;
                b4[2] = 0;
                b4[3] = 0;
            }
            unsafe
            {
                int n = 0;
                fixed (byte* p = b4)
                {
                    n = Marshal.ReadInt32((IntPtr)p);
                }
                return n;
            }
        }
        public static uint ToUint(byte[] data, int pos)
        {
            unsafe
            {
                int n = 0;
                fixed (byte* p = &(data[pos]))
                {
                    n = Marshal.ReadInt32((IntPtr)p);
                }
                return (uint)n;
            }
        }
        /// <summary>
        /// 转换数组为float
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public static float ToFloat(byte[] data)
        {
            float a = 0;
            byte i;

            byte[] x = data;
            void* pf;
            fixed (byte* px = x)
            {
                pf = &a;
                for (i = 0; i < data.Length; i++)
                {
                    *((byte*)pf + i) = *(px + i);
                }
            }

            return a;
        }
        /// <summary>
        /// 转换数组为float
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public static float ToFloat(byte[] data,int offset)
        {
            float a = 0;
            int i;

            byte[] x = data;
            void* pf;
            fixed (byte* px = x)
            {
                pf = &a;
                for (i = 0; i < sizeof(float); i++)
                {
                    *((byte*)pf + i) = *(px + offset + i);
                }
            }

            return a;
        }

        /// <summary>
        /// 转换数组为Double
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public static double ToDouble(byte[] data)
        {
            double a = 0;
            byte i;

            byte[] x = data;
            void* pf;
            fixed (byte* px = x)
            {
                pf = &a;
                for (i = 0; i < data.Length; i++)
                {
                    *((byte*)pf + i) = *(px + i);
                }
            }
            return a;
        }
        public static double ToDouble(byte[] data, int p)
        {
            double a = 0;
            byte i;

            byte[] x = data;
            void* pf;
            fixed (byte* px = x)
            {
                pf = &a;
                for (i = 0; i < sizeof(double); i++)
                {
                    *((byte*)pf + i) = *(px + i + p);
                }
            }
            return a;
        }




        public static int ToInt(byte[] data, int start)
        {
            unsafe
            {
                int n = 0;
                fixed (byte* p = &(data[start]))
                {
                    n = Marshal.ReadInt32((IntPtr)p);
                }
                return n;
            }
        }
        public static Int16 ToInt16(byte[] data, int start)
        {
            unsafe
            {
                Int16 n = 0;
                fixed (byte* p = &(data[start]))
                {
                    n = Marshal.ReadInt16((IntPtr)p);
                }
                return n;
            }
        }

        public static UInt16 ToUInt16(byte[] data, int start)
        {
            unsafe
            {
                Int16 n = 0;
                fixed (byte* p = &(data[start]))
                {
                    n = Marshal.ReadInt16((IntPtr)p);
                }
                return (UInt16)n;
            }
        }

    }
}
