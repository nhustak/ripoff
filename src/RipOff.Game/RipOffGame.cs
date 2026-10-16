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
        readonly SoundStub _sound = new SoundStub();
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

        protected override void LoadContent() => _renderer = new LineRenderer(GraphicsDevice);

        /// <summary>
        /// Keys: P1 ←/→ turn, ↑ thrust, Right Ctrl fire; P2 A/D turn, W thrust, Left Ctrl fire;
        /// 5 coin, 1 / 2 start, Esc quit. UNCHECKED: RawInput (per-device) input is deferred; MonoGame's keyboard
        /// (SDL2 on DesktopGL) is used for now.
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
            _prev = k;
            return f;
        }

        protected override void Update(GameTime gameTime)
        {
            if (_shotPath != null) { RunShot(); Exit(); return; }
            _sim.Step(ReadInput());
            _sound.Update(_sim.State.Sound);
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

        protected override void UnloadContent() { _renderer?.Dispose(); base.UnloadContent(); }
    }
}
