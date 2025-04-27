// === Константы ===
const MAX_CHUNK_SIZE = 2 * 1024 * 1024;
const MAX_WINDOWS_PATH = 260;
const MAX_FILENAME_LENGTH = 120;
const GUID_LENGTH = 36;

// === Переменные состояния ===
let activeUploads = 0;
let setupInProgress = false;
let currentStorageFiles = [];
let pendingUploadFile = null;
const currentlyUploadingFiles = new Set();

// === Утилиты ===
function isFileUploading(fileName) {
    return currentlyUploadingFiles.has(fileName.toLowerCase());
}

function markFileAsUploading(fileName) {
    currentlyUploadingFiles.add(fileName.toLowerCase());
}

function unmarkFileAsUploading(fileName) {
    currentlyUploadingFiles.delete(fileName.toLowerCase());
}

async function refreshStorageStatus() {
    const storageCounter = document.getElementById("storageCounter");
    if (!storageCounter) return;
    try {
        const res = await fetch("/Content/profile_page?handler=StorageStatus");
        if (!res.ok) return;
        const json = await res.json();
        if (json.success) {
            storageCounter.textContent = `Загружается: ${json.reservedMb ?? 0} МБ    Свободно: ${json.freeMb} МБ / 10240 МБ`;
        }
    } catch (err) {
        console.error("Ошибка получения статуса хранилища:", err);
    }
}

function generateGuid() {
    return "xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx".replace(/[xy]/g, c => {
        const r = Math.random() * 16 | 0;
        const v = c === "x" ? r : (r & 0x3) | 0x8;
        return v.toString(16);
    });
}

function getFileNameParts(name) {
    const lastDot = name.lastIndexOf(".");
    return lastDot === -1 ? [name, ""] : [name.slice(0, lastDot), name.slice(lastDot)];
}

function isFinalPathTooLong(baseName, extension, guid) {
    if (!window.userStorageBasePath || !window.userIdFromClaims) return false;
    const fullPath = `${window.userStorageBasePath}\\userData_${window.userIdFromClaims}\\${baseName}__${guid}${extension}`;
    return fullPath.length >= MAX_WINDOWS_PATH;
}

function fileExistsInStorage(fileName) {
    return currentStorageFiles.some(f => f.name.toLowerCase() === fileName.toLowerCase());
}

function showUploadWarning() {
    if (!document.getElementById("upload-warning")) {
        const div = document.createElement("div");
        div.id = "upload-warning";
        div.textContent = "Загрузка файлов в процессе. Закрыв страницу вы потеряете прогресс.";
        Object.assign(div.style, {
            position: "fixed", bottom: "15px", right: "15px", backgroundColor: "#ffc107",
            padding: "10px 20px", borderRadius: "6px", boxShadow: "0 2px 6px rgba(0,0,0,0.2)",
            fontWeight: "bold", zIndex: 9999
        });
        document.body.appendChild(div);
    }
}

function hideUploadWarning() {
    const div = document.getElementById("upload-warning");
    if (div) div.remove();
}

function showConflictModal(fileName) {
    const modal = document.getElementById("uploadConflictModal");
    const filename = document.getElementById("modalFilename");
    if (modal && filename) {
        filename.textContent = `Файл \"${fileName}\" уже существует.`;
        modal.style.display = "block";
    }
}

function hideConflictModal() {
    const modal = document.getElementById("uploadConflictModal");
    if (modal) modal.style.display = "none";
}

function showRestartUploadModal(fileName, onConfirm) {
    if (confirm(`Файл \"${fileName}\" уже загружается. Перезапустить загрузку?`)) {
        onConfirm();
    }
}

async function reserveFile(fileName, fileSize) {
    const fd = new FormData();
    fd.append("fileName", fileName);
    fd.append("fileSize", fileSize);
    const token = document.querySelector('meta[name="csrf-token"]').content;
    const res = await fetch("/Content/profile_page?handler=TryReserve", {
        method: "POST", headers: { "X-CSRF-TOKEN": token }, body: fd
    });
    return res.ok && (await res.json()).success;
}

async function releaseFile(fileName) {
    const fd = new FormData();
    fd.append("fileName", fileName);
    const token = document.querySelector('meta[name="csrf-token"]').content;
    await fetch("/Content/profile_page?handler=ReleaseFile", {
        method: "POST", headers: { "X-CSRF-TOKEN": token }, body: fd
    });
}

// === Основная логика загрузки ===
async function uploadSelectedFile(file, action) {
    const [baseName, extension] = getFileNameParts(file.name);
    let finalName = file.name;

    if (action === "new-version") {
        const guid = generateGuid();
        if (isFinalPathTooLong(baseName, extension, guid)) {
            alert("Путь слишком длинный. Сократите имя файла.");
            return;
        }
        finalName = `${baseName}__${guid}${extension}`;
    }

    if (isFileUploading(finalName)) {
        console.warn(`[Upload] Файл \"${finalName}\" уже загружается.`);
        alert(`Файл \"${finalName}\" уже загружается.`);
        return;
    }

    if (!await reserveFile(finalName, file.size)) {
        alert("Недостаточно места для загрузки.");
        return;
    }

    markFileAsUploading(finalName);
    await refreshStorageStatus();

    activeUploads++;
    showUploadWarning();

    const totalChunks = Math.ceil(file.size / MAX_CHUNK_SIZE);
    try {
        for (let i = 0; i < totalChunks; i++) {
            const chunk = file.slice(i * MAX_CHUNK_SIZE, (i + 1) * MAX_CHUNK_SIZE);
            const form = new FormData();
            form.append("chunk", chunk);
            form.append("metadata.FileName", finalName);
            form.append("metadata.ChunkIndex", i);
            form.append("metadata.TotalChunks", totalChunks);

            const res = await fetch("/Content/profile_page?handler=UploadChunk", {
                method: "POST",
                headers: { "Accept": "application/json", "X-CSRF-TOKEN": document.querySelector('meta[name="csrf-token"]').content },
                body: form
            });

            const result = await res.json();
            if (!result.success) {
                console.error("Ошибка при загрузке чанка", result.error);
                throw new Error(result.error);
            }
        }

        console.log(`[Upload] Файл успешно загружен: ${finalName}`);

    } catch (err) {
        console.error("Ошибка загрузки файла:", err);
        alert(`Ошибка загрузки файла \"${finalName}\": ${err.message}`);

    } finally {
        activeUploads--;
        unmarkFileAsUploading(finalName);

        if (activeUploads <= 0) {
            hideUploadWarning();
            await releaseFile(finalName);
            if (typeof connection !== "undefined" && connection.state === signalR.HubConnectionState.Connected) {
                console.log("[Upload] Перезагружаем таблицу через SignalR...");
                connection.invoke("RequestCurrentFiles").catch(err => console.error("Ошибка запроса файлов:", err));
            }
        }

        await refreshStorageStatus();
    }
}

// === Инициализация ===
async function setupUpload() {
    if (setupInProgress) return;
    setupInProgress = true;

    try {
        await refreshStorageStatus();

        const uploadButton = document.getElementById("uploadFileButton");
        const fileInput = document.getElementById("hiddenFileInput");

        if (!uploadButton || !fileInput || uploadButton.dataset.initialized) return;
        uploadButton.dataset.initialized = "true";

        uploadButton.addEventListener("click", () => fileInput.click());

        fileInput.addEventListener("change", async () => {
            const file = fileInput.files[0];
            if (!file) return;

            if (file.name.length > MAX_FILENAME_LENGTH) {
                alert(`Имя файла слишком длинное: ${file.name.length}`);
                fileInput.value = "";
                return;
            }

            if (isFileUploading(file.name)) {
                showRestartUploadModal(file.name, async () => {
                    unmarkFileAsUploading(file.name);
                    await uploadSelectedFile(file, "overwrite");
                });
                return;
            }

            if (fileExistsInStorage(file.name)) {
                pendingUploadFile = file;
                showConflictModal(file.name);
            } else {
                await uploadSelectedFile(file, "overwrite");
            }
            fileInput.value = "";
        });

        document.getElementById("overwriteButton").addEventListener("click", async () => {
            hideConflictModal();
            if (pendingUploadFile) {
                await uploadSelectedFile(pendingUploadFile, "overwrite");
                pendingUploadFile = null;
            }
            document.getElementById("hiddenFileInput").value = "";
        });

        document.getElementById("newVersionButton").addEventListener("click", async () => {
            hideConflictModal();
            if (pendingUploadFile) {
                await uploadSelectedFile(pendingUploadFile, "new-version");
                pendingUploadFile = null;
            }
            document.getElementById("hiddenFileInput").value = "";
        });

        document.getElementById("cancelUploadButton").addEventListener("click", () => {
            hideConflictModal();
            pendingUploadFile = null;
            document.getElementById("hiddenFileInput").value = "";
        });

    } finally {
        setupInProgress = false;
    }
}

if (typeof userIsAuthenticated !== "undefined" && userIsAuthenticated) {
    document.addEventListener("DOMContentLoaded", setupUpload);

    const observer = new MutationObserver(setupUpload);
    observer.observe(document.body, { childList: true, subtree: true });

    window.addEventListener("beforeunload", e => {
        if (activeUploads > 0) {
            e.preventDefault();
            e.returnValue = "";
        }
    });
}
