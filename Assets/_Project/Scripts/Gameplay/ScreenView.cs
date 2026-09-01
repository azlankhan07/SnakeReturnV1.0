using SnakeReturns.Core;
using UnityEngine;

namespace SnakeReturns.Gameplay
{
    /// <summary>
    /// Everything the LCD says: titles, prompts, the score and the big-food timer.
    /// </summary>
    /// <remarks>
    /// A pure observer. It subscribes to GameManager and reads ScoreManager and FoodSpawner;
    /// it never calls into any of them, and GameManager does not know this class exists.
    ///
    /// DELIBERATELY SEPARATE FROM BoardView'S POOL. Paused hides the board but must still show
    /// the word PAUSED, so the two cannot share a visibility switch — one goes dark while the
    /// other keeps talking.
    /// </remarks>
    public class ScreenView : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private GridManager grid;
        [SerializeField] private ScoreManager score;
        [SerializeField] private FoodSpawner food;
        [SerializeField] private LcdText text;
        [SerializeField] private GameManager game;
        [SerializeField] private TickSystem tick;

        [Header("Layout")]
        [Tooltip("Must match CameraFramer's hudRows.")]
        [SerializeField] private int hudRows = 2;

        [Tooltip("Padding inside the HUD strip, in font pixels.")]
        [SerializeField] private int hudPadding = 1;

        [Tooltip("Width of the big-food timer bar, in cells.")]
        [SerializeField] private float barWidth = 12f;

        [Tooltip("Height of the score digits in ROWS — the same unit as hudRows. Stated as a height rather than a font size because the font pixel is 1/12 of a cell, so asking for a scale factor would mean redoing that arithmetic by hand every time hudRows moves. Clamped to what the strip can actually hold.")]
        [Range(0.5f, 3f)]
        [SerializeField] private float scoreRows = 1.5f;

        [SerializeField] private string credit = "MADE BY AZLAN KHAN";

        private GameState state = GameState.Boot;
        private int lastScore = -1;
        private int lastTicks = -1;
        private bool lastBigAlive;

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
            Wiring.Resolve(this, ref score, nameof(score));
            Wiring.Resolve(this, ref food, nameof(food));
            Wiring.Resolve(this, ref game, nameof(game));
            Wiring.Resolve(this, ref tick, nameof(tick));

            // The text mesh usually hangs off this object as a child, so look there before
            // falling back to the scene-wide search.
            if (text == null)
            {
                text = GetComponentInChildren<LcdText>(true);
            }
            Wiring.Resolve(this, ref text, nameof(text));
        }

        private void OnEnable()
        {
            AutoWire();

            if (game != null)
            {
                game.OnStateChanged += SetState;

                // Catch the state the game was ALREADY in. Subscribing only gets us future
                // transitions, and if the game reached Menu before this object enabled, the
                // screen would sit blank until the player pressed something.
                SetState(game.State);
            }

            if (tick != null)
            {
                tick.OnTick += Tick;
            }
        }

        private void OnDisable()
        {
            if (game != null)
            {
                game.OnStateChanged -= SetState;
            }

            if (tick != null)
            {
                tick.OnTick -= Tick;
            }
        }

        public void SetState(GameState next)
        {
            state = next;
            Redraw();
        }

        /// <summary>Subscribed to TickSystem.OnTick.</summary>
        public void Tick()
        {
            if (state != GameState.Playing && state != GameState.Ready)
            {
                return;
            }

            if (score == null || food == null)
            {
                return;
            }

            int ticks = food.BigFoodTicksLeft;
            bool bigAlive = food.BigFood.HasValue;

            // A normal tick costs three comparisons and no mesh work at all.
            if (score.Score == lastScore && ticks == lastTicks && bigAlive == lastBigAlive)
            {
                return;
            }

            lastScore = score.Score;
            lastTicks = ticks;
            lastBigAlive = bigAlive;

            Redraw();
        }

        private void Redraw()
        {
            if (text == null || grid == null || score == null)
            {
                return;
            }

            text.Clear();

            float fieldTop = grid.Height * 0.5f;         //  6.5
            float fieldBottom = -grid.Height * 0.5f;     // -6.5
            float right = grid.Width * 0.5f;             //  10.5
            float left = -grid.Width * 0.5f;             // -10.5
            float stripTop = fieldTop + hudRows;         //  8.5
            float stripBottom = fieldTop;                //  6.5

            float p = text.PixelSize;

            switch (state)
            {
                case GameState.Menu:
                    text.Add("SNAKE RETURNS", 0f, 4.2f, TextAlign.Centre);
                    text.Add("HIGH SCORE " + Format(score.HighScore), 0f, 0.2f, TextAlign.Centre);
                    text.Add("ENTER TO START", 0f, -2.0f, TextAlign.Centre);
                    text.Add(credit, 0f, fieldBottom + 1.1f, TextAlign.Centre);
                    break;

                case GameState.Ready:
                    DrawHud(stripTop, stripBottom, right, left, p);
                    text.Add("READY", 0f, 1.0f, TextAlign.Centre);
                    break;

                case GameState.Playing:
                    DrawHud(stripTop, stripBottom, right, left, p);
                    break;

                case GameState.Paused:
                    // No HUD. The strip goes with the hidden board, so the field really is empty.
                    text.Add("PAUSED", 0f, 1.0f, TextAlign.Centre);
                    text.Add("ESC RESUME Q MENU", 0f, fieldBottom + 1.2f, TextAlign.Centre);
                    break;

                case GameState.GameOver:
                    text.Add("GAME OVER", 0f, 4.0f, TextAlign.Centre);
                    text.Add("SCORE " + Format(score.Score), 0f, 1.6f, TextAlign.Centre);
                    text.Add(
                        score.IsNewRecord ? "NEW BEST!" : "BEST " + Format(score.HighScore),
                        0f, -0.4f, TextAlign.Centre);
                    text.Add("PRESS ENTER", 0f, fieldBottom + 1.2f, TextAlign.Centre);
                    break;
            }

            text.Rebuild();
        }

        /// <summary>
        /// The score, right-aligned in the HUD strip, and the big-food timer bar to its left.
        /// </summary>
        /// <remarks>
        /// THE SCORE SIZE IS DERIVED, NEVER TYPED IN. At body size a 7-pixel glyph is 0.58 of a
        /// cell, so in a 2-cell strip the number sits in the middle of an empty band and reads
        /// as an afterthought rather than as the thing the player is chasing.
        ///
        /// The timer bar shares the strip to the LEFT of the score rather than owning a row of
        /// its own: the score is four digits at most, which leaves the left two thirds free.
        /// </remarks>
        private void DrawHud(float stripTop, float stripBottom, float right, float left, float p)
        {
            float pad = hudPadding * p;
            float mid = (stripTop + stripBottom) * 0.5f;

            float available = (stripTop - stripBottom) - pad * 2f;
            float glyph = Mathf.Min(scoreRows, available);
            float scale = Mathf.Max(1f, glyph / (LcdFont.Height * p));

            text.Add(Format(score.Score), right - pad, mid + glyph * 0.5f, TextAlign.Right, scale);

            if (food == null || !food.BigFood.HasValue)
            {
                return;
            }

            int total = Mathf.Max(1, food.BigFoodLifetimeTicks);
            float fraction = Mathf.Clamp01(food.BigFoodTicksLeft / (float)total);
            if (fraction <= 0f)
            {
                return;
            }

            float h = LcdFont.Height * p * 0.6f;
            text.AddBar(left + pad, mid + h * 0.5f, barWidth * fraction, h);
        }

        private static string Format(int value)
        {
            return Mathf.Clamp(value, 0, 9999).ToString("D4");
        }
    }
}
