// D:\Unity\VCS-DOCs\VCS-DOCs.Support\wwwroot\js\user\user_open_tickets.js
(() => {
    const USE_MOCK = /[?&]mock=1\b/i.test(location.search);

    const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    async function getJson(url) {
        const r = await fetch(url, { credentials: 'same-origin', cache: 'no-store' });
        if (!r.ok) { const e = new Error('HTTP ' + r.status); e.status = r.status; throw e; }
        return r.json();
    }
    async function postJson(url, body) {
        const token = document.querySelector('meta[name="csrf-token"]')?.content || '';
        const r = await fetch(url, {
            method: 'POST',
            credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json; charset=utf-8', ...(token ? { 'RequestVerificationToken': token } : {}) },
            body: JSON.stringify(body ?? {})
        });
        const txt = await r.text().catch(() => '');
        let json = null; try { json = txt ? JSON.parse(txt) : null; } catch { }
        if (!r.ok) throw new Error(json?.error || ('HTTP ' + r.status));
        return json || {};
    }

    const fmt = (ms) => {
        try {
            return new Intl.DateTimeFormat('ru-RU', { year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit' }).format(new Date(ms));
        } catch {
            const d = new Date(ms); const p = n => String(n).padStart(2, '0');
            return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())} ${p(d.getHours())}:${p(d.getMinutes())}`;
        }
    };

    // --- pluralization & ETA text ---
    function plural(n, forms) {
        n = Math.abs(n) % 100; const n1 = n % 10;
        if (n > 10 && n < 20) return forms[2];
        if (n1 > 1 && n1 < 5) return forms[1];
        if (n1 === 1) return forms[0];
        return forms[2];
    }
    function fmtEta(sec) {
        sec = Math.max(0, Math.round(sec));
        if (sec < 60) return `До авто-закрытия: ${sec} ${plural(sec, ['секунда', 'секунды', 'секунд'])}`;
        const min = Math.ceil(sec / 60);
        if (min < 60) return `До авто-закрытия: ${min} ${plural(min, ['минута', 'минуты', 'минут'])}`;
        const h = Math.ceil(min / 60);
        return `До авто-закрытия: ${h} ${plural(h, ['час', 'часа', 'часов'])}`;
    }

    // --- horizon from config (seconds has priority for tests) ---
    function getHorizonMs() {
        const sec = (window.SUPPORT_AUTO_CLOSE_SECONDS | 0);
        if (sec > 0) return sec * 1000;
        const hrs = (window.SUPPORT_AUTO_CLOSE_HOURS | 0);
        return hrs > 0 ? hrs * 3600 * 1000 : 0;
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
    async function ensureConn(onMessage, onStatus) {
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
        conn.on('status', payload => {
            try {
                if (!payload?.ticketId) return;
                onStatus?.(payload.ticketId, payload.status, payload.updatedAt);
                document.dispatchEvent(new CustomEvent('SupportTicketStatus', { detail: { ticketId: payload.ticketId, status: payload.status, updatedAt: payload.updatedAt } }));
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

    // ---- Моки ----
    function mockOpenForUser() {
        const now = Date.now();
        return [
            { id: '121000ab', subject: 'Проблема с входом', wait: 'operator', createdAt: now - 86400000, updatedAt: now - 3600000, notify: false, autoCloseEtaSec: 20 },
            { id: '121001ab', subject: 'Не приходит письмо', wait: 'user', createdAt: now - 7200000, updatedAt: now - 4200000, notify: true, autoCloseEtaSec: null },
            { id: '121002ab', subject: 'Доступ к отчётам', wait: 'operator', createdAt: now - 5400000, updatedAt: now - 1800000, notify: false, autoCloseEtaSec: 12 },
        ];
    }

    function badgeTxt(who) { return who === 'user' ? 'Пользователь ответил' : 'Оператор ответил'; }
    function badgeCls(who) { return who === 'user' ? 'wait-user' : 'wait-operator'; }

    function rowHtml(t) {
        const w = (t.wait === 'user') ? 'user' : 'operator';

        // --- вычисляем, надо ли показывать ETA ---
        const horizonMs = getHorizonMs();
        let etaHtml = '';
        let showEta = false;

        // 1) если сервер прислал точные секунды — используем их
        if (w === 'operator' && t.autoCloseEtaSec != null) {
            showEta = true;
            const etaAttr = `data-eta="${t.autoCloseEtaSec}"`;
            etaHtml = `<div class="auto-eta muted" ${etaAttr} style="margin-top:4px;"></div>`;
        }
        // 2) fallback: сервер не прислал, но можем посчитать по updatedAt + horizon
        else if (w === 'operator' && horizonMs > 0 && t.updatedAt) {
            const u = Date.parse(t.updatedAt);
            if (!Number.isNaN(u)) {
                const deadline = u + horizonMs;
                if (deadline > Date.now()) {
                    showEta = true;
                    const dlAttr = `data-deadline="${deadline}"`;
                    etaHtml = `<div class="auto-eta muted" ${dlAttr} style="margin-top:4px;"></div>`;
                }
            }
        }

        return `
      <tr data-id="${esc(t.id)}" data-subject="${esc(t.subject)}" data-wait="${w}">
        <td>${esc(t.id)}</td>
        <td class="tt-auto" title="${esc(t.subject)}">${esc(t.subject)}</td>
        <td>
          <span class="status-badge ${badgeCls(w)}">${badgeTxt(w)}</span>
          ${showEta ? etaHtml : ''}
        </td>
        <td>${fmt(t.createdAt)}</td>
        <td class="col-updated">${fmt(t.updatedAt)}</td>
        <td>
          <label class="checkbox notify-wrapper">
            <input class="custom-checkbox notify-toggle" type="checkbox" ${t.notify ? 'checked' : ''} />
            <span class="notify-state">${t.notify ? 'включено' : 'отключено'}</span>
          </label>
        </td>
        <td><button class="button-sliding primary small btn-view">Просмотр</button></td>
      </tr>`;
    }

    // --- ETA: инициализация и периодический апдейт ---
    function setupEta(container) {
        // из data-eta (сек) — выставляем deadline = now + eta (даже если 0!)
        container.querySelectorAll('.auto-eta[data-eta]').forEach(el => {
            let eta = parseInt(el.getAttribute('data-eta') || '', 10);
            if (!isFinite(eta)) return;
            eta = Math.max(0, eta);
            const deadline = Date.now() + eta * 1000;
            el.setAttribute('data-deadline', String(deadline));
            el.removeAttribute('data-eta');
            el.textContent = fmtEta(eta);
            el.style.display = '';
        });

        // первичный рендер по готовому deadline
        container.querySelectorAll('.auto-eta[data-deadline]').forEach(el => {
            const dl = parseInt(el.getAttribute('data-deadline') || '', 10);
            if (!isFinite(dl)) return;
            const leftSec = Math.max(0, Math.round((dl - Date.now()) / 1000));
            el.textContent = fmtEta(leftSec);
            el.style.display = '';
        });
    }

    let etaTimer = null;
    function startEtaLoop(root, reloadFn) {
        stopEtaLoop();
        const zeroFired = new Set();

        etaTimer = setInterval(() => {
            root.querySelectorAll('.auto-eta[data-deadline]').forEach(el => {
                const dl = parseInt(el.getAttribute('data-deadline') || '', 10);
                if (!isFinite(dl)) return;
                const leftSec = Math.max(0, Math.round((dl - Date.now()) / 1000));
                el.textContent = fmtEta(leftSec);

                // если дошли до 0 — один раз попробуем мягко обновить список через 1.5с
                if (leftSec === 0) {
                    const tr = el.closest('tr');
                    const id = tr?.getAttribute('data-id');
                    if (id && !zeroFired.has(id)) {
                        zeroFired.add(id);
                        setTimeout(() => {
                            if (document.body.contains(tr)) {
                                try { reloadFn?.(); } catch { }
                            }
                        }, 1500);
                    }
                }
            });
        }, 1000);
    }
    function stopEtaLoop() {
        try { if (etaTimer) clearInterval(etaTimer); } catch { }
        etaTimer = null;
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

        // ETA: показываем, если ждём пользователя и есть horizon
        const etaEl = tr.querySelector('.auto-eta');
        const horizonMs = getHorizonMs();
        if (!etaEl) return;

        if (who === 'operator' && horizonMs > 0 && atIso) {
            const opAt = (new Date(atIso)).getTime();
            const deadline = opAt + horizonMs;
            etaEl.setAttribute('data-deadline', String(deadline));
            etaEl.textContent = fmtEta(Math.max(0, Math.round((deadline - Date.now()) / 1000)));
            etaEl.style.display = '';
        } else {
            etaEl.style.display = 'none';
            etaEl.removeAttribute('data-deadline');
        }
    }

    // ---------- INIT ----------
    window.initUserOpenTickets = function (panel) {
        if (panel.__user_open_inited) return;
        panel.__user_open_inited = true;

        const root = panel.querySelector('#user-open-tickets') || panel;
        const tbody = root.querySelector('#userOpenTicketsBody');
        const table = root.querySelector('#ticketsTable');
        const searchBox = root.querySelector('#user_searchBox');
        const btnSearch = root.querySelector('#btn-search');

        // NEW: элементы модалки "Создать заявку"
        const btnCreate = root.querySelector('#btn-create-ticket');
        const modal = document.getElementById('newTicketModal');
        const modalClose = document.getElementById('newTicketClose');
        const modalBackdrop = document.getElementById('newTicketBackdrop');
        const modalFrame = document.getElementById('newTicketFrame');

        let q = '';
        let debounce = null;

        function filter(list, query) {
            if (!query) return list;
            const t = query.toLowerCase();
            return list.filter(r => (r.id + ' ' + r.subject).toLowerCase().includes(t));
        }

        async function loadRows() {
            if (!tbody) return;
            stopEtaLoop();
            tbody.innerHTML = `<tr><td>Загрузка…</td></tr>`;
            try {
                let list;
                if (!USE_MOCK) {
                    list = await getJson('/api/support/self/open');
                } else { throw { status: 404 }; }

                try { console.debug('self/open payload', list?.slice?.(0, 3)); } catch { }

                const filtered = filter(Array.isArray(list) ? list : [], q);
                tbody.innerHTML = filtered.length ? filtered.map(rowHtml).join('') : `<tr><td colspan="7">Нет данных</td></tr>`;

                setupEta(tbody);
                startEtaLoop(root, loadRows);

                // Реалтайм
                try {
                    await ensureConn(
                        (ticketId, msg) => {
                            const tr = tbody.querySelector(`tr[data-id="${ticketId}"]`);
                            if (!tr) return;
                            const who = (msg?.role === 'user') ? 'user' : 'operator';
                            setRowState(tr, who, msg?.createdAt);
                        },
                        (ticketId, status) => {
                            if (status === 'closed') {
                                tbody.querySelector(`tr[data-id="${ticketId}"]`)?.remove();
                            }
                        }
                    );
                    const ids = filtered.map(x => x.id).filter(Boolean);
                    await joinMany(ids);
                } catch { /* без realtime тоже ок */ }
            } catch {
                const list = filter(mockOpenForUser(), q);
                tbody.innerHTML = list.length ? list.map(rowHtml).join('') : `<tr><td colspan="7">Нет данных</td></tr>`;
                setupEta(tbody);
                startEtaLoop(root, loadRows);
            }
        }

        // ▼ ТУМБЛЕР УВЕДОМЛЕНИЙ
        table?.addEventListener('change', async (e) => {
            const cb = e.target && e.target.closest && e.target.closest('.notify-toggle');
            if (!cb) return;
            const tr = cb.closest('tr'); if (!tr) return;
            const id = tr.getAttribute('data-id');
            const on = !!cb.checked;
            const label = tr.querySelector('.notify-state');
            if (label) label.textContent = on ? 'включено' : 'отключено';
            try {
                await postJson('/api/support/self/notify', { ticketId: id, enabled: on });
            } catch (err) {
                cb.checked = !on;
                if (label) label.textContent = (!on) ? 'включено' : 'отключено';
                alert('Не удалось сохранить настройку уведомлений.');
            }
        });

        // Локальные события
        document.addEventListener('SupportTicketMessage', (e) => {
            try {
                const { ticketId, message } = e.detail || {};
                const tr = tbody?.querySelector(`tr[data-id="${ticketId}"]`);
                if (!tr) return;
                const who = (message?.role === 'user') ? 'user' : 'operator';
                setRowState(tr, who, message?.createdAt);
            } catch { }
        });
        document.addEventListener('SupportTicketStatus', (e) => {
            try {
                const { ticketId, status } = e.detail || {};
                if (status !== 'closed') return;
                tbody?.querySelector(`tr[data-id="${ticketId}"]`)?.remove();
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

        // --- "Создать заявку" (модалка с iframe) ---
        function openCreate() {
            const f = '/Content/Users/new_ticket';
            if (modalFrame && modalFrame.src !== f) modalFrame.src = f;
            modal?.classList.add('show');
            document.body.classList.add('modal-open');
        }
        function closeCreate(refresh = true) {
            modal?.classList.remove('show');
            document.body.classList.remove('modal-open');
            if (modalFrame) modalFrame.src = 'about:blank';
            if (refresh) loadRows();
        }
        btnCreate?.addEventListener('click', openCreate);
        modalClose?.addEventListener('click', () => closeCreate(true));
        modalBackdrop?.addEventListener('click', () => closeCreate(false));
        document.addEventListener('keydown', (e) => {
            if (e.key === 'Escape' && modal?.classList.contains('show')) closeCreate(false);
        });
        window.addEventListener('message', (e) => {
            try {
                const d = e.data || {};
                if (d && d.kind === 'support:ticket_created') {
                    closeCreate(true);
                }
            } catch { }
        });

        loadRows();

        panel.__dispose = function () {
            try { clearTimeout(debounce); } catch { }
            stopEtaLoop();
        };
    };
})();
