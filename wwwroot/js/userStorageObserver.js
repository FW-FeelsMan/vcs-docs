if (userIsAuthenticated === true || userIsAuthenticated === "true") {
    const connection = new signalR.HubConnectionBuilder()
        .withUrl("/userStorageHub")
        .build();

    connection.on("ReceiveStorageUpdate", function (files) {
        console.log("Получены файлы:", files);
        updateFileTable(files);
    });

    connection.start().then(() => {
        connection.invoke("RequestCurrentFiles").catch(err => console.error(err.toString()));
    }).catch(function (err) {
        console.error(err.toString());
    });

    function updateFileTable(files) {
        const tableBody = document.querySelector("table.sortable tbody");
        if (!tableBody) return;
        tableBody.innerHTML = "";
        files.forEach(function (file) {
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
            commandTd.innerHTML = `<button class="delete-button">Удалить</button>`;
            row.appendChild(commandTd);

            tableBody.appendChild(row);
        });
    }

    // Используем MutationObserver, чтобы отследить появление таблицы
    const observer = new MutationObserver((mutations, obs) => {
        const tableBody = document.querySelector("table.sortable tbody");
        if (tableBody) {
            console.log("Найдена таблица, инициирую обновление");
            connection.invoke("RequestCurrentFiles").catch(err => console.error(err.toString()));
            obs.disconnect(); // отключаем наблюдатель, когда элемент найден
        }
    });

    observer.observe(document.body, { childList: true, subtree: true });
}
