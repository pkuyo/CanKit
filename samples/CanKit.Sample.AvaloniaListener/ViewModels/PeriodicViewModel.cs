using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using CanKit.Sample.AvaloniaListener.Models;
using CanKit.Sample.AvaloniaListener.Services;

namespace CanKit.Sample.AvaloniaListener.ViewModels
{

    public class PeriodicViewModel : ObservableObject
    {
        private readonly IBusState _bus;
        private readonly IPeriodicTxService _svc;
        private bool _isRunning;
        private bool _allowFd;

        public ObservableCollection<PeriodicItemModel> Items { get; } = new();


        public bool IsRunning
        {
            get => _isRunning;
            set
            {
                if (SetProperty(ref _isRunning, value))
                {
                    OnPropertyChanged(nameof(RunText));
                    RefreshCanRun();
                }
            }
        }

        public string RunText => IsRunning ? "Stop" : "Run";

        public bool AllowFd
        {
            get => _allowFd;
            set => SetProperty(ref _allowFd, value);
        }

        public RelayCommand RunCommand { get; }
        public RelayCommand AddCommand { get; }

        public RelayCommand DeleteCommand { get; }

        public Func<PeriodicItemModel?>? ShowAddItemDialog { get; set; }

        public PeriodicViewModel(IBusState bus, IPeriodicTxService svc)
        {
            _bus = bus;
            _svc = svc;

            RunCommand = new RelayCommand(_ => OnRunStop(), _ => CanRun());
            AddCommand = new RelayCommand(_ => OnAdd(), _ => !IsRunning);
            DeleteCommand = new RelayCommand(obj => OnDelete(obj), _ => !IsRunning);
            Items.CollectionChanged += OnItemsChanged;
            _bus.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(IBusState.IsListening)) RefreshCanRun(); };
        }


        private void OnDelete(object? obj)
        {
            if (IsRunning)
                return;
            if (obj is PeriodicItemModel item)
            {
                Items.Remove(item);
            }
        }

        private void OnAdd()
        {
            if (IsRunning) return;
            var item = ShowAddItemDialog?.Invoke();
            if (item != null)
                Items.Add(item);
        }

        private void OnRunStop()
        {
            if (!IsRunning)
            {
                var list = Items.Where(x => x.Enabled).Select(x => x.ToSchedule()).ToArray();
                if (list.Length == 0) return;
                _svc.Start(list);
                IsRunning = true;
            }
            else
            {
                _svc.Stop();
                IsRunning = false;
            }
        }

        private bool CanRun()
        {
            if (IsRunning) return true;
            var hasEnabled = Items.Any(x => x.Enabled);
            return hasEnabled && _bus.IsListening;
        }

        private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (var it in e.OldItems.OfType<PeriodicItemModel>())
                    it.PropertyChanged -= OnItemPropertyChanged;
            }
            if (e.NewItems != null)
            {
                foreach (var it in e.NewItems.OfType<PeriodicItemModel>())
                    it.PropertyChanged += OnItemPropertyChanged;
            }
            RefreshCanRun();
        }

        private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PeriodicItemModel.Enabled))
                RefreshCanRun();
        }

        public void RefreshCanRun()
        {
            RunCommand.RaiseCanExecuteChanged();
            AddCommand.RaiseCanExecuteChanged();
        }
    }
}

