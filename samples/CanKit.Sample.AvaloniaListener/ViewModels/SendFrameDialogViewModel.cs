using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using CanKit.Core.Definitions;

namespace CanKit.Sample.AvaloniaListener.ViewModels;

public class SendFrameDialogViewModel : ObservableObject
{
    private int _frameTypeIndex; // 0=CAN20, 1=CANFD
    private int _idTypeIndex;    // 0=Standard, 1=Extend
    private string _idText = string.Empty;
    private string _dlcText = string.Empty;
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

    public Func<ICanFrame, int>? Transmit { get; set; }

    public RelayCommand SendCommand { get; }
    public RelayCommand CloseCommand { get; }

    public event EventHandler<bool?>? CloseRequested;

    public SendFrameDialogViewModel()
    {
        // Defaults
        IdTypeIndex = 0;
        FrameTypeIndex = 0;
        DlcText = "8";

        SendCommand = new RelayCommand(_ => OnSend());
        CloseCommand = new RelayCommand(_ => CloseRequested?.Invoke(this, null));
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

    private void OnSend()
    {
        ErrorText = string.Empty;

        if (!TryParseInt(IdText, out var id) || id < 0)
        {
            ErrorText = "Invalid ID.";
            return;
        }
        if (!TryParseInt(DlcText, out var dlc) || dlc < 0)
        {
            ErrorText = "Invalid DLC.";
            return;
        }

        var isExtended = IsExtended;
        var isFd = IsFd;
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
                var brs = Brs;
                var frame = new CanFdFrame(id, bytes, isExtendedFrame: isExtended, BRS: brs, ESI: false);
                var n = Transmit?.Invoke(frame) ?? 0;
                if (n <= 0)
                    ErrorText = "Frame not sent (driver rejected or not ready).";
            }
            else
            {
                if (dlc > 8) { ErrorText = "CAN 2.0 DLC must be 0..8."; return; }
                var rtr = Rtr;
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
                var frame = new CanClassicFrame(id, bytes, isExtendedFrame: isExtended, isRemoteFrame: rtr);
                var n = Transmit?.Invoke(frame) ?? 0;
                if (n <= 0)
                    ErrorText = "Frame not sent (driver rejected or not ready).";
            }
        }
        catch (Exception ex)
        {
            ErrorText = $"Failed to send: {ex.Message}";
        }
    }
}

