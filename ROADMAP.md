# Project Roadmap

This roadmap outlines steps to implement the Retrieval-Augmented Generation platform described in `.cursor/rules/architecture.mdc`.

## Phase 1: Infrastructure Setup
- Containerize all core services with Docker Compose for local development.
- Provision foundational components:
  - Main Database, PostgresSQL (`rag-main-db`)
  - Data Cache, Redis (`rag-data-cache`)
  - Message Broker, RabbitMQ (`rag-message-broker`)
  - Vector Database, Qdrant (`rag-vector-db`)
  - AI Host, LM Studio runs locally
- Define shared network and environment variables:
  - `WEB_SERVICE_URL`
  - `DATA_CACHE_URL`
  - `MESSAGE_BROKER_URL`
  - `VECTOR_DB_URL`
  - `AI_HOST_URL`
  - `MAIN_DB_URL`
  - `AUTH_SERVICE_URL`
  - `MONITORING_URL`

## Phase 2: Auth Service
- Implement the Auth Service in the `rag-auth-service` container using .NET 8.
- Provide login endpoints that verify credentials against PostgreSQL and issue JWTs.
- Support token introspection and refresh tokens.

## Phase 3: Web Service
- Develop the API server in the `rag-web-service` container using .NET 8.
- Expose REST endpoints for prompt submission and result retrieval.
- Read connection settings from environment variables and enqueue tasks to RabbitMQ via MassTransit.
- Validate JWTs from the auth service before accepting requests.

## Phase 4: Background Worker
- Create a .NET worker running in the `rag-background-worker` container.
- Consume jobs from RabbitMQ and orchestrate retrieval and generation:
  - Check Redis for cached vectors and user data.
  - Query Qdrant and PostgreSQL on cache misses.
  - Forward augmented prompts to the AI Host.
- Store responses in PostgreSQL and cache them in Redis.

## Phase 5: Client App
- Build a ChatGPT-style interface in the `rag-client-app` container.
- Authenticate via the Auth Service and send prompts to the Web Service.
- Poll for task status using the returned task identifier.

## Phase 6: Monitoring Stack
- Deploy Prometheus and Grafana in the `rag-monitoring` container.
- Collect metrics, logs, and traces from all services via OpenTelemetry.
- Configure alerts for system health.

## Phase 7: Deployment & Scaling
- Ensure all services emit structured logs and propagate trace IDs.
- Use Kubernetes manifests for production to scale services independently.
- Add load balancing for the Web Service and Client App.
- Encrypt sensitive data at rest and in transit.