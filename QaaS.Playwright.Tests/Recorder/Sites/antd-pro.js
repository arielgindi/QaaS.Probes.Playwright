// An imitation of Ant Design Pro's preview, a public demo of React and antd: a table of rules whose counts and statuses
// are random on each load, a Status select whose options sit in a portal that stays in the page once closed, hidden,
// each with the title "[object Object]" (the demo's own bug) and a hidden list for assistive technology beside them, a
// "New" button whose name starts with its icon's label ("plus New"), a modal with a date picker that keeps what is typed
// or clicked only once OK or Enter commits it, a settings list of switches (role=switch) that React renders a moment after
// the click, and an avatar whose menu opens on hover. Handlers are bound on the root, as React binds them: nothing in the
// page tells which element handles a click.
(() => {
  'use strict';
  const sheet = document.head.appendChild(document.createElement('style'));
  sheet.textContent = `
    body{margin:0;font:14px/1.5 -apple-system,"Segoe UI",Roboto,Arial,sans-serif;color:#000000e0;background:#f5f5f5}
    header{display:flex;justify-content:flex-end;align-items:center;height:48px;padding:0 24px;background:#fff}
    main{padding:16px 24px}.ant-card{background:#fff;border-radius:8px;padding:16px 24px;margin-bottom:16px}
    .ant-btn{height:32px;padding:4px 15px;border:1px solid #d9d9d9;border-radius:6px;background:#fff;cursor:pointer;font:inherit}
    .ant-btn-primary{background:#1677ff;border-color:#1677ff;color:#fff}.anticon{display:inline-block;width:12px;margin-right:6px}
    .ant-form-item{display:flex;gap:8px;align-items:center;margin-bottom:12px}.ant-form-item-label{width:120px}
    .ant-select{position:relative;width:200px;cursor:pointer}.ant-select-selector{display:flex;height:32px;align-items:center;padding:0 11px;border:1px solid #d9d9d9;border-radius:6px;background:#fff}
    .ant-select-selection-search-input{position:absolute;inset:0;opacity:0;cursor:pointer;width:100%}
    .ant-select-dropdown{position:absolute;z-index:1050;width:200px;background:#fff;border-radius:8px;box-shadow:0 6px 16px #00000014;padding:4px}
    .ant-select-dropdown-hidden,.ant-dropdown-hidden,.ant-picker-dropdown-hidden{display:none}
    .ant-select-item{padding:5px 12px;border-radius:4px;cursor:pointer}.ant-select-item:hover{background:#0000000a}
    table{border-collapse:collapse;width:100%}th,td{padding:8px;border-bottom:1px solid #f0f0f0;text-align:left}
    .ant-list{list-style:none;margin:0;padding:0}.ant-list-item{display:flex;justify-content:space-between;align-items:center;padding:8px 0;width:300px}
    .ant-switch{position:relative;width:44px;height:22px;border:0;border-radius:11px;background:#00000040;cursor:pointer}
    .ant-switch[aria-checked=true]{background:#1677ff}.ant-switch-handle{position:absolute;top:2px;left:2px;width:18px;height:18px;border-radius:50%;background:#fff}
    .ant-switch[aria-checked=true] .ant-switch-handle{left:24px}
    .ant-dropdown{position:absolute;z-index:1050;background:#fff;border-radius:8px;box-shadow:0 6px 16px #00000014;padding:4px;min-width:160px}
    .ant-dropdown-menu{list-style:none;margin:0;padding:0}.ant-dropdown-menu-item{padding:5px 12px;cursor:pointer}
    .ant-modal-wrap{position:fixed;inset:0;z-index:1000;display:flex;align-items:flex-start;justify-content:center;padding-top:80px;background:#00000073}
    .ant-modal{width:420px;background:#fff;border-radius:8px;padding:20px 24px}.ant-modal-title{font-weight:600;margin-bottom:16px}
    .ant-picker{display:inline-flex;width:200px;border:1px solid #d9d9d9;border-radius:6px;padding:4px 11px}.ant-picker input{border:0;outline:0;width:100%;font:inherit}
    .ant-picker-dropdown{position:absolute;z-index:1100;background:#fff;border-radius:8px;box-shadow:0 6px 16px #00000014;padding:8px}
    .ant-picker-content td{width:28px;height:24px;text-align:center;cursor:pointer;padding:0}.ant-picker-cell-selected{background:#1677ff;color:#fff}
    .ant-picker-footer{display:flex;justify-content:flex-end;padding-top:6px}.ant-form-item-explain-error{color:#ff4d4f}
    .ant-message{position:fixed;top:8px;left:0;right:0;text-align:center}`;
  const statuses = ['running', 'online', 'abnormal', 'closed'];
  const random = (items) => items[Math.floor(Math.random() * items.length)];
  const rules = [97, 98, 99].map((n) => ({ name: `TradeCode ${n}`, calls: Math.floor(Math.random() * 9e6), status: random(statuses) }));
  const root = document.getElementById('root');
  root.innerHTML = `
    <header><span class="ant-dropdown-trigger ant-pro-global-header-action" id="avatar"><span class="ant-avatar">S</span> Serati Ma</span></header>
    <main>
      <div class="ant-card"><h2>Search Table</h2>
        <form class="ant-form"><div class="ant-form-item"><label class="ant-form-item-label" for="status">Status</label>
          <div class="ant-select"><span class="ant-select-selector"><input id="status" role="combobox" readonly aria-haspopup="listbox"
            aria-expanded="false" aria-controls="status_list" aria-owns="status_list" class="ant-select-selection-search-input">
            <span class="ant-select-selection-item"></span></span></div></div>
          <button type="button" class="ant-btn" id="query"><span>Query</span></button></form></div>
      <div class="ant-card"><button type="button" class="ant-btn ant-btn-primary" id="new"><span role="img" aria-label="plus" class="anticon anticon-plus">+</span><span>New</span></button>
        <table class="ant-table"><thead><tr><th>Rule Name</th><th>Description</th><th>Number of Service Calls</th><th>Status</th></tr></thead>
          <tbody>${rules.map((rule) => `<tr class="ant-table-row" data-row-key="${rule.name}"><td><a>${rule.name}</a></td><td>这是一段描述</td>
            <td>${rule.calls}</td><td><span class="ant-badge-status-text">${rule.status}</span></td></tr>`).join('')}</tbody></table></div>
      <div class="ant-card"><h3>Page style setting</h3><ul class="ant-list">${['Fixed Header', 'Split Menus', 'Weak Mode'].map((label) =>
        `<li class="ant-list-item"><span>${label}</span><button type="button" role="switch" aria-checked="false" class="ant-switch"><span class="ant-switch-handle"></span></button></li>`).join('')}</ul></div>
      <div id="page"></div>
    </main>`;

  // React's way: one handler on the root, and the state rendered after the event, in a microtask.
  const render = (update) => Promise.resolve().then(update);
  root.addEventListener('click', (event) => {
    const toggle = event.target.closest('[role=switch]');
    if (toggle) render(() => toggle.setAttribute('aria-checked', String(toggle.getAttribute('aria-checked') !== 'true')));
  });

  // The Status select: its options in a portal kept once closed, hidden; the visible items have no role.
  const status = document.getElementById('status');
  let dropdown = null;
  const close = () => { dropdown?.classList.add('ant-select-dropdown-hidden'); status.setAttribute('aria-expanded', 'false'); };
  const openSelect = () => {
    if (!dropdown) {
      dropdown = document.body.appendChild(document.createElement('div'));
      dropdown.className = 'ant-select-dropdown';
      dropdown.innerHTML = `<div role="listbox" id="status_list" style="height:0;width:0;overflow:hidden">
          <div role="option" aria-selected="false" id="status_list_0">${statuses[0]}</div></div>
        <div class="rc-virtual-list">${statuses.map((value) => `<div class="ant-select-item ant-select-item-option" title="[object Object]">
          <div class="ant-select-item-option-content">${value}</div></div>`).join('')}</div>`;
      dropdown.addEventListener('click', (event) => {
        const item = event.target.closest('.ant-select-item-option');
        if (!item) return;
        render(() => { status.parentElement.querySelector('.ant-select-selection-item').textContent = item.textContent.trim(); close(); });
      });
    }
    const box = status.parentElement.getBoundingClientRect();
    Object.assign(dropdown.style, { left: `${box.left + scrollX}px`, top: `${box.bottom + scrollY + 4}px` });
    dropdown.classList.remove('ant-select-dropdown-hidden');
    status.setAttribute('aria-expanded', 'true');
  };
  status.parentElement.addEventListener('mousedown', (event) => {
    event.preventDefault();
    if (status.getAttribute('aria-expanded') === 'true') close(); else openSelect();
  });
  document.addEventListener('mousedown', (event) => {
    if (dropdown && !dropdown.contains(event.target) && !status.parentElement.contains(event.target)) close();
  });
  document.getElementById('query').addEventListener('click', () => {
    const chosen = status.parentElement.querySelector('.ant-select-selection-item').textContent;
    for (const row of root.querySelectorAll('tbody tr')) row.hidden = !!chosen && row.cells[3].textContent !== chosen;
  });

  // The avatar's menu opens on hover only.
  const avatar = document.getElementById('avatar');
  const menu = document.body.appendChild(document.createElement('div'));
  menu.className = 'ant-dropdown ant-dropdown-hidden';
  menu.innerHTML = '<ul class="ant-dropdown-menu" role="menu"><li class="ant-dropdown-menu-item" role="menuitem">Account Settings</li>' +
    '<li class="ant-dropdown-menu-item" role="menuitem">Logout</li></ul>';
  let hide = 0;
  const showMenu = () => {
    clearTimeout(hide);
    const box = avatar.getBoundingClientRect();
    Object.assign(menu.style, { left: `${box.right - 160 + scrollX}px`, top: `${box.bottom + scrollY}px` });
    menu.classList.remove('ant-dropdown-hidden');
  };
  const hideMenu = () => { hide = setTimeout(() => menu.classList.add('ant-dropdown-hidden'), 200); };
  avatar.addEventListener('mouseenter', showMenu);
  avatar.addEventListener('mouseleave', hideMenu);
  menu.addEventListener('mouseenter', showMenu);
  menu.addEventListener('mouseleave', hideMenu);
  menu.addEventListener('click', (event) => {
    if (!event.target.closest('[role=menuitem]')) return;
    menu.classList.add('ant-dropdown-hidden');
    document.getElementById('page').innerHTML = `<div class="ant-card"><h2>${event.target.textContent}</h2></div>`;
  });

  // New opens the rule's modal: its date picker keeps a day clicked, or a date typed, only once OK or Enter commits it.
  document.getElementById('new').addEventListener('click', () => {
    const wrap = document.body.appendChild(document.createElement('div'));
    wrap.className = 'ant-modal-wrap';
    wrap.innerHTML = `<div class="ant-modal" role="dialog" aria-labelledby="rule-title"><div class="ant-modal-title" id="rule-title">New Rule</div>
      <div class="ant-form-item"><label class="ant-form-item-label" for="rule-name">Rule Name</label><input id="rule-name" class="ant-input"></div>
      <div class="ant-form-item"><label class="ant-form-item-label" for="time">Starting Time</label>
        <div class="ant-picker"><div class="ant-picker-input"><input id="time" placeholder="Select date" autocomplete="off"></div></div></div>
      <div class="ant-form-item-explain-error" role="alert" hidden>Please select the starting time</div>
      <button type="button" class="ant-btn ant-btn-primary" id="finish"><span>Finish</span></button></div>`;
    const time = wrap.querySelector('#time');
    let committed = '';
    const picker = document.body.appendChild(document.createElement('div'));
    picker.className = 'ant-picker-dropdown ant-picker-dropdown-hidden';
    const days = Array.from({ length: 30 }, (_, index) => index + 1);
    picker.innerHTML = `<div class="ant-picker-panel"><div class="ant-picker-header">November 2026</div><table class="ant-picker-content"><tbody>
      ${[0, 1, 2, 3, 4].map((week) => `<tr>${days.slice(week * 7, week * 7 + 7).map((day) =>
        `<td class="ant-picker-cell" title="2026-11-${String(day).padStart(2, '0')}"><div class="ant-picker-cell-inner">${day}</div></td>`).join('')}</tr>`).join('')}
      </tbody></table><div class="ant-picker-footer"><button type="button" class="ant-btn ant-btn-primary ant-btn-sm"><span>OK</span></button></div></div>`;
    const commit = () => { committed = time.value; picker.classList.add('ant-picker-dropdown-hidden'); };
    time.addEventListener('focus', () => {
      const box = time.getBoundingClientRect();
      Object.assign(picker.style, { left: `${box.left + scrollX}px`, top: `${box.bottom + scrollY + 6}px` });
      picker.classList.remove('ant-picker-dropdown-hidden');
    });
    time.addEventListener('keydown', (event) => { if (event.key === 'Enter') commit(); });
    picker.addEventListener('mousedown', (event) => event.preventDefault());
    picker.addEventListener('click', (event) => {
      const cell = event.target.closest('td[title]');
      if (cell) time.value = `${cell.title} 00:00:00`;
      if (event.target.closest('.ant-picker-footer button')) commit();
    });
    wrap.querySelector('#finish').addEventListener('click', () => {
      if (!committed) { wrap.querySelector('[role=alert]').hidden = false; return; }
      const name = wrap.querySelector('#rule-name').value;
      wrap.remove();
      picker.remove();
      const message = document.body.appendChild(document.createElement('div'));
      message.className = 'ant-message';
      message.innerHTML = `<span role="alert">Added ${name}</span>`;
    });
  });
})();
