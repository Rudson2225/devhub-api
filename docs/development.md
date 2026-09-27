# Ambiente de desenvolvimento

Ambiente real usado neste repositório (Ubuntu 26.04, usuário sem sudo):

## .NET

SDK instalado em user-space:

```bash
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.dotnet:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
```

Ferramentas locais (manifesto em `.config/dotnet-tools.json`):

```bash
dotnet tool restore
dotnet tool run dotnet-ef --version   # 10.0.x
```

## PostgreSQL em user-space (sem root)

Servidor PostgreSQL 18 extraído localmente e rodando em `127.0.0.1:5432`
(usuário `devhub`, trust auth, bancos `devhub` e `devhub_test`):

```bash
export PGBIN=/tmp/opencode/pg/usr/lib/postgresql/18/bin
export PATH="$PGBIN:$PATH"
export LD_LIBRARY_PATH=/tmp/opencode/pg/usr/lib/x86_64-linux-gnu
```

- Verificar: `pg_isready -h 127.0.0.1 -p 5432 -U devhub`
- Criar banco: `createdb -h 127.0.0.1 -U devhub devhub`
- CLI: `psql -h 127.0.0.1 -U devhub -d devhub`

## Migrações

```bash
dotnet tool run dotnet-ef migrations add InitialCreate \
  --project src/DevHub.Infrastructure --startup-project src/DevHub.Api

dotnet tool run dotnet-ef database update \
  --project src/DevHub.Infrastructure --startup-project src/DevHub.Api
```

> O startup da API exige `Jwt__SecretKey`; ao executar comandos do EF a partir desta máquina,
> exporte um valor de desenvolvimento primeiro (ex.: `export Jwt__SecretKey="dev~key~32bytes~min"`).

As migrações são aplicadas automaticamente no startup quando `Database__AutoMigrate=true`
(Development via `appsettings.Development.json`, e nos testes de integração).

## Rodar a API

```bash
export Jwt__SecretKey="dev-only-secret-com-pelo-menos-32-bytes"
export ConnectionStrings__Default="Host=127.0.0.1;Database=devhub;Username=devhub;Password="
export ASPNETCORE_ENVIRONMENT=Development
dotnet run --project src/DevHub.Api
```

- Swagger: `http://localhost:5XXX/swagger`
- Health: `http://localhost:5XXX/health`

Em Development o seed cria `admin@devhub.local` / `ChangeMe123!`.

## Testes

```bash
dotnet test DevHub.slnx
```

- Unitários: sem dependências externas.
- Integração: exigem o PostgreSQL local com o banco `devhub_test` (recriado a cada execução).
  Se o banco não estiver disponível, a suíte falha sinalizando a conexão — rode novamente com o PG ativo.