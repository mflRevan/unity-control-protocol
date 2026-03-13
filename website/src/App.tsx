import { Routes, Route, useLocation } from 'react-router-dom';
import { useEffect } from 'react';
import { Navbar } from '@/components/navbar';
import { Footer } from '@/components/footer';
import { LandingPage } from '@/pages/landing';
import { DocsLayout } from '@/pages/docs-layout';
import { MarkdownDoc } from '@/components/markdown-page';
import { SkillsPage } from '@/pages/docs/skills';

function ScrollToTop() {
  const { pathname } = useLocation();
  useEffect(() => {
    window.scrollTo(0, 0);
  }, [pathname]);
  return null;
}

function App() {
  return (
    <div className="min-h-screen bg-background text-foreground">
      <ScrollToTop />
      <Navbar />
      <Routes>
        <Route path="/" element={<LandingPage />} />
        <Route path="/docs" element={<DocsLayout />}>
          <Route index element={<MarkdownDoc docKey="" />} />
          <Route path="agents/skills" element={<SkillsPage />} />
          <Route path="*" element={<MarkdownDoc />} />
        </Route>
      </Routes>
      <Footer />
    </div>
  );
}

export default App;
