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
- **Autorização por projeto**: `IProjectAccessService.EnsurePermissionAsync(projectId, permission, userId)` resolve a `project_members` do usuário, carrega o papel e consulta as permissões em `role_permissions`. Não há role global: a autorização é sempre derivada da associação ao projeto. Falta de associação e permissão insuficiente dão `403` nos dois casos.
- **Exceções**: `ApiException` e derivadas (`UnauthorizedException`, `ForbiddenException`, `NotFoundException`, `ConflictException`, `BadRequestException`, `ValidationException`) mapeadas para HTTP na API.

## Infrastructure

- `ApplicationDbContext`: convenção de enums→`text`, timestamps automáticos, `UseNpgsql`.
- `Configurations/*`: mapeamento por agregado — 15 tabelas no total, sendo 12 de entidade e as 3 de junção `project_tags`, `task_tags`, `role_permissions`.
- `Persistence/Repositories/Repositories.cs`: implementação EF de todos os repositórios (com `Include` para navegações exigidas pelos mappers).
- `Persistence/Seed/DatabaseSeeder.cs`: garante roles+permissions, tags padrão (`backend`, `frontend`, `bug`, `feature`, `urgent`, `documentation`) e cria admin de desenvolvimento se configurado.
- `Authentication`: `PasswordService` (PBKDF2-HMAC-SHA256 com 100.000 iterações, o padrão de `Microsoft.AspNetCore.Identity.PasswordHasher<object>`; o hash é autodescritivo, carrega sal e custo para poder ser reforçado sem invalidar senhas existentes), `AccessTokenGenerator` (HS256, claims `sub`, `email`, `jti`, `name`), `RefreshTokenGenerator` (64 entropia, hash SHA-256 para persistir).

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

### Decisões de contrato

Registradas aqui para que fiquem explícitas em vez de implícitas no código.

**Mantidas como estão (deliberado):**

- **`403` para recurso existente sem permissão, `404` para recurso inexistente.** A regra é
  aplicada em toda a API e está coberta por teste (`Non_member_cannot_access_project`): um `GET`
  em projeto que existe devolve `403` para quem não é membro, e `404` quando o id não existe.
  `PATCH`/`DELETE` checam a permissão antes de carregar, então para não-membros devolvem `403`
  tanto se o projeto existe quanto se não existe. O efeito colateral é que o `GET` confirma a
  existência de um projeto para um usuário autenticado que não é membro. Isso foi considerado e
  aceito: em uma ferramenta de colaboração o usuário precisa saber que existe um projeto do qual
  foi invited mas ainda não tem acesso, e o id é um GUID v7 com 62 bits aleatórios, o que não
  permite enumerar projetos. Fechar o `GET` seria trocar utilidade por um ganho de sigilo pequeno.
- **Truncamento silencioso no Kanban.** `GET /board` corta em 100 tarefas por coluna. O corte
  protects o cliente de carregar um quadro inteiro, e a listagem paginada cobre quem precisa do
  total. Documentado em `docs/api.md`. Se um dia o corte incomodar, o caminho é incluir `total` e um
  sinalizador por coluna — é mudança de contrato, por isso não foi feita.
- **Dashboard sem zeros.** `tasksByStatus`/`tasksByPriority` omitem valores sem tarefas. Mudar para
  sempre devolver todos os enums é mudança de contrato; o formato atual está documentado.
- **Enums em `text`.** Persistir o nome (`Active`, `High`) é legível, mas amarra o valor gravado ao
  identificador do membro do enum: renomear um membro exige migração de dados. Mantido, porque a
  portabilidade entre bancos compensa, e renomeação passa a ser tratada como mudança de schema.

**Corrigida nesta rodada:**

- **`Location` em criações.** A criação de projeto, tarefa e documento já aponta para o recurso
  criado. Membros, tags e comentários apontam para a coleção porque **não existe rota `GET` de
  recurso individual** para eles — a coleção é a única URI canônica disponível. A criação de
  conta, essa sim, apontava para a própria rota `POST /api/auth/register`, que responde `405` a um
  `GET`: um `Location` que não pode ser seguido quebra qualquer cliente que o siga. Passou a
  apontar para `GET /api/users/me`, com teste que percorre o header e exige `200`.
