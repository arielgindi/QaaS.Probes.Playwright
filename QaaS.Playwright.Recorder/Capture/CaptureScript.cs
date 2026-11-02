namespace QaaS.Playwright.Recorder.Capture;

/// <summary>
/// The script the recorder installs in every page it records. It watches what the person does, without changing
/// it, and reports each action through the binding <c>__qaasRecord</c>: every way to find the element, each checked
/// with Playwright's own engine (its console API, present in debug mode "console"), the dialog it is in, the value a
/// field settled on, and what the page did a moment later.
/// </summary>
internal static class CaptureScript
{
    /// <summary>The script, recording the test ids of <paramref name="testIdAttribute"/>.</summary>
    public static string For(string testIdAttribute) =>
        Source.Replace("__TEST_ID_ATTRIBUTE__", testIdAttribute.Replace("'", "", StringComparison.Ordinal),
            StringComparison.Ordinal);

    private const string Source = """
        (() => {
          // QaaS recorder capture script. Runs in every document of the recording, before the page's own scripts. Its
          // listeners are capture-phase and passive: they never prevent or stop an event, so the page behaves exactly as for a
          // person. Each action is described when the pointer goes down, before the page reacts to it, and reported to the
          // recorder through window.__qaasRecord as JSON.
          if (window.__qaasCaptureInstalled || window !== window.top) return;
          window.__qaasCaptureInstalled = true;

          const TEST_ID = '__TEST_ID_ATTRIBUTE__';
          const INTERACTIVE = 'button, a[href], input, textarea, select, tr, [role=button], [role=link], [role=checkbox], ' +
            '[role=radio], [role=switch], [role=tab], [role=menuitem], [role=menuitemcheckbox], [role=menuitemradio], ' +
            '[role=option], [role=combobox], [role=gridcell], [role=row], [contenteditable=""], [contenteditable=true]';
          const TOGGLES = 'input[type=checkbox], input[type=radio], [role=checkbox], [role=radio], [role=switch]';
          const DIALOGS = '[role=dialog], [role=alertdialog]';
          const PICKER = '.MuiPickersPopper-root';
          const range = (from, to = from) => String.fromCharCode(...Array.from({ length: to - from + 1 }, (_, i) => from + i));
          // Bidi marks and isolates, and the zero-width space an empty MUI Select shows.
          const BIDI = new RegExp(`[${range(0x200b)}${range(0x200e, 0x200f)}${range(0x202a, 0x202e)}${range(0x2066, 0x2069)}${range(0x061c)}]`, 'g');
          const INVISIBLE = new RegExp(`[${range(0x200b)}${range(0x00ad)}]`, 'g');
          // Taken before the page's scripts run, so the overlay's shadow root stays closed whatever they replace.
          const attachShadow = Element.prototype.attachShadow;
          const page = Math.random().toString(36).slice(2, 8);
          let counter = 0;

          const send = (message) => {
            try { window.__qaasRecord?.(JSON.stringify(message)); } catch { /* the recorder is gone */ }
          };
          const safe = (work, fallback) => {
            try { return work(); } catch { return fallback; }
          };
          const newId = () => `${page}-${++counter}`;
          const now = () => Date.now();

          // ---------------------------------------------------------------------------------------------- text and names
          const normalize = (text) => (text ?? '').replace(INVISIBLE, '').replace(/\s+/g, ' ').trim();
          const clean = (text) => normalize((text ?? '').replace(BIDI, '')).replace(/\s*\*$/, '').trim();
          const quote = (value) => `'${String(value).replace(/\\/g, '\\\\').replace(/'/g, "\\'")}'`;
          const regexEscape = (text) => text.replace(/[\\^$.|?*+()[\]{}]/g, '\\$&');
          // The text before the first word that holds a digit: "Close HD-1041" -> "Close".
          const beforeDigits = (text) => {
            const words = text.split(' ');
            const index = words.findIndex((word) => /\d/.test(word));
            return (index < 0 ? text : words.slice(0, index).join(' ')).replace(/[\s(:\-–—,]+$/, '').trim();
          };

          const styleOf = (element) => safe(() => getComputedStyle(element), null);
          const hiddenSelf = (element) => {
            if (element.getAttribute('aria-hidden') === 'true') return true;
            const style = styleOf(element);
            return !style || style.display === 'none' || style.visibility === 'hidden';
          };
          const hiddenForAria = (element) => {
            for (let node = element; node && node.nodeType === 1; node = node.parentElement)
              if (node.getAttribute('aria-hidden') === 'true' || styleOf(node)?.display === 'none') return true;
            return styleOf(element)?.visibility === 'hidden';
          };
          const visible = (element) => {
            if (!element?.isConnected) return false;
            const box = element.getBoundingClientRect();
            if (box.width === 0 && box.height === 0) return false;
            const style = styleOf(element);
            return !!style && style.visibility !== 'hidden' && style.display !== 'none';
          };
          const idRefs = (element, attribute) => (element.getAttribute(attribute) || '').split(/\s+/).filter(Boolean)
            .map((id) => document.getElementById(id)).filter(Boolean);

          function roleOf(element) {
            const explicit = (element.getAttribute('role') || '').split(/\s+/).filter(Boolean)[0];
            if (explicit && explicit !== 'presentation' && explicit !== 'none') return explicit;
            switch (element.tagName) {
              case 'BUTTON': return 'button';
              case 'A': return element.hasAttribute('href') ? 'link' : null;
              case 'TEXTAREA': return 'textbox';
              case 'SELECT': return element.multiple || element.size > 1 ? 'listbox' : 'combobox';
              case 'OPTION': return 'option';
              case 'TR': return 'row';
              case 'TD': return element.closest('table')?.getAttribute('role') === 'grid' ? 'gridcell' : 'cell';
              case 'TH': return 'columnheader';
              case 'LI': return 'listitem';
              case 'H1': case 'H2': case 'H3': case 'H4': case 'H5': case 'H6': return 'heading';
              case 'INPUT': {
                const type = (element.type || 'text').toLowerCase();
                if (['button', 'submit', 'reset', 'image'].includes(type)) return 'button';
                if (type === 'checkbox') return 'checkbox';
                if (type === 'radio') return 'radio';
                if (type === 'range') return 'slider';
                if (type === 'number') return 'spinbutton';
                if (type === 'file') return 'button';
                if (type === 'hidden' || type === 'password') return null;
                if (type === 'search') return element.hasAttribute('list') ? 'combobox' : 'searchbox';
                return element.hasAttribute('list') ? 'combobox' : 'textbox';
              }
              default: return null;
            }
          }

          const NAME_FROM_CONTENT = new Set(['button', 'cell', 'checkbox', 'columnheader', 'gridcell', 'heading', 'link',
            'menuitem', 'menuitemcheckbox', 'menuitemradio', 'option', 'radio', 'row', 'rowheader', 'switch', 'tab',
            'tooltip', 'treeitem']);

          // A port of the accessible name computation of Playwright's role engine (roleUtils.ts), for the elements a person
          // acts on. A name it gets wrong is only a candidate that fails its check against Playwright's own engine.
          function accessibleName(element) {
            return safe(() => textAlternative(element, { visited: new Set(), self: true }), '').replace(/\s+/g, ' ').trim();
          }

          function textAlternative(element, context) {
            if (context.visited.has(element)) return '';
            if (!context.referenced && hiddenSelf(element)) { context.visited.add(element); return ''; }
            const labelledBy = context.inLabelledBy ? [] : idRefs(element, 'aria-labelledby');
            if (labelledBy.length) {
              const name = labelledBy.map((reference) => textAlternative(reference, {
                visited: context.visited, inLabelledBy: true, embedded: true, referenced: hiddenForAria(reference),
              })).join(' ');
              if (name.trim()) return name;
            }
            const role = roleOf(element) || '';
            const tag = element.tagName;
            if (context.embedded && !context.self) {
              const ownLabel = [...(element.labels || [])].length > 0 && context.inLabel;
              if (!ownLabel && role === 'textbox') {
                context.visited.add(element);
                return tag === 'INPUT' || tag === 'TEXTAREA' ? element.value : element.textContent || '';
              }
              if (!ownLabel && role === 'combobox' && tag === 'INPUT') { context.visited.add(element); return element.value; }
            }
            const ariaLabel = element.getAttribute('aria-label') || '';
            if (ariaLabel.trim()) { context.visited.add(element); return ariaLabel; }
            if (tag === 'INPUT' && ['button', 'submit', 'reset'].includes(element.type)) {
              context.visited.add(element);
              return element.value || (element.type === 'submit' ? 'Submit' : element.type === 'reset' ? 'Reset' : '');
            }
            if (!labelledBy.length && ['INPUT', 'TEXTAREA', 'SELECT', 'BUTTON'].includes(tag)) {
              const labels = [...(element.labels || [])];
              if (labels.length) {
                context.visited.add(element);
                return labels.map((label) => textAlternative(label, {
                  visited: context.visited, embedded: true, inLabel: true, referenced: hiddenForAria(label),
                })).join(' ');
              }
              if (tag !== 'BUTTON') {
                context.visited.add(element);
                const usesPlaceholder = tag === 'TEXTAREA' || (tag === 'INPUT' &&
                  ['text', 'password', 'search', 'tel', 'email', 'url'].includes(element.type));
                const title = element.getAttribute('title') || '';
                return !usesPlaceholder || title ? title : element.getAttribute('placeholder') || '';
              }
            }
            if (tag === 'IMG') { context.visited.add(element); return element.getAttribute('alt') || element.title || ''; }
            if (NAME_FROM_CONTENT.has(role) || context.embedded) {
              context.visited.add(element);
              const tokens = [];
              for (let child = element.firstChild; child; child = child.nextSibling) {
                if (child.nodeType === 3) tokens.push(child.textContent || '');
                else if (child.nodeType === 1) {
                  let token = textAlternative(child, { ...context, self: false, embedded: true });
                  const display = styleOf(child)?.display || 'inline';
                  if (display !== 'inline' || child.nodeName === 'BR') token = ` ${token} `;
                  tokens.push(token);
                }
              }
              const name = tokens.join('');
              if (name.trim()) return name;
            }
            context.visited.add(element);
            return element.getAttribute('title') || '';
          }

          // ----------------------------------------------------------------------------------- Playwright's own engine
          const engine = () => window.playwright;
          // A locator as parts the recorder turns into C#, resolved here with Playwright's console API, the engine a replay uses.
          function resolve(parts) {
            let locator = engine();
            for (const part of parts) {
              if (!locator) return null;
              if (part.m === 'css') locator = locator.locator(part.css);
              else if (part.m === 'testid') locator = locator.getByTestId(part.value);
              else if (part.m === 'filter') locator = locator.filter({ hasText: part.hasText });
              else if (part.m === 'role') {
                const options = {};
                if (part.regex !== undefined) options.name = new RegExp(part.regex);
                else if (part.name !== undefined) options.name = part.name;
                if (part.exact) options.exact = true;
                locator = locator.getByRole(part.role, options);
              }
            }
            return locator;
          }
          const matches = (parts) => safe(() => resolve(parts)?.elements ?? null, null);

          // ------------------------------------------------------------------------------------------- what is touched
          const deepTarget = (event) => {
            const node = event.composedPath()[0];
            return node?.nodeType === 1 ? node : node?.parentElement ?? null;
          };
          const isTextual = (element) => element.isContentEditable || element.tagName === 'TEXTAREA' ||
            (element.tagName === 'INPUT' &&
             !['checkbox', 'radio', 'file', 'button', 'submit', 'reset', 'image', 'range', 'color', 'hidden']
               .includes((element.type || 'text').toLowerCase()));
          const isMuiSelect = (element) => element.getAttribute('role') === 'combobox' && element.tagName !== 'INPUT' &&
            !!element.parentElement?.querySelector(':scope > input[name]');
          const selectInput = (element) => element.parentElement?.querySelector(':scope > input[name]');
          const isAutocomplete = (element) => element.tagName === 'INPUT' && element.getAttribute('role') === 'combobox';
          const controlOfLabel = (label) => label.control ?? label.querySelector(TOGGLES);

          // The element a person meant: the interactive element around what the pointer hit, or for a label of a checkbox,
          // radio or switch, the control itself.
          function retarget(hit) {
            const interactive = hit.closest(INTERACTIVE);
            const label = hit.closest('label');
            if (label && (!interactive || interactive.contains(label))) {
              const control = controlOfLabel(label);
              if (control) return control;
            }
            return interactive ?? hit;
          }

          function dialogOf(element) {
            for (let dialog = element.closest(DIALOGS); dialog; dialog = dialog.parentElement?.closest(DIALOGS))
              if (!dialog.matches(PICKER)) return dialog;
            return null;
          }
          const openDialogs = () => [...document.querySelectorAll(DIALOGS)].filter((dialog) => !dialog.matches(PICKER) &&
            visible(dialog));

          function describeDialog(dialog) {
            const raw = normalize(accessibleName(dialog));
            const title = clean(raw);
            const prefix = beforeDigits(title);
            const count = (name) => name ? (matches([{ m: 'role', role: 'dialog', name }])?.length ?? -1) : -1;
            const hits = (name) => { const found = matches([{ m: 'role', role: 'dialog', name }]); return !!found && found.length === 1 && found[0] === dialog; };
            return {
              title, raw,
              titleMatches: !!title && hits(title),
              prefix: prefix !== title ? prefix : null,
              prefixMatches: !!prefix && prefix !== title && hits(prefix),
              rawMatches: raw !== title && hits(raw),
              unnamedMatches: (matches([{ m: 'role', role: 'dialog' }])?.length ?? 0) === 1,
              count: count(title),
            };
          }

          // The scope the element is found in: its dialog, as the recorder will write it, or the page.
          function scopeOf(element, dialogInfo, dialog) {
            if (!dialog || !dialogInfo) return [];
            if (dialogInfo.titleMatches) return [{ m: 'role', role: 'dialog', name: dialogInfo.title }];
            if (dialogInfo.prefixMatches) return [{ m: 'role', role: 'dialog', name: dialogInfo.prefix }];
            if (dialogInfo.rawMatches) return [{ m: 'role', role: 'dialog', name: dialogInfo.raw, exact: true }];
            if (dialogInfo.unnamedMatches) return [{ m: 'role', role: 'dialog' }];
            return [];
          }

          function nameVariants(element, role) {
            const raw = normalize(accessibleName(element));
            const stripped = clean(raw);
            if (!stripped) return [];
            if (isMuiSelect(element) || (role === 'combobox' && element.getAttribute('aria-labelledby'))) {
              const label = clean(idRefs(element, 'aria-labelledby').filter((ref) => ref !== element)
                .map((ref) => ref.textContent).join(' '));
              return label ? [{ regex: '^' + regexEscape(label), text: label }] : [];
            }
            if (role === 'tab' && /\d/.test(stripped)) {
              const prefix = beforeDigits(stripped).replace(/\s*\($/, '');
              return prefix ? [{ regex: '^' + regexEscape(prefix), text: prefix }] : [];
            }
            const variants = [{ name: stripped, exact: true }, { name: stripped }];
            if (raw !== stripped && raw.replace(BIDI, '') !== raw) variants.push({ name: raw, exact: true, marks: true });
            return variants;
          }

          // A row of a table and the text that tells it apart from the others: the first cell, preferring one with a digit,
          // whose text no other row has.
          function rowOf(element) {
            const row = element.closest('tr, [role=row]');
            if (!row || row.querySelector('th[scope=col], [role=columnheader]')) return null;
            const cells = [...row.querySelectorAll(':scope > td, :scope > th, :scope > [role=cell], :scope > [role=gridcell]')];
            const texts = cells.map((cell) => clean(cell.innerText || cell.textContent));
            const table = row.closest('table, [role=grid], [role=table], [role=treegrid]');
            const headers = table ? [...table.querySelectorAll('th[scope=col], [role=columnheader], thead th')] : [];
            const order = texts.map((text, index) => ({ text, index }))
              .filter(({ text }) => text.length >= 2 && text.length <= 40)
              .sort((a, b) => Number(/\d/.test(b.text)) - Number(/\d/.test(a.text)) || a.index - b.index);
            for (const { text, index } of order) {
              const found = matches([{ m: 'role', role: 'row' }, { m: 'filter', hasText: text }]);
              if (found?.length === 1 && found[0] === row)
                return { key: text, header: clean(headers[index]?.textContent ?? ''), element: row };
            }
            return { key: null, header: null, element: row };
          }

          function candidate(kind, scope, parts, element, extra) {
            const found = matches([...scope, ...parts]);
            const hidden = found && found.length > 1 ? found.filter((other) => !visible(other)).length : 0;
            return { kind, parts: [...scope, ...parts], count: found ? found.length : -1,
              matches: !!found && found.length === 1 && found[0] === element, ...(hidden ? { hidden } : {}), ...extra };
          }

          // Every way to find the element, each checked against the page as it is now. The recorder picks one.
          function describeTarget(element, dialog, dialogInfo, options = {}) {
            const scope = options.pageLevel ? [] : scopeOf(element, dialogInfo, dialog);
            const role = roleOf(element);
            const candidates = [];
            const tag = element.tagName;
            const nameAttribute = element.getAttribute('name');
            if (nameAttribute && ['INPUT', 'TEXTAREA', 'SELECT'].includes(tag)) {
              candidates.push(candidate('name', scope, [{ m: 'css', css: `[name=${quote(nameAttribute)}]` }], element,
                { value: nameAttribute }));
              if (element.matches('input[type=radio], input[type=checkbox]') && element.value)
                candidates.push(candidate('nameValue', scope,
                  [{ m: 'css', css: `[name=${quote(nameAttribute)}][value=${quote(element.value)}]` }], element,
                  { value: nameAttribute }));
            }
            if (element.id) candidates.push(candidate('id', scope, [{ m: 'css', css: `[id=${quote(element.id)}]` }], element,
              { value: element.id }));
            if (isMuiSelect(element)) {
              const input = selectInput(element);
              candidates.push(candidate('select', scope, [{ m: 'css', css: `div:has(> input[name=${quote(input.name)}])` },
                { m: 'role', role: 'combobox' }], element, { value: input.name }));
            }
            const testId = element.getAttribute(TEST_ID);
            if (testId) candidates.push(candidate('testid', scope, [{ m: 'testid', value: testId }], element, { value: testId }));
            let roleText = null;
            if (role) {
              for (const variant of nameVariants(element, role)) {
                const { text, ...part } = variant;
                const found = candidate('role', scope, [{ m: 'role', role, ...part }], element, { text: text ?? variant.name });
                candidates.push(found);
                roleText ??= text ?? variant.name;
                if (found.matches) break;
              }
              if (role === 'radio' || role === 'checkbox') {
                const group = element.closest('[role=radiogroup], [role=group], fieldset');
                if (group && nameAttribute) {
                  const groupCss = `${group.matches('[role]') ? `[role=${quote(group.getAttribute('role'))}]` : group.tagName.toLowerCase()}:has([name=${quote(nameAttribute)}])`;
                  const name = clean(accessibleName(element));
                  if (name) candidates.push(candidate('roleInGroup', scope,
                    [{ m: 'css', css: groupCss }, { m: 'role', role, name, exact: true }], element, { text: name }));
                }
              }
            }
            const row = options.noRow ? null : rowOf(element);
            if (row?.key && role) {
              const name = clean(accessibleName(element));
              const rowParts = [{ m: 'role', role: 'row' }, { m: 'filter', hasText: row.key }];
              const inner = row.element === element ? [] : name ? [{ m: 'role', role, name, exact: true }] : [{ m: 'role', role }];
              candidates.push(candidate('row', scope, [...rowParts, ...inner], element, { text: name }));
            }
            const generated = safe(() => engine()?.generateLocator(element, 'csharp'), null);
            if (generated) candidates.push({ kind: 'generator', csharp: generated, count: 1, matches: true });
            return {
              tag: tag.toLowerCase(), role, type: element.getAttribute('type'), id: element.id || null,
              name: nameAttribute, testId, value: element.matches('input[type=radio], input[type=checkbox]') ? element.value : null,
              text: roleText ?? clean(accessibleName(element)),
              label: clean(idRefs(element, 'aria-labelledby').filter((ref) => ref !== element).map((ref) => ref.textContent).join(' ')) ||
                clean([...(element.labels || [])].map((label) => label.textContent).join(' ')) || null,
              interactive: options.interactive ?? (element.matches(INTERACTIVE) || element.matches(TOGGLES)),
              row: row ? { key: row.key, header: row.header } : null,
              candidates,
            };
          }

          function describe(element, extra = {}) {
            const dialog = dialogOf(element);
            const dialogInfo = dialog ? describeDialog(dialog) : null;
            return {
              dialog: dialogInfo,
              openDialogs: openDialogs().map((open) => clean(accessibleName(open))),
              target: describeTarget(element, dialog, dialogInfo, extra),
            };
          }

          // -------------------------------------------------------------------------------------------------- actions
          let lastAction = null;
          const remember = (id) => { lastAction = { id, t: now() }; };
          function record(action) {
            send({ type: 'action', url: location.href, ...action });
          }
          function effect(kind, data, actionId) {
            send({ type: 'effect', kind, t: now(), actionId: actionId ?? lastAction?.id ?? null,
              sinceAction: lastAction ? now() - lastAction.t : null, ...data });
          }

          // ------------------------------------------------------------------------------------- typing: settled values
          const typing = new Map();
          const valueOf = (element) => element.isContentEditable ? element.innerText : element.value;
          function emitFill(entry, final) {
            clearTimeout(entry.timer);
            const raw = valueOf(entry.element) ?? '';
            record({ ...entry.description, id: entry.id, t: entry.t, kind: 'fill', final, value: normalizeValue(raw), raw,
              widget: isAutocomplete(entry.element) ? 'autocomplete' : entry.element.tagName === 'TEXTAREA' ? 'textarea' : 'text' });
            if (final) typing.delete(entry.element);
          }
          const normalizeValue = (raw) => raw.replace(BIDI, '');
          function flushTyping() {
            for (const entry of [...typing.values()]) emitFill(entry, true);
          }

          addEventListener('input', (event) => safe(() => {
            if (!event.isTrusted) return;
            const element = deepTarget(event);
            if (!element || !isTextual(element)) return;
            let entry = typing.get(element);
            if (!entry) {
              entry = { id: newId(), t: now(), element, description: describe(element) };
              typing.set(element, entry);
              remember(entry.id);
            }
            clearTimeout(entry.timer);
            entry.timer = setTimeout(() => safe(() => emitFill(entry, false)), 700);
          }), true);
          addEventListener('focusout', (event) => safe(() => {
            const entry = typing.get(deepTarget(event));
            if (entry) emitFill(entry, true);
          }), true);
          addEventListener('pagehide', () => safe(flushTyping), true);

          // ------------------------------------------------------------------------------------------- date pickers
          // A date chosen in a MUI X picker is recorded as the date typed into its field: the calendar's clicks depend on
          // today's date, the field's value does not.
          let picker = null;
          const fieldOfAdornment = (element) => element.closest('.MuiInputAdornment-root')
            ?.closest('.MuiFormControl-root, .MuiTextField-root')?.querySelector('input:not([type=hidden])') ?? null;
          function watchPicker(field) {
            const description = describe(field);
            const before = valueOf(field);
            picker = { field, description, before, id: newId(), t: now() };
            const started = now();
            const poll = setInterval(() => safe(() => {
              const open = [...document.querySelectorAll(PICKER)].some(visible);
              if (open && now() - started < 120000) return;
              clearInterval(poll);
              const state = picker;
              picker = null;
              if (!state || valueOf(state.field) === state.before) return;
              remember(state.id);
              record({ ...state.description, id: state.id, t: state.t, kind: 'fill', final: true, widget: 'date',
                value: normalizeValue(valueOf(state.field)), raw: valueOf(state.field), picked: true });
            }), 150);
          }

          // ------------------------------------------------------------------------------------------------ pointer
          let pending = null;
          let opened = null;
          let lastClick = null;
          let lastEnter = 0;
          let lastPointer = 0;

          function draftFor(hit) {
            const picked = pickedFor(hit);
            const element = picked ?? retarget(hit);
            if (hit.closest(PICKER)) return { ignore: 'picker' };
            const fileInput = element.matches('input[type=file]') ? element
              : element.closest('label')?.control?.matches?.('input[type=file]') ? element.closest('label').control : null;
            if (fileInput) return { ignore: 'file chooser' };
            if (isTextual(element) && !element.readOnly) return { ignore: 'focus', element };
            const toggle = element.matches(TOGGLES) ? element : null;
            const option = element.closest('[role=option]');
            if (option) {
              const listbox = option.closest('[role=listbox]');
              const owner = listbox?.id ? document.querySelector(`[aria-controls="${CSS.escape(listbox.id)}"]`) : null;
              const info = describe(option, { pageLevel: true, noRow: true });
              // A Select's menu hides the rest of the page from assistive technology, so the field is described as it was
              // when its menu opened.
              const ownerInfo = !owner ? null : opened?.element === owner ? opened.info : describe(owner);
              return { element: option, owner, kind: 'choose',
                widget: !owner ? 'option' : isAutocomplete(owner) ? 'autocomplete' : owner.tagName === 'SELECT' ? 'native' : 'select',
                option: info.target, ...(ownerInfo ?? info), field: ownerInfo?.target ?? null,
                hoverMenu: hoverMenus.has(option.closest('[role=menu], [role=listbox]')) };
            }
            // An element the person picked is what they act on, whatever it is.
            const info = describe(element, picked ? { interactive: true } : {});
            if (isMuiSelect(element) || isAutocomplete(element)) opened = { element, info };
            const widget = toggle ? (roleOf(toggle) === 'radio' ? 'radio' : roleOf(toggle) === 'switch' ? 'switch' : 'checkbox')
              : isMuiSelect(element) ? 'select-open'
              : element.closest('.MuiAutocomplete-root') && element.tagName === 'BUTTON' ? 'autocomplete-button'
              : roleOf(element) === 'tab' ? 'tab'
              : roleOf(element) === 'menuitem' ? 'menuitem'
              : null;
            const field = widget === 'autocomplete-button'
              ? describe(element.closest('.MuiAutocomplete-root').querySelector('input')).target : null;
            return { element, kind: toggle ? 'toggle' : 'click', widget, ...info, field,
              adornmentField: element.tagName === 'BUTTON' ? fieldOfAdornment(element) : null,
              hoverMenu: hoverMenus.has(element.closest('[role=menu]')) };
          }

          function finish(state, via) {
            if (!state || state.done) return;
            state.done = true;
            const { draft } = state;
            const { element, adornmentField, owner, ignore, ...rest } = draft;
            if (draft.kind === 'toggle') {
              const checked = element.matches('input') ? element.checked : element.getAttribute('aria-checked') === 'true';
              record({ ...rest, id: state.id, t: state.t, kind: checked ? 'check' : draft.widget === 'radio' ? 'check' : 'uncheck', checked, via });
              return;
            }
            if (draft.kind === 'choose') {
              record({ ...rest, id: state.id, t: state.t, via, value: draft.option.text });
              if (owner) settleChoice(state.id, owner, draft.option);
              return;
            }
            if (adornmentField) {
              // The picker's calendar opens a moment later; until then the click is held back.
              setTimeout(() => safe(() => {
                if ([...document.querySelectorAll(PICKER)].some(visible)) { watchPicker(adornmentField); return; }
                emitClick(state, rest, element, via);
              }), 200);
              return;
            }
            emitClick(state, rest, element, via);
          }

          function emitClick(state, rest, element, via) {
            const action = { ...rest, id: state.id, t: state.t, kind: 'click', via };
            record(action);
            lastClick = { element, t: state.t, record: action };
            if (rest.widget === 'tab') setTimeout(() => safe(() => {
              if (element.getAttribute('aria-selected') === 'true') effect('tabSelected', {}, state.id);
            }), 300);
          }

          // The field shows the choice once its list has closed.
          function settleChoice(id, owner, option) {
            const started = now();
            const poll = setInterval(() => safe(() => {
              const listbox = document.getElementById(owner.getAttribute('aria-controls') || '');
              const open = listbox && visible(listbox);
              if (open && now() - started < 1500) return;
              clearInterval(poll);
              const shown = isAutocomplete(owner) || owner.tagName === 'SELECT' ? owner.value : owner.innerText || owner.textContent;
              effect('settled', { value: normalizeValue(normalize(shown)), raw: shown, closed: !open }, id);
            }), 50);
          }

          addEventListener('pointerdown', (event) => safe(() => {
            if (!event.isTrusted || event.button !== 0) return;
            lastPointer = now();
            flushTyping();
            const hit = deepTarget(event);
            if (!hit) return;
            const draft = draftFor(hit);
            if (draft.ignore) { pending = null; return; }
            pending = { id: newId(), t: now(), x: event.clientX, y: event.clientY, draft, hit, detail: event.detail };
            remember(pending.id);
          }), { capture: true, passive: true });

          addEventListener('pointerup', (event) => safe(() => {
            if (!event.isTrusted || !pending) return;
            const state = pending;
            if (Math.hypot(event.clientX - state.x, event.clientY - state.y) > 8) {
              pending = null;
              state.done = true;
              const drop = deepTarget(event);
              const { element, adornmentField, owner, ...rest } = state.draft;
              record({ ...rest, id: state.id, t: state.t, kind: 'drag', to: drop ? describe(retarget(drop)).target : null });
              return;
            }
            // A MUI Select opens on mousedown and its menu then takes the click: without a click on the element, the press
            // itself was the action.
            setTimeout(() => safe(() => { if (pending === state) pending = null; finish(state, 'pointer'); }), 80);
          }), { capture: true, passive: true });

          addEventListener('pointercancel', () => safe(() => { pending = null; }), { capture: true, passive: true });
          addEventListener('dragstart', (event) => safe(() => {
            if (!event.isTrusted || !pending) return;
            const state = pending;
            pending = null;
            state.done = true;
            const { element, adornmentField, owner, ...rest } = state.draft;
            record({ ...rest, id: state.id, t: state.t, kind: 'drag', to: null });
          }), true);

          addEventListener('click', (event) => safe(() => {
            if (!event.isTrusted) return;
            const target = deepTarget(event);
            if (!target) return;
            const state = pending;
            if (state && !state.done) {
              const element = state.draft.element;
              const related = target === element || element.contains(target) || target.contains(state.hit) ||
                state.hit.contains(target);
              if (!related) return;
              if (state.draft.kind === 'toggle' && target !== element && target.closest('label')) {
                // The browser clicks the control next: its state is known then.
                return;
              }
              pending = null;
              finish(state, 'pointer');
              return;
            }
            // A click from the keyboard (Enter or Space on a button, Space on a checkbox).
            if (event.detail !== 0 || now() - lastPointer < 400 || now() - lastEnter < 150) return;
            const draft = draftFor(target);
            if (draft.ignore) return;
            const keyboard = { id: newId(), t: now(), draft, hit: target, detail: 0 };
            remember(keyboard.id);
            finish(keyboard, 'keyboard');
          }), true);

          addEventListener('dblclick', (event) => safe(() => {
            if (!event.isTrusted || !lastClick) return;
            const hit = deepTarget(event);
            if ((pickedFor(hit) ?? retarget(hit)) !== lastClick.element) return;
            record({ ...lastClick.record, kind: 'dblclick' });
            lastClick = null;
          }), true);

          // ------------------------------------------------------------------------------------------------- overlay
          // A box follows the pointer over the element a click would record, labelled with the locator the flow would use
          // and why the ones tried before it lost, as the recorder's own policy answers through window.__qaasExplain. It is
          // drawn in a closed shadow root, which neither the page nor Playwright's selectors see; it never takes the pointer
          // or the focus, and takes no room. F9 hides or shows it. While it shows, Alt+Up picks the element around the one in
          // the box and Alt+Down goes back toward the element under the pointer; a click then records the element picked.
          // Moving to another element drops the pick.
          const OVERLAY_CSS = `
            :host { all: initial !important; display: block !important; position: fixed !important; top: 0 !important;
              left: 0 !important; width: 0 !important; height: 0 !important; z-index: 2147483647 !important;
              pointer-events: none !important; }
            .box, .label { position: fixed; display: none; box-sizing: border-box; pointer-events: none; }
            .box { border: 2px solid #1a73e8; border-radius: 2px; background: rgba(26, 115, 232, .12);
              box-shadow: 0 0 0 1px rgba(255, 255, 255, .9); }
            .box.picked { border-color: #e37400; background: rgba(227, 116, 0, .14); }
            .label { max-width: min(560px, calc(100vw - 8px)); padding: 4px 8px; border-radius: 4px;
              background: rgba(24, 24, 27, .95); color: #d4d4d8; direction: ltr; unicode-bidi: isolate; text-align: left;
              font: 11px/1.45 system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif; overflow-wrap: anywhere;
              box-shadow: 0 2px 6px rgba(0, 0, 0, .35), 0 0 0 1px rgba(255, 255, 255, .25); }
            .locator, .reasons { font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, 'DejaVu Sans Mono', monospace; }
            .locator { font-size: 12px; font-weight: 600; color: #fff; }
            .pick, .problem { color: #fbbf24; }
            .reasons, .note { color: #a1a1aa; }`;
          // Each quoted name keeps its own direction, so a right-to-left name cannot reorder the rest of the line.
          const isolateQuoted = (text) => text.replace(/"[^"]*"/g, (quoted) => range(0x2068) + quoted + range(0x2069));
          let overlayOn = true;
          let view = null;
          // What the pointer is over: the element hit, the one a click there records, the one picked instead with the
          // Alt+arrows, the one shown, and the recorder's answer for it.
          let hover = null;
          let explaining = false;
          let explainAgain = false;
          let explainTimer = 0;

          function overlayView() {
            if (view?.host.isConnected) return view;
            const host = document.createElement('qaas-recorder-overlay');
            const root = attachShadow.call(host, { mode: 'closed' });
            const sheet = new CSSStyleSheet();
            sheet.replaceSync(OVERLAY_CSS);
            root.adoptedStyleSheets = [sheet];
            const part = (parent, name) => {
              const element = parent.appendChild(document.createElement('div'));
              element.className = name;
              element.setAttribute('aria-hidden', 'true');
              return element;
            };
            const box = part(root, 'box');
            const label = part(root, 'label');
            view = { host, box, label, pick: part(label, 'pick'), locator: part(label, 'locator'),
              problem: part(label, 'problem'), reasons: part(label, 'reasons'), note: part(label, 'note') };
            document.documentElement.appendChild(host);
            return view;
          }

          function hideOverlay() {
            if (!view) return;
            view.box.style.display = 'none';
            view.label.style.display = 'none';
          }

          // The box over the element, and the label below it, or above it when there is no room below.
          function drawOverlay() {
            const element = hover?.shown;
            if (!overlayOn || !element?.isConnected) { hideOverlay(); return; }
            const rect = element.getBoundingClientRect();
            if (!rect.width && !rect.height) { hideOverlay(); return; }
            const { box, label } = overlayView();
            box.classList.toggle('picked', !!hover.picked);
            Object.assign(box.style, { display: 'block', left: `${rect.left}px`, top: `${rect.top}px`,
              width: `${rect.width}px`, height: `${rect.height}px` });
            if (!hover.answer) { label.style.display = 'none'; return; }
            label.style.display = 'block';
            const size = label.getBoundingClientRect();
            const below = rect.bottom + 4;
            const above = rect.top - size.height - 4;
            const top = below + size.height <= innerHeight ? below : above >= 0 ? above : innerHeight - size.height - 4;
            const left = Math.min(Math.max(4, rect.left), innerWidth - size.width - 4);
            Object.assign(label.style, { left: `${Math.max(4, left)}px`, top: `${Math.max(4, top)}px` });
          }

          function fillLabel(answer, picked) {
            const parts = overlayView();
            const line = (part, text) => { part.textContent = text || ''; part.style.display = text ? 'block' : 'none'; };
            line(parts.pick, picked && `picked: ${tagOf(picked)}  (Alt+${range(0x2191)} ${range(0x2193)})`);
            line(parts.locator, answer.locator ? isolateQuoted(answer.locator) : 'nothing finds it');
            line(parts.problem, answer.problem);
            line(parts.reasons, (answer.reasons ?? []).join('  ·  '));
            line(parts.note, answer.note);
          }

          const tagOf = (element) => element.tagName.toLowerCase() + (element.id ? `#${element.id}`
            : typeof element.className === 'string' && element.className.trim() ? `.${element.className.trim().split(/\s+/)[0]}` : '');
          const hoverOver = (hit) => {
            const element = retarget(hit);
            return { hit, element, picked: null, shown: element.closest('[role=option]') ?? element, answer: null, at: 0 };
          };

          // The element picked with the Alt+arrows, while the pointer is still on what it was picked from.
          const pickedFor = (hit) => (overlayOn && hover?.hit === hit && hover.picked) || null;

          // Steps the pick through the elements from the one under the pointer up to the body: up by Alt+Up, down by Alt+Down.
          function pick(step) {
            const state = hover;
            if (!state) return;
            if (!state.chain) {
              state.chain = [];
              for (let node = state.element.contains(state.hit) ? state.hit : state.element;
                   node && node !== document.body && node !== document.documentElement; node = node.parentElement)
                state.chain.push(node);
              state.index = state.chain.indexOf(state.element);
            }
            const index = Math.min(Math.max(state.index + step, 0), state.chain.length - 1);
            if (index === state.index || index < 0) return;
            state.index = index;
            state.picked = state.chain[index] === state.element ? null : state.chain[index];
            state.shown = state.picked ?? state.element.closest('[role=option]') ?? state.element;
            state.answer = null;
            drawOverlay();
            scheduleExplain();
          }

          function scheduleExplain() {
            if (!explainTimer) explainTimer = setTimeout(() => { explainTimer = 0; safe(explain); }, 80);
          }

          // Asks the recorder what a click on the element records: one question at a time, about the latest element.
          function explain() {
            if (explaining) { explainAgain = true; return; }
            const state = hover;
            if (!overlayOn || !state?.shown.isConnected || typeof window.__qaasExplain !== 'function') return;
            explaining = true;
            state.at = now();
            const { shown, picked } = state;
            const option = !picked && shown !== state.element;
            const { target } = describe(shown, picked ? { interactive: true } : option ? { pageLevel: true, noRow: true } : {});
            const byName = shown.matches(TOGGLES) && roleOf(shown) === 'radio';
            Promise.resolve(window.__qaasExplain(JSON.stringify({ target, byName })))
              .then((reply) => safe(() => {
                if (hover !== state || !reply) return;
                state.answer = JSON.parse(reply);
                fillLabel(state.answer, picked);
                drawOverlay();
              }))
              .catch(() => { /* the recorder is gone */ })
              .finally(() => {
                explaining = false;
                if (explainAgain) { explainAgain = false; scheduleExplain(); }
              });
          }

          addEventListener('pointermove', (event) => safe(() => {
            if (!event.isTrusted || !overlayOn) return;
            const hit = deepTarget(event);
            if (hit === hover?.hit) {
              // The page may have changed under a pointer at rest.
              if (now() - hover.at > 1500) scheduleExplain();
              return;
            }
            hover = hit && hit !== document.body && hit !== document.documentElement && !hit.matches('iframe, frame')
              ? hoverOver(hit) : null;
            drawOverlay();
            if (hover) scheduleExplain();
          }), { capture: true, passive: true });
          addEventListener('pointerout', (event) => safe(() => {
            // Off the page, or into a frame, which records nothing.
            if (event.isTrusted && !event.relatedTarget) { hover = null; hideOverlay(); }
          }), { capture: true, passive: true });
          addEventListener('scroll', () => safe(drawOverlay), { capture: true, passive: true });
          addEventListener('resize', () => safe(drawOverlay), { passive: true });
          addEventListener('keydown', (event) => safe(() => {
            if (!event.isTrusted || event.ctrlKey || event.metaKey || event.shiftKey) return;
            if (event.key === 'F9' && !event.altKey && !event.repeat) {
              overlayOn = !overlayOn;
              hover = null;
              hideOverlay();
            } else if (overlayOn && event.altKey && (event.key === 'ArrowUp' || event.key === 'ArrowDown')) {
              pick(event.key === 'ArrowUp' ? 1 : -1);
            }
          }), { capture: true, passive: true });

          // ------------------------------------------------------------------------------------------------ keyboard
          const activeElement = () => {
            let element = document.activeElement;
            while (element?.shadowRoot?.activeElement) element = element.shadowRoot.activeElement;
            return element;
          };
          addEventListener('keydown', (event) => safe(() => {
            if (!event.isTrusted || (event.key !== 'Enter' && event.key !== 'Escape')) return;
            const element = activeElement();
            if (event.key === 'Enter' && element?.matches?.('[role=option]')) {
              flushTyping();
              const draft = draftFor(element);
              const state = { id: newId(), t: now(), draft, hit: element, detail: 0 };
              remember(state.id);
              finish(state, 'keyboard');
              return;
            }
            if (event.key === 'Enter' && !(element && isTextual(element) && element.tagName !== 'TEXTAREA')) return;
            flushTyping();
            const id = newId();
            remember(id);
            if (event.key === 'Enter') lastEnter = now();
            const onPage = !element || element === document.body || element === document.documentElement;
            record({ ...(onPage ? {} : describe(element)), id, t: now(), kind: 'press', key: event.key, onPage });
          }), true);

          // ---------------------------------------------------------------------------------------- selects and files
          addEventListener('change', (event) => safe(() => {
            if (!event.isTrusted) return;
            const element = deepTarget(event);
            if (!element) return;
            if (element.matches('input[type=file]')) {
              flushTyping();
              const id = newId();
              remember(id);
              record({ ...describe(element), id, t: now(), kind: 'upload', files: [...element.files].map((file) => file.name) });
            } else if (element.tagName === 'SELECT') {
              flushTyping();
              const id = newId();
              remember(id);
              const labels = [...element.selectedOptions].map((option) => clean(option.label || option.textContent));
              const info = describe(element);
              record({ ...info, id, t: now(), kind: 'choose', widget: 'native', field: info.target, value: labels.join(', '),
                values: labels });
            }
          }), true);

          // ----------------------------------------------------------------------------------------------- effects
          // What the page shows changes a moment after an action: a dialog opens or closes, an alert appears.
          const hoverMenus = new WeakSet();
          let shown = { dialogs: new Map(), alerts: new Map(), menus: new Set() };
          function snapshot() {
            const dialogs = new Map();
            for (const dialog of openDialogs()) dialogs.set(dialog, null);
            const alerts = new Map();
            for (const alert of document.querySelectorAll('[role=alert]'))
              if (visible(alert)) alerts.set(alert, normalize(alert.innerText || alert.textContent));
            const menus = new Set([...document.querySelectorAll('[role=menu]')].filter(visible));
            return { dialogs, alerts, menus };
          }
          setInterval(() => safe(() => {
            if (!document.body) return;
            const next = snapshot();
            for (const dialog of next.dialogs.keys()) {
              if (shown.dialogs.has(dialog)) { next.dialogs.set(dialog, shown.dialogs.get(dialog)); continue; }
              const info = describeDialog(dialog);
              next.dialogs.set(dialog, info);
              effect('dialogOpened', { dialog: info });
            }
            for (const [dialog, info] of shown.dialogs)
              if (!next.dialogs.has(dialog)) effect('dialogClosed', { dialog: info ?? { title: '' } });
            const alertCount = next.alerts.size;
            for (const [alert, text] of next.alerts)
              if (shown.alerts.get(alert) !== text && text) effect('alert', { text: normalizeValue(text), alertCount });
            for (const menu of next.menus)
              if (!shown.menus.has(menu) && now() - lastPointer > 1000) hoverMenus.add(menu);
            shown = next;
          }), 100);
        })();
        """;
}
