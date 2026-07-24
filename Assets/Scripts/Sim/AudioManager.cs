using UnityEngine;

namespace Trickshot
{
    /// <summary>
    /// Single persistent audio hub. Self-installs on load (like GameBootstrap) and survives
    /// every screen/match teardown, so the menu-music loop is never restarted as the player
    /// walks between menus and the crowd bed is owned in one place.
    ///
    /// Clips load lazily from Resources/Audio (no scene wiring), matching Make.Hair's pattern,
    /// with a graceful null-skip if a clip is missing from the build.
    ///
    /// Four mixer channels, each a local 0..1 slider persisted in PlayerPrefs (per player, not
    /// networked): Master, Music, Crowd (ambient bed + lively swells/stingers), SFX (cheer,
    /// applause, boos). Effective volume of any source = channel * master.
    ///
    /// Loops live on dedicated sources so they can be stopped/duck-cut individually:
    ///   _music     - menu music (2D loop)
    ///   _ambient[2]- two crowd beds for a live match, swelling in antiphase (2D loops)
    ///   _lively[2] - two swell/stinger sources; a new swell plays on the free one while the old
    ///                one fades out, so livelies cross-fade instead of hard-cutting
    ///   _event     - fire-and-forget one-shots (cheer/applause/boos) via PlayOneShot (can overlay)
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        // ---- channels ----
        public enum Channel { Master, Music, Crowd, Sfx }

        static readonly string[] PrefKey = { "vol_master", "vol_music", "vol_crowd", "vol_sfx" };
        static readonly float[]  Default  = { 1f,           0.6f,        0.7f,        0.9f      };
        readonly float[] _vol = new float[4];

        public float GetVolume(Channel c) => _vol[(int)c];
        public void SetVolume(Channel c, float v)
        {
            v = Mathf.Clamp01(v);
            _vol[(int)c] = v;
            PlayerPrefs.SetFloat(PrefKey[(int)c], v);
            ApplyLoopVolumes();   // live-update the running loops so the slider is audible immediately
        }

        // ---- singleton / install ----
        public static AudioManager Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoStart()
        {
            if (Instance != null) return;
            var go = new GameObject("AudioManager");
            go.AddComponent<AudioManager>();
        }

        AudioSource _music, _event;
        // Two ambient beds (crowd_ambient + crowd_ambient_2) that swell in antiphase: as one rises
        // the other falls, so there's always a living bed but the texture keeps shifting. Their
        // volumes sum to ~Chan(Crowd) and each peaks at Chan(Crowd) (same volume as the other).
        readonly AudioSource[] _ambient = new AudioSource[2];
        const float AmbientSwellPeriod = 10f;   // seconds for a full swell in-and-out cycle
        float _ambientPhase;
        readonly AudioSource[] _lively = new AudioSource[2];   // cross-fading swell/stinger pair
        int _livelyCur;                                        // index of the most-recently started lively

        // 3D positional pool for ball-kick thuds. Several so rapid successive kicks don't cut each
        // other; round-robin. The one AudioListener rides the main camera (which follows the local
        // player), so distance attenuation is naturally relative to THIS player - the 10 m rolloff
        // means only kicks within ~10 m (plus the player the ball hits, who is right on top of it)
        // are audible, exactly per spec.
        const int KickVoices = 4;
        readonly AudioSource[] _kick = new AudioSource[KickVoices];
        int _kickNext;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            for (int i = 0; i < 4; i++) _vol[i] = PlayerPrefs.GetFloat(PrefKey[i], Default[i]);

            _music      = MakeSource("Music",    loop: true);
            _ambient[0] = MakeSource("Ambient0", loop: true);
            _ambient[1] = MakeSource("Ambient1", loop: true);
            _lively[0]  = MakeSource("Lively0",  loop: false);
            _lively[1]  = MakeSource("Lively1",  loop: false);
            _event      = MakeSource("Event",    loop: false);
            for (int i = 0; i < KickVoices; i++) _kick[i] = MakeSource3D("Kick" + i);
        }

        AudioSource MakeSource(string name, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = loop;
            s.spatialBlend = 0f;   // 2D
            return s;
        }

        // A fully 3D one-shot source with a hard ~10 m linear cutoff. Not parented to the manager:
        // it's repositioned to the kick point each time it plays.
        AudioSource MakeSource3D(string name)
        {
            var go = new GameObject(name);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = false;
            s.spatialBlend = 1f;                       // fully 3D
            s.rolloffMode = AudioRolloffMode.Linear;   // clean 0-at-max falloff
            s.minDistance = 1.5f;                      // full volume within ~arm's reach
            s.maxDistance = 10f;                       // silent beyond 10 m (the spec)
            s.dopplerLevel = 0f;                       // no pitch shift on fast play
            return s;
        }

        // ---- clip cache (Resources/Audio/<name>) ----
        readonly System.Collections.Generic.Dictionary<string, AudioClip> _clips = new();
        AudioClip Clip(string name)
        {
            if (_clips.TryGetValue(name, out var c)) return c;
            c = Resources.Load<AudioClip>("Audio/" + name);   // null if absent from build
            _clips[name] = c;
            return c;
        }

        float Chan(Channel c) => _vol[(int)c] * _vol[(int)Channel.Master];

        // Crowd REACTIONS (cheer / applause / boos) are crowd sounds, so they play at the crowd
        // volume - the same level as the ambient bed and the livelies by default, not the louder
        // Sfx channel (which is reserved for the whistle + ball kick). CrowdReactionGain is a hand
        // tuning knob if a source clip is mastered hotter/quieter than the ambient loop.
        const float CrowdReactionGain = 1f;
        float CrowdReactionVol() => Chan(Channel.Crowd) * CrowdReactionGain;

        // ===================================================================== menu music
        // Idempotent: calling while it's already playing this clip is a no-op, so navigating
        // between menus never restarts the track. BuildMode stops it; TearDownMatch resumes it.
        public void PlayMenuMusic()
        {
            var clip = Clip("menu_music");
            if (clip == null) return;
            if (_music.isPlaying && _music.clip == clip) return;
            _music.clip = clip;
            _music.volume = Chan(Channel.Music);
            _music.Play();
        }

        public void StopMenuMusic() => _music.Stop();

        // ===================================================================== match beds
        GameMode _mode;
        bool _matchActive;
        // Swell timer (action modes only). FreeKick/SetPieces use per-shooter streak stingers
        // instead and leave this disabled.
        bool  _swellEnabled;
        float _swellTimer;
        bool  _livelyToggle;      // periodic-swell alternation (lively_1 <-> lively_2)
        readonly System.Collections.Generic.Dictionary<int, int> _streak = new();      // per-shooter GOAL streak
        readonly System.Collections.Generic.Dictionary<int, int> _missStreak = new();  // per-shooter MISS streak
        int _scrimHome, _scrimAway;   // last-seen scrimmage score, to detect a new goal + the trailing margin

        // Called from GameBootstrap.BuildMode / StartNetworkedMatch once the mode is known.
        public void BeginMatch(GameMode mode)
        {
            _mode = mode;
            _matchActive = true;
            _streak.Clear();
            _missStreak.Clear();
            _scrimHome = _scrimAway = 0;
            _livelyToggle = false;
            _fadeOld = null;
            _ambientPhase = 0f;

            StopMenuMusic();

            // Two beds, started together; Update swells them in antiphase.
            StartAmbient(0, "crowd_ambient");
            StartAmbient(1, "crowd_ambient_2");
            ApplyAmbientSwell();   // set the initial split immediately (bed 0 full, bed 1 silent)

            // Set pieces drive the livelies off scoring streaks (no timer); everything else swells
            // the crowd on a clock.
            _swellEnabled = !(mode == GameMode.FreeKick || mode == GameMode.SetPieces);
            _swellTimer = NextSwellInterval();
        }

        public void EndMatch()
        {
            _matchActive = false;
            _swellEnabled = false;
            _ambient[0].Stop();
            _ambient[1].Stop();
            _lively[0].Stop();
            _lively[1].Stop();
            _fadeOld = null;
        }

        void StartAmbient(int i, string clipName)
        {
            var clip = Clip(clipName);
            if (clip == null) return;
            _ambient[i].clip = clip;
            _ambient[i].volume = 0f;   // set by ApplyAmbientSwell
            _ambient[i].Play();
        }

        // Antiphase swell: bed 0 = cos, bed 1 = its complement, both scaled to Chan(Crowd). As one
        // rises the other falls; each peaks at the full crowd volume and they never both drop out.
        void ApplyAmbientSwell()
        {
            float crowd = Chan(Channel.Crowd);
            float t = 0.5f * (1f + Mathf.Cos(_ambientPhase));   // 1 -> 0 -> 1 over a cycle
            if (_ambient[0] != null) _ambient[0].volume = crowd * t;
            if (_ambient[1] != null) _ambient[1].volume = crowd * (1f - t);
        }

        // Scrimmage 15-20s; every other action mode (Striker/GK/Accuracy/TimeTrial/Freeplay) 60-90s.
        float NextSwellInterval() =>
            _mode == GameMode.Scrimmage ? Random.Range(15f, 20f) : Random.Range(60f, 90f);

        void Update()
        {
            // Cross-fade: fade the previous lively out over ~0.6s whenever a newer one is playing.
            if (_fadeOld != null)
            {
                _fadeOld.volume -= Chan(Channel.Crowd) * Time.unscaledDeltaTime / 0.6f;
                if (_fadeOld.volume <= 0.001f) { _fadeOld.Stop(); _fadeOld = null; }
            }

            if (!_matchActive || PauseMenu.Paused) return;

            // Antiphase ambient swell runs the whole match (independent of the lively timer).
            _ambientPhase += Time.unscaledDeltaTime * (2f * Mathf.PI / AmbientSwellPeriod);
            if (_ambientPhase > 2f * Mathf.PI) _ambientPhase -= 2f * Mathf.PI;
            ApplyAmbientSwell();

            if (!_swellEnabled) return;
            _swellTimer -= Time.unscaledDeltaTime;
            if (_swellTimer <= 0f)
            {
                PlayLivelyAlternating();
                _swellTimer = NextSwellInterval();
            }
        }

        // ===================================================================== livelies
        // A lively swell rides ON TOP of the ambient bed. Two sources cross-fade: the new swell
        // starts at full on the free source while the previously-playing one fades out (driven in
        // Update). So a fresh swell never hard-cuts the tail of the last one.
        AudioSource _fadeOld;   // the lively currently fading out (null = none)
        void PlayLively(bool second)
        {
            var clip = Clip(second ? "crowd_ambient_lively_2" : "crowd_ambient_lively");
            if (clip == null) return;

            int next = 1 - _livelyCur;               // start the new swell on the OTHER source
            var cur = _lively[_livelyCur];           // the one currently sounding (if any)

            // Retire any in-progress fade, then hand the currently-playing source to the fader so it
            // ramps down in Update while the new swell comes in at full on `next`.
            if (_fadeOld != null && _fadeOld != cur) _fadeOld.Stop();
            _fadeOld = (cur != null && cur.isPlaying && cur != _lively[next]) ? cur : null;

            // Make sure the source we're about to (re)use isn't the fading one and is silent-free.
            _lively[next].Stop();
            _lively[next].clip = clip;
            _lively[next].volume = Chan(Channel.Crowd);
            _lively[next].Play();
            _livelyCur = next;
        }

        void PlayLivelyAlternating()
        {
            PlayLively(_livelyToggle);
            _livelyToggle = !_livelyToggle;
        }

        // Per-shooter scoring streak (FreeKick single shooter -> key 0; SetPieces -> active slot).
        // A goal fires the full cheer+applause celebration AND, from the 2nd consecutive goal on,
        // layers a lively swell (alternating). Only this shooter's own non-goal resets the goal run.
        public void OnSetPieceGoal(int shooterKey)
        {
            _missStreak[shooterKey] = 0;             // a goal breaks any miss run
            _streak.TryGetValue(shooterKey, out int s);
            s++;
            _streak[shooterKey] = s;
            PlayGoalCelebration(cutLively: false);   // cheer + applause on EVERY goal (keep the swell)
            if (s >= 2) PlayLively(second: (s % 2) == 1);
        }

        // A missed set-piece resets the goal run and grows this shooter's miss run. From the 2nd
        // consecutive miss on, the crowd boos (and keeps booing each further consecutive miss).
        public void OnSetPieceMiss(int shooterKey)
        {
            _streak[shooterKey] = 0;
            _missStreak.TryGetValue(shooterKey, out int m);
            m++;
            _missStreak[shooterKey] = m;
            if (m >= 2) PlayBoos();
        }

        // Scrimmage goal: cheer + applause on every goal; if a team is now 2+ behind (and the
        // margin just changed), overlay boos for the trailing crowd. home/away are the new totals.
        public void OnScrimmageGoal(int home, int away)
        {
            if (home == _scrimHome && away == _scrimAway) return;   // no actual change
            _scrimHome = home; _scrimAway = away;
            PlayGoalCelebration();
            if (Mathf.Abs(home - away) >= 2) PlayBoos();
        }

        // ===================================================================== events
        // Goal celebration: cheer + applause overlaid. By default cuts any lively swell (a goal in
        // open play ends the pre-goal buzz); set cutLively:false to LAYER over a swell (set pieces,
        // where the streak swell and the goal cheer coexist).
        public void PlayGoalCelebration(bool cutLively = true)
        {
            if (cutLively) FadeLivelies();
            float v = CrowdReactionVol();
            var cheer = Clip("goal_cheer");
            var clap  = Clip("applause");
            if (cheer != null) _event.PlayOneShot(cheer, v);
            if (clap  != null) _event.PlayOneShot(clap,  v);
        }

        // Applause alone (no cheer) - used at the end of a set-piece match.
        public void PlayApplauseOnly()
        {
            var clap = Clip("applause");
            if (clap != null) _event.PlayOneShot(clap, CrowdReactionVol());
        }

        // Unconditional boos (streak-gated by the callers above).
        public void PlayBoos()
        {
            var clip = Clip("boos");
            if (clip != null) _event.PlayOneShot(clip, CrowdReactionVol());
        }

        // Boos after a miss, but only occasionally (~1 in 5-6). Independent roll per miss. Used by
        // Striker (SP + MP), which has no per-shooter streak concept.
        public void PlayMissBoosMaybe()
        {
            if (Random.value > 0.18f) return;   // ~1 in 5.5
            PlayBoos();
        }

        // ===================================================================== ball kick (3D)
        // Fires whenever the ball strikes a player body. 3D + 10 m rolloff, so only players within
        // ~10 m (and the one the ball hit) hear it. Round-robins the voice pool so rapid touches
        // don't cut each other. Position comes from the collision contact point.
        public void PlayBallKick(Vector3 worldPos)
        {
            var clip = Clip("ball_kick");
            if (clip == null) return;
            var s = _kick[_kickNext];
            _kickNext = (_kickNext + 1) % KickVoices;
            s.transform.position = worldPos;
            s.clip = clip;
            s.volume = Chan(Channel.Sfx);
            s.Play();
        }

        // ===================================================================== whistles
        // A single referee whistle (2D - the ref's call is heard equally by everyone).
        public void PlayWhistle()
        {
            var clip = Clip("whistle");
            if (clip != null) _event.PlayOneShot(clip, Chan(Channel.Sfx));
        }

        // Three whistles in quick succession (end of a scrimmage).
        public void PlayWhistleTriple() => StartCoroutine(WhistleTriple());
        System.Collections.IEnumerator WhistleTriple()
        {
            for (int i = 0; i < 3; i++)
            {
                PlayWhistle();
                if (i < 2) yield return new WaitForSecondsRealtime(0.22f);
            }
        }

        // Fade out whichever livelies are currently sounding (used when a goal cheer takes over).
        void FadeLivelies()
        {
            var playing = _lively[_livelyCur].isPlaying ? _lively[_livelyCur] : null;
            // Retire any existing fade, then fade the current one.
            if (_fadeOld != null && _fadeOld != playing) _fadeOld.Stop();
            _fadeOld = playing;
            // Stop the non-current source outright (it's already the older layer).
            int other = 1 - _livelyCur;
            if (_lively[other] != _fadeOld) _lively[other].Stop();
        }

        // ---- live volume refresh (slider moved mid-match) ----
        void ApplyLoopVolumes()
        {
            if (_music != null && _music.isPlaying) _music.volume = Chan(Channel.Music);
            ApplyAmbientSwell();   // re-splits both beds at the new crowd volume (holds current phase)
            // Refresh the actively-playing lively (the fading one keeps ramping down in Update).
            var cur = _lively[_livelyCur];
            if (cur != null && cur.isPlaying && cur != _fadeOld) cur.volume = Chan(Channel.Crowd);
        }
    }
}
