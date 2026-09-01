using UnityEngine;

namespace SnakeReturns.Core
{
    /// <summary>
    /// The board: its size, and the only place that converts between cell coordinates
    /// and world positions. Cells are integer (x, y) with (0, 0) at the bottom-left.
    /// </summary>
    /// <remarks>
    /// This class answers questions about the board. It does not know what a snake is,
    /// and it never decides what any answer means.
    /// </remarks>
    public class GridManager : MonoBehaviour
    {
        [Header("Board Size")]
        [Tooltip("Cells across. Should be ODD so the board has a true centre cell for the snake to start on.")]
        [SerializeField] private int width = 21;

        [Tooltip("Cells tall. Should be ODD so the board has a true centre cell for the snake to start on.")]
        [SerializeField] private int height = 13;

        [Header("Gizmos")]
        [SerializeField] private bool drawGizmos = true;
        [SerializeField] private Color gridColor = new Color(1f, 1f, 1f, 0.08f);
        [SerializeField] private Color borderColor = new Color(0.2f, 1f, 0.6f, 0.65f);
        [SerializeField] private Color centreColor = new Color(1f, 0.85f, 0.2f, 0.9f);

        public int Width => width;
        public int Height => height;
        public int CellCount => width * height;

        /// <summary>The middle cell. With the default 21 x 13 board this is (10, 6), which maps to world origin.</summary>
        public Vector2Int CentreCell => new Vector2Int(width / 2, height / 2);

        private void Awake()
        {
            // An even dimension has no true centre cell, so the snake would start half a
            // cell off the middle of the phone screen and the board would look misaligned.
            if (width % 2 == 0 || height % 2 == 0)
            {
                Debug.LogWarning(
                    $"[GridManager] Board is {width} x {height}. Both dimensions should be ODD so a true centre cell exists.",
                    this);
            }
        }

        private void OnValidate()
        {
            // Clamp to a sane minimum ONLY. Deliberately no snapping to odd numbers:
            // OnValidate fires on every keystroke in the Inspector, so snapping would
            // rewrite the value while it is being typed and make the field untypeable
            // (type "2" heading for "21" and it jumps to 3 under your cursor).
            if (width < 3) width = 3;
            if (height < 3) height = 3;
        }

        /// <summary>True if the cell lies on the board.</summary>
        public bool IsInBounds(Vector2Int cell)
        {
            return cell.x >= 0 && cell.x < width && cell.y >= 0 && cell.y < height;
        }

        /// <summary>
        /// Centre of a cell in world space, on the z = 0 plane.
        /// The board is centred on the origin: (0,0) -> (-10,-6,0), centre -> (0,0,0), (20,12) -> (10,6,0).
        /// </summary>
        public Vector3 CellToWorld(Vector2Int cell)
        {
            return new Vector3(
                cell.x - (width - 1) * 0.5f,
                cell.y - (height - 1) * 0.5f,
                0f);
        }

        /// <summary>
        /// Inverse of <see cref="CellToWorld"/>, rounded to the nearest cell.
        /// Editor and debugging only — nothing in the game loop should be asking this.
        /// </summary>
        public Vector2Int WorldToCell(Vector3 world)
        {
            return new Vector2Int(
                Mathf.RoundToInt(world.x + (width - 1) * 0.5f),
                Mathf.RoundToInt(world.y + (height - 1) * 0.5f));
        }

        /// <summary>
        /// Wraps a cell back onto the board, so walking off one edge arrives at the opposite one.
        /// </summary>
        /// <remarks>
        /// DOUBLE MODULO, on purpose. In C# the % operator keeps the sign of the dividend:
        /// -1 % 21 is -1, not 20. A single % therefore sends a snake leaving the left edge to
        /// cell (-1, y) — off the board, invisible, and out of bounds for every other check.
        /// Adding the dimension and taking the remainder again forces the result non-negative.
        /// </remarks>
        public Vector2Int Wrap(Vector2Int cell)
        {
            int x = ((cell.x % width) + width) % width;
            int y = ((cell.y % height) + height) % height;
            return new Vector2Int(x, y);
        }

        private void OnDrawGizmos()
        {
            // Scene view only. The player never sees a grid — the board is drawn by the
            // view layer onto the phone's RenderTexture.
            if (!drawGizmos || width < 1 || height < 1)
            {
                return;
            }

            float left   = -(width  * 0.5f);
            float right  =   width  * 0.5f;
            float bottom = -(height * 0.5f);
            float top    =   height * 0.5f;

            // Faint interior lines, one per cell boundary.
            Gizmos.color = gridColor;
            for (int x = 1; x < width; x++)
            {
                float wx = left + x;
                Gizmos.DrawLine(new Vector3(wx, bottom, 0f), new Vector3(wx, top, 0f));
            }
            for (int y = 1; y < height; y++)
            {
                float wy = bottom + y;
                Gizmos.DrawLine(new Vector3(left, wy, 0f), new Vector3(right, wy, 0f));
            }

            // Brighter border around the playable area.
            Gizmos.color = borderColor;
            Vector3 bl = new Vector3(left, bottom, 0f);
            Vector3 br = new Vector3(right, bottom, 0f);
            Vector3 tr = new Vector3(right, top, 0f);
            Vector3 tl = new Vector3(left, top, 0f);
            Gizmos.DrawLine(bl, br);
            Gizmos.DrawLine(br, tr);
            Gizmos.DrawLine(tr, tl);
            Gizmos.DrawLine(tl, bl);

            // The centre cell, where the snake starts.
            Gizmos.color = centreColor;
            Gizmos.DrawWireCube(CellToWorld(CentreCell), new Vector3(0.8f, 0.8f, 0.8f));
        }
    }
}
