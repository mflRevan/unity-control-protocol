import { Link, useLocation } from 'react-router-dom';
import { Moon, Sun, Menu, X, Github } from 'lucide-react';
import { useTheme } from '@/components/theme-provider';
import { Button } from '@/components/ui/button';
import { useState, useEffect, useRef } from 'react';
import { cn } from '@/lib/utils';
import { motion } from 'framer-motion';

function DiscordIcon({ className }: { className?: string }) {
  return (
    <svg viewBox="0 0 24 24" className={className} fill="currentColor" aria-hidden="true">
      <path d="M18.8943 4.34399C17.5183 3.71467 16.057 3.256 14.5317 3C14.3396 3.33067 14.1263 3.77866 13.977 4.13067C12.3546 3.89599 10.7439 3.89599 9.14391 4.13067C8.99457 3.77866 8.77056 3.33067 8.58922 3C7.05325 3.256 5.59191 3.71467 4.22552 4.34399C1.46286 8.41865 0.716188 12.3973 1.08952 16.3226C2.92418 17.6559 4.69486 18.4666 6.4346 19C6.86126 18.424 7.24527 17.8053 7.57594 17.1546C6.9466 16.92 6.34927 16.632 5.77327 16.2906C5.9226 16.184 6.07194 16.0667 6.21061 15.9493C9.68793 17.5387 13.4543 17.5387 16.889 15.9493C17.0383 16.0667 17.177 16.184 17.3263 16.2906C16.7503 16.632 16.153 16.92 15.5236 17.1546C15.8543 17.8053 16.2383 18.424 16.665 19C18.4036 18.4666 20.185 17.6559 22.01 16.3226C22.4687 11.7787 21.2836 7.83202 18.8943 4.34399ZM8.05593 13.9013C7.01058 13.9013 6.15725 12.952 6.15725 11.7893C6.15725 10.6267 6.98925 9.67731 8.05593 9.67731C9.11191 9.67731 9.97588 10.6267 9.95454 11.7893C9.95454 12.952 9.11191 13.9013 8.05593 13.9013ZM15.065 13.9013C14.0196 13.9013 13.1652 12.952 13.1652 11.7893C13.1652 10.6267 13.9983 9.67731 15.065 9.67731C16.121 9.67731 16.985 10.6267 16.9636 11.7893C16.9636 12.952 16.1317 13.9013 15.065 13.9013Z" />
    </svg>
  );
}

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
        scrolled
          ? 'bg-background/70 backdrop-blur-xl border-b border-border/40 shadow-[0_10px_40px_-24px_rgba(0,0,0,0.55)]'
          : 'bg-transparent',
      )}
    >
      <nav className="mx-auto max-w-7xl flex items-center justify-between px-6 py-4">
        <Link to="/" className="flex items-center gap-3 group">
          <div className="relative flex h-11 w-11 items-center justify-center transition-transform duration-300 group-hover:scale-[1.04]">
            <img
              src="/favicon.svg"
              alt="UCP logo"
              className="h-11 w-11 drop-shadow-[0_10px_20px_rgba(109,40,217,0.28)]"
            />
          </div>
          <div className="leading-none">
            <div className="font-semibold text-[0.72rem] uppercase tracking-[0.34em] text-primary/70">
              Unity Control
            </div>
            <span className="font-bold text-lg tracking-tight">UCP</span>
          </div>
        </Link>

        {/* Desktop Nav – sliding indicator */}
        <div className="hidden md:flex items-center rounded-xl border border-border/40 bg-muted/25 p-1 backdrop-blur-xl shadow-[inset_0_1px_3px_rgba(0,0,0,0.06)]">
          {navLinks.map((link) => {
            const isActive =
              location.pathname === link.href ||
              (link.href !== '/' && location.pathname.startsWith(link.href));
            return (
              <Link key={link.href} to={link.href} className="relative px-5 py-1.5 text-sm font-medium">
                {isActive && (
                  <motion.div
                    layoutId="nav-indicator"
                    className="absolute inset-0.5 rounded-[9px] bg-primary shadow-[0_0_20px_rgba(167,139,250,0.25)]"
                    transition={{ type: 'spring', stiffness: 500, damping: 32 }}
                  />
                )}
                <span
                  className={cn(
                    'relative z-10 transition-colors duration-200',
                    isActive
                      ? 'text-primary-foreground font-semibold'
                      : 'text-muted-foreground hover:text-foreground/70',
                  )}
                >
                  {link.label}
                </span>
              </Link>
            );
          })}
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
            <Button
              variant="ghost"
              size="icon"
              className="rounded-full border border-border/60 bg-background/72 backdrop-blur-xl hover:border-primary/30 hover:bg-primary/8"
            >
              <Github className="h-4 w-4" />
            </Button>
          </a>
          <a href="https://discord.gg/F4RjhdVTbz" target="_blank" rel="noopener noreferrer">
            <Button
              variant="ghost"
              size="icon"
              className="rounded-full border border-[#5865F2]/70 bg-[#5865F2] text-white backdrop-blur-xl hover:border-[#4752C4] hover:bg-[#4752C4] flex items-center justify-center"
            >
              <DiscordIcon className="h-4 w-4" />
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
              <a href="https://discord.gg/F4RjhdVTbz" target="_blank" rel="noopener noreferrer">
                <Button
                  variant="ghost"
                  size="icon"
                  className="border border-[#5865F2]/70 bg-[#5865F2] text-white hover:border-[#4752C4] hover:bg-[#4752C4] flex items-center justify-center"
                >
                  <DiscordIcon className="h-4 w-4" />
                </Button>
              </a>
            </div>
          </div>
        </div>
      )}
    </header>
  );
}
