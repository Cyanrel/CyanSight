using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CyanSight.Converters
{
    // 1. 如果是 Null，则显示 (Visible)；如果有东西，则隐藏 (Collapsed)
    // 用途：没选中项目时，显示“请选择...”提示文字
    public class NullToVisibleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value == null ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    // 2. 如果是 Null，则隐藏 (Collapsed)；如果有东西，则显示 (Visible)
    // 用途：没选中项目时，隐藏整个详情面板
    public class NullToCollapsedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value == null ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}