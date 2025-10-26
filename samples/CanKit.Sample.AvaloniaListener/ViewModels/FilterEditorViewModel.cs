using System;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Threading;
using CanKit.Sample.AvaloniaListener.Abstractions;
using CanKit.Sample.AvaloniaListener.Models;

namespace CanKit.Sample.AvaloniaListener.ViewModels
{
    public class FilterEditorViewModel : ObservableObject
    {
        public ObservableCollection<FilterRuleModel> Filters { get; }

        private FilterRuleModel? _selected;
        public FilterRuleModel? Selected
        {
            get => _selected;
            set
            {
                if (SetProperty(ref _selected, value))
                {
                    DeleteCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public RelayCommand AddCommand { get; }
        public RelayCommand DeleteCommand { get; }

        public FilterEditorViewModel(ObservableCollection<FilterRuleModel> filters)
        {
            Filters = filters;
            AddCommand = new RelayCommand(_ => OnAdd());
            DeleteCommand = new RelayCommand(_ => OnDelete(), _ => Selected != null);
        }

        private void OnAdd()
        {
            Dispatcher.UIThread.Post(async void () =>
            {
                try
                {
                    var dlg = new Views.AddFilterDialog();
                    var app = Application.Current;
                    var owner = (app?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                    bool? result = await dlg.ShowDialog<bool?>(owner!);

                    if (result == true && dlg.Result != null)
                    {
                        Filters.Add(dlg.Result);
                    }
                }
                catch (Exception)
                {
                    // ignore
                }
            });
        }

        private void OnDelete()
        {
            if (Selected != null)
            {
                Filters.Remove(Selected);
                Selected = null;
            }
        }
    }
}
