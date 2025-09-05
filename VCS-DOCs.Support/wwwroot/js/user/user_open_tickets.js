(() => {
    const table = document.getElementById('ticketsTable');
    const searchBox = document.getElementById('searchBox');
    const btnRefresh = document.getElementById('btn-refresh');

    const csrf = document.querySelector('meta[name="csrf-token"]')?.content || '';

    function updateNotifyState(tr) {
        const cb = tr.querySelector('.notify-toggle');
        const label = tr.querySelector('.notify-state');
        if (!label || !cb) return;
        label.textContent = cb.checked ? 'включено' : 'отключены';
    }

    // Инициализация подписей для текущих значений
    document.querySelectorAll('#ticketsTable tbody tr').forEach(updateNotifyState);

    // Открыть тикет
    table?.addEventListener('click', (e) => {
        const btn = e.target.closest('.btn-open');
        if (!btn) return;
        const tr = btn.closest('tr');
        const id = tr?.getAttribute('data-id');
        if (!id) return;
        window.location.href = `/Content/Users/user_ticket?id=${encodeURIComponent(id)}`;
    });

    // Переключение почтовых уведомлений
    table?.addEventListener('change', async (e) => {
        const cb = e.target.closest('.notify-toggle');
        if (!cb) return;
        const tr = cb.closest('tr');
        const id = tr?.getAttribute('data-id');
        updateNotifyState(tr);

        // Логика сохранения — подключим, когда появится API
        // try {
        //     await fetch(`/api/support/tickets/${encodeURIComponent(id)}/notify`, {
        //         method: 'POST',
        //         headers: {
        //             'Content-Type': 'application/json',
        //             'X-CSRF-TOKEN': csrf
        //         },
        //         body: JSON.stringify({ email: cb.checked })
        //     });
        // } catch { /* no-op */ }
    });

    // Поиск по №/теме
    searchBox?.addEventListener('input', () => {
        const term = (searchBox.value || '').toLowerCase();
        document.querySelectorAll('#ticketsTable tbody tr').forEach(tr => {
            const text = tr.innerText.toLowerCase();
            tr.style.display = text.includes(term) ? '' : 'none';
        });
    });

    // Обновить (пока заглушка)
    btnRefresh?.addEventListener('click', () => {
        // TODO: заменить на fetch('/api/support/tickets/mine?status=open') и ререндер таблицы
        // location.reload();
    });
})();
