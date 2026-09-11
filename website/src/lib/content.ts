import manifest from '../../.generated/content.json';

export interface Heading {
  depth: number;
  text: string;
  id: string;
}

export interface DocPage {
  title: string;
  route: string;
  path: string;
  md: string;
  github: string;
  description: string;
  headings: Heading[];
}

export interface NavGroup {
  title: string;
  items: DocPage[];
}

export interface Skill {
  name: string;
  title: string;
  description: string;
  compatibility: string;
  version: string;
  kind: 'omni' | 'surface';
  source: string;
  raw: string;
  page: string;
  commands: string[];
  path: string;
  md: string;
  github: string;
  headings: Heading[];
}

export interface Content {
  version: string;
  site: string;
  repo: string;
  nav: NavGroup[];
  skills: Skill[];
  install: {
    claudeCode: { marketplace: string; omni: string; surfaces: string };
    skillsSh: string;
    githubCli: string;
    manual: string;
  };
}

export const content = manifest as Content;
export const { version, site, repo, nav, skills, install } = content;

export const docs: DocPage[] = nav.flatMap((group) => group.items);

export function findDoc(pathname: string): DocPage | undefined {
  const clean = pathname.replace(/\/+$/, '') || '/docs';
  return docs.find((page) => page.path === clean);
}

export function docNeighbours(page: DocPage): { prev?: DocPage; next?: DocPage } {
  const index = docs.indexOf(page);
  return { prev: docs[index - 1], next: docs[index + 1] };
}

export function groupOf(page: DocPage): NavGroup | undefined {
  return nav.find((group) => group.items.includes(page));
}

export function findSkill(name: string): Skill | undefined {
  return skills.find((skill) => skill.name === name);
}

export const omniSkill = skills.find((skill) => skill.kind === 'omni');
export const surfaceSkills = skills.filter((skill) => skill.kind === 'surface');
