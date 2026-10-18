using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework.Input;
using RipOff.Core;
using RipOff.Net;

namespace RipOff.Game
{
    /// <summary>
    /// The game's front end as a pure state machine (no MonoGame): splash, main menu, online menus, options,
    /// on-screen keyboard, local and online play, pause and game over. Fed one <see cref="UiInput"/> +
    /// <see cref="PlayInput"/> per 38 Hz tick by RipOffGame; draws into a line list. Screens form a stack; Back
    /// (Esc / B) pops it. Only the EXIT items (main menu, pause menu) set <see cref="ExitRequested"/>.
    /// Menu tree and controls: README "Controls" / NOTES §23.
    /// </summary>
    public sealed class FrontEnd
    {
        public enum Screen { Splash, Main, Online, Options, Keyboard, Connecting, LocalGame, OnlineGame, Pause, GameOver, InputTest, ModeSelect, EnhancedOptions, ShipSelect }
        public enum GameKind { None, OnePlayer, TwoPlayer, Online }

        /// <summary>Splash auto-advance (any key or button skips it sooner).</summary>
        /// <summary>Ticks per second (Tps = 38).</summary>
        public const int Tps = 38;
        public const int SplashTicks = 6 * Tps;
        /// <summary>Game-over screen ignores input this long, so a held fire/A does not skip it.</summary>
        public const int GameOverLockTicks = Tps;

        private static readonly SoundState Silence = new() { BackgroundMuted = true };

        private readonly List<Screen> _stack = new() { Screen.Splash };
        private readonly Dictionary<Screen, int> _sel = new();
        private readonly ISteamUi? _steam;
        private readonly Action<Settings>? _save;
        private OnScreenKeyboard? _kb;
        private string _kbTitle = "";
        private Action<string>? _kbDone;
        /// <summary>What MODE SELECT starts once a mode is picked (local game or hosting).</summary>
        private Action? _modeDone;
        private int _ticks, _splashTicks, _gameOverTicks, _waitingTicks, _explosions;
        private bool _startPulse;
        private string? _notice;
        private int _noticeTicks;

        public Settings Settings { get; }
        public OnlineController Online { get; }
        public Simulation? LocalSim { get; private set; }
        public GameKind Game { get; private set; }
        public bool ExitRequested { get; private set; }
        /// <summary>Sound state for this tick (silence in menus and while a local game is paused).</summary>
        public SoundState Sound { get; private set; } = Silence;
        public IReadOnlyList<Screen> Stack => _stack;
        public Screen Top => _stack[^1];
        public int Selected => _sel.TryGetValue(Top, out var i) ? i : 0;
        public OnScreenKeyboard? Keyboard => _kb;
        /// <summary>UDP port for HOST DIRECT IP (0 = any free port; tests).</summary>
        public int LanPort { get; set; } = RipOff.Net.LiteNetTransport.DefaultPort;
        /// <summary>Labels of the menu on top (empty for non-menu screens).</summary>
        public List<string> CurrentLabels => Items().ConvertAll(i => i.Label);
        /// <summary>The notes under the current screen title (tests).</summary>
        public List<string> Notes => Header().notes;
        public string Title => Header().title;
        /// <summary>Whether each item of the menu on top can be chosen.</summary>
        public List<bool> CurrentEnabled => Items().ConvertAll(i => i.Enabled);

        public FrontEnd(Settings settings, OnlineController online, ISteamUi? steam = null, Action<Settings>? save = null, string? notice = null)
        {
            Settings = settings; Online = online; _steam = steam; _save = save;
            online.FrameSimulated = (_, s) => _explosions += s.Sound.Explosions;
            if (notice != null) Notify(notice);
        }

        public bool SteamAvailable => _steam?.Available == true;
        /// <summary>Steam options can be picked: Steam not tried yet (starts lazily on pick) or tried and working.</summary>
        public bool SteamSelectable => _steam != null && (!_steam.Initialized || _steam.Available);

        /// <summary>Raw pad slots 0-3 as the host read them this tick (INPUT TEST screen). Filled by the host / tests.</summary>
        public readonly PadSlot[] Pads = new PadSlot[InputReader.MaxPads];
        /// <summary>Gameplay controls seen this tick (INPUT TEST screen).</summary>
        public PlayInput LastPlay { get; private set; }
        /// <summary>A game (not a menu) is on top: Steam Input then uses Spacewar's ship action set, else its menu set.</summary>
        public bool InGame => Top is Screen.LocalGame or Screen.OnlineGame;
        /// <summary>Where the pads come from this tick (INPUT TEST), set by the host.</summary>
        public string PadSource { get; set; } = "SDL (MONOGAME GAMEPAD)";
        /// <summary>Ticks the INPUT TEST screen needs B held (or Esc) to leave, so B itself can be tested.</summary>
        public const int InputTestHoldTicks = Tps;
        private int _bHeld;

        /// <summary>Starts Steam on first use (lazy, NOTES §24). False (with a notice) when it cannot start.</summary>
        private bool SteamReady()
        {
            if (_steam == null) return false;
            bool first = !_steam.Initialized;
            bool ok = _steam.EnsureInit();
            if (!ok) Notify(_steam.Status);
            else if (first) Notify("Steam started. If the controller stops responding, see README: Steam Input");
            return ok;
        }

        public void Notify(string text, int seconds = 6) { _notice = text; _noticeTicks = seconds * Tps; }

        // ================================================================= update

        public void Update(in UiInput ui, in PlayInput play)
        {
            _ticks++;
            if (_noticeTicks > 0 && --_noticeTicks == 0) _notice = null;
            Sound = Silence;
            _startPulse = false;
            LastPlay = play;

            if (Top != Screen.Keyboard) Shortcuts(ui);

            if (_steam?.Initialized == true && _steam.TakePendingJoin() is ulong lobby) // Steam invite / friends list "Join game" / +connect_lobby
            {
                EndGame();
                Online.GuestShip = Settings.Ship1; // an invite skips SHIP SELECT: the last pick is the wish
                Online.JoinSteamLobby(lobby);
                SetStack(Screen.Main, Screen.Online, Screen.Connecting);
            }

            if (ui.Options && Top is not (Screen.Options or Screen.EnhancedOptions or Screen.Keyboard))
            {
                if (Top == Screen.Splash) SetStack(Screen.Main);
                Push(Screen.Options);
            }
            else
            {
                switch (Top)
                {
                    case Screen.Splash:
                        if (ui.Any || ui.Select || ui.Back || ui.Start || ++_splashTicks >= SplashTicks) SetStack(Screen.Main);
                        break;
                    case Screen.LocalGame:
                    case Screen.OnlineGame:
                        if (ui.Escape || ui.Start) Push(Screen.Pause);
                        break;
                    case Screen.Keyboard:
                        switch (_kb!.Update(ui))
                        {
                            case OnScreenKeyboard.Result.Ok: Pop(); _kbDone?.Invoke(_kb.Text); break;
                            case OnScreenKeyboard.Result.Cancel: Pop(); break;
                        }
                        break;
                    case Screen.InputTest:
                        // Every button is being tested here, so B only leaves when held; Esc leaves at once.
                        bool bDown = false;
                        foreach (var p in Pads) bDown |= p.Connected && p.State.Buttons.B == ButtonState.Pressed;
                        _bHeld = bDown ? _bHeld + 1 : 0;
                        if (ui.Escape || _bHeld >= InputTestHoldTicks) { _bHeld = 0; Pop(); }
                        break;
                    case Screen.Pause:
                        if (ui.Back || ui.Start) { Pop(); break; }
                        MenuUpdate(ui);
                        break;
                    case Screen.GameOver:
                        if (++_gameOverTicks < GameOverLockTicks) break;
                        if (ui.Escape || ui.Start) { Push(Screen.Pause); break; }
                        MenuUpdate(ui);
                        break;
                    default:
                        MenuUpdate(ui);
                        break;
                }
            }

            TickGames(play);
        }

        private void Shortcuts(in UiInput ui)
        {
            bool changed = false;
            if (ui.Mute) { Settings.Muted = !Settings.Muted; changed = true; }
            if (ui.VolumeDown) { Settings.Volume = Math.Max(0, Settings.Volume - Settings.VolumeStep); Settings.Muted = false; changed = true; }
            if (ui.VolumeUp) { Settings.Volume = Math.Min(1, Settings.Volume + Settings.VolumeStep); Settings.Muted = false; changed = true; }
            if (changed) { Settings.Normalize(); _save?.Invoke(Settings); }
        }

        private void TickGames(in PlayInput play)
        {
            // Local: the simulation only runs while the game itself is on top (menus pause it).
            if (Game is GameKind.OnePlayer or GameKind.TwoPlayer && LocalSim != null && Top == Screen.LocalGame)
            {
                var input = Game == GameKind.OnePlayer ? new FrameInput { P1 = play.All } : new FrameInput { P1 = play.P1, P2 = play.P2 };
                LocalSim.Step(input);
                Sound = LocalSim.State.Sound;
                if (LocalSim.State.Mode != GameMode.Playing) { _gameOverTicks = 0; Push(Screen.GameOver); }
            }

            // Online: the shared simulation never pauses (NOTES §23). Menus overlay it; this player's controls
            // read as released while a menu is open.
            if (Online.Active)
            {
                var local = Top == Screen.OnlineGame ? play.All : default;
                _explosions = 0;
                Online.Update(local, _startPulse);
                var s = Online.Session;
                if (s?.Sim != null)
                {
                    var snd = s.Sim.State.Sound; snd.Explosions = s.StepsLastUpdate > 0 ? _explosions : 0;
                    Sound = snd;
                }
                _waitingTicks = s != null && s.Waiting ? _waitingTicks + 1 : 0;

                bool failed = Online.Error != null || s?.Phase is SessionPhase.Desynced or SessionPhase.Closed;
                if (failed && Top != Screen.Connecting) { Game = GameKind.None; SetStack(Screen.Main, Screen.Connecting); }
                else if (!failed && Online.Running && Game != GameKind.Online) { Game = GameKind.Online; SetStack(Screen.Main, Screen.OnlineGame); }
                if (Game == GameKind.Online && s?.Sim != null)
                {
                    bool over = s.Sim.State.Mode != GameMode.Playing;
                    if (over && Top == Screen.OnlineGame) { _gameOverTicks = 0; Push(Screen.GameOver); }
                    else if (!over && Top == Screen.GameOver) Pop();
                }
            }
        }

        // ================================================================= menus

        private sealed record Item(string Label, Action? Select, Action<int>? Adjust = null, bool Enabled = true);

        private List<Item> Items()
        {
            var l = new List<Item>();
            switch (Top)
            {
                case Screen.Main:
                    l.Add(new("START 1 PLAYER", () => ChooseMode(() => StartLocal(true), ShipPick.OnePlayer)));
                    l.Add(new("START 2 PLAYERS", () => ChooseMode(() => StartLocal(false), ShipPick.TwoPlayers)));
                    l.Add(new("ONLINE", () => Push(Screen.Online)));
                    l.Add(new("OPTIONS", () => Push(Screen.Options)));
                    l.Add(new("EXIT", () => ExitRequested = true));
                    break;
                case Screen.Online:
                    l.Add(new("HOST ONLINE (STEAM)", () => { if (!SteamReady()) return; ChooseMode(() => { Online.HostSteam(Settings.GameConfig); Push(Screen.Connecting); }, ShipPick.Host); }, Enabled: SteamSelectable));
                    l.Add(new("JOIN BY CODE (STEAM)", () => { if (!SteamReady()) return; ChooseGuestShip(() => OpenKeyboard(OnScreenKeyboard.Mode.LobbyCode, "ENTER LOBBY CODE",
                        code => { Online.GuestShip = Settings.Ship1; Online.JoinSteamCode(code); Push(Screen.Connecting); })); }, Enabled: SteamSelectable));
                    l.Add(new("HOST DIRECT IP", () => ChooseMode(() => { Online.HostLan(Settings.GameConfig, LanPort); Push(Screen.Connecting); }, ShipPick.Host)));
                    l.Add(new("JOIN DIRECT IP", () => ChooseGuestShip(() => OpenKeyboard(OnScreenKeyboard.Mode.Address, "HOST ADDRESS (IP OR IP:PORT)",
                        addr => { Online.GuestShip = Settings.Ship1; Online.JoinLan(addr); Push(Screen.Connecting); }))));
                    l.Add(new("BACK", Pop));
                    break;
                case Screen.ModeSelect:
                    foreach (var m in new[] { PlayMode.Arcade, PlayMode.Enhanced })
                        l.Add(new(Branding.ModeName(m), () => PickMode(m)));
                    l.Add(new("BACK", Pop));
                    break;
                case Screen.ShipSelect:
                {
                    int taken = ShipTakenNow;
                    foreach (var sh in Ships.All)
                    {
                        int id = sh.Id;
                        bool free = id != taken;
                        l.Add(new(free ? sh.Name : sh.Name + " - PLAYER 1", free ? () => PickShip(id) : null, Enabled: free));
                    }
                    l.Add(new("BACK", () => BackFrom(Screen.ShipSelect)));
                    break;
                }
                case Screen.Connecting:
                    bool ended = Online.Error != null || Online.Session?.Phase is SessionPhase.Desynced or SessionPhase.Closed || !Online.Active;
                    if (!ended && Online.Mode == OnlineController.Kind.SteamHost && _steam?.Code != null)
                        l.Add(new("INVITE A STEAM FRIEND", () => _steam.OpenInviteOverlay()));
                    l.Add(new(ended ? "BACK" : "CANCEL", LeaveOnline));
                    break;
                case Screen.Options:
                    l.Add(new("AUTO FIRE: " + (Settings.AutoFire ? "ON" : "OFF"), () => Change(() => Settings.AutoFire = !Settings.AutoFire),
                        d => Change(() => Settings.AutoFire = !Settings.AutoFire)));
                    l.Add(new("CANISTERS: " + Settings.Canisters, () => Change(() => CycleCanisters(1)), d => Change(() => CycleCanisters(d))));
                    l.Add(new($"VOLUME: {Math.Round(Settings.Volume * 100)}%", () => Change(() => Settings.Volume = Settings.Volume >= 1 ? 0 : Settings.Volume + Settings.VolumeStep),
                        d => Change(() => { Settings.Volume = Math.Clamp(Settings.Volume + d * Settings.VolumeStep, 0, 1); Settings.Muted = false; })));
                    l.Add(new("MUTE: " + (Settings.Muted ? "ON" : "OFF"), () => Change(() => Settings.Muted = !Settings.Muted), d => Change(() => Settings.Muted = !Settings.Muted)));
                    l.Add(new("DISPLAY: " + (Settings.Fullscreen ? "FULLSCREEN" : "WINDOWED"), () => Change(() => Settings.Fullscreen = !Settings.Fullscreen),
                        d => Change(() => Settings.Fullscreen = !Settings.Fullscreen)));
                    l.Add(new($"STICK DEAD ZONE: {Math.Round(Settings.StickDeadzone * 100)}%", () => Change(() => Settings.StickDeadzone = Settings.StickDeadzone >= Settings.DeadzoneMax - 0.001f ? Settings.DeadzoneMin : Settings.StickDeadzone + Settings.DeadzoneStep),
                        d => Change(() => Settings.StickDeadzone = Math.Clamp(Settings.StickDeadzone + d * Settings.DeadzoneStep, Settings.DeadzoneMin, Settings.DeadzoneMax))));
                    l.Add(new("STEAM AT LAUNCH: " + (Settings.SteamAtLaunch ? "ON" : "OFF"), () => Change(() => Settings.SteamAtLaunch = !Settings.SteamAtLaunch),
                        d => Change(() => Settings.SteamAtLaunch = !Settings.SteamAtLaunch)));
                    l.Add(new("ENHANCED OPTIONS: " + EnhancedSummary(Settings.EnhancedOptions), () => Push(Screen.EnhancedOptions)));
                    l.Add(new("INPUT TEST", () => { _bHeld = 0; Push(Screen.InputTest); }));
                    l.Add(new("BACK", Pop));
                    break;
                case Screen.EnhancedOptions:
                    foreach (var v in GameRules.Variations)
                    {
                        var opt = v.Option;
                        bool on = (Settings.EnhancedOptions & opt) != 0;
                        void Toggle() => Change(() => Settings.EnhancedOptions ^= opt);
                        // A row that needs another option (RANDOM WARP-IN, WARP KILLS need WARP-IN) is dimmed and can't
                        // be changed while that one is off; its setting is kept and used again once it's back on.
                        l.Add(new(v.Label + ": " + (on ? "ON" : "OFF"), Toggle, _ => Toggle(), Enabled: v.Applies(Settings.EnhancedOptions)));
                    }
                    l.Add(new("ALL ON", () => Change(() => Settings.EnhancedOptions = EnhancedOption.All), Enabled: Settings.EnhancedOptions != EnhancedOption.All));
                    l.Add(new("BACK", Pop));
                    break;
                case Screen.Pause:
                    l.Add(new("RESUME", Pop));
                    l.Add(new("OPTIONS", () => Push(Screen.Options)));
                    l.Add(new(Game == GameKind.Online ? "LEAVE ONLINE GAME" : "QUIT TO MAIN MENU", () => { EndGame(); SetStack(Screen.Main); }));
                    l.Add(new("EXIT TO DESKTOP", () => ExitRequested = true));
                    break;
                case Screen.GameOver:
                    if (Game == GameKind.Online)
                    {
                        l.Add(new("PLAY AGAIN", () => _startPulse = true));
                        l.Add(new("LEAVE ONLINE GAME", () => { EndGame(); SetStack(Screen.Main); }));
                    }
                    else
                    {
                        l.Add(new("PLAY AGAIN", () => StartLocal(Game == GameKind.OnePlayer)));
                        l.Add(new("MAIN MENU", () => { EndGame(); SetStack(Screen.Main); }));
                    }
                    break;
            }
            return l;
        }

        private void MenuUpdate(in UiInput ui)
        {
            var items = Items();
            if (items.Count == 0) return;
            int sel = Math.Clamp(Selected, 0, items.Count - 1);
            if (ui.Up) sel = (sel + items.Count - 1) % items.Count;
            if (ui.Down) sel = (sel + 1) % items.Count;
            _sel[Top] = sel;
            var it = items[sel];
            if ((ui.Left || ui.Right) && it.Adjust != null && it.Enabled) it.Adjust(ui.Right ? 1 : -1);
            if (ui.Select && it.Enabled) { it.Select?.Invoke(); return; }
            if (ui.Back && Top is not (Screen.Main or Screen.GameOver)) BackFrom(Top);
            else if (ui.Back && Top == Screen.GameOver && Game != GameKind.Online) { EndGame(); SetStack(Screen.Main); }
        }

        private void BackFrom(Screen s)
        {
            if (s == Screen.Connecting) LeaveOnline();
            else if (s == Screen.ShipSelect && _shipStep > 0) { _shipStep--; _sel[Screen.ShipSelect] = ShipOf(_shipStep); }
            else Pop();
        }

        private void Change(Action a) { a(); Settings.Normalize(); _save?.Invoke(Settings); }

        private void CycleCanisters(int d)
        {
            var order = new[] { 4, 8, 12, 16 };
            int i = Array.IndexOf(order, Settings.Canisters); if (i < 0) i = 1;
            Settings.Canisters = order[(i + d + order.Length) % order.Length];
        }

        /// <summary>The SHIP SELECT stats line: "SPEED 14  TURN 10  ARMOR 1  DAMAGE 1", or a note that SHIP ABILITIES is
        /// off (every ship then flies like the arcade's). Online the host's options decide.</summary>
        public static string ShipStatsText(int ship, GameRules rules)
        {
            if (!Ships.AbilitiesOn(rules)) return "SHIP ABILITIES OFF - EVERY SHIP FLIES LIKE THE ARCADE'S";
            var (speed, turn, armor, damage) = Ships.Stats(rules, ship);
            return $"SPEED {speed}  TURN {turn}  ARMOR {armor}  DAMAGE {damage}";
        }

        /// <summary>Who picks a ship on SHIP SELECT (NOTES §30) after ENHANCED is chosen with SHIP SELECT on.</summary>
        public enum ShipPick { None, OnePlayer, TwoPlayers, Host, Guest }

        /// <summary>Opens MODE SELECT with the last mode highlighted; <paramref name="then"/> runs after a pick (and, for
        /// ENHANCED with SHIP SELECT on, after SHIP SELECT for <paramref name="ships"/>).</summary>
        private void ChooseMode(Action then, ShipPick ships = ShipPick.None)
        {
            _modeDone = then; _modeShips = ships;
            Push(Screen.ModeSelect);
            _sel[Screen.ModeSelect] = Settings.Mode == PlayMode.Enhanced ? 1 : 0;
        }

        private void PickMode(PlayMode m)
        {
            Change(() => Settings.Mode = m); // remembered in settings.json
            Pop();
            var then = _modeDone; _modeDone = null;
            if (then != null && GameRules.For(m, Settings.EnhancedOptions).ShipSelect && _modeShips != ShipPick.None) ChooseShips(_modeShips, then);
            else then?.Invoke();
        }

        /// <summary>Joining: the guest picks the ship it would like first, when its own ENHANCED OPTIONS have SHIP SELECT
        /// on (the host's settings decide whether ships are used at all); otherwise its last pick is sent.</summary>
        private void ChooseGuestShip(Action then)
        {
            if (GameRules.For(PlayMode.Enhanced, Settings.EnhancedOptions).ShipSelect) ChooseShips(ShipPick.Guest, then);
            else then();
        }

        private ShipPick _modeShips;
        private Action? _shipDone;
        private int _shipStep;

        /// <summary>The kind of SHIP SELECT open (for its title and notes).</summary>
        public ShipPick ShipPicking { get; private set; }

        private void ChooseShips(ShipPick who, Action then)
        {
            ShipPicking = who; _shipDone = then; _shipStep = 0;
            Push(Screen.ShipSelect);
            _sel[Screen.ShipSelect] = ShipOf(0);
        }

        private int ShipOf(int step) => step == 0 ? Settings.Ship1 : (Settings.Ship2 == Settings.Ship1 ? Ships.FirstOtherThan(Settings.Ship1) : Settings.Ship2);

        /// <summary>The ship nobody may pick right now: P1's, while P2 picks in a local 2-player game; -1 otherwise.</summary>
        private int ShipTakenNow => ShipPicking == ShipPick.TwoPlayers && _shipStep == 1 ? Settings.Ship1 : -1;

        private void PickShip(int id)
        {
            if (id == ShipTakenNow) return; // never reached: that item is disabled
            if (_shipStep == 0)
            {
                Change(() => Settings.Ship1 = id); // Normalize moves Ship2 off it if they clash
                if (ShipPicking == ShipPick.TwoPlayers) { _shipStep = 1; _sel[Screen.ShipSelect] = ShipOf(1); return; }
            }
            else Change(() => Settings.Ship2 = id);
            Pop();
            var then = _shipDone; _shipDone = null; ShipPicking = ShipPick.None;
            then?.Invoke();
        }

        private void OpenKeyboard(OnScreenKeyboard.Mode mode, string title, Action<string> done)
        {
            _kb = new OnScreenKeyboard(mode); _kbTitle = title; _kbDone = done;
            Push(Screen.Keyboard);
        }

        // ================================================================= games

        /// <summary>Starts a local game at once (no credits, no start button): the settings' canisters and auto-fire.</summary>
        /// <summary>Per-game salt for the Enhanced RNG of local games (tests replace it for fixed results).</summary>
        public Func<uint> LocalSeed { get; set; } = () => (uint)Random.Shared.Next() ^ (uint)Environment.TickCount;

        public void StartLocal(bool onePlayer)
        {
            EndGame();
            LocalSim = new Simulation();
            LocalSim.StartGame(onePlayer, Settings.GameConfig);
            LocalSim.State.SaltEnhancedRng(LocalSeed()); // Enhanced only: a new random warp-in spot each game
            Game = onePlayer ? GameKind.OnePlayer : GameKind.TwoPlayer;
            SetStack(Screen.Main, Screen.LocalGame);
        }

        private void EndGame()
        {
            if (Online.Active) Online.Stop();
            LocalSim = null; Game = GameKind.None;
        }

        private void LeaveOnline()
        {
            Online.Stop(); Game = GameKind.None;
            _stack.RemoveAll(s => s == Screen.Connecting);
            if (_stack.Count == 0) _stack.Add(Screen.Main);
        }

        private void Push(Screen s) { _stack.Add(s); _sel[s] = 0; }
        private void Pop() { if (_stack.Count > 1) _stack.RemoveAt(_stack.Count - 1); }
        private void SetStack(params Screen[] s) { _stack.Clear(); _stack.AddRange(s); _sel[s[^1]] = 0; }

        // ================================================================= drawing

        public void Build(List<Line> o)
        {
            o.Clear();
            const float cx = Scene.Width / 2f;
            var online = Online.Session;
            bool onlineScene = Game == GameKind.Online && online?.Sim != null;

            if (Top == Screen.LocalGame && LocalSim != null) { Scene.Build(LocalSim.State, o); }
            else if (onlineScene && (Top is Screen.OnlineGame or Screen.Pause or Screen.Options or Screen.EnhancedOptions or Screen.GameOver))
            {
                Scene.Build(online!.Sim!.State, o);
                if (Top != Screen.OnlineGame) for (int i = 0; i < o.Count; i++) o[i] = o[i] with { Bright = false }; // overlay: game dimmed, still running
                VectorFont.Draw((online.IsHost ? "YOU: P1  " : "YOU: P2  ") + Branding.ModeName(online.Sim!.State.PlayMode), 8, 8, 12, o, false);
                if (Top == Screen.OnlineGame && _waitingTicks > Tps / 2)
                    VectorFont.DrawCentered("WAITING FOR OTHER PLAYER...", cx, 400, 24, o);
            }

            switch (Top)
            {
                case Screen.Splash: BuildSplash(o); break;
                case Screen.LocalGame: case Screen.OnlineGame: break;
                case Screen.Keyboard: _kb!.Build(o, _kbTitle); break;
                case Screen.InputTest: BuildInputTest(o); break;
                default: BuildMenu(o); break;
            }
            if (_notice != null) VectorFont.DrawCentered(_notice.ToUpperInvariant(), cx, 20, Fit(_notice, 13), o, false);
        }

        private void BuildSplash(List<Line> o)
        {
            const float cx = Scene.Width / 2f, cy = Scene.Height / 2f;
            // Title from Branding, revealed left to right over the first second, inside a frame that grows in.
            string title = Branding.GameName;
            float size = Fit(title, 64, 900);
            float t = Math.Min(1f, _splashTicks / (float)Tps);
            int shown = (int)Math.Ceiling(title.Length * t);
            float w = VectorFont.Width(title, size);
            VectorFont.Draw(title[..shown], cx - w / 2f, cy, size, o);
            float fw = (w / 2f + 40) * t, fh = (size + 50) * t;
            OnScreenKeyboard.Box(cx - fw, cy - 40 - (fh - size - 50) / 2f, cx + fw, cy + size + 10, o);
            if ((_splashTicks / (Tps / 2)) % 2 == 0 || _splashTicks < Tps)
                VectorFont.DrawCentered("PRESS ANY KEY OR BUTTON", cx, cy - 160, 20, o, t >= 1f);
        }

        /// <summary>SHIP SELECT preview size (scale/128; the ships are 128 in play).</summary>
        public const int ShipPreviewScale = 120;

        private void BuildMenu(List<Line> o)
        {
            const float cx = Scene.Width / 2f;
            var items = Items();
            var (title, notes) = Header();
            float top = 650;
            if (Top == Screen.Main) VectorFont.DrawCentered(Branding.GameName, cx, top, Fit(Branding.GameName, 44, 900), o);
            else VectorFont.DrawCentered(title, cx, top, Fit(title, 34, 900), o);
            float y = 560;
            foreach (var n in notes) { VectorFont.DrawCentered(n, cx, y, Fit(n, n.Length > 36 ? 15 : 20), o, true); y -= 34; }
            y = Math.Min(y - 20, Top == Screen.Main ? 470 : 480);
            float step = items.Count > 1 ? Math.Min(50, (y - 110) / (items.Count - 1)) : 50; // keep clear of the hint line
            for (int i = 0; i < items.Count; i++, y -= step)
            {
                string label = (i == Selected ? "> " : "  ") + items[i].Label + (i == Selected ? " <" : "  ");
                // Long lists (ENHANCED OPTIONS keeps growing): the labels shrink to fit the row spacing.
                VectorFont.DrawCentered(label, cx, y, Fit(label, Math.Min(26, step * 0.8f)), o, items[i].Enabled);
                if (Top == Screen.ShipSelect && i < Ships.Count) // the ship itself, left of its name (NOTES §30)
                    Scene.DrawShip(i, cx - 350, y + 9, 180, ShipPreviewScale, o, items[i].Enabled && i == Selected);
            }
            string hint = Top is Screen.Options or Screen.EnhancedOptions ? "UP/DOWN CHOOSE   LEFT/RIGHT OR A CHANGE   B/ESC BACK"
                : Top == Screen.Main ? "UP/DOWN CHOOSE   A/ENTER SELECT   F10 OPTIONS"
                : "UP/DOWN CHOOSE   A/ENTER SELECT   B/ESC BACK";
            VectorFont.DrawCentered(hint, cx, 50, 14, o, false);
            if (Top is Screen.Main or Screen.Pause) DrawVersion(o);
        }

        /// <summary>Build version in the bottom-right corner (main menu and pause): "V2026.10.17.1" or "DEV ABC1234".</summary>
        public static string VersionText => BuildInfo.Display;

        private static void DrawVersion(List<Line> o)
        {
            const float size = 11;
            string t = VersionText;
            VectorFont.Draw(t, Scene.Width - 10 - VectorFont.Width(t, size), 12, size, o, false);
        }

        private void BuildInputTest(List<Line> o)
        {
            const float cx = Scene.Width / 2f;
            VectorFont.DrawCentered("INPUT TEST", cx, 700, 30, o);
            float y = 640;
            for (int i = 0; i < Pads.Length; i++)
            {
                var p = Pads[i];
                string head = $"PAD SLOT {i}: " + (p.Connected ? PadText.ForFont(p.Name.Length > 0 ? p.Name : "CONNECTED") + (p.Type.Length > 0 ? " (" + PadText.ForFont(p.Type) + ")" : "") : "NOT CONNECTED");
                VectorFont.Draw(head, 40, y, Fit(head, 18, 940), o, p.Connected);
                if (p.Connected)
                {
                    string b = "BUTTONS: " + PadText.ForFont(PadText.Pressed(p.State));
                    string a = PadText.ForFont(PadText.Axes(p.State));
                    VectorFont.Draw(b, 70, y - 30, Fit(b, 15, 900), o);
                    VectorFont.Draw(a, 70, y - 56, Fit(a, 15, 900), o);
                }
                y -= p.Connected ? 110 : 50;
            }
            var all = LastPlay.All;
            string game = "GAME SEES: " + (all.Left ? "LEFT " : "") + (all.Right ? "RIGHT " : "") + (all.Thrust ? "THRUST " : "") + (all.Fire ? "FIRE" : "")
                + (all.Left || all.Right || all.Thrust || all.Fire ? "" : "NOTHING");
            VectorFont.DrawCentered(game, cx, 180, 18, o);
            string src = "PAD SOURCE: " + PadText.ForFont(PadSource);
            VectorFont.DrawCentered(src, cx, 145, Fit(src, 14), o, false);
            string st = "STEAM: " + PadText.ForFont(_steam?.Status ?? "off");
            VectorFont.DrawCentered(st, cx, 110, Fit(st, 14), o, false);
            VectorFont.DrawCentered("HOLD B OR PRESS ESC TO GO BACK", cx, 50, 14, o, false);
        }

        private (string title, List<string> notes) Header()
        {
            var n = new List<string>();
            switch (Top)
            {
                case Screen.Main:
                    return ("", n);
                case Screen.Online:
                    if (_steam == null || (_steam.Initialized && !_steam.Available)) n.Add(PadText.ForFont("STEAM: " + (_steam?.Status ?? "off")));
                    else if (!_steam.Initialized) n.Add("STEAM STARTS WHEN YOU PICK A STEAM OPTION");
                    n.Add($"YOUR RULES WHEN HOSTING: {Settings.Canisters} CANISTERS, AUTO FIRE {(Settings.AutoFire ? "ON" : "OFF")}");
                    n.Add("HOSTS PICK THE MODE; JOINING PLAYERS USE THE HOST'S MODE");
                    return ("ONLINE", n);
                case Screen.ModeSelect:
                {
                    if (Selected <= 1) n.Add(Branding.ModeInfo(Selected == 1 ? PlayMode.Enhanced : PlayMode.Arcade));
                    if (Selected == 1) n.Add("ENHANCED OPTIONS: " + EnhancedSummary(Settings.EnhancedOptions) + " (CHANGE IN OPTIONS)");
                    n.Add("LAST PLAYED: " + Branding.ModeName(Settings.Mode));
                    return (Branding.ModeSelectTitle, n);
                }
                case Screen.ShipSelect:
                {
                    if (Selected < Ships.Count)
                    {
                        n.Add(Ships.Get(Selected).Info);
                        n.Add(ShipStatsText(Selected, GameRules.For(PlayMode.Enhanced, Settings.EnhancedOptions)));
                    }
                    switch (ShipPicking)
                    {
                        case ShipPick.TwoPlayers:
                            n.Add(_shipStep == 0 ? "PLAYER 2 PICKS NEXT - NO TWO PLAYERS ON THE SAME SHIP" : "PLAYER 1 HAS " + Ships.Get(Settings.Ship1).Name);
                            break;
                        case ShipPick.Host:
                            n.Add("YOU ARE PLAYER 1 - THE OTHER PLAYER GETS A DIFFERENT SHIP");
                            break;
                        case ShipPick.Guest:
                            n.Add("USED IF THE HOST PLAYS ENHANCED WITH SHIP SELECT ON");
                            n.Add("IF THE HOST HAS THIS SHIP YOU GET ANOTHER ONE");
                            break;
                    }
                    n.Add("SHIP SELECT CAN BE TURNED OFF IN OPTIONS - ENHANCED OPTIONS");
                    string who = ShipPicking == ShipPick.TwoPlayers ? $"PLAYER {_shipStep + 1} SHIP" : "YOUR SHIP";
                    return ("SHIP SELECT - " + who, n);
                }
                case Screen.Connecting:
                {
                    var s = Online.Session;
                    if (s?.Phase == SessionPhase.Desynced)
                    {
                        n.Add(s.Status.ToUpperInvariant());
                        n.Add("THE TWO GAMES NO LONGER MATCH, SO THIS ONE WAS STOPPED");
                        if (Online.DesyncReportPath != null) n.Add("REPORT: " + Path.GetFileName(Online.DesyncReportPath).ToUpperInvariant());
                        return ("DESYNC", n);
                    }
                    if (s?.Phase == SessionPhase.Closed) { n.Add(s.Status.ToUpperInvariant()); return ("GAME ENDED", n); }
                    n.AddRange(Online.Describe());
                    if (Online.ModeText() is { } mt) n.Add(mt);
                    if (Online.OptionsText() is { } ot) n.Add(ot);
                    if (Online.ShipsText() is { } st) n.Add(st);
                    if (Online.PeerBuildText() is { } pb) n.Add(pb);
                    if (Online.Error != null) return ("DISCONNECTED", n);
                    return (Online.Mode switch
                    {
                        OnlineController.Kind.SteamHost => "HOST ONLINE",
                        OnlineController.Kind.SteamJoin => "JOIN ONLINE",
                        OnlineController.Kind.LanHost => "HOST DIRECT IP",
                        OnlineController.Kind.LanJoin => "JOIN DIRECT IP",
                        _ => "ONLINE",
                    }, n);
                }
                case Screen.Options:
                    if (Game == GameKind.Online) n.Add("ONLINE GAMES USE THE HOST RULES FOR AUTO FIRE AND CANISTERS");
                    else if (Game != GameKind.None) n.Add("AUTO FIRE AND CANISTERS APPLY FROM THE NEXT GAME");
                    return ("OPTIONS", n);
                case Screen.EnhancedOptions:
                {
                    var vs = GameRules.Variations;
                    if (Selected < vs.Length)
                    {
                        var v = vs[Selected];
                        n.Add(v.Info);
                        if (!v.Applies(Settings.EnhancedOptions))
                            n.Add("NEEDS " + Array.Find(vs, x => x.Option == v.Requires)!.Label + " - IGNORED WHILE IT IS OFF");
                    }
                    n.Add("ENHANCED MODE ONLY - ARCADE IGNORES THESE");
                    var mode = Game == GameKind.Online ? Online.Session?.Sim?.State.PlayMode : LocalSim?.State.PlayMode;
                    if (Game == GameKind.Online) n.Add("ONLINE GAMES USE THE HOST'S ENHANCED OPTIONS");
                    else if (Game != GameKind.None && mode == PlayMode.Arcade) n.Add("NOT USED IN THIS ARCADE GAME");
                    else if (Game != GameKind.None) n.Add("CHANGES APPLY FROM THE NEXT GAME");
                    return ("ENHANCED OPTIONS", n);
                }
                case Screen.Pause:
                    if (Game == GameKind.Online) n.Add("THE ONLINE GAME KEEPS RUNNING");
                    return ("PAUSED", n);
                case Screen.GameOver:
                {
                    var st = Game == GameKind.Online ? Online.Session?.Sim?.State : LocalSim?.State;
                    if (st != null)
                    {
                        n.Add("P1 SCORE " + Points(st, 0));
                        if (!st.OnePlayer) n.Add("P2 SCORE " + Points(st, 1));
                    }
                    if (Game == GameKind.Online) n.Add("EITHER PLAYER CAN START THE NEXT GAME");
                    return ("GAME OVER", n);
                }
            }
            return ("", n);
        }

        /// <summary>"ALL ON", "ALL OFF" or "OFF: WARP-IN, SLOWER TOWING".</summary>
        public static string EnhancedSummary(EnhancedOption o)
        {
            if ((o & EnhancedOption.All) == EnhancedOption.All) return "ALL ON";
            var off = new List<string>();
            foreach (var v in GameRules.Variations) if ((o & v.Option) == 0) off.Add(v.Label);
            return off.Count == GameRules.Variations.Length ? "ALL OFF" : "OFF: " + string.Join(", ", off);
        }

        /// <summary>Score as shown in game: tens plus the trailing 0, thousands word in front.</summary>
        public static string Points(GameState s, int p) =>
            (s.ScoreThousands[p] > 0 ? s.ScoreThousands[p] + s.Score[p].ToString("D3") : s.Score[p].ToString()) + (s.Score[p] > 0 || s.ScoreThousands[p] > 0 ? "0" : "");

        private static float Fit(string text, float size, float width = 990f) =>
            Math.Min(size, size * width / Math.Max(1f, VectorFont.Width(text, size)));
    }
}
