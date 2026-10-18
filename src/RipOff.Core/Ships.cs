using System;
using RipOff.Core.Shapes;

namespace RipOff.Core
{
    /// <summary>
    /// One selectable player ship (Enhanced SHIP SELECT, NOTES §30; abilities NOTES §31).
    /// </summary>
    /// <param name="ShapeSet">ShapeData tank set drawn for it (0 = the arcade's P1 ship, 1 = P2's), or -1 for a
    /// remake shape in <paramref name="Body"/>.</param>
    /// <param name="Speed">Top speed (the arcade's thrust cap is 10).</param>
    /// <param name="Turn">Top turn step in degrees per pass (the arcade's is 8).</param>
    /// <param name="Armor">Enemy-fire hits it takes to destroy (the arcade: 1).</param>
    /// <param name="Damage">Hits one of its shots does to an enemy (multi-hit enemies, NOTES §32).</param>
    /// <param name="Body">Remake hull (pivot-relative, y up, nose at -y like the arcade shapes); null for arcade sets.</param>
    /// <param name="Plates">Armor plating drawn only while the ship has more than one hit left (remake art).</param>
    public sealed record ShipSpec(int Id, string Name, string Info, int ShapeSet,
        int Speed = GameData.PlayerMaxSpeed, int Turn = GameData.PlayerMaxTurnStep, int Armor = 1, int Damage = 1,
        Seg[]? Body = null, Seg[]? Plates = null);

    /// <summary>
    /// The ships a player can pick in Enhanced (SHIP SELECT on). Ids are what GameConfig.Ship1/Ship2 and
    /// GameState.Ships carry, so they must stay stable: new ships are appended. Arcade, and Enhanced with SHIP SELECT
    /// off, always use the arcade pairing: P1 = ship 0, P2 = ship 1 (<see cref="Default"/>). The stats are used only
    /// with SHIP ABILITIES on; otherwise every ship flies like the arcade's.
    /// </summary>
    public static class Ships
    {
        public const int Classic1 = 0, Classic2 = 1, Scout = 2, Cruiser = 3;

        /// <summary>SCOUT hull: a slim dart, ±16 wide, nose at -40 (remake art).</summary>
        public static readonly Seg[] ScoutBody =
        {
            new(0, -40, 9, 2, false), new(9, 2, 16, 14, false), new(16, 14, 5, 8, false), new(5, 8, 0, 12, false),
            new(0, 12, -5, 8, false), new(-5, 8, -16, 14, false), new(-16, 14, -9, 2, false), new(-9, 2, 0, -40, false),
            new(0, -26, 0, 4, true), new(-9, 2, 9, 2, true),
        };

        /// <summary>CRUISER hull: wide and blunt, ±26 wide, with a gun barrel to -44 (remake art).</summary>
        public static readonly Seg[] CruiserBody =
        {
            new(-8, -34, 8, -34, false), new(8, -34, 14, -20, false), new(14, -20, 26, -12, false), new(26, -12, 26, 10, false),
            new(26, 10, 14, 16, false), new(14, 16, -14, 16, false), new(-14, 16, -26, 10, false), new(-26, 10, -26, -12, false),
            new(-26, -12, -14, -20, false), new(-14, -20, -8, -34, false),
            new(-2, -34, -2, -44, false), new(-2, -44, 2, -44, false), new(2, -44, 2, -34, false),
        };

        /// <summary>CRUISER armor plates (drawn while it has its second hit left).</summary>
        public static readonly Seg[] CruiserPlates =
        {
            new(-14, -20, 14, -20, true), new(-20, -4, 20, -4, true), new(-20, 8, 20, 8, true),
            new(-20, -4, -20, 8, true), new(20, -4, 20, 8, true),
        };

        public static readonly ShipSpec[] All =
        {
            new(Classic1, "CLASSIC 1", "THE ARCADE'S PLAYER 1 SHIP", 0),
            new(Classic2, "CLASSIC 2", "THE ARCADE'S PLAYER 2 SHIP", 1),
            new(Scout, "SCOUT", "FAST AND NIMBLE - ONE HIT DESTROYS IT", -1, Speed: 14, Turn: 10, Body: ScoutBody),
            new(Cruiser, "CRUISER", "SLOW - TAKES TWO HITS - SHOTS DO DOUBLE DAMAGE", -1, Speed: 7, Turn: 6, Armor: 2, Damage: 2,
                Body: CruiserBody, Plates: CruiserPlates),
        };

        public static int Count => All.Length;

        public static bool IsDefined(int id) => id >= 0 && id < All.Length;

        public static ShipSpec Get(int id) => IsDefined(id) ? All[id] : throw new ArgumentOutOfRangeException(nameof(id), id, "unknown ship");

        /// <summary>The arcade's ship of player <paramref name="p"/> (P1 = 0, P2 = 1).</summary>
        public static int Default(int p) => p == 0 ? Classic1 : Classic2;

        /// <summary>The lowest ship id other than <paramref name="taken"/>: where a duplicate pick is moved.</summary>
        public static int FirstOtherThan(int taken)
        {
            for (int i = 0; i < All.Length; i++) if (i != taken) return i;
            throw new InvalidOperationException("only one ship");
        }

        /// <summary>SHIP ABILITIES in effect: its toggle on and SHIP SELECT on (it Requires it; with the arcade pairing
        /// the ships have the arcade's stats anyway, and no armor state is kept or hashed).</summary>
        public static bool AbilitiesOn(GameRules rules) => rules.ShipAbilities && rules.ShipSelect;

        /// <summary>The stats a ship flies with under <paramref name="rules"/>: its own with SHIP ABILITIES on, the
        /// arcade's otherwise (speed 10, turn 8, one hit, damage 1).</summary>
        public static (int Speed, int Turn, int Armor, int Damage) Stats(GameRules rules, int id)
        {
            if (!AbilitiesOn(rules)) return (GameData.PlayerMaxSpeed, GameData.PlayerMaxTurnStep, 1, 1);
            var s = Get(id);
            return (s.Speed, s.Turn, s.Armor, s.Damage);
        }
    }
}
