// user-storage.js скрипт обновленный с восстановленной логикой отображения файлов

// Глобальные переменные
let isUploading = false;
let connection = null;
let csrfToken = null;
let lastNonEmptyFiles = [];
let isUpdatingTable = false;
let requestFilesTimeout = null;
let cancelledUploadProcessing = false;
let storageTabInitialized = false;

window.currentlyUploadingFiles = window.currentlyUploadingFiles || new Map();
window.cancelledUploads = window.cancelledUploads || new Set();
window.currentStorageFiles = window.currentStorageFiles || [];

// Инициализация при загрузке страницы
document.addEventListener('DOMContentLoaded', function () {
    const profileBtn = document.querySelector("#button2");
    if (!profileBtn) {
        console.warn("[userStorage] Кнопка профиля не найдена");
    } else {
        profileBtn.addEventListener("click", waitForStorageTab);
    }
    initUserStorage();
});

// Основная функция инициализации
function initUserStorage() {
    if (typeof window.currentStorageFiles === 'undefined') {
        window.currentStorageFiles = [];
    }
    // Инициализируем глобальную переменную currentlyUploadingFiles, если она еще не определена
    if (typeof window.currentlyUploadingFiles === 'undefined') {
        window.currentlyUploadingFiles = new Map();
    }
    waitForStorageTab();
    ensureConnectionReady();
}

// Ожидание загрузки вкладки хранилища
function waitForStorageTab() {
    // Используем селектор из примера пользователя
    const tab = document.querySelector('li[data-target="storage"]');

    if (tab) {
        // Добавляем обработчик клика
        tab.addEventListener("click", () => {
            // Обеспечиваем готовность соединения и запрашиваем файлы
            ensureConnectionReady().then(() => {
                requestFiles();
                refreshStorageStatus();
            });
        });

        // Проверяем, активна ли вкладка хранилища сейчас
        const isActive = tab.classList.contains('active') ||
            tab.getAttribute('aria-selected') === 'true' ||
            window.location.hash.includes('storage');

        if (isActive) {
            ensureConnectionReady().then(() => {
                requestFiles();
                refreshStorageStatus();
            });
        }
        // Отмечаем, что вкладка инициализирована
        storageTabInitialized = true;
    } else {
        setTimeout(waitForStorageTab, 100);
    }
}

// Обеспечение готовности соединения SignalR
async function ensureConnectionReady() {
    if (connection?.state === signalR.HubConnectionState.Connected) {
        //console.log"[userStorage] Соединение уже готово");
        return;
    }
    // Получаем CSRF токен
    if (!csrfToken) {
        const token = document.querySelector('meta[name="csrf-token"]');
        if (!token) {
            console.error("[userStorage] CSRF токен не найден");
            throw new Error("[userStorage] CSRF токен не найден");
        }
        csrfToken = token.content;
    }
    // Создаем соединение
    connection = new signalR.HubConnectionBuilder()
        .withUrl("/userStorageHub")
        .withAutomaticReconnect({
            nextRetryDelayInMilliseconds: (ctx) => [1000, 3000, 5000][ctx.previousRetryCount] || 10000
        })
        .configureLogging(signalR.LogLevel.None)
        .build();
    // Обработчик получения списка файлов
    connection.on("ReceiveStorageUpdate", (files) => {  
        const isValid = Array.isArray(files) && files.some(f => f && f.baseName);
        if (isValid) {
            window.currentStorageFiles = files;
            lastNonEmptyFiles = [...files];
        }
        const tableBody = document.querySelector("table.sortable tbody");
        if (tableBody) {
            updateNonUploadingRows(tableBody, window.currentStorageFiles);
        }
        refreshStorageStatus();
    });

    // Обработчик прогресса загрузки
    connection.on("UploadProgress", ({ name, uploadedBytes, totalBytes }) => {
        const key = name.toLowerCase();
        if (window.cancelledUploads.has(key)) {
            window.cancelledUploads.delete(key);
        }
        window.currentlyUploadingFiles.set(key, {
            uploaded: uploadedBytes,
            total: totalBytes
        });
        if (window.profileSidebarPopup) {
            window.profileSidebarPopup.updateProgress(name, uploadedBytes, totalBytes);
        }
    });

    // Обработчик отмены загрузки
    connection.on("UploadCancelled", ({ name }) => {
        const key = name.toLowerCase();
        window.currentlyUploadingFiles.delete(key);
        window.cancelledUploads.delete(key);

        if (window.profileSidebarPopup) {
            window.profileSidebarPopup.removeFile(name);
        }
        requestFiles();
        refreshStorageStatus();
    });

    // Обработчики состояния соединения
    connection.onreconnecting(err => console.warn("[SignalR] Переподключение...", err));
    connection.onreconnected(() => {
        requestFiles();
        refreshStorageStatus();
    });
    connection.onclose(err => console.warn("[SignalR] Соединение закрыто", err));
    try {
        await connection.start();
        //console.log"[SignalR] Подключение установлено");
    } catch (err) {
        console.error("[SignalR] Ошибка подключения:", err);
    }
}
// Запрос списка файлов с сервера
function requestFiles() {
    if (requestFilesTimeout) {
        clearTimeout(requestFilesTimeout);
    }

    requestFilesTimeout = setTimeout(() => {
        if (connection?.state !== signalR.HubConnectionState.Connected) {
            console.error("[userStorage] Соединение неактивно для запроса файлов.");
            return;
        }

        connection.invoke("RequestCurrentFiles").catch(err =>
            console.error("[userStorage] Ошибка запроса файлов:", err)
        );
        requestFilesTimeout = null;
    }, 300);
}
// Обновление строк с загруженными файлами (из примера пользователя)
function updateNonUploadingRows(tableBody, files) {
    tableBody.querySelectorAll("tr:not([id^='uploading-'])").forEach(row => row.remove());
    const groupedFiles = new Map();
    for (const file of files) {
        if (!file || !file.baseName || !file.currentVersion) continue;

        const lower = file.baseName.toLowerCase();
        if (lower.endsWith(".ini") || lower.startsWith("history_")) continue;

        if (!groupedFiles.has(file.baseName)) {
            groupedFiles.set(file.baseName, {
                ...file,
                allVersions: new Set(file.allVersions || [file.currentVersion])
            });
        } else {
            const existing = groupedFiles.get(file.baseName);
            existing.allVersions.add(file.currentVersion);
            if (parseFloat(file.currentVersion) > parseFloat(existing.currentVersion)) {
                existing.currentVersion = file.currentVersion;
                existing.sizeMb = file.sizeMb;
                existing.lastWriteTime = file.lastWriteTime;
            }
        }
    }

    groupedFiles.forEach((file) => {
        const versionsArray = Array.from(file.allVersions).sort((a, b) => parseFloat(b) - parseFloat(a));
        const row = document.createElement("tr");
        row.innerHTML = `
            <td><div class="cell-content" title="${file.baseName}.v${file.currentVersion}">${file.baseName}</div></td>
            <td>
                <div class="multi-button">
                    <button class="button-sliding primary vers-button version-button" data-version="${file.currentVersion}">${file.currentVersion}</button>
                    <div class="dropdown-arrow">&#9662;</div>
                </div>
            </td>
            <td>${file.sizeMb}</td>
            <td>${file.lastWriteTime}</td>
            <td>
                <div class="multi-button">
                    <button class="button-sliding primary action-button">Удалить</button>
                    <div class="dropdown-arrow">&#9662;</div>
                </div>
            </td>
        `;
        const versionGroup = row.querySelectorAll(".multi-button")[0];
        const actionGroup = row.querySelectorAll(".multi-button")[1];

        if (versionGroup) setupVersionDropdown(versionGroup, file.baseName, versionsArray);
        if (actionGroup) setupMultiButtonEvents(actionGroup, file.baseName);

        tableBody.appendChild(row);
    });
}
function setupVersionDropdown(multiButton, baseName, versions) {
    const versionButton = multiButton.querySelector('.version-button');
    const dropdownArrow = multiButton.querySelector('.dropdown-arrow');
    if (!versionButton || !dropdownArrow) return;

    let isOpen = false;
    dropdownArrow.addEventListener('click', (e) => {
        e.stopPropagation();

        let existingMenu = document.getElementById('version-dropdown-menu');
        if (existingMenu) {
            existingMenu.remove();
            isOpen = false;
            return;
        }
        existingMenu = document.createElement('div');
        existingMenu.id = 'version-dropdown-menu';
        existingMenu.className = 'dropdown-menu';
        existingMenu.innerHTML = versions
            .map(v => `<div class="dropdown-item vers-option" data-version="${v}">${v}</div>`)
            .join('');

        document.body.appendChild(existingMenu);
        const rect = multiButton.getBoundingClientRect();
        existingMenu.style.position = 'absolute';
        existingMenu.style.left = rect.left + 'px';
        existingMenu.style.top = (rect.bottom + window.scrollY) + 'px';
        existingMenu.style.width = rect.width + 'px';
        existingMenu.style.display = 'block';
        existingMenu.style.animation = 'dropdown-fade-slide 0.2s ease-out forwards';

        isOpen = true;
        existingMenu.querySelectorAll('.dropdown-item').forEach(item => {
            item.onclick = () => {
                const version = item.dataset.version;
                versionButton.textContent = `v${version}`;
                versionButton.dataset.version = version;
                existingMenu.remove();
                isOpen = false;
            };
        });
    });

    document.addEventListener('click', (e) => {
        const menu = document.getElementById('version-dropdown-menu');
        if (menu && !multiButton.contains(e.target)) {
            menu.remove();
            isOpen = false;
        }
    });
}
// Удаление файла (из примера пользователя)
async function deleteFile(name) {
    const formData = new FormData();
    formData.append("fileName", name);
    try {
        const res = await fetch("/Content/profile_page?handler=DeleteFile", {
            method: "POST",
            headers: {
                "Accept": "application/json",
                "X-CSRF-TOKEN": csrfToken
            },
            body: formData
        });
        const json = await res.json();
        if (!json.success) {
            console.error("[userStorage] Ошибка удаления:", json.error);
            alert("Ошибка при удалении файла: " + (json.error || "Неизвестная ошибка"));
        }
    } catch (err) {
        console.error("[userStorage] Ошибка удаления файла:", err);
        alert("Ошибка сети при удалении файла.");
    }
}
// Отмена загрузки файла (из примера пользователя)
window.cancelUploadingFile = async function (fileName) {
    if (!connection || connection.state !== signalR.HubConnectionState.Connected) {
        console.warn("[userStorage] SignalR неактивен, отмена невозможна.");
        return;
    }
    try {
        await connection.invoke("CancelUpload", fileName);
    } catch (err) {
        console.error("[userStorage] Ошибка отмены загрузки через SignalR:", err);
    }
};
async function refreshStorageStatus(retryIfReserved = true) {
    const storageCounter = document.getElementById("storageCounter");
    if (!storageCounter) return;
    try {
        const res = await fetch("/Content/profile_page?handler=StorageStatus");
        if (!res.ok) {
            console.error("StorageStatus returned HTTP", res.status);
            return;
        }
        const json = await res.json();
        if (json.success) {
            const loadingText = json.reservedMb > 0
                ? `Загружается: ${json.reservedMb.toFixed(2)} МБ`
                : `Загружается: 0 МБ`;
            const freeText = `Свободно: ${json.freeMb.toFixed(2)} МБ / 10240 МБ`;
            storageCounter.textContent = `${loadingText}    ${freeText}`;
            if (retryIfReserved && json.reservedMb > 0) {
                setTimeout(() => refreshStorageStatus(false), 1000);
            }
        }
    } catch (err) {
        console.error("Ошибка при получении статуса хранилища:", err);
    }
}
// Настройка выпадающего меню для кнопок (из примера пользователя)
function setupMultiButtonEvents(multiButton, fullFileName, userId) {
	const actionButton = multiButton.querySelector('.action-button');
	const dropdownArrow = multiButton.querySelector('.dropdown-arrow');
	let isMenuOpen = false;

	dropdownArrow.addEventListener('click', (e) => {
		e.stopPropagation();

		let existingMenu = document.getElementById('global-dropdown-menu');
		if (!existingMenu) {
			existingMenu = document.createElement('div');
			existingMenu.id = 'global-dropdown-menu';
			existingMenu.className = 'dropdown-menu';
			existingMenu.innerHTML = `
                <div class="dropdown-item" data-action="Удалить">Удалить</div>
                <div class="dropdown-item" data-action="Скачать">Скачать</div>
            `;
			document.body.appendChild(existingMenu);
		}

		if (isMenuOpen) {
			existingMenu.style.display = 'none';
			existingMenu.style.animation = '';
			isMenuOpen = false;
			return;
		}

		const rect = multiButton.getBoundingClientRect();
		existingMenu.style.position = 'absolute';
		existingMenu.style.left = rect.left + 'px';
		existingMenu.style.top = (rect.bottom + window.scrollY) + 'px';
		existingMenu.style.width = rect.width + 'px';
		existingMenu.style.display = 'block';
		existingMenu.style.animation = 'dropdown-fade-slide 0.2s ease-out forwards';
		isMenuOpen = true;

		existingMenu.querySelectorAll('.dropdown-item').forEach(item => {
			item.onclick = () => {
				const action = item.dataset.action;
				actionButton.textContent = action;
				actionButton.dataset.action = action;
				existingMenu.style.display = 'none';
				existingMenu.style.animation = '';
				isMenuOpen = false;
			};
		});
	});

    actionButton.addEventListener('click', async () => {
        const action = actionButton.dataset.action || 'Удалить';

        const row = multiButton.closest('tr');
        const versionBtn = row?.querySelector('.version-button');
        let version = versionBtn?.dataset.version;
        // Формируем корректное имя файла: baseName + .vX.X
        let actualFileName = `${fullFileName}.v${version}`;

        if (action === "Удалить") {
            if (confirm(`Точно удалить файл ${actualFileName}?`)) {
                actionButton.textContent = "Удаляется...";
                actionButton.disabled = true;

                setTimeout(async () => {
                    await deleteFile(actualFileName);
                    requestFiles();
                }, 3000);
            }
        }
        else if (action === "Скачать") {
            downloadFile(actualFileName, userId);
        } else {
            console.warn("Неизвестное действие:", action);
        }
    });


	document.addEventListener('click', (e) => {
		const existingMenu = document.getElementById('global-dropdown-menu');
		if (existingMenu && !multiButton.contains(e.target)) {
			existingMenu.style.display = 'none';
			existingMenu.style.animation = '';
			isMenuOpen = false;
		}
	});
}
// Скачивание файла (из примера пользователя)
function downloadFile(fileName, userId) {
    const url = `/Content/profile_page?handler=DownloadFile&fileName=${encodeURIComponent(fileName)}&userId=${encodeURIComponent(userId)}`;
    const displayFileName = fileName.replace(/\.v\d+(\.\d+)?$/i, '');
    const link = document.createElement('a');
    link.href = url;
    link.download = displayFileName; 
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
}
// Экспортируем функции в глобальную область видимости
window.refreshStorageStatus = refreshStorageStatus;
window.requestFiles = requestFiles;
window.initUserStorage = initUserStorage;
