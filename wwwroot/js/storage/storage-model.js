// storage-model.js

window.StorageModel = (function () {
	const state = {
		files: [],
		uploading: new Map(),
		cancelled: new Set()
	};

	const listeners = new Set();

	function notify() {
		for (const cb of listeners) {
			cb(getState());
		}
	}

	function notifyUploadsOnly() {
		for (const cb of listeners) {
			cb({
				files: [],
				uploading: new Map(state.uploading),
				cancelled: new Set(state.cancelled)
			});
		}
	}

	function getState() {
		return {
			files: [...state.files],
			uploading: new Map(state.uploading),
			cancelled: new Set(state.cancelled)
		};
	}

	return {
		subscribe(cb) {
			listeners.add(cb);
			cb(getState());
		},

		unsubscribe(cb) {
			listeners.delete(cb);
		},

		setFiles(files) {
			state.files = files;
			notify();
		},

		startUpload(name, totalSize) {
			const key = name.trim().toLowerCase();
			state.uploading.set(key, { uploaded: 0, total: totalSize });
			notifyUploadsOnly();
		},

		updateUpload(name, uploaded) {
			const key = name.trim().toLowerCase();
			if (state.uploading.has(key)) {
				const file = state.uploading.get(key);
				file.uploaded = uploaded;
				state.uploading.set(key, file);
				notifyUploadsOnly();
			}
		},

		cancelUpload(name) {
			const key = name.trim().toLowerCase();
			state.cancelled.add(key);
			state.uploading.delete(key);
			notifyUploadsOnly();
		},

		finishUpload(name) {
			const key = name.trim().toLowerCase();
			state.uploading.delete(key);
			state.cancelled.delete(key);
			notifyUploadsOnly();
		},

		getState
	};
})();

window.renderStorageTable = function (state) {
	const tableBody = document.querySelector("table.sortable tbody");
	if (!tableBody) return;

	renderUploadedFiles(state.files, state.uploading, tableBody);
	renderUploadingFiles(state.uploading, tableBody);
};

function renderUploadedFiles(files, uploading, tableBody) {
	const uploadingKeys = new Set([...uploading.keys()]);
	const filesMap = new Map();

	for (const file of files) {
		if (!file || !file.extension || !file.baseName || !file.currentVersion) continue;
		const ext = file.extension.startsWith(".") ? file.extension : `.${file.extension}`;
		const fullName = `${file.baseName}_${file.currentVersion}${ext}`;
		const displayName = `${file.displayName}${ext}`;
		const key = fullName.toLowerCase();
		if (uploadingKeys.has(key)) continue;

		filesMap.set(key, { fullName, displayName, file });
	}

	tableBody.querySelectorAll("tr.uploaded-row").forEach(row => row.remove());

	for (const [key, { fullName, displayName, file }] of filesMap.entries()) {
		const row = document.createElement("tr");
		row.classList.add("uploaded-row");
		row.innerHTML = `
			<td><div class="cell-content">${displayName}</div></td>
			<td>
				<div class="multi-button">
					<button class="button-sliding primary vers-button version-button" data-version="${file.currentVersion}">${file.currentVersion}</button>
					<div class="dropdown-arrow">&#9662;</div>
				</div>
			</td>
			<td>${file.sizeMb}</td>
			<td>${file.lastWriteTime}</td>
			<td>
				<div class="multi-button">
					<button class="button-sliding primary action-button">Удалить</button>
					<div class="dropdown-arrow">&#9662;</div>
				</div>
			</td>
		`;

		const versionGroup = row.querySelectorAll(".multi-button")[0];
		const actionGroup = row.querySelectorAll(".multi-button")[1];

		if (versionGroup) setupVersionDropdown(versionGroup, file);
		if (actionGroup) setupMultiButtonEvents(actionGroup, fullName, window.currentUserId);

		tableBody.appendChild(row);
	}
}

function renderUploadingFiles(uploading, tableBody) {
	tableBody.querySelectorAll("tr.uploading-row").forEach(row => row.remove());

	for (const [key, info] of uploading.entries()) {
		const sizeText = `${Math.round(info.uploaded / 1048576)}/${Math.round(info.total / 1048576)} МБ`;
		const row = document.createElement("tr");
		row.classList.add("uploading-row");
		row.id = `uploading-${key}`;
		row.style.backgroundColor = "#f0f0f0";
		row.innerHTML = `
			<td><div class="cell-content">${key}</div></td>
			<td>—</td>
			<td class="size-cell">${sizeText}</td>
			<td>Загружается...</td>
			<td><button class="button-sliding danger cancel-button">Отмена</button></td>
		`;

		const cancelBtn = row.querySelector(".cancel-button");
		if (cancelBtn) {
			cancelBtn.addEventListener("click", function () {
				this.disabled = true;
				this.textContent = "Отмена...";
				StorageModel.cancelUpload(key);
				if (window.cancelUploadingFile) {
					window.cancelUploadingFile(key);
				}
			});
		}

		tableBody.appendChild(row);
	}
}
