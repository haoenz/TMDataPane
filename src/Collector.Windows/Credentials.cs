using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using UsageBridge.Core;
namespace UsageBridge;

internal sealed class Credentials(string directory)
{
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr pointer);
    private string PathFor(string id) => Path.Combine(directory, "credentials", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))) + ".bin");
    public void Save(string id, string value)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(value)) throw new FormatException("凭据名称和值不能为空");
        Directory.CreateDirectory(Path.Combine(directory, "credentials"));
        byte[] plain = Encoding.UTF8.GetBytes(value);
        try { File.WriteAllBytes(PathFor(id), Transform(plain, true)); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    public string Read(string id)
    {
        try {
            byte[] plain = Transform(File.ReadAllBytes(PathFor(id)), false);
            try { return Encoding.UTF8.GetString(plain); } finally { CryptographicOperations.ZeroMemory(plain); }
        } catch { throw new CollectionFailure("需要登录", "凭据未保存或当前 Windows 用户无法解密"); }
    }
    private static byte[] Transform(byte[] bytes, bool protect)
    {
        var input = new Blob { Length = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) }; Blob output = default;
        try {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            bool ok = protect ? CryptProtectData(ref input, "UsageBridge", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new CryptographicException("Windows DPAPI 操作失败");
            var result = new byte[output.Length]; Marshal.Copy(output.Data, result, 0, result.Length); return result;
        } finally {
            for (int i = 0; i < input.Length; i++) Marshal.WriteByte(input.Data, i, 0);
            Marshal.FreeHGlobal(input.Data); if (output.Data != IntPtr.Zero) LocalFree(output.Data);
        }
    }
}
