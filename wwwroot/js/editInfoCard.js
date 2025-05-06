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

document.addEventListener('DOMContentLoaded', function () {
    document.querySelectorAll('.date-input').forEach(applyDateMask);
});

document.addEventListener("click", function (e) {
    const editBtn = e.target.closest(".edit-button");
    if (editBtn) handleEditClick.call(editBtn);
});

function handleEditClick() {
    const card = this.closest(".info-card");
    if (!card) return;

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

                input.replaceWith(newText);
                saveButton.replaceWith(editButton);
            })
            .catch(err => {
                alert(err.message);
                if (textElement) {
                    input.replaceWith(textElement);
                } else {
                    inputElement.value = currentValue;
                    inputElement.disabled = true;
                    card.appendChild(inputElement);
                }
                saveButton.replaceWith(editButton);
            });
    });
}
