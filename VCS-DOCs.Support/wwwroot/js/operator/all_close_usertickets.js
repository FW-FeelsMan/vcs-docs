(() => {
    const USE_MOCK = /[?&]mock=1\b/i.test(location.search) || false;

    function escapeHtml(s) {
        return String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    }
    async function getJson(url) {
        const res = await fetch(url, { credentials: 'same-origin', cache: 'no-store' });
        if (!res.ok) { const e = new Error('HTTP ' + res.status); e.status = res.status; throw e; }
        return res.json();
    }

    // ---- MOCKS ----
    function mockClosedTickets() {
        const orgs = ['ООО «Орг 1»', 'ООО «Орг 2»', 'ООО «Орг 3»', 'АО «Корпорация»'];
        const rows = [];
        for (let i = 0; i < 24; i++) {
            const id = `22${(2000 + i).toString().padStart(4, '0')}zx`;
            const assigned = i % 3 === 0;
            rows.push({
                id,
                subject: `Демо: закрытая заявка №${i + 1}`,
                userLogin: `user${(200 + i).toString().padStart(3, '0')}`,
                organization: orgs[i % orgs.length],
                operatorLogin: assigned ? '2825' : '',
                createdAt: `2025-08-${(10 + (i % 9)).toString().padStart(2, '0')} 09:${(10 + i) % 60}`,
                closedAt: `2025-08-${(10 + (i % 9)).toString().padStart(2, '0')} 12:${(25 + i) % 60}`
            });
        }
        return { rows, orgs: Array.from(new Set(orgs)) };
    }

    function rowHtml(t) {
        const operatorText = (t.operatorLogin || '').trim() ? t.operatorLogin : '—';
        return `
          <tr data-id="${t.id}" data-subject="${escapeHtml(t.subject)}" data-operator="${t.operatorLogin || ''}">
            <td>${t.id}</td>
            <td class="tt-auto" title="${escapeHtml(t.subject)}">${escapeHtml(t.subject)}</td>
            <td>${escapeHtml(t.userLogin || '')}</td>
            <td>${escapeHtml(t.organization || '')}</td>
            <td><span class="status-badge closed">Закрыто</span></td>
            <td>${escapeHtml(operatorText)}</td>
            <td><button class="button-sliding small btn-open">Открыть</button></td>
          </tr>`;
    }

    window.initAllCloseUserTickets = function (panel) {
        if (panel.__op_close_inited) return;
        panel.__op_close_inited = true;

        const root = panel.querySelector("#op-close-tickets") || panel;
        const tbody = root.querySelector("#opCloseTicketsBody");
        const table = root.querySelector("#opCloseTicketsTable");
        const searchBox = root.querySelector("#op_close_searchBox");
        const btnSearch = root.querySelector("#btn-op-close-search");
        const scopeTabs = root.querySelector("#scopeTabsClose");
        const orgSel = root.querySelector("#op_close_orgFilter");

        let currentScope = "all";   // all | mine | unassigned
        let currentOrg = "";
        let currentQuery = "";
        let debounce = null;

        async function loadOrgs() {
            if (!orgSel) return;
            try {
                if (USE_MOCK) throw { status: 404 };
                const url = new URL('/api/support/tickets/orgs', location.origin);
                url.searchParams.set('status', 'closed');
                const list = await getJson(url.toString());

                const uniq = Array.isArray(list) ? Array.from(new Set(list)).filter(Boolean).sort((a, b) => a.localeCompare(b, 'ru')) : [];
                orgSel.innerHTML = `<option value="">Все организации</option>` + uniq.map(o => `<option value="${escapeHtml(o)}">${escapeHtml(o)}</option>`).join('');
            } catch {
                const { orgs } = mockClosedTickets();
                orgSel.innerHTML = `<option value="">Все организации</option>` + orgs.map(o => `<option value="${escapeHtml(o)}">${escapeHtml(o)}</option>`).join('');
            }
        }

        async function loadTickets() {
            if (!tbody) return;
            tbody.innerHTML = `<tr><td>Загрузка…</td></tr>`;
            try {
                let list;
                if (!USE_MOCK) {
                    const url = new URL('/api/support/tickets/closed', location.origin);
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
            } catch {
                // fallback to mocks with фильтрами
                const { rows } = mockClosedTickets();
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

        // табы
        scopeTabs?.addEventListener('click', (e) => {
            const btn = e.target.closest('.seg-btn'); if (!btn) return;
            scopeTabs.querySelectorAll('.seg-btn').forEach(b => b.classList.remove('is-active'));
            btn.classList.add('is-active');
            currentScope = btn.dataset.scope || 'all';
            loadTickets();
        });
        // организация
        orgSel?.addEventListener('change', () => { currentOrg = orgSel.value || ''; loadTickets(); });
        // поиск
        btnSearch?.addEventListener('click', () => { currentQuery = (searchBox.value || '').trim(); loadTickets(); });
        searchBox?.addEventListener('input', () => {
            clearTimeout(debounce);
            debounce = setTimeout(() => { currentQuery = (searchBox.value || '').trim(); loadTickets(); }, 250);
        });

        // открытие тикета
        table?.addEventListener('click', (e) => {
            const btn = e.target.closest('.btn-open'); if (!btn) return;
            const tr = btn.closest('tr');
            const id = tr?.getAttribute('data-id');
            const subject = tr?.getAttribute('data-subject') || '';
            if (id && typeof window.openTicket === 'function') {
                window.openTicket({ id, subject, fromId: 'closed_tickets' });
            }
        });

        // init
        loadOrgs().finally(loadTickets);

        panel.__dispose = function () {
            try { clearTimeout(debounce); } catch { }
        };
    };
})();
