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

### Setup
1. Copy `.env.example` to `.env` and fill in the required values.
   - Generate a strong `JWT_SECRET`:
     ```bash
     openssl rand -base64 32
     ```
2. Start the database and create the `users` table:
   ```bash
   docker compose up -d rag-main-db
   docker compose exec rag-main-db psql -U rag_user -d rag
   ```
   ```sql
   CREATE TABLE users (
       id SERIAL PRIMARY KEY,
       email TEXT NOT NULL UNIQUE,
       password_hash TEXT NOT NULL
   );
   ```
3. Build and run the auth service:
   ```bash
   docker compose build rag-auth-service
   docker compose up -d rag-auth-service
   ```
4. Insert a user (generate the password hash with your preferred BCrypt tool):
   ```sql
   INSERT INTO users (email, password_hash)
   VALUES ('alice@example.com', '$2b$12$...bcrypt_hash_here...');
   ```

## Running
Set the required environment variables in `.env` and start the stack:

```
docker-compose up --build
```
