// userStatus.js (Client-side)
(function () {
    const initializeSignalR = () => {
        if (window.location.pathname.toLowerCase() === '/login') return;

        if (typeof signalR === 'undefined') {
            console.error('SignalR library not loaded');
            return;
        }

        const connection = new signalR.HubConnectionBuilder()
            .withUrl("/Data/userStatusHub", {
                skipNegotiation: true,
                transport: signalR.HttpTransportType.WebSockets
            })
            .configureLogging(signalR.LogLevel.Warning)
            .withAutomaticReconnect()
            .build();

        connection.on("UserStatusUpdated", (userId, isOnline) => {
            console.log(`User ${userId} status: ${isOnline ? 'online' : 'offline'}`);
        });

        connection.onclose(error => {
            if (error?.statusCode === 401) {
                window.location.href = '/login';
            }
            console.error('Connection closed:', error);
        });

        connection.start()
            .then(() => console.log('SignalR connection established'))
            .catch(error => console.error('Connection error:', error));
    };

    initializeSignalR();
})();