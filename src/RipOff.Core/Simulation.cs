using System;

namespace RipOff.Core
{
    /// <summary>
    /// Deterministic fixed-step simulation, one <see cref="Step"/> = one arcade main-loop
    /// pass = one 38 Hz frame (GAME_RULES §Units). The order of work inside a pass follows
    /// the main loop: sound/command byte (lst:283-353), wave check (lst:464-498),
    /// respawn (lst:862-932), game-over test (lst:1460-1481), enemy records $00/$10/$20
    /// (lst:1500-2069 via $0CAA), players $30/$40 (lst:2086-2664), pass counter
    /// (lst:4587-4590), shots (lst:3391-3623).
    /// Comments cite GAME_RULES.md (GR §n), NOTES.md (§n) and ripoff.lst (lst:n).
    /// </summary>
    public sealed class Simulation
    {
        public GameState State { get; }

        public Simulation(GameState? state = null) { State = state ?? new GameState(); }

        // ---------------------------------------------------------------- helpers

        /// <summary>Box test $11E0: hit when |dx| ≤ r and |dy| ≤ r, inclusive (lst:4150-4186, §18.1, GR §7).</summary>
        public static bool Box(int dx, int dy, int r) => Math.Abs(dx) <= r && Math.Abs(dy) <= r;

        private static int Sign12(int v) { v &= 0xFFF; return v >= 0x800 ? v - 0x1000 : v; }

        private static bool OnField(int x, int y) => x >= 0 && x < 0x400 && y >= 0 && y < 0x300;

        /// <summary>Points per kill in tens: type + level − 1 (lst:3550-3573, lst:2433-2455, GR §8).</summary>
        public static int PointsTens(int type, int level) => type + level - 1;

        /// <summary>Enemy max speed v = type + ⌊type/2⌋ + level + 1 (lst:666-674, GR §5).</summary>
        public static int EnemyMaxSpeed(int type, int level) => type + (type >> 1) + level + 1;

        /// <summary>Enemy turn step ⌊v/2⌋ + 1 (lst:675-677, GR §5).</summary>
        public static int EnemyTurnStep(int v) => (v >> 1) + 1;

        /// <summary>Fire window: RAM[$E8] = $10·((A8 &gt;&gt; 2) &amp; 3) (§18.4). UNCHECKED: the write site of
        /// $E8 is not decoded; the phase uses the pass counter value during the enemy pass, which matches the
        /// frame-end relation seen in ram1 (trace:excerpts/laser_windows.txt).</summary>
        public int FireWindowBase => 0x10 * ((State.PassCounter >> 2) & 3);

        // ---------------------------------------------------------------- game start / credits

        /// <summary>
        /// Game start (lst:236-255, GR §2): scores 0, type 1, level 1, kill/theft counters 0,
        /// wave counter 0, canister table rebuilt ($0C1E, lst:2900-2958).
        /// </summary>
        /// <summary>Applies <paramref name="config"/> (canisters, auto-fire) and starts a game: the front end's
        /// "Start 1 Player" / "Start 2 Players", and the online session start.</summary>
        public void StartGame(bool onePlayer, in GameConfig config)
        {
            config.ApplyTo(State);
            StartGame(onePlayer);
        }

        public void StartGame(bool onePlayer)
        {
            var s = State;
            s.Mode = GameMode.Playing;
            s.OnePlayer = onePlayer;
            s.Score[0] = s.Score[1] = 0;
            s.ScoreThousands[0] = s.ScoreThousands[1] = 0;
            s.EnemyType = 1;
            s.Level = 1;
            s.KillsThisWave = 0;
            s.TheftsThisWave = 0;
            s.WaveCounter = 0;
            s.BonusFlag = false;
            s.MotorOff = true;
            s.AutoFireTimer[0] = s.AutoFireTimer[1] = 0;
            s.ShotsFired[0] = s.ShotsFired[1] = 0;
            int n = GameData.CanistersForDip[s.CanisterDip];
            for (int i = 0; i < GameData.CanisterSlots; i++)
            {
                if (i < n)
                {
                    // RAM $B0 table = start byte ×8 = field position ×2 (lst:2919-2939, §20.2)
                    s.Canisters[i] = new Canister { X2 = GameData.CanisterStarts[i].X * 2, Y2 = GameData.CanisterStarts[i].Y * 2 };
                }
                else
                {
                    s.Canisters[i] = new Canister { X2 = 1, Y2 = 1 }; // unused slots: both words := 1 (lst:2940-2955)
                }
            }
            // Background drone start = step for the DIP (lst:2892-2898, §15).
            s.BackgroundStep = GameData.BackgroundStepForDip[s.CanisterDip];
            s.Background = s.BackgroundStep;
            foreach (var e in s.Enemies) { e.A = EnemyState.Free; }
            for (int p = 0; p < 2; p++)
            {
                var r = s.Players[p];
                r.A = EnemyState.Free;
                // Word 3 is $19 before the start (set by the game-over/attract path, lst:86-90), so P1 (and P2 in a
                // two-player game) respawn in the first pass: trace idle1/shoot1/ram1/tp_wander, start f202 =
                // respawn f202 (trace:excerpts/p7_checks.txt (f)).
                r.Word3 = GameData.RespawnDelay;
                r.Speed = 0;
            }
            for (int i = 0; i < s.Shots.Length; i++) s.Shots[i] = default;
        }

        // ---------------------------------------------------------------- one pass

        public void Step(in FrameInput input)
        {
            var s = State;
            s.FireLines.Clear();
            s.GameOverThisPass = false;
            s.Sound.Explosions = 0;
            s.Sound.Laser = false;

            // Game-over test: the first thing after the frame wait, $062E-$0653 (lst:1446-1481), i.e. before the
            // sound routine and the wave check of the pass. Replays ram1 f3434 / wander1 f4462 / tp_shoot2 f4784:
            // RAM[$EC] does not get the +2 of a wave check in the game-over pass.
            bool playingAtPassStart = State.Mode == GameMode.Playing;
            if (State.Mode == GameMode.Playing) GameOverCheck();

            // Coin/start handling (lst:197-255) is not modelled: the remake has no credits and the front end
            // calls StartGame directly (NOTES §23). Attract mode just idles.

            // Command byte RAM[$FC] (GR §9, NOTES §15/§19.3): built and shifted out by the sound routine at the
            // start of the pass (lst:283-384), so it reflects the state left by the previous pass. Bits 2-4 and 7-5
            // compared every frame against the RAM logs by tools/remake/Replay (NOTES §21.4).
            // Motor: bit 2 is written by the wave code later in the pass, so the latch gets the previous pass's value;
            // in the game-over pass that is still the in-play value (replays ram1 f3434, shoot1 f4790, wander1 f4462).
            s.Sound.Motor = playingAtPassStart && !s.MotorOff;
            // Beep: command bit 3 is cleared when RAM[$A8] bit 1 = 0 at the sound routine (lst:317-338). sl_base:
            // bit 3 = 0 on 49 of 49 BONUS frames with frame-end A8 & 3 = 1 or 2 after the first BONUS frame
            // (trace:excerpts/p8_sound.txt (7)).
            s.Sound.Beep = s.Mode == GameMode.Playing && s.BonusFlag && (s.PassCounter & 2) == 0;
            s.Sound.BackgroundMuted = s.Mode != GameMode.Playing || s.BonusFlag;
            s.Sound.BackgroundLevel = s.Background;

            // Sound routine (lst:283-353, §15): torpedo countdown; Q3 = 0 while RAM[$FD] counts.
            s.Sound.Torpedo = s.TorpedoTimer > 0;
            if (s.TorpedoTimer > 0) s.TorpedoTimer--;
            if (s.Mode == GameMode.Playing && s.BonusFlag)
            {
                // BONUS screen: RAM[$EC] −= 1 each pass while command bit 0 is set (lst:317-338).
                s.WaveCounter -= 1;
            }

            if (s.Mode == GameMode.Playing)
            {
                WaveCheck();
                RespawnPlayers();
            }

            if (s.Mode == GameMode.Playing)
            {
                for (int i = 0; i < 3; i++) EnemyPass(i);
                PlayerPass(0, input.P1);
                if (!s.OnePlayer) PlayerPass(1, input.P2); // P2 only in a two-player game (lst:1021-1024 area, §19.2)
            }

            s.PassCounter = (s.PassCounter + 1) & 0xFFF; // RAM[$A8] += 1 at $13A1 (lst:4587-4590)

            if (s.Mode == GameMode.Playing) ShotsPass();

            // Score carry (lst:1046-1080, $0475-$0498, NOTES §13): while units ≥ 1000 ($3E8) subtract 1000 and add one
            // to the thousands word. UNCHECKED: its exact position within the pass (it is in the display code); only
            // frame-end values are compared, and no traced score reached 1000 tens.
            if (s.Mode == GameMode.Playing)
                for (int p = 0; p < 2; p++)
                    while (s.Score[p] >= 1000) { s.Score[p] -= 1000; s.ScoreThousands[p] += 1; }

        }

        // ---------------------------------------------------------------- waves

        /// <summary>Wave check (lst:464-498, §14.1, GR §4).</summary>
        private void WaveCheck()
        {
            var s = State;
            foreach (var e in s.Enemies) if (!EnemyState.IsFree(e.A)) return;
            s.WaveCounter += 2;
            if (s.WaveCounter == 2)
            {
                WaveEnd();
                s.MotorOff = true; // $0339-$0348: command bit 2 set (lst:768-782)
            }
            else if (s.WaveCounter < GameData.WaveGapEnd)
            {
                s.MotorOff = true;
            }
            else
            {
                s.WaveCounter = 0;
                SpawnWave();
            }
        }

        /// <summary>Wave-end bookkeeping (lst:500-573, §14.1, GR §4).</summary>
        private void WaveEnd()
        {
            var s = State;
            int k = s.KillsThisWave;
            if (k < 3) s.KillsThisWave = 0;                  // lst:500-507 → $0247
            else if (k == 3)
            {
                if (s.OnePlayer) Advance();                  // $0225-$0229 → $0232
                // two-player: kept ($024E)
            }
            else if (k < 6) s.KillsThisWave = 3;             // $022D-$0231 → $024B
            else Advance();                                  // k ≥ 6

            // Step-back (lst:558-571): type ≠ 1 and thefts ≥ 3 → type −1; thefts reset.
            if (s.EnemyType != 1 && s.TheftsThisWave >= 3) s.EnemyType -= 1;
            s.TheftsThisWave = 0;
        }

        /// <summary>Advance (lst:529-547): type +1; after 6 → type 1, level +1, BONUS flag. Kills := 0.</summary>
        private void Advance()
        {
            var s = State;
            s.EnemyType += 1;
            if (s.EnemyType >= 7)
            {
                s.EnemyType = 1;
                s.Level += 1;
                s.BonusFlag = true; // FC bit 0 (lst:540-546)
            }
            s.KillsThisWave = 0;
        }

        /// <summary>Spawn a wave (lst:575-861, §14.2, GR §5).</summary>
        private void SpawnWave()
        {
            var s = State;
            s.BonusFlag = false; // FC := (FC & $F2) + 8 clears bits 0 and 2 (lst:575-581)
            s.MotorOff = false;

            int ptr = 0; // RAM[$E4] := $B0 (lst:582-585)
            for (int i = 0; i < 3; i++)
            {
                var e = s.Enemies[i];
                // Target canister: round-robin over live slots from $B0; past the end, the first live one
                // (lst:611-660).
                int target = -1;
                while (ptr < GameData.CanisterSlots)
                {
                    int j = ptr++;
                    if (!s.Canisters[j].Gone) { target = j; break; }
                }
                if (target < 0)
                {
                    for (int j = 0; j < GameData.CanisterSlots; j++) if (!s.Canisters[j].Gone) { target = j; break; }
                    if (target < 0) target = 0; // no live canister: the game-over test ends the game first
                }
                e.Word2 = target;
                e.F = 0;
                e.A = EnemyState.Approach;                      // lst:661-665
                int v = EnemyMaxSpeed(s.EnemyType, s.Level);    // lst:666-674
                e.Speed = v;
                e.Word3 = v;
                e.TurnStep = EnemyTurnStep(v);                  // lst:675-677
                e.Type = s.EnemyType;                           // sprite set (lst:678-740)
                e.Scale = GameData.EnemyScale;
            }
            // Spawn point k = record $00 word B & $E (lst:786-794); all three at that point, C/D = the same
            // point, heading from the table (lst:795-842).
            int k = s.Enemies[0].B & 0xE;
            var sp = GameData.SpawnPoints[k >> 1];
            foreach (var e in s.Enemies)
            {
                e.X = e.C = sp.X;
                e.Y = e.D = sp.Y;
                e.Heading = sp.Heading;
            }
            // One-player handicap at level 1 (lst:843-861, GR §4).
            if (s.OnePlayer && s.Level <= 1)
            {
                s.TheftsThisWave = 1;
                s.Enemies[2].A = EnemyState.Free;
                s.KillsThisWave += 1;
            }
        }

        /// <summary>Player respawn (lst:862-932, §19.2, GR §7 "Player respawn").</summary>
        private void RespawnPlayers()
        {
            var s = State;
            for (int p = 0; p < 2; p++)
            {
                if (p == 1 && s.OnePlayer) break;
                var r = s.Players[p];
                if (!EnemyState.IsFree(r.A)) continue;
                r.Word3 += 1;
                if (r.Word3 < GameData.RespawnDelay) continue;
                r.Word3 = 0;
                r.TurnStep = 0;
                r.A = EnemyState.PlayerAlive;      // lst:890
                r.Speed = GameData.RespawnSpeed;   // lst:891-893
                var st = GameData.PlayerStarts[p]; // lst:899-906, lst:924-932
                r.X = st.X; r.Y = st.Y; r.Heading = st.Heading;
                r.Scale = GameData.PlayerScale;
            }
        }

        /// <summary>Game over when no canister slot has y bit 0 clear (lst:1452-1481, GR §2).</summary>
        private void GameOverCheck()
        {
            var s = State;
            for (int j = 0; j < GameData.CanisterSlots; j++) if (!s.Canisters[j].Gone) return;
            s.Mode = GameMode.Attract;
            s.GameOverThisPass = true;
        }

        // ---------------------------------------------------------------- enemies

        private void EnemyPass(int i)
        {
            var s = State;
            var e = s.Enemies[i];
            if (EnemyState.IsFree(e.A)) return;                  // lst:1505-1508
            if (EnemyState.IsExploding(e.A)) { ExplosionTick(e, true); return; } // AI skipped (lst:1509-1516)

            e.Scale = GameData.EnemyScale; // word 4 := $50 (lst:1526-1527)
            int fAtPassStart = e.F;

            // Target canister gone → first slot neither gone nor taken, F := 0 (lst:1528-1573, GR §6).
            if (s.Canisters[e.Word2].Gone)
            {
                for (int j = 0; j < GameData.CanisterSlots; j++)
                {
                    if (!s.Canisters[j].Gone && !s.Canisters[j].Taken) { e.Word2 = j; e.F = 0; break; }
                }
            }

            int bearing;
            if ((e.A & EnemyState.BitMoving) == 0)
            {
                // At canister ($014): bearing of (own − canister/2), i.e. pointing away from the canister
                // (lst:1587-1609). NOTES §20.1 corrects GR §6 "Turn, then tow": the turn target is this bearing,
                // not the exit point (12,162 of 12,162 at-canister frames turn toward it, trace:excerpts/p7_checks.txt (b)).
                // The code negates the stored ×2 word and halves it with ASR, then adds the own position, so the
                // taken bit makes x round down by one more: dx = x + ASR(−X2) (lst:1588-1609, ASR = arithmetic).
                var c = s.Canisters[e.Word2];
                bearing = Trig.Bearing(e.X + ((-c.X2) >> 1), e.Y + ((-c.Y2) >> 1));
            }
            else
            {
                bool separated = false;
                bearing = 0;
                // Separation (lst:1611-1667, GR §6; NOTES §20.1): the loop walks records $00,$10,$20 in steps of
                // $10, but the self-skip at $06EA jumps past the "+1" of $06F2, so after the enemy's own record
                // the walk is one word off ($0F, $1F, $2F). Effect, reproduced literally here:
                //  - proper compares only with LOWER-index enemies (not free; exploding ones count);
                //  - then misaligned pseudo-records: x = word F of record m, y = word 0 (x) of record m+1, and
                //    the "free" bit is bit 0 of word 9 of record m+1 (the tread-frame pointer, whose parity is
                //    fixed per sprite set, GameData.Word9Parity).
                // The first hit steers along (own − other); this path skips the C/D update below.
                int fStart = fAtPassStart; // own word F as still stored in the record page
                for (int o = 0; o < i && !separated; o++)
                {
                    var other = s.Enemies[o];
                    if (EnemyState.IsFree(other.A)) continue;
                    int dx = e.X - other.X, dy = e.Y - other.Y;
                    if (Box(dx, dy, GameData.SeparationRadius)) { bearing = Trig.Bearing(dx, dy); separated = true; }
                }
                for (int m = i; m < 3 && !separated; m++)
                {
                    var next = s.Record(m + 1); // record m+1 ($10, $20 or P1 $30)
                    if ((GameData.Word9Parity(next) & 1) != 0) continue;
                    int px = m == i ? fStart : s.Enemies[m].F;
                    int dx = e.X - px, dy = e.Y - next.X;
                    if (Box(dx, dy, GameData.SeparationRadius)) { bearing = Trig.Bearing(dx, dy); separated = true; }
                }
                if (!separated)
                {
                    if ((e.A & EnemyState.BitAim) == 0)
                    {
                        // Chase: C/D := chosen player's position (lst:1686-1774, GR §6 "Chase mode").
                        // UNCHECKED (NOTES §20.6 item 1): literal code reading, refreshed every chase pass except when
                        // separation steers instead. It matches C/D in every frame of the 9 replays (NOTES §20.5).
                        var t = ChooseChaseTarget(e);
                        e.C = t.X; e.D = t.Y;
                    }
                    bearing = Trig.Bearing(e.C - e.X, e.D - e.Y); // lst:1776-1785
                }
            }

            Steer(i, e, bearing);

            // lst:1786-1790: word-A bit 3 clear → no movement.
            if ((e.A & EnemyState.BitMoving) == 0) { EnemyDraw(i, e); return; }

            // Movement: speed − word F, minimum 2 (lst:1813-1830); heading formula (lst:1795-1841).
            int eff = Math.Max(2, e.Speed - e.F);
            var (mx, my, _) = Trig.Displacement(e.Heading, eff);
            e.X += mx; e.Y += my;

            // Speed recovers +1 toward max every 4th pass (lst:1842-1854).
            if ((s.PassCounter & 3) == 0 && e.Speed != e.Word3) e.Speed += 1;

            // Theft (lst:1856-1904, GR §6): towing and off the field (x/y negative or ≥ $400/$300).
            if ((e.A & EnemyState.BitTow) != 0 && !OnField(e.X, e.Y))
            {
                s.TheftsThisWave += 1;                                   // RAM[$F9] += 1
                s.Background = (s.Background + s.BackgroundStep) & 0xFFF; // RAM[$78] += RAM[$7A]
                e.A = EnemyState.Free;
                s.Canisters[e.Word2].Y2 += 1;                            // gone bit (store $0810)
                return;
            }

            PickupCheck(e, i);

            // Hops (lst:1906-2068, GR §6 "Approach"): bits 3 and 2 set, bit 1 clear, within $48 of C/D.
            if ((e.A & 0x0E) == 0x0C && Box(e.X - e.C, e.Y - e.D, GameData.HopArrivalRadius))
            {
                int r0 = Rng.Step(e.B);
                int r1 = Rng.Step(r0);
                e.B = r1;                                    // lst:1945-1975
                int ox = Sign12(r0) >> 4, oy = Sign12(r1) >> 4; // 4 × ASR (lst:1976-1989)
                for (int n = 0; n < e.F && n < 16; n++) { ox >>= 1; oy >>= 1; } // F more (lst:1990-2004); saturates
                int h = oy >> 1; oy = h + (h >> 1);          // y offset × 3/4 (lst:2005-2012)
                var c = s.Canisters[e.Word2];
                e.C = ox + (c.X2 >> 1);                       // canister/2 + offset (lst:2013-2034)
                e.D = oy + (c.Y2 >> 1);
                e.F += 1;
                // Clamp to 0..$400 and 0..$300 (lst:2037-2068). NOTES §20.1: the docs' "C ≥ 4, D ≥ 3" misread
                // LDAI n (= n·$100).
                if (e.C < 0) e.C = 0; else if (e.C >= 0x400) e.C = 0x400;
                if (e.D < 0) e.D = 0; else if (e.D >= 0x300) e.D = 0x300;
            }

            EnemyDraw(i, e);
        }

        /// <summary>Chase target (lst:1686-1774, GR §6).</summary>
        private TankRecord ChooseChaseTarget(TankRecord e)
        {
            var s = State;
            if (s.OnePlayer) return s.Players[0];
            // UNCHECKED (NOTES §19.7 item 2): P1 distance is |dx| (literal reading of $074F); max(|dx|, −dy) also
            // fits every traced frame.
            int d1 = Math.Abs(e.X - s.Players[0].X);
            int d2 = Math.Max(Math.Abs(e.X - s.Players[1].X), Math.Abs(e.Y - s.Players[1].Y));
            return d1 < d2 ? s.Players[0] : s.Players[1]; // ties → P2; dead players not excluded
        }

        /// <summary>Steering $109C-$1122 (lst:3848-3970, §14.3).</summary>
        private void Steer(int i, TankRecord e, int bearing)
        {
            int h = e.Heading;
            if (bearing == h)
            {
                // $10A7-$10BE (lst:3858-3877)
                if ((e.A & EnemyState.BitAim) == 0) FireTest(i, e);
                else if ((e.A & EnemyState.BitMoving) == 0) e.A = (e.A & 0x15) + 0x0A; // $014 → $01E: tow starts
                return;
            }
            int d = h - bearing;
            int ad = Math.Abs(d);
            int m = ad < 360 - ad ? ad : 360 - ad;   // lst:3880-3909
            if (m < e.TurnStep)
            {
                // Snap (lst:3910-3921). The tow starts on the next pass (equal path above).
                e.Heading = bearing;
                if ((e.A & EnemyState.BitAim) == 0) FireTest(i, e);
                return;
            }
            if (!(ad < 180)) d = -d;                 // lst:3922-3929
            h = d < 0 ? h + e.TurnStep : h - e.TurnStep; // lst:3930-3943
            if (h < 0) h += 360; else if (h >= 360) h -= 360; // lst:3944-3965
            e.Heading = h;
        }

        /// <summary>
        /// Enemy fire test $1123-$11B7 (lst:3972-4109, §18.4, GR §6 "Enemy fire").
        /// NOTES §20.1 (d): reach is |d| &lt; 150 on both axes (strict, $112C-$1152, lst:3980-4013), and the
        /// on-field test at $1158-$1168 (lst:4023-4036) is on the enemy's own position.
        /// </summary>
        private void FireTest(int i, TankRecord e)
        {
            var s = State;
            if (FireWindowBase != e.Base) return;
            if (!(Math.Abs(e.X - e.C) < GameData.FireReach)) return;
            if (!(Math.Abs(e.Y - e.D) < GameData.FireReach)) return;
            if (!OnField(e.X, e.Y)) return;
            var p1 = s.Players[0];
            var victim = (p1.X == e.C && p1.Y == e.D) ? p1 : s.Players[1]; // lst:4038-4057
            if (!EnemyState.IsAlive(victim.A)) return;                     // lst:4058-4064
            victim.A &= 0xF;
            victim.Scale = GameData.ExplosionRam;
            victim.C = 2;
            s.Sound.Explosions++;
            AllEnemiesAim(); // lst:4074-4107
        }

        /// <summary>Every enemy record: F := 0, A := (A &amp; $1B) + 4 (lst:2457-2480, lst:4074-4107).</summary>
        private void AllEnemiesAim()
        {
            foreach (var e in State.Enemies) { e.F = 0; e.A = (e.A & 0x1B) + 4; }
        }

        /// <summary>
        /// Canister contact $0B49 (lst:2666-2835), called for enemies (lst:1906-1908) and players
        /// (lst:2335-2349). First slot neither taken nor gone within 20 (box).
        /// </summary>
        private void PickupCheck(TankRecord r, int enemyIndex)
        {
            var s = State;
            for (int j = 0; j < GameData.CanisterSlots; j++)
            {
                var c = s.Canisters[j];
                if (c.Taken || c.Gone) continue;
                if (!Box(c.X - r.X, c.Y - r.Y, GameData.PickupRadius)) continue;
                if ((r.A & EnemyState.BitTow) != 0)
                {
                    // $0BDD-$0C0A (lst:2804-2843), NOTES §20.3: a towing enemy or a player (whose word A has bit 1
                    // set during this call) passing over a free canister: word B steps once, draw scale +=
                    // B & $F, heading += (B & $F) − 8.
                    r.B = Rng.Step(r.B);
                    r.Scale += r.B & 0xF;
                    int h = r.Heading + (r.B & 0xF) - 8;
                    // UNCHECKED: literal reading of $0BFB-$0C00 stores 360 (not h − 360) when h ≥ 360. 360 then
                    // behaves as 0 in $1204 (lst:4192-4198).
                    if (h < 0) h += 360; else if (h >= 360) h = 360;
                    r.Heading = h;
                    return;
                }
                // Pick-up (lst:2718-2798, GR §6 "Pick-up").
                r.A = (r.A & 0x13) + 4;            // → $014
                int old = r.Word2;
                r.Word2 = j;
                s.Canisters[j].X2 += 1;            // taken bit
                for (int o = 0; o < 3; o++)        // another enemy aiming at this slot gets our old one (lst:2745-2767)
                {
                    if (o == enemyIndex) continue;
                    if (s.Enemies[o].Word2 == j) { s.Enemies[o].Word2 = old; break; }
                }
                r.F = 0;
                r.Speed = 0;
                var exit = GameData.SpawnPoints[r.B & 7]; // exit point = spawn entry word B & 7 (lst:2768-2798)
                r.C = exit.X; r.D = exit.Y;
                return;
            }
        }

        /// <summary>
        /// Enemy part of the draw routine $0CC6/$0D39 (lst:3023-3340): skipped off the field; a towed canister
        /// is placed behind the enemy; the fire line is requested in the enemy's window while it chases.
        /// </summary>
        private void EnemyDraw(int i, TankRecord e)
        {
            var s = State;
            if (!OnField(e.X, e.Y)) return; // lst:3125-3145
            if ((e.A & EnemyState.BitTow) != 0)
            {
                // Canister := own + R(heading)·(0, $20)·scale/128, stored ×2 with the taken bit (lst:3182-3228).
                // scale is word 4: $50 (20 units behind), plus the canister-contact jitter of this pass (§20.3,
                // trace:excerpts/p7_checks.txt (e)).
                var (sn, cs, _) = Trig.ScaledSinCos(e.Heading, e.Scale);
                int ox = -Trig.Mul128(sn, GameData.TowOffset);
                int oy = Trig.Mul128(cs, GameData.TowOffset);
                s.Canisters[e.Word2].X2 = ((e.X + ox) << 1) | 1;
                s.Canisters[e.Word2].Y2 = (e.Y + oy) << 1;
            }
            if (e.A == EnemyState.Chase && FireWindowBase == e.Base)
            {
                // Fire line (cosmetic) and Laser bit (lst:3292-3338, GR §6 "Fire line").
                // UNCHECKED: which of the 4 beam shapes is drawn in which window frame; assumed pass counter & 3.
                s.FireLines.Add(new FireLine { Record = i, Frame = s.PassCounter & 3 });
                s.Sound.Laser = true;
            }
        }

        /// <summary>Explosion countdown (lst:3093-3121, GR §7): word 4 −= $20; at 0 the record is free and an
        /// enemy's canister x bit 0 is cleared (dropped canister stays).</summary>
        private void ExplosionTick(TankRecord r, bool enemy)
        {
            r.Scale -= GameData.ExplosionStep;
            if (r.Scale > 0) return;
            r.Scale = 0;
            r.A = EnemyState.Free;
            if (enemy) State.Canisters[r.Word2].X2 &= ~1;
        }

        // ---------------------------------------------------------------- players

        private void PlayerPass(int p, in PlayerInput inp)
        {
            var s = State;
            var r = s.Players[p];
            if (EnemyState.IsFree(r.A)) return;
            if (EnemyState.IsExploding(r.A)) { ExplosionTick(r, false); return; }

            r.Scale = GameData.PlayerScale; // lst:2105

            // Rotation (lst:2141-2218, GR §3): turn step +1 on even passes up to 8; left adds, right subtracts.
            if (!inp.Left && !inp.Right) r.TurnStep = 0;
            else
            {
                if ((s.PassCounter & 1) == 0) r.TurnStep = Math.Min(r.TurnStep + 1, 8);
                int h = r.Heading;
                if (inp.Left) h += r.TurnStep;
                if (inp.Right) h -= r.TurnStep;
                if (h < 0) h += 360; else if (h >= 360) h -= 360;
                r.Heading = h;
            }

            // Thrust (lst:2219-2255): only when pass counter bits 0-1 are clear (every 4th pass, NOTES §20.1
            // corrects GR §3 "per pass"); +1 up to 10, released −1 down to 0.
            if ((s.PassCounter & 3) == 0)
            {
                if (inp.Thrust) r.Speed = Math.Min(r.Speed + 1, 10);
                else r.Speed = Math.Max(r.Speed - 1, 0);
            }

            // Move (lst:2256-2287).
            var (dx, dy, q) = Trig.Displacement(r.Heading, r.Speed);
            r.X += dx; r.Y += dy;
            // Wall clamp and bounce heading (lst:2288-2334, §20.3).
            if (r.X < 0) { r.X = 0; r.Heading = GameData.WallHeadingX[q]; }
            else if (r.X >= 0x400) { r.X = 0x3FF; r.Heading = GameData.WallHeadingX[q]; }
            if (r.Y < 0) { r.Y = 0; r.Heading = GameData.WallHeadingY[q]; }
            else if (r.Y >= 0x300) { r.Y = 0x2FF; r.Heading = GameData.WallHeadingY[q]; }

            // Canister contact with word A bit 1 temporarily set (lst:2335-2349, lst:2558-2561).
            r.A = (r.A & 0x1D) + 2;
            PickupCheck(r, -1);

            // Tank contact and chase trigger over records $00-$40 except itself (lst:2350-2557).
            for (int idx = 0; idx < 5; idx++)
            {
                var o = s.Record(idx);
                if (ReferenceEquals(o, r)) continue;
                if (!EnemyState.IsAlive(o.A)) continue;
                int ddx = o.X - r.X, ddy = o.Y - r.Y;
                if (Box(ddx, ddy, GameData.TankHitRadius))
                {
                    // Ram (lst:2393-2494, GR §7): both explode; enemy victims score for the rammer.
                    o.A &= 0xF; o.Scale = GameData.ExplosionRam; o.C = 1;
                    s.Sound.Explosions++;
                    if (idx < 3)
                    {
                        s.KillsThisWave += 1;
                        s.Score[p] += PointsTens(s.EnemyType, s.Level);
                    }
                    AllEnemiesAim();
                    r.Scale = GameData.ExplosionRam; r.A &= 0xF; r.C = 1;
                    s.Sound.Explosions++;
                    ExplosionTick(r, false); // $0A8D → $0CC6 in the same pass
                    return;
                }
                if (idx < 3 && (o.A & EnemyState.BitTow) == 0 && (o.A & EnemyState.BitMoving) != 0
                    && Box(ddx, ddy, GameData.ChaseRadius))
                {
                    // Chase start (lst:2498-2548, GR §6): A &= $1B, F := 0.
                    o.A &= 0x1B; o.F = 0;
                }
            }
            r.A &= 0x1D; // lst:2558-2561

            // Fire (lst:2562-2656, GR §3): edge-triggered on the active-low button level kept in word 2.
            int level = inp.Fire ? 0 : 1;
            if (level != r.Word2)
            {
                r.Word2 = level;
                if (inp.Fire) { TryFire(p, r); s.AutoFireTimer[p] = 0; }
            }
            else if (inp.Fire && s.AutoFire)
            {
                // Auto-fire (remake option, NOTES §23): while held, another shot every AutoFireInterval passes. With
                // all 4 slots busy the timer stays due, so the next shot leaves as soon as a slot frees up.
                if (s.AutoFireTimer[p] < GameData.AutoFireInterval) s.AutoFireTimer[p]++;
                if (s.AutoFireTimer[p] >= GameData.AutoFireInterval && TryFire(p, r)) s.AutoFireTimer[p] = 0;
            }
            if (!inp.Fire) s.AutoFireTimer[p] = 0;
        }

        /// <summary>Puts a shot in the player's first free slot (lst:2584-2656). False when all 4 are live.</summary>
        private bool TryFire(int p, TankRecord r)
        {
            var s = State;
            int first = p * GameData.ShotSlotsPerPlayer;
            for (int k = first; k < first + GameData.ShotSlotsPerPlayer; k++)
            {
                if (s.Shots[k].Live) continue;
                s.TorpedoTimer = GameData.TorpedoPasses;              // lst:2606-2609
                var (vx, vy, _) = Trig.Displacement(r.Heading, r.Speed + GameData.ShotSpeedBonus);
                s.Shots[k] = new Shot { Live = true, X = r.X, Y = r.Y, Vx = vx, Vy = vy };
                s.ShotsFired[p]++;
                return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- shots

        /// <summary>Shots (lst:3391-3623, §14.6, GR §3/§7).</summary>
        private void ShotsPass()
        {
            var s = State;
            for (int k = 0; k < s.Shots.Length; k++)
            {
                ref var sh = ref s.Shots[k];
                if (!sh.Live) continue;
                int x = sh.X + sh.Vx, y = sh.Y + sh.Vy;           // lst:3432-3445
                if (!OnField(x, y)) { sh.Live = false; continue; } // unsigned compares (lst:3446-3466)
                sh.X = x; sh.Y = y;
                for (int i = 0; i < 3; i++)
                {
                    var e = s.Enemies[i];
                    if (!Box(e.X - x, e.Y - y, GameData.ShotHitRadius)) continue; // lst:3481-3525
                    if (!EnemyState.IsAlive(e.A)) continue;
                    e.A &= 0xF; e.Scale = GameData.ExplosionShot; e.C = 0; // lst:3526-3549
                    s.Sound.Explosions++;
                    sh.Live = false;
                    s.Score[k < GameData.ShotSlotsPerPlayer ? 0 : 1] += PointsTens(s.EnemyType, s.Level); // lst:3550-3573
                    s.KillsThisWave += 1;
                    break;
                }
            }
        }
    }
}
