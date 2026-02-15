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

    const setText = (el, text) => {
        if (!el) return;
        el.textContent = text;
    };

    const rowHtml = (t) => `
      <tr data-id="${esc(t.id)}" data-subject="${esc(t.subject)}">
        <td>${esc(t.id)}</td>
        <td class="tt-auto" title="${esc(t.subject)}">${esc(t.subject)}</td>
        <td>${esc(t.organization || '')}</td>
        <td>${fmtDate(t.closedAt)}</td>
        <td>${fmtDuration(t.resolutionMinutes)}</td>
        <td>
            <div class="reply-badges">
                <span class="status-badge closed">
                    Пользователь: ${(t.replies?.user ?? 0)}
                </span>
                <span class="status-badge closed">
                    Оператор: ${(t.replies?.op ?? 0)}
                </span>
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

        const elTotalCount = root.querySelector('#mywork-total-count');
        const elAvgDuration = root.querySelector('#mywork-avg-duration');
        const elTotalReplies = root.querySelector('#mywork-total-replies');

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
                list = await getJson(url.toString());

                if (!Array.isArray(list) || list.length === 0) {
                    tbody.innerHTML = '<tr><td colspan="7">Нет данных</td></tr>';
                    setText(elTotalCount, '0');
                    setText(elAvgDuration, '—');
                    setText(elTotalReplies, '0');
                    return;
                }
                tbody.innerHTML = list.map(rowHtml).join('');

                // Сводка
                const total = list.length;
                const totalMins = list.reduce((acc, x) => acc + (Number(x.resolutionMinutes) || 0), 0);
                const avgMins = total ? Math.round(totalMins / total) : 0;
                const replies = list.reduce((acc, x) => acc + (Number(x.replies?.user) || 0) + (Number(x.replies?.op) || 0), 0);

                setText(elTotalCount, String(total));
                setText(elAvgDuration, avgMins ? fmtDuration(avgMins) : '—');
                setText(elTotalReplies, String(replies));
            } catch (e) {
                console.error('[my-work] failed to load', e);
                tbody.innerHTML = '<tr><td colspan="7">Ошибка загрузки</td></tr>';
                setText(elTotalCount, '—');
                setText(elAvgDuration, '—');
                setText(elTotalReplies, '—');
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