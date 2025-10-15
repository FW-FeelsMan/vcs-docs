// wwwroot/js/operator/ticket_thread.js
// История, realtime, presence, вложения, отправка, закрытие — операторская версия с корректной загрузкой файлов
(() => {
    // ===== utils ==============================================================
    const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({
        '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
    }[c]));
    const fmtShort = iso => { try { return new Intl.DateTimeFormat('ru-RU', { hour: '2-digit', minute: '2-digit' }).format(new Date(iso)); } catch { return String(iso); } };
    const fmtFull = iso => { try { return new Intl.DateTimeFormat('ru-RU', { year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit' }).format(new Date(iso)); } catch { return String(iso); } };
    const fmtSize = n => n >= 1048576 ? (n / 1048576).toFixed(1) + ' MB' : n >= 1024 ? (n / 1024).toFixed(1) + ' KB' : n + ' B';

    const csrfMeta = () => document.querySelector('meta[name="csrf-token"]')?.content || '';
    const aftInput = () => /** @type {HTMLInputElement|null} */(document.querySelector('input[name="__RequestVerificationToken"]'));
    const anti = () => aftInput()?.value || csrfMeta();
    const cssEsc = v => (window.CSS && CSS.escape) ? CSS.escape(String(v)) : String(v).replace(/[^a-zA-Z0-9_-]/g, '\\$&');

    const msgKey = (m) => {
        const id = (m && (m.id ?? m.Id ?? m.messageId ?? m.MessageId));
        return id == null ? '' : String(id);
    };

    async function getJson(url) {
        const r = await fetch(url, { credentials: 'same-origin', cache: 'no-store', headers: { 'X-Requested-With': 'fetch' } });
        if (!r.ok) throw new Error(`HTTP ${r.status}`);
        return await r.json();
    }
    async function safeJson(resp) { try { return await resp.json(); } catch { return null; } }

    // ===== API ================================================================
    const API = {
        async getTicket(ticketId) {
            return await getJson(`/api/support/tickets/${encodeURIComponent(ticketId)}`);
        },
        async uploadFiles(ticketId, fd) {
            const hdr = anti() ? { 'RequestVerificationToken': anti() } : {};
            // операторский эндпоинт
            const url = `/api/ops/tickets/${encodeURIComponent(ticketId)}/files`;
            const resp = await fetch(url, {
                method: 'POST',
                credentials: 'same-origin',
                headers: { ...hdr, 'Accept': 'application/json' },
                body: fd,
                cache: 'no-store'
            });
            let data = null;
            try { data = await resp.json(); } catch { }
            if (!resp.ok) {
                throw new Error((data && (data.error || data.message)) || ('HTTP ' + resp.status));
            }
            const files = Array.isArray(data?.files) ? data.files : [];
            if (!files.length) throw new Error('Нет файлов в ответе сервера');
            return files;
        },
        async postReply(ticketId, text, attIds) {
            const headers = { 'Content-Type': 'application/json; charset=utf-8', ...(anti() ? { 'RequestVerificationToken': anti() } : {}) };
            let resp = await fetch(`/api/ops/tickets/${encodeURIComponent(ticketId)}/reply`, {
                method: 'POST', credentials: 'same-origin', headers,
                body: JSON.stringify({ text, attachmentIds: attIds, attachments: attIds })
            });
            if (resp.status === 404) {
                resp = await fetch(`/api/support/tickets/${encodeURIComponent(ticketId)}/messages`, {
                    method: 'POST', credentials: 'same-origin', headers,
                    body: JSON.stringify({ body: text, attachments: attIds })
                });
            }
            const j = await safeJson(resp);
            if (!resp.ok) throw new Error((j && (j.error || j.message)) || ('HTTP ' + resp.status));
            return {
                ok: !!(j?.ok ?? true),
                id: j?.messageId ?? j?.id ?? null,
                at: j?.at ?? j?.createdAt ?? new Date().toISOString(),
                authorName: j?.authorName, authorUserId: j?.authorUserId, authorAvatarUrl: j?.authorAvatarUrl
            };
        }
    };

    // ===== self / role ========================================================
    let SELF = null;
    let IS_ADMIN = false;
    async function loadMe() {
        try {
            const me = await getJson('/api/ops/accounts/me');
            IS_ADMIN = !!me.isAdmin;
            return { id: me.id || null, name: null, avatar: null };
        } catch { IS_ADMIN = false; return { id: null, name: null, avatar: null }; }
    }
    function detectSelf(msgBox) {
        if (SELF) return SELF;
        const nm = document.querySelector('meta[name="current-user-name"]')?.content || '';
        const av = document.querySelector('meta[name="current-user-avatar"]')?.content || '';
        const id = document.querySelector('meta[name="current-user-id"]')?.content || '';
        if (nm || av || id) { SELF = { name: nm || null, avatar: av || null, id: id || null }; return SELF; }
        const op = msgBox?.querySelector?.('.tt-msg.op');
        if (op) {
            const name = op.querySelector('.tt-name')?.textContent?.trim() || null;
            const avatarUrl = op.querySelector('.tt-avatar img')?.getAttribute('src') || null;
            const uid = op.getAttribute('data-author-id') || null;
            SELF = { name, avatar: avatarUrl, id: uid };
            return SELF;
        }
        SELF = { name: null, avatar: null, id: null };
        return SELF;
    }

    // ===== presence ===========================================================
    function setPresence(userId, online) {
        if (!userId) return;
        document.querySelectorAll(`.tt-msg[data-author-id="${cssEsc(userId)}"] .tt-presence`)
            .forEach(dot => {
                dot.classList.toggle('online', !!online);
                dot.classList.toggle('offline', !online);
            });
    }

    // ===== SignalR ============================================================
    const HUB_URL = '/hubs/ticket';
    const PRESENCE_HUB_URL = '/hubs/userStatus';

    async function loadSignalR() {
        if (window.signalR?.HubConnectionBuilder) return window.signalR;
        const srcs = ['/lib/microsoft/signalr/dist/browser/signalr.js', '/lib/microsoft/signalr/signalr.js'];
        for (const src of srcs) {
            try {
                await new Promise((res, rej) => {
                    const s = document.createElement('script'); s.src = src; s.defer = true; s.onload = res; s.onerror = () => rej(new Error('load ' + src)); document.head.appendChild(s);
                });
                if (window.signalR?.HubConnectionBuilder) return window.signalR;
            } catch { }
        }
        throw new Error('SignalR client not found');
    }

    async function connectTicketHub(ticketId, onMessage, onStatus) {
        const signalR = await loadSignalR();
        const conn = new signalR.HubConnectionBuilder().withUrl(HUB_URL, { withCredentials: true }).withAutomaticReconnect().build();

        const handle = (payload) => {
            try {
                if (!payload) return;
                const tId = String(payload.ticketId ?? payload.TicketId ?? payload?.message?.ticketId ?? payload?.Message?.TicketId ?? '');
                if (tId !== String(ticketId)) return;
                const msg = payload.message || payload.Message;
                if (msg) onMessage(msg);
            } catch { }
        };

        conn.on('message', handle);
        conn.on('status', p => {
            try {
                const tId = String(p?.ticketId ?? p?.TicketId ?? '');
                if (tId === String(ticketId)) onStatus?.(p);
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
        conn.on('Presence', handle); conn.on('presence', handle);
        await conn.start().catch(() => { });
        try { await conn.invoke('WatchUsers', authorIds); } catch { }
        return conn;
    }

    // ===== render one message =================================================
    const initialsFrom = (name) => {
        const s = String(name || '').trim(); if (!s) return '•';
        const p = s.split(/\s+/).slice(0, 2); return p.map(x => x[0]).join('').toUpperCase();
    };
    const authorIdFrom = (m) => {
        const direct = m?.authorUserId || m?.authorId || m?.userId || m?.user?.id || m?.author?.id || m?.author?.userId;
        if (direct) return String(direct);
        const url = m?.authorAvatarUrl || '';
        const m1 = String(url).match(/\/avatars\/([^\/]+)\.jpg(?:\?|$)/i);
        if (m1) return m1[1];
        return null;
    };
    const renderAttList = list => (list || []).map(a =>
        `<a class="tt-file" href="/api/support/files/${encodeURIComponent(a.id)}" target="_blank" rel="noopener">${esc(a.name || a.fileName || ('file-' + a.id))}</a>`
    ).join(' ');

    function renderMsg(m) {
        const isUser = (m.role === 'user');
        const cls = isUser ? 'usr' : 'op';
        const key = msgKey(m);
        const msgIdAttr = key ? ` data-msg-id="${esc(String(key))}"` : '';
        const authorId = authorIdFrom(m);
        const authorAttr = authorId ? ` data-author-id="${esc(authorId)}"` : '';

        const name = m.authorName || (isUser ? 'Пользователь' : 'Оператор');
        const atIso = m.createdAt ?? m.at ?? new Date();
        const atS = fmtShort(atIso);
        const atFull = fmtFull(atIso);

        const avatarUrl = m.authorAvatarUrl;
        const av = avatarUrl ? `<img src="${esc(avatarUrl)}" alt="${esc(name)}" />`
            : `<span class="initials">${esc(initialsFrom(name))}</span>`;

        const filesHtml = Array.isArray(m.attachments) && m.attachments.length
            ? `<div class="tt-files">${renderAttList(m.attachments)}</div>` : '';

        return `
<div class="tt-msg ${cls}"${msgIdAttr}${authorAttr}>
  <div class="tt-avatar" title="${esc(name)}">
    ${av}
    <span class="tt-presence offline" aria-hidden="true"></span>
  </div>
  <div class="tt-bubble">
    <div class="tt-msg-head">
      <span class="tt-name">${esc(name)}</span>
      <span class="tt-time" title="${esc(atFull)}">${esc(atS)}</span>
    </div>
    <div class="tt-msg-text">${esc(m.body ?? m.text ?? '')}</div>
    ${filesHtml}
  </div>
</div>`;
    }

    function ensureAttachmentsInDom(msgBox, msgId, attList) {
        const key = String(msgId ?? '');
        if (!key || !Array.isArray(attList) || attList.length === 0) return;
        const root = msgBox.querySelector(`.tt-msg[data-msg-id="${cssEsc(key)}"]`);
        if (!root) return;
        let files = root.querySelector('.tt-files');
        if (!files) {
            files = document.createElement('div'); files.className = 'tt-files';
            files.innerHTML = renderAttList(attList);
            root.querySelector('.tt-bubble')?.appendChild(files);
        } else if (!files.innerHTML.trim()) {
            files.innerHTML = renderAttList(attList);
        }
    }

    // ===== init ===============================================================
    window.initOpTicketThread = async function (panel) {
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

        const btnBack = root.querySelector('#tt_back');
        const btnClose = root.querySelector('#tt_close');
        const txt = root.querySelector('#tt_text');
        const btnSend = root.querySelector('#tt_send');

        const btnAttach = root.querySelector('#tt_attach');
        const inpFiles = root.querySelector('#tt_files');
        const list = root.querySelector('#tt_attach_list');

        titleId && (titleId.textContent = ticketId);
        dupId && (dupId.textContent = ticketId);
        subjEl && (subjEl.textContent = subject || '—');

        // snapshot
        let ticket, messages;
        try {
            const data = await API.getTicket(ticketId);
            ticket = data.ticket ?? data.data?.ticket ?? {};
            messages =
                Array.isArray(data.messages) ? data.messages :
                    Array.isArray(ticket.messages) ? ticket.messages :
                        Array.isArray(data.data?.messages) ? data.data.messages : [];
        } catch (e) {
            console.error('[op-ticket] load failed:', e);
            if (msgBox) msgBox.innerHTML = `<div class="muted" style="padding:8px">Ошибка загрузки данных заявки.</div>`;
            return;
        }

        const statusRu = ticket.status === 'closed' ? 'Закрыта' : 'Открыта';
        stEl && (stEl.textContent = statusRu);
        if (ticket.status === 'closed') stEl?.classList.add('tt-status-closed');
        crEl && (crEl.textContent = ticket.createdAt ? fmtFull(ticket.createdAt) : '—');
        upEl && (upEl.textContent = ticket.updatedAt ? fmtFull(ticket.updatedAt) : '—');

        if (msgBox) {
            msgBox.innerHTML = (messages || []).map(m => renderMsg(m)).join('');
            if (wrap) wrap.scrollTop = wrap.scrollHeight;
        }

        // анти-дубль
        const seenIds = new Set((messages || []).map(m => msgKey(m)).filter(Boolean));

        function addMessage(m) {
            if (!m) return;
            const key = msgKey(m);
            if (!key) return;
            if (seenIds.has(key)) return;
            if (msgBox.querySelector(`.tt-msg[data-msg-id="${cssEsc(key)}"]`)) {
                seenIds.add(key);
                return;
            }
            seenIds.add(key);
            msgBox.insertAdjacentHTML('beforeend', renderMsg(m));
            const aid = authorIdFrom(m);
            if (aid) setPresence(aid, true);
            upEl && (upEl.textContent = fmtFull(m.createdAt ?? m.at ?? new Date()));
            if ((!m.attachments || !m.attachments.length) && key) {
                setTimeout(() => refreshAttachmentsFromSnapshot(key), 80);
            }
            if (wrap) wrap.scrollTop = wrap.scrollHeight;
        }

        // закрытие/разблокировка ввода
        function applyClosedState(closed) {
            const inputWrap = root.querySelector('.tt-input');
            if (closed) {
                inputWrap?.classList.add('disabled');
                if (txt) { txt.disabled = true; txt.placeholder = 'Заявка закрыта. Отправка сообщений недоступна.'; }
                if (btnSend) btnSend.disabled = true;
                if (btnClose) btnClose.disabled = true;
                if (btnAttach) btnAttach.disabled = true;
                if (inpFiles) inpFiles.disabled = true;
            } else {
                inputWrap?.classList.remove('disabled');
                if (txt) { txt.disabled = false; txt.placeholder = 'Напишите ответ… (Ctrl+Enter — отправить)'; }
                if (btnSend) btnSend.disabled = false;
                if (btnClose) btnClose.disabled = false;
                if (btnAttach) btnAttach.disabled = false;
                if (inpFiles) inpFiles.disabled = false;
            }
        }
        applyClosedState(ticket.status === 'closed');

        // self/role и «замок» по назначению
        const meInfo = await loadMe();
        const selfMeta = detectSelf(msgBox);
        const MY_ID = selfMeta?.id || meInfo.id || null;

        function applyAssignmentLock() {
            if (ticket.status === 'closed') return; // уже закрыта — оставляем как есть
            const assignedTo = ticket.assignedUserId || ticket.AssignedUserId || null;
            const locked = !IS_ADMIN && assignedTo && String(assignedTo) !== String(MY_ID || '');
            const inputWrap = root.querySelector('.tt-input');
            if (locked) {
                inputWrap?.classList.add('disabled');
                if (txt) { txt.disabled = true; txt.placeholder = 'Тикет назначен на другого оператора'; }
                if (btnSend) btnSend.disabled = true;
                if (btnAttach) btnAttach.disabled = true;
                if (inpFiles) inpFiles.disabled = true;
            } else {
                inputWrap?.classList.remove('disabled');
                if (txt) { txt.disabled = false; txt.placeholder = 'Напишите ответ… (Ctrl+Enter — отправить)'; }
                if (btnSend) btnSend.disabled = false;
                if (btnAttach) btnAttach.disabled = false;
                if (inpFiles) inpFiles.disabled = false;
            }
        }
        applyAssignmentLock();

        // presence
        const participantIds = new Set();
        (messages || []).forEach(m => { const uid = authorIdFrom(m); if (uid) participantIds.add(uid); });
        if (ticket?.ownerUserId) participantIds.add(String(ticket.ownerUserId));

        // ==== Attachments ======================================================
        const inflight = new Set();
        function track(p) { inflight.add(p); onInflightChange(); p.finally(() => { inflight.delete(p); onInflightChange(); }); return p; }
        function ensureStatusEl(container) {
            let st = container?.querySelector?.('.tt-attach-status');
            if (!st && container) {
                st = document.createElement('div');
                st.className = 'tt-attach-status muted';
                st.style.fontSize = '12px'; st.style.marginTop = '4px';
                container.appendChild(st);
            }
            return st;
        }
        const statusEl = ensureStatusEl(list);
        const pending = []; // { id, name, size, ... }

        function onInflightChange() {
            const n = inflight.size;
            if (statusEl) statusEl.textContent = n > 0 ? `Загрузка файлов… (${n})` : '';
            if (btnSend) btnSend.disabled = n > 0 || btnSend.disabled;
        }
        function renderList() {
            if (!list) return;
            list.innerHTML = pending.map(p =>
                `<span class="tt-attach-pill"><b>${esc(p.name)}</b><small>${fmtSize(p.size)}</small><button title="Убрать" data-id="${p.id}">×</button></span>`
            ).join('');
            list.appendChild(statusEl);
            list.querySelectorAll('button[data-id]').forEach(b => {
                b.addEventListener('click', () => {
                    const id = b.getAttribute('data-id');
                    const i = pending.findIndex(x => String(x.id) === String(id));
                    if (i >= 0) { pending.splice(i, 1); renderList(); }
                });
            });
        }

        async function uploadSelected(files) {
            // блокировать аплоад если замок
            const assignedTo = ticket.assignedUserId || ticket.AssignedUserId || null;
            const locked = !IS_ADMIN && assignedTo && String(assignedTo) !== String(MY_ID || '');
            if (locked) { alert('Тикет назначен на другого оператора. Загрузка недоступна.'); return; }

            if (!files || !files.length) return;
            const fd = new FormData();
            Array.from(files).forEach(f => fd.append('files', f, f.name));
            const p = (async () => {
                try {
                    const files = await API.uploadFiles(ticketId, fd);
                    files.forEach(f => pending.push(f));
                    renderList();
                } catch (e) {
                    console.warn('[op-ticket] upload error:', e);
                    alert('Ошибка загрузки: ' + (e.message || 'неизвестно'));
                }
            })();
            return track(p);
        }

        btnAttach?.addEventListener('click', () => {
            const assignedTo = ticket.assignedUserId || ticket.AssignedUserId || null;
            const locked = !IS_ADMIN && assignedTo && String(assignedTo) !== String(MY_ID || '');
            if (locked) { alert('Тикет назначен на другого оператора. Загрузка недоступна.'); return; }

            if (inpFiles && typeof inpFiles.showPicker === 'function') { try { inpFiles.showPicker(); return; } catch { } }
            if (inpFiles) { try { inpFiles.click(); return; } catch { } }
            const tmp = document.createElement('input');
            tmp.type = 'file'; tmp.multiple = true;
            Object.assign(tmp.style, { position: 'fixed', left: '-9999px', top: '0', opacity: '0', width: '1px', height: '1px' });
            document.body.appendChild(tmp);
            tmp.addEventListener('change', async () => {
                const files = Array.from(tmp.files || []);
                if (files.length) await uploadSelected(files);
                tmp.remove();
            });

            tmp.click();
        });
        inpFiles?.addEventListener('change', async () => {
            const files = Array.from(inpFiles.files || []);
            if (files.length) await uploadSelected(files);
            inpFiles.value = '';
        });

        panel.addEventListener('dragover', (e) => { if (e.dataTransfer?.types?.includes('Files')) e.preventDefault(); });
        panel.addEventListener('drop', async (e) => {
            if (!e.dataTransfer?.files?.length) return;
            e.preventDefault();
            await uploadSelected(Array.from(e.dataTransfer.files));
        });

        // ==== Отправка =========================================================
        async function sendNow() {
            const v = (txt?.value || '').trim();
            if (ticket.status === 'closed') return;

            // проверка «замка»
            const assignedTo = ticket.assignedUserId || ticket.AssignedUserId || null;
            const locked = !IS_ADMIN && assignedTo && String(assignedTo) !== String(MY_ID || '');
            if (locked) { alert('Тикет назначен на другого оператора. Отправка недоступна.'); return; }

            if (inflight.size) { try { await Promise.all([...inflight]); } catch { } }
            if (!v && pending.length === 0) { alert('Добавьте текст или вложение.'); return; }
            if (v.length > 1500) { alert(`Слишком длинное сообщение (${v.length}). Максимум 1500 символов.`); return; }

            const localAtt = pending.map(p => ({ id: p.id, name: p.name }));
            btnSend && (btnSend.disabled = true);

            try {
                const reply = await API.postReply(ticketId, v, pending.map(x => x.id));
                const me = detectSelf(msgBox);
                const key = String(reply.id ?? '');

                if (key && (seenIds.has(key) || msgBox.querySelector(`.tt-msg[data-msg-id="${cssEsc(key)}"]`))) {
                    if (txt) txt.value = '';
                    pending.splice(0, pending.length); renderList();
                    upEl && (upEl.textContent = fmtFull(reply.at || new Date().toISOString()));
                    if (wrap) wrap.scrollTop = wrap.scrollHeight;
                    return;
                }

                const mineMsg = {
                    id: reply.id, role: 'agent', body: v,
                    createdAt: reply.at || new Date().toISOString(),
                    attachments: localAtt,
                    authorName: reply.authorName || me.name || 'Оператор',
                    authorUserId: reply.authorUserId || me.id || undefined,
                    authorAvatarUrl: reply.authorAvatarUrl || me.avatar || undefined
                };

                const k = msgKey(mineMsg);
                if (k) seenIds.add(k);

                msgBox.insertAdjacentHTML('beforeend', renderMsg(mineMsg));
                ensureAttachmentsInDom(msgBox, mineMsg.id, localAtt);
                if (wrap) wrap.scrollTop = wrap.scrollHeight;
                if (txt) txt.value = '';
                pending.splice(0, pending.length); renderList();
                upEl && (upEl.textContent = fmtFull(mineMsg.createdAt));

                const myAid = (mineMsg.authorUserId) || (me.id);
                if (myAid) setPresence(String(myAid), true);

            } catch (e) {
                alert('Не удалось отправить сообщение: ' + (e.message || 'ошибка'));
            } finally {
                btnSend && (btnSend.disabled = false);
            }
        }
        function onKey(e) { if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) { e.preventDefault(); sendNow(); } }
        txt?.addEventListener('keydown', onKey);
        btnSend?.addEventListener('click', sendNow);

        // ==== Назад/Закрыть ====================================================
        function goBack() {
            const exists = id => !!document.querySelector(`.sidebar .sidebar-button[data-content="${id}"]`);
            const fromId = ds.from || 'user_tickets';
            const backup = (typeof window.__support_backTarget === 'string' && window.__support_backTarget) ? window.__support_backTarget : '';
            const target = (fromId && exists(fromId)) ? fromId : (backup && exists(backup)) ? backup : 'user_tickets';
            if (typeof window.selectSidebarByContentId === 'function') window.selectSidebarByContentId(target); else history.back();
        }
        btnBack?.addEventListener('click', goBack);

        const onCloseClick = async () => {
            if (ticket.status === 'closed') { goBack(); return; }
            if (!confirm('Закрыть эту заявку?')) return;
            btnClose.disabled = true;
            try {
                const r = await fetch(`/api/support/tickets/${encodeURIComponent(ticketId)}/close`, {
                    method: 'POST', credentials: 'same-origin',
                    headers: { 'Content-Type': 'application/json; charset=utf-8', ...(anti() ? { 'RequestVerificationToken': anti() } : {}) },
                    body: '{}'
                });
                const j = await safeJson(r);
                if (!r.ok) throw new Error((j && (j.error || j.message)) || ('HTTP ' + r.status));
                stEl && (stEl.textContent = 'Закрыта'); stEl && stEl.classList.add('tt-status-closed');
                applyClosedState(true);
                upEl && (upEl.textContent = fmtFull(j?.updatedAt || new Date().toISOString()));
                setTimeout(goBack, 50);
            } catch (e) {
                alert('Не удалось закрыть заявку: ' + (e.message || 'ошибка'));
            } finally { btnClose.disabled = false; }
        };
        btnClose?.addEventListener('click', onCloseClick);

        // ==== Realtime =========================================================
        async function refreshAttachmentsFromSnapshot(messageId) {
            try {
                const data = await API.getTicket(ticketId);
                const mm = (Array.isArray(data.messages) ? data.messages : (Array.isArray(data?.data?.messages) ? data.data.messages : []))
                    .find(x => String(x.id) === String(messageId));
                if (mm?.attachments?.length) ensureAttachmentsInDom(msgBox, messageId, mm.attachments);
            } catch { }
        }

        let conn = null, presenceConn = null;
        try {
            conn = await connectTicketHub(
                ticketId,
                (m) => { addMessage(m); },
                (payload) => {
                    if (payload?.status === 'closed') {
                        stEl && (stEl.textContent = 'Закрыта'); stEl && stEl.classList.add('tt-status-closed');
                        applyClosedState(true);
                        upEl && (upEl.textContent = fmtFull(payload.updatedAt || new Date().toISOString()));
                        ticket.status = 'closed';
                    }
                }
            );
            // ловим переназначение — переключаем «замок»
            conn?.on?.('assigned', (p) => {
                try {
                    const tId = String(p?.ticketId ?? p?.TicketId ?? '');
                    if (tId !== String(ticketId)) return;
                    ticket.assignedUserId = p?.assignedUserId || p?.AssignedUserId || null;
                    applyAssignmentLock();
                } catch { }
            });
        } catch (e) { console.warn('[op-ticket] realtime off:', e?.message || e); }

        try {
            if (participantIds.size > 0) {
                presenceConn = await connectPresenceHub([...participantIds], (uid, online) => setPresence(uid, online));
            }
        } catch (e) { console.warn('[op-ticket] presence off:', e?.message || e); }

        // dispose
        panel.__dispose = function () {
            try { txt?.removeEventListener('keydown', onKey); } catch { }
            try { btnSend?.removeEventListener('click', sendNow); } catch { }
            try { btnBack?.removeEventListener('click', goBack); } catch { }
            try { btnClose?.removeEventListener('click', onCloseClick); } catch { }
            try { conn?.stop(); } catch { }
            try { presenceConn?.stop(); } catch { }
        };
    };
})();