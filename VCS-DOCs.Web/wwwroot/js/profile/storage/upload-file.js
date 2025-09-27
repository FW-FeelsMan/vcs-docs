(function ($) {
    //D:\Unity\VCS-DOCs\VCS-DOCs.Web\wwwroot\js\profile\storage\upload-file.js
    // ===== Upload banner & chunk uploader with anti-flicker and single-owner polling =====
    // This script is safe to include multiple times across AJAX page swaps.

    // ---- Global singleton guard ----
    if (window.__uploadFileInitialized) {
        if (typeof window.fetchAndRenderUploadGate === "function") {
            window.fetchAndRenderUploadGate();
        }
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
        bannerMode: 'hidden',
        pausedReadsInRow: 0,
        currentController: null
    };

    // ---- Timings ----
    var UI_STALE_SECONDS = 30;
    var FLICKER_SMOOTH_READS = 2;
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
    function isBannerVisible() {
        return state.bannerMode === 'uploading' || state.bannerMode === 'paused';
    }
    function lockUploadUi(lock) {
        disableBtn(!!lock);
    }
    function isStorageVisible() {
        var s = document.getElementById('storage');
        return !!(s && s.classList.contains('active'));
    }
    function fmtSize(bytes) { return (bytes / 1024 / 1024).toFixed(2) + ' МБ'; }

    // ===== Storage counter =====
    async function updateStorageCounter() {
        try {
            var res = await fetch(cfg.endpoints.stats, { cache: 'no-store' });
            var data = null;
            if (res.ok) {
                data = await res.json();
            } else if (res.status === 404) {
                var uf = await fetch(cfg.endpoints.userFiles, { cache: 'no-store' });
                if (!uf.ok) return;
                data = await uf.json();
            } else {
                return;
            }

            var used = data.usedBytes || 0, temp = data.tempBytes || 0, limit = data.limitBytes || 0;
            var free = Math.max(0, (limit - used - temp));
            var changed = state.freeBytes !== free || (state._lastUsed !== used) || (state._lastTemp !== temp) || (state._lastLimit !== limit);
            state.freeBytes = free; state._lastUsed = used; state._lastTemp = temp; state._lastLimit = limit;

            var el = document.getElementById('storageCounter');
            if (el && changed) {
                el.textContent =
                    'Использовано: ' + fmtSize(used) +
                    ' из ' + fmtSize(limit) +
                    ' (временных: ' + fmtSize(temp) + '); свободно: ' + fmtSize(free);
            }

            if (free <= 0) lockUploadUi(true);
        } catch (e) { /* ignore */ }
    }

    // If #storageCounter appears later (SPA), refresh it immediately.
    (function observeStorageCounter() {
        const mo = new MutationObserver(() => {
            const el = document.getElementById('storageCounter');
            if (el) { updateStorageCounter(); mo.disconnect(); }
        });
        mo.observe(document.body, { childList: true, subtree: true });
    })();

    // polling controller
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
    function stopStatsPolling() {
        if (statsTimer) { clearInterval(statsTimer); statsTimer = null; keepTimer('statsTimer', null); }
    }

    // ===== Banner UI =====
    function ensureBanner() {
        var banner = document.getElementById('upload-busy-banner');
        if (!banner) {
            banner = document.createElement('div');
            banner.id = 'upload-busy-banner';

            banner.style.position = 'fixed';
            banner.style.bottom = '14px';
            banner.style.left = '45.5%';
            banner.style.transform = 'translateX(-50%)';
            banner.style.display = 'none';
            banner.style.padding = '2px 14px';
            banner.style.background = '#292929';
            banner.style.color = '#fff';
            banner.style.fontSize = '12px';
            banner.style.zIndex = '9999';
            banner.style.borderRadius = '8px';
            banner.style.boxShadow = '0 8px 24px rgba(0,0,0,.25)';
            banner.style.width = '850px';
            banner.style.boxSizing = 'border-box';
            banner.style.alignItems = 'center';
            banner.style.gap = '12px';
            banner.style.whiteSpace = 'normal';
            banner.style.wordBreak = 'break-word';
            banner.style.display = 'flex';

            var span = document.createElement('span');
            span.className = 'upload-busy-message';

            var actions = document.createElement('span');
            actions.style.display = 'inline-flex';
            actions.style.gap = '8px';
            actions.style.marginLeft = '8px';
            actions.style.flexShrink = '0';

            var btnContinue = document.createElement('button');
            btnContinue.id = 'upload-continue-btn';
            btnContinue.textContent = 'Продолжить';
            btnContinue.style.padding = '6px 10px';
            btnContinue.style.border = '0';
            btnContinue.style.borderRadius = '6px';
            btnContinue.style.cursor = 'pointer';
            btnContinue.style.background = '#2563eb';
            btnContinue.style.color = '#fff';

            var btnCancel = document.createElement('button');
            btnCancel.id = 'upload-cancel-btn';
            btnCancel.textContent = 'Отменить';
            btnCancel.style.padding = '6px 10px';
            btnCancel.style.border = '0';
            btnCancel.style.borderRadius = '6px';
            btnCancel.style.cursor = 'pointer';
            btnCancel.style.background = '#b91c1c';
            btnCancel.style.color = '#fff';

            actions.appendChild(btnContinue);
            actions.appendChild(btnCancel);
            banner.appendChild(span);
            banner.appendChild(actions);
            document.body.appendChild(banner);
        }
        return banner;
    }

    function ensureCancelModal() {
        var modal = document.getElementById('upload-cancel-modal');
        var msg = document.getElementById('upload-cancel-message');
        var ok = document.getElementById('upload-cancel-confirm');
        var close = document.getElementById('upload-cancel-close');
        if (!modal || !msg || !ok || !close) return null;
        return { modal: modal, msg: msg, ok: ok, close: close };
    }

    function showCancelConfirm(percent, fileName, onConfirm) {
        var m = ensureCancelModal();
        if (!m) {
            if (confirm('Вы уверены? Было загружено ' + percent + '%\n' + fileName + '\nОтмена загрузки очистит прогресс.')) {
                if (typeof onConfirm === 'function') onConfirm();
            }
            return;
        }
        m.msg.innerHTML = 'Вы уверены? Было загружено ' + percent + '%<br />' +
            (fileName || 'файл') + '<br />' +
            'Отмена загрузки очистит прогресс.';
        m.modal.style.display = 'block';
        m.ok.onclick = function () {
            m.modal.style.display = 'none';
            if (typeof onConfirm === 'function') onConfirm();
        };
        m.close.onclick = function () { m.modal.style.display = 'none'; };
    }

    function preferFresh(active) {
        if (!active) return false;
        if (typeof active.isFresh === 'boolean') return active.isFresh && !active.stopped;
        if (typeof active.ageSec === 'number') return active.ageSec <= UI_STALE_SECONDS && !active.stopped;
        return false;
    }

    function computePercent(active) {
        if (state.isUploading && state.total > 0) {
            return Math.min(99, Math.floor((state.index / state.total) * 100));
        }
        if (active && typeof active.uploadedBytes === 'number' && active.fileSize > 0) {
            return Math.max(0, Math.min(99, Math.floor((active.uploadedBytes / active.fileSize) * 100)));
        }
        if (active && Array.isArray(active.uploaded) && active.fileSize > 0) {
            var approx = (active.uploaded.length * cfg.chunkSize) / active.fileSize;
            return Math.max(0, Math.min(99, Math.floor(approx * 100)));
        }
        return 0;
    }

    function setBannerMode(nextMode, active) {
        if (nextMode === state.bannerMode) {
            renderBanner(active, nextMode !== 'hidden');
            return;
        }
        state.bannerMode = nextMode;
        renderBanner(active, nextMode !== 'hidden');
    }

    function renderBanner(active, on) {
        if (!renderBanner._cache) renderBanner._cache = { on: null, text: "", mode: null };
        var b = ensureBanner();
        var span = b.querySelector('.upload-busy-message');
        var btnCancel = document.getElementById('upload-cancel-btn');
        var btnContinue = document.getElementById('upload-continue-btn');

        var percent = computePercent(active);
        var name = (active && active.fileName) || (state.file && state.file.name) || 'файл';

        if (state.bannerMode === 'uploading') {
            span.textContent = 'Идёт загрузка: ' + name + ' — ' + percent + '% . Если закроете страницу или обновите — загрузка прервётся.';
            btnContinue.style.display = 'none';
            btnCancel.style.display = 'inline-block';
        } else if (state.bannerMode === 'paused') {
            span.textContent = 'Загрузка прервалась: ' + name + ' — ' + percent + '%. Можно продолжить с тем же файлом или отменить.';
            btnContinue.style.display = 'inline-block';
            btnCancel.style.display = 'inline-block';
        } else {
            span.textContent = '';
            btnContinue.style.display = 'none';
            btnCancel.style.display = 'none';
        }

        var nextText =
            state.bannerMode === 'uploading'
                ? ('Идёт загрузка: ' + name + ' — ' + percent + '% . Если закроете страницу или обновите — загрузка прервётся.')
                : state.bannerMode === 'paused'
                    ? ('Загрузка прервалась: ' + name + ' — ' + percent + '%. Можно продолжить с тем же файлом или отменить.')
                    : '';
        if (renderBanner._cache.text !== nextText) {
            span.textContent = nextText;
            renderBanner._cache.text = nextText;
        }
        var nextDisplay = on ? 'block' : 'none';
        if (renderBanner._cache.on !== nextDisplay) {
            b.style.display = nextDisplay;
            renderBanner._cache.on = nextDisplay;
        }
        renderBanner._cache.mode = state.bannerMode;

        lockUploadUi(on && state.bannerMode === 'uploading');

        btnContinue.onclick = async function () {
            if (!active) return;
            var input = document.querySelector(cfg.inputSelector);
            if (input) {
                input.disabled = false;
                input.value = null;
                input.click();
            }
        };
        btnCancel.onclick = function () {
            var cur = active || state.active || {};
            var fname = cur.fileName || (state.file && state.file.name) || 'файл';
            var p = computePercent(active);
            showCancelConfirm(p, fname, async function () {
                try {
                    state.cancelRequested = true;
                    try { state.currentController?.abort(); } catch { }
                    try { if (pollTimer) { clearInterval(pollTimer); pollTimer = null; keepTimer('pollTimer', null); } } catch { }
                    try { stopHeartbeat(); } catch { }
                    var hash = state.hash || (cur && cur.fileHash) || '';
                    sendStopped(hash);
                    var fd = new FormData();
                    fd.append('fileName', fname);
                    fd.append('fileHash', hash);
                    await fetch(cfg.endpoints.restart, { method: 'POST', body: fd });
                    state.active = null;
                    state.index = 0;
                    state.total = 0;
                    setBannerMode('hidden', null);
                    await fetchAndRenderUploadGate();
                    updateStorageCounter();
                    if (!pollTimer) {
                        pollTimer = setInterval(fetchAndRenderUploadGate, ACTIVE_POLL_MS);
                        keepTimer('pollTimer', pollTimer);
                    }
                    if (!isBannerVisible() && (state.freeBytes === null || state.freeBytes > 0))
                        lockUploadUi(false);
                } catch { }
            });
        };
    }
    function showBanner(active, on) { renderBanner(active, on); }

    // ===== Active upload polling =====
    async function fetchAndRenderUploadGate() {
        try {
            var r = await fetch(cfg.endpoints.active, { cache: 'no-store' });
            if (!r.ok) return;
            var active = await r.json();
            if (active && active.found) {
                state.active = active;
                var isFresh = preferFresh(active);
                var live = !!state.isUploading || isFresh;
                if (live) {
                    state.pausedReadsInRow = 0;
                    setBannerMode('uploading', active);
                } else {
                    state.pausedReadsInRow += 1;
                    if (state.pausedReadsInRow >= FLICKER_SMOOTH_READS) {
                        setBannerMode('paused', active);
                    } else {
                        renderBanner(active, true);
                    }
                }
                if (state.isUploading) lockUploadUi(true);
            } else {
                state.pausedReadsInRow = 0;
                setBannerMode('hidden', null);
                state.active = null;
                if (state.freeBytes === null || state.freeBytes > 0) {
                    lockUploadUi(false);
                }
            }
        } catch { }
    }
    window.fetchAndRenderUploadGate = fetchAndRenderUploadGate;

    // ===== Page lifecycle =====
    function installBeforeUnload() {
        window.addEventListener('beforeunload', function () {
            var hash = (state && state.hash) || (state.active && state.active.fileHash) || '';
            if (hash) sendStopped(hash);
        });
        document.addEventListener('visibilitychange', function () {
            if (!document.hidden) {
                fetchAndRenderUploadGate();
            }
        });
    }

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

    // ===== Uploading state =====
    function setUploading(flag) {
        state.isUploading = !!flag;

        if (flag) {
            lockUploadUi(true);
        } else {
            if (!isBannerVisible() && (state.freeBytes === null || state.freeBytes > 0)) {
                lockUploadUi(false);
            }
        }

        if (flag) {
            startHeartbeat();
            adjustStatsInterval(2000);
        } else {
            stopHeartbeat();
            adjustStatsInterval(5000);
        }
        showBanner(state.active, flag || !!state.active);
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
        if (state.forceVersion != null) {
            fd.append('targetVersion', String(state.forceVersion));
        }

        const maxAttempts = 4;
        let lastErr = null;

        for (let attempt = 1; attempt <= maxAttempts; attempt++) {
            const t0 = performance.now();
            try {
                const res = await fetch(cfg.endpoints.chunk, { method: 'POST', body: fd, signal: controller.signal });
                const t1 = performance.now();
                console.log(`[upload] chunk ${index + 1}/${total}: fetch=${((t1 - t0) / 1000).toFixed(2)}s, size=${((end - start) / 1024 / 1024).toFixed(2)}MB`);

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
                        console.warn('AV unavailable, proceeding by policy');
                        data = data || { nextExpectedIndex: index + 1 };

                        const dt = (performance.now() - t0) / 1000;
                        const mb = (end - start) / (1024 * 1024);
                        const mbps = dt > 0 ? (mb / dt) : 0;
                        window.__u_hist = window.__u_hist || [];
                        window.__u_hist.push(mbps);
                        if (window.__u_hist.length > 20) window.__u_hist.shift();
                        if (index % 200 === 0 || index === total - 1) {
                            const avg20 = window.__u_hist.reduce((a, b) => a + b, 0) / window.__u_hist.length;
                            console.log(`[upload] chunk ${index + 1}/${total}: ${mb.toFixed(2)} MB за ${dt.toFixed(2)}s => ${mbps.toFixed(2)} MB/s (avg20=${avg20.toFixed(2)} MB/s) [AV 503]`);
                        }

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

                const dt = (performance.now() - t0) / 1000;
                const mb = (end - start) / (1024 * 1024);
                const mbps = dt > 0 ? (mb / dt) : 0;
                window.__u_hist = window.__u_hist || [];
                window.__u_hist.push(mbps);
                if (window.__u_hist.length > 20) window.__u_hist.shift();
                if (index % 50 === 0 || index === total - 1) {
                    const avg20 = window.__u_hist.reduce((a, b) => a + b, 0) / window.__u_hist.length;
                    console.log(`[upload] chunk ${index + 1}/${total}: ${mb.toFixed(2)} MB за ${dt.toFixed(2)}s => ${mbps.toFixed(2)} MB/s (avg20=${avg20.toFixed(2)} MB/s)`);
                }

                try { if ((index % 2) === 0) updateStorageCounter(); } catch { }

                return data || {};
            } catch (e) {
                if (e && (e.name === 'AbortError' || /abort/i.test(String(e.message)))) {
                    return { aborted: true, nextExpectedIndex: index };
                }
                lastErr = e;
                if (attempt < maxAttempts && (
                    (e && /network|fetch|timeout|av_unavailable/i.test(String(e.message || e))) || !e.message
                )) {
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
                    var progEl = document.getElementById('uploadProgress');
                    if (progEl) {
                        var percent = Math.min(100, Math.floor(((state.index + 1) / state.total) * 100));
                        progEl.textContent = percent + '%';
                    }
                    state.index++;
                    if (state.index % 8 === 0) await fetchAndRenderUploadGate();
                    continue;
                }

                var r = await uploadChunk(file, state.hash, state.index, state.total);
                if (r && r.aborted) {
                    break;
                }
                var next = (typeof r.nextExpectedIndex === 'number') ? r.nextExpectedIndex : (state.index + 1);
                state.index = next;

                var prog = document.getElementById('uploadProgress');
                if (prog) {
                    var p = Math.min(100, Math.floor((state.index / state.total) * 100));
                    prog.textContent = p + '%';
                }

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
            state.active = null;
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
            if (isBannerVisible() || (state.freeBytes !== null && state.freeBytes <= 0)) return;

            var input = document.querySelector(cfg.inputSelector);
            if (input) { input.value = null; input.click(); }
        });

        $(document).off('change.uploadFile').on('change.uploadFile', cfg.inputSelector, async function (e) {
            var file = e.target.files && e.target.files[0];
            if (!file) return;

            await fetchAndRenderUploadGate();
            await updateStorageCounter();

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
                                setUploading(true);
                                await startUpload(file, null);
                            } catch { }
                        },
                        onNewVersion: async function () {
                            state.hash = await quickFingerprint(file);
                            state.forceVersion = null;
                            setUploading(true);
                            await startUpload(file, null);
                        },
                        onCancel: function () { }
                    }
                );
                return;
            }

            state.hash = await quickFingerprint(file);
            state.forceVersion = null;
            setUploading(true);
            await startUpload(file, null);
        });
    }

    // ---- Initialize once ----
    (function init() {
        bindHandlers();
        installBeforeUnload();
        fetchAndRenderUploadGate();
        updateStorageCounter();
        ensureStatsPolling();

        if (!pollTimer) {
            pollTimer = setInterval(fetchAndRenderUploadGate, ACTIVE_POLL_MS);
            keepTimer('pollTimer', pollTimer);
        }
    })();
})(jQuery);
