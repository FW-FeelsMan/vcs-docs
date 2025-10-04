(function () {
    // ---- utils ----
    const fmtPct = v => `${Math.round(v)}%`;
    const fmtMb = v => `${Math.round(v)} МБ`;
    const fmtRps = v => v.toFixed(1);

    function css(name, root) { return getComputedStyle(root).getPropertyValue(name).trim(); }
    function palette(root = document.getElementById("op-workload")) {
        return {
            grid: css('--wl-line', root) || '#e6e9f2',
            tick: css('--wl-muted', root) || '#6d7588',
            cpu: css('--chart-cpu', root) || '#5741ff',
            ram: css('--chart-ram', root) || '#00c2a8',
            rps: css('--chart-rps', root) || '#2f9e44',
            err: css('--chart-err', root) || '#d14141',
            netIn: css('--chart-net-in', root) || '#2080ff',
            netOut: css('--chart-net-out', root) || '#ff7e47',
        };
    }

    // ---- chart factory ----
    function mkChart(canvas, labels, datasets, pal, extra) {
        const ctx = canvas.getContext('2d');
        const ds = datasets.map(d => ({
            ...d,
            borderColor: d.color,
            backgroundColor: d.color,
            borderWidth: 2,
            pointRadius: 0,
            tension: 0.25
        }));

        return new Chart(ctx, {
            type: 'line',
            data: { labels, datasets: ds },
            options: {
                responsive: true, maintainAspectRatio: false, animation: false,
                interaction: { intersect: false, mode: 'nearest' },
                plugins: { legend: { display: true, position: 'bottom', labels: { color: pal.tick } } },
                scales: {
                    x: { grid: { display: false }, ticks: { color: pal.tick } },
                    y: { beginAtZero: true, grid: { color: pal.grid, drawBorder: false }, ticks: { color: pal.tick } },
                    ...(extra?.scales || {})
                }
            }
        });
    }

    function setSeries(chart, labels, arrays) {
        chart.data.labels = labels;
        chart.data.datasets.forEach((ds, i) => ds.data = arrays[i] || []);
        chart.update();
    }

    // ---- UI fill ----
    function fillKpi(root, k) {
        root.querySelector("#kpiCpuVal").textContent = fmtPct(k.cpuAvg);
        root.querySelector("#kpiCpuInfo").textContent = `P95 ${fmtPct(k.cpuP95)}`;

        root.querySelector("#kpiRamVal").textContent = fmtPct(k.ramUsedPct);
        root.querySelector("#kpiRamInfo").textContent = `${fmtMb(k.ramUsedMb)} из ${fmtMb(k.ramTotalMb)}`;

        // диском не занимаемся в этой версии — показываем «—»
        root.querySelector("#kpiDiskVal").textContent = `—`;
        root.querySelector("#kpiDiskInfo").textContent = `нет данных`;

        // сеть
        root.querySelector("#kpiNetVal").textContent = `см. график`;
        root.querySelector("#kpiNetInfo").textContent = `вх/исх`;

        root.querySelector("#kpiRpsVal").textContent = fmtRps(k.rpsAvg);
        root.querySelector("#kpiRpsInfo").textContent = `пик ${fmtRps(k.rpsPeak)}`;

        root.querySelector("#kpiErrVal").textContent = `${k.errRate.toFixed(1)}%`;
        root.querySelector("#kpiErrInfo").textContent = `за 5 мин`;
    }

    function fillLists(root, services, queues) {
        const svc = root.querySelector("#svcList");
        svc.innerHTML = (services || []).map(s =>
            `<li class="svc-row ${s.state.toLowerCase()}">
              <span class="dot"></span><span>${s.name}</span><i>${s.state}</i><em>${s.note}</em>
            </li>`).join("");

        const q = root.querySelector("#queueList");
        q.innerHTML = (queues || []).map(x =>
            `<li class="svc-row">
              <span class="dot"></span><span>${x.name}</span><i>${x.depth}</i><em>${x.rate}</em>
            </li>`).join("");
        if (!queues || queues.length === 0) {
            q.innerHTML = `<li class="svc-row"><span class="dot"></span><span>Очередей нет</span><i>—</i><em>—</em></li>`;
        }
    }

    function fillEndpoints(root, rows) {
        const tbody = root.querySelector("#tblEndpoints tbody");
        tbody.innerHTML = (rows || []).map(r =>
            `<tr>
                <td>${r.route}</td>
                <td>${r.avgMs}</td>
                <td>${r.p95Ms}</td>
                <td>${r.rps}</td>
                <td>${r.errPct}</td>
            </tr>`).join("");
    }

    // ---- fetch snapshot ----
    async function querySnapshot(range) {
        const r = await fetch(`/api/ops/workload?range=${encodeURIComponent(range)}`, { cache: 'no-store', credentials: 'same-origin' });
        if (!r.ok) throw new Error('HTTP ' + r.status);
        return r.json();
    }

    // ---- main ----
    window.initWorkload = function (panel) {
        if (panel.__wl_live_inited) return;
        panel.__wl_live_inited = true;

        const root = document.getElementById("op-workload") || panel;
        const pal = palette(root);

        const rangeSel = panel.querySelector("#wl-range");
        const autoBtn = panel.querySelector("#wl-refresh");

        let range = rangeSel?.value || "1h";

        // init empty charts
        const cpuRam = mkChart(panel.querySelector("#chartCpuRam"), [],
            [{ label: "CPU %", color: pal.cpu }, { label: "RAM %", color: pal.ram }],
            pal);

        const rpsErr = mkChart(panel.querySelector("#chartRpsErr"), [],
            [{ label: "RPS", color: pal.rps, yAxisID: 'y' }, { label: "Ошибки %", color: pal.err, yAxisID: 'y1' }],
            pal, { scales: { y1: { position: 'right', grid: { drawOnChartArea: false }, ticks: { color: pal.tick }, beginAtZero: true } } });

        const net = mkChart(panel.querySelector("#chartNet"), [],
            [{ label: "Входящий МБ/с", color: pal.netIn }, { label: "Исходящий МБ/с", color: pal.netOut }],
            pal);

        async function step() {
            try {
                const s = await querySnapshot(range);

                // KPI
                fillKpi(panel, s.kpi);

                // charts
                setSeries(cpuRam, s.labels, [s.series.cpu, s.series.ram]);
                setSeries(rpsErr, s.labels, [s.series.rps, s.series.err]);
                setSeries(net, s.labels, [s.series.netIn, s.series.netOut]);

                // lists/table
                fillLists(panel, s.services, s.queues);
                fillEndpoints(panel, s.endpoints);
            } catch (e) {
                console.warn('[workload] fetch failed', e);
            }
        }

        let timer = null;
        function setAuto(on) {
            if (on) {
                if (!timer) timer = setInterval(step, 5000);
                autoBtn.dataset.state = "auto";
                autoBtn.textContent = "Авто-обновление";
            } else {
                if (timer) { clearInterval(timer); timer = null; }
                autoBtn.dataset.state = "manual";
                autoBtn.textContent = "Обновить";
            }
        }

        autoBtn?.addEventListener("click", () => {
            setAuto(autoBtn.dataset.state !== "auto");
        });
        rangeSel?.addEventListener("change", () => { range = rangeSel.value; step(); });

        setAuto(true);
        step();

        panel.__dispose = function () {
            try { if (timer) clearInterval(timer); } catch { }
            try { cpuRam?.destroy(); } catch { }
            try { rpsErr?.destroy(); } catch { }
            try { net?.destroy(); } catch { }
        };
    };
})();