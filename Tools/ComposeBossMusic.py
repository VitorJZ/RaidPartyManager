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
class Groove:
    eighths: int
    kicks: tuple
    snares: tuple
    riff: tuple
    melody: tuple


GROOVES = {
    "march": Groove(8, (0, 1.5, 4, 5.5), (2, 6, 7.5),
                    (0, 1.5, 2, 3.5, 4.5, 5, 6, 7.5), (0, 2, 4.5, 6)),
    "half": Groove(8, (0, 2.5, 3.5, 5.5), (4, 7.5),
                   (0, 1.5, 2.5, 4, 5.5, 7.5), (0, 2.5, 5)),
    "double": Groove(8, (0, .5, 1, 2.5, 3.5, 4, 4.5, 5, 6.5, 7.5), (2, 6),
                     (0, 1, 1.5, 2, 3, 4, 5, 5.5, 6, 7), (0, 1, 2.5, 4, 5.5, 7)),
    "six": Groove(6, (0, 2, 3, 4.5), (3, 5.5),
                  (0, 1, 2, 3, 4, 5), (0, 1.5, 3, 5)),
    "seven": Groove(7, (0, 2, 4, 6.5), (2.5, 5),
                    (0, 1.5, 2, 3, 4, 5.5, 6.5), (0, 2, 3.5, 5, 6.5)),
    "five": Groove(10, (0, 1.5, 4, 6.5, 8), (2, 6, 9),
                    (0, 1, 2, 4, 5.5, 6, 7.5, 8, 9), (0, 2, 4.5, 6, 8)),
    "waltz": Groove(6, (0, 1.5, 4.5), (2, 4),
                    (0, 1.5, 2.5, 4, 5.5), (0, 2, 3.5, 4.5)),
}


@dataclass(frozen=True)
class Section:
    bars: int
    bpm: int
    groove: str
    lift: float

    @property
    def eighth(self):
        return 30 / self.bpm

    @property
    def length(self):
        return self.bars * GROOVES[self.groove].eighths * self.eighth


@dataclass(frozen=True)
class Score:
    boss: str
    title: str
    tonic: int
    chords: tuple
    motif: tuple
    voices: tuple
    metal: str
    sections: tuple
    space: float


# Each chord starts with the bass interval from tonic. The remaining intervals form
# the voicing above it. Motifs use semitone intervals from tonic across two bars.
SCORES = (
    Score("EnemyBlockadeAdmiral", "Broadside at Blackwater", 50,
          ((0, 0, 3, 7), (-2, 0, 4, 7), (-4, 0, 4, 7), (-5, 0, 4, 7),
           (0, 0, 3, 7), (1, 0, 4, 7), (-4, 0, 4, 7), (-5, 0, 4, 7)),
          (0, 7, 12, 10, 7, 15, 14, 7, 8, 3, 5, 11),
          ("brass", "reed", "spiccato", "strings"), "snare",
          (Section(8, 146, "march", .91), Section(6, 126, "half", .68),
           Section(8, 170, "double", 1.23), Section(8, 154, "six", .94)), .33),
    Score("EnemyDrownedLanternRay", "Riptide Ascendant", 52,
          ((0, 0, 3, 7, 14), (1, 0, 4, 7), (3, 0, 4, 7), (-1, 0, 3, 6),
           (0, 0, 3, 7), (-2, 0, 3, 7), (5, 0, 3, 7), (-5, 0, 4, 7)),
          (7, 13, 15, 12, 17, 19, 22, 20, 15, 13, 14, 11),
          ("glass", "flute", "spiccato", "choir"), "gong",
          (Section(8, 151, "six", .94), Section(8, 158, "seven", 1.03),
           Section(8, 127, "half", .66), Section(8, 172, "double", 1.21)), .55),
    Score("EnemyNamelessKing", "Rise of the Unnamed Crown", 49,
          ((0, 0, 3, 7), (3, 0, 4, 7), (-4, 0, 4, 7), (-5, 0, 4, 7),
           (0, 0, 3, 7), (1, 0, 4, 7), (5, 0, 3, 7), (-5, 0, 4, 7)),
          (0, 11, 12, 15, 19, 18, 17, 12, 15, 14, 8, 7),
          ("organ", "brass", "guitar", "choir"), "bell",
          (Section(8, 134, "half", .88), Section(8, 148, "five", 1.01),
           Section(8, 162, "double", 1.21), Section(6, 142, "march", .91)), .53),
    Score("EnemySepulchralMatron", "Vigil in Revolt", 55,
          ((0, 0, 3, 7), (-4, 0, 4, 7), (3, 0, 4, 7), (-5, 0, 4, 7),
           (5, 0, 3, 7), (8, 0, 4, 7), (-2, 0, 4, 7), (-5, 0, 4, 7)),
          (3, 7, 12, 14, 15, 19, 17, 12, 14, 10, 8, 11),
          ("keys", "brass", "spiccato", "choir"), "censer",
          (Section(12, 143, "waltz", .92), Section(8, 151, "six", .81),
           Section(8, 169, "double", 1.22), Section(8, 139, "waltz", .96)), .51),
    Score("EnemyRoyalEngine", "Runaway Imperial", 53,
          ((0, 0, 3, 7), (-2, 0, 4, 7), (1, 0, 4, 7), (3, 0, 4, 7),
           (0, 0, 3, 7), (5, 0, 3, 7), (6, 0, 3, 6), (-5, 0, 4, 7)),
          (0, 0, 7, 13, 12, 15, 12, 10, 7, 5, 6, 11),
          ("brass", "organ", "guitar", "strings"), "steam",
          (Section(8, 153, "march", .97), Section(8, 164, "seven", 1.04),
           Section(8, 182, "double", 1.27), Section(8, 139, "half", .74)), .25),
    Score("EnemyAstralGovernor", "Meridian Overdrive", 47,
          ((0, 0, 3, 7, 14), (6, 0, 3, 6), (-2, 0, 4, 7), (3, 0, 4, 7, 11),
           (0, 0, 3, 7), (-4, 0, 4, 7), (5, 0, 3, 7), (-5, 0, 4, 7)),
          (12, 7, 14, 18, 19, 15, 24, 22, 17, 18, 15, 11),
          ("glass", "brass", "guitar", "strings"), "clock",
          (Section(8, 154, "seven", .96), Section(8, 148, "five", .82),
           Section(8, 179, "six", 1.24), Section(8, 165, "double", 1.14)), .41),
    Score("EnemyCrucibleRegent", "Break the Furnace Crown", 56,
          ((0, 0, 3, 7), (1, 0, 4, 7), (6, 0, 3, 6), (3, 0, 3, 7),
           (0, 0, 3, 7), (5, 0, 3, 7), (-2, 0, 4, 7), (-5, 0, 4, 7)),
          (0, 6, 7, 3, 12, 13, 19, 18, 15, 10, 8, 11),
          ("brass", "organ", "guitar", "choir"), "anvil",
          (Section(8, 146, "five", 1.03), Section(8, 127, "half", .72),
           Section(8, 178, "double", 1.30), Section(8, 166, "seven", 1.04)), .31),
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
        "guitar": (.006, .074),
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
    elif voice == "guitar":
        string = partials(p, (1, .74, .46, .36, .23, .18, .11, .08))
        pickup = np.sin(p * 2.006) * .14 + np.sin(p * 3.017) * np.exp(-t * 25) * .15
        layer = np.tanh(2.6 * (string + pickup)) * (.37 + .63 * np.exp(-t * 11))
    elif voice == "motor":
        layer = np.tanh(1.7 * partials(p, (1, .51, .21, .12, .09))) * (.68 + .32 * np.exp(-t * 22))
    elif voice == "bass":
        layer = partials(p, (1, .32, .16, .07)) * (.6 + .4 * np.exp(-t * 16))
    else:
        layer = partials(p, (1, .24, .07))
    return envelope(t, length, attack, release) * layer


def percussion(kind, rng):
    duration = {"kick": .38, "tom": .56, "hat": .12, "crash": 1.18,
                "snare": .28, "gong": 1.35, "bell": 1.70, "censer": .40,
                "steam": .21, "clock": .17, "anvil": .63}[kind]
    t = np.arange(int(duration * RATE)) / RATE
    if kind == "kick":
        p = TAU * (49 * t + 49 * (1 - np.exp(-43*t)) / 43)
        sound = np.sin(p) * np.exp(-t * 12)
    elif kind == "tom":
        p = TAU * (71 * t + 38 * (1 - np.exp(-25*t)) / 25)
        sound = (.84 * np.sin(p) + .18 * np.sin(p * 1.53)) * np.exp(-t * 9)
    else:
        color = {"snare": (1220, 6300, 15), "gong": (400, 3100, 5), "bell": (640, 3500, 8),
                 "censer": (1900, 7200, 11), "steam": (1200, 5900, 19), "clock": (2300, 7800, 29),
                 "anvil": (650, 4800, 11), "hat": (4500, 10500, 54), "crash": (2200, 10200, 4.7)}[kind]
        noise = sosfilt(butter(2, color[:2], btype="bandpass", fs=RATE, output="sos"), rng.standard_normal(len(t)))
        sound = noise * np.exp(-t * color[2]) * .68
        resonances = {
            "snare": (178, 303), "gong": (103, 184, 276, 392, 591),
            "bell": (87.3, 176.8, 244, 388, 572), "censer": (568, 799, 1247, 2010),
            "steam": (232, 363), "clock": (849, 1315, 2077), "anvil": (172, 237, 389, 612, 945),
            "hat": (2377, 3613, 5879), "crash": (1117, 1697, 2687, 3709, 4787),
        }[kind]
        for i, frequency in enumerate(resonances):
            sound += .33 / (1 + i * .58) * np.sin(TAU * frequency * t) * np.exp(
                -t * (2 + color[2] * .4 + i * 2.5))
    return sound * envelope(t, duration * .75, .004, duration * .25)


class Mix:
    def __init__(self, beat, beats, seed):
        self.beat = beat
        self.samples = round(RATE * self.beat * beats)
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


def score_bar(mix, score, section, index, bar, start):
    groove = GROOVES[section.groove]
    end = groove.eighths
    eighth = section.eighth
    level = section.lift
    root, *chord = score.chords[(bar + index * 2) % len(score.chords)]
    lead, answer, riff, pad = score.voices
    bass = score.tonic + root - 12
    bridge = index > 0 and level < .85
    climax = level > 1.15

    for j, interval in enumerate(chord):
        mix.note(start + j * .018, pad, bass + interval + (24 if j == 0 else 12),
                 eighth * (end - .6), (.055 if bridge else .048) * level, (-.50, .44, -.21, .22)[j])

    for j, beat in enumerate(groove.riff):
        if bridge and j % 2 and j != len(groove.riff) - 1:
            continue
        gap = (groove.riff + (end,))[j + 1] - beat
        duration = eighth * gap * (.67 if climax else .83)
        position = start + eighth * beat
        pitch = bass + (chord[2] if j % 4 == 2 else 0)
        mix.note(position, "bass", pitch, duration, (.16 if j == 0 else .097) * level)
        mix.note(position, riff, pitch + 12, duration, .086 * level, -.52)
        if not bridge:
            mix.note(position + .009, riff, pitch + 12 + chord[2], duration, .055 * level, .54)

    for j, beat in enumerate(groove.melody):
        gap = (groove.melody + (end,))[j + 1] - beat
        pitch = score.tonic + score.motif[(bar * 3 + j + index * 2) % len(score.motif)]
        if j == 0 and bar % 2:
            pitch = bass + 24 + chord[1]
        if index == len(score.sections) - 1 and bar == section.bars - 1 and j == len(groove.melody) - 1:
            pitch = score.tonic + 11
        position = start + eighth * beat
        length = eighth * gap * (.76 if climax else .91)
        mix.note(position, answer if bridge else lead, pitch, length, (.161 if j == 0 else .111) * level, -.10)
        if climax and j % 2 == 0:
            mix.note(position + .023, answer, pitch + 12, length * .75, .062, .43)

    for j, beat in enumerate(groove.kicks):
        position = start + eighth * beat
        mix.hit(position, "kick", (.237 if j == 0 else .145) * level)
        if j == 0:
            mix.hit(position + .014, "tom", .146 * level, -.18)
    for beat in groove.snares:
        mix.hit(start + eighth * beat, "snare", .166 * level, .08)
    for beat in range(end):
        if bridge and beat % 2 == 0:
            continue
        mix.hit(start + eighth * beat, "hat", (.055 if beat % 2 else .078) * level, .48)
    mix.hit(start + eighth * (end - 1.5), score.metal, .146 * level, -.29)
    if bar == 0:
        mix.hit(start, "crash", .19 * level, .39)
    if bar % 4 == 3 or bar == section.bars - 1:
        for j in range(4):
            mix.hit(start + eighth * (end - 2 + j * .5), "tom", (.105 + .023 * j) * level, -.42 + .26 * j)


def arrange(score, seed):
    mix = Mix(1, sum(section.length for section in score.sections), seed)
    start = 0
    for index, section in enumerate(score.sections):
        bar_length = section.eighth * GROOVES[section.groove].eighths
        for bar in range(section.bars):
            score_bar(mix, score, section, index, bar, start + bar * bar_length)
        start += section.length
    return mix.master(score.space, target_rms=.137)


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
