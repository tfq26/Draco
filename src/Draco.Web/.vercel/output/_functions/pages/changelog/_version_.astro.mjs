import { c as createComponent, r as renderComponent, a as renderTemplate, b as createAstro, m as maybeRenderHead } from '../../chunks/astro/server_C1xCvP2A.mjs';
import 'piccolore';
import { $ as $$Layout } from '../../chunks/Layout_DhxFmJuB.mjs';
import postgres from 'postgres';
export { renderers } from '../../renderers.mjs';

const $$Astro = createAstro();
const $$version = createComponent(async ($$result, $$props, $$slots) => {
  const Astro2 = $$result.createAstro($$Astro, $$props, $$slots);
  Astro2.self = $$version;
  const { version } = Astro2.params;
  const DATABASE_URL = "postgresql://neondb_owner:npg_DMQ6XPnf8yCB@ep-small-field-aeynkd82-pooler.c-2.us-east-2.aws.neon.tech/neondb?sslmode=require&channel_binding=require";
  let release = null;
  {
    try {
      const sql = postgres(DATABASE_URL);
      const dbReleases = await sql`
      SELECT id, version, title, description, date 
      FROM releases 
      WHERE project_id = 6 AND version = ${version} AND published = true
      LIMIT 1
    `;
      if (dbReleases && dbReleases.length > 0) {
        const r = dbReleases[0];
        const dbSections = await sql`
        SELECT id, category, order_index
        FROM release_sections
        WHERE release_id = ${r.id}
        ORDER BY order_index ASC
      `;
        const sectionIds = dbSections.map((s) => s.id);
        let dbItems = [];
        if (sectionIds.length > 0) {
          dbItems = await sql`
            SELECT section_id, title, description, details, order_index
            FROM release_items
            WHERE section_id = ANY(${sectionIds})
            ORDER BY order_index ASC
          `;
        }
        release = {
          version: r.version,
          title: r.title,
          description: r.description,
          date: new Date(r.date).toLocaleDateString("en-US", { month: "long", day: "numeric", year: "numeric" }),
          sections: dbSections.map((s) => ({
            category: s.category,
            items: dbItems.filter((i) => i.section_id === s.id)
          }))
        };
      }
    } catch (e) {
      console.error("Failed to fetch changelog:", e);
    }
  }
  if (!release && version === "1.0.0") {
    release = {
      version: "1.0.0",
      title: "Project Draco Launch",
      date: "Feb 17, 2026",
      description: "The official launch of Draco: Autonomous Cloud Governance.",
      sections: [
        { category: "Architecture", items: [{ title: "Unified Configuration System", description: "Hierarchical config support.", details: ["env support"] }] }
      ]
    };
  }
  return renderTemplate`${renderComponent($$result, "Layout", $$Layout, { "title": release ? `Changelog v${release.version}` : "Changelog Not Found" }, { "default": async ($$result2) => renderTemplate` ${maybeRenderHead()}<div class="container"> <div style="margin-top: 4rem; margin-bottom: 2rem;"> <a href="/docs" style="color: var(--dragon-red); text-decoration: none; font-weight: 600; display: inline-flex; align-items: center; gap: 8px;"> <span>←</span> Back to Documentation
</a> </div> ${release ? renderTemplate`<section class="legal-content"> <div style="display: flex; justify-content: space-between; align-items: baseline; margin-bottom: 2rem; border-bottom: 1px solid var(--border-color); padding-bottom: 2rem;"> <div> <span style="color: var(--dragon-red); font-weight: 800; font-size: 1.2rem; text-transform: uppercase; letter-spacing: 2px;">Release v${release.version}</span> <h1 style="color: var(--text-color); margin-top: 0.5rem; border-bottom: none; display: block;">${release.title}</h1> </div> <span style="color: #64748b; font-weight: 500;">${release.date}</span> </div> <p style="font-size: 1.2rem; color: #cbd5e1; line-height: 1.8; margin-bottom: 4rem; max-width: 800px;"> ${release.description} </p> <div style="display: grid; gap: 4rem;"> ${release.sections.map((section) => renderTemplate`<div> <h2 style="color: var(--meteo-bronze); font-size: 1.2rem; text-transform: uppercase; letter-spacing: 1px; margin-bottom: 2rem; padding-left: 1.5rem; border-left: 4px solid var(--meteo-bronze); display: block;"> ${section.category} </h2> <div style="display: grid; gap: 3rem; padding-left: 1.5rem;"> ${section.items.map((item) => renderTemplate`<div> <h3 style="color: var(--text-color); font-size: 1.4rem; margin-bottom: 1rem; font-weight: 800;">${item.title}</h3> <p style="color: #94a3b8; font-size: 1.1rem; margin-bottom: 1.5rem; max-width: 700px;">${item.description}</p> ${item.details && renderTemplate`<ul style="list-style: none; padding: 0; display: grid; gap: 0.8rem;"> ${item.details.map((d) => renderTemplate`<li style="color: #64748b; font-size: 1rem; display: flex; align-items: start; background: rgba(255,255,255,0.02); padding: 1rem; border-radius: 8px; border: 1px solid rgba(255,255,255,0.05);"> <span style="color: var(--dragon-red); margin-right: 12px; font-weight: bold;">›</span> ${d} </li>`)} </ul>`} </div>`)} </div> </div>`)} </div> </section>` : renderTemplate`<div style="text-align: center; padding: 8rem 0;"> <h1 style="color: var(--text-color);">Changelog Not Found</h1> <p style="color: var(--sub-text-color);">The version you are looking for does not exist.</p> </div>`} </div> ` })}`;
}, "/Users/taufeeqali/Projects/Draco/src/Draco.Web/src/pages/changelog/[version].astro", void 0);
const $$file = "/Users/taufeeqali/Projects/Draco/src/Draco.Web/src/pages/changelog/[version].astro";
const $$url = "/changelog/[version]";

const _page = /*#__PURE__*/Object.freeze(/*#__PURE__*/Object.defineProperty({
  __proto__: null,
  default: $$version,
  file: $$file,
  url: $$url
}, Symbol.toStringTag, { value: 'Module' }));

const page = () => _page;

export { page };
