using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using RipOff.Core;
using RipOff.Game;

namespace RipOff.Game.Tests;

/// <summary>Fake pads and a FrontEnd driven through the real InputReader, one 38 Hz tick at a time.</summary>
static class Fake
{
    public static GamePadState Pad(Buttons buttons = 0, float stickX = 0, float stickY = 0, float lt = 0, float rt = 0) =>
        new GamePadState(new GamePadThumbSticks(new Vector2(stickX, stickY), Vector2.Zero), new GamePadTriggers(lt, rt),
            new GamePadButtons(buttons & ~(Buttons.DPadUp | Buttons.DPadDown | Buttons.DPadLeft | Buttons.DPadRight)),
            new GamePadDPad(Btn(buttons, Buttons.DPadUp), Btn(buttons, Buttons.DPadDown), Btn(buttons, Buttons.DPadLeft), Btn(buttons, Buttons.DPadRight)));
    public static void AllCanistersGone(GameState s) { for (int j = 0; j < s.Canisters.Length; j++) s.Canisters[j].Y2 |= 1; }
    static ButtonState Btn(Buttons all, Buttons b) => (all & b) != 0 ? ButtonState.Pressed : ButtonState.Released;
}

sealed class FakeSteam : ISteamUi
{
    /// <summary>Default true (as if initialized at launch); false = lazy Steam not started yet.</summary>
    public bool Initialized { get; set; } = true;
    public int Inits;
    public bool EnsureInit() { if (!Initialized) { Inits++; Initialized = true; } return Available; }
    public bool Available { get; set; }
    public string Status { get; set; } = "Steam not running";
    public string? Code { get; set; }
    public ulong? Pending;
    public int Invites;
    public ulong? TakePendingJoin() { var p = Pending; Pending = null; return p; }
    public void OpenInviteOverlay() => Invites++;
}

sealed class Rig : IDisposable
{
    public readonly FrontEnd F;
    public readonly InputReader R = new();
    public readonly Settings S;
    public int Saves;
    /// <summary>Which pad index the "player" uses (others stay disconnected unless set).</summary>
    public int PadIndex;
    public GamePadState[] Others = new GamePadState[InputReader.MaxPads];

    public Rig(Settings? s = null, ISteamUi? steam = null, int padIndex = 0, bool skipSplash = true)
    {
        S = s ?? new Settings();
        PadIndex = padIndex;
        F = new FrontEnd(S, new OnlineController(null), steam, _ => Saves++) { LanPort = 0 };
        Tick(); // pad connects (no edges on the connect tick)
        if (skipSplash) { Press(Buttons.A); Assert.Equal(FrontEnd.Screen.Main, F.Top); }
    }

    public void Tick(GamePadState? pad = null, KeyboardState? keys = null, string? typed = null)
    {
        var pads = (GamePadState[])Others.Clone();
        pads[PadIndex] = pad ?? Fake.Pad();
        for (int i = 0; i < pads.Length; i++) F.Pads[i] = new PadSlot { State = pads[i], Name = pads[i].IsConnected ? "Fake Pad" : "", Type = pads[i].IsConnected ? "GamePad" : "" };
        var (ui, play) = R.Read(keys ?? new KeyboardState(), pads, typed);
        F.Update(ui, play);
    }

    public void Ticks(int n, GamePadState? pad = null) { for (int i = 0; i < n; i++) Tick(pad); }
    /// <summary>Press and release a pad button (two ticks).</summary>
    public void Press(Buttons b) { Tick(Fake.Pad(b)); Tick(); }
    public void Key(Keys k) { Tick(keys: new KeyboardState(k)); Tick(); }
    public void Type(string text) { Tick(typed: text); }

    /// <summary>Moves the highlight to <paramref name="label"/> (prefix) with D-pad down presses, then presses A.</summary>
    public void Choose(string label)
    {
        for (int i = 0; i < 20 && !F.CurrentLabels[F.Selected].StartsWith(label); i++) Press(Buttons.DPadDown);
        Assert.StartsWith(label, F.CurrentLabels[F.Selected]);
        Press(Buttons.A);
    }

    public void Dispose() => F.Online.Dispose();
}
