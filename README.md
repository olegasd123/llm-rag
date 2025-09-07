# llm-rag

Store a custom knowledge as vector embeddings

### Setup

1. Bring up the stack and verify

```bash
docker compose up -d
docker network ls | grep rag-net     # verify the network exists
docker compose ps
```

2. Confirm container-to-container name resolution

```bash
docker compose exec rag-main-db ping rag-data-cache
```

## Services

- **rag-auth-service**: .NET 8 service providing JWT-based authentication.
- **rag-web-service**: .NET 8 API for prompt submission and result retrieval.
- **rag-background-worker**: .NET 8 worker processing queued prompts.
- **rag-client-app**: Next.js interface for authentication and chat.

### Setup

1. Copy `.env.example` to `.env` and fill in the required values.
   - Generate a strong `JWT_SECRET`:
     ```bash
     openssl rand -base64 32
     ```
2. Start the database and create the `users` table:

   ```bash
   docker compose up -d rag-main-db
   docker compose exec rag-main-db psql -U rag_user -d rag -e PGPASSWORD=<you can take it from .env>
   ```

   ```sql
    -- Enable UUID generation (PostgreSQL 16)
    CREATE EXTENSION IF NOT EXISTS pgcrypto;

    CREATE TABLE users (
        id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
        email TEXT NOT NULL UNIQUE,
        password_hash TEXT NOT NULL
    );

    CREATE TABLE IF NOT EXISTS user_data (
        id SERIAL PRIMARY KEY,
        data TEXT NOT NULL
    );

    CREATE TABLE IF NOT EXISTS responses (
        task_id UUID PRIMARY KEY,
        prompt   TEXT NOT NULL,
        response TEXT NOT NULL,
        created_at TIMESTAMPTZ DEFAULT NOW()
    );
   ```

   ```psql
   \dt public.users
   ```

   ```psql
   \d tablename
   ```

3. Insert a user user@example.com with password 'user123', or generate the password hash with your preferred BCrypt tool, then run:
   ```sql
   INSERT INTO users (email, password_hash)
   VALUES ('user@example.com', '$2a$12$XUaL3gaf3JePnNPZf20vf.oWqSBcLnvB.HQgPb1mgu4I2rkbKN6.K');
   ```
4. Build and run the auth service:
   ```bash
   docker compose build rag-auth-service
   docker compose up -d rag-auth-service
   ```

## Running Dev

```bash
export ASPNETCORE_ENVIRONMENT=Development
export JWT_SECRET="<some strong secret>"
export MESSAGE_BROKER_URL="amqp://user:pass@host:5672"  # Use AMQP 5672 (not 15672)
dotnet run
```

## Running

Set the required environment variables in `.env` and start the stack:

```
docker-compose up --build
```

### Troubleshooting

- RabbitMQ connection failed (MassTransit shows `rabbitmq://...:15672/`):
  - 15672 is the management UI port. Set `MESSAGE_BROKER_URL` to the AMQP port 5672, e.g. `amqp://guest:guest@rag-message-broker:5672`.
  - No host port mapping for 5672 is required when services run inside Docker; they reach `rag-message-broker:5672` on the internal network.
  - The management UI is available on `http://localhost:15672` (guest/guest by default).
- Worker faulted on prompt with 404 from vector DB or AI host:
  - Qdrant doesn't serve `/search`; current code now falls back if unavailable. If you want real vector search, set up a collection and call Qdrant's `/collections/{name}/points/search`.
  - If your AI host runs on your laptop (not in Docker), set `AI_HOST_URL` to `http://host.docker.internal:1234` so containers can reach it. `http://localhost:1234` resolves inside the container and will fail.
  - The worker calls the OpenAI-compatible endpoint `/v1/completions`. Set `AI_MODEL` to a valid model id if required by your AI host. For LM Studio, any string typically works and the loaded model is used; `lmstudio` is the default.
