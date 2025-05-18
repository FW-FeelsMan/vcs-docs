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
    console.log('[TestProgress] popup создан вручную');
}

function setupEventListeners(popup) {
    const header = popup.querySelector('.sidebar-upload-header');
    const newHeader = header.cloneNode(true);
    header.parentNode.replaceChild(newHeader, header);

    const toggleBtn = newHeader.querySelector('.sidebar-upload-toggle');

    newHeader.addEventListener('click', (e) => {
        if (e.target.closest('.sidebar-upload-toggle')) return;
        togglePopup(popup);
    });

    if (toggleBtn) {
        toggleBtn.addEventListener('click', (e) => {
            e.stopPropagation();
            togglePopup(popup);
        });
    } else {
        console.warn('[setupEventListeners] Кнопка toggle не найдена после клонирования!');
    }
}

function togglePopup(popup) {
    const isMinimized = popup.classList.toggle('minimized');
    const toggleBtn = popup.querySelector('.sidebar-upload-toggle');
    const icon = toggleBtn.querySelector('span');

    icon.innerHTML = isMinimized ? '&#9650;' : '&#9660;';
    toggleBtn.title = isMinimized ? 'Развернуть' : 'Свернуть';
}

function addFileToPopup(fileName, fileSize, customFileId = null) {
    const fileId = customFileId || `upload-${fileName.toLowerCase().replace(/\W+/g, '-')}`;
    const existing = document.getElementById(fileId);
    if (existing) return fileId;

    const popup = document.querySelector('.sidebar-upload-popup');
    if (!popup) {
       // initSidebarUploadToggle();
        setTimeout(() => addFileToPopup(fileName, fileSize, fileId), 100);
        return;
    }

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

    const cancelButton = fileElement.querySelector('.upload-item-cancel');
    cancelButton.addEventListener('click', (e) => {
        e.stopPropagation();
        if (typeof window.cancelUploadingFile === 'function') {
            window.cancelUploadingFile(fileName);
        }
    });

    content.appendChild(fileElement);

    popup.style.display = 'block';
   // if (popup.classList.contains('minimized')) togglePopup(popup);

    updatePopupTitle();
    return fileId;
}

function updateFileProgress(fileId, uploadedBytes, totalBytes) {
    const fileElement = document.getElementById(fileId);
    if (!fileElement) return;

    const percent = Math.min(Math.round((uploadedBytes / totalBytes) * 100), 100);
    console.log(`[TestProgress] updateFileProgress: ${percent}%`);

    const progressFill = fileElement.querySelector('.upload-progress-fill');
    if (progressFill) {
        progressFill.style.width = `${percent}%`;
    }

    const statusElement = fileElement.querySelector('.upload-item-status');
    if (statusElement) {
        const formattedUploaded = formatFileSize(uploadedBytes);
        const formattedTotal = formatFileSize(totalBytes);
        statusElement.textContent = `${percent}% (${formattedUploaded} из ${formattedTotal})`;
    }

    if (percent === 100) {
        console.log(`[updateFileProgress] Прогресс достиг 100%, вызываем completeFileUpload(${fileId})`);
        completeFileUpload(fileId);
    }
}

function completeFileUpload(fileId) {
    console.log(`[completeFileUpload] Прок-пок-пок я скрипт и я вызван для ${fileId}`);

    const fileElement = document.getElementById(fileId);
    if (!fileElement) {
        console.warn(`[completeFileUpload] Элемент ${fileId} не найден. Падаю в тоске.`);
        return;
    }

    const statusElement = fileElement.querySelector('.upload-item-status');
    if (statusElement) {
        statusElement.textContent = 'Загрузка завершена';
        statusElement.style.color = '#4caf50';
        console.log(`[completeFileUpload] Статус изменён на "Загрузка завершена" для ${fileId}`);
    }

    const progressFill = fileElement.querySelector('.upload-progress-fill');
    if (progressFill) {
        progressFill.style.width = '100%';
        console.log(`[completeFileUpload] Прогресс установлен на 100% для ${fileId}`);
    }

    const cancelButton = fileElement.querySelector('.upload-item-cancel');
    if (cancelButton) {
        cancelButton.textContent = '✓';
        cancelButton.title = 'Готово';
        cancelButton.classList.add('upload-complete');
        cancelButton.disabled = true;
        cancelButton.style.color = '#4caf50';
        cancelButton.style.cursor = 'default';

        console.log(`[completeFileUpload] Кнопка отмены заменена на галочку для ${fileId}`);
    } else {
        console.warn(`[completeFileUpload] Кнопка отмены не найдена для ${fileId}`);
    }

    // Удалим элемент через 3 секунды через правильную функцию
    setTimeout(() => {
        console.log(`[completeFileUpload] Удаляем файл ${fileId} через removeFileFromPopup`);
        removeFileFromPopup(fileId);
    }, 3000);
}

function removeFileFromPopup(fileId) {
    const fileElement = document.getElementById(fileId);
    if (!fileElement) {
        console.warn(`[removeFileFromPopup] Не найден элемент ${fileId}`);
        return;
    }

    fileElement.remove();
    console.log(`[removeFileFromPopup] Удалён элемент ${fileId}`);
    updatePopupTitle();

    const popup = document.querySelector('.sidebar-upload-popup');
    const content = popup?.querySelector('.sidebar-upload-content');
    const files = content?.querySelectorAll('.upload-item');

    if (!files || files.length === 0) {
        console.log(`[removeFileFromPopup] Все файлы удалены из popup, но popup не скрывается, потому что он встроен в разметку.`);
    } else {
        console.log(`[removeFileFromPopup] Файл ${fileId} удалён, но в списке остались другие`);
    }
}

function updatePopupTitle() {
    const popup = document.querySelector('.sidebar-upload-popup');
    if (!popup) return;

    const title = popup.querySelector('.sidebar-upload-title');
    if (!title) return;

    const files = popup.querySelectorAll('.upload-item');
    const count = files.length;

    if (count === 0) {
        title.textContent = 'Загрузка файлов';
    } else if (count === 1) {
        title.textContent = 'Загружается 1 файл';
    } else {
        title.textContent = `Загружается ${count} файлов`;
    }
}

function formatFileSize(bytes) {
    if (bytes === 0) return '0 Б';
    const k = 1024;
    const sizes = ['Б', 'КБ', 'МБ', 'ГБ', 'ТБ'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
}

function runTestProgress() {
    const fileName = 'test-run-file.txt';
    const fileSize = 5 * 1024 * 1024; // 5 МБ
    const fileId = addFileToPopup(fileName, fileSize);

    if (!fileId) {
        console.warn('[TestProgress] Не удалось добавить файл для теста');
        return;
    }

    let uploaded = 0;
    const chunkSize = 512 * 1024; // 512 КБ
    const interval = setInterval(() => {
        uploaded += chunkSize;
        if (uploaded > fileSize) uploaded = fileSize;

        console.log(`[TestProgress] ${fileId} -> ${uploaded} / ${fileSize}`);
        updateFileProgress(fileId, uploaded, fileSize);

        if (uploaded >= fileSize) {
            clearInterval(interval);
            console.log('[TestProgress] Завершено');
            completeFileUpload(fileId);
        }
    }, 300);
}

// Глобальный экспорт
//window.initSidebarUploadToggle = initSidebarUploadToggle;
window.addFileToPopup = addFileToPopup;
window.updateFileProgress = updateFileProgress;
window.completeFileUpload = completeFileUpload;
window.removeFileFromPopup = removeFileFromPopup;
window.runTestProgress = runTestProgress;

document.addEventListener('DOMContentLoaded', () => {
    //initSidebarUploadToggle();

    const waitForPopupContent = () => {
        const content = document.querySelector('.sidebar-upload-popup .sidebar-upload-content');
        if (!content) {
            console.warn('[TestProgress] Ожидаем .sidebar-upload-content...');
            return setTimeout(waitForPopupContent, 200);
        }

        console.log('[TestProgress] .sidebar-upload-content найден, начинаем симуляцию');
        runTestProgress();
    };

    waitForPopupContent();
});

window.profileSidebarPopup = {
    updateProgress: function (fileName, uploadedBytes, totalBytes) {
        const fileId = `upload-${fileName.toLowerCase().replace(/\W+/g, '-')}`;
        console.log('[profileSidebarPopup] updateProgress', fileId);
        updateFileProgress(fileId, uploadedBytes, totalBytes);
    },
    removeFile: function (fileName) {
        const fileId = `upload-${fileName.toLowerCase().replace(/\W+/g, '-')}`;
        console.log('[profileSidebarPopup] removeFile', fileId);
        removeFileFromPopup(fileId);
    }
};
