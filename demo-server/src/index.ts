import path from 'node:path';
import { fileURLToPath } from 'node:url';
import fs from 'node:fs';
import express from 'express';
import { createDatabase } from './db/database.js';
import { createApp } from './app.js';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const dbPath = process.env.DB_PATH || path.resolve(__dirname, '../data/finanve.db');
const port = Number(process.env.PORT || 5050);

const db = createDatabase(dbPath);
const app = createApp(db);

// Если собран production-бандл WebClient — раздаём его как SPA-статику
const distPath = path.resolve(__dirname, '../../src/WebClient/dist');
if (fs.existsSync(distPath)) {
  app.use(express.static(distPath));
  app.get('*', (req, res, next) => {
    if (req.path.startsWith('/api')) return next();
    res.sendFile(path.join(distPath, 'index.html'));
  });
}

app.listen(port, '0.0.0.0', () => {
  console.log(`FinanveManager Demo API listening on http://0.0.0.0:${port}`);
});
