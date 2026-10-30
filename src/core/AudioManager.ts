export type PingKind = 'generic' | 'trash' | 'coin' | 'deep';

/** Zero-asset Web Audio synthesiser. Everything is generated at runtime. */
export class AudioManager {
  private ctx: AudioContext | null = null;
  private master!: GainNode;
  private oceanGain!: GainNode;
  private noiseBuf!: AudioBuffer;
  private muted = false;
  private masterVol = 1;
  private musicScale = 1;
  private ambScale = 1;
  private lastOcean = 0.06;
  private rainGain: GainNode | null = null;
  private rainLevel = 0;
  private lastBrush = 0;
  private engine: { osc: OscillatorNode; osc2: OscillatorNode; gain: GainNode; noise: GainNode; src: AudioBufferSourceNode } | null = null;
  /** the detector may only make sound while it is the tool in hand */
  detectorEnabled = false;
  private musicGain!: GainNode;
  private musicFilter!: BiquadFilterNode;
  private musicTimer = 0;
  private musicOn = false;
  private nextStep = 0;
  private step = 0;
  private musicLevel = 0;
  private musicDuck = 1;

  constructor() {
    const unlock = (): void => {
      if (!this.ctx) this.init();
      else if (this.ctx.state === 'suspended') void this.ctx.resume();
    };
    window.addEventListener('pointerdown', unlock);
    window.addEventListener('keydown', unlock);
  }

  get ready(): boolean {
    return this.ctx !== null;
  }

  private init(): void {
    const Ctor = window.AudioContext ?? (window as unknown as { webkitAudioContext: typeof AudioContext }).webkitAudioContext;
    if (!Ctor) return;
    this.ctx = new Ctor();
    this.master = this.ctx.createGain();
    this.master.gain.value = this.muted ? 0 : 0.8 * this.masterVol;
    this.master.connect(this.ctx.destination);

    // one shared 2s white noise buffer for all the noise-based sounds
    const len = this.ctx.sampleRate * 2;
    this.noiseBuf = this.ctx.createBuffer(1, len, this.ctx.sampleRate);
    const d = this.noiseBuf.getChannelData(0);
    for (let i = 0; i < len; i++) d[i] = Math.random() * 2 - 1;

    this.musicFilter = this.ctx.createBiquadFilter();
    this.musicFilter.type = 'lowpass';
    this.musicFilter.frequency.value = 800;
    this.musicGain = this.ctx.createGain();
    this.musicGain.gain.value = 0;
    // dry path
    this.musicFilter.connect(this.musicGain);
    // a generated room reverb makes everything softer and roomier
    const reverb = this.ctx.createConvolver();
    const rl = this.ctx.sampleRate * 3;
    const ir = this.ctx.createBuffer(2, rl, this.ctx.sampleRate);
    for (let ch = 0; ch < 2; ch++) {
      const d = ir.getChannelData(ch);
      for (let i = 0; i < rl; i++) d[i] = (Math.random() * 2 - 1) * Math.pow(1 - i / rl, 2.6);
    }
    reverb.buffer = ir;
    const wet = this.ctx.createGain();
    wet.gain.value = 0.55;
    this.musicFilter.connect(reverb).connect(wet).connect(this.musicGain);
    // a slow, quiet echo
    const echo = this.ctx.createDelay(1.5);
    echo.delayTime.value = 0.62;
    const fb = this.ctx.createGain();
    fb.gain.value = 0.32;
    const send = this.ctx.createGain();
    send.gain.value = 0.22;
    this.musicFilter.connect(send).connect(echo);
    echo.connect(fb).connect(echo);
    echo.connect(this.musicGain);
    this.musicGain.connect(this.master);

    this.startOcean();
    if (this.musicOn) this.runMusic();
  }

  // ------------------------------------------------------------------ radio music

  get radioOn(): boolean {
    return this.musicOn;
  }

  setRadio(on: boolean): void {
    this.musicOn = on;
    if (!this.ctx) return;
    if (on) this.runMusic();
    else this.stopMusic();
  }

  /** 0..1 from how close the player stands; also muffles the sound with distance. */
  setMusicLevel(level: number, duck = 1): void {
    this.musicLevel = level;
    this.musicDuck = duck;
    if (!this.ctx || !this.musicGain) return;
    const t = this.ctx.currentTime;
    this.musicGain.gain.setTargetAtTime(this.musicOn ? level * duck * 0.8 * this.musicScale : 0, t, 0.25);
    this.musicFilter.frequency.setTargetAtTime(450 + level * level * 3800, t, 0.15);
  }

  private runMusic(): void {
    if (!this.ctx || this.musicTimer) return;
    this.nextStep = this.ctx.currentTime + 0.1;
    this.step = 0;
    this.setMusicLevel(this.musicLevel, this.musicDuck);
    this.musicTimer = window.setInterval(() => this.scheduleMusic(), 90);
  }

  private stopMusic(): void {
    window.clearInterval(this.musicTimer);
    this.musicTimer = 0;
    if (this.ctx) this.musicGain.gain.setTargetAtTime(0, this.ctx.currentTime, 0.1);
  }

  private mTone(freq: number, dur: number, type: OscillatorType, vol: number, at: number, attack = 0.01, detune = 0): void {
    const ctx = this.ctx!;
    const o = ctx.createOscillator();
    const g = ctx.createGain();
    o.type = type;
    o.frequency.value = freq;
    o.detune.value = detune;
    g.gain.setValueAtTime(0.0001, at);
    g.gain.exponentialRampToValueAtTime(vol, at + attack);
    g.gain.exponentialRampToValueAtTime(0.0001, at + dur);
    o.connect(g).connect(this.musicFilter);
    o.start(at);
    o.stop(at + dur + 0.05);
  }

  private mNoise(dur: number, vol: number, type: BiquadFilterType, freq: number, at: number): void {
    const ctx = this.ctx!;
    const src = ctx.createBufferSource();
    src.buffer = this.noiseBuf;
    src.loop = true;
    const f = ctx.createBiquadFilter();
    f.type = type;
    f.frequency.value = freq;
    const g = ctx.createGain();
    g.gain.setValueAtTime(vol, at);
    g.gain.exponentialRampToValueAtTime(0.0001, at + dur);
    src.connect(f).connect(g).connect(this.musicFilter);
    src.start(at, Math.random());
    src.stop(at + dur + 0.05);
  }

  /** Warm electric-piano note: a soft sine body, a quiet octave and a faint bell on top. */
  private mKeys(freq: number, vol: number, at: number, dur = 2.2): void {
    this.mTone(freq, dur, 'sine', vol, at, 0.012);
    this.mTone(freq * 2, dur * 0.5, 'sine', vol * 0.22, at, 0.01);
    this.mTone(freq * 4.01, 0.35, 'sine', vol * 0.06, at, 0.004);
  }

  /** Slow, shimmering chord tone: two gently detuned sines with a long swell. */
  private mPad(freq: number, dur: number, vol: number, at: number): void {
    this.mTone(freq, dur, 'sine', vol, at, 1.1, -6);
    this.mTone(freq, dur, 'sine', vol, at, 1.1, 6);
    this.mTone(freq * 2, dur, 'triangle', vol * 0.18, at, 1.4);
  }

  /** A plucked, guitar-like note: a quick triangle with a soft octave. */
  private mPluck(freq: number, vol: number, at: number, dur = 1.1): void {
    this.mTone(freq, dur, 'triangle', vol, at, 0.004);
    this.mTone(freq * 2, dur * 0.4, 'sine', vol * 0.22, at, 0.003);
  }

  /** The radio's stations. Each one is a short chord loop with its own mood, tempo and way of playing it. */
  static readonly TRACKS: { name: string; mood: 'dreamy' | 'sunset' | 'night' | 'bossa' | 'rain' | 'folk'; bpm: number; chords: number[][]; melody: number[] }[] = [
    {
      name: 'Morning Tide', mood: 'dreamy', bpm: 62,
      chords: [[48, 55, 59, 62, 64], [45, 52, 55, 59, 64], [41, 48, 52, 57, 62], [43, 50, 53, 57, 60]],
      melody: [72, 74, 76, 79, 81, 84],
    },
    {
      name: 'Lazy Sunset', mood: 'sunset', bpm: 56,
      chords: [[50, 57, 61, 64, 66], [47, 54, 57, 61, 66], [43, 50, 54, 59, 62], [45, 52, 55, 59, 62]],
      melody: [74, 76, 78, 81, 83, 86],
    },
    {
      name: 'Starlit Night', mood: 'night', bpm: 48,
      chords: [[45, 52, 55, 59, 64], [41, 48, 52, 57, 60], [48, 55, 59, 62, 64], [40, 47, 50, 55, 59]],
      melody: [69, 72, 74, 76, 79, 81, 84],
    },
    {
      name: 'Beach Bossa', mood: 'bossa', bpm: 104,
      chords: [[50, 53, 57, 60, 64], [43, 47, 53, 57, 64], [48, 52, 55, 59, 62], [45, 52, 55, 61, 64]],
      melody: [74, 77, 79, 81, 84],
    },
    {
      name: 'Rainy Window', mood: 'rain', bpm: 56,
      chords: [[41, 48, 52, 57, 60], [40, 47, 50, 55, 59], [38, 50, 53, 57, 64], [36, 48, 52, 55, 59]],
      melody: [72, 74, 77, 79, 81],
    },
    {
      name: 'Campfire Strum', mood: 'folk', bpm: 84,
      chords: [[43, 50, 55, 59, 62], [40, 47, 52, 55, 59], [48, 55, 60, 64, 67], [50, 57, 62, 66, 69]],
      melody: [79, 81, 83, 86, 88],
    },
  ];

  private trackIdx = 0;

  get trackName(): string {
    return AudioManager.TRACKS[this.trackIdx].name;
  }

  get trackNumber(): number {
    return this.trackIdx;
  }

  get trackCount(): number {
    return AudioManager.TRACKS.length;
  }

  /** Switches the radio to another station; the last notes of the old tune ring out under the new one. */
  setTrack(i: number): void {
    const n = AudioManager.TRACKS.length;
    this.trackIdx = ((Math.round(i) % n) + n) % n;
    this.step = 0;
    if (this.ctx) this.nextStep = this.ctx.currentTime + 0.25;
  }

  /** One scheduling pass of the current station: queues notes a moment ahead of the clock. */
  private scheduleMusic(): void {
    const ctx = this.ctx;
    if (!ctx || !this.musicOn) return;
    const tr = AudioManager.TRACKS[this.trackIdx];
    const midi = (n: number): number => 440 * Math.pow(2, (n - 69) / 12);
    const sixteenth = 60 / tr.bpm / 4;
    while (this.nextStep < ctx.currentTime + 0.4) {
      const s = this.step % 64;
      const bar = Math.floor(s / 16);
      const k = s % 16;
      const chord = tr.chords[bar];
      const pent = tr.melody;
      const t = this.nextStep + (Math.random() - 0.5) * 0.012; // human, not metronomic
      const bell = (chance: number, dur = 3): void => {
        if (Math.random() < chance) this.mKeys(midi(pent[Math.floor(Math.random() * pent.length)]), 0.045, t, dur);
      };

      switch (tr.mood) {
        case 'dreamy': {
          // soft chords, a lazy piano arpeggio, sparse bells and a gentle heartbeat
          if (k === 0) {
            chord.forEach((n, i) => this.mPad(midi(n + (i < 2 ? 0 : 12)), sixteenth * 17, 0.035, t + i * 0.05));
            this.mTone(midi(chord[0] - 12), sixteenth * 14, 'sine', 0.2, t, 0.05);
            this.mTone(95, 0.3, 'sine', 0.1, t, 0.01);
          }
          if (k === 8) this.mTone(midi(chord[0] - 12), sixteenth * 6, 'sine', 0.12, t, 0.05);
          const arp = [2, 5, 8, 11, 14].indexOf(k);
          if (arp >= 0 && Math.random() > 0.18) this.mKeys(midi(chord[(arp + bar) % chord.length] + 12), 0.055 + Math.random() * 0.025, t);
          if (k === 4 || k === 12) bell(0.38);
          if (k % 4 === 2) this.mNoise(0.09, 0.012, 'bandpass', 4200, t);
          if (Math.random() < 0.05) this.mNoise(0.012, 0.02, 'highpass', 5000, t + Math.random() * sixteenth);
          break;
        }
        case 'sunset': {
          // warm and unhurried: long pads, a plucked figure that rolls over the chord, a lazy tune on top
          if (k === 0) {
            chord.forEach((n, i) => this.mPad(midi(n + (i < 2 ? 0 : 12)), sixteenth * 17, 0.04, t + i * 0.06));
            this.mTone(midi(chord[0] - 12), sixteenth * 15, 'sine', 0.22, t, 0.06);
          }
          if (k === 8) this.mTone(midi(chord[0] - 12 + 7), sixteenth * 6, 'sine', 0.1, t, 0.05);
          const fig = [0, 3, 6, 8, 11, 14].indexOf(k);
          if (fig >= 0 && Math.random() > 0.12) this.mPluck(midi(chord[(fig + bar * 2) % chord.length] + 12), 0.05 + Math.random() * 0.02, t, 1.4);
          if (k === 4 || k === 12) bell(0.5, 2.6);
          if (k % 8 === 6) this.mNoise(0.1, 0.01, 'bandpass', 3800, t);
          break;
        }
        case 'night': {
          // very sparse: deep pads, a drone, and the odd high bell like a star coming out
          if (k === 0) {
            chord.forEach((n, i) => this.mPad(midi(n + (i < 2 ? 0 : 12)), sixteenth * 17, 0.045, t + i * 0.08));
            this.mTone(midi(chord[0] - 12), sixteenth * 16, 'sine', 0.2, t, 0.1);
          }
          if (k === 0 || k === 6 || k === 10) bell(0.42, 4);
          if (k === 8 && Math.random() < 0.5) this.mKeys(midi(chord[2] + 12), 0.04, t, 3);
          break;
        }
        case 'bossa': {
          // a light, swaying beat: offbeat chord stabs, a walking bass and a ticking shaker
          const root = chord[0] - 12;
          if (k === 0 || k === 8) this.mTone(midi(root), sixteenth * 6, 'sine', 0.18, t, 0.015);
          if (k === 6 || k === 14) this.mTone(midi(root + 7), sixteenth * 4, 'sine', 0.13, t, 0.015);
          if ([0, 3, 6, 10, 12].includes(k)) chord.slice(1, 4).forEach((n, i) => this.mKeys(midi(n + 12), 0.032, t + i * 0.012, 0.5));
          if (k % 2 === 0) this.mNoise(0.05, 0.011, 'bandpass', 5200, t);
          if (k === 3 || k === 6 || k === 10 || k === 13) this.mNoise(0.03, 0.02, 'bandpass', 2400, t);
          if (k === 8 || k === 12) bell(0.35, 1.6);
          break;
        }
        case 'rain': {
          // slow electric-piano chords under a soft hiss of rain
          if (k === 0) {
            chord.forEach((n, i) => this.mPad(midi(n + (i < 2 ? 0 : 12)), sixteenth * 17, 0.03, t + i * 0.05));
            this.mTone(midi(chord[0] - 12), sixteenth * 15, 'sine', 0.18, t, 0.06);
          }
          if (k % 4 === 0) this.mKeys(midi(chord[(k / 4 + bar) % chord.length] + 12), 0.05, t, 2.6);
          if (k % 2 === 0) this.mNoise(0.16, 0.007, 'highpass', 6500, t);
          if (k === 6 || k === 14) bell(0.3, 3);
          break;
        }
        case 'folk': {
          // an acoustic strum around the fire, with a soft thump underneath
          const strum = (dir: number, vol: number): void => {
            const order = dir > 0 ? [0, 1, 2, 3, 4] : [4, 3, 2, 1, 0];
            order.forEach((ci, i) => this.mPluck(midi(chord[ci] + (ci < 2 ? 0 : 12)), vol, t + i * 0.022, 1.3));
          };
          if (k === 0) { strum(1, 0.05); this.mTone(midi(chord[0] - 12), 0.4, 'sine', 0.2, t, 0.01); this.mTone(80, 0.22, 'sine', 0.13, t, 0.005); }
          if (k === 6) strum(-1, 0.03);
          if (k === 8) { strum(1, 0.04); this.mTone(midi(chord[0] - 12 + 7), 0.35, 'sine', 0.14, t, 0.01); this.mTone(80, 0.2, 'sine', 0.1, t, 0.005); }
          if (k === 10 || k === 14) strum(-1, 0.026);
          if (k === 4 || k === 12) this.mNoise(0.07, 0.014, 'bandpass', 3800, t);
          if (k === 2 || k === 10) bell(0.22, 1.8);
          break;
        }
      }

      this.nextStep += sixteenth;
      this.step++;
    }
  }

  /** 4 second looping brown-noise surf, low-passed by a slow LFO between 120Hz and 550Hz. */
  private startOcean(): void {
    const ctx = this.ctx!;
    const len = ctx.sampleRate * 4;
    const buf = ctx.createBuffer(1, len, ctx.sampleRate);
    const d = buf.getChannelData(0);
    let last = 0;
    for (let i = 0; i < len; i++) {
      last = (last + 0.02 * (Math.random() * 2 - 1)) / 1.02;
      d[i] = last * 3.5;
    }
    // crossfade the loop seam
    const fade = Math.floor(ctx.sampleRate * 0.1);
    for (let i = 0; i < fade; i++) {
      const k = i / fade;
      d[i] = d[i] * k + d[len - fade + i] * (1 - k);
    }
    const src = ctx.createBufferSource();
    src.buffer = buf;
    src.loop = true;
    const lp = ctx.createBiquadFilter();
    lp.type = 'lowpass';
    lp.frequency.value = 220;
    const lfo = ctx.createOscillator();
    lfo.frequency.value = 0.12;
    const lfoGain = ctx.createGain();
    lfoGain.gain.value = 90;
    lfo.connect(lfoGain).connect(lp.frequency);
    this.oceanGain = ctx.createGain();
    this.oceanGain.gain.value = 0.06 * this.ambScale;
    src.connect(lp).connect(this.oceanGain).connect(this.master);
    src.start();
    lfo.start();

    // rain: a soft hiss of filtered noise, silent until it rains
    const rain = ctx.createBufferSource();
    rain.buffer = this.noiseBuf;
    rain.loop = true;
    const hp = ctx.createBiquadFilter();
    hp.type = 'highpass';
    hp.frequency.value = 1100;
    const rlp = ctx.createBiquadFilter();
    rlp.type = 'lowpass';
    rlp.frequency.value = 7000;
    this.rainGain = ctx.createGain();
    this.rainGain.gain.value = 0;
    rain.connect(hp).connect(rlp).connect(this.rainGain).connect(this.master);
    rain.start();
  }

  /** 0..1 amount of rain. */
  setRain(level: number): void {
    this.rainLevel = level;
    if (this.ctx && this.rainGain) this.rainGain.gain.setTargetAtTime(level * 0.07 * this.ambScale, this.ctx.currentTime, 0.5);
  }

  /** The dog's bark, louder the closer it is. */
  bark(distance: number): void {
    if (!this.ctx) return;
    const near = Math.max(0, 1 - distance / 110);
    const vol = 0.02 + 0.2 * near * near;
    for (let i = 0; i < 2; i++) {
      const w = i * 0.24;
      this.tone(520 - i * 40, 0.13, 'sawtooth', vol, w, 300);
      this.noise(0.1, vol * 1.4, 'bandpass', 1000, 1.5, w, 520);
    }
  }

  /** Packing wet sand into a bucket and tipping it out. */
  sandBuild(): void {
    this.noise(0.22, 0.2, 'bandpass', 900, 1.2);
    this.noise(0.1, 0.16, 'lowpass', 400, 1, 0.16);
    this.tone(110, 0.14, 'sine', 0.14, 0.12, 70);
  }

  /** A sandcastle slumping into the sand. */
  crumble(): void {
    this.noise(0.5, 0.18, 'bandpass', 700, 0.9, 0, 250);
    this.noise(0.3, 0.12, 'lowpass', 300, 1, 0.12);
  }

  /** A happy little yip. */
  yip(): void {
    this.tone(820, 0.1, 'triangle', 0.1, 0, 1250);
    this.tone(980, 0.09, 'triangle', 0.08, 0.14, 1400);
  }

  setOceanLevel(v: number): void {
    this.lastOcean = v;
    if (this.ctx) this.oceanGain.gain.setTargetAtTime(v * this.ambScale, this.ctx.currentTime, 0.4);
  }

  /** The three volume sliders from the settings screen, each 0..1. */
  setVolumes(master: number, music: number, ambience: number): void {
    this.masterVol = master;
    this.musicScale = music;
    this.ambScale = ambience;
    if (!this.ctx) return;
    const t = this.ctx.currentTime;
    this.master.gain.setTargetAtTime(this.muted ? 0 : 0.8 * master, t, 0.05);
    this.oceanGain.gain.setTargetAtTime(this.lastOcean * ambience, t, 0.1);
    this.rainGain?.gain.setTargetAtTime(this.rainLevel * 0.07 * ambience, t, 0.1);
    this.setMusicLevel(this.musicLevel, this.musicDuck);
  }

  toggleMute(): boolean {
    this.muted = !this.muted;
    if (this.ctx) this.master.gain.setTargetAtTime(this.muted ? 0 : 0.8 * this.masterVol, this.ctx.currentTime, 0.05);
    return this.muted;
  }

  private tone(freq: number, dur: number, type: OscillatorType, vol: number, when = 0, slideTo?: number): void {
    const ctx = this.ctx;
    if (!ctx) return;
    const t = ctx.currentTime + when;
    const o = ctx.createOscillator();
    const g = ctx.createGain();
    o.type = type;
    o.frequency.setValueAtTime(freq, t);
    if (slideTo) o.frequency.exponentialRampToValueAtTime(slideTo, t + dur);
    g.gain.setValueAtTime(0.0001, t);
    g.gain.exponentialRampToValueAtTime(vol, t + 0.008);
    g.gain.exponentialRampToValueAtTime(0.0001, t + dur);
    o.connect(g).connect(this.master);
    o.start(t);
    o.stop(t + dur + 0.05);
  }

  private noise(dur: number, vol: number, filter: BiquadFilterType, freq: number, q = 1, when = 0, freqEnd?: number): void {
    const ctx = this.ctx;
    if (!ctx) return;
    const t = ctx.currentTime + when;
    const src = ctx.createBufferSource();
    src.buffer = this.noiseBuf;
    src.loop = true;
    const f = ctx.createBiquadFilter();
    f.type = filter;
    f.frequency.setValueAtTime(freq, t);
    if (freqEnd) f.frequency.exponentialRampToValueAtTime(freqEnd, t + dur);
    f.Q.value = q;
    const g = ctx.createGain();
    g.gain.setValueAtTime(0.0001, t);
    g.gain.exponentialRampToValueAtTime(vol, t + Math.min(0.03, dur / 3));
    g.gain.exponentialRampToValueAtTime(0.0001, t + dur);
    src.connect(f).connect(g).connect(this.master);
    src.start(t, Math.random());
    src.stop(t + dur + 0.05);
  }

  /** Detector beep. strength 0..1 raises pitch as the coil nears a target. */
  ping(kind: PingKind, strength: number): void {
    if (!this.detectorEnabled) return;
    const s = Math.min(1, Math.max(0, strength));
    const dur = 0.06;
    switch (kind) {
      case 'trash':
        this.tone(110 + s * 90, dur * 1.6, 'sawtooth', 0.14);
        this.tone(118 + s * 95, dur * 1.6, 'square', 0.05);
        break;
      case 'coin':
        this.tone(520 + s * 700, dur * 2.4, 'triangle', 0.2);
        this.tone((520 + s * 700) * 2, dur * 2, 'sine', 0.06);
        break;
      case 'deep': {
        const f = 330 + s * 450;
        this.tone(f, dur * 2.6, 'sine', 0.2);
        this.tone(f * 1.5, dur * 2.6, 'sine', 0.12, 0.012);
        break;
      }
      default:
        this.tone(300 + s * 600, dur * 1.5, 'triangle', 0.18);
    }
  }

  /** A seagull call; `distance` in metres sets how loud and muffled it is. */
  gull(distance: number): void {
    if (!this.ctx) return;
    const ctx = this.ctx;
    const near = Math.max(0, 1 - distance / 70);
    if (near <= 0.02) return;
    const vol = 0.05 * near * near + 0.004;
    const base = 1500 + Math.random() * 300;
    const t0 = ctx.currentTime + Math.random() * 0.05;
    const calls = 2 + Math.floor(Math.random() * 2);
    for (let i = 0; i < calls; i++) {
      const at = t0 + i * 0.22;
      const o = ctx.createOscillator();
      const g = ctx.createGain();
      const f = ctx.createBiquadFilter();
      o.type = 'sawtooth';
      o.frequency.setValueAtTime(base * 0.8, at);
      o.frequency.linearRampToValueAtTime(base * 1.25, at + 0.07);
      o.frequency.exponentialRampToValueAtTime(base * 0.7, at + 0.2);
      f.type = 'bandpass';
      f.frequency.value = base * 1.1;
      f.Q.value = 3;
      g.gain.setValueAtTime(0.0001, at);
      g.gain.exponentialRampToValueAtTime(vol, at + 0.03);
      g.gain.exponentialRampToValueAtTime(0.0001, at + 0.22);
      o.connect(f).connect(g).connect(this.master);
      o.start(at);
      o.stop(at + 0.26);
    }
  }

  /** Whirr of the cart's electric motor plus tyre noise on sand. level 0..1 is the speed. */
  setEngine(on: boolean, level = 0, boat = false): void {
    const ctx = this.ctx;
    if (!ctx) return;
    if (on && !this.engine) {
      const osc = ctx.createOscillator();
      const osc2 = ctx.createOscillator();
      osc.type = 'sawtooth';
      osc2.type = 'sine';
      const lp = ctx.createBiquadFilter();
      lp.type = 'lowpass';
      lp.frequency.value = 900;
      const gain = ctx.createGain();
      gain.gain.value = 0;
      osc.connect(lp);
      osc2.connect(lp);
      lp.connect(gain).connect(this.master);
      const src = ctx.createBufferSource();
      src.buffer = this.noiseBuf;
      src.loop = true;
      const bp = ctx.createBiquadFilter();
      bp.type = 'bandpass';
      bp.frequency.value = 600;
      const noise = ctx.createGain();
      noise.gain.value = 0;
      src.connect(bp).connect(noise).connect(this.master);
      osc.start();
      osc2.start();
      src.start();
      this.engine = { osc, osc2, gain, noise, src };
    }
    const e = this.engine;
    if (!e) return;
    const t = ctx.currentTime;
    if (!on) {
      e.gain.gain.setTargetAtTime(0, t, 0.1);
      e.noise.gain.setTargetAtTime(0, t, 0.1);
      const dead = e;
      this.engine = null;
      setTimeout(() => {
        dead.osc.stop();
        dead.osc2.stop();
        dead.src.stop();
      }, 600);
      return;
    }
    if (boat) {
      // a deeper outboard rumble and the rush of water along the hull
      e.osc.frequency.setTargetAtTime(38 + level * 95, t, 0.1);
      e.osc2.frequency.setTargetAtTime(76 + level * 190, t, 0.1);
      e.gain.gain.setTargetAtTime(0.05 + level * 0.07, t, 0.1);
      e.noise.gain.setTargetAtTime(0.02 + level * 0.14, t, 0.1);
      return;
    }
    e.osc.frequency.setTargetAtTime(70 + level * 190, t, 0.08);
    e.osc2.frequency.setTargetAtTime(140 + level * 380, t, 0.08);
    e.gain.gain.setTargetAtTime(0.035 + level * 0.05, t, 0.1);
    e.noise.gain.setTargetAtTime(level * 0.09, t, 0.1);
  }

  thud(): void {
    this.noise(0.18, 0.5, 'lowpass', 260, 1);
    this.tone(90, 0.2, 'sine', 0.35, 0, 40);
  }

  /** A fish leaping or a shark diving; `distance` in metres sets how loud it is. */
  splash(distance: number): void {
    if (!this.ctx) return;
    const near = Math.max(0, 1 - distance / 90);
    if (near <= 0.02) return;
    this.noise(0.35, 0.22 * near * near + 0.01, 'bandpass', 1100 + Math.random() * 600, 0.8, 0, 500);
  }

  /** The creak of a bow being drawn. */
  bowDraw(): void {
    this.noise(0.5, 0.1, 'bandpass', 260, 2, 0, 520);
    this.tone(70, 0.5, 'sawtooth', 0.025, 0, 120);
  }

  /** The twang of a bowstring and the whistle of the arrow. */
  bowRelease(power: number): void {
    this.tone(420 + power * 160, 0.22, 'triangle', 0.22, 0, 150);
    this.tone(210, 0.3, 'sine', 0.15, 0, 90);
    this.noise(0.25, 0.12 + power * 0.1, 'bandpass', 1800, 1.2, 0.02, 700);
  }

  /** Pop of the toy pistol. */
  shot(): void {
    this.noise(0.14, 0.5, 'bandpass', 1600, 1.1);
    this.noise(0.06, 0.35, 'highpass', 3500, 1);
    this.tone(150, 0.16, 'sine', 0.45, 0, 45);
  }

  splat(): void {
    this.noise(0.18, 0.35, 'lowpass', 700, 1);
    this.tone(300, 0.12, 'triangle', 0.15, 0, 90);
  }

  crunch(): void {
    this.noise(0.22, 0.5, 'bandpass', 700, 0.9);
    this.noise(0.12, 0.35, 'lowpass', 400, 1, 0.03);
  }

  /** Soft bristle noise burst; call often while scrubbing, it self-throttles. */
  brush(speed: number): void {
    const ctx = this.ctx;
    if (!ctx) return;
    const now = ctx.currentTime;
    if (now - this.lastBrush < 0.07) return;
    this.lastBrush = now;
    const v = Math.min(1, speed / 900);
    if (v < 0.03) return;
    this.noise(0.12, 0.03 + v * 0.22, 'bandpass', 800 + v * 800 + Math.random() * 200, 1.2);
  }

  ui(): void {
    this.tone(660, 0.07, 'sine', 0.1);
  }

  error(): void {
    this.tone(200, 0.18, 'sine', 0.12, 0, 150);
  }

  coin(): void {
    this.tone(1318, 0.12, 'triangle', 0.14);
    this.tone(1760, 0.22, 'triangle', 0.12, 0.07);
  }

  discover(): void {
    [523.25, 659.25, 783.99, 1046.5].forEach((f, i) => this.tone(f, 1.1 - i * 0.1, 'triangle', 0.14, i * 0.09));
    [261.6, 392].forEach((f) => this.tone(f, 1.4, 'sine', 0.1, 0.05));
  }

  sip(): void {
    this.noise(0.5, 0.18, 'bandpass', 500, 2, 0, 900);
    this.tone(392, 0.5, 'sine', 0.08, 0.1);
  }

  whoosh(dur = 3): void {
    this.noise(dur, 0.6, 'bandpass', 250, 0.7, 0, 1400);
    this.noise(dur * 0.8, 0.45, 'lowpass', 900, 0.5, dur * 0.35, 200);
  }

  private staticAcc = 0;

  /** Radio-style crackle while the coil sweeps; denser and louder as it nears a target. */
  detectorStatic(dt: number, strength: number, kind: PingKind | null): void {
    if (!this.ctx || !this.detectorEnabled) return;
    this.staticAcc += dt * (4 + strength * 12);
    const rough = kind === 'trash' ? 0.6 : 1;
    while (this.staticAcc >= 1) {
      this.staticAcc -= 1;
      const freq = (1800 + Math.random() * 3500) * rough;
      this.noise(0.015 + Math.random() * 0.03, (0.012 + strength * 0.04) * (0.5 + Math.random()), 'bandpass', freq, 1.5);
    }
  }

  /** Wood-pop of the campfire; `near` is 0..1 and sets how loud it is. */
  crackle(near = 1): void {
    const f = 700 + Math.random() * 1400;
    this.noise(0.02 + Math.random() * 0.05, 0.16 * near * near * (0.4 + Math.random()), 'bandpass', f, 0.8);
  }
}
