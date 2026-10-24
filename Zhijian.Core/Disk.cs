using System.Security.Cryptography;
using System.Text.Json;

namespace Zhijian.Core;

public static class Disk
{
    public static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    public static byte[] Serialize<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Rules.Json);
    public static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllBytes(path), Rules.Json) ?? throw new InvalidDataException($"文件为空：{path}");
    public static void Durable(string path, byte[] bytes)
    {
        using var f = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        f.Write(bytes); f.Flush(true);
    }
    public static void Atomic(string path, byte[] bytes)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        Durable(temp, bytes);
        try
        {
            if (File.Exists(path))
            {
                var protection = path + ".previous";
                File.Replace(temp, path, protection, true);
                // A cleanup failure must never turn a committed save into a reported failure.
                try { File.Delete(protection); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            else File.Move(temp, path);
        }
        finally { if (File.Exists(temp)) { try { File.Delete(temp); } catch (IOException) { } } }
    }
    public static string Root(string path)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (full.StartsWith("\\\\", StringComparison.Ordinal)) throw new IOException("请选择本机磁盘目录，暂不支持网络目录。");
        NoReparse(full);
        return full;
    }
    public static bool Inside(string child, string parent) => string.Equals(child, parent, StringComparison.OrdinalIgnoreCase) ||
        child.StartsWith(Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    public static void NoReparse(string path)
    {
        for (var p = path; !string.IsNullOrEmpty(p); p = Path.GetDirectoryName(p))
            if ((Directory.Exists(p) || File.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("不支持符号链接或目录联接，请选择普通本地目录。");
    }
    public static string SafeFile(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Contains('\\') || relative.Contains(':') || Path.IsPathRooted(relative) ||
            relative.Split('/').Any(p => p is "" or "." or "..")) throw new InvalidDataException("备份包含非法路径。");
        var result = Path.GetFullPath(Path.Combine(root, relative));
        if (!Inside(result, root)) throw new InvalidDataException("备份路径越界。");
        NoReparse(result);
        return result;
    }
}
