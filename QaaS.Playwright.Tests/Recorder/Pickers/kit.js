// Pages that imitate the DOM of the selects, menus and option pickers of common UI libraries, each in the modal flow of
// a React app: a button with only an id opens a dialog with a form, and a button in it opens a second dialog, over the
// first, where the person chooses. As in such an app, class names and many ids are generated anew on every load, click
// handlers are not attributes but props the app keeps on each element (React's __reactProps$ key) and runs from one
// listener on the document, and dialogs and menus are portals at the end of the body. Nothing here is a real library.
(() => {
  'use strict';
  const SVG = 'http://www.w3.org/2000/svg';
  const rand = (length = 6) => {
    let text = '';
    while (text.length < length) text += Math.random().toString(36).slice(2);
    return text.slice(0, length);
  };
  const number = (digits) => String(10 ** (digits - 1) + Math.floor(Math.random() * 9 * 10 ** (digits - 1)));
  const reactId = () => `:r${rand(2)}:`;
  const propsKey = `__reactProps$${rand(11)}`;
  const sheet = document.head.appendChild(document.createElement('style'));
  const rule = (text) => sheet.append(`${text}\n`);
  // An emotion-like class: a hash that changes with each load, and the component's label.
  const css = (label, style) => {
    const name = `css-${rand(7)}-${label}`;
    rule(`.${name}{${style}}`);
    return name;
  };

  // ------------------------------------------------------------------------------------------------- elements
  function h(tag, props, ...children) {
    const element = ['svg', 'path', 'circle'].includes(tag) ? document.createElementNS(SVG, tag) : document.createElement(tag);
    const handlers = {};
    for (const [key, value] of Object.entries(props ?? {})) {
      if (value === undefined || value === null || value === false) continue;
      if (/^on[A-Z]/.test(key)) handlers[key] = value;
      else if (key === 'ref') value(element);
      else if (key === 'style') element.style.cssText = value;
      else if (key === 'value' && !tag.includes('-')) element.value = value;
      else element.setAttribute(key, value === true ? '' : String(value));
    }
    if (Object.keys(handlers).length) element[propsKey] = handlers;
    for (const child of children.flat(Infinity))
      if (child !== undefined && child !== null && child !== false) element.append(child.nodeType ? child : String(child));
    return element;
  }
  const icon = (d, className) => h('svg', { class: className, viewBox: '0 0 24 24', width: 18, height: 18, 'aria-hidden': 'true', focusable: 'false' },
    h('path', { d, fill: 'currentColor' }));
  const ICONS = {
    plus: 'M19 13h-6v6h-2v-6H5v-2h6V5h2v6h6v2z',
    down: 'M7 10l5 5 5-5z',
    truck: 'M20 8h-3V4H3v13h2a3 3 0 006 0h2a3 3 0 006 0h2v-5l-3-4zM6 18.5a1.5 1.5 0 110-3 1.5 1.5 0 010 3zm12 0a1.5 1.5 0 110-3 1.5 1.5 0 010 3z',
    check: 'M9 16.2L4.8 12l-1.4 1.4L9 19 21 7l-1.4-1.4z',
  };

  // One listener per event on the document, as React's root does: each element's handler, from the target up, until one
  // stops the event. A portal's elements bubble to the document like the rest.
  const EVENTS = { click: 'onClick', mousedown: 'onMouseDown', mouseup: 'onMouseUp', mousemove: 'onMouseMove',
    keydown: 'onKeyDown', input: 'onChange', focusin: 'onFocus', focusout: 'onBlur' };
  for (const [type, prop] of Object.entries(EVENTS))
    document.addEventListener(type, (event) => {
      for (const node of event.composedPath()) {
        if (event.cancelBubble) break;
        node[propsKey]?.[prop]?.call(node, event);
      }
    });

  // ------------------------------------------------------------------------------------------------- the page
  const C = {
    page: css('page', 'font: 14px/1.5 system-ui, sans-serif; color: #1f2328; margin: 0; padding: 24px;'),
    header: css('header', 'display: flex; align-items: center; gap: 12px;'),
    iconButton: css('iconButton', 'display: inline-grid; place-items: center; width: 36px; height: 36px; border: 0; border-radius: 50%; background: #1565c0; color: #fff; cursor: pointer;'),
    backdrop: css('backdrop', 'position: fixed; inset: 0; background: rgba(0,0,0,.4);'),
    modal: css('modal', 'position: fixed; inset: 0; display: grid; place-items: center;'),
    paper: css('paper', 'position: relative; width: 640px; max-height: 90vh; overflow: auto; background: #fff; border-radius: 8px; box-shadow: 0 8px 24px rgba(0,0,0,.3); outline: 0;'),
    dialogTitle: css('dialogTitle', 'padding: 16px 24px 0; font-size: 18px; font-weight: 600;'),
    dialogBody: css('dialogBody', 'padding: 16px 24px; display: grid; gap: 12px;'),
    dialogFooter: css('dialogFooter', 'display: flex; justify-content: flex-end; gap: 8px; padding: 8px 24px 16px;'),
    label: css('label', 'font-size: 12px; color: #57606a;'),
    input: css('input', 'height: 32px; padding: 0 8px; border: 1px solid #d0d7de; border-radius: 4px; font: inherit;'),
    row: css('row', 'display: flex; align-items: center; gap: 8px;'),
    summary: css('summary', 'font-weight: 600;'),
    linkButton: css('linkButton', 'border: 0; background: none; color: #1565c0; cursor: pointer; font: inherit; padding: 0;'),
    primary: css('primary', 'height: 34px; padding: 0 16px; border: 0; border-radius: 4px; background: #1565c0; color: #fff; font: inherit; cursor: pointer;'),
    alert: css('alert', 'margin-top: 16px; padding: 8px 12px; border-radius: 4px; background: #e8f5e9;'),
    field: css('field', 'display: grid; gap: 4px; position: relative;'),
  };

  // A dialog as a portal at the end of the body. Labelled, it is named by its title, as MUI's Dialog is with a
  // DialogTitle; else it has no name, as a hand-made modal often has. Hiding the others is what MUI's modal manager does.
  function dialog({ title, labelled, hideOthers, body, footer }) {
    const titleId = labelled ? reactId() : null;
    const paper = h('div', { class: C.paper, role: 'dialog', 'aria-modal': 'true', 'aria-labelledby': titleId, tabindex: '-1' },
      h('div', { class: C.dialogTitle, id: titleId }, title),
      h('div', { class: C.dialogBody }, body),
      footer ? h('div', { class: C.dialogFooter }, footer) : null);
    const root = h('div', { class: C.modal, role: 'presentation' }, h('div', { class: C.backdrop, 'aria-hidden': 'true' }), paper);
    const hidden = hideOthers
      ? [...document.body.children].filter((child) => !child.hasAttribute('aria-hidden') && child.tagName !== 'SCRIPT')
      : [];
    for (const other of hidden) other.setAttribute('aria-hidden', 'true');
    document.body.append(root);
    paper.focus();
    return {
      root, paper,
      close() {
        root.remove();
        for (const other of hidden) other.removeAttribute('aria-hidden');
      },
    };
  }

  // A portal of its own, as libraries put a menu, placed under the element it belongs to.
  function popover(anchor, content, className) {
    const box = anchor.getBoundingClientRect();
    const portal = h('div', { class: className, style: `position: fixed; z-index: 1300; left: ${box.left}px; top: ${box.bottom + 2}px; min-width: ${box.width}px;` }, content);
    document.body.append(portal);
    return portal;
  }

  const SHIPPING = [
    { label: 'Standard', days: '5-7 days', price: '4.90' },
    { label: 'Express', days: '2 days', price: '12.00' },
    { label: 'Overnight', days: 'Next day', price: '24.50' },
    { label: 'Pickup', days: 'Today', price: '0.00' },
  ];
  const labels = SHIPPING.map((option) => option.label);

  // The modal flow: the opener with only an id, the order dialog with its customer field and the choice made so far,
  // and the dialog over it where the case lets the person choose.
  function orderPage(kase) {
    document.body.className = C.page;
    const root = document.getElementById('root');
    let shipping = kase.initial ?? 'Standard';
    root.append(h('header', { class: C.header }, h('h1', {}, 'Orders'),
      h('button', { id: 'create-order-redirect-button', class: C.iconButton, type: 'button', onClick: openOrder }, icon(ICONS.plus))));

    function openOrder() {
      const customer = h('input', { id: reactId(), name: 'customer', class: C.input, autocomplete: 'off' });
      const summary = h('span', { class: C.summary }, shipping);
      const order = dialog({
        title: 'New order', labelled: kase.labelled, hideOthers: kase.hideOthers,
        body: [
          h('div', { class: C.field }, h('label', { class: C.label, for: customer.id }, 'Customer'), customer),
          h('div', { class: C.row }, h('span', {}, `${kase.field ?? 'Shipping'}:`), summary,
            h('button', { id: 'change-shipping-button', class: C.linkButton, type: 'button', onClick: openChoices }, 'Change')),
        ],
        footer: h('button', { class: C.primary, type: 'button', onClick: create }, 'Create order'),
      });

      function openChoices() {
        let chosen = shipping;
        const confirm = () => {
          shipping = chosen;
          summary.textContent = chosen;
          choices.close();
        };
        const choices = dialog({
          title: kase.field ?? 'Shipping', labelled: kase.labelled, hideOthers: kase.hideOthers,
          body: kase.mount({ value: shipping, choose: (value) => { chosen = value; if (kase.closeOnChoice) confirm(); } }),
          footer: kase.closeOnChoice ? null : h('button', { class: C.primary, type: 'button', onClick: confirm }, 'Confirm'),
        });
      }

      function create() {
        order.close();
        root.append(h('div', { role: 'alert', class: C.alert }, `Order created for ${customer.value}: ${shipping}`));
      }
    }
  }

  // -------------------------------------------------------------------------------------------- the cases
  const CASES = {};

  // Option cards: nested divs and spans, an icon, the time as a badge, the price split in two; the app's click handler
  // is on the card, which alone has the hand cursor. Choosing closes the dialog.
  CASES.cards = {
    closeOnChoice: true,
    mount: ({ value, choose }) => {
      const grid = css('cardGrid', 'display: grid; grid-template-columns: repeat(2, 1fr); gap: 12px;');
      const card = css('card', 'border: 1px solid #d0d7de; border-radius: 8px; padding: 12px; cursor: pointer;');
      const selected = css('cardSelected', 'border-color: #1565c0; box-shadow: 0 0 0 1px #1565c0;');
      const head = css('cardHead', 'display: flex; align-items: center; gap: 8px;');
      const title = css('cardTitle', 'font-weight: 600;');
      const badge = css('badge', 'display: inline-block; margin-top: 6px; padding: 0 8px; border-radius: 10px; background: #eef2f7; font-size: 12px;');
      const price = css('price', 'margin-top: 6px; color: #57606a;');
      return h('div', { class: grid }, SHIPPING.map((option) => h('div', {
        class: `${card}${option.label === value ? ` ${selected}` : ''}`, onClick: () => choose(option.label),
      },
      h('div', { class: head }, icon(ICONS.truck), h('span', { class: title }, option.label)),
      h('span', { class: badge }, option.days),
      h('div', { class: price }, h('span', {}, '$'), h('span', {}, option.price)))));
    },
  };

  // Tiles as cards, without even the hand cursor: only the app's handler on the tile tells it is clickable.
  CASES.tiles = {
    mount: ({ value, choose }) => {
      const list = css('tileList', 'display: flex; gap: 12px;');
      const tile = css('tile', 'flex: 1; border: 1px solid #d0d7de; border-radius: 8px; padding: 10px;');
      const on = css('tileOn', 'background: #e3f2fd;');
      const inner = css('tileInner', 'display: flex; flex-direction: column; align-items: center; gap: 4px;');
      const tiles = SHIPPING.map((option) => h('div', {
        class: `${tile}${option.label === value ? ` ${on}` : ''}`,
        onClick: (event) => {
          for (const each of tiles) each.classList.toggle(on, each === event.currentTarget);
          choose(option.label);
        },
      }, h('div', { class: inner }, icon(ICONS.truck), h('div', {}, h('span', {}, option.label)), h('small', {}, option.days))));
      // The handler reads the tile it was called on, which a delegating listener passes as `this`.
      tiles.forEach((element) => {
        const handler = element[propsKey].onClick;
        element[propsKey].onClick = function (event) { handler({ currentTarget: this, nativeEvent: event }); };
      });
      return h('div', { class: list }, tiles);
    },
  };

  // A react-slick carousel: each option wrapped in two divs, the first and last slides cloned on both sides for an
  // endless loop, the clones hidden from assistive technology.
  CASES.carousel = {
    mount: ({ value, choose }) => {
      rule('.slick-slider{position:relative;display:flex;align-items:center;gap:4px}.slick-list{overflow:hidden;width:540px}' +
        '.slick-track{display:flex;transition:transform .2s}.slick-slide{flex:none;width:180px}.slick-arrow{flex:none}');
      const tile = css('optionTile', 'box-sizing: border-box; margin: 0 6px; padding: 12px; border: 1px solid #d0d7de; border-radius: 8px; cursor: pointer;');
      const on = css('optionTileOn', 'border-color: #1565c0;');
      const make = (option) => h('div', { class: `${tile}${option.label === value ? ` ${on}` : ''}`, tabindex: '-1',
        style: 'width: 100%; display: inline-block;', onClick: () => choose(option.label) },
      h('div', {}, h('div', {}, h('strong', {}, option.label))), h('div', {}, option.days));
      const slide = (option, index, cloned) => h('div', {
        'data-index': index, tabindex: '-1', class: `slick-slide${cloned ? ' slick-cloned' : ' slick-active'}`,
        'aria-hidden': cloned ? 'true' : 'false', style: 'outline: none;',
      }, h('div', {}, make(option)));
      const count = SHIPPING.length;
      const slides = [
        ...SHIPPING.slice(-3).map((option, index) => slide(option, index - 3, true)),
        ...SHIPPING.map((option, index) => slide(option, index, false)),
        ...SHIPPING.slice(0, 3).map((option, index) => slide(option, count + index, true)),
      ];
      let at = 0;
      const track = h('div', { class: 'slick-track' }, slides);
      const move = (by) => { at = (at + by + count) % count; track.style.transform = `translate3d(${-(at + 3) * 180}px, 0, 0)`; };
      move(0);
      return h('div', { class: 'slick-slider slick-initialized', dir: 'ltr' },
        h('button', { type: 'button', class: 'slick-arrow slick-prev', onClick: () => move(-1) }, 'Previous'),
        h('div', { class: 'slick-list' }, track),
        h('button', { type: 'button', class: 'slick-arrow slick-next', onClick: () => move(1) }, 'Next'));
    },
  };

  // MUI's Select before it became a combobox: a div with role button that opens a listbox in a popover. Its label's id
  // is generated (mui-123); MUI's dialogs are named by their title and hide the rest of the page while open.
  CASES['mui-select'] = {
    labelled: true, hideOthers: true,
    mount: ({ value, choose }) => {
      rule('.MuiInputBase-root{position:relative;display:flex;align-items:center;border-bottom:1px solid #888;width:260px}' +
        '.MuiSelect-select{flex:1;padding:6px 24px 6px 0;cursor:pointer;user-select:none}.MuiSelect-nativeInput{position:absolute;opacity:0;pointer-events:none;bottom:0;left:0;width:100%}' +
        '.MuiSelect-icon{position:absolute;right:0;pointer-events:none}.MuiPaper-root{background:#fff;box-shadow:0 4px 12px rgba(0,0,0,.25);border-radius:4px}' +
        '.MuiList-root{list-style:none;margin:0;padding:8px 0;outline:0}.MuiMenuItem-root{position:relative;padding:6px 16px;cursor:pointer}' +
        '.MuiMenuItem-root:hover{background:#f5f5f5}.MuiTouchRipple-root{position:absolute;inset:0;pointer-events:none;overflow:hidden}');
      const labelId = `mui-${number(9)}`;
      const display = h('div', { class: 'MuiSelect-root MuiSelect-select MuiSelect-outlined MuiInputBase-input', tabindex: '0',
        role: 'button', 'aria-haspopup': 'listbox', id: 'mui-component-select-shipping', 'aria-labelledby': `${labelId} mui-component-select-shipping`,
        onMouseDown: (event) => { event.preventDefault(); open(); } }, value);
      const input = h('input', { name: 'shipping', 'aria-hidden': 'true', tabindex: '-1', class: 'MuiSelect-nativeInput', value: value.toLowerCase() });
      let menu = null;
      function open() {
        display.setAttribute('aria-expanded', 'true');
        const list = h('ul', { class: 'MuiList-root MuiMenu-list', role: 'listbox', tabindex: '-1', 'aria-labelledby': labelId },
          labels.map((label) => h('li', { class: 'MuiButtonBase-root MuiMenuItem-root', tabindex: label === display.textContent ? '0' : '-1',
            role: 'option', 'aria-selected': String(label === display.textContent), 'data-value': label.toLowerCase(),
            onClick: () => { display.textContent = label; input.value = label.toLowerCase(); close(); choose(label); } },
          label, h('span', { class: 'MuiTouchRipple-root' }))));
        menu = popover(display, h('div', { class: 'MuiPaper-root MuiMenu-paper MuiPopover-paper' }, list), 'MuiPopover-root MuiMenu-root');
        menu.id = 'menu-shipping';
        menu.setAttribute('role', 'presentation');
      }
      function close() {
        menu?.remove();
        display.removeAttribute('aria-expanded');
      }
      return h('div', { class: 'MuiFormControl-root' },
        h('label', { class: 'MuiFormLabel-root MuiInputLabel-root', id: labelId, 'data-shrink': 'true' }, 'Shipping'),
        h('div', { class: 'MuiInputBase-root MuiOutlinedInput-root MuiInputBase-formControl' }, display, input, icon(ICONS.down, 'MuiSvgIcon-root MuiSelect-icon')));
    },
  };

  // react-select. Version 5: an input with role combobox in the control, which opens on mousedown, and a menu with role
  // listbox, here in a portal (menuPortalTarget), its options ids from the select's instance number. Version 3 and
  // older: no roles at all, the input a plain text box.
  function reactSelect({ value, choose, old }) {
    const n = number(2);
    const s = (label, style) => css(label, style);
    const container = s('container', 'position: relative; width: 280px;');
    const control = s('control', 'display: flex; align-items: center; min-height: 38px; border: 1px solid #ccc; border-radius: 4px; cursor: default;');
    const valueContainer = s('ValueContainer', 'flex: 1; display: grid; padding: 2px 8px;');
    const single = s('singleValue', 'grid-area: 1 / 1 / 2 / 3;');
    const inputBox = s('Input', 'grid-area: 1 / 1 / 2 / 3; visibility: visible;');
    const indicator = s('indicatorContainer', 'display: flex; padding: 8px; color: #ccc;');
    const menuClass = s('menu', 'background: #fff; border-radius: 4px; box-shadow: 0 0 0 1px rgba(0,0,0,.1), 0 4px 11px rgba(0,0,0,.1);');
    const menuList = s('MenuList', 'padding: 4px 0; max-height: 300px; overflow-y: auto;');
    const option = s('option', 'padding: 8px 12px; cursor: default;');
    const focused = s('option', 'padding: 8px 12px; cursor: default; background: #deebff;');
    const shown = h('div', { class: single }, value);
    const input = h('input', {
      id: `react-select-${n}-input`, autocapitalize: 'none', autocomplete: 'off', autocorrect: 'off', spellcheck: 'false',
      tabindex: '0', type: 'text', 'aria-autocomplete': 'list', 'aria-label': 'Shipping',
      ...(old ? {} : { role: 'combobox', 'aria-expanded': 'false', 'aria-haspopup': 'true' }),
      style: 'width: 2px; border: 0; outline: 0; padding: 0; background: transparent; color: transparent;',
    });
    let menu = null;
    const close = () => {
      menu?.remove();
      menu = null;
      if (!old) { input.setAttribute('aria-expanded', 'false'); input.removeAttribute('aria-controls'); }
    };
    const open = () => {
      if (menu) { close(); return; }
      input.focus();
      const items = labels.map((label, index) => h('div', {
        class: label === shown.textContent ? focused : option, id: `react-select-${n}-option-${index}`, tabindex: '-1',
        ...(old ? {} : { role: 'option', 'aria-disabled': 'false', 'aria-selected': String(label === shown.textContent) }),
        onClick: () => { shown.textContent = label; close(); choose(label); },
      }, label));
      const list = h('div', { class: menuList, ...(old ? {} : { role: 'listbox', id: `react-select-${n}-listbox` }) }, items);
      menu = popover(controlElement, h('div', { class: menuClass, onMouseDown: (event) => event.preventDefault() }, list), s('MenuPortal', 'z-index: 1400;'));
      if (!old) { input.setAttribute('aria-expanded', 'true'); input.setAttribute('aria-controls', list.id); }
    };
    const controlElement = h('div', { class: control, onMouseDown: (event) => { if (event.target !== input) event.preventDefault(); open(); } },
      h('div', { class: valueContainer }, shown, h('div', { class: inputBox, 'data-value': '' }, input)),
      h('div', { class: indicator, 'aria-hidden': 'true' }, icon(ICONS.down)));
    input.addEventListener('blur', () => setTimeout(close, 0));
    return h('div', { class: container },
      h('span', { id: `react-select-${n}-live-region`, class: s('a11yText', 'position: absolute; width: 1px; height: 1px; overflow: hidden;') }),
      controlElement, h('input', { name: 'shipping', type: 'hidden', value }));
  }
  CASES['react-select'] = { mount: (props) => reactSelect(props) };
  CASES['react-select-old'] = { mount: (props) => reactSelect({ ...props, old: true }) };

  // antd's Select: a read-only search input with role combobox under the item it shows, opened on mousedown; its
  // dropdown, a portal, holds a list for assistive technology (0 by 0 pixels, the active option and those beside it) and
  // the virtual list a person sees, whose options have no role, only a title.
  CASES['antd-select'] = {
    mount: ({ value, choose }) => {
      rule('.ant-select{position:relative;display:inline-block;width:260px;cursor:pointer}.ant-select-selector{display:flex;position:relative;height:32px;padding:0 11px;border:1px solid #d9d9d9;border-radius:6px;cursor:pointer}' +
        '.ant-select-selection-search{position:absolute;inset:0 11px}.ant-select-selection-search-input{width:100%;height:100%;border:0;padding:0;opacity:0;cursor:pointer}' +
        '.ant-select-selection-item{position:relative;flex:1;line-height:30px;user-select:none}.ant-select-arrow{position:absolute;right:11px;top:7px;pointer-events:none}' +
        '.ant-select-dropdown{background:#fff;border-radius:8px;box-shadow:0 6px 16px rgba(0,0,0,.08);padding:4px}.ant-select-item{padding:5px 12px;border-radius:4px;cursor:pointer}' +
        '.ant-select-item-option-active{background:rgba(0,0,0,.04)}.ant-select-item-option-selected{background:#e6f4ff;font-weight:600}');
      const n = number(1);
      const id = `rc_select_${n}`;
      const item = h('span', { class: 'ant-select-selection-item', title: value }, value);
      const input = h('input', { type: 'search', id, autocomplete: 'off', class: 'ant-select-selection-search-input', role: 'combobox',
        'aria-haspopup': 'listbox', 'aria-owns': `${id}_list`, 'aria-autocomplete': 'list', 'aria-controls': `${id}_list`,
        'aria-expanded': 'false', readonly: true, unselectable: 'on', value: '' });
      let dropdown = null;
      const close = () => { dropdown?.remove(); dropdown = null; input.setAttribute('aria-expanded', 'false'); };
      const open = () => {
        if (dropdown) { close(); return; }
        const active = labels.indexOf(item.textContent);
        const a11y = h('div', { role: 'listbox', id: `${id}_list`, style: 'height: 0; width: 0; overflow: hidden;' },
          labels.map((label, index) => Math.abs(index - active) <= 1
            ? h('div', { role: 'option', id: `${id}_list_${index}`, 'aria-selected': String(index === active), 'aria-label': label }, label.toLowerCase())
            : null));
        const options = labels.map((label, index) => h('div', {
          'aria-selected': String(index === active), title: label,
          class: `ant-select-item ant-select-item-option${index === active ? ' ant-select-item-option-active ant-select-item-option-selected' : ''}`,
          onClick: () => { item.textContent = label; item.title = label; close(); choose(label); },
        }, h('div', { class: 'ant-select-item-option-content' }, label),
        h('span', { class: 'ant-select-item-option-state', unselectable: 'on', 'aria-hidden': 'true' })));
        dropdown = popover(selector, h('div', { class: `ant-select-dropdown css-dev-only-do-not-override-${rand(6)} ant-select-dropdown-placement-bottomLeft`,
          onMouseDown: (event) => event.preventDefault() }, h('div', {}, a11y, h('div', { class: 'rc-virtual-list' },
          h('div', { class: 'rc-virtual-list-holder', style: 'max-height: 256px; overflow-y: auto;' },
            h('div', {}, h('div', { class: 'rc-virtual-list-holder-inner', style: 'display: flex; flex-direction: column;' }, options)))))), '');
        input.setAttribute('aria-expanded', 'true');
        input.focus();
      };
      const selector = h('div', { class: 'ant-select-selector', onMouseDown: (event) => { event.preventDefault(); open(); } },
        h('span', { class: 'ant-select-selection-search' }, input), item);
      return h('div', { class: `ant-select ant-select-single ant-select-show-arrow css-dev-only-do-not-override-${rand(6)}` }, selector,
        h('span', { class: 'ant-select-arrow', unselectable: 'on', 'aria-hidden': 'true' },
          h('span', { role: 'img', 'aria-label': 'down', class: 'anticon anticon-down ant-select-suffix' }, icon(ICONS.down))));
    },
  };

  // Headless UI's Listbox, styled with Tailwind: a button named by its label and the value it shows, which opens a list
  // of options, each a li with the label in a span and a check icon; every id is React's.
  CASES['headless-listbox'] = {
    mount: ({ value, choose }) => {
      rule('.relative{position:relative}.absolute{position:absolute}.block{display:block}.truncate{overflow:hidden;white-space:nowrap}' +
        '.w-72{width:18rem}.w-full{width:100%}.cursor-default{cursor:default}.rounded-lg{border-radius:8px}.border{border:1px solid #ccc}' +
        '.py-2{padding-top:8px;padding-bottom:8px}.pl-3{padding-left:12px}.pl-10{padding-left:40px}.pr-10{padding-right:40px}.text-left{text-align:left}' +
        '.bg-white{background:#fff}.mt-1{margin-top:4px}.shadow-lg{box-shadow:0 10px 15px rgba(0,0,0,.1)}.list-none{list-style:none;margin:0;padding:4px 0}' +
        '.inset-y-0{top:0;bottom:0}.left-0{left:0}.right-0{right:0}.flex{display:flex}.items-center{align-items:center}.pointer-events-none{pointer-events:none}' +
        '.select-none{user-select:none}.z-10{z-index:10}.font-medium{font-weight:500}.bg-amber-100{background:#fef3c7}');
      const ids = { label: `headlessui-listbox-label-${reactId()}`, button: `headlessui-listbox-button-${reactId()}`, options: `headlessui-listbox-options-${reactId()}` };
      const text = h('span', { class: 'block truncate' }, value);
      const button = h('button', { id: ids.button, type: 'button', class: 'relative w-full cursor-default rounded-lg border bg-white py-2 pl-3 pr-10 text-left',
        'aria-haspopup': 'listbox', 'aria-expanded': 'false', 'data-headlessui-state': '', 'aria-labelledby': `${ids.label} ${ids.button}`,
        onClick: () => (list ? close() : open()) },
      text, h('span', { class: 'pointer-events-none absolute inset-y-0 right-0 flex items-center' }, icon(ICONS.down)));
      const holder = h('div', { class: 'relative mt-1 w-72' }, button);
      let list = null;
      const close = () => {
        list?.remove();
        list = null;
        button.setAttribute('aria-expanded', 'false');
        button.removeAttribute('aria-controls');
      };
      const open = () => {
        list = h('ul', { id: ids.options, role: 'listbox', tabindex: '0', 'aria-orientation': 'vertical', 'aria-labelledby': ids.button,
          class: 'absolute z-10 mt-1 w-full list-none rounded-lg bg-white shadow-lg', 'data-headlessui-state': 'open' },
        labels.map((label) => h('li', { id: `headlessui-listbox-option-${reactId()}`, role: 'option', tabindex: '-1',
          'aria-selected': String(label === text.textContent), 'data-headlessui-state': label === text.textContent ? 'selected' : '',
          class: 'relative cursor-default select-none py-2 pl-10', onClick: () => { text.textContent = label; close(); choose(label); } },
        h('span', { class: `block truncate${label === text.textContent ? ' font-medium' : ''}` }, label),
        label === text.textContent ? h('span', { class: 'absolute inset-y-0 left-0 flex items-center pl-3' }, icon(ICONS.check)) : null)));
        holder.append(list);
        button.setAttribute('aria-expanded', 'true');
        button.setAttribute('aria-controls', ids.options);
      };
      return h('div', {}, h('label', { id: ids.label, class: 'block' }, 'Shipping'), holder);
    },
  };

  // Select2: the native select hidden, spans with roles in its place, a dropdown appended to the body with a search box
  // and the results, whose ids carry four random characters; it opens on mousedown and chooses on mouseup.
  CASES.select2 = {
    mount: ({ value, choose }) => {
      rule('.select2-container{display:inline-block;position:relative;width:280px}.select2-selection--single{display:block;height:28px;border:1px solid #aaa;border-radius:4px;cursor:pointer;user-select:none}' +
        '.select2-selection__rendered{display:block;padding-left:8px;line-height:28px}.select2-selection__arrow{position:absolute;top:1px;right:1px;width:20px;height:26px}' +
        '.select2-hidden-accessible{border:0!important;clip:rect(0 0 0 0)!important;height:1px!important;overflow:hidden!important;padding:0!important;position:absolute!important;width:1px!important;white-space:nowrap!important}' +
        '.select2-dropdown{display:block;background:#fff;border:1px solid #aaa;width:280px}.select2-search--dropdown{display:block;padding:4px}.select2-search__field{width:100%;box-sizing:border-box}' +
        '.select2-results__options{list-style:none;margin:0;padding:0}.select2-results__option{padding:6px;user-select:none}.select2-results__option--highlighted{background:#5897fb;color:#fff}');
      const select = h('select', { id: 'shipping', name: 'shipping', class: 'select2-hidden-accessible', tabindex: '-1', 'aria-hidden': 'true', 'data-select2-id': 'select2-data-shipping' },
        labels.map((label) => h('option', { value: label.toLowerCase(), ...(label === value ? { selected: true } : {}) }, label)));
      const rendered = h('span', { class: 'select2-selection__rendered', id: 'select2-shipping-container', role: 'textbox', 'aria-readonly': 'true', title: value }, value);
      const selection = h('span', { class: 'select2-selection select2-selection--single', role: 'combobox', 'aria-haspopup': 'true',
        'aria-expanded': 'false', tabindex: '0', 'aria-disabled': 'false', 'aria-labelledby': 'select2-shipping-container' },
      rendered, h('span', { class: 'select2-selection__arrow', role: 'presentation' }, h('b', { role: 'presentation' })));
      const seed = rand(4);
      let dropdown = null;
      const close = () => {
        dropdown?.remove();
        dropdown = null;
        selection.setAttribute('aria-expanded', 'false');
        selection.removeAttribute('aria-owns');
      };
      selection.addEventListener('mousedown', (event) => {
        event.preventDefault();
        if (dropdown) { close(); return; }
        const results = h('ul', { class: 'select2-results__options', role: 'listbox', id: 'select2-shipping-results', 'aria-expanded': 'true', 'aria-hidden': 'false' },
          labels.map((label) => h('li', { class: `select2-results__option select2-results__option--selectable${label === rendered.textContent ? ' select2-results__option--highlighted' : ''}`,
            id: `select2-shipping-result-${seed}-${label.toLowerCase()}`, role: 'option', 'aria-selected': String(label === rendered.textContent),
            'data-select2-id': `select2-data-${seed}-${label.toLowerCase()}` }, label)));
        results.addEventListener('mouseup', (up) => {
          const option = up.target.closest('.select2-results__option--selectable');
          if (!option) return;
          rendered.textContent = option.textContent;
          rendered.title = option.textContent;
          select.value = option.textContent.toLowerCase();
          close();
          choose(option.textContent);
        });
        const box = selection.getBoundingClientRect();
        dropdown = h('span', { class: 'select2-container select2-container--default select2-container--open', style: `position: fixed; z-index: 1400; left: ${box.left}px; top: ${box.bottom}px;` },
          h('span', { class: 'select2-dropdown select2-dropdown--below', dir: 'ltr' },
            h('span', { class: 'select2-search select2-search--dropdown' }, h('input', { class: 'select2-search__field', type: 'search', tabindex: '0', autocomplete: 'off', role: 'searchbox', 'aria-autocomplete': 'list', 'aria-controls': 'select2-shipping-results' })),
            h('span', { class: 'select2-results' }, results)));
        document.body.append(dropdown);
        selection.setAttribute('aria-expanded', 'true');
        selection.setAttribute('aria-owns', 'select2-shipping-results');
      });
      return h('div', {}, select, h('span', { class: 'select2 select2-container select2-container--default', dir: 'ltr', 'data-select2-id': `select2-data-${rand(3)}` },
        h('span', { class: 'selection' }, selection), h('span', { class: 'dropdown-wrapper', 'aria-hidden': 'true' })));
    },
  };

  // Chosen: the native select hidden, an anchor without an address that shows the choice, and a drop that is always in
  // the page, clipped away while closed; a result is chosen on mouseup.
  CASES.chosen = {
    mount: ({ value, choose }) => {
      rule('.chosen-container{position:relative;display:inline-block;width:280px;user-select:none}.chosen-single{display:block;height:24px;padding-left:8px;border:1px solid #aaa;border-radius:5px;line-height:24px;color:#444;text-decoration:none}' +
        '.chosen-drop{position:absolute;top:100%;z-index:1010;width:100%;background:#fff;border:1px solid #aaa;clip:rect(0,0,0,0);clip-path:inset(100% 100%)}' +
        '.chosen-with-drop .chosen-drop{clip:auto;clip-path:none}.chosen-results{list-style:none;margin:0;padding:0}.chosen-results li.active-result{padding:5px 6px;cursor:pointer}.chosen-results li.highlighted{background:#3875d7;color:#fff}');
      const select = h('select', { id: 'shipping', name: 'shipping', style: 'display: none;' },
        labels.map((label) => h('option', { value: label.toLowerCase(), ...(label === value ? { selected: true } : {}) }, label)));
      const shown = h('span', {}, value);
      const results = h('ul', { class: 'chosen-results' }, labels.map((label, index) => h('li', {
        class: `active-result${label === value ? ' result-selected highlighted' : ''}`, 'data-option-array-index': index }, label)));
      const container = h('div', { class: 'chosen-container chosen-container-single', title: '', id: 'shipping_chosen' },
        h('a', { class: 'chosen-single' }, shown, h('div', {}, h('b', {}))),
        h('div', { class: 'chosen-drop' }, h('div', { class: 'chosen-search' }, h('input', { class: 'chosen-search-input', type: 'text', autocomplete: 'off' })), results));
      container.addEventListener('mousedown', (event) => {
        if (event.target.closest('.chosen-drop')) return;
        event.preventDefault();
        container.classList.toggle('chosen-with-drop');
        container.classList.toggle('chosen-container-active');
      });
      results.addEventListener('mouseup', (event) => {
        const result = event.target.closest('li.active-result');
        if (!result) return;
        shown.textContent = result.textContent;
        select.value = result.textContent.toLowerCase();
        for (const each of results.children) each.classList.toggle('result-selected', each === result);
        container.classList.remove('chosen-with-drop', 'chosen-container-active');
        choose(result.textContent);
      });
      return h('div', {}, select, container);
    },
  };

  // Bootstrap's dropdown: a toggle button that shows the choice, and a menu of anchors to "#", one with a badge.
  CASES['bootstrap-dropdown'] = {
    mount: ({ value, choose }) => {
      rule('.dropdown{position:relative;display:inline-block}.btn{display:inline-block;padding:6px 12px;border:1px solid #6c757d;border-radius:6px;background:#6c757d;color:#fff;cursor:pointer;font:inherit}' +
        '.dropdown-menu{position:absolute;z-index:1000;display:none;min-width:10rem;padding:8px 0;margin:2px 0 0;list-style:none;background:#fff;border:1px solid rgba(0,0,0,.15);border-radius:6px}' +
        '.dropdown-menu.show{display:block}.dropdown-item{display:block;width:100%;padding:4px 16px;color:#212529;text-decoration:none;white-space:nowrap;box-sizing:border-box}' +
        '.dropdown-item:hover{background:#e9ecef}.badge{display:inline-block;margin-left:6px;padding:2px 6px;font-size:.75em;border-radius:6px;background:#6c757d;color:#fff}');
      const toggle = h('button', { class: 'btn btn-secondary dropdown-toggle', type: 'button', 'data-bs-toggle': 'dropdown', 'aria-expanded': 'false' }, value);
      const menu = h('ul', { class: 'dropdown-menu' }, SHIPPING.map((option) => h('li', {},
        h('a', { class: 'dropdown-item', href: '#', onClick: (event) => {
          event.preventDefault();
          toggle.textContent = option.label;
          menu.classList.remove('show');
          toggle.setAttribute('aria-expanded', 'false');
          choose(option.label);
        } }, option.label, h('span', { class: 'badge text-bg-secondary' }, option.days)))));
      // Bootstrap's own listener on the document toggles every [data-bs-toggle=dropdown].
      document.addEventListener('click', (event) => {
        const own = event.target.closest?.('[data-bs-toggle=dropdown]') === toggle;
        if (!own && menu.contains(event.target)) return;
        const show = own && !menu.classList.contains('show');
        menu.classList.toggle('show', show);
        toggle.setAttribute('aria-expanded', String(show));
      });
      return h('div', { class: 'dropdown' }, toggle, menu);
    },
  };

  // A native select, and a text field with a datalist, in the dialog.
  CASES.native = {
    mount: ({ value, choose }) => {
      const select = h('select', { name: 'shipping', id: reactId(), class: C.input, onChange: () => choose(select.value) },
        labels.map((label) => h('option', { ...(label === value ? { selected: true } : {}) }, label)));
      const carrier = h('input', { name: 'carrier', list: 'carriers', class: C.input, autocomplete: 'off' });
      return [h('div', { class: C.field }, h('label', { class: C.label, for: select.id }, 'Shipping'), select),
        h('div', { class: C.field }, h('label', { class: C.label }, 'Carrier', carrier)),
        h('datalist', { id: 'carriers' }, ['Parcel Post', 'Road Freight', 'Air Courier'].map((name) => h('option', { value: name })))];
    },
  };

  // MUI's Autocomplete: an input with role combobox, its listbox in a popper portal, every id React's.
  CASES['mui-autocomplete'] = {
    labelled: true, hideOthers: true,
    mount: ({ value, choose }) => {
      rule('.MuiAutocomplete-root{width:280px}.MuiAutocomplete-inputRoot{position:relative;display:flex;border:1px solid #bbb;border-radius:4px;padding:4px 8px}' +
        '.MuiAutocomplete-input{flex:1;border:0;outline:0;font:inherit;height:24px}.MuiAutocomplete-paper{background:#fff;box-shadow:0 4px 12px rgba(0,0,0,.25);border-radius:4px}' +
        '.MuiAutocomplete-listbox{list-style:none;margin:0;padding:8px 0;max-height:40vh;overflow:auto}.MuiAutocomplete-option{padding:6px 16px;cursor:pointer}.MuiAutocomplete-option.Mui-focused{background:#f5f5f5}');
      const id = reactId();
      const input = h('input', { id, type: 'text', class: 'MuiInputBase-input MuiOutlinedInput-input MuiAutocomplete-input', role: 'combobox',
        'aria-autocomplete': 'list', 'aria-expanded': 'false', autocomplete: 'off', autocapitalize: 'none', spellcheck: 'false', value,
        onChange: () => render(), onBlur: () => close(), onMouseDown: () => setTimeout(render, 0) });
      let popper = null;
      const close = () => { popper?.remove(); popper = null; input.setAttribute('aria-expanded', 'false'); input.removeAttribute('aria-controls'); };
      const render = () => {
        close();
        const typed = input.value.toLowerCase();
        const matching = labels.filter((label) => label.toLowerCase().includes(typed) || labels.includes(input.value));
        const list = h('ul', { class: 'MuiAutocomplete-listbox', role: 'listbox', id: `${id}-listbox`, 'aria-labelledby': `${id}-label`,
          onMouseDown: (event) => event.preventDefault() },
        matching.map((label, index) => h('li', { tabindex: '-1', role: 'option', id: `${id}-option-${index}`, 'data-option-index': index,
          'aria-disabled': 'false', 'aria-selected': String(label === input.value), class: 'MuiAutocomplete-option',
          onClick: () => { input.value = label; close(); choose(label); } }, label)));
        popper = popover(input.parentElement, h('div', { class: 'MuiPaper-root MuiAutocomplete-paper' }, list), 'MuiPopper-root MuiAutocomplete-popper');
        popper.setAttribute('role', 'presentation');
        input.setAttribute('aria-expanded', 'true');
        input.setAttribute('aria-controls', list.id);
      };
      return h('div', { class: 'MuiAutocomplete-root MuiAutocomplete-hasPopupIcon' }, h('div', { class: 'MuiFormControl-root MuiTextField-root' },
        h('label', { class: 'MuiFormLabel-root MuiInputLabel-root', for: id, id: `${id}-label` }, 'Shipping'),
        h('div', { class: 'MuiInputBase-root MuiOutlinedInput-root MuiAutocomplete-inputRoot' }, input)));
    },
  };

  // Two dropdowns in one dialog whose options read alike. Both lists stay in the page, the closed one faded out and
  // ignoring the pointer, as a list with a fade transition stays.
  CASES['same-options'] = {
    field: 'Delivery',
    initial: 'Morning',
    mount: ({ choose }) => {
      const times = ['Morning', 'Afternoon', 'Evening'];
      const trigger = css('trigger', 'display: flex; justify-content: space-between; width: 220px; padding: 6px 10px; border: 1px solid #d0d7de; border-radius: 6px; background: #fff; cursor: pointer; font: inherit;');
      const list = css('timeList', 'position: absolute; top: 100%; left: 0; z-index: 5; width: 220px; margin: 2px 0 0; padding: 4px 0; list-style: none; background: #fff; box-shadow: 0 4px 12px rgba(0,0,0,.2); border-radius: 6px; transition: opacity .1s;');
      const closed = css('timeListClosed', 'opacity: 0; pointer-events: none;');
      const item = css('timeItem', 'padding: 6px 10px; cursor: pointer;');
      const chosen = { Pickup: 'Afternoon', Delivery: 'Morning' };
      const dropdown = (name) => {
        const labelId = reactId();
        const listId = reactId();
        const shown = h('span', {}, chosen[name]);
        const options = h('ul', { class: `${list} ${closed}`, role: 'listbox', id: listId, 'aria-labelledby': labelId },
          times.map((time) => h('li', { role: 'option', 'aria-selected': String(time === chosen[name]), class: item,
            onClick: () => {
              chosen[name] = time;
              shown.textContent = time;
              options.classList.add(closed);
              button.setAttribute('aria-expanded', 'false');
              choose(chosen.Delivery);
            } }, time)));
        const button = h('button', { type: 'button', class: trigger, 'aria-haspopup': 'listbox', 'aria-expanded': 'false', 'aria-controls': listId,
          'aria-labelledby': labelId, onClick: () => {
            const open = options.classList.contains(closed);
            options.classList.toggle(closed, !open);
            button.setAttribute('aria-expanded', String(open));
          } }, shown, icon(ICONS.down));
        return h('div', { class: C.field }, h('span', { class: C.label, id: labelId }, `${name} window`), button, options);
      };
      return [dropdown('Pickup'), dropdown('Delivery')];
    },
  };

  // A hand-made menu that opens when its trigger takes the focus and closes as soon as it loses it; an item is chosen
  // on mousedown, before the focus goes.
  CASES['blur-menu'] = {
    mount: ({ value, choose }) => {
      const trigger = css('menuTrigger', 'display: inline-flex; gap: 8px; padding: 6px 10px; border: 1px solid #d0d7de; border-radius: 6px; outline: 0;');
      const menu = css('menu', 'position: absolute; top: 100%; left: 0; z-index: 5; min-width: 200px; background: #fff; box-shadow: 0 4px 12px rgba(0,0,0,.2); border-radius: 6px; padding: 4px 0;');
      const item = css('menuItem', 'padding: 6px 12px;');
      const shown = h('span', {}, value);
      let open = null;
      const holder = h('div', { style: 'position: relative; display: inline-block;' });
      const button = h('div', { class: trigger, tabindex: '0',
        onFocus: () => {
          open = h('div', { class: menu }, labels.map((label) => h('div', { class: item,
            onMouseDown: () => { shown.textContent = label; choose(label); } }, label)));
          holder.append(open);
        },
        onBlur: () => { open?.remove(); open = null; } }, shown, icon(ICONS.down));
      holder.prepend(button);
      return h('div', { class: C.field }, h('span', { class: C.label }, 'Shipping'), holder);
    },
  };

  // A virtualised list of pickup points: it renders only the options that show, as the list scrolls.
  const PLACES = ['North', 'South', 'East', 'West', 'Central', 'Old', 'New', 'Upper', 'Lower', 'Little'].flatMap((first) =>
    ['Market', 'Station', 'Harbor', 'Park', 'Bridge', 'Garden', 'Square', 'Mill', 'Castle', 'Church', 'Forest', 'Lake'].map((second) => `${first} ${second}`));
  CASES['virtual-list'] = {
    field: 'Pickup point',
    initial: 'North Market',
    mount: ({ value, choose }) => {
      const row = 32;
      const item = css('virtualItem', 'position: absolute; left: 0; right: 0; height: 32px; padding: 0 12px; line-height: 32px; cursor: pointer; box-sizing: border-box;');
      const selected = css('virtualItemOn', 'background: #e3f2fd;');
      const inner = h('div', { style: `position: relative; height: ${PLACES.length * row}px;` });
      let current = value;
      const list = h('div', { role: 'listbox', 'aria-label': 'Pickup point', tabindex: '0', style: 'height: 224px; overflow-y: auto; border: 1px solid #d0d7de; border-radius: 6px;' }, inner);
      const render = () => {
        const first = Math.max(0, Math.floor(list.scrollTop / row) - 1);
        const last = Math.min(PLACES.length, Math.ceil((list.scrollTop + 224) / row) + 1);
        inner.replaceChildren(...PLACES.slice(first, last).map((place, index) => h('div', {
          role: 'option', 'aria-selected': String(place === current), 'aria-setsize': PLACES.length, 'aria-posinset': first + index + 1,
          class: `${item}${place === current ? ` ${selected}` : ''}`, style: `top: ${(first + index) * row}px;`,
          onClick: () => { current = place; render(); choose(place); } }, place)));
      };
      list.addEventListener('scroll', render);
      setTimeout(render, 0);
      return list;
    },
  };

  // A web component select like Shoelace's: the combobox and the listbox in its shadow root, each option a custom
  // element with its own shadow root around the label it slots.
  CASES['shadow-select'] = {
    mount: ({ value, choose }) => {
      if (!customElements.get('sl-option')) {
        customElements.define('sl-option', class extends HTMLElement {
          connectedCallback() {
            if (this.shadowRoot) return;
            this.setAttribute('role', 'option');
            this.setAttribute('aria-selected', 'false');
            const root = this.attachShadow({ mode: 'open' });
            root.innerHTML = '<style>.option{display:flex;align-items:center;gap:6px;padding:6px 12px;cursor:pointer;user-select:none}' +
              ':host([aria-selected=true]) .option{background:#e0f2fe}.option__check{width:14px}</style>' +
              '<div part="base" class="option"><span part="checked-icon" class="option__check"></span><slot part="label" class="option__label"></slot></div>';
          }
        });
        customElements.define('sl-select', class extends HTMLElement {
          connectedCallback() {
            if (this.shadowRoot) return;
            const root = this.attachShadow({ mode: 'open' });
            root.innerHTML = '<style>.form-control{display:grid;gap:4px;width:280px}.select{position:relative}' +
              '.select__combobox{display:flex;align-items:center;border:1px solid #d0d7de;border-radius:6px;padding:0 8px;cursor:pointer}' +
              '.select__display-input{flex:1;border:0;outline:0;height:30px;font:inherit;cursor:inherit;background:transparent}' +
              '.select__listbox{display:none;position:absolute;top:100%;left:0;right:0;z-index:5;background:#fff;box-shadow:0 4px 12px rgba(0,0,0,.2);border-radius:6px;padding:4px 0}' +
              '.select--open .select__listbox{display:block}</style>' +
              '<div part="form-control" class="form-control"><label id="label" part="form-control-label" class="form-control__label"></label>' +
              '<div class="select"><div part="combobox" class="select__combobox"><input part="display-input" class="select__display-input" type="text" readonly ' +
              'aria-controls="listbox" aria-expanded="false" aria-haspopup="listbox" aria-labelledby="label" role="combobox" tabindex="0">' +
              '<span part="expand-icon">&#9662;</span></div>' +
              '<div id="listbox" role="listbox" aria-expanded="false" aria-labelledby="label" part="listbox" class="select__listbox" tabindex="-1"><slot></slot></div></div></div>';
            root.getElementById('label').textContent = this.getAttribute('label');
            const box = root.querySelector('.select');
            const input = root.querySelector('input');
            const sync = () => {
              const selected = [...this.children].find((option) => option.getAttribute('value') === this.getAttribute('value'));
              input.value = selected?.textContent ?? '';
              for (const option of this.children) option.setAttribute('aria-selected', String(option === selected));
            };
            const toggle = (open) => {
              box.classList.toggle('select--open', open);
              input.setAttribute('aria-expanded', String(open));
            };
            root.querySelector('.select__combobox').addEventListener('mousedown', (event) => {
              event.preventDefault();
              toggle(!box.classList.contains('select--open'));
            });
            this.addEventListener('mouseup', (event) => {
              const option = event.composedPath().find((node) => node.tagName === 'SL-OPTION');
              if (!option) return;
              this.setAttribute('value', option.getAttribute('value'));
              sync();
              toggle(false);
              this.dispatchEvent(new CustomEvent('sl-change', { bubbles: true, composed: true }));
            });
            setTimeout(sync, 0);
          }
        });
      }
      const select = h('sl-select', { label: 'Shipping', value: value.toLowerCase() },
        labels.map((label) => h('sl-option', { value: label.toLowerCase() }, label)));
      select.addEventListener('sl-change', () => choose(labels.find((label) => label.toLowerCase() === select.getAttribute('value'))));
      return select;
    },
  };

  window.kit = { cases: Object.keys(CASES), start: (name) => orderPage(CASES[name]) };
})();
