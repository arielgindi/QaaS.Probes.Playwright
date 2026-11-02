import { render } from 'preact';
import { App } from './app';
import { initialTheme } from './theme';
import './styles.css';

document.documentElement.dataset.theme = initialTheme();
render(<App />, document.getElementById('app')!);

// `npm run dev` shows a sample recording; the recorder's build never includes it.
if (import.meta.env.DEV && !window.__qaasPanelSend) void import('./sample').then((sample) => sample.start());
