using System;
using UnityEngine;

namespace SnakeReturns.Core
{
    /// <summary>
    /// The game's heartbeat. Nothing in this game moves in Update() — everything that
    /// advances the world listens to <see cref="OnTick"/> and steps exactly once per tick.
    /// </summary>
    /// <remarks>
    /// This class answers "has another step elapsed?". It does not know what a step means
    /// to anyone, and it never decides anything about the game.
    /// </remarks>
    public class TickSystem : MonoBehaviour
    {
        /// <summary>
        /// Hard cap on steps executed in a single frame. See the comment in Update() —
        /// this is what stops an alt-tab from teleporting the snake into a wall.
        /// </summary>
        private const int MaxStepsPerFrame = 4;

        [Header("Speed")]
        [Tooltip("Steps per second at the start of a run.")]
        [SerializeField] private float startSpeed = 6f;

        [Tooltip("Steps per second the snake will never exceed, however much it eats.")]
        [SerializeField] private float maxSpeed = 12f;

        [Tooltip("Steps per second added at each speed-up.")]
        [SerializeField] private float speedIncrement = 0.25f;

        [Tooltip("Foods eaten per speed-up.")]
        [SerializeField] private int foodsPerIncrement = 2;

        [Header("Read-only (runtime display)")]
        [SerializeField] private float currentSpeed;
        [SerializeField] private float stepInterval;

        /// <summary>Raised once per step. Listeners must not assume any ordering between themselves.</summary>
        public event Action OnTick;

        private float timer;

        public bool IsRunning { get; private set; }
        public int TickCount { get; private set; }
        public float CurrentSpeed => currentSpeed;
        public float StepInterval => stepInterval;

        /// <summary>
        /// How far the current step has progressed, 0..1. For view-side interpolation —
        /// the logic itself is strictly discrete and never asks this.
        /// </summary>
        public float StepProgress => stepInterval <= 0f ? 0f : Mathf.Clamp01(timer / stepInterval);

        private void Awake()
        {
            ResetSpeed();
        }

        /// <summary>Starts or resumes ticking.</summary>
        public void Run()
        {
            IsRunning = true;
        }

        /// <summary>
        /// Suspends ticking.
        /// </summary>
        /// <remarks>
        /// This must NEVER touch Time.timeScale. timeScale is a global switch that also
        /// freezes UI animation, particle systems and audio scheduling — pausing the game
        /// would silently pause the pause menu itself. Pausing here means "stop raising
        /// OnTick"; the rest of the engine keeps running normally.
        /// </remarks>
        public void Pause()
        {
            IsRunning = false;
        }

        /// <summary>Returns to the starting speed and clears the accumulator and tick count.</summary>
        public void ResetSpeed()
        {
            currentSpeed = startSpeed;
            stepInterval = currentSpeed > 0f ? 1f / currentSpeed : 0f;
            timer = 0f;
            TickCount = 0;
        }

        /// <summary>
        /// Recomputes the speed for a given number of foods eaten this run.
        /// </summary>
        /// <remarks>
        /// The division below is INTEGER division on purpose. It turns a continuous count
        /// into a staircase: nothing happens, nothing happens, then the whole game gets
        /// faster in one audible step. A float divide would ramp smoothly and the player
        /// would never notice they were speeding up — the felt moment is the point.
        /// </remarks>
        public void SetFoodEaten(int foodEaten)
        {
            int steps = foodsPerIncrement > 0 ? foodEaten / foodsPerIncrement : 0;

            currentSpeed = Mathf.Min(startSpeed + steps * speedIncrement, maxSpeed);
            stepInterval = currentSpeed > 0f ? 1f / currentSpeed : 0f;
        }

        private void Update()
        {
            if (!IsRunning || stepInterval <= 0f)
            {
                return;
            }

            timer += Time.deltaTime;

            int stepsThisFrame = 0;
            while (timer >= stepInterval && stepsThisFrame < MaxStepsPerFrame)
            {
                // SUBTRACT, never timer = 0. The remainder is real elapsed time that belongs
                // to the next step. Zeroing it throws that time away every single step, which
                // makes the game run a couple of percent slow — and the error compounds, so a
                // long game drifts further and further behind where it should be.
                timer -= stepInterval;

                stepsThisFrame++;
                TickCount++;

                OnTick?.Invoke();

                // A listener may have ended the game during the callback (the snake hit a wall
                // on this very step). We must not keep stepping a dead snake, so bail out of
                // the loop immediately rather than finishing the backlog.
                if (!IsRunning)
                {
                    return;
                }
            }

            // MaxStepsPerFrame: after an editor recompile, a breakpoint or an alt-tab,
            // Time.deltaTime can arrive as several seconds. An uncapped loop would run
            // hundreds of steps in one frame and teleport the snake across the board into a
            // wall. Past the cap we DROP the backlog instead of fast-forwarding through it:
            // losing a few steps is invisible, dying because you tabbed out is not.
            if (stepsThisFrame >= MaxStepsPerFrame && timer >= stepInterval)
            {
                timer = 0f;
            }
        }
    }
}
