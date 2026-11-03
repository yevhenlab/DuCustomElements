(() => {
    if (window.customElementsNamePanel && window.customElementsNamePanel.version === 2) return;
    if (window.customElementsNamePanel) window.customElementsNamePanel.show(false);
    class CustomElementsNamePanel extends MessageBox {
        constructor() {
            super('Rename shared custom item', EMessageBoxButtons.OkCancel, false, true, '');
            this.version = 2;
            this.addText('Changes the shared name of all copies. Reconnect to see the new name.');
            this.input = this.addInput('text', null, 'ce_shared_item_name', null, true);
            this.input.setAttribute('maxlength', 80);
            this.addErrorMessage('error_input', 'ce_shared_item_name_error');
            this.ok = this.okButton;
            this.defaultButton = this.okButton;
            this.setIsValidCallback(() => this.validateName());
            this.onOk = () => {
                CPPMod.sendModAction('CustomElements', 21,
                    [Number(this.source.construct), Number(this.source.element)],
                    JSON.stringify({ nonce: this.source.nonce, name: this.input.value.trim() }));
                this.submitted = true;
            };
        }
        validateName() {
            const name = this.input.value.trim();
            const ids = [Number(this.source.construct), Number(this.source.element)];
            const valid = name.length > 0 && name.length <= 80 &&
                !/[\u0000-\u001f\u007f-\u009f]/.test(name) &&
                ids.every(id => Number.isSafeInteger(id) && id >= 0);
            if (!valid) this.displayErrorMessage('ce_shared_item_name_error', 'ce_shared_item_name',
                'Enter a name of 1–80 characters without control characters.');
            return valid && !this.submitted;
        }
        open(source) {
            this.source = source; this.input.value = source.name; this.submitted = false;
            if (Number(source.construct) !== 0 && !hudManager.enhancedMouse) hudManager.toggleEnhancedMouse();
            this.show(true);
            window.setTimeout(() => { this.input.focus(); this.input.select(); }, 100);
        }
        submit() { this.callButton(EMessageBoxCallback.Ok); }
    }
    window.customElementsNamePanel = new CustomElementsNamePanel();
})();
