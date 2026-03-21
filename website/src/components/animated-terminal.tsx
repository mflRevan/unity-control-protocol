import { useState, useEffect, useRef } from 'react';
import { cn } from '@/lib/utils';

interface TerminalLine {
  text: string;
  type: 'command' | 'output' | 'success' | 'info' | 'dim';
  delay: number;
}

const lines: TerminalLine[] = [
  { text: '$ npm install -g @mflrevan/ucp', type: 'command', delay: 0 },
  { text: 'added 1 package in 3.2s', type: 'dim', delay: 800 },
  { text: '', type: 'output', delay: 200 },
  { text: '$ cd MyUnityProject', type: 'command', delay: 400 },
  { text: '$ ucp install', type: 'command', delay: 600 },
  { text: '✓ Bridge package installed', type: 'success', delay: 800 },
  { text: '', type: 'output', delay: 100 },
  { text: '$ ucp connect', type: 'command', delay: 500 },
  { text: '✓ Connected to Unity 6000.3.1f1', type: 'success', delay: 700 },
  { text: '  Project: "MyGame"', type: 'info', delay: 300 },
  { text: '  Protocol: v0.4.2', type: 'info', delay: 200 },
  { text: '', type: 'output', delay: 100 },
  { text: '$ ucp scene snapshot', type: 'command', delay: 600 },
  { text: 'Scene: SampleScene (1 root)', type: 'output', delay: 500 },
  { text: '  └─ Main Camera [children=0] [Transform, Camera, AudioListener]', type: 'info', delay: 200 },
  { text: '  └─ Directional Light [children=0] [Transform, Light]', type: 'info', delay: 200 },
  { text: '  └─ Player [children=4] [Transform, Rigidbody, PlayerController]', type: 'info', delay: 200 },
];

export function AnimatedTerminal() {
  const [visibleLines, setVisibleLines] = useState<number>(0);
  const [isTyping, setIsTyping] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    let totalDelay = 500;
    const timeouts: ReturnType<typeof setTimeout>[] = [];

    for (let i = 0; i < lines.length; i++) {
      totalDelay += lines[i].delay;
      const timeout = setTimeout(() => {
        if (lines[i].type === 'command') setIsTyping(true);
        setTimeout(
          () => {
            setVisibleLines(i + 1);
            setIsTyping(false);
            if (containerRef.current) {
              containerRef.current.scrollTop = containerRef.current.scrollHeight;
            }
          },
          lines[i].type === 'command' ? 400 : 0,
        );
      }, totalDelay);
      timeouts.push(timeout);
    }

    return () => timeouts.forEach(clearTimeout);
  }, []);

  return (
    <div className="relative group">
      {/* Glow effect */}
      <div className="absolute -inset-2 bg-linear-to-r from-primary/20 via-purple-400/10 to-primary/20 rounded-2xl blur-xl opacity-50 group-hover:opacity-80 transition-opacity duration-700" />

      <div className="relative rounded-xl overflow-hidden border border-white/8 bg-[#0a0a0c] shadow-2xl shadow-primary/5">
        {/* Title bar */}
        <div className="flex items-center gap-2 px-4 py-3 bg-[#111114] border-b border-white/5">
          <div className="flex gap-1.5">
            <div className="w-3 h-3 rounded-full bg-red-500/70 hover:bg-red-500 transition-colors" />
            <div className="w-3 h-3 rounded-full bg-yellow-500/70 hover:bg-yellow-500 transition-colors" />
            <div className="w-3 h-3 rounded-full bg-green-500/70 hover:bg-green-500 transition-colors" />
          </div>
          <span className="text-xs text-white/30 ml-2 font-mono">terminal</span>
        </div>

        {/* Terminal content */}
        <div
          ref={containerRef}
          className="p-4 font-mono text-sm leading-relaxed h-80 overflow-y-auto hide-scrollbar"
          style={{ scrollbarWidth: 'none' }}
        >
          {lines.slice(0, visibleLines).map((line, i) => (
            <div
              key={i}
              className={cn(
                'animate-in fade-in slide-in-from-bottom-1 duration-300',
                line.type === 'command' && 'text-white',
                line.type === 'output' && 'text-white/70',
                line.type === 'success' && 'text-emerald-400',
                line.type === 'info' && 'text-blue-300/80',
                line.type === 'dim' && 'text-white/40',
                !line.text && 'h-4',
              )}
            >
              {line.text}
            </div>
          ))}
          {isTyping && <span className="inline-block w-2 h-4 bg-primary animate-pulse" />}
          {visibleLines >= lines.length && (
            <div className="mt-1 flex items-center gap-1">
              <span className="text-white/70">$</span>
              <span className="inline-block w-2 h-4 bg-primary/80 animate-pulse" />
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
