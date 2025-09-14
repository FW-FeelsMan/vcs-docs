// wwwroot/js/user/user_closed_tickets.js
(() => {
    const USE_MOCK = /[?&]mock=1\b/i.test(location.search);

    const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    async function getJson(url) {
        const r = await fetch(url, { credentials: 'same-origin', cache: 'no-store' });
        if (!r.ok) { const e = new Error('HTTP ' + r.status); e.status = r.status; throw e; }
        return r.json();
    }

    function mockClosedForUser() {
        const now = Date.now();
        return [
            { id: '221000zx', subject: 'Демо: закрытая заявка №1', createdAt: now - 86400000 * 3, updatedAt: now - 86400000 * 2 },
            { id: '221001zx', subject: 'Демо: закрытая заявка №2', createdAt: now - 86400000 * 5, updatedAt: now - 86400000 * 4 },
        ];
    }

    const fmt = (ms) => {
        try {
            return new Intl.DateTimeFormat('ru-RU', { year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit' }).format(new Date(ms));
        } catch {
            const d = new Date(ms); const p = n => String(n).padStart(2, '0');
            return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())} ${p(d.getHours())}:${p(d.getMinutes())}`;
        }
    };

    function rowHtml(t) {
        return `
      <tr data-id="${esc(t.id)}" data-subject="${esc(t.subject)}">
        <td>${esc(t.id)}</td>
        <td class="tt-auto" title="${esc(t.subject)}">${esc(t.subject)}</td>
        <td><span class="status-badge closed">Закрыто</span></td>
        <td>${fmt(t.createdAt)}</td>
        <td>${fmt(t.updatedAt)}</td>
        <td>
          <label class="checkbox notify-wrapper">
            <input class="custom-checkbox notify-toggle" type="checkbox" disabled />
            <span class="notify-state">недоступно</span>
          </label>
        </td>
        <td><button class="button-sliding small btn-view">Просмотр</button></td>
      </tr>`;
    }

    window.initUserClosedTickets = function (panel) {
        if (panel.__user_closed_inited) return;
        panel.__user_closed_inited = true;

        const root = panel.querySelector('#user-closed-tickets') || panel;
        const tbody = root.querySelector('#userClosedTicketsBody');
        const table = root.querySelector('#ticketsTable');
        const searchBox = root.querySelector('#user_searchBox');
        const btnSearch = root.querySelector('#btn-search');

        let q = '';
        let debounce = null;

        async function loadRows() {
            if (!tbody) return;
            tbody.innerHTML = `<tr><td>Загрузка…</td></tr>`;
            try {
                let list;
                if (!USE_MOCK) {
                    list = await getJson('/api/support/self/closed');  // наш бек
                } else { throw { status: 404 }; }
                const filtered = filter(list, q);
                tbody.innerHTML = filtered.length ? filtered.map(rowHtml).join('') : `<tr><td colspan="7">Нет данных</td></tr>`;
            } catch {
                const list = mockClosedForUser();
                const filtered = filter(list, q);
                tbody.innerHTML = filtered.length ? filtered.map(rowHtml).join('') : `<tr><td colspan="7">Нет данных</td></tr>`;
            }
        }

        function filter(list, query) {
            if (!query) return list;
            const t = query.toLowerCase();
            return list.filter(r => (r.id + ' ' + r.subject).toLowerCase().includes(t));
        }

        btnSearch?.addEventListener('click', () => { q = (searchBox.value || '').trim(); loadRows(); });
        searchBox?.addEventListener('input', () => {
            clearTimeout(debounce);
            debounce = setTimeout(() => { q = (searchBox.value || '').trim(); loadRows(); }, 250);
        });

        table?.addEventListener('click', (e) => {
            const btn = e.target.closest('.btn-view'); if (!btn) return;
            const tr = btn.closest('tr'); const id = tr?.getAttribute('data-id');
            //alert(`Просмотр закрытой заявки #${id} (для BaseUser пока не реализован отдельный экран)`);
            if (id && typeof window.openUTicket === 'function') {
                window.openUTicket({ id, subject: tr?.getAttribute('data-subject') || '', fromId: 'closed_tickets' });
            }

        });

        loadRows();

        panel.__dispose = function () {
            try { clearTimeout(debounce); } catch { }
        };
    };
})();
