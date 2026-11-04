import { render } from 'preact';
import { useEffect, useState } from 'preact/hooks';
import { App } from './app';
import { type Screen, subscribeScreen } from './bridge';
import { SetupScreen } from './setup/SetupScreen';
import { initialTheme } from './theme';
import './styles.css';

document.documentElement.dataset.theme = initialTheme();

// The window shows the start screen first, then each recording, then what was saved, as the recorder says; a recording
// is shown afresh each time.
function Root() {
  const [screen, setScreen] = useState<Screen | null>(null);
  const [recording, setRecording] = useState(0);
  useEffect(() => subscribeScreen((next) => {
    setScreen((was) => {
      // A recording after the start screen starts afresh; the first one of a window opened for it alone goes on as it is.
      if (next === 'recording' && (was === 'setup' || was === 'done')) setRecording((count) => count + 1);
      return next;
    });
  }), []);
  if (screen === 'setup' || screen === 'done') return <SetupScreen />;
  return <App key={recording} />;
}

render(<Root />, document.getElementById('app')!);

// `npm run dev` shows a sample recording; the recorder's build never includes it.
if (import.meta.env.DEV && !window.__qaasPanelSend) void import('./sample').then((sample) => sample.start());
