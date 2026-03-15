import { useParams } from 'react-router-dom';
import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import rehypeRaw from 'rehype-raw';
import { docsContent } from '@/lib/docs-content';
import type { Components } from 'react-markdown';
import { useState, isValidElement, type ReactNode } from 'react';
import { Copy, Check } from 'lucide-react';

// Simple syntax colorizer for bash/shell code blocks
function colorize(code: string, lang: string): ReactNode[] {
  if (lang === 'json') {
    return code.split('\n').flatMap((line, i, arr) => {
      const nodes: ReactNode[] = [];
      const tokens = line.match(
        /("(?:\\.|[^"])*"\s*:|"(?:\\.|[^"])*"|-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?|true|false|null|[{}\[\],:]+|\s+|[^\s]+)/g,
      ) ?? [line];

      for (let t = 0; t < tokens.length; t++) {
        const tok = tokens[t];
        const key = `${i}-${t}`;

        if (/^\s+$/.test(tok)) {
          nodes.push(tok);
        } else if (/^"(?:\\.|[^"])*"\s*:$/.test(tok)) {
          nodes.push(
            <span key={key} className="text-sky-300">
              {tok}
            </span>,
          );
        } else if (/^"(?:\\.|[^"])*"$/.test(tok)) {
          nodes.push(
            <span key={key} className="text-emerald-300">
              {tok}
            </span>,
          );
        } else if (/^-?\d/.test(tok)) {
          nodes.push(
            <span key={key} className="text-amber-300">
              {tok}
            </span>,
          );
        } else if (/^(true|false|null)$/.test(tok)) {
          nodes.push(
            <span key={key} className="text-fuchsia-300">
              {tok}
            </span>,
          );
        } else if (/^[{}\[\],:]+$/.test(tok)) {
          nodes.push(
            <span key={key} className="text-white/45">
              {tok}
            </span>,
          );
        } else {
          nodes.push(
            <span key={key} className="text-white/88">
              {tok}
            </span>,
          );
        }
      }

      if (i < arr.length - 1) {
        nodes.push('\n');
      }
      return nodes;
    });
  }

  if (lang !== 'bash' && lang !== 'shell' && lang !== 'sh') {
    return [
      <span key="plain" className="text-white/88">
        {code}
      </span>,
    ];
  }

  return code.split('\n').flatMap((line, i, arr) => {
    const nodes: ReactNode[] = [];

    if (/^\s*#/.test(line)) {
      // Full-line comment
      nodes.push(
        <span key={i} className="text-white/30 italic">
          {line}
        </span>,
      );
    } else {
      // Tokenize the line
      const tokens = line.match(/(?:"[^"]*"|'[^']*'|--?\w[\w-]*|[^\s"']+|\s+)/g) ?? [line];
      let isFirst = true;
      for (let t = 0; t < tokens.length; t++) {
        const tok = tokens[t];
        const k = `${i}-${t}`;

        if (/^\s+$/.test(tok)) {
          nodes.push(tok);
          continue;
        }

        if (isFirst && tok === 'ucp') {
          // Command prefix
          nodes.push(
            <span key={k} className="text-purple-400 font-semibold">
              {tok}
            </span>,
          );
          isFirst = false;
        } else if (isFirst && /^\w/.test(tok)) {
          // First non-whitespace word (subcommand after ucp, or standalone command)
          nodes.push(
            <span key={k} className="text-cyan-400">
              {tok}
            </span>,
          );
          isFirst = false;
        } else if (/^--?\w/.test(tok)) {
          // Flags
          nodes.push(
            <span key={k} className="text-amber-400">
              {tok}
            </span>,
          );
        } else if (/^["']/.test(tok)) {
          // Quoted strings
          nodes.push(
            <span key={k} className="text-green-400">
              {tok}
            </span>,
          );
        } else if (/^(Assets\/|Builds\/|Packages\/|src\/|\.\/)/.test(tok)) {
          // Paths
          nodes.push(
            <span key={k} className="text-orange-300">
              {tok}
            </span>,
          );
        } else if (/^\[OK\]/.test(tok)) {
          nodes.push(
            <span key={k} className="text-green-400 font-semibold">
              {tok}
            </span>,
          );
        } else if (/^(true|false)$/.test(tok)) {
          nodes.push(
            <span key={k} className="text-amber-300">
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

    if (i < arr.length - 1) {
      nodes.push('\n');
    }
    return nodes;
  });
}

function CodeFence({ className, children }: { className?: string; children?: ReactNode }) {
  const [copied, setCopied] = useState(false);
  const code = String(children).replace(/\n$/, '');
  const lang = className?.replace('language-', '') ?? '';

  const handleCopy = () => {
    navigator.clipboard.writeText(code);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  const highlighted = colorize(code, lang);

  return (
    <div className="rounded-lg overflow-hidden border border-border bg-[#0d0d0f] my-5">
      <div className="flex items-center justify-between px-4 py-2 bg-[#1a1a1e] border-b border-white/5">
        <span className="text-xs text-white/40 font-mono">{lang || 'code'}</span>
        <button onClick={handleCopy} className="text-white/30 hover:text-white/70 transition-colors">
          {copied ? <Check className="h-3.5 w-3.5" /> : <Copy className="h-3.5 w-3.5" />}
        </button>
      </div>
      <pre className="p-4 overflow-x-auto">
        <code className="text-sm font-mono leading-relaxed whitespace-pre text-white/88">{highlighted}</code>
      </pre>
    </div>
  );
}

export const markdownComponents: Components = {
  code({ children, ...props }) {
    // Inline code only - block code is handled by `pre`
    return <code {...props}>{children}</code>;
  },
  pre({ children }) {
    // Extract props from the nested <code> element
    if (isValidElement(children)) {
      const { className, children: codeChildren } = children.props as {
        className?: string;
        children?: ReactNode;
      };
      return <CodeFence className={className}>{codeChildren}</CodeFence>;
    }
    return <pre>{children}</pre>;
  },
  a({ href, children, ...props }) {
    if (href?.startsWith('/') || href?.startsWith('#')) {
      return (
        <a href={href} {...props}>
          {children}
        </a>
      );
    }
    return (
      <a href={href} target="_blank" rel="noopener noreferrer" {...props}>
        {children}
      </a>
    );
  },
};

interface MarkdownDocProps {
  docKey?: string;
}

export function MarkdownDoc({ docKey }: MarkdownDocProps) {
  const params = useParams();
  const key = docKey !== undefined ? docKey : (params['*'] ?? '');
  const content = docsContent[key];

  if (!content) {
    return (
      <article className="prose-custom">
        <h1>Page Not Found</h1>
        <p>The documentation page you're looking for doesn't exist.</p>
      </article>
    );
  }

  return (
    <article className="prose-custom">
      <ReactMarkdown remarkPlugins={[remarkGfm]} rehypePlugins={[rehypeRaw]} components={markdownComponents}>
        {content}
      </ReactMarkdown>
    </article>
  );
}
