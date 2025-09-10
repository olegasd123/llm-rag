# llm-rag

Store a custom knowledge as vector embeddings

### Setup

1. Bring up the stack and verify

```bash
# Base + dev overrides
docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d
docker network ls | grep rag-net     # verify the network exists
docker compose ps
```

2. Confirm container-to-container name resolution

```bash
docker compose exec main-db ping data-cache
```

## Services

- **AuthService**: .NET 8 service providing JWT-based authentication.
- **WebService**: .NET 8 API for prompt submission and result retrieval.
- **BackgroundWorker**: .NET 8 worker processing queued prompts.
- **client-app**: Next.js interface for authentication and chat.

### Setup

1. Copy `.env.example` to `.env` and fill in the required values.
   - Generate a strong `JWT_SECRET`:
     ```bash
     openssl rand -base64 32
     ```
2. Start the database and create the `users` and `refresh_tokens` tables:

   ```bash
   docker compose up -d main-db
   docker compose exec main-db psql -U rag_user -d rag -e PGPASSWORD=<you can take it from .env>
   ```

   ```sql
    -- Enable UUID generation (PostgreSQL 16)
    CREATE EXTENSION IF NOT EXISTS pgcrypto;

    CREATE TABLE users (
        id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
        email TEXT NOT NULL UNIQUE,
        password_hash TEXT NOT NULL
    );

    -- Per-user context used by the worker to augment prompts
    CREATE TABLE IF NOT EXISTS user_data (
        user_id UUID PRIMARY KEY REFERENCES users(id) ON DELETE CASCADE,
        data TEXT NOT NULL,
        updated_at TIMESTAMPTZ DEFAULT NOW()
    );

    CREATE TABLE IF NOT EXISTS responses (
        task_id UUID PRIMARY KEY,
        prompt   TEXT NOT NULL,
        response TEXT NOT NULL,
        created_at TIMESTAMPTZ DEFAULT NOW()
    );

    -- Server-side refresh token storage (multiple per user, token stores SHA-256 hash)
    CREATE TABLE IF NOT EXISTS refresh_tokens (
        user_id    UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
        token      TEXT NOT NULL,  -- SHA-256 hex
        expires_at TIMESTAMPTZ NOT NULL,
        created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
        rotated_at TIMESTAMPTZ,
        PRIMARY KEY (user_id, token)
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
5. (Optional) Save per-user context for RAG augmentation via the web service:
   - PUT `WebService` `/api/v1/user-data` with body `{ "data": "your notes or profile context" }` using the access token.
   - GET `/api/v1/user-data` to read it back.
4. Build and run the auth service:
   ```bash
   docker compose build AuthService
   docker compose up -d AuthService
   ```

## Auth Flow Notes

- Login returns only an access token and `expiresIn`. The refresh token is stored only on the server in `refresh_tokens`.
- Refresh: POST `AuthService` `/auth/refresh` with body `{ "token": "<access token>" }` (access token can be expired). Service validates the server-stored refresh token, rotates it, and returns a new access token.
- Logout: POST `AuthService` `/auth/logout` with body `{ "token": "<access token>" }`. Service deletes the user’s entry in `refresh_tokens`.

## Running Dev

```bash
export ASPNETCORE_ENVIRONMENT=Development
export JWT_SECRET="<some strong secret>"
export MESSAGE_BROKER_URL="amqp://user:pass@host:5672"  # Use AMQP 5672 (not 15672)
dotnet run
```

## Compose Files

- docker-compose.yml: shared defaults (images, networks, env, healthchecks, restart policies)
- docker-compose.dev.yml: dev-only overrides (ports, dev env)
- docker-compose.prod.yml: prod-only overrides (resource limits, read-only FS, ports only for public services)

## Running

Set the required environment variables in `.env` and start either dev or prod:

```bash
# Development (binds dev ports, enables dev env for .NET services)
docker compose -f docker-compose.yml -f docker-compose.dev.yml up --build -d

# Production-like (no DB/broker/cache host ports, resource limits/read-only)
docker compose -f docker-compose.yml -f docker-compose.prod.yml up --build -d
```

Tips:
- To switch, stop current stack first: `docker compose down` and bring up with the other override file.
- You can also add `-f docker-compose.dev.yml -f docker-compose.prod.yml` together for mixed overrides if useful.

### Troubleshooting

- RabbitMQ connection failed (MassTransit shows `rabbitmq://...:15672/`):
  - 15672 is the management UI port. Set `MESSAGE_BROKER_URL` to the AMQP port 5672, e.g. `amqp://guest:guest@message-broker:5672`.
  - No host port mapping for 5672 is required when services run inside Docker; they reach `message-broker:5672` on the internal network.
  - The management UI is available on `http://localhost:15672` (guest/guest by default).
- Worker faulted on prompt with 404 from vector DB or AI host:
  - Qdrant doesn't serve `/search`; current code now falls back if unavailable. If you want real vector search, set up a collection and call Qdrant's `/collections/{name}/points/search`.
  - If your AI host runs on your laptop (not in Docker), set `AI_HOST_URL` to `http://host.docker.internal:1234` so containers can reach it. `http://localhost:1234` resolves inside the container and will fail.
  - The worker calls the OpenAI-compatible endpoint `/v1/completions`. Set `AI_HOST_LM_NAME` to a valid model id if required by your AI host. For LM Studio, any string typically works and the loaded model is used; `lmstudio` is the default.
