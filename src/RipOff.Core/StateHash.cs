namespace RipOff.Core
{
    /// <summary>
    /// 32-bit FNV-1a checksum over every field of <see cref="GameState"/> that influences later passes or the
    /// picture (records, shots, canisters, counters, scores, sound state, fire lines). Used by online lockstep to
    /// detect desyncs: two peers that ran the same inputs must produce the same value every pass.
    /// </summary>
    public static class StateHash
    {
        public static uint Compute(GameState s)
        {
            uint h = 2166136261;
            void Add(int v)
            {
                unchecked
                {
                    for (int i = 0; i < 4; i++) { h ^= (byte)(v >> (8 * i)); h *= 16777619; }
                }
            }
            void B(bool b) => Add(b ? 1 : 0);
            Add((int)s.Mode); B(s.OnePlayer); Add(s.CanisterDip); Add(s.PassCounter); Add(s.WaveCounter);
            Add(s.EnemyType); Add(s.Level); Add(s.KillsThisWave); Add(s.TheftsThisWave); B(s.BonusFlag); B(s.MotorOff);
            Add(s.TorpedoTimer); Add(s.Background); Add(s.BackgroundStep);
            for (int i = 0; i < 2; i++) { Add(s.Score[i]); Add(s.ScoreThousands[i]); }
            for (int i = 0; i < 5; i++)
            {
                var r = s.Record(i);
                Add(r.X); Add(r.Y); Add(r.Word2); Add(r.Word3); Add(r.Scale); Add(r.Heading); Add(r.Speed); Add(r.Type);
                Add(r.A); Add(r.B); Add(r.C); Add(r.D); Add(r.TurnStep); Add(r.F);
            }
            foreach (var c in s.Canisters) { Add(c.X2); Add(c.Y2); }
            foreach (var sh in s.Shots) { B(sh.Live); Add(sh.X); Add(sh.Y); Add(sh.Vx); Add(sh.Vy); }
            var snd = s.Sound;
            B(snd.Torpedo); Add(snd.Explosions); B(snd.Laser); B(snd.Motor); B(snd.Beep); B(snd.BackgroundMuted); Add(snd.BackgroundLevel);
            Add(s.FireLines.Count); foreach (var f in s.FireLines) { Add(f.Record); Add(f.Frame); }
            B(s.GameOverThisPass);
            B(s.AutoFire); Add(s.AutoFireTimer[0]); Add(s.AutoFireTimer[1]); Add(s.ShotsFired[0]); Add(s.ShotsFired[1]);
            if (s.PlayMode != PlayMode.Arcade) // Arcade adds nothing: hashes as before modes
            {
                Add(0x4D0D0000 | (int)s.PlayMode);
                Add(s.ClearsTowardStep); Add((int)s.EnhancedRng);
                foreach (var c in s.EnemyFireCooldown) Add(c);
                Add(0x0F7E0000 | (int)s.Options);
                for (int p = 0; p < 2; p++) { Add(s.WarpTimer[p]); Add(s.WarpX[p]); Add(s.WarpY[p]); Add(s.WarpHeading[p]); }
                // Later Enhanced features add their state only while their option is on, so with them off the hash
                // is exactly that of 2026.10.17.5 (Enhanced20261017_5Tests).
                var rules = s.Rules;
                if (rules.ShipSelect) Add(0x5A1B0000 | (s.Ships[0] << 8) | s.Ships[1]);
            }
            return h;
        }
    }
}
