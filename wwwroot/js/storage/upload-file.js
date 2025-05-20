const MAX_CHUNK_SIZE = 10 * 1024 * 1024;
const MAX_FILENAME_LENGTH = 120;
window.currentlyUploadingFiles = window.currentlyUploadingFiles || new Map();

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

async function uploadSelectedFile(file) {
	console.log("Начало загрузки файла:", file.name);

	isUploading = true;

	const reserveResult = await reserveFile(file.name, file.size);
	if (!reserveResult.success) {
		alert(reserveResult.error || "Превышен лимит хранилища.\n\nС учетом текущих загрузок, добавление новых файлов приведет к переполнению Вашего личного хранилища.\n\nОсвободите место или дождитесь/отмените активные загрузки.");
		return;
	}
	if (!window.currentlyUploadingFiles || typeof window.currentlyUploadingFiles.set !== 'function') {
		window.currentlyUploadingFiles = new Map();
	}

	const finalName = reserveResult.finalFileName;
	const key = getFileKey(finalName);
	console.log("Финальное имя файла:", finalName);
	console.log("Ключ файла:", key);
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
	console.log("Добавлено в Map:", key, window.currentlyUploadingFiles.get(key));
	console.log("Текущие загрузки после добавления:", Array.from(window.currentlyUploadingFiles.entries()));

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

			// Для больших файлов периодически обновляем статус активности
			// Делаем это каждые 5 чанков для файлов больше 100 МБ
			if (file.size > 100 * 1024 * 1024 && i % 5 === 0 && i > 0) {
				await fetch("/Content/profile_page?handler=TouchUpload", {
					method: "POST",
					headers: {
						"Accept": "application/json",
						"X-CSRF-TOKEN": document.querySelector('meta[name="csrf-token"]').content,
						"X-File-Name": encodeURIComponent(finalName)
					}
				});
			}

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

		uploadSucceeded = true;

	} catch (err) {
		await releaseFile(finalName);
	} finally {
		isUploading = false;
		activeUploads--;
		cancelledUploads.delete(key);
		uploadAbortControllers.delete(key);
		window.currentlyUploadingFiles.delete(key);

		if (uploadSucceeded && typeof requestFiles === "function") {
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

		// Отладка: проверяем инициализацию Map
		console.log("Тип currentlyUploadingFiles:", typeof window.currentlyUploadingFiles);
		console.log("Является ли Map:", window.currentlyUploadingFiles instanceof Map);

		// Если не Map, пересоздаем
		if (!(window.currentlyUploadingFiles instanceof Map)) {
			console.warn("currentlyUploadingFiles не является Map, пересоздаем");
			window.currentlyUploadingFiles = new Map();
		}

		// Отладка: выводим текущие загрузки
		console.log("Текущие загрузки:", Array.from(window.currentlyUploadingFiles.entries()));

		for (const file of files) {
			if (file.name.length > MAX_FILENAME_LENGTH) {
				alert(`Имя файла слишком длинное: ${file.name.length}`);
				continue;
			}

			const fileKey = getFileKey(file.name);
			console.log("Проверяем файл:", file.name);
			console.log("Ключ файла:", fileKey);

			// Отладка: проверяем каждую текущую загрузку
			let foundUploading = false;
			window.currentlyUploadingFiles.forEach((entry, key) => {
				console.log("Сравниваем с:", key);
				console.log("Данные загрузки:", entry);

				// Проверяем наличие поля name
				if (entry.name) {
					const entryKey = getFileKey(entry.name);
					console.log("Ключ текущей загрузки:", entryKey);
					if (entryKey === fileKey) {
						foundUploading = true;
						console.log("СОВПАДЕНИЕ НАЙДЕНО!");
					}
				} else {
					console.warn("В записи загрузки отсутствует поле name:", entry);
				}
			});

			// Используем результат нашей проверки
			if (foundUploading) {
				console.warn(`Файл "${file.name}" уже загружается.`);
				alert(`Файл "${file.name}" уже загружается.`);
				continue;
			}

			// Альтернативная проверка (оригинальная)
			const isUploading = Array.from(window.currentlyUploadingFiles.values())
				.some(entry => {
					// Отладка: проверяем каждую запись
					console.log("Проверка записи:", entry);
					if (!entry.name) {
						console.warn("Запись не содержит имя файла:", entry);
						return false;
					}
					const entryKey = getFileKey(entry.name);
					const matches = entryKey === fileKey;
					console.log(`Сравнение ${entryKey} === ${fileKey}: ${matches}`);
					return matches;
				});

			if (isUploading) {
				console.warn(`Файл "${file.name}" уже загружается (проверка 2).`);
				alert(`Файл "${file.name}" уже загружается.`);
				continue;
			}

			const conflictFile = window.currentStorageFiles.find(f =>
				getFileKey(f.baseName) === fileKey
			);

			if (conflictFile) {
				showConflictModal(file.name, "exists", {
					onReplace: async () => {
						const fileToDelete = `${conflictFile.baseName}.v${conflictFile.currentVersion}`;
						await deleteFile(fileToDelete);
						setTimeout(() => uploadSelectedFile(file), 300);
					},
					onNewVersion: async () => {
						setTimeout(() => uploadSelectedFile(file), 300);
					},
					onCancel: () => { }
				});
				continue;
			}

			await uploadSelectedFile(file);
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