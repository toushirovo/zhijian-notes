using System.IO;
using System.Windows.Media.Imaging;
using Zhijian.Core;
namespace zhijian.app;
internal static class ImageCodec
{
    public static BitmapSource Decode(byte[] bytes)
    {
        NoteMedia.ImageExtension(bytes);
        using var input = new MemoryStream(bytes, false);
        var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
        var frame = decoder.Frames[0];
        if (frame.PixelWidth > 8192 || frame.PixelHeight > 8192 || (long)frame.PixelWidth * frame.PixelHeight > 24_000_000) throw new InvalidDataException("图片尺寸过大，最长边最多8192像素，总像素最多2400万。");
        input.Position = 0;
        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = input;
        if (frame.PixelWidth >= frame.PixelHeight) bitmap.DecodePixelWidth = Math.Min(1600, frame.PixelWidth); else bitmap.DecodePixelHeight = Math.Min(1600, frame.PixelHeight); bitmap.EndInit(); bitmap.Freeze(); return bitmap;
    }
    public static byte[] Png(BitmapSource bitmap)
    {
        if (bitmap.PixelWidth > 8192 || bitmap.PixelHeight > 8192 || (long)bitmap.PixelWidth * bitmap.PixelHeight > 24_000_000) throw new InvalidDataException("图片尺寸过大，请先缩小图片。");
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream(); encoder.Save(stream); var bytes = stream.ToArray(); NoteMedia.ImageExtension(bytes); return bytes;
    }
}
