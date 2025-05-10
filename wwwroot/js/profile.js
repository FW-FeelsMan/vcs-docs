//profile.js скрипт для сайдбара в личном кабинете
const observer = new MutationObserver(function (mutationsList) {
    handleDomChanges();
});

const resizeObserver = new ResizeObserver(entries => {
    updateTooltips();
});

function handleDomChanges() {
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

                requestAnimationFrame(() => {
                    updateTooltips(true);
                });
            }
        });
    });

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

observer.observe(document.body, {
    childList: true,
    subtree: true
});

document.querySelectorAll('table').forEach(table => {
    resizeObserver.observe(table);
});

document.addEventListener('DOMContentLoaded', () => {
    setTimeout(() => {
        updateTooltips(true);
    }, 300); 
});

window.addEventListener('resize', () => {
    updateTooltips(true);
});