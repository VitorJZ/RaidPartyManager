"""Generate the requested WAV files from the project's ElevenLabs sound list."""

import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
from urllib.error import HTTPError, URLError
from urllib.request import HTTPRedirectHandler, Request, build_opener
import wave

ROOT = Path(__file__).resolve().parents[1]
MANIFEST = ROOT / "Docs" / "ElevenLabsSoundEffects.json"
OUTPUT = ROOT / "Logs" / "ElevenLabsSoundEffects"
FORMAT = "mp3_44100_128"
API_URL = f"https://api.elevenlabs.io/v1/sound-generation?output_format={FORMAT}"
MAX_AUDIO_BYTES = 8 * 1024 * 1024


class NoRedirect(HTTPRedirectHandler):
    """Do not forward an API credential to a redirected URL."""

    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def read_key():
    name = "ELEVENLABS_API_KEY"
    if value := os.environ.get(name, "").strip():
        return value
    if sys.platform == "win32":
        import winreg

        # A newly configured Windows variable may not reach this existing shell.
        for root, location in (
            (winreg.HKEY_CURRENT_USER, "Environment"),
            (winreg.HKEY_LOCAL_MACHINE, r"SYSTEM\CurrentControlSet\Control\Session Manager\Environment"),
        ):
            try:
                with winreg.OpenKey(root, location) as key:
                    value, _ = winreg.QueryValueEx(key, name)
                    if isinstance(value, str) and value.strip():
                        return value.strip()
            except OSError:
                pass
    return ""


def read_manifest(path):
    effects = json.loads(path.read_text(encoding="utf-8-sig"))["effects"]
    if not isinstance(effects, list) or not effects:
        raise ValueError("The manifest must contain a non-empty effects list.")
    seen = set()
    for item in effects:
        filename = item.get("filename", "")
        seconds = item.get("duration_seconds")
        if not isinstance(filename, str) or not re.fullmatch(r"[a-z0-9_]+\.wav", filename) or filename in seen:
            raise ValueError(f"Invalid or duplicate WAV name: {filename!r}")
        if type(seconds) not in (float, int) or not math.isfinite(seconds) or not 0.5 <= seconds <= 30:
            raise ValueError(f"{filename}: duration must be between 0.5 and 30 seconds.")
        if type(item.get("loop")) is not bool or not isinstance(item.get("text"), str) or not item["text"].strip():
            raise ValueError(f"{filename}: a prompt and an explicit loop flag are required.")
        seen.add(filename)
    return effects


def request_body(item, influence, loop_model):
    return json.dumps({
        "text": item["text"],
        "duration_seconds": item["duration_seconds"],
        "loop": item["loop"],
        "prompt_influence": influence,
        "model_id": "eleven_text_to_sound_" + (loop_model if item["loop"] else "v3"),
    }, ensure_ascii=False).encode("utf-8")


def download(opener, key, payload):
    request = Request(API_URL, data=payload, headers={
        "xi-api-key": key, "Content-Type": "application/json", "Accept": "audio/mpeg",
    }, method="POST")
    try:
        with opener.open(request, timeout=120) as response:
            kind = response.headers.get_content_type()
            if not (kind.startswith("audio/") or kind == "application/octet-stream"):
                raise ValueError(f"ElevenLabs returned {kind}, not audio.")
            audio = response.read(MAX_AUDIO_BYTES + 1)
            if not audio or len(audio) > MAX_AUDIO_BYTES:
                raise ValueError("Unexpected audio response size.")
            cost = response.headers.get("character-cost", "")
            return audio, cost if cost.isdecimal() else None
    except HTTPError as error:
        hint = {
            401: "Check the API key.",
            402: "Check the account's available credits.",
            403: "Check the key's Sound Effects permission and workspace quota.",
            422: "Check the model, loop option and requested duration.",
            429: "Wait for rate limits or check the quota, then resume.",
        }.get(error.code, "Check ElevenLabs before resuming.")
        provider_status = ""
        try:
            detail = json.loads(error.read(8192)).get("detail")
            if isinstance(detail, dict):
                status = detail.get("status")
                if isinstance(status, str) and re.fullmatch(r"[a-zA-Z0-9_]{1,80}", status):
                    provider_status = f" ({status})"
        except (ValueError, AttributeError):
            pass
        raise ValueError(f"ElevenLabs HTTP {error.code}{provider_status}. {hint}") from None
    except (URLError, TimeoutError) as error:
        raise ValueError(f"Connection failed ({type(error).__name__}). Check generation history before retrying.") from None


def validate_wav(path, seconds, *, allow_native_wav=False):
    with wave.open(str(path), "rb") as audio:
        formats = {(44100, 2), (48000, 2), (48000, 3), (48000, 4)} if allow_native_wav else {(44100, 2)}
        if (audio.getframerate(), audio.getsampwidth()) not in formats or not 1 <= audio.getnchannels() <= 2:
            raise ValueError("Unexpected PCM WAV format.")
        frames = audio.getnframes()
        actual = frames / audio.getframerate()
        if abs(actual - seconds) > max(0.1, seconds * 0.02) or frames == 0:
            raise ValueError(f"WAV length {actual:.3f}s differs from the requested {seconds:g}s.")
        data = audio.readframes(frames)
        if len(data) != frames * audio.getnchannels() * audio.getsampwidth() or not any(data):
            raise ValueError("The WAV is truncated or silent.")
        return actual


def generate_one(opener, key, ffmpeg, output, item, influence, loop_model):
    destination = output / item["filename"]
    if destination.exists():
        seconds = validate_wav(destination, item["duration_seconds"], allow_native_wav=True)
        return f"exists ({seconds:.3f}s)"

    payload = request_body(item, influence, loop_model)
    fingerprint = hashlib.sha256(FORMAT.encode("ascii") + payload).hexdigest()[:16]
    source_dir = output / ".source"
    source_dir.mkdir(parents=True, exist_ok=True)
    source = source_dir / f"{destination.stem}.{fingerprint}.mp3"
    cost = None
    if not source.exists():
        audio, cost = download(opener, key, payload)
        with tempfile.NamedTemporaryFile(dir=source_dir, delete=False) as stream:
            temporary = Path(stream.name)
            stream.write(audio)
        os.replace(temporary, source)

    with tempfile.TemporaryDirectory(dir=output) as workspace:
        wav = Path(workspace) / "audio.wav"
        subprocess.run(
            [ffmpeg, "-nostdin", "-hide_banner", "-loglevel", "error", "-i", str(source),
             "-map", "0:a:0", "-vn", "-ar", "44100", "-c:a", "pcm_s16le", str(wav)],
            check=True,
        )
        seconds = validate_wav(wav, item["duration_seconds"])
        os.replace(wav, destination)
    billed = f", billed units: {cost}" if cost is not None else ""
    return f"saved ({seconds:.3f}s{billed})"


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", type=Path, default=MANIFEST)
    parser.add_argument("--output", type=Path, default=OUTPUT)
    parser.add_argument("--generate", action="store_true", help="Send billable requests (default: dry run).")
    parser.add_argument("--only", action="append", default=[], metavar="WAV", help="Select a file by name; repeatable.")
    parser.add_argument("--limit", type=int, help="Take only the first N selected entries.")
    parser.add_argument("--prompt-influence", type=float, default=0.3)
    parser.add_argument("--loop-model", choices=("v3", "v2"), default="v3",
                        help="Model for looped sounds; use v2 only if the API rejects v3 looping.")
    args = parser.parse_args(argv)
    if (args.limit is not None and args.limit < 1) or not 0 <= args.prompt_influence <= 1:
        parser.error("the limit must be positive and prompt influence must be in [0, 1]")

    try:
        effects = read_manifest(args.manifest)
        if args.only:
            selected = {n if n.endswith(".wav") else n + ".wav" for n in args.only}
            unknown = selected - {item["filename"] for item in effects}
            if unknown:
                raise ValueError("Unknown files: " + ", ".join(sorted(unknown)))
            effects = [item for item in effects if item["filename"] in selected]
        effects = effects[:args.limit]
        print(f"{len(effects)} sounds, {sum(i['loop'] for i in effects)} loops, "
              f"{sum(i['duration_seconds'] for i in effects):.1f}s requested")
        if not args.generate:
            for item in effects:
                print(f"{item['filename']}\t{item['duration_seconds']:g}s\t{'loop' if item['loop'] else 'one shot'}")
            return 0

        key = read_key()
        if not key:
            raise ValueError("ELEVENLABS_API_KEY is not configured. No request was sent.")
        ffmpeg = shutil.which("ffmpeg")
        if not ffmpeg:
            raise ValueError("FFmpeg must be available on PATH. No request was sent.")
        args.output.mkdir(parents=True, exist_ok=True)
        opener = build_opener(NoRedirect())
        for index, item in enumerate(effects, 1):
            name = item["filename"]
            print(f"[{index}/{len(effects)}] {name}", end=": ", flush=True)
            print(generate_one(opener, key, ffmpeg, args.output, item, args.prompt_influence, args.loop_model), flush=True)
        return 0
    except (OSError, ValueError, KeyError, TypeError, wave.Error, subprocess.CalledProcessError) as error:
        print(f"\nStopped: {error}", file=sys.stderr, flush=True)
        return 1


if __name__ == "__main__":
    sys.exit(main())
