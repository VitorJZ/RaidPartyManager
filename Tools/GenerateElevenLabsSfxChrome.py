"""Batch-generate game sound effects through the normal ElevenLabs Chrome UI."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
from time import monotonic
from urllib.parse import urljoin, urlsplit
import wave

from GenerateElevenLabsSfx import MANIFEST, OUTPUT, read_manifest

ORIGIN = "https://elevenlabs.io"
HISTORY = ORIGIN + "/app/sound-effects/history"
PROFILE = Path(os.environ.get("LOCALAPPDATA") or Path.home()) / "RaidPartyManager" / "ElevenLabsChrome"
PRIVATE = re.compile(
    r"生成内容不会被分享至\s*Explore|(?:generations?|generated (?:content|sounds?|sound effects?))\s+"
    r"(?:will not|won.t|are not) be shared", re.I,
)
GENERATE = re.compile(r"^(?:生成音效|Generate(?: sound effects?)?)(?:\s|$)", re.I)
TIME_CONTROL = re.compile(r"^(?:时长|Duration)\s*[:：]", re.I)
LOOP_CONTROL = re.compile(r"^(?:循环|Loop(?:ing)?)\s*[:：]", re.I)
HISTORY_SEARCH = re.compile(r"搜索音效历史记录|Search.*history|Search.*past", re.I)
CREDITS = re.compile(r"([\d,]+)\s*(?:积分|credits?)\s*/\s*([\d,]+)\s*(?:积分|credits?)", re.I)
FIRST_VARIANT = re.compile(r"^(?:播放|Play)\s*#1\s+(\d+(?:\.\d+)?)\s*s\b", re.I)
WAV = re.compile(r"^WAV\b", re.I)
MP3 = re.compile(r"^MP3\b", re.I)


class StopBatch(RuntimeError):
    """An action needs attention; do not resubmit it blindly."""


def target_url(value):
    parts = urlsplit(urljoin(ORIGIN, value))
    if (parts.scheme, parts.netloc, parts.path) != ("https", "elevenlabs.io", "/app/sound-effects/history"):
        raise StopBatch("History link points outside the ElevenLabs sound effects page.")
    return parts.geturl()


def request_id(item):
    values = [item[k] for k in ("text", "duration_seconds", "loop", "filename")]
    return hashlib.sha256(json.dumps(values, ensure_ascii=False).encode()).hexdigest()


def save_journal(path, data):
    with tempfile.NamedTemporaryFile(
        mode="w", encoding="utf-8", dir=path.parent, delete=False,
    ) as handle:
        staged = Path(handle.name)
        json.dump(data, handle, ensure_ascii=False, indent=2)
        handle.write("\n")
    os.replace(staged, path)


def wav_duration(path, requested):
    with wave.open(str(path), "rb") as audio:
        channels, width, rate = audio.getnchannels(), audio.getsampwidth(), audio.getframerate()
        count = audio.getnframes()
        if channels not in (1, 2) or width not in (2, 3, 4) or rate not in (44100, 48000):
            raise StopBatch(f"{path.name}: expected mono or stereo PCM WAV at 44.1 or 48 kHz.")
        actual = count / rate
        if abs(actual - requested) > max(0.10, requested * 0.02):
            raise StopBatch(f"{path.name}: received {actual:.3f}s, requested {requested:g}s.")
        frames = audio.readframes(count)
        if len(frames) != count * width * channels or not any(frames):
            raise StopBatch(f"{path.name}: incomplete or silent WAV.")
    return actual


def visible_one(locator, label):
    visible = locator.filter(visible=True)
    if visible.count() != 1:
        raise StopBatch(f"Expected one visible {label}; found {visible.count()}. Check Chrome.")
    return visible


class ElevenLabsPage:
    def __init__(self, page):
        self.page = page
        self.prompt = page.locator("#sfx-prompt-box")

    def assert_private(self):
        current = urlsplit(self.page.url)
        if (current.scheme, current.netloc) != ("https", "elevenlabs.io") or not re.fullmatch(
            r"/app/sound-effects(?:/history)?/?", current.path
        ):
            raise StopBatch("Chrome is not on the ElevenLabs Sound Effects page. Run --setup and sign in.")
        if not self.page.get_by_text(PRIVATE).filter(visible=True).count():
            raise StopBatch("Private generation is not confirmed. Run --setup and turn off Explore sharing.")

    def open_history(self, link=HISTORY):
        self.page.goto(target_url(link), wait_until="domcontentloaded", timeout=45000)
        self.prompt.wait_for(state="visible", timeout=20000)
        self.assert_private()

    def search_history(self, item):
        search = visible_one(
            self.page.get_by_role("textbox", name=HISTORY_SEARCH).or_(
                self.page.get_by_placeholder(HISTORY_SEARCH)
            ),
            "History search field",
        )
        search.fill(item["text"][:60])

    def configure(self, item):
        self.assert_private()
        model = visible_one(self.page.get_by_role("combobox", name=re.compile(
            r"^(?:音效模型|Sound effects? model)$", re.I,
        )), "sound model selector")
        if model.inner_text().strip().lower() != "v3":
            raise StopBatch("Select the v3 sound model in Chrome with --setup.")
        toggle = visible_one(self.page.get_by_role("button", name=LOOP_CONTROL), "loop toggle")
        self.set_loop(toggle, item["loop"])
        duration = visible_one(self.page.get_by_role("button", name=TIME_CONTROL), "duration button")
        self.set_duration(duration, item["duration_seconds"])
        self.prompt.fill(item["text"])
        if self.prompt.input_value() != item["text"]:
            raise StopBatch("The prompt field did not retain the exact text.")

    @staticmethod
    def set_loop(toggle, required):
        def is_on():
            value = toggle.get_attribute("aria-pressed")
            if value in ("true", "false"):
                return value == "true"
            value = toggle.get_attribute("aria-label") or toggle.inner_text()
            if re.search(r"(?:关闭|\boff\b)", value, re.I):
                return False
            if re.search(r"(?:开启|\bon\b)", value, re.I):
                return True
            raise StopBatch("Could not verify whether looping is on.")

        if is_on() != required:
            toggle.click()
        if is_on() != required:
            raise StopBatch("The loop setting did not change.")

    def set_duration(self, button, seconds):
        def selected():
            label = button.get_attribute("aria-label") or button.inner_text()
            value = re.search(r"(\d+(?:[.,]\d+)?)\s*(?:秒|s(?:ec(?:onds?)?\.?)?)", label, re.I)
            return float(value[1].replace(",", ".")) if value else None

        if selected() == seconds:
            return
        button.click()
        auto = self.page.locator("#auto-duration")
        auto.wait_for(state="visible")
        if selected() is None:
            auto.click()
        scope = auto.locator("xpath=ancestor::*[.//input[@type='range'] or .//*[@role='slider']][1]")
        slider = visible_one(scope.get_by_role("slider"), "duration slider")
        tenths = round(seconds * 10)
        if abs(seconds * 10 - tenths) > 1e-6:
            raise StopBatch("The website uses a 0.1-second duration step.")

        # The browser UI's Page Up adds one second; ArrowRight adds a tenth.
        # Home selects Auto (0.4), so start from the current numeric setting.
        slider.focus()
        for _ in range(40):
            current = selected()
            gap = tenths - round((0.4 if current is None else current) * 10)
            if gap == 0:
                break
            direction = 1 if gap > 0 else -1
            if abs(gap) >= 10:
                slider.press("PageUp" if direction > 0 else "PageDown")
            else:
                slider.press("ArrowRight" if direction > 0 else "ArrowLeft")
        if selected() != seconds:
            raise StopBatch(f"Could not set the website duration to {seconds:g}s.")
        self.prompt.click()

    def charges(self):
        # Verify the numeric estimate as displayed; do not infer an API rate.
        costs = self.page.get_by_text(CREDITS).filter(visible=True)
        if costs.count() != 1:
            raise StopBatch("Could not verify the displayed generation cost and remaining credits.")
        values = CREDITS.search(costs.inner_text())
        if not values:
            raise StopBatch("Could not parse the displayed generation credits.")
        return tuple(int(value.replace(",", "")) for value in values.groups())

    def generation_links(self, text):
        candidates = self.page.get_by_role(
            "link", name=re.compile(re.escape(text[:60]) + r".*" + re.escape(text[-25:]), re.S),
        ).and_(self.page.locator("a[href*='/app/sound-effects/history?id=']")).filter(visible=True)
        return {target_url(link.get_attribute("href")) for link in candidates.all()}

    def submit(self, item, old_links):
        self.assert_private()
        visible_one(self.page.get_by_role("button", name=GENERATE), "Generate button").click()
        deadline = monotonic() + 60
        while monotonic() < deadline:
            new = self.generation_links(item["text"]) - old_links
            if len(new) == 1:
                return new.pop()
            if len(new) > 1:
                break
            self.page.wait_for_timeout(500)
        raise StopBatch("Could not confirm a new generation link. Search History before retrying.")

    def first_variant(self, item):
        exact = self.page.get_by_text(item["text"], exact=True)
        heading = exact.locator("xpath=ancestor::*[self::li or @role='listitem'][1]").filter(visible=True)
        deadline = monotonic() + 10
        while heading.count() == 0 and monotonic() < deadline:
            self.page.wait_for_timeout(250)
        if heading.count() != 1:
            raise StopBatch(
                "Could not identify exactly one matching generation in History. "
                "Check the account's history and save this WAV manually before continuing."
            )
        row = heading.locator("xpath=following-sibling::*[self::li or @role='listitem'][1]")
        # Tie the export menu to the first variant of this exact generation.
        variant = visible_one(row.get_by_role("button", name=FIRST_VARIANT), "first audio variant")
        label = variant.get_attribute("aria-label") or variant.aria_snapshot()
        match = re.search(r"#1\s+(\d+(?:\.\d+)?)\s*s\b", label, re.I)
        if not match or abs(float(match[1]) - item["duration_seconds"]) > 0.10:
            raise StopBatch("The first variant does not show the expected duration.")
        return visible_one(row.locator('button[aria-haspopup="menu"]'), "audio download menu")

    def export(self, item, destination, ffmpeg):
        menu = self.first_variant(item)
        menu.click()
        format_name = MP3 if item["loop"] else WAV
        choice = visible_one(self.page.get_by_role("menuitem", name=format_name), "download format")
        if not choice.is_enabled():
            raise StopBatch("The account cannot download this audio format.")
        with tempfile.TemporaryDirectory(dir=destination.parent) as folder:
            temporary_wav = Path(folder) / destination.name
            source = temporary_wav.with_suffix(".mp3") if item["loop"] else temporary_wav
            with self.page.expect_download(timeout=45000) as pending:
                choice.click()
            pending.value.save_as(str(source))
            if item["loop"]:
                subprocess.run(
                    [ffmpeg, "-nostdin", "-hide_banner", "-loglevel", "error", "-i", str(source),
                     "-map", "0:a:0", "-vn", "-c:a", "pcm_s16le", str(temporary_wav)],
                    check=True,
                )
            actual = wav_duration(temporary_wav, item["duration_seconds"])
            if destination.exists():
                raise StopBatch(f"{destination.name} appeared on disk; refusing to overwrite it.")
            os.replace(temporary_wav, destination)
            return actual


def run_batch(site, effects, output, ffmpeg, max_credits):
    journal_file = output / ".chrome-progress.json"
    journal = json.loads(journal_file.read_text(encoding="utf-8")) if journal_file.exists() else {}
    spent = 0
    for number, item in enumerate(effects, 1):
        name = item["filename"]
        dest = output / name
        if dest.exists():
            print(f"[{number}/{len(effects)}] {name}: exists ({wav_duration(dest, item['duration_seconds']):.3f}s)")
            continue
        signature = request_id(item)
        entry = journal.get(name)
        if entry and entry["signature"] != signature:
            raise StopBatch(f"{name}: the manifest changed since the recorded request. Use a new output folder.")
        if entry:
            if not entry.get("history_url"):
                # The previous click may have succeeded even if the result link was lost.
                # Look for its output, but never click Generate a second time.
                print(f"[{number}/{len(effects)}] {name}: looking for the interrupted request in History", flush=True)
                site.open_history()
                site.search_history(item)
            else:
                site.open_history(entry["history_url"])
        else:
            site.open_history()
            site.configure(item)
            price, available = site.charges()
            if price > available or (max_credits is not None and spent + price > max_credits):
                raise StopBatch(f"{name}: {price} credits required; remaining balance or batch limit is too low.")
            old_links = site.generation_links(item["text"])
            entry = journal[name] = {"signature": signature, "credits": price}
            save_journal(journal_file, journal)
            print(f"[{number}/{len(effects)}] {name}: generating ({price} credits shown)", flush=True)
            spent += price
            entry["history_url"] = site.submit(item, old_links)
            save_journal(journal_file, journal)
            site.open_history(entry["history_url"])
        actual = site.export(item, dest, ffmpeg)
        print(f"[{number}/{len(effects)}] {name}: saved ({actual:.3f}s)", flush=True)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--setup", action="store_true", help="Open Chrome for manual sign-in and sharing setup.")
    mode.add_argument("--generate", action="store_true", help="Submit the selected rows with existing credits.")
    parser.add_argument("--manifest", type=Path, default=MANIFEST)
    parser.add_argument("--output", type=Path, default=OUTPUT)
    parser.add_argument("--only", action="append", default=[], metavar="WAV", help="Select a WAV by name; repeatable.")
    parser.add_argument("--limit", type=int, help="Limit the number of missing WAVs to process.")
    parser.add_argument("--max-credits", type=int, help="Limit new credits charged in this run.")
    args = parser.parse_args(argv)
    if args.limit is not None and args.limit < 1 or args.max_credits is not None and args.max_credits < 0:
        parser.error("--limit must be positive and --max-credits must be non-negative")

    effects = read_manifest(args.manifest)
    if args.only:
        names = {n if n.endswith(".wav") else n + ".wav" for n in args.only}
        unknown = names - {item["filename"] for item in effects}
        if unknown:
            parser.error("Unknown filenames: " + ", ".join(sorted(unknown)))
        effects = [item for item in effects if item["filename"] in names]
    remaining = []
    for item in effects:
        path = args.output / item["filename"]
        if path.exists():
            wav_duration(path, item["duration_seconds"])
        else:
            remaining.append(item)
    print(f"{len(effects) - len(remaining)} already on disk; {len(remaining)} missing.")
    remaining = remaining[:args.limit]
    if not args.generate and not args.setup:
        for item in remaining:
            print(f"{item['filename']}\t{item['duration_seconds']:g}s\t{'loop' if item['loop'] else 'one shot'}")
        return 0
    if not args.setup and not remaining:
        return 0
    ffmpeg = shutil.which("ffmpeg")
    if args.generate and any(item["loop"] for item in remaining) and not ffmpeg:
        raise StopBatch("FFmpeg is needed to convert looping MP3s to WAV. No request was sent.")

    try:
        from playwright.sync_api import Error as PlaywrightError, sync_playwright
    except ImportError:
        raise StopBatch("Install the Playwright Python package: python -m pip install playwright") from None
    try:
        with sync_playwright() as playwright:
            context = playwright.chromium.launch_persistent_context(
                user_data_dir=PROFILE, channel="chrome", headless=False,
                locale="zh-CN", viewport={"width": 1450, "height": 900}, accept_downloads=True,
            )
            try:
                page = context.pages[0] if context.pages else context.new_page()
                if args.setup:
                    page.goto(HISTORY, wait_until="domcontentloaded")
                    input("In Chrome, sign in, turn off Explore sharing, select v3, then press Enter here: ")
                    site = ElevenLabsPage(page)
                    site.assert_private()
                    visible_one(site.prompt, "sound prompt")
                else:
                    args.output.mkdir(parents=True, exist_ok=True)
                    run_batch(ElevenLabsPage(page), remaining, args.output, ffmpeg, args.max_credits)
            finally:
                context.close()
    except PlaywrightError as error:
        message = str(error).splitlines()[0]
        raise StopBatch(f"Chrome: {message}. Check the page and History before retrying.") from None
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (StopBatch, OSError, ValueError, KeyError, wave.Error, subprocess.CalledProcessError) as exc:
        print(f"Stopped: {exc}", file=sys.stderr, flush=True)
        sys.exit(1)
    except KeyboardInterrupt:
        print("Stopped. Completed files and confirmed history links were kept.", file=sys.stderr)
        sys.exit(130)
