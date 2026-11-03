(() => {
    try {
    const version = 9;
    function selectedDesign(inspector) {
        const type = inspector.getAttribute('data-ce-item-type');
        const image = inspector.querySelector('img.item_icon');
        const source = image ? image.getAttribute('src') || '' : '';
        return (window.customElementsItemCatalog || window.customElementsPreviewDesigns || [])
            .find(design => type ? String(design.type) === type : source.indexOf(design.hash) !== -1);
    }
    if (window.customElementsPreview && window.customElementsPreview.version === version) {
        window.customElementsPreview.update(); window.customElementsPreview.ready = true; return;
    }
    if (window.customElementsPreview && window.customElementsPreview.dispose) window.customElementsPreview.dispose();
    function appendContentsSection(parent, id, label, entries, materials) {
        const drilldown = new Drilldown();
        const count = materials ? entries.length : entries.reduce((total, entry) => total + entry.quantity, 0);
        const header = new DrilldownElementLabelAndString(id, null, { label: label, value: count });
        drilldown.addElement(header);
        entries.slice().sort((a, b) => itemBank.getItemDefinition(String(a.type)).fullName.localeCompare(
            itemBank.getItemDefinition(String(b.type)).fullName)).forEach(entry => {
            const row = new DrilldownItemQuantity(String(entry.type), id, {
                itemDefinition: itemBank.getItemDefinition(String(entry.type)), itemQuantity: entry.quantity
            });
            if (materials) {
                const parts = entry.quantity.toFixed(6).replace(/\.?0+$/, '').split('.');
                row.valueNode.innerHTML = FormatNumber.toThousandSeparatedString(Number(parts[0])) +
                    (parts[1] ? '.' + parts[1] : '') + ' m<sup>3</sup>';
            }
            drilldown.addElement(row);
        });
        header.toggle(true); parent.appendChild(drilldown.wrapperNode);
    }
    function updateContents(fields, design) {
        if (!fields) return;
        let contents = fields.querySelector('.ce-contents');
        if (!contents && design) {
            contents = document.createElement('div'); contents.className = 'ce-contents';
            fields.appendChild(contents);
        }
        if (!contents) return;
        if (contents.classList.contains('hide') === !!design) contents.classList.toggle('hide', !design);
        if (!design) return;
        if (!contents.classList.contains('package_fields')) contents.className += ' package_fields';
        const elements = design.elements || [], materials = design.materials || [];
        const key = JSON.stringify([version, design.type, elements, materials]);
        if (contents.getAttribute('data-ce-contents') === key) return;
        while (contents.firstChild) contents.removeChild(contents.firstChild);
        const description = document.createElement('div'); description.className = 'item_description_wrapper';
        const holder = document.createElement('div'); holder.className = 'item_description_holder';
        const caption = document.createElement('p'); caption.textContent = 'Contents per item';
        holder.appendChild(caption); description.appendChild(holder); contents.appendChild(description);
        appendContentsSection(contents, 'ce-elements', 'Elements', elements, false);
        appendContentsSection(contents, 'ce-materials', 'Materials', materials, true);
        contents.setAttribute('data-ce-contents', key);
    }
    function sync() {
        Array.from(document.querySelectorAll('.item_inspector')).forEach(inspector => {
            const controls = inspector.querySelector('.item_inspector_button_wrapper');
            const popout = inspector.querySelector('.open_in_new_inspector_button');
            if (!controls || !popout) return;
            const design = selectedDesign(inspector);
            let row = inspector.querySelector('.ce-design-id');
            const fields = inspector.querySelector('.item_datatable .custom_fields');
            if (!row && fields && design) {
                row = document.createElement('div'); row.className = 'item_data ce-design-id';
                const label = document.createElement('h4'); label.className = 'label'; label.textContent = 'Design ID';
                const value = document.createElement('p'); value.className = 'value';
                value.style.cssText = 'word-break:break-all;white-space:normal';
                row.appendChild(label); row.appendChild(value); fields.appendChild(row);
            }
            if (row) {
                if (design && row.querySelector('.value').textContent !== design.hash) {
                    row.querySelector('.value').textContent = design.hash;
                    row.querySelector('.value').setAttribute('data-tooltip', design.hash);
                }
                if (row.classList.contains('hide') === !!design) row.classList.toggle('hide', !design);
            }
            updateContents(fields, design);
            const canRename = !!design && design.canRename !== false;
            let button = controls.querySelector('.ce-rename-item');
            if (!button && canRename) {
                button = document.createElement('div');
                button.className = 'item_inspector_window_input ce-rename-item';
                button.setAttribute('role', 'button'); button.tabIndex = 0;
                button.title = 'Rename shared custom item (all copies)';
                button.setAttribute('data-tooltip', button.title);
                button.setAttribute('aria-label', button.title);
                const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
                svg.setAttribute('class', 'icon_sml');
                const use = document.createElementNS('http://www.w3.org/2000/svg', 'use');
                use.setAttributeNS('http://www.w3.org/1999/xlink', 'href', '#icon_edit');
                svg.appendChild(use); button.appendChild(svg);
                const activate = event => {
                    if (event.type === 'keydown' && event.key !== 'Enter' && event.key !== ' ') return;
                    event.preventDefault(); event.stopPropagation();
                    const current = selectedDesign(inspector);
                    if (current && current.canRename !== false)
                        CPPMod.sendModAction('CustomElements', 14, [0, 0], JSON.stringify({ type: current.type }));
                };
                button.addEventListener('click', activate); button.addEventListener('keydown', activate);
                controls.insertBefore(button, popout);
            }
            if (button) {
                button.style.cssText = 'cursor:pointer;flex-shrink:0;margin-right:4px';
                controls.style.display = 'flex'; controls.style.flexDirection = 'row';
                controls.style.alignItems = 'center';
            }
            if (button && button.classList.contains('hide') === canRename) button.classList.toggle('hide', !canRename);
        });
    }
    let queued = false;
    const onChange = () => {
        if (queued) return;
        queued = true;
        window.setTimeout(() => { queued = false; sync(); }, 0);
    };
    const observer = typeof MutationObserver === 'function' ? new MutationObserver(onChange) : null;
    if (observer) observer.observe(document.body, { subtree: true, childList: true, attributes: true,
        attributeFilter: ['src', 'class', 'data-ce-item-type'] });
    const timer = observer ? null : window.setInterval(sync, 500);
    const originalDisplay = typeof ItemInspector === 'function' ? ItemInspector.prototype.displayItem : null;
    const originalClear = originalDisplay ? ItemInspector.prototype._clearDisplayedItem : null;
    function rememberType(inspector) {
        const item = inspector.displayedItem;
        inspector.wrapperNode.setAttribute('data-ce-item-type', item ? String(item.typeId) : '');
    }
    function displayItem() {
        const result = originalDisplay.apply(this, arguments);
        rememberType(this); onChange(); return result;
    }
    function clearItem() {
        const result = originalClear.apply(this, arguments);
        rememberType(this); onChange(); return result;
    }
    if (originalDisplay) ItemInspector.prototype.displayItem = displayItem;
    if (originalClear) ItemInspector.prototype._clearDisplayedItem = clearItem;
    if (typeof inventoryPanelView !== 'undefined' && inventoryPanelView.itemInspector)
        rememberType(inventoryPanelView.itemInspector);
    window.customElementsPreview = { version: version, ready: false, update: sync, dispose: () => {
        if (observer) observer.disconnect(); else window.clearInterval(timer);
        if (originalDisplay && ItemInspector.prototype.displayItem === displayItem)
            ItemInspector.prototype.displayItem = originalDisplay;
        if (originalClear && ItemInspector.prototype._clearDisplayedItem === clearItem)
            ItemInspector.prototype._clearDisplayedItem = originalClear;
    } };
    sync();
    window.customElementsPreview.ready = true;
    } catch (error) {
        CPPMod.sendModAction('CustomElements', 95, [0, 0], JSON.stringify({ previewError: String(error) }));
    }
})();
