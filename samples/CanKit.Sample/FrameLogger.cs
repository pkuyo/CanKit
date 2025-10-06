namespace CanKit.Sample;

// 需要的命名空间
using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Collections.Generic;

public struct FrameRecord
{
    public DateTime SystemTimestamp;
    public double ReceiveMs;
    public uint Id;
    public byte Dlc;               // 0..8
    public ulong PayloadPacked;    // data[0] 在最低位，依次上移

    public FrameRecord(DateTime sysTs, double rxMs, uint id, byte dlc, ReadOnlySpan<byte> data)
    {
        SystemTimestamp = sysTs;
        ReceiveMs = rxMs;
        Id = id;
        Dlc = dlc;
        ulong p = 0UL;
        // 最多 8 字节装进一个 ulong，避免每帧分配数组
        for (int i = 0; i < dlc && i < 8; i++)
            p |= ((ulong)data[i]) << (i * 8);
        PayloadPacked = p;
    }

    // 写入一行文本到 StringBuilder（InvariantCulture，避免逗号/小数点本地化差异）
    public void AppendTextLine(StringBuilder sb)
    {
        sb.Append('[').Append(SystemTimestamp).Append("] [")
          .Append(ReceiveMs.ToString("F3", CultureInfo.InvariantCulture))
          .Append("ms] 0x").Append(Id.ToString("X"))
          .Append(", ").Append(Dlc);

        // 逐字节 -> ' XX' 两字符 HEX（手写查表，兼容 netstandard2.0）
        ulong p = PayloadPacked;
        for (int i = 0; i < Dlc; i++)
        {
            byte b = (byte)(p & 0xFF);
            p >>= 8;
            sb.Append(' ');
            AppendByteHex(sb, b);
        }
        sb.Append('\n'); // 统一换行，跨平台一致
    }

    private static readonly char[] Hex = "0123456789ABCDEF".ToCharArray();
    private static void AppendByteHex(StringBuilder sb, byte b)
    {
        sb.Append(Hex[(b >> 4) & 0xF]);
        sb.Append(Hex[b & 0xF]);
    }
}

public sealed class FrameLogger : IDisposable
{
    private readonly Queue<FrameRecord> _q;
    private readonly int _capacity;
    private readonly object _lock = new object();
    private readonly AutoResetEvent _evt = new AutoResetEvent(false);
    private readonly Thread _writerThread;
    private volatile bool _stop;

    private readonly string _path;
    private readonly int _batchLines;
    private readonly int _flushBytes;

    public long dropped;


    public FrameLogger(string path, int capacity = 65536, int batchLines = 5000, int flushBytes = 1 << 20)
    {
        _path = path;
        _capacity = Math.Max(1024, capacity);
        _batchLines = Math.Max(256, batchLines);
        _flushBytes = Math.Max(1 << 16, flushBytes);
        _q = new Queue<FrameRecord>(_capacity);

        _writerThread = new Thread(WriterLoop)
        {
            IsBackground = true,
            Name = "FrameWriter"
        };
        _writerThread.Start();
    }
    public bool TryEnqueue(FrameRecord r)
    {
        lock (_lock)
        {
            if (_q.Count >= _capacity)
            {
                _q.Dequeue();
                dropped++;
            }
            _q.Enqueue(r);
        }

        _evt.Set();
        return true;
    }

    private void WriterLoop()
    {
        using (var fs = new FileStream(
            _path, FileMode.Create, FileAccess.Write, FileShare.Read,
            bufferSize: 1 << 20,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan))
        using (var sw = new StreamWriter(fs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = false, NewLine = "\n" })
        {
            var sb = new StringBuilder(1 << 20);
            int lines = 0;

            while (!_stop)
            {

                _evt.WaitOne(50); // 最多 20fps 刷盘一次，兼顾延迟与吞吐

                int drained = 0;
                do
                {
                    FrameRecord r;
                    lock (_lock)
                    {
                        if (_q.Count == 0) break;
                        r = _q.Dequeue();
                    }

                    r.AppendTextLine(sb);
                    lines++; drained++;

                    // 达到批量阈值就写一次
                    if (lines >= _batchLines || sb.Length >= _flushBytes)
                    {
                        sw.Write(sb.ToString());
                        sb.Length = 0;
                        lines = 0;
                    }
                }
                while (true);

                if (lines > 0)
                {
                    sw.Write(sb.ToString());
                    sb.Length = 0;
                    lines = 0;
                }
            }

            if (sb.Length > 0) sw.Write(sb.ToString());
            sw.Flush();
        }
    }

    public void Dispose()
    {
        _stop = true;
        _evt.Set();
        _writerThread.Join();
        _evt.Dispose();
    }
}
