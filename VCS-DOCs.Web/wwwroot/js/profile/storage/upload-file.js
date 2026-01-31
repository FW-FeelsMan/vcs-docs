(function ($) {
    // D:\Unity\VCS-DOCs\VCS-DOCs.Web\wwwroot\js\profile\storage\upload-file.js

    // ---- Global singleton guard ----
    if (window.__uploadFileInitialized) {
        try { if (typeof window.fetchAndRenderUploadGate === "function") window.fetchAndRenderUploadGate(); } catch { }
        return;
    }
    window.__uploadFileInitialized = true;

    // ---- Config ----
    var cfg = {
        buttonSelector: '#uploadFileButton',
        inputSelector: '#hiddenFileInput',
        chunkSize: 16 * 1024 * 1024,
        endpoints: {
            active: '/api/Upload/active',
            heartbeat: '/api/Upload/heartbeat',
            stopped: '/api/Upload/stopped',
            status: '/api/Upload/upload-status',
            check: '/api/Upload/check-version-conflict',
            restart: '/api/Upload/restart',
            chunk: '/api/Upload/chunk',
            userFiles: '/api/Upload/user-files',
            stats: '/api/Upload/stats',
            delete: '/api/Upload/delete/'
        }
    };

    // ---- State ----
    var state = {
        isUploading: false,
        cancelRequested: false,
        active: null,
        file: null,
        hash: null,
        index: 0,
        total: 0,
        skip: null,
        freeBytes: null,
        forceVersion: null,
        currentController: null,

        resumePick: false,
        resumeExpectedHash: null,
        resumeExpectedName: null,
        resumeExpectedSize: null
    };
    // ===== Debug switch =====
    var UDBG = (window.__UPLOAD_DEBUG === true);
    function ulog() { if (!UDBG) return; try { console.log.apply(console, arguments); } catch { } }
    function uwarn() { if (!UDBG) return; try { console.warn.apply(console, arguments); } catch { } }
    function utime() { try { return new Date().toLocaleTimeString(); } catch { return ""; } }

    // ---- Timings ----
    var UI_STALE_SECONDS = 30;
    var ACTIVE_POLL_MS = 1500;

    // ---- Timers (global) ----
    if (!window.__uploadTimers) window.__uploadTimers = {};
    let hbTimer = window.__uploadTimers.hbTimer || null;
    let statsTimer = window.__uploadTimers.statsTimer || null;
    let pollTimer = window.__uploadTimers.pollTimer || null;
    let statsEveryMs = 5000;
    function keepTimer(name, val) { window.__uploadTimers[name] = val; }

    // ---- Helpers ----
    function disableBtn(disabled) {
        var btn = document.querySelector(cfg.buttonSelector);
        if (btn) btn.disabled = !!disabled;
        var input = document.querySelector(cfg.inputSelector);
        if (input) input.disabled = !!disabled;
        document.body.classList.toggle('upload-busy', !!disabled);
    }
    function lockUploadUi(lock) {
        disableBtn(!!lock);
        ulog(`[UP ${utime()}] lockUploadUi(${!!lock}) active=${!!state.active} freeBytes=${state.freeBytes}`);
    }

    function isStorageVisible() {
        var s = document.getElementById('storage');
        return !!(s && s.classList.contains('active'));
    }
    function fmtSize(bytes) { return (bytes / 1024 / 1024).toFixed(2) + ' МБ'; }

    function preferFresh(active) {
        if (!active) return false;
        if (typeof active.isFresh === 'boolean') return active.isFresh && !active.stopped;
        if (typeof active.ageSec === 'number') return active.ageSec <= UI_STALE_SECONDS && !active.stopped;
        return false;
    }

    // ===== Storage counter =====
    async function updateStorageCounter() {
        try {
            var res = await fetch(cfg.endpoints.stats, { cache: 'no-store' });
            var data = null;
            if (res.ok) data = await res.json();
            else if (res.status === 404) {
                var uf = await fetch(cfg.endpoints.userFiles, { cache: 'no-store' });
                if (!uf.ok) return;
                data = await uf.json();
            } else return;

            var used = data.usedBytes || 0, temp = data.tempBytes || 0, limit = data.limitBytes || 0;
            var free = Math.max(0, (limit - used - temp));
            state.freeBytes = free;

            var el = document.getElementById('storageCounter');
            if (el) {
                el.textContent =
                    'Использовано: ' + fmtSize(used) +
                    ' из ' + fmtSize(limit) +
                    ' (временных: ' + fmtSize(temp) + '); свободно: ' + fmtSize(free);
            }

            if (free <= 0) lockUploadUi(true);
        } catch { }
    }

    // ===== Active upload polling (for logic, UI is dock now) =====
    async function fetchAndRenderUploadGate() {
        try {
            var r = await fetch(cfg.endpoints.active, { cache: 'no-store' });
            if (!r.ok) return;
            var active = await r.json();
            state.active = (active && active.found) ? active : null;
            ulog(
                `[UP ${utime()}] /active found=${!!(active && active.found)} ` +
                (active && active.found
                    ? (`ageSec=${active.ageSec} isFresh=${active.isFresh} stopped=${active.stopped} uploadedBytes=${active.uploadedBytes}/${active.fileSize}`)
                    : '')
            );

            // правило UI: если есть активная/paused сессия — блокируем кнопку/инпут в ЛК
            if (state.active) lockUploadUi(true);
            else if (state.freeBytes === null || state.freeBytes > 0) lockUploadUi(false);
        } catch { }
    }
    window.fetchAndRenderUploadGate = fetchAndRenderUploadGate;

    // ===== Heartbeat =====
    function startHeartbeat() {
        if (hbTimer || !state.hash) return;
        hbTimer = setInterval(function () {
            try {
                var fd = new FormData();
                fd.append('fileHash', state.hash);
                fetch(cfg.endpoints.heartbeat, { method: 'POST', body: fd });
            } catch { }
        }, 6000);
        keepTimer('hbTimer', hbTimer);
    }
    function stopHeartbeat() {
        if (hbTimer) { clearInterval(hbTimer); hbTimer = null; keepTimer('hbTimer', null); }
    }

    function setUploading(flag) {
        state.isUploading = !!flag;
        if (flag) {
            lockUploadUi(true);
            startHeartbeat();
            adjustStatsInterval(2000);
        } else {
            stopHeartbeat();
            adjustStatsInterval(5000);
            if (!state.active && (state.freeBytes === null || state.freeBytes > 0)) lockUploadUi(false);
        }
    }

    // ===== Stats polling =====
    function ensureStatsPolling() {
        if (statsTimer) return;
        statsTimer = setInterval(() => {
            if (isStorageVisible()) updateStorageCounter();
        }, statsEveryMs);
        keepTimer('statsTimer', statsTimer);
    }
    function adjustStatsInterval(ms) {
        statsEveryMs = ms;
        if (statsTimer) { clearInterval(statsTimer); statsTimer = null; keepTimer('statsTimer', null); }
        ensureStatsPolling();
    }

    // ===== Fingerprint =====
    async function sha256Hex(buf) {
        var hash = await crypto.subtle.digest('SHA-256', buf);
        var arr = Array.from(new Uint8Array(hash));
        return arr.map(function (b) { return b.toString(16).padStart(2, '0'); }).join('');
    }

    async function quickFingerprint(file) {
        var size = file.size;
        var firstLen = Math.min(1024 * 1024, size);
        var firstBuf = await file.slice(0, firstLen).arrayBuffer();
        var firstHash = await sha256Hex(firstBuf);
        var lastHash = '';
        if (size > 1024 * 1024) {
            var lastBuf = await file.slice(size - (1024 * 1024), size).arrayBuffer();
            lastHash = await sha256Hex(lastBuf);
        }
        return 'fp:' + size + ':' + firstHash + ':' + lastHash;
    }

    // ===== Stop / Restart =====
    function sendStopped(hash) {
        if (!hash) return;
        try {
            var fd = new FormData();
            fd.append('fileHash', hash);
            if (!navigator.sendBeacon || !navigator.sendBeacon(cfg.endpoints.stopped, fd)) {
                fetch(cfg.endpoints.stopped, { method: 'POST', body: fd, keepalive: true }).catch(function () { });
            }
        } catch { }
    }

    async function restartOnServer(fileName, hash) {
        var fd = new FormData();
        fd.append('fileName', fileName);
        fd.append('fileHash', hash);
        var res = await fetch(cfg.endpoints.restart, { method: 'POST', body: fd });
        if (!res.ok) throw new Error('restart failed');
        return res.json();
    }

    // ===== Resume picker (hook for dock) =====
    function resetResumeFlags() {
        state.resumePick = false;
        state.resumeExpectedHash = null;
        state.resumeExpectedName = null;
        state.resumeExpectedSize = null;
    }

    function openFilePickerWithExpected(expected) {
        if (!expected) return;

        state.resumePick = true;
        state.resumeExpectedHash = expected.fileHash || null;
        state.resumeExpectedName = expected.fileName || null;
        state.resumeExpectedSize = expected.fileSize != null ? expected.fileSize : null;

        var tries = 0;
        function attempt() {
            tries++;
            var input = document.querySelector(cfg.inputSelector);
            if (input) {
                input.disabled = false;
                input.value = null;
                input.click();

                // после открытия окна выбора файла не трогаем dock — он всегда живёт отдельно
                return true;
            }
            if (tries < 30) setTimeout(attempt, 120);
            return false;
        }
        attempt();
    }

    // IMPORTANT: dock вызывает этот хук — тогда picker откроется сразу и dock не исчезнет
    window.__uploadGateOpenPicker = function (intent) {
        // intent может прийти из dock (local/global), либо возьмём state.active
        var a = intent || (state.active || null);
        if (!a) return;
        openFilePickerWithExpected({
            fileHash: a.fileHash || null,
            fileName: a.fileName || null,
            fileSize: a.fileSize != null ? a.fileSize : null
        });
    };

    // если при входе в ЛК уже был intent от dock — откроем picker автоматически
    function applyResumeIntentIfAny() {
        try {
            var intent = window.__uploadGateResumeIntent;
            if (!intent) return;
            window.__uploadGateResumeIntent = null;
            openFilePickerWithExpected(intent);
        } catch { }
    }

    // ===== Chunk upload =====
    async function uploadChunk(file, hash, index, total) {
        const controller = new AbortController();
        state.currentController = controller;

        const start = index * cfg.chunkSize;
        const end = Math.min(start + cfg.chunkSize, file.size);
        const blob = file.slice(start, end);

        const fd = new FormData();
        fd.append('chunk', blob, 'chunk_' + index);
        fd.append('hash', hash);
        fd.append('chunkIndex', String(index));
        fd.append('totalChunks', String(total));
        fd.append('fileSize', String(file.size));
        fd.append('fileName', file.name);
        if (state.forceVersion != null) fd.append('targetVersion', String(state.forceVersion));

        const maxAttempts = 4;
        let lastErr = null;

        for (let attempt = 1; attempt <= maxAttempts; attempt++) {
            const t0 = performance.now();
            try {
                const res = await fetch(cfg.endpoints.chunk, { method: 'POST', body: fd, signal: controller.signal });

                let bodyText = '';
                let data = null;
                try { data = await res.clone().json(); } catch { try { bodyText = await res.text(); } catch { bodyText = ''; } }

                if (!res.ok) {
                    const msg = (data && (data.message || data.error)) || bodyText || '';
                    if (res.status === 507 || /Недостаточно места/i.test(msg)) {
                        alert('Недостаточно места на диске. Освободите место и попробуйте снова.');
                        throw new Error('insufficient_storage');
                    }
                    if (res.status === 503 || /(antivirus|clamav|service unavailable|timeout|av_unavailable|av_timeout)/i.test(msg || '')) {
                        uwarn(
                            `[UP ${utime()}] CHUNK ${index + 1}/${total} got HTTP 503 => proceeding by policy. ` +
                            `msg="${String(msg).slice(0, 120)}"`
                        );
                        data = data || { nextExpectedIndex: index + 1 };
                        try { if ((index % 2) === 0) updateStorageCounter(); } catch { }
                        return data;
                    }

                    if (res.status === 409 && /infected/i.test(msg)) {
                        alert('Файл отклонён антивирусной проверкой. Он не был сохранён.');
                        throw new Error('infected');
                    }
                    if (res.status === 409 && msg) {
                        alert(msg);
                        throw new Error(msg);
                    }
                    throw new Error(msg || `upload failed (HTTP ${res.status})`);
                }

                try { if ((index % 2) === 0) updateStorageCounter(); } catch { }
                return data || {};
            } catch (e) {
                if (e && (e.name === 'AbortError' || /abort/i.test(String(e.message)))) {
                    return { aborted: true, nextExpectedIndex: index };
                }
                lastErr = e;
                if (attempt < maxAttempts) {
                    const backoff = 500 * Math.pow(2, attempt - 1);
                    await new Promise(r => setTimeout(r, backoff));
                    continue;
                }
                const details = (e && e.message) ? e.message : 'unknown';
                const mb = (cfg.chunkSize / (1024 * 1024));
                throw new Error(`Chunk #${index + 1}/${total} (${mb}MB) failed: ${details}`);
            }
        }

        throw lastErr || new Error('upload failed');
    }

    // ===== Upload flow =====
    async function startUpload(file, skipSet) {
        state.file = file;
        state.index = 0;
        state.total = Math.ceil(file.size / cfg.chunkSize);
        state.skip = skipSet || null;
        state.cancelRequested = false;

        setUploading(true);

        try {
            while (state.index < state.total) {
                if (state.cancelRequested) {
                    try {
                        sendStopped(state.hash);
                        await restartOnServer(file.name, state.hash);
                    } catch { }
                    break;
                }

                if (state.skip && state.skip.has(state.index)) {
                    state.index++;
                    continue;
                }

                var r = await uploadChunk(file, state.hash, state.index, state.total);
                if (r && r.aborted) break;

                var next = (typeof r.nextExpectedIndex === 'number') ? r.nextExpectedIndex : (state.index + 1);
                state.index = next;

                if ((state.index % 4) === 0) {
                    try { updateStorageCounter(); } catch { }
                    await fetchAndRenderUploadGate();
                }
            }

            if (!state.cancelRequested) {
                if (typeof window.initStorageTable === 'function') window.initStorageTable();
                else if (typeof window.fetchFiles === 'function') window.fetchFiles();
            }
        } finally {
            setUploading(false);
            state.forceVersion = null;
            state.currentController = null;
            await fetchAndRenderUploadGate();
            updateStorageCounter();
        }
    }

    // ===== Bind UI handlers =====
    function bindHandlers() {
        $(document).off('click.uploadFile').on('click.uploadFile', cfg.buttonSelector, async function () {
            await fetchAndRenderUploadGate();
            await updateStorageCounter();
            if (state.active || (state.freeBytes !== null && state.freeBytes <= 0)) return;

            var input = document.querySelector(cfg.inputSelector);
            if (input) { input.value = null; input.click(); }
        });

        $(document).off('change.uploadFile').on('change.uploadFile', cfg.inputSelector, async function (e) {
            var file = e.target.files && e.target.files[0];
            if (!file) return;

            await fetchAndRenderUploadGate();
            await updateStorageCounter();

            var active = state.active;

            // resume flow (paused)
            if (active && active.found && state.resumePick) {
                var expectedSize = state.resumeExpectedSize != null ? state.resumeExpectedSize : (active.fileSize != null ? active.fileSize : null);
                var expectedHash = state.resumeExpectedHash || active.fileHash || null;

                if (expectedSize != null && file.size !== expectedSize) {
                    alert('Выбран не тот файл: размер не совпадает. Выберите исходный файл, который загружался ранее.');
                    return;
                }

                var fp = await quickFingerprint(file);
                if (expectedHash && fp !== expectedHash) {
                    alert('Выбран не тот файл. Выберите тот же файл, который был поставлен на загрузку, чтобы продолжить.');
                    return;
                }

                var uploaded = Array.isArray(active.uploaded) ? active.uploaded : [];
                var skipSet = new Set(uploaded);

                state.hash = expectedHash || fp;
                resetResumeFlags();
                await startUpload(file, skipSet);
                return;
            }

            resetResumeFlags();

            // normal start
            const SAFETY_BYTES = 500 * 1024 * 1024;
            if ((state.freeBytes !== null) && (file.size > Math.max(0, state.freeBytes - SAFETY_BYTES))) {
                alert('Недостаточно места с учётом резервирования (500 МБ). Освободите место и попробуйте снова.');
                return;
            }
            if ((state.freeBytes !== null && file.size > state.freeBytes)) {
                alert('Невозможно начать загрузку: недостаточно места.\nСвободно: ' + fmtSize(state.freeBytes) + ', файл: ' + fmtSize(file.size));
                return;
            }
            if (state.active && preferFresh(state.active)) return;

            var fdCheck = new FormData();
            fdCheck.append('fileName', file.name);
            var res = await fetch(cfg.endpoints.check, { method: 'POST', body: fdCheck });
            var conflict = res.ok ? (await res.json()).conflict : false;

            if (conflict && typeof window.showConflictModal === 'function') {
                window.showConflictModal(
                    file.name,
                    'conflict',
                    {
                        onReplace: async function (version) {
                            try {
                                try {
                                    var uf = await fetch(cfg.endpoints.userFiles, { cache: 'no-store' });
                                    var data = await uf.json();
                                    var files = Array.isArray(data.files) ? data.files : [];
                                    var entry = files.find(function (f) { return (f.fileName || f.FileName) === file.name; });
                                    if (entry) {
                                        var gid = entry.fileGroupId || entry.FileGroupId;
                                        await fetch(cfg.endpoints.delete + gid + '/' + version, { method: 'DELETE' });
                                    }
                                } catch { }
                                state.hash = await quickFingerprint(file);
                                state.forceVersion = Number(version);
                                await startUpload(file, null);
                            } catch { }
                        },
                        onNewVersion: async function () {
                            state.hash = await quickFingerprint(file);
                            state.forceVersion = null;
                            await startUpload(file, null);
                        },
                        onCancel: function () { }
                    }
                );
                return;
            }

            state.hash = await quickFingerprint(file);
            state.forceVersion = null;
            await startUpload(file, null);
        });
    }

    // ===== Init =====
    (function init() {
        bindHandlers();
        fetchAndRenderUploadGate();
        updateStorageCounter();
        ensureStatsPolling();

        // логика: uploader может работать, но UI — всегда dock
        applyResumeIntentIfAny();

        if (!pollTimer) {
            pollTimer = setInterval(fetchAndRenderUploadGate, ACTIVE_POLL_MS);
            keepTimer('pollTimer', pollTimer);
        }
    })();
})(jQuery);
