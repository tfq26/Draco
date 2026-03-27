import { drizzle } from 'drizzle-orm/postgres-js';
import postgres from 'postgres';
import { pgTable, serial, text, timestamp, integer, boolean } from 'drizzle-orm/pg-core';
import { eq, desc } from 'drizzle-orm';
import * as dotenv from 'dotenv';

dotenv.config();

const connectionString = process.env.DRACO_DB_RELEASE_CONNECTION || process.env.DATABASE_URL;

if (!connectionString) {
    console.error("No connection string found in environment variables.");
    process.exit(1);
}

// Define the releases table schema
const releases = pgTable('releases', {
    id: serial('id').primaryKey(),
    projectId: integer('project_id'),
    version: text('version').notNull(),
    title: text('title'),
    description: text('description'),
    date: timestamp('date'),
    isLatest: boolean('is_latest'),
    published: boolean('published'),
});

const client = postgres(connectionString);
const db = drizzle(client);

async function main() {
    console.log("Querying latest releases from the Release Connection...");

    try {
        const latestReleases = await db.select()
            .from(releases)
            .where(eq(releases.projectId, 6)) // Draco project ID
            .orderBy(desc(releases.date))
            .limit(5);

        console.log("\n--- Latest Releases from Release DB ---");
        if (latestReleases.length === 0) {
            console.log("No releases found.");
        } else {
            latestReleases.forEach((r) => {
                console.log(`v${r.version}: ${r.title} (${r.date ? new Date(r.date).toLocaleDateString() : 'N/A'})`);
            });
        }
    } catch (error) {
        console.error("Query failed:", error.message);
    } finally {
        process.exit(0);
    }
}

main().catch((err) => {
    console.error(err);
    process.exit(1);
});
