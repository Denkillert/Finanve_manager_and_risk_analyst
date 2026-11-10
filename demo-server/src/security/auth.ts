import crypto from 'node:crypto';
import type { Request, Response, NextFunction } from 'express';

const JWT_SECRET = process.env.JWT_SECRET || 'stroyfinrisk-dev-secret-key-2026-min-32-chars';

export interface AuthUser {
  id: string;
  login: string;
  fullName: string;
  roleCode: string;
  roleName: string;
}

export interface AuthedRequest extends Request {
  user?: AuthUser;
}

export function hashPassword(password: string): string {
  if (password.length < 12) {
    throw new Error('Пароль должен содержать не менее 12 символов (НФТ-7).');
  }
  const salt = crypto.randomBytes(16).toString('hex');
  const hash = crypto.pbkdf2Sync(password, salt, 10000, 32, 'sha256').toString('hex');
  return `${salt}:${hash}`;
}

export function verifyPassword(password: string, stored: string): boolean {
  const [salt, expected] = stored.split(':');
  if (!salt || !expected) return false;
  const actual = crypto.pbkdf2Sync(password, salt, 10000, 32, 'sha256').toString('hex');
  const aBuf = Buffer.from(actual, 'hex');
  const eBuf = Buffer.from(expected, 'hex');
  return aBuf.length === eBuf.length && crypto.timingSafeEqual(aBuf, eBuf);
}

export function signJwt(payload: AuthUser): string {
  const header = Buffer.from(JSON.stringify({ alg: 'HS256', typ: 'JWT' })).toString('base64url');
  const body = Buffer.from(
    JSON.stringify({
      ...payload,
      iat: Math.floor(Date.now() / 1000),
      exp: Math.floor(Date.now() / 1000) + 8 * 3600,
    })
  ).toString('base64url');
  const sig = crypto.createHmac('sha256', JWT_SECRET).update(`${header}.${body}`).digest('base64url');
  return `${header}.${body}.${sig}`;
}

export function verifyJwt(token: string): AuthUser | null {
  try {
    const parts = token.split('.');
    if (parts.length !== 3) return null;
    const [header, body, sig] = parts;
    const expected = crypto.createHmac('sha256', JWT_SECRET).update(`${header}.${body}`).digest('base64url');
    if (sig !== expected) return null;
    const parsed = JSON.parse(Buffer.from(body, 'base64url').toString('utf8'));
    if (parsed.exp && parsed.exp < Math.floor(Date.now() / 1000)) return null;
    return {
      id: parsed.id,
      login: parsed.login,
      fullName: parsed.fullName,
      roleCode: parsed.roleCode,
      roleName: parsed.roleName,
    };
  } catch {
    return null;
  }
}

export function requireAuth(req: AuthedRequest, res: Response, next: NextFunction) {
  const header = req.headers.authorization;
  const token = header?.startsWith('Bearer ') ? header.slice(7) : (req.query.token as string | undefined);
  if (!token) {
    res.status(401).json({ error: 'Требуется аутентификация.' });
    return;
  }
  const user = verifyJwt(token);
  if (!user) {
    res.status(401).json({ error: 'Сессия истекла или токен недействителен.' });
    return;
  }
  req.user = user;
  next();
}

export function requireRoles(...allowedRoles: string[]) {
  return (req: AuthedRequest, res: Response, next: NextFunction) => {
    if (!req.user) {
      res.status(401).json({ error: 'Требуется аутентификация.' });
      return;
    }
    if (!allowedRoles.includes(req.user.roleCode)) {
      res.status(403).json({
        error: `Недостаточно прав для роли «${req.user.roleName}».`,
        requiredRoles: allowedRoles,
      });
      return;
    }
    next();
  };
}
