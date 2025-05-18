// upload-file.js (только загрузка, без UI вмешательств)
const MAX_CHUNK_SIZE = 2 * 1024 * 1024;
const MAX_FILENAME_LENGTH = 120;

const uploadAbortControllers = new Map();
const cancelledUploads = new Set();
let activeUploads = 0;

function getFileNameParts(name) {
	const lastDot = name.lastIndexOf(".");
	return lastDot === -1 ? [name, ""] : [name.slice(0, lastDot), name.slice(lastDot)];
}

function getFileKey(name) {
	return name.trim().toLowerCase();
}

function generateNextVersion(existingVersions) {
	if (!existingVersions.length) return "v1.0";

	const versionNumbers = existingVersions
		.map(v => parseFloat(v.replace(/[^\d.]/g, "")))
		.filter(n => !isNaN(n));

	const maxVersion = versionNumbers.length ? Math.max(...versionNumbers) : 0;
	const nextVersion = (Math.floor(maxVersion) + 1) + ".0";
	return "v" + nextVersion;
}

function findExistingVersions(baseName) {
	return currentStorageFiles
		.filter(f => f.baseName.toLowerCase() === baseName.toLowerCase())
		.map(f => f.currentVersion)
		.filter(v => v);
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
	} catch { }
}

async function uploadSelectedFile(file, action = "overwrite") {
    isUploading = true;

    const [baseName, extension] = getFileNameParts(file.name);
    let finalName = file.name;

    if (action === "new-version") {
        const versions = findExistingVersions(baseName);
        const nextVersion = generateNextVersion(versions);
        finalName = `${baseName}_${nextVersion}${extension}`;
    }

    const reserveResult = await reserveFile(finalName, file.size);
    if (!reserveResult.success) {
        alert(reserveResult.error || "Ошибка при резервировании файла.");
        return;
    }

    const key = getFileKey(finalName);
    cancelledUploads.delete(key);

    const fileId = `upload-${key.replace(/\W+/g, '-')}`;

    if (typeof window.addFileToPopup === 'function') {
        window.addFileToPopup(finalName, file.size, fileId);
    }

    window.currentlyUploadingFiles.set(key, {
        name: finalName,
        uploaded: 0,
        total: file.size,
        fileId
    });

    activeUploads++;
    const totalChunks = Math.ceil(file.size / MAX_CHUNK_SIZE);
    const controller = new AbortController();
    uploadAbortControllers.set(key, controller);

    let uploadedBytes = 0;
    let uploadSucceeded = false;

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

            uploadedBytes += chunk.size;

            if (window.currentlyUploadingFiles.has(key)) {
                const fileInfo = window.currentlyUploadingFiles.get(key);
                fileInfo.uploaded = uploadedBytes;
                window.currentlyUploadingFiles.set(key, fileInfo);

                if (typeof window.updateFileProgress === 'function' && fileInfo.fileId) {
                    window.updateFileProgress(fileInfo.fileId, uploadedBytes, file.size);
                }
            }
        }

        const fileInfo = window.currentlyUploadingFiles.get(key);
        if (typeof window.completeFileUpload === 'function' && fileInfo?.fileId) {
            window.completeFileUpload(fileInfo.fileId);
        }

        console.log(`[Upload] Загрузка файла "${finalName}" завершена успешно`);

        // ✅ Только после успешной загрузки
        uploadSucceeded = true;

    } catch (err) {
        await releaseFile(finalName);
        console.warn(`[Upload] Ошибка загрузки "${finalName}": ${err.message}`);
    } finally {
        isUploading = false;
        activeUploads--;
        cancelledUploads.delete(key);
        uploadAbortControllers.delete(key);
        window.currentlyUploadingFiles.delete(key);

        if (uploadSucceeded && typeof requestFiles === "function") {
            //await releaseFile(finalName);
            setTimeout(() => requestFiles(), 400);
        }

        if (typeof refreshStorageStatus === "function") {
            refreshStorageStatus();
        }

        if (activeUploads === 0) {
            window.uploadTotalFiles = 0;
            if (typeof window.updatePopupTitle === 'function') {
                window.updatePopupTitle();
            }
        }
    }
}
function setupUploadBindings() {
	const uploadButton = document.getElementById("uploadFileButton");
	const fileInput = document.getElementById("hiddenFileInput");

	if (!uploadButton || !fileInput || uploadButton.dataset.initialized) return;

	uploadButton.dataset.initialized = "true";
	uploadButton.addEventListener("click", () => fileInput.click());

	fileInput.addEventListener("change", async () => {
		const files = Array.from(fileInput.files || []);
		if (files.length === 0) return;

		window.uploadTotalFiles = files.length;

		for (const file of files) {
			if (file.name.length > MAX_FILENAME_LENGTH) {
				alert(`Имя файла слишком длинное: ${file.name.length}`);
				continue;
			}

			const key = getFileKey(file.name);

			if (window.currentlyUploadingFiles?.has(key)) {
				showConflictModal(file.name, "uploading", {
					onReplace: async () => {
						await window.cancelUploadingFile(file.name);
						setTimeout(() => uploadSelectedFile(file), 300);
					},
					onCancel: () => console.log("Отмена загрузки"),
				});
				continue;
			}

			const incomingBaseName = getFileNameParts(file.name)[0].toLowerCase();
			const exists = currentStorageFiles?.some(f => f?.baseName?.toLowerCase() === incomingBaseName);

			if (exists) {
				showConflictModal(file.name, "exists", {
					onReplace: () => uploadSelectedFile(file, "overwrite"),
					onNewVersion: () => uploadSelectedFile(file, "new-version"),
					onCancel: () => console.log("Отмена загрузки"),
				});
			} else {
				await uploadSelectedFile(file);
			}
		}

		fileInput.value = "";
	});
}

window.initUploadFile = function () {
	setupUploadBindings();
};

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
