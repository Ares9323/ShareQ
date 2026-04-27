using System.Runtime.InteropServices;
using System.Text;
using ShareQ.Clipboard.Native;

namespace ShareQ.Clipboard;

public sealed class Win32ClipboardReader : IClipboardReader
{
    private readonly IForegroundProcessProbe _probe;
    private readonly uint _cfHtml;
    private readonly uint _cfRtf;

    public Win32ClipboardReader(IForegroundProcessProbe probe)
    {
        _probe = probe;
        _cfHtml = ClipboardNativeMethods.RegisterClipboardFormat("HTML Format");
        _cfRtf = ClipboardNativeMethods.RegisterClipboardFormat("Rich Text Format");
    }

    public ClipboardChange? ReadCurrent(IntPtr ownerHwnd)
    {
        if (!ClipboardNativeMethods.OpenClipboard(ownerHwnd)) return null;
        try
        {
            var (format, payload, preview, files) = ReadFirstAvailable();
            if (format == ClipboardFormat.None) return null;

            return new ClipboardChange(
                Format: format,
                CapturedAt: DateTimeOffset.UtcNow,
                SourceProcess: _probe.GetForegroundProcessName(),
                SourceWindow: _probe.GetForegroundWindowTitle(),
                Payload: payload,
                PreviewText: preview,
                FilePaths: files);
        }
        finally
        {
            ClipboardNativeMethods.CloseClipboard();
        }
    }

    private (ClipboardFormat Format, ReadOnlyMemory<byte> Payload, string? Preview, string[]? Files) ReadFirstAvailable()
    {
        if (ClipboardNativeMethods.IsClipboardFormatAvailable(ClipboardNativeMethods.CfHdrop))
        {
            var files = ReadFileList();
            if (files is not null && files.Length > 0)
                return (ClipboardFormat.Files, ReadOnlyMemory<byte>.Empty, string.Join(Environment.NewLine, files), files);
        }
        if (ClipboardNativeMethods.IsClipboardFormatAvailable(_cfHtml))
        {
            var bytes = ReadGlobalBytes(_cfHtml);
            return (ClipboardFormat.Html, bytes, ExtractPreview(bytes), null);
        }
        if (ClipboardNativeMethods.IsClipboardFormatAvailable(_cfRtf))
        {
            var bytes = ReadGlobalBytes(_cfRtf);
            return (ClipboardFormat.Rtf, bytes, ExtractPreview(bytes), null);
        }
        if (ClipboardNativeMethods.IsClipboardFormatAvailable(ClipboardNativeMethods.CfUnicodeText))
        {
            var text = ReadUnicodeText();
            var bytes = Encoding.UTF8.GetBytes(text);
            return (ClipboardFormat.Text, bytes, text, null);
        }
        if (ClipboardNativeMethods.IsClipboardFormatAvailable(ClipboardNativeMethods.CfDib))
        {
            var bytes = ReadGlobalBytes(ClipboardNativeMethods.CfDib);
            return (ClipboardFormat.Image, bytes, "[image]", null);
        }
        return (ClipboardFormat.None, ReadOnlyMemory<byte>.Empty, null, null);
    }

    private static byte[] ReadGlobalBytes(uint format)
    {
        var hMem = ClipboardNativeMethods.GetClipboardData(format);
        if (hMem == IntPtr.Zero) return [];
        var size = (int)ClipboardNativeMethods.GlobalSize(hMem);
        if (size <= 0) return [];
        var ptr = ClipboardNativeMethods.GlobalLock(hMem);
        if (ptr == IntPtr.Zero) return [];
        try
        {
            var buffer = new byte[size];
            Marshal.Copy(ptr, buffer, 0, size);
            return buffer;
        }
        finally
        {
            ClipboardNativeMethods.GlobalUnlock(hMem);
        }
    }

    private static string ReadUnicodeText()
    {
        var hMem = ClipboardNativeMethods.GetClipboardData(ClipboardNativeMethods.CfUnicodeText);
        if (hMem == IntPtr.Zero) return string.Empty;
        var ptr = ClipboardNativeMethods.GlobalLock(hMem);
        if (ptr == IntPtr.Zero) return string.Empty;
        try
        {
            return Marshal.PtrToStringUni(ptr) ?? string.Empty;
        }
        finally
        {
            ClipboardNativeMethods.GlobalUnlock(hMem);
        }
    }

    private static string[]? ReadFileList()
    {
        var hDrop = ClipboardNativeMethods.GetClipboardData(ClipboardNativeMethods.CfHdrop);
        if (hDrop == IntPtr.Zero) return null;

        var count = ClipboardNativeMethods.DragQueryFile(hDrop, 0xFFFFFFFF, null, 0);
        if (count == 0) return null;

        var paths = new string[count];
        var buffer = new char[260];
        for (uint i = 0; i < count; i++)
        {
            var len = ClipboardNativeMethods.DragQueryFile(hDrop, i, buffer, (uint)buffer.Length);
            paths[i] = new string(buffer, 0, (int)len);
        }
        return paths;
    }

    private static string ExtractPreview(byte[] bytes)
    {
        if (bytes.Length == 0) return string.Empty;
        var text = Encoding.UTF8.GetString(bytes);
        if (text.Length > 256) text = text[..256] + "…";
        return text;
    }
}
