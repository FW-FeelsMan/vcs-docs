// realtime + send + attachments + presence via SignalR (snapshot + live)
(() => {
    // ===== utils =================================================================
    const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    const fmtTime = iso => { try { return new Intl.DateTimeFormat('ru-RU', { hour: '2-digit', minute: '2-digit' }).format(new Date(iso)); } catch { return String(iso); } };
    const fmtFull = iso => { try { return new Intl.DateTimeFormat('ru-RU', { year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit' }).format(new Date(iso)); } catch { return String(iso); } };
    const csrfMeta = () => document.querySelector('meta[name="csrf-token"]')?.content || '';
    const aftInput = () => /** @type {HTMLInputElement|null} */(document.querySelector('input[name="__RequestVerificationToken"]'));
    const anti = () => aftInput()?.value || csrfMeta();
    const cssEsc = v => (window.CSS && CSS.escape) ? CSS.escape(String(v)) : String(v).replace(/[^a-zA-Z0-9_-]/g, '\\$&');

    async function getJson(url) {
        const res = await fetch(url, { credentials: 'same-origin', cache: 'no-store', headers: { 'X-Requested-With': 'fetch' } });
        if (!res.ok) throw new Error(`HTTP ${res.status}`);
        return await res.json();
    }
    async function postJson(url, body) {
        const token = anti();
        const res = await fetch(url, {
            method: 'POST', credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json; charset=utf-8', ...(token ? { 'RequestVerificationToken': token } : {}) },
            body: JSON.stringify(body ?? {})
        });
        if (!res.ok) throw new Error(`HTTP ${res.status}`);
        return await res.json().catch(() => ({}));
    }
    async function safeJson(resp) { try { return await resp.json(); } catch { return null; } }
    function fmtSize(n) { if (n >= 1048576) return (n / 1048576).toFixed(1) + ' MB'; if (n >= 1024) return (n / 1024).toFixed(1) + ' KB'; return n + ' B'; }

    const renderAttList = list => (list || []).map(a => `<a class="tt-file" href="/api/support/files/${encodeURIComponent(a.id)}" target="_blank" rel="noopener">${esc(a.name || a.fileName || ('file-' + a.id))}</a>`).join(' ');

    const initialsFrom = (name) => {
        const s = String(name || '').trim(); if (!s) return '•';
        const parts = s.split(/\s+/).slice(0, 2); return parts.map(p => p[0]).join('').toUpperCase();
    };
    const whoDisplay = (m, mine, isOp) => m?.authorName ?? (mine ? 'Вы' : (isOp ? 'Оператор' : 'Пользователь'));

    // --- извлекаем userId автора ---
    function authorIdFrom(m) {
        const direct = m?.authorUserId || m?.authorId || m?.userId || m?.user?.id || m?.author?.id || m?.author?.userId;
        if (direct) return String(direct);
        const url = m?.authorAvatarUrl || '';
        const m1 = String(url).match(/\/avatars\/([^\/]+)\.jpg(?:\?|$)/i);
        if (m1) return m1[1];
        return null;
    }

    // ===== Presence UI ===========================================================
    function setPresence(userId, online) {
        if (!userId) return;
        document.querySelectorAll(`.tt-msg[data-author-id="${cssEsc(userId)}"] .tt-presence`)
            .forEach(dot => {
                dot.classList.toggle('online', !!online);
                dot.classList.toggle('offline', !online);
            });
    }
    function setPresenceFor(userId, online) { setPresence(userId, online); }

    // ===== SignalR ===============================================================
    const TICKET_HUB_URL = '/hubs/ticket';
    const PRESENCE_HUB_URL = '/hubs/userStatus';

    async function loadSignalR() {
        if (window.signalR?.HubConnectionBuilder) return window.signalR;
        const srcs = ['/lib/microsoft/signalr/dist/browser/signalr.js', '/lib/microsoft/signalr/signalr.js'];
        for (const src of srcs) {
            try {
                await new Promise((res, rej) => { const s = document.createElement('script'); s.src = src; s.defer = true; s.onload = res; s.onerror = () => rej(new Error('load ' + src)); document.head.appendChild(s); });
                if (window.signalR?.HubConnectionBuilder) return window.signalR;
            } catch { }
        }
        throw new Error('SignalR client not found');
    }

    async function connectTicketHub(ticketId, onMessage) {
        const signalR = await loadSignalR();
        const conn = new signalR.HubConnectionBuilder().withUrl(TICKET_HUB_URL, { withCredentials: true }).withAutomaticReconnect().build();
        conn.on('message', payload => {
            try {
                if (!payload || payload.ticketId !== ticketId) return;
                const msg = payload.message || {};
                onMessage(msg);
                document.dispatchEvent(new CustomEvent('SupportTicketMessage', { detail: { ticketId, message: msg } }));
            } catch { }
        });
        await conn.start().catch(() => { });
        for (const [m, arg] of [['JoinTicketGroup', ticketId], ['JoinTicket', ticketId], ['Join', `ticket:${ticketId}`]]) {
            try { await conn.invoke(m, arg); break; } catch { }
        }
        return conn;
    }

    async function connectPresenceHub(authorIds, onPresence) {
        if (!Array.isArray(authorIds) || authorIds.length === 0) return null;
        const signalR = await loadSignalR();
        const conn = new signalR.HubConnectionBuilder().withUrl(PRESENCE_HUB_URL, { withCredentials: true }).withAutomaticReconnect().build();

        const handle = p => {
            try {
                const uid = p?.userId || p?.uid || p?.id || p?.UserId || p?.Id;
                const online = !!(p?.online ?? p?.isOnline);
                if (uid != null) onPresence(String(uid), online);
            } catch { }
        };

        conn.on('Presence', handle);
        conn.on('presence', handle);

        await conn.start().catch(() => { });

        // подписка + моментальный снимок (сервер шлёт в ответ)
        try { await conn.invoke('WatchUsers', authorIds); } catch { }

        return conn;
    }

    // ===== Attachments ===========================================================
    const inflight = new Set();
    function track(p, onChange) { inflight.add(p); onChange?.(); p.finally(() => { inflight.delete(p); onChange?.(); }); return p; }
    function ensureStatusEl(container) {
        let st = container?.querySelector?.('.tt-attach-status');
        if (!st && container) { st = document.createElement('div'); st.className = 'tt-attach-status muted'; st.style.fontSize = '12px'; st.style.marginTop = '4px'; container.appendChild(st); }
        return st;
    }
    async function uploadSelected(uploadUrl, files, csrf, pending, renderList, onInflightChange) {
        if (!files || !files.length) return;
        const fd = new FormData(); Array.from(files).forEach(f => fd.append("files", f, f.name));
        const p = (async () => {
            let resp;
            try { resp = await fetch(uploadUrl, { method: "POST", body: fd, headers: { "RequestVerificationToken": csrf } }); }
            catch (e) { console.error('upload fetch failed', e); alert('Сеть недоступна или соединение оборвалось.'); return; }
            if (!resp.ok) { const t = await safeJson(resp); alert(`Ошибка загрузки: ${(t && (t.error || t.message)) || ('HTTP ' + resp.status)}`); return; }
            const data = await resp.json().catch(() => null);
            if (data?.ok && Array.isArray(data.files)) { for (const f of data.files) pending.push(f); renderList(); }
        })();
        return track(p, onInflightChange);
    }
    async function bindAttachments(bindUrl, csrf, pending, messageId, onCleared) {
        if (!pending.length) return;
        const ids = pending.map(p => p.id);
        let resp;
        try {
            resp = await fetch(bindUrl, { method: "POST", headers: { "Content-Type": "application/json", "RequestVerificationToken": csrf }, body: JSON.stringify({ attachmentIds: ids, messageId }) });
        } catch (e) { console.warn('bind fetch failed', e); return; }
        if (!resp.ok) { const t = await safeJson(resp); console.warn('bind failed', t); return; }
        pending.length = 0; onCleared?.();
    }

    // ===== INIT ===================================================================
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

        const MAX_LEN = 1500;
        if (txt) { txt.setAttribute('maxlength', String(MAX_LEN)); txt.placeholder = 'Напишите ответ… (Ctrl+Enter — отправить, ≤ 1500 символов)'; }

        // attach UI
        const btnAttach = root.querySelector('#tt_attach');
        const inpFiles = root.querySelector('#tt_files');
        const list = root.querySelector('#tt_attach_list');
        const statusEl = ensureStatusEl(list);
        const csrf = anti();
        const uploadUrl = `/api/user/tickets/${encodeURIComponent(ticketId)}/files`;
        const bindUrl = `/api/user/tickets/${encodeURIComponent(ticketId)}/files/bind`;
        const pending = [];

        function setSendDisabled(disabled /*, text */) {
            if (!btnSend) return;
            btnSend.disabled = !!disabled;     
        }

        function onInflightChange() {
            const n = inflight.size;
            if (statusEl) statusEl.textContent = n > 0 ? `Загрузка файлов… (${n})` : '';
            setSendDisabled(n > 0, 'Ждём файлы…');
        }
        function renderList() {
            if (!list) return;
            list.innerHTML = pending.map(p => `<span class="tt-attach-pill"><b>${esc(p.name)}</b><small>${fmtSize(p.size)}</small><button title="Убрать" data-id="${p.id}">×</button></span>`).join("");
            list.appendChild(statusEl);
            list.querySelectorAll("button[data-id]").forEach(btn => {
                btn.addEventListener("click", () => {
                    const id = btn.dataset.id;
                    const i = pending.findIndex(x => String(x.id) === id);
                    if (i >= 0) { pending.splice(i, 1); renderList(); }
                });
            });
        }
        btnAttach?.addEventListener("click", () => inpFiles?.click());
        inpFiles?.addEventListener("change", () => {
            if (inpFiles.files?.length) uploadSelected(uploadUrl, inpFiles.files, csrf, pending, renderList, onInflightChange);
            inpFiles.value = "";
        });

        // header
        if (titleId) titleId.textContent = ticketId;
        if (dupId) dupId.textContent = ticketId;
        if (subjEl) subjEl.textContent = subject || '—';

        // load ticket
        let ticket, messages;
        try {
            const data = await getJson(`/api/support/tickets/${encodeURIComponent(ticketId)}`);
            ticket = data.ticket || {};
            messages = Array.isArray(data.messages) ? data.messages : [];
        } catch (e) {
            console.error('[user-ticket] load failed:', e);
            if (msgBox) msgBox.innerHTML = `<div class="muted" style="padding:8px">Ошибка загрузки данных заявки.</div>`;
            return;
        }

        const statusRu = ticket.status === 'closed' ? 'Закрыта' : 'Открыта';
        if (stEl) stEl.textContent = statusRu;
        if (ticket.status === 'closed') stEl?.classList.add('tt-status-closed');
        if (crEl) crEl.textContent = ticket.createdAt ? fmtFull(ticket.createdAt) : '—';
        if (upEl) upEl.textContent = ticket.updatedAt ? fmtFull(ticket.updatedAt) : '—';

        // render
        const seenIds = new Set(messages.map(m => m.id).filter(Boolean));
        function addMessageAndScroll(m, mine = false) {
            if (m && m.id && seenIds.has(m.id)) return;
            if (m && m.id) seenIds.add(m.id);
            msgBox.insertAdjacentHTML('beforeend', renderMsg(m, mine));
            if (upEl && (m?.createdAt || m?.at)) upEl.textContent = fmtFull(m.createdAt ?? m.at);
            if (wrap) wrap.scrollTop = wrap.scrollHeight;
            const aid = authorIdFrom(m);
            if (aid) setPresenceFor(aid, true);
        }

        if (msgBox) {
            msgBox.innerHTML = messages.map(m => renderMsg(m)).join('');
            if (wrap) wrap.scrollTop = wrap.scrollHeight;
        }

        applyClosedState(root, ticket.status === 'closed');

        // participants for presence
        const participantIds = new Set();
        (messages || []).forEach(m => { const aid = authorIdFrom(m); if (aid) participantIds.add(aid); });
        if (ticket?.ownerUserId) participantIds.add(String(ticket.ownerUserId));

        // send
        async function sendNow() {
            const v = (txt?.value || '').trim();
            if (ticket.status === 'closed') return;

            if (inflight.size) {
                onInflightChange();
                try { await Promise.all([...inflight]); } catch { } finally { onInflightChange(); }
            }

            if (!v && !pending.length) { alert('Добавьте текст или вложение.'); return; }
            if (v.length > 1500) { alert(`Слишком длинное сообщение (${v.length}). Максимум 1500 символов.`); return; }

            const localAtt = pending.map(p => ({ id: p.id, name: p.name }));

            try {
                const res = await postJson(`/api/support/tickets/${encodeURIComponent(ticketId)}/messages`, { body: v });
                const newId = res?.id;

                const mineMsg = {
                    id: newId, role: 'user', body: v,
                    createdAt: res?.at || new Date().toISOString(),
                    attachments: localAtt,
                    authorName: res?.authorName,
                    authorUserId: res?.authorUserId,
                    authorAvatarUrl: res?.authorAvatarUrl,
                    authorOnline: true
                };
                addMessageAndScroll(mineMsg, true);
                ensureAttachmentsInDom(msgBox, newId, localAtt);

                if (txt) txt.value = '';

                const aid = authorIdFrom(mineMsg);
                if (aid) { participantIds.add(aid); setPresenceFor(aid, true); }

                if (newId && pending.length) {
                    await bindAttachments(bindUrl, csrf, pending, newId, () => renderList());
                    ensureAttachmentsInDom(msgBox, newId, localAtt);
                } else if (!newId) {
                    pending.length = 0; renderList();
                }
            } catch (e) {
                console.error('[user-ticket] post failed:', e);
                alert('Не удалось отправить сообщение. Попробуйте ещё раз.');
            }
        }
        function onKey(e) { if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) { e.preventDefault(); sendNow(); } }
        txt?.addEventListener('keydown', onKey);
        btnSend?.addEventListener('click', sendNow);

        // SignalR: messages
        let ticketConn = null;
        try {
            ticketConn = await connectTicketHub(ticketId, (msg) => {
                addMessageAndScroll(msg, !!msg.mine);
                const aid = authorIdFrom(msg); if (aid) participantIds.add(aid);
            });
        } catch (e) { console.warn('[user-ticket] signalr(ticket) disabled:', e?.message || e); }

        // SignalR: presence (подписка + снимок)
        let presenceConn = null;
        try {
            if (participantIds.size > 0) {
                presenceConn = await connectPresenceHub([...participantIds], (uid, online) => setPresenceFor(uid, online));
            }
        } catch (e) { console.warn('[user-ticket] signalr(presence) disabled:', e?.message || e); }

        // Back
        const btnBackEl = root.querySelector('#tt_back');
        function goBack() {
            const exists = id => !!document.querySelector(`.sidebar .sidebar-button[data-content="${id}"]`);
            const fromId = ds.from || '';
            const backup = (typeof window.__support_backTarget === 'string' && window.__support_backTarget) ? window.__support_backTarget : '';
            const target = (fromId && exists(fromId)) ? fromId : (backup && exists(backup)) ? backup : 'open_tickets';
            if (typeof window.selectSidebarByContentId === 'function') window.selectSidebarByContentId(target);
            else history.back();
        }
        btnBackEl?.addEventListener('click', goBack);

        // dispose
        panel.__dispose = async function () {
            try { txt?.removeEventListener('keydown', onKey); } catch { }
            try { btnSend?.removeEventListener('click', sendNow); } catch { }
            try { btnBackEl?.removeEventListener('click', goBack); } catch { }
            if (ticketConn) { try { await ticketConn.stop(); } catch { } ticketConn = null; }
            if (presenceConn) { try { await presenceConn.stop(); } catch { } presenceConn = null; }
        };

        // export
        window.TT_Presence = { set: (authorUserId, online) => setPresenceFor(authorUserId, online) };
        window.TT_Attach = {
            bindTo: (messageId) => bindAttachments(bindUrl, csrf, pending, messageId, () => renderList()),
            getIds: () => pending.map(p => p.id),
            clear: () => { pending.length = 0; renderList(); }
        };
    };

    // ===== render =================================================================
    function renderMsg(m, mine = false) {
        const isOp = (m.role && m.role !== 'user') && !mine;
        const name = whoDisplay(m, mine, isOp);
        const atIso = m.createdAt ?? m.at ?? new Date();
        const atStr = fmtTime(atIso);
        const atFull = fmtFull(atIso);

        const avatarUrl = m.authorAvatarUrl;
        const avHtml = avatarUrl
            ? `<img src="${esc(avatarUrl)}" alt="${esc(name)}" />`
            : `<span class="initials">${esc(initialsFrom(name))}</span>`;

        const filesHtml = Array.isArray(m.attachments) && m.attachments.length
            ? `<div class="tt-files">` + m.attachments.map(a =>
                `<a class="tt-file" href="/api/support/files/${encodeURIComponent(a.id)}" target="_blank" rel="noopener">${esc(a.name || a.fileName || ('file-' + a.id))}</a>`
            ).join('') + `</div>`
            : '';

        const msgIdAttr = m.id ? ` data-msg-id="${String(m.id)}"` : '';
        const roleCls = isOp ? ' op' : ' usr';
        const authorId = authorIdFrom(m);
        const authorIdAttr = authorId ? ` data-author-id="${esc(authorId)}"` : '';

        return `
  <div class="tt-msg${roleCls}"${msgIdAttr}${authorIdAttr}>
    <div class="tt-avatar" title="${esc(name)}">
      ${avHtml}
      <span class="tt-presence offline" aria-hidden="true"></span>
    </div>
    <div class="tt-bubble">
      <div class="tt-msg-head">
        <span class="tt-name">${esc(name)}</span>
        <span class="tt-time" title="${esc(atFull)}">${esc(atStr)}</span>
      </div>
      <div class="tt-msg-text">${esc(m.body ?? m.text ?? '')}</div>
      ${filesHtml}
    </div>
  </div>`;
    }

    function ensureAttachmentsInDom(msgBox, msgId, attList) {
        if (!msgId || !Array.isArray(attList) || !attList.length) return;
        const root = msgBox.querySelector(`.tt-msg[data-msg-id="${msgId}"]`);
        if (!root) return;
        let files = root.querySelector('.tt-files');
        if (!files) {
            files = document.createElement('div');
            files.className = 'tt-files';
            const bubble = root.querySelector('.tt-bubble');
            files.innerHTML = renderAttList(attList);
            if (bubble) bubble.appendChild(files);
        } else if (!files.innerHTML.trim()) {
            files.innerHTML = renderAttList(attList);
        }
    }

    function applyClosedState(root, closed) {
        const inputWrap = root.querySelector('.tt-input');
        const txt = root.querySelector('#tt_text');
        const btnSend = root.querySelector('#tt_send');
        if (closed) {
            inputWrap?.classList.add('disabled');
            if (txt) { txt.disabled = true; txt.placeholder = 'Заявка закрыта. Отправка сообщений недоступна.'; }
            if (btnSend) btnSend.disabled = true;
        } else {
            inputWrap?.classList.remove('disabled');
            if (txt) { txt.disabled = false; txt.placeholder = 'Напишите ответ… (Ctrl+Enter — отправить, ≤ 1500 символов)'; }
            if (btnSend) btnSend.disabled = false;
        }
    }
})();