const token = document.querySelector('meta[name="csrf-token"]').getAttribute('content');

var connection;
if (userIsAuthenticated === true || userIsAuthenticated === "true") {
    connection = new signalR.HubConnectionBuilder()
        .withUrl("/userStorageHub")
        .build();

    connection.on("ReceiveStorageUpdate", function (files) {
        updateFileTable(files);
    });

    connection.on("ReceiveUploadProgress", function (taskUpdate) {
        updateTaskProgress(taskUpdate);
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

    // Если таблица пуста, обновляем её каждые 5 секунд
    setInterval(() => {
        const tableBody = document.querySelector("table.sortable tbody");
        if (tableBody && tableBody.children.length === 0) {
            requestFiles();
        }
    }, 5000);

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

    function updateTaskProgress(taskUpdate) {
        // taskUpdate: { fileName: string, progress: number }
        let taskCard = document.querySelector(`.task-card[data-filename="${taskUpdate.fileName}"]`);
        if (!taskCard) {
            taskCard = document.createElement("div");
            taskCard.classList.add("task-card");
            taskCard.setAttribute("data-filename", taskUpdate.fileName);

            let header = document.createElement("div");
            header.classList.add("task-header");
            let status = document.createElement("span");
            status.classList.add("task-status", "processing");
            status.textContent = "В обработке";
            let time = document.createElement("span");
            time.classList.add("task-time");
            let now = new Date();
            time.textContent = now.getHours() + ":" + now.getMinutes() + ", " + now.toLocaleDateString();
            header.appendChild(status);
            header.appendChild(time);
            taskCard.appendChild(header);

            let content = document.createElement("div");
            content.classList.add("task-content");
            let title = document.createElement("h4");
            title.innerHTML = `Загрузка файла: <span class="task-filename">${taskUpdate.fileName}</span>`;
            content.appendChild(title);
            let progressContainer = document.createElement("div");
            progressContainer.classList.add("task-progress");
            let progressBar = document.createElement("div");
            progressBar.classList.add("progress-bar");
            progressBar.style.width = "0%";
            progressContainer.appendChild(progressBar);
            content.appendChild(progressContainer);
            taskCard.appendChild(content);

            let tasksGrid = document.querySelector(".tasks-grid");
            if (tasksGrid) {
                tasksGrid.appendChild(taskCard);
            }
        }
        let progressBar = taskCard.querySelector(".progress-bar");
        progressBar.style.width = taskUpdate.progress + "%";
        if (taskUpdate.progress >= 100) {
            let status = taskCard.querySelector(".task-status");
            status.textContent = "Завершено";
            status.classList.remove("processing");
            status.classList.add("completed");
            // Здесь мы не удаляем карточку, оставляем ее для отображения
        }
    }

    const tableObserver = new MutationObserver((mutations, obs) => {
        const tableBody = document.querySelector("table.sortable tbody");
        if (tableBody) {
            requestFiles();
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
                .then(response => {
                    if (!response.ok) {
                        return response.text().then(text => { throw new Error("HTTP error " + response.status + ": " + text); });
                    }
                    return response.json();
                })
                .then(data => {
                    console.log("Файл успешно загружен", data);
                    fileInput.value = "";
                    if (connection) {
                        requestFiles();
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

window.addEventListener("load", function () {
    if (connection) {
        requestFiles();
    }
});
