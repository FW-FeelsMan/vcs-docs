document.addEventListener("click", function (e) {
    const editBtn = e.target.closest(".edit-button");
    if (editBtn) handleEditClick.call(editBtn);
});

function handleEditClick() {
    const card = this.closest(".info-card");
    if (!card) return;

    const textElement = card.querySelector("p[data-field]");
    if (!textElement) return;

    const currentText = textElement.innerText.trim();
    const fieldName = textElement.dataset.field;

    const input = document.createElement("input");
    input.type = "text";
    input.value = currentText;
    input.classList.add("edit-input");

    textElement.replaceWith(input);
    input.focus();

    const editButton = card.querySelector(".edit-button");
    const saveButton = document.createElement("button");
    saveButton.className = "save-button";
    saveButton.innerHTML = '<img src="/images/save_icon.png" alt="Save">';
    saveButton.title = "Применить";

    editButton.replaceWith(saveButton);

    saveButton.addEventListener("click", () => {
        const newValue = input.value.trim();
        const tokenElement = document.querySelector('meta[name="csrf-token"]');
        if (!tokenElement) {
            alert("Ошибка безопасности. Перезагрузите страницу.");
            return;
        }
        const token = tokenElement.getAttribute('content');

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
                if (!data.success) throw new Error(data.error);
                textElement.textContent = newValue;
            })
            .catch(error => {
                alert(error.message);
            })
            .finally(() => {
                input.replaceWith(textElement);
                saveButton.replaceWith(editButton);
            });
    });
}
