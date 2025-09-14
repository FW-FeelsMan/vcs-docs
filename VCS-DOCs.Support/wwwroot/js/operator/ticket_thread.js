//D:\Unity\VCS-DOCs\VCS-DOCs.Support\wwwroot\js\operator\ticket_thread.js
(() => {
    function fmt(dt) {
        try {
            return new Intl.DateTimeFormat('ru-RU', {
                year: 'numeric', month: '2-digit', day: '2-digit',
                hour: '2-digit', minute: '2-digit', second: '2-digit'
            }).format(dt);
        } catch {
            const p = n => String(n).padStart(2, '0');
            return `${dt.getFullYear()}-${p(dt.getMonth() + 1)}-${p(dt.getDate())} ${p(dt.getHours())}:${p(dt.getMinutes())}:${p(dt.getSeconds())}`;
        }
    }
    const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

    // демо-данные (можно закрывать тред для проверки по id/источнику)
    function mockThread(ticketId, fromId) {
        const now = Date.now(), base = now - 60 * 60 * 1000;
        const closed = fromId === 'closed_tickets' || /zx$/i.test(ticketId); // простое правило для демо
        return {
            status: closed ? 'Закрыта' : 'Открыта',
            created: new Date(base),
            updated: new Date(base + 35 * 60 * 1000),
            messages: [
                { role: 'user', at: new Date(base + 0 * 60 * 1000), text: 'Здравствуйте! Нужна помощь.' },
                { role: 'operator', at: new Date(base + 10 * 60 * 1000), text: 'Добрый день! Подскажите детали.' }
            ]
        };
    }

    // пузырь
    function renderMsg(m) {
        const who = m.role === 'operator' ? 'Оператор' : 'Пользователь';
        const cls = m.role === 'operator' ? 'tt-msg op' : 'tt-msg usr';
        return `
          <div class="${cls}">
            <div class="tt-bubble">
              <div class="tt-msg-text">${esc(m.text)}</div>
              <div class="tt-msg-meta"><span class="tt-msg-who">${who}</span><span class="tt-msg-at">${fmt(m.at)}</span></div>
            </div>
          </div>`;
    }

    // ===== инициализация =====
    window.initTicketThread = async function (panel) {
        if (!panel || panel.__tt_inited) return;
        panel.__tt_inited = true;

        const root = panel.querySelector('#ticket-thread') || panel;
        const ds = root.dataset || {};
        const ticketId = ds.ticketId || '—';
        const subject = ds.subject || '—';
        const fromId = ds.from || 'user_tickets'; // user_tickets | closed_tickets

        const titleId = root.querySelector('#tt_id');
        const dupId = root.querySelector('#tt_id_dup');
        const subjEl = root.querySelector('#tt_subject');
        const stEl = root.querySelector('#tt_status');
        const crEl = root.querySelector('#tt_created');
        const upEl = root.querySelector('#tt_updated');
        const msgBox = root.querySelector('#tt_messages');

        const txt = root.querySelector('#tt_text');
        const btnSend = root.querySelector('#tt_send');
        const btnBack = root.querySelector('#tt_back');
        const inputWrap = root.querySelector('.tt-input');

        titleId && (titleId.textContent = ticketId);
        dupId && (dupId.textContent = ticketId);
        subjEl && (subjEl.textContent = subject || '—');

        const data = mockThread(ticketId, fromId);

        stEl && (stEl.textContent = data.status);
        if (data.status !== 'Открыта') stEl?.classList.add('tt-status-closed');

        crEl && (crEl.textContent = fmt(data.created));
        upEl && (upEl.textContent = fmt(data.updated));

        if (msgBox) {
            msgBox.innerHTML = data.messages.map(renderMsg).join('');
            const wrap = root.querySelector('.tt-wrap');
            if (wrap) wrap.scrollTop = wrap.scrollHeight;
        }

        // режим ЗАКРЫТО — блокируем ввод
        function applyClosedState(closed) {
            if (closed) {
                inputWrap?.classList.add('disabled');
                if (txt) {
                    txt.disabled = true;
                    txt.placeholder = 'Заявка закрыта. Отправка сообщений недоступна.';
                }
                if (btnSend) { btnSend.disabled = true; btnSend.classList.remove('primary'); }
            } else {
                inputWrap?.classList.remove('disabled');
                if (txt) { txt.disabled = false; txt.placeholder = 'Напишите ответ… (Ctrl+Enter — отправить)'; }
                if (btnSend) { btnSend.disabled = false; btnSend.classList.add('primary'); }
            }
        }
        applyClosedState(data.status !== 'Открыта');

        async function sendNow() {
            const v = (txt?.value || '').trim();
            if (!v || (data.status !== 'Открыта')) return;
            const newMsg = { role: 'operator', at: new Date(), text: v };
            msgBox.insertAdjacentHTML('beforeend', renderMsg(newMsg));
            txt.value = '';
            upEl && (upEl.textContent = fmt(newMsg.at));
            const wrap = root.querySelector('.tt-wrap');
            if (wrap) wrap.scrollTop = wrap.scrollHeight;
            // TODO: POST /api/support/tickets/{id}/reply
        }
        function onKey(e) { if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) { e.preventDefault(); sendNow(); } }

        txt?.addEventListener('keydown', onKey);
        btnSend?.addEventListener('click', sendNow);

        function goBack() {
            const exists = id => !!document.querySelector(`.sidebar .sidebar-button[data-content="${id}"]`);
            const backup = (typeof window.__support_backTarget === 'string' && window.__support_backTarget) ? window.__support_backTarget : null;
            const target = (fromId && exists(fromId)) ? fromId : (backup && exists(backup)) ? backup : 'user_tickets';
            if (typeof window.selectSidebarByContentId === 'function') window.selectSidebarByContentId(target); else history.back();
        }
        btnBack?.addEventListener('click', goBack);

        panel.__dispose = function () {
            try { txt?.removeEventListener('keydown', onKey); } catch { }
            try { btnSend?.removeEventListener('click', sendNow); } catch { }
            try { btnBack?.removeEventListener('click', goBack); } catch { }
        };
    };
})();
