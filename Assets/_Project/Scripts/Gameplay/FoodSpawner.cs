using System.Collections.Generic;
using SnakeReturns.Core;
using UnityEngine;

namespace SnakeReturns.Gameplay
{
    /// <summary>
    /// Owns both foods: the permanent normal food and the timed big food.
    /// </summary>
    /// <remarks>
    /// This class answers "where is the food, and is the big one still there?". It is told
    /// when something was eaten and when a tick passed; it does not watch the snake, and it
    /// does not decide what eating is worth.
    ///
    /// THE BIG FOOD APPEARS ALONGSIDE THE NORMAL FOOD, NEVER INSTEAD OF IT.
    /// That is the entire mechanic: there is always a safe option nearby and a valuable one
    /// somewhere awkward, and choosing between them under a shrinking clock is the game. If
    /// the big food replaced the normal one there would be no choice left — eat it or starve.
    /// </remarks>
    public class FoodSpawner : MonoBehaviour
    {
        [Header("Big Food")]
        [Tooltip("A big food is offered after every Nth normal food.")]
        [SerializeField] private int normalFoodsPerBig = 4;

        [Tooltip("How many TICKS the big food stays on the board.")]
        [SerializeField] private int bigFoodLifetimeTicks = 40;

        [Tooltip("Ticks remaining at which the blink speeds up to warn the player.")]
        [SerializeField] private int bigFoodWarningTicks = 10;

        [Tooltip("Minimum Manhattan distance from the normal food, so the two are a real choice apart.")]
        [SerializeField] private int bigFoodMinDistance = 6;

        [Header("Blink")]
        [SerializeField] private int blinkPeriod = 4;
        [SerializeField] private int blinkPeriodWarning = 2;

        [Header("Dependencies")]
        [SerializeField] private GridManager grid;

        public Vector2Int NormalFood { get; private set; }
        public Vector2Int? BigFood { get; private set; }
        public bool BigFoodVisible { get; private set; } = true;
        public int BigFoodTicksLeft { get; private set; }
        public int BigFoodLifetimeTicks => bigFoodLifetimeTicks;

        private int normalFoodCounter;

        // Reused across the whole run and never reallocated. Building a fresh 273-cell list
        // every time a food spawns would produce garbage on a steady cadence, and that is
        // precisely the pattern that gives you a periodic GC hitch — in a game whose entire
        // feel depends on an even tempo, a hitch every few seconds is the one thing you
        // cannot ship. Clear() keeps the capacity and throws nothing away.
        private readonly List<Vector2Int> freeCells = new List<Vector2Int>(273);

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
        }

        /// <summary>Clears both foods and places a fresh normal food. Call at the start of a run.</summary>
        public void ResetSpawner(SnakeController snake)
        {
            ClearBigFood();
            normalFoodCounter = 0;
            TrySpawnNormal(snake);
        }

        /// <summary>
        /// Places the normal food on a random free cell.
        /// Returns false when there is no free cell left — the board is full and the player has won.
        /// </summary>
        public bool TrySpawnNormal(SnakeController snake)
        {
            CollectFreeCells(snake, excludeBigFood: true, excludeNormalFood: false);
            if (freeCells.Count == 0)
            {
                return false;
            }

            NormalFood = freeCells[Random.Range(0, freeCells.Count)];
            return true;
        }

        /// <summary>
        /// Counts a normal food eaten and offers a big food on every Nth one.
        /// </summary>
        public void OnNormalFoodEaten(SnakeController snake)
        {
            normalFoodCounter++;

            if (normalFoodsPerBig > 0 && normalFoodCounter % normalFoodsPerBig == 0)
            {
                TrySpawnBig(snake);
            }
        }

        public void ClearBigFood()
        {
            BigFood = null;
            BigFoodTicksLeft = 0;
            BigFoodVisible = true;
        }

        /// <summary>
        /// Ages the big food by one tick and updates its blink.
        /// Returns true on the single tick it expires, so the caller can react once.
        /// </summary>
        /// <remarks>
        /// THE LIFETIME IS COUNTED IN TICKS, NOT SECONDS, and that is the point.
        /// Crossing the board is 21 moves, so 40 ticks is always "about two board crossings"
        /// no matter how fast the game is running. A timer in seconds would quietly become
        /// brutal at 12 steps/sec — exactly when the snake is longest and routing around your
        /// own body is hardest. Ticks keep the offer the same size all game.
        /// </remarks>
        public bool TickBigFood()
        {
            if (!BigFood.HasValue)
            {
                return false;
            }

            BigFoodTicksLeft--;

            if (BigFoodTicksLeft <= 0)
            {
                ClearBigFood();

                // Restart the count so the next big food is a full N normal foods away,
                // rather than arriving right behind the one that just timed out.
                normalFoodCounter = 0;
                return true;
            }

            int period = BigFoodTicksLeft <= bigFoodWarningTicks ? blinkPeriodWarning : blinkPeriod;
            BigFoodVisible = (BigFoodTicksLeft / period) % 2 == 0;
            return false;
        }

        private void TrySpawnBig(SnakeController snake)
        {
            // Never teleport a big food that is already on the board and already being chased.
            if (BigFood.HasValue)
            {
                return;
            }

            // THE BIG FOOD MUST NEVER LAND ON THE NORMAL FOOD'S CELL.
            // If it did, one head-enter would satisfy both eat checks at once. Step() checks
            // big first and would report AteBigFood, so the caller's big-food branch runs and
            // its normal-food branch never does — no replacement normal food is spawned, and
            // the board silently loses its normal food for the rest of the run.
            CollectFreeCells(snake, excludeBigFood: false, excludeNormalFood: true);
            if (freeCells.Count == 0)
            {
                return;
            }

            // Prefer somewhere far enough from the normal food that the player has a genuine
            // decision to make. Counted first, then indexed, so no second list is allocated.
            int qualifying = 0;
            for (int i = 0; i < freeCells.Count; i++)
            {
                if (ManhattanDistance(freeCells[i], NormalFood) >= bigFoodMinDistance)
                {
                    qualifying++;
                }
            }

            Vector2Int chosen;
            if (qualifying > 0)
            {
                int pick = Random.Range(0, qualifying);
                chosen = freeCells[0];
                for (int i = 0; i < freeCells.Count; i++)
                {
                    if (ManhattanDistance(freeCells[i], NormalFood) < bigFoodMinDistance)
                    {
                        continue;
                    }

                    if (pick == 0)
                    {
                        chosen = freeCells[i];
                        break;
                    }

                    pick--;
                }
            }
            else
            {
                // Late in a run the board can be too crowded for the distance rule. A close
                // big food beats no big food.
                chosen = freeCells[Random.Range(0, freeCells.Count)];
            }

            BigFood = chosen;
            BigFoodTicksLeft = bigFoodLifetimeTicks;
            BigFoodVisible = true;
        }

        /// <summary>
        /// Refills the CACHED free-cell list. Never allocates a new List — see the field comment.
        /// </summary>
        private void CollectFreeCells(SnakeController snake, bool excludeBigFood, bool excludeNormalFood)
        {
            freeCells.Clear();

            for (int y = 0; y < grid.Height; y++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    Vector2Int cell = new Vector2Int(x, y);

                    if (snake != null && snake.IsOccupied(cell))
                    {
                        continue;
                    }

                    if (excludeNormalFood && cell == NormalFood)
                    {
                        continue;
                    }

                    if (excludeBigFood && BigFood.HasValue && cell == BigFood.Value)
                    {
                        continue;
                    }

                    freeCells.Add(cell);
                }
            }
        }

        private static int ManhattanDistance(Vector2Int a, Vector2Int b)
        {
            return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
        }
    }
}
