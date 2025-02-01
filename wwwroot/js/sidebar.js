document.addEventListener("DOMContentLoaded", function () {
    // Автоматически выбираем первую кнопку при загрузке
    const firstButton = document.querySelector('.sidebar-button');
    if (firstButton) {
        selectButton(firstButton);
    }
});

function selectButton(button) {
    // Убираем класс "selected" у всех кнопок
    const buttons = document.querySelectorAll('.sidebar-button');
    buttons.forEach(btn => btn.classList.remove('selected'));

    // Добавляем класс "selected" к текущей кнопке
    button.classList.add('selected');

    // Обновляем контент на основе выбранной кнопки
    const content = document.getElementById('content');
    let selectedItem;

    switch (button.id) {
        case 'button1':
            content.innerHTML = '<h1>Проекты</h1><p>Здесь информация о проектах.</p>';
            selectedItem = 'item1';
            break;
        case 'button2':
            content.innerHTML = '<h1>Инфографика</h1><p>Здесь информация об инфографике.</p>';
            selectedItem = 'item2';
            break;
        case 'button3':
            content.innerHTML = '<h1>Доп. возможности</h1><p>Здесь дополнительные возможности.</p>';
            selectedItem = 'item3';
            break;
        case 'button4':
            content.innerHTML = '<h1>Обратная связь</h1><p>Форма обратной связи.</p>';
            selectedItem = 'item4';
            break;
    }

    // Обновляем URL в адресной строке
    const newUrl = `https://vcs-docs.local:7120/?selectedItem=${selectedItem}`;
    history.pushState({ selectedItem }, '', newUrl);
}

// Обработка события "popstate" для навигации
window.addEventListener('popstate', function (event) {
    const selectedItem = event.state ? event.state.selectedItem : null;
    if (selectedItem) {
        const buttons = document.querySelectorAll('.sidebar-button');
        buttons.forEach(btn => btn.classList.remove('selected'));
        const selectedButton = document.getElementById(selectedItem);
        if (selectedButton) {
            selectButton(selectedButton);
        }
    }
});