// wwwroot/js/support-request.js
(() => {
    'use strict';

    const form = document.getElementById('supportForm');
    if (!form) return;

    const isEmbedded = window.self !== window.top;
    if (isEmbedded) {
        document.documentElement.classList.add('embedded');
        document.addEventListener('DOMContentLoaded', () => document.body.classList.add('embedded'));
    }

    const allowedHosts = new Set(['vcs-docs.local', 'localhost', '127.0.0.1']);
    const allowedPorts = new Set(['7120', '5120']); // добавь порты при необходимости

    function isAllowedParent(origin) {
        try {
            const u = new URL(origin);
            return allowedHosts.has(u.hostname) && allowedPorts.has(u.port);
        } catch {
            return false;
        }
    }

    function lockInput(el) {
        if (!el) return;
        el.readOnly = true;
        el.setAttribute('aria-readonly', 'true');
        el.classList.add('is-locked');
    }

    function setValAny(selectors, value, lock) {
        const el = document.querySelector(selectors);
        if (!el) return;
        if (value != null) el.value = String(value);
        if (lock) lockInput(el);
    }

    function getReferrerOrigin() {
        try {
            return document.referrer ? new URL(document.referrer).origin : null;
        } catch {
            return null;
        }
    }

    function notifyReadyToParent() {
        if (!isEmbedded) return;

        const parentOrigin = getReferrerOrigin();
        if (!parentOrigin) return;
        if (!isAllowedParent(parentOrigin)) return;

        try {
            window.parent?.postMessage({ type: 'support.ready' }, parentOrigin);
        } catch {
            /* ignore */
        }
    }

    document.addEventListener('DOMContentLoaded', notifyReadyToParent);

    window.addEventListener('message', (ev) => {
        if (!isAllowedParent(ev.origin)) return;

        const d = ev.data;
        if (!d || d.type !== 'vdocs.prefill') return;

        const lock = !!d.lock;

        setValAny(
            '#supFullName, #fullName, #Input_FullName, input[name="fullName"], input[name="Input.FullName"]',
            d.fullName,
            lock
        );

        setValAny(
            '#supLogin, #login, #Input_Login, input[name="login"], input[name="Input.Login"]',
            d.login,
            lock
        );

        // ВАЖНО: в форме поле почты — name="replyTo"
        setValAny(
            '#supEmail, #replyTo, #Input_ReplyTo, input[name="replyTo"], input[name="Input.ReplyTo"]',
            d.email,
            lock
        );
    });

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
            if (img && captchaId) img.src = `/api/Support/captcha/image/${encodeURIComponent(captchaId)}?t=${Date.now()}`;
            if (ans) ans.value = '';
        } catch {
            /* ignore */
        }
    }

    if (btn) btn.addEventListener('click', () => loadCaptcha());
    document.addEventListener('DOMContentLoaded', () => {
        if (box) box.style.display = 'flex';
        loadCaptcha();
    });

    const card = form.closest('.support-card');
    let loader = card?.querySelector('.sr-loader');
    let panel = card?.querySelector('.sr-result');

    if (card && !loader) {
        loader = document.createElement('div');
        loader.className = 'sr-loader';
        loader.innerHTML = '<div class="sr-spinner" aria-label="Загрузка"></div>';
        card.appendChild(loader);
    }

    if (card && !panel) {
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

    const $ = (s, r = panel || document) => r.querySelector(s);
    const title = $('#srTitle');
    const text = $('#srText');
    const btnYes = $('#srConfirm');
    const btnNo = $('#srCancel');

    const showLoader = (on) => {
        if (!loader || !card) return;
        if (on) {
            loader.classList.add('is-on');
            card.setAttribute('aria-busy', 'true');
        } else {
            loader.classList.remove('is-on');
            card.removeAttribute('aria-busy');
        }
    };

    const showPanel = (on) => {
        if (!panel) return;
        panel.classList[on ? 'add' : 'remove']('show');
    };

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

    function suggestLoginFromEmail(email) {
        try {
            if (!email || !email.includes('@')) return null;
            const local = email.split('@', 2)[0];
            let s = (local || '')
                .normalize('NFKD')
                .replace(/[^\w.\-]+/g, '')
                .replace(/_+/g, '_')
                .replace(/[.\-]{2,}/g, (m) => m[0])
                .replace(/^[.\-_]+|[.\-_]+$/g, '');
            if (!s) s = 'user';
            if (!/^[A-Za-z0-9]/.test(s)) s = 'u-' + s;
            return s.slice(0, 32);
        } catch {
            return null;
        }
    }

    function validate(m) {
        if (!m.subject || m.subject.length < 3) return 'Заполните тему (минимум 3 символа).';
        if (!m.message || m.message.length < 5) return 'Заполните текст обращения (минимум 5 символов).';
        if (m.replyTo && !/^\S+@\S+\.\S+$/.test(m.replyTo)) return 'Почта указана неверно.';
        return null;
    }

    async function postForm(model, confirmCreate = false) {
        const url = '/Support/Request';
        const data = new URLSearchParams();
        const tok = antiToken();

        data.set('Input.FullName', model.fullName ?? '');
        data.set('Input.Login', model.login ?? '');
        data.set('Input.ReplyTo', model.replyTo ?? '');
        data.set('Input.Subject', model.subject ?? '');
        data.set('Input.Message', model.message ?? '');
        data.set('Input.CaptchaAnswer', model.captchaAnswer ?? '');
        data.set('Input.CaptchaToken', model.captchaToken ?? '');
        if (captchaId) data.set('Input.CaptchaId', captchaId);
        if (confirmCreate) data.set('Input.ConfirmCreate', 'true');
        if (tok) data.set('__RequestVerificationToken', tok);

        const res = await fetch(url, {
            method: 'POST',
            credentials: 'same-origin',
            headers: {
                'Content-Type': 'application/x-www-form-urlencoded; charset=UTF-8',
                RequestVerificationToken: tok
            },
            body: data
        });

        const raw = await res.text().catch(() => '');
        let json = null;
        try {
            json = raw ? JSON.parse(raw) : null;
        } catch {
            /* ignore */
        }

        return { ok: res.ok, status: res.status, json, raw };
    }

    async function doSubmit(model) {
        showLoader(true);

        try {
            let r = await postForm(model, false);

            if (r.status === 400 && r.json?.code === 'bad_login') {
                const suggested = r.json?.suggestedLogin || suggestLoginFromEmail(model.replyTo) || '';

                if (title) title.textContent = 'Исправьте логин';
                if (text) {
                    text.textContent =
                        suggested && suggested !== model.login
                            ? `Логин содержит недопустимые символы. Предложенный вариант: "${suggested}". Применить и продолжить?`
                            : 'Логин содержит недопустимые символы. Разрешены латиница, цифры и . _ - (3–32 символа).';
                }

                if (btnYes) {
                    btnYes.style.display = suggested ? '' : 'none';
                    btnYes.textContent = 'Применить';
                    btnYes.onclick = () => {
                        const el = document.getElementById('supLogin');
                        if (el && suggested) el.value = suggested;
                        showPanel(false);
                        doSubmit({ ...model, login: suggested });
                    };
                }

                if (btnNo) {
                    btnNo.style.display = '';
                    btnNo.textContent = 'Отмена';
                    btnNo.onclick = () => showPanel(false);
                }

                showLoader(false);
                showPanel(true);
                return;
            }

            if (r.status === 409 && r.json?.code === 'account_absent') {
                const who = r.json?.suggestedLogin || model.login || model.replyTo || 'без логина';

                if (title) title.textContent = 'Создать учётную запись?';
                if (text) {
                    text.textContent =
                        `Пользователь «${who}» не найден.\nСоздать нового и отправить обращение?` +
                        (model.replyTo ? `\nДанные для входа отправим на ${model.replyTo}.` : '');
                }

                if (btnYes) {
                    btnYes.style.display = '';
                    btnYes.textContent = 'Создать и отправить';
                    btnYes.onclick = async () => {
                        showPanel(false);
                        showLoader(true);
                        try {
                            const r2 = await postForm(model, true);
                            if (!r2.ok || !r2.json?.success) throw new Error(r2.json?.error || `HTTP ${r2.status}`);

                            if (title) title.textContent = r2.json?.created ? 'Учётная запись создана' : 'Обращение отправлено';

                            const lines = [];
                            if (r2.json?.ticketId) lines.push(`Номер заявки: ${r2.json.ticketId}`);
                            if (r2.json?.login) lines.push(`Логин: ${r2.json.login}`);
                            if (r2.json?.email) lines.push(`Почта: ${r2.json.email}`);
                            if (r2.json?.created) lines.push('Мы отправили письмо с данными для входа.');
                            if (text) text.textContent = lines.join('\n') || 'Готово.';

                            if (btnYes) btnYes.style.display = 'none';
                            if (btnNo) {
                                btnNo.textContent = 'Закрыть';
                                btnNo.onclick = () => showPanel(false);
                            }
                            showPanel(true);

                            try {
                                form.reset();
                            } catch {
                                /* ignore */
                            }
                            loadCaptcha();
                        } catch (e) {
                            if (title) title.textContent = 'Ошибка';
                            if (text) text.textContent = e?.message || 'Не удалось отправить обращение.';
                            if (btnYes) btnYes.style.display = 'none';
                            if (btnNo) {
                                btnNo.textContent = 'Понятно';
                                btnNo.onclick = () => showPanel(false);
                            }
                            showPanel(true);
                        } finally {
                            showLoader(false);
                        }
                    };
                }

                if (btnNo) {
                    btnNo.style.display = '';
                    btnNo.textContent = 'Отмена';
                    btnNo.onclick = () => showPanel(false);
                }

                showLoader(false);
                showPanel(true);
                return;
            }

            if (r.ok && r.json?.success) {
                if (title) title.textContent = r.json?.created ? 'Учётная запись создана' : 'Обращение отправлено';

                const lines = [];
                if (r.json?.ticketId) lines.push(`Номер заявки: ${r.json.ticketId}`);
                if (r.json?.login) lines.push(`Логин: ${r.json.login}`);
                if (r.json?.email) lines.push(`Почта: ${r.json.email}`);
                if (r.json?.created) lines.push('Мы отправили письмо с данными для входа.');
                if (text) text.textContent = lines.join('\n') || 'Готово.';

                if (btnYes) btnYes.style.display = 'none';
                if (btnNo) {
                    btnNo.textContent = 'Закрыть';
                    btnNo.onclick = () => showPanel(false);
                }
                showPanel(true);

                try {
                    form.reset();
                } catch {
                    /* ignore */
                }
                loadCaptcha();
                return;
            }

            const msg = r.json?.error || `HTTP ${r.status}` || 'Не удалось отправить.';
            if (title) title.textContent = 'Ошибка';
            if (text) text.textContent = msg;
            if (btnYes) btnYes.style.display = 'none';
            if (btnNo) {
                btnNo.textContent = 'Понятно';
                btnNo.onclick = () => showPanel(false);
            }
            showPanel(true);
        } catch (e) {
            if (title) title.textContent = 'Сбой сети';
            if (text) text.textContent = e?.message || 'Проверьте соединение и попробуйте ещё раз.';
            if (btnYes) btnYes.style.display = 'none';
            if (btnNo) {
                btnNo.textContent = 'Ок';
                btnNo.onclick = () => showPanel(false);
            }
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
            if (title) title.textContent = 'Проверьте поля';
            if (text) text.textContent = err;
            if (btnYes) btnYes.style.display = 'none';
            if (btnNo) {
                btnNo.textContent = 'Ок';
                btnNo.onclick = () => showPanel(false);
            }
            showPanel(true);
            return;
        }

        doSubmit(model);
    });
})();