const MAX_CHUNK_SIZE = 2 * 1024 * 1024; // 2 MB
const MAX_WINDOWS_PATH = 260;
const MAX_FILENAME_LENGTH = 120;
const GUID_LENGTH = 36; // Стандартный GUID
let activeUploads = 0;
let setupInProgress = false;
let previousActiveUploads = 0;
let currentStorageFiles = [];
let pendingUploadFile = null; // Для хранения выбранного файла при конфликте
let pendingAction = null; // Что делать: "overwrite" или "new-version"

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

function generateGuid() {
    return "xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx".replace(/[xy]/g, c => {
        const r = (Math.random() * 16) | 0;
        const v = c === "x" ? r : (r & 0x3) | 0x8;
        return v.toString(16);
    });
}

function getFileNameWithoutExtension(name) {
    const lastDotIndex = name.lastIndexOf(".");
    if (lastDotIndex === -1) return name;
    return name.substring(0, lastDotIndex);
}

function getFileExtension(name) {
    const lastDotIndex = name.lastIndexOf(".");
    if (lastDotIndex === -1) return "";
    return name.substring(lastDotIndex);
}

function buildFinalFileName(baseName, extension, guid) {
    return `${baseName}__${guid}${extension}`;
}

function isFinalPathTooLong(baseName, extension, guid) {
    if (!window.userStorageBasePath || !window.userIdFromClaims) {
        console.error("userStorageBasePath или userIdFromClaims не заданы!");
        return false;
    }
    const fullPath = `${window.userStorageBasePath}\\userData_${window.userIdFromClaims}\\${baseName}__${guid}${extension}`;
    console.log("[Upload] Вычисленный полный путь:", fullPath);
    return fullPath.length >= MAX_WINDOWS_PATH;
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

function fileExistsInStorage(fileName) {
    return currentStorageFiles.some(file => file.name.toLowerCase() === fileName.toLowerCase());
}

function showConflictModal(fileName) {
    const modal = document.getElementById("uploadConflictModal");
    const modalFilename = document.getElementById("modalFilename");
    modalFilename.textContent = `Файл "${fileName}" уже существует.`;
    modal.style.display = "block";
}

function hideConflictModal() {
    const modal = document.getElementById("uploadConflictModal");
    modal.style.display = "none";
}

async function uploadSelectedFile(file, action) {
    let baseName = getFileNameWithoutExtension(file.name);
    let extension = getFileExtension(file.name);
    let finalFileName = file.name;

    if (action === "new-version") {
        const guid = generateGuid();
        if (isFinalPathTooLong(baseName, extension, guid)) {
            alert("Путь слишком длинный. Сократите имя файла.");
            return;
        }
        finalFileName = buildFinalFileName(baseName, extension, guid);
    }

    const ok = await reserveFile(finalFileName, file.size);
    if (!ok) {
        alert("Недостаточно места для загрузки этого файла.");
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
        chunkForm.append("metadata.FileName", finalFileName);
        chunkForm.append("metadata.ChunkIndex", i);
        chunkForm.append("metadata.TotalChunks", totalChunks);

        try {
            const response = await fetch("/Content/profile_page?handler=UploadChunk", {
                method: "POST",
                headers: { "Accept": "application/json", "X-CSRF-TOKEN": document.querySelector('meta[name="csrf-token"]').getAttribute("content") },
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
        await releaseFile(finalFileName);
    }

    console.log("[Upload] Файл успешно загружен:", `${window.userStorageBasePath}\\userData_${window.userIdFromClaims}\\${finalFileName}`);

    await refreshStorageStatusAndTable();
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

            if (file.name.length > MAX_FILENAME_LENGTH) {
                alert(`Имя файла слишком длинное (${file.name.length} символов).`);
                fileInput.value = "";
                return;
            }

            if (fileExistsInStorage(file.name)) {
                pendingUploadFile = file;
                showConflictModal(file.name);
            } else {
                await uploadSelectedFile(file, "overwrite");
            }
        });

        document.getElementById("overwriteButton").addEventListener("click", async () => {
            hideConflictModal();
            await uploadSelectedFile(pendingUploadFile, "overwrite");
            pendingUploadFile = null;
        });

        document.getElementById("newVersionButton").addEventListener("click", async () => {
            hideConflictModal();
            await uploadSelectedFile(pendingUploadFile, "new-version");
            pendingUploadFile = null;
        });

        document.getElementById("cancelUploadButton").addEventListener("click", () => {
            hideConflictModal();
            pendingUploadFile = null;
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
        currentStorageFiles = files || [];
        updateFileTable(files);
        refreshStorageStatus();
    });

    connection.start().catch(err => console.error("Ошибка подключения SignalR:", err));
}
