using UnityEngine;

namespace SnakeReturns.Core
{
    /// <summary>
    /// Keeps the score for the current run and the best score across runs.
    /// </summary>
    /// <remarks>
    /// This class answers "what is the score?". It is told when food was eaten; it does
    /// not watch the snake, and it does not decide when a run is over.
    /// </remarks>
    public class ScoreManager : MonoBehaviour
    {
        private const string HighScoreKey = "SnakeReturns.HighScore.v1";

        [Header("Points")]
        [SerializeField] private int normalFoodPoints = 10;
        [SerializeField] private int bigFoodPoints = 30;

        /// <summary>Points scored in the current run.</summary>
        public int Score { get; private set; }

        /// <summary>Best score ever recorded. Survives a reset; persisted at game over.</summary>
        public int HighScore { get; private set; }

        /// <summary>True once this run has overtaken the previous best.</summary>
        public bool IsNewRecord { get; private set; }

        private void Awake()
        {
            HighScore = PlayerPrefs.GetInt(HighScoreKey, 0);
        }

        /// <summary>Clears the run. The high score deliberately survives.</summary>
        public void ResetRun()
        {
            Score = 0;
            IsNewRecord = false;
        }

        public void AddNormalFood()
        {
            Add(normalFoodPoints);
        }

        public void AddBigFood()
        {
            Add(bigFoodPoints);
        }

        private void Add(int points)
        {
            Score += points;

            // Promote LIVE, the moment the run passes the old best, so the HUD can show the
            // player overtaking themselves as it happens — that is the whole drama of the
            // high score. Only the disk write waits for game over.
            if (Score > HighScore)
            {
                HighScore = Score;
                IsNewRecord = true;
            }
        }

        /// <summary>
        /// Writes the high score to disk. Call once, at game over.
        /// </summary>
        /// <remarks>
        /// Deliberately not called from Add(): persisting on every food would write to disk
        /// several times a second for a value that only matters when the run ends.
        /// </remarks>
        public void CommitHighScore()
        {
            if (!IsNewRecord)
            {
                return;
            }

            PlayerPrefs.SetInt(HighScoreKey, HighScore);
            PlayerPrefs.Save();
        }

        [ContextMenu("Clear High Score")]
        private void ClearHighScore()
        {
            PlayerPrefs.DeleteKey(HighScoreKey);
            PlayerPrefs.Save();
            HighScore = 0;
            IsNewRecord = false;
            Debug.Log("[ScoreManager] High score cleared.", this);
        }
    }
}
