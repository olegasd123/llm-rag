"use client";

import { useState } from 'react';
import { useFormState, useFormStatus } from 'react-dom';
import { Dialog } from '@headlessui/react';
import { ArrowRightIcon } from '@heroicons/react/24/solid';
import { loginAction, type AuthState } from '../actions/auth';

function SubmitButton() {
  const { pending } = useFormStatus();
  return (
    <button
      type="submit"
      className="flex w-full items-center justify-center rounded bg-blue-600 py-2 text-white hover:bg-blue-700 disabled:opacity-50"
      disabled={pending}
    >
      {pending ? 'Loading...' : (
        <span className="flex items-center">
          Login
          <ArrowRightIcon className="ml-2 h-5 w-5" />
        </span>
      )}
    </button>
  );
}

export function LoginForm() {
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [state, formAction] = useFormState<AuthState, FormData>(loginAction, {} as AuthState);

  return (
    <Dialog open onClose={() => {}}>
      <div className="fixed inset-0 flex items-center justify-center bg-gray-100">
        <Dialog.Panel className="w-full max-w-sm rounded bg-white p-6 shadow">
          <Dialog.Title className="mb-4 text-lg font-medium">Login</Dialog.Title>
          {state?.error && (
            <p className="mb-2 rounded border border-red-300 bg-red-50 p-2 text-sm text-red-700">{state.error}</p>
          )}
          <form className="space-y-4" action={formAction}>
            <input
              type="email"
              value={email}
              onChange={e => setEmail(e.target.value)}
              className="w-full rounded border p-2"
              placeholder="Email"
              autoComplete="email"
              name="email"
              required
            />
            <input
              type="password"
              value={password}
              onChange={e => setPassword(e.target.value)}
              className="w-full rounded border p-2"
              placeholder="Password"
              autoComplete="current-password"
              name="password"
              required
            />
            <SubmitButton />
          </form>
        </Dialog.Panel>
      </div>
    </Dialog>
  );
}
