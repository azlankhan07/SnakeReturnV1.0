using UnityEngine;

namespace SnakeReturns.Core
{
    /// <summary>
    /// The four cardinal directions the snake can travel on the board.
    /// </summary>
    /// <remarks>
    /// LOAD-BEARING ORDER — DO NOT "TIDY" THIS.
    /// The members are laid out clockwise (Up, Right, Down, Left) with contiguous
    /// values 0..3. That numbering is part of the contract, not decoration:
    /// DirectionExtensions.IsOpposite is implemented as ((int)a + 2) % 4 == (int)b,
    /// i.e. "two clockwise quarter turns make a half turn". Reordering, renumbering,
    /// inserting or removing a member silently breaks the reverse check, and the only
    /// symptom is a snake that is allowed to double back into its own neck.
    /// </remarks>
    public enum Direction
    {
        Up = 0,
        Right = 1,
        Down = 2,
        Left = 3
    }

    /// <summary>
    /// Pure helpers over <see cref="Direction"/>. No state, no scene access.
    /// </summary>
    public static class DirectionExtensions
    {
        /// <summary>
        /// The unit step, in grid cells, for this direction.
        /// +Y is up the board and +X is to the right, matching GridManager.
        /// </summary>
        public static Vector2Int ToVector(this Direction direction)
        {
            switch (direction)
            {
                case Direction.Up:    return new Vector2Int(0, 1);
                case Direction.Right: return new Vector2Int(1, 0);
                case Direction.Down:  return new Vector2Int(0, -1);
                case Direction.Left:  return new Vector2Int(-1, 0);
                default:              return Vector2Int.zero;
            }
        }

        /// <summary>
        /// True when <paramref name="b"/> is the exact reverse of <paramref name="a"/>.
        /// </summary>
        /// <remarks>
        /// Depends entirely on the clockwise enum order declared above: adding 2 to a
        /// clockwise quarter-turn index and wrapping at 4 lands on the opposite heading.
        /// </remarks>
        public static bool IsOpposite(this Direction a, Direction b)
        {
            return ((int)a + 2) % 4 == (int)b;
        }
    }
}
