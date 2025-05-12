//profile.js скрипт для сайдбара в личном кабинете
const observer = new MutationObserver(function (mutationsList) {
    handleDomChanges();
});

const resizeObserver = new ResizeObserver(entries => {
    updateTooltips();
});

function handleDomChanges() {
    const menuItems = document.querySelectorAll('.sidebar-menu li');
    const contentSections = document.querySelectorAll('.content-section');

    menuItems.forEach(item => {
        item.addEventListener('click', function () {
            menuItems.forEach(i => i.classList.remove('active'));
            this.classList.add('active');
            contentSections.forEach(section => section.classList.remove('active'));
            const targetSection = document.getElementById(this.dataset.target);
            if (targetSection) {
                targetSection.classList.add('active');

                requestAnimationFrame(() => {
                    updateTooltips(true);
                });
            }
        });
    });

    requestAnimationFrame(() => {
        updateTooltips(true);
    });
}

function updateTooltips(force = false) {
    document.querySelectorAll('.cell-content').forEach(cell => {
        const isOverflow = cell.scrollWidth > cell.clientWidth;
        const currentText = cell.textContent.trim();

        if (force || (isOverflow && cell.title !== currentText)) {
            cell.title = isOverflow ? currentText : '';
        }
    });
}

observer.observe(document.body, {
    childList: true,
    subtree: true
});

document.querySelectorAll('table').forEach(table => {
    resizeObserver.observe(table);
});

document.addEventListener('DOMContentLoaded', () => {
    setTimeout(() => {
        updateTooltips(true);
    }, 300); 
});

window.addEventListener('resize', () => {
    updateTooltips(true);
});
document.getElementById('avatarFileInput').addEventListener('change', function (event) {
    const file = event.target.files[0];
    const formData = new FormData();
    formData.append('avatar', file);

    fetch('/Content/profile_page?handler=UploadAvatar', {
        method: 'POST',
        body: formData,
        headers: {
            'X-CSRF-TOKEN': document.querySelector('input[name="__RequestVerificationToken"]').value
        }
    })
        .then(response => response.json())
        .then(result => {
            if (result.success) {
                const avatarImage = document.getElementById('avatarImage');
                avatarImage.src = `/userdata/userData_${result.userId}/Avatars/avatar.jpg?v=${result.timestamp}`;
                console.log('Аватарка успешно загружена:', avatarImage.src);
            } else {
                alert('Ошибка: ' + result.error);
            }
        })
        .catch(err => {
            console.error('Ошибка при отправке файла:', err);
        });
});
function initAvatarUpload() {
    const avatarInput = document.getElementById('avatarFileInput');
    const avatarImage = document.getElementById('avatarImage');

    if (!avatarInput || !avatarImage) {
        console.warn('[Avatar Upload] Элементы не найдены!');
        return;
    }

    avatarInput.onchange = function (event) {
        const file = event.target.files[0];
        const formData = new FormData();
        formData.append('avatar', file);

        fetch('/Content/profile_page?handler=UploadAvatar', {
            method: 'POST',
            body: formData,
            headers: {
                'X-CSRF-TOKEN': document.querySelector('input[name="__RequestVerificationToken"]').value
            }
        })
            .then(response => response.json())
            .then(result => {
                if (result.success) {
                    avatarImage.src = `/userdata/userData_${result.userId}/Avatars/avatar.jpg?v=${result.timestamp}`;
                    console.log('Аватарка успешно загружена:', avatarImage.src);
                } else {
                    alert('Ошибка: ' + result.error);
                }
            })
            .catch(err => {
                console.error('Ошибка при отправке файла:', err);
            });
    };
}
