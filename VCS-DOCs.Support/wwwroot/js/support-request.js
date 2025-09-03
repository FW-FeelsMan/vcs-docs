(() => {
    if (window.self !== window.top) {
        document.documentElement.classList.add('embedded');
        document.addEventListener('DOMContentLoaded', () => document.body.classList.add('embedded'));
    }

    const box = document.getElementById('captchaContainerLocal');
    const img = document.getElementById('captchaImage');
    const btn = document.getElementById('captchaRefresh');
    const ans = document.getElementById('captchaAnswer');
    const form = document.getElementById('supportForm');

    let captchaId = null;

    async function loadCaptcha() {
        try {
            const r = await fetch('/api/Support/captcha/new', {
                cache: 'no-store',
                credentials: 'same-origin'
            });
            if (!r.ok) {
                const t = await r.text().catch(() => '');
                throw new Error(`new captcha http ${r.status} ${t}`);
            }
            const data = await r.json();
            captchaId = data.id;
            if (img) img.src = `/api/Support/captcha/image/${encodeURIComponent(captchaId)}?t=${Date.now()}`;
            if (ans) ans.value = '';
        } catch (e) {
            console.error('captcha/new failed:', e);
            alert('Не удалось получить капчу.');
        }
    }

    btn?.addEventListener('click', () => loadCaptcha());
    document.addEventListener('DOMContentLoaded', () => { if (box) box.style.display = 'flex'; loadCaptcha(); });

    form?.addEventListener('submit', async (e) => {
        e.preventDefault();
        const f = e.target;
        const payload = {
            fullName: f.fullName?.value || null,
            login: f.login?.value || null,
            replyTo: f.replyTo?.value || '',
            subject: f.subject?.value || '',
            message: f.message?.value || '',
            code: null, originalPath: null, traceId: null,
            userAgent: navigator.userAgent,
            captchaId,
            captchaAnswer: ans?.value?.trim() || null
        };

        try {
            // Если тикет у тебя тоже на WEB — добавь ниже прокси-экшен (см. шаг 3) и оставь относительный путь:
            const res = await fetch('/api/Support/ticket', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                credentials: 'same-origin',
                body: JSON.stringify(payload)
            });

            if (!res.ok) {
                const txt = await res.text().catch(() => '');
                try {
                    const j = JSON.parse(txt);
                    if (j && j.errors && Array.isArray(j.errors) && j.errors.length) {
                        alert('Проверьте поля:\n- ' + j.errors.join('\n- '));
                        return;
                    }
                } catch { }
                alert('Не удалось отправить обращение. Код: ' + res.status + (txt ? '\n' + txt : ''));
                return;
            }

            const data = await res.json().catch(() => ({}));
            alert('Спасибо! Ваше обращение принято' + (data.ticketId ? ' (#' + data.ticketId + ')' : '') + '.');
            f.reset();
            loadCaptcha();
        } catch (e) {
            console.error(e);
            alert('Сбой сети при отправке обращения.');
        }
    });
})();
