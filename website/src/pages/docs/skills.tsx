import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import rehypeRaw from 'rehype-raw';
import { docsContent, skillFileRaw } from '@/lib/docs-content';
import { Download } from 'lucide-react';
import { markdownComponents } from '@/components/markdown-page';

function stripFrontmatter(md: string): string {
  const match = md.match(/^---\r?\n[\s\S]*?\r?\n---\r?\n/);
  return match ? md.slice(match[0].length).trimStart() : md;
}

export function SkillsPage() {
  const introContent = docsContent['agents/skills'];
  const skillBody = stripFrontmatter(skillFileRaw);

  const handleDownload = () => {
    const blob = new Blob([skillFileRaw], { type: 'text/markdown' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = 'SKILL.md';
    a.click();
    URL.revokeObjectURL(url);
  };

  return (
    <article className="prose-custom">
      <ReactMarkdown remarkPlugins={[remarkGfm]} rehypePlugins={[rehypeRaw]} components={markdownComponents}>
        {introContent}
      </ReactMarkdown>

      {/* Download button */}
      <div className="my-6">
        <button
          onClick={handleDownload}
          className="inline-flex items-center gap-2 px-5 py-2.5 rounded-lg bg-primary text-primary-foreground font-medium text-sm hover:bg-primary/90 transition-colors cursor-pointer"
        >
          <Download className="h-4 w-4" />
          Download SKILL.md
        </button>
      </div>

      {/* SKILL.md preview */}
      <div className="rounded-lg border border-border bg-[#0d0d0f] overflow-hidden">
        <div className="flex items-center justify-between px-4 py-2.5 bg-[#1a1a1e] border-b border-white/5">
          <span className="text-xs text-white/40 font-mono">SKILL.md</span>
        </div>
        <div className="terminal-prose p-6 max-h-[600px] overflow-y-auto">
          <ReactMarkdown remarkPlugins={[remarkGfm]} rehypePlugins={[rehypeRaw]} components={markdownComponents}>
            {skillBody}
          </ReactMarkdown>
        </div>
      </div>
    </article>
  );
}
