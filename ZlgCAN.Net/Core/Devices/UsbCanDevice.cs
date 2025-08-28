using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using ZlgCAN.Net.Core.Abstractions;
using ZlgCAN.Net.Core.Channels;
using ZlgCAN.Net.Core.Diagnostics;
using ZlgCAN.Net.Core.Models;
using ZlgCAN.Net.Native;

namespace ZlgCAN.Net.Core.Devices
{
    public class UsbCanDevice : ICanDevice
    {
        internal UsbCanDevice(CanDeviceInfo info)
        {
            _info = info;
        }

        public bool OpenDevice()
        {
            if(_isDisposed)
                throw new InvalidOperationException();

            _nativePtr = Native.ZLGCAN.ZCAN_OpenDevice((uint)DeviceInfo.DeviceType, DeviceInfo.DeviceIndex, 0);
            return IsDeviceOpen;
        }

        public void CloseDevice()
        {
            if (_isDisposed)
                throw new InvalidOperationException();

            ZlgErr.ThrowIfError(Native.ZLGCAN.ZCAN_CloseDevice(_nativePtr));
        }


        public CanDeviceInfo DeviceInfo => _info;

        public IntPtr NativePtr => _nativePtr;

        public ICanChannel InitChannel(CanChannelConfig config)
        {
            if (_isDisposed)
                throw new InvalidOperationException();

            if (config is not ClassicCanChannelConfig normal)
                throw new ArgumentException("Only NormalCanChannelConfig is supported in UsbCanIDevice");

            string path = string.Format("{0}/baud_rate", normal.ChannelIndex);

            ZlgErr.ThrowIfError(ZLGCAN.ZCAN_SetValue(_nativePtr, path, normal.BaudRate.ToString()));
           
            ZLGCAN.ZCAN_CHANNEL_INIT_CONFIG InitConfig = new ZLGCAN.ZCAN_CHANNEL_INIT_CONFIG();
            InitConfig.can_type = (uint)CanType.UsbCan;                
            InitConfig.config.can.mode = (byte)normal.Mode;         
            InitConfig.config.can.acc_code = normal.AcceptCode;
            InitConfig.config.can.acc_mask = normal.AcceptMask;


            IntPtr P2InitConfig = Marshal.AllocHGlobal(Marshal.SizeOf(InitConfig));
            Marshal.StructureToPtr(InitConfig, P2InitConfig, true);                       
            var chnPtr = ZLGCAN.ZCAN_InitCAN(_nativePtr, config.ChannelIndex, P2InitConfig);
            Marshal.FreeHGlobal(P2InitConfig);

            if(chnPtr == IntPtr.Zero)
                throw new InvalidOperationException("Failed to initialize CAN channel");

            return new ClassicCanChannel(chnPtr, config);
        }

        public bool IsDeviceConnected()
        {
            if (_isDisposed)
                throw new InvalidOperationException();
            return ZLGCAN.ZCAN_IsDeviceOnLine(_nativePtr) == 1;
        }

        public void Dispose()
        {
            try
            {
                if (_isDisposed)
                    throw new InvalidOperationException();


                if (_nativePtr != IntPtr.Zero)
                {
                    CloseDevice();
                }
            }
            finally
            {
                _nativePtr = IntPtr.Zero;
                _isDisposed = true;
            }
        }

        public bool IsDeviceOpen => _nativePtr != IntPtr.Zero;

        private CanDeviceInfo _info;

        private IntPtr _nativePtr;

        private bool _isDisposed;
    }
}
