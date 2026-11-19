"""Author the sixteen-frame, transparent combat VFX strips shown in Docs/SkillEffects.md."""

import argparse
from math import cos, pi, sin
from pathlib import Path
from random import Random

from PIL import Image, ImageDraw, ImageFilter, ImageFont


CELL = 96
FRAMES = 16
ROOT = Path(__file__).resolve().parents[1]
ART = ROOT / "Assets/RaidPartyManager/Art/SkillEffects"
PREVIEW = ROOT / "Docs/Previews/SkillEffects.gif"

# Shadow, saturated middle tone, highlight. The contour stays legible over bright arenas.
STYLES = (
    ("WarCry",         "#81331d", "#fd771f", "#ffe0a2"),
    ("BlazingFire",    "#742120", "#ff7229", "#fff1aa"),
    ("AbsoluteZero",  "#17456e", "#5abce7", "#dcfeff"),
    ("Sunshine",      "#86521b", "#ffc746", "#fff5bd"),
    ("StoneGuard",    "#41303a", "#af826b", "#f5c88e"),
    ("HealingHands",  "#18574b", "#56d5a1", "#dcffce"),
    ("ArrowVolley",   "#143d49", "#39ead2", "#e4fff3"),
    ("Meteor",        "#50256e", "#a986ff", "#fce7ff"),
    ("ArmySweep",     "#533068", "#f8a76d", "#fff3be"),
    ("LightningWhip", "#33347b", "#7a91ff", "#f2e4ff"),
    ("ToxicMist",     "#304421", "#a6d650", "#edff9b"),
    ("FleshFeast",    "#541430", "#cf4172", "#ffc5b7"),
    ("Sidestep",      "#1b5269", "#4fe1c8", "#fff2ae"),
    ("GoldenBell",    "#78481c", "#edaa3b", "#fff3ad"),
    ("FieryEyes",     "#811a27", "#ff654a", "#fff4a2"),
    ("CrimsonReap",   "#5f1739", "#f3496b", "#ffe4c8"),
    ("HeroicAnthem",  "#4c3b88", "#b5a4ff", "#fff4b6"),
    ("Intimidate",    "#4c335f", "#c583f3", "#ffe0a1"),
    ("Earthquake",    "#4a2a38", "#dc8242", "#ffe0a0"),
    ("CloudPiercer",  "#1b596d", "#65ddf5", "#f4ffff"),
)


class Frame:
    def __init__(self, palette):
        self.image = Image.new("RGBA", (CELL, CELL))
        self.draw = ImageDraw.Draw(self.image)
        self.shadow, self.mid, self.light = palette

    def line(self, points, width=2):
        self.draw.line(points, self.shadow, width + 4, joint="curve")
        self.draw.line(points, self.mid, width + 1, joint="curve")
        self.draw.line(points, self.light, max(1, width - 1), joint="curve")

    def arc(self, x, y, rx, ry, start=0, end=360, width=2):
        if min(rx, ry) < 1:
            return
        bounds = (x - rx, y - ry, x + rx, y + ry)
        self.draw.arc(bounds, start, end, self.shadow, width + 3)
        self.draw.arc(bounds, start, end, self.mid, width + 1)
        self.draw.arc(bounds, start, end, self.light, width)

    def gem(self, x, y, rx, ry):
        self.draw.polygon([(x, y-ry), (x+rx, y), (x, y+ry), (x-rx, y)], fill=self.mid)
        self.draw.polygon([(x, y-ry), (x, y+ry), (x-rx, y)], fill=self.light)

    def star(self, x, y, size):
        self.draw.line((x-size, y, x+size, y), self.mid, 1)
        self.draw.line((x, y-size, x, y+size), self.mid, 1)
        self.draw.rectangle((x-1, y-1, x+1, y+1), self.light)

    def sparks(self, rng, t, count=15, rise=0):
        for _ in range(count):
            angle = rng.uniform(0, 2*pi)
            distance = rng.uniform(13, 41) * (0.45 + 0.55*t)
            x = 48 + cos(angle) * distance
            y = 49 + sin(angle) * distance - rise*t
            size = rng.choice((1, 2, 2, 3))
            if rng.random() > t * 0.64:
                self.star(x, y, size + t)


def arrow(f, start, tip, wing=5):
    x, y = tip
    f.line((start, tip), 2)
    f.draw.polygon([(x+4, y-2), (x-wing, y+1), (x-1, y+wing), (x+1, y+3)], fill=f.light)
    f.draw.line((start, (start[0]+6, start[1]+2)), f.mid, 2)


def smooth(t):
    t = max(0, min(1, t))
    return t*t*(3 - 2*t)


def pulse(t, attack=.14, decay=.45):
    return smooth(t/attack) * (1 - smooth((t-decay)/(1-decay)))


def ray(f, center, angle, start, end, width=1):
    x, y = center
    dx, dy = cos(angle), sin(angle)
    f.line([(x+dx*start, y+dy*start), (x+dx*end, y+dy*end)], width)


def war_cry(f, t, _):
    for delay in (0, .14, .31):
        wave = smooth((t-delay)*1.64)
        if 0 < wave < 1:
            f.arc(48, 56, 9+36*wave, 5+22*wave, 198, 342, max(1, int(4-3*wave)))
    for side in (-1, 1):
        f.line([(48+side*6, 65), (48+side*15, 55), (48+side*21, 26),
                (48+side*(13+15*t), 21)], 3)
        f.gem(48+side*(17+18*t), 39-11*t, 3, 7)
    f.star(48, 50, 6+8*pulse(t))

def fire(f, t, rng):
    expansion = smooth(t*1.9)
    f.arc(48, 76, 12+30*expansion, 5+4*expansion, 8, 350, 3)
    for i, offset in enumerate((-27, -18, -8, 2, 12, 22, 28)):
        x = 48+offset
        sway = 7*sin(t*10+i*1.45)
        growth = pulse(t + i*.017, .28, .52)
        tip = 72-(12+38*growth)*rng.uniform(.77, 1.18)
        f.draw.polygon([(x-8, 77), (x-10, 60), (x+sway, tip), (x+sway+2, tip+13),
                        (x+8, 60), (x+9, 73)], fill=f.shadow)
        f.draw.polygon([(x-6, 73), (x-6, 61), (x+sway, tip+2),
                        (x+5, 63), (x+6, 72)], fill=f.mid)
        f.draw.polygon([(x-2, 74), (x-4, 64), (x+sway*.5, tip+14), (x+4, 70)], fill=f.light)
    for i in range(6):
        f.gem(17+12*i+3*sin(t*9+i), 62-(t+.1)*(19+i*3), 2, 3)

def ice(f, t, _):
    growth = smooth(t/.29)
    for x, height, slope in ((23, 25, -9), (35, 41, -7), (49, 57, 3), (62, 39, 9), (74, 23, 7)):
        rise = 76-height*growth
        f.draw.polygon([(x-9, 78), (x+slope, rise), (x+10, 71)], fill=f.shadow)
        f.draw.polygon([(x-5, 75), (x+slope, rise), (x+6, 68)], fill=f.mid)
        f.draw.polygon([(x-5, 75), (x+slope, rise), (x+1, 68)], fill=f.light)
    f.arc(48, 74, 12+32*t, 4+10*t, 7, 350)
    burst = smooth((t-.4)/.6)
    for n in range(8):
        angle = n*pi/4
        f.gem(48+cos(angle)*(17+23*burst), 46+sin(angle)*(17+21*burst), 2, 3+2*burst)
    f.star(50, 39, 5+10*pulse(t, .28))

def sunshine(f, t, _):
    opening = smooth(t/.35)
    y = 30+4*t
    for i in range(12):
        angle = i*pi/6 + t*.45
        ray(f, (48, y), angle, 15+6*opening, 19+14*opening, 1+i%2)
    f.arc(48, y, 10+8*opening, 10+8*opening, width=3)
    f.gem(48, y, 5+5*opening, 5+5*opening)
    for x in (26, 37, 48, 61, 72):
        f.line([(48+(x-48)*.47, 48), (x, 54+23*opening)], 1)
    f.arc(48, 74, 12+27*t, 4+7*t, 3, 177, 2)

def stone_guard(f, t, _):
    close = 1-smooth(t/.44)
    for row, columns in enumerate(((0, 1, 2), (0, 1, 2), (1,))):
        for col in columns:
            x = 23+col*17+(col-1)*close*13
            y = 31+row*13+(row-1)*close*9
            f.draw.polygon([(x-2,y+3), (x+3,y-2), (x+15,y), (x+17,y+11), (x,y+13)], fill=f.shadow)
            f.draw.polygon([(x,y+3), (x+4,y), (x+13,y+2), (x+15,y+9), (x+2,y+10)], fill=f.mid)
            f.draw.line((x+4,y+1,x+13,y+2), f.light, 2)
    f.arc(48, 69, 23+14*t, 7+4*t, 0, 183)
    if t >= .30:
        f.star(48, 42, 4+8*pulse(t-.3, .12))

def healing(f, t, _):
    y = 62-24*smooth(t)
    f.line([(35, y), (61, y)], 4)
    f.line([(48, y-14), (48, y+14)], 4)
    for index in range(5):
        angle = index*pi*2/5 + t*5
        x = 48+cos(angle)*(21+8*t)
        lift = 69-t*31 + sin(angle)*8
        f.gem(x, lift, 4, 6)
    f.arc(48, 71-12*t, 19+17*t, 6+7*t, 25+t*60, 309+t*55, 2)

def volley(f, t, _):
    # The flying arrows are separate sprites. This strip is the burst at contact.
    travel = smooth(t/.68)
    for i, angle in enumerate((-2.62, -2.12, -.98, -.54, .32, .94, 2.1, 2.75)):
        base = 4+27*travel
        ray(f, (49, 48), angle, base, base+10-i%3*2, 1+i%2)
    f.arc(51, 48, 7+22*travel, 7+23*travel, 146, 455, 2)
    if t < .62:
        f.star(49, 48, 11*(1-t))

def meteor(f, t, _):
    # The intact rock and its fragments are animated by the battle scene.
    spread = smooth(t/.75)
    f.arc(48, 61, 9+33*spread, 4+13*spread, 5, 355, 3)
    f.arc(48, 61, 6+33*spread, 3+10*spread, 11, 348, 1)
    for i, angle in enumerate((-2.6, -2.03, -1.63, -1.12, -.58, -.22)):
        radius = 6 + spread * (19 + i%3 * 4)
        ray(f, (48, 56), angle, radius, radius + 10*(1-spread), 2)
    if t < .55:
        f.star(48, 55, 14*(1-t))

def sweep(f, t, _):
    travel = smooth(t/.72)
    for offset, thickness in ((0, 7), (-13, 3), (10, 2)):
        f.arc(48, 50+offset, 28+14*travel, 13+12*travel,
              150+73*travel, 280+63*travel, thickness)
    f.gem(24+48*travel, 45-20*sin(pi*travel), 7, 4)
    for i in range(4):
        ray(f, (47, 51), i*.66-1.6, 13+13*travel, 24+20*travel, 1)

def lightning(f, t, rng):
    flicker = .7+.3*cos(t*8*pi)
    anchors = [(10, 53), (22, 34), (32, 50), (44, 30), (56, 49), (66, 41), (84, 62)]
    points = [(x, y+rng.uniform(-8, 8)*sin(14*t+x)) for x, y in anchors]
    f.line(points, 3+int(3*flicker))
    for anchor, endpoint in ((2, (21,72)), (4, (61,77)), (5, (80,27))):
        x, y = points[anchor]
        ex, ey = endpoint
        f.line([(x,y), ((x+ex)/2+4*cos(9*t), (y+ey)/2-6), endpoint], 1)
    f.star(84, 61, 6+4*flicker)
    f.star(36, 48, 3+4*flicker)

def toxic(f, t, rng):
    drift = smooth(t)
    for i in range(12):
        x = 48 + rng.uniform(-35,35)*(.45+.55*drift)
        y = 61 + rng.uniform(-18,13) - 21*drift
        radius = rng.uniform(4,10)+6*drift
        f.draw.ellipse((x-radius, y-radius*.65, x+radius, y+radius*.65), fill=f.shadow)
        f.draw.arc((x-radius, y-radius*.65, x+radius, y+radius*.65), 190+i*4, 354, f.mid, 3)
        if i % 3 == 0:
            f.star(x+radius*.3, y-3, 2)
    for i in range(4):
        f.arc(24+i*15, 72-30*drift-i%2*6, 2+i%2+2*drift, 3+i%2+2*drift)
    f.arc(48, 72, 18+23*t, 7, 10, 178, 2)

def feast(f, t, _):
    bite = smooth(t/.6)
    for side in (-1, 1):
        for i in range(4):
            x = 48 + side*(33+(i-1.5)*3 - 22*bite)
            y = 30 + i*11
            f.draw.polygon([(x,y-7), (x-side*12,y+2), (x-side*3,y+7), (x+side*4,y+4)], fill=f.mid)
            f.draw.line((x,y-5,x-side*10,y+2), f.light, 2)
        f.arc(48, 51, 39-20*bite, 27-11*bite, 16+side*83+48*t, 102+side*83+48*t, 3)
    f.gem(48, 53, 5+7*pulse(t, .34), 6+9*pulse(t, .34))

def sidestep(f, t, _):
    drift = smooth(t)
    for i in range(4):
        x = 20+i*15+8*drift
        y = 62-i*4
        f.line([(x-13, y+5), (x+3, y-1), (x+13, y-10)], 1+i//2)
        if i < 3:
            f.line([(x-6, 32+i*5), (x+3, 40+i*5), (x-6, 48+i*5)], 2)
    f.arc(50, 70, 19+18*drift, 7, 15, 159)
    f.gem(63+11*drift, 33-7*drift, 7, 6)
    f.star(53+16*drift, 44, 6)

def golden_bell(f, t, _):
    form = smooth(t/.35)
    width = 12+17*form
    top = 55-12*form
    f.arc(48, top, width, 13+14*form, 183, 357, 4)
    f.line([(48-width, top), (43-width, 67), (53+width, 67), (48+width, top)], 3)
    f.arc(48, 67, width+6, 7+2*form, 0, 181, 2)
    f.gem(48, 68, 5, 6)
    f.arc(48, 68, width+10*t, 8+14*t, 7, 165)
    for side in (-1, 1):
        ray(f, (48, 57), side*.63+.5, 12+15*t, 19+18*t)

def fiery_eyes(f, t, _):
    blink = pulse(t, .25, .58)
    radius = 4+12*blink
    f.arc(48, 45+3*blink, 29, radius, 10, 169, 3)
    f.arc(48, 52-3*blink, 29, radius, 191, 346, 3)
    f.gem(48, 49, 4+4*blink, 3+10*blink)
    d = 15+18*smooth(t)
    f.line([(48-d,49), (48,49-d), (48+d,49), (48,49+d), (48-d,49)], 1)
    f.star(48, 49, 3+9*blink)

def reaping(f, t, _):
    swing = smooth(t/.7)
    start = 152+120*swing
    f.arc(47, 49, 32+7*swing, 19+14*swing, start, start+130, 7)
    f.arc(49, 51, 39, 23+15*swing, start-24, start+76, 2)
    f.line([(65-15*swing, 24), (50, 47), (25+9*swing, 74)], 2)
    for i in range(4):
        f.gem(18+18*i+11*swing, 66-25*swing-i%2*7, 3, 5)

def anthem(f, t, _):
    drift = smooth(t)
    f.arc(48, 71-7*drift, 14+24*drift, 6+7*drift, 9, 350)
    for i in range(5):
        angle = i*1.12+drift*1.6
        x = 48+cos(angle)*(21+10*drift)
        y = 64-drift*(16+i*4)+sin(angle)*9
        f.gem(x-4, y+4, 5, 4)
        f.line([(x,y+4), (x,y-10), (x+7,y-6)], 2)
    f.star(49, 32-10*drift, 6+4*pulse(t))

def intimidate(f, t, _):
    for delay in (0,.16,.29):
        wave = smooth((t-delay)*1.6)
        if 0 < wave < 1:
            f.arc(48, 46, 9+34*wave, 5+20*wave, 2, 358, 2)
    for i in range(4):
        angle = i*pi/2 + t*3.7
        x = 48+cos(angle)*(18+13*t)
        y = 39+sin(angle)*(8+8*t)
        f.star(x, y, 4+3*pulse(t))
    f.line([(32, 65), (39, 55), (45, 64), (52, 55), (62, 65)], 2)

def earthquake(f, t, rng):
    spread = smooth(t/.7)
    f.arc(48, 72, 10+29*spread, 3+9*spread, width=2)
    for i in range(7):
        angle = 2*pi*i/7
        dx, dy = cos(angle), .43*sin(angle)
        distance = (18+rng.randrange(18))*(.32+.68*spread)
        f.line([(48, 70), (48+dx*distance*.38+3, 68+dy*distance*.38),
                (48+dx*distance*.61-2, 71+dy*distance*.61),
                (48+dx*distance, 70+dy*distance)], 2)
    for i in range(9):
        x = 48+rng.uniform(-31,31)*(.61+.39*spread)
        y = 71-rng.uniform(16,43)*sin(pi*t)
        f.gem(x, y, rng.uniform(2,6), rng.uniform(3,8))

def cloud_piercer(f, t, _):
    thrust = smooth(t/.73)
    x, y = 19+57*thrust, 72-45*thrust
    f.line([(x-30, y+16), (x-18, y+9), (x+3, y-1)], 5)
    arrow(f, (x-18, y+11), (x+8, y-5), 6)
    for delay in (0, .17, .33):
        ripple = smooth((t-delay)*1.6)
        if 0 < ripple < 1:
            f.arc(48+14*ripple, 49, 7+13*ripple, 8+20*ripple, 266, 448, 2)
    f.star(x+2, y-3, 7)

DRAWINGS = (war_cry, fire, ice, sunshine, stone_guard, healing, volley, meteor, sweep, lightning,
            toxic, feast, sidestep, golden_bell, fiery_eyes, reaping, anthem, intimidate,
            earthquake, cloud_piercer)


def render_skill(number, palette, render):
    frames = []
    for i in range(FRAMES):
        t = i/(FRAMES-1)
        frame = Frame(palette)
        render(frame, t, Random(1000 + number))
        rising = number in (2, 4, 6, 11, 17)
        frame.sparks(Random(4000 + number), t, count=17 if rising else 10, rise=22 if rising else 0)
        opacity = (.34 + .66*smooth(t/.17)) * (1 - .97*smooth((t-.64)/.36))
        mask = frame.image.getchannel("A")
        glow = Image.new("RGBA", frame.image.size, palette[1])
        glow.putalpha(mask.filter(ImageFilter.GaussianBlur(2.3)).point(lambda alpha: int(alpha*.64*opacity)))
        frame.image.putalpha(mask.point(lambda alpha: int(alpha*opacity)))
        frames.append(Image.alpha_composite(glow, frame.image))
    return frames


def preview(sets):
    frames = []
    font = ImageFont.load_default()
    for i in range(FRAMES):
        board = Image.new("RGBA", (624, 502), "#10151e")
        label = ImageDraw.Draw(board)
        for n, skill in enumerate(sets):
            x, y = 10 + (n%5)*122, 10 + (n//5)*123
            label.rounded_rectangle((x, y, x+114, y+115), 4, fill="#202937", outline="#3c495a")
            board.alpha_composite(skill[i], (x+9, y+4))
            label.text((x+56, y+103), f"{n+1:02d}", font=font, fill="#c8d0db", anchor="mt")
        frames.append(board.convert("RGB"))
    PREVIEW.parent.mkdir(parents=True, exist_ok=True)
    frames[0].save(PREVIEW, save_all=True, append_images=frames[1:], duration=45, loop=0, optimize=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--preview", action="store_true", help="also rebuild the animated contact sheet")
    args = parser.parse_args()
    ART.mkdir(parents=True, exist_ok=True)
    if len(STYLES) != len(DRAWINGS):
        raise ValueError("Every palette needs a skill drawing")
    rendered = []
    for number, (style, draw) in enumerate(zip(STYLES, DRAWINGS), 1):
        name, *palette = style
        frames = render_skill(number, palette, draw)
        sheet = Image.new("RGBA", (FRAMES*CELL, CELL))
        for i, frame in enumerate(frames):
            sheet.paste(frame, (i*CELL, 0))
        sheet.save(ART / f"{number:02d}-{name}.png", optimize=True)
        rendered.append(frames)
    if args.preview:
        preview(rendered)


if __name__ == "__main__":
    main()
