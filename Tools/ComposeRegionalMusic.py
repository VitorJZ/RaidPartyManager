"""Render the 18 original regional scores as stereo loops (NumPy, SciPy, FFmpeg)."""

from dataclasses import dataclass
from pathlib import Path

from ComposeBossMusic import Mix, write_loop


OUTPUT = Path(__file__).resolve().parents[1] / "Assets/RaidPartyManager/Audio/Resources/RegionalMusic"
BARS = 24

# A chord is the interval from tonic to its root followed by intervals above that root.
MINOR = ((0, 0, 3, 7, 14), (-4, 0, 4, 7), (3, 0, 4, 7, 11), (-2, 0, 4, 7),
         (0, 0, 3, 7), (5, 0, 3, 7), (-4, 0, 4, 7), (-5, 0, 4, 7))
DARK = ((0, 0, 3, 7), (1, 0, 4, 7), (-2, 0, 3, 7), (0, 0, 3, 7),
        (-4, 0, 4, 7), (5, 0, 3, 7), (6, 0, 3, 6), (-5, 0, 4, 7))
MAJOR = ((0, 0, 4, 7, 14), (-3, 0, 3, 7), (5, 0, 4, 7, 11), (-5, 0, 4, 7),
         (0, 0, 4, 7), (2, 0, 3, 7), (5, 0, 4, 7), (-5, 0, 4, 7, 10))
MODAL = ((0, 0, 3, 7, 14), (-2, 0, 4, 7), (5, 0, 4, 7), (0, 0, 3, 7),
         (3, 0, 4, 7), (5, 0, 4, 7), (-2, 0, 4, 7), (0, 0, 3, 7))


@dataclass(frozen=True)
class Track:
    region: str
    setting: str
    title: str
    tempo: int
    meter: int
    tonic: int
    chords: tuple
    motif: tuple
    lead: str
    response: str
    harmony: str
    bass: str
    percussion: str
    space: float

    @property
    def id(self):
        return f"{self.region}-{self.setting}"


TRACKS = (
    Track("Dock", "Exploration", "Across the Mooring Lights", 87, 4, 50, MODAL,
          (7, 3, 5, 0, 2, 7, 9, 5), "flute", "pluck", "strings", "bass", "censer", .64),
    Track("Dock", "Battle", "Cut the Mooring Lines", 128, 4, 50, MINOR,
          (0, 7, 8, 7, 3, 5, 10, 7), "brass", "spiccato", "strings", "bass", "snare", .26),
    Track("Dock", "Town", "Lanterns on the Quay", 99, 4, 53, MAJOR,
          (4, 7, 9, 7, 5, 4, 2, 0), "keys", "reed", "strings", "bass", "censer", .31),
    Track("Dock", "Shop", "The Tackle Drawer", 106, 3, 57, MODAL,
          (0, 3, 7, 9, 7, 5, 3, 2), "pluck", "glass", "spiccato", "sub", "clock", .22),
    Track("Dock", "TalentMarket", "A Berth for Hire", 105, 4, 55, MODAL,
          (3, 3, 7, 9, 10, 9, 5, 7), "reed", "keys", "strings", "bass", "snare", .34),
    Track("Dock", "Camp", "One Lamp Left Afloat", 73, 4, 50, MINOR,
          (0, 3, 7, 5, 3, 2, 0, 7), "pluck", "flute", "strings", "sub", "censer", .66),
    Track("Graveyard", "Exploration", "Footprints Past the Yews", 74, 3, 48, DARK,
          (7, 6, 3, 1, 0, 3, 8, 7), "glass", "flute", "choir", "sub", "bell", .77),
    Track("Graveyard", "Battle", "The Earth Gives Way", 121, 4, 48, DARK,
          (0, 1, 6, 7, 3, 8, 5, 1), "organ", "spiccato", "choir", "motor", "bell", .53),
    Track("Graveyard", "Town", "Candles at the Gatehouse", 92, 4, 51, MAJOR,
          (7, 4, 5, 7, 2, 4, 9, 7), "reed", "keys", "choir", "sub", "censer", .54),
    Track("Graveyard", "Shop", "The Keepsake Cabinet", 96, 3, 55, MINOR,
          (7, 8, 3, 2, 0, 5, 7, 3), "glass", "pluck", "strings", "bass", "clock", .45),
    Track("Graveyard", "TalentMarket", "Names for the Crossing", 97, 4, 53, MINOR,
          (5, 3, 0, 7, 10, 8, 7, 5), "keys", "reed", "organ", "sub", "censer", .53),
    Track("Graveyard", "Camp", "The Quiet Watch", 68, 3, 46, MODAL,
          (3, 2, 0, 7, 5, 3, 2, 0), "flute", "glass", "choir", "sub", "bell", .75),
    Track("Capital", "Exploration", "An Hour Above the Rails", 103, 4, 52, MODAL,
          (0, 7, 9, 5, 7, 10, 3, 2), "glass", "keys", "strings", "motor", "clock", .45),
    Track("Capital", "Battle", "Platforms in Revolt", 226, 7, 52, MINOR,
          (0, 3, 7, 10, 9, 7, 5, 2), "spiccato", "brass", "strings", "motor", "anvil", .24),
    Track("Capital", "Town", "The Hour Opens", 114, 4, 55, MAJOR,
          (0, 4, 7, 11, 9, 7, 5, 7), "keys", "brass", "strings", "bass", "clock", .30),
    Track("Capital", "Shop", "A Counter Full of Gears", 116, 3, 50, MAJOR,
          (4, 7, 11, 9, 5, 7, 4, 2), "pluck", "keys", "spiccato", "bass", "clock", .24),
    Track("Capital", "TalentMarket", "Applications at Platform Four", 115, 4, 57, MAJOR,
          (7, 4, 2, 7, 9, 11, 7, 5), "brass", "reed", "strings", "bass", "steam", .30),
    Track("Capital", "Camp", "Warmth in the Service Alcove", 79, 4, 52, MINOR,
          (7, 5, 3, 2, 0, 3, 7, 8), "keys", "flute", "strings", "sub", "clock", .67),
)


@dataclass(frozen=True)
class Arrangement:
    melody: tuple
    bass: tuple
    accents: tuple
    backing: tuple
    strength: float


# Rhythms are fractions of a bar so the 3/4 and 7/8 scores use the same arrangement.
ARRANGEMENTS = {
    "Exploration": Arrangement((0, .375, .625), (0, .625), (.72,), (.25, .52, .84), .42),
    "Battle": Arrangement((0, .125, .375, .5, .75, .875), (0, .125, .375, .5, .625, .8125),
                          (0, .35, .5, .8), (.19, .47, .73, .92), 1.27),
    "Town": Arrangement((0, .25, .5, .75), (0, .5, .80), (.25, .75), (.125, .375, .62, .87), .59),
    "Shop": Arrangement((0, .16, .34, .67, .84), (0, .67), (.34, .67), (.25, .5, .76), .45),
    "TalentMarket": Arrangement((0, .17, .44, .62, .81), (0, .38, .58, .86), (.25, .75), (.26, .51, .89), .66),
    "Camp": Arrangement((0, .57, .78), (0, .58), (.92,), (.28, .61), .24),
}
SEVEN_EIGHT = Arrangement((0, 1/7, 2/7, 3/7, 5/7, 6/7), (0, 1/7, 3/7, 5/7, 6/7),
                          (0, 3/7, 5/7, 6.5/7), (1.5/7, 4/7, 5.5/7, 6.5/7), 1.27)


def near_chord_tone(wanted, root, chord):
    options = [root + interval + octave for interval in chord for octave in (-12, 0, 12)]
    return min(options, key=lambda interval: abs(wanted - interval))


def arrange(track, seed):
    mix = Mix(track, seed, BARS)
    style = SEVEN_EIGHT if track.meter == 7 else ARRANGEMENTS[track.setting]
    battle = track.setting == "Battle"
    quiet = track.setting in ("Camp", "Exploration")
    for bar in range(BARS):
        section = bar // 8
        position = bar * track.meter
        root, *chord = track.chords[bar % 8]
        level = (.89, .75, 1.05)[section]
        for index, interval in enumerate(chord):
            voice = track.harmony
            note = track.tonic + root + interval + (12 if index == 0 else 0)
            mix.note(position + .025 * index, voice, note, track.meter * .87,
                     .050 * level, (-.5, .47, -.22, .27)[index])

        for index, step in enumerate(style.bass):
            if section == 1 and index % 2:
                continue
            gap = (style.bass + (1,))[index + 1] - step
            chord_note = chord[2] if index > 0 and index % 2 == 0 else 0
            mix.note(position + track.meter * step, track.bass, track.tonic + root - 12 + chord_note,
                     gap * track.meter * .88, (.14 if battle else .098) * level)

        voice = track.response if section == 1 else track.lead
        for index, step in enumerate(style.melody):
            if quiet and bar % 4 == 3 and index == len(style.melody) - 1:
                continue
            motif_index = (bar * 2 + index + (2 if section == 1 else 0)) % len(track.motif)
            interval = track.motif[motif_index]
            gap = (style.melody + (1,))[index + 1] - step
            if index == 0 or gap > .30:
                interval = near_chord_tone(interval, root, chord)
            if bar == BARS - 1 and index == len(style.melody) - 1:
                interval = near_chord_tone(track.motif[0], 0, (0, 7))
            gain = (.145 if index == 0 else .103) * level
            length = track.meter * gap * (.8 if battle else .92)
            mix.note(position + step * track.meter, voice, track.tonic + 12 + interval, length, gain, -.17)
            if section == 2 and battle and index in (0, 3):
                mix.note(position + step * track.meter + .035, track.response,
                         track.tonic + 12 + interval, length * .79, .062, .48)

        if section != 0 or not quiet:
            for index, step in enumerate(style.backing):
                note = track.tonic + 12 + root + chord[(index + bar) % len(chord)]
                mix.note(position + step * track.meter, track.response, note,
                         track.meter * (.13 if battle else .20),
                         (.065 if section == 2 else .047) * level, -.45 if index % 2 == 0 else .50)

        for index, step in enumerate(style.accents):
            if quiet and bar % 2 == 0:
                continue
            if battle:
                mix.hit(position + step * track.meter, "kick", (.12 if index == 0 else .065) * level)
            if not battle or index % 2 == 1:
                mix.hit(position + step * track.meter, track.percussion,
                        .086 * style.strength * level, -.31 if index % 2 == 0 else .33)
        if battle and bar % 8 == 7:
            mix.hit(position + track.meter * .94, track.percussion, .13, .40)
    return mix.master(track.space, target_rms=.119 if battle else .099)


def main():
    OUTPUT.mkdir(parents=True, exist_ok=True)
    for index, track in enumerate(TRACKS):
        write_loop(arrange(track, 6347 + index * 773), OUTPUT / (track.id + ".ogg"), track.title, quality=5)


if __name__ == "__main__":
    main()
