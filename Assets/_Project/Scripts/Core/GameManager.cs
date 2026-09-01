using System;
using SnakeReturns.Gameplay;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SnakeReturns.Core
{
    public enum GameState
    {
        Boot,
        Menu,
        Ready,
        Playing,
        Paused,
        GameOver
    }

    /// <summary>
    /// The state machine, and the only class that knows how the pieces fit together.
    /// </summary>
    /// <remarks>
    /// Everything else answers questions: the snake reports what happened on a step, the
    /// spawner reports where the food is, the score keeps a number. This class is the one
    /// that decides what any of it MEANS — that DiedSelf ends the run, that a full board is
    /// a win, that eating is worth ten points and one notch of speed.
    /// </remarks>
    public class GameManager : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private GridManager grid;
        [SerializeField] private TickSystem tick;
        [SerializeField] private SnakeController snake;
        [SerializeField] private FoodSpawner food;
        [SerializeField] private ScoreManager score;
        [SerializeField] private InputReader input;
        [SerializeField] private BoardView view;

        [Header("Timing")]
        [Tooltip("Seconds the snake sits still on READY before the first tick.")]
        [SerializeField] private float readyDuration = 1f;

        public GameState State { get; private set; } = GameState.Boot;

        /// <summary>True when the run ended because there was nowhere left to put food.</summary>
        public bool BoardCleared { get; private set; }

        public event Action<GameState> OnStateChanged;

        private int foodEaten;
        private float readyTimer;

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
            Wiring.Resolve(this, ref tick, nameof(tick));
            Wiring.Resolve(this, ref snake, nameof(snake));
            Wiring.Resolve(this, ref food, nameof(food));
            Wiring.Resolve(this, ref score, nameof(score));
            Wiring.Resolve(this, ref input, nameof(input));
            Wiring.Resolve(this, ref view, nameof(view));
        }

        private void Start()
        {
            if (tick == null)
            {
                Debug.LogError("[GameManager] No TickSystem, so nothing can ever advance. Disabling.", this);
                enabled = false;
                return;
            }

            tick.OnTick += HandleTick;
            SetState(GameState.Menu);
        }

        private void OnDestroy()
        {
            // A MonoBehaviour that is destroyed while still hooked to an event on a surviving
            // object leaks: the TickSystem keeps a reference to a dead behaviour and keeps
            // firing into it. Null-guarded because tick may never have been resolved.
            if (tick != null)
            {
                tick.OnTick -= HandleTick;
            }
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            bool enter = keyboard.enterKey.wasPressedThisFrame
                         || keyboard.numpadEnterKey.wasPressedThisFrame
                         || keyboard.spaceKey.wasPressedThisFrame;
            bool escape = keyboard.escapeKey.wasPressedThisFrame;

            switch (State)
            {
                case GameState.Menu:
                    if (enter)
                    {
                        BeginRun();
                    }
                    break;

                case GameState.Ready:
                    readyTimer -= Time.deltaTime;
                    if (readyTimer <= 0f)
                    {
                        SetState(GameState.Playing);
                        tick.Run();
                    }
                    break;

                case GameState.Playing:
                    if (escape)
                    {
                        tick.Pause();

                        // The paused screen HIDES the board. At 12 steps/sec, pausing to study
                        // your route is a real advantage in a score-chasing game — blanking the
                        // field removes the exploit, and it is less to draw besides.
                        view.Hide();
                        SetState(GameState.Paused);
                    }
                    break;

                case GameState.Paused:
                    if (escape || enter)
                    {
                        view.Render();
                        SetState(GameState.Playing);
                        tick.Run();
                    }
                    else if (keyboard.qKey.wasPressedThisFrame)
                    {
                        SetState(GameState.Menu);
                    }
                    break;

                case GameState.GameOver:
                    // Enter restarts straight into a run, never back to the menu. For a score
                    // game the retry loop has to be near-instant; a menu round trip is exactly
                    // what kills the "one more go" reflex.
                    if (enter)
                    {
                        BeginRun();
                    }
                    else if (escape)
                    {
                        SetState(GameState.Menu);
                    }
                    break;
            }
        }

        private void SetState(GameState next)
        {
            State = next;
            OnStateChanged?.Invoke(next);
        }

        /// <summary>
        /// Resets every piece of run state and puts the snake on the board, still frozen.
        /// </summary>
        /// <remarks>
        /// A full reset with NO scene reload: the scene is the shell, not the state. Reloading
        /// would throw away the view's pool and every resolved reference to rebuild identical
        /// ones, and would cost a hitch on exactly the action a player repeats most.
        ///
        /// The tick is NOT running yet — but InputReader keeps reading in its own Update, so an
        /// eager first turn pressed during READY is buffered rather than thrown away.
        /// </remarks>
        private void BeginRun()
        {
            foodEaten = 0;
            BoardCleared = false;

            score.ResetRun();
            tick.ResetSpeed();
            snake.ResetSnake();
            food.ResetSpawner(snake);
            view.Render();

            readyTimer = readyDuration;
            SetState(GameState.Ready);
        }

        private void EndRun(bool cleared)
        {
            BoardCleared = cleared;
            tick.Pause();

            // The only disk write in the whole run.
            score.CommitHighScore();

            SetState(GameState.GameOver);
        }

        private void HandleTick()
        {
            if (State != GameState.Playing)
            {
                return;
            }

            // An exception thrown inside a tick handler propagates up through
            // TickSystem.Update, so Unity aborts that Update and the game appears to simply
            // stop dead — with an error that is easy to miss in a busy Console. Catching it
            // here means the game halts LOUDLY, with the exact state it halted in.
            try
            {
                StepBody();
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "[GameManager] Exception during tick. State at failure: " +
                    $"score={score.Score}, foodEaten={foodEaten}, snakeLength={snake.Length}, " +
                    $"head={snake.Head}, normalFood={food.NormalFood}, " +
                    $"bigFood={(food.BigFood.HasValue ? food.BigFood.Value.ToString() : "none")}, " +
                    $"bigFoodTicksLeft={food.BigFoodTicksLeft}, speed={tick.CurrentSpeed:0.00}",
                    this);
                Debug.LogException(exception, this);

                EndRun(false);
            }
        }

        private void StepBody()
        {
            StepResult result = snake.Step(food.NormalFood, food.BigFood);

            switch (result)
            {
                case StepResult.DiedWall:
                case StepResult.DiedSelf:
                    EndRun(false);
                    return;

                case StepResult.AteNormalFood:
                    score.AddNormalFood();
                    foodEaten++;
                    food.OnNormalFoodEaten(snake); // may put a big food on the board

                    if (!food.TrySpawnNormal(snake))
                    {
                        // Nowhere left to put food means the player has filled the grid.
                        // That is a WIN, not a crash.
                        view.Render();
                        EndRun(true);
                        return;
                    }

                    tick.SetFoodEaten(foodEaten);
                    break;

                case StepResult.AteBigFood:
                    score.AddBigFood();

                    // The big food counts as ONE food toward the speed ramp, not three, even
                    // though it is worth three times the points. Score and difficulty stay on
                    // separate axes: the reward for the risky grab is points, not a game that
                    // suddenly lurches faster.
                    foodEaten++;
                    food.ClearBigFood();
                    tick.SetFoodEaten(foodEaten);
                    break;
            }

            food.TickBigFood();
            view.Render();
        }

        // ------------------------------------------------------------------
        // TEMPORARY HUD — DELETE THIS ENTIRE REGION WHEN THE REAL UI LANDS.
        // OnGUI allocates every single frame (strings, GUIContent, layout state) and has no
        // place in a shipped game. It exists only so this phase can be played and judged
        // before there is any real UI to judge it with.
        // ------------------------------------------------------------------
        private GUIStyle hudStyle;

        private void OnGUI()
        {
            if (hudStyle == null)
            {
                hudStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 18,
                    alignment = TextAnchor.UpperLeft
                };
                hudStyle.normal.textColor = Color.white;
            }

            Rect rect = new Rect(16f, 12f, Screen.width - 32f, 100f);

            switch (State)
            {
                case GameState.Menu:
                    GUI.Label(rect, $"ENTER  PLAY        BEST {score.HighScore}", hudStyle);
                    break;

                case GameState.Ready:
                    GUI.Label(rect, "READY", hudStyle);
                    break;

                case GameState.Paused:
                    GUI.Label(rect, "PAUSED     ESC RESUME     Q MENU", hudStyle);
                    break;

                case GameState.GameOver:
                    string headline = BoardCleared ? "BOARD CLEARED" : "GAME OVER";
                    string record = score.IsNewRecord ? "  NEW BEST!" : string.Empty;
                    GUI.Label(rect,
                        $"{headline}     SCORE {score.Score}     BEST {score.HighScore}{record}" +
                        "        ENTER RETRY",
                        hudStyle);
                    break;

                case GameState.Playing:
                    string big = food.BigFood.HasValue ? $"    BIG {food.BigFoodTicksLeft}" : string.Empty;
                    GUI.Label(rect,
                        $"SCORE {score.Score}    BEST {score.HighScore}\n" +
                        $"len {snake.Length}    speed {tick.CurrentSpeed:0.00}    food {foodEaten}    buf {input.Buffered}{big}",
                        hudStyle);
                    break;
            }
        }
    }
}
