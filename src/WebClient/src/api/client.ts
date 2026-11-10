export interface SessionUser {
  id: string;
  login: string;
  fullName: string;
  roleCode: string;
  roleName: string;
  clientType?: string;
}

const TOKEN_KEY = 'stroyfin_jwt';
const USER_KEY = 'stroyfin_user';

export function getSavedSession(): { token: string | null; user: SessionUser | null } {
  const token = localStorage.getItem(TOKEN_KEY);
  const raw = localStorage.getItem(USER_KEY);
  try {
    return { token, user: raw ? JSON.parse(raw) : null };
  } catch {
    return { token: null, user: null };
  }
}

export function saveSession(token: string, user: SessionUser) {
  localStorage.setItem(TOKEN_KEY, token);
  localStorage.setItem(USER_KEY, JSON.stringify(user));
}

export function clearSession() {
  localStorage.removeItem(TOKEN_KEY);
  localStorage.removeItem(USER_KEY);
}

export class ApiError extends Error {
  status: number;
  code?: string;
  requiresTwoFactor?: boolean;
  constructor(message: string, status: number, code?: string, requiresTwoFactor?: boolean) {
    super(message);
    this.status = status;
    this.code = code;
    this.requiresTwoFactor = requiresTwoFactor;
  }
}

export async function apiFetch<T = any>(path: string, options: RequestInit = {}): Promise<T> {
  const { token } = getSavedSession();
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
    ...(options.headers as Record<string, string> | undefined),
  };
  if (token) headers.Authorization = `Bearer ${token}`;

  const res = await fetch(path, { ...options, headers });
  const text = await res.text();
  let data: any = null;
  try {
    data = text ? JSON.parse(text) : null;
  } catch {
    data = text;
  }

  if (!res.ok) {
    throw new ApiError(
      data?.error || `Ошибка сервера (${res.status})`,
      res.status,
      data?.code,
      Boolean(data?.requiresTwoFactor)
    );
  }
  return data as T;
}

export async function downloadFile(path: string, filename: string) {
  const { token } = getSavedSession();
  const res = await fetch(path, {
    headers: token ? { Authorization: `Bearer ${token}` } : {},
  });
  if (!res.ok) {
    throw new Error(`Не удалось скачать файл (${res.status})`);
  }
  const blob = await res.blob();
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  a.remove();
  URL.revokeObjectURL(url);
}
