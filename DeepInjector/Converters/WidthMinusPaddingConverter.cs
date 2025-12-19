using System;
using System.Globalization;
using System.Windows.Data;

namespace DeepInjector
{
    public class WidthMinusPaddingConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double width)
            {
                // Subtract padding from width (adjust value as needed based on your padding)
                return Math.Max(0, width - 30);
            }
            
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 