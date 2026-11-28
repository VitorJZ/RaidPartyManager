"""Render the original boss scores as stereo Vorbis loops (NumPy, SciPy, FFmpeg)."""

from dataclasses import dataclass
from pathlib import Path
import subprocess

import numpy as np
from scipy.signal import butter, sosfilt


RATE = 48000
TAU = 2 * np.pi
OUTPUT = Path(__file__).resolve().parents[1] / "Assets/RaidPartyManager/Audio/Resources/BossMusic"


@dataclass(frozen=True)
class Score:
    boss: str
    title: str
    tempo: int
    meter: int
    tonic: int
    progression: tuple
    melody: tuple
    lead: str
    answer: str
    harmony: str
    bass: str
    bass_steps: tuple
    drum_steps: tuple
    metal: str
    space: float


# Chords use intervals from tonic for the bass note, then semitone offsets from that note.
# Melodic phrases use (position in bar, interval from tonic, duration) in score beats.
SCORES = (
    Score("EnemyBlockadeAdmiral", "Cannonwatch", 124, 4, 50,
          ((0, 0, 3, 7), (-4, 0, 4, 7), (-2, 0, 4, 7), (-5, 0, 4, 7),
           (0, 0, 3, 7), (1, 0, 4, 7), (-4, 0, 4, 7), (-5, 0, 4, 7)),
          (((0, 12, .75), (1, 19, .68), (2, 20, .55), (2.75, 19, .25), (3, 15, .85)),
           ((0, 15, 1.1), (1.5, 17, .4), (2, 20, .55), (3, 19, .8)),
           ((0, 14, .8), (1, 17, .65), (2, 19, .8), (3, 22, .7)),
           ((0, 16, .85), (1, 19, .8), (2.5, 13, .4), (3, 11, .85))),
          "brass", "spiccato", "strings", "bass", (0, .75, 1.5, 2, 3, 3.5),
          (0, 1.5, 2, 3), "snare", .28),
    Score("EnemyDrownedLanternRay", "Light Below the Wake", 216, 6, 52,
          ((0, 0, 3, 7, 14), (1, 0, 4, 7), (3, 0, 4, 7), (-5, 0, 3, 7, 10),
           (0, 0, 3, 7, 14), (-2, 0, 3, 7), (-4, 0, 4, 7), (-1, 0, 3, 6)),
          (((0, 19, 2.15), (2.4, 22, .6), (3.15, 17, 2.55)),
           ((0, 13, 1.7), (2, 17, .7), (3.25, 20, 2.6)),
           ((0, 15, 1.4), (1.9, 19, .7), (3.05, 27, 2.5)),
           ((0, 24, 2.5), (3.2, 22, 1.0), (4.4, 19, 1.3))),
          "glass", "flute", "choir", "sub", (0, 3.5, 5), (0, 3), "gong", .60),
    Score("EnemyNamelessKing", "The Unnamed Sentence", 108, 4, 49,
          ((0, 0, 3, 7), (-3, 0, 3, 7), (-4, 0, 4, 7), (-5, 0, 4, 7),
           (0, 0, 3, 7), (1, 0, 4, 7), (3, 0, 4, 7), (-5, 0, 4, 7)),
          (((0, 12, 1.45), (1.5, 11, .45), (2, 15, 1.6)),
           ((0, 21, 1.35), (1.5, 17, .48), (2.25, 14, 1.35)),
           ((0, 20, 1.1), (1.5, 19, .45), (2.1, 15, 1.6)),
           ((0, 14, .85), (1, 16, .8), (2, 19, .55), (3, 11, .86))),
          "organ", "brass", "choir", "sub", (0, 1.75, 2.5, 3.5), (0, 2.5, 3.5), "bell", .52),
    Score("EnemySepulchralMatron", "Vigil of Ash", 114, 3, 55,
          ((0, 0, 3, 7), (-4, 0, 4, 7), (-2, 0, 4, 7), (-5, 0, 4, 7),
           (5, 0, 3, 7), (3, 0, 4, 7), (-4, 0, 4, 7), (-5, 0, 4, 7)),
          (((0, 15, .82), (1, 19, .43), (1.5, 22, .43), (2, 24, .85)),
           ((0, 22, 1.35), (1.5, 17, .42), (2, 15, .83)),
           ((0, 17, .83), (1, 19, .44), (1.5, 24, .44), (2, 26, .85)),
           ((0, 24, .85), (1, 19, .83), (2, 14, .91))),
          "keys", "flute", "choir", "bass", (0, 1.5, 2), (0, 2), "censer", .58),
    Score("EnemyRoyalEngine", "No Station Beyond", 146, 4, 53,
          ((0, 0, 3, 7), (3, 0, 4, 7), (-2, 0, 4, 7), (1, 0, 4, 7),
           (0, 0, 3, 7), (-4, 0, 4, 7), (5, 0, 3, 7), (-5, 0, 4, 7)),
          (((0, 12, .42), (.5, 12, .4), (1, 19, .82), (2, 15, .4), (2.75, 12, .35), (3.25, 13, .63)),
           ((0, 15, .4), (.5, 15, .4), (1.25, 22, .56), (2, 24, .8), (3, 22, .8)),
           ((0, 17, .4), (.5, 17, .4), (1.25, 19, .55), (2, 22, .9), (3.25, 20, .65)),
           ((0, 20, .45), (.5, 21, .45), (1, 20, .52), (1.75, 17, .55), (2.5, 13, .42), (3, 11, .82))),
          "brass", "spiccato", "organ", "motor", (0, .5, .75, 1.5, 2, 2.5, 3, 3.75),
          (0, 1, 2, 2.75, 3.5), "steam", .23),
    Score("EnemyAstralGovernor", "The Fault in the Meridian", 238, 7, 47,
          ((0, 0, 3, 7, 14), (3, 0, 4, 7, 11), (-2, 0, 4, 7), (6, 0, 3, 6),
           (0, 0, 3, 7, 14), (-4, 0, 4, 7, 11), (5, 0, 3, 7), (-5, 0, 4, 7)),
          (((0, 24, .8), (1, 19, .7), (2, 26, .8), (3, 30, 1.75), (5.3, 25, 1.3)),
           ((0, 27, 1.65), (2, 26, .75), (3, 24, 1.65), (5, 19, 1.7)),
           ((0, 22, .8), (1, 26, .8), (2, 31, .8), (3, 29, 1.6), (5, 26, 1.7)),
           ((0, 30, 1.5), (1.8, 25, 1.1), (3, 23, .76), (4, 18, .85), (5, 23, 1.7))),
          "glass", "keys", "strings", "sub", (0, 3, 5, 6.5), (0, 3, 5), "clock", .47),
    Score("EnemyCrucibleRegent", "Redline Crucible", 136, 4, 56,
          ((0, 0, 3, 7), (1, 0, 4, 7), (0, 0, 3, 7), (6, 0, 3, 6),
           (0, 0, 3, 7), (5, 0, 3, 7), (3, 0, 4, 7), (-5, 0, 4, 7)),
          (((0, 12, .42), (.5, 12, .44), (1, 18, .8), (2, 19, .38), (2.75, 18, .4), (3.25, 15, .67)),
           ((0, 13, .78), (1, 17, .45), (1.5, 20, .42), (2, 19, 1.52)),
           ((0, 12, .4), (.75, 15, .5), (1.5, 19, .42), (2, 22, .8), (3, 18, .78)),
           ((0, 18, .75), (1, 21, .8), (2, 18, .4), (2.5, 15, .43), (3.25, 11, .67))),
          "spiccato", "brass", "strings", "motor", (0, .75, 1.25, 2, 2.5, 3.25, 3.75),
          (0, .75, 1.5, 2, 2.5, 3.25), "anvil", .34),
)


def envelope(t, length, attack, release):
    return np.sin(np.clip(t / attack, 0, 1) * np.pi / 2) ** 2 * np.clip(
        (length + release - t) / release, 0, 1
    ) ** 2


def partials(phase, gains):
    return sum(gain * np.sin(phase * index) for index, gain in enumerate(gains, 1))


def instrument(voice, pitch, length, rng):
    attack, release = {
        "strings": (.102, .25), "choir": (.16, .36), "brass": (.046, .13), "organ": (.027, .19),
        "spiccato": (.021, .09), "keys": (.008, .19), "flute": (.059, .22), "glass": (.012, .35),
        "pluck": (.006, .16), "reed": (.045, .145),
        "bass": (.012, .074), "sub": (.043, .145), "motor": (.010, .085),
    }[voice]
    t = np.arange(int((length + release) * RATE)) / RATE
    freq = 440 * 2 ** ((pitch - 69) / 12)
    p = TAU * freq * t
    vibrato = .16 * np.sin(TAU * (4.6 + rng.uniform(-.5, .5)) * t) * np.minimum(1, t / .31)
    if voice in ("strings", "spiccato"):
        layer = (.5 * partials(p * .997 + vibrato, (1, .39, .28, .19, .10, .054)) +
                 .5 * partials(p * 1.003 - vibrato * .8, (1, .41, .19, .12, .07)))
        if voice == "spiccato":
            layer *= .38 + .62 * np.exp(-t * 9)
    elif voice == "choir":
        layer = .60 * partials(p + vibrato, (.7, .16, .33, .09, .05)) + .40 * partials(
            p * 1.005, (.8, .22, .37, .11))
    elif voice == "brass":
        color = .84 * (1 - np.exp(-t * 21)) * (.8 + .2 * np.exp(-t * 6))
        layer = np.sin(p + vibrato + color * np.sin(p)) + .20 * np.sin(2*p) + .07 * np.sin(3*p)
    elif voice == "organ":
        layer = partials(p, (.84, .32, .24, .14, .10, .075)) + .075 * np.sin(p * 2.004)
    elif voice == "flute":
        layer = partials(p + vibrato * 1.27, (1, .15, .075)) * (.92 + .08 * np.sin(TAU * 6.2 * t))
    elif voice == "keys":
        layer = (partials(p, (1, .39, .19, .085)) * np.exp(-t * 2.5) +
                 .19 * np.sin(p * 3.012) * np.exp(-t * 14))
    elif voice == "glass":
        layer = (.87 * np.sin(p) * np.exp(-t * 2.3) +
                 .31 * np.sin(p * 2.006) * np.exp(-t * 5.2) +
                 .10 * np.sin(p * 2.731) * np.exp(-t * 9.5))
    elif voice == "pluck":
        layer = (.88 * np.sin(p) * np.exp(-t * 4.2) +
                 .42 * np.sin(2 * p) * np.exp(-t * 8) +
                 .22 * np.sin(3 * p) * np.exp(-t * 11) +
                 .11 * np.sin(5 * p) * np.exp(-t * 17))
    elif voice == "reed":
        layer = partials(p + vibrato * .6, (1, .28, .38, .07, .19, .06, .08)) * (
            .90 + .10 * np.sin(TAU * 5.6 * t))
    elif voice == "motor":
        layer = np.tanh(1.7 * partials(p, (1, .51, .21, .12, .09))) * (.68 + .32 * np.exp(-t * 22))
    elif voice == "bass":
        layer = partials(p, (1, .32, .16, .07)) * (.6 + .4 * np.exp(-t * 16))
    else:
        layer = partials(p, (1, .24, .07))
    return envelope(t, length, attack, release) * layer


def percussion(kind, rng):
    duration = {"kick": .38, "snare": .28, "gong": 1.35, "bell": 1.70, "censer": .40,
                "steam": .21, "clock": .17, "anvil": .63}[kind]
    t = np.arange(int(duration * RATE)) / RATE
    if kind == "kick":
        p = TAU * (49 * t + 49 * (1 - np.exp(-43*t)) / 43)
        sound = np.sin(p) * np.exp(-t * 12)
    else:
        color = {"snare": (1220, 6300, 15), "gong": (400, 3100, 5), "bell": (640, 3500, 8),
                 "censer": (1900, 7200, 11), "steam": (1200, 5900, 19), "clock": (2300, 7800, 29),
                 "anvil": (650, 4800, 11)}[kind]
        noise = sosfilt(butter(2, color[:2], btype="bandpass", fs=RATE, output="sos"), rng.standard_normal(len(t)))
        sound = noise * np.exp(-t * color[2]) * .68
        resonances = {
            "snare": (178, 303), "gong": (103, 184, 276, 392, 591),
            "bell": (87.3, 176.8, 244, 388, 572), "censer": (568, 799, 1247, 2010),
            "steam": (232, 363), "clock": (849, 1315, 2077), "anvil": (172, 237, 389, 612, 945),
        }[kind]
        for i, frequency in enumerate(resonances):
            sound += .33 / (1 + i * .58) * np.sin(TAU * frequency * t) * np.exp(
                -t * (2 + color[2] * .4 + i * 2.5))
    return sound * envelope(t, duration * .75, .004, duration * .25)


class Mix:
    def __init__(self, score, seed, bars=32):
        self.beat = 60 / score.tempo
        self.samples = round(RATE * self.beat * score.meter * bars)
        self.buffer = np.zeros((self.samples, 2), dtype=np.float32)
        self.rng = np.random.default_rng(seed)

    def add(self, position, audio, gain, pan):
        begin = round(position * self.beat * RATE) % self.samples
        angle = (pan + 1) * np.pi / 4
        stereo = (audio[:, None] * np.array([np.cos(angle), np.sin(angle)]) * gain).astype(np.float32)
        head = min(len(stereo), self.samples - begin)
        self.buffer[begin:begin + head] += stereo[:head]
        self.buffer[:len(stereo) - head] += stereo[head:]

    def note(self, position, voice, pitch, beats, gain, pan=0):
        self.add(position, instrument(voice, pitch, beats * self.beat, self.rng), gain, pan)

    def hit(self, position, kind, gain, pan=0):
        self.add(position, percussion(kind, self.rng), gain, pan)

    def master(self, space, target_rms=.127):
        dry = self.buffer.copy()
        # Circular reflections preserve the reverb tail across the loop boundary.
        for delay, gain in ((.043, .21), (.079, .16), (.131, .13), (.193, .09), (.277, .073),
                            (.359, .050), (.523, .030), (.733, .017)):
            self.buffer += np.roll(dry[:, ::-1], round(delay * RATE), axis=0) * (gain * space)
        self.buffer -= self.buffer.mean(axis=0)
        rms = np.sqrt(np.mean(self.buffer ** 2))
        leveled = np.tanh(self.buffer * (target_rms / rms) * 1.24) / 1.24
        peak = np.max(np.abs(leveled))
        return leveled * min(1, .74 / peak)


def arrange(score, seed):
    mix = Mix(score, seed)
    for bar in range(32):
        start = bar * score.meter
        section = bar // 8
        root, *chord = score.progression[bar % len(score.progression)]
        if score.meter in (6, 7):
            accent = .97
            passing = (1, 2, 4, 5) if score.meter == 6 else (1, 2, 4, 6)
        else:
            accent = 1.05
            passing = (1, 1.5, 2.5) if score.meter == 3 else (.5, 1.5, 2.5, 3.5)

        for i, interval in enumerate(chord):
            pitch = score.tonic + root + interval + (12 if i == 0 else 0)
            mix.note(start + i * .032, score.harmony, pitch, score.meter - .32,
                     (.058 if section != 2 else .074) * accent, (-.52, .42, .18, -.32)[i])
        for i, step in enumerate(score.bass_steps):
            if section == 1 and i % 3 == 2:
                continue
            next_step = (score.bass_steps + (score.meter,))[i + 1]
            pitch = score.tonic + root - 12 + (7 if i % 4 == 3 else 0)
            mix.note(start + step, score.bass, pitch, max(.16, (next_step - step) * .79),
                     .16 if i == 0 else .102 + .017 * (section == 2))

        # The middle section hands the motif to a second instrument. The final reprise
        # answers the lead in the upper register before resolving into the loop pickup.
        voice = score.answer if section == 1 else score.lead
        if section != 1 or bar % 4 != 3:
            for j, (step, interval, length) in enumerate(score.melody[bar % 4]):
                if bar % 8 >= 4 and j == 0:
                    interval = root + 12 + chord[1]
                if bar == 31 and j == len(score.melody[bar % 4]) - 1:
                    interval = 11
                mix.note(start + step, voice, score.tonic + interval, length,
                         (.142 if j == 0 else .114) * (1.13 if section == 2 else 1), -.15)
                if section == 2 and j % 2 == 0:
                    mix.note(start + step + .045, score.answer, score.tonic + interval + 12,
                             length * .72, .052, .42)
        if section != 0 or bar % 2:
            for i, step in enumerate(passing):
                pitch = score.tonic + root + chord[(bar + i) % len(chord)] + 12
                mix.note(start + step, score.answer, pitch, .42 if score.meter < 6 else .67,
                         .047 if section != 2 else .072, .50 if i % 2 == 0 else -.39)

        for i, step in enumerate(score.drum_steps):
            if section == 1 and i > 0 and i % 2:
                continue
            mix.hit(start + step, "kick", (.145 if i == 0 else .083) * (1.22 if section == 2 else 1))
        metal_at = (score.meter / 2,) if score.metal in ("bell", "gong") else (1, score.meter - .5)
        for i, step in enumerate(metal_at):
            mix.hit(start + step, score.metal, (.139 if i == 0 else .086) * (1 if section != 1 else .74),
                    -.27 if i == 0 else .35)
        if bar % 8 == 7:
            mix.hit(start + score.meter - .32, "censer", .13, .51)
        if bar % 8 == 0:
            mix.hit(start, "gong", .18, -.37)
    return mix.master(score.space)


def write_loop(audio, target, title, quality=6):
    audio = np.asarray(audio, dtype="<f4")
    subprocess.run(
        ["ffmpeg", "-hide_banner", "-loglevel", "error", "-y", "-f", "f32le", "-ar", str(RATE),
         "-ac", "2", "-i", "-", "-c:a", "libvorbis", "-q:a", str(quality), "-metadata",
         "title=" + title, "-metadata", "artist=Raid Party Manager", str(target)],
        input=audio.tobytes(), check=True,
    )
    print(f"{target.stem}: {title}, {len(audio) / RATE:.2f} s, {target.stat().st_size:,} bytes", flush=True)


def main():
    OUTPUT.mkdir(parents=True, exist_ok=True)
    for i, score in enumerate(SCORES):
        write_loop(arrange(score, 14653 + i * 881), OUTPUT / (score.boss + ".ogg"), score.title)


if __name__ == "__main__":
    main()
