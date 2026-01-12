(() => {
    // защита от повторной инициализации (важно при AJAX/iframe/SPA-подгрузке)
    if (window.__profileEditInfoInit) return;
    window.__profileEditInfoInit = true;

    const PLACEHOLDER = "Не установлено";

    const getCsrfToken = () =>
        document.querySelector('input[name="__RequestVerificationToken"]')?.value || "";

    function applyDateMask(el) {
        el.removeAttribute("disabled");
        el.setAttribute("maxlength", "10");
        el.setAttribute("inputmode", "numeric");

        el.addEventListener("input", function (e) {
            let v = (e.target.value || "").replace(/[^\d]/g, "");
            if (v.length > 2) v = v.slice(0, 2) + "." + v.slice(2);
            if (v.length > 5) v = v.slice(0, 5) + "." + v.slice(5, 9);
            e.target.value = v;
        });
    }

    function isDateField(fieldName, inputEl) {
        if (inputEl?.classList?.contains("date-input")) return true;
        return (fieldName || "").toLowerCase().includes("birth");
    }

    function isEmailField(fieldName) {
        const f = (fieldName || "").toLowerCase();
        return f === "email" || f.includes("email");
    }

    function createEditButton() {
        const btn = document.createElement("button");
        btn.className = "edit-button";
        btn.setAttribute("data-action", "edit");
        btn.innerHTML = '<img src="/images/edit_icon.png" alt="Edit">';
        btn.title = "Редактировать";
        return btn;
    }

    function createSaveButton() {
        const btn = document.createElement("button");
        btn.className = "edit-button";
        btn.setAttribute("data-action", "save");
        btn.innerHTML = '<img src="/images/save_icon.png" alt="Save">';
        btn.title = "Сохранить";
        return btn;
    }

    function revertCard(card) {
        const input = card.querySelector("input[data-field]");
        const saveIcon = card.querySelector('.edit-button img[src*="save_icon"]');
        if (!input || !saveIcon) return;

        const field = (input.dataset.field || "").trim();
        const value = (input.dataset.originalValue ?? input.value ?? "").trim();

        const reverted = document.createElement("p");
        reverted.dataset.field = field;
        reverted.textContent = value || PLACEHOLDER;

        input.replaceWith(reverted);
        saveIcon.closest("button")?.replaceWith(createEditButton());
    }

    function revertOtherCards(currentCard) {
        document.querySelectorAll(".info-card").forEach((card) => {
            if (card !== currentCard) revertCard(card);
        });
    }

    async function updateField(fieldName, value) {
        const token = getCsrfToken();
        if (!token) throw new Error("Ошибка безопасности. Перезагрузите страницу.");

        const res = await fetch("/Content/profile_page?handler=UpdateUserData", {
            method: "POST",
            headers: {
                "Content-Type": "application/json",
                "RequestVerificationToken": token,
                "X-CSRF-TOKEN": token
            },
            body: JSON.stringify({ Field: fieldName, Value: value }),
        });

        if (!res.ok) throw new Error(`HTTP ${res.status}`);
        const data = await res.json().catch(() => null);
        if (!data?.success) throw new Error(data?.error || "Ошибка обновления");
        return data;
    }

    function startEdit(card) {
        const textEl = card.querySelector("p[data-field]");
        const oldInputEl = card.querySelector("input[data-field]");

        const fieldName = (textEl?.dataset.field || oldInputEl?.dataset.field || "").trim();
        if (!fieldName) return;

        revertOtherCards(card);

        let currentValue = (textEl?.textContent || oldInputEl?.value || "").trim();
        if (currentValue === PLACEHOLDER) currentValue = "";

        const dateMode = isDateField(fieldName, oldInputEl);
        const emailMode = isEmailField(fieldName);

        const input = document.createElement("input");
        input.type = emailMode ? "email" : "text";
        input.className = dateMode ? "date-input" : "edit-input";
        input.value = currentValue;
        input.dataset.field = fieldName;
        input.dataset.originalValue = currentValue;

        if (emailMode) {
            input.setAttribute("maxlength", "254");
            input.setAttribute("autocomplete", "email");
            input.setAttribute("placeholder", "name@example.com");
        }

        if (dateMode) applyDateMask(input);

        if (textEl) textEl.replaceWith(input);
        else oldInputEl?.replaceWith(input);

        input.focus();

        const editBtn = card.querySelector(".edit-button");
        const saveBtn = createSaveButton();
        editBtn?.replaceWith(saveBtn);

        saveBtn.addEventListener(
            "click",
            async () => {
                const newValue = (input.value || "").trim();

                try {
                    await updateField(fieldName, newValue);

                    const newText = document.createElement("p");
                    newText.dataset.field = fieldName;
                    newText.textContent = newValue || PLACEHOLDER;

                    input.replaceWith(newText);
                    saveBtn.replaceWith(createEditButton());
                } catch (err) {
                    alert(err?.message || "Ошибка");

                    const fallback = document.createElement("p");
                    fallback.dataset.field = fieldName;
                    fallback.textContent = (input.dataset.originalValue || "").trim() || PLACEHOLDER;

                    input.replaceWith(fallback);
                    saveBtn.replaceWith(createEditButton());
                }
            },
            { once: true }
        );
    }    

    document.addEventListener("click", (e) => {
        if (e.target.closest("#deleteAccountButton")) return;

        const btn = e.target.closest(".info-card .edit-button");
        if (btn) {
            const action = (btn.getAttribute("data-action") || "").toLowerCase();
            const imgSrc = btn.querySelector("img")?.getAttribute("src") || "";
            if (action === "save" || imgSrc.includes("save_icon")) return;

            const card = btn.closest(".info-card");
            if (card) startEdit(card);
            return;
        }

        //const delBtn = e.target.closest(".info-card .button-sliding");
        //if (delBtn && (delBtn.textContent || "").includes("Удалить")) {
        //    tryDeleteAccount().catch((err) => alert(err?.message || "Ошибка"));
        //}
    });
})();