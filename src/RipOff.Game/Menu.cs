using System;
using System.Collections.Generic;
using System.Text;
using RipOff.Core;

namespace RipOff.Game
{
    /// <summary>One pass of menu navigation: edges from keyboard and pads, plus typed characters.</summary>
    public struct MenuInput
    {
        public bool Up, Down, Left, Right, Select, Back, Backspace;
        public string Typed;
    }

    public enum MenuAction { None, LocalPlay, HostSteam, JoinSteamCode, HostLan, JoinLan, InviteOverlay, Cancel, Quit }

    /// <summary>
    /// MINIMAL STOPGAP menu for this milestone: the front-end milestone (splash, options, pause, pad keyboard)
    /// replaces it. It only calls the <see cref="OnlineController"/> API and holds no network logic.
    /// Main menu, text entry (lobby code / IP address) and status screens. Pure logic (no MonoGame), driven by
    /// <see cref="MenuInput"/>, drawn as vector text. Keyboard: arrows, Enter, Esc, typing. Pad: D-pad/stick, A, B;
    /// in text entry Up/Down cycle the character under the cursor, Right adds a character, Left deletes one.
    /// </summary>
    public sealed class Menu
    {
        public enum Screen { Main, EnterCode, EnterAddress, Status }

        public const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no 0/O/1/I: easy to read aloud
        public const int CodeLength = 6;
        private const string AddressAlphabet = "0123456789.:abcdefghijklmnopqrstuvwxyz-";

        public Screen Current { get; private set; } = Screen.Main;
        public int Selected { get; private set; }
        public string Text { get; private set; } = "";
        /// <summary>Set by the host: Steam features available?</summary>
        public bool SteamAvailable { get; set; }
        public string SteamNote { get; set; } = "";
        public string Footer { get; set; } = "";

        // status screen content (set by the online controller)
        public string StatusTitle { get; private set; } = "";
        public List<string> StatusLines { get; } = new();
        private readonly List<(string label, MenuAction action)> _statusOptions = new();

        /// <summary>The value entered on the last text screen (code or address) when its action fires.</summary>
        public string EnteredText { get; private set; } = "";

        private static readonly (string label, MenuAction action, bool steam)[] MainItems =
        {
            ("LOCAL PLAY", MenuAction.LocalPlay, false),
            ("HOST ONLINE (STEAM)", MenuAction.HostSteam, true),
            ("JOIN BY CODE (STEAM)", MenuAction.JoinSteamCode, true),
            ("HOST DIRECT IP (LAN)", MenuAction.HostLan, false),
            ("JOIN DIRECT IP", MenuAction.JoinLan, false),
            ("QUIT", MenuAction.Quit, false),
        };

        public IReadOnlyList<string> MainLabels
        {
            get { var l = new List<string>(); foreach (var m in MainItems) l.Add(m.label); return l; }
        }

        public void ShowMain() { Current = Screen.Main; Selected = 0; }

        public void ShowStatus(string title, IEnumerable<string> lines, params (string label, MenuAction action)[] options)
        {
            bool same = Current == Screen.Status && StatusTitle == title && _statusOptions.Count == options.Length;
            Current = Screen.Status; StatusTitle = title;
            StatusLines.Clear(); StatusLines.AddRange(lines);
            _statusOptions.Clear(); _statusOptions.AddRange(options);
            if (!same) Selected = 0;
            Selected = Math.Clamp(Selected, 0, Math.Max(0, options.Length - 1));
        }

        public MenuAction Update(in MenuInput i)
        {
            switch (Current)
            {
                case Screen.Main:
                {
                    if (i.Up) Selected = (Selected + MainItems.Length - 1) % MainItems.Length;
                    if (i.Down) Selected = (Selected + 1) % MainItems.Length;
                    if (i.Back) { Selected = MainItems.Length - 1; return MenuAction.None; }
                    if (!i.Select) return MenuAction.None;
                    var item = MainItems[Selected];
                    if (item.steam && !SteamAvailable) return MenuAction.None;
                    if (item.action == MenuAction.JoinSteamCode) { Current = Screen.EnterCode; Text = ""; return MenuAction.None; }
                    if (item.action == MenuAction.JoinLan) { Current = Screen.EnterAddress; Text = ""; return MenuAction.None; }
                    return item.action;
                }
                case Screen.EnterCode:
                case Screen.EnterAddress:
                {
                    bool code = Current == Screen.EnterCode;
                    string alphabet = code ? CodeAlphabet : AddressAlphabet;
                    int max = code ? CodeLength : 60;
                    if (i.Back) { ShowMain(); return MenuAction.None; }
                    var sb = new StringBuilder(Text);
                    foreach (char raw in i.Typed ?? "")
                    {
                        char c = code ? char.ToUpperInvariant(raw) : char.ToLowerInvariant(raw);
                        if (alphabet.IndexOf(c) >= 0 && sb.Length < max) sb.Append(c);
                    }
                    if (i.Backspace && sb.Length > 0) sb.Length--;
                    if (i.Left && sb.Length > 0) sb.Length--;
                    if (i.Right && sb.Length < max) sb.Append(alphabet[0]);
                    if ((i.Up || i.Down) && sb.Length == 0) sb.Append(alphabet[0]);
                    else if (i.Up || i.Down)
                    {
                        int k = alphabet.IndexOf(sb[^1]); if (k < 0) k = 0;
                        k = (k + (i.Up ? 1 : alphabet.Length - 1)) % alphabet.Length;
                        sb[^1] = alphabet[k];
                    }
                    Text = sb.ToString();
                    if (i.Select && (code ? Text.Length == CodeLength : Text.Length > 0))
                    {
                        EnteredText = Text;
                        return code ? MenuAction.JoinSteamCode : MenuAction.JoinLan;
                    }
                    return MenuAction.None;
                }
                case Screen.Status:
                {
                    if (_statusOptions.Count > 0)
                    {
                        if (i.Up) Selected = (Selected + _statusOptions.Count - 1) % _statusOptions.Count;
                        if (i.Down) Selected = (Selected + 1) % _statusOptions.Count;
                        if (i.Select) return _statusOptions[Selected].action;
                    }
                    if (i.Back) return MenuAction.Cancel;
                    return MenuAction.None;
                }
            }
            return MenuAction.None;
        }

        /// <summary>Size that keeps <paramref name="text"/> within the 1024-unit field (with margins).</summary>
        private static float Fit(string text, float size) => Math.Min(size, size * 990f / Math.Max(1f, VectorFont.Width(text, size)));

        public void Build(List<Line> o)
        {
            const float cx = Scene.Width / 2f;
            VectorFont.DrawCentered(Branding.GameName, cx, 640, 40, o);
            switch (Current)
            {
                case Screen.Main:
                    for (int k = 0; k < MainItems.Length; k++)
                    {
                        var (label, _, steam) = MainItems[k];
                        bool off = steam && !SteamAvailable;
                        string text = (k == Selected ? "> " : "  ") + label + (k == Selected ? " <" : "  ");
                        VectorFont.DrawCentered(text, cx, 500 - k * 56, 26, o, bright: !off);
                    }
                    if (!SteamAvailable && SteamNote.Length > 0) VectorFont.DrawCentered(SteamNote, cx, 120, Fit(SteamNote, 14), o, false);
                    break;
                case Screen.EnterCode:
                case Screen.EnterAddress:
                    bool code = Current == Screen.EnterCode;
                    VectorFont.DrawCentered(code ? "ENTER LOBBY CODE" : "HOST ADDRESS (IP OR IP:PORT)", cx, 480, 26, o);
                    string shown = code ? Text.PadRight(CodeLength, '_') : Text + "_";
                    VectorFont.DrawCentered(shown, cx, 380, Fit(shown, code ? 48 : 30), o);
                    VectorFont.DrawCentered("TYPE, OR PAD: UP/DOWN CHANGE, RIGHT ADD, LEFT DELETE", cx, 250, 14, o, false);
                    VectorFont.DrawCentered("ENTER / A = OK     ESC / B = BACK", cx, 220, 14, o, false);
                    break;
                case Screen.Status:
                    VectorFont.DrawCentered(StatusTitle, cx, 520, 28, o);
                    for (int k = 0; k < StatusLines.Count; k++) VectorFont.DrawCentered(StatusLines[k], cx, 450 - k * 40, Fit(StatusLines[k], StatusLines[k].Length > 40 ? 14 : 20), o);
                    for (int k = 0; k < _statusOptions.Count; k++)
                        VectorFont.DrawCentered((k == Selected ? "> " : "  ") + _statusOptions[k].label + (k == Selected ? " <" : "  "), cx, 200 - k * 44, 22, o);
                    break;
            }
            if (Footer.Length > 0) VectorFont.DrawCentered(Footer, cx, 40, 14, o, false);
        }
    }
}
