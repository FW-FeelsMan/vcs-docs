window.taskManager = (function () {
    const taskListEl = document.getElementById("taskCardList");
    const tasks = [];
    const removedTasks = new Map();

    function getTaskKey(task) {
        return task.taskKey || `${task.title}_${task.type}`;
    }

    function addToBlacklist(task, reason = 'auto') {
        const key = getTaskKey(task);
        const val = removedTasks.get(key) || {};
        val[task.statusClass] = {
            timestamp: Date.now(),
            reason: reason,
            title: task.title
        };
        removedTasks.set(key, val);
    }

    function isInBlacklist(task) {
        const key = getTaskKey(task);
        const val = removedTasks.get(key);
        if (!val) return false;
        return !!val[task.statusClass];
    }

    function removeFromBlacklist(task) {
        const key = getTaskKey(task);
        if (!removedTasks.has(key)) return;
        const val = removedTasks.get(key);
        delete val[task.statusClass];
        if (Object.keys(val).length === 0) removedTasks.delete(key);
    }

    function cleanupBlacklist() {
        const now = Date.now();
        const maxAge = 60000;
        for (const [key, data] of removedTasks.entries()) {
            for (const [status, info] of Object.entries(data)) {
                if (now - info.timestamp > maxAge) {
                    delete data[status];
                }
            }
            if (Object.keys(data).length === 0) removedTasks.delete(key);
        }
    }

    function render() {
        if (!taskListEl) {
            console.warn("[TaskManager] taskCardList не найден");
            return;
        }

        taskListEl.innerHTML = "";

        tasks.forEach(task => {
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
                const cancelBtn = card.querySelector(".task-cancel-btn");
                cancelBtn.addEventListener("click", () => {
                    if (typeof task.onCancel === "function") {
                        task.onCancel(task);
                    }
                    removeTask(task);
                });
            }

            if (task.manualTrigger) {
                const triggerBtn = card.querySelector(".task-trigger-btn");
                triggerBtn.addEventListener("click", () => {
                    if (triggerBtn.disabled) return;

                    const confirmAndTrigger = () => {
                        triggerBtn.disabled = true;
                        triggerTask(task.taskKey);
                        setTimeout(() => triggerBtn.disabled = false, 3000);
                    };

                    if (task.taskKey === "uploadCleanup_incomplete") {
                        showCleanupWarningModal({
                            title: "Очистка INCOMPLETE",
                            message: "Будут удалены чанки в статусе INCOMPLETE. Это остановит текущие загрузки. Вы уверены?",
                            onConfirm: confirmAndTrigger,
                            onCancel: () => console.log("Очистка INCOMPLETE отменена.")
                        });
                    }
                    else if (task.taskKey === "uploadCleanup_compiling") {
                        showCleanupWarningModal({
                            title: "Очистка COMPILING",
                            message: "Будут удалены чанки в статусе COMPILING. Это может оборвать сборку больших файлов. Уверены?",
                            onConfirm: confirmAndTrigger,
                            onCancel: () => console.log("Очистка COMPILING отменена.")
                        });
                    }
                    else {
                        confirmAndTrigger();
                    }
                });
            }
            taskListEl.appendChild(card);
        });
    }

    function addOrUpdateTask(task) {
        if (!task || (!task.taskKey && !task.title)) return;
        if (isInBlacklist(task)) return;

        let existing = null;
        let justCompleted = false;
        let shouldAutoRemove = false;
        let statusChanged = false;

        if (task.taskKey) {
            existing = tasks.find(t => t.taskKey === task.taskKey);
        } else {
            existing = tasks.find(t => t.title === task.title && t.type === task.type);
        }

        if (existing) {
            const wasDone = existing.statusClass === "done";
            const oldStatus = existing.statusClass;
            Object.assign(existing, task);
            if (oldStatus !== task.statusClass) {
                if (task.statusClass !== "done") {
                    removeFromBlacklist(task);
                }
                statusChanged = true;
            }
            if (!wasDone && task.statusClass === "done") {
                justCompleted = true;
                addToBlacklist(task, 'auto');
            }
        } else {
            tasks.push(task);
            if (task.statusClass === "done") justCompleted = true;
        }

        const isSystemTask = task.type === "system";
        const defaultAutoRemove = !isSystemTask;
        const autoRemove = task.autoRemove !== undefined ? task.autoRemove : defaultAutoRemove;
        const autoRemoveDelay = task.autoRemoveDelay || 5000;
        if (autoRemove) {
            shouldAutoRemove = true;
        }

        render();

        if (justCompleted) {
            setTimeout(() => {
                const stillDone = tasks.find(t => t.taskKey === task.taskKey)?.statusClass === "done";
                if (stillDone) {
                    removeTask(task, true);
                }
            }, 5000);
        }

        if (shouldAutoRemove && !justCompleted && !statusChanged) {
            const taskToRemove = existing || task;
            setTimeout(() => {
                const stillExists = tasks.find(t =>
                    (t.taskKey && t.taskKey === taskToRemove.taskKey) ||
                    (t.title === taskToRemove.title && t.type === taskToRemove.type)
                );
                if (stillExists) {
                    removeTask(taskToRemove, true);
                }
            }, autoRemoveDelay);
        }
    }

    function updateTimers() {
        const now = Date.now();
        tasks.forEach(task => {
            if (typeof task.nextRunUtc === "string" && task.taskKey) {
                const target = Date.parse(task.nextRunUtc);
                let diffSec = Math.max(0, Math.floor((target - now) / 1000));
                task.statusText = `Автозапуск: ${formatTime(diffSec)}`;
                const el = taskListEl.querySelector(`.task-status[data-taskkey="${task.taskKey}"]`);
                if (el) el.innerText = task.statusText;
            }
        });
    }

    function fetchTasks() {
        fetch("/api/tasks/active")
            .then(res => {
                if (!res.ok) throw new Error(`HTTP ${res.status}`);
                return res.json();
            })
            .then(result => {
                if (!Array.isArray(result)) return;
                result.forEach(task => addOrUpdateTask(task));
            })
            .catch(err => {
                console.error("Ошибка при получении задач:", err);
            });
    }

    function triggerTask(taskKey) {
        if (!taskKey) return;

        fetch("/api/tasks/trigger", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ taskKey })
        })
            .then(res => {
                if (!res.ok) throw new Error(`HTTP ${res.status}`);
                return res.json();
            })
            .then(result => {
                console.log("Задача вызвана:", result.message);
            })
            .catch(err => {
                console.error("Ошибка при вызове задачи:", err);
                alert("Не удалось вызвать задачу.");
            });
    }

    function formatTime(seconds) {
        const mins = Math.floor(seconds / 60);
        const secs = seconds % 60;
        if (seconds < 60) return `${secs} сек.`;
        if (seconds < 3600) return `${mins} мин. ${secs.toString().padStart(2, "0")} сек.`;
        return `${Math.floor(seconds / 3600)} ч. ${mins % 60} мин.`;
    }

    function removeTask(task, addToBlacklistFlag = false) {
        const index = tasks.indexOf(task);
        if (index !== -1) {
            const cardEl = taskListEl.querySelector(`.task-status[data-taskkey="${task.taskKey}"]`)?.closest('.task-card');
            if (addToBlacklistFlag) {
                addToBlacklist(task, 'auto');
            }
            if (cardEl) {
                console.log("Запуск анимации удаления для:", task.title);
                cardEl.classList.add('removing');
                setTimeout(() => {
                    tasks.splice(index, 1);
                    if (cardEl && cardEl.parentNode) {
                        cardEl.parentNode.removeChild(cardEl);
                    }
                }, 500);
            }
            else {
                tasks.splice(index, 1);
                render();
            }
        }
    }
    function clear() {
        tasks.length = 0;
        render();
    }

    function capitalize(text) {
        return text.charAt(0).toUpperCase() + text.slice(1);
    }

    function escapeHtml(str) {
        const div = document.createElement("div");
        div.textContent = str;
        return div.innerHTML;
    }

    function debugTasks() {
        console.table(tasks);
    }
    window.debugTasks = debugTasks;

    // SignalR
    if (window.signalR && signalR.HubConnectionBuilder) {
        const connection = new signalR.HubConnectionBuilder()
            .withUrl("/hubs/tasks")
            .withAutomaticReconnect()
            .build();

        connection.on("TaskUpdate", task => {
            addOrUpdateTask(task);
        });

        connection.start().catch(err => console.error("Ошибка подключения к TaskHub:", err));
    }

    setInterval(cleanupBlacklist, 30000);
    setInterval(updateTimers, 1000);
    setInterval(fetchTasks, 10000);
    fetchTasks();

    return {
        addTask: addOrUpdateTask,
        clear,
        render,
        clearBlacklist: () => removedTasks.clear(),
        getBlacklist: () => Array.from(removedTasks.entries())
    };
})();
function showCleanupWarningModal({ title, message, onConfirm, onCancel }) {
    const modal = document.getElementById("upload-warning-modal");
    if (!modal) {
        console.error("Модалка upload-warning-modal не найдена в DOM");
        return;
    }

    const titleEl = modal.querySelector("#warning-modal-title");
    const messageEl = modal.querySelector("#warning-message");
    const confirmBtn = modal.querySelector("#warning-confirm");
    const cancelBtn = modal.querySelector("#warning-cancel");

    if (!titleEl || !messageEl || !confirmBtn || !cancelBtn) {
        console.error("Элементы предупреждающей модалки не найдены");
        return;
    }

    titleEl.textContent = title || "Подтверждение";
    messageEl.textContent = message || "Вы уверены?";

    // Очищаем старые обработчики
    const newConfirm = () => {
        modal.style.display = "none";
        onConfirm?.();
    };
    const newCancel = () => {
        modal.style.display = "none";
        onCancel?.();
    };

    confirmBtn.onclick = newConfirm;
    cancelBtn.onclick = newCancel;

    modal.style.display = "block";
}
