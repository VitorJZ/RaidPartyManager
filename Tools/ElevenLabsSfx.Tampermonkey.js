// ==UserScript==
// @name         讨伐队经理 · ElevenLabs 音效批量生成
// @namespace    RaidPartyManager
// @version      1.0.5
// @description  按项目清单填写 ElevenLabs 音效表单，校验并保存 WAV。
// @match        https://elevenlabs.io/app/sound-effects*
// @grant        none
// @run-at       document-idle
// @noframes
// ==/UserScript==

(() => {
    "use strict";
    const jobs = /* BUILD:MANIFEST */ null;
    const path = "/app/sound-effects";
    if (location.origin !== "https://elevenlabs.io" ||
        ![path, path + "/", path + "/history"].includes(location.pathname) || !Array.isArray(jobs)) return;

    const storageKey = "raid-party-manager:sfx:progress:v1";
    const privateLabel = /生成内容不会被分享至\s*Explore|(?:generations?|generated (?:content|sounds?|sound effects?))\s+(?:will not|won.t|are not) be shared/i;
    const creditsLabel = /([\d,]+)\s*(?:积分|credits?)\s*\/\s*([\d,]+)\s*(?:积分|credits?)/i;
    const generateLabel = /^(?:生成音效|Generate(?: sound effects?)?)$/i;
    const formatLabel = {wav: /^WAV\b/i, mp3: /^MP3\b/i};
    const filterLabel = /搜索音效历史记录|Search.*history|Search.*past/i;
    const rowSelector = "li,[role=listitem]";
    const normalized = value => (value || "").replace(/\s+/g, " ").trim();
    const label = element => normalized(element.getAttribute("aria-label") ||
        element.getAttribute("aria-labelledby")?.split(/\s+/)
            .map(id => document.getElementById(id)?.textContent || "").join(" ") ||
        element.getAttribute("title") || element.innerText || element.textContent);
    const visible = element => element.getClientRects().length > 0 && getComputedStyle(element).visibility !== "hidden";
    const find = (root, selector, predicate) =>
        [...root.querySelectorAll(selector)].filter(e => predicate(e) && visible(e));
    const one = (values, description) => {
        if (values.length !== 1) throw new Error(`无法唯一识别${description}（找到 ${values.length} 项）。`);
        return values[0];
    };
    const sameText = (root, text) => find(root, "*", e =>
        normalized(e.textContent) === text &&
        ![...e.children].some(child => normalized(child.textContent) === text));
    const buttons = (root, regex) => find(root, "button,[role=button]", e => regex.test(label(e)));
    const disabled = e => e.disabled || e.getAttribute("aria-disabled") === "true" || e.hasAttribute("data-disabled");
    const prompt = () => one(find(document, "#sfx-prompt-box", () => true), "音效提示词输入框");
    const delay = ms => new Promise(resolve => setTimeout(resolve, ms));

    async function waitFor(check, description, timeout = 10000) {
        const until = Date.now() + timeout;
        do {
            const result = check();
            if (result) return result;
            await delay(220);
        } while (Date.now() < until);
        throw new Error(`等待${description}超时。`);
    }

    function requirePrivate() {
        if (location.origin !== "https://elevenlabs.io" ||
            ![path, path + "/", path + "/history"].includes(location.pathname)) {
            throw new Error("当前不在 ElevenLabs 音效页。");
        }
        if (!privateLabel.test(document.body.innerText)) {
            throw new Error("未确认关闭 Explore 分享；请先在生成框下方关闭分享。");
        }
        const composer = prompt();
        return composer;
    }

    function setValue(input, value) {
        const type = input instanceof HTMLTextAreaElement ? HTMLTextAreaElement : HTMLInputElement;
        Object.getOwnPropertyDescriptor(type.prototype, "value").set.call(input, value);
        input.dispatchEvent(new Event("input", {bubbles: true}));
        input.dispatchEvent(new Event("change", {bubbles: true}));
    }

    function modelVersion() {
        const model = one(find(document, "[role=combobox],select", e =>
            /音效模型|sound effects? model/i.test(label(e)) || /^v[23]$/i.test(normalized(e.textContent))), "音效模型");
        // The selected label is what the user sees. Some renderer versions
        // expose a non-empty backing value even when the visible label is v3.
        for (const candidate of [model.getAttribute("aria-valuetext"), model.selectedOptions?.[0]?.textContent,
            model.innerText, model.value, model.getAttribute("data-value"), model.textContent]) {
            const matched = normalized(candidate).match(/^(?:eleven_text_to_sound_|(?:SFX|sound effects?)\s+)?v([23])$/i);
            if (matched) return "v" + matched[1];
        }
        throw new Error("读取不到音效模型。请刷新音效历史页后点「检查模型」。");
    }

    async function fill(item) {
        const input = requirePrivate();
        const version = modelVersion();
        if (version !== "v3") throw new Error(`网页当前是 ${version}，请手动切换到 v3。`);
        const toggle = () => one(buttons(document, /^(?:循环|Loop(?:ing)?)\s*[:：]/i), "循环开关");
        const looping = el => {
            const pressed = el.getAttribute("aria-pressed");
            if (pressed === "true" || pressed === "false") return pressed === "true";
            if (/(?:关闭|\boff\b)/i.test(label(el))) return false;
            if (/(?:开启|\bon\b)/i.test(label(el))) return true;
            throw new Error("无法验证网页上的循环状态。");
        };
        if (looping(toggle()) !== item.loop) toggle().click();
        await waitFor(() => looping(toggle()) === item.loop, "循环设置");
        const durationButton = () => one(buttons(document, /^(?:时长|Duration)\s*[:：]/i), "时长设置");
        const duration = () => {
            const number = label(durationButton()).match(/(\d+(?:[.,]\d+)?)\s*(?:秒|s\b|sec)/i);
            return number ? Number(number[1].replace(",", ".")) : null;
        };
        if (duration() !== item.seconds) {
            durationButton().click();
            const auto = await waitFor(() => document.querySelector("#auto-duration"), "时长设置菜单");
            if (duration() === null && auto.getAttribute("aria-checked") !== "false") {
                auto.click();
                await waitFor(() => duration() !== null, "手动时长");
            }
            let scope = auto.parentElement;
            while (scope && !scope.querySelector("[role=slider],input[type=range]")) scope = scope.parentElement;
            if (!scope) throw new Error("找不到时长滑块。");
            const slider = one(find(scope, "[role=slider],input[type=range]", () => true), "时长滑块");
            if (slider instanceof HTMLInputElement && slider.type === "range") {
                setValue(slider, String(item.seconds));
            } else {
                const requested = Math.round(item.seconds * 10);
                for (let i = 0; i < 40; i++) {
                    const current = Math.round((duration() ?? 0.4) * 10);
                    const gap = requested - current;
                    if (!gap) break;
                    const key = Math.abs(gap) >= 10 ? (gap > 0 ? "PageUp" : "PageDown")
                        : (gap > 0 ? "ArrowRight" : "ArrowLeft");
                    slider.focus();
                    slider.dispatchEvent(new KeyboardEvent("keydown", {key, code: key, bubbles: true, cancelable: true}));
                    await waitFor(() => Math.round((duration() ?? 0.4) * 10) !== current, "滑块步进", 800);
                }
            }
            await waitFor(() => duration() === item.seconds, `${item.seconds} 秒时长`);
            // Radix closes this popover when its trigger is clicked again.
            if (visible(auto)) durationButton().click();
        }
        input.focus();
        setValue(input, item.prompt);
        await waitFor(() => input.value === item.prompt && duration() === item.seconds &&
            looping(toggle()) === item.loop, "表单回读");
    }

    function readCost() {
        const matched = [...document.body.innerText.matchAll(new RegExp(creditsLabel.source, "gi"))];
        if (matched.length !== 1) throw new Error("无法核对单条积分用量和账号余额。");
        return [Number(matched[0][1].replace(/,/g, "")), Number(matched[0][2].replace(/,/g, ""))];
    }

    function linksFor(item) {
        const text = normalized(item.prompt);
        return find(document, 'a[href*="/app/sound-effects/history?id="]', a => {
            const name = label(a);
            const url = new URL(a.href);
            return url.origin === location.origin && url.pathname === path + "/history" &&
                url.searchParams.has("id") && name.includes(text.slice(0, 44)) && name.includes(text.slice(-22));
        });
    }

    async function history() {
        if (location.pathname !== path + "/history") {
            const links = find(document, "a", a => a.origin === location.origin &&
                a.pathname === path + "/history" && !a.search && /^(?:历史|History)$/i.test(label(a)));
            if (!links.length) throw new Error("请先打开音效的「历史」页。");
            links[0].click();
            await waitFor(() => location.pathname === path + "/history", "音效历史页");
        }
    }

    function generationHeader(item) {
        const root = document.querySelector("main,[role=main]") || document;
        const expected = normalized(item.prompt);
        const matching = [...sameText(root, expected), ...find(root, "[title],[aria-label]", element =>
            [element.getAttribute("title"), element.getAttribute("aria-label")].some(value => normalized(value) === expected))];
        const matches = new Set(matching.map(element => element.closest(rowSelector)).filter(Boolean));
        if (!matches.size) {
            // History can elide the middle of the prompt in its visible text.
            for (const element of find(root, "span,p,div", element => {
                const [start, end, extra] = normalized(element.textContent).split(/…|\.{3}/u);
                if (extra !== undefined || start.length < 32 || !end || end.length < 22) return false;
                const fits = value => value.startsWith(start.trimEnd()) && value.endsWith(end.trimStart());
                return fits(expected) && !jobs.some(other => other !== item && fits(normalized(other.prompt)));
            })) {
                const row = element.closest(rowSelector);
                if (row) matches.add(row);
            }
        }
        if (matches.size > 1) throw new Error("历史中有多条相同提示词，请手动选择正确音效。");
        return [...matches][0];
    }

    function firstHistoryVariant(item) {
        const heading = generationHeader(item);
        if (!heading) return null;
        const root = heading.closest("main,[role=main]") || document;
        const rows = [...root.querySelectorAll(rowSelector)];
        const nextIndex = rows.indexOf(heading) + 1;
        // Some History layouts wrap the candidate rows in a separate group.
        const sibling = nextIndex > 0 ? rows.slice(nextIndex).find(visible) : null;
        if (!sibling) return null;
        const variantLabel = /^(?:(?:播放|暂停|Play|Pause)\s*)?#\s*1\s*([\d.,]+)\s*s\b/i;
        const button = buttons(sibling, variantLabel);
        let seconds = null;
        if (button.length === 1) {
            seconds = Number(label(button[0]).match(variantLabel)[1].replace(",", "."));
        } else {
            // ElevenLabs' compound row may name the play control through its
            // children. The DOM does not necessarily contain that AX name on
            // the row button; read the displayed candidate number and duration.
            const trigger = sibling.querySelector('[id^="list-item-trigger-"]');
            if (!trigger) return null;
            const combined = normalized(trigger.innerText || trigger.textContent) || normalized(sibling.innerText);
            const exact = /^[\d.,]+\s*s$/i;
            const durations = new Set(find(sibling, "span,div", element =>
                exact.test(normalized(element.textContent))).map(e => normalized(e.textContent)));
            if (durations.size !== 1) return null;
            const [durationText] = durations;
            if (!combined.replace(/\s+/g, "").startsWith(`#1${durationText.replace(/\s+/g, "")}`)) return null;
            seconds = Number.parseFloat(durationText.replace(",", "."));
        }
        if (Math.abs(seconds - item.seconds) > 0.10) throw new Error("历史候选 #1 的时长与清单不符；未重新提交。");
        if (sameText(heading, "v2").length) throw new Error("匹配的历史音效是 v2；未重新提交。");
        const menus = find(sibling, 'button[aria-haspopup="menu"]', () => true);
        if (!menus.length) return null;
        return one(menus, "音效下载菜单");
    }

    async function variantMenu(item, rememberedLink) {
        if (rememberedLink) {
            const input = new URL(rememberedLink);
            if (input.origin !== location.origin || input.pathname !== path + "/history" ||
                !input.searchParams.has("id")) throw new Error("记录中的历史链接不是 ElevenLabs 音效地址。");
            const link = !generationHeader(item) && linksFor(item).find(a => a.href === input.href);
            if (link) link.click();
        }
        await history();
        if (!generationHeader(item)) {
            const search = one(find(document, "input", e =>
                filterLabel.test(label(e) || e.placeholder)), "历史搜索框");
            setValue(search, item.prompt.slice(0, 60));
        }
        return waitFor(() => firstHistoryVariant(item), "匹配的历史音效及其下载按钮", 15000);
    }

    async function downloadFormat(trigger, item) {
        if (disabled(trigger)) throw new Error(`${item.file} 的下载按钮当前不可用。`);
        const popupForTrigger = () => {
            const id = trigger.getAttribute("aria-controls");
            const matches = find(document, '[role="menu"]', popup =>
                (id && popup.id === id) || (trigger.id && popup.getAttribute("aria-labelledby") === trigger.id));
            return matches.length ? one(matches, "对应音效的下载菜单") : null;
        };
        if (!popupForTrigger() && trigger.getAttribute("aria-expanded") !== "true") {
            // The History dropdown opens on pointerdown. HTMLElement.click()
            // only sends a click and leaves this trigger collapsed.
            trigger.dispatchEvent(new PointerEvent("pointerdown", {
                bubbles: true, cancelable: true, composed: true,
                pointerType: "mouse", button: 0, buttons: 1, isPrimary: true
            }));
        }
        const popup = await waitFor(popupForTrigger, ` ${item.file} 的下载菜单打开`);
        const options = find(popup, '[role="menuitem"]', () => true);
        const wav = options.filter(option => formatLabel.wav.test(label(option)));
        const mp3 = options.filter(option => formatLabel.mp3.test(label(option)));
        if (wav.length > 1 || mp3.length > 1) throw new Error(`${item.file} 的菜单包含重复的音频格式。`);
        if (wav[0] && !disabled(wav[0])) return {choice: wav[0], type: "wav"};
        if (item.loop && mp3[0] && !disabled(mp3[0])) return {choice: mp3[0], type: "mp3"};
        const visibleFormats = options.map(label).filter(Boolean).join("、") || "无";
        throw new Error(`${item.file} 没有可用的下载格式。该菜单显示：${visibleFormats}。`);
    }

    const filenameKey = text => text.toLowerCase().replace(/[^a-z0-9]/g, "");
    const promptKeys = jobs.map(item => [item.file, filenameKey(item.prompt)]);

    async function downloadedVariants(directory, item) {
        const matches = [];
        for await (const handle of directory.values()) {
            if (handle.kind !== "file") continue;
            // Chrome can add a collision suffix such as " (1)" before the extension.
            const filename = handle.name.match(/^(.+?)_#1-\d+(?: \(\d+\))?\.(wav|mp3)$/i);
            if (!filename) continue;
            const type = filename[2].toLowerCase();
            if (type === "mp3" && !item.loop) continue;
            const prefix = filenameKey(filename[1]);
            if (prefix.length < 15) continue;
            const candidates = promptKeys.filter(([, prompt]) => prompt.startsWith(prefix));
            if (candidates.length === 1 && candidates[0][0] === item.file) matches.push({handle, type});
        }
        return matches;
    }

    async function downloadToDirectory(directory, item, choice, type) {
        const before = new Set((await downloadedVariants(directory, item)).map(({handle}) => handle.name));
        choice.click();
        const until = Date.now() + 45000;
        let readError;
        do {
            const recent = (await downloadedVariants(directory, item)).filter(download =>
                download.type === type && !before.has(download.handle.name));
            if (recent.length > 1) throw new Error(`${item.file} 同时出现多个新下载，请核对候选 #1。`);
            if (recent.length === 1) {
                try {
                    return await readVerifiedDownload(recent[0], item);
                } catch (error) {
                    // The final filename can be visible before the audio finishes writing.
                    readError = error;
                }
            }
            await delay(500);
        } while (Date.now() < until);
        if (readError) throw new Error(`${item.file} 的 Chrome 下载已出现，但校验未通过。${readError.message}`);
        throw new Error(`${item.file} 未在所选目录出现。请核对 Chrome 下载位置与已选目录一致，或将已下载文件移入该目录后点「仅补下载」。`);
    }

    function verifyWav(buffer, seconds) {
        const view = new DataView(buffer);
        const fourcc = offset => String.fromCharCode(...new Uint8Array(buffer, offset, 4));
        if (view.byteLength < 44 || fourcc(0) !== "RIFF" || fourcc(8) !== "WAVE") throw new Error("下载结果不是 WAV。");
        let rate = 0, alignment = 0, data;
        for (let at = 12; at + 8 <= view.byteLength;) {
            const size = view.getUint32(at + 4, true);
            const start = at + 8;
            if (start + size > view.byteLength) throw new Error("WAV 音频被截断。");
            if (fourcc(at) === "fmt " && size >= 16) {
                const format = view.getUint16(start, true);
                const channels = view.getUint16(start + 2, true);
                const bits = view.getUint16(start + 14, true);
                rate = view.getUint32(start + 4, true);
                alignment = view.getUint16(start + 12, true);
                if ((format !== 1 && !(format === 0xfffe && size >= 40 && view.getUint16(start + 24, true) === 1)) ||
                    ![1, 2].includes(channels) || ![16, 24, 32].includes(bits) ||
                    ![44100, 48000].includes(rate) || alignment !== channels * bits / 8) {
                    throw new Error("WAV 不是有效的单声道或双声道 PCM。");
                }
            }
            if (fourcc(at) === "data") data = new Uint8Array(buffer, start, size);
            at = start + size + (size % 2);
        }
        if (!rate || !alignment || !data?.length || data.length % alignment !== 0 || !data.some(value => value)) {
            throw new Error("WAV 不完整或没有音频信号。");
        }
        const actual = data.length / (rate * alignment);
        if (Math.abs(actual - seconds) > Math.max(0.10, seconds * 0.02)) {
            throw new Error(`下载为 ${actual.toFixed(3)} 秒，预期 ${seconds} 秒。`);
        }
        return actual;
    }

    async function mp3ToWav(input) {
        const context = new OfflineAudioContext(2, 1, 44100);
        const decoded = await context.decodeAudioData(input);
        const channels = decoded.numberOfChannels;
        if (![1, 2].includes(channels)) throw new Error("循环音频的声道数不受支持。");
        const decodedChannels = Array.from({length: channels}, (_, n) => decoded.getChannelData(n));
        const size = decoded.length * channels * 2;
        const buffer = new ArrayBuffer(44 + size);
        const view = new DataView(buffer);
        const tag = (at, text) => [...text].forEach((c, i) => view.setUint8(at + i, c.charCodeAt(0)));
        tag(0, "RIFF"); view.setUint32(4, 36 + size, true); tag(8, "WAVE"); tag(12, "fmt ");
        view.setUint32(16, 16, true); view.setUint16(20, 1, true); view.setUint16(22, channels, true);
        view.setUint32(24, decoded.sampleRate, true); view.setUint32(28, decoded.sampleRate * channels * 2, true);
        view.setUint16(32, channels * 2, true); view.setUint16(34, 16, true);
        tag(36, "data"); view.setUint32(40, size, true);
        let index = 44;
        for (let frame = 0; frame < decoded.length; frame++) {
            for (const channel of decodedChannels) {
                const sample = Math.max(-1, Math.min(1, channel[frame]));
                view.setInt16(index, Math.round(sample * (sample < 0 ? 32768 : 32767)), true);
                index += 2;
            }
        }
        return buffer;
    }

    async function existing(directory, item) {
        try {
            const handle = await directory.getFileHandle(item.file);
            verifyWav(await (await handle.getFile()).arrayBuffer(), item.seconds);
            return true;
        } catch (error) {
            if (error.name !== "NotFoundError") throw error;
            return false;
        }
    }

    async function readVerifiedDownload({handle, type}, item) {
        let data = await (await handle.getFile()).arrayBuffer();
        if (type === "mp3") data = await mp3ToWav(data);
        const seconds = verifyWav(data, item.seconds);
        return {name: handle.name, data, seconds};
    }

    async function writeWav(directory, item, buffer) {
        if (await existing(directory, item)) return;
        const file = await directory.getFileHandle(item.file, {create: true});
        const stream = await file.createWritable();
        try {
            await stream.write(buffer);
            await stream.close();
        } catch (error) {
            await stream.abort().catch(() => {});
            throw error;
        }
    }

    let directory = null;
    let running = false, stopping = false;
    const box = document.createElement("aside");
    box.id = "raid-party-sfx-batch";
    box.style.cssText = "position:fixed;right:14px;top:115px;z-index:99999;width:324px;background:#182432;color:#f0f6ff;border:1px solid #6a829a;border-radius:9px;padding:14px;box-shadow:0 3px 18px #0005;font:13px/1.5 system-ui;";
    box.innerHTML = `<strong>讨伐队经理 · 音效批量</strong>
      <p style="margin:5px 0">Chrome 下载位置须与所选目录一致。 · v1.0.5</p>
      <p><button type="button" id="rpm-sfx-folder">选 WAV 输出目录</button> <button type="button" id="rpm-sfx-check">检查模型</button></p>
      <p><label>批量上限 <input id="rpm-sfx-credits" type="number" min="0" step="1" placeholder="当前余额" style="width:80px"> 积分</label></p>
      <p><button type="button" id="rpm-sfx-test">生成一条短音效</button> <button type="button" id="rpm-sfx-run">开始批量</button> <button type="button" id="rpm-sfx-stop">暂停</button></p>
      <p><button type="button" id="rpm-sfx-recover">仅补下载（不生成）</button></p>
      <small id="rpm-sfx-status" aria-live="polite">选择 Logs/ElevenLabsSoundEffects 后再开始。</small>`;
    for (const b of box.querySelectorAll("button")) {
        b.style.cssText = "background:#2e4459;border:1px solid #718da6;color:white;padding:3px 7px;border-radius:4px;margin:2px;cursor:pointer";
    }
    document.body.append(box);
    const status = text => { box.querySelector("#rpm-sfx-status").textContent = text; };
    box.querySelector("#rpm-sfx-check").onclick = () => {
        if (running) return;
        try { status(`网页当前模型：${modelVersion()}。本次检查不消耗积分。`); }
        catch (error) { status(error.message); }
    };
    box.querySelector("#rpm-sfx-folder").onclick = async () => {
        try {
            if (running) return;
            directory = await window.showDirectoryPicker({mode: "readwrite", id: "raid-party-sfx"});
            status(`输出目录：${directory.name}。Chrome 也应下载到此目录。`);
        } catch (error) { status(error.name === "AbortError" ? "未选择目录。" : error.message); }
    };

    async function processBatch(mode) {
        if (running) return;
        running = true;
        stopping = false;
        const test = mode === "test";
        const recover = mode === "recover";
        try {
            await navigator.locks.request(storageKey, {ifAvailable: true}, async lock => {
                if (!lock) throw new Error("另一个音效标签页正在运行。");
                requirePrivate();
                if (!directory) throw new Error("请先选择 WAV 输出目录。");
                await history();
                const options = test ? jobs.filter(item => !item.loop) : jobs;
                const limitInput = box.querySelector("#rpm-sfx-credits");
                const limit = recover ? 0 : limitInput.value ? Number(limitInput.value) : readCost()[1];
                if (!Number.isInteger(limit) || limit < 0) throw new Error("请输入有效的积分上限。");
                const journal = JSON.parse(localStorage.getItem(storageKey) || "{}");
                const record = () => localStorage.setItem(storageKey, JSON.stringify(journal));
                let spent = 0, saved = 0;
                for (const item of options) {
                    if (stopping) break;
                    if (await existing(directory, item)) continue;
                    let entry = journal[item.file];
                    if (entry && entry.signature !== item.signature) throw new Error(`${item.file}：清单已改变，请核对历史记录。`);
                    const localDownloads = await downloadedVariants(directory, item);
                    const localWavs = localDownloads.filter(download => download.type === "wav");
                    const preferred = localWavs.length ? localWavs : localDownloads;
                    const download = preferred.length === 1 ? preferred[0] : null;
                    // The website may place the completed sound straight into History.
                    // Reuse a matching history entry before charging for any new one.
                    if (!entry && (download || generationHeader(item))) {
                        entry = journal[item.file] = {signature: item.signature};
                        record();
                    }
                    if (!entry) {
                        if (preferred.length > 1) throw new Error(`${item.file} 有多个原始下载，请从历史核对正确的候选。`);
                        if (recover) continue;
                        await fill(item);
                        if (stopping) break;
                        requirePrivate();
                        const [price, available] = readCost();
                        if (price > available || spent + price > limit) throw new Error(`${item.file}：需 ${price} 积分，余额或批量上限不足。`);
                        const before = new Set(linksFor(item).map(a => a.href));
                        const button = one(buttons(document, generateLabel).filter(b => !disabled(b)), "生成音效按钮");
                        entry = journal[item.file] = {signature: item.signature, price, attemptedAt: Date.now()};
                        record();
                        spent += price;
                        status(`正在生成 ${item.file}（${price} 积分）…`);
                        button.click();
                        const result = await waitFor(() => {
                            if (generationHeader(item)) return {listed: true};
                            const next = linksFor(item).filter(link => !before.has(link.href));
                            if (next.length > 1) throw new Error("发现多个生成结果，请手动核对历史。");
                            return next[0] ? {link: next[0].href} : null;
                        }, "新生成结果；中断后只查历史，不会自动重发", 90000);
                        if (result.link) entry.history = result.link;
                        else entry.recoveredFromHistory = result.listed;
                        record();
                    }
                    let audio;
                    if (download) {
                        status(`正在校验已有下载 ${item.file}…`);
                        audio = await readVerifiedDownload(download, item);
                    } else {
                        status(`已进入历史核对：${item.file}，正在找下载按钮…`);
                        const menu = await variantMenu(item, entry.history);
                        status(`正在打开 ${item.file} 的下载菜单…`);
                        const {choice, type} = await downloadFormat(menu, item);
                        status(`等待 Chrome 将 ${item.file} 下载到所选目录…`);
                        audio = await downloadToDirectory(directory, item, choice, type);
                    }
                    status(`正在保存 ${item.file}…`);
                    await writeWav(directory, item, audio.data);
                    entry.download = audio.name;
                    entry.saved = true;
                    record();
                    saved++;
                    status(`已保存 ${item.file}（${audio.seconds.toFixed(3)} 秒）。`);
                    if (test) break;
                }
                const ending = stopping ? "已暂停" : test ? "单条测试结束" : recover ? "补下载结束" : "批量结束";
                status(`${ending}：本次保存 ${saved} 条，提交时显示共 ${spent} 积分。${test && saved ? "可点「开始批量」继续。" : ""}`);
            });
        } catch (error) {
            status(`已停止：${error.message}`);
        } finally {
            running = false;
        }
    }
    box.querySelector("#rpm-sfx-test").onclick = () => processBatch("test");
    box.querySelector("#rpm-sfx-run").onclick = () => processBatch("run");
    box.querySelector("#rpm-sfx-recover").onclick = () => processBatch("recover");
    box.querySelector("#rpm-sfx-stop").onclick = () => {
        if (running) { stopping = true; status("将在当前音效保存后暂停。"); }
    };
})();
