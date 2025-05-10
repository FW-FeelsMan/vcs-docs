//editInfoCard.js скрипт редактирования личных данных пользователя
function applyDateMask(el) {
    el.removeAttribute('disabled');
    el.setAttribute('maxlength', '10');
    el.setAttribute('inputmode', 'numeric');

    el.addEventListener('input', function (e) {
        let v = e.target.value.replace(/[^\d]/g, '');
        if (v.length > 2) v = v.slice(0, 2) + '.' + v.slice(2);
        if (v.length > 5) v = v.slice(0, 5) + '.' + v.slice(5, 9);
        e.target.value = v;
    });
}

function createEditButton() {
    const button = document.createElement("button");
    button.className = "edit-button";
    button.innerHTML = '<img src="/images/edit_icon.png" alt="Edit">';
    button.title = "Редактировать";
    button.addEventListener("click", handleEditClick);
    return button;
}

function handleEditClick(event) {
    const button = event.target.closest(".edit-button");
    if (!button) return;

    const card = button.closest(".info-card");
    if (!card) return;

    // Сброс других редактируемых карточек
    document.querySelectorAll(".info-card").forEach(otherCard => {
        if (otherCard === card) return;

        const input = otherCard.querySelector("input[data-field]");
        const saveBtn = otherCard.querySelector(".edit-button img[src*='save_icon']");

        if (input && saveBtn) {
            const value = input.dataset.originalValue || input.value;
            const field = input.dataset.field;

            const revertedText = document.createElement("p");
            revertedText.dataset.field = field;
            revertedText.textContent = value;

            input.replaceWith(revertedText);

            const revertButton = createEditButton();
            saveBtn.closest("button").replaceWith(revertButton);
        }
    });

    const textElement = card.querySelector("p[data-field]");
    const inputElement = card.querySelector("input[data-field]");
    let fieldName, currentValue;

    if (textElement) {
        fieldName = textElement.dataset.field;
        currentValue = textElement.textContent.trim();
    } else if (inputElement) {
        fieldName = inputElement.dataset.field;
        currentValue = inputElement.value.trim();
    } else {
        return;
    }

    const isDate = inputElement?.classList.contains("date-input")
        || fieldName.toLowerCase().includes("birth");

    const input = document.createElement("input");
    input.type = "text";
    input.className = isDate ? "date-input" : "edit-input";
    input.value = currentValue;
    input.dataset.field = fieldName;
    input.dataset.originalValue = currentValue;

    if (isDate) applyDateMask(input);

    if (textElement) textElement.replaceWith(input);
    else inputElement.replaceWith(input);

    input.focus();

    const editButton = card.querySelector(".edit-button");
    const saveButton = document.createElement("button");
    saveButton.className = "edit-button";
    saveButton.innerHTML = '<img src="/images/save_icon.png" alt="Save">';
    saveButton.title = "Сохранить";

    editButton.replaceWith(saveButton);

    saveButton.addEventListener("click", () => {
        const newValue = input.value.trim();
        const tokenElement = document.querySelector('meta[name="csrf-token"]');
        if (!tokenElement) {
            alert("Ошибка безопасности. Перезагрузите страницу.");
            return;
        }
        const token = tokenElement.getAttribute("content");

        fetch("/Content/profile_page?handler=UpdateUserData", {
            method: "POST",
            headers: {
                "Content-Type": "application/json",
                "X-CSRF-TOKEN": token
            },
            body: JSON.stringify({ Field: fieldName, Value: newValue })
        })
            .then(response => {
                if (!response.ok) throw new Error(`HTTP ${response.status}`);
                return response.json();
            })
            .then(data => {
                if (!data.success) throw new Error(data.error || "Ошибка обновления");

                const newText = document.createElement("p");
                newText.dataset.field = fieldName;
                newText.textContent = newValue;

                const currentInput = card.querySelector("input[data-field]");
                if (currentInput) {
                    currentInput.replaceWith(newText);
                }

                const newEditButton = createEditButton();
                const currentButton = card.querySelector(".edit-button");
                if (currentButton) {
                    currentButton.replaceWith(newEditButton);
                } else {
                    const h4 = card.querySelector("h4");
                    if (h4) h4.appendChild(newEditButton);
                }
            })
            .catch(err => {
                alert(err.message);

                const fallbackText = document.createElement("p");
                fallbackText.dataset.field = fieldName;
                fallbackText.textContent = input.dataset.originalValue || input.value;

                input.replaceWith(fallbackText);

                const fallbackButton = createEditButton();
                saveButton.replaceWith(fallbackButton);
            });
    });
}

document.addEventListener("click", function (e) {
    const button = e.target.closest(".edit-button");
    if (!button) return;
    handleEditClick(e);
});
document.addEventListener("click", function (e) {
    const deleteButton = e.target.closest(".info-card .button-sliding.primary");
    if (deleteButton && deleteButton.textContent.includes("Удалить")) {
        if (!confirm("Вы уверены, что хотите удалить аккаунт?")) return;

        fetch("/Content/profile_page?handler=DeleteAccount", {
            method: "POST",
            headers: {
                "X-CSRF-TOKEN": document.querySelector('meta[name="csrf-token"]').content
            }
        })
            .then(res => {
                if (!res.ok) throw new Error("Ошибка при удалении аккаунта.");
                return res.text();
            })
            .then(() => {
                alert("Аккаунт удалён.");
                location.href = "/Login";
            })
            .catch(err => alert(err.message));
    }
});
