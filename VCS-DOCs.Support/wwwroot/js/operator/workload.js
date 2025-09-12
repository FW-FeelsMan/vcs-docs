// wwwroot/js/operator/workload.js
(function () {
    // ---- utils / formatting ----
    function rand(n, base = 0) { return Math.round(base + Math.random() * n); }
    function fmtPct(v) { return (v).toFixed(0) + "%"; }
    function fmtMb(v) { return (v).toFixed(0) + " МБ"; }
    function fmtRps(v) { return (v).toFixed(1); }

    // css variables -> palette
    function cssVar(name, root) {
        return getComputedStyle(root).getPropertyValue(name).trim();
    }
    function chartPalette(root = document.getElementById("op-workload")) {
        return {
            grid: cssVar('--wl-line', root) || '#e6e9f2',
            tick: cssVar('--wl-muted', root) || '#6d7588',
            cpu: cssVar('--chart-cpu', root) || '#5741ff',
            ram: cssVar('--chart-ram', root) || '#00c2a8',
            rps: cssVar('--chart-rps', root) || '#2f9e44',
            err: cssVar('--chart-err', root) || '#d14141',
            netIn: cssVar('--chart-net-in', root) || '#2080ff',
            netOut: cssVar('--chart-net-out', root) || '#ff7e47',
        };
    }

    // labels & dummy series
    function makeLabels(range) {
        const now = new Date();
        const pts = range === "24h" ? 24 : range === "1h" ? 12 : 15; // 60m / 5m / 1m
        const stepMin = range === "24h" ? 60 : range === "1h" ? 5 : 1;
        const res = [];
        for (let i = pts - 1; i >= 0; i--) {
            const d = new Date(now.getTime() - i * stepMin * 60000);
            res.push(d.toTimeString().slice(0, 5));
        }
        return res;
    }

    function makeSeries(len, { base = 50, spread = 30, floor = 0, ceil = 100 } = {}) {
        return new Array(len).fill(0).map(() => {
            const v = base + (Math.random() - 0.5) * spread * 2;
            return Math.max(floor, Math.min(ceil, v));
        });
    }

    // kpis / lists / table
    function fillKpis(panel, model) {
        panel.querySelector("#kpiCpuVal").textContent = fmtPct(model.cpu.avg);
        panel.querySelector("#kpiCpuInfo").textContent = `P95 ${fmtPct(model.cpu.p95)}`;

        panel.querySelector("#kpiRamVal").textContent = fmtPct(model.ram.usedPct);
        panel.querySelector("#kpiRamInfo").textContent = `${fmtMb(model.ram.usedMb)} из ${fmtMb(model.ram.totalMb)}`;

        panel.querySelector("#kpiDiskVal").textContent = `${model.disk.iops} IOPS`;
        panel.querySelector("#kpiDiskInfo").textContent = `${model.disk.readMb}/с чтение · ${model.disk.writeMb}/с запись`;

        panel.querySelector("#kpiNetVal").textContent = `${model.net.inMb}/${model.net.outMb} МБ/с`;
        panel.querySelector("#kpiNetInfo").textContent = `вх/исх`;

        panel.querySelector("#kpiRpsVal").textContent = fmtRps(model.rps.avg);
        panel.querySelector("#kpiRpsInfo").textContent = `пик ${fmtRps(model.rps.peak)}`;

        panel.querySelector("#kpiErrVal").textContent = fmtPct(model.err.rate);
        panel.querySelector("#kpiErrInfo").textContent = `${model.err.count4xx + model.err.count5xx} ошибок`;
    }

    function fakeModel() {
        return {
            cpu: { avg: rand(70, 20), p95: rand(90, 10) },
            ram: { totalMb: 32768, usedMb: rand(22000, 6000), get usedPct() { return (this.usedMb / this.totalMb) * 100; } },
            disk: { iops: rand(400, 50), readMb: rand(100, 10), writeMb: rand(80, 10) },
            net: { inMb: rand(60, 5), outMb: rand(40, 5) },
            rps: { avg: Math.random() * 30 + 5, peak: Math.random() * 60 + 30 },
            err: { rate: Math.random() * 6, count4xx: rand(60), count5xx: rand(20) }
        };
    }

    // chart helpers
    function makeChart(canvas, cfg, pal) {
        const ctx = canvas.getContext("2d");
        // normalize datasets: add colors & line style
        const datasets = cfg.data.datasets.map(ds => ({
            ...ds,
            borderColor: ds.borderColor || ds.color,
            backgroundColor: ds.backgroundColor || ds.color,
            pointRadius: 0,
            borderWidth: 2,
            tension: ds.tension ?? 0.25
        }));

        const baseOpts = {
            responsive: true,
            maintainAspectRatio: false,
            animation: false,
            interaction: { intersect: false, mode: "nearest" },
            plugins: {
                legend: { display: true, position: "bottom", labels: { color: pal.tick } }
            },
            scales: {
                x: { grid: { display: false }, ticks: { color: pal.tick } },
                y: { beginAtZero: true, grid: { color: pal.grid, drawBorder: false }, ticks: { color: pal.tick } }
            }
        };

        // deep merge minimal (only for scales/plugins we touch)
        const options = cfg.options ? mergeOptions(baseOpts, cfg.options) : baseOpts;

        return new Chart(ctx, {
            type: "line",
            data: { labels: cfg.data.labels, datasets },
            options
        });
    }

    function mergeOptions(base, extra) {
        const out = structuredClone ? structuredClone(base) : JSON.parse(JSON.stringify(base));
        function rec(dst, src) {
            for (const k in src) {
                const sv = src[k], dv = dst[k];
                if (sv && typeof sv === 'object' && !Array.isArray(sv)) {
                    dst[k] = rec(dv ? { ...dv } : {}, sv);
                } else {
                    dst[k] = sv;
                }
            }
            return dst;
        }
        return rec(out, extra);
    }

    function recolorChart(chart, pal, mapping) {
        // mapping = ['cpu','ram'] etc to pick colors from palette in order
        chart.options.plugins.legend.labels.color = pal.tick;
        if (chart.options.scales?.x?.ticks) chart.options.scales.x.ticks.color = pal.tick;
        if (chart.options.scales?.y?.ticks) chart.options.scales.y.ticks.color = pal.tick;
        if (chart.options.scales?.y1?.ticks) chart.options.scales.y1.ticks.color = pal.tick;
        if (chart.options.scales?.y?.grid) chart.options.scales.y.grid.color = pal.grid;

        chart.data.datasets.forEach((ds, i) => {
            const key = mapping[i];
            const color = pal[key] || ds.borderColor;
            ds.borderColor = color;
            ds.backgroundColor = color;
        });
        chart.update('none');
    }

    // ---- public init ----
    window.initWorkload = async function (panel) {
        if (panel.__wl_inited) return;
        panel.__wl_inited = true;

        const root = document.getElementById("op-workload") || panel;
        let pal = chartPalette(root);

        const rangeSel = panel.querySelector("#wl-range");
        const autoBtn = panel.querySelector("#wl-refresh");

        let range = rangeSel?.value || "1h";
        let labels = makeLabels(range);

        const cpuRam = makeChart(panel.querySelector("#chartCpuRam"), {
            data: {
                labels,
                datasets: [
                    { label: "CPU %", data: makeSeries(labels.length, { base: 55, spread: 20 }), color: pal.cpu },
                    { label: "RAM %", data: makeSeries(labels.length, { base: 60, spread: 10 }), color: pal.ram }
                ]
            }
        }, pal);

        const rpsErr = makeChart(panel.querySelector("#chartRpsErr"), {
            data: {
                labels,
                datasets: [
                    { label: "RPS", data: makeSeries(labels.length, { base: 12, spread: 8, floor: 0, ceil: 80 }), color: pal.rps, yAxisID: 'y' },
                    { label: "Ошибки %", data: makeSeries(labels.length, { base: 2, spread: 2, floor: 0, ceil: 10 }), color: pal.err, yAxisID: 'y1' }
                ]
            },
            options: {
                scales: {
                    y: { beginAtZero: true },
                    y1: { beginAtZero: true, position: 'right', grid: { drawOnChartArea: false } }
                }
            }
        }, pal);

        const net = makeChart(panel.querySelector("#chartNet"), {
            data: {
                labels,
                datasets: [
                    { label: "Входящий МБ/с", data: makeSeries(labels.length, { base: 20, spread: 10, floor: 0, ceil: 100 }), color: pal.netIn },
                    { label: "Исходящий МБ/с", data: makeSeries(labels.length, { base: 12, spread: 8, floor: 0, ceil: 80 }), color: pal.netOut }
                ]
            }
        }, pal);

        // lists & table & kpis
        function fillLists(panel) {
            const svc = panel.querySelector("#svcList");
            const q = panel.querySelector("#queueList");
            if (svc) {
                svc.innerHTML = [
                    { name: "Auth", state: "OK", note: "22 ms" },
                    { name: "Mail", state: "OK", note: "SMTP" },
                    { name: "VDocsBridge", state: "OK", note: "120 ms" },
                    { name: "Db", state: "WARN", note: "P95 480 ms" }
                ].map(s =>
                    `<li class="svc-row ${s.state.toLowerCase()}">
            <span class="dot"></span><span>${s.name}</span><i>${s.state}</i><em>${s.note}</em>
          </li>`
                ).join("");
            }
            if (q) {
                q.innerHTML = [
                    { name: "EmailQueue", depth: 3, rate: "2/s" },
                    { name: "Jobs", depth: 0, rate: "—" },
                    { name: "Export3D", depth: 1, rate: "0.2/s" }
                ].map(x =>
                    `<li class="svc-row">
            <span class="dot"></span><span>${x.name}</span><i>${x.depth}</i><em>${x.rate}</em>
          </li>`
                ).join("");
            }
        }
        function fillEndpoints(panel) {
            const tbody = panel.querySelector("#tblEndpoints tbody");
            if (!tbody) return;
            const rows = [
                ["/api/Support/ticket", 42, 120, 3.2, 0.8],
                ["/Content/Operators/all_open_usertickets", 55, 180, 2.1, 0.2],
                ["/api/Users/search", 28, 90, 4.5, 0.0],
                ["/hubs/userStatus", 12, 35, 6.0, 0.0],
                ["/api/VDocs/files", 88, 310, 1.4, 1.2]
            ];
            tbody.innerHTML = rows.map(r => `<tr><td>${r[0]}</td><td>${r[1]}</td><td>${r[2]}</td><td>${r[3]}</td><td>${r[4]}</td></tr>`).join("");
        }

        fillLists(panel);
        fillEndpoints(panel);
        fillKpis(panel, fakeModel());

        // data refresh
        let timer = null;

        function step() {
            const m = fakeModel();
            fillKpis(panel, m);

            function regen(chart, gen) {
                chart.data.labels = labels;
                chart.data.datasets.forEach((ds, i) => ds.data = gen(i));
                chart.update();
            }

            regen(cpuRam, (i) => i === 0
                ? makeSeries(labels.length, { base: 55, spread: 20 })
                : makeSeries(labels.length, { base: 60, spread: 10 }));

            regen(rpsErr, (i) => i === 0
                ? makeSeries(labels.length, { base: 12, spread: 8, floor: 0, ceil: 80 })
                : makeSeries(labels.length, { base: 2, spread: 2, floor: 0, ceil: 10 }));

            regen(net, (i) => i === 0
                ? makeSeries(labels.length, { base: 20, spread: 10, floor: 0, ceil: 100 })
                : makeSeries(labels.length, { base: 12, spread: 8, floor: 0, ceil: 80 }));
        }

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

        rangeSel?.addEventListener("change", () => {
            range = rangeSel.value;
            labels = makeLabels(range);
            step();
        });

        // optional theme toggle (button id="wl-theme", if present)
        const themeBtn = panel.querySelector("#wl-theme");
        if (themeBtn) {
            themeBtn.addEventListener("click", () => {
                const root = document.getElementById("op-workload");
                root.classList.toggle("is-dark");
                pal = chartPalette(root);
                recolorChart(cpuRam, pal, ['cpu', 'ram']);
                recolorChart(rpsErr, pal, ['rps', 'err']);
                recolorChart(net, pal, ['netIn', 'netOut']);
                themeBtn.textContent = root.classList.contains("is-dark") ? "Тёмная" : "Светлая";
            });
        }

        // start
        setAuto(true);
        step();

        // dispose
        panel.__dispose = function () {
            try { if (timer) clearInterval(timer); } catch { }
            try { cpuRam?.destroy(); } catch { }
            try { rpsErr?.destroy(); } catch { }
            try { net?.destroy(); } catch { }
        };
    };
})();
