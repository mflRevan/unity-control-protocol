import { Link } from 'react-router-dom';
import { LogoMark } from '@/components/logo';
import { repo, version } from '@/lib/content';

const columns = [
  {
    title: 'Product',
    links: [
      { label: 'Documentation', to: '/docs' },
      { label: 'Quick start', to: '/docs/quickstart' },
      { label: 'Agent skills', to: '/skills' },
      { label: 'Changelog', href: `${repo}/blob/main/CHANGELOG.md` },
    ],
  },
  {
    title: 'For agents',
    links: [
      { label: 'llms.txt', href: '/llms.txt' },
      { label: 'llms-full.txt', href: '/llms-full.txt' },
      { label: 'skills/index.json', href: '/skills/index.json' },
      { label: 'This site as Markdown', href: '/index.md' },
    ],
  },
  {
    title: 'Community',
    links: [
      { label: 'GitHub', href: repo },
      { label: 'npm', href: 'https://www.npmjs.com/package/@mflrevan/ucp' },
      { label: 'Discord', href: 'https://discord.gg/F4RjhdVTbz' },
      { label: 'Issues', href: `${repo}/issues` },
    ],
  },
];

export function Footer() {
  return (
    <footer className="border-t border-border/70">
      <div className="container-x grid gap-10 py-12 md:grid-cols-[1.4fr_repeat(3,1fr)]">
        <div className="max-w-xs">
          <div className="inline-flex items-center gap-2.5 font-semibold">
            <LogoMark />
            Unity Control Protocol
          </div>
          <p className="mt-3 text-sm leading-6 text-muted-foreground">
            The Unity Editor as a command line, for humans and agents. MIT licensed, runs entirely on your machine.
          </p>
          <p className="mt-4 font-mono text-xs text-muted-foreground">v{version}</p>
        </div>
        {columns.map((column) => (
          <div key={column.title}>
            <h2 className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">{column.title}</h2>
            <ul className="mt-3 space-y-2 text-sm">
              {column.links.map((link) => (
                <li key={link.label}>
                  {'to' in link && link.to ? (
                    <Link to={link.to} className="text-foreground/80 transition-colors hover:text-foreground">
                      {link.label}
                    </Link>
                  ) : (
                    <a
                      href={link.href}
                      className="text-foreground/80 transition-colors hover:text-foreground"
                      {...(link.href?.startsWith('http') ? { target: '_blank', rel: 'noreferrer' } : {})}
                    >
                      {link.label}
                    </a>
                  )}
                </li>
              ))}
            </ul>
          </div>
        ))}
      </div>
    </footer>
  );
}
