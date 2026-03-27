# Build Step
FROM node:20-alpine AS build
WORKDIR /app

# Copy package and lock files
COPY package*.json ./
COPY bun.lock ./

# Install dependencies (using npm since we are in a generic node image)
RUN npm install

# Copy source
COPY . .

# Build the project
RUN npm run build

# Runtime Step
FROM node:20-alpine AS runtime
WORKDIR /app

# Copy built app from the previous stage
COPY --from=build /app/dist ./dist
COPY --from=build /app/node_modules ./node_modules
COPY --from=build /app/package.json ./package.json

# Environment variables (Railway sets PORT automatically)
ENV PORT=4321
ENV HOST=0.0.0.0

# Command to run the Astro server (standalone node mode)
CMD ["node", "./dist/server/entry.mjs"]
