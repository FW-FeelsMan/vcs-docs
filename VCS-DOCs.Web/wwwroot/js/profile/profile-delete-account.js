// wwwroot/js/profile/profile-delete-account.js
(() => {
    // guard для SPA/динамической подгрузки
    if (window.__profileDeleteAccountInit) return;
    window.__profileDeleteAccountInit = true;

    // === config ===
    const ENDPOINT = "/Content/profile_page?handler=DeleteAccount";

    // === utils ===
    const getCsrfToken = () => {
        // ВАЖНО: ты убрал meta csrf-token (Antiforgery в .cshtml не доступен),
        // поэтому берем токен из скрытого input, который рендерит @Html.AntiForgeryToken()
        const el = document.querySelector('input[name="__RequestVerificationToken"]');
        return el?.value || "";
    };

    const esc = (s) =>
        String(s ?? "").replace(/[&<>"']/g, (c) => ({
            "&": "&amp;",
            "<": "&lt;",
            ">": "&gt;",
            '"': "&quot;",
            "'": "&#39;",
        }[c]));

    // === modal ===
    function ensureDeleteModal() {
        let modal = document.getElementById("delete-account-modal");
        if (modal) return modal;

        modal = document.createElement("div");
        modal.id = "delete-account-modal";
        modal.className = "modal";
        modal.style.display = "none";
        modal.innerHTML = `
      <div class="modal-content">
        <h3 id="delete-modal-title">Удаление аккаунта</h3>

        <p id="delete-modal-message">
          Это действие <b>необратимо</b>. Аккаунт будет помечен как удалённый, и вы выйдете из системы.
        </p>

        <div class="row" style="margin:12px 0;">
          <label style="display:block; text-align:left; margin-bottom:6px;">Пароль для подтверждения</label>
          <input id="delete-account-password"
                 type="password"
                 autocomplete="current-password"
                 placeholder="Введите пароль"
                 style="width:100%; box-sizing:border-box;" />
          <div id="delete-account-error" style="display:none; margin-top:8px;"></div>
        </div>

        <div class="modal-buttons">
          <button id="delete-account-confirm" class="button-sliding danger">Удалить</button>
          <button id="delete-account-cancel" class="button-sliding">Отмена</button>
        </div>
      </div>
    `;

        document.body.appendChild(modal);

        // закрытие по клику вне контента
        modal.addEventListener("click", (e) => {
            if (e.target === modal) hideDeleteModal();
        });

        // ESC
        document.addEventListener("keydown", (e) => {
            if (e.key === "Escape" && modal.style.display !== "none") hideDeleteModal();
        });

        return modal;
    }

    function showDeleteModal() {
        const modal = ensureDeleteModal();
        const pass = modal.querySelector("#delete-account-password");
        const err = modal.querySelector("#delete-account-error");

        if (err) {
            err.style.display = "none";
            err.textContent = "";
            err.className = "";
        }
        if (pass) pass.value = "";

        modal.style.display = "block";
        // фокус чуть позже, чтобы модалка успела отрисоваться
        setTimeout(() => pass?.focus(), 0);
    }

    function hideDeleteModal() {
        const modal = document.getElementById("delete-account-modal");
        if (!modal) return;
        modal.style.display = "none";
    }

    function setError(message) {
        const modal = document.getElementById("delete-account-modal");
        const err = modal?.querySelector("#delete-account-error");
        if (!err) return;

        err.className = "upload-busy-message"; // если у тебя этот класс красивый — переиспользуем
        err.textContent = message;
        err.style.display = "block";
    }

    function lockModal(lock) {
        const modal = document.getElementById("delete-account-modal");
        if (!modal) return;

        const btnOk = modal.querySelector("#delete-account-confirm");
        const btnCancel = modal.querySelector("#delete-account-cancel");
        const pass = modal.querySelector("#delete-account-password");

        if (btnOk) btnOk.disabled = !!lock;
        if (btnCancel) btnCancel.disabled = !!lock;
        if (pass) pass.disabled = !!lock;
    }

    // === api ===
    async function deleteAccount(password) {
        const token = getCsrfToken();
        if (!token) throw new Error("Ошибка безопасности: нет CSRF токена. Перезагрузите страницу.");

        const body = new URLSearchParams();
        body.set("Password", password || "");   // ВАЖНО: имя поля = Password (как в DeleteAccountRequest)

        const res = await fetch(ENDPOINT, {
            method: "POST",
            credentials: "same-origin",
            headers: {
                "Content-Type": "application/x-www-form-urlencoded; charset=UTF-8",
                "RequestVerificationToken": token,
                "X-CSRF-TOKEN": token,             // на всякий случай, у тебя местами используется
            },
            body: body.toString(),
        });

        const data = await res.json().catch(() => null);

        if (!res.ok) {
            const msg = data?.error || `HTTP ${res.status}`;
            throw new Error(msg);
        }
        if (!data?.success) throw new Error(data?.error || "Ошибка при удалении аккаунта");
        return data;
    }


    // === wiring ===
    function isDeleteButton(el) {
        // Рекомендуется в верстке: <button id="deleteAccountButton" ...>
        return !!el?.closest?.("#deleteAccountButton");
    }

    document.addEventListener("click", (e) => {
        const target = e.target;

        if (!isDeleteButton(target)) return;

        // показываем модалку подтверждения
        showDeleteModal();
    });

    // обработчики кнопок модалки (делаем через делегирование, чтобы не зависеть от момента создания)
    document.addEventListener("click", async (e) => {
        const modal = document.getElementById("delete-account-modal");
        if (!modal || modal.style.display === "none") return;

        const ok = e.target.closest("#delete-account-confirm");
        const cancel = e.target.closest("#delete-account-cancel");

        if (cancel) {
            hideDeleteModal();
            return;
        }

        if (!ok) return;

        const pass = modal.querySelector("#delete-account-password");
        const password = pass?.value || "";

        // базовая проверка, чтобы не жать "удалить" пустым
        if (!password.trim()) {
            setError("Введите пароль для подтверждения.");
            pass?.focus();
            return;
        }

        try {
            lockModal(true);
            await deleteAccount(password);
            hideDeleteModal();

            alert("Аккаунт удалён.");
            // после SignOut хорошо бы уйти на логин
            location.href = "/Login";
        } catch (err) {
            setError(err?.message || "Ошибка");
        } finally {
            lockModal(false);
        }
    });
})();