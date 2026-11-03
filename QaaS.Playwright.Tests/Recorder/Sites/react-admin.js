// An imitation of react-admin's demo (Posters Galore), a public showcase of React and MUI, routed by the address's
// fragment: a login whose demo password is its user name, a list of orders, each a row of its date and time as the
// browser's locale writes them, its reference, its customer with the avatar of an initial, and its total; a customer
// filter whose autocomplete searches the first names only, so the full name finds nobody; posters whose links are named
// by their size and price too, prices the demo makes anew on each load; a poster's form whose Save shows a message with
// an UNDO button in it; and a progress bar while a page loads.
(() => {
  'use strict';
  const sheet = document.head.appendChild(document.createElement('style'));
  sheet.textContent = `
    body{margin:0;font:14px/1.5 Roboto,Helvetica,Arial,sans-serif;color:#000000de;background:#fafafa}
    .RaLayout-content{padding:16px 24px}.MuiPaper-root{background:#fff;padding:12px;margin-bottom:12px;border-radius:4px}
    .MuiButton-root{padding:6px 16px;margin-right:6px;cursor:pointer}.MuiAvatar-root{display:inline-grid;place-items:center;width:24px;height:24px;margin-right:6px;border-radius:50%;background:#bdbdbd;color:#fff}
    table{border-collapse:collapse;width:100%}th,td{padding:6px 8px;border-bottom:1px solid #e0e0e0;text-align:left}
    .MuiAutocomplete-root{position:relative;display:inline-block}.MuiAutocomplete-popper{position:absolute;z-index:10;left:0;right:0;background:#fff;box-shadow:0 2px 8px #0003}
    .MuiAutocomplete-listbox{list-style:none;margin:0;padding:0}.MuiAutocomplete-option{padding:6px 12px;cursor:pointer}
    .MuiSnackbar-root{position:fixed;bottom:16px;left:16px}.MuiSnackbarContent-root{background:#323232;color:#fff;padding:6px 16px;display:flex;gap:12px;align-items:center}
    .MuiLinearProgress-root{position:fixed;top:0;left:0;right:0;height:4px;background:#1976d2}
    .poster{display:inline-block;width:220px;margin:6px;color:inherit}`;
  const root = document.getElementById('root');
  const random = (from, to) => Math.round((from + Math.random() * (to - from)) * 100) / 100;
  const posters = [['Black & White', '14.4x17.88', random(10, 30)], ['Cat Nose', '30x20', random(10, 30)], ['Sunset', '20x30', random(10, 30)]];
  const when = (day, time) => new Date(`2025-${day}T${time}`).toLocaleString('en-GB');
  const orders = [
    [when('04-15', '08:33:15'), '50SKLO', 'Leonie Abernathy', 71.54],
    [when('10-30', '07:53:49'), '9QV2EB', 'Margarita Abernathy', 120.1],
    [when('03-02', '16:12:01'), 'K7ZM4A', 'Hipolito Bergnaum', 33.9],
  ];
  const customers = ['Hipolito Bergnaum', 'Randi Bergnaum', 'Leonie Abernathy', 'Priscilla Bauch'];
  const loggedIn = () => sessionStorage.getItem('user') === 'demo';

  function loading() {
    const bar = document.body.appendChild(document.createElement('div'));
    bar.className = 'MuiLinearProgress-root';
    bar.setAttribute('role', 'progressbar');
    setTimeout(() => bar.remove(), 250);
  }
  function snackbar(text) {
    const bar = document.body.appendChild(document.createElement('div'));
    bar.className = 'MuiSnackbar-root';
    bar.innerHTML = `<div class="MuiSnackbarContent-root" role="alert"><div class="MuiSnackbarContent-message">${text}</div>
      <div class="MuiSnackbarContent-action"><button type="button" class="MuiButton-root">UNDO</button></div></div>`;
    setTimeout(() => bar.remove(), 3000);
  }

  function render() {
    const route = location.hash.slice(1) || '/';
    if (!loggedIn() && route !== '/login') { location.hash = '#/login'; return; }
    loading();
    if (route === '/login') {
      root.innerHTML = `<form class="MuiPaper-root" id="login"><h1>Posters Galore</h1>
        <p><label for="username">Username</label> <input id="username" name="username"></p>
        <p><label for="password">Password</label> <input id="password" name="password" type="password"></p>
        <button type="submit" class="MuiButton-root">Sign in</button></form>`;
      root.querySelector('#login').addEventListener('submit', (event) => {
        event.preventDefault();
        if (root.querySelector('#username').value === 'demo' && root.querySelector('#password').value === 'demo') {
          sessionStorage.setItem('user', 'demo');
          location.hash = '#/';
        }
      });
      return;
    }

    const menu = `<nav class="RaSidebar"><a href="#/orders" role="menuitem">Orders</a> · <a href="#/products" role="menuitem">Posters</a></nav>`;
    if (route === '/orders') {
      root.innerHTML = `${menu}<div class="RaLayout-content"><h2>Orders</h2>
        <div class="RaFilterForm"><div class="MuiAutocomplete-root"><label for="customer">Customer</label>
          <input id="customer" name="customer_id" role="combobox" aria-autocomplete="list" aria-expanded="false" autocomplete="off">
          <div class="MuiAutocomplete-popper" hidden><ul class="MuiAutocomplete-listbox" role="listbox" id="customer-listbox"></ul></div></div></div>
        <table><thead><tr><th></th><th>Date</th><th>Reference</th><th>Customer</th><th>Total</th></tr></thead><tbody>
        ${orders.map(([date, reference, customer, total]) => `<tr><td><input type="checkbox" aria-label="Select this row"></td><td>${date}</td>
          <td>${reference}</td><td><span class="MuiAvatar-root">${customer[0]}</span><a href="#/customers/${reference}">${customer}</a></td>
          <td>US$${total.toFixed(2)}</td></tr>`).join('')}</tbody></table>
        <button type="button" class="MuiButton-root" id="delete" hidden>Delete</button></div>`;
      const input = root.querySelector('#customer');
      const popper = root.querySelector('.MuiAutocomplete-popper');
      const listbox = root.querySelector('#customer-listbox');
      input.setAttribute('aria-controls', 'customer-listbox');
      input.addEventListener('input', () => {
        // Its search finds customers by the start of their first name only.
        const found = customers.filter((name) => input.value && name.split(' ')[0].toLowerCase().startsWith(input.value.toLowerCase()));
        listbox.innerHTML = found.map((name) => `<li class="MuiAutocomplete-option" role="option">${name}</li>`).join('');
        popper.hidden = !found.length;
        input.setAttribute('aria-expanded', String(found.length > 0));
      });
      listbox.addEventListener('mousedown', (event) => event.preventDefault());
      listbox.addEventListener('click', (event) => {
        const option = event.target.closest('[role=option]');
        if (!option) return;
        input.value = option.textContent;
        popper.hidden = true;
        input.setAttribute('aria-expanded', 'false');
        loading();
        for (const row of root.querySelectorAll('tbody tr')) row.hidden = row.cells[3].textContent.trim().slice(1) !== option.textContent;
      });
      const remove = root.querySelector('#delete');
      root.querySelector('tbody').addEventListener('change', () => { remove.hidden = !root.querySelector('tbody input:checked'); });
      remove.addEventListener('click', () => {
        for (const box of root.querySelectorAll('tbody input:checked')) box.closest('tr').remove();
        remove.hidden = true;
        snackbar('Order deleted');
      });
      return;
    }
    if (route === '/products') {
      root.innerHTML = `${menu}<div class="RaLayout-content"><h2>Posters</h2>${posters.map(([name, size, price], index) =>
        `<a class="poster MuiPaper-root" href="#/products/${index}"><span>${name}</span> <span>${size}, US$${price.toFixed(2)}</span></a>`).join('')}</div>`;
      return;
    }
    if (route.startsWith('/products/')) {
      const poster = posters[Number(route.split('/')[2])];
      root.innerHTML = `${menu}<div class="RaLayout-content"><h2>Poster ${poster[0]}</h2><form class="MuiPaper-root" id="poster">
        <p><label for="price">Price</label> <input id="price" name="price" type="number" step="0.01" value="${poster[2]}"></p>
        <button type="submit" class="MuiButton-root">Save</button></form></div>`;
      root.querySelector('#poster').addEventListener('submit', (event) => {
        event.preventDefault();
        poster[2] = Number(root.querySelector('#price').value);
        snackbar('Poster updated');
        location.hash = '#/products';
      });
      return;
    }
    root.innerHTML = `${menu}<div class="RaLayout-content"><h2>Welcome to the demo</h2></div>`;
  }
  addEventListener('hashchange', render);
  render();
})();
