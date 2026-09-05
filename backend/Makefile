EF_PROJECT = --project src/Namorix.Scout.csproj
EF_STARTUP = --startup-project src/Namorix.Scout.csproj
EF_MIGRATE = dotnet ef migrations add $(EF_PROJECT) $(EF_STARTUP)
EF_UPDATE = dotnet ef database update $(EF_PROJECT) $(EF_STARTUP)
EF_REMOVE = dotnet ef migrations remove $(EF_PROJECT) $(EF_STARTUP)
EF_DROP = dotnet ef database drop $(EF_PROJECT) $(EF_STARTUP)
SERVER_PROJECT = src/Namorix.Scout.csproj

db-init-create:
	$(EF_MIGRATE) InitialCreate

db-migrate:
	$(EF_MIGRATE) $(name)

db-update:
	$(EF_UPDATE)

db-rollback:
	$(EF_REMOVE)

db-remove:
	$(EF_REMOVE)

db-reset:
	$(EF_DROP) --force
	$(EF_UPDATE)

# Run Project
watch:
	dotnet watch run --project $(SERVER_PROJECT)

run:
	dotnet run --project $(SERVER_PROJECT)

# Build
build:
	dotnet build Namorix.Scout.sln

.PHONY: db-init-create db-migrate db-update db-rollback db-remove db-reset watch run build
