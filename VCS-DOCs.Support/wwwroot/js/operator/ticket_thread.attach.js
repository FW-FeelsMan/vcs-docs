(function () {
    function $(sel, root = document) { return root.querySelector(sel); }
    function esc(s) { return String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c])); }

    const panel = document.getElementById('ticket-thread');
    if (!panel) return;

    const ticketId = panel.getAttribute('data-ticket-id');
    const csrf = document.querySelector('meta[name="csrf-token"]')?.content || '';

    const btnAttach = $('#tt_attach', panel);
    const inputFile = $('#tt_file', panel);
    const chips = $('#tt_files', panel);
    const txt = $('#tt_text', panel);
    const btnSend = $('#tt_send', panel);

    const uploaded = []; // { id,name,size,contentType,url }

    btnAttach?.addEventListener('click', () => inputFile?.click());
    inputFile?.addEventListener('change', async () => {
        if (!inputFile.files || inputFile.files.length === 0) return;

        const fd = new FormData();
        for (const f of inputFile.files) fd.append('files', f, f.name);

        try {
            const r = await fetch(`/api/ops/tickets/${encodeURIComponent(ticketId)}/files`, {
                method: 'POST',
                credentials: 'same-origin',
                headers: csrf ? { 'RequestVerificationToken': csrf } : {},
                body: fd
            });
            const j = await r.json();
            if (!r.ok || !j?.ok) { throw new Error(j?.error || ('HTTP ' + r.status)); }

            for (const f of j.files || []) {
                uploaded.push(f);
                addChip(f);
            }
        } catch (e) {
            alert('Не удалось загрузить файл: ' + (e.message || e));
        } finally {
            inputFile.value = '';
        }
    });

    function addChip(f) {
        const el = document.createElement('span');
        el.className = 'tt-chip';
        el.setAttribute('data-id', f.id);
        el.innerHTML = `<b title="${esc(f.name)}">${esc(f.name)}</b> <span>${Math.round((f.size || 0) / 1024)} КБ</span> <button title="Убрать">×</button>`;
        el.querySelector('button')?.addEventListener('click', () => {
            const i = uploaded.findIndex(x => x.id === f.id);
            if (i >= 0) uploaded.splice(i, 1);
            el.remove();
        });
        chips.appendChild(el);
    }

    async function bindAttachments(messageId) {
        if (uploaded.length === 0) return;
        try {
            await fetch(`/api/ops/tickets/${encodeURIComponent(ticketId)}/files/bind`, {
                method: 'POST',
                credentials: 'same-origin',
                headers: {
                    'Content-Type': 'application/json; charset=utf-8',
                    ...(csrf ? { 'RequestVerificationToken': csrf } : {})
                },
                body: JSON.stringify({ attachmentIds: uploaded.map(x => x.id), messageId })
            });
        } catch { }
    }

    // отправка сообщения (псевдо: подстрой под ваш API отправки сообщения)
    async function sendMessage() {
        const text = (txt.value || '').trim();
        if (!text && uploaded.length === 0) return;

        btnSend.disabled = true;
        try {
            // 1) шлем текст сообщения (ваш действующий эндпоинт/контроллер)
            const res = await fetch(`/api/ops/tickets/${encodeURIComponent(ticketId)}/reply`, {
                method: 'POST',
                credentials: 'same-origin',
                headers: {
                    'Content-Type': 'application/json; charset=utf-8',
                    ...(csrf ? { 'RequestVerificationToken': csrf } : {})
                },
                body: JSON.stringify({ text, attachments: uploaded.map(x => x.id) })
            });
            const j = await res.json();
            if (!res.ok || !j?.ok) throw new Error(j?.error || ('HTTP ' + res.status));

            // 2) если сервер создаёт messageId отдельно — можно добиндить (оставлено для универсальности)
            if (j.messageId) await bindAttachments(j.messageId);

            // 3) очистка UI
            txt.value = '';
            chips.innerHTML = '';
            uploaded.splice(0, uploaded.length);
        } catch (e) {
            alert('Не удалось отправить: ' + (e.message || e));
        } finally {
            btnSend.disabled = false;
        }
    }

    btnSend?.addEventListener('click', sendMessage);
    txt?.addEventListener('keydown', (e) => {
        if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) {
            e.preventDefault();
            sendMessage();
        }
    });

    // drag&drop на поле
    panel.addEventListener('dragover', (e) => {
        if (e.dataTransfer?.types?.includes('Files')) { e.preventDefault(); }
    });
    panel.addEventListener('drop', (e) => {
        if (!e.dataTransfer?.files?.length) return;
        e.preventDefault();
        inputFile.files = e.dataTransfer.files; // передаём в input — сработает загрузчик
        const event = new Event('change');
        inputFile.dispatchEvent(event);
    });
})();