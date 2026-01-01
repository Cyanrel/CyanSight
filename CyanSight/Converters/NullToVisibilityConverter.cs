using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CyanSight.Converters
{
	// 实现 IValueConverter 接口，用于在 XAML 中转换数据
	public class NullToVisibilityConverter : IValueConverter
	{
		// 一个开关：是否反转逻辑
		public bool IsInverted { get; set; } = false;

		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			// 检查传入的值是否为 null
			bool isNull = value == null;

			if (IsInverted)
			{
				// 反转模式：如果是 null，则显示 (Visible)；否则隐藏 (Collapsed)
				// 用途：用于“请选择项目”这行提示文字
				return isNull ? Visibility.Visible : Visibility.Collapsed;
			}
			else
			{
				// 正常模式：如果是 null，则隐藏 (Collapsed)；否则显示 (Visible)
				// 用途：用于右侧的详细内容面板
				return isNull ? Visibility.Collapsed : Visibility.Visible;
			}
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			throw new NotImplementedException();
		}
	}
}