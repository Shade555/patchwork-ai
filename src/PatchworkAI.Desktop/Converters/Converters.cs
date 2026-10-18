using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using PatchworkAI.Core.Models;

namespace PatchworkAI.Desktop.Converters
{
    public class SeverityToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is IssueSeverity severity)
            {
                return severity switch
                {
                    IssueSeverity.Error => new SolidColorBrush(Color.FromRgb(239, 68, 68)),    // Red
                    IssueSeverity.Warning => new SolidColorBrush(Color.FromRgb(245, 158, 11)), // Amber
                    IssueSeverity.Info => new SolidColorBrush(Color.FromRgb(59, 130, 246)),    // Blue
                    _ => new SolidColorBrush(Color.FromRgb(156, 163, 175))
                };
            }
            return new SolidColorBrush(Color.FromRgb(156, 163, 175));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class StatusToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is TransformationStatus status)
            {
                return status switch
                {
                    TransformationStatus.Approved => new SolidColorBrush(Color.FromRgb(34, 197, 94)),  // Green
                    TransformationStatus.Applied => new SolidColorBrush(Color.FromRgb(34, 197, 94)),   // Green
                    TransformationStatus.Rejected => new SolidColorBrush(Color.FromRgb(239, 68, 68)),  // Red
                    TransformationStatus.Reverted => new SolidColorBrush(Color.FromRgb(156, 163, 175)),// Gray
                    _ => new SolidColorBrush(Color.FromRgb(245, 158, 11))                              // Amber (Pending)
                };
            }
            return new SolidColorBrush(Color.FromRgb(156, 163, 175));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class BooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
            {
                bool invert = parameter is string s && s.Equals("invert", StringComparison.OrdinalIgnoreCase);
                bool visible = invert ? !b : b;
                return visible ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class NullOrEmptyToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool hasValue = value switch
            {
                string s => !string.IsNullOrWhiteSpace(s),
                null => false,
                _ => true
            };
            bool invert = parameter is string param && param.Equals("invert", StringComparison.OrdinalIgnoreCase);
            return (invert ? !hasValue : hasValue) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class BoolToStatusTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
            {
                return b ? "Online" : "Offline";
            }
            return "Unknown";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class StatusToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value != null && parameter != null)
            {
                bool matches = string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase);
                return matches ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}
