const MAX_CHUNK_SIZE = 2 * 1024 * 1024;
let activeUploads = 0;
let setupInProgress = false;
let previousActiveUploads = 0;

async function refreshStorageStatusAndTable() {
    await refreshStorageStatus();
}

async function refreshStorageStatus() {
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
            const loadingText = json.reservedMb > 0 ? `Загружается: ${json.reservedMb} МБ` : `Загружается: 0 МБ`;
            const freeText = `Свободно: ${json.freeMb} МБ / 10240 МБ`;
            storageCounter.textContent = `${loadingText}    ${freeText}`;
        }
    } catch (err) {
        console.error("Ошибка при получении статуса хранилища:", err);
    }
}

async function reserveFile(fileName, fileSize) {
    const fd = new FormData();
    fd.append("fileName", fileName);
    fd.append("fileSize", fileSize);
    const token = document.querySelector('meta[name="csrf-token"]').getAttribute("content");
    try {
        const res = await fetch("/Content/profile_page?handler=TryReserve", {
            method: "POST",
            headers: { "X-CSRF-TOKEN": token },
            body: fd
        });
        if (!res.ok) return false;
        const json = await res.json();
        return json.success;
    } catch (err) {
        console.error("Ошибка при резервировании места:", err);
        return false;
    }
}

async function releaseFile(fileName) {
    const fd = new FormData();
    fd.append("fileName", fileName);
    const token = document.querySelector('meta[name="csrf-token"]').getAttribute("content");
    try {
        await fetch("/Content/profile_page?handler=ReleaseFile", {
            method: "POST",
            headers: { "X-CSRF-TOKEN": token },
            body: fd
        });
    } catch (err) {
        console.error("Ошибка при освобождении места:", err);
    }
}

function showUploadWarning() {
    let notice = document.getElementById("upload-warning");
    if (!notice) {
        notice = document.createElement("div");
        notice.id = "upload-warning";
        notice.style.position = "fixed";
        notice.style.bottom = "15px";
        notice.style.right = "15px";
        notice.style.backgroundColor = "#ffc107";
        notice.style.padding = "10px 20px";
        notice.style.borderRadius = "6px";
        notice.style.boxShadow = "0 2px 6px rgba(0,0,0,0.2)";
        notice.style.zIndex = "9999";
        notice.style.fontWeight = "bold";
        notice.textContent = "Загрузка файлов в процессе. Закрыв страницу вы потеряете прогресс.";
        document.body.appendChild(notice);
    }
}

function hideUploadWarning() {
    const notice = document.getElementById("upload-warning");
    if (notice) notice.remove();
}

async function setupUpload() {
    if (setupInProgress) return;
    setupInProgress = true;
    try {
        await refreshStorageStatus();

        const uploadButton = document.getElementById("uploadFileButton");
        const fileInput = document.getElementById("hiddenFileInput");

        if (!uploadButton || !fileInput || uploadButton.dataset.initialized === "true") return;

        uploadButton.dataset.initialized = "true";

        uploadButton.addEventListener("click", () => fileInput.click());

        fileInput.addEventListener("change", async () => {
            const file = fileInput.files[0];
            if (!file) return;

            const ok = await reserveFile(file.name, file.size);
            if (!ok) {
                alert("Недостаточно места для загрузки этого файла.");
                fileInput.value = "";
                return;
            }

            await refreshStorageStatus();
            const totalChunks = Math.ceil(file.size / MAX_CHUNK_SIZE);

            activeUploads++;
            if (activeUploads > previousActiveUploads) {
                previousActiveUploads = activeUploads;
                await refreshStorageStatus();
            }

            showUploadWarning();

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
                            "X-CSRF-TOKEN": document.querySelector('meta[name="csrf-token"]').getAttribute("content")
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

            activeUploads--;
            previousActiveUploads = activeUploads;

            if (activeUploads <= 0) {
                hideUploadWarning();
                await releaseFile(file.name);
            }

            fileInput.value = "";

            await refreshStorageStatusAndTable();
        });
    } finally {
        setupInProgress = false;
    }
}

if (typeof userIsAuthenticated !== "undefined" && userIsAuthenticated === true) {
    document.addEventListener("DOMContentLoaded", setupUpload);

    const uploadObserver = new MutationObserver(setupUpload);
    uploadObserver.observe(document.body, { childList: true, subtree: true });

    window.addEventListener("beforeunload", e => {
        if (activeUploads > 0) {
            e.preventDefault();
            e.returnValue = "";
        }
    });

    const connection = new signalR.HubConnectionBuilder()
        .withUrl("/userStorageHub")
        .configureLogging(signalR.LogLevel.Information)
        .build();

    connection.on("ReceiveStorageUpdate", (files) => {
        updateFileTable(files);
        refreshStorageStatus();
    });

    connection.start().catch(err => console.error("Ошибка подключения SignalR:", err));
}