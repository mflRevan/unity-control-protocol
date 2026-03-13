import { Link, useLocation } from 'react-router-dom';
import { Moon, Sun, Menu, X, Github, Terminal } from 'lucide-react';
import { useTheme } from '@/components/theme-provider';
import { Button } from '@/components/ui/button';
import { useState, useEffect, useRef } from 'react';
import { cn } from '@/lib/utils';

const navLinks = [
  { label: 'Home', href: '/' },
  { label: 'Docs', href: '/docs' },
];

export function Navbar() {
  const { resolved, setTheme } = useTheme();
  const [scrolled, setScrolled] = useState(false);
  const [mobileOpen, setMobileOpen] = useState(false);
  const location = useLocation();
  const prevPathRef = useRef(location.pathname);

  useEffect(() => {
    const onScroll = () => setScrolled(window.scrollY > 20);
    window.addEventListener('scroll', onScroll);
    return () => window.removeEventListener('scroll', onScroll);
  }, []);

  // Close mobile menu on navigation without triggering cascading renders
  if (prevPathRef.current !== location.pathname) {
    prevPathRef.current = location.pathname;
    if (mobileOpen) setMobileOpen(false);
  }

  return (
    <header
      className={cn(
        'fixed top-0 left-0 right-0 z-50 transition-all duration-500',
        scrolled ? 'bg-background/70 backdrop-blur-xl border-b border-border/40 shadow-[0_10px_40px_-24px_rgba(0,0,0,0.55)]' : 'bg-transparent',
      )}
    >
      <nav className="mx-auto max-w-7xl flex items-center justify-between px-6 py-4">
        <Link to="/" className="flex items-center gap-3 group">
          <div className="relative flex h-10 w-10 items-center justify-center rounded-2xl border border-primary/20 bg-linear-to-br from-primary/18 via-primary/8 to-transparent shadow-[0_12px_30px_-18px_rgba(109,40,217,0.65)] transition-transform duration-300 group-hover:scale-[1.04]">
            <Terminal className="h-5 w-5 text-primary transition-transform group-hover:scale-110" />
            <div className="absolute -left-2 top-1/2 hidden -translate-y-1/2 text-primary/70 md:block">&gt;</div>
          </div>
          <div className="leading-none">
            <div className="font-semibold text-[0.72rem] uppercase tracking-[0.34em] text-primary/70">Unity Control</div>
            <span className="font-bold text-lg tracking-tight">UCP</span>
          </div>
        </Link>

        {/* Desktop Nav */}
        <div className="hidden md:flex items-center gap-1 rounded-full border border-border/60 bg-background/82 p-1.5 shadow-[0_8px_30px_-24px_rgba(0,0,0,0.45)] backdrop-blur-xl">
          {navLinks.map((link) => (
            <Link
              key={link.href}
              to={link.href}
              className={cn(
                'relative overflow-hidden rounded-full px-4 py-2 text-sm font-medium transition-all duration-300',
                location.pathname === link.href || (link.href !== '/' && location.pathname.startsWith(link.href))
                  ? 'bg-linear-to-r from-primary via-primary/90 to-primary/75 text-primary-foreground shadow-[0_10px_30px_-18px_rgba(109,40,217,0.9)]'
                  : 'text-muted-foreground hover:bg-accent/70 hover:text-foreground',
              )}
            >
              {link.label}
            </Link>
          ))}
        </div>

        <div className="hidden md:flex items-center gap-2">
          <Button
            variant="ghost"
            size="icon"
            className="rounded-full border border-border/60 bg-background/72 backdrop-blur-xl hover:border-primary/30 hover:bg-primary/8"
            onClick={() => setTheme(resolved === 'dark' ? 'light' : 'dark')}
          >
            {resolved === 'dark' ? <Sun className="h-4 w-4" /> : <Moon className="h-4 w-4" />}
          </Button>
          <a href="https://github.com/mflRevan/unity-control-protocol" target="_blank" rel="noopener noreferrer">
            <Button variant="ghost" size="icon" className="rounded-full border border-border/60 bg-background/72 backdrop-blur-xl hover:border-primary/30 hover:bg-primary/8">
              <Github className="h-4 w-4" />
            </Button>
          </a>
          <Link to="/docs">
            <Button size="sm" className="ml-2 rounded-full px-4 shadow-[0_14px_32px_-18px_rgba(109,40,217,0.9)]">
              Get Started
            </Button>
          </Link>
        </div>

        {/* Mobile Toggle */}
        <Button variant="ghost" size="icon" className="md:hidden" onClick={() => setMobileOpen(!mobileOpen)}>
          {mobileOpen ? <X className="h-5 w-5" /> : <Menu className="h-5 w-5" />}
        </Button>
      </nav>

      {/* Mobile Menu */}
      {mobileOpen && (
        <div className="md:hidden bg-background/95 backdrop-blur-xl border-b border-border">
          <div className="px-6 py-4 space-y-2">
            {navLinks.map((link) => (
              <Link
                key={link.href}
                to={link.href}
                className={cn(
                  'block rounded-2xl px-4 py-3 text-sm font-medium transition-colors',
                  location.pathname === link.href || (link.href !== '/' && location.pathname.startsWith(link.href))
                    ? 'bg-primary text-primary-foreground'
                    : 'text-muted-foreground hover:text-foreground',
                )}
              >
                {link.label}
              </Link>
            ))}
            <div className="flex items-center gap-2 pt-2 border-t border-border mt-2">
              <Button variant="ghost" size="icon" onClick={() => setTheme(resolved === 'dark' ? 'light' : 'dark')}>
                {resolved === 'dark' ? <Sun className="h-4 w-4" /> : <Moon className="h-4 w-4" />}
              </Button>
              <a href="https://github.com/mflRevan/unity-control-protocol" target="_blank" rel="noopener noreferrer">
                <Button variant="ghost" size="icon">
                  <Github className="h-4 w-4" />
                </Button>
              </a>
            </div>
          </div>
        </div>
      )}
    </header>
  );
}
