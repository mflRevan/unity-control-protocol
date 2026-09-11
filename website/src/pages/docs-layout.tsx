import { Outlet } from 'react-router-dom';
import { DocsSidebar } from '@/components/docs/sidebar';

export function DocsLayout() {
  return (
    <div className="container-x flex gap-8 lg:gap-12">
      <DocsSidebar />
      <Outlet />
    </div>
  );
}
