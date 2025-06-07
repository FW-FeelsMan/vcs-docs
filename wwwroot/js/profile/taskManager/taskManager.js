// Task Manager Module
window.taskManager = (function () {
    const taskListEl = document.getElementById("taskCardList");
    const tasks = [];
    const removedTasks = new Map();

    function getTaskKey(task) {
        const keys = [];
        if (task.taskKey) keys.push(task.taskKey);
        if (task.title && task.type) keys.push(`${task.title}_${task.type}`);
        return keys;
    }

    function addToBlacklist(task, reason = 'auto') {
        const keys = getTaskKey(task);
        keys.forEach(key => {
            const val = removedTasks.get(key) || {};
            val[task.statusClass] = {
                timestamp: Date.now(),
                reason,
                title: task.title
            };
            removedTasks.set(key, val);
        });
    }

    function isInBlacklist(task) {
        return getTaskKey(task).some(key => {
            const val = removedTasks.get(key);
            return val && val[task.statusClass];
        });
    }

    function removeFromBlacklist(task) {
        getTaskKey(task).forEach(key => {
            if (!removedTasks.has(key)) return;
            const val = removedTasks.get(key);
            delete val[task.statusClass];
            if (Object.keys(val).length === 0) removedTasks.delete(key);
        });
    }

    function cleanupBlacklist() {
        const now = Date.now();
        const maxAge = 60000;
        for (const [key, data] of removedTasks.entries()) {
            for (const [status, info] of Object.entries(data)) {
                if (now - info.timestamp > maxAge) delete data[status];
            }
            if (Object.keys(data).length === 0) removedTasks.delete(key);
        }
    }

    function render() {
        if (!taskListEl) return console.warn("[TaskManager] taskCardList не найден");

        const domCards = new Map();
        taskListEl.querySelectorAll(".task-card").forEach(card => {
            const key = card.querySelector(".task-status")?.dataset.taskkey;
            if (key) domCards.set(key, card);
        });

        const seenKeys = new Set();

        tasks.forEach(task => {
            const key = task.taskKey;
            if (!key) return;

            seenKeys.add(key);
            const existingCard = domCards.get(key);

            if (existingCard) {
                let hasChanged = false;

                const titleEl = existingCard.querySelector(".task-title");
                if (titleEl && titleEl.textContent !== task.title) {
                    titleEl.textContent = task.title;
                    hasChanged = true;
                }

                const statusEl = existingCard.querySelector(".task-status");
                if (statusEl) {
                    if (statusEl && !task.nextRunUtc && statusEl.textContent !== task.statusText) {
                        statusEl.textContent = task.statusText;
                        hasChanged = true;
                    }

                    const desiredClass = `task-status ${task.statusClass}`;
                    if (statusEl.className !== desiredClass) {
                        statusEl.className = desiredClass;
                        statusEl.style = task.statusClass === 'waiting'
                            ? 'color: var(--primary-color); background: none;'
                            : '';
                        hasChanged = true;
                    }
                }

                // optionally: update visibility of buttons if dynamic

            } else {
                // Create new card
                const card = document.createElement("div");
                card.className = `task-card ${task.type}-task`;

                const titleHtml = escapeHtml(task.title);
                const statusHtml = escapeHtml(task.statusText);

                card.innerHTML = `
				<div class="task-card-header">
					<h4 class="task-title">${titleHtml}</h4>
					<span class="task-status ${task.statusClass}" data-taskkey="${task.taskKey || ""}" style="${task.statusClass === 'waiting' ? 'color: var(--primary-color); background: none;' : ''}">
						${statusHtml}
					</span>
				</div>
				<div class="task-card-meta">
					<span class="task-type">Тип: ${capitalize(task.type)}</span>
					<div class="task-buttons">
						${task.cancelable ? '<button class="task-cancel-btn">Отменить</button>' : ""}
						${task.manualTrigger ? '<button class="button-sliding danger small call task-trigger-btn">Вызвать</button>' : ""}
					</div>
				</div>
			`;

                if (task.cancelable) {
                    const btn = card.querySelector(".task-cancel-btn");
                    btn.onclick = () => {
                        if (typeof task.onCancel === "function") task.onCancel(task);
                        removeTask(task);
                    };
                }

                if (task.manualTrigger) {
                    const btn = card.querySelector(".task-trigger-btn");
                    btn.onclick = () => {
                        if (btn.disabled) return;
                        const confirmAndTrigger = () => {
                            btn.disabled = true;
                            triggerTask(task.taskKey);
                            setTimeout(() => btn.disabled = false, 3000);
                        };
                        if (task.taskKey === "uploadCleanup_incomplete") {
                            showCleanupWarningModal({
                                title: "Очистка INCOMPLETE",
                                message: "Будут удалены чанки в статусе INCOMPLETE. Это остановит текущие загрузки. Вы уверены?",
                                onConfirm: confirmAndTrigger,
                                onCancel: () => console.log("Очистка INCOMPLETE отменена.")
                            });
                        } else if (task.taskKey === "uploadCleanup_compiling") {
                            showCleanupWarningModal({
                                title: "Очистка COMPILING",
                                message: "Будут удалены чанки в статусе COMPILING. Это может оборвать сборку больших файлов. Уверены?",
                                onConfirm: confirmAndTrigger,
                                onCancel: () => console.log("Очистка COMPILING отменена.")
                            });
                        } else {
                            confirmAndTrigger();
                        }
                    };
                }

                taskListEl.appendChild(card);
            }
        });

        domCards.forEach((card, key) => {
            if (!seenKeys.has(key)) card.remove();
        });
    }

    function addOrUpdateTask(task) {
        if (!task || (!task.taskKey && !task.title)) return;
        if (isInBlacklist(task)) return;

        let existing = tasks.find(t => t.taskKey && t.taskKey === task.taskKey);
        let justCompleted = false;

        if (existing) {
            const wasDone = existing.statusClass === "done";
            const oldStatus = existing.statusClass;
            Object.assign(existing, task);
            if (oldStatus !== task.statusClass && task.statusClass !== "done") removeFromBlacklist(task);
            if (!wasDone && task.statusClass === "done") {
                justCompleted = true;
                addToBlacklist(task, 'auto');
            }
        } else {
            tasks.push(task);
            if (task.statusClass === "done") justCompleted = true;
        }

        render();

        const autoRemove = task.autoRemove ?? (task.type !== "system");
        if (justCompleted && autoRemove) {
            setTimeout(() => {
                const stillDone = tasks.find(t => t.taskKey === task.taskKey)?.statusClass === "done";
                if (stillDone) removeTask(task, true);
            }, task.autoRemoveDelay || 5000);
        }
    }

    function updateTimers() {
        const now = Date.now();
        tasks.forEach(task => {
            if (typeof task.nextRunUtc === "string" && task.taskKey) {
                const el = taskListEl.querySelector(`.task-status[data-taskkey="${task.taskKey}"]`);
                if (!el) return;
                const target = Date.parse(task.nextRunUtc);
                const diff = Math.max(0, Math.floor((target - now) / 1000));
                const mins = Math.floor(diff / 60);
                const secs = diff % 60;
                el.innerText = `${Math.floor(diff / 60)}:${(diff % 60).toString().padStart(2, "0")}`;
            }
        });
    }

    function fetchTasks() {
        fetch("/api/tasks/active")
            .then(res => res.json())
            .then(result => Array.isArray(result) && result.forEach(addOrUpdateTask))
            .catch(err => console.error("Ошибка при получении задач:", err));
    }

    function triggerTask(taskKey) {
        fetch("/api/tasks/trigger", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ taskKey })
        })
            .then(res => res.json())
            .then(result => console.log("Задача вызвана:", result.message))
            .catch(err => alert("Не удалось вызвать задачу."));
    }

    function removeTask(task, toBlacklist = false) {
        const index = tasks.indexOf(task);
        if (index === -1) return;
        const el = taskListEl.querySelector(`.task-status[data-taskkey="${task.taskKey}"]`)?.closest(".task-card");
        if (toBlacklist) addToBlacklist(task);
        if (el) {
            el.classList.add("removing");
            setTimeout(() => {
                tasks.splice(index, 1);
                el.remove();
            }, 500);
        } else {
            tasks.splice(index, 1);
            render();
        }
    }

    function escapeHtml(str) {
        const div = document.createElement("div");
        div.textContent = str;
        return div.innerHTML;
    }

    function capitalize(s) {
        return s.charAt(0).toUpperCase() + s.slice(1);
    }

    // Init
    if (window.signalR?.HubConnectionBuilder) {
        const connection = new signalR.HubConnectionBuilder()
            .withUrl("/hubs/tasks")
            .withAutomaticReconnect()
            .build();
        connection.on("TaskUpdate", task => addOrUpdateTask(task));
        connection.start().catch(err => console.error("Ошибка подключения к TaskHub:", err));
    }

    setInterval(cleanupBlacklist, 30000);
    setInterval(updateTimers, 1000);
    setInterval(fetchTasks, 10000);
    fetchTasks();

    return {
        addTask: addOrUpdateTask,
        clear: () => { tasks.length = 0; render(); },
        render,
        clearBlacklist: () => removedTasks.clear(),
        getBlacklist: () => Array.from(removedTasks.entries())
    };
})();

function showCleanupWarningModal({ title, message, onConfirm, onCancel }) {
    const modal = document.getElementById("upload-warning-modal");
    if (!modal) return console.error("Модалка upload-warning-modal не найдена в DOM");

    modal.querySelector("#warning-modal-title").textContent = title || "Подтверждение";
    modal.querySelector("#warning-message").textContent = message || "Вы уверены?";

    const confirmBtn = modal.querySelector("#warning-confirm");
    const cancelBtn = modal.querySelector("#warning-cancel");

    confirmBtn.onclick = () => {
        modal.style.display = "none";
        onConfirm?.();
    };
    cancelBtn.onclick = () => {
        modal.style.display = "none";
        onCancel?.();
    };
    modal.style.display = "block";
}

window.showCleanupWarningModal = showCleanupWarningModal;
