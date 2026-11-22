#!/usr/bin/env node
// tools/sfx/gen-sfx.js: procedural audio generator (Node, no dependencies).
// Writes 16-bit mono 22050 Hz WAV files to game/audio/sfx/ (one per SoundId) and game/audio/music/.
// Fully deterministic (seeded PRNG), so re-running reproduces identical files.
// Sound names are read from the SoundId enum in src/Artillery.Core/Match/GameEvents.cs;
// file name = snake_case(enum name) + ".wav" (ExplosionBig -> explosion_big.wav).
// Usage: node tools/sfx/gen-sfx.js
'use strict';
const fs = require('fs');
const path = require('path');

const ROOT = path.resolve(__dirname, '..', '..');
const OUT = path.join(ROOT, 'game', 'audio');
const SR = 22050;
const TAU = Math.PI * 2;

// ---------- primitives ----------
function rng(seed) {
  let s = (seed >>> 0) || 1; // xorshift32
  return () => { s ^= s << 13; s ^= s >>> 17; s ^= s << 5; return (s >>> 0) / 4294967296; };
}
const clamp = (x, a, b) => Math.min(b, Math.max(a, x));
const snake = (s) => s.replace(/([a-z0-9])([A-Z])/g, '$1_$2').toLowerCase();

// One-pole lowpass with a per-sample cutoff (Hz): step(x, fc) -> y.
function lowpass() {
  let y = 0;
  return (x, fc) => { y += (1 - Math.exp(-TAU * fc / SR)) * (x - y); return y; };
}
// Chamberlin state-variable filter; returns band-pass output: step(x, fc, damping).
function bandpass() {
  let low = 0, band = 0;
  return (x, fc, damp) => {
    const f = 2 * Math.sin(Math.PI * clamp(fc, 20, SR / 6) / SR);
    const high = x - low - damp * band;
    band += f * high;
    low += f * band;
    return band;
  };
}
// Synthesize `dur` seconds; fn(t, i) returns the sample at time t (index i).
function build(dur, fn) {
  const out = new Float32Array(Math.round(dur * SR));
  for (let i = 0; i < out.length; i++) out[i] = fn(i / SR, i);
  return out;
}
// Add a tone into buf at `start` seconds. wave: sin | tri | sq | bell. decay: optional exp decay (s).
function note(buf, f, start, len, amp, { wave = 'sin', atk = 0.005, rel = 0.2, decay = 0 } = {}) {
  const s0 = Math.round(start * SR), n = Math.round(len * SR);
  for (let i = 0; i < n && s0 + i < buf.length; i++) {
    const t = i / SR, p = TAU * f * t;
    let v;
    if (wave === 'tri') v = (2 / Math.PI) * Math.asin(Math.sin(p));
    else if (wave === 'bell') v = Math.sin(p) + 0.35 * Math.sin(2 * p) + 0.12 * Math.sin(3.01 * p);
    else if (wave === 'sq') v = 0.6 * Math.sign(Math.sin(p)) + 0.4 * Math.sin(p);
    else v = Math.sin(p);
    const env = Math.min(1, t / atk) * Math.min(1, Math.max(0, (len - t) / rel)) * (decay ? Math.exp(-t / decay) : 1);
    buf[s0 + i] += v * amp * env;
  }
}
function mixAt(dst, src, start, gain = 1) {
  const s0 = Math.round(start * SR);
  for (let i = 0; i < src.length && s0 + i < dst.length; i++) dst[s0 + i] += src[i] * gain;
}
// Normalize to `peak` (no clipping). One-shots get 2 ms fades to avoid clicks.
function finish(s, peak = 0.85, oneShot = true) {
  if (oneShot) {
    const f = Math.round(0.002 * SR);
    for (let i = 0; i < f && i < s.length; i++) { s[i] *= i / f; s[s.length - 1 - i] *= i / f; }
  }
  let max = 0;
  for (const x of s) max = Math.max(max, Math.abs(x));
  const g = max > 0 ? peak / max : 1;
  for (let i = 0; i < s.length; i++) s[i] *= g;
  return s;
}
// Loops: synthesize 2T and keep the second copy. Everything is periodic in T (integer cycles per
// loop), so the seam is continuous and filter state has settled by the time the kept copy starts.
function loopBuild(T, fn) {
  return build(2 * T, fn).subarray(Math.round(T * SR));
}

// ---------- sound designs ----------
const EXPLOSION = {
  small:  { dur: 0.6, tau: 0.18, cut: 800, rumble: 0.3,  rTau: 0.3, hz: [90, 50], tail: 0 },
  medium: { dur: 1.0, tau: 0.28, cut: 600, rumble: 0.45, rTau: 0.5, hz: [70, 40], tail: 0.3 },
  big:    { dur: 1.6, tau: 0.40, cut: 450, rumble: 0.6,  rTau: 0.8, hz: [60, 32], tail: 0.5 },
  huge:   { dur: 2.4, tau: 0.60, cut: 350, rumble: 0.75, rTau: 1.2, hz: [50, 28], tail: 0.8 },
  nuke:   { dur: 3.6, tau: 0.90, cut: 250, rumble: 0.9,  rTau: 1.8, hz: [40, 22], tail: 1.2 },
};
function explosion(size, seed) {
  const p = EXPLOSION[size], r = rng(seed), lpA = lowpass(), lpB = lowpass();
  let ph = 0;
  return build(p.dur, (t) => {
    const bright = lpA(r() * 2 - 1, p.cut * (1 + 3 * Math.exp(-t / 0.25))); // darkens as it decays
    let v = bright * Math.min(1, t / 0.003) * Math.exp(-t / p.tau);
    if (p.tail) v += lpB(r() * 2 - 1, 250) * Math.min(1, t / 0.02) * Math.exp(-t / p.tail) * 0.8;
    ph += TAU * (p.hz[1] + (p.hz[0] - p.hz[1]) * Math.exp(-t * 4)) / SR;
    v += Math.sin(ph) * p.rumble * Math.min(1, t / 0.01) * Math.exp(-t / p.rTau);
    return v;
  });
}
// Launch whoosh: rising band-pass noise + pitch-dropping thump. level 0 = light, 1 = heavy.
function launch(level, seed) {
  const dur = 0.8 + 0.6 * level, r = rng(seed), bp = bandpass();
  let ph = 0;
  return build(dur, (t) => {
    const u = t / dur;
    const fc = 300 + (1700 + 800 * level) * u * u;
    const whoosh = bp(r() * 2 - 1, fc, 1.2) * 3 * Math.min(1, t / 0.03) * Math.exp(-t / (0.35 + 0.3 * level));
    ph += TAU * (50 + 120 * level + 40 * Math.exp(-t * 12)) / SR;
    const thump = Math.sin(ph) * Math.exp(-t / 0.08) * (0.6 + 0.4 * level);
    return whoosh + thump * 0.9;
  });
}
function fire(seed) {
  const r = rng(seed), bed = lowpass();
  let age = 1e9, amp = 0;
  return build(1.6, (t) => {
    if (r() < 0.004) { age = 0; amp = (r() * 2 - 1) * (0.4 + 0.6 * r()); } // crackle pops
    age++;
    const pop = amp * Math.exp(-age / (0.0015 * SR)) * (0.5 + 0.5 * r());
    const roar = bed(r() * 2 - 1, 500) * 0.5 * (0.7 + 0.3 * Math.sin(TAU * 2 * t));
    return (pop + roar) * Math.min(1, (1.6 - t) / 0.3);
  });
}
function goo(seed) {
  const r = rng(seed), wet = lowpass();
  let p1 = 0, p2 = 0;
  return build(0.6, (t) => {
    p1 += TAU * (70 + 280 * Math.exp(-t * 18)) / SR;
    p2 += TAU * (200 + 700 * Math.exp(-t * 14)) / SR;
    return Math.sin(p1) * Math.exp(-t / 0.12)
      + Math.sin(p2) * Math.exp(-t / 0.1) * 0.35
      + wet(r() * 2 - 1, 1200) * Math.exp(-t / 0.05) * 0.6;
  });
}
function splash(seed) {
  const r = rng(seed), lp = lowpass();
  let bt = 1e9, bf = 0; // current bubble age (s) and frequency
  return build(0.9, (t) => {
    if (r() < 0.0015) { bt = 0; bf = 300 + 900 * r(); }
    bt += 1 / SR;
    const bubble = bt < 0.05 ? Math.sin(TAU * bf * bt) * Math.exp(-bt / 0.02) * 0.5 : 0;
    const n = r() * 2 - 1;
    const hiss = (n - lp(n, 800)) * 1.5 * Math.min(1, t / 0.005) * Math.exp(-t / 0.25);
    return hiss + bubble * Math.exp(-t / 0.4);
  });
}
function shockwave(seed) {
  const r = rng(seed), lp = lowpass();
  let ph = 0;
  return build(1.2, (t) => {
    ph += TAU * (35 + 125 * Math.exp(-t * 4)) / SR;
    return Math.sin(ph) * Math.min(1, t / 0.02) * Math.exp(-t / 0.5) * 0.9
      + lp(r() * 2 - 1, 300) * Math.min(1, t / 0.4) * Math.exp(-t / 0.6) * 0.5;
  });
}
function teleport() {
  let pc = 0, pm = 0;
  return build(1.0, (t) => {
    const u = t;
    const fc = 400 + 1400 * u;
    pc += TAU * fc / SR;
    pm += TAU * 2 * fc / SR;
    return Math.sin(pc + 3 * Math.sin(pm)) * Math.sin(Math.PI * u) ** 0.7 * 0.8;
  });
}
function rumble(seed) {
  const r = rng(seed), lp1 = lowpass(), lp2 = lowpass();
  let ph = 0;
  return build(3.0, (t) => {
    const env = Math.sin(Math.PI * t / 3) ** 0.5;
    ph += TAU * (38 + 4 * Math.sin(TAU * t)) / SR;
    const sub = Math.sin(ph) * 0.8;
    const grumble = lp1(r() * 2 - 1, 120) * 2.5 * (0.6 + 0.4 * Math.sin(TAU * 3.5 * t));
    const crumble = lp2(r() * 2 - 1, 500) * 0.2 * (0.5 + 0.5 * Math.sin(TAU * 7 * t));
    return (sub + grumble + crumble) * env;
  });
}
// Plane flyover: harmonic engine drone with a doppler pitch fall, plus air noise.
function airStrikePlane(seed) {
  const dur = 3.5, r = rng(seed), lp = lowpass();
  let ph = 0;
  return build(dur, (t) => {
    const u = t / dur;
    const dop = 1 + 0.4 * Math.tanh(4 * (0.5 - u));
    ph += TAU * 90 * dop / SR;
    let eng = 0;
    for (let h = 1; h <= 6; h++) eng += Math.sin(h * ph) / h;
    const passing = 0.5 + 0.5 * Math.exp(-(((u - 0.5) / 0.2) ** 2));
    const env = Math.min(1, t / 0.3) * Math.min(1, (dur - t) / 0.5) * passing;
    return (eng * 0.6 + lp(r() * 2 - 1, 700) * 0.4) * env;
  });
}
// Falling bomb whistle (doppler-like drop) ending in a small impact.
function airStrike(seed) {
  let ph = 0;
  const out = build(2.4, (t) => {
    ph += TAU * (250 + 1650 * Math.exp(-t * 1.3)) / SR;
    return Math.sin(ph) * 0.35 * Math.min(1, t / 0.05) * (1 - 0.5 * t / 2.4) * Math.min(1, Math.max(0, (2.0 - t) / 0.05));
  });
  mixAt(out, explosion('small', seed + 1), 2.0, 1.2);
  return out;
}
function beam() {
  let ph = 0;
  return build(0.6, (t) => {
    ph += TAU * (180 + 2200 * Math.exp(-t * 9)) * (1 + 0.04 * Math.sin(TAU * 40 * t)) / SR;
    return (Math.sin(ph) + 0.3 * Math.sin(3 * ph)) * Math.min(1, t / 0.005) * Math.exp(-t / 0.25);
  });
}
function countdown() {
  return build(0.15, (t) => (Math.sin(TAU * 880 * t) + 0.2 * Math.sin(TAU * 2640 * t))
    * Math.min(1, t / 0.005) * Math.min(1, (0.15 - t) / 0.02));
}
function victory() {
  const b = new Float32Array(Math.round(2.2 * SR));
  const arp = [523.25, 659.25, 783.99, 1046.5];
  arp.forEach((f, k) => note(b, f, k * 0.12, 0.6, 0.5, { wave: 'tri', rel: 0.3, decay: 0.5 }));
  arp.forEach((f) => note(b, f, 0.5, 1.7, 0.25, { wave: 'tri', atk: 0.02, rel: 0.8 }));
  return b;
}
function timeUp() {
  const b = new Float32Array(Math.round(1.0 * SR));
  [880, 660, 440, 220].forEach((f, k) => note(b, f, k * 0.22, 0.2, 0.5, { wave: 'sq', atk: 0.003, rel: 0.05 }));
  return b;
}
function uiClick(seed) {
  const r = rng(seed), hp = lowpass();
  return build(0.04, (t) => {
    const n = r() * 2 - 1;
    return (n - hp(n, 2500)) * Math.exp(-t / 0.006) * 0.8 + Math.sin(TAU * 1800 * t) * Math.exp(-t / 0.01) * 0.4;
  });
}
function button(seed) {
  const r = rng(seed);
  return build(0.12, (t) => Math.sin(TAU * 620 * t) * Math.min(1, t / 0.003) * Math.exp(-t / 0.05)
    + Math.sin(TAU * 930 * t) * 0.3 * Math.exp(-t / 0.03)
    + (r() * 2 - 1) * Math.exp(-t / 0.003) * 0.3);
}
function menuPad() {
  const dur = 3.5, b = new Float32Array(Math.round(dur * SR));
  [220, 261.63, 329.63, 392].forEach((f) => {
    note(b, f, 0, dur, 0.22, { atk: 0.8, rel: 1.2 });
    note(b, f * 1.004, 0, dur, 0.22, { atk: 0.8, rel: 1.2 });
  });
  return b;
}
function credits() {
  const b = new Float32Array(Math.round(2.6 * SR));
  [392, 493.88, 587.33, 783.99].forEach((f, k) => note(b, f, k * 0.18, 1.0, 0.3, { wave: 'bell', atk: 0.003, rel: 0.5, decay: 0.6 }));
  [392, 493.88, 587.33].forEach((f) => note(b, f, 0.75, 1.8, 0.2, { wave: 'bell', atk: 0.01, rel: 1.0, decay: 1.2 }));
  return b;
}
function scatter(seed) {
  const r = rng(seed), out = new Float32Array(Math.round(0.5 * SR));
  for (let k = 0; k < 8; k++) {
    const s0 = Math.round((k * 0.05 + r() * 0.02) * SR), amp = 0.5 + 0.5 * r(), f = 900 + 1200 * r();
    for (let i = 0; i < Math.round(0.15 * SR) && s0 + i < out.length; i++) {
      const t = i / SR;
      out[s0 + i] += ((r() * 2 - 1) * 0.7 + Math.sin(TAU * f * t) * 0.3) * amp * Math.exp(-t / 0.02);
    }
  }
  return out;
}
function shieldBreak(seed) {
  const r = rng(seed);
  let ph = 0;
  return build(0.9, (t) => {
    ph += TAU * (900 + 1700 * Math.exp(-t * 6)) / SR;
    const shimmer = Math.sin(TAU * 3900 * t) * (0.5 + 0.5 * Math.sin(TAU * 60 * t));
    return (Math.sin(ph) * 0.6 + shimmer * 0.3) * Math.exp(-t / 0.25) * Math.min(1, t / 0.002)
      + (r() * 2 - 1) * Math.exp(-t / 0.03) * 0.6;
  });
}

// ---------- music loops (seamless: all frequencies are integer cycles per loop) ----------
function menuLoop() {
  const T = 20;
  const q = (f) => Math.round(f * T) / T;
  const chordA = [110, 130.81, 164.81, 196, 246.94]; // Am9
  const chordB = [87.31, 110, 130.81, 164.81];       // Fmaj7
  return loopBuild(T, (t) => {
    const w = 0.5 + 0.5 * Math.cos(TAU * t / T); // periodic crossfade A <-> B
    const lfo = 0.8 + 0.2 * Math.sin(TAU * t / 10);
    let v = 0;
    for (const [amp, chord] of [[w, chordA], [1 - w, chordB]]) {
      for (const f of chord) {
        v += amp * (Math.sin(TAU * q(f) * t) * 0.5 + Math.sin(TAU * q(f * 1.004) * t) * 0.3
          + Math.sin(TAU * q(2 * f) * t) * 0.1);
      }
    }
    return v * lfo;
  });
}
function battleLoop() {
  const T = 30, r = rng(77), noiseTab = new Float32Array(SR), lp = lowpass();
  for (let i = 0; i < SR; i++) noiseTab[i] = r() * 2 - 1; // 1 s tile repeats each beat
  return loopBuild(T, (t, i) => {
    let drone = 0;
    for (let h = 1; h <= 5; h++) drone += Math.sin(TAU * 41.2 * h * t) / h; // 41.2 Hz * 30 s = integer
    drone += 0.5 * Math.sin(TAU * (1748 / 30) * t);                          // dissonant interval
    const pad = lp(drone, 200 + 150 * (0.5 + 0.5 * Math.cos(TAU * t / T))) * 3;
    const beat = (i % SR) / SR;                                              // 1 Hz heartbeat pulse
    const kick = Math.sin(TAU * (45 + 35 * Math.exp(-beat * 30)) * beat) * Math.exp(-beat / 0.12);
    const off = (((i + SR / 2) % SR) / SR);
    const tick = noiseTab[i % SR] * Math.exp(-off / 0.01) * 0.3;
    return pad * 0.6 + kick * 0.9 + tick;
  });
}

// ---------- registry ----------
// Every SoundId (except None) must have an entry; a missing one fails the build.
const SYNTH = {
  GooSplat: () => goo(1),
  LaunchHeavy: () => launch(1, 2),
  AirStrike: () => airStrike(3),
  LaunchLight: () => launch(0, 4),
  ExplosionNuke: () => explosion('nuke', 5),
  ExplosionMedium: () => explosion('medium', 6),
  Countdown: () => countdown(),
  Shockwave: () => shockwave(8),
  Victory: () => victory(),
  ExplosionBig: () => explosion('big', 11),
  ExplosionSmall: () => explosion('small', 12),
  ShieldBreak: () => shieldBreak(13),
  Splash: () => splash(14),
  Rumble: () => rumble(15),
  AirStrikePlane: () => airStrikePlane(16),
  Teleport: () => teleport(),
  Fire: () => fire(22),
  Scatter: () => scatter(23),
  UiClick: () => uiClick(25),
  Button: () => button(26),
  ExplosionHuge: () => explosion('huge', 27),
  Launch: () => launch(0.5, 28),
  TimeUp: () => timeUp(),
  Menu: () => menuPad(),
  Credits: () => credits(),
};
// Extra cue not in the SoundId enum (kept for presentation use; no enum value maps to it).
const EXTRA_SFX = { Beam: () => beam() };

function soundIdNames() {
  const src = fs.readFileSync(path.join(ROOT, 'src', 'Artillery.Core', 'Match', 'GameEvents.cs'), 'utf8');
  const body = src.slice(src.indexOf('enum SoundId'));
  const inner = body.slice(body.indexOf('{') + 1, body.indexOf('}'));
  return [...inner.matchAll(/\b([A-Z][A-Za-z]*)\s*=\s*\d+/g)].map((m) => m[1]).filter((n) => n !== 'None');
}

function writeWav(file, s) {
  const n = s.length, buf = Buffer.alloc(44 + n * 2);
  buf.write('RIFF', 0); buf.writeUInt32LE(36 + n * 2, 4); buf.write('WAVE', 8);
  buf.write('fmt ', 12); buf.writeUInt32LE(16, 16); buf.writeUInt16LE(1, 20); buf.writeUInt16LE(1, 22);
  buf.writeUInt32LE(SR, 24); buf.writeUInt32LE(SR * 2, 28); buf.writeUInt16LE(2, 32); buf.writeUInt16LE(16, 34);
  buf.write('data', 36); buf.writeUInt32LE(n * 2, 40);
  for (let i = 0; i < n; i++) buf.writeInt16LE(Math.round(clamp(s[i], -1, 1) * 32767), 44 + 2 * i);
  fs.writeFileSync(file, buf);
}

function main() {
  const sfxDir = path.join(OUT, 'sfx'), musicDir = path.join(OUT, 'music');
  fs.mkdirSync(sfxDir, { recursive: true });
  fs.mkdirSync(musicDir, { recursive: true });

  const names = soundIdNames();
  const missing = names.filter((n) => !SYNTH[n]);
  if (missing.length) throw new Error('No synth for SoundId: ' + missing.join(', '));

  const jobs = [
    ...names.map((n) => [path.join(sfxDir, snake(n) + '.wav'), SYNTH[n], true]),
    ...Object.entries(EXTRA_SFX).map(([n, fn]) => [path.join(sfxDir, snake(n) + '.wav'), fn, true]),
    [path.join(musicDir, 'menu_loop.wav'), menuLoop, false],
    [path.join(musicDir, 'battle_loop.wav'), battleLoop, false],
  ];
  for (const [file, fn, oneShot] of jobs) {
    const s = finish(fn(), oneShot ? 0.85 : 0.7, oneShot);
    writeWav(file, s);
    console.log(`${path.relative(ROOT, file)}  ${(s.length / SR).toFixed(2)}s`);
  }
  console.log(`Wrote ${jobs.length} files.`);
}

main();
