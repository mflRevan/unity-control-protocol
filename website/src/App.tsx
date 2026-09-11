import { lazy, Suspense } from 'react';
import { Navigate, Route, Routes } from 'react-router-dom';
import { SiteShell } from '@/components/site-shell';
import { NotFound } from '@/pages/not-found';

const Landing = lazy(() => import('@/pages/landing').then((m) => ({ default: m.Landing })));
const DocsLayout = lazy(() => import('@/pages/docs-layout').then((m) => ({ default: m.DocsLayout })));
const DocPage = lazy(() => import('@/pages/doc-page').then((m) => ({ default: m.DocPage })));
const SkillsIndex = lazy(() => import('@/pages/skills-index').then((m) => ({ default: m.SkillsIndex })));
const SkillPage = lazy(() => import('@/pages/skill-page').then((m) => ({ default: m.SkillPage })));

function Loading() {
  return <div className="min-h-[50svh]" aria-busy="true" />;
}

export default function App() {
  return (
    <Suspense fallback={<Loading />}>
      <Routes>
        <Route element={<SiteShell />}>
          <Route index element={<Landing />} />
          <Route path="docs" element={<DocsLayout />}>
            <Route index element={<DocPage />} />
            <Route path="*" element={<DocPage />} />
          </Route>
          {/* The old skills page moved out of the docs tree. */}
          <Route path="docs/agents/skills" element={<Navigate to="/skills" replace />} />
          <Route path="skills" element={<SkillsIndex />} />
          <Route path="skills/:name" element={<SkillPage />} />
          <Route path="*" element={<NotFound />} />
        </Route>
      </Routes>
    </Suspense>
  );
}
