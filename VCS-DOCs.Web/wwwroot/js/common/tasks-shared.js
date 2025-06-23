// tasks-shared.js
export function broadcastTaskToChat(task) {
    window.dispatchEvent(new CustomEvent("taskUpdate", { detail: task }));
    console.log("[TaskManager] Отправка задачи в чат:", task);
}

export function renderChatMiniTaskCard(task) {
    const container = document.getElementById("chat-mini-tasks");
    if (!container) return;

    let el = document.getElementById(`chat-task-${task.taskKey}`);
    if (!el) {
        el = document.createElement("div");
        el.className = "chat-task";
        el.id = `chat-task-${task.taskKey}`;
        container.appendChild(el);
    }

    el.innerHTML = `
        <div class="chat-task-title">${escapeHtml(task.title)}</div>
        <div class="chat-task-progress">
            ${task.statusText}
            ${task.progress ? `(${task.progress}%)` : ""}
        </div>
        <div class="chat-task-status">${statusClassToText(task.statusClass)}</div>
    `;
}

function statusClassToText(cls) {
    switch (cls) {
        case "done": return "✅ Завершено";
        case "waiting": return "⏳ Ожидание";
        case "failed": return "❌ Ошибка";
        case "running": return "🚧 Выполняется";
        default: return cls;
    }
}

function escapeHtml(str) {
    const div = document.createElement("div");
    div.textContent = str;
    return div.innerHTML;
}
