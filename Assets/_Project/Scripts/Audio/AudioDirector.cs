using SnakeReturns.Core;
using SnakeReturns.Gameplay;
using UnityEngine;

namespace SnakeReturns.Audio
{
    /// <summary>
    /// Watches the game and makes noise about it.
    /// </summary>
    /// <remarks>
    /// A PURE OBSERVER. Nothing here calls into GameManager, and GameManager does not know this
    /// component exists. Deleting it must not be able to break the game — the run keeps running
    /// in silence. That is why every sound is derived from state the game already publishes
    /// (a score jump, a big food appearing) rather than from a hook the game had to grow.
    /// </remarks>
    public class AudioDirector : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private SoundBank bank;
        [SerializeField] private GameManager game;
        [SerializeField] private TickSystem tick;
        [SerializeField] private ScoreManager score;
        [SerializeField] private FoodSpawner food;

        [Tooltip("Used only to notice a direction change, so the turn sound has something to fire on.")]
        [SerializeField] private InputReader input;

        [Header("Levels — safe to drag while the game is running")]
        [Range(0f, 1f)] [SerializeField] private float masterVolume = 1f;
        [Range(0f, 1f)] [SerializeField] private float sfxVolume = 1f;
        [Range(0f, 1f)] [SerializeField] private float musicVolume = 0.5f;
        [SerializeField] private bool muteAll = false;

        [Header("Music")]
        [Tooltip("One track for the whole game — the menu and the run share it, so it never restarts on a state change.")]
        [SerializeField] private AudioClip music;

        [Range(0f, 10f)] [SerializeField] private float musicFadeIn = 2.5f;

        [Header("Voices")]
        [Tooltip("Simultaneous one-shots. Beyond this the oldest is reused, so a burst can never spawn an AudioSource on a tick.")]
        [SerializeField] private int sfxVoices = 8;

        private AudioSource musicSource;
        private AudioSource[] voices;
        private int nextVoice;
        private float fadeTimer;
        private bool fading;

        // The previous tick's snapshot. Everything this class plays is a DIFFERENCE against
        // these, which is what lets it stay an observer instead of needing events of its own.
        private int lastScore;
        private bool lastBigAlive;
        private bool lastWarning;
        private bool started;

        private Direction lastDirection;
        private bool turnKnown;

        private float MusicLevel => muteAll ? 0f : masterVolume * musicVolume;
        private float SfxLevel => muteAll ? 0f : masterVolume * sfxVolume;

        private void Reset()
        {
            AutoWire();
        }

        private void OnEnable()
        {
            AutoWire();
            BuildSources();

            if (game != null)
            {
                game.OnStateChanged += OnState;
            }

            if (tick != null)
            {
                tick.OnTick += OnTick;
            }

            if (score != null)
            {
                lastScore = score.Score;
            }

            StartMusic();
        }

        private void OnDisable()
        {
            if (game != null)
            {
                game.OnStateChanged -= OnState;
            }

            if (tick != null)
            {
                tick.OnTick -= OnTick;
            }
        }

        private void AutoWire()
        {
            Wiring.Resolve(this, ref game, nameof(game));
            Wiring.Resolve(this, ref tick, nameof(tick));
            Wiring.Resolve(this, ref score, nameof(score));
            Wiring.Resolve(this, ref food, nameof(food));
            Wiring.Resolve(this, ref input, nameof(input));
        }

        private void BuildSources()
        {
            if (started)
            {
                return;
            }

            started = true;

            musicSource = gameObject.AddComponent<AudioSource>();
            musicSource.playOnAwake = false;
            musicSource.loop = true;
            musicSource.spatialBlend = 0f;

            int count = Mathf.Max(1, sfxVoices);
            voices = new AudioSource[count];

            for (int i = 0; i < count; i++)
            {
                AudioSource voice = gameObject.AddComponent<AudioSource>();
                voice.playOnAwake = false;
                voice.loop = false;
                voice.spatialBlend = 0f;
                voices[i] = voice;
            }
        }

        /// <summary>Plays a one-shot. A null or clipless entry does nothing at all.</summary>
        public void Play(SoundBank.Entry e)
        {
            if (e == null || !e.HasClip || voices == null || voices.Length == 0)
            {
                return;
            }

            // Round-robin. Past the end of the pool the oldest voice is simply reused, so no
            // burst of sound can ever allocate an AudioSource mid-tick.
            AudioSource voice = voices[nextVoice];
            nextVoice = (nextVoice + 1) % voices.Length;

            voice.clip = e.clip;
            voice.volume = e.volume * SfxLevel;
            voice.pitch = 1f + Random.Range(-e.pitchJitter, e.pitchJitter);
            voice.Play();
        }

        private AudioClip MusicClip()
        {
            if (music != null)
            {
                return music;
            }

            if (bank != null && bank.musicMenu != null && bank.musicMenu.HasClip)
            {
                return bank.musicMenu.clip;
            }

            if (bank != null && bank.musicGame != null && bank.musicGame.HasClip)
            {
                return bank.musicGame.clip;
            }

            return null;
        }

        private void StartMusic()
        {
            if (musicSource == null)
            {
                return;
            }

            AudioClip clip = MusicClip();
            if (clip == null)
            {
                return;
            }

            musicSource.clip = clip;
            musicSource.loop = true;
            musicSource.volume = 0f;
            musicSource.Play();

            fadeTimer = 0f;
            fading = musicFadeIn > 0f;

            if (!fading)
            {
                musicSource.volume = MusicLevel;
            }
        }

        private void Update()
        {
            if (musicSource == null)
            {
                return;
            }

            if (fading)
            {
                fadeTimer += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(fadeTimer / Mathf.Max(0.0001f, musicFadeIn));
                musicSource.volume = MusicLevel * t;

                if (t >= 1f)
                {
                    fading = false;
                }

                return;
            }

            // Tracked every frame so the level sliders can be dragged live during play.
            musicSource.volume = MusicLevel;
        }

        private void OnState(GameState next)
        {
            if (bank == null)
            {
                return;
            }

            switch (next)
            {
                case GameState.Menu:
                    Play(bank.menuMove);
                    break;

                case GameState.Ready:
                    Play(bank.ready);
                    // A fresh run, so the big-food warning is armed again.
                    lastWarning = false;
                    lastBigAlive = false;
                    turnKnown = false;
                    if (score != null)
                    {
                        lastScore = score.Score;
                    }
                    break;

                case GameState.Playing:
                    Play(bank.menuSelect);
                    break;

                case GameState.Paused:
                    Play(bank.pause);
                    break;

                case GameState.GameOver:
                    Play(bank.death);
                    if (score != null && score.IsNewRecord)
                    {
                        Play(bank.newHighScore);
                    }
                    break;
            }

            // Leaving Paused for anything at all is a resume.
            if (previousState == GameState.Paused && next != GameState.Paused)
            {
                Play(bank.resume);
            }

            previousState = next;
        }

        private GameState previousState = GameState.Boot;

        private void OnTick()
        {
            if (bank == null)
            {
                return;
            }

            bool playing = game != null && game.State == GameState.Playing;

            if (playing)
            {
                Play(bank.step);
            }

            if (input != null)
            {
                Direction direction = input.Current;

                // turnKnown guards the very first tick, which would otherwise always sound like
                // a turn because there is nothing to compare against yet.
                if (turnKnown && playing && direction != lastDirection)
                {
                    Play(bank.turn);
                }

                lastDirection = direction;
                turnKnown = true;
            }

            if (score == null || food == null)
            {
                return;
            }

            bool bigAlive = food.BigFood.HasValue;
            bool bigDisappeared = lastBigAlive && !bigAlive;
            int current = score.Score;

            if (current != lastScore)
            {
                // A score jump on the same tick the big food vanished IS the big food being
                // eaten — which is also why bigFoodLost below only fires when the score did
                // NOT move. No new hook into the game was needed for either.
                Play(bigDisappeared ? bank.eatBig : bank.eatNormal);
            }
            else if (bigDisappeared)
            {
                Play(bank.bigFoodLost);
            }

            if (!lastBigAlive && bigAlive)
            {
                Play(bank.bigFoodAppear);
                lastWarning = false;
            }

            // Once per big food, at the third that is left.
            if (bigAlive && !lastWarning && food.BigFoodTicksLeft < food.BigFoodLifetimeTicks / 3f)
            {
                Play(bank.bigFoodWarning);
                lastWarning = true;
            }

            lastScore = current;
            lastBigAlive = bigAlive;
        }

        // ------------------------------------------------------------------
        // Inspector auditions. One per entry, so a clip can be judged in place without
        // playing the game up to the moment that triggers it.
        // ------------------------------------------------------------------
        [ContextMenu("Test/Step")] private void TestStep() => Audition(bank?.step);
        [ContextMenu("Test/Turn")] private void TestTurn() => Audition(bank?.turn);
        [ContextMenu("Test/Eat normal")] private void TestEatNormal() => Audition(bank?.eatNormal);
        [ContextMenu("Test/Eat big")] private void TestEatBig() => Audition(bank?.eatBig);
        [ContextMenu("Test/Big food appear")] private void TestBigAppear() => Audition(bank?.bigFoodAppear);
        [ContextMenu("Test/Big food warning")] private void TestBigWarning() => Audition(bank?.bigFoodWarning);
        [ContextMenu("Test/Big food lost")] private void TestBigLost() => Audition(bank?.bigFoodLost);
        [ContextMenu("Test/Ready")] private void TestReady() => Audition(bank?.ready);
        [ContextMenu("Test/Death")] private void TestDeath() => Audition(bank?.death);
        [ContextMenu("Test/New high score")] private void TestNewHighScore() => Audition(bank?.newHighScore);
        [ContextMenu("Test/Menu move")] private void TestMenuMove() => Audition(bank?.menuMove);
        [ContextMenu("Test/Menu select")] private void TestMenuSelect() => Audition(bank?.menuSelect);
        [ContextMenu("Test/Pause")] private void TestPause() => Audition(bank?.pause);
        [ContextMenu("Test/Resume")] private void TestResume() => Audition(bank?.resume);

        [ContextMenu("Test/Restart music and hear the fade")]
        private void TestMusic()
        {
            if (musicSource != null)
            {
                musicSource.Stop();
            }

            BuildSources();
            StartMusic();
        }

        private void Audition(SoundBank.Entry e)
        {
            BuildSources();
            Play(e);
        }
    }
}
