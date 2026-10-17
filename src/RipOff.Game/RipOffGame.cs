using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RipOff.Core;
using RipOff.Game.Steam;

namespace RipOff.Game
{
    /// <summary>
    /// MonoGame host: a fixed 38 Hz step (one simulation pass per arcade frame, GAME_RULES §Units / NOTES §18.8).
    /// Reads the keyboard and every pad (InputReader), runs the <see cref="FrontEnd"/> state machine, renders its
    /// lines and plays its sound, and applies the display/audio/dead-zone settings. All rules live in RipOff.Core;
    /// all menu logic in FrontEnd.
    /// </summary>
    public sealed class RipOffGame : Microsoft.Xna.Framework.Game
    {
        readonly GraphicsDeviceManager _gfx;
        readonly List<Line> _lines = new List<Line>();
        readonly InputReader _input = new InputReader();
        readonly GamePadState[] _pads = new GamePadState[InputReader.MaxPads];
        readonly StringBuilder _typed = new StringBuilder();
        readonly SteamGate? _steam;
        readonly InputLog? _log;
        readonly PadSlot[] _slots = new PadSlot[InputReader.MaxPads];
        readonly GamePadState[] _sdl = new GamePadState[InputReader.MaxPads];
        readonly GamePadState[] _steamPads = new GamePadState[InputReader.MaxPads];
        readonly PadSlot[] _sdlSlots = new PadSlot[InputReader.MaxPads];
        string _padSource = "";
        readonly FrontEnd? _front;
        readonly SettingsStore? _store;
        SoundOutput? _sound;
        LineRenderer? _renderer;
        readonly string? _shotPath;
        readonly int _shotPasses;
        bool _fullscreenApplied;
        string _title = "";
        bool _applyingWindow;      // our own ApplyChanges: ignore the ClientSizeChanged it raises
        int _geometryQuietTicks = -1; // ticks since the windowed geometry last changed (-1 = saved / nothing pending)
        /// <summary>Ticks of unchanged geometry before a move/resize is saved (1 s): one save per drag, not per frame.</summary>
        public const int GeometrySaveDelayTicks = 38;

        public RipOffGame(string? shotPath = null, int shotPasses = 400, SteamGate? steam = null,
            Settings? settings = null, SettingsStore? store = null, string? notice = null, InputLog? log = null)
        {
            _shotPath = shotPath; _shotPasses = shotPasses; _steam = steam; _store = store; _log = log;
            _gfx = new GraphicsDeviceManager(this)
            {
                PreferredBackBufferWidth = 1024,
                PreferredBackBufferHeight = 768,
                SynchronizeWithVerticalRetrace = true,
                HardwareModeSwitch = false, // fullscreen = borderless window at desktop resolution
            };
            IsFixedTimeStep = true;
            TargetElapsedTime = TimeSpan.FromSeconds(1.0 / GameData.PassesPerSecond);
            Window.AllowUserResizing = true;
            Window.ClientSizeChanged += (_, _) => OnClientSizeChanged();
            Window.Title = Branding.WindowTitle + " " + BuildInfo.Display.ToLowerInvariant();
            IsMouseVisible = false;
            Window.TextInput += (_, e) => { if (_typed.Length < 32) _typed.Append(e.Character); };
            if (shotPath == null)
            {
                var s = settings ?? new Settings();
                _front = new FrontEnd(s, OnlineController.WithSteamSource(() => steam?.Platform), steam, st => { if (_store?.Save(st) is { } err) _front?.Notify(err); }, notice);
            }
        }

        protected override void Initialize()
        {
            if (_front != null)
            {
                SdlWindow.SetMinimumSize(Window.Handle, WindowGeometry.MinWidth, WindowGeometry.MinHeight);
                var displays = SdlWindow.Displays();
                _log?.Header("displays: " + (displays == null ? "unknown (" + SdlWindow.Problem + ")" : string.Join(", ", displays.ConvertAll(d => $"{d.Width}x{d.Height}@{d.X},{d.Y}")))
                    + $"; saved window: {(_front.Settings.WindowRect is { } r ? $"{r.Width}x{r.Height}@{r.X},{r.Y}" : "none")}{(_front.Settings.WindowMaximized ? " maximized" : "")}");
                if (_front.Settings.Fullscreen) ApplyFullscreen(true);
                else ApplyWindowed(displays);
            }
            base.Initialize();
        }

        /// <summary>Windowed size and position from the saved geometry, validated against the displays (WindowGeometry).</summary>
        void ApplyWindowed(List<PixelRect>? displays = null)
        {
            var s = _front!.Settings;
            var pl = WindowGeometry.Resolve(s.WindowRect, displays ?? SdlWindow.Displays());
            _applyingWindow = true;
            try
            {
                _gfx.IsFullScreen = false;
                _gfx.PreferredBackBufferWidth = pl.Width; _gfx.PreferredBackBufferHeight = pl.Height;
                _gfx.ApplyChanges();
                if (pl.Position is { } p) Window.Position = new Point(p.X, p.Y);
                if (s.WindowMaximized) SdlWindow.Maximize(Window.Handle);
            }
            finally { _applyingWindow = false; }
            _log?.Event($"window: {pl.Width}x{pl.Height}" + (pl.Position is { } q ? $" at {q.X},{q.Y}" : " (OS placement)") + (pl.FromSaved ? " (saved)" : " (default)") + (s.WindowMaximized ? ", maximized" : ""));
        }

        /// <summary>User resize: the back buffer follows the client area, so the 4:3 letterbox (LineRenderer) uses the
        /// whole window. Not while fullscreen, and not for our own ApplyChanges.</summary>
        void OnClientSizeChanged()
        {
            if (_applyingWindow || _front == null || _gfx.IsFullScreen) return;
            var cb = Window.ClientBounds;
            if (cb.Width <= 0 || cb.Height <= 0) return; // minimized
            if (cb.Width != _gfx.PreferredBackBufferWidth || cb.Height != _gfx.PreferredBackBufferHeight)
            {
                _applyingWindow = true;
                try { _gfx.PreferredBackBufferWidth = cb.Width; _gfx.PreferredBackBufferHeight = cb.Height; _gfx.ApplyChanges(); }
                finally { _applyingWindow = false; }
            }
        }

        /// <summary>Copies the current windowed geometry into the settings (only while windowed; a maximized window keeps
        /// its un-maximized rect and sets WindowMaximized). True if something changed.</summary>
        bool CaptureWindowed()
        {
            if (_front == null || _gfx.IsFullScreen || _fullscreenApplied) return false;
            var s = _front.Settings;
            bool max = SdlWindow.IsMaximized(Window.Handle);
            bool changed = max != s.WindowMaximized;
            s.WindowMaximized = max;
            if (max) return changed;
            var cb = Window.ClientBounds; var pos = Window.Position;
            if (cb.Width <= 0 || cb.Height <= 0 || !WindowGeometry.IsSaneSize(cb.Width, cb.Height)) return changed; // minimized / odd
            var r = new PixelRect(pos.X, pos.Y, cb.Width, cb.Height);
            if (s.WindowRect == r) return changed;
            s.WindowX = r.X; s.WindowY = r.Y; s.WindowWidth = r.Width; s.WindowHeight = r.Height;
            return true;
        }

        /// <summary>Debounced save of window moves/resizes: after <see cref="GeometrySaveDelayTicks"/> quiet ticks.</summary>
        void TrackWindowGeometry()
        {
            if (CaptureWindowed()) { _geometryQuietTicks = 0; return; }
            if (_geometryQuietTicks < 0) return;
            if (++_geometryQuietTicks < GeometrySaveDelayTicks) return;
            _geometryQuietTicks = -1;
            if (_store?.Save(_front!.Settings) is { } err) _front!.Notify(err);
            var g = _front!.Settings;
            _log?.Event($"window saved: {g.WindowWidth}x{g.WindowHeight} at {g.WindowX},{g.WindowY}{(g.WindowMaximized ? ", maximized" : "")}");
        }

        protected override void LoadContent()
        {
            _renderer = new LineRenderer(GraphicsDevice);
            _sound = new SoundOutput(enable: _shotPath == null);
            ApplyAudio();
        }

        void ApplyFullscreen(bool on)
        {
            if (on && !_gfx.IsFullScreen && !_fullscreenApplied) CaptureWindowed(); // remember the windowed geometry first
            _fullscreenApplied = on;
            if (_gfx.IsFullScreen == on) return;
            if (on)
            {
                var dm = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode;
                _applyingWindow = true;
                try
                {
                    _gfx.PreferredBackBufferWidth = dm.Width; _gfx.PreferredBackBufferHeight = dm.Height;
                    _gfx.IsFullScreen = true;
                    _gfx.ApplyChanges();
                }
                finally { _applyingWindow = false; }
            }
            else ApplyWindowed(); // back to the saved windowed geometry
        }

        void ApplyAudio()
        {
            var b = _sound?.Board; if (b == null || _front == null) return;
            b.Volume = _front.Settings.Volume; b.Muted = _front.Settings.Muted;
        }

        /// <summary>
        /// Reads every slot 0-3 with GamePad.GetState(i) and MonoGame's default dead zone (IndependentAxes), making
        /// no assumption about which slot a pad lands in. A failing or absent pad reads as disconnected. The name and
        /// type (GetCapabilities) are refreshed when a slot's connection changes.
        /// </summary>
        void ReadPads()
        {
            Array.Copy(_sdlSlots, _slots, _slots.Length); // SDL view (names cached per SDL slot)
            for (int i = 0; i < _slots.Length; i++)
            {
                GamePadState st;
                try { st = GamePad.GetState(i); }
                catch (Exception) { st = default; }
                if (st.IsConnected != _slots[i].Connected || (st.IsConnected && _slots[i].Name == null))
                {
                    string name = "", type = "";
                    if (st.IsConnected)
                        try { var c = GamePad.GetCapabilities(i); name = c.DisplayName ?? ""; type = c.GamePadType.ToString(); }
                        catch (Exception) { }
                    _slots[i].Name = name; _slots[i].Type = type;
                }
                _slots[i].Name ??= ""; _slots[i].Type ??= "";
                _slots[i].State = st;
                _sdl[i] = st;
            }
            Array.Copy(_slots, _sdlSlots, _slots.Length);

            // Steam Input (NOTES §25): once Steam is initialized under App 480, Steam can take the controller away from
            // SDL/XInput and deliver it as Spacewar actions instead; read those and prefer them when they are bound.
            var sip = _steam?.Platform?.InputPads;
            int bound = 0;
            if (sip != null) { sip.Read(_front!.InGame, _steamPads); bound = sip.Bound; }
            else Array.Clear(_steamPads);
            bool useSteam = SteamPadMapping.Merge(_sdl, _steamPads, bound, _pads);
            if (useSteam)
                for (int i = 0; i < _slots.Length; i++)
                    _slots[i] = new PadSlot { State = _pads[i], Name = _pads[i].IsConnected ? "Steam Input" : "", Type = sip!.Types[i] ?? "" };
            string src = useSteam ? $"Steam Input ({sip!.Bound} of {sip.Connected} controllers bound, {( _front!.InGame ? "ship" : "menu")} actions; SDL pads ignored)"
                : sip != null ? $"SDL (MonoGame GamePad); Steam Input: {sip.Status}, {sip.Connected} controllers, none bound{(sip.HandlesResolved ? "" : ", Spacewar actions not found")}"
                : "SDL (MonoGame GamePad); Steam Input off (Steam not started)";
            if (src != _padSource) { _padSource = src; _log?.Event("pad source: " + src); }
            _front!.PadSource = useSteam ? $"STEAM INPUT, {sip!.Bound} CONTROLLER(S)" : sip != null ? $"SDL; STEAM INPUT {sip.Connected} SEEN, NONE BOUND" : "SDL (STEAM NOT STARTED)";
        }

        protected override void Update(GameTime gameTime)
        {
            if (_shotPath != null) { RunShot(); Exit(); return; }
            var front = _front!;
            _steam?.RunCallbacks();
            ReadPads();
            Array.Copy(_slots, front.Pads, _slots.Length);
            _log?.Tick(_slots, _steam?.Status ?? "Steam off");
            _input.Pads.StickDeadzone = front.Settings.StickDeadzone;
            var (ui, play) = _input.Read(Keyboard.GetState(), _pads, _typed.ToString());
            _typed.Clear();

            front.Update(ui, play);
            if (front.ExitRequested) { CaptureWindowed(); _store?.Save(front.Settings); Exit(); return; }

            if (front.Settings.Fullscreen != _fullscreenApplied) ApplyFullscreen(front.Settings.Fullscreen);
            TrackWindowGeometry();
            ApplyAudio();
            _sound?.Update(front.Sound);
            UpdateTitle();
            base.Update(gameTime);
        }

        void UpdateTitle()
        {
            var s = _front!.Settings;
            string audio = _sound == null || _sound.Available ? (s.Muted ? "muted" : $"volume {Math.Round(s.Volume * 100)}%") : _sound.Status;
            int pads = _input.Pads.ConnectedCount;
            string padText = pads == 0 ? "no pad" : pads == 1 ? "1 pad" : pads + " pads";
            string steam = _steam == null || !_steam.Initialized ? ", Steam not started" : _steam.Available ? ", Steam" : ", no Steam";
            string t = $"{Branding.WindowTitle} {BuildInfo.Display.ToLowerInvariant()} - {audio}, {padText}{steam}";
            if (t != _title) { Window.Title = t; _title = t; }
        }

        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(Color.Black);
            _front?.Build(_lines);
            _renderer!.Draw(_lines, GraphicsDevice.Viewport.Bounds);
            base.Draw(gameTime);
        }

        /// <summary>Scripted run for the render check: a 2-player game started directly, some P1 input, one frame to PNG.</summary>
        void RunShot()
        {
            var sim = new Simulation();
            sim.StartGame(onePlayer: false, GameConfig.Default);
            for (int f = 0; f < _shotPasses; f++)
                sim.Step(new FrameInput { P1 = new PlayerInput { Left = f % 60 < 15, Thrust = f % 5 < 3, Fire = f % 11 == 0 } });
            using var rt = new RenderTarget2D(GraphicsDevice, 1024, 768);
            GraphicsDevice.SetRenderTarget(rt);
            GraphicsDevice.Clear(Color.Black);
            Scene.Build(sim.State, _lines);
            _renderer!.Draw(_lines, new Rectangle(0, 0, 1024, 768));
            GraphicsDevice.SetRenderTarget(null);
            using var fs = File.Create(_shotPath!);
            rt.SaveAsPng(fs, 1024, 768);
            Console.WriteLine($"saved {_shotPath} ({_lines.Count} lines)");
        }

        protected override void UnloadContent()
        {
            if (_front != null) { CaptureWindowed(); _store?.Save(_front.Settings); _front.Online.Dispose(); }
            _renderer?.Dispose(); _sound?.Dispose(); base.UnloadContent();
        }
    }
}
