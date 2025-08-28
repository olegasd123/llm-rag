'use client';

import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import clsx from 'clsx';
import { PaperAirplaneIcon } from '@heroicons/react/24/solid';

interface Message {
  role: 'user' | 'assistant';
  content: string;
}

export default function ChatPage() {
  const [messages, setMessages] = useState<Message[]>([]);
  const [input, setInput] = useState('');
  const [token, setToken] = useState('');
  const [isSending, setIsSending] = useState(false);
  const router = useRouter();

  useEffect(() => {
    const t = localStorage.getItem('token');
    if (!t) {
      router.push('/login');
    } else {
      setToken(t);
    }
  }, [router]);

  async function handleSend() {
    if (!input.trim()) return;
    const prompt = input.trim();
    setMessages(m => [...m, { role: 'user', content: prompt }]);
    setInput('');
    setIsSending(true);
    const res = await fetch(`${process.env.WEB_SERVICE_URL}/prompts`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        Authorization: `Bearer ${token}`,
      },
      body: JSON.stringify({ prompt }),
    });
    if (res.ok) {
      const { taskId } = await res.json();
      await poll(taskId);
    }
    setIsSending(false);
  }

  async function poll(id: string) {
    while (true) {
      const res = await fetch(`${process.env.WEB_SERVICE_URL}/prompts/${id}`, {
        headers: { Authorization: `Bearer ${token}` },
      });
      if (res.ok) {
        const data = await res.json();
        setMessages(m => [...m, { role: 'assistant', content: data.response }]);
        break;
      }
      await new Promise(r => setTimeout(r, 1000));
    }
  }

  return (
    <div className="flex h-screen max-h-screen flex-col">
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
        onSubmit={e => {
          e.preventDefault();
          handleSend();
        }}
        className="flex items-center gap-2 border-t bg-white p-4"
      >
        <input
          value={input}
          onChange={e => setInput(e.target.value)}
          className="flex-1 rounded border p-2"
          placeholder="Type a message"
        />
        <button
          type="submit"
          className="rounded bg-green-600 p-2 text-white hover:bg-green-700 disabled:opacity-50"
          disabled={isSending}
        >
          <PaperAirplaneIcon className="h-5 w-5" />
        </button>
      </form>
    </div>
  );
}
