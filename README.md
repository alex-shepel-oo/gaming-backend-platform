# Gaming Backend Platform

This is a portfolio project designed to demonstrate practical experience in building modern backend systems using a microservices architecture. It showcases backend development, frontend integration, infrastructure design, deployment, and the operation of distributed applications in a production-like environment.

The project implements a multi-tenant backend platform for games. Each game has its own economy, inventory, progression, and validation rules while leveraging a shared set of backend services and infrastructure. Games integrate through an SDK, enabling them to reuse common functionality while remaining logically isolated.

> **Status:** Slice 1 in progress — infrastructure, IdentityService, ApiGateway.

## Tech stack
![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet)
![ASP.NET Core](https://img.shields.io/badge/ASP.NET%20Core-Minimal%20APIs-512BD4?logo=dotnet)
![Angular](https://img.shields.io/badge/Angular-20-DD0031?logo=angular)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-17-336791?logo=postgresql)
![RabbitMQ](https://img.shields.io/badge/RabbitMQ-FF6600?logo=rabbitmq)
![Ocelot](https://img.shields.io/badge/Ocelot-API%20Gateway-008080)
![Docker](https://img.shields.io/badge/Docker-2496ED?logo=docker)
![Kubernetes](https://img.shields.io/badge/Kubernetes-326CE5?logo=kubernetes)

## Architecture
[docs/architecture.md](docs/architecture.md).

## Running locally

```
cp infra/.env.example infra/.env
cd infra
docker compose up
```

This brings up Postgres and Consul. IdentityService and ApiGateway join the
stack in later commits, once they exist.

The values in `infra/.env.example` are committed on purpose and are not
production secrets: the stack only binds to `localhost`, so nothing in it is
reachable from outside the machine it runs on, and every clone gets its own
`.env` by copying the example rather than sharing one committed file.

## Architecture decisions
[docs/adr/](docs/adr/).

## Known limitations / what's next