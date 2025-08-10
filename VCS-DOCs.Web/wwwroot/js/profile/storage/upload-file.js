(function ($) {
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
        inited: false,
        isUploading: false,
        cancelRequested: false,
        active: null,
        file: null,
        hash: null,
        index: 0,
        total: 0,
        skip: null,
        freeBytes: null
    };

    var UI_STALE_SECONDS = 6;
    var hbTimer = null;
    var statsTimer = null;

    function disableBtn(disabled) {
        var btn = document.querySelector(cfg.buttonSelector);
        if (btn) btn.disabled = !!disabled;
    }

    function fmtSize(bytes) {
        return (bytes / 1024 / 1024).toFixed(2) + ' МБ';
    }

    async function updateStorageCounter() {
        try {
            // 1) Пытаемся получить чистые статы
            var res = await fetch(cfg.endpoints.stats, { cache: 'no-store' });
            var data = null;
            if (res.ok) {
                data = await res.json();
            } else if (res.status === 404) {
                // 2) Fallback: берём статы из user-files
                var uf = await fetch(cfg.endpoints.userFiles, { cache: 'no-store' });
                if (!uf.ok) return;
                data = await uf.json();
            } else {
                return; // молча выходим (сеть/ошибка)
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
            banner.style.width = 'auto';
            banner.style.boxSizing = 'border-box';

            // компактная вёрстка
            banner.style.display = 'none';
            banner.style.alignItems = 'center';
            banner.style.gap = '12px';
            banner.style.whiteSpace = 'normal';
            banner.style.wordBreak = 'break-word';

            // контент
            banner.style.display = 'none';
            banner.style.padding = '10px 14px';
            banner.style.display = 'none';
            banner.style.display = 'none'; //просто гарантируем начальное скрытие

            // сделаем контейнер flex
            banner.style.display = 'none';
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
            if (confirm('Вы уверены? Было загружено ' + percent + '%\\n' + fileName + '\\nОтмена загрузки очистит прогресс.')) {
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
        m.close.onclick = function () {
            m.modal.style.display = 'none';
        };
    }

    function computePercent(active) {
        if (active && typeof active.uploadedBytes === 'number' && active.fileSize > 0) {
            return Math.max(0, Math.min(99, Math.floor((active.uploadedBytes / active.fileSize) * 100)));
        }
        if (state.isUploading && state.total > 0) {
            return Math.min(99, Math.floor((state.index / state.total) * 100));
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
    }
    function stopHeartbeat() {
        if (hbTimer) { clearInterval(hbTimer); hbTimer = null; }
    }

    function startStatsPolling() {
        if (statsTimer) return;
        statsTimer = setInterval(updateStorageCounter, 5000);
    }
    function stopStatsPolling() {
        if (statsTimer) { clearInterval(statsTimer); statsTimer = null; }
    }

    function renderBanner(active, on) {
        var b = ensureBanner();
        var span = b.querySelector('.upload-busy-message');
        var btnCancel = document.getElementById('upload-cancel-btn');
        var btnContinue = document.getElementById('upload-continue-btn');
        var percent = computePercent(active);
        var name = (active && active.fileName) || (state.file && state.file.name) || 'файл';

        var uiFresh = !!(active && typeof active.ageSec === 'number' && active.ageSec <= UI_STALE_SECONDS);
        var isStopped = !!(active && active.stopped === true);

        if (active && uiFresh && !isStopped) {
            span.textContent = 'Идёт загрузка: ' + name + ' — ' + percent + '% . Если закроете страницу или обновите — загрузка прервётся.';
            btnContinue.style.display = 'none';
            btnCancel.style.display = 'inline-block';
        } else if (active) {
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
            if (input) {
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
                    var hash = state.hash || (cur && cur.fileHash) || '';
                    sendStopped(hash);
                    var fd = new FormData();
                    fd.append('fileName', fname);
                    fd.append('fileHash', hash);
                    await fetch(cfg.endpoints.restart, { method: 'POST', body: fd });
                    state.active = null;
                    await fetchAndRenderGate();
                    updateStorageCounter();
                } catch { }
            });
        };
    }

    function showBanner(active, on) {
        renderBanner(active, on);
    }

    async function fetchAndRenderGate() {
        try {
            var r = await fetch(cfg.endpoints.active, { cache: 'no-store' });
            if (!r.ok) return;
            var active = await r.json();
            if (active && active.found) {
                state.active = active;
                disableBtn(active.ageSec <= UI_STALE_SECONDS && !active.stopped);
                showBanner(active, true);
            } else {
                showBanner(null, false);
                state.active = null;
            }
        } catch { }
    }

    function installBeforeUnload() {
        window.addEventListener('beforeunload', function () {
            var hash = (state && state.hash) || (state.active && state.active.fileHash) || '';
            if (hash) sendStopped(hash);
        });
    }

    function setUploading(flag) {
        state.isUploading = !!flag;
        disableBtn(flag || (state.freeBytes !== null && state.freeBytes <= 0));
        if (flag) { startHeartbeat(); } else { stopHeartbeat(); }
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
        const start = index * cfg.chunkSize;
        const end = Math.min(start + cfg.chunkSize, file.size);
        const blob = file.slice(start, end);

        const fd = new FormData();
        fd.append('chunk', blob, 'chunk_' + index);
        fd.append('hash', hash);
        fd.append('chunkIndex', index.toString());
        fd.append('totalChunks', total.toString());
        fd.append('fileSize', file.size.toString());
        fd.append('fileName', file.name);

        const res = await fetch(cfg.endpoints.chunk, { method: 'POST', body: fd });

        let data = null;
        try { data = await res.json(); } catch { /* ignore */ }

        if (!res.ok) {
            let msg = (data && data.message) ? data.message : '';
            if (!msg) { try { msg = await res.text(); } catch { } }

            // 507 — недостаточно места
            if (res.status === 507 || /Недостаточно места/i.test(msg)) {
                alert('Недостаточно места на диске. Освободите место и попробуйте снова.');
                return { aborted: true, reason: 'insufficient_storage' };
            }

            // 503 — антивирус недоступен/таймаут
            if (res.status === 503 || /(antivirus|service unavailable|timeout|av_unavailable|av_timeout)/i.test(msg || '')) {
                alert('Антивирус временно недоступен. Повторите попытку позже.');
                return { aborted: true, reason: 'av_unavailable' };
            }

            // 409 — заражён
            if (res.status === 409 && /infected/i.test(msg)) {
                alert('Файл отклонён антивирусной проверкой. Он не был сохранён.');
                return { aborted: true, reason: 'infected' };
            }

            // 409 — другие конфликты (busy/hash mismatch и т.п.)
            if (res.status === 409 && (msg || '').length) {
                alert(msg);
                return { aborted: true, reason: 'conflict', message: msg };
            }

            // неизвестная ошибка — оставим как исключение
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
                    const progEl = document.getElementById('uploadProgress');
                    if (progEl) {
                        const percent = Math.min(100, Math.floor(((state.index + 1) / state.total) * 100));
                        progEl.textContent = percent + '%';
                    }
                    state.index++;
                    showBanner(state.active, true);
                    updateStorageCounter();
                    continue;
                }

                const r = await uploadChunk(file, state.hash, state.index, state.total);

                // НОВОЕ: мягкая остановка без исключений
                if (r && r.aborted) {
                    break;
                }

                const next = (typeof r.nextExpectedIndex === 'number') ? r.nextExpectedIndex : (state.index + 1);
                state.index = next;

                const prog = document.getElementById('uploadProgress');
                if (prog) {
                    const p = Math.min(100, Math.floor((state.index / state.total) * 100));
                    prog.textContent = p + '%';
                }
                showBanner(state.active, true);
                updateStorageCounter();
            }

            if (!state.cancelRequested) {
                if (typeof window.initStorageTable === 'function') window.initStorageTable();
                else if (typeof window.fetchFiles === 'function') window.fetchFiles();
            }
        } finally {
            setUploading(false);
            state.active = null;
            await fetchAndRenderGate();
            updateStorageCounter();
        }
    }

    function bindHandlers() {
        $(document).off('click.uploadFile').on('click.uploadFile', cfg.buttonSelector, async function () {
            await fetchAndRenderGate();
            await updateStorageCounter();
            if ((state.freeBytes !== null && state.freeBytes <= 0) ||
                (state.active && state.active.ageSec <= UI_STALE_SECONDS && !state.active.stopped)) return;
            var input = document.querySelector(cfg.inputSelector);
            if (input) {
                input.value = null;
                input.click();
            }
        });

        $(document).off('change.uploadFile').on('change.uploadFile', cfg.inputSelector, async function (e) {
            var file = e.target.files && e.target.files[0];
            if (!file) return;

            await fetchAndRenderGate();
            await updateStorageCounter();
            if ((state.freeBytes !== null && file.size > state.freeBytes)) {
                alert('Невозможно начать загрузку: недостаточно места.\\nСвободно: ' + fmtSize(state.freeBytes) + ', файл: ' + fmtSize(file.size));
                return;
            }
            if (state.active && state.active.ageSec <= UI_STALE_SECONDS && !state.active.stopped) return;

            if (state.active && (state.active.ageSec > UI_STALE_SECONDS || state.active.stopped === true)) {
                var fp = await quickFingerprint(file);
                if (fp.toLowerCase() === String(state.active.fileHash || '').toLowerCase() &&
                    String(file.name || '') === String(state.active.fileName || '')) {
                    var set = new Set(Array.isArray(state.active.uploaded) ? state.active.uploaded : []);
                    state.hash = fp;
                    await startUpload(file, set);
                    return;
                } else {
                    var p = Math.max(0, Math.min(99, Math.floor(((state.active.uploaded?.length || 0) * cfg.chunkSize) / state.active.fileSize * 100)));
                    showCancelConfirm(p, state.active.fileName || file.name, async function () {
                        try {
                            sendStopped(state.active.fileHash || '');
                            await restartOnServer(state.active.fileName || file.name, state.active.fileHash || '');
                            state.hash = fp;
                            await startUpload(file, null);
                        } catch { }
                    });
                    return;
                }
            }

            var fd = new FormData();
            fd.append('fileName', file.name);
            var res = await fetch(cfg.endpoints.check, { method: 'POST', body: fd });
            var conflict = res.ok ? (await res.json()).conflict : false;
            if (conflict && typeof window.showConflictModal === 'function') {
                window.showConflictModal(
                    file.name,
                    'conflict',
                    {
                        onReplace: async function (version) {
                            try {
                                var uf = await fetch(cfg.endpoints.userFiles);
                                var data = await uf.json();
                                var files = Array.isArray(data.files) ? data.files : [];
                                var entry = files.find(function (f) { return f.fileName === file.name || f.FileName === file.name; });
                                if (!entry) { alert('Файл не найден'); disableBtn(false); return; }
                                var gid = entry.fileGroupId || entry.FileGroupId;
                                var del = await fetch(cfg.endpoints.delete + gid + '/' + version, { method: 'DELETE' });
                                if (!del.ok) { alert('Не удалось удалить выбранную версию'); disableBtn(false); return; }
                            } catch { alert('Не удалось удалить выбранную версию'); disableBtn(false); return; }
                            state.hash = await quickFingerprint(file);
                            await startUpload(file, null);
                        },
                        onNewVersion: async function () { state.hash = await quickFingerprint(file); await startUpload(file, null); },
                        onCancel: function () { disableBtn(false); }
                    }
                );
                return;
            }

            state.hash = await quickFingerprint(file);
            await startUpload(file, null);
        });
    }

    window.initUploadFile = function () {
        if (state.inited) return;
        state.inited = true;
        bindHandlers();
        installBeforeUnload();
        fetchAndRenderGate();
        updateStorageCounter();
        startStatsPolling();
        setInterval(fetchAndRenderGate, 1000);
    };
})(jQuery);
