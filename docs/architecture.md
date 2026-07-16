# Architecture

```mermaid
flowchart LR
    Client[Angular WEB-app / Game SDK] --> GW
    Admin[Angular admin panel] --> GW
    GW[ApiGateway<br/>Ocelot + Consul]

    GW --> ID[IdentityService]
    GW -.-> EC[EconomyService]
    GW -.-> INV[InventoryService]
    GW -.-> MP[MarketplaceService]
    GW -.-> VAL[ValidationService]
    GW -.-> NOT[NotificationService]

    ID --> IDDB[(identity_db)]
    EC -.-> ECDB[(economy_db)]

    EC -.-> MQ[(RabbitMQ)]
    MQ -.-> VAL
    W[Platform.Worker<br/>Quartz.NET] -.-> MQ

    classDef planned stroke-dasharray: 4 4;
    class EC,INV,MP,VAL,NOT,ECDB,MQ,W planned
```

Solid lines — implemented. Dashed lines — designed and scheduled for implementation in future iterations.

## Currently implemented
-

## Cross-cutting
- Multi-tenancy: GameId is a first-class property across schemas and events.
- Event bus: RabbitMQ, choreography-based saga
- Observability: correlation ID + Serilog

## Path-filtered CI

## Cleanup jobs (Platform.Worker)