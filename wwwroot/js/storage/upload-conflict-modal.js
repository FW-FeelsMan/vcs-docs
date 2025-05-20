//upload-conflict-modal.js скрипт модального окна личного хранилища
let pendingUploadFile = null;
function showConflictModal(fileName, conflictType, { onReplace, onNewVersion, onCancel }) {
	console.log(`[modal] Показ модалки для: ${fileName}, тип: ${conflictType}`);
	const modal = document.getElementById("upload-conflict-modal");
	const message = document.getElementById("conflict-message");
	const cancelBtn = document.getElementById("conflict-cancel");
	const replaceBtn = document.getElementById("conflict-replace");
	const newVersionBtn = document.getElementById("conflict-new-version");

	if (!modal || !message || !cancelBtn || !replaceBtn || !newVersionBtn) return;

	if (conflictType === "uploading") {
		message.textContent = `Файл "${fileName}" уже загружается. Что вы хотите сделать?`;
		replaceBtn.textContent = "Загрузить заново";
		newVersionBtn.style.display = "none"; 
	} else if (conflictType === "exists") {
		message.textContent = `Файл "${fileName}" уже существует. Заменить последнюю версию?`;
		replaceBtn.textContent = "Заменить";
		newVersionBtn.style.display = "inline-block";
	} else {
		message.textContent = "Обнаружен конфликт. Выберите действие:";
	}

	modal.style.display = "block";

	cancelBtn.onclick = () => {
		modal.style.display = "none";
		pendingUploadFile = null;
		if (typeof onCancel === "function") onCancel();
	};

	replaceBtn.onclick = () => {
		modal.style.display = "none";
		if (typeof onReplace === "function") onReplace();
		pendingUploadFile = null;
	};

	newVersionBtn.onclick = () => {
		modal.style.display = "none";
		if (typeof onNewVersion === "function") onNewVersion();
		pendingUploadFile = null;
	};
}
