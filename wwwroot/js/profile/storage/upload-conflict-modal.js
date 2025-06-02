function showConflictModal(fileName, conflictType, { onReplace, onNewVersion, onCancel }) {
	const modal = document.getElementById("upload-version-modal");
	if (!modal) {
		console.error("Модалка upload-version-modal не найдена в DOM");
		return;
	}

	const title = modal.querySelector("#version-modal-title");
	const message = modal.querySelector("#version-conflict-message");
	const cancelBtn = modal.querySelector("#conflict-cancel");
	const versionBtn = modal.querySelector("#conflict-new-version");

	const replaceContainer = modal.querySelector("#version-selector");
	const selectedVersionSpan = modal.querySelector("#selected-version");
	const dropdownArrow = modal.querySelector("#version-dropdown");
	const versionList = modal.querySelector("#version-list");

	if (!title || !message || !cancelBtn || !versionBtn || !replaceContainer || !dropdownArrow || !versionList || !selectedVersionSpan) {
		console.error("Один или несколько элементов модалки не найдены");
		return;
	}

	let selectedVersion = null;

	title.textContent = "Конфликт версий";
	message.textContent = `Файл "${fileName}" уже существует. Выберите действие.`;

	selectedVersionSpan.textContent = "V?";
	versionList.innerHTML = "";
	versionList.style.display = "none";

	// Загрузка списка версий
	fetch(`/api/Upload/versions/${encodeURIComponent(fileName)}`)
		.then(res => res.json())
		.then(versions => {
			if (versions.length > 0) {
				selectedVersion = versions[0].Version;
				selectedVersionSpan.textContent = `V${selectedVersion}`;

				versions.forEach(ver => {
					const item = document.createElement('div');
					item.textContent = `V${ver.Version} (${new Date(ver.UpdatedAt).toLocaleString()})`;
					item.title = `V${ver.Version} (${new Date(ver.UpdatedAt).toLocaleString()})`;
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

	// Обновленная логика для выпадающего списка
	const toggleList = (e) => {
		// Позиционируем список прямо под кнопкой
		const rect = replaceContainer.getBoundingClientRect();
		versionList.style.top = `${replaceContainer.offsetHeight + 1}px`; // 1px отступ
		versionList.style.left = `${8}px`;
		versionList.style.display = versionList.style.display === "none" ? "block" : "none";
		e?.stopPropagation();
	};

	dropdownArrow.onclick = toggleList;
	selectedVersionSpan.onclick = toggleList;

	document.addEventListener('click', (e) => {
		if (!versionList.contains(e.target) && !dropdownArrow.contains(e.target) && e.target !== selectedVersionSpan) {
			versionList.style.display = "none";
		}
	});

	replaceContainer.onclick = (e) => {
		// Предотвращаем срабатывание при клике на стрелку или версию
		if (e.target === dropdownArrow || e.target === selectedVersionSpan) {
			return;
		}

		if (selectedVersion !== null) {
			modal.style.display = "none";
			onReplace?.(selectedVersion);
		} else {
			alert("Выберите версию для замены.");
		}
	};

	cancelBtn.onclick = () => {
		modal.style.display = "none";
		onCancel?.();
	};

	versionBtn.onclick = () => {
		modal.style.display = "none";
		onNewVersion?.();
	};

	modal.style.display = "block";
}
