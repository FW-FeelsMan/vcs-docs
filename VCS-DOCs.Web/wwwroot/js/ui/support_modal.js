(function () {
    "use strict";

    const modal = document.getElementById('supportModal');
    const btnOpen = document.getElementById('btnSupport');
    const btnClose = document.getElementById('supClose');
    const btnSend = document.getElementById('supSend');
    if (!modal || !btnOpen || !btnClose || !btnSend) return;

    const capEnabled = (modal.dataset.captchaEnabled || 'false') === 'true';
    const capProvider = (modal.dataset.captchaProvider || 'ReCaptchaV2');
    const capSiteKey = modal.dataset.captchaSitekey || '';

    // --- элементы для LocalCaptcha
    const localBox = document.getElementById('captchaContainerLocal');
    const localImg = document.getElementById('captchaImage');
    const localRefresh = document.getElementById('captchaRefresh');
    const localInput = document.getElementById('captchaAnswer');

    // --- элементы для ReCaptcha v2
    const v2Box = document.getElementById('captchaContainerV2');
    let recaptchaWidgetId = null;

    // текущее состояние LocalCaptcha
    let currentCaptchaId = null;

    // ====== ReCaptcha v2 ======
    let waitingCbs = [];
    let scriptInjected = false;
    window.__recaptchaV2Ready = function () {
        const t0 = Date.now();
        const timer = setInterval(() => {
            if (window.grecaptcha && typeof window.grecaptcha.render === 'function') {
                clearInterval(timer);
                const cbs = waitingCbs.slice(); waitingCbs.length = 0;
                cbs.forEach(fn => { try { fn(); } catch { } });
            } else if (Date.now() - t0 > 4000) {
                clearInterval(timer);
                const cbs = waitingCbs.slice(); waitingCbs.length = 0;
                cbs.forEach(fn => { try { fn(new Error('recaptcha-v2-not-ready')); } catch { } });
            }
        }, 100);
    };
    function loadReCaptchaScript(cb) {
        if (window.grecaptcha && typeof window.grecaptcha.render === 'function') { cb && cb(); return; }
        waitingCbs.push(cb || function () { });
        if (scriptInjected || document.getElementById('recaptcha-api-js')) return;
        const s = document.createElement('script');
        s.id = 'recaptcha-api-js';
        s.src = 'https://www.google.com/recaptcha/api.js?onload=__recaptchaV2Ready&render=explicit';
        s.async = true; s.defer = true; scriptInjected = true;
        document.head.appendChild(s);
    }
    function ensureV2Rendered() {
        if (!capEnabled || capProvider !== 'ReCaptchaV2' || !capSiteKey) return;
        if (!v2Box) return;
        v2Box.style.display = 'block';
        if (recaptchaWidgetId !== null && window.grecaptcha && grecaptcha.reset) {
            try { grecaptcha.reset(recaptchaWidgetId); } catch { }
            return;
        }
        loadReCaptchaScript(function (err) {
            if (err) { alert('Капча недоступна.'); return; }
            try { recaptchaWidgetId = grecaptcha.render(v2Box, { sitekey: capSiteKey }); }
            catch (e) { console.error(e); alert('Не удалось инициализировать капчу.'); }
        });
    }

    // ====== LocalCaptcha ======
    async function refreshLocalCaptcha() {
        try {
            const r = await fetch('/api/Support/captcha/new', { cache: 'no-store' });
            if (!r.ok) throw new Error('new captcha http ' + r.status);
            const data = await r.json();
            currentCaptchaId = data.id;
            if (localImg) {
                localImg.src = `/api/Support/captcha/image/${currentCaptchaId}?t=${Date.now()}`;
            }
            if (localInput) localInput.value = '';
        } catch (e) {
            console.error(e);
            alert('Не удалось получить капчу.');
        }
    }

    function showLocalCaptcha() {
        if (localBox) {
            localBox.style.display = 'flex';
            refreshLocalCaptcha();
        }
    }

    // ====== Modal open/close ======
    function openModal() {
        const code = modal.dataset.code || '';
        const path = modal.dataset.originalPath || '';
        const trace = modal.dataset.traceId || '';

        const subj = document.getElementById('supSubject');
        const msg = document.getElementById('supMessage');

        if (subj && !subj.value) subj.value = 'Ошибка ' + code + (path ? ' на ' + path : '');
        if (msg && !msg.value) {
            msg.value = [
                'Что произошло: ', '',
                'Шаги для воспроизведения: ', '',
                'Тех.инфо:',
                ' - Код: ' + code,
                (path ? ' - URL: ' + path : ''),
                (trace ? ' - TraceId: ' + trace : ''),
                ' - UA: ' + navigator.userAgent
            ].filter(Boolean).join('\n');
        }

        modal.style.display = 'block';
        modal.setAttribute('aria-hidden', 'false');

        if (capEnabled) {
            if (capProvider === 'LocalCaptcha') showLocalCaptcha();
            else if (capProvider === 'ReCaptchaV2') ensureV2Rendered();
        }
    }

    function closeModal() {
        modal.style.display = 'none';
        modal.setAttribute('aria-hidden', 'true');
        if (capProvider === 'ReCaptchaV2' && window.grecaptcha && typeof grecaptcha.reset === 'function' && recaptchaWidgetId !== null) {
            try { grecaptcha.reset(recaptchaWidgetId); } catch { }
        }
        // local captcha не надо сбрасывать — при следующем открытии обновим
    }

    btnOpen.addEventListener('click', openModal);
    btnClose.addEventListener('click', closeModal);
    modal.addEventListener('click', (e) => { if (e.target === modal) closeModal(); });
    if (localRefresh) localRefresh.addEventListener('click', refreshLocalCaptcha);

    // ====== Send ======
    btnSend.addEventListener('click', async function () {
        const fullName = (document.getElementById('supFullName')?.value || '').trim();
        const login = (document.getElementById('supLogin')?.value || '').trim();
        const replyTo = (document.getElementById('supEmail')?.value || '').trim();
        const subject = (document.getElementById('supSubject')?.value || '').trim();
        const message = (document.getElementById('supMessage')?.value || '').trim();

        if (!replyTo || !subject || !message) {
            alert('Укажите почту для ответа, тему и текст обращения.');
            return;
        }

        let captchaToken = null;
        let captchaId = null;
        let captchaAnswer = null;

        if (capEnabled) {
            if (capProvider === 'ReCaptchaV2') {
                if (!window.grecaptcha || recaptchaWidgetId === null) {
                    alert('Капча ещё не готова.');
                    return;
                }
                captchaToken = grecaptcha.getResponse(recaptchaWidgetId);
                if (!captchaToken) { alert('Подтвердите, что вы не робот.'); return; }
            } else if (capProvider === 'LocalCaptcha') {
                captchaId = currentCaptchaId;
                captchaAnswer = (localInput?.value || '').trim();
                if (!captchaId || !captchaAnswer) {
                    alert('Заполните капчу.'); return;
                }
            }
        }

        const payload = {
            fullName: fullName || null,
            login: login || null,
            replyTo,
            subject,
            message,
            code: Number(modal.dataset.code || '0') || null,
            originalPath: modal.dataset.originalPath || null,
            traceId: modal.dataset.traceId || null,
            userAgent: navigator.userAgent,
            // reCAPTCHA v2
            captchaToken,
            // LocalCaptcha
            captchaId, captchaAnswer
        };

        try {
            const res = await fetch('/api/Support/ticket', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(payload)
            });
            if (!res.ok) {
                const txt = await res.text().catch(() => '');
                alert('Не удалось отправить обращение. Код: ' + res.status + (txt ? '\n' + txt : ''));
                return;
            }
            const data = await res.json().catch(() => ({}));
            alert('Спасибо! Ваше обращение принято' + (data.ticketId ? ' (#' + data.ticketId + ')' : '') + '.');
            closeModal();
        } catch (e) {
            console.error(e);
            alert('Сбой сети при отправке обращения.');
        }
    });
})();
