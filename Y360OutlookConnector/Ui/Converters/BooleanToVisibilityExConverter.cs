using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Y360OutlookConnector.Ui.Converters
{ 
    public class BooleanToVisibilityExConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == DependencyProperty.UnsetValue)
            {
                return Binding.DoNothing;
            }

            if (!(value is bool))
            {
                throw new ArgumentException("Invalid value type");
            }

            var val = (bool)value;

            if (parameter == null)
            {
                return val ? Visibility.Visible : Visibility.Collapsed;
            }

            var p = parameter.ToString();

            var items = p.Split(new[] { "," }, StringSplitOptions.RemoveEmptyEntries);

            if (items.Length == 2)
            {
                if (!Enum.TryParse(items[0], out Visibility positive))
                {
                    throw new ArgumentException($"Invalid value {items[0]}");
                }

                if (!Enum.TryParse(items[1], out Visibility negative))
                {
                    throw new ArgumentException($"Invalid value {items[1]}");
                }

                return val ? positive : negative;
            }

            throw new ArgumentException($"Invalid value {p}");
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}
