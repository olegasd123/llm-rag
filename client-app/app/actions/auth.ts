"use server";

import { cookies } from 'next/headers';
import { redirect } from 'next/navigation';

interface TokenResponse {
  accessToken: string;
  accessExpiresIn: number;
  refreshToken: string;
  refreshExpiresIn: number;
  sessionKey: string;
}

export interface AuthState {
  error?: string;
}

export async function loginAction(_: AuthState, formData: FormData): Promise<AuthState> {
  const email = String(formData.get('email') || '');
  const password = String(formData.get('password') || '');
  const jar = cookies();
  const sessionKey = jar.get('session_key')?.value;
  if (!email || !password) return { error: 'Email and password are required' };
  let data: TokenResponse | null = null;
  try {
    const res = await fetch(`${process.env.AUTH_SERVICE_URL}/auth/login`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ Email: email, Password: password, SessionKey: sessionKey }),
      cache: 'no-store',
    });
    if (!res.ok) return { error: 'Invalid credentials' };
    data = (await res.json()) as TokenResponse;
  } catch (e) {
    return { error: 'Unable to reach Auth service' };
  }

  if (!data) return { error: 'Unexpected auth response' };
  const { accessToken, accessExpiresIn, refreshToken, refreshExpiresIn, sessionKey: newSessionKey } = data;
  
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

  redirect('/chat');
}

export async function logoutAction(): Promise<void> {
  const jar = cookies();
  const token = jar.get('refresh_token')?.value;
  if (token) {
    await fetch(`${process.env.AUTH_SERVICE_URL}/auth/logout-device`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ refreshToken: token }),
      cache: 'no-store',
    }).catch(() => {});
  }
  jar.set('access_token', '', { path: '/', maxAge: 0 });
  jar.set('refresh_token', '', { path: '/', maxAge: 0 });
  jar.set('session_key', '', { path: '/', maxAge: 0 });
  redirect('/login');
}
