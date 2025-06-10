(function ($) {
    let uploadFileInitialized = false;
    let isUploadInProgress = false;
    let isCanceled = false;

    window.initUploadFile = function () {
        if (uploadFileInitialized) return;
        uploadFileInitialized = true;
        console.log("initUploadFile: скрипт загружен");

        $(document).on('click', '#uploadFileButton', () => {
            const fileInput = document.getElementById('hiddenFileInput');
            if (fileInput) {
                fileInput.value = null;
                fileInput.click();
            }
        });

        $(document).on('change', '#hiddenFileInput', async (event) => {
            isCanceled = false;
            const uploadBtn = document.getElementById('uploadFileButton');
            if (uploadBtn) uploadBtn.disabled = true;

            const file = event.target.files[0];
            if (!file) return console.warn("Файл не выбран");

            await updateStorageCounter();

            const list = await (await fetch('/api/Upload/list')).json();
            const freeBytes = list.limitBytes - list.usedBytes - list.tempBytes;
            if (file.size > freeBytes) {
                alert(`Невозможно загрузить "${file.name}", мало места (${formatSize(freeBytes)})`);
                if (uploadBtn) uploadBtn.disabled = false;
                return;
            }

            let hash;
            try {
                if (file.size <= 100 * 1024 * 1024)
                    hash = await computeSHA256(file);
                else
                    hash = await computeSparkMD5Hash(file);

                const hashTaskKey = `hash_${hash}`;
                const hashTask = {
                    taskKey: hashTaskKey,
                    title: `Подготовка: ${file.name}`,
                    type: "upload",
                    statusClass: "starting",
                    statusText: "Вычисление хеша...",
                    cancelable: true,
                    autoRemove: false
                };
                window.taskManager.addTask(hashTask);
            } catch (err) {
                if (err === "Отменено") {
                    console.log(`Пользователь отменил хеширование файла "${file.name}"`);
                    if (uploadBtn) uploadBtn.disabled = false;
                    return;
                }

                const failTask = {
                    taskKey: `hash_failed_${Date.now()}`,
                    title: `Подготовка: ${file.name}`,
                    type: "upload",
                    statusClass: "failed",
                    statusText: "Ошибка при хешировании",
                    cancelable: true,
                    autoRemove: true,
                    autoRemoveDelay: 5000
                };
                window.taskManager.addTask(failTask);
                alert("Не удалось вычислить хеш");
                if (uploadBtn) uploadBtn.disabled = false;
                return;
            }

            try {
                const status = await (await fetch(`/api/Upload/upload-status?fileHash=${hash}`)).json();
                if (status.found) {
                    showResumeModal(file.name,
                        () => startUpload(file, hash, null, status.sessionId, new Set(status.uploaded)),
                        () => checkConflictThenUpload(file, hash)
                    );
                    return;
                }
            } catch (err) {
                console.warn("Проверка старой загрузки провалилась", err);
            }

            checkConflictThenUpload(file, hash);
        });
    };

    async function checkConflictThenUpload(file, hash) {
        let conflict;
        try {
            const conflictRes = await fetch('/api/Upload/conflict-check', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ fileName: file.name, hash })
            });
            conflict = conflictRes.ok ? await conflictRes.json() : null;
        } catch (error) {
            console.error('Ошибка запроса конфликта:', error);
            alert('Не удалось проверить конфликт.');
            return;
        }

        if (!conflict || conflict.status === "ok") {
            startUpload(file, hash, null, null, new Set());
        } else if (["exists", "uploading"].includes(conflict.status)) {
            showConflictModal(file.name, conflict.status, {
                onReplace: sel => startUpload(file, hash, sel, null, new Set()),
                onNewVersion: () => startUpload(file, hash, null, null, new Set()),
                onCancel: () => console.log("Пользователь отменил.")
            });
        } else {
            console.error('Неожиданный ответ:', conflict);
        }
    }

    async function startUpload(file, hash, replaceVersion, sessionId, alreadyUploaded) {
        const uploadBtn = document.getElementById('uploadFileButton');
        const chunkSize = 1 * 1024 * 1024;
        const totalChunks = Math.ceil(file.size / chunkSize);

        isUploadInProgress = true;

        for (let i = 0; i < totalChunks; i++) {
            if (isCanceled) {
                await cleanupTempUpload(hash);
                isUploadInProgress = false;
                if (uploadBtn) uploadBtn.disabled = false;
                return;
            }
            if (alreadyUploaded.has(i)) continue;
            const chunk = file.slice(i * chunkSize, (i + 1) * chunkSize);

            const form = new FormData();
            form.append("chunk", chunk);
            form.append("hash", hash);
            form.append("chunkIndex", i);
            form.append("totalChunks", totalChunks);
            form.append("fileSize", file.size);
            form.append("fileName", file.name);
            if (replaceVersion != null) form.append("replaceVersion", replaceVersion);
            if (sessionId != null) form.append("sessionId", sessionId);

            if (uploadBtn) uploadBtn.disabled = true;

            try {
                const res = await fetch('/api/Upload/chunk', { method: 'POST', body: form });
                if (!res.ok) {
                    let errBody = {};
                    try {
                        const contentType = res.headers.get("content-type") || "";
                        if (contentType.includes("application/json")) {
                            errBody = await res.json();
                        } else {
                            const text = await res.text();
                            errBody.message = text;
                        }
                    } catch (parseError) {
                        errBody.message = "Не удалось прочитать сообщение об ошибке";
                    }

                    if (res.status === 409 && errBody.status === "busy") {
                        alert(errBody.message || "Идёт другая загрузка");
                        isUploadInProgress = false;
                        if (uploadBtn) uploadBtn.disabled = false;
                        return;
                    }

                    throw new Error(errBody.message || `Ошибка HTTP ${res.status}`);
                }

                await updateStorageCounter();
            } catch (err) {
                showUploadErrorModal(file.name, i, err.message);
                isUploadInProgress = false;
                if (uploadBtn) uploadBtn.disabled = false;
                return;
            }
        }

        try {
            const form = new FormData();
            form.append("hash", hash);
            const res = await fetch('/api/Upload/complete', {
                method: 'POST',
                body: form,
                headers: { 'X-CSRF-TOKEN': document.querySelector('input[name="__RequestVerificationToken"]')?.value }
            });
            if (!res.ok) throw new Error(await res.text());

            if (typeof initStorageTable === 'function') initStorageTable();
            else if (typeof fetchFiles === 'function') fetchFiles();
        } catch (err) {
            showUploadErrorModal(file.name, -1, err.message);
        } finally {
            isUploadInProgress = false;
            if (uploadBtn) uploadBtn.disabled = false;
            await updateStorageCounter();
        }
    }

    function computeSparkMD5Hash(file, chunkSize = 10 * 1024 * 1024) {
        return new Promise((resolve, reject) => {
            const reader = new FileReader();
            const spark = new SparkMD5.ArrayBuffer();
            const chunks = Math.ceil(file.size / chunkSize);
            let idx = 0;

            const taskKey = `hash_progress_${Date.now()}`;
            const hashTask = {
                taskKey,
                title: `Подготовка: ${file.name}`,
                type: "upload",
                statusClass: "starting",
                statusText: "Хеширование: 0%",
                cancelable: true,
                autoRemove: true,
                autoRemoveDelay: 2000,
                onCancel: () => { isCanceled = true; }
            };

            // Добавляем задачу один раз
            window.taskManager.addTask(hashTask);

            reader.onload = (e) => {
                if (isCanceled) {
                    window.taskManager.removeTask(hashTask);
                    return reject("Отменено");
                }

                spark.append(e.target.result);
                idx++;

                const percent = Math.floor((idx / chunks) * 100);
                hashTask.statusText = `Хеширование: ${percent}%`;

                // Просто перерендерим один раз, не добавляя заново
                window.taskManager.render();

                if (idx < chunks) {
                    reader.readAsArrayBuffer(file.slice(idx * chunkSize, (idx + 1) * chunkSize));
                } else {
                    window.taskManager.removeTask(hashTask);
                    resolve(spark.end());
                }
            };

            reader.onerror = () => {
                window.taskManager.removeTask(hashTask);
                reject("Ошибка чтения");
            };

            reader.readAsArrayBuffer(file.slice(0, chunkSize));
        });
    }

    async function computeSHA256(file) {
        if (isCanceled) throw new Error("Отменено");

        const buf = await file.arrayBuffer();
        if (isCanceled) throw new Error("Отменено");

        const hashTask = {
            taskKey: `hash_progress_${Date.now()}`,
            title: `Подготовка: ${file.name}`,
            type: "upload",
            statusClass: "starting",
            statusText: "Хеширование SHA-256...",
            cancelable: true,
            autoRemove: true,
            autoRemoveDelay: 2000,
            onCancel: () => { isCanceled = true; }
        };
        window.taskManager.addTask(hashTask);

        const hash = await crypto.subtle.digest("SHA-256", buf);
        if (isCanceled) throw new Error("Отменено");

        window.taskManager.removeTask(hashTask);
        return Array.from(new Uint8Array(hash)).map(b => b.toString(16).padStart(2, '0')).join('');
    }
    function showResumeModal(fileName, onContinue, onRestart) {
        const modal = document.getElementById("upload-resume-modal");
        const message = modal.querySelector("#resume-modal-message");
        const confirmBtn = modal.querySelector("#resume-confirm");
        const cancelBtn = modal.querySelector("#resume-cancel");

        if (!modal || !message || !confirmBtn || !cancelBtn) {
            const answer = confirm("Файл уже загружался. Продолжить загрузку?");
            answer ? onContinue?.() : onRestart?.();
            return;
        }

        message.textContent = `Файл "${fileName}" уже загружался. Хотите продолжить с того места?`;

        confirmBtn.onclick = () => {
            modal.style.display = "none";
            onContinue?.();
        };

        cancelBtn.onclick = () => {
            modal.style.display = "none";
            onRestart?.();
        };

        modal.style.display = "block";
    }

    function showUploadErrorModal(fileName, chunkIndex, error) {
        const modal = document.getElementById("upload-error-modal");
        const title = document.getElementById("upload-error-title");
        const message = document.getElementById("upload-error-message");

        if (!modal || !title || !message) {
            alert(`Ошибка загрузки файла ${fileName} на чанке ${chunkIndex + 1}. ${error}.\nЗагрузка прервана!`);
            return;
        }

        title.textContent = "Ошибка загрузки";
        message.textContent = `Файл: ${fileName}\nЧанк: ${chunkIndex + 1}\nОписание: ${error}`;
        modal.style.display = "block";
    }

    window.addEventListener("beforeunload", function (e) {
        if (isUploadInProgress) {
            const message = "Файл всё ещё загружается. Уход со страницы прервёт загрузку.";
            e.preventDefault();
            e.returnValue = message;
            return message;
        }
    });

    async function updateStorageCounter() {
        const counter = document.getElementById('storageCounter');
        if (!counter) return;
        try {
            const res = await fetch('/api/Upload/list');
            if (!res.ok) throw new Error(`HTTP ${res.status}`);
            const data = await res.json();
            const used = formatSize(data.usedBytes);
            const temp = formatSize(data.tempBytes);
            const limit = formatSize(data.limitBytes);
            const free = formatSize(data.limitBytes - data.usedBytes - data.tempBytes);
            counter.textContent =
                `Использовано: ${used} из ${limit} (временных: ${temp}); свободно: ${free}`;
        } catch (e) {
            console.error("Не удалось обновить счётчик хранилища", e);
        }
    }

    function formatSize(bytes) {
        return (bytes / 1024 / 1024).toFixed(2) + ' МБ';
    }

    async function cleanupTempUpload(hash) {
        try {
            await fetch('/api/Upload/cleanup-temp', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ hash })
            });
            console.log("Очистил temp-чАнки");
        } catch (err) {
            console.warn("Не смог очистить temp:", err);
        }
    }
})(jQuery);
