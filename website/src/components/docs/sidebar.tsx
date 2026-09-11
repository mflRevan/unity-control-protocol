import { NavLink } from 'react-router-dom';
import { nav } from '@/lib/content';
import { cn } from '@/lib/utils';

export function DocsNav({ onNavigate }: { onNavigate?: () => void }) {
  return (
    <nav aria-label="Documentation" className="space-y-6">
      {nav.map((group) => (
        <div key={group.title}>
          <h2 className="mb-1.5 px-2 text-[11px] font-semibold uppercase tracking-wider text-muted-foreground">{group.title}</h2>
          <ul className="space-y-px">
            {group.items.map((page) => (
              <li key={page.path}>
                <NavLink
                  to={page.path}
                  end
                  onClick={onNavigate}
                  className={({ isActive }) =>
                    cn(
                      'block rounded-md px-2 py-1.5 text-[13.5px] leading-5 text-muted-foreground transition-colors hover:bg-muted hover:text-foreground',
                      isActive && 'bg-accent font-medium text-accent-foreground hover:bg-accent',
                    )
                  }
                >
                  {page.title}
                </NavLink>
              </li>
            ))}
          </ul>
        </div>
      ))}
    </nav>
  );
}

export function DocsSidebar() {
  return (
    <aside className="sticky top-14 hidden h-[calc(100svh-3.5rem)] w-60 shrink-0 md:block">
      <div className="thin-scroll h-full overflow-y-auto border-r border-border/70 py-6 pr-3">
        <DocsNav />
      </div>
    </aside>
  );
}
