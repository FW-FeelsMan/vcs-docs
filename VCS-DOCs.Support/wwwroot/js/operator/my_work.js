(() => {
    const esc = (s) => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

    async function getJson(url) {
        const res = await fetch(url, { credentials: 'same-origin', cache: 'no-store' });
        if (!res.ok) { const e = new Error('HTTP ' + res.status); e.status = res.status; throw e; }
        return res.json();
    }

    const fmtDate = (val) => {
        if (!val) return '—';
        try {
            return new Intl.DateTimeFormat('ru-RU', { year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit' }).format(new Date(val));
        } catch {
            const d = new Date(val); const p = (n) => String(n).padStart(2, '0');
            return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())} ${p(d.getHours())}:${p(d.getMinutes())}`;
        }
    };

    const fmtDuration = (minutes) => {
        if (minutes == null) return '—';
        const mins = Math.max(0, Number(minutes));
        const days = Math.floor(mins / 1440);
        const hours = Math.floor((mins % 1440) / 60);
        const restMin = Math.floor(mins % 60);
        const parts = [];
        if (days) parts.push(`${days}д`);
        if (hours) parts.push(`${hours}ч`);
        if (restMin || parts.length === 0) parts.push(`${restMin}м`);
        return parts.join(' ');
    };

    const setDateInput = (input, date) => {
        if (!input || !date) return;
        const iso = date.toISOString().slice(0, 10);
        input.value = iso;
    };

    const toIsoRange = (value, endOfDay = false) => {
        if (!value) return '';
        const d = new Date(value);
        if (Number.isNaN(d.getTime())) return '';
        if (endOfDay) d.setHours(23, 59, 59, 999);
        return d.toISOString();
    };

    const mock = () => {
        const now = Date.now();
        return Array.from({ length: 8 }).map((_, i) => ({
            id: `22${(3000 + i).toString().padStart(4, '0')}zx`,
            subject: `Демо: закрытая заявка #${i + 1}`,
            organization: i % 2 === 0 ? 'ООО «Орг 1»' : 'АО «Корпорация»',
            closedAt: now - i * 3600_000 * 12,
            createdAt: now - i * 3600_000 * 36,
            resolutionMinutes: 60 + i * 12,
            replies: { user: 1 + (i % 3), op: 2 + (i % 2) }
        }));
    };

    const rowHtml = (t) => `
      <tr data-id="${esc(t.id)}" data-subject="${esc(t.subject)}">
        <td>${esc(t.id)}</td>
        <td class="tt-auto" title="${esc(t.subject)}">${esc(t.subject)}</td>
        <td>${esc(t.organization || '')}</td>
        <td>${fmtDate(t.closedAt)}</td>
        <td>${fmtDuration(t.resolutionMinutes)}</td>
        <td>
            <div class="reply-tags">
                <span class="reply-tag"><span class="dot user"></span>${(t.replies?.user ?? 0)} от пользователя</span>
                <span class="reply-tag"><span class="dot op"></span>${(t.replies?.op ?? 0)} от оператора</span>
            </div>
        </td>
        <td><button class="button-sliding small btn-open">Открыть</button></td>
      </tr>`;

    window.initMyWork = function (panel) {
        if (panel.__my_work_inited) return;
        panel.__my_work_inited = true;

        const root = panel.querySelector('#op-my-work') || panel;
        const tbody = root.querySelector('#opMyWorkBody');
        const table = root.querySelector('#opMyWorkTable');
        const searchBox = root.querySelector('#op_mywork_search');
        const btnSearch = root.querySelector('#btn-op-mywork-search');
        const inputFrom = root.querySelector('#op_mywork_from');
        const inputTo = root.querySelector('#op_mywork_to');

        let q = '';
        let from = '';
        let to = '';
        let debounce = null;

        // defaults: последние 30 дней
        const today = new Date();
        const monthAgo = new Date();
        monthAgo.setDate(today.getDate() - 30);
        setDateInput(inputTo, today);
        setDateInput(inputFrom, monthAgo);
        from = toIsoRange(inputFrom?.value, false);
        to = toIsoRange(inputTo?.value, true);

        async function loadRows() {
            if (!tbody) return;
            tbody.innerHTML = '<tr><td>Загрузка…</td></tr>';
            try {
                let list = [];
                const url = new URL('/api/ops/tickets/my-closed', location.origin);
                if (from) url.searchParams.set('from', from);
                if (to) url.searchParams.set('to', to);
                if (q) url.searchParams.set('q', q);
                try {
                    list = await getJson(url.toString());
                } catch (err) {
                    console.warn('[my-work] api fallback to mock', err);
                    list = mock();
                }

                if (!Array.isArray(list) || list.length === 0) {
                    tbody.innerHTML = '<tr><td colspan="7">Нет данных</td></tr>';
                    return;
                }
                tbody.innerHTML = list.map(rowHtml).join('');
            } catch (e) {
                console.error('[my-work] failed to load', e);
                tbody.innerHTML = '<tr><td colspan="7">Ошибка загрузки</td></tr>';
            }
        }

        btnSearch?.addEventListener('click', () => { q = (searchBox.value || '').trim(); loadRows(); });
        searchBox?.addEventListener('input', () => {
            clearTimeout(debounce);
            debounce = setTimeout(() => { q = (searchBox.value || '').trim(); loadRows(); }, 250);
        });

        inputFrom?.addEventListener('change', () => { from = toIsoRange(inputFrom.value, false); loadRows(); });
        inputTo?.addEventListener('change', () => { to = toIsoRange(inputTo.value, true); loadRows(); });

        table?.addEventListener('click', (e) => {
            const btn = e.target.closest('.btn-open'); if (!btn) return;
            const tr = btn.closest('tr');
            const id = tr?.getAttribute('data-id');
            const subject = tr?.getAttribute('data-subject') || '';
            if (id && typeof window.openTicket === 'function') {
                window.openTicket({ id, subject, fromId: 'my_work' });
            }
        });

        loadRows();

        panel.__dispose = function () {
            try { clearTimeout(debounce); } catch { }
        };
    };
})();
