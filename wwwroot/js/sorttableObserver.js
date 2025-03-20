const sortableObserver = new MutationObserver((mutations) => {
    document.querySelectorAll('table.sortable').forEach(table => {
        if (!table.classList.contains('sortable-processed')) {
            sorttable.makeSortable(table);
            table.classList.add('sortable-processed');
        }
    });
});

sortableObserver.observe(document.body, {
    childList: true,
    subtree: true
});

document.addEventListener('DOMContentLoaded', () => {
    sorttable.init();
});