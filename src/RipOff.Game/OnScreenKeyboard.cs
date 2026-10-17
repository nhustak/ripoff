using System;
using System.Collections.Generic;
using System.Text;
using RipOff.Core;

namespace RipOff.Game
{
    /// <summary>
    /// Pad-driven text entry for a lobby code or a host address: a grid of keys plus DEL / OK / CANCEL. D-pad or
    /// stick moves (wrapping), A presses the highlighted key, X deletes, B cancels. A physical keyboard can type
    /// straight in (Enter = OK, Backspace = delete, Esc = cancel).
    /// </summary>
    public sealed class OnScreenKeyboard
    {
        public enum Mode { LobbyCode, Address }
        public enum Result { None, Ok, Cancel }

        public const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no 0/O/1/I: easy to read aloud
        public const int CodeLength = 6;
        public const string AddressAlphabet = "1234567890.:abcdefghijklmnopqrstuvwxyz-[]";
        public const string KeyDel = "DEL", KeyOk = "OK", KeyCancel = "CANCEL";
        private const int Columns = 10;

        public Mode Kind { get; }
        public string Text { get; private set; } = "";
        public int Row { get; private set; }
        public int Col { get; private set; }
        public IReadOnlyList<string[]> Rows => _rows;
        private readonly List<string[]> _rows = new();
        private readonly string _alphabet;
        private readonly int _max;

        public OnScreenKeyboard(Mode kind)
        {
            Kind = kind;
            _alphabet = kind == Mode.LobbyCode ? CodeAlphabet : AddressAlphabet;
            _max = kind == Mode.LobbyCode ? CodeLength : 60;
            for (int i = 0; i < _alphabet.Length; i += Columns)
            {
                var r = new List<string>();
                for (int j = i; j < Math.Min(i + Columns, _alphabet.Length); j++) r.Add(_alphabet[j].ToString().ToUpperInvariant());
                _rows.Add(r.ToArray());
            }
            _rows.Add(new[] { KeyDel, KeyOk, KeyCancel });
        }

        public string Current => _rows[Row][Col];
        public bool CanSubmit => Kind == Mode.LobbyCode ? Text.Length == CodeLength : Text.Length > 0;

        public Result Update(in UiInput i)
        {
            if (i.Back) return Result.Cancel;
            if (i.Up) MoveRow(-1);
            if (i.Down) MoveRow(1);
            if (i.Left) Col = (Col + _rows[Row].Length - 1) % _rows[Row].Length;
            if (i.Right) Col = (Col + 1) % _rows[Row].Length;
            foreach (char c in i.Typed ?? "") Add(c);
            if (i.Delete) Del();
            if (i.Enter) return CanSubmit ? Result.Ok : Result.None;
            if (i.Accept) return Press(Current);
            return Result.None;
        }

        /// <summary>Presses a key by its label (what A does on the highlighted key).</summary>
        public Result Press(string key)
        {
            switch (key)
            {
                case KeyDel: Del(); return Result.None;
                case KeyOk: return CanSubmit ? Result.Ok : Result.None;
                case KeyCancel: return Result.Cancel;
                default: Add(key[0]); return Result.None;
            }
        }

        /// <summary>Moves the highlight to a key (tests / mouse).</summary>
        public bool Select(string key)
        {
            for (int r = 0; r < _rows.Count; r++)
                for (int c = 0; c < _rows[r].Length; c++)
                    if (_rows[r][c] == key) { Row = r; Col = c; return true; }
            return false;
        }

        private void MoveRow(int d)
        {
            Row = (Row + d + _rows.Count) % _rows.Count;
            Col = Math.Min(Col, _rows[Row].Length - 1);
        }

        private void Add(char raw)
        {
            char c = Kind == Mode.LobbyCode ? char.ToUpperInvariant(raw) : char.ToLowerInvariant(raw);
            if (_alphabet.IndexOf(c) >= 0 && Text.Length < _max) Text += c;
            if (Kind == Mode.LobbyCode && Text.Length == CodeLength) Select(KeyOk); // code complete: jump to OK
        }

        private void Del() { if (Text.Length > 0) Text = Text[..^1]; }

        public void Build(List<Line> o, string title)
        {
            const float cx = Scene.Width / 2f;
            VectorFont.DrawCentered(title, cx, 600, 28, o);
            string shown = Kind == Mode.LobbyCode ? Text.PadRight(CodeLength, '_') : (Text.Length == 0 ? "_" : Text.ToUpperInvariant() + "_");
            VectorFont.DrawCentered(shown, cx, 520, Math.Min(44, 990f / Math.Max(1, shown.Length)), o);
            float y = 430;
            for (int r = 0; r < _rows.Count; r++, y -= 56)
            {
                var row = _rows[r];
                float pitch = r == _rows.Count - 1 ? 260 : 80;
                float x0 = cx - (row.Length - 1) * pitch / 2f;
                for (int c = 0; c < row.Length; c++)
                {
                    float x = x0 + c * pitch, size = 24;
                    bool sel = r == Row && c == Col;
                    bool dim = row[c] == KeyOk && !CanSubmit;
                    VectorFont.DrawCentered(row[c], x, y, size, o, bright: !dim);
                    if (sel)
                    {
                        float w = Math.Max(VectorFont.Width(row[c], size), size) / 2f + 10;
                        Box(x - w, y - 10, x + w, y + size + 10, o);
                    }
                }
            }
            VectorFont.DrawCentered("A PRESS KEY   X DELETE   B CANCEL   (OR TYPE, ENTER = OK)", cx, 60, 14, o, false);
        }

        internal static void Box(float x1, float y1, float x2, float y2, List<Line> o)
        {
            o.Add(new Line(x1, y1, x2, y1, true)); o.Add(new Line(x2, y1, x2, y2, true));
            o.Add(new Line(x2, y2, x1, y2, true)); o.Add(new Line(x1, y2, x1, y1, true));
        }
    }
}
