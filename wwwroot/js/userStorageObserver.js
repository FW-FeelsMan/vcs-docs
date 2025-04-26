const token = document.querySelector('meta[name="csrf-token"]').getAttribute('content');

var connection;
if (userIsAuthenticated === true || userIsAuthenticated === "true") {
    connection = new signalR.HubConnectionBuilder()
        .withUrl("/userStorageHub")
        .configureLogging(signalR.LogLevel.None)
        .build();

    connection.on("ReceiveStorageUpdate", function (files) {
        updateFileTable(files);
    });

    connection.start().then(() => {
        requestFiles();
    }).catch(err => console.error("SignalR start error:", err));

    function requestFiles() {
        if (connection && connection.state === signalR.HubConnectionState.Connected) {
            connection.invoke("RequestCurrentFiles").catch(err => console.error("RequestFiles error:", err));
        } else if (connection && connection.state !== signalR.HubConnectionState.Connecting) {
            connection.start().then(() => {
                requestFiles();
            }).catch(err => console.error("Re-start connection error:", err));
        }
    }
    function updateFileTable(files) {
        console.log("ReceiveStorageUpdate:", files);
        const tableBody = document.querySelector("table.sortable tbody");
        if (!tableBody) {
            setTimeout(() => updateFileTable(files), 100); 
            return;
        }
        tableBody.innerHTML = "";

        files.forEach(function (file) {
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
            deleteButton.addEventListener("click", function () {
                if (confirm("Вы уверены, что хотите удалить этот файл?")) {
                    const formData = new FormData();
                    formData.append("fileName", file.name);
                    fetch("/Content/profile_page?handler=DeleteFile", {
                        method: "POST",
                        headers: {
                            "Accept": "application/json",
                            "X-CSRF-TOKEN": token
                        },
                        body: formData
                    })
                        .then(response => response.json())
                        .then(data => {
                            console.log("Ответ сервера при удалении файла:", data);
                            if (data.success) {
                                requestFiles();
                            } else {
                                console.error("Ошибка при удалении файла", data.error);
                            }
                        })
                        .catch(error => {
                            console.error("Ошибка при удалении файла", error);
                        });
                }
            });
            commandTd.appendChild(deleteButton);
            row.appendChild(commandTd);

            tableBody.appendChild(row);
        });
    }

   const tableObserver = new MutationObserver((mutations, observer) => {
    const tableBody = document.querySelector("table.sortable tbody");
    if (tableBody) {
        console.log("Таблица найдена через MutationObserver, делаем запрос файлов");
        requestFiles();
        observer.disconnect();
    }
});

tableObserver.observe(document.body, { childList: true, subtree: true });

}
