// An imitation of OrangeHRM's open source demo, a public practice site: a Vue app of custom oxd-* widgets under its own
// addresses (/web/index.php/...). Its selects are divs in a form group whose label is tied to nothing, the value they show
// in a focusable div, their list beside them in the group; its icon buttons have no words, only a Bootstrap icon whose
// glyph the icon font draws before them, which an accessible name then holds; its table is divs of rows and cells, with a
// checkbox in its header and in each row, each under the box drawn over it, a sort menu hidden in each column's header,
// and a trash and a pencil in each row;
// deleting asks in a dialog with no name. Its pages have several forms, each with a heading and a Save of its own, and a
// date input whose calendar opens in its group. Handlers are bound with addEventListener, as Vue's are: nothing in the
// page tells which element handles a click.
(() => {
  'use strict';
  const path = location.pathname;
  const sheet = document.head.appendChild(document.createElement('style'));
  sheet.textContent = `
    body{margin:0;font:14px/1.4 "Nunito Sans",Arial,sans-serif;color:#64728c;background:#f6f6f6}
    .oxd-layout{display:flex;min-height:100vh}.oxd-sidepanel{width:240px;background:#fff;flex:none}
    .oxd-navbar-nav{height:100%;display:flex;flex-direction:column}.oxd-sidepanel-body{flex:1;padding:12px;min-height:420px}
    .oxd-main-menu{list-style:none;margin:0;padding:0}.oxd-main-menu-item{display:flex;gap:8px;align-items:center;padding:10px 12px;border-radius:24px;color:#64728c;text-decoration:none}
    .oxd-main-menu-item.active{background:#ff7b1d;color:#fff}.oxd-layout-container{flex:1;display:flex;flex-direction:column}
    .oxd-topbar-header{display:flex;align-items:center;justify-content:space-between;height:60px;padding:0 24px;background:#fff}
    .oxd-topbar-header-breadcrumb{display:flex;gap:8px}.oxd-text--h6{margin:0;font-size:14px;font-weight:700;color:#64728c}
    .oxd-layout-context{padding:24px;flex:1}.oxd-sheet,.orangehrm-card-container,.orangehrm-paper-container{background:#fff;border-radius:16px;padding:16px 24px;margin-bottom:16px}
    .oxd-form-row{display:block;margin-bottom:8px}.oxd-grid-4,.oxd-grid-3,.oxd-grid-2{display:grid;gap:16px}.oxd-grid-4{grid-template-columns:repeat(4,1fr)}
    .oxd-grid-3{grid-template-columns:repeat(3,1fr)}.oxd-grid-2{grid-template-columns:repeat(2,1fr)}
    .oxd-input-group{display:flex;flex-direction:column;gap:4px}.oxd-label{font-size:12px;color:#64728c}
    .oxd-input,.oxd-textarea{height:38px;box-sizing:border-box;width:100%;padding:0 12px;border:1px solid #e8eaef;border-radius:8px;font:inherit}
    .oxd-textarea{height:72px;padding:8px 12px}.oxd-select-wrapper,.oxd-date-wrapper{position:relative}
    .oxd-select-text{display:flex;align-items:center;height:38px;padding:0 12px;border:1px solid #e8eaef;border-radius:8px;background:#fff;cursor:pointer}
    .oxd-select-text-input{flex:1;cursor:pointer;outline:0}.oxd-select-text--focus{border-color:#ff7b1d}
    .oxd-select-dropdown{position:absolute;top:42px;left:0;right:0;z-index:10;background:#fff;border-radius:8px;box-shadow:0 4px 12px rgba(0,0,0,.15)}
    .oxd-select-option{padding:8px 12px;cursor:pointer}.oxd-select-option:hover{background:#fff3ec}
    .oxd-button{height:38px;padding:0 20px;border-radius:20px;border:1px solid #ff7b1d;background:#fff;color:#ff7b1d;font:inherit;cursor:pointer}
    .oxd-button--secondary{background:#ff7b1d;color:#fff}.oxd-button--label-danger{background:#eb0910;border-color:#eb0910;color:#fff}
    .oxd-form-actions{display:flex;gap:8px;align-items:center;justify-content:flex-end;padding-top:8px}
    .oxd-icon-button{width:32px;height:32px;border:0;border-radius:50%;background:transparent;cursor:pointer;color:#64728c}
    .oxd-icon{font-style:normal;font-family:"bootstrap-icons",sans-serif}
    .bi-trash::before{content:"\\f5de"}.bi-pencil-fill::before{content:"\\f4c9"}.bi-plus::before{content:"\\f4fe"}
    .bi-question-lg::before{content:"\\f64e"}.bi-caret-down-fill::before{content:"\\f229"}.bi-caret-up-fill::before{content:"\\f235"}
    .bi-check::before{content:"\\f26e"}.bi-calendar::before{content:"\\f1e8"}.bi-chevron-left::before{content:"\\f284"}
    .bi-chevron-right::before{content:"\\f285"}.bi-upload::before{content:"\\f603"}.bi-sort-alpha-down::before{content:"\\f575"}
    .oxd-table{display:flex;flex-direction:column}.oxd-table-row{display:flex;align-items:center;padding:8px 0}
    .oxd-table-header-cell,.oxd-table-cell{flex:1;padding:0 8px}.oxd-table-header-cell:first-child,.oxd-table-cell:first-child{flex:0 0 40px}
    .oxd-table-card{background:#fff;border-radius:12px;margin:6px 0}.oxd-table-header-sort{display:inline-block;margin-left:4px}
    .oxd-checkbox-wrapper label{position:relative;display:inline-flex;cursor:pointer}
    .oxd-checkbox-wrapper input{position:absolute;inset:0;width:100%;height:100%;margin:0;opacity:0}
    .oxd-checkbox-input{position:relative;z-index:1;display:inline-grid;place-items:center;width:16px;height:16px;border:1px solid #64728c;border-radius:4px;color:transparent}
    input:checked+.oxd-checkbox-input{background:#ff7b1d;border-color:#ff7b1d;color:#fff}
    .oxd-switch-wrapper label{position:relative;display:inline-flex;cursor:pointer}.oxd-switch-wrapper input{position:absolute;inset:0;width:100%;height:100%;margin:0;opacity:0}
    .oxd-switch-input{position:relative;z-index:1;display:inline-block;width:36px;height:20px;border-radius:10px;background:#e8eaef}
    input:checked+.oxd-switch-input{background:#ff7b1d}
    .oxd-overlay{position:fixed;inset:0;z-index:1000;display:flex;align-items:center;justify-content:center;background:rgba(0,0,0,.4)}
    .oxd-overlay--hide{display:none}.oxd-dialog-sheet{position:relative;width:420px;background:#fff;border-radius:16px;padding:24px;transition:opacity .2s}
    .oxd-dialog-close-button{position:absolute;top:8px;right:12px;border:0;background:none;font-size:20px;cursor:pointer}
    .fade-leave-active{opacity:0}.oxd-date-input{position:relative;display:flex;align-items:center}.oxd-date-input-icon{position:absolute;right:12px;cursor:pointer}
    .oxd-date-input-calendar{position:absolute;top:42px;left:0;z-index:10;width:280px;background:#fff;border-radius:12px;box-shadow:0 4px 12px rgba(0,0,0,.2);padding:8px}
    .oxd-calendar-header{display:flex;align-items:center;justify-content:space-between}.oxd-calendar-selector{display:flex;gap:8px;list-style:none;margin:0;padding:0}
    .oxd-calendar-selector-month,.oxd-calendar-selector-year{position:relative;cursor:pointer}.oxd-calendar-selector p{margin:0}
    .oxd-calendar-dropdown{position:absolute;top:24px;left:0;z-index:11;max-height:200px;overflow:auto;list-style:none;margin:0;padding:4px;background:#fff;box-shadow:0 2px 8px rgba(0,0,0,.2)}
    .oxd-calendar-dropdown--option{padding:4px 8px;cursor:pointer}
    .oxd-calendar-week-grid,.oxd-calendar-dates-grid{display:grid;grid-template-columns:repeat(7,1fr);text-align:center}
    .oxd-calendar-date{padding:4px;border-radius:50%;cursor:pointer}.oxd-calendar-date.--selected{background:#ff7b1d;color:#fff}
    .oxd-date-input-links{display:flex;justify-content:space-between;padding-top:6px}.oxd-date-input-link{cursor:pointer;color:#ff7b1d}
    .orangehrm-action-header{display:flex;align-items:center;gap:12px}.oxd-file-input{display:none}
    .oxd-file-div{display:flex;align-items:center;gap:8px;height:38px;border:1px solid #e8eaef;border-radius:8px;padding:0 12px}
    .oxd-file-button{padding:2px 12px;border-radius:12px;background:#e8eaef;cursor:pointer}
    .oxd-tabs,.orangehrm-tabs{display:flex;gap:4px}.orangehrm-tabs-item{display:block;padding:8px 12px;color:#64728c;text-decoration:none}
    .orangehrm-tabs-item.--active{color:#ff7b1d}.oxd-toast{position:fixed;bottom:16px;left:16px;background:#fff;padding:12px;border-radius:8px}
    .orangehrm-buzz{padding:16px}.orangehrm-buzz-post-actions{display:flex;gap:12px}.orangehrm-buzz-post-actions svg{cursor:pointer}
    .orangehrm-like-animation path{fill:#eb0910}`;

  function h(tag, attributes, ...children) {
    const element = ['svg', 'path', 'g'].includes(tag) ? document.createElementNS('http://www.w3.org/2000/svg', tag) : document.createElement(tag);
    for (const [key, value] of Object.entries(attributes ?? {})) {
      if (value === undefined || value === null || value === false) continue;
      if (key.startsWith('on')) element.addEventListener(key.slice(2).toLowerCase(), value);
      else element.setAttribute(key, value === true ? '' : String(value));
    }
    for (const child of children.flat(Infinity))
      if (child !== undefined && child !== null && child !== false) element.append(child.nodeType ? child : String(child));
    return element;
  }
  const icon = (name, extra = '') => h('i', { class: `oxd-icon bi-${name}${extra ? ` ${extra}` : ''}` });
  const text = (tag, kind, value, extra = '') => h(tag, { class: `oxd-text oxd-text--${kind}${extra ? ` ${extra}` : ''}` }, value);
  const button = (label, kind, attributes = {}, iconName = null) =>
    h('button', { type: 'button', class: `oxd-button oxd-button--medium oxd-button--${kind}`, ...attributes },
      iconName ? icon(iconName, 'oxd-button-icon') : null, ` ${label} `);
  const iconButton = (name, attributes = {}, extra = '') =>
    h('button', { class: `oxd-icon-button${extra ? ` ${extra}` : ''}`, type: 'button', ...attributes }, icon(name));
  function toast(message) {
    const shown = h('div', { class: 'oxd-toast-container oxd-toast-container--bottom' },
      h('div', { class: 'oxd-toast oxd-toast--success oxd-toast-container--toast' },
        text('p', 'p', 'Success', 'oxd-text--toast-title'), text('p', 'p', message, 'oxd-text--toast-message')));
    document.body.append(shown);
    setTimeout(() => shown.remove(), 2500);
  }

  // A form group: its label, tied to nothing, above what it holds.
  const group = (label, content, required) => h('div', { class: 'oxd-input-group oxd-input-field-bottom-space' },
    h('div', { class: 'oxd-input-group__label-wrapper' }, h('label', { class: `oxd-label${required ? ' oxd-input-field-required' : ''}` }, label)),
    h('div', { class: '' }, content));
  const input = (attributes = {}) => h('input', { class: 'oxd-input oxd-input--active', ...attributes });

  // oxd's select: the value it shows in a focusable div, its list beside it in its wrapper, opened by a click.
  let openSelect = null;
  function select(options, value = '-- Select --') {
    const shown = h('div', { class: 'oxd-select-text-input', tabindex: '0' }, value);
    const caret = icon('caret-down-fill', 'oxd-select-text--arrow');
    const trigger = h('div', { class: 'oxd-select-text oxd-select-text--active' }, shown, h('div', { class: 'oxd-select-text--after' }, caret));
    const wrapper = h('div', { class: 'oxd-select-wrapper' }, trigger);
    let list = null;
    const close = () => {
      list?.remove();
      list = null;
      trigger.classList.replace('oxd-select-text--focus', 'oxd-select-text--active');
      caret.className = 'oxd-icon bi-caret-down-fill oxd-select-text--arrow';
      if (openSelect === close) openSelect = null;
    };
    trigger.addEventListener('click', (event) => {
      event.stopPropagation();
      if (list) { close(); return; }
      openSelect?.();
      list = h('div', { role: 'listbox', class: 'oxd-select-dropdown --positon-bottom', loading: 'false' },
        ['-- Select --', ...options].map((option) => h('div', { role: 'option', class: 'oxd-select-option',
          onClick: (chosen) => { chosen.stopPropagation(); shown.textContent = option; close(); } },
        option === '-- Select --' ? option : h('span', {}, option))));
      wrapper.append(list);
      trigger.classList.replace('oxd-select-text--active', 'oxd-select-text--focus');
      caret.className = 'oxd-icon bi-caret-up-fill oxd-select-text--arrow';
      openSelect = close;
    });
    return wrapper;
  }
  document.addEventListener('click', () => openSelect?.());

  // oxd's autocomplete: an input of no role whose hints, found as it is typed into, show in its wrapper a moment later.
  function autocomplete(names) {
    const field = h('input', { placeholder: 'Type for hints...' });
    const wrapper = h('div', { class: 'oxd-autocomplete-wrapper' }, h('div', { class: 'oxd-autocomplete-text-input oxd-autocomplete-text-input--active' },
      h('div', { class: 'oxd-autocomplete-text-input--before' }), field, h('div', { class: 'oxd-autocomplete-text-input--after' })));
    let list = null;
    let timer = null;
    const close = () => { list?.remove(); list = null; };
    field.addEventListener('input', () => {
      clearTimeout(timer);
      close();
      if (!field.value) return;
      list = h('div', { role: 'listbox', class: 'oxd-autocomplete-dropdown --positon-bottom' }, h('div', { role: 'option', class: 'oxd-autocomplete-option --disabled' }, 'Searching....'));
      wrapper.append(list);
      timer = setTimeout(() => {
        const found = names.filter((name) => name.toLowerCase().includes(field.value.toLowerCase()));
        list?.replaceChildren(...(found.length ? found : ['No Records Found']).map((name) => h('div', { role: 'option', class: 'oxd-autocomplete-option',
          onClick: (event) => { event.stopPropagation(); if (!found.length) return; field.value = name; close(); } }, h('span', {}, name))));
      }, 400);
    });
    document.addEventListener('click', close);
    return wrapper;
  }

  // oxd's date input: a calendar that opens in its wrapper, with a month and a year of its own lists, which sets the
  // input's value as yyyy-dd-mm and closes as a day is chosen.
  const MONTHS = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];
  function dateInput() {
    const field = input({ placeholder: 'yyyy-dd-mm' });
    const opener = icon('calendar', 'oxd-date-input-icon');
    const wrapper = h('div', { class: 'oxd-date-wrapper' }, h('div', { class: 'oxd-date-input' }, field, opener));
    let calendar = null;
    let shown = new Date(2026, 10, 1);
    const pad = (number) => String(number).padStart(2, '0');
    const close = () => { calendar?.remove(); calendar = null; };
    function render() {
      const days = new Date(shown.getFullYear(), shown.getMonth() + 1, 0).getDate();
      const offset = shown.getDay();
      const dropdown = (items, pick) => h('ul', { class: 'oxd-calendar-dropdown', role: 'menu' },
        items.map((item) => h('li', { class: 'oxd-calendar-dropdown--option', onClick: (event) => { event.stopPropagation(); pick(item); render(); } }, String(item))));
      const month = h('li', { class: 'oxd-calendar-selector-month' },
        h('div', { class: 'oxd-calendar-selector-month-selected', onClick: (event) => { event.stopPropagation();
          month.append(dropdown(MONTHS, (name) => { shown = new Date(shown.getFullYear(), MONTHS.indexOf(name), 1); })); } },
        text('p', 'p', MONTHS[shown.getMonth()]), icon('caret-down-fill', 'oxd-icon-button__icon')));
      const years = Array.from({ length: 61 }, (_, index) => 1970 + index);
      const year = h('li', { class: 'oxd-calendar-selector-year' },
        h('div', { class: 'oxd-calendar-selector-year-selected', onClick: (event) => { event.stopPropagation();
          year.append(dropdown(years, (number) => { shown = new Date(number, shown.getMonth(), 1); })); } },
        text('p', 'p', String(shown.getFullYear())), icon('caret-down-fill', 'oxd-icon-button__icon')));
      const next = calendar ?? h('div', { class: 'oxd-date-input-calendar' });
      next.replaceChildren(h('div', { class: 'oxd-calendar-wrapper' },
        h('div', { class: 'oxd-calendar-header' },
          iconButton('chevron-left', { onClick: (event) => { event.stopPropagation(); shown = new Date(shown.getFullYear(), shown.getMonth() - 1, 1); render(); } }),
          h('ul', { class: 'oxd-calendar-selector' }, month, year),
          iconButton('chevron-right', { onClick: (event) => { event.stopPropagation(); shown = new Date(shown.getFullYear(), shown.getMonth() + 1, 1); render(); } })),
        h('div', { class: 'oxd-calendar-week-grid' }, ['S', 'M', 'T', 'W', 'T', 'F', 'S'].map((day) => h('div', { class: 'oxd-calendar-day' }, day))),
        h('div', { class: 'oxd-calendar-dates-grid' }, Array.from({ length: days }, (_, index) => h('div', {
          class: `oxd-calendar-date-wrapper${index === 0 && offset ? ` --offset-${offset}` : ''}` },
        h('div', { class: 'oxd-calendar-date', onClick: (event) => {
          event.stopPropagation();
          field.value = `${shown.getFullYear()}-${pad(index + 1)}-${pad(shown.getMonth() + 1)}`;
          field.dispatchEvent(new Event('input', { bubbles: true }));
          close();
        } }, String(index + 1))))),
        h('div', {}, h('div', { class: 'oxd-date-input-links' },
          h('div', { class: 'oxd-date-input-link --today' }, 'Today'), h('div', { class: 'oxd-date-input-link --clear' }, 'Clear'),
          h('div', { class: 'oxd-date-input-link --close', onClick: (event) => { event.stopPropagation(); close(); } }, 'Close')))));
      if (!calendar) { calendar = next; wrapper.append(calendar); }
    }
    opener.addEventListener('click', (event) => { event.stopPropagation(); if (calendar) close(); else render(); });
    field.addEventListener('click', (event) => { event.stopPropagation(); if (!calendar) render(); });
    document.addEventListener('click', close);
    return wrapper;
  }

  // A checkbox drawn by the span after its hidden input, in a label of no text.
  const checkbox = (onChange) => h('div', { class: 'oxd-checkbox-wrapper' }, h('label', { class: '' },
    h('input', { type: 'checkbox', onChange }), h('span', { class: 'oxd-checkbox-input oxd-checkbox-input--active --label-right oxd-checkbox-input' },
      icon('check', 'oxd-checkbox-input-icon'))));

  // ------------------------------------------------------------------------------------------------ the layout
  const MENU = [['Admin', '/web/index.php/admin/viewSystemUsers'], ['PIM', '/web/index.php/pim/viewEmployeeList'],
    ['Leave', '/web/index.php/leave/viewLeaveList'], ['Time', '/web/index.php/time/viewEmployeeTimesheet'],
    ['My Info', '/web/index.php/pim/viewPersonalDetails/empNumber/7'], ['Dashboard', '/web/index.php/dashboard/index'],
    ['Buzz', '/web/index.php/buzz/viewBuzz']];
  const module = path.includes('/admin/') ? 'Admin' : path.includes('/buzz/') ? 'Buzz' : path.includes('/pim/') ? 'PIM' : 'Dashboard';
  const helpPanel = h('div', { class: 'oxd-help', style: 'display: none' }, 'Help is on its way.');
  const content = h('div', { class: 'oxd-layout-context' });
  document.getElementById('root').append(h('div', { class: 'oxd-layout orangehrm-upgrade-layout' },
    h('aside', { class: 'oxd-sidepanel' }, h('nav', { class: 'oxd-navbar-nav', role: 'navigation', 'aria-label': 'Sidepanel' },
      h('div', { class: 'oxd-sidepanel-header' }, h('a', { class: 'oxd-brand', href: '/web/index.php/dashboard/index' }, 'OrangeHRM')),
      h('div', { class: 'oxd-sidepanel-body' }, h('ul', { class: 'oxd-main-menu' }, MENU.map(([name, href]) =>
        h('li', { class: 'oxd-main-menu-item-wrapper' }, h('a', { class: `oxd-main-menu-item${name === module || (name === 'My Info' && path.includes('PersonalDetails')) ? ' active' : ''}`, href },
          h('svg', { width: 14, height: 14, viewBox: '0 0 14 14', 'aria-hidden': 'true' }, h('path', { d: 'M1 1h12v12H1z' })),
          h('span', { class: 'oxd-text oxd-text--span oxd-main-menu-item--name' }, name)))))))),
    h('div', { class: 'oxd-layout-container' },
      h('header', { class: 'oxd-topbar' }, h('div', { class: 'oxd-topbar-header' },
        h('div', { class: 'oxd-topbar-header-title' }, h('span', { class: 'oxd-topbar-header-breadcrumb' },
          text('h6', 'h6', module, 'oxd-topbar-header-breadcrumb-module'))),
        h('div', { class: 'oxd-topbar-body-nav' }, iconButton('question-lg', { title: 'Help',
          onClick: () => { helpPanel.style.display = helpPanel.style.display === 'none' ? 'block' : 'none'; } }), helpPanel))),
      content),
    h('div', { role: 'dialog', class: 'oxd-overlay oxd-overlay--flex oxd-overlay--flex-centered oxd-overlay--hide oxd-layout-overlay' })));

  // A dialog that asks before deleting, with no name but its title's text; it fades as it closes.
  function confirmDelete(onYes) {
    const close = () => { sheetElement.classList.add('fade-leave-active'); setTimeout(() => overlay.remove(), 200); };
    const sheetElement = h('div', { class: 'oxd-sheet oxd-sheet--rounded oxd-sheet--white oxd-dialog-sheet oxd-dialog-sheet--shadow oxd-dialog-sheet--gutters orangehrm-dialog-popup', role: 'document' },
      h('button', { class: 'oxd-dialog-close-button oxd-dialog-close-button-position', onClick: close }, '×'),
      h('div', { class: 'orangehrm-modal-header' }, text('p', 'p', 'Are you Sure?', 'oxd-text--card-title')),
      h('div', { class: 'orangehrm-text-center-align' }, text('p', 'p', 'The selected record will be permanently deleted. Are you sure you want to continue?', 'oxd-text--card-body')),
      h('div', { class: 'orangehrm-modal-footer' }, button('No, Cancel', 'ghost', { onClick: close }),
        button('Yes, Delete', 'label-danger', { onClick: () => { onYes(); close(); toast('Successfully Deleted'); } }, 'trash')));
    const overlay = h('div', { role: 'dialog', class: 'oxd-overlay oxd-overlay--flex oxd-overlay--flex-centered' },
      h('div', { class: 'oxd-dialog-container-default' }, h('div', { class: 'oxd-dialog-container-default--inner' }, sheetElement)));
    document.body.append(overlay);
  }

  // ------------------------------------------------------------------------------------------------ the pages
  function systemUsers() {
    const users = [['Admin', 'Admin', 'Leticia Amanda', 'Enabled'], ['adna.maric.49677', 'ESS', 'Adna Maric', 'Enabled'],
      ['Dayana.Wisoky', 'ESS', 'Dayana Wisoky', 'Enabled'], ['dtr69884', 'ESS', 'Dan Rose', 'Enabled'], ['manimarann', 'ESS', 'Manimaran N', 'Enabled'],
      ['Jobinsam@6742', 'ESS', 'Jobin Sam', 'Enabled'], ['ksenia.kuz751968', 'ESS', 'Ksenia Kuz', 'Disabled'], ['kmn25326', 'ESS', 'Joao Santos', 'Enabled'],
      ['nqw35599', 'ESS', 'Joao Santos', 'Enabled']];
    const boxes = [];
    const header = (name) => h('div', { class: 'oxd-table-header-cell oxd-padding-cell oxd-table-th', role: 'columnheader', style: 'flex: 1 1 0%;' }, name,
      h('div', { class: 'oxd-table-header-sort' }, icon('sort-alpha-down', 'oxd-icon-button__icon oxd-table-header-sort-icon'),
        h('div', { class: 'oxd-table-header-sort-dropdown', role: 'dropdown', style: 'display: none;' }, h('ul', { role: 'menu' },
          h('li', { class: 'oxd-table-header-sort-dropdown-item' }, icon('sort-alpha-down'), text('span', 'span', 'Ascending')),
          h('li', { class: 'oxd-table-header-sort-dropdown-item' }, icon('sort-alpha-up'), text('span', 'span', 'Descending'))))));
    const body = h('div', { class: 'oxd-table-body', role: 'rowgroup' });
    const count = text('span', 'span', '');
    const recount = () => { count.textContent = `(${body.children.length}) Records Found`; };
    for (const [username, role, employee, status] of users) {
      const box = checkbox();
      boxes.push(box.querySelector('input'));
      const card = h('div', { class: 'oxd-table-card' }, h('div', { class: 'oxd-table-row oxd-table-row--with-border', role: 'row' },
        h('div', { class: 'oxd-table-cell oxd-padding-cell', role: 'cell' }, h('div', { class: 'oxd-table-card-cell-checkbox' }, box)),
        ...[username, role, employee, status].map((value) => h('div', { class: 'oxd-table-cell oxd-padding-cell', role: 'cell', style: 'flex: 1 1 0%;' }, h('div', {}, value))),
        h('div', { class: 'oxd-table-cell oxd-padding-cell', role: 'cell', style: 'flex: 1 1 0%;' }, h('div', { class: 'oxd-table-cell-actions' },
          iconButton('trash', { onClick: () => confirmDelete(() => {
            // The list loads again, every checkbox cleared.
            card.remove();
            for (const each of document.querySelectorAll('.oxd-table input[type=checkbox]')) each.checked = false;
            recount();
          }) }, 'oxd-table-cell-action-space'),
          iconButton('pencil-fill', {}, 'oxd-table-cell-action-space')))));
      body.append(card);
    }
    recount();
    const all = checkbox((event) => { for (const each of document.querySelectorAll('.oxd-table-body input[type=checkbox]')) each.checked = event.target.checked; });
    const filterArea = h('div', { class: 'oxd-table-filter-area' }, h('form', { class: 'oxd-form', novalidate: '', onSubmit: (event) => {
      event.preventDefault();
      body.style.opacity = '0.5';
      setTimeout(() => { body.style.opacity = ''; recount(); }, 300);
    } },
    h('div', { class: 'oxd-form-row' }, h('div', { class: 'oxd-grid-4 orangehrm-full-width-grid' },
      h('div', { class: 'oxd-grid-item oxd-grid-item--gutters' }, group('Username', input())),
      h('div', { class: 'oxd-grid-item oxd-grid-item--gutters' }, group('User Role', select(['Admin', 'ESS']))),
      h('div', { class: 'oxd-grid-item oxd-grid-item--gutters' }, group('Employee Name', autocomplete(['Adna Maric', 'Dan Rose', 'Joao Santos', 'Leticia Amanda', 'Manimaran N']))),
      h('div', { class: 'oxd-grid-item oxd-grid-item--gutters' }, group('Status', select(['Enabled', 'Disabled']))))),
    h('hr', { class: 'oxd-divider', role: 'separator' }),
    h('div', { class: 'oxd-form-actions' }, button('Reset', 'ghost'), h('button', { type: 'submit', class: 'oxd-button oxd-button--medium oxd-button--secondary orangehrm-left-space' }, ' Search '))));
    const toggle = iconButton('caret-down-fill', { onClick: () => { filterArea.style.display = filterArea.style.display === 'none' ? '' : 'none'; } });
    content.append(h('div', { class: 'orangehrm-paper-container oxd-table-filter' },
      h('div', { class: 'oxd-table-filter-header' }, h('div', { class: 'oxd-table-filter-header-title' }, text('h5', 'h5', 'System Users', 'oxd-table-filter-title')),
        h('div', { class: 'oxd-table-filter-header-options' }, h('div', { class: '--toggle' }, toggle))),
      filterArea),
    h('div', { class: 'orangehrm-paper-container' },
      h('div', { class: 'orangehrm-header-container' }, button('Add', 'secondary', {}, 'plus')),
      h('div', { class: 'orangehrm-horizontal-padding' }, count),
      h('div', { class: 'orangehrm-container' }, h('div', { class: 'oxd-table', role: 'table' },
        h('div', { class: 'oxd-table-header', role: 'rowgroup' }, h('div', { class: 'oxd-table-row oxd-table-row--with-border', role: 'row' },
          h('div', { class: 'oxd-table-header-cell oxd-padding-cell oxd-table-th', role: 'columnheader' }, all),
          header('Username'), header('User Role'), header('Employee Name'), header('Status'),
          h('div', { class: 'oxd-table-header-cell oxd-padding-cell oxd-table-th', role: 'columnheader', style: 'flex: 1 1 0%;' }, 'Actions'))),
        body))));
  }

  // A section: its heading over a form of its own, with a Save of its own.
  const form = (rows, onSave, required) => h('form', { class: 'oxd-form', novalidate: '', onSubmit: (event) => { event.preventDefault(); onSave(); } },
    ...rows.map((row) => h('div', { class: 'oxd-form-row' }, h('div', { class: `oxd-grid-${row.length} orangehrm-full-width-grid` },
      row.map((cell) => h('div', { class: 'oxd-grid-item oxd-grid-item--gutters' }, cell))))),
    h('hr', { class: 'oxd-divider', role: 'separator' }),
    h('div', { class: 'oxd-form-actions' }, required ? text('p', 'p', ' * Required', 'orangehrm-form-hint') : null,
      h('button', { type: 'submit', class: 'oxd-button oxd-button--medium oxd-button--secondary orangehrm-left-space' }, ' Save ')));

  function personalDetails() {
    const saved = () => toast('Successfully Updated');
    const radio = (label, value) => h('div', { class: 'oxd-radio-wrapper' }, h('label', { class: '' }, label,
      h('input', { type: 'radio', value, name: 'gender' }), h('span', { class: 'oxd-radio-input oxd-radio-input--active --label-right oxd-radio-input' })));
    const contract = h('div', { class: 'oxd-form-row', style: 'display: none' }, h('div', { class: 'oxd-grid-3 orangehrm-full-width-grid' },
      h('div', { class: 'oxd-grid-item oxd-grid-item--gutters' }, group('Contract Start Date', dateInput())),
      h('div', { class: 'oxd-grid-item oxd-grid-item--gutters' }, group('Contract End Date', dateInput()))));
    const attachmentForm = h('div', { class: 'orangehrm-card-container', style: 'display: none' },
      text('h6', 'h6', 'Add Attachment', 'orangehrm-main-title'),
      form([[group('Select File', [h('input', { type: 'file', class: 'oxd-file-input' }),
        h('div', { class: 'oxd-file-div oxd-file-div--active', onClick: (event) => event.currentTarget.parentElement.querySelector('input[type=file]').click() },
          h('div', { class: 'oxd-file-button' }, 'Browse'), h('div', { class: 'oxd-file-input-div' }, 'No file selected'), icon('upload', 'oxd-file-input-icon'))], true)],
      [group('Comment', h('textarea', { class: 'oxd-textarea oxd-textarea--active oxd-textarea--resize-vertical', placeholder: 'Type comment here' }))]],
      () => { attachmentForm.style.display = 'none'; saved(); }, true));
    content.append(
      h('div', { role: 'tablist', class: 'orangehrm-tabs' }, ['Personal Details', 'Contact Details', 'Job'].map((name, index) =>
        h('div', { role: 'tab', class: 'orangehrm-tabs-wrapper' }, h('a', { class: `orangehrm-tabs-item${index === 0 ? ' --active' : ''}`,
          href: index === 0 ? '/web/index.php/pim/viewPersonalDetails/empNumber/7' : `/web/index.php/pim/${index === 1 ? 'contactDetails' : 'viewJobDetails'}/empNumber/7` }, name)))),
      h('div', { class: 'orangehrm-edit-employee-content' },
        h('div', { class: 'orangehrm-horizontal-padding orangehrm-vertical-padding' }, text('h6', 'h6', 'Personal Details', 'orangehrm-main-title'),
          h('hr', { class: 'oxd-divider', role: 'separator' }),
          form([[group('Employee Full Name', h('div', { class: 'oxd-grid-3' },
            input({ name: 'firstName', placeholder: 'First Name', class: 'oxd-input oxd-input--active orangehrm-firstname', value: 'Leticia' }),
            input({ name: 'middleName', placeholder: 'Middle Name', class: 'oxd-input oxd-input--active orangehrm-middlename' }),
            input({ name: 'lastName', placeholder: 'Last Name', class: 'oxd-input oxd-input--active orangehrm-lastname', value: 'Amanda' })), true)],
          [group('Employee Id', input()), group("Driver's License Number", input()), group('License Expiry Date', dateInput())],
          [group('Nationality', select(['American', 'Brazilian', 'Indian'])), group('Marital Status', select(['Single', 'Married', 'Other'], 'Single')),
            group('Gender', h('div', { class: 'oxd-grid-2' }, radio('Male', 1), radio('Female', 2)))]], saved, true)),
        h('div', { class: 'orangehrm-custom-fields' }, h('hr', { class: 'oxd-divider', role: 'separator' }),
          h('div', { class: 'orangehrm-card-container' }, text('h6', 'h6', 'Custom Fields', 'orangehrm-main-title'),
            h('hr', { class: 'oxd-divider', role: 'separator' }),
            form([[group('Blood Type', select(['A+', 'A-', 'B+', 'B-', 'O+', 'O-'], 'A+')), group('Test_Field', input({ options: '' }))]], saved))),
        h('div', { class: 'orangehrm-card-container' }, h('div', { class: 'oxd-form-row user-form-header' },
          text('p', 'p', 'Include Employment Contract Details', 'user-form-header-text'),
          h('div', { class: 'oxd-switch-wrapper' }, h('label', { class: '' },
            h('input', { type: 'checkbox', onChange: (event) => { contract.style.display = event.target.checked ? '' : 'none'; } }),
            h('span', { class: 'oxd-switch-input oxd-switch-input--active --label-right' })))), contract),
        h('div', { class: 'orangehrm-horizontal-padding' }, h('div', { class: 'orangehrm-action-header' },
          text('h6', 'h6', 'Assigned Dependents', 'orangehrm-main-title'), button('Add', 'text', {}, 'plus'))),
        h('div', { class: 'orangehrm-attachment' }, h('hr', { class: 'oxd-divider', role: 'separator' }),
          h('div', { class: 'orangehrm-horizontal-padding' }, h('div', { class: 'orangehrm-action-header' },
            text('h6', 'h6', 'Attachments', 'orangehrm-main-title'),
            button('Add', 'text', { onClick: () => { attachmentForm.style.display = ''; } }, 'plus'))),
          attachmentForm)));
  }

  // Posts of a feed, each with a heart of the same id as every other post's.
  function buzz() {
    const posts = [['Leticia Amanda', 'Welcome to the team, everyone!'], ['Joao Santos', 'The quarterly review is on Friday.'],
      ['Ksenia Kuz', 'Who is up for lunch at noon?']];
    content.append(h('div', { class: 'orangehrm-buzz-newsfeed-posts' }, posts.map(([author, said]) => {
      let likes = 0;
      const count = text('p', 'p', '0 Likes', 'orangehrm-buzz-stats-active');
      const heart = h('svg', { id: 'heart-svg', width: 24, height: 24, viewBox: '0 0 24 24' }, h('g', { id: 'Group' }, h('path', { id: 'heart', d: 'M12 21l-1-1C5 15 2 12 2 8a5 5 0 0110-1 5 5 0 0110 1c0 4-3 7-9 12z' })));
      heart.addEventListener('click', () => { heart.classList.toggle('orangehrm-like-animation'); likes = 1 - likes; count.textContent = `${likes} Likes`; });
      return h('div', {}, h('div', { class: 'oxd-sheet oxd-sheet--rounded oxd-sheet--white orangehrm-buzz' },
        h('div', { class: 'orangehrm-buzz-post-header' }, text('p', 'p', author, 'orangehrm-buzz-post-emp-name')),
        h('div', { class: 'orangehrm-buzz-post-body' }, text('p', 'p', said, 'orangehrm-buzz-post-body-text')),
        h('div', { class: 'orangehrm-buzz-post-footer' }, h('div', { class: 'orangehrm-buzz-post-actions' }, h('div', {}, heart)), count)));
    })));
  }

  if (path.includes('/admin/viewSystemUsers')) systemUsers();
  else if (path.includes('/pim/viewPersonalDetails')) personalDetails();
  else if (path.includes('/buzz/')) buzz();
  else content.append(h('div', { class: 'orangehrm-paper-container' }, text('p', 'p', 'Time at Work', 'oxd-text--card-title')));
})();
