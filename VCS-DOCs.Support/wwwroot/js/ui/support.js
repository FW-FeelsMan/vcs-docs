(function () {
    const content = document.getElementById('content');
    function renderContactFrame() {
        const wrapper = document.createElement('div');
        wrapper.className = 'view-panel view-enter';
        wrapper.innerHTML = `
        <div class="contact-embed">
          <iframe class="contact-iframe" src="https://vcs-docs.support.local:7121/Support/Request"
                  loading="lazy" referrerpolicy="no-referrer"
                  allow="clipboard-read; clipboard-write"></iframe>
        </div>`;
        content.replaceChildren(wrapper);
        wrapper.addEventListener('animationend', () => wrapper.classList.remove('view-enter'), { once: true });
    }
})();