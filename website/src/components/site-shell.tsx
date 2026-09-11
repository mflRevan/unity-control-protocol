import { useEffect } from 'react';
import { Outlet, useLocation } from 'react-router-dom';
import { Footer } from '@/components/footer';
import { Navbar } from '@/components/navbar';

function ScrollManager() {
  const { pathname, hash } = useLocation();
  useEffect(() => {
    if (hash) {
      const target = document.getElementById(decodeURIComponent(hash.slice(1)));
      if (target) {
        target.scrollIntoView({ block: 'start' });
        return;
      }
    }
    window.scrollTo({ top: 0 });
  }, [pathname, hash]);
  return null;
}

export function SiteShell() {
  return (
    <div className="flex min-h-svh flex-col">
      <a
        href="#main"
        className="sr-only focus:not-sr-only focus:fixed focus:top-3 focus:left-3 focus:z-50 focus:rounded-md focus:bg-primary focus:px-3 focus:py-2 focus:text-sm focus:text-primary-foreground"
      >
        Skip to content
      </a>
      <ScrollManager />
      <Navbar />
      <main id="main" className="flex-1">
        <Outlet />
      </main>
      <Footer />
    </div>
  );
}
