import type { JSX } from 'preact';

// Line icons on a 24-unit grid, drawn with the text's colour.

const paths = {
  click: 'M6 3.5l12.5 6.2-5.4 1.9-2 5.6z M13.2 11.7l5 5',
  dblclick: 'M6 3.5l12.5 6.2-5.4 1.9-2 5.6z M13.2 11.7l5 5 M20 4.5l1.5-1.5 M17 3V1.5',
  fill: 'M5 7V5h14v2 M12 5v14 M9 19h6',
  check: 'M5 4h14a1 1 0 0 1 1 1v14a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1z M8.5 12.2l2.4 2.4 4.8-5.2',
  uncheck: 'M5 4h14a1 1 0 0 1 1 1v14a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1z',
  choose: 'M4 6h11 M4 12h11 M4 18h7 M18 15l2.5 3 2.5-3 M18 9l2.5-3 2.5 3',
  press: 'M19 5v7a3 3 0 0 1-3 3H6 M9.5 11L6 15l3.5 4',
  upload: 'M12 15V4 M7.5 8.5L12 4l4.5 4.5 M5 15v3a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2v-3',
  drag: 'M12 3v18 M3 12h18 M9.5 5.5L12 3l2.5 2.5 M9.5 18.5L12 21l2.5-2.5 M5.5 9.5L3 12l2.5 2.5 M18.5 9.5L21 12l-2.5 2.5',
  navigate: 'M12 3a9 9 0 1 0 0 18a9 9 0 1 0 0-18z M3.5 12h17 M12 3c2.5 2.6 3.6 5.6 3.6 9s-1.1 6.4-3.6 9c-2.5-2.6-3.6-5.6-3.6-9s1.1-6.4 3.6-9z',
  newTab: 'M5 5h14a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2z M12 9v6 M9 12h6',
  undo: 'M9 14L4 9l5-5 M4 9h10.5a5.5 5.5 0 0 1 0 11H11',
  sun: 'M12 8a4 4 0 1 0 0 8a4 4 0 1 0 0-8z M12 2.5v2 M12 19.5v2 M4.6 4.6l1.4 1.4 M18 18l1.4 1.4 M2.5 12h2 M19.5 12h2 M4.6 19.4L6 18 M18 6l1.4-1.4',
  moon: 'M20.5 14.2A8.5 8.5 0 1 1 9.8 3.5a6.8 6.8 0 0 0 10.7 10.7z',
  warn: 'M12 4l9 15.5H3z M12 10v4.2 M12 17.2v.1',
  dialog: 'M4 4.5h16a1 1 0 0 1 1 1v13a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1v-13a1 1 0 0 1 1-1z M3 9h18',
  closed: 'M4 4.5h16a1 1 0 0 1 1 1v13a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1v-13a1 1 0 0 1 1-1z M9.5 11.5l5 5 M14.5 11.5l-5 5',
  url: 'M10 14a4 4 0 0 0 5.7 0l3-3A4 4 0 0 0 13 5.3l-1 1 M14 10a4 4 0 0 0-5.7 0l-3 3a4 4 0 0 0 5.7 5.7l1-1',
  alert: 'M4 5h16v11H9l-5 4z M12 8.5v3 M12 13.5v.1',
  tab: 'M3 19V9h5V5h8v4h5v10z',
  popup: 'M14 4h6v6 M20 4l-8 8 M18 14v5a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V7a1 1 0 0 1 1-1h5',
  wrap: 'M4 6h16 M4 12h12.5a3.5 3.5 0 0 1 0 7H13 M15 17l-2 2 2 2 M4 18h5',
  file: 'M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8z M14 3v5h5',
  pointer: 'M6 3.5l12.5 6.2-5.4 1.9-2 5.6z',
  tick: 'M5 12.5l4.5 4.5L19 7.5',
  pencil: 'M14.5 5.5l4 4 M4 20l1-5L15.5 4.5a1.4 1.4 0 0 1 2 0l2 2a1.4 1.4 0 0 1 0 2L9 19z',
  cross: 'M6.5 6.5l11 11 M17.5 6.5l-11 11',
  pause: 'M9 5.5v13 M15 5.5v13',
  list: 'M9 6.5h11 M9 12h11 M9 17.5h11 M4.5 6.5h.01 M4.5 12h.01 M4.5 17.5h.01',
  play: 'M7.5 5.2v13.6a.6.6 0 0 0 .9.5l10.6-6.8a.6.6 0 0 0 0-1L8.4 4.7a.6.6 0 0 0-.9.5z',
  elements: 'M4 5h6 M7 5v14 M7 12h5 M7 19h5 M14 9.5h6v5h-6z M14 16.5h6v5h-6z',
  inspect: 'M4 9.5V5a1 1 0 0 1 1-1h14a1 1 0 0 1 1 1v4.5 M9.5 20H5a1 1 0 0 1-1-1v-4.5 M12 12l8.5 3.1-3.6 1.4-1.4 3.6z',
  filter: 'M4 5h16l-6.2 7.4v5.8l-3.6 1.8v-7.6z',
  search: 'M10.5 4a6.5 6.5 0 1 0 0 13a6.5 6.5 0 1 0 0-13z M15.3 15.3L20 20',
  refresh: 'M19.5 12a7.5 7.5 0 1 1-2.2-5.3 M19.5 4.5v4.2h-4.2',
  freeze: 'M12 3v18 M4.2 7.5l15.6 9 M4.2 16.5l15.6-9 M9.5 4.5L12 7l2.5-2.5 M9.5 19.5L12 17l2.5 2.5',
  copy: 'M9 9h10a1 1 0 0 1 1 1v10a1 1 0 0 1-1 1H9a1 1 0 0 1-1-1V10a1 1 0 0 1 1-1z M5 15H4.5A1.5 1.5 0 0 1 3 13.5v-9A1.5 1.5 0 0 1 4.5 3h9A1.5 1.5 0 0 1 15 4.5V5',
  plus: 'M12 5v14 M5 12h14',
  chevron: 'M9.5 6.5l5.5 5.5-5.5 5.5',
  up: 'M6.5 14.5l5.5-5.5 5.5 5.5',
  down: 'M6.5 9.5l5.5 5.5 5.5-5.5',
  key: 'M8 10.5a4.5 4.5 0 1 0 0 .01z M12.5 10.5H21 M18 10.5v3.5 M15.5 10.5v2.5',
  target: 'M12 4.5a7.5 7.5 0 1 0 0 15a7.5 7.5 0 1 0 0-15z M12 2v4 M12 18v4 M2 12h4 M18 12h4',
  stop: 'M7.5 6h9A1.5 1.5 0 0 1 18 7.5v9a1.5 1.5 0 0 1-1.5 1.5h-9A1.5 1.5 0 0 1 6 16.5v-9A1.5 1.5 0 0 1 7.5 6z',
  gauge: 'M3 16.5a9 9 0 0 1 18 0 M12 16.5L7.6 12.1 M12 16.5h.01 M3 19.5h18',
  runTo: 'M5.5 6.3v11.4a.6.6 0 0 0 .92.5l8.6-5.7a.6.6 0 0 0 0-1L6.42 5.8a.6.6 0 0 0-.92.5z M19 5.5v13',
  bulb: 'M9.5 18h5 M10.5 21h3 M12 3a6 6 0 0 0-3.6 10.8c.7.6 1.1 1.3 1.1 2.1V16h5v-.1c0-.8.4-1.5 1.1-2.1A6 6 0 0 0 12 3z',
  expand: 'M4 9V4h5 M20 9V4h-5 M4 15v5h5 M20 15v5h-5',
  minus: 'M7 12h10',
  checkCircle: 'M12 3.5a8.5 8.5 0 1 0 0 17a8.5 8.5 0 1 0 0-17z M8.3 12.3l2.6 2.6 4.9-5.3',
  trash: 'M4.5 7h15 M10 4h4 M6.5 7l.8 12a1.5 1.5 0 0 0 1.5 1.4h6.4a1.5 1.5 0 0 0 1.5-1.4l.8-12 M10 11v5.5 M14 11v5.5',
  calendar: 'M5 5.5h14a1 1 0 0 1 1 1V19a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V6.5a1 1 0 0 1 1-1z M4 10h16 M8.5 3.5v4 M15.5 3.5v4',
  // A wand: what the recorder suggests.
  wand: 'M4 20 15 9 M13.5 6.5l4 4 M17 3v3 M15.5 4.5h3 M20 7v2 M19 8h2 M10 3v2 M9 4h2',
  hash: 'M9.5 4l-2 16 M16.5 4l-2 16 M4.5 9h15 M4 15h15',
  restore: 'M4.5 12a7.5 7.5 0 1 0 2.2-5.3 M4.5 4.5v4.2h4.2',
  rerecord: 'M4.5 12a7.5 7.5 0 1 0 2.2-5.3 M4.5 4.5v4.2h4.2 M12 10a2 2 0 1 0 0 4a2 2 0 1 0 0-4z',
  recordAfter: 'M12 3.5v9 M8.5 9l3.5 3.5 3.5-3.5 M5 16.5h14 M5 20h9',
  command: 'M9 6.5a2.5 2.5 0 1 0-2.5 2.5H9z M15 6.5A2.5 2.5 0 1 1 17.5 9H15z M9 17.5A2.5 2.5 0 1 1 6.5 15H9z M15 17.5a2.5 2.5 0 1 0 2.5-2.5H15z M9 9h6v6H9z',
  keyboard: 'M4 6.5h16a1 1 0 0 1 1 1v9a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1v-9a1 1 0 0 1 1-1z M7 10h.01 M10.5 10h.01 M14 10h.01 M17 10h.01 M7 13.5h.01 M17 13.5h.01 M10 14h4',
  grip: 'M9.5 6h.01 M14.5 6h.01 M9.5 12h.01 M14.5 12h.01 M9.5 18h.01 M14.5 18h.01',
  flask: 'M9 3.5h6 M10.3 3.5v5.4L5.2 18a1.6 1.6 0 0 0 1.4 2.5h10.8a1.6 1.6 0 0 0 1.4-2.5l-5.1-9.1V3.5 M7.4 14.5h9.2',
} as const;

export type IconName = keyof typeof paths;

export function Icon({ name, size = 16, class: className, ...rest }: { name: IconName; size?: number } & JSX.SVGAttributes<SVGSVGElement>) {
  return (
    <svg
      viewBox="0 0 24 24"
      width={size}
      height={size}
      fill="none"
      stroke="currentColor"
      stroke-width={1.8}
      stroke-linecap="round"
      stroke-linejoin="round"
      aria-hidden="true"
      class={className}
      {...rest}
    >
      <path d={paths[name]} />
    </svg>
  );
}

/** The icon of an action's kind. */
export function kindIcon(kind: string): IconName {
  return kind in paths ? (kind as IconName) : 'click';
}
