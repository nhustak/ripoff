// Test-only startup hook: attaches an SDL2 virtual game controller inside the game process and plays a script.
// RIPOFF_PADSCRIPT = "sec:ACTION;..." ACTION = press:BTN | hold:BTN | release:BTN | axis:N:VALUE | log:TEXT
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

internal class StartupHook
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate uint WasInitD(uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int AttachD(int type, int naxes, int nbuttons, int nhats);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr OpenD(int index);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int SetBtnD(IntPtr j, int b, byte v);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int SetAxisD(IntPtr j, int a, short v);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr ErrD();

    static readonly string[] Names = { "A","B","X","Y","BACK","GUIDE","START","LS","RS","LB","RB","UP","DOWN","LEFT","RIGHT" };

    public static void Initialize()
    {
        var script = Environment.GetEnvironmentVariable("RIPOFF_PADSCRIPT");
        if (string.IsNullOrEmpty(script)) return;
        var t = new Thread(() => Run(script)) { IsBackground = true };
        t.Start();
    }

    static void Log(string s) { Console.WriteLine($"[padhook {DateTime.Now:HH:mm:ss.fff}] {s}"); Console.Out.Flush(); }

    static void Run(string script)
    {
        try
        {
            IntPtr lib = NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "runtimes", "linux-x64", "native", "libSDL2-2.0.so.0"));
            T F<T>(string n) => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(lib, n));
            var wasInit = F<WasInitD>("SDL_WasInit"); var attach = F<AttachD>("SDL_JoystickAttachVirtual");
            var open = F<OpenD>("SDL_JoystickOpen"); var setb = F<SetBtnD>("SDL_JoystickSetVirtualButton");
            var seta = F<SetAxisD>("SDL_JoystickSetVirtualAxis"); var err = F<ErrD>("SDL_GetError");
            var start = DateTime.Now;
            while ((wasInit(0x2000) & 0x2000) == 0) Thread.Sleep(20); // SDL_INIT_GAMECONTROLLER
            Thread.Sleep(1500);
            int idx = attach(1 /*GAMECONTROLLER*/, 6, 15, 0);
            if (idx < 0) { Log("attach failed: " + Marshal.PtrToStringAnsi(err())); return; }
            IntPtr j = open(idx);
            Log($"virtual pad attached at SDL device index {idx}, joystick {(j == IntPtr.Zero ? "open FAILED " + Marshal.PtrToStringAnsi(err()) : "open")}");
            double t0 = (DateTime.Now - start).TotalSeconds;
            foreach (var step in script.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = step.Split(':');
                double at = double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
                var wait = TimeSpan.FromSeconds(at) - (DateTime.Now - start - TimeSpan.FromSeconds(t0));
                if (wait > TimeSpan.Zero) Thread.Sleep(wait);
                string a = parts[1];
                int B(string n) => Array.IndexOf(Names, n);
                switch (a)
                {
                    case "press": setb(j, B(parts[2]), 1); Thread.Sleep(120); setb(j, B(parts[2]), 0); break;
                    case "hold": setb(j, B(parts[2]), 1); break;
                    case "release": setb(j, B(parts[2]), 0); break;
                    case "shot": Log("SNAP:shot:" + parts[2]); Thread.Sleep(400); break;
                    case "axis": seta(j, int.Parse(parts[2]), short.Parse(parts[3])); break;
                }
                Log(step);
            }
            Log("script done");
        }
        catch (Exception e) { Log("hook error: " + e); }
    }
}
