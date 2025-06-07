let uploadFileInitialized = false;
let isUploadInProgress = false;

window.initUploadFile = function () {
	if (uploadFileInitialized) return;
	uploadFileInitialized = true;
	console.log("initUploadFile: скрипт загружен");

	const uploadBtn = document.getElementById('uploadFileButton');
	const fileInput = document.getElementById('hiddenFileInput');

	if (!uploadBtn || !fileInput) {
		console.warn('Элементы загрузки не найдены');
		return;
	}

	uploadBtn.addEventListener('click', () => {
		fileInput.value = null;
		fileInput.click();
	});

	fileInput.addEventListener('change', async (event) => {
		const file = event.target.files[0];
		if (!file) return console.warn("Файл не выбран");

		console.log(`Файл выбран: ${file.name}, размер: ${file.size}`);

		let realHash;
		const hashTask = {
			taskKey: `hash_${file.name}_${file.size}_${Date.now()}`,
			title: file.name,
			type: "hashing",
			statusClass: "waiting",
			statusText: "Подготовка к загрузке (хеширование)",
			cancelable: false
		};
		window.taskManager.addTask(hashTask);

		try {
			if (file.size <= 100 * 1024 * 1024) {
				realHash = await computeSHA256(file);
				hashTask.statusClass = "done";
				hashTask.statusText = "Хеш готов";
				window.taskManager.addTask(hashTask);
				console.log("SHA-256 хэш:", realHash);
			} else {
				console.log("Используется SparkMD5 для больших файлов");
				realHash = await computeSparkMD5Hash(file);
				hashTask.statusClass = "done";
				hashTask.statusText = "Хеш готов";
				window.taskManager.addTask(hashTask);
				console.log("MD5 хэш:", realHash);
			}
		} catch (error) {
			console.error("Ошибка вычисления хеша:", error);
			hashTask.statusClass = "failed";
			hashTask.statusText = "Ошибка хеширования";
			window.taskManager.addTask(hashTask);
			alert("Не удалось вычислить хеш файла. Попробуйте еще раз.");
			return;
		}

		try {
			const res = await fetch(`/api/upload/upload-status?fileHash=${realHash}`);
			if (!res.ok) throw new Error(`HTTP ${res.status}`);
			const status = await res.json();

			if (status.found) {
				showResumeModal(file.name,
					() => startUpload(file, realHash, null, status.sessionId, new Set(status.uploaded)),
					() => checkConflictThenUpload(file, realHash)
				);
				return;
			}
		} catch (e) {
			console.warn("Проверка предыдущей сессии загрузки не удалась:", e);
		}

		checkConflictThenUpload(file, realHash);
	});
};

function showResumeModal(fileName, onContinue, onRestart) {
	const modal = document.getElementById("upload-resume-modal");
	const message = modal.querySelector("#resume-modal-message");
	const confirmBtn = modal.querySelector("#resume-confirm");
	const cancelBtn = modal.querySelector("#resume-cancel");

	if (!modal || !message || !confirmBtn || !cancelBtn) {
		const answer = confirm("Файл уже загружался. Продолжить загрузку?");
		answer ? onContinue?.() : onRestart?.();
		return;
	}

	message.textContent = `Файл "${fileName}" уже загружался. Хотите продолжить с того места?`;

	confirmBtn.onclick = () => {
		modal.style.display = "none";
		onContinue?.();
	};

	cancelBtn.onclick = () => {
		modal.style.display = "none";
		onRestart?.();
	};

	modal.style.display = "block";
}

async function checkConflictThenUpload(file, hash) {
	let conflict;
	try {
		const conflictRes = await fetch('/api/Upload/conflict-check', {
			method: 'POST',
			headers: { 'Content-Type': 'application/json' },
			body: JSON.stringify({ fileName: file.name, hash })
		});

		if (conflictRes.ok) conflict = await conflictRes.json();
		else throw new Error(`HTTP ${conflictRes.status}: ${await conflictRes.text()}`);
	} catch (error) {
		console.error('Ошибка запроса конфликта:', error);
		alert('Не удалось проверить конфликт, попробуйте позже.');
		return;
	}

	if (conflict.status === "ok") {
		startUpload(file, hash, null);
	} else if (["exists", "uploading"].includes(conflict.status)) {
		showConflictModal(file.name, conflict.status, {
			onReplace: (selectedVersion) => startUpload(file, hash, selectedVersion),
			onNewVersion: () => startUpload(file, hash, null),
			onCancel: () => console.log("Пользователь отменил загрузку")
		});
	} else {
		console.error('Неожиданный ответ сервера:', conflict);
	}
}

async function startUpload(file, hash, replaceVersion, sessionId = null, alreadyUploaded = new Set()) {
	const chunkSize = 1 * 1024 * 1024;
	const totalChunks = Math.ceil(file.size / chunkSize);

	isUploadInProgress = true;

	for (let i = 0; i < totalChunks; i++) {
		if (alreadyUploaded.has(i)) continue;

		const start = i * chunkSize;
		const end = Math.min(file.size, start + chunkSize);
		const chunk = file.slice(start, end);

		const formData = new FormData();
		formData.append("chunk", chunk);
		formData.append("hash", hash);
		formData.append("chunkIndex", i);
		formData.append("totalChunks", totalChunks);
		formData.append("fileSize", file.size);
		formData.append("fileName", file.name);
		if (replaceVersion !== null) formData.append("replaceVersion", replaceVersion);
		if (sessionId !== null) formData.append("sessionId", sessionId);

		try {
			const response = await fetch('/api/Upload/chunk', { method: 'POST', body: formData });
			if (!response.ok) throw new Error(`Ошибка загрузки чанка ${i}`);
		} catch (err) {
			console.error("Ошибка загрузки чанка:", err);
			isUploadInProgress = false;
			showUploadErrorModal(file.name, i, err.message || err);
			return;
		}
	}

	try {
		const completeData = new FormData();
		completeData.append("hash", hash);

		const response = await fetch('/api/Upload/complete', {
			method: 'POST',
			body: completeData,
			headers: {
				'X-CSRF-TOKEN': document.querySelector('input[name="__RequestVerificationToken"]')?.value || ''
			}
		});

		if (!response.ok) throw new Error(await response.text());
		const result = await response.json();
		console.log("Файл успешно собран:", result);
	} catch (err) {
		console.error("Ошибка при сборке файла:", err);
		showUploadErrorModal(file.name, -1, err.message || err);
	} finally {
		isUploadInProgress = false;
	}
}

function computeSparkMD5Hash(file, chunkSize = 10 * 1024 * 1024) {
	return new Promise((resolve, reject) => {
		const chunks = Math.ceil(file.size / chunkSize);
		let currentChunk = 0;
		const spark = new SparkMD5.ArrayBuffer();
		const reader = new FileReader();

		reader.onload = (e) => {
			spark.append(e.target.result);
			currentChunk++;
			if (currentChunk < chunks) {
				loadNext();
			} else {
				resolve(spark.end());
			}
		};

		reader.onerror = () => {
			reject("Ошибка чтения файла");
		};

		function loadNext() {
			const start = currentChunk * chunkSize;
			const end = Math.min(start + chunkSize, file.size);
			reader.readAsArrayBuffer(file.slice(start, end));
		}

		loadNext();
	});
}

async function computeSHA256(file) {
	const buffer = await file.arrayBuffer();
	const hashBuffer = await crypto.subtle.digest("SHA-256", buffer);
	return Array.from(new Uint8Array(hashBuffer))
		.map(b => b.toString(16).padStart(2, '0')).join('');
}

function showUploadErrorModal(fileName, chunkIndex, error) {
	const modal = document.getElementById("upload-error-modal");
	const title = document.getElementById("upload-error-title");
	const message = document.getElementById("upload-error-message");

	if (!modal || !title || !message) {
		alert(`Ошибка загрузки файла ${fileName} на чанке ${chunkIndex + 1}. ${error}.\nЗагрузка прервана!`);
		return;
	}

	title.textContent = "Ошибка загрузки";
	message.textContent = `Файл: ${fileName}\nЧанк: ${chunkIndex + 1}\nОписание: ${error}`;
	modal.style.display = "block";
}

function closeUploadErrorModal() {
	const modal = document.getElementById("upload-error-modal");
	if (modal) modal.style.display = "none";
}

window.addEventListener("beforeunload", function (e) {
	if (isUploadInProgress) {
		const message = "Файл всё ещё загружается. Уход со страницы прервёт загрузку.";
		e.preventDefault();
		e.returnValue = message;
		return message;
	}
});
