# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Pena_e_Arte.API/Pena_e_Arte.API.csproj", "Pena_e_Arte.API/"]
COPY ["Pena_e_Arte.Application/Pena_e_Arte.Application.csproj", "Pena_e_Arte.Application/"]
COPY ["Pena_e_Arte.Infrastructure/Pena_e_Arte.Infrastructure.csproj", "Pena_e_Arte.Infrastructure/"]
COPY ["Pena_e_Arte.Contracts/Pena_e_Arte.Contracts.csproj", "Pena_e_Arte.Contracts/"]
COPY ["Pena_e_Arte.Domain/Pena_e_Arte.Domain.csproj", "Pena_e_Arte.Domain/"]
# CI-VERIFICATION-DELIBERATE-BREAKAGE-DO-NOT-MERGE
COPY ["Pena_e_Arte.API/this-file-does-not-exist.csproj", "Pena_e_Arte.API/"]
RUN dotnet restore "Pena_e_Arte.API/Pena_e_Arte.API.csproj"

COPY Pena_e_Arte.API/ Pena_e_Arte.API/
COPY Pena_e_Arte.Application/ Pena_e_Arte.Application/
COPY Pena_e_Arte.Infrastructure/ Pena_e_Arte.Infrastructure/
COPY Pena_e_Arte.Contracts/ Pena_e_Arte.Contracts/
COPY Pena_e_Arte.Domain/ Pena_e_Arte.Domain/

RUN dotnet publish "Pena_e_Arte.API/Pena_e_Arte.API.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd -r appuser && useradd -r -g appuser appuser
COPY --from=build /app/publish .
USER appuser

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
    CMD ["curl", "-f", "http://localhost:8080/health/live"]

ENTRYPOINT ["dotnet", "Pena_e_Arte.API.dll"]
