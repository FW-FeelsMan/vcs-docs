// wwwroot/js/user/user_new_ticket.js
(() => {
    const csrfMeta = () => document.querySelector('meta[name="csrf-token"]')?.content || '';
    const aftInput = () => /** @type {HTMLInputElement|null} */(document.querySelector('input[name="__RequestVerificationToken"]'));
    const anti = () => aftInput()?.value || csrfMeta();

    async function postJson(url, body) {
        const token = anti();
        const res = await fetch(url, {
            method: 'POST',
            credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json; charset=utf-8', ...(token ? { 'RequestVerificationToken': token } : {}) },
            body: JSON.stringify(body ?? {})
        });
        const txt = await res.text().catch(() => '');
        let json = null; try { json = txt ? JSON.parse(txt) : null; } catch { }
        if (!res.ok) throw new Error(json?.error || ('HTTP ' + res.status));
        return json || {};
    }

    function esc(s) { return String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c])); }

    document.addEventListener('DOMContentLoaded', () => {
        const form = document.getElementById('ntForm');
        const subj = document.getElementById('ntSubject');
        const msg = document.getElementById('ntMessage');
        const btnCancel = document.getElementById('ntCancel');
        const btnSubmit = document.getElementById('ntSubmit');
        const status = document.getElementById('ntStatus');

        async function submitNow() {
            const s = (subj?.value || '').trim();
            const m = (msg?.value || '').trim();
            if (!s || s.length < 3) { status.innerHTML = '<span class="err">Укажите тему (≥3 символов).</span>'; return; }
            if (!m || m.length < 5) { status.innerHTML = '<span class="err">Опишите проблему (≥5 символов).</span>'; return; }

            btnSubmit.disabled = true;
            status.textContent = 'Отправка…';

            try {
                const r = await postJson('/api/support/self/tickets', { subject: s, message: m });
                status.innerHTML = `<span class="ok">Заявка создана. № ${esc(r.ticketId || '')}</span>`;

                // сообщим родителю (модалка закроется и список обновится)
                try { window.parent.postMessage({ kind: 'support:ticket_created', ticketId: r.ticketId }, '*'); } catch { }
            } catch (e) {
                status.innerHTML = `<span class="err">Не удалось создать заявку: ${esc(e.message || 'ошибка')}</span>`;
            } finally {
                btnSubmit.disabled = false;
            }
        }

        form?.addEventListener('submit', (e) => { e.preventDefault(); submitNow(); });
        btnCancel?.addEventListener('click', () => { try { window.parent.postMessage({ kind: 'support:ticket_cancel' }, '*'); } catch { } });
    });
})();
