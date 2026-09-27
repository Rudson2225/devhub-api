# DevHub API

Backend em **.NET 10 / ASP.NET Core** para uma plataforma de gerenciamento de projetos para desenvolvedores: projetos, equipes, tarefas, comentários, tags, documentação, atividade e notificações.

## Stack

- .NET 10 (SDK 10.0.401), ASP.NET Core Web API (controllers)
- PostgreSQL + Entity Framework Core (Npgsql), enums gravados como `text`
- Autenticação **JWT Bearer** (access token curto) + **refresh token** rotativo (hash SHA-256 no banco)
- Autorização por **permissões por projeto** (papéis `Owner`, `Admin`, `Member`, `Viewer`)
- Validação com **FluentValidation** (422 + ProblemDetails)
- **Serilog** (console compacto), health checks (`/health`, `/health/ready`), rate limiting em `/api/auth/*`, CORS
- Testes: xUnit + FluentAssertions (unitários) e WebApplicationFactory + PostgreSQL (integração)

## Arquitetura (Clean Architecture)

| Projeto | Responsabilidade |
|---|---|
| `src/DevHub.Domain` | Entidades, enums, catálogo de permissões — sem dependências |
| `src/DevHub.Application` | Casos de uso (services), DTOs, validators, interfaces de repositório, exceções | 
| `src/DevHub.Infrastructure` | EF Core (DbContext, configurações, migrações), repositórios, seed, autenticação |
| `src/DevHub.Api` | Controllers, middleware, `Program.cs`, configuração |
| `tests/DevHub.UnitTests` | Testes unitários da camada de aplicação |
| `tests/DevHub.IntegrationTests` | Testes de API contra PostgreSQL |

## Requisitos

- [.NET SDK 10](https://dotnet.microsoft.com/download)
- PostgreSQL 14+ local (ou use o docker-compose fornecido)

> Sem acesso root, o PostgreSQL 18 do sistema roda com o cluster de dados em user-space
> (`/tmp/opencode/devhub-pgdata`) em `127.0.0.1:55432`, user `devhub`, trust. Os binários estão em
> `/usr/lib/postgresql/18/bin`. A string de conexão precisa da porta. Ver `docs/development.md`.

## Como rodar

```bash
# 1. Configurar variáveis (ver .env.example)
export Jwt__SecretKey="troque-por-um-segredo-de-pelo-menos-32-bytes"
export ConnectionStrings__Default="Host=127.0.0.1;Port=55432;Database=devhub;Username=devhub;Password="

# 2. Criar o banco (uma vez)
createdb -h 127.0.0.1 -p 55432 -U devhub devhub     # se não existir

# 3. Aplicar migrações
dotnet tool restore
dotnet tool run dotnet-ef database update \
  --project src/DevHub.Infrastructure --startup-project src/DevHub.Api

# 4. Rodar (migrations + seed automáticos em Development via appsettings.Development.json)
dotnet run --project src/DevHub.Api
```

Endpoints úteis:
- Swagger: `http://localhost:5XXX/swagger`
- Health: `http://localhost:5XXX/health` e `/health/ready`

> **Atenção**: em `appsettings.Development.json` o seed cria um admin de desenvolvimento
> (`admin@devhub.local` / `ChangeMe123!`). Nunca ative `Seed__CreateAdminUser` em produção
> e sempre troque o segredo JWT por um valor de produção.

## Autenticação

1. `POST /api/auth/register` ou `POST /api/auth/login` → `{ accessToken, refreshToken, ... }`
2. Envie `Authorization: Bearer <accessToken>` nas demais rotas.
3. Quando o access token expirar, `POST /api/auth/refresh` com o refresh token (rotativo: o token antigo é invalidado).
4. `POST /api/auth/logout` revoga o refresh token.

## Endpoints principais

| Rota | Descrição |
|---|---|
| `POST /api/auth/register`, `login`, `refresh`, `logout` | Autenticação |
| `GET/PATCH /api/users/me` | Perfil do usuário logado |
| `POST /api/projects` · `GET /api/projects` · `GET/PATCH/DELETE /api/projects/{projectId}` | Projetos |
| `POST /api/projects/{projectId}/members` · `GET` · `PATCH/DELETE /members/{userId}` | Membros e papéis |
| `POST /api/projects/{projectId}/tasks` · `GET` · `GET /tasks/board` | Tarefas (lista filtrada + kanban) |
| `GET/PATCH/DELETE /api/tasks/{id}` · `GET/POST /api/tasks/{id}/comments` | Tarefas e comentários |
| `GET/POST /api/projects/{projectId}/tags` · `PATCH/DELETE /tags/{tagId}` | Tags |
| `GET/POST /api/projects/{projectId}/documents` · `GET/PATCH/DELETE /api/documents/{id}` | Documentação |
| `GET /api/projects/{projectId}/activities` | Atividade do projeto |
| `GET /api/projects/{projectId}/dashboard` | Métricas (kanban, contadores, prioridades) |
| `GET /api/notifications` · `unread-count` · `POST {id}/read` · `POST read-all` | Notificações |
| `GET /health` · `GET /health/ready` | Health checks |

## Testes

```bash
dotnet test DevHub.slnx
```

- **Unitários** (`DevHub.UnitTests`): validators, `AuthService` (registo/login/refresh/rotação/caducidade), `ProjectAccessService` e proteções de `ProjectMemberService`.
- **Integração** (`DevHub.IntegrationTests`): preferem o PostgreSQL local (`devhub_test`, recriado a cada execução); se indisponível, a suíte documenta a falha de conexão.

## Docker (NÃO TESTADO neste ambiente — sem Docker disponível)

```bash
docker compose -f docker/docker-compose.yml up --build
```

Containers: `devhub-postgres` (PostgreSQL, banco `devhub`) e `devhub-api` (API, migra + seed em Development). Ver `docker/README.md`.

## CI

`.github/workflows/ci.yml`: build, testes unitários + integração (PostgreSQL service container), `dotnet format --verify-no-changes`. NÃO executado localmente (sem acesso ao GitHub).

## Documentação

- `docs/architecture.md` — decisões de arquitetura e modelo de dados
- `docs/api.md` — referencia detalhada da API (rotas, exemplos, códigos de erro)
- `docs/development.md` — ambiente local (PostgreSQL user-space, migrações, testes)