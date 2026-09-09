(() => {
    const loadingClass = 'is-submit-loading';

    function setButtonLoading(button, loadingText) {
        if (!button || button.dataset.submitLoading === 'true') {
            return;
        }

        button.dataset.submitLoading = 'true';
        button.dataset.originalHtml = button.innerHTML;
        button.disabled = true;
        button.setAttribute('aria-busy', 'true');
        button.classList.add(loadingClass);

        const label = loadingText || button.dataset.loadingText || 'Traitement...';
        button.innerHTML = `<span class="submit-loading-spinner" aria-hidden="true"></span><span>${label}</span>`;
    }

    document.addEventListener('submit', event => {
        const form = event.target;
        if (!(form instanceof HTMLFormElement) || form.dataset.submitLoading !== 'true') {
            return;
        }

        const submitter = event.submitter instanceof HTMLButtonElement
            ? event.submitter
            : form.querySelector('button[type="submit"], input[type="submit"]');

        setButtonLoading(submitter, form.dataset.loadingText);
    }, true);
})();
