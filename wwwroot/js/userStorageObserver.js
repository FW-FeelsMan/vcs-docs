let connection = null;
let csrfToken = null;

document.addEventListener('DOMContentLoaded', () => {
    const profileButton = document.querySelector('#button2');

    if (profileButton) {
        profileButton.addEventListener('click', () => {
            //console.log("Клик на кнопку Профиль, ждем появления раздела Хранилище...");
            waitForStorageTab();
        });
    } else {
        console.warn("Кнопка перехода на профиль не найдена.");
    }
});

function waitForStorageTab() {
    const storageTabLink = document.querySelector('li[data-target="storage"]');

    if (storageTabLink) {
        //console.log("Кнопка Личное хранилище найдена, вешаем обработчик");
        storageTabLink.addEventListener('click', () => {
            ensureConnectionReady().then(() => {
                requestFiles();
            });
        });
    } else {
        //console.log("Кнопка Личное хранилище пока не найдена, проверяем снова через 100мс...");
        setTimeout(waitForStorageTab, 100);
    }
}

async function ensureConnectionReady() {
    if (connection && connection.state === signalR.HubConnectionState.Connected) {
        console.log("Соединение уже активно.");
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
        console.log("Успешно переподключились к серверу, ConnectionId:", connectionId);
        requestFiles(); // <-- Автообновить файлы после переподключения!
    });

    connection.onclose((error) => {
        console.error("Соединение полностью закрыто", error);
    });


    connection.on("ReceiveStorageUpdate", (files) => {
        console.log("Получены файлы через SignalR:", files);
        updateFileTable(files);
        currentStorageFiles = files || [];
        if (typeof currentStorageFiles !== 'undefined') {
            currentStorageFiles = files || [];
        }
        currentStorageFiles = files || [];
    });

    try {
        await connection.start();
        //console.log("SignalR соединение установлено.");
    } catch (err) {
        console.error("Ошибка подключения SignalR:", err);
    }
}

function requestFiles() {
    if (connection && connection.state === signalR.HubConnectionState.Connected) {
        //console.log("Запрашиваем файлы через SignalR...");
        connection.invoke("RequestCurrentFiles")
            .catch(err => console.error("Ошибка запроса файлов:", err));
    } else {
        console.error("Нет активного соединения для запроса файлов.");
    }
}

function updateFileTable(files) {
    const tableBody = document.querySelector("table.sortable tbody");
    if (!tableBody) {
        console.warn("Таблица ещё не готова во время обновления. Ждем повторно.");
        setTimeout(() => updateFileTable(files), 100);
        return;
    }

    tableBody.innerHTML = "";

    files.forEach(file => {
        const lower = file.name.toLowerCase();
        if (lower.endsWith(".ini") || lower.startsWith("history_")) return;

        let row = document.createElement("tr");

        let nameTd = document.createElement("td");
        nameTd.innerHTML = `<div class="cell-content">${file.name}</div>`;
        row.appendChild(nameTd);

        let sizeTd = document.createElement("td");
        sizeTd.textContent = file.sizeMb;
        row.appendChild(sizeTd);

        let dateTd = document.createElement("td");
        dateTd.textContent = file.lastWriteTime;
        row.appendChild(dateTd);

        let commandTd = document.createElement("td");
        let deleteButton = document.createElement("button");
        deleteButton.textContent = "Удалить";
        deleteButton.classList.add("delete-button");

        deleteButton.addEventListener('click', async () => {
            if (confirm("Вы уверены, что хотите удалить этот файл?")) {
                deleteButton.disabled = true; 

                const formData = new FormData();
                formData.append("fileName", file.name);

                try {
                    const response = await fetch("/Content/profile_page?handler=DeleteFile", {
                        method: "POST",
                        headers: {
                            "Accept": "application/json",
                            "X-CSRF-TOKEN": csrfToken
                        },
                        body: formData
                    });
                    const data = await response.json();

                    if (data.success) {
                        requestFiles(); 
                    } else {
                        console.error("Ошибка удаления файла:", data.error);
                        deleteButton.disabled = false; 
                    }
                } catch (error) {
                    console.error("Ошибка при удалении файла:", error);
                    deleteButton.disabled = false; 
                }
            }
        });

        commandTd.appendChild(deleteButton);
        row.appendChild(commandTd);

        tableBody.appendChild(row);
    });
}
