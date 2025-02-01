$(document).ready(function () {
    // Предотвращаем стандартное контекстное меню, кроме случая Ctrl + ПКМ
    $(document).on("contextmenu", function (event) {
        if (!event.shiftKey) { // Если Ctrl не нажат
            event.preventDefault();
        }
    });

    // Показываем пользовательское контекстное меню
    $(document).on("mousedown", function (event) {
        if (event.which === 3 && !event.ctrlKey) { // ПКМ без Ctrl
            const $menu = $('.context-menu');
            $menu.fadeOut(0); // Скрываем меню перед расчетом позиции

            const pageX = Math.min(event.pageX, $(window).width() - $menu.outerWidth()); // Учет границ окна
            const pageY = Math.min(event.pageY, $(window).height() - $menu.outerHeight());

            $menu.css({
                left: pageX + 'px',
                top: pageY + 'px'
            }).fadeIn(200); // Показываем меню
        } else {
            $('.context-menu').fadeOut(200); // Закрываем меню
        }
    });

    // Закрытие меню при клике вне него
    $(document).on("click", function () {
        $('.context-menu').fadeOut(200);
    });
});
