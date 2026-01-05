using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CyanSight.Services
{
    public static class ScrollHelper
    {
        // 定义一个 "SpeedMultiplier" 属性，默认值 1.0 (原速)
        public static readonly DependencyProperty SpeedMultiplierProperty =
            DependencyProperty.RegisterAttached(
                "SpeedMultiplier",
                typeof(double),
                typeof(ScrollHelper),
                new PropertyMetadata(1.0, OnSpeedMultiplierChanged));

        public static double GetSpeedMultiplier(DependencyObject obj) => (double)obj.GetValue(SpeedMultiplierProperty);
        public static void SetSpeedMultiplier(DependencyObject obj, double value) => obj.SetValue(SpeedMultiplierProperty, value);

        // 当属性值改变时触发
        private static void OnSpeedMultiplierChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ScrollViewer scrollViewer)
            {
                scrollViewer.PreviewMouseWheel -= ScrollViewer_PreviewMouseWheel;
                // 如果速度倍率不为 1.0，则挂载我们的自定义滚动逻辑
                if ((double)e.NewValue != 1.0)
                {
                    scrollViewer.PreviewMouseWheel += ScrollViewer_PreviewMouseWheel;
                }
            }
        }

        // 拦截滚轮，手动计算偏移量
        private static void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var scrollViewer = (ScrollViewer)sender;
            double multiplier = GetSpeedMultiplier(scrollViewer);

            // 系统默认通常是 48，乘以系数
            // 比如 0.3 的系数，就是 48 * 0.3 = 14.4 像素
            double scrollAmount = e.Delta * multiplier;

            // 计算新位置
            double newOffset = scrollViewer.VerticalOffset - scrollAmount;

            // 执行滚动
            scrollViewer.ScrollToVerticalOffset(newOffset);

            // 标记事件已处理，阻止 WPF 默认的“大步跨越”滚动
            e.Handled = true;
        }
    }
}