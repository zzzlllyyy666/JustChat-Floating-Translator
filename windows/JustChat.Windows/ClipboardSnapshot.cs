using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using Clipboard = System.Windows.Clipboard;
using DataObject = System.Windows.DataObject;
using IDataObject = System.Windows.IDataObject;

namespace JustChat.Windows;

internal sealed class ClipboardSnapshot
{
    private readonly Dictionary<string, object> _values;

    private ClipboardSnapshot(Dictionary<string, object> values)
    {
        _values = values;
    }

    public static ClipboardSnapshot Capture()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var sequenceBeforeCapture = GetSequenceNumber();
            var snapshot = CaptureOnce();
            if (sequenceBeforeCapture == GetSequenceNumber())
            {
                return snapshot;
            }
        }

        throw new ExternalException("剪贴板内容持续变化，请稍后重试");
    }

    public static uint GetSequenceNumber() => GetClipboardSequenceNumber();

    public void Restore()
    {
        if (_values.Count == 0)
        {
            Clipboard.Clear();
            return;
        }

        var data = new DataObject();
        foreach (var (format, value) in _values)
        {
            data.SetData(format, value);
        }

        Clipboard.SetDataObject(data, copy: true);
    }

    private static ClipboardSnapshot CaptureOnce()
    {
        var values = new Dictionary<string, object>(StringComparer.Ordinal);
        IDataObject? data = Clipboard.GetDataObject();
        if (data is null)
        {
            return new ClipboardSnapshot(values);
        }

        foreach (var format in data.GetFormats())
        {
            var value = data.GetData(format);
            if (value is not null)
            {
                values[format] = CloneValue(value);
            }
        }

        return new ClipboardSnapshot(values);
    }

    private static object CloneValue(object value)
    {
        if (value is Stream stream)
        {
            var originalPosition = stream.CanSeek ? stream.Position : 0;
            var copy = new MemoryStream();
            stream.CopyTo(copy);
            copy.Position = 0;
            if (stream.CanSeek)
            {
                stream.Position = originalPosition;
            }

            return copy;
        }

        if (value is Bitmap bitmap)
        {
            return new Bitmap(bitmap);
        }

        if (value is byte[] bytes)
        {
            return bytes.ToArray();
        }

        if (value is string[] strings)
        {
            return strings.ToArray();
        }

        return value;
    }

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();
}
