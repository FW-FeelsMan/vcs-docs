//contextMenu.js скрипт кастомного контекстного меню (ПКМ)
$(document).ready(function () {
    $(document).on("contextmenu", function (event) {
        if (!event.shiftKey) { 
            event.preventDefault();
        }
    });

    $(document).on("mousedown", function (event) {
        if (event.which === 3 && !event.ctrlKey) { 
            const $menu = $('.context-menu');
            $menu.fadeOut(0); 

            const pageX = Math.min(event.pageX, $(window).width() - $menu.outerWidth()); 
            const pageY = Math.min(event.pageY, $(window).height() - $menu.outerHeight());

            $menu.css({
                left: pageX + 'px',
                top: pageY + 'px'
            }).fadeIn(200); 
        } else {
            $('.context-menu').fadeOut(200); 
        }
    });

    $(document).on("click", function () {
        $('.context-menu').fadeOut(200);
    });
});
