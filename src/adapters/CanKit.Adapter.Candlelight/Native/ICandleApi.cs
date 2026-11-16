using System;

namespace CanKit.Adapter.Candlelight.Native;

internal interface ICandleApi
{
    bool Scan(out IntPtr list);
    bool FreeList(IntPtr list);
    bool ListLength(IntPtr list, out byte length);
    bool GetDevice(IntPtr list, byte index, out IntPtr device);
    bool Open(IntPtr device);
    bool Close(IntPtr device);
    bool FreeDevice(IntPtr device);
    bool ChannelCount(IntPtr device, out byte count);
    bool GetCapabilities(IntPtr device, byte channel, out CandleNative.Capabilities capabilities);
    bool SetTiming(IntPtr device, byte channel, ref CandleNative.BitTiming timing);
    bool Start(IntPtr device, byte channel, CandleNative.Mode mode);
    bool Stop(IntPtr device, byte channel);
    bool Send(IntPtr device, byte channel, ref CandleNative.Frame frame);
    bool Read(IntPtr device, out CandleNative.Frame frame, uint timeoutMilliseconds);
    int LastError(IntPtr device);
}

internal sealed class CandleApi : ICandleApi
{
    internal static readonly CandleApi Instance = new();
    private CandleApi() { }
    public bool Scan(out IntPtr list) => CandleNative.candle_list_scan(out list);
    public bool FreeList(IntPtr list) => CandleNative.candle_list_free(list);
    public bool ListLength(IntPtr list, out byte length) => CandleNative.candle_list_length(list, out length);
    public bool GetDevice(IntPtr list, byte index, out IntPtr device) => CandleNative.candle_dev_get(list, index, out device);
    public bool Open(IntPtr device) => CandleNative.candle_dev_open(device);
    public bool Close(IntPtr device) => CandleNative.candle_dev_close(device);
    public bool FreeDevice(IntPtr device) => CandleNative.candle_dev_free(device);
    public bool ChannelCount(IntPtr device, out byte count) => CandleNative.candle_channel_count(device, out count);
    public bool GetCapabilities(IntPtr device, byte channel, out CandleNative.Capabilities capabilities) => CandleNative.candle_channel_get_capabilities(device, channel, out capabilities);
    public bool SetTiming(IntPtr device, byte channel, ref CandleNative.BitTiming timing) => CandleNative.candle_channel_set_timing(device, channel, ref timing);
    public bool Start(IntPtr device, byte channel, CandleNative.Mode mode) => CandleNative.candle_channel_start(device, channel, mode);
    public bool Stop(IntPtr device, byte channel) => CandleNative.candle_channel_stop(device, channel);
    public bool Send(IntPtr device, byte channel, ref CandleNative.Frame frame) => CandleNative.candle_frame_send(device, channel, ref frame);
    public bool Read(IntPtr device, out CandleNative.Frame frame, uint timeoutMilliseconds) => CandleNative.candle_frame_read(device, out frame, timeoutMilliseconds);
    public int LastError(IntPtr device) => CandleNative.candle_dev_last_error(device);
}
