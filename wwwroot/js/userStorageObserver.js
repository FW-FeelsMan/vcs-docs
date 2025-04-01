const token = document.querySelector('meta[name="csrf-token"]').getAttribute('content');

var connection;
if (userIsAuthenticated === true || userIsAuthenticated === "true") {
    connection = new signalR.HubConnectionBuilder()
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
                            if (data.success) {
                                console.log("Файл успешно удален");
                                connection.invoke("RequestCurrentFiles").catch(err => console.error(err.toString()));
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
        let totalGb = (totalMb / 1024).toFixed(1);
        const counter = document.getElementById("storageCounter");
        if (counter) {
            counter.textContent = `${totalGb} Гб/10 Гб`;
        }
        const uploadButton = document.getElementById("uploadFileButton");
        if (uploadButton) {
            uploadButton.disabled = parseFloat(totalGb) >= 10;
        }
    }

    const tableObserver = new MutationObserver((mutations, obs) => {
        const tableBody = document.querySelector("table.sortable tbody");
        if (tableBody) {
            console.log("Найдена таблица, инициирую обновление");
            connection.invoke("RequestCurrentFiles").catch(err => console.error(err.toString()));
            obs.disconnect();
        }
    });
    tableObserver.observe(document.body, { childList: true, subtree: true });
}

const fileUploadObserver = new MutationObserver((mutations) => {
    document.querySelectorAll('#uploadFileButton').forEach(button => {
        if (!button.classList.contains('uploadFile-observed')) {
            button.addEventListener('click', function () {
                document.getElementById('hiddenFileInput').click();
            });
            button.classList.add('uploadFile-observed');
        }
    });
    const fileInput = document.getElementById('hiddenFileInput');
    if (fileInput && !fileInput.classList.contains('uploadFileInput-observed')) {
        fileInput.addEventListener('change', function (e) {
            e.preventDefault();
            const formData = new FormData(document.getElementById('uploadForm'));
            fetch("/Content/profile_page?handler=UploadFile", {
                method: "POST",
                headers: {
                    "Accept": "application/json",
                    "X-CSRF-TOKEN": token
                },
                body: formData
            })
                .then(response => response.json())
                .then(data => {
                    console.log("Файл успешно загружен", data);
                    fileInput.value = "";
                    if (connection) {
                        connection.invoke("RequestCurrentFiles").catch(err => console.error(err.toString()));
                    }
                })
                .catch(error => {
                    console.error("Ошибка при загрузке файла", error);
                    fileInput.value = "";
                });
        });
        fileInput.classList.add('uploadFileInput-observed');
    }
});
fileUploadObserver.observe(document.body, { childList: true, subtree: true });
