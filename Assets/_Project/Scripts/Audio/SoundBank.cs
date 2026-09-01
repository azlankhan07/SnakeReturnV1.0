using System.Collections.Generic;
using UnityEngine;

namespace SnakeReturns.Audio
{
    /// <summary>
    /// Every sound the game can make, as data.
    /// </summary>
    /// <remarks>
    /// A ScriptableObject rather than a component, so the whole audio design is one asset you
    /// can duplicate, swap and diff — and so the fields can be public without weakening
    /// anything. This is a data asset; there is no behaviour here to protect.
    /// </remarks>
    [CreateAssetMenu(menuName = "SnakeReturns/Sound Bank", fileName = "SoundBank")]
    public class SoundBank : ScriptableObject
    {
        [System.Serializable]
        public class Entry
        {
            public AudioClip clip;

            [Range(0f, 1f)] public float volume = 1f;

            [Tooltip("Random pitch spread per play. Matters more than it sounds: a sound firing on a fixed cadence turns into a machine-gun drone when every instance is identical.")]
            [Range(0f, 0.5f)] public float pitchJitter = 0.04f;

            [Tooltip("Ignored for one-shots; only the two music entries loop.")]
            public bool loop;

            public bool HasClip => clip != null;
        }

        [Header("Movement")]
        // The step blip fires 6 to 12 times a second. Without jitter that is not a heartbeat,
        // it is a dial tone — which is why this one carries the widest spread in the bank.
        public Entry step = new Entry { volume = 0.18f, pitchJitter = 0.06f };
        public Entry turn = new Entry { volume = 0.25f, pitchJitter = 0.04f };

        [Header("Food")]
        public Entry eatNormal = new Entry { volume = 0.7f, pitchJitter = 0.04f };
        public Entry eatBig = new Entry { volume = 0.85f, pitchJitter = 0.04f };
        public Entry bigFoodAppear = new Entry { volume = 0.6f, pitchJitter = 0.04f };
        public Entry bigFoodWarning = new Entry { volume = 0.5f, pitchJitter = 0.04f };
        public Entry bigFoodLost = new Entry { volume = 0.45f, pitchJitter = 0.04f };

        [Header("Run")]
        public Entry ready = new Entry { volume = 0.6f, pitchJitter = 0.04f };

        // No jitter on these two: they fire once, at a moment that matters, and a wobbling
        // pitch on a death sting reads as a bug rather than as variety.
        public Entry death = new Entry { volume = 0.9f, pitchJitter = 0f };
        public Entry newHighScore = new Entry { volume = 0.8f, pitchJitter = 0f };

        [Header("Menu")]
        public Entry menuMove = new Entry { volume = 0.5f, pitchJitter = 0.04f };
        public Entry menuSelect = new Entry { volume = 0.7f, pitchJitter = 0.04f };
        public Entry pause = new Entry { volume = 0.6f, pitchJitter = 0.04f };
        public Entry resume = new Entry { volume = 0.6f, pitchJitter = 0.04f };

        [Header("Music — the only entries that loop")]
        public Entry musicMenu = new Entry { volume = 0.35f, pitchJitter = 0f, loop = true };
        public Entry musicGame = new Entry { volume = 0.30f, pitchJitter = 0f, loop = true };

        /// <summary>Comma-separated list of entries with no clip, or "" when the bank is complete.</summary>
        public string Missing()
        {
            List<string> missing = new List<string>(16);

            Check(step, nameof(step), missing);
            Check(turn, nameof(turn), missing);
            Check(eatNormal, nameof(eatNormal), missing);
            Check(eatBig, nameof(eatBig), missing);
            Check(bigFoodAppear, nameof(bigFoodAppear), missing);
            Check(bigFoodWarning, nameof(bigFoodWarning), missing);
            Check(bigFoodLost, nameof(bigFoodLost), missing);
            Check(ready, nameof(ready), missing);
            Check(death, nameof(death), missing);
            Check(newHighScore, nameof(newHighScore), missing);
            Check(menuMove, nameof(menuMove), missing);
            Check(menuSelect, nameof(menuSelect), missing);
            Check(pause, nameof(pause), missing);
            Check(resume, nameof(resume), missing);
            Check(musicMenu, nameof(musicMenu), missing);
            Check(musicGame, nameof(musicGame), missing);

            return missing.Count == 0 ? string.Empty : string.Join(", ", missing);
        }

        private static void Check(Entry entry, string entryName, List<string> missing)
        {
            if (entry == null || !entry.HasClip)
            {
                missing.Add(entryName);
            }
        }
    }
}
