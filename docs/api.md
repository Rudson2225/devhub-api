# API

Base URL: `http://localhost:5XXX/api` (Swagger em `/swagger`).

Todos os endpoints exigem `Authorization: Bearer <accessToken>`, exceto
`POST /api/auth/register`, `POST /api/auth/login` e `POST /api/auth/refresh`.
Entre os endpoints de `/api/auth/*`, só o `logout` exige token.

Formato de erro: [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457) `ProblemDetails`
com `errorCode`, `traceId` e `errors` para validação de domínio:

```json
{
  "title": "One or more validation errors occurred.",
  "status": 422,
  "errorCode": "validation_failed",
  "traceId": "0HN7...",
  "errors": { "email": ["'Email' não pode ser vazio."] }
}
```

Códigos comuns: `unauthorized` (401), `forbidden` (403), `not_found` (404), `bad_request` (400),
`conflict` (409), `validation_failed` (422), `internal_error` (500), `concurrency_conflict` (409),
`duplicate_key` (409 — violação de unicidade no Postgres), `foreign_key_violation` (409).

`403` significa que o recurso existe e o chamador autenticado não tem a permissão necessária;
`404` significa que o recurso não existe. Ou seja, um `GET` em um projeto de que o chamador não é
membro responde `403`, e é essa resposta que confirma a existência do projeto. Ver a justificativa
em `docs/architecture.md`.

Erros de **model binding** (JSON malformado, tipo inválido) não passam pelo handler de
exceções: saem como `400` do MVC, com o formato padrão de `ValidationProblemDetails` e sem
`errorCode`. Nenhum SQL, schema, connection string ou stack trace chega ao cliente.

## Autenticação

`register`, `login` e `refresh` são públicos e sujeitos a **dois** limites independentes, ambos
em janela fixa de 60 s e ambos configuráveis em `RateLimit:AuthPolicy`:

| Orçamento | Padrão | Chave |
|---|---|---|
| Por endereço de cliente | 10 req/min (`PermitLimit`) | IP |
| Por conta submetida | 20 req/min (`AccountPermitLimit`) | e-mail do corpo da requisição |

O limite por conta é o que impede *account spraying* de prosseguir ao trocar de IP; o por
endereço protege o servidor de abuso. Exceder qualquer um dos dois devolve `429`.

### POST `/api/auth/register` — público
```json
{ "email": "dev@example.com", "password": "Str0ng!Pass123", "displayName": "Dev" }
```
→ `201` com `{ accessToken, refreshToken, expiresIn, tokenType: "Bearer", user }`
- `Location` aponta para `/api/users/me`, que devolve o perfil criado num `GET`.
- `409` se o email já existe.

### POST `/api/auth/login` — público
```json
{ "email": "dev@example.com", "password": "Str0ng!Pass123" }
```
→ `200` com o mesmo payload de `register`. `401` para credenciais inválidas.

### POST `/api/auth/refresh` — público
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

`GET /board` devolve no máximo **100 tarefas por coluna** para não carregar o quadro inteiro.
O corte é silencioso: quando um status tem mais de 100 tarefas, a resposta traz as 100 primeiras
e nada sinaliza que há mais. Clientes que precisam do total devem usar a listagem paginada.

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

Uma tag escopada a outro projeto não pode ser aplicada a uma tarefa: o `PATCH` de tarefa
responde `404`, o mesmo que daria para um id inexistente. Tags globais podem ser usadas em
qualquer projeto, sendo então associadas a ele na primeira vez que são usadas.

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
- `tasksByStatus` e `tasksByPriority` só incluem os valores que têm tarefas: um status sem
  tarefas não aparece no array, e não vem com `count: 0`. Quem quiser os cinco status
  fixos precisa completá-los no cliente.

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

`/api/auth/register|login|refresh` são limitados por dois orçamentos independentes
(`RateLimit:AuthPolicy`): **10 req/min por IP** (`PermitLimit`) e **20 req/min por conta
submetida** (`AccountPermitLimit`), ambos em janela fixa de 60 s (`WindowSeconds`).
Exceder qualquer um dos dois → `429 Too Many Requests`.

O limite por conta conta o e-mail do corpo da requisição, não o do token, porque é justamente
o que limita adivinhação de senha e *spraying* quando o atacante varia o IP de origem.