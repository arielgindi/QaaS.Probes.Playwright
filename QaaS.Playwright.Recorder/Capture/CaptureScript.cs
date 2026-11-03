namespace QaaS.Playwright.Recorder.Capture;

/// <summary>
/// The script the recorder installs in every page it records. It watches what the person does, without changing
/// it, and reports each action through the binding <c>__qaasRecord</c>: every way to find the element, each checked
/// with Playwright's own engine (its console API, which a driver in debug mode "console" installs), the dialog it is
/// in, the value a field settled on, and what the page did a moment later. Through <c>__qaasControl</c> it learns
/// whether to record (F8 pauses), and hands over the page's elements frozen (F4); <c>window.__qaasRecorder</c> answers
/// the recorder's panel about the page's elements, and in inspect mode (Ctrl+Shift+C) a click picks one for it instead.
/// In check mode (F6) a click picks an element to check instead, and a chooser in the overlay reports the check the
/// person chose as an action of its own. While the person tries things (F7) it reports their actions as when recording,
/// for the recorder to tell them apart; while they record a part again, F2 tells the recorder it is done.
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
          // recorder through window.__qaasRecord as JSON. Only in check mode (F6), which the person turns on to pick what to
          // check, are clicks and keys kept from the page.
          if (window.__qaasCaptureInstalled || window !== window.top) return;
          window.__qaasCaptureInstalled = true;

          const TEST_ID = '__TEST_ID_ATTRIBUTE__';
          const INTERACTIVE = 'button, a[href], input, textarea, select, tr, [role=button], [role=link], [role=checkbox], ' +
            '[role=radio], [role=switch], [role=tab], [role=menuitem], [role=menuitemcheckbox], [role=menuitemradio], ' +
            '[role=option], [role=combobox], [role=gridcell], [role=row], [contenteditable=""], [contenteditable=true]';
          // A row or a grid's cell is interactive as a whole, but what looks clickable inside it is the target.
          const CONTAINERS = 'tr, [role=row], [role=gridcell]';
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

          // Whether the person's actions are recorded: 'recording', 'paused' (F8, or the panel), 'login' while a login
          // flow runs, 'testing' while the panel tests the flow in a window of its own or replays it here, or 'trying'
          // (F7), where they are reported as when recording, and the recorder keeps them apart. The recorder decides, and
          // tells each page; a page that loads asks. It also names the part of the flow being recorded again, if any.
          let state = 'recording';
          // While a login flow runs, the pill says how it goes, as the recorder tells each second; once it is slow, Esc
          // stops it, for the person to log in by hand.
          let loginText = '';
          let loginHint = '';
          let loginEscape = false;
          let part = null;
          // In check mode a click picks what to check: nothing the person does is an action, while what the page does
          // still belongs to the action before.
          let checking = false;
          // In inspect mode (Ctrl+Shift+C) a click picks an element for the panel's Elements view, and is not an action.
          let inspecting = false;
          const recording = () => (state === 'recording' || state === 'trying') && !checking && !inspecting;
          // The recorder's bindings are called without waiting for an answer, which it gives through
          // window.__qaasRecorder: another driver attached to the browser answers each call it does not know with an error.
          const call = (binding, message) => safe(() => {
            Promise.resolve(window[binding]?.(JSON.stringify(message))).catch(() => { /* answered elsewhere, or gone */ });
          });
          const send = (message) => { if (state === 'recording' || state === 'trying') call('__qaasRecord', message); };
          const control = (message) => call('__qaasControl', message);
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

          // What looks clickable without being a control: the pointer turns into a hand over it and not over its parent, it
          // has an onclick attribute, or the Tab key stops on it. A focusable area that scrolls is for the keyboard.
          function looksClickable(element) {
            if (element === document.body || element === document.documentElement) return false;
            if (element.hasAttribute('onclick')) return true;
            const style = styleOf(element);
            if (!style) return false;
            if (style.cursor === 'pointer' && styleOf(element.parentElement)?.cursor !== 'pointer') return true;
            if (!element.hasAttribute('tabindex') || element.tabIndex < 0 || element.matches(DIALOGS)) return false;
            return !(/auto|scroll/.test(style.overflowX + style.overflowY) &&
              (element.scrollHeight > element.clientHeight || element.scrollWidth > element.clientWidth));
          }

          // The element a person meant: the control around what the pointer hit (for a label of a checkbox, radio or
          // switch, the control itself), else the outermost element below the next row or cell that looks clickable, else
          // the row or cell, else what was hit.
          function retarget(hit) {
            const interactive = hit.closest(INTERACTIVE);
            const label = hit.closest('label');
            if (label && (!interactive || interactive.contains(label))) {
              const control = controlOfLabel(label);
              if (control) return control;
            }
            if (interactive && !interactive.matches(CONTAINERS)) return interactive;
            let clickable = null;
            for (let node = hit; node && node !== interactive; node = node.parentElement)
              if (looksClickable(node)) clickable = node;
            return clickable ?? interactive ?? hit;
          }

          // The only element child of a wrapper with (nearly) the same box, as a carousel wraps each slide's content.
          function onlyChild(element) {
            const child = element.children.length === 1 ? element.firstElementChild : null;
            if (!child || [...element.childNodes].some((node) => node.nodeType === 3 && node.textContent.trim())) return null;
            const outer = element.getBoundingClientRect();
            const inner = child.getBoundingClientRect();
            return ['left', 'top', 'right', 'bottom'].every((side) => Math.abs(outer[side] - inner[side]) <= 8) ? child : null;
          }

          // The elements a click on the target hits as well that have an id or a test id: those between it and what the
          // pointer hit, outermost first, then a wrapper's only child. The recorder locates the click by one of them when
          // only Playwright's own locator finds the target.
          function equivalentsOf(hit, element) {
            const found = [];
            if (element.contains(hit)) for (let node = hit; node !== element; node = node.parentElement) found.unshift(node);
            for (let child = onlyChild(element); child; child = onlyChild(child)) if (!found.includes(child)) found.push(child);
            return found.filter((node) => node.id || node.getAttribute(TEST_ID));
          }
          // Each is the same click: when the target is a control, so are they.
          const describeEquivalents = (elements, target) =>
            elements.map((other) => describe(other, target.interactive ? { interactive: true } : {}).target);

          // What the element is beyond its role, for the recorder to write it.
          const widgetOf = (element) => element.matches(TOGGLES)
            ? (roleOf(element) === 'radio' ? 'radio' : roleOf(element) === 'switch' ? 'switch' : 'checkbox')
            : isMuiSelect(element) ? 'select-open'
            : element.closest('.MuiAutocomplete-root') && element.tagName === 'BUTTON' ? 'autocomplete-button'
            : roleOf(element) === 'tab' ? 'tab'
            : roleOf(element) === 'menuitem' ? 'menuitem'
            : null;

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
            if (generated) candidates.push({ kind: 'generator', csharp: generated, selector: safe(() => engine().selector(element), null), count: 1, matches: true });
            return {
              tag: tag.toLowerCase(), role, type: element.getAttribute('type'), id: element.id || null,
              name: nameAttribute, testId, value: element.matches('input[type=radio], input[type=checkbox]') ? element.value : null,
              text: roleText ?? clean(accessibleName(element)),
              label: clean(idRefs(element, 'aria-labelledby').filter((ref) => ref !== element).map((ref) => ref.textContent).join(' ')) ||
                clean([...(element.labels || [])].map((label) => label.textContent).join(' ')) || null,
              interactive: options.interactive ??
                (element.matches(INTERACTIVE) || element.matches(TOGGLES) || looksClickable(element)),
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
            if (!event.isTrusted || !recording()) return;
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
            const widget = widgetOf(element);
            const field = widget === 'autocomplete-button'
              ? describe(element.closest('.MuiAutocomplete-root').querySelector('input')).target : null;
            const equivalents = picked || widget ? [] : describeEquivalents(equivalentsOf(hit, element), info.target);
            return { element, kind: toggle ? 'toggle' : 'click', widget, ...info, field,
              ...(equivalents.length ? { equivalents } : {}),
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
            if (!recording()) { pending = null; return; }
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
            if (!recording() || event.detail !== 0 || now() - lastPointer < 400 || now() - lastEnter < 150) return;
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
          // Moving to another element drops the pick. While the recording is paused, a pill at the top says so instead,
          // and while a login flow runs, how it goes.
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
            .reasons, .note { color: #a1a1aa; }
            .state { position: fixed; display: none; top: 12px; left: 50%; transform: translateX(-50%); box-sizing: border-box;
              align-items: center; gap: 8px; padding: 6px 12px 6px 10px; border-radius: 999px; pointer-events: none;
              background: rgba(24, 24, 27, .94); color: #f4f4f5; direction: ltr; white-space: nowrap;
              font: 500 12px/1.4 system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif;
              box-shadow: 0 4px 14px rgba(0, 0, 0, .35), 0 0 0 1px rgba(255, 255, 255, .18); }
            .state.shown { display: flex; }
            .state::before { content: ''; width: 8px; height: 8px; border-radius: 2px; background: #fbbf24; }
            .state.note::before { border-radius: 50%; background: #60a5fa; }
            .state span { color: #a1a1aa; font-weight: 400; }
            .box.check { border-color: #16a34a; background: rgba(22, 163, 74, .12); }
            .chooser, .bar { --canvas: #f7f8fa; --panel: #ffffff; --raised: #f2f4f7; --line: #e3e6eb; --strong: #d3d8df;
              --fg: #16181d; --muted: #5a626e; --faint: #8a919c; --accent: #5157e6; --ok: #17824f;
              position: fixed; box-sizing: border-box; pointer-events: auto; direction: ltr; text-align: left;
              color: var(--fg); background: var(--panel); border: 1px solid var(--strong);
              font: 13px/1.45 Inter, 'Segoe UI Variable Text', 'Segoe UI', system-ui, -apple-system, Roboto, sans-serif;
              box-shadow: 0 14px 36px rgba(15, 18, 28, .22), 0 2px 6px rgba(15, 18, 28, .14); }
            @media (prefers-color-scheme: dark) {
              .chooser, .bar { --canvas: #0c0d10; --panel: #181b20; --raised: #22262c; --line: #2a2f36; --strong: #3a4049;
                --fg: #e8eaed; --muted: #9aa1ab; --faint: #6c737e; --accent: #7c82ff; --ok: #4cc38a;
                box-shadow: 0 14px 36px rgba(0, 0, 0, .5), 0 0 0 1px rgba(255, 255, 255, .04); }
            }
            .bar { display: none; top: 12px; left: 50%; transform: translateX(-50%); align-items: center; gap: 10px;
              padding: 5px 6px 5px 12px; border-radius: 999px; white-space: nowrap; font-size: 12.5px; }
            .bar.shown { display: flex; }
            .bar b { display: inline-flex; align-items: center; gap: 7px; font-weight: 600; }
            .bar b::before { content: ''; width: 8px; height: 8px; border-radius: 50%; background: var(--ok);
              box-shadow: 0 0 0 3px color-mix(in oklab, var(--ok) 22%, transparent); }
            .bar .hint { color: var(--muted); }
            .chooser button, .bar button { font: inherit; color: inherit; cursor: pointer; }
            .bar .url { height: 26px; padding: 0 10px; border-radius: 999px; border: 1px solid var(--strong);
              background: var(--raised); font-size: 12px; font-weight: 500; }
            .bar .url:hover { border-color: var(--faint); }
            kbd { display: inline-block; padding: 0 5px; border-radius: 4px; border: 1px solid var(--strong);
              background: var(--canvas); color: var(--muted); font: 500 10.5px/16px inherit; }
            .chooser { width: 348px; max-width: calc(100vw - 16px); border-radius: 12px; overflow: hidden; }
            .chooser .head { display: flex; align-items: center; gap: 8px; padding: 10px 12px 9px;
              border-bottom: 1px solid var(--line); }
            .chooser .badge { display: inline-flex; align-items: center; gap: 5px; flex: none; padding: 1px 8px 1px 6px;
              border-radius: 999px; font-size: 11.5px; font-weight: 600; color: var(--ok);
              background: color-mix(in oklab, var(--ok) 14%, transparent); }
            .chooser .badge svg { width: 12px; height: 12px; }
            .chooser .what { min-width: 0; flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap;
              color: var(--muted); font: 11.5px/1.4 ui-monospace, 'JetBrains Mono', 'Cascadia Code', Menlo, Consolas, monospace; }
            .chooser .options { padding: 4px; max-height: 296px; overflow-y: auto; }
            .chooser .option { display: flex; align-items: center; gap: 9px; width: 100%; padding: 6px 8px;
              border: 0; border-radius: 8px; background: transparent; text-align: left; }
            .chooser .option:hover { background: var(--raised); }
            .chooser .option.on { background: color-mix(in oklab, var(--accent) 13%, transparent); }
            .chooser .radio { display: grid; place-items: center; flex: none; width: 14px; height: 14px; box-sizing: border-box;
              border-radius: 50%; border: 1.5px solid var(--strong); }
            .chooser .on .radio { border-color: var(--accent); background: var(--accent); }
            .chooser .on .radio::after { content: ''; width: 6px; height: 6px; border-radius: 50%; background: #fff; }
            .chooser .name { flex: none; font-weight: 500; }
            .chooser .value { min-width: 0; flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap;
              text-align: right; color: var(--muted); font-size: 12px; unicode-bidi: plaintext; }
            .chooser .now { flex: none; padding: 0 5px; border-radius: 4px; border: 1px solid var(--line);
              color: var(--faint); font-size: 10.5px; line-height: 16px; }
            .chooser .field { padding: 6px 12px 2px; }
            .chooser .field input { width: 100%; height: 30px; box-sizing: border-box; padding: 0 9px; border-radius: 8px;
              border: 1px solid var(--strong); background: var(--canvas); color: var(--fg); font: inherit; outline: none;
              unicode-bidi: plaintext; }
            .chooser .field input:focus { border-color: var(--accent);
              box-shadow: 0 0 0 3px color-mix(in oklab, var(--accent) 22%, transparent); }
            .chooser .cells { display: flex; flex-wrap: wrap; gap: 5px; padding: 6px 12px 2px; }
            .chooser .cell { max-width: 100%; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; padding: 2px 9px;
              border-radius: 999px; border: 1px solid var(--strong); background: transparent; color: var(--muted);
              font-size: 12px; unicode-bidi: plaintext; }
            .chooser .cell.on { border-color: color-mix(in oklab, var(--accent) 55%, transparent); color: var(--fg);
              background: color-mix(in oklab, var(--accent) 12%, transparent); }
            .chooser .foot { display: flex; align-items: center; gap: 6px; padding: 9px 10px 10px 12px; color: var(--faint);
              font-size: 11.5px; }
            .chooser .add { display: inline-flex; align-items: center; gap: 7px; margin-left: auto; height: 28px;
              padding: 0 10px; border: 0; border-radius: 8px; background: var(--accent); color: #fff; font-size: 12px;
              font-weight: 600; }
            .chooser .add kbd { border: 0; background: rgba(255, 255, 255, .2); color: #fff; }
            .chooser .add:disabled { opacity: .45; cursor: default; }`;
          // Each quoted name keeps its own direction, so a right-to-left name cannot reorder the rest of the line.
          const isolateQuoted = (text) => text.replace(/"[^"]*"/g, (quoted) => range(0x2068) + quoted + range(0x2069));
          let overlayOn = true;
          let view = null;
          // What the pointer is over: the element hit, the one a click there targets, the one picked instead with the
          // Alt+arrows, the one described, the one the recorder records it on (boxed), and the recorder's answer.
          let hover = null;
          let explaining = null;
          let explainAgain = false;
          let explainTimer = 0;
          let explainSeq = 0;

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
            view = { host, root, box, label, pick: part(label, 'pick'), locator: part(label, 'locator'),
              problem: part(label, 'problem'), reasons: part(label, 'reasons'), note: part(label, 'note'),
              state: part(root, 'state'), bar: part(root, 'bar') };
            // What is done in the chooser and the bar stays in them: the page sees none of it.
            for (const type of ['pointerdown', 'mousedown', 'pointerup', 'mouseup', 'click', 'dblclick', 'keydown', 'keyup', 'input'])
              root.addEventListener(type, (event) => event.stopPropagation());
            document.documentElement.appendChild(host);
            return view;
          }

          function hideOverlay() {
            if (!view) return;
            view.box.style.display = 'none';
            view.label.style.display = 'none';
          }

          // The pill at the top: the recording is paused, or, for a moment, what a key did.
          let pillTimer = 0;
          function showPill(text, hint, note, keep) {
            clearTimeout(pillTimer);
            const { state: pill } = overlayView();
            pill.className = `state shown${note ? ' note' : ''}`;
            pill.textContent = text;
            const span = pill.appendChild(document.createElement('span'));
            span.textContent = hint;
            if (note && !keep) pillTimer = setTimeout(() => safe(showState), 2500);
          }
          function showState() {
            clearTimeout(pillTimer);
            if (state === 'paused') showPill('Recording paused', 'F8 resumes');
            else if (state === 'testing') showPill('Testing the flow', part ? 'replaying it here; nothing is recorded' : 'in its own window; nothing is recorded', true, true);
            else if (state === 'login' && loginText) showPill(loginText, loginHint, true, true);
            else if (state === 'trying') showPill('Trying: nothing is recorded', 'F7 records again', true, true);
            else if (state === 'recording' && part) showPill(part, 'F2 when done', true, true);
            else if (view) view.state.className = 'state';
          }
          function applyState(next, nextPart) {
            if (!['recording', 'paused', 'login', 'testing', 'trying'].includes(next)) return;
            const changed = next !== state;
            part = nextPart || null;
            if (!changed) { showState(); return; }
            if (next !== 'recording') stopChecking();
            // What is being typed belongs to the state it was typed in.
            flushTyping();
            state = next;
            if (!recording()) {
              // What the page does next is no earlier action's doing.
              pending = null;
              lastAction = null;
              hover = null;
              hideOverlay();
            }
            showState();
          }

          // The box over the element, and the label below it, or above it when there is no room below.
          function drawOverlay() {
            const element = hover?.boxed ?? hover?.shown;
            if (!(overlayOn || checking) || !element?.isConnected) { hideOverlay(); return; }
            const rect = element.getBoundingClientRect();
            if (!rect.width && !rect.height) { hideOverlay(); return; }
            const { box, label } = overlayView();
            box.classList.toggle('picked', !!hover.picked);
            box.classList.toggle('check', checking);
            Object.assign(box.style, { display: 'block', left: `${rect.left}px`, top: `${rect.top}px`,
              width: `${rect.width}px`, height: `${rect.height}px` });
            if (!hover.answer || chooser) { label.style.display = 'none'; return; }
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
            line(parts.note, checking ? 'click to check it  ·  Esc leaves check mode' : answer.note);
          }

          const tagOf = (element) => element.tagName.toLowerCase() + (element.id ? `#${element.id}`
            : typeof element.className === 'string' && element.className.trim() ? `.${element.className.trim().split(/\s+/)[0]}` : '');
          const hoverOver = (hit) => {
            const element = checking ? checkTarget(hit) : retarget(hit);
            return { hit, element, picked: null, shown: checking ? element : element.closest('[role=option]') ?? element,
              boxed: null, answer: null, at: 0 };
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
            state.boxed = null;
            state.answer = null;
            drawOverlay();
            scheduleExplain();
          }

          function scheduleExplain() {
            if (!explainTimer) explainTimer = setTimeout(() => { explainTimer = 0; safe(explain); }, 80);
          }

          // Asks the recorder what a click on the element records: one question at a time, about the latest element. The
          // answer comes through window.__qaasRecorder, as explained().
          function explain() {
            if (explaining && now() - explaining.at < 3000) { explainAgain = true; return; }
            const state = hover;
            if (!(overlayOn && recording()) && !checking || !state?.shown.isConnected
              || typeof window.__qaasExplain !== 'function') return;
            state.at = now();
            const { shown, picked } = state;
            const option = !picked && shown !== state.element;
            const { target } = describe(shown, picked ? { interactive: true } : option ? { pageLevel: true, noRow: true } : {});
            const widget = widgetOf(shown);
            const others = picked || option || widget || checking ? [] : equivalentsOf(state.hit, shown);
            const equivalents = describeEquivalents(others, target);
            explaining = { seq: ++explainSeq, at: now(), state, shown, picked, others };
            call('__qaasExplain', { seq: explaining.seq, target, equivalents, byName: widget === 'radio' });
          }
          function explained(seq, reply) {
            const asked = explaining;
            if (!asked || asked.seq !== seq) return;
            explaining = null;
            const { state, shown, picked, others } = asked;
            if (hover === state && state.shown === shown && reply) {
              state.answer = JSON.parse(reply);
              state.boxed = others[(state.answer.element ?? 0) - 1] ?? null;
              fillLabel(state.answer, picked);
              if (chooser?.element === shown && state.answer.locator) chooser.what.textContent = state.answer.locator;
              drawOverlay();
            }
            if (explainAgain) { explainAgain = false; scheduleExplain(); }
          }

          addEventListener('pointermove', (event) => safe(() => {
            if (!event.isTrusted || chooser || !(overlayOn && recording()) && !checking) return;
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
            } else if (event.key === 'Escape' && state === 'login' && loginEscape && !event.repeat) {
              control({ type: 'loginByHand' });
            } else if (event.key === 'F8' && !event.altKey && !event.repeat && state !== 'login') {
              // What is being typed belongs before the pause.
              flushTyping();
              control({ type: 'pause' });
            } else if (event.key === 'F4' && !event.altKey && !event.repeat && state !== 'login') {
              freeze();
            } else if (event.key === 'F7' && !event.altKey && !event.repeat && (state === 'recording' || state === 'paused' || state === 'trying')) {
              flushTyping();
              control({ type: 'try' });
            } else if ((overlayOn || checking) && !chooser && event.altKey && (event.key === 'ArrowUp' || event.key === 'ArrowDown')) {
              pick(event.key === 'ArrowUp' ? 1 : -1);
            }
          }), { capture: true, passive: true });

          // While a login flow runs, what it clicks and types in, never the value, for the recorder to show how it goes.
          let loginActed = null;
          const actedInLogin = (action, event) => safe(() => {
            if (state !== 'login' || !event.isTrusted) return;
            const hit = deepTarget(event);
            const element = hit && (action === 'click' ? retarget(hit) : hit);
            if (!element || (action === 'type' && loginActed?.element === element && now() - loginActed.at < 1000)) return;
            loginActed = { element, at: now() };
            control({ type: 'loginAction', action, role: roleOf(element) ?? '', name: clean(accessibleName(element)).slice(0, 60) });
          });
          addEventListener('click', (event) => actedInLogin('click', event), { capture: true, passive: true });
          addEventListener('input', (event) => actedInLogin('type', event), { capture: true, passive: true });

          // F7 would toggle Chrome's caret browsing, with a prompt that takes the focus. F2, while a part of the flow is
          // recorded again, ends it, what is being typed included; else it is the page's.
          addEventListener('keydown', (event) => safe(() => {
            if (!event.isTrusted || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
            if (event.key === 'F7') event.preventDefault();
            if (event.key !== 'F2' || !part || state !== 'recording' || checking) return;
            event.preventDefault();
            event.stopImmediatePropagation();
            if (event.repeat) return;
            flushTyping();
            control({ type: 'done' });
          }), { capture: true });

          // ---------------------------------------------------------------------------------------------- check mode
          // F6, or Check in the recorder's panel, turns check mode on: the box follows the pointer as for a click, in
          // green, and a click picks the element instead of reaching the page. A chooser then offers the checks that fit
          // it, the likeliest chosen: its text, a field's value, a checkbox's state, how many of a repeated item there are,
          // a table row by its cells, whether it shows or is enabled, or the page's address. Enter adds the check, Esc
          // goes back to picking, and Esc again leaves check mode. The check is reported as an action of its own, which
          // the flow makes after the action before it. Clicks and keys meant for the chooser never reach the page.
          const FIELDS = 'input:not([type=checkbox]):not([type=radio]):not([type=button]):not([type=submit]):not([type=reset])' +
            ':not([type=image]):not([type=file]):not([type=hidden]), textarea, select';
          const CONTROLS = 'button, input, select, textarea, a[href], [role=button], [role=checkbox], [role=radio], ' +
            '[role=switch], [role=tab], [role=menuitem], [role=option], [role=combobox], [role=textbox], [aria-disabled]';
          const ITEM_ROLES = ['listitem', 'row', 'option', 'article', 'treeitem', 'menuitem', 'tab'];
          // A tick, drawn without markup, which a page's Trusted Types would refuse.
          function tick() {
            const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
            svg.setAttribute('viewBox', '0 0 24 24');
            const path = svg.appendChild(document.createElementNS('http://www.w3.org/2000/svg', 'path'));
            path.setAttribute('d', 'M5 12.5l4.5 4.5L19 7.5');
            for (const [name, value] of [['fill', 'none'], ['stroke', 'currentColor'], ['stroke-width', '2.6'],
              ['stroke-linecap', 'round'], ['stroke-linejoin', 'round']]) path.setAttribute(name, value);
            return svg;
          }
          let chooser = null;

          // What a check is of: the control around what the pointer is on, else that element itself, as text is checked
          // where it is.
          const checkTarget = (hit) => {
            const control = hit.closest(INTERACTIVE);
            return control && !control.matches(CONTAINERS) ? control : hit;
          };
          const textOf = (element) => normalizeValue(normalize(element.innerText ?? element.textContent ?? ''));
          const isChecked = (element) => element.matches('input') ? element.checked : element.getAttribute('aria-checked') === 'true';
          const isDisabled = (element) => element.disabled === true || element.getAttribute('aria-disabled') === 'true';
          // A class a framework did not generate, which items of a list share.
          const stableClass = (element, other) => [...element.classList].find((name) => other.classList.contains(name) &&
            !/^(css|jss|sc|emotion|styled)-/i.test(name) && !/\d{3,}/.test(name));
          const alike = (a, b) => a.tagName === b.tagName && roleOf(a) === roleOf(b) &&
            (['LI', 'TR', 'OPTION'].includes(a.tagName) || ITEM_ROLES.includes(roleOf(a)) || !!stableClass(a, b));

          // The items the element is one of, the nearest that repeats, and the locators that find them all: by the id or
          // test id of what holds them, or by their role.
          function itemsOf(element) {
            for (let item = element, depth = 0; item && item !== document.body && depth < 6; item = item.parentElement, depth++) {
              const parent = item.parentElement;
              const items = parent ? [...parent.children].filter((child) => alike(child, item) && visible(child)) : [];
              if (items.length < 2) continue;
              const dialog = dialogOf(item);
              const scope = scopeOf(item, dialog ? describeDialog(dialog) : null, dialog);
              const role = item.getAttribute('role');
              const other = items.find((child) => child !== item);
              const one = role ? `[role=${quote(role)}]` : ['LI', 'TR', 'OPTION'].includes(item.tagName)
                ? item.tagName.toLowerCase() : `${item.tagName.toLowerCase()}.${CSS.escape(stableClass(item, other) ?? '')}`;
              const candidates = [];
              const add = (kind, parts, value) => {
                const found = matches([...scope, ...parts]);
                candidates.push({ kind, parts: [...scope, ...parts], count: found ? found.length : -1, value,
                  matches: !!found && found.length === items.length && items.every((each) => found.includes(each)) });
              };
              let path = one;
              for (let holder = parent, up = 0; holder && holder !== document.body && up < 4; holder = holder.parentElement, up++) {
                if (holder.id) add('id', [{ m: 'css', css: `[id=${quote(holder.id)}] > ${path}` }], holder.id);
                const testId = holder.getAttribute(TEST_ID);
                if (testId) add('testid', [{ m: 'testid', value: testId }, { m: 'css', css: `:scope > ${path}` }], testId);
                path = `${holder.tagName.toLowerCase()} > ${path}`;
              }
              if (ITEM_ROLES.includes(roleOf(item)) && roleOf(item) !== 'row') add('role', [{ m: 'role', role: roleOf(item) }]);
              return candidates.some((candidate) => candidate.matches)
                ? { count: items.length, isItem: item === element, group: { tag: item.tagName.toLowerCase(), candidates } }
                : null;
            }
            return null;
          }

          // The cells of the table row the element is in, as the person may choose them to find the row by.
          function cellsOf(element) {
            const row = element.closest('tr, [role=row]');
            if (!row || row.querySelector('th[scope=col], [role=columnheader]')) return null;
            const cells = [...row.querySelectorAll(':scope > td, :scope > th, :scope > [role=cell], :scope > [role=gridcell]')]
              .map(textOf).filter((text) => text && text.length <= 80);
            return cells.length ? { row, cells: [...new Set(cells)].map((text) => ({ text, on: true })) } : null;
          }

          // The checks that fit the element, and the one it most likely needs: a checkbox's state, a field's value, the
          // count of a repeated item, else its text.
          function kindsFor(element) {
            const kinds = [];
            const add = (kind, name, extra = {}) => kinds.push({ kind, name, ...extra });
            const toggle = element.matches(TOGGLES);
            const field = !toggle && element.matches(FIELDS);
            const text = field ? '' : textOf(element);
            if (field) add('value', 'Has value', { value: normalizeValue(element.value ?? ''), edit: true });
            if (text) {
              add('text', 'Has text', { value: text, edit: true });
              add('contains', 'Contains text', { value: text, edit: true });
            }
            if (toggle) {
              add('checked', 'Checked', { now: isChecked(element) });
              add('unchecked', 'Unchecked', { now: !isChecked(element) });
            }
            const items = itemsOf(element);
            if (items) add('count', 'Count', { value: String(items.count), edit: true, group: items.group });
            const cells = cellsOf(element);
            if (cells) add('row', 'Row exists with', { cells });
            add('visible', 'Visible');
            add('hidden', 'Gone', { hint: 'hidden or removed' });
            if (element.matches(CONTROLS)) {
              add('enabled', 'Enabled', { now: !isDisabled(element) });
              add('disabled', 'Disabled', { now: isDisabled(element) });
            }
            add('url', 'URL is', { value: location.href, shown: location.pathname + location.search + location.hash });
            const likely = toggle ? (isChecked(element) ? 'checked' : 'unchecked') : field ? 'value'
              : items?.isItem ? 'count' : text ? 'text' : 'visible';
            return { kinds, index: Math.max(0, kinds.findIndex((option) => option.kind === likely)) };
          }

          function startChecking() {
            if (checking) return;
            if (state !== 'recording') {
              showPill(state === 'paused' ? 'Recording paused' : state === 'trying' ? 'Trying' : 'Not now',
                state === 'paused' ? 'F8 resumes, then F6 checks' : state === 'trying' ? 'F7 records again, then F6 checks' : 'the recording is busy', true);
              return;
            }
            flushTyping();
            pending = null;
            checking = true;
            hover = null;
            hideOverlay();
            const { bar } = overlayView();
            bar.replaceChildren();
            const title = bar.appendChild(document.createElement('b'));
            title.textContent = 'Check mode';
            const hint = bar.appendChild(document.createElement('span'));
            hint.className = 'hint';
            hint.textContent = 'Click what the flow should check';
            const url = bar.appendChild(document.createElement('button'));
            url.className = 'url';
            url.type = 'button';
            url.textContent = 'URL is…';
            url.addEventListener('click', () => safe(() => openChooser(null)));
            bar.appendChild(document.createElement('kbd')).textContent = 'Esc';
            bar.classList.add('shown');
            // In its place at the top, the pill of a part being recorded again.
            clearTimeout(pillTimer);
            view.state.className = 'state';
            control({ type: 'checking', on: true });
          }

          function stopChecking() {
            if (!checking) return;
            closeChooser();
            checking = false;
            hover = null;
            hideOverlay();
            view?.bar.classList.remove('shown');
            showState();
            control({ type: 'checking', on: false });
          }

          // The chooser, beside the element, or under the bar for the address.
          function openChooser(element) {
            closeChooser();
            const { kinds, index } = element ? kindsFor(element) : { kinds: [{ kind: 'url', name: 'URL is', value: location.href,
              shown: location.pathname + location.search + location.hash }], index: 0 };
            const box = overlayView().root.appendChild(document.createElement('div'));
            box.className = 'chooser';
            box.setAttribute('role', 'dialog');
            box.setAttribute('aria-label', 'Check');
            chooser = { element, kinds, index, box };
            renderChooser();
            const rect = element?.getBoundingClientRect() ?? { left: innerWidth / 2 - 174, right: innerWidth / 2 + 174, top: 0, bottom: 44 };
            // Below the element, else above it, else beside it, where it covers none of it.
            const size = box.getBoundingClientRect();
            const fit = (top, left) => ({ top: Math.min(Math.max(8, top), innerHeight - size.height - 8),
              left: Math.min(Math.max(8, left), innerWidth - size.width - 8) });
            const place = rect.bottom + 8 + size.height <= innerHeight - 8 ? fit(rect.bottom + 8, rect.left)
              : rect.top - size.height - 8 >= 8 ? fit(rect.top - size.height - 8, rect.left)
              : rect.right + 12 + size.width <= innerWidth - 8 ? fit(rect.top, rect.right + 12)
              : fit(rect.top, rect.left - size.width - 12);
            Object.assign(box.style, { left: `${place.left}px`, top: `${place.top}px` });
            drawOverlay();
          }

          function closeChooser() {
            if (!chooser) return;
            chooser.box.remove();
            chooser = null;
            drawOverlay();
          }

          function renderChooser() {
            const { box, kinds, index, element } = chooser;
            const option = kinds[index];
            const make = (parent, tag, name, text) => {
              const node = parent.appendChild(document.createElement(tag));
              if (name) node.className = name;
              if (text !== undefined) node.textContent = text;
              return node;
            };
            box.replaceChildren();
            const head = make(box, 'div', 'head');
            const badge = make(head, 'span', 'badge');
            badge.append(tick(), 'Check');
            chooser.what = make(head, 'span', 'what', element ? (hover?.answer?.locator ?? tagOf(element)) : 'this page');
            const list = make(box, 'div', 'options');
            list.setAttribute('role', 'listbox');
            kinds.forEach((each, at) => {
              const row = make(list, 'button', `option${at === index ? ' on' : ''}`);
              row.type = 'button';
              row.setAttribute('role', 'option');
              row.setAttribute('aria-selected', String(at === index));
              make(row, 'span', 'radio');
              make(row, 'span', 'name', each.name);
              make(row, 'span', 'value', each.kind === 'row' ? `${each.cells.cells.filter((cell) => cell.on).length} cells`
                : each.shown ?? (each.value === undefined ? each.hint ?? '' : each.kind === 'count' ? each.value : `“${each.value}”`));
              if (each.now) make(row, 'span', 'now', 'now');
              row.addEventListener('click', () => safe(() => { chooser.index = at; renderChooser(); }));
              row.addEventListener('dblclick', () => safe(acceptCheck));
            });
            if (option.edit) {
              const field = make(make(box, 'div', 'field'), 'input');
              field.value = option.value;
              field.setAttribute('aria-label', option.name);
              if (option.kind === 'count') field.inputMode = 'numeric';
              field.addEventListener('input', () => { option.value = field.value; });
              chooser.field = field;
            } else chooser.field = null;
            if (option.kind === 'row') {
              const cells = make(box, 'div', 'cells');
              for (const cell of option.cells.cells) {
                const chip = make(cells, 'button', `cell${cell.on ? ' on' : ''}`, cell.text);
                chip.type = 'button';
                chip.setAttribute('aria-pressed', String(cell.on));
                chip.addEventListener('click', () => safe(() => { cell.on = !cell.on; renderChooser(); }));
              }
            }
            const foot = make(box, 'div', 'foot');
            make(foot, 'kbd', null, '↑↓');
            make(foot, 'span', null, 'choose');
            make(foot, 'kbd', null, 'Esc');
            make(foot, 'span', null, 'back');
            const add = make(foot, 'button', 'add', 'Add check');
            add.type = 'button';
            add.disabled = option.kind === 'row' && !option.cells.cells.some((cell) => cell.on);
            make(add, 'kbd', null, '↵');
            add.addEventListener('click', () => safe(acceptCheck));
          }

          // The check, as an action: the element as a click on it would be located, what to expect of it, and for a count
          // or a row, the locators that find them.
          function acceptCheck() {
            const { element, kinds, index } = chooser;
            const option = kinds[index];
            const value = chooser.field?.value ?? option.value;
            if (option.kind === 'count' && !/^\d+$/.test(value ?? '')) return;
            let group = option.group;
            let described = element && option.kind !== 'url' ? describe(element) : {};
            if (option.kind === 'row') {
              const { row, cells } = option.cells;
              const chosen = cells.filter((cell) => cell.on).map((cell) => cell.text);
              if (!chosen.length) return;
              described = describe(row, { noRow: true });
              const dialog = dialogOf(row);
              const parts = [...scopeOf(row, described.dialog, dialog), { m: 'role', role: 'row' },
                ...chosen.map((text) => ({ m: 'filter', hasText: text }))];
              const found = matches(parts);
              group = { candidates: [{ kind: 'row', parts, count: found ? found.length : -1, matches: !!found && found.includes(row) }] };
            }
            record({ ...described, id: newId(), t: now(), kind: 'expect', check: option.kind, value, group,
              url: location.href });
            const what = option.kind === 'url' ? 'the address' : option.name.toLowerCase();
            stopChecking();
            showPill('Check added', what, true);
          }

          // In check mode, a click picks what to check, and nothing reaches the page but the pointer's moves; keys go to
          // the chooser while it is open.
          for (const type of ['pointerdown', 'mousedown', 'pointerup', 'mouseup', 'click', 'dblclick', 'auxclick', 'contextmenu']) {
            addEventListener(type, (event) => safe(() => {
              if (!checking || !event.isTrusted || event.composedPath()[0] === view?.host) return;
              event.preventDefault();
              event.stopImmediatePropagation();
              if (type !== 'click' || event.button !== 0) return;
              const hit = deepTarget(event);
              if (!hit || hit === document.body || hit === document.documentElement) return;
              const picked = hover?.hit === hit ? hover.picked ?? hover.element : checkTarget(hit);
              if (hover?.hit !== hit) {
                hover = { ...hoverOver(hit), element: picked, shown: picked };
                drawOverlay();
              }
              if (!hover.answer) scheduleExplain();
              // Once the browser has undone what the click would have changed, such as a checkbox's state.
              setTimeout(() => safe(() => openChooser(picked)), 0);
            }), { capture: true });
          }

          addEventListener('keydown', (event) => safe(() => {
            if (!event.isTrusted) return;
            const handled = () => { event.preventDefault(); event.stopImmediatePropagation(); };
            if (event.key === 'F6' && !event.altKey && !event.ctrlKey && !event.metaKey && !event.shiftKey) {
              // Chrome would move the focus to its address bar.
              handled();
              if (!event.repeat) checking ? stopChecking() : startChecking();
              return;
            }
            if (!checking) return;
            if (event.key === 'Escape') {
              handled();
              if (chooser) closeChooser(); else stopChecking();
              return;
            }
            if (!chooser) return;
            if (event.key === 'Enter') { handled(); acceptCheck(); return; }
            if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
              handled();
              chooser.index = (chooser.index + (event.key === 'ArrowDown' ? 1 : chooser.kinds.length - 1)) % chooser.kinds.length;
              renderChooser();
              return;
            }
            // Typing into the chooser's field; the page sees none of it.
            if (document.activeElement === view.host) { event.stopImmediatePropagation(); return; }
            if (event.ctrlKey || event.metaKey || event.altKey) return;
            handled();
            // A key typed at the chooser starts its value afresh.
            if (event.key.length === 1 && chooser.field) {
              chooser.field.focus();
              chooser.field.value = chooser.kinds[chooser.index].value = event.key;
            }
          }), { capture: true });

          // ------------------------------------------------------------------------------------------------ keyboard
          const activeElement = () => {
            let element = document.activeElement;
            while (element?.shadowRoot?.activeElement) element = element.shadowRoot.activeElement;
            return element;
          };
          addEventListener('keydown', (event) => safe(() => {
            if (!event.isTrusted || !recording() || (event.key !== 'Enter' && event.key !== 'Escape')) return;
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
            if (!event.isTrusted || !recording()) return;
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
          // What the page shows changes a moment after an action: a dialog opens or closes, an alert appears, a busy
          // indicator (a progress bar, as MUI's CircularProgress and LinearProgress are, or aria-busy) comes and goes.
          const hoverMenus = new WeakSet();
          const BUSY = '[role=progressbar], [aria-busy=true]';
          const busyKind = (element) => element.matches('[role=progressbar]') ? 'progressbar' : 'ariaBusy';
          const busySince = new WeakMap();
          let shown = { dialogs: new Map(), alerts: new Map(), menus: new Set(), busy: new Set() };
          function snapshot() {
            const dialogs = new Map();
            for (const dialog of openDialogs()) dialogs.set(dialog, null);
            const alerts = new Map();
            for (const alert of document.querySelectorAll('[role=alert]'))
              if (visible(alert)) alerts.set(alert, normalize(alert.innerText || alert.textContent));
            const menus = new Set([...document.querySelectorAll('[role=menu]')].filter(visible));
            const busy = new Set([...document.querySelectorAll(BUSY)].filter(visible));
            return { dialogs, alerts, menus, busy };
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
            // An indicator that came up after an action is its doing, once none of its kind is left.
            for (const element of next.busy)
              if (!shown.busy.has(element) && lastAction && now() - lastAction.t <= 5000)
                busySince.set(element, { actionId: lastAction.id, sinceAction: now() - lastAction.t });
            for (const element of shown.busy) {
              const seen = busySince.get(element);
              if (next.busy.has(element) || !seen) continue;
              busySince.delete(element);
              const kind = busyKind(element);
              if (![...next.busy].some((other) => busyKind(other) === kind))
                effect('busy', { value: kind, sinceAction: seen.sinceAction }, seen.actionId);
            }
            shown = next;
          }), 100);

          // ------------------------------------------------------------------------------------------------ elements
          // The page's elements for the panel's Elements view, as a tree the panel loads a level at a time: each element by
          // an id of its own, with what tells it apart (tag, id, classes, role, test id, accessible name, own text, or the
          // start of the text it holds). "Interactive only" leaves out what a person does not act on, what is inside it
          // shown in its place: a chain of wrappers is then a mark on what it held. The recorder locates an element the
          // panel picks with describe(), as it locates a recorded action's. F4 freezes the tree, with how to locate each
          // element a click may act on, for a menu that closes as soon as the focus leaves the app. In inspect mode
          // (Ctrl+Shift+C, here or in the panel) the tree follows the pointer, and a click picks the element under it for
          // the panel instead of reaching the page. The panel's pointer on an element marks it here, as its search marks
          // each match. A same-origin frame's elements are shown inside its iframe.
          const SKIPPED = new Set(['SCRIPT', 'STYLE', 'NOSCRIPT', 'TEMPLATE', 'LINK', 'META', 'QAAS-RECORDER-OVERLAY']);
          const TRANSIENT = '[role=menu], [role=listbox], [role=tree], [role=dialog], [role=alertdialog], [role=tooltip], ' +
            '[popover], [aria-modal=true]';
          // What holds the elements a flow acts on, which "interactive only" shows too.
          const SCOPES = 'table, form, nav, dialog, iframe, frame, [role=dialog], [role=alertdialog], [role=menu], ' +
            '[role=menubar], [role=listbox], [role=table], [role=grid], [role=treegrid], [role=tablist], [role=tree], ' +
            '[role=radiogroup], [role=toolbar], [role=navigation], [aria-modal=true], [popover]';
          const ITEMS = 'tr, [role=row], li, [role=option], [role=menuitem], [role=listitem], [role=treeitem]';
          const MAX_FROZEN = 15000;
          const MAX_MATCHES = 500;
          const MAX_LIFTED = 2000;
          const DESCRIBE_BUDGET_MS = 1500;
          // Accessible names are worked out while there is time, so a long table still comes at once.
          const NAME_BUDGET_MS = 250;
          const nodeIds = new WeakMap();
          const nodesById = new Map();
          let lastNode = 0;
          let frozen = new Map();
          let compact = false;
          let namesUntil = 0;
          const nodeId = (node) => {
            let id = nodeIds.get(node);
            if (!id) {
              id = ++lastNode;
              nodeIds.set(node, id);
              nodesById.set(id, new WeakRef(node));
            }
            return id;
          };
          const nodeById = (id) => nodesById.get(id)?.deref() ?? null;
          // A same-origin frame's body; none for another origin's, which the page cannot read.
          const frameBody = (node) => node.nodeType === 1 && (node.tagName === 'IFRAME' || node.tagName === 'FRAME')
            ? safe(() => node.contentDocument?.body ?? null, null) : null;
          const inFrame = (node) => (node.ownerDocument ?? document) !== document;
          // An element's children, its open shadow root first, or a frame's body; a shadow root's children.
          const childrenOf = (node) => {
            const body = frameBody(node);
            if (body) return [body];
            return [
              ...(node.nodeType === 1 && node.shadowRoot ? [node.shadowRoot] : []),
              ...[...node.children].filter((child) => !SKIPPED.has(child.tagName)),
            ];
          };
          const parentOf = (node) => {
            if (node.nodeType === 11) return node.host;
            if (node !== document.body && node === node.ownerDocument?.body) return node.ownerDocument.defaultView?.frameElement ?? null;
            return node.parentNode?.nodeType === 11 ? node.parentNode : node.parentElement;
          };
          const ownText = (element) => normalize([...element.childNodes].filter((child) => child.nodeType === 3)
            .map((child) => child.textContent).join(' ')).replace(BIDI, '');
          // The start of the text an element holds, to tell apart wrappers that have none of their own.
          function glimpseOf(element) {
            const texts = element.ownerDocument.createTreeWalker(element, NodeFilter.SHOW_TEXT);
            let text = '';
            for (let node = texts.nextNode(); node && text.length < 120; node = texts.nextNode())
              if (!SKIPPED.has(node.parentElement?.tagName)) text += ` ${node.textContent}`;
            return normalize(text).replace(BIDI, '').slice(0, 80) || undefined;
          }
          // What "interactive only" shows: what a person acts on, and what holds it.
          const actedOn = (node) => node.nodeType !== 1 || node === document.body || node === node.ownerDocument?.body || safe(() =>
            node.matches(INTERACTIVE) || node.matches(SCOPES) || isTextual(node) || looksClickable(node), false);
          // An element's children as the tree shows them, each with the number of wrappers "interactive only" left out
          // between them.
          function childList(node, limit = MAX_LIFTED) {
            if (!compact) return childrenOf(node).map((child) => [child, 0]);
            const found = [];
            let visits = 0;
            const walk = (from, via) => {
              for (const child of childrenOf(from)) {
                if (found.length >= limit || ++visits > 20000) return;
                if (actedOn(child)) found.push([child, via]);
                else walk(child, via + 1);
              }
            };
            walk(node, 0);
            return found;
          }
          // The element the tree shows for one it may leave out: itself, else the nearest around it that it shows.
          const shownFor = (node) => {
            if (!compact) return node;
            for (let up = node; up; up = parentOf(up)) if (actedOn(up)) return up;
            return document.body;
          };
          const shownParent = (node) => {
            if (node === document.body) return null;
            const parent = parentOf(node);
            return parent && compact ? shownFor(parent) : parent;
          };
          // The elements around it as the tree shows them, the body first.
          function pathOf(node) {
            const path = [];
            for (let up = shownParent(node); up && path.length < 400; up = shownParent(up)) path.unshift(up);
            return path;
          }
          function summary(node, parent = shownParent(node), via = 0) {
            const common = { id: nodeId(node), parent: parent ? nodeId(parent) : undefined,
              count: compact ? childList(node, 1).length : childrenOf(node).length, via: via || undefined };
            if (node.nodeType === 11) return { ...common, tag: '#shadow-root' };
            const attribute = (name) => node.getAttribute(name) || undefined;
            const text = ownText(node);
            const shownText = text ? text.slice(0, 120) : undefined;
            const role = attribute('role');
            const implicit = role ? undefined : roleOf(node) ?? undefined;
            const named = (role || implicit) && now() < namesUntil ? clean(accessibleName(node)).slice(0, 100) : '';
            return {
              ...common, tag: node.tagName.toLowerCase(), elementId: node.id || undefined, classes: attribute('class'),
              role, implicit, testId: attribute(TEST_ID), name: attribute('name'), type: attribute('type'),
              label: attribute('aria-label') ?? attribute('placeholder'), accName: named && named !== shownText ? named : undefined,
              text: shownText, glimpse: !text && !named && node.firstElementChild ? glimpseOf(node) : undefined,
              hidden: !visible(node) || undefined, fillable: isTextual(node) || undefined,
              acts: (node !== document.body && actedOn(node)) || undefined, framed: inFrame(node) || undefined,
            };
          }
          // The page sends a parent's children once until the page changes, while the panel follows it.
          let sent = new WeakMap();
          let epoch = 0;
          // The nodes, with each parent's children, for the panel to merge into its tree.
          function tree(parents, { once = false } = {}) {
            namesUntil = now() + NAME_BUDGET_MS;
            const nodes = new Map();
            const children = {};
            for (const parent of parents) {
              if (!nodes.has(parent)) nodes.set(parent, summary(parent));
              if (once && sent.get(parent) === epoch) continue;
              const list = childList(parent);
              children[nodeId(parent)] = list.map(([child]) => nodeId(child));
              for (const [child, via] of list) nodes.set(child, summary(child, parent, via));
              sent.set(parent, epoch);
            }
            return { root: nodeId(document.body), nodes: [...nodes.values()], children, compact: compact || undefined };
          }
          // The elements below the body, in document order, open shadow roots and same-origin frames included.
          function* everyElement(from) {
            for (const child of childrenOf(from)) {
              if (child.nodeType === 1) yield child;
              yield* everyElement(child);
            }
          }
          // By CSS or Playwright selector (role=button, text=Save), or by text in its id, test id, name, label or own text.
          function search(query) {
            const text = normalize(query).toLowerCase();
            if (!text) return { ...tree([document.body]), matches: [], total: 0 };
            const selected = safe(() => engine()?.$$(query) ?? null, null) ?? safe(() => [...document.querySelectorAll(query)], []);
            const bySelector = new Set(selected);
            const found = [];
            for (const element of everyElement(document.body)) {
              const values = [element.id, element.getAttribute(TEST_ID), element.getAttribute('name'),
                element.getAttribute('aria-label'), element.getAttribute('placeholder'), ownText(element)];
              if (bySelector.has(element) || values.some((value) => value && value.toLowerCase().includes(text))) found.push(element);
            }
            // In "interactive only", the element shown for each.
            const shown = [...new Set(found.map(shownFor))].filter((element) => element !== document.body);
            const kept = shown.slice(0, MAX_MATCHES);
            // Every element on the way to each match, with its children, for the panel to open the tree to it.
            const parents = new Set([document.body]);
            for (const element of kept) for (const up of pathOf(element)) parents.add(up);
            return { ...tree([...parents]), matches: kept.map(nodeId), total: shown.length };
          }
          // How a click on the element would locate it, while it is on the page; whether it is near the element of the
          // step <near> locates, as when a click was recorded on what is around it or inside it; and the checks that fit it.
          function describeNode(id, near) {
            const element = nodeById(id) ?? frozen.get(id);
            if (!element?.isConnected || element.nodeType !== 1) return null;
            if (inFrame(element)) return { framed: true };
            const item = (node) => node.closest(ITEMS);
            const related = !!near && (safe(() => matches(near), null) ?? []).some((other) => other === element ||
              other.contains(element) || element.contains(other) || (!!item(other) && item(other) === item(element)));
            const { kinds, index } = safe(() => kindsFor(element), { kinds: [], index: 0 });
            const checks = kinds.filter((kind) => kind.kind !== 'url' && kind.kind !== 'row')
              .map(({ kind, name, value, group }) => ({ kind, name, value, group }));
            return { description: describe(element, { interactive: true }), fillable: isTextual(element), visible: visible(element),
              related, checks, check: Math.max(0, checks.findIndex((check) => check.kind === kinds[index]?.kind)) };
          }
          function freeze() {
            const started = now();
            const kept = new Map();
            const nodes = [];
            const children = {};
            // A frozen tree is the whole page, as the panel shows it with or without wrappers.
            const wasCompact = compact;
            compact = false;
            namesUntil = now() + NAME_BUDGET_MS * 2;
            const walk = (node) => {
              if (nodes.length >= MAX_FROZEN) return;
              const id = nodeId(node);
              kept.set(id, node);
              nodes.push(summary(node));
              const list = childrenOf(node);
              children[id] = list.map(nodeId);
              for (const child of list) walk(child);
            };
            try { walk(document.body); } finally { compact = wasCompact; }
            frozen = kept;
            // What a click may act on and shows, in a menu, a list or a dialog first, then the latest in the page, where a
            // portal puts them; as many as there is time for.
            const actsOn = (node) => (node.matches(INTERACTIVE) || node.matches(TOGGLES) || looksClickable(node)) && retarget(node) === node;
            const targets = [...kept.values()]
              .filter((node) => node.nodeType === 1 && node !== document.body && !inFrame(node) && visible(node) && safe(() => actsOn(node), false))
              .reverse()
              .map((node) => ({ node, transient: !!node.closest(TRANSIENT) }))
              .sort((a, b) => Number(b.transient) - Number(a.transient));
            const described = [];
            for (const { node } of targets) {
              if (now() - started > DESCRIBE_BUDGET_MS) break;
              const description = safe(() => describe(node, { interactive: true }), null);
              if (description) described.push({ id: nodeId(node), description });
            }
            control({ type: 'freeze', described,
              tree: { root: nodeId(document.body), nodes, children, truncated: nodes.length >= MAX_FROZEN || undefined } });
            showPill('Elements frozen', 'pick them in the panel', true);
          }

          // The panel follows the page as it changes, once it settles; it hears of a dialog, a menu or a list that opened.
          let watcher = null;
          let watching = false;
          let changedTimer = 0;
          let openedSince = [];
          const noteOpened = (node) => {
            if (node?.nodeType !== 1 || openedSince.length > 20) return;
            const found = node.matches(TRANSIENT) ? [node] : [...node.querySelectorAll(TRANSIENT)].slice(0, 4);
            openedSince.push(...found.map((element) => new WeakRef(element)));
          };
          function observe() {
            if (watcher || !document.body) return;
            watcher = new MutationObserver((records) => safe(() => {
              epoch++;
              for (const record of records) {
                if (record.type === 'childList') record.addedNodes.forEach(noteOpened);
                else noteOpened(record.target);
              }
              if (!watching) return;
              clearTimeout(changedTimer);
              changedTimer = setTimeout(() => control({ type: 'changed' }), 600);
            }));
            watcher.observe(document.body, { childList: true, subtree: true, attributes: true,
              attributeFilter: ['id', 'class', 'role', 'hidden', 'open', 'aria-hidden', 'aria-label', 'aria-expanded', 'name', TEST_ID] });
          }
          function watch(on) {
            watching = on;
            if (on || inspecting) observe();
            else { watcher?.disconnect(); watcher = null; }
          }
          // What opened since the panel last asked, still on the page and showing; asked afresh, what shows now.
          function takeOpened(fresh) {
            const elements = fresh ? [...document.querySelectorAll(TRANSIENT)] : openedSince.map((ref) => ref.deref());
            openedSince = [];
            return [...new Set(elements)].filter((element) => element?.isConnected && visible(element) &&
              !element.parentElement?.closest(TRANSIENT)).slice(0, 4);
          }
          const openedLabel = (element) => {
            const name = clean(accessibleName(element)).slice(0, 60);
            const role = element.getAttribute('role') || roleOf(element) || element.tagName.toLowerCase();
            return name ? `${role} “${name}”` : role;
          };

          // ------------------------------------------------------------------------------------------- marks, inspect
          // The panel's pointer on an element boxes it here, named as the tree names it, and scrolls it into view; its
          // search boxes each match, the current one stronger. Drawn in the overlay's closed shadow root.
          const MARKS_CSS = `
            .mark, .hit, .tip { position: fixed; display: none; box-sizing: border-box; pointer-events: none; }
            .mark { border: 2px solid #5157e6; border-radius: 2px; background: rgba(81, 87, 230, .16);
              box-shadow: 0 0 0 1px rgba(255, 255, 255, .9); }
            .hit { border: 1.5px solid rgba(217, 119, 6, .9); border-radius: 2px; background: rgba(245, 158, 11, .14); }
            .hit.current { border-width: 2px; border-color: #d97706; background: rgba(245, 158, 11, .26); }
            .tip { max-width: min(520px, calc(100vw - 8px)); padding: 4px 8px; border-radius: 4px; white-space: nowrap;
              overflow: hidden; text-overflow: ellipsis; background: rgba(24, 24, 27, .95); color: #a1a1aa; direction: ltr;
              font: 11.5px/1.45 ui-monospace, SFMono-Regular, Menlo, Consolas, 'DejaVu Sans Mono', monospace;
              box-shadow: 0 2px 6px rgba(0, 0, 0, .35), 0 0 0 1px rgba(255, 255, 255, .25); }
            .tip b { color: #c4b5fd; font-weight: 600; }
            .tip i { color: #93c5fd; font-style: normal; }
            .tip span { color: #e4e4e7; font-family: system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif; unicode-bidi: isolate; }
            .bar.inspect b::before { background: #5157e6; box-shadow: 0 0 0 3px rgba(81, 87, 230, .22); }`;
          let marked = { hover: null, matches: [], current: null };
          let marks = null;
          let markFrame = 0;
          function marksView() {
            const { root } = overlayView();
            if (marks?.root === root) return marks;
            const sheet = new CSSStyleSheet();
            sheet.replaceSync(MARKS_CSS);
            root.adoptedStyleSheets = [...root.adoptedStyleSheets, sheet];
            const part = (name) => {
              const element = root.appendChild(document.createElement('div'));
              element.className = name;
              element.setAttribute('aria-hidden', 'true');
              return element;
            };
            marks = { root, hits: [], box: part('mark'), tip: part('tip') };
            return marks;
          }
          const markable = (node) => node?.nodeType === 11 ? node.host : node?.nodeType === 1 && node.isConnected ? node : null;
          // Its box on the top page, through the frames it is in.
          function boxOf(element) {
            const rect = element.getBoundingClientRect();
            let [left, top] = [rect.left, rect.top];
            for (let frame = element.ownerDocument.defaultView?.frameElement; frame; frame = frame.ownerDocument.defaultView?.frameElement) {
              const outer = frame.getBoundingClientRect();
              left += outer.left + frame.clientLeft;
              top += outer.top + frame.clientTop;
            }
            return { left, top, width: rect.width, height: rect.height };
          }
          const place = (element, box) => Object.assign(element.style, { display: 'block', left: `${box.left}px`, top: `${box.top}px`,
            width: `${box.width}px`, height: `${box.height}px` });
          function nameTip(tip, element, box) {
            const id = element.id ? `#${element.id}` : '';
            const first = typeof element.className === 'string' ? element.className.trim().split(/\s+/)[0] : '';
            const role = element.getAttribute('role') || roleOf(element);
            const name = role ? clean(accessibleName(element)).slice(0, 60) : '';
            tip.replaceChildren();
            tip.appendChild(document.createElement('b')).textContent = element.tagName.toLowerCase();
            if (id) tip.appendChild(document.createElement('i')).textContent = id;
            else if (first) tip.append(`.${first}`);
            tip.append(`  ${Math.round(box.width)} × ${Math.round(box.height)}`);
            if (role) tip.append(`  ${role}`);
            if (name) tip.appendChild(document.createElement('span')).textContent = ` “${name}”`;
          }
          function drawMarks() {
            markFrame = 0;
            const { hover, matches: hits, current } = marked;
            if (!hover?.isConnected && !hits.length) {
              if (marks) for (const element of [marks.box, marks.tip, ...marks.hits]) element.style.display = 'none';
              return;
            }
            const view = marksView();
            hits.forEach((element, index) => {
              const hit = view.hits[index] ?? (view.hits[index] = view.root.insertBefore(document.createElement('div'), view.box));
              hit.className = `hit${element === current ? ' current' : ''}`;
              if (element.isConnected) place(hit, boxOf(element)); else hit.style.display = 'none';
            });
            for (const hit of view.hits.slice(hits.length)) hit.style.display = 'none';
            if (hover?.isConnected) {
              const box = boxOf(hover);
              place(view.box, box);
              nameTip(view.tip, hover, box);
              view.tip.style.display = 'block';
              const size = view.tip.getBoundingClientRect();
              const top = box.top - size.height - 4 >= 0 ? box.top - size.height - 4 : Math.min(box.top + box.height + 4, innerHeight - size.height - 4);
              Object.assign(view.tip.style, { left: `${Math.max(4, Math.min(box.left, innerWidth - size.width - 4))}px`, top: `${Math.max(4, top)}px` });
            } else {
              view.box.style.display = 'none';
              view.tip.style.display = 'none';
            }
            // It follows the element as the page scrolls or moves it.
            markFrame = requestAnimationFrame(() => safe(drawMarks));
          }
          // Scrolls the element into view when it is out of it, the frame it is in first.
          function reveal(element) {
            const box = boxOf(element);
            if (box.top >= 0 && box.left >= 0 && box.top + Math.min(box.height, innerHeight) <= innerHeight && box.left < innerWidth) return;
            const frame = element.ownerDocument.defaultView?.frameElement;
            if (frame) safe(() => frame.scrollIntoView({ block: 'center', inline: 'nearest' }));
            safe(() => element.scrollIntoView({ block: 'center', inline: 'nearest' }));
          }
          function setMarks(command) {
            if ('hover' in command) {
              marked.hover = markable(nodeById(command.hover));
              if (marked.hover && command.scroll) reveal(marked.hover);
            }
            if ('matches' in command) marked.matches = (command.matches ?? []).map(nodeById).map(markable).filter(Boolean).slice(0, 300);
            if ('current' in command) {
              const was = marked.current;
              marked.current = markable(nodeById(command.current));
              // Going to the next match shows it.
              if (marked.current && marked.current !== was) reveal(marked.current);
            }
            if (!markFrame) drawMarks();
          }

          // Inspect mode: the box follows the pointer, and the panel's tree with it; a click picks the element there, and
          // nothing reaches the page but the pointer's moves. Esc, or Ctrl+Shift+C again, leaves it, as a pick does.
          const INSPECT_BLOCKED = ['pointerdown', 'mousedown', 'pointerup', 'mouseup', 'click', 'dblclick', 'auxclick', 'contextmenu'];
          let inspected = null;
          let inspectTimer = 0;
          let unhookFrames = [];
          function startInspecting() {
            if (inspecting) return;
            if (state === 'login' || state === 'testing') { showPill('Not now', 'the recording is busy', true); return; }
            stopChecking();
            flushTyping();
            inspecting = true;
            hover = null;
            hideOverlay();
            observe();
            sent = new WeakMap();
            const { bar } = overlayView();
            bar.replaceChildren();
            bar.appendChild(document.createElement('b')).textContent = 'Inspect';
            const hint = bar.appendChild(document.createElement('span'));
            hint.className = 'hint';
            hint.textContent = 'Point at an element; a click picks it in the panel';
            bar.appendChild(document.createElement('kbd')).textContent = 'Esc';
            bar.classList.add('shown', 'inspect');
            clearTimeout(pillTimer);
            view.state.className = 'state';
            hookFrames();
            control({ type: 'inspect', on: true });
          }
          function stopInspecting() {
            if (!inspecting) return;
            inspecting = false;
            clearTimeout(inspectTimer);
            inspectTimer = 0;
            inspected = null;
            marked.hover = null;
            if (!markFrame) drawMarks();
            for (const unhook of unhookFrames) safe(unhook);
            unhookFrames = [];
            view?.bar.classList.remove('inspect');
            if (!checking) {
              view?.bar.classList.remove('shown');
              showState();
            }
            if (!watching) { watcher?.disconnect(); watcher = null; }
            control({ type: 'inspect', on: false });
          }
          function inspectAt(hit) {
            if (!inspecting || hit?.nodeType !== 1 || hit === document.documentElement || hit === view?.host) return;
            if (state === 'login' || state === 'testing') { stopInspecting(); return; }
            const element = shownFor(hit);
            if (element === inspected) return;
            inspected = element;
            marked.hover = element === document.body ? null : element;
            if (!markFrame) drawMarks();
            if (!inspectTimer) inspectTimer = setTimeout(() => safe(() => { inspectTimer = 0; reportInspected(false); }), 90);
          }
          function reportInspected(picked) {
            const element = inspected;
            if (!element?.isConnected) return;
            control({ type: 'inspect', on: true, node: nodeId(element), picked: picked || undefined,
              tree: tree([...pathOf(element), element], { once: true }) });
          }
          function blockForInspect(event) {
            if (!inspecting || checking || !event.isTrusted || event.composedPath()[0] === view?.host) return;
            event.preventDefault();
            event.stopImmediatePropagation();
            if (event.type !== 'click' || event.button !== 0) return;
            inspectAt(deepTarget(event));
            clearTimeout(inspectTimer);
            inspectTimer = 0;
            reportInspected(true);
            stopInspecting();
          }
          function inspectKeys(event) {
            if (!event.isTrusted) return;
            // F6 went to check mode.
            if (inspecting && checking) stopInspecting();
            const handled = () => { event.preventDefault(); event.stopImmediatePropagation(); };
            // Chrome would open its developer tools.
            if ((event.ctrlKey || event.metaKey) && event.shiftKey && !event.altKey && event.code === 'KeyC') {
              handled();
              if (!event.repeat) inspecting ? stopInspecting() : startInspecting();
            } else if (inspecting && event.key === 'Escape') {
              handled();
              stopInspecting();
            }
          }
          const inspectMove = (event) => safe(() => { if (inspecting && event.isTrusted) inspectAt(deepTarget(event)); });
          addEventListener('pointermove', inspectMove, { capture: true, passive: true });
          for (const type of INSPECT_BLOCKED) addEventListener(type, (event) => safe(() => blockForInspect(event)), { capture: true });
          addEventListener('keydown', (event) => safe(() => inspectKeys(event)), { capture: true });
          // A same-origin frame has no capture script of its own: the pointer is followed into it while inspecting.
          function hookFrames() {
            for (const frame of document.querySelectorAll('iframe, frame')) {
              const frameWindow = safe(() => frame.contentDocument ? frame.contentWindow : null, null);
              if (!frameWindow) continue;
              const block = (event) => safe(() => blockForInspect(event));
              const keys = (event) => safe(() => inspectKeys(event));
              frameWindow.addEventListener('pointermove', inspectMove, { capture: true, passive: true });
              frameWindow.addEventListener('keydown', keys, { capture: true });
              for (const type of INSPECT_BLOCKED) frameWindow.addEventListener(type, block, { capture: true });
              unhookFrames.push(() => {
                frameWindow.removeEventListener('pointermove', inspectMove, { capture: true });
                frameWindow.removeEventListener('keydown', keys, { capture: true });
                for (const type of INSPECT_BLOCKED) frameWindow.removeEventListener(type, block, { capture: true });
              });
            }
          }

          // The recorder's calls, each answered as JSON. Not enumerable, and not writable by the page.
          Object.defineProperty(window, '__qaasRecorder', { value: (command) => safe(() => {
            switch (command?.op) {
              case 'state': applyState(command.state, command.part); return null;
              case 'flush': flushTyping(); return null;
              case 'explained': explained(command.seq, command.label); return null;
              case 'tree': {
                compact = !!command.compact;
                sent = new WeakMap();
                const shown = takeOpened(!!command.fresh);
                const ids = (command.ids ?? []).map(nodeById).filter((node) => node?.isConnected && node !== document.body);
                // The element the panel has selected, as this mode shows it, with the way to it.
                const target = nodeById(command.reveal);
                const revealed = target?.isConnected ? shownFor(target) : null;
                return JSON.stringify({ ...tree([document.body, ...ids, ...shown.flatMap(pathOf), ...(revealed ? pathOf(revealed) : [])]),
                  opened: shown.map((element) => ({ id: nodeId(element), label: openedLabel(element) })),
                  revealed: revealed && revealed !== document.body ? nodeId(revealed) : undefined });
              }
              case 'search':
                compact = !!command.compact;
                sent = new WeakMap();
                return JSON.stringify(search(command.query ?? ''));
              case 'describe': return JSON.stringify(describeNode(command.id, command.near ? JSON.parse(command.near) : null));
              case 'watch': watch(!!command.on); return null;
              case 'mark': setMarks(command); return null;
              case 'inspect': command.on ? startInspecting() : stopInspecting(); return null;
              case 'check':
                if (command.on) { stopInspecting(); startChecking(); } else stopChecking();
                return null;
              case 'login':
                [loginText, loginHint, loginEscape] = [command.text ?? '', command.hint ?? '', !!command.esc];
                if (state === 'login') showState();
                return null;
              default: return null;
            }
          }, null) });
          control({ type: 'state' });
        })();
        """;
}
