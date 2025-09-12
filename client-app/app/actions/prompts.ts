"use server";

import { cookies } from 'next/headers';

interface TokenResponse {
  accessToken: string;
  accessExpiresIn: number;
  refreshToken: string;
  refreshExpiresIn: number;
  sessionKey: string;
}

export interface StartResult {
  ok: boolean;
  unauthorized?: boolean;
  taskId?: string;
  error?: string;
}

export interface PollResult {
  ok: boolean;
  unauthorized?: boolean;
  // Echoes the task id that was polled to disambiguate concurrent polls
  id?: string;
  response?: string;
  error?: string;
}

async function refreshTokens(): Promise<boolean> {
  const jar = cookies();
  const refresh = jar.get('refresh_token')?.value;
  const sessionKey = jar.get('session_key')?.value;
  if (!refresh || !sessionKey) return false;

  const res = await fetch(`${process.env.AUTH_SERVICE_URL}/auth/refresh`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ RefreshToken: refresh, SessionKey: sessionKey }),
    cache: 'no-store',
  });
  if (!res.ok) {
    jar.set('access_token', '', { path: '/', maxAge: 0 });
    jar.set('refresh_token', '', { path: '/', maxAge: 0 });
    return false;
  }
  const { accessToken, accessExpiresIn, refreshToken, refreshExpiresIn, sessionKey: newSessionKey } = (await res.json()) as TokenResponse;

  jar.set('access_token', accessToken, {
    httpOnly: true,
    secure: process.env.NODE_ENV === 'production',
    sameSite: 'lax',
    path: '/',
    maxAge: accessExpiresIn,
  });
  jar.set('refresh_token', refreshToken, {
    httpOnly: true,
    secure: process.env.NODE_ENV === 'production',
    sameSite: 'lax',
    path: '/',
    maxAge: refreshExpiresIn,
  });
  jar.set('session_key', newSessionKey, {
    httpOnly: true,
    secure: process.env.NODE_ENV === 'production',
    sameSite: 'lax',
    path: '/',
    maxAge: refreshExpiresIn,
  });
  return true;
}

export async function startPromptAction(
  _prev: StartResult,
  formData: FormData
): Promise<StartResult> {
  const prompt = String(formData.get('prompt') || '');
  if (!prompt) return { ok: false, error: 'Prompt is required' };

  const jar = cookies();
  const access = jar.get('access_token')?.value;

  const forward = async (token?: string) =>
    fetch(`${process.env.WEB_SERVICE_URL}/api/v1/prompts`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
      },
      body: JSON.stringify({ prompt }),
      cache: 'no-store',
    });

  try {
    let res = await forward(access);
    if (res.status === 401) {
      const refreshed = await refreshTokens();
      if (!refreshed) return { ok: false, unauthorized: true };
      const token = cookies().get('access_token')?.value;
      res = await forward(token);
    }
    if (!res.ok) return { ok: false, error: 'Failed to start' };
    const data = await res.json();
    return { ok: true, taskId: data.taskId };
  } catch {
    return { ok: false, error: 'Service unavailable' };
  }
}

export async function pollPromptAction(
  _prev: PollResult,
  formData: FormData
): Promise<PollResult> {
  const id = String(formData.get('id') || '');
  if (!id) return { ok: false, error: 'Id is required' };

  const jar = cookies();
  const access = jar.get('access_token')?.value;

  const forward = async (token?: string) =>
    fetch(`${process.env.WEB_SERVICE_URL}/api/v1/prompts/${id}`, {
      headers: {
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
      },
      cache: 'no-store',
    });

  try {
    let res = await forward(access);
    if (res.status === 401) {
      const refreshed = await refreshTokens();
      if (!refreshed) return { ok: false, unauthorized: true, id };
      const token = cookies().get('access_token')?.value;
      res = await forward(token);
    }
    if (!res.ok) return { ok: false, error: 'Failed to fetch', id };
    const data = await res.json();
    return { ok: true, id, response: data.response };
  } catch {
    return { ok: false, error: 'Service unavailable', id };
  }
}
