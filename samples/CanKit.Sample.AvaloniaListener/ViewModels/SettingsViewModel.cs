using System;
using Avalonia.Controls;
using CanKit.Sample.AvaloniaListener.Services;
using CanKit.Sample.AvaloniaListener.Abstractions;

namespace CanKit.Sample.AvaloniaListener.ViewModels;

public class SettingsViewModel : ObservableObject
{
    private AppLanguage _selectedLanguage;
    public AppLanguage SelectedLanguage
    {
        get => _selectedLanguage;
        set => SetProperty(ref _selectedLanguage, value);
    }

    public RelayCommand OkCommand { get; }
    public RelayCommand CancelCommand { get; }

    private readonly Window _owner;

    public SettingsViewModel(Window owner)
    {
        _owner = owner;
        _selectedLanguage = SettingsService.Current.Language;

        OkCommand = new RelayCommand(_ =>
        {
            LocalizationService.SetLanguage(SelectedLanguage);
            TryClose(true);
        });

        CancelCommand = new RelayCommand(_ => TryClose(false));
    }

    private void TryClose(bool dialogResult)
    {
        try
        {
            _owner.Close();
        }
        catch { }
    }
}
