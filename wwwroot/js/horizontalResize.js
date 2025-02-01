// Элементы
const horizontalResizer = document.querySelector('.horizontal-resizer');
const topPane = document.querySelector('.top-pane');
const bottomPane = document.querySelector('.bottom-pane');
const rightPane = document.querySelector('.right-pane');

let isResizingVertical = false;

// Начало изменения высоты
horizontalResizer.addEventListener('mousedown', (e) => {
    isResizingVertical = true;
    document.body.style.cursor = 'row-resize'; // Меняем курсор на весь экран
    document.body.style.userSelect = 'none'; // Отключаем выделение текста
});

// Завершение изменения высоты
document.addEventListener('mouseup', () => {
    isResizingVertical = false;
    document.body.style.cursor = 'default';
    document.body.style.userSelect = 'auto'; // Возвращаем выделение текста
});

// Изменение высоты при движении мыши
document.addEventListener('mousemove', (e) => {
    if (!isResizingVertical) return;

    const containerHeight = rightPane.offsetHeight; // Общая высота правой панели
    const newTopHeight = e.clientY - rightPane.getBoundingClientRect().top; // Положение мыши относительно верхнего края правой панели

    // Ограничиваем минимальную/максимальную высоту
    if (newTopHeight > 50 && newTopHeight < containerHeight - 50) {
        topPane.style.height = `${newTopHeight}px`;
        bottomPane.style.flex = `1`; // Нижняя панель автоматически занимает оставшееся место
    }
});



