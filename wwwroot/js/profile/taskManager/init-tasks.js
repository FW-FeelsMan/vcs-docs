//init - tasks.js
(async function initGlobalTasks() {
    if (!window.userIsAuthenticated || !window.taskManager) return;

    try {
        const response = await fetch("/api/tasks/active");
        if (!response.ok) throw new Error(`Ошибка: ${response.status}`);
        const tasks = await response.json();
        tasks.forEach(task => window.taskManager.addTask(task));
        
    } catch (e) {
        console.error("[TaskManager] Ошибка при загрузке задач:", e);
    }
})();
