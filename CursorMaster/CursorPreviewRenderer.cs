using System.Buffers.Binary;
using System.ComponentModel;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace CursorMaster;

internal sealed class CursorPreviewAnimation : IDisposable
{
    private const uint DrawIconNormal = 0x0003;
    private readonly IntPtr cursor;
    private readonly bool isIcon;
    private bool disposed;

    internal CursorPreviewAnimation(
        IntPtr cursor,
        int[] frames,
        int[] delays,
        bool isIcon = false)
    {
        this.cursor = cursor;
        Frames = frames;
        Delays = delays;
        this.isIcon = isIcon;
    }

    public int[] Frames { get; }
    public int[] Delays { get; }
    public bool IsAnimated => Frames.Length > 1;

    public Bitmap Render(int frame)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if ((uint)frame > (uint)Frames.Max())
            throw new ArgumentOutOfRangeException(nameof(frame));

        var bitmap = new Bitmap(128, 128, PixelFormat.Format32bppArgb);
        try
        {
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.FromArgb(36, 38, 45));
                using var pen = new Pen(Color.FromArgb(54, 57, 66));
                graphics.DrawRectangle(pen, 0, 0, bitmap.Width - 1, bitmap.Height - 1);
                graphics.DrawLine(pen, 0, 64, 128, 64);
                graphics.DrawLine(pen, 64, 0, 64, 128);
            }

            using var target = Graphics.FromImage(bitmap);
            var hdc = target.GetHdc();
            try
            {
                if (!CursorPreviewRenderer.DrawIconEx(
                        hdc, 32, 32, cursor, 64, 64, (uint)frame,
                        IntPtr.Zero, DrawIconNormal))
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        "Windows could not draw the cursor preview.");
            }
            finally
            {
                target.ReleaseHdc(hdc);
            }
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        CursorPreviewRenderer.DestroyPreviewHandle(cursor, isIcon);
    }
}

internal static class CursorPreviewRenderer
{
    private const int MaxAniFileBytes = 64 * 1024 * 1024;
    private const int MaxAniSteps = 4096;
    private const int DefaultAniDelay = 100;
    private const uint ImageIcon = 1;
    private const uint LoadFromFile = 0x0010;

    [DllImport("user32.dll", EntryPoint = "LoadCursorFromFileW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadCursorFromFile(string fileName);

    [DllImport("user32.dll", EntryPoint = "LoadImageW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadIconFromFile(
        IntPtr instance, string fileName, uint imageType, int width, int height, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DrawIconEx(
        IntPtr deviceContext,
        int x,
        int y,
        IntPtr icon,
        int width,
        int height,
        uint frame,
        IntPtr brush,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyCursor(IntPtr cursor);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    internal static void DestroyPreviewHandle(IntPtr handle, bool isIcon)
    {
        if (isIcon)
            _ = DestroyIcon(handle);
        else
            _ = DestroyCursor(handle);
    }

    public static CursorPreviewAnimation Open(string path)
    {
        var extension = Path.GetExtension(path);
        var isIcon = extension.Equals(".ico", StringComparison.OrdinalIgnoreCase);
        var isAnimation = extension.Equals(".ani", StringComparison.OrdinalIgnoreCase);
        if (!isIcon && !isAnimation &&
            !extension.Equals(".cur", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(
                "Only .ani, .cur, and .ico files can be previewed.", nameof(path));

        var handle = isIcon
            ? LoadIconFromFile(IntPtr.Zero, path, ImageIcon, 64, 64, LoadFromFile)
            : LoadCursorFromFile(path);
        if (handle == IntPtr.Zero)
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                $"Windows could not load the {extension} preview.");

        try
        {
            var animation = isAnimation
                ? ReadAnimation(path)
                : new AniData([0], [DefaultAniDelay]);
            return new CursorPreviewAnimation(
                handle, animation.Frames, animation.Delays, isIcon);
        }
        catch
        {
            DestroyPreviewHandle(handle, isIcon);
            throw;
        }
    }

    private static AniData ReadAnimation(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
            throw new FileNotFoundException("The ANI file was not found.", path);
        if (info.Length > MaxAniFileBytes)
            throw new FormatException("ANI files larger than 64 MB cannot be previewed.");

        var data = File.ReadAllBytes(path);
        if (data.Length < 12 ||
            FourCc(data, 0) != "RIFF" ||
            FourCc(data, 8) != "ACON")
            throw new FormatException("The ANI file does not have a valid RIFF/ACON header.");

        var riffEnd = checked((long)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4, 4)) + 8);
        if (riffEnd < 12 || riffEnd > data.Length)
            throw new FormatException("The ANI RIFF size is invalid.");

        byte[]? header = null;
        byte[]? rates = null;
        byte[]? sequence = null;
        ReadChunks(data, 12, (int)riffEnd, (id, chunk, _) =>
        {
            switch (id)
            {
                case "anih":
                    header ??= chunk;
                    break;
                case "rate":
                    rates ??= chunk;
                    break;
                case "seq ":
                    sequence ??= chunk;
                    break;
            }
        });

        if (header is null || header.Length < 36 ||
            BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(0, 4)) < 36)
            throw new FormatException("The ANI animation header is missing or invalid.");

        var frameCount = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4, 4));
        var stepCount = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8, 4));
        var defaultRate = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(28, 4));
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(32, 4));
        if (frameCount is 0 or > MaxAniSteps ||
            stepCount is 0 or > MaxAniSteps)
            throw new FormatException("The ANI animation has an unsupported frame or step count.");

        var frames = new int[stepCount];
        var delays = new int[stepCount];
        var hasSequence = (flags & 2) != 0 && sequence is not null;
        if (hasSequence && sequence!.Length < stepCount * sizeof(uint))
            throw new FormatException("The ANI frame sequence is incomplete.");
        if (rates is not null && rates.Length < stepCount * sizeof(uint))
            throw new FormatException("The ANI frame timing data is incomplete.");

        for (var step = 0; step < stepCount; step++)
        {
            var frame = hasSequence
                ? BinaryPrimitives.ReadUInt32LittleEndian(
                    sequence!.AsSpan(step * sizeof(uint), sizeof(uint)))
                : step % frameCount;
            if (frame >= frameCount)
                throw new FormatException("The ANI sequence references a missing frame.");
            frames[step] = (int)frame;

            var rate = rates is null
                ? defaultRate
                : BinaryPrimitives.ReadUInt32LittleEndian(
                    rates.AsSpan(step * sizeof(uint), sizeof(uint)));
            if (rate == 0)
                rate = DefaultRate;
            delays[step] = Math.Max(
                16, (int)Math.Round(rate * (1000d / 60d), MidpointRounding.AwayFromZero));
        }
        return new AniData(frames, delays);
    }

    private static void ReadChunks(
        byte[] data,
        int start,
        int end,
        Action<string, byte[], string?> onChunk)
    {
        var offset = start;
        while (offset < end)
        {
            if (end - offset < 8)
                throw new FormatException("The ANI contains an incomplete chunk header.");

            var id = FourCc(data, offset);
            var length = BinaryPrimitives.ReadUInt32LittleEndian(
                data.AsSpan(offset + 4, 4));
            var chunkStart = offset + 8;
            var chunkEnd = (long)chunkStart + length;
            var paddedEnd = chunkEnd + (length & 1);
            if (chunkEnd > end || paddedEnd > end)
                throw new FormatException("The ANI contains an invalid chunk size.");

            string? listType = null;
            if (id == "LIST")
            {
                if (length < 4)
                    throw new FormatException("The ANI contains an invalid LIST chunk.");
                listType = FourCc(data, chunkStart);
                if (listType == "fram")
                    ReadChunks(data, chunkStart + 4, (int)chunkEnd, onChunk);
            }
            else
            {
                onChunk(id, data.AsSpan(chunkStart, (int)length).ToArray(), listType);
            }
            offset = (int)paddedEnd;
        }
    }

    private static string FourCc(byte[] data, int offset) =>
        Encoding.ASCII.GetString(data, offset, 4);

    private static uint DefaultRate => 6;

    private sealed record AniData(int[] Frames, int[] Delays);
}
