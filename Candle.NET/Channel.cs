using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace Candle {
    public class Channel {
        Device FDevice;
        byte FChannelIndex;
        protected BlockingCollection<Frame> FRxQueue = new BlockingCollection<Frame>();

        protected DateTime FCounterLastTime;
        protected UInt64 FCounterRxBits = 0;
        protected UInt64 FCounterTxBits = 0;

        int FRxBitsPerSecond = 0;
        int FTxBitsPerSecond = 0;

        public Channel(Device device, byte channelIndex) {
            this.FDevice = device;
            this.FChannelIndex = channelIndex;
        }

        virtual public NativeFunctions.candle_capability_t Capabilities {
            get {
                var capabilities = new NativeFunctions.candle_capability_t();
                this.FDevice.PerformBlocking(() => {
                    if (!NativeFunctions.candle_channel_get_capabilities(this.FDevice.Handle, this.FChannelIndex, out capabilities)) {
                        NativeFunctions.throwError(this.FDevice.Handle);
                    }
                });
                return capabilities;
            }
        }

        virtual public void SetTiming(NativeFunctions.candle_bittiming_t value) {
            this.FDevice.PerformBlocking(() => {
                if (!NativeFunctions.candle_channel_set_timing(this.FDevice.Handle, this.FChannelIndex, ref value)) {
                    NativeFunctions.throwError(this.FDevice.Handle);
                }
            });
        }

        virtual public void SetBitrate(int value) {
            if (value <= 0) {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            this.SetTiming(CalculateTiming(this.Capabilities, (uint)value));
        }

        virtual public void Start(int bitrate) {
            this.SetBitrate(bitrate);
            this.StartChannel();
        }

        virtual public void Start(NativeFunctions.candle_bittiming_t timing) {
            this.SetTiming(timing);
            this.StartChannel();
        }

        private void StartChannel() {
            this.FDevice.PerformBlocking(() => {
                if (!NativeFunctions.candle_channel_start(this.FDevice.Handle, this.FChannelIndex, 0)) {
                    NativeFunctions.throwError(this.FDevice.Handle);
                }
            });

            this.FCounterLastTime = DateTime.Now;
            this.FCounterRxBits = 0;
            this.FCounterTxBits = 0;
        }

        private static NativeFunctions.candle_bittiming_t CalculateTiming(NativeFunctions.candle_capability_t capabilities, uint bitrate) {
            var bestTiming = new NativeFunctions.candle_bittiming_t();
            ulong bestQuanta = 0;
            long bestSampleError = 0;

            if (capabilities.fclk_can == 0 || capabilities.brp_inc == 0 || capabilities.sjw_max == 0) {
                throw new InvalidOperationException("The Candle device reports invalid CAN timing capabilities.");
            }

            for (var tseg1 = capabilities.tseg1_min; tseg1 <= capabilities.tseg1_max; tseg1++) {
                for (var tseg2 = capabilities.tseg2_min; tseg2 <= capabilities.tseg2_max; tseg2++) {
                    var quanta = 1UL + tseg1 + tseg2;
                    var divisor = (ulong)bitrate * quanta;
                    if (capabilities.fclk_can % divisor != 0) {
                        continue;
                    }

                    var brp = capabilities.fclk_can / divisor;
                    if (brp < capabilities.brp_min || brp > capabilities.brp_max || brp % capabilities.brp_inc != 0) {
                        continue;
                    }

                    var sampleError = Math.Abs(8L * (1 + (long)tseg1) - 7L * (long)quanta);
                    var weightedError = sampleError * (long)bestQuanta;
                    var bestWeightedError = bestSampleError * (long)quanta;
                    if (bestQuanta != 0 && (weightedError > bestWeightedError || (weightedError == bestWeightedError && quanta <= bestQuanta))) {
                        continue;
                    }

                    bestQuanta = quanta;
                    bestSampleError = sampleError;
                    bestTiming = new NativeFunctions.candle_bittiming_t {
                        prop_seg = tseg1 / 2,
                        phase_seg1 = tseg1 - tseg1 / 2,
                        phase_seg2 = tseg2,
                        sjw = 1,
                        brp = (uint)brp
                    };
                }
            }

            if (bestQuanta == 0) {
                throw new InvalidOperationException($"The Candle device cannot generate {bitrate} bps from its {capabilities.fclk_can} Hz CAN clock.");
            }

            return bestTiming;
        }

        public void Update() {
            var now = DateTime.Now;
            var timeDelta = now - this.FCounterLastTime;

            var timeDeltaMillis = (UInt64)timeDelta.Milliseconds;
            if (timeDeltaMillis > 0) {
                this.FRxBitsPerSecond = (int)(this.FCounterRxBits * 1000 / timeDeltaMillis);
                this.FTxBitsPerSecond = (int)(this.FCounterTxBits * 1000 / timeDeltaMillis);
            } else {
                this.FRxBitsPerSecond = 0;
                this.FTxBitsPerSecond = 0;
            }

            this.FCounterLastTime = now;
            this.FCounterRxBits = 0;
            this.FCounterTxBits = 0;
        }

        virtual public void Stop() {
            this.FDevice.PerformBlocking(() => {
                if (!NativeFunctions.candle_channel_stop(this.FDevice.Handle, this.FChannelIndex)) {
                    NativeFunctions.throwError(this.FDevice.Handle);
                }
            });
        }

        virtual public void Send(Frame frame, bool blocking = false) {
            this.FDevice.SendOnChannel(frame, this.FChannelIndex, blocking);

        }

        public void NotifyReceive(Frame frame) {
            this.FRxQueue.Add(frame);
            this.IncrementRx(frame.LengthOnBus);
        }

        public List<Frame> Receive() {
            var frames = new List<Frame>();
            Frame frame;
            while (this.FRxQueue.TryTake(out frame)) {
                frames.Add(frame);
            }
            return frames;
        }

        public void IncrementTx(int bits) {
            this.FCounterTxBits += (UInt64)bits;
        }

        public void IncrementRx(int bits) {
            this.FCounterRxBits += (UInt64)bits;
        }

        public int RxBitsPerSecond {
            get {
                return this.FRxBitsPerSecond;
            }
        }

        public int TxBitsPerSecond {
            get {
                return this.FTxBitsPerSecond;
            }
        }

        public Device Device {
            get {
                return this.FDevice;
            }
        }

        public int Index {
            get {
                return this.FChannelIndex;
            }
        }
    }
}