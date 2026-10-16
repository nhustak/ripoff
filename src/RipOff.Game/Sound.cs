using RipOff.Core;

namespace RipOff.Game
{
    /// <summary>
    /// Sound output stub. The simulation already produces the arcade's sound command state each pass
    /// (<see cref="SoundState"/>, GAME_RULES §9); turning it into audio is a later milestone.
    /// </summary>
    public sealed class SoundStub
    {
        public void Update(in SoundState s)
        {
            // TODO(sound): torpedo (s.Torpedo) — player shot cue while RAM[$FD] counts down (GR §9).
            // TODO(sound): explosion pulse per s.Explosions (Q7 falling edge → LM555 one-shot, GR §9).
            // TODO(sound): laser while s.Laser (enemy fire line drawn, GR §9).
            // TODO(sound): motor drone while s.Motor (during waves).
            // TODO(sound): BONUS beep while s.Beep. UNCHECKED: beep phase.
            // TODO(sound): background drone from s.BackgroundLevel bits 7-5, muted while s.BackgroundMuted.
            //              UNCHECKED: pitch mapping of the 3 bits.
        }
    }
}
