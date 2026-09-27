# Arquitetura

## Visão geral

- **Clean Architecture** em 4 camadas + 2 projetos de teste, com dependências sempre na direção do domínio.
- **API**: ASP.NET Core controllers com rotas REST, `ProblemDetails` para erros, FVS + serilog.
- **Persistence**: Entity Framework Core sobre PostgreSQL. Enums gravados como `text` (legibilidade e portabilidade).

```
DevHub.Domain  <----  DevHub.Application  <----  DevHub.Infrastructure  <----  DevHub.Api
    ^                    ^                         ^
    |                    |                         |
    +--------------------+-------------------------+  (referências sempre internas)
```

## Domain

- Entidades em `src/DevHub.Domain/Entities`; enums em `src/DevHub.Domain/Enums`.
- `BaseEntity`: `Id = Guid.CreateVersion7()` (ordenável, tempo-embutido) + `CreatedAt`/`UpdatedAt` auditados via SaveChanges.
- `Permissions.cs`: catálogo estável de permissões (ex.: `ProjectRead`, `TasksManage`, `CommentsUpdateOwn`).
- Papéis por projeto: `Owner`, `Admin`, `Member`, `Viewer`, com permissões via `RolePermissions`.

## Application

- **Services** (`ITaskService`, `IAuthService`, ...) implementam os casos de uso. Cada um valida com FluentValidation (via `IValidator<T>`), consulta pelos repositórios e persiste por `IUnitOfWork`.
- **Repositórios**: interfaces por agregado (`IProjectRepository`, `ITaskRepository`, `ITagRepository`, `IDocumentRepository`, `IActivityRepository`, `INotificationRepository`, ...).
- **Autorização por projeto**: `IProjectAccessService.EnsurePermissionAsync(projectId, permission, userId)` resolve o papel do usuário e seus permissions (membro Owner/Admin/Member definem automaticamente sobre o próprio role + permissões globais via roles globais).
- **Exceções**: `ApiException` e derivadas (`UnauthorizedException`, `ForbiddenException`, `NotFoundException`, `ConflictException`, `BadRequestException`, `ValidationException`) mapeadas para HTTP na API.

## Infrastructure

- `ApplicationDbContext`: convenção de enums→`text`, timestamps automáticos, `UseNpgsql`.
- `Configurations/*`: mapeamento por agregado (12 tabelas + joins `project_tags`, `task_tags`, `role_permissions`).
- `Persistence/Repositories/Repositories.cs`: implementação EF de todos os repositórios (com `Include` para navegações exigidas pelos mappers).
- `Persistence/Seed/DatabaseSeeder.cs`: garante roles+permissions, tags padrão (`backend`, `frontend`, `bug`, `feature`, `urgent`, `documentation`) e cria admin de desenvolvimento se configurado.
- `Authentication`: `PasswordService` (PBKDF2 via `PasswordHasher`), `AccessTokenGenerator` (HS256, claims `sub`, `email`, `jti`, `name`), `RefreshTokenGenerator` (64 entropia, hash SHA-256 para persistir).

## Api

- `Program.cs`: Serilog, `AddApplication`/`AddInfrastructure`, JWT Bearer (sem mapeamento de claims → tipos nativos preservados), Swagger com Bearer, health checks (`/health` liveness, `/health/ready` readiness com check do DbContext), rate limiting para `/api/auth/*` (10 req/min/IP → 429), CORS.
- `Middlewares/`: `GlobalExceptionHandler` (mapeia ApiException e erros de infraestrutura → ProblemDetails; 23505/23503 do Npgsql, concorrência, etc.) e `CurrentUserService` (claims).
- Auto-migrate/seed no startup quando `Database__AutoMigrate=true` (Development e testes).

## Modelo de dados (resumo)

`users` • `refresh_tokens` • `roles` • `permissions` • `role_permissions`
`projects` → `project_members` (users x roles) • `project_tags`
`tasks` (assignee/reporter → users) → `task_comments` • `task_tags`
`tags` (global quando `ProjectId IS NULL`, ou escopo do projeto)
`documents` • `activity_logs` • `notifications`

Regras de negócio importantes:

- Projeto sempre nasce com o criador como `Owner` (não pode ser alterado/removido).
- Membros: `Admin`, `Member`, `Viewer` podem ser adicionados; papéis podem ser alterados (exceto `Owner`).
- Tarefa só pode ser atribuída a um membro do projeto. Tags devem estar associadas ao projeto.
- Comentário: autor edita sem `Manage`, terceiros só com permissões `*Any`.
- Atividade é uma trilha de auditoria imutável (append-only) por projeto.
- Notificações são por usuário; mark-all-read usa `ExecuteUpdate` (sem trazer linhas).

## Segurança

- JWT HS256 com segredo ≥ 32 bytes (validado no startup — falha rápida se ausente/curto).
- `ClockSkew` de 30 s; refresh tokens: rotação + detecção de reuso (token usado é rejeitado), revogação no logout e rejeição de expirados.
- Secreto nunca em `appsettings.json`; somente variáveis de ambiente / user secrets.
- ProblemDetails não expõe stack trace.