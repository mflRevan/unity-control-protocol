import { useState, type ReactNode } from 'react';
import { Copy, Check } from 'lucide-react';

interface Line {
  text: string;
  type: 'command' | 'output' | 'success' | 'info' | 'dim';
}

const session: Line[] = [
  { text: '$ ucp connect', type: 'command' },
  { text: '✓ Connected to Unity 6000.3.1f1', type: 'success' },
  { text: '  Project: "MyGame"   Protocol: v0.6.1', type: 'info' },
  { text: '', type: 'output' },
  { text: '$ ucp scene snapshot', type: 'command' },
  { text: 'Scene: SampleScene (3 roots)', type: 'output' },
  { text: '  └─ Main Camera        [Transform, Camera, AudioListener]', type: 'info' },
  { text: '  └─ Directional Light  [Transform, Light]', type: 'info' },
  { text: '  └─ Player [children=4] [Transform, Rigidbody, PlayerController]', type: 'info' },
  { text: '', type: 'output' },
  { text: '$ ucp play', type: 'command' },
  { text: '✓ Entered play mode', type: 'success' },
  { text: '$ ucp screenshot -o capture.png', type: 'command' },
  { text: 'Wrote capture.png (1920x1080)', type: 'dim' },
];

const plainText = session.map((l) => l.text).join('\n');

function renderLine(line: Line, i: number): ReactNode {
  const cls =
    line.type === 'command'
      ? 'text-white'
      : line.type === 'success'
        ? 'text-emerald-400'
        : line.type === 'info'
          ? 'text-blue-300/80'
          : line.type === 'dim'
            ? 'text-white/40'
            : 'text-white/70';
  return (
    <div key={i} className={!line.text ? 'h-4' : cls}>
      {line.text}
    </div>
  );
}

export function AnimatedTerminal() {
  const [copied, setCopied] = useState(false);

  const handleCopy = () => {
    navigator.clipboard.writeText(plainText);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  return (
    <div className="relative rounded-xl overflow-hidden border border-border bg-[#0a0a0c]">
      {/* Title bar */}
      <div className="flex items-center justify-between px-4 py-2.5 bg-[#111114] border-b border-white/5">
        <span className="text-xs text-white/40 font-mono">ucp session</span>
        <button
          onClick={handleCopy}
          aria-label="Copy commands"
          className="text-white/30 hover:text-white/70 transition-colors p-1 -m-1 rounded hover:bg-white/5"
        >
          {copied ? <Check className="h-3.5 w-3.5" /> : <Copy className="h-3.5 w-3.5" />}
        </button>
      </div>

      {/* Terminal content */}
      <pre className="p-4 font-mono text-sm leading-relaxed overflow-x-auto whitespace-pre">
        <code>{session.map(renderLine)}</code>
      </pre>
    </div>
  );
}
