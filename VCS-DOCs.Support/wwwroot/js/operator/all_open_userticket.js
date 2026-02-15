(() => {
    const USE_MOCK = /[?&]mock=1\b/i.test(location.search);

    // --- role info ---
    let IS_ADMIN = false;
    let SELF_ID = null;

    const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({
        '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
    }[c]));

    async function getJson(url) {
        const r = await fetch(url, { credentials: 'same-origin', cache: 'no-store' });
        if (!r.ok) { const e = new Error('HTTP ' + r.status); e.status = r.status; throw e; }
        return r.json();
    }
    async function safeJson(resp) { try { return await resp.json(); } catch { return null; } }

    async function loadMe() {
        try {
            const me = await getJson('/api/ops/accounts/me');
            IS_ADMIN = !!me.isAdmin;
            SELF_ID = me.id || null;
        } catch {
            IS_ADMIN = false;
            SELF_ID = null;
        }
    }

    const csrf = () => document.querySelector('meta[name="csrf-token"]')?.content || '';

    // --- notifications (назначили меня) ---
    const ensureAssignStyles = () => {
        if (document.getElementById('op-assign-style')) return;
        const s = document.createElement('style');
        s.id = 'op-assign-style';
        s.textContent = `
          .op-assign-toast-wrap { position: fixed; top: 16px; right: 16px; z-index: 2000; display: flex; flex-direction: column; gap: 10px; pointer-events: none; }
          .op-assign-toast { background: #1f2a3d; color: #fff; padding: 12px 14px; border-radius: 10px; box-shadow: 0 10px 30px rgba(0,0,0,0.35); border-left: 4px solid #4caf50; min-width: 260px; max-width: 360px; pointer-events: auto; }
          .op-assign-toast h4 { margin: 0 0 4px; font-size: 15px; }
          .op-assign-toast p { margin: 0 0 8px; font-size: 13px; opacity: .9; }
          .op-assign-toast a { color: #8bd1ff; text-decoration: underline; font-weight: 600; }
          .op-assign-badge { background: #ff7043; color: #fff; border-radius: 12px; padding: 2px 8px; font-size: 12px; min-width: 20px; text-align: center; margin-left: 8px; display: inline-block; }
        `;
        document.head.appendChild(s);
    };

    const ensureToastHost = () => {
        let wrap = document.querySelector('.op-assign-toast-wrap');
        if (!wrap) {
            wrap = document.createElement('div');
            wrap.className = 'op-assign-toast-wrap';
            document.body.appendChild(wrap);
        }
        return wrap;
    };

    function playAssignSound() {
        try {
            const AudioCtx = window.AudioContext || window.webkitAudioContext;
            if (!AudioCtx) return;

            const ctx = new AudioCtx();
            const t0 = ctx.currentTime;

            const master = ctx.createGain();
            master.gain.setValueAtTime(0.0001, t0);
            master.connect(ctx.destination);

            // чуть "воздуха" через фильтр
            const filter = ctx.createBiquadFilter();
            filter.type = 'lowpass';
            filter.frequency.setValueAtTime(2200, t0);
            filter.Q.setValueAtTime(0.7, t0);
            filter.connect(master);

            // helper: нота с ADSR
            function note(freq, start, dur, peak = 0.12) {
                const osc = ctx.createOscillator();
                const g = ctx.createGain();

                osc.type = 'sine';
                osc.frequency.setValueAtTime(freq, start);

                // ADSR: атака быстро, спад мягко
                g.gain.setValueAtTime(0.0001, start);
                g.gain.linearRampToValueAtTime(peak, start + 0.015);
                g.gain.exponentialRampToValueAtTime(0.0001, start + dur);

                osc.connect(g).connect(filter);
                osc.start(start);
                osc.stop(start + dur + 0.02);
            }

            // Две нотки (C6 -> E6), мягкий "дзынь"
            note(1046.50, t0 + 0.00, 0.22, 0.10);  // C6
            note(1318.51, t0 + 0.08, 0.26, 0.08);  // E6

            // общий мастер чуть плавнее
            master.gain.linearRampToValueAtTime(0.14, t0 + 0.02);
            master.gain.exponentialRampToValueAtTime(0.0001, t0 + 0.45);

            setTimeout(() => { try { ctx.close(); } catch { } }, 700);
        } catch { }
    }


    let assignBadgeCount = 0;

    function updateAssignBadges() {
        ensureAssignStyles();
        const targets = [];

        const sidebarBtn = document.querySelector('#btn-tickets');
        if (sidebarBtn) targets.push(sidebarBtn);

        const header = document.querySelector('.header-container');
        if (header) targets.push(header);

        targets.forEach(target => {
            let badge = target.querySelector('.op-assign-badge');
            if (!badge) {
                badge = document.createElement('span');
                badge.className = 'op-assign-badge';

                if (target.classList.contains('header-container')) {
                    const title = target.querySelector('.header-title');
                    if (title) title.insertAdjacentElement('afterend', badge);
                    else target.appendChild(badge);
                } else {
                    target.appendChild(badge);
                }
            }

            badge.textContent = assignBadgeCount > 99 ? '99+' : String(assignBadgeCount);
            badge.style.display = assignBadgeCount > 0 ? 'inline-block' : 'none';
        });
    }

    function bumpAssignBadge() {
        assignBadgeCount += 1;
        updateAssignBadges();
    }

    function showAssignToast(ticketId, subject) {
        ensureAssignStyles();
        const host = ensureToastHost();
        const toast = document.createElement('div');
        toast.className = 'op-assign-toast';

        const safeSubject = esc(subject || '');
        toast.innerHTML = `
          <h4>Заявка назначена вам</h4>
          <p>#${esc(ticketId)}${safeSubject ? ': ' + safeSubject : ''}</p>
          <a href="/Content/Operators/ticket/${encodeURIComponent(ticketId)}">Открыть</a>
        `;

        toast.querySelector('a')?.addEventListener('click', (e) => {
            e.preventDefault();
            if (typeof window.openTicket === 'function') {
                window.openTicket({ id: ticketId, subject: subject || '', fromId: 'user_tickets' });
            } else {
                location.href = `/Content/Operators/ticket/${encodeURIComponent(ticketId)}`;
            }
        });

        host.prepend(toast);
        playAssignSound();
        setTimeout(() => { try { toast.remove(); } catch { } }, 20000);
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
                    const s = document.createElement('script');
                    s.src = src;
                    s.defer = true;
                    s.onload = res;
                    s.onerror = () => rej(new Error('load ' + src));
                    document.head.appendChild(s);
                });
                if (window.signalR?.HubConnectionBuilder) return window.signalR;
            } catch { }
        }
        throw new Error('SignalR client not found');
    }

    // onCreated — 4-й колбэк
    async function ensureConn(onMessage, onStatus, onAssigned, onCreated) {
        if (conn && (conn.state === 'Connected' || conn.state === 1)) return conn;

        const signalR = await loadSignalR();
        conn = new signalR.HubConnectionBuilder()
            .withUrl(HUB_URL, { withCredentials: true })
            .withAutomaticReconnect()
            .build();

    // Re-sync once after SignalR reconnect (no polling)
    conn.onreconnected(() => {
        try { document.dispatchEvent(new CustomEvent('supportTickets:hubReconnected')); } catch { }
    });



        conn.on('message', payload => {
            try {
                const id = payload?.ticketId;
                const msg = payload?.message || {};
                if (!id) return;
                onMessage?.(id, msg);
                document.dispatchEvent(new CustomEvent('SupportTicketMessage', { detail: { ticketId: id, message: msg } }));
            } catch { }
        });

        conn.on('status', payload => {
            try {
                if (!payload?.ticketId) return;
                onStatus?.(payload.ticketId, payload.status, payload.updatedAt);
                document.dispatchEvent(new CustomEvent('SupportTicketStatus', {
                    detail: { ticketId: payload.ticketId, status: payload.status, updatedAt: payload.updatedAt }
                }));
            } catch { }
        });

        conn.on('assigned', payload => {
            try {
                const id = payload?.ticketId;
                if (!id) return;
                onAssigned?.(id, payload.assignedUserId || null, payload.assignmentMode || null, payload.assignedAt || null);
            } catch { }
        });

        // пуш создания тикета
        conn.on('created', payload => {
            try { onCreated?.(payload || {}); } catch { }
        });

        try {
            await conn.start();
            console.info('[TicketHub] connected');
        } catch (e) {
            console.error('[TicketHub] start failed', e);
            throw e;
        }

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
                try {
                    await conn.invoke(m, arg);
                    joined.add(id);
                    break;
                } catch { }
            }
        }
    }

    // ---------- COMMON ----------
    function waitText(who) { return who === 'user' ? 'Пользователь ответил' : 'Оператор ответил'; }
    function waitCls(who) { return who === 'user' ? 'wait-user' : 'wait-operator'; }

    // для рендера “Обслуживает”
    let AGENTS = [];
    async function loadAgents() {
        try {
            const r = await fetch('/api/ops/accounts/agents', { credentials: 'same-origin' });
            const j = await r.json().catch(() => ({}));
            AGENTS = Array.isArray(j.items) ? j.items : [];
        } catch {
            AGENTS = [];
        }
    }

    function agentLabelById(idOrLogin) {
        if (!idOrLogin) return '—';
        const a = AGENTS.find(x => String(x.id) === String(idOrLogin) || String(x.login) === String(idOrLogin));
        return a ? (a.login || a.name || a.id) : idOrLogin;
    }

    function buildAssignSelect(currentId) {
        const s = document.createElement('select');
        s.className = 'assign-pick small';

        const optNone = document.createElement('option');
        optNone.value = '';
        optNone.textContent = '— не назначен —';
        s.appendChild(optNone);

        for (const a of AGENTS) {
            const o = document.createElement('option');
            o.value = a.id;
            o.textContent = a.login || a.name || a.id;
            s.appendChild(o);
        }

        s.value = currentId || '';
        return s;
    }

    async function assign(ticketId, userIdOrEmpty, tr, cell) {
        const body = JSON.stringify({ userId: userIdOrEmpty || null, mode: 'manual' });
        const r = await fetch(`/api/ops/tickets/${encodeURIComponent(ticketId)}/assign`, {
            method: 'POST',
            credentials: 'same-origin',
            headers: {
                'Content-Type': 'application/json; charset=utf-8',
                ...(csrf() ? { 'RequestVerificationToken': csrf() } : {})
            },
            body
        });

        const j = await safeJson(r);
        if (!r.ok || !j?.ok) {
            const msg = (j && (j.error || j.message)) || ('HTTP ' + r.status);
            throw new Error(msg);
        }

        tr.dataset.operator = j.assignedUserId || '';
        cell.dataset.assigned = tr.dataset.operator;
    }

    // ---------- MOCKS ----------
    function mockOpenTickets() {
        const orgs = ['ООО «Орг 1»', 'ООО «Орг 2»', 'ООО «Орг 3»', 'АО «Корпорация»'];
        const rows = [];
        for (let i = 0; i < 24; i++) {
            const id = `12${(1000 + i).toString().padStart(4, '0')}ab`;
            const wait = (i % 2 === 0) ? 'user' : 'operator';
            const assigned = i % 3 === 0;
            rows.push({
                id,
                subject: `Демо: проблема с файлом №${i + 1}`,
                userLogin: `user${(300 + i).toString().padStart(3, '0')}`,
                organization: orgs[i % orgs.length],
                wait,
                assignedUserId: assigned ? '2825' : null,
                operatorLogin: assigned ? '2825' : ''
            });
        }
        return { rows, orgs: Array.from(new Set(orgs)) };
    }

    // ---------- RENDER ----------
    function rowHtml(t) {
        const w = (t.wait === 'user') ? 'user' : 'operator';
        const assignedData = t.assignedUserId || t.operatorLogin || '';
        const operatorText = agentLabelById(assignedData);

        return `
          <tr data-id="${esc(t.id)}" data-wait="${esc(w)}" data-operator="${esc(assignedData)}" data-subject="${esc(t.subject)}">
            <td>${esc(t.id)}</td>
            <td class="tt-auto" title="${esc(t.subject)}">${esc(t.subject)}</td>
            <td>${esc(t.userLogin || '')}</td>
            <td>${esc(t.organization || '')}</td>
            <td><span class="status-badge ${waitCls(w)}">${waitText(w)}</span></td>
            <td class="assign-cell" data-assigned="${esc(assignedData)}">${esc(operatorText || '—')}</td>
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

    // после отрисовки таблицы превращаем колонку “Обслуживает” в селекты
    function upgradeAssigneeColumn(tbody) {
        tbody.querySelectorAll('tr').forEach(tr => {
            const cell = tr.querySelector('.assign-cell');
            if (!cell) return;

            const current = cell.dataset.assigned || tr.dataset.operator || '';
            const select = buildAssignSelect(current);

            cell.replaceChildren(select);

            // не-админ — только просмотр
            if (!IS_ADMIN) {
                select.disabled = true;
                select.title = 'Переназначение доступно только администратору';
                select.classList.add('is-readonly');
                return;
            }

            // админ — может менять
            select.addEventListener('change', async () => {
                const val = select.value || '';
                select.disabled = true;
                try {
                    await assign(tr.dataset.id, val, tr, cell);
                } catch (e) {
                    alert('Не удалось назначить: ' + (e.message || 'ошибка'));
                    select.value = current || '';
                } finally {
                    select.disabled = false;
                }
            });
        });
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

        let currentScope = 'all'; // all | mine | unassigned
        let currentOrg = '';
        let currentQuery = '';
        let debounce = null;


// ---- realtime fallback sync (no F5) ----
// Иногда SignalR-событие может быть пропущено (например, если тикет создан в другом инстансе или вкладка была в фоне).
// Мы не делаем постоянный polling — вместо этого обновляем список один раз после reconnection SignalR.
let loadInFlight = false;
let lastSyncKey = '';
function buildOpenTicketsUrl() {
    const url = new URL('/api/support/tickets/open', location.origin);
    url.searchParams.set('scope', currentScope);
    if (currentOrg) url.searchParams.set('org', currentOrg);
    if (currentQuery) url.searchParams.set('q', currentQuery);
    return url.toString();
}

function calcSyncKey(list) {
    // Ключ достаточно чувствительный, чтобы ловить новые тикеты/назначения/изменение wait/организации.
    return (list || []).map(r => `${r.id}|${r.wait}|${r.assignedUserId || ''}|${r.organization || ''}`).join('~');
}




        // ---- Orgs ----
        async function loadOrgs() {
            try {
                if (USE_MOCK) throw { status: 404 };
                const url = new URL('/api/support/tickets/orgs', location.origin);

                // ВАЖНО: это список ОТКРЫТЫХ тикетов => status=open (или можно вообще не передавать)
                url.searchParams.set('status', 'open');

                const list = await getJson(url.toString());
                const uniq = Array.isArray(list)
                    ? Array.from(new Set(list)).filter(Boolean).sort((a, b) => a.localeCompare(b, 'ru'))
                    : [];

                orgSel.innerHTML =
                    `<option value="">Все организации</option>` +
                    uniq.map(o => `<option value="${esc(o)}">${esc(o)}</option>`).join('');
            } catch {
                const { orgs } = mockOpenTickets();
                orgSel.innerHTML =
                    `<option value="">Все организации</option>` +
                    orgs.map(o => `<option value="${esc(o)}">${esc(o)}</option>`).join('');
            }
        }

        function normalizeRow(r) {
            return {
                id: r.id,
                subject: r.subject,
                userLogin: r.userLogin,
                organization: r.organization,
                wait: r.wait,
                assignedUserId: r.assignedUserId ?? null,
                operatorLogin: r.operatorLogin ?? ''
            };
        }

        // ---- Tickets ----
        async function loadTickets() {
            if (!tbody) return;

            loadInFlight = true;
            try {

            tbody.innerHTML = `<tr><td>Загрузка…</td></tr>`;

            await loadAgents();

            try {
                let raw;
                if (!USE_MOCK) {
                    const url = new URL('/api/support/tickets/open', location.origin);
                    url.searchParams.set('scope', currentScope);
                    if (currentOrg) url.searchParams.set('org', currentOrg);
                    if (currentQuery) url.searchParams.set('q', currentQuery);
                    raw = await getJson(url.toString());
                } else {
                    throw { status: 404 };
                }

                if (!Array.isArray(raw) || raw.length === 0) {
                    tbody.innerHTML = `<tr><td colspan="7">Нет данных</td></tr>`;
                    return;
                }

                const list = raw.map(normalizeRow);
                lastSyncKey = calcSyncKey(list);
                tbody.innerHTML = list.map(rowHtml).join('');
                upgradeAssigneeColumn(tbody);

                // Реалтайм
                try {
                    await ensureConn(
                        // message
                        (ticketId, msg) => {
                            const tr = tbody.querySelector(`tr[data-id="${CSS.escape(String(ticketId))}"]`);
                            if (!tr) return;
                            const who = (msg?.role === 'user') ? 'user' : 'operator';
                            updateBadge(tr, who);
                        },
                        // status
                        (ticketId, status) => {
                            if (status === 'closed') {
                                const tr = tbody.querySelector(`tr[data-id="${CSS.escape(String(ticketId))}"]`);
                                tr?.remove();
                            }
                        },
                        // assigned
                        (ticketId, assignedUserId /*, mode, at */) => {
                            const tr = tbody.querySelector(`tr[data-id="${CSS.escape(String(ticketId))}"]`);
                            const prevOperator = tr?.dataset?.operator || '';

                            if (tr) {
                                tr.dataset.operator = assignedUserId || '';

                                const td = tr.querySelector('.assign-cell');
                                if (td) {
                                    // если в ячейке селект — обновим его, иначе текстом
                                    const sel = td.querySelector('select');
                                    if (sel) {
                                        sel.value = assignedUserId || '';
                                    } else {
                                        td.textContent = agentLabelById(assignedUserId || '');
                                    }
                                    td.dataset.assigned = assignedUserId || '';
                                }
                            }

                            // нотификация только если назначили ИМЕННО МНЕ и раньше не был назначен на меня
                            const isSelfAssign = assignedUserId && SELF_ID && String(assignedUserId) === String(SELF_ID);
                            if (isSelfAssign && String(prevOperator) !== String(SELF_ID)) {
                                const subject = tr?.getAttribute('data-subject') || '';
                                bumpAssignBadge();
                                showAssignToast(ticketId, subject);
                            }
                        },
                        // created
                        (payload) => {
                            const id = String(payload?.id || payload?.ticketId || '');
                            if (!id) return;

                            // уже есть? выходим
                            if (tbody.querySelector(`tr[data-id="${CSS.escape(id)}"]`)) return;

                            const r = normalizeRow({
                                id,
                                subject: payload?.subject || '(без темы)',
                                userLogin: payload?.userLogin || '',
                                organization: payload?.organization || '',
                                wait: payload?.wait || 'user',
                                assignedUserId: payload?.assignedUserId ?? null,
                                operatorLogin: ''
                            });

                            // применим текущие фильтры UI
                            const q = (currentQuery || '').toLowerCase();
                            if (q) {
                                const hay = (r.id + ' ' + (r.subject || '') + ' ' + (r.userLogin || '') + ' ' + (r.organization || '')).toLowerCase();
                                if (!hay.includes(q)) return;
                            }
                            if (currentOrg && r.organization !== currentOrg) return;

                            const isAssigned = !!(r.assignedUserId || r.operatorLogin);
                            const isMine = !!(r.assignedUserId && SELF_ID && String(r.assignedUserId) === String(SELF_ID));

                            // ВАЖНО: "mine" = назначено мне, а не просто "назначено кому-то"
                            if (currentScope === 'mine' && !isMine) return;
                            if (currentScope === 'unassigned' && isAssigned) return;

                            const html = rowHtml(r);

                            // убираем "Нет данных" если он был
                            const onlyRow = tbody.children.length === 1 ? tbody.children[0] : null;
                            const onlyCell = onlyRow?.querySelector?.('td');
                            const isPlaceholder = onlyCell && onlyCell.getAttribute('colspan') === '7';

                            if (isPlaceholder) {
                                tbody.innerHTML = html;
                            } else if (tbody.firstElementChild) {
                                tbody.firstElementChild.insertAdjacentHTML('beforebegin', html);
                            } else {
                                tbody.innerHTML = html;
                            }

                            // превратить ячейку в селект (для новой строки)
                            upgradeAssigneeColumn(tbody);

                            // подписка на этот тикет в хабе
                            joinMany([id]).catch(() => { });
                        }
                    );

                    const ids = list.map(x => x.id).filter(Boolean);
                    await joinMany(ids);
                } catch {
                    // ok, без realtime
                }
            } catch {
                // fallback to mocks с фильтрами
                const { rows } = mockOpenTickets();
                const data = rows.map(normalizeRow);

                const q = (currentQuery || '').toLowerCase();
                const filtered = data.filter(r => {
                    if (q && !(r.id + ' ' + r.subject + ' ' + r.userLogin + ' ' + r.organization).toLowerCase().includes(q)) return false;
                    if (currentOrg && r.organization !== currentOrg) return false;

                    const isAssigned = !!(r.assignedUserId || r.operatorLogin);
                    const isMine = !!(r.assignedUserId && SELF_ID && String(r.assignedUserId) === String(SELF_ID));

                    if (currentScope === 'mine') return isMine;
                    if (currentScope === 'unassigned') return !isAssigned;
                    return true;
                });

                tbody.innerHTML = filtered.length
                    ? filtered.map(rowHtml).join('')
                    : `<tr><td colspan="7">Нет данных</td></tr>`;

                upgradeAssigneeColumn(tbody);
            }
            } finally {
                loadInFlight = false;
            }

        }

        // Локальные события (на всякий случай)
        document.addEventListener('SupportTicketMessage', (e) => {
            try {
                const { ticketId, message } = e.detail || {};
                const tr = tbody?.querySelector(`tr[data-id="${CSS.escape(String(ticketId))}"]`);
                if (!tr) return;
                const who = (message?.role === 'user') ? 'user' : 'operator';
                updateBadge(tr, who);
            } catch { }
        });

        document.addEventListener('SupportTicketStatus', (e) => {
            try {
                const { ticketId, status } = e.detail || {};
                if (status !== 'closed') return;
                const tr = tbody?.querySelector(`tr[data-id="${CSS.escape(String(ticketId))}"]`);
                tr?.remove();
            } catch { }
        });

        // ---- UI handlers ----
        scopeTabs?.addEventListener('click', (e) => {
            const btn = e.target.closest('.seg-btn');
            if (!btn) return;
            scopeTabs.querySelectorAll('.seg-btn').forEach(b => b.classList.remove('is-active'));
            btn.classList.add('is-active');
            currentScope = btn.dataset.scope || 'all';
            loadTickets();
        });

        orgSel?.addEventListener('change', () => {
            currentOrg = orgSel.value || '';
            loadTickets();
        });

        btnSearch?.addEventListener('click', () => {
            currentQuery = (searchBox?.value || '').trim();
            loadTickets();
        });

        const onType = () => {
            clearTimeout(debounce);
            debounce = setTimeout(() => {
                currentQuery = (searchBox?.value || '').trim();
                loadTickets();
            }, 250);
        };
        searchBox?.addEventListener('input', onType);

        // открыть тикет
        table?.addEventListener('click', (e) => {
            const btn = e.target.closest('.btn-open');
            if (!btn) return;
            const tr = btn.closest('tr');
            const id = tr?.getAttribute('data-id');
            const subject = tr?.getAttribute('data-subject') || '';
            if (id && typeof window.openTicket === 'function') {
                window.openTicket({ id, subject, fromId: 'user_tickets' });
            }
        });

        // ---- boot ----
        (async () => {
            await loadMe();
            await loadOrgs();
            await loadTickets();

    // If SignalR reconnects, refresh once to catch missed events
    document.addEventListener('supportTickets:hubReconnected', () => {
        // best-effort; loadTickets handles its own in-flight guard
        loadTickets();
    }, { passive: true });
                    })();

        panel.__dispose = function () {
            try { clearTimeout(debounce); } catch { }
        };
    };
})();
