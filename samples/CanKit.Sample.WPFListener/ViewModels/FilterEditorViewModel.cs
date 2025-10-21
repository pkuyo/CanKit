using System.Collections.ObjectModel;
using EndpointListenerWpf.Models;

namespace EndpointListenerWpf.ViewModels
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
            var dlg = new Views.AddFilterDialog();
            if (dlg.ShowDialog() == true && dlg.Result != null)
            {
                Filters.Add(dlg.Result);
            }
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

