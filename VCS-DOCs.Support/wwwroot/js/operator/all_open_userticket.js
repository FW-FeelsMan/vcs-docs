// wwwroot/js/operator/all_open_userticket.js
(() => {
    const USE_MOCK = /[?&]mock=1\b/i.test(location.search);

    const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    async function getJson(url) {
        const r = await fetch(url, { credentials: 'same-origin', cache: 'no-store' });
        if (!r.ok) { const e = new Error('HTTP ' + r.status); e.status = r.status; throw e; }
        return r.json();
    }

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
        try { await conn.start(); } catch { /* ок, попробуем без реалтайма */ }
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

    // ---------- MOCKS ----------
    function mockOpenTickets() {
        const orgs = ['ООО «Орг 1»', 'ООО «Орг 2»', 'ООО «Орг 3»', 'АО «Корпорация»'];
        const rows = [];
        for (let i = 0; i < 24; i++) {
            const id = `12${(1000 + i).toString().padStart(4, '0')}ab`;
            const wait = (i % 2 === 0) ? 'user' : 'operator';  // user=последним писал пользователь
            const assigned = i % 3 === 0;
            rows.push({
                id,
                subject: `Демо: проблема с файлом №${i + 1}`,
                userLogin: `user${(300 + i).toString().padStart(3, '0')}`,
                organization: orgs[i % orgs.length],
                wait,
                operatorLogin: assigned ? '2825' : ''
            });
        }
        return { rows, orgs: Array.from(new Set(orgs)) };
    }

    function waitText(who) { return who === 'user' ? 'Пользователь ответил' : 'Оператор ответил'; }
    function waitCls(who) { return who === 'user' ? 'wait-user' : 'wait-operator'; }

    function rowHtml(t) {
        const w = (t.wait === 'user') ? 'user' : 'operator';
        const operatorText = (t.operatorLogin || '').trim() ? t.operatorLogin : '—';
        return `
          <tr data-id="${t.id}" data-wait="${w}" data-operator="${t.operatorLogin || ''}" data-subject="${esc(t.subject)}">
            <td>${t.id}</td>
            <td class="tt-auto" title="${esc(t.subject)}">${esc(t.subject)}</td>
            <td>${esc(t.userLogin || '')}</td>
            <td>${esc(t.organization || '')}</td>
            <td><span class="status-badge ${waitCls(w)}">${waitText(w)}</span></td>
            <td>${esc(operatorText)}</td>
            <td><button class="button-sliding primary small btn-open">Открыть</button></td>
          </tr>`;
    }

    function updateBadge(tr, who) {
        if (!tr) return;
        const badge = tr.querySelector('.status-badge');
        if (!badge) return;
        tr.setAttribute('data-wait', who);
        badge.classList.remove('wait-user', 'wait-operator');
        badge.classList.add(waitCls(who));
        badge.textContent = waitText(who);
    }

    // ---------- INIT ----------
    window.initAllOpenUserTickets = function (panel) {
        if (panel.__op_open_inited) return;
        panel.__op_open_inited = true;

        const root = panel.querySelector('#op-open-tickets') || panel;
        const tbody = root.querySelector('#opTicketsBody');
        const table = root.querySelector('#opTicketsTable');
        const searchBox = root.querySelector('#op_searchBox');
        const btnSearch = root.querySelector('#btn-op-search');
        const scopeTabs = root.querySelector('#scopeTabs');
        const orgSel = root.querySelector('#op_orgFilter');

        let currentScope = 'all';    // all | mine | unassigned
        let currentOrg = '';
        let currentQuery = '';
        let debounce = null;

        // ---- Orgs ----
        async function loadOrgs() {
            try {
                if (USE_MOCK) throw { status: 404 };
                const list = await getJson('/api/support/tickets/orgs'); // ожидаем string[]
                const uniq = Array.isArray(list) ? Array.from(new Set(list)).filter(Boolean).sort((a, b) => a.localeCompare(b, 'ru')) : [];
                orgSel.innerHTML = `<option value="">Все организации</option>` + uniq.map(o => `<option value="${esc(o)}">${esc(o)}</option>`).join('');
            } catch {
                const { orgs } = mockOpenTickets();
                orgSel.innerHTML = `<option value="">Все организации</option>` + orgs.map(o => `<option value="${esc(o)}">${esc(o)}</option>`).join('');
            }
        }

        // ---- Tickets ----
        async function loadTickets() {
            if (!tbody) return;
            tbody.innerHTML = `<tr><td>Загрузка…</td></tr>`;
            try {
                let list;
                if (!USE_MOCK) {
                    const url = new URL('/api/support/tickets/open', location.origin);
                    url.searchParams.set('scope', currentScope);
                    if (currentOrg) url.searchParams.set('org', currentOrg);
                    if (currentQuery) url.searchParams.set('q', currentQuery);
                    list = await getJson(url.toString());
                } else { throw { status: 404 }; }

                if (!Array.isArray(list) || list.length === 0) {
                    tbody.innerHTML = `<tr><td colspan="7">Нет данных</td></tr>`;
                    return;
                }
                tbody.innerHTML = list.map(rowHtml).join('');

                // Реалтайм: подписываемся на все тикеты из списка
                try {
                    await ensureConn((ticketId, msg) => {
                        const tr = tbody.querySelector(`tr[data-id="${ticketId}"]`);
                        if (!tr) return;
                        const who = (msg?.role === 'user') ? 'user' : 'operator';
                        updateBadge(tr, who);
                    });
                    const ids = list.map(x => x.id).filter(Boolean);
                    await joinMany(ids);
                } catch { /* ок, без realtime */ }
            } catch {
                // fallback to mocks with фильтрами
                const { rows } = mockOpenTickets();
                const q = currentQuery.toLowerCase();
                const filtered = rows.filter(r => {
                    if (q && !(r.id + ' ' + r.subject + ' ' + r.userLogin + ' ' + r.organization).toLowerCase().includes(q)) return false;
                    if (currentOrg && r.organization !== currentOrg) return false;
                    if (currentScope === 'mine' && !r.operatorLogin) return false;
                    if (currentScope === 'unassigned' && r.operatorLogin) return false;
                    return true;
                });
                tbody.innerHTML = filtered.length ? filtered.map(rowHtml).join('') : `<tr><td colspan="7">Нет данных</td></tr>`;
            }
        }

        // Локальные события из карточки (если открыта в этой же вкладке)
        document.addEventListener('SupportTicketMessage', (e) => {
            try {
                const { ticketId, message } = e.detail || {};
                const tr = tbody?.querySelector(`tr[data-id="${ticketId}"]`);
                if (!tr) return;
                const who = (message?.role === 'user') ? 'user' : 'operator';
                updateBadge(tr, who);
            } catch { }
        });

        // ---- UI handlers ----
        scopeTabs?.addEventListener('click', (e) => {
            const btn = e.target.closest('.seg-btn'); if (!btn) return;
            scopeTabs.querySelectorAll('.seg-btn').forEach(b => b.classList.remove('is-active'));
            btn.classList.add('is-active');
            currentScope = btn.dataset.scope || 'all';
            loadTickets();
        });
        orgSel?.addEventListener('change', () => { currentOrg = orgSel.value || ''; loadTickets(); });
        btnSearch?.addEventListener('click', () => { currentQuery = (searchBox.value || '').trim(); loadTickets(); });
        searchBox?.addEventListener('input', () => {
            clearTimeout(debounce);
            debounce = setTimeout(() => { currentQuery = (searchBox.value || '').trim(); loadTickets(); }, 250);
        });

        // открыть тикет
        table?.addEventListener('click', (e) => {
            const btn = e.target.closest('.btn-open'); if (!btn) return;
            const tr = btn.closest('tr');
            const id = tr?.getAttribute('data-id');
            const subject = tr?.getAttribute('data-subject') || '';
            if (id && typeof window.openTicket === 'function') {
                window.openTicket({ id, subject, fromId: 'user_tickets' });
            }
        });

        // ---- init ----
        loadOrgs().finally(loadTickets);

        panel.__dispose = function () {
            try { clearTimeout(debounce); } catch { }
            // соединение общее на приложение — не гасим
        };
    };
})();
