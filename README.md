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
    CREATE TABLE users (
        id SERIAL PRIMARY KEY,
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
export MESSAGE_BROKER_URL="amqp://user:pass@host:port"
dotnet run
```

## Running

Set the required environment variables in `.env` and start the stack:

```
docker-compose up --build
```
