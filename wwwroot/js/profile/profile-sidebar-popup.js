function ensurePopupExists() {
    if (document.querySelector('.sidebar-upload-popup')) return;

    const popup = document.createElement('div');
    popup.className = 'sidebar-upload-popup';
    popup.innerHTML = `
        <div class="sidebar-upload-header">
            <div class="sidebar-upload-title">Загрузка файлов</div>
        </div>
        <div class="sidebar-upload-content"></div>
    `;
    popup.style.display = 'none';
    document.body.appendChild(popup);
}

function addFileToPopup(fileName, fileSize, customFileId = null) {
    const fileId = customFileId || `upload-${fileName.toLowerCase().replace(/\W+/g, '-')}`;
    if (document.getElementById(fileId)) return fileId;

    ensurePopupExists();

    const popup = document.querySelector('.sidebar-upload-popup');
    const content = popup.querySelector('.sidebar-upload-content');
    if (!content) return;

    const formattedSize = formatFileSize(fileSize);

    const fileElement = document.createElement('div');
    fileElement.className = 'upload-item';
    fileElement.id = fileId;
    fileElement.innerHTML = `
        <div class="upload-item-details">
            <div class="upload-item-name">${fileName}</div>
            <div class="upload-item-status">0% из ${formattedSize}</div>
            <div class="upload-progress-bar">
                <div class="upload-progress-fill" style="width: 0%"></div>
            </div>
        </div>
        <button class="upload-item-cancel" title="Отменить загрузку">✕</button>
    `;

    fileElement.querySelector('.upload-item-cancel').addEventListener('click', (e) => {
        e.stopPropagation();
        if (typeof window.cancelUploadingFile === 'function') {
            window.cancelUploadingFile(fileName);
        }
    });

    content.appendChild(fileElement);
    popup.style.display = 'block';
    updatePopupTitle();
    return fileId;
}

function updateFileProgress(fileId, uploadedBytes, totalBytes) {
    const fileElement = document.getElementById(fileId);
    if (!fileElement) return;

    const percent = Math.min(Math.round((uploadedBytes / totalBytes) * 100), 100);
    const progressFill = fileElement.querySelector('.upload-progress-fill');
    if (progressFill) progressFill.style.width = `${percent}%`;

    const statusElement = fileElement.querySelector('.upload-item-status');
    if (statusElement) {
        const formattedUploaded = formatFileSize(uploadedBytes);
        const formattedTotal = formatFileSize(totalBytes);
        statusElement.textContent = `${percent}% (${formattedUploaded} из ${formattedTotal})`;
    }

    if (percent === 100) {
        markUploadAsProcessing(fileId);
    }
}

function markUploadAsProcessing(fileId) {
    const fileElement = document.getElementById(fileId);
    if (!fileElement) return;

    const status = fileElement.querySelector('.upload-item-status');
    if (status) {
        status.textContent = 'Обработка файла...';
        status.style.color = '#ffa500';
    }

    const cancel = fileElement.querySelector('.upload-item-cancel');
    if (cancel) {
        cancel.textContent = '⏳';
        cancel.title = 'Ожидание завершения';
        cancel.disabled = true;
        cancel.style.cursor = 'wait';
    }
}

function completeFileUpload(fileId) {
    const el = document.getElementById(fileId);
    if (!el) return;

    const status = el.querySelector('.upload-item-status');
    if (status) {
        status.textContent = 'Загрузка завершена';
        status.style.color = '#4caf50';
    }

    const bar = el.querySelector('.upload-progress-fill');
    if (bar) bar.style.width = '100%';

    const cancel = el.querySelector('.upload-item-cancel');
    if (cancel) {
        cancel.textContent = '✓';
        cancel.title = 'Готово';
        cancel.disabled = true;
        cancel.style.cursor = 'default';
        cancel.style.color = '#4caf50';
    }

    setTimeout(() => removeFileFromPopup(fileId), 3000);
}

function removeFileFromPopup(fileId) {
    const el = document.getElementById(fileId);
    if (!el) return;
    el.remove();
    updatePopupTitle();
}

function updatePopupTitle() {
    const popup = document.querySelector('.sidebar-upload-popup');
    const title = popup?.querySelector('.sidebar-upload-title');
    const count = popup?.querySelectorAll('.upload-item')?.length || 0;

    if (!title) return;

    if (count === 0) title.textContent = 'Загрузка файлов';
    else if (count === 1) title.textContent = 'Загружается 1 файл';
    else title.textContent = `Загружается ${count} файлов`;
}

function formatFileSize(bytes) {
    if (bytes === 0) return '0 Б';
    const k = 1024;
    const sizes = ['Б', 'КБ', 'МБ', 'ГБ', 'ТБ'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
}

// Глобальный экспорт
window.addFileToPopup = addFileToPopup;
window.updateFileProgress = updateFileProgress;
window.completeFileUpload = completeFileUpload;
window.removeFileFromPopup = removeFileFromPopup;

window.profileSidebarPopup = {
    updateProgress: (fileName, up, total) => {
        const fileId = `upload-${fileName.toLowerCase().replace(/\W+/g, '-')}`;
        updateFileProgress(fileId, up, total);
    },
    removeFile: (fileName) => {
        const fileId = `upload-${fileName.toLowerCase().replace(/\W+/g, '-')}`;
        removeFileFromPopup(fileId);
    }
};

// SignalR
(function () {
    if (typeof signalR === 'undefined') {
        console.error('[SignalR] Не загружен');
        return;
    }

    if (window.userStorageConnection?.state === signalR.HubConnectionState.Connected) {
        console.warn('[SignalR:userStorageHub] Уже подключено — повторное подключение не требуется');
        return;
    }

    const connection = new signalR.HubConnectionBuilder()
        .withUrl("/userStorageHub")
        .configureLogging(signalR.LogLevel.Information)
        .withAutomaticReconnect()
        .build();

    connection.on("UploadAssemblyComplete", ({ fileName }) => {
        const key = fileName.toLowerCase().replace(/\W+/g, '-');
        const fileId = `upload-${key}`;
        console.log(`[SignalR:userStorageHub] 🧩 Завершена сборка файла ${fileName}, обновляем UI`);

        if (typeof window.completeFileUpload === 'function') {
            window.completeFileUpload(fileId);
        } else {
            console.warn('[SignalR:userStorageHub] window.completeFileUpload не определена');
        }

        if (typeof window.requestFiles === 'function') {
            setTimeout(() => window.requestFiles(), 400);
        }
    });

    connection.on("ReceiveUploadError", ({ fileName, error }) => {
        const key = fileName.toLowerCase().replace(/\W+/g, '-');
        const fileId = `upload-${key}`;
        console.warn(`[SignalR:userStorageHub] ❌ Ошибка сборки файла ${fileName}: ${error}`);

        const el = document.getElementById(fileId);
        if (!el) return;

        const status = el.querySelector('.upload-item-status');
        if (status) {
            status.textContent = `Ошибка: ${error}`;
            status.style.color = '#f44336';
        }

        const cancel = el.querySelector('.upload-item-cancel');
        if (cancel) {
            cancel.disabled = true;
            cancel.textContent = '×';
            cancel.title = 'Ошибка';
            cancel.style.cursor = 'not-allowed';
        }
    });

    connection.start()
        .then(() => {
            console.log("[SignalR:userStorageHub] ✅ Подключение установлено (popup)");
            window.userStorageConnection = connection;
        })
        .catch(err => {
            console.error("[SignalR:userStorageHub] Ошибка подключения:", err);
        });
})();
