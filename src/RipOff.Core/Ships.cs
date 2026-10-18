using System;

namespace RipOff.Core
{
    /// <summary>One selectable player ship (Enhanced SHIP SELECT, NOTES §30). <paramref name="ShapeSet"/> is the
    /// ShapeData tank set drawn for it (0 = the arcade's P1 ship, 1 = P2's).</summary>
    public sealed record ShipSpec(int Id, string Name, string Info, int ShapeSet);

    /// <summary>
    /// The ships a player can pick in Enhanced (SHIP SELECT on). Ids are what GameConfig.Ship1/Ship2 and
    /// GameState.Ships carry, so they must stay stable: new ships are appended. Arcade, and Enhanced with SHIP SELECT
    /// off, always use the arcade pairing: P1 = ship 0, P2 = ship 1 (<see cref="Default"/>).
    /// </summary>
    public static class Ships
    {
        public const int Classic1 = 0, Classic2 = 1;

        public static readonly ShipSpec[] All =
        {
            new(Classic1, "CLASSIC 1", "THE ARCADE'S PLAYER 1 SHIP", 0),
            new(Classic2, "CLASSIC 2", "THE ARCADE'S PLAYER 2 SHIP", 1),
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
    }
}
