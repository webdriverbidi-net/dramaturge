// <copyright file="code-recorder.js" company="WebDriverBiDi.NET Committers">
// Copyright (c) WebDriverBiDi.NET Committers. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

// Records the user's actions for code generation, and, in the top frame, shows the toolbar that picks locators and
// adds assertions, which can be dragged by its grip and collapsed to a pill. Each message goes through a channel as its
// JSON, then the element it is about and that element's nameable ancestors, or null.
(() => {
  const buttons = [
    ['record', 'Record'],
    ['pick', 'Pick locator'],
    ['assertVisible', 'Assert visible'],
    ['assertText', 'Assert text'],
    ['assertValue', 'Assert value'],
    ['assertSnapshot', 'Assert snapshot'],
  ];
  // The bar's columns are the grip, the six buttons, and the collapse button.
  const style = `
    :host { all: initial; position: fixed; z-index: 2147483647; }
    .bar, form, .pill { gap: 4px; padding: 4px; background: #f8f9fa; border: 1px solid #9aa0a6; border-radius: 6px; font: 12px system-ui, sans-serif; }
    .bar, form { display: grid; }
    .bar { width: 650px; box-sizing: border-box; grid-template-columns: 16px repeat(6, 1fr) 24px; }
    .grip { cursor: grab; display: flex; align-items: center; justify-content: center; color: #5f6368; user-select: none; touch-action: none; }
    form { grid-template-columns: 1fr auto auto; margin-top: 4px; }
    button { font: inherit; height: 24px; padding: 0 4px; border: 1px solid #9aa0a6; border-radius: 4px; background: #fff; cursor: pointer; }
    button[aria-pressed=true] { background: #1a73e8; border-color: #1a73e8; color: #fff; }
    .pill { display: block; height: auto; cursor: grab; touch-action: none; }
    .pill::before { content: '\\25CF'; color: #d93025; margin-right: 4px; }
    [hidden] { display: none; }
    input { font: inherit; }`;

  let session = null;

  const facts = ({ element, ...rest }) => rest;

  // A control's value, or null for an element without one; checkboxes and radio buttons are checked or not.
  const valueOf = (element, checked) => {
    if (element instanceof HTMLInputElement && (element.type === 'checkbox' || element.type === 'radio')) {
      return { kind: 'assertChecked', checked };
    }

    return element instanceof HTMLInputElement || element instanceof HTMLTextAreaElement || element instanceof HTMLSelectElement
      ? { kind: 'assertValue', value: element.value }
      : null;
  };

  // Starts where the layout puts it, or at the top center; a drag or a collapse sends the new layout.
  const createToolbar = (onChoose, onAnswer, layout, onLayout) => {
    const host = document.createElement('dramaturge-toolbar');
    host.setAttribute('aria-hidden', 'true');
    const root = host.attachShadow({ mode: 'closed' });
    root.innerHTML = `<style>${style}</style><div class="bar"><span class="grip" title="Drag to move">\u2807</span></div><button type="button" class="pill" hidden>Recording</button>`;
    const bar = root.querySelector('.bar');
    const pill = root.querySelector('.pill');
    for (const [mode, label] of buttons) {
      const button = document.createElement('button');
      button.type = 'button';
      button.dataset.mode = mode;
      button.textContent = label;
      button.addEventListener('click', () => onChoose(mode));
      bar.appendChild(button);
    }

    const collapse = document.createElement('button');
    collapse.type = 'button';
    collapse.title = 'Collapse';
    collapse.textContent = '\u2013';
    bar.appendChild(collapse);
    document.documentElement.appendChild(host);

    let x = layout?.x ?? null;
    let y = layout?.y ?? 8;
    let collapsed = layout?.collapsed ?? false;
    let form = null;
    // Kept wholly within the viewport, as its size or the window's changes.
    const place = () => {
      const { width, height } = host.getBoundingClientRect();
      x = Math.max(0, Math.min(x ?? (innerWidth - width) / 2, innerWidth - width));
      y = Math.max(0, Math.min(y, innerHeight - height));
      host.style.left = `${x}px`;
      host.style.top = `${y}px`;
    };
    const show = (isCollapsed) => {
      collapsed = isCollapsed;
      bar.hidden = collapsed;
      pill.hidden = !collapsed;
      if (form) {
        form.hidden = collapsed;
      }

      place();
    };
    const sendLayout = () => onLayout({ x, y, collapsed });
    // A drag moves the toolbar by the pointer's movement; one that does not move is a click.
    const dragFrom = (handle, onClick) => {
      handle.addEventListener('pointerdown', (event) => {
        if (event.button !== 0) {
          return;
        }

        event.preventDefault();
        handle.setPointerCapture(event.pointerId);
        const start = { pointerX: event.clientX, pointerY: event.clientY, x, y };
        let moved = false;
        const onMove = (move) => {
          moved ||= Math.abs(move.clientX - start.pointerX) + Math.abs(move.clientY - start.pointerY) > 3;
          if (moved) {
            x = start.x + move.clientX - start.pointerX;
            y = start.y + move.clientY - start.pointerY;
            place();
          }
        };
        const onUp = () => {
          handle.removeEventListener('pointermove', onMove);
          handle.removeEventListener('pointerup', onUp);
          if (moved) {
            sendLayout();
          } else {
            onClick?.();
          }
        };
        handle.addEventListener('pointermove', onMove);
        handle.addEventListener('pointerup', onUp);
      });
    };
    dragFrom(bar.querySelector('.grip'));
    dragFrom(pill, () => {
      show(false);
      sendLayout();
    });
    // The pill's own click event would follow its pointer events; the drag handles them.
    pill.addEventListener('click', (event) => event.preventDefault());
    collapse.addEventListener('click', () => {
      show(true);
      sendLayout();
    });
    addEventListener('resize', place);
    show(collapsed);
    return {
      host,
      show(mode) {
        for (const button of bar.querySelectorAll('[data-mode]')) {
          button.setAttribute('aria-pressed', String(button.dataset.mode === mode));
        }
      },
      // Asks for the text to assert, starting from the element's.
      ask(text) {
        form?.remove();
        form = document.createElement('form');
        form.innerHTML = '<input aria-label="Text"><button type="submit">Assert</button><button type="button">Cancel</button>';
        const input = form.querySelector('input');
        input.value = text;
        const close = (answer) => {
          form.remove();
          form = null;
          onAnswer(answer);
        };
        form.addEventListener('submit', (event) => {
          event.preventDefault();
          close(input.value);
        });
        form.querySelector('button[type=button]').addEventListener('click', () => close(null));
        input.addEventListener('keydown', (event) => event.key === 'Escape' && close(null));
        root.appendChild(form);
        place();
        input.focus();
        input.select();
      },
      remove() {
        removeEventListener('resize', place);
        host.remove();
      },
    };
  };

  const start = (send, testIdAttribute, mode, showToolbar, layout) => {
    const describer = new Acquiescence.ElementDescriber();
    const describe = (element, details) => {
      const { target, ancestors } = describer.describe(element, { testIdAttribute });
      send([JSON.stringify({ ...details, target: facts(target), ancestors: ancestors.map(facts) }), target.element, ...ancestors.map((ancestor) => ancestor.element)]);
    };
    let toolbar = null;
    let current = null;
    let asked = null;
    const ignore = (element) => toolbar !== null && element === toolbar.host;

    const recorder = new Acquiescence.ActionRecorder(({ element, ...action }) => describe(element, action), { ignore });
    // A clicked checkbox or radio button is toggled until its cancelled click ends, so its state is read as the mouse
    // button goes down, by a listener that runs before the picker's, which cancels the mouse events.
    let pressed = null;
    const onPointerDown = (event) => {
      const element = event.composedPath()[0];
      pressed = element instanceof HTMLInputElement ? { element, checked: element.checked } : null;
    };
    document.addEventListener('pointerdown', onPointerDown, true);
    // A navigation the document starts is reported, so that one without a report is known to be the browser's own; a
    // traversal of the history is reported with its steps, negative for back.
    const onNavigate = (event) => {
      if (!event.destination.sameDocument) {
        const steps = event.navigationType === 'traverse' && navigation.currentEntry ? event.destination.index - navigation.currentEntry.index : null;
        send([JSON.stringify({ kind: 'navigate', url: event.destination.url, steps }), null]);
      }
    };
    navigation.addEventListener('navigate', onNavigate);
    const picker = new Acquiescence.ElementPicker((element) => onPick(element), { resolve: (element) => describer.getActionTarget(element), ignore });
    const onPick = (element) => {
      if (current === 'assertText') {
        asked = element;
        picker.stop();
        toolbar.ask(describer.describe(element, { testIdAttribute }).target.text);
        return;
      }

      const details = current === 'pick' ? { kind: 'pick' }
        : current === 'assertVisible' ? { kind: 'assertVisible' }
        : current === 'assertSnapshot' ? { kind: 'assertSnapshot', snapshot: new Acquiescence.AriaSnapshotGenerator().generate(element, { refs: false }).text }
        : valueOf(element, pressed?.element === element ? pressed.checked : element.checked);
      if (details) {
        describe(element, details);
        choose('record');
      }
    };

    // Applies a mode in this document; a mode the user chooses is also sent, so the recording applies it in the others.
    const apply = (mode) => {
      current = mode;
      recorder.stop();
      picker.stop();
      if (mode === 'record') {
        recorder.start(document);
      } else if (mode !== 'none') {
        picker.start(document);
      }

      toolbar?.show(mode);
    };
    const choose = (mode) => {
      const next = mode === current ? (mode === 'record' ? 'none' : 'record') : mode;
      apply(next);
      send([JSON.stringify({ kind: 'mode', mode: next }), null]);
    };

    if (window === window.top && showToolbar) {
      toolbar = createToolbar(
        choose,
        (text) => {
          if (text !== null) {
            describe(asked, { kind: 'assertText', text });
          }

          choose('record');
        },
        layout === null ? null : JSON.parse(layout),
        (changed) => send([JSON.stringify({ kind: 'toolbar', ...changed }), null]));
    }

    apply(mode);
    return {
      apply,
      // An action the library took, whose events are not the user's, is recorded as one of the user's would be.
      report: (element, details) => {
        if (current === 'record') {
          describe(element, details);
        }
      },
      stop: () => {
        recorder.stop();
        picker.stop();
        toolbar?.remove();
        document.removeEventListener('pointerdown', onPointerDown, true);
        navigation.removeEventListener('navigate', onNavigate);
      },
    };
  };

  return {
    start(send, testIdAttribute, mode, showToolbar, layout) {
      session?.stop();
      session = start(send, testIdAttribute, mode, showToolbar, layout);
    },

    setMode(mode) {
      session?.apply(mode);
    },

    // Options the library selected, by the values now selected.
    reportSelect(element) {
      session?.report(element, { kind: 'select', values: Array.from(element.selectedOptions, (option) => option.value) });
    },

    // Files the library set, by their paths, where the page knows only their names.
    reportFiles(element, files) {
      session?.report(element, { kind: 'setInputFiles', files, paths: true });
    },

    // An assertion the library met, of a kind the toolbar adds, with its value: the text, the value, whether the
    // element is checked, or the snapshot template.
    reportAssertion(element, kind, value) {
      const details = { assertText: { text: value }, assertValue: { value }, assertChecked: { checked: value }, assertSnapshot: { snapshot: value } }[kind];
      session?.report(element, { kind, ...details });
    },

    // Each frame element's window, then its description as a message has it.
    describeFrames(testIdAttribute) {
      const describer = new Acquiescence.ElementDescriber();
      return Array.from(document.querySelectorAll('iframe, frame')).filter((element) => element.contentWindow).map((element) => {
        const { target, ancestors } = describer.describe(element, { testIdAttribute });
        return [element.contentWindow, JSON.stringify({ target: facts(target), ancestors: ancestors.map(facts) }), target.element, ...ancestors.map((ancestor) => ancestor.element)];
      });
    },

    stop() {
      session?.stop();
      session = null;
    },
  };
})()
