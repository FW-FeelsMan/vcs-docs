//uploadFileObserver.js скрипт для загрузки файлов в личное хранилище
const MAX_CHUNK_SIZE = 2 * 1024 * 1024;
const MAX_FILENAME_LENGTH = 120;

const uploadAbortControllers = new Map();
const cancelledUploads = new Set();
let activeUploads = 0;

function getFileNameParts(name) {
    const lastDot = name.lastIndexOf(".");
    return lastDot === -1 ? [name, ""] : [name.slice(0, lastDot), name.slice(lastDot)];
}

function generateGuid() {
    return "xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx".replace(/[xy]/g, c => {
        const r = Math.random() * 16 | 0;
        const v = c === "x" ? r : (r & 0x3) | 0x8;
        return v.toString(16);
    });
}

async function reserveFile(fileName, fileSize) {
    const fd = new FormData();
    fd.append("fileName", fileName);
    fd.append("fileSize", fileSize);
    const token = document.querySelector('meta[name="csrf-token"]').content;
    try {
        const res = await fetch("/Content/profile_page?handler=TryReserve", {
            method: "POST",
            headers: { "X-CSRF-TOKEN": token },
            body: fd
        });
        return await res.json();
    } catch (err) {
        return { success: false, error: err.message };
    }
}

async function releaseFile(fileName) {
    const fd = new FormData();
    fd.append("fileName", fileName);
    const token = document.querySelector('meta[name="csrf-token"]').content;
    try {
        await fetch("/Content/profile_page?handler=ReleaseFile", {
            method: "POST",
            headers: { "X-CSRF-TOKEN": token },
            body: fd
        });
    } catch {
       
    }
}

async function uploadSelectedFile(file, action = "overwrite") {
    const [baseName, extension] = getFileNameParts(file.name);
    let finalName = file.name;

    if (action === "new-version") {
        finalName = `${baseName}__${generateGuid()}${extension}`;
    }

    const reserveResult = await reserveFile(finalName, file.size);
    if (!reserveResult.success) {
        alert(reserveResult.error || "Ошибка при резервировании файла.");
        return;
    }

    const key = finalName.toLowerCase();
    cancelledUploads.delete(key); // Убираем из отменённых

    // ВОТ ЭТО — КЛЮЧЕВОЙ МОМЕНТ
    if (window.currentlyUploadingFiles) {
        window.currentlyUploadingFiles.set(key, {
            uploaded: 0,
            total: file.size
        });
    }

    const tableBody = document.querySelector("table.sortable tbody");
    if (tableBody && typeof renderUploadingFiles === "function") {
        renderUploadingFiles(tableBody); // Отрисовать новую строку сразу
    }

    activeUploads++;
    const totalChunks = Math.ceil(file.size / MAX_CHUNK_SIZE);
    const controller = new AbortController();
    uploadAbortControllers.set(key, controller);

    try {
        for (let i = 0; i < totalChunks; i++) {
            if (cancelledUploads.has(key)) throw new Error("Загрузка отменена");

            const chunk = file.slice(i * MAX_CHUNK_SIZE, (i + 1) * MAX_CHUNK_SIZE);
            const res = await fetch("/Content/profile_page?handler=UploadChunk", {
                method: "POST",
                headers: {
                    "Accept": "application/json",
                    "X-CSRF-TOKEN": document.querySelector('meta[name="csrf-token"]').content,
                    "X-File-Name": encodeURIComponent(finalName),
                    "X-Chunk-Index": i.toString(),
                    "X-Total-Chunks": totalChunks.toString()
                },
                body: chunk,
                signal: controller.signal
            });

            const result = await res.json();
            if (!result.success) throw new Error(result.error);

            if (window.currentlyUploadingFiles.has(key)) {
                window.currentlyUploadingFiles.set(key, {
                    uploaded: (i + 1) * MAX_CHUNK_SIZE,
                    total: file.size
                });
            }
        }
    } catch (err) {
        await releaseFile(finalName);
        console.warn(`[Upload] Загрузка файла "${finalName}" была отменена или прервана: ${err.message}`);
    } finally {
        activeUploads--;
        cancelledUploads.delete(key);
        uploadAbortControllers.delete(key);
        if (window.currentlyUploadingFiles) window.currentlyUploadingFiles.delete(key);

        const row = document.getElementById(`uploading-${key}`);
        if (row) row.remove();

        if (typeof requestFiles === "function") requestFiles();
        if (typeof refreshStorageStatus === "function") refreshStorageStatus();
    }
}

window.cancelUploadingFile = async (fileName) => {
    const key = fileName.toLowerCase();
    cancelledUploads.add(key);

    const controller = uploadAbortControllers.get(key);
    if (controller) controller.abort();

    try {
        const fd = new FormData();
        fd.append("fileName", fileName);
        await fetch("/Content/profile_page?handler=CancelUpload", {
            method: "POST",
            headers: {
                "X-CSRF-TOKEN": document.querySelector('meta[name="csrf-token"]').content
            },
            body: fd
        });
    } catch (err) {
        console.warn(`[Cancel] Не удалось отменить загрузку на сервере: ${err.message}`);
    }
};

function setupUploadBindings() {
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
        const lowerName = file.name.toLowerCase();

        if (window.currentlyUploadingFiles?.has(lowerName)) {
            showConflictModal(file.name, "uploading", {
                onReplace: async () => {
                    const lowerName = file.name.toLowerCase();
                    if (window.cancelUploadingFile) {
                        await window.cancelUploadingFile(file.name); 
                    }
                    setTimeout(() => uploadSelectedFile(file), 300);
                },

                onCancel: () => console.log("Отмена загрузки"),
            });
            fileInput.value = "";
            return;
        }

        const exists = currentStorageFiles?.some(f => f.name.toLowerCase() === lowerName);

        if (exists) {
            showConflictModal(file.name, "exists", {
                onReplace: () => uploadSelectedFile(file, "overwrite"),
                onNewVersion: () => uploadSelectedFile(file, "new-version"),
                onCancel: () => console.log("Отмена загрузки"),
            });
        } else {
            await uploadSelectedFile(file);
        }

        fileInput.value = "";
    });
}

if (typeof userIsAuthenticated !== "undefined" && userIsAuthenticated) {
    document.addEventListener("DOMContentLoaded", () => {
        setupUploadBindings();
        const observer = new MutationObserver(setupUploadBindings);
        observer.observe(document.body, { childList: true, subtree: true });
    });

    window.addEventListener("beforeunload", (e) => {
        if (activeUploads > 0) {
            e.preventDefault();
            e.returnValue = "";
        }
    });
}
