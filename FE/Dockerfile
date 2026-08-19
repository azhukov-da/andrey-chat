FROM node:22-alpine AS build
WORKDIR /app
COPY package*.json ./
# node:22-alpine bundles npm 10, which resolves the lock file's optional peer
# dependencies differently from the npm 11 that writes it and rejects the lock
# as out of sync. Match the version used for local development.
RUN npm install -g npm@11 && npm ci
COPY . .
RUN npm run build

FROM nginx:alpine AS final
COPY --from=build /app/dist /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 80
