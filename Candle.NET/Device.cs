using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;

namespace Candle {
    public class Device : IDisposable {
        static List<Device> FAlternativeDevices = new List<Device>();

        // Register a device from a different manufacturer which follows our interface
        public static void RegisterAlternativeDevice(Device device) {
            FAlternativeDevices.Add(device);
        }

        public static List<Device> ListDevices() {
            var result = new List<Device>();

            IntPtr list;
            if (!NativeFunctions.candle_list_scan(out list)) {
                throw (new Exception("Failed to get device list"));
            }

            byte count;
            if (!NativeFunctions.candle_list_length(list, out count)) {
                throw (new Exception("Failed to get length of device list"));
            }

            for (byte i = 0; i < count; i++) {
                IntPtr deviceHandle;
                if (NativeFunctions.candle_dev_get(list, i, out deviceHandle)) {
                    result.Add(new Device(deviceHandle));
                } else {
                    throw (new Exception(String.Format("Failed to get device {0}", i)));
                }
            }

            if (!NativeFunctions.candle_list_free(list)) {
                throw (new Exception(String.Format("Failed to free device list")));
            }

            // Add devices from other manufacturers to the list
            result.AddRange(Device.FAlternativeDevices);

            return result;
        }

        IntPtr FDeviceHandle;
        protected Dictionary<int, Channel> FChannels = new Dictionary<int, Channel>();

        protected BlockingCollection<Action> FActionQueue;
        protected BlockingCollection<Exception> FExceptionQueue;
        protected Thread FThread = null;
        protected bool FIsClosing = false;

        Object FIsBlockingUntilCompleteLock = new Object();
        bool FIsBlockingUntilComplete = false;

        static int NextInstanceIndex = 0;
        int FInstanceIndex;

        public Device(IntPtr deviceHandle) {
            this.FDeviceHandle = deviceHandle;
            this.FInstanceIndex = NextInstanceIndex++;
        }

        public virtual void Dispose() {
            this.Close();
            NativeFunctions.candle_dev_free(this.FDeviceHandle);
        }

        public virtual void Open() {
            this.Close();

            if (!NativeFunctions.candle_dev_open(this.FDeviceHandle)) {
                NativeFunctions.throwError(this.FDeviceHandle);
            }

            // Create queues
            this.FActionQueue = new BlockingCollection<Action>();
            this.FExceptionQueue = new BlockingCollection<Exception>();

            // Flush Rx on device
            {
                NativeFunctions.candle_frame_t nativeFrame;
                while (NativeFunctions.candle_frame_read(this.FDeviceHandle
                    , out nativeFrame
                    , 0)) { }
            }

            // Setup device thread
            this.FIsClosing = false;
            this.FThread = new Thread(this.ThreadedUpdate);
            this.FThread.Name = String.Format("Candle {0}", this.FInstanceIndex);

            // List Channels
            this.FChannels.Clear();
            byte channelCount;
            if (!NativeFunctions.candle_channel_count(this.FDeviceHandle, out channelCount)) {
                NativeFunctions.throwError(this.FDeviceHandle);
            }
            for (byte i = 0; i < channelCount; i++) {
                this.FChannels.Add(i, new Channel(this, i));
            }

            // Start device thread
            this.FThread.Start();
        }

        public virtual void Close() {
            if (this.FThread != null) {
                this.PerformBlocking(() => {
                    this.FIsClosing = true;
                    if (!NativeFunctions.candle_dev_close(this.FDeviceHandle)) {
                        NativeFunctions.throwError(this.FDeviceHandle);
                    }
                });

                // Close queues
                this.FActionQueue.CompleteAdding();
                this.FExceptionQueue.CompleteAdding();

                this.FThread.Join();
                this.FThread = null;

                this.FActionQueue.Dispose();
                this.FActionQueue = null;
                this.FExceptionQueue.Dispose();
                this.FExceptionQueue = null;
            }
        }

        public void Update() {
            foreach (var channel in this.FChannels) {
                channel.Value.Update();
            }
        }

        public void PerformInRightThread(Action action, bool blocking) {
            if (!this.IsOpen || Thread.CurrentThread == this.FThread) {
                action();
            } else {
                if (blocking) {
                    this.PerformBlocking(action);
                } else {
                    this.Perform(action);
                }
            }
        }

        public virtual void BlockUntilActionsComplete(TimeSpan timeout) {
            this.PerformBlocking(() => {
                // Wait until this action gets to the head of the queue and is performed
            });
        }

        public virtual string Path {
            get {
                var path = string.Empty;
                Action action = () => {
                    var nativePath = NativeFunctions.candle_dev_get_path(this.FDeviceHandle);
                    if (nativePath == IntPtr.Zero) {
                        throw (new Exception("Failed to get path"));
                    }
                    path = Marshal.PtrToStringUni(nativePath) ?? string.Empty;
                };
                this.PerformInRightThread(action, true);
                return path;
            }
        }

        public virtual UInt32 Timestamp {
            get {
                UInt32 value = 0;
                Action action = () => {
                    if (!NativeFunctions.candle_dev_get_timestamp_us(this.FDeviceHandle, out value)) {
                        NativeFunctions.throwError(this.FDeviceHandle);
                    }
                };
                this.PerformInRightThread(action, true);
                return value;
            }
        }

        public int ActionQueueSize {
            get {
                return this.FActionQueue.Count;
            }
        }

        public virtual NativeFunctions.candle_devstate_t DeviceState {
            get {
                var value = new NativeFunctions.candle_devstate_t();
                Action action = () => {
                    if (!NativeFunctions.candle_dev_get_state(this.FDeviceHandle, out value)) {
                        NativeFunctions.throwError(this.FDeviceHandle);
                    }
                };
                this.PerformInRightThread(action, true);
                return value;
            }
        }

        public bool IsOpen {
            get {
                return this.FThread != null;
            }
        }

        public Dictionary<int, Channel> Channels {
            get {
                return this.FChannels;
            }
        }

        public IntPtr Handle {
            get {
                return this.FDeviceHandle;
            }
        }

        public void Perform(Action action) {
            this.FActionQueue.Add(action);
        }

        public void PerformBlocking(Action action) {
            var returnEvent = new ManualResetEvent(false);
            Exception exception = null;
            this.FActionQueue.Add(() => {
                try {
                    action();
                } catch (Exception e) {
                    exception = e;
                }
                returnEvent.Set();
            });
            returnEvent.WaitOne();
            if (exception != null) {
                throw (exception);
            }
        }

        void ThreadedUpdate() {
            while (!this.FIsClosing) {
                try {
                    // Rx frames
                    {
                        NativeFunctions.candle_frame_t nativeFrame;

                        // Don't wait on the receive if we have messages to send
                        var waitTime = this.FActionQueue.Count > 0
                            ? 0U
                            : 10U;

                        if (NativeFunctions.candle_frame_read(this.FDeviceHandle
                            , out nativeFrame
                            , waitTime)) {
                            // Convert to Frame
                            var frame = new Frame();
                            {
                                var flags = (NativeFunctions.candle_id_flags)(nativeFrame.can_id);
                                frame.Identifier = nativeFrame.can_id & ((1 << 29) - 1);
                                frame.Extended = flags.HasFlag(NativeFunctions.candle_id_flags.CANDLE_ID_EXTENDED);
                                frame.RTR = flags.HasFlag(NativeFunctions.candle_id_flags.CANDLE_ID_RTR);
                                frame.Error = flags.HasFlag(NativeFunctions.candle_id_flags.CANDLE_ID_ERR);

                                frame.Data = new byte[nativeFrame.can_dlc];
                                Buffer.BlockCopy(nativeFrame.data, 0, frame.Data, 0, nativeFrame.can_dlc);

                                frame.Timestamp = nativeFrame.timestamp_us;
                            }

                            // Find the channel 
                            if (this.FChannels.ContainsKey(nativeFrame.channel)) {
                                this.FChannels[nativeFrame.channel].NotifyReceive(frame);
                            } else {
                                throw (new Exception(String.Format("Cannot find channel {0}", nativeFrame.channel)));
                            }
                        }
                    }

                    // Perform actions
                    {
                        int count = 0;

                        Action action;
                        while (this.FActionQueue.TryTake(out action)) {
                            action();

                            if (count++ > 64) {
                                // We have trouble when sending more than 92 message in a row between receives
                                break;
                            }
                        }
                    }
                } catch (Exception e) {
                    this.FExceptionQueue.Add(e);
                }
            }
        }

        public void SendOnChannel(Frame frame, byte channelIndex, bool blocking = false) {
            var nativeFrame = new NativeFunctions.candle_frame_t();
            nativeFrame.can_id = frame.Identifier;
            if (frame.Extended) {
                nativeFrame.can_id |= (UInt32)NativeFunctions.candle_id_flags.CANDLE_ID_EXTENDED;
            }
            if (frame.RTR) {
                nativeFrame.can_id |= (UInt32)NativeFunctions.candle_id_flags.CANDLE_ID_RTR;
            }
            if (frame.Error) {
                nativeFrame.can_id |= (UInt32)NativeFunctions.candle_id_flags.CANDLE_ID_ERR;
            }

            nativeFrame.data = new byte[8];
            nativeFrame.can_dlc = (byte)frame.Data.Length;
            Buffer.BlockCopy(frame.Data, 0, nativeFrame.data, 0, frame.Data.Length);

            var lengthOnBus = frame.LengthOnBus;
            this.PerformInRightThread(() => {
                if (!NativeFunctions.candle_frame_send(this.FDeviceHandle, channelIndex, ref nativeFrame)) {
                    NativeFunctions.throwError(this.FDeviceHandle);
                }
                this.Channels[channelIndex].IncrementTx(lengthOnBus);
            }, blocking);
        }

        public List<Exception> ReceiveErrors() {
            var result = new List<Exception>();
            if (this.IsOpen) {
                Exception exception;
                while (this.FExceptionQueue.TryTake(out exception)) {
                    result.Add(exception);
                }
            }
            return result;
        }

        public void NotifyError(Exception e) {
            this.FExceptionQueue.Add(e);
        }

        public int TxQueueSize {
            get {
                return this.FActionQueue.Count;
            }
        }
    }
}