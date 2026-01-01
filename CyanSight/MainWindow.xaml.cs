using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Wpf.Ui.Controls;
using CyanSight.ViewModels;
using Wpf.Ui.Appearance;
using System.Runtime.Versioning;

namespace CyanSight
{
    [SupportedOSPlatform("windows")]
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : FluentWindow
	{
        public MainWindow()
        {
            // 初始化 XAML 组件
            InitializeComponent();

            // 自动跟随系统变化，无需手动 ApplySystemTheme
            SystemThemeWatcher.Watch(this);

            // 窗口尺寸与定位逻辑
            ConfigureWindowSize();          
        }

        // **窗口尺寸与定位逻辑**
        private void ConfigureWindowSize()
        {
            
            // 获取屏幕工作区（不包含任务栏）的宽高
            double screenWidth = SystemParameters.WorkArea.Width;
            double screenHeight = SystemParameters.WorkArea.Height;

            // 设置宽度为屏幕的 60%; 高度为 80% 
            this.Width = screenWidth * 0.6;
            this.Height = screenHeight * 0.8;

            // 再次强制居中
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;

            // 再次强调背景为透明 (双重保险，防止某些样式被重置)
            this.Background = Brushes.Transparent;
        }      
    }
}
