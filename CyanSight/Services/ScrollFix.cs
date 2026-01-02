using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CyanSight.Services
{
    public static class ScrollFix
    {
        // 1. 注册附加属性 "FixMouseWheel"
        public static readonly DependencyProperty FixMouseWheelProperty =
            DependencyProperty.RegisterAttached(
                "FixMouseWheel",
                typeof(bool),
                typeof(ScrollFix),
                new PropertyMetadata(false, OnFixMouseWheelChanged));

        // 标准的 Getter 和 Setter
        public static bool GetFixMouseWheel(DependencyObject obj)
        {
            return (bool)obj.GetValue(FixMouseWheelProperty);
        }

        public static void SetFixMouseWheel(DependencyObject obj, bool value)
        {
            obj.SetValue(FixMouseWheelProperty, value);
        }

        // 当属性值改变时（例如在 XAML 中设为 True），自动绑定事件
        private static void OnFixMouseWheelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is UIElement element)
            {
                if ((bool)e.NewValue)
                {
                    // 启用：订阅事件
                    element.PreviewMouseWheel += HandlePreviewMouseWheel;
                }
                else
                {
                    // 禁用：取消订阅
                    element.PreviewMouseWheel -= HandlePreviewMouseWheel;
                }
            }
        }

        // 拦截并转发滚轮事件
        private static void HandlePreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // 如果事件已经被处理，就不管了
            if (e.Handled) return;

            // 标记为已处理，防止控件自己“吃掉”
            e.Handled = true;

            // 构造一个新的事件，源头指向 Parent（外层容器）
            var eventArg = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = UIElement.MouseWheelEvent,
                Source = sender
            };

            // 向上抛出事件
            var parent = ((Control)sender).Parent as UIElement;
            parent?.RaiseEvent(eventArg);
        }
    }
}
