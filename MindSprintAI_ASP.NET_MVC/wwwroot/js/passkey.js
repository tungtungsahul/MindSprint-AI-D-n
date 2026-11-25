(() => {
    'use strict';
    const invalid = 'Không xác thực được passkey. Hãy thử lại.';
    const decode = value => {
        const text = value.replace(/-/g, '+').replace(/_/g, '/');
        return Uint8Array.from(atob(text.padEnd(Math.ceil(text.length / 4) * 4, '=')), c => c.charCodeAt(0));
    };
    const encode = value => btoa(Array.from(new Uint8Array(value), c => String.fromCharCode(c)).join(''))
        .replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
    function parse(options, create) {
        const native = create ? PublicKeyCredential.parseCreationOptionsFromJSON : PublicKeyCredential.parseRequestOptionsFromJSON;
        if (native) return native.call(PublicKeyCredential, options);
        options.challenge = decode(options.challenge);
        if (create) options.user.id = decode(options.user.id);
        for (const item of options[create ? 'excludeCredentials' : 'allowCredentials'] || []) item.id = decode(item.id);
        return options;
    }
    function serialize(credential) {
        if (credential.toJSON) return credential.toJSON();
        const r = credential.response;
        const response = { clientDataJSON: encode(r.clientDataJSON) };
        if (r.attestationObject) {
            response.attestationObject = encode(r.attestationObject);
            response.transports = r.getTransports ? r.getTransports() : [];
        } else {
            response.authenticatorData = encode(r.authenticatorData);
            response.signature = encode(r.signature);
            response.userHandle = r.userHandle ? encode(r.userHandle) : null;
        }
        return { id: credential.id, rawId: encode(credential.rawId), type: credential.type, response,
            authenticatorAttachment: credential.authenticatorAttachment, clientExtensionResults: credential.getClientExtensionResults() };
    }
    async function post(url, body, token) {
        const response = await fetch(url, { method: 'POST', credentials: 'same-origin', body,
            headers: { 'Accept': 'application/json', 'RequestVerificationToken': token } });
        const data = await response.json().catch(() => ({}));
        if (!response.ok || response.redirected) throw Object.assign(new Error(data.error || invalid), { name: 'PasskeyServerError' });
        return data;
    }
    for (const form of document.querySelectorAll('form[data-passkey]')) {
        const status = form.querySelector('[data-passkey-status]');
        if (!window.PublicKeyCredential || !navigator.credentials) {
            status.textContent = 'Trình duyệt này chưa hỗ trợ passkey. Hãy dùng cách đăng nhập khác.';
            continue;
        }
        form.querySelector('[data-passkey-controls]').hidden = false;
        form.addEventListener('submit', async event => {
            event.preventDefault();
            const button = form.querySelector('button');
            if (button.disabled) return;
            button.disabled = true;
            status.textContent = 'Đang chờ xác nhận trên thiết bị…';
            try {
                const token = form.querySelector('[name="__RequestVerificationToken"]').value;
                const create = form.dataset.passkey === 'create';
                const options = parse(await post(form.dataset.options, null, token), create);
                const credential = await navigator.credentials[create ? 'create' : 'get']({ publicKey: options });
                if (!credential) throw new Error(invalid);
                const body = new FormData(form);
                body.set('credentialJson', JSON.stringify(serialize(credential)));
                if (form.dataset.action === 'PasskeyLogin') body.set('rememberMe', document.getElementById('RememberMe')?.checked ? 'true' : 'false');
                const result = await post(form.action, body, token);
                window.location.assign(result.redirect);
            } catch (error) {
                status.textContent = error.name === 'NotAllowedError' ? 'Bạn đã hủy hoặc hết thời gian xác nhận. Có thể thử lại khi sẵn sàng.'
                    : error.name === 'InvalidStateError' ? 'Passkey này đã có trên thiết bị. Hãy chọn thiết bị khác.'
                    : error.name === 'SecurityError' ? 'Không thể dùng passkey ở địa chỉ trang này. Hãy mở MindSprint AI bằng link an toàn do giảng viên hoặc quản trị viên cung cấp.'
                    : error.name === 'PasskeyServerError' ? error.message : invalid;
            } finally { button.disabled = false; }
        });
    }
})();
