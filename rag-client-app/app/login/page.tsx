'use client';

import { useState } from 'react';
import { useRouter } from 'next/navigation';
import { Dialog } from '@headlessui/react';
import { ArrowRightIcon } from '@heroicons/react/24/solid';

export default function LoginPage() {
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [isLoading, setIsLoading] = useState(false);
  const router = useRouter();

  async function handleLogin() {
    setIsLoading(true);
    const res = await fetch(`${process.env.AUTH_SERVICE_URL}/auth/login`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email, password }),
    });
    if (res.ok) {
      const data = await res.json();
      localStorage.setItem('token', data.access);
      router.push('/chat');
    }
    setIsLoading(false);
  }

  return (
    <Dialog open onClose={() => {}}>
      <div className="fixed inset-0 flex items-center justify-center bg-gray-100">
        <Dialog.Panel className="w-full max-w-sm rounded bg-white p-6 shadow">
          <Dialog.Title className="mb-4 text-lg font-medium">Login</Dialog.Title>
          <form
            className="space-y-4"
            onSubmit={e => {
              e.preventDefault();
              handleLogin();
            }}
          >
            <input
              type="email"
              value={email}
              onChange={e => setEmail(e.target.value)}
              className="w-full rounded border p-2"
              placeholder="Email"
            />
            <input
              type="password"
              value={password}
              onChange={e => setPassword(e.target.value)}
              className="w-full rounded border p-2"
              placeholder="Password"
            />
            <button
              type="submit"
              className="flex w-full items-center justify-center rounded bg-blue-600 py-2 text-white hover:bg-blue-700 disabled:opacity-50"
              disabled={isLoading}
            >
              {isLoading ? 'Loading...' : (
                <span className="flex items-center">
                  Login
                  <ArrowRightIcon className="ml-2 h-5 w-5" />
                </span>
              )}
            </button>
          </form>
        </Dialog.Panel>
      </div>
    </Dialog>
  );
}
