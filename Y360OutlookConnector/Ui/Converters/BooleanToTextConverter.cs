using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace Y360OutlookConnector.Ui.Converters
{
    public class BooleanToTextConverter : MarkupExtension, IValueConverter
    {
        public string TextForTrue { set; private get; }

        public string TextForFalse { set; private get; }

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

            return (val ? TextForTrue : TextForFalse) ?? string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            return this;
        }
    }
}
