// wwwroot/js/user/user_open_tickets.js — realtime badge updates
(() => {
    const USE_MOCK = /[?&]mock=1\b/i.test(location.search);

    const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    async function getJson(url) {
        const r = await fetch(url, { credentials: 'same-origin', cache: 'no-store' });
        if (!r.ok) { const e = new Error('HTTP ' + r.status); e.status = r.status; throw e; }
        return r.json();
    }

    const fmt = (ms) => {
        try {
            return new Intl.DateTimeFormat('ru-RU', { year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit' }).format(new Date(ms));
        } catch {
            const d = new Date(ms); const p = n => String(n).padStart(2, '0');
            return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())} ${p(d.getHours())}:${p(d.getMinutes())}`;
        }
    };

    // ---- SignalR (реалтайм для списка) ----
    const HUB_URL = '/hubs/ticket';
    let conn = null;
    let joined = new Set();
    async function loadSignalR() {
        if (window.signalR?.HubConnectionBuilder) return window.signalR;
        const srcs = ['/lib/microsoft/signalr/dist/browser/signalr.js', '/lib/microsoft/signalr/signalr.js'];
        for (const src of srcs) {
            try {
                await new Promise((res, rej) => {
                    const s = document.createElement('script'); s.src = src; s.defer = true;
                    s.onload = res; s.onerror = () => rej(new Error('load ' + src));
                    document.head.appendChild(s);
                });
                if (window.signalR?.HubConnectionBuilder) return window.signalR;
            } catch { }
        }
        throw new Error('SignalR client not found');
    }
    async function ensureConn(onMessage) {
        if (conn && (conn.state === 'Connected' || conn.state === 1)) return conn;
        const signalR = await loadSignalR();
        conn = new signalR.HubConnectionBuilder()
            .withUrl(HUB_URL, { withCredentials: true })
            .withAutomaticReconnect()
            .build();
        conn.on('message', payload => {
            try {
                const id = payload?.ticketId; const msg = payload?.message || {};
                if (!id) return;
                onMessage(id, msg);
                document.dispatchEvent(new CustomEvent('SupportTicketMessage', { detail: { ticketId: id, message: msg } }));
            } catch { }
        });
        try { await conn.start(); } catch { }
        return conn;
    }
    async function joinMany(ids) {
        if (!conn) return;
        for (const id of ids) {
            if (!id || joined.has(id)) continue;
            const tryCalls = [
                ['JoinTicketGroup', id],
                ['JoinTicket', id],
                ['Join', `ticket:${id}`]
            ];
            for (const [m, arg] of tryCalls) {
                try { await conn.invoke(m, arg); joined.add(id); break; } catch { }
            }
        }
    }

    // ---- Моки для BaseUser (открытые) ----
    function mockOpenForUser() {
        const now = Date.now();
        return [
            { id: '121000ab', subject: 'Проблема с входом', wait: 'operator', createdAt: now - 86400000, updatedAt: now - 3600000, notify: false },
            { id: '121001ab', subject: 'Не приходит письмо', wait: 'user', createdAt: now - 7200000, updatedAt: now - 4200000, notify: true },
            { id: '121002ab', subject: 'Доступ к отчётам', wait: 'operator', createdAt: now - 5400000, updatedAt: now - 1800000, notify: false },
        ];
    }

    function badgeTxt(who) { return who === 'user' ? 'Пользователь ответил' : 'Оператор ответил'; }
    function badgeCls(who) { return who === 'user' ? 'wait-user' : 'wait-operator'; }

    function rowHtml(t) {
        const w = (t.wait === 'user') ? 'user' : 'operator';
        return `
      <tr data-id="${esc(t.id)}" data-subject="${esc(t.subject)}" data-wait="${w}">
        <td>${esc(t.id)}</td>
        <td class="tt-auto" title="${esc(t.subject)}">${esc(t.subject)}</td>
        <td><span class="status-badge ${badgeCls(w)}">${badgeTxt(w)}</span></td>
        <td>${fmt(t.createdAt)}</td>
        <td class="col-updated">${fmt(t.updatedAt)}</td>
        <td>
          <label class="checkbox notify-wrapper">
            <input class="custom-checkbox notify-toggle" type="checkbox" ${t.notify ? 'checked' : ''} disabled />
            <span class="notify-state">${t.notify ? 'включено' : 'отключены'}</span>
          </label>
        </td>
        <td><button class="button-sliding primary small btn-view">Просмотр</button></td>
      </tr>`;
    }

    function setRowState(tr, who, atIso) {
        if (!tr) return;
        tr.setAttribute('data-wait', who);
        const badge = tr.querySelector('.status-badge');
        if (badge) {
            badge.classList.remove('wait-user', 'wait-operator');
            badge.classList.add(badgeCls(who));
            badge.textContent = badgeTxt(who);
        }
        const colUp = tr.querySelector('.col-updated');
        if (colUp && atIso) {
            try { colUp.textContent = fmt(new Date(atIso).getTime()); } catch { }
        }
    }

    window.initUserOpenTickets = function (panel) {
        if (panel.__user_open_inited) return;
        panel.__user_open_inited = true;

        const root = panel.querySelector('#user-open-tickets') || panel;
        const tbody = root.querySelector('#userOpenTicketsBody');
        const table = root.querySelector('#ticketsTable');
        const searchBox = root.querySelector('#user_searchBox');
        const btnSearch = root.querySelector('#btn-search');

        let q = '';
        let debounce = null;

        function filter(list, query) {
            if (!query) return list;
            const t = query.toLowerCase();
            return list.filter(r => (r.id + ' ' + r.subject).toLowerCase().includes(t));
        }

        async function loadRows() {
            if (!tbody) return;
            tbody.innerHTML = `<tr><td>Загрузка…</td></tr>`;
            try {
                let list;
                if (!USE_MOCK) {
                    list = await getJson('/api/support/self/open'); // наш бек
                } else { throw { status: 404 }; }
                const filtered = filter(Array.isArray(list) ? list : [], q);
                if (!filtered.length) { tbody.innerHTML = `<tr><td colspan="7">Нет данных</td></tr>`; }
                else { tbody.innerHTML = filtered.map(rowHtml).join(''); }

                // Реалтайм подписка на id из таблицы
                try {
                    await ensureConn((ticketId, msg) => {
                        const tr = tbody.querySelector(`tr[data-id="${ticketId}"]`);
                        if (!tr) return;
                        const who = (msg?.role === 'user') ? 'user' : 'operator';
                        setRowState(tr, who, msg?.createdAt);
                    });
                    const ids = filtered.map(x => x.id).filter(Boolean);
                    await joinMany(ids);
                } catch { /* ок */ }
            } catch {
                const list = filter(mockOpenForUser(), q);
                tbody.innerHTML = list.length ? list.map(rowHtml).join('') : `<tr><td colspan="7">Нет данных</td></tr>`;
            }
        }

        // Локальные события из карточки (если открыта рядом)
        document.addEventListener('SupportTicketMessage', (e) => {
            try {
                const { ticketId, message } = e.detail || {};
                const tr = tbody?.querySelector(`tr[data-id="${ticketId}"]`);
                if (!tr) return;
                const who = (message?.role === 'user') ? 'user' : 'operator';
                setRowState(tr, who, message?.createdAt);
            } catch { }
        });

        // Поиск
        btnSearch?.addEventListener('click', () => { q = (searchBox.value || '').trim(); loadRows(); });
        searchBox?.addEventListener('input', () => {
            clearTimeout(debounce);
            debounce = setTimeout(() => { q = (searchBox.value || '').trim(); loadRows(); }, 250);
        });

        // Просмотр
        table?.addEventListener('click', (e) => {
            const btn = e.target.closest('.btn-view'); if (!btn) return;
            const tr = btn.closest('tr'); const id = tr?.getAttribute('data-id');
            if (id && typeof window.openUTicket === 'function') {
                window.openUTicket({ id, subject: tr?.getAttribute('data-subject') || '', fromId: 'open_tickets' });
            }
        });

        loadRows();

        panel.__dispose = function () {
            try { clearTimeout(debounce); } catch { }
            // соединение общее — не останавливаем
        };
    };
})();
