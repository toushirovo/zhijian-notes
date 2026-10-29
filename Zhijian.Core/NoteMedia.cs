using System.Text.RegularExpressions;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
namespace Zhijian.Core;
public static class NoteMedia
{
    public const int MaxImageBytes = 10 * 1024 * 1024;
    public static bool IsAsset(string url) => Regex.IsMatch(url, @"^assets/[0-9a-f]{64}\.(png|jpg)$", RegexOptions.CultureInvariant);
    public static string[] Assets(string body) => MarkdownLinks.Parse(body).Descendants<LinkInline>().Where(l => l.IsImage && l.Url != null && IsAsset(l.Url)).Select(l => l.Url!).Distinct().ToArray();
    public static string ImageExtension(byte[] bytes)
    {
        if (bytes.Length == 0 || bytes.Length > MaxImageBytes) throw new InvalidDataException("图片须小于或等于10 MiB。");
        if (bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return ".png";
        if (bytes.Length > 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255) return ".jpg";
        throw new InvalidDataException("只支持有效的PNG和JPEG图片。");
    }
    public static byte[] ReadAsset(string root, string url)
    {
        if (!IsAsset(url)) throw new InvalidDataException("图片不在工作区附件中。");
        var path = Disk.SafeFile(root, url);
        if (new FileInfo(path).Length > MaxImageBytes) throw new InvalidDataException("图片超过10 MiB。");
        var bytes = File.ReadAllBytes(path);
        var ext = ImageExtension(bytes);
        if (url != "assets/" + Disk.Hash(bytes) + ext) throw new InvalidDataException("图片校验失败，请从备份恢复。");
        return bytes;
    }
}
public sealed partial class Workspace
{
    public string AddImage(byte[] bytes)
    {
        lock (gate)
        {
            Ensure(); var ext = NoteMedia.ImageExtension(bytes); var url = "assets/" + Disk.Hash(bytes) + ext;
            var dir = Disk.SafeFile(Path, "assets"); Directory.CreateDirectory(dir);
            var file = Disk.SafeFile(Path, url);
            if (File.Exists(file)) NoteMedia.ReadAsset(Path, url); else Disk.Durable(file, bytes);
            return url;
        }
    }
    public byte[] ReadImage(string url) { lock (gate) { Ensure(); return NoteMedia.ReadAsset(Path, url); } }
    public void ExportImages(string body, string destination)
    {
        lock (gate)
        {
            Ensure(); destination = Disk.Root(destination);
            if (Disk.Inside(destination, Path)) throw new IOException("图片输出位置不能在当前工作区内。");
            foreach (var url in NoteMedia.Assets(body))
            {
                var bytes = NoteMedia.ReadAsset(Path, url); var file = Disk.SafeFile(destination, url);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
                if (File.Exists(file)) { if (!File.ReadAllBytes(file).SequenceEqual(bytes)) throw new IOException("目标存在不同内容的同名附件，请另选目录。"); }
                else Disk.Durable(file, bytes);
            }
        }
    }
    private string ImportImages(string body, string markdownPath, List<string> warnings)
    {
        var root = Disk.Root(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(markdownPath))!);
        return MarkdownLinks.Rewrite(body, url =>
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out _)) { warnings.Add("远程或绝对路径图片未导入，预览不会加载"); return null; }
            try
            {
                var relative = Uri.UnescapeDataString(url).Replace('\\', '/');
                while (relative.StartsWith("./", StringComparison.Ordinal)) relative = relative[2..];
                var file = Disk.SafeFile(root, relative);
                if (new FileInfo(file).Length > NoteMedia.MaxImageBytes) throw new InvalidDataException("超过10 MiB");
                return AddImage(File.ReadAllBytes(file));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
            { warnings.Add("图片未导入：" + url + "（" + ex.Message + "）"); return null; }
        }, images: true);
    }
}
