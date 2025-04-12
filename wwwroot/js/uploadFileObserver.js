const MAX_CHUNK_SIZE = 2 * 1024 * 1024;
console.log("Скрипт загружен");
if (typeof userIsAuthenticated !== "undefined" && userIsAuthenticated === true) {
    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", setupUpload);
    } else {
        setupUpload();
    }

    const uploadObserver = new MutationObserver(setupUpload);
    uploadObserver.observe(document.body, { childList: true, subtree: true });

    function setupUpload() {
        const uploadButton = document.getElementById("uploadFileButton");
        const fileInput = document.getElementById("hiddenFileInput");

        if (!uploadButton || !fileInput || uploadButton.dataset.initialized === "true") return;

        uploadButton.dataset.initialized = "true";

        uploadButton.addEventListener("click", () => {
            console.log("Кнопка 'Загрузить файл' нажата");
            fileInput.click();
        });

        fileInput.addEventListener("change", async () => {
            const file = fileInput.files[0];
            if (!file) return;

            const formData = new FormData();
            formData.append("fileName", file.name);
            formData.append("fileSize", file.size);

            const token = document.querySelector('meta[name="csrf-token"]').getAttribute("content");

            const reserveResponse = await fetch("/Content/profile_page?handler=TryReserve", {
                method: "POST",
                headers: {
                    "Accept": "application/json",
                    "X-CSRF-TOKEN": token
                },
                body: formData
            });

            const reserveResult = await reserveResponse.json();
            if (!reserveResult.success) {
                alert(reserveResult.error);
                fileInput.value = "";
                return;
            }

            const totalChunks = Math.ceil(file.size / MAX_CHUNK_SIZE);
            for (let i = 0; i < totalChunks; i++) {
                const chunk = file.slice(i * MAX_CHUNK_SIZE, (i + 1) * MAX_CHUNK_SIZE);
                const chunkForm = new FormData();
                chunkForm.append("chunk", chunk);
                chunkForm.append("metadata.FileName", file.name);
                chunkForm.append("metadata.ChunkIndex", i);
                chunkForm.append("metadata.TotalChunks", totalChunks);

                try {
                    const response = await fetch("/Content/profile_page?handler=UploadChunk", {
                        method: "POST",
                        headers: {
                            "Accept": "application/json",
                            "X-CSRF-TOKEN": token
                        },
                        body: chunkForm
                    });

                    const result = await response.json();
                    if (!result.success) {
                        console.error("Ошибка при загрузке чанка:", result.error);
                        break;
                    }
                } catch (err) {
                    console.error("Ошибка при отправке чанка:", err);
                    break;
                }
            }

            fileInput.value = "";
        });
    }

    function createTaskCard(fileName, taskId) {
        const taskContainer = document.createElement("div");
        taskContainer.classList.add("task-card");
        taskContainer.setAttribute("data-filename", fileName);
        taskContainer.innerHTML = `
            <span>${fileName}</span>
            <button class="cancel-btn">Отменить</button>
        `;
        taskContainer.querySelector(".cancel-btn").addEventListener("click", async () => {
            const token = document.querySelector('meta[name="csrf-token"]').getAttribute("content");
            const response = await fetch("/Content/profile_page?handler=CancelUpload", {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                    "X-CSRF-TOKEN": token
                },
                body: JSON.stringify({ taskId })
            });
            const result = await response.json();
            if (result.success) {
                alert(`Загрузка "${fileName}" отменена`);
                taskContainer.remove();
            } else {
                alert("Не удалось отменить задачу");
            }
        });
        const tasksContainer = document.querySelector(".tasks-grid");
        if (tasksContainer) tasksContainer.appendChild(taskContainer);
    }

    const connection = new signalR.HubConnectionBuilder()
        .withUrl("/userStorageHub")
        .configureLogging(signalR.LogLevel.Information)
        .build();

    connection.on("NewTaskStarted", ({ fileName, taskId }) => {
        createTaskCard(fileName, taskId);
    });

    connection.start().catch(err => console.error("Ошибка подключения SignalR:", err));
}