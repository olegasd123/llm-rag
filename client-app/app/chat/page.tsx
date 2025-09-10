'use client';

import { useEffect, useState } from 'react';
import { useFormState } from 'react-dom';
import { useRouter } from 'next/navigation';
import clsx from 'clsx';
import { PaperAirplaneIcon } from '@heroicons/react/24/solid';
import { startPromptAction, type StartResult } from '../actions/prompts';
import { pollPromptAction, type PollResult } from '../actions/prompts';
import { logoutAction } from '../actions/auth';

interface Message {
  role: 'user' | 'assistant';
  content: string;
}

export default function ChatPage() {
  const [messages, setMessages] = useState<Message[]>([]);
  const [input, setInput] = useState('');
  const [isSending, setIsSending] = useState(false);
  const router = useRouter();
  const [startState, startFormAction] = useFormState<StartResult, FormData>(startPromptAction, { ok: false });
  const [pollState, pollAction] = useFormState<PollResult, FormData>(pollPromptAction, { ok: false });
  const [pollId, setPollId] = useState<string | null>(null);

  async function handleSend() {
    if (!input.trim()) return;
    const prompt = input.trim();
    setMessages(m => [...m, { role: 'user', content: prompt }]);
    setInput('');
    setIsSending(true);
    // The form will submit via action={startFormAction}
  }

  // React to startState changes
  useEffect(() => {
    if (!startState) return;
    if (startState.unauthorized) {
      router.push('/login');
      setIsSending(false);
      return;
    }
    if (!startState.ok && startState.error) {
      setIsSending(false);
      return;
    }
    if (startState.ok && startState.taskId) {
      setPollId(startState.taskId);
    }
  }, [startState, router]);

  // Trigger initial poll when pollId appears
  useEffect(() => {
    if (!pollId) return;
    const fd = new FormData();
    fd.set('id', pollId);
    void pollAction(fd);
  }, [pollId, pollAction]);

  // Handle pollState updates and periodic retry
  useEffect(() => {
    if (!pollId) return;
    if (pollState.unauthorized) {
      router.push('/login');
      setIsSending(false);
      setPollId(null);
      return;
    }
    if (pollState.ok && pollState.response) {
      setMessages(m => [...m, { role: 'assistant', content: String(pollState.response) }]);
      setIsSending(false);
      setPollId(null);
      return;
    }
    const t = setTimeout(() => {
      const fd = new FormData();
      fd.set('id', pollId);
      void pollAction(fd);
    }, 1000);
    return () => clearTimeout(t);
  }, [pollState, pollId, pollAction, router]);

  return (
    <div className="flex h-screen max-h-screen flex-col">
      <div className="flex items-center justify-between border-b bg-white p-3">
        <h1 className="text-sm font-medium">Chat</h1>
        <form action={logoutAction}>
          <button
            type="submit"
            className="rounded bg-gray-200 px-3 py-1 text-sm hover:bg-gray-300"
          >
            Logout
          </button>
        </form>
      </div>
      <div className="flex-1 space-y-4 overflow-y-auto p-4">
        {messages.map((m, i) => (
          <div
            key={i}
            className={clsx(
              'max-w-xs rounded p-2',
              m.role === 'user' ? 'ml-auto bg-blue-600 text-white' : 'mr-auto bg-gray-200'
            )}
          >
            {m.content}
          </div>
        ))}
      </div>
      <form
        action={startFormAction}
        onSubmit={() => {
          // Enqueue the message before server action runs
          handleSend();
        }}
        className="flex items-center gap-2 border-t bg-white p-4"
      >
        <input
          value={input}
          onChange={e => setInput(e.target.value)}
          className="flex-1 rounded border p-2"
          placeholder="Type a message"
          name="prompt"
          autoComplete="off"
        />
        <button
          type="submit"
          className="rounded bg-green-600 p-2 text-white hover:bg-green-700 disabled:opacity-50"
          disabled={isSending}
          aria-label="Send"
        >
          <PaperAirplaneIcon className="h-5 w-5" />
        </button>
      </form>
    </div>
  );
}
