using System.Collections.Generic;
using SnakeReturns.Core;
using UnityEngine;

namespace SnakeReturns.Gameplay
{
    /// <summary>
    /// What happened on a single step. The snake reports; it does not interpret.
    /// </summary>
    public enum StepResult
    {
        Moved,
        AteNormalFood,
        AteBigFood,
        DiedWall,
        DiedSelf
    }

    /// <summary>
    /// The snake as pure grid logic: a list of cells and the rules for advancing it one step.
    /// </summary>
    /// <remarks>
    /// Holds cells, not transforms. Owns no GameObjects, instantiates nothing, draws nothing.
    /// Step() reports what happened; GameManager decides what that means; the view reads Body
    /// and draws it. Keep that split.
    /// </remarks>
    public class SnakeController : MonoBehaviour
    {
        [Header("Start State")]
        [SerializeField] private int startLength = 3;
        [SerializeField] private Direction startDirection = Direction.Right;

        [Tooltip("ON. Leaving one edge re-enters at the same cell on the opposite edge, as Snake II did. OFF gives solid walls that kill.")]
        [SerializeField] private bool wrapAround = true;

        [Header("Dependencies")]
        [SerializeField] private GridManager grid;
        [SerializeField] private InputReader input;

        // TWO PARALLEL STRUCTURES, ON PURPOSE.
        // The List gives ORDER: which cell is the head, which is the tail, and what order to
        // draw the segments in. The HashSet gives O(1) "is this cell occupied?" — collision is
        // tested every single tick, and food spawning scans all 273 cells looking for free
        // ones, so a linear scan of the body would be the hot path in both. The set earns its
        // keep several times over.
        // THE COST OF THE PATTERN: they must be kept in sync on EVERY mutation, without
        // exception. Every add to one is an add to the other; same for every removal.
        private readonly List<Vector2Int> body = new List<Vector2Int>(273);
        private readonly HashSet<Vector2Int> occupied = new HashSet<Vector2Int>();

        /// <summary>Every cell of the snake, head first.</summary>
        public IReadOnlyList<Vector2Int> Body => body;

        public Vector2Int Head => body[0];
        public int Length => body.Count;

        public bool IsOccupied(Vector2Int cell) => occupied.Contains(cell);

        private void Reset()
        {
            AutoWire();
        }

        private void Awake()
        {
            AutoWire();
        }

        private void AutoWire()
        {
            Wiring.Resolve(this, ref grid, nameof(grid));
            Wiring.Resolve(this, ref input, nameof(input));
        }

        /// <summary>
        /// Rebuilds the snake at the centre of the board, laid out backwards from the head
        /// along its starting heading, and re-anchors the input reader to match.
        /// </summary>
        public void ResetSnake()
        {
            body.Clear();
            occupied.Clear();

            Vector2Int head = grid.CentreCell;
            Vector2Int back = startDirection.ToVector() * -1;

            for (int i = 0; i < startLength; i++)
            {
                Vector2Int cell = head + back * i;
                body.Add(cell);
                occupied.Add(cell);
            }

            // The reader must agree with the body's heading, or the first press could be
            // validated against a stale direction and a legal turn would be rejected.
            input.ResetTo(startDirection);
        }

        /// <summary>
        /// Advances the snake one cell and reports what happened. Decides nothing beyond the
        /// rules of the grid — scoring, spawning and game over belong to the caller.
        /// </summary>
        public StepResult Step(Vector2Int normalFood, Vector2Int? bigFood)
        {
            Direction dir = input.ConsumeDirection();
            Vector2Int newHead = body[0] + dir.ToVector();

            // Wall.
            if (!grid.IsInBounds(newHead))
            {
                if (!wrapAround)
                {
                    return StepResult.DiedWall;
                }

                newHead = grid.Wrap(newHead);
            }

            bool ateNormal = newHead == normalFood;
            bool ateBig = bigFood.HasValue && newHead == bigFood.Value;
            bool willGrow = ateNormal || ateBig;

            Vector2Int tail = body[body.Count - 1];

            // THE TAIL EXCEPTION.
            // The tail vacates its cell on this very step — UNLESS the snake is eating, in
            // which case the body grows, the tail stays put and its cell is genuinely solid.
            // So entering the tail's cell is legal exactly when the snake is not growing.
            // Get this wrong and the snake dies from touching its own tail tip while chasing
            // it, a move that must ALWAYS be legal. It only bites when the snake is long and
            // fast, so it will never show up in early testing — it shows up in a good run.
            if (occupied.Contains(newHead) && !(newHead == tail && !willGrow))
            {
                return StepResult.DiedSelf;
            }

            // ORDER MATTERS: the tail comes off BEFORE the head goes on.
            // Reverse it and the legal tail-follow case breaks. Adding the head to a cell that
            // is still in the HashSet is a silent no-op (sets ignore duplicates), and removing
            // the tail then takes that same cell straight back out — leaving the head's cell
            // unmarked, so next tick something walks clean through the snake's neck.
            if (!willGrow)
            {
                body.RemoveAt(body.Count - 1);
                occupied.Remove(tail);
            }

            body.Insert(0, newHead);
            occupied.Add(newHead);

            if (ateBig)
            {
                return StepResult.AteBigFood;
            }

            if (ateNormal)
            {
                return StepResult.AteNormalFood;
            }

            return StepResult.Moved;
        }
    }
}
