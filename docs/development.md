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

PostgreSQL 18 dos binários do sistema, com o cluster de dados em user-space e rodando em
`127.0.0.1:55432` (porta diferente da padrão para não colidir com o cluster do sistema),
usuário `devhub`, trust auth, bancos `devhub` e `devhub_test`:

```bash
export PGBIN=/usr/lib/postgresql/18/bin
export PATH="$PGBIN:$PATH"
```

O diretório de dados fica em `/tmp/opencode/devhub-pgdata`. A porta vem do arranque, não do
`postgresql.conf` (que continua com o padrão comentado em 5432):

```bash
# iniciar / reiniciar o cluster
/usr/lib/postgresql/18/bin/pg_ctl -D /tmp/opencode/devhub-pgdata \
  -l /tmp/opencode/devhub-pg.log \
  -o "-p 55432 -h 127.0.0.1 -k /tmp/opencode/devhub-pgdata" start
```

- Verificar: `pg_isready -h 127.0.0.1 -p 55432 -U devhub`
- Criar banco: `createdb -h 127.0.0.1 -p 55432 -U devhub devhub`
- CLI: `psql -h 127.0.0.1 -p 55432 -U devhub -d devhub`

> A string de conexão em `appsettings.json` não tem porta, ou seja, assume 5432. Contra este
> cluster é preciso exportá-la com a porta antes de rodar a API, os testes ou o EF:
>
> ```bash
> export ConnectionStrings__Default="Host=127.0.0.1;Port=55432;Database=devhub;Username=devhub;Password="
> ```

## Migrações

```bash
dotnet tool run dotnet-ef migrations add InitialCreate \
  --project src/DevHub.Infrastructure --startup-project src/DevHub.Api

dotnet tool run dotnet-ef database update \
  --project src/DevHub.Infrastructure --startup-project src/DevHub.Api
```

> O startup da API exige `Jwt__SecretKey`; ao executar comandos do EF a partir desta máquina,
> exporte um valor de desenvolvimento primeiro (ex.: `export Jwt__SecretKey="dev~key~32bytes~min"`).
> O EF usa a mesma `ConnectionStrings__Default` da API — veja a seção do PostgreSQL para a porta.
>
> Sem `--configuration Release` o EF carrega a saída **Debug**, e com `--no-build` ele nem recompila:
> as duas opções juntas podem validar contra um assembly antigo e dar um resultado falso (uma migração
> recém-criada simplesmente "não existe" para o comando). Depois de mexer no modelo, confira com
> `dotnet ef migrations list` o que o EF enxerga antes de confiar no resultado de qualquer outro comando.

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
  A string usada é a de `ConnectionStrings__Default`, então aponte para a porta do seu cluster:

  ```bash
  export ConnectionStrings__Default="Host=127.0.0.1;Port=55432;Database=devhub_test;Username=devhub;Password="
  dotnet test DevHub.slnx
  ```