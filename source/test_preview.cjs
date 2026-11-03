const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const test = require('node:test');

function fixture(canRename = true, contents = {}) {
    function node(tag = 'div', classes = '') {
        const n = { tag, className: classes, children: [], attrs: {}, style: {}, handlers: {}, textContent: '',
            appendChild(child) { this.children.push(child); return child; },
            get firstChild() { return this.children[0]; },
            removeChild(child) { this.children.splice(this.children.indexOf(child), 1); },
            insertBefore(child, before) { this.children.splice(this.children.indexOf(before), 0, child); },
            setAttribute(key, value) { this.attrs[key] = value; }, getAttribute(key) { return this.attrs[key]; },
            setAttributeNS(ns, key, value) { this.setAttribute(key, value); },
            addEventListener(key, callback) { this.handlers[key] = callback; },
            querySelector(selector) {
                const last = selector.split(' ').at(-1), cls = last.split('.').at(-1);
                for (const child of this.children) {
                    if (child.className.split(' ').includes(cls)) return child;
                    const result = child.querySelector(selector); if (result) return result;
                }
                return null;
            } };
        n.classList = {
            contains: cls => n.className.split(' ').includes(cls),
            toggle(cls, value) {
                const classes = new Set(n.className.split(' '));
                if (value) classes.add(cls); else classes.delete(cls);
                n.className = [...classes].join(' ');
            }
        };
        return n;
    }
    const inspector = node('div', 'item_inspector'), image = node('img', 'item_icon');
    image.attrs.src = '../../resources_generated/custom_elements/' + 'a'.repeat(64) + '.r4.png';
    const controls = node('div', 'item_inspector_button_wrapper');
    const popout = node('div', 'open_in_new_inspector_button'); controls.appendChild(popout);
    const fields = node('div', 'custom_fields');
    inspector.appendChild(image); inspector.appendChild(controls); inspector.appendChild(fields);
    const inspectors = [inspector];
    class Drilldown {
        constructor() { this.wrapperNode = node('div', 'native-drilldown'); }
        addElement(entry) { this.wrapperNode.appendChild(entry.wrapperNode); }
    }
    class DrilldownElementLabelAndString {
        constructor(id, parent, data) {
            this.wrapperNode = node('div', 'native-header'); this.wrapperNode.textContent = data.label;
            const value = node('div', 'string'); value.textContent = String(data.value);
            this.wrapperNode.appendChild(value);
        }
        toggle(visible) { this.wrapperNode.expanded = visible; }
    }
    class DrilldownItemQuantity {
        constructor(id, parent, data) {
            this.wrapperNode = node('div', 'native-item'); this.wrapperNode.type = id;
            this.wrapperNode.textContent = data.itemDefinition.fullName;
            this.valueNode = node('div', 'value'); this.valueNode.textContent = String(data.itemQuantity);
            this.wrapperNode.appendChild(this.valueNode);
        }
    }
    const sent = [], context = {
        Drilldown, DrilldownElementLabelAndString, DrilldownItemQuantity,
        itemBank: { getItemDefinition: type => {
            assert.equal(typeof type, 'string'); return { fullName: 'Item ' + type };
        } },
        FormatNumber: { toThousandSeparatedString: value => String(value) },
        document: { body: {}, querySelectorAll: () => inspectors, createElement: node,
            createElementNS: (ns, tag) => node(tag) },
        MutationObserver: class { observe() {} disconnect() {} },
        CPPMod: { sendModAction: (...args) => sent.push(args) },
        window: { customElementsItemCatalog: [{ type: 123, hash: 'a'.repeat(64), canRename, ...contents }],
            setTimeout: callback => callback() }
    };
    vm.runInNewContext(fs.readFileSync(__dirname + '/custom-elements-preview.js', 'utf8'), context);
    return { inspector, inspectors, node, image, controls, popout, fields, sent, context,
        update: () => context.window.customElementsPreview.update() };
}

test('preview pencil appears beside pop-out and opens the selected shared design', () => {
    const f = fixture(), button = f.controls.children[0];
    assert.equal(f.controls.children[1], f.popout);
    assert.match(button.className, /ce-rename-item/);
    assert.equal(button.children[0].children[0].attrs.href, '#icon_edit');
    button.handlers.click({ type: 'click', preventDefault() {}, stopPropagation() {} });
    assert.equal(f.sent[0][1], 14);
    assert.deepEqual(Array.from(f.sent[0][2]), [0, 0]);
    assert.deepEqual(JSON.parse(f.sent[0][3]), { type: 123 });
    f.update(); assert.equal(f.controls.children.length, 2);
});

test('contents show per-item native element counts and precise cubic metres without inventory requirements', () => {
    const f = fixture(false, { elements: [{ type: 2, quantity: 3 }, { type: 1, quantity: 2 }],
        materials: [{ type: 9, quantity: 0.15625 }] });
    const contents = f.fields.querySelector('.ce-contents');
    assert.equal(contents.children[0].className, 'item_description_wrapper');
    assert.equal(contents.children[0].children[0].children[0].textContent, 'Contents per item');
    assert.equal(contents.classList.contains('package_fields'), true);
    const elements = contents.children[1], materials = contents.children[2];
    assert.equal(elements.children[0].textContent, 'Elements');
    assert.equal(elements.children[0].children[0].textContent, '5');
    assert.equal(elements.children[0].expanded, true);
    assert.equal(elements.children[1].type, '1');
    assert.equal(elements.children[1].children[0].textContent, '2');
    assert.equal(materials.children[0].textContent, 'Materials');
    assert.equal(materials.children[0].children[0].textContent, '1');
    assert.equal(materials.children[1].children[0].innerHTML, '0.15625 m<sup>3</sup>');
    f.update(); assert.equal(contents.children.length, 3);
    f.image.attrs.src = 'vanilla.png'; f.update();
    assert.equal(contents.classList.contains('hide'), true);
});

test('empty sections and a cleared inspector rebuild without retaining another design contents', () => {
    const f = fixture();
    let contents = f.fields.querySelector('.ce-contents');
    assert.equal(contents.children[1].children[0].children[0].textContent, '0');
    assert.equal(contents.children[2].children[0].children[0].textContent, '0');
    f.fields.children = []; f.update();
    contents = f.fields.querySelector('.ce-contents');
    assert.equal(contents.children.length, 3);
    assert.equal(f.context.window.customElementsPreview.ready, true);
});

test('a separately created item inspector receives the same package contents', () => {
    const f = fixture(false, { elements: [{ type: 2, quantity: 1 }], materials: [] });
    const popup = f.node('div', 'item_inspector'); popup.setAttribute('data-ce-item-type', '123');
    const controls = f.node('div', 'item_inspector_button_wrapper');
    controls.appendChild(f.node('div', 'open_in_new_inspector_button'));
    popup.appendChild(controls); popup.appendChild(f.node('div', 'custom_fields'));
    f.inspectors.push(popup); f.update();
    const contents = popup.querySelector('.ce-contents');
    assert.equal(contents.children[1].children[0].textContent, 'Elements');
    assert.equal(contents.children[1].children[0].children[0].textContent, '1');
    assert.equal(contents.children[1].children[1].textContent, 'Item 2');
    assert.equal(controls.children.length, 1);
});

test('all recipients see immutable Design ID, but only the creator gets Rename', () => {
    const f = fixture(false);
    assert.equal(f.controls.children.length, 1);
    assert.equal(f.fields.querySelector('.value').textContent, 'a'.repeat(64));
    assert.equal(f.fields.querySelector('.label').textContent, 'Design ID');
    f.image.attrs.src = 'vanilla-light.png'; f.update();
    assert.equal(f.fields.children[0].classList.contains('hide'), true);
});

test('changing selection hides the old action and never submits the former item', () => {
    const f = fixture(), button = f.controls.children[0];
    f.image.attrs.src = 'vanilla-light.png'; f.update();
    assert.equal(button.classList.contains('hide'), true);
    button.handlers.click({ type: 'click', preventDefault() {}, stopPropagation() {} });
    assert.equal(f.sent.length, 0);
});

test('native item identity works without a generated voxel icon and details rebuild safely', () => {
    const f = fixture();
    f.image.attrs.src = 'native-element-icon.png';
    f.inspector.setAttribute('data-ce-item-type', '123');
    f.fields.children = []; f.update();
    assert.equal(f.controls.children[0].classList.contains('hide'), false);
    assert.equal(f.fields.querySelector('.value').textContent, 'a'.repeat(64));
    f.inspector.setAttribute('data-ce-item-type', '999'); f.update();
    assert.equal(f.controls.children[0].classList.contains('hide'), true);
});
