# Dockerfile for rag-client-app
FROM node:18-alpine AS deps
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm install

FROM node:18-alpine AS build
WORKDIR /app
COPY --from=deps /app/node_modules ./node_modules
COPY . .
ARG AUTH_SERVICE_URL
ARG WEB_SERVICE_URL
ENV AUTH_SERVICE_URL=$AUTH_SERVICE_URL
ENV WEB_SERVICE_URL=$WEB_SERVICE_URL
RUN npm run build

FROM node:18-alpine AS runner
WORKDIR /app
ENV NODE_ENV=production
ENV AUTH_SERVICE_URL=$AUTH_SERVICE_URL
ENV WEB_SERVICE_URL=$WEB_SERVICE_URL
COPY --from=build /app/next.config.js ./next.config.js
COPY --from=build /app/package.json ./package.json
COPY --from=build /app/node_modules ./node_modules
COPY --from=build /app/.next ./.next
COPY --from=build /app/public ./public
EXPOSE 3000
CMD ["npm", "run", "start"]
