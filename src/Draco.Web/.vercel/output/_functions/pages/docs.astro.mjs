import { c as createComponent, r as renderComponent, a as renderTemplate, m as maybeRenderHead, d as addAttribute } from '../chunks/astro/server_C1xCvP2A.mjs';
import 'piccolore';
import { $ as $$Layout } from '../chunks/Layout_DhxFmJuB.mjs';
import { g as getCollection } from '../chunks/_astro_content_DHv6_Vnq.mjs';
import postgres from 'postgres';
export { renderers } from '../renderers.mjs';

const $$Index = createComponent(async ($$result, $$props, $$slots) => {
  const legalEntries = await getCollection("legal");
  const DATABASE_URL = "postgresql://neondb_owner:npg_DMQ6XPnf8yCB@ep-small-field-aeynkd82-pooler.c-2.us-east-2.aws.neon.tech/neondb?sslmode=require&channel_binding=require";
  let changelogs = [
    {
      version: "1.0.0",
      date: "Feb 17, 2026",
      title: "Project Draco Launch",
      content: "Official Project Draco Launch. High-performance web portal, advanced configuration system, and premium L-Drago aesthetics."
    },
    {
      version: "0.9.5",
      date: "Feb 10, 2026",
      title: "Initial Alpha",
      content: "Initial CLI sentinel implementation with Gemini AI anomaly detection."
    }
  ];
  {
    try {
      const sql = postgres(DATABASE_URL);
      const dbReleases = await sql`
      SELECT id, version, title, description, date 
      FROM releases 
      WHERE project_id = 6 AND published = true
      ORDER BY date DESC, id DESC
    `;
      if (dbReleases && dbReleases.length > 0) {
        const releaseIds = dbReleases.map((r) => r.id);
        const dbSections = await sql`
        SELECT id, release_id, category, order_index
        FROM release_sections
        WHERE release_id = ANY(${releaseIds})
        ORDER BY order_index ASC
      `;
        const sectionIds = dbSections.map((s) => s.id);
        const dbItems = await sql`
        SELECT section_id, title, description, details, order_index
        FROM release_items
        WHERE section_id = ANY(${sectionIds})
        ORDER BY order_index ASC
      `;
        changelogs = dbReleases.map((r) => {
          const sections = dbSections.filter((s) => s.release_id === r.id).map((s) => ({
            category: s.category,
            items: dbItems.filter((i) => i.section_id === s.id)
          }));
          return {
            version: r.version,
            title: r.title,
            description: r.description,
            date: new Date(r.date).toLocaleDateString("en-US", { month: "short", day: "numeric", year: "numeric" }),
            sections
          };
        });
      }
    } catch (e) {
      console.error("Failed to fetch changelogs from database:", e);
    }
  }
  return renderTemplate`${renderComponent($$result, "Layout", $$Layout, { "title": "Documentation" }, { "default": async ($$result2) => renderTemplate` ${maybeRenderHead()}<div class="container"> <section class="legal-content"> <h1 style="color: var(--text-color);">Project Documentation</h1> <p style="margin-bottom: 3rem; color: var(--sub-text-color);">Resources, Legal Policies, and Changelogs for the Draco Sentinel.</p> <div style="display: grid; grid-template-columns: repeat(auto-fit, minmax(300px, 1fr)); gap: 3rem;"> <!-- Legal Docs Section --> <div> <h2 style="color: var(--meteo-bronze); margin-bottom: 1.5rem; text-transform: uppercase; font-size: 1.2rem; letter-spacing: 1px;">Legal & Privacy</h2> <ul style="list-style: none; padding: 0;"> ${legalEntries.map((entry) => renderTemplate`<li style="margin-bottom: 1rem;"> <a${addAttribute(`/${entry.slug || entry.id}`, "href")} style="font-size: 1.1rem; display: flex; align-items: center;"> <span style="margin-right: 10px; color: var(--dragon-red);">•</span> ${(entry.slug || entry.id || "document").toUpperCase()} </a> </li>`)} </ul> </div> <!-- Changelogs Section --> <div> <h2 style="color: var(--meteo-bronze); margin-bottom: 2rem; text-transform: uppercase; font-size: 1.2rem; letter-spacing: 1px;">Latest Releases</h2> <div style="display: flex; flex-direction: column; gap: 2rem;"> ${changelogs.map((log) => renderTemplate`<div style="background: var(--card-bg); border: 1px solid var(--border-color); padding: 2rem; border-radius: 16px; position: relative; overflow: hidden; transition: transform 0.2s ease, border-color 0.2s ease;" class="changelog-card"> <div style="position: absolute; top: 0; left: 0; width: 4px; height: 100%; background: var(--dragon-red);"></div> <div style="display: flex; justify-content: space-between; align-items: baseline; margin-bottom: 1rem;"> <h3 style="color: var(--text-color); font-size: 1.5rem; font-weight: 800;">${log.title}</h3> <div style="text-align: right;"> <span style="color: var(--dragon-red); font-weight: 800; font-size: 1rem; display: block;">v${log.version}</span> </div> </div> <p style="font-size: 1rem; color: #94a3b8; margin-bottom: 1.5rem; line-height: 1.5; display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical; overflow: hidden;"> ${log.description} </p> <a${addAttribute(`/changelog/${log.version}`, "href")} class="btn-secondary" style="display: inline-flex; align-items: center; font-size: 0.9rem; font-weight: 600; color: var(--dragon-red); text-decoration: none; gap: 8px;">
View Full Changelog
<span style="font-size: 1.2rem;">→</span> </a> </div>`)} </div> </div> </div> </section> </div> ` })}`;
}, "/Users/taufeeqali/Projects/Draco/src/Draco.Web/src/pages/docs/index.astro", void 0);
const $$file = "/Users/taufeeqali/Projects/Draco/src/Draco.Web/src/pages/docs/index.astro";
const $$url = "/docs";

const _page = /*#__PURE__*/Object.freeze(/*#__PURE__*/Object.defineProperty({
  __proto__: null,
  default: $$Index,
  file: $$file,
  url: $$url
}, Symbol.toStringTag, { value: 'Module' }));

const page = () => _page;

export { page };
