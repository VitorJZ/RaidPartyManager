"""Bake 16-cel motion, scene art, and enemy definitions from the city's painted masters."""
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor
import json
import math
import re
import cv2
import numpy as np
from PIL import Image, ImageDraw, ImageFilter
import AnimationCels as anim
import BuildWhisperingGraveyard as unity

ROOT = unity.ROOT
SOURCE = ROOT / "Docs/References/ClockworkCapital"
DATA = json.loads((ROOT / "Docs/ClockworkCapital.json").read_text())
ART = ROOT / "Assets/RaidPartyManager/Art"
CITY = ART / "ClockworkCapital/Resources/Clockwork"
ENEMIES = ART / "Enemies/ClockworkCapital"
DEFS = ROOT / "Assets/RaidPartyManager/PrototypeData/Enemies/ClockworkCapital"
EVENTS = ART / "DockPlayhouse/Resources/StoryEvents"
TEMPLATE = ART / "Enemies/SaltCrab-Combat.png.meta"
N = anim.COUNT


def save(p, im):
    unity.folder(p.parent)
    anim.save(im, p, TEMPLATE)


def isolated(sheet, cols, rows):
    master = Image.open(SOURCE / (sheet + ".png")).convert("RGBA")
    cells = anim.split(master, cols, rows)
    result = []
    for index, cel in enumerate(cells):
        if sheet == "council" and index == 4:
            result.append(governor_figure(master))
            continue
        a = np.array(cel)
        alpha = a[:, :, 3]
        alpha[alpha < 25] = 0
        # A neighbouring tool or antenna can enter a painted gutter. Discard isolated edge slivers.
        _, labels, stats, _ = cv2.connectedComponentsWithStats((alpha > 0).astype("uint8"))
        for ix, (x,y,w,h,area) in enumerate(stats[1:],1):
            if area < 12 or (x == 0 or y == 0 or x+w == cel.width or y+h == cel.height) and area < .02*alpha.size:
                alpha[labels == ix] = 0
        clean = Image.fromarray(a)
        box = clean.getchannel("A").getbbox()
        if not box: raise ValueError(sheet)
        result.append(clean.crop(box))
    return result


def governor_figure(council):
    # The crown and upper orbits cross the nominal row divider on the painted master.
    pixels = np.array(council)
    alpha = pixels[:, :, 3]
    _, labels, _, _ = cv2.connectedComponentsWithStats((alpha > 170).astype(np.uint8))
    body = labels[council.height * 3 // 4, council.width // 2]
    if not body:
        raise ValueError("Could not locate the governor in the council painting")
    # Keep the antialiased edge around this figure, without the translucent plate or its neighbours.
    edge = cv2.dilate((labels == body).astype(np.uint8),
                      cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (7, 7)))
    pixels[:, :, 3] = edge * np.clip((alpha.astype(float) - 80) * (255 / 170), 0, 255).astype(np.uint8)
    pixels[pixels[:, :, 3] == 0] = 0
    image = Image.fromarray(pixels)
    return image.crop(image.getbbox())


def fit(im, size, padding=.08, foot=False):
    copy = im.copy()
    copy.thumbnail((round(size[0]*(1-2*padding)), round(size[1]*(1-2*padding))), Image.Resampling.LANCZOS)
    frame = Image.new("RGBA", size)
    frame.alpha_composite(copy, ((size[0]-copy.width)//2, size[1]-round(size[1]*padding)-copy.height if foot else (size[1]-copy.height)//2))
    return frame


def poses(key, move, index, ground_fall=False):
    base = anim.premultiplied(key)
    h,w = base.shape[:2]
    xy = anim.grid(w,h)
    u,v = xy[:,:,0]/w, xy[:,:,1]/h
    frames = []
    if ground_fall:
        ys, xs = np.nonzero(base[:, :, 3])
        floor = ys.max()
    for i in range(N):
        t=i/(N-1) if move=="death" else i/N; a=2*np.pi*i/N; pulse=math.sin(math.pi*t)
        field=xy.copy(); rotation=0; dx=dy=0; scale=1
        if move=="idle":
            field[:,:,1] += h*.021*(1-v)*math.sin(a)
            field[:,:,0] += w*.012*(1-v)*math.cos(a)
        elif move=="walk":
            leg=np.clip((v-.52)/.33,0,1)
            field[:,:,0] += w*.10*leg*np.tanh((u-.5)*10)*math.cos(a)
            field[:,:,1] += h*.05*leg*np.maximum(0,np.tanh((u-.5)*10)*np.sin(a)) + h*.022*abs(math.sin(a))
            rotation=2.3*math.sin(a)
        elif move in ("attack","special"):
            recoil=math.sin(min(t/.38,1)*math.pi/2) if t<.38 else math.cos(min((t-.38)/.25,1)*math.pi)
            recoil*=1 if t<.63 else (1-t)/.37
            dx=w*.058*recoil; rotation=(7 if move=="attack" else 11)*recoil
            field[:,:,0] -= w*(.055 if move=="attack" else .09)*(1-u)*(1-v)*recoil
            dy=-h*.038*pulse if move=="special" else 0
        elif move=="hit":
            snap=np.sin(min(t*2,1)*np.pi*.5)*(1-t)**2
            dx=w*.14*snap; rotation=-14*snap
            field[:,:,1] += h*.025*(1-v)*pulse
        else:
            rotation=-72*t**1.25; dy=h*.27*t; scale=1-.25*t
        # Rotating about the centre keeps the falling sprite on the painted card.
        remap=cv2.remap(base,field,None,cv2.INTER_LINEAR)
        mat=cv2.getRotationMatrix2D(((w-1)*.5,(h-1)*(.50 if move=="death" else .76)),rotation,scale)
        mat[:,2]+=(dx,dy)
        if ground_fall:
            # Land the turning hem on the same boards as the idle pose, inside its own cel.
            mat[1,2] = floor - np.max(mat[1,0] * xs + mat[1,1] * ys)
        p=cv2.warpAffine(remap,mat,(w,h))
        if move in ("special","hit"):
            p[:,:,:3] += p[:,:,3:] * np.array([.13,.073,.027])*pulse
        frames.append(anim.straight(p))
    return frames


def actor_sheets(entry, im):
    governor = entry["id"] == "AstralGovernor"
    key=fit(im,(192,192),.12,True)
    rows={m:poses(key,m,entry["number"],ground_fall=governor and m=="death") for m in ("idle","walk","attack","special","hit","death")}
    prefix=entry["id"]
    def out(name,im): save(ENEMIES / (prefix+"-"+name+".png"),im)
    out("Portrait",fit(im,(256,384),.055,True))
    out("Combat",anim.pack(sum((rows[m] for m in ("idle","attack","hit","death")),[]),16))
    out("Walk",anim.pack(rows["walk"],4))
    for name in ("Attack","Special"):
        out(name,unity.pack(rows[name.lower()],4,4,wide=True))
    if governor:
        governor_entrance(rows["idle"][0])


def governor_entrance(idle):
    size = 320
    idle = idle.resize((size, size), Image.Resampling.LANCZOS)
    left, top, right, floor = idle.getchannel("A").point(lambda a: 255 if a > 10 else 0).getbbox()
    with Image.open(SOURCE / "AstralGovernor-EntranceKeys.png") as master:
        raw = anim.split(master.convert("RGBA"), 2, 2)
    keys = []
    for cel in raw:
        alpha = cel.getchannel("A").point(lambda a: 0 if a < 9 else a)
        cel.putalpha(alpha)
        keys.append(cel.crop(alpha.getbbox()))
    scale = min((floor - top) / np.median([key.height for key in keys]),
                (size - 8) / max(key.width for key in keys), (floor - 3) / max(key.height for key in keys))
    fitted = []
    for key in keys:
        ink = key.resize((round(key.width * scale), round(key.height * scale)), Image.Resampling.LANCZOS)
        tile = Image.new("RGBA", idle.size)
        tile.alpha_composite(ink, (round((left + right - ink.width) / 2), floor - ink.height))
        fitted.append(tile)
    fitted.append(idle)

    # Reproject one key at a time: two superimposed armillary rings look like a second instrument.
    cels = []
    xy = anim.grid(size, size)
    prev = None
    for i in range(N):
        at = i * (len(fitted) - 1) / (N - 1)
        index = int(at)
        fraction = at - index
        if fraction == 0:
            cels.append(fitted[index])
            continue
        if index != prev:
            prev = index
            a, b = (anim.premultiplied(frame) for frame in fitted[index:index + 2])
            forward, backward = anim.motion(a, b)
        source, displacement = (a, forward * fraction) if fraction <= .5 else (b, backward * (1 - fraction))
        cels.append(anim.straight(cv2.remap(source, xy - displacement, None, cv2.INTER_LINEAR)))
    save(ART / "Enemies/Resources/BossEntrances/EnemyAstralGovernor.png", anim.pack(cels, 4))


def actor(entry, im):
    actor_sheets(entry, im)
    save(ENEMIES / (entry["id"] + "-SpecialFX.png"), unity.special_fx(entry))
    unity.make_definition(entry)


def district_art():
    sheet=Image.open(SOURCE/"districts.png").convert("RGB")
    # Painted dividing rails between vistas stay outside the stage opening.
    bounds=[(0,.035,1,.303),(0,.346,1,.610),(0,.674,1,.982)]
    for name,(x,y,r,b) in zip(("Terminus","Furnace","Meridian"),bounds):
        im=sheet.crop((0,round(y*sheet.height),sheet.width,round(b*sheet.height)))
        save(CITY/(name+".png"),im)
        pix=np.array(im).astype(np.float32)
        yy,xx=np.indices(pix.shape[:2]); glazing=(.35+.65*(1-yy/im.height))[:,:,None]
        fmt="Panoramas" if im.width>2*im.height else "Backdrops"
        for i in range(N):
            a=math.tau*i/N
            light=1+.035*np.sin(a+xx/im.width*5)[:,:,None]*glazing
            mist=np.maximum(0,np.cos(a+xx/im.width*7+yy/im.height*3))[:,:,None]*glazing*2.5
            frame=Image.fromarray(np.clip(pix*light+mist,0,255).astype("uint8"))
            path=ART/"DockPlayhouse/Resources/SceneryCels"/fmt/str(i)/(name+".jpg")
            frame.save(path,quality=91); unity.meta(path,"texture")
    deck = CITY / "IronDeck.png"
    save(deck, Image.open(SOURCE / "deck.png").resize((512, 512)))
    # Floors repeat in world space across the revolving platform.
    importer = Path(str(deck) + ".meta")
    guid = re.search(r"^guid: .+$", importer.read_text(), re.M).group()
    template = (ART / "BattleStage/StoneDeck.png.meta").read_text()
    importer.write_text(re.sub(r"^guid: .+$", guid, template, flags=re.M))
    for i,art in enumerate(isolated("facilities",2,2)):
        save(CITY/("Facility"+str(i)+".png"),art)
    props=isolated("props",4,4)
    for event in DATA["events"]:
        i=event["propSlot"]
        prop=props[i]
        board=fit(prop,(320,320))
        save(CITY/(event["id"]+".png"),prop)
        save(EVENTS/"Playbills"/(event["id"]+".png"),board)
        frames=poses(board,"special",i)
        save(EVENTS/(event["id"]+".png"),anim.pack(frames,4))
    # These atlas cells dress the district sets independently of playable events.
    for i,name in ((4,"LostFreight"),(7,"StreetCurrent"),(10,"SilentOrchestra"),(15,"Gate")):
        save(CITY/(name+".png"),props[i])


def main():
    unity.ART=ENEMIES;unity.ASSETS=DEFS
    unity.folder(ENEMIES);unity.folder(DEFS);unity.folder(CITY)
    characters=[]
    for name,rows in (("sentry",3),("foundry",3),("archive",3),("council",2)):
        characters.extend(isolated(name,3,rows))
    with ThreadPoolExecutor(4) as pool:
        list(pool.map(actor,DATA["enemies"],characters))
    district_art()
    icons=isolated("loot",3,2)
    for i,reward in enumerate(DATA["rewards"]):
        for k,folder,j in (("gear","Equipment",i),("book","Skills",i+3)):
            item=reward[k]
            save(ART/folder/(str(item["number"])+"-"+item["id"]+".png"),fit(icons[j],(256,256)))
        # Each spell has a separate 16-cel impact reel with the emblem of its paired relic.
        symbol=fit(icons[i],(72,72))
        strip=[]
        color=((250,158,66),(114,216,242),(239,113,54))[i]
        for n in range(N):
            t=n/(N-1); layer=Image.new("RGBA",(96,96)); pen=ImageDraw.Draw(layer)
            for blade in range(8):
                theta=blade*math.tau/8+t*(1+i); distance=6+35*t
                x=48+math.cos(theta)*distance;y=48+math.sin(theta)*distance
                pen.line((48+(x-48)*.65,48+(y-48)*.65,x,y),fill=(*color,round(240*(1-.8*t))),width=3)
            stamp=symbol.resize((round(22+30*math.sin(math.pi*t)),)*2,Image.Resampling.LANCZOS)
            stamp.putalpha(stamp.getchannel("A").point(lambda v:int(v*(1-.85*t))))
            layer.alpha_composite(stamp,((96-stamp.width)//2,(96-stamp.height)//2));strip.append(layer)
        item=reward["book"]
        save(ART/"SkillEffects"/(str(item["number"])+"-"+item["id"]+".png"),anim.pack(strip,16))


def build_catalogues():
    import re
    proto = ROOT / "Assets/RaidPartyManager/PrototypeData"
    eq = proto / "Equipment"
    gear_refs = []
    catalog = proto / "SkillCatalog.asset"
    books = catalog.read_text()
    for index, reward in enumerate(DATA["rewards"]):
        gear, book = reward["gear"], reward["book"]
        boss = "Enemy" + reward["boss"]
        path = eq / (gear["id"] + ".asset")
        body = (eq / "FacelessCrown.asset").read_text()
        fields = dict(m_Name=gear["id"], id=re.sub(r"(?<!^)([A-Z])", r"-\1", gear["id"]).lower(),
            displayName=gear["name"], effect=55+index, description=gear["description"], rewardBossId=boss,
            icon=unity.ref(ART/"Equipment"/(str(gear["number"])+"-"+gear["id"]+".png")))
        for field, value in fields.items():
            body = re.sub(r"^  "+field+r":.*$", lambda _: "  "+field+": "+str(value), body, flags=re.M)
        path.write_text(body); unity.meta(path, "asset")
        gear_refs.append(unity.ref(path, 11400000, 2))
        key=str(book["number"])+"-"+book["id"]+".png"
        entry = "  - number: "+str(book["number"])+"\n"
        for field, value in dict(displayName=book["name"],icon=unity.ref(ART/"Skills"/key),
                battleEffect=unity.ref(ART/"SkillEffects"/key),requirement=book["requirement"],cost=book["cost"],
                skillEffect=33+index,tier=0,rewardBossId=boss,description=book["description"]).items():
            entry += f"    {field}: {value}\n"
        pattern = rf"^  - number: {book['number']}\n.*?(?=^  - number: |\Z)"
        if re.search(pattern, books, flags=re.M | re.S):
            books = re.sub(pattern, lambda _: entry, books, count=1, flags=re.M | re.S)
        else:
            later = next((m for m in re.finditer(r"^  - number: (\d+)$", books, re.M)
                          if int(m[1]) > book["number"]), None)
            at = later.start() if later else len(books)
            books = books[:at] + entry + books[at:]
    catalog.write_text(books)
    for name in ["RoadsideEncounter","WhisperingGraveyardEncounter"]:
        path=proto/(name+".asset"); body=path.read_text()
        for ref in gear_refs:
            if ref not in body: body=body.replace("  backpackIcon:", "  - "+ref+"\n  backpackIcon:")
        path.write_text(body)
    encounter=proto/"ClockworkCapitalEncounter.asset"
    body=(proto/"WhisperingGraveyardEncounter.asset").read_text().replace("WhisperingGraveyardEncounter","ClockworkCapitalEncounter").replace("低语墓园演练","机巧遗都")
    def combatant(key): return unity.ref(DEFS/("Enemy"+key+".asset"),11400000,2)
    roster="  enemyRoster:\n"+"".join("  - "+combatant(e["id"])+"\n" for e in DATA["enemies"])
    groups="  enemyFormations:\n"
    for group in DATA["formations"]:
        line=group["enemies"]
        for offset in range(len(line) if len(line)==4 else 1):
            groups+="  - name: "+group["name"]+(" · "+str(offset+1) if offset else "")+"\n    enemyIds:\n"
            groups+="".join("    - Enemy"+e+"\n" for e in line[offset:]+line[:offset])
    body=re.sub(r"  enemyRoster:.*?(?=  statusIcons:)",lambda _: roster+groups,body,flags=re.S)
    opening="  enemies:\n"
    for i,key in enumerate(DATA["formations"][0]["enemies"],1):
        opening+=f"  - instanceId: city-{i}\n    character: {combatant(key)}\n    position: {i}\n    startingHealth: -1\n"
    body=re.sub(r"  enemies:.*?(?=  additionalRecruits:)",lambda _:opening,body,flags=re.S)
    encounter.write_text(body); unity.meta(encounter,"asset")
    scene=ROOT/"Assets/Scenes/ClockworkCapital.unity"
    body=scene.with_name("WhisperingGraveyard.unity").read_text()
    body=re.sub(r"  encounter:.*","  encounter: "+unity.ref(encounter,11400000,2),body,count=1)
    stage=ART/"BattleStage/BattleStageArt.asset"
    stage_body=stage.read_text()
    vistas=[]
    for back,l,r,far,fl,fr in [("Terminus","Gate","TimeOffice","FareGate","LostFreight","WrongSwitch"),("Furnace","BoilerUnion","SpareHands","StreetCurrent","WaterCharter","GoldStandard"),("Meridian","LastMeridian","SilentOrchestra","ElectionPress","SeedArchive","ClinicQueue")]:
        def art(key):return unity.ref(CITY/(key+".png"))
        vistas.append(art(back))
        token="  - region: "+art(back)+"\n"
        if token not in stage_body:
            stage_body+=token+"    turntable: 1\n    clockwork: 1\n"
            for k,v in dict(backcloth=back,floor="IronDeck",upstage=far,leftWing=l,rightWing=r,leftDressing=fl,rightDressing=fr).items():
                stage_body+="    "+k+": "+art(v)+"\n"
    stage.write_text(stage_body)
    body=re.sub(r"  battleBackgrounds:.*?(?=  randomizeLineups:)","  battleBackgrounds:\n"+"".join("  - "+v+"\n" for v in vistas),body,flags=re.S)
    scene.write_text(body);unity.meta(scene)
    path=ROOT/"ProjectSettings/EditorBuildSettings.asset";body=path.read_text()
    if "Assets/Scenes/ClockworkCapital.unity" not in body:
        path.write_text(body.replace("  m_configObjects:","  - enabled: 1\n    path: Assets/Scenes/ClockworkCapital.unity\n    guid: "+unity.guid(scene)+"\n  m_configObjects:"))
    unity.meta(ROOT/"Assets/RaidPartyManager/Run/CapitalStories.cs","script")


if __name__=="__main__":
    import sys
    if "--catalogues-only" not in sys.argv: main()
    build_catalogues()
