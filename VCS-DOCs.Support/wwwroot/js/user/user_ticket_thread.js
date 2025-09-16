// wwwroot/js/user/user_ticket_thread.js — realtime + send
(() => {
    const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    const fmt = iso => {
        try {
            const d = (iso instanceof Date) ? iso : new Date(iso);
            return new Intl.DateTimeFormat('ru-RU', { year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit' }).format(d);
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

    function renderMsg(m, mine = false) {
        // у пользователя bubbles: user → справа, operator/agent → слева
        const isOp = (m.role && m.role !== 'user') && !mine;
        const cls = isOp ? 'tt-msg op' : 'tt-msg usr';
        const who = isOp ? 'Оператор' : 'Вы';
        const msgIdAttr = m.id ? ` data-msg-id="${String(m.id)}"` : '';
        return `
      <div class="${cls}"${msgIdAttr}>
        <div class="tt-bubble">
          <div class="tt-msg-text">${esc(m.body ?? m.text ?? '')}</div>
          <div class="tt-msg-meta">
            <span class="tt-msg-who">${who}</span>
            <span class="tt-msg-at">${fmt(m.createdAt ?? m.at ?? new Date())}</span>
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

    // SignalR
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
            } catch { }
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

    // init
    window.initUTicketThread = async function (panel) {
        if (!panel || panel.__u_tt_inited) return;
        panel.__u_tt_inited = true;

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

        // 1) загрузка тикета
        let ticket, messages;
        try {
            const data = await getJson(`/api/support/tickets/${encodeURIComponent(ticketId)}`);
            ticket = data.ticket || {};
            messages = Array.isArray(data.messages) ? data.messages : [];
        } catch (e) {
            console.error('[user-ticket] load failed:', e);
            msgBox && (msgBox.innerHTML = `<div class="muted" style="padding:8px">Ошибка загрузки данных заявки.</div>`);
            return;
        }

        const statusRu = ticket.status === 'closed' ? 'Закрыта' : 'Открыта';
        stEl && (stEl.textContent = statusRu);
        if (ticket.status === 'closed') stEl?.classList.add('tt-status-closed');
        crEl && (crEl.textContent = ticket.createdAt ? fmt(ticket.createdAt) : '—');
        upEl && (upEl.textContent = ticket.updatedAt ? fmt(ticket.updatedAt) : '—');

        // набор уже показанных id (чтобы не дублировать при SignalR-эхе)
        const seenIds = new Set(messages.map(m => m.id).filter(Boolean));

        function addMessageAndScroll(m, mine = false) {
            if (m && m.id && seenIds.has(m.id)) return;
            if (m && m.id) seenIds.add(m.id);
            msgBox.insertAdjacentHTML('beforeend', renderMsg(m, mine));
            if (upEl && (m?.createdAt || m?.at)) upEl.textContent = fmt(m.createdAt ?? m.at);
            if (wrap) wrap.scrollTop = wrap.scrollHeight;
        }

        if (msgBox) {
            msgBox.innerHTML = messages.map(m => renderMsg(m)).join('');
            if (wrap) wrap.scrollTop = wrap.scrollHeight;
        }

        applyClosedState(root, ticket.status === 'closed');

        // 2) отправка
        async function sendNow() {
            const v = (txt?.value || '').trim();
            if (!v || ticket.status === 'closed') return;
            try {
                const res = await postJson(`/api/support/tickets/${encodeURIComponent(ticketId)}/messages`, { body: v });
                const mineMsg = { id: res?.id, role: 'user', body: v, createdAt: res?.at || new Date().toISOString() };
                addMessageAndScroll(mineMsg, true);
                txt.value = '';
            } catch (e) {
                console.error('[user-ticket] post failed:', e);
                alert('Не удалось отправить сообщение. Попробуйте ещё раз.');
            }
        }
        function onKey(e) { if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) { e.preventDefault(); sendNow(); } }
        txt?.addEventListener('keydown', onKey);
        btnSend?.addEventListener('click', sendNow);

        // 3) SignalR: живые сообщения
        let conn = null;
        try {
            conn = await connectAndJoin(ticketId, (msg) => {
                // если это моё же только что отправленное — отфильтруем по id
                addMessageAndScroll(msg, !!msg.mine);
            });
        } catch (e) {
            console.warn('[user-ticket] signalr disabled:', e?.message || e);
        }

        // 4) Назад
        function goBack() {
            const exists = id => !!document.querySelector(`.sidebar .sidebar-button[data-content="${id}"]`);
            const fromId = ds.from || '';
            const backup = (typeof window.__support_backTarget === 'string' && window.__support_backTarget) ? window.__support_backTarget : '';
            const target = (fromId && exists(fromId)) ? fromId : (backup && exists(backup)) ? backup : 'open_tickets';
            if (typeof window.selectSidebarByContentId === 'function') {
                window.selectSidebarByContentId(target);
            } else {
                history.back();
            }
        }
        btnBack?.addEventListener('click', goBack);

        // 5) dispose
        panel.__dispose = async function () {
            try { txt?.removeEventListener('keydown', onKey); } catch { }
            try { btnSend?.removeEventListener('click', sendNow); } catch { }
            try { btnBack?.removeEventListener('click', goBack); } catch { }
            if (conn) {
                try { await conn.stop(); } catch { }
                conn = null;
            }
        };
    };
})();
