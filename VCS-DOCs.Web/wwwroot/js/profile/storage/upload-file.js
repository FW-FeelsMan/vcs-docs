(function ($) {
    var cfg = {
        buttonSelector: '#uploadFileButton',
        inputSelector: '#hiddenFileInput',
        chunkSize: 2 * 1024 * 1024,
        endpoints: {
            active: '/api/Upload/active',
            status: '/api/Upload/upload-status',
            check: '/api/Upload/check-version-conflict',
            restart: '/api/Upload/restart',
            chunk: '/api/Upload/chunk',
            userFiles: '/api/Upload/user-files',
            delete: '/api/Upload/delete/'
        }
    };

    var LS_KEY = 'vcs-upload-busy';

    var state = {
        inited: false,
        isUploading: false,
        activeInfo: null,
        file: null,
        hash: null,
        index: 0,
        total: 0,
        skip: null
    };

    function disableBtn(disabled) {
        var btn = document.querySelector(cfg.buttonSelector);
        if (btn) btn.disabled = !!disabled;
    }

    function showResumeModal(fileName, onContinue, onRestart) {
        var modal = document.getElementById("upload-resume-modal");
        var message = modal && modal.querySelector("#resume-modal-message");
        var confirmBtn = modal && modal.querySelector("#resume-confirm");
        var cancelBtn = modal && modal.querySelector("#resume-cancel");
        if (!modal || !message || !confirmBtn || !cancelBtn) {
            var answer = confirm('Файл "' + fileName + '" уже загружался. Продолжить? Отмена — начать заново.');
            if (answer) onContinue && onContinue(); else onRestart && onRestart();
            return;
        }
        message.textContent = 'Файл "' + fileName + '" уже загружался. Хотите продолжить с того места?';
        confirmBtn.onclick = function () { modal.style.display = "none"; onContinue && onContinue(); };
        cancelBtn.onclick = function () { modal.style.display = "none"; onRestart && onRestart(); };
        modal.style.display = "block";
    }

    function ensureBanner() {
        var banner = document.getElementById('upload-busy-banner');
        if (!banner) {
            banner = document.createElement('div');
            banner.id = 'upload-busy-banner';
            banner.style.position = 'fixed';
            banner.style.left = '0';
            banner.style.right = '0';
            banner.style.bottom = '0';
            banner.style.padding = '10px 14px';
            banner.style.background = '#b91c1c';
            banner.style.color = '#fff';
            banner.style.fontSize = '14px';
            banner.style.textAlign = 'center';
            banner.style.zIndex = '9999';
            banner.style.display = 'none';
            document.body.appendChild(banner);
        }
        return banner;
    }

    function toggleWarning(flag, msg) {
        var holder = document.getElementById('uploadWarning');
        var text = msg || 'Идёт загрузка. Если закроете страницу или обновите — загрузка прервётся.';
        if (holder) {
            holder.textContent = flag ? text : '';
            holder.style.display = flag ? '' : 'none';
        } else {
            var banner = ensureBanner();
            banner.textContent = text;
            banner.style.display = flag ? 'block' : 'none';
        }
    }

    function markBusyLS(on, meta) {
        try {
            if (on) localStorage.setItem(LS_KEY, JSON.stringify(meta || { t: Date.now() }));
            else localStorage.removeItem(LS_KEY);
        } catch { }
    }

    function setUploading(flag, meta) {
        state.isUploading = !!flag;
        disableBtn(flag);
        toggleWarning(flag);
        markBusyLS(flag, meta);
    }

    window.addEventListener('beforeunload', function (e) {
        if (state.isUploading) {
            var msg = 'Идёт загрузка файла. Закроете или обновите страницу — загрузка прервётся.';
            e.preventDefault();
            e.returnValue = msg;
            return msg;
        }
    });

    async function updateStorageCounter() {
        var el = document.getElementById('storageCounter');
        if (!el) return;
        try {
            var res = await fetch(cfg.endpoints.userFiles);
            if (!res.ok) return;
            var data = await res.json();
            var used = (data.usedBytes / 1024 / 1024).toFixed(2) + ' МБ';
            var temp = (data.tempBytes / 1024 / 1024).toFixed(2) + ' МБ';
            var limit = (data.limitBytes / 1024 / 1024).toFixed(2) + ' МБ';
            var free = ((data.limitBytes - data.usedBytes - data.tempBytes) / 1024 / 1024).toFixed(2) + ' МБ';
            el.textContent = 'Использовано: ' + used + ' из ' + limit + ' (временных: ' + temp + '); свободно: ' + free;
        } catch { }
    }

    function computeSparkMD5Hash(file, chunkSize) {
        if (!window.SparkMD5 || !SparkMD5.ArrayBuffer) return Promise.reject(new Error('SparkMD5 не подключён'));
        chunkSize = chunkSize || 10 * 1024 * 1024;
        var chunks = Math.ceil(file.size / chunkSize);
        var currentChunk = 0;
        var spark = new SparkMD5.ArrayBuffer();
        return new Promise(function (resolve, reject) {
            var reader = new FileReader();
            reader.onload = function (e) {
                spark.append(e.target.result);
                currentChunk++;
                if (currentChunk < chunks) loadNext(); else resolve(spark.end());
            };
            reader.onerror = function () { reject(new Error('Ошибка чтения')); };
            function loadNext() {
                var start = currentChunk * chunkSize;
                var end = Math.min(start + chunkSize, file.size);
                reader.readAsArrayBuffer(file.slice(start, end));
            }
            loadNext();
        });
    }

    async function ensureNoVersionConflict(file) {
        var fd = new FormData();
        fd.append('fileName', file.name);
        var res = await fetch(cfg.endpoints.check, { method: 'POST', body: fd });
        if (!res.ok) throw new Error('check failed');
        var data = await res.json();
        return !!data.conflict;
    }

    async function restartOnServerByHash(hash, fileName) {
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
        var res = await fetch(cfg.endpoints.chunk, { method: 'POST', body: fd });
        var data = {};
        try { data = await res.json(); } catch { }
        if (res.status === 409 && data && (data.status === 'busy' || data.message)) {
            toggleWarning(true, data.message || 'Идёт другая загрузка.');
            disableBtn(true);
            throw new Error('busy');
        }
        if (!res.ok) throw new Error((data && data.message) ? data.message : 'upload failed');
        return data;
    }

    async function startUpload(file, skipSet) {
        state.index = 0;
        state.total = Math.ceil(file.size / cfg.chunkSize);
        state.skip = skipSet || null;
        setUploading(true, { t: Date.now(), name: file.name });
        try {
            while (state.index < state.total) {
                if (state.skip && state.skip.has(state.index)) {
                    var progEl = document.getElementById('uploadProgress');
                    if (progEl) {
                        var percent = Math.min(100, Math.floor(((state.index + 1) / state.total) * 100));
                        progEl.textContent = percent + '%';
                    }
                    state.index++;
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
                await updateStorageCounter();
            }
            if (typeof window.initStorageTable === 'function') window.initStorageTable();
            else if (typeof window.fetchFiles === 'function') window.fetchFiles();
            await updateStorageCounter();
        } finally {
            setUploading(false);
            state.activeInfo = null;
        }
    }

    async function checkActiveOnServer() {
        try {
            var r = await fetch(cfg.endpoints.active);
            if (!r.ok) return null;
            var data = await r.json();
            if (!data.found) return null;
            return data;
        } catch { return null; }
    }

    function bindHandlers() {
        $(document).off('click.uploadFile').on('click.uploadFile', cfg.buttonSelector, async function () {
            var active = await checkActiveOnServer();
            if (active) {
                state.activeInfo = active;
                disableBtn(true);
                showResumeModal(active.fileName, async function () {
                    var input = document.querySelector(cfg.inputSelector);
                    if (!input) return;
                    input.onchange = async function (e) {
                        var f = e.target.files && e.target.files[0];
                        if (!f) { disableBtn(false); return; }
                        var md5 = await computeSparkMD5Hash(f);
                        if (md5.toLowerCase() !== String(active.fileHash || '').toLowerCase()) {
                            alert('Выбран другой файл. Выберите тот же файл: ' + active.fileName);
                            disableBtn(false);
                            return;
                        }
                        state.file = f;
                        state.hash = md5;
                        var set = new Set(Array.isArray(active.uploaded) ? active.uploaded : []);
                        await startUpload(f, set);
                    };
                    input.value = null;
                    input.click();
                }, async function () {
                    await restartOnServerByHash(active.fileHash, active.fileName);
                    disableBtn(false);
                });
                return;
            }
            var input2 = document.querySelector(cfg.inputSelector);
            if (input2) {
                input2.value = null;
                input2.click();
            }
        });

        $(document).off('change.uploadFile').on('change.uploadFile', cfg.inputSelector, async function (e) {
            var file = e.target.files && e.target.files[0];
            if (!file) return;
            var active = await checkActiveOnServer();
            if (active) {
                state.activeInfo = active;
                disableBtn(true);
                showResumeModal(active.fileName, async function () {
                    var md5 = await computeSparkMD5Hash(file);
                    if (md5.toLowerCase() !== String(active.fileHash || '').toLowerCase()) {
                        alert('Выбран другой файл. Выберите: ' + active.fileName);
                        disableBtn(false);
                        return;
                    }
                    state.file = file;
                    state.hash = md5;
                    var set = new Set(Array.isArray(active.uploaded) ? active.uploaded : []);
                    await startUpload(file, set);
                }, async function () {
                    await restartOnServerByHash(active.fileHash, active.fileName);
                    disableBtn(false);
                });
                return;
            }
            disableBtn(true);
            try {
                state.hash = await computeSparkMD5Hash(file);
            } catch (err) {
                alert('Не удалось вычислить MD5: ' + err.message + '. Подключи spark-md5.min.js');
                disableBtn(false);
                return;
            }
            var st = await fetch(cfg.endpoints.status + '?fileHash=' + encodeURIComponent(state.hash));
            var sdata = st.ok ? await st.json() : { found: false };
            if (sdata.found && Array.isArray(sdata.uploaded)) {
                var set2 = new Set(sdata.uploaded);
                showResumeModal(file.name,
                    async function () { await startUpload(file, set2); },
                    async function () { await restartOnServerByHash(state.hash, file.name); await startUpload(file, null); }
                );
                return;
            }
            var hasConflict = await ensureNoVersionConflict(file);
            if (hasConflict && typeof window.showConflictModal === 'function') {
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
                            await startUpload(file, null);
                        },
                        onNewVersion: async function () { await startUpload(file, null); },
                        onCancel: function () { disableBtn(false); }
                    }
                );
                return;
            }
            await startUpload(file, null);
        });
    }

    window.initUploadFile = function () {
        if (state.inited) return;
        state.inited = true;
        bindHandlers();
        (async function initGate() {
            var active = await checkActiveOnServer();
            if (active) {
                state.activeInfo = active;
                disableBtn(true);
                toggleWarning(true, 'Обнаружена незавершённая загрузка: ' + active.fileName + '. Нажмите «Загрузить файл» для продолжения или очистите загрузку.');
            }
        })();
    };
})(jQuery);