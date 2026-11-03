// An imitation of demoqa.com's automation practice form, a public practice site: a React app with Bootstrap's grid and
// react-select 5. Each label sits in a column of its own, beside its field, tied to nothing. The selects are react-select's
// markup: emotion classes whose hash changes with each load and with the select's state (focused, open, disabled), the
// ids react-select numbers by instance (react-select-3-input), which shift when the page mounts another select before,
// the arrow hidden from assistive technology, a multi-select of subjects typed into to filter, whose choices show as chips
// with a "Remove ..." button. The hobbies are checkboxes numbered by id. Submit opens a Bootstrap modal that fades in and
// out; as on demoqa, its Close button throws and closes nothing, and Escape or the backdrop closes it. Click handlers are
// React's: props kept on each element (__reactProps$) and run from one listener on the document.
(() => {
  'use strict';
  const SVG = 'http://www.w3.org/2000/svg';
  const rand = (length = 6) => {
    let text = '';
    while (text.length < length) text += Math.random().toString(36).slice(2);
    return text.slice(0, length);
  };
  const propsKey = `__reactProps$${rand(11)}`;
  const sheet = document.head.appendChild(document.createElement('style'));
  const rule = (text) => sheet.append(`${text}\n`);
  // An emotion class: a hash that changes with each load, and the component's label.
  const css = (label, style) => {
    const name = `css-${rand(7)}${label ? `-${label}` : ''}`;
    rule(`.${name}{${style}}`);
    return name;
  };

  function h(tag, props, ...children) {
    const element = ['svg', 'path'].includes(tag) ? document.createElementNS(SVG, tag) : document.createElement(tag);
    const handlers = {};
    for (const [key, value] of Object.entries(props ?? {})) {
      if (value === undefined || value === null || value === false) continue;
      if (/^on[A-Z]/.test(key)) handlers[key] = value;
      else if (key === 'style') element.style.cssText = value;
      else if (key === 'value') element.value = value;
      else element.setAttribute(key, value === true ? '' : String(value));
    }
    if (Object.keys(handlers).length) element[propsKey] = handlers;
    for (const child of children.flat(Infinity))
      if (child !== undefined && child !== null && child !== false) element.append(child.nodeType ? child : String(child));
    return element;
  }
  const on = (element, prop, handler) => { element[propsKey] = { ...(element[propsKey] ?? {}), [prop]: handler }; };
  const EVENTS = { click: 'onClick', mousedown: 'onMouseDown', mousemove: 'onMouseMove', keydown: 'onKeyDown', input: 'onChange',
    focusin: 'onFocus', focusout: 'onBlur' };
  for (const [type, prop] of Object.entries(EVENTS))
    document.addEventListener(type, (event) => {
      for (const node of event.composedPath()) {
        if (event.cancelBubble) break;
        node[propsKey]?.[prop]?.call(node, event);
      }
    });

  const arrow = () => h('svg', { height: 20, width: 20, viewBox: '0 0 20 20', 'aria-hidden': 'true', focusable: 'false' },
    h('path', { d: 'M4.516 7.548c0.436-0.446 1.043-0.481 1.576 0l3.908 3.747 3.908-3.747c0.533-0.481 1.141-0.446 1.574 0 0.436 0.445 0.408 1.197 0 1.615-0.406 0.418-4.695 4.502-4.695 4.502-0.217 0.223-0.502 0.335-0.787 0.335s-0.57-0.112-0.789-0.335c0 0-4.287-4.084-4.695-4.502s-0.436-1.17 0-1.615z' }));
  const cross = () => h('svg', { height: 14, width: 14, viewBox: '0 0 20 20', 'aria-hidden': 'true', focusable: 'false' },
    h('path', { d: 'M14.348 14.849c-0.469 0.469-1.229 0.469-1.697 0l-2.651-3.030-2.651 3.029c-0.469 0.469-1.229 0.469-1.697 0-0.469-0.469-0.469-1.229 0-1.697l2.758-3.15-2.759-3.152c-0.469-0.469-0.469-1.228 0-1.697s1.228-0.469 1.697 0l2.652 3.031 2.651-3.031c0.469-0.469 1.228-0.469 1.697 0s0.469 1.229 0 1.697l-2.758 3.152 2.758 3.15c0.469 0.469 0.469 1.229 0 1.698z' }));

  // ------------------------------------------------------------------------------------------------ react-select
  let instances = 0;
  function reactSelect({ id, placeholder, options, multi, classNamePrefix, disabled, onChange }) {
    const n = ++instances;
    const prefix = (part) => (classNamePrefix ? `${classNamePrefix}__${part} ` : '');
    const C = () => ({
      container: css('container', 'position: relative; box-sizing: border-box;'),
      disabledContainer: css('container', 'position: relative; pointer-events: none;'),
      control: css('control', 'display: flex; flex-wrap: wrap; align-items: center; min-height: 38px; border: 1px solid #ccc; border-radius: 4px; background: #fff; cursor: default;'),
      focused: css('control', 'display: flex; flex-wrap: wrap; align-items: center; min-height: 38px; border: 1px solid #2684ff; box-shadow: 0 0 0 1px #2684ff; border-radius: 4px; background: #fff; cursor: default;'),
      disabledControl: css('control', 'display: flex; align-items: center; min-height: 38px; border: 1px solid #e6e6e6; border-radius: 4px; background: #f2f2f2;'),
      value: css('', 'display: grid; flex: 1; flex-wrap: wrap; align-items: center; padding: 2px 8px; overflow: hidden; position: relative;'),
      multiValueBox: css('', 'display: flex; flex: 1; flex-wrap: wrap; align-items: center; padding: 2px 8px; overflow: hidden;'),
      placeholder: css('placeholder', 'grid-area: 1 / 1 / 2 / 3; color: #808080; margin: 0 2px;'),
      single: css('singleValue', 'grid-area: 1 / 1 / 2 / 3; color: #333; margin: 0 2px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap;'),
      inputBox: css('', 'grid-area: 1 / 1 / 2 / 3; display: inline-grid; margin: 2px; padding: 2px 0; color: #333;'),
      indicators: css('', 'display: flex; align-items: center; align-self: stretch;'),
      separator: css('indicatorSeparator', 'align-self: stretch; width: 1px; margin: 8px 0; background: #ccc;'),
      indicator: css('indicatorContainer', 'display: flex; padding: 8px; color: #ccc; transition: color 150ms;'),
      openIndicator: css('indicatorContainer', 'display: flex; padding: 8px; color: #666;'),
      menu: css('menu', 'position: absolute; top: 100%; left: 0; right: 0; z-index: 1; margin: 8px 0; background: #fff; border-radius: 4px; box-shadow: 0 0 0 1px rgba(0,0,0,.1), 0 4px 11px rgba(0,0,0,.1);'),
      menuList: css('', 'max-height: 300px; overflow-y: auto; padding: 4px 0;'),
      option: css('option', 'padding: 8px 12px; cursor: default; background: transparent;'),
      focusedOption: css('option', 'padding: 8px 12px; cursor: default; background: #deebff;'),
      noOptions: css('', 'padding: 8px 12px; color: #999; text-align: center;'),
      chip: css('multiValue', 'display: flex; margin: 2px; background: #e6e6e6; border-radius: 2px; min-width: 0;'),
      chipLabel: css('', 'padding: 3px 3px 3px 6px; font-size: 85%; color: #333; border-radius: 2px;'),
      chipRemove: css('', 'display: flex; align-items: center; padding: 0 4px; border-radius: 2px;'),
      a11y: css('a11yText', 'position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); white-space: nowrap;'),
    });
    // Emotion's classes, hashed anew on each load; a state of its own has a class of its own.
    const classes = C();
    let values = [];
    let focused = false;
    let menu = null;
    let active = 0;
    let isDisabled = !!disabled;

    const live = h('span', { id: `react-select-${n}-live-region`, class: classes.a11y });
    const log = h('span', { 'aria-live': 'polite', 'aria-atomic': 'false', 'aria-relevant': 'additions text', role: 'log', class: classes.a11y });
    const input = h('input', {
      class: classNamePrefix ? `${classNamePrefix}__input` : '', autocapitalize: 'none', autocomplete: 'off', autocorrect: 'off',
      id: id === 'subjects' ? 'subjectsInput' : `react-select-${n}-input`, spellcheck: 'false', tabindex: '0',
      'aria-autocomplete': 'list', 'aria-expanded': 'false', 'aria-haspopup': 'true', role: 'combobox', 'aria-activedescendant': '',
      type: 'text', value: '',
      style: 'color: inherit; background: 0px center; opacity: 1; width: 100%; grid-area: 1 / 2; font: inherit; min-width: 2px; border: 0px; margin: 0px; outline: 0px; padding: 0px;',
    });
    const inputBox = h('div', { class: `${prefix('input-container')}${classes.inputBox}`, 'data-value': '' }, input);
    const valueBox = h('div', {});
    const indicators = h('div', { class: `${prefix('indicators')}${classes.indicators}` });
    const control = h('div', {});
    const container = h('div', { id: id === 'subjects' ? 'subjectsContainer' : id }, live, log, control);

    const filtered = () => options.filter((option) => !values.includes(option) &&
      option.toLowerCase().includes(input.value.toLowerCase()));

    // Rendered without moving the input, which would lose the focus.
    function render() {
      container.className = isDisabled ? classes.disabledContainer : classes.container;
      control.className = `${prefix('control')}${isDisabled ? classes.disabledControl : focused ? classes.focused : classes.control}`;
      if (isDisabled) control.setAttribute('aria-disabled', 'true'); else control.removeAttribute('aria-disabled');
      input.disabled = isDisabled;
      valueBox.className = `${prefix('value-container')}${multi ? `${classNamePrefix ? `${classNamePrefix}__value-container--is-multi ` : ''}${classes.multiValueBox}` : classes.value}`;
      for (const child of [...valueBox.children]) if (child !== inputBox) child.remove();
      const before = (node) => valueBox.insertBefore(node, inputBox);
      if (!inputBox.isConnected) valueBox.append(inputBox);
      if (multi) {
        for (const value of values)
          before(h('div', { class: `${prefix('multi-value')}${classes.chip}` },
            h('div', { class: `${prefix('multi-value__label')}${classes.chipLabel}` }, value),
            h('div', { role: 'button', class: `${prefix('multi-value__remove')}${classes.chipRemove}`, 'aria-label': `Remove ${value}`,
              onMouseDown: (event) => { event.preventDefault(); event.stopPropagation(); },
              onClick: (event) => { event.stopPropagation(); remove(value); } }, cross())));
      }
      const showPlaceholder = !input.value && (multi ? values.length === 0 : values.length === 0);
      if (showPlaceholder) {
        before(h('div', { class: `${prefix('placeholder')}${classes.placeholder}`, id: `react-select-${n}-placeholder` }, multi ? '' : placeholder));
        input.setAttribute('aria-describedby', `react-select-${n}-placeholder`);
      } else input.removeAttribute('aria-describedby');
      if (!multi && values.length && !input.value) before(h('div', { class: `${prefix('single-value')}${classes.single}` }, values[0]));
      indicators.replaceChildren();
      if (multi && values.length)
        indicators.append(h('div', { class: `${prefix('indicator')}${prefix('clear-indicator')}${classes.indicator}`, 'aria-hidden': 'true',
          onMouseDown: (event) => { event.preventDefault(); event.stopPropagation(); values = []; onChange?.(values); render(); } }, cross()));
      indicators.append(h('span', { class: `${prefix('indicator-separator')}${classes.separator}` }));
      if (!multi)
        indicators.append(h('div', { class: `${prefix('indicator')}${prefix('dropdown-indicator')}${menu ? classes.openIndicator : classes.indicator}`, 'aria-hidden': 'true',
          onMouseDown: (event) => {
            if (isDisabled) return;
            event.preventDefault();
            event.stopPropagation();
            if (menu) closeMenu(); else { input.focus(); openMenu(); }
          } }, arrow()));
      if (!valueBox.isConnected) control.append(valueBox, indicators);
      if (menu) renderMenu();
    }

    function renderMenu() {
      const list = filtered();
      if (active >= list.length) active = 0;
      const listbox = h('div', { class: classes.menuList, role: 'listbox', 'aria-multiselectable': String(!!multi), id: `react-select-${n}-listbox` },
        list.length ? list.map((option, index) => h('div', {
          class: `${prefix('option')}${index === active ? classes.focusedOption : classes.option}`, 'aria-disabled': 'false',
          id: `react-select-${n}-option-${options.indexOf(option)}`, tabindex: '-1', role: 'option', 'aria-selected': String(values.includes(option)),
          onMouseMove: () => { if (active !== index) { active = index; renderMenu(); } },
          onMouseDown: (event) => event.preventDefault(),
          onClick: () => choose(option),
        }, option)) : h('div', { class: classes.noOptions }, 'No options'));
      const box = h('div', { class: `${prefix('menu')}${classes.menu}` }, listbox);
      if (menu) menu.replaceWith(box); else container.append(box);
      menu = box;
      input.setAttribute('aria-expanded', 'true');
      input.setAttribute('aria-controls', listbox.id);
    }

    function openMenu() { if (!menu) { active = 0; renderMenu(); render(); } }
    function closeMenu() {
      menu?.remove();
      menu = null;
      input.setAttribute('aria-expanded', 'false');
      input.removeAttribute('aria-controls');
      render();
    }
    function choose(option) {
      values = multi ? [...values, option] : [option];
      input.value = '';
      live.textContent = `option ${option}, selected.`;
      onChange?.(values);
      closeMenu();
    }
    function remove(value) {
      values = values.filter((other) => other !== value);
      live.textContent = `option ${value}, deselected.`;
      onChange?.(values);
      render();
    }

    on(control, 'onMouseDown', (event) => {
      if (isDisabled) return;
      if (event.target !== input) event.preventDefault();
      if (!focused) { input.focus(); openMenu(); }
      else if (!menu) openMenu();
      else if (event.target !== input) closeMenu();
    });
    on(input, 'onFocus', () => { focused = true; render(); });
    on(input, 'onBlur', () => { focused = false; input.value = ''; if (menu) closeMenu(); else render(); });
    on(input, 'onChange', () => { if (!menu) { active = 0; renderMenu(); } else renderMenu(); render(); });
    on(input, 'onKeyDown', (event) => {
      if (event.key === 'Enter' && menu) {
        event.preventDefault();
        const list = filtered();
        if (list[active]) choose(list[active]);
      } else if (event.key === 'Escape' && menu) { closeMenu(); }
      else if (event.key === 'ArrowDown') { if (!menu) openMenu(); else { active = Math.min(active + 1, filtered().length - 1); renderMenu(); } }
      else if (event.key === 'Backspace' && !input.value && multi && values.length) remove(values[values.length - 1]);
    });
    render();
    return {
      element: container,
      enable(on) { isDisabled = !on; if (!on) { values = []; } render(); },
      reset(next) { options = next; values = []; render(); },
      get values() { return values; },
    };
  }

  // ------------------------------------------------------------------------------------------------ the page
  rule('body{margin:0;font:16px/1.5 -apple-system,"Segoe UI",Roboto,Arial,sans-serif;color:#212529;background:#fff}' +
    '.container{max-width:1140px;margin:0 auto;padding:0 15px}.row{display:flex;flex-wrap:wrap;margin:0 -15px}' +
    '.row>[class*=col]{box-sizing:border-box;padding:0 15px}.col-md-3{flex:0 0 25%;max-width:25%}.col-md-9{flex:0 0 75%;max-width:75%}' +
    '.col-md-4{flex:0 0 33.33%;max-width:33.33%}.col-md-1{flex:0 0 8.33%;max-width:8.33%}.col-md-2{flex:0 0 16.66%;max-width:16.66%}' +
    '.mt-2{margin-top:.5rem}.mt-4{margin-top:1.5rem}.align-items-center{align-items:center}.justify-content-end{justify-content:flex-end}' +
    '.form-control{display:block;width:100%;box-sizing:border-box;height:38px;padding:6px 12px;font:inherit;border:1px solid #ced4da;border-radius:4px}' +
    'textarea.form-control{height:auto}.form-label{margin:0}.col-form-label{padding-top:7px;padding-bottom:7px}' +
    '.form-check-inline{display:inline-flex;align-items:center;margin-right:.75rem}.form-check-input{margin:0 .3rem 0 0}' +
    '.form-check-label{cursor:pointer}.btn{display:inline-block;padding:6px 12px;font:inherit;border:1px solid transparent;border-radius:4px;cursor:pointer}' +
    '.btn-primary{color:#fff;background:#007bff}.text-center{text-align:center}.practice-form-wrapper{padding:16px 0}' +
    '.fade{transition:opacity .15s linear}.fade:not(.show){opacity:0}.modal-backdrop{position:fixed;inset:0;z-index:1040;background:#000}' +
    '.modal-backdrop.show{opacity:.5}.modal{position:fixed;inset:0;z-index:1050;display:none;overflow:auto;outline:0}' +
    '.modal-dialog{position:relative;width:auto;max-width:800px;margin:1.75rem auto;pointer-events:none}' +
    '.modal-content{position:relative;display:flex;flex-direction:column;background:#fff;border-radius:6px;pointer-events:auto}' +
    '.modal-header,.modal-footer{display:flex;align-items:center;padding:16px}.modal-footer{justify-content:flex-end}' +
    '.modal-title{margin:0;font-size:1.5rem}.modal-body{padding:16px}table{border-collapse:collapse;width:100%}td,th{border:1px solid #444;padding:4px 8px}' +
    'body.modal-open{overflow:hidden}');

  const STATES = { NCR: ['Delhi', 'Gurgaon', 'Noida'], 'Uttar Pradesh': ['Agra', 'Lucknow', 'Merrut'], Haryana: ['Karnal', 'Panipat'],
    Rajasthan: ['Jaipur', 'Jaiselmer'] };
  const SUBJECTS = ['Maths', 'Accounting', 'Arts', 'Social Studies', 'Physics', 'Chemistry', 'Computer Science', 'Commerce',
    'Economics', 'Civics', 'Hindi', 'English', 'History', 'Biology'];

  const label = (id, text) => h('div', { class: 'col-md-3 col-sm-12' }, h('label', { id, class: 'form-label col-form-label col-form-label-sm col' }, text));
  const text = (id, placeholder, extra = {}) => h('input', { required: extra.required, autocomplete: 'off', placeholder, id,
    class: ' mr-sm-2 form-control', type: 'text', ...extra.attributes });
  const check = (kind, name, index, value, caption) => h('div', { class: 'form-check form-check-inline' },
    h('input', { id: `${name}-${kind}-${index}`, class: 'form-check-input', type: kind, value, ...(kind === 'radio' ? { name: 'gender', required: true } : {}) }),
    h('label', { title: '', for: `${name}-${kind}-${index}`, class: 'form-check-label' }, caption));

  // Another page part mounts selects before the form on some loads, so the form's react-select ids shift.
  const elsewhere = h('div', { id: 'fixedban', style: 'display: none' });
  for (let count = Math.floor(Math.random() * 3); count > 0; count--)
    elsewhere.append(reactSelect({ id: `language-${count}`, placeholder: 'Language', options: ['English', 'Hindi'] }).element);
  const unused = reactSelect({ id: 'unused-select', placeholder: '', options: [] });
  elsewhere.append(unused.element);

  const subjects = reactSelect({ id: 'subjects', options: SUBJECTS, multi: true, classNamePrefix: 'subjects-auto-complete' });
  let city = null;
  const state = reactSelect({ id: 'state', placeholder: 'Select State', options: Object.keys(STATES),
    onChange: ([chosen]) => { city.reset(STATES[chosen] ?? []); city.enable(!!chosen); } });
  city = reactSelect({ id: 'city', placeholder: 'Select City', options: [], disabled: true });

  const form = h('form', { novalidate: '', id: 'userForm', class: '' },
    h('div', { id: 'userName-wrapper', class: 'mt-2 align-items-center row' }, label('userName-label', 'Name'),
      h('div', { class: 'col-md-4 col-sm-6' }, text('firstName', 'First Name', { required: true })), h('div', { class: 'col-md-1' }),
      h('div', { class: 'col-md-4 col-sm-6' }, text('lastName', 'Last Name', { required: true }))),
    h('div', { id: 'userEmail-wrapper', class: 'mt-2 align-items-center row' }, label('userEmail-label', 'Email'),
      h('div', { class: 'col-md-9 col-sm-12' }, h('input', { autocomplete: 'off', placeholder: 'name@example.com', id: 'userEmail',
        class: 'mr-sm-2 form-control', type: 'text' }))),
    h('div', { id: 'genterWrapper', class: 'mt-2 row' }, h('div', { class: 'col-md-3 col-sm-12' }, 'Gender'),
      h('div', { class: 'col-md-9 col-sm-12' }, check('radio', 'gender', 1, 'Male', 'Male'), check('radio', 'gender', 2, 'Female', 'Female'),
        check('radio', 'gender', 3, 'Other', 'Other'))),
    h('div', { id: 'userNumber-wrapper', class: 'mt-2 align-items-center row' },
      h('div', { class: 'col-md-3 col-sm-12' }, h('label', { id: 'userNumber-label', class: 'form-label col-form-label col-form-label-sm col' }, 'Mobile', h('small', {}, '(10 Digits)'))),
      h('div', { class: 'col-md-9 col-sm-12' }, text('userNumber', 'Mobile Number', { required: true, attributes: { pattern: '\\d*', minlength: '10', maxlength: '10' } }))),
    h('div', { id: 'dateOfBirth-wrapper', class: 'mt-2 align-items-center row' }, label('dateOfBirth-label', 'Date of Birth'),
      h('div', { id: 'dateOfBirth', class: 'col-md-9 col-sm-12' }, h('div', { class: 'react-datepicker-wrapper' },
        h('div', { class: 'react-datepicker__input-container' }, h('input', { id: 'dateOfBirthInput', class: 'form-control', type: 'text', value: '03 Nov 2026' }))))),
    h('div', { id: 'subjectsWrapper', class: 'mt-2 align-items-center row' }, label('subjects-label', 'Subjects'),
      h('div', { class: 'col-md-9 col-sm-12' }, subjects.element)),
    h('div', { id: 'hobbiesWrapper', class: 'mt-2 align-items-center row' },
      h('div', { class: 'col-md-3 col-sm-12' }, ' ', h('label', { id: 'subjects-label', class: 'form-label col-form-label col-form-label-sm col' }, 'Hobbies')),
      h('div', { class: 'col-md-9 col-sm-12' }, check('checkbox', 'hobbies', 1, '1', 'Sports'), check('checkbox', 'hobbies', 2, '2', 'Reading'),
        check('checkbox', 'hobbies', 3, '3', 'Music'))),
    h('div', { id: 'currentAddress-wrapper', class: 'mt-2 align-items-center row' }, label('currentAddress-label', 'Current Address'),
      h('div', { class: 'col-md-9 col-sm-12' }, h('textarea', { placeholder: 'Current Address', rows: 5, cols: 20, id: 'currentAddress', class: 'form-control' }))),
    h('div', { id: 'stateCity-wrapper', class: 'mt-2 align-items-center row' }, label('stateCity-label', 'State and City'),
      h('div', { class: 'col-md-4 col-sm-12' }, state.element), h('div', { class: 'col-md-4 col-sm-12' }, city.element)),
    h('div', { class: 'mt-4 justify-content-end row' }, h('div', { class: 'text-right col-md-2 col-sm-12' },
      h('button', { type: 'submit', id: 'submit', class: 'btn btn-primary' }, 'Submit'))));

  // A submitted form shows what was sent in a Bootstrap modal, which fades in. Its Close button throws, as demoqa's does
  // (findDOMNode is gone in React 19), so it closes nothing; Escape and the backdrop close it, fading out.
  function showModal() {
    const value = (id) => document.getElementById(id).value;
    const rows = [['Student Name', `${value('firstName')} ${value('lastName')}`], ['Student Email', value('userEmail')],
      ['Gender', document.querySelector('[name=gender]:checked')?.value ?? ''], ['Mobile', value('userNumber')],
      ['Subjects', subjects.values.join(', ')],
      ['Hobbies', [...document.querySelectorAll('[id^=hobbies-checkbox]:checked')].map((box) => box.labels[0].textContent).join(', ')],
      ['State and City', `${state.values[0] ?? ''} ${city.values[0] ?? ''}`]];
    const backdrop = h('div', { class: 'fade modal-backdrop' });
    const modal = h('div', { role: 'dialog', 'aria-modal': 'true', class: 'fade modal', tabindex: '-1', 'aria-labelledby': 'example-modal-sizes-title-lg',
      style: 'display: block; padding-left: 0px;' },
    h('div', { class: 'modal-dialog modal-lg' }, h('div', { class: 'modal-content' },
      h('div', { class: 'modal-header' }, h('div', { class: 'modal-title h4', id: 'example-modal-sizes-title-lg' }, 'Thanks for submitting the form')),
      h('div', { class: 'modal-body' }, h('div', { class: 'table-responsive' }, h('table', { class: 'table table-dark table-striped table-bordered table-hover' },
        h('thead', {}, h('tr', {}, h('th', {}, 'Label'), h('th', {}, 'Values'))),
        h('tbody', {}, rows.map(([name, shown]) => h('tr', {}, h('td', {}, name), h('td', {}, shown))))))),
      h('div', { class: 'modal-footer' }, h('button', { type: 'button', id: 'closeLargeModal', class: 'btn btn-primary',
        onClick: () => { throw new TypeError('Lr.findDOMNode is not a function'); } }, 'Close')))));
    const hide = () => {
      modal.classList.remove('show');
      backdrop.classList.remove('show');
      setTimeout(() => { modal.remove(); backdrop.remove(); document.body.classList.remove('modal-open'); }, 150);
    };
    on(modal, 'onKeyDown', (event) => { if (event.key === 'Escape') hide(); });
    on(modal, 'onClick', (event) => { if (event.target === modal) hide(); });
    document.body.classList.add('modal-open');
    document.body.append(backdrop, modal);
    modal.focus();
    requestAnimationFrame(() => requestAnimationFrame(() => { backdrop.classList.add('show'); modal.classList.add('show'); }));
  }
  form.addEventListener('submit', (event) => {
    event.preventDefault();
    form.classList.add('was-validated');
    const filled = ['firstName', 'lastName'].every((id) => document.getElementById(id).value) &&
      /^\d{10}$/.test(document.getElementById('userNumber').value) && !!document.querySelector('[name=gender]:checked');
    if (filled) showModal();
  });

  document.getElementById('root').append(elsewhere, h('div', { class: 'container' }, h('div', { class: 'practice-form-wrapper' },
    h('h1', { class: 'text-center' }, 'Practice Form'), h('h5', {}, 'Student Registration Form'), form)));
})();
