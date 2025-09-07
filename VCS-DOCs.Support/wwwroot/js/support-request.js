// wwwroot/js/support-request.js
(() => {
    // Если страница во фрейме — помечаем классами (для CSS)
    if (window.self !== window.top) {
        document.documentElement.classList.add('embedded');
        document.addEventListener('DOMContentLoaded', () => document.body.classList.add('embedded'));
    }

    const form = document.getElementById('supportForm');
    if (!form) return;

    // --- КАПЧА (локальная) ---
    const box = document.getElementById('captchaContainerLocal');
    const img = document.getElementById('captchaImage');
    const btn = document.getElementById('captchaRefresh');
    const ans = document.getElementById('captchaAnswer');

    let captchaId = null;

    async function loadCaptcha() {
        try {
            const r = await fetch('/api/Support/captcha/new', {
                cache: 'no-store',
                credentials: 'same-origin'
            });
            if (!r.ok) throw new Error(`new captcha http ${r.status}`);
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

    // --- UI: оверлей-лоадер и панель результата ---
    const card = form.closest('.support-card');
    if (!card) return;

    // оверлей + спиннер
    let loader = card.querySelector('.sr-loader');
    if (!loader) {
        loader = document.createElement('div');
        loader.className = 'sr-loader';
        loader.innerHTML = '<div class="sr-spinner" aria-label="Загрузка"></div>';
        card.appendChild(loader);
    }
    const showLoader = (on) => {
        if (on) { loader.classList.add('is-on'); card.setAttribute('aria-busy', 'true'); }
        else { loader.classList.remove('is-on'); card.removeAttribute('aria-busy'); }
    };

    // панель результата
    let panel = card.querySelector('.sr-result');
    if (!panel) {
        panel = document.createElement('div');
        panel.className = 'sr-result';
        panel.innerHTML = `
          <div>
            <h3 id="sr-title">Готово</h3>
            <p id="sr-text" class="sr-text">Сообщение отправлено.</p>
          </div>
          <div class="sr-actions">
            <button id="sr-primary"  class="sr-btn primary"   data-click="once">Ок</button>
            <button id="sr-secondary"class="sr-btn secondary" data-click="once">Закрыть</button>
          </div>`;
        card.appendChild(panel);
    }
    const $ = (sel) => panel.querySelector(sel);
    const hideResult = () => panel.classList.remove('show');
    function showResult({ title, text, primary, secondary }) {
        $('#sr-title').textContent = title || 'Готово';
        $('#sr-text').textContent = text || '';

        // снимаем старые обработчики, заменяя на клоны
        const oldP = $('#sr-primary'), oldS = $('#sr-secondary');
        const p = oldP.cloneNode(true), s = oldS.cloneNode(true);
        oldP.replaceWith(p); oldS.replaceWith(s);

        if (primary && typeof primary.onClick === 'function') {
            p.textContent = primary.text || 'Ок';
            p.style.display = '';
            p.disabled = false;
            p.setAttribute('data-click', primary.dataClick || 'once');
            p.addEventListener('click', primary.onClick);
        } else { p.style.display = 'none'; }

        if (secondary && typeof secondary.onClick === 'function') {
            s.textContent = secondary.text || 'Закрыть';
            s.style.display = '';
            s.disabled = false;
            s.setAttribute('data-click', secondary.dataClick || 'once');
            s.addEventListener('click', secondary.onClick);
        } else { s.style.display = 'none'; }

        panel.classList.add('show');
    }

    // --- helpers ---
    const csrfMeta = () => document.querySelector('meta[name="csrf-token"]')?.content || '';
    const aftInput = () => /** @type {HTMLInputElement|null} */(document.querySelector('input[name="__RequestVerificationToken"]'));
    const antiforgery = () => aftInput()?.value || csrfMeta(); // предпочитаем hidden-поле

    // универсальный fetch с идемпотентностью (если твой щит подгружен)
    function ffetch(url, opts = {}) {
        if (typeof window.fetchSafe === 'function') return window.fetchSafe(url, opts);
        return fetch(url, { credentials: 'same-origin', cache: 'no-store', ...opts });
    }

    // POST Razor Page (x-www-form-urlencoded, поля Input.* + __RequestVerificationToken)
    async function postRazor(model) {
        const url = form.getAttribute('action') || '/Support/Request';
        const data = new URLSearchParams();
        if (model.fullName != null) data.set('Input.FullName', model.fullName);
        if (model.login != null) data.set('Input.Login', model.login);
        if (model.replyTo != null) data.set('Input.ReplyTo', model.replyTo);
        if (model.subject != null) data.set('Input.Subject', model.subject);
        if (model.message != null) data.set('Input.Message', model.message);
        if (model.captchaAnswer != null) data.set('Input.CaptchaAnswer', model.captchaAnswer);
        if (model.captchaToken != null) data.set('Input.CaptchaToken', model.captchaToken);
        // если нужно — можно добавить: data.set('Input.CaptchaId', captchaId);

        const token = antiforgery();
        if (token) data.set('__RequestVerificationToken', token); // главное — положить в тело формы

        const headers = {
            'Content-Type': 'application/x-www-form-urlencoded; charset=UTF-8',
            'RequestVerificationToken': token // и в заголовок — на всякий случай
        };

        const res = await ffetch(url, { method: 'POST', headers, body: data });
        const text = await res.text().catch(() => '');
        let json = null;
        try { json = JSON.parse(text || '{}'); } catch { /* не JSON — вероятно HTML (ошибка 400/500) */ }
        return { res, json, text, tried: 'razor', url };
    }

    // POST API (JSON) — fallback маршрут (если Razor Page не ответила JSON-ом)
    async function postApi(payload) {
        const url = '/api/Support/ticket';
        const token = antiforgery();
        const headers = { 'Content-Type': 'application/json', 'RequestVerificationToken': token };
        const res = await ffetch(url, { method: 'POST', headers, body: JSON.stringify(payload) });
        const text = await res.text().catch(() => '');
        let json = null;
        try { json = JSON.parse(text || '{}'); } catch { /* ок */ }
        return { res, json, text, tried: 'api', url };
    }

    function resetFormAndCaptcha() { try { form.reset(); } catch { } loadCaptcha(); }

    // --- submit ---
    form.addEventListener('submit', async (e) => {
        e.preventDefault();
        const f = e.target;

        const model = {
            fullName: f.fullName?.value?.trim() || null,
            login: f.login?.value?.trim() || null,
            replyTo: f.replyTo?.value?.trim() || '',
            subject: f.subject?.value?.trim() || '',
            message: f.message?.value?.trim() || '',
            captchaAnswer: ans?.value?.trim() || null,
            captchaToken: null
        };

        // мини-проверка
        if (!model.replyTo || !/^\S+@\S+\.\S+$/.test(model.replyTo)) { alert('Укажите корректную почту для ответа.'); return; }
        if (!model.subject || !model.message) { alert('Заполните тему и текст обращения.'); return; }

        // payload для API (включая captchaId)
        const apiPayload = {
            fullName: model.fullName,
            login: model.login,
            replyTo: model.replyTo,
            subject: model.subject,
            message: model.message,
            code: null, originalPath: null, traceId: null,
            userAgent: navigator.userAgent,
            captchaId,
            captchaAnswer: model.captchaAnswer
        };

        showLoader(true);
        try {
            // 1) пробуем Razor Page
            let r = await postRazor(model);

            // успех Razor Page — ok + JSON { success:true }
            if (r.res.ok && r.json && r.json.success === true) {
                showLoader(false);
                const created = !!r.json.created;
                showResult({
                    title: created ? 'Учётная запись создана' : 'Запрос создан',
                    text: created
                        ? 'Мы создали учётную запись и отправили письмо на указанную почту.'
                        : 'Данные отправлены. Мы ответим на указанную почту.',
                    primary: { text: 'На главную', onClick: () => { location.href = 'https://vcs-docs.local:7120/'; } },
                    secondary: { text: 'Закрыть', onClick: () => { hideResult(); resetFormAndCaptcha(); } }
                });
                return;
            }

            // 2) fallback на API, если 404 / не-JSON / success !== true
            const needFallback = (r.res.status === 404) || !r.json || (r.json && r.json.success !== true);
            if (needFallback) {
                // если пользователь логин не вводил — пробуем попросить API автосоздать учётку
                if (!model.login) {
                    apiPayload.forceCreateAccount = true; // сработает только если API это поддерживает
                }

                r = await postApi(apiPayload);

                if (r.res.ok) {
                    showLoader(false);
                    const createdByApi = !!(r.json && (r.json.created || r.json.userCreated || r.json.status === 'user_created'));
                    const loginHint = r.json?.login ? ` Логин: ${r.json.login}.` : '';
                    const ticketHint = r.json?.ticketId ? ` Номер запроса: #${r.json.ticketId}.` : '';

                    showResult({
                        title: createdByApi ? 'Учётная запись создана' : 'Запрос создан',
                        text: createdByApi
                            ? ('Мы создали учётную запись и отправили письмо на указанную почту.' + loginHint + ticketHint)
                            : ('Данные отправлены на почту.' + ticketHint),
                        primary: { text: 'На главную', onClick: () => { location.href = 'https://vcs-docs.local:7120/'; } },
                        secondary: { text: 'Закрыть', onClick: () => { hideResult(); resetFormAndCaptcha(); } }
                    });
                    return;
                }
            }

            // оба варианта не сработали — показываем ошибку из последнего ответа
            showLoader(false);
            const msg = (r.json && r.json.error) ? r.json.error : (`Код: ${r.res.status}` + (r.text ? ' · ' + r.text : ''));
            showResult({
                title: 'Не удалось отправить',
                text: msg,
                primary: { text: 'Назад к форме', onClick: () => hideResult() }
            });

        } catch (err) {
            console.error('[support-request] submit error', err);
            showLoader(false);
            showResult({
                title: 'Сбой сети',
                text: 'Проверьте соединение и попробуйте ещё раз.',
                primary: { text: 'Повторить', onClick: () => { hideResult(); form.requestSubmit(); } },
                secondary: { text: 'Закрыть', onClick: () => hideResult() }
            });
        }
    });
})();
