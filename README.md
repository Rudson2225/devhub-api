# DevHub API

Backend em **.NET 10 / ASP.NET Core** para gerenciamento de projetos de software: o espaço onde uma
equipe registra o que precisa ser feito, quem faz o quê, e o histórico do que já foi entregue.

## O problema que resolve

Equipes de software costumam espalhar o trabalho por ferramentas desconectadas: um quadro de tarefas
num lugar, a documentação em outro, a conversa em um terceiro, e nada disso ligado a quem fez o quê.
O resultado é contexto perdido — não se sabe quem é responsável por quê, o que já foi decidido, nem
por que uma tarefa existe.

O DevHub modela **o projeto como a unidade de autorização e de contexto**. Tudo — tarefa, comentário,
documento, tag, notificação, trilha de atividade — pertence a um projeto, e o acesso a esse projeto é
governado por papéis. Isso resolve três problemas de forma estrutural, em vez de por convenção:

- **Permissões por projeto, não por aplicação.** Um mesmo usuário é `Owner` em um projeto, `Viewer`
  em outro e não tem acesso a um terceiro. Não existe "administrador global": quem enxerga o quê é
  resolvido por projeto, e a checagem acontece na borda de cada operação.
- **Rastreabilidade.** Concluir uma tarefa, mudar um papel, editar um documento gera um registro
  append-only na atividade do projeto. O histórico não é reescrevível.
- **Documentação no mesmo lugar da decisão.** Um documento técnico é um recurso do projeto, com a
  mesma permissão de leitura que o restante, e versionado junto com o trabalho que ele explica.

## Funcionalidades

| Área | O que existe |
|---|---|
| **Autenticação** | Registro, login, access token JWT curto, refresh token rotativo com detecção de reuso, revogação no logout e em troca de senha |
| **Projetos** | CRUD, estados, listagem paginada e ordenável |
| **Membros e papéis** | Convite por e-mail, `Owner`/`Admin`/`Member`/`Viewer`, alteração de papel, remoção — com proteção do papel `Owner` |
| **Tarefas** | CRUD, status e prioridade, responsável, prazo, filtros, ordenação, paginação e visão Kanban |
| **Tags** | Taxonomia por projeto, com um conjunto global de tags compartilhado, e unicidade por escopo |
| **Comentários** | Thread por tarefa, com edição pelo autor e moderação por permissão |
| **Documentos** | Documentos técnicos por projeto, tipados |
| **Atividade** | Trilha de auditoria append-only por projeto, paginada |
| **Dashboard** | Métricas de tarefas por status e prioridade, contadores e atividade recente |
| **Notificações** | Notificações por usuário, contador de não lidas, marcar uma ou todas como lidas |
| **Operação** | Health checks, rate limiting em `/api/auth/*`, OpenAPI/Swagger, ProblemDetails RFC 9457 |

## Stack

- .NET 10 (SDK 10.0.401) e ASP.NET Core Web API (controllers)
- PostgreSQL 18 + Entity Framework Core (Npgsql), enums gravados como `text`
- Autenticação JWT Bearer (HS256) com refresh token rotativo armazenado como hash SHA-256
- Autorização por permissões por projeto (papéis `Owner`, `Admin`, `Member`, `Viewer`)
- Validação com FluentValidation, erros em `ProblemDetails` (RFC 9457)
- Serilog em console com formato compacto, health checks, rate limiting e CORS
- xUnit, FluentAssertions e `WebApplicationFactory` para os testes

## Arquitetura

Clean Architecture em quatro camadas, com dependências sempre apontando para dentro:

```
DevHub.Domain  <----  DevHub.Application  <----  DevHub.Infrastructure  <----  DevHub.Api
    ^                    ^                         ^
    |                    |                         |
    +--------------------+-------------------------+  (referências sempre internas)
```

### Estrutura de projetos

| Projeto | Responsabilidade |
|---|---|
| `src/DevHub.Domain` | Entidades, enums, catálogo de permissões — sem dependências externas |
| `src/DevHub.Application` | Casos de uso, DTOs, validators, interfaces de repositório, exceções de domínio da aplicação |
| `src/DevHub.Infrastructure` | EF Core (DbContext, configurações, migrações), repositórios, seed, autenticação |
| `src/DevHub.Api` | Controllers, middleware, composição em `Program.cs` |
| `tests/DevHub.UnitTests` | Testes unitários da camada de aplicação |
| `tests/DevHub.IntegrationTests` | Testes da API real contra PostgreSQL |

O desenho completo, o modelo de dados e as decisões de contrato estão em
[`docs/architecture.md`](docs/architecture.md).

## Requisitos

- [.NET SDK 10](https://dotnet.microsoft.com/download)
- PostgreSQL 14 ou superior — local ou via Docker

## Como executar

### Opção A — Docker (tudo junto)

```bash
export Jwt__SecretKey="troque-por-um-segredo-de-pelo-menos-32-bytes"
docker compose -f docker/docker-compose.yml up --build
```

Sobe o PostgreSQL e a API já migrada e populada. A API responde em `http://localhost:8080`, com
Swagger em `/swagger` e o admin de desenvolvimento `admin@devhub.local` / `ChangeMe123!`.
Detalhes em [`docker/README.md`](docker/README.md).

> As credenciais padrão do compose existem só para desenvolvimento local. Em qualquer ambiente real,
> defina `Jwt__SecretKey` e `Seed__AdminPassword` e nunca habilite `Seed__CreateAdminUser`.

### Opção B — PostgreSQL local

```bash
# 1. Banco de dados (ajuste usuário/senha conforme sua instalação)
createdb -h 127.0.0.1 -U devhub devhub

# 2. Segredo do JWT — a aplicação recusa a subida sem um valor de 32 bytes ou mais
export Jwt__SecretKey="$(openssl rand -base64 48)"

# 3. String de conexão
export ConnectionStrings__Default="Host=127.0.0.1;Port=5432;Database=devhub;Username=devhub;Password="

# 4. Restaurar ferramentas locais e aplicar as migrações
dotnet tool restore
dotnet tool run dotnet-ef database update \
  --project src/DevHub.Infrastructure --startup-project src/DevHub.Api

# 5. Rodar
dotnet run --project src/DevHub.Api
```

Em Development as migrações e o seed são aplicados automaticamente no startup
(`Database__AutoMigrate=true`), então o passo 4 é opcional para a primeira execução.

- Swagger: `http://localhost:5XXX/swagger`
- Health: `http://localhost:5XXX/health` e `/health/ready`

Todas as variáveis de ambiente estão documentadas em [`.env.example`](.env.example).
Se o seu PostgreSQL não estiver na porta padrão, veja a seção de banco de dados em
[`docs/development.md`](docs/development.md).

## Migrações

As migrações do EF Core ficam em `src/DevHub.Infrastructure/Migrations` e são aplicadas por
`dotnet tool run dotnet-ef`. O comando do EF **não** usa a `ConnectionStrings__Default` da API: em
tempo de design ele passa por `DesignTimeDbContextFactory`, que lê `DEVHUB_CONNECTIONSTRING` ou as
variáveis `DEVHUB_DB_*`. A seção de migrações em
[`docs/development.md`](docs/development.md) traz os comandos completos e a armadilha do
`--configuration`.

Para começar uma nova migração:

```bash
dotnet tool run dotnet-ef migrations add NomeDaMudanca \
  --project src/DevHub.Infrastructure --startup-project src/DevHub.Api \
  --configuration Release
```

## Autenticação

1. `POST /api/auth/register` ou `POST /api/auth/login` devolve `{ accessToken, refreshToken, ... }`.
2. Envie `Authorization: Bearer <accessToken>` nas demais rotas.
3. Access token expirado? `POST /api/auth/refresh` com o refresh token. A rotação invalida o token
   antigo: reenviar um token já usado é tratado como possível roubo, e todas as sessões da conta são
   revogadas.
4. `POST /api/auth/logout` revoga o refresh token.

Os endpoints públicos de autenticação têm dois limites de taxa independentes, por endereço de
cliente e por conta submetida, para conter adivinhação de senha e *account spraying*.

## Testes

```bash
export ConnectionStrings__Default="Host=127.0.0.1;Port=5432;Database=devhub_test;Username=devhub;Password="
dotnet test DevHub.slnx -c Release
```

- **Unitários** (`DevHub.UnitTests`): validators, `AuthService` (registro, login, rotação, expiração),
  `ProjectAccessService` e as proteções de dono em `ProjectMemberService`.
- **Integração** (`DevHub.IntegrationTests`): sobem a API real contra um PostgreSQL de verdade. O banco
  `devhub_test` é recriado a cada execução, com migrações e seed aplicados do zero — por isso a
  suíte também exercita a cadeia de migrações. Requer um PostgreSQL acessível; sem ele, os testes
  falham na conexão.

Suíte atual: **52 testes unitários e 105 de integração**, todos passando.

## Documentação

| Documento | Conteúdo |
|---|---|
| [`docs/architecture.md`](docs/architecture.md) | Camadas, modelo de dados, regras de negócio, segurança e decisões de contrato |
| [`docs/api.md`](docs/api.md) | Rotas, exemplos de payload, códigos de erro e rate limiting |
| [`docs/development.md`](docs/development.md) | Ambiente local, PostgreSQL em porta não padrão, migrações, testes |
| [`docker/README.md`](docker/README.md) | Execução via Docker Compose |

A especificação OpenAPI é gerada em tempo de execução e fica disponível em `/swagger` (Development).

## Observações de honestidade

- A suíte de integração e as migrações foram validadas localmente contra PostgreSQL 18 em
  `127.0.0.1`, com round-trip de migração (apply → rollback → apply).
- O `docker-compose.yml` está incluído e é coerente com a configuração da aplicação, mas **não foi
  executado neste ambiente**, que não tem Docker disponível.
- O workflow em `.github/workflows/ci.yml` roda build, testes e verificação de formatação com um
  PostgreSQL de serviço, mas **não foi executado localmente** (requer GitHub Actions).
