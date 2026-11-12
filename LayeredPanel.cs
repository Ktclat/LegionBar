using System.Runtime.InteropServices;
namespace LegionBar;

internal static class LayeredPanel
{
    public static void Present(IntPtr window, Bitmap bitmap, Point position)
    {
        var screen = Native.GetDC(IntPtr.Zero);
        var memory = Native.CreateCompatibleDC(screen);
        var handle = bitmap.GetHbitmap(Color.FromArgb(0));
        var old = Native.SelectObject(memory, handle);
        try
        {
            var size = new Native.Size(bitmap.Width, bitmap.Height);
            var destination = new Native.Point(position.X, position.Y); var source = new Native.Point(0, 0);
            var blend = new Native.BlendFunction { Operation = 0, Alpha = 255, Format = 1 };
            if (!Native.UpdateLayeredWindow(window, screen, ref destination, ref size, memory, ref source, 0, ref blend, 2))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        finally { Native.SelectObject(memory, old); Native.DeleteObject(handle); Native.DeleteDC(memory); Native.ReleaseDC(IntPtr.Zero, screen); }
    }
}
