import { Link, useLocation } from 'react-router-dom';
import {
  Terminal,
  BookOpen,
  Download,
  Plug,
  Gamepad2,
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
} from 'lucide-react';
import { cn } from '@/lib/utils';

interface NavItem {
  title: string;
  href: string;
  icon: React.ComponentType<{ className?: string }>;
}

interface NavGroup {
  title: string;
  items: NavItem[];
}

const navigation: NavGroup[] = [
  {
    title: 'Getting Started',
    items: [
      { title: 'Introduction', href: '/docs', icon: BookOpen },
      { title: 'Installation', href: '/docs/installation', icon: Download },
      { title: 'Quick Start', href: '/docs/quickstart', icon: Zap },
    ],
  },
  {
    title: 'Commands',
    items: [
      { title: 'Overview', href: '/docs/commands', icon: Terminal },
      { title: 'Connection', href: '/docs/commands/connection', icon: Plug },
      { title: 'Play Mode', href: '/docs/commands/playmode', icon: Gamepad2 },
      { title: 'Scenes', href: '/docs/commands/scenes', icon: FolderCode },
      { title: 'Files', href: '/docs/commands/files', icon: FileCode2 },
      { title: 'Screenshots & Logs', href: '/docs/commands/media', icon: Camera },
      { title: 'Testing', href: '/docs/commands/testing', icon: TestTube2 },
      { title: 'Scripting', href: '/docs/commands/scripting', icon: ScrollText },
      { title: 'Version Control', href: '/docs/commands/vcs', icon: GitBranch },
      { title: 'Objects & Components', href: '/docs/commands/objects', icon: Box },
      { title: 'Assets', href: '/docs/commands/assets', icon: Package },
      { title: 'Materials', href: '/docs/commands/materials', icon: Palette },
      { title: 'Prefabs', href: '/docs/commands/prefabs', icon: Puzzle },
      { title: 'Settings', href: '/docs/commands/settings', icon: Settings },
      { title: 'Build Pipeline', href: '/docs/commands/build', icon: Hammer },
    ],
  },
  {
    title: 'Agents',
    items: [{ title: 'Skills', href: '/docs/agents/skills', icon: Bot }],
  },
];

interface DocsSidebarProps {
  mobile?: boolean;
  onNavigate?: () => void;
}

export function DocsSidebar({ mobile, onNavigate }: DocsSidebarProps) {
  const location = useLocation();

  return (
    <nav
      className={cn('space-y-6', mobile ? 'px-2 py-4' : 'sticky top-24 max-h-[calc(100vh-8rem)] overflow-y-auto pr-4')}
    >
      {navigation.map((group) => (
        <div key={group.title}>
          <h4 className="text-xs font-semibold text-muted-foreground uppercase tracking-wider mb-2 px-3">
            {group.title}
          </h4>
          <ul className="space-y-0.5">
            {group.items.map((item) => {
              const isActive = location.pathname === item.href;
              return (
                <li key={item.href}>
                  <Link
                    to={item.href}
                    onClick={onNavigate}
                    className={cn(
                      'flex items-center gap-2.5 px-3 py-2 rounded-md text-sm transition-colors',
                      isActive
                        ? 'bg-primary/10 text-primary font-medium'
                        : 'text-muted-foreground hover:text-foreground hover:bg-accent',
                    )}
                  >
                    <item.icon className="h-4 w-4 shrink-0" />
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
