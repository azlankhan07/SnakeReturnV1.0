using System.Collections.Generic;
using SnakeReturns.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SnakeReturns.Gameplay
{
    /// <summary>
    /// Reads direction input every frame and buffers it until the next tick consumes it.
    /// </summary>
    /// <remarks>
    /// This class answers "which way does the player want to go next?". It never moves
    /// anything and never decides whether a turn is possible on the board.
    ///
    /// WHY BUFFER AT ALL — this is the whole reason the class exists.
    /// A step lasts 83-167 ms depending on speed. An L-turn (press Right, then Up) is
    /// two presses that frequently land inside a single tick window. If input were read
    /// at tick time we would see only whichever key was pressed last and silently throw
    /// the other away, and the snake would appear to ignore the player on exactly the
    /// manoeuvre that matters most. Buffering keeps both presses and plays them out one
    /// per tick, in the order they arrived.
    /// </remarks>
    public class InputReader : MonoBehaviour
    {
        /// <summary>
        /// Buffer depth. Two is not arbitrary: it holds one committed turn plus one staged
        /// behind it — exactly an L-turn — without executing a move the player pressed three
        /// ticks ago. At depth 3+ the queue outlives the player's intent and the snake starts
        /// to feel like it is playing itself.
        /// </summary>
        private const int MaxBuffered = 2;

        private readonly Queue<Direction> buffer = new Queue<Direction>(MaxBuffered);

        /// <summary>The direction the snake is travelling right now.</summary>
        public Direction Current { get; private set; } = Direction.Right;

        /// <summary>
        /// The last direction accepted into the buffer — the heading the snake WILL be facing
        /// once the buffer drains. New input is validated against this, not against Current.
        /// </summary>
        private Direction lastQueued = Direction.Right;

        public int Buffered => buffer.Count;

        /// <summary>Forces the reader back to a known heading and drops anything pending.</summary>
        public void ResetTo(Direction d)
        {
            Current = d;
            lastQueued = d;
            buffer.Clear();
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            // Each key is tested SEPARATELY, not as an if/else chain, so two keys pressed in
            // the same frame both register and both enter the buffer in event order.
            if (keyboard.wKey.wasPressedThisFrame || keyboard.upArrowKey.wasPressedThisFrame)
            {
                Enqueue(Direction.Up);
            }
            if (keyboard.dKey.wasPressedThisFrame || keyboard.rightArrowKey.wasPressedThisFrame)
            {
                Enqueue(Direction.Right);
            }
            if (keyboard.sKey.wasPressedThisFrame || keyboard.downArrowKey.wasPressedThisFrame)
            {
                Enqueue(Direction.Down);
            }
            if (keyboard.aKey.wasPressedThisFrame || keyboard.leftArrowKey.wasPressedThisFrame)
            {
                Enqueue(Direction.Left);
            }
        }

        /// <summary>
        /// Offers a direction to the buffer. Silently ignored if the buffer is full, if it
        /// repeats the pending heading, or if it would reverse into the snake's own neck.
        /// </summary>
        /// <remarks>
        /// THE VALIDATION MUST COMPARE AGAINST lastQueued, NOT Current.
        /// Facing Right, the player queues Up, then queues Down. Down is not opposite to
        /// RIGHT, so validating against Current would accept it — and two ticks later the
        /// snake turns Up and immediately Down, straight into its own neck, from a move the
        /// player never intended to make. Validating against lastQueued asks the right
        /// question: "is this legal from where the snake will actually be facing?"
        /// </remarks>
        public void Enqueue(Direction d)
        {
            if (buffer.Count >= MaxBuffered)
            {
                return;
            }

            // A repeat of the pending heading is a no-op, so don't burn a buffer slot on it.
            if (d == lastQueued)
            {
                return;
            }

            if (d.IsOpposite(lastQueued))
            {
                return;
            }

            buffer.Enqueue(d);
            lastQueued = d;
        }

        /// <summary>
        /// Takes the next buffered direction, if any. Called exactly once per tick by the snake.
        /// </summary>
        public Direction ConsumeDirection()
        {
            if (buffer.Count > 0)
            {
                Current = buffer.Dequeue();
            }

            // With the buffer empty, the heading the snake will be facing IS the current one,
            // so re-anchor validation to it. Skipping this would keep validating new input
            // against a heading that has already been consumed and left behind.
            if (buffer.Count == 0)
            {
                lastQueued = Current;
            }

            return Current;
        }
    }
}
