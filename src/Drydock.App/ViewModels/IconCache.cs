using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Drydock.App.ViewModels;

/// <summary>Item pictures from the game install, decoded once.</summary>
public static class IconCache
{
    static readonly ConcurrentDictionary<string, BitmapSource?> Loaded = new(StringComparer.OrdinalIgnoreCase);

    /// <returns>Null when there is no picture or it can't be read, so a bad file only costs the icon.</returns>
    public static BitmapSource? Load(string? path) => path == null ? null : Loaded.GetOrAdd(path, Read);

    static BitmapSource? Read(string path)
    {
        try
        {
            return Decode(File.ReadAllBytes(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    const int DdsHeader = 128, Dx10Header = 20;
    const uint RgbaUnorm = 28, RgbaSrgb = 29, BgraUnorm = 87, BgraSrgb = 91;

    /// <summary>Reads the game's icons, which are DDS files with plain 8-bit pixels despite their .png name, or a real PNG.</summary>
    public static BitmapSource? Decode(byte[] data)
    {
        try
        {
            if (data.Length > DdsHeader + Dx10Header && data.AsSpan(0, 4).SequenceEqual("DDS "u8) && data.AsSpan(84, 4).SequenceEqual("DX10"u8))
                return DecodeDds(data);
            if (data.Length > 8 && data[0] == 0x89 && data.AsSpan(1, 3).SequenceEqual("PNG"u8))
            {
                // decoded here, a lazily decoded bad file would throw while WPF draws the row
                var frame = BitmapFrame.Create(new MemoryStream(data), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                frame.Freeze();
                return frame;
            }
        }
        catch (Exception e) when (e is NotSupportedException or ArgumentException or System.Runtime.InteropServices.COMException or FileFormatException)
        {
        }
        return null;
    }

    static BitmapSource? DecodeDds(byte[] data)
    {
        int height = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(12));
        int width = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(16));
        uint format = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(DdsHeader));
        int stride = width * 4, start = DdsHeader + Dx10Header;
        if (width <= 0 || height <= 0 || width > 4096 || height > 4096 || data.Length < start + stride * height)
            return null;
        var pixels = data.AsSpan(start, stride * height).ToArray();
        if (format is RgbaUnorm or RgbaSrgb)
            for (int i = 0; i < pixels.Length; i += 4)
                (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
        else if (format is not (BgraUnorm or BgraSrgb))
            return null;
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        image.Freeze();
        return image;
    }
}
