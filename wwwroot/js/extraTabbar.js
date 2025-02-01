const tabBar = document.getElementById('tabBar');
const newTabButton = document.getElementById('newTab');

function setActiveTab(tab) {
    document.querySelectorAll('.tab').forEach(t => t.classList.remove('active'));
    tab.classList.add('active');
}



document.querySelectorAll('.tab').forEach(tab => {
    tab.addEventListener('click', (e) => {
        if (e.target.classList.contains('close')) {
            tab.remove();
        } else {
            setActiveTab(tab);
        }
    });
});
