// wwwroot/js/support-request.js — единый POST на Razor Page (+подтверждение при 409)
(() => {
    if (window.self !== window.top) {
        document.documentElement.classList.add('embedded');
        document.addEventListener('DOMContentLoaded', () => document.body.classList.add('embedded'));
    }

    const form = document.getElementById('supportForm');
    if (!form) return;

    // --- локальная капча (не блокирует отправку, если бэка нет) ---
    const box = document.getElementById('captchaContainerLocal');
    const img = document.getElementById('captchaImage');
    const btn = document.getElementById('captchaRefresh');
    const ans = document.getElementById('captchaAnswer');
    let captchaId = null;

    async function loadCaptcha() {
        try {
            const r = await fetch('/api/Support/captcha/new', { cache: 'no-store', credentials: 'same-origin' });
            if (!r.ok) throw 0;
            const data = await r.json();
            captchaId = data?.id || null;
            if (img) img.src = `/api/Support/captcha/image/${encodeURIComponent(captchaId)}?t=${Date.now()}`;
            if (ans) ans.value = '';
        } catch { /* ok */ }
    }
    btn?.addEventListener('click', () => loadCaptcha());
    document.addEventListener('DOMContentLoaded', () => { if (box) box.style.display = 'flex'; loadCaptcha(); });

    // --- UI: лоадер + слайд-панель ---
    const card = form.closest('.support-card');
    let loader = card?.querySelector('.sr-loader');
    let panel = card?.querySelector('.sr-result');
    if (!loader) { loader = document.createElement('div'); loader.className = 'sr-loader'; loader.innerHTML = '<div class="sr-spinner" aria-label="Загрузка"></div>'; card.appendChild(loader); }
    if (!panel) {
        panel = document.createElement('div');
        panel.className = 'sr-result';
        panel.innerHTML = `
      <h3 id="srTitle">Подтверждение</h3>
      <p id="srText">—</p>
      <div class="sr-actions">
        <button type="button" class="sr-btn primary"   id="srConfirm">Ок</button>
        <button type="button" class="sr-btn secondary" id="srCancel">Закрыть</button>
      </div>`;
        card.appendChild(panel);
    }
    const $ = (s, r = panel) => r.querySelector(s);
    const title = $('#srTitle'), text = $('#srText');
    const btnYes = $('#srConfirm'), btnNo = $('#srCancel');
    const showLoader = on => { if (on) { loader.classList.add('is-on'); card.setAttribute('aria-busy', 'true'); } else { loader.classList.remove('is-on'); card.removeAttribute('aria-busy'); } };
    const showPanel = on => panel.classList[on ? 'add' : 'remove']('show');

    // --- helpers ---
    const csrfMeta = () => document.querySelector('meta[name="csrf-token"]')?.content || '';
    const aftInput = () => /** @type {HTMLInputElement|null} */(document.querySelector('input[name="__RequestVerificationToken"]'));
    const antiToken = () => aftInput()?.value || csrfMeta();

    function getModel() {
        const fd = new FormData(form);
        return {
            fullName: (fd.get('fullName') || '').toString().trim(),
            login: (fd.get('login') || '').toString().trim(),
            replyTo: (fd.get('replyTo') || '').toString().trim(),
            subject: (fd.get('subject') || '').toString().trim(),
            message: (fd.get('message') || '').toString().trim(),
            captchaAnswer: (fd.get('captchaAnswer') || '').toString().trim(),
            captchaToken: (fd.get('captchaToken') || '').toString().trim()
        };
    }

    function validate(m) {
        if (!m.subject || m.subject.length < 3) return 'Заполните тему (минимум 3 символа).';
        if (!m.message || m.message.length < 5) return 'Заполните текст обращения (минимум 5 символов).';
        if (m.replyTo && !/^\S+@\S+\.\S+$/.test(m.replyTo)) return 'Почта указана неверно.';
        return null;
    }

    async function postForm(model, confirmCreate = false) {
        const url = '/Support/Request';                       // один Razor-хендлер
        const data = new URLSearchParams();
        const tok = antiToken();

        // биндинг в RequestModel.Input.*
        if (model.fullName != null) data.set('Input.FullName', model.fullName);
        if (model.login != null) data.set('Input.Login', model.login);
        if (model.replyTo != null) data.set('Input.ReplyTo', model.replyTo);
        if (model.subject != null) data.set('Input.Subject', model.subject);
        if (model.message != null) data.set('Input.Message', model.message);
        if (model.captchaAnswer != null) data.set('Input.CaptchaAnswer', model.captchaAnswer);
        if (model.captchaToken != null) data.set('Input.CaptchaToken', model.captchaToken);
        if (captchaId) data.set('Input.CaptchaId', captchaId);          // на будущее
        if (confirmCreate) data.set('Input.ConfirmCreate', 'true');     // ключевой флаг
        if (tok) data.set('__RequestVerificationToken', tok);

        const res = await fetch(url, {
            method: 'POST',
            credentials: 'same-origin',
            headers: {
                'Content-Type': 'application/x-www-form-urlencoded; charset=UTF-8',
                'RequestVerificationToken': tok
            },
            body: data
        });

        const raw = await res.text().catch(() => '');
        let json = null; try { json = raw ? JSON.parse(raw) : null; } catch { }
        return { ok: res.ok, status: res.status, json, raw };
    }

    async function doSubmit(model) {
        showLoader(true);
        try {
            // 1-я попытка — без ConfirmCreate
            let r = await postForm(model, false);

            // если аккаунта нет — вернётся 409 + code=account_absent + suggestedLogin
            if (r.status === 409 && r.json?.code === 'account_absent') {
                const suggested = r.json?.suggestedLogin || model.login || model.replyTo || 'без логина';
                title.textContent = 'Создать учётную запись?';
                text.textContent = `Пользователь «${suggested}» не найден.\nСоздать нового и отправить обращение?` +
                    (model.replyTo ? `\nДанные для входа отправим на ${model.replyTo}.` : '');

                btnYes.style.display = '';
                btnYes.textContent = 'Создать и отправить';
                btnYes.onclick = async () => {
                    showPanel(false);
                    showLoader(true);
                    try {
                        // 2-я попытка — с ConfirmCreate=true
                        const r2 = await postForm(model, true);
                        if (!r2.ok || !r2.json?.success) throw new Error(r2.json?.error || `HTTP ${r2.status}`);

                        title.textContent = r2.json?.created ? 'Учётная запись создана' : 'Обращение отправлено';
                        const lines = [];
                        if (r2.json?.ticketId) lines.push(`Номер заявки: ${r2.json.ticketId}`);
                        if (r2.json?.login) lines.push(`Логин: ${r2.json.login}`);
                        if (r2.json?.email) lines.push(`Почта: ${r2.json.email}`);
                        if (r2.json?.created) lines.push('Мы отправили письмо с данными для входа.');
                        text.textContent = lines.join('\n') || 'Готово.';

                        btnYes.style.display = 'none';
                        btnNo.textContent = 'Закрыть';
                        btnNo.onclick = () => showPanel(false);
                        showPanel(true);

                        try { form.reset(); } catch { }
                        loadCaptcha();
                    } catch (e) {
                        title.textContent = 'Ошибка';
                        text.textContent = e?.message || 'Не удалось отправить обращение.';
                        btnYes.style.display = 'none';
                        btnNo.textContent = 'Понятно';
                        btnNo.onclick = () => showPanel(false);
                        showPanel(true);
                    } finally {
                        showLoader(false);
                    }
                };

                btnNo.style.display = '';
                btnNo.textContent = 'Отмена';
                btnNo.onclick = () => showPanel(false);

                showLoader(false);
                showPanel(true);
                return;
            }

            // успех с первой попытки
            if (r.ok && r.json?.success) {
                title.textContent = r.json?.created ? 'Учётная запись создана' : 'Обращение отправлено';
                const lines = [];
                if (r.json?.ticketId) lines.push(`Номер заявки: ${r.json.ticketId}`);
                if (r.json?.login) lines.push(`Логин: ${r.json.login}`);
                if (r.json?.email) lines.push(`Почта: ${r.json.email}`);
                if (r.json?.created) lines.push('Мы отправили письмо с данными для входа.');
                text.textContent = lines.join('\n') || 'Готово.';

                btnYes.style.display = 'none';
                btnNo.textContent = 'Закрыть';
                btnNo.onclick = () => showPanel(false);
                showPanel(true);

                try { form.reset(); } catch { }
                loadCaptcha();
                return;
            }

            // прочие ошибки
            const msg = r.json?.error || `HTTP ${r.status}` || 'Не удалось отправить.';
            title.textContent = 'Ошибка';
            text.textContent = msg;
            btnYes.style.display = 'none';
            btnNo.textContent = 'Понятно';
            btnNo.onclick = () => showPanel(false);
            showPanel(true);
        } catch (e) {
            title.textContent = 'Сбой сети';
            text.textContent = e?.message || 'Проверьте соединение и попробуйте ещё раз.';
            btnYes.style.display = 'none';
            btnNo.textContent = 'Ок';
            btnNo.onclick = () => showPanel(false);
            showPanel(true);
        } finally {
            showLoader(false);
        }
    }

    form.addEventListener('submit', (ev) => {
        ev.preventDefault();
        const model = getModel();
        const err = validate(model);
        if (err) {
            title.textContent = 'Проверьте поля';
            text.textContent = err;
            btnYes.style.display = 'none';
            btnNo.textContent = 'Ок';
            btnNo.onclick = () => showPanel(false);
            showPanel(true);
            return;
        }
        doSubmit(model);
    });
})();
