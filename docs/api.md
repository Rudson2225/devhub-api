# API

Base URL: `http://localhost:5XXX/api` (Swagger em `/swagger`).

Todos os endpoints (exceto `/api/auth/*`) exigem `Authorization: Bearer <accessToken>`.

Formato de erro: [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457) `ProblemDetails`
com `errorCode` (e `errors` para validação):

```json
{
  "title": "Field validation failed.",
  "status": 422,
  "errorCode": "validation_error",
  "errors": { "email": ["'Email' não pode ser vazio."] }
}
```

Códigos comuns: `unauthorized` (401), `forbidden` (403), `not_found` (404), `conflict` (409),
`validation_error` (422), `internal_error` (500), `concurrency_conflict` (409),
`duplicate_key` (409 — violação de unicidade no Postgres).

## Autenticação

### POST `/api/auth/register` — público, rate limit 10/min
```json
{ "email": "dev@example.com", "password": "Str0ng!Pass123", "displayName": "Dev" }
```
→ `201` com `{ accessToken, refreshToken, expiresIn, tokenType: "Bearer", user }`
- `409` se o email já existe.

### POST `/api/auth/login`
```json
{ "email": "dev@example.com", "password": "Str0ng!Pass123" }
```
→ `200` com o mesmo payload de `register`. `401` para credenciais inválidas.

### POST `/api/auth/refresh`
```json
{ "refreshToken": "<replace>" }
```
→ `200` com novo par (rotação: o token antigo é invalidado). `401` se inválido, usado, expirado ou revogado.

### POST `/api/auth/logout` — autenticado
```json
{ "refreshToken": "<replace>" }
```
→ `204`. Revoga o refresh token (idempotente).

## Usuário

| Método | Rota | Descrição |
|---|---|---|
| GET | `/api/users/me` | Perfil do usuário autenticado |
| PATCH | `/api/users/me` | `{ displayName?, bio?, avatarUrl? }` → perfil atualizado |
| POST | `/api/users/me/change-password` | `{ currentPassword, newPassword }` → `204`; revoga refresh tokens |

## Projetos

| Método | Rota | Permissão |
|---|---|---|
| POST | `/api/projects` | autenticado (vira `Owner`) |
| GET | `/api/projects?page&pageSize` | membro (`ProjectRead`) |
| GET | `/api/projects/{projectId}` | `ProjectRead` |
| PATCH | `/api/projects/{projectId}` | `ProjectUpdate` — `{ name?, description?, status? }` |
| DELETE | `/api/projects/{projectId}` | `ProjectDelete` |

`ProjectResponse`: `id, name, description, status, ownerId, ownerName, createdAt, updatedAt, memberCount, taskCount`.

## Membros

| Método | Rota | Permissão |
|---|---|---|
| GET | `/api/projects/{projectId}/members` | `ProjectRead` |
| POST | `/api/projects/{projectId}/members` | `MembersManage` — `{ email, role }` (roles `Admin`, `Member`, `Viewer`; `Owner` não pode ser atribuído) |
| PATCH | `/api/projects/{projectId}/members/{userId}` | `MembersManage` — `{ role }` (Owner imutável) |
| DELETE | `/api/projects/{projectId}/members/{userId}` | `MembersManage` (Owner não pode ser removido) |

O `GET` devolve a lista completa (nome, papel, data de entrada) para qualquer membro, mas o campo `email` só é preenchido para quem tem `MembersManage` e para a própria linha do pedido; nos restantes casos chega a `null`.

## Tarefas

| Método | Rota | Permissão |
|---|---|---|
| POST | `/api/projects/{projectId}/tasks` | `TasksCreate` — `{ title, description?, status?, priority?, assigneeId?, dueDate?, tagIds? }` |
| GET | `/api/projects/{projectId}/tasks` | `TasksRead` — filtros `search, status, priority, assigneeId, tagId, sortBy, sortOrder, page, pageSize` |
| GET | `/api/projects/{projectId}/tasks/board` | `TasksRead` — `TaskKanbanResponse` agrupado por status |
| GET | `/api/tasks/{id}` | `TasksRead` |
| PATCH | `/api/tasks/{id}` | `TasksUpdate` |
| DELETE | `/api/tasks/{id}` | `TasksDelete` |

Status: `Backlog, Todo, InProgress, Review, Done`. Prioridade: `Low, Medium, High, Critical`.
Concluir uma tarefa (status `Done`) grava `completedAt` e registra atividade `TaskCompleted`.

## Comentários

| Método | Rota | Permissão |
|---|---|---|
| GET | `/api/tasks/{taskId}/comments` | `CommentsRead` |
| POST | `/api/tasks/{taskId}/comments` | `CommentsCreate` — `{ content }` |
| PATCH | `/api/comments/{id}` | autor: `CommentsUpdateOwn`; outro: `CommentsUpdateAny` |
| DELETE | `/api/comments/{id}` | autor: `CommentsDeleteOwn`; outro: `CommentsDeleteAny` |

## Tags

| Método | Rota | Permissão |
|---|---|---|
| GET | `/api/projects/{projectId}/tags` | `ProjectRead` |
| POST | `/api/projects/{projectId}/tags` | `TagsManage` — `{ name, description?, color? }` |
| PATCH | `/api/projects/{projectId}/tags/{tagId}` | `TagsManage` |
| DELETE | `/api/projects/{projectId}/tags/{tagId}` | `TagsManage` |

Tags globais (ex.: `bug`, `feature`) nascem no seed; tags criadas pela API são escopo do projeto.

## Documentos

| Método | Rota | Permissão |
|---|---|---|
| GET | `/api/projects/{projectId}/documents` | `DocumentsRead` |
| POST | `/api/projects/{projectId}/documents` | `DocumentsManage` — `{ title, content?, type? }` |
| GET | `/api/documents/{id}` | `DocumentsRead` |
| PATCH | `/api/documents/{id}` | `DocumentsManage` |
| DELETE | `/api/documents/{id}` | `DocumentsManage` |

Tipos (`DocumentType`): `Other, Readme, Api, Architecture, Setup, Database, Deployment`.

## Atividade e Dashboard

- `GET /api/projects/{projectId}/activities?page&pageSize` (`ActivitiesRead`) — trilha do projeto (paginada, mais recentes primeiro).
- `GET /api/projects/{projectId}/dashboard` (`DashboardRead`) — `{ projectId, totalTasks, completedTasks, pendingTasks, criticalTasks, tasksByStatus[], tasksByPriority[], totalMembers, recentActivities[] }`.

## Notificações

- `GET /api/notifications?page&pageSize` → `{ items[], page, pageSize, totalItems, totalPages, unreadCount }`
- `GET /api/notifications/unread-count` → `{ count }`
- `POST /api/notifications/{id}/read` → notificação atualizada
- `POST /api/notifications/read-all` → `204`

Tipos: `TaskAssigned, TaskStatusChanged, TaskComment, MemberAdded, RoleChanged`.

## Health

- `GET /health` → `{ status, code }` (sempre `Healthy` se a app está no ar)
- `GET /health/ready` → `{ status, code, details: { database: "Healthy|Unhealthy" } }` (readiness usa o DbContext)

## Rate limiting

`/api/auth/register|login|refresh` → 10 requisições/minuto por IP → `429 Too Many Requests` (ajustável via `RateLimit:AuthPolicy:PermitLimit`, com padrão `10`).