using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CanKit.Sample.AvaloniaListener.Models;
using CanKit.Sample.AvaloniaListener.ViewModels;

namespace CanKit.Sample.AvaloniaListener.Views;

public partial class FilterEditorWindow : Window
{
    public FilterEditorWindow(ObservableCollection<FilterRuleModel> filters)
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = new FilterEditorViewModel(filters);
    }
}
