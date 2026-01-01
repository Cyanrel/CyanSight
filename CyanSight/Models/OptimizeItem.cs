using CommunityToolkit.Mvvm.ComponentModel;
using System.Diagnostics; // 引入这个用于打印日志
using System.Xml.Serialization;
using System.Text;

namespace CyanSight.Models
{
    public enum ItemType
    {
        Normal,     // 正常优化
        Legacy,     // 过时/怀旧
    }
    // 对应 <StatusChecks> 里的 <Check> 标签
    public class RegCheckRule
    {
        public RegCheckRule()
        {
            // 💀 只要对象被创建，这行日志必须出现！如果不出现，说明 XML 没读到 Check 标签
            //Debug.WriteLine("💀 [生命周期] RegCheckRule 被创建了！");
        }

        private string _key = "";

        // 对应 XML: <Check Key="...">
        [XmlAttribute("Key")]
        public string Key
        {
            get => _key;
            set
            {
                _key = value;
                // 🔥 如果代码生效了，你必须在“输出”窗口看到这行字！
                // Debug.WriteLine($"🔥 [数据注入] 成功读取到 Key: '{value}'");
            }
        }

        [XmlAttribute("Value")]
        public string ValueName { get; set; } = "";

        [XmlAttribute("Data")]
        public string TargetData { get; set; } = "";

        // 自动计算属性
        [XmlIgnore]
        public string RootKey => Services.RegistryHelper.ParseRegistryPath(Key).root;

        [XmlIgnore]
        public string Path => Services.RegistryHelper.ParseRegistryPath(Key).path;
    }

    public partial class OptimizeItem : ObservableObject
    {
        [XmlAttribute("name")]
        public string Title { get; set; } = "";

        // 这个属性现在由代码逻辑在加载文件时手动赋值，不再依赖 XML 内容
        public ItemType Type { get; set; } = ItemType.Normal;

        // 手动标签 (对应 XML 的 <Tags>)
        [XmlElement("Tags")]
        public string RawTags { get; set; } = "";

        [XmlIgnore]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [XmlIgnore]
        public string Icon { get; set; } = "\uE9ca";

        [XmlElement("Description")]
        public string Description { get; set; } = "";

        [XmlAttribute("category")]
        public string Category { get; set; } = "";

        // 显式指定 Type，强行告诉 Serializer 用哪个类
        [XmlArray("StatusChecks")]
        [XmlArrayItem("Check", Type = typeof(RegCheckRule))]
        public List<RegCheckRule> StatusChecks { get; set; } = new();

        // === 执行指令 ===
        [XmlArray("Optimize")]
        [XmlArrayItem("RegWrite", Type = typeof(RegCommand))]
        [XmlArrayItem("Cmd", Type = typeof(RegCommand))]
        public List<RegCommand> OptimizeCommands { get; set; } = new();

        [XmlArray("Restore")]
        [XmlArrayItem("RegDelete", typeof(RegCommand))]
        [XmlArrayItem("RegWrite", typeof(RegCommand))] // 兼容某些还原是写入操作
        [XmlArrayItem("Cmd", typeof(RegCommand))]
        public List<RegCommand> RestoreCommands { get; set; } = new();

        [ObservableProperty]
        private bool _isSelected;

        // === 控制是否允许用户操作 ===
        // 默认为 true (可用)，但在加载时会将 Legacy 项目设为 false
        [ObservableProperty]
        private bool _isEnabled = true;

        // === 深度搜索索引 ===
        // 这个字段不存入 XML，而是在加载数据时自动生成
        [XmlIgnore]
        private string _searchIndex = "";

        /// <summary>
        /// 构建搜索索引：将标题、描述、标签、注册表路径、键名全部拼成一个字符串
        /// </summary>
        public void BuildSearchIndex()
        {
            var sb = new StringBuilder();

            // 1. 基础信息
            sb.Append(Title).Append(" ");
            sb.Append(Description).Append(" ");
            sb.Append(Category).Append(" ");
            sb.Append(RawTags).Append(" "); // XML 里的 <Tags>

            // 2. 自动吸入注册表键名 (Deep Search 核心)
            // 用户搜 "HiberbootEnabled" 或 "SearchboxTaskbarMode" 时能直接命中
            if (OptimizeCommands != null)
            {
                foreach (var cmd in OptimizeCommands)
                {
                    // 加入键名 (ValueName)
                    if (!string.IsNullOrEmpty(cmd.ValueName))
                        sb.Append(cmd.ValueName).Append(" ");

                    // 加入路径末尾 (Key Path) - 比如 ...\Explorer\Serialize
                    // 防止全路径导致搜索结果太杂，只取最后一段
                    if (!string.IsNullOrEmpty(cmd.FullKeyPath))
                    {
                        var parts = cmd.FullKeyPath.Split('\\');
                        if (parts.Length > 0)
                            sb.Append(parts.Last()).Append(" ");
                    }
                }
            }

            // 转为小写以支持忽略大小写搜索
            _searchIndex = sb.ToString().ToLowerInvariant();
        }

        /// <summary>
        /// 对外提供的搜索匹配方法
        /// </summary>
        public bool Matches(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return true;

            // 多关键词支持 (例如搜 "任务栏 搜索")
            var keywords = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return keywords.All(k => _searchIndex.Contains(k));
        }
    }
}