document.addEventListener('DOMContentLoaded', () => {
    const tabBar = document.getElementById('tabBar');

    function setActiveTab(tab) {
        console.log('Setting active tab:', tab);
        document.querySelectorAll('.tab').forEach(t => t.classList.remove('active'));
        tab.classList.add('active');
    }


    document.querySelectorAll('.tab').forEach(tab => {
        tab.addEventListener('click', (e) => {
            console.log('Tab clicked:', tab);
            if (!e.target.classList.contains('close')) {
                setActiveTab(tab);
            }
        });
    });

});
