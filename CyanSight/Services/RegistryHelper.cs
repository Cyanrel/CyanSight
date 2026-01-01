using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using CyanSight.Models;
using System.Security.Principal;
using System.Runtime.Versioning;
using System.Linq;

namespace CyanSight.Services
{
    [SupportedOSPlatform("windows")]
    public static class RegistryHelper
    {
        public static bool IsOptimized(string root, string path, string valueName, string targetData)
        {
            try
            {
                // === 智能修复：如果传入的 root 是空的，尝试从 path 里重新解析 ===
                if (string.IsNullOrWhiteSpace(root))
                {
                    // 假设 path 实际上是全路径 (例如 "HKEY_LOCAL_MACHINE\SYSTEM\...")
                    var result = ParseRegistryPath(path);
                    if (!string.IsNullOrEmpty(result.root))
                    {
                        root = result.root;
                        path = result.path;
                    }
                }

                // 1. 获取根键 (带详细日志)
                using RegistryKey? baseKey = GetBaseKey(root);
                if (baseKey == null)
                {
                    Debug.WriteLine($"[检测失败] 无法识别根键: '{root}'");
                    return false;
                }

                // 2. 打开子键
                using var key = baseKey.OpenSubKey(path, false);
                if (key == null)
                {
                    // 路径不存在视为未优化
                    return false;
                }

                // 3. 读取值
                object? val = key.GetValue(valueName);
                if (val == null) return false;

                // 4. 对比值 (去除首尾空格，忽略大小写)
                string valStr = val.ToString()?.Trim() ?? "";
                string cleanTarget = targetData?.Trim() ?? "";
                              
                return string.Equals(valStr, cleanTarget, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[检测异常] {ex.Message}");
                return false;
            }
        }

        public static bool CheckAll(List<RegCheckRule> rules)
        {
            if (rules == null || rules.Count == 0) return false;

            foreach (var rule in rules)
            {
                if (!IsOptimized(rule.RootKey, rule.Path, rule.ValueName, rule.TargetData))
                {
                    return false;
                }
            }
            return true;
        }

        public static void ExecuteCommands(List<RegCommand> commands)
        {
            if (commands == null) return;

            foreach (var cmd in commands)
            {
                try
                {
                    // 如果是 CMD 命令，直接执行，跳过注册表解析
                    if (cmd.Type == CommandType.Cmd)
                    {
                        ExecuteCmd(cmd.Data);
                        continue; // 处理完直接进入下一次循环
                    }

                    // --- 以下是注册表操作逻辑 ---

                    // 1. 解析路径 (只有注册表操作才需要这一步)
                    var (root, path) = ParseRegistryPath(cmd.FullKeyPath);

                    // 2. 获取根键
                    using RegistryKey? baseKey = GetBaseKey(root);
                    if (baseKey == null)
                    {
                        Debug.WriteLine($"[执行跳过] 无效的注册表根键: {cmd.FullKeyPath}");
                        continue;
                    }
                    // 3. 执行写入
                    if (cmd.Type == CommandType.Write)
                    {
                        using var key = baseKey.CreateSubKey(path, true); // true = 可写权限
                        if (key != null)
                        {
                            object dataToWrite = ParseData(cmd.ValueKind, cmd.Data);
                            RegistryValueKind kind = GetValueKind(cmd.ValueKind);
                            key.SetValue(cmd.ValueName, dataToWrite, kind);
                        }
                    }
                    // 4. 执行删除
                    else if (cmd.Type == CommandType.Delete)
                    {
                        using var key = baseKey.OpenSubKey(path, true); // true = 可写权限
                        if (key != null)
                        {
                            try { key.DeleteValue(cmd.ValueName); } catch { }
                        }
                    }

                   

                }
                catch (Exception ex)
                {
                    // 建议在调试期把这个弹窗打开，方便抓 Bug
                    System.Windows.MessageBox.Show($"执行失败:\n{ex.Message}");
                    Debug.WriteLine($"[执行异常] {ex.Message}");


                    //    // 将 Debug.WriteLine 改为 MessageBox，或者在输出窗口仔细看日志
                    //    Debug.WriteLine($"[写入失败] {ex.Message}");

                    //    // 🐛 调试期建议取消下面这行的注释，这样你就能看到哪里出错了
                    //    System.Windows.MessageBox.Show($"写入注册表失败:\n{cmd.FullKeyPath}\n\n错误信息:\n{ex.Message}");
                }
            }
        }

        // === 提取出来的 CMD 执行方法 ===
        private static void ExecuteCmd(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return;

            try
            {
                // 构建参数：/c 后面跟上用引号包裹的完整命令，防止内部引号打架
                // 例如：cmd.exe /c "你的命令"
                string args = $"/c \"{command}\"";

                var processInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = args,
                    CreateNoWindow = true,          // 隐藏黑框
                    UseShellExecute = false,        // 必须为 false
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using var process = Process.Start(processInfo);
                process?.WaitForExit(); // 等待执行完毕
            }
            catch (Exception ex)
            {
                // bug调试期可以启用下面这行，看看具体报什么错
                System.Windows.MessageBox.Show($"CMD启动失败: {ex.Message}");

                Debug.WriteLine($"[CMD执行失败] {ex.Message}");
            }
        }

        // === 辅助方法 ===

        private static RegistryKey? GetBaseKey(string root)
        {
            // 清理 root 字符串
            string cleanRoot = root?.Trim().TrimStart('\\').ToUpper() ?? "";

            var view = RegistryView.Registry64;

            // === 核心修改：针对 HKCU 的用户上下文重定向 ===
            if (cleanRoot == "HKCU" || cleanRoot == "HKEY_CURRENT_USER")
            {
                try
                {
                    // 1. 尝试在 HKEY_USERS 中找到当前登录用户的 SID
                    using var usersKey = RegistryKey.OpenBaseKey(RegistryHive.Users, view);
                    var subKeyNames = usersKey.GetSubKeyNames();

                    // 寻找以 "S-1-5-21" 开头(标准用户) 且 不以 "_Classes" 结尾的项
                    string? userSid = subKeyNames.FirstOrDefault(s =>
                        s.StartsWith("S-1-5-21", StringComparison.OrdinalIgnoreCase) &&
                        !s.EndsWith("_Classes", StringComparison.OrdinalIgnoreCase));

                    if (!string.IsNullOrEmpty(userSid))
                    {
                        // 必须加 true，表示以【可写】方式打开！
                        return usersKey.OpenSubKey(userSid, true);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[用户上下文获取失败] 回退到默认 HKCU: {ex.Message}");
                }

                // 如果找不到 SID，回退到标准的 CurrentUser
                return RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view);
            }

            // === 其他标准根键 ===
            return cleanRoot switch
            {
                "HKCR" or "HKEY_CLASSES_ROOT" => RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, view),
                // HKCU 已经在上面处理了，这里留个 fallback
                "HKLM" or "HKEY_LOCAL_MACHINE" => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view),
                "HKU" or "HKEY_USERS" => RegistryKey.OpenBaseKey(RegistryHive.Users, view),
                "HKCC" or "HKEY_CURRENT_CONFIG" => RegistryKey.OpenBaseKey(RegistryHive.CurrentConfig, view),
                _ => null
            };        
        }

        public static (string root, string path) ParseRegistryPath(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath)) return ("", "");

            // === 去除首尾空格和首部的反斜杠 ===
            // 防止出现 "\HKEY_LOCAL_MACHINE" 导致 index=0 的情况
            fullPath = fullPath.Trim().TrimStart('\\');

            int index = fullPath.IndexOf('\\');

            // 如果找不到斜杠，或者斜杠在开头（虽然TrimStart处理了，防万一），则无法解析
            if (index < 1) return (fullPath, "");

            string root = fullPath.Substring(0, index).Trim();
            string path = fullPath.Substring(index + 1).Trim();

            return (root, path);
        }

        private static object ParseData(string kind, object? dataObj)
        {
            string data = dataObj?.ToString()?.Trim() ?? "";

            return kind.ToUpper().Trim() switch
            {
                "REG_DWORD" => int.TryParse(data, out int i) ? i : 0,
                "REG_QWORD" => long.TryParse(data, out long l) ? l : 0L,
                "REG_BINARY" => new byte[0],
                _ => data
            };
        }

        private static RegistryValueKind GetValueKind(string kind)
        {
            return kind.ToUpper().Trim() switch
            {
                "REG_DWORD" => RegistryValueKind.DWord,
                "REG_QWORD" => RegistryValueKind.QWord,
                "REG_BINARY" => RegistryValueKind.Binary,
                "REG_EXPAND_SZ" => RegistryValueKind.ExpandString,
                "REG_MULTI_SZ" => RegistryValueKind.MultiString,
                _ => RegistryValueKind.String
            };
        }

        public static void RestartExplorer()
        {
            try
            {
                foreach (var proc in Process.GetProcessesByName("explorer"))
                {
                    proc.Kill();
                }
            }
            catch { }
        }
    }
}