using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using CanKit.Core.Definitions;
using CanKit.Sample.AvaloniaListener.Models;

namespace CanKit.Sample.AvaloniaListener.ViewModels;

public class AddPeriodicItemDialogViewModel : ObservableObject
{
    private int _frameTypeIndex; // 0=CAN20, 1=CANFD
    private int _idTypeIndex;    // 0=Standard, 1=Extend
    private string _idText = string.Empty;
    private string _dlcText = string.Empty;
    private string _periodText = string.Empty;
    private string _dataText = string.Empty;
    private bool _rtr;
    private bool _brs;
    private string _errorText = string.Empty;
    private bool _allowFd;

    public bool AllowFd
    {
        get => _allowFd;
        set => SetProperty(ref _allowFd, value);
    }

    public int FrameTypeIndex
    {
        get => _frameTypeIndex;
        set
        {
            if (SetProperty(ref _frameTypeIndex, value))
            {
                OnPropertyChanged(nameof(IsFd));
                OnPropertyChanged(nameof(IsClassic));
            }
        }
    }

    public int IdTypeIndex
    {
        get => _idTypeIndex;
        set => SetProperty(ref _idTypeIndex, value);
    }

    public string IdText
    {
        get => _idText;
        set => SetProperty(ref _idText, value);
    }

    public string DlcText
    {
        get => _dlcText;
        set => SetProperty(ref _dlcText, value);
    }

    public string PeriodText
    {
        get => _periodText;
        set => SetProperty(ref _periodText, value);
    }

    public string DataText
    {
        get => _dataText;
        set => SetProperty(ref _dataText, value);
    }

    public bool Rtr
    {
        get => _rtr;
        set => SetProperty(ref _rtr, value);
    }

    public bool Brs
    {
        get => _brs;
        set => SetProperty(ref _brs, value);
    }

    public string ErrorText
    {
        get => _errorText;
        set => SetProperty(ref _errorText, value);
    }

    public bool IsFd => FrameTypeIndex == 1;
    public bool IsClassic => !IsFd;
    public bool IsExtended => IdTypeIndex == 1;

    public PeriodicItemModel? Result { get; private set; }

    public RelayCommand OkCommand { get; }
    public RelayCommand CancelCommand { get; }

    public event EventHandler<bool?>? CloseRequested;

    public AddPeriodicItemDialogViewModel()
    {
        // Defaults. Caller should set AllowFd first, then set FrameTypeIndex accordingly if needed.
        IdTypeIndex = 0; // Standard
        FrameTypeIndex = 0; // default to CAN20
        DlcText = "8";
        PeriodText = "1000";

        OkCommand = new RelayCommand(_ => OnOk());
        CancelCommand = new RelayCommand(_ => CloseRequested?.Invoke(this, false));
    }

    private static bool TryParseInt(string? text, out int value)
    {
        text ??= string.Empty;
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        }
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static byte[] ParseHexBytes(string? text)
    {
        text ??= string.Empty;
        text = text.Replace(',', ' ');
        text = Regex.Replace(text, "\r?\n", " ");
        var parts = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Select(p => Convert.ToByte(p, 16)).ToArray();
    }

    private void OnOk()
    {
        ErrorText = string.Empty;

        if (!TryParseInt(IdText, out var id) || id < 0)
        {
            ErrorText = "Invalid ID.";
            return;
        }
        if (!TryParseInt(PeriodText, out var ms) || ms <= 0)
        {
            ErrorText = "Invalid period (ms).";
            return;
        }
        if (!TryParseInt(DlcText, out var dlc) || dlc < 0)
        {
            ErrorText = "Invalid DLC.";
            return;
        }

        var isFd = IsFd;
        var isExtended = IsExtended;
        if (isFd && !AllowFd)
        {
            ErrorText = "FD mode is not enabled.";
            return;
        }

        byte[] bytes;
        try
        {
            bytes = ParseHexBytes(DataText);
        }
        catch
        {
            ErrorText = "Invalid DATA. Use hex bytes like: 01 02 0A FF";
            return;
        }

        var rtr = Rtr;
        var brs = Brs;

        try
        {
            if (isFd)
            {
                if (dlc > 15) { ErrorText = "FD DLC must be 0..15."; return; }
                var targetLen = CanFdFrame.DlcToLen((byte)dlc);
                if (bytes.Length > targetLen)
                {
                    ErrorText = $"DATA length ({bytes.Length}) exceeds FD DLC length ({targetLen}).";
                    return;
                }
                if (bytes.Length < targetLen)
                {
                    Array.Resize(ref bytes, targetLen);
                }
                Result = new PeriodicItemModel
                {
                    Enabled = true,
                    Id = id,
                    PeriodMs = ms,
                    IsFd = true,
                    IsExtended = isExtended,
                    Brs = brs,
                    IsRemote = false,
                    DataBytes = bytes,
                    Dlc = (byte)dlc,
                };
            }
            else
            {
                if (dlc > 8) { ErrorText = "CAN 2.0 DLC must be 0..8."; return; }
                if (rtr)
                {
                    bytes = new byte[dlc];
                }
                else
                {
                    if (bytes.Length > dlc)
                    {
                        ErrorText = $"DATA length ({bytes.Length}) exceeds DLC ({dlc}).";
                        return;
                    }
                    if (bytes.Length < dlc)
                    {
                        Array.Resize(ref bytes, dlc);
                    }
                }
                Result = new PeriodicItemModel
                {
                    Enabled = true,
                    Id = id,
                    PeriodMs = ms,
                    IsFd = false,
                    IsExtended = isExtended,
                    IsRemote = rtr,
                    Brs = false,
                    DataBytes = bytes,
                    Dlc = (byte)dlc,
                };
            }
        }
        catch (Exception ex)
        {
            ErrorText = $"Failed to build frame: {ex.Message}";
            return;
        }

        CloseRequested?.Invoke(this, true);
    }
}

