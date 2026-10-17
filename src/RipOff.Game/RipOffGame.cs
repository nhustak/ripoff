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
        readonly SteamPlatform? _steam;
        readonly FrontEnd? _front;
        readonly SettingsStore? _store;
        SoundOutput? _sound;
        LineRenderer? _renderer;
        readonly string? _shotPath;
        readonly int _shotPasses;
        bool _fullscreenApplied;
        string _title = "";

        public RipOffGame(string? shotPath = null, int shotPasses = 400, SteamPlatform? steam = null,
            Settings? settings = null, SettingsStore? store = null, string? notice = null)
        {
            _shotPath = shotPath; _shotPasses = shotPasses; _steam = steam; _store = store;
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
            Window.Title = Branding.WindowTitle;
            IsMouseVisible = false;
            Window.TextInput += (_, e) => { if (_typed.Length < 32) _typed.Append(e.Character); };
            if (shotPath == null)
            {
                var s = settings ?? new Settings();
                _front = new FrontEnd(s, new OnlineController(steam), steam, st => { if (_store?.Save(st) is { } err) _front?.Notify(err); }, notice);
            }
        }

        protected override void Initialize()
        {
            if (_front != null && _front.Settings.Fullscreen) ApplyFullscreen(true);
            base.Initialize();
        }

        protected override void LoadContent()
        {
            _renderer = new LineRenderer(GraphicsDevice);
            _sound = new SoundOutput(enable: _shotPath == null);
            ApplyAudio();
        }

        void ApplyFullscreen(bool on)
        {
            _fullscreenApplied = on;
            if (_gfx.IsFullScreen == on) return;
            if (on)
            {
                var dm = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode;
                _gfx.PreferredBackBufferWidth = dm.Width; _gfx.PreferredBackBufferHeight = dm.Height;
            }
            else { _gfx.PreferredBackBufferWidth = 1024; _gfx.PreferredBackBufferHeight = 768; }
            _gfx.IsFullScreen = on;
            _gfx.ApplyChanges();
        }

        void ApplyAudio()
        {
            var b = _sound?.Board; if (b == null || _front == null) return;
            b.Volume = _front.Settings.Volume; b.Muted = _front.Settings.Muted;
        }

        /// <summary>GamePad.GetState never takes the game down: a failing or absent pad reads as disconnected.</summary>
        static GamePadState ReadPad(int index)
        {
            try { return GamePad.GetState(index, GamePadDeadZone.None); }
            catch (Exception) { return default; }
        }

        protected override void Update(GameTime gameTime)
        {
            if (_shotPath != null) { RunShot(); Exit(); return; }
            var front = _front!;
            _steam?.RunCallbacks();
            int padCount = Math.Min(InputReader.MaxPads, GamePad.MaximumGamePadCount);
            for (int i = 0; i < _pads.Length; i++) _pads[i] = i < padCount ? ReadPad(i) : default;
            _input.Pads.StickDeadzone = front.Settings.StickDeadzone;
            var (ui, play) = _input.Read(Keyboard.GetState(), _pads, _typed.ToString());
            _typed.Clear();

            front.Update(ui, play);
            if (front.ExitRequested) { _store?.Save(front.Settings); Exit(); return; }

            if (front.Settings.Fullscreen != _fullscreenApplied) ApplyFullscreen(front.Settings.Fullscreen);
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
            string steam = _steam == null ? "" : _steam.Available ? ", Steam" : ", no Steam";
            string t = $"{Branding.WindowTitle} - {audio}, {padText}{steam}";
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
            if (_front != null) { _store?.Save(_front.Settings); _front.Online.Dispose(); }
            _renderer?.Dispose(); _sound?.Dispose(); base.UnloadContent();
        }
    }
}
