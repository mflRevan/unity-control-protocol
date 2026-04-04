import { useState, type ReactNode } from 'react';
import { Copy, Check } from 'lucide-react';
import { cn } from '@/lib/utils';

function colorizeShell(code: string): ReactNode[] {
  return code.split('\n').flatMap((line, i, arr) => {
    const nodes: ReactNode[] = [];

    if (/^\s*#/.test(line)) {
      nodes.push(
        <span key={i} className="text-white/30 italic">
          {line}
        </span>,
      );
    } else {
      const tokens = line.match(/(?:"[^"]*"|'[^']*'|--?\w[\w-]*|[^\s"']+|\s+)/g) ?? [line];
      let isFirst = true;
      for (let t = 0; t < tokens.length; t++) {
        const tok = tokens[t];
        const k = `${i}-${t}`;

        if (/^\s+$/.test(tok)) {
          nodes.push(tok);
          continue;
        }

        if (isFirst && tok === '$') {
          nodes.push(
            <span key={k} className="text-white/40">
              {tok}
            </span>,
          );
          continue;
        }

        if (isFirst && tok === 'ucp') {
          nodes.push(
            <span key={k} className="text-purple-400 font-semibold">
              {tok}
            </span>,
          );
          isFirst = false;
        } else if (isFirst && /^\w/.test(tok)) {
          nodes.push(
            <span key={k} className="text-cyan-400">
              {tok}
            </span>,
          );
          isFirst = false;
        } else if (/^--?\w/.test(tok)) {
          nodes.push(
            <span key={k} className="text-amber-400">
              {tok}
            </span>,
          );
        } else if (/^["']/.test(tok)) {
          nodes.push(
            <span key={k} className="text-green-400">
              {tok}
            </span>,
          );
        } else if (/^(Assets\/|Builds\/|Packages\/|src\/|\.\/)/.test(tok)) {
          nodes.push(
            <span key={k} className="text-orange-300">
              {tok}
            </span>,
          );
        } else {
          nodes.push(
            <span key={k} className="text-white/80">
              {tok}
            </span>,
          );
          isFirst = false;
        }
      }
    }

    if (i < arr.length - 1) nodes.push('\n');
    return nodes;
  });
}

interface CodeBlockProps {
  code: string;
  language?: string;
  title?: string;
  className?: string;
}

export function CodeBlock({ code, title, className }: CodeBlockProps) {
  const [copied, setCopied] = useState(false);

  const handleCopy = () => {
    navigator.clipboard.writeText(code);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  const highlighted = colorizeShell(code);

  return (
    <div
      className={cn(
        'rounded-lg overflow-hidden border border-border bg-[#0d0d0f] group/code transition-colors duration-200 hover:border-border/80',
        className,
      )}
    >
      {title && (
        <div className="flex items-center justify-between px-4 py-2.5 bg-[#1a1a1e] border-b border-white/5">
          <span className="text-xs text-white/40 font-mono">{title}</span>
          <button
            onClick={handleCopy}
            className="text-white/30 hover:text-white/70 transition-all duration-200 p-1 -m-1 rounded hover:bg-white/5"
          >
            {copied ? <Check className="h-3.5 w-3.5" /> : <Copy className="h-3.5 w-3.5" />}
          </button>
        </div>
      )}
      {!title && (
        <button
          onClick={handleCopy}
          className="absolute top-3 right-3 text-white/30 hover:text-white/70 transition-colors z-10"
        >
          {copied ? <Check className="h-3.5 w-3.5" /> : <Copy className="h-3.5 w-3.5" />}
        </button>
      )}
      <pre className="p-4 overflow-x-auto">
        <code className="text-sm font-mono leading-relaxed whitespace-pre">{highlighted}</code>
      </pre>
    </div>
  );
}
