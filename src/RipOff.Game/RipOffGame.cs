using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using RipOff.Core;
using RipOff.Game.Steam;
using RipOff.Net;

namespace RipOff.Game
{
    /// <summary>
    /// MonoGame host: a fixed 38 Hz step (one simulation pass per arcade frame, GAME_RULES §Units / NOTES §18.8),
    /// keyboard/pad input, the menu (local play, Steam host/join by code, direct-IP host/join), online lockstep
    /// play through <see cref="OnlineController"/>, and the line renderer. All rules live in RipOff.Core.
    /// </summary>
    public sealed class RipOffGame : Microsoft.Xna.Framework.Game
    {
        readonly GraphicsDeviceManager _gfx;
        enum AppMode { Menu, Local, Online }
        Simulation _sim = new Simulation();
        AppMode _mode = AppMode.Menu;
        readonly Menu _menu = new Menu();
        readonly SteamPlatform? _steam;
        readonly OnlineController _online;
        readonly System.Text.StringBuilder _typed = new System.Text.StringBuilder();
        readonly GamePadState[] _menuPadPrev = new GamePadState[2];
        int _explosionsPending;
        int _waitingTicks;
        static readonly SoundState Silence = new SoundState { BackgroundMuted = true };
        readonly List<Line> _lines = new List<Line>();
        readonly PadMapper _pads = new PadMapper();
        SoundOutput? _sound;
        LineRenderer? _renderer;
        KeyboardState _prev;
        readonly string? _shotPath;
        readonly int _shotPasses;

        public RipOffGame(string? shotPath = null, int shotPasses = 400, SteamPlatform? steam = null)
        {
            _shotPath = shotPath; _shotPasses = shotPasses; _steam = steam;
            _online = new OnlineController(steam) { FrameSimulated = (f, st) => _explosionsPending += st.Sound.Explosions };
            _menu.SteamAvailable = steam?.Available == true;
            _menu.SteamNote = steam == null ? "STEAM OFF" : steam.Available ? "" : (steam.Status + " - local and direct IP play still work").ToUpperInvariant();
            _menu.Footer = "ARROWS/D-PAD MOVE   ENTER/A SELECT   ESC/B BACK";
            _gfx = new GraphicsDeviceManager(this)
            {
                PreferredBackBufferWidth = 1024,
                PreferredBackBufferHeight = 768,
                SynchronizeWithVerticalRetrace = true,
            };
            IsFixedTimeStep = true;
            TargetElapsedTime = TimeSpan.FromSeconds(1.0 / GameData.PassesPerSecond);
            Window.AllowUserResizing = true;
            Window.Title = Branding.WindowTitle;
            IsMouseVisible = false;
            Window.TextInput += (_, e) => { if (_typed.Length < 32) _typed.Append(e.Character); };
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
            string steam = _steam == null ? "" : _steam.Available ? " Steam" : " no Steam";
            Window.Title = $"{Branding.WindowTitle} - {audio}{pads}{steam}  [M mute, -/+ volume]";
        }

        /// <summary>GamePad.GetState never takes the game down: a failing or absent pad reads as disconnected.</summary>
        static GamePadState ReadPad(int index)
        {
            try { return GamePad.GetState(index, GamePadDeadZone.None); }
            catch (Exception) { return default; }
        }

        /// <summary>
        /// Keys: P1 ←/→ turn, ↑ thrust, Right Ctrl fire; P2 A/D turn, W thrust, Left Ctrl fire;
        /// 5 coin, 1 / 2 start, M mute, − / + volume, Esc back to the menu. Game pads 0 and 1 are OR'd in (PadMapper, README).
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
            var b = _sound?.Board;
            bool title = false;
            if (b != null)
            {
                if (Edge(Keys.M)) { b.Muted = !b.Muted; title = true; }
                if (Edge(Keys.OemMinus) || Edge(Keys.Subtract)) { b.Volume = Math.Max(0, Math.Round(b.Volume * 10 - 1) / 10); b.Muted = false; title = true; }
                if (Edge(Keys.OemPlus) || Edge(Keys.Add)) { b.Volume = Math.Min(1, Math.Round(b.Volume * 10 + 1) / 10); b.Muted = false; title = true; }
            }
            bool c0 = _pads.IsConnected(0), c1 = _pads.IsConnected(1);
            var pf = _pads.Update(ReadPad(0), ReadPad(1));
            if (title || c0 != _pads.IsConnected(0) || c1 != _pads.IsConnected(1)) UpdateTitle();
            return PadMapper.Combine(f, pf);
        }

        /// <summary>Menu navigation edges: keyboard arrows/Enter/Esc/Backspace and typed text; pads D-pad/left stick, A/Start, B/Back.</summary>
        MenuInput ReadMenuInput(KeyboardState k)
        {
            bool Edge(Keys key) => k.IsKeyDown(key) && !_prev.IsKeyDown(key);
            var m = new MenuInput
            {
                Up = Edge(Keys.Up), Down = Edge(Keys.Down), Left = Edge(Keys.Left), Right = Edge(Keys.Right),
                Select = Edge(Keys.Enter), Back = Edge(Keys.Escape), Backspace = Edge(Keys.Back),
                Typed = _typed.ToString(),
            };
            _typed.Clear();
            for (int i = 0; i < 2; i++)
            {
                var s = ReadPad(i); var p = _menuPadPrev[i];
                if (s.IsConnected && p.IsConnected)
                {
                    bool B(Buttons b) => s.IsButtonDown(b) && !p.IsButtonDown(b);
                    m.Up |= B(Buttons.DPadUp) || B(Buttons.LeftThumbstickUp);
                    m.Down |= B(Buttons.DPadDown) || B(Buttons.LeftThumbstickDown);
                    m.Left |= B(Buttons.DPadLeft) || B(Buttons.LeftThumbstickLeft);
                    m.Right |= B(Buttons.DPadRight) || B(Buttons.LeftThumbstickRight);
                    m.Select |= B(Buttons.A) || B(Buttons.Start);
                    m.Back |= B(Buttons.B) || B(Buttons.Back);
                }
                _menuPadPrev[i] = s;
            }
            return m;
        }

        protected override void Update(GameTime gameTime)
        {
            if (_shotPath != null) { RunShot(); Exit(); return; }
            var k = Keyboard.GetState();
            _steam?.RunCallbacks();
            if (_steam?.PendingJoin is ulong lobby) // Steam invite / "Join game" / +connect_lobby
            {
                _steam.PendingJoin = null;
                _online.JoinSteamLobby(lobby);
                _mode = AppMode.Online;
            }
            switch (_mode)
            {
                case AppMode.Menu: UpdateMenu(ReadMenuInput(k)); break;
                case AppMode.Local:
                {
                    var f = ReadInput();
                    _typed.Clear();
                    if (k.IsKeyDown(Keys.Escape) && !_prev.IsKeyDown(Keys.Escape)) { _mode = AppMode.Menu; _menu.ShowMain(); break; }
                    _sim.Step(f);
                    _sound?.Update(_sim.State.Sound);
                    break;
                }
                case AppMode.Online: UpdateOnline(k); break;
            }
            _prev = k;
            base.Update(gameTime);
        }

        void UpdateMenu(in MenuInput mi)
        {
            switch (_menu.Update(mi))
            {
                case MenuAction.LocalPlay: _sim = new Simulation(); _mode = AppMode.Local; break;
                case MenuAction.HostSteam: _online.HostSteam(); _mode = AppMode.Online; break;
                case MenuAction.JoinSteamCode: _online.JoinSteamCode(_menu.EnteredText); _mode = AppMode.Online; break;
                case MenuAction.HostLan: _online.HostLan(); _mode = AppMode.Online; break;
                case MenuAction.JoinLan: _online.JoinLan(_menu.EnteredText); _mode = AppMode.Online; break;
                case MenuAction.Quit: Exit(); break;
            }
            _sound?.Update(Silence); // keep the audio stream fed
        }

        void UpdateOnline(KeyboardState k)
        {
            var s = _online.Session;
            bool inGame = s != null && s.Phase == SessionPhase.Running;
            // In a game: the local player uses either keyboard set (arrows/RCtrl or WASD/LCtrl) or pad 1.
            var f = ReadInput();
            var local = new PlayerInput
            {
                Left = f.P1.Left || f.P2.Left, Right = f.P1.Right || f.P2.Right,
                Thrust = f.P1.Thrust || f.P2.Thrust, Fire = f.P1.Fire || f.P2.Fire,
            };
            if (inGame && k.IsKeyDown(Keys.Escape) && !_prev.IsKeyDown(Keys.Escape)) { LeaveOnline(); return; }
            _explosionsPending = 0;
            _online.Update(local, f.Start1 || f.Start2);
            s = _online.Session;
            if (s?.Sim != null && s.StepsLastUpdate > 0)
            {
                var snd = s.Sim.State.Sound; snd.Explosions = _explosionsPending;
                _sound?.Update(snd);
            }
            else if (s?.Sim != null) { var snd = s.Sim.State.Sound; snd.Explosions = 0; _sound?.Update(snd); }
            else _sound?.Update(Silence);
            _waitingTicks = s != null && s.Waiting ? _waitingTicks + 1 : 0;

            if (s != null && s.Phase == SessionPhase.Running) { _typed.Clear(); return; }
            // Not in a game: status screen with options.
            UpdateStatusScreen();
            switch (_menu.Update(ReadMenuInput(k)))
            {
                case MenuAction.InviteOverlay: _steam?.OpenInviteOverlay(); break;
                case MenuAction.Cancel: LeaveOnline(); break;
            }
        }

        void UpdateStatusScreen()
        {
            var s = _online.Session;
            var lines = _online.Describe();
            if (s != null && s.Phase == SessionPhase.Desynced)
            {
                lines = new List<string> { s.Status.ToUpperInvariant(), "THE TWO GAMES NO LONGER MATCH - THIS ONE HAS BEEN STOPPED" };
                if (_online.DesyncReportPath != null) lines.Add("REPORT: " + Path.GetFileName(_online.DesyncReportPath).ToUpperInvariant());
                _menu.ShowStatus("DESYNC", lines, ("BACK TO MENU", MenuAction.Cancel));
                return;
            }
            if (_online.Error != null) { _menu.ShowStatus("DISCONNECTED", lines, ("BACK TO MENU", MenuAction.Cancel)); return; }
            string title = _online.Mode switch
            {
                OnlineController.Kind.SteamHost => "HOST ONLINE",
                OnlineController.Kind.SteamJoin => "JOIN ONLINE",
                OnlineController.Kind.LanHost => "HOST DIRECT IP",
                _ => "JOIN DIRECT IP",
            };
            if (_online.Mode == OnlineController.Kind.SteamHost && _steam?.Code != null)
                _menu.ShowStatus(title, lines, ("INVITE A STEAM FRIEND", MenuAction.InviteOverlay), ("CANCEL", MenuAction.Cancel));
            else
                _menu.ShowStatus(title, lines, ("CANCEL", MenuAction.Cancel));
        }

        void LeaveOnline() { _online.Stop(); _mode = AppMode.Menu; _menu.ShowMain(); }

        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(Color.Black);
            var s = _online.Session;
            if (_mode == AppMode.Local) Scene.Build(_sim.State, _lines);
            else if (_mode == AppMode.Online && s?.Sim != null && s.Phase == SessionPhase.Running)
            {
                Scene.Build(s.Sim.State, _lines);
                VectorFont.Draw(s.IsHost ? "YOU: P1" : "YOU: P2", 8, 8, 12, _lines, false);
                if (_waitingTicks > GameData.PassesPerSecond / 2) VectorFont.DrawCentered("WAITING FOR OTHER PLAYER...", Scene.Width / 2f, 400, 24, _lines);
            }
            else { _lines.Clear(); _menu.Build(_lines); }
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

        protected override void UnloadContent() { _online.Dispose(); _renderer?.Dispose(); _sound?.Dispose(); base.UnloadContent(); }
    }
}
