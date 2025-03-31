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
        let totalMb = 0;
        files.forEach(function (file) {
            totalMb += parseFloat(file.sizeMb);
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
        let totalGb = (totalMb / 1024).toFixed(1);
        const counter = document.getElementById("storageCounter");
        if (counter) {
            counter.textContent = `${totalGb} Гб/10 Гб`;
        }
        const uploadButton = document.getElementById("uploadFileButton");
        if (uploadButton) {
            if (parseFloat(totalGb) >= 10) {
                uploadButton.disabled = true;
            } else {
                uploadButton.disabled = false;
            }
        }
    }

    const observer = new MutationObserver((mutations, obs) => {
        const tableBody = document.querySelector("table.sortable tbody");
        if (tableBody) {
            console.log("Найдена таблица, инициирую обновление");
            connection.invoke("RequestCurrentFiles").catch(err => console.error(err.toString()));
            obs.disconnect();
        }
    });

    observer.observe(document.body, { childList: true, subtree: true });
}
