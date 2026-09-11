FROM node:22-bookworm-slim
WORKDIR /app
COPY package.json ./
RUN npm install --omit=dev --ignore-scripts --no-audit --no-fund
COPY server.mjs ./server.mjs
COPY public ./public
COPY test ./test
RUN npm test
ENV NODE_ENV=production
USER node
CMD ["npm","start"]
