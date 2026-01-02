using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CyanSight.Models;
using CyanSight.Services;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Xml.Linq;
using System.Text.RegularExpressions;

namespace CyanSight.ViewModels
{
	public partial class MainViewModel : ObservableObject
	{
        // 全量数据源 (内存大池子)
        private readonly List<OptimizeItem> _allSourceItems = [];

        // UI 绑定：正常优化列表
        [ObservableProperty]
        private ObservableCollection<OptimizeItem> _coreItems = [];

        // UI 绑定：怀旧/避坑列表
        [ObservableProperty]
        private ObservableCollection<OptimizeItem> _legacyItems = [];

        // UI 绑定：脚本/命令列表
        [ObservableProperty]
        private ObservableCollection<OptimizeItem> _scriptItems = [];

        // 搜索感知计数器 (用于 Tab 标题上的 Badge)
        [ObservableProperty]
        private int _coreCount;

        [ObservableProperty]
        private int _legacyCount;

        [ObservableProperty]
        private int _scriptCount;

        [ObservableProperty]
		private OptimizeItem? _selectedItem;

        [ObservableProperty]
        private string _searchText = "";

        // 定义编译时正则
        [GeneratedRegex(@"^\d+[、\.]")]
        private static partial Regex MyTitleRegex();

        // 静态复用，避免每次点击按钮都重新分配内存
        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

        partial void OnSearchTextChanged(string value)
        {
            ApplySearchFilter();
        }

        public MainViewModel()
		{           

            //LoadDataFromXml();
            if (System.ComponentModel.DesignerProperties.GetIsInDesignMode(new DependencyObject())) return;
            LoadAllData();
        }
        // === 核心改动：统一入口，分两个文件读取 ===
        private void LoadAllData()
        {
            try
            {
                _allSourceItems.Clear();

                // 读取主数据 (CyanSight.xml) -> 标记为 Normal
                LoadFile("Data.xml", ItemType.Normal);

                // 读取怀旧数据 (Legacy.xml) -> 标记为 Legacy
                LoadFile("Legacy.xml", ItemType.Legacy);

                // 读取脚本数据 (Script.xml) -> 标记为 Script
                LoadFile("Script.xml", ItemType.Script);

                // 初始渲染 
                ApplySearchFilter();
            }
            catch (Exception ex)
            {
                MessageBox.Show("数据加载异常: " + ex.Message);
            }
        }

        // === 通用文件读取方法 ===
        private void LoadFile(string fileName, ItemType targetType)
        {
            // 获取当前程序集
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();

            // 构建资源名称
            // ⚠️ 注意：资源名称的格式通常是 "命名空间.文件夹名.文件名"
            string resourceName = $"CyanSight.Assets.{fileName}";

            try
            {
                // 从程序集中获取文件流
                using Stream? stream = assembly.GetManifestResourceStream(resourceName);
                
                if (stream == null)
                {
                    // 调试技巧：如果这里报错，说明资源名不对。
                    // 可以在这里打断点，运行 string[] names = assembly.GetManifestResourceNames(); 查看真实名字。
                    //System.Diagnostics.Debug.WriteLine($"❌ 错误：未找到嵌入资源 '{resourceName}'。请检查文件属性是否设为 Embedded Resource。");
                    return;
                }

                // 直接从流加载 XML (XDocument 支持从 Stream 加载)
                var doc = XDocument.Load(stream);

                foreach (var config in doc.Descendants("Configuration"))
                    {
                        string category = config.Attribute("category")?.Value ?? "General";

                        foreach (var element in config.Descendants("Item"))
                        {
                            var item = new OptimizeItem
                            {
                                Id = Guid.NewGuid().ToString(),
                                Title = element.Attribute("name")?.Value ?? "未命名",
                                Category = category,

                                // 调用辅助方法，根据 Category 分配不同的图标
                                Icon = GetIconByCategory(category),

                                // 这里直接根据传入的参数赋值，不看 XML 属性
                                Type = targetType,

                                // === 如果是 Legacy，直接禁用 UI (只读模式) ===
                                IsEnabled = (targetType == ItemType.Normal),

                                RawTags = element.Element("Tags")?.Value ?? "",
                                // 描述处理逻辑保持不变
                                Description = ""
                            };

                            // 解析 <StatusChecks> 
                            var statusChecksNode = element.Element("StatusChecks");
                            if (statusChecksNode != null)
                            {
                                foreach (var check in statusChecksNode.Elements("Check"))
                                {
                                    item.StatusChecks.Add(new RegCheckRule
                                    {
                                        Key = check.Attribute("Key")?.Value ?? "",
                                        ValueName = check.Attribute("Value")?.Value ?? "",
                                        TargetData = check.Attribute("Data")?.Value ?? ""
                                    });
                                }
                            }

                            // 解析 <Optimize> 指令
                            var optimizeNode = element.Element("Optimize");
                            if (optimizeNode != null)
                            {
                                // 解析 RegWrite
                                foreach (var cmd in optimizeNode.Elements("RegWrite"))
                                {
                                    item.OptimizeCommands.Add(new RegCommand
                                    {
                                        Type = CommandType.Write,
                                        FullKeyPath = cmd.Attribute("Key")?.Value ?? "",
                                        ValueName = cmd.Attribute("Value")?.Value ?? "",
                                        ValueKind = cmd.Attribute("Type")?.Value ?? "REG_DWORD",
                                        Data = cmd.Attribute("Data")?.Value ?? ""
                                    });
                                }
                                // 解析 <Cmd>
                                foreach (var cmd in optimizeNode.Elements("Cmd"))
                                {
                                    item.OptimizeCommands.Add(new RegCommand
                                    {
                                        Type = CommandType.Cmd,
                                        Data = cmd.Value?.Trim() ?? "", // 读取标签中间的文本

                                        // 借用 ValueKind 属性来存储脚本类型 (cmd vs ps)
                                        // 如果 XML 里没写 type，默认视为 "cmd"
                                        ValueKind = cmd.Attribute("type")?.Value ?? "cmd"
                                    });
                                }
                            }

                            // 4. 解析 <Restore> 指令
                            var restoreNode = element.Element("Restore");
                            if (restoreNode != null)
                            {
                                // 支持 RegDelete
                                foreach (var cmd in restoreNode.Elements("RegDelete"))
                                {
                                    item.RestoreCommands.Add(new RegCommand
                                    {
                                        Type = CommandType.Delete,
                                        FullKeyPath = cmd.Attribute("Key")?.Value ?? "",
                                        ValueName = cmd.Attribute("Value")?.Value ?? ""
                                    });
                                }
                                // 支持 RegWrite (有些还原操作是写回默认值)
                                foreach (var cmd in restoreNode.Elements("RegWrite"))
                                {
                                    item.RestoreCommands.Add(new RegCommand
                                    {
                                        Type = CommandType.Write,
                                        FullKeyPath = cmd.Attribute("Key")?.Value ?? "",
                                        ValueName = cmd.Attribute("Value")?.Value ?? "",
                                        ValueKind = cmd.Attribute("Type")?.Value ?? "REG_DWORD",
                                        Data = cmd.Attribute("Data")?.Value ?? ""
                                    });
                                }
                                // 解析 <Cmd>
                                foreach (var cmd in restoreNode.Elements("Cmd"))
                                {
                                    item.RestoreCommands.Add(new RegCommand
                                    {
                                        Type = CommandType.Cmd,
                                        Data = cmd.Value?.Trim() ?? "",
                                        ValueKind = cmd.Attribute("type")?.Value ?? "cmd"
                                    });
                                }
                            }

                            // [重构] 描述与技术细节分离逻辑
                            // 1. 先生成技术细节表格，存入新属性 TechDetails
                            item.TechDetails = GenerateTechDetails(item);

                            // 2. 处理描述文案 Description (只保留文字描述)
                            string? customDesc = element.Element("Description")?.Value;
                            if (!string.IsNullOrEmpty(customDesc))
                            {
                                customDesc = customDesc.Replace("\r\n", "\n").Replace("\r", "\n");
                                var lines = customDesc.Split('\n');
                                var cleanLines = lines.Select(line => line.TrimStart());
                                customDesc = string.Join("\n", cleanLines);

                                // 如果 XML 里写了占位符，直接替换为空（因为我们会在 UI 上把 TechDetails 放在下面）
                                if (customDesc.Contains("{AutoDetails}"))
                                    item.Description = customDesc.Replace("{AutoDetails}", "");
                                else
                                    item.Description = customDesc;
                            }
                            else
                            {
                                item.Description = GenerateAutoDescription(item);
                            }

                            // ... 后续代码 (构建索引等) ...
                            //// 处理描述文案
                            //string? customDesc = element.Element("Description")?.Value;
                            //if (!string.IsNullOrEmpty(customDesc))
                            //{
                            //    // 统一换行符，防止 \r\n 造成干扰
                            //    customDesc = customDesc.Replace("\r\n", "\n").Replace("\r", "\n");

                            //    // 按行分割，但【保留空行】(去掉 StringSplitOptions.RemoveEmptyEntries)
                            //    var lines = customDesc.Split('\n');

                            //    // 只去除每行前面的缩进空格 (TrimStart)，保留行尾空格 (Markdown换行需要行尾空格)
                            //    var cleanLines = lines.Select(line => line.TrimStart());

                            //    // 重新组合
                            //    customDesc = string.Join("\n", cleanLines);

                            //    if (customDesc.Contains("{AutoDetails}"))
                            //        item.Description = customDesc.Replace("{AutoDetails}", GenerateTechDetails(item));
                            //    else
                            //        item.Description = customDesc;
                            //}
                            //else
                            //{
                            //    item.Description = GenerateAutoDescription(item);
                            //}

                            // 构建搜索索引
                            item.BuildSearchIndex();

                            // 检查系统状态 (Legacy 项目也可以检查状态，告知用户是否“不幸”开启了该功能)
                            item.IsSelected = RegistryHelper.CheckAll(item.StatusChecks);

                            // 绑定事件
                            item.PropertyChanged += Item_PropertyChanged;

                            // 加入总池子
                            _allSourceItems.Add(item);
                        }
                    }                
            }
            catch (Exception ex)
            {
                // 可以只记录日志，防止一个文件坏了导致另一个文件也读不出来
                System.Diagnostics.Debug.WriteLine($"文件 {fileName} 解析失败: {ex.Message}");
                // 如果想让用户知道错误，可以取消下面这行的注释
                MessageBox.Show($"加载内置数据错误: {resourceName}\n{ex.Message}");
            }
        }

        // === 搜索与分流逻辑 ===
        private void ApplySearchFilter()
        {
            // 全局搜索 (不管它是哪个文件的，只要匹配关键词就捞出来)
            var filtered = string.IsNullOrWhiteSpace(SearchText)
                ? _allSourceItems
               : [.. _allSourceItems.Where(i => i.Matches(SearchText))];

            // 分流到三个 UI 列表
            List<OptimizeItem> core = [.. filtered.Where(i => i.Type == ItemType.Normal)];
            List<OptimizeItem> legacy = [.. filtered.Where(i => i.Type == ItemType.Legacy)];
            List<OptimizeItem> script = [.. filtered.Where(i => i.Type == ItemType.Script)];

            UpdateCollection(CoreItems, core);
            UpdateCollection(LegacyItems, legacy);
            UpdateCollection(ScriptItems, script); 

            // 更新计数器 (UI 会自动收到通知)
            CoreCount = core.Count;
            LegacyCount = legacy.Count;
            ScriptCount = script.Count;

        }

        private static void UpdateCollection(ObservableCollection<OptimizeItem> collection, IEnumerable<OptimizeItem> newItems)
        {
            collection.Clear();
            foreach (var item in newItems) collection.Add(item);
        }

        [RelayCommand]
        private async Task ApplyChanges()
        {
            // 再次确认 (防误触的最后一道防线)
            var result = MessageBox.Show(
                "您即将应用当前的优化设置。\n\n" +
                "• 选中的项目将被【执行优化】\n" +
                "• 未选中的项目将被【还原默认】\n\n" +
                "是否继续？",
                "应用确认",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            // 批量执行
            int successCount = 0;
            bool needRestart = false;

            await Task.Run(() =>
            {
                foreach (var item in _allSourceItems)
                {
                    // === 博物馆模式拦截器 ===
                    // 如果是 Legacy 类型，彻底跳过！
                    // 无论它现在的 IsSelected 是 true 还是 false，都只当它是空气。
                    // 它只能看（在列表中显示状态），不能摸（UI已禁用），不能用（此处拦截）。
                    if (item.Type == ItemType.Legacy || item.Type == ItemType.Script)
                    {
                        continue;
                    }

                    try
                    {
                        if (item.IsSelected)
                        {
                            RegistryHelper.ExecuteCommands(item.OptimizeCommands);
                        }
                        else
                        {
                            if (item.Type == ItemType.Legacy)
                            {
                                continue; // 彻底跳过后续计数和重启检测
                            }

                            RegistryHelper.ExecuteCommands(item.RestoreCommands);
                        }
                        successCount++;

                        if (item.Category.Contains("explorer", StringComparison.OrdinalIgnoreCase))
                        {
                            needRestart = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        // 记录日志...
                        System.Diagnostics.Debug.WriteLine($"Item {item.Title} failed: {ex.Message}");
                    }
                }
            });

            // 3. 完成提示
            if (needRestart)
            {
                var restart = MessageBox.Show(
                    $"应用完成！共处理 {successCount} 个项目。\n检测到涉及资源管理器的更改，是否立即重启 Explorer 使其生效？",
                    "操作成功",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (restart == MessageBoxResult.Yes)
                {
                    RegistryHelper.RestartExplorer();
                }
            }
            else
            {
                MessageBox.Show($"应用完成！所有设置已生效。", "操作成功");
            }
        }

        // === 安全配置系统 ===
        [RelayCommand]
        private void ExportProfile()
        {
            // 核心逻辑：只保存 "ID/Title" 和 "状态"。
            // 即使以后你 XML 加了新功能，或者调整了顺序，这个配置依然能准确找到原来的项目。
            var profile = new Dictionary<string, bool>();

            foreach (var item in _allSourceItems)
            {
                // 使用 Title 作为唯一标识符 (前提是你 XML 里 name 不重复)
                // 也可以用 item.Id (如果你 XML 里有固定 ID)
                profile[item.Title] = item.IsSelected;
            }

            // 序列化为 JSON 字符串
            string json = JsonSerializer.Serialize(profile, _jsonOptions);

            // 保存文件
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "配置文件 (*.json)|*.json",
                FileName = "MyOptimizationProfile.json"
            };

            if (dialog.ShowDialog() == true)
            {
                File.WriteAllText(dialog.FileName, json);
                MessageBox.Show("配置已导出！您可以安全地在其他电脑上使用此文件。", "导出成功");
            }
        }

		[RelayCommand]
		private void ImportProfile()
		{
			var dialog = new Microsoft.Win32.OpenFileDialog
			{
				Filter = "配置文件 (*.json)|*.json"
			};

			if (dialog.ShowDialog() == true)
			{
				try
				{
					string json = File.ReadAllText(dialog.FileName);
					var profile = JsonSerializer.Deserialize<Dictionary<string, bool>>(json);

					if (profile == null) return;

					int matchCount = 0;

					// 核心安全逻辑：
					// 不按顺序套用，而是拿名字去列表里找。找不到的就忽略，绝不会错位。
					foreach (var kvp in profile)
					{
						var targetItem = _allSourceItems.FirstOrDefault(i => i.Title == kvp.Key);
						if (targetItem != null)
						{
							targetItem.IsSelected = kvp.Value;
							matchCount++;
						}
					}

					MessageBox.Show($"配置导入成功！\n成功匹配并更新了 {matchCount} 个选项。\n\n请点击【立即应用】以生效。", "导入成功");
				}
				catch (Exception ex)

				{
					MessageBox.Show("配置文件格式错误或已损坏。\n" + ex.Message, "导入失败", MessageBoxButton.OK, MessageBoxImage.Error);
				}
			}
		}

        // 复制注册表路径
        [RelayCommand]
        private void CopyRegKey()
        {
            // 安全检查：确保当前有选中项，且包含至少一条指令
            if (SelectedItem == null || SelectedItem.OptimizeCommands.Count == 0) return;

            // 获取第一条指令的 Key (通常优化项的主要路径都在第一条)
            string keyPath = SelectedItem.OptimizeCommands[0].FullKeyPath;

            if (!string.IsNullOrEmpty(keyPath))
            {
                Clipboard.SetText(keyPath);
                // MessageBox.Show("注册表路径已复制！", "提示"); // 可选：嫌弹窗烦可以注释掉
            }
        }
        // 复制脚本内容到剪贴板
        [RelayCommand]
        private void CopyScript()
        {
            // 防御性编程：防止空引用
            if (SelectedItem == null || SelectedItem.OptimizeCommands.Count == 0) return;

            // 获取第一条命令的内容
            string cmdData = SelectedItem.OptimizeCommands[0].Data;

            // 写入剪贴板
            Clipboard.SetText(cmdData);
        }

        // 尝试直接运行
        [RelayCommand]
        private void RunScript()
        {
            if (SelectedItem == null) return;

            var cmd = SelectedItem.OptimizeCommands.FirstOrDefault();
            if (cmd == null) return;

            try
            {
                // 简单的运行逻辑，根据类型决定启动 powershell 还是 cmd
                string fileName = (cmd.ValueKind == "ps") ? "powershell.exe" : "cmd.exe";
                string arguments = (cmd.ValueKind == "ps")
                    ? $"-NoExit -Command \"{cmd.Data}\""  // -NoExit 让窗口执行完不关闭，方便看结果
                    : $"/k \"{cmd.Data}\"";               // /k 同理

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = true // 必须为 true 才能弹出新窗口
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("无法启动终端: " + ex.Message);
            }
        }

        // 事件处理逻辑抽离出来
        private void Item_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
		{
            // 仅仅是监听变化，用于更新 UI 状态（比如让“应用”按钮变亮），但绝对不执行 RegistryHelper
            if (e.PropertyName == nameof(OptimizeItem.IsSelected))
            {
                // 可以在这里标记一个 IsDirty = true，提示用户有未保存的更改
                // 但不要调用 RegistryHelper.ExecuteCommands
            }
        }

        private static string GetIconByCategory(string category)
        {
            return category.ToLowerInvariant() switch
            {
                "explorer" => "\uE8B7", // 📂 文件夹/资源管理器
                "system" => "\uE770",   // ⚙️ CPU/系统芯片
                "network" => "\uE774",  // 🌐 [新增] 网络/地球仪图标
                "privacy" => "\uE72E",  // 🔒 隐私/锁
                _ => "\uE9D9"           // 🛠️ 默认/工具箱
            };
        }

        // 辅助方法：拆分 "HKEY_CU\Software\..." 为 "HKEY_CU" 和 "Software\..."
        private static (string root, string path) ParseRegistryPath(string fullPath)
		{
			if (string.IsNullOrEmpty(fullPath)) return ("", "");

			int firstSlash = fullPath.IndexOf('\\');
			if (firstSlash == -1) return (fullPath, "");

			string root = fullPath[..firstSlash];
			string path = fullPath[(firstSlash + 1)..];
			return (root, path);
		}

        // 生成纯技术细节的部分（供占位符使用）
        private static string GenerateTechDetails(OptimizeItem item)
        {
            var sb = new StringBuilder();

            // 智能检测：是否为单路径模式？
            // 如果优化和还原指令都指向同一个 RootKey，我们就认为它是“单路径”
            // 在单路径模式下，Markdown 里不再重复打印 "📂 位置: ... "，避免冗余
            var allCmds = item.OptimizeCommands.Concat(item.RestoreCommands).ToList();
            var uniqueKeys = allCmds
                .Where(c => c.Type != CommandType.Cmd && !string.IsNullOrEmpty(c.FullKeyPath))
                .Select(c => c.FullKeyPath)
                .Distinct()
                .ToList();

            bool isSingleKeyMode = (uniqueKeys.Count <= 1);

            // 辅助本地函数：转换注册表类型名称
            string GetShortType(string kind) => kind switch
            {
                "REG_DWORD"  => "DWORD(32-bit)值",
                "REG_QWORD"  => "QWORD(64-bit)值",
                "REG_SZ"     => "字符串值",
                "REG_BINARY" => "二进制值",
                _ => "kind" // 其他情况直接显示原名
            };

            // 辅助函数：生成美化的操作表格
            void AppendCommandTable(IEnumerable<RegCommand> commands, string titleEmoji, string titleText)
            {
                if (commands == null || !commands.Any()) return;

                sb.AppendLine($"### {titleEmoji} {titleText}\n");

                // 分离 CMD 和 注册表操作
                var cmdCommands = commands.Where(c => c.Type == CommandType.Cmd).ToList();
                var regCommands = commands.Where(c => c.Type != CommandType.Cmd).ToList();

                // 1. 先列出 CMD 命令
                if (cmdCommands.Count > 0)
                {
                    sb.AppendLine("> 💻 **命令执行**\n");
                    foreach (var cmd in cmdCommands)
                    {
                        sb.AppendLine($"- `{cmd.Data}`");
                    }
                    sb.AppendLine();
                }

                // 2. 再列出注册表操作
                if (regCommands.Count > 0)
                {
                    var groupedCommands = regCommands.GroupBy(c => c.FullKeyPath);
                    foreach (var group in groupedCommands)
                    {
                        if (!isSingleKeyMode)
                        {
                            sb.AppendLine($"> 📂 **位置**: `{group.Key}`\n");
                        }
                        // 调整表格定义
                        // - 移除 "键名 (Key)" 中的英文，缩短表头
                        // - 关键：将对齐方式全部改为左对齐 (:---)，强制列宽收缩
                        sb.AppendLine("| 操作 | 类型 | 键名 | 数据 |");
                        sb.AppendLine("| :---: | :---: | :---: | :---: |");
                        foreach (var cmd in group)
                        {
                            string actionIcon = cmd.Type == CommandType.Write ? "📝写入" : "🗑️删除";
                            string dataVal = cmd.Type == CommandType.Write ? $"`{cmd.Data}`" : "--";
                            sb.AppendLine($"| {actionIcon} | {GetShortType(cmd.ValueKind)} | **{cmd.ValueName}** | {dataVal} |");
                        }
                        sb.AppendLine();
                    }
                }             
            }

            // --- 构建输出 ---

            // 1. 优化逻辑 (应用修改)
            AppendCommandTable(item.OptimizeCommands, "🚀", "执行优化 (改动内容)");

            // 2. 视觉分割线
            sb.AppendLine("---\n");

            // 3. 恢复逻辑 (撤销修改)
            if (item.RestoreCommands != null && item.RestoreCommands.Count > 0)
            {
                // 既然是查看详情，用户通常更关注“优化了什么”，恢复逻辑可以折叠或者简化，
                // 但为了清晰，这里依然保持结构化，但标题区分。
                AppendCommandTable(item.RestoreCommands, "↩️", "还原逻辑 (用于回滚)");
            }
            else
            {
                sb.AppendLine("### ↩️ 还原逻辑\n");
                sb.AppendLine("*此项目未定义特定还原指令（可能通过删除新建项即可还原）*");
            }

            // 4. 特殊警告 (资源管理器重启)
            if (item.Category.Contains("explorer", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine("\n> ⚠️ **注意**：执行此操作会自动重启资源管理器 (Explorer.exe)，屏幕可能会短暂闪烁。");
            }

            return sb.ToString();
        }

        // 原来的生成方法（作为兜底）
        private static string GenerateAutoDescription(OptimizeItem item)
		{
			var sb = new StringBuilder();
            // 自动生成标题
            string cleanTitle = MyTitleRegex().Replace(item.Title, "");
            sb.AppendLine($"# {cleanTitle}");
			sb.AppendLine($"> 🏷️ **分类**: {item.Category}");
			sb.AppendLine();
			sb.AppendLine("## 📝 功能概述");
			sb.AppendLine("此选项由自动生成。启用它将修改系统配置以达到优化目的。");
			sb.AppendLine();

			//// 追加技术细节
			//sb.AppendLine(GenerateTechDetails(item));

			return sb.ToString();
		}

        // 恢复默认与预设脚本

        /// <summary>
        /// 恢复默认：
        /// 重新根据 Data.xml 检查一遍系统状态，符合的勾选上。
        /// 用于用户在修改勾选后后悔了，想重置回当前系统的实际状态。
        /// </summary>
        [RelayCommand]
        private void RestoreDefault()
        {
            try
            {
                // 1. 提示用户
                // 只有当存在"脏数据"（即用户改动了勾选但还没应用）时，这个按钮才有意义
                // 这里直接执行，作为“刷新”功能使用

                int checkedCount = 0;

                // 2. 重新扫描逻辑
                foreach (var item in _allSourceItems)
                {
                    // 修复点：使用新的 CheckAll 逻辑，替代旧的单项检查
                    bool isSystemOptimized = RegistryHelper.CheckAll(item.StatusChecks);

                    item.IsSelected = isSystemOptimized;
                    if (isSystemOptimized) checkedCount++;
                }

                MessageBox.Show(
                    $"状态已刷新！\n\n" +
                    $"- {checkedCount} 个项目已优化\n" +
                    $"- {_allSourceItems.Count - checkedCount} 个项目未优化",
                    "刷新完成");
            }
            catch (Exception ex)
            {
                MessageBox.Show("刷新失败: " + ex.Message);
            }
        }

        /// <summary>
        /// 应用预设配置：下拉菜单的逻辑
        /// </summary>
        /// <param name="presetType">XAML 传来的参数: Safe, Gaming, Extreme</param>
        [RelayCommand]
        private void ApplyPreset(string? presetType)
        {
            if (string.IsNullOrEmpty(presetType) || _allSourceItems.Count == 0) return;

            string presetNameCN = "";
            int count = 0;

            // 先全部取消勾选（重置状态）
            foreach (var item in _allSourceItems) item.IsSelected = false;

            // 遍历并根据策略勾选
            foreach (var item in _allSourceItems)
            {
                bool shouldSelect = false;

                switch (presetType)
                {
                   
                    case "AllSelect":
                        presetNameCN = "全选";
                        // 策略：全选常规项, 避开怀旧项
                        if (item.Type == ItemType.Normal)
                        {
                            shouldSelect = true;
                        }
                        break;

                    case "AllNotSelect":
                        presetNameCN = "清除";
                        shouldSelect = false; // 保持全不选
                        break;

                    case "surfacego2": // 场景：通用/办公笔记本
                        presetNameCN = "Surface Go 2";
                      
                        var surfacego2List = new HashSet<string>
                        {
                            "禁用处理器的幽灵和熔断补丁",
                            "禁用SysMain与预读取",
                            "关闭快速启动",
                            "弹出U盘后彻底断开其电源",
                            "关闭系统自动调试功能",
                            "消除登录脚本延迟",
                            "启用更新重启后自动登录",
                            "禁用软盘服务"                           
                        };

                        if (surfacego2List.Contains(item.Title)) shouldSelect = true;
                        break;


                    
                }

                // 执行勾选
                if (shouldSelect)
                {
                    item.IsSelected = true;
                    count++;
                }
            }

            MessageBox.Show(
                $"已加载【{presetNameCN}】预设方案。\n" +
                $"共选中了 {count} 个项目。\n\n" +
                "请检查列表，确认无误后点击右下角的【立即应用更改】。",
                "预设加载完毕");
        }
    }
}