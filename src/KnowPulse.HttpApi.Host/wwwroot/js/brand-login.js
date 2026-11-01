(() => {
    'use strict';

    const form = document.getElementById('kp-login-form');
    if (!form) return;

    const password = form.elements.namedItem('LoginInput.Password');
    const toggle = form.querySelector('[data-password-toggle]');
    if (password && toggle) {
        toggle.addEventListener('click', () => {
            const visible = password.type === 'password';
            password.type = visible ? 'text' : 'password';
            toggle.setAttribute('aria-pressed', String(visible));
            toggle.setAttribute('aria-label', visible ? '隐藏密码' : '显示密码');
            toggle.textContent = visible ? '隐藏' : '显示';
        });
    }

    const submit = form.querySelector('[data-login-submit]');
    const feedback = document.getElementById('kp-login-feedback');
    if (!password || !submit || !feedback || !window.fetch || !window.FormData || !window.DOMParser ||
        form.method.toLowerCase() !== 'post' || new URL(form.action).origin !== window.location.origin) return;

    const idleLabel = submit.textContent.trim();
    const fields = Array.from(form.querySelectorAll('input:not([type="hidden"])'));
    const errors = Array.from(form.querySelectorAll('[data-field-error]'));
    let pending = false;
    let disabledStates = [];
    form.noValidate = true;

    function clearFeedback() {
        feedback.textContent = '';
        feedback.hidden = true;
        delete feedback.dataset.state;
        if (!pending) submit.textContent = idleLabel;
    }

    function showFeedback(message, state = 'error') {
        feedback.textContent = message;
        feedback.dataset.state = state;
        feedback.hidden = false;
        feedback.focus();
    }

    function setFieldError(name, message) {
        if (typeof name !== 'string') return;
        const field = fields.find(input => input.name.toLowerCase() === name.toLowerCase() ||
            input.name.toLowerCase().endsWith('.' + name.toLowerCase()));
        if (!field) return;
        const error = errors.find(span => span.dataset.fieldErrorFor?.toLowerCase() === field.name.toLowerCase());
        if (message) field.setAttribute('aria-invalid', 'true');
        else field.removeAttribute('aria-invalid');
        if (error) {
            error.textContent = message;
            error.hidden = !message;
        }
    }

    function setPending(value) {
        pending = value;
        form.setAttribute('aria-busy', String(value));
        if (value) {
            disabledStates = Array.from(form.elements).filter(element => 'disabled' in element)
                .map(element => [element, element.disabled]);
            disabledStates.forEach(([element]) => { element.disabled = true; });
        } else {
            disabledStates.forEach(([element, disabled]) => { element.disabled = disabled; });
            disabledStates = [];
        }
        submit.textContent = value ? '处理中…' : feedback.dataset.state === 'network' ? '重试登录' : idleLabel;
    }

    fields.forEach(field => field.addEventListener('input', () => {
        setFieldError(field.name, '');
        clearFeedback();
    }));

    form.addEventListener('submit', async event => {
        const submitter = event.submitter || submit;
        if (submitter.name !== 'Action' || submitter.value !== 'Login') return;
        event.preventDefault();
        if (pending) return;
        clearFeedback();
        fields.forEach(field => setFieldError(field.name, ''));

        const invalid = fields.filter(field => field.willValidate && !field.validity.valid);
        if (invalid.length > 0) {
            invalid.forEach(field => setFieldError(field.name, field.validity.valueMissing
                ? field.name === 'LoginInput.Password' ? '请输入密码' : '请输入用户名或邮箱'
                : field.validationMessage));
            invalid[0].focus();
            return;
        }

        const data = new FormData(form);
        data.set('Action', 'Login');
        setPending(true);
        let navigating = false;
        let receivedResponse = false;
        try {
            const response = await fetch(form.action, {
                method: 'POST',
                body: data,
                credentials: 'same-origin',
                cache: 'no-store',
                redirect: 'error',
                headers: { 'X-Requested-With': 'XMLHttpRequest', 'Accept': 'application/json, text/html' }
            });
            receivedResponse = true;

            if ((response.headers.get('Content-Type') || '').includes('application/json')) {
                const result = await response.json();
                if (response.ok && typeof result.redirectUrl === 'string') {
                    const destination = new URL(result.redirectUrl, window.location.href);
                    if (!['https:', 'http:'].includes(destination.protocol) || destination.username || destination.password) {
                        showFeedback('登录未完成，请刷新页面后重试。');
                        return;
                    }
                    password.value = '';
                    navigating = true;
                    window.location.assign(destination.href);
                    return;
                }
                (result.error?.validationErrors || []).forEach(error => {
                    (error.members || []).forEach(name => setFieldError(name, error.message || '请检查输入内容。'));
                });
                showFeedback(response.status < 500 && result.error?.message
                    ? result.error.message : '登录服务暂时不可用，请稍后重试。');
                return;
            }

            const page = new DOMParser().parseFromString(await response.text(), 'text/html');
            page.querySelectorAll('[data-field-error]').forEach(error => {
                if (error.dataset.fieldErrorFor) setFieldError(error.dataset.fieldErrorFor, error.textContent.trim());
            });
            const message = page.getElementById('kp-login-feedback')?.textContent.trim();
            showFeedback(message || (response.ok && page.getElementById('kp-login-form')
                ? '账号或密码不正确，请核对后重试。' : '登录未完成，请刷新页面后重试。'));
        } catch {
            showFeedback(receivedResponse ? '登录未完成，请刷新页面后重试。' : '暂时无法连接登录服务，请稍后重试。',
                receivedResponse ? 'error' : 'network');
        } finally {
            if (!navigating) setPending(false);
        }
    });
})();
