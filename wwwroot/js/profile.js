const observer = new MutationObserver(function (mutationsList) {
    handleDomChanges();
});

// Добавляем ResizeObserver для отслеживания изменений размеров
const resizeObserver = new ResizeObserver(entries => {
    updateTooltips();
});

function handleDomChanges() {
    // Обработка меню
    const menuItems = document.querySelectorAll('.sidebar-menu li');
    const contentSections = document.querySelectorAll('.content-section');

    menuItems.forEach(item => {
        item.addEventListener('click', function () {
            menuItems.forEach(i => i.classList.remove('active'));
            this.classList.add('active');
            contentSections.forEach(section => section.classList.remove('active'));
            const targetSection = document.getElementById(this.dataset.target);
            if (targetSection) {
                targetSection.classList.add('active');

                // Форсируем обновление после изменения контента
                requestAnimationFrame(() => {
                    updateTooltips(true);
                });
            }
        });
    });

    // Обновление подсказок
    requestAnimationFrame(() => {
        updateTooltips(true);
    });
}

function updateTooltips(force = false) {
    document.querySelectorAll('.cell-content').forEach(cell => {
        const isOverflow = cell.scrollWidth > cell.clientWidth;
        const currentText = cell.textContent.trim();

        if (force || (isOverflow && cell.title !== currentText)) {
            cell.title = isOverflow ? currentText : '';
        }
    });
}

// Инициализация
observer.observe(document.body, {
    childList: true,
    subtree: true
});

// Отслеживаем изменения размеров для всех таблиц
document.querySelectorAll('table').forEach(table => {
    resizeObserver.observe(table);
});

// Первоначальная настройка с задержкой
document.addEventListener('DOMContentLoaded', () => {
    setTimeout(() => {
        updateTooltips(true);
    }, 300); // Даем время на полную отрисовку
});

// Обновление при изменении размера окна
window.addEventListener('resize', () => {
    updateTooltips(true);
});