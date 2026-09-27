# DevHub via Docker

> **Status**: NÃO TESTADO neste ambiente (máquina de desenvolvimento sem Docker/sudo).
> A configuração segue a documentação oficial do .NET 10; valide com um `docker build` antes de usar em produção.

## Uso

```bash
# Opcional: forneça segredos
export Jwt__SecretKey="troque-por-segredo-de-32+bytes"
export Seed__AdminPassword="SenhaSegura!1"

# Subir PostgreSQL + API
docker compose -f docker/docker-compose.yml up --build
```

- API: `http://localhost:8080` (health em `/health`, Swagger em `/swagger`)
- PostgreSQL: `localhost:5432` (user `devhub`, senha `devhub_dev_password`, banco `devhub`)

A API aplica migrações e seeds automaticamente no startup (desenvolvimento).

## Produção

- Desative `Database__AutoMigrate` e `Seed__CreateAdminUser`.
- Use migrations explícitas ou um job de migração dedicado.
- Defina `Jwt__SecretKey`, `ConnectionStrings__Default` e CORS por variáveis de ambiente.
- Rode apenas natureza http em container (terminação TLS + proxy reverso no host).