// wwwroot/js/operator/ticket_thread.js — realtime + send (+dedup) для оператора
(() => {
    const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    const fmt = iso => {
        try {
            const d = (iso instanceof Date) ? iso : new Date(iso);
            return new Intl.DateTimeFormat('ru-RU', {
                year: 'numeric', month: '2-digit', day: '2-digit',
                hour: '2-digit', minute: '2-digit', second: '2-digit'
            }).format(d);
        } catch { return String(iso); }
    };

    const csrfMeta = () => document.querySelector('meta[name="csrf-token"]')?.content || '';
    const aftInput = () => /** @type {HTMLInputElement|null} */(document.querySelector('input[name="__RequestVerificationToken"]'));
    const anti = () => aftInput()?.value || csrfMeta();

    async function getJson(url) {
        const res = await fetch(url, { credentials: 'same-origin', cache: 'no-store', headers: { 'X-Requested-With': 'fetch' } });
        if (!res.ok) throw new Error(`HTTP ${res.status}`);
        return await res.json();
    }
    async function postJson(url, body) {
        const token = anti();
        const res = await fetch(url, {
            method: 'POST',
            credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json; charset=utf-8', ...(token ? { 'RequestVerificationToken': token } : {}) },
            body: JSON.stringify(body ?? {})
        });
        if (!res.ok) throw new Error(`HTTP ${res.status}`);
        return await res.json().catch(() => ({}));
    }

    // У оператора: сообщения пользователя → справа (usr), оператора → слева (op)
    function renderMsg(m) {
        const isUser = (m.role === 'user');
        const cls = isUser ? 'tt-msg usr' : 'tt-msg op';
        const who = isUser ? 'Пользователь' : 'Оператор';
        const at = m.createdAt ?? m.at ?? new Date();
        return `
      <div class="${cls}" data-msg-id="${m.id ?? ''}">
        <div class="tt-bubble">
          <div class="tt-msg-text">${esc(m.body ?? m.text ?? '')}</div>
          <div class="tt-msg-meta">
            <span class="tt-msg-who">${who}</span>
            <span class="tt-msg-at">${fmt(at)}</span>
          </div>
        </div>
      </div>`;
    }

    function applyClosedState(root, closed) {
        const inputWrap = root.querySelector('.tt-input');
        const txt = root.querySelector('#tt_text');
        const btnSend = root.querySelector('#tt_send');
        if (closed) {
            inputWrap?.classList.add('disabled');
            if (txt) { txt.disabled = true; txt.placeholder = 'Заявка закрыта. Отправка сообщений недоступна.'; }
            if (btnSend) { btnSend.disabled = true; btnSend.classList.remove('primary'); }
        } else {
            inputWrap?.classList.remove('disabled');
            if (txt) { txt.disabled = false; txt.placeholder = 'Напишите ответ… (Ctrl+Enter — отправить)'; }
            if (btnSend) { btnSend.disabled = false; btnSend.classList.add('primary'); }
        }
    }

    // === SignalR ===
    const HUB_URL = '/hubs/ticket';
    async function loadSignalR() {
        if (window.signalR?.HubConnectionBuilder) return window.signalR;
        const srcs = [
            '/lib/microsoft/signalr/dist/browser/signalr.js',
            '/lib/microsoft/signalr/signalr.js'
        ];
        for (const src of srcs) {
            try {
                await new Promise((res, rej) => {
                    const s = document.createElement('script'); s.src = src; s.defer = true;
                    s.onload = res; s.onerror = () => rej(new Error('load ' + src));
                    document.head.appendChild(s);
                });
                if (window.signalR?.HubConnectionBuilder) return window.signalR;
            } catch { /* try next */ }
        }
        throw new Error('SignalR client not found');
    }
    async function connectAndJoin(ticketId, onMessage) {
        const signalR = await loadSignalR();
        const conn = new signalR.HubConnectionBuilder()
            .withUrl(HUB_URL, { withCredentials: true })
            .withAutomaticReconnect()
            .build();

        conn.on('message', payload => {
            try {
                if (!payload || payload.ticketId !== ticketId) return;
                const msg = payload.message || {};
                onMessage(msg);
                document.dispatchEvent(new CustomEvent('SupportTicketMessage', { detail: { ticketId, message: msg } }));
            } catch { }
        });

        await conn.start().catch(() => { });
        const tryCalls = [
            ['JoinTicketGroup', ticketId],
            ['JoinTicket', ticketId],
            ['Join', `ticket:${ticketId}`]
        ];
        for (const [m, arg] of tryCalls) {
            try { await conn.invoke(m, arg); break; } catch { }
        }
        return conn;
    }

    // === init ===
    window.initTicketThread = async function (panel) {
        if (!panel || panel.__op_tt_inited) return;
        panel.__op_tt_inited = true;

        const root = panel.querySelector('#ticket-thread') || panel;
        const ds = root.dataset || {};
        const ticketId = ds.ticketId || '—';
        const subject = ds.subject || '—';

        const titleId = root.querySelector('#tt_id');
        const dupId = root.querySelector('#tt_id_dup');
        const subjEl = root.querySelector('#tt_subject');
        const stEl = root.querySelector('#tt_status');
        const crEl = root.querySelector('#tt_created');
        const upEl = root.querySelector('#tt_updated');
        const msgBox = root.querySelector('#tt_messages');
        const wrap = root.querySelector('.tt-wrap');

        const txt = root.querySelector('#tt_text');
        const btnSend = root.querySelector('#tt_send');
        const btnBack = root.querySelector('#tt_back');

        titleId && (titleId.textContent = ticketId);
        dupId && (dupId.textContent = ticketId);
        subjEl && (subjEl.textContent = subject || '—');

        // 1) загрузка истории
        let ticket, messages;
        try {
            const data = await getJson(`/api/support/tickets/${encodeURIComponent(ticketId)}`);
            ticket = data.ticket || {};
            messages = Array.isArray(data.messages) ? data.messages : [];
        } catch (e) {
            console.error('[op-ticket] load failed:', e);
            msgBox && (msgBox.innerHTML = `<div class="muted" style="padding:8px">Ошибка загрузки данных заявки.</div>`);
            return;
        }

        const statusRu = ticket.status === 'closed' ? 'Закрыта' : 'Открыта';
        stEl && (stEl.textContent = statusRu);
        if (ticket.status === 'closed') stEl?.classList.add('tt-status-closed');
        crEl && (crEl.textContent = ticket.createdAt ? fmt(ticket.createdAt) : '—');
        upEl && (upEl.textContent = ticket.updatedAt ? fmt(ticket.updatedAt) : '—');

        if (msgBox) {
            msgBox.innerHTML = messages.map(m => renderMsg(m)).join('');
            if (wrap) wrap.scrollTop = wrap.scrollHeight;
        }
        applyClosedState(root, ticket.status === 'closed');

        // --- dedup state ---
        const receivedIds = new Set(); // пришли по SignalR
        const sentIds = new Set();     // мы отправили (ожидаем эхо)

        // 2) отправка
        async function sendNow() {
            const v = (txt?.value || '').trim();
            if (!v || ticket.status === 'closed') return;
            try {
                const res = await postJson(`/api/support/tickets/${encodeURIComponent(ticketId)}/messages`, { body: v });
                const id = res?.id;
                const at = res?.at || new Date().toISOString();

                // Если пуш уже успел прийти с этим id — ничего не рисуем (уже есть в DOM)
                if (id && receivedIds.has(id)) {
                    receivedIds.delete(id);
                } else {
                    // Иначе рисуем локально своё сообщение и помечаем id,
                    // чтобы игнорировать возможный последующий эхо-пуш.
                    const mineMsg = { id, role: 'agent', body: v, createdAt: at };
                    msgBox.insertAdjacentHTML('beforeend', renderMsg(mineMsg));
                    if (id) sentIds.add(id);
                    if (wrap) wrap.scrollTop = wrap.scrollHeight;
                }

                txt.value = '';
                upEl && (upEl.textContent = fmt(at));
            } catch (e) {
                console.error('[op-ticket] post failed:', e);
                alert('Не удалось отправить сообщение. Попробуйте ещё раз.');
            }
        }
        function onKey(e) { if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) { e.preventDefault(); sendNow(); } }
        txt?.addEventListener('keydown', onKey);
        btnSend?.addEventListener('click', sendNow);

        // 3) realtime подписка
        let conn = null;
        try {
            conn = await connectAndJoin(ticketId, (m) => {
                const id = m?.id;
                if (id) {
                    // если это эхо собственного отправления — игнорируем
                    if (sentIds.has(id)) { sentIds.delete(id); return; }
                    receivedIds.add(id);
                }
                msgBox.insertAdjacentHTML('beforeend', renderMsg(m));
                upEl && (upEl.textContent = fmt(m.createdAt ?? new Date()));
                if (wrap) wrap.scrollTop = wrap.scrollHeight;
            });
        } catch (e) {
            console.warn('[op-ticket] realtime off:', e?.message || e);
        }

        // back
        function goBack() {
            const exists = id => !!document.querySelector(`.sidebar .sidebar-button[data-content="${id}"]`);
            const fromId = ds.from || 'user_tickets';
            const backup = (typeof window.__support_backTarget === 'string' && window.__support_backTarget) ? window.__support_backTarget : '';
            const target = (fromId && exists(fromId)) ? fromId : (backup && exists(backup)) ? backup : 'user_tickets';
            if (typeof window.selectSidebarByContentId === 'function') window.selectSidebarByContentId(target); else history.back();
        }
        btnBack?.addEventListener('click', goBack);

        // dispose
        panel.__dispose = function () {
            try { txt?.removeEventListener('keydown', onKey); } catch { }
            try { btnSend?.removeEventListener('click', sendNow); } catch { }
            try { btnBack?.removeEventListener('click', goBack); } catch { }
            try { conn?.stop(); } catch { }
        };
    };
})();
