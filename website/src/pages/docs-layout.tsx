import { DocsSidebar } from '@/components/docs-sidebar';
import { Outlet } from 'react-router-dom';
import { Menu } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { useState } from 'react';

export function DocsLayout() {
  const [sidebarOpen, setSidebarOpen] = useState(false);

  return (
    <div className="mx-auto max-w-7xl px-6 pt-24 pb-16">
      <div className="flex gap-8">
        {/* Mobile sidebar toggle */}
        <div className="lg:hidden fixed bottom-6 right-6 z-50">
          <Button size="icon" onClick={() => setSidebarOpen(!sidebarOpen)} className="rounded-full shadow-lg h-12 w-12">
            <Menu className="h-5 w-5" />
          </Button>
        </div>

        {/* Mobile sidebar overlay */}
        {sidebarOpen && (
          <div
            className="fixed inset-0 bg-background/80 backdrop-blur-sm z-40 lg:hidden"
            onClick={() => setSidebarOpen(false)}
          >
            <div
              className="absolute left-0 top-0 bottom-0 w-72 bg-background border-r border-border p-4 pt-20 overflow-y-auto"
              onClick={(e) => e.stopPropagation()}
            >
              <DocsSidebar mobile onNavigate={() => setSidebarOpen(false)} />
            </div>
          </div>
        )}

        {/* Desktop sidebar */}
        <aside className="hidden lg:block w-56 shrink-0">
          <DocsSidebar />
        </aside>

        {/* Content */}
        <main className="flex-1 min-w-0">
          <Outlet />
        </main>
      </div>
    </div>
  );
}
