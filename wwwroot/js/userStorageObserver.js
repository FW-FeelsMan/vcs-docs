//userStorageObserver.js
let connection = null;
let csrfToken = null;
window.currentlyUploadingFiles = window.currentlyUploadingFiles || new Map();
window.cancelledUploads = window.cancelledUploads || new Set();
let currentStorageFiles = [];

document.addEventListener("DOMContentLoaded", () => {
	const profileBtn = document.querySelector("#button2");
	if (!profileBtn) return console.warn("[userStorage] Кнопка профиля не найдена");

	profileBtn.addEventListener("click", waitForStorageTab);
});

function waitForStorageTab() {
	const tab = document.querySelector('li[data-target="storage"]');
	if (tab) {
		tab.addEventListener("click", () => {
			ensureConnectionReady().then(() => {
				requestFiles();
				refreshStorageStatus();
			});
		});
	} else {
		setTimeout(waitForStorageTab, 100);
	}
}

async function ensureConnectionReady() {
	if (connection?.state === signalR.HubConnectionState.Connected) return;

	if (!csrfToken) {
		const token = document.querySelector('meta[name="csrf-token"]');
		if (!token) throw new Error("[userStorage] CSRF токен не найден");
		csrfToken = token.content;
	}

	connection = new signalR.HubConnectionBuilder()
		.withUrl("/userStorageHub")
		.withAutomaticReconnect({
			nextRetryDelayInMilliseconds: (ctx) => [1000, 3000, 5000][ctx.previousRetryCount] || 10000
		})
		.configureLogging(signalR.LogLevel.None)
		.build();

	connection.on("ReceiveStorageUpdate", (files) => {
		currentStorageFiles = files || [];
		const tableBody = document.querySelector("table.sortable tbody");
		if (tableBody) updateNonUploadingRows(tableBody, currentStorageFiles);
		refreshStorageStatus();
	});

	connection.on("UploadProgress", ({ name, uploadedBytes, totalBytes }) => {
		const key = name.toLowerCase();

		if (window.cancelledUploads.has(key)) {
			window.cancelledUploads.delete(key);
		}

		window.currentlyUploadingFiles.set(key, {
			uploaded: uploadedBytes,
			total: totalBytes
		});

		const tableBody = document.querySelector("table.sortable tbody");
		if (tableBody) renderUploadingFiles(tableBody);
	});


	connection.on("UploadCancelled", ({ name }) => {
		const key = name.toLowerCase();
		window.currentlyUploadingFiles.delete(key);
		window.cancelledUploads.delete(key);
		const row = document.getElementById(`uploading-${key}`);
		if (row) row.remove();
		refreshStorageStatus();
	});

	connection.onreconnecting(err => console.warn("[SignalR] Переподключение...", err));
	connection.onreconnected(() => {
		requestFiles();
		refreshStorageStatus();
	});
	connection.onclose(err => console.warn("[SignalR] Соединение закрыто", err));

	try {
		await connection.start();
	} catch (err) {
		console.error("[SignalR] Ошибка подключения:", err);
	}
}

function requestFiles() {
	if (connection?.state !== signalR.HubConnectionState.Connected) {
		console.error("[userStorage] Соединение неактивно для запроса файлов.");
		return;
	}
	connection.invoke("RequestCurrentFiles").catch(err =>
		console.error("[userStorage] Ошибка запроса файлов:", err)
	);
}

function updateNonUploadingRows(tableBody, files) {
	tableBody.querySelectorAll("tr:not([id^='uploading-'])").forEach(row => row.remove());

	for (const file of files) {
		const lower = file.name.toLowerCase();
		if (lower.endsWith(".ini") || lower.startsWith("history_")) continue;

		const row = document.createElement("tr");
		row.innerHTML = `
			<td><div class="cell-content">${file.name}</div></td>
			<td>${file.sizeMb}</td>
			<td>${file.lastWriteTime}</td>
			<td><button class="button-sliding danger delete-button">Удалить</button></td>
		`;

		const deleteBtn = row.querySelector(".delete-button");
		if (deleteBtn) {
			deleteBtn.addEventListener("click", () => {
				if (confirm("Удалить файл?")) deleteFile(file.name);
			});
		}

		tableBody.appendChild(row);
	}
}

function renderUploadingFiles(tableBody) {
	for (const [fileName, fileInfo] of window.currentlyUploadingFiles.entries()) {
		const key = fileName.toLowerCase();
		if (window.cancelledUploads.has(key)) continue;
		if (currentStorageFiles?.some(f => f.name.toLowerCase() === key)) continue;

		const size = fileInfo
			? `${Math.round(fileInfo.uploaded / 1048576)}/${Math.round(fileInfo.total / 1048576)} МБ`
			: "В процессе...";

		let row = document.getElementById(`uploading-${key}`);
		if (row) {
			row.querySelector(".size-cell").textContent = size;
			continue;
		}

		row = document.createElement("tr");
		row.id = `uploading-${key}`;
		row.style.backgroundColor = "#f0f0f0";
		row.innerHTML = `
			<td><div class="cell-content">${fileName}</div></td>
			<td class="size-cell">${size}</td>
			<td>Загружается...</td>
			<td><button class="button-sliding danger cancel-button">Отмена</button></td>
		`;

		const cancelBtn = row.querySelector(".cancel-button");
		if (cancelBtn) {
			cancelBtn.addEventListener("click", function () {
				const btn = this;
				btn.disabled = true;
				btn.textContent = "Отмена...";
				window.cancelledUploads.add(key);
				const rowEl = btn.closest("tr");
				if (rowEl) rowEl.remove();
				if (window.cancelUploadingFile) window.cancelUploadingFile(fileName);
			});
		}

		tableBody.appendChild(row);
	}
}

async function deleteFile(name) {
	const formData = new FormData();
	formData.append("fileName", name);

	try {
		const res = await fetch("/Content/profile_page?handler=DeleteFile", {
			method: "POST",
			headers: {
				"Accept": "application/json",
				"X-CSRF-TOKEN": csrfToken
			},
			body: formData
		});
		const json = await res.json();
		console.log("[StorageStatus]", JSON.stringify(json));
		if (json.success) {
			requestFiles();
			refreshStorageStatus();
		} else {
			console.error("[userStorage] Ошибка удаления:", json.error);
		}
	} catch (err) {
		console.error("[userStorage] Ошибка удаления файла:", err);
	}
}

window.cancelUploadingFile = async function (fileName) {
	if (!connection || connection.state !== signalR.HubConnectionState.Connected) {
		console.warn("[userStorage] SignalR неактивен, отмена невозможна.");
		return;
	}
	try {
		await connection.invoke("CancelUpload", fileName);
	} catch (err) {
		console.error("[userStorage] Ошибка отмены загрузки через SignalR:", err);
	}
};

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
			const loadingText = json.reservedMb > 0
				? `Загружается: ${json.reservedMb.toFixed(2)} МБ`
				: `Загружается: 0 МБ`;
			const freeText = `Свободно: ${json.freeMb.toFixed(2)} МБ / 10240 МБ`;
			storageCounter.textContent = `${loadingText}    ${freeText}`;
		}
	} catch (err) {
		console.error("Ошибка при получении статуса хранилища:", err);
	}
}

window.refreshStorageStatus = refreshStorageStatus;
