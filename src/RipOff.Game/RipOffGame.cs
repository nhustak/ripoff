using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RipOff.Core;

namespace RipOff.Game
{
    /// <summary>
    /// MonoGame host: a fixed 38 Hz step (one simulation pass per arcade frame, GAME_RULES §Units / NOTES §18.8),
    /// keyboard input, and the line renderer. All rules live in RipOff.Core.
    /// </summary>
    public sealed class RipOffGame : Microsoft.Xna.Framework.Game
    {
        readonly GraphicsDeviceManager _gfx;
        readonly Simulation _sim = new Simulation();
        readonly List<Line> _lines = new List<Line>();
        readonly PadMapper _pads = new PadMapper();
        SoundOutput? _sound;
        LineRenderer? _renderer;
        KeyboardState _prev;
        readonly string? _shotPath;
        readonly int _shotPasses;

        public RipOffGame(string? shotPath = null, int shotPasses = 400)
        {
            _shotPath = shotPath; _shotPasses = shotPasses;
            _gfx = new GraphicsDeviceManager(this)
            {
                PreferredBackBufferWidth = 1024,
                PreferredBackBufferHeight = 768,
                SynchronizeWithVerticalRetrace = true,
            };
            IsFixedTimeStep = true;
            TargetElapsedTime = TimeSpan.FromSeconds(1.0 / GameData.PassesPerSecond);
            Window.AllowUserResizing = true;
            Window.Title = "Rip-Off (remake)";
            IsMouseVisible = false;
        }

        protected override void LoadContent()
        {
            _renderer = new LineRenderer(GraphicsDevice);
            _sound = new SoundOutput(enable: _shotPath == null);
            UpdateTitle();
        }

        void UpdateTitle()
        {
            var b = _sound?.Board;
            string vol = b == null ? "" : b.Muted ? "muted" : $"volume {Math.Round(b.Volume * 100)}%";
            string audio = _sound == null || _sound.Available ? vol : _sound.Status;
            string pads = (_pads.IsConnected(0) ? " P1 pad" : "") + (_pads.IsConnected(1) ? " P2 pad" : "");
            Window.Title = $"Rip-Off (remake) - {audio}{pads}  [M mute, -/+ volume]";
        }

        /// <summary>GamePad.GetState never takes the game down: a failing or absent pad reads as disconnected.</summary>
        static GamePadState ReadPad(int index)
        {
            try { return GamePad.GetState(index, GamePadDeadZone.None); }
            catch (Exception) { return default; }
        }

        /// <summary>
        /// Keys: P1 ←/→ turn, ↑ thrust, Right Ctrl fire; P2 A/D turn, W thrust, Left Ctrl fire;
        /// 5 coin, 1 / 2 start, M mute, − / + volume, Esc quit. Game pads 0 and 1 are OR'd in (PadMapper, README).
        /// UNCHECKED: RawInput (per-device) keyboard input is deferred; MonoGame's keyboard (SDL2 on DesktopGL) is used.
        /// </summary>
        FrameInput ReadInput()
        {
            var k = Keyboard.GetState();
            bool Edge(Keys key) => k.IsKeyDown(key) && !_prev.IsKeyDown(key);
            var f = new FrameInput
            {
                P1 = new PlayerInput { Left = k.IsKeyDown(Keys.Left), Right = k.IsKeyDown(Keys.Right), Thrust = k.IsKeyDown(Keys.Up), Fire = k.IsKeyDown(Keys.RightControl) },
                P2 = new PlayerInput { Left = k.IsKeyDown(Keys.A), Right = k.IsKeyDown(Keys.D), Thrust = k.IsKeyDown(Keys.W), Fire = k.IsKeyDown(Keys.LeftControl) },
                Coin = Edge(Keys.D5) || Edge(Keys.NumPad5),
                Start1 = Edge(Keys.D1) || Edge(Keys.NumPad1),
                Start2 = Edge(Keys.D2) || Edge(Keys.NumPad2),
            };
            if (k.IsKeyDown(Keys.Escape)) Exit();
            var b = _sound?.Board;
            bool title = false;
            if (b != null)
            {
                if (Edge(Keys.M)) { b.Muted = !b.Muted; title = true; }
                if (Edge(Keys.OemMinus) || Edge(Keys.Subtract)) { b.Volume = Math.Max(0, Math.Round(b.Volume * 10 - 1) / 10); b.Muted = false; title = true; }
                if (Edge(Keys.OemPlus) || Edge(Keys.Add)) { b.Volume = Math.Min(1, Math.Round(b.Volume * 10 + 1) / 10); b.Muted = false; title = true; }
            }
            _prev = k;
            bool c0 = _pads.IsConnected(0), c1 = _pads.IsConnected(1);
            var pf = _pads.Update(ReadPad(0), ReadPad(1));
            if (title || c0 != _pads.IsConnected(0) || c1 != _pads.IsConnected(1)) UpdateTitle();
            return PadMapper.Combine(f, pf);
        }

        protected override void Update(GameTime gameTime)
        {
            if (_shotPath != null) { RunShot(); Exit(); return; }
            _sim.Step(ReadInput());
            _sound?.Update(_sim.State.Sound);
            base.Update(gameTime);
        }

        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(Color.Black);
            Scene.Build(_sim.State, _lines);
            _renderer!.Draw(_lines, GraphicsDevice.Viewport.Bounds);
            base.Draw(gameTime);
        }

        /// <summary>Scripted run for the render check: coin, 2P start, some P1 input, then one frame to PNG.</summary>
        void RunShot()
        {
            _sim.Step(new FrameInput { Coin = true });
            _sim.Step(new FrameInput { Coin = true });
            _sim.Step(new FrameInput { Start2 = true });
            for (int f = 0; f < _shotPasses; f++)
                _sim.Step(new FrameInput { P1 = new PlayerInput { Left = f % 60 < 15, Thrust = f % 5 < 3, Fire = f % 11 == 0 } });
            using var rt = new RenderTarget2D(GraphicsDevice, 1024, 768);
            GraphicsDevice.SetRenderTarget(rt);
            GraphicsDevice.Clear(Color.Black);
            Scene.Build(_sim.State, _lines);
            _renderer!.Draw(_lines, new Rectangle(0, 0, 1024, 768));
            GraphicsDevice.SetRenderTarget(null);
            using var fs = File.Create(_shotPath!);
            rt.SaveAsPng(fs, 1024, 768);
            Console.WriteLine($"saved {_shotPath} ({_lines.Count} lines)");
        }

        protected override void UnloadContent() { _renderer?.Dispose(); _sound?.Dispose(); base.UnloadContent(); }
    }
}
