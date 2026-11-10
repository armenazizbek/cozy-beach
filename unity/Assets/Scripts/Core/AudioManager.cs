using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;

// Port of src/core/AudioManager.ts. The Web Audio graph becomes a small synthesiser on Unity's audio thread (SynthHost below):
// oscillators and filtered noise with the same envelopes, filters and levels; the radio plays the MP3s on two AudioSources that
// fade into each other.

/// <summary>Zero-asset synthesiser: every sound effect is generated at runtime.</summary>
public class AudioManager
{
    private SynthHost host;
    private bool muted = false;
    private double masterVol = 1, musicScale = 1, ambScale = 1;
    private double lastOcean = 0.06;
    private double rainLevel = 0;
    private double lastBrush = -1;
    private bool engineOn;
    /// <summary>the detector may only make sound while it is the tool in hand</summary>
    public bool detectorEnabled = false;
    private bool musicOn = false;
    private double musicLevel = 0, musicDuck = 1;

    public AudioManager() { init(); }

    public bool ready => host != null;

    private void init()
    {
        var go = new GameObject("Audio");
        host = go.AddComponent<SynthHost>();
        host.owner = this;
        host.master.Set(muted ? 0 : 0.8 * masterVol, 0);
        host.ocean.Set(0.06 * ambScale, 0);
        startMusicSlots();
    }

    private double now => host.Now;

    // ------------------------------------------------------------------ radio music

    public bool radioOn => musicOn;

    public void setRadio(bool on)
    {
        musicOn = on;
        refreshMusic();
    }

    private bool menuOn = false;

    /// <summary>The title screen plays the main theme whether or not the radio is on.</summary>
    public void setMenu(bool on)
    {
        if (on == menuOn) return;
        menuOn = on;
        refreshMusic();
    }

    private bool endingOn = false;

    /// <summary>The ending plays its own music, whether or not the radio is on.</summary>
    public void setEnding(bool on)
    {
        endingOn = on;
        refreshMusic();
        setMusicLevel(on ? 1 : musicLevel, 1);
    }

    private bool playing => musicOn || menuOn || endingOn;

    private void refreshMusic()
    {
        if (host == null) return;
        if (playing) runMusic();
        else stopMusic();
    }

    /// <summary>Master level of the radio, 0..1 (the pause menu turns it down).</summary>
    public void setMusicLevel(double level, double duck = 1)
    {
        musicLevel = level;
        musicDuck = duck;
        if (host == null) return;
        host.musicGain.Target(playing ? level * duck * 0.8 * musicScale : 0, 0.25);
    }

    public class Station
    {
        public string name;
        public int file;
    }

    /// <summary>The stations: the first follows the player, the rest are fixed. `file` indexes RADIO_FILES.</summary>
    public static readonly Station[] STATIONS =
    {
        new Station { name = "Wherever you are", file = -1 },
        new Station { name = "Beach Day", file = 1 },
        new Station { name = "Camp Night", file = 2 },
        new Station { name = "Under the Water", file = 3 },
        new Station { name = "Under the Dunes", file = 4 },
        new Station { name = "Cozy Treasure Hunter", file = 0 },
    };

    private static readonly string[] RADIO_FILES = { "01-main-theme", "02-beach-day", "03-camp-night", "04-underwater", "05-underground", "06-ending" };

    /// <summary>0 = follow the place the player is in, 1.. = a fixed tune</summary>
    private int trackIdx = 0;
    private int place = 1;
    private int activeSlot = 0;

    public string trackName
    {
        get
        {
            var st = STATIONS[trackIdx];
            return st.file < 0 ? $"{STATIONS[place].name} (follows you)" : st.name;
        }
    }

    public int trackNumber => trackIdx;
    public int trackCount => STATIONS.Length;

    public void setTrack(int i)
    {
        int n = STATIONS.Length;
        trackIdx = ((i % n) + n) % n;
        syncTrack();
    }

    /// <summary>Tells the radio where the player is: 1 beach by day, 2 beach by night, 3 underwater, 4 underground.</summary>
    public void setPlace(int p)
    {
        if (p == place) return;
        place = p;
        syncTrack();
    }

    private int wantedFile()
    {
        if (endingOn) return 5;
        if (menuOn) return 0;
        var st = STATIONS[trackIdx];
        return st.file >= 0 ? st.file : place;
    }

    private void startMusicSlots()
    {
        for (int i = 0; i < 2; i++)
        {
            // each radio slot gets its own object: the synth's OnAudioFilterRead can only belong to one AudioSource
            var slotGo = new GameObject("Radio " + (i + 1));
            slotGo.transform.SetParent(host.transform, false);
            var src = slotGo.AddComponent<AudioSource>();
            src.loop = true;
            src.playOnAwake = false;
            src.spatialBlend = 0;
            src.volume = 0;
            host.slots.Add(new SynthHost.Slot { src = src, file = -1 });
        }
    }

    private void runMusic()
    {
        if (host == null || !playing) return;
        setMusicLevel(musicLevel, musicDuck);
        syncTrack();
    }

    private void stopMusic()
    {
        if (host == null) return;
        foreach (var sl in host.slots)
        {
            sl.gain.Target(0, 0.5);
            sl.pauseAt = Time.unscaledTimeAsDouble + 2.5;
            sl.pauseUnlessPlaying = true;
        }
        host.musicGain.Target(0, 0.1);
    }

    /// <summary>Fades to the right tune for the current station and place.</summary>
    private void syncTrack()
    {
        if (host == null || !playing || host.slots.Count == 0) return;
        int want = wantedFile();
        var cur = host.slots[activeSlot];
        if (cur.file == want && cur.src.isPlaying) return;
        cur.gain.Target(0, 1.2);
        cur.pauseAt = Time.unscaledTimeAsDouble + 6;
        cur.pauseUnlessPlaying = false;
        activeSlot = 1 - activeSlot;
        var next = host.slots[activeSlot];
        next.file = want;
        next.pauseAt = -1;
        var clip = Resources.Load<AudioClip>("Music/" + RADIO_FILES[want]);
        next.src.clip = clip;
        next.src.time = 0;
        if (clip != null) next.src.Play();
        next.gain.Ramp(0, 1, 3);
    }

    internal bool IsActiveSlot(SynthHost.Slot s) => host.slots.IndexOf(s) == activeSlot;
    internal bool Playing => playing;

    // ------------------------------------------------------------------ ambience

    /// <summary>0..1 amount of rain.</summary>
    public void setRain(double level)
    {
        rainLevel = level;
        host?.rain.Target(level * 0.07 * ambScale, 0.5);
    }

    /// <summary>The dog's bark, louder the closer it is.</summary>
    public void bark(double distance)
    {
        if (host == null) return;
        double near = Math.Max(0, 1 - distance / 110);
        double vol = 0.02 + 0.2 * near * near;
        for (int i = 0; i < 2; i++)
        {
            double w = i * 0.24;
            tone(520 - i * 40, 0.13, Wave.Sawtooth, vol, w, 300);
            noise(0.1, vol * 1.4, Filter.Bandpass, 1000, 1.5, w, 520);
        }
    }

    /// <summary>Packing wet sand into a bucket and tipping it out.</summary>
    public void sandBuild()
    {
        noise(0.22, 0.2, Filter.Bandpass, 900, 1.2);
        noise(0.1, 0.16, Filter.Lowpass, 400, 1, 0.16);
        tone(110, 0.14, Wave.Sine, 0.14, 0.12, 70);
    }

    /// <summary>A sandcastle slumping into the sand.</summary>
    public void crumble()
    {
        noise(0.5, 0.18, Filter.Bandpass, 700, 0.9, 0, 250);
        noise(0.3, 0.12, Filter.Lowpass, 300, 1, 0.12);
    }

    /// <summary>A happy little yip.</summary>
    public void yip()
    {
        tone(820, 0.1, Wave.Triangle, 0.1, 0, 1250);
        tone(980, 0.09, Wave.Triangle, 0.08, 0.14, 1400);
    }

    public void setOceanLevel(double v)
    {
        lastOcean = v;
        host?.ocean.Target(v * ambScale, 0.4);
    }

    /// <summary>The three volume sliders from the settings screen, each 0..1.</summary>
    public void setVolumes(double master, double music, double ambience)
    {
        masterVol = master;
        musicScale = music;
        ambScale = ambience;
        if (host == null) return;
        host.master.Target(muted ? 0 : 0.8 * master, 0.05);
        host.ocean.Target(lastOcean * ambience, 0.1);
        host.rain.Target(rainLevel * 0.07 * ambience, 0.1);
        setMusicLevel(musicLevel, musicDuck);
    }

    public bool toggleMute()
    {
        muted = !muted;
        host?.master.Target(muted ? 0 : 0.8 * masterVol, 0.05);
        return muted;
    }

    // ------------------------------------------------------------------ one-shots

    internal enum Wave { Sine, Triangle, Sawtooth, Square }
    internal enum Filter { Lowpass, Highpass, Bandpass }

    private void tone(double freq, double dur, Wave type, double vol, double when = 0, double slideTo = 0)
    {
        if (host == null) return;
        host.Enqueue(new SynthHost.Voice
        {
            kind = 0, wave = type, freq = freq, freqEnd = slideTo, dur = dur, vol = vol, delay = when, attack = 0.008, stopAfter = dur + 0.05,
        });
    }

    private void noise(double dur, double vol, Filter filter, double freq, double q = 1, double when = 0, double freqEnd = 0)
    {
        if (host == null) return;
        host.Enqueue(new SynthHost.Voice
        {
            kind = 1, filter = filter, freq = freq, freqEnd = freqEnd, q = q, dur = dur, vol = vol, delay = when,
            attack = Math.Min(0.03, dur / 3), stopAfter = dur + 0.05, noiseOffset = JS.Random(),
        });
    }

    /// <summary>Detector beep. strength 0..1 raises pitch as the coil nears a target.</summary>
    public void ping(string kind, double strength)
    {
        if (!detectorEnabled) return;
        double s = Math.Min(1, Math.Max(0, strength));
        const double dur = 0.06;
        switch (kind)
        {
            case "trash":
                tone(110 + s * 90, dur * 1.6, Wave.Sawtooth, 0.14);
                tone(118 + s * 95, dur * 1.6, Wave.Square, 0.05);
                break;
            case "coin":
                tone(520 + s * 700, dur * 2.4, Wave.Triangle, 0.2);
                tone((520 + s * 700) * 2, dur * 2, Wave.Sine, 0.06);
                break;
            case "deep":
            {
                double f = 330 + s * 450;
                tone(f, dur * 2.6, Wave.Sine, 0.2);
                tone(f * 1.5, dur * 2.6, Wave.Sine, 0.12, 0.012);
                break;
            }
            default:
                tone(300 + s * 600, dur * 1.5, Wave.Triangle, 0.18);
                break;
        }
    }

    /// <summary>A seagull call; `distance` in metres sets how loud and muffled it is.</summary>
    public void gull(double distance)
    {
        if (host == null) return;
        double near = Math.Max(0, 1 - distance / 70);
        if (near <= 0.02) return;
        double vol = 0.05 * near * near + 0.004;
        double bas = 1500 + JS.Random() * 300;
        double t0 = JS.Random() * 0.05;
        int calls = 2 + (int)Math.Floor(JS.Random() * 2);
        for (int i = 0; i < calls; i++)
        {
            host.Enqueue(new SynthHost.Voice
            {
                kind = 2, wave = Wave.Sawtooth, freq = bas, q = 3, vol = vol, delay = t0 + i * 0.22, dur = 0.22, attack = 0.03, stopAfter = 0.26,
            });
        }
    }

    /// <summary>Whirr of the cart's electric motor plus tyre noise on sand. level 0..1 is the speed.</summary>
    public void setEngine(bool on, double level = 0, bool boat = false)
    {
        if (host == null) return;
        if (on && !engineOn)
        {
            engineOn = true;
            host.engineRunning = true;
            host.engGain.Set(0, 0);
            host.engNoise.Set(0, 0);
        }
        if (!engineOn) return;
        if (!on)
        {
            host.engGain.Target(0, 0.1);
            host.engNoise.Target(0, 0.1);
            engineOn = false;
            host.engineStopAt = now + 0.6;
            return;
        }
        host.engineStopAt = -1;
        if (boat)
        {
            // a deeper outboard rumble and the rush of water along the hull
            host.engFreq1.Target(38 + level * 95, 0.1);
            host.engFreq2.Target(76 + level * 190, 0.1);
            host.engGain.Target(0.05 + level * 0.07, 0.1);
            host.engNoise.Target(0.02 + level * 0.14, 0.1);
            return;
        }
        host.engFreq1.Target(70 + level * 190, 0.08);
        host.engFreq2.Target(140 + level * 380, 0.08);
        host.engGain.Target(0.035 + level * 0.05, 0.1);
        host.engNoise.Target(level * 0.09, 0.1);
    }

    public void thud()
    {
        noise(0.18, 0.5, Filter.Lowpass, 260, 1);
        tone(90, 0.2, Wave.Sine, 0.35, 0, 40);
    }

    /// <summary>A fish leaping or a shark diving; `distance` in metres sets how loud it is.</summary>
    public void splash(double distance)
    {
        if (host == null) return;
        double near = Math.Max(0, 1 - distance / 90);
        if (near <= 0.02) return;
        noise(0.35, 0.22 * near * near + 0.01, Filter.Bandpass, 1100 + JS.Random() * 600, 0.8, 0, 500);
    }

    /// <summary>The creak of a bow being drawn.</summary>
    public void bowDraw()
    {
        noise(0.5, 0.1, Filter.Bandpass, 260, 2, 0, 520);
        tone(70, 0.5, Wave.Sawtooth, 0.025, 0, 120);
    }

    /// <summary>The twang of a bowstring and the whistle of the arrow.</summary>
    public void bowRelease(double power)
    {
        tone(420 + power * 160, 0.22, Wave.Triangle, 0.22, 0, 150);
        tone(210, 0.3, Wave.Sine, 0.15, 0, 90);
        noise(0.25, 0.12 + power * 0.1, Filter.Bandpass, 1800, 1.2, 0.02, 700);
    }

    /// <summary>Pop of the toy pistol.</summary>
    public void shot()
    {
        noise(0.14, 0.5, Filter.Bandpass, 1600, 1.1);
        noise(0.06, 0.35, Filter.Highpass, 3500, 1);
        tone(150, 0.16, Wave.Sine, 0.45, 0, 45);
    }

    public void splat()
    {
        noise(0.18, 0.35, Filter.Lowpass, 700, 1);
        tone(300, 0.12, Wave.Triangle, 0.15, 0, 90);
    }

    public void crunch()
    {
        noise(0.22, 0.5, Filter.Bandpass, 700, 0.9);
        noise(0.12, 0.35, Filter.Lowpass, 400, 1, 0.03);
    }

    /// <summary>Soft bristle noise burst; call often while scrubbing, it self-throttles.</summary>
    public void brush(double speed)
    {
        if (host == null) return;
        double t = now;
        if (t - lastBrush < 0.07) return;
        lastBrush = t;
        double v = Math.Min(1, speed / 900);
        if (v < 0.03) return;
        noise(0.12, 0.03 + v * 0.22, Filter.Bandpass, 800 + v * 800 + JS.Random() * 200, 1.2);
    }

    public void ui() => tone(660, 0.07, Wave.Sine, 0.1);

    /// <summary>
    /// The small sounds of fishing: 'cast' the line whipping out, 'plop' the float landing, 'nibble' a touch at the bait, 'bite'
    /// the float going under, 'hook' the strike, 'reel' one click of the reel, 'catch' the fish coming out, 'away' the one that
    /// did not, 'net' a sweep of the hand net through the water.
    /// </summary>
    public void fishing(string what)
    {
        switch (what)
        {
            case "cast":
                noise(0.28, 0.16, Filter.Bandpass, 2600, 2.2, 0, 900);
                break;
            case "plop":
                tone(520, 0.12, Wave.Sine, 0.16, 0, 170);
                noise(0.14, 0.12, Filter.Lowpass, 900, 1, 0.01);
                break;
            case "nibble":
                tone(880, 0.05, Wave.Sine, 0.05);
                break;
            case "bite":
                tone(660, 0.08, Wave.Triangle, 0.14);
                tone(990, 0.1, Wave.Triangle, 0.14, 0.08);
                noise(0.2, 0.14, Filter.Lowpass, 1100, 1, 0.02);
                break;
            case "hook":
                noise(0.12, 0.2, Filter.Bandpass, 1500, 1.4);
                tone(300, 0.12, Wave.Triangle, 0.1, 0, 420);
                break;
            case "reel":
                noise(0.018, 0.1, Filter.Highpass, 3200, 1);
                break;
            case "catch":
                noise(0.3, 0.22, Filter.Lowpass, 1300, 1);
                tone(420, 0.2, Wave.Sine, 0.1, 0.02, 760);
                break;
            case "away":
                tone(330, 0.2, Wave.Sine, 0.08, 0, 200);
                break;
            case "net":
                noise(0.3, 0.14, Filter.Lowpass, 700, 1, 0, 300);
                break;
        }
    }

    /// <summary>The camera's shutter: a sharp click as it opens and a softer one as it closes, then the little film wind.</summary>
    public void shutter()
    {
        noise(0.025, 0.45, Filter.Highpass, 4200, 1);
        tone(2200, 0.02, Wave.Square, 0.05);
        noise(0.035, 0.32, Filter.Bandpass, 2600, 1.4, 0.07);
        noise(0.22, 0.07, Filter.Bandpass, 1400, 3, 0.16, 2200);
    }

    public void error() => tone(200, 0.18, Wave.Sine, 0.12, 0, 150);

    public void coin()
    {
        tone(1318, 0.12, Wave.Triangle, 0.14);
        tone(1760, 0.22, Wave.Triangle, 0.12, 0.07);
    }

    public void discover()
    {
        var a = new[] { 523.25, 659.25, 783.99, 1046.5 };
        for (int i = 0; i < a.Length; i++) tone(a[i], 1.1 - i * 0.1, Wave.Triangle, 0.14, i * 0.09);
        foreach (var f in new[] { 261.6, 392 }) tone(f, 1.4, Wave.Sine, 0.1, 0.05);
    }

    public void sip()
    {
        noise(0.5, 0.18, Filter.Bandpass, 500, 2, 0, 900);
        tone(392, 0.5, Wave.Sine, 0.08, 0.1);
    }

    public void whoosh(double dur = 3)
    {
        noise(dur, 0.6, Filter.Bandpass, 250, 0.7, 0, 1400);
        noise(dur * 0.8, 0.45, Filter.Lowpass, 900, 0.5, dur * 0.35, 200);
    }

    private double staticAcc = 0;

    /// <summary>Radio-style crackle while the coil sweeps; denser and louder as it nears a target.</summary>
    public void detectorStatic(double dt, double strength, string kind)
    {
        if (host == null || !detectorEnabled) return;
        staticAcc += dt * (4 + strength * 12);
        double rough = kind == "trash" ? 0.6 : 1;
        while (staticAcc >= 1)
        {
            staticAcc -= 1;
            double freq = (1800 + JS.Random() * 3500) * rough;
            noise(0.015 + JS.Random() * 0.03, (0.012 + strength * 0.04) * (0.5 + JS.Random()), Filter.Bandpass, freq, 1.5);
        }
    }

    /// <summary>Wood-pop of the campfire; `near` is 0..1 and sets how loud it is.</summary>
    public void crackle(double near = 1)
    {
        double f = 700 + JS.Random() * 1400;
        noise(0.02 + JS.Random() * 0.05, 0.16 * near * near * (0.4 + JS.Random()), Filter.Bandpass, f, 0.8);
    }
}

/// <summary>
/// The audio thread side: a mixer of short voices plus the ocean, rain and engine beds, all through the master gain. Parameters
/// set from the game thread glide the way Web Audio's setTargetAtTime does.
/// </summary>
internal class SynthHost : MonoBehaviour
{
    internal AudioManager owner;

    /// <summary>A value the game sets and the audio thread glides toward (setTargetAtTime), or ramps linearly.</summary>
    internal class Smooth
    {
        private volatile float target, tau = 0.0001f;
        private volatile float rampFrom, rampTo;
        private double rampStart = -1, rampEnd = -1;
        internal double value;

        public void Set(double v, double t) { value = v; target = (float)v; tau = 0.0001f; rampStart = -1; }
        public void Target(double v, double timeConstant) { target = (float)v; tau = (float)Math.Max(1e-4, timeConstant); rampStart = -1; }
        public void Ramp(double from, double to, double seconds)
        {
            rampFrom = (float)from;
            rampTo = (float)to;
            value = from;
            target = (float)to;
            rampStart = Time.unscaledTimeAsDouble;
            rampEnd = rampStart + seconds;
        }

        /// <summary>Main-thread update for things the game thread itself applies (music volumes).</summary>
        public double StepMain(double dt)
        {
            if (rampStart >= 0)
            {
                double t = Time.unscaledTimeAsDouble;
                if (t >= rampEnd) { rampStart = -1; value = rampTo; }
                else value = rampFrom + (rampTo - rampFrom) * (t - rampStart) / (rampEnd - rampStart);
                return value;
            }
            value += (target - value) * (1 - Math.Exp(-dt / tau));
            return value;
        }

        public double Step(double coefPerSample, int sampleRate)
        {
            double k = 1 - Math.Exp(-1.0 / (tau * sampleRate));
            value += (target - value) * k;
            return value;
        }

        public double Coef(int sampleRate) => 1 - Math.Exp(-1.0 / (tau * sampleRate));
        public float TargetValue => target;
    }

    internal class Slot
    {
        public AudioSource src;
        public int file;
        public Smooth gain = new Smooth();
        public double pauseAt = -1;
        public bool pauseUnlessPlaying;
    }

    internal class Voice
    {
        public int kind; // 0 tone, 1 noise, 2 gull
        public AudioManager.Wave wave;
        public AudioManager.Filter filter;
        public double freq, freqEnd, q = 1, dur, vol, delay, attack, stopAfter, noiseOffset;
        // runtime
        public long start, end;
        public double phase;
        public int noisePos;
        public Biquad bq;
    }

    internal class Biquad
    {
        double b0, b1, b2, a1, a2, x1, x2, y1, y2;

        public void Set(AudioManager.Filter type, double freq, double q, double fs)
        {
            freq = Math.Max(10, Math.Min(fs * 0.49, freq));
            double w0 = 2 * Math.PI * freq / fs, cw = Math.Cos(w0), sw = Math.Sin(w0);
            double alpha, a0;
            switch (type)
            {
                case AudioManager.Filter.Lowpass:
                    alpha = sw / (2 * Math.Pow(10, q / 20));
                    a0 = 1 + alpha;
                    b0 = (1 - cw) / 2 / a0; b1 = (1 - cw) / a0; b2 = (1 - cw) / 2 / a0;
                    break;
                case AudioManager.Filter.Highpass:
                    alpha = sw / (2 * Math.Pow(10, q / 20));
                    a0 = 1 + alpha;
                    b0 = (1 + cw) / 2 / a0; b1 = -(1 + cw) / a0; b2 = (1 + cw) / 2 / a0;
                    break;
                default:
                    alpha = sw / (2 * Math.Max(1e-4, q));
                    a0 = 1 + alpha;
                    b0 = alpha / a0; b1 = 0; b2 = -alpha / a0;
                    break;
            }
            a1 = -2 * cw / a0;
            a2 = (1 - alpha) / a0;
        }

        public double Process(double x)
        {
            double y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
            x2 = x1; x1 = x; y2 = y1; y1 = y;
            return y;
        }
    }

    internal readonly Smooth master = new Smooth(), ocean = new Smooth(), rain = new Smooth(), musicGain = new Smooth();
    internal readonly Smooth engGain = new Smooth(), engNoise = new Smooth(), engFreq1 = new Smooth(), engFreq2 = new Smooth();
    internal volatile bool engineRunning;
    internal double engineStopAt = -1;
    internal readonly List<Slot> slots = new List<Slot>();

    private readonly ConcurrentQueue<Voice> incoming = new ConcurrentQueue<Voice>();
    private readonly List<Voice> voices = new List<Voice>();
    private int sampleRate = 48000;
    private long frame;
    private float[] noiseBuf, oceanBuf;
    private int oceanPos;
    private readonly Biquad oceanLp = new Biquad(), rainHp = new Biquad(), rainLp = new Biquad(), engLp = new Biquad(), engBp = new Biquad();
    private double engPhase1, engPhase2;
    private int enginePos;
    private int rainPos;
    private System.Random rng = new System.Random();
    private volatile float nowSeconds;

    internal double Now => nowSeconds;

    internal void Enqueue(Voice v) => incoming.Enqueue(v);

    private void Awake()
    {
        sampleRate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
        // one shared 2s white noise buffer for all the noise-based sounds
        noiseBuf = new float[sampleRate * 2];
        for (int i = 0; i < noiseBuf.Length; i++) noiseBuf[i] = (float)(rng.NextDouble() * 2 - 1);
        // 4 second looping brown-noise surf
        int len = sampleRate * 4;
        oceanBuf = new float[len];
        double last = 0;
        for (int i = 0; i < len; i++)
        {
            last = (last + 0.02 * (rng.NextDouble() * 2 - 1)) / 1.02;
            oceanBuf[i] = (float)(last * 3.5);
        }
        // crossfade the loop seam
        int fade = (int)Math.Floor(sampleRate * 0.1);
        for (int i = 0; i < fade; i++)
        {
            float k = (float)i / fade;
            oceanBuf[i] = oceanBuf[i] * k + oceanBuf[len - fade + i] * (1 - k);
        }
        rainHp.Set(AudioManager.Filter.Highpass, 1100, 1, sampleRate);
        rainLp.Set(AudioManager.Filter.Lowpass, 7000, 1, sampleRate);
        engLp.Set(AudioManager.Filter.Lowpass, 900, 1, sampleRate);
        engBp.Set(AudioManager.Filter.Bandpass, 600, 1, sampleRate);
        var src = gameObject.AddComponent<AudioSource>();
        src.clip = AudioClip.Create("synth", sampleRate, 2, sampleRate, false);
        src.loop = true;
        src.spatialBlend = 0;
        src.volume = 1;
        src.Play();
    }

    private void Update()
    {
        double dt = Time.unscaledDeltaTime;
        double m = master.StepMain(0);
        m = master.value;
        double mg = musicGain.StepMain(dt);
        foreach (var s in slots)
        {
            double g = s.gain.StepMain(dt);
            s.src.volume = (float)Math.Max(0, Math.Min(1, g * mg * m));
            if (s.pauseAt >= 0 && Time.unscaledTimeAsDouble >= s.pauseAt)
            {
                s.pauseAt = -1;
                bool keep = s.pauseUnlessPlaying ? owner.Playing : owner.IsActiveSlot(s);
                if (!keep) s.src.Pause();
            }
        }
        if (engineStopAt >= 0 && Now >= engineStopAt)
        {
            engineStopAt = -1;
            engineRunning = false;
        }
    }

    private void OnAudioFilterRead(float[] data, int channels)
    {
        while (incoming.TryDequeue(out var v))
        {
            v.start = frame + (long)(v.delay * sampleRate);
            v.end = v.start + (long)(v.stopAfter * sampleRate);
            v.noisePos = (int)(v.noiseOffset * noiseBuf.Length) % noiseBuf.Length;
            if (v.kind != 0)
            {
                v.bq = new Biquad();
                v.bq.Set(v.kind == 2 ? AudioManager.Filter.Bandpass : v.filter, v.kind == 2 ? v.freq * 1.1 : v.freq, v.q, sampleRate);
            }
            voices.Add(v);
        }
        int n = data.Length / channels;
        double fs = sampleRate;
        double mCoef = master.Coef(sampleRate), oCoef = ocean.Coef(sampleRate), rCoef = rain.Coef(sampleRate);
        double egC = engGain.Coef(sampleRate), enC = engNoise.Coef(sampleRate), ef1C = engFreq1.Coef(sampleRate), ef2C = engFreq2.Coef(sampleRate);
        for (int i = 0; i < n; i++)
        {
            long f = frame + i;
            double t = f / fs;
            double mix = 0;
            // the sea: brown noise through a lowpass that breathes with a slow LFO
            if ((i & 31) == 0) oceanLp.Set(AudioManager.Filter.Lowpass, 220 + 90 * Math.Sin(2 * Math.PI * 0.12 * t), 1, fs);
            ocean.value += (ocean.TargetValue - ocean.value) * oCoef;
            mix += oceanLp.Process(oceanBuf[oceanPos]) * ocean.value;
            oceanPos = (oceanPos + 1) % oceanBuf.Length;
            // rain: filtered white noise
            rain.value += (rain.TargetValue - rain.value) * rCoef;
            if (rain.value > 1e-5 || rain.TargetValue > 0)
                mix += rainLp.Process(rainHp.Process(noiseBuf[rainPos])) * rain.value;
            rainPos = (rainPos + 1) % noiseBuf.Length;
            // the engine
            if (engineRunning)
            {
                engGain.value += (engGain.TargetValue - engGain.value) * egC;
                engNoise.value += (engNoise.TargetValue - engNoise.value) * enC;
                engFreq1.value += (engFreq1.TargetValue - engFreq1.value) * ef1C;
                engFreq2.value += (engFreq2.TargetValue - engFreq2.value) * ef2C;
                engPhase1 = (engPhase1 + engFreq1.value / fs) % 1;
                engPhase2 = (engPhase2 + engFreq2.value / fs) % 1;
                double saw = 2 * engPhase1 - 1, sine = Math.Sin(2 * Math.PI * engPhase2);
                mix += engLp.Process(saw + sine) * engGain.value;
                mix += engBp.Process(noiseBuf[enginePos]) * engNoise.value;
                enginePos = (enginePos + 1) % noiseBuf.Length;
            }
            // one-shots
            for (int k = voices.Count - 1; k >= 0; k--)
            {
                var v = voices[k];
                if (f < v.start) continue;
                if (f >= v.end) { voices.RemoveAt(k); continue; }
                double lt = (f - v.start) / fs;
                mix += VoiceSample(v, lt, fs, (i & 31) == 0);
            }
            master.value += (master.TargetValue - master.value) * mCoef;
            float s = (float)(mix * master.value);
            for (int c = 0; c < channels; c++) data[i * channels + c] += s;
        }
        frame += n;
        nowSeconds = (float)(frame / fs);
    }

    /// <summary>exponentialRampToValueAtTime from a to b over [t0, t1].</summary>
    private static double ExpRamp(double a, double b, double t0, double t1, double t)
    {
        if (t <= t0) return a;
        if (t >= t1) return b;
        return a * Math.Pow(b / a, (t - t0) / (t1 - t0));
    }

    private double VoiceSample(Voice v, double lt, double fs, bool updateFilter)
    {
        // gain: 0.0001 -> vol over the attack, then down to 0.0001 at dur
        double g = lt < v.attack ? ExpRamp(0.0001, v.vol, 0, v.attack, lt) : ExpRamp(v.vol, 0.0001, v.attack, v.dur, lt);
        if (lt > v.dur) g = 0.0001;
        if (v.kind == 0)
        {
            double fr = v.freqEnd > 0 ? ExpRamp(v.freq, v.freqEnd, 0, v.dur, lt) : v.freq;
            v.phase = (v.phase + fr / fs) % 1;
            double x;
            switch (v.wave)
            {
                case AudioManager.Wave.Sine: x = Math.Sin(2 * Math.PI * v.phase); break;
                case AudioManager.Wave.Triangle: x = 1 - 4 * Math.Abs(v.phase - 0.5); break;
                case AudioManager.Wave.Sawtooth: x = 2 * v.phase - 1 - PolyBlep(v.phase, fr / fs); break;
                default: x = (v.phase < 0.5 ? 1 : -1) + PolyBlep(v.phase, fr / fs) - PolyBlep((v.phase + 0.5) % 1, fr / fs); break;
            }
            return x * g;
        }
        if (v.kind == 2)
        {
            // gull: sawtooth sweeping up then down through a bandpass
            double fr = lt < 0.07 ? v.freq * 0.8 + (v.freq * 1.25 - v.freq * 0.8) * lt / 0.07 : ExpRamp(v.freq * 1.25, v.freq * 0.7, 0.07, 0.2, lt);
            v.phase = (v.phase + fr / fs) % 1;
            double x = 2 * v.phase - 1 - PolyBlep(v.phase, fr / fs);
            return v.bq.Process(x) * g;
        }
        if (updateFilter && v.freqEnd > 0) v.bq.Set(v.filter, ExpRamp(v.freq, v.freqEnd, 0, v.dur, lt), v.q, fs);
        double nz = noiseBuf[v.noisePos];
        v.noisePos = (v.noisePos + 1) % noiseBuf.Length;
        return v.bq.Process(nz) * g;
    }

    private static double PolyBlep(double t, double dt)
    {
        if (dt <= 0) return 0;
        if (t < dt) { t /= dt; return t + t - t * t - 1; }
        if (t > 1 - dt) { t = (t - 1) / dt; return t * t + t + t + 1; }
        return 0;
    }
}
