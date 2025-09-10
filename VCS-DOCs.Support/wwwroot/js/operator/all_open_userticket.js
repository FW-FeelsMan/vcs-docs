(() => {
    const body = document.getElementById('opTicketsBody');
    const searchBox = document.getElementById('op_searchBox');
    const btnSearch = document.getElementById('btn-op-search');
    const scopeTabs = document.getElementById('scopeTabs');
    const orgSel = document.getElementById('op_orgFilter');

    let currentScope = 'all';       // all | mine | unassigned
    let currentOrg = '';
    let currentQuery = '';

    function getCsrf() {
        return document.querySelector('meta[name="csrf-token"]')?.content ?? '';
    }

    async function getJson(url) {
        const res = await fetch(url, { credentials: 'same-origin', cache: 'no-store' });
        if (!res.ok) throw new Error('HTTP ' + res.status);
        return res.json();
    }

    // заглушка: подгружаем список организаций
    async function loadOrgs() {
        try {
            // TODO: поменять на ваш эндпоинт, например /api/support/tickets/orgs
            const list = await getJson('/api/support/tickets/orgs'); // ожидаем array of strings
            const uniq = Array.isArray(list) ? Array.from(new Set(list)).filter(Boolean).sort((a, b) => a.localeCompare(b, 'ru')) : [];
            orgSel.innerHTML = `<option value="">Все организации</option>` + uniq.map(o => `<option value="${o}">${o}</option>`).join('');
        } catch {
            // fallback — оставить «Все организации»
        }
    }

    function rowHtml(t) {
        const waitCls = t.wait === 'user' ? 'wait-user' : 'wait-operator';
        const operatorText = t.operatorLogin?.trim() ? t.operatorLogin : '—';
        return `
      <tr data-id="${t.id}" data-wait="${t.wait}" data-operator="${t.operatorLogin || ''}">
        <td>${t.id}</td>
        <td class="tt-auto" title="${escapeHtml(t.subject)}">${escapeHtml(t.subject)}</td>
        <td>${escapeHtml(t.userLogin || '')}</td>
        <td>${escapeHtml(t.organization || '')}</td>
        <td><span class="status-badge ${waitCls}">${t.wait === 'user' ? 'Пользователь ответил' : 'Оператор ответил'}</span></td>
        <td>${escapeHtml(operatorText)}</td>
        <td>${escapeHtml(t.updatedAt || '')}</td>
        <td><button class="button-sliding primary small btn-open">Открыть</button></td>
      </tr>`;
    }

    function escapeHtml(s) {
        return String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    }

    async function loadTickets() {
        const url = new URL('/api/support/tickets/open', location.origin);
        url.searchParams.set('scope', currentScope);        // all|mine|unassigned
        if (currentOrg) url.searchParams.set('org', currentOrg);
        if (currentQuery) url.searchParams.set('q', currentQuery);

        body.innerHTML = `<tr><td>Загрузка…</td></tr>`;
        try {
            // ожидаем массив объектов: { id, subject, userLogin, organization, wait: 'user'|'operator', operatorLogin, updatedAt }
            const list = await getJson(url.toString());
            if (!Array.isArray(list) || list.length === 0) {
                body.innerHTML = `<tr><td colspan="8">Нет данных</td></tr>`;
                return;
            }
            body.innerHTML = list.map(rowHtml).join('');
        } catch (e) {
            body.innerHTML = `<tr><td style="color:#c33;">Ошибка загрузки</td></tr>`;
            console.error('[op tickets] load error', e);
        }
    }

    // переключатели «Все/Только мои/Неназначенные»
    scopeTabs?.addEventListener('click', (e) => {
        const btn = e.target.closest('.seg-btn');
        if (!btn) return;
        scopeTabs.querySelectorAll('.seg-btn').forEach(b => b.classList.remove('is-active'));
        btn.classList.add('is-active');
        currentScope = btn.dataset.scope || 'all';
        loadTickets();
    });

    // организация
    orgSel?.addEventListener('change', () => {
        currentOrg = orgSel.value || '';
        loadTickets();
    });

    // поиск
    btnSearch?.addEventListener('click', () => {
        currentQuery = (searchBox.value || '').trim();
        loadTickets();
    });
    let t = null;
    searchBox?.addEventListener('input', () => {
        clearTimeout(t);
        t = setTimeout(() => {
            currentQuery = (searchBox.value || '').trim();
            loadTickets();
        }, 300);
    });

    // открытие тикета
    document.addEventListener('click', (e) => {
        const btn = e.target.closest('.btn-open');
        if (!btn) return;
        const id = btn.closest('tr')?.getAttribute('data-id');
        if (!id) return;
        // TODO: заменить на реальный маршрут карточки
        // location.href = `/Support/Ticket/${id}`;
        alert(`Открыть заявку ${id}`);
    });

    // init
    loadOrgs().finally(loadTickets);
})();
