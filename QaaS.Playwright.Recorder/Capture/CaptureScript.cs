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
          // In the element's own document or shadow root.
          const idRefs = (element, attribute) => (element.getAttribute(attribute) || '').split(/\s+/).filter(Boolean)
            .map((id) => element.getRootNode().getElementById?.(id) ?? document.getElementById(id)).filter(Boolean);

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
              else if (part.m === 'text') locator = locator.getByText(part.text, { exact: true });
              else if (part.m === 'title') locator = locator.getByTitle(part.text, { exact: true });
              else if (part.m === 'has') locator = locator.filter({ has: engine().getByText(part.text, { exact: true }) });
              else if (part.m === 'role') {
                const options = {};
                if (part.regex !== undefined) options.name = new RegExp(part.regex);
                else if (part.name !== undefined) options.name = part.name;
                if (part.exact) options.exact = true;
                locator = locator.getByRole(part.role, options);
                if (part.heading !== undefined) locator = locator.filter({ has: engine().getByText(part.heading, { exact: true }) });
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
          const isAutocomplete = (element) => element.tagName === 'INPUT' && element.getAttribute('role') === 'combobox' &&
            !element.readOnly;
          const controlOfLabel = (label) => label.control ?? label.querySelector(TOGGLES);

          // An element's parent as the page shows it: the slot it is shown in, else its parent, and out of a shadow root,
          // its host.
          const upFrom = (element) => element.assignedSlot ?? element.parentElement ??
            (element.parentNode instanceof ShadowRoot ? element.parentNode.host : null);
          const closestUp = (element, selector) => {
            for (let node = element; node; node = upFrom(node)) if (node.matches(selector)) return node;
            return null;
          };
          const containsDeep = (ancestor, element) => {
            for (let node = element; node; node = upFrom(node)) if (node === ancestor) return true;
            return false;
          };

          // A click handler the page keeps on the element, which a framework runs from one listener of its own (event
          // delegation), so the element has no onclick attribute: React's props, and the onclick it sets on an element
          // with onClick, Vue's invokers, jQuery's record of what it bound. Read, never called.
          const CLICK_PROPS = ['onClick', 'onMouseDown', 'onMouseUp', 'onPointerDown', 'onPointerUp', 'onDoubleClick'];
          const VUE_CLICKS = ['onClick', 'onMousedown', 'onMouseup', 'onPointerdown', 'onPointerup'];
          function hasClickHandler(element) {
            if (element.hasAttribute('onclick') || typeof element.onclick === 'function' ||
                typeof element.onmousedown === 'function') return true;
            for (const key of Object.keys(element)) {
              const value = element[key];
              if (!value || typeof value !== 'object') continue;
              if (key.startsWith('__reactProps$') || key.startsWith('__reactEventHandlers$')) {
                if (CLICK_PROPS.some((prop) => typeof value[prop] === 'function')) return true;
              } else if (key === '_vei' && VUE_CLICKS.some((prop) => value[prop])) return true;
            }
            const events = safe(() => window.jQuery?._data?.(element, 'events'), null);
            return !!events && ['click', 'mousedown', 'mouseup'].some((type) => events[type]?.length > 0);
          }

          // What looks clickable without being a control: the pointer turns into a hand over it and not over its parent, it
          // has an onclick attribute, or the Tab key stops on it. A focusable area that scrolls is for the keyboard.
          function looksClickable(element) {
            if (element === document.body || element === document.documentElement) return false;
            if (element.hasAttribute('onclick')) return true;
            const style = styleOf(element);
            if (!style) return false;
            const parent = upFrom(element);
            if (style.cursor === 'pointer' && (!parent || styleOf(parent)?.cursor !== 'pointer')) return true;
            if (!element.hasAttribute('tabindex') || element.tabIndex < 0 || element.matches(DIALOGS)) return false;
            return !(/auto|scroll/.test(style.overflowX + style.overflowY) &&
              (element.scrollHeight > element.clientHeight || element.scrollWidth > element.clientWidth));
          }

          // An item of a list of options, as its tag, role or class say, or as it handles clicks or looks clickable.
          const OPTION_CLASS = /option|item|card|tile|choice|result|slide|entry/i;
          const itemLike = (element) => ['LI', 'OPTION', 'TR'].includes(element.tagName) || element.hasAttribute('role') ||
            OPTION_CLASS.test(element.getAttribute('class') ?? '') || hasClickHandler(element) || looksClickable(element);
          // Two of a kind: the same tag and role, and a class in common, or none at all.
          const sameKind = (a, b) => a.tagName === b.tagName && a.getAttribute('role') === b.getAttribute('role') &&
            ((a.classList.length === 0 && b.classList.length === 0) || [...a.classList].some((name) => b.classList.contains(name)));
          // The element holds other items like the one the pointer is in, each with words: a list of options.
          const holdsSeveral = (element, item) => itemLike(item) &&
            [...(element.tagName === 'SLOT' ? element.assignedElements() : element.children)].some((other) =>
              other !== item && sameKind(other, item) && visible(other) && hasLetters(other.textContent ?? ''));
          const controlsIn = (element) => element.querySelectorAll(INTERACTIVE).length;

          // What retarget found by a handler of the page's or as an item of a list, which a person acts on.
          const actionable = new WeakSet();

          // The element a person meant: the control around what the pointer hit (for a label of a checkbox, radio or
          // switch, the control itself), out of shadow roots; else, below the next row, cell or dialog, the nearest element
          // that handles clicks (one with no more than one control in it), else the outermost one that looks clickable,
          // never above an item of a list of options: that item, when the list or what holds it handles the click; else
          // the row or cell, else what was hit.
          // A click on a part of a widget hidden from assistive technology, as react-select's arrow is, is a click on the
          // widget: its one control, the input of its box.
          const fromDecoration = new WeakSet();
          function widgetOfDecoration(hit) {
            const decoration = closestUp(hit, '[aria-hidden=true]');
            if (!decoration || decoration === document.body || controlsIn(decoration) > 0 || decoration.matches(DIALOGS)) return null;
            for (let holder = upFrom(decoration), depth = 0; holder && depth < 4; holder = upFrom(holder), depth++) {
              if (holder === document.body || holder.matches(DIALOGS)) return null;
              const controls = [...holder.querySelectorAll(INTERACTIVE)].filter((control) => control.isConnected && !control.closest('[aria-hidden=true]'));
              if (controls.length === 1) { fromDecoration.add(controls[0]); return controls[0]; }
              if (controls.length > 1) return null;
            }
            return null;
          }

          function retarget(hit) {
            const interactive = closestUp(hit, INTERACTIVE);
            const label = hit.closest('label');
            if (label && (!interactive || interactive.contains(label))) {
              const control = controlOfLabel(label);
              if (control) return control;
            }
            if (interactive && !interactive.matches(CONTAINERS)) return interactive;
            if (!interactive) {
              const widget = widgetOfDecoration(hit);
              if (widget) return widget;
            }
            let handled = null;
            let clickable = null;
            let item = null;
            for (let node = hit, from = null; node && node !== interactive; from = node, node = upFrom(node)) {
              if (node === document.body || node === document.documentElement || node.matches(DIALOGS)) break;
              if (from && !item && holdsSeveral(node, from)) {
                if (handled || clickable) break;
                item = from;
              }
              const handles = hasClickHandler(node);
              if (item) {
                if (handles || looksClickable(node)) { actionable.add(item); return item; }
                continue;
              }
              if (!handled && handles && controlsIn(node) <= 1) handled = node;
              if (looksClickable(node)) clickable = node;
            }
            if (handled) actionable.add(handled);
            // In a header's row, the cell clicked, as it sorts or filters its column: never the whole header for a click on
            // one of its cells. A row of data is clicked as a whole, as an app opens its record.
            const cell = interactive?.matches(CONTAINERS) ? closestUp(hit, HEADER_CELLS) : null;
            return handled ?? clickable ?? item ?? (cell && interactive.contains(cell) ? cell : interactive) ?? hit;
          }
          const HEADER_CELLS = 'th, [role=columnheader], [role=rowheader]';
          const CELLS = 'th, td, [role=gridcell], [role=columnheader], [role=rowheader], [role=cell]';

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
            if (containsDeep(element, hit)) for (let node = hit; node !== element; node = upFrom(node)) found.unshift(node);
            for (let child = onlyChild(element); child; child = onlyChild(child)) if (!found.includes(child)) found.push(child);
            const kept = found.filter((node) => node.id || node.getAttribute(TEST_ID));
            // The one control in the element, as the input in a select's box, when a click on its middle reaches it.
            const controls = element.matches(INTERACTIVE) ? [] : [...element.querySelectorAll(INTERACTIVE)].filter(visible);
            if (controls.length === 1 && !kept.includes(controls[0])) {
              const box = controls[0].getBoundingClientRect();
              const atMiddle = deepAt(box.left + box.width / 2, box.top + box.height / 2);
              if (atMiddle && containsDeep(controls[0], atMiddle)) kept.push(controls[0]);
            }
            return kept;
          }
          const deepAt = (x, y) => {
            let element = document.elementFromPoint(x, y);
            while (element?.shadowRoot) {
              const inner = element.shadowRoot.elementFromPoint(x, y);
              if (!inner || inner === element) break;
              element = inner;
            }
            return element;
          };
          // Each is the same click: when the target is a control, so are they.
          const describeEquivalents = (elements, target) =>
            elements.map((other) => describe(other, target.interactive ? { interactive: true } : {}).target);

          // What the element is beyond its role, for the recorder to write it.
          const widgetOf = (element) => element.matches(TOGGLES)
            // MUI's checkboxes and radios are of its SwitchBase: no switch.
            ? (roleOf(element) === 'radio' ? 'radio' : roleOf(element) === 'switch' || closestUp(element, '[class*=switch i]:not([class*=switchbase i])') ? 'switch' : 'checkbox')
            : isMuiSelect(element) ? 'select-open'
            : element.closest('.MuiAutocomplete-root') && element.tagName === 'BUTTON' ? 'autocomplete-button'
            : roleOf(element) === 'tab' ? 'tab'
            : roleOf(element) === 'menuitem' ? 'menuitem'
            : null;

          function dialogOf(element) {
            for (let dialog = closestUp(element, DIALOGS); dialog; dialog = upFrom(dialog) && closestUp(upFrom(dialog), DIALOGS))
              if (!dialog.matches(PICKER)) return dialog;
            return null;
          }
          const openDialogs = () => [...document.querySelectorAll(DIALOGS)].filter((dialog) => !dialog.matches(PICKER) &&
            visible(dialog));
          const hasLetters = (text) => /\p{L}{2}/u.test(text);

          // The title a dialog shows when nothing names it, as a hand-made modal often does: its heading, else what is
          // styled as its title, else its first words. The person reads it as the dialog's title; the flow finds the
          // dialog by it, and names its steps after it.
          function headingOf(dialog) {
            const own = (element) => element.closest(DIALOGS) === dialog && visible(element);
            const fits = (text) => text.length <= 60 && hasLetters(text);
            for (const selector of ['h1, h2, h3, h4, h5, h6, [role=heading]', '[class*=title i], [class*=heading i], [class*=header i]'])
              for (const element of dialog.querySelectorAll(selector)) {
                const text = own(element) ? clean(element.innerText ?? element.textContent) : '';
                if (text && fits(text)) return text;
              }
            const walker = document.createTreeWalker(dialog, NodeFilter.SHOW_TEXT);
            for (let node = walker.nextNode(); node; node = walker.nextNode()) {
              const text = clean(node.textContent);
              if (text && node.parentElement && own(node.parentElement)) return fits(text) ? text : null;
            }
            return null;
          }

          // A dialog by its accessible name; one that has none by the heading it shows.
          const dialogTitle = (dialog) => clean(accessibleName(dialog)) || headingOf(dialog) || '';

          function describeDialog(dialog) {
            const raw = normalize(accessibleName(dialog));
            const named = clean(raw);
            const heading = named ? null : headingOf(dialog);
            const title = named || heading || '';
            const prefix = beforeDigits(named);
            const count = (name) => name ? (matches([{ m: 'role', role: 'dialog', name }])?.length ?? -1) : -1;
            const finds = (parts) => { const found = matches(parts); return !!found && found.length === 1 && found[0] === dialog; };
            const hits = (name) => finds([{ m: 'role', role: 'dialog', name }]);
            return {
              title, raw,
              titleMatches: !!named && hits(named),
              prefix: prefix !== named ? prefix : null,
              prefixMatches: !!prefix && prefix !== named && hits(prefix),
              rawMatches: raw !== named && hits(raw),
              ...(heading ? { heading, headingMatches: finds([{ m: 'role', role: 'dialog', heading }]) } : {}),
              unnamedMatches: (matches([{ m: 'role', role: 'dialog' }])?.length ?? 0) === 1,
              count: count(named),
            };
          }

          // The scope the element is found in: its dialog, as the recorder will write it, or the page.
          function scopeOf(element, dialogInfo, dialog) {
            if (!dialog || !dialogInfo) return [];
            if (dialogInfo.titleMatches) return [{ m: 'role', role: 'dialog', name: dialogInfo.title }];
            if (dialogInfo.prefixMatches) return [{ m: 'role', role: 'dialog', name: dialogInfo.prefix }];
            if (dialogInfo.rawMatches) return [{ m: 'role', role: 'dialog', name: dialogInfo.raw, exact: true }];
            if (dialogInfo.headingMatches) return [{ m: 'role', role: 'dialog', heading: dialogInfo.heading }];
            if (dialogInfo.unnamedMatches) return [{ m: 'role', role: 'dialog' }];
            return [];
          }

          function nameVariants(element, role) {
            const raw = normalize(accessibleName(element));
            const stripped = clean(raw);
            // A field that opens a list is named by its label and the value it shows: by its label, as the value changes. One
            // labelled only by what it shows, or not at all, is found by its role alone.
            const opensList = role === 'combobox' || ['listbox', 'true', 'menu'].includes(element.getAttribute('aria-haspopup'));
            if (!stripped) return opensList ? [{}] : [];
            if (isMuiSelect(element) || (opensList && element.getAttribute('aria-labelledby'))) {
              const label = clean(idRefs(element, 'aria-labelledby').filter((ref) => ref !== element && !containsDeep(element, ref))
                .map((ref) => ref.textContent).join(' '));
              return label ? [{ regex: '^' + regexEscape(label), text: label }] : [{}];
            }
            if (role === 'tab' && /\d/.test(stripped)) {
              const prefix = beforeDigits(stripped).replace(/\s*\($/, '');
              return prefix ? [{ regex: '^' + regexEscape(prefix), text: prefix }] : [];
            }
            const variants = [{ name: stripped, exact: true }, { name: stripped }];
            if (raw !== stripped && raw.replace(BIDI, '') !== raw) variants.push({ name: raw, exact: true, marks: true });
            // A name that holds what changes from load to load, or what the flow may edit, a price, a date, a time or a
            // count, is found first by the words before it, when they find the element alone: a poster's link by its
            // title and size, without its price.
            const stable = beforeVolatile(stripped);
            return stable ? [{ regex: '^' + regexEscape(stable), text: stable }, ...variants] : variants;
          }
          const VOLATILE_IN_NAME = new RegExp([
            '[-+]?(\\p{Sc}|\\b[A-Z]{1,3}\\$)\\s?\\d', '\\d[\\d.,]*\\s?\\p{Sc}', '\\d{1,4}[/.-]\\d{1,2}[/.-]\\d{1,4}', '\\b\\d{1,2}:\\d{2}',
            '\\b\\d+([.,]\\d+)?\\s?%', '\\b(mon|tue|wed|thu|fri|sat|sun)[a-z]*day\\b',
            '\\b(jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)[a-z]*\\.?\\s\\d{1,2}\\b', '\\b\\d{1,2}\\s(jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)[a-z]*\\b',
          ].join('|'), 'iu');
          // The words of a name before what in it changes, without the punctuation and the little words that led to it;
          // null when nothing in it changes, or too little is left to tell the element by.
          function beforeVolatile(name) {
            const found = VOLATILE_IN_NAME.exec(name);
            if (!found) return null;
            const words = name.slice(0, found.index).split(/\s+/).filter(Boolean);
            while (words.length && /^(on|at|of|for|from|to|in|by|and|or|-|–|—|\(|,)$/i.test(words[words.length - 1].replace(/[,:;(]+$/, '') || ','))
              words.pop();
            const stable = words.join(' ').replace(/[\s,:;(–—-]+$/, '');
            return (stable.match(/\p{L}/gu)?.length ?? 0) >= 3 ? stable : null;
          }

          // A row of a table and the text that tells it apart from the others: its key, the first cell whose text no other
          // row has, by what a key is: a reference of letters and digits (TCK-1040, 50SKLO, TradeCode 99), then a number in
          // a column of ids, then a name; never what changes from load to load or the flow may edit, a price, a date or a
          // time, a count or a percentage, unless nothing else tells the row apart.
          const VOLATILE = /^[-+]?\p{Sc}|\p{Sc}\s*[-+]?\d|^[A-Z]{1,3}\$|^[-+]?\d+([.,]\d+)*\s*%?$|\d{1,4}[/.-]\d{1,2}[/.-]\d{1,4}|\d{1,2}:\d{2}|^(true|false|yes|no)$/iu;
          const keyRank = (text, header) => VOLATILE.test(text) ? (/^\d+$/.test(text) && /\b(id|no|nr|number|code|ref|#)\b|^#$/i.test(header) ? 1 : 3)
            : /\p{L}/u.test(text) && /\d/.test(text) && text.split(/\s+/).length <= 3 ? 0
            : /\p{L}/u.test(text) ? 2 : 3;
          function rowOf(element) {
            const row = element.closest('tr, [role=row]');
            if (!row || row.querySelector('th[scope=col], [role=columnheader]')) return null;
            const cells = [...row.querySelectorAll(':scope > td, :scope > th, :scope > [role=cell], :scope > [role=gridcell]')];
            const texts = cells.map((cell) => clean(cell.innerText || cell.textContent));
            // A grid's headers may be in a table of their own, above its rows' (Kendo's).
            const tables = [row.closest('table, [role=grid], [role=table], [role=treegrid]'), row.closest('[role=grid], [role=table], [role=treegrid]')];
            const headers = tables.filter(Boolean).map((table) => [...table.querySelectorAll('th[scope=col], [role=columnheader], thead th')])
              .find((found) => found.length) ?? [];
            const header = (index) => headers[index] ? shownText(headers[index]) : '';
            const order = texts.map((text, index) => ({ text, index, rank: keyRank(text, header(index)) }))
              .filter(({ text }) => text.length >= 2 && text.length <= 40)
              .sort((a, b) => a.rank - b.rank || a.index - b.index);
            for (const { text, index } of order) {
              const found = matches([{ m: 'role', role: 'row' }, { m: 'filter', hasText: text }]);
              if (found?.length === 1 && found[0] === row)
                return { key: text, header: header(index), element: row };
            }
            return { key: null, header: null, element: row };
          }

          function candidate(kind, scope, parts, element, extra) {
            const found = matches([...scope, ...parts]);
            const hidden = found && found.length > 1 ? found.filter((other) => !visible(other)).length : 0;
            return { kind, parts: [...scope, ...parts], count: found ? found.length : -1,
              matches: !!found && found.length === 1 && found[0] === element, ...(hidden ? { hidden } : {}), ...extra };
          }

          // What an element without a role says first: the text of its first element of words, as a card's title before
          // its badge and price.
          function labelOf(element) {
            const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT);
            for (let node = walker.nextNode(); node; node = walker.nextNode()) {
              const holder = node.parentElement;
              // An element of this text alone, beside icons at most.
              const alone = holder && [...holder.children].every((child) => !child.textContent.trim());
              const text = alone && visible(holder) ? clean(holder.textContent) : '';
              if (text && hasLetters(text) && text.length <= 60) return { holder, text };
            }
            return null;
          }
          // The element, or one in it so deep, holds several items like each other.
          const listIn = (element, depth) => depth > 0 && [...element.children].some((child) =>
            holdsSeveral(element, child) || listIn(child, depth - 1));

          // A class a person gave it, not one a framework generated.
          const ownClass = (element) => [...element.classList].find((name) => name.length > 2 &&
            !/^(css|jss|sc|emotion|styled|tw)-|^_|[0-9]{3,}|[A-Za-z]+_[A-Za-z]+__/i.test(name) && !/[a-z][0-9][a-z]/i.test(name));

          // The element by its label, an element of exactly that text in it, which a click reaches the element through. Copies
          // of it hidden from assistive technology, as a carousel's clones, are left out by the item it is in.
          function labelCandidate(element, scope) {
            // Not a list's or a form's: its first words are one item's or one field's.
            if (controlsIn(element) > 1 || listIn(element, 3)) return null;
            const label = labelOf(element);
            if (!label) return null;
            const parts = [{ m: 'text', text: label.text }];
            const found = matches([...scope, ...parts]);
            const inside = (other) => containsDeep(element, other);
            if (found?.length > 1 && found.filter((other) => !closestUp(other, '[aria-hidden=true]')).length === 1) {
              for (let item = element; item && item !== document.body; item = upFrom(item)) {
                const own = ownClass(item);
                const parent = upFrom(item);
                if (!own || !parent || ![...parent.children].some((other) => other !== item && other.classList.contains(own) &&
                    other.getAttribute('aria-hidden') === 'true')) continue;
                const scoped = [{ m: 'css', css: `.${CSS.escape(own)}:not([aria-hidden='true'])` }, ...parts];
                const again = matches([...scope, ...scoped]);
                return { kind: 'text', parts: [...scope, ...scoped], count: again?.length ?? -1,
                  matches: again?.length === 1 && inside(again[0]), text: label.text };
              }
            }
            return { kind: 'text', parts: [...scope, ...parts], count: found?.length ?? -1,
              matches: found?.length === 1 && inside(found[0]), text: label.text };
          }

          // An option's label is its list's: "Pickup point" for one of its pickup points.
          const listLabel = (element, role) => {
            const listbox = role === 'option' ? closestUp(element, '[role=listbox]') : null;
            return listbox ? clean(accessibleName(listbox)) : '';
          };

          // One of a list of options, or in one, as a carousel's tile is in the wrappers of its slide.
          const isItem = (element) => {
            for (let item = element, depth = 0; item && depth < 3; item = upFrom(item), depth++)
              if (upFrom(item) && holdsSeveral(upFrom(item), item)) return true;
            return false;
          };

          // ------------------------------------------------------------------------------- what a person reads beside it
          // A class a library's author gave the element, which stays from build to build: not one a style library hashed
          // (css-1x2y3z-control, sc-bdVaJa, jss12, makeStyles-root-3, Button_primary__3xYz1), nor a modifier its state
          // turns on and off (oxd-select-text--focus, is-open).
          const HASHED = /^(css|emotion|e|sc|styled|jsx|svelte|astro|tw)-[0-9a-zA-Z]{4,}(-.*)?$|^jss\d+$|^[A-Za-z][A-Za-z0-9]*-[A-Za-z0-9]+-\d+$|^[A-Za-z][A-Za-z0-9]*_[A-Za-z0-9-]+__[A-Za-z0-9_-]{5}$|^_[0-9a-zA-Z]{5,}$/;
          const STATE_CLASS = /--|^(active|focus|focused|hover|selected|disabled|open|opened|show|shown|fade|in|collapsed|expanded|checked)$|^(is|has)-/i;
          const libraryClass = (element) => [...(element.classList ?? [])].find((name) => name.length > 2 && !HASHED.test(name) &&
            !STATE_CLASS.test(name) && !/[0-9]{3,}/.test(name));
          // The element as CSS by what stays: its tag and a class of its library's, else its tag.
          const libraryCss = (element) => {
            const own = libraryClass(element);
            return own ? `${element.tagName.toLowerCase()}.${CSS.escape(own)}` : element.tagName.toLowerCase();
          };
          const shownText = (element) => clean(element?.innerText ?? element?.textContent ?? '');

          // What a person fills in, in a group: a field, a select's trigger, a group of checkboxes or radios as one.
          const FILLABLE = 'input:not([type=hidden]):not([type=button]):not([type=submit]):not([type=reset]):not([type=image]), ' +
            'textarea, select, [role=combobox], [role=textbox], [role=switch], [contenteditable=true], [tabindex="0"]:not(button):not(a)';
          function fieldsIn(group) {
            let toggles = 0;
            let others = 0;
            for (const field of group.querySelectorAll(FILLABLE)) {
              if (field.closest('[role=option], [role=listbox], [role=menu]') && !field.matches('[role=combobox]')) continue;
              if (field.matches(TOGGLES)) toggles = 1;
              else if (!field.parentElement?.closest(FILLABLE)) others++;
            }
            return toggles + others;
          }
          // Text a person reads as the label of what follows it: a label of no control, a legend, what is styled as one,
          // or an element of text alone, short.
          const LABEL_LIKE = 'label, legend, [class*=label i], [id*=label i], [class*=caption i]';
          const isLabelText = (element) => {
            const text = shownText(element);
            if (!text || text.length > 50 || !hasLetters(text) || element.querySelector(FILLABLE) || element.matches(FILLABLE)) return false;
            if (element.matches('label') && element.control) return false;
            return element.matches(LABEL_LIKE) || [...element.children].every((child) => /^(SMALL|SPAN|B|STRONG|EM|I|SUP)$/.test(child.tagName));
          };
          // The label of the element's form group: the closest container holding the element as its one field, and one
          // label of none other, before it. Null when the element has a label of its own, or shares the label with others.
          // The widget a field is part of, which shows its value and prompt in it: the nearest around it that handles clicks
          // or looks clickable, holding no other field, as react-select's control holds the value it shows.
          function widgetAround(element) {
            for (let node = element, depth = 0; node && depth < 4; node = upFrom(node), depth++) {
              if (node === document.body || node.matches(DIALOGS) || fieldsIn(node) > 1) break;
              if (node !== element && (hasClickHandler(node) || looksClickable(node))) return node;
            }
            return element;
          }
          const SHOWN_VALUE = /value|placeholder|selected|single|chip|tag|token|display/i;
          function groupOf(element) {
            if (!element || element === document.body) return null;
            const own = new Set([...(element.labels ?? [])]);
            const widget = widgetAround(element);
            for (let group = upFrom(element), depth = 0; group && depth < 7; group = upFrom(group), depth++) {
              if (group === document.body || group.matches(DIALOGS) || group.matches('form, table, [role=row], [role=table], [role=grid], tr')) break;
              if (fieldsIn(group) > 1) break;
              const labels = [...group.querySelectorAll(`${LABEL_LIKE}, div, p, span, h6, h5`)].filter((label) =>
                !own.has(label) && !containsDeep(label, element) && !containsDeep(widget, label) && isLabelText(label) &&
                !SHOWN_VALUE.test(label.getAttribute('class') ?? '') &&
                !(label.matches('label') && label.control) && visible(label) &&
                (label.compareDocumentPosition(element) & Node.DOCUMENT_POSITION_FOLLOWING) &&
                ![...label.children].some((child) => isLabelText(child) && shownText(child) === shownText(label)));
              const outer = labels.filter((label) => !labels.some((other) => other !== label && containsDeep(other, label)));
              if (outer.length === 1) return { label: shownText(outer[0]), group, labelElement: outer[0] };
              if (outer.length > 1) break;
            }
            return null;
          }
          // What a select's prompt says, which aria-describedby points at: react-select's "Select State".
          const describedBy = (element) => clean(idRefs(element, 'aria-describedby').map((ref) => ref.textContent).join(' ')) || null;

          // The heading of the section the element is in: the nearest one before it in what holds both.
          function sectionOf(element) {
            for (let holder = upFrom(element), depth = 0; holder && depth < 12; holder = upFrom(holder), depth++) {
              if (holder === document.body || holder.matches(DIALOGS)) return null;
              const headings = [...holder.querySelectorAll('h1, h2, h3, h4, h5, h6, [role=heading]')].filter((heading) =>
                visible(heading) && !containsDeep(heading, element) &&
                (heading.compareDocumentPosition(element) & Node.DOCUMENT_POSITION_FOLLOWING) && shownText(heading).length <= 60 &&
                hasLetters(shownText(heading)));
              if (headings.length) return { heading: shownText(headings[headings.length - 1]), holder };
            }
            return null;
          }

          // The icon an element with no words is all of: a class of an icon font (bi-trash, fa-pen, mdi-delete), the text
          // of a ligature font (material-icons "delete"), an SVG's data-icon or id (heart-svg). Its word, as the recorder
          // names the element ("trash" is Delete), and the CSS that finds the element by it.
          const ICON_CLASS = /^(bi|fa|fas|far|fab|fal|mdi|ti|ri|las|lar|glyphicon|icon|feather|ion|ph)-([a-z0-9-]+)$/;
          const ICON_SUFFIX = /-(fill|filled|outline|outlined|alt|o|lg|sm|solid|regular|line|light|bold)$/;
          function iconOf(element) {
            // What shows any text of its own is no icon alone; an icon font's glyph is no text.
            if (shownText(element).replace(/[\uE000-\uF8FF]/g, '').trim()) return null;
            for (const icon of [element, ...element.querySelectorAll('i, svg, span, use, img')]) {
              const iconClass = [...(icon.classList ?? [])].find((name) => ICON_CLASS.test(name) && !/^(fa|bi)-(fw|lg|xs|sm|\dx)$/.test(name));
              if (iconClass) {
                const word = iconClass.replace(ICON_CLASS, '$2').replace(ICON_SUFFIX, '');
                const css = icon === element ? `${element.tagName.toLowerCase()}.${CSS.escape(iconClass)}`
                  : `${element.tagName.toLowerCase()}:has(${icon.tagName.toLowerCase()}.${CSS.escape(iconClass)})`;
                return { word, css };
              }
              if (icon.matches?.('.material-icons, .material-symbols-outlined, .material-icons-outlined') && /^[a-z_]+$/.test(icon.textContent.trim()))
                return { word: icon.textContent.trim().replace(/_/g, '-'), css: null };
              const named = icon.getAttribute?.('data-icon') || (icon.tagName === 'svg' && icon.id ? icon.id.replace(/[-_](svg|icon)$/i, '') : '');
              if (named && /^[a-z][a-z-]*$/i.test(named)) return { word: named.toLowerCase(), css: null };
            }
            return null;
          }

          // A card or post among others like it: the outermost element of a class its library gave it that each card has, on
          // the way from the card to the element, and the first short text of the card no other card has.
          function cardOf(element) {
            for (let item = element, depth = 0; item && depth < 9; item = upFrom(item), depth++) {
              const parent = upFrom(item);
              if (!parent || parent === document.body || item.matches(DIALOGS) || item.matches('tr, [role=row]')) return null;
              const others = [...parent.children].filter((other) => other !== item && sameKind(other, item) && visible(other));
              if (others.length < 1 || !hasLetters(item.textContent ?? '')) continue;
              let holder = null;
              for (let node = item; node && node !== element; node = [...node.children].find((child) => containsDeep(child, element))) {
                const own = libraryClass(node);
                if (own && others.every((other) => other.matches(`.${CSS.escape(own)}`) || other.querySelector(`.${CSS.escape(own)}`))) { holder = node; break; }
              }
              if (!holder) return null;
              const walker = document.createTreeWalker(item, NodeFilter.SHOW_TEXT);
              for (let node = walker.nextNode(); node; node = walker.nextNode()) {
                const text = clean(node.textContent);
                if (text.length < 3 || text.length > 60 || !hasLetters(text) || !node.parentElement || !visible(node.parentElement)) continue;
                if (!others.some((other) => (other.textContent ?? '').includes(text))) return { item: holder, text };
              }
              return null;
            }
            return null;
          }

          // A checkbox under the box a library draws over it: a click at its middle lands on the box, which Playwright's
          // check does not take for the checkbox's own.
          function isCovered(element) {
            const box = element.getBoundingClientRect();
            if (box.width === 0 || box.height === 0) return false;
            const atMiddle = deepAt(box.left + box.width / 2, box.top + box.height / 2);
            return !!atMiddle && atMiddle !== element && !containsDeep(element, atMiddle);
          }

          // The candidates that find the element by what is around it: its form group's label, its section's heading, the
          // card it is in, the nearest stable id around it, its icon, its title, its place in a table's header or a
          // toolbar.
          function nearbyCandidates(element, scope, role, name, found) {
            const candidates = [];
            const add = (kind, parts, extra) => { candidates.push(candidate(kind, scope, parts, element, extra)); };
            const icon = iconOf(element);
            // The element within what holds it: by its role and name (exactly, else as a part of its name, as an icon
            // font's glyph is part of it), its icon, its class, its tag.
            const inners = role && name ? [[{ m: 'role', role, name, exact: true }], [{ m: 'role', role, name }]]
              : [icon?.css ? [{ m: 'css', css: icon.css }]
                : element.id && found(element.id) ? [{ m: 'css', css: `[id=${quote(element.id)}]` }]
                : role ? [{ m: 'role', role }]
                : [{ m: 'css', css: libraryCss(element) }]];
            const within = (kind, outer, extra) => {
              for (const inner of inners) {
                const tried = candidate(kind, scope, [...outer, ...inner], element, extra);
                if (tried.matches || inner === inners[inners.length - 1]) { candidates.push(tried); return; }
              }
            };
            // A field's group: a button or a link of a group is no field of it.
            const field = !element.matches('button, a[href], [role=button], [role=link], [role=menuitem], [role=tab], [role=option]');
            const group = field ? groupOf(element) : null;
            if (group) within('group', [{ m: 'css', css: libraryCss(group.group) }, { m: 'has', text: group.label }], { text: group.label });
            const section = sectionOf(element);
            if (section && (role === 'button' || role === 'link') && name)
              within('section', [{ m: 'css', css: libraryCss(section.holder) }, { m: 'has', text: section.heading }], { text: section.heading });
            const card = cardOf(element);
            if (card && libraryClass(card.item))
              within('item', [{ m: 'css', css: libraryCss(card.item) }, { m: 'filter', hasText: card.text }], { text: card.text });
            let ids = 0;
            for (let holder = upFrom(element), depth = 0; holder && depth < 8 && ids < 2; holder = upFrom(holder), depth++) {
              if (holder === document.body || holder.matches(DIALOGS)) break;
              if (!holder.id || /^\d|[\s'"\\]/.test(holder.id)) continue;
              ids++;
              within('within', [{ m: 'css', css: `[id=${quote(holder.id)}]` }], { value: holder.id });
            }
            if (icon?.css) add('icon', [{ m: 'css', css: icon.css }], { text: icon.word });
            const title = element.getAttribute('title');
            if (title && clean(title) && !hasLetters(shownText(element))) add('title', [{ m: 'title', text: clean(title) }], { text: clean(title) });
            if (role && !name) {
              const holder = closestUp(upFrom(element) ?? element, '[role=columnheader], [role=rowheader], th, [role=toolbar], [role=navigation], nav, [role=banner], header, [role=search], [role=menubar], [role=tablist]');
              const holderRole = holder ? roleOf(holder) ?? { TH: 'columnheader', NAV: 'navigation', HEADER: 'banner' }[holder.tagName] : null;
              if (holderRole) add('structure', [{ m: 'role', role: holderRole }, { m: 'role', role }], {});
            }
            return { candidates, group, section, icon };
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
                // A field found by its role alone is named by nothing it shows.
                roleText ??= text ?? variant.name ?? '';
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
              // An icon button of no name by its icon: the row's trash, not its first button.
              const rowIcon = !name ? iconOf(element) : null;
              const inner = row.element === element ? [] : name ? [{ m: 'role', role, name, exact: true }]
                : rowIcon?.css ? [{ m: 'css', css: rowIcon.css }] : [{ m: 'role', role }];
              candidates.push(candidate('row', scope, [...rowParts, ...inner], element, { text: name }));
            }
            if (role === 'option' && !candidates.some((found) => found.matches)) {
              // An option whose name another list has too, in its own list.
              const listbox = closestUp(element, '[role=listbox]');
              const listName = listbox ? clean(accessibleName(listbox)) : '';
              const name = clean(accessibleName(element));
              if (listName && name)
                candidates.push(candidate('role', scope, [{ m: 'role', role: 'listbox', name: listName, exact: true },
                  { m: 'role', role: 'option', name, exact: true }], element, { text: name }));
            }
            const ownName = clean(accessibleName(element));
            const nearby = nearbyCandidates(element, scope, role, ownName,
              (id) => (matches([{ m: 'css', css: `[id=${quote(id)}]` }])?.length ?? 0) > 1);
            candidates.push(...nearby.candidates);
            let labelText = null;
            if (!role || !NAME_FROM_CONTENT.has(role)) {
              const text = labelCandidate(element, scope);
              if (text) candidates.push(text);
              labelText = text?.text ?? null;
            }
            const generated = safe(() => engine()?.generateLocator(element, 'csharp'), null);
            if (generated) candidates.push({ kind: 'generator', csharp: generated, selector: safe(() => engine().selector(element), null), count: 1, matches: true });
            return {
              tag: tag.toLowerCase(), role, type: element.getAttribute('type'), id: element.id || null,
              name: nameAttribute, testId, value: element.matches('input[type=radio], input[type=checkbox]') ? element.value : null,
              text: roleText ?? (clean(accessibleName(element)) || labelText || ''),
              label: listLabel(element, role) ||
                clean(idRefs(element, 'aria-labelledby').filter((ref) => ref !== element && !containsDeep(element, ref)).map((ref) => ref.textContent).join(' ')) ||
                clean([...(element.labels || [])].map((label) => label.textContent).join(' ')) || null,
              covered: element.matches(TOGGLES) && isCovered(element) || undefined,
              groupLabel: nearby.group?.label || null,
              placeholder: element.getAttribute('placeholder') || describedBy(element),
              section: nearby.section?.heading || null,
              icon: nearby.icon?.word || null,
              editable: isTextual(element) ? !element.readOnly && !element.disabled : undefined,
              inForm: isTextual(element) && !!(element.form ?? element.closest('form')) || undefined,
              interactive: options.interactive ??
                (element.matches(INTERACTIVE) || element.matches(TOGGLES) || looksClickable(element) || actionable.has(element)),
              row: row ? { key: row.key, header: row.header } : null,
              // One of a list of options, or in one: a card, a menu's link.
              item: isItem(element) || undefined,
              candidates,
            };
          }

          function describe(element, extra = {}) {
            const dialog = dialogOf(element);
            const dialogInfo = dialog ? describeDialog(dialog) : null;
            return {
              dialog: dialogInfo,
              openDialogs: openDialogs().map(dialogTitle),
              target: describeTarget(element, dialog, dialogInfo, extra),
            };
          }

          // -------------------------------------------------------------------------------------------------- actions
          let lastAction = null;
          // What the page changes after a click is its doing until the next action begins.
          let changesWatched = null;
          const remember = (id) => {
            lastAction = { id, t: now() };
            if (changesWatched && changesWatched.id !== id) { changesWatched.observer.disconnect(); changesWatched = null; }
          };
          let lastRecorded = null;
          function record(action) {
            send({ type: 'action', url: location.href, ...action });
            if (recording()) lastRecorded = action.id;
          }
          // The click just before a choice that only opened its list: on the field, or on what holds it or is in it.
          const openerOf = (field) => lastClick && lastRecorded === lastClick.record.id && now() - lastClick.t < 60000 &&
            (containsDeep(lastClick.element, field) || containsDeep(field, lastClick.element)) ? lastClick.record.id : null;
          function effect(kind, data, actionId) {
            send({ type: 'effect', kind, t: now(), actionId: actionId ?? lastAction?.id ?? null,
              sinceAction: lastAction ? now() - lastAction.t : null, ...data });
          }

          // ------------------------------------------------------------------------------------- typing: settled values
          const typing = new Map();
          const valueOf = (element) => element.isContentEditable ? element.innerText : element.value;
          // What the person typed of a field that completes it in place, the rest of the value selected after it, as a
          // combobox suggests an option (Kendo's, the browser's datalist): the typed letters, not the suggestion.
          const typedOf = (element) => {
            const value = valueOf(element) ?? '';
            const { selectionStart: start, selectionEnd: end } = element.matches?.('input') ? element : {};
            return typeof start === 'number' && start > 0 && start < end && end === value.length
              && (/both|inline/.test(element.getAttribute('aria-autocomplete') ?? '') || element.getAttribute('role') === 'combobox')
              ? value.slice(0, start) : value;
          };
          function emitFill(entry, final) {
            clearTimeout(entry.timer);
            const raw = typedOf(entry.element);
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
          // A calendar: what holds a month of days, 28 of them at least, each a number of its own, under a month's name, a
          // year, or a class that says so; the outermost such around what was clicked, short of the field it fills.
          const DAY = /^\d{1,2}$/;
          const MONTHS = /\b(jan(uary)?|feb(ruary)?|mar(ch)?|apr(il)?|may|june?|july?|aug(ust)?|sep(tember)?|oct(ober)?|nov(ember)?|dec(ember)?|(19|20)\d\d)\b/i;
          function isCalendar(element) {
            let days = 0;
            const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT);
            for (let node = walker.nextNode(); node && days < 28; node = walker.nextNode())
              if (DAY.test(node.textContent.trim()) && node.parentElement?.children.length === 0) days++;
            return days >= 28 && (MONTHS.test(element.textContent ?? '') || /calendar|date|picker|month/i.test(element.getAttribute('class') ?? ''))
              // A calendar a library names so, in the view of the months of a year or of the years of a decade too, as
              // Kendo's does: its "Navigate to parent view: 2026" names today's year.
              || /calendar|datepicker|date-picker|picker-panel|picker-dropdown|picker-popup/i.test(element.getAttribute('class') ?? '')
                && element.querySelectorAll('td, [role=gridcell], button, a, li').length >= 10;
          }
          // The button that commits what a picker shows, as antd's OK: a date picked is kept only once it is pressed.
          const COMMITS = /^(ok|apply|done|set|confirm|select)$/i;
          const holdsTextField = (element) => !!element.querySelector('input:not([type=hidden]):not([type=checkbox]):not([type=radio]), textarea');
          function calendarOf(hit) {
            let calendar = null;
            for (let node = hit, depth = 0; node && depth < 12; node = upFrom(node), depth++) {
              if (node === document.body || holdsTextField(node)) break;
              if (isCalendar(node)) calendar = node;
              else if (calendar) break;
            }
            return calendar;
          }
          // The field a calendar fills: the one text field near it, else the one last focused.
          function fieldOfCalendar(calendar) {
            for (let holder = upFrom(calendar), depth = 0; holder && depth < 4; holder = upFrom(holder), depth++) {
              const fields = [...holder.querySelectorAll('input:not([type=hidden]):not([type=checkbox]):not([type=radio])')].filter((field) => !containsDeep(calendar, field));
              if (fields.length === 1) return fields[0];
              if (fields.length > 1) break;
            }
            return lastField?.isConnected ? lastField : null;
          }
          let lastField = null;
          addEventListener('focusin', (event) => safe(() => { const field = deepTarget(event); if (field && isTextual(field)) lastField = field; }), true);
          // A file chooser opened by what stands for it: a "Browse" beside the hidden file input of its group.
          function fileInputNear(hit) {
            if (closestUp(hit, 'button, a[href], [role=button], [role=link], input, select, textarea')) return null;
            for (let holder = hit, depth = 0; holder && depth < 4; holder = upFrom(holder), depth++) {
              if (holder === document.body) return null;
              const inputs = holder.querySelectorAll('input[type=file]');
              if (inputs.length > 1 || [...holder.querySelectorAll(INTERACTIVE)].some((other) => !other.matches('input[type=file]'))) return null;
              if (inputs.length === 1) return inputs[0];
            }
            return null;
          }
          // The calendar icon of a date field, MUI's adornment or one beside the field in its group.
          const fieldOfAdornment = (element) => {
            const mui = element.closest('.MuiInputAdornment-root')
              ?.closest('.MuiFormControl-root, .MuiTextField-root')?.querySelector('input:not([type=hidden])');
            if (mui) return mui;
            const said = `${element.getAttribute('class') ?? ''} ${element.getAttribute('aria-label') ?? ''} ${element.querySelector?.('i, svg, span')?.getAttribute('class') ?? ''}`;
            if (!/calendar|date|picker/i.test(said)) return null;
            for (let holder = upFrom(element), depth = 0; holder && depth < 3; holder = upFrom(holder), depth++) {
              const fields = holder.querySelectorAll('input:not([type=hidden]):not([type=checkbox]):not([type=radio])');
              if (fields.length === 1) return fields[0];
              if (fields.length > 1) return null;
            }
            return null;
          };
          const pickerOpen = (calendar) => calendar ? calendar.isConnected && visible(calendar)
            : [...document.querySelectorAll(PICKER)].some(visible);
          function calendarNear(field) {
            for (let holder = upFrom(field), depth = 0; holder && depth < 4; holder = upFrom(holder), depth++)
              for (const child of holder.querySelectorAll('div, table, section'))
                if (!containsDeep(child, field) && visible(child) && isCalendar(child)) return child;
            return null;
          }
          function watchPicker(field, calendar) {
            const description = describe(field);
            const before = valueOf(field);
            picker = { field, description, before, id: newId(), t: now() };
            const started = now();
            const poll = setInterval(() => safe(() => {
              const open = pickerOpen(calendar) || (!calendar && !!calendarNear(field));
              if (open && now() - started < 120000) return;
              clearInterval(poll);
              const state = picker;
              picker = null;
              if (!state || valueOf(state.field) === state.before) return;
              remember(state.id);
              record({ ...state.description, id: state.id, t: state.t, kind: 'fill', final: true, widget: 'date',
                value: normalizeValue(valueOf(state.field)), raw: valueOf(state.field), picked: true,
                ...(state.committed ? { committed: true } : {}) });
            }), 150);
          }

          // ------------------------------------------------------------------------------------------------ pointer
          let pending = null;
          let opened = null;
          let lastClick = null;
          let lastEnter = 0;
          let lastPointer = 0;

          // The field a listbox belongs to: what controls it, in the listbox's own document or shadow root.
          const ownerOf = (listbox) => {
            const selector = `[aria-controls="${CSS.escape(listbox.id)}"], [aria-owns="${CSS.escape(listbox.id)}"]`;
            const owners = !listbox.id ? []
              : [...new Set([...listbox.getRootNode().querySelectorAll(selector), ...document.querySelectorAll(selector)])]
                .filter((owner) => !containsDeep(listbox, owner) && owner.getAttribute('role') !== 'searchbox');
            const owner = owners.find((each) => each.matches('[role=combobox], [aria-haspopup], select, input')) ?? owners[0];
            if (owner) return owner;
            // Else the one trigger of a listbox that says it is open, as MUI's Select before it was a combobox.
            const open = [...new Set([...listbox.getRootNode().querySelectorAll('[aria-haspopup=listbox][aria-expanded=true]'),
              ...document.querySelectorAll('[aria-haspopup=listbox][aria-expanded=true]')])].filter((each) => !containsDeep(listbox, each));
            if (open.length === 1) return open[0];
            // Else the field typed into or the element clicked just before, when the list opened beside it, in what holds
            // both, as OrangeHRM's select and autocomplete open their lists in their own wrappers: what tells which field
            // the choice is of.
            const near = (element) => {
              if (!element?.isConnected || containsDeep(listbox, element)) return false;
              for (let holder = upFrom(element), depth = 0; holder && depth < 4; holder = upFrom(holder), depth++)
                if (containsDeep(holder, listbox)) return true;
              return false;
            };
            const typed = activeElement();
            if (typed && isTextual(typed) && near(typed)) return typed;
            if (near(lastField)) return lastField;
            return lastClick && now() - lastClick.t <= 60000 && near(lastClick.element) ? lastClick.element : null;
          };

          // An option without a role, as antd's virtual list draws them beside a hidden list for assistive technology: an
          // item of a list that opened in a popup of its own, while one combobox says it is open, which it is the choice of.
          function optionOfPopup(element) {
            const option = closestUp(element, '[class*=select-item-option], [class*=-option][class*=item], [class*=option][aria-selected]');
            if (!option || containsDeep(option, element) === false) return null;
            const open = [...document.querySelectorAll('[role=combobox][aria-expanded=true]')].filter((field) => !containsDeep(option, field));
            if (open.length !== 1) return null;
            let popup = option;
            while (upFrom(popup) && upFrom(popup) !== document.body && !/dropdown|popup|popover|listbox|menu/i.test(popup.getAttribute('class') ?? '')) popup = upFrom(popup);
            return { option, owner: open[0], popup };
          }

          function draftFor(hit) {
            const picked = pickedFor(hit);
            const element = picked ?? retarget(hit);
            if (hit.closest(PICKER)) return { ignore: 'picker' };
            // A day, a month or a year of a date picker's calendar: the flow types the date the field then shows.
            const calendar = calendarOf(hit);
            if (calendar) {
              const field = fieldOfCalendar(calendar);
              if (field && !picker) watchPicker(field, calendar);
              const button = closestUp(hit, 'button, [role=button]');
              if (picker && button && COMMITS.test(clean(button.innerText || button.textContent || ''))) picker.committed = true;
              return { ignore: 'picker' };
            }
            const fileInput = element.matches('input[type=file]') ? element
              : element.closest('label')?.control?.matches?.('input[type=file]') ? element.closest('label').control
              : fileInputNear(hit);
            if (fileInput) return { ignore: 'file chooser' };
            if (isTextual(element) && !element.readOnly && !fromDecoration.has(element)) return { ignore: 'focus', element };
            const toggle = element.matches(TOGGLES) ? element : null;
            const roleless = closestUp(element, '[role=option]') ? null : optionOfPopup(element);
            const option = closestUp(element, '[role=option]') ?? roleless?.option;
            if (option) {
              const listbox = roleless?.popup ?? closestUp(option, '[role=listbox]');
              const owner = roleless?.owner ?? (listbox ? ownerOf(listbox) : null);
              const info = describe(option, { pageLevel: true, noRow: true });
              // A Select's menu hides the rest of the page from assistive technology, so the field is described as it was
              // when its menu opened.
              const ownerInfo = !owner ? null : opened?.element === owner ? opened.info : describe(owner);
              return { element: option, owner, listbox, kind: 'choose',
                widget: !owner ? 'option' : isAutocomplete(owner) || isTextual(owner) && !owner.readOnly ? 'autocomplete'
                  : owner.tagName === 'SELECT' ? 'native' : 'select',
                option: info.target, ...(ownerInfo ?? info), field: ownerInfo?.target ?? null,
                ...(roleless ? { optionCss: libraryCss(option), optionText: shownText(option) } : {}),
                hoverMenu: hoverMenus.has(option.closest('[role=menu], [role=listbox]')),
                hoverOpener: hoverMenus.get(option.closest('[role=menu], [role=listbox]')) ?? null, scrolled: scrolledList(option) };
            }
            // An element the person picked is what they act on, whatever it is.
            const info = describe(element, picked ? { interactive: true } : {});
            if (isMuiSelect(element) || isAutocomplete(element)) opened = { element, info };
            const widget = widgetOf(element);
            const field = widget === 'autocomplete-button'
              ? describe(element.closest('.MuiAutocomplete-root').querySelector('input')).target : null;
            const equivalents = picked || widget ? [] : describeEquivalents(equivalentsOf(hit, element), info.target);
            return { element, kind: toggle ? 'toggle' : 'click', widget, ...info, field,
              ...(toggle && !toggle.matches('input') ? { before: toggle.getAttribute('aria-checked') === 'true' } : {}),
              ...(equivalents.length ? { equivalents } : {}),
              adornmentField: !isTextual(element) ? fieldOfAdornment(element) : null,
              hoverMenu: hoverMenus.has(element.closest('[role=menu]')), hoverOpener: hoverMenus.get(element.closest('[role=menu]')) ?? null,
              scrolled: scrolledList(element) };
          }

          // A list that renders only what is in sight, scrolled to the element: what the flow scrolls first, as far. It says
          // it has more options than it shows (aria-setsize), or what it shows is far shorter than what it scrolls.
          function scrolledList(element) {
            for (let list = upFrom(element); list && list !== document.body; list = upFrom(list)) {
              const style = styleOf(list);
              if (!style || !/auto|scroll/.test(style.overflowY) || list.scrollTop <= 0) continue;
              const items = [...list.querySelectorAll('[role=option], [role=row], [role=listitem], [role=treeitem], li')];
              const said = Number(closestUp(element, '[aria-setsize]')?.getAttribute('aria-setsize') ?? 0);
              const shown = items.reduce((sum, item) => sum + item.getBoundingClientRect().height, 0);
              if (!items.length || (said <= items.length && shown > list.scrollHeight * 0.6)) return null;
              return { top: Math.round(list.scrollTop), list: describe(list, { interactive: true }).target };
            }
            return null;
          }

          function finish(state, via) {
            if (!state || state.done) return;
            state.done = true;
            const { draft } = state;
            const { element, adornmentField, owner, listbox, ignore, before: _before, ...rest } = draft;
            if (draft.kind === 'toggle') {
              // A native checkbox has its new state as the click reaches it; a switch or checkbox of ARIA only once the app
              // handled the click, after this listener, and a framework rendered it, maybe a moment later (antd's, Kendo's).
              // The state is told as the page then shows it: at once as the click turns it, then as it settled.
              const on = () => element.matches('input') ? element.checked : element.getAttribute('aria-checked') === 'true';
              const tell = (checked, final) => record({ ...rest, id: state.id, t: state.t,
                kind: checked || draft.widget === 'radio' ? 'check' : 'uncheck', checked, via, ...(final ? {} : { final: false }) });
              if (element.matches('input')) { tell(on(), true); return; }
              const before = draft.before ?? on();
              tell(!before, false);
              let waited = 0;
              const settle = () => safe(() => {
                const now = element.isConnected ? on() : !before;
                if (now !== before || waited >= 400) { tell(now, true); return; }
                waited += 25;
                setTimeout(settle, 25);
              });
              setTimeout(settle, 0);
              return;
            }
            if (draft.kind === 'choose') {
              // An option of no role by the text it shows, as its name may come from a title no person wrote.
              const { optionText, ...told } = rest;
              const value = optionText || draft.option.text;
              record({ ...told, id: state.id, t: state.t, via, value, opener: owner ? openerOf(owner) : null });
              if (owner) settleChoice(state.id, owner, listbox, value);
              return;
            }
            if (adornmentField) {
              // The picker's calendar opens a moment later; until then the click is held back.
              setTimeout(() => safe(() => {
                if ([...document.querySelectorAll(PICKER)].some(visible)) { watchPicker(adornmentField); return; }
                const calendar = calendarNear(adornmentField);
                if (calendar) { watchPicker(adornmentField, calendar); return; }
                emitClick(state, rest, element, via);
              }), 200);
              return;
            }
            emitClick(state, rest, element, via);
          }

          // Whether the page changed after a click, other than a field's focus or the pointer's hover: an element added or
          // removed, a text or a state changed. A click on what is no control that changed nothing is noise.
          const FLEETING = /focus|hover|active|ripple|pressed|touch|keyboard/i;
          function watchChanges(id) {
            if (!document.body) return;
            const observer = new MutationObserver((mutations) => safe(() => {
              const changed = mutations.some((mutation) => {
                if (mutation.type === 'childList')
                  return [...mutation.addedNodes, ...mutation.removedNodes].some((node) => node.nodeType === 1 || node.textContent.trim());
                if (mutation.type === 'characterData') return !!mutation.target.textContent.trim();
                // A style that shows or hides something is a change; one that animates it is not.
                if (mutation.attributeName === 'style') {
                  const shown = (style) => /(display|visibility)\s*:\s*(none|hidden)/i.test(style ?? '');
                  return shown(mutation.oldValue) !== shown(mutation.target.getAttribute('style'));
                }
                if (['tabindex', 'aria-activedescendant'].includes(mutation.attributeName)) return false;
                if (mutation.attributeName === 'class') {
                  const before = new Set((mutation.oldValue ?? '').split(/\s+/).filter(Boolean));
                  const after = new Set([...mutation.target.classList]);
                  const turned = [...after].filter((name) => !before.has(name)).concat([...before].filter((name) => !after.has(name)));
                  return turned.some((name) => !FLEETING.test(name));
                }
                return !/^data-(focus|hover)/.test(mutation.attributeName ?? '');
              });
              if (changed) { observer.disconnect(); effect('changed', {}, id); }
            }));
            observer.observe(document.body, { subtree: true, childList: true, characterData: true, attributes: true, attributeOldValue: true });
            changesWatched = { id, observer };
            setTimeout(() => observer.disconnect(), 900);
          }

          function emitClick(state, rest, element, via) {
            // The focus it left in a text field, the element itself or one in it: such a click may only have led to typing.
            const focused = activeElement();
            const focus = !!focused && isTextual(focused) && !focused.readOnly && (focused === element || containsDeep(element, focused));
            const action = { ...rest, id: state.id, t: state.t, kind: 'click', via, watched: true, ...(focus ? { focus } : {}) };
            record(action);
            if (changesWatched?.id !== state.id) watchChanges(state.id);
            lastClick = { element, t: state.t, record: action };
            if (rest.widget === 'tab') setTimeout(() => safe(() => {
              if (element.getAttribute('aria-selected') === 'true') effect('tabSelected', {}, state.id);
            }), 300);
          }

          // The field shows the choice once its list has closed. A field of several choices shows each with a button that
          // removes it ("Remove Maths"), or says so of its list, as a multi-select does.
          function settleChoice(id, owner, listbox, chosen) {
            const started = now();
            const multiple = listbox?.getAttribute('aria-multiselectable') === 'true' || owner.multiple === true;
            const poll = setInterval(() => safe(() => {
              const open = listbox?.isConnected && visible(listbox);
              if (open && now() - started < 1500) return;
              clearInterval(poll);
              const shown = owner.tagName === 'INPUT' || owner.tagName === 'SELECT' ? owner.value : owner.innerText || owner.textContent;
              let root = owner;
              for (let depth = 0; depth < 4 && upFrom(root) && upFrom(root) !== document.body && fieldsIn(upFrom(root)) <= 1; depth++) root = upFrom(root);
              const removable = !!chosen && [...root.querySelectorAll('button, [role=button]')].some((button) => {
                const said = clean(accessibleName(button));
                return said.includes(chosen) && /remove|delete|clear|unselect|deselect/i.test(said);
              });
              effect('settled', { value: normalizeValue(normalize(shown)), raw: shown, closed: !open, multi: multiple || removable }, id);
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
            // From the press on: a select opens its list as it is pressed.
            watchChanges(pending.id);
          }), { capture: true, passive: true });

          addEventListener('pointerup', (event) => safe(() => {
            if (!event.isTrusted || !pending) return;
            const state = pending;
            if (Math.hypot(event.clientX - state.x, event.clientY - state.y) > 8) {
              pending = null;
              state.done = true;
              // A link or an image moved as it was clicked is the browser's own drag of it, which drops nowhere: nothing.
              if (nativeDrag(state.draft.element)) return;
              const drop = dropTarget(event, state);
              const { element, adornmentField, owner, listbox, ...rest } = state.draft;
              // On a table, the cell dropped on, as a scheduler's slot is.
              const cell = drop ? closestUp(drop, CELLS) : null;
              record({ ...rest, id: state.id, t: state.t, kind: 'drag', to: drop ? describe(cell ?? retarget(drop)).target : null });
              return;
            }
            // A MUI Select opens on mousedown and its menu then takes the click: without a click on the element, the press
            // itself was the action.
            setTimeout(() => safe(() => { if (pending === state) pending = null; finish(state, 'pointer'); }), 80);
          }), { capture: true, passive: true });

          // Where a drag dropped: the topmost element under the pointer that was there before the drag, so neither the item
          // dragged, which the app may have moved there already, nor a hint a library moves with the pointer naming where
          // it would drop (Kendo's scheduler), nor anything that copies the item dragged.
          const DRAG_HINT = /drag|hint|clue|ghost|placeholder|preview|helper|chosen|dragging/i;
          function dropTarget(event, state) {
            const dragged = state.draft.element;
            const said = clean(dragged?.innerText ?? dragged?.textContent ?? '');
            const copies = (element) => !!said && clean(element.innerText ?? element.textContent ?? '') === said;
            for (const element of document.elementsFromPoint(event.clientX, event.clientY)) {
              if (!dragged || element === dragged || containsDeep(dragged, element) || containsDeep(element, dragged) && element !== document.body) continue;
              let hint = false;
              for (let node = element, depth = 0; node && node !== document.body && depth < 4; node = upFrom(node), depth++)
                if (DRAG_HINT.test(node.getAttribute?.('class') ?? '') || copies(node)) { hint = true; break; }
              if (!hint) return element;
            }
            return deepTarget(event);
          }

          addEventListener('pointercancel', () => safe(() => { pending = null; }), { capture: true, passive: true });
          const nativeDrag = (element) => !!element && !closestUp(element, '[draggable=true]') &&
            (element.matches('a[href], img') || !!closestUp(element, 'a[href]'));
          addEventListener('dragstart', (event) => safe(() => {
            if (!event.isTrusted || !pending) return;
            const state = pending;
            pending = null;
            state.done = true;
            if (nativeDrag(state.draft.element)) return;
            const { element, adornmentField, owner, listbox, ...rest } = state.draft;
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
          // A box follows the pointer over the element a click would record, labelled with what the click does, the
          // locator the flow would use, how many it finds, and once the pointer rests, why the ones tried before it lost, as
          // the recorder's own policy answers through window.__qaasExplain. One box in every mode, its colour and its tag
          // saying which: recording, an element picked with Alt+Up, check mode, trying, inspect mode, and what the panel
          // points at. It is drawn in a closed shadow root, which neither the page nor Playwright's selectors see; it never
          // takes the pointer or the focus, and takes no room. It glides from element to element, drawn once a frame, and
          // steps aside while the person drags or selects, or is on a scrollbar. F9 hides or shows it. While it shows,
          // Alt+Up picks the element around the one in the box and Alt+Down goes back toward the element under the pointer;
          // a click then records the element picked. Moving to another element drops the pick. A pill at the top says the
          // mode the recording is in, when it is not plain recording, and fades when the pointer comes near it.
          const OVERLAY_CSS = `
            :host { all: initial !important; display: block !important; position: fixed !important; top: 0 !important;
              left: 0 !important; width: 0 !important; height: 0 !important; z-index: 2147483647 !important;
              pointer-events: none !important; }
            .box, .label, .mark, .tip, .extra { position: fixed; top: 0; left: 0; display: none; box-sizing: border-box;
              pointer-events: none; }
            [data-mode] { --mode: #4f55e0; }
            [data-mode=picked] { --mode: #d97706; }
            [data-mode=check] { --mode: #16a34a; }
            [data-mode=try] { --mode: #0e9aa7; }
            [data-mode=inspect] { --mode: #8b5cf6; }
            .box, .mark, .extra { border: 2px solid var(--mode); border-radius: 3px;
              background: color-mix(in srgb, var(--mode) 10%, transparent);
              box-shadow: 0 0 0 1px rgba(255, 255, 255, .85), 0 0 0 3px color-mix(in srgb, var(--mode) 16%, transparent); }
            .extra { border-width: 1.5px; border-style: dashed; box-shadow: 0 0 0 1px rgba(255, 255, 255, .7); }
            .label, .tip { max-width: min(460px, calc(100vw - 12px)); padding: 6px 9px 7px; border-radius: 8px;
              background: rgba(20, 22, 27, .97); color: #d6dae0; direction: ltr; unicode-bidi: isolate; text-align: left;
              font: 12px/1.45 'Segoe UI Variable Text', 'Segoe UI', Inter, system-ui, -apple-system, Roboto, sans-serif;
              overflow-wrap: anywhere; box-shadow: 0 8px 24px rgba(0, 0, 0, .26), 0 1px 3px rgba(0, 0, 0, .3),
              0 0 0 1px rgba(255, 255, 255, .14); }
            .glide { transition: transform 90ms cubic-bezier(.2, .7, .2, 1), width 90ms cubic-bezier(.2, .7, .2, 1),
              height 90ms cubic-bezier(.2, .7, .2, 1); }
            @media (prefers-reduced-motion: reduce) { .glide { transition: none; } }
            .stale .head, .stale .loc, .stale .problem { opacity: .5; }
            .head { display: flex; align-items: center; gap: 7px; min-width: 0; }
            .head::before { content: attr(data-tag); flex: none; padding: 0 6px; border-radius: 4px; color: #fff;
              background: var(--mode); font-size: 10.5px; font-weight: 650; line-height: 17px; }
            .what { min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; color: #f4f5f7;
              font-weight: 600; unicode-bidi: plaintext; }
            .loc { display: flex; align-items: baseline; gap: 6px; margin-top: 3px; }
            .dot { flex: none; align-self: center; width: 7px; height: 7px; border-radius: 50%; background: #4cc38a; }
            .fragile .dot { background: #f1b44c; }
            .plain .dot { display: none; }
            .locator, .reasons, .pick { font-family: ui-monospace, 'Cascadia Mono', 'JetBrains Mono', SFMono-Regular, Menlo,
              Consolas, 'DejaVu Sans Mono', monospace; }
            .locator { min-width: 0; color: #e9ebee; font-size: 11.5px; }
            .locator b { color: #c6a2ff; font-weight: 600; }
            .locator i { color: #86b4ff; font-style: normal; }
            .count { flex: none; color: #4cc38a; font-size: 11px; }
            .fragile .count { color: #f1b44c; }
            .plain .count { color: #a3aab4; }
            .problem { margin-top: 3px; color: #f1b44c; font-size: 11.5px; }
            .more { display: none; margin-top: 5px; padding-top: 4px; border-top: 1px solid rgba(255, 255, 255, .1); }
            .rest .more, .picked .more { display: block; }
            .reasons, .note { color: #a3aab4; font-size: 11px; }
            .pick { color: #a3aab4; font-size: 11px; }
            .pick b { color: #c6a2ff; font-weight: 600; }
            .pick i { color: #86b4ff; font-style: normal; }
            .pick .on { padding: 0 3px; border-radius: 3px; background: rgba(217, 119, 6, .3); color: #fbbf24; }
            .pick .on b, .pick .on i { color: #fbbf24; }
            .pick .keys { margin-left: 8px; color: #848c97; font-family: 'Segoe UI', system-ui, sans-serif; }
            .pill, .chooser { --canvas: #f6f7f9; --panel: #ffffff; --raised: #f1f3f6; --line: #e3e6eb; --strong: #cfd4db;
              --fg: #16181d; --muted: #555d69; --faint: #6b7380; --accent: #4f55e0; --ok: #157a4a;
              position: fixed; box-sizing: border-box; direction: ltr; text-align: left; color: var(--fg);
              background: var(--panel); border: 1px solid var(--strong);
              font: 12.5px/1.45 Inter, 'Segoe UI Variable Text', 'Segoe UI', system-ui, -apple-system, Roboto, sans-serif;
              box-shadow: 0 12px 32px rgba(16, 24, 40, .18), 0 2px 6px rgba(16, 24, 40, .1); }
            @media (prefers-color-scheme: dark) {
              .pill, .chooser { --canvas: #0d0e11; --panel: #1a1d22; --raised: #23272d; --line: #2a2f36; --strong: #3a4049;
                --fg: #e9ebee; --muted: #a3aab4; --faint: #848c97; --accent: #5e63ea; --ok: #4cc38a;
                box-shadow: 0 12px 32px rgba(0, 0, 0, .5), 0 0 0 1px rgba(255, 255, 255, .04); }
            }
            .pill { --tone: #e5484d; top: 10px; left: 50%; transform: translateX(-50%); display: none; align-items: center;
              gap: 8px; max-width: calc(100vw - 24px); padding: 4px 5px 4px 11px; border-radius: 999px; white-space: nowrap;
              pointer-events: none; transition: opacity 120ms ease-out; }
            .pill.shown { display: flex; }
            .pill.near { opacity: .18; }
            .pill.interactive { pointer-events: auto; }
            .pill::before { content: ''; flex: none; width: 8px; height: 8px; border-radius: 50%; background: var(--tone);
              box-shadow: 0 0 0 3px color-mix(in srgb, var(--tone) 22%, transparent); }
            .pill[data-tone=warn] { --tone: #d68f00; }
            .pill[data-tone=info] { --tone: #1d7ae0; }
            .pill[data-tone=accent] { --tone: #5e63ea; }
            .pill[data-tone=try] { --tone: #0e9aa7; }
            .pill[data-tone=ok] { --tone: #16a34a; }
            .pill[data-tone=inspect] { --tone: #8b5cf6; }
            .pill b { font-weight: 600; }
            .pill .hint { min-width: 0; overflow: hidden; text-overflow: ellipsis; color: var(--muted); }
            .pill kbd:last-child { margin-right: 4px; }
            .chooser button, .pill button { font: inherit; color: inherit; cursor: pointer; }
            .pill .url { height: 24px; padding: 0 10px; border-radius: 999px; border: 1px solid var(--strong);
              background: var(--raised); font-size: 12px; font-weight: 500; }
            .pill .url:hover { border-color: var(--faint); }
            kbd { display: inline-block; padding: 0 5px; border-radius: 4px; border: 1px solid var(--strong);
              background: var(--canvas); color: var(--muted); font: 500 10.5px/16px Inter, 'Segoe UI', system-ui, sans-serif; }
            .chooser { width: 348px; max-width: calc(100vw - 16px); border-radius: 12px; overflow: hidden; pointer-events: auto; }
            .chooser .head { padding: 10px 12px 9px; border-bottom: 1px solid var(--line); }
            .chooser .head::before { content: none; }
            .chooser .badge { display: inline-flex; align-items: center; gap: 5px; flex: none; padding: 1px 8px 1px 6px;
              border-radius: 999px; font-size: 11.5px; font-weight: 600; color: var(--ok);
              background: color-mix(in oklab, var(--ok) 14%, transparent); }
            .chooser .badge svg { width: 12px; height: 12px; }
            .chooser .what { flex: 1; color: var(--muted); font-weight: 400; font: 11.5px/1.4 ui-monospace, 'Cascadia Mono', 'JetBrains Mono',
              Menlo, Consolas, monospace; }
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
            .chooser .field input { width: 100%; height: 30px; box-sizing: border-box; padding: 0 9px; border-radius: 6px;
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
              padding: 0 10px; border: 0; border-radius: 6px; background: var(--accent); color: #fff; font-size: 12px;
              font-weight: 600; }
            .chooser .add kbd { border: 0; background: rgba(255, 255, 255, .2); color: #fff; }
            .chooser .add:disabled { opacity: .45; cursor: default; }`;
          // Each quoted name keeps its own direction, so a right-to-left name cannot reorder the rest of the line.
          const isolateQuoted = (text) => text.replace(/"[^"]*"/g, (quoted) => range(0x2068) + quoted + range(0x2069));
          const reducedMotion = matchMedia('(prefers-reduced-motion: reduce)');
          let overlayOn = true;
          let view = null;
          // What the pointer is over: the element hit, the one a click there targets, the one picked instead with the
          // Alt+arrows, the one described, the one the recorder records it on (boxed), and the recorder's answer.
          let hover = null;
          let explaining = null;
          let explainAgain = false;
          let explainTimer = 0;
          let explainBy = 0;
          let explainSeq = 0;
          let restTimer = 0;
          // The label shows the last answer, dimmed, until the next one comes: no flicker on the way between elements.
          let labelled = false;
          // Where the pointer is, which the label never covers; where it went down, to tell a drag from a click.
          let pointer = { x: -1, y: -1 };
          let downAt = null;
          let dragging = false;

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
            const head = part(label, 'head');
            const what = part(head, 'what');
            const loc = part(label, 'loc');
            part(loc, 'dot');
            const locator = part(loc, 'locator');
            const count = part(loc, 'count');
            const problem = part(label, 'problem');
            const more = part(label, 'more');
            view = { host, root, box, label, head, what, loc, locator, count, problem, more, reasons: part(more, 'reasons'),
              note: part(more, 'note'), pick: part(more, 'pick'), pill: part(root, 'pill') };
            box.dataset.mode = label.dataset.mode = 'record';
            // What is done in the chooser and the pill stays in them: the page sees none of it.
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

          // The pill at the top: the mode the recording is in, or, for a moment, what a key did. Its tone is the mode's
          // colour, as the panel shows it; its key, the one that leaves the mode.
          let pillTimer = 0;
          function showPill(tone, text, hint, key, transient) {
            clearTimeout(pillTimer);
            const { pill } = overlayView();
            pill.replaceChildren();
            pill.className = 'pill shown';
            pill.dataset.tone = tone;
            pill.appendChild(document.createElement('b')).textContent = text;
            if (hint) {
              const span = pill.appendChild(document.createElement('span'));
              span.className = 'hint';
              span.textContent = hint;
            }
            if (key) pill.appendChild(document.createElement('kbd')).textContent = key;
            if (transient) pillTimer = setTimeout(() => safe(showState), 2500);
            return pill;
          }
          function showState() {
            clearTimeout(pillTimer);
            if (checking || inspecting) return;
            if (state === 'paused') showPill('warn', 'Recording paused', 'nothing is recorded · resume', 'F8');
            else if (state === 'testing') showPill('accent', 'Testing the flow', part ? 'replaying it here · nothing is recorded' : 'in its own window · nothing is recorded');
            else if (state === 'login' && loginText) showPill('info', loginText, loginHint);
            else if (state === 'trying') showPill('try', 'Trying', 'nothing is recorded · record again', 'F7');
            else if (state === 'recording' && part) showPill('rec', part, 'do it again, then', 'F2');
            else if (view) view.pill.className = 'pill';
          }
          // The pill fades while the pointer is near it, so what is under it can be seen and clicked.
          function nearPill() {
            const pill = view?.pill;
            if (!pill?.classList.contains('shown')) return;
            const box = pill.getBoundingClientRect();
            const near = pointer.x > box.left - 24 && pointer.x < box.right + 24 && pointer.y < box.bottom + 24;
            pill.classList.toggle('near', near && !pill.classList.contains('interactive'));
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

          // Drawn once a frame at most, however fast the pointer moves or the page scrolls.
          let drawFrame = 0;
          const scheduleDraw = () => {
            if (!drawFrame) drawFrame = requestAnimationFrame(() => { drawFrame = 0; safe(drawOverlay); });
          };
          const modeOf = () => (checking ? 'check' : hover?.picked ? 'picked' : state === 'trying' ? 'try' : 'record');

          // The box over the element, and the label beside it.
          function drawOverlay() {
            const element = hover?.boxed ?? hover?.shown;
            if (!(overlayOn || checking) || dragging || !element?.isConnected) { hideOverlay(); return; }
            const rect = element.getBoundingClientRect();
            if (!rect.width && !rect.height) { hideOverlay(); return; }
            const { box, label } = overlayView();
            const glide = box.style.display === 'block' && !reducedMotion.matches;
            box.dataset.mode = label.dataset.mode = modeOf();
            box.classList.toggle('glide', glide);
            Object.assign(box.style, { display: 'block', transform: `translate(${rect.left}px, ${rect.top}px)`,
              width: `${rect.width}px`, height: `${rect.height}px` });
            if (!labelled || chooser) { label.style.display = 'none'; return; }
            const showing = label.style.display === 'block';
            label.style.display = 'block';
            const { left, top } = placeLabel(rect, label.getBoundingClientRect());
            label.classList.toggle('glide', showing && !reducedMotion.matches);
            label.style.transform = `translate(${left}px, ${top}px)`;
          }

          // Below the element, else above it, else inside it at the edge away from the pointer; on the screen, and never
          // over the pointer.
          function placeLabel(rect, size) {
            const gap = 6;
            const edge = 6;
            const fits = (top) => top >= edge && top + size.height <= innerHeight - edge;
            const left = Math.max(edge, Math.min(rect.left, innerWidth - size.width - edge));
            const covers = (top) => pointer.x >= left - 4 && pointer.x <= left + size.width + 4 && pointer.y >= top - 4 &&
              pointer.y <= top + size.height + 4;
            const below = rect.bottom + gap;
            const above = rect.top - gap - size.height;
            const insideTop = Math.max(edge, rect.top + gap);
            const insideBottom = Math.min(innerHeight - edge, rect.bottom - gap) - size.height;
            const away = Math.abs(pointer.y - insideTop) > Math.abs(pointer.y - insideBottom - size.height) ? [insideTop, insideBottom] : [insideBottom, insideTop];
            const choices = [below, above, ...away].filter(fits);
            const top = choices.find((choice) => !covers(choice)) ?? choices[0] ?? Math.max(edge, innerHeight - size.height - edge);
            // Still over the pointer: beside it instead.
            if (covers(top)) {
              const right = pointer.x + 16;
              return { left: right + size.width <= innerWidth - edge ? right : Math.max(edge, pointer.x - 16 - size.width), top };
            }
            return { left, top };
          }

          // What a click there does, as the tag of the label says it.
          const verbFor = (element) => element.matches(TOGGLES) ? (roleOf(element) === 'radio' ? 'Pick' : 'Tick')
            : isTextual(element) ? 'Fill'
            : element.tagName === 'SELECT' || closestUp(element, '[role=option]') ? 'Choose'
            : 'Click';
          // What it is, as a person reads it: its role and name, else its tag.
          const whatOf = (element) => {
            const role = roleOf(element);
            const name = clean(accessibleName(element)).slice(0, 80);
            return role ? (name ? `${role} “${name}”` : role) : tagOf(element);
          };
          // Why the locators before the one chosen lost, without those the element simply has none of.
          const shortReasons = (reasons) => (reasons ?? []).filter((reason) => !/: (none|no name)$/.test(reason)).slice(0, 3).join('  ·  ');

          function fillLabel(answer, state) {
            const parts = overlayView();
            const line = (part, text) => { part.textContent = text || ''; part.style.display = text ? 'block' : 'none'; };
            const element = state.picked ?? state.shown;
            const verb = verbFor(element);
            parts.head.dataset.tag = checking ? 'Check' : state.picked ? `Picked · ${verb}` : modeOf() === 'try' ? `Try · ${verb}` : verb;
            parts.what.textContent = whatOf(element);
            parts.locator.textContent = answer.locator ? isolateQuoted(answer.locator) : 'nothing finds it';
            parts.loc.classList.toggle('fragile', !!answer.problem || !answer.locator);
            parts.loc.classList.remove('plain');
            parts.count.textContent = answer.count > 1 ? `found ${answer.count}` : answer.count === 1 ? '1 match' : '';
            line(parts.problem, answer.problem);
            line(parts.reasons, shortReasons(answer.reasons));
            line(parts.note, checking ? '' : answer.note);
            crumbs(parts.pick, state);
            parts.more.style.display = parts.reasons.textContent || parts.note.textContent || state.chain ? '' : 'none';
            parts.label.classList.remove('stale');
            parts.label.classList.toggle('picked', !!state.chain);
            labelled = true;
            // The details, once the pointer rests on it.
            clearTimeout(restTimer);
            restTimer = setTimeout(() => safe(() => {
              if (hover !== state) return;
              parts.label.classList.add('rest');
              scheduleDraw();
            }), 450);
          }

          const tagOf = (element) => element.tagName.toLowerCase() + (element.id ? `#${element.id}`
            : typeof element.className === 'string' && element.className.trim() ? `.${element.className.trim().split(/\s+/)[0]}` : '');
          // An element in a breadcrumb as Inspect names it: its tag, then its id or its first class, here without the hash a
          // framework put in it (css-1x2y3z-card gives card).
          const crumbOf = (element) => {
            const crumb = document.createElement('span');
            crumb.appendChild(document.createElement('b')).textContent = element.tagName.toLowerCase();
            const name = [...element.classList].map((each) => each.replace(/^(css|sc|jss)-[a-z0-9]+-?/i, '').replace(/__[A-Za-z0-9-]{4,}$/, ''))
              .find((each) => each.length > 1);
            if (element.id) crumb.appendChild(document.createElement('i')).textContent = `#${element.id}`;
            else if (name) crumb.append(`.${name}`);
            return crumb;
          };

          // The elements Alt+Up and Alt+Down step through, as a breadcrumb from the outermost: the one picked marked, the
          // keys after it. Shown once the person picked, also back at the element a click records by itself.
          function crumbs(part, state) {
            part.replaceChildren();
            if (!state?.chain) { part.style.display = 'none'; return; }
            part.style.display = 'block';
            const from = Math.max(0, state.index - 2);
            const to = Math.min(state.chain.length, state.index + 4);
            const shown = state.chain.slice(from, to).reverse();
            if (to < state.chain.length) part.append(`${range(0x2026)} ${range(0x203a)} `);
            shown.forEach((element, at) => {
              if (at > 0) part.append(` ${range(0x203a)} `);
              const crumb = part.appendChild(crumbOf(element));
              if (element === state.chain[state.index]) crumb.className = 'on';
            });
            if (from > 0) part.append(` ${range(0x203a)} ${range(0x2026)}`);
            const keys = part.appendChild(document.createElement('span'));
            keys.className = 'keys';
            keys.textContent = `Alt+${range(0x2191)} ${range(0x2193)}`;
          }
          const hoverOver = (hit) => {
            const element = checking ? checkTarget(hit) : retarget(hit);
            return { hit, element, picked: null, shown: checking ? element : closestUp(element, '[role=option]') ?? element,
              boxed: null, answer: null, at: 0 };
          };

          // The element picked with the Alt+arrows, while the pointer is still on what it was picked from.
          const pickedFor = (hit) => (overlayOn && hover?.hit === hit && hover.picked) || null;

          // Steps the pick through the elements from the one under the pointer up to the body: up by Alt+Up, down by Alt+Down.
          // The elements from the one under the pointer out, through shadow roots, up to its dialog, or below the root of the
          // app or of a portal: never the page's own containers. Those that take no room (a slot) are passed over.
          function chainOf(state) {
            const chain = [];
            for (let node = containsDeep(state.element, state.hit) ? state.hit : state.element;
                 node && node !== document.body && node !== document.documentElement; node = upFrom(node)) {
              if (node !== state.element && upFrom(node) === document.body) break;
              if (node === state.element || node.getClientRects().length > 0) chain.push(node);
              if (node.matches(DIALOGS)) break;
            }
            return chain;
          }

          function pick(step) {
            const state = hover;
            if (!state) return;
            if (!state.chain) {
              state.chain = chainOf(state);
              state.index = state.chain.indexOf(state.element);
            }
            const index = Math.min(Math.max(state.index + step, 0), state.chain.length - 1);
            if (index < 0) return;
            if (index === state.index) {
              // At either end: the breadcrumb shows it.
              if (state.answer) fillLabel(state.answer, state);
              scheduleDraw();
              return;
            }
            state.index = index;
            state.picked = state.chain[index] === state.element ? null : state.chain[index];
            state.shown = state.picked ?? closestUp(state.element, '[role=option]') ?? state.element;
            state.boxed = null;
            state.answer = null;
            view?.label.classList.add('stale');
            scheduleDraw();
            scheduleExplain(0);
          }

          // Asked once the pointer settles on an element, so passing over many asks about none of them; at the latest a
          // moment after it began to move.
          function scheduleExplain(wait = 90) {
            clearTimeout(explainTimer);
            if (!explainBy) explainBy = now() + 300;
            explainTimer = setTimeout(() => { explainTimer = 0; explainBy = 0; safe(explain); }, Math.max(0, Math.min(wait, explainBy - now())));
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
              fillLabel(state.answer, state);
              if (chooser?.element === shown && state.answer.locator) chooser.what.textContent = state.answer.locator;
              scheduleDraw();
            }
            if (explainAgain) { explainAgain = false; scheduleExplain(0); }
          }

          // On a scrollbar of the element: the page's own, or one of a box that scrolls.
          function onScrollbar(element, x, y) {
            if (element === document.documentElement) return true;
            if (element.scrollHeight <= element.clientHeight && element.scrollWidth <= element.clientWidth) return false;
            const box = element.getBoundingClientRect();
            const inX = x - box.left - element.clientLeft;
            const inY = y - box.top - element.clientTop;
            return (element.scrollHeight > element.clientHeight && inX > element.clientWidth) ||
              (element.scrollWidth > element.clientWidth && inY > element.clientHeight);
          }

          let moveFrame = 0;
          let moved = null;
          addEventListener('pointermove', (event) => safe(() => {
            if (!event.isTrusted) return;
            pointer = { x: event.clientX, y: event.clientY };
            moved = { hit: deepTarget(event), buttons: event.buttons };
            if (!moveFrame) moveFrame = requestAnimationFrame(() => { moveFrame = 0; safe(() => movedTo(moved)); });
          }), { capture: true, passive: true });
          addEventListener('pointerdown', (event) => { if (event.isTrusted) downAt = { x: event.clientX, y: event.clientY }; },
            { capture: true, passive: true });
          addEventListener('pointerup', () => safe(() => {
            downAt = null;
            if (dragging) { dragging = false; scheduleDraw(); }
          }), { capture: true, passive: true });

          // What the pointer moved onto, once a frame.
          function movedTo({ hit, buttons }) {
            nearPill();
            // Dragging, or selecting text: the overlay steps aside until the button is let go.
            dragging = buttons !== 0 && !checking && !!downAt && Math.hypot(pointer.x - downAt.x, pointer.y - downAt.y) > 6;
            if (dragging) { hideOverlay(); return; }
            if (chooser || !(overlayOn && recording()) && !checking) return;
            const target = hit && !onScrollbar(hit, pointer.x, pointer.y) ? hit : null;
            if (target && target === hover?.hit) {
              // The page may have changed under a pointer at rest.
              if (now() - hover.at > 1500) scheduleExplain();
              scheduleDraw();
              return;
            }
            // Within the element picked, the pick holds, on the path to where the pointer is now.
            if (hover?.picked && target && containsDeep(hover.picked, target) && !checking) {
              const picked = hover.picked;
              hover.hit = target;
              hover.chain = chainOf(hover);
              if (!hover.chain.includes(picked)) hover.chain = [target, ...hover.chain.filter((node) => node !== target)];
              hover.index = hover.chain.indexOf(picked);
              return;
            }
            hover = target && target !== document.body && target !== document.documentElement && !target.matches('iframe, frame')
              ? hoverOver(target) : null;
            clearTimeout(restTimer);
            if (view) view.label.classList.remove('rest', 'picked');
            if (hover) view?.label.classList.add('stale');
            else labelled = false;
            scheduleDraw();
            if (hover) scheduleExplain();
          }
          addEventListener('pointerout', (event) => safe(() => {
            // Off the page, or into a frame, which records nothing.
            if (event.isTrusted && !event.relatedTarget) { hover = null; labelled = false; hideOverlay(); }
          }), { capture: true, passive: true });
          addEventListener('scroll', scheduleDraw, { capture: true, passive: true });
          addEventListener('resize', scheduleDraw, { passive: true });
          addEventListener('keydown', (event) => safe(() => {
            if (!event.isTrusted || event.ctrlKey || event.metaKey || event.shiftKey) return;
            if (event.key === 'F9' && !event.altKey && !event.repeat) {
              overlayOn = !overlayOn;
              hover = null;
              labelled = false;
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
            const control = closestUp(hit, INTERACTIVE);
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
            // Each cell's text as the flow's filter matches it, its elements' texts run together as they are; what changes
            // from load to load or with an edit (a count, a price, a date) left out at first.
            const cells = [...row.querySelectorAll(':scope > td, :scope > th, :scope > [role=cell], :scope > [role=gridcell]')]
              .map((cell) => clean(cell.textContent ?? '')).filter((text) => text && text.length <= 80);
            return cells.length ? { row, cells: [...new Set(cells)].map((text) => ({ text, on: !VOLATILE.test(text) })) } : null;
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
              showPill(state === 'paused' ? 'warn' : state === 'trying' ? 'try' : 'warn', state === 'paused' ? 'Recording paused' : state === 'trying' ? 'Trying' : 'Not now',
                state === 'paused' ? 'resume first, then check' : state === 'trying' ? 'record again first, then check' : 'the recording is busy',
                state === 'paused' ? 'F8' : state === 'trying' ? 'F7' : '', true);
              return;
            }
            flushTyping();
            pending = null;
            checking = true;
            hover = null;
            hideOverlay();
            // In its place at the top, the pill of the mode the recording was in.
            const pill = showPill('ok', 'Check', 'click what the flow should check · leave', 'Esc');
            const url = pill.insertBefore(document.createElement('button'), pill.lastChild);
            url.className = 'url';
            url.type = 'button';
            url.textContent = 'URL is…';
            url.addEventListener('click', () => safe(() => openChooser(null)));
            pill.classList.add('interactive');
            control({ type: 'checking', on: true });
          }

          function stopChecking() {
            if (!checking) return;
            closeChooser();
            checking = false;
            hover = null;
            hideOverlay();
            if (view) view.pill.className = 'pill';
            showState();
            control({ type: 'checking', on: false });
          }

          // The chooser, beside the element, or under the pill for the address.
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
            showPill('ok', 'Check added', what, '', true);
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
            // Ctrl+K opens the panel's commands, as in the panel, not Chrome's search; never in a field the person types in.
            if ((event.ctrlKey || event.metaKey) && !event.altKey && !event.shiftKey && event.code === 'KeyK' && !isTextual(activeElement() ?? document.body)) {
              handled();
              if (!event.repeat) control({ type: 'palette' });
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
                values: labels, opener: openerOf(element) });
            }
          }), true);

          // ----------------------------------------------------------------------------------------------- effects
          // What the page shows changes a moment after an action: a dialog opens or closes, an alert appears, a busy
          // indicator (a progress bar, as MUI's CircularProgress and LinearProgress are, or aria-busy) comes and goes.
          // Each menu that opened as the pointer rested, with what the pointer was on then: what the flow hovers first.
          const hoverMenus = new WeakMap();
          let hovered = null;
          addEventListener('pointermove', (event) => { if (event.isTrusted) hovered = deepTarget(event); }, { capture: true, passive: true });
          const BUSY = '[role=progressbar], [aria-busy=true]';
          const busyKind = (element) => element.matches('[role=progressbar]') ? 'progressbar' : 'ariaBusy';
          const busySince = new WeakMap();
          let shown = { dialogs: new Map(), alerts: new Map(), menus: new Set(), busy: new Set() };
          // What a message says, without the words of the buttons it holds, as MUI's snackbar's UNDO.
          const messageOf = (alert) => [...alert.querySelectorAll('button, [role=button]')]
            .reduce((text, button) => text.replace(button.innerText || button.textContent || '\u0000', ' '), alert.innerText || alert.textContent || '');
          function snapshot() {
            const dialogs = new Map();
            for (const dialog of openDialogs()) dialogs.set(dialog, null);
            const alerts = new Map();
            for (const alert of document.querySelectorAll('[role=alert]'))
              if (visible(alert)) alerts.set(alert, normalize(messageOf(alert)));
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
              if (!shown.menus.has(menu) && now() - lastPointer > 1000)
                hoverMenus.set(menu, hovered?.isConnected && !containsDeep(menu, hovered) ? describe(retarget(hovered)).target : null);
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
            showPill('info', 'Elements frozen', 'pick them in the panel', '', true);
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
          // The panel's pointer on an element boxes it here, as the overlay's box does, named as the tree names it, and
          // scrolls it into view; so do a step or a locator the panel points at, every element it finds; the Elements
          // view's search boxes each match, the current one stronger. Drawn in the overlay's closed shadow root.
          const MARKS_CSS = `
            .hit { position: fixed; top: 0; left: 0; display: none; box-sizing: border-box; pointer-events: none;
              border: 1.5px solid rgba(217, 119, 6, .9); border-radius: 2px; background: rgba(245, 158, 11, .14); }
            .hit.current { border-width: 2px; border-color: #d97706; background: rgba(245, 158, 11, .26); }`;
          let marked = { hover: null, matches: [], current: null, shown: [], tag: '' };
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
            marks = { root, hits: [], extras: [], box: part('mark'), tip: part('tip'), tipFor: null };
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
            return { left, top, width: rect.width, height: rect.height, right: left + rect.width, bottom: top + rect.height };
          }
          const place = (element, box) => Object.assign(element.style, { display: 'block', transform: `translate(${box.left}px, ${box.top}px)`,
            width: `${box.width}px`, height: `${box.height}px` });
          // The tip over a marked element, as the label over a hovered one: what it is, its tag, id or class, its size.
          function nameTip(tip, element, box, tag, more) {
            tip.replaceChildren();
            const head = tip.appendChild(document.createElement('div'));
            head.className = 'head';
            head.dataset.tag = tag;
            head.appendChild(document.createElement('div')).className = 'what';
            head.firstChild.textContent = whatOf(element);
            const loc = tip.appendChild(document.createElement('div'));
            loc.className = 'loc plain';
            const code = loc.appendChild(document.createElement('div'));
            code.className = 'locator';
            code.appendChild(document.createElement('b')).textContent = element.tagName.toLowerCase();
            const first = typeof element.className === 'string' ? element.className.trim().split(/\s+/)[0] : '';
            if (element.id) code.appendChild(document.createElement('i')).textContent = `#${element.id}`;
            else if (first) code.append(`.${first}`);
            const size = loc.appendChild(document.createElement('div'));
            size.className = 'count';
            size.textContent = `${Math.round(box.width)} × ${Math.round(box.height)}${more ? `  ·  ${more}` : ''}`;
          }
          function drawMarks() {
            markFrame = 0;
            const { hover, matches: hits, current, shown, tag } = marked;
            const target = hover?.isConnected ? hover : shown.find((element) => element.isConnected) ?? null;
            if (!target && !hits.length && !shown.length) {
              if (marks) for (const element of [marks.box, marks.tip, ...marks.hits, ...marks.extras]) element.style.display = 'none';
              return;
            }
            const view = marksView();
            hits.forEach((element, index) => {
              const hit = view.hits[index] ?? (view.hits[index] = view.root.insertBefore(document.createElement('div'), view.box));
              hit.className = `hit${element === current ? ' current' : ''}`;
              if (element.isConnected) place(hit, boxOf(element)); else hit.style.display = 'none';
            });
            for (const hit of view.hits.slice(hits.length)) hit.style.display = 'none';
            // The other elements a locator the panel points at finds.
            const others = hover?.isConnected ? [] : shown.filter((element) => element !== target && element.isConnected);
            others.forEach((element, index) => {
              const extra = view.extras[index] ?? (view.extras[index] = view.root.insertBefore(document.createElement('div'), view.box));
              extra.className = 'extra';
              extra.dataset.mode = 'record';
              place(extra, boxOf(element));
            });
            for (const extra of view.extras.slice(others.length)) extra.style.display = 'none';
            if (target) {
              const box = boxOf(target);
              const mode = hover?.isConnected ? (inspecting ? 'inspect' : 'record') : 'record';
              view.box.dataset.mode = view.tip.dataset.mode = mode;
              place(view.box, box);
              const name = hover?.isConnected ? (inspecting ? 'Inspect' : 'Element') : tag || 'Step';
              const more = shown.length > 1 && !hover?.isConnected ? `found ${shown.length}` : '';
              const key = `${name}|${more}|${Math.round(box.width)}×${Math.round(box.height)}`;
              // Filled again only when it shows another element, or its size changed.
              if (view.tipFor !== target || view.tipKey !== key) {
                nameTip(view.tip, target, box, name, more);
                view.tipFor = target;
                view.tipKey = key;
              }
              view.tip.style.display = 'block';
              const at = placeLabel(box, view.tip.getBoundingClientRect());
              view.tip.style.transform = `translate(${at.left}px, ${at.top}px)`;
            } else {
              view.box.style.display = 'none';
              view.tip.style.display = 'none';
              view.tipFor = null;
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
            if ('elements' in command || command.op === 'show') {
              marked.shown = (command.elements ?? []).filter((element) => element?.nodeType === 1).slice(0, 50);
              marked.tag = command.tag ?? '';
              if (marked.shown[0] && command.scroll) reveal(marked.shown[0]);
            }
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
            if (state === 'login' || state === 'testing') { showPill('warn', 'Not now', 'the recording is busy', '', true); return; }
            stopChecking();
            flushTyping();
            inspecting = true;
            hover = null;
            hideOverlay();
            observe();
            sent = new WeakMap();
            showPill('inspect', 'Inspect', 'point at an element, a click picks it in the panel · leave', 'Esc');
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
            if (!checking) {
              if (view) view.pill.className = 'pill';
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
              // What a step or a locator the panel points at finds, every element of it; none hides them.
              case 'show': setMarks({ op: 'show', elements: command.elements, tag: command.tag }); return null;
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
