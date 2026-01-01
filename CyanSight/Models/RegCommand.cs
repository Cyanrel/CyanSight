using System.Xml.Serialization;

namespace CyanSight.Models
{
	public enum CommandType
	{
		Write,  // 对应 <RegWrite>
		Delete,  // 对应 <RegDelete>
		Cmd     // 执行 CMD 命令
    }

	public class RegCommand
	{
        // 必须对应 XML 里的属性名 "Key"
        [XmlAttribute("Key")]
        public string FullKeyPath { get; set; } = "";

        [XmlAttribute("Value")]
        public string ValueName { get; set; } = "";

        [XmlAttribute("Type")]
        public string ValueKind { get; set; } = "REG_SZ";

        [XmlAttribute("Data")]
        public string Data { get; set; } = "";
        public CommandType Type { get; set; }

	}
}