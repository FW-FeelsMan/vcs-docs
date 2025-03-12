const observer = new MutationObserver(function (mutationsList) {
    for (const mutation of mutationsList) {
        if (mutation.type === 'childList') {
            const menuItems = document.querySelectorAll('.sidebar-menu li');
            const contentSections = document.querySelectorAll('.content-section');
            console.log("Динамический контент загружен");

            menuItems.forEach(item => {
                item.addEventListener('click', function () {
                    console.log("Клик по пункту меню:", item);

                    // Убираем активный класс с всех пунктов меню
                    menuItems.forEach(i => i.classList.remove('active'));
                    // Добавляем активный класс на текущий пункт меню
                    item.classList.add('active');
                    console.log("Текущий активный пункт:", item);

                    // Скрываем все разделы контента
                    contentSections.forEach(section => section.classList.remove('active'));
                    console.log("Все секции скрыты");

                    // Показать соответствующий раздел контента
                    const target = item.getAttribute('data-target');
                    const targetSection = document.getElementById(target);
                    console.log("Целевая секция:", targetSection);

                    targetSection.classList.add('active');
                    console.log("Целевая секция теперь активна");
                });
            });
        }
    }
});

observer.observe(document.body, { childList: true, subtree: true });
