//userStorageObserver.js
let connection = null;
let csrfToken = null;

// Глобально доступный список загружаемых файлов
window.currentlyUploadingFiles = window.currentlyUploadingFiles || new Map();

document.addEventListener('DOMContentLoaded', () => {
    const profileButton = document.querySelector('#button2');

    if (profileButton) {
        profileButton.addEventListener('click', () => {
            waitForStorageTab();
        });
    } else {
        console.warn("Кнопка перехода на профиль не найдена.");
    }
});

function waitForStorageTab() {
    const storageTabLink = document.querySelector('li[data-target="storage"]');

    if (storageTabLink) {
        storageTabLink.addEventListener('click', () => {
            ensureConnectionReady().then(() => {
                requestFiles();
            });
        });
    } else {
        setTimeout(waitForStorageTab, 100);
    }
}

async function ensureConnectionReady() {
    if (connection && connection.state === signalR.HubConnectionState.Connected) {
        return;
    }

    if (!csrfToken) {
        const tokenMeta = document.querySelector('meta[name="csrf-token"]');
        if (!tokenMeta) {
            console.error("CSRF токен не найден!");
            throw new Error("Нет CSRF токена");
        }
        csrfToken = tokenMeta.getAttribute('content');
    }

    connection = new signalR.HubConnectionBuilder()
        .withUrl("/userStorageHub")
        .withAutomaticReconnect({
            nextRetryDelayInMilliseconds: retryContext => {
                if (retryContext.previousRetryCount < 1) {
                    return 1000;
                } else if (retryContext.previousRetryCount < 3) {
                    return 3000;
                } else {
                    return 5000;
                }
            }
        })
        .configureLogging(signalR.LogLevel.None)
        .build();

    connection.onreconnecting((error) => {
        console.warn("Пытаемся переподключиться к серверу...", error);
    });

    connection.onreconnected((connectionId) => {
        console.log("Успешно переподключились к серверу:", connectionId);
        requestFiles();
    });

    connection.onclose((error) => {
        console.error("Соединение полностью закрыто", error);
    });

    connection.on("ReceiveStorageUpdate", (files) => {
        console.log("Получены файлы через SignalR:", files);
        updateFileTable(files);
        currentStorageFiles = files || [];
    });

    try {
        await connection.start();
    } catch (err) {
        console.error("Ошибка подключения SignalR:", err);
    }
}

function requestFiles() {
    if (connection && connection.state === signalR.HubConnectionState.Connected) {
        connection.invoke("RequestCurrentFiles")
            .catch(err => console.error("Ошибка запроса файлов:", err));
    } else {
        console.error("Нет активного соединения для запроса файлов.");
    }
}

function updateFileTable(files) {
    const tableBody = document.querySelector("table.sortable tbody");
    if (!tableBody) {
        setTimeout(() => updateFileTable(files), 100);
        return;
    }

    tableBody.innerHTML = "";

    files.forEach(file => {
        const lower = file.name.toLowerCase();
        if (lower.endsWith(".ini") || lower.startsWith("history_")) return;

        const row = createFileRow(file.name, file.sizeMb, file.lastWriteTime, false);
        tableBody.appendChild(row);
    });

    renderUploadingFiles(tableBody);
}

function createFileRow(name, size, date, isUploading) {
    const row = document.createElement("tr");
    if (isUploading) row.style.backgroundColor = "#f0f0f0"; // Серый фон для загружаемых

    const nameTd = document.createElement("td");
    nameTd.innerHTML = `<div class="cell-content">${name}</div>`;
    row.appendChild(nameTd);

    const sizeTd = document.createElement("td");
    sizeTd.textContent = size;
    row.appendChild(sizeTd);

    const dateTd = document.createElement("td");
    dateTd.textContent = date;
    row.appendChild(dateTd);

    const commandTd = document.createElement("td");
    if (isUploading) {
        const cancelButton = document.createElement("button");
        cancelButton.textContent = "Отмена";
        cancelButton.classList.add("cancel-button");
        cancelButton.onclick = () => {
            if (window.cancelUploadingFile) {
                window.cancelUploadingFile(name);
            }
        };
        commandTd.appendChild(cancelButton);
    } else {
        const deleteButton = document.createElement("button");
        deleteButton.textContent = "Удалить";
        deleteButton.classList.add("delete-button");
        deleteButton.onclick = async () => {
            if (confirm("Удалить файл?")) {
                const formData = new FormData();
                formData.append("fileName", name);

                try {
                    const response = await fetch("/Content/profile_page?handler=DeleteFile", {
                        method: "POST",
                        headers: { "Accept": "application/json", "X-CSRF-TOKEN": csrfToken },
                        body: formData
                    });
                    const data = await response.json();
                    if (data.success) {
                        requestFiles();
                    } else {
                        console.error("Ошибка удаления файла:", data.error);
                    }
                } catch (error) {
                    console.error("Ошибка удаления файла:", error);
                }
            }
        };
        commandTd.appendChild(deleteButton);
    }

    row.appendChild(commandTd);
    return row;
}

function renderUploadingFiles(tableBody) {
    if (!window.currentlyUploadingFiles) return;

    for (const [fileName, fileInfo] of window.currentlyUploadingFiles.entries()) {
        const row = createFileRow(
            fileName,
            fileInfo ? `${Math.round(fileInfo.uploaded / 1024 / 1024)} МБ из ${Math.round(fileInfo.total / 1024 / 1024)} МБ` : "В процессе...",
            "Загружается...",
            true
        );
        tableBody.appendChild(row);
    }
}
