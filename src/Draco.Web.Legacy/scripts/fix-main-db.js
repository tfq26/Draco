import postgres from 'postgres';
import * as dotenv from 'dotenv';
import path from 'path';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

dotenv.config({ path: path.join(__dirname, '../.env') });

const connectionString = process.env.DRACO_DB_MAIN_CONNECTION;

if (!connectionString) {
    console.error("No DRACO_DB_MAIN_CONNECTION found in environment variables.");
    process.exit(1);
}

const sql = postgres(connectionString);

async function main() {
    console.log("🛠️ Fixing Main Database Schema...");

    try {
        console.log("- Creating 'UserAccounts' table...");
        await sql.unsafe(`
            CREATE TABLE IF NOT EXISTS "UserAccounts" (
                "Phone" TEXT PRIMARY KEY,
                "Name" TEXT NOT NULL,
                "Email" TEXT,
                "AuthId" TEXT,
                "CreatedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                "LastSeenAt" TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            DO $$ 
            BEGIN 
                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='UserAccounts' AND column_name='AuthId') THEN
                    ALTER TABLE "UserAccounts" ADD COLUMN "AuthId" TEXT;
                END IF;
                IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='UserAccounts' AND column_name='Email') THEN
                    ALTER TABLE "UserAccounts" ADD COLUMN "Email" TEXT;
                END IF;
            END $$;
        `);

        console.log("- Creating 'CloudConnections' table...");
        await sql.unsafe(`
            CREATE TABLE IF NOT EXISTS "CloudConnections" (
                "Id" SERIAL PRIMARY KEY,
                "UserPhone" TEXT NOT NULL REFERENCES "UserAccounts"("Phone"),
                "Provider" TEXT NOT NULL,
                "SubscriptionId" TEXT NOT NULL,
                "AccessToken" TEXT,
                "RefreshToken" TEXT,
                "TokenExpiresAt" TIMESTAMPTZ,
                "IsActive" BOOLEAN NOT NULL DEFAULT TRUE,
                "ConnectedAt" TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
        `);

        console.log("- Creating 'PulseReportSchedules' table...");
        await sql.unsafe(`
            CREATE TABLE IF NOT EXISTS "PulseReportSchedules" (
                "Id" SERIAL PRIMARY KEY,
                "UserPhone" TEXT NOT NULL REFERENCES "UserAccounts"("Phone"),
                "Frequency" TEXT NOT NULL,
                "IncludeCostAnalysis" BOOLEAN NOT NULL,
                "IncludeSecurityHealth" BOOLEAN NOT NULL,
                "LastSentAt" TIMESTAMPTZ,
                "NextRunAt" TIMESTAMPTZ NOT NULL,
                "IsActive" BOOLEAN NOT NULL
            );
        `);

        console.log("✅ Main Database Schema fixed successfully.");
    } catch (error) {
        console.error("❌ Schema fix failed:", error.message);
    } finally {
        await sql.end();
        process.exit(0);
    }
}

main().catch((err) => {
    console.error(err);
    process.exit(1);
});
