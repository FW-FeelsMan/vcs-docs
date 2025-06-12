// Updated task-manager.js
window.currentUploadHash = null;
window.isUploadCanceled = false;

const TASK_REFRESH_INTERVAL_MS = 60000;

window.taskManager = (function () {
    const tasks = [];
    const removedTasks = new Map();

    function getAllTaskContainers() {
        return [
            ...document.querySelectorAll(".tasks-grid#taskCardList"),
            ...document.querySelectorAll(".tasks-grid#taskCardListChat")
        ];
    }

    function render() {
        const containers = getAllTaskContainers();
        if (containers.length === 0) {
            console.warn("[TaskManager] Контейнеры задач не найдены — отложим render()");
            setTimeout(render, 300);
            return;
        }

        for (const taskListEl of containers) {
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
                    const titleEl = existingCard.querySelector(".task-title");
                    if (titleEl && titleEl.textContent !== task.title) {
                        titleEl.textContent = task.title;
                    }

                    const statusEl = existingCard.querySelector(".task-status");
                    if (statusEl) {
                        if (!task.nextRunUtc && statusEl.textContent !== task.statusText) {
                            statusEl.textContent = task.statusText;
                        }
                        const desiredClass = `task-status ${task.statusClass}`;
                        if (statusEl.className !== desiredClass) {
                            statusEl.className = desiredClass;
                            statusEl.style = task.statusClass === 'waiting'
                                ? 'color: var(--primary-color); background: none;'
                                : '';
                        }
                    }

                    const buttonsContainer = existingCard.querySelector(".task-buttons");
                    if (buttonsContainer) {
                        const cancelBtn = buttonsContainer.querySelector(".task-cancel-btn");
                        if (cancelBtn) {
                            const shouldDisable = ['done', 'failed', 'canceled'].includes(task.statusClass);
                            cancelBtn.disabled = shouldDisable;
                            cancelBtn.textContent = shouldDisable ? "Завершено" : "Отменить";
                            cancelBtn.style.opacity = shouldDisable ? "0.5" : "";
                            cancelBtn.style.cursor = shouldDisable ? "not-allowed" : "";
                        }

                        const triggerBtn = buttonsContainer.querySelector(".task-trigger-btn");
                        if (triggerBtn && !triggerBtn.dataset.bound) {
                            triggerBtn.dataset.bound = "true";
                            triggerBtn.addEventListener("click", () => {
                                triggerBtn.disabled = true;
                                triggerBtn.style.opacity = "0.5";
                                triggerBtn.style.cursor = "not-allowed";

                                setTimeout(() => {
                                    triggerBtn.disabled = false;
                                    triggerBtn.style.opacity = "";
                                    triggerBtn.style.cursor = "";
                                }, 3000);
                            });
                        }
                    }
                } else {
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

                    taskListEl.appendChild(card);

                    const triggerBtn = card.querySelector(".task-trigger-btn");
                    if (triggerBtn) {
                        triggerBtn.dataset.bound = "true";
                        triggerBtn.addEventListener("click", () => {
                            triggerBtn.disabled = true;
                            triggerBtn.style.opacity = "0.5";
                            triggerBtn.style.cursor = "not-allowed";

                            setTimeout(() => {
                                triggerBtn.disabled = false;
                                triggerBtn.style.opacity = "";
                                triggerBtn.style.cursor = "";
                            }, 3000);
                        });
                    }
                }
            });

            domCards.forEach((card, key) => {
                if (!seenKeys.has(key)) card.remove();
            });
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

    return {
        render,
        addTask: task => {
            const existing = tasks.find(t => t.taskKey === task.taskKey);
            if (existing) Object.assign(existing, task);
            else tasks.push(task);
            
            render();
        },
        getTasks: () => tasks
    };
})();
