(() => {
    // чисто декоративно, чтобы табы переключали подсветку
    const tabs = document.querySelectorAll('#scopeTabsClose .seg-btn');
    tabs.forEach(btn => {
        btn.addEventListener('click', () => {
            tabs.forEach(b => b.classList.remove('is-active'));
            btn.classList.add('is-active');
            // тут позже добавишь рефреш данных по data-scope
        });
    });

    // демо-обработчик поиска
    document.getElementById('btn-op-close-search')?.addEventListener('click', () => {
        const q = document.getElementById('op_close_searchBox')?.value?.trim() || '';
        if (!q) return;
        // пока просто подсветим кнопку — логика API будет позже
        console.log('[close-tickets] поиск по:', q);
    });
})();
