import { RenderMode, ServerRoute } from '@angular/ssr';

/**
 * Pages whose content only changes when someone edits copy are prerendered at build time.
 * Pages fed by the site read API render on each request (and are cached at the edge, see server.ts),
 * so a new class or a sold-out piece shows up without a rebuild. `/memberships` reads its tiers from the CMS
 * (ADR 0040), and there is no API at build time, so it renders on request too.
 */
export const serverRoutes: ServerRoute[] = [
  { path: 'about', renderMode: RenderMode.Prerender },
  { path: 'visit', renderMode: RenderMode.Prerender },
  { path: 'submit', renderMode: RenderMode.Prerender },
  { path: 'artists/submit', renderMode: RenderMode.Prerender },
  { path: '**', renderMode: RenderMode.Server },
];
