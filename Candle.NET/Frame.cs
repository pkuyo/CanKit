using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Candle {
    public class Frame {
        public UInt32 Identifier;
        public byte[] Data;
        public UInt32 Timestamp;
        public bool Extended;
        public bool RTR;
        public bool Error;

        public override string ToString() {
            var value = String.Format("ID : {0}, Data : {1}, Time : {2}us"
                , this.Identifier
                , BitConverter.ToString(this.Data)
                , this.Timestamp
            );

            if (this.Extended) {
                value += " EXT";
            }
            if (this.RTR) {
                value += " RTR";
            }
            if (this.Error) {
                value += " Error";
            }

            return value;
        }

        // From https://en.wikipedia.org/wiki/CAN_bus#Frames
        public int LengthOnBus {
            get {
                if (!this.Extended) {
                    return 1 + 11 + 1 + 2 + 4 + 8 + 15 + 1 + 2 + 7 + 3;
                } else {
                    return 1 + 11 + 1 + 1 + 18 + 1 + 2 + 4 + (this.Data.Length * 8) + 15 + 1 + 1 + 1 + 7;
                }
            }
        }
    }
}