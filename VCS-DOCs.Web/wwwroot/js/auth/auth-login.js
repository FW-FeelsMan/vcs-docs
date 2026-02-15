// auth-login.js — вход/регистрация (Web)
(() => {
    'use strict';

    const signUpButton = document.getElementById('signUp');
    const signInButton = document.getElementById('signIn');
    const container = document.getElementById('container');

    // ===== Loader =====
    const loaderOverlay = document.createElement('div');
    loaderOverlay.className = 'loader-overlay';
    loaderOverlay.innerHTML = `<div class="loader"></div>`;
    document.body.appendChild(loaderOverlay);
    hideLoader();

    function showLoader() { loaderOverlay.style.display = 'flex'; }
    function hideLoader() { loaderOverlay.style.display = 'none'; }

    signUpButton?.addEventListener('click', () => container?.classList.add('right-panel-active'));
    signInButton?.addEventListener('click', () => container?.classList.remove('right-panel-active'));

    // ===== Вспомогательные =====
    function isPrintableKey(e) {
        try {
            if (!e || typeof e.key !== 'string') return false;
            if (e.ctrlKey || e.metaKey || e.altKey) return false;
            const k = e.key;
            if (k.length > 1 && k !== ' ') return false;
            return true;
        } catch {
            return false;
        }
    }

    function blinkHint(hintEl, inputEl) {
        if (!hintEl) return;
        hintEl.style.display = 'inline';
        inputEl?.classList.add('over-limit');
        clearTimeout(hintEl.__hideTimer);
        hintEl.__hideTimer = setTimeout(() => {
            hintEl.style.display = 'none';
            inputEl?.classList.remove('over-limit');
        }, 1500);
    }

    // ===== Лимиты длины и подсказки "не более N символов" =====
    function attachMaxLenHints(root = document) {
        const inputs = root.querySelectorAll('input[maxlength], input[data-maxlen], textarea[maxlength], textarea[data-maxlen]');
        inputs.forEach(inp => {
            const form = inp.closest('form') || root;
            const name = inp.getAttribute('name') || inp.getAttribute('id') || '';
            const aspFor = inp.getAttribute('asp-for') || '';
            const max = parseInt(inp.getAttribute('data-maxlen') || inp.getAttribute('maxlength') || '0', 10);

            // 1) сначала ищем ХИНТ в пределах текущей формы
            let hint =
                form.querySelector(`.field-hint[data-hint-for="${name}"]`) ||
                (aspFor ? form.querySelector(`.field-hint[data-hint-for="${aspFor}"]`) : null) ||
                (inp.id ? form.querySelector(`.field-hint[data-hint-for="${inp.id}"]`) : null);

            // 2) если в форме нет — пробуем глобально (как запасной вариант)
            if (!hint) {
                hint =
                    root.querySelector(`.field-hint[data-hint-for="${name}"]`) ||
                    (aspFor ? root.querySelector(`.field-hint[data-hint-for="${aspFor}"]`) : null) ||
                    (inp.id ? root.querySelector(`.field-hint[data-hint-for="${inp.id}"]`) : null);
            }

            function update() {
                if (!max) return;
                if (inp.value.length > max) inp.value = inp.value.substring(0, max);
                if (hint) hint.style.display = inp.value.length >= max ? 'inline' : 'none';
            }

            function onKeyDown(e) {
                if (!max) return;
                if (!isPrintableKey(e)) return;
                const hasSelection = inp.selectionStart !== inp.selectionEnd;
                if (inp.value.length >= max && !hasSelection) {
                    e.preventDefault();
                    blinkHint(hint, inp);
                }
            }

            inp.addEventListener('keydown', onKeyDown);
            inp.addEventListener('input', update);
            inp.addEventListener('paste', () => setTimeout(update, 0));
            update();
        });
    }

    // ===== Валидация полей (логин — только латиница/цифры; пароль — любые символы) =====
    function validateInput(event, isPassword = false) {
        const input = event.target;
        if (!input) return;

        const attrMax = parseInt(input.getAttribute('data-maxlen') || input.getAttribute('maxlength') || '0', 10);
        const maxLen = attrMax || (isPassword ? 100 : 20);

        if (input.value.length > maxLen) {
            input.value = input.value.substring(0, maxLen);
            return;
        }

        // Только для Username: режем запрещённые символы
        if (!isPassword) {
            if (!/^[a-zA-Z0-9._-]*$/.test(input.value)) {
                input.value = input.value.replace(/[^a-zA-Z0-9._-]/g, '');
            }
        }
    }

    function wireUsernamePasswordValidation() {
        // Login form
        document.querySelectorAll('.sign-in-container input[name="Username"]').forEach(i => {
            i.addEventListener('input', e => validateInput(e, false));
        });
        document.querySelectorAll('.sign-in-container input[name="Password"]').forEach(i => {
            i.addEventListener('input', e => validateInput(e, true));
        });

        // Register form (и basic, и org — оба используют name=Username/Password)
        document.querySelectorAll('.sign-up-container input[name="Username"]').forEach(i => {
            i.addEventListener('input', e => validateInput(e, false));
        });
        document.querySelectorAll('.sign-up-container input[name="Password"]').forEach(i => {
            i.addEventListener('input', e => validateInput(e, true));
        });
    }

    document.addEventListener('DOMContentLoaded', () => {
        attachMaxLenHints(document);
        wireUsernamePasswordValidation();
    });

    // ===== HardwareId =====
    const hardwareId = navigator.userAgent;
    document.getElementById('hardwareId')?.setAttribute('value', hardwareId);
    document.getElementById('hardwareIdRegister')?.setAttribute('value', hardwareId);

    // ===== Message =====
    const params = new URLSearchParams(window.location.search);
    if (params.get('message') === 'session_terminated') {
        alert('Вы были разлогинены, так как выполнен вход с другого устройства');
    }

    // ===== Submit =====
    async function submitForm(event, url, errorSelector, successRedirect = null) {
        event.preventDefault();
        const form = event.target;
        const formData = new FormData(form);
        showLoader();

        try {
            const response = await fetch(url, {
                method: 'POST',
                body: formData,
                credentials: 'same-origin'
            });

            if (response.redirected) {
                window.location.href = response.url;
                return;
            }

            const ct = (response.headers.get('content-type') || '').toLowerCase();
            const raw = await response.text();

            let result = null;
            if (ct.includes('application/json')) {
                result = JSON.parse(raw);
            } else {
                try { result = JSON.parse(raw); }
                catch {
                    const snippet = raw.slice(0, 400);
                    console.error('Ответ сервера не JSON:', snippet);
                    alert('Сервер вернул HTML вместо JSON.\n\nКратко:\n' + snippet);
                    return;
                }
            }

            const errorMessage = document.querySelector(errorSelector);
            if (result && result.success) {
                if (successRedirect) {
                    window.location.href = successRedirect;
                } else {
                    document.querySelector('.successful-message')?.setAttribute('style', 'display:block');
                    if (errorMessage) errorMessage.style.display = 'none';
                }
            } else {
                const errs = (result && (result.errors || result.details)) || null;
                if (errorMessage) {
                    if (Array.isArray(errs)) {
                        errorMessage.innerHTML = errs.map(e => `<p>${e}</p>`).join('');
                    } else if (errs && typeof errs === 'object') {
                        const flat = Object.entries(errs).flatMap(([k, arr]) => (arr || []).map(x => `${k}: ${x}`));
                        errorMessage.innerHTML = flat.map(e => `<p>${e}</p>`).join('');
                    } else {
                        errorMessage.textContent = (result && (result.error || 'Ошибка')) || 'Ошибка';
                    }
                    errorMessage.style.display = 'block';
                } else {
                    alert((Array.isArray(errs) ? errs : [result?.error || 'Ошибка']).join('\n'));
                }
            }
        } catch (err) {
            console.error('Ошибка fetch/парсинга:', err);
            alert('Произошла ошибка сети или парсинга ответа. Попробуйте позже.');
        } finally {
            hideLoader();
        }
    }

    document.querySelector('.sign-up-container form')?.addEventListener('submit', e =>
        submitForm(e, window.location.pathname + '?handler=Register', '.error-message-registration')
    );
    document.querySelector('.sign-in-container form')?.addEventListener('submit', e =>
        submitForm(e, window.location.pathname + '?handler=Login', '.error-message', '/')
    );

    // ===== Helpers: enable/disable sections (важно, чтобы FormData не тащил скрытые дубли) =====
    function setSectionEnabled(sectionEl, enabled) {
        if (!sectionEl) return;
        const fields = sectionEl.querySelectorAll('input, select, textarea, button');
        fields.forEach(el => {
            // Не трогаем сабмит кнопки формы — их контролирует Razor (disabled) и логика UI
            if (el.matches('button[type="submit"]')) return;

            // Если поле должно участвовать в сабмите — оно НЕ disabled
            // Если секция скрыта — отключаем, чтобы не было дублей name=Username/Password...
            el.disabled = !enabled;

            // required тоже надо корректировать, иначе hidden-required ломает submit в некоторых браузерах
            if (el instanceof HTMLInputElement || el instanceof HTMLSelectElement || el instanceof HTMLTextAreaElement) {
                const wasReq = el.getAttribute('data-was-required');
                if (!enabled) {
                    if (el.required && !wasReq) el.setAttribute('data-was-required', '1');
                    el.required = false;
                } else {
                    if (wasReq) {
                        el.required = true;
                        el.removeAttribute('data-was-required');
                    }
                }
            }
        });
    }

    // ===== Org registration mode toggle (robust) =====
    (function () {
        const ORG_LABEL = 'Зарегистрировать организацию';

        function normalize(s) {
            return (s || '').replace(/\s+/g, ' ').trim();
        }

        function getSelect() {
            return document.querySelector('.sign-up-container select[name="speciality"]');
        }

        function isOrgSelected(sel) {
            if (!sel) return false;
            const target = Array.from(sel.options || [])
                .find(o => normalize(o.textContent) === ORG_LABEL);

            if (!target) return false;
            return normalize(sel.value) === normalize(target.value);
        }

        function updateOrgMode() {
            if (!container) return;

            const isSignUp = container.classList.contains('right-panel-active');
            const sel = getSelect();

            const enableOrg = isSignUp && isOrgSelected(sel);
            container.classList.toggle('org-register-mode', enableOrg);

            // ВАЖНО: отключаем неактуальные поля, чтобы не было дублей в POST
            const regBasic = document.querySelector('.sign-up-container .reg-basic');
            const orgGrid = document.querySelector('.sign-up-container .org-grid');

            if (enableOrg) {
                setSectionEnabled(regBasic, false);
                setSectionEnabled(orgGrid, true);
            } else {
                setSectionEnabled(orgGrid, false);
                setSectionEnabled(regBasic, true);
            }
        }

        // клики туда/обратно
        signUpButton?.addEventListener('click', () => setTimeout(updateOrgMode, 0));
        signInButton?.addEventListener('click', () => {
            container?.classList.remove('org-register-mode');
            setTimeout(updateOrgMode, 0);
        });

        // изменения селекта
        document.addEventListener('change', (e) => {
            const t = e.target;
            if (t && t.matches('.sign-up-container select[name="speciality"]')) {
                updateOrgMode();
            }
        });

        // если правую панель включили не кликом (класс меняется) — тоже отследим
        const mo = new MutationObserver(() => updateOrgMode());
        if (container) {
            mo.observe(container, { attributes: true, attributeFilter: ['class'] });
        }

        document.addEventListener('DOMContentLoaded', updateOrgMode);
        setTimeout(updateOrgMode, 0);
    })();
})();
