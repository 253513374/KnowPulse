const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const { join } = require('node:path');
const test = require('node:test');
const vm = require('node:vm');

const source = readFileSync(join(__dirname, '../../src/KnowPulse.HttpApi.Host/wwwroot/js/brand-login.js'), 'utf8');

function element(properties = {}) {
    const listeners = new Map();
    const attributes = new Map();
    return {
        textContent: '', hidden: false, dataset: {}, disabled: false,
        ...properties,
        addEventListener(name, callback) { listeners.set(name, callback); },
        fire(name, event = {}) { return Promise.resolve(listeners.get(name)?.(event)); },
        setAttribute(name, value) { attributes.set(name, value); },
        removeAttribute(name) { attributes.delete(name); },
        getAttribute(name) { return attributes.get(name); },
        focus() { this.focused = true; }
    };
}

function input(name, value, properties = {}) {
    const field = element({ name, value, type: 'text', required: true, ...properties });
    return Object.defineProperties(field, {
        willValidate: { get() { return !this.disabled; } },
        validity: { get() { return { valid: !this.required || this.value !== '', valueMissing: this.value === '' }; } }
    });
}

function jsonResponse(result, status = 200) {
    return {
        ok: status < 400, status,
        headers: { get: () => 'application/json; charset=utf-8' },
        json: async () => result
    };
}

function page(fetchResponse) {
    const username = input('LoginInput.UserNameOrEmailAddress', 'teacher@example.invalid');
    const password = input('LoginInput.Password', 'kept-password', { type: 'password' });
    const disabled = input('DisabledField', 'retained', { disabled: true });
    const token = input('__RequestVerificationToken', 'test-token', { type: 'hidden', required: false });
    const submit = element({ name: 'Action', value: 'Login', type: 'submit', textContent: '登录' });
    const toggle = element({ type: 'button' });
    const feedback = element({ hidden: true });
    const fields = [username, password, disabled];
    const errors = fields.map(field => element({ hidden: true, dataset: { fieldErrorFor: field.name } }));
    const elements = [username, password, disabled, token, submit, toggle];
    elements.namedItem = name => elements.find(item => item.name === name);
    const form = element({
        method: 'post', action: 'https://localhost:44305/Account/Login', elements,
        querySelector: selector => selector === '[data-password-toggle]' ? toggle : submit,
        querySelectorAll: selector => selector === '[data-field-error]' ? errors : fields
    });
    const requests = [];
    const destinations = [];
    const fetch = async (...args) => {
        requests.push(args);
        return fetchResponse(...args);
    };
    class FormData {
        constructor(currentForm) {
            this.values = new Map(currentForm.elements.filter(item => item.name && !item.disabled && item.type !== 'submit')
                .map(item => [item.name, item.value]));
        }
        get(name) { return this.values.get(name); }
        set(name, value) { this.values.set(name, value); }
    }
    class DOMParser {
        parseFromString() { throw new Error('Unexpected HTML response in this test'); }
    }
    const window = {
        fetch, FormData, DOMParser,
        location: { origin: 'https://localhost:44305', href: form.action, assign: url => destinations.push(url) }
    };
    const document = { getElementById: id => id === 'kp-login-form' ? form : feedback };
    vm.runInNewContext(source, { document, window, fetch, FormData, DOMParser, URL });
    const submitForm = () => {
        const event = { submitter: submit, preventDefault() { this.prevented = true; } };
        const finished = form.fire('submit', event);
        assert.equal(event.prevented, true);
        return finished;
    };
    return { username, password, disabled, token, submit, toggle, feedback, fields, errors, form, requests, destinations, submitForm };
}

test('required fields stop requests and focus the first invalid field', async () => {
    const login = page(() => { throw new Error('A missing field must not make a request'); });
    login.username.value = '';
    login.password.value = '';

    await login.submitForm();

    assert.equal(login.requests.length, 0);
    assert.equal(login.username.focused, true);
    assert.equal(login.username.getAttribute('aria-invalid'), 'true');
    assert.equal(login.password.getAttribute('aria-invalid'), 'true');
    assert.equal(login.errors[0].textContent, '请输入用户名或邮箱');
    assert.equal(login.errors[1].textContent, '请输入密码');
    assert.equal(login.submit.disabled, false);
});

test('network failure preserves input and allows a second request', async () => {
    let calls = 0;
    const login = page(() => {
        if (++calls === 1) throw new TypeError('Offline');
        return jsonResponse({ error: { message: '账号或密码不正确' } }, 400);
    });

    await login.submitForm();

    assert.equal(login.feedback.dataset.state, 'network');
    assert.equal(login.submit.textContent, '重试登录');
    assert.equal(login.username.value, 'teacher@example.invalid');
    assert.equal(login.password.value, 'kept-password');
    assert.equal(login.submit.disabled, false);
    assert.equal(login.password.disabled, false);

    await login.submitForm();

    assert.equal(login.requests.length, 2);
    assert.equal(login.feedback.textContent, '账号或密码不正确');
    assert.equal(login.password.value, 'kept-password');
    assert.equal(login.submit.textContent, '登录');
});

test('pending submission prevents duplicates and restores previous disabled states', async () => {
    let complete;
    const response = new Promise(resolve => { complete = resolve; });
    const login = page(() => response);
    const first = login.submitForm();

    assert.equal(login.form.getAttribute('aria-busy'), 'true');
    assert.equal(login.submit.textContent, '处理中…');
    assert.equal(login.form.elements.every(item => item.disabled), true);
    await login.submitForm();
    assert.equal(login.requests.length, 1);
    const request = login.requests[0][1];
    assert.equal(request.method, 'POST');
    assert.equal(request.credentials, 'same-origin');
    assert.equal(request.redirect, 'error');
    assert.equal(request.body.get('__RequestVerificationToken'), 'test-token');
    assert.equal(request.body.get('LoginInput.Password'), 'kept-password');
    assert.equal(request.body.get('Action'), 'Login');

    complete(jsonResponse({ error: { message: '请检查输入内容' } }, 400));
    await first;

    assert.equal(login.form.getAttribute('aria-busy'), 'false');
    assert.equal(login.submit.disabled, false);
    assert.equal(login.password.disabled, false);
    assert.equal(login.token.disabled, false);
    assert.equal(login.disabled.disabled, true);
});

test('successful server JSON redirect clears the password and keeps controls pending', async () => {
    const destination = 'https://localhost:44307/authentication/login-callback';
    const login = page(() => jsonResponse({ redirectUrl: destination }));

    await login.submitForm();

    assert.deepEqual(login.destinations, [destination]);
    assert.equal(login.password.value, '');
    assert.equal(login.form.getAttribute('aria-busy'), 'true');
    assert.equal(login.submit.disabled, true);
});

for (const destination of ['javascript:alert(1)', 'https://user:password@localhost:44307/']) {
    test(`rejects unsafe server redirect ${destination}`, async () => {
        const login = page(() => jsonResponse({ redirectUrl: destination }));

        await login.submitForm();

        assert.deepEqual(login.destinations, []);
        assert.equal(login.feedback.dataset.state, 'error');
        assert.equal(login.password.value, 'kept-password');
        assert.equal(login.submit.disabled, false);
    });
}

test('password visibility toggle keeps its accessible state in sync', async () => {
    const login = page(() => { throw new Error('A visibility toggle must not make a request'); });

    await login.toggle.fire('click');
    assert.equal(login.password.type, 'text');
    assert.equal(login.toggle.getAttribute('aria-pressed'), 'true');
    assert.equal(login.toggle.getAttribute('aria-label'), '隐藏密码');

    await login.toggle.fire('click');
    assert.equal(login.password.type, 'password');
    assert.equal(login.toggle.getAttribute('aria-pressed'), 'false');
    assert.equal(login.toggle.getAttribute('aria-label'), '显示密码');
    assert.equal(login.password.value, 'kept-password');
    assert.equal(login.requests.length, 0);
});
