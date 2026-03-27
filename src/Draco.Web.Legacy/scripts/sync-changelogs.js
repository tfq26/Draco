const postgres = require('postgres');
const fs = require('fs');
const path = require('path');

const DATABASE_URL = process.env.DATABASE_URL;
const PROJECT_ID = 6; // Draco Project ID

if (!DATABASE_URL) {
    console.error('Error: DATABASE_URL environment variable is not set.');
    process.exit(1);
}

const sql = postgres(DATABASE_URL);

async function syncChangelogs() {
    const changelogDir = path.join(__dirname, '../public/changelogs');
    const files = fs.readdirSync(changelogDir).filter(f => f.endsWith('.json'));

    console.log(`Found ${files.length} changelog files. Syncing to project ${PROJECT_ID}...`);

    for (const file of files) {
        const filePath = path.join(changelogDir, file);
        const data = JSON.parse(fs.readFileSync(filePath, 'utf8'));

        const { version, title, description, releaseDate, highlights, sections } = data;

        // Check if release exists
        const [existing] = await sql`SELECT id FROM releases WHERE project_id = ${PROJECT_ID} AND version = ${version}`;

        if (existing) {
            console.log(`Skipping v${version} - already exists in database.`);
            continue;
        }

        console.log(`Pushing v${version}: ${title}...`);

        await sql.begin(async (sql) => {
            // 1. Insert Release
            const [release] = await sql`
        INSERT INTO releases (project_id, version, title, description, date, is_latest, published)
        VALUES (${PROJECT_ID}, ${version}, ${title}, ${description}, ${releaseDate || new Date()}, false, true)
        RETURNING id
      `;

            // 2. Insert Sections & Items
            if (sections) {
                for (let sIdx = 0; sIdx < sections.length; sIdx++) {
                    const section = sections[sIdx];
                    const [dbSection] = await sql`
            INSERT INTO release_sections (release_id, category, order_index)
            VALUES (${release.id}, ${section.category}, ${sIdx})
            RETURNING id
          `;

                    if (section.items) {
                        for (let iIdx = 0; iIdx < section.items.length; iIdx++) {
                            const item = section.items[iIdx];
                            await sql`
                INSERT INTO release_items (section_id, title, description, details, order_index)
                VALUES (${dbSection.id}, ${item.title}, ${item.description}, ${item.details || []}, ${iIdx})
              `;
                        }
                    }
                }
            }

            console.log(`Successfully synced v${version}.`);
        });
    }

    console.log('Sync complete.');
    process.exit(0);
}

syncChangelogs().catch(err => {
    console.error('Sync failed:', err);
    process.exit(1);
});
