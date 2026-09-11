import { Link } from 'react-router-dom';
import ReactMarkdown, { type Components } from 'react-markdown';
import rehypeAutolinkHeadings from 'rehype-autolink-headings';
import rehypeRaw from 'rehype-raw';
import rehypeSlug from 'rehype-slug';
import remarkGfm from 'remark-gfm';
import { isValidElement, type ReactNode } from 'react';
import { CodeBlock } from '@/components/code-block';
import { cn } from '@/lib/utils';

interface MarkdownProps {
  markdown: string;
  className?: string;
}

function textOf(node: ReactNode): string {
  if (typeof node === 'string') return node;
  if (typeof node === 'number') return String(node);
  if (Array.isArray(node)) return node.map(textOf).join('');
  if (isValidElement<{ children?: ReactNode }>(node)) return textOf(node.props.children);
  return '';
}

const components: Components = {
  pre({ children }) {
    const child = Array.isArray(children) ? children[0] : children;
    if (isValidElement<{ className?: string; children?: ReactNode }>(child)) {
      const lang = /language-([\w+-]+)/.exec(child.props.className ?? '')?.[1];
      return <CodeBlock code={textOf(child.props.children)} lang={lang} className="my-5" />;
    }
    return <pre>{children}</pre>;
  },
  a({ href, children, className, ...rest }) {
    if (!href) return <a {...rest}>{children}</a>;
    if (href.startsWith('/') && !/\.[a-z0-9]+$/i.test(href.split('#')[0])) {
      return (
        <Link to={href} className={className}>
          {children}
        </Link>
      );
    }
    const external = /^https?:\/\//.test(href);
    return (
      <a href={href} className={className} {...(external ? { target: '_blank', rel: 'noreferrer' } : {})} {...rest}>
        {children}
      </a>
    );
  },
  table({ children }) {
    return (
      <div className="table-wrap thin-scroll">
        <table>{children}</table>
      </div>
    );
  },
  img({ src, alt }) {
    return <img src={src} alt={alt ?? ''} loading="lazy" decoding="async" />;
  },
};

export function Markdown({ markdown, className }: MarkdownProps) {
  return (
    <div className={cn('prose', className)}>
      <ReactMarkdown
        remarkPlugins={[remarkGfm]}
        rehypePlugins={[
          rehypeRaw,
          rehypeSlug,
          [
            rehypeAutolinkHeadings,
            {
              behavior: 'append',
              properties: { className: 'anchor', ariaHidden: 'true', tabIndex: -1 },
              content: { type: 'text', value: '#' },
            },
          ],
        ]}
        components={components}
      >
        {markdown}
      </ReactMarkdown>
    </div>
  );
}
