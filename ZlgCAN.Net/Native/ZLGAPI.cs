// update time 2025/7/16

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace ZlgCAN.Net.Native
{

    public class ZLGCAN
    {

        #region 设备类型
        public static UInt32 ZCAN_PCI9810 = 2;
        public static UInt32 ZCAN_USBCAN1 = 3;
        public static UInt32 ZCAN_USBCAN2 = 4;
        public static UInt32 ZCAN_PCI9820 = 5;
        public static UInt32 ZCAN_CANETUDP = 12;
        public static UInt32 ZCAN_PCI9840 = 14;
        public static UInt32 ZCAN_PCI9820I = 16;
        public static UInt32 ZCAN_CANETTCP = 17;
        public static UInt32 ZCAN_PCI5010U = 19;
        public static UInt32 ZCAN_USBCAN_E_U = 20;
        public static UInt32 ZCAN_USBCAN_2E_U = 21;
        public static UInt32 ZCAN_PCI5020U = 22;
        public static UInt32 ZCAN_PCIE9221 = 24;
        public static UInt32 ZCAN_WIFICAN_TCP = 25;
        public static UInt32 ZCAN_WIFICAN_UDP = 26;
        public static UInt32 ZCAN_PCIe9120 = 27;
        public static UInt32 ZCAN_PCIe9110 = 28;
        public static UInt32 ZCAN_PCIe9140 = 29;
        public static UInt32 ZCAN_USBCAN_4E_U = 31;
        public static UInt32 ZCAN_CANDTU_200UR = 32;
        public static UInt32 ZCAN_USBCAN_8E_U = 34;
        public static UInt32 ZCAN_CANDTU_NET = 36;
        public static UInt32 ZCAN_CANDTU_100UR = 37;
        public static UInt32 ZCAN_PCIE_CANFD_200U = 39;
        public static UInt32 ZCAN_PCIE_CANFD_400U = 40;
        public static UInt32 ZCAN_USBCANFD_200U = 41;
        public static UInt32 ZCAN_USBCANFD_100U = 42;
        public static UInt32 ZCAN_USBCANFD_MINI = 43;
        public static UInt32 ZCAN_CANSCOPE = 45;
        public static UInt32 ZCAN_CLOUD = 46;
        public static UInt32 ZCAN_CANDTU_NET_400 = 47;
        public static UInt32 ZCAN_CANFDNET_TCP = 48;
        public static UInt32 ZCAN_CANFDNET_200U_TCP = 48;
        public static UInt32 ZCAN_CANFDNET_UDP = 49;
        public static UInt32 ZCAN_CANFDNET_200U_UDP = 49;
        public static UInt32 ZCAN_CANFDWIFI_TCP = 50;
        public static UInt32 ZCAN_CANFDWIFI_100U_TCP = 50;
        public static UInt32 ZCAN_CANFDWIFI_UDP = 51;
        public static UInt32 ZCAN_CANFDWIFI_100U_UDP = 51;
        public static UInt32 ZCAN_CANFDNET_400U_TCP = 52;
        public static UInt32 ZCAN_CANFDNET_400U_UDP = 53;
        public static UInt32 ZCAN_CANFDNET_100U_TCP = 55;
        public static UInt32 ZCAN_CANFDNET_100U_UDP = 56;
        public static UInt32 ZCAN_CANFDNET_800U_TCP = 57;
        public static UInt32 ZCAN_CANFDNET_800U_UDP = 58;
        public static UInt32 ZCAN_USBCANFD_800U = 59;
        public static UInt32 ZCAN_PCIE_CANFD_100U_EX = 60;
        public static UInt32 ZCAN_PCIE_CANFD_400U_EX = 61;
        public static UInt32 ZCAN_PCIE_CANFD_200U_MINI = 62;
        public static UInt32 ZCAN_PCIE_CANFD_200U_EX = 63;
        public static UInt32 ZCAN_PCIE_CANFD_200U_M2 = 63;
        public static UInt32 ZCAN_CANFDDTU_400_TCP = 64;
        public static UInt32 ZCAN_CANFDDTU_400_UDP = 65;
        public static UInt32 ZCAN_CANFDWIFI_200U_TCP = 66;
        public static UInt32 ZCAN_CANFDWIFI_200U_UDP = 67;
        public static UInt32 ZCAN_CANFDDTU_800ER_TCP = 68;
        public static UInt32 ZCAN_CANFDDTU_800ER_UDP = 69;
        public static UInt32 ZCAN_CANFDDTU_800EWGR_TCP = 70;
        public static UInt32 ZCAN_CANFDDTU_800EWGR_UDP = 71;
        public static UInt32 ZCAN_CANFDDTU_600EWGR_TCP = 72;
        public static UInt32 ZCAN_CANFDDTU_600EWGR_UDP = 73;
        public static UInt32 ZCAN_CANFDDTU_CASCADE_TCP = 74;
        public static UInt32 ZCAN_CANFDDTU_CASCADE_UDP = 75;
        public static UInt32 ZCAN_USBCANFD_400U = 76;
        public static UInt32 ZCAN_CANFDDTU_200U = 77;
        public static UInt32 ZCAN_ZPSCANFD_TCP = 78;
        public static UInt32 ZCAN_ZPSCANFD_USB = 79;
        public static UInt32 ZCAN_CANFDBRIDGE_PLUS = 80;
        public static UInt32 ZCAN_CANFDDTU_300U = 81;
        public static UInt32 ZCAN_PCIE_CANFD_800U = 82;
        public static UInt32 ZCAN_PCIE_CANFD_1200U = 83;
        public static UInt32 ZCAN_MINI_PCIE_CANFD = 84;
        public static UInt32 ZCAN_USBCANFD_800H = 85;
        #endregion

        #region LIN事件
        public static UInt32 ZCAN_LIN_WAKE_UP = 1;
        public static UInt32 ZCAN_LIN_ENTERED_SLEEP_MODE = 2;
        public static UInt32 ZCAN_LIN_EXITED_SLEEP_MODE = 3;
        #endregion

        #region 函数
        [DllImport(".\\zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern IntPtr ZCAN_OpenDevice(uint device_type, uint device_index, uint reserved);


        [DllImport(".\\zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern uint ZCAN_CloseDevice(IntPtr device_handle);


        [DllImport(".\\zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern IntPtr ZCAN_InitCAN(IntPtr device_handle, uint can_index, IntPtr pInitConfig);


        [DllImport(".\\zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern uint ZCAN_StartCAN(IntPtr chn_handle);


        [DllImport(".\\zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern uint ZCAN_ResetCAN(IntPtr chn_handle);


        [DllImport(".\\zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern uint ZCAN_ClearBuffer(IntPtr chn_handle);


        [DllImport(".\\zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern uint ZCAN_GetReceiveNum(IntPtr channel_handle, byte type);


        [DllImport(".\\zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern uint ZCAN_Transmit(IntPtr channel_handle, IntPtr pTransmit, uint len);


        [DllImport(".\\zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern uint ZCAN_TransmitFD(IntPtr channel_handle, IntPtr pTransmit, uint len);


        [DllImport(".\\zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern uint ZCAN_TransmitData(IntPtr device_handle, IntPtr pTransmit, uint len);


        [DllImport(".\\zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern uint ZCAN_Receive(IntPtr channel_handle, IntPtr pReceive, uint len, int wait_time = -1);


        [DllImport(".\\zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern uint ZCAN_ReceiveFD(IntPtr channel_handle, IntPtr pReceive, uint len, int wait_time = -1);


        [DllImport(".\\zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern uint ZCAN_ReceiveData(IntPtr device_handle, IntPtr pReceive, uint len, int wait_time);


        [DllImport(".\\zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern uint ZCAN_SetValue(IntPtr device_handle, string path, string value);


        [DllImport(".\\zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern uint ZCAN_SetValue(IntPtr device_handle, string path, IntPtr value);


        [DllImport(".\\zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern IntPtr ZCAN_GetValue(IntPtr device_handle, string path);



        [DllImport(".\\zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern IntPtr ZCAN_InitLIN(IntPtr device_handle, uint lin_index, IntPtr pLINInitConfig);


        [DllImport(".\\zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern uint ZCAN_StartLIN(IntPtr channel_handle);


        [DllImport(".\\zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern uint ZCAN_ResetLIN(IntPtr channel_handle);


        [DllImport(".\\zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern uint ZCAN_TransmitLIN(IntPtr channel_handle, IntPtr pSend, uint Len);


        [DllImport(".\\zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern uint ZCAN_GetLINReceiveNum(IntPtr channel_handle);


        [DllImport(".\\zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern uint ZCAN_ReceiveLIN(IntPtr channel_handle, IntPtr pReceive, uint Len, int WaitTime);


        [DllImport(".\\zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern uint ZCAN_SetLINPublish(IntPtr channel_handle, IntPtr pSend, uint nPublishCount);


        [DllImport(".\\zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern uint ZCAN_SetLINSubscribe(IntPtr channel_handle, IntPtr pSend, uint nSubscribeCount);


        [DllImport(".\\zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern uint ZCAN_WakeUpLIN(IntPtr channel_handle);


        [DllImport(".\\zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern uint ZCAN_UDS_Request(IntPtr device_handle, IntPtr req, IntPtr resp, IntPtr dataBuf, uint dataBufSize);


        [DllImport(".\\zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern uint ZCAN_UDS_Control(IntPtr device_handle, IntPtr ctrl, IntPtr resp);


        [DllImport(".\\zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern uint ZCAN_UDS_RequestEX(IntPtr device_handle, IntPtr requestData, IntPtr resp, IntPtr dataBuf, uint dataBufSize);


        [DllImport(".\\zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern uint ZCAN_UDS_ControlEX(IntPtr device_handle, uint dataType, IntPtr ctrl, IntPtr resp);

        [DllImport(".\\zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern uint ZCAN_IsDeviceOnLine(IntPtr device_handle);

        [DllImport(".\\zlgcan.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern uint ZCAN_ReadChannelErrInfo(IntPtr channel_handle, IntPtr pErrInfo);
        #endregion

        #region 结构体
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct ZCAN_CHANNEL_INIT_CONFIG
        {
            public uint can_type;
            public _ZCAN_CHANNEL_INIT_CONFIG config;
        }


        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct _ZCAN_CHANNEL_INIT_CONFIG
        {
            public _ZCAN_CHANNEL_CAN_INIT_CONFIG can;
            public _ZCAN_CHANNEL_CANFD_INIT_CONFIG canfd;
        }


        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct _ZCAN_CHANNEL_CAN_INIT_CONFIG
        {
            public uint acc_code;
            public uint acc_mask;
            public uint reserved;
            public byte filter;
            public byte timing0;
            public byte timing1;
            public byte mode;
        }


        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct _ZCAN_CHANNEL_CANFD_INIT_CONFIG
        {
            public uint acc_code;
            public uint acc_mask;
            public uint abit_timing;
            public uint dbit_timing;
            public uint brp;
            public byte filter;
            public byte mode;
            public ushort pad;
            public uint reserved;
        }


        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct ZCAN_Transmit_Data
        {
            public can_frame frame;
            public uint transmit_type;
        }


        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public unsafe struct can_frame
        {
            public uint can_id;
            public byte can_dlc;            // frame payload length in byte (0 .. CAN_MAX_DLEN)
            public byte __pad;             // padding
            public byte __res0;            // reserved / padding
            public byte __res1;            // reserved / padding
            public fixed byte data[8];
        }


        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public unsafe struct canfd_frame
        {
            public uint can_id;
            public byte len;
            public byte flags;
            public byte __res0;
            public byte __res1;  /* reserved / padding */
            public fixed byte data[64];
        };


        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class ZCAN_Receive_Data
        {
            public can_frame frame;
            public UInt64 timestamp;
        }


        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class ZCAN_ReceiveFD_Data
        {
            public canfd_frame frame;
            public UInt64 timestamp;
        }


        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct ZCAN_AUTO_TRANSMIT_OBJ    //CANFD定时发送帧结构体
        {
            public ushort enable;           //0-禁用，1-使能  
            public ushort index;            //定时报文索引  
            public uint interval;                  //定时周期
            public ZCAN_Transmit_Data obj;
        }


        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct ZCANFD_AUTO_TRANSMIT_OBJ    //CANFD定时发送帧结构体
        {
            public ushort enable;           //0-禁用，1-使能  
            public ushort index;            //定时报文索引  
            public uint interval;                  //定时周期
            public ZCAN_TransmitFD_Data obj;
        }


        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct ZCAN_TransmitFD_Data
        {
            public canfd_frame frame; // 报文数据信息，详见 canfd_frame 结构说明。
            public uint transmit_type;      // 发送方式，0=正常发送，1=单次发送，2=自发自收，3=单次自发自收。
        }


        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public unsafe struct ZCANCANFDData
        {
            public UInt64 timeStamp;
            public UInt32 flag;                                     // flag用于设置一些参数，内部结构可以通过以下函数实现设置和取值
            public fixed byte extraData[4];                                //未使用  
            public canfd_frame frame;                               //实际报文结构体

            // frameType 帧类型 0-CAN 1-CANFD
            public uint frameType {                 
                get { return (flag & 0x03); }
                set { flag = (uint)((flag & ~0x03) | (value & 0x03)); }
            }

            // txDelay队列发送延时，延时时间存放在 timeStamp 字段；0-不启用延时，1-启用延时，单位1ms，2-启用延时，单位100us
            public uint txDelay {
                get { return ((flag >> 2) & 0x03); }
                set { flag = (uint)((flag & ~0x0C) | (value & 0x03) << 2); }
            }

            public uint transmitType {
                get { return ((flag >> 4) & 0x0F); }
                set { flag = (uint)((flag & ~0x0F) | (value & 0x0F) << 4); }
            }

            public uint txEchoRequest {
                get { return ((flag >> 8) & 0x01); }
                set { flag = (uint)(flag | (value & 0x01) << 8); }
            }

            public uint txEchoed {
                get { return ((flag >> 9) & 0x01); }  // bit9
                set { flag = (uint)((flag & ~0x200) | (value & 0x01) << 9); }
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct PID
        {
            public byte rawVal;
        }


        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct RxData
        {
            public UInt64 timeStamp;
            public byte datalen;
            public byte dir;
            public byte chkSum;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 13)]
            public byte[] reserved;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public byte[] data;
        }


        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public unsafe struct ZCANDataObj
        {
            public byte dataType;                                       // 1-CAN/CANFD数据, 4-LIN数据
            public byte chnl;                                           // 数据通道 
            public UInt16 flag;                                         // 未使用
    
            public fixed byte extraData[4];                             // 未使用  
   
            public fixed byte data[92];                                 // 报文结构体
        }


        // LIN
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public unsafe struct ZCANLINData
        {
            public PID pid;                 // 受保护的ID
            public RxData rxData;           // 数据
            public fixed byte reserved[7];
        }


        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public unsafe struct ZCANLINErrData
        {
            public UInt64 timeStamp;        // 时间戳，单位微秒(us)
            public PID pid;                 // 受保护的ID

            public byte dataLen;            // 数据长度
  
            public fixed byte data[8];      // 数据


            public fixed byte errData[2];   // 错误信息

            public byte dir;                // 传输方向
            public byte chkSum;             // 数据校验，部分设备不支持校验数据的获取
            
            public fixed byte reserved[10];
        }


        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public unsafe struct ZCANLINEventData
        {
            public UInt64 timeStamp;        // 时间戳，单位微秒(us)
            public byte type;               // 数据长度
          
            public fixed byte reserved[7];
        }


        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct ZCAN_LIN_MSG
        {
            public byte chnl;                       // 数据通道
            public byte dataType;                   // 0-LIN，1-ErrLIN
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 46)]
            public byte[] data;
        };


        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct ZCAN_LIN_INIT_CONFIG
        {
            public byte linMode;            // 0-slave,1-master
            public byte chkSumMode;         // 1-经典校验，2-增强校验 3-自动(对应eZLINChkSumMode的模式)
            public byte maxLength;          // 最大数据长度，8~64
            public byte reserved;
            public uint libBaud;            // 波特率，取值1000~20000

        };


        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct ZCAN_LIN_PUBLISH_CFG
        {
            public byte ID;                                         // 受保护的ID（ID取值范围为0-63）
            public byte datelen;                                    // 范围1~8
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]    // 数据段内容
            public byte[] data;
            public byte chkSumMode;                                 // 校验方式：0-默认，启动时配置  1-经典校验  2-增强校验(对应eZLINChkSumMode的模式)
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
            public byte[] reserved;
        };


        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct ZCAN_LIN_SUBSCIBE_CFG
        {
            public byte ID;                                         // 受保护的ID（ID取值范围为0-63）
            public byte datelen;                                    // dataLen范围为1-8 当为255（0xff）则表示设备自动识别报文长度
            public byte chkSumMode;                                 // 校验方式：0-默认，启动时配置  1-经典校验  2-增强校验(对应eZLINChkSumMode的模式)
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
            public byte[] reserved;
        };


        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct SESSION_PARAM
        {
            public uint timeout;            // 响应超时时间(ms)。因PC定时器误差，建议设置不小于200ms
            public uint enhanced_timeout;   // 收到消极响应错误码为0x78后的超时时间(ms)。因PC定时器误差，建议设置不小于200ms
            public byte threeInOne;         //三合一，把下面三个变量写进这一个变量

            // threeInOne 包含以下三个变量
            // BYTE check_any_negative_response : 1;  // 接收到非本次请求服务的消极响应时是否需要判定为响应错误
            // BYTE wait_if_suppress_response   : 1;  // 抑制响应时是否需要等待消极响应，等待时长为响应超时时间
            // BYTE flag                        : 6;  // 保留
            public byte check_any_negative_response
            {
                get { return (byte)(threeInOne & 0x0001); }
                set { threeInOne = (byte)((threeInOne & ~0x0001) | (value & 0x0001)); }
            }
            public byte wait_if_suppress_response
            {
                get { return (byte)((threeInOne & 0x0002) >> 1); }
                set { threeInOne = (byte)((threeInOne & ~0x0002) | (value & 0x0002)); }
            }
            public byte flag
            {
                get { return (byte)((threeInOne & 0x00FC) >> 2); }
                set { threeInOne = (byte)((threeInOne & ~0x00FC) | (value & 0x00FC)); }
            }
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 7)]
            public byte[] reserved0;
        };


        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct TRANS_PARAM
        {
            public byte version;            // 传输协议版本，VERSION_0，VERSION_1
            public byte max_data_len;       // 单帧最大数据长度，can:8，canfd:64
            public byte local_st_min;       // 本程序发送流控时用，连续帧之间的最小间隔，0x00-0x7F(0ms~127ms)，0xF1-0xF9(100us~900us)
            public byte block_size;         // 流控帧的块大小
            public byte fill_byte;          // 无效字节的填充数据
            public byte ext_frame;          // 0:标准帧 1:扩展帧
            public byte is_modify_ecu_st_min;   // 是否忽略ECU 返回流控的STmin，强制使用本程序设置的remote_st_min 参数代替
            public byte remote_st_min;          // 发送多帧时用, is_modify_ecu_st_min = 1 时有效，0x00 - 0x7F(0ms~127ms), 0xF1 - 0xF9(100us~900us)
            public uint fc_timeout;             // 接收流控超时时间(ms)，如发送首帧后需要等待回应流控帧
            public byte fill_mode;              // 数据长度填充模式 0-就近填充 1-不填充 2-最大填充
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
            public byte[] reserved0;
        };


        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct TRANS_PARAM_LIN
        {
            public byte fill_byte;      // 无效字节的填充数据
            public byte st_min;         // 从节点准备接收诊断请求的下一帧或传输诊断响应的下一帧所需的最小时间
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
            public byte[] reserved0;
        };


        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public unsafe struct ZCAN_CHANNEL_ERROR_INFO
        {
            public uint error_code;
            public fixed byte passive_ErrData[3];
            public byte arLost_ErrData;
         }

        // CAN UDS请求数据
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct ZCAN_UDS_REQUEST      // 硬件UDS接口构体
        {
            public uint req_id;             // 请求事务索引ID，范围0~65535
            public byte channel;            // 设备通道索引
            public byte frame_type;         // 帧类型
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
            public byte[] reserved0;
            public uint src_addr;           // 请求地址
            public uint dst_addr;           // 响应地址
            public byte suppress_response;  // 1-抑制响应
            public byte sid;                // 请求服务id
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
            public byte[] reserved1;

            public SESSION_PARAM session_param;
            public TRANS_PARAM trans_param;
            public IntPtr data;             // 数据数组(不包含SID)
            public uint data_len;           // 数据数组的长度
            public uint reserved2;
        }


        // LIN UDS请求数据
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct ZLIN_UDS_REQUEST      // 硬件UDS接口构体
        {
            public uint req_id;                 // 请求事务索引ID，范围0~65535
            public byte channel;                // 设备通道索引
            public byte suppress_response;      // 1:抑制响应 0:不抑制
            public byte sid;                // 请求服务id
            public byte Nad;                // 节点地址
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public byte[] reserved1;


            public SESSION_PARAM session_param;
            public TRANS_PARAM_LIN trans_param;
            public IntPtr data;             // 数据数组(不包含SID)
            public uint data_len;           // 数据数组的长度
            public uint reserved2;
        }


        // UDS响应数据
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct ZCAN_UDS_RESPONSE
        {
            public byte status;             // 响应状态
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
            public byte[] reserved;
            public byte type;               // 响应类型
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] // 大小为8
            public byte[] raw;
        };


        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct UDS_RESPONSE_Positive
        {
            public byte sid;            // 响应服务id
            public uint data_len;       // 数据长度(不包含SID), 数据存放在接口传入的dataBuf中
        }


        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct UDS_RESPONSE_Negative
        {
            public byte neg_code;       // 固定为0x7F
            public byte sid;            // 请求服务id
            public byte error_code;     // 错误码
        }


        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct UDS_RESPONSE_raw
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] // 大小为8
            public byte[] raw;
        }


        [StructLayout(LayoutKind.Explicit)]
        public struct _UDS_RESPONSE_union
        {
            [FieldOffset(0)]
            public UDS_RESPONSE_Positive zudsPositive;

            [FieldOffset(0)]
            public UDS_RESPONSE_Negative zudsNegative;

            [FieldOffset(0)]
            public UDS_RESPONSE_raw raw;
        }


        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct ZCANCANFDUdsData
        {
            public IntPtr req;             // 请求信息
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 24)]
            public byte[] reserved;        // 保留位
        }


        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct ZCANLINUdsData
        {
            public IntPtr req;             // 请求信息
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 24)]
            public byte[] reserved;        // 保留位
        }


        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct ZCANUdsRequestDataObj
        {
            public uint dataType;          // 数据类型
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 63)]
            public byte[] data;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] reserved;        // 保留位
        }


        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct ZCAN_DYNAMIC_CONFIG_DATA
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
            public char[] key;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
            public char[] value;
        }
        #endregion
    }


    public class ZDBC
    {
        #region 常量定义
        public const int _MAX_FILE_PATH_          = 260;    // 最长文件路径
        public const int _DBC_NAME_LENGTH_        = 127;    // 名称最长长度
        public const int _DBC_COMMENT_MAX_LENGTH_ = 127;    // 注释最长长度
        public const int _DBC_UNIT_MAX_LENGTH_    = 23;     // 单位最长长度
        public const int _DBC_SIGNAL_MAX_COUNT_   = 256;    // 一个消息含有的信号的最大数目

        public const int MUTIPLEXER_NONE     = 0;   // 不使用复用器
        public const int MUTIPLEXER_M_VALUE  = 1;   // 复用信号，当复用器开关的值为multiplexer_value时，该信号有效
        public const int MUTIPLEXER_M_SWITCH = 2;   // 复用器开关，一个DBC消息只能有一个信号为开关

        public const int FT_CAN = 0;    // CAN
        public const int FT_CANFD = 1;    // CANFD

        public const int PROTOCOL_J1939 = 0;
        public const int PROTOCOL_OTHER = 1;
        public const uint INVALID_DBC_HANDLE = 0xffffffff;  // 无效的DBC句柄
        #endregion

        #region 函数部分

        // ZDBC.dll
        public delegate bool OnSend(IntPtr ctx, IntPtr pObj);
        public delegate void OnMultiTransDone(IntPtr ctx, IntPtr pMsg, IntPtr data, UInt16 nLen, byte nDirection);
        public static OnSend onSend;
        public static OnMultiTransDone onMultiTransDone;

        /// <summary>
        /// 此函数用于初始化解析模块，只需要初始化一次。
        /// </summary>
        /// <param name="disableMultiSend">是否关闭多帧发送，为 1 时不支持多帧的消息发送。</param>
        /// <param name="enableAsyncAnalyse">是否开启异步解析；0-不启动，ZDBC_AsyncAnalyse 接口无效；1-启动, 独立线程解析出消息。</param>
        /// <returns>为 INVALID_DBC_HANDLE 表示初始化失败，其他表示初始化成功，保存该返回值，之后的函数调用都要用到该句柄。</returns>
        [DllImport(".\\zdbc.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern uint ZDBC_Init(byte disableMultiSend = 0, byte enableAsyncAnalyse = 1);

        /// <summary>
        /// 释放资源, 与DBC_Init配对使用
        /// </summary>
        /// <param name="hDBC">hDBC-句柄, ZDBC_Init的返回值</param>
        [DllImport(".\\zdbc.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern void ZDBC_Release(uint DBCHandle);

        /// <summary>
        /// 此函数用以加载 DBC 格式文件。
        /// </summary>
        /// <param name="hDBC">句柄；ZDBC_Init的返回值</param>
        /// <param name="pFileInfo">结构体 FileInfo的指针 </param>
        /// <returns>为 true 表示加载成功，false 表示失败。</returns>
        [DllImport(".\\zdbc.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern bool ZDBC_LoadFile(uint DBCHandle, IntPtr pFileInfo);

        /// <summary>
        /// 从字符串加载DBC
        /// </summary>
        /// <param name="hDBC">hDBC-句柄, DBC_Load的返回值</param>
        /// <param name="pFileContent">pFileContent-文件内容字符串</param>
        /// <param name="merge">merge-是否合并到当前数据库; 1:不清除现有的数据, 即支持加载多个文件;0：清除原来的数据</param>
        /// <returns></returns>
        [DllImport(".\\zdbc.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern bool ZDBC_LoadContent(uint DBCHandle, IntPtr pFileContent, uint merge);

        /// <summary>
        /// 获取文件的第一条消息。
        /// </summary>
        /// <param name="DBCHandle">hDBC-句柄, DBC_Load的返回值</param>
        /// <param name="pMsg">pMsg 存储消息的信息</param>
        /// <returns>true表示成功</returns>
        [DllImport(".\\zdbc.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern bool ZDBC_GetFirstMessage(uint DBCHandle, IntPtr pMsg);

        /// <summary>
        /// 获取下一条消息。
        /// </summary>
        /// <param name="DBCHandle">hDBC-句柄, DBC_Load的返回值</param>
        /// <param name="pMsg">pMsg 存储消息的信息</param>
        /// <returns>true表示成功</returns>
        [DllImport(".\\zdbc.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern bool ZDBC_GetNextMessage(uint DBCHandle, IntPtr pMsg);

        /// <summary>
        /// 此函数用以根据 ID 获取消息数据。
        /// </summary>
        /// <param name="DBCHandle">句柄；</param>
        /// <param name="nID">帧 ID；</param>
        /// <param name="pMsg">消息信息结构体</param>
        /// <returns></returns>
        [DllImport(".\\zdbc.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern bool ZDBC_GetMessageById(uint DBCHandle, uint nID, IntPtr pMsg);

        /// <summary>
        /// 此函数用以获取 DBC 文件中含有的消息数目。
        /// </summary>
        /// <param name="DBCHandle">DBC句柄</param>
        /// <returns>DBC 文件中含有的消息数目</returns>
        [DllImport(".\\zdbc.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern uint ZDBC_GetMessageCount(uint DBCHandle);

        [DllImport(".\\zdbc.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern bool ZDBC_Analyse(uint DBCHandle, IntPtr pObj, IntPtr pMsg);


        [DllImport(".\\zdbc.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern bool ZDBC_AsyncAnalyse(uint DBCHandle, IntPtr pObj, uint frame_type, UInt64 extraData);


        [DllImport(".\\zdbc.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern void ZDBC_OnReceive(uint DBCHandle, IntPtr pObj, uint frame_type);


        [DllImport(".\\zdbc.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern void ZDBC_SetSender(uint hDBC, OnSend sender, IntPtr ctx);


        [DllImport(".\\zdbc.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern void ZDBC_SetOnMultiTransDoneFunc(uint hDBC, OnMultiTransDone func, IntPtr ctx);


        [DllImport(".\\zdbc.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern byte ZDBC_Send(uint hDBC, IntPtr pMsg);

        /// <summary>
        /// 根据原始数据解码为 DBCMessage。
        /// </summary>
        /// <param name="DBCHandle">DBC句柄</param>
        /// <param name="P2DBCMessage">输出参数，解析结果。</param>
        /// <param name="P2Obj">帧数据数组, ControlCAN 传入 VCI_CAN_OBJ, zlgcan 传入 can_frame。</param>
        /// <param name="nCount">原始帧数据个数, 即数组大小。</param>
        /// <param name="frame_type">frame_type 帧类型, 参考FT_CAN=0、FT_CANFD=1，ControlCAN不支持CANFD。</param>
        /// <returns>是否成功。</returns>
        [DllImport(".\\zdbc.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern bool ZDBC_Decode(uint DBCHandle, IntPtr P2DBCMessage, IntPtr P2Obj, uint nCount, byte frame_type);
        
        /// <summary>
        /// 根据 DBCMessage 编码为原始数据。
        /// </summary>
        /// <param name="DBCHandle">DBC句柄；</param>
        /// <param name="P2Obj">编码的原始数据缓冲区数组, ControlCAN 传入 VCI_CAN_OBJ, zlgcan 传入 can_frame。</param>
        /// <param name="nCount">输出参数，pObj 缓冲区大小, 返回时为实际原始数据个数。</param>
        /// <param name="pMsg">输入参数，DBC 消息。</param>
        /// <param name="frame_type">frame_type 帧类型, FT_CAN=0、FT_CANFD=1，ControlCAN不支持CANFD。</param>
        /// <returns>是否成功。</returns>
        [DllImport(".\\zdbc.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern bool ZDBC_Encode(uint DBCHandle, IntPtr P2Obj, IntPtr P2nCount, IntPtr pMsg, byte frame_type);
        
        /// <summary>
        /// 信号原始值转换为实际值
        /// </summary>
        /// <param name="sgl">sgl 信号</param> 
        /// <param name="rawVal">rawVal 原始值, 如果该值超出信号长度可表示范围，会被截断。</param>
        /// <returns>实际值</returns>
        [DllImport(".\\zdbc.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern double ZDBC_CalcActualValue(IntPtr sgl, IntPtr rawVal); //原始值通过计算转为实际值,实际值会传入rawVal的地址

        /// <summary>
        /// 信号实际值转换为原始值
        /// </summary>
        /// <param name="sgl">sgl 信号</param>
        /// <param name="actualVal">actualVal 实际值, 超出可表示范围时会被修正</param>
        /// <returns>原始值</returns>
        [DllImport(".\\zdbc.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern UInt64 ZDBC_CalcRawValue(IntPtr sgl, IntPtr actualVal);

        /// <summary>
        /// 获取网络节点数量
        /// </summary>
        /// <param name="DBCHandle">ZDBC_Init的返回值</param>
        /// <returns>网络节点总数量</returns>
        [DllImport(".\\zdbc.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern UInt32 ZDBC_GetNetworkNodeCount(uint DBCHandle);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="DBCHandle">ZDBC_Init的返回值</param>
        /// <param name="index">index 位置索引</param>
        /// <param name="node">DBCNetworkNode * node 网络节点信息</param>
        /// <returns></returns>
        [DllImport(".\\zdbc.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern bool ZDBC_GetNetworkNodeAt(uint DBCHandle, UInt32 index, IntPtr node);


        /// <summary>
        /// 获取具体信号的值与含义对个数
        /// </summary>
        /// <param name="DBCHandle">ZDBC_Init的返回值</param>
        /// <param name="mag_id">message的ID</param>
        /// <param name="signal_name">signal的名字</param>
        /// <returns></returns>
        [DllImport(".\\zdbc.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern UInt32 ZDBC_GetValDescPairCount(uint DBCHandle, UInt32 mag_id, string signal_name);


        /// <summary>
        /// 获取具体信号的值与含义对
        /// </summary>
        /// <param name="DBCHandle">ZDBC_Init的返回值</param>
        /// <param name="mag_id">message的ID</param>
        /// <param name="signal_name">signal的名字</param>
        /// <param name="pair">ValDescPair结构体类型</param>
        [DllImport(".\\zdbc.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern void ZDBC_GetValDescPair(uint DBCHandle, UInt32 mag_id, string signal_name, IntPtr pair);


        #endregion

        #region DBC 结构体部分

        public struct FileInfo
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = _MAX_FILE_PATH_ + 1)]
            public byte[] strFilePath;  // dbc文件路径
            public byte type;           // dbc的协议类型, j1939选择PROTOCOL_J1939, 其他协议选择PROTOCOL_OTHER
            public byte merge;          // 1:不清除现有的数据, 即支持加载多个文件 0：清除原来的数据
        };

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct DBCSignal
        {

            public UInt32 nStartBit;    // 起始位
            public UInt32 nLen;         // 位长度
            public double nFactor;      // 转换因子
            public double nOffset;      // 转换偏移实际值=原始值*nFactor+nOffset
            public double nMin;         // 最小值
            public double nMax;         // 最大值
            public UInt64 nRawvalue;        // 原始值
            public byte is_signed;          // 1:有符号数据, 0:无符号
            public byte is_motorola;        // 是否摩托罗拉格式
            public byte multiplexer_type;   // 复用器类型
            public byte val_type;           // 0:integer, 1:float, 2:double
            public UInt32 multiplexer_value;    // 复用器开关值为此值时信号有效

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = _DBC_UNIT_MAX_LENGTH_ + 1)]
            public byte[] unit;                                         //单位
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = _DBC_NAME_LENGTH_ + 1)]        
            public byte[] strName;                                      //名称
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = _DBC_COMMENT_MAX_LENGTH_ + 1)]
            public byte[] strComment;                                   //注释
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = _DBC_NAME_LENGTH_ + 1)]
            public byte[] strValDesc;                                   //值描述

            public double initialValue;         // 初始化值（原始值）
            public uint initialValueValid;      // 初始值是否有效
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct DBCMessage
        {
            public UInt32 nSignalCount;     // 信号数量
            public UInt32 nID;              // ID
            public UInt32 nSize;            // 消息占的字节数目
            public double nCycleTime;       // 发送周期
            public byte nExtend;            // 1:扩展帧, 0:标准帧
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = _DBC_SIGNAL_MAX_COUNT_)]
            public DBCSignal[] vSignals;    // 信号集合
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = _DBC_NAME_LENGTH_ + 1)]
            public byte[] strName;          // 名称
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = _DBC_COMMENT_MAX_LENGTH_ + 1)]
            public byte[] strComment;       // 注释
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct ValDescPair
        {
            public double value;            // 信号值
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = _DBC_NAME_LENGTH_ + 1)]
            public byte[] strName;          // 对应的值描述
        }
        #endregion
    }


    public class ZUDS
    {
        #region 参数定义

        public static uint udsRTR = 0x40000000; // Remote Transmission Request
        public static uint udsEFF = 0x80000000; // Extend Frame Flag 
        public static uint udsERR = 0x20000000; // Err flag

        #endregion

        #region 函数部分
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate uint OnUDSTransmitDelegate(IntPtr ctx, IntPtr frame, uint count);


        /// <summary>
        /// 该函数用于初始化 UDS 函数库，返回操作句柄，用于后续的操作，与 ZUDS_Release
        /// 配对使用。
        /// typedef uint32 TP_TYPE; // transport protocol
        /// #define DoCAN 0
        /// </summary>
        /// <param name="Chn_Handle"></param>
        /// <returns>操作句柄，= ZUDS_INVALID_HANDLE 为无效句柄，其他值为有效句柄。</returns>
        [DllImport(".\\zuds.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern UInt32 ZUDS_Init(uint type);


        /// <summary>
        /// 该函数用于释放资源，与 ZUDS_Init 配对使用。
        /// </summary>
        /// <param name="Chn_Handle"></param>
        /// <returns></returns>
        [DllImport(".\\zuds.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern void ZUDS_Release(uint type);


        /// <summary>
        /// 该函数用于设置函数库的参数。
        /// </summary>
        /// <param name="ZUDS_HANDLE"></param>
        /// <param name="type">参数类型,= PARAM_TYPE_SESSION 0 用 于 设 置 会 话 层 参 数 ， =
        ///PARAM_TYPE_ISO15765 1 用于设置 ISO15765 的通信参数；</param>
        /// <param name="param">参数值,type =PARAM_TYPE_SESSION 0 时为 ZUDS_SESSION_PARAM，
        ///type= PARAM_TYPE_ISO15765 1 时为 ZUDS_ISO15765_PARAM。</param>
        [DllImport(".\\zuds.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern void ZUDS_SetParam(UInt32 ZUDS_HANDLE, byte type, IntPtr param);


        /// <summary>
        /// 该函数用于设置发送回调函数。函数库自身并不发送帧数据，把打包的帧数据通过回调
        ///函数传出给用户发送，用户可通过 zlgcan 函数库进行帧数据发送。
        /// </summary>
        /// <param name="ZUDS_HANDLE"></param>
        /// <param name="ctx">ctx 上下文参数, 在回调函数中传出, 库内部不会处理该参数；</param>
        /// <param name="onUDSTransmit">：回调函数原型；typedef uint32 (*OnUDSTransmit)(void* ctx, const ZUDS_FRAME* frame, uint32 count);</param>
        [DllImport(".\\zuds.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern void ZUDS_SetTransmitHandler(UInt32 ZUDS_HANDLE, IntPtr ctx, OnUDSTransmitDelegate onUDSTransmit);


        [DllImport(".\\zuds.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern void ZUDS_OnReceive(UInt32 ZUDS_HANDLE, IntPtr ZUDS_FRAME);


        [DllImport(".\\zuds.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern void ZUDS_Request(UInt32 ZUDS_HANDLE, IntPtr ZUDS_REQUEST, IntPtr ZUDS_RESPONSE);


        [DllImport(".\\zuds.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern void ZUDS_Stop(UInt32 ZUDS_HANDLE);


        [DllImport(".\\zuds.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern void ZUDS_SetTesterPresent(UInt32 ZUDS_HANDLE, byte enable, IntPtr param);

        #endregion

        #region 结构体部分
        /// <summary>
        /// 会话层面参数；即一应一答传输时的通讯参数。
        /// </summary>
        public struct ZUDS_SESSION_PARAM
        {
            public UInt16 timeout;// ms, timeout to wait the response of the server
            public UInt16 enhanced_timeout; // ms,timeout to wait after negative response: error code 0x78
            public UInt32 reserved0; // 保留
            public UInt32 reserved1; // 保留
        }


        /// <summary>
        /// 传输数据部分的参数，例如传输时侯每帧报文的字节数。
        /// </summary>
        public struct ZUDS_ISO15765_PARAM
        {
            public byte version; // VERSION_0, VERSION_1格式版本，为 VERSION_0 时符合 ISO15765-2 的 2004 版本格式要求；为
            //hVERSION_1 是符合 ISO15765-2 的 2016 版本新增的格式要求，如下图所示
            public byte max_data_len; // max data length, can:8, canfd:64 
            public byte local_st_min; // ms, min time between two consecutive frames
            public byte block_size;
            public byte fill_byte; // fill to invalid byte
            public byte frame_type; // 0:std 1:ext
            public byte is_modify_ecu_st_min; //是否忽略 ECU 返回流控的 STmin，强制使用本程序设置的
            //remote_st_min 参数代替

            public byte remote_st_min; //发 送 多 帧 时 用, is_ignore_ecu_st_min = 1 时 有 效 ,
            //0x00-0x7F(0ms ~127ms), 0xF1-0xF9(100us ~900us)
            public UInt16 fc_timeout; //接收流控超时时间(ms), 如发送首帧后需要等待回应流控帧。
            public byte fill_mode;//字节填充模式。FILL_MODE_NONE-不填充0；FILL_MODE_SHORT- 小于 8 字节填充至 8 字节，大于 8 字节时按 DLC 就近填充1；FILL_MODE_MAX- 始终填充至最大数据长度 (不建议)2。
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
            public byte[] reserved;
        }


        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct ZUDS_TESTER_PRESENT_PARAM
        {
            public UInt32 addr;//会话保持的请求地址；
            public UInt16 cycle;//发送周期，单位毫秒；
            public byte suppress_response; // 1:suppress是否抑制响应，建议设置为 1；
            public UInt32 reserved;//：保留，忽略即可。
        }


        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct ZUDS_REQUEST
        {
            public uint src_addr;             // 请求地址
            public uint dst_addr;             // 响应地址
            public byte suppress_response;    // 1:抑制响应
            public byte sid;                  //service id of request
            public ushort reserve0;
            public IntPtr param;              //array,params of the service 
            public uint param_len;            //参数数组的长度
            public uint reserved;
        }


        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class ZUDS_RESPONSE
        {
            public byte status;
            public byte type; // RT_POSITIVE, RT_NEGATIVE
            public _ZUDS_Union union;
            public uint reserved;
        }


        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct ZUDS_positive
        {
            public byte sid; // service id of response
            public IntPtr param; // array, params of the service, don't free
            public uint param_len;

        }


        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct _ZUDS_negative
        {
            public byte neg_code; // 0x7F
            public byte sid;      //service id of response
            public byte error_code;//消极响应的错误码

        }


        [StructLayout(LayoutKind.Explicit)]
        public struct _ZUDS_Union
        {
            [FieldOffset(0)]
            public ZUDS_positive zudsPositive;

            [FieldOffset(0)]
            public _ZUDS_negative zudsNegative;
        }


        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class ZUDS_FRAME
        {
            public uint id;
            public byte extend;
            public byte remote;
            public byte data_len;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
            public byte[] data;
            public uint reserved;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class ZUDS_CTX
        {
            public IntPtr can_type;     // 0-CAN 1-CANFD 2-CANFD加速
            public IntPtr chn_handle;   // 通道句柄
        }
        #endregion
    }
}
