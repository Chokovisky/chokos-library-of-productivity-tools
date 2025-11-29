using System;
using System.Runtime.InteropServices;
using System.Text;
using ChokoLPT.Shared.Helpers;

namespace ChokoLPT.Shared.Services;

/// <summary>
/// Helpers for window messaging between AHK and C# (WM_COPYDATA / PostMessage).
/// Centralizes interop so apps like RadialMenu and HKCheatsheetOverlay don't
/// declare their own P/Invokes.
/// </summary>
public static class MessageService
{
    public const int WM_COPYDATA = 0x004A;

    /// <summary>
    /// Send a WM_COPYDATA message carrying a UTF-8 JSON payload to a target HWND.
    /// Returns true if SendMessage returns non-zero.
    /// </summary>
    public static bool SendJsonCopyData(IntPtr targetHwnd, string json, IntPtr dwData = default)
    {
        if (targetHwnd == IntPtr.Zero)
            return false;

        byte[] bytes = Encoding.UTF8.GetBytes(json);
        IntPtr ptr = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, ptr, bytes.Length);

            var cds = new Win32.COPYDATASTRUCT
            {
                dwData = dwData,
                cbData = bytes.Length,
                lpData = ptr
            };

            IntPtr result = Win32.SendMessage(targetHwnd, WM_COPYDATA, IntPtr.Zero, ref cds);
            return result != IntPtr.Zero;
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    /// <summary>
    /// Simple PostMessage wrapper (e.g. for ping/toggle messages).
    /// </summary>
    public static bool Post(IntPtr targetHwnd, uint msg, IntPtr wParam, IntPtr lParam)
        => targetHwnd != IntPtr.Zero && Win32.PostMessage(targetHwnd, msg, wParam, lParam);

    /// <summary>
    /// Helper to find window by title (for simple singleton signaling scenarios).
    /// </summary>
    public static IntPtr FindByTitle(string windowTitle)
        => string.IsNullOrWhiteSpace(windowTitle)
            ? IntPtr.Zero
            : Win32.FindWindow(null, windowTitle);
}