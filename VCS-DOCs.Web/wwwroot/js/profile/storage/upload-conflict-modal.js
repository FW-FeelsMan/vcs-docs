//upload-conflict-modal.js
function showConflictModal(fileName, conflictType, { onReplace, onNewVersion, onCancel }) {
	const modal = document.getElementById("upload-version-modal");
	if (!modal) return console.error("Модалка upload-version-modal не найдена");

	const title = modal.querySelector("#version-modal-title");
	const message = modal.querySelector("#version-conflict-message");
	const cancelBtn = modal.querySelector("#conflict-cancel");
	const versionBtn = modal.querySelector("#conflict-new-version");

	const replaceContainer = modal.querySelector("#split-button");
	const selectedVersionSpan = modal.querySelector("#selected-version");
	const dropdownArrow = modal.querySelector("#version-dropdown");
	const versionList = modal.querySelector("#version-list");

	if (!title || !message || !cancelBtn || !versionBtn || !replaceContainer || !dropdownArrow || !versionList || !selectedVersionSpan) {
		console.error("Элементы модалки не найдены");
		return;
	}

	let selectedVersion = null;

	title.textContent = "Конфликт версий";
	message.textContent = `Файл "${fileName}" уже существует. Выберите действие.`;

	selectedVersionSpan.textContent = "V?";
	versionList.innerHTML = "";
	versionList.style.display = "none";

	// Подгружаем версии
	fetch(`/api/Upload/versions/${encodeURIComponent(fileName)}`)
		.then(res => res.json())
		.then(versions => {
			if (versions.length > 0) {
				selectedVersion = versions[0].Version;
				selectedVersionSpan.textContent = `V${selectedVersion}`;

				versions.forEach(ver => {
					const item = document.createElement("div");
					item.className = "dropdown-item";
					item.textContent = `V${ver.Version} (${new Date(ver.UpdatedAt).toLocaleString()})`;
					item.onclick = () => {
						selectedVersion = ver.Version;
						selectedVersionSpan.textContent = `V${selectedVersion}`;
						versionList.style.display = "none";
					};
					versionList.appendChild(item);
				});
			} else {
				selectedVersionSpan.textContent = "Нет версий";
			}
		})
		.catch(err => {
			console.error("Ошибка загрузки версий:", err);
			selectedVersionSpan.textContent = "Ошибка";
		});

	// Показываем список
	const toggleList = (e) => {
		const visible = versionList.style.display === "block";
		versionList.style.display = visible ? "none" : "block";
		e?.stopPropagation();
	};

	dropdownArrow.onclick = toggleList;
	selectedVersionSpan.onclick = toggleList;

	// Скрыть при клике вне
	document.addEventListener("click", (e) => {
		if (!replaceContainer.contains(e.target)) {
			versionList.style.display = "none";
		}
	});

	// Клик по замене
	replaceContainer.onclick = (e) => {
		if (e.target === dropdownArrow || e.target === selectedVersionSpan) return;

		if (selectedVersion !== null) {
			modal.style.display = "none";
			onReplace?.(selectedVersion);
		} else {
			alert("Выберите версию для замены.");
		}
	};
	cancelBtn.onclick = () => {
		try {
			modal.style.display = "none";
			isCanceled = true;
			if (typeof onCancel === 'function') onCancel();
		} finally {
			const uploadBtn = document.getElementById('uploadFileButton');
			if (uploadBtn) uploadBtn.disabled = false;
		}
	};

	versionBtn.onclick = () => {
		modal.style.display = "none";
		onNewVersion?.();
	};

	modal.style.display = "block";
}