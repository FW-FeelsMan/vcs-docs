(function ($) {
    // ===== Upload banner & chunk uploader with anti-flicker and single-owner polling =====
    // This script is safe to include multiple times across AJAX page swaps.

    // ---- Global singleton guard ----
    if (window.__uploadFileInitialized) {
        // Reattach light listeners if needed, but don't start new timers
        if (typeof window.fetchAndRenderUploadGate === "function") {
            window.fetchAndRenderUploadGate(); // force a refresh when user returns
        }
        return;
    }
    window.__uploadFileInitialized = true;

    var cfg = {
        buttonSelector: '#uploadFileButton',
        inputSelector: '#hiddenFileInput',
        chunkSize: 2 * 1024 * 1024,
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
        bannerMode: 'hidden',           // 'hidden' | 'uploading' | 'paused'
        pausedReadsInRow: 0
    };

    // timings
    var UI_STALE_SECONDS = 30;          // local grace to treat session as fresh
    var FLICKER_SMOOTH_READS = 2;       // require 2 consecutive "paused" reads to switch text
    var ACTIVE_POLL_MS = 1500;

    // timers (global to survive partial reloads)
    if (!window.__uploadTimers) window.__uploadTimers = {};
    var hbTimer = window.__uploadTimers.hbTimer || null;
    var statsTimer = window.__uploadTimers.statsTimer || null;
    var pollTimer = window.__uploadTimers.pollTimer || null;

    function keepTimer(name, val) { window.__uploadTimers[name] = val; }

    function disableBtn(disabled) {
        var btn = document.querySelector(cfg.buttonSelector);
        if (btn) btn.disabled = !!disabled;
    }

    function fmtSize(bytes) { return (bytes / 1024 / 1024).toFixed(2) + ' МБ'; }

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
            state.freeBytes = free;
            var el = document.getElementById('storageCounter');
            if (el) {
                el.textContent = 'Использовано: ' + fmtSize(used) + ' из ' + fmtSize(limit) + ' (временных: ' + fmtSize(temp) + '); свободно: ' + fmtSize(free);
            }
            if (!state.isUploading && (!state.active || state.active.stopped) && free <= 0) {
                disableBtn(true);
            } else if (!state.isUploading && (!state.active || state.active.stopped)) {
                disableBtn(false);
            }
        } catch (e) { /* ignore */ }
    }

    function ensureBanner() {
        var banner = document.getElementById('upload-busy-banner');
        if (!banner) {
            banner = document.createElement('div');
            banner.id = 'upload-busy-banner';

            banner.style.position = 'fixed';
            banner.style.bottom = '12px';
            banner.style.left = '50%';
            banner.style.transform = 'translateX(-50%)';
            banner.style.display = 'none';
            banner.style.padding = '10px 14px';
            banner.style.background = '#111827';
            banner.style.color = '#fff';
            banner.style.fontSize = '14px';
            banner.style.zIndex = '9999';
            banner.style.borderRadius = '12px';
            banner.style.boxShadow = '0 8px 24px rgba(0,0,0,.25)';
            banner.style.maxWidth = 'min(90vw, 800px)';
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

    function startHeartbeat() {
        if (hbTimer || !state.hash) return;
        hbTimer = setInterval(function () {
            try {
                var fd = new FormData();
                fd.append('fileHash', state.hash);
                fetch(cfg.endpoints.heartbeat, { method: 'POST', body: fd });
            } catch { }
        }, 2000);
        keepTimer('hbTimer', hbTimer);
    }
    function stopHeartbeat() {
        if (hbTimer) { clearInterval(hbTimer); hbTimer = null; keepTimer('hbTimer', null); }
    }

    function startStatsPolling() {
        if (statsTimer) return;
        statsTimer = setInterval(updateStorageCounter, 5000);
        keepTimer('statsTimer', statsTimer);
    }
    function stopStatsPolling() {
        if (statsTimer) { clearInterval(statsTimer); statsTimer = null; keepTimer('statsTimer', null); }
    }

    function setBannerMode(nextMode, active) {
        if (nextMode === state.bannerMode) {
            // still update contents (percent)
            renderBanner(active, nextMode !== 'hidden');
            return;
        }
        state.bannerMode = nextMode;
        renderBanner(active, nextMode !== 'hidden');
    }

    function renderBanner(active, on) {
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
        b.style.display = on ? 'block' : 'none';

        btnContinue.onclick = async function () {
            if (!active) return;
            var input = document.querySelector(cfg.inputSelector);
            if (input) { input.value = null; input.click(); }
        };
        btnCancel.onclick = function () {
            var cur = active || state.active || {};
            var fname = cur.fileName || (state.file && state.file.name) || 'файл';
            var p = computePercent(active);
            showCancelConfirm(p, fname, async function () {
                try {
                    state.cancelRequested = true;
                    var hash = state.hash || (cur && cur.fileHash) || '';
                    sendStopped(hash);
                    var fd = new FormData();
                    fd.append('fileName', fname);
                    fd.append('fileHash', hash);
                    await fetch(cfg.endpoints.restart, { method: 'POST', body: fd });
                    state.active = null;
                    await fetchAndRenderUploadGate();
                    updateStorageCounter();
                } catch { }
            });
        };
    }
    function showBanner(active, on) { renderBanner(active, on); }

    async function fetchAndRenderUploadGate() {
        try {
            var r = await fetch(cfg.endpoints.active, { cache: 'no-store' });
            if (!r.ok) return;
            var active = await r.json();
            if (active && active.found) {
                state.active = active;
                var isFresh = preferFresh(active);
                // Hysteresis to avoid flicker between "uploading" and "paused"
                var live = !!state.isUploading || isFresh;
                if (live) {
                    state.pausedReadsInRow = 0;
                    setBannerMode('uploading', active);
                } else {
                    state.pausedReadsInRow += 1;
                    if (state.pausedReadsInRow >= FLICKER_SMOOTH_READS) {
                        setBannerMode('paused', active);
                    } else {
                        // keep showing previous mode (likely 'uploading')
                        renderBanner(active, true);
                    }
                }
                disableBtn(isFresh && !active.stopped);
            } else {
                state.pausedReadsInRow = 0;
                setBannerMode('hidden', null);
                state.active = null;
            }
        } catch { }
    }
    window.fetchAndRenderUploadGate = fetchAndRenderUploadGate; // expose for reuse

    function installBeforeUnload() {
        window.addEventListener('beforeunload', function () {
            var hash = (state && state.hash) || (state.active && state.active.fileHash) || '';
            if (hash) sendStopped(hash);
        });
        document.addEventListener('visibilitychange', function () {
            // When user returns, refresh immediately to sync UI
            if (!document.hidden) {
                fetchAndRenderUploadGate();
            }
        });
    }

    function setUploading(flag) {
        state.isUploading = !!flag;
        disableBtn(flag || (state.freeBytes !== null && state.freeBytes <= 0));
        if (flag) { startHeartbeat(); } else { stopHeartbeat(); }
        // Do not change banner mode here; allow fetch to drive it to avoid flicker
        showBanner(state.active, flag || !!state.active);
    }

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

    async function restartOnServer(fileName, hash) {
        var fd = new FormData();
        fd.append('fileName', fileName);
        fd.append('fileHash', hash);
        var res = await fetch(cfg.endpoints.restart, { method: 'POST', body: fd });
        if (!res.ok) throw new Error('restart failed');
        return res.json();
    }

    async function uploadChunk(file, hash, index, total) {
        var start = index * cfg.chunkSize;
        var end = Math.min(start + cfg.chunkSize, file.size);
        var blob = file.slice(start, end);
        var fd = new FormData();
        fd.append('chunk', blob, 'chunk_' + index);
        fd.append('hash', hash);
        fd.append('chunkIndex', index.toString());
        fd.append('totalChunks', total.toString());
        fd.append('fileSize', file.size.toString());
        fd.append('fileName', file.name);
        if (state.forceVersion != null) {
            fd.append('targetVersion', String(state.forceVersion));
        }
        var res = await fetch(cfg.endpoints.chunk, { method: 'POST', body: fd });
        var data = null;
        try { data = await res.json(); } catch { }
        if (!res.ok) {
            var msg = (data && data.message) ? data.message : '';
            if (!msg) { try { msg = await res.text(); } catch { } }

            if (res.status === 507 || /Недостаточно места/i.test(msg)) {
                alert('Недостаточно места на диске. Освободите место и попробуйте снова.');
                throw new Error('insufficient_storage');
            }
            if (res.status === 503 || /(antivirus|clamav|service unavailable|timeout|av_unavailable|av_timeout)/i.test(msg || '')) {
                alert('Антивирус временно недоступен. Повторите попытку позже.');
                throw new Error('av_unavailable');
            }
            if (res.status === 409 && /infected/i.test(msg)) {
                alert('Файл отклонён антивирусной проверкой. Он не был сохранён.');
                throw new Error('infected');
            }
            if (res.status === 409 && (msg || '').length) {
                alert(msg);
                throw new Error(msg);
            }
            throw new Error(msg || 'upload failed');
        }
        return data || {};
    }

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
                var next = (typeof r.nextExpectedIndex === 'number') ? r.nextExpectedIndex : (state.index + 1);
                state.index = next;

                var prog = document.getElementById('uploadProgress');
                if (prog) {
                    var p = Math.min(100, Math.floor((state.index / state.total) * 100));
                    prog.textContent = p + '%';
                }
                if (state.index % 4 === 0) await fetchAndRenderUploadGate();
            }
            if (!state.cancelRequested) {
                if (typeof window.initStorageTable === 'function') window.initStorageTable();
                else if (typeof window.fetchFiles === 'function') window.fetchFiles();
            }
        } finally {
            setUploading(false);
            state.active = null;
            state.forceVersion = null;
            await fetchAndRenderUploadGate();
            updateStorageCounter();
        }
    }

    function bindHandlers() {
        $(document).off('click.uploadFile').on('click.uploadFile', cfg.buttonSelector, async function () {
            await fetchAndRenderUploadGate();
            await updateStorageCounter();
            if ((state.freeBytes !== null && state.freeBytes <= 0) ||
                (state.active && preferFresh(state.active))) return;
            var input = document.querySelector(cfg.inputSelector);
            if (input) { input.value = null; input.click(); }
        });

        $(document).off('change.uploadFile').on('change.uploadFile', cfg.inputSelector, async function (e) {
            var file = e.target.files && e.target.files[0];
            if (!file) return;

            await fetchAndRenderUploadGate();
            await updateStorageCounter();
            if ((state.freeBytes !== null && file.size > state.freeBytes)) {
                alert('Невозможно начать загрузку: недостаточно места.\nСвободно: ' + fmtSize(state.freeBytes) + ', файл: ' + fmtSize(file.size));
                return;
            }
            if (state.active && preferFresh(state.active)) return;

            // Проверяем конфликт «имя уже есть»
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
                        onCancel: function () { disableBtn(false); }
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
        updateStorageCounter();
        fetchAndRenderUploadGate();
        startStatsPolling();
        if (!pollTimer) {
            pollTimer = setInterval(fetchAndRenderUploadGate, ACTIVE_POLL_MS);
            keepTimer('pollTimer', pollTimer);
        }
    })();
})(jQuery);
