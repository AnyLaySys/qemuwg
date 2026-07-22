using System.Runtime.InteropServices;

namespace QemuWG.服务;

public sealed partial class QEMU会话
{
    private static IReadOnlyList<string> 解析命令行(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return [];
        var pointer = CommandLineToArgvW(commandLine, out var count);
        if (pointer == IntPtr.Zero) return [];
        try
        {
            var result = new string[count];
            for (var index = 0; index < count; index++)
            {
                var item = Marshal.ReadIntPtr(pointer, index * IntPtr.Size);
                result[index] = Marshal.PtrToStringUni(item) ?? string.Empty;
            }
            return result;
        }
        finally
        {
            LocalFree(pointer);
        }
    }

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW([MarshalAs(UnmanagedType.LPWStr)] string commandLine, out int argumentCount);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
