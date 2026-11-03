(() => {
    const styleId = 'custom-elements-dialog-style';
    const style = document.getElementById(styleId) || createElement(document.head, 'style');
    style.id = styleId;
    style.textContent = '.ce-dialog{position:absolute!important;left:50%!important;top:25%!important;' +
        'transform:translateX(-50%);width:460px!important;min-width:0!important;max-width:80vw!important;' +
        'height:auto!important;min-height:0!important;bottom:auto!important;z-index:20000;' +
        'background:#10232e;border:1px solid #7693a3;color:#e9f4f8}' +
        '.ce-dialog .header{height:auto!important;min-width:0!important;padding:16px!important}' +
        '.ce-dialog .panel_title{font-size:20px!important;line-height:1.3!important}' +
        '.ce-dialog .content{height:auto!important;min-height:0!important;width:auto!important;' +
        'min-width:0!important;padding:20px!important}' +
        '.ce-dialog p,.ce-dialog label{font-size:18px!important;line-height:1.4!important}' +
        '.ce-dialog input{font-size:20px!important;box-sizing:border-box;color:#fff;background:#07131b;' +
        'border:1px solid #91b3c4}' +
        '.ce-dialog button{font-size:18px!important;line-height:1.2!important;min-width:96px!important;' +
        'padding:10px 16px!important;position:static!important;width:auto!important;height:auto!important;' +
        'color:#fff;background:#294655;border:1px solid #91b3c4;cursor:pointer}' +
        '.ce-dialog button:disabled{opacity:.45;cursor:default}';
    function showWithMouse(visible) {
        if (visible && !this.isVisible) hudManager.toggleEnhancedMouse();
        MousePage.prototype.show.call(this, visible);
    }
    function sendToServer(action, quantity) {
        try {
            const ids = [Number(this.source.constructId), Number(this.source.elementId)];
            if (ids.some(id => !Number.isSafeInteger(id) || id < 0))
                throw new Error('The native mod interface cannot represent this construct or element ID.');
            CPPMod.sendModAction('CustomElements', action, ids,
                JSON.stringify({ nonce: this.source.nonce, requestId: this.source.requestId, quantity }));
        } catch (error) {
            this.message.textContent = 'Could not send request: ' + error.message;
            this.ok.disabled = false; this.quantity.disabled = false;
            this.cancel.textContent = 'Cancel';
        }
    }
    if (window.customElementsPanel) {
        window.customElementsPanel.wrapperNode.classList.add('ce-dialog');
        window.customElementsPanel.show = showWithMouse;
        window.customElementsPanel.send = sendToServer;
    }
    if (window.customElementsPanel) return;
    class CustomElementsPanel extends MousePage {
        show(visible) { showWithMouse.call(this, visible); }
        constructor() {
            super();
            this.wrapperNode = createElement(document.body, 'div', 'mining_unit_panel');
            this.wrapperNode.classList.add('ce-dialog');
            this.wrapperNode.style.width = '440px';
            this.wrapperNode.classList.add('hide');
            const header = createElement(this.wrapperNode, 'div', 'header');
            createElement(header, 'div', 'panel_title').textContent = 'Create custom items';
            const content = createElement(this.wrapperNode, 'div', 'content');
            content.style.cssText = 'display:block;padding:20px';
            createElement(content, 'p').textContent = 'How many custom items do you want to create?';
            createElement(content, 'p').textContent =
                'Each copy consumes matching honeycomb from your active inventory or linked container.';
            const label = createElement(content, 'label');
            label.textContent = 'Amount';
            this.quantity = createElement(label, 'input');
            this.quantity.type = 'number'; this.quantity.min = '1'; this.quantity.value = '1';
            this.quantity.style.cssText = 'display:block;width:100%;margin:8px 0;padding:8px';
            this.quantity.addEventListener('keydown', event => this.key(event));
            this.message = createElement(content, 'p');
            this.message.setAttribute('aria-live', 'polite');
            const buttons = createElement(content, 'div');
            buttons.style.cssText = 'display:flex;gap:12px;justify-content:flex-end;margin-top:18px';
            this.ok = createElement(buttons, 'button', 'generic_button');
            this.ok.textContent = 'OK'; this.ok.addEventListener('click', () => this.submit());
            this.cancel = createElement(buttons, 'button', 'generic_button');
            this.cancel.textContent = 'Cancel'; this.cancel.addEventListener('click', () => this.show(false));
            engine.on('CustomElements.open', payload => this.open(payload));
            engine.on('CustomElements.result', payload => this.result(payload));
        }
        _onVisibilityChange() {
            super._onVisibilityChange();
            this.wrapperNode.classList.toggle('hide', !this.isVisible);
            inputCaptureManager.captureText(!!this.isVisible, () => this.show(false));
        }
        key(event) {
            if (event.key !== 'Enter' && event.key !== 'Escape') return;
            event.preventDefault(); event.stopPropagation();
            if (event.key === 'Enter') this.submit(); else this.show(false);
        }
        open(payload) {
            this.source = typeof payload === 'string' ? JSON.parse(payload) : payload;
            this.quantity.max = String(this.source.maximumBatch);
            this.quantity.value = '1'; this.quantity.disabled = false;
            this.message.textContent = ''; this.ok.disabled = false;
            this.cancel.textContent = 'Cancel'; this.show(true);
            window.setTimeout(() => {
                if (!this.isVisible) return;
                this.quantity.focus(); this.quantity.select();
                const rect = this.wrapperNode.getBoundingClientRect();
                if (rect.width && rect.height) this.send(90, 0);
            }, 100);
        }
        result(payload) {
            const value = typeof payload === 'string' ? JSON.parse(payload) : payload;
            this.message.textContent = value.message;
            this.ok.disabled = true; this.quantity.disabled = true;
            this.cancel.textContent = 'Close'; this.show(true);
        }
        submit() {
            if (this.ok.disabled) return;
            const quantity = Number(this.quantity.value);
            if (!Number.isInteger(quantity) || quantity < 1 || quantity > this.source.maximumBatch) {
                this.message.textContent = `Enter a whole number from 1 to ${this.source.maximumBatch}.`;
                this.quantity.focus(); return;
            }
            this.ok.disabled = true; this.quantity.disabled = true;
            this.cancel.textContent = 'Close'; this.message.textContent = 'Creating custom items…';
            this.send(20, quantity);
        }
        send(action, quantity) {
            sendToServer.call(this, action, quantity);
        }
    }
    window.customElementsPanel = new CustomElementsPanel();
})();
