import { Link, useLocation } from 'react-router-dom';
import {
  Terminal,
  BookOpen,
  Download,
  Plug,
  Gamepad2,
  Gauge,
  FolderCode,
  FileCode2,
  Camera,
  ScrollText,
  TestTube2,
  Zap,
  GitBranch,
  ChevronRight,
  Box,
  Package,
  Palette,
  Puzzle,
  Settings,
  Hammer,
  Bot,
  Search,
} from 'lucide-react';
import { cn } from '@/lib/utils';
import { docsNavigation } from '@/lib/docs-content';

const iconByHref: Record<string, React.ComponentType<{ className?: string }>> = {
  '/docs': BookOpen,
  '/docs/installation': Download,
  '/docs/quickstart': Zap,
  '/docs/overview': Terminal,
  '/docs/overview/project-setup': Plug,
  '/docs/overview/editor-lifecycle': Terminal,
  '/docs/authoring/scenes': FolderCode,
  '/docs/authoring/objects': Box,
  '/docs/authoring/prefabs': Puzzle,
  '/docs/authoring/assets': Package,
  '/docs/authoring/materials': Palette,
  '/docs/authoring/references': Search,
  '/docs/authoring/files': FileCode2,
  '/docs/authoring/scripting': ScrollText,
  '/docs/runtime/play-mode': Gamepad2,
  '/docs/runtime/logs-and-media': Camera,
  '/docs/runtime/testing': TestTube2,
  '/docs/runtime/profiler': Gauge,
  '/docs/project/packages': Download,
  '/docs/project/settings': Settings,
  '/docs/project/build': Hammer,
  '/docs/project/version-control': GitBranch,
  '/docs/agents/skills': Bot,
};

interface DocsSidebarProps {
  mobile?: boolean;
  onNavigate?: () => void;
}

export function DocsSidebar({ mobile, onNavigate }: DocsSidebarProps) {
  const location = useLocation();

  return (
    <nav
      className={cn(
        'docs-sidebar-scroll space-y-1',
        mobile ? 'px-2 py-4' : 'sticky top-24 max-h-[calc(100vh-8rem)] overflow-y-auto pr-2',
      )}
    >
      {docsNavigation.map((group, gi) => (
        <div key={group.title} className={gi > 0 ? 'pt-5' : ''}>
          <div className="flex items-center gap-2.5 mb-3 px-3">
            <div className="h-4 w-[3px] rounded-full bg-primary/50" />
            <h4 className="text-[0.75rem] font-semibold uppercase tracking-[0.1em] text-primary/55" style={{ textShadow: '0 0 12px var(--color-primary)' }}>
              {group.title}
            </h4>
          </div>
          <ul className="space-y-0.5">
            {group.items.map((item) => {
              const isActive = location.pathname === item.href;
              const Icon = iconByHref[item.href] ?? Terminal;
              return (
                <li key={item.href}>
                  <Link
                    to={item.href}
                    onClick={onNavigate}
                    className={cn(
                      'flex items-center gap-2.5 py-2 rounded-md text-sm transition-all duration-200',
                      isActive
                        ? 'bg-primary/8 text-primary font-medium border-l-2 border-primary px-3 pl-[10px]'
                        : 'text-muted-foreground hover:text-foreground hover:bg-accent/50 px-3',
                    )}
                  >
                    <Icon className="h-4 w-4 shrink-0" />
                    <span>{item.title}</span>
                    {isActive && <ChevronRight className="h-3 w-3 ml-auto" />}
                  </Link>
                </li>
              );
            })}
          </ul>
        </div>
      ))}
    </nav>
  );
}
