using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CanKit.Sample.AvaloniaListener.Abstractions;

namespace CanKit.Sample.AvaloniaListener.ViewModels
{
    public class EndpointGeneratorViewModel : ObservableObject
    {
        public class DeviceItem
        {
            public string DisplayName { get; }
            public string PathName { get; }
            public DeviceItem(string display, string path)
            {
                DisplayName = display;
                PathName = path;
            }
            public override string ToString() => DisplayName;
        }

        public ObservableCollection<string> Vendors { get; } = new() { "周立功 (ZLG)" };

        private string _selectedVendor;
        public string SelectedVendor
        {
            get => _selectedVendor;
            set
            {
                if (SetProperty(ref _selectedVendor, value))
                {
                    OnPropertyChanged(nameof(IsZlg));
                }
            }
        }

        public bool IsZlg => SelectedVendor?.Contains("ZLG") == true || SelectedVendor?.Contains("周立功") == true;

        public ObservableCollection<DeviceItem> ZlgDevices { get; } = new();

        private DeviceItem? _selectedZlgDevice;
        public DeviceItem? SelectedZlgDevice
        {
            get => _selectedZlgDevice;
            set
            {
                if (SetProperty(ref _selectedZlgDevice, value))
                {
                    OnPropertyChanged(nameof(Preview));
                }
            }
        }

        private uint _deviceIndex = 0;
        public uint DeviceIndex
        {
            get => _deviceIndex;
            set
            {
                if (SetProperty(ref _deviceIndex, value))
                {
                    OnPropertyChanged(nameof(Preview));
                }
            }
        }

        private int _devicePort = 0;
        public int DevicePort
        {
            get => _devicePort;
            set
            {
                if (SetProperty(ref _devicePort, value))
                {
                    OnPropertyChanged(nameof(Preview));
                }
            }
        }

        public string Preview
        {
            get
            {
                if (IsZlg && SelectedZlgDevice != null)
                {
                    return $"zlg://{SelectedZlgDevice.PathName}?index={DeviceIndex}#ch{DevicePort}";
                }
                return string.Empty;
            }
        }

        public EndpointGeneratorViewModel()
        {
            _selectedVendor = Vendors[0];
            // ZLG device dictionary (display -> endpoint name)
            foreach (var it in GetZlgMap())
                ZlgDevices.Add(it);
            if (ZlgDevices.Count > 0) SelectedZlgDevice = ZlgDevices[0];
        }

        private static IEnumerable<DeviceItem> GetZlgMap()
        {
            yield return new DeviceItem("USBCAN-II", "USBCAN2");
            yield return new DeviceItem("USBCAN-I", "USBCAN1");
            yield return new DeviceItem("USBCAN-E-U", "USBCAN-E-U");
            yield return new DeviceItem("USBCAN-2E-U", "USBCAN-2E-U");
            yield return new DeviceItem("USBCAN-4E-U", "USBCAN-4E-U");
            yield return new DeviceItem("USBCAN-8E-U", "USBCAN-8E-U");
            yield return new DeviceItem("USBCANFD-100U", "USBCANFD-100U");
            yield return new DeviceItem("USBCANFD-200U", "USBCANFD-200U");
            yield return new DeviceItem("USBCANFD-MINI", "USBCANFD-MINI");
            yield return new DeviceItem("PCIE-CANFD-200U", "PCIE-CANFD-200U");
            yield return new DeviceItem("PCIE-CANFD-400U", "PCIE-CANFD-400U");
            yield return new DeviceItem("PCIE9820", "PCIE9820");
            yield return new DeviceItem("PCIE9820I", "PCIE9820I");
        }
    }
}

