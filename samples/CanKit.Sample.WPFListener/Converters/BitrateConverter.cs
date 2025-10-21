using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace EndpointListenerWpf.Converters
{

    public class BitrateConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return string.Empty;

            double bps;
            try { bps = System.Convert.ToDouble(value, culture); }
            catch { return string.Empty; }

            string unit;
            double v;
            if (bps < 1_000) { v = bps; unit = "bps"; }
            else if (bps < 1_000_000) { v = bps / 1_000.0; unit = "kbps"; }
            else { v = bps / 1_000_000.0; unit = "Mbps"; }

            var numberFormat = parameter as string ?? "0.##";
            return v.ToString(numberFormat, culture) + " " + unit;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}
