import { useState } from 'react';
import { Copy, Check } from 'lucide-react';
import { cn } from '@/lib/utils';

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

  return (
    <div className={cn('rounded-lg overflow-hidden border border-border bg-[#0d0d0f]', className)}>
      {title && (
        <div className="flex items-center justify-between px-4 py-2.5 bg-[#1a1a1e] border-b border-white/5">
          <span className="text-xs text-white/40 font-mono">{title}</span>
          <button onClick={handleCopy} className="text-white/30 hover:text-white/70 transition-colors">
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
      <pre className="p-4 overflow-x-auto text-white/88">
        <code className="text-sm font-mono text-white/88 leading-relaxed whitespace-pre">{code}</code>
      </pre>
    </div>
  );
}
