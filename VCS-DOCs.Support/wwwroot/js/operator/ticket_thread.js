// wwwroot/js/operator/ticket_thread.js
// История, realtime, presence, вложения, отправка, закрытие — операторская версия
// FIX: корректная ре-активация кнопки "Отправить" после upload (раньше залипала из-за `btnSend.disabled = n>0 || btnSend.disabled`)
// + лёгкий рефактор: единая функция recomputeComposerEnabledState() — единственный источник правды для enabled/disabled

(() => {
    // ===== utils ==============================================================
    const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({
        '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
    }[c]));

    const fmtShort = iso => {
        try { return new Intl.DateTimeFormat('ru-RU', { hour: '2-digit', minute: '2-digit' }).format(new Date(iso)); }
        catch { return String(iso); }
    };
    const fmtFull = iso => {
        try {
            return new Intl.DateTimeFormat('ru-RU', {
                year: 'numeric', month: '2-digit', day: '2-digit',
                hour: '2-digit', minute: '2-digit', second: '2-digit'
            }).format(new Date(iso));
        } catch { return String(iso); }
    };
    const fmtSize = n =>
        n >= 1048576 ? (n / 1048576).toFixed(1) + ' MB' :
            n >= 1024 ? (n / 1024).toFixed(1) + ' KB' : n + ' B';

    const csrfMeta = () => document.querySelector('meta[name="csrf-token"]')?.content || '';
    const aftInput = () => /** @type {HTMLInputElement|null} */(document.querySelector('input[name="__RequestVerificationToken"]'));
    const anti = () => aftInput()?.value || csrfMeta();

    const cssEsc = v => (window.CSS && CSS.escape)
        ? CSS.escape(String(v))
        : String(v).replace(/[^a-zA-Z0-9_-]/g, '\\$&');

    async function getJson(url) {
        const r = await fetch(url, { credentials: 'same-origin', cache: 'no-store', headers: { 'X-Requested-With': 'fetch' } });
        if (!r.ok) throw new Error(`HTTP ${r.status}`);
        return await r.json();
    }
    async function safeJson(resp) { try { return await resp.json(); } catch { return null; } }

    // ===== message id helper ==================================================
    const msgKey = (m) => {
        const id = (m && (m.id ?? m.Id ?? m.messageId ?? m.MessageId));
        return id == null ? '' : String(id);
    };

    // ===== close button helper ===============================================
    function setCloseDisabled(disabled, title) {
        const btn = document.getElementById('tt_close');
        if (!btn) return;
        btn.disabled = !!disabled;
        btn.setAttribute('aria-disabled', disabled ? 'true' : 'false');
        btn.classList.toggle('is-disabled', !!disabled);
        if (title !== undefined) btn.title = title || '';
    }

    // ===== API ================================================================
    const API = {
        async getTicket(ticketId) {
            return await getJson(`/api/support/tickets/${encodeURIComponent(ticketId)}`);
        },
        async uploadFiles(ticketId, fd) {
            const token = anti();
            const hdr = token ? { 'RequestVerificationToken': token } : {};
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

            if (!resp.ok) throw new Error((data && (data.error || data.message)) || ('HTTP ' + resp.status));

            const files = Array.isArray(data?.files) ? data.files : [];
            if (!files.length) throw new Error('Нет файлов в ответе сервера');
            return files;
        },
        async postReply(ticketId, text, attIds) {
            const token = anti();
            const headers = {
                'Content-Type': 'application/json; charset=utf-8',
                ...(token ? { 'RequestVerificationToken': token } : {})
            };

            // основной операторский эндпоинт
            let resp = await fetch(`/api/ops/tickets/${encodeURIComponent(ticketId)}/reply`, {
                method: 'POST',
                credentials: 'same-origin',
                headers,
                body: JSON.stringify({ text, attachmentIds: attIds, attachments: attIds })
            });

            // fallback (если вдруг нет)
            if (resp.status === 404) {
                resp = await fetch(`/api/support/tickets/${encodeURIComponent(ticketId)}/messages`, {
                    method: 'POST',
                    credentials: 'same-origin',
                    headers,
                    body: JSON.stringify({ body: text, attachments: attIds })
                });
            }

            const j = await safeJson(resp);
            if (!resp.ok) throw new Error((j && (j.error || j.message)) || ('HTTP ' + resp.status));

            return {
                ok: !!(j?.ok ?? true),
                id: j?.messageId ?? j?.id ?? null,
                at: j?.at ?? j?.createdAt ?? new Date().toISOString(),
                authorName: j?.authorName,
                authorUserId: j?.authorUserId,
                authorAvatarUrl: j?.authorAvatarUrl
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
            return { id: me.id || null };
        } catch {
            IS_ADMIN = false;
            return { id: null };
        }
    }

    function detectSelf(msgBox) {
        if (SELF) return SELF;

        const nm = document.querySelector('meta[name="current-user-name"]')?.content || '';
        const av = document.querySelector('meta[name="current-user-avatar"]')?.content || '';
        const id = document.querySelector('meta[name="current-user-id"]')?.content || '';
        if (nm || av || id) {
            SELF = { name: nm || null, avatar: av || null, id: id || null };
            return SELF;
        }

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

    async function connectTicketHub(ticketId, onMessage, onStatus) {
        const signalR = await loadSignalR();
        const conn = new signalR.HubConnectionBuilder()
            .withUrl(HUB_URL, { withCredentials: true })
            .withAutomaticReconnect()
            .build();

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
        const conn = new signalR.HubConnectionBuilder()
            .withUrl(PRESENCE_HUB_URL, { withCredentials: true })
            .withAutomaticReconnect()
            .build();

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
        try { await conn.invoke('WatchUsers', authorIds); } catch { }
        return conn;
    }

    // ===== render =============================================================
    const initialsFrom = (name) => {
        const s = String(name || '').trim();
        if (!s) return '•';
        const p = s.split(/\s+/).slice(0, 2);
        return p.map(x => x[0]).join('').toUpperCase();
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
        const atF = fmtFull(atIso);

        const avatarUrl = m.authorAvatarUrl;
        const av = avatarUrl
            ? `<img src="${esc(avatarUrl)}" alt="${esc(name)}" />`
            : `<span class="initials">${esc(initialsFrom(name))}</span>`;

        const filesHtml = Array.isArray(m.attachments) && m.attachments.length
            ? `<div class="tt-files">${renderAttList(m.attachments)}</div>`
            : '';

        return `
<div class="tt-msg ${cls}"${msgIdAttr}${authorAttr}>
  <div class="tt-avatar" title="${esc(name)}">
    ${av}
    <span class="tt-presence offline" aria-hidden="true"></span>
  </div>
  <div class="tt-bubble">
    <div class="tt-msg-head">
      <span class="tt-name">${esc(name)}</span>
      <span class="tt-time" title="${esc(atF)}">${esc(atS)}</span>
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
            files = document.createElement('div');
            files.className = 'tt-files';
            files.innerHTML = renderAttList(attList);
            root.querySelector('.tt-bubble')?.appendChild(files);
        } else if (!files.innerHTML.trim()) {
            files.innerHTML = renderAttList(attList);
        }
    }

    // ===== INIT ===============================================================
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

        if (titleId) titleId.textContent = ticketId;
        if (dupId) dupId.textContent = ticketId;
        if (subjEl) subjEl.textContent = subject || '—';

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

        // header
        const statusRu = ticket.status === 'closed' ? 'Закрыта' : 'Открыта';
        if (stEl) stEl.textContent = statusRu;
        if (ticket.status === 'closed') stEl?.classList.add('tt-status-closed');
        if (crEl) crEl.textContent = ticket.createdAt ? fmtFull(ticket.createdAt) : '—';
        if (upEl) upEl.textContent = ticket.updatedAt ? fmtFull(ticket.updatedAt) : '—';

        // render snapshot
        if (msgBox) {
            msgBox.innerHTML = (messages || []).map(m => renderMsg(m)).join('');
            if (wrap) wrap.scrollTop = wrap.scrollHeight;
        }

        // анти-дубль по msg id
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

            if (upEl) upEl.textContent = fmtFull(m.createdAt ?? m.at ?? new Date());
            if ((!m.attachments || !m.attachments.length) && key) {
                setTimeout(() => refreshAttachmentsFromSnapshot(key), 80);
            }
            if (wrap) wrap.scrollTop = wrap.scrollHeight;
        }

        // закрытие: выключает всё независимо от назначения/inflight
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
                // важно: btnSend не включаем "всегда" — включение управляется recomputeComposerEnabledState()
                if (btnAttach) btnAttach.disabled = false;
                if (inpFiles) inpFiles.disabled = false;
            }
        }
        applyClosedState(ticket.status === 'closed');

        // self/role & assignment lock
        const meInfo = await loadMe();
        const selfMeta = detectSelf(msgBox);
        const MY_ID = selfMeta?.id || meInfo.id || null;

        // ===== gating (единственный источник правды) ===========================
        const inflight = new Set();     // Promises upload
        const pending = [];             // {id,name,size,...}

        const isTicketClosed = () => (ticket?.status === 'closed');

        const assignedToId = () => (ticket?.assignedUserId || ticket?.AssignedUserId || null);

        const isLockedByAssignment = () => {
            const assignedTo = assignedToId();
            // админ не лочится
            if (IS_ADMIN) return false;
            // если не назначен вообще — не лочим
            if (!assignedTo) return false;
            // если не совпало с моим — лочим
            return String(assignedTo) !== String(MY_ID || '');
        };

        function canCloseTicket() {
            const assignedTo = assignedToId();
            return !!(IS_ADMIN || (assignedTo && String(assignedTo) === String(MY_ID || '')));
        }

        function recomputeComposerEnabledState() {
            const closed = isTicketClosed();
            const locked = isLockedByAssignment();
            const uploading = inflight.size > 0;

            // Close button — отдельная логика
            if (closed) setCloseDisabled(true, 'Заявка уже закрыта');
            else setCloseDisabled(!canCloseTicket(), !canCloseTicket() ? 'Только назначенный оператор или админ может закрыть заявку' : '');

            // Composer
            const inputWrap = root.querySelector('.tt-input');

            if (closed || locked) {
                inputWrap?.classList.add('disabled');
                if (txt) {
                    txt.disabled = true;
                    txt.placeholder = closed
                        ? 'Заявка закрыта. Отправка сообщений недоступна.'
                        : 'Тикет назначен на другого оператора';
                }
                if (btnSend) btnSend.disabled = true;
                if (btnAttach) btnAttach.disabled = true;
                if (inpFiles) inpFiles.disabled = true;
                return; // closed/locked важнее аплоада
            }

            // не закрыт и не залочен:
            inputWrap?.classList.remove('disabled');
            if (txt) {
                txt.disabled = false;
                txt.placeholder = 'Напишите ответ… (Ctrl+Enter — отправить)';
            }

            // attach разрешаем всегда (если не закрыт/не залочен)
            if (btnAttach) btnAttach.disabled = false;
            if (inpFiles) inpFiles.disabled = false;

            // send запрещаем только пока грузятся вложения (иначе можно отправлять текст/вложения)
            if (btnSend) btnSend.disabled = uploading;
        }

        // начальное вычисление (после loadMe/detectSelf)
        recomputeComposerEnabledState();

        // ===== Attachments UI ==================================================
        function ensureStatusEl(container) {
            let st = container?.querySelector?.('.tt-attach-status');
            if (!st && container) {
                st = document.createElement('div');
                st.className = 'tt-attach-status muted';
                st.style.fontSize = '12px';
                st.style.marginTop = '4px';
                container.appendChild(st);
            }
            return st;
        }

        const statusEl = ensureStatusEl(list);

        function onInflightChange() {
            const n = inflight.size;
            if (statusEl) statusEl.textContent = n > 0 ? `Загрузка файлов… (${n})` : '';
            // ВАЖНО: больше не залипаем! пересчитываем по правилам
            recomputeComposerEnabledState();
        }

        function trackUpload(p) {
            inflight.add(p);
            onInflightChange();
            p.finally(() => {
                inflight.delete(p);
                onInflightChange();
            });
            return p;
        }

        function renderList() {
            if (!list) return;

            list.innerHTML = pending.map(p =>
                `<span class="tt-attach-pill">
                    <b>${esc(p.name)}</b>
                    <small>${fmtSize(p.size)}</small>
                    <button title="Убрать" data-id="${esc(p.id)}">×</button>
                 </span>`
            ).join('');

            list.appendChild(statusEl);

            list.querySelectorAll('button[data-id]').forEach(b => {
                b.addEventListener('click', () => {
                    const id = b.getAttribute('data-id');
                    const i = pending.findIndex(x => String(x.id) === String(id));
                    if (i >= 0) {
                        pending.splice(i, 1);
                        renderList();
                    }
                });
            });
        }

        async function uploadSelected(files) {
            if (isTicketClosed()) return;
            if (isLockedByAssignment()) { alert('Тикет назначен на другого оператора. Загрузка недоступна.'); return; }

            if (!files || !files.length) return;

            const fd = new FormData();
            Array.from(files).forEach(f => fd.append('files', f, f.name));

            const p = (async () => {
                try {
                    const uploaded = await API.uploadFiles(ticketId, fd);
                    uploaded.forEach(f => pending.push(f));
                    renderList();
                } catch (e) {
                    console.warn('[op-ticket] upload error:', e);
                    alert('Ошибка загрузки: ' + (e?.message || 'неизвестно'));
                }
            })();

            return trackUpload(p);
        }

        // attach click
        btnAttach?.addEventListener('click', () => {
            if (isTicketClosed()) return;
            if (isLockedByAssignment()) { alert('Тикет назначен на другого оператора. Загрузка недоступна.'); return; }

            // удобное открытие picker
            if (inpFiles && typeof inpFiles.showPicker === 'function') { try { inpFiles.showPicker(); return; } catch { } }
            if (inpFiles) { try { inpFiles.click(); return; } catch { } }

            // fallback временный input
            const tmp = document.createElement('input');
            tmp.type = 'file';
            tmp.multiple = true;
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

        // drag&drop
        panel.addEventListener('dragover', (e) => { if (e.dataTransfer?.types?.includes('Files')) e.preventDefault(); });
        panel.addEventListener('drop', async (e) => {
            if (!e.dataTransfer?.files?.length) return;
            e.preventDefault();
            await uploadSelected(Array.from(e.dataTransfer.files));
        });

        // ===== Send ============================================================
        async function sendNow() {
            if (isTicketClosed()) return;

            const v = (txt?.value || '').trim();

            if (isLockedByAssignment()) {
                alert('Тикет назначен на другого оператора. Отправка недоступна.');
                return;
            }

            // если ещё грузим вложения — дождаться
            if (inflight.size) {
                try { await Promise.all([...inflight]); } catch { }
            }

            if (!v && pending.length === 0) { alert('Добавьте текст или вложение.'); return; }
            if (v.length > 1500) { alert(`Слишком длинное сообщение (${v.length}). Максимум 1500 символов.`); return; }

            const localAtt = pending.map(p => ({ id: p.id, name: p.name }));

            // на время отправки заблокируем SEND, но не ломаем общую логику
            if (btnSend) btnSend.disabled = true;

            try {
                const reply = await API.postReply(ticketId, v, pending.map(x => x.id));

                const me = detectSelf(msgBox);
                const mineMsg = {
                    id: reply.id,
                    role: 'agent',
                    body: v,
                    createdAt: reply.at || new Date().toISOString(),
                    attachments: localAtt,
                    authorName: reply.authorName || me.name || 'Оператор',
                    authorUserId: reply.authorUserId || me.id || undefined,
                    authorAvatarUrl: reply.authorAvatarUrl || me.avatar || undefined
                };

                const k = msgKey(mineMsg);
                if (k && (seenIds.has(k) || msgBox.querySelector(`.tt-msg[data-msg-id="${cssEsc(k)}"]`))) {
                    // уже есть — просто чистим композер
                    if (txt) txt.value = '';
                    pending.splice(0, pending.length);
                    renderList();
                    if (upEl) upEl.textContent = fmtFull(reply.at || new Date().toISOString());
                    if (wrap) wrap.scrollTop = wrap.scrollHeight;
                    recomputeComposerEnabledState();
                    return;
                }

                if (k) seenIds.add(k);

                msgBox.insertAdjacentHTML('beforeend', renderMsg(mineMsg));
                ensureAttachmentsInDom(msgBox, mineMsg.id, localAtt);

                if (wrap) wrap.scrollTop = wrap.scrollHeight;
                if (txt) txt.value = '';

                pending.splice(0, pending.length);
                renderList();

                if (upEl) upEl.textContent = fmtFull(mineMsg.createdAt);

                const myAid = mineMsg.authorUserId || me.id;
                if (myAid) setPresence(String(myAid), true);
            } catch (e) {
                alert('Не удалось отправить сообщение: ' + (e?.message || 'ошибка'));
            } finally {
                // вернём кнопку согласно правилам (closed/locked/inflight)
                recomputeComposerEnabledState();
            }
        }

        function onKey(e) {
            if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) {
                e.preventDefault();
                sendNow();
            }
        }

        txt?.addEventListener('keydown', onKey);
        btnSend?.addEventListener('click', sendNow);

        // ===== Back / Close =====================================================
        function goBack() {
            const exists = id => !!document.querySelector(`.sidebar .sidebar-button[data-content="${id}"]`);
            const fromId = ds.from || 'user_tickets';
            const backup = (typeof window.__support_backTarget === 'string' && window.__support_backTarget) ? window.__support_backTarget : '';
            const target = (fromId && exists(fromId)) ? fromId : (backup && exists(backup)) ? backup : 'user_tickets';
            if (typeof window.selectSidebarByContentId === 'function') window.selectSidebarByContentId(target);
            else history.back();
        }
        btnBack?.addEventListener('click', goBack);

        const onCloseClick = async () => {
            if (isTicketClosed()) { goBack(); return; }
            if (!confirm('Закрыть эту заявку?')) return;

            if (!canCloseTicket()) {
                alert('Только назначенный оператор или админ может закрыть заявку.');
                return;
            }

            btnClose.disabled = true;
            try {
                const token = anti();
                const r = await fetch(`/api/support/tickets/${encodeURIComponent(ticketId)}/close`, {
                    method: 'POST',
                    credentials: 'same-origin',
                    headers: { 'Content-Type': 'application/json; charset=utf-8', ...(token ? { 'RequestVerificationToken': token } : {}) },
                    body: '{}'
                });

                const j = await safeJson(r);
                if (!r.ok) throw new Error((j && (j.error || j.message)) || ('HTTP ' + r.status));

                if (stEl) { stEl.textContent = 'Закрыта'; stEl.classList.add('tt-status-closed'); }

                ticket.status = 'closed';
                applyClosedState(true);
                recomputeComposerEnabledState();

                if (upEl) upEl.textContent = fmtFull(j?.updatedAt || new Date().toISOString());
                setTimeout(goBack, 50);
            } catch (e) {
                alert('Не удалось закрыть заявку: ' + (e?.message || 'ошибка'));
            } finally {
                btnClose.disabled = false;
            }
        };
        btnClose?.addEventListener('click', onCloseClick);

        // ===== Realtime =========================================================
        async function refreshAttachmentsFromSnapshot(messageId) {
            try {
                const data = await API.getTicket(ticketId);
                const list =
                    Array.isArray(data.messages) ? data.messages :
                        Array.isArray(data?.data?.messages) ? data.data.messages : [];

                const mm = list.find(x => String(x.id) === String(messageId));
                if (mm?.attachments?.length) ensureAttachmentsInDom(msgBox, messageId, mm.attachments);
            } catch { }
        }

        // presence snapshot targets
        const participantIds = new Set();
        (messages || []).forEach(m => { const uid = authorIdFrom(m); if (uid) participantIds.add(uid); });
        if (ticket?.ownerUserId) participantIds.add(String(ticket.ownerUserId));

        let conn = null, presenceConn = null;

        try {
            conn = await connectTicketHub(
                ticketId,
                (m) => { addMessage(m); },
                (payload) => {
                    if (payload?.status === 'closed') {
                        if (stEl) { stEl.textContent = 'Закрыта'; stEl.classList.add('tt-status-closed'); }
                        ticket.status = 'closed';
                        applyClosedState(true);
                        recomputeComposerEnabledState();
                        if (upEl) upEl.textContent = fmtFull(payload.updatedAt || new Date().toISOString());
                        setCloseDisabled(true, 'Заявка уже закрыта');
                    }
                }
            );

            // переназначение
            conn?.on?.('assigned', (p) => {
                try {
                    const tId = String(p?.ticketId ?? p?.TicketId ?? '');
                    if (tId !== String(ticketId)) return;
                    ticket.assignedUserId = p?.assignedUserId || p?.AssignedUserId || null;
                    recomputeComposerEnabledState();
                } catch { }
            });
        } catch (e) {
            console.warn('[op-ticket] realtime off:', e?.message || e);
        }

        try {
            if (participantIds.size > 0) {
                presenceConn = await connectPresenceHub([...participantIds], (uid, online) => setPresence(uid, online));
            }
        } catch (e) {
            console.warn('[op-ticket] presence off:', e?.message || e);
        }

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
