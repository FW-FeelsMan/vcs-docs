// Minimal desk sidebar controller with smooth, single-run animations.
(function(){
    const contentCache = new Map();
    let currentContentId = null;
    let clickLock = false;

    document.addEventListener('DOMContentLoaded', () => {
        const firstButton = document.querySelector('.sidebar-button');
        document.querySelectorAll('.sidebar-button').forEach(btn => {
            btn.addEventListener('click', () => selectButton(btn));
        });
        if (firstButton) selectButton(firstButton);
    });

    function selectButton(button){
        if (clickLock) return;
        clickLock = true; setTimeout(() => clickLock=false, 250);

        const contentId = button.getAttribute('data-content');
        if (currentContentId === contentId) return;

        showLoader();
        loadContent(contentId);
        currentContentId = contentId;
        updateButtonSelection(button);
    }

    function updateButtonSelection(button){
        document.querySelectorAll('.sidebar-button').forEach(b=>b.classList.remove('selected'));
        button.classList.add('selected');
    }

    async function loadContent(contentId){
        const contentContainer = document.getElementById('content');
        if (!contentContainer) return;

        try{
            contentContainer.innerHTML = '';
            const resp = await fetch(`/Content/${contentId}`, {cache:'no-store', credentials:'same-origin'});
            if(!resp.ok) throw new Error(`HTTP ${resp.status}`);
            const html = await resp.text();

            const panel = document.createElement('div');
            panel.className = 'view-panel';
            panel.innerHTML = html;
            contentContainer.replaceChildren(panel);

            const startAnim = (()=>{
                let started = false;
                return ()=>{
                    if(started) return; started = true;
                    void panel.offsetWidth; // reflow
                    panel.classList.add('view-enter');
                    panel.addEventListener('animationend', ()=>panel.classList.remove('view-enter'), {once:true});
                    hideLoader();
                };
            })();

            const iframe = panel.querySelector('iframe');
            let fallback;
            if(iframe){
                iframe.addEventListener('load', ()=>{ startAnim(); if(fallback){clearTimeout(fallback);} }, {once:true});
                fallback = setTimeout(startAnim, 1500);
            }else{
                startAnim();
            }
        }catch(err){
            console.error('Load error', err);
            document.getElementById('content').innerHTML = `<div class="card"><h2>Ошибка загрузки</h2><p class="muted">${String(err)}</p></div>`;
            hideLoader();
        }
    }

    function showLoader(){ document.getElementById('loader')?.classList.remove('hidden'); }
    function hideLoader(){ document.getElementById('loader')?.classList.add('hidden'); }
})();