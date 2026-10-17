using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace RipOff.Game
{
    /// <summary>
    /// The few SDL2 calls MonoGame DesktopGL 3.8.6 doesn't expose: the bounds of every display and the window's
    /// maximized state. MonoGame only offers GraphicsAdapter.DefaultAdapter.CurrentDisplayMode (the size of one
    /// display, no position) and no maximize API. The SDL2 library is the one MonoGame already loaded (bundled as
    /// SDL2.dll / libSDL2-2.0.so.0 / libSDL2-2.0.0.dylib); it is looked up, never shipped separately. Every call is
    /// optional: if the library or a function can't be found, or a call fails, the result is "unknown" (null/false)
    /// and the game falls back to default placement. Calls are made on the game thread only, after MonoGame has
    /// initialized SDL's video subsystem (from Initialize on).
    /// </summary>
    public static class SdlWindow
    {
        [StructLayout(LayoutKind.Sequential)] struct SdlRect { public int X, Y, W, H; }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int GetNumDisplaysFn();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int GetBoundsFn(int index, out SdlRect r);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate uint GetFlagsFn(IntPtr window);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void WindowFn(IntPtr window);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void WindowSizeFn(IntPtr window, int w, int h);

        const uint WindowMaximizedFlag = 0x80; // SDL_WINDOW_MAXIMIZED

        static bool _tried;
        static GetNumDisplaysFn? _num;
        static GetBoundsFn? _usable, _bounds;
        static GetFlagsFn? _flags;
        static WindowFn? _maximize;
        static WindowSizeFn? _minSize;

        /// <summary>Why SDL calls are unavailable (null when they work). For the input log.</summary>
        public static string? Problem { get; private set; }

        static bool Load()
        {
            if (_tried) return _num != null;
            _tried = true;
            try
            {
                string b = AppContext.BaseDirectory;
                var names = OperatingSystem.IsWindows() ? new[] { Path.Combine(b, "SDL2.dll"), "SDL2.dll" }
                    : OperatingSystem.IsMacOS() ? new[] { Path.Combine(b, "libSDL2-2.0.0.dylib"), Path.Combine(b, "runtimes", "osx", "native", "libSDL2-2.0.0.dylib"), "libSDL2-2.0.0.dylib" }
                    : new[] { Path.Combine(b, "libSDL2-2.0.so.0"), Path.Combine(b, "runtimes", RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "linux-arm64" : "linux-x64", "native", "libSDL2-2.0.so.0"), "libSDL2-2.0.so.0" };
                IntPtr lib = IntPtr.Zero;
                foreach (var n in names) if (NativeLibrary.TryLoad(n, out lib)) break;
                if (lib == IntPtr.Zero) { Problem = "SDL2 library not found"; return false; }
                T? F<T>(string n) where T : Delegate => NativeLibrary.TryGetExport(lib, n, out var p) ? Marshal.GetDelegateForFunctionPointer<T>(p) : null;
                _num = F<GetNumDisplaysFn>("SDL_GetNumVideoDisplays");
                _usable = F<GetBoundsFn>("SDL_GetDisplayUsableBounds");
                _bounds = F<GetBoundsFn>("SDL_GetDisplayBounds");
                _flags = F<GetFlagsFn>("SDL_GetWindowFlags");
                _maximize = F<WindowFn>("SDL_MaximizeWindow");
                _minSize = F<WindowSizeFn>("SDL_SetWindowMinimumSize");
                if (_num == null || (_usable == null && _bounds == null)) { Problem = "SDL2 display functions not found"; _num = null; return false; }
                return true;
            }
            catch (Exception e) { Problem = "SDL2: " + e.Message; _num = null; return false; }
        }

        /// <summary>Usable bounds (without task bars, where the OS says so) of every display, primary first; null if unknown.</summary>
        public static List<PixelRect>? Displays()
        {
            if (!Load()) return null;
            try
            {
                int n = _num!();
                if (n <= 0) { Problem = "SDL reports no displays"; return null; }
                var l = new List<PixelRect>();
                for (int i = 0; i < n; i++)
                {
                    SdlRect r = default;
                    bool ok = _usable != null && _usable(i, out r) == 0 && r.W > 0 && r.H > 0;
                    if (!ok && _bounds != null) ok = _bounds(i, out r) == 0 && r.W > 0 && r.H > 0;
                    if (ok) l.Add(new PixelRect(r.X, r.Y, r.W, r.H));
                }
                return l.Count > 0 ? l : null;
            }
            catch (Exception e) { Problem = "SDL2: " + e.Message; return null; }
        }

        /// <summary>True when the SDL window is maximized; false if not or unknown.</summary>
        public static bool IsMaximized(IntPtr window)
        {
            if (window == IntPtr.Zero || !Load() || _flags == null) return false;
            try { return (_flags(window) & WindowMaximizedFlag) != 0; } catch (Exception) { return false; }
        }

        public static bool CanMaximize => Load() && _maximize != null && _flags != null;

        /// <summary>Stops the user from resizing the window below <paramref name="w"/>×<paramref name="h"/> (no-op if unknown).</summary>
        public static void SetMinimumSize(IntPtr window, int w, int h)
        {
            if (window == IntPtr.Zero || !Load() || _minSize == null) return;
            try { _minSize(window, w, h); } catch (Exception) { }
        }

        public static void Maximize(IntPtr window)
        {
            if (window == IntPtr.Zero || !CanMaximize) return;
            try { _maximize!(window); } catch (Exception) { }
        }
    }
}
