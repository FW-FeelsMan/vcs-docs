// wwwroot/js/support-presence.js
(function () {
    if (window.__presence?.initialized) {
        console.debug("[presence] already initialized");
        return;
    }

    const statusEl = () => document.querySelector(".main-user-status");
    const setStatus = (cls, text) => {
        const el = statusEl();
        if (!el) return;
        el.classList.remove("online", "offline", "connecting");
        if (cls) el.classList.add(cls);
        if (text) el.textContent = text;
    };

    const presence = (window.__presence = window.__presence || {});
    presence.initialized = true;

    const hubUrl =
        (window.__presenceHubUrl /* на всякий */) ||
        (location.origin.replace(/\/+$/, "") + "/hubs/userStatus");

    if (!presence.connection) {
        presence.connection = new signalR.HubConnectionBuilder()
            .withUrl(hubUrl, {
                transport:
                    signalR.HttpTransportType.WebSockets |
                    signalR.HttpTransportType.ServerSentEvents |
                    signalR.HttpTransportType.LongPolling,
            })
            .withAutomaticReconnect({
                nextRetryDelayInMilliseconds: (ctx) => {
                    const steps = [0, 2000, 5000, 10000, 15000, 30000];
                    return steps[Math.min(ctx.previousRetryCount + 1, steps.length - 1)];
                },
            })
            .configureLogging(signalR.LogLevel.Information)
            .build();

        const mySid = document.querySelector('meta[name="support-sid"]')?.content || null;

        presence.connection.on("ForceLogout", (sidFromServer) => {
            // если сервер прислал sid новой сессии — эту вкладку не трогаем
            if (sidFromServer && mySid && sidFromServer === mySid) return;

            try { sessionStorage.setItem("support_logout_reason", "forced"); } catch { }
            alert("Сеанс завершён: выполнен вход в другом месте или администратор отключил вас.");
            window.location.replace("/Account/LoginSupport?forced=1");
        });

        presence.connection.onreconnecting((err) => {
            console.warn("[presence] reconnecting:", err?.message || err);
            setStatus("connecting", "Переподключение…");
        });

        presence.connection.onreconnected((id) => {
            console.info("[presence] reconnected:", id);
            setStatus("online", "В сети");
        });

        presence.connection.onclose((err) => {
            console.warn("[presence] closed:", err?.message || err);
            presence._started = false;          
            setStatus("offline", "Оффлайн");
            if (!presence._manualStop) setTimeout(startSafe, 5000);
        });
    }

    // защищённый старт с дедупликацией попыток
    async function startSafe() {
        if (presence._starting || presence._started) return presence._startPromise;
        presence._starting = true;
        setStatus("connecting", "Подключение…");

        presence._startPromise = (async () => {
            let attempt = 0;
            while (!presence._started) {
                try {
                    await presence.connection.start();
                    presence._started = true;
                    console.info("[presence] connected");
                    setStatus("online", "В сети");

                    if (!presence._pulseTimer) {
                        presence._pulseTimer = setInterval(() => {
                            try { presence.connection.invoke("Pulse"); } catch { }
                        }, 15000);
                    }

                    break;
                } catch (e) {
                    attempt++;
                    console.warn("[presence] start failed:", e?.message || e);
                    const delay = Math.min(15000, 800 * Math.pow(2, attempt));
                    setStatus("connecting", "Подключение…");
                    await new Promise((r) => setTimeout(r, delay));
                }
            }
        })();

        return presence._startPromise.finally(() => {
            presence._starting = false;
        });
    }

    // останавливаем только на реальном выгрузе страницы
    window.addEventListener("beforeunload", () => {
        try {
            presence._manualStop = true;
            const HubState = signalR.HubConnectionState || {};
            if (presence.connection && presence.connection.state !== HubState.Disconnected) {
                presence.connection.stop();
            }
        } catch { /* noop */ }
    });

   // console.log("[presence] init on page:", document.title || location.pathname);
    startSafe();
})();
