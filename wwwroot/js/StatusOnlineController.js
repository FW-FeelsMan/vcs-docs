// Установка онлайн-статуса при загрузке страницы
document.addEventListener('DOMContentLoaded', async () => {
    try {
        const response = await fetch('/Index?handler=SetUserOnline', {
            method: 'POST',
            headers: {
                'RequestVerificationToken': document.querySelector('input[name="__RequestVerificationToken"]').value,
                'Content-Type': 'application/json'
            }
        });

        if (!response.ok) {
            console.error('Ошибка установки онлайн:', await response.text());
        }
    } catch (error) {
        console.error('Ошибка сети:', error);
    }
});

// Установка офлайн-статуса при закрытии вкладки/обновлении
window.addEventListener('beforeunload', async () => {
    try {
        await fetch('/Index?handler=SetUserOffline', {
            method: 'POST',
            headers: {
                'RequestVerificationToken': document.querySelector('input[name="__RequestVerificationToken"]').value,
                'Content-Type': 'application/json'
            },
            keepalive: true 
        });
    } catch (error) {
        console.error('Ошибка установки офлайн:', error);
    }
});
