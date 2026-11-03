const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const test = require('node:test');

function panel(mode = 'create') {
    const sent = [], handlers = new Map(), captures = [];
    let mouseToggles = 0;
    function element() {
        return { style: {}, classList: { add() {}, toggle() {} }, addEventListener() {}, setAttribute() {},
            focus() {}, select() {}, getBoundingClientRect: () => ({ width: 440, height: 240 }) };
    }
    class MousePage {
        show(visible) { this.isVisible = visible; this._onVisibilityChange(); }
        _onVisibilityChange() {}
    }
    class MessageBox {
        constructor() { this.okButton = element(); this.okButton.disabled = false; }
        addText() {}
        addInput() { return element(); }
        addErrorMessage() {}
        displayErrorMessage() {}
        setIsValidCallback(callback) { this.validate = callback; }
        show(visible) { this.isVisible = visible; captures.push(visible); }
        callButton(button) {
            if (button === 1 || this.validate()) {
                if (button === 0) this.onOk();
                this.show(false);
            }
        }
    }
    const context = { MousePage, MessageBox, EMessageBoxButtons: { OkCancel: 1 }, EMessageBoxCallback: { Ok: 0 },
        document: { body: {}, head: {}, getElementById: () => null }, createElement: element,
        inputCaptureManager: { captureText: value => captures.push(value) },
        hudManager: { toggleEnhancedMouse: () => mouseToggles++ },
        engine: { on: (name, callback) => handlers.set(name, callback) },
        CPPMod: { sendModAction: (...args) => sent.push(args) },
        window: { setTimeout: callback => callback() } };
    const script = mode === 'rename' ? '/custom-elements-name-panel.js' : '/custom-elements-panel.js';
    vm.runInNewContext(fs.readFileSync(__dirname + script, 'utf8'), context);
    const instance = mode === 'rename' ? context.window.customElementsNamePanel : context.window.customElementsPanel;
    if (mode === 'rename') instance.open({ construct: '1234', element: '3456', nonce: 'rename', name: 'Wing' });
    else handlers.get('CustomElements.open')(JSON.stringify({ constructId: '1234', elementId: '2345',
        nonce: 'nonce', requestId: 'request', maximumBatch: 1000 }));
    return { instance, sent, captures, handlers, mouseToggles: () => mouseToggles };
}

test('OK submits the entered amount and both source IDs exactly once', () => {
    const { instance, sent } = panel();
    assert.equal(instance.ok.textContent, 'OK');
    assert.equal(instance.cancel.textContent, 'Cancel');
    instance.quantity.value = '7'; instance.submit(); instance.submit();
    const creation = sent.filter(call => call[1] === 20);
    assert.equal(creation.length, 1);
    assert.equal(creation[0][0], 'CustomElements');
    assert.deepEqual(Array.from(creation[0][2]), [1234, 2345]);
    assert.deepEqual(JSON.parse(creation[0][3]), { nonce: 'nonce', requestId: 'request', quantity: 7 });
});

test('shared rename submits the name and numeric source IDs once', () => {
    const { instance, sent, captures, mouseToggles } = panel('rename');
    instance.input.value = ' Wing: "Left" · v2 '; instance.submit(); instance.submit();
    assert.equal(mouseToggles(), 1);
    assert.equal(sent.length, 1);
    assert.equal(sent[0][1], 21);
    assert.deepEqual(Array.from(sent[0][2]), [1234, 3456]);
    assert.deepEqual(JSON.parse(sent[0][3]), { nonce: 'rename', name: 'Wing: "Left" · v2' });
    assert.equal(captures.at(-1), false);
});

test('shared rename rejects invalid names and unsafe IDs without closing', () => {
    const { instance, sent } = panel('rename');
    for (const name of ['', 'bad\nname', 'x'.repeat(81)]) { instance.input.value = name; instance.submit(); }
    instance.input.value = 'Wing'; instance.source.element = '9007199254740993'; instance.submit();
    assert.equal(sent.length, 0);
    assert.equal(instance.isVisible, true);
    assert.equal(instance.ok.disabled, false);
});

test('native rename Cancel submits nothing', () => {
    const { instance, sent, captures } = panel('rename');
    instance.callButton(1);
    assert.equal(instance.isVisible, false);
    assert.equal(sent.length, 0);
    assert.equal(captures.at(-1), false);
});

test('a native binding failure is shown and leaves the amount editable', () => {
    const { instance } = panel();
    instance.source.constructId = '9007199254740993';
    instance.submit();
    assert.match(instance.message.textContent, /cannot represent this construct/);
    assert.equal(instance.ok.disabled, false);
    assert.equal(instance.quantity.disabled, false);
    assert.equal(instance.cancel.textContent, 'Cancel');
});

test('opening enables the mouse once and a visible result does not toggle it off', () => {
    const { instance, mouseToggles } = panel();
    assert.equal(mouseToggles(), 1);
    instance.result({ message: 'Created 1 custom item.' });
    assert.equal(mouseToggles(), 1);
    instance.show(false); instance.show(true);
    assert.equal(mouseToggles(), 2);
});

test('Cancel closes the dialog and releases text capture without creating items', () => {
    const { instance, sent, captures } = panel();
    instance.show(false);
    assert.equal(instance.isVisible, false);
    assert.equal(captures.at(-1), false);
    assert.equal(sent.filter(call => call[1] === 20).length, 0);
});

test('invalid quantities are rejected while the dialog stays editable', () => {
    const { instance, sent } = panel();
    for (const value of ['0', '-1', '1.2', '1001', 'abc', '']) {
        instance.quantity.value = value; instance.submit();
        assert.equal(instance.ok.disabled, false);
    }
    assert.equal(sent.filter(call => call[1] === 20).length, 0);
});

test('Escape cancels and Enter submits without forwarding the key to the game', () => {
    const { instance, sent } = panel();
    let prevented = 0;
    instance.key({ key: 'Escape', preventDefault() { prevented++; }, stopPropagation() {} });
    assert.equal(instance.isVisible, false);
    instance.show(true); instance.quantity.value = '2';
    instance.key({ key: 'Enter', preventDefault() { prevented++; }, stopPropagation() {} });
    assert.equal(prevented, 2);
    assert.equal(sent.filter(call => call[1] === 20).length, 1);
});

