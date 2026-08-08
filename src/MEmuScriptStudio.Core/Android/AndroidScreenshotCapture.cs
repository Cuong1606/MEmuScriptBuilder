using System.Buffers.Binary;
using MEmuScriptStudio.Core.Models;

namespace MEmuScriptStudio.Core.Android;

public sealed record AndroidScreenshotData(byte[] PngBytes);

public interface IAndroidScreenshotCaptureService
{
    Task<AndroidScreenshotData> CaptureAsync(
        string adbPath,
        string serial,
        CancellationToken cancellationToken);
}

public static class AndroidPngHeaderValidator
{
    public const int MaximumDimension = 8192;
    public const long MaximumPixels = 40_000_000;
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static (int Width, int Height) ValidateAndReadDimensions(ReadOnlySpan<byte> bytes)
    {
        const int minimumHeaderLength = 24;
        if (bytes.Length < minimumHeaderLength || !bytes[..Signature.Length].SequenceEqual(Signature))
            throw new InvalidDataException("ADB không trả về ảnh PNG hợp lệ.");
        if (BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(8, 4)) != 13 ||
            !bytes.Slice(12, 4).SequenceEqual("IHDR"u8))
            throw new InvalidDataException("Ảnh PNG không có IHDR hợp lệ.");

        var width = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(16, 4));
        var height = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(20, 4));
        if (width == 0 || height == 0 ||
            width > MaximumDimension || height > MaximumDimension ||
            (long)width * height > MaximumPixels)
            throw new InvalidDataException(
                $"Kích thước ảnh PNG vượt giới hạn {MaximumDimension} px / {MaximumPixels:N0} pixel.");

        return ((int)width, (int)height);
    }
}

public readonly record struct DisplayPoint(double X, double Y);

public readonly record struct DisplayRectangle(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
    public bool Contains(DisplayPoint point) =>
        point.X >= Left && point.X < Right && point.Y >= Top && point.Y < Bottom;
}

public static class UniformImageCoordinateMapper
{
    public static DisplayRectangle GetImageRectangle(
        double displayWidth,
        double displayHeight,
        int pixelWidth,
        int pixelHeight)
    {
        Validate(displayWidth, displayHeight, pixelWidth, pixelHeight);
        var scale = Math.Min(displayWidth / pixelWidth, displayHeight / pixelHeight);
        var width = pixelWidth * scale;
        var height = pixelHeight * scale;
        return new DisplayRectangle(
            (displayWidth - width) / 2d,
            (displayHeight - height) / 2d,
            width,
            height);
    }

    public static bool TryToNative(
        DisplayPoint displayPoint,
        double displayWidth,
        double displayHeight,
        int pixelWidth,
        int pixelHeight,
        out ScreenPoint nativePoint)
    {
        var image = GetImageRectangle(displayWidth, displayHeight, pixelWidth, pixelHeight);
        if (!image.Contains(displayPoint))
        {
            nativePoint = default;
            return false;
        }

        var x = Math.Min(pixelWidth - 1,
            (int)Math.Floor((displayPoint.X - image.Left) * pixelWidth / image.Width));
        var y = Math.Min(pixelHeight - 1,
            (int)Math.Floor((displayPoint.Y - image.Top) * pixelHeight / image.Height));
        nativePoint = new ScreenPoint(x, y);
        return true;
    }

    public static DisplayPoint ToDisplay(
        ScreenPoint nativePoint,
        double displayWidth,
        double displayHeight,
        int pixelWidth,
        int pixelHeight)
    {
        if (nativePoint.X < 0 || nativePoint.X >= pixelWidth ||
            nativePoint.Y < 0 || nativePoint.Y >= pixelHeight)
            throw new ArgumentOutOfRangeException(nameof(nativePoint));

        var image = GetImageRectangle(displayWidth, displayHeight, pixelWidth, pixelHeight);
        return new DisplayPoint(
            image.Left + ((nativePoint.X + 0.5d) * image.Width / pixelWidth),
            image.Top + ((nativePoint.Y + 0.5d) * image.Height / pixelHeight));
    }

    private static void Validate(double displayWidth, double displayHeight, int pixelWidth, int pixelHeight)
    {
        if (!double.IsFinite(displayWidth) || !double.IsFinite(displayHeight) ||
            displayWidth <= 0 || displayHeight <= 0 || pixelWidth <= 0 || pixelHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(displayWidth), "Kích thước hiển thị và ảnh phải lớn hơn 0.");
    }
}
