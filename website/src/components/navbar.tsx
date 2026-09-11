import { Github, Menu } from 'lucide-react';
import { useState } from 'react';
import { Link, NavLink, useLocation } from 'react-router-dom';
import { Sheet, SheetContent, SheetTitle, SheetTrigger } from '@/components/ui/sheet';
import { LogoMark } from '@/components/logo';
import { ThemeToggle } from '@/components/theme-toggle';
import { DocsNav } from '@/components/docs/sidebar';
import { repo, version } from '@/lib/content';
import { cn } from '@/lib/utils';

const links = [
  { to: '/docs', label: 'Docs' },
  { to: '/skills', label: 'Skills' },
];

export function Navbar() {
  const [open, setOpen] = useState(false);
  const location = useLocation();
  const inDocs = location.pathname.startsWith('/docs');

  return (
    <header className="sticky top-0 z-40 border-b border-border/70 bg-background/80 backdrop-blur-md">
      <div className="container-x flex h-14 items-center gap-3">
        <Sheet open={open} onOpenChange={setOpen}>
          <SheetTrigger
            aria-label="Open navigation"
            className="-ml-2 inline-flex size-9 items-center justify-center rounded-md text-muted-foreground hover:bg-muted hover:text-foreground md:hidden"
          >
            <Menu className="size-5" aria-hidden />
          </SheetTrigger>
          <SheetContent side="left" className="w-80 overflow-y-auto p-0">
            <SheetTitle className="sr-only">Navigation</SheetTitle>
            <div className="border-b border-border px-5 py-4">
              <Link to="/" onClick={() => setOpen(false)} className="inline-flex items-center gap-2 font-semibold">
                <LogoMark />
                UCP
              </Link>
            </div>
            <nav className="flex flex-col gap-1 border-b border-border px-3 py-3 text-sm" aria-label="Site">
              {links.map((link) => (
                <NavLink
                  key={link.to}
                  to={link.to}
                  onClick={() => setOpen(false)}
                  className={({ isActive }) =>
                    cn('rounded-md px-2 py-1.5 text-muted-foreground hover:bg-muted hover:text-foreground', isActive && 'text-foreground')
                  }
                >
                  {link.label}
                </NavLink>
              ))}
              <a href={repo} target="_blank" rel="noreferrer" className="rounded-md px-2 py-1.5 text-muted-foreground hover:bg-muted hover:text-foreground">
                GitHub
              </a>
            </nav>
            <div className="px-3 py-4">
              <DocsNav onNavigate={() => setOpen(false)} />
            </div>
          </SheetContent>
        </Sheet>

        <Link to="/" className="inline-flex items-center gap-2.5 font-semibold tracking-tight" aria-label="Unity Control Protocol home">
          <LogoMark />
          <span className="hidden sm:inline">Unity Control Protocol</span>
          <span className="sm:hidden">UCP</span>
        </Link>
        <Link
          to={`${repo}/releases`}
          className="hidden rounded-full border border-border px-2 py-0.5 font-mono text-[11px] text-muted-foreground hover:text-foreground sm:inline"
          target="_blank"
          rel="noreferrer"
        >
          v{version}
        </Link>

        <nav className="ml-6 hidden items-center gap-1 text-sm md:flex" aria-label="Site">
          {links.map((link) => (
            <NavLink
              key={link.to}
              to={link.to}
              className={({ isActive }) =>
                cn(
                  'rounded-md px-2.5 py-1.5 text-muted-foreground transition-colors hover:text-foreground',
                  (isActive || (link.to === '/docs' && inDocs)) && 'text-foreground',
                )
              }
            >
              {link.label}
            </NavLink>
          ))}
          <a href="/llms.txt" className="rounded-md px-2.5 py-1.5 font-mono text-[13px] text-muted-foreground transition-colors hover:text-foreground">
            llms.txt
          </a>
        </nav>

        <div className="ml-auto flex items-center gap-1">
          <a
            href={repo}
            target="_blank"
            rel="noreferrer"
            aria-label="GitHub repository"
            className="inline-flex size-8 items-center justify-center rounded-md text-muted-foreground transition-colors hover:bg-muted hover:text-foreground"
          >
            <Github className="size-4" aria-hidden />
          </a>
          <ThemeToggle />
        </div>
      </div>
    </header>
  );
}
