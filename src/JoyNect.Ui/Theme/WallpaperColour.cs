using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JoyNect.Core.Design;

namespace JoyNect.Ui.Theme;

/// <summary>
/// Picks a Material seed colour from the desktop wallpaper.
/// </summary>
/// <remarks>
/// This is what makes the app feel like part of the system rather than a themed window: the whole
/// palette is derived from whatever the user is looking at. The extraction is deliberately simple and
/// robust — downscale, score pixels by how colourful they are, average the best group — because a
/// clever quantiser is easy to write and easy to make produce muddy results. Anything unexpected
/// (no wallpaper, a locked-down session, an unreadable file) returns null and the caller falls back to
/// the stored seed.
/// </remarks>
public static class WallpaperColour
{
    private const int SpiGetDesktopWallpaper = 0x0073;
    private const int MaxPath = 512;

    /// <summary>The dominant colour of the current wallpaper, or null.</summary>
    public static uint? Extract()
    {
        try
        {
            var path = GetWallpaperPath();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }

            var frame = BitmapFrame.Create(
                new Uri(path!),
                BitmapCreateOptions.IgnoreImageCache | BitmapCreateOptions.DelayCreation,
                BitmapCacheOption.OnLoad);

            // A thumbnail is enough to find the dominant hue and costs almost nothing.
            const int sample = 64;
            var scale = Math.Min((double)sample / frame.PixelWidth, (double)sample / frame.PixelHeight);
            if (scale <= 0.0 || double.IsNaN(scale))
            {
                return null;
            }

            var small = new TransformedBitmap(frame, new ScaleTransform(scale, scale));
            var converted = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);

            var width = converted.PixelWidth;
            var height = converted.PixelHeight;
            if (width <= 0 || height <= 0)
            {
                return null;
            }

            var stride = width * 4;
            var pixels = new byte[stride * height];
            converted.CopyPixels(pixels, stride, 0);

            return PickSeed(pixels, width, height);
        }
        catch (Exception)
        {
            // A wallpaper can be a video, a slideshow, on a disconnected drive or unreadable. None of
            // those should break startup.
            return null;
        }
    }

    /// <summary>Chooses a seed from raw BGRA pixels.</summary>
    internal static uint PickSeed(byte[] pixels, int width, int height)
    {
        // Group by hue band and keep the most colourful group; fall back to the average colour for a
        // grey wallpaper, which produces a neutral theme rather than an arbitrary one.
        var buckets = new Dictionary<int, (double Weight, double Red, double Green, double Blue)>();
        double totalRed = 0, totalGreen = 0, totalBlue = 0;
        var count = 0;

        for (var offset = 0; offset + 3 < pixels.Length; offset += 4)
        {
            var blue = pixels[offset] / 255.0;
            var green = pixels[offset + 1] / 255.0;
            var red = pixels[offset + 2] / 255.0;

            totalRed += red;
            totalGreen += green;
            totalBlue += blue;
            count++;

            var max = Math.Max(red, Math.Max(green, blue));
            var min = Math.Min(red, Math.Min(green, blue));
            var saturation = max <= 0.0 ? 0.0 : (max - min) / max;

            // Near-black and near-white pixels carry almost no hue information and would drag the
            // average towards grey, so they are excluded from the hue search.
            if (saturation < 0.18 || max < 0.12 || min > 0.96)
            {
                continue;
            }

            var hue = Hue(red, green, blue);
            var bucket = (int)(hue / 15.0) % 24;
            var weight = saturation * saturation * max;

            var existing = buckets.GetValueOrDefault(bucket);
            buckets[bucket] = (
                existing.Weight + weight,
                existing.Red + (red * weight),
                existing.Green + (green * weight),
                existing.Blue + (blue * weight));
        }

        if (count == 0)
        {
            return MaterialSchemes.BaselineSeed;
        }

        if (buckets.Count == 0)
        {
            return Pack(totalRed / count, totalGreen / count, totalBlue / count);
        }

        var best = buckets.OrderByDescending(pair => pair.Value.Weight).First().Value;
        if (best.Weight <= 0.0)
        {
            return Pack(totalRed / count, totalGreen / count, totalBlue / count);
        }

        return Pack(best.Red / best.Weight, best.Green / best.Weight, best.Blue / best.Weight);
    }

    private static uint Pack(double red, double green, double blue)
    {
        // Keep the seed inside a range where a palette still has room to be colourful: a nearly black
        // or white seed produces a theme with no identity at all.
        var max = Math.Max(red, Math.Max(green, blue));
        if (max < 0.25 || max > 0.98)
        {
            var scale = 0.62 / Math.Max(max, 0.01);
            red *= scale;
            green *= scale;
            blue *= scale;
        }

        return 0xFF000000u
            | ((uint)Math.Clamp((int)Math.Round(red * 255.0), 0, 255) << 16)
            | ((uint)Math.Clamp((int)Math.Round(green * 255.0), 0, 255) << 8)
            | (uint)Math.Clamp((int)Math.Round(blue * 255.0), 0, 255);
    }

    private static double Hue(double red, double green, double blue)
    {
        var max = Math.Max(red, Math.Max(green, blue));
        var min = Math.Min(red, Math.Min(green, blue));
        var delta = max - min;

        if (delta <= 0.0)
        {
            return 0.0;
        }

        double hue;
        if (max == red)
        {
            hue = 60.0 * (((green - blue) / delta) % 6.0);
        }
        else if (max == green)
        {
            hue = 60.0 * (((blue - red) / delta) + 2.0);
        }
        else
        {
            hue = 60.0 * (((red - green) / delta) + 4.0);
        }

        return hue < 0.0 ? hue + 360.0 : hue;
    }

    private static string? GetWallpaperPath()
    {
        var buffer = new System.Text.StringBuilder(MaxPath);
        return SystemParametersInfo(SpiGetDesktopWallpaper, MaxPath, buffer, 0) ? buffer.ToString() : null;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(int action, int parameter, System.Text.StringBuilder value, int update);
}
