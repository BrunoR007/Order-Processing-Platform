FROM mcr.microsoft.com/dotnet/sdk:10.0.401 AS build
ARG PROJECT
WORKDIR /source
COPY . .
RUN test -n "$PROJECT" && dotnet restore "$PROJECT" --locked-mode
RUN dotnet publish "$PROJECT" --configuration Release --no-restore --output /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime
USER root
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
USER $APP_UID
EXPOSE 8080
HEALTHCHECK --interval=10s --timeout=3s --start-period=15s --retries=5 CMD curl --fail --silent http://localhost:8080/health/ready || exit 1
ENTRYPOINT ["dotnet"]
