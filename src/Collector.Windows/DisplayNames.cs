using System.Buffers.Binary;
using System.Text;
using DataPane.Core;
namespace DataPane;

// Derived display metadata only; readable without collection or credentials.
internal static class DisplayNames
{
    internal static void Write(string directory, Configuration config)
    {
        byte[] bytes = new byte[4 + 16 * 64 * 2];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0x314E5044); // DPN1
        foreach (var view in config.Views) {
            string name = string.IsNullOrWhiteSpace(view.Label) ? "未命名" : view.Label;
            name = new string(name.Select(c => char.IsControl(c) ? ' ' : c).ToArray());
            int length = Math.Min(name.Length, 63);
            if (length > 0 && char.IsHighSurrogate(name[length - 1])) length--;
            Encoding.Unicode.GetBytes(name.AsSpan(0, length), bytes.AsSpan(4 + (view.Slot - 1) * 128, 128));
        }
        string path = Path.Combine(directory, "display-names.bin");
        File.WriteAllBytes(path + ".tmp", bytes);
        File.Move(path + ".tmp", path, true);
        byte[] widths = new byte[4 + 16 * 64 * 2];
        BinaryPrimitives.WriteUInt32LittleEndian(widths, 0x31575044); // DPW1
        foreach (var view in config.Views)
            Encoding.Unicode.GetBytes(view.EffectiveWidthSample.AsSpan(), widths.AsSpan(4 + (view.Slot - 1) * 128, 126));
        string widthPath = Path.Combine(directory, "display-widths.bin");
        File.WriteAllBytes(widthPath + ".tmp", widths);
        File.Move(widthPath + ".tmp", widthPath, true);
    }
}
