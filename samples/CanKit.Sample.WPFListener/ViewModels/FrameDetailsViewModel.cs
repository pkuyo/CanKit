using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Threading.Channels;
using System.Windows.Threading;
using CanKit.Core.Definitions;
using EndpointListenerWpf.Models;
using EndpointListenerWpf.Resources;

namespace EndpointListenerWpf.ViewModels
{
    public class FrameDetailsViewModel : IDisposable
    {
        private readonly MainViewModel _main;
        private readonly string _idKey;

        public string Title => $"{I18n.T("Title_Detail")} - {_idKey}";
        public RangeObservableCollection<FrameRow> Items { get; }

        private readonly Channel<FrameRow> _itemChannel;
        private readonly DispatcherTimer _timer = new();
        public FrameDetailsViewModel(MainViewModel main, string idKey)
        {
            _main = main;
            _idKey = idKey;
            _main.FrameReceived += OnFrame;
            Items = new RangeObservableCollection<FrameRow>();
            _itemChannel = Channel.CreateBounded<FrameRow>(new BoundedChannelOptions(1000)
            {
                SingleWriter = false,
                SingleReader = false,
                FullMode = BoundedChannelFullMode.DropOldest
            });
            _timer.Interval = TimeSpan.FromSeconds(0.25f);
            _timer.Tick +=  (_, __) =>
            {
                try
                {
                    Items.Suppress = true;
                    var insertCount = _itemChannel.Reader.Count;
                    for(var i = 0; i < insertCount; i++)
                        if(_itemChannel.Reader.TryRead(out var frameRow))
                            Items.Add(frameRow);
                    Items.Suppress = false;
                    Items.NotifyReset();
                }
                finally
                {
                    Items.Suppress = false;
                }
            };
            _timer.Start();

        }

        private void OnFrame(CanReceiveData rec, FrameDirection dir)
        {
            ICanFrame f = rec.CanFrame;
            var key = f.IsExtendedFrame ? $"0x{f.ID:X8}" : $"0x{f.ID:X3}";
            if (!string.Equals(key, _idKey, StringComparison.OrdinalIgnoreCase))
                return;
            var row = FrameRow.From(rec, dir);
            _itemChannel.Writer.TryWrite(row);
        }

        public void Dispose()
        {
            _main.FrameReceived -= OnFrame;
            _timer.Stop();
        }
    }
}

