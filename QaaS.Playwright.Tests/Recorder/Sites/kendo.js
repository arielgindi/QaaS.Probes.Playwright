// An imitation of the Kendo UI for jQuery demos, a public showcase of jQuery widgets: a grid of products whose headers are
// a table of their own above the rows, edited in a popup window titled with its Close button in it, which shows a
// transient "Loading..." when saved, and deleted through the browser's confirm; a switch its own handler turns; a tree of
// checkboxes whose ids are new GUIDs on each load; a combobox that completes what is typed in place, the rest selected;
// and a week of a scheduler whose event is dragged to another slot, with a hint that follows the pointer and names the
// time it would drop at. Handlers are bound with jQuery's way, on the elements: nothing in the page tells which is which.
(() => {
  'use strict';
  const sheet = document.head.appendChild(document.createElement('style'));
  sheet.textContent = `
    body{margin:0;font:14px/1.4 Roboto,Arial,sans-serif;color:#424242;background:#fff}main{padding:16px 24px;max-width:1100px}
    .k-grid{border:1px solid #dee2e6;margin-bottom:16px}.k-grid table{border-collapse:collapse;width:100%;table-layout:fixed}
    .k-grid th,.k-grid td{padding:6px 8px;border-bottom:1px solid #dee2e6;text-align:left}.k-button{padding:3px 10px;margin-right:4px;cursor:pointer}
    .k-window{position:fixed;top:80px;left:300px;z-index:100;width:360px;background:#fff;border:1px solid #ccc;box-shadow:0 4px 16px #0003}
    .k-window-titlebar{display:flex;justify-content:space-between;padding:8px 12px;background:#f5f5f5}.k-window-content{padding:12px}
    .k-edit-label{display:block;margin-top:8px}.k-loading-mask{position:fixed;top:8px;left:8px;background:#fffbe6;padding:4px 8px}
    .k-switch{display:inline-block;width:52px;height:26px;border-radius:13px;background:#ccc;cursor:pointer;position:relative;vertical-align:middle}
    .k-switch[aria-checked=true]{background:#ff6358}.k-switch-thumb{position:absolute;top:3px;left:3px;width:20px;height:20px;border-radius:50%;background:#fff}
    .k-switch[aria-checked=true] .k-switch-thumb{left:29px}
    .k-treeview ul{list-style:none;margin:0;padding-left:20px}.k-treeview-top{display:inline-flex;gap:4px;align-items:center;padding:2px}
    .k-combobox{position:relative;display:inline-block}.k-list{position:absolute;z-index:10;left:0;right:0;margin:0;padding:0;list-style:none;background:#fff;border:1px solid #ccc}
    .k-list-item{padding:4px 8px;cursor:pointer}.k-list-item:hover{background:#eee}
    .k-scheduler table{border-collapse:collapse}.k-scheduler td,.k-scheduler th{width:110px;height:36px;border:1px solid #dee2e6;padding:0;vertical-align:top}
    .k-event{display:block;margin:2px;padding:2px 4px;background:#ff6358;color:#fff;cursor:move;user-select:none}
    .k-event-drag-hint{position:fixed;z-index:200;opacity:.8;width:100px}`;
  const guid = () => '_' + crypto.randomUUID();
  const products = [['Chai', 18], ['Chang', 19], ['Aniseed Syrup', 10], ['Tofu', 23.25], ['Konbu', 6], ['Ikura', 31]];
  const money = (price) => `$${price.toFixed(2)}`;
  const root = document.getElementById('root');
  root.innerHTML = `<main>
    <h1>jQuery Grid Popup Editing</h1>
    <div id="grid" class="k-grid" role="grid">
      <div class="k-grid-header"><table><thead><tr><th role="columnheader">Product Name</th><th role="columnheader">Unit Price</th>
        <th role="columnheader">Units In Stock</th><th role="columnheader"></th></tr></thead></table></div>
      <div class="k-grid-content"><table><tbody>${products.map(([name, price], index) => `<tr class="k-master-row" data-uid="${guid()}">
        <td>${name}</td><td>${money(price)}</td><td>${(index * 17 + 13) % 120}</td>
        <td><button type="button" class="k-button k-grid-edit">Edit</button><button type="button" class="k-button k-grid-remove">Delete</button></td></tr>`).join('')}
      </tbody></table></div></div>
    <h2>Event</h2><p><span id="all-day-label">All day</span> <span role="switch" aria-checked="false" aria-labelledby="all-day-label" tabindex="0" class="k-switch"><span class="k-switch-thumb"></span></span></p>
    <h2>Check nodes</h2><div id="treeview" class="k-treeview" role="tree"><ul>${[['My Documents', ['about.html', 'index.html']], ['Reports', ['February.pdf', 'March.pdf']]]
      .map(([folder, files]) => `<li role="treeitem" aria-expanded="true"><span class="k-treeview-top"><input type="checkbox" class="k-checkbox" id="${guid()}"><span class="k-treeview-leaf">${folder}</span></span>
        <ul role="group">${files.map((file) => `<li role="treeitem"><span class="k-treeview-top"><input type="checkbox" class="k-checkbox" id="${guid()}"><span class="k-treeview-leaf">${file}</span></span></li>`).join('')}</ul></li>`).join('')}</ul></div>
    <h2>Hobbies</h2><span class="k-combobox"><input id="sport" role="combobox" aria-autocomplete="both" aria-expanded="false" aria-controls="sport_listbox" placeholder="Select sport..." autocomplete="off">
      <ul class="k-list" role="listbox" id="sport_listbox" hidden></ul></span>
    <h2>Scheduler</h2><div class="k-scheduler"><table><thead><tr><th></th><th role="columnheader">Mon 10/6</th><th role="columnheader">Tue 10/7</th></tr></thead>
      <tbody>${['9:00 AM', '10:00 AM'].map((hour) => `<tr><th>${hour}</th><td class="k-slot" data-day="Monday, October 06" data-hour="${hour}"></td>
        <td class="k-slot" data-day="Tuesday, October 07" data-hour="${hour}"></td></tr>`).join('')}</tbody></table></div>
    <p id="moved"></p></main>`;

  // The grid: Edit opens a window, Delete asks the browser first.
  const grid = document.getElementById('grid');
  for (const button of grid.querySelectorAll('.k-grid-remove'))
    button.addEventListener('click', (event) => {
      if (confirm('Are you sure you want to delete this record?')) event.target.closest('tr').remove();
    });
  for (const button of grid.querySelectorAll('.k-grid-edit'))
    button.addEventListener('click', (event) => {
      const row = event.target.closest('tr');
      const window = document.body.appendChild(document.createElement('div'));
      window.className = 'k-window';
      window.setAttribute('role', 'dialog');
      window.setAttribute('aria-labelledby', 'edit-titlebar');
      window.innerHTML = `<div class="k-window-titlebar" id="edit-titlebar"><span class="k-window-title">Edit</span>
          <button type="button" class="k-window-titlebar-action" aria-label="Close">x</button></div>
        <div class="k-window-content"><label class="k-edit-label" for="UnitPrice">Unit Price</label>
          <input id="UnitPrice" name="UnitPrice" role="spinbutton" value="${row.cells[1].textContent.slice(1)}">
          <p><button type="button" class="k-button k-grid-update">Update</button><button type="button" class="k-button k-grid-cancel">Cancel</button></p></div>`;
      const close = () => window.remove();
      window.querySelector('[aria-label=Close]').addEventListener('click', close);
      window.querySelector('.k-grid-cancel').addEventListener('click', close);
      window.querySelector('.k-grid-update').addEventListener('click', () => {
        row.cells[1].textContent = money(Number(window.querySelector('#UnitPrice').value));
        close();
        const loading = document.body.appendChild(document.createElement('div'));
        loading.className = 'k-loading-mask';
        loading.innerHTML = '<span role="alert" class="k-loading-text">Loading...</span>';
        setTimeout(() => loading.remove(), 400);
      });
    });

  // The switch: its own handler, jQuery's way, after the recorder's listener has seen the click.
  const allDay = root.querySelector('.k-switch');
  allDay.addEventListener('click', () => allDay.setAttribute('aria-checked', String(allDay.getAttribute('aria-checked') !== 'true')));

  // The combobox: what is typed is completed in place, by the first sport it starts, the rest selected.
  const sports = ['Baseball', 'Basketball', 'Cricket', 'Field Hockey', 'Football', 'Table Tennis', 'Tennis', 'Volleyball'];
  const sport = document.getElementById('sport');
  const list = document.getElementById('sport_listbox');
  const show = (typed) => {
    const found = sports.filter((name) => name.toLowerCase().startsWith(typed.toLowerCase()));
    list.innerHTML = found.map((name) => `<li class="k-list-item" role="option">${name}</li>`).join('');
    list.hidden = !typed || !found.length;
    sport.setAttribute('aria-expanded', String(!list.hidden));
    return found;
  };
  sport.addEventListener('input', (event) => {
    const typed = sport.value.slice(0, sport.selectionStart);
    const found = show(typed);
    if (event.inputType?.startsWith('insert') && found.length && typed.length === sport.value.length) {
      sport.value = found[0];
      sport.setSelectionRange(typed.length, found[0].length);
    }
  });
  list.addEventListener('mousedown', (event) => event.preventDefault());
  list.addEventListener('click', (event) => {
    const option = event.target.closest('[role=option]');
    if (!option) return;
    sport.value = option.textContent;
    list.hidden = true;
    sport.setAttribute('aria-expanded', 'false');
  });

  // The scheduler: an event dragged by the pointer, a hint under it naming the slot it would drop at.
  const label = (slot) => `Home Ec on ${slot.dataset.day}, 2025 at ${slot.dataset.hour}`;
  const event = document.createElement('div');
  event.className = 'k-event';
  event.setAttribute('role', 'button');
  event.textContent = 'Home Ec';
  const place = (slot) => { slot.appendChild(event); event.setAttribute('aria-label', label(slot)); };
  place(root.querySelector('.k-slot[data-day^=Monday][data-hour="10:00 AM"]'));
  let hint = null;
  const slotAt = (x, y) => document.elementsFromPoint(x, y).find((element) => element.matches?.('.k-slot'));
  event.addEventListener('pointerdown', (down) => {
    down.preventDefault();
    hint = document.body.appendChild(event.cloneNode(true));
    hint.classList.add('k-event-drag-hint');
    const follow = (move) => {
      Object.assign(hint.style, { left: `${move.clientX - 50}px`, top: `${move.clientY - 12}px` });
      const slot = slotAt(move.clientX, move.clientY);
      if (slot) hint.setAttribute('aria-label', label(slot));
    };
    follow(down);
    const drop = (up) => {
      removeEventListener('pointermove', follow);
      removeEventListener('pointerup', drop);
      const slot = slotAt(up.clientX, up.clientY);
      hint.remove();
      hint = null;
      if (slot && slot !== event.parentElement) {
        place(slot);
        document.getElementById('moved').textContent = `Moved to ${slot.dataset.day} ${slot.dataset.hour}`;
      }
    };
    addEventListener('pointermove', follow);
    addEventListener('pointerup', drop);
  });
})();
